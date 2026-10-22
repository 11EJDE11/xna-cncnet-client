using System;
using System.Collections.Generic;
using System.Linq;
using Rampastring.XNAUI.XNAControls;

namespace DTAClient.DXGUI.Designer
{
    /// <summary>
    /// The INI keys each control class reads, for the designer's key suggestions.
    /// Controls parse their keys in ParseControlINIAttribute switches, which can't be inspected at run time,
    /// so this list is kept by hand: it was taken from those methods (and TranslationINIParser and
    /// INItializableWindow) and needs an entry when a control learns a new key.
    /// Classes are matched by name along the selected control's type hierarchy.
    /// </summary>
    internal static class DesignerKeyCatalog
    {
        private static readonly Dictionary<string, string[]> KeysByClass = new Dictionary<string, string[]>
        {
            ["XNAControl"] = new[] { "Location", "Size", "X", "Y", "Width", "Height", "DistanceFromRightBorder", "DistanceFromBottomBorder",
                "FillWidth", "FillHeight", "Text", "Visible", "Enabled", "RemapColor", "DrawOrder", "UpdateOrder", "ControlDrawMode" },
            ["XNAPanel"] = new[] { "BackgroundTexture", "SolidColorBackgroundTexture", "DrawMode", "DrawBorders", "BorderColor", "AlphaRate", "Padding" },
            ["XNAExtraPanel"] = new[] { "BackgroundTexture" },
            ["XNALabel"] = new[] { "TextColor", "FontIndex", "TextShadowDistance", "AnchorPoint", "TextAnchor" },
            ["XNALinkLabel"] = new[] { "IdleColor", "HoverColor", "DrawUnderline" },
            ["XNAClientLinkLabel"] = new[] { "URL", "UnixURL", "ToolTip", "HoverSoundEffect", "ClickSoundEffect" },
            ["XNAButton"] = new[] { "IdleTexture", "HoverTexture", "TextColorIdle", "TextColorHover", "FontIndex", "TextShadowDistance",
                "AdaptiveText", "AlphaRate", "HoverSoundEffect", "ClickSoundEffect" },
            ["XNAClientButton"] = new[] { "ToolTip", "MatchTextureSize" },
            ["XNALinkButton"] = new[] { "URL", "UnixURL", "Arguments" },
            ["XNAClientToggleButton"] = new[] { "CheckedTexture", "UncheckedTexture", "ToolTip" },
            ["XNACheckBox"] = new[] { "Checked", "AllowChecking", "FontIndex", "IdleColor", "HighlightColor", "AlphaRate", "TextPadding" },
            ["XNAClientCheckBox"] = new[] { "ToolTip" },
            ["XNADropDown"] = new[] { "DropDownTexture", "DropDownOpenTexture", "BackColor", "BorderColor", "FocusColor", "DisabledItemColor",
                "ItemHeight", "FontIndex", "OpenUp", "ShowEllipsisOnOverflow", "ClickSoundEffect", "Option0" },
            ["XNAClientDropDown"] = new[] { "ToolTip" },
            ["XNAClientColorDropDown"] = new[] { "ItemsDrawMode", "RandomColorTexture", "DisabledItemTexture", "ColorTextureWidth", "ColorTextureHeight" },
            ["XNAClientPreferredItemDropDown"] = new[] { "PreferredItemLabel" },
            ["XNAListBox"] = new[] { "FontIndex", "LineHeight", "TextBorderDistance", "EnableScrollbar", "DrawSelectionUnderScrollbar",
                "AllowMultiLineItems", "AllowRightClickUnselect" },
            ["XNAMultiColumnListBox"] = new[] { "FontIndex", "LineHeight", "ColumnWidth0" },
            ["XNATextBox"] = new[] { "MaximumTextLength", "NextControl", "PreviousControl" },
            ["XNASuggestionTextBox"] = new[] { "Suggestion" },
            ["XNATextBlock"] = new[] { "TextColor" },
            ["XNATabControl"] = new[] { "TextColor", "TextColorDisabled", "RemapColor" },
            ["XNATrackbar"] = new[] { "MinValue", "MaxValue", "Value", "ClickSound" },
            ["XNAIndicator"] = new[] { "FontIndex", "HighlightColor", "AlphaRate" },
            ["XNAContextMenu"] = new[] { "FontIndex", "HintFontIndex", "ItemHeight", "TextHorizontalPadding", "TextVerticalPadding" },
            ["XNAScrollPanel"] = new[] { "AllowScroll", "AllowScrollX", "AllowScrollY", "AllowKeyboardInput", "ScrollStep",
                "OverscrollMargin", "OverscrollMarginX", "OverscrollMarginY", "Padding", "DrawBorders" },
            ["INItializableWindow"] = new[] { "HasCloseButton" },
            ["SettingCheckBoxBase"] = new[] { "SettingSection", "SettingKey", "DefaultValue", "RestartRequired", "ResetToDefaultOnGameExit",
                "ParentCheckBoxName", "ParentCheckBoxRequiredValue" },
            ["SettingCheckBox"] = new[] { "EnabledSettingValue", "DisabledSettingValue", "WriteSettingValue" },
            ["FileSettingCheckBox"] = new[] { "CheckAvailability", "ResetUnavailableValue", "Reversed" },
            ["SettingDropDownBase"] = new[] { "SettingSection", "SettingKey", "DefaultValue", "RestartRequired", "Items" },
            ["SettingDropDown"] = new[] { "WriteItemValue" },
            ["FileSettingDropDown"] = new[] { "CheckAvailability", "ResetUnavailableValue" },
            ["GameSessionCheckBox"] = new[] { "SpawnIniOption", "CustomIniPath", "EnabledSpawnIniValue", "DisabledSpawnIniValue", "Reversed",
                "BroadcastToLobby", "ShowInFilters", "ShowInGameList", "ShowInGameListOnRight", "ShowInGameInformationPanel",
                "ShowInGameInformationPanelAsIconOnly", "ShowIconInGameLobby", "EnabledIcon", "DisabledIcon", "SortOrder", "MapScoringMode" },
            ["GameSessionDropDown"] = new[] { "Items", "OptionName", "DefaultIndex", "SpawnIniOption", "DataWriteMode", "BroadcastToLobby",
                "ShowInFilters", "ShowInGameList", "ShowInGameListOnRight", "ShowInGameInformationPanel",
                "ShowInGameInformationPanelAsIconOnly", "ShowIconInGameLobby", "SortOrder" },
            ["GameLobbyCheckBox"] = new[] { "CheckedMP", "DisallowedSideIndex", "DisallowedSideIndices" },
            ["GameLobbyDropDown"] = new[] { "DefaultIndex" },
            ["CampaignCheckBox"] = new[] { "CustomIniPath", "ResetToDefaultOnGameExit" },
            ["CampaignDropDown"] = new[] { "DataWriteMode" },
        };

