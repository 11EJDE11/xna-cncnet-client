using System;

using ClientLogic.Launch;

using Rampastring.XNAUI;

namespace ClientGUI
{
    /// <summary>
    /// The XNA client's access to the <see cref="GameProcessService"/>: launches the game executable and reports when it
    /// starts and exits. The front end registers <see cref="Service"/> as its game process service.
    /// </summary>
    public static class GameProcessLogic
    {
        public static GameProcessService Service { get; } = new GameProcessService();

        public static event Action GameProcessStarted
        {
            add => Service.GameProcessStarted += value;
            remove => Service.GameProcessStarted -= value;
        }

        public static event Action GameProcessStarting
        {
            add => Service.GameProcessStarting += value;
            remove => Service.GameProcessStarting -= value;
        }

        public static event Action GameProcessExited
        {
            add => Service.GameProcessExited += value;
            remove => Service.GameProcessExited -= value;
        }

        public static bool UseQres
        {
            get => Service.UseQres;
            set => Service.UseQres = value;
        }

        public static bool SingleCoreAffinity
        {
            get => Service.SingleCoreAffinity;
            set => Service.SingleCoreAffinity = value;
        }

        /// <summary>
        /// Starts the main game process.
        /// </summary>
        public static void StartGameProcess(WindowManager windowManager) => Service.Start(new XnaDialogService(windowManager));
    }
}
