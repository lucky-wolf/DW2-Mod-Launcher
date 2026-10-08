using System.Linq;
using System.Xml.Linq;
using DW2ModLauncher.XmlPatching;
using Xunit;

namespace DW2ModLauncher.Tests.XmlPatching
{
    public class PatchRunnerTests
    {
        private static XElement Race0(XDocument d)
        {
            return d.Root.Elements("Race").First(r => r.Element("RaceId").Value == "0");
        }

        private static XElement Cannon(XDocument d)
        {
            return d.Root.Elements("ComponentDefinition").First(c => c.Element("ComponentId").Value == "6");
        }

        // ---- scalars and structs ----

        [Fact]
        public void Scalar_Overrides_And_Leaves_The_Rest_Alone()
        {
            PatchTestKit kit = new PatchTestKit().Patch("<Race id=\"0\"><Aggression>1.5</Aggression></Race>");
            XDocument d = kit.Apply(kit.Races(PatchTestKit.HumanRace));
            kit.Runner.Finish();

            Assert.Equal("1.5", Race0(d).Element("Aggression").Value);
            Assert.Equal("Human", Race0(d).Element("Name").Value);
            Assert.Empty(kit.Errors());
            Assert.Equal(1, kit.Report.Applied("patches/p.xml"));
        }

        [Fact]
        public void Absent_Scalar_Is_Created()
        {
            PatchTestKit kit = new PatchTestKit().Patch(
                "<ComponentDefinition id=\"6\"><Values><ComponentStats index=\"1\"><WeaponBombardDamageInfrastructure>2</WeaponBombardDamageInfrastructure></ComponentStats></Values></ComponentDefinition>",
                root: "ArrayOfComponentDefinition");
            XDocument d = kit.Apply(kit.Components(PatchTestKit.Cannon));

            Assert.Equal("2", PatchTestKit.Text(Cannon(d), "Values/ComponentStats[1]/WeaponBombardDamageInfrastructure"));
            Assert.Null(PatchTestKit.Text(Cannon(d), "Values/ComponentStats[2]/WeaponBombardDamageInfrastructure"));
        }

        [Fact]
        public void Same_Value_Counts_As_Unchanged_Even_When_Written_Differently()
        {
            PatchTestKit kit = new PatchTestKit().Patch("<Race id=\"0\"><Aggression>1.20</Aggression></Race>");
            kit.Apply(kit.Races(PatchTestKit.HumanRace));

            Assert.Equal(0, kit.Report.Applied("patches/p.xml"));
            Assert.Equal(1, kit.Report.Unchanged("patches/p.xml"));
        }

        [Fact]
        public void Struct_Merges_Field_By_Field()
        {
            PatchTestKit kit = new PatchTestKit().Patch(
                "<ComponentDefinition id=\"6\"><WeaponEffect><StaticTextureFilepath>Effects/Weapons/GreenLaser1</StaticTextureFilepath>" +
                "<TextureTintColor><G>255</G><R>0</R><B>0</B></TextureTintColor><BodyScaling><X>2</X></BodyScaling></WeaponEffect></ComponentDefinition>",
                root: "ArrayOfComponentDefinition");
            XDocument d = kit.Apply(kit.Components(PatchTestKit.Cannon));
            XElement fx = Cannon(d).Element("WeaponEffect");

            Assert.Equal("Effects/Weapons/GreenLaser1", fx.Element("StaticTextureFilepath").Value);
            Assert.Equal("0", fx.Element("TextureTintColor").Element("R").Value);
            Assert.Equal("255", fx.Element("TextureTintColor").Element("G").Value);
            Assert.Equal("255", fx.Element("TextureTintColor").Element("A").Value); // not mentioned: untouched
            Assert.Equal("2", fx.Element("BodyScaling").Element("X").Value);
            Assert.Equal("1.5", fx.Element("BodyScaling").Element("Y").Value);
        }

        [Fact]
        public void Absent_Struct_Is_Created_From_The_Written_Fields()
        {
            PatchTestKit kit = new PatchTestKit().Patch("<Race id=\"0\"><MainColor><R>9</R></MainColor></Race>");
            XDocument d = kit.Apply(kit.Races("<Race><RaceId>0</RaceId></Race>"));

            Assert.Equal("9", Race0(d).Element("MainColor").Element("R").Value);
        }

        [Fact]
        public void Remove_Field_Drops_It()
        {
            PatchTestKit kit = new PatchTestKit().Patch("<Race id=\"0\"><Aggression op=\"remove\"/><Name op=\"remove\"/></Race>");
            XDocument d = kit.Apply(kit.Races(PatchTestKit.HumanRace));

            Assert.Null(Race0(d).Element("Aggression"));
            Assert.Null(Race0(d).Element("Name"));
            Assert.Equal(2, kit.Report.Applied("patches/p.xml"));
        }

