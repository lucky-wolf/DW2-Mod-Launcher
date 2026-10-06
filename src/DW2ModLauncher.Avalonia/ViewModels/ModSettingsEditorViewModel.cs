using System.Collections.Generic;
using System.IO;
using System.Linq;
using DW2ModLauncher.Avalonia.Services;
using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using DW2ModLauncher.Core.Models;
using DW2ModLauncher.Core.Services;

namespace DW2ModLauncher.Avalonia.ViewModels
{
    /// <summary>One editable value from a mod's settings.schema.json. Exactly one of the typed properties is used, per Kind.</summary>
    public class SettingFieldViewModel : ViewModelBase
    {
        private readonly ModSettingsField schemaField;

        private readonly IDialogService dialogs;
        private readonly LocalizedStrings l;
        private readonly JsonNode defaultNode;
        private readonly string defaultJson;

        public SettingFieldViewModel(ModSettingsField field, JsonNode current, IDialogService dialogs, LocalizedStrings l)
        {
            schemaField = field;
            this.dialogs = dialogs;
            this.l = l;
            Kind = ModSettingsValues.KindOf(field);
            Label = string.IsNullOrWhiteSpace(field.Label) ? field.Key : field.Label;
            Description = field.Description ?? "";
            Options = field.Options ?? new List<string>();
            MinValue = (decimal)(field.Min ?? -1000000);
            MaxValue = (decimal)(field.Max ?? 1000000);
            // Remember what the schema's default looks like once it has passed through the same conversions as an edited value,
            // so "at default" compares like with like.
            // A field with no schema default falls back to its zero value (false, 0 clamped to Min/Max, first option, blank text),
            // which is what loading null gives, so every field can be reset.
            Load(ModSettingsStore.ToJsonNode(field.Default));
            defaultNode = ToJson();
            defaultJson = defaultNode.ToJsonString();
            // A key missing from the saved values (e.g. a field added to the schema since) starts at the default.
            Load(current ?? defaultNode);
            BrowseCommand = new RelayCommand(BrowseAsync);
            ResetCommand = new RelayCommand(Reset);
        }

        public string Key { get { return schemaField.Key; } }
        /// <summary>The schema marks this a hidden (developer) field: only shown on request, and never for Workshop copies.</summary>
        public bool IsHiddenField { get { return schemaField.Hidden; } }
        public ModSettingKind Kind { get; }
        public string Label { get; }
        public string Description { get; }
        /// <summary>Row tooltip: the description, or null (no tooltip) when the schema gives none.</summary>
        public string Tooltip { get { return string.IsNullOrWhiteSpace(Description) ? null : Description; } }
        public List<string> Options { get; }
        public decimal MinValue { get; }
        public decimal MaxValue { get; }
        public decimal Increment { get { return Kind == ModSettingKind.Integer ? 1 : 0.1m; } }
        public string NumberFormat { get { return Kind == ModSettingKind.Integer ? "0" : "0.##"; } }

        public bool IsBool { get { return Kind == ModSettingKind.Bool; } }
        public bool IsChoice { get { return Kind == ModSettingKind.Choice; } }
        public bool IsNumber { get { return Kind == ModSettingKind.Integer || Kind == ModSettingKind.Number; } }
        public bool IsText { get { return Kind == ModSettingKind.Text; } }
        public bool IsPath { get { return Kind == ModSettingKind.Folder || Kind == ModSettingKind.File; } }
        /// <summary>Plain strings and paths share the text box; paths add a browse button and validation.</summary>
        public bool IsTextBox { get { return IsText || IsPath; } }
        public bool IsInvalid { get { return IsPath && !ModSettingsValues.IsValidPath(Kind, textValue); } }
        public RelayCommand BrowseCommand { get; }
        public RelayCommand ResetCommand { get; }

        private async System.Threading.Tasks.Task BrowseAsync()
        {
            string current = (textValue ?? "").Trim();
            string start = null;
            try
            {
                if (current.Length > 0) start = Kind == ModSettingKind.Folder ? current : Path.GetDirectoryName(current);
                if (!string.IsNullOrEmpty(start) && !Directory.Exists(start)) start = null;
            }
            catch { start = null; }
            string picked = Kind == ModSettingKind.Folder
                ? await dialogs.PickFolderAsync(Label, start)
                : await dialogs.PickFileAsync(Label, start, "All files", "*");
            if (picked != null) TextValue = picked;
        }

