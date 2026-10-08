# Mods menu (in-game)

When the game is started through the launcher, the game's Game Menu (Esc) gets a **Mods** button inside its panel, where the Random Seed text used to be. Click it and a centered dialog opens, styled like Settings: **DW2 Mod Launcher** first (a small dialog with the launcher's version and mod count; its Ok returns to this dialog; the status panel is still opened from the status line, see [Mod Status Line.md](<Mod Status Line.md>)), then one button per code mod that added an entry, alphabetically. Click an entry to run it; the dialog closes (see [The stack](#the-stack)). Design notes: [plans/mod-menu.md](plans/mod-menu.md).

The Random Seed text now lives on a button below Mods: click it to copy the seed to the clipboard (it says "Copied to clipboard" for two seconds). The panel is made one row taller for it.

The game gives its panel no extension point, so while the panel is visible the launcher adds the two buttons to it each frame (the game lays the panel out again every time it is shown) and builds the dialog from the game's own `Dialog` class.

## Adding an entry from a mod

Same soft dependency as `LauncherStatus`: no compile-time reference to the loader, and with no launcher every call is a no-op. Copy this next to `LauncherStatus.cs` and change the namespace.

```csharp
using System.Reflection;

namespace DW2FreighterLogistics;

// Adds entries to the mod launcher's in-game Mods menu (docs/Mod Menu.md in DW2-Mod-Launcher), if it is there.
static class LauncherMenu
{
    public static readonly bool Present;

    static readonly string id = typeof(LauncherMenu).Assembly.GetName().Name;
    static readonly Action<string, string, Action> add;
    static readonly Action<string> remove;
    static readonly Action<string, bool> setEnabled;
    static readonly Action show;

    static LauncherMenu()
    {
        try
        {
            Type menu = null;
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (a.GetName().Name == "DW2ModLauncher.Loader")
                {
                    menu = a.GetType("DW2ModLauncher.Loader.ModMenu");
                    break;
                }
            }
            if (menu == null)
            {
                return;
            }
            add = Bind<Action<string, string, Action>>(menu, "Add");
            remove = Bind<Action<string>>(menu, "Remove");
            setEnabled = Bind<Action<string, bool>>(menu, "SetEnabled");
            show = Bind<Action>(menu, "Show");
            Present = true;
        }
        catch (Exception)
        {
            Present = false;
        }
    }

    static T Bind<T>(Type type, string name) where T : Delegate
        => (T)Delegate.CreateDelegate(typeof(T), type.GetMethod(name, BindingFlags.Public | BindingFlags.Static) ?? throw new MissingMethodException(name));

    // onClick runs when the player picks the entry; an exception from it is swallowed by the loader
    public static void Add(string label, Action onClick) => add?.Invoke(id, label, onClick);

    public static void Remove() => remove?.Invoke(id);

    public static void SetEnabled(bool enabled) => setEnabled?.Invoke(id, enabled);

    // call when your own dialog closes, to bring the Mods dialog back
    public static void Show() => show?.Invoke();
}
```

| Call | Meaning |
| --- | --- |
| `Add(label, onClick)` | Adds the mod's entry, or replaces its label and handler. |
| `Remove()` | Removes it. |
| `SetEnabled(bool)` | Greys it out and ignores clicks (for example, not in a game). |
| `Show()` | Brings the Mods dialog back. Call it when your own UI closes (see below). |

One entry per mod; a mod that wants several actions opens its own dialog from `onClick`. A mod that needs a second entry can use its own id, by calling `ModMenu.Add` directly with a different first argument.

## The stack

Game Menu > Mods > your dialog. When the player picks your entry the Mods dialog closes so your UI can show. When your UI closes, call `LauncherMenu.Show()` and the player is back in the Mods dialog (nothing happens if the Game Menu has since closed). Closing the Mods dialog (Close or the x) closes the Game Menu too, so the player is back in the game: one close from the top collapses the whole stack. The launcher's own entry works the same way: its Ok returns to the Mods dialog.

A mod that does not call `Show()` simply leaves the player on the Esc menu when its UI closes, as before.

## Notes

- The handler runs on the game's input thread inside a click: do the quick thing (open your dialog, toggle a setting) and return.
- The dialog is modal and closes with its Close button, the x, or when the Game Menu closes.
- If a game update renames a member the widget uses, `dw2modlauncher.log` says which ("status widget not installed"), and the status line and the menu are the only things lost.
