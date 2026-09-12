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
        private Type enemyType;
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
            enemyType = RequireType(gameAssembly, "Enemy");
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

        [UnityTest]
        public IEnumerator PlayerMoveCompletesWithinConfiguredDuration()
        {
            const float configuredMoveTime = 0.2f;
            GameObject playerObject = CreatePlayer("M1.9 Timed Move Player", configuredMoveTime);
            Component player = playerObject.GetComponent(playerType);
            float movementStartedAt = Time.realtimeSinceStartup;

            InvokeGenericAttemptMove(player, 1, 0);

            Assert.That(session.ActiveRun.Food, Is.EqualTo(9));

            float movementDeadline = movementStartedAt + configuredMoveTime + 0.4f;
            while ((playerObject.transform.position - Vector3.right).sqrMagnitude > float.Epsilon
                && Time.realtimeSinceStartup < movementDeadline)
                yield return null;

            Assert.That(playerObject.transform.position.x, Is.EqualTo(1f).Within(0.001f));
            Assert.That(playerObject.transform.position.y, Is.EqualTo(0f).Within(0.001f));
            Assert.That(Time.realtimeSinceStartup, Is.LessThanOrEqualTo(movementDeadline));
        }

        [UnityTest]
        public IEnumerator PlayerMovementSnapsAcrossAllFourDirections()
        {
            GameObject playerObject = CreatePlayer("M1.9 Four Direction Player", 0.01f);
            Component player = playerObject.GetComponent(playerType);
            Vector2 expectedPosition = Vector2.zero;
            Vector2Int[] directions =
            {
                Vector2Int.right,
                Vector2Int.up,
                Vector2Int.left,
                Vector2Int.down
            };

            foreach (Vector2Int direction in directions)
            {
                SetField(gameManager, "playerTurn", true);
                InvokeGenericAttemptMove(player, direction.x, direction.y);
                expectedPosition += new Vector2(direction.x, direction.y);

                float movementDeadline = Time.realtimeSinceStartup + 2f;
                while (((Vector2)playerObject.transform.position - expectedPosition).sqrMagnitude > float.Epsilon
                    && Time.realtimeSinceStartup < movementDeadline)
                    yield return null;

                Assert.That(playerObject.transform.position.x, Is.EqualTo(expectedPosition.x).Within(0.001f));
                Assert.That(playerObject.transform.position.y, Is.EqualTo(expectedPosition.y).Within(0.001f));
            }

            Assert.That(session.ActiveRun.Food, Is.EqualTo(6));
        }

        [Test]
        public void EnemyContactDamagesPlayerFromBothAxes()
        {
            GameObject playerObject = CreatePlayer("M1.9 Enemy Target", 0.01f);
            playerObject.layer = 8;
            playerObject.tag = "Player";
            GameObject soundObject = Track(new GameObject("M1.9 Enemy SoundManager"));
            AudioClip attackSound = Track(AudioClip.Create("M1.9 Enemy Attack", 1, 1, 44100, false));
            CreateSoundManager(soundObject);
            Component horizontalEnemy = CreateEnemy("M1.9 Horizontal Enemy", Vector2.right, attackSound);
            Component verticalEnemy = CreateEnemy("M1.9 Vertical Enemy", Vector2.up, attackSound);
            Physics2D.SyncTransforms();
            int startingHealth = session.ActiveRun.Health;

            Invoke(horizontalEnemy, "MoveEnemy");
            Assert.That(session.ActiveRun.Health, Is.EqualTo(startingHealth - 10));

            Invoke(verticalEnemy, "MoveEnemy");
            Assert.That(session.ActiveRun.Health, Is.EqualTo(startingHealth - 20));
        }

        [UnityTest]
        public IEnumerator EnemyCannotEnterPlayersDestination()
        {
            GameObject playerObject = CreatePlayer("M1.9 Moving Enemy Target", 0.1f);
            playerObject.layer = 8;
            playerObject.tag = "Player";
            Component player = playerObject.GetComponent(playerType);
            GameObject soundObject = Track(new GameObject("M1.9 Enemy Collision SoundManager"));
            AudioClip actionSound = Track(AudioClip.Create("M1.9 Enemy Collision", 1, 1, 44100, false));
            CreateSoundManager(soundObject);
            SetField(player, "moveSound1", actionSound);
            SetField(player, "moveSound2", actionSound);
            Component enemy = CreateEnemy("M1.9 Blocking Enemy", new Vector2(2f, 0f), actionSound);

            yield return null;

            IList registeredEnemies = GetField<IList>(gameManager, "enemies");
            registeredEnemies.Clear();
            Invoke(gameManager, "AddEnemytoList", enemy);
            SetField(enemy, "skipMove", false);
            SetField(gameManager, "playerTurn", true);
            Physics2D.SyncTransforms();
            int startingHealth = session.ActiveRun.Health;

            InvokeGenericAttemptMove(player, 1, 0);

            float turnDeadline = Time.realtimeSinceStartup + 1f;
            while (!GetProperty<bool>(gameManager, "IsGameplayInputEnabled")
                && Time.realtimeSinceStartup < turnDeadline)
                yield return null;

            Assert.That(GetProperty<bool>(gameManager, "IsGameplayInputEnabled"), Is.True);
            Assert.That(playerObject.transform.position.x, Is.EqualTo(1f).Within(0.001f));
            Assert.That(enemy.transform.position.x, Is.EqualTo(2f).Within(0.001f));
            Assert.That(session.ActiveRun.Health, Is.EqualTo(startingHealth - 10));
            Assert.That(playerObject.transform.position, Is.Not.EqualTo(enemy.transform.position));

            InvokeGenericAttemptMove(player, 0, 1);

            turnDeadline = Time.realtimeSinceStartup + 1f;
            while (!GetProperty<bool>(gameManager, "IsGameplayInputEnabled")
                && Time.realtimeSinceStartup < turnDeadline)
                yield return null;

            Assert.That(GetProperty<bool>(gameManager, "IsGameplayInputEnabled"), Is.True);
            Assert.That(playerObject.transform.position, Is.EqualTo(new Vector3(1f, 1f, 0f)));
            Assert.That(enemy.transform.position, Is.EqualTo(new Vector3(2f, 0f, 0f)));
            Assert.That(session.ActiveRun.Health, Is.EqualTo(startingHealth - 10));
        }

        [UnityTest]
        public IEnumerator EnteringExitSnapsOnceAndStopsPlayerInput()
        {
            GameObject playerObject = CreatePlayer("M1.9 Exit Player", 0.01f);
            Component player = playerObject.GetComponent(playerType);
            SetField(player, "restartLevelDelay", 30f);
            GameObject exitObject = Track(new GameObject("M1.9 Exit"));
            exitObject.tag = "Exit";
            exitObject.transform.position = Vector2.right;
            exitObject.AddComponent<BoxCollider2D>().isTrigger = true;
            Physics2D.SyncTransforms();

            InvokeGenericAttemptMove(player, 1, 0);

            float movementDeadline = Time.realtimeSinceStartup + 2f;
            while ((((Vector2)playerObject.transform.position - Vector2.right).sqrMagnitude > float.Epsilon
                    || ((Behaviour)player).enabled)
                && Time.realtimeSinceStartup < movementDeadline)
                yield return null;

            Assert.That(session.ActiveRun.Food, Is.EqualTo(9));
            Assert.That(playerObject.transform.position.x, Is.EqualTo(1f).Within(0.001f));
            Assert.That(playerObject.transform.position.y, Is.EqualTo(0f).Within(0.001f));
            Assert.That(((Behaviour)player).enabled, Is.False);
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

        private Component CreateEnemy(string name, Vector2 position, AudioClip attackSound)
        {
            GameObject enemyObject = Track(new GameObject(name));
            enemyObject.layer = 8;
            enemyObject.transform.position = position;
            enemyObject.AddComponent<BoxCollider2D>();
            enemyObject.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            enemyObject.AddComponent<Animator>();
            Component enemy = enemyObject.AddComponent(enemyType);
            SetField(enemy, "moveTime", 0.01f);
            SetField(enemy, "blockingLayer", (LayerMask)(1 << 8));
            SetField(enemy, "playerDamage", 10);
            SetField(enemy, "enemyAttack1", attackSound);
            SetField(enemy, "enemyAttack2", attackSound);
            SetField(enemy, "enemyAttack3", attackSound);
            Invoke(enemy, "Start");
            return enemy;
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