        private void Load(JsonNode node)
        {
            switch (Kind)
            {
                case ModSettingKind.Bool: boolValue = ModSettingsValues.ToBool(node); break;
                case ModSettingKind.Choice: textValue = ModSettingsValues.ToChoice(schemaField, node); break;
                case ModSettingKind.Integer:
                case ModSettingKind.Number: numberValue = ModSettingsValues.ToNumber(schemaField, node); break;
                default: textValue = node?.ToString() ?? ""; break;
            }
        }

        /// <summary>Every field has a reset button: its schema default, or the zero value when the schema gives none.</summary>
        public bool HasDefault { get { return true; } }
        /// <summary>The reset button is only live while the value differs from the default.</summary>
        public bool CanReset { get { return ToJson().ToJsonString() != defaultJson; } }
        public string ResetTooltip
        {
            get
            {
                string shown = defaultNode.ToString();
                if (defaultNode is JsonValue v && v.TryGetValue(out bool b)) shown = b ? "true" : "false";
                return l.Format("ModSettingsResetToDefault", shown.Length == 0 ? l["ModSettingsBlankValue"] : shown);
            }
        }

        /// <summary>Puts the field back to its default; used by the row button and the group button.</summary>
        public void Reset()
        {
            Load(defaultNode);
            Raise(nameof(BoolValue));
            Raise(nameof(TextValue));
            Raise(nameof(NumberValue));
            Raise(nameof(IsInvalid));
            Raise(nameof(CanReset));
        }

        private bool boolValue;
        public bool BoolValue { get { return boolValue; } set { if (Set(ref boolValue, value)) Raise(nameof(CanReset)); } }

        private string textValue = "";
        public string TextValue { get { return textValue; } set { if (Set(ref textValue, value)) { Raise(nameof(IsInvalid)); Raise(nameof(CanReset)); } } }

        private decimal? numberValue = 0;
        public decimal? NumberValue { get { return numberValue; } set { if (Set(ref numberValue, value)) Raise(nameof(CanReset)); } }

        public JsonNode ToJson()
        {
            switch (Kind)
            {
                case ModSettingKind.Bool: return ModSettingsValues.FromBool(boolValue);
                case ModSettingKind.Choice: return ModSettingsValues.FromChoice(textValue);
                case ModSettingKind.Integer:
                case ModSettingKind.Number: return ModSettingsValues.FromNumber(schemaField, numberValue ?? 0);
                case ModSettingKind.Folder:
                case ModSettingKind.File: return ModSettingsValues.FromPath(textValue);
                default: return ModSettingsValues.FromText(textValue);
            }
        }
    }

    /// <summary>One group of the settings schema: an optional heading and its fields, shown in their own grid.</summary>
    public class SettingGroupViewModel : ViewModelBase
    {
        private readonly List<SettingFieldViewModel> allFields;
        private bool isExpanded = true;

