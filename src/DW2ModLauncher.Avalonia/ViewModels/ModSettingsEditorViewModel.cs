using System.Collections.Generic;
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

        public SettingFieldViewModel(ModSettingsField field, JsonNode current)
        {
            schemaField = field;
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
        }

        public string Key { get { return schemaField.Key; } }
        public ModSettingKind Kind { get; }
        public string Label { get; }
        public string Description { get; }
        public List<string> Options { get; }
        public decimal MinValue { get; }
        public decimal MaxValue { get; }
        public decimal Increment { get { return Kind == ModSettingKind.Integer ? 1 : 0.1m; } }
        public string NumberFormat { get { return Kind == ModSettingKind.Integer ? "0" : "0.##"; } }

        public bool IsBool { get { return Kind == ModSettingKind.Bool; } }
        public bool IsChoice { get { return Kind == ModSettingKind.Choice; } }
        public bool IsNumber { get { return Kind == ModSettingKind.Integer || Kind == ModSettingKind.Number; } }
        public bool IsText { get { return Kind == ModSettingKind.Text; } }

        private bool boolValue;
        public bool BoolValue { get { return boolValue; } set { Set(ref boolValue, value); } }

        private string textValue = "";
        public string TextValue { get { return textValue; } set { Set(ref textValue, value); } }

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
                default: return ModSettingsValues.FromText(textValue);
            }
        }
    }

    public class ModSettingsEditorViewModel : ViewModelBase
    {
        private readonly JsonObject values;

        public ModSettingsEditorViewModel(string title, ModSettingsSchema schema, JsonObject values, LocalizedStrings l)
        {
            Title = title;
            L = l;
            this.values = values;
            foreach (ModSettingsField field in schema.Fields)
            {
                if (string.IsNullOrWhiteSpace(field.Key)) continue;
                Fields.Add(new SettingFieldViewModel(field, values[field.Key]));
            }
        }

        public string Title { get; }
        public LocalizedStrings L { get; }
        public ObservableCollection<SettingFieldViewModel> Fields { get; } = new ObservableCollection<SettingFieldViewModel>();

        /// <summary>Writes every edited value into the JSON object that was passed in.</summary>
        public JsonObject Apply()
        {
            foreach (SettingFieldViewModel field in Fields) values[field.Key] = field.ToJson();
            return values;
        }
    }
}
