using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.Session;
using HallowBlaze.Core.State;
using HallowBlaze.Core.Turns.Contracts;
using HallowBlaze.Core.Turns.Resolution;
using HallowBlaze.Presentation.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using EntityId = HallowBlaze.Core.Board.Primitives.EntityId;

namespace HallowBlaze.Tests.PlayMode
{
    /// <summary>Verifies persistent intent geometry using transient objects, never production scenes.</summary>
    public sealed class EnemyIntentPresentationTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        private readonly List<BoardRuntime> runtimes = new List<BoardRuntime>();
        private readonly List<EnemyIntentLegend> legends = new List<EnemyIntentLegend>();

        /// <summary>Releases all transient views and their owned materials after each isolated fixture.</summary>
        [TearDown]
        public void TearDown()
        {
            foreach (EnemyIntentLegend legend in legends)
                legend.Dispose();
            legends.Clear();
            foreach (BoardRuntime runtime in runtimes)
                runtime.Dispose();
            runtimes.Clear();
            foreach (GameObject instance in objects)
                if (instance != null)
                    UnityEngine.Object.DestroyImmediate(instance);
            objects.Clear();
        }

        /// <summary>
        /// Five action shapes differ in active geometry despite identical colors, and remain
        /// persistent with the behavior disabled and fixed layout dimensions across frames.
        /// </summary>
        [UnityTest]
        public IEnumerator Symbols_AreDistinctWithoutColorOrAnimation()
        {
            var instance = new GameObject("Intent symbol fixture");
            objects.Add(instance);
            EnemyIntentView view = instance.AddComponent<EnemyIntentView>();
            Bounds layout = view.LocalBounds;
            Vector3 scale = instance.transform.localScale;
            var signatures = new HashSet<string>();
            var actorId = new EntityId(0);
            var playerId = new EntityId(-1);
            var source = new GridPosition(3, 3);
            var target = new GridPosition(3, 4);
            foreach (EnemyIntentSymbol symbol in new[] { EnemyIntentSymbol.Move, EnemyIntentSymbol.ConditionalMove,
                EnemyIntentSymbol.Attack, EnemyIntentSymbol.Investigate, EnemyIntentSymbol.Wait })
            {
                EnemyIntentProjection projection = EnemyIntentProjection.CreateExample(actorId, source, symbol,
                    symbol == EnemyIntentSymbol.Wait ? (GridPosition?)null : target,
                    symbol == EnemyIntentSymbol.Attack || symbol == EnemyIntentSymbol.ConditionalMove ? (EntityId?)playerId : null);

                view.Show(projection);

                LineRenderer[] lines = ActiveGlyphLines(view);
                Assert.That(lines.Length, Is.EqualTo(symbol == EnemyIntentSymbol.ConditionalMove ? 4 : 2), symbol.ToString());
                Assert.That(view.Projection, Is.SameAs(projection));
                Assert.That(view.LocalBounds, Is.EqualTo(layout));
                Assert.That(instance.transform.localScale, Is.EqualTo(scale));
                GridPosition displayedCell = projection.TargetPosition ?? source;
                Assert.That(instance.transform.position, Is.EqualTo(new Vector3(displayedCell.X, displayedCell.Y, EnemyIntentView.MarkerDepth)));
                foreach (LineRenderer line in lines)
                {
                    Assert.That(line.enabled && line.gameObject.activeInHierarchy, Is.True);
                    Assert.That(line.sharedMaterial, Is.Not.Null);
                    Assert.That(line.sharedMaterial.shader.name, Is.EqualTo("Sprites/Default"));
                    Assert.That(line.startColor, Is.EqualTo(Color.white));
                    Assert.That(line.endColor, Is.EqualTo(Color.white));
                    Assert.That(line.widthMultiplier, Is.EqualTo(EnemyIntentView.StrokeWidth));
                    Assert.That(line.sortingOrder, Is.EqualTo(EnemyIntentView.GlyphSortingOrder));
                    Assert.That(line.positionCount, Is.GreaterThanOrEqualTo(2));
                    for (int index = 0; index < line.positionCount; index++)
                    {
                        Vector3 point = view.transform.Find("Glyph").InverseTransformPoint(line.transform.TransformPoint(line.GetPosition(index)));
                        Assert.That(Mathf.Abs(point.x) + EnemyIntentView.StrokeWidth / 2f, Is.LessThan(layout.extents.x));
                        Assert.That(Mathf.Abs(point.y) + EnemyIntentView.StrokeWidth / 2f, Is.LessThan(layout.extents.y));
                    }
                    Assert.That(Enumerable.Range(0, line.positionCount).Select(line.GetPosition).Distinct().Count(),
                        Is.GreaterThanOrEqualTo(2));
                }
                GameObject badge = view.GetComponentsInChildren<Transform>(true)
                    .Single(item => item.name == "AttackOnPlayerEntry").gameObject;
                Assert.That(badge.activeInHierarchy, Is.EqualTo(symbol == EnemyIntentSymbol.ConditionalMove));
                if (symbol == EnemyIntentSymbol.ConditionalMove)
                    Assert.That(badge.GetComponentsInChildren<LineRenderer>().Length, Is.EqualTo(2));
                string signature = GeometrySignature(view);
                Assert.That(signatures.Add(signature), Is.True, $"Geometry must distinguish {symbol} without color.");
                view.enabled = false;
                Assert.That(view.GetComponentsInChildren<Animator>(), Is.Empty);

                yield return null;
                yield return null;

                Assert.That(GeometrySignature(view), Is.EqualTo(signature));
                Assert.That(ActiveGlyphLines(view).All(line => line.enabled && line.gameObject.activeInHierarchy), Is.True);
                Assert.That(view.LocalBounds, Is.EqualTo(layout));
            }
            EnemyIntentProjection previous = view.Projection;
            string previousGeometry = GeometrySignature(view);
            Assert.Throws<ArgumentNullException>(() => view.Show(null));
            Assert.That(view.Projection, Is.SameAs(previous));
            Assert.That(GeometrySignature(view), Is.EqualTo(previousGeometry));
            Assert.That(signatures.Count, Is.EqualTo(5));
        }

