using System.Collections.Generic;

namespace DW2ModLauncher.Core.Models
{
    // The shapes of the two hook files (docs/plans/hook-manifest.md). Property names are matched case-insensitively when read.

    /// <summary>The code hooks a mod declares in dw2modlauncher.hooks.json (generated beside the mod, or written by hand).</summary>
    public class HookManifest
    {
        public int Schema { get; set; }
        /// <summary>The mod version the manifest was written for; a different installed version marks it stale.</summary>
        public string ModVersion { get; set; }
        public string GameVersion { get; set; }
        public List<DeclaredHook> Hooks { get; set; } = new List<DeclaredHook>();
        public List<DeclaredChannel> Channels { get; set; } = new List<DeclaredChannel>();
    }

    public class DeclaredHook
    {
        /// <summary>Harmony's spelling, Namespace.Type:Method(argtypes).</summary>
        public string Target { get; set; }
        /// <summary>prefix, postfix, transpiler or finalizer.</summary>
        public string Kind { get; set; }
        /// <summary>observes, changes-result, changes-args, skips-original or rewrites; see <see cref="Services.HookEffects"/>.</summary>
        public string Effect { get; set; }
        public string Touches { get; set; }
        public string Why { get; set; }
        public string Feature { get; set; }
        /// <summary>The settings key that switches the hook on; with the setting off the hook is not counted.</summary>
        public string Setting { get; set; }
    }

    public class DeclaredChannel
    {
        public string Name { get; set; }
        /// <summary>provides or consumes.</summary>
        public string Role { get; set; }
        public int Version { get; set; }
    }

    /// <summary>What the loader saw patched in the last game session (hooks-observed.json, written by DW2ModLauncher.Loader.ObservedHooks).</summary>
    public class ObservedHooksFile
    {
        public int Schema { get; set; }
        public string CapturedAt { get; set; }
        public string GameVersion { get; set; }
        /// <summary>The code mods that were loaded, with the versions they had.</summary>
        public List<ObservedModInfo> Mods { get; set; } = new List<ObservedModInfo>();
        public List<ObservedHookInfo> Hooks { get; set; } = new List<ObservedHookInfo>();
        public ObservedChannelsInfo Channels { get; set; } = new ObservedChannelsInfo();
    }

    public class ObservedModInfo
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public string Version { get; set; }
    }

    public class ObservedHookInfo
    {
        public string Target { get; set; }
        public List<ObservedPatchInfo> Patches { get; set; } = new List<ObservedPatchInfo>();
    }

    public class ObservedPatchInfo
    {
        /// <summary>A mod's <see cref="ModInfo.Key"/>, "launcher" for the loader's own patches, or "unknown".</summary>
        public string Owner { get; set; }
        public string Assembly { get; set; }
        public string HarmonyId { get; set; }
        public string Kind { get; set; }
        public string Effect { get; set; }
        public int Priority { get; set; }
        public List<string> Before { get; set; } = new List<string>();
        public List<string> After { get; set; } = new List<string>();
        public string Method { get; set; }
    }

    public class ObservedChannelsInfo
    {
        public List<ObservedChannelProvisionInfo> Provided { get; set; } = new List<ObservedChannelProvisionInfo>();
        public List<ObservedChannelUseInfo> Consumed { get; set; } = new List<ObservedChannelUseInfo>();
        public List<string> Problems { get; set; } = new List<string>();
    }

    public class ObservedChannelProvisionInfo
    {
        public string Channel { get; set; }
        public string Provider { get; set; }
        /// <summary>The <see cref="ModInfo.Key"/> of the mod the provider's assembly came from (null when unknown, or recorded by an older loader).</summary>
        public string ProviderKey { get; set; }
        public int Version { get; set; }
        public string DelegateType { get; set; }
    }

    public class ObservedChannelUseInfo
    {
        public string Channel { get; set; }
        public string Consumer { get; set; }
        /// <summary>As <see cref="ObservedChannelProvisionInfo.ProviderKey"/>, for the consumer.</summary>
        public string ConsumerKey { get; set; }
        public int Version { get; set; }
        public string DelegateType { get; set; }
    }
}
