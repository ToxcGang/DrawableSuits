using BepInEx.Configuration;
using UnityEngine;

namespace DrawableSuits;

internal sealed class DrawableSuitsConfig
{
    public ConfigEntry<KeyCode> OpenEditorKey { get; }
    public ConfigEntry<int> MaxTextureSize { get; }
    public ConfigEntry<int> MaxUndoStates { get; }
    public ConfigEntry<int> MaxSyncBytes { get; }
    public ConfigEntry<int> SyncChunkBytes { get; }
    public ConfigEntry<float> ControllerCursorSpeed { get; }
    public ConfigEntry<bool> EnableNetworkSync { get; }
    public ConfigEntry<bool> EnableOsFileDialog { get; }

    public DrawableSuitsConfig(ConfigFile config)
    {
        OpenEditorKey = config.Bind("Input", "OpenEditorKey", KeyCode.F8, "Keyboard key that opens or closes the suit editor.");
        ControllerCursorSpeed = config.Bind("Input", "ControllerCursorSpeed", 900f, "Virtual cursor speed in pixels per second.");
        MaxTextureSize = config.Bind("Textures", "MaxTextureSize", 1024, "Maximum width and height for editable and synced suit textures.");
        MaxUndoStates = config.Bind("Editor", "MaxUndoStates", 12, "Maximum undo snapshots kept while editing.");
        EnableNetworkSync = config.Bind("Multiplayer", "EnableNetworkSync", true, "Sync applied and saved suit designs to other DrawableSuits users.");
        MaxSyncBytes = config.Bind("Multiplayer", "MaxSyncBytes", 1048576, "Maximum PNG payload size allowed for multiplayer sync.");
        SyncChunkBytes = config.Bind("Multiplayer", "SyncChunkBytes", 48000, "Byte size for each Netcode custom-message texture chunk.");
        EnableOsFileDialog = config.Bind("Decals", "EnableOsFileDialog", true, "Enable the optional Windows open-file dialog for importing decals.");
    }
}
