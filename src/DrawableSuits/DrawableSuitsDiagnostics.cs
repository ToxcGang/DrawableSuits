using System;
using System.IO;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DrawableSuits;

internal static class DrawableSuitsDiagnostics
{
    private static readonly object SyncRoot = new();
    private static string _logPath;
    private static bool _initialized;

    public static string LogPath => _logPath ?? Path.Combine(DrawableSuitsPaths.Logs, "diagnostics.log");

    public static void Initialize()
    {
        try
        {
            DrawableSuitsPaths.EnsureCreated();
            _logPath = Path.Combine(DrawableSuitsPaths.Logs, "diagnostics.log");
            Directory.CreateDirectory(Path.GetDirectoryName(_logPath));
            File.WriteAllText(_logPath, string.Empty);
            _initialized = true;

            Info("Diagnostics initialized.");
            Info($"{PluginInfo.Name} {PluginInfo.Version} ({PluginInfo.Guid}) starting.");
            Info($"UnityVersion={Application.unityVersion}; ApplicationVersion={Application.version}; Platform={Application.platform}; ProductName={Application.productName}");
            Info($"DataPath={Application.dataPath}; PersistentDataPath={Application.persistentDataPath}; ConfigRoot={DrawableSuitsPaths.Root}");
            Info($"ActiveScene={SceneManager.GetActiveScene().name}; SceneBuildIndex={SceneManager.GetActiveScene().buildIndex}");

            var references = Assembly.GetExecutingAssembly().GetReferencedAssemblies();
            foreach (var reference in references)
            {
                Info($"Reference={reference.Name}, Version={reference.Version}");
            }
        }
        catch (Exception ex)
        {
            _initialized = false;
            LogToBepInEx(LogLevel.Error, $"DrawableSuits diagnostics failed to initialize: {ex}");
        }
    }

    public static void Info(string message)
    {
        Write(LogLevel.Info, message);
    }

    public static void Warn(string message)
    {
        Write(LogLevel.Warning, message);
    }

    public static void Error(string message)
    {
        Write(LogLevel.Error, message);
    }

    public static void Exception(string context, Exception exception)
    {
        if (exception == null)
        {
            Error($"{context}: unknown exception.");
            return;
        }

        Write(LogLevel.Error, $"{context}: {exception.GetType().FullName}: {exception.Message}{Environment.NewLine}{exception.StackTrace}");
    }

    private static void Write(LogLevel level, string message)
    {
        var line = $"[{DateTime.Now:O}] [{level}] {message ?? string.Empty}";
        LogToBepInEx(level, message ?? string.Empty);

        if (!_initialized)
        {
            return;
        }

        try
        {
            lock (SyncRoot)
            {
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
        }
        catch
        {
            // Do not let diagnostics logging break the mod.
        }
    }

    private static void LogToBepInEx(LogLevel level, string message)
    {
        var logger = DrawableSuitsPlugin.ModLogger;
        if (logger == null)
        {
            Debug.Log($"[DrawableSuits] {message}");
            return;
        }

        switch (level)
        {
            case LogLevel.Error:
                logger.LogError(message);
                break;
            case LogLevel.Warning:
                logger.LogWarning(message);
                break;
            case LogLevel.Debug:
                logger.LogDebug(message);
                break;
            default:
                logger.LogInfo(message);
                break;
        }
    }
}
