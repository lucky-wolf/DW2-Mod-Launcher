using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using DW2ModLauncher.Core.Diagnostics;
using Steamworks;

namespace DW2ModLauncher.Core.Services.Publishing
{
    /// <summary>
    /// Workshop publisher on Steamworks.NET + Valve's native Steam API library
    /// (steam_api64.dll on Windows, libsteam_api.so on Linux), talking to the user's already-running,
    /// already-logged-in Steam client. Needs "steam_appid.txt" and the native library next to the executable; the
    /// native library's SDK version must match the Steamworks.NET version (see README "License").
    /// </summary>
    public class SteamworksNetModPublisher : IModPublisher
    {
        private static readonly TimeSpan CallTimeout = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(10);

        // One Steam connection per process, and its callbacks are pumped by whichever call is running, so calls take turns.
        private static readonly object Gate = new object();
        private static bool connected;

        private readonly uint appId;
        private readonly bool keepConnected;

        /// <param name="keepConnected">True: connect on the first call and stay connected until the launcher exits, so Steam sees DW2 running
        /// for as long as the launcher does. False: connect and shut down around every call (used by the short-lived <see cref="SteamWorker"/>).</param>
        public SteamworksNetModPublisher(uint appId, bool keepConnected = false)
        {
            this.appId = appId;
            this.keepConnected = keepConnected;
        }

        private bool Connect()
        {
            if (keepConnected && connected) return true;
            if (!SteamAPI.Init()) return false;
            connected = true;
            return true;
        }

        private void Disconnect()
        {
            if (keepConnected) return;
            connected = false;
            try { SteamAPI.Shutdown(); } catch (Exception ex) { Logger.LogException("Steam shutdown", ex); }
        }

        public ModPublishResult Publish(ModPublishRequest request)
        {
            lock (Gate) return PublishLocked(request);
        }

