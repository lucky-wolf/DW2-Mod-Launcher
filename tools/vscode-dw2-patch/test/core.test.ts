import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { analyse } from '../src/core/actions.ts';
import type { PatchAction } from '../src/core/actions.ts';
import { ENTITY_KEYS, ITEM_KEYS } from '../src/core/keyMap.ts';
import { mergeIntoPatchFile } from '../src/core/patchFile.ts';
import type { PatchNode } from '../src/core/patchTree.ts';
import { renderNode } from '../src/core/patchTree.ts';
import { parseXml } from '../src/core/xml.ts';

const DATA = `<?xml version="1.0" encoding="utf-8"?>
<ArrayOfRace xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
  <Race>
    <RaceId>0</RaceId>
    <Name>Human</Name>
    <Aggression>1.5</Aggression>
    <MainColor><R>10</R><G>20</G><B>30</B></MainColor>
    <AllowableGovernmentIds>
      <short>1</short>
      <short>2</short>
    </AllowableGovernmentIds>
    <Bonuses>
      <Bonus>
        <Type>ResearchAll</Type>
        <Amount>0.1</Amount>
      </Bonus>
      <Bonus>
        <Type>Wealth</Type>
        <Amount>0.2</Amount>
      </Bonus>
    </Bonuses>
    <Values>
      <ComponentStats><Damage>5</Damage></ComponentStats>
      <ComponentStats><Damage>9</Damage></ComponentStats>
    </Values>
  </Race>
  <Race>
    <RaceId>1</RaceId>
    <Name>Ackdarian</Name>
  </Race>
</ArrayOfRace>
`;

/** Caret marked with '|' (or a selection with '[' and ']'); returns the actions the extension would offer there. */
function at(marked: string) {
  const open = marked.indexOf('[');
  const close = marked.indexOf(']');
  const caret = marked.indexOf('|');
  const src = marked.replace(/[|[\]]/g, '');
  const root = parseXml(src);
  const analysis = open >= 0 ? analyse(root, src, open, close - 1) : analyse(root, src, caret, caret);
  return { analysis, root };
}

function render(forest: PatchNode[]): string {
  return forest.flatMap((n) => renderNode(n, '', '  ')).join('\n');
}

function run(action: PatchAction, pick: number[] = []): string {
  const leaves = action.fixedLeaves ?? (action.leafChoices ? pick.map((i) => action.leafChoices![i]) : []);
  return render(action.build(leaves));
}

const withCaret = (needle: string, offset = 0): string => {
  const i = DATA.indexOf(needle) + offset;
  return DATA.slice(0, i) + '|' + DATA.slice(i);
};

test('parser records offsets and decodes text', () => {
  const root = parseXml('<A><B x="1 &amp; 2">a &lt; b</B><C/></A>');
  assert.equal(root.children[0].text, 'a < b');
  assert.equal(root.children[0].attrs.x, '1 & 2');
  assert.equal(root.children[1].selfClosing, true);
  assert.equal(root.children[0].openStart, 3);
});

test('scalar field: change value', () => {
  const { analysis } = at(withCaret('<Aggression>', 4));
  assert.equal(analysis.kind, 'scalar');
  assert.equal(analysis.title, 'Race id=0 › Aggression');
  assert.equal(run(analysis.actions[0]), '<Race id="0">\n  <Aggression>1.5</Aggression>\n</Race>');
});

test('scalar field: remove', () => {
  const { analysis } = at(withCaret('<Aggression>', 4));
  assert.equal(run(analysis.actions[1]), '<Race id="0">\n  <Aggression op="remove"/>\n</Race>');
});

test('caret on whitespace before a field still finds it', () => {
  const { analysis } = at(withCaret('<Aggression>', -2));
  assert.equal(analysis.title, 'Race id=0 › Aggression');
});

test('field inside a struct', () => {
  const { analysis } = at(withCaret('<G>', 4));
  assert.equal(run(analysis.actions[0]), '<Race id="0">\n  <MainColor><G>20</G></MainColor>\n</Race>');
});

