using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace DrawableSuits;

[BepInPlugin(PluginInfo.Guid, PluginInfo.Name, PluginInfo.Version)]
[BepInProcess("Lethal Company.exe")]
public sealed class DrawableSuitsPlugin : BaseUnityPlugin
{
    internal static DrawableSuitsPlugin Instance { get; private set; }
    internal static ManualLogSource ModLogger { get; private set; }
    internal static DrawableSuitsConfig ModConfig { get; private set; }
    internal static SuitTextureRegistry Registry { get; private set; }
    internal static SuitEditorController Editor { get; private set; }
    internal static SuitSyncManager Sync { get; private set; }

    private Harmony _harmony;
    private GameObject _runtimeObject;

    private void Awake()
    {
        Instance = this;
        ModLogger = Logger;
        ModConfig = new DrawableSuitsConfig(Config);
        DrawableSuitsPaths.EnsureCreated();

        _harmony = new Harmony(PluginInfo.Guid);
        _harmony.PatchAll();

        _runtimeObject = new GameObject("DrawableSuitsRuntime");
        DontDestroyOnLoad(_runtimeObject);
        Registry = _runtimeObject.AddComponent<SuitTextureRegistry>();
        Sync = _runtimeObject.AddComponent<SuitSyncManager>();
        Editor = _runtimeObject.AddComponent<SuitEditorController>();

        Logger.LogInfo($"{PluginInfo.Name} {PluginInfo.Version} loaded with GUID {PluginInfo.Guid}.");
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
        if (_runtimeObject != null)
        {
            Destroy(_runtimeObject);
        }
    }
}
