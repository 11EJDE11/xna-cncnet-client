#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using ClientCore;

using DTAClient.Domain;

using Rampastring.Tools;

namespace ClientLogic.Campaign;

/// <summary>
/// The campaign's missions, as the XNA CampaignSelector reads them: Battle.ini (unless IgnoreBattleIni), else the
/// BattleFSFileName INI, then the custom missions (maps with [ClientMissionConfig] in CustomMissionPath). A mission
/// whose custom mission ID is taken is kept in the list but disabled.
/// </summary>
public sealed class CampaignCatalog
{
    private readonly List<Mission> allMissions = [];
    private readonly Dictionary<int, Mission> uniqueIDToMissions = [];

    public IReadOnlyCollection<Mission> AllMissions => allMissions;

    public IReadOnlyDictionary<int, Mission> UniqueIDToMissions => uniqueIDToMissions;

    /// <summary>The catalog of the running client's missions.</summary>
    public static CampaignCatalog Load()
    {
        var catalog = new CampaignCatalog();
        catalog.ReadMissionList();
        return catalog;
    }

    private void AddMission(Mission mission)
    {
        // no matter whether the key is duplicated, the mission is always added to AllMissions
        allMissions.Add(mission);

        // but only the first mission is recorded in UniqueIDToMissions
        if (uniqueIDToMissions.ContainsKey(mission.CustomMissionID))
        {
            Logger.Log($"CampaignSelector: duplicated mission. CodeName: {mission.CodeName}. ID: {mission.CustomMissionID}. Description: {mission.UntranslatedGUIName}.");
            if (!string.IsNullOrEmpty(mission.Scenario))
                mission.Enabled = false;
        }
        else
        {
            uniqueIDToMissions.Add(mission.CustomMissionID, mission);
        }
    }

    private void ReadMissionList()
    {
        if (!ClientConfiguration.Instance.IgnoreBattleIni && AllMissions.Count == 0)
            ParseBattleIni("INI/Battle.ini");

        if (AllMissions.Count == 0)
            ParseBattleIni("INI/" + ClientConfiguration.Instance.BattleFSFileName);

        LoadCustomMissions();
    }

    private void LoadCustomMissions()
    {
        string customMissionsDirectory = SafePath.CombineDirectoryPath(ProgramConstants.GamePath, ClientConfiguration.Instance.CustomMissionPath);
        if (!Directory.Exists(customMissionsDirectory))
            return;

        string[] mapFiles = Directory.GetFiles(customMissionsDirectory, "*.map");
        if (mapFiles.Length == 0)
            return;

        foreach (string mapFilePath in mapFiles)
        {
            var mapFile = new IniFile(mapFilePath);

            IniSection clientMissionDataSection = mapFile.GetSection("ClientMissionConfig");
            if (clientMissionDataSection is null)
                continue;

            IniSection? gameMissionDataSection = mapFile.GetSection("GameMissionConfig");

            string filename = new FileInfo(mapFilePath).Name;
            string scenario = SafePath.CombineFilePath(ClientConfiguration.Instance.CustomMissionPath, filename);
            Mission mission = Mission.NewCustomMission(clientMissionDataSection, missionCodeName: filename, scenario, gameMissionDataSection);
            AddMission(mission);
        }
    }

    private bool ParseBattleIni(string path)
    {
        Logger.Log("Attempting to parse " + path + " to populate mission list.");

        FileInfo battleIniFileInfo = SafePath.GetFile(ProgramConstants.GamePath, path);
        if (!battleIniFileInfo.Exists)
        {
            Logger.Log("File " + path + " not found. Ignoring.");
            return false;
        }

        var battleIni = new IniFile(battleIniFileInfo.FullName);

        List<string> battleKeys = battleIni.GetSectionKeys("Battles");
        if (battleKeys == null)
            return false; // File exists but [Battles] doesn't

        for (int i = 0; i < battleKeys.Count; i++)
        {
            string battleEntry = battleKeys[i];
            string battleSection = battleIni.GetStringValue("Battles", battleEntry, "NOT FOUND");

            if (!battleIni.SectionExists(battleSection))
                continue;

            var mission = new Mission(battleIni.GetSection(battleSection), missionCodeName: battleEntry);
            AddMission(mission);
        }

        Logger.Log("Finished parsing " + path + ".");
        return true;
    }

    /// <summary>
    /// The missions to list (CampaignSelector.LoadMissionsWithFilter): official and/or custom ones, and only those
    /// with one of the tags if tags are given.
    /// </summary>
    public List<Mission> Filter(ISet<string>? selectedTags, bool disableCustomMissions = true, bool disableOfficialMissions = false)
    {
        IEnumerable<Mission> missions = AllMissions;

        if (disableCustomMissions && disableOfficialMissions)
        {
            // do nothing
        }
        else if (disableCustomMissions)
        {
            missions = missions.Where(mission => !mission.IsCustomMission);
        }
        else if (disableOfficialMissions)
        {
            missions = missions.Where(mission => mission.IsCustomMission);
        }

        if (selectedTags != null)
            missions = missions.Where(mission => mission.Tags.Intersect(selectedTags).Any()).ToList();

        return missions.ToList();
    }
}
