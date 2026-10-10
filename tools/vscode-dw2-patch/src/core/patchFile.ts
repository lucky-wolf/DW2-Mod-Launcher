// Adds a patch to a patch file without disturbing what is already there: new elements are inserted at the end of the
// element they belong to, elements the file already has are left alone (and reported), and a missing file is created.

import { PatchError, fromXml, identity, renderNode } from './patchTree.ts';
import type { PatchNode } from './patchTree.ts';
import { parseXml, XmlError } from './xml.ts';
import type { XNode } from './xml.ts';

export interface MergeResult {
  text: string;
  /** Offset in the new text to put the caret at: the first field that was added (or the one already there). */
  focus: number;
  /** Patch elements that were already in the file and so were not added again. */
  existing: string[];
  added: number;
}

interface Edit {
  start: number;
  end: number;
  text: string;
  /** Offset within text of the line to focus, if this edit adds new elements. */
  focusInText: number;
}

function leadingWhitespace(line: string): string {
  return /^[ \t]*/.exec(line)![0];
}

/** The line to put the caret on in an inserted block: the first of the deepest ones, i.e. the changed field. */
function focusLine(lines: string[], eol: string): number {
  let bestIndent = -1;
  let offset = 0;
  let bestOffset = 0;
  for (const l of lines) {
    const w = leadingWhitespace(l).length;
    if (w > bestIndent) {
      bestIndent = w;
      bestOffset = offset + w;
    }
    offset += l.length + eol.length;
  }
  return bestOffset;
}

export function mergeIntoPatchFile(existing: string | null, rootName: string, forest: PatchNode[], unit = '  '): MergeResult {
  if (existing === null) {
    const lines = forest.flatMap((n) => renderNode(n, unit, unit));
    const head = `<${rootName}>\n`;
    return {
      text: `${head}${lines.join('\n')}\n</${rootName}>\n`,
      focus: head.length + focusLine(lines, '\n'),
      existing: [],
      added: forest.length,
    };
  }

  let root: XNode;
  try {
    root = parseXml(existing);
  } catch (e) {
    if (e instanceof XmlError) throw new PatchError(`The patch file is not valid XML (${e.message}); fix it first.`);
    throw e;
  }
  if (root.name !== rootName) {
    throw new PatchError(`The patch file's root is <${root.name}> but this data file's is <${rootName}>; a patch file covers one data file.`);
  }

  const eol = existing.includes('\r\n') ? '\r\n' : '\n';
  const inserts = new Map<XNode, PatchNode[]>();
  const found: string[] = [];
  let focusExisting = -1;

  const plan = (parent: XNode, nodes: PatchNode[], path: string): void => {
    for (const node of nodes) {
      const here = path === '' ? node.name : `${path} › ${node.name}`;
      const match = parent.children.find((c) => identity(fromXml(c)) === identity(node));
      if (!match) {
        const list = inserts.get(parent) ?? [];
        list.push(node);
        inserts.set(parent, list);
      } else if (node.children.length === 0) {
        found.push(here);
        if (focusExisting < 0) focusExisting = match.openStart;
      } else {
        plan(match, node.children, here);
      }
    }
  };
  plan(root, forest, '');

  const edits: Edit[] = [];
  for (const [parent, nodes] of inserts) {
    const lineStart = existing.lastIndexOf('\n', parent.openStart - 1) + 1;
    const parentIndent = leadingWhitespace(existing.slice(lineStart, parent.openStart));
    const lines = nodes.flatMap((n) => renderNode(n, parentIndent + unit, unit));
    const block = lines.join(eol);
    const focus = focusLine(lines, eol);

    if (parent.selfClosing) {
      const open = existing.slice(parent.openStart, parent.openEnd).replace(/\s*\/>$/, '>');
      const prefix = `${open}${eol}`;
      edits.push({
        start: parent.openStart,
        end: parent.openEnd,
        text: `${prefix}${block}${eol}${parentIndent}</${parent.name}>`,
        focusInText: prefix.length + focus,
      });
      continue;
    }
    const closeLine = existing.lastIndexOf('\n', parent.closeStart - 1) + 1;
    if (/^[ \t]*$/.test(existing.slice(closeLine, parent.closeStart))) {
      edits.push({ start: closeLine, end: closeLine, text: block + eol, focusInText: focus });
    } else {
      const prefix = eol;
      edits.push({ start: parent.closeStart, end: parent.closeStart, text: `${prefix}${block}${eol}${parentIndent}`, focusInText: prefix.length + focus });
    }
  }

  edits.sort((a, b) => a.start - b.start);
  let focus = focusExisting;
  if (edits.length > 0) {
    // Edits are applied last-to-first, so nothing shifts the first one.
    focus = edits[0].start + edits[0].focusInText;
  }

  let text = existing;
  for (let i = edits.length - 1; i >= 0; i--) {
    text = text.slice(0, edits[i].start) + edits[i].text + text.slice(edits[i].end);
  }

  const added = [...inserts.values()].reduce((sum, l) => sum + l.length, 0);
  return { text, focus: Math.max(0, focus), existing: found, added };
}
