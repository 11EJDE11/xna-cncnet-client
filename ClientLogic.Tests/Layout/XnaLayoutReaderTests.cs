using System.IO;
using System.Text;

using ClientLogic.Layout;
using ClientLogic.UI;

using Rampastring.Tools;

using Xunit;

namespace ClientLogic.Tests.Layout;

public class XnaLayoutReaderTests
{
    private static IniFile Ini(string text) => new(new MemoryStream(Encoding.UTF8.GetBytes(text)));

    private static XnaLayoutReader Reader() =>
        new(texture => texture == "MainMenu/button.png" ? (156, 30) : null, 1280, 768);

    private static LayoutControl Window(params (string Name, string Type)[] children)
    {
        var window = new LayoutControl("MainMenu", "XNAWindow", LayoutControlKind.Panel);
        foreach ((string name, string type) in children)
            window.AddChild(new LayoutControl(name, type, XnaLayoutReader.KindOf(type)));

        return window;
    }

    [Fact]
    public void AWindowAndItsCodeControlsReadTheirSections()
    {
        var window = Window(("btnSkirmish", "XNAClientButton"));

        Reader().ReadWindow(Ini("""
            [MainMenu]
            Size=800,600
            [btnSkirmish]
            Location=645,325
            Text=Skirmish
            IdleTexture=MainMenu/button.png
            HoverTexture=MainMenu/button_c.png
            FontIndex=1
            """), window);

        Assert.Equal((800, 600), (window.Width, window.Height));
        LayoutControl button = window.Find("btnSkirmish");
        Assert.Equal((645, 325), (button.X, button.Y));
        Assert.Equal((156, 30), (button.Width, button.Height)); // from the idle texture
        Assert.Equal("Skirmish", button.Text);
        Assert.Equal("MainMenu/button_c.png", button.HoverTexture);
        Assert.Equal(1, button.FontIndex);
    }

    [Fact]
    public void AWindowWithoutASectionUsesGenericWindow()
    {
        var window = Window();

        Reader().ReadWindow(Ini("""
            [GenericWindow]
            BackgroundTexture=genericbg.png
            DrawBorders=false
            """), window);

        Assert.Equal("genericbg.png", window.BackgroundTexture);
        Assert.False(window.DrawBorders);
    }

    [Fact]
    public void ExtraControlsAreAddedUnlessTheyExist()
    {
        var window = Window(("btnSkirmish", "XNAClientButton"));

        Reader().ReadWindow(Ini("""
            [ExtraControls]
            0=lblPoweredBy:XNALabel
            1=btnSkirmish:XNALinkButton
            [lblPoweredBy]
            Text=Powered by:
            RemapColor=204,210,206
            AnchorPoint=716,54
            TextAnchor=HORIZONTAL_CENTER
            """), window);

        Assert.Equal(2, window.Children.Count);
        Assert.Equal("XNAClientButton", window.Find("btnSkirmish").TypeName);

        LayoutControl label = window.Find("lblPoweredBy");
        Assert.Equal(LayoutControlKind.Label, label.Kind);
        Assert.Equal(new ChatColor(204, 210, 206), label.TextColor);
        Assert.Equal((716f, 54f), label.AnchorPoint);
        Assert.Equal("HORIZONTAL_CENTER", label.TextAnchor);
    }

    [Fact]
    public void BorderDistancesUseTheParentsSizeAndInvisibleControlsAreDisabled()
    {
        var window = Window(("btnExit", "XNAClientButton"));

        Reader().ReadWindow(Ini("""
            [MainMenu]
            Size=800,600
            [btnExit]
            Size=100,20
            DistanceFromRightBorder=10
            DistanceFromBottomBorder=5
            Visible=0
            """), window);

        LayoutControl button = window.Find("btnExit");
        Assert.Equal((690, 575), (button.X, button.Y));
        Assert.False(button.Visible);
        Assert.False(button.Enabled);
    }

    [Fact]
    public void TextKeepsTheIniEscapes()
    {
        var window = Window(("lblNote", "XNALabel"));

        Reader().ReadWindow(Ini("""
            [lblNote]
            Text=One@Two
            """), window);

        Assert.Contains("One", window.Find("lblNote").Text);
        Assert.Contains("Two", window.Find("lblNote").Text);
        Assert.Equal("One@Two", window.Find("lblNote").Attributes["Text"]);
    }
}
