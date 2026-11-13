using System;
using DW2ModLauncher.Core.Services.Publishing;

namespace DW2ModLauncher.Avalonia.ViewModels
{
    /// <summary>The words Mark Up to Date uses for the targets it acts on, shared by the mod list and the Publish dialog.</summary>
    internal static class MarkUpToDateText
    {
        /// <summary>"the Steam Workshop", "GitHub" or "the Steam Workshop and GitHub", in the player's language.</summary>
        public static string Targets(PublishChannels channels, Func<string, string> t)
        {
            return channels == (PublishChannels.Steam | PublishChannels.GitHub) ? t("MarkUpToDateTargetBoth")
                : channels == PublishChannels.GitHub ? t("MarkUpToDateTargetGitHub")
                : t("MarkUpToDateTargetSteam");
        }
    }
}
