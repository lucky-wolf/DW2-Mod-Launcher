# Mod status widget (in-game)

When the game is started through the launcher, the loader draws one line of text in the lower left of the screen. It says how many injected code mods loaded and, in red, any problem it knows of. Click it and a dialog opens: the screen dims, and a panel above the line shows one block per code mod.

```
[+] DW2 mods: 4 loaded
[+] /!\ 1 mod with a problem: DW2FreighterLogistics: FuelFirst failed to install
```

The loader claims only what it knows: the mods loaded (or one failed to: a missing DLL, or an exception from `Init`, shows in red on its own). It does not claim a mod is running or healthy. Anything more in a mod's block is what that mod chose to report (below); a block with nothing extra means the mod is not wired to the widget, which is fine.

Mods can also add entries to the Game Menu: [Mod Menu.md](<Mod Menu.md>).

Design history and open questions: [plans/mod-status-line.md](plans/mod-status-line.md).

## What the loader reports by itself

For every code mod in the manifest: its DLL name, the version and build time read from the DLL (the build time is the file's write time, so a stale build is visible), and whether the entry point ran. A DLL that is missing, a type or method that is not found, or an exception from `Init` is an error ("failed to load: ...").

## Reporting more from a mod

A mod has no compile-time reference to the loader (it must keep working when injected directly), so it reaches `DW2ModLauncher.Loader.ModStatus` by reflection. Copy this file into the mod, change the namespace, and call it. With no launcher every call is a no-op and `LauncherStatus.Present` is false, which is the cue to draw the mod's own line if it wants one. DW2FreighterLogistics is the worked example (`dll/DW2FreighterLogistics/LauncherStatus.cs` in DW2-XL).

```csharp
using System.Reflection;

namespace DW2FreighterLogistics;

// Reports to the mod launcher's shared in-game status widget (docs/Mod Status Line.md in DW2-Mod-Launcher), if it is there.
//
// A soft dependency: there is no compile-time reference to the loader. The loader's DW2ModLauncher.Loader.ModStatus is found by name
// among the loaded assemblies, and every call is a delegate created once. When the dll is injected directly (no launcher) Present is
// false, every call is a no-op, and the mod keeps drawing its own line (StatusLine.cs). When the launcher is there it draws one line
// and one panel for all code mods, so this mod draws nothing of its own.
//
// This file is the copy-paste snippet for other mods: change the namespace and nothing else.
static class LauncherStatus
{
    public static readonly bool Present;

    // the loader registers every mod under its dll's file name without the extension, which is the assembly name
    static readonly string id = typeof(LauncherStatus).Assembly.GetName().Name;
    static readonly Action<string, string, string, string> register;
    static readonly Action<string, int, string> setLevel;
    static readonly Action<string, string, string, int> detail;
    static readonly Action<string, string> error;
    static readonly Action<string, string> installed;
    static readonly Action<string, string> setLog;

    static LauncherStatus()
    {
        try
        {
            Type status = null;
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (a.GetName().Name == "DW2ModLauncher.Loader")
                {
                    status = a.GetType("DW2ModLauncher.Loader.ModStatus");
                    break;
                }
            }
            if (status == null)
            {
                return;
            }
            register = Bind<Action<string, string, string, string>>(status, "Register");
            setLevel = Bind<Action<string, int, string>>(status, "SetLevel");
            detail = Bind<Action<string, string, string, int>>(status, "Detail");
            error = Bind<Action<string, string>>(status, "Error");
            installed = Bind<Action<string, string>>(status, "Installed");
            setLog = Bind<Action<string, string>>(status, "SetLog");
            Present = true;
        }
        catch (Exception)
        {
            // an older or odd loader: act as if it were not there
            Present = false;
        }
    }

    static T Bind<T>(Type type, string name) where T : Delegate
        => (T)Delegate.CreateDelegate(typeof(T), type.GetMethod(name, BindingFlags.Public | BindingFlags.Static) ?? throw new MissingMethodException(name));

    // name is shown in place of the assembly name; version and built are what the loader already read from the dll, so pass null to keep them
    public static void Register(string name, string version, string built) => register?.Invoke(id, name, version, built);

    // level: 0 ok, 1 info, 2 warn, 3 error
    public static void SetLevel(int level, string summary) => setLevel?.Invoke(id, level, summary);

    // a named line in the expanded panel; the same key again replaces it, empty text removes it
    public static void Detail(string key, string text, int level = 0) => detail?.Invoke(id, key, text, level);

    public static void Error(string text) => error?.Invoke(id, text);

    public static void Installed(string feature) => installed?.Invoke(id, feature);

    public static void SetLog(string path) => setLog?.Invoke(id, path);
}
```

| Call | Meaning |
| --- | --- |
| `Installed(feature)` | A feature that installed. Listed in the mod's block. |
| `Error(text)` | Something failed. First line only is kept (put the full text in the mod's own log); repeats are kept once. Turns the mod red. |
| `Detail(key, text, level)` | A named line in the panel. The same key again replaces it, empty text removes it. `level` 0 ok, 1 info, 2 warn, 3 error; warn and error also count toward the mod's colour. |
| `SetLevel(level, summary)` | The mod's own overall level, and one line shown under its name. |
| `SetLog(path)` | Where the mod's full log is; shown in the panel. |
| `Register(name, version, built)` | Optional. Overrides the name shown (the DLL name by default). |

A mod is identified by its assembly name, which is also what the loader registers it under (the DLL file name without `.dll`).

### Rules

- A status call never throws and never affects the mod or the game.
- The line is an index. `dw2modlauncher.log` and the mod's own log keep the full text.
- Do not draw a second line of your own when `LauncherStatus.Present`; the whole point is one line for all mods.
- Every log line that says "failed" or "fatal" becoming an `Error` (as DW2FreighterLogistics does) is a cheap way to make errors surface without each call site remembering to report them.
- There is deliberately no heartbeat: the loader does not ask mods to run extra code just to be counted.

## Using it

- Click the line to open the dialog; click anywhere (the line, a block, or the dimmed screen) to close it. Nothing underneath can be clicked while it is open. Blocks list mods with problems first.
- The widget hides with the rest of the game's interface.
- `[+]` / `[-]` on the line shows whether the dialog is closed or open; `/!\` means at least one mod has an error.

## How it is built

- `StatusRegistry` / `ModStatus` (loader, BCL only): the data. `StatusRegistry` is an ordinary class so it is unit tested with a fake clock; `ModStatus` is the shared instance with primitive-only methods that mods call by reflection.
- `StatusText` (loader): all the words, as pure functions, unit tested.
- `StatusWidget` (loader): the drawing. The loader is built on CI machines that do not have the game installed, so it cannot reference the game's assemblies. The widget therefore uses the game's own `DWButton` custom controls and `UserInterfaceController.Update` (hooked with Harmony) entirely by reflection. Every game member is resolved once in `Game.Bind`; anything missing throws there, is logged to `dw2modlauncher.log` as "status widget not installed", and leaves the mods and the game untouched. Colour is the only icon (there is no custom drawing), plus the `[+]` and `/!\` markers.
- The game clears its custom controls when the interface is rebuilt (scaling change, new game), so the per-frame hook adds the controls back whenever they are missing.

If a game update renames one of the members the widget uses, `dw2modlauncher.log` will say which, and the widget is the only thing lost.