        [Fact]
        public void Removing_An_Absent_Field_Is_Unchanged()
        {
            PatchTestKit kit = new PatchTestKit().Patch("<Race id=\"0\"><Aggression op=\"remove\"/></Race>");
            kit.Apply(kit.Races("<Race><RaceId>0</RaceId></Race>"));
            Assert.Equal(1, kit.Report.Unchanged("patches/p.xml"));
        }

        [Fact]
        public void Replace_Struct_Rewrites_It_With_Defaults_For_What_Is_Left_Out()
        {
            PatchTestKit kit = new PatchTestKit().Patch(
                "<ComponentDefinition id=\"6\"><WeaponEffect op=\"replace\"><StaticTextureFilepath>New</StaticTextureFilepath></WeaponEffect></ComponentDefinition>",
                root: "ArrayOfComponentDefinition");
            XDocument d = kit.Apply(kit.Components(PatchTestKit.Cannon));
            XElement fx = Cannon(d).Element("WeaponEffect");

            Assert.Equal("New", fx.Element("StaticTextureFilepath").Value);
            Assert.Null(fx.Element("TextureTintColor"));
            Assert.Null(fx.Element("BodyScaling"));
        }

        // ---- entities ----

        [Fact]
        public void Entity_Is_Matched_By_Id_Numerically()
        {
            PatchTestKit kit = new PatchTestKit().Patch("<Race id=\"00\"><Aggression>2</Aggression></Race>");
            XDocument d = kit.Apply(kit.Races(PatchTestKit.HumanRace));
            Assert.Equal("2", Race0(d).Element("Aggression").Value);
        }

        [Fact]
        public void Entity_Is_Patched_In_Every_File_That_Defines_It()
        {
            PatchTestKit kit = new PatchTestKit().Patch("<Race id=\"0\"><Aggression>2</Aggression></Race>");
            XDocument first = kit.Apply(kit.Races(PatchTestKit.HumanRace), "/data/Races.xml");
            XDocument second = kit.Apply(kit.Races("<Race><RaceId>0</RaceId><Aggression>9</Aggression></Race>"), "/mods/x/Races_X.xml");
            kit.Runner.Finish();

            Assert.Equal("2", Race0(first).Element("Aggression").Value);
            Assert.Equal("2", Race0(second).Element("Aggression").Value);
            Assert.Empty(kit.Errors());
        }

        [Fact]
        public void Entity_Found_In_Only_One_Of_Several_Files_Is_Not_An_Error()
        {
            PatchTestKit kit = new PatchTestKit().Patch("<Race id=\"5\"><Aggression>2</Aggression></Race>");
            kit.Apply(kit.Races(PatchTestKit.HumanRace), "/data/Races.xml");
            kit.Apply(kit.Races("<Race><RaceId>5</RaceId></Race>"), "/data/Races_Atuuk.xml");
            kit.Runner.Finish();
            Assert.Empty(kit.Errors());
        }

        [Fact]
        public void Entity_Found_Nowhere_Is_Reported_With_A_Hint()
        {
            PatchTestKit kit = new PatchTestKit().Patch("<Race id=\"99\"><Aggression>2</Aggression></Race>");
            kit.Apply(kit.Races("<Race><RaceId>0</RaceId></Race><Race><RaceId>9</RaceId></Race>"));
            kit.Runner.Finish();

            string error = Assert.Single(kit.Errors());
            Assert.Contains("Race id=99 not found", error);
            Assert.Contains("did you mean '9'", error);
            Assert.Equal(1, kit.Report.Skipped("patches/p.xml"));
        }

        [Fact]
        public void Remove_Entity_Takes_It_Out_Of_The_File()
        {
            PatchTestKit kit = new PatchTestKit().Patch("<Race id=\"9\" op=\"remove\"/>");
            XDocument d = kit.Apply(kit.Races("<Race><RaceId>0</RaceId></Race><Race><RaceId>9</RaceId></Race>"));

            Assert.Single(d.Root.Elements("Race"));
            Assert.Equal("0", d.Root.Element("Race").Element("RaceId").Value);
        }

        [Fact]
        public void Entity_Without_Id_Is_Rejected_But_Others_Still_Apply()
        {
            PatchTestKit kit = new PatchTestKit().Patch("<Race><RaceId>0</RaceId><Aggression>3</Aggression></Race><Race id=\"0\"><Name>Terran</Name></Race>");
            XDocument d = kit.Apply(kit.Races(PatchTestKit.HumanRace));

            Assert.Contains("needs id=", Assert.Single(kit.Errors()));
            Assert.Equal("Terran", Race0(d).Element("Name").Value);
            Assert.Equal("1.2", Race0(d).Element("Aggression").Value);
        }

        [Theory]
        [InlineData("<Race id=\"0\" op=\"replace\"/>", "only remove")]
        [InlineData("<Race id=\"0\" index=\"1\"/>", "list items only")]
        [InlineData("<Race id=\"0\" op=\"add\"/>", "only remove")]
        public void Entity_Level_Misuse_Is_Rejected(string xml, string expected)
        {
            PatchTestKit kit = new PatchTestKit().Patch(xml);
            kit.Runner.ValidateAll();
            Assert.Contains(expected, Assert.Single(kit.Errors()));
        }

