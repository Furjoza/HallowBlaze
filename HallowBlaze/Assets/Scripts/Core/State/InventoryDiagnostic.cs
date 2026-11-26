using System;

namespace HallowBlaze.Core.State
{
    /// <summary>Identifies a rejected item catalogue or inventory change without parsing messages.</summary>
    public enum InventoryErrorCode
    {
        /// <summary>No rule was violated.</summary>
        None,
        /// <summary>A stable textual identifier is empty or has surrounding whitespace.</summary>
        InvalidId,
        /// <summary>A rectangular footprint has a nonpositive dimension.</summary>
        InvalidFootprint,
        /// <summary>A catalogue entry is null.</summary>
        NullDefinition,
        /// <summary>More than one definition uses the same ordinal ID.</summary>
        DuplicateDefinitionId,
        /// <summary>An instance refers to content absent from the catalogue.</summary>
        UnknownDefinitionId,
        /// <summary>The proposed placement sequence is null.</summary>
        NullLayout,
        /// <summary>A proposed placement is null.</summary>
        NullPlacement,
        /// <summary>One instance ID appears in more than one placement.</summary>
        DuplicateInstanceId,
        /// <summary>A placement names an unsupported container.</summary>
        InvalidContainer,
        /// <summary>A rectangle or pocket anchor is outside its container.</summary>
        OutOfBounds,
        /// <summary>Two rectangles occupy at least one common main-grid cell.</summary>
        Overlap,
        /// <summary>A pocket item is ineligible or is not one cell wide and one to three tall.</summary>
        IncompatiblePocket,
        /// <summary>More than one item occupies a pocket.</summary>
        PocketOccupied,
        /// <summary>A repack adds, removes, or changes the definition of an owned instance.</summary>
        ItemSetMismatch,
        /// <summary>An owned definition refers to a required effect absent from the supplied content.</summary>
        UnknownEffectId,
        /// <summary>The expected revision no longer identifies the active inventory snapshot.</summary>
        StaleRevision,
        /// <summary>The revision cannot advance without reusing an already accepted value.</summary>
        RevisionExhausted,
        /// <summary>The run cannot accept inventory commits after a terminal outcome or zero resources.</summary>
        InvalidState
    }

    /// <summary>Immutable rejection detail; a failed operation leaves its original inventory unchanged.</summary>
    public sealed class InventoryDiagnostic
    {
        internal InventoryDiagnostic(InventoryErrorCode code, string instanceId = null, string definitionId = null)
        {
            Code = code;
            InstanceId = instanceId;
            DefinitionId = definitionId;
        }

        /// <summary>Gets the machine-readable cause; successful operations report <see cref="InventoryErrorCode.None"/>.</summary>
        public InventoryErrorCode Code { get; }
        /// <summary>Gets the offending run-local instance ID when one is available.</summary>
        public string InstanceId { get; }
        /// <summary>Gets the offending content ID when one is available.</summary>
        public string DefinitionId { get; }
    }

    /// <summary>Reports invalid item content or identity at construction, with typed diagnostic detail.</summary>
    public sealed class InventoryValidationException : ArgumentException
    {
        internal InventoryValidationException(InventoryDiagnostic diagnostic, string parameterName)
            : base("Item validation failed: " + diagnostic.Code + ".", parameterName)
        {
            Diagnostic = diagnostic;
        }

        /// <summary>Gets the rule and stable IDs responsible for the rejected construction.</summary>
        public InventoryDiagnostic Diagnostic { get; }
    }
}