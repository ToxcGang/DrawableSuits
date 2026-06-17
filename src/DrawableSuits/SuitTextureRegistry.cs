using System;
using System.Collections.Generic;
using System.IO;
using GameNetcodeStuff;
using Unity.Netcode;
using UnityEngine;

namespace DrawableSuits;

internal sealed class SuitTextureRegistry : MonoBehaviour
{
    public const int UnknownPlayerSlot = -1;
    private const int ReapplyRetryAttempts = 8;
    private const float ReapplyRetryIntervalSeconds = 0.25f;

    private readonly Dictionary<int, SuitTextureState> _states = new();
    private readonly Dictionary<string, SuitTextureState> _playerStates = new(StringComparer.Ordinal);
    private readonly Dictionary<Renderer, Material[]> _localRendererOriginalMaterials = new();
    private int _pendingReapplyAllAttempts;
    private float _nextPendingReapplyAllTime;
    private string _pendingReapplyAllContext = string.Empty;
    private readonly List<DeadBodyReapplyRequest> _pendingDeadBodyReapplications = new();
    private readonly Dictionary<int, int> _lastKnownSuitIdByPlayerSlot = new();
    private readonly Dictionary<ulong, int> _lastKnownSuitIdByClientId = new();

    private sealed class DeadBodyReapplyRequest
    {
        public DeadBodyInfo Body;
        public int Attempts;
        public float NextTime;
        public string Context;
    }

    public IReadOnlyDictionary<int, SuitTextureState> States => _states;
    public IEnumerable<SuitTextureState> ActivePlayerStates => _playerStates.Values;

    private void Update()
    {
        ProcessPendingReapplyAll();
        ProcessPendingDeadBodyReapplications();
    }

    private void ProcessPendingReapplyAll()
    {
        if (_pendingReapplyAllAttempts <= 0 || Time.unscaledTime < _nextPendingReapplyAllTime)
        {
            return;
        }

        _pendingReapplyAllAttempts--;
        _nextPendingReapplyAllTime = Time.unscaledTime + ReapplyRetryIntervalSeconds;
        if (!CanReapplyAll(out var reason))
        {
            DrawableSuitsDiagnostics.Info($"SyncReapplySkipped: context={_pendingReapplyAllContext}; reason={reason}; attemptsRemaining={_pendingReapplyAllAttempts}");
            return;
        }

        ReapplyAll();
        DrawableSuitsDiagnostics.Info($"SyncReapplyApplied: context={_pendingReapplyAllContext}; attemptsRemaining={_pendingReapplyAllAttempts}; playerStates={_playerStates.Count}; sharedStates={_states.Count}");
    }

    private void ProcessPendingDeadBodyReapplications()
    {
        if (_pendingDeadBodyReapplications.Count == 0)
        {
            return;
        }

        for (var i = _pendingDeadBodyReapplications.Count - 1; i >= 0; i--)
        {
            var request = _pendingDeadBodyReapplications[i];
            if (request == null || request.Body == null)
            {
                _pendingDeadBodyReapplications.RemoveAt(i);
                continue;
            }

            if (Time.unscaledTime < request.NextTime)
            {
                continue;
            }

            request.Attempts--;
            ApplyToDeadBody(request.Body, $"retry:{request.Context}");
            if (request.Attempts <= 0)
            {
                _pendingDeadBodyReapplications.RemoveAt(i);
                continue;
            }

            request.NextTime = Time.unscaledTime + ReapplyRetryIntervalSeconds;
        }
    }

    public SuitTextureState GetOrCreateState(int suitId)
    {
        var localPlayer = StartOfRound.Instance?.localPlayerController;
        if (localPlayer != null)
        {
            return GetOrCreateLocalState(suitId);
        }

        return GetOrCreateSharedState(suitId);
    }

    private SuitTextureState GetOrCreateLocalState(int suitId)
    {
        var ownerClientId = ResolveLocalClientId();
        var ownerPlayerSlot = ResolveLocalPlayerSlot();
        var key = PlayerStateKey(ownerClientId, ownerPlayerSlot, suitId);
        if (_playerStates.TryGetValue(key, out var state))
        {
            state.IsLocalPlayerState = true;
            return state;
        }

        var migrated = TryMigrateLocalState(suitId, ownerClientId, ownerPlayerSlot, "GetOrCreateLocalState");
        if (migrated != null)
        {
            return migrated;
        }

        state = CreateState(suitId, ownerClientId, ownerPlayerSlot, true, $"local client {ownerClientId} slot {ownerPlayerSlot}", true);
        if (state != null)
        {
            _playerStates[key] = state;
        }

        return state;
    }

