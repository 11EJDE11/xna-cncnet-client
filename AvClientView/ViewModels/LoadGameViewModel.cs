using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

using Avalonia.Threading;

using ClientCore.Extensions;

using ClientLogic.Campaign;
using ClientLogic.Launch;
using ClientLogic.UI;

using CommunityToolkit.Mvvm.ComponentModel;

using DTAClient.Domain;

namespace AvClientView.ViewModels;

/// <summary>A saved game row: its name and date / time.</summary>
public sealed record SavedGameItemViewModel(SavedGame SavedGame, string Name, string DateTime);

/// <summary>The load game window, as the XNA GameLoadingWindow: the saved games, Load, Delete (with confirmation), Cancel.</summary>
public sealed partial class LoadGameViewModel : ObservableObject
{
    private readonly GameProcessService gameProcess;
    private readonly IDialogService dialogs;
    private readonly CampaignViewModel campaign;
    private List<SavedGame> savedGames = [];

    private readonly DiscordHandler discord;

    public LoadGameViewModel(GameProcessService gameProcess, IDialogService dialogs, CampaignViewModel campaign, DiscordHandler discord)
    {
        this.discord = discord;
        this.gameProcess = gameProcess;
        this.dialogs = dialogs;
        this.campaign = campaign;
    }

    public ObservableCollection<SavedGameItemViewModel> SavedGames { get; } = [];

    [ObservableProperty]
    private bool isOpen;

    [ObservableProperty]
    private int selectedIndex = -1;

    public bool HasSelection => SelectedIndex >= 0 && SelectedIndex < savedGames.Count;

    partial void OnSelectedIndexChanged(int value) => OnPropertyChanged(nameof(HasSelection));

    public void Open()
    {
        ListSaves();
        IsOpen = true;
    }

    public void Cancel() => IsOpen = false;

    public void ListSaves()
    {
        SelectedIndex = -1;
        SavedGames.Clear();
        savedGames = SavedGameLoader.ListSaves();
        foreach (SavedGame sg in savedGames)
            SavedGames.Add(new SavedGameItemViewModel(sg, sg.GUIName, sg.LastModified.ToString()));
    }

    public void Load()
    {
        if (!HasSelection)
            return;

        SavedGame sg = savedGames[SelectedIndex];
        Mission mission = campaign.Catalog.UniqueIDToMissions.GetValueOrDefault(sg.CustomMissionID, null);
        SavedGameLoader.WriteSpawnFiles(sg, mission);

        discord.UpdatePresence(sg.GUIName, true);

        IsOpen = false;
        gameProcess.GameProcessExited += GameProcessExited_Callback;
        gameProcess.Start(dialogs);
    }

    private void GameProcessExited_Callback() => Dispatcher.UIThread.Post(() =>
    {
        gameProcess.GameProcessExited -= GameProcessExited_Callback;
        CustomMissionHelper.DeleteSupplementalMissionFiles();
        discord.UpdatePresence();
    });

    public void Delete()
    {
        if (!HasSelection)
            return;

        SavedGame sg = savedGames[SelectedIndex];
        dialogs.Confirm("Delete Confirmation".L10N("Client:Main:DeleteConfirmationTitle"),
            string.Format(("The following saved game will be deleted permanently:\n\n" +
                "Filename: {0}\n" +
                "Saved game name: {1}\n" +
                "Date and time: {2}\n\n" +
                "Are you sure you want to proceed?").L10N("Client:Main:DeleteConfirmationText"),
                sg.FileName, sg.GUIName, sg.LastModified.ToString()),
            () =>
            {
                SavedGameLoader.Delete(sg);
                ListSaves();
            });
    }
}
