using HallowBlaze.Core.Persistence.Dto;

namespace HallowBlaze.Core.Persistence.Mapping
{
    /// <summary>Migrates only evidenced v1 checkpoint data; never infers roads from node history.</summary>
    public static class RunStateV1Migration
    {
        /// <summary>
        /// Validates v1 before conversion. Old currentDay becomes completedDayCount, old worldNodeId
        /// remains the reached checkpoint, route nodes remain legacy entries, and leg/sequence start empty/zero.
        /// Resources, tools, status and profile-independent identity are preserved; no file is rewritten.
        /// </summary>
        public static RunStateV2Dto Migrate(RunStateDto legacy) => RunStateV2Mapper.ToDto(RunStateMapper.FromDto(legacy));
    }
}