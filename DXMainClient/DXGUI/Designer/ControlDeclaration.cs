#nullable enable

namespace DTAClient.DXGUI.Designer
{
    /// <summary>
    /// The INI key that creates a control, and the type and parent it names.
    /// </summary>
    internal sealed class ControlDeclaration
    {
        public ControlDeclaration(string section, string key, string typeName, string? parentName)
        {
            Section = section;
            Key = key;
            TypeName = typeName;
            ParentName = parentName;
        }

        public string Section { get; }
        public string Key { get; }
        public string TypeName { get; }

        /// <summary>
        /// The parent an ExtraControls entry names, or null.
        /// </summary>
        public string? ParentName { get; }
    }
}
