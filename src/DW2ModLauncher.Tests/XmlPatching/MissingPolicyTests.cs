using System.Linq;
using System.Xml.Linq;
using DW2ModLauncher.XmlPatching;
using Xunit;

namespace DW2ModLauncher.Tests.XmlPatching
{
    // The missing="error|warn|ok" setting: what the widget shows for a patch item whose target is not there.
    // Resolution: patch (nearest element) > mod > default (warn); the developer override only escalates.
    public class MissingPolicyTests
    {
        private const string MissingEntity = "<Race id=\"99\"><Aggression>2</Aggression></Race>";

        private static PatchDiagnostic Unapplied(PatchTestKit kit)
        {
            kit.Apply(kit.Races(PatchTestKit.HumanRace));
            kit.Runner.Finish();
            return Assert.Single(kit.Report.Entries, e => e.Unapplied);
        }

        [Fact]
        public void Parse_Accepts_Only_The_Three_Words()
        {
            Assert.True(MissingPolicy.TryParse(" Warn ", out MissingLevel l));
            Assert.Equal(MissingLevel.Warn, l);
            Assert.True(MissingPolicy.TryParse("ERROR", out l));
            Assert.Equal(MissingLevel.Error, l);
            Assert.True(MissingPolicy.TryParse("ok", out l));
            Assert.Equal(MissingLevel.Ok, l);
            Assert.False(MissingPolicy.TryParse("silent", out _));
            Assert.False(MissingPolicy.TryParse("", out _));
            Assert.False(MissingPolicy.TryParse(null, out _));
        }

        [Fact]
        public void Escalate_Never_Lowers()
        {
            Assert.Equal(MissingLevel.Warn, MissingPolicy.Escalate(MissingLevel.Ok, MissingLevel.Warn));
            Assert.Equal(MissingLevel.Error, MissingPolicy.Escalate(MissingLevel.Error, MissingLevel.Warn));
            Assert.Equal(MissingLevel.Error, MissingPolicy.Escalate(MissingLevel.Warn, MissingLevel.Error));
            Assert.Equal(MissingLevel.Ok, MissingPolicy.Escalate(MissingLevel.Ok, null));
        }

        [Fact]
        public void Default_Is_A_Warning()
        {
            PatchDiagnostic d = Unapplied(new PatchTestKit().Patch(MissingEntity));
            Assert.Equal(MissingLevel.Warn, d.Level);
            Assert.Equal(Severity.Warning, d.Severity);
        }

        [Fact]
        public void Ok_Is_Still_Recorded_But_Not_Shown()
        {
            PatchTestKit kit = new PatchTestKit().Patch("<Race id=\"99\" missing=\"ok\"><Aggression>2</Aggression></Race>");
            PatchDiagnostic d = Unapplied(kit);
            Assert.Equal(MissingLevel.Ok, d.Level);
            Assert.Equal(Severity.Info, d.Severity);
            Assert.Contains("not applied", d.ToString());
            Assert.Equal(0, kit.Report.CountAt(MissingLevel.Warn) + kit.Report.CountAt(MissingLevel.Error));
            Assert.Equal(1, kit.Report.Skipped("patches/p.xml"));
        }

        [Fact]
        public void Error_Is_Red()
        {
            PatchTestKit kit = new PatchTestKit().Patch("<Race id=\"99\" missing=\"error\"><Aggression>2</Aggression></Race>");
            PatchDiagnostic d = Unapplied(kit);
            Assert.Equal(MissingLevel.Error, d.Level);
            Assert.Equal(Severity.Error, d.Severity);
            Assert.Equal(1, kit.Report.CountAt(MissingLevel.Error));
        }

        [Fact]
        public void A_Missing_List_Item_Uses_Its_Own_Setting_Over_The_Entity_s()
        {
            PatchTestKit kit = new PatchTestKit().Patch(
                "<Race id=\"0\" missing=\"error\"><Bonuses><Bonus id=\"PlagueCuring\" missing=\"ok\"><Amount>1</Amount></Bonus></Bonuses></Race>");
            Assert.Equal(MissingLevel.Ok, Unapplied(kit).Level);
        }

        [Fact]
        public void A_Missing_List_Item_Inherits_The_Entity_s_Setting()
        {
            PatchTestKit kit = new PatchTestKit().Patch(
                "<Race id=\"0\" missing=\"error\"><Bonuses><Bonus id=\"PlagueCuring\"><Amount>1</Amount></Bonus></Bonuses></Race>");
            Assert.Equal(MissingLevel.Error, Unapplied(kit).Level);
        }

        [Fact]
        public void The_Root_Element_Sets_The_Default_For_The_Whole_File()
        {
            PatchTestKit kit = new PatchTestKit();
            kit.Runner.AddFile("mod", "patches/p.xml", "<ArrayOfRace missing=\"ok\">" + MissingEntity + "</ArrayOfRace>");
            Assert.Equal(MissingLevel.Ok, Unapplied(kit).Level);
        }

        [Fact]
        public void The_Mod_Setting_Applies_When_The_Patch_Says_Nothing_And_Loses_To_The_Patch()
        {
            PatchTestKit silent = new PatchTestKit();
            silent.Runner.AddFile("mod", "patches/p.xml", "<ArrayOfRace>" + MissingEntity + "</ArrayOfRace>", modMissing: MissingLevel.Error);
            Assert.Equal(MissingLevel.Error, Unapplied(silent).Level);

            PatchTestKit explicitOk = new PatchTestKit();
            explicitOk.Runner.AddFile("mod", "patches/p.xml", "<ArrayOfRace><Race id=\"99\" missing=\"ok\"><Aggression>2</Aggression></Race></ArrayOfRace>", modMissing: MissingLevel.Error);
            Assert.Equal(MissingLevel.Ok, Unapplied(explicitOk).Level);
        }

