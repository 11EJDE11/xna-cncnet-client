using System.Collections.Generic;

using DTAClient.Domain.Multiplayer;

namespace ClientLogic.MapPreview;

/// <summary>
/// Where a map preview image goes in a preview box of a given size (scaled to fit, keeping its aspect ratio, inside
/// a 1-pixel border) and where points on the preview end up in the box.
/// </summary>
/// <param name="Ratio">The scale from preview image pixels to box pixels.</param>
/// <param name="X">The left of the scaled image in the box.</param>
/// <param name="Y">The top of the scaled image in the box.</param>
/// <param name="UseNearestNeighbour">The image is scaled down: draw it with nearest-neighbour sampling.</param>
public sealed record MapPreviewLayout(double Ratio, int X, int Y, int Width, int Height, bool UseNearestNeighbour)
{
    public const int MAX_STARTING_LOCATIONS = 8;

    /// <summary>Fits a preview image into a box.</summary>
    public static MapPreviewLayout Fit(int boxWidth, int boxHeight, int previewWidth, int previewHeight)
    {
        double xRatio = (boxWidth - 2) / (double)previewWidth;
        double yRatio = (boxHeight - 2) / (double)previewHeight;

        double ratio;
        int x = 1;
        int y = 1;
        int width;
        int height;

        // The offsets are as the client has always drawn them: centred horizontally without the border,
        // vertically with it
        if (xRatio > yRatio)
        {
            ratio = yRatio;
            height = boxHeight - 2;
            width = (int)(previewWidth * ratio);
            x = (boxWidth - 2 - width) / 2;
        }
        else
        {
            ratio = xRatio;
            width = boxWidth - 2;
            height = (int)(previewHeight * ratio);
            y = (boxHeight - 2 - height) / 2 + 1;
        }

        return new MapPreviewLayout(ratio, x, y, width, height, ratio < 1.0);
    }

    /// <summary>Converts a point on the preview image to a point in the box.</summary>
    public MapPoint ToBox(MapPoint previewPoint) =>
        new(X + (int)(previewPoint.X * Ratio), Y + (int)(previewPoint.Y * Ratio));

    /// <summary>
    /// The box positions of the start location markers (index 0 = start 1), or null for starts the map doesn't have
    /// or doesn't allow.
    /// </summary>
    public IReadOnlyList<MapPoint?> StartMarkers(Map map, MapPoint previewSize, ICollection<int> allowedStartingLocations)
    {
        List<MapPoint> startingLocations = map.GetStartingLocationPreviewCoords(previewSize);
        var markers = new MapPoint?[MAX_STARTING_LOCATIONS];

        for (int i = 0; i < MAX_STARTING_LOCATIONS; i++)
        {
            if (i < startingLocations.Count && allowedStartingLocations.Contains(i + 1))
                markers[i] = ToBox(startingLocations[i]);
        }

        return markers;
    }

    /// <summary>
    /// The box position of an extra preview texture placed at a map point (its top-left corner, so the texture's
    /// centre is on the point).
    /// </summary>
    public MapPoint ExtraTexturePosition(Map map, MapPoint mapPoint, int level, MapPoint previewSize, int textureWidth, int textureHeight) =>
        ToBox(map.MapPointToMapPreviewPoint(mapPoint,
            new MapPoint(previewSize.X - (textureWidth / 2), previewSize.Y - (textureHeight / 2)), level));
}
