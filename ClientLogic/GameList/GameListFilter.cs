using System;
using System.Collections.Generic;
using System.Linq;

using DTAClient.Domain.Multiplayer;
using DTAClient.Domain.Multiplayer.CnCNet;

namespace ClientLogic.GameList;

/// <summary>The user's game list filter settings, read when the list is filtered.</summary>
/// <param name="SearchText">The search box text, or null/empty when there is none.</param>
/// <param name="OptionFilters">
/// Per broadcast game option (in broadcast order), the value a game must have, or null for any.
/// </param>
public sealed record GameListFilterSettings(
    bool ShowFriendGamesOnly,
    bool HideLockedGames,
    bool HideIncompatibleGames,
    bool HidePasswordedGames,
    int MaxPlayerCount,
    string SearchText,
    IReadOnlyList<int?> OptionFilters);

/// <summary>Which games the CnCNet game list shows.</summary>
public static class GameListFilter
{
    /// <param name="isFriend">Whether a player name is on the friend list.</param>
    /// <param name="translateGameMode">The translated name of a game mode (for the search).</param>
    /// <param name="translateMapName">The translated name of a map, or null if unknown (for the search).</param>
    public static bool Matches(GenericHostedGame hg, GameListFilterSettings settings, Func<string, bool> isFriend,
        Func<string, string> translateGameMode, Func<string, string> translateMapName)
    {
        // friends list takes priority over other filters below
        if (settings.ShowFriendGamesOnly)
            return hg.Players.Any(isFriend);

        if (settings.HideLockedGames && hg.Locked)
            return false;

        if (settings.HideIncompatibleGames && hg.Incompatible)
            return false;

        if (settings.HidePasswordedGames && hg.Passworded)
            return false;

        if (hg.MaxPlayers > settings.MaxPlayerCount)
            return false;

        if (hg is HostedCnCNetGame cncnetGame && !OptionsMatch(cncnetGame, settings.OptionFilters))
            return false;

        // Looked up before the search check, as always: the lookups can report missing translations
        string translatedGameMode = translateGameMode(hg.GameMode);
        string translatedMapName = translateMapName(hg.Map);

        if (string.IsNullOrWhiteSpace(settings.SearchText))
            return true;

        string textUpper = settings.SearchText.ToUpperInvariant();

        return
            hg.RoomName.ToUpperInvariant().Contains(textUpper) ||
            hg.GameMode.ToUpperInvariant().Equals(textUpper, StringComparison.Ordinal) ||
            translatedGameMode.ToUpperInvariant().Equals(textUpper, StringComparison.Ordinal) ||
            hg.Map.ToUpperInvariant().Contains(textUpper) ||
            (translatedMapName is not null && translatedMapName.ToUpperInvariant().Contains(textUpper)) ||
            hg.Players.Any(pl => pl.ToUpperInvariant().Equals(textUpper, StringComparison.Ordinal));
    }

    /// <summary>Whether a game's broadcast option values match the option filters.</summary>
    public static bool OptionsMatch(HostedCnCNetGame game, IReadOnlyList<int?> optionFilters)
    {
        if (game.BroadcastedGameOptionValues == null)
            return true;

        for (int i = 0; i < optionFilters.Count; i++)
        {
            if (i >= game.BroadcastedGameOptionValues.Length)
                break;

            int? filterValue = optionFilters[i];
            if (filterValue != null && game.BroadcastedGameOptionValues[i] != filterValue.Value)
                return false;
        }

        return true;
    }
}
