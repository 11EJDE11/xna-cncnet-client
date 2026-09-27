using System;
using System.Collections.Generic;
using System.Linq;

using ClientCore;

using DTAClient.Domain.Multiplayer.CnCNet;

namespace ClientLogic.Tunnels;

/// <summary>
/// The tunnel server list's state, as the XNA TunnelListBox keeps it: the tunnels of the target version, the
/// selection, the best official or recommended tunnel (lowest rating) that is selected until the user picks one, and
/// the user's pick, kept across refreshes.
/// </summary>
public sealed class TunnelListState
{
    private readonly Func<IEnumerable<CnCNetTunnel>> allTunnels;

    private int? targetVersion;
    private int bestTunnelIndex;
    private int lowestTunnelRating = int.MaxValue;
    private bool isManuallySelectedTunnel;
    private string manuallySelectedTunnelKey;

    /// <param name="allTunnels">The tunnel handler's current tunnels.</param>
    public TunnelListState(Func<IEnumerable<CnCNetTunnel>> allTunnels) => this.allTunnels = allTunnels;

    /// <summary>The tunnels listed at the last refresh.</summary>
    public IReadOnlyList<CnCNetTunnel> Tunnels { get; private set; } = [];

    public int SelectedIndex { get; private set; } = -1;

    /// <summary>The list was refreshed, a tunnel's ping changed or the selection changed.</summary>
    public event EventHandler Changed;

    /// <summary>
    /// The tunnel version listed (2 or 3); null follows the tunnel mode setting. Changing it forgets the user's pick;
    /// the list then calls <see cref="Refresh"/> if it has tunnels listed.
    /// </summary>
    public int? TargetVersion
    {
        get => targetVersion;
        set
        {
            if (targetVersion == value)
                return;

            targetVersion = value;
            isManuallySelectedTunnel = false;
            manuallySelectedTunnelKey = null;
        }
    }

    public bool IsValidIndexSelected => SelectedIndex > -1 && SelectedIndex < Tunnels.Count;

    private List<CnCNetTunnel> GetFilteredTunnels()
    {
        int version = TargetVersion ?? ((TunnelMode)UserINISettings.Instance.TunnelMode.Value == TunnelMode.V2Legacy ? 2 : 3);
        return allTunnels().Where(tunnel => tunnel.Version == version).ToList();
    }

    private static string GetTunnelKey(CnCNetTunnel tunnel) => GetTunnelKey(tunnel.Address, tunnel.Port);

    private static string GetTunnelKey(string address, int port) => $"{address}:{port}";

    /// <summary>
    /// Selects a row (the list's SelectedIndex): a change to a valid row counts as the user's pick, as the XNA list's
    /// SelectedIndexChanged handler does, whoever made it.
    /// </summary>
    public void Select(int index)
    {
        if (index == SelectedIndex)
            return;

        SelectedIndex = index;

        List<CnCNetTunnel> filteredTunnels = GetFilteredTunnels();
        if (IsValidIndexSelected && SelectedIndex < filteredTunnels.Count)
        {
            isManuallySelectedTunnel = true;
            manuallySelectedTunnelKey = GetTunnelKey(filteredTunnels[SelectedIndex]);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Selects the tunnel with the given address and port, if it's listed.</summary>
    public void SelectTunnel(string address, int port)
    {
        int index = GetFilteredTunnels().FindIndex(t => t.Address == address && t.Port == port);
        if (index > -1)
        {
            Select(index);
            isManuallySelectedTunnel = true;
            manuallySelectedTunnelKey = GetTunnelKey(address, port);
        }
    }

    /// <summary>Whether the tunnel with the given address and port is the selected one.</summary>
    public bool IsTunnelSelected(string address, int port) =>
        GetFilteredTunnels().FindIndex(t => t.Address == address && t.Port == port) == SelectedIndex;

    public CnCNetTunnel GetSelectedTunnel() => IsValidIndexSelected ? GetFilteredTunnels()[SelectedIndex] : null;

    /// <summary>Lists the tunnels again (the tunnel handler refreshed them) and selects the pick or the best one.</summary>
    public void Refresh()
    {
        // Clearing the list deselects
        Tunnels = [];
        SelectedIndex = -1;

        List<CnCNetTunnel> filteredTunnels = GetFilteredTunnels();
        Tunnels = filteredTunnels;
        bestTunnelIndex = 0;
        lowestTunnelRating = int.MaxValue;

        for (int tunnelIndex = 0; tunnelIndex < filteredTunnels.Count; tunnelIndex++)
        {
            CnCNetTunnel tunnel = filteredTunnels[tunnelIndex];

            if ((tunnel.Official || tunnel.Recommended) && tunnel.Ping.IsValid())
            {
                int rating = GetTunnelRating(tunnel);
                if (rating < lowestTunnelRating)
                {
                    bestTunnelIndex = tunnelIndex;
                    lowestTunnelRating = rating;
                }
            }
        }

        if (filteredTunnels.Count > 0)
        {
            if (!isManuallySelectedTunnel)
            {
                Select(bestTunnelIndex);
                isManuallySelectedTunnel = false;
            }
            else
            {
                int manuallySelectedIndex = filteredTunnels.FindIndex(t => GetTunnelKey(t) == manuallySelectedTunnelKey);

                if (manuallySelectedIndex == -1)
                {
                    Select(bestTunnelIndex);
                    isManuallySelectedTunnel = false;
                    manuallySelectedTunnelKey = null;
                }
                else
                {
                    Select(manuallySelectedIndex);
                }
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// A tunnel's ping was measured: until the user picks one, a better official or recommended tunnel is selected.
    /// </summary>
    /// <returns>The tunnel's row, or -1 if it isn't listed.</returns>
    public int OnTunnelPinged(string address, int port)
    {
        CnCNetTunnel tunnel = allTunnels().FirstOrDefault(t => t.Address == address && t.Port == port);
        if (tunnel == null)
            return -1;

        int filteredIndex = GetFilteredTunnels().FindIndex(t => t.Address == address && t.Port == port);
        if (filteredIndex == -1)
            return -1;

        if (tunnel.Ping.IsValid())
        {
            int rating = GetTunnelRating(tunnel);

            if (!isManuallySelectedTunnel && (tunnel.Recommended || tunnel.Official) && rating < lowestTunnelRating)
            {
                bestTunnelIndex = filteredIndex;
                lowestTunnelRating = rating;
                Select(filteredIndex);
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return filteredIndex;
    }

    /// <summary>A tunnel's rating: lower is better (the square of the ping times the usage percentage).</summary>
    public static int GetTunnelRating(CnCNetTunnel tunnel)
    {
        double usageRatio = (double)tunnel.Clients / tunnel.MaxClients;

        if (usageRatio == 0)
            usageRatio = 0.1;

        usageRatio *= 100.0;

        return Convert.ToInt32(Math.Pow(tunnel.Ping.Milliseconds, 2.0) * usageRatio);
    }
}
