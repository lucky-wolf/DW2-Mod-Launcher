using System;
using System.Collections.Generic;
using System.Linq;

namespace DW2ModLauncher.Loader
{
    // Which mods are in this game session (docs/DLL Injection.md, "Is a mod loaded?").
    //
    // "Loaded" means enabled in the launcher for this launch: the mod is in the manifest the launcher wrote, code or data only. Whether a
    // code mod's own entry point then ran cleanly is its status line's business (ModStatus), not this list's. The list is complete before the
    // first mod's entry point runs, so it does not depend on load order, and it needs no channel from the other mod.
    //
    // A question that cannot be answered is an error, not "no": a blank name, or a uid or Workshop id that is not of the right form, throws
    // ArgumentException, so a typo in the caller never reads as "that mod is not installed".
    //
    // Instance-based so it can be unit tested; LoadedMods is the single shared instance mods reach by reflection.
    public sealed class LoadedModList
    {
        sealed class Mod
        {
            public string Name;
            public string Uid;
            public string WorkshopId;
        }

        readonly List<Mod> _mods = new List<Mod>();

        public LoadedModList(IEnumerable<LoaderManifestInstalledMod> installed = null)
        {
            foreach (LoaderManifestInstalledMod m in installed ?? Enumerable.Empty<LoaderManifestInstalledMod>())
                _mods.Add(new Mod { Name = m.Name ?? "", Uid = m.Uid ?? "", WorkshopId = m.WorkshopId ?? "" });
        }

        /// <summary>Whether an enabled mod has this display name (the launcher's name for it; case does not matter).</summary>
        public bool IsLoaded(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A mod name is required.", nameof(name));
            string wanted = name.Trim();
            return _mods.Any(m => string.Equals(m.Name, wanted, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Whether an enabled mod has this uid (dw2modlauncher.json "uid", a UUID in any of the usual spellings).</summary>
        public bool IsLoadedByUid(string uid)
        {
            string wanted = CanonicalUid(uid);
            return _mods.Any(m => m.Uid == wanted);
        }

        /// <summary>Whether an enabled mod has this Steam Workshop id (digits only; mod.json "workshopId", or the id of the Workshop folder).</summary>
        public bool IsLoadedByWorkshopId(string workshopId)
        {
            if (string.IsNullOrWhiteSpace(workshopId) || !workshopId.Trim().All(char.IsAsciiDigit))
                throw new ArgumentException("A Workshop id is digits only, got '" + workshopId + "'.", nameof(workshopId));
            string wanted = workshopId.Trim();
            return _mods.Any(m => m.WorkshopId == wanted);
        }

        /// <summary>The display names of the enabled mods, in load order.</summary>
        public string[] Names() => _mods.Select(m => m.Name).ToArray();

        /// <summary>The uids of the enabled mods that have one, in load order.</summary>
        public string[] Uids() => _mods.Where(m => m.Uid.Length > 0).Select(m => m.Uid).ToArray();

        // The form the launcher writes: lower case, hyphenated.
        static string CanonicalUid(string uid)
        {
            if (!Guid.TryParse(uid, out Guid parsed)) throw new ArgumentException("A mod uid is a UUID, got '" + uid + "'.", nameof(uid));
            return parsed.ToString("D");
        }
    }

    // The shared list, with plain static methods and BCL-only signatures so mods bind to them by reflection without referencing this assembly,
    // as they do LiveChannels.
    public static class LoadedMods
    {
        static LoadedModList _list = new LoadedModList();

        /// <summary>Called once by the loader, before any mod's entry point.</summary>
        internal static void Install(IEnumerable<LoaderManifestInstalledMod> installed) => _list = new LoadedModList(installed);

        public static bool IsLoaded(string name) => _list.IsLoaded(name);

        public static bool IsLoadedByUid(string uid) => _list.IsLoadedByUid(uid);

        public static bool IsLoadedByWorkshopId(string workshopId) => _list.IsLoadedByWorkshopId(workshopId);

        public static string[] Names() => _list.Names();

        public static string[] Uids() => _list.Uids();
    }
}
