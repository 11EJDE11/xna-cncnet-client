using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Xna.Framework;

namespace DTAClient.DXGUI.Designer
{
    /// <summary>
    /// Turns a control's change of position or size in the designer into INI key changes, keeping the
    /// way the control is already positioned: expressions ($X) get their trailing offset adjusted, right
    /// and bottom anchors (DistanceFrom*) and fills (Fill*) stay anchors, plain coordinates stay plain.
    /// Only an axis the INI does not position at all gets an absolute X/Y/Width/Height key.
    /// Anchors and fills are calculated from the parent's new size, so they stay right when the parent
    /// is resized in the same change, and from the size or position in effect when the INI reader gets to
    /// them: keys are applied in order, and a position or size key this change adds goes before the anchors.
    /// What this can't foresee, like an expression that reads the control's own position or keys merged from
    /// a BasedOn file, is fixed afterwards by <see cref="Correct"/> from where the preview put the control.
    /// </summary>
    internal static class GeometryEdits
    {
        private static readonly Regex TrailingOffset = new Regex(@"^(.*\S)\s*([+-])\s*(\d+)$");

        private sealed class Axis
        {
            public string Expression;
            public string Distance;
            public string Position;
            public string SizeExpression;
            public string Fill;
            public string Size;
            public int Index;
        }

        private static readonly Axis Horizontal = new Axis
        {
            Expression = "$X", Distance = "DistanceFromRightBorder", Position = "X",
            SizeExpression = "$Width", Fill = "FillWidth", Size = "Width", Index = 0
        };

        private static readonly Axis Vertical = new Axis
        {
            Expression = "$Y", Distance = "DistanceFromBottomBorder", Position = "Y",
            SizeExpression = "$Height", Fill = "FillHeight", Size = "Height", Index = 1
        };

        /// <param name="orderedKeys">The control's current INI keys and values, in the order they are applied.</param>
        /// <param name="before">The control's rectangle before the change, relative to its parent.</param>
        /// <param name="after">The rectangle it should have, relative to its parent.</param>
        /// <param name="parentSize">The parent's size after the change, or null if the control has no parent.</param>
        /// <param name="parentResized">Whether the parent's size changes too, which moves far-edge anchors.</param>
        public static List<KeyValuePair<string, string>> Apply(IReadOnlyList<KeyValuePair<string, string>> orderedKeys, Rectangle before,
            Rectangle after, Point? parentSize, bool parentResized)
        {
            var keys = new Dictionary<string, string>();
            var order = new Dictionary<string, int>();
            for (int i = 0; i < orderedKeys.Count; i++)
            {
                keys[orderedKeys[i].Key] = orderedKeys[i].Value;
                order[orderedKeys[i].Key] = i;
            }

            var result = new Dictionary<string, string>();
            Point? location = ParsePoint(keys, "Location");
            Point? size = ParsePoint(keys, "Size");
            Point? originalLocation = location;
            Point? originalSize = size;

            ApplyAxis(keys, order, result, Horizontal, before.X, after.X, before.Width, after.Width, parentSize?.X, parentResized, ref location, ref size);
            ApplyAxis(keys, order, result, Vertical, before.Y, after.Y, before.Height, after.Height, parentSize?.Y, parentResized, ref location, ref size);

            if (location != originalLocation)
                result["Location"] = Format(location.Value.X) + "," + Format(location.Value.Y);

            if (size != originalSize)
                result["Size"] = Format(size.Value.X) + "," + Format(size.Value.Y);

            // Keys that would keep their value aren't changes
            foreach (string key in result.Keys.ToList())
            {
                if (keys.TryGetValue(key, out string current) && current.Trim() == result[key])
                    result.Remove(key);
            }

            return new List<KeyValuePair<string, string>>(result);
        }