        /// <summary>Recorded cardinal/edge targets and source-only wait stay fixed despite unrelated visual or player movement.</summary>
        [TestCase(3, 3, 3, 4, false)]
        [TestCase(3, 3, 3, 2, false)]
        [TestCase(3, 3, 4, 3, false)]
        [TestCase(3, 3, 2, 3, false)]
        [TestCase(1, 0, 0, 0, false)]
        [TestCase(6, 0, 7, 0, false)]
        [TestCase(0, 6, 0, 7, false)]
        [TestCase(7, 6, 7, 7, false)]
        [TestCase(0, 0, 0, 0, true)]
        [TestCase(7, 7, 7, 7, true)]
        public void TargetMarker_UsesRecordedGridCell(int sourceX, int sourceY, int targetX, int targetY, bool wait)
        {
            var actorId = new EntityId(0);
            var playerId = new EntityId(-1);
            var source = new GridPosition(sourceX, sourceY);
            var target = new GridPosition(targetX, targetY);
            var board = new BoardState();
            var enemyDefinition = new BoardEntityDefinition(BoardLayer.Actor, EntityKind.Enemy, "marker.enemy", BoardEntityTraits.Default);
            var playerDefinition = new BoardEntityDefinition(BoardLayer.Actor, EntityKind.Player, "marker.player", BoardEntityTraits.Default);
            Assert.That(board.TryAdd(new BoardEntityState(actorId, enemyDefinition, source)), Is.True);
            Assert.That(board.TryAdd(new BoardEntityState(playerId, playerDefinition, new GridPosition(5, 5))), Is.True);
            var before = board.GetEntities();
            var instance = new GameObject("Recorded target fixture");
            var enemyVisual = new GameObject("Interpolating enemy");
            var playerVisual = new GameObject("Moving player");
            var cameraObject = new GameObject("Eight-cell board camera");
            objects.AddRange(new[] { instance, enemyVisual, playerVisual, cameraObject });
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.orthographicSize = 4;
            camera.aspect = 1;
            camera.transform.position = new Vector3(3.5f, 3.5f, -10);
            EnemyIntentView view = instance.AddComponent<EnemyIntentView>();
            EnemyIntentProjection projection = EnemyIntentProjection.CreateExample(actorId, source,
                wait ? EnemyIntentSymbol.Wait : EnemyIntentSymbol.ConditionalMove,
                wait ? (GridPosition?)null : target, wait ? (EntityId?)null : playerId);

            view.Show(projection);

            Vector3 expectedTarget = new Vector3(targetX, targetY, EnemyIntentView.MarkerDepth);
            Assert.That(view.transform.position, Is.EqualTo(expectedTarget));
            Assert.That(view.Projection, Is.SameAs(projection));
            Transform glyph = view.transform.Find("Glyph");
            Assert.That(glyph.position, Is.EqualTo(expectedTarget));
            LineRenderer cue = view.transform.Find("SourceCue").GetComponent<LineRenderer>();
            LineRenderer link = view.transform.Find("SourceLink").GetComponent<LineRenderer>();
            Assert.That(cue.enabled, Is.EqualTo(!wait));
            Assert.That(link.enabled, Is.EqualTo(!wait));
            if (!wait)
            {
                Vector3 expectedSource = new Vector3(sourceX, sourceY, EnemyIntentView.MarkerDepth);
                Assert.That(cue.transform.position, Is.EqualTo(expectedSource));
                Assert.That(link.transform.TransformPoint(link.GetPosition(0)), Is.EqualTo(expectedSource));
                Assert.That(Vector3.Distance(link.transform.TransformPoint(link.GetPosition(1)), expectedTarget),
                    Is.EqualTo(EnemyIntentView.MarkerSize / 2f + EnemyIntentView.StrokeWidth).Within(0.0001f));
                Assert.That(Vector3.Dot(glyph.up, (expectedTarget - expectedSource).normalized), Is.EqualTo(1).Within(0.0001f));
            }
            foreach (LineRenderer line in view.GetComponentsInChildren<LineRenderer>().Where(item => item.enabled))
            {
                Assert.That(line.gameObject.activeInHierarchy, Is.True);
                for (int index = 0; index < line.positionCount; index++)
                {
                    Vector3 viewport = camera.WorldToViewportPoint(line.transform.TransformPoint(line.GetPosition(index)));
                    float margin = EnemyIntentView.StrokeWidth / 16f;
                    Assert.That(viewport.x, Is.InRange(margin, 1 - margin));
                    Assert.That(viewport.y, Is.InRange(margin, 1 - margin));
                    Assert.That(viewport.z, Is.InRange(camera.nearClipPlane, camera.farClipPlane));
                }
            }
            CollectionAssert.AreEqual(before, board.GetEntities());
            string geometry = GeometrySignature(view);
            enemyVisual.transform.position = new Vector3(sourceX + 0.35f, sourceY + 0.2f, 3);
            playerVisual.transform.position = new Vector3(6, 5, 3);
            Assert.That(view.transform.position, Is.EqualTo(expectedTarget));
            CollectionAssert.AreEqual(before, board.GetEntities());
            Assert.That(board.TryMove(playerId, new GridPosition(6, 5)), Is.True);
            Assert.That(view.Projection.TargetPosition, Is.EqualTo(wait ? (GridPosition?)null : target));
            Assert.That(view.transform.position, Is.EqualTo(expectedTarget));
            Assert.That(GeometrySignature(view), Is.EqualTo(geometry));
            Assert.That(board.TryGetEntity(actorId, out BoardEntityState actor), Is.True);
            Assert.That(actor.Position, Is.EqualTo(source));
        }

        /// <summary>Adjacent contenders keep disjoint glyphs and exact source links across every input permutation.</summary>
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public void SharedTarget_PreservesSourceAssociations(int contenderCount)
        {
            var target = new GridPosition(3, 3);
            var sourceCells = new[] { new GridPosition(3, 4), new GridPosition(4, 3), new GridPosition(3, 2), new GridPosition(2, 3) };
            var ids = new[] { new EntityId(7), new EntityId(-5), new EntityId(0), new EntityId(11) };
            var offsets = new[] { new Vector3(0.23f, 0.23f), new Vector3(0.23f, -0.23f),
                new Vector3(-0.23f, -0.23f), new Vector3(-0.23f, 0.23f) };
            var projections = Enumerable.Range(0, contenderCount).Select(index => EnemyIntentProjection.CreateExample(
                ids[index], sourceCells[index], EnemyIntentSymbol.ConditionalMove, target, new EntityId(-100))).ToList();
            EnemyIntentProjection unrelated = EnemyIntentProjection.CreateExample(new EntityId(9), new GridPosition(6, 6),
                EnemyIntentSymbol.Move, new GridPosition(6, 7));
            projections.Add(unrelated);
            var views = new Dictionary<EntityId, EnemyIntentView>();
            var recordedGeometry = new Dictionary<EntityId, Vector3[]>();
            foreach (EnemyIntentProjection projection in projections)
            {
                var instance = new GameObject("Source association " + projection.SourceId);
                objects.Add(instance);
                views.Add(projection.SourceId, instance.AddComponent<EnemyIntentView>());
            }
            int permutationCount = 0;
            foreach (EnemyIntentProjection[] permutation in ProjectionPermutations(projections.ToArray()))
            {
                string context = "Contenders=" + contenderCount + "; IDs=[" + string.Join(",", permutation.Select(item => item.SourceId)) + "]";
                foreach (EnemyIntentProjection projection in permutation)
                    views[projection.SourceId].Show(projection, !ReferenceEquals(projection, unrelated));

                var occupiedBounds = new List<Bounds>();
                foreach (EnemyIntentProjection projection in projections)
                {
                    EnemyIntentView view = views[projection.SourceId];
                    bool shared = !ReferenceEquals(projection, unrelated);
                    GridPosition cell = projection.TargetPosition.Value;
                    Assert.That(view.Projection, Is.SameAs(projection), context);
                    Assert.That(view.transform.position, Is.EqualTo(new Vector3(cell.X, cell.Y, EnemyIntentView.MarkerDepth)), context);
                    Transform glyph = view.transform.Find("Glyph");
                    int sourceIndex = Array.IndexOf(ids, projection.SourceId);
                    Assert.That(glyph.localPosition, Is.EqualTo(shared ? offsets[sourceIndex] : Vector3.zero), context);
                    Assert.That(glyph.localScale, Is.EqualTo(Vector3.one * (shared ? 0.5f : 1)), context);
                    Assert.That(view.LocalBounds.size, Is.EqualTo(new Vector3(0.6f, 0.6f, 0.1f)), context);
                    Vector3 expectedSource = new Vector3(projection.SourcePosition.X, projection.SourcePosition.Y, EnemyIntentView.MarkerDepth);
                    Assert.That(view.transform.Find("SourceCue").position, Is.EqualTo(expectedSource), context);
                    LineRenderer link = view.transform.Find("SourceLink").GetComponent<LineRenderer>();
                    Assert.That(link.enabled, Is.True, context);
                    Assert.That(link.transform.TransformPoint(link.GetPosition(0)), Is.EqualTo(expectedSource), context);
                    Assert.That(Vector3.Distance(link.transform.TransformPoint(link.GetPosition(1)), glyph.position),
                        Is.EqualTo(shared ? 0.19f : 0.34f).Within(0.0001f), context);
                    LineRenderer[] lines = ActiveGlyphLines(view);
                    Assert.That(lines.Length, Is.EqualTo(shared ? 4 : 2), context);
                    Assert.That(lines.All(line => line.enabled && line.gameObject.activeInHierarchy &&
                        line.startColor == Color.white && line.endColor == Color.white), Is.True, context);
                    Vector3[] points = lines.SelectMany(line => Enumerable.Range(0, line.positionCount)
                        .Select(index => line.transform.TransformPoint(line.GetPosition(index)))).ToArray();
                    var bounds = new Bounds(points[0], Vector3.zero);
                    foreach (Vector3 point in points)
                        bounds.Encapsulate(point);
                    bounds.Expand(EnemyIntentView.StrokeWidth);
                    Assert.That(bounds.min.x, Is.GreaterThan(cell.X - 0.5f), context);
                    Assert.That(bounds.max.x, Is.LessThan(cell.X + 0.5f), context);
                    Assert.That(bounds.min.y, Is.GreaterThan(cell.Y - 0.5f), context);
                    Assert.That(bounds.max.y, Is.LessThan(cell.Y + 0.5f), context);
                    Assert.That(occupiedBounds.All(previous => !previous.Intersects(bounds)), Is.True, context);
                    occupiedBounds.Add(bounds);
                    if (recordedGeometry.TryGetValue(projection.SourceId, out Vector3[] previousPoints))
                        CollectionAssert.AreEqual(previousPoints, points, context);
                    else
                        recordedGeometry.Add(projection.SourceId, points);
                }
                permutationCount++;
            }
            Assert.That(permutationCount, Is.EqualTo(contenderCount == 2 ? 6 : contenderCount == 3 ? 24 : 120));
            EnemyIntentView retained = views[ids[0]];
            EnemyIntentProjection previousProjection = retained.Projection;
            Vector3 previousPosition = retained.transform.position;
            string previousGeometry = GeometrySignature(retained);
            Assert.Throws<ArgumentException>(() => retained.Show(EnemyIntentProjection.CreateExample(ids[0], sourceCells[0], EnemyIntentSymbol.Wait), true));
            Assert.Throws<ArgumentException>(() => retained.Show(EnemyIntentProjection.CreateExample(ids[0], new GridPosition(4, 4),
                EnemyIntentSymbol.Move, target), true));
            Assert.That(retained.Projection, Is.SameAs(previousProjection));
            Assert.That(retained.transform.position, Is.EqualTo(previousPosition));
            Assert.That(GeometrySignature(retained), Is.EqualTo(previousGeometry));
            retained.Show(previousProjection);
            Assert.That(retained.transform.Find("Glyph").localPosition, Is.EqualTo(Vector3.zero));
            Assert.That(retained.transform.Find("Glyph").localScale, Is.EqualTo(Vector3.one));
            TestContext.WriteLine("Contenders=" + contenderCount + "; permutations=" + permutationCount);
        }

