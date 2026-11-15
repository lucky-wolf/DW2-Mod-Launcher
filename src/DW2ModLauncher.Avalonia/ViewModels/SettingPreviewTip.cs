using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Media.Imaging;
using DW2ModLauncher.Core.Diagnostics;
using DW2ModLauncher.Core.Models;

namespace DW2ModLauncher.Avalonia.ViewModels
{
    /// <summary>
    /// What a setting's tooltip shows when the mod gave it a preview image (the schema's "_previews"): the image, then the setting's description.
    /// An image that is missing or cannot be read is not replaced by anything: the tip says so in <see cref="Problem"/> instead.
    /// </summary>
    public class SettingPreviewTip
    {
        public SettingPreviewTip(Bitmap image, string problem, string description)
        {
            Image = image;
            Problem = problem;
            Description = description;
        }

        public Bitmap Image { get; }
        public string Problem { get; }
        public string Description { get; }
        public bool HasImage { get { return Image != null; } }
        public bool HasProblem { get { return Problem != null; } }
        public bool HasDescription { get { return !string.IsNullOrWhiteSpace(Description); } }
    }

    /// <summary>Loads a mod's preview images from its content folder, once each per editor, reporting what it cannot load (log plus the tip's own text) rather than guessing another image.</summary>
    public class SettingPreviewImages
    {
        private readonly string root;
        private readonly LocalizedStrings l;
        private readonly Dictionary<string, SettingPreviewTip> loaded = new Dictionary<string, SettingPreviewTip>(StringComparer.OrdinalIgnoreCase);

        public SettingPreviewImages(string contentRoot, LocalizedStrings l)
        {
            root = contentRoot;
            this.l = l;
        }

        /// <summary>The tip for an image file name from the schema (looked up in the mod's preview folder), with the setting's description under it (null for none).</summary>
        public SettingPreviewTip Get(string fileName, string description)
        {
            if (!loaded.TryGetValue(fileName, out SettingPreviewTip image))
                loaded[fileName] = image = Load(fileName);
            return new SettingPreviewTip(image.Image, image.Problem, description);
        }

        private SettingPreviewTip Load(string fileName)
        {
            string relativePath = ModSettingsField.PreviewFolder + "/" + fileName;
            string full = Path.Combine(root ?? "", ModSettingsField.PreviewFolder, fileName);
            if (!File.Exists(full))
            {
                Logger.Log("Setting preview", "Image not found: " + full);
                return new SettingPreviewTip(null, l.Format("ModSettingsPreviewMissing", ("Path", relativePath)), null);
            }
            try
            {
                return new SettingPreviewTip(new Bitmap(full), null, null);
            }
            catch (Exception ex)
            {
                Logger.LogException("Setting preview " + full, ex);
                return new SettingPreviewTip(null, l.Format("ModSettingsPreviewUnreadable", ("Path", relativePath)), null);
            }
        }
    }
}
