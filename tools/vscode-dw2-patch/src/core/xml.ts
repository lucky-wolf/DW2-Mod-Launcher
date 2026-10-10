// A small position-aware XML reader. The game's data files and patch files are plain XML (elements, attributes, text,
// comments), and the extension needs source offsets for every tag to map the cursor to an element and to insert text
// into an existing patch file without reformatting it, which the usual XML libraries do not give.

export interface XNode {
  name: string;
  attrs: Record<string, string>;
  parent: XNode | null;
  children: XNode[];
  /** Decoded text content; meaningful for leaf elements. */
  text: string;
  /** Offset of the '<' of the opening tag, and one past its '>'. */
  openStart: number;
  openEnd: number;
  /** Offset of the '<' of the closing tag, and one past its '>'. For a self-closing element both are openEnd. */
  closeStart: number;
  closeEnd: number;
  selfClosing: boolean;
  /** Text of a comment that follows the element on the same line (`<RaceId>0</RaceId> <!-- Human -->`). */
  comment?: string;
}

export class XmlError extends Error {
  offset: number;
  constructor(message: string, offset: number) {
    super(message);
    this.offset = offset;
  }
}

function decode(s: string): string {
  return s.replace(/&(#[xX][0-9a-fA-F]+|#\d+|lt|gt|amp|quot|apos);/g, (_m, e: string) => {
    switch (e) {
      case 'lt': return '<';
      case 'gt': return '>';
      case 'amp': return '&';
      case 'quot': return '"';
      case 'apos': return "'";
      default: return String.fromCodePoint(e[1] === 'x' || e[1] === 'X' ? parseInt(e.slice(2), 16) : parseInt(e.slice(1), 10));
    }
  });
}

export function escapeText(s: string): string {
  return s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}

export function escapeAttr(s: string): string {
  return s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/"/g, '&quot;');
}

export function parseXml(src: string): XNode {
  const n = src.length;
  const stack: XNode[] = [];
  let root: XNode | null = null;
  let lastClosed: XNode | null = null;
  let i = 0;

  const addText = (s: string): void => {
    if (stack.length > 0) stack[stack.length - 1].text += s;
  };

  while (i < n) {
    const lt = src.indexOf('<', i);
    if (lt < 0) {
      addText(decode(src.slice(i)));
      break;
    }
    if (lt > i) addText(decode(src.slice(i, lt)));

    if (src.startsWith('<!--', lt)) {
      const end = src.indexOf('-->', lt + 4);
      if (end < 0) throw new XmlError('unterminated comment', lt);
      if (lastClosed && lastClosed.comment === undefined && /^[ 	]*$/.test(src.slice(lastClosed.closeEnd, lt))) {
        lastClosed.comment = src.slice(lt + 4, end).trim();
      }
      i = end + 3;
    } else if (src.startsWith('<![CDATA[', lt)) {
      const end = src.indexOf(']]>', lt + 9);
      if (end < 0) throw new XmlError('unterminated CDATA section', lt);
      addText(src.slice(lt + 9, end));
      i = end + 3;
    } else if (src.startsWith('<?', lt)) {
      const end = src.indexOf('?>', lt + 2);
      if (end < 0) throw new XmlError('unterminated processing instruction', lt);
      i = end + 2;
    } else if (src.startsWith('<!', lt)) {
      const end = src.indexOf('>', lt);
      if (end < 0) throw new XmlError('unterminated declaration', lt);
      i = end + 1;
    } else if (src[lt + 1] === '/') {
      const end = src.indexOf('>', lt);
      if (end < 0) throw new XmlError('unterminated closing tag', lt);
      const name = src.slice(lt + 2, end).trim();
      const top = stack.pop();
      if (!top || top.name !== name) {
        throw new XmlError(`closing tag </${name}> does not match ${top ? '<' + top.name + '>' : 'any open element'}`, lt);
      }
      top.closeStart = lt;
      top.closeEnd = end + 1;
      lastClosed = top;
      i = end + 1;
    } else {
      let j = lt + 1;
      let quote = '';
      while (j < n) {
        const c = src[j];
        if (quote) {
          if (c === quote) quote = '';
        } else if (c === '"' || c === "'") {
          quote = c;
        } else if (c === '>') {
          break;
        }
        j++;
      }
      if (j >= n) throw new XmlError('unterminated tag', lt);

      const selfClosing = src[j - 1] === '/';
      const inner = src.slice(lt + 1, selfClosing ? j - 1 : j);
      const m = /^([^\s/>]+)([\s\S]*)$/.exec(inner);
      if (!m) throw new XmlError('empty tag', lt);

      const attrs: Record<string, string> = {};
      const attrRe = /([^\s=]+)\s*=\s*(?:"([^"]*)"|'([^']*)')/g;
      let am: RegExpExecArray | null;
      while ((am = attrRe.exec(m[2])) !== null) attrs[am[1]] = decode(am[2] ?? am[3]);

      const parent = stack.length > 0 ? stack[stack.length - 1] : null;
      const node: XNode = {
        name: m[1],
        attrs,
        parent,
        children: [],
        text: '',
        openStart: lt,
        openEnd: j + 1,
        closeStart: j + 1,
        closeEnd: j + 1,
        selfClosing,
      };
      if (parent) {
        parent.children.push(node);
      } else {
        if (root) throw new XmlError('more than one root element', lt);
        root = node;
      }
      if (selfClosing) lastClosed = node;
      else stack.push(node);
      i = j + 1;
    }
  }

  if (stack.length > 0) throw new XmlError(`<${stack[stack.length - 1].name}> is never closed`, stack[stack.length - 1].openStart);
  if (!root) throw new XmlError('no root element', 0);
  return root;
}
