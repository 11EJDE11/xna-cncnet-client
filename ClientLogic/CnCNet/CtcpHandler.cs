using System;

namespace ClientLogic.CnCNet;

/// <summary>
/// One CTCP command handler, with the matching rules of the XNA lobby's handler classes (StringCommandHandler,
/// NoParamCommandHandler, NotificationHandler, IntNotificationHandler, IntCommandHandler).
/// </summary>
internal sealed class CtcpHandler
{
    private readonly Func<string, string, bool> handle;

    private CtcpHandler(Func<string, string, bool> handle) => this.handle = handle;

    public bool Handle(string sender, string message) => handle(sender, message);

    public static CtcpHandler String(string command, Action<string, string> handler) => new((sender, message) =>
    {
        if (message.Length < command.Length + 1 || !message.StartsWith(command))
            return false;

        handler(sender, message.Substring(command.Length + 1));
        return true;
    });

    public static CtcpHandler NoParam(string command, Action<string> handler) => new((sender, message) =>
    {
        if (message != command)
            return false;

        handler(sender);
        return true;
    });

    public static CtcpHandler Notification(string command, Action<string> handler) => NoParam(command, handler);

    public static CtcpHandler IntNotification(string command, Action<string, int> handler) => new((sender, message) =>
    {
        if (!message.StartsWith(command))
            return false;

        // As the XNA handler: a value that doesn't parse is passed as 0
        if (!int.TryParse(message.Substring(command.Length + 1), out int value))
            value = 0;

        handler(sender, value);
        return true;
    });

    public static CtcpHandler Int(string command, Action<string, int> handler) => new((sender, message) =>
    {
        if (message.Length < command.Length + 1 || !message.StartsWith(command))
            return false;

        if (!int.TryParse(message.Substring(command.Length + 1), out int value))
            return false;

        handler(sender, value);
        return true;
    });
}