        private ModPublishResult PublishLocked(ModPublishRequest request)
        {
            ModPublishResult result = new ModPublishResult();
            bool initialized = false;
            string stage = "pre-flight checks";
            try
            {
                if (request.DescriptionOnly && (!request.ExistingWorkshopId.HasValue || string.IsNullOrWhiteSpace(request.Description)))
                {
                    result.ErrorMessage = "A description-only update needs an existing Workshop item and a non-blank description.";
                    return result;
                }
                if (request.VisibilityOnly && (!request.ExistingWorkshopId.HasValue || !request.Visibility.HasValue))
                {
                    result.ErrorMessage = "A visibility-only update needs an existing Workshop item and a visibility.";
                    return result;
                }
                bool metadataOnly = request.DescriptionOnly || request.VisibilityOnly;
                string problem = metadataOnly ? null : Preflight(request);
                if (problem != null)
                {
                    result.ErrorMessage = problem;
                    Logger.Log("Workshop publish pre-flight failed", problem);
                    return result;
                }

                stage = "connecting to Steam";
                if (!Connect())
                {
                    result.ErrorMessage = "Could not reach the Steam client. Make sure Steam is running and you're logged in.";
                    return result;
                }
                initialized = true;
                if (!SteamUser.BLoggedOn())
                {
                    result.ErrorMessage = "Could not reach the Steam client. Make sure Steam is running and you're logged in.";
                    return result;
                }

                PublishedFileId_t fileId;
                if (request.ExistingWorkshopId.HasValue)
                {
                    fileId = new PublishedFileId_t((ulong)request.ExistingWorkshopId.Value);
                }
                else
                {
                    stage = "creating the Workshop item (SteamUGC.CreateItem)";
                    CreateItemResult_t created;
                    if (!Await(SteamUGC.CreateItem((AppId_t)appId, EWorkshopFileType.k_EWorkshopFileTypeCommunity), out created, result, request.Cancel)) return result;
                    if (created.m_eResult != EResult.k_EResultOK)
                    {
                        result.ErrorMessage = Describe(stage, created.m_eResult, request, null);
                        Logger.Log("Workshop publish failed", result.ErrorMessage);
                        return result;
                    }
                    fileId = created.m_nPublishedFileId;
                    result.NeedsWorkshopAgreement = created.m_bUserNeedsToAcceptWorkshopLegalAgreement;
                }
                // Known as soon as Steam creates the item, so even a failed upload below leaves the
                // caller able to retry as an update instead of creating a duplicate.
                result.WorkshopId = (long)fileId.m_PublishedFileId;

                stage = "preparing the update (SteamUGC.StartItemUpdate)";
                UGCUpdateHandle_t update = SteamUGC.StartItemUpdate((AppId_t)appId, fileId);
                stage = "setting the title (SteamUGC.SetItemTitle)";
                if (!metadataOnly && !SteamUGC.SetItemTitle(update, request.Title ?? ""))
                    return Fail(result, stage, "Steam rejected the title " + Quote(request.Title) + " (too long or invalid).", request);
                // Only send a description the author supplied: leaving it out keeps whatever the Steam page already has (an empty one would wipe it).
                stage = "setting the description (SteamUGC.SetItemDescription)";
                if (!string.IsNullOrWhiteSpace(request.Description) && !SteamUGC.SetItemDescription(update, request.Description))
                    return Fail(result, stage, "Steam rejected the description (" + request.Description.Length + " characters; too long?).", request);
                stage = "setting the content folder (SteamUGC.SetItemContent)";
                if (!metadataOnly && !SteamUGC.SetItemContent(update, request.ContentFolder))
                    return Fail(result, stage, "Steam rejected the content folder " + Quote(request.ContentFolder) + ".", request);
                stage = "setting the visibility (SteamUGC.SetItemVisibility)";
                if (!request.DescriptionOnly && request.Visibility.HasValue && !SteamUGC.SetItemVisibility(update, ToSteam(request.Visibility.Value)))
                    return Fail(result, stage, "Steam rejected visibility " + request.Visibility.Value + ".", request);
                stage = "setting the preview image (SteamUGC.SetItemPreview)";
                if (!metadataOnly && !string.IsNullOrWhiteSpace(request.PreviewImagePath) && !SteamUGC.SetItemPreview(update, request.PreviewImagePath))
                    return Fail(result, stage, "Steam could not add the preview image " + Quote(request.PreviewImagePath) + " (" + DescribeFile(request.PreviewImagePath) + ").", request);

                stage = "uploading the content and metadata (SteamUGC.SubmitItemUpdate)";
                SubmitItemUpdateResult_t submitted;
                if (!Await(SteamUGC.SubmitItemUpdate(update, request.ChangeNote ?? ""), out submitted, result, request.Cancel)) return result;
                if (submitted.m_eResult != EResult.k_EResultOK)
                {
                    result.ErrorMessage = Describe(stage, submitted.m_eResult, request, fileId);
                    Logger.Log("Workshop publish failed", result.ErrorMessage);
                    return result;
                }
                result.NeedsWorkshopAgreement |= submitted.m_bUserNeedsToAcceptWorkshopLegalAgreement;

                // The update took; the dependency links are separate Steam calls and never undo it, so their failures are reported beside the success.
                if (request.DependencyIds != null && !metadataOnly)
                {
                    stage = "linking the dependencies (SteamUGC.AddDependency)";
                    SyncDependencies(fileId, request, result);
                }
            }
            catch (Exception ex)
            {
                result.ErrorMessage = "Failed while " + stage + ": " + ex.GetType().Name + ": " + ex.Message;
                Logger.LogException("Workshop publish (" + stage + ")", ex);
            }
            finally
            {
                if (initialized) Disconnect();
            }
            return result;
        }

