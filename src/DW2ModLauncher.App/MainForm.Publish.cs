using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DW2ModLauncher.Core.Diagnostics;
using DW2ModLauncher.Core.Models;
using DW2ModLauncher.Core.Services;
using DW2ModLauncher.Core.Services.Publishing;

namespace DW2ModLauncher.App
{
    public partial class MainForm
    {
        private void PublishSelectedMod()
        {
            if (modList == null || modList.SelectedItems.Count == 0) return;
            OpenPublishDialog(modList.SelectedItems[0].Tag as ModInfo);
        }

        // One dialog that both edits the mod.json fields Steam Workshop publish itself reads
        // (title, description, preview image, version, bundles - see docs/workshop-publish.md) and
        // kicks off the actual publish, via Publish/Cancel buttons - not a separate "edit" step
        // followed by a separate confirmation popup. Not a Mod's own settings.schema.json, which is
        // a different, Mod-author-defined thing entirely (see MainForm.ModSettings).
        private void OpenPublishDialog(ModInfo mod)
        {
            if (mod == null) return;
            if (mod.IsWorkshop || string.IsNullOrWhiteSpace(mod.ModJsonPath))
            {
                MessageBox.Show(T("PublishNotLocalMod"), Text);
                return;
            }

            ModPublishMetadata metadata;
            try { metadata = ModPublishMetadataEditor.Read(mod.ModJsonPath); }
            catch (Exception ex)
            {
                Logger.LogException("Read mod.json for publish", ex);
                MessageBox.Show(ex.Message, Text);
                return;
            }
            bool isUpdate = !string.IsNullOrWhiteSpace(mod.WorkshopId);

            using (Form dialog = new Form())
            {
                dialog.Text = T("PublishToWorkshop") + " - " + (mod.DisplayName ?? mod.Id);
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.MaximizeBox = false;
                dialog.MinimizeBox = false;
                dialog.ClientSize = new Size(700, 860);
                dialog.BackColor = Dw2Deep;
                dialog.ForeColor = Dw2Text;
                dialog.Font = Font;

                Label titleLabel = new Label { Text = T("PublishInfoTitle"), Location = new Point(24, 20), AutoSize = true };
                dialog.Controls.Add(titleLabel);
                TextBox titleBox = new TextBox { Location = new Point(24, 44), Size = new Size(640, 25), BackColor = Dw2Void, ForeColor = Dw2Text, BorderStyle = BorderStyle.FixedSingle, Text = metadata.DisplayName };
                dialog.Controls.Add(titleBox);

                Label versionLabel = new Label { Text = T("Version"), Location = new Point(24, 82), AutoSize = true };
                dialog.Controls.Add(versionLabel);
                TextBox versionBox = new TextBox { Location = new Point(24, 106), Size = new Size(200, 25), BackColor = Dw2Void, ForeColor = Dw2Text, BorderStyle = BorderStyle.FixedSingle, Text = metadata.Version };
                dialog.Controls.Add(versionBox);

                Label previewLabel = new Label { Text = T("PublishInfoPreviewImage"), Location = new Point(24, 144), AutoSize = true };
                dialog.Controls.Add(previewLabel);
                TextBox previewBox = new TextBox { Location = new Point(24, 168), Size = new Size(560, 25), BackColor = Dw2Void, ForeColor = Dw2Text, BorderStyle = BorderStyle.FixedSingle, Text = metadata.PreviewImage };
                dialog.Controls.Add(previewBox);
                Button previewBrowse = MakeButton("...", 594, 166, 70, 27);
                previewBrowse.Click += delegate
                {
                    using (OpenFileDialog picker = new OpenFileDialog())
                    {
                        picker.InitialDirectory = mod.ContentRoot ?? mod.Folder;
                        picker.Filter = "Image files (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png";
                        if (picker.ShowDialog(dialog) == DialogResult.OK) previewBox.Text = Path.GetFileName(picker.FileName);
                    }
                };
                dialog.Controls.Add(previewBrowse);

                Label descriptionLabel = new Label { Text = T("Description"), Location = new Point(24, 204), AutoSize = true };
                dialog.Controls.Add(descriptionLabel);
                TextBox descriptionBox = new TextBox
                {
                    Location = new Point(24, 228),
                    Size = new Size(640, 320),
                    Multiline = true,
                    ScrollBars = ScrollBars.Vertical,
                    BackColor = Dw2Void,
                    ForeColor = Dw2Text,
                    BorderStyle = BorderStyle.FixedSingle,
                    Text = metadata.Description
                };
                dialog.Controls.Add(descriptionBox);

                Label bundlesLabel = new Label { Text = T("PublishInfoBundles"), Location = new Point(24, 562), AutoSize = true };
                dialog.Controls.Add(bundlesLabel);
                TextBox bundlesBox = new TextBox
                {
                    Location = new Point(24, 586),
                    Size = new Size(640, 90),
                    Multiline = true,
                    ScrollBars = ScrollBars.Vertical,
                    BackColor = Dw2Void,
                    ForeColor = Dw2Text,
                    BorderStyle = BorderStyle.FixedSingle,
                    Text = string.Join(Environment.NewLine, metadata.Bundles ?? new System.Collections.Generic.List<string>())
                };
                dialog.Controls.Add(bundlesBox);

                Label note = new Label
                {
                    Location = new Point(24, 690),
                    Size = new Size(650, 100),
                    ForeColor = Dw2Muted,
                    Text = T(isUpdate ? "PublishAboutToRunUpdate" : "PublishAboutToRunFirstTime")
                };
                dialog.Controls.Add(note);

                Button publish = MakeButton(T("PublishToWorkshop"), 400, 806, 130, 34);
                Button cancel = MakeButton(T("Cancel"), 540, 806, 130, 34);
                cancel.DialogResult = DialogResult.Cancel;
                dialog.Controls.Add(publish);
                dialog.Controls.Add(cancel);

                publish.Click += delegate
                {
                    metadata.DisplayName = titleBox.Text.Trim();
                    metadata.Version = versionBox.Text.Trim();
                    metadata.PreviewImage = previewBox.Text.Trim();
                    metadata.Description = descriptionBox.Text;
                    metadata.Bundles = bundlesBox.Lines.Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
                    try
                    {
                        ModPublishMetadataEditor.Write(mod.ModJsonPath, metadata);
                        dialog.DialogResult = DialogResult.OK;
                        dialog.Close();
                    }
                    catch (Exception ex)
                    {
                        Logger.LogException("Write mod.json publish info", ex);
                        MessageBox.Show(ex.Message, Text);
                    }
                };

                if (dialog.ShowDialog(this) == DialogResult.OK) RunPublishProcess(mod, metadata);
            }
        }

