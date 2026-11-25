using System;
using System.Collections.Generic;

namespace HallowBlaze.Core.State
{
    public sealed partial class RunState
    {
        /// <summary>Gets the run's immutable carried-item snapshot, separate from tool slots and profile knowledge.</summary>
        public InventoryState Inventory { get; private set; }
        /// <summary>Gets the committed monotonic revision; even an accepted no-op commit advances it exactly once.</summary>
        public int InventoryRevision { get; private set; }
        /// <summary>Gets immutable trusted definitions/effects; saves contain references, not executable content.</summary>
        public InventoryContent InventoryContent { get; private set; }

        /// <summary>Commits one fully validated layout at the expected revision without charging or resolving a turn.</summary>
        /// <param name="expectedRevision">The snapshot observed by the owning resolver; a reused value rejects for free.</param>
        /// <param name="placements">Complete final ownership; action/transfer validation belongs to the future command resolver.</param>
        /// <param name="diagnostic">Typed rejection detail, or no error.</param>
        /// <returns>False without mutation on stale, terminal, exhausted or invalid input; otherwise advances once, even for no-op.</returns>
        public bool TryCommitInventory(int expectedRevision, IEnumerable<InventoryPlacement> placements,
            out InventoryDiagnostic diagnostic)
        {
            if (Status != RunStatus.Active || Health <= 0 || Satiety <= 0)
                diagnostic = new InventoryDiagnostic(InventoryErrorCode.InvalidState);
            else if (expectedRevision != InventoryRevision)
                diagnostic = new InventoryDiagnostic(InventoryErrorCode.StaleRevision);
            else if (InventoryRevision == int.MaxValue)
                diagnostic = new InventoryDiagnostic(InventoryErrorCode.RevisionExhausted);
            else if (InventoryContent.TryCreateInventory(placements, out InventoryState next, out diagnostic))
            {
                Inventory = next;
                InventoryRevision++;
                return true;
            }
            return false;
        }

        /// <summary>Restores a detached trusted boundary, including terminal saves, without replaying actions or advancing revision.</summary>
        /// <param name="content">Trusted content for exact definition/effect resolution.</param>
        /// <param name="revision">Nonnegative saved revision; reset alone returns it to zero for a fresh run.</param>
        /// <param name="placements">Complete saved layout; all validation precedes mutation.</param>
        /// <exception cref="ArgumentNullException">The trusted content is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The revision is negative.</exception>
        /// <exception cref="InventoryValidationException">Identity, geometry or a required effect is invalid.</exception>
        public void RestoreInventoryState(InventoryContent content, int revision, IEnumerable<InventoryPlacement> placements)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            if (revision < 0) throw new ArgumentOutOfRangeException(nameof(revision));
            if (!content.TryCreateInventory(placements, out InventoryState restored, out InventoryDiagnostic diagnostic))
                throw new InventoryValidationException(diagnostic, nameof(placements));
            InventoryContent = content;
            Inventory = restored;
            InventoryRevision = revision;
        }
    }
}