using System;
using System.Collections.Generic;
using System.Linq;

using ClientCore.Extensions;

using ClientLogic.Lobby;
using ClientLogic.UI;

using CommunityToolkit.Mvvm.ComponentModel;

namespace AvClientView.ViewModels;

/// <summary>
/// The XNA LoadOrSaveGameOptionPresetWindow: pick a saved preset to load, or a preset (or a new name) to save the
/// game options as; presets can be deleted.
/// </summary>
public sealed partial class GameOptionPresetsViewModel : ObservableObject
{
    private readonly IDialogService dialogs;
    private Action<string> onLoad;
    private Action<string> onSave;

    public GameOptionPresetsViewModel(IDialogService dialogs) => this.dialogs = dialogs;

    [ObservableProperty]
    private bool isOpen;

    [ObservableProperty]
    private bool isLoad;

    [ObservableProperty]
    private IReadOnlyList<string> items = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNewPresetName), nameof(CanLoadOrSave), nameof(CanDelete))]
    private int selectedIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLoadOrSave))]
    private string newPresetName = string.Empty;

    public string Header => IsLoad ? "Load Preset".L10N("Client:Main:LoadPreset") : "Save Preset".L10N("Client:Main:SavePreset");

    public string LoadSaveText => IsLoad ? "Load".L10N("Client:Main:ButtonLoad") : "Save".L10N("Client:Main:ButtonSave");

    /// <summary>The first item: "[Select Preset]" (load, not selectable) or "[Create New]" (save).</summary>
    private bool IsFirstItemSelected => SelectedIndex <= 0;

    /// <summary>The new preset name field shows in save mode with "[Create New]" selected.</summary>
    public bool ShowNewPresetName => !IsLoad && IsFirstItemSelected;

    public bool CanLoadOrSave => IsLoad ? !IsFirstItemSelected : !IsFirstItemSelected || !string.IsNullOrWhiteSpace(NewPresetName);

    public bool CanDelete => !IsFirstItemSelected;

    /// <param name="isLoad">Load mode, else save mode.</param>
    public void Open(bool isLoad, Action<string> onLoad, Action<string> onSave)
    {
        this.onLoad = onLoad;
        this.onSave = onSave;
        IsLoad = isLoad;
        OnPropertyChanged(nameof(Header));
        OnPropertyChanged(nameof(LoadSaveText));
        ListPresets();
        NewPresetName = string.Empty;
        IsOpen = true;
    }

    private void ListPresets()
    {
        string first = IsLoad ? "[Select Preset]".L10N("Client:Main:SelectPreset") : "[Create New]".L10N("Client:Main:CreateNewPreset");
        Items = [first, .. LobbySession.GameOptionPresetNames()];
        SelectedIndex = 0;
        OnPropertyChanged(nameof(ShowNewPresetName));
        OnPropertyChanged(nameof(CanLoadOrSave));
        OnPropertyChanged(nameof(CanDelete));
    }

    public void LoadOrSave()
    {
        if (!CanLoadOrSave)
            return;

        string selected = Items[SelectedIndex];
        IsOpen = false;

        if (IsLoad)
            onLoad?.Invoke(selected);
        else
            onSave?.Invoke(IsFirstItemSelected ? NewPresetName : selected);
    }

    public void Delete()
    {
        if (!CanDelete)
            return;

        string selected = Items[SelectedIndex];
        dialogs.Confirm("Confirm Preset Delete".L10N("Client:Main:ConfirmPresetDeleteTitle"),
            "Are you sure you want to delete this preset?".L10N("Client:Main:ConfirmPresetDeleteText") + "\n\n" + selected,
            () =>
            {
                LobbySession.DeleteGameOptionPreset(selected);
                Items = Items.Where(item => item != selected).ToList();
                SelectedIndex = 0;
            });
    }

    public void Cancel() => IsOpen = false;
}
