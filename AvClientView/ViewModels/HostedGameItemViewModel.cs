using System.Collections.Generic;
using System.IO;

using Avalonia.Media.Imaging;

using ClientCore.Caching;

using ClientLogic.GameList;
using ClientLogic.Options;

using DTAClient.Domain.Multiplayer;
using DTAClient.Domain.Multiplayer.CnCNet;

using SixLabors.ImageSharp;

namespace AvClientView.ViewModels;

/// <summary>
/// A hosted game in a game list (CnCNet or LAN), with the broadcast option icons the XNA GameListBox and
/// GameInformationPanel show for it. Without a game option set (the LAN lobby, as XNA's LAN list has no game lobby)
/// there are no option icons.
/// </summary>
public class HostedGameItemViewModel(GenericHostedGame game, GameOptionSet options = null)
{
    private static readonly (List<string>, List<string>) NoIcons = ([], []);
    private static readonly (List<BroadcastedOptionIcon>, List<BroadcastedOptionIcon>) NoOptions = ([], []);

    public GenericHostedGame Game { get; } = game;

    /// <summary>The broadcast option icons of the game list row (left of the name, and on the right).</summary>
    public (List<string> Left, List<string> Right) OptionIcons { get; } =
        options != null && game is HostedCnCNetGame cncnetGame
            ? BroadcastedOptionDisplay.GameListIcons(options, cncnetGame.BroadcastedGameOptionValues)
            : NoIcons;

    /// <summary>The game options section of the information panel.</summary>
    public (List<BroadcastedOptionIcon> IconsOnly, List<BroadcastedOptionIcon> WithText) InformationOptions { get; } =
        options != null && game is HostedCnCNetGame cncnetGame
            ? BroadcastedOptionDisplay.InformationPanel(options, cncnetGame.BroadcastedGameOptionValues)
            : NoOptions;

    /// <summary>A hosted game's map name and preview, as the XNA game information panel finds them by map hash.</summary>
    public static (string MapName, Bitmap Preview) FindMap(MapLoader mapLoader, GenericHostedGame game)
    {
        Map map = string.IsNullOrEmpty(game.MapHash) ? null : mapLoader.FindMapByHash(game.MapHash);
        if (map == null)
            return (null, null);

        using CacheLease<Image> lease = mapLoader.GetCachedPreviewImageFromMap(map, syncLoadOnCacheMiss: true);
        if (lease == null)
            return (map.Name ?? map.UntranslatedName, null);

        using var stream = new MemoryStream();
        lease.Value.SaveAsPng(stream);
        stream.Position = 0;
        return (map.Name ?? map.UntranslatedName, new Bitmap(stream));
    }
}
