using System;
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
    private bool _controllerOpenChordWasHeld;

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

    private void Start()
    {
        _cursor = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        RefreshFileLists();
        EnsureEditorCanvas(out _);
    }

    private void Update()
    {
        if (WasOpenShortcutPressed())
        {
            DrawableSuitsPlugin.ModLogger.LogInfo("DrawableSuits editor toggle requested from fallback shortcut.");
            ToggleEditor();
        }

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
        DrawableSuitsPlugin.ModLogger.LogInfo("DrawableSuits editor open requested.");
        if (_isOpen)
        {
            return true;
        }

        if (!PrepareEditorForOpen(out var failureReason))
        {
            DrawableSuitsPlugin.ModLogger.LogWarning(failureReason);
            return false;
        }

        if (!EnsureEditorCanvas(out failureReason))
        {
            DrawableSuitsPlugin.ModLogger.LogWarning(failureReason);
            return false;
        }

        EnsureEventSystem();
        CaptureAndUnlockCursor();
        _isOpen = true;
        _editorCanvasObject.SetActive(true);
        _cursor = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        RefreshFileLists();
        UpdateUiState();
        RebuildPreview();
        UpdateCursorMarker();
        DrawableSuitsPlugin.ModLogger.LogInfo("DrawableSuits editor opened.");
        return true;
    }

    public void CloseEditor()
    {
        if (!_isOpen)
        {
            return;
        }

        DrawableSuitsPlugin.ModLogger.LogInfo("Closing DrawableSuits editor.");
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
        if (_isOpen)
        {
            CloseEditor();
            return true;
        }

        return OpenEditor();
    }

    private bool PrepareEditorForOpen(out string failureReason)
    {
        failureReason = string.Empty;
        var localSuitId = DrawableSuitsPlugin.Registry.GetLocalSuitId();
        _selectedSuitId = localSuitId >= 0 ? localSuitId : FirstKnownSuitId();
        if (_selectedSuitId < 0)
        {
            failureReason = "DrawableSuits editor cannot open: no editable suit is available. Join a lobby and equip a suit first.";
            SetStatus(failureReason, false);
            return false;
        }

        if (DrawableSuitsPlugin.Registry.GetOrCreateState(_selectedSuitId) == null)
        {
            failureReason = "DrawableSuits editor cannot open: the selected suit does not expose an editable suit material.";
            SetStatus(failureReason, false);
            return false;
        }

        var player = StartOfRound.Instance?.localPlayerController;
        if (player?.thisPlayerModel == null)
        {
            failureReason = "DrawableSuits editor cannot open: local player model is not loaded yet.";
            SetStatus(failureReason, false);
            return false;
        }

        SetStatus(string.Empty, false);
        return true;
    }

    private bool EnsureEditorCanvas(out string failureReason)
    {
        failureReason = string.Empty;
        if (_editorCanvasObject != null)
        {
            return true;
        }

        try
        {
            BuildEditorCanvas();
            return _editorCanvasObject != null;
        }
        catch (Exception ex)
        {
            failureReason = $"DrawableSuits editor cannot open: failed to build Unity UI overlay ({ex.Message}).";
            return false;
        }
    }

    private void BuildEditorCanvas()
    {
        _editorCanvasObject = new GameObject("DrawableSuitsEditorCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        _editorCanvasObject.transform.SetParent(transform, false);
        _canvasRect = _editorCanvasObject.GetComponent<RectTransform>();

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
        CreateButton(editButtons.transform, "Reset", () =>
        {
            SaveUndo();
            DrawableSuitsPlugin.Registry.ResetSuit(_selectedSuitId);
        });

        var applyButtons = CreateHorizontalGroup(panel.transform, "ApplyButtons", 34f);
        CreateButton(applyButtons.transform, "Apply", () => DrawableSuitsPlugin.Registry.ApplyEditedTexture(_selectedSuitId, _designName, true));
        CreateButton(applyButtons.transform, "Save", SaveDesign);
        CreateButton(applyButtons.transform, "Load", LoadSelectedDesign);

        CreateText(panel.transform, "Saved Designs", 16f, FontStyles.Bold, TextAlignmentOptions.Left, 24f);
        _designListContent = CreateScrollList(panel.transform, "DesignList", 92f);

        CreateText(panel.transform, "Controller: View/Back+Y open, left stick cursor, right trigger paint, bumpers rotate, Y tool, X undo, Start save, A apply.", 13f, FontStyles.Normal, TextAlignmentOptions.Left, 48f);

        _cursorMarker = CreateUiObject("DrawableSuitsCursor", _editorCanvasObject.transform, typeof(Image)).GetComponent<RectTransform>();
        _cursorMarker.sizeDelta = new Vector2(14f, 14f);
        _cursorMarker.GetComponent<Image>().color = Color.white;

        _editorCanvasObject.SetActive(false);
        RefreshListButtons();
        UpdateUiState();
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
        if (FindObjectOfType<EventSystem>() != null)
        {
            return;
        }

        var eventSystem = new GameObject("DrawableSuitsEventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        DontDestroyOnLoad(eventSystem);
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
            DrawableSuitsPlugin.ModLogger.LogWarning(_statusMessage);
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
    }

    private void UpdateUiState()
    {
        if (_suitLabel != null)
        {
            _suitLabel.text = $"Suit: {DrawableSuitsPlugin.Registry.GetSuitName(_selectedSuitId)} ({_selectedSuitId})";
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

        UpdateToolButtons();
        UpdateLabels();
        UpdateColorUi();
        RefreshListButtons();
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
        if (DrawableSuitsPlugin.Registry.SaveDesign(_selectedSuitId, _designName))
        {
            RefreshFileLists();
        }
    }

    private void LoadSelectedDesign()
    {
        if (_selectedDesignIndex < 0 || _selectedDesignIndex >= _designFiles.Count)
        {
            return;
        }

        SaveUndo();
        if (DrawableSuitsPlugin.Registry.LoadDesign(_selectedSuitId, _designFiles[_selectedDesignIndex]))
        {
            _designName = Path.GetFileNameWithoutExtension(_designFiles[_selectedDesignIndex]);
            if (_designNameInput != null)
            {
                _designNameInput.text = _designName;
            }
            RebuildPreview();
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
    }

    private void ImportDecalFromDialog()
    {
        if (!DrawableSuitsPlugin.ModConfig.EnableOsFileDialog.Value)
        {
            return;
        }

        if (!WindowsFileDialog.TryOpenImage(out var path) || string.IsNullOrWhiteSpace(path))
        {
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
            DrawableSuitsPlugin.ModLogger.LogWarning($"Failed to import decal '{path}': {ex.Message}");
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
            return;
        }

        _selectedSuitId = suitId;
        DrawableSuitsPlugin.Registry.GetOrCreateState(_selectedSuitId);
        _undo.Clear();
        _redo.Clear();
        UpdateUiState();
        RebuildPreview();
    }

    private int FirstKnownSuitId()
    {
        var ids = DrawableSuitsPlugin.Registry.GetSuitIds();
        return ids.Count > 0 ? ids[0] : -1;
    }

    private void RebuildPreview()
    {
        DestroyPreview();
        var player = StartOfRound.Instance?.localPlayerController;
        var source = player?.thisPlayerModel;
        if (source == null)
        {
            SetStatus("Player model unavailable. Open the editor after joining a game.", true);
            return;
        }

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
        if (string.IsNullOrWhiteSpace(_statusMessage) || _statusMessage.StartsWith("Player model unavailable"))
        {
            SetStatus(string.Empty, false);
        }

        UpdatePreviewTransform();
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
    }

    private bool WasOpenShortcutPressed()
    {
        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard.f8Key.wasPressedThisFrame)
        {
            return true;
        }

        try
        {
            if (UnityEngine.Input.GetKeyDown(DrawableSuitsPlugin.ModConfig.OpenEditorKey.Value))
            {
                return true;
            }
        }
        catch (Exception ex)
        {
            DrawableSuitsPlugin.ModLogger.LogDebug($"Legacy shortcut polling failed: {ex.Message}");
        }

        return WasControllerOpenPressed();
    }

    private bool WasControllerOpenPressed()
    {
        var gamepad = Gamepad.current;
        if (gamepad == null)
        {
            _controllerOpenChordWasHeld = false;
            return false;
        }

        var chordHeld = gamepad.selectButton.isPressed && gamepad.buttonNorth.isPressed;
        var pressed = chordHeld && !_controllerOpenChordWasHeld;
        _controllerOpenChordWasHeld = chordHeld;
        return pressed;
    }

    private static bool WasGamepadPressed(Func<Gamepad, ButtonControl> accessor)
    {
        var gamepad = Gamepad.current;
        return gamepad != null && accessor(gamepad).wasPressedThisFrame;
    }
}
