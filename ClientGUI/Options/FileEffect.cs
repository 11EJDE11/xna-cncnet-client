#nullable enable

using System;
using System.IO;

using ClientCore;

using ClientGUI.Settings;

using Rampastring.Tools;

namespace ClientGUI.Options;

/// <summary>
/// Copies a file into place while the option is in the effect's state,
/// and removes it again when the option switches to another state.
/// </summary>
public class FileEffect : OptionEffect, IPersistentEffect
{
    public const string TYPE_NAME = "File";

    private readonly FileSourceDestinationInfo fileInfo;

    public FileEffect(string source, string destination, FileOperationOption operation)
    {
        fileInfo = new FileSourceDestinationInfo(source, destination, operation);
    }

    internal FileEffect(FileSourceDestinationInfo fileInfo)
    {
        this.fileInfo = fileInfo;
    }

    public bool IsAvailable => File.Exists(fileInfo.SourcePath);

    public void Apply() => fileInfo.Apply();

    public void Revert() => fileInfo.Revert();

    public static FileEffect FromIniSection(IniSection section)
    {
        string operationName = section.GetStringValue("Operation", nameof(FileOperationOption.AlwaysOverwrite));

        if (!Enum.TryParse(operationName, out FileOperationOption operation))
            throw new ClientConfigurationException($"Option effect section [{section.SectionName}] has an invalid Operation \"{operationName}\".");

        return new FileEffect(GetRequiredValue(section, "Source"), GetRequiredValue(section, "Destination"), operation);
    }
}
