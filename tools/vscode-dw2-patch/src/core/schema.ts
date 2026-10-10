// The game's data schema as exported by tools/export-schema (what the launcher's XmlPatching reflects from
// DistantWorlds.Types.dll at run time). Tells exactly what an element is, instead of guessing from the document's shape.

import type { XNode } from './xml.ts';

export interface SchemaMember {
  /** s = scalar field, t = struct, l = list */
  k: 's' | 't' | 'l';
  /** List: element name of one item. */
  item?: string;
  /** Struct: its type. List of structs: the item type. Index into SchemaData.types. */
  type?: number;
  /** Scalar (or scalar list item) value kind: string, bool, integer, float, enum. */
  v?: string;
  flags?: boolean;
  names?: string[];
}

export interface SchemaData {
  generatedFrom?: string;
  roots: Record<string, { entity: string; type: number }>;
  types: Record<string, SchemaMember>[];
}

export type SchemaKind = 'entity' | 'struct' | 'list' | 'scalar' | 'scalarItem' | 'structItem';

export interface Resolved {
  kind: SchemaKind;
  /** Members of an entity, struct or struct item. */
  type?: Record<string, SchemaMember>;
  /** The member that declares this element (absent for an entity or a struct item). */
  member?: SchemaMember;
}

export class Schema {
  data: SchemaData;

  constructor(data: SchemaData) {
    this.data = data;
  }

  hasRoot(rootName: string): boolean {
    return rootName in this.data.roots;
  }

  /** What n is according to the schema, or null when the schema does not know it (unknown root or field). */
  resolve(n: XNode, root: XNode): Resolved | null {
    const info = this.data.roots[root.name];
    if (!info) return null;

    const chain: XNode[] = [];
    for (let a: XNode | null = n; a && a !== root; a = a.parent) chain.unshift(a);
    if (chain.length === 0 || chain[0].name !== info.entity) return null;

    let cur: Resolved = { kind: 'entity', type: this.data.types[info.type] };
    for (const el of chain.slice(1)) {
      if (cur.kind === 'entity' || cur.kind === 'struct' || cur.kind === 'structItem') {
        const m: SchemaMember | undefined = cur.type?.[el.name];
        if (!m) return null;
        if (m.k === 's') cur = { kind: 'scalar', member: m };
        else if (m.k === 't') cur = { kind: 'struct', type: this.data.types[m.type!], member: m };
        else cur = { kind: 'list', member: m };
      } else if (cur.kind === 'list') {
        const m: SchemaMember = cur.member!;
        if (el.name !== m.item) return null;
        cur = m.type !== undefined ? { kind: 'structItem', type: this.data.types[m.type] } : { kind: 'scalarItem', member: m };
      } else {
        return null;
      }
    }
    return cur;
  }
}
