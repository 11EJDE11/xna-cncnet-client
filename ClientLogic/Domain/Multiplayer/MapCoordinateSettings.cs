namespace DTAClient.Domain.Multiplayer
{
    /// <summary>
    /// How map waypoints and cells are converted into map preview coordinates.
    /// Set from the client configuration at startup by <see cref="MainClientConstants.Initialize"/>.
    /// </summary>
    public static class MapCoordinateSettings
    {
        public static bool USE_ISOMETRIC_CELLS = true;
        public static int TDRA_WAYPOINT_COEFFICIENT = 128;
        public static int MAP_CELL_SIZE_X = 48;
        public static int MAP_CELL_SIZE_Y = 24;
    }
}
