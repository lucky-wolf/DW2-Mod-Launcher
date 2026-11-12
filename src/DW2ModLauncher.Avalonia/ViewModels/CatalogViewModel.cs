using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using DW2ModLauncher.Core;
using DW2ModLauncher.Core.Diagnostics;
using DW2ModLauncher.Core.Services.Updates;
using DW2ModLauncher.Core.Models;
using DW2ModLauncher.Core.Services;
using DW2ModLauncher.Core.Services.Catalog;
using DW2ModLauncher.Core.Services.Publishing;

namespace DW2ModLauncher.Avalonia.ViewModels
{
    /// <summary>One line of the Browse window: a catalog entry and how it stands against what is installed.</summary>
    public class CatalogRowViewModel : ViewModelBase
    {
        private readonly string previewFolder;
        private Bitmap thumbnail;
        private bool thumbnailRequested;

        private string progressText = "";

        public CatalogRowViewModel(CatalogEntry entry, CatalogStatus status, Func<string, (string Name, object Value)[], string> text, string previewFolder, long? subscribers, Func<CatalogRowViewModel, Task> install)
        {
            this.previewFolder = previewFolder;
            Entry = entry;
            Status = status;
            // Download for a mod that is not installed, Update for an older local copy, nothing otherwise (current, or kept up to date by Steam).
            ActionText = status.State == CatalogInstallState.NotInstalled ? text("CatalogDownload", new (string, object)[0])
                : status.State == CatalogInstallState.UpdateAvailable ? text("CatalogUpdate", new (string, object)[] { ("Version", entry.Version) })
                : "";
            ActionTooltip = status.State == CatalogInstallState.NotInstalled ? text("CatalogDownloadTooltip", new (string, object)[0])
                : status.State == CatalogInstallState.UpdateAvailable ? text("CatalogUpdateTooltip", new (string, object)[0])
                : "";
            InstallCommand = new RelayCommand(() => install(this), () => HasAction);
            KindText = string.Join(text("ListSeparator", new (string, object)[0]), entry.Kinds.Select(k => text("CatalogKind_" + k, new (string, object)[0])));
            StateText = status.State switch
            {
                CatalogInstallState.UpToDate => text("CatalogStateUpToDate", new (string, object)[0]),
                CatalogInstallState.UpdateAvailable => text("CatalogStateUpdate", new (string, object)[] { ("Installed", status.Installed.Version) }),
                CatalogInstallState.FromSteam => text("CatalogStateFromSteam", new (string, object)[0]),
                _ => ""
            };
            SizeText = text("CatalogSizeLabel", new (string, object)[] { ("Size", FormatSize(entry.Size)) });
            // Steam's subscriber count when the launcher could ask Steam, else the catalog's total downloads of all releases. Neither known is shown as nothing, never as 0.
            PopularityText = subscribers != null ? text("CatalogSubscribersLabel", new (string, object)[] { ("Count", subscribers.Value.ToString("N0", CultureInfo.CurrentCulture)) })
                : entry.Downloads != null ? text("CatalogDownloadsLabel", new (string, object)[] { ("Count", entry.Downloads.Value.ToString("N0", CultureInfo.CurrentCulture)) })
                : "";
            PublishedText = text("CatalogPublishedLabel", new (string, object)[] { ("Date", entry.PublishedUtc.ToLocalTime().ToString("d", CultureInfo.CurrentCulture)) });
            TagsText = entry.Tags.Count == 0 ? "" : text("CatalogTagsLabel", new (string, object)[] { ("Tags", string.Join(text("ListSeparator", new (string, object)[0]), entry.Tags)) });
            AuthorText = entry.Author == null ? "" : text("CatalogAuthorLabel", new (string, object)[] { ("Author", entry.Author) });
        }

