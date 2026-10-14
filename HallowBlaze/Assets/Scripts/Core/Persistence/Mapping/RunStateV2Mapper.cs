using System;
using System.Collections.Generic;
using HallowBlaze.Core.Persistence.Dto;
using HallowBlaze.Core.State;

namespace HallowBlaze.Core.Persistence.Mapping
{
    /// <summary>Maps validated stable travel boundaries without World, Unity or private-field access.</summary>
    public static class RunStateV2Mapper
    {
        /// <summary>Copies and validates all run-owned authoritative fields into an independent v2 document.</summary>
        public static RunStateV2Dto ToDto(RunState run)
        {
            if (run == null)
                throw new ArgumentNullException(nameof(run));
            RunStateDto resources = RunStateMapper.ToDto(run);
            var dto = new RunStateV2Dto
            {
                RunId = run.RunId, RunSeed = run.RunSeed, Health = run.Health, Food = run.Food,
                Status = resources.Status, ToolSlots = resources.ToolSlots,
                CompletedDayCount = run.CompletedDayCount, ReachedCheckpointNodeId = run.WorldNodeId,
                TraversalSequence = run.TraversalSequence,
                TravelHistory = new TravelHistoryDto[run.TravelHistory.Count]
            };
            for (int index = 0; index < dto.TravelHistory.Length; index++)
            {
                TravelHistoryEntry entry = run.TravelHistory[index];
                dto.TravelHistory[index] = new TravelHistoryDto
                {
                    Kind = entry.Kind == TravelHistoryKind.LegacyCheckpoint ? "legacyCheckpoint" : "completedTraversal",
                    ToNodeId = entry.ToNodeId, LegId = entry.LegId, EdgeId = entry.EdgeId, FromNodeId = entry.FromNodeId,
                    DayNumber = entry.DayNumber, SegmentCount = entry.SegmentCount,
                    CompletedSegmentCount = entry.CompletedSegmentCount
                };
            }
            if (run.ActiveLeg != null)
            {
                ActiveRouteLegState leg = run.ActiveLeg;
                dto.ActiveLeg = new ActiveRouteLegDto
                {
                    LegId = leg.LegId, EdgeId = leg.EdgeId, FromNodeId = leg.FromNodeId, ToNodeId = leg.ToNodeId,
                    DayNumber = leg.DayNumber, SegmentIndex = leg.SegmentIndex,
                    CompletedSegmentCount = leg.CompletedSegmentCount,
                    IntroState = leg.IntroState == DayIntroState.Pending ? "pending" : "consumed",
                    Segments = new RouteSegmentDto[leg.Segments.Count]
                };
                for (int index = 0; index < leg.Segments.Count; index++)
                {
                    RouteSegmentState segment = leg.Segments[index];
                    dto.ActiveLeg.Segments[index] = new RouteSegmentDto
                    {
                        SegmentId = segment.SegmentId, GenerationConfigurationId = segment.GenerationConfigurationId,
                        GenerationVersion = segment.GenerationVersion
                    };
                }
            }
            FromDto(dto);
            return dto;
        }

        /// <summary>Reads historical v2 travel with its original layout RNG and controlled corruption diagnostics.</summary>
        /// <exception cref="PersistenceDataException">A field or cross-owner travel invariant is invalid.</exception>
        public static RunState FromDto(RunStateV2Dto dto)
        {
            return FromDto(dto, RunStateV2Dto.CurrentSchemaVersion,
                RunState.LegacyRandomAlgorithmId, RunState.LegacyRandomAlgorithmVersion);
        }

