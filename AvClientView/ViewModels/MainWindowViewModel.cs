using System;

using CommunityToolkit.Mvvm.ComponentModel;

using Microsoft.Extensions.DependencyInjection;

namespace AvClientView.ViewModels;

/// <summary>The main window: shows the main menu, the skirmish lobby or the LAN screens.</summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly IServiceProvider services;

    public MainWindowViewModel(MainMenuViewModel menu, IServiceProvider services)
    {
        this.services = services;
        Menu = menu;
        Title = menu.Title;
        currentPage = menu;

        menu.SkirmishRequested += (_, _) => OpenSkirmish();
        menu.LanRequested += (_, _) => OpenLan();
    }

    public MainMenuViewModel Menu { get; }

    public string Title { get; }

    [ObservableProperty]
    private object currentPage;

    public event EventHandler ExitRequested
    {
        add => Menu.ExitRequested += value;
        remove => Menu.ExitRequested -= value;
    }

    private void OpenSkirmish()
    {
        var skirmish = services.GetRequiredService<SkirmishViewModel>();
        skirmish.BackRequested += (_, _) => CurrentPage = Menu;
        CurrentPage = skirmish;
    }

    private LanLobbyViewModel lanLobby;

    private void OpenLan()
    {
        if (lanLobby == null)
        {
            lanLobby = services.GetRequiredService<LanLobbyViewModel>();
            lanLobby.BackRequested += (_, _) => CurrentPage = Menu;
            lanLobby.RoomEntered += (_, _) => CurrentPage = lanLobby.Room;
            lanLobby.RoomLeft += (_, _) => CurrentPage = lanLobby;
        }

        lanLobby.Open();
        CurrentPage = lanLobby;
    }
}
