using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DW2ModLauncher.XmlPatching
{
    /// <summary>
    /// Which field identifies an element type, so <c>id="…"</c> can select it: an entity (<c>Race</c> by <c>RaceId</c>) or a
    /// list item (<c>ResourceQuantity</c> by <c>ResourceId</c>). An element type that is not in the map has no key and
    /// can only be addressed by <c>index</c>. Keyed by element name; derived from a scan of the shipped data
    /// (see docs/plans/xml-patching.md, "The key map").
    /// </summary>
    public sealed class KeyMap
    {
        private readonly Dictionary<string, string> _keys = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly HashSet<string> _entities = new HashSet<string>(StringComparer.Ordinal);

        public static KeyMap Default { get; } = CreateDefault();

        public KeyMap Add(string elementName, string keyField)
        {
            _keys[elementName] = keyField;
            return this;
        }

        /// <summary>Adds the key of a top-level entity (<c>Race</c> by <c>RaceId</c>), as opposed to a list item (see <see cref="Add"/>).</summary>
        public KeyMap AddEntity(string elementName, string keyField)
        {
            _entities.Add(elementName);
            return Add(elementName, keyField);
        }

        /// <summary>
        /// The map as JSON for tools outside the launcher (the release publishes it as <c>keymap.json</c>): <c>entities</c> and
        /// <c>items</c>, each element name to its key field, sorted by name so the file only changes when the map does.
        /// <c>format</c> is bumped only if the shape of this file changes.
        /// </summary>
        public string ToJson()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{\n  \"format\": 1,\n");
            AppendGroup(sb, "entities", true);
            sb.Append(",\n");
            AppendGroup(sb, "items", false);
            sb.Append("\n}\n");
            return sb.ToString();
        }

        private void AppendGroup(StringBuilder sb, string name, bool entities)
        {
            sb.Append("  \"").Append(name).Append("\": {");
            bool first = true;
            foreach (KeyValuePair<string, string> kv in _keys.Where(k => _entities.Contains(k.Key) == entities).OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                sb.Append(first ? "\n" : ",\n").Append("    \"").Append(kv.Key).Append("\": \"").Append(kv.Value).Append('"');
                first = false;
            }
            sb.Append("\n  }");
        }

        public bool TryGetKey(string elementName, out string keyField)
        {
            return _keys.TryGetValue(elementName, out keyField);
        }

        private static KeyMap CreateDefault()
        {
            KeyMap map = new KeyMap();

            // Entities.
            map.AddEntity("ArmyTemplate", "ArmyTemplateId")
                .AddEntity("Artifact", "ArtifactId")
                .AddEntity("CharacterAnimation", "CharacterAnimationId")
                .AddEntity("CharacterRoom", "RoomId")
                .AddEntity("ColonyEventDefinition", "ColonyEventDefinitionId")
                .AddEntity("ComponentDefinition", "ComponentId")
                .AddEntity("CreatureType", "CreatureTypeId")
                .AddEntity("DesignTemplate", "DesignTemplateId")
                .AddEntity("FixedStructureDefinition", "FixedStructureDefinitionId")
                .AddEntity("FleetTemplate", "FleetTemplateId")
                .AddEntity("GameEvent", "Name")
                .AddEntity("Government", "GovernmentId")
                .AddEntity("MusicTrack", "Mood")
                .AddEntity("OrbType", "OrbTypeId")
                .AddEntity("Overlay", "OverlayId")
                .AddEntity("PlanetaryFacilityDefinition", "PlanetaryFacilityDefinitionId")
                .AddEntity("Race", "RaceId")
                .AddEntity("ResearchProjectDefinition", "ResearchProjectId")
                .AddEntity("Resource", "ResourceId")
                .AddEntity("ShipHull", "ShipHullId")
                .AddEntity("SpaceItemDefinition", "SpaceItemDefinitionId")
                .AddEntity("TourItem", "Title")
                .AddEntity("TroopDefinition", "TroopDefinitionId");

            // List items that are keyed (unique in every list of the shipped data).
            map.Add("ResourceQuantity", "ResourceId")
                .Add("Component", "ComponentId")
                .Add("ComponentBay", "ComponentBayId")
                .Add("OrbTypeFactor", "OrbTypeId")
                .Add("RaceFactor", "RaceId")
                .Add("RaceProbability", "RaceId")
                .Add("ResourcePrevalence", "ResourceId")
                .Add("IndexFactor", "Index")
                .Add("FleetTemplateItem", "Role")
                .Add("ShipRoleFactor", "Role")
                .Add("ModelModule", "MeshName")
                .Add("OverlayPart", "ImageFilepath")
                .Add("BonusRange", "Type")
                .Add("SortableTextValue", "Text")
                .Add("MusicTrackLayer", "Filepath")
                .Add("FixedStructureItem", "EntityName")
                .Add("PlanetaryFacilityValues", "PlanetaryFacilityDefinitionId")
                .Add("Bonus", "Type");

            return map;
        }
    }
}
