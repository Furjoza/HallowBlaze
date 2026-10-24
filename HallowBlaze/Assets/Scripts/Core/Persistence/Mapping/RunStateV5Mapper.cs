using System;
using System.Linq;
using HallowBlaze.Core.Persistence.Dto;
using HallowBlaze.Core.State;

namespace HallowBlaze.Core.Persistence.Mapping
{
    /// <summary>Extends validated equipment/travel mapping without changing prior versions or profile data.</summary>
    public static class RunStateV5Mapper
    {
        /// <summary>Copies complete boundary data, preserving selected identities and completed results exactly.</summary>
        public static RunStateV5Dto ToDto(RunState run)
        {
            RunStateV4Dto previous = RunStateV4Mapper.ToDto(run);
            var dto = new RunStateV5Dto();
            CopyEquipment(previous, dto);
            dto.SchemaVersion = RunStateV5Dto.CurrentSchemaVersion;
            dto.ActiveLandmark = ToRecord(run.ActiveLandmark);
            dto.CompletedLandmarks = run.CompletedLandmarks.Select(ToRecord).ToArray();
            FromDto(dto);
            return dto;
        }

        /// <summary>Validates every identity/association before returning an independent run; malformed data is never repaired silently.</summary>
        public static RunState FromDto(RunStateV5Dto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            DtoValidation.RequireCurrentSchemaVersion(dto.SchemaVersion, RunStateV5Dto.CurrentSchemaVersion);
            var previous = new RunStateV4Dto();
            CopyEquipment(dto, previous);
            previous.SchemaVersion = RunStateV4Dto.CurrentSchemaVersion;
            RunState run = RunStateV4Mapper.FromDto(previous);
            try
            {
                run.RestoreLandmarkState(FromRecord(dto.ActiveLandmark),
                    DtoValidation.RequireArray(dto.CompletedLandmarks, "completedLandmarks").Select(record =>
                        FromRecord(record) ?? throw new ArgumentException("Null landmark result.")));
            }
            catch (ArgumentException) { DtoValidation.ThrowInvalidValue("landmarks"); }
            return run;
        }

        private static void CopyEquipment(RunStateV4Dto source, RunStateV4Dto target)
        {
            target.RunId = source.RunId; target.RunSeed = source.RunSeed; target.CompletedDayCount = source.CompletedDayCount;
            target.ReachedCheckpointNodeId = source.ReachedCheckpointNodeId; target.TraversalSequence = source.TraversalSequence;
            target.Health = source.Health; target.Food = source.Food; target.Status = source.Status;
            target.TravelHistory = source.TravelHistory; target.ActiveLeg = source.ActiveLeg; target.ToolSlots = source.ToolSlots;
            target.RandomAlgorithmId = source.RandomAlgorithmId; target.RandomAlgorithmVersion = source.RandomAlgorithmVersion;
            target.ToolInstanceSequence = source.ToolInstanceSequence;
        }

        private static LandmarkRecordDto ToRecord(LandmarkRecord record) => record == null ? null : new LandmarkRecordDto
        {
            RunId = record.Address.RunId, LegId = record.Address.LegId, EdgeId = record.Address.EdgeId, SegmentId = record.Address.SegmentId,
            Day = record.Address.DayNumber, ConfigurationId = record.Address.GenerationConfigurationId,
            GenerationVersion = record.Address.GenerationVersion, BoardSeed = record.Address.BoardSeed,
            TemplateId = record.TemplateId, TemplateVersion = record.TemplateVersion, VariantId = record.VariantId, OutcomeId = record.OutcomeId
        };

        private static LandmarkRecord FromRecord(LandmarkRecordDto dto) => dto == null ? null : new LandmarkRecord(new BoardAddress(
            dto.RunId, dto.LegId, dto.EdgeId, dto.SegmentId, dto.Day, dto.ConfigurationId, dto.GenerationVersion, dto.BoardSeed),
            dto.TemplateId, dto.TemplateVersion, dto.VariantId, dto.OutcomeId);
    }
}