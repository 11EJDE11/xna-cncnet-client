using CommunityToolkit.Mvvm.ComponentModel;

namespace ClientLogic.Settings;

/// <summary>The options window's Audio tab, as DXMainClient's AudioOptionsPanel: volumes 0-10 and the music/sound options.</summary>
public sealed partial class AudioOptionsModel : OptionsPanelModel
{
    public const int VOLUME_MIN = 0;
    public const int VOLUME_MAX = 10;
    private const int VOLUME_SCALE = 10;

    public AudioOptionsModel() : base("AudioOptionsPanel")
    {
    }

    [ObservableProperty]
    private int scoreVolume;

    [ObservableProperty]
    private int soundVolume;

    [ObservableProperty]
    private int voiceVolume;

    [ObservableProperty]
    private bool scoreShuffle;

    /// <summary>The client volume; the client's sounds follow it at once (before saving), as in XNA.</summary>
    [ObservableProperty]
    private int clientVolume;

    [ObservableProperty]
    private bool mainMenuMusic;

    [ObservableProperty]
    private bool stopMusicOnMenu;

    /// <summary>"Don't play main menu music in lobbies" can only be checked with main menu music on.</summary>
    [ObservableProperty]
    private bool canStopMusicOnMenu = true;

    [ObservableProperty]
    private bool stopGameLobbyMessageAudio;

    [ObservableProperty]
    private bool playSoundOnGameHosted;

    partial void OnMainMenuMusicChanged(bool value)
    {
        CanStopMusicOnMenu = value;
        StopMusicOnMenu = value;
    }

    public override void Load()
    {
        base.Load();
        ScoreVolume = (int)(IniSettings.ScoreVolume * VOLUME_SCALE);
        SoundVolume = (int)(IniSettings.SoundVolume * VOLUME_SCALE);
        VoiceVolume = (int)(IniSettings.VoiceVolume * VOLUME_SCALE);
        ScoreShuffle = IniSettings.IsScoreShuffle;
        ClientVolume = (int)(IniSettings.ClientVolume * VOLUME_SCALE);
        MainMenuMusic = IniSettings.PlayMainMenuMusic;
        StopMusicOnMenu = IniSettings.StopMusicOnMenu;
        StopGameLobbyMessageAudio = IniSettings.StopGameLobbyMessageAudio;
        PlaySoundOnGameHosted = IniSettings.PlaySoundOnGameHosted;
    }

    public override bool Save()
    {
        bool restartRequired = base.Save();
        IniSettings.ScoreVolume.Value = ScoreVolume / (double)VOLUME_SCALE;
        IniSettings.SoundVolume.Value = SoundVolume / (double)VOLUME_SCALE;
        IniSettings.VoiceVolume.Value = VoiceVolume / (double)VOLUME_SCALE;
        IniSettings.IsScoreShuffle.Value = ScoreShuffle;
        IniSettings.ClientVolume.Value = ClientVolume / (double)VOLUME_SCALE;
        IniSettings.PlayMainMenuMusic.Value = MainMenuMusic;
        IniSettings.StopMusicOnMenu.Value = StopMusicOnMenu;
        IniSettings.StopGameLobbyMessageAudio.Value = StopGameLobbyMessageAudio;
        IniSettings.PlaySoundOnGameHosted.Value = PlaySoundOnGameHosted;
        return restartRequired;
    }
}
