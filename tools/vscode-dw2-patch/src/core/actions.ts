// From a cursor or selection in a data file to the patch the modder most likely wants: finds the element, offers the
// actions that make sense for what it is, and builds the patch tree for the chosen one.

import { childText, commentFor, entityOf, isEntityKey, isLeaf, isUnpatchable, kindOf, listItemName, selectorFor } from './model.ts';
import type { Kind } from './model.ts';
import { ENTITY_KEYS } from './keyMap.ts';
import { PatchError, literalOf, mergeInto } from './patchTree.ts';
import type { PatchNode } from './patchTree.ts';
import type { XNode } from './xml.ts';

export interface PatchAction {
  label: string;
  detail?: string;
  /** When set, the user picks which of these leaf fields to patch before build runs. */
  leafChoices?: XNode[];
  /** When set, build runs on exactly these leaves without asking. */
  fixedLeaves?: XNode[];
  /** Returns the patch as a forest of entity elements. */
  build(leaves: XNode[]): PatchNode[];
}

export interface Analysis {
  root: XNode;
  /** What the caret is on (an entity key counts as the entity). Null for a multi-line selection. */
  target: XNode | null;
  kind: Kind | 'selection';
  /** Human description of the target for prompts and messages. */
  title: string;
  /** First action is the default one. */
  actions: PatchAction[];
}

function lineBounds(src: string, off: number): [number, number] {
  const start = src.lastIndexOf('\n', off - 1) + 1;
  let end = src.indexOf('\n', off);
  if (end < 0) end = src.length;
  return [start, end];
}

/** Deepest element at offset; on whitespace, the element that starts on the same line, else the enclosing one. */
function elementAt(root: XNode, src: string, off: number): XNode {
  let node = root;
  for (;;) {
    const hit = node.children.find((c) => c.openStart <= off && off < c.closeEnd);
    if (!hit) break;
    node = hit;
  }
  const inOwnTag = node !== root && ((off >= node.openStart && off < node.openEnd) || (off >= node.closeStart && off < node.closeEnd));
  if (inOwnTag) return node;
  const [ls, le] = lineBounds(src, off);
  const sameLine = node.children.filter((c) => c.openStart >= ls && c.openStart <= le);
  if (sameLine.length > 0) {
    const before = sameLine.filter((c) => c.openStart <= off);
    return before.length > 0 ? before[before.length - 1] : sameLine[0];
  }
  return node;
}

function leavesWithin(root: XNode, a: number, b: number, out: XNode[] = [], n: XNode = root): XNode[] {
  for (const c of n.children) {
    if (c.closeEnd < a || c.openStart > b) continue;
    if (isLeaf(c)) {
      if (c.openStart >= a && c.closeEnd <= b && !isEntityKey(c, root) && n !== root && !isUnpatchable(c, root)) out.push(c);
    } else {
      leavesWithin(root, a, b, out, c);
    }
  }
  return out;
}

/** Plain fields under t, through structs but not through lists, without an entity's key. */
function collectLeaves(t: XNode, root: XNode, out: XNode[] = []): XNode[] {
  for (const c of t.children) {
    if (isLeaf(c)) {
      if (!isEntityKey(c, root) && !isUnpatchable(c, root)) out.push(c);
    } else if (kindOf(c, root) === 'struct') {
      collectLeaves(c, root, out);
    }
  }
  return out;
}

export function leafLabel(leaf: XNode, base: XNode): string {
  const path: string[] = [];
  for (let a: XNode | null = leaf; a && a !== base; a = a.parent) path.unshift(a.name);
  const value = leaf.text.replace(/\s+/g, ' ').trim();
  return `${path.join(' › ')} = ${value.length > 48 ? value.slice(0, 45) + '…' : value}`;
}

/** Wraps node in the ancestors of el (entity down to el's parent) and merges it into forest. */
function place(root: XNode, el: XNode, self: PatchNode, forest: PatchNode[]): void {
  // The name the data gives an id (<RaceId>0</RaceId> <!-- Human -->) goes after the element that selects by it.
  if (self.comment === undefined && (kindOf(el, root) === 'scalarItem' || self.attrs.some(([k]) => k === 'id'))) {
    self.comment = commentFor(el, root);
  }
  let top = self;
  for (let a = el.parent; a && a !== root; a = a.parent) {
    top = { name: a.name, attrs: selectorFor(a, root), children: [top], comment: commentFor(a, root) };
  }
  mergeInto(forest, top);
}

function setNode(el: XNode, root: XNode): PatchNode {
  const node: PatchNode = { name: el.name, attrs: selectorFor(el, root), children: [] };
  if (el.text !== '') node.text = el.text;
  return node;
}

function withOp(el: XNode, root: XNode, op: string, selector: boolean): PatchNode {
  const node = literalOf(el);
  node.attrs = [...(selector ? selectorFor(el, root) : []), ['op', op]];
  return node;
}

function single(root: XNode, el: XNode, make: () => PatchNode): PatchNode[] {
  const forest: PatchNode[] = [];
  place(root, el, make(), forest);
  return forest;
}

function fieldsAction(t: XNode, root: XNode): PatchAction[] {
  const choices = collectLeaves(t, root);
  if (choices.length === 0) return [];
  return [{
    label: 'Change fields…',
    detail: 'pick which values to put in the patch',
    leafChoices: choices,
    build(leaves) {
      const forest: PatchNode[] = [];
      for (const l of leaves) place(root, l, setNode(l, root), forest);
      return forest;
    },
  }];
}

