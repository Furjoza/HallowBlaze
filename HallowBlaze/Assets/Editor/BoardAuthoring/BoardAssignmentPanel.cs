using System;
using System.IO;
using System.Linq;
using HallowBlaze.Core.Generation;
using HallowBlaze.Core.Generation.Landmarks;
using HallowBlaze.Core.World;
using UnityEditor;
using UnityEngine;

namespace HallowBlaze.Editor.BoardAuthoring
{
    /// <summary>Retains an explicit sandbox target independently of board layout, without editing the campaign world.</summary>
    [Serializable]
    public sealed class BoardAssignmentPanel
    {
        [SerializeField] private string worldJson = "";
        [SerializeField] private string segmentId = "";
        [SerializeField] private bool enabled;
        [SerializeField] private bool landmarkTarget;
        private WorldDefinition world;
        /// <summary>Gets whether Test should use the explicitly chosen route/segment rather than the minimal local world.</summary>
        public bool Enabled => enabled;
        /// <summary>Gets the immutable source world document, including its publication identity.</summary>
        public string WorldJson { get { EnsureWorld(); return worldJson; } }
        /// <summary>Gets the parsed source world; changing a target does not change any published node or segment.</summary>
        public WorldDefinition World { get { EnsureWorld(); return world; } }
        /// <summary>Gets the exact selected stable segment key.</summary>
        public string SegmentId => segmentId;
        /// <summary>Disables the target when opening an unassigned board while retaining the chosen world publication.</summary>
        public void ClearTarget() { enabled = false; segmentId = ""; landmarkTarget = false; }
        /// <summary>Selects a validated external assignment for the currently open template.</summary>
        public void Select(BoardAssignment assignment)
        {
            EnsureWorld();
            if (assignment.WorldId != world.WorldDefinitionId || assignment.WorldVersion != world.Version)
                throw new InvalidOperationException("Open the matching world publication before this assignment.");
            segmentId = assignment.SegmentId;
            enabled = true;
            landmarkTarget = assignment.IsLandmark;
        }
        /// <summary>Builds a detached binding from the exact currently edited template and variant.</summary>
        public BoardAssignment Create(LandmarkTemplate template, string variantId)
        {
            EnsureWorld();
            return new BoardAssignment(world, segmentId, template, variantId);
        }
        /// <summary>Draws target controls; disk operations are delegated to the window's protected document store.</summary>
        public void Draw(Action<Action> defer, Action save, Action open)
        {
            EnsureWorld();
            enabled = EditorGUILayout.Toggle("Assign To World Segment", enabled);
            if (!enabled) return;
            landmarkTarget = EditorGUILayout.Toggle("Landmark Destination", landmarkTarget);
            var routes = world.Edges.Where(edge => !landmarkTarget || world.TryGetNode(edge.ToNodeId, out WorldNodeDefinition destination) &&
                destination.PlaceKind == "landmark").ToArray();
            if (routes.Length == 0) { EditorGUILayout.HelpBox("No compatible route is available.", MessageType.Warning); return; }
            int routeIndex = Math.Max(0, Array.FindIndex(routes, edge => edge.Segments.Any(segment => segment.SegmentId == segmentId)));
            string[] labels = routes.Select(edge => ObjectNames.NicifyVariableName(edge.EdgeId.Replace("road.", "").Replace('-', ' ')) +
                " | " + edge.FromNodeId + " > " + edge.ToNodeId + " | " + edge.EdgeId).ToArray();
            routeIndex = EditorGUILayout.Popup("Route", routeIndex, labels);
            WorldEdgeDefinition selectedRoute = routes[routeIndex];
            var segments = landmarkTarget ? selectedRoute.Segments.Skip(selectedRoute.SegmentCount - 1).ToArray() : selectedRoute.Segments.ToArray();
            int segmentIndex = Math.Max(0, Array.FindIndex(segments, segment => segment.SegmentId == segmentId));
            segmentIndex = EditorGUILayout.Popup("Segment", segmentIndex, segments.Select((segment, index) => (landmarkTarget ? selectedRoute.SegmentCount : index + 1) +
                "/" + selectedRoute.SegmentCount + " | " + segment.SegmentId).ToArray());
            segmentId = segments[segmentIndex].SegmentId;
            if (world.TryGetNode(selectedRoute.ToNodeId, out WorldNodeDefinition node))
                EditorGUILayout.LabelField("Biome / World", node.BiomeFamily + " | " + world.WorldDefinitionId + "/" + world.Version, EditorStyles.miniLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent(EditorGUIUtility.IconContent("SaveActive").image, "Save exact assignment"), GUILayout.Width(27))) defer(save);
                if (GUILayout.Button(new GUIContent(EditorGUIUtility.IconContent("FolderOpened Icon").image, "Open assignment for this template"), GUILayout.Width(27))) defer(open);
                if (GUILayout.Button(new GUIContent(EditorGUIUtility.IconContent("Folder Icon").image, "Choose world publication"), GUILayout.Width(27))) defer(ChooseWorld);
            }
        }
        private void EnsureWorld()
        {
            if (world != null) return;
            if (string.IsNullOrEmpty(worldJson)) worldJson = File.ReadAllText(Path.Combine(Application.dataPath, "GameData/World/prototype-world.json"));
            world = WorldDefinitionJson.Deserialize(worldJson);
        }
        private void ChooseWorld()
        {
            string path = EditorUtility.OpenFilePanel("World Publication", Application.dataPath, "json");
            if (string.IsNullOrEmpty(path)) return;
            string json = File.ReadAllText(path);
            WorldDefinition candidate = WorldDefinitionJson.Deserialize(json);
            world = candidate;
            worldJson = json;
            segmentId = candidate.Edges[0].Segments[0].SegmentId;
        }
    }
}