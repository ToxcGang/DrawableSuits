using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace DrawableSuits;

internal sealed class SuitSyncManager : MonoBehaviour
{
    private const string MessageName = "DrawableSuits.Design";
    private const int MessageKindChunk = 1;
    private const int MessageKindRequestAll = 2;
    private const int MessageKindChunkV2 = 3;
    private const int LegacyDefaultMaxSyncBytes = 1048576;
    private const int RaisedDefaultMaxSyncBytes = 4194304;

    private readonly Dictionary<string, IncomingPayload> _incoming = new();
    private readonly Dictionary<string, CachedDesignPayload> _activePayloads = new(StringComparer.Ordinal);
    private bool _registered;
    private NetworkManager _registeredManager;
    private float _nextRegisterAttempt;
    private bool _loggedLegacyMaxSyncBytes;

    private void Update()
    {
        if (Time.unscaledTime < _nextRegisterAttempt)
        {
            return;
        }

        _nextRegisterAttempt = Time.unscaledTime + 1f;
        EnsureRegistered();
    }

    public void BroadcastDesign(SuitTextureState state)
    {
        if (!DrawableSuitsPlugin.ModConfig.EnableNetworkSync.Value || state?.EditableTexture == null)
        {
            DrawableSuitsDiagnostics.Info($"SyncBroadcastSkipped: reason=disabled-or-missing-texture; stateNull={state == null}; hasTexture={state?.EditableTexture != null}; networkSync={DrawableSuitsPlugin.ModConfig.EnableNetworkSync.Value}");
            return;
        }

        var manager = NetworkManager.Singleton;
        if (manager == null || manager.CustomMessagingManager == null || !manager.IsClient)
        {
            DrawableSuitsDiagnostics.Info($"SyncBroadcastSkipped: reason=network-not-ready; managerNull={manager == null}; hasMessaging={manager?.CustomMessagingManager != null}; isClient={manager?.IsClient.ToString() ?? "null"}");
            return;
        }

        state = DrawableSuitsPlugin.Registry?.NormalizeLocalStateOwner(state, "BroadcastDesign") ?? state;
        var bytes = ImageConversion.EncodeToPNG(state.EditableTexture);
        if (bytes == null || bytes.Length == 0)
        {
            DrawableSuitsDiagnostics.Info($"SyncBroadcastSkipped: reason=empty-png; ownerClientId={state.OwnerClientId}; suitId={state.SuitId}; designName={state.ActiveDesignName}");
            return;
        }

        var maxSyncBytes = EffectiveMaxSyncBytes();
        if (bytes.Length > maxSyncBytes)
        {
            var message = $"Not syncing {state.SuitName}: PNG payload is {bytes.Length} bytes, over MaxSyncBytes {maxSyncBytes}.";
            DrawableSuitsPlugin.ModLogger.LogWarning(message);
            DrawableSuitsDiagnostics.Warn($"SyncBroadcastSkipped: reason=payload-too-large; ownerClientId={state.OwnerClientId}; localClientId={manager.LocalClientId}; suitId={state.SuitId}; designName={state.ActiveDesignName}; bytes={bytes.Length}; maxSyncBytes={maxSyncBytes}");
            DrawableSuitsPlugin.Editor?.ShowExternalStatus($"Suit sync skipped: texture is {bytes.Length:n0} bytes, limit {maxSyncBytes:n0}.", true);
            return;
        }

        var recipients = manager.IsServer ? OtherClientIds(manager) : new List<ulong> { NetworkManager.ServerClientId };
        var ownerClientId = state.IsPlayerSpecific ? (state.IsLocalPlayerState ? SuitTextureRegistry.ResolveLocalClientId() : state.OwnerClientId) : manager.LocalClientId;
        DrawableSuitsDiagnostics.Info($"SyncBroadcastQueued: ownerClientId={ownerClientId}; localClientId={manager.LocalClientId}; suitId={state.SuitId}; designName={state.ActiveDesignName}; bytes={bytes.Length}; recipients=[{string.Join(",", recipients)}]; isServer={manager.IsServer}; chunkBytes={DrawableSuitsPlugin.ModConfig.SyncChunkBytes.Value}");
        if (manager.IsServer)
        {
            CacheActivePayload(ownerClientId, state.SuitId, state.ActiveDesignName, state.EditableTexture.width, state.EditableTexture.height, bytes, "host-broadcast");
        }

        SendTextureChunks(manager, recipients, ownerClientId, state.SuitId, state.ActiveDesignName, state.EditableTexture.width, state.EditableTexture.height, bytes);
    }

