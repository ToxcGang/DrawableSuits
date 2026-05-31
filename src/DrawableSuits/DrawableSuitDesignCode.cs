using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;

namespace DrawableSuits;

internal static class DrawableSuitDesignCode
{
    internal const string Prefix = "DSUIT1:";
    private const int FormatVersion = 1;
    private const int MaxDecodedJsonBytes = 24 * 1024 * 1024;
    private const int MaxPngBytes = 16 * 1024 * 1024;

    [Serializable]
    internal sealed class Payload
    {
        public int formatVersion;
        public string modVersion;
        public string designName;
        public string baseSuitName;
        public int sourceSuitId;
        public int width;
        public int height;
        public string exportedUtc;
        public string pngBase64;
    }

    internal struct CodeInfo
    {
        internal int FormatVersion;
        internal string DesignName;
        internal string BaseSuitName;
        internal int SourceSuitId;
        internal int Width;
        internal int Height;
        internal int PngBytes;
        internal int JsonBytes;
        internal int CompressedBytes;
        internal int CodeLength;
    }

    internal static bool TryExport(SuitTextureState state, string designName, out string code, out CodeInfo info, out string failureReason)
    {
        code = string.Empty;
        info = default;
        failureReason = string.Empty;

        if (state?.EditableTexture == null)
        {
            failureReason = "No editable suit texture is available.";
            return false;
        }

        var safeName = TextureTools.SanitizeFileName(designName);
        if (string.IsNullOrWhiteSpace(safeName))
        {
            failureReason = "Design name is empty.";
            return false;
        }

        var pngBytes = ImageConversion.EncodeToPNG(state.EditableTexture);
        if (pngBytes == null || pngBytes.Length == 0)
        {
            failureReason = "Failed to encode the editable texture.";
            return false;
        }

        if (pngBytes.Length > MaxPngBytes)
        {
            failureReason = $"Encoded texture is too large ({pngBytes.Length} bytes).";
            return false;
        }

        var payload = new Payload
        {
            formatVersion = FormatVersion,
            modVersion = PluginInfo.Version,
            designName = safeName,
            baseSuitName = state.SuitName ?? string.Empty,
            sourceSuitId = state.SuitId,
            width = state.EditableTexture.width,
            height = state.EditableTexture.height,
            exportedUtc = DateTime.UtcNow.ToString("o"),
            pngBase64 = Convert.ToBase64String(pngBytes)
        };

        var json = JsonUtility.ToJson(payload, false);
        var jsonBytes = Encoding.UTF8.GetBytes(json);
        var compressed = Compress(jsonBytes);
        code = Prefix + ToBase64Url(compressed);

        info = new CodeInfo
        {
            FormatVersion = FormatVersion,
            DesignName = safeName,
            BaseSuitName = payload.baseSuitName,
            SourceSuitId = state.SuitId,
            Width = state.EditableTexture.width,
            Height = state.EditableTexture.height,
            PngBytes = pngBytes.Length,
            JsonBytes = jsonBytes.Length,
            CompressedBytes = compressed.Length,
            CodeLength = code.Length
        };
        return true;
    }

