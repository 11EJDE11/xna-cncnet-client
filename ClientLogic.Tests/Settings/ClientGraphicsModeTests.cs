using ClientLogic.Settings;

using Xunit;

namespace ClientLogic.Tests.Settings;

public class ClientGraphicsModeTests
{
    // The YR package's ClientDefinitions: render 1280x768 to 1440x800
    private static (int, int) Render(int width, int height, bool integerScale) =>
        ClientGraphicsMode.GetRenderResolution(width, height, integerScale, 1280, 768, 1440, 800);

    [Theory]
    [InlineData(1440, 800, false, 1440, 800)]   // within the limits: unscaled
    [InlineData(1280, 768, true, 1280, 768)]
    [InlineData(1366, 768, false, 1366, 768)]   // the 1366x768 rule
    [InlineData(1920, 1080, false, 1440, 810)]  // 1280x720 isn't a valid render resolution here: zoomed
    [InlineData(2880, 1600, false, 1440, 800)]  // exactly 2x
    [InlineData(2560, 1600, true, 1280, 800)]   // integer scaling from the minimum: 2x
    [InlineData(1024, 768, true, 1280, 960)]    // smaller than the minimum: zoomed down
    public void RenderResolutionFollowsTheXnaRules(int width, int height, bool integerScale, int expectedWidth, int expectedHeight) =>
        Assert.Equal((expectedWidth, expectedHeight), Render(width, height, integerScale));

    [Fact]
    public void Full_hd_uses_1280x720_when_the_theme_allows_it() =>
        Assert.Equal((1280, 720), ClientGraphicsMode.GetRenderResolution(1920, 1080, false, 1280, 720, 1920, 1080));
}