        /// <summary>Only complete explicit publications replace the display; malformed batches preserve every previous marker.</summary>
        [TestCase("initial")]
        [TestCase("replacement")]
        [TestCase("duplicate")]
        [TestCase("unplanned")]
        [TestCase("missing-source")]
        [TestCase("null-entry")]
        [TestCase("invalid-shared")]
        [TestCase("null-input")]
        public void Publication_ReplacesOnlyCompleteDetachedBatch(string scenario)
        {
            EnemyIntentPresenter presenter = CreatePublisherFixture(out BoardState board, out ShamblerState[] enemies);
            var input = enemies.Reverse().ToList();
            var entitiesBefore = board.GetEntities();
            EnemyIntent[] intentsBefore = enemies.Select(enemy => enemy.LockedIntent).ToArray();

            presenter.Publish(board, input);

            CollectionAssert.AreEqual(entitiesBefore, board.GetEntities());
            CollectionAssert.AreEqual(intentsBefore, enemies.Select(enemy => enemy.LockedIntent));
            Assert.That(enemies.All(enemy => enemy.Phase == ShamblerPhase.Active), Is.True);
            CollectionAssert.AreEqual(new[] { new EntityId(-5), new EntityId(0), new EntityId(7) }, presenter.Projections.Select(item => item.SourceId));
            Assert.That(presenter.Views.Count, Is.EqualTo(3));
            Assert.That(presenter.GetComponentsInChildren<EnemyIntentView>().Length, Is.EqualTo(3));
            IReadOnlyList<EnemyIntentProjection> previousProjections = presenter.Projections;
            IReadOnlyDictionary<EntityId, EnemyIntentView> previousViews = presenter.Views;
            foreach (EnemyIntentProjection projection in previousProjections)
            {
                Assert.That(previousViews[projection.SourceId].Projection, Is.SameAs(projection));
                Assert.That(previousViews[projection.SourceId].gameObject.activeInHierarchy, Is.True);
                if (projection.Symbol == EnemyIntentSymbol.ConditionalMove)
                {
                    Assert.That(projection.TargetPosition, Is.EqualTo(new GridPosition(3, 3)));
                    Assert.That(projection.AttackOnPlayerEntry, Is.True);
                }
            }
            input.Clear();
            enemies[0].ConsumeIntent();
            enemies[0].LockIntent(new EnemyIntent(enemies[0].ActorId, EnemyIntentKind.Wait));
            Assert.That(board.TryMove(enemies[0].ActorId, new GridPosition(2, 4)), Is.True);
            Assert.That(presenter.Projections, Is.SameAs(previousProjections));
            Assert.That(previousProjections[0].SourcePosition, Is.EqualTo(new GridPosition(2, 3)));
            Assert.That(previousViews[enemies[0].ActorId].Projection.Symbol, Is.EqualTo(EnemyIntentSymbol.ConditionalMove));
            if (scenario == "initial")
                return;
            if (scenario == "replacement")
            {
                var beforeReplacement = board.GetEntities();
                ShamblerPhase[] phases = enemies.Select(enemy => enemy.Phase).ToArray();
                EnemyIntent[] locks = enemies.Select(enemy => enemy.LockedIntent).ToArray();

                presenter.Publish(board, enemies);

                Assert.That(presenter.Projections, Is.Not.SameAs(previousProjections));
                Assert.That(presenter.Projections[0].SourcePosition, Is.EqualTo(new GridPosition(2, 4)));
                Assert.That(presenter.Projections[0].Symbol, Is.EqualTo(EnemyIntentSymbol.Wait));
                Assert.That(presenter.Views.Count, Is.EqualTo(3));
                Assert.That(presenter.GetComponentsInChildren<EnemyIntentView>().Length, Is.EqualTo(3));
                Assert.That(previousViews.Values.All(view => !view.gameObject.activeInHierarchy), Is.True);
                Assert.That(previousProjections[0].Symbol, Is.EqualTo(EnemyIntentSymbol.ConditionalMove));
                CollectionAssert.AreEqual(beforeReplacement, board.GetEntities());
                CollectionAssert.AreEqual(phases, enemies.Select(enemy => enemy.Phase));
                CollectionAssert.AreEqual(locks, enemies.Select(enemy => enemy.LockedIntent));
                return;
            }
            IEnumerable<ShamblerState> malformed = enemies;
            switch (scenario)
            {
                case "duplicate": malformed = enemies.Concat(new[] { enemies[0] }); break;
                case "unplanned": enemies[1].ConsumeIntent(); break;
                case "missing-source": Assert.That(board.TryRemove(enemies[1].ActorId), Is.True); break;
                case "null-entry": malformed = enemies.Concat(new ShamblerState[] { null }); break;
                case "invalid-shared":
                    Assert.That(board.TryMove(enemies[0].ActorId, new GridPosition(6, 4)), Is.True);
                    enemies[0].ConsumeIntent();
                    enemies[0].LockIntent(new EnemyIntent(enemies[0].ActorId, EnemyIntentKind.Move, new GridPosition(3, 3), new EntityId(-100), true));
                    break;
                case "null-input": malformed = null; break;
                default: throw new ArgumentOutOfRangeException(nameof(scenario));
            }
            var invalidBefore = board.GetEntities();
            EnemyIntent[] invalidLocks = enemies.Select(enemy => enemy.LockedIntent).ToArray();
            ShamblerPhase[] invalidPhases = enemies.Select(enemy => enemy.Phase).ToArray();
            Assert.That(() => presenter.Publish(board, malformed), Throws.InstanceOf<ArgumentException>());
            Assert.That(presenter.Projections, Is.SameAs(previousProjections));
            Assert.That(presenter.Views, Is.SameAs(previousViews));
            Assert.That(previousViews.Values.All(view => view.gameObject.activeInHierarchy), Is.True);
            Assert.That(presenter.GetComponentsInChildren<EnemyIntentView>().Length, Is.EqualTo(3));
            CollectionAssert.AreEqual(invalidBefore, board.GetEntities());
            CollectionAssert.AreEqual(invalidLocks, enemies.Select(enemy => enemy.LockedIntent));
            CollectionAssert.AreEqual(invalidPhases, enemies.Select(enemy => enemy.Phase));
            Assert.Throws<ArgumentNullException>(() => presenter.Publish(null, enemies));
            Assert.That(presenter.Projections, Is.SameAs(previousProjections));
        }

