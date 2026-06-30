using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Rampastring.XNAUI;
using Rampastring.XNAUI.XNAControls;
using System;
using System.Collections.Generic;
using ClientCore.Extensions;
using ClientGUI;
using DTAClient.Domain.Multiplayer.CnCNet;

#nullable enable

namespace DTAClient.DXGUI.Multiplayer.GameLobby;

/// <summary>
/// A UI component that displays the tunnel negotiation status between players
/// </summary>
public class TunnelNegotiationStatusPanel : XNAPanel
{
    private const int CELL_WIDTH = 90;
    private const int CELL_HEIGHT = 25;
    private const int HEADER_HEIGHT = 30;
    private const int PLAYER_NAME_WIDTH_LHS = 120;
    private const int PANEL_PADDING = 15;
    private const int TITLE_HEIGHT = 25;
    private const int CLOSE_BUTTON_SIZE = 20;
    private const int TAB_HEIGHT = 26;
    private const int LIST_ROW_HEIGHT = 24;
    private const int LIST_NAME_WIDTH = 225;
    private const int LIST_BAR_MAX_WIDTH = 150;
    private const int LIST_BAR_MAX_PING = 500;
    private const int LIST_PING_LABEL_WIDTH = 70;

    private XNALabel lblTitle = null!;
    private XNAPanel matrixPanel = null!;
    private XNAPanel listPanel = null!;
    private XNAClientTabControl tabControl = null!;
    private XNAClientButton btnClose = null!;
    private readonly List<XNALabel> playerLabels = new List<XNALabel>();
    private readonly Dictionary<(string, string), XNALabel> statusCells = new Dictionary<(string, string), XNALabel>();
    private static Texture2D? sharedCellBackground;
    private static Texture2D? sharedBarBackground;
    private static Texture2D? texGreen;
    private static Texture2D? texYellow;
    private static Texture2D? texOrange;
    private static Texture2D? texRed;

    public TunnelNegotiationStatusPanel(WindowManager windowManager) : base(windowManager)
    {
    }

    public override void Initialize()
    {
        Name = nameof(TunnelNegotiationStatusPanel);
        ClientRectangle = new Rectangle(0, 0, 500, 300);
        BackgroundTexture = AssetLoader.LoadTexture("ModalBG.png");
        PanelBackgroundDrawMode = PanelBackgroundImageDrawMode.STRETCHED;
        DrawBorders = true;

        lblTitle = new XNALabel(WindowManager);
        lblTitle.Name = nameof(lblTitle);
        lblTitle.Text = "Tunnel Negotiation Status".L10N("Client:Main:NegStatusTitle");
        lblTitle.ClientRectangle = new Rectangle(PANEL_PADDING, 8, 0, 0);
        lblTitle.FontIndex = 1;

        btnClose = new XNAClientButton(WindowManager);
        btnClose.Name = nameof(btnClose);
        btnClose.IdleTexture = AssetLoader.LoadTexture("optionsButtonClose.png");
        btnClose.HoverTexture = AssetLoader.LoadTexture("optionsButtonClose_c.png");
        btnClose.ClientRectangle = new Rectangle(Width - CLOSE_BUTTON_SIZE - 8, 5, CLOSE_BUTTON_SIZE, CLOSE_BUTTON_SIZE);
        btnClose.LeftClick += BtnClose_LeftClick;

        tabControl = new XNAClientTabControl(WindowManager);
        tabControl.Name = nameof(tabControl);
        tabControl.ClientRectangle = new Rectangle(PANEL_PADDING, TITLE_HEIGHT + 3, 0, 0);
        tabControl.AddTab("List".L10N("Client:Main:NegStatusTabList"), 92);
        tabControl.AddTab("Matrix".L10N("Client:Main:NegStatusTabMatrix"), 92);
        tabControl.SelectedIndexChanged += TabControl_SelectedIndexChanged;

        int contentY = TITLE_HEIGHT + TAB_HEIGHT + PANEL_PADDING;
        int contentHeight = Height - contentY - PANEL_PADDING;

        listPanel = new XNAPanel(WindowManager);
        listPanel.Name = nameof(listPanel);
        listPanel.ClientRectangle = new Rectangle(PANEL_PADDING, contentY, Width - PANEL_PADDING * 2, contentHeight);
        listPanel.DrawBorders = false;

        matrixPanel = new XNAPanel(WindowManager);
        matrixPanel.Name = nameof(matrixPanel);
        matrixPanel.ClientRectangle = new Rectangle(PANEL_PADDING, contentY, Width - PANEL_PADDING * 2, contentHeight);
        matrixPanel.DrawBorders = false;

        AddChild(lblTitle);
        AddChild(btnClose);
        AddChild(tabControl);
        AddChild(listPanel);
        AddChild(matrixPanel);

        base.Initialize();

        matrixPanel.Disable();
        CenterOnParent();
        Disable();
    }

