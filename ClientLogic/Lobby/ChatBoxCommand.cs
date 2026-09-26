using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using ClientCore.Extensions;

namespace ClientLogic.Lobby;

/// <summary>
/// A command that can be executed by typing a message starting with / on
/// a multiplayer game lobby's chat box.
/// </summary>
public class ChatBoxCommand
{
    public ChatBoxCommand(string command, string description, bool hostOnly, Action<string> action)
    {
        Command = command;
        Description = description;
        HostOnly = hostOnly;
        Action = action;
    }

    public string Command { get; private set; }
    public string Description { get; private set; }
    public bool HostOnly { get; private set; }
    public Action<string> Action { get; private set; }
}

/// <summary>What happened to a chat line that starts with /.</summary>
public enum ChatCommandOutcome
{
    /// <summary>The command ran.</summary>
    Executed,

    /// <summary>The command is for the game host only; show <see cref="ChatCommandResult.Notice"/>.</summary>
    HostOnly,

    /// <summary>No such command; show the help (<see cref="ChatBoxCommands.HelpText"/>).</summary>
    Unknown,
}

public sealed record ChatCommandResult(ChatCommandOutcome Outcome, string Notice = null);

/// <summary>Parses and runs chat box commands.</summary>
public static class ChatBoxCommands
{
    /// <summary>
    /// Splits "/command parameters" into the command and its parameters (everything after the first space).
    /// </summary>
    public static (string Command, string Parameters) Parse(string text)
    {
        int spaceIndex = text.IndexOf(' ');

        return spaceIndex == -1
            ? (text.Substring(1).ToUpper(), string.Empty)
            : (text.Substring(1, spaceIndex - 1), text.Substring(spaceIndex + 1));
    }

    /// <summary>Runs the command a chat line starting with / names (case-insensitive).</summary>
    public static ChatCommandResult Execute(string text, IEnumerable<ChatBoxCommand> commands, bool isHost)
    {
        (string command, string parameters) = Parse(text);

        ChatBoxCommand chatBoxCommand = commands.FirstOrDefault(c => command.ToUpper() == c.Command);
        if (chatBoxCommand == null)
            return new ChatCommandResult(ChatCommandOutcome.Unknown);

        if (!isHost && chatBoxCommand.HostOnly)
        {
            return new ChatCommandResult(ChatCommandOutcome.HostOnly,
                string.Format("/{0} is for game hosts only.".L10N("Client:Main:ChatboxCommandHostOnly"), chatBoxCommand.Command));
        }

        chatBoxCommand.Action(parameters);
        return new ChatCommandResult(ChatCommandOutcome.Executed);
    }

    public static string HelpTitle => "Chat Box Command Help".L10N("Client:Main:ChatboxCommandTipTitle");

    public static string HelpText(IEnumerable<ChatBoxCommand> commands)
    {
        var sb = new StringBuilder("To use a command, start your message with /<command>. Possible chat box commands:".L10N("Client:Main:ChatboxCommandTipText") + " ");
        foreach (ChatBoxCommand chatBoxCommand in commands)
        {
            sb.Append(Environment.NewLine);
            sb.Append(Environment.NewLine);
            sb.Append($"{chatBoxCommand.Command}: {chatBoxCommand.Description}");
        }

        return sb.ToString();
    }
}
