using System;

using ClientLogic.Skirmish;
using ClientLogic.UI;

using CommunityToolkit.Mvvm.Input;

using DTAClient.Domain.Multiplayer;

namespace AvClientView.ViewModels;

/// <summary>The skirmish lobby screen, over a <see cref="SkirmishSession"/>.</summary>
public sealed partial class SkirmishViewModel : LobbyViewModelBase
{
    private readonly SkirmishSession session;
    private readonly IDialogService dialogs;

    public SkirmishViewModel(SkirmishSession session, MapLoader mapLoader, IDialogService dialogs)
        : base(session, mapLoader)
    {
        this.session = session;
        this.dialogs = dialogs;

        session.LoadSettings();
        Refresh();
        session.Opened();
    }

    public event EventHandler BackRequested;

    [RelayCommand]
    private void Launch()
    {
        string error = session.Launch();
        if (error != null)
            dialogs.ShowMessage("Cannot launch game", error);
    }

    [RelayCommand]
    private void Back()
    {
        session.Closed();
        BackRequested?.Invoke(this, EventArgs.Empty);
    }
}
