using System.Collections.Generic;
using ClientCore;
using Microsoft.Xna.Framework;
using Rampastring.Tools;
using Rampastring.XNAUI;
using Rampastring.XNAUI.XNAControls;

namespace ClientGUI
{
    public class XNAWindowBase : XNAPanel
    {
        public XNAWindowBase(WindowManager windowManager) : base(windowManager)
        {
            PanelBackgroundDrawMode = PanelBackgroundImageDrawMode.TILED;
        }

        /// <summary>
        /// Designer mode only: the rectangles controls had before their INI section was first applied,
        /// so a reload can start from the same state and give the same result as loading the client.
        /// </summary>
        protected Dictionary<XNAControl, Rectangle> PreIniRectangles { get; } =
            ProgramConstants.DesignerMode ? new Dictionary<XNAControl, Rectangle>() : null;

        /// <summary>
        /// Reads extra control information from a specific section of an INI file.
        /// Entries are in the format <c>name:type</c> or <c>name:type:parent</c>.
        /// With a parent, the control is placed inside that control instead of this window;
        /// if a control with the given name already exists, it is moved there.
        /// </summary>
        /// <param name="iniFile">The INI file.</param>
        /// <param name="sectionName">The section.</param>
        protected virtual void ParseExtraControls(IniFile iniFile, string sectionName)
        {
            var section = iniFile.GetSection(sectionName);

            if (section == null)
                return;

            foreach (var kvp in section.Keys)
            {
                string[] parts = kvp.Value.Split(':');
                if (parts.Length is not (2 or 3))
                    throw new ClientConfigurationException("Invalid ExtraControl specified in " + Name + ": " + kvp.Value);

                XNAControl container = this;

                if (parts.Length == 3)
                {
                    XNAControl parent = FindExtraControl(this, parts[2])
                        ?? throw new ClientConfigurationException("Parent " + parts[2] + " of ExtraControl " + parts[0] +
                            " not found in " + Name + ". Parents must be listed before their children.");

                    container = parent is XNAClientScrollPanel scrollPanel ? scrollPanel.Content : parent;
                }

                XNAControl control = FindExtraControl(this, parts[0]);

                if (control == null)
                {
                    control = ClientGUICreator.GetXnaControl(parts[1]);
                    control.Name = parts[0];
                    control.DrawOrder = -container.Children.Count;
                    container.AddChild(control);

                    if (PreIniRectangles != null && !PreIniRectangles.ContainsKey(control))
                        PreIniRectangles.Add(control, control.ClientRectangle);
                }
                else if (parts.Length == 3 && control.Parent != container)
                {
                    // Moves a control that was created in code, e.g. into a scroll panel
                    control.Parent.RemoveChild(control);
                    container.AddChildWithoutInitialize(control);
                }
                else
                {
                    continue;
                }

                if (container != this)
                    OnExtraControlNested(control);
            }
        }

        /// <summary>
        /// Called when an extra control has been placed inside a nested container
        /// rather than directly in this window.
        /// </summary>
        /// <param name="control">The control.</param>
        protected virtual void OnExtraControlNested(XNAControl control)
        {
        }

        /// <summary>
        /// Finds a control by name among the descendants of the given control.
        /// Does not search inside nested windows, as they parse their own extra controls.
        /// </summary>
        private static XNAControl FindExtraControl(XNAControl root, string name)
        {
            foreach (XNAControl child in root.Children)
            {
                if (child.Name == name)
                    return child;

                if (child is XNAWindowBase)
                    continue;

                XNAControl control = FindExtraControl(child, name);
                if (control != null)
                    return control;
            }

            return null;
        }

        protected virtual void ReadChildControlAttributes(IniFile iniFile)
        {
            foreach (XNAControl child in Children)
            {
                if (!(typeof(XNAWindowBase).IsAssignableFrom(child.GetType())))
                    child.GetAttributes(iniFile);
            }
        }

        /// <summary>
        /// Creates a control with a given name, using the specified GUI creator
        /// and control type name.
        /// </summary>
        /// <param name="guiCreator">The <see cref="GUICreator"/> to use.</param>
        /// <param name="controlTypeName">The name of the control's type.</param>
        /// <param name="controlName">The name of the created control.</param>
        /// <returns>The created control.</returns>
        protected virtual XNAControl CreateControl(GUICreator guiCreator, string controlTypeName, string controlName)
        {
            var control = guiCreator.CreateControl(WindowManager, controlTypeName);
            control.Name = controlName;
            control.DrawOrder = -Children.Count;
            AddChild(control);
            return control;
        }
    }
}
