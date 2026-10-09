using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace DW2ModLauncher.Loader
{
    /// <summary>
    /// The game finds tour items and Galactopedia articles by title, and the title it looks for is a translated one
    /// (<c>GetByTitle(GetText("Construction"))</c>), while the data's own title (the tour's Title, the article's file name) stays
    /// English in a translation that patches the data. When the lookup finds nothing, this retries with the data's titles translated
    /// the same way, so a translation does not have to rename them. Exact matches behave as before.
    /// </summary>
    public static class TitleLookupHooks
    {
        private static MethodInfo _getText;
        private static string _logPath;
        private static readonly System.Collections.Generic.HashSet<string> Logged = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);

        public static int Install(string baseDir)
        {
            _logPath = Path.Combine(baseDir, "textfiles.log");
            Type resolver = Type.GetType("DistantWorlds.Types.TextResolver, DistantWorlds.Types", false);
            _getText = resolver?.GetMethod("GetText", new[] { typeof(string) });
            if (_getText == null) { Log("TextResolver.GetText not found; titles are not matched by translation."); HookStatus.Warn("titles", "translated tour/article titles are not matched: TextResolver.GetText not found", _logPath); return 0; }

            Harmony harmony = new Harmony("dw2modlauncher.loader.titlelookup");
            int hooked = 0;
            foreach (string spec in new[] { "DistantWorlds.Types.TourItemList|GetByTitle|TourItem", "DistantWorlds.Types.GalactopediaTopicList|GetFirstByTitle|Galactopedia" })
            {
                string[] p = spec.Split('|');
                MethodInfo m = Type.GetType(p[0] + ", DistantWorlds.Types", false)?.GetMethod(p[1], new[] { typeof(string) });
                if (m == null) { Log(p[1] + " not found."); HookStatus.Warn("titles", "translated titles are not matched: " + p[1] + " not found (game update?)", _logPath); continue; }
                harmony.Patch(m, postfix: new HarmonyMethod(typeof(TitleLookupHooks).GetMethod(p[2] == "TourItem" ? nameof(TourPostfix) : nameof(ArticlePostfix), BindingFlags.Static | BindingFlags.NonPublic)));
                hooked++;
            }
            Log("title lookup by translation: " + hooked + " hook(s)");
            return hooked;
        }

        // Harmony binds the parameters below by name.
        private static void TourPostfix(object __instance, string title, ref object __result)
        {
            Log("tour lookup '" + title + "': " + (__result == null ? "not found" : "found"));
            if (__result == null) __result = Retry(__instance, title, "tour");
        }

        private static void ArticlePostfix(object __instance, string title, ref object __result)
        {
            Log("Galactopedia lookup '" + title + "': " + (__result == null ? "not found" : "found"));
            if (__result == null) __result = Retry(__instance, title, "Galactopedia article");
        }

        private static object Retry(object list, string title, string what)
        {
            try
            {
                if (title == null || !(list is IEnumerable items)) return null;
                foreach (object item in items)
                {
                    FieldInfo f = item?.GetType().GetField("Title");
                    string own = f?.GetValue(item) as string;
                    if (string.IsNullOrEmpty(own)) continue;
                    string translated = _getText.Invoke(null, new object[] { own }) as string;
                    if (!string.Equals(translated, title, StringComparison.Ordinal)) continue;
                    Log(what + " '" + title + "' found as '" + own + "' by its translation");
                    if (what == "tour") ForgetSeen(own);
                    return item;
                }
                Log("no " + what + " is titled '" + title + "'");
            }
            catch (Exception ex) { Log("title lookup failed for '" + title + "': " + ex.Message); }
            return null;
        }

        // The game keeps the titles of the tours already shown (the "TourItemsSeen" file) and a tour that was seen is not shown again; a
        // tour the player launches from its button first has its title removed from that list, but with the translated title the
        // game asks for, which is not the English one the list holds. So the English title is removed here, as the game meant to.
        private static void ForgetSeen(string title)
        {
            try
            {
                Type ui = Type.GetType("DistantWorlds.UI.UserInterfaceController, DistantWorlds.UI", false);
                System.Collections.Generic.List<string> seen = ui?.GetField("TourItemsSeen", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as System.Collections.Generic.List<string>;
                if (seen != null && seen.Remove(title)) Log("tour '" + title + "' taken off the list of tours already seen");
            }
            catch (Exception ex) { Log("could not update the seen tours: " + ex.Message); }
        }

        private static void Log(string message)
        {
            try
            {
                lock (Logged)
                {
                    if (_logPath == null || !Logged.Add(message)) return;
                    File.AppendAllText(_logPath, "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " + message + Environment.NewLine);
                }
            }
            catch
            {
                // Logging must never crash the game.
            }
        }
    }
}
