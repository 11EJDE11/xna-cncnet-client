using ClientLogic.GameList;
using ClientLogic.Options;

using Xunit;

namespace ClientLogic.Tests.GameList;

public class BroadcastedOptionDisplayTests
{
    private static GameOption Option(string name, GameOptionKind kind, params (string Key, string Value)[] keys)
    {
        var builder = new GameOptionDefinitionBuilder(name, kind, isLobbyOption: true, (_, d) => d);
        builder.Label = name;
        foreach ((string key, string value) in keys)
            builder.TryParse(null, key, value);

        if (kind == GameOptionKind.DropDown)
        {
            builder.Items.Add(new GameOptionItem("0", "Low", "low.png"));
            builder.Items.Add(new GameOptionItem("1", "High", null));
        }

        return new GameOption(builder.Build(), 0);
    }

    /// <summary>
    /// A drop-down added before the check-boxes, to check the broadcast order (check-boxes first), and one option
    /// that isn't broadcast.
    /// </summary>
    private static GameOptionSet Create()
    {
        TestGame.EnsureInitialized();
        var set = new GameOptionSet();
        set.Add(Option("ddCredits", GameOptionKind.DropDown, ("BroadcastToLobby", "true"), ("ShowInGameList", "true"),
            ("ShowInGameInformationPanel", "true"), ("OptionName", "Credits"), ("SortOrder", "1")));
        set.Add(Option("chkHidden", GameOptionKind.CheckBox, ("ShowInGameList", "true"), ("EnabledIcon", "hidden.png")));
        set.Add(Option("chkRa2", GameOptionKind.CheckBox, ("BroadcastToLobby", "true"), ("ShowInGameList", "true"),
            ("ShowInGameInformationPanel", "true"), ("EnabledIcon", "on.png"), ("DisabledIcon", "off.png"), ("SortOrder", "2")));
        set.Add(Option("chkSw", GameOptionKind.CheckBox, ("BroadcastToLobby", "true"), ("ShowInGameListOnRight", "true"),
            ("ShowInGameList", "true"), ("ShowInGameInformationPanel", "true"), ("ShowInGameInformationPanelAsIconOnly", "true"),
            ("EnabledIcon", "sw.png")));
        return set;
    }

    [Fact]
    public void ValuesPairWithTheBroadcastCheckBoxesThenDropDowns()
    {
        var pairs = BroadcastedOptionDisplay.Pair(Create(), [1, 0, 1, 99]);

        Assert.Equal(["chkRa2", "chkSw", "ddCredits"], pairs.ConvertAll(p => p.Definition.Name));
        Assert.Equal([1, 0, 1], pairs.ConvertAll(p => p.Value));
    }

    [Fact]
    public void GameListIconsFollowTheValuesAndSkipMissingIcons()
    {
        var (left, right) = BroadcastedOptionDisplay.GameListIcons(Create(), [0, 1, 0]);
        Assert.Equal(["off.png", "low.png"], left);
        Assert.Equal(["sw.png"], right);

        // chkSw off has no disabled icon; the drop-down's second item has none either
        (left, right) = BroadcastedOptionDisplay.GameListIcons(Create(), [1, 0, 1]);
        Assert.Equal(["on.png"], left);
        Assert.Empty(right);
    }

    [Fact]
    public void TheInformationPanelSortsAndLabelsTheOptions()
    {
        var (iconsOnly, withText) = BroadcastedOptionDisplay.InformationPanel(Create(), [1, 1, 0]);

        Assert.Equal([new BroadcastedOptionIcon("sw.png", "chkSw: On")], iconsOnly);
        Assert.Equal([new BroadcastedOptionIcon("low.png", "Credits: Low"), new BroadcastedOptionIcon("on.png", "chkRa2: On")], withText);
    }

    [Fact]
    public void GamesWithoutValuesShowNothing()
    {
        Assert.Empty(BroadcastedOptionDisplay.Pair(Create(), null));
    }
}
