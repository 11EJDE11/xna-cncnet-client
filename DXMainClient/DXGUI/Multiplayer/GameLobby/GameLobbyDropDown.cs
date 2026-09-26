using ClientLogic.Options;

using DTAClient.DXGUI.Generic;

using Rampastring.Tools;
using Rampastring.XNAUI;
using Rampastring.XNAUI.XNAControls;

namespace DTAClient.DXGUI.Multiplayer.GameLobby;

public class GameLobbyDropDown : GameSessionDropDown
{
    public GameLobbyDropDown(WindowManager windowManager) : base(windowManager) { }

    /// <summary>The last host-defined selection (<see cref="GameOption.HostValue"/>).</summary>
    public int HostSelectedIndex
    {
        get => Option.HostValue;
        set => Option.HostValue = value;
    }

    /// <summary>The last selection the local player made (<see cref="GameOption.UserValue"/>).</summary>
    public int UserSelectedIndex
    {
        get => Option.UserValue;
        set => Option.UserValue = value;
    }

    public override void Initialize()
    {
        // Find the game lobby that this control belongs to and register ourselves as a game option.

        XNAControl parent = Parent;
        while (true)
        {
            if (parent == null)
                break;

            // oh no, we have a circular class reference here!
            if (parent is GameLobbyBase configView)
            {
                configView.DropDowns.Add(this);
                configView.GameOptions.Add(Option);
                break;
            }

            parent = parent.Parent;
        }

        base.Initialize();
    }

    public override void OnLeftClick(InputEventArgs inputEventArgs)
    {
        // FIXME there's a discrepancy with how base XNAUI handles this
        // it doesn't set handled if changing the setting is not allowed
        inputEventArgs.Handled = true;

        if (!AllowDropDown)
            return;

        base.OnLeftClick(inputEventArgs);
        UserSelectedIndex = SelectedIndex;
    }

    public override void OnMouseScrolled(InputEventArgs inputEventArgs)
    {
        base.OnMouseScrolled(inputEventArgs);

        // Scrolling over a closed drop-down changes the selection like a click does
        if (AllowDropDown)
            UserSelectedIndex = SelectedIndex;
    }
}