        private static void ApplyAxis(IDictionary<string, string> keys, IDictionary<string, int> order, Dictionary<string, string> result, Axis axis,
            int oldPosition, int newPosition, int oldLength, int newLength, int? parentLength, bool parentResized,
            ref Point? location, ref Point? size)
        {
            bool moved = newPosition != oldPosition;
            bool resized = newLength != oldLength;
            bool hasFill = parentLength.HasValue && !keys.ContainsKey(axis.SizeExpression) && TryGetInt(keys, axis.Fill, out _);
            bool hasDistance = parentLength.HasValue && TryGetInt(keys, axis.Distance, out _);

            if (resized && !hasFill)
            {
                if (keys.TryGetValue(axis.SizeExpression, out string expression))
                    result[axis.SizeExpression] = AdjustExpression(expression, newLength - oldLength);
                else if (size.HasValue)
                    size = WithComponent(size.Value, axis.Index, newLength);
                else
                    result[axis.Size] = Format(newLength);
            }

            // The size the control has when its far-edge distance is read, and the position when its fill is read:
            // the new one if a key setting it is read earlier, otherwise the old one. A new key goes before anchors.
            int lengthAtDistance = IsReadBefore(order, axis.Distance, axis.SizeExpression, "Size", axis.Size) || result.ContainsKey(axis.Size) ?
                newLength : oldLength;

            // An anchor to the far edge depends on the control's size and the parent's size as well
            if (moved || (hasDistance && (resized || parentResized)))
            {
                if (keys.TryGetValue(axis.Expression, out string expression))
                {
                    if (moved)
                        result[axis.Expression] = AdjustExpression(expression, newPosition - oldPosition);
                }
                else if (hasDistance)
                {
                    result[axis.Distance] = Format(parentLength.Value - lengthAtDistance - newPosition);
                }
                else if (location.HasValue)
                {
                    location = WithComponent(location.Value, axis.Index, newPosition);
                }
                else
                {
                    result[axis.Position] = Format(newPosition);
                }
            }

            int positionAtFill = IsReadBefore(order, axis.Fill, axis.Expression, "Location", axis.Position, axis.Distance) ||
                result.ContainsKey(axis.Position) ? newPosition : oldPosition;

            // A fill sets the size from the far edge's distance to the parent's edge
            if (hasFill && (moved || resized || parentResized))
                result[axis.Fill] = Format(parentLength.Value - positionAtFill - newLength);
        }

        /// <summary>
        /// Plans a correction for a control the preview put somewhere other than planned: shifts the key that
        /// decides each wrong edge or size by what is still missing. It works whatever the keys are calculated
        /// from, such as a $Width that reads the control's own position, or a FillWidth read before a new X key
        /// from another file of the BasedOn chain.
        /// </summary>
        /// <param name="orderedKeys">The control's current INI keys and values, in the order they are applied.</param>
        /// <param name="actual">Where the preview put the control, relative to its parent.</param>
        /// <param name="target">Where it should be, relative to its parent.</param>
        public static List<KeyValuePair<string, string>> Correct(IReadOnlyList<KeyValuePair<string, string>> orderedKeys,
            Rectangle actual, Rectangle target)
        {
            var keys = new Dictionary<string, string>();
            var order = new Dictionary<string, int>();
            for (int i = 0; i < orderedKeys.Count; i++)
            {
                keys[orderedKeys[i].Key] = orderedKeys[i].Value;
                order[orderedKeys[i].Key] = i;
            }

            var result = new Dictionary<string, string>();
            Point? location = ParsePoint(keys, "Location");
            Point? size = ParsePoint(keys, "Size");
            Point? originalLocation = location;
            Point? originalSize = size;

            CorrectAxis(keys, order, result, Horizontal, target.X - actual.X, target.Width - actual.Width, target.X, target.Width, ref location, ref size);
            CorrectAxis(keys, order, result, Vertical, target.Y - actual.Y, target.Height - actual.Height, target.Y, target.Height, ref location, ref size);

            if (location != originalLocation)
                result["Location"] = Format(location.Value.X) + "," + Format(location.Value.Y);

            if (size != originalSize)
                result["Size"] = Format(size.Value.X) + "," + Format(size.Value.Y);

            return new List<KeyValuePair<string, string>>(result);
        }

