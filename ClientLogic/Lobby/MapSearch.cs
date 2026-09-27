using System;
using System.Collections.Generic;
using System.Linq;

using DTAClient.Domain.Multiplayer;

namespace ClientLogic.Lobby;

/// <summary>The game lobby's map search, as the XNA lobby's ListMaps filters the map list.</summary>
public static class MapSearch
{
    /// <summary>
    /// The maps that match a search, best first: exact name matches, then names containing the whole search, then
    /// names containing every word of it. Translated and untranslated names both count. An empty search keeps all.
    /// </summary>
    public static List<GameModeMap> Filter(IReadOnlyList<GameModeMap> maps, string search)
    {
        search = search?.Trim() ?? string.Empty;
        if (search.Length == 0)
            return maps.ToList();

        string[] searchWords = search.Split([' '], StringSplitOptions.RemoveEmptyEntries);

        var exactMatches = maps.Where(gmm =>
            gmm.Map.Name.Equals(search, StringComparison.CurrentCultureIgnoreCase) ||
            gmm.Map.UntranslatedName.Equals(search, StringComparison.InvariantCultureIgnoreCase)).ToList();

        var substringMatches = maps.Except(exactMatches).Where(gmm =>
            gmm.Map.Name.IndexOf(search, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
            gmm.Map.UntranslatedName.IndexOf(search, StringComparison.InvariantCultureIgnoreCase) >= 0).ToList();

        var multiWordMatches = maps.Except(exactMatches).Except(substringMatches).Where(gmm =>
            searchWords.All(word => gmm.Map.Name.IndexOf(word, StringComparison.CurrentCultureIgnoreCase) >= 0) ||
            searchWords.All(word => gmm.Map.UntranslatedName.IndexOf(word, StringComparison.InvariantCultureIgnoreCase) >= 0)).ToList();

        return [.. exactMatches, .. substringMatches, .. multiWordMatches];
    }
}
