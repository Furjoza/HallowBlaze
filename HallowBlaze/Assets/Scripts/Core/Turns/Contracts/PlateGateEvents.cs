using System;
using HallowBlaze.Core.Board.State;

namespace HallowBlaze.Core.Turns.Contracts
{
    /// <summary>Records committed pressure at its causal position in the turn, not the board's later final state.</summary>
    public sealed class PressurePlateChangedEvent : GameEvent
    {
        /// <summary>Captures an immutable changed plate snapshot for ordered read-only presentation.</summary>
        public PressurePlateChangedEvent(PressurePlateState plate) { Plate = plate ?? throw new ArgumentNullException(nameof(plate)); }
        /// <summary>Gets the exact link, cell and pressed state produced by the mutation.</summary>
        public PressurePlateState Plate { get; }
        /// <inheritdoc />
        public override string EventType => "PressurePlateChanged";
    }

    /// <summary>Records derived gate walkability before the next actor/environment/planning opportunity.</summary>
    public sealed class GateChangedEvent : GameEvent
    {
        /// <summary>Captures the already committed gate state; animation cannot change its legality.</summary>
        public GateChangedEvent(GateState gate) { Gate = gate ?? throw new ArgumentNullException(nameof(gate)); }
        /// <summary>Gets the exact link, combination rule, cell and open state at this mutation.</summary>
        public GateState Gate { get; }
        /// <inheritdoc />
        public override string EventType => "GateChanged";
    }
}