        // Only this method (and ApplyPublishSuccess below) know IModPublisher exists - everything
        // about how a publish actually happens is that implementation's own business, not this
        // class's. SteamworksModPublisher talks to the Steamworks API in-process (see
        // docs/workshop-publish.md), so - unlike the old DW2.exe-shelling approach - the result is
        // always known synchronously: either a real WorkshopId or a real ErrorMessage, never "maybe."
        private void RunPublishProcess(ModInfo mod, ModPublishMetadata metadata)
        {
            if (publishRunning) return;
            publishRunning = true;
            if (publishButton != null) publishButton.Enabled = false;
            SetStatus(T("PublishRunning"));

            IModPublisher publisher = ModPublisherFactory.Create(uint.Parse(SteamLocator.AppId));
            ModPublishRequest request = new ModPublishRequest
            {
                ContentFolder = mod.ContentRoot ?? mod.Folder,
                Title = metadata.DisplayName,
                Description = metadata.Description,
                PreviewImagePath = string.IsNullOrWhiteSpace(metadata.PreviewImage) ? null : Path.Combine(mod.ContentRoot ?? mod.Folder, metadata.PreviewImage),
                ExistingWorkshopId = long.TryParse(mod.WorkshopId, out long existingId) ? existingId : (long?)null
            };

            BackgroundWorker worker = new BackgroundWorker();
            worker.DoWork += delegate (object sender, DoWorkEventArgs e) { e.Result = publisher.Publish(request); };
            worker.RunWorkerCompleted += delegate (object sender, RunWorkerCompletedEventArgs e)
            {
                publishRunning = false;
                if (publishButton != null) publishButton.Enabled = modList != null && modList.SelectedItems.Count > 0 &&
                    !((modList.SelectedItems[0].Tag as ModInfo)?.IsWorkshop ?? true);
                if (e.Error != null)
                {
                    Logger.LogException("Publish Mod to Workshop", e.Error);
                    MessageBox.Show(T("PublishFailed", e.Error.Message), Text);
                    SetStatus(T("PublishFailedStatus"));
                    return;
                }
                ModPublishResult result = e.Result as ModPublishResult;
                if (result == null || !result.WorkshopId.HasValue)
                {
                    MessageBox.Show(T("PublishFailed", result?.ErrorMessage ?? ""), Text);
                    SetStatus(T("PublishFailedStatus"));
                    return;
                }
                ApplyPublishSuccess(mod, result);
            };
            worker.RunWorkerAsync();
        }

