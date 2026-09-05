using System;
using System.Collections.Generic;
using HallowBlaze.Core.Persistence.Dto;
using HallowBlaze.Core.State;

namespace HallowBlaze.Core.Persistence.Mapping
{
    public static class ProfileStateMapper
    {
        public static ProfileStateDto ToDto(ProfileState profile)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));

            ProfileRunSummaryDto[] summaryDtos =
                new ProfileRunSummaryDto[profile.RunSummaries.Count];
            for (int index = 0; index < summaryDtos.Length; index++)
            {
                ProfileRunSummary summary = profile.RunSummaries[index];
                summaryDtos[index] = new ProfileRunSummaryDto
                {
                    RunId = summary.RunId,
                    DaysSurvived = summary.DaysSurvived,
                    Status = ToStatusId(summary.Status)
                };
            }

            return new ProfileStateDto
            {
                SchemaVersion = ProfileStateDto.CurrentSchemaVersion,
                ProfileId = profile.ProfileId,
                WorldDefinitionId = profile.WorldDefinitionId,
                WorldDefinitionVersion = profile.WorldDefinitionVersion,
                DiscoveredNodeIds = Copy(profile.DiscoveredNodeIds),
                DiscoveredEdgeIds = Copy(profile.DiscoveredEdgeIds),
                DiscoveredFactIds = Copy(profile.DiscoveredFactIds),
                PersistentNoteIds = Copy(profile.PersistentNoteIds),
                RunSummaries = summaryDtos
            };
        }

        public static ProfileState FromDto(ProfileStateDto dto)
        {
            if (dto == null)
                throw new ArgumentNullException(nameof(dto));

            DtoValidation.RequireCurrentSchemaVersion(
                dto.SchemaVersion,
                ProfileStateDto.CurrentSchemaVersion);
            DtoValidation.RequireStableId(dto.ProfileId, "profileId");
            DtoValidation.RequireStableId(dto.WorldDefinitionId, "worldDefinitionId");
            DtoValidation.RequirePositive(
                dto.WorldDefinitionVersion,
                "worldDefinitionVersion");

            string[] nodeIds = DtoValidation.RequireArray(
                dto.DiscoveredNodeIds,
                "discoveredNodeIds");
            string[] edgeIds = DtoValidation.RequireArray(
                dto.DiscoveredEdgeIds,
                "discoveredEdgeIds");
            string[] factIds = DtoValidation.RequireArray(
                dto.DiscoveredFactIds,
                "discoveredFactIds");
            string[] noteIds = DtoValidation.RequireArray(
                dto.PersistentNoteIds,
                "persistentNoteIds");
            ProfileRunSummaryDto[] summaryDtos = DtoValidation.RequireArray(
                dto.RunSummaries,
                "runSummaries");

            DtoValidation.ValidateStableIds(nodeIds, "discoveredNodeIds", true);
            DtoValidation.ValidateStableIds(edgeIds, "discoveredEdgeIds", true);
            DtoValidation.ValidateStableIds(factIds, "discoveredFactIds", true);
            DtoValidation.ValidateStableIds(noteIds, "persistentNoteIds", true);

            ProfileRunSummary[] summaries = ValidateSummaries(summaryDtos);
            ProfileState profile = new ProfileState(
                dto.ProfileId,
                dto.WorldDefinitionId,
                dto.WorldDefinitionVersion);

            AddIds(nodeIds, profile.DiscoverNode);
            AddIds(edgeIds, profile.DiscoverEdge);
            AddIds(factIds, profile.DiscoverFact);
            AddIds(noteIds, profile.AddPersistentNote);
            foreach (ProfileRunSummary summary in summaries)
                profile.RecordRunSummary(summary);

            return profile;
        }

        private static ProfileRunSummary[] ValidateSummaries(
            ProfileRunSummaryDto[] summaryDtos)
        {
            ProfileRunSummary[] summaries = new ProfileRunSummary[summaryDtos.Length];
            HashSet<string> runIds = new HashSet<string>(StringComparer.Ordinal);
            long totalDaysSurvived = 0;

            for (int index = 0; index < summaryDtos.Length; index++)
            {
                ProfileRunSummaryDto summaryDto = summaryDtos[index];
                string itemPath = $"runSummaries[{index}]";
                if (summaryDto == null)
                {
                    throw new PersistenceDataException(
                        PersistenceDataError.MissingField,
                        itemPath,
                        "A required run summary is missing.");
                }

                DtoValidation.RequireStableId(summaryDto.RunId, $"{itemPath}.runId");
                if (!runIds.Add(summaryDto.RunId))
                {
                    throw new PersistenceDataException(
                        PersistenceDataError.DuplicateId,
                        $"{itemPath}.runId",
                        "Run summary IDs must be unique.");
                }

                DtoValidation.RequireNonNegative(
                    summaryDto.DaysSurvived,
                    $"{itemPath}.daysSurvived");
                totalDaysSurvived += summaryDto.DaysSurvived;
                if (totalDaysSurvived > int.MaxValue)
                    DtoValidation.ThrowInvalidValue("runSummaries.daysSurvived");

                RunStatus status = ParseTerminalStatus(
                    summaryDto.Status,
                    $"{itemPath}.status");
                summaries[index] = new ProfileRunSummary(
                    summaryDto.RunId,
                    summaryDto.DaysSurvived,
                    status);
            }

            return summaries;
        }

        private static RunStatus ParseTerminalStatus(string statusId, string fieldPath)
        {
            switch (statusId)
            {
                case RunStatusIds.Dead:
                    return RunStatus.Dead;
                case RunStatusIds.Won:
                    return RunStatus.Won;
                default:
                    DtoValidation.ThrowInvalidValue(fieldPath);
                    return default;
            }
        }

        private static string ToStatusId(RunStatus status)
        {
            switch (status)
            {
                case RunStatus.Dead:
                    return RunStatusIds.Dead;
                case RunStatus.Won:
                    return RunStatusIds.Won;
                default:
                    throw new ArgumentException(
                        "Profile summaries must have a terminal run status.",
                        nameof(status));
            }
        }

        private static string[] Copy(IReadOnlyList<string> values)
        {
            string[] copy = new string[values.Count];
            for (int index = 0; index < values.Count; index++)
                copy[index] = values[index];

            return copy;
        }

        private static void AddIds(string[] ids, Func<string, bool> add)
        {
            foreach (string id in ids)
                add(id);
        }
    }
}