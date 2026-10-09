using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using DW2ModLauncher.XmlPatching;
using Xunit;

namespace DW2ModLauncher.Tests.XmlPatching
{
    public class LocalizationPatchGeneratorTests
    {
        private static Dictionary<string, LocalizationFile> Generate(params string[] files)
        {
            LocalizationPatchGenerator g = new LocalizationPatchGenerator();
            foreach (string f in files) g.AddData(XDocument.Parse(f));
            return g.Generate();
        }

        private const string Races =
            "<ArrayOfRace><Race><RaceId>0</RaceId><BundleName>Human</BundleName><Name>Human</Name><Description>Humans are adaptable.</Description><Aggression>1.2</Aggression>" +
            "<CharacterFirstNames><string>Donato</string></CharacterFirstNames></Race></ArrayOfRace>";

        [Fact]
        public void Collects_Display_Text_Only_And_Selects_The_Entity_By_Id()
        {
            XElement race = Generate(Races)["Race.xml"].Document.Root.Element("Race");

            Assert.Equal("0", race.Attribute("id").Value);
            Assert.Equal("Human", race.Element("Name").Value);
            Assert.Equal("Humans are adaptable.", race.Element("Description").Value);
            Assert.Null(race.Element("RaceId"));      // the key is the id, never text
            Assert.Null(race.Element("BundleName"));  // not on the allow-list
            Assert.Null(race.Element("Aggression"));
            Assert.Equal("Donato", race.Element("CharacterFirstNames").Element("string").Value); // name pools are display text too
        }

        [Fact]
        public void An_Entitys_Key_Is_Never_Collected_Even_When_It_Is_A_Name()
        {
            // GameEvent: Name is the identifier, MessageTitle/Description are what the player reads. References by name stay untouched.
            string events = "<ArrayOfGameEvent><GameEvent><Name>Home Ruin</Name><TriggerActions>" +
                            "<GameEventAction><Type>GeneralStoryMessageToEmpire</Type><MessageTitle>Expand and Exploit!</MessageTitle><Description>At long last.</Description><GeneratedItemName>Ruin 1</GeneratedItemName><ActionLocationItemName>Ruin 2</ActionLocationItemName></GameEventAction>" +
                            "<GameEventAction><Type>ResearchProgress</Type><Id1>5</Id1></GameEventAction>" +
                            "<GameEventAction><Type>X</Type><ChoiceButtonText>Okay</ChoiceButtonText></GameEventAction>" +
                            "</TriggerActions></GameEvent></ArrayOfGameEvent>";
            XElement ev = Generate(events)["GameEvent.xml"].Document.Root.Element("GameEvent");

            Assert.Equal("Home Ruin", ev.Attribute("id").Value);
            Assert.Null(ev.Element("Name"));
            XElement[] actions = ev.Element("TriggerActions").Elements("GameEventAction").ToArray();
            Assert.Equal(2, actions.Length);                          // the action without text is left out
            Assert.Equal("1", actions[0].Attribute("index").Value);   // unkeyed list: by position (1-based)
            Assert.Equal("3", actions[1].Attribute("index").Value);   // positions are those of the original list
            Assert.Equal("Expand and Exploit!", actions[0].Element("MessageTitle").Value);
            Assert.Null(actions[0].Element("GeneratedItemName"));     // references to other entities by name
            Assert.Null(actions[0].Element("ActionLocationItemName"));
            Assert.Null(actions[0].Element("Type"));
        }

        [Fact]
        public void Keyed_List_Items_Are_Selected_By_Id_Unless_The_Keys_Repeat()
        {
            string uniqueKeys = "<ArrayOfRace><Race><RaceId>0</RaceId><Name>H</Name><DiplomacyFactors>" +
                                "<EmpireIncidentFactor><Description>a</Description></EmpireIncidentFactor><EmpireIncidentFactor><Description>b</Description></EmpireIncidentFactor></DiplomacyFactors></Race></ArrayOfRace>";
            XElement items = Generate(uniqueKeys)["Race.xml"].Document.Root.Element("Race").Element("DiplomacyFactors");
            Assert.Equal(new[] { "1", "2" }, items.Elements().Select(e => e.Attribute("index").Value)); // EmpireIncidentFactor is not keyed

            string keyed = "<ArrayOfRace><Race><RaceId>0</RaceId><Name>H</Name><Bonuses>" +
                           "<Bonus><Type>A</Type><Description>x</Description></Bonus><Bonus><Type>B</Type><Description>y</Description></Bonus></Bonuses></Race></ArrayOfRace>";
            Assert.Equal(new[] { "A", "B" }, Generate(keyed)["Race.xml"].Document.Root.Element("Race").Element("Bonuses").Elements().Select(e => e.Attribute("id").Value));

            string repeated = keyed.Replace("<Type>B</Type>", "<Type>A</Type>");
            Assert.Equal(new[] { "1", "2" }, Generate(repeated)["Race.xml"].Document.Root.Element("Race").Element("Bonuses").Elements().Select(e => e.Attribute("index").Value));
        }

        [Fact]
        public void A_Later_File_Replaces_An_Entity_As_The_Game_Does()
        {
            string later = "<ArrayOfRace><Race><RaceId>0</RaceId><Name>Terran</Name></Race><Race><RaceId>1</RaceId><Name>Other</Name></Race></ArrayOfRace>";
            XElement[] races = Generate(Races, later)["Race.xml"].Document.Root.Elements("Race").ToArray();

            Assert.Equal(2, races.Length);
            Assert.Equal("Terran", races[0].Element("Name").Value);   // replaced, keeps its position
            Assert.Null(races[0].Element("Description"));              // the whole entity was replaced, not merged
        }