    private void TabControl_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (tabControl.SelectedTab == 0)
        {
            listPanel.Enable();
            matrixPanel.Disable();
        }
        else
        {
            listPanel.Disable();
            matrixPanel.Enable();
        }
    }

    private void BtnClose_LeftClick(object? sender, EventArgs e)
    {
        Disable();
    }

    public void UpdateNegotiationStatus(List<string> players, NegotiationDataManager negotiationData, bool inferInProgress = false)
    {
        while (matrixPanel.Children.Count > 0)
            matrixPanel.RemoveChild(matrixPanel.Children[0]);
        while (listPanel.Children.Count > 0)
            listPanel.RemoveChild(listPanel.Children[0]);

        playerLabels.Clear();
        statusCells.Clear();

        if (players.Count < 2)
            return;

        int pairCount = players.Count * (players.Count - 1) / 2;
        int matrixWidth = PLAYER_NAME_WIDTH_LHS + (players.Count * CELL_WIDTH) + (PANEL_PADDING * 2);
        int listWidth = LIST_NAME_WIDTH + LIST_BAR_MAX_WIDTH + LIST_PING_LABEL_WIDTH + 20 + (PANEL_PADDING * 2);

        int matrixContentHeight = HEADER_HEIGHT + (players.Count * CELL_HEIGHT);
        int listContentHeight = pairCount * LIST_ROW_HEIGHT;

        int contentY = TITLE_HEIGHT + TAB_HEIGHT + PANEL_PADDING;
        int contentHeight = Math.Max(matrixContentHeight, listContentHeight);

        Width = Math.Max(500, Math.Max(matrixWidth, listWidth));
        Height = Math.Max(300, contentY + contentHeight + PANEL_PADDING);

        btnClose.ClientRectangle = new Rectangle(Width - CLOSE_BUTTON_SIZE - 8, 5, CLOSE_BUTTON_SIZE, CLOSE_BUTTON_SIZE);

        listPanel.ClientRectangle = new Rectangle(PANEL_PADDING, contentY, Width - PANEL_PADDING * 2, Height - contentY - PANEL_PADDING);
        matrixPanel.ClientRectangle = new Rectangle(PANEL_PADDING, contentY, Width - PANEL_PADDING * 2, Height - contentY - PANEL_PADDING);

        CenterOnParent();

        BuildMatrixView(players, negotiationData, inferInProgress);
        BuildListView(players, negotiationData, inferInProgress);
    }

    private void BuildMatrixView(List<string> players, NegotiationDataManager negotiationData, bool inferInProgress = false)
    {
        for (int i = 0; i < players.Count; i++)
        {
            var headerLabel = new XNALabel(WindowManager);
            string displayName = players[i];
            headerLabel.Text = displayName;
            headerLabel.TextAnchor = LabelTextAnchorInfo.CENTER;
            headerLabel.AnchorPoint = new Vector2(
                PLAYER_NAME_WIDTH_LHS + (i * CELL_WIDTH) + (CELL_WIDTH / 2f),
                HEADER_HEIGHT / 2f);
            headerLabel.TextColor = Color.LightBlue;
            matrixPanel.AddChild(headerLabel);
        }

        for (int i = 0; i < players.Count; i++)
        {
            var rowLabel = new XNALabel(WindowManager);
            rowLabel.Text = players[i];
            rowLabel.ClientRectangle = new Rectangle(
                0,
                HEADER_HEIGHT + (i * CELL_HEIGHT),
                PLAYER_NAME_WIDTH_LHS - 5,
                CELL_HEIGHT);
            rowLabel.TextColor = Color.LightBlue;
            matrixPanel.AddChild(rowLabel);
            playerLabels.Add(rowLabel);

            for (int j = 0; j < players.Count; j++)
            {
                if (i == j)
                    continue;

                sharedCellBackground ??= AssetLoader.CreateTexture(new Color(30, 30, 30, 120), 1, 1);

                var cellPanel = new XNAPanel(WindowManager)
                {
                    ClientRectangle = new Rectangle(
                        PLAYER_NAME_WIDTH_LHS + (j * CELL_WIDTH),
                        HEADER_HEIGHT + (i * CELL_HEIGHT),
                        CELL_WIDTH,
                        CELL_HEIGHT),
                    BackgroundTexture = sharedCellBackground,
                    DrawBorders = true
                };
                matrixPanel.AddChild(cellPanel);

                var statusCell = new XNALabel(WindowManager)
                {
                    ClientRectangle = new Rectangle(0, 0, CELL_WIDTH, CELL_HEIGHT),
                    TextAnchor = LabelTextAnchorInfo.CENTER
                };

                var status = negotiationData.GetNegotiationStatus(players[i], players[j]);
                var ping = negotiationData.GetPing(players[i], players[j]);
                var displayStatus = inferInProgress && status == NegotiationStatus.NotStarted
                    ? NegotiationStatus.InProgress : status;

                UpdateCell(statusCell, displayStatus, ping);
                statusCell.AnchorPoint = new Vector2(CELL_WIDTH / 2f, CELL_HEIGHT / 2f);

                cellPanel.AddChild(statusCell);
                statusCells[(players[i], players[j])] = statusCell;
            }
        }
    }

    private void BuildListView(List<string> players, NegotiationDataManager negotiationData, bool inferInProgress = false)
    {
        var pairs = new List<(string p1, string p2, NegotiationStatus status, PingValue? ping)>();

        foreach (var (p1, p2) in negotiationData.GetPlayerPairs(players))
        {
            var status = negotiationData.GetNegotiationStatus(p1, p2);
            if (inferInProgress && status == NegotiationStatus.NotStarted)
                status = NegotiationStatus.InProgress;
            var ping = negotiationData.GetPing(p1, p2);
            pairs.Add((p1, p2, status, ping));
        }

        pairs.Sort((a, b) =>
        {
            int rankA = GetSortRank(a.status, a.ping);
            int rankB = GetSortRank(b.status, b.ping);
            if (rankA != rankB)
                return rankA.CompareTo(rankB);
            if (a.ping.HasValue && b.ping.HasValue)
                return a.ping.Value.Milliseconds.CompareTo(b.ping.Value.Milliseconds);
            return 0;
        });

        sharedBarBackground ??= AssetLoader.CreateTexture(new Color(30, 30, 30, 120), 1, 1);
        texGreen ??= AssetLoader.CreateTexture(new Color(0, 180, 0, 200), 1, 1);
        texYellow ??= AssetLoader.CreateTexture(new Color(200, 180, 0, 200), 1, 1);
        texOrange ??= AssetLoader.CreateTexture(new Color(200, 100, 0, 200), 1, 1);
        texRed ??= AssetLoader.CreateTexture(new Color(200, 0, 0, 200), 1, 1);

        for (int i = 0; i < pairs.Count; i++)
        {
            var (p1, p2, status, ping) = pairs[i];
            int rowY = i * LIST_ROW_HEIGHT;
            int barY = rowY + (LIST_ROW_HEIGHT - 14) / 2;
            int barCenterY = barY + 7;

            var nameLabel = new XNALabel(WindowManager)
            {
                Text = $"{p1} <> {p2}",
                TextColor = Color.LightBlue,
                TextAnchor = LabelTextAnchorInfo.RIGHT | LabelTextAnchorInfo.VERTICAL_CENTER,
                AnchorPoint = new Vector2(0, barCenterY)
            };
            listPanel.AddChild(nameLabel);

            var barBg = new XNAPanel(WindowManager)
            {
                ClientRectangle = new Rectangle(LIST_NAME_WIDTH + 5, barY, LIST_BAR_MAX_WIDTH, 14),
                BackgroundTexture = sharedBarBackground,
                DrawBorders = false
            };
            listPanel.AddChild(barBg);

            if (status == NegotiationStatus.Succeeded && ping.HasValue && ping.Value.IsValid())
            {
                int ms = ping.Value.Milliseconds;
                int fillWidth = Math.Max(2, Math.Min(LIST_BAR_MAX_WIDTH, ms * LIST_BAR_MAX_WIDTH / LIST_BAR_MAX_PING));

                var barFill = new XNAPanel(WindowManager)
                {
                    ClientRectangle = new Rectangle(0, 0, fillWidth, 14),
                    BackgroundTexture = GetPingTexture(ms),
                    DrawBorders = false
                };
                barBg.AddChild(barFill);
            }

            var (pingText, pingColor) = GetListRowLabel(status, ping);
            var pingLabel = new XNALabel(WindowManager)
            {
                Text = pingText,
                TextColor = pingColor,
                TextAnchor = LabelTextAnchorInfo.RIGHT | LabelTextAnchorInfo.VERTICAL_CENTER,
                AnchorPoint = new Vector2(LIST_NAME_WIDTH + LIST_BAR_MAX_WIDTH + 10, barCenterY)
            };
            listPanel.AddChild(pingLabel);
        }
    }

    private static int GetSortRank(NegotiationStatus status, PingValue? ping) => status switch
    {
        NegotiationStatus.Succeeded when ping.HasValue && ping.Value.IsValid() => 0,
        NegotiationStatus.Succeeded => 1,
        NegotiationStatus.InProgress => 2,
        NegotiationStatus.NotStarted => 3,
        NegotiationStatus.Failed => 4,
        _ => 5
    };

    private static (string text, Color color) GetListRowLabel(NegotiationStatus status, PingValue? ping) => status switch
    {
        NegotiationStatus.NotStarted => ("-", Color.Gray),
        NegotiationStatus.InProgress => ("...", Color.Yellow),
        NegotiationStatus.Succeeded when ping.HasValue => (ping.Value.ToString(), GetPingColor(ping.Value.Milliseconds)),
        NegotiationStatus.Succeeded => ("OK".L10N("Client:Main:NegStatusOK"), Color.LightGreen),
        NegotiationStatus.Failed => ("FAIL".L10N("Client:Main:NegStatusFail"), Color.Red),
        _ => ("?", Color.Gray)
    };

    private static Color GetPingColor(int ms)
    {
        if (ms < 50) return Color.LightGreen;
        if (ms < 100) return Color.Yellow;
        if (ms < 200) return Color.Orange;
        return Color.Red;
    }

    private static Texture2D GetPingTexture(int ms)
    {
        if (ms < 50) return texGreen!;
        if (ms < 100) return texYellow!;
        if (ms < 200) return texOrange!;
        return texRed!;
    }

    private void UpdateCell(XNALabel cell, NegotiationStatus status, PingValue? ping)
    {
        switch (status)
        {
            case NegotiationStatus.NotStarted:
                cell.Text = "-";
                cell.TextColor = Color.Gray;
                break;
            case NegotiationStatus.InProgress:
                cell.Text = "...";
                cell.TextColor = Color.Yellow;
                break;
            case NegotiationStatus.Succeeded:
                if (ping.HasValue)
                {
                    cell.Text = ping.Value.ToString();
                    cell.TextColor = GetPingColor(ping.Value.Milliseconds);
                }
                else
                {
                    cell.Text = "OK".L10N("Client:Main:NegStatusOK");
                    cell.TextColor = Color.LightGreen;
                }
                break;
            case NegotiationStatus.Failed:
                cell.Text = "FAIL".L10N("Client:Main:NegStatusFail");
                cell.TextColor = Color.Red;
                break;
            default:
                cell.Text = "?";
                cell.TextColor = Color.Gray;
                break;
        }
    }
}