test('caret on the key element selects the entity', () => {
  const { analysis } = at(withCaret('<RaceId>', 3));
  assert.equal(analysis.kind, 'entity');
  assert.deepEqual(analysis.actions.map((a) => a.label), ['Change fields…', 'Remove the entity']);
  assert.equal(run(analysis.actions[1]), '<Race id="0" op="remove"/>');
});

test('entity: pick fields', () => {
  const { analysis } = at(withCaret('<Race>', 2));
  const fields = analysis.actions[0];
  assert.ok(fields.leafChoices!.some((l) => l.name === 'Name'));
  assert.ok(!fields.leafChoices!.some((l) => l.name === 'RaceId'));
  const names = fields.leafChoices!.map((l) => l.name);
  const out = run(fields, [names.indexOf('Name'), names.indexOf('G')]);
  assert.equal(out, '<Race id="0">\n  <Name>Human</Name>\n  <MainColor><G>20</G></MainColor>\n</Race>');
});

test('keyed list item field uses id', () => {
  const { analysis } = at(withCaret('<Amount>0.2', 3));
  assert.equal(run(analysis.actions[0]), '<Race id="0">\n  <Bonuses>\n    <Bonus id="Wealth"><Amount>0.2</Amount></Bonus>\n  </Bonuses>\n</Race>');
});

test('unkeyed list item field uses a 1-based index', () => {
  const { analysis } = at(withCaret('<Damage>9', 3));
  assert.match(run(analysis.actions[0]), /<ComponentStats index="2">/);
});

test('scalar list items: set, add, remove', () => {
  const { analysis } = at(withCaret('<short>2', 3));
  assert.equal(analysis.kind, 'scalarItem');
  assert.match(run(analysis.actions[0]), /<short index="2">2<\/short>/);
  assert.match(run(analysis.actions[1]), /<short op="add">2<\/short>/);
  assert.match(run(analysis.actions[2]), /<short op="remove">2<\/short>/);
});

test('list item: remove and add-copy', () => {
  const { analysis } = at(withCaret('<Bonus>', 2));
  assert.equal(analysis.kind, 'structItem');
  const labels = analysis.actions.map((a) => a.label);
  assert.ok(labels.includes('Remove this item'));
  const remove = analysis.actions[labels.indexOf('Remove this item')];
  assert.match(run(remove), /<Bonus id="ResearchAll" op="remove"\/>/);
  const add = analysis.actions[labels.indexOf('Add a new item like this one')];
  assert.match(run(add), /<Bonus op="add">\s*<Type>ResearchAll<\/Type>\s*<Amount>0.1<\/Amount>\s*<\/Bonus>/);
});

test('struct replace writes plain data', () => {
  const { analysis } = at(withCaret('<MainColor>', 3));
  assert.equal(analysis.kind, 'struct');
  const replace = analysis.actions.find((a) => a.label.startsWith('Replace'))!;
  assert.equal(run(replace), '<Race id="0">\n  <MainColor op="replace"><R>10</R><G>20</G><B>30</B></MainColor>\n</Race>');
});

test('multi-line selection patches every complete field, across entities', () => {
  const start = DATA.indexOf('<Name>Human');
  const end = DATA.indexOf('</MainColor>') + '</MainColor>'.length;
  const { analysis } = at(DATA.slice(0, start) + '[' + DATA.slice(start, end) + ']' + DATA.slice(end));
  assert.equal(analysis.kind, 'selection');
  const out = run(analysis.actions[0]);
  assert.match(out, /<Name>Human<\/Name>/);
  assert.match(out, /<Aggression>1.5<\/Aggression>/);
  assert.match(out, /<R>10<\/R>/);
  assert.equal((out.match(/<Race id="0">/g) ?? []).length, 1);
});

