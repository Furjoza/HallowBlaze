namespace HallowBlaze.Core.Board.State
{
    /// <summary>
    /// Defines the layers on which board entities can exist.
    /// </summary>
    public enum BoardLayer
    {
        /// <summary>
        /// The base terrain layer (ground, walls, etc.).
        /// </summary>
        Terrain = 0,

        /// <summary>
        /// Obstacles that block movement (rocks, trees, etc.).
        /// </summary>
        Obstacle = 1,

        /// <summary>
        /// Items that can be picked up or interacted with.
        /// </summary>
        Item = 2,

        /// <summary>
        /// Actors (player, NPCs, creatures).
        /// </summary>
        Actor = 3
    }
}