        // ---- list items: keyed, positional ----

        [Fact]
        public void Keyed_Item_Is_Edited_By_Id()
        {
            PatchTestKit kit = new PatchTestKit().Patch(
                "<ComponentDefinition id=\"6\"><ResourcesRequired><ResourceQuantity id=\"8\"><Amount>6</Amount></ResourceQuantity></ResourcesRequired></ComponentDefinition>",
                root: "ArrayOfComponentDefinition");
            XDocument d = kit.Apply(kit.Components(PatchTestKit.Cannon));

            Assert.Equal(new[] { "6", "4" }, PatchTestKit.Values(Cannon(d), "ResourcesRequired/ResourceQuantity/Amount"));
        }

        [Fact]
        public void Keyed_Item_That_Does_Not_Exist_Lists_What_Does()
        {
            PatchTestKit kit = new PatchTestKit().Patch(
                "<ComponentDefinition id=\"6\"><ResourcesRequired><ResourceQuantity id=\"80\"><Amount>6</Amount></ResourceQuantity></ResourcesRequired></ComponentDefinition>",
                root: "ArrayOfComponentDefinition");
            kit.Apply(kit.Components(PatchTestKit.Cannon));
            kit.Runner.Finish();

            string error = Assert.Single(kit.Errors());
            Assert.Contains("no such item (has: 8, 9)", error);
        }

        [Fact]
        public void Ambiguous_Id_Names_The_Candidates()
        {
            PatchTestKit kit = new PatchTestKit().Patch(
                "<Race id=\"0\"><Bonuses><Bonus id=\"PlagueCuring\"><Amount>1</Amount></Bonus></Bonuses></Race>");
            kit.Apply(kit.Races("<Race><RaceId>0</RaceId><Bonuses><Bonus><Type>PlagueCuring</Type><Amount>1</Amount></Bonus><Bonus><Type>PlagueCuring</Type><Amount>0.05</Amount></Bonus></Bonuses></Race>"));
            kit.Runner.Finish();

            Assert.Contains("id matches items 1, 2; use index", Assert.Single(kit.Errors()));
        }

        [Fact]
        public void Positional_Item_Is_Edited_By_One_Based_Index()
        {
            PatchTestKit kit = new PatchTestKit().Patch(
                "<ComponentDefinition id=\"6\"><Values><ComponentStats index=\"2\"><WeaponRawDamage>14</WeaponRawDamage></ComponentStats></Values></ComponentDefinition>",
                root: "ArrayOfComponentDefinition");
            XDocument d = kit.Apply(kit.Components(PatchTestKit.Cannon));

            Assert.Equal(new[] { "10", "14" }, PatchTestKit.Values(Cannon(d), "Values/ComponentStats/WeaponRawDamage"));
        }

        [Fact]
        public void Index_Out_Of_Range_Is_Reported_With_The_Valid_Range_And_Never_Creates()
        {
            PatchTestKit kit = new PatchTestKit().Patch(
                "<ComponentDefinition id=\"6\"><Values><ComponentStats index=\"3\"><WeaponRawDamage>14</WeaponRawDamage></ComponentStats></Values></ComponentDefinition>",
                root: "ArrayOfComponentDefinition");
            XDocument d = kit.Apply(kit.Components(PatchTestKit.Cannon));
            kit.Runner.Finish();

            Assert.Contains("list has 2 item(s) (valid: 1..2)", Assert.Single(kit.Errors()));
            Assert.Equal(2, Cannon(d).Element("Values").Elements().Count());
        }

        [Fact]
        public void Index_Zero_Is_Rejected_Because_Indexes_Start_At_One()
        {
            PatchTestKit kit = new PatchTestKit().Patch(
                "<ComponentDefinition id=\"6\"><Values><ComponentStats index=\"0\"><WeaponRawDamage>14</WeaponRawDamage></ComponentStats></Values></ComponentDefinition>",
                root: "ArrayOfComponentDefinition");
            kit.Runner.ValidateAll();
            Assert.Contains("starting at 1", Assert.Single(kit.Errors()));
        }

        [Fact]
        public void A_Bare_List_Item_Is_Rejected_Whatever_The_Data_Holds()
        {
            PatchTestKit kit = new PatchTestKit().Patch(
                "<ComponentDefinition id=\"6\"><Values><ComponentStats><WeaponRawDamage>14</WeaponRawDamage></ComponentStats></Values></ComponentDefinition>",
                root: "ArrayOfComponentDefinition");
            kit.Runner.ValidateAll();

            string error = Assert.Single(kit.Errors());
            Assert.Contains("is a list item of <Values>", error);
            Assert.Contains("index=\"N\" or op=\"add\"", error);
        }

