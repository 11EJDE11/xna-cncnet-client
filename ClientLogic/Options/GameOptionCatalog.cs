using System;
using System.Collections.Generic;
using System.Linq;

using ClientCore.Extensions;
using ClientCore.I18N;

using Rampastring.Tools;

namespace ClientLogic.Options;

/// <summary>
/// Reads the game option definitions of a lobby window straight from its layout INI, without creating controls.
/// Follows the same rules as the XNA client: child controls are listed as "$CCnn=Name:Type" in their parent's
/// section, depth first, and "$ExtraControls" adds top-level controls that don't exist yet.
/// </summary>
public static class GameOptionCatalog
{
    public const string CHECK_BOX_TYPE = "GameLobbyCheckBox";
    public const string DROP_DOWN_TYPE = "GameLobbyDropDown";

    /// <param name="layoutIni">The window's layout INI (a <c>CCIniFile</c>, with base sections applied).</param>
    /// <param name="windowName">The window's name, which is also its section name.</param>
    public static List<GameOptionDefinition> Load(IniFile layoutIni, string windowName)
    {
        var definitions = new List<GameOptionDefinition>();
        var controlNames = new HashSet<string>();

        ReadChildren(layoutIni, windowName, definitions, controlNames);

        IniSection extraControls = layoutIni.GetSection("$ExtraControls");
        if (extraControls != null)
        {
            foreach (var kvp in extraControls.Keys.Where(k => k.Key.StartsWith("$CC")))
            {
                string[] parts = kvp.Value.Split(':');
                if (parts.Length == 2 && !controlNames.Contains(parts[0]))
                    ReadControl(layoutIni, windowName, parts[0], parts[1], definitions, controlNames);
            }
        }

        return definitions;
    }

    private static void ReadChildren(IniFile layoutIni, string parentName, List<GameOptionDefinition> definitions, HashSet<string> controlNames)
    {
        IniSection section = layoutIni.GetSection(parentName);
        if (section == null)
            return;

        foreach (var kvp in section.Keys.Where(k => k.Key.StartsWith("$CC")))
        {
            string[] parts = kvp.Value.Split(':');
            if (parts.Length != 2)
                continue;

            ReadControl(layoutIni, parentName, parts[0], parts[1], definitions, controlNames);
        }
    }

    private static void ReadControl(IniFile layoutIni, string parentName, string name, string type,
        List<GameOptionDefinition> definitions, HashSet<string> controlNames)
    {
        controlNames.Add(name);

        // Children are created and registered before the control itself is initialized
        ReadChildren(layoutIni, name, definitions, controlNames);

        GameOptionKind kind;
        if (type == CHECK_BOX_TYPE)
            kind = GameOptionKind.CheckBox;
        else if (type == DROP_DOWN_TYPE)
            kind = GameOptionKind.DropDown;
        else
            return;

        string Localize(string attributeName, string defaultValue) => Translation.Instance.LookUp(
            $"INI:Controls:{parentName}:{name}:{attributeName}",
            $"INI:Controls:Global:{name}:{attributeName}",
            defaultValue);

        var builder = new GameOptionDefinitionBuilder(name, kind, isLobbyOption: true, Localize);

        IniSection section = layoutIni.GetSection(name);
        if (section != null)
        {
            foreach (var kvp in section.Keys)
            {
                if (kvp.Key.StartsWith("$"))
                    continue;

                if (kvp.Key == "Text")
                    builder.Label = Localize("Text", kvp.Value.FromIniString());
                else
                    builder.TryParse(layoutIni, kvp.Key, kvp.Value);
            }
        }

        definitions.Add(builder.Build());
    }
}
