using System;
using System.Drawing;
using System.Windows.Forms;
using DW2ModLauncher.Core.Models;

namespace DW2ModLauncherBeta
{
    public partial class MainForm
    {
        private void BuildUi()
        {
            TableLayoutPanel shell = new TableLayoutPanel();
            shell.Dock = DockStyle.Fill;
            shell.Margin = new Padding(0);
            shell.Padding = new Padding(0);
            shell.ColumnCount = 1;
            shell.RowCount = 4;
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 92F));
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            Controls.Add(shell);

            Panel top = new Panel();
            top.Dock = DockStyle.Fill;
            top.Padding = new Padding(12, 10, 12, 8);
            top.BackColor = Dw2Void;
            shell.Controls.Add(top, 0, 0);

            Label title = new Label();
            title.Text = "DW2 MOD LAUNCHER  BETA";
            title.Font = new Font("Segoe UI Semibold", 16F, FontStyle.Bold);
            title.ForeColor = Dw2Gold;
            title.AutoSize = true;
            title.Location = new Point(14, 10);
            top.Controls.Add(title);

            Label subtitle = new Label();
            subtitle.Text = "MOD MANAGEMENT SYSTEM  //  METAPO";
            subtitle.Font = new Font("Segoe UI", 7.5F, FontStyle.Bold);
            subtitle.ForeColor = Dw2BlueGlow;
            subtitle.AutoSize = true;
            subtitle.Location = new Point(17, 39);
            top.Controls.Add(subtitle);

            Label langLabel = new Label();
            langLabel.Name = "LanguageCaption";
            langLabel.Text = "言語 / Language";
            langLabel.AutoSize = true;
            langLabel.Location = new Point(360, 16);
            top.Controls.Add(langLabel);

            languageCombo = new ComboBox();
            languageCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            languageCombo.Items.Add("日本語");
            languageCombo.Items.Add("English");
            languageCombo.Width = 130;
            languageCombo.Location = new Point(465, 12);
            languageCombo.SelectedIndex = settings.Language == "en" ? 1 : 0;
            languageCombo.SelectedIndexChanged += delegate
            {
                settings.Language = languageCombo.SelectedIndex == 1 ? "en" : "ja";
                ApplyLanguage();
                SaveSettings();
                ApplyLanguageToManagedMods();
                LoadAiSettings();
                AnalyzeConflicts();
                RefreshModStatusColumns();
                RefreshSelectedDetails();
                UpdateCommandPreview();
            };
            top.Controls.Add(languageCombo);

            refreshButton = MakeButton("再読込", 620, 10, 100, 30);
            refreshButton.Click += delegate { RefreshAll(); };
            top.Controls.Add(refreshButton);

            folderSettingsButton = MakeButton("検索フォルダー設定", 730, 10, 135, 30);
            folderSettingsButton.Click += delegate { tabs.SelectedTab = settingsTab; };
            top.Controls.Add(folderSettingsButton);