        [Fact]
        public void A_Bare_Keyed_Item_Suggests_Its_Key()
        {
            PatchTestKit kit = new PatchTestKit().Patch(
                "<ComponentDefinition id=\"6\"><ResourcesRequired><ResourceQuantity><Amount>6</Amount></ResourceQuantity></ResourcesRequired></ComponentDefinition>",
                root: "ArrayOfComponentDefinition");
            kit.Runner.ValidateAll();
            Assert.Contains("id=\"ResourceId value\"", Assert.Single(kit.Errors()));
        }

        [Fact]
        public void Id_On_An_Unkeyed_Item_Says_To_Use_Index()
        {
            PatchTestKit kit = new PatchTestKit().Patch(
                "<ComponentDefinition id=\"6\"><Values><ComponentStats id=\"0.6\"><WeaponRawDamage>14</WeaponRawDamage></ComponentStats></Values></ComponentDefinition>",
                root: "ArrayOfComponentDefinition");
            kit.Runner.ValidateAll();
            Assert.Contains("has no id (it is not keyed); use index", Assert.Single(kit.Errors()));
        }

        [Fact]
        public void Id_And_Index_Together_Are_Rejected()
        {
            PatchTestKit kit = new PatchTestKit().Patch(
                "<Race id=\"0\"><Bonuses><Bonus id=\"ShipSpeed\" index=\"2\"><Amount>1</Amount></Bonus></Bonuses></Race>");
            kit.Runner.ValidateAll();
            Assert.Contains("either id or index", Assert.Single(kit.Errors()));
        }

        // ---- add / remove / replace on list items and lists ----

        [Fact]
        public void Add_Appends_The_Item_As_Written()
        {
            PatchTestKit kit = new PatchTestKit().Patch(
                "<ComponentDefinition id=\"6\"><Values><ComponentStats op=\"add\"><CrewRequirement>5</CrewRequirement><WeaponRawDamage>40</WeaponRawDamage><WeaponRange>1600</WeaponRange></ComponentStats></Values></ComponentDefinition>",
                root: "ArrayOfComponentDefinition");
            XDocument d = kit.Apply(kit.Components(PatchTestKit.Cannon));
            XElement added = Cannon(d).Element("Values").Elements().Last();

            Assert.Equal(3, Cannon(d).Element("Values").Elements().Count());
            Assert.Equal("40", added.Element("WeaponRawDamage").Value);
            Assert.Null(added.Element("ComponentCountermeasuresBonus")); // not inherited: written as is
        }

        [Fact]
        public void Add_With_Index_Inserts_At_That_Position()
        {
            PatchTestKit kit = new PatchTestKit().Patch(
                "<ComponentDefinition id=\"6\"><Values><ComponentStats op=\"add\" index=\"2\"><WeaponRawDamage>12</WeaponRawDamage></ComponentStats></Values></ComponentDefinition>",
                root: "ArrayOfComponentDefinition");
            XDocument d = kit.Apply(kit.Components(PatchTestKit.Cannon));

            Assert.Equal(new[] { "10", "12", "15" }, PatchTestKit.Values(Cannon(d), "Values/ComponentStats/WeaponRawDamage"));
        }

        [Fact]
        public void Add_With_Index_One_Past_The_End_Appends_And_Further_Is_An_Error()
        {
            PatchTestKit kit = new PatchTestKit().Patch(
                "<ComponentDefinition id=\"6\"><Values><ComponentStats op=\"add\" index=\"3\"><WeaponRawDamage>20</WeaponRawDamage></ComponentStats>" +
                "<ComponentStats op=\"add\" index=\"9\"><WeaponRawDamage>30</WeaponRawDamage></ComponentStats></Values></ComponentDefinition>",
                root: "ArrayOfComponentDefinition");
            XDocument d = kit.Apply(kit.Components(PatchTestKit.Cannon));
            kit.Runner.Finish();

            Assert.Equal(new[] { "10", "15", "20" }, PatchTestKit.Values(Cannon(d), "Values/ComponentStats/WeaponRawDamage"));
            Assert.Contains("valid positions are 1..4", Assert.Single(kit.Errors()));
        }

        [Fact]
        public void Add_With_Id_Is_Rejected()
        {
            PatchTestKit kit = new PatchTestKit().Patch(
                "<Race id=\"0\"><Bonuses><Bonus op=\"add\" id=\"ShipSpeed\"><Amount>1</Amount></Bonus></Bonuses></Race>");
            kit.Runner.ValidateAll();
            Assert.Contains("cannot have id", Assert.Single(kit.Errors()));
        }

