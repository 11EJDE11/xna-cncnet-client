using System;
using System.Collections.Generic;
using System.IO;

using ClientCore;

using ClientLogic.UI;

using NAudio.Wave;
using NAudio.Wave.SampleProviders;

using Rampastring.Tools;

namespace AvClientView.Theme;

/// <summary>
/// A theme sound, as XNA's EnhancedSoundEffect: found with the texture search order, silent if the file doesn't
/// exist, optionally not repeated within a number of seconds, played at the client volume (ClientVolume). Played
/// with NAudio (WinMM), each play on its own output so sounds can overlap.
/// </summary>
public sealed class ThemeSound
{
    private static readonly Dictionary<string, byte[]> cache = new(StringComparer.OrdinalIgnoreCase);

    private readonly byte[] data;
    private DateTime lastPlayTime;

    private ThemeSound(byte[] data, float repeatPrevention)
    {
        this.data = data;
        RepeatPrevention = repeatPrevention;
    }

    public bool Enabled { get; set; } = true;

    /// <summary>Seconds after playing during which the sound isn't played again; 0 for none.</summary>
    public float RepeatPrevention { get; }

    public static ThemeSound Load(string name, float repeatPrevention = 0f)
    {
        if (!cache.TryGetValue(name, out byte[] data))
        {
            string path = ThemeAssets.FindFile(name);
            data = path == null ? null : File.ReadAllBytes(path);
            cache[name] = data;
        }

        return new ThemeSound(data, repeatPrevention);
    }

    public void Play()
    {
        if (!Enabled || data == null)
            return;

        if (RepeatPrevention > 0f)
        {
            DateTime now = DateTime.Now;
            if ((now - lastPlayTime).TotalSeconds < RepeatPrevention)
                return;

            lastPlayTime = now;
        }

        // The options window's slider while it is open, else the saved client volume
        float volume = (float)(ViewModels.OptionsWindowViewModel.PreviewClientVolume ?? UserINISettings.Instance.ClientVolume.Value);
        if (volume <= 0f)
            return;

        try
        {
            var reader = new WaveFileReader(new MemoryStream(data));
            var output = new WaveOutEvent();
            output.Init(new VolumeSampleProvider(reader.ToSampleProvider()) { Volume = volume });
            output.PlaybackStopped += (_, _) =>
            {
                output.Dispose();
                reader.Dispose();
            };
            output.Play();
        }
        catch (Exception ex)
        {
            Logger.Log("ThemeSound: playing a sound failed: " + ex.Message);
        }
    }
}

/// <summary>The theme's UI sounds (XNAClientButton hover, XNAClientCheckBox, XNAClientDropDown).</summary>
public static class ThemeSounds
{
    private static ThemeSound buttonHover;
    private static ThemeSound checkBox;
    private static ThemeSound dropDown;

    public static ThemeSound ButtonHover => buttonHover ??= ThemeSound.Load("button.wav");

    public static ThemeSound CheckBox => checkBox ??= ThemeSound.Load("checkbox.wav");

    public static ThemeSound DropDown => dropDown ??= ThemeSound.Load("dropdown.wav");
}

/// <summary>The lobby sounds with their configured cooldowns, as XnaLobbySoundService.</summary>
public sealed class ThemeLobbySoundService : ISoundService
{
    private readonly Dictionary<LobbySound, ThemeSound> sounds;

    public ThemeLobbySoundService()
    {
        ClientConfiguration config = ClientConfiguration.Instance;

        sounds = new Dictionary<LobbySound, ThemeSound>
        {
            [LobbySound.PlayerJoined] = ThemeSound.Load("joingame.wav", config.SoundGameLobbyJoinCooldown),
            [LobbySound.PlayerLeft] = ThemeSound.Load("leavegame.wav", config.SoundGameLobbyLeaveCooldown),
            [LobbySound.Message] = ThemeSound.Load("message.wav", config.SoundMessageCooldown),
            [LobbySound.GetReady] = ThemeSound.Load("getready.wav", config.SoundGameLobbyGetReadyCooldown),
            [LobbySound.PlayerReturned] = ThemeSound.Load("return.wav", config.SoundGameLobbyReturnCooldown),
        };
    }

    public void Play(LobbySound sound) => sounds[sound].Play();

    public void SetEnabled(LobbySound sound, bool enabled) => sounds[sound].Enabled = enabled;
}
