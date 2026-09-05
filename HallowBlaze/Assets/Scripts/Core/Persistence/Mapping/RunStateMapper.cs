using System;
using HallowBlaze.Core.Persistence.Dto;
using HallowBlaze.Core.State;

namespace HallowBlaze.Core.Persistence.Mapping
{
    public static class RunStateMapper
    {
        public static RunStateDto ToDto(RunState run)
        {
            if (run == null)
                throw new ArgumentNullException(nameof(run));

            ToolSlotDto[] toolSlots = new ToolSlotDto[run.ToolSlots.Count];
            for (int index = 0; index < toolSlots.Length; index++)
            {
                ToolSlotState toolSlot = run.ToolSlots[index];
                toolSlots[index] = new ToolSlotDto
                {
                    ToolId = toolSlot.ToolId,
                    RemainingUses = toolSlot.RemainingUses
                };
            }

            return new RunStateDto
            {
                SchemaVersion = RunStateDto.CurrentSchemaVersion,
                RunId = run.RunId,
                RunSeed = run.RunSeed,
                CurrentDay = run.CurrentDay,
                WorldNodeId = run.WorldNodeId,
                Health = run.Health,
                Food = run.Food,
                Status = ToStatusId(run.Status),
                ToolSlots = toolSlots,
                Route = Copy(run.Route)
            };
        }

        public static RunState FromDto(RunStateDto dto)
        {
            if (dto == null)
                throw new ArgumentNullException(nameof(dto));

            DtoValidation.RequireCurrentSchemaVersion(
                dto.SchemaVersion,
                RunStateDto.CurrentSchemaVersion);
            DtoValidation.RequireStableId(dto.RunId, "runId");
            DtoValidation.RequireNonNegative(dto.CurrentDay, "currentDay");
            DtoValidation.RequireStableId(dto.WorldNodeId, "worldNodeId");
            DtoValidation.RequireNonNegative(dto.Health, "health");
            DtoValidation.RequireNonNegative(dto.Food, "food");
            RunStatus status = ParseStatus(dto.Status, "status");

            ToolSlotDto[] toolSlotDtos = DtoValidation.RequireArray(
                dto.ToolSlots,
                "toolSlots");
            if (toolSlotDtos.Length != RunState.ToolSlotCount)
                DtoValidation.ThrowInvalidValue("toolSlots");

            ToolSlotState[] toolSlots = ValidateToolSlots(toolSlotDtos);
            string[] route = DtoValidation.RequireArray(dto.Route, "route");
            DtoValidation.ValidateStableIds(route, "route", false);

            RunStateConfiguration configuration = new RunStateConfiguration(
                dto.Health,
                dto.Food,
                dto.CurrentDay,
                dto.WorldNodeId);
            RunState run = new RunState(dto.RunId, dto.RunSeed, configuration);

            foreach (string nodeId in route)
                run.RecordRouteNode(nodeId);
            for (int index = 0; index < toolSlots.Length; index++)
            {
                if (!toolSlots[index].IsEmpty)
                    run.EquipTool(index, toolSlots[index]);
            }

            if (status == RunStatus.Dead)
                run.MarkDead();
            else if (status == RunStatus.Won)
                run.MarkWon();

            return run;
        }

        private static ToolSlotState[] ValidateToolSlots(ToolSlotDto[] toolSlotDtos)
        {
            ToolSlotState[] toolSlots = new ToolSlotState[toolSlotDtos.Length];
            for (int index = 0; index < toolSlotDtos.Length; index++)
            {
                ToolSlotDto toolSlotDto = toolSlotDtos[index];
                string itemPath = $"toolSlots[{index}]";
                if (toolSlotDto == null)
                {
                    throw new PersistenceDataException(
                        PersistenceDataError.MissingField,
                        itemPath,
                        "A required tool slot is missing.");
                }

                DtoValidation.RequireNonNegative(
                    toolSlotDto.RemainingUses,
                    $"{itemPath}.remainingUses");
                if (toolSlotDto.ToolId == null)
                {
                    throw new PersistenceDataException(
                        PersistenceDataError.MissingField,
                        $"{itemPath}.toolId",
                        "A required tool ID field is missing.");
                }

                if (toolSlotDto.ToolId.Length == 0)
                {
                    if (toolSlotDto.RemainingUses != 0)
                        DtoValidation.ThrowInvalidValue(itemPath);

                    toolSlots[index] = ToolSlotState.Empty;
                    continue;
                }

                DtoValidation.RequireStableId(toolSlotDto.ToolId, $"{itemPath}.toolId");
                toolSlots[index] = new ToolSlotState(
                    toolSlotDto.ToolId,
                    toolSlotDto.RemainingUses);
            }

            return toolSlots;
        }

        private static RunStatus ParseStatus(string statusId, string fieldPath)
        {
            switch (statusId)
            {
                case RunStatusIds.Active:
                    return RunStatus.Active;
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
                case RunStatus.Active:
                    return RunStatusIds.Active;
                case RunStatus.Dead:
                    return RunStatusIds.Dead;
                case RunStatus.Won:
                    return RunStatusIds.Won;
                default:
                    throw new ArgumentOutOfRangeException(nameof(status));
            }
        }

        private static string[] Copy(System.Collections.Generic.IReadOnlyList<string> values)
        {
            string[] copy = new string[values.Count];
            for (int index = 0; index < values.Count; index++)
                copy[index] = values[index];

            return copy;
        }
    }
}