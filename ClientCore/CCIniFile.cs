using Rampastring.Tools;
using System.Collections.Generic;
using System.IO;

namespace ClientCore
{
    public class CCIniFile : IniFile
    {
        // The file and section every key of the consolidated INI came from, for the designer tools.
        private Dictionary<string, Dictionary<string, IniKeySource>> keySources = new Dictionary<string, Dictionary<string, IniKeySource>>();
        private bool baseIniApplied;

        public CCIniFile(string path) : base(path)
        {
            // IniFile only applies the base INI when the file exists, but a file that so far
            // only exists as designer overlay edits still needs its BasedOn chain
            if (!baseIniApplied)
                ApplyBaseIni();

            foreach (IniSection section in Sections)
            {
                string baseSectionName = section.GetStringValue("$BaseSection", null);

                if (string.IsNullOrWhiteSpace(baseSectionName))
                    continue;

                var baseSection = Sections.Find(s => s.SectionName == baseSectionName);
                if (baseSection == null)
                {
                    Logger.Log($"Base section not found in INI file {path}, section {section.SectionName}, base section name: {baseSectionName}");
                    continue;
                }

                int addedKeyCount = 0;

                foreach (var kvp in baseSection.Keys)
                {
                    if (!section.KeyExists(kvp.Key))
                    {
                        section.Keys.Insert(addedKeyCount, kvp);
                        addedKeyCount++;
                        SetKeySource(section.SectionName, kvp.Key, GetKeySource(baseSectionName, kvp.Key));
                    }
                }
            }
        }

        /// <summary>
        /// Returns the file and section a key was defined in, following BasedOn and $BaseSection,
        /// or null if the key does not exist.
        /// </summary>
        public IniKeySource GetKeySource(string section, string key)
        {
            if (keySources.TryGetValue(section, out var sectionSources) && sectionSources.TryGetValue(key, out IniKeySource source))
                return source;

            return null;
        }

        private void SetKeySource(string section, string key, IniKeySource source)
        {
            if (source == null)
                return;

            if (!keySources.TryGetValue(section, out var sectionSources))
            {
                sectionSources = new Dictionary<string, IniKeySource>();
                keySources.Add(section, sectionSources);
            }

            sectionSources[key] = source;
        }

        // A new position or size key is read before anchors such as FillWidth, where saving the edit puts it too
        private bool InsertBeforeAnchors(IniOverlayEdit edit)
        {
            IniSection section = GetSection(edit.Section);
            if (section == null || !IniOverlay.GoesBeforeAnchors(edit.Key) || KeyExists(edit.Section, edit.Key))
                return false;

            int anchor = section.Keys.FindIndex(k => IniOverlay.IsAnchorKey(k.Key));
            if (anchor < 0)
                return false;

            section.Keys.Insert(anchor, new KeyValuePair<string, string>(edit.Key, edit.Value));
            return true;
        }

        protected override void ApplyBaseIni()
        {
            baseIniApplied = true;

            foreach (IniOverlayEdit edit in IniOverlay.GetEdits(FilePath))
            {
                if (edit.RemovesSection)
                    RemoveSection(edit.Section);
                else if (edit.RemovesKey)
                    RemoveKey(edit.Section, edit.Key);
                else if (!InsertBeforeAnchors(edit))
                    SetStringValue(edit.Section, edit.Key, edit.Value);
            }

            string fullPath = Path.GetFullPath(FilePath);
            foreach (IniSection section in Sections)
            {
                foreach (var kvp in section.Keys)
                    SetKeySource(section.SectionName, kvp.Key, new IniKeySource(fullPath, section.SectionName));
            }

            string basedOnSetting = GetStringValue("INISystem", "BasedOn", string.Empty);
            if (string.IsNullOrEmpty(basedOnSetting))
                return;

            string[] basedOns = basedOnSetting.Split(',');
            foreach (string basedOn in basedOns)
                ApplyBasedOnIni(basedOn);
        }

        private void ApplyBasedOnIni(string basedOn)
        {
            if (string.IsNullOrEmpty(basedOn))
                return;

            FileInfo baseIniFile;
            if (basedOn.Contains("$THEME_DIR$"))
                baseIniFile = SafePath.GetFile(basedOn.Replace("$THEME_DIR$", ProgramConstants.GetResourcePath()));
            else
                baseIniFile = SafePath.GetFile(SafePath.GetFileDirectoryName(FilePath), basedOn);

            // Consolidate with the INI file that this INI file is based on
            if (!baseIniFile.Exists && !IniOverlay.FileExists(baseIniFile.FullName))
                Logger.Log(FilePath + ": Base INI file not found! " + baseIniFile.FullName);

            CCIniFile baseIni = new CCIniFile(baseIniFile.FullName);
            ConsolidateIniFiles(baseIni, this);
            Sections = baseIni.Sections;

            // Keys of this file override the base file's, so their sources do too
            foreach (var section in keySources)
            {
                foreach (var key in section.Value)
                    baseIni.SetKeySource(section.Key, key.Key, key.Value);
            }

            keySources = baseIni.keySources;
        }
    }
}
