using System;
using System.Collections.Generic;

using Rampastring.Tools;

namespace ClientLogic.Layout;

/// <summary>
/// The XNA client's layout expression parser (ClientGUI's Parser) over <see cref="LayoutControl"/>s, with the same
/// rules and quirks: integers, UPPERCASE constants, getX/getY/getWidth/getHeight/getBottom/getRight(name, $Self or
/// $ParentControl) and horizontalCenterOnParent(), evaluated left to right without operator precedence; '*' and '/'
/// take the value of the rest of the expression.
/// </summary>
public sealed class LayoutExpressionParser
{
    private readonly Dictionary<string, int> constants;
    private readonly LayoutControl primaryControl;

    private string input;
    private int tokenPlace;
    private LayoutControl parsingControl;

    /// <param name="constants">The parser constants (RESOLUTION_WIDTH, RESOLUTION_HEIGHT and [ParserConstants]).</param>
    /// <param name="primaryControl">The window; control names are looked up under it.</param>
    public LayoutExpressionParser(Dictionary<string, int> constants, LayoutControl primaryControl)
    {
        this.constants = constants;
        this.primaryControl = primaryControl;
    }

    /// <summary>The constants the XNA client uses: the render resolution and the [ParserConstants] section.</summary>
    public static Dictionary<string, int> ClientConstants(int renderWidth, int renderHeight, IniSection parserConstants)
    {
        var constants = new Dictionary<string, int>
        {
            ["RESOLUTION_WIDTH"] = renderWidth,
            ["RESOLUTION_HEIGHT"] = renderHeight,
        };

        if (parserConstants != null)
        {
            foreach (var kvp in parserConstants.Keys)
                constants[kvp.Key] = Conversions.IntFromString(kvp.Value, 0);
        }

        return constants;
    }

    public int GetExprValue(string expression, LayoutControl control)
    {
        parsingControl = control;
        input = expression;
        tokenPlace = 0;
        return GetExprValue();
    }

    private LayoutControl GetControl(string controlName)
    {
        if (controlName == primaryControl.Name)
            return primaryControl;

        return Find(primaryControl.Children, controlName)
            ?? throw new KeyNotFoundException($"Control '{controlName}' not found while parsing input '{input}'");
    }

    private static LayoutControl Find(IEnumerable<LayoutControl> list, string controlName)
    {
        foreach (LayoutControl child in list)
        {
            if (child.Name == controlName)
                return child;

            LayoutControl childOfChild = Find(child.Children, controlName);
            if (childOfChild != null)
                return childOfChild;
        }

        return null;
    }

    private int GetConstant(string constantName) =>
        constants.TryGetValue(constantName, out int value)
            ? value
            : throw new KeyNotFoundException($"Constant '{constantName}' not found. Please check the [ParserConstants] section.");

    private int GetExprValue()
    {
        int value = 0;

        while (true)
        {
            SkipWhitespace();

            if (IsEndOfInput())
                return value;

            char c = input[tokenPlace];

            if (char.IsDigit(c))
            {
                value = GetInt();
            }
            else if (c == '+')
            {
                tokenPlace++;
                value += GetNumericalValue();
            }
            else if (c == '-')
            {
                tokenPlace++;
                value -= GetNumericalValue();
            }
            else if (c == '/')
            {
                tokenPlace++;
                value /= GetExprValue();
            }
            else if (c == '*')
            {
                tokenPlace++;
                value *= GetExprValue();
            }
            else if (c == '(')
            {
                tokenPlace++;
                value = GetExprValue();
            }
            else if (c == ')')
            {
                tokenPlace++;
                return value;
            }
            else if (char.IsUpper(c))
            {
                value = GetConstantValue();
            }
            else if (char.IsLower(c))
            {
                value = GetFunctionValue();
            }
            else
            {
                // The XNA parser loops forever here; stop instead
                throw new FormatException($"Unexpected character '{c}' when parsing input: {input}");
            }
        }
    }

    private int GetNumericalValue()
    {
        SkipWhitespace();

        if (IsEndOfInput())
            return 0;

        char c = input[tokenPlace];

        if (char.IsDigit(c))
            return GetInt();

        if (char.IsUpper(c))
            return GetConstantValue();

        if (char.IsLower(c))
            return GetFunctionValue();

        if (c == '(')
        {
            tokenPlace++;
            return GetExprValue();
        }

        throw new FormatException("Unexpected character " + c + " when parsing input: " + input);
    }

    private void SkipWhitespace()
    {
        while (!IsEndOfInput() && input[tokenPlace] is ' ' or '\r' or '\n')
            tokenPlace++;
    }

    private string GetIdentifier()
    {
        int start = tokenPlace;

        while (!IsEndOfInput())
        {
            char c = input[tokenPlace];
            if (char.IsWhiteSpace(c) || (!char.IsLetterOrDigit(c) && c != '_' && c != '$' && c != '.'))
                break;

            tokenPlace++;
        }

        return input.Substring(start, tokenPlace - start);
    }

    private int GetConstantValue() => GetConstant(GetIdentifier());

    private int GetFunctionValue()
    {
        string functionName = GetIdentifier();
        SkipWhitespace();
        ConsumeChar('(');
        string paramName = GetIdentifier();
        SkipWhitespace();
        ConsumeChar(')');

        if (paramName == "$ParentControl")
        {
            paramName = parsingControl.Parent?.Name
                ?? throw new FormatException("$ParentControl used for control that has no parent: " + parsingControl.Name);
        }
        else if (paramName == "$Self")
        {
            paramName = parsingControl.Name;
        }

        switch (functionName)
        {
            case "getX":
                return GetControl(paramName).X;
            case "getY":
                return GetControl(paramName).Y;
            case "getWidth":
                return GetControl(paramName).Width;
            case "getHeight":
                return GetControl(paramName).Height;
            case "getBottom":
                LayoutControl bottom = GetControl(paramName);
                return bottom.Y + bottom.Height;
            case "getRight":
                LayoutControl right = GetControl(paramName);
                return right.X + right.Width;
            case "horizontalCenterOnParent":
                if (parsingControl.Parent != null)
                    parsingControl.X = (parsingControl.Parent.Width - parsingControl.Width) / 2;
                return parsingControl.X;
            default:
                throw new FormatException("Unknown function " + functionName + " in expression " + input);
        }
    }

    private void ConsumeChar(char token)
    {
        if (IsEndOfInput() || input[tokenPlace] != token)
            throw new FormatException($"Parse error: expected '{token}' in expression {input}.");

        tokenPlace++;
    }

    private int GetInt()
    {
        int value = 0;
        while (!IsEndOfInput() && char.IsDigit(input[tokenPlace]))
        {
            value = (value * 10) + input[tokenPlace] - '0';
            tokenPlace++;
        }

        return value;
    }

    private bool IsEndOfInput() => tokenPlace >= input.Length;
}
