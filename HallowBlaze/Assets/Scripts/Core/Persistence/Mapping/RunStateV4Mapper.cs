using System;
using System.Collections.Generic;
using HallowBlaze.Core.Persistence.Dto;
using HallowBlaze.Core.State;

namespace HallowBlaze.Core.Persistence.Mapping
{
    /// <summary>Preserves equipment identity and allocation while reusing the validated travel/RNG contract.</summary>
    public static class RunStateV4Mapper
    {
        /// <summary>Copies a complete run boundary without aliasing mutable DTO arrays or serializing definitions.</summary>
        public static RunStateV4Dto ToDto(RunState run)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));
            var dto = new RunStateV4Dto(RunStateV3Mapper.ToDto(run))
            {
                ToolInstanceSequence = run.ToolInstanceSequence,
                ToolSlots = new ToolInstanceDto[RunState.ToolSlotCount]
            };
            for (int index = 0; index < dto.ToolSlots.Length; index++)
            {
                ToolInstanceState instance = run.ToolSlots[index].Instance;
                if (instance != null)
                    dto.ToolSlots[index] = new ToolInstanceDto
                    { InstanceId = instance.InstanceId, DefinitionId = instance.DefinitionId, Charges = instance.Charges };
            }
            FromDto(dto);
            return dto;
        }

        /// <summary>
        /// Reads both known and unknown stable definition IDs without dropping, refilling or replacing a tool.
        /// Availability of unknown content is a catalog concern, not permission to discard persisted ownership.
        /// </summary>
        /// <exception cref="PersistenceDataException">Equipment or the existing travel/RNG contract is invalid.</exception>
        public static RunState FromDto(RunStateV4Dto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            DtoValidation.RequireCurrentSchemaVersion(dto.SchemaVersion, RunStateV4Dto.CurrentSchemaVersion);
            DtoValidation.RequireNonNegative(dto.ToolInstanceSequence, "toolInstanceSequence");
            ToolInstanceDto[] slots = DtoValidation.RequireArray(dto.ToolSlots, "toolSlots");
            if (slots.Length != RunState.ToolSlotCount) DtoValidation.ThrowInvalidValue("toolSlots");
            var instances = new ToolInstanceState[RunState.ToolSlotCount];
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < slots.Length; index++)
            {
                ToolInstanceDto slot = slots[index];
                if (slot == null) continue;
                string path = "toolSlots[" + index + "]";
                DtoValidation.RequireStableId(slot.InstanceId, path + ".instanceId");
                DtoValidation.RequireStableId(slot.DefinitionId, path + ".definitionId");
                DtoValidation.RequireNonNegative(slot.Charges, path + ".charges");
                if (!ids.Add(slot.InstanceId)) DtoValidation.ThrowInvalidValue(path + ".instanceId");
                instances[index] = new ToolInstanceState(slot.InstanceId, slot.DefinitionId, slot.Charges);
            }
            RunState run = RunStateV3Mapper.FromDto(dto.CreateTravelDocument());
            try { run.RestoreToolState(dto.ToolInstanceSequence, instances); }
            catch (ArgumentException) { DtoValidation.ThrowInvalidValue("toolSlots"); }
            return run;
        }
    }
}