        private static void CorrectAxis(IDictionary<string, string> keys, IDictionary<string, int> order, Dictionary<string, string> result,
            Axis axis, int positionError, int lengthError, int targetPosition, int targetLength, ref Point? location, ref Point? size)
        {
            string sizeKey = LastRead(keys, order, axis.SizeExpression, axis.Fill, "Size", axis.Size);
            string positionKey = LastRead(keys, order, axis.Expression, axis.Distance, "Location", axis.Position);

            // A fill read after the position key changes the size when the control moves, and a far-edge distance
            // read after the size key changes the position when it is resized. A key added here goes before anchors.
            bool moveChangesFill = sizeKey == axis.Fill && (positionKey == null || order[positionKey] < order[sizeKey]);
            bool resizeChangesDistance = positionKey == axis.Distance && (sizeKey == null || order[sizeKey] < order[positionKey]);

            if (lengthError != 0 || (moveChangesFill && positionError != 0))
            {
                if (sizeKey == axis.SizeExpression)
                    result[sizeKey] = AdjustExpression(keys[sizeKey], lengthError);
                else if (sizeKey == axis.Fill)
                    result[sizeKey] = Format(ParseInt(keys[sizeKey]) - lengthError - (moveChangesFill ? positionError : 0));
                else if (sizeKey == "Size")
                    size = WithComponent(size.Value, axis.Index, (axis.Index == 0 ? size.Value.X : size.Value.Y) + lengthError);
                else if (sizeKey == axis.Size)
                    result[sizeKey] = Format(ParseInt(keys[sizeKey]) + lengthError);
                else
                    result[axis.Size] = Format(targetLength);
            }

            if (positionError != 0 || (resizeChangesDistance && lengthError != 0))
            {
                if (positionKey == axis.Expression)
                    result[positionKey] = AdjustExpression(keys[positionKey], positionError);
                else if (positionKey == axis.Distance)
                    result[positionKey] = Format(ParseInt(keys[positionKey]) - positionError - (resizeChangesDistance ? lengthError : 0));
                else if (positionKey == "Location")
                    location = WithComponent(location.Value, axis.Index, (axis.Index == 0 ? location.Value.X : location.Value.Y) + positionError);
                else if (positionKey == axis.Position)
                    result[positionKey] = Format(ParseInt(keys[positionKey]) + positionError);
                else
                    result[axis.Position] = Format(targetPosition);
            }
        }

        // Of the keys that can set an edge or size, the one read last, which decides it
        private static string LastRead(IDictionary<string, string> keys, IDictionary<string, int> order, params string[] candidates)
        {
            string last = null;

            foreach (string key in candidates)
            {
                bool usable = key == "Location" || key == "Size" ? ParsePoint(keys, key).HasValue :
                    key.StartsWith("$", StringComparison.Ordinal) ? keys.ContainsKey(key) : TryGetInt(keys, key, out _);

                if (usable && (last == null || order[key] > order[last]))
                    last = key;
            }

            return last;
        }

        private static int ParseInt(string value) => int.Parse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture);

        // Whether any of the setting keys exists and is read before the given key
        private static bool IsReadBefore(IDictionary<string, int> order, string key, params string[] settingKeys)
        {
            if (!order.TryGetValue(key, out int keyIndex))
                return true;

            foreach (string settingKey in settingKeys)
            {
                if (order.TryGetValue(settingKey, out int index) && index < keyIndex)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Adds an offset to an expression, merging it into a trailing "+ n" or "- n" term if there is one.
        /// </summary>
        public static string AdjustExpression(string expression, int delta)
        {
            expression = expression.Trim();

            if (int.TryParse(expression, NumberStyles.Integer, CultureInfo.InvariantCulture, out int constant))
                return Format(constant + delta);

            string baseExpression = expression;
            int offset = 0;

            Match match = TrailingOffset.Match(expression);
            if (match.Success)
            {
                baseExpression = match.Groups[1].Value;
                offset = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture) * (match.Groups[2].Value == "-" ? -1 : 1);
            }

            offset += delta;

            if (offset == 0)
                return baseExpression;

            return baseExpression + (offset > 0 ? " + " : " - ") + Format(Math.Abs(offset));
        }

        private static Point WithComponent(Point point, int index, int value) =>
            index == 0 ? new Point(value, point.Y) : new Point(point.X, value);

        private static Point? ParsePoint(IDictionary<string, string> keys, string key)
        {
            if (!keys.TryGetValue(key, out string value))
                return null;

            string[] parts = value.Split(',');
            if (parts.Length != 2 ||
                !int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int x) ||
                !int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int y))
            {
                return null;
            }

            return new Point(x, y);
        }

        private static bool TryGetInt(IDictionary<string, string> keys, string key, out int value)
        {
            value = 0;
            return keys.TryGetValue(key, out string text) &&
                int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private static string Format(int value) => value.ToString(CultureInfo.InvariantCulture);
    }
}
