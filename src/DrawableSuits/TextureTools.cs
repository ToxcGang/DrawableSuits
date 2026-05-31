using System;
using System.IO;
using UnityEngine;

namespace DrawableSuits;

internal static class TextureTools
{
    public static Texture2D CreateEditableCopy(Texture source, int maxSize)
    {
        var width = Mathf.Clamp(source != null ? source.width : maxSize, 64, maxSize);
        var height = Mathf.Clamp(source != null ? source.height : maxSize, 64, maxSize);
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        texture.name = "DrawableSuitsEditableTexture";

        if (source == null)
        {
            Fill(texture, new Color32(180, 180, 180, 255));
            return texture;
        }

        var previous = RenderTexture.active;
        var temporary = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
        try
        {
            Graphics.Blit(source, temporary);
            RenderTexture.active = temporary;
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            texture.Apply(false, false);
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(temporary);
        }

        return texture;
    }

    public static Texture2D LoadImageFile(string path, int maxSize)
    {
        var bytes = File.ReadAllBytes(path);
        return LoadImageBytes(bytes, Path.GetFileNameWithoutExtension(path), maxSize);
    }

    public static Texture2D LoadImageBytes(byte[] bytes, string name, int maxSize, bool resizeOversized = true)
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!ImageConversion.LoadImage(texture, bytes, false))
        {
            UnityEngine.Object.Destroy(texture);
            return null;
        }

        texture.name = string.IsNullOrWhiteSpace(name) ? "DrawableSuitsImage" : name;
        if (texture.width <= maxSize && texture.height <= maxSize)
        {
            return texture;
        }

        if (!resizeOversized)
        {
            UnityEngine.Object.Destroy(texture);
            return null;
        }

        var resized = Resize(texture, maxSize, maxSize);
        resized.name = texture.name;
        UnityEngine.Object.Destroy(texture);
        return resized;
    }

    public static Texture2D Resize(Texture source, int width, int height)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        var previous = RenderTexture.active;
        var temporary = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
        try
        {
            Graphics.Blit(source, temporary);
            RenderTexture.active = temporary;
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            texture.Apply(false, false);
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(temporary);
        }

        return texture;
    }

    public static void Fill(Texture2D texture, Color32 color)
    {
        var pixels = new Color32[texture.width * texture.height];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = color;
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, false);
    }

    public static void CopyInto(Texture2D target, Texture2D source)
    {
        var resized = source.width == target.width && source.height == target.height
            ? source
            : Resize(source, target.width, target.height);

        target.SetPixels32(resized.GetPixels32());
        target.Apply(false, false);

        if (!ReferenceEquals(resized, source))
        {
            UnityEngine.Object.Destroy(resized);
        }
    }

    public static string SanitizeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "DrawableSuit";
        }

        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '_');
        }

        return value.Trim();
    }

    public static bool IsImagePath(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
    }
}