export function analyse(root: XNode, src: string, selStart: number, selEnd: number): Analysis {
  if (!root.name.startsWith('ArrayOf')) {
    throw new PatchError(`<${root.name}> is not a list of entities (ArrayOf…); patches only apply to such data files.`);
  }

  // A selection that covers several complete fields patches all of them.
  if (selEnd > selStart) {
    const leaves = leavesWithin(root, selStart, selEnd);
    if (leaves.length >= 2) {
      return {
        root, target: null, kind: 'selection',
        title: `${leaves.length} selected fields`,
        actions: [{
          label: `Change the ${leaves.length} selected values`,
          fixedLeaves: leaves,
          build(ls) {
            const forest: PatchNode[] = [];
            for (const l of ls) place(root, l, setNode(l, root), forest);
            return forest;
          },
        }],
      };
    }
  }

  let target = elementAt(root, src, selStart);
  if (target === root || entityOf(target, root) === null) {
    throw new PatchError('Put the cursor inside an entity (on a <Race>, or on one of its fields).');
  }
  if (isEntityKey(target, root)) target = target.parent!;
  if (isUnpatchable(target, root)) {
    throw new PatchError(`<${target.name}> cannot be patched: the game's data types have no such field here, so the launcher would reject the patch.`);
  }
  const kind = kindOf(target, root);
  const t = target;
  const actions: PatchAction[] = [];

  switch (kind) {
    case 'scalar':
      actions.push({
        label: 'Change value',
        detail: t.text.trim(),
        build: () => single(root, t, () => setNode(t, root)),
      });
      actions.push({
        label: 'Remove the field',
        detail: 'back to its default',
        build: () => single(root, t, () => ({ name: t.name, attrs: [['op', 'remove']], children: [] })),
      });
      break;

    case 'scalarItem': {
      const value = t.text;
      actions.push({
        label: 'Change this item',
        detail: `item ${selectorFor(t, root)[0][1]} of the list`,
        build: () => single(root, t, () => setNode(t, root)),
      });
      actions.push({
        label: 'Add an item',
        detail: 'appended to the list',
        build: () => single(root, t, () => ({ name: t.name, attrs: [['op', 'add']], text: value, children: [] })),
      });
      actions.push({
        label: 'Remove this item',
        detail: 'matched by value',
        build: () => single(root, t, () => ({ name: t.name, attrs: [['op', 'remove']], text: value, children: [] })),
      });
      break;
    }

    case 'entity':
      actions.push(...fieldsAction(t, root));
      actions.push({
        label: 'Remove the entity',
        build: () => single(root, t, () => ({ name: t.name, attrs: [...selectorFor(t, root), ['op', 'remove']], children: [] })),
      });
      break;

    case 'struct':
      actions.push(...fieldsAction(t, root));
      actions.push({
        label: 'Replace the whole struct',
        detail: 'a copy to edit; anything you leave out gets its default',
        build: () => single(root, t, () => withOp(t, root, 'replace', true)),
      });
      actions.push({
        label: 'Remove the struct',
        build: () => single(root, t, () => ({ name: t.name, attrs: [['op', 'remove']], children: [] })),
      });
      break;

    case 'structItem':
      actions.push(...fieldsAction(t, root));
      actions.push({
        label: 'Replace this item',
        detail: 'a copy to edit',
        build: () => single(root, t, () => withOp(t, root, 'replace', true)),
      });
      actions.push({
        label: 'Add a new item like this one',
        detail: 'appended to the list, written out in full',
        build: () => single(root, t, () => withOp(t, root, 'add', false)),
      });
      actions.push({
        label: 'Remove this item',
        build: () => single(root, t, () => ({ name: t.name, attrs: [...selectorFor(t, root), ['op', 'remove']], children: [] })),
      });
      break;

    case 'list': {
      const last = t.children[t.children.length - 1];
      const itemName = listItemName(t, root);
      if (last) {
        actions.push({
          label: 'Add an item',
          detail: `a copy of the last <${last.name}> to edit`,
          build: () => single(root, t, () => ({ name: t.name, attrs: [], children: [withOp(last, root, 'add', false)] })),
        });
      } else if (itemName) {
        actions.push({
          label: 'Add an item',
          detail: `an empty <${itemName}> to fill in`,
          build: () => single(root, t, () => ({ name: t.name, attrs: [], children: [{ name: itemName, attrs: [['op', 'add']], children: [] }] })),
        });
      }
      if (last) {
        actions.push({
          label: 'Replace the whole list',
          detail: `${t.children.length} item(s) copied, to edit`,
          build: () => single(root, t, () => withOp(t, root, 'replace', true)),
        });
      }
      actions.push({
        label: 'Remove the list',
        build: () => single(root, t, () => ({ name: t.name, attrs: [['op', 'remove']], children: [] })),
      });
      break;
    }

    default:
      throw new PatchError('Nothing to patch here.');
  }

  return { root, target, kind, title: describeTarget(t, root), actions };
}

function describeTarget(t: XNode, root: XNode): string {
  const e = entityOf(t, root)!;
  const keyField = ENTITY_KEYS.get(e.name);
  const key = keyField ? childText(e, keyField) : undefined;
  const entity = key === undefined ? e.name : `${e.name} id=${key}`;
  if (t === e) return entity;
  const path: string[] = [];
  for (let a: XNode | null = t; a && a !== e; a = a.parent) path.unshift(a.name);
  return `${entity} › ${path.join(' › ')}`;
}