        public CatalogEntry Entry { get; }
        public CatalogStatus Status { get; }
        public string Name { get { return Entry.Name; } }
        public string Version { get { return Entry.Version; } }
        public string Description { get { return Entry.ShortDescription ?? ""; } }
        public string KindText { get; }
        public string StateText { get; }
        public string SizeText { get; }
        public string PopularityText { get; }
        public string PublishedText { get; }
        public string AuthorText { get; }
        public string TagsText { get; }
        public bool HasTags { get { return TagsText.Length > 0; } }
        public bool HasAuthor { get { return AuthorText.Length > 0; } }
        public bool HasState { get { return StateText.Length > 0; } }
        public bool HasPopularity { get { return PopularityText.Length > 0; } }
        public bool HasRelease { get { return Entry.ReleaseUrl != null; } }
        public string ActionText { get; }
        public string ActionTooltip { get; }
        public bool HasAction { get { return ActionText.Length > 0; } }
        public RelayCommand InstallCommand { get; }
        /// <summary>What an install in progress is doing ("Downloading... 40%"); empty otherwise.</summary>
        public string ProgressText { get { return progressText; } set { if (Set(ref progressText, value)) Raise(nameof(HasProgress)); } }
        public bool HasProgress { get { return progressText.Length > 0; } }

        /// <summary>The release's preview image, downloaded (once, then cached) the first time the row is shown. Null while loading, and when the release has none or it could not be fetched (logged); no other image is substituted.</summary>
        public Bitmap Thumbnail
        {
            get
            {
                if (!thumbnailRequested)
                {
                    thumbnailRequested = true;
                    var _ = LoadThumbnailAsync();
                }
                return thumbnail;
            }
        }

        private async Task LoadThumbnailAsync()
        {
            string url = Entry.PreviewUrl;
            if (url == null) return;
            try
            {
                string path = await CatalogPreviewCache.GetAsync(url, previewFolder, CancellationToken.None);
                thumbnail = ImageLoader.Load(path, 480);
                Raise(nameof(Thumbnail));
            }
            catch (Exception ex)
            {
                Logger.LogException("Load catalog preview " + url, ex);
            }
        }

        public static string FormatSize(long bytes)
        {
            if (bytes < 1024 * 1024) return Math.Max(1, (bytes + 1023) / 1024).ToString("N0", CultureInfo.CurrentCulture) + " KB";
            return (bytes / (1024.0 * 1024.0)).ToString("N1", CultureInfo.CurrentCulture) + " MB";
        }
    }

    /// <summary>A choice in one of the Browse window's drop-downs.</summary>
    public class CatalogChoice
    {
        public string Text { get; set; }
        /// <summary>A catalog kind for the kind filter (null = all), or the <see cref="CatalogSort"/> for the sort.</summary>
        public object Value { get; set; }
        public override string ToString() { return Text; }
    }

    /// <summary>The Browse window: fetches the catalog once when opened, then filters and sorts the entries it holds. Read-only for now.</summary>
    public class CatalogViewModel : ViewModelBase
    {
        private readonly MainViewModel main;
        private CatalogIndex index;
        private string filterText = "";
        private CatalogChoice kind;
        private CatalogChoice sort;
        private CatalogChoice tag;
        private List<CatalogChoice> sortChoices;
        private readonly CatalogChoice subscribersChoice;
        private readonly CatalogChoice downloadsChoice;
        private bool sortPickedByUser;
        private List<CatalogChoice> tagChoices;
        private CatalogRowViewModel selected;
        private bool loading;
        private string summary = "";
        private string error = "";
        // Steam subscriber counts by Workshop id, filled when Steam answers; empty when it can't be asked.
        private Dictionary<long, long> subscribers = new Dictionary<long, long>();

