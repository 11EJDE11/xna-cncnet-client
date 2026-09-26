using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ClientCore;
using Rampastring.Tools;

namespace DTAClient.Domain.Multiplayer
{
    /// <summary>
    /// Debugging aid: when the CNCNET_CAPTURE_LAUNCH environment variable is set to a directory,
    /// every game launched from a game lobby leaves a copy of its spawn files and the lobby inputs
    /// that produced them in a new sub-directory of it. Used to compare launches between client
    /// versions and between the players of the same game.
    /// </summary>
    public sealed class LaunchCapture
    {
        public const string ENVIRONMENT_VARIABLE = "CNCNET_CAPTURE_LAUNCH";

        private static readonly string captureRoot = Environment.GetEnvironmentVariable(ENVIRONMENT_VARIABLE);

        public static bool IsEnabled => !string.IsNullOrWhiteSpace(captureRoot);

        private readonly string inputsJson;

        private LaunchCapture(string inputsJson)
        {
            this.inputsJson = inputsJson;
        }

        /// <summary>
        /// Records the lobby inputs of a launch that is about to happen.
        /// Returns null when capturing is disabled or fails.
        /// </summary>
        public static LaunchCapture Begin(Func<IDictionary<string, object>> getInputs)
        {
            if (!IsEnabled)
                return null;

            try
            {
                string json = JsonSerializer.Serialize(getInputs(), new JsonSerializerOptions { WriteIndented = true });
                return new LaunchCapture(json);
            }
            catch (Exception ex)
            {
                Logger.Log("Launch capture: failed to record the lobby inputs. " + ex);
                return null;
            }
        }

        /// <summary>
        /// Copies the written spawn files and the recorded inputs to a new capture directory.
        /// </summary>
        public void Complete(string localPlayerName)
        {
            try
            {
                string name = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "_" + string.Concat(localPlayerName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
                DirectoryInfo directory = Directory.CreateDirectory(Path.Combine(captureRoot, name));

                var fileNames = new List<string> { ProgramConstants.SPAWNER_SETTINGS, ProgramConstants.SPAWNMAP_INI };
                fileNames.AddRange(ClientConfiguration.Instance.SupplementalMapFileExtensions.Select(ext => $"spawnmap.{ext}"));

                foreach (string fileName in fileNames.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    FileInfo file = SafePath.GetFile(ProgramConstants.GamePath, fileName);
                    if (file.Exists)
                        file.CopyTo(Path.Combine(directory.FullName, fileName), true);
                }

                File.WriteAllText(Path.Combine(directory.FullName, "inputs.json"), inputsJson);
                Logger.Log("Launch capture: saved to " + directory.FullName);
            }
            catch (Exception ex)
            {
                Logger.Log("Launch capture: failed to save the capture. " + ex);
            }
        }

        public static object DescribePlayer(PlayerInfo player) => new
        {
            player.Name,
            player.SideId,
            player.StartingLocation,
            player.ColorId,
            player.TeamId,
            player.Ready,
            player.AutoReady,
            player.IsAI,
            player.IsInGame,
            player.IPAddress,
            player.Port,
            player.HashReceived,
            player.Index,
            Ping = player.Ping.ToString(),
            player.AILevel,
            player.HouseHandicapAILevel,
        };
    }
}
