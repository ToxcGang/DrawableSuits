using BepInEx.Configuration;
using UnityEngine;

namespace DrawableSuits;

internal sealed class DrawableSuitsConfig
{
    public ConfigEntry<KeyCode> OpenEditorKey { get; }
    public ConfigEntry<KeyCode> EmergencyOpenKey { get; }
    public ConfigEntry<KeyCode> DebugOverlayKey { get; }
    public ConfigEntry<bool> ShowStartupDiagnostics { get; }
    public ConfigEntry<float> StartupDiagnosticsSeconds { get; }
    public ConfigEntry<int> MaxTextureSize { get; }
    public ConfigEntry<int> MaxUndoStates { get; }
    public ConfigEntry<int> MaxSyncBytes { get; }
    public ConfigEntry<int> SyncChunkBytes { get; }
    public ConfigEntry<float> ControllerCursorSpeed { get; }
    public ConfigEntry<bool> EnableNetworkSync { get; }
    public ConfigEntry<bool> EnableOsFileDialog { get; }
    public ConfigEntry<bool> EnableExperimentalModelPreview { get; }
    public ConfigEntry<bool> StartInUvFallbackMode { get; }
    public ConfigEntry<float> ThirdPersonCameraDistance { get; }
    
    public ConfigEntry<bool> ApplyLocalFirstPersonArms { get; }
    public DrawableSuitsConfig(ConfigFile config)
    {
        OpenEditorKey = config.Bind("Input", "OpenEditorKey", KeyCode.F8, "Keyboard key that opens or closes the suit editor.");
        EmergencyOpenKey = config.Bind("Input", "EmergencyOpenKey", KeyCode.F10, "Keyboard key that opens the diagnostics editor overlay even when suit/player detection is incomplete.");
        DebugOverlayKey = config.Bind("Input", "DebugOverlayKey", KeyCode.F9, "Keyboard key that toggles the lightweight DrawableSuits debug HUD.");
        ShowStartupDiagnostics = config.Bind("Diagnostics", "ShowStartupDiagnostics", true, "Show a small DrawableSuits loaded/debug HUD for a short time after startup.");
        StartupDiagnosticsSeconds = config.Bind("Diagnostics", "StartupDiagnosticsSeconds", 30f, "How long the startup debug HUD remains visible after plugin load.");
        ControllerCursorSpeed = config.Bind("Input", "ControllerCursorSpeed", 900f, "Virtual cursor speed in pixels per second.");
        MaxTextureSize = config.Bind("Textures", "MaxTextureSize", 1024, "Maximum width and height for editable and synced suit textures.");
        MaxUndoStates = config.Bind("Editor", "MaxUndoStates", 12, "Maximum undo snapshots kept while editing.");
        EnableNetworkSync = config.Bind("Multiplayer", "EnableNetworkSync", true, "Sync applied and saved suit designs to other DrawableSuits users.");
        MaxSyncBytes = config.Bind("Multiplayer", "MaxSyncBytes", 1048576, "Maximum PNG payload size allowed for multiplayer sync.");
        SyncChunkBytes = config.Bind("Multiplayer", "SyncChunkBytes", 48000, "Byte size for each Netcode custom-message texture chunk.");
        EnableOsFileDialog = config.Bind("Decals", "EnableOsFileDialog", false, "Disabled/experimental. In-game OS file dialogs are ignored in 0.4.1; place PNG/JPG decals in the Decals folder and press Refresh Decals.");
        EnableExperimentalModelPreview = config.Bind("Editor", "EnableExperimentalModelPreview", false, "Disabled by default. Uses the old camera/RenderTexture 3D model preview only for diagnostics; third-person world painting is the default.");
        StartInUvFallbackMode = config.Bind("Editor", "StartInUvFallbackMode", false, "Open directly into the old UV texture fallback instead of third-person world painting. Useful for diagnostics.");
        ThirdPersonCameraDistance = config.Bind("Editor", "ThirdPersonCameraDistance", 3.4f, "Default third-person editor camera orbit distance.");
        ApplyLocalFirstPersonArms = config.Bind("Compatibility", "ApplyLocalFirstPersonArms", false, "Experimental. When false, DrawableSuits does not apply edited materials to the local first-person arms/body outside the editor.");
    }
}
