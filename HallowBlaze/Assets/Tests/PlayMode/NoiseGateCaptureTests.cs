#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.Session;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Contracts;
using HallowBlaze.Presentation.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using EntityId = HallowBlaze.Core.Board.Primitives.EntityId;
using Object = UnityEngine.Object;

namespace HallowBlaze.Tests.PlayMode
{
    /// <summary>Exports mechanically checked moderator cards, never participant answers or a human gate verdict.</summary>
    public sealed class NoiseGateCaptureTests
    {
        private const int ImageSize = 960;
        private const int CaptureLayer = 31;
        private string outputRoot;
        private Sprite tile;
        private Sprite playerSprite;
        private Sprite enemySprite;
        private readonly HashSet<string> completedForms = new HashSet<string>();

        /// <summary>Creates an isolated external package and borrows sprite art without instantiating legacy behaviours.</summary>
        [OneTimeSetUp]
        public void CreatePackage()
        {
            Assert.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(UnityEngine.Rendering.GraphicsDeviceType.Null));
            outputRoot = Path.Combine(Path.GetTempPath(), "HB-M49-cards-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(outputRoot);
            tile = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1);
            playerSprite = ReadSprite("Assets/Prefabs/Player.prefab");
            enemySprite = ReadSprite("Assets/Prefabs/Enemy1.prefab");
        }

        /// <summary>Marks a package usable only after both forms pass, then releases the sole owned sprite.</summary>
        [OneTimeTearDown]
        public void FinishPackage()
        {
            if (completedForms.SetEquals(new[] { "A", "B" }))
                File.WriteAllText(Path.Combine(outputRoot, "PACKAGE-COMPLETE.txt"),
                    "Protocol version 1; forms A/B mechanically validated. Human sessions: 0. Gate: Pending.\n", Encoding.UTF8);
            if (tile != null) Object.DestroyImmediate(tile);
            TestContext.WriteLine("M4.9 package: " + outputRoot);
        }

