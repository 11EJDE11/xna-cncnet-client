namespace ClientCore.Enums
{
    /// <summary>
    /// The game's own SessionClass::GameMode values, as recorded into a replay header by the
    /// spawner.
    /// </summary>
    public enum SessionGameMode
    {
        Campaign = 0,
        LAN = 3,
        Internet = 4,
        Skirmish = 5,
    }
}
