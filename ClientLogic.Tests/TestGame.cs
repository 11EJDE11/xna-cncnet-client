using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;

using ClientCore;

using DTAClient.Domain.Multiplayer;

using Rampastring.Tools;

namespace ClientLogic.Tests;

/// <summary>
/// The minimal game installation in the TestGame folder, which the build copies next to the test assembly.
/// The client finds the game root from the "Resources" folder, so the test output folder is the game root.
/// </summary>
internal static class TestGame
{
    private static readonly object initLock = new();
    private static bool initialized;

    public static string Root => ProgramConstants.GamePath;

    public static void EnsureInitialized()
    {
        lock (initLock)
        {
            if (initialized)
                return;

            SetEntryAssembly();
            InitializeClient();
            initialized = true;
        }
    }

    /// <summary>
    /// Sets the entry assembly as soon as the test assembly loads, so a test that reaches ProgramConstants before
    /// calling <see cref="EnsureInitialized"/> can't make its type initializer fail for every later test.
    /// </summary>
#pragma warning disable CA2255 // A test assembly's own set-up
    [ModuleInitializer]
    internal static void OnModuleLoaded() => SetEntryAssembly();
#pragma warning restore CA2255

    // Separate and not inlined: the .NET Framework JIT may initialize ProgramConstants when it compiles
    // a method that uses it, which must happen after SetEntryAssembly.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void InitializeClient()
    {
        // Map paths in MPMaps.ini are relative to the game directory, like when the client runs.
        Environment.CurrentDirectory = ProgramConstants.GamePath;
        UserINISettings.Initialize("TestSettings.ini");
        _ = ClientConfiguration.Instance;
    }

    /// <summary>
    /// The client finds the game directory from the entry assembly. The .NET Framework test host runs from
    /// the NuGet cache, so make this test assembly (next to the TestGame files) the entry assembly there.
    /// </summary>
    private static void SetEntryAssembly()
    {
#if NETFRAMEWORK
        var manager = new AppDomainManager();
        typeof(AppDomainManager).GetField("m_entryAssembly", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(manager, typeof(TestGame).Assembly);
        typeof(AppDomain).GetField("_domainManager", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(AppDomain.CurrentDomain, manager);
#endif
    }

    /// <summary>Loads a map from the test MPMaps.ini together with one of its game modes.</summary>
    public static GameModeMap LoadGameModeMap(string mapPath, string gameModeName)
    {
        EnsureInitialized();

        var mpMapsIni = new IniFile(SafePath.CombineFilePath(Root, ClientConfiguration.Instance.MPMapsIniPath));

        var map = new Map(mapPath, false);
        if (!map.InitializeFromMpMapsINI(mpMapsIni))
            throw new InvalidOperationException($"Could not load test map {mapPath}.");

        var gameMode = new GameMode(gameModeName);
        gameMode.Initialize();

        return new GameModeMap(gameMode, map);
    }

    /// <summary>Returns the INI file exactly as it would be written to disk.</summary>
    public static string ToText(IniFile iniFile)
    {
        using var stream = new MemoryStream();
        iniFile.WriteIniStream(stream);
        return iniFile.Encoding.GetString(stream.ToArray());
    }
}