        /// <summary>Checks nine scenarios per mirrored form against production resolution and captures blind/outcome images separately.</summary>
        [TestCase("A", false)]
        [TestCase("B", true)]
        public void ExportFormWithVerifiedOutcomes(string form, bool mirrored)
        {
            string participant = Path.Combine(outputRoot, "participant", form);
            string moderator = Path.Combine(outputRoot, "moderator", form);
            Directory.CreateDirectory(participant);
            Directory.CreateDirectory(moderator);
            var cards = new StringBuilder("# Form " + form + "\n\nP = player; S = Shambler; L = Listener; F = interaction object.\n" +
                "Coordinates are printed around the board. Record predictions before the moderator reveals an outcome.\n\n");
            var key = new StringBuilder("# Moderator Only: Form " + form + "\n\nProtocol version 1. Unity " + Application.unityVersion +
                ". Animations and audio off. No participant responses are generated.\n\n");
            for (int caseNumber = 1; caseNumber <= 9; caseNumber++)
            {
                string id = form + caseNumber.ToString("00");
                cards.Append("## " + id + "\n\n![](" + id + "-before.png)\n\n" + Prompt(caseNumber, mirrored) + "\n\n");
                foreach (bool noisyChoice in caseNumber == 9 ? new[] { false, true } : new[] { false })
                {
                    using (Fixture fixture = CreateFixture(caseNumber, mirrored))
                    {
                        EnemyIntent announced = fixture.Intent;
                        Assert.That(announced.Kind, Is.EqualTo(ExpectedIntent(caseNumber)), id);
                        if (caseNumber >= 5 && caseNumber <= 8)
                        {
                            Assert.That(fixture.Display.Projections.Single().InvestigationTarget, Is.EqualTo(announced.InvestigationTarget), id);
                            Assert.That(fixture.Display.Projections.Single().NoiseId, Is.EqualTo(announced.NoiseId), id);
                        }
                        if (!noisyChoice)
                            Capture(fixture, Path.Combine(participant, id + "-before.png"), true);
                        int healthBefore = fixture.Run.Health;
                        int foodBefore = fixture.Run.Food;
                        var results = new List<TurnResult>();
                        results.Add(Submit(fixture, Command(caseNumber, mirrored, noisyChoice, fixture)));
                        if (caseNumber == 9)
                            results.Add(Submit(fixture, new WaitCommand()));
                        GridPosition expected = ExpectedCell(caseNumber, noisyChoice, mirrored);
                        Assert.That(fixture.Runtime.BoardState.TryGetEntity(fixture.EnemyId, out BoardEntityState enemy), Is.True, id);
                        Assert.That(enemy.Position, Is.EqualTo(expected), id);
                        Assert.That(fixture.Run.Health - healthBefore, Is.EqualTo(caseNumber == 1 ? -10 : 0), id);
                        Assert.That(foodBefore - fixture.Run.Food, Is.EqualTo(results.Count), id);
                        GameEvent[] enemyEvents = results.SelectMany(result => result.Events).Where(gameEvent =>
                            gameEvent is EntityMovedEvent moved && moved.EntityId.Equals(fixture.EnemyId) ||
                            gameEvent is EntityWaitedEvent waited && waited.EntityId.Equals(fixture.EnemyId) ||
                            gameEvent is EnemyAttackResolvedEvent attack && attack.AttackerId.Equals(fixture.EnemyId)).ToArray();
                        Assert.That(enemyEvents.Length, Is.EqualTo(results.Count), id);
                        string expectedAction = ExpectedAction(caseNumber, noisyChoice);
                        Assert.That(string.Join(",", enemyEvents.Select(Outcome)), Is.EqualTo(expectedAction), id);
                        if (caseNumber == 7)
                        {
                            Assert.That(((EntityMovedEvent)enemyEvents[0]).To, Is.EqualTo(announced.TargetPosition), id);
                            Assert.That(fixture.Intent.InvestigationTarget, Is.EqualTo(Cell(1, 1, mirrored)), id);
                        }
                        if (caseNumber == 8)
                        {
                            Assert.That(fixture.Runtime.BoardState.TryGetListenerNoise(fixture.EnemyId, out _), Is.False, id);
                            Assert.That(fixture.Intent.Kind, Is.EqualTo(EnemyIntentKind.Wait), id);
                        }
                        string branch = caseNumber == 9 ? noisyChoice ? "interact-wait" : "wait-wait" : "result";
                        string image = id + "-" + branch + ".png";
                        Capture(fixture, Path.Combine(moderator, image), false);
                        key.Append("## " + id + " / " + branch + "\n\nAnnounced: " + announced.Kind +
                            "; step " + (announced.TargetPosition?.ToString() ?? "none") + "; noise source " +
                            (announced.InvestigationTarget?.ToString() ?? "none") + ".\n\nActual: " + expectedAction +
                            "; final enemy cell " + expected + "; HP change " + (fixture.Run.Health - healthBefore) +
                            "; Food cost " + results.Count + "; next announced investigation source " +
                            (fixture.Intent.InvestigationTarget?.ToString() ?? "none") + ".\n\n![](" + image + ")\n\n");
                    }
                }
            }
            File.WriteAllText(Path.Combine(participant, "cards.md"), cards.ToString(), Encoding.UTF8);
            File.WriteAllText(Path.Combine(moderator, "answer-key.md"), key.ToString(), Encoding.UTF8);
            File.WriteAllText(Path.Combine(moderator, "responses-template.csv"),
                "participant_id,phase,form,case_id,command,locked_prediction,reason,actual_outcome,correct,error_category,assistance,evidence\n", Encoding.UTF8);
            Assert.That(Directory.GetFiles(participant, "*-before.png").Length, Is.EqualTo(9));
            Assert.That(Directory.GetFiles(moderator, "*.png").Length, Is.EqualTo(10));
            Assert.That(Directory.GetFiles(participant).All(path => path.EndsWith("-before.png", StringComparison.Ordinal) ||
                Path.GetFileName(path) == "cards.md"), Is.True, "No answers or outcome images may enter the participant folder.");
            completedForms.Add(form);
            TestContext.WriteLine("Verified form " + form + ": 9 blind cards, 10 outcome branches. Root: " + outputRoot);
        }