    internal static bool TryDecode(string code, int maxTextureSize, out Payload payload, out Texture2D texture, out CodeInfo info, out string failureReason)
    {
        payload = null;
        texture = null;
        info = default;
        failureReason = string.Empty;

        if (string.IsNullOrWhiteSpace(code))
        {
            failureReason = "Design code is empty.";
            return false;
        }

        code = code.Trim();
        if (!code.StartsWith(Prefix, StringComparison.Ordinal))
        {
            failureReason = $"Design code must start with {Prefix}";
            return false;
        }

        byte[] compressed;
        try
        {
            compressed = FromBase64Url(RemoveWhitespace(code.Substring(Prefix.Length)));
        }
        catch (Exception ex)
        {
            failureReason = $"Design code is not valid Base64Url data ({ex.GetType().Name}).";
            return false;
        }

        byte[] jsonBytes;
        try
        {
            jsonBytes = Decompress(compressed, MaxDecodedJsonBytes);
        }
        catch (Exception ex)
        {
            failureReason = $"Design code payload could not be decompressed ({ex.GetType().Name}).";
            return false;
        }

        try
        {
            payload = JsonUtility.FromJson<Payload>(Encoding.UTF8.GetString(jsonBytes));
        }
        catch (Exception ex)
        {
            failureReason = $"Design code JSON could not be parsed ({ex.GetType().Name}).";
            return false;
        }

        if (payload == null)
        {
            failureReason = "Design code payload is missing.";
            return false;
        }

        if (payload.formatVersion != FormatVersion)
        {
            failureReason = $"Unsupported design code format {payload.formatVersion}.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(payload.designName))
        {
            failureReason = "Design code is missing a design name.";
            return false;
        }

        if (payload.width <= 0 || payload.height <= 0)
        {
            failureReason = "Design code has invalid texture dimensions.";
            return false;
        }

        if (payload.width > maxTextureSize || payload.height > maxTextureSize)
        {
            failureReason = $"Design code texture is too large ({payload.width}x{payload.height}, max {maxTextureSize}).";
            return false;
        }

        if (string.IsNullOrWhiteSpace(payload.pngBase64))
        {
            failureReason = "Design code is missing PNG data.";
            return false;
        }

        byte[] pngBytes;
        try
        {
            pngBytes = Convert.FromBase64String(payload.pngBase64);
        }
        catch (Exception ex)
        {
            failureReason = $"Design code PNG data is invalid ({ex.GetType().Name}).";
            return false;
        }

        if (pngBytes.Length == 0)
        {
            failureReason = "Design code PNG data is empty.";
            return false;
        }

        if (pngBytes.Length > MaxPngBytes)
        {
            failureReason = $"Design code PNG data is too large ({pngBytes.Length} bytes).";
            return false;
        }

        texture = TextureTools.LoadImageBytes(pngBytes, "DrawableSuitsDesignCodeTexture", maxTextureSize, false);
        if (texture == null)
        {
            failureReason = $"Design code PNG data could not be loaded as an image or exceeds max size {maxTextureSize}.";
            return false;
        }

        if (texture.width > maxTextureSize || texture.height > maxTextureSize)
        {
            var width = texture.width;
            var height = texture.height;
            UnityEngine.Object.Destroy(texture);
            texture = null;
            failureReason = $"Decoded texture is too large ({width}x{height}, max {maxTextureSize}).";
            return false;
        }

        info = new CodeInfo
        {
            FormatVersion = FormatVersion,
            DesignName = payload.designName,
            BaseSuitName = payload.baseSuitName,
            SourceSuitId = payload.sourceSuitId,
            Width = texture.width,
            Height = texture.height,
            PngBytes = pngBytes.Length,
            JsonBytes = jsonBytes.Length,
            CompressedBytes = compressed.Length,
            CodeLength = code.Length
        };
        return true;
    }

    private static byte[] Compress(byte[] bytes)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionMode.Compress, true))
        {
            gzip.Write(bytes, 0, bytes.Length);
        }

        return output.ToArray();
    }

    private static byte[] Decompress(byte[] bytes, int maxBytes)
    {
        using var input = new MemoryStream(bytes);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = gzip.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (output.Length + read > maxBytes)
            {
                throw new InvalidDataException("Decoded payload exceeds the maximum allowed size.");
            }

            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    private static string ToBase64Url(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static byte[] FromBase64Url(string value)
    {
        value = value.Replace('-', '+').Replace('_', '/');
        switch (value.Length % 4)
        {
            case 2:
                value += "==";
                break;
            case 3:
                value += "=";
                break;
            case 0:
                break;
            default:
                throw new FormatException("Invalid Base64Url padding.");
        }

        return Convert.FromBase64String(value);
    }

    private static string RemoveWhitespace(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (!char.IsWhiteSpace(value[i]))
            {
                builder.Append(value[i]);
            }
        }

        return builder.ToString();
    }
}
