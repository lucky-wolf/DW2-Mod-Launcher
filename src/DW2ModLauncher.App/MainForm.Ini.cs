using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DW2ModLauncher.Core.Diagnostics;
using DW2ModLauncher.Core.Models;
using DW2ModLauncher.Core.Services;

namespace DW2ModLauncherBeta
{
    public partial class MainForm
    {
        private string FindAiIni()
        {
            try
            {
                if (string.IsNullOrEmpty(settings.ManagedModsRoot) || !Directory.Exists(settings.ManagedModsRoot)) return null;
                string[] files = Directory.GetFiles(settings.ManagedModsRoot, "*.ini", SearchOption.AllDirectories);
                foreach (string f in files)
                {
                    string n = Path.GetFileName(f).ToUpperInvariant();
                    if (n.Contains("AI") && n.Contains("COMMANDER")) return f;
                }
            }
            catch { }
            return null;
        }

        private Dictionary<string, string> ReadIni(string path) { return IniFile.Read(path); }
        private bool IniBool(Dictionary<string, string> d, string key, bool fallback) { return IniFile.GetBool(d, key, fallback); }
        private string IniValue(Dictionary<string, string> d, string key, string fallback) { return IniFile.GetValue(d, key, fallback); }

        private void LoadAiSettings()
        {
            if (aiIniPathLabel == null || aiEnabled == null || aiWar == null || aiPeace == null ||
                aiUltimatum == null || aiAdvisor == null || aiBackend == null || aiBaseUrl == null || aiModel == null) return;
            string ini = FindAiIni();
            aiIniPathLabel.Text = ini == null ? "INI: not found" : "INI: " + ini;
            Dictionary<string, string> d = ReadIni(ini);
            aiEnabled.Checked = IniBool(d, "Enabled", true);
            aiWar.Checked = IniBool(d, "WarControlEnabled", true);
            aiPeace.Checked = IniBool(d, "PeaceControlEnabled", true);
            aiUltimatum.Checked = IniBool(d, "UltimatumEnabled", true);
            aiAdvisor.Checked = IniBool(d, "AdvisorEnabled", true);
            aiBackend.Text = IniValue(d, "Backend", "lmstudio");
            aiBaseUrl.Text = IniValue(d, "BaseUrl", "http://127.0.0.1:1234/v1");
            aiModel.Text = IniValue(d, "Model", "");
        }

        private void SaveAiSettings()
        {
            if (aiEnabled == null || aiWar == null || aiPeace == null || aiUltimatum == null ||
                aiAdvisor == null || aiBackend == null || aiBaseUrl == null || aiModel == null) return;
            string ini = FindAiIni();
            if (ini == null)
            {
                MessageBox.Show(T("AI Commander INIが見つかりません。", "AI Commander INI was not found."), Text);
                return;
            }
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            values["Enabled"] = aiEnabled.Checked ? "true" : "false";
            values["Language"] = settings.Language == "en" ? "en" : "ja";
            values["WarControlEnabled"] = aiWar.Checked ? "true" : "false";
            values["PeaceControlEnabled"] = aiPeace.Checked ? "true" : "false";
            values["UltimatumEnabled"] = aiUltimatum.Checked ? "true" : "false";
            values["AdvisorEnabled"] = aiAdvisor.Checked ? "true" : "false";
            values["Backend"] = aiBackend.Text.Trim();
            values["BaseUrl"] = aiBaseUrl.Text.Trim();
            values["Model"] = aiModel.Text.Trim();
            WriteIniValues(ini, values);
            SetStatus(T("AI Commander INIを保存しました。", "AI Commander INI saved."));
        }

        private void WriteIniValues(string path, Dictionary<string, string> values)
        {
            try
            {
                IniFile.Write(path, values);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, Text);
            }
        }

        private LauncherMeta ReadLauncherMeta(ModInfo mod)
        {
            try
            {
                if (mod == null || string.IsNullOrWhiteSpace(mod.Folder)) return null;
                string path = Path.Combine(mod.Folder, "launcher.json");
                if (!File.Exists(path)) return null;
                return System.Text.Json.JsonSerializer.Deserialize<LauncherMeta>(File.ReadAllText(path, System.Text.Encoding.UTF8));
            }
            catch { return null; }
        }

