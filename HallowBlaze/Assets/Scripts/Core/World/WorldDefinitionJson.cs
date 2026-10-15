using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HallowBlaze.Core.World
{
    /// <summary>
    /// Materializes immutable world catalogues from authoring JSON text.
    /// </summary>
    public static class WorldDefinitionJson
    {
        private static readonly JsonSerializerSettings SerializerSettings =
            new JsonSerializerSettings
            {
                Culture = CultureInfo.InvariantCulture,
                MissingMemberHandling = MissingMemberHandling.Error,
                NullValueHandling = NullValueHandling.Include
            };

        /// <summary>
        /// Reads one complete JSON document and creates a pure world definition.
        /// </summary>
        /// <param name="json">Authoring JSON containing one world definition object.</param>
        /// <returns>An immutable world catalogue independent of the JSON collection order.</returns>
        /// <exception cref="ArgumentException"><paramref name="json"/> is empty.</exception>
        /// <exception cref="JsonException">The document is malformed or misses required data.</exception>
        public static WorldDefinition Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new ArgumentException("World definition JSON cannot be empty.", nameof(json));

            JObject document;
            using (StringReader stringReader = new StringReader(json))
            using (JsonTextReader jsonReader = new JsonTextReader(stringReader))
            {
                jsonReader.DateParseHandling = DateParseHandling.None;
                JToken token = JToken.ReadFrom(
                    jsonReader,
                    new JsonLoadSettings
                    {
                        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                        LineInfoHandling = LineInfoHandling.Load
                    });

                if (jsonReader.Read() || !(token is JObject parsedDocument))
                    throw new JsonSerializationException("World definition JSON must contain one object.");

                document = parsedDocument;
            }

            if (document["edges"] is JArray edgeRows)
                foreach (JToken edgeRow in edgeRows)
                {
                    if (!(edgeRow is JObject rawEdge))
                        continue;
                    foreach (JProperty field in rawEdge.Properties())
                        if (string.Equals(field.Name, "segments", StringComparison.OrdinalIgnoreCase) && field.Name != "segments")
                            throw new JsonSerializationException("Segment field names are case-sensitive.");
                    if (rawEdge["segments"] is JArray segmentRows)
                        foreach (JToken segmentRow in segmentRows)
                        {
                            if (!(segmentRow is JObject row))
                                throw new JsonSerializationException("Segment rows must be objects.");
                            foreach (JProperty field in row.Properties())
                                if (field.Name != "segmentId" && field.Name != "generationConfigurationId" && field.Name != "generationVersion")
                                    throw new JsonSerializationException("Unknown segment field: " + field.Name);
                            RequireSegmentToken(row, "segmentId", JTokenType.String);
                            RequireSegmentToken(row, "generationConfigurationId", JTokenType.String);
                            RequireSegmentToken(row, "generationVersion", JTokenType.Integer);
                        }
                }

            JsonSerializer serializer = JsonSerializer.Create(SerializerSettings);
            WorldDefinitionDto dto = document.ToObject<WorldDefinitionDto>(serializer);
            if (dto == null)
                throw new JsonSerializationException("World definition JSON could not be materialized.");

            List<WorldNodeDefinition> nodes = new List<WorldNodeDefinition>(dto.Nodes.Count);
            foreach (WorldNodeDto node in dto.Nodes)
            {
                if (node == null)
                    throw new JsonSerializationException("World node definitions cannot contain null values.");

                nodes.Add(
                    new WorldNodeDefinition(
                        node.NodeId,
                        node.AtlasX,
                        node.AtlasY,
                        node.DistanceLayer,
                        node.PlaceKind,
                        node.BiomeFamilyKey));
            }

            List<WorldEdgeDefinition> edges = new List<WorldEdgeDefinition>(dto.Edges.Count);
            foreach (WorldEdgeDto edge in dto.Edges)
            {
                if (edge == null)
                    throw new JsonSerializationException("World edge definitions cannot contain null values.");

                if (edge.Segments != null)
                {
                    List<RouteSegmentDefinition> segments = new List<RouteSegmentDefinition>();
                    foreach (RouteSegmentDto segment in edge.Segments)
                    {
                        if (segment == null)
                            throw new JsonSerializationException("Segment rows cannot be null.");
                        segments.Add(new RouteSegmentDefinition(segment.SegmentId,
                            segment.GenerationConfigurationId, segment.GenerationVersion));
                    }
                    edges.Add(new WorldEdgeDefinition(edge.EdgeId, edge.FromNodeId, edge.ToNodeId,
                        edge.Direction, edge.ClueKey, segments));
                    continue;
                }

                edges.Add(
                    new WorldEdgeDefinition(
                        edge.EdgeId,
                        edge.FromNodeId,
                        edge.ToNodeId,
                        edge.Direction,
                        edge.ClueKey));
            }

            return new WorldDefinition(
                dto.WorldDefinitionId,
                dto.Version,
                dto.StartNodeId,
                dto.GoalNodeId,
                nodes,
                edges);
        }

        private static void RequireSegmentToken(JObject row, string field, JTokenType type)
        {
            if (row[field] == null || row[field].Type != type)
                throw new JsonSerializationException("Invalid segment scalar type: " + field);
        }

        [JsonObject(MemberSerialization.OptIn)]
        private sealed class WorldDefinitionDto
        {
            [JsonProperty("worldDefinitionId", Required = Required.Always)]
            public string WorldDefinitionId { get; set; }

            [JsonProperty("version", Required = Required.Always)]
            public int Version { get; set; }

            [JsonProperty("startNodeId", Required = Required.Always)]
            public string StartNodeId { get; set; }

            [JsonProperty("goalNodeId", Required = Required.Always)]
            public string GoalNodeId { get; set; }

            [JsonProperty("nodes", Required = Required.Always)]
            public List<WorldNodeDto> Nodes { get; set; }

            [JsonProperty("edges", Required = Required.Always)]
            public List<WorldEdgeDto> Edges { get; set; }
        }

        [JsonObject(MemberSerialization.OptIn)]
        private sealed class WorldNodeDto
        {
            [JsonProperty("nodeId", Required = Required.Always)]
            public string NodeId { get; set; }

            [JsonProperty("atlasX", Required = Required.Always)]
            public int AtlasX { get; set; }

            [JsonProperty("atlasY", Required = Required.Always)]
            public int AtlasY { get; set; }

            [JsonProperty("distanceLayer", Required = Required.Always)]
            public int DistanceLayer { get; set; }

            [JsonProperty("placeKind", Required = Required.Always)]
            public string PlaceKind { get; set; }

            [JsonProperty("biomeFamilyKey", Required = Required.Always)]
            public string BiomeFamilyKey { get; set; }
        }

        [JsonObject(MemberSerialization.OptIn)]
        private sealed class WorldEdgeDto
        {
            [JsonProperty("edgeId", Required = Required.Always)]
            public string EdgeId { get; set; }

            [JsonProperty("fromNodeId", Required = Required.Always)]
            public string FromNodeId { get; set; }

            [JsonProperty("toNodeId", Required = Required.Always)]
            public string ToNodeId { get; set; }

            [JsonProperty("direction", Required = Required.Always)]
            public string Direction { get; set; }

            [JsonProperty("clueKey", Required = Required.Always)]
            public string ClueKey { get; set; }

            [JsonProperty("segments", Required = Required.DisallowNull)]
            public List<RouteSegmentDto> Segments { get; set; }
        }

        [JsonObject(MemberSerialization.OptIn)]
        private sealed class RouteSegmentDto
        {
            [JsonProperty("segmentId", Required = Required.Always)]
            public string SegmentId { get; set; }

            [JsonProperty("generationConfigurationId", Required = Required.Always)]
            public string GenerationConfigurationId { get; set; }

            [JsonProperty("generationVersion", Required = Required.Always)]
            public int GenerationVersion { get; set; }
        }
    }
}