using Newtonsoft.Json;

namespace HallowBlaze.Core.Persistence.Dto
{
    /// <summary>Complete boundary with separate carried-item ownership; no board, draft or profile inventory is persisted.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class RunStateV8Dto : RunStateV7Dto
    {
        /// <summary>Inventory-aware schema; versions 1-7 migrate with empty containers and revision zero.</summary>
        public new const int CurrentSchemaVersion = 8;
        /// <summary>Creates a current document rather than a v7 resource-only document.</summary>
        public RunStateV8Dto() { SchemaVersion = CurrentSchemaVersion; }
        /// <summary>Gets or sets required carried ownership and its durable revision, separate from tool slots.</summary>
        [JsonProperty("inventory", Required = Required.Always, Order = 20)] public InventoryDto Inventory { get; set; }
    }

    /// <summary>Stable instance identities have exactly one grid placement or pocket reference.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class InventoryDto
    {
        /// <summary>Gets or sets the nonnegative monotonic committed revision, including accepted no-ops.</summary>
        [JsonProperty("revision", Required = Required.Always)] public int Revision { get; set; }
        /// <summary>Gets or sets unique owned instances; definitions and effects resolve only from trusted content.</summary>
        [JsonProperty("items", Required = Required.Always)] public ItemInstanceDto[] Items { get; set; }
        /// <summary>Gets or sets anchors of unrotated rectangles in the fixed 3x3 grid, never a per-cell item copy.</summary>
        [JsonProperty("mainGrid", Required = Required.Always)] public InventoryAnchorDto[] MainGrid { get; set; }
        /// <summary>Gets or sets the single left-pocket instance ID, explicitly null when empty.</summary>
        [JsonProperty("leftPocket", Required = Required.AllowNull)] public string LeftPocket { get; set; }
        /// <summary>Gets or sets the single right-pocket instance ID, explicitly null when empty.</summary>
        [JsonProperty("rightPocket", Required = Required.AllowNull)] public string RightPocket { get; set; }
    }

    /// <summary>Run-local item identity and stable content reference, without Unity objects or executable effect data.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class ItemInstanceDto
    {
        /// <summary>Gets or sets the unique textual identity within the run's carried inventory.</summary>
        [JsonProperty("instanceId", Required = Required.Always)] public string InstanceId { get; set; }
        /// <summary>Gets or sets the exact trusted definition ID; missing definitions reject the entire boundary.</summary>
        [JsonProperty("definitionId", Required = Required.Always)] public string DefinitionId { get; set; }
    }

    /// <summary>One grid owner for an instance, expressed in column/row coordinates.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class InventoryAnchorDto
    {
        /// <summary>Gets or sets an owned instance ID, not a definition ID.</summary>
        [JsonProperty("instanceId", Required = Required.Always)] public string InstanceId { get; set; }
        /// <summary>Gets or sets the horizontal anchor in the fixed grid.</summary>
        [JsonProperty("column", Required = Required.Always)] public int Column { get; set; }
        /// <summary>Gets or sets the vertical anchor in the fixed grid.</summary>
        [JsonProperty("row", Required = Required.Always)] public int Row { get; set; }
    }
}