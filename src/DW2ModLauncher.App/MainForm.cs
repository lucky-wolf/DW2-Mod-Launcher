using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using DW2ModLauncher.Core.Diagnostics;
using DW2ModLauncher.Core.Models;

namespace DW2ModLauncherBeta
{
    public partial class MainForm : Form
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
                Logger.LogException(name, ex);
                SetStatus(T("一部処理をスキップしました: ", "Skipped a failed step: ") + name + " - " + ex.Message);
            }
        }
    }
}
