using System;

using ClientCore.Extensions;

using DTAClient.Domain.Multiplayer.CnCNet;

using Rampastring.Tools;

namespace ClientLogic.CnCNet;

/// <summary>What joining a listed CnCNet game needs to know about the local client.</summary>
/// <param name="GetGameName">The display name of a game (by its internal name), for the "wrong game" error.</param>
public sealed record JoinContext(
    string LocalGameId,
    string LocalPlayerName,
    bool DisallowJoiningIncompatibleGames,
    bool IsJoiningGame,
    bool IsGameRunning,
    Func<string, string> GetGameName);

/// <summary>The rules for joining a game from the CnCNet game list.</summary>
public static class JoinGameRules
{
    /// <summary>Why no game can be joined right now, or null.</summary>
    public static string BaseError(bool isJoiningGame, bool isGameRunning)
    {
        if (isJoiningGame)
            return "Cannot join game - joining game in progress. If you believe this is an error, please log out and back in.".L10N("Client:Main:JoinGameErrorInProgress");

        if (isGameRunning)
            return "Cannot join game while the main game executable is running.".L10N("Client:Main:JoinGameErrorGameRunning");

        return null;
    }

    /// <summary>Why this game can't be joined, or null.</summary>
    public static string Error(HostedCnCNetGame hg, JoinContext context)
    {
        if (hg.Game.InternalName.ToUpper() != context.LocalGameId.ToUpper())
            return string.Format("The selected game is for {0}!".L10N("Client:Main:GameIsOfPurpose"), context.GetGameName(hg.Game.InternalName));

        if (hg.Incompatible && context.DisallowJoiningIncompatibleGames)
            return "Cannot join game. The host is on a different game version than you.".L10N("Client:Main:DisallowJoiningIncompatibleGames");

        if (hg.Locked)
            return string.Format("The game {0} is locked!".L10N("Client:Main:GameLockedWithName"), hg.RoomName);

        if (hg.IsLoadedGame && !hg.Players.Contains(context.LocalPlayerName))
            return "You do not exist in the saved game!".L10N("Client:Main:NotInSavedGame");

        return BaseError(context.IsJoiningGame, context.IsGameRunning);
    }

    /// <summary>Whether the user must type the game's password (none was given, e.g. by an invitation).</summary>
    public static bool NeedsPasswordPrompt(HostedCnCNetGame hg, string password) =>
        hg.Passworded && string.IsNullOrEmpty(password);

    /// <summary>
    /// The channel key to join with: the given password for passworded games; otherwise derived from the channel
    /// name, or for a saved game from its game ID.
    /// </summary>
    /// <param name="getSavedGameId">Reads the saved game's ID (spawnSG.ini); only called for saved games.</param>
    public static string JoinPassword(HostedCnCNetGame hg, string password, Func<string> getSavedGameId)
    {
        if (hg.Passworded)
            return password;

        return hg.IsLoadedGame ? DerivedPassword(getSavedGameId()) : DerivedPassword(hg.ChannelName);
    }

    /// <summary>The channel key of a game without a custom password: the first 10 characters of a SHA-1.</summary>
    public static string DerivedPassword(string source) => Utilities.CalculateSHA1ForString(source).Substring(0, 10);
}
