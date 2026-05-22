using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using GameNetcodeStuff;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DrawableSuits;

internal static class SessionSafetyGuard
{
    private static readonly string[] StrayObjectNames =
    {
        "DrawableSuitsThirdPersonCamera",
        "DrawableSuitsPreviewCamera",
        "DrawableSuitsWorldPaintProxy",
        "DrawableSuitsWorldBrushMarker"
    };

    private static float _lastFullLogTime;
    private static bool _externalLateUpdateWarningLogged;

    internal static void Run(string reason, bool forceLog = false)
    {
        try
        {
            var editorOpen = DrawableSuitsPlugin.IsEditorOpen;
            var repaired = 0;

            if (!editorOpen)
            {
                repaired += RepairStrayObjects();

                if (DrawableSuitsPlugin.Editor != null)
                {
                    repaired += DrawableSuitsPlugin.Editor.RepairClosedEditorState(reason);
                }

                if (DrawableSuitsPlugin.Registry != null)
                {
                    repaired += DrawableSuitsPlugin.Registry.RestoreLocalPlayerMaterials($"SessionSafetyCheck:{reason}");
                }
            }

            var shouldLog = forceLog || repaired > 0 || Time.unscaledTime - _lastFullLogTime >= 2.5f;
            if (!shouldLog)
            {
                return;
            }

            _lastFullLogTime = Time.unscaledTime;
            WarnAboutExternalLateUpdateSpamIfPresent();
            var scene = SceneManager.GetActiveScene();
            DrawableSuitsDiagnostics.Info(
                $"SessionSafetyCheck reason={reason}; scene={scene.name}; editorOpen={editorOpen}; repaired={repaired}; " +
                $"activeCameras=[{DescribeCameras()}]; localRenderers=[{DescribeLocalRenderers()}]");
        }
        catch (Exception ex)
        {
            DrawableSuitsDiagnostics.Exception($"SessionSafetyCheck failed. reason={reason}", ex);
        }
    }


    private static void WarnAboutExternalLateUpdateSpamIfPresent()
    {
        if (_externalLateUpdateWarningLogged)
        {
            return;
        }

        try
        {
            var logPath = Path.Combine(Paths.BepInExRootPath, "LogOutput.log");
            if (!File.Exists(logPath))
            {
                return;
            }

            const int tailBytes = 65536;
            using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length <= 0)
            {
                return;
            }

            stream.Seek(-Math.Min(tailBytes, stream.Length), SeekOrigin.End);
            using var reader = new StreamReader(stream);
            var tail = reader.ReadToEnd();
            if (tail.IndexOf("JetpackWarning", StringComparison.OrdinalIgnoreCase) >= 0
                && tail.IndexOf("PlayerControllerB.LateUpdate", StringComparison.OrdinalIgnoreCase) >= 0
                && tail.IndexOf("NullReferenceException", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                _externalLateUpdateWarningLogged = true;
                DrawableSuitsDiagnostics.Warn("Detected repeated JetpackWarning PlayerControllerB.LateUpdate NullReferenceException entries in BepInEx/LogOutput.log. DrawableSuits will not patch another mod, but this external error can affect session rendering/input while debugging the black-screen issue.");
            }
        }
        catch (Exception ex)
        {
            DrawableSuitsDiagnostics.Exception("Failed to scan BepInEx/LogOutput.log for external LateUpdate errors", ex);
        }
    }

    private static int RepairStrayObjects()
    {
        var repaired = 0;
        var objects = Resources.FindObjectsOfTypeAll<GameObject>();
        for (var i = 0; i < objects.Length; i++)
        {
            var gameObject = objects[i];
            if (gameObject == null || !IsStrayDrawableSuitsObject(gameObject.name))
            {
                continue;
            }

            var camera = gameObject.GetComponent<Camera>();
            if (camera != null && camera.enabled)
            {
                camera.enabled = false;
            }

            DrawableSuitsDiagnostics.Warn($"SessionSafetyCheck destroying stray closed-editor object: name={gameObject.name}; active={gameObject.activeSelf}; scene={gameObject.scene.name}; cameraEnabled={camera?.enabled.ToString() ?? "null"}; targetTexture={camera?.targetTexture?.name ?? "null"}");
            UnityEngine.Object.Destroy(gameObject);
            repaired++;
        }

        return repaired;
    }

    private static bool IsStrayDrawableSuitsObject(string name)
    {
        for (var i = 0; i < StrayObjectNames.Length; i++)
        {
            if (string.Equals(name, StrayObjectNames[i], StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string DescribeCameras()
    {
        var cameras = Resources.FindObjectsOfTypeAll<Camera>();
        if (cameras == null || cameras.Length == 0)
        {
            return "none";
        }

        var parts = new List<string>();
        for (var i = 0; i < cameras.Length && parts.Count < 16; i++)
        {
            var camera = cameras[i];
            if (camera == null)
            {
                continue;
            }

            var go = camera.gameObject;
            if (go == null)
            {
                continue;
            }

            var isDrawable = go.name.IndexOf("DrawableSuits", StringComparison.OrdinalIgnoreCase) >= 0;
            var isLiveSceneObject = go.scene.IsValid() && go.activeInHierarchy;
            if (!isDrawable && !camera.enabled && !isLiveSceneObject)
            {
                continue;
            }

            parts.Add($"{go.name}:enabled={camera.enabled}:active={go.activeInHierarchy}:depth={camera.depth:0.##}:clear={camera.clearFlags}:target={(camera.targetTexture != null ? camera.targetTexture.name : "none")}:mask={camera.cullingMask}");
        }

        return parts.Count > 0 ? string.Join(" | ", parts) : "none";
    }

    private static string DescribeLocalRenderers()
    {
        var player = StartOfRound.Instance?.localPlayerController;
        if (player == null)
        {
            return "localPlayer=null";
        }

        var builder = new StringBuilder();
        AppendRenderer(builder, "body", player.thisPlayerModel);
        AppendRenderer(builder, "lod1", player.thisPlayerModelLOD1);
        AppendRenderer(builder, "lod2", player.thisPlayerModelLOD2);
        AppendRenderer(builder, "arms", player.thisPlayerModelArms);
        return builder.Length > 0 ? builder.ToString() : "none";
    }

    private static void AppendRenderer(StringBuilder builder, string label, Renderer renderer)
    {
        if (builder.Length > 0)
        {
            builder.Append(" | ");
        }

        if (renderer == null)
        {
            builder.Append(label).Append("=null");
            return;
        }

        var material = renderer.sharedMaterial;
        builder.Append(label)
            .Append("=")
            .Append(renderer.name)
            .Append(":enabled=")
            .Append(renderer.enabled)
            .Append(":active=")
            .Append(renderer.gameObject.activeInHierarchy)
            .Append(":layer=")
            .Append(renderer.gameObject.layer)
            .Append(":material=")
            .Append(material != null ? material.name : "null");
    }
}