        private Fixture CreateFixture(int caseNumber, bool mirrored)
        {
            var root = new GameObject("M4.9 isolated card");
            var run = new RunState("m49-" + caseNumber, 49, new RunStateConfiguration(100, 30, 0, "forest.start"));
            var request = new BoardRequest(run.RunId, run.RunSeed, run.WorldNodeId, run.CurrentDay,
                run.GetBoardSeed(), "forest", "temperate", 1);
            var layout = new List<LegacyBoardView>();
            for (int horizontal = 0; horizontal < 6; horizontal++)
                for (int vertical = 0; vertical < 6; vertical++)
                    layout.Add(View(root, LegacyBoardContentKind.Floor, new GridPosition(horizontal, vertical)));
            bool listener = caseNumber >= 5;
            GridPosition player = caseNumber == 5 || caseNumber == 6 || caseNumber == 8 ? Cell(0, 0, mirrored) : Cell(1, 1, mirrored);
            GridPosition enemy = Cell(caseNumber == 3 || caseNumber == 4 ? 2 : 3, 1, mirrored);
            layout.Add(View(root, LegacyBoardContentKind.Player, player));
            layout.Add(View(root, listener ? LegacyBoardContentKind.Listener : LegacyBoardContentKind.Enemy, enemy));
            layout.Add(View(root, LegacyBoardContentKind.Exit, Cell(5, 5, mirrored)));
            if (caseNumber == 7 || caseNumber == 9)
                layout.Add(View(root, LegacyBoardContentKind.Aid, Cell(1, 2, mirrored)));
            BoardRuntime runtime = null;
            BoardEventPresenter presenter = null;
            try
            {
                runtime = LegacyBoardRuntimeComposer.Compose(request, run, new GridBounds(0, 0, 5, 5), layout);
                var owner = new GameObject("Production intent display");
                owner.transform.SetParent(root.transform, false);
                EnemyIntentPresenter display = owner.AddComponent<EnemyIntentPresenter>();
                var sinks = new Sinks();
                presenter = new BoardEventPresenter(runtime, sinks, sinks, sinks,
                    diagnostic => throw new InvalidOperationException("Capture replay failed: " + diagnostic.Code), 0f, display)
                { AnimationsEnabled = false };
                presenter.NoiseDisplay.transform.SetParent(root.transform, false);
                presenter.SynchronizeFromState();
                presenter.Coordinator.Gate.SetBlocked(PresentationInputBlock.Setup, false);
                var fixture = new Fixture(root, runtime, run, presenter, display);
                if (caseNumber >= 5 && caseNumber <= 8)
                {
                    runtime.BoardState.EmitNoise(new EntityId(-10), Cell(caseNumber == 6 ? 5 : 4, 1, mirrored),
                        caseNumber == 6 ? 5 : 4, caseNumber == 8 ? 2 : 5);
                    if (caseNumber == 6)
                        runtime.BoardState.EmitNoise(new EntityId(-11), Cell(3, 3, mirrored), 2, 5);
                }
                if (caseNumber == 4 || caseNumber >= 5 && caseNumber <= 8)
                    Submit(fixture, new WaitCommand());
                for (int coordinate = 0; coordinate < 6; coordinate++)
                {
                    Label(root.transform, coordinate.ToString(), new Vector3(coordinate, -0.8f, -0.2f), 0.075f);
                    Label(root.transform, coordinate.ToString(), new Vector3(-0.8f, coordinate, -0.2f), 0.075f);
                }
                fixture.UpdateHud();
                return fixture;
            }
            catch
            {
                presenter?.Dispose();
                runtime?.Dispose();
                Object.DestroyImmediate(root);
                throw;
            }
        }

        private LegacyBoardView View(GameObject root, LegacyBoardContentKind kind, GridPosition position)
        {
            var instance = new GameObject(kind.ToString());
            instance.transform.SetParent(root.transform, false);
            instance.transform.position = new Vector3(position.X, position.Y, 0);
            SpriteRenderer renderer = instance.AddComponent<SpriteRenderer>();
            bool floor = kind == LegacyBoardContentKind.Floor;
            renderer.sprite = kind == LegacyBoardContentKind.Player ? playerSprite :
                kind == LegacyBoardContentKind.Enemy || kind == LegacyBoardContentKind.Listener ? enemySprite : tile;
            renderer.color = floor ? new Color(0.15f, 0.19f, 0.18f) : kind == LegacyBoardContentKind.Aid
                ? new Color(0.8f, 0.65f, 0.16f) : kind == LegacyBoardContentKind.Exit ? new Color(0.16f, 0.45f, 0.55f) : Color.white;
            renderer.sortingLayerName = floor ? "Floor" : "Units";
            renderer.sortingOrder = 0;
            instance.transform.localScale = Vector3.one * (floor ? 0.97f : kind == LegacyBoardContentKind.Aid ? 0.38f : 0.85f);
            if (!floor)
            {
                string symbol = kind == LegacyBoardContentKind.Player ? "P" : kind == LegacyBoardContentKind.Enemy ? "S" :
                    kind == LegacyBoardContentKind.Listener ? "L" : kind == LegacyBoardContentKind.Aid ? "F" : "E";
                Label(instance.transform, symbol, new Vector3(0.34f, -0.32f, -0.18f), 0.055f);
            }
            return new LegacyBoardView(instance, kind, position, kind == LegacyBoardContentKind.Enemy ? 10 : (int?)null);
        }

