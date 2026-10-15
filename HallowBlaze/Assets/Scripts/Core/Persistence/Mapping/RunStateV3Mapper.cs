using System;
using HallowBlaze.Core.Persistence.Dto;
using HallowBlaze.Core.Random;
using HallowBlaze.Core.State;

namespace HallowBlaze.Core.Persistence.Mapping
{
    /// <summary>Preserves the exact RNG contract while reusing validated stable travel fields.</summary>
    public static class RunStateV3Mapper
    {
        /// <summary>Copies the root seed, RNG identity and stable travel boundary without random cursor snapshots.</summary>
        public static RunStateV3Dto ToDto(RunState run)
        {
            if (run == null)
                throw new ArgumentNullException(nameof(run));
            var dto = new RunStateV3Dto(RunStateV2Mapper.ToDto(run))
            {
                RandomAlgorithmId = run.RandomAlgorithmId,
                RandomAlgorithmVersion = run.RandomAlgorithmVersion
            };
            FromDto(dto);
            return dto;
        }

        /// <summary>Rejects unknown RNG versions rather than substituting an algorithm with a different layout.</summary>
        /// <exception cref="PersistenceDataException">The schema, RNG metadata or travel fields are invalid.</exception>
        public static RunState FromDto(RunStateV3Dto dto)
        {
            if (dto == null)
                throw new ArgumentNullException(nameof(dto));
            DtoValidation.RequireCurrentSchemaVersion(dto.SchemaVersion, RunStateV3Dto.CurrentSchemaVersion);
            DtoValidation.RequireStableId(dto.RandomAlgorithmId, "randomAlgorithmId");
            DtoValidation.RequirePositive(dto.RandomAlgorithmVersion, "randomAlgorithmVersion");
            bool legacy = dto.RandomAlgorithmId == RunState.LegacyRandomAlgorithmId;
            if (!legacy && dto.RandomAlgorithmId != RandomStreamProvider.AlgorithmId)
                DtoValidation.ThrowInvalidValue("randomAlgorithmId");
            if (!(legacy && dto.RandomAlgorithmVersion == RunState.LegacyRandomAlgorithmVersion) &&
                !RandomStreamProvider.IsSupported(dto.RandomAlgorithmId, dto.RandomAlgorithmVersion))
                DtoValidation.ThrowInvalidValue("randomAlgorithmVersion");
            return RunStateV2Mapper.FromDto(dto, RunStateV3Dto.CurrentSchemaVersion,
                dto.RandomAlgorithmId, dto.RandomAlgorithmVersion);
        }
    }
}