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

        public SettingFieldViewModel(ModSettingsField field, JsonNode current, IDialogService dialogs)
        {
            schemaField = field;
            this.dialogs = dialogs;
            Kind = ModSettingsValues.KindOf(field);
            Label = string.IsNullOrWhiteSpace(field.Label) ? field.Key : field.Label;
            Description = field.Description ?? "";
            Options = field.Options ?? new List<string>();
            MinValue = (decimal)(field.Min ?? -1000000);
            MaxValue = (decimal)(field.Max ?? 1000000);
            switch (Kind)
            {
                case ModSettingKind.Bool: boolValue = ModSettingsValues.ToBool(current); break;
                case ModSettingKind.Choice: textValue = ModSettingsValues.ToChoice(field, current); break;
                case ModSettingKind.Integer:
                case ModSettingKind.Number: numberValue = ModSettingsValues.ToNumber(field, current); break;
                default: textValue = current?.ToString() ?? ""; break;
            }
            BrowseCommand = new RelayCommand(BrowseAsync);
        }

        public string Key { get { return schemaField.Key; } }
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

        private bool boolValue;
        public bool BoolValue { get { return boolValue; } set { Set(ref boolValue, value); } }

        private string textValue = "";
        public string TextValue { get { return textValue; } set { if (Set(ref textValue, value)) Raise(nameof(IsInvalid)); } }

        private decimal? numberValue = 0;
        public decimal? NumberValue { get { return numberValue; } set { Set(ref numberValue, value); } }

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

    public class ModSettingsEditorViewModel : ViewModelBase
    {
        private readonly JsonObject values;

        public ModSettingsEditorViewModel(string title, ModSettingsSchema schema, ModInfo mod, JsonObject values, LocalizedStrings l, IDialogService dialogs)
        {
            Title = title;
            L = l;
            this.values = values;
            foreach (ModSettingsField field in schema.VisibleFields(mod))
            {
                if (string.IsNullOrWhiteSpace(field.Key)) continue;
                SettingFieldViewModel row = new SettingFieldViewModel(field, values[field.Key], dialogs);
                row.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(SettingFieldViewModel.IsInvalid)) Raise(nameof(AllValid)); };
                (field.LocalOnly ? LocalFields : Fields).Add(row);
            }
        }

        /// <summary>False while any folder/filename field holds a path that does not exist (Save is disabled).</summary>
        public bool AllValid { get { return !Fields.Concat(LocalFields).Any(f => f.IsInvalid); } }

        public string Title { get; }
        public LocalizedStrings L { get; }
        public ObservableCollection<SettingFieldViewModel> Fields { get; } = new ObservableCollection<SettingFieldViewModel>();
        /// <summary>Developer-only fields, shown under a separator; empty for Workshop mods.</summary>
        public ObservableCollection<SettingFieldViewModel> LocalFields { get; } = new ObservableCollection<SettingFieldViewModel>();
        public bool HasLocalFields { get { return LocalFields.Count > 0; } }

        /// <summary>Writes every edited value into the JSON object that was passed in.</summary>
        public JsonObject Apply()
        {
            foreach (SettingFieldViewModel field in Fields.Concat(LocalFields)) values[field.Key] = field.ToJson();
            return values;
        }
    }
}