    public void RequestActiveDesigns()
    {
        if (!DrawableSuitsPlugin.ModConfig.EnableNetworkSync.Value)
        {
            return;
        }

        var manager = NetworkManager.Singleton;
        if (manager == null || manager.CustomMessagingManager == null || !manager.IsClient || manager.IsServer)
        {
            DrawableSuitsDiagnostics.Info($"SyncRequestSkipped: reason=not-client-requester; managerNull={manager == null}; hasMessaging={manager?.CustomMessagingManager != null}; isClient={manager?.IsClient.ToString() ?? "null"}; isServer={manager?.IsServer.ToString() ?? "null"}");
            return;
        }

        EnsureRegistered();
        using var writer = new FastBufferWriter(16, Allocator.Temp, 16);
        writer.WriteValueSafe(MessageKindRequestAll);
        manager.CustomMessagingManager.SendNamedMessage(MessageName, NetworkManager.ServerClientId, writer, NetworkDelivery.Reliable);
        DrawableSuitsDiagnostics.Info($"SyncRequestSent: requesterClientId={manager.LocalClientId}; serverClientId={NetworkManager.ServerClientId}");
    }

    private void EnsureRegistered()
    {
        var manager = NetworkManager.Singleton;
        if (manager == null || manager.CustomMessagingManager == null)
        {
            if (_incoming.Count > 0 || _activePayloads.Count > 0)
            {
                DrawableSuitsDiagnostics.Info($"SyncCacheCleared: reason=network-manager-unavailable; incoming={_incoming.Count}; cachedPayloads={_activePayloads.Count}");
                _incoming.Clear();
                _activePayloads.Clear();
            }

            _registered = false;
            _registeredManager = null;
            return;
        }

        if (_registered && ReferenceEquals(manager, _registeredManager))
        {
            return;
        }

        if (_registeredManager?.CustomMessagingManager != null)
        {
            _registeredManager.CustomMessagingManager.UnregisterNamedMessageHandler(MessageName);
        }

        if (!ReferenceEquals(manager, _registeredManager) && (_incoming.Count > 0 || _activePayloads.Count > 0))
        {
            DrawableSuitsDiagnostics.Info($"SyncCacheCleared: reason=network-manager-changed; incoming={_incoming.Count}; cachedPayloads={_activePayloads.Count}");
            _incoming.Clear();
            _activePayloads.Clear();
        }

        manager.CustomMessagingManager.RegisterNamedMessageHandler(MessageName, HandleMessage);
        _registered = true;
        _registeredManager = manager;
        DrawableSuitsDiagnostics.Info($"SyncRegistered: manager={DrawableSuitsPlugin.DescribeUnityObject(manager)}; localClientId={manager.LocalClientId}; isClient={manager.IsClient}; isServer={manager.IsServer}; connectedClients={manager.ConnectedClientsIds?.Count ?? 0}");
    }

    private void HandleMessage(ulong senderClientId, FastBufferReader reader)
    {
        try
        {
            reader.ReadValueSafe(out int kind);
            if (kind == MessageKindRequestAll)
            {
                HandleRequestAll(senderClientId);
                return;
            }

            if (kind == MessageKindChunk)
            {
                HandleChunk(senderClientId, reader, false);
                return;
            }

            if (kind == MessageKindChunkV2)
            {
                HandleChunk(senderClientId, reader, true);
            }
        }
        catch (Exception ex)
        {
            DrawableSuitsPlugin.ModLogger.LogWarning($"Failed to read sync message: {ex.Message}");
        }
    }

