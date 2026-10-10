import * as fs from 'node:fs';
import * as path from 'node:path';
import * as vscode from 'vscode';
import { analyse, leafLabel } from './core/actions.ts';
import type { Analysis, PatchAction } from './core/actions.ts';
import { useSchema } from './core/model.ts';
import { mergeIntoPatchFile } from './core/patchFile.ts';
import { PatchError } from './core/patchTree.ts';
import { Schema } from './core/schema.ts';
import { parseXml, XmlError } from './core/xml.ts';
import type { XNode } from './core/xml.ts';

const MOD_MARKERS = ['mod.json', 'dw2modlauncher.json'];
const REMEMBERED_MOD = 'modFolder';

export function activate(context: vscode.ExtensionContext): void {
  loadSchema(context);
  context.subscriptions.push(
    vscode.commands.registerCommand('dw2Patch.patchHere', () => run(context, false)),
    vscode.commands.registerCommand('dw2Patch.patchHereChoose', () => run(context, true)),
    vscode.commands.registerCommand('dw2Patch.selectModFolder', async () => {
      const folder = await askForModFolder(context);
      if (folder) vscode.window.showInformationMessage(`DW2 patches will go to ${path.join(folder.fsPath, 'patches')}`);
    }),
  );
}

export function deactivate(): void {}

function loadSchema(context: vscode.ExtensionContext): void {
  try {
    const file = path.join(context.extensionPath, 'schema', 'schema.json');
    useSchema(new Schema(JSON.parse(fs.readFileSync(file, 'utf8'))));
  } catch {
    // Without the exported schema the shape of the document is used to guess (less exact for one-item lists).
    useSchema(null);
  }
}

async function run(context: vscode.ExtensionContext, alwaysAsk: boolean): Promise<void> {
  const editor = vscode.window.activeTextEditor;
  if (!editor) return;
  const doc = editor.document;

  try {
    if (/[\\/]patches[\\/]/i.test(doc.uri.fsPath)) {
      throw new PatchError('This already is a patch file. Run the command in one of the game\'s data files.');
    }

    let root: XNode;
    const src = doc.getText();
    try {
      root = parseXml(src);
    } catch (e) {
      if (e instanceof XmlError) {
        throw new PatchError(`This file is not valid XML: ${e.message} (line ${doc.positionAt(e.offset).line + 1}).`);
      }
      throw e;
    }

    const sel = editor.selection;
    const analysis = analyse(root, src, doc.offsetAt(sel.start), doc.offsetAt(sel.end));

    const action = await chooseAction(analysis, alwaysAsk);
    if (!action) return;
    const leaves = await chooseLeaves(analysis, action);
    if (!leaves) return;
    const forest = action.build(leaves);

    const modFolder = await findModFolder(context);
    if (!modFolder) return;

    const cfg = vscode.workspace.getConfiguration('dw2Patch');
    const configured = (cfg.get<string>('patchFile') ?? '').trim();
    const relative = configured !== '' ? configured : `patches/${path.basename(doc.uri.fsPath)}`;
    const target = vscode.Uri.joinPath(modFolder, ...relative.split(/[\\/]/));
    const unit = (cfg.get<string>('indent') ?? '  ').replace(/\\t/g, '\t') || '  ';

    const opened = vscode.workspace.textDocuments.find((d) => d.uri.toString() === target.toString());
    let existing: string | null = null;
    if (opened) {
      existing = opened.getText();
    } else {
      try {
        existing = new TextDecoder().decode(await vscode.workspace.fs.readFile(target));
      } catch {
        existing = null; // no patch file yet
      }
    }

    const merged = mergeIntoPatchFile(existing, root.name, forest, unit);
    if (merged.text !== existing) await write(target, opened, merged.text);

    const shown = await vscode.window.showTextDocument(target, { viewColumn: vscode.ViewColumn.Beside, preserveFocus: false });
    const pos = shown.document.positionAt(merged.focus);
    shown.selection = new vscode.Selection(pos, pos);
    shown.revealRange(new vscode.Range(pos, pos), vscode.TextEditorRevealType.InCenter);

    const where = vscode.workspace.asRelativePath(target, false);
    if (merged.existing.length > 0) {
      vscode.window.showWarningMessage(`Already in ${where}, left as it is: ${merged.existing.join('; ')}`);
    } else {
      vscode.window.setStatusBarMessage(`DW2: ${analysis.title} → ${where}`, 5000);
    }
  } catch (e) {
    if (e instanceof PatchError) vscode.window.showWarningMessage(e.message);
    else vscode.window.showErrorMessage(`DW2 patch failed: ${e instanceof Error ? e.message : String(e)}`);
  }
}

