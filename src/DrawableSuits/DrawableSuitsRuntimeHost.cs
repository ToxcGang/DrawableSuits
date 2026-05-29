using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UI;

namespace DrawableSuits;

internal sealed class DrawableSuitsRuntimeHost : MonoBehaviour
{
    private GameObject _hudCanvasObject;
    private Text _hudLabel;
    private bool _hudPinned;
    private bool _firstUpdateLogged;
    private bool _controllerOpenChordWasHeld;
    private float _startupHudUntil;
    private float _lastHudUpdate;

    private bool ShouldShowStartupHud => DrawableSuitsPlugin.ModConfig.ShowStartupDiagnostics.Value && Time.realtimeSinceStartup <= _startupHudUntil;
    private bool ShouldShowHud => _hudPinned || ShouldShowStartupHud;

    private void Awake()
    {
        DrawableSuitsDiagnostics.Info($"RuntimeHost Awake. host={DrawableSuitsPlugin.DescribeUnityObject(this)}; gameObject={DrawableSuitsPlugin.DescribeUnityObject(gameObject)}");
    }

    private void Start()
    {
        _startupHudUntil = Time.realtimeSinceStartup + Mathf.Max(1f, DrawableSuitsPlugin.ModConfig.StartupDiagnosticsSeconds.Value);
        DrawableSuitsDiagnostics.Info($"RuntimeHost Start. startupHudUntil={_startupHudUntil}; showStartupDiagnostics={DrawableSuitsPlugin.ModConfig.ShowStartupDiagnostics.Value}; editor={DrawableSuitsPlugin.DescribeUnityObject(DrawableSuitsPlugin.Editor)}");
        if (ShouldShowHud)
        {
            EnsureHudCanvas();
            UpdateHud(true);
        }
        else
        {
            DrawableSuitsDiagnostics.Info("RuntimeHost startup HUD suppressed by config; press F9 to show the debug HUD.");
        }
    }

    private void Update()
    {
        if (!_firstUpdateLogged)
        {
            _firstUpdateLogged = true;
            DrawableSuitsDiagnostics.Info($"RuntimeHost first Update. frame={Time.frameCount}; scene={UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}; editor={DrawableSuitsPlugin.DescribeUnityObject(DrawableSuitsPlugin.Editor)}");
        }

        DrawableSuitsPlugin.EnsureRuntimeReady("RuntimeHost.Update");
        HandleInput();

        if (ShouldShowHud && _hudCanvasObject == null)
        {
            EnsureHudCanvas();
        }

        if (_hudCanvasObject != null)
        {
            _hudCanvasObject.SetActive(ShouldShowHud);
        }

        if (ShouldShowHud && Time.unscaledTime - _lastHudUpdate >= 0.5f)
        {
            UpdateHud(false);
        }
    }

    private void OnGUI()
    {
        if (!ShouldShowHud)
        {
            return;
        }

        GUI.depth = -32768;
        GUI.Box(new Rect(12f, 12f, 820f, 190f), GUIContent.none);
        GUI.Label(new Rect(20f, 18f, 804f, 178f), BuildHudText());
    }

    private void HandleInput()
    {
        if (DrawableSuitsInput.WasKeyPressed(Key.F9, DrawableSuitsPlugin.ModConfig.DebugOverlayKey.Value))
        {
            _hudPinned = !_hudPinned;
            DrawableSuitsDiagnostics.Info($"RuntimeHost debug HUD toggled. pinned={_hudPinned}; frame={Time.frameCount}");
            EnsureHudCanvas();
            UpdateHud(true);
        }

        if (DrawableSuitsInput.WasKeyPressed(Key.F10, DrawableSuitsPlugin.ModConfig.EmergencyOpenKey.Value))
        {
            _hudPinned = true;
            DrawableSuitsDiagnostics.Info($"RuntimeHost F10 emergency open. editor={DrawableSuitsPlugin.DescribeUnityObject(DrawableSuitsPlugin.Editor)}");
            DrawableSuitsPlugin.RequestOpenEditor("F10Emergency");
            EnsureHudCanvas();
            UpdateHud(true);
        }

        if (DrawableSuitsInput.WasKeyPressed(Key.F8, DrawableSuitsPlugin.ModConfig.OpenEditorKey.Value))
        {
            DrawableSuitsDiagnostics.Info($"RuntimeHost F8 toggle. editor={DrawableSuitsPlugin.DescribeUnityObject(DrawableSuitsPlugin.Editor)}");
            RequestGameplayToggle("F8Keyboard");
            UpdateHud(true);
        }

        if (WasControllerOpenChordPressed())
        {
            DrawableSuitsDiagnostics.Info($"RuntimeHost controller open chord. editor={DrawableSuitsPlugin.DescribeUnityObject(DrawableSuitsPlugin.Editor)}");
            RequestGameplayToggle("ControllerViewBackY");
            UpdateHud(true);
        }
    }

