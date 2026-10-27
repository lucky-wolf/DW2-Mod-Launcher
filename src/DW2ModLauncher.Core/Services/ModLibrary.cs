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
                if (old.AuthorFromSteam && string.IsNullOrWhiteSpace(mod.Author)) { mod.Author = old.Author; mod.AuthorFromSteam = true; }
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
        public static SetEnabledResult SetEnabled(ModInfo mod, bool enabled, LauncherSettings settings, ModOrderState order, IList<string> listOrder = null)
        {
            settings.SelectedMods[mod.Key] = enabled;
            SetEnabledResult result = SaveOrderSelection(mod, enabled, settings, order, listOrder);
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

        /// <summary>The outcome of putting the enabled mods in the order their dependency rules ask for.</summary>
        public class DependencyOrderResult
        {
            /// <summary>One line for each rule the order broke, which the new order now meets.</summary>
            public List<string> Moves = new List<string>();
            /// <summary>Why the order could not be changed or written, or empty.</summary>
            public List<string> Problems = new List<string>();
            public bool Written;
        }

        /// <summary>
        /// Puts the enabled mods (in load order) in the order their loadAfter / loadBefore rules ask for and writes it to DW2's mods.json, so the
        /// game and the loader agree. Each enabled mod keeps a place that an enabled mod already held; the order changes only as far as the rules need.
        /// On success the order state is updated in place.
        /// </summary>
        public static DependencyOrderResult ApplyDependencyOrder(IList<ModInfo> orderedEnabled, LauncherSettings settings, ModOrderState order, Func<string, string> t)
        {
            DependencyOrderResult result = new DependencyOrderResult();
            ModDependencies.OrderResult wanted = ModDependencies.Enforce(orderedEnabled, t);
            if (wanted.Cycle.Count > 0) result.Problems.Add(NamedText.Fill(t("LoadOrderRulesContradict"), ("Mods", string.Join(t("ListSeparator"), wanted.Cycle.Select(m => m.DisplayName ?? m.Id)))));
            bool reordered = !wanted.Order.SequenceEqual(orderedEnabled);
            if (!reordered) return result;
            if (order.ReadFailed || !order.FileFound) { result.Problems.Add(t("LoadOrderNotFixedUnreadable")); return result; }
            if (GameProcess.IsRunning(settings.GameRoot)) { result.Problems.Add(t("LoadOrderNotFixedGameRunning")); return result; }
            // the slots the enabled mods hold in mods.json, filled in the new order
            HashSet<string> tokens = new HashSet<string>(wanted.Order.Select(m => m.ActiveToken), StringComparer.OrdinalIgnoreCase);
            Queue<string> sorted = new Queue<string>(wanted.Order.Select(m => m.ActiveToken));
            List<string> rewritten = order.Order.Select(token => tokens.Contains(token) ? sorted.Dequeue() : token).ToList();
            try
            {
                List<string> written = ModOrderStore.Write(ModOrderStore.PathFor(settings.ManagedModsRoot, settings.GameRoot), rewritten);
                if (written == null) { result.Problems.Add(t("LoadOrderNotFixedNoFolder")); return result; }
                order.Order = written;
                result.Moves.AddRange(wanted.Moves);
                result.Written = true;
            }
            catch (Exception ex)
            {
                Diagnostics.Logger.LogException("Write mods.json for dependency order", ex);
                result.Problems.Add(NamedText.Fill(t("LoadOrderNotFixedFailed"), ("Problem", ex.Message)));
            }
            return result;
        }

        /// <param name="listOrder">The enabled mods (including this one when enabling) as the list shows them, top to bottom. When given, an enabled mod
        /// takes the place its row holds in the list (e.g. dragged there while the game was running) instead of going to the end.</param>
        public static SetEnabledResult SaveOrderSelection(ModInfo mod, bool enabled, LauncherSettings settings, ModOrderState order, IList<string> listOrder = null)
        {
            if (order.ReadFailed) return new SetEnabledResult { Outcome = SetEnabledOutcome.ModsJsonInvalid };
            if (GameProcess.IsRunning(settings.GameRoot)) return new SetEnabledResult { Outcome = SetEnabledOutcome.GameRunning };
            try
            {
                List<string> next = ModOrderStore.WithEnabled(order.Order, mod.ActiveToken, enabled);
                if (enabled && listOrder != null) next = ModOrderStore.Reorder(next, listOrder);
                List<string> written = ModOrderStore.Write(ModOrderStore.PathFor(settings.ManagedModsRoot, settings.GameRoot), next);
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