        private static Sprite ReadSprite(string path)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null, path);
            Sprite sprite = prefab.GetComponentsInChildren<SpriteRenderer>(true).Select(renderer => renderer.sprite)
                .FirstOrDefault(candidate => candidate != null);
            if (sprite == null)
            {
                Animator animator = prefab.GetComponentInChildren<Animator>(true);
                Assert.That(animator != null && animator.runtimeAnimatorController != null, Is.True, path + " animator");
                sprite = animator.runtimeAnimatorController.animationClips.SelectMany(clip =>
                    AnimationUtility.GetObjectReferenceCurveBindings(clip)
                        .Where(binding => binding.type == typeof(SpriteRenderer) && binding.propertyName == "m_Sprite")
                        .SelectMany(binding => AnimationUtility.GetObjectReferenceCurve(clip, binding)))
                    .Select(frame => frame.value as Sprite).FirstOrDefault(candidate => candidate != null);
            }
            Assert.That(sprite, Is.Not.Null, path + " sprite");
            return sprite;
        }

        private static TextMesh Label(Transform parent, string text, Vector3 position, float size)
        {
            var instance = new GameObject("Card label");
            instance.transform.SetParent(parent, false);
            instance.transform.localPosition = position;
            TextMesh label = instance.AddComponent<TextMesh>();
            label.text = text;
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 48;
            label.characterSize = size;
            label.anchor = TextAnchor.MiddleCenter;
            label.color = Color.white;
            MeshRenderer renderer = instance.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = label.font.material;
            renderer.sortingLayerName = "Units";
            renderer.sortingOrder = 21;
            return label;
        }

        private static TurnResult Submit(Fixture fixture, PlayerCommand command)
        {
            CommandSubmission result = fixture.Presenter.Coordinator.SubmitAsync(command).GetAwaiter().GetResult();
            Assert.That(result.Status, Is.EqualTo(CommandSubmissionStatus.Presented), "Capture must use complete production replay.");
            fixture.UpdateHud();
            return result.Result;
        }

        private static GridPosition Cell(int horizontal, int vertical, bool mirrored) =>
            mirrored ? new GridPosition(5 - horizontal, 5 - vertical) : new GridPosition(horizontal, vertical);

        private static PlayerCommand Command(int caseNumber, bool mirrored, bool noisyChoice, Fixture fixture)
        {
            if (caseNumber == 7 || caseNumber == 9 && noisyChoice)
                return new InteractCommand(fixture.Runtime.BoardState.GetEntities().Single(entity => entity.Definition.ContentId == "legacy.aid").Id);
            if (caseNumber == 1) return new MoveCommand(mirrored ? Direction.West : Direction.East);
            if (caseNumber >= 2 && caseNumber <= 4) return new MoveCommand(mirrored ? Direction.South : Direction.North);
            return new WaitCommand();
        }

        private static EnemyIntentKind ExpectedIntent(int caseNumber) => caseNumber <= 2 ? EnemyIntentKind.Move :
            caseNumber == 3 ? EnemyIntentKind.Attack : caseNumber == 4 || caseNumber == 9 ? EnemyIntentKind.Wait : EnemyIntentKind.Investigate;

        private static GridPosition ExpectedCell(int caseNumber, bool noisyChoice, bool mirrored)
        {
            if (caseNumber == 1 || caseNumber == 8 || caseNumber == 9 && !noisyChoice) return Cell(3, 1, mirrored);
            if (caseNumber >= 2 && caseNumber <= 4 || caseNumber == 9) return Cell(2, 1, mirrored);
            if (caseNumber == 6) return Cell(3, 2, mirrored);
            return Cell(4, 1, mirrored);
        }

        private static string ExpectedAction(int caseNumber, bool noisyChoice) => caseNumber == 1 ? "Hit" :
            caseNumber == 3 ? "Miss" : caseNumber == 4 || caseNumber == 8 ? "Wait" :
            caseNumber == 9 ? noisyChoice ? "Wait,Move" : "Wait,Wait" : "Move";

        private static string Outcome(GameEvent gameEvent) => gameEvent is EnemyAttackResolvedEvent attack ? attack.IsHit ? "Hit" : "Miss" :
            gameEvent is EntityMovedEvent ? "Move" : "Wait";

        private static string Prompt(int caseNumber, bool mirrored)
        {
            string direction = caseNumber == 1 ? mirrored ? "West" : "East" : mirrored ? "South" : "North";
            string command = caseNumber <= 4 ? "Move " + direction : caseNumber == 7 ? "Interact with F" : "Wait";
            string context = caseNumber == 6 ? "Emissions occurred in this order: " + Cell(5, 1, mirrored) + ", then " + Cell(3, 3, mirrored) + ". " : "";
            if (caseNumber == 9)
                return "Goal: bring L to " + Cell(2, 1, mirrored) + " within two accepted commands without entering that cell. " +
                    "Choose your first command: Wait or Interact with F. The second command is Wait. " +
                    "For your chosen sequence only, record the enemy action/cell after each of its two commands, HP change and your reason before either outcome is revealed. " +
                    "You do not need to predict the unchosen alternative.";
            return context + "Player command: " + command + ". Record the enemy action, actual enemy cell and HP change. " +
                (caseNumber >= 5 ? "Also record its announced immediate step and noise source, then the next announced investigation source (none for Wait). " : "") +
                "Explain your prediction before the outcome is revealed.";
        }

        private static void Capture(Fixture fixture, string path, bool checkGlyphs)
        {
            foreach (Transform child in fixture.Root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = CaptureLayer;
            var cameraObject = new GameObject("Isolated capture camera");
            cameraObject.transform.SetParent(fixture.Root.transform, false);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.cullingMask = 1 << CaptureLayer;
            camera.transform.position = new Vector3(2.5f, 2.5f, -15);
            camera.orthographic = true;
            camera.orthographicSize = 4.05f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.035f, 0.045f, 0.04f);
            var target = new RenderTexture(ImageSize, ImageSize, 24);
            var pixels = new Texture2D(ImageSize, ImageSize, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, ImageSize, ImageSize), 0, 0);
                pixels.Apply();
                Color32[] withGlyphs = pixels.GetPixels32();
                byte[] image = pixels.EncodeToPNG();
                if (checkGlyphs)
                {
                    fixture.Display.gameObject.SetActive(false);
                    camera.Render();
                    pixels.ReadPixels(new Rect(0, 0, ImageSize, ImageSize), 0, 0);
                    pixels.Apply();
                    Color32[] withoutGlyphs = pixels.GetPixels32();
                    int changed = 0;
                    for (int pixelIndex = 0; pixelIndex < withGlyphs.Length; pixelIndex++)
                        if (Math.Abs(withGlyphs[pixelIndex].r - withoutGlyphs[pixelIndex].r) +
                            Math.Abs(withGlyphs[pixelIndex].g - withoutGlyphs[pixelIndex].g) +
                            Math.Abs(withGlyphs[pixelIndex].b - withoutGlyphs[pixelIndex].b) > 60) changed++;
                    Assert.That(changed, Is.GreaterThan(25), "The production intent, not only board artwork, must be visible.");
                }
                File.WriteAllBytes(path, image);
            }
            finally
            {
                fixture.Display.gameObject.SetActive(true);
                camera.targetTexture = null;
                RenderTexture.active = previous;
                Object.DestroyImmediate(pixels);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(cameraObject);
            }
        }

        private sealed class Fixture : IDisposable
        {
            public readonly GameObject Root;
            public readonly BoardRuntime Runtime;
            public readonly RunState Run;
            public readonly BoardEventPresenter Presenter;
            public readonly EnemyIntentPresenter Display;
            public readonly EntityId EnemyId;
            private TextMesh hud;
            public EnemyIntent Intent => Runtime.Listeners.Count == 0 ? Runtime.Enemies.Single().LockedIntent : Runtime.Listeners.Single().LockedIntent;

            public Fixture(GameObject root, BoardRuntime runtime, RunState run, BoardEventPresenter presenter,
                EnemyIntentPresenter display)
            {
                Root = root;
                Runtime = runtime;
                Run = run;
                Presenter = presenter;
                Display = display;
                EnemyId = runtime.Listeners.Count == 0 ? runtime.Enemies.Single().ActorId : runtime.Listeners.Single().ActorId;
            }

            public void UpdateHud()
            {
                if (hud == null) hud = Label(Root.transform, "", new Vector3(2.5f, 5.95f, -0.2f), 0.085f);
                hud.text = "HP " + Run.Health + "   Food " + Run.Food;
            }

            public void Dispose()
            {
                Root.SetActive(false);
                Presenter.Dispose();
                Runtime.Dispose();
                Object.DestroyImmediate(Root);
            }
        }

        private sealed class Sinks : IRunHud, ITurnFeedback, IBoardOutcomeSink
        {
            public void Refresh(int health, int food) { }
            public void ShowRejection(CommandRejectionCode reason) { }
            public void ShowEvent(GameEvent gameEvent) { }
            public bool TryNotify(BoardRequest request, GameEvent outcome) => throw new InvalidOperationException("A gate card must not end a board.");
        }
    }
}
#endif