    private void HandleRequestAll(ulong senderClientId)
    {
        var manager = NetworkManager.Singleton;
        if (manager == null || !manager.IsServer)
        {
            return;
        }

        var sent = new HashSet<string>(StringComparer.Ordinal);
        var sentCount = 0;
        foreach (var payload in _activePayloads.Values)
        {
            if (payload == null || payload.Bytes == null || payload.Bytes.Length == 0)
            {
                continue;
            }

            if (!IsOwnerConnected(manager, payload.OwnerClientId))
            {
                DrawableSuitsDiagnostics.Info($"SyncRequestPayloadSkipped: requesterClientId={senderClientId}; ownerClientId={payload.OwnerClientId}; suitId={payload.SuitId}; reason=owner-not-connected");
                continue;
            }

            sent.Add(ActivePayloadKey(payload.OwnerClientId, payload.SuitId));
            SendTextureChunks(manager, new List<ulong> { senderClientId }, payload.OwnerClientId, payload.SuitId, payload.DesignName, payload.Width, payload.Height, payload.Bytes);
            sentCount++;
        }

        foreach (var state in DrawableSuitsPlugin.Registry.ActivePlayerStates)
        {
            if (state?.EditableTexture == null || string.IsNullOrWhiteSpace(state.ActiveDesignName))
            {
                continue;
            }

            var ownerClientId = state.IsLocalPlayerState ? SuitTextureRegistry.ResolveLocalClientId() : state.OwnerClientId;
            if (!IsOwnerConnected(manager, ownerClientId))
            {
                DrawableSuitsDiagnostics.Info($"SyncRequestPayloadSkipped: requesterClientId={senderClientId}; ownerClientId={ownerClientId}; suitId={state.SuitId}; reason=owner-not-connected");
                continue;
            }

            var key = ActivePayloadKey(ownerClientId, state.SuitId);
            if (sent.Contains(key))
            {
                continue;
            }

            var bytes = ImageConversion.EncodeToPNG(state.EditableTexture);
            if (bytes == null || bytes.Length == 0 || bytes.Length > EffectiveMaxSyncBytes())
            {
                DrawableSuitsDiagnostics.Warn($"SyncRequestPayloadSkipped: requesterClientId={senderClientId}; ownerClientId={ownerClientId}; suitId={state.SuitId}; reason=empty-or-too-large; bytes={bytes?.Length ?? 0}; maxSyncBytes={EffectiveMaxSyncBytes()}");
                continue;
            }

            CacheActivePayload(ownerClientId, state.SuitId, state.ActiveDesignName, state.EditableTexture.width, state.EditableTexture.height, bytes, "request-fallback-registry");
            SendTextureChunks(manager, new List<ulong> { senderClientId }, ownerClientId, state.SuitId, state.ActiveDesignName, state.EditableTexture.width, state.EditableTexture.height, bytes);
            sentCount++;
        }

        DrawableSuitsDiagnostics.Info($"SyncRequestHandled: requesterClientId={senderClientId}; cachedPayloads={_activePayloads.Count}; sentPayloads={sentCount}");
    }

    private void HandleChunk(ulong senderClientId, FastBufferReader reader, bool v2)
    {
        var ownerClientId = senderClientId;
        if (v2)
        {
            reader.ReadValueSafe(out ownerClientId);
        }

        reader.ReadValueSafe(out int suitId);
        reader.ReadValueSafe(out int width);
        reader.ReadValueSafe(out int height);
        reader.ReadValueSafe(out int chunkIndex);
        reader.ReadValueSafe(out int chunkCount);
        reader.ReadValueSafe(out int totalBytes);
        reader.ReadValueSafe(out string designName, false);
        reader.ReadValueSafe(out string hash, false);
        reader.ReadValueSafe(out int chunkLength);

        var maxSyncBytes = EffectiveMaxSyncBytes();
        if (chunkLength <= 0 || totalBytes <= 0 || totalBytes > maxSyncBytes)
        {
            DrawableSuitsDiagnostics.Warn($"SyncPayloadReceived: status=rejected; reason=invalid-size; senderClientId={senderClientId}; ownerClientId={ownerClientId}; suitId={suitId}; chunkLength={chunkLength}; totalBytes={totalBytes}; maxSyncBytes={maxSyncBytes}");
            return;
        }

        byte[] chunk = null;
        reader.ReadBytesSafe(ref chunk, chunkLength, 0);

        var key = $"{senderClientId}:{ownerClientId}:{suitId}:{hash}";
        if (!_incoming.TryGetValue(key, out var payload))
        {
            payload = new IncomingPayload(ownerClientId, suitId, width, height, designName, hash, totalBytes, chunkCount);
            _incoming[key] = payload;
        }

        payload.SetChunk(chunkIndex, chunk);
        if (!payload.IsComplete)
        {
            return;
        }

        _incoming.Remove(key);
        var bytes = payload.Combine();
        DrawableSuitsDiagnostics.Info($"SyncPayloadReceived: status=complete; ownerClientId={ownerClientId}; senderClientId={senderClientId}; suitId={suitId}; designName={designName}; bytes={bytes.Length}; chunks={chunkCount}; version={(v2 ? "v2" : "legacy")}");
        if (!string.Equals(hash, Hash(bytes), StringComparison.OrdinalIgnoreCase))
        {
            DrawableSuitsPlugin.ModLogger.LogWarning($"Rejected synced suit {suitId} for client {ownerClientId}: hash mismatch.");
            DrawableSuitsDiagnostics.Warn($"SyncPayloadReceived: status=rejected; reason=hash-mismatch; ownerClientId={ownerClientId}; senderClientId={senderClientId}; suitId={suitId}; bytes={bytes.Length}");
            return;
        }

        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!ImageConversion.LoadImage(texture, bytes, false))
        {
            Destroy(texture);
            return;
        }

