using System;
using System.Collections.Generic;
using HallowBlaze.Core.Persistence.Dto;
using HallowBlaze.Core.State;

namespace HallowBlaze.Core.Persistence.Mapping
{
    /// <summary>
    /// Converts profile state between the domain model and the current persistence schema.
    /// </summary>
    public static class ProfileStateMapper
    {
        /// <summary>
        /// Creates a schema v2 DTO while preserving first-discovery and insertion order.
        /// </summary>
        /// <param name="profile">The validated profile to serialize.</param>
        /// <returns>A detached persistence DTO.</returns>
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

            NodeDiscoveryDto[] nodeDtos =
                new NodeDiscoveryDto[profile.NodeDiscoveries.Count];
            for (int index = 0; index < nodeDtos.Length; index++)
            {
                NodeDiscovery discovery = profile.NodeDiscoveries[index];
                nodeDtos[index] = new NodeDiscoveryDto
                {
                    NodeId = discovery.NodeId,
                    State = ToNodeStateId(discovery.State)
                };
            }

            EdgeDiscoveryDto[] edgeDtos =
                new EdgeDiscoveryDto[profile.EdgeDiscoveries.Count];
            for (int index = 0; index < edgeDtos.Length; index++)
            {
                EdgeDiscovery discovery = profile.EdgeDiscoveries[index];
                edgeDtos[index] = new EdgeDiscoveryDto
                {
                    EdgeId = discovery.EdgeId,
                    State = ToEdgeStateId(discovery.State)
                };
            }

            return new ProfileStateDto
            {
                SchemaVersion = ProfileStateDto.CurrentSchemaVersion,
                ProfileId = profile.ProfileId,
                WorldDefinitionId = profile.WorldDefinitionId,
                WorldDefinitionVersion = profile.WorldDefinitionVersion,
                NodeDiscoveries = nodeDtos,
                EdgeDiscoveries = edgeDtos,
                DiscoveredFactIds = Copy(profile.DiscoveredFactIds),
                PersistentNoteIds = Copy(profile.PersistentNoteIds),
                RunSummaries = summaryDtos
            };
        }

        /// <summary>
        /// Validates and materializes a profile from a complete schema v2 DTO.
        /// </summary>
        /// <param name="dto">The profile DTO to validate and materialize.</param>
        /// <returns>A profile containing the persisted durable knowledge.</returns>
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

            NodeDiscoveryDto[] nodeDtos = DtoValidation.RequireArray(
                dto.NodeDiscoveries,
                "nodeDiscoveries");
            EdgeDiscoveryDto[] edgeDtos = DtoValidation.RequireArray(
                dto.EdgeDiscoveries,
                "edgeDiscoveries");
            string[] factIds = DtoValidation.RequireArray(
                dto.DiscoveredFactIds,
                "discoveredFactIds");
            string[] noteIds = DtoValidation.RequireArray(
                dto.PersistentNoteIds,
                "persistentNoteIds");
            ProfileRunSummaryDto[] summaryDtos = DtoValidation.RequireArray(
                dto.RunSummaries,
                "runSummaries");

            DtoValidation.ValidateStableIds(factIds, "discoveredFactIds", true);
            DtoValidation.ValidateStableIds(noteIds, "persistentNoteIds", true);

            ProfileRunSummary[] summaries = ValidateSummaries(summaryDtos);
            ProfileState profile = new ProfileState(
                dto.ProfileId,
                dto.WorldDefinitionId,
                dto.WorldDefinitionVersion);

            AddNodeDiscoveries(nodeDtos, profile);
            AddEdgeDiscoveries(edgeDtos, profile);
            AddIds(factIds, profile.DiscoverFact);
            AddIds(noteIds, profile.AddPersistentNote);
            foreach (ProfileRunSummary summary in summaries)
                profile.RecordRunSummary(summary);

