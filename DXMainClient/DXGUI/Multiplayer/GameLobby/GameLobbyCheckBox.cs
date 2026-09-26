using System.Collections.Generic;
using System.Linq;

using ClientLogic.Options;

using DTAClient.DXGUI.Generic;

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using Rampastring.Tools;
using Rampastring.XNAUI;
using Rampastring.XNAUI.XNAControls;

namespace DTAClient.DXGUI.Multiplayer.GameLobby;

public class GameLobbyCheckBox : GameSessionCheckBox
{
    public GameLobbyCheckBox(WindowManager windowManager) : base(windowManager) { }

    /// <summary>
    /// The last host-defined value for this check box (<see cref="GameOption.HostValue"/>).
    /// Defaults to the default value of Checked after the check-box
    /// has been initialized, but its value is only changed by user interaction.
    /// </summary>
    public bool HostChecked
    {
        get => Option.HostValue != 0;
        set => Option.HostValue = value ? 1 : 0;
    }

    /// <summary>
    /// The last value that the local player gave for this check box (<see cref="GameOption.UserValue"/>).
    /// Defaults to the default value of Checked after the check-box
    /// has been initialized, but its value is only changed by user interaction.
    /// </summary>
    public bool UserChecked
    {
        get => Option.UserValue != 0;
        set => Option.UserValue = value ? 1 : 0;
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
                configView.CheckBoxes.Add(this);
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

        if (!AllowChanges)
            return;

        base.OnLeftClick(inputEventArgs);
        UserChecked = Checked;
    }

    public override void Draw(GameTime gameTime)
    {
        if (ShowIconInGameLobby)
        {
            string iconName = Checked ? EnabledIcon : DisabledIcon;
            if (!string.IsNullOrEmpty(iconName))
            {
                Texture2D icon = AssetLoader.LoadTexture(iconName);
                if (icon != null)
                {
                    const int iconSpacing = 6;
                    int iconX = -icon.Width - iconSpacing;
                    int iconY = (Height - icon.Height) / 2;

                    DrawTexture(icon, new Rectangle(iconX, iconY, icon.Width, icon.Height), Color.White);
                }
            }
        }

        base.Draw(gameTime);
    }
}