        DrawableSuitsPlugin.Registry.ApplyReceivedTexture(ownerClientId, suitId, designName, texture);
        Destroy(texture);

        var manager = NetworkManager.Singleton;
        if (manager != null && manager.IsServer)
        {
            CacheActivePayload(ownerClientId, suitId, designName, width, height, bytes, "server-received");
        }

        if (manager != null && manager.IsServer && senderClientId != manager.LocalClientId)
        {
            var recipients = OtherClientIds(manager).Where(id => id != senderClientId).ToList();
            SendTextureChunks(manager, recipients, ownerClientId, suitId, designName, width, height, bytes);
        }
    }

    private static void SendTextureChunks(NetworkManager manager, IReadOnlyList<ulong> recipients, ulong ownerClientId, int suitId, string designName, int width, int height, byte[] bytes)
    {
        if (manager?.CustomMessagingManager == null || recipients == null || recipients.Count == 0)
        {
            DrawableSuitsDiagnostics.Info($"SyncChunkSent: status=skipped; reason=no-recipients-or-messaging; ownerClientId={ownerClientId}; localClientId={manager?.LocalClientId.ToString() ?? "null"}; suitId={suitId}; recipients={recipients?.Count ?? 0}; bytes={bytes?.Length ?? 0}");
            return;
        }

        var chunkSize = Mathf.Clamp(DrawableSuitsPlugin.ModConfig.SyncChunkBytes.Value, 4096, 60000);
        var chunkCount = Mathf.CeilToInt(bytes.Length / (float)chunkSize);
        var hash = Hash(bytes);
        designName ??= string.Empty;

        for (var i = 0; i < chunkCount; i++)
        {
            var offset = i * chunkSize;
            var length = Math.Min(chunkSize, bytes.Length - offset);
            var chunk = new byte[length];
            Buffer.BlockCopy(bytes, offset, chunk, 0, length);

            var writerSize = length + 1024 + Encoding.UTF8.GetByteCount(designName) + Encoding.UTF8.GetByteCount(hash);
            using var writer = new FastBufferWriter(writerSize, Allocator.Temp, writerSize);
            writer.WriteValueSafe(MessageKindChunkV2);
            writer.WriteValueSafe(ownerClientId);
            writer.WriteValueSafe(suitId);
            writer.WriteValueSafe(width);
            writer.WriteValueSafe(height);
            writer.WriteValueSafe(i);
            writer.WriteValueSafe(chunkCount);
            writer.WriteValueSafe(bytes.Length);
            writer.WriteValueSafe(designName, false);
            writer.WriteValueSafe(hash, false);
            writer.WriteValueSafe(length);
            writer.WriteBytesSafe(chunk, length, 0);

            foreach (var recipient in recipients)
            {
                manager.CustomMessagingManager.SendNamedMessage(MessageName, recipient, writer, NetworkDelivery.ReliableFragmentedSequenced);
            }
        }

        DrawableSuitsDiagnostics.Info($"SyncChunkSent: status=sent; ownerClientId={ownerClientId}; localClientId={manager.LocalClientId}; suitId={suitId}; designName={designName}; bytes={bytes.Length}; chunkSize={chunkSize}; chunkCount={chunkCount}; recipients=[{string.Join(",", recipients)}]");
    }

    private static List<ulong> OtherClientIds(NetworkManager manager)
    {
        var result = new List<ulong>();
        if (manager?.ConnectedClientsIds == null)
        {
            return result;
        }

        foreach (var id in manager.ConnectedClientsIds)
        {
            if (id != manager.LocalClientId)
            {
                result.Add(id);
            }
        }

        return result;
    }

    private static bool IsOwnerConnected(NetworkManager manager, ulong ownerClientId)
    {
        if (manager?.ConnectedClientsIds == null)
        {
            return true;
        }

        return manager.ConnectedClientsIds.Contains(ownerClientId);
    }

    private static string Hash(byte[] bytes)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(bytes);
        var builder = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
        {
            builder.Append(b.ToString("x2"));
        }

        return builder.ToString();
    }

    private int EffectiveMaxSyncBytes()
    {
        var configured = Mathf.Max(4096, DrawableSuitsPlugin.ModConfig.MaxSyncBytes.Value);
        if (configured <= LegacyDefaultMaxSyncBytes)
        {
            if (!_loggedLegacyMaxSyncBytes)
            {
                _loggedLegacyMaxSyncBytes = true;
                DrawableSuitsDiagnostics.Info($"MaxSyncBytes raised for legacy/default config. configured={configured}; effective={RaisedDefaultMaxSyncBytes}");
            }

            return RaisedDefaultMaxSyncBytes;
        }

        return configured;
    }

    private void CacheActivePayload(ulong ownerClientId, int suitId, string designName, int width, int height, byte[] bytes, string reason)
    {
        if (bytes == null || bytes.Length == 0)
        {
            return;
        }

        var key = ActivePayloadKey(ownerClientId, suitId);
        _activePayloads[key] = new CachedDesignPayload(ownerClientId, suitId, width, height, designName ?? string.Empty, bytes);
        DrawableSuitsDiagnostics.Info($"SyncPayloadCached: reason={reason}; ownerClientId={ownerClientId}; suitId={suitId}; designName={designName}; texture={width}x{height}; bytes={bytes.Length}; cachedPayloads={_activePayloads.Count}");
    }

    private static string ActivePayloadKey(ulong ownerClientId, int suitId)
    {
        return $"{ownerClientId}:{suitId}";
    }

    private sealed class CachedDesignPayload
    {
        public ulong OwnerClientId { get; }
        public int SuitId { get; }
        public int Width { get; }
        public int Height { get; }
        public string DesignName { get; }
        public byte[] Bytes { get; }

        public CachedDesignPayload(ulong ownerClientId, int suitId, int width, int height, string designName, byte[] bytes)
        {
            OwnerClientId = ownerClientId;
            SuitId = suitId;
            Width = width;
            Height = height;
            DesignName = designName;
            Bytes = bytes;
        }
    }

    private sealed class IncomingPayload
    {
        private readonly byte[][] _chunks;
        private int _received;

        public int SuitId { get; }
        public ulong OwnerClientId { get; }
        public int Width { get; }
        public int Height { get; }
        public string DesignName { get; }
        public string Hash { get; }
        public int TotalBytes { get; }
        public bool IsComplete => _received == _chunks.Length;

        public IncomingPayload(ulong ownerClientId, int suitId, int width, int height, string designName, string hash, int totalBytes, int chunkCount)
        {
            OwnerClientId = ownerClientId;
            SuitId = suitId;
            Width = width;
            Height = height;
            DesignName = designName;
            Hash = hash;
            TotalBytes = totalBytes;
            _chunks = new byte[Mathf.Max(1, chunkCount)][];
        }

        public void SetChunk(int index, byte[] bytes)
        {
            if (index < 0 || index >= _chunks.Length || bytes == null || _chunks[index] != null)
            {
                return;
            }

            _chunks[index] = bytes;
            _received++;
        }

        public byte[] Combine()
        {
            var result = new byte[TotalBytes];
            var offset = 0;
            foreach (var chunk in _chunks)
            {
                if (chunk == null)
                {
                    continue;
                }

                Buffer.BlockCopy(chunk, 0, result, offset, chunk.Length);
                offset += chunk.Length;
            }

            return result;
        }
    }
}
