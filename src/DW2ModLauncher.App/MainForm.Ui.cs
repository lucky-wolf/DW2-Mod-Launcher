using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DW2ModLauncher.Core.Models;
using DW2ModLauncher.Core.Services;

namespace DW2ModLauncher.App
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
            shell.RowCount = 3;
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 92F));
            shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            Controls.Add(shell);

            Panel top = new Panel();
            top.Dock = DockStyle.Fill;
            top.Padding = new Padding(12, 10, 12, 8);
            top.BackColor = Dw2Void;
            shell.Controls.Add(top, 0, 0);

            Label title = new Label();
            title.Text = "DW2 MOD LAUNCHER";
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

            // The language list itself, and each entry's display name, come
            // entirely from whatever "_displayName" a Languages/*.json pack
            // declares - adding a language never requires a code change here.
            List<string> languageCodes = Localization.AvailableLanguageCodes().ToList();
            languageCombo = new ComboBox();
            languageCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            languageCombo.DrawMode = DrawMode.OwnerDrawFixed;
            languageCombo.ItemHeight = 22;
            languageCombo.DropDownWidth = 150;
            foreach (string code in languageCodes) languageCombo.Items.Add(code);
            languageCombo.DrawItem += delegate (object sender, DrawItemEventArgs e)
            {
                if (e.Index < 0 || e.Index >= languageCodes.Count) { e.DrawBackground(); return; }
                string itemCode = languageCodes[e.Index];
                string codeLabel = itemCode.ToUpperInvariant();
                e.DrawBackground();
                bool isClosedButton = (e.State & DrawItemState.ComboBoxEdit) != 0;
                if (isClosedButton)
                {
                    TextRenderer.DrawText(e.Graphics, codeLabel, languageCombo.Font, e.Bounds, e.ForeColor,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
                else
                {
                    TextRenderer.DrawText(e.Graphics, codeLabel, languageCombo.Font, new Rectangle(e.Bounds.X + 4, e.Bounds.Y, 28, e.Bounds.Height),
                        e.ForeColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                    TextRenderer.DrawText(e.Graphics, Localization.DisplayNameFor(itemCode), languageCombo.Font,
                        new Rectangle(e.Bounds.X + 34, e.Bounds.Y, e.Bounds.Width - 38, e.Bounds.Height),
                        e.ForeColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                }
                e.DrawFocusRectangle();
            };
            languageCombo.Width = 46;
            languageCombo.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            languageCombo.SelectedIndex = Math.Max(0, languageCodes.IndexOf(settings.Language));
            languageCombo.SelectedIndexChanged += delegate
            {
                int index = languageCombo.SelectedIndex;
                if (index < 0 || index >= languageCodes.Count) return;
                settings.Language = languageCodes[index];
                ApplyLanguage();
                SaveSettings();
                AnalyzeConflicts();
                RefreshModStatusColumns();
                RefreshSelectedDetails();
                UpdateCommandPreview();
            };
            top.Controls.Add(languageCombo);

            // The whole button cluster is anchored to the top-right corner so it
            // hugs the right edge of the window instead of trailing off with a
            // gap on wider screens.
            int rightMargin = 14;
            int buttonTop = 10;

            playButton = MakeButton(T("PlayButton"), 0, 8, 260, 38);
            playButton.Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold);
            playButton.BackColor = Dw2Blue;
            playButton.MouseLeave += delegate { playButton.BackColor = Dw2Blue; };
            playButton.Click += delegate { LaunchGame(); };
            playButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            top.Controls.Add(playButton);

            refreshButton = MakeButton(T("Refresh"), 0, buttonTop, 100, 30);
            refreshButton.Click += delegate { RefreshAll(); };
            refreshButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            top.Controls.Add(refreshButton);

            settingsNavigationButton = MakeButton(T("Settings"), 0, buttonTop, 110, 30);
            settingsNavigationButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            top.Controls.Add(settingsNavigationButton);

            modsNavigationButton = MakeButton(T("Mods"), 0, buttonTop, 110, 30);
            modsNavigationButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            top.Controls.Add(modsNavigationButton);

            int panelWidth = top.ClientSize.Width > 0 ? top.ClientSize.Width : ClientSize.Width;
            int cursorX = panelWidth - rightMargin;
            cursorX -= playButton.Width; playButton.Location = new Point(cursorX, playButton.Top);
            cursorX -= 10 + refreshButton.Width; refreshButton.Location = new Point(cursorX, refreshButton.Top);
            cursorX -= 10 + settingsNavigationButton.Width; settingsNavigationButton.Location = new Point(cursorX, settingsNavigationButton.Top);
            cursorX -= 10 + modsNavigationButton.Width; modsNavigationButton.Location = new Point(cursorX, modsNavigationButton.Top);
            cursorX -= 16 + languageCombo.Width; languageCombo.Location = new Point(cursorX, 12);

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
            shell.Controls.Add(tabs, 0, 1);

            modsTab = new TabPage(T("Mods"));
            settingsTab = new TabPage(T("Settings"));
            foreach (TabPage t in new TabPage[] { modsTab, settingsTab })
            {
                t.BackColor = Dw2Panel;
                t.ForeColor = Dw2Text;
                tabs.TabPages.Add(t);
            }

            modsNavigationButton.Click += delegate { tabs.SelectedTab = modsTab; };
            settingsNavigationButton.Click += delegate { tabs.SelectedTab = settingsTab; };
            foreach (Button navigationButton in new Button[] { modsNavigationButton, settingsNavigationButton })
                navigationButton.MouseLeave += delegate { RefreshNavigationButtons(); };
            tabs.SelectedIndexChanged += delegate { RefreshNavigationButtons(); };
            RefreshNavigationButtons();

            BuildModsTab(modsTab);
            BuildSettingsTab();

            statusLabel = new Label();
            statusLabel.Dock = DockStyle.Fill;
            statusLabel.Padding = new Padding(10, 6, 0, 0);
            statusLabel.BackColor = Dw2Void;
            statusLabel.ForeColor = Dw2Muted;
            shell.Controls.Add(statusLabel, 0, 2);
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
            Button[] buttons = new Button[] { modsNavigationButton, settingsNavigationButton };
            TabPage[] pages = new TabPage[] { modsTab, settingsTab };
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

        private void BuildModsTab(TabPage tab)
        {
            SplitContainer split = new SplitContainer();
            split.Dock = DockStyle.Fill;
            split.SplitterDistance = 700;
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
            list.DrawColumnHeader += delegate (object sender, DrawListViewColumnHeaderEventArgs e)
            {
                using (SolidBrush back = new SolidBrush(Dw2Steel)) e.Graphics.FillRectangle(back, e.Bounds);
                using (Pen edge = new Pen(Dw2Blue)) e.Graphics.DrawRectangle(edge, e.Bounds.X, e.Bounds.Y, e.Bounds.Width - 1, e.Bounds.Height - 1);
                TextRenderer.DrawText(e.Graphics, e.Header.Text, list.Font, new Rectangle(e.Bounds.X + 7, e.Bounds.Y, e.Bounds.Width - 10, e.Bounds.Height),
                    Dw2Gold, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            };
            list.DrawItem += delegate (object sender, DrawListViewItemEventArgs e) { };
            list.DrawSubItem += delegate (object sender, DrawListViewSubItemEventArgs e)
            {
                bool selected = e.Item.Selected;
                Color background = selected ? Dw2Steel : e.SubItem.BackColor;
                Color foreground = selected ? Dw2Text : e.SubItem.ForeColor;
                using (SolidBrush back = new SolidBrush(background)) e.Graphics.FillRectangle(back, e.Bounds);

                Rectangle textBounds = e.Bounds;
                bool centered = e.ColumnIndex == ColumnModState || e.ColumnIndex == ColumnHealth;
                if (e.ColumnIndex == 0 && list.SmallImageList != null && !string.IsNullOrWhiteSpace(e.Item.ImageKey) && list.SmallImageList.Images.ContainsKey(e.Item.ImageKey))
                {
                    Image icon = list.SmallImageList.Images[e.Item.ImageKey];
                    int imageY = e.Bounds.Y + Math.Max(0, (e.Bounds.Height - icon.Height) / 2);
                    e.Graphics.DrawImage(icon, new Rectangle(e.Bounds.X + 3, imageY, icon.Width, icon.Height));
                    textBounds = new Rectangle(e.Bounds.X + icon.Width + 9, e.Bounds.Y, Math.Max(0, e.Bounds.Width - icon.Width - 12), e.Bounds.Height);
                }
                else if (centered) textBounds = e.Bounds;
                else textBounds = new Rectangle(e.Bounds.X + 6, e.Bounds.Y, Math.Max(0, e.Bounds.Width - 9), e.Bounds.Height);

                TextRenderer.DrawText(e.Graphics, e.SubItem.Text ?? "", list.Font, textBounds, foreground,
                    (centered ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left) | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
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
            // ColumnHeader widths aren't Controls, so they're untouched by the
            // DPI Scale() pass applied to the rest of the tree in the constructor;
            // scale them here by the same real-DPI ratio so they don't end up
            // undersized relative to the (now DPI-correct) text they hold.
            float columnDpiScale = DeviceDpi / 96f;
            Func<int, int> col = w => (int)Math.Round(w * columnDpiScale);
            list.Columns.Add(T("ModName"), col(320));
            list.Columns.Add(T("Source"), col(160));
            list.Columns.Add(T("ModState"), col(110));
            list.Columns.Add(T("Health"), col(95));
            list.Columns.Add(T("LoadOrder"), col(100));
            // Mod Name absorbs whatever width the other (fixed) columns don't use,
            // so the header row's background always reaches the right edge instead
            // of leaving a plain white gap after the last column.
            list.Resize += delegate { FitModListColumns(list); };
            list.ColumnWidthChanged += delegate (object sender, ColumnWidthChangedEventArgs e)
            {
                if (e.ColumnIndex != 0) FitModListColumns(list);
            };
            list.ColumnClick += delegate (object sender, ColumnClickEventArgs e)
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
                list.ListViewItemSorter = new ModListComparer(e.Column, ascending, IsModSelected, HealthSeverity);
                list.Sort();
                ApplyAlternatingRowColors(list);
            };
            list.ItemDrag += delegate (object sender, ItemDragEventArgs e) { list.DoDragDrop(e.Item, DragDropEffects.Move); };
            list.DragEnter += delegate (object sender, DragEventArgs e)
            {
                e.Effect = e.Data.GetDataPresent(typeof(ListViewItem)) ? DragDropEffects.Move : DragDropEffects.None;
            };
            list.DragDrop += delegate (object sender, DragEventArgs e)
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

            selectedFolderButton = MakeButton(T("SelectedModFolder"), 8, 7, 150, 30);
            selectedFolderButton.Enabled = false;
            selectedFolderButton.Click += delegate { OpenSelectedModFolder(list); };
            leftTop.Controls.Add(selectedFolderButton);

            modSettingsButton = MakeButton(T("ModSettings"), 166, 7, 115, 30);
            modSettingsButton.Enabled = false;
            modSettingsButton.Click += delegate { OpenSelectedModConfigEditor(); };
            leftTop.Controls.Add(modSettingsButton);

            Button documentsButton = MakeButton(T("OpenDocs"), 289, 7, 90, 30);
            documentsButton.Name = "ModDocumentsButton";
            documentsButton.Enabled = false;
            documentsButton.Click += delegate { OpenSelectedModDocument(list); };
            leftTop.Controls.Add(documentsButton);

            workshopUpdateButton = MakeButton(T("CheckUpdates"), 387, 7, 130, 30);
            workshopUpdateButton.Click += delegate { BeginWorkshopUpdateCheck(true); };
            leftTop.Controls.Add(workshopUpdateButton);

            publishButton = MakeButton(T("PublishToWorkshop"), 525, 7, 150, 30);
            publishButton.Enabled = false;
            publishButton.Click += delegate { PublishSelectedMod(); };
            leftTop.Controls.Add(publishButton);

            Label hint = new Label();
            hint.Name = "ModListHint";
            hint.AutoSize = true;
            hint.ForeColor = Dw2Gold;
            hint.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold);
            hint.Location = new Point(685, 13);
            hint.Text = T("DragRowsToChangeLoadOrder");
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
            detail.Paint += delegate (object sender, PaintEventArgs e)
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

            // A colored callout, shown only when the selected mod has conflicts,
            // duplicates or a pending update - sits right under the title so
            // problems are the first thing noticed, not buried in the text below.
            Panel problemsPanel = new Panel();
            problemsPanel.Dock = DockStyle.Top;
            problemsPanel.Visible = false;
            problemsPanel.Padding = new Padding(10, 8, 10, 8);
            detail.Controls.Add(problemsPanel);
            problemsPanel.BringToFront();

            Label problemsLabel = new Label();
            problemsLabel.Dock = DockStyle.Fill;
            problemsLabel.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold);
            problemsLabel.TextAlign = ContentAlignment.TopLeft;
            problemsPanel.Controls.Add(problemsLabel);

            // A plain multiline TextBox instead of a Label so long mod details
            // (conflicts, duplicate locations, included tools/docs, etc.) scroll
            // instead of being clipped by the panel.
            TextBox desc = new TextBox();
            desc.Dock = DockStyle.Fill;
            desc.Multiline = true;
            desc.ReadOnly = true;
            desc.ScrollBars = ScrollBars.Vertical;
            desc.BorderStyle = BorderStyle.None;
            desc.BackColor = Dw2PanelAlt;
            desc.ForeColor = Dw2Muted;
            detail.Controls.Add(desc);
            desc.BringToFront();

            list.SelectedIndexChanged += delegate
            {
                ModInfo selectedMod = list.SelectedItems.Count == 0 ? null : list.SelectedItems[0].Tag as ModInfo;
                if (modSettingsButton != null) modSettingsButton.Enabled = ModHasConfigurableSettings(selectedMod);
                if (publishButton != null) publishButton.Enabled = selectedMod != null && !selectedMod.IsWorkshop;
                selectedFolderButton.Enabled = list.SelectedItems.Count > 0;
                Control documentsButton = FindControlRecursive(leftTop, "ModDocumentsButton");
                if (documentsButton != null) documentsButton.Enabled = selectedMod != null && selectedMod.IncludedDocuments != null && selectedMod.IncludedDocuments.Count > 0;
                if (list.SelectedItems.Count == 0) return;
                ShowModDetails(selectedMod, preview, name, problemsPanel, problemsLabel, desc);
            };
            list.DoubleClick += delegate { OpenSelectedModConfigEditor(); };
            list.MouseClick += delegate (object sender, MouseEventArgs e) { ToggleModStateAtLocation(list, e.Location); };

            modList = list;
            modImages = images;
            modPreview = preview;
            modName = name;
            modProblemsPanel = problemsPanel;
            modProblemsLabel = problemsLabel;
            modDesc = desc;
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
            header.Text = T("PathsAndLaunchSettings");
            header.Font = new Font("Segoe UI Semibold", 14F, FontStyle.Bold);
            header.ForeColor = Dw2Gold;
            header.AutoSize = true;
            header.Location = new Point(24, 20);
            p.Controls.Add(header);

            AddPathRow(p, "DW2 Game Root", 74, out gameRootBox, delegate { BrowseFolderInto(gameRootBox); });
            AddPathRow(p, "Workshop Root", 132, out workshopRootBox, delegate { BrowseFolderInto(workshopRootBox); });
            AddPathRow(p, "DW2 Mod Root", 190, out managedRootBox, delegate { BrowseFolderInto(managedRootBox); });

            Label argLabel = new Label();
            argLabel.Name = "LaunchArgumentsLabel";
            argLabel.Text = T("AdditionalLaunchArguments");
            argLabel.Location = new Point(28, 260);
            argLabel.AutoSize = true;
            p.Controls.Add(argLabel);
            launchArgsBox = new TextBox();
            launchArgsBox.Location = new Point(28, 284);
            launchArgsBox.Size = new Size(520, 25);
            launchArgsBox.BackColor = Dw2Void;
            launchArgsBox.ForeColor = Dw2Text;
            launchArgsBox.BorderStyle = BorderStyle.FixedSingle;
            launchArgsBox.TextChanged += delegate { UpdateCommandPreview(); };
            p.Controls.Add(launchArgsBox);
            importSteamButton = MakeButton(T("ImportFromSteam"), 558, 281, 170, 30);
            importSteamButton.Click += delegate { ImportSteamLaunchOptions(); };
            p.Controls.Add(importSteamButton);
            resetLaunchButton = MakeButton(T("ResetLaunchOptions"), 738, 281, 160, 30);
            resetLaunchButton.Click += delegate
            {
                if (launchArgsBox != null) launchArgsBox.Text = "";
                if (launchEnvBox != null) launchEnvBox.Text = "";
            };
            p.Controls.Add(resetLaunchButton);

            Label envLabel = new Label();
            envLabel.Name = "EnvironmentLabel";
            envLabel.Text = T("EnvironmentVariables");
            envLabel.Location = new Point(28, 318);
            envLabel.AutoSize = true;
            p.Controls.Add(envLabel);
            launchEnvBox = new TextBox();
            launchEnvBox.Location = new Point(28, 340);
            launchEnvBox.Size = new Size(870, 50);
            launchEnvBox.Multiline = true;
            launchEnvBox.AcceptsReturn = true;
            launchEnvBox.ScrollBars = ScrollBars.Vertical;
            launchEnvBox.BackColor = Dw2Void;
            launchEnvBox.ForeColor = Dw2Text;
            launchEnvBox.BorderStyle = BorderStyle.FixedSingle;
            p.Controls.Add(launchEnvBox);

            Label cmdLabel = new Label();
            cmdLabel.Name = "CommandPreviewLabel";
            cmdLabel.Text = T("EffectiveLaunchCommand");
            cmdLabel.Location = new Point(28, 398);
            cmdLabel.AutoSize = true;
            p.Controls.Add(cmdLabel);
            commandPreviewBox = new TextBox();
            commandPreviewBox.Location = new Point(28, 420);
            commandPreviewBox.Size = new Size(870, 40);
            commandPreviewBox.Multiline = true;
            commandPreviewBox.ReadOnly = true;
            commandPreviewBox.BackColor = Dw2Void;
            commandPreviewBox.ForeColor = Dw2BlueGlow;
            commandPreviewBox.BorderStyle = BorderStyle.FixedSingle;
            p.Controls.Add(commandPreviewBox);

            detectButton = MakeButton(T("AutoDetect"), 28, 468, 130, 34);
            saveSettingsButton = MakeButton(T("SaveSettings"), 172, 468, 130, 34);
            gameOpenButton = MakeButton(T("GameFolder"), 316, 468, 150, 34);
            workshopRootButton = MakeButton(T("WorkshopRoot"), 480, 468, 150, 34);
            modRootButton = MakeButton(T("ModRoot"), 644, 468, 150, 34);
            detectButton.Click += delegate { DetectPaths(true); RefreshAll(); };
            saveSettingsButton.Click += delegate { SaveSettingsFromUi(); RefreshAll(); };
            gameOpenButton.Click += delegate { OpenFolder(settings.GameRoot); };
            workshopRootButton.Click += delegate { OpenFolder(settings.WorkshopRoot); };
            modRootButton.Click += delegate { OpenFolder(settings.ManagedModsRoot); };
            p.Controls.Add(detectButton);
            p.Controls.Add(saveSettingsButton);
            p.Controls.Add(gameOpenButton);
            p.Controls.Add(workshopRootButton);
            p.Controls.Add(modRootButton);

            Label profileLabel = new Label();
            profileLabel.Name = "ProfileLabel";
            profileLabel.Text = T("ModProfiles");
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
            Button saveProfile = MakeButton(T("SaveCurrent"), 292, 545, 145, 31);
            Button applyProfile = MakeButton(T("ApplyProfile"), 449, 545, 120, 31);
            Button deleteProfile = MakeButton(T("Delete"), 581, 545, 80, 31);
            Button snapshot = MakeButton(T("Snapshot"), 673, 545, 145, 31);
            Button restoreSnapshot = MakeButton(T("RestoreLatest"), 830, 545, 115, 31);
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

        private Label MakeSettingsHeader(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.Font = new Font(Font, FontStyle.Bold);
            label.ForeColor = Dw2BlueGlow;
            label.Margin = new Padding(3, 3, 3, 10);
            return label;
        }

        private void FitModListColumns(ListView list)
        {
            if (list == null || list.Columns.Count == 0 || fittingModListColumns) return;
            fittingModListColumns = true;
            try
            {
                int othersWidth = 0;
                for (int i = 1; i < list.Columns.Count; i++) othersWidth += list.Columns[i].Width;
                list.Columns[0].Width = Math.Max((int)Math.Round(120 * (DeviceDpi / 96f)), list.ClientSize.Width - othersWidth);
            }
            finally { fittingModListColumns = false; }
        }

        private void FitInitialListLayout()
        {
            ListView list = modList;
            // list -> listLayout (TableLayoutPanel) -> Panel1 (SplitterPanel) -> SplitContainer
            SplitContainer split = list == null || list.Parent == null || list.Parent.Parent == null
                ? null : list.Parent.Parent.Parent as SplitContainer;
            if (split == null || split.ClientSize.Width <= 0) return;
            int maximum = Math.Max(split.Panel1MinSize, split.ClientSize.Width - split.Panel2MinSize - split.SplitterWidth);
            // The details (right) panel gets about 30% of the window by default,
            // leaving the mod list the remaining 70%.
            int desired = Math.Min((int)Math.Round(split.ClientSize.Width * 0.70), maximum);
            if (desired >= split.Panel1MinSize && desired <= maximum) split.SplitterDistance = desired;
            FitModListColumns(list);
        }

        private void ApplyLanguage()
        {
            if (modsTab == null) return;
            modsTab.Text = T("Mods");
            settingsTab.Text = T("Settings");
            refreshButton.Text = T("Refresh");
            playButton.Text = T("PlayButton");
            if (modsNavigationButton != null) modsNavigationButton.Text = T("Mods");
            if (settingsNavigationButton != null) settingsNavigationButton.Text = T("Settings");
            RefreshNavigationButtons();
            if (modRootButton != null) modRootButton.Text = T("ModRoot");
            if (workshopRootButton != null) workshopRootButton.Text = T("WorkshopRoot");
            if (selectedFolderButton != null) selectedFolderButton.Text = T("SelectedModFolder");
            if (modSettingsButton != null) modSettingsButton.Text = T("ModSettings");
            if (workshopUpdateButton != null) workshopUpdateButton.Text = T("CheckUpdates");
            if (publishButton != null) publishButton.Text = T("PublishToWorkshop");
            Control modDocumentsButton = FindControlRecursive(this, "ModDocumentsButton");
            if (modDocumentsButton != null) modDocumentsButton.Text = T("OpenDocs");
            if (detectButton != null) detectButton.Text = T("AutoDetect");
            if (importSteamButton != null) importSteamButton.Text = T("ImportFromSteam");
            if (resetLaunchButton != null) resetLaunchButton.Text = T("ResetLaunchOptions");
            if (saveSettingsButton != null) saveSettingsButton.Text = T("SaveSettings");
            if (gameOpenButton != null) gameOpenButton.Text = T("GameFolder");
            Control settingsHeader = FindControlRecursive(this, "SettingsHeader");
            if (settingsHeader != null) settingsHeader.Text = T("PathsAndLaunchSettings");
            Control launchArgumentsLabel = FindControlRecursive(this, "LaunchArgumentsLabel");
            if (launchArgumentsLabel != null) launchArgumentsLabel.Text = T("AdditionalLaunchArguments");
            Control environmentLabel = FindControlRecursive(this, "EnvironmentLabel");
            if (environmentLabel != null) environmentLabel.Text = T("EnvironmentVariables");
            Control commandPreviewLabel = FindControlRecursive(this, "CommandPreviewLabel");
            if (commandPreviewLabel != null) commandPreviewLabel.Text = T("EffectiveLaunchCommand");
            Control profileLabel = FindControlRecursive(this, "ProfileLabel");
            if (profileLabel != null) profileLabel.Text = T("ModProfiles");
            Control saveProfileButton = FindControlRecursive(this, "SaveProfileButton");
            if (saveProfileButton != null) saveProfileButton.Text = T("SaveCurrent");
            Control applyProfileButton = FindControlRecursive(this, "ApplyProfileButton");
            if (applyProfileButton != null) applyProfileButton.Text = T("ApplyProfile");
            Control deleteProfileButton = FindControlRecursive(this, "DeleteProfileButton");
            if (deleteProfileButton != null) deleteProfileButton.Text = T("Delete");
            Control snapshotButton = FindControlRecursive(this, "SnapshotButton");
            if (snapshotButton != null) snapshotButton.Text = T("Snapshot");
            Control restoreSnapshotButton = FindControlRecursive(this, "RestoreSnapshotButton");
            if (restoreSnapshotButton != null) restoreSnapshotButton.Text = T("RestoreLatest");
            Control modListHint = FindControlRecursive(this, "ModListHint");
            if (modListHint != null) modListHint.Text = T("DragRowsToChangeLoadOrder");

            if (currentManagedMods != null)
                foreach (ModInfo mod in currentManagedMods) if (mod != null) mod.SourceName = T("GameModFolder");
            if (currentWorkshopMods != null)
                foreach (ModInfo mod in currentWorkshopMods) if (mod != null) mod.SourceName = "Steam Workshop";
            if (modList != null && modList.Columns.Count > ColumnLoadOrder)
            {
                modList.Columns[0].Text = T("ModName");
                modList.Columns[1].Text = T("Source");
                modList.Columns[ColumnModState].Text = T("ModState");
                modList.Columns[ColumnHealth].Text = T("Health");
                modList.Columns[ColumnLoadOrder].Text = T("LoadOrder");
            }

            RefreshListSourceText(modList);
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