async function chooseAction(analysis: Analysis, alwaysAsk: boolean): Promise<PatchAction | undefined> {
  const { actions } = analysis;
  // Changing one value is the common case: no prompt unless asked.
  if (!alwaysAsk && (analysis.kind === 'scalar' || analysis.kind === 'selection')) return actions[0];
  if (actions.length === 1) return actions[0];
  const pick = await vscode.window.showQuickPick(
    actions.map((a) => ({ label: a.label, description: a.detail, action: a })),
    { title: `Patch ${analysis.title}`, placeHolder: 'What should the patch do?' },
  );
  return pick?.action;
}

async function chooseLeaves(analysis: Analysis, action: PatchAction): Promise<XNode[] | undefined> {
  if (action.fixedLeaves) return action.fixedLeaves;
  const choices = action.leafChoices;
  if (!choices) return [];
  const base = analysis.target!;
  const picks = await vscode.window.showQuickPick(
    choices.map((leaf) => ({ label: leafLabel(leaf, base), leaf })),
    { title: `Patch ${analysis.title}`, placeHolder: 'Pick the values to change', canPickMany: true },
  );
  if (!picks || picks.length === 0) return undefined;
  return picks.map((p) => p.leaf);
}

async function write(target: vscode.Uri, opened: vscode.TextDocument | undefined, text: string): Promise<void> {
  if (opened) {
    const edit = new vscode.WorkspaceEdit();
    edit.replace(target, new vscode.Range(opened.positionAt(0), opened.positionAt(opened.getText().length)), text);
    await vscode.workspace.applyEdit(edit);
    await opened.save();
    return;
  }
  await vscode.workspace.fs.createDirectory(vscode.Uri.joinPath(target, '..'));
  await vscode.workspace.fs.writeFile(target, new TextEncoder().encode(text));
}

async function exists(uri: vscode.Uri): Promise<boolean> {
  try {
    await vscode.workspace.fs.stat(uri);
    return true;
  } catch {
    return false;
  }
}

async function isModFolder(dir: vscode.Uri): Promise<boolean> {
  for (const m of MOD_MARKERS) if (await exists(vscode.Uri.joinPath(dir, m))) return true;
  return false;
}

async function findModFolder(context: vscode.ExtensionContext): Promise<vscode.Uri | undefined> {
  const configured = (vscode.workspace.getConfiguration('dw2Patch').get<string>('modFolder') ?? '').trim();
  if (configured !== '') {
    const base = vscode.workspace.workspaceFolders?.[0]?.uri;
    return path.isAbsolute(configured) || !base ? vscode.Uri.file(configured) : vscode.Uri.joinPath(base, configured);
  }

  const remembered = context.workspaceState.get<string>(REMEMBERED_MOD);
  if (remembered && (await exists(vscode.Uri.file(remembered)))) return vscode.Uri.file(remembered);

  const found: vscode.Uri[] = [];
  for (const wf of vscode.workspace.workspaceFolders ?? []) {
    if (await isModFolder(wf.uri)) found.push(wf.uri);
    for (const [name, type] of await vscode.workspace.fs.readDirectory(wf.uri)) {
      if (type !== vscode.FileType.Directory || name.startsWith('.') || name === 'node_modules') continue;
      const sub = vscode.Uri.joinPath(wf.uri, name);
      if (await isModFolder(sub)) found.push(sub);
    }
  }

  if (found.length === 1) return found[0];
  if (found.length > 1) {
    const pick = await vscode.window.showQuickPick(
      found.map((u) => ({ label: path.basename(u.fsPath), description: u.fsPath, uri: u })),
      { title: 'Which mod should receive the patch?' },
    );
    if (!pick) return undefined;
    await context.workspaceState.update(REMEMBERED_MOD, pick.uri.fsPath);
    return pick.uri;
  }
  return askForModFolder(context);
}

async function askForModFolder(context: vscode.ExtensionContext): Promise<vscode.Uri | undefined> {
  const picked = await vscode.window.showOpenDialog({
    canSelectFiles: false,
    canSelectFolders: true,
    canSelectMany: false,
    openLabel: 'Use as mod folder',
    title: 'Choose your mod folder (the one with mod.json); patches go to its "patches" folder',
  });
  if (!picked || picked.length === 0) return undefined;
  await context.workspaceState.update(REMEMBERED_MOD, picked[0].fsPath);
  return picked[0];
}