        internal static RunState FromDto(RunStateV2Dto dto, int schemaVersion,
            string randomAlgorithmId, int randomAlgorithmVersion)
        {
            if (dto == null)
                throw new ArgumentNullException(nameof(dto));
            DtoValidation.RequireCurrentSchemaVersion(dto.SchemaVersion, schemaVersion);
            DtoValidation.RequireNonNegative(dto.CompletedDayCount, "completedDayCount");
            DtoValidation.RequireNonNegative(dto.TraversalSequence, "traversalSequence");
            DtoValidation.RequireStableId(dto.ReachedCheckpointNodeId, "reachedCheckpointNodeId");
            RunState run = RunStateMapper.FromDto(new RunStateDto
            {
                RunId = dto.RunId, RunSeed = dto.RunSeed, Health = dto.Health, Food = dto.Food, Status = dto.Status,
                ToolSlots = dto.ToolSlots, CurrentDay = dto.CompletedDayCount, WorldNodeId = dto.ReachedCheckpointNodeId,
                Route = Array.Empty<string>()
            }, randomAlgorithmId, randomAlgorithmVersion);
            var history = new List<TravelHistoryEntry>();
            TravelHistoryDto[] entries = DtoValidation.RequireArray(dto.TravelHistory, "travelHistory");
            for (int index = 0; index < entries.Length; index++)
            {
                TravelHistoryDto entry = entries[index];
                string path = "travelHistory[" + index + "]";
                if (entry == null)
                    DtoValidation.ThrowInvalidValue(path);
                DtoValidation.RequireStableId(entry.ToNodeId, path + ".toNodeId");
                if (entry.Kind == "legacyCheckpoint")
                {
                    if (entry.LegId != null || entry.EdgeId != null || entry.FromNodeId != null ||
                        entry.DayNumber != null || entry.SegmentCount != null || entry.CompletedSegmentCount != null)
                        DtoValidation.ThrowInvalidValue(path);
                    history.Add(TravelHistoryEntry.LegacyCheckpoint(entry.ToNodeId));
                }
                else if (entry.Kind == "completedTraversal")
                {
                    DtoValidation.RequireStableId(entry.LegId, path + ".legId");
                    DtoValidation.RequireStableId(entry.EdgeId, path + ".edgeId");
                    DtoValidation.RequireStableId(entry.FromNodeId, path + ".fromNodeId");
                    if (!entry.DayNumber.HasValue || entry.DayNumber <= 0 || !entry.SegmentCount.HasValue ||
                        entry.SegmentCount <= 0 || entry.CompletedSegmentCount != entry.SegmentCount)
                        DtoValidation.ThrowInvalidValue(path);
                    history.Add(TravelHistoryEntry.Completed(entry.LegId, entry.EdgeId, entry.FromNodeId,
                        entry.ToNodeId, entry.DayNumber.Value, entry.SegmentCount.Value));
                }
                else
                    DtoValidation.ThrowInvalidValue(path + ".kind");
            }
            ActiveRouteLegState leg = dto.ActiveLeg == null ? null : ReadLeg(dto.ActiveLeg);
            try
            {
                run.RestoreTravelState(dto.CompletedDayCount, dto.ReachedCheckpointNodeId, dto.TraversalSequence, history, leg);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is OverflowException)
            {
                DtoValidation.ThrowInvalidValue("travelState");
            }
            return run;
        }

        private static ActiveRouteLegState ReadLeg(ActiveRouteLegDto dto)
        {
            DtoValidation.RequireStableId(dto.LegId, "activeLeg.legId");
            DtoValidation.RequireStableId(dto.EdgeId, "activeLeg.edgeId");
            DtoValidation.RequireStableId(dto.FromNodeId, "activeLeg.fromNodeId");
            DtoValidation.RequireStableId(dto.ToNodeId, "activeLeg.toNodeId");
            DtoValidation.RequirePositive(dto.DayNumber, "activeLeg.dayNumber");
            DayIntroState intro = DayIntroState.Pending;
            if (dto.IntroState == "consumed")
                intro = DayIntroState.Consumed;
            else if (dto.IntroState != "pending")
                DtoValidation.ThrowInvalidValue("activeLeg.introState");
            var segments = new List<RouteSegmentState>();
            RouteSegmentDto[] source = DtoValidation.RequireArray(dto.Segments, "activeLeg.segments");
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < source.Length; index++)
            {
                RouteSegmentDto segment = source[index];
                string path = "activeLeg.segments[" + index + "]";
                if (segment == null)
                    DtoValidation.ThrowInvalidValue(path);
                DtoValidation.RequireStableId(segment.SegmentId, path + ".segmentId");
                DtoValidation.RequireStableId(segment.GenerationConfigurationId, path + ".generationConfigurationId");
                DtoValidation.RequirePositive(segment.GenerationVersion, path + ".generationVersion");
                if (!ids.Add(segment.SegmentId))
                    throw new PersistenceDataException(PersistenceDataError.DuplicateId, path + ".segmentId", "Duplicate segment identity.");
                segments.Add(new RouteSegmentState(segment.SegmentId, segment.GenerationConfigurationId, segment.GenerationVersion));
            }
            if (dto.SegmentIndex < 0 || dto.SegmentIndex >= segments.Count ||
                dto.CompletedSegmentCount != dto.SegmentIndex || (intro == DayIntroState.Pending && dto.SegmentIndex != 0))
                DtoValidation.ThrowInvalidValue("activeLeg.segmentIndex");
            return new ActiveRouteLegState(dto.LegId, dto.EdgeId, dto.FromNodeId, dto.ToNodeId,
                segments, dto.DayNumber, dto.SegmentIndex, dto.CompletedSegmentCount, intro);
        }
    }
}