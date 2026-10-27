using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DW2ModLauncher.Core.Services;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class InstalledLanguagePacksTests : IDisposable
    {
        private readonly string dir = Path.Combine(Path.GetTempPath(), "dw2-langpacks-" + Guid.NewGuid().ToString("N"));

        public InstalledLanguagePacksTests() { Directory.CreateDirectory(dir); }
        public void Dispose() { Directory.Delete(dir, true); }

        private string Write(string relative, string text)
        {
            string full = Path.Combine(dir, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllText(full, text);
            return full;
        }

        private static Dictionary<string, Dictionary<string, string>> English()
        {
            return new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["en"] = new Dictionary<string, string> { ["Hello"] = "Hello" },
                ["ru"] = new Dictionary<string, string> { ["Hello"] = "Privet", ["Bye"] = "Poka" },
            };
        }

        [Fact]
        public void FindsLauncherJsonOfEveryInstalledModEnabledOrNotInFolderOrder()
        {
            Write("mods/PackB/translations/fr/launcher.json", "{}");
            Write("mods/PackA/translations/fr/launcher.json", "{}");
            Write("mods/PackA/translations/fr/DW2Troops/status.json", "{}");
            Write("mods/PackA/translations/de/settings.schema.json", "{}");
            Write("mods/NoPack/readme.txt", "x");

            List<InstalledLanguagePacks.Pack> packs = InstalledLanguagePacks.Find(Path.Combine(dir, "mods"), "");

            Assert.Equal(new[] { "PackA/translations/fr/launcher.json", "PackB/translations/fr/launcher.json" },
                packs.Select(p => Path.GetRelativePath(Path.Combine(dir, "mods"), p.File).Replace(Path.DirectorySeparatorChar, '/')));
            Assert.All(packs, p => Assert.Equal("fr", p.Code));
        }

        [Fact]
        public void ABlankOrMissingRootFindsNothing()
        {
            Assert.Empty(InstalledLanguagePacks.Find("", null));
            Assert.Empty(InstalledLanguagePacks.Find(Path.Combine(dir, "nothing"), Path.Combine(dir, "nothing-either")));
        }

        [Fact]
        public void ANewCodeBecomesALanguageNamedByTheFile()
        {
            var languages = English();
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string file = Write("fr.json", "{ \"_displayName\": \"Francais\", \"Hello\": \"Bonjour\" }");

            var problems = InstalledLanguagePacks.Merge(languages, names, new[] { new InstalledLanguagePacks.Pack { Code = "fr", File = file } }, new List<string>());

            Assert.Empty(problems);
            Assert.Equal("Bonjour", languages["fr"]["Hello"]);
            Assert.Equal("Francais", names["fr"]);
            Assert.False(languages["fr"].ContainsKey("_displayName"));
        }

        [Fact]
        public void AKnownCodeGetsItsKeysReplacedAndTheReplacementIsListed()
        {
            var languages = English();
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["ru"] = "Russkiy" };
            var replaced = new List<string>();
            string file = Write("ru.json", "{ \"_displayName\": \"Other\", \"Hello\": \"Zdravstvuyte\", \"New\": \"Novoe\" }");

            InstalledLanguagePacks.Merge(languages, names, new[] { new InstalledLanguagePacks.Pack { Code = "ru", File = file } }, replaced);

            Assert.Equal("Zdravstvuyte", languages["ru"]["Hello"]);
            Assert.Equal("Poka", languages["ru"]["Bye"]);
            Assert.Equal("Novoe", languages["ru"]["New"]);
            Assert.Equal("Russkiy", names["ru"]);
            Assert.Single(replaced);
            Assert.StartsWith("ru: Hello (", replaced[0]);
        }

        [Fact]
        public void ALaterFileWinsPerKey()
        {
            var languages = English();
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string first = Write("a.json", "{ \"Hello\": \"A\", \"Only\": \"A\" }");
            string second = Write("b.json", "{ \"Hello\": \"B\" }");

            InstalledLanguagePacks.Merge(languages, names, new[]
            {
                new InstalledLanguagePacks.Pack { Code = "de", File = first },
                new InstalledLanguagePacks.Pack { Code = "de", File = second },
            }, new List<string>());

            Assert.Equal("B", languages["de"]["Hello"]);
            Assert.Equal("A", languages["de"]["Only"]);
        }

        [Fact]
        public void AFileThatIsNotUsableIsReportedAndAddsNothing()
        {
            var languages = English();
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string broken = Write("broken.json", "{ not json");
            string wrongShape = Write("array.json", "[1, 2]");
            string missing = Path.Combine(dir, "missing.json");

            var problems = InstalledLanguagePacks.Merge(languages, names, new[]
            {
                new InstalledLanguagePacks.Pack { Code = "xx", File = broken },
                new InstalledLanguagePacks.Pack { Code = "yy", File = wrongShape },
                new InstalledLanguagePacks.Pack { Code = "zz", File = missing },
            }, new List<string>());

            Assert.Equal(new[] { broken, wrongShape, missing }, problems.Select(p => p.File));
            Assert.All(problems, p => Assert.False(string.IsNullOrWhiteSpace(p.Message)));
            Assert.False(languages.ContainsKey("xx"));
            Assert.False(languages.ContainsKey("yy"));
            Assert.False(languages.ContainsKey("zz"));
        }
    }
}