        /// <summary>Failed preparation after a hidden glyph was created releases its material without touching the previous display.</summary>
        [UnityTest]
        public IEnumerator Publication_ReplacesOnlyCompleteDetachedBatch_AfterPartialGeometryFailure()
        {
            EnemyIntentPresenter presenter = CreatePublisherFixture(out BoardState board, out ShamblerState[] enemies);
            presenter.Publish(board, enemies);
            IReadOnlyDictionary<EntityId, EnemyIntentView> previousViews = presenter.Views;
            var previousMaterials = new HashSet<Material>(Resources.FindObjectsOfTypeAll<Material>());
            Material[] borrowedDisplayMaterials = previousViews.Values.Select(view =>
                ActiveGlyphLines(view)[0].sharedMaterial).ToArray();
            Assert.That(board.TryMove(enemies[1].ActorId, new GridPosition(6, 5)), Is.True);

            Assert.Throws<ArgumentException>(() => presenter.Publish(board, enemies));

            Material[] preparedMaterials = Resources.FindObjectsOfTypeAll<Material>()
                .Where(material => material.name == "Intent glyph material" && !previousMaterials.Contains(material)).ToArray();
            Assert.That(preparedMaterials.Length, Is.EqualTo(1));
            Assert.That(presenter.Views, Is.SameAs(previousViews));
            Assert.That(previousViews.Values.All(view => view.gameObject.activeInHierarchy), Is.True);

            yield return null;
            yield return null;

            Assert.That(preparedMaterials.All(material => material == null), Is.True);
            Assert.That(borrowedDisplayMaterials.All(material => material != null), Is.True);
            Assert.That(presenter.GetComponentsInChildren<EnemyIntentView>().Length, Is.EqualTo(3));
        }

        /// <summary>Removal, clear, terminal disposal and board replacement never resurrect old markers or destroy borrowed state/views.</summary>
        [UnityTest]
        public IEnumerator BoardLifetime_ClearsAndRejectsStalePublication()
        {
            BoardRuntime first = CreateIntentRuntime(out ShamblerState enemy);
            BoardRuntime activeRuntime = first;
            var owner = new GameObject("First board intent owner");
            objects.Add(owner);
            EnemyIntentPresenter presenter = owner.AddComponent<EnemyIntentPresenter>();
            presenter.Bind(first.BoardState, () => activeRuntime == null || activeRuntime.IsDisposed ? null : activeRuntime.BoardState);
            Assert.Throws<InvalidOperationException>(() => presenter.Bind(first.BoardState, () => first.BoardState));
            var borrowedViews = first.Views.Values.ToArray();
            var controller = first.Controller;
            int health = first.RunState.Health;
            int food = first.RunState.Food;
            EnemyIntent locked = enemy.LockedIntent;
            presenter.Publish(first.BoardState, new[] { enemy });
            EnemyIntentView removedMarker = presenter.Views[enemy.ActorId];
            Material removedMaterial = ActiveGlyphLines(removedMarker)[0].sharedMaterial;
            Assert.That(first.BoardState.TryGetEntity(enemy.ActorId, out BoardEntityState source), Is.True);
            Assert.That(first.BoardState.TryRemove(enemy.ActorId), Is.True);

            presenter.Publish(first.BoardState, Array.Empty<ShamblerState>());

            Assert.That(presenter.Views, Is.Empty);
            Assert.That(presenter.Projections, Is.Empty);
            Assert.That(removedMarker.gameObject.activeInHierarchy, Is.False);
            yield return null;
            Assert.That(removedMarker == null, Is.True);
            Assert.That(removedMaterial == null, Is.True);
            Assert.That(first.BoardState.TryAdd(source), Is.True);
            presenter.Publish(first.BoardState, new[] { enemy });
            presenter.Clear();
            presenter.Clear();
            Assert.That(presenter.Views, Is.Empty);
            Assert.That(presenter.IsDisposed, Is.False);
            presenter.Publish(first.BoardState, new[] { enemy });
            EnemyIntentView staleMarker = presenter.Views[enemy.ActorId];
            var firstSnapshot = first.BoardState.GetEntities();

            BoardRuntime second = CreateIntentRuntime(out ShamblerState newEnemy);
            activeRuntime = second;
            var newOwner = new GameObject("Second board intent owner");
            objects.Add(newOwner);
            EnemyIntentPresenter replacement = newOwner.AddComponent<EnemyIntentPresenter>();
            replacement.Bind(second.BoardState, () => activeRuntime == null || activeRuntime.IsDisposed ? null : activeRuntime.BoardState);
            var secondSnapshot = second.BoardState.GetEntities();
            var newBorrowedViews = second.Views.Values.ToArray();
            replacement.Publish(second.BoardState, new[] { newEnemy });
            Assert.That(newEnemy.ActorId, Is.EqualTo(enemy.ActorId));
            EnemyIntentView newMarker = replacement.Views[newEnemy.ActorId];
            Assert.That(newMarker, Is.Not.SameAs(staleMarker));

            Assert.Throws<InvalidOperationException>(() => presenter.Publish(first.BoardState, new[] { enemy }));

            Assert.That(presenter.IsDisposed, Is.True);
            Assert.That(presenter.Views, Is.Empty);
            Assert.That(staleMarker.gameObject.activeInHierarchy, Is.False);
            Assert.Throws<ObjectDisposedException>(() => presenter.Publish(second.BoardState, new[] { newEnemy }));
            Assert.Throws<ObjectDisposedException>(() => presenter.Bind(second.BoardState, () => second.BoardState));
            Assert.That(newMarker.gameObject.activeInHierarchy, Is.True);
            replacement.Dispose();
            replacement.Dispose();
            replacement.Clear();
            Assert.That(replacement.Views, Is.Empty);
            Assert.Throws<ObjectDisposedException>(() => replacement.Publish(second.BoardState, new[] { newEnemy }));
            Assert.That(newMarker.gameObject.activeInHierarchy, Is.False);

            EnemyIntentPresenter destroyed = newOwner.AddComponent<EnemyIntentPresenter>();
            destroyed.Bind(second.BoardState, () => second.BoardState);
            destroyed.Publish(second.BoardState, new[] { newEnemy });
            EnemyIntentView destroyedMarker = destroyed.Views[newEnemy.ActorId];
            Material destroyedMaterial = ActiveGlyphLines(destroyedMarker)[0].sharedMaterial;
            UnityEngine.Object.Destroy(destroyed);
            yield return null;
            yield return null;
            Assert.That(destroyedMarker == null && destroyedMaterial == null, Is.True);
            Assert.That(newOwner != null, Is.True);
            Assert.That(borrowedViews.Concat(newBorrowedViews).All(instance => instance != null && instance.activeInHierarchy), Is.True);
            Assert.That(first.IsDisposed || second.IsDisposed, Is.False);
            Assert.That(first.Controller, Is.SameAs(controller));
            Assert.That(first.RunState.Health, Is.EqualTo(health));
            Assert.That(first.RunState.Food, Is.EqualTo(food));
            Assert.That(second.RunState.Health, Is.EqualTo(100));
            Assert.That(second.RunState.Food, Is.EqualTo(25));
            CollectionAssert.AreEqual(firstSnapshot, first.BoardState.GetEntities());
            CollectionAssert.AreEqual(secondSnapshot, second.BoardState.GetEntities());
            Assert.That(enemy.LockedIntent, Is.SameAs(locked));
            Assert.That(enemy.Phase, Is.EqualTo(ShamblerPhase.Active));
            Assert.That(newEnemy.Phase, Is.EqualTo(ShamblerPhase.Active));
        }

