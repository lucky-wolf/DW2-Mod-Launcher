using System;
using System.Threading;
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
            try
            {
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
                    CreateItemResult_t created;
                    if (!Await(SteamUGC.CreateItem((AppId_t)appId, EWorkshopFileType.k_EWorkshopFileTypeCommunity), out created, result, request.Cancel)) return result;
                    if (created.m_eResult != EResult.k_EResultOK)
                    {
                        result.ErrorMessage = "Steam reported an error: " + created.m_eResult;
                        return result;
                    }
                    fileId = created.m_nPublishedFileId;
                    result.NeedsWorkshopAgreement = created.m_bUserNeedsToAcceptWorkshopLegalAgreement;
                }
                // Known as soon as Steam creates the item, so even a failed upload below leaves the
                // caller able to retry as an update instead of creating a duplicate.
                result.WorkshopId = (long)fileId.m_PublishedFileId;

                UGCUpdateHandle_t update = SteamUGC.StartItemUpdate((AppId_t)appId, fileId);
                SteamUGC.SetItemTitle(update, request.Title ?? "");
                SteamUGC.SetItemDescription(update, request.Description ?? "");
                SteamUGC.SetItemContent(update, request.ContentFolder);
                if (request.Visibility.HasValue) SteamUGC.SetItemVisibility(update, ToSteam(request.Visibility.Value));
                if (!string.IsNullOrWhiteSpace(request.PreviewImagePath)) SteamUGC.SetItemPreview(update, request.PreviewImagePath);

                SubmitItemUpdateResult_t submitted;
                if (!Await(SteamUGC.SubmitItemUpdate(update, ""), out submitted, result, request.Cancel)) return result;
                if (submitted.m_eResult != EResult.k_EResultOK)
                {
                    result.ErrorMessage = "Steam reported an error: " + submitted.m_eResult;
                    return result;
                }
                result.NeedsWorkshopAgreement |= submitted.m_bUserNeedsToAcceptWorkshopLegalAgreement;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
            }
            finally
            {
                if (initialized) { try { SteamAPI.Shutdown(); } catch { } }
            }
            return result;
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
