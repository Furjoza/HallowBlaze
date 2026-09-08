using System;
using System.IO;
using System.Security;
using System.Text;
using HallowBlaze.Core.Persistence.Mapping;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HallowBlaze.Core.Persistence.Storage
{
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
                PersistenceJsonSerializer.DeserializeRun,
                replaceFile);
        }

        private static SaveStoreResult Migrate<T>(
            string rootPath,
            string fileName,
            string backupFileName,
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

                if (schemaVersion > 1)
                    return SaveStoreResult.UnsupportedFutureSchema();
                if (schemaVersion != 0)
                    return SaveStoreResult.Corrupt();

                document["schemaVersion"] = 1;
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