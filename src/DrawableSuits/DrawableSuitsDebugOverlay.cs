using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace DrawableSuits;

internal sealed class DrawableSuitsDebugOverlay : MonoBehaviour
{
    private GameObject _canvasObject;
    private Text _label;
    private bool _hudPinned;
    private float _lastLabelUpdate;
    private int _lastLoggedFrame = -1;

    private bool ShouldShowStartupHud =>
        DrawableSuitsPlugin.ModConfig.ShowStartupDiagnostics.Value
        && Time.realtimeSinceStartup <= DrawableSuitsPlugin.ModConfig.StartupDiagnosticsSeconds.Value;

    private bool ShouldShowHud => _hudPinned || ShouldShowStartupHud;

    private void Start()
    {
        DrawableSuitsDiagnostics.Info("DrawableSuitsDebugOverlay.Start called.");
        EnsureCanvas();
        UpdateLabel(true);
    }

    private void Update()
    {
        if (WasDebugOverlayTogglePressed())
        {
            _hudPinned = !_hudPinned;
            DrawableSuitsDiagnostics.Info($"Debug HUD toggled. pinned={_hudPinned}; frame={Time.frameCount}");
            EnsureCanvas();
            UpdateLabel(true);
        }

        if (WasEmergencyOpenPressed())
        {
            DrawableSuitsDiagnostics.Info($"Debug overlay detected emergency open key. editorNull={DrawableSuitsPlugin.Editor == null}; frame={Time.frameCount}");
            EnsureCanvas();
            _hudPinned = true;
            DrawableSuitsPlugin.Editor?.OpenEditor("DebugOverlayEmergencyKey");
            UpdateLabel(true);
        }

        if (_canvasObject != null)
        {
            _canvasObject.SetActive(ShouldShowHud);
        }

        if (ShouldShowHud && Time.unscaledTime - _lastLabelUpdate >= 0.5f)
        {
            UpdateLabel(false);
        }
    }

    private void OnGUI()
    {
        if (!ShouldShowHud)
        {
            return;
        }

        GUI.depth = -32768;
        var text = BuildText();
        var rect = new Rect(12f, 12f, 760f, 190f);
        GUI.Box(rect, GUIContent.none);
        GUI.Label(new Rect(20f, 18f, 744f, 178f), text);
    }

    private void EnsureCanvas()
    {
        if (_canvasObject != null)
        {
            return;
        }

        try
        {
            _canvasObject = new GameObject("DrawableSuitsDebugOverlayCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            DontDestroyOnLoad(_canvasObject);
            var rect = _canvasObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var canvas = _canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32766;

            var scaler = _canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var panel = new GameObject("DebugPanel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(_canvasObject.transform, false);
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0f, 1f);
            panelRect.anchorMax = new Vector2(0f, 1f);
            panelRect.pivot = new Vector2(0f, 1f);
            panelRect.anchoredPosition = new Vector2(16f, -16f);
            panelRect.sizeDelta = new Vector2(760f, 190f);
            panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.82f);

            var labelObject = new GameObject("DebugText", typeof(RectTransform), typeof(Text));
            labelObject.transform.SetParent(panel.transform, false);
            var labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(12f, 10f);
            labelRect.offsetMax = new Vector2(-12f, -10f);

            _label = labelObject.GetComponent<Text>();
            _label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            _label.fontSize = 18;
            _label.alignment = TextAnchor.UpperLeft;
            _label.color = new Color(1f, 0.78f, 0.42f, 1f);
            _label.horizontalOverflow = HorizontalWrapMode.Wrap;
            _label.verticalOverflow = VerticalWrapMode.Overflow;

            _canvasObject.SetActive(ShouldShowHud);
            DrawableSuitsDiagnostics.Info("Debug overlay canvas created.");
        }
        catch (Exception ex)
        {
            DrawableSuitsDiagnostics.Exception("Debug overlay canvas creation failed; OnGUI fallback remains active", ex);
            if (_canvasObject != null)
            {
                Destroy(_canvasObject);
                _canvasObject = null;
            }
        }
    }

    private void UpdateLabel(bool forceLog)
    {
        _lastLabelUpdate = Time.unscaledTime;
        var text = BuildText();
        if (_label != null)
        {
            _label.text = text;
        }

        if (forceLog || Time.frameCount - _lastLoggedFrame > 600)
        {
            _lastLoggedFrame = Time.frameCount;
            DrawableSuitsDiagnostics.Info("DebugOverlayState: " + text.Replace("\n", " | "));
        }
    }

    private string BuildText()
    {
        var editor = DrawableSuitsPlugin.Editor;
        var quickMenu = FindObjectOfType<QuickMenuManager>();
        var eventSystem = EventSystem.current;
        var camera = Camera.main;
        return
            $"{PluginInfo.Name} {PluginInfo.Version} loaded | F8 editor | F10 force editor | F9 debug HUD\n" +
            $"Frame={Time.frameCount} Scene={UnityEngine.SceneManagement.SceneManager.GetActiveScene().name} StartupHud={ShouldShowStartupHud} Pinned={_hudPinned}\n" +
            $"EditorNull={editor == null} EditorOpen={editor?.IsOpenForDiagnostics.ToString() ?? "null"} EditorCanvas={editor?.CanvasActiveForDiagnostics.ToString() ?? "null"}\n" +
            $"QuickMenuNull={quickMenu == null} QuickMenuOpen={quickMenu?.isMenuOpen.ToString() ?? "null"} EventSystem={eventSystem?.name ?? "null"} Camera={camera?.name ?? "null"}\n" +
            $"Log={DrawableSuitsDiagnostics.LogPath}";
    }

    private bool WasDebugOverlayTogglePressed()
    {
        return WasKeyboardKeyPressed(Key.F9, DrawableSuitsPlugin.ModConfig.DebugOverlayKey.Value);
    }

    private bool WasEmergencyOpenPressed()
    {
        return WasKeyboardKeyPressed(Key.F10, DrawableSuitsPlugin.ModConfig.EmergencyOpenKey.Value);
    }

    private static bool WasKeyboardKeyPressed(Key inputSystemKey, KeyCode legacyKey)
    {
        try
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard[inputSystemKey].wasPressedThisFrame)
            {
                return true;
            }
        }
        catch (Exception ex)
        {
            DrawableSuitsDiagnostics.Exception($"Input System key polling failed for {inputSystemKey}", ex);
        }

        try
        {
            return UnityEngine.Input.GetKeyDown(legacyKey);
        }
        catch (Exception ex)
        {
            DrawableSuitsDiagnostics.Exception($"Legacy key polling failed for {legacyKey}", ex);
            return false;
        }
    }
}