        public CatalogViewModel(MainViewModel main)
        {
            this.main = main;
            KindChoices = new List<CatalogChoice> { new CatalogChoice { Text = main.T("CatalogKindAll"), Value = null } };
            foreach (string k in new[] { "code", "xml", "translation", "font" })
                KindChoices.Add(new CatalogChoice { Text = main.T("CatalogKind_" + k), Value = k });
            subscribersChoice = new CatalogChoice { Text = main.T("CatalogSortSubscribers"), Value = CatalogSort.MostSubscribers };
            downloadsChoice = new CatalogChoice { Text = main.T("CatalogSortDownloads"), Value = CatalogSort.MostDownloads };
            // "Most subscribers" is only offered once Steam has answered (see LoadSubscribersAsync); without Steam the catalog can only count downloads.
            sortChoices = new List<CatalogChoice>
            {
                downloadsChoice,
                new CatalogChoice { Text = main.T("CatalogSortRecent"), Value = CatalogSort.RecentlyReleased },
                new CatalogChoice { Text = main.T("CatalogSortName"), Value = CatalogSort.Name }
            };
            tagChoices = new List<CatalogChoice> { new CatalogChoice { Text = main.T("CatalogTagAll"), Value = null } };
            tag = tagChoices[0];
            kind = KindChoices[0];
            sort = sortChoices[0];
            OpenReleaseCommand = new RelayCommand(OpenRelease, () => selected != null && selected.HasRelease);
            ReloadCommand = new RelayCommand(LoadAsync, () => !loading);
        }

        public LocalizedStrings L { get { return main.L; } }
        public string Url { get; set; } = CatalogClient.DefaultUrl;
        public List<CatalogChoice> KindChoices { get; }
        public List<CatalogChoice> SortChoices { get { return sortChoices; } private set { Set(ref sortChoices, value); } }
        /// <summary>"All tags" and then every tag some mod in the loaded catalog has.</summary>
        public List<CatalogChoice> TagChoices { get { return tagChoices; } private set { Set(ref tagChoices, value); } }
        public ObservableCollection<CatalogRowViewModel> Rows { get; } = new ObservableCollection<CatalogRowViewModel>();
        public RelayCommand OpenReleaseCommand { get; }
        public RelayCommand ReloadCommand { get; }

        public string FilterText { get { return filterText; } set { if (Set(ref filterText, value ?? "")) Rebuild(); } }
        public CatalogChoice Kind { get { return kind; } set { if (value != null && Set(ref kind, value)) Rebuild(); } }
        public CatalogChoice Tag { get { return tag; } set { if (value != null && Set(ref tag, value)) Rebuild(); } }
        public CatalogChoice Sort
        {
            get { return sort; }
            set
            {
                // null arrives while the drop-down's list is being replaced; it is not a choice.
                if (value == null) return;
                sortPickedByUser = true;
                if (Set(ref sort, value)) Rebuild();
            }
        }

        public CatalogRowViewModel Selected
        {
            get { return selected; }
            set
            {
                if (!Set(ref selected, value)) return;
                OpenReleaseCommand.RaiseCanExecuteChanged();
            }
        }

        public bool IsLoading { get { return loading; } private set { if (Set(ref loading, value)) ReloadCommand.RaiseCanExecuteChanged(); } }
        /// <summary>How many mods are listed, shown under the list.</summary>
        public string Summary { get { return summary; } private set { Set(ref summary, value); } }
        /// <summary>Why the catalog could not be shown; empty when it loaded.</summary>
        public string Error { get { return error; } private set { if (Set(ref error, value)) Raise(nameof(HasError)); } }
        public bool HasError { get { return error.Length > 0; } }

        public async Task LoadAsync()
        {
            IsLoading = true;
            Error = "";
            try
            {
                index = await CatalogClient.FetchAsync(Url, CancellationToken.None);
            }
            catch (Exception ex)
            {
                // Network, an unreadable file and a bad entry all end up here; the parser's message names the entry and field.
                Logger.LogException("Load mod catalog " + Url, ex);
                index = null;
                Error = main.T("CatalogLoadFailed", ("Url", Url), ("Problem", ex.Message));
            }
            finally { IsLoading = false; }
            OfferTags();
            Rebuild();
            if (index != null) { var _ = LoadSubscribersAsync(index); }
        }

