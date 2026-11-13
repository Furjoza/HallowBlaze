using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using HallowBlaze.Core.Board.Primitives;
using HallowBlaze.Core.Generation.Landmarks;
using HallowBlaze.Core.Generation.Validation;
using HallowBlaze.Core.State;
using HallowBlaze.Core.World;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HallowBlaze.Editor.BoardAuthoring
{
    /// <summary>Retains an owning validator's error without inventing a cell for a document-wide failure.</summary>
    public sealed class BoardAuthoringDiagnostic
    {
        internal BoardAuthoringDiagnostic(string code, string message, string fieldPath = "", string variantId = null,
            GridPosition? position = null, int? entityId = null)
        {
            Code = code;
            Message = message;
            FieldPath = fieldPath;
            VariantId = variantId;
            Position = position;
            EntityId = entityId;
        }

        /// <summary>Gets the original rule code, or ReaderError for unstructured reader/constructor failures.</summary>
        public string Code { get; }
        /// <summary>Gets Error because every diagnostic blocks a validated template/assignment save.</summary>
        public string Severity => "Error";
        /// <summary>Gets the original error with template/version context when available; all diagnostics are blocking errors.</summary>
        public string Message { get; }
        /// <summary>Gets the exact JSON field path when available, otherwise an empty document-wide path.</summary>
        public string FieldPath { get; }
        /// <summary>Gets the affected stable variant ID, or null for an unlocalized failure.</summary>
        public string VariantId { get; }
        /// <summary>Gets the affected grid cell; consumers display X.Y, not row/column.</summary>
        public GridPosition? Position { get; }
        /// <summary>Gets an explicitly identified entity, or null when the owning error does not identify one.</summary>
        public int? EntityId { get; }
    }

    /// <summary>Adapts the strict reader and authoritative landmark validation; does not implement board rules.</summary>
    public static class BoardAuthoringValidation
    {
        /// <summary>Checks every variant, preserving the optional-exit exception owned by LandmarkBoard.Validate.</summary>
        /// <remarks>A constructor failure is reported before downstream validation; no partial board is treated as valid.</remarks>
        public static IReadOnlyList<BoardAuthoringDiagnostic> Template(JObject data, BoardValidationCatalog catalog)
        {
            var errors = new List<BoardAuthoringDiagnostic>();
            try
            {
                if (data == null) throw new ArgumentNullException(nameof(data));
                LandmarkTemplate template = LandmarkTemplateJson.Deserialize(data.ToString(Formatting.None), catalog);
                foreach (string variantId in template.VariantIds)
                {
                    var address = new BoardAddress("authoring", "authoring", "authoring", "authoring", 1,
                        template.Config.Id, template.Config.Version, 0);
                    LandmarkBoard board = template.CreateBoard(address, variantId);
                    JObject variant = ((JArray)data["variants"]).OfType<JObject>().Single(row => (string)row["id"] == variantId);
                    foreach (BoardValidationError error in board.Validate(catalog).Errors)
                    {
                        JObject[] candidates = error.Position.HasValue
                            ? ((JArray)variant["entities"]).OfType<JObject>().Where(row =>
                                (int)row["x"] == error.Position.Value.X && (int)row["y"] == error.Position.Value.Y).ToArray()
                            : Array.Empty<JObject>();
                        JObject entity = candidates.Length == 1 ? candidates[0] : null;
                        errors.Add(new BoardAuthoringDiagnostic(error.Code,
                            template.Id + "/" + template.Version + " variant " + variantId + ": " + error.Message,
                            entity?.Path ?? variant.Path, variantId, error.Position, entity == null ? null : (int?)entity["id"]));
                    }
                }
            }
            catch (Exception exception) when (IsValidationFailure(exception))
            {
                errors.Add(FromException(data, exception));
            }
            return errors.AsReadOnly();
        }

        /// <summary>Checks only the existing placement contract against immutable world/template publications.</summary>
        public static IReadOnlyList<BoardAuthoringDiagnostic> Placement(JObject data, WorldDefinition world, LandmarkTemplate template)
        {
            try
            {
                if (data == null) throw new ArgumentNullException(nameof(data));
                LandmarkTemplateJson.DeserializePlacement(data.ToString(Formatting.None), world, template);
                return Array.Empty<BoardAuthoringDiagnostic>();
            }
            catch (Exception exception) when (IsValidationFailure(exception))
            {
                return Array.AsReadOnly(new[] { FromException(data, exception) });
            }
        }

        internal static void RequireValid(IReadOnlyList<BoardAuthoringDiagnostic> errors)
        {
            if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors.Select(error => error.Message)));
        }

        private static bool IsValidationFailure(Exception exception) => exception is JsonException || exception is ArgumentException;

        private static BoardAuthoringDiagnostic FromException(JObject data, Exception exception)
        {
            string message = exception.Message;
            string path = exception is JsonReaderException reader ? reader.Path : string.Empty;
            Match field = Regex.Match(message, @"^Landmark ([^:]+):");
            if (exception is JsonException && field.Success && field.Groups[1].Value != "<missing>") path = field.Groups[1].Value;
            JObject variant = (data?["variants"] as JArray)?.OfType<JObject>().FirstOrDefault(row =>
                message.Contains("variant " + (row["id"]?.ToString() ?? string.Empty) + ":") ||
                !string.IsNullOrEmpty(path) && path.StartsWith(row.Path + ".", StringComparison.Ordinal));
            Match code = Regex.Match(message, @"\| ([A-Za-z]+):");
            Match cell = Regex.Match(message, @" at \((-?\d+), (-?\d+)\)");
            GridPosition? position = null;
            if (cell.Success && int.TryParse(cell.Groups[1].Value, out int horizontal) && int.TryParse(cell.Groups[2].Value, out int vertical))
                position = new GridPosition(horizontal, vertical);
            JToken fieldToken = null;
            if (!string.IsNullOrEmpty(path))
            {
                try { fieldToken = data?.SelectToken(path, false); }
                catch (JsonException) { }
                if (fieldToken == null) path = string.Empty;
            }
            JObject entity = fieldToken?.AncestorsAndSelf().OfType<JObject>().FirstOrDefault(row =>
                row["layer"] != null && row["x"]?.Type == JTokenType.Integer && row["y"]?.Type == JTokenType.Integer);
            if (entity == null && position.HasValue)
            {
                JObject[] candidates = (variant?["entities"] as JArray)?.OfType<JObject>().Where(row =>
                    row["x"]?.Type == JTokenType.Integer && row["y"]?.Type == JTokenType.Integer &&
                    row["x"].ToString() == position.Value.X.ToString() && row["y"].ToString() == position.Value.Y.ToString()).ToArray();
                if (candidates?.Length == 1) entity = candidates[0];
            }
            if (string.IsNullOrEmpty(path)) path = entity?.Path ?? variant?.Path ?? string.Empty;
            string identity = data == null ? "<missing>" : (data["templateId"]?.ToString() ?? "<missing>") + "/" + data["templateVersion"];
            return new BoardAuthoringDiagnostic(code.Success ? code.Groups[1].Value : "ReaderError",
                identity + ": " + message, path, variant?["id"]?.Type == JTokenType.String ? (string)variant["id"] : null, position,
                entity?["id"]?.Type == JTokenType.Integer && int.TryParse(entity["id"].ToString(), out int identifier) ? identifier : (int?)null);
        }
    }
}