        private void ApplyManagedSelectionToIni(ModInfo mod, bool selected)
        {
            LauncherMeta meta = ReadLauncherMeta(mod);
            if (meta == null || string.IsNullOrWhiteSpace(meta.iniPath) || string.IsNullOrWhiteSpace(meta.enabledKey)) return;
            string ini = Path.Combine(mod.Folder, meta.iniPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(ini)) return;
            Dictionary<string, string> d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            d[meta.enabledKey] = selected ? "true" : "false";
            if (!string.IsNullOrWhiteSpace(meta.languageKey)) d[meta.languageKey] = settings.Language;
            WriteIniValues(ini, d);
            if (ini.Equals(FindAiIni(), StringComparison.OrdinalIgnoreCase)) LoadAiSettings();
        }

        private void ApplyLanguageToManagedMods()
        {
            List<ModInfo> mods = ScanMods(settings.ManagedModsRoot, false);
            mods.AddRange(ScanMods(settings.WorkshopRoot, true));
            foreach (ModInfo mod in mods)
            {
                LauncherMeta meta = ReadLauncherMeta(mod);
                if (meta == null || string.IsNullOrWhiteSpace(meta.iniPath) || string.IsNullOrWhiteSpace(meta.languageKey)) continue;
                string ini = Path.Combine(mod.Folder, meta.iniPath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(ini)) continue;
                Dictionary<string, string> d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                d[meta.languageKey] = settings.Language;
                WriteIniValues(ini, d);
            }
        }

        private string FindManagedIni(ModInfo mod)
        {
            if (mod == null) return null;
            LauncherMeta meta = ReadLauncherMeta(mod);
            if (meta != null && !string.IsNullOrWhiteSpace(meta.iniPath))
            {
                string configured = Path.Combine(mod.Folder, meta.iniPath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(configured)) return configured;
            }
            try
            {
                string[] files = Directory.GetFiles(mod.Folder, "*.ini", SearchOption.TopDirectoryOnly);
                return files.FirstOrDefault();
            }
            catch { return null; }
        }

        private void OpenSelectedManagedIniEditor()
        {
            if (managedList == null || managedList.SelectedItems.Count == 0) return;
            ModInfo mod = managedList.SelectedItems[0].Tag as ModInfo;
            OpenIniEditor(mod);
        }

        private void OpenIniEditor(ModInfo mod)
        {
            if (mod == null) return;
            string ini = FindManagedIni(mod);
            if (ini == null)
            {
                MessageBox.Show(T("このMODには設定可能なINIがありません。", "This MOD has no configurable INI file."), Text);
                return;
            }

            List<IniEditorRow> rows;
            try { rows = ReadIniEditorRows(ini); }
            catch (Exception ex)
            {
                Logger.LogException("Open individual INI editor", ex);
                MessageBox.Show(T("INIを読み込めませんでした。", "The INI file could not be read.") + "\r\n" + ex.Message, Text);
                return;
            }
            if (rows.Count == 0)
            {
                MessageBox.Show(T("INIに設定項目が見つかりません。", "No settings were found in the INI file."), Text);
                return;
            }

            using (Form editor = new Form())
            {
                editor.Text = T("INI個別設定 - ", "Individual INI Settings - ") + (mod.DisplayName ?? Path.GetFileName(mod.Folder));
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

                Button save = MakeButton(T("保存", "Save"), 680, 12, 125, 34);
                Button cancel = MakeButton(T("キャンセル", "Cancel"), 820, 12, 125, 34);
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

                table.Controls.Add(MakeIniHeader(T("設定項目", "Setting")), 0, 0);
                table.Controls.Add(MakeIniHeader(T("値", "Value")), 1, 0);
                table.Controls.Add(MakeIniHeader(T("説明", "Description")), 2, 0);
                int rowIndex = 1;
                foreach (IniEditorRow row in rows)
                {
                    table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                    Label keyLabel = new Label();
                    keyLabel.Text = IniDisplayName(row.Key);
                    keyLabel.AutoSize = true;
                    keyLabel.MaximumSize = new Size(220, 0);
                    keyLabel.Margin = new Padding(3, 9, 3, 8);
                    keyLabel.ForeColor = Dw2Gold;
                    table.Controls.Add(keyLabel, 0, rowIndex);

                    row.Editor.Width = 205;
                    row.Editor.Margin = new Padding(3, 5, 3, 7);
                    row.Editor.BackColor = Dw2Void;
                    row.Editor.ForeColor = Dw2Text;
                    table.Controls.Add(row.Editor, 1, rowIndex);

                    Label description = new Label();
                    description.Text = settings.Language == "en" ? row.EnglishDescription : row.JapaneseDescription;
                    description.AutoSize = true;
                    description.MaximumSize = new Size(450, 0);
                    description.Margin = new Padding(3, 8, 3, 8);
                    description.ForeColor = Dw2Muted;
                    table.Controls.Add(description, 2, rowIndex);
                    rowIndex++;
                }

                save.Click += delegate
                {
                    Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (IniEditorRow row in rows) values[row.Key] = (row.Editor.Text ?? "").Trim();
                    try { File.Copy(ini, ini + ".launcher_backup", true); } catch { }
                    WriteIniValues(ini, values);
                    editor.DialogResult = DialogResult.OK;
                    editor.Close();
                };

                if (editor.ShowDialog(this) == DialogResult.OK)
                {
                    if (ini.Equals(FindAiIni(), StringComparison.OrdinalIgnoreCase)) LoadAiSettings();
                    SetStatus(T("INI個別設定を保存しました。", "Individual INI settings saved."));
                }
            }
        }