            playButton = MakeButton("DW2を起動", 875, 8, 260, 38);
            playButton.Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold);
            playButton.BackColor = Dw2Blue;
            playButton.MouseLeave += delegate { playButton.BackColor = Dw2Blue; };
            playButton.Click += delegate { LaunchGame(); };
            top.Controls.Add(playButton);

            gamePathLabel = new Label();
            gamePathLabel.AutoEllipsis = true;
            gamePathLabel.Location = new Point(15, 52);
            gamePathLabel.Size = new Size(530, 20);
            top.Controls.Add(gamePathLabel);

            workshopPathLabel = new Label();
            workshopPathLabel.AutoEllipsis = true;
            workshopPathLabel.Location = new Point(15, 72);
            workshopPathLabel.Size = new Size(760, 20);
            top.Controls.Add(workshopPathLabel);

            Panel accentLine = new Panel();
            accentLine.Dock = DockStyle.Bottom;
            accentLine.Height = 2;
            accentLine.BackColor = Dw2BlueGlow;
            top.Controls.Add(accentLine);

            tabs = new TabControl();
            tabs.Dock = DockStyle.Fill;
            tabs.Appearance = TabAppearance.FlatButtons;
            tabs.Padding = new Point(0, 0);
            tabs.DrawMode = TabDrawMode.Normal;
            tabs.SizeMode = TabSizeMode.Fixed;
            tabs.ItemSize = new Size(1, 1);
            shell.Controls.Add(tabs, 0, 2);

            managedTab = new TabPage("DW2 MODS");
            workshopTab = new TabPage("Workshop MODS");
            aiTab = new TabPage("AI Commander");
            settingsTab = new TabPage("設定");
            foreach (TabPage t in new TabPage[] { managedTab, workshopTab, aiTab, settingsTab })
            {
                t.BackColor = Dw2Panel;
                t.ForeColor = Dw2Text;
                tabs.TabPages.Add(t);
            }

            Panel navigation = new Panel();
            navigation.Dock = DockStyle.Fill;
            navigation.BackColor = Dw2Deep;
            navigation.Padding = new Padding(8, 4, 8, 4);
            shell.Controls.Add(navigation, 0, 1);

            managedNavigationButton = MakeButton("本体MODフォルダー", 8, 4, 175, 32);
            workshopNavigationButton = MakeButton("Steam Workshop", 191, 4, 160, 32);
            aiNavigationButton = MakeButton("AI Commander", 359, 4, 150, 32);
            settingsNavigationButton = MakeButton("設定", 517, 4, 110, 32);
            managedNavigationButton.Click += delegate { tabs.SelectedTab = managedTab; };
            workshopNavigationButton.Click += delegate { tabs.SelectedTab = workshopTab; };
            aiNavigationButton.Click += delegate { tabs.SelectedTab = aiTab; };
            settingsNavigationButton.Click += delegate { tabs.SelectedTab = settingsTab; };
            navigation.Controls.Add(managedNavigationButton);
            navigation.Controls.Add(workshopNavigationButton);
            navigation.Controls.Add(aiNavigationButton);
            navigation.Controls.Add(settingsNavigationButton);
            foreach (Button navigationButton in new Button[] { managedNavigationButton, workshopNavigationButton, aiNavigationButton, settingsNavigationButton })
                navigationButton.MouseLeave += delegate { RefreshNavigationButtons(); };
            tabs.SelectedIndexChanged += delegate { RefreshNavigationButtons(); };
            RefreshNavigationButtons();

            BuildModTab(managedTab, true);
            BuildModTab(workshopTab, false);
            BuildAiTab();
            BuildSettingsTab();

            statusLabel = new Label();
            statusLabel.Dock = DockStyle.Fill;
            statusLabel.Padding = new Padding(10, 6, 0, 0);
            statusLabel.BackColor = Dw2Void;
            statusLabel.ForeColor = Dw2Muted;
            shell.Controls.Add(statusLabel, 0, 3);
        }

        private Button MakeButton(string text, int x, int y, int w, int h)
        {
            Button b = new Button();
            b.Text = text;
            b.Location = new Point(x, y);
            b.Size = new Size(w, h);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = Dw2Blue;
            b.FlatAppearance.BorderSize = 1;
            b.BackColor = Dw2Steel;
            b.ForeColor = Dw2Text;
            b.Cursor = Cursors.Hand;
            b.MouseEnter += delegate { if (b.Enabled) { b.BackColor = Dw2Blue; b.FlatAppearance.BorderColor = Dw2BlueGlow; } };
            b.MouseLeave += delegate { b.BackColor = Dw2Steel; b.FlatAppearance.BorderColor = Dw2Blue; };
            return b;
        }

        private void RefreshNavigationButtons()
        {
            Button[] buttons = new Button[] { managedNavigationButton, workshopNavigationButton, aiNavigationButton, settingsNavigationButton };
            TabPage[] pages = new TabPage[] { managedTab, workshopTab, aiTab, settingsTab };
            for (int i = 0; i < buttons.Length; i++)
            {
                Button button = buttons[i];
                if (button == null) continue;
                bool selected = tabs != null && tabs.SelectedTab == pages[i];
                button.BackColor = selected ? Dw2Blue : Dw2Steel;
                button.ForeColor = selected ? Dw2Gold : Dw2Text;
                button.FlatAppearance.BorderColor = selected ? Dw2BlueGlow : Dw2Blue;
            }
        }

        private void BuildModTab(TabPage tab, bool managed)
        {
            SplitContainer split = new SplitContainer();
            split.Dock = DockStyle.Fill;
            split.SplitterDistance = 760;
            split.BackColor = tab.BackColor;
            tab.Controls.Add(split);

            ListView list = new ListView();
            list.Dock = DockStyle.Fill;
            list.View = View.Details;
            list.CheckBoxes = false;
            list.FullRowSelect = true;
            list.GridLines = false;
            list.AllowDrop = true;
            list.Scrollable = true;
            list.OwnerDraw = true;
            list.DrawColumnHeader += delegate(object sender, DrawListViewColumnHeaderEventArgs e)
            {
                using (SolidBrush back = new SolidBrush(Dw2Steel)) e.Graphics.FillRectangle(back, e.Bounds);
                using (Pen edge = new Pen(Dw2Blue)) e.Graphics.DrawRectangle(edge, e.Bounds.X, e.Bounds.Y, e.Bounds.Width - 1, e.Bounds.Height - 1);
                TextRenderer.DrawText(e.Graphics, e.Header.Text, list.Font, new Rectangle(e.Bounds.X + 7, e.Bounds.Y, e.Bounds.Width - 10, e.Bounds.Height),
                    Dw2Gold, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            };
            list.DrawItem += delegate(object sender, DrawListViewItemEventArgs e) { };
            list.DrawSubItem += delegate(object sender, DrawListViewSubItemEventArgs e)
            {
                bool selected = e.Item.Selected;
                Color background = selected ? Dw2Steel : e.SubItem.BackColor;
                Color foreground = selected ? Dw2Text : e.SubItem.ForeColor;
                using (SolidBrush back = new SolidBrush(background)) e.Graphics.FillRectangle(back, e.Bounds);

                Rectangle textBounds = e.Bounds;
                if (e.ColumnIndex == 0 && list.SmallImageList != null && !string.IsNullOrWhiteSpace(e.Item.ImageKey) && list.SmallImageList.Images.ContainsKey(e.Item.ImageKey))
                {
                    Image icon = list.SmallImageList.Images[e.Item.ImageKey];
                    int imageY = e.Bounds.Y + Math.Max(0, (e.Bounds.Height - icon.Height) / 2);
                    e.Graphics.DrawImage(icon, new Rectangle(e.Bounds.X + 3, imageY, icon.Width, icon.Height));
                    textBounds = new Rectangle(e.Bounds.X + icon.Width + 9, e.Bounds.Y, Math.Max(0, e.Bounds.Width - icon.Width - 12), e.Bounds.Height);
                }
                else textBounds = new Rectangle(e.Bounds.X + 6, e.Bounds.Y, Math.Max(0, e.Bounds.Width - 9), e.Bounds.Height);

                TextRenderer.DrawText(e.Graphics, e.SubItem.Text ?? "", list.Font, textBounds, foreground,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                using (Pen separator = new Pen(Dw2Steel))
                {
                    e.Graphics.DrawLine(separator, e.Bounds.Right - 1, e.Bounds.Top, e.Bounds.Right - 1, e.Bounds.Bottom);
                    e.Graphics.DrawLine(separator, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
                }
            };
            list.HideSelection = false;
            list.BackColor = Dw2Deep;
            list.ForeColor = Dw2Text;
            list.BorderStyle = BorderStyle.FixedSingle;
            list.Columns.Add("MOD名", 180);
            list.Columns.Add("Source", 105);
            list.Columns.Add("付属ツール", 115);
            list.Columns.Add("付属文書", 115);
            list.Columns.Add("MOD状態", 100);
            list.Columns.Add("競合状態", 115);
            list.Columns.Add("重複状態", 110);
            list.Columns.Add("更新状態", 95);
            list.Columns.Add("ロード順", 65);
            list.ColumnClick += delegate(object sender, ColumnClickEventArgs e)
            {
                int previous;
                bool ascending;
                if (listSortColumns.TryGetValue(list, out previous) && previous == e.Column)
                {
                    bool oldAscending;
                    ascending = !listSortAscending.TryGetValue(list, out oldAscending) || !oldAscending;
                }
                else ascending = true;
                listSortColumns[list] = e.Column;
                listSortAscending[list] = ascending;
                list.ListViewItemSorter = new ModListComparer(e.Column, ascending, IsModSelected);
                list.Sort();
                ApplyAlternatingRowColors(list);
            };
            list.ItemDrag += delegate(object sender, ItemDragEventArgs e) { list.DoDragDrop(e.Item, DragDropEffects.Move); };
            list.DragEnter += delegate(object sender, DragEventArgs e)
            {
                e.Effect = e.Data.GetDataPresent(typeof(ListViewItem)) ? DragDropEffects.Move : DragDropEffects.None;
            };
            list.DragDrop += delegate(object sender, DragEventArgs e)
            {
                ListViewItem moving = e.Data.GetData(typeof(ListViewItem)) as ListViewItem;
                if (moving == null || moving.ListView != list) return;
                Point client = list.PointToClient(new Point(e.X, e.Y));
                ListViewItem target = list.GetItemAt(client.X, client.Y);
                int index = target == null ? list.Items.Count - 1 : target.Index;
                list.ListViewItemSorter = null;
                list.Items.Remove(moving);
                list.Items.Insert(Math.Max(0, Math.Min(index, list.Items.Count)), moving);
                moving.Selected = true;
                ApplyAlternatingRowColors(list);
                SaveLoadOrderFromList(list);
                RefreshLoadOrderNumbers();
            };

            ImageList images = new ImageList();
            images.ImageSize = new Size(72, 48);
            images.ColorDepth = ColorDepth.Depth32Bit;
            list.SmallImageList = images;

            Panel leftTop = new Panel();
            leftTop.Dock = DockStyle.Fill;
            leftTop.Height = 44;
            leftTop.BackColor = Dw2Panel;
            Button open = MakeButton(managed ? "MODルート" : "Workshopルート", 8, 7, 125, 30);
            open.Click += delegate { OpenFolder(managed ? settings.ManagedModsRoot : settings.WorkshopRoot); };
            leftTop.Controls.Add(open);

            Button selectedFolder = MakeButton("選択MODフォルダー", 141, 7, 150, 30);
            selectedFolder.Enabled = false;
            selectedFolder.Click += delegate { OpenSelectedModFolder(list); };
            leftTop.Controls.Add(selectedFolder);

            Label hint = new Label();
            hint.Name = managed ? "ManagedListHint" : "WorkshopListHint";
            hint.AutoSize = true;
            hint.ForeColor = Dw2Gold;
            hint.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold);
            if (managed)
            {
                managedIniButton = MakeButton("INI個別設定", 299, 7, 115, 30);
                managedIniButton.Enabled = false;
                managedIniButton.Click += delegate { OpenSelectedManagedIniEditor(); };
                leftTop.Controls.Add(managedIniButton);
                managedDetailsButton = MakeButton("詳細", 422, 7, 70, 30);
                managedDetailsButton.Enabled = false;
                managedDetailsButton.Click += delegate { OpenSelectedModDetails(list); };
                leftTop.Controls.Add(managedDetailsButton);
                Button toolsButton = MakeButton(T("ツール実行", "Run Tool"), 500, 7, 90, 30);
                toolsButton.Name = "ManagedToolsButton";
                toolsButton.Enabled = false;
                toolsButton.Click += delegate { RunSelectedModTool(list); };
                leftTop.Controls.Add(toolsButton);
                Button documentsButton = MakeButton(T("文書を開く", "Open Docs"), 598, 7, 90, 30);
                documentsButton.Name = "ManagedDocumentsButton";
                documentsButton.Enabled = false;
                documentsButton.Click += delegate { OpenSelectedModDocument(list); };
                leftTop.Controls.Add(documentsButton);
                hint.Location = new Point(696, 13);
                hint.Text = T("↕ 行をドラッグしてロード順を変更", "↕ Drag rows to change load order");
            }
            else
            {
                workshopUpdateButton = MakeButton("更新確認", 299, 7, 95, 30);
                workshopUpdateButton.Click += delegate { BeginWorkshopUpdateCheck(true); };
                leftTop.Controls.Add(workshopUpdateButton);

                workshopSteamButton = MakeButton("Steamページ", 402, 7, 100, 30);
                workshopSteamButton.Click += delegate { OpenSelectedWorkshopPage(); };
                leftTop.Controls.Add(workshopSteamButton);

                workshopDetailsButton = MakeButton("詳細", 510, 7, 70, 30);
                workshopDetailsButton.Enabled = false;
                workshopDetailsButton.Click += delegate { OpenSelectedModDetails(list); };
                leftTop.Controls.Add(workshopDetailsButton);

                Button toolsButton = MakeButton(T("ツール実行", "Run Tool"), 588, 7, 90, 30);
                toolsButton.Name = "WorkshopToolsButton";
                toolsButton.Enabled = false;
                toolsButton.Click += delegate { RunSelectedModTool(list); };
                leftTop.Controls.Add(toolsButton);
                Button documentsButton = MakeButton(T("文書を開く", "Open Docs"), 686, 7, 90, 30);
                documentsButton.Name = "WorkshopDocumentsButton";
                documentsButton.Enabled = false;
                documentsButton.Click += delegate { OpenSelectedModDocument(list); };
                leftTop.Controls.Add(documentsButton);

                hint.Location = new Point(784, 13);
                hint.Text = T("↕ ドラッグでロード順変更", "↕ Drag to change load order");
            }
            leftTop.Controls.Add(hint);

            TableLayoutPanel listLayout = new TableLayoutPanel();
            listLayout.Dock = DockStyle.Fill;
            listLayout.Margin = new Padding(0);
            listLayout.Padding = new Padding(0);
            listLayout.ColumnCount = 1;
            listLayout.RowCount = 2;
            listLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            listLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
            listLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            listLayout.BackColor = Dw2Panel;
            listLayout.Controls.Add(leftTop, 0, 0);
            listLayout.Controls.Add(list, 0, 1);
            split.Panel1.Controls.Add(listLayout);

            Panel detail = new Panel();
            detail.Dock = DockStyle.Fill;
            detail.Padding = new Padding(14);
            detail.BackColor = Dw2PanelAlt;
            detail.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (Pen frame = new Pen(Dw2Blue)) e.Graphics.DrawRectangle(frame, 0, 0, detail.ClientSize.Width - 1, detail.ClientSize.Height - 1);
            };
            split.Panel2.Controls.Add(detail);

            PictureBox preview = new PictureBox();
            preview.Dock = DockStyle.Top;
            preview.Height = 240;
            preview.SizeMode = PictureBoxSizeMode.Zoom;
            preview.BackColor = Dw2Void;
            detail.Controls.Add(preview);

            Label name = new Label();
            name.Dock = DockStyle.Top;
            name.Height = 58;
            name.Padding = new Padding(0, 14, 0, 0);
            name.Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold);
            name.ForeColor = Dw2Gold;
            name.AutoEllipsis = true;
            detail.Controls.Add(name);
            name.BringToFront();

            Label desc = new Label();
            desc.Dock = DockStyle.Fill;
            desc.Padding = new Padding(0, 6, 0, 0);
            desc.ForeColor = Dw2Muted;
            detail.Controls.Add(desc);
            desc.BringToFront();

            list.SelectedIndexChanged += delegate
            {
                if (managed && managedIniButton != null)
                {
                    ModInfo selected = list.SelectedItems.Count == 0 ? null : list.SelectedItems[0].Tag as ModInfo;
                    managedIniButton.Enabled = FindManagedIni(selected) != null;
                }
                if (managed && managedDetailsButton != null) managedDetailsButton.Enabled = list.SelectedItems.Count > 0;
                if (!managed && workshopDetailsButton != null) workshopDetailsButton.Enabled = list.SelectedItems.Count > 0;
                selectedFolder.Enabled = list.SelectedItems.Count > 0;
                Control toolsButton = FindControlRecursive(leftTop, managed ? "ManagedToolsButton" : "WorkshopToolsButton");
                ModInfo selectedMod = list.SelectedItems.Count == 0 ? null : list.SelectedItems[0].Tag as ModInfo;
                if (toolsButton != null) toolsButton.Enabled = selectedMod != null && selectedMod.IncludedTools != null && selectedMod.IncludedTools.Count > 0;
                Control documentsButton = FindControlRecursive(leftTop, managed ? "ManagedDocumentsButton" : "WorkshopDocumentsButton");
                if (documentsButton != null) documentsButton.Enabled = selectedMod != null && selectedMod.IncludedDocuments != null && selectedMod.IncludedDocuments.Count > 0;
                if (list.SelectedItems.Count == 0) return;
                ModInfo mod = list.SelectedItems[0].Tag as ModInfo;
                ShowModDetails(mod, preview, name, desc);
            };
            list.DoubleClick += delegate { OpenSelectedModDetails(list); };
            list.MouseClick += delegate(object sender, MouseEventArgs e) { ShowStateDropDown(list, e.Location); };

            if (managed)
            {
                managedList = list;
                managedImages = images;
                managedPreview = preview;
                managedName = name;
                managedDesc = desc;
                managedOpenButton = open;
                managedSelectedFolderButton = selectedFolder;
            }
            else
            {
                workshopList = list;
                workshopImages = images;
                workshopPreview = preview;
                workshopName = name;
                workshopDesc = desc;
                workshopOpenButton = open;
                workshopSelectedFolderButton = selectedFolder;
            }
        }

        private void BuildAiTab()
        {
            Panel p = new Panel();
            p.Dock = DockStyle.Fill;
            p.AutoScroll = true;
            p.Padding = new Padding(22);
            aiTab.Controls.Add(p);

            Label header = new Label();
            header.Name = "AiHeader";
            header.Text = "AI Commander 基本設定";
            header.Font = new Font("Segoe UI Semibold", 14F, FontStyle.Bold);
            header.ForeColor = Dw2Gold;
            header.AutoSize = true;
            header.Location = new Point(24, 20);
            p.Controls.Add(header);

            aiIniPathLabel = new Label();
            aiIniPathLabel.Location = new Point(26, 58);
            aiIniPathLabel.Size = new Size(900, 22);
            aiIniPathLabel.AutoEllipsis = true;
            aiIniPathLabel.ForeColor = Dw2Muted;
            p.Controls.Add(aiIniPathLabel);

            aiEnabled = MakeCheckBox("MOD全体", 28, 100);
            aiWar = MakeCheckBox("AI開戦判断", 28, 136);
            aiPeace = MakeCheckBox("AI停戦判断", 28, 172);
            aiUltimatum = MakeCheckBox("最後通牒", 28, 208);
            aiAdvisor = MakeCheckBox("補佐官", 28, 244);
            p.Controls.Add(aiEnabled);
            p.Controls.Add(aiWar);
            p.Controls.Add(aiPeace);
            p.Controls.Add(aiUltimatum);
            p.Controls.Add(aiAdvisor);

            AddLabeledTextBox(p, "Backend", 320, 102, out aiBackend);
            AddLabeledTextBox(p, "Base URL", 320, 160, out aiBaseUrl);
            AddLabeledTextBox(p, "Model", 320, 218, out aiModel);

            reloadAiButton = MakeButton("INI再読込", 28, 310, 140, 34);
            saveAiButton = MakeButton("INIへ保存", 182, 310, 140, 34);
            reloadAiButton.Click += delegate { LoadAiSettings(); };
            saveAiButton.Click += delegate { SaveAiSettings(); };
            p.Controls.Add(reloadAiButton);
            p.Controls.Add(saveAiButton);

            Label note = new Label();
            note.Name = "AiNote";
            note.Location = new Point(28, 375);
            note.Size = new Size(900, 70);
            note.ForeColor = Dw2Muted;
            note.Text = "ランチャーの言語変更は Language=ja/en にも反映します。\r\nこのベータ版では主要なON/OFF項目だけをGUI化しています。";
            p.Controls.Add(note);
        }

        private CheckBox MakeCheckBox(string text, int x, int y)
        {
            CheckBox c = new CheckBox();
            c.Text = text;
            c.Location = new Point(x, y);
            c.AutoSize = true;
            c.ForeColor = Dw2Text;
            return c;
        }

        private void AddLabeledTextBox(Control parent, string label, int x, int y, out TextBox box)
        {
            Label l = new Label();
            l.Text = label;
            l.AutoSize = true;
            l.Location = new Point(x, y);
            parent.Controls.Add(l);
            box = new TextBox();
            box.Location = new Point(x, y + 22);
            box.Width = 520;
            box.BackColor = Dw2Void;
            box.ForeColor = Dw2Text;
            box.BorderStyle = BorderStyle.FixedSingle;
            parent.Controls.Add(box);
        }

        private void BuildSettingsTab()
        {
            Panel p = new Panel();
            p.Dock = DockStyle.Fill;
            p.AutoScroll = true;
            p.Padding = new Padding(22);
            settingsTab.Controls.Add(p);

            Label header = new Label();
            header.Name = "SettingsHeader";
            header.Text = "パスと起動設定";
            header.Font = new Font("Segoe UI Semibold", 14F, FontStyle.Bold);
            header.ForeColor = Dw2Gold;
            header.AutoSize = true;
            header.Location = new Point(24, 20);
            p.Controls.Add(header);

            AddPathRow(p, "DW2 Game Root", 74, out gameRootBox, delegate { BrowseFolderInto(gameRootBox); });
            AddPathRow(p, "Workshop Root", 132, out workshopRootBox, delegate { BrowseFolderInto(workshopRootBox); });
            AddPathRow(p, "DW2 MOD Root", 190, out managedRootBox, delegate { BrowseFolderInto(managedRootBox); });

            Label argLabel = new Label();
            argLabel.Name = "LaunchArgumentsLabel";
            argLabel.Text = "追加起動オプション";
            argLabel.Location = new Point(28, 260);
            argLabel.AutoSize = true;
            p.Controls.Add(argLabel);
            launchArgsBox = new TextBox();
            launchArgsBox.Location = new Point(28, 284);
            launchArgsBox.Size = new Size(870, 25);
            launchArgsBox.BackColor = Dw2Void;
            launchArgsBox.ForeColor = Dw2Text;
            launchArgsBox.BorderStyle = BorderStyle.FixedSingle;
            launchArgsBox.TextChanged += delegate { UpdateCommandPreview(); };
            p.Controls.Add(launchArgsBox);

            Label cmdLabel = new Label();
            cmdLabel.Name = "CommandPreviewLabel";
            cmdLabel.Text = "実際に使用する起動コマンド";
            cmdLabel.Location = new Point(28, 330);
            cmdLabel.AutoSize = true;
            p.Controls.Add(cmdLabel);
            commandPreviewBox = new TextBox();
            commandPreviewBox.Location = new Point(28, 354);
            commandPreviewBox.Size = new Size(870, 82);
            commandPreviewBox.Multiline = true;
            commandPreviewBox.ReadOnly = true;
            commandPreviewBox.BackColor = Dw2Void;
            commandPreviewBox.ForeColor = Dw2BlueGlow;
            commandPreviewBox.BorderStyle = BorderStyle.FixedSingle;
            p.Controls.Add(commandPreviewBox);

            detectButton = MakeButton("自動検出", 28, 468, 130, 34);
            saveSettingsButton = MakeButton("設定を保存", 172, 468, 130, 34);
            gameOpenButton = MakeButton("ゲームフォルダー", 316, 468, 150, 34);
            detectButton.Click += delegate { DetectPaths(true); RefreshAll(); };
            saveSettingsButton.Click += delegate { SaveSettingsFromUi(); RefreshAll(); };
            gameOpenButton.Click += delegate { OpenFolder(settings.GameRoot); };
            p.Controls.Add(detectButton);
            p.Controls.Add(saveSettingsButton);
            p.Controls.Add(gameOpenButton);

            Label profileLabel = new Label();
            profileLabel.Name = "ProfileLabel";
            profileLabel.Text = T("MODプロファイル", "MOD Profiles");
            profileLabel.Location = new Point(28, 522);
            profileLabel.AutoSize = true;
            profileLabel.ForeColor = Dw2Gold;
            p.Controls.Add(profileLabel);
            profileCombo = new ComboBox();
            profileCombo.Location = new Point(28, 548);
            profileCombo.Size = new Size(250, 25);
            profileCombo.DropDownStyle = ComboBoxStyle.DropDown;
            profileCombo.BackColor = Dw2Void;
            profileCombo.ForeColor = Dw2Text;
            p.Controls.Add(profileCombo);
            Button saveProfile = MakeButton(T("現在構成を保存", "Save Current"), 292, 545, 145, 31);
            Button applyProfile = MakeButton(T("構成を適用", "Apply Profile"), 449, 545, 120, 31);
            Button deleteProfile = MakeButton(T("削除", "Delete"), 581, 545, 80, 31);
            Button snapshot = MakeButton(T("スナップショット", "Snapshot"), 673, 545, 145, 31);
            Button restoreSnapshot = MakeButton(T("最新へ戻す", "Restore Latest"), 830, 545, 115, 31);
            saveProfile.Name = "SaveProfileButton";
            applyProfile.Name = "ApplyProfileButton";
            deleteProfile.Name = "DeleteProfileButton";
            snapshot.Name = "SnapshotButton";
            restoreSnapshot.Name = "RestoreSnapshotButton";
            saveProfile.Click += delegate { SaveCurrentProfile(); };
            applyProfile.Click += delegate { ApplySelectedProfile(); };
            deleteProfile.Click += delegate { DeleteSelectedProfile(); };
            snapshot.Click += delegate { CreateEnvironmentSnapshot(); };
            restoreSnapshot.Click += delegate { RestoreLatestSnapshot(); };
            p.Controls.Add(saveProfile);
            p.Controls.Add(applyProfile);
            p.Controls.Add(deleteProfile);
            p.Controls.Add(snapshot);
            p.Controls.Add(restoreSnapshot);
            RefreshProfileCombo();

            Label beta = new Label();
            beta.Name = "BetaNote";
            beta.Location = new Point(28, 610);
            beta.Size = new Size(900, 80);
            beta.ForeColor = Dw2Muted;
            beta.Text = T("v0.4.6: インストーラー・付属文書・管理ファイルをゲームデータ競合の判定対象から除外しました。", "v0.4.6 excludes installers, documents and launcher metadata from game-data conflict detection.");
            p.Controls.Add(beta);
        }

        private void AddPathRow(Control parent, string labelText, int y, out TextBox box, EventHandler browse)
        {
            Label l = new Label();
            l.Text = labelText;
            l.Location = new Point(28, y);
            l.Size = new Size(160, 25);
            parent.Controls.Add(l);
            box = new TextBox();
            box.Location = new Point(190, y - 2);
            box.Size = new Size(610, 25);
            box.BackColor = Dw2Void;
            box.ForeColor = Dw2Text;
            box.BorderStyle = BorderStyle.FixedSingle;
            parent.Controls.Add(box);
            Button b = MakeButton("...", 816, y - 4, 50, 29);
            b.Click += browse;
            parent.Controls.Add(b);
        }

        private Label MakeIniHeader(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.Font = new Font(Font, FontStyle.Bold);
            label.ForeColor = Dw2BlueGlow;
            label.Margin = new Padding(3, 3, 3, 10);
            return label;
        }

        private void FitInitialListLayout()
        {
            foreach (ListView list in new ListView[] { managedList, workshopList })
            {
                SplitContainer split = list == null || list.Parent == null ? null : list.Parent.Parent as SplitContainer;
                if (split == null || split.ClientSize.Width <= 0) continue;
                int maximum = Math.Max(split.Panel1MinSize, split.ClientSize.Width - split.Panel2MinSize - split.SplitterWidth);
                int desired = Math.Min(1040, maximum);
                if (desired >= split.Panel1MinSize && desired <= maximum) split.SplitterDistance = desired;
            }
        }

        private void ApplyLanguage()
        {
            if (managedTab == null) return;
            managedTab.Text = T("本体MODフォルダー", "Game MOD Folder");
            workshopTab.Text = T("Steam Workshop", "STEAM WORKSHOP");
            aiTab.Text = "AI Commander";
            settingsTab.Text = T("設定", "Settings");
            refreshButton.Text = T("再読込", "Refresh");
            playButton.Text = T("DW2を起動", "PLAY DW2");
            if (managedNavigationButton != null) managedNavigationButton.Text = T("本体MODフォルダー", "Game MOD Folder");
            if (workshopNavigationButton != null) workshopNavigationButton.Text = "Steam Workshop";
            if (aiNavigationButton != null) aiNavigationButton.Text = "AI Commander";
            if (settingsNavigationButton != null) settingsNavigationButton.Text = T("設定", "Settings");
            RefreshNavigationButtons();
            if (folderSettingsButton != null) folderSettingsButton.Text = T("検索フォルダー設定", "Scan Folders");
            if (managedOpenButton != null) managedOpenButton.Text = T("MODルート", "MOD Root");
            if (managedSelectedFolderButton != null) managedSelectedFolderButton.Text = T("選択MODフォルダー", "Selected MOD Folder");
            if (managedIniButton != null) managedIniButton.Text = T("INI個別設定", "INI Settings");
            if (workshopOpenButton != null) workshopOpenButton.Text = T("Workshopルート", "Workshop Root");
            if (workshopSelectedFolderButton != null) workshopSelectedFolderButton.Text = T("選択MODフォルダー", "Selected MOD Folder");
            if (workshopUpdateButton != null) workshopUpdateButton.Text = T("更新確認", "Check Updates");
            if (workshopSteamButton != null) workshopSteamButton.Text = T("Steamページ", "Steam Page");
            if (managedDetailsButton != null) managedDetailsButton.Text = T("詳細", "Details");
            if (workshopDetailsButton != null) workshopDetailsButton.Text = T("詳細", "Details");
            Control managedToolsButton = FindControlRecursive(this, "ManagedToolsButton");
            if (managedToolsButton != null) managedToolsButton.Text = T("ツール実行", "Run Tool");
            Control workshopToolsButton = FindControlRecursive(this, "WorkshopToolsButton");
            if (workshopToolsButton != null) workshopToolsButton.Text = T("ツール実行", "Run Tool");
            Control managedDocumentsButton = FindControlRecursive(this, "ManagedDocumentsButton");
            if (managedDocumentsButton != null) managedDocumentsButton.Text = T("文書を開く", "Open Docs");
            Control workshopDocumentsButton = FindControlRecursive(this, "WorkshopDocumentsButton");
            if (workshopDocumentsButton != null) workshopDocumentsButton.Text = T("文書を開く", "Open Docs");
            if (saveAiButton != null) saveAiButton.Text = T("INIへ保存", "Save INI");
            if (reloadAiButton != null) reloadAiButton.Text = T("INI再読込", "Reload INI");
            if (detectButton != null) detectButton.Text = T("自動検出", "Auto Detect");
            if (saveSettingsButton != null) saveSettingsButton.Text = T("設定を保存", "Save Settings");
            if (gameOpenButton != null) gameOpenButton.Text = T("ゲームフォルダー", "Game Folder");
            if (aiEnabled != null) aiEnabled.Text = T("MOD全体", "Enable MOD");
            if (aiWar != null) aiWar.Text = T("AI開戦判断", "AI War Decisions");
            if (aiPeace != null) aiPeace.Text = T("AI停戦判断", "AI Peace Decisions");
            if (aiUltimatum != null) aiUltimatum.Text = T("最後通牒", "Ultimatums");
            if (aiAdvisor != null) aiAdvisor.Text = T("補佐官", "Advisor");
            Control aiHeader = FindControlRecursive(this, "AiHeader");
            if (aiHeader != null) aiHeader.Text = T("AI Commander 基本設定", "AI Commander Basic Settings");
            Control settingsHeader = FindControlRecursive(this, "SettingsHeader");
            if (settingsHeader != null) settingsHeader.Text = T("パスと起動設定", "Paths and Launch Settings");
            Control launchArgumentsLabel = FindControlRecursive(this, "LaunchArgumentsLabel");
            if (launchArgumentsLabel != null) launchArgumentsLabel.Text = T("追加起動オプション", "Additional Launch Arguments");
            Control commandPreviewLabel = FindControlRecursive(this, "CommandPreviewLabel");
            if (commandPreviewLabel != null) commandPreviewLabel.Text = T("実際に使用する起動コマンド", "Effective Launch Command");
            Control profileLabel = FindControlRecursive(this, "ProfileLabel");
            if (profileLabel != null) profileLabel.Text = T("MODプロファイル", "MOD Profiles");
            Control saveProfileButton = FindControlRecursive(this, "SaveProfileButton");
            if (saveProfileButton != null) saveProfileButton.Text = T("現在構成を保存", "Save Current");
            Control applyProfileButton = FindControlRecursive(this, "ApplyProfileButton");
            if (applyProfileButton != null) applyProfileButton.Text = T("構成を適用", "Apply Profile");
            Control deleteProfileButton = FindControlRecursive(this, "DeleteProfileButton");
            if (deleteProfileButton != null) deleteProfileButton.Text = T("削除", "Delete");
            Control snapshotButton = FindControlRecursive(this, "SnapshotButton");
            if (snapshotButton != null) snapshotButton.Text = T("スナップショット", "Snapshot");
            Control restoreSnapshotButton = FindControlRecursive(this, "RestoreSnapshotButton");
            if (restoreSnapshotButton != null) restoreSnapshotButton.Text = T("最新へ戻す", "Restore Latest");
            Control managedListHint = FindControlRecursive(this, "ManagedListHint");
            if (managedListHint != null) managedListHint.Text = T("↕ 行をドラッグしてロード順を変更", "↕ Drag rows to change load order");
            Control workshopListHint = FindControlRecursive(this, "WorkshopListHint");
            if (workshopListHint != null) workshopListHint.Text = T("↕ ドラッグでロード順変更", "↕ Drag to change load order");

            if (currentManagedMods != null)
                foreach (ModInfo mod in currentManagedMods) if (mod != null) mod.SourceName = T("本体MODフォルダー", "Game MOD Folder");
            if (currentWorkshopMods != null)
                foreach (ModInfo mod in currentWorkshopMods) if (mod != null) mod.SourceName = "Steam Workshop";
            if (managedList != null && managedList.Columns.Count >= 9)
            {
                managedList.Columns[0].Text = T("MOD名", "MOD Name");
                managedList.Columns[1].Text = T("取得元", "Source");
                managedList.Columns[2].Text = T("付属ツール", "Included Tools");
                managedList.Columns[3].Text = T("付属文書", "Included Docs");
                managedList.Columns[4].Text = T("MOD状態", "MOD State");
                managedList.Columns[5].Text = T("競合状態", "Conflict State");
                managedList.Columns[6].Text = T("重複状態", "Duplicate State");
                managedList.Columns[7].Text = T("更新状態", "Update State");
                managedList.Columns[8].Text = T("ロード順", "Load Order");
            }
            if (workshopList != null && workshopList.Columns.Count >= 9)
            {
                workshopList.Columns[0].Text = T("MOD名", "MOD Name");
                workshopList.Columns[1].Text = T("取得元", "Source");
                workshopList.Columns[2].Text = T("付属ツール", "Included Tools");
                workshopList.Columns[3].Text = T("付属文書", "Included Docs");
                workshopList.Columns[4].Text = T("MOD状態", "MOD State");
                workshopList.Columns[5].Text = T("競合状態", "Conflict State");
                workshopList.Columns[6].Text = T("重複状態", "Duplicate State");
                workshopList.Columns[7].Text = T("更新状態", "Update State");
                workshopList.Columns[8].Text = T("ロード順", "Load Order");
            }

            RefreshListSourceText(managedList);
            RefreshListSourceText(workshopList);

            Control aiNote = FindControlRecursive(this, "AiNote");
            if (aiNote != null) aiNote.Text = T(
                "ランチャーの言語変更は Language=ja/en にも反映します。\r\nこのベータ版では主要なON/OFF項目だけをGUI化しています。",
                "Launcher language also updates Language=ja/en.\r\nThis beta exposes the main AI Commander switches in the GUI.");
            Control beta = FindControlRecursive(this, "BetaNote");
            if (beta != null) beta.Text = T(
                "v0.4.6: インストーラー・付属文書・管理ファイルをゲームデータ競合の判定対象から除外しました。",
                "v0.4.6 excludes installers, documents and launcher metadata from game-data conflict detection.");
        }

        private void RefreshListSourceText(ListView list)
        {
            if (list == null) return;
            foreach (ListViewItem item in list.Items)
            {
                ModInfo mod = item.Tag as ModInfo;
                if (mod != null && item.SubItems.Count > 1) item.SubItems[1].Text = mod.SourceName ?? "";
            }
            list.Invalidate();
        }

        private Control FindControlRecursive(Control root, string name)
        {
            foreach (Control c in root.Controls)
            {
                if (c.Name == name) return c;
                Control found = FindControlRecursive(c, name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
