using System;
using System.Collections.Generic;
using System.IO;
using GameNetcodeStuff;
using UnityEngine;

namespace DrawableSuits;

internal sealed class SuitTextureRegistry : MonoBehaviour
{
    private readonly Dictionary<int, SuitTextureState> _states = new();

    public IReadOnlyDictionary<int, SuitTextureState> States => _states;

    public SuitTextureState GetOrCreateState(int suitId)
    {
        if (_states.TryGetValue(suitId, out var state))
        {
            return state;
        }

        var item = GetUnlockableItem(suitId);
        if (item == null || item.suitMaterial == null)
        {
            return null;
        }

        var maxSize = DrawableSuitsPlugin.ModConfig.MaxTextureSize.Value;
        var originalTexture = item.suitMaterial.mainTexture;
        var baseTexture = TextureTools.CreateEditableCopy(originalTexture, maxSize);
        var editableTexture = TextureTools.CreateEditableCopy(originalTexture, maxSize);
        var material = new Material(item.suitMaterial)
        {
            name = $"{item.suitMaterial.name} (DrawableSuits)",
            mainTexture = editableTexture
        };

        state = new SuitTextureState
        {
            SuitId = suitId,
            SuitName = string.IsNullOrWhiteSpace(item.unlockableName) ? $"Suit {suitId}" : item.unlockableName,
            OriginalMaterial = item.suitMaterial,
            OriginalTexture = originalTexture,
            RuntimeMaterial = material,
            BaseTexture = baseTexture,
            EditableTexture = editableTexture,
            ActiveDesignName = string.Empty
        };

        _states[suitId] = state;
        return state;
    }

    public Texture2D GetEditableTexture(int suitId)
    {
        return GetOrCreateState(suitId)?.EditableTexture;
    }

    public Material GetRuntimeMaterial(int suitId)
    {
        return GetOrCreateState(suitId)?.RuntimeMaterial;
    }

    public void ApplyEditedTexture(int suitId, string designName, bool broadcast)
    {
        var state = GetOrCreateState(suitId);
        if (state == null)
        {
            return;
        }

        state.RuntimeMaterial.mainTexture = state.EditableTexture;
        state.ActiveDesignName = designName ?? string.Empty;
        ApplyStateToWorld(state);

        if (broadcast)
        {
            DrawableSuitsPlugin.Sync.BroadcastDesign(state);
        }
    }

    public void ApplyReceivedTexture(int suitId, string designName, Texture2D texture)
    {
        var state = GetOrCreateState(suitId);
        if (state == null || texture == null)
        {
            return;
        }

        TextureTools.CopyInto(state.EditableTexture, texture);
        state.RuntimeMaterial.mainTexture = state.EditableTexture;
        state.ActiveDesignName = designName ?? "Received design";
        ApplyStateToWorld(state);
    }

    public void ResetSuit(int suitId)
    {
        var state = GetOrCreateState(suitId);
        if (state == null)
        {
            return;
        }

        TextureTools.CopyInto(state.EditableTexture, state.BaseTexture);
        state.ActiveDesignName = string.Empty;
        ApplyStateToWorld(state);
    }

    public void ReapplyAll()
    {
        RefreshKnownSuitMaterials();
        foreach (var state in _states.Values)
        {
            ApplyStateToWorld(state);
        }
    }

    public void RefreshKnownSuitMaterials()
    {
        var round = StartOfRound.Instance;
        if (round?.unlockablesList?.unlockables == null)
        {
            return;
        }

        foreach (var pair in _states)
        {
            var item = GetUnlockableItem(pair.Key);
            if (item != null)
            {
                item.suitMaterial = pair.Value.RuntimeMaterial;
            }
        }
    }

    public int GetLocalSuitId()
    {
        var player = StartOfRound.Instance?.localPlayerController;
        return player != null ? player.currentSuitID : -1;
    }

    public List<int> GetSuitIds()
    {
        var result = new List<int>();
        var round = StartOfRound.Instance;
        var list = round?.unlockablesList?.unlockables;
        if (list == null)
        {
            return result;
        }

        for (var i = 0; i < list.Count; i++)
        {
            if (list[i]?.suitMaterial != null)
            {
                result.Add(i);
            }
        }

        return result;
    }

    public string GetSuitName(int suitId)
    {
        var item = GetUnlockableItem(suitId);
        if (item == null)
        {
            return $"Suit {suitId}";
        }

        return string.IsNullOrWhiteSpace(item.unlockableName) ? $"Suit {suitId}" : item.unlockableName;
    }

