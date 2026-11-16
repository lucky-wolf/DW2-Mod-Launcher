using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DW2ModLauncher.Avalonia.Services;
using DW2ModLauncher.Core.Models;
using DW2ModLauncher.Core.Services;

namespace DW2ModLauncher.Avalonia.ViewModels
{
    /// <summary>
    /// One line of the "Compatible with" list: a hook of this mod that overlaps another mod's. Ticked means the author investigated it and it is
    /// marked compatible (a hook claim); the other columns say who it overlaps, how badly, and the method.
    /// </summary>
    public class CompatibleRow : ViewModelBase
    {
        /// <summary>The overlapping mod with its installed version, so the column says exactly which one.</summary>
        public string Partner { get; set; }
        /// <summary>The patched game method, short (Type:Method).</summary>
        public string Method { get; set; }
        public string Args { get; set; }
        public string Target { get; set; }
        /// <summary>How the overlap grades if it is not marked; Info (no icon) when nothing overlaps any more.</summary>
        public HookSeverity Severity { get; set; }
        public bool IsConflict { get { return Severity == HookSeverity.Conflict; } }
        public bool IsCaution { get { return Severity == HookSeverity.Caution; } }
        public string SeverityTooltip { get; set; }
        /// <summary>Everything that was seen: the method in full, both mods' patches, and what changed. Shown by the info icon's tooltip and copied by the copy button.</summary>
        public string Details { get; set; }
        /// <summary>The hook spelled out on one line (type, method and argument types without namespaces): the row's tooltip.</summary>
        public string Summary { get; set; }
        /// <summary>False when a side's patches are not known in full, so it cannot be marked yet.</summary>
        public bool CanCheck { get; set; } = true;
        /// <summary>False in a read-only list (someone else's mod): no tick is shown.</summary>
        public bool CanEdit { get; set; } = true;
        public string CheckTooltip { get; set; }
        /// <summary>The overlap as it is now, with the claim it would be marked with; null for a mark whose overlap is gone.</summary>
        public HookReviewItem Item { get; set; }
        /// <summary>The mark in the list, as made (null while unmarked).</summary>
        public HookClaim Claim { get; set; }
        /// <summary>A mark exists whose patches have changed since, so it counts as not investigated.</summary>
        public bool Changed { get; set; }
        internal Action<CompatibleRow, bool> Toggled { get; set; }

        private bool isChecked;
        public bool IsChecked
        {
            get { return isChecked; }
            set
            {
                if (!Set(ref isChecked, value)) return;
                Raise(nameof(Opacity));
                Toggled?.Invoke(this, value);
            }
        }

        /// <summary>Puts <see cref="Details"/> on the clipboard.</summary>
        public RelayCommand Copy { get; set; }
        public string CopyTooltip { get; set; }
        private bool copied;
        /// <summary>True for a moment after copying, so the icon can turn to a tick.</summary>
        public bool Copied { get { return copied; } set { if (Set(ref copied, value)) Raise(nameof(NotCopied)); } }
        public bool NotCopied { get { return !copied; } }

        /// <summary>Marked rows are dimmed: what still needs attention stands out.</summary>
        public double Opacity { get { return isChecked ? 0.6 : 1; } }

        private string note = "";
        /// <summary>What is wrong with the row (changed, not installed, no longer overlaps, not checked yet); empty when nothing is.</summary>
        public string Note { get { return note; } set { if (Set(ref note, value ?? "")) Raise(nameof(HasNote)); } }
        public bool HasNote { get { return note.Length > 0; } }

        /// <summary>Sets the tick without marking or unmarking anything (building the list).</summary>
        internal void SetCheckedQuietly(bool value) { isChecked = value; Raise(nameof(IsChecked)); Raise(nameof(Opacity)); }
    }

    /// <summary>
    /// The list of one mod's hooks that overlap another mod's, with a tick for each one its author investigated and marked compatible (hook claims in
    /// the mod's hooks file). Edited as a copy; <see cref="Commit"/> writes it. Used by the Publish and Edit Properties dialogs and by the
    /// dialog the details pane opens (docs/plans/hook-compatibility-claims.md).
    /// </summary>
    public class CompatibleListViewModel : ViewModelBase
    {
        private readonly IDialogService dialogs;
        private readonly ModInfo mod;
        private readonly List<ModInfo> installedMods;
        private readonly HookAnalysis hookAnalysis;
        private readonly List<HookClaim> compatibleClaims;
        private string savedClaimsKey;
        // why the mod's hooks file could not be read; nothing is written over it while this is set
        private readonly string claimsError;
        // someone else's mod (a Workshop copy): the list only shows what still clashes, with no ticks, and nothing is written
        private readonly bool readOnly;

