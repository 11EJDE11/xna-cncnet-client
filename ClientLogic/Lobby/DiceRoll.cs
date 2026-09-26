using System;

using ClientCore.Extensions;

using Rampastring.Tools;

namespace ClientLogic.Lobby;

/// <summary>The /roll chat command: parsing "XdY", rolling, and reading the results other players send.</summary>
public static class DiceRoll
{
    public const int MaxDice = 10;
    public const int MaxDieSides = 100;

    /// <summary>
    /// Parses a dice spec such as "3d6" (empty = one six-sided die).
    /// </summary>
    /// <param name="error">The notice to show when the spec is invalid; null when it's valid.</param>
    public static bool TryParse(string spec, out int dieCount, out int dieSides, out string error)
    {
        dieSides = 6;
        dieCount = 1;
        error = null;

        if (!string.IsNullOrEmpty(spec))
        {
            string[] parts = spec.Split('d');
            if (parts.Length == 2)
            {
                if (!int.TryParse(parts[0], out dieCount) || !int.TryParse(parts[1], out dieSides))
                {
                    error = "Invalid dice specified. Expected format: /roll <die count>d<die sides>".L10N("Client:Main:ChatboxCommandRollInvalidAndSyntax");
                    return false;
                }
            }
        }

        if (dieCount > MaxDice || dieCount < 1)
        {
            error = "You can only have between 1 to 10 dice at once.".L10N("Client:Main:ChatboxCommandRollInvalid2");
            return false;
        }

        if (dieSides > MaxDieSides || dieSides < 2)
        {
            error = "You can only have between 2 and 100 sides in a die.".L10N("Client:Main:ChatboxCommandRollInvalid3");
            return false;
        }

        return true;
    }

    public static int[] Roll(int dieCount, int dieSides, Random random)
    {
        int[] results = new int[dieCount];
        for (int i = 0; i < dieCount; i++)
            results[i] = random.Next(1, dieSides + 1);

        return results;
    }

    /// <summary>
    /// Reads a roll another player sent: the number of die sides, then each result, separated by commas
    /// ("6,3,5,1" = three six-sided dice that rolled 3, 5 and 1).
    /// </summary>
    public static bool TryParseResult(string result, out int dieSides, out int[] results)
    {
        dieSides = 0;
        results = null;

        if (string.IsNullOrEmpty(result))
            return false;

        string[] parts = result.Split(',');
        if (parts.Length < 2 || parts.Length > MaxDice + 1)
            return false;

        int[] intArray = Array.ConvertAll(parts, s => Conversions.IntFromString(s, -1));
        int sides = intArray[0];
        if (sides < 1 || sides > MaxDieSides)
            return false;

        for (int i = 1; i < intArray.Length; i++)
        {
            if (intArray[i] < 1 || intArray[i] > sides)
                return false;
        }

        dieSides = sides;
        results = new int[intArray.Length - 1];
        Array.Copy(intArray, 1, results, 0, results.Length);
        return true;
    }

    public static string FormatResult(string senderName, int dieSides, int[] results) =>
        string.Format("{0} rolled {1}d{2} and got {3}".L10N("Client:Main:PrintDiceRollResult"),
            senderName, results.Length, dieSides, string.Join(", ", results));
}