        // Makes the item's page list the wanted dependencies: reads what it lists now, adds the missing ones and, when asked, removes the others.
        private static void SyncDependencies(PublishedFileId_t item, ModPublishRequest request, ModPublishResult result)
        {
            List<long> listed = ReadDependencies(item, result);
            if (listed == null) return;
            SteamLinks.Diff(request.DependencyIds, listed, request.TrimDependencies, out List<long> add, out List<long> remove);
            foreach (long id in add)
            {
                AddUGCDependencyResult_t answer;
                ModPublishResult wait = new ModPublishResult();
                if (!Await(SteamUGC.AddDependency(item, new PublishedFileId_t((ulong)id)), out answer, wait, request.Cancel, QueryTimeout))
                    result.LinkProblems.Add("Could not link Workshop item " + id + " as a dependency: " + wait.ErrorMessage);
                else if (answer.m_eResult != EResult.k_EResultOK)
                    result.LinkProblems.Add("Steam would not link Workshop item " + id + " as a dependency: " + answer.m_eResult + " (" + (int)answer.m_eResult + "). It may be private, unlisted or deleted.");
                else result.LinkedIds.Add(id);
            }
            foreach (long id in remove)
            {
                RemoveUGCDependencyResult_t answer;
                ModPublishResult wait = new ModPublishResult();
                if (!Await(SteamUGC.RemoveDependency(item, new PublishedFileId_t((ulong)id)), out answer, wait, request.Cancel, QueryTimeout))
                    result.LinkProblems.Add("Could not remove the dependency link to Workshop item " + id + ": " + wait.ErrorMessage);
                else if (answer.m_eResult != EResult.k_EResultOK)
                    result.LinkProblems.Add("Steam would not remove the dependency link to Workshop item " + id + ": " + answer.m_eResult + " (" + (int)answer.m_eResult + ").");
                else result.UnlinkedIds.Add(id);
            }
        }

        // The Workshop ids the item's page lists as required items, or null (with a problem noted) when Steam could not say.
        private static List<long> ReadDependencies(PublishedFileId_t item, ModPublishResult result)
        {
            UGCQueryHandle_t query = SteamUGC.CreateQueryUGCDetailsRequest(new[] { item }, 1);
            try
            {
                if (!SteamUGC.SetReturnChildren(query, true))
                {
                    result.LinkProblems.Add("Steam would not list the item's current dependencies, so none were changed.");
                    return null;
                }
                SteamUGCQueryCompleted_t completed;
                ModPublishResult wait = new ModPublishResult();
                if (!Await(SteamUGC.SendQueryUGCRequest(query), out completed, wait, CancellationToken.None, QueryTimeout))
                {
                    result.LinkProblems.Add("Could not read the item's current dependencies, so none were changed: " + wait.ErrorMessage);
                    return null;
                }
                SteamUGCDetails_t details;
                if (completed.m_eResult != EResult.k_EResultOK || completed.m_unNumResultsReturned < 1 || !SteamUGC.GetQueryUGCResult(query, 0, out details) || details.m_eResult != EResult.k_EResultOK)
                {
                    result.LinkProblems.Add("Steam did not return the item's current dependencies (" + completed.m_eResult + "), so none were changed.");
                    return null;
                }
                List<long> listed = new List<long>();
                if (details.m_unNumChildren > 0)
                {
                    PublishedFileId_t[] children = new PublishedFileId_t[details.m_unNumChildren];
                    if (!SteamUGC.GetQueryUGCChildren(query, 0, children, details.m_unNumChildren))
                    {
                        result.LinkProblems.Add("Steam would not list the item's current dependencies, so none were changed.");
                        return null;
                    }
                    listed.AddRange(children.Select(c => (long)c.m_PublishedFileId));
                }
                return listed;
            }
            finally { SteamUGC.ReleaseQueryUGCRequest(query); }
        }

        public ModVisibility? GetVisibility(long workshopId)
        {
            lock (Gate) return GetVisibilityLocked(workshopId);
        }

        private ModVisibility? GetVisibilityLocked(long workshopId)
        {
            bool initialized = false;
            try
            {
                if (!Connect()) return null;
                initialized = true;
                if (!SteamUser.BLoggedOn()) return null;

                // A details query by id returns the logged-in user's own private items too, which the anonymous Web API does not.
                UGCQueryHandle_t query = SteamUGC.CreateQueryUGCDetailsRequest(new[] { new PublishedFileId_t((ulong)workshopId) }, 1);
                try
                {
                    SteamUGCQueryCompleted_t completed;
                    if (!Await(SteamUGC.SendQueryUGCRequest(query), out completed, new ModPublishResult(), System.Threading.CancellationToken.None, QueryTimeout)) return null;
                    if (completed.m_eResult != EResult.k_EResultOK || completed.m_unNumResultsReturned < 1) return null;
                    SteamUGCDetails_t details;
                    if (!SteamUGC.GetQueryUGCResult(query, 0, out details) || details.m_eResult != EResult.k_EResultOK) return null;
                    return FromSteam(details.m_eVisibility);
                }
                finally { SteamUGC.ReleaseQueryUGCRequest(query); }
            }
            catch (Exception ex)
            {
                Logger.LogException("Read Workshop item visibility", ex);
                return null;
            }
            finally
            {
                if (initialized) Disconnect();
            }
        }

