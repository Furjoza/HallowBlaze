using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Presentation.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using EntityId = HallowBlaze.Core.Board.Primitives.EntityId;

namespace HallowBlaze.Tests.PlayMode
{
    /// <summary>Verifies persistent intent geometry using transient objects, never production scenes.</summary>
    public sealed class EnemyIntentPresentationTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();

        /// <summary>Releases all transient views and their owned materials after each isolated fixture.</summary>
        [TearDown]
        public void TearDown()
        {
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