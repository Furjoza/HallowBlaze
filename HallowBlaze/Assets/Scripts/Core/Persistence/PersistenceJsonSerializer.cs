using System;
using System.Globalization;
using System.IO;
using HallowBlaze.Core.Persistence.Dto;
using HallowBlaze.Core.Persistence.Mapping;
using HallowBlaze.Core.State;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace HallowBlaze.Core.Persistence
{
    /// <summary>
    /// Serializes and validates current profile and run persistence documents.
    /// </summary>
    public static class PersistenceJsonSerializer
    {
        private static readonly JsonSerializerSettings SerializerSettings =
            new JsonSerializerSettings
            {
                Culture = CultureInfo.InvariantCulture,
                Formatting = Formatting.Indented,
                MissingMemberHandling = MissingMemberHandling.Error,
                NullValueHandling = NullValueHandling.Include
            };

        /// <summary>Serializes a profile as a complete schema v2 JSON document.</summary>
        /// <param name="profile">The validated profile to serialize.</param>
        /// <returns>The profile JSON document.</returns>
        public static string SerializeProfile(ProfileState profile)
        {
            return Serialize(ProfileStateMapper.ToDto(profile));
        }

        /// <summary>Validates and materializes a complete schema v2 profile document.</summary>
        /// <param name="json">The profile JSON document.</param>
        /// <returns>The materialized profile.</returns>
        public static ProfileState DeserializeProfile(string json)
        {
            ProfileStateDto dto = Deserialize<ProfileStateDto>(
                json,
                ProfileStateDto.CurrentSchemaVersion);
            return ProfileStateMapper.FromDto(dto);
        }

        /// <summary>Writes the validated v8 inventory/resource/selection/landmark/equipment/travel boundary; profile is unchanged.</summary>
        public static string SerializeRun(RunState run)
        {
            return SerializeTravelRun(run);
        }

        /// <summary>Reads v1-v8 with empty default item content; nonempty v8 requires the trusted-content overload.</summary>
        public static RunState DeserializeRun(string json)
        {
            return DeserializeTravelRun(json);
        }

        /// <summary>Reads v1-v8 using explicit trusted inventory definitions/effects; legacy saves receive empty inventory and revision zero.</summary>
        /// <exception cref="ArgumentNullException">The trusted content is null.</exception>
        /// <exception cref="PersistenceDataException">The schema, boundary or any required inventory reference is invalid.</exception>
        public static RunState DeserializeRun(string json, InventoryContent content) => DeserializeTravelRun(json, content);

        /// <summary>Writes all carried ownership and one resource configuration; no mid-board snapshot or draft is implied.</summary>
        public static string SerializeTravelRun(RunState run) => Serialize(RunStateV8Mapper.ToDto(run));

        /// <summary>Reads v1-v8 with empty default item content, retaining historical resource and RNG migration rules.</summary>
        /// <exception cref="PersistenceDataException">Schema, JSON structure or required references are invalid.</exception>
        public static RunState DeserializeTravelRun(string json) => DeserializeTravelRun(json, InventoryContent.Empty);

        /// <summary>Restores complete carried ownership against caller-supplied content; never drops unknown items or effects.</summary>
        /// <exception cref="ArgumentNullException">The trusted content is null.</exception>
        /// <exception cref="PersistenceDataException">Schema, JSON structure or required references are invalid.</exception>
        public static RunState DeserializeTravelRun(string json, InventoryContent content)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            RunState run = ReadTravelRun(json, content);
            if (!ReferenceEquals(run.InventoryContent, content))
                run.RestoreInventoryState(content, run.InventoryRevision, run.Inventory.Placements);
            return run;
        }

        private static RunState ReadTravelRun(string json, InventoryContent content)
        {
            JObject document = ParseDocument(json);
            int version = ReadSchemaVersion(document);
            if (version == RunStateDto.CurrentSchemaVersion)
                return RunStateV2Mapper.FromDto(RunStateV1Migration.Migrate(Deserialize<RunStateDto>(json, version)));
            if (version == RunStateV2Dto.CurrentSchemaVersion)
            {
                ValidateTravelTokenTypes(document, typeof(RunStateV2Dto), string.Empty);
                return RunStateV2Mapper.FromDto(Deserialize<RunStateV2Dto>(json, version));
            }
            if (version == RunStateV3Dto.CurrentSchemaVersion)
            {
                ValidateTravelTokenTypes(document, typeof(RunStateV3Dto), string.Empty);
                return RunStateV3Mapper.FromDto(Deserialize<RunStateV3Dto>(json, version));
            }
            if (version == RunStateV4Dto.CurrentSchemaVersion)
            {
                ValidateTravelTokenTypes(document, typeof(RunStateV4Dto), string.Empty);
                return RunStateV4Mapper.FromDto(Deserialize<RunStateV4Dto>(json, version));
            }
            if (version == RunStateV5Dto.CurrentSchemaVersion)
            {
                ValidateTravelTokenTypes(document, typeof(RunStateV5Dto), string.Empty);
                return RunStateV5Mapper.FromDto(Deserialize<RunStateV5Dto>(json, version));
            }
            if (version == RunStateV6Dto.CurrentSchemaVersion)
            {
                ValidateTravelTokenTypes(document, typeof(RunStateV6Dto), string.Empty);
                return RunStateV6Mapper.FromDto(Deserialize<RunStateV6Dto>(json, version));
            }
            if (version == RunStateV7Dto.CurrentSchemaVersion)
            {
                ValidateTravelTokenTypes(document, typeof(RunStateV7Dto), string.Empty);
                return RunStateV7Mapper.FromDto(Deserialize<RunStateV7Dto>(json, version));
            }
            DtoValidation.RequireCurrentSchemaVersion(version, RunStateV8Dto.CurrentSchemaVersion);
            ValidateTravelTokenTypes(document, typeof(RunStateV8Dto), string.Empty);
            return RunStateV8Mapper.FromDto(Deserialize<RunStateV8Dto>(json, version), content);
        }

        private static void ValidateTravelTokenTypes(JToken token, Type expectedType, string path)
        {
            if (token == null || token.Type == JTokenType.Null)
                return;
            expectedType = Nullable.GetUnderlyingType(expectedType) ?? expectedType;
            if (expectedType == typeof(int) || expectedType == typeof(string))
            {
                JTokenType required = expectedType == typeof(int) ? JTokenType.Integer : JTokenType.String;
                if (token.Type != required)
                    DtoValidation.ThrowInvalidValue(path);
                if (expectedType == typeof(int) && !int.TryParse(token.ToString(Formatting.None),
                    System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out _))
                    DtoValidation.ThrowInvalidValue(path);
                return;
            }
            if (expectedType.IsArray)
            {
                if (!(token is JArray array))
                {
                    DtoValidation.ThrowInvalidValue(path);
                    return;
                }
                for (int index = 0; index < array.Count; index++)
                    ValidateTravelTokenTypes(array[index], expectedType.GetElementType(), path + "[" + index + "]");
                return;
            }
            if (!(token is JObject document))
            {
                DtoValidation.ThrowInvalidValue(path);
                return;
            }
            var contract = (JsonObjectContract)JsonSerializer.Create(SerializerSettings).ContractResolver.ResolveContract(expectedType);
            foreach (JProperty field in document.Properties())
            {
                string fieldPath = path.Length == 0 ? field.Name : path + "." + field.Name;
                JsonProperty property = contract.Properties.GetProperty(field.Name, StringComparison.Ordinal);
                if (property == null || property.Ignored)
                    DtoValidation.ThrowInvalidValue(fieldPath);
                ValidateTravelTokenTypes(field.Value, property.PropertyType, fieldPath);
            }
        }

        private static string Serialize<T>(T dto)
        {
            return JsonConvert.SerializeObject(dto, SerializerSettings);
        }

        private static T Deserialize<T>(string json, int currentSchemaVersion)
        {
            JObject document = ParseDocument(json);
            ValidateSchemaVersion(document, currentSchemaVersion);

            try
            {
                JsonSerializer serializer = JsonSerializer.Create(SerializerSettings);
                T dto = document.ToObject<T>(serializer);
                if (dto == null)
                {
                    throw new PersistenceDataException(
                        PersistenceDataError.InvalidJson,
                        string.Empty,
                        "The persisted document could not be read.");
                }

                return dto;
            }
            catch (JsonSerializationException)
            {
                throw new PersistenceDataException(
                    PersistenceDataError.MissingField,
                    string.Empty,
                    "The persisted document has missing or invalid fields.");
            }
        }

        private static JObject ParseDocument(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new PersistenceDataException(
                    PersistenceDataError.EmptyDocument,
                    string.Empty,
                    "The persisted document is empty.");
            }

            try
            {
                using (StringReader stringReader = new StringReader(json))
                using (JsonTextReader jsonReader = new JsonTextReader(stringReader))
                {
                    jsonReader.DateParseHandling = DateParseHandling.None;
                    JToken token = JToken.ReadFrom(
                        jsonReader,
                        new JsonLoadSettings
                        {
                            DuplicatePropertyNameHandling =
                                DuplicatePropertyNameHandling.Error,
                            LineInfoHandling = LineInfoHandling.Load
                        });

                    if (jsonReader.Read() || !(token is JObject document))
                    {
                        throw new PersistenceDataException(
                            PersistenceDataError.InvalidJson,
                            string.Empty,
                            "The persisted document must be one JSON object.");
                    }

                    return document;
                }
            }
            catch (JsonReaderException)
            {
                throw new PersistenceDataException(
                    PersistenceDataError.InvalidJson,
                    string.Empty,
                    "The persisted document is not valid JSON.");
            }
        }

        private static void ValidateSchemaVersion(
            JObject document,
            int currentSchemaVersion)
        {
            DtoValidation.RequireCurrentSchemaVersion(ReadSchemaVersion(document), currentSchemaVersion);
        }

        private static int ReadSchemaVersion(JObject document)
        {
            if (!document.TryGetValue(
                    "schemaVersion",
                    StringComparison.Ordinal,
                    out JToken versionToken))
            {
                throw new PersistenceDataException(
                    PersistenceDataError.MissingField,
                    "schemaVersion",
                    "The persisted document has no schema version.");
            }

            if (versionToken.Type != JTokenType.Integer)
                DtoValidation.ThrowInvalidValue("schemaVersion");

            int schemaVersion;
            try
            {
                schemaVersion = versionToken.Value<int>();
            }
            catch (Exception exception) when (
                exception is FormatException ||
                exception is InvalidCastException ||
                exception is OverflowException)
            {
                DtoValidation.ThrowInvalidValue("schemaVersion");
                return 0;
            }

            return schemaVersion;
        }
    }
}