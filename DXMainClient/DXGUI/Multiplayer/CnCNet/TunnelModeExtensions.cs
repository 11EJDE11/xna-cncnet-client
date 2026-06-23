using ClientCore.Extensions;
using DTAClient.Domain.Multiplayer.CnCNet;

namespace DTAClient.DXGUI.Multiplayer.CnCNet;

internal static class TunnelModeExtensions
{
    /// <summary>
    /// A human-readable description of the tunnel mode, used in lobby notices.
    /// </summary>
    public static string GetDescription(this TunnelMode mode) => mode switch
    {
        TunnelMode.V3Dynamic => "dynamic tunnels (V3)".L10N("Client:Main:TunnelModeDynamicV3"),
        TunnelMode.V2Legacy => "legacy tunnels (V2)".L10N("Client:Main:TunnelModeLegacyV2"),
        _ => "static tunnels (V3)".L10N("Client:Main:TunnelModeStaticV3")
    };
}
