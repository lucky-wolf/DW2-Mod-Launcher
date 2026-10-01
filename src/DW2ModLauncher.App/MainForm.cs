using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DW2ModLauncher.Core.Diagnostics;
using DW2ModLauncher.Core.Models;

namespace DW2ModLauncherBeta
{
    public partial class MainForm : Form
    {
        // The mod list has 5 columns: MOD Name, Source, MOD State (checkbox),
        // Health (collapsed conflict/duplicate/update status) and Load Order.
        // Everything else that used to be its own column now lives in the
        // details panel only.
        private const int ColumnModState = 2;
        private const int ColumnHealth = 3;
        private const int ColumnLoadOrder = 4;

        private sealed class ModListComparer : IComparer
        {
            private readonly int column;
            private readonly bool ascending;
            private readonly Func<ModInfo, bool> isEnabled;
            private readonly Func<ModInfo, int> healthSeverity;

            public ModListComparer(int column, bool ascending, Func<ModInfo, bool> isEnabled, Func<ModInfo, int> healthSeverity)
            {
                this.column = column;
                this.ascending = ascending;
                this.isEnabled = isEnabled;
                this.healthSeverity = healthSeverity;
            }

            public int Compare(object x, object y)
            {
                ListViewItem leftItem = x as ListViewItem;
                ListViewItem rightItem = y as ListViewItem;
                ModInfo left = leftItem == null ? null : leftItem.Tag as ModInfo;
                ModInfo right = rightItem == null ? null : rightItem.Tag as ModInfo;
                int result;
                if (column == ColumnModState)
                    result = CompareInt(left == null || !isEnabled(left) ? 0 : 1, right == null || !isEnabled(right) ? 0 : 1);
                else if (column == ColumnHealth)
                    result = CompareInt(left == null ? 0 : healthSeverity(left), right == null ? 0 : healthSeverity(right));
                else if (column == ColumnLoadOrder)
                {
                    int leftOrder;
                    int rightOrder;
                    if (!int.TryParse(leftItem == null || leftItem.SubItems.Count <= ColumnLoadOrder ? "" : leftItem.SubItems[ColumnLoadOrder].Text, out leftOrder)) leftOrder = int.MaxValue;
                    if (!int.TryParse(rightItem == null || rightItem.SubItems.Count <= ColumnLoadOrder ? "" : rightItem.SubItems[ColumnLoadOrder].Text, out rightOrder)) rightOrder = int.MaxValue;
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
        private bool publishRunning;
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
        private TabPage modsTab;
        private TabPage settingsTab;

        private ListView modList;
        private readonly Dictionary<ListView, int> listSortColumns = new Dictionary<ListView, int>();
        private readonly Dictionary<ListView, bool> listSortAscending = new Dictionary<ListView, bool>();
        private ImageList modImages;
        private PictureBox modPreview;
        private Label modName;
        private Panel modProblemsPanel;
        private Label modProblemsLabel;
        private TextBox modDesc;
        private bool fittingModListColumns;

        private TextBox gameRootBox;
        private TextBox workshopRootBox;
        private TextBox managedRootBox;
        private TextBox launchArgsBox;
        private ComboBox profileCombo;
        private TextBox commandPreviewBox;

        private Button modRootButton;
        private Button iniButton;
        private Button workshopRootButton;
        private Button gameOpenButton;
        private Button detectButton;
        private Button saveSettingsButton;
        private Button workshopUpdateButton;
        private Button publishButton;
        private Button selectedFolderButton;
        private Button modsNavigationButton;
        private Button settingsNavigationButton;

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
            AutoScaleMode = AutoScaleMode.None;
            MinimumSize = new Size(1000, 650);
            Rectangle workArea = Screen.PrimaryScreen == null ? new Rectangle(0, 0, 1500, 900) : Screen.PrimaryScreen.WorkingArea;
            Size = new Size(Math.Max(1000, Math.Min(1500, workArea.Width - 40)), Math.Max(650, Math.Min(860, workArea.Height - 60)));
            BackColor = Dw2Deep;
            ForeColor = Dw2Text;
            Font = new Font("Segoe UI", 9F);

            // The whole UI below is laid out with pixel coordinates authored for a
            // 96 DPI screen. Windows Forms does not auto-scale a manually built
            // control tree like this, so on a scaled-DPI monitor the (now DPI-aware,
            // crisp) text renders larger than the hand-placed control bounds expect
            // and gets clipped. Scale the built tree - and grow the window to match -
            // by the real DPI ratio so the layout keeps its proportions.
            float dpiScale = DeviceDpi / 96f;

            BuildUi();

            if (dpiScale > 1.01f)
            {
                SuspendLayout();
                foreach (Control child in Controls) child.Scale(new SizeF(dpiScale, dpiScale));
                MinimumSize = new Size((int)Math.Round(MinimumSize.Width * dpiScale), (int)Math.Round(MinimumSize.Height * dpiScale));
                Size = new Size((int)Math.Round(Size.Width * dpiScale), (int)Math.Round(Size.Height * dpiScale));
                ResumeLayout(true);
            }

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
            if (string.IsNullOrWhiteSpace(settings.Language)) settings.Language = "en";
            if (!Localization.AvailableLanguageCodes().Contains(settings.Language)) settings.Language = "en";
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
                SetStatus("Skipped a failed step: " + name + " - " + ex.Message);
            }
        }
    }
}
