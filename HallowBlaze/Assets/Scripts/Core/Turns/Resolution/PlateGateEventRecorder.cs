using System.Collections.Generic;
using HallowBlaze.Core.Board.State;
using HallowBlaze.Core.Turns.Contracts;

namespace HallowBlaze.Core.Turns.Resolution
{
    internal static class PlateGateEventRecorder
    {
        internal static void Append(BoardState board, ICollection<GameEvent> events)
        {
            foreach (PlateGateChange change in board.TakePlateGateChanges())
                if (change.Plate != null) events.Add(new PressurePlateChangedEvent(change.Plate));
                else events.Add(new GateChangedEvent(change.Gate));
        }
    }
}