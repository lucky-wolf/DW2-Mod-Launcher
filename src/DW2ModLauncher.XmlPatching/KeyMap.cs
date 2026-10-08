using System;
using System.Collections.Generic;

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

        public static KeyMap Default { get; } = CreateDefault();

        public KeyMap Add(string elementName, string keyField)
        {
            _keys[elementName] = keyField;
            return this;
        }

        public bool TryGetKey(string elementName, out string keyField)
        {
            return _keys.TryGetValue(elementName, out keyField);
        }

        private static KeyMap CreateDefault()
        {
            KeyMap map = new KeyMap();

            // Entities.
            map.Add("ArmyTemplate", "ArmyTemplateId")
                .Add("Artifact", "ArtifactId")
                .Add("CharacterAnimation", "CharacterAnimationId")
                .Add("CharacterRoom", "RoomId")
                .Add("ColonyEventDefinition", "ColonyEventDefinitionId")
                .Add("ComponentDefinition", "ComponentId")
                .Add("CreatureType", "CreatureTypeId")
                .Add("DesignTemplate", "DesignTemplateId")
                .Add("FixedStructureDefinition", "FixedStructureDefinitionId")
                .Add("FleetTemplate", "FleetTemplateId")
                .Add("GameEvent", "Name")
                .Add("Government", "GovernmentId")
                .Add("MusicTrack", "Mood")
                .Add("OrbType", "OrbTypeId")
                .Add("Overlay", "OverlayId")
                .Add("PlanetaryFacilityDefinition", "PlanetaryFacilityDefinitionId")
                .Add("Race", "RaceId")
                .Add("ResearchProjectDefinition", "ResearchProjectId")
                .Add("Resource", "ResourceId")
                .Add("ShipHull", "ShipHullId")
                .Add("SpaceItemDefinition", "SpaceItemDefinitionId")
                .Add("TourItem", "Title")
                .Add("TroopDefinition", "TroopDefinitionId");

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
