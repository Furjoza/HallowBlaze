using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using HallowBlaze.Core.Persistence.Storage;
using HallowBlaze.Core.Session;
using HallowBlaze.Core.State;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace HallowBlaze.Tests.PlayMode
{
    [TestFixture]
    public sealed class M1OwnershipPersistenceContractPlayModeTests
    {
        private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags StaticFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        private Type gameManagerType;
        private string persistenceRoot;
        private bool hadHighScore;
        private int originalHighScore;
        private UnityEngine.Random.State originalRandomState;

        [SetUp]
        public void SetUp()
        {
            hadHighScore = PlayerPrefs.HasKey("HighScore");
            originalHighScore = PlayerPrefs.GetInt("HighScore");
            originalRandomState = UnityEngine.Random.state;

            Assembly gameAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .First(assembly => assembly.GetName().Name == "Assembly-CSharp");
            gameManagerType = RequireType(gameAssembly, "GameManager");
            DestroyGameManagerSingleton();

            persistenceRoot = Path.Combine(
                Path.GetTempPath(),
                "HallowBlaze-M1.8-Ownership-" + Guid.NewGuid().ToString("N"));
            SetStaticField(gameManagerType, "PersistenceRootOverride", persistenceRoot);
            InvokeStatic(gameManagerType, "RequestNewRun");
            Time.timeScale = 1f;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            DestroyGameManagerSingleton();
            InvokeStatic(gameManagerType, "RequestNewRun");
            SetStaticField(gameManagerType, "PersistenceRootOverride", null);
            Time.timeScale = 1f;
            UnityEngine.Random.state = originalRandomState;

            Scene mainScene = SceneManager.GetSceneByName("Main");
            if (mainScene.IsValid() && mainScene.isLoaded)
            {
                if (SceneManager.GetActiveScene() == mainScene)
                {
                    Scene cleanupScene = SceneManager.CreateScene(
                        "M1.8 Cleanup " + Guid.NewGuid().ToString("N"));
                    Assert.That(SceneManager.SetActiveScene(cleanupScene), Is.True);
                }

                AsyncOperation unload = SceneManager.UnloadSceneAsync(mainScene);
                if (unload != null)
                {
                    while (!unload.isDone)
                        yield return null;
                }
            }

            if (IsOwnedPersistenceRoot() && Directory.Exists(persistenceRoot))
                Directory.Delete(persistenceRoot, true);

            if (hadHighScore)
                PlayerPrefs.SetInt("HighScore", originalHighScore);
            else
                PlayerPrefs.DeleteKey("HighScore");

            yield return null;
        }

        [UnityTest]
        public IEnumerator RepeatedContinueRecreatesBoardAndLastCommittedBoundary()
        {
            InvokeStatic(gameManagerType, "RequestNewRun");
            UnityEngine.Random.InitState(101);
            AsyncOperation firstLoad = SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            while (!firstLoad.isDone)
                yield return null;
            yield return null;

            Component firstManager = GetStaticField(gameManagerType, "instance") as Component;
            Assert.That(firstManager, Is.Not.Null);
            GameSession firstSession = GetProperty<GameSession>(firstManager, "Session");
            RunState firstRun = firstSession.ActiveRun;
            Assert.That(firstRun, Is.Not.Null);
            string layoutHash = GetGameplayLayoutHash();
            Vector3Int playerStart = GetPlayerGridPosition();
            firstSession.ConsumeFood(15);
            firstSession.TakeDamage(10);

            string runId = firstRun.RunId;
            int day = firstRun.CurrentDay;
            Assert.That((bool)Invoke(firstManager, "ExitToMenu"), Is.True);
            Assert.That(firstSession.ActiveRun, Is.Null);

            DestroyGameManagerSingleton();
            InvokeStatic(gameManagerType, "RequestContinue");
            UnityEngine.Random.InitState(202);
            AsyncOperation secondLoad = SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            while (!secondLoad.isDone)
                yield return null;
            yield return null;

            Component secondManager = GetStaticField(gameManagerType, "instance") as Component;
            Assert.That(secondManager, Is.Not.Null);
            Assert.That(secondManager, Is.Not.SameAs(firstManager));
            RunState continuedRun = GetProperty<GameSession>(secondManager, "Session").ActiveRun;
            Assert.That(continuedRun, Is.Not.Null);
            Assert.That(continuedRun.RunId, Is.EqualTo(runId));
            Assert.That(continuedRun.CurrentDay, Is.EqualTo(day));
            Assert.That(continuedRun.Food, Is.EqualTo(100));
            Assert.That(continuedRun.Health, Is.EqualTo(100));
            string continuedLayoutHash = GetGameplayLayoutHash();
            Assert.That(continuedLayoutHash, Is.EqualTo(layoutHash));
            Assert.That(GetPlayerGridPosition(), Is.EqualTo(playerStart));

            Assert.That((bool)Invoke(secondManager, "ExitToMenu"), Is.True);
            DestroyGameManagerSingleton();
            InvokeStatic(gameManagerType, "RequestContinue");
            UnityEngine.Random.InitState(303);
            AsyncOperation thirdLoad = SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            while (!thirdLoad.isDone)
                yield return null;
            yield return null;

            Component thirdManager = GetStaticField(gameManagerType, "instance") as Component;
            Assert.That(thirdManager, Is.Not.Null);
            Assert.That(thirdManager, Is.Not.SameAs(secondManager));
            RunState repeatedRun = GetProperty<GameSession>(thirdManager, "Session").ActiveRun;
            Assert.That(repeatedRun.RunId, Is.EqualTo(runId));
            Assert.That(repeatedRun.CurrentDay, Is.EqualTo(day));
            Assert.That(repeatedRun.Food, Is.EqualTo(100));
            Assert.That(repeatedRun.Health, Is.EqualTo(100));
            string repeatedLayoutHash = GetGameplayLayoutHash();
            Assert.That(repeatedLayoutHash, Is.EqualTo(layoutHash));
            Assert.That(GetPlayerGridPosition(), Is.EqualTo(playerStart));
            TestContext.WriteLine(
                "M1.10 layout hashes: initial={0}, continue1={1}, continue2={2}; playerStart={3}",
                layoutHash,
                continuedLayoutHash,
                repeatedLayoutHash,
                playerStart);
        }

        [UnityTest]
        public IEnumerator GameOverSavesProfileWithDeadSummaryAndRemovesRunSave()
        {
            Component manager = CreatePersistedRunManager("M1.8 GameOver Manager");
            GameSession session = GetProperty<GameSession>(manager, "Session");
            string runId = session.ActiveRun.RunId;

            Invoke(manager, "GameOver", false);

            AssertTerminalResult(manager, runId, RunStatus.Dead);
            yield return null;
        }

        [UnityTest]
        public IEnumerator WinGameSavesProfileWithWonSummaryAndRemovesRunSave()
        {
            Component manager = CreatePersistedRunManager("M1.8 Win Manager");
            GameSession session = GetProperty<GameSession>(manager, "Session");
            string runId = session.ActiveRun.RunId;

            Invoke(manager, "WinGame");

            AssertTerminalResult(manager, runId, RunStatus.Won);
            yield return null;
        }

        private Component CreatePersistedRunManager(string name)
        {
            Component manager = CreateManager(name);
            Invoke(manager, "EnsurePersistence");
            Invoke(manager, "StartNewRun");
            SaveStoreResult boundaryResult =
                GetField<RunLifecycleService>(manager, "lifecycle").SaveBoardBoundary();
            Assert.That(boundaryResult.IsSuccess, Is.True);
            Assert.That(File.Exists(Path.Combine(persistenceRoot, "run.backup.json")), Is.True);
            return manager;
        }

        private void AssertTerminalResult(
            Component manager,
            string runId,
            RunStatus expectedStatus)
        {
            GameSession session = GetProperty<GameSession>(manager, "Session");
            Assert.That(session.ActiveRun, Is.Null);

            FileSystemSaveStore store = new FileSystemSaveStore(persistenceRoot);
            SaveStoreResult<ProfileState> profileResult = store.LoadProfile();
            Assert.That(profileResult.IsSuccess, Is.True);
            Assert.That(profileResult.Data.RunSummaries, Has.Count.EqualTo(1));
            Assert.That(profileResult.Data.RunSummaries[0].RunId, Is.EqualTo(runId));
            Assert.That(profileResult.Data.RunSummaries[0].Status, Is.EqualTo(expectedStatus));
            Assert.That(store.LoadRun().Type, Is.EqualTo(SaveStoreResultType.Missing));
            Assert.That(File.Exists(Path.Combine(persistenceRoot, "run.json")), Is.False);
            Assert.That(File.Exists(Path.Combine(persistenceRoot, "run.backup.json")), Is.False);
        }

        private Component CreateManager(string name)
        {
            GameObject managerObject = new GameObject(name);
            return managerObject.AddComponent(gameManagerType);
        }

        private static string GetGameplayLayoutHash()
        {
            string[] gameplayTags = { "InnerWall", "Food", "Soda", "Aid", "Carrot", "Enemy" };
            GameObject[] gameplayObjects = UnityEngine.Object.FindObjectsByType<GameObject>(
                    FindObjectsSortMode.None)
                .Where(gameObject =>
                    gameObject.scene.name == "Main" && gameplayTags.Contains(gameObject.tag))
                .ToArray();
            Assert.That(gameplayObjects, Is.Not.Empty);

            string layout = string.Join("\n", gameplayObjects
                .Select(gameObject =>
                {
                    Vector3 position = gameObject.transform.position;
                    Assert.That(position.x, Is.EqualTo(Mathf.Round(position.x)));
                    Assert.That(position.y, Is.EqualTo(Mathf.Round(position.y)));
                    string objectName = gameObject.name.Replace("(Clone)", string.Empty);
                    return gameObject.tag + "|" + objectName + "|" +
                        Mathf.RoundToInt(position.x) + "|" + Mathf.RoundToInt(position.y);
                })
                .OrderBy(entry => entry, StringComparer.Ordinal));

            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(layout));
                return BitConverter.ToString(hash).Replace("-", string.Empty);
            }
        }

        private static Vector3Int GetPlayerGridPosition()
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            Assert.That(player, Is.Not.Null);
            Vector3 position = player.transform.position;
            Assert.That(position.x, Is.EqualTo(Mathf.Round(position.x)));
            Assert.That(position.y, Is.EqualTo(Mathf.Round(position.y)));
            return Vector3Int.RoundToInt(position);
        }

        private bool IsOwnedPersistenceRoot()
        {
            if (string.IsNullOrEmpty(persistenceRoot))
                return false;

            string fullRoot = Path.GetFullPath(persistenceRoot);
            string fullTemp = Path.GetFullPath(Path.GetTempPath())
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            return fullRoot.StartsWith(fullTemp, StringComparison.OrdinalIgnoreCase) &&
                Path.GetFileName(fullRoot).StartsWith(
                    "HallowBlaze-M1.8-Ownership-",
                    StringComparison.Ordinal);
        }

        private static Type RequireType(Assembly assembly, string name)
        {
            Type type = assembly.GetType(name);
            Assert.That(type, Is.Not.Null, name + " must exist in Assembly-CSharp.");
            return type;
        }

        private static object Invoke(Component target, string methodName, params object[] arguments)
        {
            MethodInfo method = target.GetType().GetMethod(methodName, InstanceFlags);
            Assert.That(method, Is.Not.Null, methodName + " must exist.");
            return method.Invoke(target, arguments);
        }

        private static object InvokeStatic(Type type, string methodName)
        {
            MethodInfo method = type.GetMethod(methodName, StaticFlags);
            Assert.That(method, Is.Not.Null, methodName + " must exist.");
            return method.Invoke(null, null);
        }

        private static T GetField<T>(Component target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(fieldName, InstanceFlags);
            Assert.That(field, Is.Not.Null, fieldName + " must exist.");
            return (T)field.GetValue(target);
        }

        private static T GetProperty<T>(Component target, string propertyName)
        {
            PropertyInfo property = target.GetType().GetProperty(propertyName, InstanceFlags);
            Assert.That(property, Is.Not.Null, propertyName + " must exist.");
            return (T)property.GetValue(target, null);
        }

        private static object GetStaticField(Type type, string fieldName)
        {
            FieldInfo field = type.GetField(fieldName, StaticFlags);
            Assert.That(field, Is.Not.Null, fieldName + " must exist.");
            return field.GetValue(null);
        }

        private static void SetStaticField(Type type, string fieldName, object value)
        {
            FieldInfo field = type.GetField(fieldName, StaticFlags);
            Assert.That(field, Is.Not.Null, fieldName + " must exist.");
            field.SetValue(null, value);
        }

        private void DestroyGameManagerSingleton()
        {
            if (gameManagerType == null)
                return;

            Component manager = GetStaticField(gameManagerType, "instance") as Component;
            if (manager != null)
                UnityEngine.Object.DestroyImmediate(manager.gameObject);
            gameManagerType.GetField("instance", StaticFlags).SetValue(null, null);
        }
    }
}