        /// <summary>
        /// Asks Steam, through the same batched item query the launcher already makes, how many players subscribe to each catalog mod that
        /// has a Workshop item, and offers (and, unless the player already chose an order, uses) the Most subscribers order when it answers.
        /// Without Steam (not running, not logged in, no account) nothing changes: the list stays ordered by the catalog's own download counts.
        /// </summary>
        private async Task LoadSubscribersAsync(CatalogIndex loaded)
        {
            List<long> ids = loaded.Entries.Where(e => e.WorkshopId != null).Select(e => long.Parse(e.WorkshopId, CultureInfo.InvariantCulture)).Distinct().ToList();
            if (ids.Count == 0) return;
            List<SteamItemStatus> items;
            try
            {
                IModPublisher publisher = ModPublisherFactory.Create(uint.Parse(SteamLocator.AppId));
                items = await Task.Run(() => publisher.QueryItems(ids));
            }
            catch (Exception ex)
            {
                Logger.LogException("Ask Steam for catalog subscriber counts", ex);
                return;
            }
            if (index != loaded) return; // the catalog was reloaded meanwhile
            Dictionary<long, long> found = items.Where(i => i.Subscribers.HasValue).ToDictionary(i => i.Id, i => i.Subscribers.Value);
            if (found.Count == 0)
            {
                Logger.Log("Mod catalog", "Steam gave no subscriber counts; showing the catalog's download counts.");
                return;
            }
            subscribers = found;
            // Offer the subscriber sort, and make it the order unless the player already picked one.
            if (!sortPickedByUser) sort = subscribersChoice;
            SortChoices = new[] { subscribersChoice }.Concat(sortChoices).ToList();
            Raise(nameof(Sort));
            Rebuild();
        }

        private long? SubscribersOf(CatalogEntry entry)
        {
            return entry.WorkshopId != null && subscribers.TryGetValue(long.Parse(entry.WorkshopId, CultureInfo.InvariantCulture), out long count) ? count : (long?)null;
        }

        // The tag filter lists the tags the catalog has; one that is no longer there (after a reload) goes back to "All tags".
        private void OfferTags()
        {
            string keep = (string)tag.Value;
            List<CatalogChoice> choices = new List<CatalogChoice> { new CatalogChoice { Text = main.T("CatalogTagAll"), Value = null } };
            if (index != null)
                foreach (string name in CatalogBrowse.AllTags(index.Entries))
                    choices.Add(new CatalogChoice { Text = name, Value = name });
            TagChoices = choices;
            main.KnownCatalogTags = choices.Skip(1).Select(c => (string)c.Value).ToList();
            tag = choices.FirstOrDefault(c => (string)c.Value == keep) ?? choices[0];
            Raise(nameof(Tag));
        }

        private void Rebuild()
        {
            string keep = selected?.Entry.Id;
            Rows.Clear();
            if (index == null)
            {
                Summary = "";
                Selected = null;
                return;
            }
            List<ModInfo> installed = main.AllMods.ToList();
            string previewFolder = System.IO.Path.Combine(UserDataRoot.Get(), "Catalog", "Previews");
            foreach (CatalogEntry entry in CatalogBrowse.Apply(index.Entries, filterText, (string)kind.Value, (CatalogSort)sort.Value, SubscribersOf, (string)tag.Value))
                Rows.Add(new CatalogRowViewModel(entry, CatalogStatusFinder.Find(entry, installed), (key, args) => main.T(key, args), previewFolder, SubscribersOf(entry), InstallAsync));
            Summary = main.T("CatalogSummary", ("Shown", Rows.Count), ("Total", index.Entries.Count));
            Selected = Rows.FirstOrDefault(r => r.Entry.Id == keep);
        }

