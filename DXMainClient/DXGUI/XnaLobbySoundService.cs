using System.Collections.Generic;

using ClientCore;

using ClientLogic.UI;

using Rampastring.XNAUI;

namespace DTAClient.DXGUI
{
    /// <summary>
    /// Plays the lobby sounds with <see cref="EnhancedSoundEffect"/>, each with its configured cooldown.
    /// Each lobby has its own instance, so the cooldowns are per lobby. Create it once the audio is ready
    /// (in the lobby's Initialize).
    /// </summary>
    public sealed class XnaLobbySoundService : ISoundService
    {
        private readonly Dictionary<LobbySound, EnhancedSoundEffect> sounds;

        public XnaLobbySoundService()
        {
            ClientConfiguration config = ClientConfiguration.Instance;

            sounds = new Dictionary<LobbySound, EnhancedSoundEffect>
            {
                [LobbySound.PlayerJoined] = new EnhancedSoundEffect("joingame.wav", 0.0, 0.0, config.SoundGameLobbyJoinCooldown),
                [LobbySound.PlayerLeft] = new EnhancedSoundEffect("leavegame.wav", 0.0, 0.0, config.SoundGameLobbyLeaveCooldown),
                [LobbySound.Message] = new EnhancedSoundEffect("message.wav", 0.0, 0.0, config.SoundMessageCooldown),
                [LobbySound.GetReady] = new EnhancedSoundEffect("getready.wav", 0.0, 0.0, config.SoundGameLobbyGetReadyCooldown),
                [LobbySound.PlayerReturned] = new EnhancedSoundEffect("return.wav", 0.0, 0.0, config.SoundGameLobbyReturnCooldown),
            };
        }

        public void Play(LobbySound sound) => sounds[sound].Play();

        public void SetEnabled(LobbySound sound, bool enabled) => sounds[sound].Enabled = enabled;
    }
}