        /// <summary>Desktop/narrow legends retain actual renderer meshes and readable labels without covering inspected cells or submitting commands.</summary>
        [UnityTest]
        public IEnumerator Legend_MatchesSymbolsWithoutSubmittingCommands()
        {
            BoardRuntime runtime = CreateIntentRuntime(out ShamblerState enemy);
            var before = runtime.BoardState.GetEntities();
            EnemyIntent locked = enemy.LockedIntent;
            var owner = new GameObject("Legend lifetime owner");
            objects.Add(owner);
            foreach (Vector2 size in new[] { new Vector2(1280, 720), new Vector2(360, 640) })
            {
                Rect inspected = size.x > 800 ? new Rect(20, 20, 860, 680) : new Rect(12, 480, 336, 148);
                var legend = new EnemyIntentLegend(owner.transform, size, inspected);
                legends.Add(legend);
                Assert.That(legend.IsVisible, Is.False);
                Assert.That(legend.Control.interactable && legend.Control.gameObject.activeInHierarchy, Is.True);
                Assert.That(legend.Control.GetComponentInChildren<Text>().text, Is.EqualTo("Enemy intents"));
                Assert.That(legend.Control.navigation.mode, Is.Not.EqualTo(Navigation.Mode.None));
                legend.Control.onClick.Invoke();
                legend.Show();
                legend.Resize(new Vector2(320, 640), new Rect(12, 480, 296, 148));
                legend.Resize(size, inspected);
                Canvas.ForceUpdateCanvases();
                Assert.That(legend.IsVisible, Is.True);
                Assert.That(legend.PanelBounds.Overlaps(inspected) || legend.ControlBounds.Overlaps(inspected), Is.False);
                Assert.That(legend.PanelBounds.xMin, Is.GreaterThanOrEqualTo(0));
                Assert.That(legend.PanelBounds.yMax, Is.LessThanOrEqualTo(size.y));
                Assert.That(legend.PanelBounds.xMax, Is.LessThanOrEqualTo(size.x));
                var signatures = new HashSet<string>();
                foreach (var glyph in legend.Glyphs)
                {
                    Mesh mesh = glyph.Value.GlyphMesh;
                    Assert.That(glyph.Value.gameObject.activeInHierarchy, Is.True);
                    Assert.That(mesh.vertexCount, Is.GreaterThan(0));
                    Assert.That(mesh.triangles.Length, Is.GreaterThan(0));
                    Assert.That(signatures.Add(string.Join(";", mesh.vertices.Select(vertex => vertex.ToString("F4")))), Is.True);
                    foreach (Vector3 vertex in mesh.vertices)
                    {
                        Assert.That(Mathf.Abs(vertex.x), Is.LessThanOrEqualTo(24));
                        Assert.That(Mathf.Abs(vertex.y), Is.LessThanOrEqualTo(24));
                    }
                }
                Assert.That(signatures.Count, Is.EqualTo(5));
                foreach (Text text in legend.Root.GetComponentsInChildren<Text>())
                {
                    var rect = (RectTransform)text.transform;
                    Assert.That(text.preferredHeight, Is.LessThanOrEqualTo(rect.rect.height + 0.1f), text.text);
                    var wordLayout = new TextGenerator();
                    TextGenerationSettings settings = text.GetGenerationSettings(Vector2.zero);
                    Assert.That(text.text.Split(new[] { ' ', '\n' }, StringSplitOptions.RemoveEmptyEntries).Max(word =>
                        wordLayout.GetPreferredWidth(word, settings) / text.pixelsPerUnit),
                        Is.LessThanOrEqualTo(rect.rect.width + 0.1f), text.text);
                    var corners = new Vector3[4];
                    rect.GetWorldCorners(corners);
                    var parentRect = (RectTransform)rect.parent;
                    foreach (Vector3 corner in corners)
                    {
                        Vector3 localCorner = parentRect.InverseTransformPoint(corner);
                        Assert.That(localCorner.x, Is.InRange(parentRect.rect.xMin - 0.1f, parentRect.rect.xMax + 0.1f));
                        Assert.That(localCorner.y, Is.InRange(parentRect.rect.yMin - 0.1f, parentRect.rect.yMax + 0.1f));
                    }
                }
                legend.Hide();
                legend.Hide();
                legend.Control.onClick.Invoke();
                Assert.That(legend.IsVisible, Is.True);
                Assert.That(runtime.RunState.Health, Is.EqualTo(100));
                Assert.That(runtime.RunState.Food, Is.EqualTo(25));
                Assert.That(enemy.LockedIntent, Is.SameAs(locked));
                Assert.That(enemy.Phase, Is.EqualTo(ShamblerPhase.Active));
                CollectionAssert.AreEqual(before, runtime.BoardState.GetEntities());
                GameObject root = legend.Root;
                Mesh[] meshes = legend.Glyphs.Values.Select(glyph => glyph.GlyphMesh).ToArray();
                legend.Dispose();
                legend.Dispose();
                Assert.Throws<ObjectDisposedException>(() => legend.Show());
                yield return null;
                yield return null;
                Assert.That(root == null && meshes.All(mesh => mesh == null), Is.True);
                Assert.That(owner != null && !runtime.IsDisposed, Is.True);
                TestContext.WriteLine("Viewport=" + size + "; glyphs=5; protected=" + inspected);
            }
        }