        /// <summary>Download (or update) the row's mod: confirm, download and verify, unpack, replace the installed copy (to the recycle bin), refresh the launcher's list.</summary>
        private async Task InstallAsync(CatalogRowViewModel row)
        {
            CatalogEntry entry = row.Entry;
            string modsRoot = main.LauncherSettings.ManagedModsRoot;
            if (string.IsNullOrWhiteSpace(modsRoot)) { await main.Dialogs.ShowMessageAsync(main.T("CatalogInstallNeedsModsFolder"), "DW2 Mod Launcher"); return; }
            if (GameProcess.IsRunning(main.LauncherSettings.GameRoot)) { await main.Dialogs.ShowMessageAsync(main.T("CatalogGameRunning"), "DW2 Mod Launcher"); return; }

            bool update = row.Status.State == CatalogInstallState.UpdateAvailable;
            string existing = null;
            if (update)
            {
                if (!LocalModManager.CanDelete(row.Status.Installed, modsRoot))
                {
                    await main.Dialogs.ShowMessageAsync(main.T("CatalogInstallFailed", ("Mod", entry.Name), ("Problem", row.Status.Installed.Folder + " is not a mod folder inside " + modsRoot)), "DW2 Mod Launcher");
                    return;
                }
                existing = row.Status.Installed.Folder;
            }
            string size = CatalogRowViewModel.FormatSize(entry.Size);
            string message = update
                ? main.T("CatalogConfirmUpdate", ("Mod", entry.Name), ("Installed", row.Status.Installed.Version), ("Version", entry.Version), ("Size", size), ("Source", entry.DownloadUrl))
                : main.T("CatalogConfirmDownload", ("Mod", entry.Name), ("Version", entry.Version), ("Size", size), ("Source", entry.DownloadUrl));
            if (row.AuthorText.Length > 0) message += "\n\n" + row.AuthorText;
            if (entry.Kinds.Contains("code")) message += "\n\n" + main.T("CatalogCodeWarning");
            if (!await main.Dialogs.ConfirmAsync(message, main.T(update ? "CatalogConfirmUpdateTitle" : "CatalogConfirmTitle"), main.T(update ? "CatalogUpdate" : "CatalogDownload", ("Version", entry.Version)), main.T("Cancel"))) return;

            string work = System.IO.Path.Combine(UserDataRoot.Get(), "Catalog", "Downloads");
            string zip = null;
            try
            {
                Progress<UpdateProgress> progress = new Progress<UpdateProgress>(p =>
                    row.ProgressText = main.T("CatalogDownloadingProgress", ("Percent", p.Total > 0 ? (int)(p.Done * 100 / p.Total) : 0)));
                row.ProgressText = main.T("CatalogDownloadingProgress", ("Percent", 0));
                zip = await CatalogInstaller.DownloadAsync(entry, AppVersion.Current, work, progress, CancellationToken.None);
                row.ProgressText = main.T("CatalogUnpacking");
                string stage = System.IO.Path.Combine(work, "stage");
                string installed = await Task.Run(() => CatalogInstaller.Install(zip, entry.Name, modsRoot, existing, stage, entry));
                // A downloaded mod must say whose it is (that is how the launcher tells it from the player's own), so a mod that names no author gets the catalog's.
                LauncherMetaWriter.StampAuthor(installed, entry.Author, false);
                main.Refresh();
                main.SetStatus(main.T(update ? "CatalogUpdatedStatus" : "CatalogInstalledStatus", ("Mod", entry.Name), ("Version", entry.Version)));
            }
            catch (Exception ex)
            {
                Logger.LogException("Install catalog mod " + entry.Id, ex);
                await main.Dialogs.ShowMessageAsync(main.T("CatalogInstallFailed", ("Mod", entry.Name), ("Problem", ex.Message)), "DW2 Mod Launcher");
            }
            finally
            {
                row.ProgressText = "";
                if (zip != null && System.IO.File.Exists(zip)) System.IO.File.Delete(zip);
            }
            Rebuild();
        }

        private void OpenRelease()
        {
            string url = selected.Entry.ReleaseUrl;
            try { PlatformShell.Create().OpenUrl(url); }
            catch (Exception ex)
            {
                Logger.LogException("Open release page " + url, ex);
                main.AddProblem(main.T("CatalogOpenFailed", ("Url", url), ("Problem", ex.Message)));
            }
        }
    }
}
