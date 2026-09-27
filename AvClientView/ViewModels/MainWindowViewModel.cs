using System;

using CommunityToolkit.Mvvm.ComponentModel;

using Microsoft.Extensions.DependencyInjection;

namespace AvClientView.ViewModels;

/// <summary>The main window: shows the main menu or the skirmish lobby.</summary>
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
}
