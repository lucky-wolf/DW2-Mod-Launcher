using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using System.Xml.Serialization;
using DW2ModLauncher.XmlPatching;

namespace DW2ModLauncher.Tests.XmlPatching
{
    // Types shaped like the game's deserialization types (public fields, wrapped lists, [XmlType] names), so the tests go
    // through SchemaReflector the same way the loader does.

    public enum TBonusType
    {
        ResearchAll,
        ShipSpeed,
        PlagueCuring
    }

    [XmlType("Color")]
    public class TColor
    {
        public byte R;
        public byte G;
        public byte B;
        public byte A;
    }

    [XmlType("Bonus")]
    public class TBonus
    {
        public TBonusType Type;
        public float Amount;
    }

    [XmlType("Race")]
    public class TRace
    {
        public byte RaceId;
        public string Name;
        public float Aggression;
        public bool IsPlayable;
        public TColor MainColor;
        public List<short> PreferredGovernmentIds;
        public List<short> AllowableGovernmentIds;
        public List<TBonus> Bonuses;
        public List<float> IncomeFactors;
        [XmlIgnore]
        public int NotSerialized;
    }

    public class TRaceList : List<TRace>
    {
    }

    [XmlType("WeaponEffect")]
    public class TWeaponEffect
    {
        public string StaticTextureFilepath;
        public TColor TextureTintColor;
        public TVector BodyScaling;
    }

    [XmlType("Vector2")]
    public class TVector
    {
        public float X;
        public float Y;
    }

    [XmlType("ComponentStats")]
    public class TStats
    {
        public float ComponentCountermeasuresBonus;
        public float CrewRequirement;
        public float WeaponBombardDamageInfrastructure;
        public float WeaponRange;
        public float WeaponRawDamage;
    }

    [XmlType("ResourceQuantity")]
    public class TResourceQuantity
    {
        public byte ResourceId;
        public float Amount;
    }

    [XmlType("ComponentDefinition")]
    public class TComponent
    {
        public short ComponentId;
        public string Name;
        public string Family;
        public TWeaponEffect WeaponEffect;
        public List<string> DisplayTextureNames;
        public List<TResourceQuantity> ResourcesRequired;
        public List<TStats> Values;
    }

    public class TComponentList : List<TComponent>
    {
    }

    public sealed class PatchTestKit
    {
        private readonly Dictionary<string, SchemaRoot> _roots = new Dictionary<string, SchemaRoot>();

        public PatchTestKit()
        {
            foreach (Type t in new[] { typeof(TRaceList), typeof(TComponentList) })
            {
                SchemaRoot root = new SchemaReflector().BuildRoot(t);
                _roots[root.RootElement] = root;
            }
            Runner = new PatchRunner(name => _roots.TryGetValue(name, out SchemaRoot r) ? r : null, KeyMap.Default);
        }

        public PatchRunner Runner { get; }

        public PatchReport Report
        {
            get { return Runner.Report; }
        }

        public PatchTestKit Patch(string body, string path = "patches/p.xml", string root = "ArrayOfRace")
        {
            Runner.AddFile("mod", path, "<" + root + ">" + body + "</" + root + ">");
            return this;
        }

        public XDocument Races(string body)
        {
            return XDocument.Parse("<ArrayOfRace>" + body + "</ArrayOfRace>");
        }

        public XDocument Components(string body)
        {
            return XDocument.Parse("<ArrayOfComponentDefinition>" + body + "</ArrayOfComponentDefinition>");
        }

        public XDocument Apply(XDocument data, string path = "/data/x.xml")
        {
            Runner.Apply(data, path);
            return data;
        }

        public IEnumerable<string> Errors()
        {
            return Report.Entries.Where(e => e.Severity == Severity.Error).Select(e => e.Message);
        }

        public const string HumanRace = "<Race><RaceId>0</RaceId><Name>Human</Name><Aggression>1.2</Aggression><IsPlayable>true</IsPlayable>" +
                                        "<MainColor><R>0</R><G>0</G><B>176</B><A>255</A></MainColor>" +
                                        "<PreferredGovernmentIds><short>2</short><short>0</short></PreferredGovernmentIds>" +
                                        "<Bonuses><Bonus><Type>ResearchAll</Type><Amount>0.1</Amount></Bonus><Bonus><Type>ShipSpeed</Type><Amount>0.2</Amount></Bonus></Bonuses>" +
                                        "<IncomeFactors><float>1</float><float>1</float><float>1</float></IncomeFactors></Race>";

        public const string Cannon = "<ComponentDefinition><ComponentId>6</ComponentId><Name>Long Range Cannon [S]</Name><Family>Rail Guns</Family>" +
                                     "<WeaponEffect><StaticTextureFilepath>Effects/Weapons/Slug1</StaticTextureFilepath>" +
                                     "<TextureTintColor><R>255</R><G>255</G><B>255</B><A>255</A></TextureTintColor><BodyScaling><X>1.5</X><Y>1.5</Y></BodyScaling></WeaponEffect>" +
                                     "<DisplayTextureNames><string>Effects/Weapons/Slug1</string><string>Effects/Weapons/Slug2</string></DisplayTextureNames>" +
                                     "<ResourcesRequired><ResourceQuantity><ResourceId>8</ResourceId><Amount>4</Amount></ResourceQuantity><ResourceQuantity><ResourceId>9</ResourceId><Amount>4</Amount></ResourceQuantity></ResourcesRequired>" +
                                     "<Values><ComponentStats><ComponentCountermeasuresBonus>0.6</ComponentCountermeasuresBonus><CrewRequirement>5</CrewRequirement><WeaponRange>1000</WeaponRange><WeaponRawDamage>10</WeaponRawDamage></ComponentStats>" +
                                     "<ComponentStats><ComponentCountermeasuresBonus>0.7</ComponentCountermeasuresBonus><CrewRequirement>5</CrewRequirement><WeaponRange>1200</WeaponRange><WeaponRawDamage>15</WeaponRawDamage></ComponentStats></Values></ComponentDefinition>";

        public static string Text(XElement e, string path)
        {
            return System.Xml.XPath.Extensions.XPathSelectElement(e, path)?.Value;
        }

        public static List<string> Values(XElement e, string path)
        {
            return System.Xml.XPath.Extensions.XPathSelectElements(e, path).Select(x => x.Value).ToList();
        }
    }
}