        [Fact]
        public void Added_Keyed_Item_Carries_Its_Key_As_An_Element()
        {
            PatchTestKit kit = new PatchTestKit().Patch(
                "<ComponentDefinition id=\"6\"><ResourcesRequired><ResourceQuantity op=\"add\"><ResourceId>13</ResourceId><Amount>2</Amount></ResourceQuantity></ResourcesRequired></ComponentDefinition>",
                root: "ArrayOfComponentDefinition");
            XDocument d = kit.Apply(kit.Components(PatchTestKit.Cannon));

            Assert.Equal(new[] { "8", "9", "13" }, PatchTestKit.Values(Cannon(d), "ResourcesRequired/ResourceQuantity/ResourceId"));
        }

        [Fact]
        public void Add_Creates_The_List_When_It_Is_Absent()
        {
            PatchTestKit kit = new PatchTestKit().Patch(
                "<Race id=\"0\"><Bonuses><Bonus op=\"add\"><Type>ShipSpeed</Type><Amount>1</Amount></Bonus></Bonuses></Race>");
            XDocument d = kit.Apply(kit.Races("<Race><RaceId>0</RaceId></Race>"));
            Assert.Equal("ShipSpeed", PatchTestKit.Text(Race0(d), "Bonuses/Bonus/Type"));
        }

        [Fact]
        public void Remove_Item_By_Id_And_By_Index()
        {
            PatchTestKit kit = new PatchTestKit().Patch(
                "<Race id=\"0\"><Bonuses><Bonus id=\"ResearchAll\" op=\"remove\"/></Bonuses><IncomeFactors><float index=\"3\" op=\"remove\"/></IncomeFactors></Race>");
            XDocument d = kit.Apply(kit.Races(PatchTestKit.HumanRace));

            Assert.Equal(new[] { "ShipSpeed" }, PatchTestKit.Values(Race0(d), "Bonuses/Bonus/Type"));
            Assert.Equal(2, Race0(d).Element("IncomeFactors").Elements().Count());
        }

        [Fact]
        public void Replace_One_Item_Rewrites_Only_That_Item()
        {
            PatchTestKit kit = new PatchTestKit().Patch(
                "<ComponentDefinition id=\"6\"><Values><ComponentStats index=\"2\" op=\"replace\"><WeaponRawDamage>18</WeaponRawDamage></ComponentStats></Values></ComponentDefinition>",
                root: "ArrayOfComponentDefinition");
            XDocument d = kit.Apply(kit.Components(PatchTestKit.Cannon));
            XElement second = Cannon(d).Element("Values").Elements().ElementAt(1);

            Assert.Equal("18", second.Element("WeaponRawDamage").Value);
            Assert.Null(second.Element("WeaponRange")); // left out: default
            Assert.Equal("10", Cannon(d).Element("Values").Elements().First().Element("WeaponRawDamage").Value);
        }

        [Fact]
        public void Replace_List_Keeps_Exactly_The_Written_Items()
        {
            PatchTestKit kit = new PatchTestKit().Patch(
                "<ComponentDefinition id=\"6\"><Values op=\"replace\"><ComponentStats><WeaponRawDamage>1</WeaponRawDamage></ComponentStats></Values></ComponentDefinition>",
                root: "ArrayOfComponentDefinition");
            XDocument d = kit.Apply(kit.Components(PatchTestKit.Cannon));

            XElement only = Assert.Single(Cannon(d).Element("Values").Elements());
            Assert.Equal("1", only.Element("WeaponRawDamage").Value);
        }

        [Fact]
        public void Selectors_Inside_A_Replaced_List_Are_Rejected()
        {
            PatchTestKit kit = new PatchTestKit().Patch(
                "<ComponentDefinition id=\"6\"><Values op=\"replace\"><ComponentStats index=\"1\"><WeaponRawDamage>1</WeaponRawDamage></ComponentStats></Values></ComponentDefinition>",
                root: "ArrayOfComponentDefinition");
            kit.Runner.ValidateAll();
            Assert.Contains("not allowed inside a replaced or added element", Assert.Single(kit.Errors()));
        }

        [Fact]
        public void Remove_List_Empties_It()
        {
            PatchTestKit kit = new PatchTestKit().Patch(
                "<ComponentDefinition id=\"6\"><Values op=\"remove\"/></ComponentDefinition>", root: "ArrayOfComponentDefinition");
            XDocument d = kit.Apply(kit.Components(PatchTestKit.Cannon));
            Assert.Null(Cannon(d).Element("Values"));
        }

        [Fact]
        public void Later_Patch_Sees_What_An_Earlier_One_Appended()
        {
            PatchTestKit kit = new PatchTestKit()
                .Patch("<ComponentDefinition id=\"6\"><Values><ComponentStats op=\"add\"><WeaponRawDamage>40</WeaponRawDamage></ComponentStats></Values></ComponentDefinition>", "patches/a.xml", "ArrayOfComponentDefinition")
                .Patch("<ComponentDefinition id=\"6\"><Values><ComponentStats index=\"3\"><WeaponRange>1600</WeaponRange></ComponentStats></Values></ComponentDefinition>", "patches/b.xml", "ArrayOfComponentDefinition");
            XDocument d = kit.Apply(kit.Components(PatchTestKit.Cannon));
            kit.Runner.Finish();

            Assert.Empty(kit.Errors());
            Assert.Equal("1600", PatchTestKit.Text(Cannon(d), "Values/ComponentStats[3]/WeaponRange"));
        }

