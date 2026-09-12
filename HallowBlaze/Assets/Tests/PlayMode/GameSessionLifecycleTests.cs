using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using HallowBlaze.Core.Session;
using HallowBlaze.Core.State;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace HallowBlaze.Tests.PlayMode
{
    public sealed class GameSessionLifecycleTests
    {
        private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private Type gameManagerType;
        private Type playerType;
        private Type restartButtonType;
        private Type soundManagerType;
        private bool hadHighScore;
        private int originalHighScore;
        private string persistenceRoot;

        [SetUp]
        public void SetUp()
        {
            hadHighScore = PlayerPrefs.HasKey("HighScore");
            originalHighScore = PlayerPrefs.GetInt("HighScore");

            Assembly gameAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .First(assembly => assembly.GetName().Name == "Assembly-CSharp");
            gameManagerType = RequireType(gameAssembly, "GameManager");
            playerType = RequireType(gameAssembly, "PlayerScript");
            restartButtonType = RequireType(gameAssembly, "RestartBttnScript");
            DestroyGameManagerSingleton();
            persistenceRoot = Path.Combine(
                Path.GetTempPath(),
                "HallowBlaze-M1.7-Lifecycle-" + Guid.NewGuid().ToString("N"));
            SetStaticField(gameManagerType, "PersistenceRootOverride", persistenceRoot);
            InvokeStatic(gameManagerType, "RequestNewRun");
        }

        [TearDown]
        public void TearDown()
        {
            DestroyGameManagerSingleton();
            SetStaticField(gameManagerType, "PersistenceRootOverride", null);
            Time.timeScale = 1f;
            if (Directory.Exists(persistenceRoot))
                Directory.Delete(persistenceRoot, true);

            if (hadHighScore)
                PlayerPrefs.SetInt("HighScore", originalHighScore);
            else
                PlayerPrefs.DeleteKey("HighScore");
        }

        [Test]
        public void NewRunCreatesFreshStateAndPreservesProfile()
        {
            Component manager = CreateManager("M1.4 Lifecycle Manager");
            Invoke(manager, "StartNewRun");
            GameSession session = GetProperty<GameSession>(manager, "Session");
            ProfileState profile = session.Profile;
            RunState firstRun = session.ActiveRun;
            session.ConsumeFood(25);
            session.TakeDamage(15);

            Invoke(manager, "StartNewRun");

            Assert.That(session.Profile, Is.SameAs(profile));
            Assert.That(session.ActiveRun, Is.Not.SameAs(firstRun));
            Assert.That(session.ActiveRun.Food, Is.EqualTo(100));
            Assert.That(session.ActiveRun.Health, Is.EqualTo(100));
            Assert.That(session.ActiveRun.CurrentDay, Is.Zero);
            Assert.That(session.ActiveRun.Status, Is.EqualTo(RunStatus.Active));
        }

        [UnityTest]
        public IEnumerator ActiveSceneBoundaryPreservesSessionAndRun()
        {
            Component manager = CreateManager("M1.4 Scene Manager");
            Invoke(manager, "StartNewRun");
            GameSession session = GetProperty<GameSession>(manager, "Session");
            RunState run = session.ActiveRun;
            session.ConsumeFood(13);
            session.TakeDamage(7);
            Scene originalScene = SceneManager.GetActiveScene();
            Scene nextScene = SceneManager.CreateScene("M1.4 Session Boundary " + Guid.NewGuid().ToString("N"));

            Assert.That(SceneManager.SetActiveScene(nextScene), Is.True);
            yield return null;

            Assert.That(GetStaticField(gameManagerType, "instance"), Is.SameAs(manager));
            Assert.That(GetProperty<GameSession>(manager, "Session"), Is.SameAs(session));
            Assert.That(session.ActiveRun, Is.SameAs(run));
            Assert.That(run.Food, Is.EqualTo(87));
            Assert.That(run.Health, Is.EqualTo(93));

            Assert.That(SceneManager.SetActiveScene(originalScene), Is.True);
            yield return SceneManager.UnloadSceneAsync(nextScene);
        }

        [UnityTest]
        public IEnumerator RealMainSceneReloadPreservesRunAndRebindsPlayerUi()
        {
            AsyncOperation firstLoad = SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            while (!firstLoad.isDone)
                yield return null;
            yield return null;

            Component manager = GetStaticField(gameManagerType, "instance") as Component;
            Assert.That(manager, Is.Not.Null);
            GameSession session = GetProperty<GameSession>(manager, "Session");
            RunState run = session.ActiveRun;
            Assert.That(run, Is.Not.Null);
            Assert.That(run.CurrentDay, Is.EqualTo(1));

            session.ConsumeFood(14);
            session.TakeDamage(11);
            Component firstPlayer = FindSceneComponent(playerType, "Main");
            Assert.That(firstPlayer, Is.Not.Null);
            int firstPlayerInstanceId = firstPlayer.GetInstanceID();
            Assert.That(GetField<Text>(firstPlayer, "foodText").text, Is.EqualTo("Food: 86"));
            Assert.That(GetField<Text>(firstPlayer, "healthText").text, Is.EqualTo("Health: 89"));

            AsyncOperation reload = SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            while (!reload.isDone)
                yield return null;
            yield return null;

            Component reloadedManager = GetStaticField(gameManagerType, "instance") as Component;
            Component replacementPlayer = FindSceneComponent(playerType, "Main");
            Assert.That(reloadedManager, Is.SameAs(manager));
            Assert.That(GetProperty<GameSession>(reloadedManager, "Session"), Is.SameAs(session));
            Assert.That(session.ActiveRun, Is.SameAs(run));
            Assert.That(run.CurrentDay, Is.EqualTo(2));
            Assert.That(run.Food, Is.EqualTo(86));
            Assert.That(run.Health, Is.EqualTo(89));
            Assert.That(replacementPlayer, Is.Not.Null);
            Assert.That(replacementPlayer.GetInstanceID(), Is.Not.EqualTo(firstPlayerInstanceId));
            Assert.That(GetField<Text>(replacementPlayer, "foodText").text, Is.EqualTo("Food: 86"));
            Assert.That(GetField<Text>(replacementPlayer, "healthText").text, Is.EqualTo("Health: 89"));
            Assert.That(GameObject.Find("LevelText").GetComponent<Text>().text, Is.EqualTo("Day: 2"));

            ProfileState profile = session.Profile;
            GameObject restartButton = GetField<GameObject>(manager, "restartButton");
            GameObject menuButton = GetField<GameObject>(manager, "menuButton");
            Assert.That(restartButton, Is.Not.Null);
            Assert.That(menuButton, Is.Not.Null);
            Assert.That(restartButton.activeSelf, Is.False);
            Assert.That(menuButton.activeSelf, Is.False);
            PlayerPrefs.SetInt("HighScore", 0);

            Invoke(replacementPlayer, "LoseHealth", run.Health);

            Assert.That(run.Health, Is.Zero);
            Assert.That(run.Status, Is.EqualTo(RunStatus.Dead));
            Assert.That(((Behaviour)manager).enabled, Is.False);
            Assert.That(restartButton.activeSelf, Is.True);
            Assert.That(menuButton.activeSelf, Is.True);
            Assert.That(PlayerPrefs.GetInt("HighScore"), Is.EqualTo(2));

            Component restartController = restartButton.GetComponent(restartButtonType);
            Assert.That(restartController, Is.Not.Null);
            Button restartButtonControl = restartButton.GetComponent<Button>();
            Assert.That(restartButtonControl, Is.Not.Null);
            Assert.That(restartButtonControl.onClick.GetPersistentEventCount(), Is.EqualTo(1));
            Assert.That(restartButtonControl.onClick.GetPersistentTarget(0), Is.SameAs(restartController));
            Assert.That(restartButtonControl.onClick.GetPersistentMethodName(0), Is.EqualTo("Restart"));
            restartButtonControl.onClick.Invoke();
            yield return null;

            RunState restartedRun = session.ActiveRun;
            Assert.That(GetProperty<GameSession>(manager, "Session"), Is.SameAs(session));
            Assert.That(session.Profile, Is.SameAs(profile));
            Assert.That(restartedRun, Is.Not.SameAs(run));
            Assert.That(restartedRun.Status, Is.EqualTo(RunStatus.Active));
            Assert.That(restartedRun.CurrentDay, Is.EqualTo(1));
            Assert.That(restartedRun.Food, Is.EqualTo(100));
            Assert.That(restartedRun.Health, Is.EqualTo(100));
            Assert.That(((Behaviour)manager).enabled, Is.True);
            Assert.That(GameObject.Find("LevelText").GetComponent<Text>().text, Is.EqualTo("Day: 1"));

            Scene cleanupScene = SceneManager.CreateScene("M1.4 Smoke Cleanup " + Guid.NewGuid().ToString("N"));
            Assert.That(SceneManager.SetActiveScene(cleanupScene), Is.True);
            yield return SceneManager.UnloadSceneAsync("Main");
        }

        [UnityTest]
        public IEnumerator GameOverMenuButtonLoadsMenuWithoutNewRun()
        {
            AsyncOperation mainLoad = SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            while (!mainLoad.isDone)
                yield return null;
            yield return null;
            AssertSceneHasNoMissingComponents("Main");

            Component manager = GetStaticField(gameManagerType, "instance") as Component;
            Assert.That(manager, Is.Not.Null);
            GameSession session = GetProperty<GameSession>(manager, "Session");
            ProfileState profile = session.Profile;
            RunState completedRun = session.ActiveRun;
            Component player = FindSceneComponent(playerType, "Main");
            Assert.That(player, Is.Not.Null);

            Invoke(player, "LoseHealth", completedRun.Health);
            GameObject menuButton = GameObject.Find("MenuBttn");
            Assert.That(menuButton, Is.Not.Null);
            Assert.That(menuButton.activeSelf, Is.True);
            Component menuController = menuButton.GetComponent(restartButtonType);
            Assert.That(menuController, Is.Not.Null);
            Button menuButtonControl = menuButton.GetComponent<Button>();
            Assert.That(menuButtonControl, Is.Not.Null);
            Assert.That(menuButtonControl.onClick.GetPersistentEventCount(), Is.EqualTo(1));
            Assert.That(menuButtonControl.onClick.GetPersistentTarget(0), Is.SameAs(menuController));
            Assert.That(menuButtonControl.onClick.GetPersistentMethodName(0), Is.EqualTo("ReturnToMenu"));

            Time.timeScale = 0f;
            Invoke(manager, "SetGameplayInputBlocked", true);
            menuButtonControl.onClick.Invoke();

            float loadDeadline = Time.realtimeSinceStartup + 5f;
            while (SceneManager.GetActiveScene().name != "Menu" &&
                Time.realtimeSinceStartup < loadDeadline)
                yield return null;
            yield return null;

            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("Menu"));
            AssertSceneHasNoMissingComponents("Menu");
            GameObject continueButton = GameObject.Find("ContinueBttn");
            Assert.That(continueButton, Is.Not.Null);
            Assert.That(continueButton.GetComponent<Button>().interactable, Is.False);
            Assert.That(GetStaticField(gameManagerType, "instance"), Is.SameAs(manager));
            Assert.That(((Behaviour)manager).enabled, Is.True);
            Assert.That(Time.timeScale, Is.EqualTo(1f));
            Assert.That(GetProperty<bool>(manager, "IsGameplayInputBlocked"), Is.False);
            Assert.That(session.Profile, Is.SameAs(profile));
            Assert.That(session.ActiveRun, Is.Null);
            Assert.That(profile.RunSummaries.Count, Is.EqualTo(1));
            Assert.That(profile.RunSummaries[0].RunId, Is.EqualTo(completedRun.RunId));
            Assert.That(profile.RunSummaries[0].Status, Is.EqualTo(RunStatus.Dead));
            Assert.That(File.Exists(Path.Combine(persistenceRoot, "profile.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(persistenceRoot, "run.json")), Is.False);

            Scene cleanupScene = SceneManager.CreateScene("M1.11 Menu Cleanup " + Guid.NewGuid().ToString("N"));
            Assert.That(SceneManager.SetActiveScene(cleanupScene), Is.True);
            yield return SceneManager.UnloadSceneAsync("Menu");
        }

        [UnityTest]
        public IEnumerator ContinueRestoresLastCommittedDayWithoutAdvancing()
        {
            AsyncOperation firstLoad = SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            while (!firstLoad.isDone)
                yield return null;
            yield return null;

            Component manager = GetStaticField(gameManagerType, "instance") as Component;
            GameSession firstSession = GetProperty<GameSession>(manager, "Session");
            RunState firstRun = firstSession.ActiveRun;
            Assert.That(firstRun.CurrentDay, Is.EqualTo(1));
            firstSession.ConsumeFood(9);
            Assert.That((bool)Invoke(manager, "ExitToMenu"), Is.True);
            Assert.That(firstSession.ActiveRun, Is.Null);

            InvokeStatic(gameManagerType, "RequestContinue");
            AsyncOperation reload = SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            while (!reload.isDone)
                yield return null;
            yield return null;

            GameSession continuedSession = GetProperty<GameSession>(manager, "Session");
            Assert.That(continuedSession.ActiveRun.RunId, Is.EqualTo(firstRun.RunId));
            Assert.That(continuedSession.ActiveRun.CurrentDay, Is.EqualTo(1));
            Assert.That(continuedSession.ActiveRun.Food, Is.EqualTo(91));

            Scene cleanupScene = SceneManager.CreateScene("M1.7 Continue Cleanup " + Guid.NewGuid().ToString("N"));
            Assert.That(SceneManager.SetActiveScene(cleanupScene), Is.True);
            yield return SceneManager.UnloadSceneAsync("Main");
        }

        [UnityTest]
        public IEnumerator DeadRunReturnsToMenuOnceWithoutStartingRun()
        {
            Component manager = CreateManager("M1.11 Game Over Manager");
            Invoke(manager, "StartNewRun");
            GameSession session = GetProperty<GameSession>(manager, "Session");
            ProfileState profile = session.Profile;
            string runId = session.ActiveRun.RunId;
            int sceneLoadCount = 0;
            string requestedScene = null;
            SetField(manager, "sceneLoader", (Action<string>)(sceneName =>
            {
                sceneLoadCount++;
                requestedScene = sceneName;
            }));

            Invoke(manager, "GameOver", false);
            Time.timeScale = 0f;
            Invoke(manager, "SetGameplayInputBlocked", true);

            Invoke(manager, "ReturnToMenuAfterGameOver");
            Invoke(manager, "ReturnToMenuAfterGameOver");

            Assert.That(sceneLoadCount, Is.EqualTo(1));
            Assert.That(requestedScene, Is.EqualTo("Menu"));
            Assert.That(Time.timeScale, Is.EqualTo(1f));
            Assert.That(GetProperty<bool>(manager, "IsGameplayInputBlocked"), Is.False);
            Assert.That(session.Profile, Is.SameAs(profile));
            Assert.That(session.ActiveRun, Is.Null);
            Assert.That(profile.RunSummaries.Count, Is.EqualTo(1));
            Assert.That(profile.RunSummaries[0].RunId, Is.EqualTo(runId));
            Assert.That(profile.RunSummaries[0].Status, Is.EqualTo(RunStatus.Dead));
            Assert.That(File.Exists(Path.Combine(persistenceRoot, "run.json")), Is.False);

            yield return null;
            Assert.That(GetStaticField(gameManagerType, "instance"), Is.SameAs(manager));
            Assert.That(((Behaviour)manager).enabled, Is.True);
        }

        [Test]
        public void GameOverMenuIsBlockedWhenTerminalizationFails()
        {
            Component manager = CreateManager("M1.11 Failed Game Over Manager");
            Invoke(manager, "EnsurePersistence");
            Invoke(manager, "StartNewRun");
            GameSession session = GetProperty<GameSession>(manager, "Session");
            int sceneLoadCount = 0;
            SetField(manager, "sceneLoader", (Action<string>)(_ => sceneLoadCount++));
            string profilePath = Path.Combine(persistenceRoot, "profile.json");
            File.Delete(profilePath);
            Directory.CreateDirectory(profilePath);

            LogAssert.Expect(LogType.Error, "Run completion save failed: IoError");
            Invoke(manager, "GameOver", false);
            Invoke(manager, "ReturnToMenuAfterGameOver");

            Assert.That(sceneLoadCount, Is.Zero);
            Assert.That(session.ActiveRun, Is.Not.Null);
            Assert.That(session.ActiveRun.Status, Is.EqualTo(RunStatus.Dead));
            Assert.That(File.Exists(Path.Combine(persistenceRoot, "run.json")), Is.True);
        }

        [UnityTest]
        public IEnumerator DeadRunRestartsOnceWithFreshRun()
        {
            Component manager = CreateManager("M1.11 Restart Manager");
            Invoke(manager, "StartNewRun");
            GameSession session = GetProperty<GameSession>(manager, "Session");
            ProfileState profile = session.Profile;
            RunState completedRun = session.ActiveRun;
            int sceneLoadCount = 0;
            SetField(manager, "sceneLoader", (Action<string>)(_ => sceneLoadCount++));

            Invoke(manager, "GameOver", false);
            Time.timeScale = 0f;
            Invoke(manager, "SetGameplayInputBlocked", true);

            Invoke(manager, "RestartGame");
            Invoke(manager, "RestartGame");

            Assert.That(sceneLoadCount, Is.EqualTo(1));
            Assert.That(Time.timeScale, Is.EqualTo(1f));
            Assert.That(GetProperty<bool>(manager, "IsGameplayInputBlocked"), Is.False);
            Assert.That(session.Profile, Is.SameAs(profile));
            Assert.That(profile.RunSummaries.Count, Is.EqualTo(1));
            Assert.That(session.ActiveRun, Is.Not.SameAs(completedRun));
            Assert.That(session.ActiveRun.Status, Is.EqualTo(RunStatus.Active));
            Assert.That(session.ActiveRun.CurrentDay, Is.Zero);
            Assert.That(session.ActiveRun.Food, Is.EqualTo(100));
            Assert.That(session.ActiveRun.Health, Is.EqualTo(100));

            yield return null;
        }

        [UnityTest]
        public IEnumerator DisabledAndRecreatedPlayerRendersSessionWithoutOwningResources()
        {
            Component manager = CreateManager("M1.4 Player Manager");
            Invoke(manager, "StartNewRun");
            GameSession session = GetProperty<GameSession>(manager, "Session");
            session.ConsumeFood(27);
            session.TakeDamage(18);

            Component firstPlayer = CreatePlayer("M1.4 First Player", out Text firstFood, out Text firstHealth);
            Assert.That(firstFood.text, Is.EqualTo("Food: 73"));
            Assert.That(firstHealth.text, Is.EqualTo("Health: 82"));

            ((Behaviour)firstPlayer).enabled = false;
            Assert.That(session.ActiveRun.Food, Is.EqualTo(73));
            Assert.That(session.ActiveRun.Health, Is.EqualTo(82));

            session.ConsumeFood(3);
            session.TakeDamage(2);
            ((Behaviour)firstPlayer).enabled = true;

            Assert.That(firstFood.text, Is.EqualTo("Food: 70"));
            Assert.That(firstHealth.text, Is.EqualTo("Health: 80"));
            Assert.That(playerType.GetField("food", InstanceFlags), Is.Null);
            Assert.That(playerType.GetField("health", InstanceFlags), Is.Null);

            UnityEngine.Object.DestroyImmediate(firstPlayer.gameObject);
            Component replacement = CreatePlayer("M1.4 Replacement Player", out Text replacementFood, out Text replacementHealth);
            yield return null;

            Assert.That(GetProperty<GameSession>(manager, "Session"), Is.SameAs(session));
            Assert.That(session.ActiveRun.Food, Is.EqualTo(70));
            Assert.That(session.ActiveRun.Health, Is.EqualTo(80));
            Assert.That(replacementFood.text, Is.EqualTo("Food: 70"));
            Assert.That(replacementHealth.text, Is.EqualTo("Health: 80"));

            UnityEngine.Object.DestroyImmediate(replacement.gameObject);
            UnityEngine.Object.DestroyImmediate(firstFood.gameObject);
            UnityEngine.Object.DestroyImmediate(firstHealth.gameObject);
            UnityEngine.Object.DestroyImmediate(replacementFood.gameObject);
            UnityEngine.Object.DestroyImmediate(replacementHealth.gameObject);
        }

        private Component CreateManager(string name)
        {
            GameObject managerObject = new GameObject(name);
            return managerObject.AddComponent(gameManagerType);
        }

        private Component CreatePlayer(string name, out Text foodText, out Text healthText)
        {
            GameObject foodObject = new GameObject(name + " Food Text");
            foodText = foodObject.AddComponent<Text>();
            GameObject healthObject = new GameObject(name + " Health Text");
            healthText = healthObject.AddComponent<Text>();
            GameObject playerObject = new GameObject(name);
            playerObject.AddComponent<BoxCollider2D>();
            playerObject.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            Component player = playerObject.AddComponent(playerType);
            SetField(player, "foodText", foodText);
            SetField(player, "healthText", healthText);
            Invoke(player, "Start");
            return player;
        }

        private static Component FindSceneComponent(Type type, string sceneName)
        {
            return UnityEngine.Object.FindObjectsOfType(type)
                .Cast<Component>()
                .FirstOrDefault(component => component.gameObject.scene.name == sceneName);
        }

        private static void AssertSceneHasNoMissingComponents(string sceneName)
        {
            Scene scene = SceneManager.GetSceneByName(sceneName);
            Assert.That(scene.IsValid() && scene.isLoaded, Is.True, sceneName + " must be loaded.");

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                {
                    Assert.That(
                        child.GetComponents<Component>().All(component => component != null),
                        Is.True,
                        child.name + " in " + sceneName + " contains a missing script.");
                }
            }
        }

        private static T GetField<T>(Component target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(fieldName, InstanceFlags);
            Assert.That(field, Is.Not.Null, fieldName + " must exist.");
            return (T)field.GetValue(target);
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

        private static void SetField(Component target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, InstanceFlags);
            Assert.That(field, Is.Not.Null, fieldName + " must exist.");
            field.SetValue(target, value);
        }

        private static T GetProperty<T>(Component target, string propertyName)
        {
            PropertyInfo property = target.GetType().GetProperty(propertyName, InstanceFlags);
            Assert.That(property, Is.Not.Null, propertyName + " must exist.");
            return (T)property.GetValue(target, null);
        }

        private static object GetStaticField(Type type, string fieldName)
        {
            FieldInfo field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
            Assert.That(field, Is.Not.Null, fieldName + " must exist.");
            return field.GetValue(null);
        }

        private static void SetStaticField(Type type, string fieldName, object value)
        {
            FieldInfo field = type.GetField(
                fieldName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(field, Is.Not.Null, fieldName + " must exist.");
            field.SetValue(null, value);
        }

        private static object InvokeStatic(Type type, string methodName)
        {
            MethodInfo method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
            Assert.That(method, Is.Not.Null, methodName + " must exist.");
            return method.Invoke(null, null);
        }

        private static void DestroySingleton(Type type)
        {
            if (type == null)
                return;

            FieldInfo instanceField = type.GetField("instance", BindingFlags.Public | BindingFlags.Static);
            Component instance = instanceField == null ? null : instanceField.GetValue(null) as Component;
            if (instance != null)
                UnityEngine.Object.DestroyImmediate(instance.gameObject);
            if (instanceField != null)
                instanceField.SetValue(null, null);
        }
        private void DestroyGameManagerSingleton()
        {
            if (gameManagerType == null)
                return;

            Component manager = GetStaticField(gameManagerType, "instance") as Component;
            if (manager != null)
                UnityEngine.Object.DestroyImmediate(manager.gameObject);
            gameManagerType.GetField("instance", BindingFlags.Public | BindingFlags.Static).SetValue(null, null);
        }
    }
}