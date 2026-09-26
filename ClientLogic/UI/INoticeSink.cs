namespace ClientLogic.UI;

/// <summary>How a notice is shown; the front end picks the colour.</summary>
public enum NoticeSeverity
{
    /// <summary>Progress or general information.</summary>
    Info,

    /// <summary>Something recovered or completed.</summary>
    Success,

    /// <summary>Something the user may need to act on.</summary>
    Warning,

    /// <summary>It works, but not the preferred way (for example a relay instead of a direct connection).</summary>
    Degraded,

    /// <summary>Something failed.</summary>
    Error,
}

/// <summary>Receives notices for the user, such as the lobby chat.</summary>
public interface INoticeSink
{
    void AddNotice(string message, NoticeSeverity severity);
}
