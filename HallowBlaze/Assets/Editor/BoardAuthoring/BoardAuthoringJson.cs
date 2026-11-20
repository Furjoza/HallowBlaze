using System;
using System.Globalization;
using System.IO;
using System.Linq;
using HallowBlaze.Core.Generation.Landmarks;
using HallowBlaze.Core.Generation.Validation;
using HallowBlaze.Core.World;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HallowBlaze.Editor.BoardAuthoring
{
    /// <summary>Preserves schema-1 authoring data, including optional-field presence, while reusing the runtime readers.</summary>
    public static class BoardAuthoringJson
    {
        private static readonly string[] FieldOrder =
        {
            "schemaVersion", "templateId", "templateVersion", "configuration", "variants", "outcomes",
            "worldDefinitionId", "worldVersion", "segmentId", "variantId", "id", "version", "pathLength",
            "branchPoints", "obstacleCount", "enemyCount", "maximumConcurrentThreats", "minimumSafeStartMoves",
            "allowedContentIds", "resourceBudgets", "resourceFamilyId", "itemCount", "minimum", "maximum",
            "entities", "items", "plates", "gates", "layer", "contentId", "x", "y", "instanceId", "entityId",
            "itemDefinitionId", "sourceId", "initiallyObservable", "terrainId", "linkId", "occupants", "rule",
            "name", "exitEntityId", "optional", "requiredToolDefinitionId", "guaranteedToolInstanceId"
        };

        /// <summary>Decodes all variants without dropping absent/null optional fields; unsupported data throws before a document is replaced.</summary>
        public static JObject ReadTemplate(string json, BoardValidationCatalog catalog)
        {
            LandmarkTemplateJson.Deserialize(json, catalog);
            return Parse(json);
        }

        /// <summary>Decodes one assignment against exact world/template versions and the existing final-landmark placement rules.</summary>
        public static JObject ReadPlacement(string json, WorldDefinition world, LandmarkTemplate template)
        {
            LandmarkTemplateJson.DeserializePlacement(json, world, template);
            return Parse(json);
        }

        /// <summary>Emits deterministic schema-1 JSON after decoding both the candidate and result with the runtime template reader.</summary>
        public static string WriteTemplate(JObject document, BoardValidationCatalog catalog)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            LandmarkTemplateJson.Deserialize(document.ToString(Formatting.None), catalog);
            string json = Format(document);
            VerifyRoundTrip(document, ReadTemplate(json, catalog));
            return json;
        }

        /// <summary>Emits only the existing assignment schema; neither activates it nor adds edge or assignment identities.</summary>
        public static string WritePlacement(JObject document, WorldDefinition world, LandmarkTemplate template)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            LandmarkTemplateJson.DeserializePlacement(document.ToString(Formatting.None), world, template);
            string json = Format(document);
            VerifyRoundTrip(document, ReadPlacement(json, world, template));
            return json;
        }

        internal static string Format(JObject document)
        {
            using (var text = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" })
            using (var writer = new JsonTextWriter(text) { Formatting = Formatting.Indented, Indentation = 2 })
            {
                Canonical(document, string.Empty).WriteTo(writer);
                writer.Flush();
                return text.ToString() + "\n";
            }
        }

        private static JObject Parse(string json)
        {
            using (var text = new StringReader(json))
            using (var reader = new JsonTextReader(text) { DateParseHandling = DateParseHandling.None })
                return JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
        }

        private static JToken Canonical(JToken token, string field)
        {
            if (token is JObject row)
                return new JObject(row.Properties()
                    .OrderBy(property => Array.IndexOf(FieldOrder, property.Name))
                    .ThenBy(property => property.Name, StringComparer.Ordinal)
                    .Select(property => new JProperty(property.Name, Canonical(property.Value, property.Name))));
            if (token is JArray array)
            {
                var values = array.AsEnumerable();
                if (field == "entities") values = values.OrderBy(value => Number(value, "id"));
                else if (field == "plates" || field == "gates") values = values.OrderBy(value => Number(value, "terrainId"));
                else if (field == "allowedContentIds") values = values.OrderBy(Text, StringComparer.Ordinal);
                else if (field == "variants" || field == "outcomes") values = values.OrderBy(value => Text((value as JObject)?["id"]), StringComparer.Ordinal);
                else if (field == "items") values = values.OrderBy(value => Text((value as JObject)?["instanceId"]), StringComparer.Ordinal);
                else if (field == "resourceBudgets") values = values.OrderBy(value => Text((value as JObject)?["resourceFamilyId"]), StringComparer.Ordinal);
                return new JArray(values.Select(value => Canonical(value, field)));
            }
            return token.DeepClone();
        }

        private static string Text(JToken token) => token?.Type == JTokenType.String ? (string)token : token?.ToString(Formatting.None) ?? string.Empty;

        private static long Number(JToken token, string field) => long.TryParse(Text((token as JObject)?[field]),
            NumberStyles.Integer, CultureInfo.InvariantCulture, out long number) ? number : long.MinValue;

        private static void VerifyRoundTrip(JObject original, JObject decoded)
        {
            if (!JToken.DeepEquals(Canonical(original, string.Empty), Canonical(decoded, string.Empty)))
                throw new JsonSerializationException("Authoring JSON round-trip changed document data.");
        }
    }
}