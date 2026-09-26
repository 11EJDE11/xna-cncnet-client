using System;

using ClientLogic.MapPreview;

using DTAClient.Domain.Multiplayer;

using Xunit;

namespace ClientLogic.Tests.MapPreview;

public class MapPreviewLayoutTests
{
    /// <summary>MapPreviewBox.UpdateMap's layout before PR 28.</summary>
    private static (double Ratio, int X, int Y, int Width, int Height) OldLayout(int Width, int Height, int textureWidth0, int textureHeight0)
    {
        double xRatio = (Width - 2) / (double)textureWidth0;
        double yRatio = (Height - 2) / (double)textureHeight0;

        double ratio;

        int texturePositionX = 1;
        int texturePositionY = 1;
        int textureHeight = 0;
        int textureWidth = 0;

        if (xRatio > yRatio)
        {
            ratio = yRatio;
            textureHeight = Height - 2;
            textureWidth = (int)(textureWidth0 * ratio);
            texturePositionX = (int)(Width - 2 - textureWidth) / 2;
        }
        else
        {
            ratio = xRatio;
            textureWidth = Width - 2;
            textureHeight = (int)(textureHeight0 * ratio);
            texturePositionY = (Height - 2 - textureHeight) / 2 + 1;
        }

        return (ratio, texturePositionX, texturePositionY, textureWidth, textureHeight);
    }

    [Fact]
    public void FitMatchesTheOldPreviewBox()
    {
        var random = new Random(28);
        for (int run = 0; run < 5000; run++)
        {
            int boxWidth = random.Next(50, 1200), boxHeight = random.Next(50, 900);
            int previewWidth = random.Next(20, 2000), previewHeight = random.Next(20, 2000);

            MapPreviewLayout layout = MapPreviewLayout.Fit(boxWidth, boxHeight, previewWidth, previewHeight);
            var old = OldLayout(boxWidth, boxHeight, previewWidth, previewHeight);

            Assert.Equal(old, (layout.Ratio, layout.X, layout.Y, layout.Width, layout.Height));
            Assert.Equal(old.Ratio < 1.0, layout.UseNearestNeighbour);

            var point = new MapPoint(random.Next(0, previewWidth), random.Next(0, previewHeight));
            MapPoint inBox = layout.ToBox(point);
            Assert.Equal((old.X + (int)(point.X * old.Ratio), old.Y + (int)(point.Y * old.Ratio)), (inBox.X, inBox.Y));
        }
    }

    [Fact]
    public void WidePreviewsAreCentredVerticallyAndTallOnesHorizontally()
    {
        MapPreviewLayout layout = MapPreviewLayout.Fit(402, 202, 800, 200);
        Assert.Equal((0.5, 1, 51, 400, 100, true), (layout.Ratio, layout.X, layout.Y, layout.Width, layout.Height, layout.UseNearestNeighbour));

        MapPreviewLayout tall = MapPreviewLayout.Fit(402, 202, 100, 400);
        Assert.Equal((0.5, 175, 1, 50, 200), (tall.Ratio, tall.X, tall.Y, tall.Width, tall.Height));
    }
}
