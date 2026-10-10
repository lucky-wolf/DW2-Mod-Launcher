using System;

namespace DW2ModLauncher.Loader
{
    // The menu surface a code mod reaches by reflection (see docs/Mod Menu.md). Same rules as ModStatus: static, primitives and
    // delegates only, identified by assembly name, and nothing here ever throws into the caller.
    public static class ModMenu
    {
        public static readonly MenuRegistry Registry = new MenuRegistry();

        // Adds (or replaces) the mod's entry under Mods. onClick runs when the player picks it; an exception from it is swallowed.
        public static void Add(string id, string label, Action onClick) => Safe(() => Registry.Add(id, label, Guard(id, onClick)));

        static volatile bool _showRequested;

        // A mod calls this when its own UI closes, to bring the Mods dialog back (the stack: Game Menu > Mods > the mod's dialog).
        // Ignored unless the Game Menu is still up. The dialog opens on the next frame.
        public static void Show() => _showRequested = true;

        internal static bool TakeShowRequest()
        {
            bool requested = _showRequested;
            _showRequested = false;
            return requested;
        }

        public static void Remove(string id) => Safe(() => Registry.Remove(id));

        public static void SetEnabled(string id, bool enabled) => Safe(() => Registry.SetEnabled(id, enabled));

        static Action Guard(string id, Action onClick)
        {
            if (onClick == null) return null;
            return () =>
            {
                try
                {
                    onClick();
                }
                catch (Exception ex)
                {
                    // a mod's handler must not take the game's input loop down, but the failure is recorded against the mod
                    Report("menu handler '" + id + "' threw", id, ex);
                }
            };
        }

        static void Safe(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Report("menu call failed", null, ex);
            }
        }

        static void Report(string what, string id, Exception ex)
        {
            try
            {
                StatusWidget.Log("ERROR mods menu: " + what + ": " + ex);
                if (id != null) ModStatus.Error(id, what + ": " + ex.Message);
            }
            catch (Exception)
            {
                // reporting must not throw into the mod or the game either
            }
        }
    }
}