test('merge creates a new patch file', () => {
  const { analysis } = at(withCaret('<Aggression>', 4));
  const res = mergeIntoPatchFile(null, 'ArrayOfRace', analysis.actions[0].build([]));
  assert.equal(res.text, '<ArrayOfRace>\n  <Race id="0">\n    <Aggression>1.5</Aggression>\n  </Race>\n</ArrayOfRace>\n');
  assert.equal(res.text.slice(res.focus, res.focus + 12), '<Aggression>');
});

test('merge adds into an existing entity and keeps the rest', () => {
  const existing = '<ArrayOfRace>\n  <!-- mine -->\n  <Race id="0">\n    <Name>X</Name>\n  </Race>\n</ArrayOfRace>\n';
  const { analysis } = at(withCaret('<Aggression>', 4));
  const res = mergeIntoPatchFile(existing, 'ArrayOfRace', analysis.actions[0].build([]));
  assert.equal(res.text, '<ArrayOfRace>\n  <!-- mine -->\n  <Race id="0">\n    <Name>X</Name>\n    <Aggression>1.5</Aggression>\n  </Race>\n</ArrayOfRace>\n');
  assert.equal(res.added, 1);
  assert.equal(res.text.slice(res.focus, res.focus + 12), '<Aggression>');
});

test('merge reports what is already patched instead of duplicating', () => {
  const existing = '<ArrayOfRace>\n  <Race id="0">\n    <Aggression>3</Aggression>\n  </Race>\n</ArrayOfRace>\n';
  const { analysis } = at(withCaret('<Aggression>', 4));
  const res = mergeIntoPatchFile(existing, 'ArrayOfRace', analysis.actions[0].build([]));
  assert.equal(res.text, existing);
  assert.deepEqual(res.existing, ['Race › Aggression']);
  assert.equal(res.text.slice(res.focus, res.focus + 12), '<Aggression>');
});

test('merge expands a self-closing entity and a new entity is appended', () => {
  const existing = '<ArrayOfRace>\n  <Race id="0"/>\n</ArrayOfRace>';
  const a = at(withCaret('<Aggression>', 4)).analysis.actions[0].build([]);
  const b = at(withCaret('<Name>Ackdarian', 3)).analysis.actions[0].build([]);
  const res = mergeIntoPatchFile(existing, 'ArrayOfRace', [...a, ...b]);
  assert.equal(
    res.text,
    '<ArrayOfRace>\n  <Race id="0">\n    <Aggression>1.5</Aggression>\n  </Race>\n  <Race id="1">\n    <Name>Ackdarian</Name>\n  </Race>\n</ArrayOfRace>',
  );
});

test('merge keeps CRLF files CRLF', () => {
  const existing = '<ArrayOfRace>\r\n  <Race id="0">\r\n    <Name>X</Name>\r\n  </Race>\r\n</ArrayOfRace>\r\n';
  const { analysis } = at(withCaret('<Aggression>', 4));
  const res = mergeIntoPatchFile(existing, 'ArrayOfRace', analysis.actions[0].build([]));
  assert.ok(!/[^\r]\n/.test(res.text));
  assert.ok(res.text.includes('<Aggression>1.5</Aggression>'));
});

test('key map matches KeyMap.cs', () => {
  const cs = readFileSync(new URL('../../../src/DW2ModLauncher.XmlPatching/KeyMap.cs', import.meta.url), 'utf8');
  const [entities, items] = cs.split('// List items that are keyed');
  const pairs = (s: string): Map<string, string> => new Map([...s.matchAll(/\.Add\("(\w+)", "(\w+)"\)/g)].map((m) => [m[1], m[2]]));
  assert.deepEqual(pairs(entities), new Map(ENTITY_KEYS));
  assert.deepEqual(pairs(items), new Map(ITEM_KEYS));
});

