using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DW2ModLauncherBeta
{
    internal static class Program
    {
        internal static string CrashLogPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DW2ModLauncher_BETA.log"); }
        }

        [STAThread]
        private static void Main()
        {
            try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; } catch { }

            try
            {
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += Application_ThreadException;
                AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                LogException("Fatal startup", ex);
                ShowException("起動中にエラーが発生しました。", ex);
            }
        }

        private static void Application_ThreadException(object sender, ThreadExceptionEventArgs e)
        {
            Exception ex = e == null ? null : e.Exception;
            LogException("UI thread", ex);
            ShowException("処理中にエラーが発生しました。ランチャーは可能な限り継続します。", ex);
        }

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Exception ex = e == null ? null : e.ExceptionObject as Exception;
            LogException("Unhandled domain exception", ex);
        }

        internal static void LogException(string context, Exception ex)
        {
            try
            {
                StringBuilder b = new StringBuilder();
                b.AppendLine("============================================================");
                b.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + (context ?? "Error"));
                if (ex == null)
                {
                    b.AppendLine("Unknown exception");
                }
                else
                {
                    b.AppendLine(ex.ToString());
                }
                File.AppendAllText(CrashLogPath, b.ToString(), new UTF8Encoding(true));
            }
            catch { }
        }

        private static void ShowException(string message, Exception ex)
        {
            try
            {
                string detail = ex == null ? "" : ("\r\n\r\n" + ex.GetType().Name + ": " + ex.Message);
                MessageBox.Show(message + detail + "\r\n\r\nログ: " + CrashLogPath,
                    "DW2 Mod Launcher BETA", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch { }
        }
    }

    public class LauncherSettings
    {
        public string Language { get; set; }
        public string GameRoot { get; set; }
        public string WorkshopRoot { get; set; }
        public string ManagedModsRoot { get; set; }
        public string GlobalLaunchArguments { get; set; }
        public string LastWorkshopUpdateCheckUtc { get; set; }
        public Dictionary<string, bool> SelectedMods { get; set; }
        public string ActiveProfile { get; set; }

        public LauncherSettings()
        {
            Language = "ja";
            GameRoot = "";
            WorkshopRoot = "";
            ManagedModsRoot = "";
            GlobalLaunchArguments = "";
            LastWorkshopUpdateCheckUtc = "";
            SelectedMods = new Dictionary<string, bool>();
            ActiveProfile = "";
        }
    }

    public class ModProfile
    {
        public string Name { get; set; }
        public List<string> Order { get; set; }
        public string ManualLaunchArguments { get; set; }
        public Dictionary<string, string> IniFiles { get; set; }
        public Dictionary<string, string> Versions { get; set; }
        public ModProfile()
        {
            Order = new List<string>();
            IniFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Versions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public class ModInfo
    {
        public string Id { get; set; }
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public string Version { get; set; }
        public string PreviewImage { get; set; }
        public string Folder { get; set; }
        public string ContentRoot { get; set; }
        public bool IsWorkshop { get; set; }
        public string SourceName { get; set; }
        public string ActiveToken { get; set; }
        public int DuplicateCount { get; set; }
        public List<string> DuplicateLocations { get; set; }
        public string WorkshopDescription { get; set; }
        public string WorkshopTitle { get; set; }
        public string WorkshopPreviewUrl { get; set; }
        public string WorkshopCreator { get; set; }
        public long WorkshopFileSize { get; set; }
        public long WorkshopTimeCreated { get; set; }
        public string WorkshopTags { get; set; }
        public long LocalWorkshopTimeUpdated { get; set; }
        public long RemoteWorkshopTimeUpdated { get; set; }
        public string UpdateState { get; set; }
        public int ConflictCount { get; set; }
        public List<string> ConflictFiles { get; set; }
        public List<string> ConflictMods { get; set; }
        public List<string> ConflictPathCache { get; set; }
        public string ModJsonPath { get; set; }
        public string ModJsonLaunchArguments { get; set; }
        public List<string> IncludedTools { get; set; }
        public List<string> IncludedDocuments { get; set; }
        public List<string> RequiredMods { get; set; }
        public List<string> OptionalMods { get; set; }
        public List<string> IncompatibleMods { get; set; }
        public List<string> LoadBefore { get; set; }
        public List<string> LoadAfter { get; set; }
        public int IdenticalFileCount { get; set; }
        public int LowRiskConflictCount { get; set; }
        public int HighRiskConflictCount { get; set; }

        public string Key
        {
            get
            {
                return (IsWorkshop ? "workshop:" : "managed:") + (Id ?? Folder ?? DisplayName ?? "unknown");
            }
        }
    }

    public class ModOrderDocument
    {
        public List<string> order { get; set; }
        public ModOrderDocument() { order = new List<string>(); }
    }

    public class WorkshopRemoteDetail
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string PreviewUrl { get; set; }
        public string Creator { get; set; }
        public long TimeUpdated { get; set; }
        public long FileSize { get; set; }
        public long TimeCreated { get; set; }
        public string Tags { get; set; }
    }

    public class WorkshopUpdateCheckResult
    {
        public Dictionary<string, long> InstalledTimes { get; set; }
        public Dictionary<string, long> DetailTimes { get; set; }
        public Dictionary<string, long> RemoteTimes { get; set; }
        public string Error { get; set; }
        public Dictionary<string, WorkshopRemoteDetail> Details { get; set; }

        public WorkshopUpdateCheckResult()
        {
            InstalledTimes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            DetailTimes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            RemoteTimes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            Error = "";
            Details = new Dictionary<string, WorkshopRemoteDetail>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public class LauncherMeta
    {
        public string iniPath { get; set; }
        public string enabledKey { get; set; }
        public string languageKey { get; set; }
        public string launchArguments { get; set; }
    }

    public class IniEditorRow
    {
        public string Key { get; set; }
        public string JapaneseDescription { get; set; }
        public string EnglishDescription { get; set; }
        public ComboBox Editor { get; set; }
    }

    public class MainForm : Form
    {
        private sealed class ModListComparer : IComparer
        {
            private readonly int column;
            private readonly bool ascending;
            private readonly Func<ModInfo, bool> isEnabled;

            public ModListComparer(int column, bool ascending, Func<ModInfo, bool> isEnabled)
            {
                this.column = column;
                this.ascending = ascending;
                this.isEnabled = isEnabled;
            }

            public int Compare(object x, object y)
            {
                ListViewItem leftItem = x as ListViewItem;
                ListViewItem rightItem = y as ListViewItem;
                ModInfo left = leftItem == null ? null : leftItem.Tag as ModInfo;
                ModInfo right = rightItem == null ? null : rightItem.Tag as ModInfo;
                int result;
                if (column == 4)
                    result = CompareInt(left == null || !isEnabled(left) ? 0 : 1, right == null || !isEnabled(right) ? 0 : 1);
                else if (column == 5)
                    result = CompareInt(left == null ? 0 : left.ConflictCount, right == null ? 0 : right.ConflictCount);
                else if (column == 6)
                    result = CompareInt(left == null ? 0 : left.DuplicateCount, right == null ? 0 : right.DuplicateCount);
                else if (column == 8)
                {
                    int leftOrder;
                    int rightOrder;
                    if (!int.TryParse(leftItem == null || leftItem.SubItems.Count <= 8 ? "" : leftItem.SubItems[8].Text, out leftOrder)) leftOrder = int.MaxValue;
                    if (!int.TryParse(rightItem == null || rightItem.SubItems.Count <= 8 ? "" : rightItem.SubItems[8].Text, out rightOrder)) rightOrder = int.MaxValue;
                    result = CompareInt(leftOrder, rightOrder);
                }
                else
                {
                    string a = leftItem != null && leftItem.SubItems.Count > column ? leftItem.SubItems[column].Text : "";
                    string b = rightItem != null && rightItem.SubItems.Count > column ? rightItem.SubItems[column].Text : "";
                    result = StringComparer.CurrentCultureIgnoreCase.Compare(a, b);
                }
                if (result == 0)
                {
                    string a = left == null ? "" : left.DisplayName ?? left.Id ?? "";
                    string b = right == null ? "" : right.DisplayName ?? right.Id ?? "";
                    result = StringComparer.CurrentCultureIgnoreCase.Compare(a, b);
                }
                return ascending ? result : -result;
            }

            private static int CompareInt(int left, int right)
            {
                return left < right ? -1 : left > right ? 1 : 0;
            }
        }

        private const string AppId = "1531540";
        private static readonly Color Dw2Void = Color.FromArgb(7, 13, 21);
        private static readonly Color Dw2Deep = Color.FromArgb(11, 20, 31);
        private static readonly Color Dw2Panel = Color.FromArgb(17, 31, 46);
        private static readonly Color Dw2PanelAlt = Color.FromArgb(21, 39, 57);
        private static readonly Color Dw2Steel = Color.FromArgb(44, 68, 91);
        private static readonly Color Dw2Blue = Color.FromArgb(62, 126, 174);
        private static readonly Color Dw2BlueGlow = Color.FromArgb(102, 185, 232);
        private static readonly Color Dw2Gold = Color.FromArgb(205, 177, 105);
        private static readonly Color Dw2Text = Color.FromArgb(220, 232, 241);
        private static readonly Color Dw2Muted = Color.FromArgb(139, 160, 177);
        private static readonly Color Dw2Green = Color.FromArgb(107, 215, 151);
        private static readonly Color Dw2Red = Color.FromArgb(235, 103, 103);
        private readonly string appRoot;
        private readonly string settingsPath;
        private readonly JavaScriptSerializer json = new JavaScriptSerializer();
        private LauncherSettings settings;
        private bool populating;
        private bool updateCheckRunning;
        private bool workshopCheckWasManual;
        private List<ModInfo> currentManagedMods = new List<ModInfo>();
        private List<ModInfo> currentWorkshopMods = new List<ModInfo>();
        private List<string> currentModOrder = new List<string>();
        private bool modOrderFileFound;
        private bool modOrderReadFailed;
        private Dictionary<string, List<ModInfo>> currentCollisions = new Dictionary<string, List<ModInfo>>(StringComparer.OrdinalIgnoreCase);

        private ComboBox languageCombo;
        private Button refreshButton;
        private Button playButton;
        private Label statusLabel;
        private Label gamePathLabel;
        private Label workshopPathLabel;

        private TabControl tabs;
        private TabPage managedTab;
        private TabPage workshopTab;
        private TabPage aiTab;
        private TabPage settingsTab;

        private ListView managedList;
        private ListView workshopList;
        private readonly Dictionary<ListView, int> listSortColumns = new Dictionary<ListView, int>();
        private readonly Dictionary<ListView, bool> listSortAscending = new Dictionary<ListView, bool>();
        private ImageList managedImages;
        private ImageList workshopImages;
        private PictureBox managedPreview;
        private PictureBox workshopPreview;
        private Label managedName;
        private Label managedDesc;
        private Label workshopName;
        private Label workshopDesc;

        private CheckBox aiEnabled;
        private CheckBox aiWar;
        private CheckBox aiPeace;
        private CheckBox aiUltimatum;
        private CheckBox aiAdvisor;
        private TextBox aiBackend;
        private TextBox aiBaseUrl;
        private TextBox aiModel;
        private Label aiIniPathLabel;

        private TextBox gameRootBox;
        private TextBox workshopRootBox;
        private TextBox managedRootBox;
        private TextBox launchArgsBox;
        private ComboBox profileCombo;
        private TextBox commandPreviewBox;

        private Button managedOpenButton;
        private Button managedIniButton;
        private Button workshopOpenButton;
        private Button gameOpenButton;
        private Button saveAiButton;
        private Button reloadAiButton;
        private Button detectButton;
        private Button saveSettingsButton;
        private Button workshopUpdateButton;
        private Button workshopSteamButton;
        private Button managedDetailsButton;
        private Button workshopDetailsButton;
        private Button managedSelectedFolderButton;
        private Button workshopSelectedFolderButton;
        private Button folderSettingsButton;
        private Button managedNavigationButton;
        private Button workshopNavigationButton;
        private Button aiNavigationButton;
        private Button settingsNavigationButton;
        private ComboBox stateEditor;
        private ListView stateEditorList;
        private ListViewItem stateEditorItem;

        public MainForm()
        {
            appRoot = AppDomain.CurrentDomain.BaseDirectory;
            settingsPath = Path.Combine(appRoot, "launcher_settings.json");
            settings = LoadSettings();
            EnsureSettingsState();
            if (string.IsNullOrWhiteSpace(settings.ManagedModsRoot))
                settings.ManagedModsRoot = string.IsNullOrWhiteSpace(settings.GameRoot) ? "" : Path.Combine(settings.GameRoot, "mods");

            Text = "DW2 Mod Launcher BETA v0.4.6 CONFLICT FILTER FIX";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(1000, 650);
            Rectangle workArea = Screen.PrimaryScreen == null ? new Rectangle(0, 0, 1500, 900) : Screen.PrimaryScreen.WorkingArea;
            Size = new Size(Math.Max(1000, Math.Min(1500, workArea.Width - 40)), Math.Max(650, Math.Min(860, workArea.Height - 60)));
            BackColor = Dw2Deep;
            ForeColor = Dw2Text;
            Font = new Font("Segoe UI", 9F);

            BuildUi();
            SafeStage("DetectPaths", delegate { DetectPaths(false); });
            SafeStage("ApplyLanguage", delegate { ApplyLanguage(); });
            SafeStage("RefreshAll", delegate { RefreshAll(); });

            // Workshop online check is delayed until the window is fully shown.
            // This keeps a Steam/network problem from breaking launcher startup.
            Shown += delegate
            {
                FitInitialListLayout();
                if (!IsDisposed && IsHandleCreated)
                    BeginInvoke(new MethodInvoker(delegate
                    {
                        if (!IsDisposed) SafeStage("Workshop auto update check", delegate { BeginWorkshopUpdateCheck(false); });
                    }));
            };
            ResizeEnd += delegate { FitInitialListLayout(); };
        }

        private void EnsureSettingsState()
        {
            if (settings == null) settings = new LauncherSettings();
            if (settings.SelectedMods == null) settings.SelectedMods = new Dictionary<string, bool>();
            if (string.IsNullOrWhiteSpace(settings.Language)) settings.Language = "ja";
            if (settings.Language != "ja" && settings.Language != "en") settings.Language = "ja";
            if (settings.GameRoot == null) settings.GameRoot = "";
            if (settings.WorkshopRoot == null) settings.WorkshopRoot = "";
            if (settings.ManagedModsRoot == null) settings.ManagedModsRoot = "";
            if (settings.GlobalLaunchArguments == null) settings.GlobalLaunchArguments = "";
            if (settings.LastWorkshopUpdateCheckUtc == null) settings.LastWorkshopUpdateCheckUtc = "";
            if (settings.ActiveProfile == null) settings.ActiveProfile = "";
        }

        private void SafeStage(string name, MethodInvoker action)
        {
            if (action == null) return;
            try
            {
                EnsureSettingsState();
                action();
            }
            catch (Exception ex)
            {
                Program.LogException(name, ex);
                SetStatus(T("一部処理をスキップしました: ", "Skipped a failed step: ") + name + " - " + ex.Message);
            }
        }

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

        private LauncherSettings LoadSettings()
        {
            try
            {
                if (File.Exists(settingsPath))
                {
                    LauncherSettings s = json.Deserialize<LauncherSettings>(File.ReadAllText(settingsPath, Encoding.UTF8));
                    if (s != null)
                    {
                        if (s.SelectedMods == null) s.SelectedMods = new Dictionary<string, bool>();
                        return s;
                    }
                }
            }
            catch { }
            return new LauncherSettings();
        }

        private void SaveSettings()
        {
            try
            {
                File.WriteAllText(settingsPath, json.Serialize(settings), new UTF8Encoding(true));
            }
            catch (Exception ex)
            {
                SetStatus(T("設定保存エラー: ", "Settings save error: ") + ex.Message);
            }
        }

        private void SaveSettingsFromUi()
        {
            EnsureSettingsState();
            string game = gameRootBox == null ? settings.GameRoot : (gameRootBox.Text ?? "").Trim();
            string workshop = workshopRootBox == null ? settings.WorkshopRoot : (workshopRootBox.Text ?? "").Trim();
            string managed = managedRootBox == null ? settings.ManagedModsRoot : (managedRootBox.Text ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(game) && !IsGameRoot(game))
            {
                MessageBox.Show(T("DistantWorlds2.exeがあるゲームフォルダーを指定してください。", "Select the game folder containing DistantWorlds2.exe."), Text);
                return;
            }
            if (!string.IsNullOrWhiteSpace(workshop) && !Directory.Exists(workshop))
            {
                MessageBox.Show(T("Workshopフォルダーが見つかりません。", "The Workshop folder does not exist."), Text);
                return;
            }
            if (!string.IsNullOrWhiteSpace(managed) && !Directory.Exists(managed))
            {
                MessageBox.Show(T("DW2 MODフォルダーが見つかりません。", "The DW2 MOD folder does not exist."), Text);
                return;
            }
            settings.GameRoot = game;
            settings.WorkshopRoot = workshop;
            settings.ManagedModsRoot = managed;
            if (launchArgsBox != null) settings.GlobalLaunchArguments = (launchArgsBox.Text ?? "").Trim();
            SaveSettings();
            UpdatePathLabels();
            UpdateCommandPreview();
        }

        private string ProfilesRoot() { return Path.Combine(appRoot, "Profiles"); }
        private string SafeFileName(string value)
        {
            string result = value ?? "Profile";
            foreach (char c in Path.GetInvalidFileNameChars()) result = result.Replace(c, '_');
            return string.IsNullOrWhiteSpace(result) ? "Profile" : result.Trim();
        }

        private void RefreshProfileCombo()
        {
            if (profileCombo == null) return;
            string selected = settings == null ? "" : settings.ActiveProfile ?? "";
            profileCombo.Items.Clear();
            try
            {
                Directory.CreateDirectory(ProfilesRoot());
                foreach (string path in Directory.GetFiles(ProfilesRoot(), "*.json", SearchOption.TopDirectoryOnly))
                    profileCombo.Items.Add(Path.GetFileNameWithoutExtension(path));
            }
            catch { }
            if (!string.IsNullOrWhiteSpace(selected)) profileCombo.Text = selected;
        }

        private void SaveCurrentProfile()
        {
            string name = profileCombo == null ? "" : (profileCombo.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show(T("プロファイル名を入力してください。", "Enter a profile name."), Text);
                return;
            }
            ModProfile profile = new ModProfile();
            profile.Name = name;
            profile.Order = new List<string>(currentModOrder ?? new List<string>());
            profile.ManualLaunchArguments = launchArgsBox == null ? settings.GlobalLaunchArguments : launchArgsBox.Text.Trim();
            foreach (ModInfo mod in (currentManagedMods ?? new List<ModInfo>()).Concat(currentWorkshopMods ?? new List<ModInfo>()))
            {
                profile.Versions[mod.ActiveToken ?? mod.Key] = mod.Version ?? "";
                try
                {
                    foreach (string ini in Directory.GetFiles(mod.Folder, "*.ini", SearchOption.AllDirectories))
                    {
                        string rel = ini.Substring(mod.Folder.TrimEnd(Path.DirectorySeparatorChar).Length).TrimStart(Path.DirectorySeparatorChar);
                        profile.IniFiles[(mod.ActiveToken ?? mod.Key) + "|" + rel] = Convert.ToBase64String(File.ReadAllBytes(ini));
                    }
                }
                catch { }
            }
            try
            {
                Directory.CreateDirectory(ProfilesRoot());
                File.WriteAllText(Path.Combine(ProfilesRoot(), SafeFileName(name) + ".json"), json.Serialize(profile), new UTF8Encoding(false));
                settings.ActiveProfile = name;
                settings.GlobalLaunchArguments = profile.ManualLaunchArguments ?? "";
                SaveSettings();
                RefreshProfileCombo();
                SetStatus(T("MODプロファイルを保存しました: ", "MOD profile saved: ") + name);
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, Text); }
        }

        private void ApplySelectedProfile()
        {
            string name = profileCombo == null ? "" : (profileCombo.Text ?? "").Trim();
            string path = Path.Combine(ProfilesRoot(), SafeFileName(name) + ".json");
            if (!File.Exists(path)) { MessageBox.Show(T("プロファイルが見つかりません。", "Profile not found."), Text); return; }
            if (IsGameRunning()) { MessageBox.Show(T("DW2を終了してから切り替えてください。", "Close DW2 before switching profiles."), Text); return; }
            try
            {
                ModProfile profile = json.Deserialize<ModProfile>(File.ReadAllText(path, Encoding.UTF8));
                if (profile == null) return;
                List<ModInfo> all = (currentManagedMods ?? new List<ModInfo>()).Concat(currentWorkshopMods ?? new List<ModInfo>()).ToList();
                List<string> versionChanges = new List<string>();
                foreach (KeyValuePair<string, string> savedVersion in profile.Versions ?? new Dictionary<string, string>())
                {
                    ModInfo installed = all.FirstOrDefault(m => string.Equals(m.ActiveToken, savedVersion.Key, StringComparison.OrdinalIgnoreCase) || string.Equals(m.Key, savedVersion.Key, StringComparison.OrdinalIgnoreCase));
                    if (installed == null) versionChanges.Add(T("未導入: ", "Not installed: ") + savedVersion.Key);
                    else if (!string.Equals(installed.Version ?? "", savedVersion.Value ?? "", StringComparison.OrdinalIgnoreCase))
                        versionChanges.Add((installed.DisplayName ?? installed.Id) + ": " + savedVersion.Value + " → " + (installed.Version ?? "?"));
                }
                if (versionChanges.Count > 0 && MessageBox.Show(
                    T("保存時からMODバージョンが変わっています。\r\n", "MOD versions differ from the saved profile.\r\n") + string.Join("\r\n", versionChanges.Take(20).ToArray()) +
                    T("\r\n\r\n構成を適用しますか？", "\r\n\r\nApply the profile?"), T("バージョン差異", "Version differences"),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                WriteModOrder(profile.Order ?? new List<string>());
                settings.ActiveProfile = profile.Name ?? name;
                settings.GlobalLaunchArguments = profile.ManualLaunchArguments ?? "";
                if (launchArgsBox != null) launchArgsBox.Text = settings.GlobalLaunchArguments;
                foreach (KeyValuePair<string, string> kv in profile.IniFiles ?? new Dictionary<string, string>())
                {
                    int split = kv.Key.IndexOf('|');
                    if (split <= 0) continue;
                    string token = kv.Key.Substring(0, split);
                    string rel = kv.Key.Substring(split + 1);
                    ModInfo mod = all.FirstOrDefault(m => string.Equals(m.ActiveToken, token, StringComparison.OrdinalIgnoreCase) || string.Equals(m.Key, token, StringComparison.OrdinalIgnoreCase));
                    if (mod == null) continue;
                    string ini = Path.Combine(mod.Folder, rel);
                    if (File.Exists(ini)) File.Copy(ini, ini + ".launcher_backup", true);
                    Directory.CreateDirectory(Path.GetDirectoryName(ini));
                    File.WriteAllBytes(ini, Convert.FromBase64String(kv.Value));
                }
                SaveSettings();
                RefreshAll();
                SetStatus(T("MODプロファイルへ切り替えました: ", "Switched MOD profile: ") + settings.ActiveProfile);
            }
            catch (Exception ex) { Program.LogException("Apply profile", ex); MessageBox.Show(ex.Message, Text); }
        }

        private void DeleteSelectedProfile()
        {
            string name = profileCombo == null ? "" : (profileCombo.Text ?? "").Trim();
            string path = Path.Combine(ProfilesRoot(), SafeFileName(name) + ".json");
            try { if (File.Exists(path)) File.Delete(path); if (settings.ActiveProfile == name) settings.ActiveProfile = ""; SaveSettings(); RefreshProfileCombo(); }
            catch (Exception ex) { MessageBox.Show(ex.Message, Text); }
        }

        private void CreateEnvironmentSnapshot()
        {
            string root = Path.Combine(appRoot, "Snapshots", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            try
            {
                Directory.CreateDirectory(root);
                string modsJson = ModsJsonPath();
                if (!string.IsNullOrWhiteSpace(modsJson) && File.Exists(modsJson)) File.Copy(modsJson, Path.Combine(root, "mods.json"), true);
                if (File.Exists(settingsPath)) File.Copy(settingsPath, Path.Combine(root, "launcher_settings.json"), true);
                List<ModInfo> enabled = (currentManagedMods ?? new List<ModInfo>()).Concat(currentWorkshopMods ?? new List<ModInfo>()).Where(IsModSelected).ToList();
                Dictionary<string, string> manifest = new Dictionary<string, string>();
                foreach (ModInfo mod in enabled)
                {
                    string destination = Path.Combine(root, "MODs", SafeFileName(mod.ActiveToken));
                    CopyDirectory(mod.Folder, destination);
                    manifest[mod.ActiveToken] = mod.Folder;
                }
                File.WriteAllText(Path.Combine(root, "snapshot_manifest.json"), json.Serialize(manifest), new UTF8Encoding(false));
                MessageBox.Show(T("スナップショットを保存しました。\r\n", "Snapshot saved.\r\n") + root, Text);
            }
            catch (Exception ex) { Program.LogException("Create snapshot", ex); MessageBox.Show(ex.Message, Text); }
        }

        private void RestoreLatestSnapshot()
        {
            string snapshots = Path.Combine(appRoot, "Snapshots");
            string root = Directory.Exists(snapshots) ? Directory.GetDirectories(snapshots).OrderByDescending(x => x).FirstOrDefault() : null;
            if (string.IsNullOrWhiteSpace(root)) { MessageBox.Show(T("スナップショットがありません。", "No snapshot is available."), Text); return; }
            if (IsGameRunning()) { MessageBox.Show(T("DW2を終了してから復元してください。", "Close DW2 before restoring."), Text); return; }
            if (MessageBox.Show(T("最新のスナップショットへ戻しますか？\r\n", "Restore the latest snapshot?\r\n") + root,
                T("スナップショット復元", "Restore snapshot"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            try
            {
                string manifestPath = Path.Combine(root, "snapshot_manifest.json");
                Dictionary<string, string> manifest = json.Deserialize<Dictionary<string, string>>(File.ReadAllText(manifestPath, Encoding.UTF8));
                foreach (KeyValuePair<string, string> kv in manifest)
                {
                    string source = Path.Combine(root, "MODs", SafeFileName(kv.Key));
                    if (Directory.Exists(source) && Directory.Exists(Path.GetDirectoryName(kv.Value))) CopyDirectory(source, kv.Value);
                }
                string savedOrder = Path.Combine(root, "mods.json");
                if (File.Exists(savedOrder) && !string.IsNullOrWhiteSpace(ModsJsonPath())) File.Copy(savedOrder, ModsJsonPath(), true);
                string savedSettings = Path.Combine(root, "launcher_settings.json");
                if (File.Exists(savedSettings)) File.Copy(savedSettings, settingsPath, true);
                settings = LoadSettings();
                EnsureSettingsState();
                RefreshAll();
                MessageBox.Show(T("最新のスナップショットを復元しました。", "Latest snapshot restored."), Text);
            }
            catch (Exception ex) { Program.LogException("Restore snapshot", ex); MessageBox.Show(ex.Message, Text); }
        }

        private void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (string file in Directory.GetFiles(source, "*", SearchOption.TopDirectoryOnly)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
            foreach (string dir in Directory.GetDirectories(source, "*", SearchOption.TopDirectoryOnly)) CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
        }

        private string ModsJsonPath()
        {
            string root = settings.ManagedModsRoot;
            if (string.IsNullOrWhiteSpace(root) && !string.IsNullOrWhiteSpace(settings.GameRoot)) root = Path.Combine(settings.GameRoot, "mods");
            return string.IsNullOrWhiteSpace(root) ? null : Path.Combine(root, "mods.json");
        }

        private void LoadModOrder()
        {
            currentModOrder = new List<string>();
            modOrderFileFound = false;
            modOrderReadFailed = false;
            string path = ModsJsonPath();
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
                ModOrderDocument document = json.Deserialize<ModOrderDocument>(File.ReadAllText(path, Encoding.UTF8));
                if (document == null || document.order == null) { modOrderReadFailed = true; return; }
                currentModOrder = document.order.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                modOrderFileFound = true;
            }
            catch (Exception ex) { modOrderReadFailed = true; Program.LogException("Read DW2 mods.json", ex); }
        }

        private bool IsGameRunning()
        {
            try { return Process.GetProcessesByName("DistantWorlds2").Length > 0; }
            catch { return false; }
        }

        private void SaveModOrderSelection(ModInfo mod, bool enabled)
        {
            if (populating || mod == null || string.IsNullOrWhiteSpace(mod.ActiveToken)) return;
            if (modOrderReadFailed)
            {
                MessageBox.Show(T("mods.jsonが壊れているか読み取れないため、上書きを中止しました。", "mods.json is invalid or unreadable. The launcher will not overwrite it."), Text);
                RefreshAll();
                return;
            }
            if (IsGameRunning())
            {
                MessageBox.Show(T("DW2起動中はMOD設定を変更できません。ゲーム終了後に変更してください。", "MOD settings cannot be changed while DW2 is running."), Text);
                RefreshAll();
                return;
            }
            string path = ModsJsonPath();
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(Path.GetDirectoryName(path))) return;
            List<string> next = new List<string>(currentModOrder ?? new List<string>());
            next.RemoveAll(x => x.Equals(mod.ActiveToken, StringComparison.OrdinalIgnoreCase));
            if (enabled) next.Add(mod.ActiveToken);
            ModOrderDocument document = new ModOrderDocument();
            document.order = next;
            string temp = path + ".launcher_tmp";
            string backup = path + ".launcher_backup";
            try
            {
                string output = json.Serialize(document);
                json.Deserialize<ModOrderDocument>(output);
                File.WriteAllText(temp, output, new UTF8Encoding(false));
                if (File.Exists(path))
                {
                    File.Copy(path, backup, true);
                    try { File.Replace(temp, path, null, true); }
                    catch { File.Copy(temp, path, true); File.Delete(temp); }
                }
                else File.Move(temp, path);
                currentModOrder = next;
                modOrderFileFound = true;
                SetStatus(T("DW2のMOD設定を保存しました。", "DW2 MOD settings saved."));
            }
            catch (Exception ex)
            {
                Program.LogException("Write DW2 mods.json", ex);
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                MessageBox.Show(T("mods.jsonの保存に失敗しました。", "Failed to save mods.json.") + "\r\n" + ex.Message, Text);
                LoadModOrder();
            }
        }

        private void SaveLoadOrderFromList(ListView list)
        {
            if (list == null || IsGameRunning()) return;
            List<string> ordered = list.Items.Cast<ListViewItem>()
                .Select(i => i.Tag as ModInfo).Where(m => m != null && IsModSelected(m))
                .Select(m => m.ActiveToken).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            HashSet<string> category = new HashSet<string>(ordered, StringComparer.OrdinalIgnoreCase);
            List<string> next = new List<string>();
            int replacement = 0;
            foreach (string token in currentModOrder ?? new List<string>())
            {
                if (category.Contains(token))
                {
                    if (replacement < ordered.Count) next.Add(ordered[replacement++]);
                }
                else next.Add(token);
            }
            while (replacement < ordered.Count) next.Add(ordered[replacement++]);
            WriteModOrder(next);
        }

        private bool WriteModOrder(List<string> order)
        {
            string path = ModsJsonPath();
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(Path.GetDirectoryName(path))) return false;
            string temp = path + ".launcher_tmp";
            try
            {
                ModOrderDocument document = new ModOrderDocument();
                document.order = (order ?? new List<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                File.WriteAllText(temp, json.Serialize(document), new UTF8Encoding(false));
                if (File.Exists(path))
                {
                    File.Copy(path, path + ".launcher_backup", true);
                    try { File.Replace(temp, path, null, true); }
                    catch { File.Copy(temp, path, true); File.Delete(temp); }
                }
                else File.Move(temp, path);
                currentModOrder = document.order;
                modOrderFileFound = true;
                SetStatus(T("ロード順を保存しました。", "Load order saved."));
                UpdateCommandPreview();
                return true;
            }
            catch (Exception ex)
            {
                Program.LogException("Write load order", ex);
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                MessageBox.Show(T("ロード順を保存できませんでした。", "Could not save load order.") + "\r\n" + ex.Message, Text);
                return false;
            }
        }

        private void DetectPaths(bool overwrite)
        {
            string game = FindGameRoot();
            if (!string.IsNullOrEmpty(game) && (overwrite || string.IsNullOrEmpty(settings.GameRoot) || !Directory.Exists(settings.GameRoot)))
                settings.GameRoot = game;

            string workshop = FindWorkshopRoot(settings.GameRoot);
            if (!string.IsNullOrEmpty(workshop) && (overwrite || string.IsNullOrEmpty(settings.WorkshopRoot) || !Directory.Exists(settings.WorkshopRoot)))
                settings.WorkshopRoot = workshop;

            if (IsGameRoot(settings.GameRoot))
            {
                string dw2Mods = Path.Combine(settings.GameRoot, "mods");
                if (overwrite || string.IsNullOrEmpty(settings.ManagedModsRoot) || !Directory.Exists(settings.ManagedModsRoot))
                    settings.ManagedModsRoot = dw2Mods;
            }

            SaveSettings();
            UpdatePathLabels();
            if (gameRootBox != null) gameRootBox.Text = settings.GameRoot;
            if (workshopRootBox != null) workshopRootBox.Text = settings.WorkshopRoot;
            if (managedRootBox != null) managedRootBox.Text = settings.ManagedModsRoot;
            if (launchArgsBox != null) launchArgsBox.Text = settings.GlobalLaunchArguments ?? "";
        }

        private string FindGameRoot()
        {
            if (IsGameRoot(settings.GameRoot)) return settings.GameRoot;

            List<string> libraries = GetSteamLibraries();
            foreach (string lib in libraries)
            {
                string p = Path.Combine(lib, "steamapps", "common", "Distant Worlds 2");
                if (IsGameRoot(p)) return p;
            }

            for (char d = 'C'; d <= 'Z'; d++)
            {
                string[] bases = new string[] { d + @":\Steam", d + @":\steam", d + @":\SteamLibrary" };
                foreach (string b in bases)
                {
                    string p = Path.Combine(b, "steamapps", "common", "Distant Worlds 2");
                    if (IsGameRoot(p)) return p;
                }
            }
            return "";
        }

        private bool IsGameRoot(string p)
        {
            try { return !string.IsNullOrEmpty(p) && File.Exists(Path.Combine(p, "DistantWorlds2.exe")); }
            catch { return false; }
        }

        private string FindWorkshopRoot(string gameRoot)
        {
            if (Directory.Exists(settings.WorkshopRoot)) return settings.WorkshopRoot;

            if (IsGameRoot(gameRoot))
            {
                try
                {
                    DirectoryInfo common = Directory.GetParent(gameRoot);
                    DirectoryInfo steamapps = common == null ? null : common.Parent;
                    if (steamapps != null)
                    {
                        string p = Path.Combine(steamapps.FullName, "workshop", "content", AppId);
                        if (Directory.Exists(p)) return p;
                    }
                }
                catch { }
            }

            foreach (string lib in GetSteamLibraries())
            {
                string p = Path.Combine(lib, "steamapps", "workshop", "content", AppId);
                if (Directory.Exists(p)) return p;
            }

            for (char d = 'C'; d <= 'Z'; d++)
            {
                string[] bases = new string[] { d + @":\Steam", d + @":\steam", d + @":\SteamLibrary" };
                foreach (string b in bases)
                {
                    string p = Path.Combine(b, "steamapps", "workshop", "content", AppId);
                    if (Directory.Exists(p)) return p;
                }
            }
            return "";
        }

        private List<string> GetSteamLibraries()
        {
            List<string> libs = new List<string>();
            string steam = ReadSteamPathFromRegistry();
            if (!string.IsNullOrEmpty(steam)) libs.Add(steam);

            List<string> initial = new List<string>(libs);
            foreach (string root in initial)
            {
                string vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
                try
                {
                    if (!File.Exists(vdf)) continue;
                    string text = File.ReadAllText(vdf);
                    MatchCollection matches = Regex.Matches(text, "\\\"path\\\"\\s+\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase);
                    foreach (Match m in matches)
                    {
                        string p = m.Groups[1].Value.Replace("\\\\", "\\");
                        if (Directory.Exists(p) && !libs.Contains(p, StringComparer.OrdinalIgnoreCase)) libs.Add(p);
                    }
                }
                catch { }
            }
            return libs;
        }

        private string ReadSteamPathFromRegistry()
        {
            string[] keys = new string[]
            {
                @"HKEY_CURRENT_USER\Software\Valve\Steam",
                @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam",
                @"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam"
            };
            foreach (string key in keys)
            {
                try
                {
                    object v = Registry.GetValue(key, "SteamPath", null);
                    if (v == null) v = Registry.GetValue(key, "InstallPath", null);
                    if (v != null && Directory.Exists(v.ToString())) return v.ToString();
                }
                catch { }
            }
            return "";
        }

        private void RefreshAll()
        {
            EnsureSettingsState();
            LoadModOrder();
            Dictionary<string, ModInfo> workshopState = (currentWorkshopMods ?? new List<ModInfo>())
                .Where(m => m != null && !string.IsNullOrWhiteSpace(m.Id))
                .GroupBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            SafeStage("Refresh path labels", delegate
            {
                UpdatePathLabels();
                if (gameRootBox != null) gameRootBox.Text = settings.GameRoot ?? "";
                if (workshopRootBox != null) workshopRootBox.Text = settings.WorkshopRoot ?? "";
                if (managedRootBox != null) managedRootBox.Text = settings.ManagedModsRoot ?? "";
                if (launchArgsBox != null) launchArgsBox.Text = settings.GlobalLaunchArguments ?? "";
            });

            SafeStage("Scan Metapo mods", delegate { currentManagedMods = ScanMods(settings.ManagedModsRoot, false) ?? new List<ModInfo>(); });
            SafeStage("Scan Workshop mods", delegate { currentWorkshopMods = ScanMods(settings.WorkshopRoot, true) ?? new List<ModInfo>(); });
            if (currentManagedMods == null) currentManagedMods = new List<ModInfo>();
            if (currentWorkshopMods == null) currentWorkshopMods = new List<ModInfo>();
            RestoreWorkshopRuntimeState(currentWorkshopMods, workshopState);
            currentManagedMods = OrderModsForDisplay(currentManagedMods);
            currentWorkshopMods = OrderModsForDisplay(currentWorkshopMods);
            RefreshAiCommanderAvailability();
            if (currentCollisions == null) currentCollisions = new Dictionary<string, List<ModInfo>>(StringComparer.OrdinalIgnoreCase);

            SafeStage("Populate Metapo list", delegate { PopulateList(managedList, managedImages, currentManagedMods, true); });
            SafeStage("Populate Workshop list", delegate { PopulateList(workshopList, workshopImages, currentWorkshopMods, false); });
            SafeStage("Conflict analysis", delegate { AnalyzeConflicts(); });
            SafeStage("Duplicate analysis", delegate { AnalyzeDuplicates(); });
            SafeStage("Refresh status columns", delegate { RefreshModStatusColumns(); });
            SafeStage("Load AI Commander settings", delegate { LoadAiSettings(); });
            SafeStage("Build launch command", delegate { UpdateCommandPreview(); });
            SafeStage("Overall status", delegate { UpdateOverallStatus(); });
        }

        private void RestoreWorkshopRuntimeState(List<ModInfo> scanned, Dictionary<string, ModInfo> previous)
        {
            if (scanned == null || previous == null || previous.Count == 0) return;
            foreach (ModInfo mod in scanned)
            {
                ModInfo old;
                if (mod == null || string.IsNullOrWhiteSpace(mod.Id) || !previous.TryGetValue(mod.Id, out old) || old == null) continue;
                mod.UpdateState = old.UpdateState;
                mod.LocalWorkshopTimeUpdated = old.LocalWorkshopTimeUpdated;
                mod.RemoteWorkshopTimeUpdated = old.RemoteWorkshopTimeUpdated;
                mod.WorkshopDescription = old.WorkshopDescription;
                mod.WorkshopTitle = old.WorkshopTitle;
                mod.WorkshopPreviewUrl = old.WorkshopPreviewUrl;
                mod.WorkshopCreator = old.WorkshopCreator;
                mod.WorkshopFileSize = old.WorkshopFileSize;
                mod.WorkshopTimeCreated = old.WorkshopTimeCreated;
                mod.WorkshopTags = old.WorkshopTags;
                if (!string.IsNullOrWhiteSpace(old.WorkshopTitle)) mod.DisplayName = old.DisplayName;
                if (!string.IsNullOrWhiteSpace(old.WorkshopDescription)) mod.Description = old.Description;
            }
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

        private List<ModInfo> OrderModsForDisplay(List<ModInfo> mods)
        {
            return (mods ?? new List<ModInfo>()).OrderBy(m =>
            {
                int index = currentModOrder == null ? -1 : currentModOrder.FindIndex(x => string.Equals(x, m.ActiveToken, StringComparison.OrdinalIgnoreCase));
                return index < 0 ? int.MaxValue : index;
            }).ThenBy(m => m.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        private void RefreshAiCommanderAvailability()
        {
            if (tabs == null || aiTab == null || aiNavigationButton == null) return;
            bool found = (currentManagedMods ?? new List<ModInfo>()).Concat(currentWorkshopMods ?? new List<ModInfo>()).Any(IsAiCommanderMod);
            aiNavigationButton.Visible = found;
            if (settingsNavigationButton != null) settingsNavigationButton.Left = found ? 517 : 359;
            bool tabExists = tabs.TabPages.Contains(aiTab);
            if (!found && tabExists)
            {
                if (tabs.SelectedTab == aiTab) tabs.SelectedTab = managedTab;
                tabs.TabPages.Remove(aiTab);
            }
            else if (found && !tabExists)
            {
                int settingsIndex = tabs.TabPages.IndexOf(settingsTab);
                tabs.TabPages.Insert(settingsIndex < 0 ? tabs.TabPages.Count : settingsIndex, aiTab);
            }
            RefreshNavigationButtons();
        }

        private bool IsAiCommanderMod(ModInfo mod)
        {
            if (mod == null) return false;
            string identity = ((mod.DisplayName ?? "") + " " + (mod.Id ?? "") + " " + Path.GetFileName(mod.Folder ?? "")).Replace("_", " ");
            if (identity.IndexOf("AI Commander", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            try
            {
                return !string.IsNullOrWhiteSpace(mod.Folder) && Directory.GetFiles(mod.Folder, "DW2AICommander.dll", SearchOption.AllDirectories).Length > 0;
            }
            catch { return false; }
        }

        private List<ModInfo> ScanMods(string root, bool workshop)
        {
            List<ModInfo> result = new List<ModInfo>();
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return result;
            string[] dirs;
            try { dirs = Directory.GetDirectories(root); }
            catch { return result; }

            foreach (string dir in dirs)
            {
                try
                {
                    string modJson = FindModJson(dir, workshop);
                    if (string.IsNullOrWhiteSpace(modJson)) continue;
                    ModInfo mod = ReadModInfo(dir, modJson, workshop);
                    result.Add(mod);
                }
                catch (Exception ex) { Program.LogException("Read MOD: " + dir, ex); }
            }
            return result.OrderBy(m => m.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        private string FindModJson(string dir, bool workshop)
        {
            string direct = FindDirectModJson(dir);
            if (!string.IsNullOrWhiteSpace(direct) || !workshop) return direct;
            try
            {
                foreach (string child in Directory.GetDirectories(dir))
                {
                    string nested = FindDirectModJson(child);
                    if (!string.IsNullOrWhiteSpace(nested)) return nested;
                    foreach (string grandchild in Directory.GetDirectories(child))
                    {
                        nested = FindDirectModJson(grandchild);
                        if (!string.IsNullOrWhiteSpace(nested)) return nested;
                    }
                }
            }
            catch { }
            return null;
        }

        private string FindDirectModJson(string dir)
        {
            try
            {
                foreach (string f in Directory.GetFiles(dir, "*", SearchOption.TopDirectoryOnly))
                    if (Path.GetFileName(f).Equals("mod.json", StringComparison.OrdinalIgnoreCase)) return f;
            }
            catch { }
            return null;
        }

        private ModInfo ReadModInfo(string dir, string modJson, bool workshop)
        {
            ModInfo m = new ModInfo();
            m.Id = Path.GetFileName(dir);
            m.DisplayName = Path.GetFileName(dir);
            m.Description = "";
            m.Version = "";
            m.Folder = dir;
            m.IsWorkshop = workshop;
            m.SourceName = workshop ? "Steam Workshop" : T("本体MODフォルダー", "Game MOD Folder");
            m.ActiveToken = workshop ? "steam/" + m.Id : "mods/" + Path.GetFileName(dir);
            m.ContentRoot = dir;
            m.UpdateState = workshop ? "unknown" : "na";
            m.ConflictFiles = new List<string>();
            m.ConflictMods = new List<string>();
            m.DuplicateLocations = new List<string>();
            m.RequiredMods = new List<string>();
            m.OptionalMods = new List<string>();
            m.IncompatibleMods = new List<string>();
            m.LoadBefore = new List<string>();
            m.LoadAfter = new List<string>();
            m.IncludedTools = new List<string>();
            m.IncludedDocuments = new List<string>();
            m.ModJsonPath = modJson;

            string preview = null;
            if (!string.IsNullOrEmpty(modJson) && File.Exists(modJson))
            {
                string text = File.ReadAllText(modJson, Encoding.UTF8);
                try
                {
                    object rootObj = json.DeserializeObject(text);
                    Dictionary<string, object> d = rootObj as Dictionary<string, object>;
                    if (d != null)
                    {
                        m.DisplayName = GetString(d, new string[] { "displayName", "name", "title" }, m.DisplayName);
                        m.Description = GetString(d, new string[] { "description", "summary" }, "");
                        m.Version = GetString(d, new string[] { "version", "modVersion" }, "");
                        preview = GetString(d, new string[] { "previewImage", "preview", "thumbnail", "icon" }, "");
                        string wid = GetString(d, new string[] { "workshopId", "workshopID" }, "");
                        if (workshop && !Regex.IsMatch(m.Id ?? "", "^\\d+$") && !string.IsNullOrWhiteSpace(wid)) m.Id = wid;
                        Dictionary<string, object> launcher = GetDictionary(d, "launcher");
                        if (launcher != null)
                            m.ModJsonLaunchArguments = GetString(launcher, new string[] { "launchArguments" }, "");
                        m.RequiredMods = GetStringList(d, new string[] { "Required", "required", "requires" });
                        m.OptionalMods = GetStringList(d, new string[] { "Optional", "optional" });
                        m.IncompatibleMods = GetStringList(d, new string[] { "Incompatible", "incompatible", "conflicts" });
                        m.LoadBefore = GetStringList(d, new string[] { "LoadBefore", "loadBefore" });
                        m.LoadAfter = GetStringList(d, new string[] { "LoadAfter", "loadAfter" });
                    }
                }
                catch
                {
                    m.DisplayName = ReadJsonStringLoose(text, "displayName", m.DisplayName);
                    m.Description = ReadJsonStringLoose(text, "description", "");
                    m.Version = ReadJsonStringLoose(text, "version", "");
                    preview = ReadJsonStringLoose(text, "previewImage", "");
                }
                if (!string.IsNullOrWhiteSpace(preview))
                {
                    string baseDir = Path.GetDirectoryName(modJson);
                    string p = Path.Combine(baseDir, preview.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(p)) m.PreviewImage = p;
                }
            }
            if (!string.IsNullOrEmpty(modJson) && File.Exists(modJson))
            {
                m.ContentRoot = Path.GetDirectoryName(modJson);
                if (workshop) m.Folder = m.ContentRoot;
            }
            if (workshop) m.ActiveToken = "steam/" + m.Id;
            if (string.IsNullOrEmpty(m.PreviewImage)) m.PreviewImage = FindFallbackImage(m.ContentRoot);
            m.IncludedTools = FindIncludedTools(m.ContentRoot);
            m.IncludedDocuments = FindIncludedDocuments(m.ContentRoot);
            return m;
        }

        private List<string> FindIncludedDocuments(string root)
        {
            List<string> result = new List<string>();
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return result;
            try
            {
                foreach (string file in Directory.GetFiles(root, "*.*", SearchOption.AllDirectories))
                {
                    string extension = Path.GetExtension(file).ToLowerInvariant();
                    if (extension != ".txt" && extension != ".md" && extension != ".pdf" && extension != ".html" && extension != ".htm" && extension != ".doc" && extension != ".docx") continue;
                    string name = Path.GetFileNameWithoutExtension(file);
                    if (name.IndexOf("README", StringComparison.OrdinalIgnoreCase) < 0 &&
                        name.IndexOf("MANUAL", StringComparison.OrdinalIgnoreCase) < 0 &&
                        name.IndexOf("GUIDE", StringComparison.OrdinalIgnoreCase) < 0 &&
                        name.IndexOf("INSTRUCTION", StringComparison.OrdinalIgnoreCase) < 0 &&
                        name.IndexOf("説明", StringComparison.OrdinalIgnoreCase) < 0 &&
                        name.IndexOf("マニュアル", StringComparison.OrdinalIgnoreCase) < 0 &&
                        name.IndexOf("導入", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    string relative = file.Substring(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length)
                        .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    result.Add(relative);
                }
            }
            catch { }
            return result.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        private string IncludedDocumentsSummary(ModInfo mod)
        {
            int count = mod == null || mod.IncludedDocuments == null ? 0 : mod.IncludedDocuments.Count;
            if (count == 0) return "—";
            return count == 1 ? T("● 文書あり", "● Document found") : T("● 付属文書: " + count + "件", "● Documents: " + count);
        }

        private List<string> FindIncludedTools(string root)
        {
            List<string> result = new List<string>();
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return result;
            try
            {
                foreach (string file in Directory.GetFiles(root, "*.*", SearchOption.AllDirectories))
                {
                    string extension = Path.GetExtension(file);
                    if (!extension.Equals(".bat", StringComparison.OrdinalIgnoreCase) &&
                        !extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                    string relative = file.Substring(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length)
                        .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    result.Add(relative);
                }
            }
            catch { }
            return result.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        private string IncludedToolsSummary(ModInfo mod)
        {
            List<string> tools = mod == null || mod.IncludedTools == null ? new List<string>() : mod.IncludedTools;
            if (tools.Count == 0) return "—";
            if (tools.Count > 1) return T("● 付属ツール: " + tools.Count + "件", "● Included tools: " + tools.Count);
            bool installer = tools.Any(x => Path.GetFileName(x).Equals("INSTALL.bat", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(x).StartsWith("INSTALL_", StringComparison.OrdinalIgnoreCase));
            bool updater = tools.Any(x => Path.GetFileName(x).IndexOf("UPDATE", StringComparison.OrdinalIgnoreCase) >= 0);
            bool config = tools.Any(x => Path.GetFileName(x).IndexOf("CONFIG", StringComparison.OrdinalIgnoreCase) >= 0 || Path.GetFileName(x).IndexOf("SETTING", StringComparison.OrdinalIgnoreCase) >= 0);
            if (installer) return T("● インストーラーあり", "● Installer found");
            if (updater) return T("● 更新ツールあり", "● Update tool found");
            if (config) return T("● 設定ツールあり", "● Config tool found");
            bool bat = tools.Any(x => Path.GetExtension(x).Equals(".bat", StringComparison.OrdinalIgnoreCase));
            bool exe = tools.Any(x => Path.GetExtension(x).Equals(".exe", StringComparison.OrdinalIgnoreCase));
            if (bat && exe) return T("● BAT／EXEあり", "● BAT / EXE found");
            return bat ? T("● BATあり", "● BAT found") : T("● EXEあり", "● EXE found");
        }

        private string GetString(Dictionary<string, object> d, string[] keys, string fallback)
        {
            foreach (string k in keys)
            {
                foreach (KeyValuePair<string, object> kv in d)
                {
                    if (!string.IsNullOrEmpty(kv.Key) && kv.Key.Equals(k, StringComparison.OrdinalIgnoreCase) && kv.Value != null)
                        return kv.Value.ToString();
                }
            }
            return fallback;
        }

        private Dictionary<string, object> GetDictionary(Dictionary<string, object> d, string key)
        {
            if (d == null) return null;
            foreach (KeyValuePair<string, object> kv in d)
                if (kv.Key.Equals(key, StringComparison.OrdinalIgnoreCase)) return kv.Value as Dictionary<string, object>;
            return null;
        }

        private List<string> GetStringList(Dictionary<string, object> d, string[] keys)
        {
            List<string> result = new List<string>();
            if (d == null) return result;
            object value = null;
            foreach (string key in keys)
                foreach (KeyValuePair<string, object> kv in d)
                    if (kv.Key.Equals(key, StringComparison.OrdinalIgnoreCase)) { value = kv.Value; break; }
            if (value == null) return result;
            object[] array = value as object[];
            if (array == null)
            {
                ArrayList list = value as ArrayList;
                if (list != null) array = list.ToArray();
            }
            if (array != null)
                foreach (object item in array)
                {
                    Dictionary<string, object> itemObject = item as Dictionary<string, object>;
                    string text = itemObject == null ? Convert.ToString(item, CultureInfo.InvariantCulture) :
                        GetString(itemObject, new string[] { "id", "modId", "workshopId", "name" }, "");
                    if (!string.IsNullOrWhiteSpace(text)) result.Add(text.Trim());
                }
            else
            {
                string text = Convert.ToString(value, CultureInfo.InvariantCulture);
                if (!string.IsNullOrWhiteSpace(text)) result.AddRange(text.Split(new char[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()));
            }
            return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private string ReadJsonStringLoose(string text, string key, string fallback)
        {
            try
            {
                Match m = Regex.Match(text, "\\\"" + Regex.Escape(key) + "\\\"\\s*:\\s*\\\"((?:\\\\.|[^\\\"])*)\\\"", RegexOptions.IgnoreCase);
                if (m.Success) return Regex.Unescape(m.Groups[1].Value);
            }
            catch { }
            return fallback;
        }

        private string FindFallbackImage(string dir)
        {
            try
            {
                string[] files = Directory.GetFiles(dir, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(f =>
                    {
                        string e = Path.GetExtension(f).ToLowerInvariant();
                        return e == ".jpg" || e == ".jpeg" || e == ".png";
                    }).ToArray();
                string preferred = files.FirstOrDefault(f =>
                {
                    string n = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                    return n.Contains("preview") || n.Contains("thumb") || n.Contains("icon");
                });
                return preferred ?? files.FirstOrDefault();
            }
            catch { return null; }
        }

        private void PopulateList(ListView list, ImageList images, List<ModInfo> mods, bool managed)
        {
            EnsureSettingsState();
            if (list == null || images == null) return;
            if (mods == null) mods = new List<ModInfo>();
            populating = true;
            list.BeginUpdate();
            try
            {
                list.Items.Clear();
                images.Images.Clear();

                int imageNumber = 0;
                foreach (ModInfo mod in mods)
                {
                    if (mod == null) continue;
                    if (mod.ConflictFiles == null) mod.ConflictFiles = new List<string>();
                    if (mod.ConflictMods == null) mod.ConflictMods = new List<string>();
                    string imageKey = "mod_image_" + imageNumber.ToString(CultureInfo.InvariantCulture);
                    imageNumber++;
                    Image thumb = LoadImageNoLock(mod.PreviewImage);
                    if (thumb != null) images.Images.Add(imageKey, thumb);

                    ListViewItem item = new ListViewItem(mod.DisplayName ?? mod.Id ?? "Unknown");
                    item.Tag = mod;
                    if (thumb != null) item.ImageKey = imageKey;
                    item.SubItems.Add(mod.SourceName ?? "");
                    item.SubItems.Add(IncludedToolsSummary(mod));
                    item.SubItems.Add(IncludedDocumentsSummary(mod));
                    item.SubItems.Add("");
                    item.SubItems.Add("");
                    item.SubItems.Add("");
                    item.SubItems.Add(managed ? "—" : T("未確認", "Not checked"));
                    int orderIndex = currentModOrder == null ? -1 : currentModOrder.FindIndex(x => string.Equals(x, mod.ActiveToken, StringComparison.OrdinalIgnoreCase));
                    item.SubItems.Add(orderIndex < 0 ? "—" : (orderIndex + 1).ToString(CultureInfo.InvariantCulture));
                    item.UseItemStyleForSubItems = false;
                    Color rowBack = (list.Items.Count % 2 == 0) ? Dw2Deep : Dw2Panel;
                    item.BackColor = rowBack;
                    foreach (ListViewItem.ListViewSubItem subItem in item.SubItems) subItem.BackColor = rowBack;
                    list.Items.Add(item);
                }
            }
            finally
            {
                list.EndUpdate();
                populating = false;
            }
        }

        private Image LoadImageNoLock(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                using (MemoryStream ms = new MemoryStream(bytes))
                using (Image temp = Image.FromStream(ms))
                    return new Bitmap(temp);
            }
            catch { return null; }
        }

        private void ShowModDetails(ModInfo mod, PictureBox preview, Label name, Label desc)
        {
            if (mod == null || preview == null || name == null || desc == null) return;
            if (preview.Image != null) { Image old = preview.Image; preview.Image = null; old.Dispose(); }
            preview.Image = LoadImageNoLock(mod.PreviewImage);
            name.Text = (mod.DisplayName ?? "") + (string.IsNullOrWhiteSpace(mod.Version) ? "" : "  v" + mod.Version);

            StringBuilder b = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(mod.Description)) b.AppendLine(mod.Description.Trim());
            b.AppendLine();
            b.AppendLine(T("取得元: ", "Source: ") + (mod.SourceName ?? ""));
            b.AppendLine(T("状態: ", "State: ") + (IsModSelected(mod) ? "ON" : "OFF"));
            b.AppendLine(mod.Folder ?? "");
            if (!string.IsNullOrWhiteSpace(mod.ModJsonLaunchArguments)) b.AppendLine(T("自動起動引数: ", "Automatic launch arguments: ") + mod.ModJsonLaunchArguments);
            if (mod.IncludedTools != null && mod.IncludedTools.Count > 0)
            {
                b.AppendLine();
                b.AppendLine(T("付属ツール: ", "Included tools: ") + mod.IncludedTools.Count);
                foreach (string tool in mod.IncludedTools) b.AppendLine("  • " + tool);
            }
            if (mod.IncludedDocuments != null && mod.IncludedDocuments.Count > 0)
            {
                b.AppendLine();
                b.AppendLine(T("付属文書: ", "Included documents: ") + mod.IncludedDocuments.Count);
                foreach (string document in mod.IncludedDocuments) b.AppendLine("  • " + document);
            }
            if (mod.RequiredMods != null && mod.RequiredMods.Count > 0) b.AppendLine("Required: " + string.Join(", ", mod.RequiredMods.ToArray()));
            if (mod.OptionalMods != null && mod.OptionalMods.Count > 0) b.AppendLine("Optional: " + string.Join(", ", mod.OptionalMods.ToArray()));
            if (mod.IncompatibleMods != null && mod.IncompatibleMods.Count > 0) b.AppendLine("Incompatible: " + string.Join(", ", mod.IncompatibleMods.ToArray()));
            if (mod.LoadBefore != null && mod.LoadBefore.Count > 0) b.AppendLine("LoadBefore: " + string.Join(", ", mod.LoadBefore.ToArray()));
            if (mod.LoadAfter != null && mod.LoadAfter.Count > 0) b.AppendLine("LoadAfter: " + string.Join(", ", mod.LoadAfter.ToArray()));

            if (mod.DuplicateCount > 0)
            {
                b.AppendLine();
                b.AppendLine(T("● 重複導入: ", "● Duplicate installations: ") + mod.DuplicateCount + T("か所", " locations"));
                foreach (string location in mod.DuplicateLocations.Take(8)) b.AppendLine("  • " + location);
            }

            if (IsModSelected(mod))
            {
                if (mod.ConflictCount > 0)
                {
                    b.AppendLine();
                    b.AppendLine(T("● 競合あり: ", "● Conflicts: ") + mod.ConflictCount + T("ファイル", " files") +
                        T("（高危険 ", " (high ") + mod.HighRiskConflictCount + T("／低危険 ", " / low ") + mod.LowRiskConflictCount + "）");
                    if (mod.ConflictMods != null && mod.ConflictMods.Count > 0)
                        b.AppendLine(T("競合MOD: ", "Conflicts with: ") + string.Join(", ", mod.ConflictMods.Take(8).ToArray()));
                    if (mod.ConflictFiles != null)
                    {
                        foreach (string file in mod.ConflictFiles.Take(8)) b.AppendLine("  • " + file);
                        if (mod.ConflictFiles.Count > 8) b.AppendLine("  ... +" + (mod.ConflictFiles.Count - 8));
                    }
                }
                else
                {
                    b.AppendLine();
                    b.AppendLine(T("● 競合なし", "● No file conflicts"));
                    if (mod.IdenticalFileCount > 0) b.AppendLine(T("同一パス・同一内容: ", "Same path and identical content: ") + mod.IdenticalFileCount);
                }
            }
            else
            {
                b.AppendLine();
                b.AppendLine(T("MODはOFFです。競合判定の対象外です。", "MOD is OFF and excluded from conflict analysis."));
            }

            if (mod.IsWorkshop)
            {
                b.AppendLine();
                if (mod.UpdateState == "update")
                    b.AppendLine(T("⚠ Steam Workshop: 更新あり", "⚠ Steam Workshop: Update available"));
                else if (mod.UpdateState == "current")
                    b.AppendLine(T("● Steam Workshop: 最新", "● Steam Workshop: Up to date"));
                else
                    b.AppendLine(T("? Steam Workshop: 更新状態未確認", "? Steam Workshop: Update state unknown"));

                if (mod.LocalWorkshopTimeUpdated > 0)
                    b.AppendLine(T("ローカル更新: ", "Local update: ") + UnixTimeText(mod.LocalWorkshopTimeUpdated));
                if (mod.RemoteWorkshopTimeUpdated > 0)
                    b.AppendLine(T("Steam更新: ", "Steam update: ") + UnixTimeText(mod.RemoteWorkshopTimeUpdated));
            }
            desc.Text = b.ToString();
        }

        private bool IsModSelected(ModInfo mod)
        {
            if (mod == null) return false;
            if (modOrderFileFound && !string.IsNullOrWhiteSpace(mod.ActiveToken))
                return currentModOrder.Any(x => x.Equals(mod.ActiveToken, StringComparison.OrdinalIgnoreCase));
            bool selected;
            if (settings.SelectedMods != null && settings.SelectedMods.TryGetValue(mod.Key, out selected)) return selected;
            return false;
        }

        // Red conflicts are based only on the authoritative enabled set.
        // Installed but disabled Workshop/local copies must not participate.
        private bool IsModEnabledForConflict(ModInfo mod)
        {
            if (mod == null || string.IsNullOrWhiteSpace(mod.ActiveToken)) return false;
            if (modOrderFileFound)
                return currentModOrder != null && currentModOrder.Any(x =>
                    string.Equals(x, mod.ActiveToken, StringComparison.OrdinalIgnoreCase));

            bool enabled;
            return settings.SelectedMods != null &&
                   settings.SelectedMods.TryGetValue(mod.Key, out enabled) && enabled;
        }

        private void ShowStateDropDown(ListView list, Point location)
        {
            if (list == null || populating) return;
            ListViewHitTestInfo hit = list.HitTest(location);
            if (hit == null || hit.Item == null || hit.SubItem == null) return;
            int column = hit.Item.SubItems.IndexOf(hit.SubItem);
            if (column != 4) return;

            HideStateDropDown();
            stateEditorList = list;
            stateEditorItem = hit.Item;
            ModInfo mod = stateEditorItem.Tag as ModInfo;
            if (mod == null) { HideStateDropDown(); return; }

            stateEditor = new ComboBox();
            stateEditor.DropDownStyle = ComboBoxStyle.DropDownList;
            stateEditor.Items.Add(T("有効（ON）", "Enabled (ON)"));
            stateEditor.Items.Add(T("無効（OFF）", "Disabled (OFF)"));
            stateEditor.SelectedIndex = IsModSelected(mod) ? 0 : 1;
            Rectangle bounds = hit.SubItem.Bounds;
            stateEditor.Bounds = new Rectangle(bounds.X, bounds.Y, Math.Max(105, bounds.Width), bounds.Height + 2);
            stateEditor.Font = list.Font;
            ComboBox editor = stateEditor;
            ListViewItem editedItem = stateEditorItem;
            stateEditor.SelectionChangeCommitted += delegate
            {
                ModInfo selectedMod = editedItem == null ? null : editedItem.Tag as ModInfo;
                bool enabled = editor.SelectedIndex == 0;
                HideStateDropDown();
                ApplyModEnabledSelection(selectedMod, enabled);
            };
            stateEditor.DropDownClosed += delegate
            {
                if (stateEditor == editor) BeginInvoke(new MethodInvoker(HideStateDropDown));
            };
            list.Controls.Add(stateEditor);
            stateEditor.BringToFront();
            stateEditor.Focus();
            stateEditor.DroppedDown = true;
        }

        private void HideStateDropDown()
        {
            ComboBox old = stateEditor;
            stateEditor = null;
            stateEditorList = null;
            stateEditorItem = null;
            if (old != null)
            {
                try { old.DroppedDown = false; old.Parent.Controls.Remove(old); old.Dispose(); }
                catch { }
            }
        }

        private void ApplyModEnabledSelection(ModInfo mod, bool enabled)
        {
            if (mod == null) return;
            EnsureSettingsState();
            settings.SelectedMods[mod.Key] = enabled;
            SaveSettings();
            ApplyManagedSelectionToIni(mod, enabled);
            SaveModOrderSelection(mod, enabled);
            AnalyzeConflicts();
            RefreshModStatusColumns();
            RefreshSelectedDetails();
            UpdateCommandPreview();
        }

        private bool IgnoreConflictFile(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) return true;
            string rel = relativePath.Replace('/', '\\').TrimStart('\\');
            string file = Path.GetFileName(rel).ToLowerInvariant();

            // Launcher metadata is not loaded as game content.
            if (file == "mod.json" || file == "mods.json" || file == "launcher.json") return true;

            // Documentation and preview assets may legitimately use the same names in every MOD.
            // Check every subfolder, not only the MOD root.
            if (file.StartsWith("readme") || file.StartsWith("license") || file.StartsWith("licence") ||
                file.StartsWith("manual") || file.StartsWith("changelog") || file.StartsWith("changes") ||
                file.StartsWith("preview.") || file.StartsWith("thumbnail.") || file.StartsWith("thumb.")) return true;

            if (file.EndsWith(".log") || file.EndsWith(".bak") || file.EndsWith(".tmp") || file.EndsWith(".launcher_backup")) return true;
            string ext = Path.GetExtension(file).ToLowerInvariant();

            // Installer/utility programs are launched by the user and are not overlaid by DW2.
            if (ext == ".ps1" || ext == ".bat" || ext == ".cmd" || ext == ".exe") return true;

            // Packaged source, debug files, archives and common document formats are not game data.
            if (ext == ".zip" || ext == ".7z" || ext == ".rar" || ext == ".cs" || ext == ".sln" ||
                ext == ".csproj" || ext == ".pdb" || ext == ".pdf" || ext == ".doc" ||
                ext == ".docx" || ext == ".rtf" || ext == ".url" || ext == ".lnk") return true;
            return false;
        }

        private IEnumerable<string> EnumerateConflictPaths(ModInfo mod)
        {
            if (mod == null) yield break;
            if (mod.ConflictPathCache != null)
            {
                foreach (string cached in mod.ConflictPathCache) yield return cached;
                yield break;
            }

            List<string> cache = new List<string>();
            string root = !string.IsNullOrWhiteSpace(mod.ContentRoot) ? mod.ContentRoot : mod.Folder;
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                mod.ConflictPathCache = cache;
                yield break;
            }
            string[] files;
            try { files = Directory.GetFiles(root, "*", SearchOption.AllDirectories); }
            catch
            {
                mod.ConflictPathCache = cache;
                yield break;
            }
            foreach (string file in files)
            {
                string rel;
                try
                {
                    rel = file.Substring(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length)
                              .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                              .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
                }
                catch { continue; }
                if (IgnoreConflictFile(rel)) continue;
                cache.Add(rel.ToLowerInvariant());
            }
            mod.ConflictPathCache = cache;
            foreach (string rel in cache) yield return rel;
        }

        private void AnalyzeConflicts()
        {
            currentCollisions = new Dictionary<string, List<ModInfo>>(StringComparer.OrdinalIgnoreCase);
            if (currentManagedMods == null) currentManagedMods = new List<ModInfo>();
            if (currentWorkshopMods == null) currentWorkshopMods = new List<ModInfo>();
            List<ModInfo> all = currentManagedMods.Concat(currentWorkshopMods).Where(m => m != null).ToList();
            foreach (ModInfo mod in all)
            {
                mod.ConflictCount = 0;
                if (mod.ConflictFiles == null) mod.ConflictFiles = new List<string>(); else mod.ConflictFiles.Clear();
                if (mod.ConflictMods == null) mod.ConflictMods = new List<string>(); else mod.ConflictMods.Clear();
                mod.IdenticalFileCount = 0;
                mod.LowRiskConflictCount = 0;
                mod.HighRiskConflictCount = 0;
            }

            List<ModInfo> enabledMods = all.Where(IsModEnabledForConflict).ToList();
            Dictionary<string, List<ModInfo>> owners = new Dictionary<string, List<ModInfo>>(StringComparer.OrdinalIgnoreCase);
            foreach (ModInfo mod in enabledMods)
            {
                HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string rel in EnumerateConflictPaths(mod))
                {
                    if (!seen.Add(rel)) continue;
                    List<ModInfo> list;
                    if (!owners.TryGetValue(rel, out list))
                    {
                        list = new List<ModInfo>();
                        owners[rel] = list;
                    }
                    list.Add(mod);
                }
            }

            foreach (KeyValuePair<string, List<ModInfo>> kv in owners)
            {
                List<ModInfo> unique = kv.Value
                    .Where(IsModEnabledForConflict)
                    .GroupBy(m => m.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .ToList();
                if (unique.Count < 2) continue;
                List<string> fileHashes = unique.Select(m => ConflictFileHash(m, kv.Key)).ToList();
                List<string> hashes = fileHashes.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (hashes.Count == 1 && fileHashes.All(x => !string.IsNullOrWhiteSpace(x)))
                {
                    foreach (ModInfo mod in unique) mod.IdenticalFileCount++;
                    continue;
                }
                currentCollisions[kv.Key] = unique;
                string extension = Path.GetExtension(kv.Key).ToLowerInvariant();
                bool highRisk = extension == ".dll" || extension == ".exe" || extension == ".xml" || extension == ".json" || extension == ".bin";
                foreach (ModInfo mod in unique)
                {
                    mod.ConflictFiles.Add(kv.Key);
                    if (highRisk) mod.HighRiskConflictCount++; else mod.LowRiskConflictCount++;
                    foreach (ModInfo other in unique)
                    {
                        if (other.Key == mod.Key) continue;
                        string otherName = other.DisplayName ?? other.Id ?? "Unknown";
                        if (!mod.ConflictMods.Contains(otherName)) mod.ConflictMods.Add(otherName);
                    }
                }
            }

            foreach (ModInfo mod in all) mod.ConflictCount = mod.ConflictFiles == null ? 0 : mod.ConflictFiles.Count;
        }

        private string ConflictFileHash(ModInfo mod, string relativePath)
        {
            try
            {
                string root = !string.IsNullOrWhiteSpace(mod.ContentRoot) ? mod.ContentRoot : mod.Folder;
                string file = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar));
                using (SHA256 sha = SHA256.Create())
                using (FileStream stream = File.OpenRead(file)) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
            }
            catch { return ""; }
        }

        private void AnalyzeDuplicates()
        {
            List<ModInfo> all = currentManagedMods.Concat(currentWorkshopMods).Where(m => m != null).ToList();
            foreach (ModInfo mod in all)
            {
                mod.DuplicateCount = 0;
                if (mod.DuplicateLocations == null) mod.DuplicateLocations = new List<string>();
                else mod.DuplicateLocations.Clear();
            }
            Dictionary<string, List<ModInfo>> groups = new Dictionary<string, List<ModInfo>>(StringComparer.OrdinalIgnoreCase);
            foreach (ModInfo mod in all)
            {
                string identity = Regex.Replace((mod.DisplayName ?? mod.Id ?? Path.GetFileName(mod.Folder) ?? "").Trim().ToLowerInvariant(), "[^a-z0-9ぁ-んァ-ン一-龯]+", "");
                if (identity.Length < 3) continue;
                List<ModInfo> list;
                if (!groups.TryGetValue(identity, out list)) { list = new List<ModInfo>(); groups[identity] = list; }
                list.Add(mod);
            }
            foreach (List<ModInfo> group in groups.Values.Where(g => g.Select(x => x.Folder).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1))
            {
                foreach (ModInfo mod in group)
                {
                    foreach (ModInfo other in group.Where(x => !string.Equals(x.Folder, mod.Folder, StringComparison.OrdinalIgnoreCase)))
                    {
                        string location = (other.SourceName ?? "") + " | " + (other.Version ?? "") + " | " + (other.Folder ?? "");
                        if (!mod.DuplicateLocations.Contains(location)) mod.DuplicateLocations.Add(location);
                    }
                    mod.DuplicateCount = mod.DuplicateLocations.Count;
                }
            }
        }

        private void RefreshModStatusColumns()
        {
            RefreshOneModListStatus(managedList);
            RefreshOneModListStatus(workshopList);
            UpdateOverallStatus();
        }

        private void RefreshOneModListStatus(ListView list)
        {
            if (list == null) return;
            foreach (ListViewItem item in list.Items)
            {
                ModInfo mod = item.Tag as ModInfo;
                if (mod == null || item.SubItems.Count < 9) continue;
                item.UseItemStyleForSubItems = false;

                item.SubItems[2].Text = IncludedToolsSummary(mod);
                item.SubItems[2].ForeColor = mod.IncludedTools != null && mod.IncludedTools.Count > 0 ? Dw2Gold : Dw2Muted;
                item.SubItems[3].Text = IncludedDocumentsSummary(mod);
                item.SubItems[3].ForeColor = mod.IncludedDocuments != null && mod.IncludedDocuments.Count > 0 ? Dw2BlueGlow : Dw2Muted;
                item.SubItems[4].Text = IsModSelected(mod) ? T("有効（ON） ▼", "Enabled (ON) ▼") : T("無効（OFF） ▼", "Disabled (OFF) ▼");
                item.SubItems[4].ForeColor = IsModSelected(mod) ? Dw2Green : Dw2Muted;

                if (!IsModSelected(mod))
                {
                    item.SubItems[5].Text = T("● MOD無効", "● MOD disabled");
                    item.SubItems[5].ForeColor = Dw2Muted;
                }
                else if (mod.ConflictCount > 0)
                {
                    item.SubItems[5].Text = mod.HighRiskConflictCount > 0
                        ? T("● 高危険: " + mod.HighRiskConflictCount, "● High risk: " + mod.HighRiskConflictCount)
                        : T("● 低危険: " + mod.LowRiskConflictCount, "● Low risk: " + mod.LowRiskConflictCount);
                    item.SubItems[5].ForeColor = mod.HighRiskConflictCount > 0 ? Dw2Red : Dw2Gold;
                }
                else if (mod.IdenticalFileCount > 0)
                {
                    item.SubItems[5].Text = T("● 同一内容: " + mod.IdenticalFileCount, "● Identical: " + mod.IdenticalFileCount);
                    item.SubItems[5].ForeColor = Dw2BlueGlow;
                }
                else
                {
                    item.SubItems[5].Text = T("● 競合なし", "● No conflicts");
                    item.SubItems[5].ForeColor = Dw2Green;
                }

                item.SubItems[6].Text = mod.DuplicateCount > 0 ? T("● 重複導入: " + mod.DuplicateCount, "● Duplicates: " + mod.DuplicateCount) : T("● 重複なし", "● No duplicates");
                item.SubItems[6].ForeColor = mod.DuplicateCount > 0 ? Dw2Gold : Dw2Muted;

                if (!mod.IsWorkshop)
                {
                    item.SubItems[7].Text = "—";
                    item.SubItems[7].ForeColor = Dw2Muted;
                }
                else if (mod.UpdateState == "update")
                {
                    item.SubItems[7].Text = T("● 更新あり", "● Update available");
                    item.SubItems[7].ForeColor = Dw2Gold;
                }
                else if (mod.UpdateState == "current")
                {
                    item.SubItems[7].Text = T("● 最新", "● Current");
                    item.SubItems[7].ForeColor = Dw2Green;
                }
                else
                {
                    item.SubItems[7].Text = T("― 未確認", "— Not checked");
                    item.SubItems[7].ForeColor = Dw2Muted;
                }
            }
        }

        private void ApplyAlternatingRowColors(ListView list)
        {
            if (list == null) return;
            for (int i = 0; i < list.Items.Count; i++)
            {
                Color rowBack = (i % 2 == 0) ? Dw2Deep : Dw2Panel;
                list.Items[i].BackColor = rowBack;
                foreach (ListViewItem.ListViewSubItem subItem in list.Items[i].SubItems) subItem.BackColor = rowBack;
            }
        }

        private void RefreshLoadOrderNumbers()
        {
            foreach (ListView list in new ListView[] { managedList, workshopList })
            {
                if (list == null) continue;
                foreach (ListViewItem item in list.Items)
                {
                    ModInfo mod = item.Tag as ModInfo;
                    if (mod == null || item.SubItems.Count < 9) continue;
                    int index = currentModOrder == null ? -1 : currentModOrder.FindIndex(x => string.Equals(x, mod.ActiveToken, StringComparison.OrdinalIgnoreCase));
                    item.SubItems[8].Text = index < 0 ? "—" : (index + 1).ToString(CultureInfo.InvariantCulture);
                    item.SubItems[8].ForeColor = index < 0 ? Dw2Muted : Dw2Gold;
                }
            }
        }

        private void RefreshSelectedDetails()
        {
            if (managedList != null && managedList.SelectedItems.Count > 0)
                ShowModDetails(managedList.SelectedItems[0].Tag as ModInfo, managedPreview, managedName, managedDesc);
            if (workshopList != null && workshopList.SelectedItems.Count > 0)
                ShowModDetails(workshopList.SelectedItems[0].Tag as ModInfo, workshopPreview, workshopName, workshopDesc);
        }

        private void UpdateOverallStatus()
        {
            if (statusLabel == null) return;
            if (currentManagedMods == null) currentManagedMods = new List<ModInfo>();
            if (currentWorkshopMods == null) currentWorkshopMods = new List<ModInfo>();
            if (currentCollisions == null) currentCollisions = new Dictionary<string, List<ModInfo>>(StringComparer.OrdinalIgnoreCase);
            int updates = currentWorkshopMods.Count(m => m != null && m.UpdateState == "update");
            int selected = currentManagedMods.Concat(currentWorkshopMods).Where(m => m != null).Count(IsModSelected);
            string conflictText = currentCollisions.Count == 0 ? T("競合なし", "No conflicts") : T("競合ファイル ", "Conflict files ") + currentCollisions.Count;
            string updateText = updates == 0 ? T("更新なし/未確認", "No updates/unchecked") : T("更新あり ", "Updates ") + updates;
            if (modOrderReadFailed) updateText = T("mods.json 読込失敗", "mods.json ERROR");
            int duplicates = currentManagedMods.Concat(currentWorkshopMods).Count(m => m != null && m.DuplicateCount > 0);
            string duplicateText = duplicates == 0 ? T("重複導入なし", "No duplicate installations") : T("重複導入 ", "Duplicate installations ") + duplicates;
            statusLabel.Text = string.Format(T("DW2 MOD: {0}件｜Workshop: {1}件｜有効: {2}件｜{3}｜{4}｜{5}", "DW2 MODs: {0} | Workshop: {1} | Enabled: {2} | {3} | {4} | {5}"), currentManagedMods.Count, currentWorkshopMods.Count, selected, conflictText, duplicateText, updateText);
        }

        private string BuildConflictWarning()
        {
            StringBuilder b = new StringBuilder();
            b.AppendLine(T("選択中のMODに同じ相対パスのファイルが見つかりました。", "Selected mods contain files with the same relative path."));
            b.AppendLine(T("DW2では上書き競合になる可能性があります。自動マージは行いません。", "These may overwrite each other in DW2. No automatic merge will be performed."));
            b.AppendLine();
            foreach (KeyValuePair<string, List<ModInfo>> kv in currentCollisions.Take(10))
            {
                b.AppendLine("• " + kv.Key);
                b.AppendLine("  " + string.Join("  ↔  ", kv.Value.Select(m => m.DisplayName ?? m.Id).ToArray()));
            }
            if (currentCollisions.Count > 10) b.AppendLine("... +" + (currentCollisions.Count - 10));
            b.AppendLine();
            b.AppendLine(T("この構成のまま起動しますか？（自己責任で続行）", "Launch with this configuration anyway? (Proceed at your own peril)"));
            return b.ToString();
        }

        private string FindWorkshopManifestPath()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(settings.WorkshopRoot)) return null;
                DirectoryInfo contentApp = new DirectoryInfo(settings.WorkshopRoot);
                DirectoryInfo content = contentApp.Parent;
                DirectoryInfo workshop = content == null ? null : content.Parent;
                if (workshop == null) return null;
                string path = Path.Combine(workshop.FullName, "appworkshop_" + AppId + ".acf");
                return File.Exists(path) ? path : null;
            }
            catch { return null; }
        }

        private string ExtractAcfBlock(string text, string sectionName)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(sectionName)) return null;
            Match m = Regex.Match(text, "\\\"" + Regex.Escape(sectionName) + "\\\"\\s*\\{", RegexOptions.IgnoreCase);
            if (!m.Success) return null;
            int open = text.IndexOf('{', m.Index);
            if (open < 0) return null;
            int depth = 0;
            for (int i = open; i < text.Length; i++)
            {
                if (text[i] == '{') depth++;
                else if (text[i] == '}')
                {
                    depth--;
                    if (depth == 0) return text.Substring(open + 1, i - open - 1);
                }
            }
            return null;
        }

        private Dictionary<string, long> ParseAcfSectionTimes(string text, string sectionName)
        {
            Dictionary<string, long> result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            string block = ExtractAcfBlock(text, sectionName);
            if (string.IsNullOrEmpty(block)) return result;
            MatchCollection entries = Regex.Matches(block, "\\\"(\\d+)\\\"\\s*\\{([^{}]*)\\}", RegexOptions.Singleline);
            foreach (Match entry in entries)
            {
                Match tm = Regex.Match(entry.Groups[2].Value, "\\\"timeupdated\\\"\\s*\\\"(\\d+)\\\"", RegexOptions.IgnoreCase);
                long value;
                if (tm.Success && long.TryParse(tm.Groups[1].Value, out value)) result[entry.Groups[1].Value] = value;
            }
            return result;
        }

        private void BeginWorkshopUpdateCheck(bool force)
        {
            if (updateCheckRunning || currentWorkshopMods == null || currentWorkshopMods.Count == 0) return;
            updateCheckRunning = true;
            workshopCheckWasManual = force;
            if (workshopUpdateButton != null) workshopUpdateButton.Enabled = false;
            SetStatus(T("Steam Workshopの更新を確認しています...", "Checking Steam Workshop updates..."));

            string manifestPath = FindWorkshopManifestPath();
            List<string> ids = currentWorkshopMods.Where(m => m != null).Select(m => m.Id).Where(id => !string.IsNullOrWhiteSpace(id) && Regex.IsMatch(id, "^\\d+$")).Distinct().ToList();
            BackgroundWorker worker = new BackgroundWorker();
            worker.DoWork += delegate(object sender, DoWorkEventArgs e)
            {
                WorkshopUpdateCheckResult r = new WorkshopUpdateCheckResult();
                try
                {
                    if (!string.IsNullOrWhiteSpace(manifestPath) && File.Exists(manifestPath))
                    {
                        string acf = File.ReadAllText(manifestPath, Encoding.UTF8);
                        r.InstalledTimes = ParseAcfSectionTimes(acf, "WorkshopItemsInstalled");
                        r.DetailTimes = ParseAcfSectionTimes(acf, "WorkshopItemDetails");
                    }
                    try
                    {
                        Dictionary<string, WorkshopRemoteDetail> details;
                        r.RemoteTimes = FetchRemoteWorkshopTimes(ids, out details);
                        r.Details = details;
                    }
                    catch (Exception ex) { r.Error = ex.Message; }
                }
                catch (Exception ex) { r.Error = ex.Message; }
                e.Result = r;
            };
            worker.RunWorkerCompleted += delegate(object sender, RunWorkerCompletedEventArgs e)
            {
                updateCheckRunning = false;
                if (workshopUpdateButton != null) workshopUpdateButton.Enabled = true;
                try
                {
                    if (e != null && e.Error != null)
                    {
                        Program.LogException("Workshop background worker", e.Error);
                        SetStatus(T("Workshop更新確認に失敗しました。ランチャーは継続します。", "Workshop update check failed. Launcher will continue."));
                        UpdateOverallStatus();
                        return;
                    }
                    WorkshopUpdateCheckResult r = e == null ? null : e.Result as WorkshopUpdateCheckResult;
                    if (r != null) ApplyWorkshopUpdateResults(r);
                    else UpdateOverallStatus();
                }
                catch (Exception ex)
                {
                    Program.LogException("Workshop completion", ex);
                    SetStatus(T("Workshop更新結果の反映をスキップしました。", "Skipped applying Workshop update results."));
                    UpdateOverallStatus();
                }
            };
            worker.RunWorkerAsync();
        }

        private Dictionary<string, long> FetchRemoteWorkshopTimes(List<string> ids, out Dictionary<string, WorkshopRemoteDetail> remoteDetails)
        {
            Dictionary<string, long> result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            remoteDetails = new Dictionary<string, WorkshopRemoteDetail>(StringComparer.OrdinalIgnoreCase);
            if (ids == null || ids.Count == 0) return result;
            const int batchSize = 50;
            for (int start = 0; start < ids.Count; start += batchSize)
            {
                List<string> batch = ids.Skip(start).Take(batchSize).ToList();
                StringBuilder form = new StringBuilder();
                form.Append("itemcount=").Append(batch.Count);
                for (int i = 0; i < batch.Count; i++)
                    form.Append("&publishedfileids%5B").Append(i).Append("%5D=").Append(Uri.EscapeDataString(batch[i]));
                byte[] body = Encoding.UTF8.GetBytes(form.ToString());

                HttpWebRequest req = (HttpWebRequest)WebRequest.Create("https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/");
                req.Method = "POST";
                req.ContentType = "application/x-www-form-urlencoded";
                req.ContentLength = body.Length;
                req.Timeout = 8000;
                req.ReadWriteTimeout = 8000;
                req.UserAgent = "DW2ModLauncherBeta/0.4.0";
                using (Stream stream = req.GetRequestStream()) stream.Write(body, 0, body.Length);
                string responseText;
                using (HttpWebResponse response = (HttpWebResponse)req.GetResponse())
                using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8)) responseText = reader.ReadToEnd();

                object rootObj = new JavaScriptSerializer().DeserializeObject(responseText);
                Dictionary<string, object> root = rootObj as Dictionary<string, object>;
                if (root == null || !root.ContainsKey("response")) continue;
                Dictionary<string, object> responseObj = root["response"] as Dictionary<string, object>;
                if (responseObj == null || !responseObj.ContainsKey("publishedfiledetails")) continue;
                object[] details = responseObj["publishedfiledetails"] as object[];
                if (details == null)
                {
                    System.Collections.ArrayList arr = responseObj["publishedfiledetails"] as System.Collections.ArrayList;
                    if (arr != null) details = arr.ToArray();
                }
                if (details == null) continue;
                foreach (object itemObj in details)
                {
                    Dictionary<string, object> item = itemObj as Dictionary<string, object>;
                    if (item == null) continue;
                    string id = DictionaryValue(item, "publishedfileid");
                    string time = DictionaryValue(item, "time_updated");
                    long value = 0;
                    if (!string.IsNullOrWhiteSpace(id) && long.TryParse(time, out value)) result[id] = value;
                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        WorkshopRemoteDetail detail = new WorkshopRemoteDetail();
                        detail.Id = id;
                        detail.Title = DictionaryValue(item, "title");
                        detail.Description = DictionaryValue(item, "description");
                        detail.PreviewUrl = DictionaryValue(item, "preview_url");
                        detail.Creator = DictionaryValue(item, "creator");
                        detail.TimeUpdated = value;
                        long created;
                        long.TryParse(DictionaryValue(item, "time_created"), out created);
                        detail.TimeCreated = created;
                        detail.Tags = WorkshopTagsValue(item);
                        long size;
                        long.TryParse(DictionaryValue(item, "file_size"), out size);
                        detail.FileSize = size;
                        remoteDetails[id] = detail;
                    }
                }
            }
            return result;
        }

        private string DictionaryValue(Dictionary<string, object> d, string key)
        {
            if (d == null) return "";
            foreach (KeyValuePair<string, object> kv in d)
                if (!string.IsNullOrEmpty(kv.Key) && kv.Key.Equals(key, StringComparison.OrdinalIgnoreCase) && kv.Value != null) return Convert.ToString(kv.Value, System.Globalization.CultureInfo.InvariantCulture);
            return "";
        }

        private string WorkshopTagsValue(Dictionary<string, object> item)
        {
            if (item == null || !item.ContainsKey("tags")) return "";
            List<string> tags = new List<string>();
            object[] array = item["tags"] as object[];
            if (array == null)
            {
                System.Collections.ArrayList list = item["tags"] as System.Collections.ArrayList;
                if (list != null) array = list.ToArray();
            }
            if (array != null)
                foreach (object value in array)
                {
                    Dictionary<string, object> tag = value as Dictionary<string, object>;
                    string text = DictionaryValue(tag, "tag");
                    if (!string.IsNullOrWhiteSpace(text)) tags.Add(text);
                }
            return string.Join(", ", tags.ToArray());
        }

        private void ApplyWorkshopUpdateResults(WorkshopUpdateCheckResult r)
        {
            if (r == null) return;
            if (currentWorkshopMods == null) currentWorkshopMods = new List<ModInfo>();
            int updates = 0;
            foreach (ModInfo mod in currentWorkshopMods)
            {
                if (mod == null) continue;
                long installed = 0;
                long detail = 0;
                long remote = 0;
                if (r.InstalledTimes != null) r.InstalledTimes.TryGetValue(mod.Id ?? "", out installed);
                if (r.DetailTimes != null) r.DetailTimes.TryGetValue(mod.Id ?? "", out detail);
                if (r.RemoteTimes != null) r.RemoteTimes.TryGetValue(mod.Id ?? "", out remote);
                WorkshopRemoteDetail remoteDetail = null;
                if (r.Details != null) r.Details.TryGetValue(mod.Id ?? "", out remoteDetail);
                if (remoteDetail != null)
                {
                    mod.WorkshopTitle = remoteDetail.Title;
                    mod.WorkshopDescription = remoteDetail.Description;
                    mod.WorkshopPreviewUrl = remoteDetail.PreviewUrl;
                    mod.WorkshopCreator = remoteDetail.Creator;
                    mod.WorkshopFileSize = remoteDetail.FileSize;
                    mod.WorkshopTimeCreated = remoteDetail.TimeCreated;
                    mod.WorkshopTags = remoteDetail.Tags;
                    if (!string.IsNullOrWhiteSpace(remoteDetail.Title)) mod.DisplayName = remoteDetail.Title;
                    if (!string.IsNullOrWhiteSpace(remoteDetail.Description)) mod.Description = remoteDetail.Description;
                }
                mod.LocalWorkshopTimeUpdated = installed;
                mod.RemoteWorkshopTimeUpdated = remote > 0 ? remote : detail;
                long latest = Math.Max(detail, remote);
                if (installed > 0 && latest > installed + 2)
                {
                    mod.UpdateState = "update";
                    updates++;
                }
                else if (installed > 0 && latest > 0)
                    mod.UpdateState = "current";
                else
                    mod.UpdateState = "unknown";
            }
            settings.LastWorkshopUpdateCheckUtc = DateTime.UtcNow.ToString("o");
            SaveSettings();
            AnalyzeDuplicates();
            if (workshopList != null)
                foreach (ListViewItem item in workshopList.Items)
                {
                    ModInfo itemMod = item.Tag as ModInfo;
                    if (itemMod != null) item.Text = itemMod.DisplayName ?? itemMod.Id;
                }
            RefreshModStatusColumns();
            RefreshSelectedDetails();
            if (!string.IsNullOrWhiteSpace(r.Error))
                SetStatus(T("Workshop更新確認: Steam通信に失敗しました。ローカルACF情報のみ使用。 ", "Workshop update check: Steam request failed; local ACF data only. ") + r.Error);
            else if (updates > 0)
            {
                SetStatus(T("Steam Workshop: 更新あり ", "Steam Workshop: updates available ") + updates);
                if (workshopCheckWasManual)
                {
                    List<ModInfo> updateMods = currentWorkshopMods.Where(m => m != null && m.UpdateState == "update").ToList();
                    if (MessageBox.Show(T("更新前のWorkshop MODをバックアップしますか？", "Back up Workshop MODs before updating?"),
                        T("更新前バックアップ", "Pre-update backup"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                        BackupWorkshopMods(updateMods);
                }
            }
            else
                SetStatus(T("Steam Workshop: 更新は見つかりませんでした。", "Steam Workshop: no updates found."));
        }

        private void BackupWorkshopMods(IEnumerable<ModInfo> mods)
        {
            string root = Path.Combine(appRoot, "WorkshopBackups", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            try
            {
                int count = 0;
                foreach (ModInfo mod in mods ?? Enumerable.Empty<ModInfo>())
                {
                    if (mod == null || string.IsNullOrWhiteSpace(mod.Folder) || !Directory.Exists(mod.Folder)) continue;
                    CopyDirectory(mod.Folder, Path.Combine(root, SafeFileName(mod.Id + "_v" + (mod.Version ?? "unknown"))));
                    count++;
                }
                MessageBox.Show(T("Workshop旧版を保存しました: ", "Workshop backups saved: ") + count + "\r\n" + root, Text);
            }
            catch (Exception ex) { Program.LogException("Workshop backup", ex); MessageBox.Show(ex.Message, Text); }
        }

        private string UnixTimeText(long unix)
        {
            try
            {
                DateTime dt = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(unix).ToLocalTime();
                return dt.ToString("yyyy/MM/dd HH:mm");
            }
            catch { return unix.ToString(); }
        }

        private void OpenSelectedWorkshopPage()
        {
            if (workshopList == null || workshopList.SelectedItems.Count == 0)
            {
                MessageBox.Show(T("Workshop MODを1つ選択してください。", "Select a Workshop mod first."), Text);
                return;
            }
            ModInfo mod = workshopList.SelectedItems[0].Tag as ModInfo;
            if (mod == null || string.IsNullOrWhiteSpace(mod.Id)) return;
            try
            {
                Process.Start("steam://url/CommunityFilePage/" + mod.Id);
            }
            catch
            {
                try { Process.Start("https://steamcommunity.com/sharedfiles/filedetails/?id=" + mod.Id); }
                catch (Exception ex) { MessageBox.Show(ex.Message, Text); }
            }
        }

        private void OpenSelectedModDetails(ListView list)
        {
            if (list == null || list.SelectedItems.Count == 0) return;
            ModInfo mod = list.SelectedItems[0].Tag as ModInfo;
            if (mod == null) return;
            using (Form detail = new Form())
            {
                detail.Text = T("MOD詳細 - ", "MOD Details - ") + (mod.DisplayName ?? mod.Id);
                detail.StartPosition = FormStartPosition.CenterParent;
                detail.Size = new Size(900, 680);
                detail.MinimumSize = new Size(720, 520);
                detail.BackColor = Dw2Deep;
                detail.ForeColor = Dw2Text;
                detail.Font = Font;

                PictureBox image = new PictureBox();
                image.Location = new Point(18, 18);
                image.Size = new Size(260, 150);
                image.SizeMode = PictureBoxSizeMode.Zoom;
                image.BackColor = Dw2Void;
                image.Image = LoadImageNoLock(mod.PreviewImage);
                detail.Controls.Add(image);

                Label title = new Label();
                title.Location = new Point(300, 20);
                title.Size = new Size(560, 58);
                title.Font = new Font("Segoe UI Semibold", 15F, FontStyle.Bold);
                title.Text = mod.WorkshopTitle ?? mod.DisplayName ?? mod.Id;
                detail.Controls.Add(title);

                Label meta = new Label();
                meta.Location = new Point(302, 84);
                meta.Size = new Size(550, 85);
                meta.Text = T("取得元: ", "Source: ") + (mod.SourceName ?? "") + "\r\n" +
                    T("バージョン: ", "Version: ") + (mod.Version ?? "") + "\r\n" +
                    (mod.IsWorkshop ? "Workshop ID: " + mod.Id + "\r\n" : "") +
                    T("保存場所: ", "Location: ") + (mod.Folder ?? "");
                detail.Controls.Add(meta);

                TextBox information = new TextBox();
                information.Location = new Point(18, 184);
                information.Size = new Size(844, 390);
                information.Multiline = true;
                information.ReadOnly = true;
                information.ScrollBars = ScrollBars.Vertical;
                information.BackColor = Dw2Void;
                information.ForeColor = Dw2Text;
                StringBuilder body = new StringBuilder();
                string description = !string.IsNullOrWhiteSpace(mod.WorkshopDescription) ? mod.WorkshopDescription : mod.Description;
                if (!string.IsNullOrWhiteSpace(description)) body.AppendLine(Regex.Replace(description, "\\[/?[^\\]]+\\]", ""));
                body.AppendLine();
                if (mod.WorkshopFileSize > 0) body.AppendLine(T("ファイルサイズ: ", "File size: ") + mod.WorkshopFileSize + " bytes");
                if (!string.IsNullOrWhiteSpace(mod.WorkshopCreator)) body.AppendLine(T("作者Steam ID: ", "Creator Steam ID: ") + mod.WorkshopCreator);
                if (mod.WorkshopTimeCreated > 0) body.AppendLine(T("作成日時: ", "Created: ") + UnixTimeText(mod.WorkshopTimeCreated));
                if (mod.RemoteWorkshopTimeUpdated > 0) body.AppendLine(T("更新日時: ", "Updated: ") + UnixTimeText(mod.RemoteWorkshopTimeUpdated));
                if (!string.IsNullOrWhiteSpace(mod.WorkshopTags)) body.AppendLine(T("タグ: ", "Tags: ") + mod.WorkshopTags);
                if (mod.ConflictCount > 0)
                {
                    body.AppendLine(); body.AppendLine(T("競合ファイル:", "Conflict files:"));
                    foreach (string f in mod.ConflictFiles) body.AppendLine(" • " + f);
                }
                if (mod.DuplicateCount > 0)
                {
                    body.AppendLine(); body.AppendLine(T("重複インストール場所:", "Duplicate installation locations:"));
                    body.AppendLine(T("現在: ", "Current: ") + mod.SourceName + " | " + mod.Folder);
                    foreach (string d in mod.DuplicateLocations) body.AppendLine(" • " + d);
                }
                information.Text = body.ToString();
                detail.Controls.Add(information);

                Button folder = MakeButton(T("MODフォルダーを開く", "Open MOD Folder"), 18, 590, 180, 34);
                folder.Click += delegate { OpenFolder(mod.Folder); };
                detail.Controls.Add(folder);
                if (mod.IsWorkshop)
                {
                    Button steam = MakeButton(T("Workshopページ", "Workshop Page"), 212, 590, 170, 34);
                    steam.Click += delegate { try { Process.Start("steam://url/CommunityFilePage/" + mod.Id); } catch { } };
                    detail.Controls.Add(steam);
                }
                string ini = FindManagedIni(mod);
                if (ini != null)
                {
                    Button iniButton = MakeButton(T("INI個別設定", "INI Settings"), 396, 590, 150, 34);
                    iniButton.Click += delegate { detail.Close(); OpenIniEditor(mod); };
                    detail.Controls.Add(iniButton);
                }
                Button close = MakeButton(T("閉じる", "Close"), 732, 590, 130, 34);
                close.DialogResult = DialogResult.OK;
                detail.Controls.Add(close);
                detail.ShowDialog(this);
                if (image.Image != null) image.Image.Dispose();
            }
        }

        private void RunSelectedModTool(ListView list)
        {
            if (list == null || list.SelectedItems.Count == 0) return;
            ModInfo mod = list.SelectedItems[0].Tag as ModInfo;
            if (mod == null || mod.IncludedTools == null || mod.IncludedTools.Count == 0) return;
            if (mod.IncludedTools.Count == 1)
            {
                ExecuteModTool(mod, mod.IncludedTools[0]);
                return;
            }
            using (Form picker = new Form())
            {
                picker.Text = T("付属ツールを選択", "Select Included Tool");
                picker.StartPosition = FormStartPosition.CenterParent;
                picker.FormBorderStyle = FormBorderStyle.Sizable;
                picker.MinimumSize = new Size(620, 240);
                picker.Size = new Size(650, Math.Min(560, 150 + mod.IncludedTools.Count * 45));
                picker.BackColor = Dw2Deep;
                picker.ForeColor = Dw2Text;

                Label note = new Label();
                note.Dock = DockStyle.Top;
                note.Height = 55;
                note.Padding = new Padding(14, 12, 14, 4);
                note.ForeColor = Dw2Gold;
                note.Text = T("実行する付属ツールを選択してください。各ボタンを押すと最終確認が表示されます。", "Select a tool to run. A final confirmation appears after clicking each button.");
                picker.Controls.Add(note);

                FlowLayoutPanel buttons = new FlowLayoutPanel();
                buttons.Dock = DockStyle.Fill;
                buttons.FlowDirection = FlowDirection.TopDown;
                buttons.WrapContents = false;
                buttons.AutoScroll = true;
                buttons.Padding = new Padding(12, 8, 12, 8);
                picker.Controls.Add(buttons);
                buttons.BringToFront();
                foreach (string tool in mod.IncludedTools)
                {
                    string toolPath = tool;
                    Button run = MakeButton(toolPath, 0, 0, 585, 36);
                    run.Margin = new Padding(3, 3, 3, 6);
                    run.TextAlign = ContentAlignment.MiddleLeft;
                    run.Click += delegate { ExecuteModTool(mod, toolPath); };
                    buttons.Controls.Add(run);
                }
                picker.ShowDialog(this);
            }
        }

        private void ExecuteModTool(ModInfo mod, string selected)
        {
            string root = !string.IsNullOrWhiteSpace(mod.ContentRoot) ? mod.ContentRoot : mod.Folder;
            if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(selected)) return;
            string fullPath = Path.GetFullPath(Path.Combine(root, selected));
            string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
            {
                MessageBox.Show(T("付属ツールが見つからないか、MODフォルダー外を参照しています。", "The tool is missing or points outside the MOD folder."), Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            string warning = T(
                "外部プログラムを実行します。信頼できるMODに付属するファイルだけ実行してください。\r\n\r\nファイル: ",
                "This will run an external program. Only run files supplied by a MOD you trust.\r\n\r\nFile: ") + fullPath;
            if (MessageBox.Show(warning, T("付属ツールの実行確認", "Confirm Tool Execution"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            try
            {
                ProcessStartInfo start = new ProcessStartInfo();
                start.FileName = fullPath;
                start.WorkingDirectory = Path.GetDirectoryName(fullPath);
                start.UseShellExecute = true;
                Process.Start(start);
            }
            catch (Exception ex)
            {
                Program.LogException("Run included tool", ex);
                MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OpenSelectedModDocument(ListView list)
        {
            if (list == null || list.SelectedItems.Count == 0) return;
            ModInfo mod = list.SelectedItems[0].Tag as ModInfo;
            if (mod == null || mod.IncludedDocuments == null || mod.IncludedDocuments.Count == 0) return;
            if (mod.IncludedDocuments.Count == 1)
            {
                OpenModDocument(mod, mod.IncludedDocuments[0]);
                return;
            }
            using (Form picker = new Form())
            {
                picker.Text = T("付属文書を選択", "Select Included Document");
                picker.StartPosition = FormStartPosition.CenterParent;
                picker.MinimumSize = new Size(620, 240);
                picker.Size = new Size(650, Math.Min(560, 150 + mod.IncludedDocuments.Count * 45));
                picker.BackColor = Dw2Deep;
                picker.ForeColor = Dw2Text;
                Label note = new Label();
                note.Dock = DockStyle.Top;
                note.Height = 55;
                note.Padding = new Padding(14, 12, 14, 4);
                note.ForeColor = Dw2Gold;
                note.Text = T("開くREADME／マニュアルを選択してください。", "Select a README or manual to open.");
                picker.Controls.Add(note);
                FlowLayoutPanel buttons = new FlowLayoutPanel();
                buttons.Dock = DockStyle.Fill;
                buttons.FlowDirection = FlowDirection.TopDown;
                buttons.WrapContents = false;
                buttons.AutoScroll = true;
                buttons.Padding = new Padding(12, 8, 12, 8);
                picker.Controls.Add(buttons);
                buttons.BringToFront();
                foreach (string document in mod.IncludedDocuments)
                {
                    string documentPath = document;
                    Button open = MakeButton(documentPath, 0, 0, 585, 36);
                    open.Margin = new Padding(3, 3, 3, 6);
                    open.TextAlign = ContentAlignment.MiddleLeft;
                    open.Click += delegate { OpenModDocument(mod, documentPath); };
                    buttons.Controls.Add(open);
                }
                picker.ShowDialog(this);
            }
        }

        private void OpenModDocument(ModInfo mod, string selected)
        {
            try
            {
                string root = !string.IsNullOrWhiteSpace(mod.ContentRoot) ? mod.ContentRoot : mod.Folder;
                if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(selected)) return;
                string fullPath = Path.GetFullPath(Path.Combine(root, selected));
                string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
                {
                    MessageBox.Show(T("付属文書が見つからないか、MODフォルダー外を参照しています。", "The document is missing or points outside the MOD folder."), Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                ProcessStartInfo start = new ProcessStartInfo();
                start.FileName = fullPath;
                start.WorkingDirectory = Path.GetDirectoryName(fullPath);
                start.UseShellExecute = true;
                Process.Start(start);
            }
            catch (Exception ex)
            {
                Program.LogException("Open included document", ex);
                MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                Program.LogException("Open individual INI editor", ex);
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

        private List<IniEditorRow> ReadIniEditorRows(string path)
        {
            List<IniEditorRow> result = new List<IniEditorRow>();
            List<string> comments = new List<string>();
            foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
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

        private Dictionary<string, string> ReadIni(string path)
        {
            Dictionary<string, string> d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return d;
            try
            {
                foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    d[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }
            }
            catch { }
            return d;
        }

        private bool IniBool(Dictionary<string, string> d, string key, bool fallback)
        {
            string v;
            if (!d.TryGetValue(key, out v)) return fallback;
            return v.Equals("true", StringComparison.OrdinalIgnoreCase) || v == "1" || v.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

        private string IniValue(Dictionary<string, string> d, string key, string fallback)
        {
            string v;
            return d.TryGetValue(key, out v) ? v : fallback;
        }

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
                List<string> lines = File.Exists(path) ? File.ReadAllLines(path, Encoding.UTF8).ToList() : new List<string>();
                HashSet<string> done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < lines.Count; i++)
                {
                    string trimmed = lines[i].Trim();
                    if (trimmed.StartsWith("#") || trimmed.StartsWith(";")) continue;
                    int eq = trimmed.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = trimmed.Substring(0, eq).Trim();
                    string value;
                    if (values.TryGetValue(key, out value))
                    {
                        lines[i] = key + "=" + value;
                        done.Add(key);
                    }
                }
                foreach (KeyValuePair<string, string> kv in values)
                    if (!done.Contains(kv.Key)) lines.Add(kv.Key + "=" + kv.Value);
                File.WriteAllLines(path, lines.ToArray(), new UTF8Encoding(true));
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
                return json.Deserialize<LauncherMeta>(File.ReadAllText(path, Encoding.UTF8));
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

        private string BuildLaunchArguments()
        {
            EnsureSettingsState();
            List<string> args = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<ModInfo> launchMods = (currentManagedMods ?? new List<ModInfo>())
                .Concat(currentWorkshopMods ?? new List<ModInfo>()).Where(IsModSelected).ToList();
            foreach (ModInfo mod in launchMods.OrderBy(m =>
            {
                int index = currentModOrder == null ? -1 : currentModOrder.FindIndex(x => x.Equals(m.ActiveToken, StringComparison.OrdinalIgnoreCase));
                return index < 0 ? int.MaxValue : index;
            }))
            {
                if (!string.IsNullOrWhiteSpace(mod.ModJsonLaunchArguments) && seen.Add(mod.ModJsonLaunchArguments.Trim()))
                    args.Add(mod.ModJsonLaunchArguments.Trim());
                LauncherMeta meta = ReadLauncherMeta(mod);
                if (meta != null && !string.IsNullOrWhiteSpace(meta.launchArguments) && seen.Add(meta.launchArguments.Trim()))
                    args.Add(meta.launchArguments.Trim());
            }
            string global = launchArgsBox == null ? settings.GlobalLaunchArguments : launchArgsBox.Text.Trim();
            if (!string.IsNullOrWhiteSpace(global) && seen.Add(global)) args.Add(global);
            return string.Join(" ", args.Where(a => !string.IsNullOrWhiteSpace(a)).ToArray()).Trim();
        }

        private void UpdateCommandPreview()
        {
            if (commandPreviewBox == null) return;
            string exe = string.IsNullOrEmpty(settings.GameRoot) ? "DistantWorlds2.exe" : Path.Combine(settings.GameRoot, "DistantWorlds2.exe");
            commandPreviewBox.Text = "\"" + exe + "\"" + (string.IsNullOrWhiteSpace(BuildLaunchArguments()) ? "" : " " + BuildLaunchArguments());
        }

        private void LaunchGame()
        {
            SaveSettingsFromUi();
            if (FindAiIni() != null) SaveAiSettings();
            AnalyzeConflicts();
            RefreshModStatusColumns();
            List<string> diagnostics = BuildLaunchDiagnostics();
            if (diagnostics.Count > 0)
            {
                DialogResult diagnosticAnswer = MessageBox.Show(
                    T("起動前診断で問題が見つかりました。\r\n\r\n", "Pre-launch diagnostics found issues.\r\n\r\n") +
                    string.Join("\r\n", diagnostics.Take(30).ToArray()) +
                    T("\r\n\r\nこのまま起動しますか？", "\r\n\r\nLaunch anyway?"),
                    T("起動前診断", "Pre-launch diagnostics"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (diagnosticAnswer != DialogResult.Yes) return;
            }
            if (currentCollisions.Count > 0)
            {
                string warning = BuildConflictWarning();
                DialogResult answer = MessageBox.Show(warning, T("MOD競合の警告", "MOD Conflict Warning"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (answer != DialogResult.Yes) return;
            }
            string exe = Path.Combine(settings.GameRoot ?? "", "DistantWorlds2.exe");
            if (!File.Exists(exe))
            {
                MessageBox.Show(T("DistantWorlds2.exe が見つかりません。設定タブでゲームフォルダーを指定してください。", "DistantWorlds2.exe was not found. Set the game folder in Settings."), Text);
                return;
            }
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = exe;
                psi.WorkingDirectory = settings.GameRoot;
                psi.Arguments = BuildLaunchArguments();
                psi.UseShellExecute = true;
                Process.Start(psi);
                SetStatus(T("Distant Worlds 2 を起動しました。", "Distant Worlds 2 launched."));
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, Text);
            }
        }

        private List<string> BuildLaunchDiagnostics()
        {
            List<string> issues = new List<string>();
            List<ModInfo> enabled = (currentManagedMods ?? new List<ModInfo>()).Concat(currentWorkshopMods ?? new List<ModInfo>()).Where(IsModSelected).ToList();
            Func<ModInfo, string, bool> matches = delegate(ModInfo m, string identity)
            {
                if (m == null || string.IsNullOrWhiteSpace(identity)) return false;
                string folder = Path.GetFileName(m.Folder ?? "");
                return new string[] { m.Id, m.DisplayName, m.ActiveToken, folder }.Any(x => string.Equals(x, identity, StringComparison.OrdinalIgnoreCase));
            };
            foreach (ModInfo mod in enabled)
            {
                foreach (string required in mod.RequiredMods ?? new List<string>())
                    if (!enabled.Any(m => matches(m, required))) issues.Add("⚠ " + (mod.DisplayName ?? mod.Id) + ": " + T("必須MODがありません: ", "Missing required MOD: ") + required);
                foreach (string incompatible in mod.IncompatibleMods ?? new List<string>())
                    if (enabled.Any(m => m != mod && matches(m, incompatible))) issues.Add("⚠ " + (mod.DisplayName ?? mod.Id) + ": " + T("同時使用不可: ", "Incompatible MOD enabled: ") + incompatible);
                int ownIndex = currentModOrder == null ? -1 : currentModOrder.FindIndex(x => string.Equals(x, mod.ActiveToken, StringComparison.OrdinalIgnoreCase));
                foreach (string before in mod.LoadBefore ?? new List<string>())
                {
                    ModInfo target = enabled.FirstOrDefault(m => matches(m, before));
                    int targetIndex = target == null || currentModOrder == null ? -1 : currentModOrder.FindIndex(x => string.Equals(x, target.ActiveToken, StringComparison.OrdinalIgnoreCase));
                    if (target != null && ownIndex >= 0 && targetIndex >= 0 && ownIndex > targetIndex)
                        issues.Add("⚠ " + (mod.DisplayName ?? mod.Id) + T(" は次のMODより先にロードする必要があります: ", " must load before: ") + before);
                }
                foreach (string after in mod.LoadAfter ?? new List<string>())
                {
                    ModInfo target = enabled.FirstOrDefault(m => matches(m, after));
                    int targetIndex = target == null || currentModOrder == null ? -1 : currentModOrder.FindIndex(x => string.Equals(x, target.ActiveToken, StringComparison.OrdinalIgnoreCase));
                    if (target != null && ownIndex >= 0 && targetIndex >= 0 && ownIndex < targetIndex)
                        issues.Add("⚠ " + (mod.DisplayName ?? mod.Id) + T(" は次のMODより後にロードする必要があります: ", " must load after: ") + after);
                }
                ValidateJsonFile(mod.ModJsonPath, issues);
                ValidateJsonFile(Path.Combine(mod.Folder ?? "", "launcher.json"), issues);
                try
                {
                    foreach (string xml in Directory.GetFiles(mod.ContentRoot ?? mod.Folder, "*.xml", SearchOption.AllDirectories))
                    {
                        try { XmlDocument document = new XmlDocument(); document.Load(xml); }
                        catch (Exception ex) { issues.Add("⚠ " + T("XML破損: ", "Invalid XML: ") + xml + " (" + ex.Message + ")"); }
                    }
                }
                catch { }
            }
            Dictionary<string, List<string>> dlls = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (ModInfo mod in enabled)
            {
                try
                {
                    foreach (string dll in Directory.GetFiles(mod.ContentRoot ?? mod.Folder, "*.dll", SearchOption.AllDirectories))
                    {
                        List<string> owners;
                        if (!dlls.TryGetValue(Path.GetFileName(dll), out owners)) { owners = new List<string>(); dlls[Path.GetFileName(dll)] = owners; }
                        owners.Add(mod.DisplayName ?? mod.Id);
                    }
                }
                catch { }
            }
            foreach (KeyValuePair<string, List<string>> pair in dlls.Where(x => x.Value.Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1))
                issues.Add("⚠ " + T("重複DLL: ", "Duplicate DLL: ") + pair.Key + " — " + string.Join(" / ", pair.Value.Distinct().ToArray()));
            foreach (Match match in Regex.Matches(BuildLaunchArguments(), "--low-level-inject\\s+(?:\\\"([^\\\"]+)\\\"|([^\\s!]+))!", RegexOptions.IgnoreCase))
            {
                string dll = !string.IsNullOrWhiteSpace(match.Groups[1].Value) ? match.Groups[1].Value : match.Groups[2].Value;
                string full = Path.IsPathRooted(dll) ? dll : Path.Combine(settings.GameRoot ?? "", dll.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(full)) issues.Add("⚠ " + T("起動引数のDLLがありません: ", "Launch argument DLL not found: ") + dll);
            }
            return issues.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private void ValidateJsonFile(string path, List<string> issues)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
            try { json.DeserializeObject(File.ReadAllText(path, Encoding.UTF8)); }
            catch (Exception ex) { issues.Add("⚠ " + T("JSON破損: ", "Invalid JSON: ") + path + " (" + ex.Message + ")"); }
        }

        private void UpdatePathLabels()
        {
            if (gamePathLabel != null) gamePathLabel.Text = "Game: " + (string.IsNullOrEmpty(settings.GameRoot) ? "Not found" : settings.GameRoot);
            if (workshopPathLabel != null) workshopPathLabel.Text = "Workshop: " + (string.IsNullOrEmpty(settings.WorkshopRoot) ? "Not found" : settings.WorkshopRoot);
        }

        private void OpenFolder(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
                {
                    MessageBox.Show(T("フォルダーが見つかりません。", "Folder not found."), Text);
                    return;
                }
                Process.Start("explorer.exe", "\"" + path + "\"");
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, Text); }
        }

        private void OpenSelectedModFolder(ListView list)
        {
            if (list == null || list.SelectedItems.Count == 0)
            {
                MessageBox.Show(T("一覧からMODを選択してください。", "Select a MOD from the list."), Text);
                return;
            }
            ModInfo mod = list.SelectedItems[0].Tag as ModInfo;
            if (mod == null || string.IsNullOrWhiteSpace(mod.Folder) || !Directory.Exists(mod.Folder))
            {
                MessageBox.Show(T("選択したMODのフォルダーが見つかりません。", "The selected MOD folder was not found."), Text);
                return;
            }
            OpenFolder(mod.Folder);
        }

        private void BrowseFolderInto(TextBox box)
        {
            using (FolderBrowserDialog d = new FolderBrowserDialog())
            {
                d.SelectedPath = Directory.Exists(box.Text) ? box.Text : appRoot;
                if (d.ShowDialog(this) == DialogResult.OK) box.Text = d.SelectedPath;
            }
        }

        private void SetStatus(string text)
        {
            if (statusLabel != null) statusLabel.Text = text;
        }

        private string T(string ja, string en)
        {
            return settings != null && settings.Language == "en" ? en : ja;
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
