namespace ClientLogic.Lobby;

/// <summary>What the game host's Lock/Unlock button does.</summary>
public enum LockButtonAction
{
    Lock,
    Unlock,

    /// <summary>The room is full: it stays locked.</summary>
    RefuseUnlock,
}

/// <summary>
/// The room lock rules: a full room locks itself and can't be unlocked until someone leaves.
/// </summary>
public static class RoomLock
{
    public static bool IsFull(int playerCount, int playerLimit) => playerCount >= playerLimit;

    public static LockButtonAction OnLockButton(bool locked, int playerCount, int playerLimit) =>
        !locked ? LockButtonAction.Lock
        : IsFull(playerCount, playerLimit) ? LockButtonAction.RefuseUnlock
        : LockButtonAction.Unlock;
}