        public List<SteamItemStatus> QueryItems(IReadOnlyList<long> workshopIds)
        {
            lock (Gate) return QueryItemsLocked(workshopIds);
        }

        private List<SteamItemStatus> QueryItemsLocked(IReadOnlyList<long> workshopIds)
        {
            List<SteamItemStatus> items = new List<SteamItemStatus>();
            if (workshopIds == null || workshopIds.Count == 0) return items;
            bool initialized = false;
            try
            {
                if (!Connect()) return items;
                initialized = true;
                if (!SteamUser.BLoggedOn()) return items;

                const int PageSize = 50; // Steam's cap on ids per details query
                for (int start = 0; start < workshopIds.Count; start += PageSize)
                {
                    PublishedFileId_t[] page = workshopIds.Skip(start).Take(PageSize).Select(id => new PublishedFileId_t((ulong)id)).ToArray();
                    UGCQueryHandle_t query = SteamUGC.CreateQueryUGCDetailsRequest(page, (uint)page.Length);
                    try
                    {
                        SteamUGCQueryCompleted_t completed;
                        if (!Await(SteamUGC.SendQueryUGCRequest(query), out completed, new ModPublishResult(), CancellationToken.None, QueryTimeout)) continue;
                        if (completed.m_eResult != EResult.k_EResultOK) continue;
                        // Results come back in request order; a deleted item has a per-item k_EResultFileNotFound.
                        List<SteamItemStatus> pageItems = new List<SteamItemStatus>();
                        int missing = 0;
                        for (uint i = 0; i < completed.m_unNumResultsReturned && i < page.Length; i++)
                        {
                            SteamUGCDetails_t details;
                            if (!SteamUGC.GetQueryUGCResult(query, i, out details)) continue;
                            long id = (long)page[i].m_PublishedFileId;
                            if (details.m_eResult == EResult.k_EResultFileNotFound)
                            {
                                // Only a live answer counts as a deletion; cached data must never read as "deleted".
                                if (completed.m_bCachedData) continue;
                                pageItems.Add(new SteamItemStatus { Id = id, Deleted = true });
                                missing++;
                            }
                            else if (details.m_eResult == EResult.k_EResultOK)
                            {
                                ulong subscribers;
                                bool counted = SteamUGC.GetQueryUGCStatistic(query, i, EItemStatistic.k_EItemStatistic_NumUniqueSubscriptions, out subscribers);
                                pageItems.Add(new SteamItemStatus { Id = id, Visibility = FromSteam(details.m_eVisibility), Subscribers = counted ? (long?)subscribers : null });
                            }
                        }
                        // Every item "missing" at once looks like Steam being unwell rather than a mass deletion
                        // (a single item can't tell, so that one is trusted).
                        if (page.Length > 1 && missing == page.Length)
                        {
                            Logger.Log("Workshop item check", "Steam reported all " + page.Length + " items missing; ignoring as unreliable.");
                            continue;
                        }
                        items.AddRange(pageItems);
                    }
                    finally { SteamUGC.ReleaseQueryUGCRequest(query); }
                }
            }
            catch (Exception ex)
            {
                Logger.LogException("Check Workshop items", ex);
                return new List<SteamItemStatus>();
            }
            finally
            {
                if (initialized) Disconnect();
            }
            return items;
        }

        public string Unsubscribe(long workshopId)
        {
            lock (Gate) return UnsubscribeLocked(workshopId);
        }

