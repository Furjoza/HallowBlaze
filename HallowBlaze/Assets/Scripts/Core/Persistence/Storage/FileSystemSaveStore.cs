using System;
using System.IO;
using System.Security;
using System.Text;
using HallowBlaze.Core.Persistence.Dto;
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
        private readonly Action<string, string> copyFile;
        private readonly InventoryContent inventoryContent;

        /// <summary>
        /// Creates a file-backed save store with optional file-operation overrides for testing.
        /// </summary>
        /// <param name="rootPath">The directory that owns profile and run save files.</param>
        /// <param name="replaceFile">Optional atomic replacement operation.</param>
        /// <param name="copyFile">Optional snapshot copy operation.</param>
        /// <param name="inventoryContent">Trusted item/effect publication for both writes and reads; null selects empty initial content.</param>
        public FileSystemSaveStore(
            string rootPath,
            Action<string, string, string> replaceFile = null,
            Action<string, string> copyFile = null,
            InventoryContent inventoryContent = null)
        {
            if (string.IsNullOrWhiteSpace(rootPath))
                throw new ArgumentException("A save root path is required.", nameof(rootPath));

            this.rootPath = Path.GetFullPath(rootPath);
            this.replaceFile = replaceFile ?? File.Replace;
            this.copyFile = copyFile ?? File.Copy;
            this.inventoryContent = inventoryContent ?? InventoryContent.Empty;
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

        /// <summary>
        /// Replaces profile and run saves together, rolling both back if any new-game write fails.
        /// </summary>
        /// <param name="profile">The fresh profile state that starts the new game.</param>
        /// <param name="run">The first run owned by the fresh profile.</param>
        /// <returns>A typed persistence result.</returns>
        public SaveStoreResult ResetGame(ProfileState profile, RunState run)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));
            if (run == null)
                throw new ArgumentNullException(nameof(run));

            if (!inventoryContent.TryCreateInventory(run.Inventory.Placements, out _, out _))
                return SaveStoreResult.Corrupt("Required inventory content is unavailable; no files were changed.");

            string transactionId = Guid.NewGuid().ToString("N");
            string profilePath = Path.Combine(rootPath, ProfileFileName);
            string profileBackupPath = Path.Combine(rootPath, ProfileBackupFileName);
            string runPath = Path.Combine(rootPath, RunFileName);
            string runBackupPath = Path.Combine(rootPath, RunBackupFileName);
            string profileTemporaryPath = Path.Combine(
                rootPath,
                $".{ProfileFileName}.{transactionId}.tmp");
            string runTemporaryPath = Path.Combine(
                rootPath,
                $".{RunFileName}.{transactionId}.tmp");
            string profileSnapshotPath = profileTemporaryPath + ".previous";
            string profileBackupSnapshotPath = profileTemporaryPath + ".backup.previous";
            string runSnapshotPath = runTemporaryPath + ".previous";
            string runBackupSnapshotPath = runTemporaryPath + ".backup.previous";
            bool profileExisted = false;
            bool profileBackupExisted = false;
            bool runExisted = false;
            bool runBackupExisted = false;
            bool commitStarted = false;

            try
            {
                Directory.CreateDirectory(rootPath);
                WriteDurableFile(
                    profileTemporaryPath,
                    PersistenceJsonSerializer.SerializeProfile(profile));
                WriteDurableFile(
                    runTemporaryPath,
                    PersistenceJsonSerializer.SerializeRun(run));

                profileExisted = CaptureFile(profilePath, profileSnapshotPath);
                profileBackupExisted = CaptureFile(
                    profileBackupPath,
                    profileBackupSnapshotPath);
                runExisted = CaptureFile(runPath, runSnapshotPath);
                runBackupExisted = CaptureFile(runBackupPath, runBackupSnapshotPath);

                commitStarted = true;
                ReplaceCurrent(profileTemporaryPath, profilePath);
                ReplaceCurrent(runTemporaryPath, runPath);
                DeleteIfExists(profileBackupPath);
                DeleteIfExists(runBackupPath);
                return SaveStoreResult.Success();
            }
            catch (Exception exception) when (IsIoException(exception))
            {
                if (!commitStarted)
                    return SaveStoreResult.IoError(
                        "The new game could not prepare its save transaction.");

                bool rollbackSucceeded =
                    TryRestoreFile(profileSnapshotPath, profilePath, profileExisted) &
                    TryRestoreFile(
                        profileBackupSnapshotPath,
                        profileBackupPath,
                        profileBackupExisted) &
                    TryRestoreFile(runSnapshotPath, runPath, runExisted) &
                    TryRestoreFile(
                        runBackupSnapshotPath,
                        runBackupPath,
                        runBackupExisted);
                return SaveStoreResult.IoError(
                    rollbackSucceeded
                        ? "The new game could not replace the previous saves."
                        : "The new game failed and its previous saves could not be fully restored.");
            }
            finally
            {
                DeleteTemporaryFileBestEffort(profileTemporaryPath);
                DeleteTemporaryFileBestEffort(runTemporaryPath);
                DeleteTemporaryFileBestEffort(profileSnapshotPath);
                DeleteTemporaryFileBestEffort(profileBackupSnapshotPath);
                DeleteTemporaryFileBestEffort(runSnapshotPath);
                DeleteTemporaryFileBestEffort(runBackupSnapshotPath);
            }
        }

        public SaveStoreResult<ProfileState> LoadProfile()
        {
            return Load(
                ProfileFileName,
                ProfileBackupFileName,
                ProfileStateDto.CurrentSchemaVersion,
                PersistenceJsonSerializer.DeserializeProfile);
        }

        /// <summary>Atomically writes the entire boundary, refusing carried content this store could not subsequently read.</summary>
        /// <returns>A typed failure before touching files if required item/effect content is unavailable.</returns>
        /// <exception cref="ArgumentNullException">The run is null.</exception>
        public SaveStoreResult SaveRun(RunState run)
        {
            if (run == null)
                throw new ArgumentNullException(nameof(run));

            if (!inventoryContent.TryCreateInventory(run.Inventory.Placements, out _, out _))
                return SaveStoreResult.Corrupt("Required inventory content is unavailable; no files were changed.");

            return Save(
                RunFileName,
                RunBackupFileName,
                PersistenceJsonSerializer.SerializeRun(run));
        }

        /// <summary>Loads v1-v8 against trusted content; missing content never substitutes an older inventory from backup.</summary>
        /// <returns>Success, explicit backup recovery for corruption, or a typed failure without file mutation.</returns>
        public SaveStoreResult<RunState> LoadRun()
        {
            return Load(
                RunFileName,
                RunBackupFileName,
                RunStateV8Dto.CurrentSchemaVersion,
                json => PersistenceJsonSerializer.DeserializeRun(json, inventoryContent));
        }

        public SaveStoreResult DeleteRun()
        {
            try
            {
                DeleteIfExists(Path.Combine(rootPath, RunFileName));
                DeleteIfExists(Path.Combine(rootPath, RunBackupFileName));
                return SaveStoreResult.Success();
            }
            catch (Exception exception) when (IsIoException(exception))
            {
                return SaveStoreResult.IoError("The run save could not be removed.");
            }
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
            int currentSchemaVersion,
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
                    if (exception.Error == PersistenceDataError.MissingInventoryContent)
                        return SaveStoreResultExtensions.Corrupt<T>("Required inventory content is unavailable; the save was not substituted.");
                    if (IsFutureSchema(exception, currentJson, currentSchemaVersion))
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
                    return IsFutureSchema(exception, backupJson, currentSchemaVersion)
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
            string json,
            int currentSchemaVersion)
        {
            if (exception.Error != PersistenceDataError.UnsupportedSchemaVersion)
                return false;

            try
            {
                JToken version = JObject.Parse(json)["schemaVersion"];
                return version != null &&
                    version.Type == JTokenType.Integer &&
                    version.Value<int>() > currentSchemaVersion;
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

        private void ReplaceCurrent(string preparedPath, string currentPath)
        {
            if (File.Exists(currentPath))
                replaceFile(preparedPath, currentPath, null);
            else
                File.Move(preparedPath, currentPath);
        }

        private bool CaptureFile(string sourcePath, string snapshotPath)
        {
            if (!File.Exists(sourcePath))
                return false;

            copyFile(sourcePath, snapshotPath);
            return true;
        }

        private static bool TryRestoreFile(
            string snapshotPath,
            string targetPath,
            bool targetExisted)
        {
            try
            {
                if (targetExisted)
                    File.Copy(snapshotPath, targetPath, true);
                else
                    DeleteIfExists(targetPath);

                return true;
            }
            catch (Exception exception) when (IsIoException(exception))
            {
                return false;
            }
        }

        private static void WriteDurableFile(string path, string content)
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
                writer.Write(content);
                writer.Flush();
                stream.Flush(true);
            }
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

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}