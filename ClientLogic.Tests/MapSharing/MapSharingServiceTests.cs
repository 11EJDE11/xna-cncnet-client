using System;
using System.Collections.Generic;

using ClientLogic.MapSharing;
using ClientLogic.UI;

using DTAClient.Domain.Multiplayer;

using Xunit;

namespace ClientLogic.Tests.MapSharing;

/// <summary>The map sharing decisions that don't need the CnCNet map database.</summary>
public class MapSharingServiceTests
{
    private sealed class Fakes : IMapSharingLobby, IMapSharingTransport, INoticeSink, IUiDispatcher
    {
        public bool IsHost { get; set; }
        public string HostName { get; set; } = "Host";
        public Map Map { get; set; }
        public Dictionary<string, Map> InstalledMaps { get; } = [];
        public List<string> Sent { get; } = [];
        public List<(string Message, NoticeSeverity Severity)> Notices { get; } = [];

        public Map FindMap(string sha1) => InstalledMaps.TryGetValue(sha1, out Map map) ? map : null;
        public void SendMapSharingMessage(string message) => Sent.Add(message);
        public void AddNotice(string message, NoticeSeverity severity) => Notices.Add((message, severity));
        public void Post(Action action) => action();
        public bool CheckAccess() => true;
    }

    private static (MapSharingService Service, Fakes Fakes) Create()
    {
        TestGame.EnsureInitialized();
        var fakes = new Fakes();
        return (new MapSharingService(fakes, fakes, fakes, fakes, "yr"), fakes);
    }

    [Fact]
    public void MissingMapReportsGoToThePlayers()
    {
        var (service, fakes) = Create();

        service.ReportMapSharingDisabled();
        service.ReportOfficialMapMissing("abc");

        Assert.Equal(new[] { "MAPSDISABLED", "MAPFAIL abc" }, fakes.Sent);
        Assert.Equal(2, fakes.Notices.Count);
    }

    [Fact]
    public void TheHostsMapAndChatCommandDownloadsAreExpected()
    {
        var (service, _) = Create();

        service.SetHostMap("hostmap", "Host Map");

        Assert.True(service.IsExpectedDownload("hostmap"));
        Assert.False(service.IsExpectedDownload("other"));
        Assert.Equal("Host Map", service.LastMapName);
    }

    [Fact]
    public void TransferFailuresAreAnnouncedByWho()
    {
        var (service, fakes) = Create();
        service.SetHostMap("hostmap", "Host Map");

        // From the host: the upload failed
        service.HandleMapTransferFailMessage("Host", "hostmap");
        // From another player, about another map: ignored
        service.HandleMapTransferFailMessage("Bob", "other");
        // From another player about the current map
        service.HandleMapTransferFailMessage("Bob", "hostmap");

        Assert.Equal(3, fakes.Notices.Count); // host failed + host must change map + Bob failed
        Assert.Contains("Bob", fakes.Notices[2].Message);
        Assert.Empty(fakes.Sent);
    }

    [Fact]
    public void DownloadByIdOfAnInstalledMapIsRefused()
    {
        var (service, fakes) = Create();
        fakes.InstalledMaps["0123"] = TestGame.LoadGameModeMap("Maps/Test/four", "Battle").Map;

        service.DownloadMapById("0123? My Map");

        (string message, NoticeSeverity severity) = Assert.Single(fakes.Notices);
        Assert.Equal(NoticeSeverity.Warning, severity);
        Assert.Contains("Maps/Test/four", message);
        Assert.False(service.IsExpectedDownload("0123"));
    }

    [Fact]
    public void MapSharingBlockedNamesThePlayer()
    {
        var (service, fakes) = Create();

        service.HandleMapSharingBlockedMessage("Bob");

        Assert.Contains("Bob", Assert.Single(fakes.Notices).Message);
    }
}
