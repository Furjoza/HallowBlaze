using System;
using System.Collections.Generic;
using System.IO;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.Generation.Validation;
using HallowBlaze.Core.World;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HallowBlaze.Core.Generation.Landmarks
{
    /// <summary>Loads versioned authored landmark data against an external content catalog, never Unity objects or inferred gameplay rules.</summary>
    public static class LandmarkTemplateJson
    {
        /// <summary>Gets the only supported authoring schema; this is not a profile/run save schema.</summary>
        public const int CurrentSchemaVersion = 1;

        /// <summary>Loads one immutable 8x8 template; all definitions come from the caller's authoritative catalog.</summary>
        /// <exception cref="ArgumentException">An ID, layout, goal or mandatory guarantee is invalid.</exception>
        /// <exception cref="JsonException">JSON structure, scalar types, schema or referenced content is invalid.</exception>
        public static LandmarkTemplate Deserialize(string json, BoardValidationCatalog catalog)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            JObject root = Document(json);
            Fields(root, "schemaVersion", "templateId", "templateVersion", "configuration", "variants", "outcomes");
            Schema(root);
            string id = Text(root, "templateId");
            JObject config = Object(root["configuration"]);
            Fields(config, "id", "version", "pathLength", "branchPoints", "obstacleCount", "enemyCount",
                "maximumConcurrentThreats", "minimumSafeStartMoves", "allowedContentIds", "resourceBudgets");
            var allowed = new List<string>();
            foreach (JToken token in Array(config["allowedContentIds"]))
            {
                string contentId = String(token);
                if (!catalog.ContainsContent(contentId)) throw Error(token, "Unknown allowed content " + contentId + ".");
                allowed.Add(contentId);
            }
            var budgets = new List<ResourceGenerationBudget>();
            foreach (JToken token in Array(config["resourceBudgets"]))
            {
                JObject row = Object(token);
                Fields(row, "resourceFamilyId", "itemCount");
                string family = Text(row, "resourceFamilyId");
                if (!catalog.ContainsFamily(family)) throw Error(row, "Unknown resource family " + family + ".");
                budgets.Add(new ResourceGenerationBudget(family, Range(row["itemCount"])));
            }
            var targets = new BoardGenerationConfig(Text(config, "id"), Integer(config["version"]), Range(config["pathLength"]),
                Range(config["branchPoints"]), Range(config["obstacleCount"]), Range(config["enemyCount"]),
                Integer(config["maximumConcurrentThreats"]), Integer(config["minimumSafeStartMoves"]), allowed, budgets);
            var outcomes = new List<LandmarkOutcomeDefinition>();
            foreach (JToken token in Array(root["outcomes"]))
            {
                JObject row = Object(token);
                Fields(row, "id", "name", "exitEntityId", "optional", "requiredToolDefinitionId", "guaranteedToolInstanceId");
                string toolId = OptionalText(row, "requiredToolDefinitionId");
                if (toolId != null && (!catalog.TryGetItemFamily(toolId, out string family) || family != "tools"))
                    throw Error(row, "Required tool does not resolve to the tools resource family: " + toolId + ".");
                outcomes.Add(new LandmarkOutcomeDefinition(Text(row, "id"), Text(row, "name"), new EntityId(Integer(row["exitEntityId"])),
                    Boolean(row["optional"]), toolId, OptionalText(row, "guaranteedToolInstanceId")));
            }
            var definitions = new Dictionary<(BoardLayer, string), BoardEntityDefinition>();
            foreach (BoardEntityDefinition definition in catalog.GetDefinitions()) definitions.Add((definition.Layer, definition.ContentId), definition);
            var variants = new List<LandmarkVariant>();
            foreach (JToken token in Array(root["variants"]))
            {
                JObject row = Object(token);
                Fields(row, "id", "entities", "items", "plates", "gates");
                var entities = new List<BoardEntityState>();
                foreach (JToken entityToken in Array(row["entities"]))
                {
                    JObject entity = Object(entityToken);
                    Fields(entity, "id", "layer", "contentId", "x", "y");
                    string layerText = Text(entity, "layer");
                    if (!Enum.TryParse(layerText, false, out BoardLayer layer) || layer.ToString() != layerText || !Enum.IsDefined(typeof(BoardLayer), layer))
                        throw Error(entity, "Unknown board layer " + layerText + ".");
                    string contentId = Text(entity, "contentId");
                    if (!allowed.Contains(contentId) || !definitions.TryGetValue((layer, contentId), out BoardEntityDefinition definition))
                        throw Error(entity, "Unknown or disallowed layer/content reference " + layerText + "/" + contentId + ".");
                    entities.Add(new BoardEntityState(new EntityId(Integer(entity["id"])), definition,
                        new GridPosition(Integer(entity["x"]), Integer(entity["y"]))));
                }
                var items = new List<BoardItemSpawn>();
                foreach (JToken itemToken in Array(row["items"]))
                {
                    JObject item = Object(itemToken);
                    Fields(item, "instanceId", "entityId", "itemDefinitionId", "resourceFamilyId", "sourceId", "initiallyObservable");
                    string definitionId = Text(item, "itemDefinitionId");
                    string family = Text(item, "resourceFamilyId");
                    if (!catalog.TryGetItemFamily(definitionId, out string actualFamily) || actualFamily != family)
                        throw Error(item, "Item definition/family reference does not resolve.");
                    string sourceId = OptionalText(item, "sourceId");
                    if (sourceId != null && (!catalog.TryGetSourceDefinition(sourceId, out string sourceDefinition) || sourceDefinition != definitionId))
                        throw Error(item, "Fixed-source reference does not resolve to this item definition.");
                    items.Add(new BoardItemSpawn(Text(item, "instanceId"), new EntityId(Integer(item["entityId"])),
                        definitionId, family, sourceId, Boolean(item["initiallyObservable"])));
                }
                var plates = new List<PressurePlateDefinition>();
                if (row["plates"] != null)
                    foreach (JToken plateToken in Array(row["plates"]))
                    {
                        JObject plate = Object(plateToken);
                        Fields(plate, "terrainId", "linkId", "occupants");
                        plates.Add(new PressurePlateDefinition(new EntityId(Integer(plate["terrainId"])), Text(plate, "linkId"),
                            EnumValue<PlateOccupants>(plate["occupants"])));
                    }
                var gates = new List<GateDefinition>();
                if (row["gates"] != null)
                    foreach (JToken gateToken in Array(row["gates"]))
                    {
                        JObject gate = Object(gateToken);
                        Fields(gate, "terrainId", "linkId", "rule");
                        gates.Add(new GateDefinition(new EntityId(Integer(gate["terrainId"])), Text(gate, "linkId"),
                            EnumValue<GateRule>(gate["rule"])));
                    }
                variants.Add(new LandmarkVariant(Text(row, "id"), entities, items, plates, gates));
            }
            return new LandmarkTemplate(id, Integer(root["templateVersion"]), targets, variants, outcomes);
        }

        /// <summary>Loads one explicit segment assignment against published world/template identities; no node-ID or day-number routing is inferred.</summary>
        public static LandmarkPlacement DeserializePlacement(string json, WorldDefinition world, LandmarkTemplate template)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (template == null) throw new ArgumentNullException(nameof(template));
            JObject root = Document(json);
            Fields(root, "schemaVersion", "worldDefinitionId", "worldVersion", "segmentId", "templateId", "templateVersion", "variantId");
            Schema(root);
            if (Text(root, "worldDefinitionId") != world.WorldDefinitionId || Integer(root["worldVersion"]) != world.Version ||
                Text(root, "templateId") != template.Id || Integer(root["templateVersion"]) != template.Version)
                throw Error(root, "Placement world/template identity or version does not match.");
            return new LandmarkPlacement(world, Text(root, "segmentId"), template, Text(root, "variantId"));
        }

        private static JObject Document(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("Landmark JSON is required.", nameof(json));
            using (var text = new StringReader(json))
            using (var reader = new JsonTextReader(text) { DateParseHandling = DateParseHandling.None })
            {
                JToken token = JToken.ReadFrom(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                if (reader.Read()) throw Error(token, "Only one JSON object is permitted.");
                return Object(token);
            }
        }

        private static void Schema(JObject root)
        {
            if (Integer(root["schemaVersion"]) != CurrentSchemaVersion) throw Error(root, "Unsupported landmark authoring schema.");
        }

        private static void Fields(JObject row, params string[] names)
        {
            var allowed = new HashSet<string>(names, StringComparer.Ordinal);
            foreach (JProperty property in row.Properties())
                if (!allowed.Contains(property.Name)) throw Error(property, "Unknown or incorrectly cased field.");
        }

        private static JObject Object(JToken token) => token as JObject ?? throw Error(token, "An object is required.");
        private static JArray Array(JToken token) => token as JArray ?? throw Error(token, "An array is required.");
        private static string Text(JObject row, string field) => String(row[field]);
        private static string OptionalText(JObject row, string field) => row[field] == null || row[field].Type == JTokenType.Null ? null : String(row[field]);
        private static string String(JToken token)
        {
            if (token == null || token.Type != JTokenType.String) throw Error(token, "A textual stable ID or name is required.");
            string value = token.Value<string>();
            GenerationContract.RequireId(value, token.Path);
            return value;
        }

        private static int Integer(JToken token)
        {
            if (token == null || token.Type != JTokenType.Integer) throw Error(token, "An Int32 integer is required.");
            long value;
            try { value = token.Value<long>(); }
            catch (Exception exception) when (exception is OverflowException || exception is InvalidCastException)
            { throw Error(token, "Integer exceeds Int32 capacity."); }
            if (value < int.MinValue || value > int.MaxValue) throw Error(token, "Integer exceeds Int32 capacity.");
            return (int)value;
        }

        private static TEnum EnumValue<TEnum>(JToken token) where TEnum : struct
        {
            string value = String(token);
            if (!Enum.TryParse(value, false, out TEnum parsed) || parsed.ToString() != value || !Enum.IsDefined(typeof(TEnum), parsed))
                throw Error(token, "Unknown rule " + value + ".");
            return parsed;
        }

        private static bool Boolean(JToken token)
        {
            if (token == null || token.Type != JTokenType.Boolean) throw Error(token, "A Boolean is required.");
            return token.Value<bool>();
        }

        private static GenerationRange Range(JToken token)
        {
            JObject row = Object(token);
            Fields(row, "minimum", "maximum");
            return new GenerationRange(Integer(row["minimum"]), Integer(row["maximum"]));
        }

        private static JsonSerializationException Error(JToken token, string message) =>
            new JsonSerializationException("Landmark " + (token?.Path ?? "<missing>") + ": " + message);
    }
}