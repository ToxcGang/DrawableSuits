using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using GameNetcodeStuff;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace DrawableSuits;

internal sealed class SuitEditorController : MonoBehaviour
{
    private enum EditorTool
    {
        Paint,
        Erase,
        Decal
    }

    private readonly Stack<Color32[]> _undo = new();
    private readonly Stack<Color32[]> _redo = new();
    private readonly List<string> _designFiles = new();
    private readonly List<string> _decalFiles = new();

    private bool _isOpen;
    private Vector2 _cursor;
    private int _selectedSuitId = -1;
    private int _selectedDesignIndex = -1;
    private int _selectedDecalIndex = -1;
    private string _designName = "MyDrawableSuit";
    private EditorTool _tool = EditorTool.Paint;
    private Color _brushColor = Color.red;
    private float _brushSize = 16f;
    private float _brushOpacity = 1f;
    private float _decalSize = 128f;
    private float _decalRotation;
    private bool _strokeActive;
    private string _statusMessage = string.Empty;
    private bool _cursorStateCaptured;
    private bool _previousCursorVisible;
    private CursorLockMode _previousCursorLockState;
    private bool _bootstrapShell;
    private bool _hasEditableSuit;
    private bool _hasLocalPlayer;
    private bool _hasPlayerModel;
    private bool _hasCamera;
    private bool _hasPreviewCollider;
    private bool _canPaint;
    private int _knownSuitCount;

    private GameObject _previewRoot;
    private Mesh _previewMesh;
    private MeshCollider _previewCollider;
    private MeshRenderer _previewRenderer;
    private float _previewYaw;
    private float _previewScale = 0.9f;

    private Texture2D _loadedDecal;

    private GameObject _editorCanvasObject;
    private RectTransform _canvasRect;
    private RectTransform _panelRect;
    private RectTransform _cursorMarker;
    private RectTransform _designListContent;
    private RectTransform _decalListContent;
    private TextMeshProUGUI _suitLabel;
    private TextMeshProUGUI _statusLabel;
    private TextMeshProUGUI _diagnosticsLabel;
    private Text _fallbackDiagnosticsLabel;
    private TextMeshProUGUI _brushSizeLabel;
    private TextMeshProUGUI _brushOpacityLabel;
    private TextMeshProUGUI _decalSizeLabel;
    private TextMeshProUGUI _decalRotationLabel;
    private TMP_InputField _designNameInput;
    private Slider _brushSizeSlider;
    private Slider _brushOpacitySlider;
    private Slider _redSlider;
    private Slider _greenSlider;
    private Slider _blueSlider;
    private Slider _decalSizeSlider;
    private Slider _decalRotationSlider;
    private Image _colorSwatch;
    private Button _paintButton;
    private Button _eraseButton;
    private Button _decalButton;
    private Button _applyButton;
    private Button _saveButton;
    private Button _loadButton;
    private Button _resetButton;

    internal bool IsOpenForDiagnostics => _isOpen;
    internal bool CanvasActiveForDiagnostics => _editorCanvasObject != null && _editorCanvasObject.activeSelf;

    private void Start()
    {
        _cursor = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        RefreshFileLists();
        DrawableSuitsDiagnostics.Info($"SuitEditorController.Start complete. Screen={Screen.width}x{Screen.height}; designFiles={_designFiles.Count}; decalFiles={_decalFiles.Count}");
    }

    private void Update()
    {
        if (!_isOpen)
        {
            return;
        }

        if (UnityEngine.Input.GetKeyDown(KeyCode.Escape) || WasGamepadPressed(g => g.buttonEast))
        {
            CloseEditor();
            return;
        }

        HandleControllerCursor();
        UpdateCursorMarker();
        HandleEditorShortcuts();
        UpdatePreviewTransform();
        HandlePaintingInput();
    }

    public bool OpenEditor()
    {
        return OpenEditor("Direct");
    }

    public bool OpenEditor(string source)
    {
        DrawableSuitsDiagnostics.Info($"OpenEditor requested. source={source}; isOpen={_isOpen}; activeScene={UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}");
        if (_isOpen)
        {
            RefreshEditorReadiness("open request while already open");
            UpdateUiState();
            LogCanvasState("already-open");
            return true;
        }

        if (!EnsureEditorCanvas(out var failureReason))
        {
            DrawableSuitsDiagnostics.Warn(failureReason);
            return false;
        }

        EnsureEventSystem();
        RefreshEditorReadiness($"before show ({source})");
        CaptureAndUnlockCursor();
        _isOpen = true;
        _editorCanvasObject.SetActive(true);
        _cursor = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        RefreshFileLists();
        TryRebuildPreviewForCurrentReadiness(source);
        RefreshEditorReadiness($"after preview ({source})");
        UpdateCursorMarker();
        UpdateUiState();
        LogCanvasState($"opened from {source}");
        DrawableSuitsDiagnostics.Info("DrawableSuits editor overlay opened.");
        return true;
    }

    public void CloseEditor()
    {
        if (!_isOpen)
        {
            return;
        }

        DrawableSuitsDiagnostics.Info("Closing DrawableSuits editor.");
        _isOpen = false;
        if (_editorCanvasObject != null)
        {
            _editorCanvasObject.SetActive(false);
        }

        DestroyPreview();
        _strokeActive = false;
        RestoreCursorState();
    }

    public bool ToggleEditor()
    {
        return ToggleEditor("Direct");
    }

    public bool ToggleEditor(string source)
    {
        if (_isOpen)
        {
            DrawableSuitsDiagnostics.Info($"ToggleEditor closing from source={source}.");
            CloseEditor();
            return true;
        }

        DrawableSuitsDiagnostics.Info($"ToggleEditor opening from source={source}.");
        return OpenEditor(source);
    }

    public void OpenFromPauseMenuNextFrame(QuickMenuManager quickMenu)
    {
        DrawableSuitsDiagnostics.Info($"Pause-menu open scheduled for next frame. quickMenuNull={quickMenu == null}; menuOpen={quickMenu?.isMenuOpen}");
        StartCoroutine(OpenFromPauseMenuNextFrameRoutine(quickMenu));
    }

    private IEnumerator OpenFromPauseMenuNextFrameRoutine(QuickMenuManager quickMenu)
    {
        yield return null;
        DrawableSuitsDiagnostics.Info($"Pause-menu next-frame open running. quickMenuNull={quickMenu == null}; menuOpen={quickMenu?.isMenuOpen}; cursorVisible={Cursor.visible}; cursorLock={Cursor.lockState}");
        OpenEditor("PauseMenuButton");
    }

    private void RefreshEditorReadiness(string context)
    {
        var localSuitId = DrawableSuitsPlugin.Registry.GetLocalSuitId();
        var suitIds = DrawableSuitsPlugin.Registry.GetSuitIds();
        _knownSuitCount = suitIds.Count;
        if (localSuitId >= 0)
        {
            _selectedSuitId = localSuitId;
        }
        else if (_selectedSuitId < 0)
        {
            _selectedSuitId = FirstKnownSuitId();
        }

        var player = StartOfRound.Instance?.localPlayerController;
        _hasLocalPlayer = player != null;
        _hasPlayerModel = player?.thisPlayerModel != null;
        _hasCamera = Camera.main != null;
        _hasPreviewCollider = _previewCollider != null;
        _hasEditableSuit = _selectedSuitId >= 0 && DrawableSuitsPlugin.Registry.GetOrCreateState(_selectedSuitId) != null;
        _canPaint = _hasEditableSuit && _hasPlayerModel && _hasCamera && _hasPreviewCollider;

        SetStatus(BuildReadinessStatus(), false);
        DrawableSuitsDiagnostics.Info($"Readiness[{context}]: selectedSuitId={_selectedSuitId}; suitCount={_knownSuitCount}; hasEditableSuit={_hasEditableSuit}; hasLocalPlayer={_hasLocalPlayer}; hasPlayerModel={_hasPlayerModel}; hasCamera={_hasCamera}; hasPreviewCollider={_hasPreviewCollider}; canPaint={_canPaint}; status='{_statusMessage}'");
    }

