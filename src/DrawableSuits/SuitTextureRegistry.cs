using System;
using System.Collections.Generic;
using System.IO;
using GameNetcodeStuff;
using Unity.Netcode;
using UnityEngine;

namespace DrawableSuits;

internal sealed class SuitTextureRegistry : MonoBehaviour
{
    private readonly Dictionary<int, SuitTextureState> _states = new();
    private readonly Dictionary<string, SuitTextureState> _playerStates = new(StringComparer.Ordinal);
    private readonly Dictionary<Renderer, Material[]> _localRendererOriginalMaterials = new();

    public IReadOnlyDictionary<int, SuitTextureState> States => _states;
    public IEnumerable<SuitTextureState> ActivePlayerStates => _playerStates.Values;

    public SuitTextureState GetOrCreateState(int suitId)
    {
        var localPlayer = StartOfRound.Instance?.localPlayerController;
        if (localPlayer != null)
        {
            return GetOrCreateStateForPlayer(localPlayer, suitId);
        }

        return GetOrCreateSharedState(suitId);
    }

    private SuitTextureState GetOrCreateSharedState(int suitId)
    {
        if (_states.TryGetValue(suitId, out var state))
        {
            return state;
        }

        state = CreateState(suitId, 0UL, false, "shared");
        if (state != null)
        {
            _states[suitId] = state;
        }

        return state;
    }

    public SuitTextureState GetOrCreateStateForPlayer(PlayerControllerB player, int suitId)
    {
        if (player == null)
        {
            return GetOrCreateSharedState(suitId);
        }

        return GetOrCreateStateForClient(GetPlayerClientId(player), suitId);
    }

    public SuitTextureState GetOrCreateStateForClient(ulong ownerClientId, int suitId)
    {
        var key = PlayerStateKey(ownerClientId, suitId);
        if (_playerStates.TryGetValue(key, out var state))
        {
            return state;
        }

        state = CreateState(suitId, ownerClientId, true, $"client {ownerClientId}");
        if (state != null)
        {
            _playerStates[key] = state;
        }

        return state;
    }

    private static string PlayerStateKey(ulong ownerClientId, int suitId)
    {
        return $"{ownerClientId}:{suitId}";
    }

    private SuitTextureState CreateState(int suitId, ulong ownerClientId, bool playerSpecific, string context)
    {
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
            name = playerSpecific
                ? $"{item.suitMaterial.name} (DrawableSuits client {ownerClientId})"
                : $"{item.suitMaterial.name} (DrawableSuits shared)",
            mainTexture = editableTexture
        };

        var state = new SuitTextureState
        {
            SuitId = suitId,
            OwnerClientId = ownerClientId,
            IsPlayerSpecific = playerSpecific,
            SuitName = string.IsNullOrWhiteSpace(item.unlockableName) ? $"Suit {suitId}" : item.unlockableName,
            OriginalMaterial = item.suitMaterial,
            OriginalTexture = originalTexture,
            RuntimeMaterial = material,
            BaseTexture = baseTexture,
            EditableTexture = editableTexture,
            ActiveDesignName = string.Empty
        };

