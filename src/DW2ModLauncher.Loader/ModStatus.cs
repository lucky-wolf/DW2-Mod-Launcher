using System;

namespace DW2ModLauncher.Loader
{
    // The surface a code mod reaches by reflection (see docs/Mod Status Line.md). Static and primitive-only on purpose: a mod has no
    // compile-time reference to the loader, so every parameter is a string or int and there is no handle object to carry around.
    //
    // A mod identifies itself with its assembly name (the loader registers each mod under the DLL's file name, so the two agree).
    // None of these ever throws: a status call must never affect a mod or the game.
    public static class ModStatus
    {
        public static readonly StatusRegistry Registry = new StatusRegistry();

        public static void Register(string id, string name, string version, string built) => Safe(() => Registry.Register(id, name, version, built));

        // level: 0 ok, 1 info, 2 warn, 3 error
        public static void SetLevel(string id, int level, string summary) => Safe(() => Registry.SetLevel(id, ToLevel(level), summary));

        public static void Detail(string id, string key, string text, int level) => Safe(() => Registry.Detail(id, key, text, ToLevel(level)));

        public static void Error(string id, string text) => Safe(() => Registry.Error(id, text));

        public static void Installed(string id, string feature) => Safe(() => Registry.Installed(id, feature));

        public static void SetLog(string id, string path) => Safe(() => Registry.SetLog(id, path));

        static StatusLevel ToLevel(int level) => level <= 0 ? StatusLevel.Ok : level >= 3 ? StatusLevel.Error : (StatusLevel)level;

        static void Safe(Action action)
        {
            try
            {
                action();
            }
            catch (Exception)
            {
                // never let a status report take the caller down
            }
        }
    }
}