        public SettingGroupViewModel(string heading, bool hasHeading, List<SettingFieldViewModel> fields, LocalizedStrings l = null)
        {
            Heading = heading;
            HasHeading = hasHeading;
            allFields = fields;
            ResetTooltip = l?["ModSettingsResetGroup"];
            ToggleCommand = new RelayCommand(() => IsExpanded = !IsExpanded);
            ResetGroupCommand = new RelayCommand(ResetAll);
            foreach (SettingFieldViewModel f in fields)
                f.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(SettingFieldViewModel.CanReset)) Raise(nameof(CanReset)); };
            Refresh(false);
        }

        public string Heading { get; }
        public bool HasHeading { get; }
        public RelayCommand ToggleCommand { get; }
        public RelayCommand ResetGroupCommand { get; }
        public string ResetTooltip { get; }
        /// <summary>The group's reset button is live while any of its fields differs from its default.</summary>
        public bool CanReset { get { return allFields.Any(f => f.CanReset); } }

        private void ResetAll()
        {
            foreach (SettingFieldViewModel f in allFields) f.Reset();
        }

        /// <summary>Whether the group's fields are shown. Only a group with a heading can be collapsed (there is nothing to click otherwise).</summary>
        public bool IsExpanded
        {
            get { return isExpanded; }
            set { if (Set(ref isExpanded, value)) Raise(nameof(Chevron)); }
        }
        public string Chevron { get { return isExpanded ? "▾" : "▸"; } }
        /// <summary>Every field of the group, shown or not.</summary>
        public IReadOnlyList<SettingFieldViewModel> AllFields { get { return allFields; } }
        /// <summary>The fields currently shown (hidden ones only while "Show hidden" is on).</summary>
        public ObservableCollection<SettingFieldViewModel> Fields { get; } = new ObservableCollection<SettingFieldViewModel>();
        public bool IsShown { get { return Fields.Count > 0; } }

        public void Refresh(bool showHidden)
        {
            Fields.Clear();
            foreach (SettingFieldViewModel field in allFields)
                if (showHidden || !field.IsHiddenField) Fields.Add(field);
        }
    }

    public class ModSettingsEditorViewModel : ViewModelBase
    {
        private readonly JsonObject values;
        private bool showHidden;

        public ModSettingsEditorViewModel(string title, ModSettingsSchema schema, ModInfo mod, JsonObject values, LocalizedStrings l, IDialogService dialogs, bool showHidden = false, IEnumerable<string> collapsedGroups = null)
        {
            HashSet<string> collapsed = new HashSet<string>(collapsedGroups ?? Enumerable.Empty<string>());
            Title = title;
            L = l;
            this.values = values;
            foreach (ModSettingsGroup group in schema.VisibleGroups(mod))
            {
                List<SettingFieldViewModel> rows = new List<SettingFieldViewModel>();
                foreach (ModSettingsField field in group.Fields)
                {
                    if (string.IsNullOrWhiteSpace(field.Key)) continue;
                    SettingFieldViewModel row = new SettingFieldViewModel(field, values[field.Key], dialogs, l);
                    row.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(SettingFieldViewModel.IsInvalid)) Raise(nameof(AllValid)); };
                    rows.Add(row);
                }
                if (rows.Count == 0) continue;
                SettingGroupViewModel g = new SettingGroupViewModel(group.Name, group.HasHeading, rows, l);
                if (g.HasHeading && collapsed.Contains(g.Heading ?? "")) g.IsExpanded = false;
                Groups.Add(g);
            }
            // Hidden fields are developer controls: only a local mod that has some gets the toggle (Workshop copies never see them).
            CanShowHidden = mod != null && !mod.IsWorkshop && Groups.Any(g => g.AllFields.Any(f => f.IsHiddenField));
            ShowHidden = showHidden;
        }

        public bool CanShowHidden { get; }

        /// <summary>Headings of the groups currently collapsed (remembered per mod by the launcher).</summary>
        public List<string> CollapsedGroups
        {
            get { return Groups.Where(g => g.HasHeading && !g.IsExpanded).Select(g => g.Heading ?? "").ToList(); }
        }

        /// <summary>The footer's "Show hidden" checkbox: reveals the schema's hidden fields in their groups.</summary>
        public bool ShowHidden
        {
            get { return showHidden; }
            set
            {
                if (!CanShowHidden || !Set(ref showHidden, value)) return;
                foreach (SettingGroupViewModel group in Groups) group.Refresh(showHidden);
                Raise(nameof(VisibleGroups));
                Raise(nameof(AllValid));
            }
        }

        /// <summary>False while any shown folder/filename field holds a path that does not exist (Save is disabled).</summary>
        public bool AllValid { get { return !Groups.SelectMany(g => g.Fields).Any(f => f.IsInvalid); } }

        public string Title { get; }
        public LocalizedStrings L { get; }
        /// <summary>The schema's groups in file order; each is shown as its own block, so a group never shares a row with another.</summary>
        public List<SettingGroupViewModel> Groups { get; } = new List<SettingGroupViewModel>();
        /// <summary>The groups with at least one shown field (what the dialog lists).</summary>
        public List<SettingGroupViewModel> VisibleGroups { get { return Groups.Where(g => g.IsShown).ToList(); } }

        /// <summary>Writes every edited value (shown or not) into the JSON object that was passed in.</summary>
        public JsonObject Apply()
        {
            foreach (SettingFieldViewModel field in Groups.SelectMany(g => g.AllFields)) values[field.Key] = field.ToJson();
            return values;
        }
    }
}
