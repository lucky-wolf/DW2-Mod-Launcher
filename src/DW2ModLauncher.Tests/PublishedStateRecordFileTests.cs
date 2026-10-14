using System;
using System.IO;
using DW2ModLauncher.Core.Services.Publishing;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class PublishedStateRecordFileTests
    {
        [Fact]
        public void RecordFile_ClearsOnlyThatFilesChange()
        {
            string root = Path.Combine(Path.GetTempPath(), "dw2-recordfile-" + Guid.NewGuid().ToString("N"));
            string folder = Path.Combine(root, "mod");
            string data = Path.Combine(root, "data");
            Directory.CreateDirectory(folder);
            try
            {
                File.WriteAllText(Path.Combine(folder, "description.bbcode"), "old");
                File.WriteAllText(Path.Combine(folder, "other.txt"), "same");
                PublishedState.Record("42", folder, data);

                File.WriteAllText(Path.Combine(folder, "description.bbcode"), "new, pushed to Steam on its own");
                File.WriteAllText(Path.Combine(folder, "other.txt"), "edited");
                PublishedState.RecordFile("42", folder, "description.bbcode", data);

                PublishedChanges changes = PublishedState.Compare(folder, PublishedState.Load("42", data));
                Assert.Equal(new[] { "other.txt" }, changes.Modified);
                Assert.Empty(changes.Added);
            }
            finally { Directory.Delete(root, true); }
        }
    }
}
