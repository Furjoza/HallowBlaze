using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HallowBlaze.Tests.EditMode
{
    /// <summary>Checks the authored boulder view independently of gameplay and provides an explicit Unity-owned rebuild operation.</summary>
    public sealed class BoulderPrefabTests
    {
        private const string TexturePath = "Assets/Sprites/Boulder.png";
        private const string PrefabPath = "Assets/Prefabs/Tiles/Boulder.prefab";

        /// <summary>The one-cell rock is a visual-only prefab with an imported sprite, no physics or missing scripts.</summary>
        [Test]
        public void AuthoredPrefabIsVisualOnlyAndHasValidReferences()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null);
            Assert.That(PrefabUtility.GetPrefabAssetType(prefab), Is.EqualTo(PrefabAssetType.Regular));
            Assert.That(prefab.GetComponentsInChildren<Component>(true).Select(component => component.GetType()),
                Is.EquivalentTo(new[] { typeof(Transform), typeof(SpriteRenderer) }));
            Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(prefab), Is.Zero);
            SpriteRenderer renderer = prefab.GetComponent<SpriteRenderer>();
            Assert.That(renderer.sprite, Is.Not.Null);
            Assert.That(AssetDatabase.GetAssetPath(renderer.sprite), Is.EqualTo(TexturePath));
            Assert.That(renderer.sprite.bounds.size.x, Is.LessThanOrEqualTo(1f));
            Assert.That(renderer.sprite.bounds.size.y, Is.LessThanOrEqualTo(1f));
            Assert.That(renderer.sortingLayerName, Is.EqualTo("Units"));
            var importer = (TextureImporter)AssetImporter.GetAtPath(TexturePath);
            Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Sprite));
            Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(importer.mipmapEnabled, Is.False);
        }

        /// <summary>Rebuilds only the explicitly authored boulder bitmap and prefab through Unity asset APIs, preserving existing metadata.</summary>
        /// <remarks>This opt-in authoring command is never invoked by test setup or normal gameplay.</remarks>
        [MenuItem("HallowBlaze/Authoring/Rebuild Boulder View")]
        public static void RebuildPrefab()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Boulder authoring requires EditMode.");
            string[] rows =
            {
                "................", "......hhhh......", "....hhhhhmm.....", "...hhhhmmmmm....",
                "..hhhmmmmlmmm...", "..hmmmmmlmmmdd..", ".hmmmmmlmmmdddd.", ".hmmmmlmmmmdddd.",
                ".hmmmlmmmdddddd.", ".mmmlmmmddddddd.", "..mmlmmddddddd..", "..mmmddddddddd..",
                "...dddddddddd...", "....dddddddd....", ".....ssssss.....", "................"
            };
            var texture = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            try
            {
                for (int horizontal = 0; horizontal < 32; horizontal++)
                for (int vertical = 0; vertical < 32; vertical++)
                {
                    char shade = rows[15 - vertical / 2][horizontal / 2];
                    Color32 color = shade == 'h' ? new Color32(207, 216, 207, 255) :
                        shade == 'm' ? new Color32(146, 155, 148, 255) :
                        shade == 'd' ? new Color32(92, 104, 98, 255) :
                        shade == 'l' ? new Color32(66, 76, 72, 255) :
                        shade == 's' ? new Color32(35, 57, 46, 180) : new Color32(0, 0, 0, 0);
                    texture.SetPixel(horizontal, vertical, color);
                }
                texture.Apply();
                File.WriteAllBytes(TexturePath, texture.EncodeToPNG());
            }
            finally { Object.DestroyImmediate(texture); }
            AssetDatabase.ImportAsset(TexturePath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(TexturePath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 32;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();
            var owner = new GameObject("Boulder");
            try
            {
                SpriteRenderer renderer = owner.AddComponent<SpriteRenderer>();
                renderer.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(TexturePath);
                renderer.sortingLayerName = "Units";
                if (PrefabUtility.SaveAsPrefabAsset(owner, PrefabPath) == null)
                    throw new InvalidOperationException("Unity did not save the boulder prefab.");
                AssetDatabase.SaveAssets();
            }
            finally { Object.DestroyImmediate(owner); }
        }
    }
}