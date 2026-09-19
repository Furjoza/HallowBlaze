using System;
using System.IO;
using System.Security;
using System.Text;
using HallowBlaze.Core.Persistence.Mapping;
using HallowBlaze.Core.State;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HallowBlaze.Core.Persistence.Storage
{
    /// <summary>
    /// Performs explicit, validated, backup-preserving migrations of persisted save files.
    /// </summary>
    public static class SaveMigration
    {
        public static SaveStoreResult MigrateProfileV0ToV1(
            string rootPath,
            Action<string, string, string> replaceFile = null)
        {
            return Migrate(
                rootPath,
                "profile.json",
                "profile.backup.json",
                0,
                1,
                null,
                ValidateProfileV1,
                replaceFile);
        }

        /// <summary>
        /// Migrates profile schema v1 discovery ID lists to schema v2 Sighted entries.
        /// </summary>
        /// <param name="rootPath">The isolated save root containing profile.json.</param>
        /// <param name="replaceFile">An optional atomic replacement operation for testing.</param>
        /// <returns>A typed result describing migration, compatibility, corruption, or I/O failure.</returns>
        public static SaveStoreResult MigrateProfileV1ToV2(
            string rootPath,
            Action<string, string, string> replaceFile = null)
        {
            return Migrate(
                rootPath,
                "profile.json",
                "profile.backup.json",
                1,
                2,
                UpgradeProfileV1Document,
                PersistenceJsonSerializer.DeserializeProfile,
                replaceFile);
        }

        public static SaveStoreResult MigrateRunV0ToV1(
            string rootPath,
            Action<string, string, string> replaceFile = null)
        {
            return Migrate(
                rootPath,
                "run.json",
                "run.backup.json",
                0,
                1,
                null,
                PersistenceJsonSerializer.DeserializeRun,
                replaceFile);
        }

        private static SaveStoreResult Migrate<T>(
            string rootPath,
            string fileName,
            string backupFileName,
            int sourceSchemaVersion,
            int targetSchemaVersion,
            Action<JObject> transform,
            Func<string, T> validate,
            Action<string, string, string> replaceFile)
        {
            if (string.IsNullOrWhiteSpace(rootPath))
                throw new ArgumentException("A save root path is required.", nameof(rootPath));

            string fullRootPath = Path.GetFullPath(rootPath);
            string currentPath = Path.Combine(fullRootPath, fileName);
            string backupPath = Path.Combine(fullRootPath, backupFileName);
            string temporaryPath = null;

            try
            {
                if (!File.Exists(currentPath))
                    return SaveStoreResult.Missing();

                string sourceJson = File.ReadAllText(currentPath, Encoding.UTF8);
                JObject document;
                int schemaVersion;
                try
                {
                    document = JObject.Parse(
                        sourceJson,
                        new JsonLoadSettings
                        {
                            DuplicatePropertyNameHandling =
                                DuplicatePropertyNameHandling.Error
                        });
                    JToken versionToken = document["schemaVersion"];
                    if (versionToken == null || versionToken.Type != JTokenType.Integer)
                        return SaveStoreResult.Corrupt();

                    schemaVersion = versionToken.Value<int>();
                }
                catch (Exception exception) when (
                    exception is JsonException ||
                    exception is FormatException ||
                    exception is InvalidCastException ||
                    exception is OverflowException)
                {
                    return SaveStoreResult.Corrupt();
                }

                if (schemaVersion > targetSchemaVersion)
                    return SaveStoreResult.UnsupportedFutureSchema();
                if (schemaVersion != sourceSchemaVersion)
                    return SaveStoreResult.Corrupt();

                document["schemaVersion"] = targetSchemaVersion;
                transform?.Invoke(document);
                string migratedJson = document.ToString(Formatting.Indented);
                try
                {
                    validate(migratedJson);
                }
                catch (PersistenceDataException)
                {
                    return SaveStoreResult.Corrupt();
                }

                Directory.CreateDirectory(fullRootPath);
                temporaryPath = Path.Combine(
                    fullRootPath,
                    $".{fileName}.migration.{Guid.NewGuid():N}.tmp");
                WriteDurably(temporaryPath, migratedJson);
                (replaceFile ?? File.Replace)(temporaryPath, currentPath, backupPath);
                temporaryPath = null;
                return SaveStoreResult.Success();
            }
            catch (Exception exception) when (IsIoException(exception))
            {
                return SaveStoreResult.IoError(
                    "The save file could not be migrated.");
            }
            finally
            {
                DeleteTemporaryFileBestEffort(temporaryPath);
            }
        }

        private static ProfileState ValidateProfileV1(string json)
        {
            JObject document = JObject.Parse(json);
            document["schemaVersion"] = 2;
            UpgradeProfileV1Document(document);
            return PersistenceJsonSerializer.DeserializeProfile(
                document.ToString(Formatting.Indented));
        }

        private static void UpgradeProfileV1Document(JObject document)
        {
            document["nodeDiscoveries"] = ConvertLegacyDiscoveries(
                document["discoveredNodeIds"],
                "nodeId");
            document["edgeDiscoveries"] = ConvertLegacyDiscoveries(
                document["discoveredEdgeIds"],
                "edgeId");
            document.Remove("discoveredNodeIds");
            document.Remove("discoveredEdgeIds");
        }

        private static JToken ConvertLegacyDiscoveries(
            JToken legacyIds,
            string idPropertyName)
        {
            if (!(legacyIds is JArray idArray))
                return legacyIds?.DeepClone() ?? JValue.CreateNull();

            JArray discoveries = new JArray();
            foreach (JToken id in idArray)
            {
                discoveries.Add(new JObject
                {
                    [idPropertyName] = id.DeepClone(),
                    ["state"] = "sighted"
                });
            }

            return discoveries;
        }

        private static void WriteDurably(string path, string json)
        {
            using (FileStream stream = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None))
            using (StreamWriter writer = new StreamWriter(
                stream,
                new UTF8Encoding(false),
                1024,
                true))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(true);
            }
        }

        private static bool IsIoException(Exception exception)
        {
            return exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is SecurityException;
        }

        private static void DeleteTemporaryFileBestEffort(string path)
        {
            if (string.IsNullOrEmpty(path))
                return;

            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception exception) when (IsIoException(exception))
            {
            }
        }
    }
}