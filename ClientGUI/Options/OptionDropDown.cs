#nullable enable

using System.Collections.Generic;
using System.Linq;

using Rampastring.Tools;
using Rampastring.XNAUI;

namespace ClientGUI.Options;

/// <summary>
/// A drop-down option with one state per item, each with its own effects.
/// </summary>
public abstract class OptionDropDown : XNAClientDropDown, IOption
{
    private const string ITEM_EFFECTS_KEY_PREFIX = "Item";
    private const string ITEM_EFFECTS_KEY_SUFFIX = "Effects";

    protected OptionDropDown(WindowManager windowManager) : base(windowManager) { }

    private readonly Dictionary<int, List<OptionEffect>> itemEffects = [];

    public int StateCount => Items.Count;

    public int CurrentState => SelectedIndex;

    public IEnumerable<OptionEffect> GetStateEffects(int state)
    {
        if (state < 0 || state >= Items.Count)
            return [];

        return itemEffects.TryGetValue(state, out List<OptionEffect>? effects)
            ? GetBuiltInEffects(state).Concat(effects)
            : GetBuiltInEffects(state);
    }

    /// <summary>
    /// Gets the effects that a derived control defines through its own INI keys
    /// or code for an item, in addition to the ones listed in Item{index}Effects.
    /// </summary>
    protected virtual IEnumerable<OptionEffect> GetBuiltInEffects(int itemIndex) => [];

    public override void Initialize()
    {
        base.Initialize();

        // INI attributes can be in any order, so this can only be checked once they're all parsed
        foreach (int itemIndex in itemEffects.Keys.Where(index => index >= Items.Count))
            Logger.Log($"{nameof(OptionDropDown)}: {Name} defines {ITEM_EFFECTS_KEY_PREFIX}{itemIndex}{ITEM_EFFECTS_KEY_SUFFIX} but only has {Items.Count} items.");
    }

    protected override void ParseControlINIAttribute(IniFile iniFile, string key, string value)
    {
        if (TryParseItemEffectsKey(key, out int itemIndex))
        {
            itemEffects[itemIndex] = OptionEffect.FromIniSections(iniFile, value, Name);
            return;
        }

        base.ParseControlINIAttribute(iniFile, key, value);
    }

    private static bool TryParseItemEffectsKey(string key, out int itemIndex)
    {
        itemIndex = -1;

        if (!key.StartsWith(ITEM_EFFECTS_KEY_PREFIX) || !key.EndsWith(ITEM_EFFECTS_KEY_SUFFIX))
            return false;

        string indexText = key.Substring(ITEM_EFFECTS_KEY_PREFIX.Length,
            key.Length - ITEM_EFFECTS_KEY_PREFIX.Length - ITEM_EFFECTS_KEY_SUFFIX.Length);

        return int.TryParse(indexText, out itemIndex) && itemIndex >= 0;
    }
}