        private void ApplyPublishSuccess(ModInfo mod, ModPublishResult result)
        {
            if (mod == null || string.IsNullOrWhiteSpace(mod.ModJsonPath)) return;
            try
            {
                ModJsonWorkshopIdWriter.Write(mod.ModJsonPath, result.WorkshopId.Value);
                string url = "https://steamcommunity.com/sharedfiles/filedetails/?id=" + result.WorkshopId.Value;
                string message = T("PublishCapturedIdMessage", result.WorkshopId.Value, url);
                if (result.NeedsWorkshopAgreement) message += "\r\n\r\n" + T("PublishNeedsWorkshopAgreement");
                ShowPublishSuccessDialog(message, url);
                SetStatus(T("WorkshopIdSaved"));
                RefreshAll();
            }
            catch (Exception ex)
            {
                Logger.LogException("Write workshopId to mod.json", ex);
                MessageBox.Show(ex.Message, Text);
            }
        }

        // A plain MessageBox can't give its buttons custom captions ("Close"/"Open in Browser"
        // instead of "OK"/"Cancel"), so this is a small dialog instead - Close does nothing, Open
        // in Browser launches the item's Workshop page and then closes the dialog too.
        private void ShowPublishSuccessDialog(string message, string url)
        {
            using (Form dialog = new Form())
            {
                dialog.Text = T("PublishToWorkshop");
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.MaximizeBox = false;
                dialog.MinimizeBox = false;
                dialog.ClientSize = new Size(480, 210);
                dialog.BackColor = Dw2Deep;
                dialog.ForeColor = Dw2Text;
                dialog.Font = Font;

                Label messageLabel = new Label
                {
                    Location = new Point(20, 20),
                    Size = new Size(440, 130),
                    Text = message
                };
                dialog.Controls.Add(messageLabel);

                Button openBrowser = MakeButton(T("OpenInBrowser"), 190, 160, 150, 34);
                Button close = MakeButton(T("Close"), 350, 160, 110, 34);
                close.DialogResult = DialogResult.Cancel;
                dialog.Controls.Add(openBrowser);
                dialog.Controls.Add(close);

                openBrowser.Click += delegate
                {
                    try { PlatformShell.Create().OpenUrl(url); }
                    catch (Exception ex) { MessageBox.Show(ex.Message, Text); }
                    dialog.Close();
                };

                dialog.ShowDialog(this);
            }
        }
    }
}
