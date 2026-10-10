using System;
using System.Collections.Generic;
using System.Linq;
using DW2ModLauncher.Core.Models;

namespace DW2ModLauncher.Core.Services
{
    public enum SetEnabledOutcome
    {
        Saved,
        /// <summary>mods.json couldn't be parsed; the launcher refuses to overwrite it.</summary>
        ModsJsonInvalid,
        GameRunning,
        /// <summary>There is no mods folder to write to; nothing was saved to mods.json.</summary>
        NoModsFolder,
        /// <summary>Writing mods.json failed; see Error.</summary>
        WriteFailed
    }

    public class SetEnabledResult
    {
        public SetEnabledOutcome Outcome { get; set; }
        public Exception Error { get; set; }
        /// <summary>The mod was enabled, but creating or completing its settings file failed (a broken schema or settings file).</summary>
        public Exception SettingsError { get; set; }
    }

    /// <summary>Scanning, ordering and enabling of the installed mods. No UI.</summary>
    public static class ModLibrary
    {
        /// <summary>Carries runtime-only Workshop data (update state, remote details) from the previous scan onto a fresh one.</summary>
        public static void RestoreWorkshopRuntimeState(List<ModInfo> scanned, Dictionary<string, ModInfo> previous)
        {
            if (scanned == null || previous == null || previous.Count == 0) return;
            foreach (ModInfo mod in scanned)
            {
                ModInfo old;
                if (mod == null || string.IsNullOrWhiteSpace(mod.Id) || !previous.TryGetValue(mod.Id, out old) || old == null) continue;
                mod.UpdateState = old.UpdateState;
                mod.LocalWorkshopTimeUpdated = old.LocalWorkshopTimeUpdated;
                mod.RemoteWorkshopTimeUpdated = old.RemoteWorkshopTimeUpdated;
                mod.WorkshopDescription = old.WorkshopDescription;
                mod.WorkshopTitle = old.WorkshopTitle;
                mod.WorkshopPreviewUrl = old.WorkshopPreviewUrl;
                mod.WorkshopCreator = old.WorkshopCreator;
                mod.WorkshopFileSize = old.WorkshopFileSize;
                mod.WorkshopTimeCreated = old.WorkshopTimeCreated;
                mod.WorkshopTags = old.WorkshopTags;
                if (!string.IsNullOrWhiteSpace(old.WorkshopTitle)) mod.DisplayName = old.DisplayName;
                if (!string.IsNullOrWhiteSpace(old.WorkshopDescription)) mod.Description = old.Description;
            }
        }

        public static Dictionary<string, ModInfo> IndexWorkshopById(IEnumerable<ModInfo> workshopMods)
        {
            return (workshopMods ?? new List<ModInfo>())
                .Where(m => m != null && !string.IsNullOrWhiteSpace(m.Id))
                .GroupBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Enabled mods first in load order, then the rest by name.</summary>
        public static List<ModInfo> OrderForDisplay(IEnumerable<ModInfo> mods, ModOrderState order)
        {
            return (mods ?? new List<ModInfo>()).OrderBy(m =>
            {
                int index = order.IndexOf(m.ActiveToken);
                return index < 0 ? int.MaxValue : index;
            }).ThenBy(m => m.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        /// <summary>
        /// Records the choice in the launcher's own settings (the caller saves them) and in DW2's mods.json.
        /// On success the order state is updated in place.
        /// </summary>
        public static SetEnabledResult SetEnabled(ModInfo mod, bool enabled, LauncherSettings settings, ModOrderState order)
        {
            settings.SelectedMods[mod.Key] = enabled;
            SetEnabledResult result = SaveOrderSelection(mod, enabled, settings, order);
            // An enabled mod always has a settings file holding every schema key (missing ones at their defaults).
            if (enabled && result.Outcome == SetEnabledOutcome.Saved)
            {
                try
                {
                    ModSettingsSchema schema = ModSettingsSchemaReader.Read(mod.ContentRoot ?? mod.Folder);
                    if (schema != null) ModSettingsStore.GetOrCreateValues(mod, schema);
                }
                catch (Exception ex)
                {
                    Diagnostics.Logger.LogException("Create settings for enabled mod: " + mod.DisplayName, ex);
                    result.SettingsError = ex;
                }
            }
            return result;
        }

        public static SetEnabledResult SaveOrderSelection(ModInfo mod, bool enabled, LauncherSettings settings, ModOrderState order)
        {
            if (order.ReadFailed) return new SetEnabledResult { Outcome = SetEnabledOutcome.ModsJsonInvalid };
            if (GameProcess.IsRunning()) return new SetEnabledResult { Outcome = SetEnabledOutcome.GameRunning };
            try
            {
                List<string> written = ModOrderStore.Write(ModOrderStore.PathFor(settings.ManagedModsRoot, settings.GameRoot),
                    ModOrderStore.WithEnabled(order.Order, mod.ActiveToken, enabled));
                if (written == null) return new SetEnabledResult { Outcome = SetEnabledOutcome.NoModsFolder };
                order.Order = written;
                order.FileFound = true;
                return new SetEnabledResult { Outcome = SetEnabledOutcome.Saved };
            }
            catch (Exception ex)
            {
                return new SetEnabledResult { Outcome = SetEnabledOutcome.WriteFailed, Error = ex };
            }
        }
    }
}
