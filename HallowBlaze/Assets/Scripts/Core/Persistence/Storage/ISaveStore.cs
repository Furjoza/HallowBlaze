using HallowBlaze.Core.State;

namespace HallowBlaze.Core.Persistence.Storage
{
    /// <summary>
    /// Contract for saving and loading profile and run state to persistent storage.
    /// </summary>
    public interface ISaveStore
    {
        /// <summary>
        /// Saves the profile state to persistent storage.
        /// </summary>
        /// <param name="profile">The profile state to save.</param>
        /// <returns>A result indicating success or failure.</returns>
        SaveStoreResult SaveProfile(ProfileState profile);

        /// <summary>
        /// Loads the profile state from persistent storage.
        /// </summary>
        /// <returns>A result containing the loaded profile state or an error.</returns>
        SaveStoreResult<ProfileState> LoadProfile();

        /// <summary>
        /// Saves the run state to persistent storage.
        /// </summary>
        /// <param name="run">The run state to save.</param>
        /// <returns>A result indicating success or failure.</returns>
        SaveStoreResult SaveRun(RunState run);

        /// <summary>
        /// Loads the run state from persistent storage.
        /// </summary>
        /// <returns>A result containing the loaded run state or an error.</returns>
        SaveStoreResult<RunState> LoadRun();

        /// <summary>
        /// Removes the active run and its recovery copy from persistent storage.
        /// </summary>
        /// <returns>A result indicating success or failure.</returns>
        SaveStoreResult DeleteRun();
    }
}