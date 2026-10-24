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
    /// <summary>One entry of a choice field: the text stored in the settings (the schema's option) and the text shown (possibly translated).</summary>
    public class ChoiceOption
    {
        public ChoiceOption(string value, string display)
        {
            Value = value;
            Display = display;
        }

        public string Value { get; }
        public string Display { get; }

        public override string ToString() { return Display; }
    }

    /// <summary>One editable value from a mod's settings.schema.json. Exactly one of the typed properties is used, per Kind.</summary>
    public class SettingFieldViewModel : ViewModelBase
    {
        private readonly ModSettingsField schemaField;

        private readonly IDialogService dialogs;
        private readonly LocalizedStrings l;
        private readonly JsonNode defaultNode;
        private readonly string defaultJson;
        private readonly string savedJson;
        private readonly string gameRoot;

        public SettingFieldViewModel(ModSettingsField field, JsonNode current, IDialogService dialogs, LocalizedStrings l, ModSettingsTranslation translation = null, string gameRoot = null)
        {
            translation = translation ?? ModSettingsTranslation.None;
            this.gameRoot = gameRoot;
            schemaField = field;
            this.dialogs = dialogs;
            this.l = l;
            Kind = ModSettingsValues.KindOf(field);
            Label = translation.Label(field.Key, string.IsNullOrWhiteSpace(field.Label) ? field.Key : field.Label);
            Description = translation.Description(field.Key, field.Description ?? "");
            ChoiceOptions = (field.Options ?? new List<string>()).Select(o => new ChoiceOption(o, translation.Option(field.Key, o))).ToList();
            MinValue = ModSettingsValues.MinOf(field);
            MaxValue = ModSettingsValues.MaxOf(field);
            // Remember what the schema's default looks like once it has passed through the same conversions as an edited value,
            // so "at default" compares like with like.
            // A field with no schema default falls back to its zero value (false, 0 clamped to Min/Max, first option, blank text),
            // which is what loading null gives, so every field can be reset.
            Load(ModSettingsStore.ToJsonNode(field.Default));
            defaultNode = ToJson();
            defaultJson = defaultNode.ToJsonString();
            // A key missing from the saved values (e.g. a field added to the schema since) starts at the default.
            Load(current ?? defaultNode);
            // What the field held when the dialog opened (normalised like an edited value), for "differs from saved".
            savedJson = ToJson().ToJsonString();
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
        public List<ChoiceOption> ChoiceOptions { get; }
        /// <summary>The selected entry of a choice field; the stored value stays the schema's option text whatever is shown.</summary>
        public ChoiceOption SelectedChoice
        {
            get { return ChoiceOptions.FirstOrDefault(o => o.Value == textValue); }
            set { if (value != null) TextValue = value.Value; }
        }
        public decimal MinValue { get; }
        public decimal MaxValue { get; }
        public decimal Increment { get { return ModSettingsValues.StepOf(schemaField); } }
        public string NumberFormat { get { return Kind == ModSettingKind.Integer ? "0" : "0.##"; } }

        public bool IsBool { get { return Kind == ModSettingKind.Bool; } }
        public bool IsChoice { get { return Kind == ModSettingKind.Choice; } }
        public bool IsNumber { get { return Kind == ModSettingKind.Integer || Kind == ModSettingKind.Number; } }
        public bool IsText { get { return Kind == ModSettingKind.Text; } }
        public bool IsPath { get { return Kind == ModSettingKind.Folder || Kind == ModSettingKind.File; } }
        /// <summary>Plain strings and paths share the text box; paths add a browse button and validation.</summary>
        public bool IsTextBox { get { return IsText || IsPath; } }
        public bool IsInvalid { get { return IsPath && !ModSettingsValues.IsValidPath(Kind, textValue, gameRoot); } }
        public RelayCommand BrowseCommand { get; }
        public RelayCommand ResetCommand { get; }

        /// <summary>Why the path is red (a bad %FOLDER% name, or a folder/file that does not exist); null while it is fine. Shown as the box's tooltip.</summary>
        public string PathProblemText
        {
            get
            {
                if (!IsPath) return null;
                string key = ModSettingsValues.PathProblemKey(Kind, textValue, gameRoot, out string expanded, out string detail);
                if (key == null) return null;
                return key == "NotFound" ? l.Format("PathNotFound", expanded) : l.Format("PathProblem" + key, detail, PathTokens.KnownList);
            }
        }

        private async System.Threading.Tasks.Task BrowseAsync()
        {
            // the picker opens on the real folder behind any %TOKEN%; what comes back is written with the most specific token again
            string start = null;
            PathExpansion current = PathTokens.ExpandForHost(textValue, gameRoot);
            if (current.Ok && current.Path.Length > 0)
            {
                start = Kind == ModSettingKind.Folder ? current.Path : Path.GetDirectoryName(current.Path);
                if (!string.IsNullOrEmpty(start) && !Directory.Exists(start)) start = null;
            }
            string picked = Kind == ModSettingKind.Folder
                ? await dialogs.PickFolderAsync(Label, start)
                : await dialogs.PickFileAsync(Label, start, l["FilterAllFiles"], "*");
            if (picked != null) TextValue = PathTokens.Collapse(picked, gameRoot);
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
        /// <summary>The value differs from what was saved when the dialog opened.</summary>
        public bool IsModified { get { return ToJson().ToJsonString() != savedJson; } }
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
            Raise(nameof(SelectedChoice));
            Raise(nameof(NumberValue));
            Raise(nameof(IsInvalid));
            Raise(nameof(PathProblemText));
            Raise(nameof(CanReset));
            Raise(nameof(IsModified));
        }

        private bool boolValue;
        public bool BoolValue { get { return boolValue; } set { if (Set(ref boolValue, value)) { Raise(nameof(CanReset)); Raise(nameof(IsModified)); } } }

        private string textValue = "";
        public string TextValue { get { return textValue; } set { if (Set(ref textValue, value)) { Raise(nameof(SelectedChoice)); Raise(nameof(IsInvalid)); Raise(nameof(PathProblemText)); Raise(nameof(CanReset)); Raise(nameof(IsModified)); } } }

        private decimal? numberValue = 0;
        public decimal? NumberValue { get { return numberValue; } set { if (Set(ref numberValue, value)) { Raise(nameof(CanReset)); Raise(nameof(IsModified)); } } }

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

    /// <summary>One column of a ladder: the short heading, with the full option text as its tooltip.</summary>
    public class LadderColumn
    {
        public LadderColumn(string option, string heading, string display)
        {
            Option = option;
            Heading = heading;
            Display = display;
        }

        /// <summary>The schema's option text (what is stored).</summary>
        public string Option { get; }
        public string Heading { get; }
        public string Display { get; }
        /// <summary>Only a shortened heading needs the full text as a tooltip.</summary>
        public string Tooltip { get { return Heading == Display ? null : Display; } }
    }

    /// <summary>One cell of a ladder row. The none column is filled only while the row holds none; any other cell is filled from the column next to none out to the row's selected one. Clicking selects that column (a no-op when it already is).</summary>
    public class LadderCellViewModel : ViewModelBase
    {
        private readonly LadderRowViewModel row;
        private readonly int index;

        public LadderCellViewModel(LadderRowViewModel row, int index)
        {
            this.row = row;
            this.index = index;
            ClickCommand = new RelayCommand(() => { if (IsAvailable) row.Click(index); });
        }

        public RelayCommand ClickCommand { get; }
        /// <summary>The row's field offers this option; otherwise the cell is blocked out and cannot be clicked.</summary>
        public bool IsAvailable { get { return row.Offers(index); } }
        public bool IsBlocked { get { return !IsAvailable; } }
        public bool IsOn { get { return IsAvailable && row.IsFilled(index); } }
        /// <summary>This column is the row's current value: the tip of the bar.</summary>
        public bool IsSelected { get { return index == row.Threshold; } }
        public string Tooltip { get { return row.CellTooltip(index); } }

        public void Changed()
        {
            Raise(nameof(IsOn));
            Raise(nameof(IsSelected));
            Raise(nameof(Tooltip));
        }
    }

    /// <summary>One row of a ladder: a field drawn as cells. The field stays the single source of truth, so reset, "modified" and saving are the field's own.</summary>
    public class LadderRowViewModel : ViewModelBase
    {
        private readonly LadderViewModel owner;

        public LadderRowViewModel(LadderViewModel owner, SettingFieldViewModel field)
        {
            this.owner = owner;
            Field = field;
            for (int i = 0; i < owner.Columns.Count; i++) Cells.Add(new LadderCellViewModel(this, i));
            field.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName != nameof(SettingFieldViewModel.TextValue)) return;
                foreach (LadderCellViewModel cell in Cells) cell.Changed();
            };
        }

        public SettingFieldViewModel Field { get; }
        public List<LadderCellViewModel> Cells { get; } = new List<LadderCellViewModel>();

        /// <summary>Index of the selected column (0, the none column, while the row holds none).</summary>
        public int Threshold { get { return owner.ColumnOf(Field.SelectedChoice?.Value); } }

        /// <summary>The bar grows from the none end: with none first, columns 1..selected are filled; with none last, selected..last-1.</summary>
        public bool IsFilled(int index)
        {
            int t = Threshold;
            int n = owner.NoneIndex;
            if (index == t) return true;
            if (t < 0 || t == n) return false;
            return n == 0 ? index > 0 && index <= t : index < n && index >= t;
        }

        public bool Offers(int index)
        {
            return Field.ChoiceOptions.Any(o => string.Equals(o.Value, owner.Columns[index].Option, System.StringComparison.OrdinalIgnoreCase));
        }

        public void Click(int index)
        {
            Field.TextValue = owner.Columns[index].Option;
        }

        public string CellTooltip(int index)
        {
            return owner.Strings.Format(Offers(index) ? "ModSettingsLadderSet" : "ModSettingsLadderBlocked", Field.Label, owner.Columns[index].Display);
        }
    }

    /// <summary>
    /// A group drawn as a ladder matrix (the schema's "_layouts" entry): columns are the widest row's options in the order the schema lists them,
    /// rows are the fields. Column text comes from the first field's option translations, as the columns are the widest row's options.
    /// </summary>
    public class LadderViewModel
    {
        private readonly List<LadderRowViewModel> allRows = new List<LadderRowViewModel>();

        public LadderViewModel(string groupName, ModSettingsLayout layout, IReadOnlyList<SettingFieldViewModel> fields, LocalizedStrings l, ModSettingsTranslation translation)
        {
            Strings = l;
            List<ChoiceOption> options = fields.OrderByDescending(f => f.ChoiceOptions.Count).First().ChoiceOptions;
            ChoiceOption none = options.First(o => string.Equals(o.Value, layout.None, System.StringComparison.OrdinalIgnoreCase));
            // the none option is a column of its own, wherever the list puts it (first or last), so "nothing" is a deliberate choice and clicking a column is never a toggle
            foreach (ChoiceOption option in options)
            {
                layout.Short.TryGetValue(option.Value, out string schemaShort);
                Columns.Add(new LadderColumn(option.Value, translation.ShortHeading(groupName, option.Value, schemaShort ?? option.Display), option.Display));
            }
            NoneIndex = ColumnOf(none.Value);
            foreach (SettingFieldViewModel field in fields) allRows.Add(new LadderRowViewModel(this, field));
        }

        public LocalizedStrings Strings { get; }
        /// <summary>Column of the none option: the first or the last, the end the bars grow from.</summary>
        public int NoneIndex { get; private set; }
        public List<LadderColumn> Columns { get; } = new List<LadderColumn>();
        /// <summary>The rows currently shown (hidden fields only while "Show hidden" is on).</summary>
        public ObservableCollection<LadderRowViewModel> Rows { get; } = new ObservableCollection<LadderRowViewModel>();

        public int ColumnOf(string option)
        {
            return Columns.FindIndex(c => string.Equals(c.Option, option, System.StringComparison.OrdinalIgnoreCase));
        }

        public void Show(IEnumerable<SettingFieldViewModel> shown)
        {
            Rows.Clear();
            foreach (SettingFieldViewModel field in shown) Rows.Add(allRows.First(r => r.Field == field));
        }
    }

    /// <summary>One group of the settings schema: an optional heading and its fields, shown in their own grid.</summary>
    public class SettingGroupViewModel : ViewModelBase
    {
        private readonly List<SettingFieldViewModel> allFields;
        private bool isExpanded = true;

        public SettingGroupViewModel(string heading, bool hasHeading, List<SettingFieldViewModel> fields, LocalizedStrings l = null, string displayHeading = null, string description = null, ModSettingsLayout layout = null, ModSettingsTranslation translation = null)
        {
            Heading = heading;
            Description = description;
            DisplayHeading = string.IsNullOrWhiteSpace(displayHeading) ? heading : displayHeading;
            HasHeading = hasHeading;
            allFields = fields;
            ResetTooltip = l?["ModSettingsResetGroup"];
            ToggleCommand = new RelayCommand(() => IsExpanded = !IsExpanded);
            ResetGroupCommand = new RelayCommand(ResetAll);
            foreach (SettingFieldViewModel f in fields)
                f.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(SettingFieldViewModel.CanReset)) Raise(nameof(CanReset)); };
            if (layout != null)
            {
                if (l == null) throw new System.ArgumentException("A ladder group needs the launcher's strings for its tooltips.", nameof(l));
                ladder = new LadderViewModel(heading, layout, fields, l, translation ?? ModSettingsTranslation.None);
            }
            Refresh(false);
        }

        private readonly LadderViewModel ladder;
        /// <summary>The group is drawn as a ladder matrix (rows = fields, columns = options) instead of the field list.</summary>
        public bool IsLadder { get { return ladder != null; } }
        public LadderViewModel Ladder { get { return ladder; } }
        public bool ShowFieldList { get { return isExpanded && !IsLadder; } }
        public bool ShowLadder { get { return isExpanded && IsLadder; } }

        /// <summary>The schema's group name; also what the launcher remembers collapsed groups by.</summary>
        public string Heading { get; }
        /// <summary>What the dialog shows as the heading: the translation if the mod ships one, else <see cref="Heading"/>.</summary>
        public string DisplayHeading { get; }
        public bool HasHeading { get; }
        /// <summary>What the group is about, shown under the heading while the group is expanded; null when the mod gave none.</summary>
        public string Description { get; }
        public bool HasDescription { get { return !string.IsNullOrWhiteSpace(Description); } }
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
            set { if (Set(ref isExpanded, value)) { Raise(nameof(Chevron)); Raise(nameof(ShowDescription)); Raise(nameof(ShowFieldList)); Raise(nameof(ShowLadder)); } }
        }
        public bool ShowDescription { get { return isExpanded && HasDescription; } }
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
            ladder?.Show(Fields);
        }
    }

    public class ModSettingsEditorViewModel : ViewModelBase
    {
        private readonly JsonObject values;
        private bool showHidden;

        public ModSettingsEditorViewModel(string title, ModSettingsSchema schema, ModInfo mod, JsonObject values, LocalizedStrings l, IDialogService dialogs, bool showHidden = false, IEnumerable<string> collapsedGroups = null, ModSettingsTranslation translation = null, string gameRoot = null)
        {
            translation = translation ?? ModSettingsTranslation.None;
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
                    SettingFieldViewModel row = new SettingFieldViewModel(field, values[field.Key], dialogs, l, translation, gameRoot);
                    row.PropertyChanged += (s, e) =>
                    {
                        if (e.PropertyName == nameof(SettingFieldViewModel.IsInvalid)) { Raise(nameof(AllValid)); Raise(nameof(CanSave)); }
                        else if (e.PropertyName == nameof(SettingFieldViewModel.IsModified)) { Raise(nameof(IsModified)); Raise(nameof(CanSave)); }
                    };
                    rows.Add(row);
                }
                if (rows.Count == 0) continue;
                SettingGroupViewModel g = new SettingGroupViewModel(group.Name, group.HasHeading, rows, l, translation.Heading(group.Name, group.Name), translation.GroupDescription(group.Name, group.Description), group.Layout, translation);
                if (g.HasHeading && collapsed.Contains(g.Heading ?? "")) g.IsExpanded = false;
                Groups.Add(g);
            }
            // Hidden fields are developer controls: only a local mod that has some gets the toggle (Workshop copies never see them).
            CanShowHidden = mod != null && !mod.IsWorkshop && Groups.Any(g => g.AllFields.Any(f => f.IsHiddenField));
            ShowHidden = showHidden;
        }

        public bool CanShowHidden { get; }

        /// <summary>The dialog's vertical scroll offset: set by the launcher before it opens (to where the user left off), updated by the dialog as it closes.</summary>
        public double ScrollOffset { get; set; }

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
                Raise(nameof(CanSave));
            }
        }

        /// <summary>False while any shown folder/filename field holds a path that does not exist (Save is disabled).</summary>
        public bool AllValid { get { return !Groups.SelectMany(g => g.Fields).Any(f => f.IsInvalid); } }

        /// <summary>Any field (shown or not) differs from the values saved when the dialog opened.</summary>
        public bool IsModified { get { return Groups.SelectMany(g => g.AllFields).Any(f => f.IsModified); } }

        /// <summary>Save is live only when something changed and every shown path is valid.</summary>
        public bool CanSave { get { return AllValid && IsModified; } }

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
