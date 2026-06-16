using BepInEx.Configuration;
using UnityEngine;

namespace DrawableSuits;

internal sealed class DrawableSuitsConfig
{
    public ConfigEntry<KeyCode> OpenEditorKey { get; }
    public ConfigEntry<KeyCode> EmergencyOpenKey { get; }
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
    public ConfigEntry<string> RecentColors { get; }
    
    public ConfigEntry<bool> ApplyLocalFirstPersonArms { get; }
    
    public ConfigEntry<bool> AutoDisableBrokenJetpackWarningLateUpdatePatch { get; }
    public DrawableSuitsConfig(ConfigFile config)
    {
        OpenEditorKey = config.Bind("Input", "OpenEditorKey", KeyCode.F8, "Keyboard key that opens or closes the suit editor.");
        EmergencyOpenKey = config.Bind("Input", "EmergencyOpenKey", KeyCode.F10, "Keyboard key that opens the editor even when suit/player detection is incomplete.");
        ControllerCursorSpeed = config.Bind("Input", "ControllerCursorSpeed", 900f, "Virtual cursor speed in pixels per second.");
        MaxTextureSize = config.Bind("Textures", "MaxTextureSize", 1024, "Maximum width and height for editable and synced suit textures.");
        MaxUndoStates = config.Bind("Editor", "MaxUndoStates", 12, "Maximum undo snapshots kept while editing.");
        EnableNetworkSync = config.Bind("Multiplayer", "EnableNetworkSync", true, "Sync applied and saved suit designs to other DrawableSuits users.");
        MaxSyncBytes = config.Bind("Multiplayer", "MaxSyncBytes", 4194304, "Maximum PNG payload size allowed for multiplayer sync.");
        SyncChunkBytes = config.Bind("Multiplayer", "SyncChunkBytes", 48000, "Byte size for each Netcode custom-message texture chunk.");
        EnableOsFileDialog = config.Bind("Decals", "EnableOsFileDialog", false, "Legacy setting. The Decals menu Add Decal button now uses an external Windows picker process and this value is ignored.");
        EnableExperimentalModelPreview = config.Bind("Editor", "EnableExperimentalModelPreview", false, "Disabled by default. Uses the old camera/RenderTexture 3D model preview only for diagnostics; third-person world painting is the default.");
        StartInUvFallbackMode = config.Bind("Editor", "StartInUvFallbackMode", false, "Open directly into the old UV texture fallback instead of third-person world painting. Useful for diagnostics.");
        ThirdPersonCameraDistance = config.Bind("Editor", "ThirdPersonCameraDistance", 3.4f, "Default third-person editor camera orbit distance.");
        RecentColors = config.Bind("Editor", "RecentColors", string.Empty, "Recent brush colors as comma-separated #RRGGBB values. Colors are added only after Paint, Fill, or Text writes to the suit.");
        ApplyLocalFirstPersonArms = config.Bind("Compatibility", "ApplyLocalFirstPersonArms", false, "Experimental. When false, DrawableSuits does not apply edited materials to the local first-person arms/body outside the editor.");
        AutoDisableBrokenJetpackWarningLateUpdatePatch = config.Bind("Compatibility", "AutoDisableBrokenJetpackWarningLateUpdatePatch", true, "Automatically disable JetpackWarning.Patches.PlayerControllerB_LateUpdate_Postfix if it repeatedly throws NullReferenceException and breaks session startup.");
    }
}
