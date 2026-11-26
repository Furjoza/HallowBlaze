namespace HallowBlaze.Core.State
{
    /// <summary>Immutable consumable content; it contains neither run ownership nor executable use effects.</summary>
    public sealed class ItemDefinition
    {
        /// <summary>Defines a fixed-orientation rectangle and stable content references without allocating an item.</summary>
        /// <param name="id">Ordinal content ID, distinct from a run-local instance ID.</param>
        /// <param name="width">Positive horizontal footprint in bag cells; rotation is not supported.</param>
        /// <param name="height">Positive vertical footprint in bag cells.</param>
        /// <param name="resourceFamily">Stable resource-family ID; no resource effect is executed here.</param>
        /// <param name="category">Stable content-category ID.</param>
        /// <param name="quickPocketEligible">Explicit pocket permission; pocket geometry is also checked on placement.</param>
        /// <param name="effectId">Stable reference reserved for the later consumable-effect resolver.</param>
        /// <exception cref="InventoryValidationException">An ID is invalid or a dimension is not positive.</exception>
        public ItemDefinition(string id, int width, int height, string resourceFamily, string category,
            bool quickPocketEligible, string effectId)
        {
            RequireId(id, nameof(id));
            RequireId(resourceFamily, nameof(resourceFamily));
            RequireId(category, nameof(category));
            RequireId(effectId, nameof(effectId));
            if (width <= 0 || height <= 0)
                throw new InventoryValidationException(
                    new InventoryDiagnostic(InventoryErrorCode.InvalidFootprint, definitionId: id),
                    width <= 0 ? nameof(width) : nameof(height));
            Id = id;
            Width = width;
            Height = height;
            ResourceFamily = resourceFamily;
            Category = category;
            QuickPocketEligible = quickPocketEligible;
            EffectId = effectId;
        }

        /// <summary>Gets the shared stable content ID.</summary>
        public string Id { get; }
        /// <summary>Gets the fixed horizontal footprint in cells.</summary>
        public int Width { get; }
        /// <summary>Gets the fixed vertical footprint in cells.</summary>
        public int Height { get; }
        /// <summary>Gets the stable resource family without interpreting its effect.</summary>
        public string ResourceFamily { get; }
        /// <summary>Gets the stable content category.</summary>
        public string Category { get; }
        /// <summary>Gets explicit quick-pocket permission; this does not override pocket dimensions.</summary>
        public bool QuickPocketEligible { get; }
        /// <summary>Gets the referenced use-effect ID, not a Unity object or executable callback.</summary>
        public string EffectId { get; }

        internal static void RequireId(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value) || value != value.Trim())
                throw new InventoryValidationException(
                    new InventoryDiagnostic(InventoryErrorCode.InvalidId), parameterName);
        }
    }
}