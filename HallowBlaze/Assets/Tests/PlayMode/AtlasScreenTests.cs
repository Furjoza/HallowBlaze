using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using HallowBlaze.Core.Session;
using HallowBlaze.Core.State;
using HallowBlaze.Core.World;
using HallowBlaze.Presentation.Runtime;
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

        /// <summary>Checks atlas presentation and retained discovery using compatible pre-opening checkpoint runs.</summary>
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
            choices = routeButtons.Select(button => choices.Single(choice =>
                button.GetComponentInChildren<Text>(true).text ==
                FormatToken(choice.WorldDirection) + "\n" + FormatToken(choice.ClueKey))).ToArray();
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
            Assert.That(run.Route, Is.Empty);
            Assert.That(run.WorldNodeId, Is.EqualTo("forest.start"));
            Assert.That(run.ActiveLeg.EdgeId, Is.EqualTo(chosenEdgeId));
            Assert.That(profile.GetEdgeDiscoveryState(chosenEdgeId), Is.EqualTo(EdgeDiscoveryState.Sighted));
            Assert.That(GetProperty<bool>(atlas, "IsOpen"), Is.False);

            WorldMapService map = GetField<WorldMapService>(manager, "worldMap");
            Assert.That(map.BeginRouteChoice().IsSuccess, Is.False);
            Assert.That(map.Travel.ConsumeIntro(out _).IsSuccess, Is.True);
            var world = GetField<HallowBlaze.Core.World.WorldDefinition>(manager, "worldDefinition");
            while (run.ActiveLeg != null)
            {
                BoardRequest request = BoardRequest.CreateFromRun(run, world);
                Assert.That(map.Travel.TryCompleteSegment(request, out var checkpoint, out _), Is.True);
                Assert.That(checkpoint.IsSuccess, Is.True);
            }
            Assert.That(map.ReconcileCompletedTravel().IsSuccess, Is.True);
            Assert.That(run.Route, Has.Count.EqualTo(1));
            Assert.That(run.WorldNodeId, Is.EqualTo(run.Route[0]));
            Invoke(manager, "SetGameplayInputBlocked", false);
            Assert.That((bool)Invoke(manager, "TryEnterCurrentWorldNode"), Is.True);
            Assert.That((bool)Invoke(manager, "BeginRouteChoice"), Is.True);
            yield return null;

            Assert.That(GetProperty<bool>(atlas, "IsOpen"), Is.True);
            AssertNamedObjectCount(atlasObject, "AtlasEdge-Traversed-", 1);
            string advancedText = GetVisibleText(atlasObject);
            StringAssert.Contains("CURRENT ROUTE / SOLID", advancedText);
            StringAssert.Contains("TRAVERSED  SOLID", advancedText);

            session.StartNewRun("atlas-second-checkpoint", 8,
                new RunStateConfiguration(100, 100, 0, "forest.start"));
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
            session.StartNewRun("atlas-final-checkpoint", 7,
                new RunStateConfiguration(100, 100, 5, "landmark.radio-tower"));
            Assert.That((bool)Invoke(manager, "TryEnterCurrentWorldNode"), Is.True);
            Assert.That((bool)Invoke(manager, "BeginRouteChoice"), Is.True);
            yield return null;
            Assert.That(GetProperty<bool>(atlas, "IsOpen"), Is.True, "Final arrival must show the atlas even without outgoing roads.");
            Assert.That(GetProperty<int>(atlas, "RouteButtonCount"), Is.Zero);
        }

        /// <summary>Route geography, focus and committed identity do not depend on incoming edge order.</summary>
        [UnityTest]
        public IEnumerator RouteChoicesKeepGeographyAndIdentityWhenInputIsReversed()
        {
            GameObject prefab = Resources.Load<GameObject>("Atlas/AtlasScreen");
            TextAsset worldDefinition = GetField<TextAsset>(prefab.GetComponent(atlasControllerType), "worldDefinitionJson");
            Component manager = CreateManager(worldDefinition);
            GameSession session = GetProperty<GameSession>(manager, "Session");
            GameObject atlasObject = UnityEngine.Object.Instantiate(prefab);
            Component atlas = atlasObject.GetComponent(atlasControllerType);
            Assert.That((bool)Invoke(manager, "BeginRouteChoice"), Is.True);
            WorldMapExitOption[] choices = GetProperty<System.Collections.Generic.IReadOnlyList<WorldMapExitOption>>(
                manager, "RouteChoices").ToArray();
            WorldMapExitOption west = choices.Single(choice => choice.WorldDirection.EndsWith("west", StringComparison.Ordinal));
            WorldMapExitOption east = choices.Single(choice => choice.WorldDirection.EndsWith("east", StringComparison.Ordinal));

            foreach (WorldMapExitOption[] permutation in new[] { new[] { east, west }, new[] { west, east } })
            {
                Invoke(atlas, "HandleRouteChoicesChanged", (object)permutation);
                yield return null;
                Canvas.ForceUpdateCanvases();
                Button[] buttons = atlasObject.GetComponentsInChildren<Button>()
                    .Where(button => button.name.StartsWith("AtlasRouteButton-", StringComparison.Ordinal)).ToArray();
                Assert.That(buttons, Has.Length.EqualTo(2));
                string leftLabel = buttons[0].GetComponentInChildren<Text>().text;
                string rightLabel = buttons[1].GetComponentInChildren<Text>().text;
                StringAssert.Contains(FormatToken(west.WorldDirection), leftLabel);
                StringAssert.Contains(FormatToken(west.ClueKey), leftLabel);
                StringAssert.Contains(FormatToken(east.WorldDirection), rightLabel);
                StringAssert.Contains(FormatToken(east.ClueKey), rightLabel);
                Assert.That(buttons[0].transform.position.x, Is.LessThan(buttons[1].transform.position.x));
                KeyboardMenuFocus focus = atlasObject.GetComponent<KeyboardMenuFocus>();
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(buttons[0].gameObject));
                focus.HandleKey(KeyCode.RightArrow);
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(buttons[1].gameObject));
                focus.HandleKey(KeyCode.LeftArrow);
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(buttons[0].gameObject));
                focus.HandleKey(KeyCode.D);
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(buttons[1].gameObject));
                foreach (KeyCode ignored in new[] { KeyCode.W, KeyCode.S })
                {
                    Assert.That(PcInputBindings.MenuKeys.Contains(ignored), Is.False);
                    focus.HandleKey(ignored);
                    Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(buttons[1].gameObject));
                }
                focus.HandleKey(KeyCode.A);
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(buttons[0].gameObject));
                Assert.That(session.ActiveRun.ActiveLeg, Is.Null);
                Assert.That(session.ActiveRun.CurrentDay, Is.Zero);
                Assert.That(GetProperty<string>(atlas, "SelectedRouteEdgeId"), Is.Null);
            }

            atlasObject.GetComponent<KeyboardMenuFocus>().HandleKey(KeyCode.RightArrow);
            atlasObject.GetComponent<KeyboardMenuFocus>().HandleKey(KeyCode.Return);
            yield return null;
            Assert.That(session.ActiveRun.ActiveLeg.EdgeId, Is.EqualTo(east.EdgeId));
            Assert.That(GetProperty<string>(atlas, "SelectedRouteEdgeId"), Is.EqualTo(east.EdgeId));
        }

        /// <summary>All cardinal/diagonal directions and unrecognized alternatives have an input-independent order.</summary>
        [Test]
        public void RouteDirectionMatrixIsStableAcrossPermutations()
        {
            string[] directions = { "west", "northwest", "southwest", "north", "south", "northeast", "east", "southeast", "riverward" };
            WorldMapExitOption[] options = directions.Select((direction, index) => CreateOption(direction, "edge." + index)).ToArray();
            MethodInfo order = atlasControllerType.GetMethod("OrderRouteChoices", BindingFlags.Static | BindingFlags.NonPublic);
            for (int offset = 0; offset < options.Length; offset++)
            {
                WorldMapExitOption[] permutation = options.Skip(offset).Concat(options.Take(offset)).Reverse().ToArray();
                var actual = (System.Collections.Generic.IReadOnlyList<WorldMapExitOption>)order.Invoke(null, new object[] { permutation });
                Assert.That(actual, Is.EqualTo(options));
                Assert.That(permutation, Is.EqualTo(options.Skip(offset).Concat(options.Take(offset)).Reverse()), "Input is read-only.");
            }
            WorldMapExitOption first = CreateOption("west", "a");
            WorldMapExitOption second = CreateOption("west", "b");
            Assert.That(order.Invoke(null, new object[] { new[] { second, first } }), Is.EqualTo(new[] { first, second }));
        }

        /// <summary>Single, paired and extra alternatives remain readable, focused and identity-safe in narrow layouts.</summary>
        [UnityTest]
        public IEnumerator RouteLayoutsRevealEveryAlternativeAndSkipUnavailableButtons()
        {
            GameObject prefab = Resources.Load<GameObject>("Atlas/AtlasScreen");
            Component manager = CreateManager(GetField<TextAsset>(prefab.GetComponent(atlasControllerType), "worldDefinitionJson"));
            GameObject atlasObject = UnityEngine.Object.Instantiate(prefab);
            Component atlas = atlasObject.GetComponent(atlasControllerType);
            Assert.That((bool)Invoke(manager, "BeginRouteChoice"), Is.True);
            WorldMapExitOption[] legal = GetProperty<System.Collections.Generic.IReadOnlyList<WorldMapExitOption>>(
                manager, "RouteChoices").ToArray();
            WorldMapExitOption[] extra = legal.Concat(new[] { CreateOption("north", "north"),
                CreateOption("south", "south"), CreateOption("riverward", "unmarked") }).ToArray();
            Canvas canvas = atlasObject.GetComponent<Canvas>();
            GameObject cameraObject = new GameObject("Atlas capture camera", typeof(Camera));
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1f;
            try
            {
                foreach (Vector2Int size in new[] { new Vector2Int(1280, 720), new Vector2Int(320, 480) })
                foreach (WorldMapExitOption[] options in new[] { legal.Take(1).ToArray(), legal, extra })
                {
                    RenderTexture target = new RenderTexture(size.x, size.y, 24);
                    RenderTexture previous = RenderTexture.active;
                    target.Create();
                    camera.targetTexture = target;
                    Texture2D capture = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
                    try
                    {
                        Invoke(atlas, "HandleRouteChoicesChanged", (object)options);
                        yield return null;
                        Canvas.ForceUpdateCanvases();
                        KeyboardMenuFocus focus = atlasObject.GetComponent<KeyboardMenuFocus>();
                        Button[] buttons = atlasObject.GetComponentsInChildren<Button>().Where(button =>
                            button.name.StartsWith("AtlasRouteButton-", StringComparison.Ordinal)).ToArray();
                        Assert.That(buttons, Has.Length.EqualTo(options.Length));
                        ScrollRect scroll = atlasObject.GetComponentInChildren<ScrollRect>();
                        foreach (Button button in buttons)
                        {
                            Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(button.gameObject));
                            Invoke(atlas, "LateUpdate");
                            Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport, (RectTransform)button.transform);
                            Assert.That(bounds.min.x, Is.GreaterThanOrEqualTo(scroll.viewport.rect.xMin - 1f));
                            Assert.That(bounds.max.x, Is.LessThanOrEqualTo(scroll.viewport.rect.xMax + 1f));
                            Text label = button.GetComponentInChildren<Text>();
                            Assert.That(label.fontSize * canvas.scaleFactor, Is.InRange(14f, 16f));
                            Assert.That(label.preferredHeight, Is.LessThanOrEqualTo(label.rectTransform.rect.height + 1f), label.text);
                            Assert.That(label.cachedTextGenerator.vertexCount, Is.GreaterThan(0));
                            focus.HandleKey(KeyCode.RightArrow);
                        }
                        Invoke(atlas, "LateUpdate");
                        if (options.Length > 2)
                        {
                            scroll.horizontalNormalizedPosition = 1f;
                            Invoke(atlas, "LateUpdate");
                            Assert.That(scroll.horizontalNormalizedPosition, Is.EqualTo(1f).Within(0.01f), "Retained focus must not undo pointer scrolling.");
                            focus.HandleKey(KeyCode.RightArrow);
                            Invoke(atlas, "LateUpdate");
                            scroll.velocity = new Vector2(-1000f, 0f);
                            buttons[0].Select();
                            Invoke(atlas, "LateUpdate");
                            Assert.That(scroll.velocity, Is.EqualTo(Vector2.zero));
                            for (int frame = 0; frame < 3; frame++) yield return null;
                            Bounds retainedBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(
                                scroll.viewport, (RectTransform)buttons[0].transform);
                            Assert.That(retainedBounds.min.x, Is.GreaterThanOrEqualTo(scroll.viewport.rect.xMin - 1f));
                            Assert.That(retainedBounds.max.x, Is.LessThanOrEqualTo(scroll.viewport.rect.xMax + 1f));
                        }
                        camera.Render();
                        RenderTexture.active = target;
                        capture.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0);
                        capture.Apply();
                        Assert.That(capture.GetPixels32().Distinct().Count(), Is.GreaterThan(12));
                        File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "HB-M710-routes-" + options.Length + "-" + size.x + "x" + size.y + ".png"), capture.EncodeToPNG());
                        if (buttons.Length > 1)
                        {
                            buttons[0].interactable = false;
                            buttons[1].Select();
                            focus.HandleKey(KeyCode.LeftArrow);
                            Assert.That(EventSystem.current.currentSelectedGameObject, Is.Not.SameAs(buttons[0].gameObject));
                        }
                        Assert.That(GetProperty<GameSession>(manager, "Session").ActiveRun.ActiveLeg, Is.Null);
                    }
                    finally
                    {
                        camera.targetTexture = null;
                        RenderTexture.active = previous;
                        UnityEngine.Object.DestroyImmediate(capture);
                        target.Release();
                        UnityEngine.Object.DestroyImmediate(target);
                    }
                }
            }
            finally
            {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.worldCamera = null;
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        private static WorldMapExitOption CreateOption(string direction, string edgeId)
        {
            return (WorldMapExitOption)Activator.CreateInstance(typeof(WorldMapExitOption), InstanceFlags,
                null, new object[] { new WorldEdgeDefinition(edgeId, "source", "destination", direction, "clue.fallen-trees") }, null);
        }

        private Component CreateManager(TextAsset worldDefinition)
        {
            GameObject managerObject = new GameObject("M2.6 Atlas Manager");
            Component manager = managerObject.AddComponent(gameManagerType);
            SetField(manager, "worldDefinitionJson", worldDefinition);
            SetField(manager, "sceneLoader", (Action<string>)(_ => { }));
            Invoke(manager, "EnsurePersistence");
            GetProperty<GameSession>(manager, "Session").StartNewRun("atlas-first-checkpoint", 7,
                new RunStateConfiguration(100, 100, 0, "forest.start"));
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