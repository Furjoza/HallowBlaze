using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Session;
using HallowBlaze.Core.State;
using HallowBlaze.Presentation.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using EntityId = HallowBlaze.Core.Board.Primitives.EntityId;

namespace HallowBlaze.Tests.PlayMode
{
    internal sealed class BoardRuntimeStartupTests
    {
        private readonly List<GameObject> createdObjects = new List<GameObject>();
        private Type boardManagerType;

        [SetUp]
        public void SetUp()
        {
            Assembly gameAssembly = AppDomain.CurrentDomain.GetAssemblies().Single(
                assembly => assembly.GetName().Name == "Assembly-CSharp");
            boardManagerType = gameAssembly.GetType("BoardManager", true);
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
        }

        [UnityTest]
        public IEnumerator SuccessfulSetupPublishesCompleteRuntimeWithModelViewParity()
        {
            Component manager = CreateBoardManager();
            GameObject player = CreateObject("Player", 0, 0);
            RunState run = CreateRun();
            BoardRequest request = CreateRequest(run);

            BoardRuntime runtime = InvokeSetup(manager, request, run, player);

            Assert.That(runtime, Is.SameAs(GetProperty<BoardRuntime>(manager, "ActiveRuntime")));
            Assert.That(GetProperty<BoardRequest>(manager, "ActiveRequest"), Is.SameAs(request));
            Assert.That(runtime.RunState, Is.SameAs(run));
            Assert.That(runtime.Controller, Is.Not.Null);
            Assert.That(runtime.BoardState.Count, Is.EqualTo(6));
            Assert.That(runtime.Views, Has.Count.EqualTo(runtime.BoardState.Count));
            foreach (var entity in runtime.BoardState.GetEntities())
            {
                Vector3 viewPosition = runtime.Views[entity.Id].transform.position;
                Assert.That(viewPosition.x, Is.EqualTo(entity.Position.X));
                Assert.That(viewPosition.y, Is.EqualTo(entity.Position.Y));
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator FailedSetupPublishesNeitherRequestNorPartialRuntime()
        {
            Component manager = CreateBoardManager();
            RunState run = CreateRun();

            TargetInvocationException invocation = Assert.Throws<TargetInvocationException>(() =>
                InvokeSetup(manager, CreateRequest(run), run, null));
            var diagnostic = invocation.InnerException as BoardRuntimeCompositionException;

            Assert.That(diagnostic, Is.Not.Null);
            Assert.That(diagnostic.Code, Is.EqualTo(BoardRuntimeDiagnosticCode.MissingPlayer));
            Assert.That(GetProperty<BoardRequest>(manager, "ActiveRequest"), Is.Null);
            Assert.That(GetProperty<BoardRuntime>(manager, "ActiveRuntime"), Is.Null);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ReloadDisposesOldRuntimeAndDoesNotLeakGeneratedViews()
        {
            Component manager = CreateBoardManager();
            GameObject player = CreateObject("Player", 0, 0);
            RunState run = CreateRun();
            BoardRequest request = CreateRequest(run);

            BoardRuntime first = InvokeSetup(manager, request, run, player);
            IReadOnlyDictionary<EntityId, GameObject> firstRegistry = first.Views;
            List<GameObject> firstGeneratedViews = firstRegistry.Values
                .Where(view => view != player)
                .ToList();
            object firstController = first.Controller;

            BoardRuntime second = InvokeSetup(manager, request, run, player);

            Assert.That(first.IsDisposed, Is.True);
            Assert.That(firstRegistry, Is.Empty);
            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(second.Controller, Is.Not.SameAs(firstController));
            Assert.That(GetProperty<BoardRuntime>(manager, "ActiveRuntime"), Is.SameAs(second));
            Assert.That(
                second.Views.Values.Where(view => view != player),
                Has.None.Matches<GameObject>(view => firstGeneratedViews.Contains(view)));

            yield return null;
            Assert.That(
                firstGeneratedViews,
                Has.All.Matches<GameObject>(view => view == null));
        }

        [Test]
        public void GameManagerExposesTheAtomicRuntimePublicationSeam()
        {
            Assembly gameAssembly = boardManagerType.Assembly;
            Type gameManagerType = gameAssembly.GetType("GameManager", true);
            PropertyInfo property = gameManagerType.GetProperty(
                "ActiveBoardRuntime",
                BindingFlags.Instance | BindingFlags.Public);

            Assert.That(property, Is.Not.Null);
            Assert.That(property.PropertyType, Is.EqualTo(typeof(BoardRuntime)));
            Assert.That(property.CanWrite, Is.False);
        }

        private Component CreateBoardManager()
        {
            GameObject owner = CreateObject("Runtime Board Manager", 0, 0);
            Component manager = owner.AddComponent(boardManagerType);
            SetField(manager, "columns", 2);
            SetField(manager, "rows", 2);
            SetCount(manager, "wallCount", 0, 0);
            SetCount(manager, "foodCount", 0, 0);
            SetCount(manager, "aidCount", 0, 0);
            SetCount(manager, "buriedCount", 0, 0);

            GameObject floor = CreateObject("Floor Prefab", 0, 0);
            GameObject outer = CreateObject("Outer Prefab", 0, 0);
            GameObject unused = CreateObject("Unused Prefab", 0, 0);
            GameObject exit = CreateObject("Exit Prefab", 0, 0);
            SetField(manager, "floorTiles", new[] { floor });
            SetField(manager, "outerWallTiles", new[] { outer });
            SetField(manager, "wallTiles", new[] { unused });
            SetField(manager, "groundFoodTiles", new[] { unused });
            SetField(manager, "bushFoodTiles", unused);
            SetField(manager, "buriedFoodTiles", new[] { unused });
            SetField(manager, "aidTiles", new[] { unused });
            SetField(manager, "enemyTiles", new[] { unused });
            SetField(manager, "exit", exit);
            return manager;
        }

        private BoardRuntime InvokeSetup(
            Component manager,
            BoardRequest request,
            RunState run,
            GameObject player)
        {
            MethodInfo method = boardManagerType.GetMethod(
                "SetupScene",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new[] { typeof(BoardRequest), typeof(RunState), typeof(GameObject) },
                null);
            Assert.That(method, Is.Not.Null);
            return (BoardRuntime)method.Invoke(manager, new object[] { request, run, player });
        }

        private void SetCount(Component manager, string fieldName, int minimum, int maximum)
        {
            Type countType = boardManagerType.GetNestedType("Count", BindingFlags.Public);
            object count = Activator.CreateInstance(countType, minimum, maximum);
            SetField(manager, fieldName, count);
        }

        private static void SetField(Component target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, fieldName);
            field.SetValue(target, value);
        }

        private static T GetProperty<T>(Component target, string propertyName)
        {
            PropertyInfo property = target.GetType().GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(property, Is.Not.Null, propertyName);
            return (T)property.GetValue(target, null);
        }

        private GameObject CreateObject(string name, int x, int y)
        {
            var instance = new GameObject(name);
            instance.transform.position = new Vector3(x, y, 0f);
            createdObjects.Add(instance);
            return instance;
        }

        private static RunState CreateRun()
        {
            return new RunState(
                "runtime-playmode-run",
                54321,
                new RunStateConfiguration(100, 100, 0, "forest.start"));
        }

        private static BoardRequest CreateRequest(RunState run)
        {
            return new BoardRequest(
                run.RunId,
                run.RunSeed,
                run.WorldNodeId,
                run.CurrentDay,
                run.GetBoardSeed(),
                "forest",
                "temperate",
                1);
        }
    }
}