        public LocalizedStrings L { get; }
        /// <summary>The list changed (a tick, a mark): whoever hosts it may want to refresh what depends on it.</summary>
        public event Action Changed;

        public CompatibleListViewModel(IDialogService dialogs, LocalizedStrings l, ModInfo mod, List<ModInfo> installedMods, HookAnalysis hookAnalysis, bool readOnly = false)
        {
            this.readOnly = readOnly;
            this.dialogs = dialogs;
            L = l;
            this.mod = mod;
            this.installedMods = installedMods;
            this.hookAnalysis = hookAnalysis;
            (HookManifest declared, string readError) = HookFiles.ReadManifest(mod, key => l[key]);
            claimsError = readError;
            compatibleClaims = new List<HookClaim>((declared?.Claims ?? new List<HookClaim>()).Where(c => c != null && !string.IsNullOrWhiteSpace(c.Target)));
            savedClaimsKey = Key;
        }

        /// <summary>The ticks work and Save writes the marks: the mod is the author's own, not a Workshop copy.</summary>
        public bool CanEdit { get { return !readOnly; } }
        /// <summary>The mod whose hooks these are.</summary>
        public ModInfo Mod { get { return mod; } }
        /// <summary>The marks differ from what the hooks file holds.</summary>
        public bool IsModified { get { return claimsError == null && Key != savedClaimsKey; } }

        /// <summary>Writes the marks into the mod's hooks file when they changed; returns what went wrong, or null. Nothing is written over a file that could not be read.</summary>
        public string Commit()
        {
            if (!IsModified) return null;
            try
            {
                HookFiles.WriteClaims(mod, compatibleClaims);
                savedClaimsKey = Key;
                return null;
            }
            catch (Exception ex)
            {
                DW2ModLauncher.Core.Diagnostics.Logger.LogException("Write dw2modlauncher.hooks.json claims", ex);
                return ex.Message;
            }
        }

        public string Header { get { return L["PublishCompatibleWith"] + ": " + CompatibleRows.Count(r => r.IsChecked); } }

        private ModInfo PartnerOf(HookClaim claim) { return ModDependencies.Find(installedMods, (claim.Partner ?? "").Trim()); }

        private bool Marks(HookClaim claim, string target, ModInfo partner)
        {
            return string.Equals(claim.Target, target, StringComparison.Ordinal) && PartnerOf(claim) == partner;
        }

        private string NameAndVersion(ModInfo m) { return (m.DisplayName ?? m.Id) + (string.IsNullOrWhiteSpace(m.Version) ? "" : "  " + m.Version.Trim()); }

        private List<CompatibleRow> compatibleRows;

        /// <summary>The list: every hook of this mod that overlaps another mod's, ticked when marked compatible, then the marks whose overlap has gone. Built once; ticking changes the marks.</summary>
        public List<CompatibleRow> CompatibleRows { get { return compatibleRows ?? (compatibleRows = BuildCompatibleRows()); } }


