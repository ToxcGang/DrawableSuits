using System.IO;
using BepInEx;

namespace DrawableSuits;

internal static class DrawableSuitsPaths
{
    public static string Root => Path.Combine(Paths.ConfigPath, "DrawableSuits");
    public static string Saves => Path.Combine(Root, "Saves");
    public static string Textures => Path.Combine(Root, "Textures");
    public static string Decals => Path.Combine(Root, "Decals");
    public static string Logs => Path.Combine(Root, "Logs");
    public static string PartPresets => Path.Combine(Root, "PartPresets");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Saves);
        Directory.CreateDirectory(Textures);
        Directory.CreateDirectory(Decals);
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(PartPresets);
    }
}
