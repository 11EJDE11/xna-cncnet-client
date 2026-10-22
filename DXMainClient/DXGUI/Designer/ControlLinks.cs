#nullable enable

using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Rampastring.XNAUI.XNAControls;

namespace DTAClient.DXGUI.Designer
{
    /// <summary>
    /// A position or size key of one control that reads another control, like $X=getX(MapPreviewBox):
    /// moving or resizing the source moves or resizes the follower too.
    /// </summary>
    internal sealed class ControlLink
    {
        public ControlLink(XNAControl follower, string section, string key, string value, XNAControl source)
        {
            Follower = follower;
            Section = section;
            Key = key;
            Value = value;
            Source = source;
        }

        public XNAControl Follower { get; }
        public string Section { get; }
        public string Key { get; }
        public string Value { get; }
        public XNAControl Source { get; }
    }

    internal static class ControlLinks
    {
        public static readonly string[] Keys = { "$X", "$Y", "$Width", "$Height" };

        // A control named as the argument of a function, as in getBottom(lblMapName)
        private static readonly Regex Reference = new Regex(@"\(\s*([A-Za-z_]\w*)\s*\)");

        /// <summary>
        /// Returns every link between the controls the window reads from its INI file.
        /// </summary>
        public static List<ControlLink> Find(IniWindow window)
        {
            var links = new List<ControlLink>();
            if (window.Ini == null)
                return links;

            List<XNAControl> controls = GetControls(window.Control).ToList();
            var controlsByName = new Dictionary<string, XNAControl>();
            foreach (XNAControl control in controls)
            {
                if (!string.IsNullOrEmpty(control.Name) && !controlsByName.ContainsKey(control.Name))
                    controlsByName.Add(control.Name, control);
            }

            foreach (XNAControl follower in controls)
            {
                string? section = window.SectionFor(follower);
                if (section == null)
                    continue;

                foreach (string key in Keys)
                {
                    string value = window.Ini.GetStringValue(section, key, string.Empty);

                    foreach (string name in Reference.Matches(value).Cast<Match>().Select(m => m.Groups[1].Value).Distinct())
                    {
                        if (controlsByName.TryGetValue(name, out XNAControl? source) && source != follower)
                            links.Add(new ControlLink(follower, section, key, value, source));
                    }
                }
            }

            return links;
        }

        // The window and the controls in it, but not those of another window inside it
        private static IEnumerable<XNAControl> GetControls(XNAControl control)
        {
            yield return control;

            foreach (XNAControl child in control.Children)
            {
                if (IniWindow.From(child) != null)
                    continue;

                foreach (XNAControl descendant in GetControls(child))
                    yield return descendant;
            }
        }
    }
}