        private List<CompatibleRow> BuildCompatibleRows()
        {
            List<CompatibleRow> rows = new List<CompatibleRow>();
            // an unpublished local mod (a tool of the author's, say) will never be on a player's machine, so nothing is marked against it
            List<HookReviewItem> items = HookReview.Candidates(hookAnalysis, mod, m => !string.Equals(m.Folder, mod.Folder, StringComparison.OrdinalIgnoreCase) && (readOnly || ModDetails.WorkshopId(m) != null), DateTime.Now);
            HashSet<HookClaim> shown = new HashSet<HookClaim>();
            foreach (HookReviewItem item in items)
            {
                // a read-only list agrees with the mod's health, which ignores a partner that is not enabled
                if (readOnly && !item.BothEnabled && hookAnalysis.Enabled.Contains(mod)) continue;
                HookClaim working = compatibleClaims.FirstOrDefault(c => Marks(c, item.Target, item.Partner));
                if (working != null) shown.Add(working);
                ClaimCheck check = working == null ? null : HookClaims.Evaluate(working, mod, item.Partner, HookParts(item.Target, mod), HookParts(item.Target, item.Partner));
                bool changed = check != null && check.Standing == ClaimStanding.Changed;
                CompatibleRow row = NewRow(item.Target, item.Partner, item.Severity, DetailsOf(item.Target, item.Partner, item.Change), item);
                row.Claim = working;
                row.Changed = changed;
                row.SetCheckedQuietly(working != null && !changed);
                if (changed) row.Note = L["PublishCompatibleChanged"];
                else if (check != null && check.Standing == ClaimStanding.Unverified) row.Note = L["PublishCompatibleUnverified"];
                if (item.Claim == null && working == null)
                {
                    row.CanCheck = false;
                    row.CheckTooltip = L["PublishCompatibleNotReady"];
                    row.Note = L["PublishCompatibleUnverified"];
                }
                row.CanEdit = !readOnly;
                if (readOnly && row.IsChecked) continue;
                rows.Add(row);
            }
            // marks that no longer have an overlap to show (the other mod is gone, or one of them dropped the patch)
            // (a read-only list shows only what still clashes)
            foreach (HookClaim claim in compatibleClaims.Where(c => !readOnly && !shown.Contains(c)).OrderBy(c => c.Partner, StringComparer.OrdinalIgnoreCase).ThenBy(c => c.Target, StringComparer.Ordinal))
            {
                ModInfo partner = PartnerOf(claim);
                CompatibleRow row = NewRow(claim.Target, partner, HookSeverity.Info, DetailsOfClaim(claim), null);
                row.Partner = partner == null ? claim.Partner : NameAndVersion(partner);
                row.Claim = claim;
                row.SetCheckedQuietly(true);
                row.Note = partner == null ? L["PublishCompatibleNotInstalled"] : L["PublishCompatibleNoOverlap"];
                rows.Add(row);
            }
            return rows;
        }

        private List<HookParticipant> HookParts(string target, ModInfo who)
        {
            return hookAnalysis.ByTarget.TryGetValue(target, out List<HookParticipant> patches) ? patches.Where(p => p.Mod == who).ToList() : new List<HookParticipant>();
        }

        private CompatibleRow NewRow(string target, ModInfo partner, HookSeverity severity, string details, HookReviewItem item)
        {
            CompatibleRow row = new CompatibleRow
            {
                Partner = partner == null ? "" : NameAndVersion(partner),
                Method = HookReport.Short(target),
                Args = HookReview.ArgumentCount(target).ToString(),
                Target = target,
                Severity = severity,
                SeverityTooltip = severity == HookSeverity.Conflict ? L["PublishCompatibleSeverityConflict"] : severity == HookSeverity.Caution ? L["PublishCompatibleSeverityCaution"] : "",
                Details = details,
                Summary = HookReport.Spelled(target),
                Item = item,
                Toggled = OnCompatibleToggled,
                CopyTooltip = L["PublishCompatibleCopy"]
            };
            row.Copy = new RelayCommand(() => CopyCompatibleAsync(row));
            return row;
        }

        // the long version of a row: the method in full, both mods' patches as the launcher saw them, and what changed since the mark
        private string DetailsOf(string target, ModInfo partner, List<string> change)
        {
            return HookReview.Describe(hookAnalysis, target, mod, partner, change, side => L.Format("PublishCompatibleDetailsSide", ("Mod", NameAndVersion(side))), L["PublishCompatibleDetailsChanged"]);
        }

        private string DetailsOfClaim(HookClaim claim)
        {
            List<string> lines = new List<string> { claim.Target, "" };
            lines.Add(L.Format("PublishCompatibleDetailsSide", ("Mod", claim.Partner)));
            lines.AddRange((claim.Other?.Facts ?? new List<string>()).Select(l => "    " + l));
            lines.Add(L.Format("PublishCompatibleDetailsSide", ("Mod", mod.DisplayName ?? mod.Id)));
            lines.AddRange((claim.Self?.Facts ?? new List<string>()).Select(l => "    " + l));
            return string.Join("\n", lines);
        }