        // ---- scalar lists ----

        [Fact]
        public void Scalar_List_Add_And_Remove_By_Value()
        {
            PatchTestKit kit = new PatchTestKit().Patch(
                "<Race id=\"0\"><PreferredGovernmentIds><short op=\"add\">9</short><short op=\"remove\">0</short></PreferredGovernmentIds></Race>");
            XDocument d = kit.Apply(kit.Races(PatchTestKit.HumanRace));
            Assert.Equal(new[] { "2", "9" }, PatchTestKit.Values(Race0(d), "PreferredGovernmentIds/short"));
        }

        [Fact]
        public void Scalar_List_Item_Is_Set_By_Index()
        {
            PatchTestKit kit = new PatchTestKit().Patch("<Race id=\"0\"><IncomeFactors><float index=\"2\">1.5</float></IncomeFactors></Race>");
            XDocument d = kit.Apply(kit.Races(PatchTestKit.HumanRace));
            Assert.Equal(new[] { "1", "1.5", "1" }, PatchTestKit.Values(Race0(d), "IncomeFactors/float"));
        }

        [Fact]
        public void Scalar_List_Remove_Of_A_Missing_Value_Is_Reported()
        {
            PatchTestKit kit = new PatchTestKit().Patch("<Race id=\"0\"><PreferredGovernmentIds><short op=\"remove\">7</short></PreferredGovernmentIds></Race>");
            kit.Apply(kit.Races(PatchTestKit.HumanRace));
            kit.Runner.Finish();
            Assert.Contains("no item with value '7' (has: 2, 0)", Assert.Single(kit.Errors()));
        }

        [Fact]
        public void Bare_Scalar_List_Item_Is_Rejected()
        {
            PatchTestKit kit = new PatchTestKit().Patch("<Race id=\"0\"><PreferredGovernmentIds><short>5</short></PreferredGovernmentIds></Race>");
            kit.Runner.ValidateAll();
            Assert.Contains("say what to do with it", Assert.Single(kit.Errors()));
        }

        [Fact]
        public void Scalar_List_Replace_Rewrites_The_Whole_List()
        {
            PatchTestKit kit = new PatchTestKit().Patch(
                "<Race id=\"0\"><PreferredGovernmentIds op=\"replace\"><short>3</short><short>4</short></PreferredGovernmentIds></Race>");
            XDocument d = kit.Apply(kit.Races(PatchTestKit.HumanRace));
            Assert.Equal(new[] { "3", "4" }, PatchTestKit.Values(Race0(d), "PreferredGovernmentIds/short"));
        }

        [Fact]
        public void Scalar_List_Value_Is_Type_Checked()
        {
            PatchTestKit kit = new PatchTestKit().Patch("<Race id=\"0\"><PreferredGovernmentIds><short op=\"add\">many</short></PreferredGovernmentIds></Race>");
            kit.Runner.ValidateAll();
            Assert.Contains("not valid for <short>", Assert.Single(kit.Errors()));
        }

        // ---- static checks ----

        [Fact]
        public void Unknown_Field_Gets_A_Suggestion()
        {
            PatchTestKit kit = new PatchTestKit().Patch("<Race id=\"0\"><Agression>2</Agression></Race>");
            kit.Runner.ValidateAll();
            string error = Assert.Single(kit.Errors());
            Assert.Contains("has no field 'Agression'", error);
            Assert.Contains("did you mean 'Aggression'", error);
        }

        [Fact]
        public void Unknown_Attribute_And_Bad_Op_Are_Rejected_With_Suggestions()
        {
            PatchTestKit kit = new PatchTestKit().Patch(
                "<Race id=\"0\"><Bonuses><Bonus idnex=\"1\"><Amount>1</Amount></Bonus><Bonus index=\"1\" op=\"remvoe\"/></Bonuses></Race>");
            kit.Runner.ValidateAll();

            string[] errors = kit.Errors().ToArray();
            Assert.Equal(2, errors.Length);
            Assert.Contains("unknown attribute 'idnex'", errors[0]);
            Assert.Contains("did you mean 'index'", errors[0]);
            Assert.Contains("did you mean 'remove'", errors[1]);
        }

        [Fact]
        public void Wrong_Value_Type_Is_Rejected_And_The_Rest_Of_The_Patch_Still_Applies()
        {
            PatchTestKit kit = new PatchTestKit().Patch("<Race id=\"0\"><Aggression>fast</Aggression><Name>Terran</Name></Race>");
            XDocument d = kit.Apply(kit.Races(PatchTestKit.HumanRace));

            Assert.Contains("'fast' is not valid for <Aggression>: expected a number", Assert.Single(kit.Errors()));
            Assert.Equal("1.2", Race0(d).Element("Aggression").Value);
            Assert.Equal("Terran", Race0(d).Element("Name").Value);
        }