        private List<IniEditorRow> ReadIniEditorRows(string path)
        {
            List<IniEditorRow> result = new List<IniEditorRow>();
            List<string> comments = new List<string>();
            foreach (string raw in File.ReadAllLines(path, System.Text.Encoding.UTF8))
            {
                string line = raw.Trim();
                if (line.StartsWith("#") || line.StartsWith(";"))
                {
                    comments.Add(line.Substring(1).Trim());
                    continue;
                }
                if (line.Length == 0) { comments.Clear(); continue; }
                int eq = line.IndexOf('=');
                if (eq <= 0) { comments.Clear(); continue; }
                string key = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1).Trim();
                IniEditorRow row = new IniEditorRow();
                row.Key = key;
                row.JapaneseDescription = comments.Count == 0 ? IniJapaneseDescription(key) : string.Join(" ", comments.ToArray());
                row.EnglishDescription = IniEnglishDescription(key);
                row.Editor = BuildIniValueEditor(key, value);
                result.Add(row);
                comments.Clear();
            }
            return result;
        }

        private ComboBox BuildIniValueEditor(string key, string value)
        {
            ComboBox box = new ComboBox();
            string lower = (value ?? "").ToLowerInvariant();
            string[] options = null;
            if (lower == "true" || lower == "false") options = new string[] { "true", "false" };
            else if (key.Equals("Language", StringComparison.OrdinalIgnoreCase)) options = new string[] { "ja", "en" };
            else if (key.Equals("Mode", StringComparison.OrdinalIgnoreCase)) options = new string[] { "ai", "pass", "block", "allow" };
            else if (key.Equals("Fallback", StringComparison.OrdinalIgnoreCase)) options = new string[] { "hold", "pass", "allow" };
            else if (key.Equals("Backend", StringComparison.OrdinalIgnoreCase)) options = new string[] { "lmstudio", "openai" };
            if (options != null)
            {
                box.DropDownStyle = ComboBoxStyle.DropDownList;
                box.Items.AddRange(options);
                int index = Array.FindIndex(options, x => x.Equals(value, StringComparison.OrdinalIgnoreCase));
                box.SelectedIndex = index >= 0 ? index : 0;
            }
            else
            {
                box.DropDownStyle = ComboBoxStyle.DropDown;
                string[] suggestions = IniValueSuggestions(key, value);
                if (suggestions != null && suggestions.Length > 0) box.Items.AddRange(suggestions);
                box.Text = value ?? "";
            }
            return box;
        }

        private string[] IniValueSuggestions(string key, string current)
        {
            string[] common = null;
            switch ((key ?? "").ToLowerInvariant())
            {
                case "targetempireid": common = new string[] { "0" }; break;
                case "peacecontinuecacheseconds": common = new string[] { "60", "180", "300", "600" }; break;
                case "warstrategymemoryseconds": common = new string[] { "300", "600", "900", "1800" }; break;
                case "warsourceholdseconds": common = new string[] { "60", "180", "300", "600" }; break;
                case "warsourcepostdeclareseconds": common = new string[] { "30", "60", "120" }; break;
                case "warmilitarychangepercent": common = new string[] { "10", "20", "30", "40", "50" }; break;
                case "wargoalemergencymilitarylosspercent": common = new string[] { "20", "30", "40", "50" }; break;
                case "requestcooldownseconds": common = new string[] { "10", "20", "30", "60" }; break;
                case "decisionttlseconds": common = new string[] { "60", "120", "180", "300" }; break;
                case "maxoutputtokens": common = new string[] { "256", "384", "512", "1024" }; break;
                case "maxconcurrentlmrequests": common = new string[] { "1", "2", "3", "4" }; break;
                case "warqueuemaxageseconds": common = new string[] { "30", "50", "60", "120" }; break;
                case "warqueuemaxpending": common = new string[] { "1", "2", "3", "5" }; break;
                case "warqueuebackoffseconds": common = new string[] { "30", "60", "120", "300" }; break;
                case "wardeclareassistdelayseconds": common = new string[] { "5", "12", "20", "30" }; break;
                case "memoryrecalllimit": common = new string[] { "3", "5", "10", "20" }; break;
                case "advisortypewriterms": common = new string[] { "0", "10", "20", "30", "50" }; break;
                case "baseurl": common = new string[] { "http://127.0.0.1:1234/v1", "https://api.openai.com/v1" }; break;
            }
            if (common == null) return null;
            if (string.IsNullOrWhiteSpace(current) || common.Contains(current)) return common;
            return (new string[] { current }).Concat(common).ToArray();
        }

        private string IniDisplayName(string key)
        {
            string ja = key;
            switch ((key ?? "").ToLowerInvariant())
            {
                case "enabled": ja = "MOD全体"; break;
                case "language": ja = "表示言語"; break;
                case "warcontrolenabled": ja = "AI開戦判断"; break;
                case "peacecontrolenabled": ja = "AI停戦判断"; break;
                case "treatymonitorenabled": ja = "条約診断"; break;
                case "researchmonitorenabled": ja = "研究診断"; break;
                case "combatmonitorenabled": ja = "戦闘診断"; break;
                case "detailedloggingenabled": ja = "詳細ログ"; break;
                case "ultimatumenabled": ja = "最後通牒"; break;
                case "ultimatumtypewriter": ja = "最後通牒タイプ表示"; break;
                case "targetempireid": ja = "対象帝国ID"; break;
                case "mode": ja = "動作モード"; break;
                case "fallback": ja = "待機中の処理"; break;
                case "backend": ja = "AI接続先"; break;
                case "baseurl": ja = "API URL"; break;
                case "model": ja = "使用モデル"; break;
                case "memoryenabled": ja = "国家記憶"; break;
                case "loreenabled": ja = "Lore読込"; break;
                case "personalityhotreload": ja = "性格自動再読込"; break;
                case "advisorenabled": ja = "補佐官"; break;
                case "advisorbuttonvisible": ja = "補佐官ボタン"; break;
                case "advisortypewriterms": ja = "補佐官表示速度"; break;
                case "decisionttlseconds": ja = "判断有効時間"; break;
            }
            return settings.Language == "en" ? key : ja + "  (" + key + ")";
        }

        private string IniEnglishDescription(string key)
        {
            switch ((key ?? "").ToLowerInvariant())
            {
                case "enabled": return "Enables the entire MOD. When false, monitoring, databases and patches are not loaded.";
                case "language": return "Language used by the MOD UI and AI-generated text. ja = Japanese, en = English.";
                case "warcontrolenabled": return "Allows LM Studio to make war declaration decisions.";
                case "peacecontrolenabled": return "Allows LM Studio to make peace decisions.";
                case "treatymonitorenabled": return "Enables treaty decision diagnostics. False is normally recommended.";
                case "researchmonitorenabled": return "Enables research decision diagnostics. False is normally recommended.";
                case "combatmonitorenabled": return "Enables combat and target-selection diagnostics. False is normally recommended.";
                case "detailedloggingenabled": return "Writes detailed WAIT, DEFER and AI DECISION diagnostic logs.";
                case "ultimatumenabled": return "Shows a pre-war ultimatum that may allow payment or station transfer to avoid war.";
                case "ultimatumtypewriter": return "Displays AI-generated ultimatum dialogue with a typewriter effect.";
                case "targetempireid": return "0 or lower controls all NPC empires. A positive value limits testing to that Empire ID.";
                case "mode": return "ai = LM decision, pass = DW2 default, block = always reject, allow = always permit.";
                case "fallback": return "Action while waiting for the LM response: hold, pass through to DW2, or temporarily allow.";
                case "backend": return "AI service: local LM Studio or the OpenAI API.";
                case "baseurl": return "Base URL of the AI API. The displayed default is LM Studio's standard endpoint.";
                case "model": return "Model name used for diplomatic decisions. It must match the model loaded by the backend.";
                case "peacecontinuecacheseconds": return "Seconds to reuse CONTINUE_WAR for the same opponent.";
                case "warstrategymemoryseconds": return "Seconds to retain a war decision unless an important situation changes.";
                case "warsourceholdseconds": return "Seconds before an empire that returned HOLD may reconsider another war.";
                case "warsourcepostdeclareseconds": return "Delay before the declaring empire considers another war candidate.";
                case "warmilitarychangepercent": return "Military ship-count change percentage that triggers early reconsideration.";
                case "wargoalemergencymilitarylosspercent": return "Military loss percentage that permits emergency peace reconsideration before the war goal is met.";
                case "requestcooldownseconds": return "Minimum interval in seconds before the same empire pair can be sent to the LM again.";
                case "decisionttlseconds": return "Seconds for which a completed AI decision remains valid before it must be reconsidered.";
                case "maxoutputtokens": return "Maximum tokens the LM may generate for one answer.";
                case "maxconcurrentlmrequests": return "Maximum simultaneous LM requests. One is recommended for a local 4B model.";
                case "warqueuemaxageseconds": return "Maximum age of a queued war request before it is discarded.";
                case "warqueuemaxpending": return "Maximum number of pending war requests in addition to the active request.";
                case "warqueuebackoffseconds": return "Retry delay after the queue is full or a request expires.";
                case "wardeclareassistdelayseconds": return "Time to wait for DW2's native declaration before assisted execution begins.";
                case "memoryenabled": return "Stores empire memory and war history in SQLite.";
                case "loreenabled": return "Loads world and race history from the Lore folder at startup.";
                case "personalityhotreload": return "Checks changed personality files periodically instead of loading them only at startup.";
                case "memoryrecalllimit": return "Number of recent memories about the opposing empire supplied to the LM.";
                case "advisorenabled": return "Enables the in-game advisor system.";
                case "advisorbuttonvisible": return "Shows the floating advisor button in game.";
                case "advisortypewriterms": return "Milliseconds per character for advisor typewriter text. Lower values are faster.";
                default: return "Individual setting from " + key + ". Select or enter a value supported by this MOD.";
            }
        }

        private string IniJapaneseDescription(string key)
        {
            switch ((key ?? "").ToLowerInvariant())
            {
                case "advisorenabled": return "ゲーム内の補佐官システムを有効にします。";
                case "advisorbuttonvisible": return "ゲーム画面に補佐官を呼び出すフローティングボタンを表示します。";
                case "advisortypewriterms": return "補佐官の返答を1文字表示する間隔です。小さいほど速く表示されます。";
                case "decisionttlseconds": return "完了したAI判断を再判断せず再利用できる秒数です。";
                default: return key + " の個別設定です。このMODが対応している値を選択または入力してください。";
            }
        }
    }
}
