// What an element of a data file is, judged from the shape of the document itself (the extension has no access to the
// game's type schema): an entity, a struct, a list of items, a plain field, or one item of a list.

import { ENTITY_KEYS, ITEM_KEYS, PRIMITIVE_ITEMS } from './keyMap.ts';
import { PatchError } from './patchTree.ts';
import type { Schema } from './schema.ts';
import type { XNode } from './xml.ts';

export type Kind = 'root' | 'entity' | 'struct' | 'list' | 'scalar' | 'scalarItem' | 'structItem';

export function isLeaf(n: XNode): boolean {
  return n.children.length === 0;
}

/** A list is an element whose children are all the same element; a lone child needs a hint that it is not a struct field. */
export function isListContainer(n: XNode, root: XNode): boolean {
  if (n === root || n.parent === root || isLeaf(n)) return false;
  const first = n.children[0].name;
  if (!n.children.every((c) => c.name === first)) return false;
  if (n.children.length > 1) return true;
  return PRIMITIVE_ITEMS.has(first) || ITEM_KEYS.has(first) || n.name.toLowerCase().startsWith(first.toLowerCase());
}

let schema: Schema | null = null;

/** Use the game's exported schema to classify elements; without one (or for data it does not cover) the shape is guessed. */
export function useSchema(s: Schema | null): void {
  schema = s;
}

export function kindOf(n: XNode, root: XNode): Kind {
  if (n === root) return 'root';
  const known = schema?.resolve(n, root);
  return known ? known.kind : guessKind(n, root);
}

/** Element name of the items of a list, from the schema or else from the items present. */
export function listItemName(list: XNode, root: XNode): string | undefined {
  return schema?.resolve(list, root)?.member?.item ?? list.children[0]?.name;
}

function guessKind(n: XNode, root: XNode): Kind {
  if (n.parent === root) return 'entity';
  const inList = n.parent !== null && isListContainer(n.parent, root);
  if (isLeaf(n)) {
    if (!inList) return 'scalar';
    return PRIMITIVE_ITEMS.has(n.name) ? 'scalarItem' : 'structItem';
  }
  if (inList) return 'structItem';
  return isListContainer(n, root) ? 'list' : 'struct';
}

export function entityOf(n: XNode, root: XNode): XNode | null {
  let a: XNode | null = n;
  while (a && a.parent !== root) a = a.parent;
  return a && a !== root ? a : null;
}

export function childText(n: XNode, name: string): string | undefined {
  const c = n.children.find((x) => x.name === name);
  return c ? c.text.trim() : undefined;
}

export function isEntityKey(n: XNode, root: XNode): boolean {
  return n.parent !== null && n.parent.parent === root && ENTITY_KEYS.get(n.parent.name) === n.name;
}

/** Position of n among its same-named siblings, 1-based: the patch's index="N". */
export function positionOf(n: XNode): number {
  return n.parent!.children.filter((c) => c.name === n.name).indexOf(n) + 1;
}

/**
 * The attributes that select n when a patch addresses it: id for an entity, id (or index when the item has no unique key)
 * for a list item, nothing for a plain field or struct.
 */
export function selectorFor(n: XNode, root: XNode): [string, string][] {
  const kind = kindOf(n, root);
  if (kind === 'entity') {
    const keyField = ENTITY_KEYS.get(n.name);
    if (keyField === undefined) {
      throw new PatchError(`No key is known for <${n.name}> entities, so a patch cannot select one (the key map in the launcher's XmlPatching/KeyMap.cs lacks it).`);
    }
    const key = childText(n, keyField);
    if (key === undefined || key === '') throw new PatchError(`This <${n.name}> has no <${keyField}> to select it by.`);
    return [['id', key]];
  }
  if (kind === 'structItem' || kind === 'scalarItem') {
    const keyField = ITEM_KEYS.get(n.name);
    if (kind === 'structItem' && keyField !== undefined) {
      const key = childText(n, keyField);
      if (key !== undefined && key !== '') {
        const same = n.parent!.children.filter((c) => c.name === n.name && childText(c, keyField) === key);
        if (same.length === 1) return [['id', key]];
      }
    }
    return [['index', String(positionOf(n))]];
  }
  return [];
}

/**
 * The comment that names what selectorFor selects by id: the data writes `<RaceId>0</RaceId> <!-- Human -->`, and the
 * patch gets `<Race id="0"> <!-- Human -->`. An item of a plain-value list keeps its own comment.
 */
export function commentFor(n: XNode, root: XNode): string | undefined {
  const kind = kindOf(n, root);
  if (kind === 'scalarItem') return n.comment || undefined;
  const keyField = kind === 'entity' ? ENTITY_KEYS.get(n.name) : kind === 'structItem' ? ITEM_KEYS.get(n.name) : undefined;
  if (keyField === undefined || selectorFor(n, root)[0]?.[0] !== 'id') return undefined;
  return n.children.find((c) => c.name === keyField)?.comment || undefined;
}

/** The visible name of an entity or item for messages: "Race id=0". */
export function describe(n: XNode, root: XNode): string {
  const sel = selectorFor(n, root).map(([k, v]) => `${k}=${v}`).join(' ');
  return sel ? `${n.name} ${sel}` : n.name;
}

/** True when the schema covers this data file but has no such field, so the launcher would reject a patch for it. */
export function isUnpatchable(n: XNode, root: XNode): boolean {
  return schema !== null && schema.hasRoot(root.name) && schema.resolve(n, root) === null;
}
