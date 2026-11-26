using System;
using HallowBlaze.Core.Persistence.Dto;
using HallowBlaze.Core.State;

namespace HallowBlaze.Core.Persistence.Mapping
{
    /// <summary>Maps bounded satiety and persisted resource rules while reusing validated travel/equipment/selection mapping.</summary>
    public static class RunStateV7Mapper
    {
        /// <summary>Copies the complete boundary and its resource configuration without a second legacy resource field.</summary>
        /// <exception cref="ArgumentNullException">The run is null.</exception>
        public static RunStateV7Dto ToDto(RunState run)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));
            RunStateV6Dto previous = RunStateV6Mapper.ToDto(run);
            ResourceRules rules = run.ResourceRules;
            var dto = new RunStateV7Dto
            {
                RunId = previous.RunId, RunSeed = previous.RunSeed, CompletedDayCount = previous.CompletedDayCount,
                ReachedCheckpointNodeId = previous.ReachedCheckpointNodeId, TraversalSequence = previous.TraversalSequence,
                Health = run.Health, Satiety = run.Satiety, Status = previous.Status, ToolSlots = previous.ToolSlots,
                TravelHistory = previous.TravelHistory, ActiveLeg = previous.ActiveLeg,
                RandomAlgorithmId = previous.RandomAlgorithmId, RandomAlgorithmVersion = previous.RandomAlgorithmVersion,
                ToolInstanceSequence = previous.ToolInstanceSequence, ActiveLandmark = previous.ActiveLandmark,
                CompletedLandmarks = previous.CompletedLandmarks, BoardSelectionPlan = previous.BoardSelectionPlan,
                ResourceRules = new ResourceRulesDto
                {
                    Version = ResourceRules.CurrentVersion, MaximumHealth = rules.MaximumHealth, MaximumSatiety = rules.MaximumSatiety,
                    NormalSatietyThreshold = rules.NormalSatietyThreshold, NormalActionCost = rules.NormalActionCost,
                    SatedActionCost = rules.SatedActionCost
                }
            };
            FromDto(dto);
            return dto;
        }

        /// <summary>Strictly validates the current resource ranges and rule version; current corruption is never clamped as migration.</summary>
        /// <exception cref="ArgumentNullException">The document is null.</exception>
        /// <exception cref="PersistenceDataException">Resources, rules, or a retained boundary field are invalid.</exception>
        public static RunState FromDto(RunStateV7Dto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            DtoValidation.RequireCurrentSchemaVersion(dto.SchemaVersion, RunStateV7Dto.CurrentSchemaVersion);
            if (dto.ResourceRules == null) DtoValidation.ThrowInvalidValue("resourceRules");
            ResourceRulesDto values = dto.ResourceRules;
            if (values.Version != ResourceRules.CurrentVersion) DtoValidation.ThrowInvalidValue("resourceRules.version");
            ResourceRules rules = null;
            try
            {
                rules = new ResourceRules(values.MaximumHealth, values.MaximumSatiety, values.NormalSatietyThreshold,
                    values.NormalActionCost, values.SatedActionCost);
            }
            catch (ArgumentException) { DtoValidation.ThrowInvalidValue("resourceRules"); }
            if (dto.Health < 0 || dto.Health > rules.MaximumHealth) DtoValidation.ThrowInvalidValue("health");
            if (dto.Satiety < 0 || dto.Satiety > rules.MaximumSatiety) DtoValidation.ThrowInvalidValue("satiety");
            var boundary = new RunStateV6Dto
            {
                RunId = dto.RunId, RunSeed = dto.RunSeed, CompletedDayCount = dto.CompletedDayCount,
                ReachedCheckpointNodeId = dto.ReachedCheckpointNodeId, TraversalSequence = dto.TraversalSequence,
                Health = dto.Health, Food = dto.Satiety, Status = dto.Status, ToolSlots = dto.ToolSlots,
                TravelHistory = dto.TravelHistory, ActiveLeg = dto.ActiveLeg,
                RandomAlgorithmId = dto.RandomAlgorithmId, RandomAlgorithmVersion = dto.RandomAlgorithmVersion,
                ToolInstanceSequence = dto.ToolInstanceSequence, ActiveLandmark = dto.ActiveLandmark,
                CompletedLandmarks = dto.CompletedLandmarks, BoardSelectionPlan = dto.BoardSelectionPlan
            };
            return RunStateV6Mapper.FromDto(boundary, rules);
        }
    }
}