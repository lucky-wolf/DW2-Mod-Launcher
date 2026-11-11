using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using DW2ModLauncher.Core.Models;
using DW2ModLauncher.Core.Services;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class DisplayChoicesTests
    {
        private static DisplayInfo Display(string monitor, string adapter, int x, int w, int h, bool primary = false)
        {
            return new DisplayInfo { DeviceName = "DISPLAY" + (x + 1), MonitorName = monitor, AdapterName = adapter, X = x, Y = 0, Width = w, Height = h, IsPrimary = primary };
        }

        [Fact]
        public void Labels_NameTheMonitorAndItsCard_AndNumberARepeat()
        {
            List<string> labels = DisplayChoices.Labels(new List<DisplayInfo>
            {
                Display("DELL U2720Q", "NVIDIA RTX", 0, 2560, 1440, true),
                Display("DELL U2720Q", "NVIDIA RTX", 2560, 2560, 1440),
                Display("", "", 5120, 1920, 1080)
            });
            Assert.Equal(new[] { "DELL U2720Q 2560x1440 on NVIDIA RTX", "DELL U2720Q 2560x1440 on NVIDIA RTX (2)", "Monitor 1920x1080" }, labels);
        }

        [Fact]
        public void Options_PutTheSchemasOwnFirst()
        {
            List<string> options = DisplayChoices.Options(new[] { "Automatic" }, new List<DisplayInfo> { Display("A", "B", 0, 800, 600) });
            Assert.Equal(new[] { "Automatic", "A 800x600 on B" }, options);
        }

        [Fact]
        public void Find_MatchesALabel_AndNeverSubstitutesAnotherMonitor()
        {
            List<DisplayInfo> displays = new List<DisplayInfo> { Display("A", "B", 0, 800, 600), Display("C", "D", 800, 1024, 768) };
            Assert.Same(displays[1], DisplayChoices.Find("c 1024x768 on d", displays));
            Assert.Null(DisplayChoices.Find("E on F", displays));
            Assert.Null(DisplayChoices.Find("", displays));
        }

        [Fact]
        public void Resolve_GivesTheChosenMonitorsOwnRectCardAndRefreshRate_AndAProblemForOneThatIsGone()
        {
            DisplayInfo main = Display("A", "Card1", 0, 5120, 1440, true);
            main.RefreshRate = 60;
            DisplayInfo second = Display("B", "Card2", 5120, 2560, 1440);
            second.RefreshRate = 144;
            List<DisplayInfo> displays = new List<DisplayInfo> { main, second };

            Assert.Null(DisplayChoices.Resolve("B 2560x1440 on Card2", displays, out string rect, out string adapter, out int refresh));
            Assert.Equal("5120,0,2560,1440", rect);
            Assert.Equal("Card2", adapter);
            Assert.Equal(144, refresh);

            Assert.Null(DisplayChoices.Resolve("", displays, out rect, out adapter, out refresh));
            Assert.Equal("", rect);
            Assert.Equal(0, refresh);

            string problem = DisplayChoices.Resolve("Gone on Nothing", displays, out rect, out adapter, out refresh);
            Assert.Contains("Gone on Nothing", problem);
            Assert.Equal("", rect);
            Assert.Equal(0, refresh);
        }
    }
}
