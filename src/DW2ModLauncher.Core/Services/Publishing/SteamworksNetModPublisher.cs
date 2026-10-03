using System;
using System.IO;
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

        private readonly uint appId;

        public SteamworksNetModPublisher(uint appId)
        {
            this.appId = appId;
        }

        public ModPublishResult Publish(ModPublishRequest request)
        {
            ModPublishResult result = new ModPublishResult();
            bool initialized = false;
            string stage = "pre-flight checks";
            try
            {
                string problem = Preflight(request);
                if (problem != null)
                {
                    result.ErrorMessage = problem;
                    Logger.Log("Workshop publish pre-flight failed", problem);
                    return result;
                }

                stage = "connecting to Steam";
                if (!SteamAPI.Init())
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
                if (!SteamUGC.SetItemTitle(update, request.Title ?? ""))
                    return Fail(result, stage, "Steam rejected the title " + Quote(request.Title) + " (too long or invalid).", request);
                // Only send a description the author supplied: leaving it out keeps whatever the Steam page already has (an empty one would wipe it).
                stage = "setting the description (SteamUGC.SetItemDescription)";
                if (!string.IsNullOrWhiteSpace(request.Description) && !SteamUGC.SetItemDescription(update, request.Description))
                    return Fail(result, stage, "Steam rejected the description (" + request.Description.Length + " characters; too long?).", request);
                stage = "setting the content folder (SteamUGC.SetItemContent)";
                if (!SteamUGC.SetItemContent(update, request.ContentFolder))
                    return Fail(result, stage, "Steam rejected the content folder " + Quote(request.ContentFolder) + ".", request);
                stage = "setting the visibility (SteamUGC.SetItemVisibility)";
                if (request.Visibility.HasValue && !SteamUGC.SetItemVisibility(update, ToSteam(request.Visibility.Value)))
                    return Fail(result, stage, "Steam rejected visibility " + request.Visibility.Value + ".", request);
                stage = "setting the preview image (SteamUGC.SetItemPreview)";
                if (!string.IsNullOrWhiteSpace(request.PreviewImagePath) && !SteamUGC.SetItemPreview(update, request.PreviewImagePath))
                    return Fail(result, stage, "Steam could not add the preview image " + Quote(request.PreviewImagePath) + " (" + DescribeFile(request.PreviewImagePath) + ").", request);

                stage = "uploading the content and metadata (SteamUGC.SubmitItemUpdate)";
                SubmitItemUpdateResult_t submitted;
                if (!Await(SteamUGC.SubmitItemUpdate(update, ""), out submitted, result, request.Cancel)) return result;
                if (submitted.m_eResult != EResult.k_EResultOK)
                {
                    result.ErrorMessage = Describe(stage, submitted.m_eResult, request, fileId);
                    Logger.Log("Workshop publish failed", result.ErrorMessage);
                    return result;
                }
                result.NeedsWorkshopAgreement |= submitted.m_bUserNeedsToAcceptWorkshopLegalAgreement;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = "Failed while " + stage + ": " + ex.GetType().Name + ": " + ex.Message;
                Logger.LogException("Workshop publish (" + stage + ")", ex);
            }
            finally
            {
                if (initialized) { try { SteamAPI.Shutdown(); } catch { } }
            }
            return result;
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
        private static bool Await<T>(SteamAPICall_t call, out T value, ModPublishResult result, CancellationToken cancel) where T : struct
        {
            T received = default(T);
            bool done = false;
            bool ioFailure = false;
            CallResult<T> callResult = CallResult<T>.Create(delegate (T r, bool failed) { received = r; ioFailure = failed; done = true; });
            callResult.Set(call);

            DateTime deadline = DateTime.UtcNow + CallTimeout;
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