        [Fact]
        public void The_Mod_Setting_Can_Be_Quieter_Than_The_Default()
        {
            PatchTestKit kit = new PatchTestKit();
            kit.Runner.AddFile("mod", "patches/p.xml", "<ArrayOfRace>" + MissingEntity + "</ArrayOfRace>", modMissing: MissingLevel.Ok);
            Assert.Equal(MissingLevel.Ok, Unapplied(kit).Level);
        }

        [Fact]
        public void The_Developer_Override_Raises_Ok_And_Warn()
        {
            PatchTestKit kit = new PatchTestKit();
            kit.Runner.DevOverride = MissingLevel.Error;
            kit.Patch("<Race id=\"99\" missing=\"ok\"><Aggression>2</Aggression></Race>");
            PatchDiagnostic d = Unapplied(kit);
            Assert.Equal(MissingLevel.Error, d.Level);
            Assert.Equal(Severity.Error, d.Severity);
        }

        [Fact]
        public void The_Developer_Override_Never_Mutes_An_Error()
        {
            PatchTestKit kit = new PatchTestKit();
            kit.Runner.DevOverride = MissingLevel.Warn;
            kit.Patch("<Race id=\"99\" missing=\"error\"><Aggression>2</Aggression></Race>");
            Assert.Equal(MissingLevel.Error, Unapplied(kit).Level);
        }

        [Fact]
        public void The_Developer_Override_Warn_Lifts_Ok_To_Warn()
        {
            PatchTestKit kit = new PatchTestKit();
            kit.Runner.DevOverride = MissingLevel.Warn;
            kit.Patch("<Race id=\"99\" missing=\"ok\"><Aggression>2</Aggression></Race>");
            Assert.Equal(MissingLevel.Warn, Unapplied(kit).Level);
        }

        [Fact]
        public void A_Patch_That_Did_Nothing_Because_Its_Data_Never_Loaded_Follows_The_Setting()
        {
            PatchTestKit kit = new PatchTestKit();
            kit.Runner.AddFile("mod", "patches/p.xml", "<ArrayOfRace missing=\"error\"><Race id=\"0\"><Aggression>2</Aggression></Race></ArrayOfRace>");
            kit.Runner.Finish();
            PatchDiagnostic d = Assert.Single(kit.Report.Entries);
            Assert.True(d.Unapplied);
            Assert.Equal(MissingLevel.Error, d.Level);
        }

        [Fact]
        public void A_Bad_Value_On_An_Element_Is_An_Error_And_The_Element_Is_Not_Applied()
        {
            PatchTestKit kit = new PatchTestKit().Patch("<Race id=\"0\" missing=\"silent\"><Aggression>2</Aggression></Race>");
            XDocument d = kit.Apply(kit.Races(PatchTestKit.HumanRace));
            Assert.Equal("1.2", d.Root.Element("Race").Element("Aggression").Value);
            Assert.Contains("missing=\"silent\" is not valid", Assert.Single(kit.Errors()));
        }

        [Fact]
        public void A_Bad_Value_On_The_Root_Rejects_The_File()
        {
            PatchRunner runner = new PatchRunner(_ => null, KeyMap.Default);
            Assert.False(runner.AddFile("mod", "patches/p.xml", "<ArrayOfRace missing=\"maybe\"/>"));
            Assert.Contains("missing=\"maybe\" is not valid", Assert.Single(runner.Report.Entries).Message);
            Assert.Empty(runner.Files);
        }

        [Fact]
        public void Missing_Is_Not_Allowed_Inside_Added_Or_Replaced_Content()
        {
            PatchTestKit kit = new PatchTestKit().Patch(
                "<Race id=\"0\"><Bonuses><Bonus op=\"add\"><Type>ShipSpeed</Type><Amount missing=\"ok\">1</Amount></Bonus></Bonuses></Race>");
            kit.Runner.ValidateAll();
            Assert.Contains("not allowed inside a replaced or added element", Assert.Single(kit.Errors()));
        }

        [Fact]
        public void A_Problem_In_The_Patch_Is_Amber_Unless_The_Override_Raises_It()
        {
            PatchTestKit amber = new PatchTestKit().Patch("<Race id=\"0\"><Agression>1</Agression></Race>");
            amber.Runner.ValidateAll();
            Assert.Equal(MissingLevel.Warn, Assert.Single(amber.Report.Entries).Level);

            PatchTestKit red = new PatchTestKit();
            red.Runner.DevOverride = MissingLevel.Error;
            red.Patch("<Race id=\"0\"><Agression>1</Agression></Race>");
            red.Runner.ValidateAll();
            Assert.Equal(MissingLevel.Error, Assert.Single(red.Report.Entries).Level);
        }

        [Fact]
        public void Applied_Changes_Never_Show_On_The_Widget()
        {
            PatchTestKit kit = new PatchTestKit();
            kit.Runner.DevOverride = MissingLevel.Error;
            kit.Patch("<Race id=\"0\"><Aggression>2</Aggression></Race>");
            kit.Apply(kit.Races(PatchTestKit.HumanRace));
            kit.Runner.Finish();
            Assert.All(kit.Report.Entries, e => Assert.Equal(MissingLevel.Ok, e.Level));
            Assert.Equal(0, kit.Report.CountAt(MissingLevel.Error));
        }
    }
}
