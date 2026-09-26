using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ClientLogic.Protocol;

/// <summary>
/// The values of the game options that are broadcast to the game list (the "GAME" message) and written
/// to spawn.ini for the loading lobby: the check-boxes packed 32 to an integer, then the drop-down
/// indices, comma-separated. Empty if there are no broadcast options.
/// </summary>
public static class BroadcastedGameOptionValues
{
    public static string Encode(IReadOnlyList<bool> checkBoxValues, IReadOnlyList<int> dropDownIndices)
    {
        var values = new List<int>();
        values.AddRange(PackedCheckBoxes.Pack(checkBoxValues));
        values.AddRange(dropDownIndices);

        return string.Join(",", values.Select(v => v.ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>
    /// Returns one value per broadcast option (check-boxes as 0/1, then drop-down indices), or null if
    /// <paramref name="packedValues"/> is empty. Values missing from the string are 0.
    /// </summary>
    /// <exception cref="FormatException">A value is not a number.</exception>
    public static int[] Decode(string packedValues, int checkBoxCount, int dropDownCount)
    {
        if (string.IsNullOrEmpty(packedValues))
            return null;

        var optionValues = new int[checkBoxCount + dropDownCount];
        string[] allValueStrings = packedValues.Split(',');

        int packedCheckBoxCount = PackedCheckBoxes.IntCount(checkBoxCount);

        // packed checkbox values
        if (checkBoxCount > 0 && allValueStrings.Length >= packedCheckBoxCount)
        {
            int[] packedCheckBoxes = new int[packedCheckBoxCount];
            for (int i = 0; i < packedCheckBoxCount; i++)
                packedCheckBoxes[i] = int.Parse(allValueStrings[i], CultureInfo.InvariantCulture);

            bool[] checkBoxValues = PackedCheckBoxes.Unpack(packedCheckBoxes, checkBoxCount);
            for (int i = 0; i < checkBoxCount; i++)
                optionValues[i] = checkBoxValues[i] ? 1 : 0;
        }

        // dropdown indices
        if (dropDownCount > 0)
        {
            int count = Math.Min(allValueStrings.Length - packedCheckBoxCount, dropDownCount);
            for (int i = 0; i < count; i++)
                optionValues[checkBoxCount + i] = int.Parse(allValueStrings[packedCheckBoxCount + i], CultureInfo.InvariantCulture);
        }

        return optionValues;
    }
}
