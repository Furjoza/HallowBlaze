using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using HallowBlaze.Core.Session;
using HallowBlaze.Core.State;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace HallowBlaze.Tests.PlayMode
{
    public sealed class AtlasScreenTests
    {
        private const BindingFlags InstanceFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

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
                "HallowBlaze-M2.6-Atlas-" + Guid.NewGuid().ToString("N"));
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

        [UnityTest]
        public IEnumerator AtlasRendersKnownInformationAndCarriesDiscoveryIntoSecondRun()
        {
            GameObject prefab = Resources.Load<GameObject>("Atlas/AtlasScreen");
            Assert.That(prefab, Is.Not.Null);
            Component prefabController = prefab.GetComponent(atlasControllerType);
            Assert.That(prefabController, Is.Not.Null);
            TextAsset worldDefinition = GetField<TextAsset>(prefabController, "worldDefinitionJson");
            Assert.That(worldDefinition, Is.Not.Null);

            Component manager = CreateManager(worldDefinition);
            GameSession session = GetProperty<GameSession>(manager, "Session");
            ProfileState profile = session.Profile;
            Assert.That(
                profile.AdvanceNodeDiscovery("forest.old-road", NodeDiscoveryState.Rumored),
                Is.True);

            GameObject atlasObject = UnityEngine.Object.Instantiate(prefab);
            atlasObject.name = "AtlasScreen";
            Component atlas = atlasObject.GetComponent(atlasControllerType);
            Assert.That(atlas, Is.Not.Null);

            int profileNodesBeforeOpen = profile.NodeDiscoveries.Count;
            int profileEdgesBeforeOpen = profile.EdgeDiscoveries.Count;
            RunState run = session.ActiveRun;
            Assert.That((bool)Invoke(manager, "BeginRouteChoice"), Is.True);
            yield return null;

            Assert.That(GetProperty<bool>(atlas, "IsOpen"), Is.True);
            Assert.That(GetProperty<bool>(atlas, "AnimationsEnabled"), Is.False);
            Assert.That(GetProperty<int>(atlas, "RenderedNodeCount"), Is.EqualTo(4));
            Assert.That(GetProperty<int>(atlas, "RenderedEdgeCount"), Is.EqualTo(2));
            Assert.That(GetProperty<int>(atlas, "RouteButtonCount"), Is.EqualTo(2));
            Assert.That(GetProperty<string>(atlas, "LastDiscoveryMessage"),
                Is.EqualTo("DISCOVERY: ROUTES OBSERVED"));

            AssertNamedObjectCount(atlasObject, "AtlasNode-Rumored-", 1);
            AssertNamedObjectCount(atlasObject, "AtlasNode-Sighted-", 2);
            AssertNamedObjectCount(atlasObject, "AtlasNode-Visited-", 1);
            AssertNamedObjectCount(atlasObject, "AtlasEdge-Sighted-", 2);
            AssertNamedObjectCount(atlasObject, "AtlasEdge-Traversed-", 0);
            AssertNamedObjectCount(atlasObject, "AtlasRouteButton-", 2);

            string initialText = GetVisibleText(atlasObject);
            StringAssert.Contains("RUMORED PLACE", initialText);
            StringAssert.Contains("SIGHTED PLACE", initialText);
            StringAssert.Contains("SHELTER", initialText);
            StringAssert.Contains("VISITED / SOLID", initialText);
            StringAssert.Contains("SIGHTED ROAD / DASHED", initialText);
            StringAssert.Contains("UNKNOWN  HIDDEN", initialText);
            StringAssert.Contains("CURRENT", initialText);
            StringAssert.DoesNotContain("FOREST.", initialText);
            StringAssert.DoesNotContain("CROSSROADS", initialText);
            StringAssert.DoesNotContain("WET-FOREST", initialText);
            StringAssert.DoesNotContain("QUARRY", initialText);
            StringAssert.DoesNotContain("RIDGE", initialText);
            StringAssert.DoesNotContain("RADIO", initialText);
            StringAssert.DoesNotContain("GROVE", initialText);

            RectTransform[] sightedNodes = atlasObject.GetComponentsInChildren<RectTransform>(true)
                .Where(child => child.name.StartsWith("AtlasNode-Sighted-"))
                .ToArray();
            Assert.That(sightedNodes, Has.Length.EqualTo(2));
            Assert.That(
                sightedNodes.All(node =>
                    node.GetComponentInChildren<Text>(true).text == "SIGHTED PLACE"),
                Is.True);

            RectTransform sightedEdge = FindByPrefix(atlasObject, "AtlasEdge-Sighted-");
            Assert.That(sightedEdge, Is.Not.Null);
            Assert.That(
                sightedEdge.Cast<Transform>().Count(child => child.name.StartsWith("Dash-")),
                Is.EqualTo(7));
            RectTransform currentNode = FindByPrefix(atlasObject, "AtlasNode-Visited-");
            Assert.That(currentNode, Is.Not.Null);
            Assert.That(currentNode.GetComponent<Outline>(), Is.Not.Null);
            StringAssert.Contains("CURRENT", currentNode.GetComponentInChildren<Text>(true).text);

            CanvasScaler scaler = atlasObject.GetComponent<CanvasScaler>();
            Assert.That(scaler, Is.Not.Null);
            Assert.That(scaler.uiScaleMode, Is.EqualTo(CanvasScaler.ScaleMode.ScaleWithScreenSize));
            Assert.That(scaler.referenceResolution, Is.EqualTo(new Vector2(1920f, 1080f)));

            WorldMapExitOption[] choices = GetProperty<System.Collections.Generic.IReadOnlyList<WorldMapExitOption>>(
                manager,
                "RouteChoices").ToArray();
            Button[] routeButtons = atlasObject.GetComponentsInChildren<Button>(true)
                .Where(button => button.name.StartsWith("AtlasRouteButton-"))
                .OrderBy(button => button.name)
                .ToArray();
            Assert.That(routeButtons, Has.Length.EqualTo(choices.Length));
            Assert.That(routeButtons.All(button => button.interactable), Is.True);
            Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(routeButtons[0].gameObject));
            for (int index = 0; index < routeButtons.Length; index++)
            {
                string label = routeButtons[index].GetComponentInChildren<Text>(true).text;
                StringAssert.Contains(FormatToken(choices[index].WorldDirection), label);
                StringAssert.Contains(FormatToken(choices[index].ClueKey), label);
                StringAssert.DoesNotContain("FOREST", label);
            }

            int nodesAfterDiscovery = profile.NodeDiscoveries.Count;
            int edgesAfterDiscovery = profile.EdgeDiscoveries.Count;
            string profileJsonBeforeRefresh = File.ReadAllText(Path.Combine(persistenceRoot, "profile.json"));
            Invoke(atlas, "RefreshAtlas");
            Invoke(atlas, "RefreshAtlas");
            Assert.That(profile.NodeDiscoveries.Count, Is.EqualTo(nodesAfterDiscovery));
            Assert.That(profile.EdgeDiscoveries.Count, Is.EqualTo(edgesAfterDiscovery));
            Assert.That(run.CurrentDay, Is.Zero);
            Assert.That(run.WorldNodeId, Is.EqualTo("forest.start"));
            Assert.That(run.Route, Is.Empty);
            Assert.That(
                File.ReadAllText(Path.Combine(persistenceRoot, "profile.json")),
                Is.EqualTo(profileJsonBeforeRefresh));
            Assert.That(profileNodesBeforeOpen, Is.EqualTo(2));
            Assert.That(profileEdgesBeforeOpen, Is.Zero);

            string chosenEdgeId = choices[0].EdgeId;
            ExecuteEvents.Execute(
                routeButtons[0].gameObject,
                new BaseEventData(EventSystem.current),
                ExecuteEvents.submitHandler);
            yield return null;

            Assert.That(GetProperty<string>(atlas, "SelectedRouteEdgeId"), Is.EqualTo(chosenEdgeId));
            Assert.That(run.CurrentDay, Is.EqualTo(1));
            Assert.That(run.Route, Has.Count.EqualTo(1));
            Assert.That(run.WorldNodeId, Is.EqualTo(run.Route[0]));
            Assert.That(GetProperty<bool>(atlas, "IsOpen"), Is.False);

            Invoke(manager, "SetGameplayInputBlocked", false);
            Assert.That((bool)Invoke(manager, "TryEnterCurrentWorldNode"), Is.True);
            Assert.That((bool)Invoke(manager, "BeginRouteChoice"), Is.True);
            yield return null;

            Assert.That(GetProperty<bool>(atlas, "IsOpen"), Is.True);
            AssertNamedObjectCount(atlasObject, "AtlasEdge-Traversed-", 1);
            string advancedText = GetVisibleText(atlasObject);
            StringAssert.Contains("CURRENT ROUTE / SOLID", advancedText);
            StringAssert.Contains("TRAVERSED  SOLID", advancedText);

            Invoke(manager, "StartNewRun");
            RunState secondRun = session.ActiveRun;
            Assert.That(secondRun, Is.Not.SameAs(run));
            Assert.That(session.Profile, Is.SameAs(profile));
            Assert.That((bool)Invoke(manager, "BeginRouteChoice"), Is.True);
            yield return null;

            Assert.That(GetProperty<bool>(atlas, "IsOpen"), Is.True);
            Assert.That(profile.GetEdgeDiscoveryState(chosenEdgeId),
                Is.EqualTo(EdgeDiscoveryState.Traversed));
            Assert.That(profile.GetNodeDiscoveryState(run.WorldNodeId),
                Is.EqualTo(NodeDiscoveryState.Visited));
            AssertNamedObjectCount(atlasObject, "AtlasEdge-Traversed-", 1);
            Assert.That(GetProperty<int>(atlas, "RenderedNodeCount"), Is.GreaterThanOrEqualTo(4));
        }

        private Component CreateManager(TextAsset worldDefinition)
        {
            GameObject managerObject = new GameObject("M2.6 Atlas Manager");
            Component manager = managerObject.AddComponent(gameManagerType);
            SetField(manager, "worldDefinitionJson", worldDefinition);
            SetField(manager, "sceneLoader", (Action<string>)(_ => { }));
            Invoke(manager, "EnsurePersistence");
            Invoke(manager, "StartNewRun");
            Assert.That((bool)Invoke(manager, "TryEnterCurrentWorldNode"), Is.True);
            return manager;
        }

        private void DestroyGameManagerSingleton()
        {
            object manager = GetStaticField(gameManagerType, "instance");
            if (manager is Component component)
                UnityEngine.Object.DestroyImmediate(component.gameObject);
            SetStaticField(gameManagerType, "instance", null);
        }

        private static void DestroyAll(Type componentType)
        {
            UnityEngine.Object[] components = UnityEngine.Object.FindObjectsByType(
                componentType,
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            foreach (UnityEngine.Object component in components)
                UnityEngine.Object.DestroyImmediate(((Component)component).gameObject);
        }

        private static void AssertNamedObjectCount(GameObject root, string prefix, int expected)
        {
            int count = root.GetComponentsInChildren<Transform>(true)
                .Count(child => child.name.StartsWith(prefix));
            Assert.That(count, Is.EqualTo(expected), prefix);
        }

        private static RectTransform FindByPrefix(GameObject root, string prefix)
        {
            Transform match = root.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(child => child.name.StartsWith(prefix));
            return match as RectTransform;
        }

        private static string GetVisibleText(GameObject root)
        {
            return string.Join(
                "\n",
                root.GetComponentsInChildren<Text>(true).Select(text => text.text));
        }

        private static string FormatToken(string token)
        {
            int separator = token.LastIndexOf('.');
            string visible = separator >= 0 ? token.Substring(separator + 1) : token;
            return visible.Replace('-', ' ').Replace('_', ' ').ToUpperInvariant();
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

        private static T GetField<T>(Component target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(fieldName, InstanceFlags);
            Assert.That(field, Is.Not.Null, fieldName + " must exist.");
            return (T)field.GetValue(target);
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
            FieldInfo field = type.GetField(
                fieldName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
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
            MethodInfo method = type.GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null, methodName + " must exist.");
            return method.Invoke(null, null);
        }
    }
}