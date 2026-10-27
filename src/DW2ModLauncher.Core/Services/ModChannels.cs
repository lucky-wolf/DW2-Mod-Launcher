using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DW2ModLauncher.Core.Models;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>
    /// Data one mod supplies to another mod's DLL over a named channel. A DLL declares the channels it "receives" in its dw2modlauncher.json;
    /// any mod can "provide" files to a channel (a slot name, and the path of the mod's file for it). The launcher reads the provided files
    /// itself, merges every provider's file for a slot in load order, and hands the DLL the combined text, so a later mod can add to or
    /// override an earlier one's data without replacing it. Nothing in the receiving mod names a provider. Pure logic; no UI.
    ///
    /// Merging: a slot file whose name ends in ".json" is merged as a JSON merge patch (RFC 7396): objects merge key by key, a later value
    /// replaces an earlier one (arrays included), and null removes the key. Comments and trailing commas are allowed in provided files; the
    /// merged text has none. Any other slot's file is replaced as a whole by the later provider.
    /// </summary>
    public static class ModChannels
    {
        /// <summary>The largest file read from a provider; a bigger one is reported and left out.</summary>
        public const int MaxFileBytes = 1024 * 1024;

        private static readonly JsonDocumentOptions Lenient = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

        private static string Name(ModInfo mod) { return mod.DisplayName ?? mod.Id; }

        /// <summary>The lookup for callers that only want the merged data: the problems Merge finds are collected and thrown away, so their wording does not matter.</summary>
        private static readonly Func<string, string> ProblemsDiscarded = key => key;

        private static bool IsJson(string slot) { return slot.EndsWith(".json", StringComparison.OrdinalIgnoreCase); }

        /// <summary>One slot of a channel as merged: the text, and which mods supplied it in load order.</summary>
        private class Slot
        {
            public JsonNode Json;
            public string Text;
            public List<string> Sources = new List<string>();
        }

        /// <summary>
        /// The files the mod's DLL receives, keyed "channel/slot", for each channel it receives: every enabled provider's files merged in the
        /// order given (load order). Problems with a provider's files are reported by Problems and leave that file out.
        /// </summary>
        public static Dictionary<string, string> FilesFor(ModInfo receiver, IList<ModInfo> orderedEnabled)
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string channel in (receiver.Receives ?? new List<string>()).Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
                foreach (KeyValuePair<string, Slot> slot in Merge(orderedEnabled, channel, new List<string>(), ProblemsDiscarded))
                    result[channel + "/" + slot.Key] = slot.Value.Json != null ? slot.Value.Json.ToJsonString() : slot.Value.Text;
            return result;
        }

        /// <summary>One line per merged slot saying which mods supplied it, for the launcher's log: "coherent-research.profiles/x.json: XL, Terran".</summary>
        public static List<string> Describe(IList<ModInfo> orderedEnabled)
        {
            List<string> lines = new List<string>();
            foreach (string channel in ChannelsProvided(orderedEnabled))
                foreach (KeyValuePair<string, Slot> slot in Merge(orderedEnabled, channel, new List<string>(), ProblemsDiscarded))
                    lines.Add(channel + "/" + slot.Key + ": " + string.Join(", ", slot.Value.Sources));
            return lines;
        }

        /// <summary>Why a provider's files cannot be handed over, for every channel any enabled mod provides.</summary>
        public static List<string> Problems(IList<ModInfo> orderedEnabled, Func<string, string> t)
        {
            List<string> problems = new List<string>();
            foreach (ModInfo mod in orderedEnabled)
                foreach (LauncherProvision provision in mod.Provides ?? new List<LauncherProvision>())
                {
                    if (provision == null || string.IsNullOrWhiteSpace(provision.channel)) problems.Add("⚠ " + NamedText.Fill(t("ChannelProvisionNoChannel"), ("Mod", Name(mod))));
                    else if (provision.files == null || provision.files.Count == 0) problems.Add("⚠ " + NamedText.Fill(t("ChannelProvisionNoFiles"), ("Mod", Name(mod)), ("Channel", provision.channel)));
                }
            foreach (string channel in ChannelsProvided(orderedEnabled))
                Merge(orderedEnabled, channel, problems, t);
            return problems;
        }

        private static List<string> ChannelsProvided(IList<ModInfo> orderedEnabled)
        {
            return orderedEnabled.SelectMany(m => m.Provides ?? new List<LauncherProvision>())
                .Where(p => p != null && !string.IsNullOrWhiteSpace(p.channel))
                .Select(p => p.channel.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static Dictionary<string, Slot> Merge(IList<ModInfo> orderedEnabled, string channel, List<string> problems, Func<string, string> t)
        {
            Dictionary<string, Slot> slots = new Dictionary<string, Slot>(StringComparer.OrdinalIgnoreCase);
            foreach (ModInfo provider in orderedEnabled)
                foreach (LauncherProvision provision in provider.Provides ?? new List<LauncherProvision>())
                {
                    if (provision == null || provision.files == null || !string.Equals(provision.channel?.Trim(), channel, StringComparison.OrdinalIgnoreCase)) continue;
                    foreach (KeyValuePair<string, string> file in provision.files)
                    {
                        string slotName = (file.Key ?? "").Trim();
                        if (slotName.Length == 0) { problems.Add("⚠ " + NamedText.Fill(t("ChannelSlotNoName"), ("Mod", Name(provider)), ("Channel", channel))); continue; }
                        string text = ReadFile(provider, channel, slotName, file.Value, problems, t);
                        if (text == null) continue;
                        if (!slots.TryGetValue(slotName, out Slot slot)) { slot = new Slot(); slots[slotName] = slot; }
                        if (IsJson(slotName)) MergeJson(provider, channel, slotName, text, slot, problems, t);
                        else slot.Text = text;
                        if (slot.Json != null || slot.Text != null) slot.Sources.Add(Name(provider));
                    }
                }
            // a slot whose every file failed has nothing to hand over
            foreach (string empty in slots.Where(kv => kv.Value.Json == null && kv.Value.Text == null).Select(kv => kv.Key).ToList()) slots.Remove(empty);
            return slots;
        }

        private static void MergeJson(ModInfo provider, string channel, string slotName, string text, Slot slot, List<string> problems, Func<string, string> t)
        {
            JsonNode patch;
            try { patch = JsonNode.Parse(text, null, Lenient); }
            catch (JsonException ex) { problems.Add("⚠ " + NamedText.Fill(t("ChannelNotValidJson"), ("Mod", Name(provider)), ("Channel", channel), ("Slot", slotName), ("Problem", ex.Message))); return; }
            if (slot.Json != null && patch is JsonObject patchObject && slot.Json is JsonObject known
                && patchObject["version"] != null && known["version"] != null && patchObject["version"].ToJsonString() != known["version"].ToJsonString())
                problems.Add("⚠ " + NamedText.Fill(t("ChannelVersionMismatch"), ("Channel", channel), ("Slot", slotName), ("Mod", Name(provider)), ("Version", patchObject["version"].ToJsonString()),
                    ("Others", string.Join(t("ListSeparator"), slot.Sources)), ("Known", known["version"].ToJsonString())));
            slot.Json = MergePatch(slot.Json, patch);
        }

        /// <summary>RFC 7396: a patch object merges into the target key by key, null removes a key, anything else replaces.</summary>
        public static JsonNode MergePatch(JsonNode target, JsonNode patch)
        {
            if (!(patch is JsonObject patchObject)) return patch?.DeepClone();
            JsonObject merged = target is JsonObject existing ? existing : new JsonObject();
            foreach (KeyValuePair<string, JsonNode> member in patchObject.ToList())
            {
                if (member.Value == null) { merged.Remove(member.Key); continue; }
                merged[member.Key] = MergePatch(merged[member.Key]?.DeepClone(), member.Value);
            }
            return merged;
        }

        /// <summary>The text of a provider's file. A path that is absolute, leaves the mod's folder, is a link, is missing, is too big or is not UTF-8 text is reported and gives null.</summary>
        private static string ReadFile(ModInfo provider, string channel, string slot, string path, List<string> problems, Func<string, string> t)
        {
            string reasonKey = null;
            (string, object)[] reasonValues = new (string, object)[0];
            string root = provider.ContentRoot ?? provider.Folder;
            if (string.IsNullOrWhiteSpace(path)) reasonKey = "ChannelCannotSupplyNoPath";
            else if (string.IsNullOrWhiteSpace(root)) reasonKey = "ChannelCannotSupplyNoFolder";
            else if (Path.IsPathRooted(path.Trim()) || path.Trim().Split('/', '\\').Contains("..")) reasonKey = "ChannelCannotSupplyOutside";
            else
            {
                string rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string full = Path.GetFullPath(Path.Combine(rootFull, path.Trim().Replace('/', Path.DirectorySeparatorChar)));
                FileInfo info = new FileInfo(full);
                if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) reasonKey = "ChannelCannotSupplyOutside";
                else if (!info.Exists) reasonKey = "ChannelCannotSupplyMissing";
                else if (info.LinkTarget != null) reasonKey = "ChannelCannotSupplyLink";
                else if (info.Length > MaxFileBytes) { reasonKey = "ChannelCannotSupplyTooBig"; reasonValues = new (string, object)[] { ("Kilobytes", MaxFileBytes / 1024) }; }
                else
                {
                    try { return new UTF8Encoding(false, true).GetString(File.ReadAllBytes(full)); }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is DecoderFallbackException)
                    { reasonKey = "ChannelCannotSupplyUnreadable"; reasonValues = new (string, object)[] { ("Problem", ex.Message) }; }
                }
            }
            List<(string Name, object Value)> values = new List<(string Name, object Value)> { ("Mod", Name(provider)), ("Channel", channel), ("Slot", slot), ("Path", path) };
            foreach ((string, object) extra in reasonValues) values.Add(extra);
            problems.Add("⚠ " + NamedText.Fill(t(reasonKey), values.ToArray()));
            return null;
        }
    }
}