        [Fact]
        public void Enum_Values_Are_Checked_Against_The_Types_Names()
        {
            PatchTestKit kit = new PatchTestKit().Patch(
                "<Race id=\"0\"><Bonuses><Bonus op=\"add\"><Type>ResearchAl</Type><Amount>1</Amount></Bonus></Bonuses></Race>");
            kit.Runner.ValidateAll();
            Assert.Contains("expected one of: ResearchAll, ShipSpeed, PlagueCuring", Assert.Single(kit.Errors()));
        }

        [Fact]
        public void Selectors_On_Fields_And_Structs_Are_Rejected()
        {
            PatchTestKit kit = new PatchTestKit().Patch(
                "<Race id=\"0\"><Aggression index=\"1\">2</Aggression><MainColor id=\"3\"><R>1</R></MainColor><Name op=\"add\">x</Name></Race>");
            kit.Runner.ValidateAll();
            Assert.Equal(3, kit.Errors().Count());
        }

        [Fact]
        public void Wrong_Item_Name_In_A_List_Is_Rejected()
        {
            PatchTestKit kit = new PatchTestKit().Patch("<Race id=\"0\"><Bonuses><Bonuss op=\"add\"><Type>ShipSpeed</Type></Bonuss></Bonuses></Race>");
            kit.Runner.ValidateAll();
            Assert.Contains("holds <Bonus> items", Assert.Single(kit.Errors()));
        }

        [Fact]
        public void A_Wrong_Entity_Element_Is_Rejected()
        {
            PatchTestKit kit = new PatchTestKit().Patch("<Raec id=\"0\"/>");
            kit.Runner.ValidateAll();
            Assert.Contains("did you mean 'Race'", Assert.Single(kit.Errors()));
        }

        // ---- output and files ----

        [Fact]
        public void Patch_Attributes_Never_Reach_The_Output()
        {
            PatchTestKit kit = new PatchTestKit().Patch(
                "<ComponentDefinition id=\"6\"><Values><ComponentStats index=\"1\"><WeaponRawDamage>1</WeaponRawDamage></ComponentStats><ComponentStats op=\"add\"><WeaponRawDamage>2</WeaponRawDamage></ComponentStats></Values></ComponentDefinition>",
                root: "ArrayOfComponentDefinition");
            XDocument d = kit.Apply(kit.Components(PatchTestKit.Cannon));
            Assert.DoesNotContain(d.Descendants().Attributes(), a => !a.IsNamespaceDeclaration);
        }

        [Fact]
        public void A_Patch_Does_Not_Touch_Data_With_A_Different_Root()
        {
            PatchTestKit kit = new PatchTestKit().Patch("<Race id=\"0\"><Aggression>2</Aggression></Race>");
            XDocument d = kit.Apply(kit.Components(PatchTestKit.Cannon));
            Assert.Equal(0, kit.Report.Applied("patches/p.xml"));
            Assert.Equal("6", Cannon(d).Element("ComponentId").Value);
        }

        [Fact]
        public void Patches_For_A_Root_That_Never_Loaded_Are_Reported_At_Finish()
        {
            PatchTestKit kit = new PatchTestKit().Patch("<Race id=\"0\"><Aggression>2</Aggression></Race>");
            kit.Runner.Finish();
            Assert.Contains(kit.Report.Entries, e => e.Severity == Severity.Warning && e.Message.Contains("did nothing"));
        }

        [Fact]
        public void A_Root_Without_A_Schema_Is_Reported_Not_Applied()
        {
            PatchRunner runner = new PatchRunner(_ => null, KeyMap.Default);
            runner.AddFile("mod", "patches/p.xml", "<ArrayOfRace><Race id=\"0\"><Aggression>2</Aggression></Race></ArrayOfRace>");
            XDocument d = XDocument.Parse("<ArrayOfRace><Race><RaceId>0</RaceId><Aggression>1</Aggression></Race></ArrayOfRace>");
            runner.Apply(d, "/data/Races.xml");

            Assert.Equal("1", d.Root.Element("Race").Element("Aggression").Value);
            Assert.Contains("could not be examined", Assert.Single(runner.Report.Entries).Message);
        }

        [Fact]
        public void Malformed_Xml_Is_Reported_With_A_Line_And_The_File_Is_Dropped()
        {
            PatchRunner runner = new PatchRunner(_ => null, KeyMap.Default);
            Assert.False(runner.AddFile("mod", "patches/bad.xml", "<ArrayOfRace>\n<Race id=\"0\">\n</ArrayOfRace>"));
            PatchDiagnostic d = Assert.Single(runner.Report.Entries);
            Assert.Equal("patches/bad.xml", d.File);
            Assert.True(d.Line >= 2);
            Assert.Empty(runner.Files);
        }

