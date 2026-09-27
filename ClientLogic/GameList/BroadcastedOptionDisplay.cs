using System.Collections.Generic;
using System.Linq;

using ClientCore.Extensions;

using ClientLogic.Options;

namespace ClientLogic.GameList;

/// <summary>A broadcast game option shown next to a hosted game: its icon and, for the information panel, its text.</summary>
public sealed record BroadcastedOptionIcon(string IconName, string Text);

/// <summary>
/// Which icons a hosted game's broadcast option values show, as the XNA GameListBox (GetGameOptionIcons) and
/// GameInformationPanel (SetGameOptionsInfo) choose them. The values are in broadcast order: the broadcast
/// check-boxes, then the broadcast drop-downs.
/// </summary>
public static class BroadcastedOptionDisplay
{
    /// <summary>The broadcast options paired with their values; extra options or values are ignored.</summary>
    public static List<(GameOptionDefinition Definition, int Value)> Pair(GameOptionSet options, int[] values)
    {
        var result = new List<(GameOptionDefinition, int)>();
        if (values == null)
            return result;

        List<GameOptionDefinition> broadcast = options.CheckBoxes.Where(o => o.BroadcastToLobby)
            .Concat(options.DropDowns.Where(o => o.BroadcastToLobby))
            .Select(o => o.Definition).ToList();

        for (int i = 0; i < broadcast.Count && i < values.Length; i++)
            result.Add((broadcast[i], values[i]));

        return result;
    }

    /// <summary>The icons drawn in the game list row, left of the name and on the right, in broadcast order.</summary>
    public static (List<string> Left, List<string> Right) GameListIcons(GameOptionSet options, int[] values)
    {
        var left = new List<string>();
        var right = new List<string>();

        foreach ((GameOptionDefinition option, int value) in Pair(options, values))
        {
            if (!option.ShowInGameList)
                continue;

            string icon = IconOf(option, value);
            if (string.IsNullOrEmpty(icon))
                continue;

            (option.ShowInGameListOnRight ? right : left).Add(icon);
        }

        return (left, right);
    }

    /// <summary>
    /// The game options section of the information panel: icons drawn alone in a row, then icons with text in two
    /// columns; each ordered by SortOrder.
    /// </summary>
    public static (List<BroadcastedOptionIcon> IconsOnly, List<BroadcastedOptionIcon> WithText) InformationPanel(
        GameOptionSet options, int[] values)
    {
        var iconsOnly = new List<(BroadcastedOptionIcon Icon, int SortOrder)>();
        var withText = new List<(BroadcastedOptionIcon Icon, int SortOrder)>();

        foreach ((GameOptionDefinition option, int value) in Pair(options, values))
        {
            if (!option.ShowInGameInformationPanel)
                continue;

            string icon = IconOf(option, value);
            if (string.IsNullOrEmpty(icon))
                continue;

            string text;
            if (option.Kind == GameOptionKind.CheckBox)
                text = $"{option.Label}: {(value != 0 ? "On".L10N("Client:Main:On") : "Off".L10N("Client:Main:Off"))}";
            else
                text = $"{option.OptionName}: {option.Items[value].Label}";

            (option.ShowInGameInformationPanelAsIconOnly ? iconsOnly : withText)
                .Add((new BroadcastedOptionIcon(icon, text), option.SortOrder));
        }

        return (iconsOnly.OrderBy(x => x.SortOrder).Select(x => x.Icon).ToList(),
            withText.OrderBy(x => x.SortOrder).Select(x => x.Icon).ToList());
    }

    /// <summary>A check-box's enabled or disabled icon, or a drop-down's selected item icon; null if none.</summary>
    private static string IconOf(GameOptionDefinition option, int value)
    {
        if (option.Kind == GameOptionKind.CheckBox)
            return value != 0 ? option.EnabledIcon : option.DisabledIcon;

        return value >= 0 && value < option.Items.Count ? option.Items[value].IconName : null;
    }
}
