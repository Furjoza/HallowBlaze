using System;
using System.IO;
using UnityEngine;

namespace HallowBlaze.Core.Persistence
{
    public static class PersistencePathProvider
    {
        public static string GetSaveRoot()
        {
            return Path.Combine(Application.persistentDataPath, "saves");
        }

        public static string GetTestSaveRoot(string testDirectoryName)
        {
            if (string.IsNullOrWhiteSpace(testDirectoryName) ||
                testDirectoryName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                testDirectoryName.Contains("..") ||
                testDirectoryName.Contains(Path.DirectorySeparatorChar.ToString()) ||
                testDirectoryName.Contains(Path.AltDirectorySeparatorChar.ToString()))
                throw new ArgumentException(
                    "A simple test directory name is required.",
                    nameof(testDirectoryName));

            return Path.Combine(
                Application.persistentDataPath,
                "tests",
                testDirectoryName);
        }
    }
}