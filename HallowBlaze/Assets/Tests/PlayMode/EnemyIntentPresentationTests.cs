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