        // ticking marks the hook as the patches are now; unticking takes the mark out (a mark whose overlap is gone can be put back as it was)
        private void OnCompatibleToggled(CompatibleRow row, bool ticked)
        {
            if (ticked)
            {
                HookClaim claim = row.Item?.Claim ?? row.Claim;
                if (claim == null) return;
                // marked over an earlier mark of the pair, it keeps the patches that mark covered
                HookClaim earlier = row.Claim ?? (row.Item == null ? null : compatibleClaims.FirstOrDefault(c => Marks(c, row.Item.Target, row.Item.Partner)));
                if (earlier != null && row.Item?.Claim != null)
                {
                    claim = new HookClaim
                    {
                        Target = claim.Target,
                        Partner = claim.Partner,
                        Self = HookFacts.ToClaimSide(HookFacts.Union(earlier.Self, claim.Self?.Facts ?? new List<string>())),
                        Other = HookFacts.ToClaimSide(HookFacts.Union(earlier.Other, claim.Other?.Facts ?? new List<string>())),
                        Reviewed = claim.Reviewed,
                        Note = string.IsNullOrEmpty(claim.Note) ? earlier.Note : claim.Note
                    };
                }
                compatibleClaims.RemoveAll(c => ReferenceEquals(c, row.Claim) || (row.Item != null && Marks(c, row.Item.Target, row.Item.Partner)));
                compatibleClaims.Add(claim);
                row.Claim = claim;
                row.Changed = false;
                row.Note = "";
            }
            else
            {
                compatibleClaims.RemoveAll(c => ReferenceEquals(c, row.Claim));
            }
            RaiseCompatible();
        }

        private IEnumerable<CompatibleRow> OpenRows { get { return CompatibleRows.Where(r => r.Item != null && !r.IsChecked); } }

        /// <summary>The number shown in the middle of the "Compatible with" bar: hooks that overlap another mod's and are not marked.</summary>
        public string CompatibleCautionCount { get { return OpenRows.Count().ToString(); } }
        public bool HasCompatibleCaution { get { return OpenRows.Any(); } }
        public string CompatibleCautionTooltip { get { return L.Format("PublishCompatibleCautionHeader", ("Hooks", string.Join("\n", OpenRows.Select(HookLine)))); } }
        public bool HasCompatible { get { return CompatibleRows.Count > 0; } }
        public bool NoCompatible { get { return CompatibleRows.Count == 0; } }
        /// <summary>What the empty list says: nothing overlaps, or why the mod's hooks file could not be read (nothing is written over it then).</summary>
        public string CompatibleNoneText { get { return claimsError ?? L["PublishCompatibleNone"]; } }

        private string HookLine(CompatibleRow r) { return L.Format("PublishCompatibleHookLine", ("Method", r.Method), ("Mod", r.Partner)); }

        /// <summary>The hooks that overlap another mod's and are not marked, as "Method (with Mod)" lines.</summary>
        public List<string> UnmarkedOverlaps()
        {
            return OpenRows.Where(r => !r.Changed).Select(HookLine).ToList();
        }

        /// <summary>The marked hooks whose patches changed since, as "Mod: Method" lines.</summary>
        public List<string> StaleCompatible()
        {
            return OpenRows.Where(r => r.Changed).Select(r => L.Format("PublishStaleCompatibleLine", ("Mod", r.Partner), ("Method", r.Method))).ToList();
        }

        /// <summary>What identifies the list, to tell whether it differs from what was read or saved.</summary>
        public string Key
        {
            get { return string.Join("\n", compatibleClaims.Select(c => c.Target + "|" + c.Partner + "|" + c.Self?.Hash + "|" + c.Other?.Hash).OrderBy(x => x, StringComparer.Ordinal)); }
        }

        private void RaiseCompatible() { Raise(nameof(Header)); Raise(nameof(CompatibleCautionCount)); Raise(nameof(HasCompatibleCaution)); Raise(nameof(CompatibleCautionTooltip)); Changed?.Invoke(); }

        // the row's details onto the clipboard (for handing to an LLM to investigate), with the copy icon turning to a tick for a moment
        private async Task CopyCompatibleAsync(CompatibleRow row)
        {
            await dialogs.CopyTextAsync(row.Details);
            row.Copied = true;
            await Task.Delay(1200);
            row.Copied = false;
        }
    }
}
