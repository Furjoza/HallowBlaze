using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Session;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Contracts;
using HallowBlaze.Presentation.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

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
        private string persistenceRoot;
        private bool hadHighScore;
        private int originalHighScore;

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
            hadHighScore = PlayerPrefs.HasKey("HighScore");
            originalHighScore = PlayerPrefs.GetInt("HighScore");
            persistenceRoot = Path.Combine(Path.GetTempPath(), "HB-M363-" + Guid.NewGuid().ToString("N"));
            gameManagerType.GetField("PersistenceRootOverride", BindingFlags.Static | BindingFlags.NonPublic)
                .SetValue(null, persistenceRoot);

            GameObject managerObject = Track(new GameObject("M1.4 Movement GameManager"));
            gameManager = managerObject.AddComponent(gameManagerType);
            Invoke(gameManager, "StartNewRun");
            session = GetProperty<GameSession>(gameManager, "Session");
            session.ConsumeFood(90);
            SetField(gameManager, "worldDefinitionJson", Track(new TextAsset(File.ReadAllText(
                Path.Combine(Application.dataPath, "GameData/World/prototype-world.json")))));
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
            gameManagerType.GetField("PersistenceRootOverride", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
            if (Directory.Exists(persistenceRoot))
                Directory.Delete(persistenceRoot, true);
            if (hadHighScore)
                PlayerPrefs.SetInt("HighScore", originalHighScore);
            else
                PlayerPrefs.DeleteKey("HighScore");
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
            GameObject playerObject = CreatePlayer("M3.6.3 Player", 0.1f);
            Component player = playerObject.GetComponent(playerType);
            AudioClip moveSound = Track(AudioClip.Create("Move", 4410, 1, 44100, false));
            SetField(player, "moveSound1", moveSound);
            SetField(player, "moveSound2", moveSound);
            AudioSource audio = CreateSoundManager(Track(new GameObject("Movement audio")));
            BoardRuntime runtime = ComposeRuntime(playerObject);
            yield return null;
            var source = new PcCommandSource(key => key == KeyCode.RightArrow);
            object[] input = { source, null };
            Assert.That((bool)Invoke(player, "TrySubmitInput", input), Is.True);
            var pending = (Task<CommandSubmission>)input[1];
            Assert.That(pending.IsCompleted, Is.False);
            for (int attempt = 0; attempt < 20; attempt++)
                Assert.That(TrySubmit(player, new WaitCommand(), out _), Is.False);
            while (!pending.IsCompleted)
                yield return null;
            Assert.That(pending.Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
            Assert.That(session.ActiveRun.Food, Is.EqualTo(9));
            Assert.That(playerObject.transform.position, Is.EqualTo(Vector3.right));
            Assert.That(GetProperty<BoardEventPresenter>(gameManager, "ActiveBoardPresenter").Coordinator.ResolutionCount, Is.EqualTo(1));
            Assert.That(GetProperty<int>(player, "PresentedSoundCount"), Is.EqualTo(1));
            Assert.That(audio.clip, Is.SameAs(moveSound));
            Assert.That(runtime.Controller.IsTerminal, Is.False);
        }

        [UnityTest]
        public IEnumerator RejectedPlayerMoveDoesNotSpendFood()
        {
            GameObject playerObject = CreatePlayer("Blocked Player", 0.01f);
            Component player = playerObject.GetComponent(playerType);
            GameObject obstacleObject = Track(new GameObject("Wall"));
            obstacleObject.layer = 8;
            obstacleObject.transform.position = Vector3.right;
            obstacleObject.AddComponent<BoxCollider2D>();
            obstacleObject.AddComponent<SpriteRenderer>();
            Component wall = obstacleObject.AddComponent(wallType);
            SetField(wall, "hp", 3);
            BoardRuntime runtime = ComposeRuntime(playerObject,
                new LegacyBoardView(obstacleObject, LegacyBoardContentKind.Wall, new GridPosition(1, 0)));
            Physics2D.SyncTransforms();
            yield return null;
            Assert.That(TrySubmit(player, new MoveCommand(Direction.East), out Task<CommandSubmission> blocked), Is.True);
            Assert.That(blocked.Result.Status, Is.EqualTo(CommandSubmissionStatus.Rejected));
            Assert.That(TrySubmit(player, new MoveCommand(Direction.West), out Task<CommandSubmission> outside), Is.True);
            Assert.That(outside.Result.Status, Is.EqualTo(CommandSubmissionStatus.Rejected));
            int resolutions = GetProperty<BoardEventPresenter>(gameManager, "ActiveBoardPresenter").Coordinator.ResolutionCount;
            Assert.Throws<ArgumentException>(() => new MoveCommand(default(Direction)));
            Assert.That(GetProperty<BoardEventPresenter>(gameManager, "ActiveBoardPresenter").Coordinator.ResolutionCount,
                Is.EqualTo(resolutions));
            Assert.That(session.ActiveRun.Food, Is.EqualTo(10));
            Assert.That(session.ActiveRun.Health, Is.EqualTo(100));
            Assert.That(playerObject.transform.position, Is.EqualTo(Vector3.zero));
            Assert.That(GetField<int>(wall, "hp"), Is.EqualTo(3));
            Assert.That(obstacleObject.activeSelf, Is.True);
            Assert.That(GetProperty<int>(player, "PresentedSoundCount"), Is.Zero);
            Assert.That(runtime.Controller.IsTerminal, Is.False);
        }

        [UnityTest]
        public IEnumerator WaitCostsExactlyOnceWithoutMovement()
        {
            GameObject playerObject = CreatePlayer("Wait Player", 0.01f);
            Component player = playerObject.GetComponent(playerType);
            ComposeRuntime(playerObject);
            yield return null;
            var source = new PcCommandSource(key => key == KeyCode.Space);
            object[] arguments = { source, null };
            Assert.That((bool)Invoke(player, "TrySubmitInput", arguments), Is.True);
            var submission = (Task<CommandSubmission>)arguments[1];
            Assert.That(submission.Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
            Assert.That(session.ActiveRun.Food, Is.EqualTo(9));
            Assert.That(playerObject.transform.position, Is.EqualTo(Vector3.zero));
            Assert.That(GetProperty<BoardEventPresenter>(gameManager, "ActiveBoardPresenter").Coordinator.ResolutionCount, Is.EqualTo(1));
            Assert.That(GetProperty<bool>(gameManager, "IsGameplayInputEnabled"), Is.True);
        }

        [UnityTest]
        public IEnumerator SupportedFoodRewardsBeforeCostAndSynchronizesHud()
        {
            session.ConsumeFood(9);
            GameObject playerObject = CreatePlayer("Food Player", 0.01f);
            Component player = playerObject.GetComponent(playerType);
            Text foodText = Track(new GameObject("Food HUD")).AddComponent<Text>();
            SetField(player, "foodText", foodText);
            GameObject food = Track(new GameObject("Food"));
            food.tag = "Food";
            food.transform.position = Vector3.right;
            food.AddComponent<BoxCollider2D>().isTrigger = true;
            BoardRuntime runtime = ComposeRuntime(playerObject,
                new LegacyBoardView(food, LegacyBoardContentKind.Food, new GridPosition(1, 0)));
            yield return null;
            Assert.That(TrySubmit(player, new MoveCommand(Direction.East), out Task<CommandSubmission> pending), Is.True);
            Assert.That(session.ActiveRun.Food, Is.EqualTo(10));
            Assert.That(food.activeSelf, Is.True);
            Assert.That(foodText.text, Is.EqualTo("Food: 1"));
            while (!pending.IsCompleted)
                yield return null;
            Assert.That(pending.Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
            Assert.That(session.ActiveRun.Food, Is.EqualTo(10));
            Assert.That(session.ActiveRun.Status, Is.EqualTo(RunStatus.Active));
            Assert.That(food.activeSelf, Is.False);
            Assert.That(foodText.text, Is.EqualTo("Food: 10"));
            Assert.That(runtime.BoardState.Count, Is.EqualTo(runtime.Views.Count - 1));
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

            ComposeRuntime(playerObject);
            yield return null;
            Assert.That(TrySubmit(player, new MoveCommand(Direction.East), out Task<CommandSubmission> pending), Is.True);

            float movementDeadline = Time.realtimeSinceStartup + 2f;
            while (!pending.IsCompleted
                && Time.realtimeSinceStartup < movementDeadline)
                yield return null;

            Assert.That(pending.IsCompleted, Is.True);
            Assert.That(session.ActiveRun.Food, Is.EqualTo(9));
            Assert.That(playerObject.transform.position.x, Is.EqualTo(1f).Within(0.01f));
            Assert.That(audioSource.clip, Is.SameAs(moveSound));
            Assert.That(pending.Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
        }

        [UnityTest]
        public IEnumerator PlayerMoveCompletesWithinConfiguredDuration()
        {
            const float configuredMoveTime = 0.2f;
            GameObject playerObject = CreatePlayer("M1.9 Timed Move Player", configuredMoveTime);
            Component player = playerObject.GetComponent(playerType);
            ComposeRuntime(playerObject);
            yield return null;
            float movementStartedAt = Time.realtimeSinceStartup;
            Assert.That(TrySubmit(player, new MoveCommand(Direction.East), out Task<CommandSubmission> pending), Is.True);

            Assert.That(session.ActiveRun.Food, Is.EqualTo(9));

            float movementDeadline = movementStartedAt + configuredMoveTime + 0.4f;
            while ((playerObject.transform.position - Vector3.right).sqrMagnitude > float.Epsilon
                && Time.realtimeSinceStartup < movementDeadline)
                yield return null;

            Assert.That(playerObject.transform.position.x, Is.EqualTo(1f).Within(0.001f));
            Assert.That(playerObject.transform.position.y, Is.EqualTo(0f).Within(0.001f));
            Assert.That(Time.realtimeSinceStartup, Is.LessThanOrEqualTo(movementDeadline));
            Assert.That(pending.Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
        }

        [UnityTest]
        public IEnumerator PlayerMovementSnapsAcrossAllFourDirections()
        {
            GameObject playerObject = CreatePlayer("M1.9 Four Direction Player", 0.01f);
            Component player = playerObject.GetComponent(playerType);
            Vector3 expectedPosition = Vector3.zero;
            ComposeRuntime(playerObject);
            yield return null;
            Direction[] directions =
            {
                Direction.East, Direction.North, Direction.West, Direction.South
            };

            foreach (Direction direction in directions)
            {
                Assert.That(TrySubmit(player, new MoveCommand(direction), out Task<CommandSubmission> pending), Is.True);
                while (!pending.IsCompleted)
                    yield return null;
                Assert.That(pending.Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
                GridPosition authoritative = GetProperty<BoardRuntime>(gameManager, "ActiveBoardRuntime").BoardState
                    .GetEntities().Single(entity => entity.Id.Equals(GetProperty<BoardRuntime>(gameManager, "ActiveBoardRuntime").PlayerId)).Position;
                expectedPosition = new Vector3(authoritative.X, authoritative.Y, 0);
                Assert.That(playerObject.transform.position, Is.EqualTo(expectedPosition));
            }

            Assert.That(session.ActiveRun.Food, Is.EqualTo(6));
        }

        [Test]
        public void RetiredMutationAndEnemySchedulingMethodsAreAbsent()
        {
            foreach (string method in new[] { "AttemptMove", "AttemptGathering", "OnCantMove", "LoseHealth", "OnTriggerEnter2D", "OnTriggerExit2D" })
                Assert.That(playerType.GetMethod(method, InstanceFlags), Is.Null, method);
            foreach (string method in new[] { "EndPlayerTurn", "MoveEnemies", "AddEnemytoList" })
                Assert.That(gameManagerType.GetMethod(method, InstanceFlags), Is.Null, method);
            Assert.That(gameManagerType.GetField("playerTurn", InstanceFlags), Is.Null);
            Assert.That(enemyType.GetMethod("MoveEnemy", InstanceFlags), Is.Null);
            Assert.That(playerType.BaseType.GetMethod("Move", InstanceFlags), Is.Null);
        }

        [UnityTest]
        public IEnumerator EnemyViewsRemainInertAndBlockTheirDomainCells()
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

            ComposeRuntime(playerObject,
                new LegacyBoardView(enemy.gameObject, LegacyBoardContentKind.Enemy, new GridPosition(2, 0)));
            Physics2D.SyncTransforms();
            yield return null;
            int startingHealth = session.ActiveRun.Health;
            Assert.That(TrySubmit(player, new MoveCommand(Direction.East), out Task<CommandSubmission> first), Is.True);
            while (!first.IsCompleted)
                yield return null;

            Assert.That(GetProperty<bool>(gameManager, "IsGameplayInputEnabled"), Is.True);
            Assert.That(playerObject.transform.position.x, Is.EqualTo(1f).Within(0.001f));
            Assert.That(enemy.transform.position.x, Is.EqualTo(2f).Within(0.001f));
            Assert.That(session.ActiveRun.Health, Is.EqualTo(startingHealth));
            Assert.That(playerObject.transform.position, Is.Not.EqualTo(enemy.transform.position));

            Assert.That(TrySubmit(player, new MoveCommand(Direction.East), out Task<CommandSubmission> blocked), Is.True);
            Assert.That(blocked.Result.Status, Is.EqualTo(CommandSubmissionStatus.Rejected));
            Assert.That(session.ActiveRun.Food, Is.EqualTo(9));
            Assert.That(TrySubmit(player, new MoveCommand(Direction.North), out Task<CommandSubmission> second), Is.True);
            while (!second.IsCompleted)
                yield return null;

            Assert.That(GetProperty<bool>(gameManager, "IsGameplayInputEnabled"), Is.True);
            Assert.That(playerObject.transform.position, Is.EqualTo(new Vector3(1f, 1f, 0f)));
            Assert.That(enemy.transform.position, Is.EqualTo(new Vector3(2f, 0f, 0f)));
            Assert.That(session.ActiveRun.Health, Is.EqualTo(startingHealth));
        }

        [UnityTest]
        public IEnumerator EnteringExitSnapsOnceCostsOnceAndGuardsOneOutcome()
        {
            GameObject playerObject = CreatePlayer("M1.9 Exit Player", 0.01f);
            Component player = playerObject.GetComponent(playerType);
            BoardRuntime runtime = ComposeRuntime(playerObject);
            int outcomeCalls = 0;
            gameManagerType.GetEvent("OnRouteChoicesChanged").AddEventHandler(gameManager,
                (Action<IReadOnlyList<WorldMapExitOption>>)(choices =>
                {
                    if (choices.Count == 0)
                        return;
                    outcomeCalls++;
                    Assert.That(playerObject.transform.position, Is.EqualTo(new Vector3(2, 1, 0)));
                    Assert.That(session.ActiveRun.Food, Is.EqualTo(7));
                }));
            yield return null;
            foreach (Direction direction in new[] { Direction.East, Direction.East, Direction.North })
            {
                Assert.That(TrySubmit(player, new MoveCommand(direction), out Task<CommandSubmission> pending), Is.True);
                while (!pending.IsCompleted)
                    yield return null;
                Assert.That(pending.Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
            }
            Assert.That(session.ActiveRun.Food, Is.EqualTo(7));
            Assert.That(GetProperty<bool>(gameManager, "IsRouteChoiceActive"), Is.True);
            Assert.That(outcomeCalls, Is.EqualTo(1));
            Assert.That(TrySubmit(player, new WaitCommand(), out _), Is.False);
            Assert.That(((IBoardOutcomeSink)gameManager).TryNotify(runtime.Request,
                new ExitReachedEvent(runtime.PlayerId, runtime.BoardState.GetEntities()
                    .Single(entity => entity.Definition.ContentId == "legacy.exit").Id)), Is.False);
            Assert.That(outcomeCalls, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator AnimationModesHaveSameSodaPickupHudAndAudio()
        {
            foreach (bool animated in new[] { true, false })
            {
                GameObject playerObject = CreatePlayer("Soda Player", 0.05f);
                Component player = playerObject.GetComponent(playerType);
                SetField(player, "animationsEnabled", animated);
                Text foodText = Track(new GameObject("Soda HUD")).AddComponent<Text>();
                SetField(player, "foodText", foodText);
                GameObject soda = Track(new GameObject("Soda"));
                soda.tag = "Soda";
                soda.transform.position = Vector3.right;
                AudioClip cue = Track(AudioClip.Create("Soda cue", 4410, 1, 44100, false));
                SetField(player, "drinkSound1", cue);
                SetField(player, "moveSound1", cue);
                DestroySingleton(soundManagerType);
                CreateSoundManager(Track(new GameObject("Soda audio")));
                Invoke(gameManager, "DisposeActiveBoardRuntime");
                ComposeRuntime(playerObject, new LegacyBoardView(soda, LegacyBoardContentKind.Soda, new GridPosition(1, 0)));
                yield return null;
                int initialFood = session.ActiveRun.Food;
                Assert.That(TrySubmit(player, new MoveCommand(Direction.East), out Task<CommandSubmission> pending), Is.True);
                while (!pending.IsCompleted)
                    yield return null;
                Assert.That(pending.Result.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
                Assert.That(session.ActiveRun.Food, Is.EqualTo(initialFood + 19));
                Assert.That(foodText.text, Is.EqualTo("Food: " + (initialFood + 19)));
                Assert.That(playerObject.transform.position, Is.EqualTo(Vector3.right));
                Assert.That(soda.activeSelf, Is.False);
                Assert.That(GetProperty<int>(player, "PresentedSoundCount"), Is.EqualTo(2));
            }
        }

        private GameObject CreatePlayer(string name, float moveTime)
        {
            GameObject playerObject = Track(new GameObject(name));
            playerObject.AddComponent<BoxCollider2D>();
            playerObject.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            Component player = playerObject.AddComponent(playerType);
            SetField(player, "moveTime", moveTime);
            SetField(player, "blockingLayer", (LayerMask)(1 << 8));
            return playerObject;
        }

        private BoardRuntime ComposeRuntime(GameObject playerObject, params LegacyBoardView[] additional)
        {
            RunState run = session.ActiveRun;
            var request = new BoardRequest(run.RunId, run.RunSeed, run.WorldNodeId, run.CurrentDay,
                run.GetBoardSeed(), "forest", "temperate", 1);
            var layout = new List<LegacyBoardView>();
            for (int horizontal = 0; horizontal < 3; horizontal++)
                for (int vertical = 0; vertical < 2; vertical++)
                {
                    GameObject floor = Track(new GameObject("Floor"));
                    floor.transform.position = new Vector3(horizontal, vertical, 0);
                    layout.Add(new LegacyBoardView(floor, LegacyBoardContentKind.Floor, new GridPosition(horizontal, vertical)));
                }
            layout.Add(new LegacyBoardView(playerObject, LegacyBoardContentKind.Player, new GridPosition(0, 0)));
            GameObject exit = Track(new GameObject("Exit"));
            exit.transform.position = new Vector3(2, 1, 0);
            layout.Add(new LegacyBoardView(exit, LegacyBoardContentKind.Exit, new GridPosition(2, 1)));
            layout.AddRange(additional);
            BoardRuntime runtime = LegacyBoardRuntimeComposer.Compose(request, run, new GridBounds(0, 0, 2, 1), layout);
            Invoke(gameManager, "PublishBoardRuntime", runtime);
            return runtime;
        }

        private static bool TrySubmit(Component player, PlayerCommand command, out Task<CommandSubmission> submission)
        {
            object[] arguments = { command, null };
            bool admitted = (bool)Invoke(player, "TrySubmitCommand", arguments);
            submission = (Task<CommandSubmission>)arguments[1];
            return admitted;
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