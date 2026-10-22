using System;

namespace ClientGUI
{
    /// <summary>
    /// Decides which INI keys are skipped when a window re-applies its INI to controls that already
    /// exist (designer hot reload). Changes to these keys need a client restart.
    /// </summary>
    internal static class IniReloadPolicy
    {
        public static bool IsUnsafeToReapply(string key)
        {
            switch (key)
            {
                // Lists are appended to rather than replaced
                case "Items":
                case "ItemLabels":
                case "Icons":
                // Game and user setting state the player may have changed since
                case "Checked":
                case "DefaultIndex":
                case "SelectedIndex":
                    return true;
            }

            // XNADropDown OptionN adds an item, XNAMultiColumnListBox ColumnN adds a column
            if (key.StartsWith("Option", StringComparison.Ordinal))
                return true;

            return key.StartsWith("Column", StringComparison.Ordinal) && !key.StartsWith("ColumnWidth", StringComparison.Ordinal);
        }
    }
}
