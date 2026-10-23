using System;
using System.IO;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Presentation.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using EntityId = HallowBlaze.Core.Board.Primitives.EntityId;
using Object = UnityEngine.Object;

namespace HallowBlaze.Tests.EditMode
{
    /// <summary>Validates Unity-authored visual-only plate/gate prefabs and exposes explicit opt-in art rebuilding.</summary>
    public sealed class PlateGatePrefabTests
    {
        /// <summary>Both prefabs have valid state sprites, no physics and no missing scripts; state changes never require a gameplay component.</summary>
        [TestCase("PressurePlate")]
        [TestCase("Gate")]
        public void PrefabHasTwoDistinctVisualStatesAndNoPhysics(string name)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Tiles/" + name + ".prefab");
            Assert.That(prefab, Is.Not.Null);
            var owner = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(owner), Is.Zero);
                Assert.That(owner.GetComponentsInChildren<Collider2D>(true), Is.Empty);
                Assert.That(owner.GetComponentsInChildren<Rigidbody2D>(true), Is.Empty);
                Assert.That(owner.GetComponentsInChildren<Collider>(true), Is.Empty);
                Assert.That(owner.GetComponentsInChildren<Rigidbody>(true), Is.Empty);
                PlateGateView view = owner.GetComponent<PlateGateView>();
                var board = new BoardState();
                board.TryAdd(new BoardEntityState(new EntityId(10), PlateGateValidator.PlateContent, new GridPosition(2, 1)));
                board.TryAdd(new BoardEntityState(new EntityId(20), PlateGateValidator.GateContent, new GridPosition(4, 1)));
                board.TryAdd(new BoardEntityState(new EntityId(1), new BoardEntityDefinition(BoardLayer.Actor, EntityKind.Player, "player"), new GridPosition(1, 1)));
                board.ConfigurePlateGates(new[] { new PressurePlateDefinition(new EntityId(10), "A") }, new[] { new GateDefinition(new EntityId(20), "A") });
                if (name == "Gate") view.Present(board.GetGates()[0]); else view.Present(board.GetPressurePlates()[0]);
                Sprite inactive = owner.GetComponent<SpriteRenderer>().sprite;
                board.TryMove(new EntityId(1), new GridPosition(2, 1));
                if (name == "Gate") view.Present(board.GetGates()[0]); else view.Present(board.GetPressurePlates()[0]);
                Assert.That(view.DisplayedActive, Is.True);
                Assert.That(owner.GetComponent<SpriteRenderer>().sprite, Is.Not.SameAs(inactive));
                Assert.That(view.LinkLabel, Does.Contain("A"));
                Assert.That(owner.GetComponent<SpriteRenderer>().sprite.bounds.size.x, Is.LessThanOrEqualTo(1));
                Assert.That(owner.GetComponentsInChildren<MonoBehaviour>(true).Length, Is.EqualTo(1));
            }
            finally { Object.DestroyImmediate(owner); }
        }

        /// <summary>Creates only four state bitmaps and two view prefabs through Unity APIs, preserving existing asset metadata on explicit rebuild.</summary>
        [MenuItem("HallowBlaze/Authoring/Rebuild Plate Gate Views")]
        public static void RebuildPrefabs()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Plate/gate authoring requires EditMode.");
            Sprite plateUp = WriteSprite("PressurePlateUp", false, false);
            Sprite plateDown = WriteSprite("PressurePlateDown", false, true);
            Sprite gateClosed = WriteSprite("GateClosed", true, false);
            Sprite gateOpen = WriteSprite("GateOpen", true, true);
            SavePrefab("PressurePlate", plateUp, plateDown);
            SavePrefab("Gate", gateClosed, gateOpen);
            AssetDatabase.SaveAssets();
        }

        private static Sprite WriteSprite(string name, bool gate, bool active)
        {
            string path = "Assets/Sprites/" + name + ".png";
            var texture = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            try
            {
                for (int horizontal = 0; horizontal < 32; horizontal++)
                for (int vertical = 0; vertical < 32; vertical++)
                {
                    Color32 color = new Color32(0, 0, 0, 0);
                    if (horizontal >= 2 && horizontal <= 29 && vertical >= 2 && vertical <= 29)
                    {
                        color = new Color32(32, 48, 43, 255);
                        if (gate)
                        {
                            bool frame = horizontal <= 5 || horizontal >= 26 || vertical >= 26;
                            bool bars = !active && (horizontal % 7 <= 2 || vertical == 16 || vertical == 17);
                            if (frame || bars) color = active ? new Color32(142, 197, 168, 255) : new Color32(209, 148, 134, 255);
                        }
                        else
                        {
                            int inset = active ? 8 : 5;
                            if (horizontal >= inset && horizontal <= 31 - inset && vertical >= inset && vertical <= 31 - inset)
                                color = active ? new Color32(240, 205, 92, 255) : new Color32(145, 155, 152, 255);
                            if (horizontal == 3 || horizontal == 28 || vertical == 3 || vertical == 28) color = new Color32(222, 227, 209, 255);
                        }
                    }
                    texture.SetPixel(horizontal, vertical, color);
                }
                texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally { Object.DestroyImmediate(texture); }
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 32;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static void SavePrefab(string name, Sprite inactive, Sprite active)
        {
            var owner = new GameObject(name);
            try
            {
                SpriteRenderer renderer = owner.AddComponent<SpriteRenderer>();
                renderer.sortingLayerName = "Floor";
                renderer.sortingOrder = 2;
                owner.AddComponent<PlateGateView>().ConfigureSprites(inactive, active);
                if (PrefabUtility.SaveAsPrefabAsset(owner, "Assets/Prefabs/Tiles/" + name + ".prefab") == null)
                    throw new InvalidOperationException("Unity did not save the plate/gate prefab.");
            }
            finally { Object.DestroyImmediate(owner); }
        }
    }
}