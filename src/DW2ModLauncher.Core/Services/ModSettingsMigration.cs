using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using DW2ModLauncher.Core.Diagnostics;
using DW2ModLauncher.Core.Models;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>
    /// Carries a player's setting from the mod that used to have it to the mod that has it now (docs/Settings Migration.md). Both mods say so in their own settings.schema.json:
    /// the giver lists the key in "_moved" with the receiver's uid and new key, the receiver lists the giver's uid and old key in the field's "movedFrom". The value moves only
    /// when both agree, so no mod can pull another's values by naming it. A declaration is a fact about where the setting lives, not an event, so it holds across any number of
    /// skipped versions; it runs once because the value is taken out of the giver's set-aside file, and only fills a setting still at its default, so a choice the player already
    /// made in the receiver is never overwritten.
    /// </summary>
    public static class ModSettingsMigration
    {
        public class Result
        {
            /// <summary>Settings that came over, as "receiver: key from giver", for the launcher's log.</summary>
            public List<string> Moved = new List<string>();
            /// <summary>What the player should hear about: a mod's schema or settings file that could not be read, or a value that does not fit where it moved to.</summary>
            public List<string> Problems = new List<string>();
        }

        private class Participant
        {
            public ModInfo Mod;
            public ModSettingsSchema Schema;
        }

        /// <summary>
        /// Sets aside what every installed mod gave away, then lets each mod for which <paramref name="takesSettings"/> is true (the enabled ones) take what it was given.
        /// Nothing is written for a mod that is not enabled, other than the giver's set-aside values.
        /// </summary>
        public static Result Run(IEnumerable<ModInfo> mods, Func<ModInfo, bool> takesSettings, Func<string, string> t)
        {
            Result result = new Result();
            List<Participant> entries = new List<Participant>();
            foreach (ModInfo mod in mods ?? Enumerable.Empty<ModInfo>())
            {
                if (mod == null) continue;
                try
                {
                    ModSettingsSchema schema = ModSettingsSchemaReader.Read(mod.ContentRoot ?? mod.Folder);
                    if (schema != null) entries.Add(new Participant { Mod = mod, Schema = schema });
                }
                catch (Exception ex)
                {
                    // the player hears about a broken schema of an enabled mod when its settings are synced (and when its settings dialog opens), so this is only logged
                    Logger.LogException("Settings migration: read schema of " + Name(mod), ex);
                }
            }

            foreach (Participant giver in entries.Where(e => e.Schema.Moved.Count > 0))
            {
                try { ModSettingsStore.RetireMoved(giver.Mod, giver.Schema); }
                catch (Exception ex)
                {
                    Logger.LogException("Settings migration: set aside values of " + Name(giver.Mod), ex);
                    result.Problems.Add(NamedText.Fill(t("ModSettingsNotPrepared"), ("Mod", Name(giver.Mod)), ("Problem", ex.Message)));
                }
            }

            // a copy of a mod from the Workshop and a local one share a uid, so a uid can stand for several installed folders
            Dictionary<string, List<Participant>> byUid = new Dictionary<string, List<Participant>>(StringComparer.Ordinal);
            foreach (Participant entry in entries.Where(e => e.Mod.Uid != null))
            {
                if (!byUid.TryGetValue(entry.Mod.Uid, out List<Participant> same)) byUid[entry.Mod.Uid] = same = new List<Participant>();
                same.Add(entry);
            }

            foreach (Participant receiver in entries.Where(e => e.Mod.Uid != null && takesSettings(e.Mod)))
            {
                foreach (ModSettingsField field in receiver.Schema.Fields.Where(f => f.MovedFrom != null && f.MovedFrom.Count > 0))
                {
                    try { Take(receiver, field, byUid, result, t); }
                    catch (Exception ex)
                    {
                        Logger.LogException("Settings migration: " + Name(receiver.Mod) + " " + field.Key, ex);
                        result.Problems.Add(NamedText.Fill(t("ModSettingsNotPrepared"), ("Mod", Name(receiver.Mod)), ("Problem", ex.Message)));
                    }
                }
            }
            foreach (string line in result.Moved) Logger.Log("Settings migration", line);
            return result;
        }

        private static void Take(Participant receiver, ModSettingsField field, Dictionary<string, List<Participant>> byUid, Result result, Func<string, string> t)
        {
            // every giver that confirms the move gives up its value, so none lingers to be taken at some later time; the first source listed is the one that counts
            List<(Participant Giver, JsonNode Value)> found = new List<(Participant, JsonNode)>();
            foreach (ModSettingMoveSource source in field.MovedFrom)
            {
                if (!byUid.TryGetValue(source.Mod, out List<Participant> givers)) continue;
                foreach (Participant giver in givers)
                {
                    if (!giver.Schema.Moved.TryGetValue(source.Key, out ModSettingMoveTarget target)) continue;
                    if (!ModUid.Same(target.Mod, receiver.Mod.Uid) || !string.Equals(target.Key, field.Key, StringComparison.Ordinal)) continue;
                    JsonNode value = ModSettingsStore.TakeRetired(giver.Mod, source.Key);
                    if (value != null) found.Add((giver, value));
                }
            }
            if (found.Count == 0) return;

            JsonObject values = ModSettingsStore.GetOrCreateValues(receiver.Mod, receiver.Schema);
            bool atDefault = !values.TryGetPropertyValue(field.Key, out JsonNode current) || ModSettingsStore.SameValue(current, ModSettingsStore.ToJsonNode(field.Default));
            if (!atDefault) return; // the player already chose here; the old value is spent

            (Participant from, JsonNode old) = found[0];
            string problemKey = Fit(field, old, out JsonNode fitted);
            if (problemKey != null)
            {
                result.Problems.Add(NamedText.Fill(t(problemKey), ("Mod", Name(receiver.Mod)), ("Key", field.Key), ("From", Name(from.Mod)), ("Value", old.ToJsonString())));
                return;
            }
            values[field.Key] = fitted;
            ModSettingsStore.SaveValues(receiver.Mod, values);
            result.Moved.Add(Name(receiver.Mod) + ": " + field.Key + " = " + fitted.ToJsonString() + ", from " + Name(from.Mod));
        }

        /// <summary>Whether a value from elsewhere is one this setting takes; null when it is (with the value as the setting stores it), else the language key of what is wrong.</summary>
        private static string Fit(ModSettingsField field, JsonNode value, out JsonNode fitted)
        {
            fitted = null;
            JsonValue scalar = value as JsonValue;
            if (scalar == null) return "SettingMoveWrongType";
            System.Text.Json.JsonValueKind kind = scalar.GetValueKind();
            switch (ModSettingsValues.KindOf(field))
            {
                case ModSettingKind.Bool:
                    if (kind != System.Text.Json.JsonValueKind.True && kind != System.Text.Json.JsonValueKind.False) return "SettingMoveWrongType";
                    fitted = value.DeepClone();
                    return null;
                case ModSettingKind.Integer:
                case ModSettingKind.Number:
                    if (kind != System.Text.Json.JsonValueKind.Number) return "SettingMoveWrongType";
                    if (!decimal.TryParse(value.ToJsonString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out decimal number)) return "SettingMoveWrongType";
                    if (ModSettingsValues.KindOf(field) == ModSettingKind.Integer && number != decimal.Truncate(number)) return "SettingMoveWrongType";
                    if (number < ModSettingsValues.MinOf(field) || number > ModSettingsValues.MaxOf(field)) return "SettingMoveOutOfRange";
                    fitted = value.DeepClone();
                    return null;
                case ModSettingKind.Choice:
                    if (kind != System.Text.Json.JsonValueKind.String) return "SettingMoveWrongType";
                    string option = field.Options.Find(o => string.Equals(o, scalar.GetValue<string>(), StringComparison.OrdinalIgnoreCase));
                    if (option == null) return "SettingMoveNotAnOption";
                    fitted = JsonValue.Create(option);
                    return null;
                default:
                    if (kind != System.Text.Json.JsonValueKind.String) return "SettingMoveWrongType";
                    fitted = value.DeepClone();
                    return null;
            }
        }

        private static string Name(ModInfo mod)
        {
            return mod.DisplayName ?? mod.Id ?? Path.GetFileName(mod.Folder);
        }
    }
}
