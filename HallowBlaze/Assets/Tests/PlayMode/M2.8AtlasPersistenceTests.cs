using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using HallowBlaze.Core.Persistence.Storage;
using HallowBlaze.Core.Session;
using HallowBlaze.Core.State;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace HallowBlaze.Tests.PlayMode
{
    /// <summary>
    /// Integration test for M2.8: Proof of atlas persistence across runs.
    /// This test validates that discoveries made during one run persist
    /// into subsequent runs using the same profile.
    /// </summary>
    public sealed class M28AtlasPersistenceTests
    {
        private const BindingFlags InstanceFlags =
            BindingFlags.Instance |
            BindingFlags.Static |
            BindingFlags.Public |
            BindingFlags.NonPublic;

        private Type atlasControllerType;
        private Type gameManagerType;
        private string persistenceRoot;

        [SetUp]
        public void SetUp()
        {
            Assembly gameAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .First(assembly => assembly.GetName().Name == "Assembly-CSharp");
            atlasControllerType = RequireType(
                gameAssembly,
                "HallowBlaze.Presentation.Atlas.AtlasScreenController");
            gameManagerType = RequireType(gameAssembly, "GameManager");

            DestroyAll(atlasControllerType);
            DestroyGameManagerSingleton();
            persistenceRoot = Path.Combine(
                Path.GetTempPath(),
                "HallowBlaze-M2.8-Atlas-Persistence-" + Guid.NewGuid().ToString("N"));
            SetStaticField(gameManagerType, "PersistenceRootOverride", persistenceRoot);
            InvokeStatic(gameManagerType, "RequestNewRun");
        }

        [TearDown]
        public void TearDown()
        {
            DestroyAll(atlasControllerType);
            DestroyGameManagerSingleton();
            SetStaticField(gameManagerType, "PersistenceRootOverride", null);
            if (Directory.Exists(persistenceRoot))
                Directory.Delete(persistenceRoot, true);
        }

        /// <summary>
        /// Main integration test: Automates a sequence of two runs to prove
        /// that discoveries persist across runs.
        /// </summary>
        [UnityTest]
        public IEnumerator AtlasPersistenceAcrossTwoRuns()
        {
            GameObject prefab = Resources.Load<GameObject>("Atlas/AtlasScreen");
            Assert.That(prefab, Is.Not.Null, "Atlas screen prefab should exist");
            Component prefabController = prefab.GetComponent(atlasControllerType);
            Assert.That(prefabController, Is.Not.Null, "Atlas controller should be present");
            TextAsset worldDefinition = GetField<TextAsset>(prefabController, "worldDefinitionJson");
            Assert.That(worldDefinition, Is.Not.Null, "World definition should be referenced by the atlas prefab");

            // Create the game manager with the world definition
            Component manager = CreateManager(worldDefinition);
            GameSession session = GetProperty<GameSession>(manager, "Session");
            ProfileState profile = session.Profile;
            RunState firstRun = session.ActiveRun;

            // Verify initial state
            Assert.That(firstRun.CurrentDay, Is.Zero, "First run should start at day 0");
            Assert.That(firstRun.WorldNodeId, Is.EqualTo("forest.start"), "Should start at forest.start");

            // Load the Atlas screen
            GameObject atlasObject = UnityEngine.Object.Instantiate(prefab);
            atlasObject.name = "AtlasScreen-M2.8";
            Component atlas = atlasObject.GetComponent(atlasControllerType);
            Assert.That(atlas, Is.Not.Null, "Atlas controller should be present");

            // Open the atlas for the first time
            Assert.That((bool)Invoke(manager, "BeginRouteChoice"), Is.True);
            yield return null;
            Assert.That(GetProperty<bool>(atlas, "IsOpen"), Is.True, "Atlas should be open");

            // Record initial discovery state
            int initialNodeCount = profile.NodeDiscoveries.Count;
            int initialEdgeCount = profile.EdgeDiscoveries.Count;
            Assert.That(initialNodeCount, Is.GreaterThan(0), "Should have some initial node discoveries");
            Assert.That(initialEdgeCount, Is.GreaterThan(0), "Should have some initial edge discoveries");

            // Make some discoveries in the first run
            // Discover a node that hasn't been discovered yet
            Assert.That(
                profile.AdvanceNodeDiscovery("forest.west-grove", NodeDiscoveryState.Rumored),
                Is.True, "Should be able to discover forest.west-grove");

            // Discover an edge that hasn't been discovered yet
            Assert.That(
                profile.AdvanceEdgeDiscovery("road.old-road-west-grove", EdgeDiscoveryState.Sighted),
                Is.True, "Should be able to discover road.old-road-west-grove");

            // Refresh the atlas to show discoveries
            Invoke(atlas, "RefreshAtlas");
            yield return null;

            // Record state after discoveries
            int nodesAfterFirstRun = profile.NodeDiscoveries.Count;
            int edgesAfterFirstRun = profile.EdgeDiscoveries.Count;
            Assert.That(nodesAfterFirstRun, Is.GreaterThan(initialNodeCount), "Should have more node discoveries");
            Assert.That(edgesAfterFirstRun, Is.GreaterThan(initialEdgeCount), "Should have more edge discoveries");

            // Verify the discoveries are in the profile
            Assert.That(
                profile.GetNodeDiscoveryState("forest.west-grove"),
                Is.EqualTo(NodeDiscoveryState.Rumored),
                "forest.west-grove should be rumored");
            Assert.That(
                profile.GetEdgeDiscoveryState("road.old-road-west-grove"),
                Is.EqualTo(EdgeDiscoveryState.Sighted),
                "road.old-road-west-grove should be sighted");

            FileSystemSaveStore saveStore = new FileSystemSaveStore(persistenceRoot);
            Assert.That(saveStore.SaveProfile(profile).IsSuccess, Is.True,
                "First run discoveries should be saved before the next run.");

            // Close the atlas and advance to the next day
            Invoke(atlas, "SetOpen", false);
            yield return null;
            Assert.That(GetProperty<bool>(atlas, "IsOpen"), Is.False, "Atlas should be closed");

            // Start a new run through a fresh manager and session.
            string profilePath = Path.Combine(persistenceRoot, "profile.json");
            Assert.That(File.Exists(profilePath), "Profile JSON file should exist");
            string profileContent = File.ReadAllText(profilePath);
            Assert.That(profileContent, Does.Contain("forest.west-grove"), "Profile should contain forest.west-grove");
            Assert.That(profileContent, Does.Contain("road.old-road-west-grove"), "Profile should contain road.old-road-west-grove");

            UnityEngine.Object.DestroyImmediate(atlasObject);
            DestroyGameManagerSingleton();

            Component secondManager = CreateManager(worldDefinition);
            GameSession secondSession = GetProperty<GameSession>(secondManager, "Session");
            ProfileState reloadedProfile = secondSession.Profile;
            RunState secondRun = secondSession.ActiveRun;
            Assert.That(secondRun, Is.Not.SameAs(firstRun), "Second run should be a different run state");
            Assert.That(reloadedProfile, Is.Not.SameAs(profile), "Second run should load a new profile instance");

            // Open the atlas again in the second run
            GameObject secondAtlasObject = UnityEngine.Object.Instantiate(prefab);
            secondAtlasObject.name = "AtlasScreen-M2.8-SecondRun";
            Component secondAtlas = secondAtlasObject.GetComponent(atlasControllerType);
            Assert.That(secondAtlas, Is.Not.Null, "Second atlas controller should be present");
            Assert.That((bool)Invoke(secondManager, "BeginRouteChoice"), Is.True);
            yield return null;
            Assert.That(GetProperty<bool>(secondAtlas, "IsOpen"), Is.True, "Atlas should be open in second run");

            // Verify that discoveries from the first run persist
            Assert.That(reloadedProfile.GetNodeDiscoveryState("forest.west-grove"),
                Is.EqualTo(NodeDiscoveryState.Rumored),
                "forest.west-grove discovery should persist into second run");
            Assert.That(reloadedProfile.GetEdgeDiscoveryState("road.old-road-west-grove"),
                Is.EqualTo(EdgeDiscoveryState.Sighted),
                "road.old-road-west-grove discovery should persist into second run");

            // Verify that the profile still has the same discoveries
            Assert.That(reloadedProfile.NodeDiscoveries.Count, Is.EqualTo(nodesAfterFirstRun),
                "Node discovery count should be the same across runs");
            Assert.That(reloadedProfile.EdgeDiscoveries.Count, Is.EqualTo(edgesAfterFirstRun),
                "Edge discovery count should be the same across runs");

            // Verify that the profile JSON file persists across runs
            Assert.That(File.Exists(profilePath), "Profile JSON file should exist");
            string finalProfileContent = File.ReadAllText(profilePath);
            Assert.That(finalProfileContent, Does.Contain("forest.west-grove"), "Profile should contain forest.west-grove");
            Assert.That(finalProfileContent, Does.Contain("road.old-road-west-grove"), "Profile should contain road.old-road-west-grove");
            UnityEngine.Object.DestroyImmediate(secondAtlasObject);
        }

        /// <summary>
        /// Helper method to create a game manager with a world definition.
        /// </summary>
        private Component CreateManager(TextAsset worldDefinition)
        {
            GameObject managerObject = new GameObject("M2.8 Atlas Manager");
            Component manager = managerObject.AddComponent(gameManagerType);
            SetField(manager, "worldDefinitionJson", worldDefinition);
            SetField(manager, "sceneLoader", (Action<string>)(_ => { }));
            Invoke(manager, "EnsurePersistence");
            Invoke(manager, "StartNewRun");
            Assert.That((bool)Invoke(manager, "TryEnterCurrentWorldNode"), Is.True);
            return manager;
        }

        /// <summary>
        /// Helper method to destroy the GameManager singleton.
        /// </summary>
        private void DestroyGameManagerSingleton()
        {
            object manager = GetStaticField(gameManagerType, "instance");
            if (manager is Component component)
                UnityEngine.Object.DestroyImmediate(component.gameObject);
            SetStaticField(gameManagerType, "instance", null);
        }

        /// <summary>
        /// Helper method to destroy all instances of a component type.
        /// </summary>
        private static void DestroyAll(Type componentType)
        {
            UnityEngine.Object[] components = UnityEngine.Object.FindObjectsByType(
                componentType,
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            foreach (UnityEngine.Object component in components)
                UnityEngine.Object.DestroyImmediate(((Component)component).gameObject);
        }

        /// <summary>
        /// Helper method to require a type from an assembly.
        /// </summary>
        private static Type RequireType(Assembly assembly, string name)
        {
            Type type = assembly.GetType(name);
            Assert.That(type, Is.Not.Null, name + " must exist in Assembly-CSharp.");
            return type;
        }

        /// <summary>
        /// Helper method to invoke a method on a component.
        /// </summary>
        private static object Invoke(Component target, string methodName, params object[] arguments)
        {
            MethodInfo method = target.GetType().GetMethod(methodName, InstanceFlags);
            Assert.That(method, Is.Not.Null, methodName + " must exist.");
            return method.Invoke(target, arguments);
        }

        /// <summary>
        /// Helper method to get a field value from a component.
        /// </summary>
        private static T GetField<T>(Component target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(fieldName, InstanceFlags);
            Assert.That(field, Is.Not.Null, fieldName + " must exist.");
            return (T)field.GetValue(target);
        }

        /// <summary>
        /// Helper method to set a field value on a component.
        /// </summary>
        private static void SetField(Component target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, InstanceFlags);
            Assert.That(field, Is.Not.Null, fieldName + " must exist.");
            field.SetValue(target, value);
        }

        /// <summary>
        /// Helper method to get a property value from a component.
        /// </summary>
        private static T GetProperty<T>(Component target, string propertyName)
        {
            PropertyInfo property = target.GetType().GetProperty(propertyName, InstanceFlags);
            Assert.That(property, Is.Not.Null, propertyName + " must exist.");
            return (T)property.GetValue(target);
        }

        /// <summary>
        /// Helper method to invoke a static method.
        /// </summary>
        private static object InvokeStatic(Type type, string methodName, params object[] arguments)
        {
            MethodInfo method = type.GetMethod(methodName, InstanceFlags);
            Assert.That(method, Is.Not.Null, methodName + " must exist.");
            return method.Invoke(null, arguments);
        }

        /// <summary>
        /// Helper method to get a static field value.
        /// </summary>
        private static object GetStaticField(Type type, string fieldName)
        {
            FieldInfo field = type.GetField(fieldName, InstanceFlags);
            Assert.That(field, Is.Not.Null, fieldName + " must exist.");
            return field.GetValue(null);
        }

        /// <summary>
        /// Helper method to set a static field value.
        /// </summary>
        private static void SetStaticField(Type type, string fieldName, object value)
        {
            FieldInfo field = type.GetField(fieldName, InstanceFlags);
            Assert.That(field, Is.Not.Null, fieldName + " must exist.");
            field.SetValue(null, value);
        }
    }
}