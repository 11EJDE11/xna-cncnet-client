using System.Diagnostics;

using ClientCore;

using Rampastring.Tools;

namespace ClientLogic.Launch;

/// <summary>Starts the map editor (MapEditorExePath, or UnixMapEditorExePath on Unix), as the XNA main menu and extras window do.</summary>
public static class MapEditorLauncher
{
    public static void Launch()
    {
        OSVersion osVersion = ClientConfiguration.Instance.GetOperatingSystemVersion();
        using var mapEditorProcess = new Process();

        if (osVersion != OSVersion.UNIX)
            mapEditorProcess.StartInfo.FileName = SafePath.CombineFilePath(ProgramConstants.GamePath, ClientConfiguration.Instance.MapEditorExePath);
        else
            mapEditorProcess.StartInfo.FileName = SafePath.CombineFilePath(ProgramConstants.GamePath, ClientConfiguration.Instance.UnixMapEditorExePath);

        mapEditorProcess.StartInfo.UseShellExecute = false;

        mapEditorProcess.Start();
    }
}
