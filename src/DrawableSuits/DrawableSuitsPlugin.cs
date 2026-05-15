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
        DrawableSuitsDiagnostics.Initialize();
        DrawableSuitsDiagnostics.Info($"Config OpenEditorKey={ModConfig.OpenEditorKey.Value}; EmergencyOpenKey={ModConfig.EmergencyOpenKey.Value}; ControllerCursorSpeed={ModConfig.ControllerCursorSpeed.Value}; MaxTextureSize={ModConfig.MaxTextureSize.Value}; MaxUndoStates={ModConfig.MaxUndoStates.Value}; NetworkSync={ModConfig.EnableNetworkSync.Value}; MaxSyncBytes={ModConfig.MaxSyncBytes.Value}; SyncChunkBytes={ModConfig.SyncChunkBytes.Value}; OsFileDialog={ModConfig.EnableOsFileDialog.Value}");

        _harmony = new Harmony(PluginInfo.Guid);
        try
        {
            DrawableSuitsDiagnostics.Info("Applying Harmony patches.");
            _harmony.PatchAll();
            DrawableSuitsDiagnostics.Info("Harmony patches applied.");
        }
        catch (System.Exception ex)
        {
            DrawableSuitsDiagnostics.Exception("Harmony PatchAll failed", ex);
            throw;
        }

        _runtimeObject = new GameObject("DrawableSuitsRuntime");
        DontDestroyOnLoad(_runtimeObject);
        DrawableSuitsDiagnostics.Info("Created DrawableSuitsRuntime object.");
        Registry = _runtimeObject.AddComponent<SuitTextureRegistry>();
        DrawableSuitsDiagnostics.Info("Added SuitTextureRegistry component.");
        Sync = _runtimeObject.AddComponent<SuitSyncManager>();
        DrawableSuitsDiagnostics.Info("Added SuitSyncManager component.");
        Editor = _runtimeObject.AddComponent<SuitEditorController>();
        DrawableSuitsDiagnostics.Info("Added SuitEditorController component.");

        DrawableSuitsDiagnostics.Info($"{PluginInfo.Name} {PluginInfo.Version} loaded with GUID {PluginInfo.Guid}.");
    }

    private void OnDestroy()
    {
        DrawableSuitsDiagnostics.Info("DrawableSuitsPlugin.OnDestroy called.");
        _harmony?.UnpatchSelf();
        if (_runtimeObject != null)
        {
            Destroy(_runtimeObject);
        }
    }
}