    public bool SaveDesign(int suitId, string designName)
    {
        var state = GetOrCreateState(suitId);
        if (state == null)
        {
            return false;
        }

        var safeName = TextureTools.SanitizeFileName(designName);
        var textureName = $"{safeName}.png";
        var metadataName = $"{safeName}.json";
        var texturePath = Path.Combine(DrawableSuitsPaths.Textures, textureName);
        var metadataPath = Path.Combine(DrawableSuitsPaths.Saves, metadataName);

        File.WriteAllBytes(texturePath, ImageConversion.EncodeToPNG(state.EditableTexture));

        var now = DateTime.UtcNow.ToString("o");
        var metadata = new SuitDesignMetadata
        {
            version = PluginInfo.Version,
            name = safeName,
            baseSuitName = state.SuitName,
            sourceSuitId = suitId,
            textureFile = textureName,
            width = state.EditableTexture.width,
            height = state.EditableTexture.height,
            createdUtc = now,
            updatedUtc = now
        };

        File.WriteAllText(metadataPath, JsonUtility.ToJson(metadata, true));
        state.ActiveDesignName = safeName;
        ApplyEditedTexture(suitId, safeName, true);
        return true;
    }

    public bool LoadDesign(int suitId, string metadataPath)
    {
        try
        {
            var json = File.ReadAllText(metadataPath);
            var metadata = JsonUtility.FromJson<SuitDesignMetadata>(json);
            if (metadata == null || string.IsNullOrWhiteSpace(metadata.textureFile))
            {
                return false;
            }

            var texturePath = Path.Combine(DrawableSuitsPaths.Textures, metadata.textureFile);
            if (!File.Exists(texturePath))
            {
                return false;
            }

            var texture = TextureTools.LoadImageFile(texturePath, DrawableSuitsPlugin.ModConfig.MaxTextureSize.Value);
            if (texture == null)
            {
                return false;
            }

            ApplyReceivedTexture(suitId, metadata.name, texture);
            UnityEngine.Object.Destroy(texture);
            return true;
        }
        catch (Exception ex)
        {
            DrawableSuitsPlugin.ModLogger.LogWarning($"Failed to load design '{metadataPath}': {ex.Message}");
            return false;
        }
    }

    public void ApplyStateToWorld(SuitTextureState state)
    {
        if (state == null)
        {
            return;
        }

        var item = GetUnlockableItem(state.SuitId);
        if (item != null)
        {
            item.suitMaterial = state.RuntimeMaterial;
        }

        foreach (var suit in FindObjectsOfType<UnlockableSuit>(true))
        {
            if (suit == null || suit.suitID != state.SuitId)
            {
                continue;
            }

            suit.suitMaterial = state.RuntimeMaterial;
            if (suit.suitRenderer != null)
            {
                suit.suitRenderer.sharedMaterial = state.RuntimeMaterial;
            }
        }

        var round = StartOfRound.Instance;
        if (round?.allPlayerScripts != null)
        {
            foreach (var player in round.allPlayerScripts)
            {
                ApplyToPlayer(player, state);
            }
        }
    }

    public void ApplyToPlayer(PlayerControllerB player)
    {
        if (player == null || !_states.TryGetValue(player.currentSuitID, out var state))
        {
            return;
        }

        ApplyToPlayer(player, state);
    }

    private static void ApplyToPlayer(PlayerControllerB player, SuitTextureState state)
    {
        if (player == null || state == null || player.currentSuitID != state.SuitId)
        {
            return;
        }

        SetRendererMaterial(player.thisPlayerModel, state.RuntimeMaterial);
        SetRendererMaterial(player.thisPlayerModelLOD1, state.RuntimeMaterial);
        SetRendererMaterial(player.thisPlayerModelLOD2, state.RuntimeMaterial);
        SetRendererMaterial(player.thisPlayerModelArms, state.RuntimeMaterial);
    }

    private static void SetRendererMaterial(Renderer renderer, Material material)
    {
        if (renderer != null && material != null)
        {
            renderer.sharedMaterial = material;
        }
    }

    private static UnlockableItem GetUnlockableItem(int suitId)
    {
        var round = StartOfRound.Instance;
        var list = round?.unlockablesList?.unlockables;
        if (list == null || suitId < 0 || suitId >= list.Count)
        {
            return null;
        }

        return list[suitId];
    }
}
