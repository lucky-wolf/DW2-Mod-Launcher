extern alias facepunch;
using System;
using facepunch::Steamworks;
using facepunch::Steamworks.Ugc;

namespace DW2ModLauncher.Core.Services.Publishing
{
    /// <summary>
    /// Publishes a local Mod by embedding the Steamworks API directly into the launcher (via
    /// Facepunch.Steamworks - MIT licensed, the same wrapper DW2 itself uses for its own
    /// "--ugc-publish", per the "source=Facepunch.Steamworks" query parameter on the URL it prints)
    /// instead of shelling out to any external tool. Because the launcher's own process talks to the
    /// Steamworks API the normal way a Steam-integrated app does, it piggybacks on whatever Steam
    /// account is already logged into the local Steam client - no separate login, no credentials of
    /// any kind ever pass through the launcher, unlike driving steamcmd.exe (a genuinely separate
    /// tool with its own login session) would have required. See docs/workshop-publish.md.
    ///
    /// Windows only - see SteamworksNetModPublisher for Linux.
    ///
    /// Requires a "steam_appid.txt" file containing the app id next to the launcher's own
    /// executable, and the native steam_api64.dll shipped alongside it (see
    /// DW2ModLauncher.App.csproj) - both standard requirements for any non-Steam-launched process
    /// using this API.
    /// </summary>
    public class SteamworksModPublisher : IModPublisher
    {
        private readonly uint appId;

        public SteamworksModPublisher(uint appId)
        {
            this.appId = appId;
        }

        public ModPublishResult Publish(ModPublishRequest request)
        {
            ModPublishResult result = new ModPublishResult();
            bool initializedHere = false;
            try
            {
                if (!SteamClient.IsValid)
                {
                    SteamClient.Init(appId, true);
                    initializedHere = true;
                }
                if (!SteamClient.IsValid || !SteamClient.IsLoggedOn)
                {
                    result.ErrorMessage = "Could not reach the Steam client. Make sure Steam is running and you're logged in.";
                    return result;
                }

                Editor editor = request.ExistingWorkshopId.HasValue
                    ? new Editor((ulong)request.ExistingWorkshopId.Value)
                    : Editor.NewCommunityFile;

                editor = editor
                    .WithTitle(request.Title ?? "")
                    .WithDescription(request.Description ?? "")
                    .WithContent(request.ContentFolder);
                if (request.Visibility.HasValue)
                {
                    switch (request.Visibility.Value)
                    {
                        case ModVisibility.Public: editor = editor.WithPublicVisibility(); break;
                        case ModVisibility.FriendsOnly: editor = editor.WithFriendsOnlyVisibility(); break;
                        case ModVisibility.Unlisted: throw new NotSupportedException("This publisher does not support unlisted items.");
                        default: editor = editor.WithPrivateVisibility(); break;
                    }
                }
                if (!string.IsNullOrWhiteSpace(request.PreviewImagePath)) editor = editor.WithPreviewFile(request.PreviewImagePath);

                PublishResult publishResult = editor.SubmitAsync(null).GetAwaiter().GetResult();
                if (!publishResult.Success)
                {
                    result.ErrorMessage = "Steam reported an error: " + publishResult.Result;
                    return result;
                }
                result.WorkshopId = (long)(ulong)publishResult.FileId;
                result.NeedsWorkshopAgreement = publishResult.NeedsWorkshopAgreement;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
            }
            finally
            {
                if (initializedHere) { try { SteamClient.Shutdown(); } catch { } }
            }
            return result;
        }
    }
}