        private string UnsubscribeLocked(long workshopId)
        {
            bool initialized = false;
            try
            {
                if (!Connect()) return "Could not reach the Steam client. Make sure Steam is running and you're logged in.";
                initialized = true;
                if (!SteamUser.BLoggedOn()) return "Could not reach the Steam client. Make sure Steam is running and you're logged in.";

                ModPublishResult wait = new ModPublishResult();
                RemoteStorageUnsubscribePublishedFileResult_t answer;
                if (!Await(SteamUGC.UnsubscribeItem(new PublishedFileId_t((ulong)workshopId)), out answer, wait, CancellationToken.None, QueryTimeout))
                    return wait.ErrorMessage;
                if (answer.m_eResult != EResult.k_EResultOK)
                {
                    string problem = "Steam refused to unsubscribe from item " + workshopId + ": " + answer.m_eResult + " (" + (int)answer.m_eResult + ").";
                    Logger.Log("Workshop unsubscribe failed", problem);
                    return problem;
                }
                return null;
            }
            catch (Exception ex)
            {
                Logger.LogException("Workshop unsubscribe", ex);
                return "Failed while unsubscribing from item " + workshopId + ": " + ex.GetType().Name + ": " + ex.Message;
            }
            finally
            {
                if (initialized) Disconnect();
            }
        }

        private static ModVisibility FromSteam(ERemoteStoragePublishedFileVisibility visibility)
        {
            switch (visibility)
            {
                case ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPublic: return ModVisibility.Public;
                case ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityFriendsOnly: return ModVisibility.FriendsOnly;
                case ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityUnlisted: return ModVisibility.Unlisted;
                default: return ModVisibility.Private;
            }
        }

        private static string Quote(string s)
        {
            return "\"" + s + "\"";
        }

        private static ModPublishResult Fail(ModPublishResult result, string stage, string detail, ModPublishRequest request)
        {
            result.ErrorMessage = "Failed while " + stage + ":\n" + detail + "\n\n" + Context(request, null);
            Logger.Log("Workshop publish failed", result.ErrorMessage);
            return result;
        }

        // What Steam was asked to read, so the failing path is visible rather than just an EResult name.
        private static string Context(ModPublishRequest request, PublishedFileId_t? fileId)
        {
            StringBuilder b = new StringBuilder();
            b.AppendLine("OS: " + RuntimeInformation.OSDescription);
            b.AppendLine("Workshop item: " + (request.ExistingWorkshopId.HasValue ? request.ExistingWorkshopId.Value.ToString() + " (update of existing)" : fileId.HasValue ? fileId.Value.m_PublishedFileId + " (just created)" : "new"));
            b.AppendLine("Content folder: " + Quote(request.ContentFolder) + " - " + DescribeFolder(request.ContentFolder));
            if (!string.IsNullOrWhiteSpace(request.PreviewImagePath))
                b.AppendLine("Preview image: " + Quote(request.PreviewImagePath) + " - " + DescribeFile(request.PreviewImagePath));
            else
                b.AppendLine("Preview image: (none)");
            b.Append("Log: " + Logger.CrashLogPath);
            return b.ToString();
        }

        private static string DescribeFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "NOT SET";
            if (!Path.IsPathRooted(path)) return "not an absolute path";
            if (!Directory.Exists(path)) return "DOES NOT EXIST";
            try { return "exists, " + Directory.GetFiles(path, "*", SearchOption.AllDirectories).Length + " file(s)"; }
            catch (Exception ex) { return "exists but cannot be listed (" + ex.Message + ")"; }
        }

