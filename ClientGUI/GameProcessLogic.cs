using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using ClientCore;
using Rampastring.Tools;
using ClientCore.INIProcessing;
using System.Threading;
using Rampastring.XNAUI;
using ClientCore.Extensions;

namespace ClientGUI
{
    /// <summary>
    /// A static class used for controlling the launching and exiting of the game executable.
    /// </summary>
    public static class GameProcessLogic
    {
        public static event Action GameProcessStarted;

        public static event Action GameProcessStarting;

        public static event Action GameProcessExited;

        public static bool UseQres { get; set; }
        public static bool SingleCoreAffinity { get; set; }

        //TODO fix all this properly
        /// <summary>
        /// Indicates whether the game is currently running in replay playback mode.
        /// This is set by GameReplayWindow when launching a replay.
        /// </summary>
        public static bool IsReplayPlayback { get; set; }

        /// <summary>
        /// The time when the game process was started.
        /// Used to detect and ignore premature exits (Phobos/Spawner debug modes).
        /// </summary>
        private static DateTime gameStartTime;

        /// <summary>
        /// The name of the actual game process to monitor.
        /// </summary>
        private const string ACTUAL_GAME_PROCESS_NAME = "gamemd-spawn";

        /// <summary>
        /// Starts the main game process.
        /// </summary>
        public static void StartGameProcess(WindowManager windowManager)
        {
            Logger.Log("About to launch main game executable.");

            // In the relatively unlikely event that INI preprocessing is still going on, just wait until it's done.
            // TODO ideally this should be handled in the UI so the client doesn't appear just frozen for the user.
            int waitTimes = 0;
            while (PreprocessorBackgroundTask.Instance.IsRunning)
            {
                Logger.Log("The preprocessor background task is still running. Wait for it...");
                Thread.Sleep(1000);
                waitTimes++;
                if (waitTimes > 10)
                {
                    XNAMessageBox.Show(windowManager,
                        "INI preprocessing not complete".L10N("Client:ClientGUI:INIPreprocessingNotCompleteTitle"),
                        ("INI preprocessing not complete. Please try " +
                        "launching the game again. If the problem persists, " +
                        "contact the game or mod authors for support.").L10N("Client:ClientGUI:INIPreprocessingNotCompleteText"));
                    return;
                }
            }

            OSVersion osVersion = ClientConfiguration.Instance.GetOperatingSystemVersion();

            string gameExecutableName;
            string additionalExecutableName = string.Empty;

            string errorLaunchingTitle = "Error launching game".L10N("Client:ClientGUI:ErrorLaunchingTitle");
            string errorLaunchingText = ("Error launching {0}. Please check that your anti-virus isn't blocking the CnCNet Client. " +
                        "You can also try running the client as an administrator.\n\nYou are unable to participate in this match. \n\n" +
                        "Returned error: {1}").L10N("Client:ClientGUI:ErrorLaunchingText");

            if (osVersion == OSVersion.UNIX)
                gameExecutableName = ClientConfiguration.Instance.UnixGameExecutableName;
            else
            {
                string launcherExecutableName = ClientConfiguration.Instance.GameLauncherExecutableName;
                if (string.IsNullOrEmpty(launcherExecutableName))
                    gameExecutableName = ClientConfiguration.Instance.GetGameExecutableName();
                else
                {
                    gameExecutableName = launcherExecutableName;
                    additionalExecutableName = "\"" + ClientConfiguration.Instance.GetGameExecutableName() + "\" ";
                }
            }

            string extraCommandLine = ClientConfiguration.Instance.ExtraExeCommandLineParameters;

            SafePath.DeleteFileIfExists(ProgramConstants.GamePath, "DTA.LOG");
            SafePath.DeleteFileIfExists(ProgramConstants.GamePath, "TI.LOG");
            SafePath.DeleteFileIfExists(ProgramConstants.GamePath, "TS.LOG");

            GameProcessStarting?.Invoke();

            gameStartTime = DateTime.Now;

            if (UserINISettings.Instance.WindowedMode && UseQres && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Logger.Log("Windowed mode is enabled - using QRes.");
                Process QResProcess = new Process();
                QResProcess.StartInfo.FileName = ProgramConstants.QRES_EXECUTABLE;
                QResProcess.StartInfo.UseShellExecute = false;

                if (!string.IsNullOrEmpty(extraCommandLine))
                    QResProcess.StartInfo.Arguments = "c=16 /R " + "\"" + SafePath.CombineFilePath(ProgramConstants.GamePath, gameExecutableName) + "\" " + additionalExecutableName + "-SPAWN " + extraCommandLine;
                else
                    QResProcess.StartInfo.Arguments = "c=16 /R " + "\"" + SafePath.CombineFilePath(ProgramConstants.GamePath, gameExecutableName) + "\" " + additionalExecutableName + "-SPAWN";
                QResProcess.EnableRaisingEvents = true;
                QResProcess.Exited += new EventHandler(Process_Exited);
                Logger.Log("Launch executable: " + QResProcess.StartInfo.FileName);
                Logger.Log("Launch arguments: " + QResProcess.StartInfo.Arguments);
                try
                {
                    QResProcess.Start();
                }
                catch (Exception ex)
                {
                    Logger.Log("Error launching QRes: " + ex.ToString());
                    XNAMessageBox.Show(windowManager,
                        errorLaunchingTitle,
                        string.Format(errorLaunchingText, ProgramConstants.QRES_EXECUTABLE, ex.Message));
                    Process_Exited(QResProcess, EventArgs.Empty);
                    return;
                }

                if (Environment.ProcessorCount > 1 && SingleCoreAffinity)
                    QResProcess.ProcessorAffinity = (IntPtr)2;
            }
            else
            {
                string arguments;

                if (!string.IsNullOrWhiteSpace(extraCommandLine))
                    arguments = " " + additionalExecutableName + "-SPAWN " + extraCommandLine;
                else
                    arguments = additionalExecutableName + "-SPAWN";

                FileInfo gameFileInfo = SafePath.GetFile(ProgramConstants.GamePath, gameExecutableName);

                var gameProcess = new Process();
                gameProcess.StartInfo.FileName = gameFileInfo.FullName;
                gameProcess.StartInfo.Arguments = arguments;
                gameProcess.StartInfo.UseShellExecute = false;

                gameProcess.EnableRaisingEvents = true;
                gameProcess.Exited += Process_Exited;

                Logger.Log("Launch executable: " + gameProcess.StartInfo.FileName);
                Logger.Log("Launch arguments: " + gameProcess.StartInfo.Arguments);
                try
                {
                    gameProcess.Start();
                    Logger.Log("GameProcessLogic: Process started.");
                }
                catch (Exception ex)
                {
                    Logger.Log("Error launching " + gameFileInfo.Name + ": " + ex.ToString());
                    XNAMessageBox.Show(windowManager,
                        errorLaunchingTitle,
                        string.Format(errorLaunchingText, gameFileInfo.Name, ex.Message));
                    Process_Exited(gameProcess, EventArgs.Empty);
                    return;
                }

                if ((RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    && Environment.ProcessorCount > 1 && SingleCoreAffinity)
                {
                    gameProcess.ProcessorAffinity = (IntPtr)2;
                }
            }

            GameProcessStarted?.Invoke();

            Logger.Log("Waiting for qres.dat or " + gameExecutableName + " to exit.");
        }

        static void Process_Exited(object sender, EventArgs e)
        {

            Process proc = (Process)sender;
            proc.Exited -= Process_Exited;
            proc.Dispose();

            //TODO fix all this
            TimeSpan elapsed = DateTime.Now - gameStartTime;
            if (elapsed.TotalSeconds < 5)
            {
                Logger.Log($"GameProcessLogic: Launcher process exited after {elapsed.TotalSeconds:F1} seconds. This is likely Syringe.exe.");
                Logger.Log($"GameProcessLogic: Now monitoring for {ACTUAL_GAME_PROCESS_NAME}.exe to exit...");

                // Start monitoring the gamemd-spawn.exe
                var monitorThread = new Thread(() => MonitorActualGameProcess());
                monitorThread.IsBackground = true;
                monitorThread.Start();
            }
            else
            {
                // Normal exit
                Logger.Log($"GameProcessLogic: Process exited after {elapsed.TotalSeconds:F1} seconds.");
                GameProcessExited?.Invoke();
            }
        }

        static void MonitorActualGameProcess()
        {
            try
            {
                Thread.Sleep(500);

                // Find the gamemd-spawn process
                Process[] gameProcesses = Process.GetProcessesByName(ACTUAL_GAME_PROCESS_NAME);

                if (gameProcesses.Length == 0)
                {
                    Logger.Log($"GameProcessLogic: No {ACTUAL_GAME_PROCESS_NAME}.exe process found. Game may have failed to start.");
                    GameProcessExited?.Invoke();
                    return;
                }

                Process gameProcess = gameProcesses[0];
                Logger.Log($"GameProcessLogic: Found {ACTUAL_GAME_PROCESS_NAME}.exe (PID: {gameProcess.Id}). Monitoring for exit...");

                for (int i = 1; i < gameProcesses.Length; i++)
                {
                    gameProcesses[i].Dispose();
                }

                gameProcess.WaitForExit();

                TimeSpan totalElapsed = DateTime.Now - gameStartTime;
                Logger.Log($"GameProcessLogic: {ACTUAL_GAME_PROCESS_NAME}.exe exited after {totalElapsed.TotalSeconds:F1} seconds total.");

                gameProcess.Dispose();

                GameProcessExited?.Invoke();
            }
            catch (Exception ex)
            {
                Logger.Log($"GameProcessLogic: Error monitoring game process: {ex.Message}");
                // Invoke exit event anyway to prevent the client from being stuck
                GameProcessExited?.Invoke();
            }
        }
    }
}
