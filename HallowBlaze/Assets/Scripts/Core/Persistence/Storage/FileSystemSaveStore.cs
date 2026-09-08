using System;
using System.IO;
using System.Security;
using System.Text;
using HallowBlaze.Core.Persistence.Mapping;
using HallowBlaze.Core.State;
using Newtonsoft.Json.Linq;

namespace HallowBlaze.Core.Persistence.Storage
{
    public sealed class FileSystemSaveStore : ISaveStore
    {
        private const string ProfileFileName = "profile.json";
        private const string ProfileBackupFileName = "profile.backup.json";
        private const string RunFileName = "run.json";
        private const string RunBackupFileName = "run.backup.json";

        private readonly string rootPath;
        private readonly Action<string, string, string> replaceFile;

        public FileSystemSaveStore(
            string rootPath,
            Action<string, string, string> replaceFile = null)
        {
            if (string.IsNullOrWhiteSpace(rootPath))
                throw new ArgumentException("A save root path is required.", nameof(rootPath));

            this.rootPath = Path.GetFullPath(rootPath);
            this.replaceFile = replaceFile ?? File.Replace;
        }

        public SaveStoreResult SaveProfile(ProfileState profile)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));

            return Save(
                ProfileFileName,
                ProfileBackupFileName,
                PersistenceJsonSerializer.SerializeProfile(profile));
        }

        public SaveStoreResult<ProfileState> LoadProfile()
        {
            return Load(
                ProfileFileName,
                ProfileBackupFileName,
                PersistenceJsonSerializer.DeserializeProfile);
        }

        public SaveStoreResult SaveRun(RunState run)
        {
            if (run == null)
                throw new ArgumentNullException(nameof(run));

            return Save(
                RunFileName,
                RunBackupFileName,
                PersistenceJsonSerializer.SerializeRun(run));
        }

        public SaveStoreResult<RunState> LoadRun()
        {
            return Load(
                RunFileName,
                RunBackupFileName,
                PersistenceJsonSerializer.DeserializeRun);
        }

        private SaveStoreResult Save(
            string fileName,
            string backupFileName,
            string json)
        {
            string temporaryPath = null;
            try
            {
                Directory.CreateDirectory(rootPath);
                string currentPath = Path.Combine(rootPath, fileName);
                string backupPath = Path.Combine(rootPath, backupFileName);
                temporaryPath = Path.Combine(
                    rootPath,
                    $".{fileName}.{Guid.NewGuid():N}.tmp");

                using (FileStream stream = new FileStream(
                    temporaryPath,
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

                if (File.Exists(currentPath))
                    replaceFile(temporaryPath, currentPath, backupPath);
                else
                    File.Move(temporaryPath, currentPath);

                temporaryPath = null;
                return SaveStoreResult.Success();
            }
            catch (Exception exception) when (IsIoException(exception))
            {
                return SaveStoreResult.IoError("The save file could not be written.");
            }
            finally
            {
                DeleteTemporaryFileBestEffort(temporaryPath);
            }
        }

        private SaveStoreResult<T> Load<T>(
            string fileName,
            string backupFileName,
            Func<string, T> deserialize)
        {
            string currentPath = Path.Combine(rootPath, fileName);
            string backupPath = Path.Combine(rootPath, backupFileName);

            try
            {
                if (!File.Exists(currentPath))
                    return SaveStoreResultExtensions.Missing<T>();

                string currentJson = File.ReadAllText(currentPath, Encoding.UTF8);
                try
                {
                    return SaveStoreResultExtensions.Success(deserialize(currentJson));
                }
                catch (PersistenceDataException exception)
                {
                    if (IsFutureSchema(exception, currentJson))
                        return SaveStoreResultExtensions.UnsupportedFutureSchema<T>();
                }

                if (!File.Exists(backupPath))
                    return SaveStoreResultExtensions.Corrupt<T>();

                string backupJson = File.ReadAllText(backupPath, Encoding.UTF8);
                try
                {
                    return SaveStoreResultExtensions.Recovered(deserialize(backupJson));
                }
                catch (PersistenceDataException exception)
                {
                    return IsFutureSchema(exception, backupJson)
                        ? SaveStoreResultExtensions.UnsupportedFutureSchema<T>()
                        : SaveStoreResultExtensions.Corrupt<T>();
                }
            }
            catch (Exception exception) when (IsIoException(exception))
            {
                return SaveStoreResultExtensions.IoError<T>(
                    "The save file could not be read.");
            }
        }

        private static bool IsFutureSchema(
            PersistenceDataException exception,
            string json)
        {
            if (exception.Error != PersistenceDataError.UnsupportedSchemaVersion)
                return false;

            try
            {
                JToken version = JObject.Parse(json)["schemaVersion"];
                return version != null &&
                    version.Type == JTokenType.Integer &&
                    version.Value<int>() > 1;
            }
            catch
            {
                return false;
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