    private static void RequestGameplayToggle(string source)
    {
        if (DrawableSuitsPlugin.Editor?.IsOpenForDiagnostics == true)
        {
            DrawableSuitsPlugin.RequestToggleEditor(source);
            return;
        }

        if (!DrawableSuitsPlugin.HasGameplayEditorContext())
        {
            DrawableSuitsDiagnostics.Info($"RuntimeHost ignored {source} open because no gameplay player/camera context is available. Use F10 for diagnostics.");
            return;
        }

        DrawableSuitsPlugin.RequestToggleEditor(source);
    }

    private void EnsureHudCanvas()
    {
        if (_hudCanvasObject != null)
        {
            return;
        }

        try
        {
            _hudCanvasObject = new GameObject("DrawableSuitsBootstrapHud", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            DontDestroyOnLoad(_hudCanvasObject);

            var rect = _hudCanvasObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var canvas = _hudCanvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32766;

            var scaler = _hudCanvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var panel = new GameObject("HudPanel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(_hudCanvasObject.transform, false);
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0f, 1f);
            panelRect.anchorMax = new Vector2(0f, 1f);
            panelRect.pivot = new Vector2(0f, 1f);
            panelRect.anchoredPosition = new Vector2(16f, -16f);
            panelRect.sizeDelta = new Vector2(820f, 190f);
            panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.82f);

            var labelObject = new GameObject("HudText", typeof(RectTransform), typeof(Text));
            labelObject.transform.SetParent(panel.transform, false);
            var labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(12f, 10f);
            labelRect.offsetMax = new Vector2(-12f, -10f);

            _hudLabel = labelObject.GetComponent<Text>();
            _hudLabel.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            _hudLabel.fontSize = 18;
            _hudLabel.color = new Color(1f, 0.78f, 0.42f, 1f);
            _hudLabel.alignment = TextAnchor.UpperLeft;
            _hudLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
            _hudLabel.verticalOverflow = VerticalWrapMode.Overflow;

            _hudCanvasObject.SetActive(ShouldShowHud);
            DrawableSuitsDiagnostics.Info($"RuntimeHost HUD canvas created. hud={DrawableSuitsPlugin.DescribeUnityObject(_hudCanvasObject)}");
        }
        catch (Exception ex)
        {
            DrawableSuitsDiagnostics.Exception("RuntimeHost HUD canvas creation failed; OnGUI fallback remains active", ex);
            if (_hudCanvasObject != null)
            {
                Destroy(_hudCanvasObject);
                _hudCanvasObject = null;
            }
        }
    }

    private void UpdateHud(bool forceLog)
    {
        _lastHudUpdate = Time.unscaledTime;
        var text = BuildHudText();
        if (_hudLabel != null)
        {
            _hudLabel.text = text;
        }

        if (forceLog)
        {
            DrawableSuitsDiagnostics.Info("RuntimeHostHudState: " + text.Replace("\n", " | "));
        }
    }

    private string BuildHudText()
    {
        var quickMenu = FindObjectOfType<QuickMenuManager>();
        return
            $"{PluginInfo.Name} {PluginInfo.Version} runtime active | F8 toggle | F10 open-only | F9 HUD\n" +
            $"Frame={Time.frameCount} Scene={UnityEngine.SceneManagement.SceneManager.GetActiveScene().name} StartupHud={ShouldShowStartupHud} Pinned={_hudPinned}\n" +
            $"Plugin={DrawableSuitsPlugin.DescribeUnityObject(DrawableSuitsPlugin.Instance)} Host={DrawableSuitsPlugin.DescribeUnityObject(DrawableSuitsPlugin.RuntimeHost)}\n" +
            $"Registry={DrawableSuitsPlugin.DescribeUnityObject(DrawableSuitsPlugin.Registry)} Sync={DrawableSuitsPlugin.DescribeUnityObject(DrawableSuitsPlugin.Sync)}\n" +
            $"Editor={DrawableSuitsPlugin.DescribeUnityObject(DrawableSuitsPlugin.Editor)} EditorOpen={DrawableSuitsPlugin.Editor?.IsOpenForDiagnostics.ToString() ?? "null"} Canvas={DrawableSuitsPlugin.Editor?.CanvasActiveForDiagnostics.ToString() ?? "null"}\n" +
            $"QuickMenu={DrawableSuitsPlugin.DescribeUnityObject(quickMenu)} QuickMenuOpen={quickMenu?.isMenuOpen.ToString() ?? "null"} EventSystem={EventSystem.current?.name ?? "null"} Camera={Camera.main?.name ?? "null"}\n" +
            "Log=BepInEx/config/DrawableSuits/Logs/diagnostics.log";
    }

    private bool WasControllerOpenChordPressed()
    {
        var gamepad = Gamepad.current;
        if (gamepad == null)
        {
            _controllerOpenChordWasHeld = false;
            return false;
        }

        var menuButton = gamepad.selectButton ?? gamepad.TryGetChildControl<ButtonControl>("backButton");
        var chordHeld = menuButton != null && menuButton.isPressed && gamepad.buttonNorth.isPressed;
        var pressed = chordHeld && !_controllerOpenChordWasHeld;
        _controllerOpenChordWasHeld = chordHeld;
        return pressed;
    }
}