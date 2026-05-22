using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
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

            JetpackWarningCompatibilityGuard.CheckAndRepair(reason, forceLog || repaired > 0);

            var shouldLog = forceLog || repaired > 0 || Time.unscaledTime - _lastFullLogTime >= 2.5f;
            if (!shouldLog)
            {
                return;
            }

            _lastFullLogTime = Time.unscaledTime;
            var scene = SceneManager.GetActiveScene();
            var localPlayer = GetLocalPlayer();
            DrawableSuitsDiagnostics.Info(
                $"SessionSafetyCheck reason={reason}; scene={scene.name}; editorOpen={editorOpen}; repaired={repaired}; " +
                $"mainCamera={DescribeCamera(Camera.main)}; activeCameras=[{DescribeCameras()}]; " +
                $"localPlayer=[{DescribeLocalPlayer(localPlayer)}]; prompt=[{DescribePrompt(localPlayer)}]; " +
                $"localRenderers=[{DescribeLocalRenderers(localPlayer)}]; jetpackWarningGuard={JetpackWarningCompatibilityGuard.Status}");
        }
        catch (Exception ex)
        {
            DrawableSuitsDiagnostics.Exception($"SessionSafetyCheck failed. reason={reason}", ex);
        }
    }

    private static PlayerControllerB GetLocalPlayer()
    {
        var roundPlayer = StartOfRound.Instance?.localPlayerController;
        if (roundPlayer != null)
        {
            return roundPlayer;
        }

        try
        {
            return UnityEngine.Object.FindObjectOfType<GameNetworkManager>()?.localPlayerController;
        }
        catch
        {
            return null;
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
        for (var i = 0; i < cameras.Length && parts.Count < 24; i++)
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

            parts.Add(DescribeCamera(camera));
        }

        return parts.Count > 0 ? string.Join(" | ", parts) : "none";
    }

    private static string DescribeCamera(Camera camera)
    {
        if (camera == null)
        {
            return "null";
        }

        var go = camera.gameObject;
        return $"{go.name}:enabled={camera.enabled}:active={go.activeInHierarchy}:scene={go.scene.name}:depth={camera.depth:0.##}:clear={camera.clearFlags}:target={(camera.targetTexture != null ? camera.targetTexture.name : "none")}:mask={camera.cullingMask}:near={camera.nearClipPlane:0.###}:far={camera.farClipPlane:0.###}";
    }

    private static string DescribeLocalPlayer(PlayerControllerB player)
    {
        if (player == null)
        {
            return "null";
        }

        return $"{player.name}:pos={FormatVector(player.transform.position)}:moveDisabled={player.disableMoveInput}:lookDisabled={player.disableLookInput}:interactDisabled={player.disableInteract}:specialMenu={player.inSpecialMenu}:controlled={DescribeMember(player, "isPlayerControlled")}:dead={DescribeMember(player, "isPlayerDead")}";
    }

    private static string DescribePrompt(PlayerControllerB player)
    {
        if (player == null)
        {
            return "localPlayer=null";
        }

        var builder = new StringBuilder();
        AppendMember(builder, player, "hoveringOverTrigger");
        AppendMember(builder, player, "currentlyGrabbingObject");
        AppendMember(builder, player, "currentlyHeldObjectServer");
        AppendMember(builder, player, "currentlyHeldObject");
        AppendMember(builder, player, "isGrabbingObjectAnimation");
        AppendMember(builder, player, "isTypingChat");
        return builder.Length > 0 ? builder.ToString() : "none";
    }

    private static string DescribeLocalRenderers(PlayerControllerB player)
    {
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

    private static void AppendMember(StringBuilder builder, object target, string memberName)
    {
        var value = DescribeMember(target, memberName);
        if (string.Equals(value, "<missing>", StringComparison.Ordinal))
        {
            return;
        }

        if (builder.Length > 0)
        {
            builder.Append(" | ");
        }

        builder.Append(memberName).Append("=").Append(value);
    }

    private static string DescribeMember(object target, string memberName)
    {
        if (target == null)
        {
            return "null";
        }

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        try
        {
            var type = target.GetType();
            var field = type.GetField(memberName, flags);
            if (field != null)
            {
                return FormatObject(field.GetValue(target));
            }

            var property = type.GetProperty(memberName, flags);
            if (property != null && property.GetIndexParameters().Length == 0)
            {
                return FormatObject(property.GetValue(target, null));
            }
        }
        catch (Exception ex)
        {
            return $"<error:{ex.GetType().Name}>";
        }

        return "<missing>";
    }

    private static string FormatObject(object value)
    {
        if (value == null)
        {
            return "null";
        }

        if (value is UnityEngine.Object unityObject)
        {
            return DrawableSuitsPlugin.DescribeUnityObject(unityObject);
        }

        if (value is Vector3 vector3)
        {
            return FormatVector(vector3);
        }

        if (value is Vector2 vector2)
        {
            return $"({vector2.x:0.###},{vector2.y:0.###})";
        }

        return value.ToString();
    }

    private static string FormatVector(Vector3 value)
    {
        return $"({value.x:0.###},{value.y:0.###},{value.z:0.###})";
    }
}
