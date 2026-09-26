using System;
using System.Collections.Generic;

using ClientLogic.CnCNet;

using DTAClient.Domain.Multiplayer.CnCNet;

using SixLabors.ImageSharp;

using Xunit;

namespace ClientLogic.Tests.CnCNet;

public class JoinAndInvitationTests
{
    private sealed class TestCnCNetGame : CnCNetGame
    {
        protected override Image LoadImage() => null;
    }

    private static HostedCnCNetGame Game(string internalName = "yr", bool locked = false, bool incompatible = false,
        bool loaded = false, bool passworded = false, string[] players = null) =>
        new("#game1", "B", "1.0", 8, "Room", passworded, true, players ?? ["Host"], "Host", "Map", "Battle", "hash")
        {
            Game = new TestCnCNetGame { InternalName = internalName },
            Locked = locked,
            Incompatible = incompatible,
            IsLoadedGame = loaded,
        };

    private static JoinContext Context(bool disallowIncompatible = false, bool joining = false, bool running = false) =>
        new("YR", "Me", disallowIncompatible, joining, running, name => name.ToUpper() + " game");

    [Fact]
    public void JoinErrorsComeInTheClientsOrder()
    {
        TestGame.EnsureInitialized();

        Assert.Null(JoinGameRules.Error(Game(), Context()));
        Assert.Contains("TS game", JoinGameRules.Error(Game("ts", locked: true), Context()));
        Assert.NotNull(JoinGameRules.Error(Game(incompatible: true), Context(disallowIncompatible: true)));
        Assert.Null(JoinGameRules.Error(Game(incompatible: true), Context()));
        Assert.Contains("Room", JoinGameRules.Error(Game(locked: true), Context()));
        Assert.NotNull(JoinGameRules.Error(Game(loaded: true), Context()));
        Assert.Null(JoinGameRules.Error(Game(loaded: true, players: ["Host", "Me"]), Context()));
        Assert.Equal(JoinGameRules.BaseError(true, false), JoinGameRules.Error(Game(), Context(joining: true)));
        Assert.Equal(JoinGameRules.BaseError(false, true), JoinGameRules.Error(Game(), Context(running: true)));
        Assert.Null(JoinGameRules.BaseError(false, false));
    }

    [Fact]
    public void PasswordsArePromptedGivenOrDerived()
    {
        Assert.True(JoinGameRules.NeedsPasswordPrompt(Game(passworded: true), ""));
        Assert.False(JoinGameRules.NeedsPasswordPrompt(Game(passworded: true), "invite"));
        Assert.False(JoinGameRules.NeedsPasswordPrompt(Game(), ""));

        Assert.Equal("invite", JoinGameRules.JoinPassword(Game(passworded: true), "invite", () => "unused"));

        string derived = JoinGameRules.JoinPassword(Game(), "", () => "unused");
        Assert.Equal(JoinGameRules.DerivedPassword("#game1"), derived);
        Assert.Equal(10, derived.Length);

        Assert.Equal(JoinGameRules.DerivedPassword("12345"), JoinGameRules.JoinPassword(Game(loaded: true), "", () => "12345"));
    }

    [Fact]
    public void InvitationsParse()
    {
        Assert.True(GameInvitation.TryParse("Host", "#game1;Room;pw", out GameInvitation withPassword));
        Assert.Equal(new GameInvitation("Host", "#game1", "Room", "pw"), withPassword);
        Assert.True(GameInvitation.TryParse("Host", "#game1;Room", out GameInvitation without));
        Assert.Equal("", without.Password);
        Assert.False(GameInvitation.TryParse("Host", "#game1", out _));
        Assert.False(GameInvitation.TryParse("Host", "a;b;c;d", out _));
    }

    [Fact]
    public void InvitationResponsesAndDeduplication()
    {
        var invitations = new GameInvitations<object>();
        var invitation = new GameInvitation("Host", "#game1", "Room", "");

        Assert.Equal(InvitationResponse.Ignore, invitations.Decide(invitation, false, false, null, false, false));
        Assert.Equal(InvitationResponse.Ignore, invitations.Decide(invitation, true, true, null, false, false));
        Assert.Equal(InvitationResponse.Decline, invitations.Decide(invitation, true, false, "error", false, false));
        Assert.Equal(InvitationResponse.Decline, invitations.Decide(invitation, true, false, null, true, false));
        Assert.Equal(InvitationResponse.Show, invitations.Decide(invitation, true, false, null, true, true));

        object notification = new();
        invitations.Add(invitation, notification);
        Assert.Equal(InvitationResponse.AlreadyShown, invitations.Decide(invitation, true, false, null, false, false));

        Assert.Empty(invitations.FindInvalid([Game()]));
        Assert.Equal([("Host", "#game1")], invitations.FindInvalid(new List<HostedCnCNetGame>()));

        Assert.Same(notification, invitations.Remove("Host", "#game1"));
        Assert.Null(invitations.Remove("Host", "#game1"));
        GC.KeepAlive(notification);
    }
}
