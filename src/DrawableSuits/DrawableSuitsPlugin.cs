using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

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
    internal static DrawableSuitsRuntimeHost RuntimeHost { get; private set; }
    internal static SuitSyncManager Sync { get; private set; }
    internal static bool IsEditorOpen => Editor != null && Editor.IsOpenForDiagnostics;

    private static GameObject _runtimeRoot;
    private Harmony _harmony;

    private void Awake()
    {
        Instance = this;
        ModLogger = Logger;
        ModConfig = new DrawableSuitsConfig(Config);
        DrawableSuitsPaths.EnsureCreated();
        DrawableSuitsDiagnostics.Initialize();
        DrawableSuitsDiagnostics.Info($"Plugin Awake. pluginInstance={DescribeUnityObject(this)}; pluginGameObject={DescribeUnityObject(gameObject)}; scene={SceneManager.GetActiveScene().name}");
        DrawableSuitsDiagnostics.Info($"Config OpenEditorKey={ModConfig.OpenEditorKey.Value}; EmergencyOpenKey={ModConfig.EmergencyOpenKey.Value}; ControllerCursorSpeed={ModConfig.ControllerCursorSpeed.Value}; MaxTextureSize={ModConfig.MaxTextureSize.Value}; MaxUndoStates={ModConfig.MaxUndoStates.Value}; NetworkSync={ModConfig.EnableNetworkSync.Value}; MaxSyncBytes={ModConfig.MaxSyncBytes.Value}; SyncChunkBytes={ModConfig.SyncChunkBytes.Value}; ExperimentalModelPreview={ModConfig.EnableExperimentalModelPreview.Value}; StartInUvFallbackMode={ModConfig.StartInUvFallbackMode.Value}; ThirdPersonCameraDistance={ModConfig.ThirdPersonCameraDistance.Value}; ApplyLocalFirstPersonArms={ModConfig.ApplyLocalFirstPersonArms.Value}");

        _harmony = new Harmony(PluginInfo.Guid);
        try
        {
            DrawableSuitsDiagnostics.Info("Applying Harmony patches.");
            _harmony.PatchAll();
            PlayerControllerBPatches.ApplyOptionalGameplayInputPatches(_harmony);
            DrawableSuitsDiagnostics.Info("Harmony patches applied.");
        }
        catch (System.Exception ex)
        {
            DrawableSuitsDiagnostics.Exception("Harmony PatchAll failed", ex);
            throw;
        }

        SceneManager.sceneLoaded += OnSceneLoaded;
        EnsureRuntimeReady("Plugin.Awake");

        DrawableSuitsDiagnostics.Info($"{PluginInfo.Name} {PluginInfo.Version} loaded with GUID {PluginInfo.Guid}.");
    }

    private void Start()
    {
        DrawableSuitsDiagnostics.Info($"Plugin Start. plugin={DescribeUnityObject(this)}; host={DescribeUnityObject(RuntimeHost)}; editor={DescribeUnityObject(Editor)}");
        EnsureRuntimeReady("Plugin.Start");
        SessionSafetyGuard.Run("Plugin.Start", true);
    }

    private void Update()
    {
        EnsureRuntimeReady("Plugin.Update");
    }

    private void OnDestroy()
    {
        DrawableSuitsDiagnostics.Info("DrawableSuitsPlugin.OnDestroy called.");
        if (Editor != null && Editor.IsOpenForDiagnostics)
        {
            Editor.CloseEditorForPluginDestroy();
        }
        SceneManager.sceneLoaded -= OnSceneLoaded;
        _harmony?.UnpatchSelf();
        Registry = null;
        Sync = null;
        Editor = null;
        RuntimeHost = null;
        if (_runtimeRoot != null)
        {
            Destroy(_runtimeRoot);
            _runtimeRoot = null;
        }

        if (ReferenceEquals(Instance, this))
        {
            Instance = null;
        }
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        DrawableSuitsDiagnostics.Info($"Scene loaded. name={scene.name}; buildIndex={scene.buildIndex}; mode={mode}");
        var shouldCloseEditor = ShouldCloseEditorForScene(scene, mode);
        EnsureRuntimeReady($"SceneLoaded:{scene.name}");
        if (shouldCloseEditor && Editor != null && Editor.IsOpenForDiagnostics)
        {
            DrawableSuitsDiagnostics.Info($"Closing editor because scene changed to '{scene.name}' with mode={mode}.");
            Editor.CloseEditorForSceneChange();
        }

        Registry?.ReapplyAllIfReady($"SceneLoaded:{scene.name}");
        SessionSafetyGuard.Run($"SceneLoaded:{scene.name}", true);
    }

    internal static bool HasSessionSafetyContext()
    {
        var sceneName = SceneManager.GetActiveScene().name;
        return StartOfRound.Instance != null
            || UnityEngine.Object.FindObjectOfType<GameNetworkManager>()?.localPlayerController != null
            || string.Equals(sceneName, "SampleSceneRelay", System.StringComparison.OrdinalIgnoreCase);
    }

    internal static bool HasGameplayEditorContext()
    {
        return StartOfRound.Instance?.localPlayerController != null && Camera.main != null;
    }

    private static bool ShouldCloseEditorForScene(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Additive)
        {
            return true;
        }

        return string.Equals(scene.name, "MainMenu", System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(scene.name, "InitScene", System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(scene.name, "InitSceneLaunchOptions", System.StringComparison.OrdinalIgnoreCase);
    }

    internal static bool EnsureRuntimeReady(string reason)
    {
        if (Instance == null)
        {
            DrawableSuitsDiagnostics.Warn($"EnsureRuntimeReady({reason}) failed: plugin instance is null.");
            return false;
        }

        var hostObject = GetOrCreateRuntimeRoot(reason);
        if (hostObject == null)
        {
            DrawableSuitsDiagnostics.Warn($"EnsureRuntimeReady({reason}) failed: runtime root is null.");
            return false;
        }

        Registry = EnsureComponent(Registry, hostObject, reason, "registry");
        Sync = EnsureComponent(Sync, hostObject, reason, "sync");
        Editor = EnsureComponent(Editor, hostObject, reason, "editor");
        RuntimeHost = EnsureComponent(RuntimeHost, hostObject, reason, "runtime host");

        var ready = Registry != null && Sync != null && Editor != null && RuntimeHost != null;
        if (!ready)
        {
            DrawableSuitsDiagnostics.Warn($"EnsureRuntimeReady({reason}) incomplete. registry={DescribeUnityObject(Registry)}; sync={DescribeUnityObject(Sync)}; editor={DescribeUnityObject(Editor)}; host={DescribeUnityObject(RuntimeHost)}");
        }

        return ready;
    }

    private static GameObject GetOrCreateRuntimeRoot(string reason)
    {
        if (_runtimeRoot != null)
        {
            return _runtimeRoot;
        }

        _runtimeRoot = new GameObject("DrawableSuitsRuntimeRoot");
        _runtimeRoot.hideFlags = HideFlags.HideAndDontSave;
        DontDestroyOnLoad(_runtimeRoot);
        DrawableSuitsDiagnostics.Info($"Runtime root created. reason={reason}; root={DescribeUnityObject(_runtimeRoot)}");
        return _runtimeRoot;
    }

    internal static bool RequestOpenEditor(string source)
    {
        var ready = EnsureRuntimeReady($"RequestOpenEditor:{source}");
        DrawableSuitsDiagnostics.Info($"RequestOpenEditor source={source}; runtimeReady={ready}; editor={DescribeUnityObject(Editor)}");
        if (!ready || Editor == null)
        {
            return false;
        }

        var opened = Editor.OpenEditor(source);
        DrawableSuitsDiagnostics.Info($"RequestOpenEditor result source={source}; opened={opened}; editorOpen={Editor.IsOpenForDiagnostics}; canvasActive={Editor.CanvasActiveForDiagnostics}");
        return opened;
    }

    internal static bool RequestToggleEditor(string source)
    {
        var ready = EnsureRuntimeReady($"RequestToggleEditor:{source}");
        DrawableSuitsDiagnostics.Info($"RequestToggleEditor source={source}; runtimeReady={ready}; editor={DescribeUnityObject(Editor)}");
        if (!ready || Editor == null)
        {
            return false;
        }

        return Editor.IsOpenForDiagnostics ? CloseEditor(source) : RequestOpenEditor(source);
    }

    internal static bool CloseEditor(string source)
    {
        EnsureRuntimeReady($"CloseEditor:{source}");
        if (Editor == null)
        {
            DrawableSuitsDiagnostics.Warn($"CloseEditor source={source} ignored because editor is null.");
            return false;
        }

        DrawableSuitsDiagnostics.Info($"CloseEditor source={source}; editor={DescribeUnityObject(Editor)}");
        Editor.CloseEditor(source);
        return true;
    }

    internal static string DescribeUnityObject(Object unityObject)
    {
        if (unityObject == null)
        {
            return "null";
        }

        return $"{unityObject.GetType().Name}(id={unityObject.GetInstanceID()}, name={unityObject.name})";
    }

    private static T EnsureComponent<T>(T current, GameObject hostObject, string reason, string label)
        where T : Component
    {
        if (current != null)
        {
            return current;
        }

        var existing = hostObject.GetComponent<T>();
        if (existing != null)
        {
            DrawableSuitsDiagnostics.Info($"EnsureRuntimeReady({reason}) recovered existing {label}: {DescribeUnityObject(existing)}");
            return existing;
        }

        var created = hostObject.AddComponent<T>();
        DrawableSuitsDiagnostics.Info($"EnsureRuntimeReady({reason}) created {label}: {DescribeUnityObject(created)} on {DescribeUnityObject(hostObject)}");
        return created;
    }
}