    private string BuildReadinessStatus()
    {
        var missing = new List<string>();
        if (_knownSuitCount == 0)
        {
            missing.Add("no suits registered yet");
        }
        if (_selectedSuitId < 0)
        {
            missing.Add("no selected suit");
        }
        if (!_hasEditableSuit)
        {
            missing.Add("selected suit has no editable material/texture");
        }
        if (!_hasLocalPlayer)
        {
            missing.Add("local player not found");
        }
        if (!_hasPlayerModel)
        {
            missing.Add("local player model not loaded");
        }
        if (!_hasCamera)
        {
            missing.Add("main camera not found");
        }
        if (_hasEditableSuit && _hasPlayerModel && _hasCamera && !_hasPreviewCollider)
        {
            missing.Add("preview collider not built yet");
        }

        return missing.Count == 0 ? "Ready." : "Diagnostics: " + string.Join("; ", missing);
    }

    private bool EnsureEditorCanvas(out string failureReason)
    {
        failureReason = string.Empty;
        if (_editorCanvasObject != null)
        {
            LogCanvasState("ensure-existing");
            return true;
        }

        try
        {
            DrawableSuitsDiagnostics.Info("Building DrawableSuits editor UGUI canvas.");
            BuildBootstrapEditorCanvas();
            LogCanvasState("ensure-created");
            return _editorCanvasObject != null;
        }
        catch (Exception ex)
        {
            DrawableSuitsDiagnostics.Exception("BuildEditorCanvas failed; attempting fallback diagnostics canvas", ex);
            if (_editorCanvasObject != null)
            {
                Destroy(_editorCanvasObject);
                _editorCanvasObject = null;
                _canvasRect = null;
                _panelRect = null;
                _cursorMarker = null;
            }

            try
            {
                BuildFallbackDiagnosticsCanvas(ex);
                LogCanvasState("fallback-created");
                return _editorCanvasObject != null;
            }
            catch (Exception fallbackEx)
            {
                DrawableSuitsDiagnostics.Exception("BuildFallbackDiagnosticsCanvas failed", fallbackEx);
                failureReason = $"DrawableSuits editor cannot open: failed to build Unity UI overlay ({ex.Message}); fallback also failed ({fallbackEx.Message}).";
                return false;
            }
        }
    }