        /// <summary>Production-owned retained plans stay hidden until replay/recovery completes, without extra turns or duplicate refreshes.</summary>
        [UnityTest]
        public IEnumerator ReplayBoundary_PreservesLockedDisplayUntilNextPublication()
        {
            foreach (string scenario in new[] { "success", "cancel", "unknown", "terminal", "disposed" })
            {
                BoardRuntime runtime = CreateReplayRuntime(scenario == "terminal" ? 1 : 25);
                var owner = new GameObject("Replay boundary " + scenario);
                objects.Add(owner);
                EnemyIntentPresenter display = owner.AddComponent<EnemyIntentPresenter>();
                var sinks = new IntentSinks();
                var diagnostics = new List<PresentationDiagnostic>();
                using (var presenter = new BoardEventPresenter(runtime, sinks, sinks, sinks, diagnostics.Add,
                    moveDuration: 0.2f, intents: display))
                using (var cancellation = new CancellationTokenSource())
                {
                    presenter.SynchronizeFromState();
                    Assert.That(presenter.Coordinator.CanSubmit, Is.False);
                    ShamblerState enemy = runtime.Enemies.Single();
                    EnemyIntent locked = enemy.LockedIntent;
                    EnemyIntentView oldMarker = display.Views[enemy.ActorId];
                    EnemyIntentProjection announced = display.Projections.Single();
                    Assert.That(announced.Symbol, Is.EqualTo(EnemyIntentSymbol.ConditionalMove));
                    Assert.That(announced.AttackOnPlayerEntry, Is.True);
                    Assert.That(announced.TargetPosition, Is.EqualTo(new GridPosition(1, 1)));
                    presenter.SynchronizeFromState();
                    Assert.That(display.Views[enemy.ActorId], Is.SameAs(oldMarker));
                    presenter.Coordinator.Gate.SetBlocked(PresentationInputBlock.Setup, false);
                    CommandSubmission rejected = presenter.Coordinator.SubmitAsync(new MoveCommand(Direction.West)).Result;
                    Assert.That(rejected.Status, Is.EqualTo(CommandSubmissionStatus.Rejected));
                    Assert.That(enemy.LockedIntent, Is.SameAs(locked));
                    Assert.That(display.Views[enemy.ActorId], Is.SameAs(oldMarker));
                    int resolutions = presenter.Coordinator.ResolutionCount;
                    int food = runtime.RunState.Food;
                    presenter.Coordinator.Gate.SetBlocked(PresentationInputBlock.Modal, true);
                    Assert.That(presenter.Coordinator.SubmitAsync(new WaitCommand()).Result.Status,
                        Is.EqualTo(CommandSubmissionStatus.Blocked));
                    yield return null;
                    Assert.That(enemy.LockedIntent, Is.SameAs(locked));
                    Assert.That(runtime.RunState.Food, Is.EqualTo(food));
                    presenter.Coordinator.Gate.SetBlocked(PresentationInputBlock.Modal, false);
                    Assert.That(presenter.Coordinator.ResolutionCount, Is.EqualTo(resolutions));

                    if (scenario == "unknown")
                    {
                        Assert.That(runtime.Controller.Resolve(new MoveCommand(Direction.North)).Accepted, Is.True);
                        Assert.That(display.Views[enemy.ActorId], Is.SameAs(oldMarker));
                        Task<PresentationDiagnostic> recovery = presenter.Dispatcher.ReplayAsync(new GameEvent[] { new UnknownIntentEvent() });
                        while (!recovery.IsCompleted)
                            yield return null;
                        Assert.That(recovery.Result.Code, Is.EqualTo(PresentationDiagnosticCode.UnknownEvent));
                        Assert.That(presenter.Coordinator.ResolutionCount, Is.EqualTo(resolutions));
                    }
                    else
                    {
                        Task<CommandSubmission> pending = presenter.Coordinator.SubmitAsync(new MoveCommand(Direction.North), cancellation.Token);
                        Assert.That(pending.IsCompleted, Is.False);
                        Assert.That(presenter.Coordinator.Gate.State, Is.EqualTo(PresentationGateState.Presenting));
                        Assert.That(display.Projections.Single(), Is.EqualTo(announced));
                        Assert.That(display.Views[enemy.ActorId], Is.SameAs(oldMarker));
                        if (scenario != "terminal")
                        {
                            Assert.That(enemy.Phase, Is.EqualTo(ShamblerPhase.Rest));
                            Assert.That(enemy.LockedIntent.Kind, Is.EqualTo(EnemyIntentKind.Wait));
                        }
                        yield return null;
                        Assert.That(display.Projections.Single(), Is.EqualTo(announced));
                        if (scenario == "cancel")
                            cancellation.Cancel();
                        if (scenario == "disposed")
                            presenter.Dispose();
                        while (!pending.IsCompleted)
                            yield return null;
                        Assert.That(pending.IsFaulted, Is.False);
                        Assert.That(pending.Result.Status, Is.EqualTo(scenario == "cancel" || scenario == "disposed"
                            ? CommandSubmissionStatus.PresentationFailed : CommandSubmissionStatus.Presented));
                        Assert.That(presenter.Coordinator.ResolutionCount, Is.EqualTo(resolutions + 1));
                        Assert.That(presenter.Coordinator.Gate.State, Is.EqualTo(PresentationGateState.Idle));
                    }

                    Assert.That(runtime.RunState.Food, Is.EqualTo(food - 1));
                    Assert.That(runtime.RunState.Health, Is.EqualTo(100));
                    Assert.That(runtime.IsDisposed, Is.False);
                    if (scenario == "terminal" || scenario == "disposed")
                    {
                        Assert.That(display.IsDisposed, Is.True);
                        Assert.That(display.Views, Is.Empty);
                        Assert.That(sinks.Outcomes, Is.EqualTo(scenario == "terminal" ? 1 : 0));
                    }
                    else
                    {
                        Assert.That(display.Projections.Single().Symbol, Is.EqualTo(EnemyIntentSymbol.Wait));
                        Assert.That(display.Projections.Single().SourcePosition, Is.EqualTo(new GridPosition(1, 1)));
                        EnemyIntentView currentMarker = display.Views[enemy.ActorId];
                        Assert.That(currentMarker, Is.Not.SameAs(oldMarker));
                        Assert.That(owner.transform.childCount, Is.LessThanOrEqualTo(2), "Recovery and completion must not publish twice.");
                        presenter.SynchronizeFromState();
                        Assert.That(display.Views[enemy.ActorId], Is.SameAs(currentMarker));
                        foreach (var entity in runtime.BoardState.GetEntities())
                        {
                            Vector3 position = runtime.Views[entity.Id].transform.position;
                            Assert.That(position.x, Is.EqualTo(entity.Position.X));
                            Assert.That(position.y, Is.EqualTo(entity.Position.Y));
                        }
                    }
                    Assert.That(diagnostics.Count, Is.EqualTo(scenario == "cancel" || scenario == "unknown" || scenario == "disposed" ? 1 : 0));
                    TestContext.WriteLine("Replay boundary scenario=" + scenario + "; retained display until boundary; no extra resolution.");
                }
                Assert.That(display.IsDisposed, Is.True);
                yield return null;
            }
        }

