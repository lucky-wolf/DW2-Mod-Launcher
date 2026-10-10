// The patch being built: a tree of elements with the three patch attributes (id, index, op), merged by identity and
// rendered in the style of the docs (one field per line, small structs inline).

import { escapeAttr, escapeText } from './xml.ts';
import type { XNode } from './xml.ts';

export class PatchError extends Error {}

export interface PatchNode {
  name: string;
  attrs: [string, string][];
  /** Text of a leaf. Undefined or empty renders as a self-closing element. */
  text?: string;
  children: PatchNode[];
  /** A note shown after the element, such as the name of the item an id stands for. */
  comment?: string;
}

const PATCH_ATTRS = ['id', 'index', 'op'];

/** What makes two patch elements "the same one": name, selector and op (plus the content for add / remove-by-value). */
export function identity(n: PatchNode): string {
  const attrs = n.attrs.map(([k, v]) => `${k}=${v}`).join(',');
  const op = n.attrs.find(([k]) => k === 'op')?.[1];
  const content = op === 'add' || op === 'remove' ? '|' + renderNode(withoutComments(n), '', '  ').join('').replace(/\s+/g, '') : '';
  return `${n.name}[${attrs}]${content}`;
}

function withoutComments(n: PatchNode): PatchNode {
  const copy: PatchNode = { name: n.name, attrs: n.attrs, children: n.children.map(withoutComments) };
  if (n.text !== undefined) copy.text = n.text;
  return copy;
}

/** An element of an existing patch file as a PatchNode, so it can be compared with a new one. */
export function fromXml(x: XNode): PatchNode {
  const attrs: [string, string][] = [];
  for (const k of PATCH_ATTRS) if (k in x.attrs) attrs.push([k, x.attrs[k]]);
  const node: PatchNode = { name: x.name, attrs, children: x.children.map(fromXml) };
  if (x.children.length === 0 && x.text !== '') node.text = x.text;
  return node;
}

/** A copy of a data element as plain data (no patch attributes), for add and replace. */
export function literalOf(x: XNode): PatchNode {
  const node: PatchNode = { name: x.name, attrs: [], children: x.children.map(literalOf) };
  if (x.children.length === 0 && x.text !== '') node.text = x.text;
  if (x.comment) node.comment = x.comment;
  return node;
}

/** Puts node into level; when an element with the same identity is already there, merges the children into it. */
export function mergeInto(level: PatchNode[], node: PatchNode): void {
  const id = identity(node);
  const existing = level.find((x) => identity(x) === id);
  if (!existing) {
    level.push(node);
    return;
  }
  for (const c of node.children) mergeInto(existing.children, c);
}

function attrString(n: PatchNode): string {
  return n.attrs.map(([k, v]) => ` ${k}="${escapeAttr(v)}"`).join('');
}

/** An element whose children are all plain fields fits on one line, except the entity itself (the top of the patch). */
export function renderNode(n: PatchNode, indent: string, unit: string, canInline = false): string[] {
  const open = `<${n.name}${attrString(n)}`;
  const note = n.comment ? ` <!-- ${n.comment.replace(/--/g, '- -')} -->` : '';
  if (n.children.length === 0) {
    if (!n.text) return [`${indent}${open}/>${note}`];
    return [`${indent}${open}>${escapeText(n.text)}</${n.name}>${note}`];
  }

  const simple = n.children.every((c) => c.children.length === 0 && c.attrs.length === 0 && !c.comment && !(c.text ?? '').includes('\n'));
  if (canInline && simple) {
    const line = `${indent}${open}>${n.children.map((c) => renderNode(c, '', unit)[0]).join('')}</${n.name}>${note}`;
    if (line.length <= 110) return [line];
  }
  const inner = n.children.flatMap((c) => renderNode(c, indent + unit, unit, true));
  return [`${indent}${open}>${note}`, ...inner, `${indent}</${n.name}>`];
}
