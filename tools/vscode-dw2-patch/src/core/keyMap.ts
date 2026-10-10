// Mirror of src/DW2ModLauncher.XmlPatching/KeyMap.cs: which child element identifies an entity or a list item, so the
// patch can say id="…". test/keyMap.test.ts parses KeyMap.cs and fails when the two drift apart.

export const ENTITY_KEYS: ReadonlyMap<string, string> = new Map([
  ['ArmyTemplate', 'ArmyTemplateId'],
  ['Artifact', 'ArtifactId'],
  ['CharacterAnimation', 'CharacterAnimationId'],
  ['CharacterRoom', 'RoomId'],
  ['ColonyEventDefinition', 'ColonyEventDefinitionId'],
  ['ComponentDefinition', 'ComponentId'],
  ['CreatureType', 'CreatureTypeId'],
  ['DesignTemplate', 'DesignTemplateId'],
  ['FixedStructureDefinition', 'FixedStructureDefinitionId'],
  ['FleetTemplate', 'FleetTemplateId'],
  ['GameEvent', 'Name'],
  ['Government', 'GovernmentId'],
  ['MusicTrack', 'Mood'],
  ['OrbType', 'OrbTypeId'],
  ['Overlay', 'OverlayId'],
  ['PlanetaryFacilityDefinition', 'PlanetaryFacilityDefinitionId'],
  ['Race', 'RaceId'],
  ['ResearchProjectDefinition', 'ResearchProjectId'],
  ['Resource', 'ResourceId'],
  ['ShipHull', 'ShipHullId'],
  ['SpaceItemDefinition', 'SpaceItemDefinitionId'],
  ['TourItem', 'Title'],
  ['TroopDefinition', 'TroopDefinitionId'],
]);

export const ITEM_KEYS: ReadonlyMap<string, string> = new Map([
  ['ResourceQuantity', 'ResourceId'],
  ['Component', 'ComponentId'],
  ['ComponentBay', 'ComponentBayId'],
  ['OrbTypeFactor', 'OrbTypeId'],
  ['RaceFactor', 'RaceId'],
  ['RaceProbability', 'RaceId'],
  ['ResourcePrevalence', 'ResourceId'],
  ['IndexFactor', 'Index'],
  ['FleetTemplateItem', 'Role'],
  ['ShipRoleFactor', 'Role'],
  ['ModelModule', 'MeshName'],
  ['OverlayPart', 'ImageFilepath'],
  ['BonusRange', 'Type'],
  ['SortableTextValue', 'Text'],
  ['MusicTrackLayer', 'Filepath'],
  ['FixedStructureItem', 'EntityName'],
  ['PlanetaryFacilityValues', 'PlanetaryFacilityDefinitionId'],
  ['Bonus', 'Type'],
]);

/** Element names XmlSerializer gives to the items of a list of plain values. */
export const PRIMITIVE_ITEMS: ReadonlySet<string> = new Set([
  'string', 'short', 'int', 'long', 'float', 'double', 'boolean', 'byte', 'decimal',
  'unsignedByte', 'unsignedShort', 'unsignedInt', 'unsignedLong', 'dateTime', 'guid',
]);
