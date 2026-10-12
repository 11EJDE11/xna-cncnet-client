#nullable enable

using Rampastring.Tools;

namespace ClientGUI.Options;

/// <summary>
/// Adds a command line argument to the game executable.
/// </summary>
public class LaunchArgumentEffect : OptionEffect
{
    public const string TYPE_NAME = "LaunchArgument";

    public LaunchArgumentEffect(string argument)
    {
        LaunchArgument = argument;
    }

    public override string LaunchArgument { get; }

    public static LaunchArgumentEffect FromIniSection(IniSection section) => new(GetRequiredValue(section, "Argument"));
}
