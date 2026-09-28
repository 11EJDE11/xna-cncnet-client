using System;

using ClientCore;
using ClientCore.Display;

namespace ClientLogic.Settings;

/// <summary>
/// The client window's mode, as the XNA GameClass.SetGraphicsMode sets it: the window size (the client resolution, or
/// the minimum client resolution if the desktop is too small), the render resolution the screens are laid out for
/// (scaled to the window), borderless windowed and full screen.
/// </summary>
/// <param name="FullScreen">Borderless with the window exactly the desktop's size.</param>
public sealed record ClientGraphicsMode(int WindowWidth, int WindowHeight, int RenderWidth, int RenderHeight,
    bool Borderless, bool FullScreen, bool IntegerScale)
{
    /// <summary>The mode from the settings (ClientResolutionX/Y, BorderlessWindowedClient, IntegerScaledClient).</summary>
    public static ClientGraphicsMode FromSettings(ScreenResolution desktop)
    {
        UserINISettings settings = UserINISettings.Instance;
        ClientConfiguration config = ClientConfiguration.Instance;

        int windowWidth = settings.ClientResolutionX.Value;
        int windowHeight = settings.ClientResolutionY.Value;

        // The minimum supported resolution when the desktop can't contain the client (a lower desktop resolution
        // than when the setting was saved)
        if (desktop.Width < windowWidth || desktop.Height < windowHeight)
            (windowWidth, windowHeight) = (config.MinimumClientResolution.Width, config.MinimumClientResolution.Height);

        bool borderless = settings.BorderlessWindowedClient.Value;
        bool integerScale = settings.IntegerScaledClient.Value;
        (int renderWidth, int renderHeight) = GetRenderResolution(windowWidth, windowHeight, integerScale,
            config.MinimumRenderWidth, config.MinimumRenderHeight, config.MaximumRenderWidth, config.MaximumRenderHeight);

        // XNA enters full screen only when the client resolution exactly matches the desktop's
        bool fullScreen = borderless && settings.ClientResolutionX.Value == desktop.Width && settings.ClientResolutionY.Value == desktop.Height;

        return new ClientGraphicsMode(windowWidth, windowHeight, renderWidth, renderHeight, borderless, fullScreen, integerScale);
    }

    /// <summary>The render resolution for a window size (GameClass.SetGraphicsMode's rules).</summary>
    public static (int Width, int Height) GetRenderResolution(int windowWidth, int windowHeight, bool integerScale,
        int minimumRenderWidth, int minimumRenderHeight, int maximumRenderWidth, int maximumRenderHeight)
    {
        int renderResolutionX = 0;
        int renderResolutionY = 0;

        if (!integerScale || windowWidth < minimumRenderWidth || windowHeight < minimumRenderHeight)
        {
            int initialXRes = Math.Max(windowWidth, minimumRenderWidth);
            initialXRes = Math.Min(initialXRes, maximumRenderWidth);

            int initialYRes = Math.Max(windowHeight, minimumRenderHeight);
            initialYRes = Math.Min(initialYRes, maximumRenderHeight);

            double xRatio = windowWidth / (double)initialXRes;
            double yRatio = windowHeight / (double)initialYRes;

            double ratio = xRatio > yRatio ? yRatio : xRatio;

            // Special rule for 1360x768 and 1366x768: the native resolution with small black bars on the sides (most
            // interfaces are designed for 1280x720 or 1280x800, which don't upscale well to it)
            if ((windowWidth == 1366 || windowWidth == 1360) && windowHeight == 768)
            {
                renderResolutionX = windowWidth;
                renderResolutionY = windowHeight;
            }

            // Special rule: if 1280x720 is a valid render resolution, 1.5x scaling for 1920x1080
            if (windowWidth == 1920 && windowHeight == 1080
                && 1280 >= minimumRenderWidth && 1280 <= maximumRenderWidth
                && 720 >= minimumRenderHeight && 720 <= maximumRenderHeight)
            {
                renderResolutionX = 1280;
                renderResolutionY = 720;
            }

            // Special rule: if 1280x800 is a valid render resolution, 1.5x scaling for 1920x1200
            if (windowWidth == 1920 && windowHeight == 1200
                && 1280 >= minimumRenderWidth && 1280 <= maximumRenderWidth
                && 800 >= minimumRenderHeight && 800 <= maximumRenderHeight)
            {
                renderResolutionX = 1280;
                renderResolutionY = 800;
            }

            // Integer-scale the window if possible
            if (ratio > 1.0)
            {
                for (int i = 2; i <= ScreenResolutions.MAX_INT_SCALE; i++)
                {
                    int sharpScaleRenderResX = windowWidth / i;
                    int sharpScaleRenderResY = windowHeight / i;

                    if (sharpScaleRenderResX >= minimumRenderWidth &&
                        sharpScaleRenderResX <= maximumRenderWidth &&
                        sharpScaleRenderResY >= minimumRenderHeight &&
                        sharpScaleRenderResY <= maximumRenderHeight)
                    {
                        renderResolutionX = sharpScaleRenderResX;
                        renderResolutionY = sharpScaleRenderResY;
                        break;
                    }
                }
            }

            // No special rule: zoom the client to the window size with minimal black bars
            if (renderResolutionX == 0 || renderResolutionY == 0)
            {
                renderResolutionX = initialXRes;
                renderResolutionY = initialYRes;

                if (ratio == xRatio)
                    renderResolutionY = (int)(windowHeight / ratio);
            }
        }
        else
        {
            // The integer scale from the minimum render resolution (a larger scale is preferred over a larger render
            // resolution; this works best when the minimum and maximum render resolutions are close)
            int xScale = windowWidth / minimumRenderWidth;
            int yScale = windowHeight / minimumRenderHeight;
            int scale = Math.Min(xScale, yScale);

            renderResolutionX = Math.Min(maximumRenderWidth,
                minimumRenderWidth + (windowWidth - minimumRenderWidth * scale) / scale);
            renderResolutionY = Math.Min(maximumRenderHeight,
                minimumRenderHeight + (windowHeight - minimumRenderHeight * scale) / scale);
        }

        return (renderResolutionX, renderResolutionY);
    }
}
