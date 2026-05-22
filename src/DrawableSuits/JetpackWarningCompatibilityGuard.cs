using System;
using System.IO;
using System.Reflection;
using BepInEx;
using GameNetcodeStuff;
using HarmonyLib;
using UnityEngine;

namespace DrawableSuits;

internal static class JetpackWarningCompatibilityGuard
{
    private const string TargetTypeName = "JetpackWarning.Patches";
    private const string TargetMethodName = "PlayerControllerB_LateUpdate_Postfix";
    private const string TargetOriginalMethodName = "LateUpdate";
    private const int MinimumErrorOccurrences = 2;
    private const int TailBytes = 131072;

    private static readonly Harmony CompatibilityHarmony = new(PluginInfo.Guid + ".jetpackwarningcompat");
    private static bool _unpatched;
    private static bool _attemptedUnpatch;
    private static bool _configDisabledLogged;
    private static float _lastScanTime;
    private static string _status = "not-run";

    internal static string Status => _status;

    internal static void CheckAndRepair(string reason, bool force = false)
    {
        if (DrawableSuitsPlugin.ModConfig == null)
        {
            return;
        }

        if (!DrawableSuitsPlugin.ModConfig.AutoDisableBrokenJetpackWarningLateUpdatePatch.Value)
        {
            if (!_configDisabledLogged)
            {
                _configDisabledLogged = true;
                _status = "disabled-by-config";
                DrawableSuitsDiagnostics.Info("JetpackWarning compatibility guard is disabled by config.");
            }
            return;
        }

        if (_unpatched)
        {
            return;
        }

        if (!force && Time.unscaledTime - _lastScanTime < 0.75f)
        {
            return;
        }

        _lastScanTime = Time.unscaledTime;
        if (!LogContainsRepeatedTargetErrors(out var occurrenceCount, out var logPath))
        {
            _status = $"watching occurrences={occurrenceCount}";
            return;
        }

        TryDisableBrokenPostfix(reason, occurrenceCount, logPath);
    }

    private static bool LogContainsRepeatedTargetErrors(out int occurrenceCount, out string logPath)
    {
        occurrenceCount = 0;
        logPath = Path.Combine(Paths.BepInExRootPath, "LogOutput.log");
        try
        {
            if (!File.Exists(logPath))
            {
                return false;
            }

            using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length <= 0)
            {
                return false;
            }

            stream.Seek(-Math.Min(TailBytes, stream.Length), SeekOrigin.End);
            using var reader = new StreamReader(stream);
            var tail = reader.ReadToEnd();
            occurrenceCount = CountOccurrences(tail, "JetpackWarning.Patches.PlayerControllerB_LateUpdate_Postfix");
            if (occurrenceCount < MinimumErrorOccurrences)
            {
                return false;
            }

            return tail.IndexOf("PlayerControllerB.LateUpdate", StringComparison.OrdinalIgnoreCase) >= 0
                && tail.IndexOf("NullReferenceException", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        catch (Exception ex)
        {
            _status = "log-scan-failed";
            DrawableSuitsDiagnostics.Exception("JetpackWarning compatibility guard failed to scan BepInEx/LogOutput.log", ex);
            return false;
        }
    }

    private static void TryDisableBrokenPostfix(string reason, int occurrenceCount, string logPath)
    {
        if (_attemptedUnpatch)
        {
            return;
        }

        _attemptedUnpatch = true;
        var original = AccessTools.Method(typeof(PlayerControllerB), TargetOriginalMethodName);
        if (original == null)
        {
            _status = "failed-original-missing";
            DrawableSuitsDiagnostics.Warn($"JetpackWarning compatibility guard could not find PlayerControllerB.{TargetOriginalMethodName}; reason={reason}; occurrences={occurrenceCount}; log={logPath}");
            return;
        }

        var patchInfo = Harmony.GetPatchInfo(original);
        if (patchInfo == null)
        {
            _status = "failed-no-patch-info";
            DrawableSuitsDiagnostics.Warn($"JetpackWarning compatibility guard found no Harmony patch info for {DescribeMethod(original)}; reason={reason}; occurrences={occurrenceCount}; log={logPath}");
            return;
        }

        MethodInfo targetPatch = null;
        string owner = null;
        foreach (var patch in patchInfo.Postfixes)
        {
            var method = patch?.PatchMethod;
            if (method == null || !IsTargetPatchMethod(method))
            {
                continue;
            }

            targetPatch = method;
            owner = patch.owner;
            break;
        }

        if (targetPatch == null)
        {
            _status = "failed-target-postfix-missing";
            DrawableSuitsDiagnostics.Warn($"JetpackWarning compatibility guard saw repeated errors but could not find the target postfix on {DescribeMethod(original)}. reason={reason}; occurrences={occurrenceCount}; postfixes=[{DescribePostfixes(patchInfo)}]");
            return;
        }

        try
        {
            CompatibilityHarmony.Unpatch(original, targetPatch);
            _unpatched = true;
            _status = $"unpatched owner={owner ?? "unknown"}";
            DrawableSuitsDiagnostics.Warn($"JetpackWarning compatibility guard disabled broken postfix. reason={reason}; occurrences={occurrenceCount}; original={DescribeMethod(original)}; patch={DescribeMethod(targetPatch)}; owner={owner ?? "unknown"}; log={logPath}");
        }
        catch (Exception ex)
        {
            _status = "failed-unpatch-exception";
            DrawableSuitsDiagnostics.Exception($"JetpackWarning compatibility guard failed to unpatch {DescribeMethod(targetPatch)} from {DescribeMethod(original)}", ex);
        }
    }

    private static bool IsTargetPatchMethod(MethodInfo method)
    {
        var declaringType = method.DeclaringType?.FullName ?? string.Empty;
        return string.Equals(method.Name, TargetMethodName, StringComparison.Ordinal)
            && (string.Equals(declaringType, TargetTypeName, StringComparison.Ordinal)
                || declaringType.IndexOf("JetpackWarning", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static string DescribePostfixes(Patches patchInfo)
    {
        if (patchInfo?.Postfixes == null || patchInfo.Postfixes.Count == 0)
        {
            return "none";
        }

        var parts = new System.Collections.Generic.List<string>();
        foreach (var patch in patchInfo.Postfixes)
        {
            parts.Add($"{DescribeMethod(patch.PatchMethod)} owner={patch.owner ?? "unknown"}");
        }

        return string.Join(" | ", parts);
    }

    private static string DescribeMethod(MethodBase method)
    {
        if (method == null)
        {
            return "null";
        }

        return $"{method.DeclaringType?.FullName ?? "<no type>"}.{method.Name}";
    }

    private static int CountOccurrences(string text, string value)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(value))
        {
            return 0;
        }

        var count = 0;
        var index = 0;
        while (index < text.Length)
        {
            index = text.IndexOf(value, index, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                break;
            }

            count++;
            index += value.Length;
        }

        return count;
    }
}
