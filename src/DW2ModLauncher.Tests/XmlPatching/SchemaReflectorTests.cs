using System.Collections.Generic;
using System.Xml.Serialization;
using DW2ModLauncher.XmlPatching;
using Xunit;

namespace DW2ModLauncher.Tests.XmlPatching
{
    public class SchemaReflectorTests
    {
        [XmlType("Odd")]
        public class Odd
        {
            public int Id;
            [XmlElement("Flat")]
            public List<string> Unwrapped;
            [XmlAttribute]
            public string Attr;
            public Dictionary<string, string> Map;
            [XmlElement("Renamed")]
            public float Original;
            public readonly int ReadOnly = 1;
            public List<TBonus> Items { get; set; }
            public List<string> GetOnlyList { get; } = new List<string>();
            public string GetOnlyScalar { get; } = "x";
        }

        public class OddList : List<Odd>
        {
        }

        [Fact]
        public void Root_And_Entity_Names_Follow_XmlSerializer()
        {
            SchemaRoot root = new SchemaReflector().BuildRoot(typeof(TRaceList));
            Assert.Equal("ArrayOfRace", root.RootElement);
            Assert.Equal("Race", root.EntityElement);
        }

        [Fact]
        public void Classifies_Scalars_Structs_And_Lists()
        {
            SchemaRoot root = new SchemaReflector().BuildRoot(typeof(TRaceList));
            Dictionary<string, SchemaMember> m = root.EntityType.Members;

            Assert.Equal(MemberKind.Scalar, m["Aggression"].Kind);
            Assert.Equal(ScalarKind.Float, m["Aggression"].Scalar.Kind);
            Assert.Equal(ScalarKind.Integer, m["RaceId"].Scalar.Kind);
            Assert.Equal(0, m["RaceId"].Scalar.Min);
            Assert.Equal(255, m["RaceId"].Scalar.Max);
            Assert.Equal(ScalarKind.Bool, m["IsPlayable"].Scalar.Kind);
            Assert.Equal(MemberKind.Struct, m["MainColor"].Kind);
            Assert.Contains("R", m["MainColor"].Type.Members.Keys);

            Assert.Equal(MemberKind.List, m["PreferredGovernmentIds"].Kind);
            Assert.True(m["PreferredGovernmentIds"].IsScalarList);
            Assert.Equal("short", m["PreferredGovernmentIds"].ItemName);
            Assert.Equal("Bonus", m["Bonuses"].ItemName);
            Assert.False(m["Bonuses"].IsScalarList);
            Assert.Equal(ScalarKind.Enum, m["Bonuses"].Type.Members["Type"].Scalar.Kind);
            Assert.Contains("ShipSpeed", m["Bonuses"].Type.Members["Type"].Scalar.EnumNames);
        }

        [Fact]
        public void XmlIgnore_Members_Are_Not_Part_Of_The_Schema()
        {
            SchemaRoot root = new SchemaReflector().BuildRoot(typeof(TRaceList));
            Assert.DoesNotContain("NotSerialized", root.EntityType.Members.Keys);
        }

        [Fact]
        public void Unsupported_Shapes_Are_Left_Out_And_Noted()
        {
            SchemaReflector reflector = new SchemaReflector();
            SchemaRoot root = reflector.BuildRoot(typeof(OddList));
            Dictionary<string, SchemaMember> m = root.EntityType.Members;

            Assert.DoesNotContain("Flat", m.Keys);
            Assert.DoesNotContain("Attr", m.Keys);
            Assert.DoesNotContain("Map", m.Keys);
            Assert.DoesNotContain("ReadOnly", m.Keys);
            Assert.DoesNotContain("GetOnlyScalar", m.Keys);
            Assert.Contains("Renamed", m.Keys);
            Assert.Contains("Items", m.Keys);
            Assert.Contains("GetOnlyList", m.Keys); // XmlSerializer fills get-only collections
            Assert.Contains(reflector.Notes, n => n.Contains("Unwrapped"));
            Assert.Contains(reflector.Notes, n => n.Contains("Attr"));
            Assert.Contains(reflector.Notes, n => n.Contains("Map"));
        }

        [Fact]
        public void A_Type_That_Is_Not_A_List_Has_No_Root()
        {
            SchemaReflector reflector = new SchemaReflector();
            Assert.Null(reflector.BuildRoot(typeof(TRace)));
            Assert.NotEmpty(reflector.Notes);
        }

        [Theory]
        [InlineData("true", true)]
        [InlineData("1", true)]
        [InlineData("yes", false)]
        public void Bool_Values_Follow_XmlConvert(string text, bool valid)
        {
            Assert.Equal(valid, ScalarSpec.Boolean().TryValidate(text, out _));
        }

        [Theory]
        [InlineData("1.5", true)]
        [InlineData("-3", true)]
        [InlineData("1,5", false)]
        [InlineData("fast", false)]
        public void Float_Values_Are_Invariant_Numbers(string text, bool valid)
        {
            Assert.Equal(valid, ScalarSpec.Float().TryValidate(text, out _));
        }

        [Theory]
        [InlineData("0", true)]
        [InlineData("255", true)]
        [InlineData("256", false)]
        [InlineData("-1", false)]
        [InlineData("1.5", false)]
        public void Integer_Values_Respect_The_Range(string text, bool valid)
        {
            Assert.Equal(valid, ScalarSpec.Integer(0, 255).TryValidate(text, out _));
        }
    }
}
