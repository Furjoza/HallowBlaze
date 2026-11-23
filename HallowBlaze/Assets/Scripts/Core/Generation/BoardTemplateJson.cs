using HallowBlaze.Core.Generation.Landmarks;
using HallowBlaze.Core.Generation.Validation;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HallowBlaze.Core.Generation
{
    /// <summary>Reads versioned shared boards using the existing strict field/content decoder, without changing landmark schema 1.</summary>
    public static class BoardTemplateJson
    {
        /// <summary>Identifies shared authoring data, not a player-save schema or a silent landmark migration.</summary>
        public const int CurrentSchemaVersion = 2;
        /// <summary>Reads one detached shared board with one or more exits; unknown fields, content and versions are rejected.</summary>
        public static BoardTemplate Deserialize(string json, BoardValidationCatalog catalog) =>
            new BoardTemplate(LandmarkTemplateJson.ReadTemplate(json, catalog, true));

        /// <summary>Dispatches explicit schema versions without converting legacy data or weakening its reader.</summary>
        public static LandmarkTemplate ReadCompatible(string json, BoardValidationCatalog catalog)
        {
            JToken schema = JObject.Parse(json)["schemaVersion"];
            if (schema?.Type != JTokenType.Integer) throw new JsonSerializationException("An integer schemaVersion is required.");
            if ((int)schema == 1) return LandmarkTemplateJson.Deserialize(json, catalog);
            if ((int)schema == CurrentSchemaVersion) return Deserialize(json, catalog).Layout;
            throw new JsonSerializationException("Unsupported board schema version: " + schema);
        }
    }
}