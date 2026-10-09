using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HallowBlaze.Core.Board.Primitives;
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

                LineRenderer[] lines = view.GetComponentsInChildren<LineRenderer>();
                Assert.That(lines.Length, Is.EqualTo(symbol == EnemyIntentSymbol.ConditionalMove ? 4 : 2), symbol.ToString());
                Assert.That(view.Projection, Is.SameAs(projection));
                Assert.That(view.LocalBounds, Is.EqualTo(layout));
                Assert.That(instance.transform.localScale, Is.EqualTo(scale));
                Assert.That(instance.transform.position, Is.EqualTo(Vector3.zero));
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
                        Vector3 point = view.transform.InverseTransformPoint(line.transform.TransformPoint(line.GetPosition(index)));
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
                Assert.That(view.GetComponentsInChildren<LineRenderer>().All(line => line.enabled && line.gameObject.activeInHierarchy), Is.True);
                Assert.That(view.LocalBounds, Is.EqualTo(layout));
            }
            EnemyIntentProjection previous = view.Projection;
            string previousGeometry = GeometrySignature(view);
            Assert.Throws<ArgumentNullException>(() => view.Show(null));
            Assert.That(view.Projection, Is.SameAs(previous));
            Assert.That(GeometrySignature(view), Is.EqualTo(previousGeometry));
            Assert.That(signatures.Count, Is.EqualTo(5));
        }

        private static string GeometrySignature(EnemyIntentView view)
        {
            return string.Join("|", view.GetComponentsInChildren<LineRenderer>().Select(line =>
            {
                var points = Enumerable.Range(0, line.positionCount).Select(index =>
                    view.transform.InverseTransformPoint(line.transform.TransformPoint(line.GetPosition(index))));
                return $"{line.loop}:{string.Join(";", points)}";
            }));
        }
    }
}