    private SuitTextureState GetOrCreateSharedState(int suitId)
    {
        if (_states.TryGetValue(suitId, out var state))
        {
            return state;
        }

        state = CreateState(suitId, 0UL, UnknownPlayerSlot, false, "shared");
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

        var localPlayer = StartOfRound.Instance?.localPlayerController;
        if (localPlayer != null && ReferenceEquals(player, localPlayer))
        {
            return GetOrCreateLocalState(suitId);
        }

        return GetOrCreateStateForPlayerIdentity(GetPlayerClientId(player), GetPlayerSlot(player), suitId);
    }

    public SuitTextureState GetOrCreateStateForClient(ulong ownerClientId, int suitId)
    {
        return GetOrCreateStateForPlayerIdentity(ownerClientId, ResolvePlayerSlotForClient(ownerClientId), suitId);
    }

    public SuitTextureState GetOrCreateStateForPlayerIdentity(ulong ownerClientId, int ownerPlayerSlot, int suitId)
    {
        var normalizedSlot = NormalizePlayerSlot(ownerPlayerSlot);
        var key = PlayerStateKey(ownerClientId, normalizedSlot, suitId);
        if (_playerStates.TryGetValue(key, out var state))
        {
            if (IsOwnerLocal(ownerClientId))
            {
                state.IsLocalPlayerState = true;
            }

            state.OwnerPlayerSlot = normalizedSlot;

            return state;
        }

        state = CreateState(suitId, ownerClientId, normalizedSlot, true, $"client {ownerClientId} slot {normalizedSlot}", IsOwnerLocal(ownerClientId));
        if (state != null)
        {
            _playerStates[key] = state;
        }

        return state;
    }

    private static string PlayerStateKey(ulong ownerClientId, int ownerPlayerSlot, int suitId)
    {
        return ownerPlayerSlot >= 0
            ? $"slot:{ownerPlayerSlot}:{suitId}"
            : $"client:{ownerClientId}:{suitId}";
    }