        /// <summary>Six fresh announced-outcome scenarios retain fixed targets and identical authoritative results in both animation modes.</summary>
        [UnityTest]
        public IEnumerator AnnouncedConditionalOutcomes_HaveAnimationParity([Values(false, true)] bool animationsEnabled)
        {
            foreach (string scenario in new[] { "empty", "same-cell", "different-cell", "fixed-miss", "adjacent-rest", "shared" })
            {
                BoardRuntime runtime = CreateParityRuntime(scenario);
                var owner = new GameObject("Parity " + scenario + " " + animationsEnabled);
                objects.Add(owner);
                EnemyIntentPresenter display = owner.AddComponent<EnemyIntentPresenter>();
                var sinks = new IntentSinks();
                var diagnostics = new List<PresentationDiagnostic>();
                using (var presenter = new BoardEventPresenter(runtime, sinks, sinks, sinks, diagnostics.Add,
                    moveDuration: 0.2f, intents: display))
                {
                    presenter.AnimationsEnabled = false;
                    presenter.SynchronizeFromState();
                    presenter.Coordinator.Gate.SetBlocked(PresentationInputBlock.Setup, false);
                    if (scenario == "adjacent-rest")
                    {
                        CommandSubmission preparation = presenter.Coordinator.SubmitAsync(new WaitCommand()).Result;
                        Assert.That(preparation.Status, Is.EqualTo(CommandSubmissionStatus.Presented));
                        Assert.That(runtime.RunState.Health, Is.EqualTo(90));
                        Assert.That(runtime.Enemies.Single().Phase, Is.EqualTo(ShamblerPhase.Rest));
                    }
                    presenter.AnimationsEnabled = animationsEnabled;
                    sinks.Events.Clear();
                    string context = "scenario=" + scenario + "; animation=" + animationsEnabled +
                        "; sources=" + string.Join(",", runtime.Enemies.Select(enemy => enemy.ActorId));
                    var announced = display.Projections.ToArray();
                    var markers = runtime.Enemies.Select(enemy => display.Views[enemy.ActorId]).ToArray();
                    var locked = runtime.Enemies.Select(enemy => enemy.LockedIntent).ToArray();
                    int health = runtime.RunState.Health;
                    int food = runtime.RunState.Food;
                    int resolutions = presenter.Coordinator.ResolutionCount;
                    GridPosition? target = scenario == "adjacent-rest" ? (GridPosition?)null : new GridPosition(1, 1);
                    EnemyIntentSymbol symbol = scenario == "adjacent-rest" ? EnemyIntentSymbol.Wait :
                        scenario == "fixed-miss" ? EnemyIntentSymbol.Attack : EnemyIntentSymbol.ConditionalMove;
                    for (int index = 0; index < announced.Length; index++)
                    {
                        Assert.That(announced[index].Symbol, Is.EqualTo(symbol), context);
                        Assert.That(announced[index].TargetPosition, Is.EqualTo(target), context);
                        Assert.That(announced[index].TargetId, Is.EqualTo(target.HasValue ? (EntityId?)runtime.PlayerId : null), context);
                        Assert.That(announced[index].AttackOnPlayerEntry, Is.EqualTo(symbol == EnemyIntentSymbol.ConditionalMove), context);
                        Assert.That(announced[index].SourceId, Is.EqualTo(runtime.Enemies[index].ActorId), context);
                        GridPosition source = scenario == "shared" && index == 0 ? new GridPosition(0, 1) : new GridPosition(2, 1);
                        Assert.That(announced[index].SourcePosition, Is.EqualTo(source), context);
                        GridPosition cell = target ?? source;
                        Assert.That(markers[index].transform.position.x, Is.EqualTo(cell.X), context);
                        Assert.That(markers[index].transform.position.y, Is.EqualTo(cell.Y), context);
                        Assert.That(ActiveGlyphLines(markers[index]).Length, Is.GreaterThan(0), context);
                        Assert.That(markers[index].gameObject.activeInHierarchy, Is.True, context);
                    }
                    CommandSubmission rejected = presenter.Coordinator.SubmitAsync(new InteractCommand(new EntityId(long.MinValue))).Result;
                    Assert.That(rejected.Status, Is.EqualTo(CommandSubmissionStatus.Rejected), context);
                    Assert.That(rejected.Result.Events, Is.Empty, context);
                    Assert.That(runtime.RunState.Health, Is.EqualTo(health), context);
                    Assert.That(runtime.RunState.Food, Is.EqualTo(food), context);
                    for (int index = 0; index < announced.Length; index++)
                    {
                        Assert.That(runtime.Enemies[index].LockedIntent, Is.SameAs(locked[index]), context);
                        Assert.That(display.Views[announced[index].SourceId], Is.SameAs(markers[index]), context);
                    }
                    Assert.That(presenter.Coordinator.ResolutionCount, Is.EqualTo(resolutions + 1), context);
                    Direction direction = scenario == "same-cell" || scenario == "different-cell" || scenario == "shared"
                        ? Direction.East : Direction.North;
                    Task<CommandSubmission> pending = presenter.Coordinator.SubmitAsync(new MoveCommand(direction));
                    if (animationsEnabled)
                    {
                        Assert.That(pending.IsCompleted, Is.False, context);
                        Assert.That(presenter.Coordinator.Gate.State, Is.EqualTo(PresentationGateState.Presenting), context);
                        CollectionAssert.AreEqual(announced, display.Projections, context);
                        yield return null;
                        CollectionAssert.AreEqual(announced, display.Projections, context);
                        for (int index = 0; index < announced.Length; index++)
                            Assert.That(display.Views[announced[index].SourceId], Is.SameAs(markers[index]), context);
                    }
                    while (!pending.IsCompleted)
                        yield return null;
                    Assert.That(pending.IsFaulted, Is.False, context);
                    CommandSubmission submitted = pending.Result;
                    Assert.That(submitted.Status, Is.EqualTo(CommandSubmissionStatus.Presented), context);
                    Assert.That(submitted.Result.Accepted, Is.True, context);
                    Assert.That(diagnostics, Is.Empty, context);
                    Assert.That(runtime.RunState.Food, Is.EqualTo(food - 1), context);
                    Assert.That(runtime.RunState.Health, Is.EqualTo(scenario == "same-cell" ? 90 : health), context);
                    Assert.That(sinks.Health, Is.EqualTo(runtime.RunState.Health), context);
                    Assert.That(sinks.Food, Is.EqualTo(runtime.RunState.Food), context);
                    Assert.That(presenter.Coordinator.ResolutionCount, Is.EqualTo(resolutions + 2), context);
                    GameEvent[] events = submitted.Result.Events.ToArray();
                    string action = scenario == "same-cell" || scenario == "fixed-miss" ? "EnemyAttackResolved" :
                        scenario == "adjacent-rest" ? "EntityWaited" : "EntityMoved";
                    var order = new List<string> { "EntityMoved", "ActionCostApplied", action };
                    if (scenario == "shared")
                        order.Add("EntityWaited");
                    CollectionAssert.AreEqual(order, events.Select(gameEvent => gameEvent.EventType), context);
                    ActionCostAppliedEvent cost = events.OfType<ActionCostAppliedEvent>().Single();
                    Assert.That(cost.EntityId, Is.EqualTo(runtime.PlayerId), context);
                    Assert.That(cost.CostAmount, Is.EqualTo(1), context);
                    CollectionAssert.AreEqual(events.Where(gameEvent => !(gameEvent is ActionCostAppliedEvent)), sinks.Events, context);
                    EntityMovedEvent playerMove = (EntityMovedEvent)events[0];
                    GridPosition playerSource = scenario == "same-cell" ? new GridPosition(0, 1) :
                        scenario == "different-cell" || scenario == "shared" ? new GridPosition(1, 0) :
                        scenario == "fixed-miss" || scenario == "adjacent-rest" ? new GridPosition(1, 1) : new GridPosition(0, 0);
                    Assert.That(playerMove.EntityId, Is.EqualTo(runtime.PlayerId), context);
                    Assert.That(playerMove.From, Is.EqualTo(playerSource), context);
                    Assert.That(playerMove.To, Is.EqualTo(playerSource.Move(direction)), context);
                    for (int index = 0; index < announced.Length; index++)
                    {
                        EntityId sourceId = announced[index].SourceId;
                        GameEvent actual = events[2 + index];
                        GridPosition expectedSource = announced[index].SourcePosition;
                        if (scenario == "same-cell" || scenario == "fixed-miss")
                        {
                            var attack = (EnemyAttackResolvedEvent)actual;
                            bool hit = scenario == "same-cell";
                            Assert.That(attack.AttackerId, Is.EqualTo(sourceId), context);
                            Assert.That(attack.TargetPosition, Is.EqualTo(target.Value), context);
                            Assert.That(attack.IsHit, Is.EqualTo(hit), context);
                            Assert.That(attack.AffectedTargetId, Is.EqualTo(hit ? (EntityId?)runtime.PlayerId : null), context);
                            Assert.That(attack.HealthChange, Is.EqualTo(hit ? -10 : 0), context);
                        }
                        else if (scenario == "adjacent-rest" || scenario == "shared" && index == 1)
                        {
                            Assert.That(((EntityWaitedEvent)actual).EntityId, Is.EqualTo(sourceId), context);
                        }
                        else
                        {
                            var move = (EntityMovedEvent)actual;
                            Assert.That(move.EntityId, Is.EqualTo(sourceId), context);
                            Assert.That(move.From, Is.EqualTo(announced[index].SourcePosition), context);
                            Assert.That(move.To, Is.EqualTo(target.Value), context);
                            expectedSource = target.Value;
                        }
                        Assert.That(runtime.BoardState.GetEntities().Single(entity => entity.Id.Equals(sourceId)).Position,
                            Is.EqualTo(expectedSource), context);
                        Assert.That(runtime.Enemies[index].Phase, Is.EqualTo(scenario == "adjacent-rest"
                            ? ShamblerPhase.Active : ShamblerPhase.Rest), context);
                        EnemyIntentProjection next = display.Projections[index];
                        Assert.That(next.SourceId, Is.EqualTo(sourceId), context);
                        Assert.That(next.SourcePosition, Is.EqualTo(expectedSource), context);
                        Assert.That(next.Symbol, Is.EqualTo(scenario == "adjacent-rest"
                            ? EnemyIntentSymbol.ConditionalMove : EnemyIntentSymbol.Wait), context);
                        Assert.That(next.TargetPosition, Is.EqualTo(scenario == "adjacent-rest"
                            ? (GridPosition?)new GridPosition(2, 2) : null), context);
                        Assert.That(next.AttackOnPlayerEntry, Is.EqualTo(scenario == "adjacent-rest"), context);
                        Assert.That(display.Views[sourceId], Is.Not.SameAs(markers[index]), context);
                    }
                    foreach (var entity in runtime.BoardState.GetEntities())
                    {
                        Assert.That(runtime.Views[entity.Id].transform.position.x, Is.EqualTo(entity.Position.X), context);
                        Assert.That(runtime.Views[entity.Id].transform.position.y, Is.EqualTo(entity.Position.Y), context);
                    }
                    Assert.That(presenter.Coordinator.CanSubmit, Is.True, context);
                    TestContext.WriteLine(context + "; announced=" + symbol + "@" + target +
                        "; outcome=" + string.Join(",", events.Select(gameEvent => gameEvent.EventType)) +
                        "; HP=" + runtime.RunState.Health + "; food=" + runtime.RunState.Food +
                        "; next=" + string.Join(",", display.Projections.Select(projection => projection.Symbol)));
                }
                yield return null;
            }
        }