        private static string DescribeFile(string path)
        {
            if (File.Exists(path)) return "exists, " + new FileInfo(path).Length + " bytes";
            string dir = Path.GetDirectoryName(path);
            string name = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                foreach (string candidate in Directory.GetFiles(dir))
                    if (string.Equals(Path.GetFileName(candidate), name, StringComparison.OrdinalIgnoreCase))
                        return "DOES NOT EXIST, but " + Quote(Path.GetFileName(candidate)) + " does (filename case differs)";
                return "DOES NOT EXIST (folder exists but has no file of that name)";
            }
            if (path.IndexOf('\\') >= 0 && Path.DirectorySeparatorChar != '\\') return "DOES NOT EXIST (path contains '\\'; use '/' in mod.json on Linux)";
            return "DOES NOT EXIST (its folder is missing too)";
        }

        // Cheap local checks first: a missing file/folder is the usual cause of k_EResultFileNotFound,
        // and naming the exact path beats Steam's one-word answer.
        private static string Preflight(ModPublishRequest request)
        {
            string folder = DescribeFolder(request.ContentFolder);
            if (!folder.StartsWith("exists", StringComparison.Ordinal) || folder.StartsWith("exists, 0 file", StringComparison.Ordinal))
                return "Failed while adding the content folder " + Quote(request.ContentFolder) + ": " + folder + ".\n\n" + Context(request, null);
            if (!string.IsNullOrWhiteSpace(request.PreviewImagePath) && !File.Exists(request.PreviewImagePath))
                return "Failed while adding the preview image " + Quote(request.PreviewImagePath) + ": file not found - " + DescribeFile(request.PreviewImagePath) + ".\n\n" + Context(request, null);
            return null;
        }

        // Steam answers with a bare EResult; say which call it was, what each EResult usually means there, and what we sent.
        private static string Describe(string stage, EResult code, ModPublishRequest request, PublishedFileId_t? fileId)
        {
            string hint;
            switch (code)
            {
                case EResult.k_EResultFileNotFound:
                    hint = request.ExistingWorkshopId.HasValue
                        ? "Steam could not find a file it was given, OR the Workshop item " + request.ExistingWorkshopId.Value + " in mod.json no longer exists (deleted, or belongs to a different app). If the paths below look right, remove \"workshopId\" from mod.json to publish as a new item. If Steam is a Flatpak/Snap install it may not be able to see the paths below."
                        : "Steam could not read a file it was given. If the paths below look right and Steam is a Flatpak/Snap install, it may not have access to that folder.";
                    break;
                case EResult.k_EResultAccessDenied: hint = "This Steam account does not own the item, or may not publish to this app."; break;
                case EResult.k_EResultInvalidParam: hint = "Steam rejected a field (title, description, or content)."; break;
                case EResult.k_EResultLimitExceeded: hint = "Over the Workshop quota or the item size limit."; break;
                case EResult.k_EResultNotLoggedOn: hint = "Steam is not logged in."; break;
                case EResult.k_EResultTimeout: hint = "Steam timed out."; break;
                case EResult.k_EResultBanned: hint = "This account is banned from the Workshop."; break;
                default: hint = null; break;
            }
            return "Failed while " + stage + ":\nSteam returned " + code + " (" + (int)code + ")." + (hint == null ? "" : "\n" + hint) + "\n\n" + Context(request, fileId);
        }

        private static ERemoteStoragePublishedFileVisibility ToSteam(ModVisibility visibility)
        {
            switch (visibility)
            {
                case ModVisibility.Public: return ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPublic;
                case ModVisibility.FriendsOnly: return ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityFriendsOnly;
                case ModVisibility.Unlisted: return ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityUnlisted;
                default: return ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPrivate;
            }
        }

        // Steamworks.NET results only arrive while SteamAPI.RunCallbacks() is being pumped.
        private static bool Await<T>(SteamAPICall_t call, out T value, ModPublishResult result, CancellationToken cancel, TimeSpan? timeout = null) where T : struct
        {
            T received = default(T);
            bool done = false;
            bool ioFailure = false;
            CallResult<T> callResult = CallResult<T>.Create(delegate (T r, bool failed) { received = r; ioFailure = failed; done = true; });
            callResult.Set(call);

            DateTime deadline = DateTime.UtcNow + (timeout ?? CallTimeout);
            while (!done && !cancel.IsCancellationRequested && DateTime.UtcNow < deadline)
            {
                SteamAPI.RunCallbacks();
                Thread.Sleep(50);
            }
            callResult.Dispose();
            value = received;
            if (!done && cancel.IsCancellationRequested) result.ErrorMessage = "Cancelled.";
            else if (!done) result.ErrorMessage = "Timed out waiting for Steam.";
            else if (ioFailure) result.ErrorMessage = "Lost contact with the Steam client.";
            return done && !ioFailure;
        }
    }
}
