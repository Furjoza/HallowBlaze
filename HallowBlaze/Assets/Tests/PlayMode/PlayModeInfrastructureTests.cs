using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HallowBlaze.Core.Session;
using HallowBlaze.Core.State;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HallowBlaze.Tests.PlayMode
{
    public sealed class PlayModeInfrastructureTests
    {
        private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private readonly List<UnityEngine.Object> createdObjects = new List<UnityEngine.Object>();
        private Type gameManagerType;
        private Type playerType;
        private Type soundManagerType;
        private Type wallType;
        private Component gameManager;
        private GameSession session;

        [SetUp]
        public void SetUp()
        {
            Time.timeScale = 1f;
            Assembly gameAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .First(assembly => assembly.GetName().Name == "Assembly-CSharp");
            gameManagerType = RequireType(gameAssembly, "GameManager");
            playerType = RequireType(gameAssembly, "PlayerScript");
            soundManagerType = RequireType(gameAssembly, "SoundManager");
            wallType = RequireType(gameAssembly, "Wall");
            DestroySingleton(gameManagerType);
            DestroySingleton(soundManagerType);

            GameObject managerObject = Track(new GameObject("M1.4 Movement GameManager"));
            gameManager = managerObject.AddComponent(gameManagerType);
            Invoke(gameManager, "StartNewRun");
            session = GetProperty<GameSession>(gameManager, "Session");
            session.ConsumeFood(90);
        }

        [TearDown]
        public void TearDown()
        {
            for (int index = createdObjects.Count - 1; index >= 0; index--)
            {
                if (createdObjects[index] != null)
                    UnityEngine.Object.DestroyImmediate(createdObjects[index]);
            }

            createdObjects.Clear();
            DestroySingleton(gameManagerType);
            DestroySingleton(soundManagerType);
            Time.timeScale = 1f;
        }

        [UnityTest]
        public IEnumerator PlayModeAssemblyLoads()
        {
            yield return null;
            Assert.That(
                typeof(PlayModeInfrastructureTests).GetTypeInfo().Assembly.GetName().Name,
                Is.EqualTo("HallowBlaze.Tests.PlayMode"));
        }

        [UnityTest]
        public IEnumerator PlayerMoveResolvesOnce()
        {
            GameObject playerObject = CreatePlayer("M1.4 Player", 0.01f);
            GameObject wallObject = Track(new GameObject("M1.4 One-hit Wall"));
            GameObject soundObject = Track(new GameObject("M1.4 Wall SoundManager"));
            AudioClip wallSound = Track(AudioClip.Create("M1.4 Wall Sound", 1, 1, 44100, false));

            wallObject.layer = 8;
            wallObject.transform.position = Vector3.right;
            wallObject.AddComponent<BoxCollider2D>();
            wallObject.AddComponent<SpriteRenderer>();
            Component wall = wallObject.AddComponent(wallType);
            SetField(wall, "hp", 1);
            SetField(wall, "chopSound1", wallSound);
            SetField(wall, "chopSound2", wallSound);
            CreateSoundManager(soundObject);
            Physics2D.SyncTransforms();

            InvokeGenericAttemptMove(playerObject.GetComponent(playerType), 1, 0);

            yield return new WaitForFixedUpdate();
            yield return null;

            Assert.That(wallObject.activeSelf, Is.False, "The accepted wall interaction should resolve once.");
            Assert.That(session.ActiveRun.Food, Is.EqualTo(9), "The accepted action should spend one food.");
            Assert.That(playerObject.transform.position.x, Is.EqualTo(0f).Within(0.01f),
                "One input must not enter a tile freed by the same wall interaction.");
            Assert.That(playerType.GetField("food", InstanceFlags), Is.Null);
        }

        [UnityTest]
        public IEnumerator RejectedPlayerMoveDoesNotSpendFood()
        {
            GameObject playerObject = CreatePlayer("M1.4 Blocked Player", 0.01f);
            GameObject obstacleObject = Track(new GameObject("M1.4 Obstacle"));
            obstacleObject.layer = 8;
            obstacleObject.transform.position = Vector3.right;
            obstacleObject.AddComponent<BoxCollider2D>();
            Physics2D.SyncTransforms();

            InvokeGenericAttemptMove(playerObject.GetComponent(playerType), 1, 0);
            yield return null;

            Assert.That(session.ActiveRun.Food, Is.EqualTo(10));
            Assert.That(playerObject.transform.position.x, Is.EqualTo(0f).Within(0.01f));
        }

        [UnityTest]
        public IEnumerator RejectedGatheringDoesNotSpendFoodOrTurn()
        {
            GameObject playerObject = CreatePlayer("M1.4 Rejected Gathering Player", 0.01f);
            Component player = playerObject.GetComponent(playerType);
            yield return null;

            Assert.That(GetProperty<bool>(gameManager, "IsGameplayInputEnabled"), Is.True);

            Invoke(player, "AttemptGathering");

            Assert.That(session.ActiveRun.Food, Is.EqualTo(10));
            Assert.That(session.ActiveRun.Status, Is.EqualTo(RunStatus.Active));
            Assert.That(GetProperty<bool>(gameManager, "IsGameplayInputEnabled"), Is.True);
        }

        [UnityTest]
        public IEnumerator GatheringAtLastFoodRewardsBeforeCostAndEndsTurn()
        {
            session.ConsumeFood(9);
            GameObject playerObject = CreatePlayer("M1.4 Gathering Player", 0.01f);
            Component player = playerObject.GetComponent(playerType);
            GameObject carrotObject = Track(new GameObject("M1.4 Gathering Carrot"));
            SetField(player, "onCarrot", true);
            SetField(player, "tmpCarrot", carrotObject);
            yield return null;

            Assert.That(GetProperty<bool>(gameManager, "IsGameplayInputEnabled"), Is.True);

            Invoke(player, "AttemptGathering");

            Assert.That(session.ActiveRun.Food, Is.EqualTo(10));
            Assert.That(session.ActiveRun.Status, Is.EqualTo(RunStatus.Active));
            Assert.That(carrotObject.activeSelf, Is.False);
            Assert.That(GetField<bool>(player, "onCarrot"), Is.False);
            Assert.That(GetField<GameObject>(player, "tmpCarrot"), Is.Null);
            Assert.That(GetProperty<bool>(gameManager, "IsGameplayInputEnabled"), Is.False);
        }

        [UnityTest]
        public IEnumerator FreePlayerMoveSpendsFoodAndPlaysSound()
        {
            GameObject playerObject = CreatePlayer("M1.4 Free Player", 0.01f);
            GameObject soundObject = Track(new GameObject("M1.4 Movement SoundManager"));
            AudioClip moveSound = Track(AudioClip.Create("M1.4 Move Sound", 4410, 1, 44100, false));
            Component player = playerObject.GetComponent(playerType);
            SetField(player, "moveSound1", moveSound);
            SetField(player, "moveSound2", moveSound);
            AudioSource audioSource = CreateSoundManager(soundObject);

            InvokeGenericAttemptMove(player, 1, 0);

            float movementDeadline = Time.realtimeSinceStartup + 2f;
            while (Mathf.Abs(playerObject.transform.position.x - 1f) > 0.01f
                && Time.realtimeSinceStartup < movementDeadline)
                yield return null;

            Assert.That(session.ActiveRun.Food, Is.EqualTo(9));
            Assert.That(playerObject.transform.position.x, Is.EqualTo(1f).Within(0.01f));
            Assert.That(audioSource.clip, Is.SameAs(moveSound));
        }

        private GameObject CreatePlayer(string name, float moveTime)
        {
            GameObject playerObject = Track(new GameObject(name));
            playerObject.AddComponent<BoxCollider2D>();
            playerObject.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            Component player = playerObject.AddComponent(playerType);
            SetField(player, "moveTime", moveTime);
            SetField(player, "blockingLayer", (LayerMask)(1 << 8));
            Invoke(player, "Start");
            return playerObject;
        }

        private AudioSource CreateSoundManager(GameObject soundObject)
        {
            AudioSource audioSource = soundObject.AddComponent<AudioSource>();
            Component soundManager = soundObject.AddComponent(soundManagerType);
            SetField(soundManager, "efxSource", audioSource);
            ((Behaviour)soundManager).enabled = false;
            return audioSource;
        }

        private void InvokeGenericAttemptMove(Component player, int xDirection, int yDirection)
        {
            MethodInfo method = playerType.GetMethod("AttemptMove", InstanceFlags);
            Assert.That(method, Is.Not.Null);
            method.MakeGenericMethod(wallType).Invoke(player, new object[] { xDirection, yDirection });
        }

        private GameObject Track(GameObject gameObject)
        {
            createdObjects.Add(gameObject);
            return gameObject;
        }

        private T Track<T>(T value) where T : UnityEngine.Object
        {
            createdObjects.Add(value);
            return value;
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
    }
}