    private void BuildBootstrapEditorCanvas()
    {
        _bootstrapShell = true;
        _editorCanvasObject = new GameObject("DrawableSuitsEditorCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        DontDestroyOnLoad(_editorCanvasObject);
        _canvasRect = _editorCanvasObject.GetComponent<RectTransform>();
        _canvasRect.anchorMin = Vector2.zero;
        _canvasRect.anchorMax = Vector2.one;
        _canvasRect.offsetMin = Vector2.zero;
        _canvasRect.offsetMax = Vector2.zero;

        var canvas = _editorCanvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32767;

        var scaler = _editorCanvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var panel = new GameObject("EditorPanelBootstrap", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(_editorCanvasObject.transform, false);
        _panelRect = panel.GetComponent<RectTransform>();
        _panelRect.anchorMin = new Vector2(0f, 1f);
        _panelRect.anchorMax = new Vector2(0f, 1f);
        _panelRect.pivot = new Vector2(0f, 1f);
        _panelRect.anchoredPosition = new Vector2(24f, -24f);
        _panelRect.sizeDelta = new Vector2(650f, 330f);
        panel.GetComponent<Image>().color = new Color(0.02f, 0.025f, 0.03f, 0.96f);

        CreateBootstrapText(panel.transform, $"{PluginInfo.Name} {PluginInfo.Version}", 22, new Rect(16f, -12f, 610f, 34f), new Color(1f, 0.62f, 0.25f, 1f));
        _fallbackDiagnosticsLabel = CreateBootstrapText(panel.transform, string.Empty, 16, new Rect(16f, -54f, 610f, 190f), Color.white);

        var closeButton = CreateBootstrapButton(panel.transform, "Close", new Rect(16f, -266f, 140f, 42f), CloseEditor);
        closeButton.interactable = true;
        var applyButton = CreateBootstrapButton(panel.transform, "Apply disabled", new Rect(168f, -266f, 180f, 42f), null);
        applyButton.interactable = false;

        _cursorMarker = new GameObject("DrawableSuitsCursor", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
        _cursorMarker.transform.SetParent(_editorCanvasObject.transform, false);
        _cursorMarker.sizeDelta = new Vector2(14f, 14f);
        _cursorMarker.GetComponent<Image>().color = Color.white;

        _editorCanvasObject.SetActive(false);
        UpdateUiState();
        DrawableSuitsDiagnostics.Info($"Bootstrap editor canvas built. canvas={DrawableSuitsPlugin.DescribeUnityObject(_editorCanvasObject)}; panel={DrawableSuitsPlugin.DescribeUnityObject(panel)}");
    }

    private static Text CreateBootstrapText(Transform parent, string text, int fontSize, Rect rect, Color color)
    {
        var go = new GameObject("BootstrapText", typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var rectTransform = go.GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0f, 1f);
        rectTransform.anchorMax = new Vector2(0f, 1f);
        rectTransform.pivot = new Vector2(0f, 1f);
        rectTransform.anchoredPosition = new Vector2(rect.x, rect.y);
        rectTransform.sizeDelta = new Vector2(rect.width, rect.height);

        var label = go.GetComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        label.fontSize = fontSize;
        label.color = color;
        label.alignment = TextAnchor.UpperLeft;
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        label.text = text;
        return label;
    }

    private static Button CreateBootstrapButton(Transform parent, string text, Rect rect, Action onClick)
    {
        var go = new GameObject(text + "Button", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rectTransform = go.GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0f, 1f);
        rectTransform.anchorMax = new Vector2(0f, 1f);
        rectTransform.pivot = new Vector2(0f, 1f);
        rectTransform.anchoredPosition = new Vector2(rect.x, rect.y);
        rectTransform.sizeDelta = new Vector2(rect.width, rect.height);

        var image = go.GetComponent<Image>();
        image.color = new Color(0.14f, 0.15f, 0.16f, 0.98f);

        var button = go.GetComponent<Button>();
        button.targetGraphic = image;
        if (onClick != null)
        {
            button.onClick.AddListener(() => onClick());
        }

        var label = CreateBootstrapText(go.transform, text, 16, new Rect(0f, 0f, rect.width, rect.height), Color.white);
        label.alignment = TextAnchor.MiddleCenter;
        return button;
    }

    private void BuildEditorCanvas()
    {
        _editorCanvasObject = new GameObject("DrawableSuitsEditorCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        DontDestroyOnLoad(_editorCanvasObject);
        _canvasRect = _editorCanvasObject.GetComponent<RectTransform>();
        _canvasRect.anchorMin = Vector2.zero;
        _canvasRect.anchorMax = Vector2.one;
        _canvasRect.offsetMin = Vector2.zero;
        _canvasRect.offsetMax = Vector2.zero;

        var canvas = _editorCanvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32767;

        var scaler = _editorCanvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var panel = CreateUiObject("EditorPanel", _editorCanvasObject.transform, typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        _panelRect = panel.GetComponent<RectTransform>();
        _panelRect.anchorMin = new Vector2(0f, 1f);
        _panelRect.anchorMax = new Vector2(0f, 1f);
        _panelRect.pivot = new Vector2(0f, 1f);
        _panelRect.anchoredPosition = new Vector2(24f, -24f);
        _panelRect.sizeDelta = new Vector2(420f, 0f);

        var panelImage = panel.GetComponent<Image>();
        panelImage.color = new Color(0.03f, 0.035f, 0.04f, 0.94f);

        var layout = panel.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 12, 12);
        layout.spacing = 6f;
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandHeight = false;

        var fitter = panel.GetComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        CreateText(panel.transform, "DrawableSuits", 24f, FontStyles.Bold, TextAlignmentOptions.Left, 32f);
        _suitLabel = CreateText(panel.transform, string.Empty, 18f, FontStyles.Normal, TextAlignmentOptions.Left, 28f);
        _statusLabel = CreateText(panel.transform, string.Empty, 15f, FontStyles.Normal, TextAlignmentOptions.Left, 42f);
        _statusLabel.color = new Color(1f, 0.58f, 0.28f, 1f);
        _diagnosticsLabel = CreateText(panel.transform, string.Empty, 13f, FontStyles.Normal, TextAlignmentOptions.Left, 96f);
        _diagnosticsLabel.color = new Color(0.78f, 0.86f, 1f, 1f);

        var suitRow = CreateHorizontalGroup(panel.transform, "SuitRow", 34f);
        CreateButton(suitRow.transform, "Previous", () => SelectAdjacentSuit(-1));
        CreateButton(suitRow.transform, "Use Current", () => SelectSuit(DrawableSuitsPlugin.Registry.GetLocalSuitId()));
        CreateButton(suitRow.transform, "Next", () => SelectAdjacentSuit(1));

        CreateText(panel.transform, "Tool", 16f, FontStyles.Bold, TextAlignmentOptions.Left, 24f);
        var toolRow = CreateHorizontalGroup(panel.transform, "ToolRow", 34f);
        _paintButton = CreateButton(toolRow.transform, "Paint", () => SetTool(EditorTool.Paint));
        _eraseButton = CreateButton(toolRow.transform, "Erase", () => SetTool(EditorTool.Erase));
        _decalButton = CreateButton(toolRow.transform, "Decal", () => SetTool(EditorTool.Decal));

        CreateText(panel.transform, "Brush", 16f, FontStyles.Bold, TextAlignmentOptions.Left, 24f);
        _brushSizeLabel = CreateText(panel.transform, string.Empty, 14f, FontStyles.Normal, TextAlignmentOptions.Left, 20f);
        _brushSizeSlider = CreateSlider(panel.transform, 1f, 96f, _brushSize, value => _brushSize = value);
        _brushOpacityLabel = CreateText(panel.transform, string.Empty, 14f, FontStyles.Normal, TextAlignmentOptions.Left, 20f);
        _brushOpacitySlider = CreateSlider(panel.transform, 0.05f, 1f, _brushOpacity, value => _brushOpacity = value);

        CreateText(panel.transform, "Color", 14f, FontStyles.Bold, TextAlignmentOptions.Left, 20f);
        _redSlider = CreateSlider(panel.transform, 0f, 1f, _brushColor.r, value => { _brushColor.r = value; UpdateColorUi(); });
        _greenSlider = CreateSlider(panel.transform, 0f, 1f, _brushColor.g, value => { _brushColor.g = value; UpdateColorUi(); });
        _blueSlider = CreateSlider(panel.transform, 0f, 1f, _brushColor.b, value => { _brushColor.b = value; UpdateColorUi(); });
        _colorSwatch = CreateColorSwatch(panel.transform);

        CreateText(panel.transform, "Decal", 16f, FontStyles.Bold, TextAlignmentOptions.Left, 24f);
        _decalSizeLabel = CreateText(panel.transform, string.Empty, 14f, FontStyles.Normal, TextAlignmentOptions.Left, 20f);
        _decalSizeSlider = CreateSlider(panel.transform, 16f, 512f, _decalSize, value => _decalSize = value);
        _decalRotationLabel = CreateText(panel.transform, string.Empty, 14f, FontStyles.Normal, TextAlignmentOptions.Left, 20f);
        _decalRotationSlider = CreateSlider(panel.transform, -180f, 180f, _decalRotation, value => _decalRotation = value);
        var decalButtons = CreateHorizontalGroup(panel.transform, "DecalButtons", 34f);
        CreateButton(decalButtons.transform, "Refresh", RefreshFileLists);
        CreateButton(decalButtons.transform, "Import File", ImportDecalFromDialog);
        _decalListContent = CreateScrollList(panel.transform, "DecalList", 82f);

        CreateText(panel.transform, "Design Name", 16f, FontStyles.Bold, TextAlignmentOptions.Left, 24f);
        _designNameInput = CreateInputField(panel.transform, _designName);
        _designNameInput.onValueChanged.AddListener(value => _designName = value);

        var editButtons = CreateHorizontalGroup(panel.transform, "EditButtons", 34f);
        CreateButton(editButtons.transform, "Undo", Undo);
        CreateButton(editButtons.transform, "Redo", Redo);
        _resetButton = CreateButton(editButtons.transform, "Reset", () =>
        {
            SaveUndo();
            DrawableSuitsPlugin.Registry.ResetSuit(_selectedSuitId);
        });

        var applyButtons = CreateHorizontalGroup(panel.transform, "ApplyButtons", 34f);
        _applyButton = CreateButton(applyButtons.transform, "Apply", () => DrawableSuitsPlugin.Registry.ApplyEditedTexture(_selectedSuitId, _designName, true));
        _saveButton = CreateButton(applyButtons.transform, "Save", SaveDesign);
        _loadButton = CreateButton(applyButtons.transform, "Load", LoadSelectedDesign);

        CreateText(panel.transform, "Saved Designs", 16f, FontStyles.Bold, TextAlignmentOptions.Left, 24f);
        _designListContent = CreateScrollList(panel.transform, "DesignList", 92f);

        CreateText(panel.transform, "Controller: View/Back+Y open, left stick cursor, right trigger paint, bumpers rotate, Y tool, X undo, Start save, A apply.", 13f, FontStyles.Normal, TextAlignmentOptions.Left, 48f);

        _cursorMarker = CreateUiObject("DrawableSuitsCursor", _editorCanvasObject.transform, typeof(Image)).GetComponent<RectTransform>();
        _cursorMarker.sizeDelta = new Vector2(14f, 14f);
        _cursorMarker.GetComponent<Image>().color = Color.white;

        _editorCanvasObject.SetActive(false);
        RefreshListButtons();
        UpdateUiState();
        DrawableSuitsDiagnostics.Info($"BuildEditorCanvas complete. childCount={_editorCanvasObject.transform.childCount}; panelChildren={panel.transform.childCount}; graphicRaycaster={_editorCanvasObject.GetComponent<GraphicRaycaster>() != null}");
    }

    private void BuildFallbackDiagnosticsCanvas(Exception originalException)
    {
        _editorCanvasObject = new GameObject("DrawableSuitsEditorCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        DontDestroyOnLoad(_editorCanvasObject);
        _canvasRect = _editorCanvasObject.GetComponent<RectTransform>();
        _canvasRect.anchorMin = Vector2.zero;
        _canvasRect.anchorMax = Vector2.one;
        _canvasRect.offsetMin = Vector2.zero;
        _canvasRect.offsetMax = Vector2.zero;

        var canvas = _editorCanvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32767;

        var scaler = _editorCanvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var panel = new GameObject("EditorPanelFallback", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(_editorCanvasObject.transform, false);
        _panelRect = panel.GetComponent<RectTransform>();
        _panelRect.anchorMin = new Vector2(0f, 1f);
        _panelRect.anchorMax = new Vector2(0f, 1f);
        _panelRect.pivot = new Vector2(0f, 1f);
        _panelRect.anchoredPosition = new Vector2(24f, -24f);
        _panelRect.sizeDelta = new Vector2(560f, 260f);
        panel.GetComponent<Image>().color = new Color(0.03f, 0.035f, 0.04f, 0.96f);

        var textObject = new GameObject("DiagnosticsTextFallback", typeof(RectTransform), typeof(Text));
        textObject.transform.SetParent(panel.transform, false);
        var textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(14f, 14f);
        textRect.offsetMax = new Vector2(-14f, -14f);

        _fallbackDiagnosticsLabel = textObject.GetComponent<Text>();
        _fallbackDiagnosticsLabel.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        _fallbackDiagnosticsLabel.fontSize = 18;
        _fallbackDiagnosticsLabel.color = Color.white;
        _fallbackDiagnosticsLabel.alignment = TextAnchor.UpperLeft;
        _fallbackDiagnosticsLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
        _fallbackDiagnosticsLabel.verticalOverflow = VerticalWrapMode.Overflow;
        _fallbackDiagnosticsLabel.text = $"DrawableSuits diagnostics fallback\nUGUI/TMP editor build failed: {originalException.GetType().Name}: {originalException.Message}";

        _cursorMarker = new GameObject("DrawableSuitsCursor", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
        _cursorMarker.transform.SetParent(_editorCanvasObject.transform, false);
        _cursorMarker.sizeDelta = new Vector2(14f, 14f);
        _cursorMarker.GetComponent<Image>().color = Color.white;
        _editorCanvasObject.SetActive(false);
        DrawableSuitsDiagnostics.Info("Fallback diagnostics canvas built.");
    }

    private static GameObject CreateUiObject(string name, Transform parent, params Type[] components)
    {
        var go = new GameObject(name, components);
        go.transform.SetParent(parent, false);
        return go;
    }

    private static TextMeshProUGUI CreateText(Transform parent, string text, float fontSize, FontStyles style, TextAlignmentOptions alignment, float height)
    {
        var go = CreateUiObject("Text", parent, typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        var label = go.GetComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = fontSize;
        label.fontStyle = style;
        label.alignment = alignment;
        label.color = Color.white;
        label.enableWordWrapping = true;

        var layout = go.GetComponent<LayoutElement>();
        layout.preferredHeight = height;
        layout.minHeight = height;
        return label;
    }

    private static GameObject CreateHorizontalGroup(Transform parent, string name, float height)
    {
        var go = CreateUiObject(name, parent, typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        var group = go.GetComponent<HorizontalLayoutGroup>();
        group.spacing = 6f;
        group.childControlWidth = true;
        group.childForceExpandWidth = true;
        group.childControlHeight = true;
        group.childForceExpandHeight = true;

        var layout = go.GetComponent<LayoutElement>();
        layout.preferredHeight = height;
        layout.minHeight = height;
        return go;
    }

    private static Button CreateButton(Transform parent, string text, Action onClick)
    {
        var go = CreateUiObject(text + "Button", parent, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        var image = go.GetComponent<Image>();
        image.color = new Color(0.14f, 0.15f, 0.16f, 0.98f);

        var button = go.GetComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() => onClick?.Invoke());

        var colors = button.colors;
        colors.normalColor = image.color;
        colors.highlightedColor = new Color(0.24f, 0.26f, 0.28f, 1f);
        colors.pressedColor = new Color(0.34f, 0.36f, 0.38f, 1f);
        colors.selectedColor = colors.highlightedColor;
        button.colors = colors;

        var layout = go.GetComponent<LayoutElement>();
        layout.preferredHeight = 34f;
        layout.minHeight = 30f;

        var label = CreateText(go.transform, text, 15f, FontStyles.Normal, TextAlignmentOptions.Center, 30f);
        var rect = label.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return button;
    }

    private static Slider CreateSlider(Transform parent, float min, float max, float value, Action<float> onValueChanged)
    {
        var go = CreateUiObject("Slider", parent, typeof(RectTransform), typeof(Slider), typeof(LayoutElement));
        var layout = go.GetComponent<LayoutElement>();
        layout.preferredHeight = 22f;
        layout.minHeight = 22f;

        var background = CreateUiObject("Background", go.transform, typeof(RectTransform), typeof(Image));
        var bgRect = background.GetComponent<RectTransform>();
        bgRect.anchorMin = new Vector2(0f, 0.25f);
        bgRect.anchorMax = new Vector2(1f, 0.75f);
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;
        background.GetComponent<Image>().color = new Color(0.08f, 0.08f, 0.09f, 1f);

        var fillArea = CreateUiObject("Fill Area", go.transform, typeof(RectTransform));
        var fillAreaRect = fillArea.GetComponent<RectTransform>();
        fillAreaRect.anchorMin = Vector2.zero;
        fillAreaRect.anchorMax = Vector2.one;
        fillAreaRect.offsetMin = new Vector2(5f, 0f);
        fillAreaRect.offsetMax = new Vector2(-5f, 0f);

        var fill = CreateUiObject("Fill", fillArea.transform, typeof(RectTransform), typeof(Image));
        fill.GetComponent<Image>().color = new Color(0.95f, 0.42f, 0.16f, 1f);

        var handleArea = CreateUiObject("Handle Slide Area", go.transform, typeof(RectTransform));
        var handleAreaRect = handleArea.GetComponent<RectTransform>();
        handleAreaRect.anchorMin = Vector2.zero;
        handleAreaRect.anchorMax = Vector2.one;
        handleAreaRect.offsetMin = new Vector2(8f, 0f);
        handleAreaRect.offsetMax = new Vector2(-8f, 0f);

        var handle = CreateUiObject("Handle", handleArea.transform, typeof(RectTransform), typeof(Image));
        handle.GetComponent<Image>().color = Color.white;
        handle.GetComponent<RectTransform>().sizeDelta = new Vector2(16f, 22f);

        var slider = go.GetComponent<Slider>();
        slider.minValue = min;
        slider.maxValue = max;
        slider.value = value;
        slider.fillRect = fill.GetComponent<RectTransform>();
        slider.handleRect = handle.GetComponent<RectTransform>();
        slider.targetGraphic = handle.GetComponent<Image>();
        slider.onValueChanged.AddListener(v => onValueChanged?.Invoke(v));
        return slider;
    }

    private static Image CreateColorSwatch(Transform parent)
    {
        var go = CreateUiObject("ColorSwatch", parent, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        var layout = go.GetComponent<LayoutElement>();
        layout.preferredHeight = 20f;
        layout.minHeight = 20f;
        return go.GetComponent<Image>();
    }

    private static TMP_InputField CreateInputField(Transform parent, string value)
    {
        var go = CreateUiObject("DesignNameInput", parent, typeof(RectTransform), typeof(Image), typeof(TMP_InputField), typeof(LayoutElement));
        go.GetComponent<Image>().color = new Color(0.08f, 0.085f, 0.09f, 1f);

        var layout = go.GetComponent<LayoutElement>();
        layout.preferredHeight = 34f;
        layout.minHeight = 34f;

        var textObject = CreateUiObject("Text", go.transform, typeof(RectTransform), typeof(TextMeshProUGUI));
        var textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(8f, 2f);
        textRect.offsetMax = new Vector2(-8f, -2f);

        var text = textObject.GetComponent<TextMeshProUGUI>();
        text.fontSize = 16f;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.enableWordWrapping = false;

        var input = go.GetComponent<TMP_InputField>();
        input.textViewport = go.GetComponent<RectTransform>();
        input.textComponent = text;
        input.text = value;
        return input;
    }

    private static RectTransform CreateScrollList(Transform parent, string name, float height)
    {
        var root = CreateUiObject(name, parent, typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(LayoutElement));
        root.GetComponent<Image>().color = new Color(0.06f, 0.065f, 0.07f, 0.9f);
        var rootLayout = root.GetComponent<LayoutElement>();
        rootLayout.preferredHeight = height;
        rootLayout.minHeight = height;

        var viewport = CreateUiObject("Viewport", root.transform, typeof(RectTransform), typeof(Image), typeof(Mask));
        var viewportRect = viewport.GetComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = new Vector2(4f, 4f);
        viewportRect.offsetMax = new Vector2(-4f, -4f);
        viewport.GetComponent<Image>().color = Color.clear;
        viewport.GetComponent<Mask>().showMaskGraphic = false;

        var content = CreateUiObject("Content", viewport.transform, typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        var contentRect = content.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.offsetMin = Vector2.zero;
        contentRect.offsetMax = Vector2.zero;

        var group = content.GetComponent<VerticalLayoutGroup>();
        group.spacing = 4f;
        group.childControlWidth = true;
        group.childForceExpandWidth = true;
        group.childControlHeight = true;
        group.childForceExpandHeight = false;

        var fitter = content.GetComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = root.GetComponent<ScrollRect>();
        scroll.viewport = viewportRect;
        scroll.content = contentRect;
        scroll.horizontal = false;
        scroll.vertical = true;
        return contentRect;
    }

    private static void EnsureEventSystem()
    {
        var existing = FindObjectOfType<EventSystem>();
        if (existing != null)
        {
            DrawableSuitsDiagnostics.Info($"EventSystem already present: {existing.name}; current={EventSystem.current?.name ?? "null"}.");
            return;
        }

        var eventSystem = new GameObject("DrawableSuitsEventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        DontDestroyOnLoad(eventSystem);
        DrawableSuitsDiagnostics.Info("Created fallback DrawableSuitsEventSystem with StandaloneInputModule.");
    }

    private void CaptureAndUnlockCursor()
    {
        if (!_cursorStateCaptured)
        {
            _previousCursorVisible = Cursor.visible;
            _previousCursorLockState = Cursor.lockState;
            _cursorStateCaptured = true;
        }

        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
        DrawableSuitsDiagnostics.Info($"Cursor unlocked for editor. previousVisible={_previousCursorVisible}; previousLock={_previousCursorLockState}; currentVisible={Cursor.visible}; currentLock={Cursor.lockState}");
    }

    private void SetStatus(string message, bool warn)
    {
        _statusMessage = message ?? string.Empty;
        if (_statusLabel != null)
        {
            _statusLabel.text = _statusMessage;
            _statusLabel.gameObject.SetActive(!string.IsNullOrWhiteSpace(_statusMessage));
        }

        if (warn && !string.IsNullOrWhiteSpace(_statusMessage))
        {
            DrawableSuitsDiagnostics.Warn(_statusMessage);
        }
    }

    private void RestoreCursorState()
    {
        if (!_cursorStateCaptured)
        {
            return;
        }

        Cursor.visible = _previousCursorVisible;
        Cursor.lockState = _previousCursorLockState;
        _cursorStateCaptured = false;
        DrawableSuitsDiagnostics.Info($"Cursor restored after editor close. visible={Cursor.visible}; lock={Cursor.lockState}");
    }

    private void UpdateUiState()
    {
        if (_suitLabel != null)
        {
            _suitLabel.text = _selectedSuitId >= 0
                ? $"Suit: {DrawableSuitsPlugin.Registry.GetSuitName(_selectedSuitId)} ({_selectedSuitId})"
                : "Suit: none selected";
        }

        if (_statusLabel != null)
        {
            _statusLabel.text = _statusMessage;
            _statusLabel.gameObject.SetActive(!string.IsNullOrWhiteSpace(_statusMessage));
        }

        if (_designNameInput != null && _designNameInput.text != _designName)
        {
            _designNameInput.text = _designName;
        }

        if (_brushSizeSlider != null) _brushSizeSlider.value = _brushSize;
        if (_brushOpacitySlider != null) _brushOpacitySlider.value = _brushOpacity;
        if (_redSlider != null) _redSlider.value = _brushColor.r;
        if (_greenSlider != null) _greenSlider.value = _brushColor.g;
        if (_blueSlider != null) _blueSlider.value = _brushColor.b;
        if (_decalSizeSlider != null) _decalSizeSlider.value = _decalSize;
        if (_decalRotationSlider != null) _decalRotationSlider.value = _decalRotation;

        if (_diagnosticsLabel != null)
        {
            _diagnosticsLabel.text = BuildDiagnosticsSummary();
        }
        if (_fallbackDiagnosticsLabel != null)
        {
            _fallbackDiagnosticsLabel.text = "DrawableSuits diagnostics\n" + BuildDiagnosticsSummary();
        }

        var hasEditableTexture = _selectedSuitId >= 0 && DrawableSuitsPlugin.Registry.GetEditableTexture(_selectedSuitId) != null;
        SetInteractable(_paintButton, _canPaint);
        SetInteractable(_eraseButton, _canPaint);
        SetInteractable(_decalButton, _canPaint);
        SetInteractable(_applyButton, hasEditableTexture);
        SetInteractable(_saveButton, hasEditableTexture);
        SetInteractable(_resetButton, hasEditableTexture);
        SetInteractable(_loadButton, hasEditableTexture && _selectedDesignIndex >= 0);

        UpdateToolButtons();
        UpdateLabels();
        UpdateColorUi();
        RefreshListButtons();
    }

    private string BuildDiagnosticsSummary()
    {
        var camera = Camera.main;
        return string.Join("\n", new[]
        {
            $"Selected suit id: {_selectedSuitId}",
            $"Suit count: {_knownSuitCount}",
            $"Local player found: {_hasLocalPlayer}",
            $"Player model found: {_hasPlayerModel}",
            $"Main camera found: {_hasCamera} ({(camera != null ? camera.name : "null")})",
            $"Preview collider found: {_hasPreviewCollider}",
            $"Can paint/apply preview: {_canPaint}",
            $"Canvas active: {(_editorCanvasObject != null && _editorCanvasObject.activeSelf)}",
            $"Diagnostics log: {DrawableSuitsDiagnostics.LogPath}"
        });
    }

    private static void SetInteractable(Selectable selectable, bool interactable)
    {
        if (selectable != null)
        {
            selectable.interactable = interactable;
        }
    }

    private void UpdateLabels()
    {
        if (_brushSizeLabel != null) _brushSizeLabel.text = $"Size: {Mathf.RoundToInt(_brushSize)} px";
        if (_brushOpacityLabel != null) _brushOpacityLabel.text = $"Opacity: {Mathf.RoundToInt(_brushOpacity * 100f)}%";
        if (_decalSizeLabel != null) _decalSizeLabel.text = $"Size: {Mathf.RoundToInt(_decalSize)} px";
        if (_decalRotationLabel != null) _decalRotationLabel.text = $"Rotation: {Mathf.RoundToInt(_decalRotation)} deg";
    }

    private void UpdateColorUi()
    {
        if (_colorSwatch != null)
        {
            _colorSwatch.color = _brushColor;
        }
    }

    private void UpdateToolButtons()
    {
        SetToolButtonColor(_paintButton, _tool == EditorTool.Paint);
        SetToolButtonColor(_eraseButton, _tool == EditorTool.Erase);
        SetToolButtonColor(_decalButton, _tool == EditorTool.Decal);
    }

    private static void SetToolButtonColor(Button button, bool selected)
    {
        if (button == null)
        {
            return;
        }

        var image = button.GetComponent<Image>();
        if (image != null)
        {
            image.color = selected ? new Color(0.95f, 0.42f, 0.16f, 1f) : new Color(0.14f, 0.15f, 0.16f, 0.98f);
        }
    }

    private void SetTool(EditorTool tool)
    {
        _tool = tool;
        UpdateToolButtons();
    }

    private void RefreshListButtons()
    {
        if (_designListContent != null)
        {
            RebuildList(_designListContent, _designFiles, _selectedDesignIndex, index => _selectedDesignIndex = index, path => Path.GetFileNameWithoutExtension(path));
        }

        if (_decalListContent != null)
        {
            RebuildList(_decalListContent, _decalFiles, _selectedDecalIndex, SelectDecal, Path.GetFileName);
        }
    }

    private static void RebuildList(RectTransform content, List<string> files, int selectedIndex, Action<int> onSelect, Func<string, string> labelSelector)
    {
        for (var i = content.childCount - 1; i >= 0; i--)
        {
            Destroy(content.GetChild(i).gameObject);
        }

        for (var i = 0; i < files.Count; i++)
        {
            var index = i;
            var button = CreateButton(content, labelSelector(files[i]), () => onSelect(index));
            if (i == selectedIndex)
            {
                var image = button.GetComponent<Image>();
                if (image != null)
                {
                    image.color = new Color(0.95f, 0.42f, 0.16f, 1f);
                }
            }
        }
    }

    private void UpdateCursorMarker()
    {
        if (_cursorMarker == null || _canvasRect == null)
        {
            return;
        }

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, _cursor, null, out var localPoint))
        {
            _cursorMarker.anchoredPosition = localPoint;
        }
    }

    private void HandleControllerCursor()
    {
        var gamepad = Gamepad.current;
        if (gamepad == null)
        {
            _cursor = new Vector2(UnityEngine.Input.mousePosition.x, UnityEngine.Input.mousePosition.y);
            return;
        }

        var delta = gamepad.leftStick.ReadValue() * DrawableSuitsPlugin.ModConfig.ControllerCursorSpeed.Value * Time.unscaledDeltaTime;
        _cursor.x = Mathf.Clamp(_cursor.x + delta.x, 0f, Screen.width);
        _cursor.y = Mathf.Clamp(_cursor.y + delta.y, 0f, Screen.height);
    }

    private void HandleEditorShortcuts()
    {
        if (_bootstrapShell)
        {
            return;
        }

        UpdateLabels();

        var gamepad = Gamepad.current;
        if (gamepad != null)
        {
            if (gamepad.leftShoulder.isPressed)
            {
                _previewYaw -= 90f * Time.unscaledDeltaTime;
            }
            if (gamepad.rightShoulder.isPressed)
            {
                _previewYaw += 90f * Time.unscaledDeltaTime;
            }
            if (gamepad.buttonNorth.wasPressedThisFrame)
            {
                SetTool((EditorTool)(((int)_tool + 1) % Enum.GetValues(typeof(EditorTool)).Length));
            }
            if (gamepad.buttonWest.wasPressedThisFrame)
            {
                Undo();
            }
            if (gamepad.startButton.wasPressedThisFrame)
            {
                SaveDesign();
            }
            if (gamepad.buttonSouth.wasPressedThisFrame)
            {
                DrawableSuitsPlugin.Registry.ApplyEditedTexture(_selectedSuitId, _designName, true);
            }
        }

        if (UnityEngine.Input.GetMouseButton(1))
        {
            _previewYaw += UnityEngine.Input.GetAxis("Mouse X") * 3f;
        }

        var scroll = UnityEngine.Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) > 0.01f)
        {
            if (UnityEngine.Input.GetKey(KeyCode.LeftControl) || UnityEngine.Input.GetKey(KeyCode.RightControl))
            {
                _previewScale = Mathf.Clamp(_previewScale + scroll * 0.05f, 0.35f, 1.8f);
            }
            else
            {
                _brushSize = Mathf.Clamp(_brushSize + scroll * 2f, 1f, 96f);
                if (_brushSizeSlider != null)
                {
                    _brushSizeSlider.value = _brushSize;
                }
            }
        }
    }

    private void HandlePaintingInput()
    {
        if (_bootstrapShell)
        {
            _strokeActive = false;
            return;
        }

        if (!_canPaint)
        {
            _strokeActive = false;
            return;
        }

        var mousePainting = UnityEngine.Input.GetMouseButton(0);
        var gamepadPainting = Gamepad.current?.rightTrigger.ReadValue() > 0.55f;
        var painting = mousePainting || gamepadPainting;

        if (!painting)
        {
            _strokeActive = false;
            return;
        }

        if (IsCursorOverEditorPanel())
        {
            return;
        }

        if (!_strokeActive)
        {
            SaveUndo();
            _redo.Clear();
            _strokeActive = true;
        }

        PaintAtCursor();
    }

    private bool IsCursorOverEditorPanel()
    {
        return _panelRect != null && RectTransformUtility.RectangleContainsScreenPoint(_panelRect, _cursor, null);
    }

    private void PaintAtCursor()
    {
        var texture = DrawableSuitsPlugin.Registry.GetEditableTexture(_selectedSuitId);
        if (texture == null || _previewCollider == null || Camera.main == null)
        {
            RefreshEditorReadiness("paint preflight failed");
            UpdateUiState();
            return;
        }

        var ray = Camera.main.ScreenPointToRay(_cursor);
        if (!_previewCollider.Raycast(ray, out var hit, 20f))
        {
            return;
        }

        var uv = hit.textureCoord;
        switch (_tool)
        {
            case EditorTool.Paint:
                PaintCircle(texture, uv, _brushColor, _brushSize, _brushOpacity);
                break;
            case EditorTool.Erase:
                EraseCircle(texture, uv, _brushSize, _brushOpacity);
                break;
            case EditorTool.Decal:
                ApplyDecal(texture, uv);
                _strokeActive = false;
                break;
        }

        texture.Apply(false, false);
        DrawableSuitsPlugin.Registry.ApplyEditedTexture(_selectedSuitId, _designName, false);
    }

    private void PaintCircle(Texture2D texture, Vector2 uv, Color color, float radius, float opacity)
    {
        var cx = Mathf.RoundToInt(uv.x * (texture.width - 1));
        var cy = Mathf.RoundToInt(uv.y * (texture.height - 1));
        var r = Mathf.RoundToInt(radius);
        var r2 = r * r;
        var xMin = Mathf.Max(0, cx - r);
        var xMax = Mathf.Min(texture.width - 1, cx + r);
        var yMin = Mathf.Max(0, cy - r);
        var yMax = Mathf.Min(texture.height - 1, cy + r);

        for (var y = yMin; y <= yMax; y++)
        {
            for (var x = xMin; x <= xMax; x++)
            {
                var dx = x - cx;
                var dy = y - cy;
                if (dx * dx + dy * dy > r2)
                {
                    continue;
                }

                var falloff = 1f - Mathf.Sqrt(dx * dx + dy * dy) / Mathf.Max(1f, r);
                var existing = texture.GetPixel(x, y);
                texture.SetPixel(x, y, Color.Lerp(existing, color, opacity * Mathf.Clamp01(falloff + 0.25f)));
            }
        }
    }

    private void EraseCircle(Texture2D texture, Vector2 uv, float radius, float opacity)
    {
        var state = DrawableSuitsPlugin.Registry.GetOrCreateState(_selectedSuitId);
        if (state?.BaseTexture == null)
        {
            return;
        }

        var cx = Mathf.RoundToInt(uv.x * (texture.width - 1));
        var cy = Mathf.RoundToInt(uv.y * (texture.height - 1));
        var r = Mathf.RoundToInt(radius);
        var r2 = r * r;
        var xMin = Mathf.Max(0, cx - r);
        var xMax = Mathf.Min(texture.width - 1, cx + r);
        var yMin = Mathf.Max(0, cy - r);
        var yMax = Mathf.Min(texture.height - 1, cy + r);

        for (var y = yMin; y <= yMax; y++)
        {
            for (var x = xMin; x <= xMax; x++)
            {
                var dx = x - cx;
                var dy = y - cy;
                if (dx * dx + dy * dy > r2)
                {
                    continue;
                }

                var existing = texture.GetPixel(x, y);
                var original = state.BaseTexture.GetPixel(x, y);
                texture.SetPixel(x, y, Color.Lerp(existing, original, opacity));
            }
        }
    }

    private void ApplyDecal(Texture2D target, Vector2 uv)
    {
        if (_loadedDecal == null)
        {
            return;
        }

        var centerX = Mathf.RoundToInt(uv.x * (target.width - 1));
        var centerY = Mathf.RoundToInt(uv.y * (target.height - 1));
        var size = Mathf.RoundToInt(_decalSize);
        var half = Mathf.Max(1, size / 2);
        var radians = _decalRotation * Mathf.Deg2Rad;
        var cos = Mathf.Cos(radians);
        var sin = Mathf.Sin(radians);

        for (var y = -half; y <= half; y++)
        {
            for (var x = -half; x <= half; x++)
            {
                var u = (x * cos - y * sin) / size + 0.5f;
                var v = (x * sin + y * cos) / size + 0.5f;
                if (u < 0f || u > 1f || v < 0f || v > 1f)
                {
                    continue;
                }

                var tx = centerX + x;
                var ty = centerY + y;
                if (tx < 0 || tx >= target.width || ty < 0 || ty >= target.height)
                {
                    continue;
                }

                var decalColor = _loadedDecal.GetPixelBilinear(u, v);
                if (decalColor.a <= 0.01f)
                {
                    continue;
                }

                var existing = target.GetPixel(tx, ty);
                target.SetPixel(tx, ty, Color.Lerp(existing, decalColor, decalColor.a * _brushOpacity));
            }
        }
    }

    private void SaveUndo()
    {
        var texture = DrawableSuitsPlugin.Registry.GetEditableTexture(_selectedSuitId);
        if (texture == null)
        {
            return;
        }

        _undo.Push(texture.GetPixels32());
        while (_undo.Count > DrawableSuitsPlugin.ModConfig.MaxUndoStates.Value)
        {
            TrimOldest(_undo);
        }
    }

    private void Undo()
    {
        var texture = DrawableSuitsPlugin.Registry.GetEditableTexture(_selectedSuitId);
        if (texture == null || _undo.Count == 0)
        {
            return;
        }

        _redo.Push(texture.GetPixels32());
        texture.SetPixels32(_undo.Pop());
        texture.Apply(false, false);
        DrawableSuitsPlugin.Registry.ApplyEditedTexture(_selectedSuitId, _designName, false);
    }

    private void Redo()
    {
        var texture = DrawableSuitsPlugin.Registry.GetEditableTexture(_selectedSuitId);
        if (texture == null || _redo.Count == 0)
        {
            return;
        }

        _undo.Push(texture.GetPixels32());
        texture.SetPixels32(_redo.Pop());
        texture.Apply(false, false);
        DrawableSuitsPlugin.Registry.ApplyEditedTexture(_selectedSuitId, _designName, false);
    }

    private static void TrimOldest(Stack<Color32[]> stack)
    {
        var items = stack.ToArray();
        stack.Clear();
        for (var i = items.Length - 2; i >= 0; i--)
        {
            stack.Push(items[i]);
        }
    }

    private void SaveDesign()
    {
        DrawableSuitsDiagnostics.Info($"SaveDesign requested. selectedSuitId={_selectedSuitId}; designName={_designName}");
        if (DrawableSuitsPlugin.Registry.SaveDesign(_selectedSuitId, _designName))
        {
            RefreshFileLists();
            DrawableSuitsDiagnostics.Info("SaveDesign succeeded.");
        }
        else
        {
            DrawableSuitsDiagnostics.Warn("SaveDesign failed; registry returned false.");
        }
    }

    private void LoadSelectedDesign()
    {
        if (_selectedDesignIndex < 0 || _selectedDesignIndex >= _designFiles.Count)
        {
            DrawableSuitsDiagnostics.Warn($"LoadSelectedDesign ignored. selectedDesignIndex={_selectedDesignIndex}; designCount={_designFiles.Count}");
            return;
        }

        DrawableSuitsDiagnostics.Info($"LoadSelectedDesign requested. file={_designFiles[_selectedDesignIndex]}; selectedSuitId={_selectedSuitId}");
        SaveUndo();
        if (DrawableSuitsPlugin.Registry.LoadDesign(_selectedSuitId, _designFiles[_selectedDesignIndex]))
        {
            _designName = Path.GetFileNameWithoutExtension(_designFiles[_selectedDesignIndex]);
            if (_designNameInput != null)
            {
                _designNameInput.text = _designName;
            }
            RefreshEditorReadiness("before load design preview");
            TryRebuildPreviewForCurrentReadiness("LoadSelectedDesign");
            RefreshEditorReadiness("after load design");
            UpdateUiState();
            DrawableSuitsDiagnostics.Info("LoadSelectedDesign succeeded.");
        }
        else
        {
            DrawableSuitsDiagnostics.Warn("LoadSelectedDesign failed; registry returned false.");
        }
    }

    private void RefreshFileLists()
    {
        DrawableSuitsPaths.EnsureCreated();
        _designFiles.Clear();
        _designFiles.AddRange(Directory.GetFiles(DrawableSuitsPaths.Saves, "*.json"));
        _decalFiles.Clear();
        foreach (var file in Directory.GetFiles(DrawableSuitsPaths.Decals))
        {
            if (TextureTools.IsImagePath(file))
            {
                _decalFiles.Add(file);
            }
        }

        _selectedDesignIndex = Mathf.Clamp(_selectedDesignIndex, -1, _designFiles.Count - 1);
        _selectedDecalIndex = Mathf.Clamp(_selectedDecalIndex, -1, _decalFiles.Count - 1);
        RefreshListButtons();
        DrawableSuitsDiagnostics.Info($"RefreshFileLists complete. designCount={_designFiles.Count}; decalCount={_decalFiles.Count}; savesPath={DrawableSuitsPaths.Saves}; decalsPath={DrawableSuitsPaths.Decals}");
    }

    private void ImportDecalFromDialog()
    {
        if (!DrawableSuitsPlugin.ModConfig.EnableOsFileDialog.Value)
        {
            DrawableSuitsDiagnostics.Info("ImportDecalFromDialog ignored because EnableOsFileDialog=false.");
            return;
        }

        if (!WindowsFileDialog.TryOpenImage(out var path) || string.IsNullOrWhiteSpace(path))
        {
            DrawableSuitsDiagnostics.Info("ImportDecalFromDialog cancelled or returned no file.");
            return;
        }

        try
        {
            var destination = Path.Combine(DrawableSuitsPaths.Decals, TextureTools.SanitizeFileName(Path.GetFileName(path)));
            File.Copy(path, destination, true);
            RefreshFileLists();
            var index = _decalFiles.FindIndex(p => string.Equals(p, destination, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                SelectDecal(index);
            }
        }
        catch (Exception ex)
        {
            DrawableSuitsDiagnostics.Exception($"Failed to import decal '{path}'", ex);
        }
    }

    private void SelectDecal(int index)
    {
        if (index < 0 || index >= _decalFiles.Count)
        {
            return;
        }

        _selectedDecalIndex = index;
        if (_loadedDecal != null)
        {
            Destroy(_loadedDecal);
        }

        _loadedDecal = TextureTools.LoadImageFile(_decalFiles[index], DrawableSuitsPlugin.ModConfig.MaxTextureSize.Value);
        SetTool(EditorTool.Decal);
        RefreshListButtons();
        DrawableSuitsDiagnostics.Info($"Selected decal index={index}; file={_decalFiles[index]}; loaded={_loadedDecal != null}");
    }

    private void SelectAdjacentSuit(int direction)
    {
        var ids = DrawableSuitsPlugin.Registry.GetSuitIds();
        if (ids.Count == 0)
        {
            return;
        }

        var index = ids.IndexOf(_selectedSuitId);
        if (index < 0)
        {
            index = 0;
        }
        else
        {
            index = (index + direction + ids.Count) % ids.Count;
        }

        SelectSuit(ids[index]);
    }

    private void SelectSuit(int suitId)
    {
        if (suitId < 0)
        {
            DrawableSuitsDiagnostics.Warn($"SelectSuit ignored invalid suitId={suitId}.");
            return;
        }

        _selectedSuitId = suitId;
        DrawableSuitsPlugin.Registry.GetOrCreateState(_selectedSuitId);
        _undo.Clear();
        _redo.Clear();
        DrawableSuitsDiagnostics.Info($"SelectSuit selected suitId={_selectedSuitId}; name={DrawableSuitsPlugin.Registry.GetSuitName(_selectedSuitId)}");
        RefreshEditorReadiness("after select suit");
        UpdateUiState();
        TryRebuildPreviewForCurrentReadiness("SelectSuit");
        RefreshEditorReadiness("after select suit preview");
        UpdateUiState();
    }

    private int FirstKnownSuitId()
    {
        var ids = DrawableSuitsPlugin.Registry.GetSuitIds();
        return ids.Count > 0 ? ids[0] : -1;
    }

    private void TryRebuildPreviewForCurrentReadiness(string context)
    {
        if (!_hasEditableSuit || !_hasPlayerModel || !_hasCamera)
        {
            DrawableSuitsDiagnostics.Warn($"Skipping preview rebuild [{context}] because dependencies are missing. hasEditableSuit={_hasEditableSuit}; hasPlayerModel={_hasPlayerModel}; hasCamera={_hasCamera}");
            DestroyPreview();
            _hasPreviewCollider = false;
            _canPaint = false;
            return;
        }

        RebuildPreview();
    }

    private void RebuildPreview()
    {
        DestroyPreview();
        var player = StartOfRound.Instance?.localPlayerController;
        var source = player?.thisPlayerModel;
        if (source == null)
        {
            SetStatus("Diagnostics: local player model not loaded; editor shell remains visible.", true);
            DrawableSuitsDiagnostics.Warn("RebuildPreview aborted: local player model is null.");
            return;
        }

        DrawableSuitsDiagnostics.Info($"RebuildPreview baking mesh. selectedSuitId={_selectedSuitId}; sourceRenderer={source.name}; verticesBeforeBake={source.sharedMesh?.vertexCount ?? 0}");
        _previewMesh = new Mesh { name = "DrawableSuitsPreviewMesh" };
        source.BakeMesh(_previewMesh, true);

        _previewRoot = new GameObject("DrawableSuitsSuitPreview");
        _previewRoot.hideFlags = HideFlags.HideAndDontSave;
        _previewRoot.layer = 2;
        _previewRoot.AddComponent<MeshFilter>().sharedMesh = _previewMesh;
        _previewRenderer = _previewRoot.AddComponent<MeshRenderer>();
        _previewRenderer.sharedMaterial = DrawableSuitsPlugin.Registry.GetRuntimeMaterial(_selectedSuitId);
        _previewCollider = _previewRoot.AddComponent<MeshCollider>();
        _previewCollider.sharedMesh = _previewMesh;
        _hasPreviewCollider = _previewCollider != null;
        _canPaint = _hasEditableSuit && _hasPlayerModel && _hasCamera && _hasPreviewCollider;
        if (string.IsNullOrWhiteSpace(_statusMessage) || _statusMessage.StartsWith("Player model unavailable"))
        {
            SetStatus(string.Empty, false);
        }

        UpdatePreviewTransform();
        DrawableSuitsDiagnostics.Info($"RebuildPreview complete. meshVertices={_previewMesh.vertexCount}; collider={_previewCollider != null}; material={_previewRenderer.sharedMaterial?.name ?? "null"}");
    }

    private void UpdatePreviewTransform()
    {
        if (_previewRoot == null || Camera.main == null)
        {
            return;
        }

        var cameraTransform = Camera.main.transform;
        _previewRoot.transform.position = cameraTransform.position
            + cameraTransform.forward * 2.15f
            + cameraTransform.right * 0.55f
            - cameraTransform.up * 0.2f;
        _previewRoot.transform.rotation = Quaternion.LookRotation(-cameraTransform.forward, cameraTransform.up) * Quaternion.Euler(0f, _previewYaw, 0f);
        _previewRoot.transform.localScale = Vector3.one * _previewScale;
    }

    private void DestroyPreview()
    {
        if (_previewRoot != null)
        {
            Destroy(_previewRoot);
            _previewRoot = null;
        }

        if (_previewMesh != null)
        {
            Destroy(_previewMesh);
            _previewMesh = null;
        }

        _previewCollider = null;
        _previewRenderer = null;
        _hasPreviewCollider = false;
        _canPaint = false;
    }

    private void LogCanvasState(string context)
    {
        if (_editorCanvasObject == null)
        {
            DrawableSuitsDiagnostics.Warn($"CanvasState[{context}]: editorCanvasObject=null");
            return;
        }

        var canvas = _editorCanvasObject.GetComponent<Canvas>();
        var raycaster = _editorCanvasObject.GetComponent<GraphicRaycaster>();
        var eventSystem = FindObjectOfType<EventSystem>();
        var panelSize = _panelRect != null ? _panelRect.rect.size.ToString() : "null";
        var canvasSize = _canvasRect != null ? _canvasRect.rect.size.ToString() : "null";
        DrawableSuitsDiagnostics.Info($"CanvasState[{context}]: activeSelf={_editorCanvasObject.activeSelf}; activeInHierarchy={_editorCanvasObject.activeInHierarchy}; canvasMode={canvas?.renderMode.ToString() ?? "null"}; sortingOrder={canvas?.sortingOrder.ToString() ?? "null"}; childCount={_editorCanvasObject.transform.childCount}; canvasSize={canvasSize}; panelSize={panelSize}; raycaster={raycaster != null && raycaster.enabled}; eventSystem={eventSystem?.name ?? "null"}; currentSelected={EventSystem.current?.currentSelectedGameObject?.name ?? "null"}");
    }

    private static bool WasGamepadPressed(Func<Gamepad, ButtonControl> accessor)
    {
        var gamepad = Gamepad.current;
        return gamepad != null && accessor(gamepad).wasPressedThisFrame;
    }
}
