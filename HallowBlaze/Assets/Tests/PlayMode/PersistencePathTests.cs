using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace HallowBlaze.Tests.PlayMode
{
    public sealed class PersistencePathTests
    {
        private string testRootPath;

        [SetUp]
        public void SetUp()
        {
            Type providerType = AppDomain.CurrentDomain.GetAssemblies()
                .First(assembly => assembly.GetName().Name == "Assembly-CSharp")
                .GetType("HallowBlaze.Core.Persistence.PersistencePathProvider", true);
            MethodInfo getTestSaveRoot = providerType.GetMethod(
                "GetTestSaveRoot",
                BindingFlags.Public | BindingFlags.Static);
            string testName = "M1.6-" + Guid.NewGuid().ToString("N");
            testRootPath = (string)getTestSaveRoot.Invoke(null, new object[] { testName });
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(testRootPath))
                Directory.Delete(testRootPath, true);
        }

        [Test]
        public void TestRootIsIsolatedAndOwnedByTheTest()
        {
            string expectedParent = Path.GetFullPath(
                Path.Combine(Application.persistentDataPath, "tests"));
            string actualRoot = Path.GetFullPath(testRootPath);

            Assert.That(
                actualRoot.StartsWith(
                    expectedParent + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase),
                Is.True);
            Assert.That(
                actualRoot,
                Is.Not.EqualTo(Path.Combine(Application.persistentDataPath, "saves")));

            Directory.CreateDirectory(actualRoot);
            File.WriteAllText(Path.Combine(actualRoot, "owned.txt"), "owned");
            Assert.That(File.Exists(Path.Combine(actualRoot, "owned.txt")), Is.True);
        }
    }
}