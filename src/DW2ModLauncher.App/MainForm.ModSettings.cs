using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text.Json.Nodes;
using System.Windows.Forms;
using DW2ModLauncher.Core.Diagnostics;
using DW2ModLauncher.Core.Models;
using DW2ModLauncher.Core.Services;

namespace DW2ModLauncher.App
{
    public partial class MainForm
    {
        // Whether OpenModConfigEditor has anything to show for this Mod - a Mod-authored
        // settings.schema.json.
        private bool ModHasConfigurableSettings(ModInfo mod)
        {
            if (mod == null) return false;
            return ModSettingsSchemaReader.Read(mod.ContentRoot ?? mod.Folder) != null;
        }

        // Dispatcher used by every "configure this Mod" entry point.
        private void OpenModConfigEditor(ModInfo mod)
        {
            if (mod == null) return;
            string root = mod.ContentRoot ?? mod.Folder;
            ModSettingsSchema schema = ModSettingsSchemaReader.Read(root);
            if (schema == null)
            {
                MessageBox.Show(T("NoConfigurableSettings"), Text);
                return;
            }
            JsonObject values = ModSettingsStore.GetOrCreateValues(mod, schema);
            OpenModSettingsEditor(mod, schema, values, v => ModSettingsStore.SaveValues(mod, v));
        }

        private void OpenSelectedModConfigEditor()
        {
            if (modList == null || modList.SelectedItems.Count == 0) return;
            ModInfo mod = modList.SelectedItems[0].Tag as ModInfo;
            OpenModConfigEditor(mod);
        }

        private void OpenModSettingsEditor(ModInfo mod, ModSettingsSchema schema, JsonObject values, Action<JsonObject> onSave)
        {
            Dictionary<string, Control> editors = new Dictionary<string, Control>(StringComparer.OrdinalIgnoreCase);

            using (Form editor = new Form())
            {
                editor.Text = T("ModSettingsTitle") + (mod.DisplayName ?? mod.Id ?? Path.GetFileName(mod.Folder));
                editor.StartPosition = FormStartPosition.CenterParent;
                editor.Size = new Size(980, 720);
                editor.MinimumSize = new Size(760, 520);
                editor.BackColor = Dw2Deep;
                editor.ForeColor = Dw2Text;
                editor.Font = Font;

                Panel bottom = new Panel();
                bottom.Dock = DockStyle.Bottom;
                bottom.Height = 58;
                bottom.BackColor = Dw2Void;
                editor.Controls.Add(bottom);

                Button save = MakeButton(T("Save"), 680, 12, 125, 34);
                Button cancel = MakeButton(T("Cancel"), 820, 12, 125, 34);
                save.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                cancel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                cancel.DialogResult = DialogResult.Cancel;
                bottom.Controls.Add(save);
                bottom.Controls.Add(cancel);

                TableLayoutPanel table = new TableLayoutPanel();
                table.Dock = DockStyle.Fill;
                table.AutoScroll = true;
                table.Padding = new Padding(14);
                table.ColumnCount = 3;
                table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
                table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
                table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                editor.Controls.Add(table);
                table.BringToFront();

                table.Controls.Add(MakeSettingsHeader(T("Setting")), 0, 0);
                table.Controls.Add(MakeSettingsHeader(T("Value")), 1, 0);
                table.Controls.Add(MakeSettingsHeader(T("Description")), 2, 0);
                int rowIndex = 1;
                foreach (ModSettingsField field in schema.Fields)
                {
                    if (string.IsNullOrWhiteSpace(field.Key)) continue;
                    table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

                    Label keyLabel = new Label();
                    keyLabel.Text = string.IsNullOrWhiteSpace(field.Label) ? field.Key : field.Label;
                    keyLabel.AutoSize = true;
                    keyLabel.MaximumSize = new Size(220, 0);
                    keyLabel.Margin = new Padding(3, 9, 3, 8);
                    keyLabel.ForeColor = Dw2Gold;
                    table.Controls.Add(keyLabel, 0, rowIndex);

                    Control control = BuildSettingsFieldEditor(field, values[field.Key]);
                    control.Width = 205;
                    control.Margin = new Padding(3, 5, 3, 7);
                    control.BackColor = Dw2Void;
                    control.ForeColor = Dw2Text;
                    table.Controls.Add(control, 1, rowIndex);
                    editors[field.Key] = control;

                    Label description = new Label();
                    description.Text = field.Description ?? "";
                    description.AutoSize = true;
                    description.MaximumSize = new Size(450, 0);
                    description.Margin = new Padding(3, 8, 3, 8);
                    description.ForeColor = Dw2Muted;
                    table.Controls.Add(description, 2, rowIndex);
                    rowIndex++;
                }

                save.Click += delegate
                {
                    foreach (ModSettingsField field in schema.Fields)
                    {
                        if (string.IsNullOrWhiteSpace(field.Key) || !editors.TryGetValue(field.Key, out Control control)) continue;
                        values[field.Key] = ReadSettingsFieldValue(field, control);
                    }
                    onSave(values);
                    editor.DialogResult = DialogResult.OK;
                    editor.Close();
                };

                if (editor.ShowDialog(this) == DialogResult.OK)
                {
                    SetStatus(T("ModSettingsSaved"));
                }
            }
        }

        private Control BuildSettingsFieldEditor(ModSettingsField field, JsonNode current)
        {
            string type = (field.Type ?? "").ToLowerInvariant();
            if (type == "bool")
            {
                CheckBox box = new CheckBox();
                box.Checked = current != null && current.GetValue<bool>();
                box.Text = "";
                return box;
            }
            if (type == "enum" && field.Options != null && field.Options.Count > 0)
            {
                ComboBox box = new ComboBox();
                box.DropDownStyle = ComboBoxStyle.DropDownList;
                box.Items.AddRange(field.Options.ToArray());
                string text = current?.ToString() ?? "";
                int index = field.Options.FindIndex(x => x.Equals(text, StringComparison.OrdinalIgnoreCase));
                box.SelectedIndex = index >= 0 ? index : 0;
                return box;
            }
            if (type == "int" || type == "float")
            {
                NumericUpDown box = new NumericUpDown();
                box.DecimalPlaces = type == "int" ? 0 : 2;
                box.Minimum = (decimal)(field.Min ?? -1000000);
                box.Maximum = (decimal)(field.Max ?? 1000000);
                decimal value = 0;
                try { value = current == null ? 0 : (decimal)current.GetValue<double>(); } catch { }
                box.Value = Math.Max(box.Minimum, Math.Min(box.Maximum, value));
                return box;
            }
            TextBox textBox = new TextBox();
            textBox.Text = current?.ToString() ?? "";
            return textBox;
        }

        private JsonNode ReadSettingsFieldValue(ModSettingsField field, Control control)
        {
            string type = (field.Type ?? "").ToLowerInvariant();
            if (type == "bool" && control is CheckBox checkBox) return JsonValue.Create(checkBox.Checked);
            if (type == "enum" && control is ComboBox comboBox) return JsonValue.Create(comboBox.Text);
            if ((type == "int" || type == "float") && control is NumericUpDown numeric)
                return type == "int" ? JsonValue.Create((long)numeric.Value) : JsonValue.Create((double)numeric.Value);
            return JsonValue.Create((control as TextBox)?.Text ?? "");
        }
    }
}