        DrawableSuitsDiagnostics.Info($"Created texture state. context={context}; suitId={suitId}; ownerClientId={ownerClientId}; playerSpecific={playerSpecific}; material={material.name}; texture={editableTexture.name} {editableTexture.width}x{editableTexture.height}");
        return state;
    }

    public Texture2D GetEditableTexture(int suitId)
    {
        return GetOrCreateState(suitId)?.EditableTexture;
    }

    public Texture2D GetEditableTextureForPlayer(PlayerControllerB player, int suitId)
    {
        return GetOrCreateStateForPlayer(player, suitId)?.EditableTexture;
    }

    public Material GetRuntimeMaterial(int suitId)
    {
        return GetOrCreateState(suitId)?.RuntimeMaterial;
    }

    public Material GetRuntimeMaterialForPlayer(PlayerControllerB player, int suitId)
    {
        return GetOrCreateStateForPlayer(player, suitId)?.RuntimeMaterial;
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
        ApplyStateToOwner(state);

        if (broadcast)
        {
            DrawableSuitsPlugin.Sync.BroadcastDesign(state);
        }
    }

    public void ApplyReceivedTexture(int suitId, string designName, Texture2D texture)
    {
        ApplyReceivedTexture(ResolveLocalClientId(), suitId, designName, texture);
    }

    public void ApplyReceivedTexture(ulong ownerClientId, int suitId, string designName, Texture2D texture)
    {
        var state = GetOrCreateStateForClient(ownerClientId, suitId);
        if (state == null || texture == null)
        {
            return;
        }

        TextureTools.CopyInto(state.EditableTexture, texture);
        state.RuntimeMaterial.mainTexture = state.EditableTexture;
        state.ActiveDesignName = designName ?? "Received design";
        ApplyStateToOwner(state);
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
        ApplyStateToOwner(state);
    }

    public void ReapplyAll()
    {
        var round = StartOfRound.Instance;
        if (round?.allPlayerScripts == null)
        {
            return;
        }

        foreach (var player in round.allPlayerScripts)
        {
            ApplyToPlayer(player);
        }
    }

    public void ReapplyAllIfReady(string context)
    {
        if (!CanReapplyAll(out var reason))
        {
            DrawableSuitsDiagnostics.Info($"ReapplyAll skipped. context={context}; reason={reason}; knownStates={_states.Count}");
            return;
        }

        ReapplyAll();
    }

    private static bool CanReapplyAll(out string reason)
    {
        var round = StartOfRound.Instance;
        if (round == null)
        {
            reason = "StartOfRound.Instance=null";
            return false;
        }

        var unlockables = round.unlockablesList?.unlockables;
        if (unlockables == null || unlockables.Count == 0)
        {
            reason = "unlockables not ready";
            return false;
        }

        if (round.allPlayerScripts == null || round.allPlayerScripts.Length == 0)
        {
            reason = "players not ready";
            return false;
        }

        reason = "ready";
        return true;
    }

    public void RefreshKnownSuitMaterials()
    {
        DrawableSuitsDiagnostics.Info("RefreshKnownSuitMaterials skipped: DrawableSuits 0.4.4 keeps active edits player-specific and no longer mutates UnlockableItem.suitMaterial.");
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

    public bool TryExportDesignCode(int suitId, string designName, out string code, out DrawableSuitDesignCode.CodeInfo info, out string failureReason)
    {
        var state = GetOrCreateState(suitId);
        if (state == null)
        {
            code = string.Empty;
            info = default;
            failureReason = "Selected suit is not editable.";
            return false;
        }

        return DrawableSuitDesignCode.TryExport(state, designName, out code, out info, out failureReason);
    }

    public bool ImportDecodedDesignCode(int suitId, DrawableSuitDesignCode.Payload payload, Texture2D texture, out string importedDesignName, out string failureReason)
    {
        importedDesignName = string.Empty;
        failureReason = string.Empty;

        var state = GetOrCreateState(suitId);
        if (state == null)
        {
            failureReason = "Selected suit is not editable.";
            return false;
        }

        if (payload == null)
        {
            failureReason = "Design code payload is missing.";
            return false;
        }

        if (texture == null)
        {
            failureReason = "Design code texture is missing.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(payload.designName))
        {
            failureReason = "Design code is missing a design name.";
            return false;
        }

        importedDesignName = TextureTools.SanitizeFileName(payload.designName);
        TextureTools.CopyInto(state.EditableTexture, texture);
        state.RuntimeMaterial.mainTexture = state.EditableTexture;
        state.ActiveDesignName = importedDesignName;
        ApplyStateToOwner(state);
        return true;
    }

    public void ApplyStateToWorld(SuitTextureState state)
    {
        ApplyStateToOwner(state);
    }

    public void ApplyStateToOwner(SuitTextureState state)
    {
        if (state == null)
        {
            return;
        }

        var round = StartOfRound.Instance;
        if (round?.allPlayerScripts != null)
        {
            foreach (var player in round.allPlayerScripts)
            {
                if (player != null
                    && player.currentSuitID == state.SuitId
                    && (!state.IsPlayerSpecific || GetPlayerClientId(player) == state.OwnerClientId))
                {
                    ApplyToPlayer(player, state);
                }
            }
        }
    }

    public void ApplyToPlayer(PlayerControllerB player)
    {
        if (player == null)
        {
            return;
        }

        var key = PlayerStateKey(GetPlayerClientId(player), player.currentSuitID);
        if (!_playerStates.TryGetValue(key, out var state))
        {
            if (!_states.TryGetValue(player.currentSuitID, out state))
            {
                return;
            }
        }

        ApplyToPlayer(player, state);
    }

    private void ApplyToPlayer(PlayerControllerB player, SuitTextureState state)
    {
        if (player == null || state == null || player.currentSuitID != state.SuitId)
        {
            return;
        }

        var localPlayer = StartOfRound.Instance?.localPlayerController;
        var isLocal = localPlayer != null && ReferenceEquals(player, localPlayer);
        if (isLocal && !DrawableSuitsPlugin.IsEditorOpen)
        {
            RestoreLocalPlayerMaterials("ApplyToPlayer skipped local closed editor");
            return;
        }

        if (isLocal)
        {
            SetLocalRendererMaterial(player.thisPlayerModel, state.RuntimeMaterial);
            SetLocalRendererMaterial(player.thisPlayerModelLOD1, state.RuntimeMaterial);
            SetLocalRendererMaterial(player.thisPlayerModelLOD2, state.RuntimeMaterial);
            if (DrawableSuitsPlugin.ModConfig.ApplyLocalFirstPersonArms.Value)
            {
                SetLocalRendererMaterial(player.thisPlayerModelArms, state.RuntimeMaterial);
            }
            else
            {
                RestoreRendererToOriginal(player.thisPlayerModelArms, state.OriginalMaterial, "local first-person arms config disabled");
            }
            return;
        }

        SetRendererMaterial(player.thisPlayerModel, state.RuntimeMaterial);
        SetRendererMaterial(player.thisPlayerModelLOD1, state.RuntimeMaterial);
        SetRendererMaterial(player.thisPlayerModelLOD2, state.RuntimeMaterial);
    }

    public int RestoreLocalPlayerMaterials(string reason)
    {
        var restored = 0;
        if (_localRendererOriginalMaterials.Count > 0)
        {
            foreach (var pair in _localRendererOriginalMaterials)
            {
                var renderer = pair.Key;
                var materials = pair.Value;
                if (renderer == null || materials == null || materials.Length == 0)
                {
                    continue;
                }

                renderer.sharedMaterials = CloneMaterials(materials);
                restored++;
            }
            _localRendererOriginalMaterials.Clear();
        }

        var player = StartOfRound.Instance?.localPlayerController;
        var state = player != null ? GetOrCreateStateForPlayer(player, player.currentSuitID) : null;
        if (player != null && state?.OriginalMaterial != null)
        {
            restored += RestoreDrawableRendererFallback(player.thisPlayerModel, state.OriginalMaterial, reason);
            restored += RestoreDrawableRendererFallback(player.thisPlayerModelLOD1, state.OriginalMaterial, reason);
            restored += RestoreDrawableRendererFallback(player.thisPlayerModelLOD2, state.OriginalMaterial, reason);
            restored += RestoreDrawableRendererFallback(player.thisPlayerModelArms, state.OriginalMaterial, reason);
        }

        if (restored > 0)
        {
            DrawableSuitsDiagnostics.Info($"Restored local player materials. reason={reason}; restored={restored}; applyLocalFirstPersonArms={DrawableSuitsPlugin.ModConfig.ApplyLocalFirstPersonArms.Value}");
        }

        return restored;
    }

    private void SetLocalRendererMaterial(Renderer renderer, Material material)
    {
        if (renderer == null || material == null)
        {
            return;
        }

        if (!_localRendererOriginalMaterials.ContainsKey(renderer))
        {
            _localRendererOriginalMaterials[renderer] = CloneMaterials(renderer.sharedMaterials);
        }

        SetRendererMaterial(renderer, material);
    }

    private void RestoreRendererToOriginal(Renderer renderer, Material fallbackMaterial, string reason)
    {
        if (renderer == null)
        {
            return;
        }

        if (_localRendererOriginalMaterials.TryGetValue(renderer, out var originalMaterials) && originalMaterials != null && originalMaterials.Length > 0)
        {
            renderer.sharedMaterials = CloneMaterials(originalMaterials);
            _localRendererOriginalMaterials.Remove(renderer);
            DrawableSuitsDiagnostics.Info($"Restored local renderer material. reason={reason}; renderer={renderer.name}");
            return;
        }

        RestoreDrawableRendererFallback(renderer, fallbackMaterial, reason);
    }

    private static int RestoreDrawableRendererFallback(Renderer renderer, Material fallbackMaterial, string reason)
    {
        if (renderer == null || fallbackMaterial == null || !RendererUsesDrawableMaterial(renderer))
        {
            return 0;
        }

        renderer.sharedMaterial = fallbackMaterial;
        DrawableSuitsDiagnostics.Info($"Restored DrawableSuits material fallback. reason={reason}; renderer={renderer.name}; material={fallbackMaterial.name}");
        return 1;
    }

    private static bool RendererUsesDrawableMaterial(Renderer renderer)
    {
        if (renderer?.sharedMaterials == null)
        {
            return false;
        }

        var materials = renderer.sharedMaterials;
        for (var i = 0; i < materials.Length; i++)
        {
            var materialName = materials[i]?.name ?? string.Empty;
            if (materialName.IndexOf("DrawableSuits", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static Material[] CloneMaterials(Material[] materials)
    {
        if (materials == null)
        {
            return Array.Empty<Material>();
        }

        var clone = new Material[materials.Length];
        Array.Copy(materials, clone, materials.Length);
        return clone;
    }

    private static void SetRendererMaterial(Renderer renderer, Material material)
    {
        if (renderer != null && material != null)
        {
            renderer.sharedMaterial = material;
        }
    }

    public static ulong GetPlayerClientId(PlayerControllerB player)
    {
        if (player == null)
        {
            return ResolveLocalClientId();
        }

        if (player.actualClientId != 0UL || player.playerClientId == 0UL)
        {
            return player.actualClientId;
        }

        return player.playerClientId;
    }

    private static ulong ResolveLocalClientId()
    {
        var manager = NetworkManager.Singleton;
        if (manager != null && manager.IsClient)
        {
            return manager.LocalClientId;
        }

        var player = StartOfRound.Instance?.localPlayerController;
        return player != null ? player.actualClientId : 0UL;
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