        // Keys INItializableWindow evaluates itself, in any control of such a window
        private static readonly string[] ExpressionKeys = { "$X", "$Y", "$Width", "$Height", "$TextAnchor", "$AnchorPoint", "$LeftClickAction" };

        private static readonly string[] ColorKeys = { "Color", "SolidColorBackgroundTexture" };
        private static readonly string[] TextureKeys = { "Texture" };
        private static readonly string[] BooleanKeys = { "Visible", "Enabled", "DrawBorders", "AdaptiveText", "MatchTextureSize", "OpenUp",
            "ShowEllipsisOnOverflow", "EnableScrollbar", "DrawSelectionUnderScrollbar", "AllowMultiLineItems", "AllowRightClickUnselect",
            "DrawUnderline", "Checked", "AllowChecking", "HasCloseButton" };

        /// <summary>
        /// Returns the keys the control can read from its INI section, sorted by name.
        /// </summary>
        public static List<string> GetKeys(XNAControl control, bool inINItializableWindow)
        {
            var keys = new HashSet<string>();

            for (Type type = control.GetType(); type != null; type = type.BaseType)
            {
                if (KeysByClass.TryGetValue(type.Name, out string[] classKeys))
                    keys.UnionWith(classKeys);
            }

            if (inINItializableWindow)
                keys.UnionWith(ExpressionKeys);

            return keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// Returns a short description of the value a key expects, or null if there is none.
        /// </summary>
        public static string GetValueHint(string key)
        {
            if (key.StartsWith("$", StringComparison.Ordinal))
                return key == "$TextAnchor" ? "LEFT, RIGHT, HORIZONTAL_CENTER, TOP, BOTTOM, VERTICAL_CENTER" : "expression, e.g. getRight(otherControl) + 10";

            if (BooleanKeys.Contains(key))
                return "true / false";

            if (key.EndsWith("Width", StringComparison.Ordinal) || key.EndsWith("Height", StringComparison.Ordinal))
                return "number of pixels";

            if (ColorKeys.Any(c => key.Contains(c)) && (!key.Contains("Texture") || key == "SolidColorBackgroundTexture"))
                return "color R,G,B or R,G,B,A";

            if (TextureKeys.Any(t => key.Contains(t)))
                return "image file, e.g. button.png";

            switch (key)
            {
                case "Location":
                case "AnchorPoint":
                    return "x,y";
                case "Size":
                    return "width,height";
                case "Padding":
                    return "left,top,right,bottom";
                case "DrawMode":
                    return "Tiled, Centered or Stretched";
                case "TextAnchor":
                    return "LEFT, RIGHT, HORIZONTAL_CENTER, TOP, BOTTOM, VERTICAL_CENTER";
                case "ControlDrawMode":
                    return "UniqueRenderTarget";
                case "Text":
                case "ToolTip":
                case "Suggestion":
                    return "text, @ for a new line";
                case "HoverSoundEffect":
                case "ClickSoundEffect":
                case "ClickSound":
                    return "sound file, e.g. button.wav";
                case "URL":
                case "UnixURL":
                    return "web address";
            }

            return key.Contains("Font") ? "font number from Fonts.ini" : null;
        }
    }
}
