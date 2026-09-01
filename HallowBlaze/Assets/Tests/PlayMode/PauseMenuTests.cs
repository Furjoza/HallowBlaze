using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace HallowBlaze.Tests.PlayMode
{
    public sealed class PauseMenuTests
    {
        private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private readonly List<UnityEngine.Object> createdObjects = new List<UnityEngine.Object>();
        private Type gameManagerType;
        private Type pauseMenuType;
        private Type playerType;
        private Type runStateType;
        private Type settingsPanelType;
        private Type soundManagerType;
        private Component gameManager;
        private Component pauseMenu;
        private Component runState;
        private GameObject pauseRoot;
        private GameObject settingsRoot;
        private GameObject resumeButton;
        private GameObject settingsBackButton;
        private float originalTimeScale;
        private PreferenceSnapshot musicPreference;
        private PreferenceSnapshot soundPreference;
        private IntPreferenceSnapshot highScorePreference;

        [SetUp]
        public void SetUp()
        {
            originalTimeScale = Time.timeScale;
            musicPreference = CaptureStringPreference("Music");
            soundPreference = CaptureStringPreference("Sound");
            highScorePreference = CaptureIntPreference("HighScore");

            Assembly gameAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .First(assembly => assembly.GetName().Name == "Assembly-CSharp");
            gameManagerType = RequireType(gameAssembly, "GameManager");
            pauseMenuType = RequireType(gameAssembly, "PauseMenuController");
            playerType = RequireType(gameAssembly, "PlayerScript");
            runStateType = RequireType(gameAssembly, "RunState");
            settingsPanelType = RequireType(gameAssembly, "SettingsPanelController");
            soundManagerType = RequireType(gameAssembly, "SoundManager");

            DestroySingleton(gameManagerType);
            DestroySingleton(soundManagerType);
            CreateGameplayHost();
            CreatePauseHost();
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = originalTimeScale;

            for (int index = createdObjects.Count - 1; index >= 0; index--)
            {
                if (createdObjects[index] != null)
                    UnityEngine.Object.DestroyImmediate(createdObjects[index]);
            }

            createdObjects.Clear();
            DestroySingleton(gameManagerType);
            DestroySingleton(soundManagerType);
            RestoreStringPreference("Music", musicPreference);
            RestoreStringPreference("Sound", soundPreference);
            RestoreIntPreference("HighScore", highScorePreference);
        }

        [Test]
        public void EscapeTransitionsClosedPauseSettingsPauseClosed()
        {
            Invoke(pauseMenu, "HandleEscape");

            AssertState("PauseRoot");
            Assert.That(Time.timeScale, Is.EqualTo(0f));
            Assert.That(GetProperty<bool>(gameManager, "IsGameplayInputBlocked"), Is.True);
            Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(resumeButton));

            Invoke(pauseMenu, "OpenSettings");
            AssertState("Settings");
            Assert.That(pauseRoot.activeSelf, Is.False);
            Assert.That(settingsRoot.activeSelf, Is.True);
            Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(settingsBackButton));

            Invoke(pauseMenu, "HandleEscape");
            AssertState("PauseRoot");
            Assert.That(Time.timeScale, Is.EqualTo(0f));

            Invoke(pauseMenu, "HandleEscape");
            AssertState("Closed");
            Assert.That(Time.timeScale, Is.EqualTo(1f));
            Assert.That(GetProperty<bool>(gameManager, "IsGameplayInputBlocked"), Is.False);
        }

        [Test]
        public void PauseCanOpenDuringEnemyTurnAndEnemyGateWaits()
        {
            SetField(runState, "playerTurn", false);
            SetField(runState, "enemiesMoving", true);

            Assert.That(GetProperty<bool>(gameManager, "CanPauseGameplay"), Is.True);
            Invoke(pauseMenu, "OpenPause");
            AssertState("PauseRoot");

            IEnumerator gate = (IEnumerator)Invoke(gameManager, "WaitWhileGameplayBlocked");
            Assert.That(gate.MoveNext(), Is.True);

            Invoke(gameManager, "SetGameplayInputBlocked", false);
            Assert.That(gate.MoveNext(), Is.False);
        }

        [Test]
        public void PauseAndSettingsRejectPlayerCostsPickupsAndDamage()
        {
            SetField(runState, "playerFoodPoints", 73);
            SetField(runState, "playerHealthPoints", 61);
            SetField(runState, "playerTurn", true);

            GameObject playerObject = Track(new GameObject("M1.1 Player"));
            Component player = playerObject.AddComponent(playerType);
            GameObject pickupObject = Track(new GameObject("M1.1 Food"));
            pickupObject.tag = "Food";
            BoxCollider2D pickup = pickupObject.AddComponent<BoxCollider2D>();
            Vector3 originalPosition = playerObject.transform.position;

            Invoke(pauseMenu, "OpenPause");
            Invoke(player, "Update");
            Invoke(player, "OnTriggerEnter2D", pickup);
            Invoke(player, "LoseHealth", 17);
            Invoke(pauseMenu, "OpenSettings");
            Invoke(player, "Update");
            Invoke(player, "OnTriggerEnter2D", pickup);
            Invoke(player, "LoseHealth", 17);

            Assert.That(GetProperty<bool>(gameManager, "IsGameplayInputEnabled"), Is.False);
            Assert.That(GetField<int>(runState, "playerFoodPoints"), Is.EqualTo(73));
            Assert.That(GetField<int>(runState, "playerHealthPoints"), Is.EqualTo(61));
            Assert.That(GetField<bool>(runState, "playerTurn"), Is.True);
            Assert.That(playerObject.transform.position, Is.EqualTo(originalPosition));
            Assert.That(pickupObject.activeSelf, Is.True);

            Invoke(pauseMenu, "BackFromSettings");
            Invoke(pauseMenu, "Resume");
            Assert.That(GetProperty<bool>(gameManager, "IsGameplayInputBlocked"), Is.False);
            Assert.That(GetProperty<bool>(gameManager, "IsGameplayInputEnabled"), Is.False);
        }

        [Test]
        public void MovementTriggersRemainActiveOutsidePauseAfterPlayerTurnEnds()
        {
            SetField(runState, "playerFoodPoints", 73);
            SetField(runState, "playerTurn", false);

            GameObject playerObject = Track(new GameObject("M1.1 Moving Player"));
            Component player = playerObject.AddComponent(playerType);
            GameObject pickupObject = Track(new GameObject("M1.1 Moving Food"));
            pickupObject.tag = "Food";
            BoxCollider2D pickup = pickupObject.AddComponent<BoxCollider2D>();
            GameObject carrotObject = Track(new GameObject("M1.1 Moving Carrot"));
            carrotObject.tag = "Carrot";
            BoxCollider2D carrot = carrotObject.AddComponent<BoxCollider2D>();

            Invoke(player, "OnTriggerEnter2D", pickup);
            Invoke(player, "OnTriggerEnter2D", carrot);

            Assert.That(GetField<int>(runState, "playerFoodPoints"), Is.EqualTo(83));
            Assert.That(pickupObject.activeSelf, Is.False);
            Assert.That(GetField<bool>(player, "onCarrot"), Is.True);

            Invoke(player, "OnTriggerExit2D", carrot);

            Assert.That(GetField<bool>(player, "onCarrot"), Is.False);
        }

        [Test]
        public void PauseRejectsWrongSceneSetupAndGameOver()
        {
            SetField(pauseMenu, "gameplaySceneName", "NotTheActiveScene");
            Invoke(pauseMenu, "OpenPause");
            AssertState("Closed");

            SetField(pauseMenu, "gameplaySceneName", SceneManager.GetActiveScene().name);
            SetField(gameManager, "doingSetup", true);
            Invoke(pauseMenu, "OpenPause");
            AssertState("Closed");

            SetField(gameManager, "doingSetup", false);
            ((Behaviour)gameManager).enabled = false;
            Invoke(pauseMenu, "OpenPause");
            AssertState("Closed");
        }

        [Test]
        public void DisablingPauseControllerRestoresRuntimeState()
        {
            Invoke(pauseMenu, "OpenPause");
            Assert.That(Time.timeScale, Is.EqualTo(0f));

            ((Behaviour)pauseMenu).enabled = false;

            Assert.That(Time.timeScale, Is.EqualTo(1f));
            Assert.That(GetProperty<bool>(gameManager, "IsGameplayInputBlocked"), Is.False);
        }

        [UnityTest]
        public IEnumerator ResumeDefersGameplayInputUntilFollowingFrame()
        {
            Invoke(pauseMenu, "OpenPause");
            Invoke(pauseMenu, "Resume");

            Assert.That(GetProperty<bool>(gameManager, "IsGameplayInputBlocked"), Is.False);
            Assert.That(GetProperty<bool>(gameManager, "IsGameplayInputEnabled"), Is.False);

            yield return null;

            Assert.That(GetProperty<bool>(gameManager, "IsGameplayInputEnabled"), Is.True);
        }

        [Test]
        public void SettingsHostsShareLiveSoundManagerState()
        {
            PlayerPrefs.SetInt("HighScore", 31415);
            Component soundManager = CreateSoundManager(out AudioSource effectsSource, out AudioSource musicSource);
            Component firstHost = CreateSettingsHost("First Settings", out Text firstMusicOn, out _, out _, out Text firstSoundOff);

            Invoke(firstHost, "SetMusicOff");
            Invoke(firstHost, "SetSoundOff");

            Assert.That(PlayerPrefs.GetString("Music"), Is.EqualTo("Off"));
            Assert.That(PlayerPrefs.GetString("Sound"), Is.EqualTo("Off"));
            Assert.That(musicSource.isPlaying, Is.False);
            Assert.That(effectsSource.mute, Is.True);
            Assert.That(PlayerPrefs.GetInt("HighScore"), Is.EqualTo(31415));
            Assert.That(GetProperty<bool>(soundManager, "MusicEnabled"), Is.False);
            Assert.That(GetProperty<bool>(soundManager, "SoundEnabled"), Is.False);

            Component secondHost = CreateSettingsHost("Second Settings", out _, out Text secondMusicOff, out _, out Text secondSoundOff);
            Invoke(secondHost, "Refresh");

            Assert.That(secondMusicOff.color, Is.EqualTo((Color)new Color32(255, 255, 255, 255)));
            Assert.That(secondSoundOff.color, Is.EqualTo((Color)new Color32(255, 255, 255, 255)));

            Invoke(firstHost, "SetMusicOn");
            Invoke(firstHost, "SetSoundOn");
            Invoke(secondHost, "Refresh");

            Assert.That(PlayerPrefs.GetString("Music"), Is.EqualTo("On"));
            Assert.That(PlayerPrefs.GetString("Sound"), Is.EqualTo("On"));
            Assert.That(effectsSource.mute, Is.False);
            Assert.That(firstMusicOn.color, Is.EqualTo((Color)new Color32(255, 255, 255, 255)));
            Assert.That(firstSoundOff.color, Is.EqualTo((Color)new Color32(50, 50, 50, 255)));
        }

        [UnityTest]
        public IEnumerator AbandonLegacyRunPreservesPreferencesAndNextManagerStartsFresh()
        {
            PlayerPrefs.SetString("Music", "Off");
            PlayerPrefs.SetString("Sound", "Off");
            PlayerPrefs.SetInt("HighScore", 27182);
            SetField(runState, "playerFoodPoints", 7);
            SetField(runState, "playerHealthPoints", 9);
            SetField(runState, "level", 4);

            Invoke(gameManager, "AbandonLegacyRun");
            Assert.That(GetStaticField(gameManagerType, "instance"), Is.Null);
            yield return null;

            GameObject nextManagerObject = Track(new GameObject("M1.1 Fresh GameManager"));
            Component nextManager = nextManagerObject.AddComponent(gameManagerType);
            Component nextRunState = (Component)GetField(nextManager, "runState");
            Track(nextRunState.gameObject);

            Assert.That(GetField<int>(nextRunState, "playerFoodPoints"), Is.EqualTo(100));
            Assert.That(GetField<int>(nextRunState, "playerHealthPoints"), Is.EqualTo(100));
            Assert.That(GetField<int>(nextRunState, "level"), Is.EqualTo(0));
            Assert.That(PlayerPrefs.GetString("Music"), Is.EqualTo("Off"));
            Assert.That(PlayerPrefs.GetString("Sound"), Is.EqualTo("Off"));
            Assert.That(PlayerPrefs.GetInt("HighScore"), Is.EqualTo(27182));
        }

        [Test]
        public void ExitToMenuRestoresRuntimeAndRequestsSceneOnce()
        {
            int sceneLoadCount = 0;
            string requestedScene = null;
            SetField(pauseMenu, "menuSceneName", "M1.1 Menu");
            SetField(pauseMenu, "sceneLoader", (Action<string>)(sceneName =>
            {
                sceneLoadCount++;
                requestedScene = sceneName;
            }));

            Invoke(pauseMenu, "OpenPause");
            Invoke(pauseMenu, "ExitToMenu");
            Invoke(pauseMenu, "ExitToMenu");

            Assert.That(sceneLoadCount, Is.EqualTo(1));
            Assert.That(requestedScene, Is.EqualTo("M1.1 Menu"));
            Assert.That(Time.timeScale, Is.EqualTo(1f));
            Assert.That(GetStaticField(gameManagerType, "instance"), Is.Null);
        }

        private void CreateGameplayHost()
        {
            GameObject managerObject = Track(new GameObject("M1.1 GameManager"));
            gameManager = managerObject.AddComponent(gameManagerType);
            runState = (Component)GetField(gameManager, "runState");
            Track(runState.gameObject);
        }

        private void CreatePauseHost()
        {
            GameObject eventSystemObject = Track(new GameObject("M1.1 EventSystem"));
            eventSystemObject.AddComponent<EventSystem>();

            pauseRoot = Track(new GameObject("PauseRoot"));
            settingsRoot = Track(new GameObject("SettingsRoot"));
            resumeButton = Track(new GameObject("Resume"));
            settingsBackButton = Track(new GameObject("Back"));
            GameObject pauseObject = Track(new GameObject("M1.1 PauseMenu"));
            pauseMenu = pauseObject.AddComponent(pauseMenuType);

            SetField(pauseMenu, "pauseRoot", pauseRoot);
            SetField(pauseMenu, "settingsRoot", settingsRoot);
            SetField(pauseMenu, "resumeButton", resumeButton);
            SetField(pauseMenu, "settingsBackButton", settingsBackButton);
            SetField(pauseMenu, "gameplaySceneName", SceneManager.GetActiveScene().name);
            pauseRoot.SetActive(false);
            settingsRoot.SetActive(false);
        }

        private Component CreateSoundManager(out AudioSource effectsSource, out AudioSource musicSource)
        {
            GameObject soundObject = Track(new GameObject("M1.1 SoundManager"));
            effectsSource = soundObject.AddComponent<AudioSource>();
            musicSource = soundObject.AddComponent<AudioSource>();
            Component soundManager = soundObject.AddComponent(soundManagerType);
            SetField(soundManager, "efxSource", effectsSource);
            SetField(soundManager, "musicSource", musicSource);
            return soundManager;
        }

        private Component CreateSettingsHost(
            string name,
            out Text musicOn,
            out Text musicOff,
            out Text soundOn,
            out Text soundOff)
        {
            GameObject host = Track(new GameObject(name));
            musicOn = CreateText(name + " Music On");
            musicOff = CreateText(name + " Music Off");
            soundOn = CreateText(name + " Sound On");
            soundOff = CreateText(name + " Sound Off");
            Component controller = host.AddComponent(settingsPanelType);
            SetField(controller, "musicOnText", musicOn);
            SetField(controller, "musicOffText", musicOff);
            SetField(controller, "soundOnText", soundOn);
            SetField(controller, "soundOffText", soundOff);
            Invoke(controller, "Refresh");
            return controller;
        }

        private Text CreateText(string name)
        {
            GameObject textObject = Track(new GameObject(name));
            return textObject.AddComponent<Text>();
        }

        private void AssertState(string expected)
        {
            Assert.That(GetProperty<object>(pauseMenu, "CurrentState").ToString(), Is.EqualTo(expected));
        }

        private GameObject Track(GameObject gameObject)
        {
            createdObjects.Add(gameObject);
            return gameObject;
        }

        private void Track(UnityEngine.Object value)
        {
            if (value != null && !createdObjects.Contains(value))
                createdObjects.Add(value);
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

        private static object GetField(Component target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(fieldName, InstanceFlags);
            Assert.That(field, Is.Not.Null, fieldName + " must exist.");
            return field.GetValue(target);
        }

        private static T GetField<T>(Component target, string fieldName)
        {
            return (T)GetField(target, fieldName);
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

        private static void DestroySingleton(Type type)
        {
            if (type == null)
                return;

            FieldInfo instanceField = type.GetField("instance", BindingFlags.Public | BindingFlags.Static);
            if (instanceField == null)
                return;

            Component instance = instanceField.GetValue(null) as Component;
            if (instance != null)
                UnityEngine.Object.DestroyImmediate(instance.gameObject);
            instanceField.SetValue(null, null);
        }

        private static PreferenceSnapshot CaptureStringPreference(string key)
        {
            return new PreferenceSnapshot(PlayerPrefs.HasKey(key), PlayerPrefs.GetString(key));
        }

        private static IntPreferenceSnapshot CaptureIntPreference(string key)
        {
            return new IntPreferenceSnapshot(PlayerPrefs.HasKey(key), PlayerPrefs.GetInt(key));
        }

        private static void RestoreStringPreference(string key, PreferenceSnapshot snapshot)
        {
            if (snapshot.Exists)
                PlayerPrefs.SetString(key, snapshot.Value);
            else
                PlayerPrefs.DeleteKey(key);
        }

        private static void RestoreIntPreference(string key, IntPreferenceSnapshot snapshot)
        {
            if (snapshot.Exists)
                PlayerPrefs.SetInt(key, snapshot.Value);
            else
                PlayerPrefs.DeleteKey(key);
        }

        private struct PreferenceSnapshot
        {
            public PreferenceSnapshot(bool exists, string value)
            {
                Exists = exists;
                Value = value;
            }

            public bool Exists { get; }
            public string Value { get; }
        }

        private struct IntPreferenceSnapshot
        {
            public IntPreferenceSnapshot(bool exists, int value)
            {
                Exists = exists;
                Value = value;
            }

            public bool Exists { get; }
            public int Value { get; }
        }
    }
}