    private SuitTextureState CreateState(int suitId, ulong ownerClientId, int ownerPlayerSlot, bool playerSpecific, string context, bool localPlayerState = false)
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
            OwnerPlayerSlot = NormalizePlayerSlot(ownerPlayerSlot),
            IsPlayerSpecific = playerSpecific,
            IsLocalPlayerState = localPlayerState,
            SuitName = string.IsNullOrWhiteSpace(item.unlockableName) ? $"Suit {suitId}" : item.unlockableName,
            OriginalMaterial = item.suitMaterial,
            OriginalTexture = originalTexture,
            RuntimeMaterial = material,
            BaseTexture = baseTexture,
            EditableTexture = editableTexture,
            ActiveDesignName = string.Empty
        };

        DrawableSuitsDiagnostics.Info($"Created texture state. context={context}; suitId={suitId}; ownerClientId={ownerClientId}; ownerPlayerSlot={state.OwnerPlayerSlot}; playerSpecific={playerSpecific}; localPlayerState={localPlayerState}; material={material.name}; texture={editableTexture.name} {editableTexture.width}x{editableTexture.height}");
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

        state = NormalizeLocalStateOwner(state, "ApplyEditedTexture");
        if (state == null)
        {
            return;
        }

        state.RuntimeMaterial.mainTexture = state.EditableTexture;
        state.ActiveDesignName = designName ?? string.Empty;
        ApplyStateToOwner(state);
        ReapplyDeadBodies($"ApplyEditedTexture suit={suitId}");

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
        ApplyReceivedTexture(ownerClientId, ResolvePlayerSlotForClient(ownerClientId), suitId, designName, texture);
    }

    public void ApplyReceivedTexture(ulong ownerClientId, int ownerPlayerSlot, int suitId, string designName, Texture2D texture)
    {
        var normalizedSlot = NormalizePlayerSlot(ownerPlayerSlot);
        var state = GetOrCreateStateForPlayerIdentity(ownerClientId, normalizedSlot, suitId);
        if (state == null || texture == null)
        {
            return;
        }

        TextureTools.CopyInto(state.EditableTexture, texture);
        state.RuntimeMaterial.mainTexture = state.EditableTexture;
        state.ActiveDesignName = designName ?? "Received design";
        RecordSuitSnapshot(ownerClientId, normalizedSlot, suitId, "ApplyReceivedTexture", $"ownerClientId={ownerClientId}; ownerPlayerSlot={normalizedSlot}; suitId={suitId}");
        ApplyStateToOwner(state);
        ReapplyDeadBodies($"ApplyReceivedTexture owner={ownerClientId} suit={suitId}");
        ScheduleReapplyAll($"ApplyReceivedTexture owner={ownerClientId} suit={suitId}");
        DrawableSuitsDiagnostics.Info($"SyncPayloadApplied: ownerClientId={ownerClientId}; ownerPlayerSlot={normalizedSlot}; suitId={suitId}; designName={state.ActiveDesignName}; texture={state.EditableTexture.width}x{state.EditableTexture.height}; localPlayerState={state.IsLocalPlayerState}");
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
        ReapplyDeadBodies($"ResetSuit suit={suitId}");
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

        ReapplyDeadBodies("ReapplyAll", false);
    }

    public void ReapplyAllIfReady(string context)
    {
        if (!CanReapplyAll(out var reason))
        {
            DrawableSuitsDiagnostics.Info($"ReapplyAll skipped. context={context}; reason={reason}; knownStates={_states.Count}");
            return;
        }

        ReapplyAll();
        ScheduleReapplyAll(context);
    }

    public void ScheduleReapplyAll(string context)
    {
        _pendingReapplyAllAttempts = Mathf.Max(_pendingReapplyAllAttempts, ReapplyRetryAttempts);
        if (_nextPendingReapplyAllTime <= 0f || Time.unscaledTime + 0.05f < _nextPendingReapplyAllTime)
        {
            _nextPendingReapplyAllTime = Time.unscaledTime + 0.05f;
        }

        _pendingReapplyAllContext = context ?? string.Empty;
        DrawableSuitsDiagnostics.Info($"SyncReapplyScheduled: context={_pendingReapplyAllContext}; attempts={_pendingReapplyAllAttempts}; nextIn={Mathf.Max(0f, _nextPendingReapplyAllTime - Time.unscaledTime):0.###}; playerStates={_playerStates.Count}; sharedStates={_states.Count}");
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
        state = NormalizeLocalStateOwner(state, "ApplyStateToWorld");
        ApplyStateToOwner(state);
    }

    public void ApplyStateToOwner(SuitTextureState state)
    {
        if (state == null)
        {
            return;
        }

        RecordStateSuitSnapshot(state, "ApplyStateToOwner");

        var round = StartOfRound.Instance;
        if (round?.allPlayerScripts != null)
        {
            foreach (var player in round.allPlayerScripts)
            {
                if (player != null
                    && player.currentSuitID == state.SuitId
                    && (!state.IsPlayerSpecific || PlayerMatchesStateOwner(player, state)))
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

        RecordPlayerSuitSnapshot(player, "ApplyToPlayer");

        var slotKey = PlayerStateKey(GetPlayerClientId(player), GetPlayerSlot(player), player.currentSuitID);
        if (!_playerStates.TryGetValue(slotKey, out var state))
        {
            var clientKey = PlayerStateKey(GetPlayerClientId(player), UnknownPlayerSlot, player.currentSuitID);
            if (!_playerStates.TryGetValue(clientKey, out state) && !_states.TryGetValue(player.currentSuitID, out state))
            {
                return;
            }
        }

        ApplyToPlayer(player, state);
    }

    public void RecordPlayerSuitSnapshot(PlayerControllerB player, string context)
    {
        if (player == null)
        {
            return;
        }

        RecordSuitSnapshot(GetPlayerClientId(player), GetPlayerSlot(player), player.currentSuitID, context, DescribePlayerIdentity(player));
    }

    private void RecordStateSuitSnapshot(SuitTextureState state, string context)
    {
        if (state == null)
        {
            return;
        }

        RecordSuitSnapshot(state.OwnerClientId, state.OwnerPlayerSlot, state.SuitId, context, $"ownerClientId={state.OwnerClientId}; ownerPlayerSlot={state.OwnerPlayerSlot}; suitId={state.SuitId}; state={state.ActiveDesignName}");
    }

    private void RecordSuitSnapshot(ulong ownerClientId, int ownerPlayerSlot, int suitId, string context, string identity)
    {
        if (suitId < 0)
        {
            return;
        }

        var normalizedSlot = NormalizePlayerSlot(ownerPlayerSlot);
        var changed = false;
        if (normalizedSlot >= 0
            && (!_lastKnownSuitIdByPlayerSlot.TryGetValue(normalizedSlot, out var previousSlotSuit) || previousSlotSuit != suitId))
        {
            _lastKnownSuitIdByPlayerSlot[normalizedSlot] = suitId;
            changed = true;
        }

        if (!_lastKnownSuitIdByClientId.TryGetValue(ownerClientId, out var previousClientSuit) || previousClientSuit != suitId)
        {
            _lastKnownSuitIdByClientId[ownerClientId] = suitId;
            changed = true;
        }

        if (changed)
        {
            DrawableSuitsDiagnostics.Info($"DeadBodySuitIdSnapshot: context={context}; ownerClientId={ownerClientId}; ownerPlayerSlot={normalizedSlot}; suitId={suitId}; identity={identity}");
        }
    }

    public void ApplyToDeadBody(DeadBodyInfo body, string context)
    {
        if (!TryResolveDeadBodyState(body, context, out var player, out var playerSlot, out var suitId, out var state, out var resolveSummary))
        {
            return;
        }

        state.RuntimeMaterial.mainTexture = state.EditableTexture;
        var rendererCount = ApplyMaterialToDeadBodyRenderers(body, state.RuntimeMaterial, out var candidateCount, out var rendererSummary);
        if (rendererCount <= 0)
        {
            DrawableSuitsDiagnostics.Info($"DeadBodySuitApplySkipped: context={context}; reason=no-compatible-renderers; {DescribePlayerIdentity(player)}; playerObjectId={body.playerObjectId}; resolvedSlot={playerSlot}; suitId={suitId}; resolve={resolveSummary}; candidates={candidateCount}; material={state.RuntimeMaterial.name}; body={body.name}; renderers=[{rendererSummary}]");
            return;
        }

        DrawableSuitsDiagnostics.Info($"DeadBodySuitApplied: context={context}; {DescribePlayerIdentity(player)}; ownerClientId={state.OwnerClientId}; ownerPlayerSlot={state.OwnerPlayerSlot}; playerObjectId={body.playerObjectId}; resolvedSlot={playerSlot}; suitId={suitId}; resolve={resolveSummary}; renderers={rendererCount}; candidates={candidateCount}; material={state.RuntimeMaterial.name}; designName={state.ActiveDesignName}; body={body.name}; renderers=[{rendererSummary}]");
    }

    public bool TryGetDeadBodyRuntimeMaterial(DeadBodyInfo body, string context, out Material material, out string reason)
    {
        material = null;
        if (!TryResolveDeadBodyState(body, context, out _, out _, out _, out var state, out reason))
        {
            return false;
        }

        material = state.RuntimeMaterial;
        material.mainTexture = state.EditableTexture;
        return material != null;
    }

    public void ScheduleDeadBodyReapply(DeadBodyInfo body, string context)
    {
        if (body == null)
        {
            DrawableSuitsDiagnostics.Info($"DeadBodySuitReapplySkipped: context={context}; reason=body-null");
            return;
        }

        DeadBodyReapplyRequest request = null;
        for (var i = 0; i < _pendingDeadBodyReapplications.Count; i++)
        {
            if (_pendingDeadBodyReapplications[i]?.Body == body)
            {
                request = _pendingDeadBodyReapplications[i];
                break;
            }
        }

        if (request == null)
        {
            request = new DeadBodyReapplyRequest { Body = body };
            _pendingDeadBodyReapplications.Add(request);
        }

        request.Attempts = Mathf.Max(request.Attempts, ReapplyRetryAttempts);
        request.NextTime = Time.unscaledTime + 0.05f;
        request.Context = context ?? string.Empty;
        DrawableSuitsDiagnostics.Info($"DeadBodySuitReapplyScheduled: context={request.Context}; attempts={request.Attempts}; nextIn={Mathf.Max(0f, request.NextTime - Time.unscaledTime):0.###}; playerObjectId={body.playerObjectId}; body={body.name}");
    }

    public void ReapplyDeadBodies(string context, bool scheduleRetries = true)
    {
        var bodies = UnityEngine.Object.FindObjectsOfType<DeadBodyInfo>();
        var found = bodies?.Length ?? 0;
        var scheduled = 0;
        if (bodies != null)
        {
            for (var i = 0; i < bodies.Length; i++)
            {
                var body = bodies[i];
                if (body == null)
                {
                    continue;
                }

                ApplyToDeadBody(body, context);
                if (scheduleRetries)
                {
                    ScheduleDeadBodyReapply(body, context);
                    scheduled++;
                }
            }
        }

        if (found > 0)
        {
            DrawableSuitsDiagnostics.Info($"DeadBodySuitReapplyExisting: context={context}; found={found}; scheduled={scheduled}; scheduleRetries={scheduleRetries}");
        }
    }

    private SuitTextureState FindStateForPlayer(PlayerControllerB player, int suitId)
    {
        if (player == null)
        {
            return _states.TryGetValue(suitId, out var sharedState) ? sharedState : null;
        }

        var slotKey = PlayerStateKey(GetPlayerClientId(player), GetPlayerSlot(player), suitId);
        if (_playerStates.TryGetValue(slotKey, out var state))
        {
            return state;
        }

        var clientKey = PlayerStateKey(GetPlayerClientId(player), UnknownPlayerSlot, suitId);
        if (_playerStates.TryGetValue(clientKey, out state))
        {
            return state;
        }

        if (_states.TryGetValue(suitId, out state))
        {
            return state;
        }

        return GetOrCreateStateForPlayer(player, suitId);
    }

    private bool TryResolveDeadBodyState(DeadBodyInfo body, string context, out PlayerControllerB player, out int playerSlot, out int suitId, out SuitTextureState state, out string resolveSummary)
    {
        player = null;
        playerSlot = UnknownPlayerSlot;
        suitId = -1;
        state = null;
        resolveSummary = string.Empty;

        if (body == null)
        {
            resolveSummary = "body-null";
            DrawableSuitsDiagnostics.Info($"DeadBodySuitApplySkipped: context={context}; reason=body-null");
            return false;
        }

        player = ResolveDeadBodyPlayer(body, out playerSlot, out var playerResolveReason);
        if (player == null)
        {
            resolveSummary = $"playerResolve={playerResolveReason}";
            DrawableSuitsDiagnostics.Info($"DeadBodySuitApplySkipped: context={context}; reason=no-player; playerObjectId={body.playerObjectId}; resolvedSlot={playerSlot}; resolve={resolveSummary}; body={body.name}");
            return false;
        }

        suitId = ResolveDeadBodySuitId(player, playerSlot, out var suitResolveReason);
        resolveSummary = $"playerResolve={playerResolveReason}; suitResolve={suitResolveReason}";
        if (suitId < 0)
        {
            DrawableSuitsDiagnostics.Info($"DeadBodySuitApplySkipped: context={context}; reason=invalid-suit; {DescribePlayerIdentity(player)}; playerObjectId={body.playerObjectId}; resolvedSlot={playerSlot}; resolve={resolveSummary}; body={body.name}");
            return false;
        }

        state = FindStateForPlayer(player, suitId);
        if (state?.RuntimeMaterial == null)
        {
            DrawableSuitsDiagnostics.Info($"DeadBodySuitApplySkipped: context={context}; reason=no-state; {DescribePlayerIdentity(player)}; playerObjectId={body.playerObjectId}; resolvedSlot={playerSlot}; suitId={suitId}; resolve={resolveSummary}; body={body.name}");
            return false;
        }

        return true;
    }

    private int ResolveDeadBodySuitId(PlayerControllerB player, int playerSlot, out string source)
    {
        var normalizedSlot = NormalizePlayerSlot(playerSlot);
        if (normalizedSlot >= 0 && _lastKnownSuitIdByPlayerSlot.TryGetValue(normalizedSlot, out var suitId) && suitId >= 0)
        {
            source = $"slot-snapshot:{normalizedSlot}";
            return suitId;
        }

        var clientId = GetPlayerClientId(player);
        if (_lastKnownSuitIdByClientId.TryGetValue(clientId, out suitId) && suitId >= 0)
        {
            source = $"client-snapshot:{clientId}";
            return suitId;
        }

        if (player != null && player.currentSuitID >= 0)
        {
            source = "player.currentSuitID";
            return player.currentSuitID;
        }

        source = "none";
        return -1;
    }

    private static PlayerControllerB ResolveDeadBodyPlayer(DeadBodyInfo body, out int playerSlot, out string reason)
    {
        playerSlot = UnknownPlayerSlot;
        if (body == null)
        {
            reason = "body-null";
            return null;
        }

        if (body.playerScript != null)
        {
            playerSlot = GetPlayerSlot(body.playerScript);
            reason = "playerScript";
            return body.playerScript;
        }

        var round = StartOfRound.Instance;
        var players = round?.allPlayerScripts;
        if (players != null && body.playerObjectId >= 0 && body.playerObjectId < players.Length && players[body.playerObjectId] != null)
        {
            playerSlot = body.playerObjectId;
            reason = "playerObjectId";
            return players[body.playerObjectId];
        }

        var ragdoll = body.GetComponentInParent<RagdollGrabbableObject>();
        if (players != null && ragdoll != null && ragdoll.bodyID >= 0 && ragdoll.bodyID < players.Length && players[ragdoll.bodyID] != null)
        {
            playerSlot = ragdoll.bodyID;
            reason = "ragdoll.bodyID";
            return players[ragdoll.bodyID];
        }

        reason = $"no-match playerObjectId={body.playerObjectId}; ragdollBodyId={(ragdoll != null ? ragdoll.bodyID.ToString() : "none")}";
        return null;
    }

    private static int ApplyMaterialToDeadBodyRenderers(DeadBodyInfo body, Material material, out int candidateCount, out string rendererSummary)
    {
        candidateCount = 0;
        rendererSummary = string.Empty;
        if (body == null || material == null)
        {
            return 0;
        }

        var unique = new HashSet<Renderer>();
        var renderers = body.GetComponentsInChildren<Renderer>(true);
        if (renderers != null)
        {
            for (var i = 0; i < renderers.Length; i++)
            {
                AddDeadBodyRenderer(renderers[i], unique);
            }
        }

        var ragdoll = body.GetComponentInParent<RagdollGrabbableObject>();
        AddDeadBodyRenderer(ragdoll?.mainObjectRenderer, unique);
        AddDeadBodyRenderer(body.GetComponent<RagdollGrabbableObject>()?.mainObjectRenderer, unique);

        if (body.grabBodyObject != null)
        {
            var grabbedRagdoll = body.grabBodyObject as RagdollGrabbableObject;
            AddDeadBodyRenderer(grabbedRagdoll?.mainObjectRenderer, unique);
            var grabbedRenderers = body.grabBodyObject.GetComponentsInChildren<Renderer>(true);
            if (grabbedRenderers != null)
            {
                for (var i = 0; i < grabbedRenderers.Length; i++)
                {
                    AddDeadBodyRenderer(grabbedRenderers[i], unique);
                }
            }
        }

        if (body.detachedHeadObject != null)
        {
            var detachedHeadRenderers = body.detachedHeadObject.GetComponentsInChildren<Renderer>(true);
            if (detachedHeadRenderers != null)
            {
                for (var i = 0; i < detachedHeadRenderers.Length; i++)
                {
                    AddDeadBodyRenderer(detachedHeadRenderers[i], unique);
                }
            }
        }

        candidateCount = unique.Count;
        var applied = 0;
        var summary = new List<string>();
        foreach (var renderer in unique)
        {
            if (renderer == null)
            {
                continue;
            }

            var beforeMaterials = DescribeRendererMaterials(renderer);
            SetDeadBodyRendererMaterial(renderer, material);

            applied++;
            if (summary.Count < 8)
            {
                summary.Add($"{renderer.GetType().Name}:{GetTransformPath(renderer.transform)} before=[{beforeMaterials}] after=[{DescribeRendererMaterials(renderer)}]");
            }
        }

        rendererSummary = string.Join(", ", summary);
        if (candidateCount > summary.Count)
        {
            rendererSummary += $", +{candidateCount - summary.Count} more";
        }

        return applied;
    }

    private static void SetDeadBodyRendererMaterial(Renderer renderer, Material material)
    {
        if (renderer == null || material == null)
        {
            return;
        }

        var shared = renderer.sharedMaterials;
        if (shared == null || shared.Length == 0)
        {
            renderer.sharedMaterial = material;
        }
        else
        {
            for (var i = 0; i < shared.Length; i++)
            {
                shared[i] = material;
            }

            renderer.sharedMaterials = shared;
        }

        try
        {
            var instanceMaterials = renderer.materials;
            if (instanceMaterials != null && instanceMaterials.Length > 0)
            {
                for (var i = 0; i < instanceMaterials.Length; i++)
                {
                    instanceMaterials[i] = material;
                }

                renderer.materials = instanceMaterials;
            }
        }
        catch (Exception ex)
        {
            DrawableSuitsDiagnostics.Warn($"DeadBodySuitRendererMaterialInstanceSkipped: renderer={GetTransformPath(renderer.transform)}; reason={ex.GetType().Name}:{ex.Message}");
        }
    }

    private static string DescribeRendererMaterials(Renderer renderer)
    {
        if (renderer == null)
        {
            return "renderer-null";
        }

        var materials = renderer.sharedMaterials;
        if (materials == null || materials.Length == 0)
        {
            return renderer.sharedMaterial != null ? renderer.sharedMaterial.name : "none";
        }

        var names = new List<string>();
        for (var i = 0; i < materials.Length && i < 4; i++)
        {
            names.Add(materials[i] != null ? materials[i].name : "null");
        }

        if (materials.Length > names.Count)
        {
            names.Add($"+{materials.Length - names.Count} more");
        }

        return string.Join("|", names);
    }

    private static void AddDeadBodyRenderer(Renderer renderer, HashSet<Renderer> renderers)
    {
        if (renderer == null || renderers == null)
        {
            return;
        }

        if (renderer is MeshRenderer || renderer is SkinnedMeshRenderer)
        {
            renderers.Add(renderer);
        }
    }

    private static string GetTransformPath(Transform transform)
    {
        if (transform == null)
        {
            return "null";
        }

        var parts = new Stack<string>();
        var current = transform;
        while (current != null && parts.Count < 12)
        {
            parts.Push(current.name);
            current = current.parent;
        }

        return string.Join("/", parts);
    }

    private void ApplyToPlayer(PlayerControllerB player, SuitTextureState state)
    {
        RecordPlayerSuitSnapshot(player, "ApplyToPlayer(state)");
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

        var localPlayer = StartOfRound.Instance?.localPlayerController;
        if (localPlayer != null && ReferenceEquals(player, localPlayer))
        {
            return ResolveLocalClientId();
        }

        if (player.actualClientId != 0UL || player.playerClientId == 0UL)
        {
            return player.actualClientId;
        }

        return player.playerClientId;
    }

    public static int GetPlayerSlot(PlayerControllerB player)
    {
        if (player == null)
        {
            return UnknownPlayerSlot;
        }

        var players = StartOfRound.Instance?.allPlayerScripts;
        if (players == null)
        {
            return NormalizePlayerSlot((int)player.playerClientId);
        }

        for (var i = 0; i < players.Length; i++)
        {
            if (ReferenceEquals(players[i], player))
            {
                return i;
            }
        }

        return NormalizePlayerSlot((int)player.playerClientId);
    }

    public static int ResolveLocalPlayerSlot()
    {
        return GetPlayerSlot(StartOfRound.Instance?.localPlayerController);
    }

    public static int ResolvePlayerSlotForClient(ulong ownerClientId)
    {
        var players = StartOfRound.Instance?.allPlayerScripts;
        if (players == null)
        {
            return UnknownPlayerSlot;
        }

        for (var i = 0; i < players.Length; i++)
        {
            var player = players[i];
            if (player == null)
            {
                continue;
            }

            if (GetPlayerClientId(player) == ownerClientId
                || player.actualClientId == ownerClientId
                || player.playerClientId == ownerClientId)
            {
                return i;
            }
        }

        return UnknownPlayerSlot;
    }

    public static string DescribePlayerIdentity(PlayerControllerB player)
    {
        if (player == null)
        {
            return "player=null";
        }

        return $"player={player.playerUsername ?? player.name}; slot={GetPlayerSlot(player)}; playerClientId={player.playerClientId}; actualClientId={player.actualClientId}; resolvedClientId={GetPlayerClientId(player)}; suitId={player.currentSuitID}";
    }

    internal static ulong ResolveLocalClientId()
    {
        var manager = NetworkManager.Singleton;
        if (manager != null && manager.IsClient)
        {
            return manager.LocalClientId;
        }

        var player = StartOfRound.Instance?.localPlayerController;
        return player != null ? player.actualClientId : 0UL;
    }

    internal SuitTextureState NormalizeLocalStateOwner(SuitTextureState state, string context)
    {
        if (state == null || !state.IsPlayerSpecific || !state.IsLocalPlayerState)
        {
            return state;
        }

        var resolvedOwner = ResolveLocalClientId();
        var resolvedSlot = ResolveLocalPlayerSlot();
        if (state.OwnerClientId == resolvedOwner && state.OwnerPlayerSlot == resolvedSlot)
        {
            return state;
        }

        var oldOwner = state.OwnerClientId;
        var oldSlot = state.OwnerPlayerSlot;
        var oldKey = PlayerStateKey(oldOwner, oldSlot, state.SuitId);
        var newKey = PlayerStateKey(resolvedOwner, resolvedSlot, state.SuitId);
        if (_playerStates.TryGetValue(newKey, out var existing) && !ReferenceEquals(existing, state))
        {
            TextureTools.CopyInto(existing.EditableTexture, state.EditableTexture);
            existing.ActiveDesignName = state.ActiveDesignName;
            existing.RuntimeMaterial.mainTexture = existing.EditableTexture;
            existing.IsLocalPlayerState = true;
            existing.OwnerPlayerSlot = resolvedSlot;
            RemovePlayerStateReferences(state);
            DrawableSuitsDiagnostics.Info($"Local suit texture state owner migrated by copy. context={context}; suitId={state.SuitId}; oldOwnerClientId={oldOwner}; newOwnerClientId={resolvedOwner}; oldPlayerSlot={oldSlot}; newPlayerSlot={resolvedSlot}; oldStateDiscarded=True");
            return existing;
        }

        _playerStates.Remove(oldKey);
        state.OwnerClientId = resolvedOwner;
        state.OwnerPlayerSlot = resolvedSlot;
        state.IsLocalPlayerState = true;
        if (state.RuntimeMaterial != null)
        {
            state.RuntimeMaterial.name = $"{state.OriginalMaterial?.name ?? "Suit material"} (DrawableSuits client {resolvedOwner} slot {resolvedSlot})";
        }

        _playerStates[newKey] = state;
        DrawableSuitsDiagnostics.Info($"Local suit texture state owner migrated. context={context}; suitId={state.SuitId}; oldOwnerClientId={oldOwner}; newOwnerClientId={resolvedOwner}; oldPlayerSlot={oldSlot}; newPlayerSlot={resolvedSlot}");
        return state;
    }

    private SuitTextureState TryMigrateLocalState(int suitId, ulong resolvedOwner, int resolvedSlot, string context)
    {
        SuitTextureState candidate = null;
        foreach (var pair in _playerStates)
        {
            var state = pair.Value;
            if (state != null
                && state.IsLocalPlayerState
                && state.SuitId == suitId
                && (state.OwnerClientId != resolvedOwner || state.OwnerPlayerSlot != resolvedSlot))
            {
                candidate = state;
                break;
            }
        }

        return candidate != null ? NormalizeLocalStateOwner(candidate, context) : null;
    }

    private void RemovePlayerStateReferences(SuitTextureState state)
    {
        if (state == null)
        {
            return;
        }

        var remove = new List<string>();
        foreach (var pair in _playerStates)
        {
            if (ReferenceEquals(pair.Value, state))
            {
                remove.Add(pair.Key);
            }
        }

        foreach (var key in remove)
        {
            _playerStates.Remove(key);
        }
    }

    private static bool IsOwnerLocal(ulong ownerClientId)
    {
        return ownerClientId == ResolveLocalClientId();
    }

    private static bool PlayerMatchesStateOwner(PlayerControllerB player, SuitTextureState state)
    {
        if (player == null || state == null)
        {
            return false;
        }

        var playerSlot = GetPlayerSlot(player);
        if (state.OwnerPlayerSlot >= 0 && playerSlot == state.OwnerPlayerSlot)
        {
            return true;
        }

        return GetPlayerClientId(player) == state.OwnerClientId;
    }

    private static int NormalizePlayerSlot(int playerSlot)
    {
        return playerSlot >= 0 && playerSlot < 64 ? playerSlot : UnknownPlayerSlot;
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
