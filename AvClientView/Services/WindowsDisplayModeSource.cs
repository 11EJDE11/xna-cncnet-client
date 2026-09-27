using System.Collections.Generic;
using System.Runtime.InteropServices;

using ClientCore.Display;

using ClientLogic.Settings;

namespace AvClientView.Services;

/// <summary>
/// The primary display's modes from Windows (EnumDisplaySettings), as the XNA client's graphics adapter lists them:
/// the 32-bit modes, and the current mode as the desktop resolution.
/// </summary>
public sealed class WindowsDisplayModeSource : IDisplayModeSource
{
    private const int ENUM_CURRENT_SETTINGS = -1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);

    private static DEVMODE NewDevMode() => new() { dmSize = (short)Marshal.SizeOf<DEVMODE>() };

    public ScreenResolution DesktopResolution
    {
        get
        {
            DEVMODE mode = NewDevMode();
            return EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref mode)
                ? new ScreenResolution(mode.dmPelsWidth, mode.dmPelsHeight)
                : new ScreenResolution(1280, 768);
        }
    }

    public IEnumerable<ScreenResolution> DisplayModes
    {
        get
        {
            var modes = new List<ScreenResolution>();
            DEVMODE mode = NewDevMode();
            for (int i = 0; EnumDisplaySettings(null, i, ref mode); i++)
            {
                if (mode.dmBitsPerPel == 32)
                    modes.Add(new ScreenResolution(mode.dmPelsWidth, mode.dmPelsHeight));
            }

            return modes;
        }
    }
}
