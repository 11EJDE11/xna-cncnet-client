#nullable enable

using System.Collections.Generic;
using System.Linq;

using Rampastring.Tools;
using Rampastring.XNAUI;

namespace ClientGUI.Options;

/// <summary>
/// A check box option with an unchecked and a checked state, each with its own effects.
/// </summary>
public abstract class OptionCheckBox : XNAClientCheckBox, IOption
{
    public const int UNCHECKED_STATE = 0;
    public const int CHECKED_STATE = 1;

    protected OptionCheckBox(WindowManager windowManager) : base(windowManager) { }

    private List<OptionEffect> checkedEffects = [];
    private List<OptionEffect> uncheckedEffects = [];

    public int StateCount => 2;

    public int CurrentState => Checked ? CHECKED_STATE : UNCHECKED_STATE;

    public IEnumerable<OptionEffect> GetStateEffects(int state) => state switch
    {
        CHECKED_STATE => GetBuiltInEffects(true).Concat(checkedEffects),
        UNCHECKED_STATE => GetBuiltInEffects(false).Concat(uncheckedEffects),
        _ => []
    };

    /// <summary>
    /// Gets the effects that a derived control defines through its own INI keys
    /// or code, in addition to the ones listed in CheckedEffects and UncheckedEffects.
    /// </summary>
    protected virtual IEnumerable<OptionEffect> GetBuiltInEffects(bool isChecked) => [];

    protected override void ParseControlINIAttribute(IniFile iniFile, string key, string value)
    {
        switch (key)
        {
            case "CheckedEffects":
                checkedEffects = OptionEffect.FromIniSections(iniFile, value, Name);
                return;
            case "UncheckedEffects":
                uncheckedEffects = OptionEffect.FromIniSections(iniFile, value, Name);
                return;
        }

        base.ParseControlINIAttribute(iniFile, key, value);
    }
}
