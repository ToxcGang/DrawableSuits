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

    private readonly Dictionary<string, IncomingPayload> _incoming = new();
    private bool _registered;
    private NetworkManager _registeredManager;
    private float _nextRegisterAttempt;

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
            return;
        }

        var manager = NetworkManager.Singleton;
        if (manager == null || manager.CustomMessagingManager == null || !manager.IsClient)
        {
            return;
        }

        var bytes = ImageConversion.EncodeToPNG(state.EditableTexture);
        if (bytes == null || bytes.Length == 0)
        {
            return;
        }

        if (bytes.Length > DrawableSuitsPlugin.ModConfig.MaxSyncBytes.Value)
        {
            DrawableSuitsPlugin.ModLogger.LogWarning($"Not syncing {state.SuitName}: PNG payload is {bytes.Length} bytes, over MaxSyncBytes.");
            return;
        }

        var recipients = manager.IsServer ? OtherClientIds(manager) : new List<ulong> { NetworkManager.ServerClientId };
        SendTextureChunks(manager, recipients, state.SuitId, state.ActiveDesignName, state.EditableTexture.width, state.EditableTexture.height, bytes);
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
            return;
        }

        EnsureRegistered();
        using var writer = new FastBufferWriter(16, Allocator.Temp, 16);
        writer.WriteValueSafe(MessageKindRequestAll);
        manager.CustomMessagingManager.SendNamedMessage(MessageName, NetworkManager.ServerClientId, writer, NetworkDelivery.Reliable);
    }

    private void EnsureRegistered()
    {
        var manager = NetworkManager.Singleton;
        if (manager == null || manager.CustomMessagingManager == null)
        {
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

        manager.CustomMessagingManager.RegisterNamedMessageHandler(MessageName, HandleMessage);
        _registered = true;
        _registeredManager = manager;
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
                HandleChunk(senderClientId, reader);
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

        foreach (var state in DrawableSuitsPlugin.Registry.States.Values)
        {
            if (state?.EditableTexture == null || string.IsNullOrWhiteSpace(state.ActiveDesignName))
            {
                continue;
            }

            var bytes = ImageConversion.EncodeToPNG(state.EditableTexture);
            if (bytes == null || bytes.Length == 0 || bytes.Length > DrawableSuitsPlugin.ModConfig.MaxSyncBytes.Value)
            {
                continue;
            }

            SendTextureChunks(manager, new List<ulong> { senderClientId }, state.SuitId, state.ActiveDesignName, state.EditableTexture.width, state.EditableTexture.height, bytes);
        }
    }

    private void HandleChunk(ulong senderClientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out int suitId);
        reader.ReadValueSafe(out int width);
        reader.ReadValueSafe(out int height);
        reader.ReadValueSafe(out int chunkIndex);
        reader.ReadValueSafe(out int chunkCount);
        reader.ReadValueSafe(out int totalBytes);
        reader.ReadValueSafe(out string designName, false);
        reader.ReadValueSafe(out string hash, false);
        reader.ReadValueSafe(out int chunkLength);

        if (chunkLength <= 0 || totalBytes <= 0 || totalBytes > DrawableSuitsPlugin.ModConfig.MaxSyncBytes.Value)
        {
            return;
        }

        byte[] chunk = null;
        reader.ReadBytesSafe(ref chunk, chunkLength, 0);

        var key = $"{senderClientId}:{suitId}:{hash}";
        if (!_incoming.TryGetValue(key, out var payload))
        {
            payload = new IncomingPayload(suitId, width, height, designName, hash, totalBytes, chunkCount);
            _incoming[key] = payload;
        }

        payload.SetChunk(chunkIndex, chunk);
        if (!payload.IsComplete)
        {
            return;
        }

        _incoming.Remove(key);
        var bytes = payload.Combine();
        if (!string.Equals(hash, Hash(bytes), StringComparison.OrdinalIgnoreCase))
        {
            DrawableSuitsPlugin.ModLogger.LogWarning($"Rejected synced suit {suitId}: hash mismatch.");
            return;
        }

        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!ImageConversion.LoadImage(texture, bytes, false))
        {
            Destroy(texture);
            return;
        }

        DrawableSuitsPlugin.Registry.ApplyReceivedTexture(suitId, designName, texture);
        Destroy(texture);

        var manager = NetworkManager.Singleton;
        if (manager != null && manager.IsServer && senderClientId != manager.LocalClientId)
        {
            var recipients = OtherClientIds(manager).Where(id => id != senderClientId).ToList();
            SendTextureChunks(manager, recipients, suitId, designName, width, height, bytes);
        }
    }

    private static void SendTextureChunks(NetworkManager manager, IReadOnlyList<ulong> recipients, int suitId, string designName, int width, int height, byte[] bytes)
    {
        if (manager?.CustomMessagingManager == null || recipients == null || recipients.Count == 0)
        {
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
            writer.WriteValueSafe(MessageKindChunk);
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

    private sealed class IncomingPayload
    {
        private readonly byte[][] _chunks;
        private int _received;

        public int SuitId { get; }
        public int Width { get; }
        public int Height { get; }
        public string DesignName { get; }
        public string Hash { get; }
        public int TotalBytes { get; }
        public bool IsComplete => _received == _chunks.Length;

        public IncomingPayload(int suitId, int width, int height, string designName, string hash, int totalBytes, int chunkCount)
        {
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