        [Fact]
        public void A_Root_That_Is_Not_ArrayOf_Is_Rejected()
        {
            PatchRunner runner = new PatchRunner(_ => null, KeyMap.Default);
            Assert.False(runner.AddFile("mod", "patches/p.xml", "<Patch/>"));
            Assert.Contains("must be the one of the data it patches", Assert.Single(runner.Report.Entries).Message);
        }

        [Fact]
        public void Diagnostics_Carry_The_Line_Of_The_Offending_Element()
        {
            PatchTestKit kit = new PatchTestKit();
            kit.Runner.AddFile("mod", "patches/p.xml", "<ArrayOfRace>\n  <Race id=\"0\">\n    <Agression>1</Agression>\n  </Race>\n</ArrayOfRace>");
            kit.Runner.ValidateAll();
            Assert.Equal(3, Assert.Single(kit.Report.Entries).Line);
        }

        [Fact]
        public void Failure_In_One_File_Is_Dropped_When_The_Item_Applies_In_Another()
        {
            PatchTestKit kit = new PatchTestKit().Patch("<Race id=\"0\"><Bonuses><Bonus id=\"ShipSpeed\"><Amount>9</Amount></Bonus></Bonuses></Race>");
            kit.Apply(kit.Races("<Race><RaceId>0</RaceId></Race>"), "/data/Races.xml");
            XDocument second = kit.Apply(kit.Races("<Race><RaceId>0</RaceId><Bonuses><Bonus><Type>ShipSpeed</Type><Amount>1</Amount></Bonus></Bonuses></Race>"), "/mods/x/Races_X.xml");
            kit.Runner.Finish();

            Assert.Empty(kit.Errors());
            Assert.Equal("9", PatchTestKit.Text(Race0(second), "Bonuses/Bonus/Amount"));
        }

        [Fact]
        public void Summary_Counts_Applied_Unchanged_And_Skipped_Per_File()
        {
            PatchTestKit kit = new PatchTestKit().Patch("<Race id=\"0\"><Aggression>2</Aggression><Name>Human</Name><Agression>1</Agression></Race>", "patches/rail.xml");
            kit.Apply(kit.Races(PatchTestKit.HumanRace));
            kit.Runner.Finish();

            Assert.Equal("patches/rail.xml: 1 applied, 1 unchanged, 1 skipped", Assert.Single(kit.Report.SummaryLines()));
        }

        [Fact]
        public void A_Second_Pass_Starts_Clean_And_Applies_Again()
        {
            PatchTestKit kit = new PatchTestKit().Patch("<Race id=\"0\"><Aggression>2</Aggression></Race><Race id=\"99\"><Aggression>2</Aggression></Race>");
            kit.Apply(kit.Races(PatchTestKit.HumanRace));
            kit.Runner.Finish();
            Assert.Single(kit.Errors()); // id 99

            kit.Runner.BeginPass();
            XDocument again = kit.Apply(kit.Races(PatchTestKit.HumanRace));

            Assert.Equal("2", Race0(again).Element("Aggression").Value);
            Assert.Equal(1, kit.Report.Applied("patches/p.xml")); // tallies restarted
        }

        [Fact]
        public void Real_World_Shape_Rail_Gun_Rebalance()
        {
            PatchTestKit kit = new PatchTestKit().Patch(
                "<ComponentDefinition id=\"6\">" +
                "<Family>Rail Guns</Family>" +
                "<Values><ComponentStats index=\"2\"><WeaponRawDamage>14</WeaponRawDamage><WeaponBombardDamageInfrastructure>2</WeaponBombardDamageInfrastructure></ComponentStats>" +
                "<ComponentStats op=\"add\"><CrewRequirement>5</CrewRequirement><WeaponRawDamage>40</WeaponRawDamage><WeaponRange>1600</WeaponRange></ComponentStats></Values>" +
                "<ResourcesRequired><ResourceQuantity id=\"8\"><Amount>6</Amount></ResourceQuantity></ResourcesRequired>" +
                "<DisplayTextureNames op=\"replace\"><string>Effects/Weapons/GreenLaser1</string></DisplayTextureNames>" +
                "</ComponentDefinition>", root: "ArrayOfComponentDefinition");
            XDocument d = kit.Apply(kit.Components(PatchTestKit.Cannon));
            kit.Runner.Finish();

            Assert.Empty(kit.Errors());
            XElement c = Cannon(d);
            Assert.Equal(new[] { "10", "14", "40" }, PatchTestKit.Values(c, "Values/ComponentStats/WeaponRawDamage"));
            Assert.Equal("2", PatchTestKit.Text(c, "Values/ComponentStats[2]/WeaponBombardDamageInfrastructure"));
            Assert.Equal(new[] { "Effects/Weapons/GreenLaser1" }, PatchTestKit.Values(c, "DisplayTextureNames/string"));
            Assert.Equal("6", PatchTestKit.Text(c, "ResourcesRequired/ResourceQuantity[1]/Amount"));
        }
    }
}