        private BoardRuntime CreateParityRuntime(string scenario)
        {
            var run = new RunState("intent-parity-" + runtimes.Count, 12345,
                new RunStateConfiguration(100, 25, 0, "forest.start"));
            var request = new BoardRequest(run.RunId, run.RunSeed, run.WorldNodeId, run.CurrentDay,
                run.GetBoardSeed(), "forest", "temperate", 1);
            var layout = new List<LegacyBoardView>();
            for (int horizontal = 0; horizontal < 3; horizontal++)
                for (int vertical = 0; vertical < 3; vertical++)
                    AddReplayView(layout, LegacyBoardContentKind.Floor, horizontal, vertical);
            GridPosition player = scenario == "same-cell" ? new GridPosition(0, 1) :
                scenario == "different-cell" || scenario == "shared" ? new GridPosition(1, 0) :
                scenario == "fixed-miss" || scenario == "adjacent-rest" ? new GridPosition(1, 1) : new GridPosition(0, 0);
            AddReplayView(layout, LegacyBoardContentKind.Player, player.X, player.Y);
            AddReplayView(layout, LegacyBoardContentKind.Exit, 0, 2);
            AddReplayView(layout, LegacyBoardContentKind.Enemy, 2, 1, 10);
            if (scenario == "shared")
                AddReplayView(layout, LegacyBoardContentKind.Enemy, 0, 1, 10);
            BoardRuntime runtime = LegacyBoardRuntimeComposer.Compose(request, run, new GridBounds(0, 0, 2, 2), layout);
            runtimes.Add(runtime);
            return runtime;
        }

        private BoardRuntime CreateReplayRuntime(int food = 25)
        {
            var run = new RunState("intent-replay-" + runtimes.Count, 12345,
                new RunStateConfiguration(100, food, 0, "forest.start"));
            var request = new BoardRequest(run.RunId, run.RunSeed, run.WorldNodeId, run.CurrentDay,
                run.GetBoardSeed(), "forest", "temperate", 1);
            var layout = new List<LegacyBoardView>();
            for (int horizontal = 0; horizontal < 3; horizontal++)
                for (int vertical = 0; vertical < 3; vertical++)
                    AddReplayView(layout, LegacyBoardContentKind.Floor, horizontal, vertical);
            AddReplayView(layout, LegacyBoardContentKind.Player, 0, 0);
            AddReplayView(layout, LegacyBoardContentKind.Exit, 2, 2);
            AddReplayView(layout, LegacyBoardContentKind.Enemy, 2, 1, 10);
            BoardRuntime runtime = LegacyBoardRuntimeComposer.Compose(request, run, new GridBounds(0, 0, 2, 2), layout);
            runtimes.Add(runtime);
            return runtime;
        }

        private void AddReplayView(List<LegacyBoardView> layout, LegacyBoardContentKind kind,
            int horizontal, int vertical, int? damage = null)
        {
            var view = new GameObject("Replay " + kind);
            view.transform.position = new Vector3(horizontal, vertical, 0);
            objects.Add(view);
            layout.Add(new LegacyBoardView(view, kind, new GridPosition(horizontal, vertical), damage));
        }

        private sealed class IntentSinks : IRunHud, ITurnFeedback, IBoardOutcomeSink
        {
            public int Outcomes;
            public int Health;
            public int Food;
            public readonly List<GameEvent> Events = new List<GameEvent>();
            public void Refresh(int health, int food)
            {
                Health = health;
                Food = food;
            }
            public void ShowRejection(CommandRejectionCode reason) { }
            public void ShowEvent(GameEvent gameEvent) => Events.Add(gameEvent);
            public bool TryNotify(BoardRequest request, GameEvent outcome)
            {
                Outcomes++;
                return true;
            }
        }

        private sealed class UnknownIntentEvent : GameEvent
        {
            public override string EventType => "Test.UnknownIntent";
        }

        private BoardRuntime CreateIntentRuntime(out ShamblerState enemy)
        {
            var run = new RunState("intent-lifetime-" + runtimes.Count, 12345, new RunStateConfiguration(100, 25, 0, "forest.start"));
            var request = new BoardRequest(run.RunId, run.RunSeed, run.WorldNodeId, run.CurrentDay,
                run.GetBoardSeed(), "forest", "temperate", 1);
            var layout = new List<LegacyBoardView>();
            foreach (LegacyBoardContentKind kind in new[] { LegacyBoardContentKind.Floor, LegacyBoardContentKind.Floor,
                LegacyBoardContentKind.Player, LegacyBoardContentKind.Exit })
            {
                int horizontal = layout.Count == 1 || kind == LegacyBoardContentKind.Exit ? 1 : 0;
                var instance = new GameObject("Borrowed runtime " + kind);
                instance.transform.position = new Vector3(horizontal, 0, 0);
                objects.Add(instance);
                layout.Add(new LegacyBoardView(instance, kind, new GridPosition(horizontal, 0)));
            }
            BoardRuntime runtime = LegacyBoardRuntimeComposer.Compose(request, run, new GridBounds(0, 0, 1, 0), layout);
            runtimes.Add(runtime);
            enemy = new ShamblerState(new EntityId(0), new ShamblerDefinition(10));
            enemy.LockIntent(new EnemyIntent(enemy.ActorId, EnemyIntentKind.Wait));
            var definition = new BoardEntityDefinition(BoardLayer.Actor, EntityKind.Enemy, "lifetime.enemy", BoardEntityTraits.Default);
            Assert.That(runtime.BoardState.TryAdd(new BoardEntityState(enemy.ActorId, definition, new GridPosition(1, 0))), Is.True);
            return runtime;
        }

        private EnemyIntentPresenter CreatePublisherFixture(out BoardState board, out ShamblerState[] enemies)
        {
            board = new BoardState();
            var definition = new BoardEntityDefinition(BoardLayer.Actor, EntityKind.Enemy, "publisher.enemy", BoardEntityTraits.Default);
            var cells = new[] { new GridPosition(2, 3), new GridPosition(4, 3), new GridPosition(6, 6) };
            var ids = new[] { new EntityId(-5), new EntityId(0), new EntityId(7) };
            enemies = new ShamblerState[3];
            for (int index = 0; index < enemies.Length; index++)
            {
                enemies[index] = new ShamblerState(ids[index], new ShamblerDefinition(10));
                Assert.That(board.TryAdd(new BoardEntityState(ids[index], definition, cells[index])), Is.True);
                enemies[index].LockIntent(index == 2 ? new EnemyIntent(ids[index], EnemyIntentKind.Wait) :
                    new EnemyIntent(ids[index], EnemyIntentKind.Move, new GridPosition(3, 3), new EntityId(-100), true));
            }
            var instance = new GameObject("Intent publisher fixture");
            objects.Add(instance);
            EnemyIntentPresenter presenter = instance.AddComponent<EnemyIntentPresenter>();
            BoardState capturedBoard = board;
            presenter.Bind(capturedBoard, () => capturedBoard);
            return presenter;
        }

        private static IEnumerable<EnemyIntentProjection[]> ProjectionPermutations(EnemyIntentProjection[] projections)
        {
            if (projections.Length == 0)
            {
                yield return Array.Empty<EnemyIntentProjection>();
                yield break;
            }
            for (int index = 0; index < projections.Length; index++)
            {
                EnemyIntentProjection selected = projections[index];
                EnemyIntentProjection[] remaining = projections.Where((item, itemIndex) => itemIndex != index).ToArray();
                foreach (EnemyIntentProjection[] suffix in ProjectionPermutations(remaining))
                    yield return new[] { selected }.Concat(suffix).ToArray();
            }
        }

        private static LineRenderer[] ActiveGlyphLines(EnemyIntentView view)
        {
            return view.transform.Find("Glyph").GetComponentsInChildren<LineRenderer>();
        }

        private static string GeometrySignature(EnemyIntentView view)
        {
            return string.Join("|", ActiveGlyphLines(view).Select(line =>
            {
                var points = Enumerable.Range(0, line.positionCount).Select(index =>
                    view.transform.Find("Glyph").InverseTransformPoint(line.transform.TransformPoint(line.GetPosition(index))));
                return $"{line.loop}:{string.Join(";", points)}";
            }));
        }
    }
}