using System.Collections.Generic;
using System.IO;
using System.Text;

using ClientLogic.Layout;

using Rampastring.Tools;

using Xunit;

namespace ClientLogic.Tests.Layout;

public class LayoutExpressionParserTests
{
    private static readonly Dictionary<string, int> Constants = new()
    {
        ["RESOLUTION_WIDTH"] = 1280,
        ["EMPTY_SPACE_TOP"] = 12,
        ["LOBBY_PANEL_SPACING"] = 10,
    };

    private static (LayoutControl Window, LayoutExpressionParser Parser) Setup()
    {
        var window = new LayoutControl("GameLobbyBase", "INItializableWindow", LayoutControlKind.Panel) { Width = 1200, Height = 700 };
        var map = new LayoutControl("MapPreviewBox", "MapPreviewBox", LayoutControlKind.Other) { X = 360, Y = 285, Width = 825, Height = 300 };
        window.AddChild(map);
        return (window, new LayoutExpressionParser(Constants, window));
    }

    [Theory]
    [InlineData("42", 42)]
    [InlineData("EMPTY_SPACE_TOP + 33", 45)]
    [InlineData("getBottom(MapPreviewBox) + LOBBY_PANEL_SPACING", 595)]
    [InlineData("getWidth($ParentControl) - getWidth(MapPreviewBox) - 12", 363)]
    [InlineData("getRight(MapPreviewBox)", 1185)]
    [InlineData("(10 + 20) - 5", 25)]
    // No operator precedence, as in the XNA parser: '*' takes the rest of the expression
    [InlineData("2 + 3 * 4", 20)]
    [InlineData("100 / 4 + 1", 20)]
    public void ExpressionsEvaluateAsInTheXnaClient(string expression, int expected)
    {
        (LayoutControl window, LayoutExpressionParser parser) = Setup();
        Assert.Equal(expected, parser.GetExprValue(expression, window.Children[0]));
    }

    [Fact]
    public void SelfAndCenteringUseTheParsingControl()
    {
        (LayoutControl window, LayoutExpressionParser parser) = Setup();
        var button = new LayoutControl("btnLaunchGame", "XNAClientButton", LayoutControlKind.Button) { Width = 200, Height = 23 };
        window.AddChild(button);

        Assert.Equal(677, parser.GetExprValue("getHeight($ParentControl) - getHeight($Self)", button));
        Assert.Equal(500, parser.GetExprValue("horizontalCenterOnParent()", button));
    }

    [Fact]
    public void InitializableWindowsAddChildrenAndReadExpressionsInOrder()
    {
        var window = new LayoutControl("SkirmishLobby", "INItializableWindow", LayoutControlKind.Panel) { Width = 1200, Height = 700 };
        var ini = new IniFile(new MemoryStream(Encoding.UTF8.GetBytes("""
            [SkirmishLobby]
            $CC00=GameOptionsPanel:XNAPanel
            $CC01=lblMapName:XNALabel
            [GameOptionsPanel]
            $Width=420
            $Height=260
            $X=getWidth($ParentControl) - getWidth($Self) - 12
            $Y=EMPTY_SPACE_TOP
            SolidColorBackgroundTexture=0,0,0,200
            $CC-GO01=chkShortGame:GameLobbyCheckBox
            [chkShortGame]
            Text=Short Game
            $X=12
            $Y=EMPTY_SPACE_TOP
            [lblMapName]
            Text=Map
            $TextAnchor=LEFT
            $AnchorPoint=getRight(GameOptionsPanel),getBottom(GameOptionsPanel) + 6
            """)));

        new XnaLayoutReader(_ => null, 1280, 768).ReadInitializableWindow(ini, window, Constants);

        LayoutControl panel = window.Find("GameOptionsPanel");
        Assert.Equal((768, 12, 420, 260), (panel.X, panel.Y, panel.Width, panel.Height));
        Assert.NotNull(panel.SolidBackground);

        LayoutControl checkBox = panel.Find("chkShortGame");
        Assert.Same(panel, checkBox.Parent);
        Assert.Equal("Short Game", checkBox.Text);
        Assert.Equal((12, 12), (checkBox.X, checkBox.Y));

        LayoutControl label = window.Find("lblMapName");
        Assert.Equal((1188f, 278f), label.AnchorPoint);
        Assert.Equal("LEFT", label.TextAnchor);
    }
}