            return profile;
        }

        private static void AddNodeDiscoveries(
            NodeDiscoveryDto[] discoveryDtos,
            ProfileState profile)
        {
            HashSet<string> nodeIds = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < discoveryDtos.Length; index++)
            {
                NodeDiscoveryDto discoveryDto = discoveryDtos[index];
                string itemPath = $"nodeDiscoveries[{index}]";
                if (discoveryDto == null)
                    ThrowMissingItem(itemPath, "A required node discovery is missing.");

                DtoValidation.RequireStableId(discoveryDto.NodeId, $"{itemPath}.nodeId");
                if (!nodeIds.Add(discoveryDto.NodeId))
                    ThrowDuplicateId($"{itemPath}.nodeId", "Node discovery IDs must be unique.");

                profile.AdvanceNodeDiscovery(
                    discoveryDto.NodeId,
                    ParseNodeState(discoveryDto.State, $"{itemPath}.state"));
            }
        }

        private static void AddEdgeDiscoveries(
            EdgeDiscoveryDto[] discoveryDtos,
            ProfileState profile)
        {
            HashSet<string> edgeIds = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < discoveryDtos.Length; index++)
            {
                EdgeDiscoveryDto discoveryDto = discoveryDtos[index];
                string itemPath = $"edgeDiscoveries[{index}]";
                if (discoveryDto == null)
                    ThrowMissingItem(itemPath, "A required edge discovery is missing.");

                DtoValidation.RequireStableId(discoveryDto.EdgeId, $"{itemPath}.edgeId");
                if (!edgeIds.Add(discoveryDto.EdgeId))
                    ThrowDuplicateId($"{itemPath}.edgeId", "Edge discovery IDs must be unique.");

                profile.AdvanceEdgeDiscovery(
                    discoveryDto.EdgeId,
                    ParseEdgeState(discoveryDto.State, $"{itemPath}.state"));
            }
        }

        private static NodeDiscoveryState ParseNodeState(
            string stateId,
            string fieldPath)
        {
            switch (stateId)
            {
                case "rumored":
                    return NodeDiscoveryState.Rumored;
                case "sighted":
                    return NodeDiscoveryState.Sighted;
                case "visited":
                    return NodeDiscoveryState.Visited;
                default:
                    DtoValidation.ThrowInvalidValue(fieldPath);
                    return default;
            }
        }

        private static EdgeDiscoveryState ParseEdgeState(
            string stateId,
            string fieldPath)
        {
            switch (stateId)
            {
                case "sighted":
                    return EdgeDiscoveryState.Sighted;
                case "traversed":
                    return EdgeDiscoveryState.Traversed;
                default:
                    DtoValidation.ThrowInvalidValue(fieldPath);
                    return default;
            }
        }

        private static string ToNodeStateId(NodeDiscoveryState state)
        {
            switch (state)
            {
                case NodeDiscoveryState.Rumored:
                    return "rumored";
                case NodeDiscoveryState.Sighted:
                    return "sighted";
                case NodeDiscoveryState.Visited:
                    return "visited";
                default:
                    throw new ArgumentException(
                        "Unknown nodes are not persisted.",
                        nameof(state));
            }
        }

        private static string ToEdgeStateId(EdgeDiscoveryState state)
        {
            switch (state)
            {
                case EdgeDiscoveryState.Sighted:
                    return "sighted";
                case EdgeDiscoveryState.Traversed:
                    return "traversed";
                default:
                    throw new ArgumentException(
                        "Unknown edges are not persisted.",
                        nameof(state));
            }
        }

        private static void ThrowMissingItem(string fieldPath, string message)
        {
            throw new PersistenceDataException(
                PersistenceDataError.MissingField,
                fieldPath,
                message);
        }

        private static void ThrowDuplicateId(string fieldPath, string message)
        {
            throw new PersistenceDataException(
                PersistenceDataError.DuplicateId,
                fieldPath,
                message);
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