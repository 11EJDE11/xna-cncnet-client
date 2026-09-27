using System;
using System.IO;

using Avalonia.Threading;

using ClientCore;

using NAudio.Wave;
using NAudio.Wave.SampleProviders;

using Rampastring.Tools;

namespace AvClientView.Theme;

/// <summary>
/// The main menu music, as the XNA main menu plays it (the DirectX build's MediaPlayer): the theme's MainMenuTheme
/// (Resources/{name}.wma), repeated at the client volume while PlayMainMenuMusic is on, and faded out over a second.
/// </summary>
public sealed class ThemeMusic : IDisposable
{
    private readonly DispatcherTimer fadeTimer;
    private WaveOutEvent output;
    private MediaFoundationReader reader;
    private VolumeSampleProvider volume;
    private bool loadFailed;

    public ThemeMusic()
    {
        fadeTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Background, (_, _) => FadeStep());
    }

    public bool IsPlaying => output?.PlaybackState == PlaybackState.Playing;

    private bool IsFading => fadeTimer.IsEnabled;

    /// <summary>Starts the music (PlayMusic), if it's enabled and the theme has it.</summary>
    public void Play()
    {
        if (!UserINISettings.Instance.PlayMainMenuMusic || !Load())
            return;

        try
        {
            fadeTimer.Stop();
            volume.Volume = (float)UserINISettings.Instance.ClientVolume;

            if (!IsPlaying)
            {
                reader.Position = 0;
                output.Play();
            }
        }
        catch (Exception ex)
        {
            Logger.Log("Playing main menu music failed! " + ex);
        }
    }

    /// <summary>Fades the music out (MusicOff), if it's playing.</summary>
    public void FadeOut()
    {
        if (IsPlaying && !IsFading)
            fadeTimer.Start();
    }

    /// <summary>The settings were saved (SettingsSaved): music turned off fades; turned on plays if the menu is shown.</summary>
    public void SettingsSaved(bool mainMenuShown)
    {
        if (IsPlaying)
        {
            if (!UserINISettings.Instance.PlayMainMenuMusic)
                FadeOut();
            else if (!IsFading)
                volume.Volume = (float)UserINISettings.Instance.ClientVolume;
        }
        else if (mainMenuShown)
        {
            Play();
        }
    }

    private void FadeStep()
    {
        // Fade during 1 second, as the XNA menu's step of the volume times the elapsed time
        float step = (float)UserINISettings.Instance.ClientVolume * (float)fadeTimer.Interval.TotalSeconds;

        if (volume.Volume > step)
        {
            volume.Volume -= step;
            return;
        }

        fadeTimer.Stop();
        output?.Stop();
    }

    private bool Load()
    {
        if (output != null)
            return true;

        if (loadFailed)
            return false;

        FileInfo file = SafePath.GetFile(ProgramConstants.GamePath, ProgramConstants.BASE_RESOURCE_PATH,
            FormattableString.Invariant($"{ClientConfiguration.Instance.MainMenuMusicName}.wma"));

        if (!file.Exists)
        {
            loadFailed = true;
            return false;
        }

        try
        {
            reader = new MediaFoundationReader(file.FullName);
            volume = new VolumeSampleProvider(new LoopingSampleProvider(reader));
            output = new WaveOutEvent();
            output.Init(volume);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Log("Loading the main menu music failed: " + ex);
            loadFailed = true;
            Dispose();
            return false;
        }
    }

    public void Dispose()
    {
        fadeTimer.Stop();
        output?.Dispose();
        reader?.Dispose();
        output = null;
        reader = null;
    }

    /// <summary>Plays a wave stream from its start again when it ends (MediaPlayer.IsRepeating).</summary>
    private sealed class LoopingSampleProvider(WaveStream source) : ISampleProvider
    {
        private readonly ISampleProvider samples = source.ToSampleProvider();

        public WaveFormat WaveFormat => samples.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            int total = 0;
            while (total < count)
            {
                int read = samples.Read(buffer, offset + total, count - total);
                if (read == 0)
                {
                    if (source.Position == 0)
                        break;

                    source.Position = 0;
                    continue;
                }

                total += read;
            }

            return total;
        }
    }
}
