using ClientCore;

using ClientGUI;

using Rampastring.Tools;
using Rampastring.XNAUI;
using Rampastring.XNAUI.XNAControls;

namespace DTAClient.DXGUI.Multiplayer.GameLobby
{
    /// <summary>
    /// A lobby option that belongs to this client alone: it is written to the local spawn.ini and
    /// remembered in the user's settings, but never broadcast to other players and never changed by
    /// the host.
    ///
    /// Deliberately not a <see cref="GameLobbyCheckBox"/>. Those register themselves into
    /// <see cref="GameLobbyBase.CheckBoxes"/>, which is serialised positionally into the game
    /// options message - LANGameLobby validates the exact field count - so adding one there would
    /// break option parsing between clients of different versions. It is also simply the wrong
    /// model for something like replay recording, where each player records their own game from
    /// their own point of view and there is nothing to keep in sync.
    /// </summary>
    public class LocalGameLobbyCheckBox : XNAClientCheckBox
    {
        public LocalGameLobbyCheckBox(WindowManager windowManager) : base(windowManager) { }

        /// <summary>
        /// The [Settings] key written to spawn.ini.
        /// </summary>
        public string SpawnIniOption { get; private set; }

        /// <summary>
        /// Key in the [Replays] section of the user's settings that this check box persists to, so
        /// the choice survives between sessions and across lobbies.
        /// </summary>
        public string UserSettingKey { get; private set; }

        /// <summary>
        /// When checked, forces the game into a multiplayer session (spawn.ini ForceMultiplayer).
        /// Recording needs this in skirmish, and it changes what the game speed values mean, which
        /// is why dropdowns can declare an alternate label list for it.
        /// </summary>
        public bool ForcesMultiplayerSession { get; private set; }

        public bool AffectsSpawnIni => !string.IsNullOrWhiteSpace(SpawnIniOption);

        public override void Initialize()
        {
            // Find the game lobby that this control belongs to and register ourselves with it,
            // mirroring how GameLobbyCheckBox attaches itself.
            XNAControl parent = Parent;
            while (parent != null)
            {
                if (parent is GameLobbyBase gameLobby)
                {
                    gameLobby.LocalCheckBoxes.Add(this);
                    break;
                }

                parent = parent.Parent;
            }

            base.Initialize();

            LoadPersistedValue();
        }

        protected override void ParseControlINIAttribute(IniFile iniFile, string key, string value)
        {
            switch (key)
            {
                case "SpawnIniOption":
                    SpawnIniOption = value;
                    return;
                case "UserSettingKey":
                    UserSettingKey = value;
                    return;
                case "ForcesMultiplayerSession":
                    ForcesMultiplayerSession = Conversions.BooleanFromString(value, false);
                    return;
            }

            base.ParseControlINIAttribute(iniFile, key, value);
        }

        public void ApplySpawnIniCode(IniFile spawnIni)
        {
            if (!AffectsSpawnIni)
                return;

            spawnIni.SetBooleanValue("Settings", SpawnIniOption, Checked);
        }

        /// <summary>
        /// Writes the current value back to the user's settings. Called by the lobby when the value
        /// changes, rather than on every click, so it also covers programmatic changes.
        /// </summary>
        public void PersistValue()
        {
            if (string.IsNullOrWhiteSpace(UserSettingKey))
                return;

            UserINISettings.Instance.SetValue(UserINISettings.REPLAYS, UserSettingKey, Checked);
        }

        private void LoadPersistedValue()
        {
            if (string.IsNullOrWhiteSpace(UserSettingKey))
                return;

            // The INI's Checked= is the default for a fresh install; the user's own choice wins.
            Checked = UserINISettings.Instance.GetValue(UserINISettings.REPLAYS, UserSettingKey, Checked);
        }
    }
}
