using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace HallowBlaze.Editor.BoardAuthoring
{
    /// <summary>Performs the explicitly approved source-asset rename, preserving Unity ownership and serialized references.</summary>
    public static class BoardContentAssetMaintenance
    {
        /// <summary>Applies the recorded idempotent rename recipe; this is not a display alias or a content-ID migration.</summary>
        public static void NormalizePrototypeNames()
        {
            for (int variant = 1; variant <= 8; variant++)
                Move("Assets/Prefabs/Tiles/Wall" + variant + ".prefab", "Assets/Prefabs/Tiles/Bush" + variant.ToString("00") + ".prefab");
            Move("Assets/Prefabs/Enemy1.prefab", "Assets/Prefabs/Shambler10.prefab");
            Move("Assets/Prefabs/Enemy2.prefab", "Assets/Prefabs/Shambler20.prefab");
            NormalizeFruitNames();
            Move("Assets/Prefabs/Tiles/LandmarkAxe.prefab", "Assets/Prefabs/Tiles/Axe.prefab");
            Move("Assets/Sprites/LandmarkAxe.png", "Assets/Sprites/Axe.png");
            AssetDatabase.SaveAssets();
            Debug.Log("M6.9e source names normalized; existing GUIDs and references retained.");
        }

        /// <summary>Names the existing red and blue fruit prefabs at their sources without changing sprites, GUIDs or saved content IDs.</summary>
        public static void NormalizeFruitNames()
        {
            string blueberrySource = string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID("Assets/Prefabs/Tiles/Berries.prefab"))
                ? "Assets/Prefabs/Tiles/Food2.prefab" : "Assets/Prefabs/Tiles/Berries.prefab";
            Move(blueberrySource, "Assets/Prefabs/Tiles/Blueberries.prefab");
            Move("Assets/Prefabs/Tiles/Food.prefab", "Assets/Prefabs/Tiles/Raspberries.prefab");
            AssetDatabase.SaveAssets();
        }

        private static void Move(string source, string destination)
        {
            string guid = AssetDatabase.AssetPathToGUID(source);
            if (string.IsNullOrEmpty(guid))
            {
                if (string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(destination))) throw new IOException("Missing rename source: " + source);
                return;
            }
            if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(destination))) throw new IOException("Rename destination already exists: " + destination);
            string error = AssetDatabase.MoveAsset(source, destination);
            if (!string.IsNullOrEmpty(error)) throw new IOException(error);
            if (AssetDatabase.AssetPathToGUID(destination) != guid) throw new IOException("Unity GUID changed: " + destination);
            if (!destination.EndsWith(".prefab", StringComparison.Ordinal)) return;
            GameObject root = PrefabUtility.LoadPrefabContents(destination);
            try
            {
                root.name = Path.GetFileNameWithoutExtension(destination);
                PrefabUtility.SaveAsPrefabAsset(root, destination);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }
}