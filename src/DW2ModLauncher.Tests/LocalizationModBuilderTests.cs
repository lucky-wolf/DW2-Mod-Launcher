using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DW2ModLauncher.Core.Models;
using DW2ModLauncher.Core.Services;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class LocalizationModBuilderTests
    {
        private static string TempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "dw2-loc-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static void Write(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text);
        }

        [Fact]
        public void CollectDataFiles_Orders_By_File_Name_Game_First_Then_Mods_And_Reads_Top_Level_Only()
        {
            string root = TempDir();
            try
            {
                string game = Path.Combine(root, "game");
                string modA = Path.Combine(root, "modA");
                string modB = Path.Combine(root, "modB");
                Write(Path.Combine(game, "data", "Races_Atuuk.xml"), "<x/>");
                Write(Path.Combine(game, "data", "Races.xml"), "<x/>");
                Write(Path.Combine(game, "data", "Hints.txt"), "x");
                Write(Path.Combine(modA, "races.xml"), "<x/>");           // same name as the game's file, different case
                Write(Path.Combine(modA, "patches", "Races.xml"), "<x/>"); // not data: below the top level
                Write(Path.Combine(modB, "Resources_B.xml"), "<x/>");

                List<string> files = LocalizationModBuilder.CollectDataFiles(game, new[]
                {
                    new ModInfo { Id = "a", Folder = modA, ContentRoot = modA },
                    new ModInfo { Id = "b", Folder = modB, ContentRoot = modB }
                });

                Assert.Equal(new[]
                {
                    Path.Combine(game, "data", "Races.xml"),
                    Path.Combine(modA, "races.xml"),
                    Path.Combine(game, "data", "Races_Atuuk.xml"),
                    Path.Combine(modB, "Resources_B.xml")
                }, files);
            }
            finally { Directory.Delete(root, true); }
        }

        [Fact]
        public void CollectModDataFiles_Returns_Only_That_Mods_Top_Level_Xml()
        {
            string root = TempDir();
            try
            {
                Write(Path.Combine(root, "Races_B.xml"), "<x/>");
                Write(Path.Combine(root, "Races_A.xml"), "<x/>");
                Write(Path.Combine(root, "mod.json"), "{}");
                Write(Path.Combine(root, "patches", "Race.xml"), "<x/>");

                List<string> files = LocalizationModBuilder.CollectModDataFiles(new ModInfo { Id = "m", Folder = root, ContentRoot = root });

                Assert.Equal(new[] { Path.Combine(root, "Races_A.xml"), Path.Combine(root, "Races_B.xml") }, files);
                Assert.Empty(LocalizationModBuilder.CollectModDataFiles(new ModInfo { Id = "gone", Folder = Path.Combine(root, "missing") }));
                Assert.Empty(LocalizationModBuilder.CollectModDataFiles(null));
            }
            finally { Directory.Delete(root, true); }
        }


        [Fact]
        public void CollectTextFiles_Takes_The_Translatable_Txt_Files_And_Lets_Mods_Replace_Them()
        {
            string root = TempDir();
            try
            {
                string game = Path.Combine(root, "game");
                string mod = Path.Combine(root, "mod");
                Write(Path.Combine(game, "data", "GameText.txt"), "A ;game");
                Write(Path.Combine(game, "data", "Hints.txt"), "hint");
                Write(Path.Combine(game, "data", "GameSettingsOverrides.txt"), "not text");
                Write(Path.Combine(game, "data", "dialog", "zenox.txt"), "GREETING ;hi");
                Write(Path.Combine(game, "data", "Galactopedia", "GameConcepts", "Alliances.txt"), "text");
                Write(Path.Combine(game, "data", "Logs", "x.txt"), "log");
                Write(Path.Combine(mod, "gametext.txt"), "A ;mod");

                List<LocalizationTextFile> files = LocalizationModBuilder.CollectTextFiles(game, new[] { new ModInfo { Id = "m", Folder = mod, ContentRoot = mod } });

                Assert.Equal(new[] { "dialog/zenox.txt", "Galactopedia/GameConcepts/Alliances.txt", "gametext.txt", "Hints.txt" }, files.ConvertAll(f => f.Relative));
                Assert.Equal(Path.Combine(mod, "gametext.txt"), files.Single(f => f.Relative == "gametext.txt").Source);
            }
            finally { Directory.Delete(root, true); }
        }

        [Fact]
        public void Build_Copies_Text_Files_Into_The_Mod_At_The_Same_Path()
        {
            string root = TempDir();
            try
            {
                Write(Path.Combine(root, "src", "GameText.txt"), "A ;one");
                Write(Path.Combine(root, "src", "zenox.txt"), "B ;two");
                string mod = Path.Combine(root, "mod");
                Directory.CreateDirectory(mod);

                LocalizationResult result = LocalizationModBuilder.Build(mod, new string[0], new[]
                {
                    new LocalizationTextFile { Source = Path.Combine(root, "src", "GameText.txt"), Relative = "GameText.txt" },
                    new LocalizationTextFile { Source = Path.Combine(root, "src", "zenox.txt"), Relative = "dialog/zenox.txt" }
                });

                Assert.Equal(2, result.TextFiles);
                Assert.Equal("A ;one", File.ReadAllText(Path.Combine(mod, "GameText.txt")));
                Assert.Equal("B ;two", File.ReadAllText(Path.Combine(mod, "dialog", "zenox.txt")));
            }
            finally { Directory.Delete(root, true); }
        }

        [Fact]
        public void Build_Names_Output_Like_The_Data_File_And_Uses_Subfolders_When_Names_Clash()
        {
            string root = TempDir();
            try
            {
                string data = Path.Combine(root, "data");
                string mod = Path.Combine(root, "SomeMod");
                string output = Path.Combine(root, "out");
                Directory.CreateDirectory(output);
                Write(Path.Combine(data, "Races.xml"), "<ArrayOfRace><Race><RaceId>0</RaceId><Name>Human</Name></Race></ArrayOfRace>");
                Write(Path.Combine(data, "Races_Atuuk.xml"), "<ArrayOfRace><Race><RaceId>2</RaceId><Name>Atuuk</Name></Race></ArrayOfRace>");
                Write(Path.Combine(mod, "Races.xml"), "<ArrayOfRace><Race><RaceId>9</RaceId><Name>Modded</Name></Race></ArrayOfRace>");

                LocalizationResult result = LocalizationModBuilder.Build(output, new[]
                {
                    Path.Combine(data, "Races.xml"), Path.Combine(mod, "Races.xml"), Path.Combine(data, "Races_Atuuk.xml")
                });

                Assert.Equal(3, result.Files);
                Assert.True(File.Exists(Path.Combine(output, "patches", "Races_Atuuk.xml")));      // unique name: top level
                Assert.True(File.Exists(Path.Combine(output, "patches", "data", "Races.xml")));    // same name in two folders: one subfolder each
                Assert.True(File.Exists(Path.Combine(output, "patches", "SomeMod", "Races.xml")));
            }
            finally { Directory.Delete(root, true); }
        }
        [Fact]
        public void Build_Writes_One_Patch_File_Per_Data_File_And_Counts_What_It_Found()
        {
            string root = TempDir();
            try
            {
                string data = Path.Combine(root, "data");
                Write(Path.Combine(data, "Races.xml"), "<ArrayOfRace><Race><RaceId>0</RaceId><Name>Human</Name><Description>Line one.\nLine two.</Description></Race></ArrayOfRace>");
                Write(Path.Combine(data, "Resources.xml"), "<ArrayOfResource><Resource><ResourceId>1</ResourceId><Name>Argon</Name><Description>Gas.</Description></Resource></ArrayOfResource>");
                Write(Path.Combine(data, "Broken.xml"), "<ArrayOfRace><Race>");
                string mod = Path.Combine(root, "mod");
                Directory.CreateDirectory(mod);

                LocalizationResult result = LocalizationModBuilder.Build(mod, Directory.GetFiles(data, "*.xml").OrderBy(f => f).ToList());

                Assert.Equal(2, result.Files);
                Assert.Equal(2, result.Entities);
                Assert.Equal(4, result.Strings);
                Assert.Single(result.Unreadable);
                Assert.True(File.Exists(Path.Combine(mod, "patches", "Races.xml")));
                Assert.True(File.Exists(Path.Combine(mod, "patches", "Resources.xml")));
                string text = File.ReadAllText(Path.Combine(mod, "patches", "Races.xml"));
                Assert.Contains("<Race id=\"0\">", text);
                Assert.Contains("Line one.\nLine two.", text); // line breaks inside descriptions survive
            }
            finally { Directory.Delete(root, true); }
        }
    }
}