        [Fact]
        public void Internal_Names_Entity_Types_Without_Text_And_Unknown_Roots_Are_Skipped()
        {
            LocalizationPatchGenerator g = new LocalizationPatchGenerator();
            g.AddData(XDocument.Parse("<ArrayOfCharacterRoom><CharacterRoom><RoomId>1</RoomId><Name>Atuuk_Default</Name></CharacterRoom></ArrayOfCharacterRoom>"));
            g.AddData(XDocument.Parse("<ArrayOfFoo><Foo><Name>x</Name></Foo></ArrayOfFoo>"));
            g.AddData(XDocument.Parse("<NotAData/>"));

            Assert.Empty(g.Generate());
            Assert.Equal(new[] { "ArrayOfFoo" }, g.SkippedRoots.ToArray());
        }

        [Fact]
        public void Lists_Of_Display_Strings_Are_Written_As_A_Whole_List_To_Replace()
        {
            XElement gov = Generate("<ArrayOfGovernment><Government><GovernmentId>1</GovernmentId><Name>Monarchy</Name><FeatureExplanations><string>[HL]Spirit:[/HL] text</string></FeatureExplanations>" +
                                    "<AlternateFlagFilenames><string>Flags/A</string></AlternateFlagFilenames></Government></ArrayOfGovernment>")["Government.xml"].Document.Root.Element("Government");

            XElement list = gov.Element("FeatureExplanations");
            Assert.Equal("replace", list.Attribute("op").Value);
            Assert.Equal("[HL]Spirit:[/HL] text", list.Element("string").Value);
            Assert.Null(gov.Element("AlternateFlagFilenames")); // file names are not text
        }

        [Fact]
        public void Army_And_Fleet_Template_Names_Are_Collected_Because_The_Game_Lists_Them()
        {
            Dictionary<string, LocalizationFile> result = Generate(
                "<ArrayOfArmyTemplate><ArmyTemplate><ArmyTemplateId>0</ArmyTemplateId><Name>Invasion Army</Name></ArmyTemplate></ArrayOfArmyTemplate>",
                "<ArrayOfFleetTemplate><FleetTemplate><FleetTemplateId>3</FleetTemplateId><Name>Attack Fleet</Name></FleetTemplate></ArrayOfFleetTemplate>");

            Assert.Equal("Invasion Army", result["ArmyTemplate.xml"].Document.Root.Element("ArmyTemplate").Element("Name").Value);
            Assert.Equal("3", result["FleetTemplate.xml"].Document.Root.Element("FleetTemplate").Attribute("id").Value);
        }


        [Fact]
        public void Output_Follows_The_Source_Files_And_Lists_An_Entity_Only_Where_Its_Winning_Definition_Is()
        {
            string basic = "<ArrayOfRace><Race><RaceId>0</RaceId><Name>Human</Name></Race><Race><RaceId>1</RaceId><Name>Ackdarian</Name></Race></ArrayOfRace>";
            string extra = "<ArrayOfRace><Race><RaceId>1</RaceId><Name>Ackdarian 2</Name></Race><Race><RaceId>5</RaceId><Name>Ikkuro</Name></Race></ArrayOfRace>";
            LocalizationPatchGenerator g = new LocalizationPatchGenerator();
            g.AddData(XDocument.Parse(basic), "Races.xml");
            g.AddData(XDocument.Parse(extra), "Races_Ikkuro.xml");
            Dictionary<string, LocalizationFile> files = g.Generate();

            Assert.Equal(new[] { "Races.xml", "Races_Ikkuro.xml" }, files.Keys.ToArray());
            Assert.Equal(new[] { "0" }, files["Races.xml"].Document.Root.Elements("Race").Select(r => r.Attribute("id").Value).ToArray());
            Assert.Equal(new[] { "1", "5" }, files["Races_Ikkuro.xml"].Document.Root.Elements("Race").Select(r => r.Attribute("id").Value).ToArray());
            Assert.Equal("Ackdarian 2", files["Races_Ikkuro.xml"].Document.Root.Elements("Race").First().Element("Name").Value);
        }

        [Fact]
        public void A_File_Without_Any_Winning_Text_Is_Not_Written()
        {
            LocalizationPatchGenerator g = new LocalizationPatchGenerator();
            g.AddData(XDocument.Parse("<ArrayOfRace><Race><RaceId>1</RaceId><Name>Old</Name></Race></ArrayOfRace>"), "Races.xml");
            g.AddData(XDocument.Parse("<ArrayOfRace><Race><RaceId>1</RaceId><Name>New</Name></Race></ArrayOfRace>"), "Races_Later.xml");
            Assert.Equal(new[] { "Races_Later.xml" }, g.Generate().Keys.ToArray());
        }
        [Fact]
        public void Translating_Text_And_Applying_It_Changes_Only_That_Text()
        {
            PatchTestKit kit = new PatchTestKit();
            XDocument patch = Generate("<ArrayOfRace>" + PatchTestKit.HumanRace.Replace("<Race>", "<Race>") + "</ArrayOfRace>")["Race.xml"].Document;
            patch.Root.Element("Race").Element("Name").Value = "Humain";
            kit.Runner.AddFile("loc", "patches/Race.xml", patch.ToString());

            XDocument d = kit.Apply(kit.Races(PatchTestKit.HumanRace));
            XElement race = d.Root.Element("Race");
            Assert.Equal("Humain", race.Element("Name").Value);
            Assert.Equal("1.2", race.Element("Aggression").Value);
            Assert.Empty(kit.Errors());
        }
    }
}