test('with the game schema a one-item list is a list and an empty list can take items', async () => {
  const { Schema } = await import('../src/core/schema.ts');
  const { useSchema } = await import('../src/core/model.ts');
  const schema = new Schema(JSON.parse(readFileSync(new URL('../schema/schema.json', import.meta.url), 'utf8')));
  const data =
    '<ArrayOfComponentDefinition>\n  <ComponentDefinition>\n    <ComponentId>5</ComponentId>\n' +
    '    <Values>\n      <ComponentStats><CrewRequirement>3</CrewRequirement></ComponentStats>\n    </Values>\n' +
    '    <ResourcesRequired />\n  </ComponentDefinition>\n</ArrayOfComponentDefinition>';
  const analyseAt = (needle: string, off: number) => {
    const root = parseXml(data);
    const i = data.indexOf(needle) + off;
    return analyse(root, data, i, i);
  };

  try {
    useSchema(null);
    assert.equal(analyseAt('<Values>', 2).kind, 'struct'); // what the shape alone suggests

    useSchema(schema);
    assert.equal(analyseAt('<Values>', 2).kind, 'list');
    const item = analyseAt('<ComponentStats>', 2);
    assert.equal(item.kind, 'structItem');
    assert.match(run(item.actions.find((a) => a.label === 'Remove this item')!), /<ComponentStats index="1" op="remove"\/>/);

    const empty = analyseAt('<ResourcesRequired', 3);
    assert.equal(empty.kind, 'list');
    assert.equal(run(empty.actions[0]), '<ComponentDefinition id="5">\n  <ResourcesRequired>\n    <ResourceQuantity op="add"/>\n  </ResourcesRequired>\n</ComponentDefinition>');
  } finally {
    useSchema(null);
  }
});

test('the comment naming an id is copied to the element that selects by it', () => {
  const data =
    '<ArrayOfRace>\n  <Race>\n    <RaceId>0</RaceId> <!-- Human -->\n    <Aggression>1.5</Aggression>\n' +
    '    <Bonuses>\n      <Bonus>\n        <Type>Wealth</Type> <!-- Money -->\n        <Amount>0.2</Amount>\n      </Bonus>\n    </Bonuses>\n' +
    '    <AllowableGovernmentIds>\n      <short>2</short> <!-- Democracy -->\n      <short>0</short> <!-- Republic -->\n    </AllowableGovernmentIds>\n  </Race>\n</ArrayOfRace>';
  const go = (needle: string, off: number, action = 0, pick: number[] = []) => {
    const root = parseXml(data);
    const i = data.indexOf(needle) + off;
    const a = analyse(root, data, i, i);
    return run(a.actions[action], pick);
  };

  assert.equal(parseXml(data).children[0].children[0].comment, 'Human');
  assert.equal(go('<Aggression>', 3), '<Race id="0"> <!-- Human -->\n  <Aggression>1.5</Aggression>\n</Race>');
  assert.equal(go('<Amount>', 3), '<Race id="0"> <!-- Human -->\n  <Bonuses>\n    <Bonus id="Wealth"><Amount>0.2</Amount></Bonus> <!-- Money -->\n  </Bonuses>\n</Race>');
  assert.equal(go('<short>0', 3, 1), '<Race id="0"> <!-- Human -->\n  <AllowableGovernmentIds>\n    <short op="add">0</short> <!-- Republic -->\n  </AllowableGovernmentIds>\n</Race>');
  // Replacing a list keeps the comments of its items.
  assert.match(go('<AllowableGovernmentIds>', 3, 1), /<short>2<\/short> <!-- Democracy -->/);
  // Entity removal.
  assert.equal(go('<Race>', 2, 1), '<Race id="0" op="remove"/> <!-- Human -->');
});

test('a comment does not stop an already patched element being recognised', () => {
  const data = '<ArrayOfRace>\n  <Race>\n    <RaceId>0</RaceId> <!-- Human -->\n    <Aggression>1.5</Aggression>\n  </Race>\n</ArrayOfRace>';
  const root = parseXml(data);
  const i = data.indexOf('<Aggression>') + 3;
  const forest = analyse(root, data, i, i).actions[0].build([]);
  const first = mergeIntoPatchFile(null, 'ArrayOfRace', forest);
  const again = mergeIntoPatchFile(first.text, 'ArrayOfRace', forest);
  assert.equal(again.text, first.text);
  assert.deepEqual(again.existing, ['Race › Aggression']);
});
