using System;
using System.Linq;
using HallowBlaze.Core.Persistence.Dto;
using HallowBlaze.Core.State;

namespace HallowBlaze.Core.Persistence.Mapping
{
    /// <summary>Extends frozen v5 travel/equipment/landmarks with a validated exact whole-leg selection plan.</summary>
    public static class RunStateV6Mapper
    {
        /// <summary>Copies a complete boundary and validates provenance without sharing mutable DTO arrays.</summary>
        public static RunStateV6Dto ToDto(RunState run)
        {
            RunStateV5Dto previous = RunStateV5Mapper.ToDto(run);
            var dto = new RunStateV6Dto();
            Copy(previous, dto);
            dto.SchemaVersion = RunStateV6Dto.CurrentSchemaVersion;
            RouteBoardPlan plan = run.ActiveLeg?.BoardPlan;
            if (plan != null) dto.BoardSelectionPlan = new RouteBoardPlanDto
            {
                RunId = plan.RunId, RunSeed = plan.RunSeed, LegId = plan.LegId, EdgeId = plan.EdgeId,
                WorldId = plan.WorldId, WorldVersion = plan.WorldVersion, PublicationId = plan.PublicationId,
                PublicationVersion = plan.PublicationVersion, PublicationHash = plan.PublicationHash,
                PolicyId = plan.SelectionPolicyId, PolicyVersion = plan.SelectionPolicyVersion,
                Selections = plan.Selections.Select(selection => new RouteBoardSelectionDto
                {
                    SegmentId = selection.SegmentId, ConfigurationId = selection.ConfigurationId, ConfigurationVersion = selection.ConfigurationVersion,
                    TemplateId = selection.TemplateId, TemplateVersion = selection.TemplateVersion, VariantId = selection.VariantId,
                    TemplateHash = selection.TemplateHash, PoolId = selection.PoolId, PoolVersion = selection.PoolVersion, PoolHash = selection.PoolHash
                }).ToArray()
            };
            FromDto(dto);
            return dto;
        }

        /// <summary>Validates all plan/run/leg/configuration invariants before returning detached state; malformed plans are never silently dropped.</summary>
        /// <exception cref="PersistenceDataException">Any required field, policy or association is invalid.</exception>
        public static RunState FromDto(RunStateV6Dto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            DtoValidation.RequireCurrentSchemaVersion(dto.SchemaVersion, RunStateV6Dto.CurrentSchemaVersion);
            var previous = new RunStateV5Dto();
            Copy(dto, previous);
            previous.SchemaVersion = RunStateV5Dto.CurrentSchemaVersion;
            RunState run = RunStateV5Mapper.FromDto(previous);
            if (dto.BoardSelectionPlan == null) return run;
            try
            {
                RouteBoardPlanDto plan = dto.BoardSelectionPlan;
                var selections = DtoValidation.RequireArray(plan.Selections, "boardSelectionPlan.selections").Select(selection =>
                    selection == null ? throw new ArgumentException("Null board selection.") : new RouteBoardSelection(selection.SegmentId,
                        selection.ConfigurationId, selection.ConfigurationVersion, selection.TemplateId, selection.TemplateVersion,
                        selection.VariantId, selection.TemplateHash, selection.PoolId, selection.PoolVersion, selection.PoolHash));
                var restored = new RouteBoardPlan(plan.RunId, plan.RunSeed, plan.LegId, plan.EdgeId, plan.WorldId, plan.WorldVersion,
                    plan.PublicationId, plan.PublicationVersion, plan.PublicationHash, selections, plan.PolicyId, plan.PolicyVersion);
                ActiveRouteLegState leg = run.ActiveLeg ?? throw new ArgumentException("A board plan requires active travel.");
                var active = new ActiveRouteLegState(leg.LegId, leg.EdgeId, leg.FromNodeId, leg.ToNodeId, leg.Segments,
                    leg.DayNumber, leg.SegmentIndex, leg.CompletedSegmentCount, leg.IntroState, restored);
                run.RestoreTravelState(run.CompletedDayCount, run.WorldNodeId, run.TraversalSequence, run.TravelHistory, active);
            }
            catch (ArgumentException) { DtoValidation.ThrowInvalidValue("boardSelectionPlan"); }
            return run;
        }

        private static void Copy(RunStateV5Dto source, RunStateV5Dto target)
        {
            RunStateV5Mapper.CopyEquipment(source, target);
            target.ActiveLandmark = source.ActiveLandmark;
            target.CompletedLandmarks = source.CompletedLandmarks;
        }
    }
}