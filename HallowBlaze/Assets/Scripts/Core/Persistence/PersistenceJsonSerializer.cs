using System;
using System.Globalization;
using System.IO;
using HallowBlaze.Core.Persistence.Dto;
using HallowBlaze.Core.Persistence.Mapping;
using HallowBlaze.Core.State;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HallowBlaze.Core.Persistence
{
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

        public static string SerializeProfile(ProfileState profile)
        {
            return Serialize(ProfileStateMapper.ToDto(profile));
        }

        public static ProfileState DeserializeProfile(string json)
        {
            ProfileStateDto dto = Deserialize<ProfileStateDto>(
                json,
                ProfileStateDto.CurrentSchemaVersion);
            return ProfileStateMapper.FromDto(dto);
        }

        public static string SerializeRun(RunState run)
        {
            return Serialize(RunStateMapper.ToDto(run));
        }

        public static RunState DeserializeRun(string json)
        {
            RunStateDto dto = Deserialize<RunStateDto>(
                json,
                RunStateDto.CurrentSchemaVersion);
            return RunStateMapper.FromDto(dto);
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
                return;
            }

            DtoValidation.RequireCurrentSchemaVersion(
                schemaVersion,
                currentSchemaVersion);
        }
    }
}