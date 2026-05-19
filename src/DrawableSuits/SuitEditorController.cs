using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using GameNetcodeStuff;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace DrawableSuits;

internal sealed class SuitEditorController : MonoBehaviour
{
    private enum EditorCloseReason
    {
        Normal,
        Shortcut,
        PauseMenu,
        SceneChange,
        PluginDestroy
    }

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
    private bool _playerInputStateCaptured;
    private bool _previousDisableMoveInput;
    private bool _previousDisableLookInput;
    private bool _previousDisableInteract;
    private bool _previousInSpecialMenu;
    private bool _previousMovementActionsEnabled;
    private PlayerControllerB _inputLockedPlayer;
    private bool _bootstrapShell;
    private bool _hasEditableSuit;
    private bool _hasLocalPlayer;
    private bool _hasPlayerModel;
    private bool _hasCamera;
    private bool _hasPreviewCollider;
    private bool _canPaint;
    private int _knownSuitCount;

    private GameObject _previewRoot;
    private GameObject _previewRigRoot;
    private GameObject _previewPivotRoot;
    private Camera _previewCamera;
    private Light _previewLight;
    private RenderTexture _previewTexture;
    private Material _previewMaterial;
    private Mesh _previewMesh;
    private MeshCollider _previewCollider;
    private MeshRenderer _previewRenderer;
    private int _previewLayer = 2;
    private float _previewYaw;
    private float _previewScale = 0.9f;
    private Texture2D _checkerTexture;
    private bool _usingTexturePreview = true;
    private string _previewMode = "Texture";
    private string _lastPreviewAssignmentLog = string.Empty;
    private Vector2 _lastPreviewUv;
    private bool _lastPreviewUvAvailable;

    private Texture2D _loadedDecal;

    private GameObject _editorCanvasObject;
    private RectTransform _canvasRect;
    private RectTransform _panelRect;
    private RectTransform _previewViewportRect;
    private RectTransform _cursorMarker;
    private RectTransform _designListContent;
    private RectTransform _decalListContent;
    private RawImage _previewImage;
    private Text _suitLabel;
    private Text _statusLabel;
    private Text _diagnosticsLabel;
    private Text _fallbackDiagnosticsLabel;
    private Text _brushSizeLabel;
    private Text _brushOpacityLabel;
    private Text _decalSizeLabel;
    private Text _decalRotationLabel;
    private InputField _designNameInput;
    private DrawableSliderControl _brushSizeSlider;
    private DrawableSliderControl _brushOpacitySlider;
    private DrawableSliderControl _redSlider;
    private DrawableSliderControl _greenSlider;
    private DrawableSliderControl _blueSlider;
    private DrawableSliderControl _decalSizeSlider;
    private DrawableSliderControl _decalRotationSlider;
    private Image _colorSwatch;
    private Button _paintButton;
    private Button _eraseButton;
    private Button _decalButton;
    private Button _applyButton;
    private Button _saveButton;
    private Button _loadButton;
    private Button _resetButton;
    private GameObject _editorEventSystemObject;
    private EventSystem _editorEventSystem;
    private InputSystemUIInputModule _editorInputModule;
    private InputActionAsset _editorUiActions;
    private bool _editorUiInputActive;
    private bool _virtualPointerDown;
    private GameObject _virtualPointerPressTarget;
    private DrawableSliderControl _virtualPointerSlider;
    private string _pointerSource = "Mouse";
    private Vector2 _lastMousePosition;
    private Vector2 _lastGamepadStick;
    private bool _mousePositionAvailable;
    private bool _usingGamepadPointer;
    private float _lastUiDiagnosticsTime;
    private readonly List<EventSystemRestoreState> _eventSystemRestoreStates = new();

    internal bool IsOpenForDiagnostics => _isOpen;
    internal bool CanvasActiveForDiagnostics => _editorCanvasObject != null && _editorCanvasObject.activeSelf;

    private sealed class EventSystemRestoreState
    {
        internal EventSystem EventSystem;
        internal bool GameObjectActive;
        internal bool EventSystemEnabled;
        internal BaseInputModule[] Modules;
        internal bool[] ModuleEnabled;
    }

    private sealed class DrawableSliderControl : Selectable, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        private RectTransform _trackRect;
        private RectTransform _fillRect;
        private RectTransform _handleRect;
        private Action<float> _onValueChanged;
        private float _minValue;
        private float _maxValue = 1f;
        private float _value;

        internal float Value
        {
            get => _value;
            set => SetValue(value, true);
        }

        internal void Configure(RectTransform trackRect, RectTransform fillRect, RectTransform handleRect, float minValue, float maxValue, float value, Action<float> onValueChanged)
        {
            _trackRect = trackRect;
            _fillRect = fillRect;
            _handleRect = handleRect;
            _minValue = minValue;
            _maxValue = Mathf.Max(minValue + 0.0001f, maxValue);
            _onValueChanged = onValueChanged;
            SetValue(value, false);
        }

        internal void SetValue(float value, bool notify)
        {
            var clamped = Mathf.Clamp(value, _minValue, _maxValue);
            if (Mathf.Approximately(_value, clamped))
            {
                UpdateVisuals();
                return;
            }

            _value = clamped;
            UpdateVisuals();
            if (notify)
            {
                _onValueChanged?.Invoke(_value);
            }
        }

        internal bool SetValueFromScreenPosition(Vector2 screenPosition, Camera eventCamera, bool notify)
        {
            if (!IsActive() || !IsInteractable() || _trackRect == null)
            {
                return false;
            }

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_trackRect, screenPosition, eventCamera, out var localPoint))
            {
                return false;
            }

            var rect = _trackRect.rect;
            if (rect.width <= 0.01f)
            {
                return false;
            }

            var normalized = Mathf.Clamp01((localPoint.x - rect.xMin) / rect.width);
            SetValue(Mathf.Lerp(_minValue, _maxValue, normalized), notify);
            return true;
        }

        public override void OnPointerDown(PointerEventData eventData)
        {
            base.OnPointerDown(eventData);
            SetValueFromScreenPosition(eventData.position, eventData.pressEventCamera, true);
        }

        public void OnDrag(PointerEventData eventData)
        {
            SetValueFromScreenPosition(eventData.position, eventData.pressEventCamera, true);
        }

        public override void OnPointerUp(PointerEventData eventData)
        {
            base.OnPointerUp(eventData);
        }

        private void UpdateVisuals()
        {
            if (_trackRect == null || _fillRect == null || _handleRect == null)
            {
                return;
            }

            var normalized = Mathf.InverseLerp(_minValue, _maxValue, _value);
            _fillRect.anchorMin = new Vector2(0f, 0.5f);
            _fillRect.anchorMax = new Vector2(normalized, 0.5f);
            _fillRect.offsetMin = new Vector2(0f, -3f);
            _fillRect.offsetMax = new Vector2(0f, 3f);

            _handleRect.anchorMin = new Vector2(normalized, 0.5f);
            _handleRect.anchorMax = new Vector2(normalized, 0.5f);
            _handleRect.anchoredPosition = Vector2.zero;
            _handleRect.sizeDelta = new Vector2(16f, 28f);
        }
    }

    private void Start()
    {
        _cursor = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        RefreshFileLists();
        DrawableSuitsDiagnostics.Info($"SuitEditorController.Start complete. Screen={Screen.width}x{Screen.height}; designFiles={_designFiles.Count}; decalFiles={_decalFiles.Count}");
    }

    private void OnDestroy()
    {
        if (_isOpen)
        {
            CloseEditor(EditorCloseReason.PluginDestroy);
        }
        else
        {
            EndEditorUiInput();
            DestroyPreview();
        }

        if (_loadedDecal != null)
        {
            Destroy(_loadedDecal);
            _loadedDecal = null;
        }

        if (_checkerTexture != null)
        {
            Destroy(_checkerTexture);
            _checkerTexture = null;
        }

        if (_editorCanvasObject != null)
        {
            Destroy(_editorCanvasObject);
            _editorCanvasObject = null;
        }

        if (_editorEventSystemObject != null)
        {
            Destroy(_editorEventSystemObject);
            _editorEventSystemObject = null;
        }

        if (_editorUiActions != null)
        {
            Destroy(_editorUiActions);
            _editorUiActions = null;
        }
    }

    private void Update()
    {
        if (!_isOpen)
        {
            return;
        }

        if (DrawableSuitsInput.WasKeyPressed(Key.Escape) || WasGamepadPressed(g => g.buttonEast))
        {
            CloseEditor(EditorCloseReason.Shortcut);
            return;
        }

        ReapplyPlayerInputLock();
        EnsureCursorUnlockedWhileOpen();
        HandleControllerCursor();
        UpdateCursorMarker();
        HandleVirtualCursorClick();
        HandleEditorShortcuts();
        HandlePaintingInput();
        if (DrawableSuitsPlugin.ModConfig.EnableExperimentalModelPreview.Value && !_usingTexturePreview)
        {
            UpdatePreviewTransform();
            RenderPreviewFrame();
        }
        LogUiInputDiagnosticsIfNeeded();
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

        BeginEditorUiInput();
        RefreshEditorReadiness($"before show ({source})");
        CaptureAndUnlockCursor();
        CaptureAndLockPlayerInput();
        _isOpen = true;
        _editorCanvasObject.SetActive(true);
        InitializePointerForOpen(source);
        RefreshFileLists();
        TryRebuildPreviewForCurrentReadiness(source);
        RefreshEditorReadiness($"after preview ({source})");
        UpdateCursorMarker();
        UpdateUiState();
        RebuildSelectableNavigation();
        LogEditorControlTree(_panelRect);
        LogCanvasState($"opened from {source}");
        DrawableSuitsDiagnostics.Info("DrawableSuits editor overlay opened.");
        return true;
    }

    public void CloseEditor()
    {
        CloseEditor(EditorCloseReason.Normal);
    }

    public void CloseEditor(string source)
    {
        CloseEditor(GetCloseReasonFromSource(source));
    }

    public void CloseEditorForSceneChange()
    {
        CloseEditor(EditorCloseReason.SceneChange);
    }

    public void CloseEditorForPluginDestroy()
    {
        CloseEditor(EditorCloseReason.PluginDestroy);
    }

    private void CloseEditor(EditorCloseReason reason)
    {
        if (!_isOpen)
        {
            return;
        }

        DrawableSuitsDiagnostics.Info($"Closing DrawableSuits editor. reason={reason}");
        _isOpen = false;
        if (_editorCanvasObject != null)
        {
            _editorCanvasObject.SetActive(false);
        }

        DestroyPreview();
        _strokeActive = false;
        _virtualPointerDown = false;
        _virtualPointerPressTarget = null;
        _virtualPointerSlider = null;
        RestorePlayerInputState();
        EndEditorUiInput();
        RestoreCursorState(reason == EditorCloseReason.SceneChange);
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
            CloseEditor(GetCloseReasonFromSource(source));
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
        DrawableSuitsDiagnostics.Info($"Pause-menu delayed open before OpenEditor. quickMenuNull={quickMenu == null}; menuOpen={quickMenu?.isMenuOpen}; cursorVisible={Cursor.visible}; cursorLock={Cursor.lockState}; eventSystem={EventSystem.current?.name ?? "null"}; inputModule={EventSystem.current?.currentInputModule?.GetType().Name ?? "null"}");
        var opened = OpenEditor("PauseMenuButton");
        DrawableSuitsDiagnostics.Info($"Pause-menu delayed open after OpenEditor. opened={opened}; isOpen={_isOpen}; canvasActive={CanvasActiveForDiagnostics}; cursorVisible={Cursor.visible}; cursorLock={Cursor.lockState}; eventSystem={EventSystem.current?.name ?? "null"}; inputModule={EventSystem.current?.currentInputModule?.GetType().Name ?? "null"}");
    }

    private static EditorCloseReason GetCloseReasonFromSource(string source)
    {
        if (!string.IsNullOrWhiteSpace(source) && source.IndexOf("Pause", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return EditorCloseReason.PauseMenu;
        }

        if (!string.IsNullOrWhiteSpace(source) && source.IndexOf("Scene", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return EditorCloseReason.SceneChange;
        }

        return EditorCloseReason.Shortcut;
    }

    private void RefreshEditorReadiness(string context)
    {
        var localSuitId = DrawableSuitsPlugin.Registry.GetLocalSuitId();
        var suitIds = DrawableSuitsPlugin.Registry.GetSuitIds();
        _knownSuitCount = suitIds.Count;
        if (_selectedSuitId < 0)
        {
            _selectedSuitId = localSuitId >= 0 ? localSuitId : FirstKnownSuitId();
        }
        else if (!suitIds.Contains(_selectedSuitId))
        {
            _selectedSuitId = localSuitId >= 0 ? localSuitId : FirstKnownSuitId();
        }

        var player = StartOfRound.Instance?.localPlayerController;
        _hasLocalPlayer = player != null;
        _hasPlayerModel = player?.thisPlayerModel != null;
        _hasCamera = Camera.main != null;
        _hasPreviewCollider = _previewCollider != null;
        var editableTexture = _selectedSuitId >= 0 ? DrawableSuitsPlugin.Registry.GetEditableTexture(_selectedSuitId) : null;
        _hasEditableSuit = editableTexture != null;
        _canPaint = editableTexture != null;

        SetStatus(BuildReadinessStatus(), false);
        DrawableSuitsDiagnostics.Info($"Readiness[{context}]: selectedSuitId={_selectedSuitId}; suitCount={_knownSuitCount}; hasEditableSuit={_hasEditableSuit}; editableTexture={DescribeEditableTexture()}; hasLocalPlayer={_hasLocalPlayer}; hasPlayerModel={_hasPlayerModel}; hasCamera={_hasCamera}; previewMode={_previewMode}; hasPreviewCollider={_hasPreviewCollider}; canPaint={_canPaint}; status='{_statusMessage}'");
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

        return missing.Count == 0 ? $"Ready. Preview: {_previewMode}." : "Diagnostics: " + string.Join("; ", missing);
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
            BuildEditorCanvas();
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

    private void BuildEditorCanvas()
    {
        _bootstrapShell = false;
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

        var panel = CreateUiObject("EditorPanel", _editorCanvasObject.transform, typeof(Image));
        _panelRect = panel.GetComponent<RectTransform>();
        _panelRect.anchorMin = new Vector2(0f, 1f);
        _panelRect.anchorMax = new Vector2(0f, 1f);
        _panelRect.pivot = new Vector2(0f, 1f);
        _panelRect.anchoredPosition = new Vector2(24f, -24f);
        _panelRect.sizeDelta = new Vector2(1240f, 820f);

        var panelImage = panel.GetComponent<Image>();
        panelImage.color = new Color(0.025f, 0.03f, 0.035f, 0.96f);

        const float leftX = 18f;
        const float leftW = 360f;
        const float previewX = 398f;
        const float previewW = 420f;
        const float rightX = 838f;
        const float rightW = 380f;

        CreateAnchoredText(panel.transform, "Title", $"{PluginInfo.Name} {PluginInfo.Version}", 24, FontStyle.Bold, TextAnchor.MiddleLeft, new Rect(leftX, 12f, 360f, 34f), new Color(1f, 0.62f, 0.25f, 1f));
        CreateAnchoredButton(panel.transform, "Close", new Rect(1124f, 14f, 96f, 34f), CloseEditor);
        _suitLabel = CreateAnchoredText(panel.transform, "SuitLabel", string.Empty, 18, FontStyle.Normal, TextAnchor.MiddleLeft, new Rect(leftX, 54f, leftW, 28f), Color.white);
        _statusLabel = CreateAnchoredText(panel.transform, "StatusLabel", string.Empty, 15, FontStyle.Normal, TextAnchor.UpperLeft, new Rect(leftX, 86f, leftW, 46f), new Color(1f, 0.58f, 0.28f, 1f));
        _statusLabel.color = new Color(1f, 0.58f, 0.28f, 1f);
        _diagnosticsLabel = CreateAnchoredText(panel.transform, "DiagnosticsLabel", string.Empty, 12, FontStyle.Normal, TextAnchor.UpperLeft, new Rect(leftX, 138f, leftW, 126f), new Color(0.78f, 0.86f, 1f, 1f));
        _diagnosticsLabel.color = new Color(0.78f, 0.86f, 1f, 1f);

        CreateAnchoredButton(panel.transform, "Previous", new Rect(leftX, 278f, 92f, 34f), () => SelectAdjacentSuit(-1));
        CreateAnchoredButton(panel.transform, "Use Current", new Rect(leftX + 100f, 278f, 126f, 34f), () => SelectSuit(DrawableSuitsPlugin.Registry.GetLocalSuitId()));
        CreateAnchoredButton(panel.transform, "Next", new Rect(leftX + 234f, 278f, 82f, 34f), () => SelectAdjacentSuit(1));

        CreateAnchoredText(panel.transform, "ToolHeader", "Tool", 16, FontStyle.Bold, TextAnchor.MiddleLeft, new Rect(leftX, 328f, leftW, 24f), Color.white);
        _paintButton = CreateAnchoredButton(panel.transform, "Paint", new Rect(leftX, 356f, 108f, 34f), () => SetTool(EditorTool.Paint));
        _eraseButton = CreateAnchoredButton(panel.transform, "Erase", new Rect(leftX + 116f, 356f, 108f, 34f), () => SetTool(EditorTool.Erase));
        _decalButton = CreateAnchoredButton(panel.transform, "Decal", new Rect(leftX + 232f, 356f, 108f, 34f), () => SetTool(EditorTool.Decal));

        CreateAnchoredText(panel.transform, "BrushHeader", "Brush", 16, FontStyle.Bold, TextAnchor.MiddleLeft, new Rect(leftX, 406f, leftW, 24f), Color.white);
        _brushSizeLabel = CreateAnchoredText(panel.transform, "BrushSizeLabel", string.Empty, 14, FontStyle.Normal, TextAnchor.MiddleLeft, new Rect(leftX, 436f, 112f, 24f), Color.white);
        _brushSizeSlider = CreateAnchoredSlider(panel.transform, "BrushSize", 1f, 96f, _brushSize, new Rect(leftX + 118f, 438f, 222f, 24f), value => _brushSize = value);
        _brushOpacityLabel = CreateAnchoredText(panel.transform, "BrushOpacityLabel", string.Empty, 14, FontStyle.Normal, TextAnchor.MiddleLeft, new Rect(leftX, 470f, 112f, 24f), Color.white);
        _brushOpacitySlider = CreateAnchoredSlider(panel.transform, "BrushOpacity", 0.05f, 1f, _brushOpacity, new Rect(leftX + 118f, 472f, 222f, 24f), value => _brushOpacity = value);

        CreateAnchoredText(panel.transform, "ColorHeader", "Color", 16, FontStyle.Bold, TextAnchor.MiddleLeft, new Rect(leftX, 510f, leftW, 24f), Color.white);
        CreateAnchoredText(panel.transform, "RedLabel", "Red", 13, FontStyle.Normal, TextAnchor.MiddleLeft, new Rect(leftX, 540f, 58f, 22f), Color.white);
        _redSlider = CreateAnchoredSlider(panel.transform, "Red", 0f, 1f, _brushColor.r, new Rect(leftX + 64f, 540f, 220f, 24f), value => { _brushColor.r = value; UpdateColorUi(); });
        CreateAnchoredText(panel.transform, "GreenLabel", "Green", 13, FontStyle.Normal, TextAnchor.MiddleLeft, new Rect(leftX, 574f, 58f, 22f), Color.white);
        _greenSlider = CreateAnchoredSlider(panel.transform, "Green", 0f, 1f, _brushColor.g, new Rect(leftX + 64f, 574f, 220f, 24f), value => { _brushColor.g = value; UpdateColorUi(); });
        CreateAnchoredText(panel.transform, "BlueLabel", "Blue", 13, FontStyle.Normal, TextAnchor.MiddleLeft, new Rect(leftX, 608f, 58f, 22f), Color.white);
        _blueSlider = CreateAnchoredSlider(panel.transform, "Blue", 0f, 1f, _brushColor.b, new Rect(leftX + 64f, 608f, 220f, 24f), value => { _brushColor.b = value; UpdateColorUi(); });
        _colorSwatch = CreateAnchoredColorSwatch(panel.transform, new Rect(leftX + 296f, 540f, 44f, 92f));

        CreateAnchoredText(panel.transform, "PreviewHeader", "Preview", 16, FontStyle.Bold, TextAnchor.MiddleLeft, new Rect(previewX, 54f, previewW, 26f), Color.white);
        var preview = CreateUiObject("PreviewViewport", panel.transform, typeof(RectTransform), typeof(Image));
        _previewViewportRect = preview.GetComponent<RectTransform>();
        SetAnchoredRect(_previewViewportRect, new Rect(previewX, 86f, previewW, 540f));
        var previewBackground = preview.GetComponent<Image>();
        previewBackground.color = new Color(0.025f, 0.028f, 0.032f, 1f);
        previewBackground.raycastTarget = true;

        var previewImageObject = CreateUiObject("TexturePreviewImage", preview.transform, typeof(RectTransform), typeof(RawImage));
        var previewImageRect = previewImageObject.GetComponent<RectTransform>();
        previewImageRect.anchorMin = Vector2.zero;
        previewImageRect.anchorMax = Vector2.one;
        previewImageRect.offsetMin = Vector2.zero;
        previewImageRect.offsetMax = Vector2.zero;
        _previewImage = previewImageObject.GetComponent<RawImage>();
        _previewImage.texture = EnsureCheckerTexture();
        _previewImage.uvRect = new Rect(0f, 0f, 10f, 10f);
        _previewImage.color = Color.white;
        _previewImage.raycastTarget = true;
        CreateAnchoredText(panel.transform, "PreviewHelp", "Texture preview: paint/erase/decal inside this UV layout. Mouse wheel changes brush size.", 13, FontStyle.Normal, TextAnchor.UpperLeft, new Rect(previewX, 638f, previewW, 58f), new Color(0.82f, 0.86f, 0.9f, 1f));

        CreateAnchoredText(panel.transform, "DecalHeader", "Decal", 16, FontStyle.Bold, TextAnchor.MiddleLeft, new Rect(rightX, 54f, rightW, 24f), Color.white);
        _decalSizeLabel = CreateAnchoredText(panel.transform, "DecalSizeLabel", string.Empty, 14, FontStyle.Normal, TextAnchor.MiddleLeft, new Rect(rightX, 84f, 116f, 24f), Color.white);
        _decalSizeSlider = CreateAnchoredSlider(panel.transform, "DecalSize", 16f, 512f, _decalSize, new Rect(rightX + 126f, 86f, 234f, 24f), value => _decalSize = value);
        _decalRotationLabel = CreateAnchoredText(panel.transform, "DecalRotationLabel", string.Empty, 14, FontStyle.Normal, TextAnchor.MiddleLeft, new Rect(rightX, 118f, 116f, 24f), Color.white);
        _decalRotationSlider = CreateAnchoredSlider(panel.transform, "DecalRotation", -180f, 180f, _decalRotation, new Rect(rightX + 126f, 120f, 234f, 24f), value => _decalRotation = value);
        CreateAnchoredButton(panel.transform, "Refresh", new Rect(rightX, 156f, 110f, 34f), RefreshFileLists);
        CreateAnchoredButton(panel.transform, "Refresh Decals", new Rect(rightX + 118f, 156f, 150f, 34f), ImportDecalFromDialog);
        _decalListContent = CreateAnchoredScrollList(panel.transform, "DecalList", new Rect(rightX, 198f, rightW, 112f));

        CreateAnchoredText(panel.transform, "DesignHeader", "Design Name", 16, FontStyle.Bold, TextAnchor.MiddleLeft, new Rect(rightX, 330f, rightW, 24f), Color.white);
        _designNameInput = CreateAnchoredInputField(panel.transform, _designName, new Rect(rightX, 360f, rightW, 34f));
        _designNameInput.onValueChanged.AddListener(value => _designName = value);

        CreateAnchoredButton(panel.transform, "Undo", new Rect(rightX, 412f, 88f, 34f), Undo);
        CreateAnchoredButton(panel.transform, "Redo", new Rect(rightX + 96f, 412f, 88f, 34f), Redo);
        _resetButton = CreateAnchoredButton(panel.transform, "Reset", new Rect(rightX + 192f, 412f, 88f, 34f), () =>
        {
            SaveUndo();
            DrawableSuitsPlugin.Registry.ResetSuit(_selectedSuitId);
            _redo.Clear();
            UpdateUiState();
        });

        _applyButton = CreateAnchoredButton(panel.transform, "Apply", new Rect(rightX, 454f, 88f, 34f), () => DrawableSuitsPlugin.Registry.ApplyEditedTexture(_selectedSuitId, _designName, true));
        _saveButton = CreateAnchoredButton(panel.transform, "Save", new Rect(rightX + 96f, 454f, 88f, 34f), SaveDesign);
        _loadButton = CreateAnchoredButton(panel.transform, "Load", new Rect(rightX + 192f, 454f, 88f, 34f), LoadSelectedDesign);

        CreateAnchoredText(panel.transform, "SavedDesignsHeader", "Saved Designs", 16, FontStyle.Bold, TextAnchor.MiddleLeft, new Rect(rightX, 508f, rightW, 24f), Color.white);
        _designListContent = CreateAnchoredScrollList(panel.transform, "DesignList", new Rect(rightX, 538f, rightW, 156f));

        CreateAnchoredText(panel.transform, "ControllerHelp", "Controller: View/Back+Y open/close, left stick cursor, A clicks under cursor, right trigger paints in preview, bumpers rotate, Y tool, X undo, Start save.", 13, FontStyle.Normal, TextAnchor.UpperLeft, new Rect(leftX, 730f, 1160f, 42f), Color.white);

        _cursorMarker = CreateUiObject("DrawableSuitsCursor", _editorCanvasObject.transform, typeof(Image)).GetComponent<RectTransform>();
        _cursorMarker.sizeDelta = new Vector2(14f, 14f);
        var cursorImage = _cursorMarker.GetComponent<Image>();
        cursorImage.color = Color.white;
        cursorImage.raycastTarget = false;

        _editorCanvasObject.SetActive(false);
        RefreshListButtons();
        UpdateUiState();
        RebuildSelectableNavigation();
        LogEditorControlTree(panel.transform);
        DrawableSuitsDiagnostics.Info($"BuildEditorCanvas complete. childCount={_editorCanvasObject.transform.childCount}; panelChildren={panel.transform.childCount}; graphicRaycaster={_editorCanvasObject.GetComponent<GraphicRaycaster>() != null}");
    }

    private void BuildFallbackDiagnosticsCanvas(Exception originalException)
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
        _fallbackDiagnosticsLabel.text = $"DrawableSuits diagnostics fallback\nFull editor build failed: {originalException.GetType().Name}: {originalException.Message}";

        _cursorMarker = new GameObject("DrawableSuitsCursor", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
        _cursorMarker.transform.SetParent(_editorCanvasObject.transform, false);
        _cursorMarker.sizeDelta = new Vector2(14f, 14f);
        var cursorImage = _cursorMarker.GetComponent<Image>();
        cursorImage.color = Color.white;
        cursorImage.raycastTarget = false;
        _editorCanvasObject.SetActive(false);
        DrawableSuitsDiagnostics.Info("Fallback diagnostics canvas built.");
    }

    private static GameObject CreateUiObject(string name, Transform parent, params Type[] components)
    {
        var go = new GameObject(name, components);
        go.transform.SetParent(parent, false);
        return go;
    }

    private static void SetAnchoredRect(RectTransform rectTransform, Rect rect)
    {
        rectTransform.anchorMin = new Vector2(0f, 1f);
        rectTransform.anchorMax = new Vector2(0f, 1f);
        rectTransform.pivot = new Vector2(0f, 1f);
        rectTransform.anchoredPosition = new Vector2(rect.x, -rect.y);
        rectTransform.sizeDelta = new Vector2(rect.width, rect.height);
    }

    private static Text CreateAnchoredText(Transform parent, string name, string text, int fontSize, FontStyle style, TextAnchor alignment, Rect rect, Color color)
    {
        var go = CreateUiObject(name, parent, typeof(RectTransform), typeof(Text));
        SetAnchoredRect(go.GetComponent<RectTransform>(), rect);

        var label = go.GetComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        label.text = text;
        label.fontSize = fontSize;
        label.fontStyle = style;
        label.alignment = alignment;
        label.color = color;
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.verticalOverflow = VerticalWrapMode.Truncate;
        label.supportRichText = false;
        label.raycastTarget = false;
        return label;
    }

    private static Button CreateAnchoredButton(Transform parent, string text, Rect rect, Action onClick)
    {
        var go = CreateUiObject(text + "Button", parent, typeof(RectTransform), typeof(Image), typeof(Button));
        SetAnchoredRect(go.GetComponent<RectTransform>(), rect);

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

        var label = CreateAnchoredText(go.transform, "Label", text, 15, FontStyle.Normal, TextAnchor.MiddleCenter, new Rect(0f, 0f, rect.width, rect.height), Color.white);
        var labelRect = label.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.pivot = new Vector2(0.5f, 0.5f);
        labelRect.anchoredPosition = Vector2.zero;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
        return button;
    }

    private static DrawableSliderControl CreateAnchoredSlider(Transform parent, string name, float min, float max, float value, Rect rect, Action<float> onValueChanged)
    {
        var go = CreateUiObject(name + "Slider", parent, typeof(RectTransform), typeof(Image), typeof(DrawableSliderControl));
        SetAnchoredRect(go.GetComponent<RectTransform>(), rect);
        var rootImage = go.GetComponent<Image>();
        rootImage.color = new Color(1f, 1f, 1f, 0.001f);
        rootImage.raycastTarget = true;

        var background = CreateUiObject("Track", go.transform, typeof(RectTransform), typeof(Image));
        var bgRect = background.GetComponent<RectTransform>();
        bgRect.anchorMin = new Vector2(0f, 0.5f);
        bgRect.anchorMax = new Vector2(1f, 0.5f);
        bgRect.pivot = new Vector2(0.5f, 0.5f);
        bgRect.offsetMin = new Vector2(0f, -4f);
        bgRect.offsetMax = new Vector2(0f, 4f);
        var backgroundImage = background.GetComponent<Image>();
        backgroundImage.color = new Color(0.08f, 0.08f, 0.09f, 1f);
        backgroundImage.raycastTarget = true;

        var fill = CreateUiObject("Fill", go.transform, typeof(RectTransform), typeof(Image));
        var fillRect = fill.GetComponent<RectTransform>();
        fillRect.anchorMin = new Vector2(0f, 0.5f);
        fillRect.anchorMax = new Vector2(1f, 0.5f);
        fillRect.pivot = new Vector2(0f, 0.5f);
        fillRect.offsetMin = new Vector2(0f, -4f);
        fillRect.offsetMax = new Vector2(0f, 4f);
        var fillImage = fill.GetComponent<Image>();
        fillImage.color = new Color(0.95f, 0.42f, 0.16f, 1f);
        fillImage.raycastTarget = false;

        var handle = CreateUiObject("Handle", go.transform, typeof(RectTransform), typeof(Image));
        var handleRect = handle.GetComponent<RectTransform>();
        handleRect.anchorMin = new Vector2(0f, 0.5f);
        handleRect.anchorMax = new Vector2(0f, 0.5f);
        handleRect.pivot = new Vector2(0.5f, 0.5f);
        handleRect.anchoredPosition = Vector2.zero;
        handleRect.sizeDelta = new Vector2(16f, 28f);
        var handleImage = handle.GetComponent<Image>();
        handleImage.color = Color.white;
        handleImage.raycastTarget = true;

        var slider = go.GetComponent<DrawableSliderControl>();
        slider.targetGraphic = handleImage;
        slider.Configure(bgRect, fillRect, handleRect, min, max, value, onValueChanged);
        DrawableSuitsDiagnostics.Info($"DrawableSliderBuilt name={name}; root={rect}; trackSize={bgRect.rect.size}; fillSize={fillRect.rect.size}; handleSize={handleRect.rect.size}; min={min}; max={max}; value={value}");
        return slider;
    }

    private static Image CreateAnchoredColorSwatch(Transform parent, Rect rect)
    {
        var go = CreateUiObject("ColorSwatch", parent, typeof(RectTransform), typeof(Image));
        SetAnchoredRect(go.GetComponent<RectTransform>(), rect);
        return go.GetComponent<Image>();
    }

    private static InputField CreateAnchoredInputField(Transform parent, string value, Rect rect)
    {
        var go = CreateUiObject("DesignNameInput", parent, typeof(RectTransform), typeof(Image), typeof(InputField));
        SetAnchoredRect(go.GetComponent<RectTransform>(), rect);
        go.GetComponent<Image>().color = new Color(0.08f, 0.085f, 0.09f, 1f);

        var textObject = CreateUiObject("Text", go.transform, typeof(RectTransform), typeof(Text));
        var textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(8f, 2f);
        textRect.offsetMax = new Vector2(-8f, -2f);

        var text = textObject.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        text.fontSize = 16;
        text.color = Color.white;
        text.alignment = TextAnchor.MiddleLeft;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.raycastTarget = false;

        var input = go.GetComponent<InputField>();
        input.textComponent = text;
        input.text = value;
        input.lineType = InputField.LineType.SingleLine;
        input.caretColor = Color.white;
        input.selectionColor = new Color(0.95f, 0.42f, 0.16f, 0.45f);
        return input;
    }

    private static RectTransform CreateAnchoredScrollList(Transform parent, string name, Rect rect)
    {
        var root = CreateUiObject(name, parent, typeof(RectTransform), typeof(Image), typeof(ScrollRect));
        SetAnchoredRect(root.GetComponent<RectTransform>(), rect);
        root.GetComponent<Image>().color = new Color(0.06f, 0.065f, 0.07f, 0.9f);

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

    private static Text CreateText(Transform parent, string text, int fontSize, FontStyle style, TextAnchor alignment, float height)
    {
        var go = CreateUiObject("Text", parent, typeof(RectTransform), typeof(Text), typeof(LayoutElement));
        var label = go.GetComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        label.text = text;
        label.fontSize = fontSize;
        label.fontStyle = style;
        label.alignment = alignment;
        label.color = Color.white;
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.verticalOverflow = VerticalWrapMode.Truncate;
        label.supportRichText = false;

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

        var label = CreateText(go.transform, text, 15, FontStyle.Normal, TextAnchor.MiddleCenter, 30f);
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

    private static InputField CreateInputField(Transform parent, string value)
    {
        var go = CreateUiObject("DesignNameInput", parent, typeof(RectTransform), typeof(Image), typeof(InputField), typeof(LayoutElement));
        go.GetComponent<Image>().color = new Color(0.08f, 0.085f, 0.09f, 1f);

        var layout = go.GetComponent<LayoutElement>();
        layout.preferredHeight = 34f;
        layout.minHeight = 34f;

        var textObject = CreateUiObject("Text", go.transform, typeof(RectTransform), typeof(Text));
        var textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(8f, 2f);
        textRect.offsetMax = new Vector2(-8f, -2f);

        var text = textObject.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        text.fontSize = 16;
        text.color = Color.white;
        text.alignment = TextAnchor.MiddleLeft;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Truncate;

        var input = go.GetComponent<InputField>();
        input.textComponent = text;
        input.text = value;
        input.lineType = InputField.LineType.SingleLine;
        input.caretColor = Color.white;
        input.selectionColor = new Color(0.95f, 0.42f, 0.16f, 0.45f);
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

    private void BeginEditorUiInput()
    {
        if (_editorUiInputActive)
        {
            return;
        }

        EnsureOwnedEventSystem();
        _eventSystemRestoreStates.Clear();
        foreach (var eventSystem in FindObjectsOfType<EventSystem>(true))
        {
            if (eventSystem == null || eventSystem == _editorEventSystem)
            {
                continue;
            }

            var modules = eventSystem.GetComponents<BaseInputModule>();
            var state = new EventSystemRestoreState
            {
                EventSystem = eventSystem,
                GameObjectActive = eventSystem.gameObject.activeSelf,
                EventSystemEnabled = eventSystem.enabled,
                Modules = modules,
                ModuleEnabled = new bool[modules.Length]
            };

            for (var i = 0; i < modules.Length; i++)
            {
                state.ModuleEnabled[i] = modules[i] != null && modules[i].enabled;
                if (modules[i] != null)
                {
                    modules[i].enabled = false;
                }
            }

            eventSystem.enabled = false;
            _eventSystemRestoreStates.Add(state);
        }

        _editorEventSystemObject.SetActive(true);
        _editorEventSystem.enabled = true;
        _editorInputModule.enabled = true;
        _editorUiActions?.Enable();
        EventSystem.current = _editorEventSystem;
        _editorUiInputActive = true;
        DrawableSuitsDiagnostics.Info($"DrawableSuits editor UI input enabled. ownedEventSystem={DrawableSuitsPlugin.DescribeUnityObject(_editorEventSystem)}; disabledEventSystems={_eventSystemRestoreStates.Count}; current={EventSystem.current?.name ?? "null"}; module={_editorInputModule.GetType().Name}");
    }

    private void EndEditorUiInput()
    {
        if (!_editorUiInputActive)
        {
            _editorUiActions?.Disable();
            if (_editorInputModule != null)
            {
                _editorInputModule.enabled = false;
            }
            if (_editorEventSystem != null)
            {
                _editorEventSystem.enabled = false;
            }
            if (_editorEventSystemObject != null)
            {
                _editorEventSystemObject.SetActive(false);
            }
            return;
        }

        _editorUiActions?.Disable();
        if (_editorInputModule != null)
        {
            _editorInputModule.enabled = false;
        }
        if (_editorEventSystem != null)
        {
            _editorEventSystem.enabled = false;
        }
        if (_editorEventSystemObject != null)
        {
            _editorEventSystemObject.SetActive(false);
        }

        EventSystem restoredCurrent = null;
        foreach (var state in _eventSystemRestoreStates)
        {
            if (state?.EventSystem == null)
            {
                continue;
            }

            state.EventSystem.gameObject.SetActive(state.GameObjectActive);
            state.EventSystem.enabled = state.EventSystemEnabled;
            if (state.Modules != null && state.ModuleEnabled != null)
            {
                for (var i = 0; i < state.Modules.Length && i < state.ModuleEnabled.Length; i++)
                {
                    if (state.Modules[i] != null)
                    {
                        state.Modules[i].enabled = state.ModuleEnabled[i];
                    }
                }
            }

            if (restoredCurrent == null && state.GameObjectActive && state.EventSystemEnabled)
            {
                restoredCurrent = state.EventSystem;
            }
        }

        if (restoredCurrent == null)
        {
            foreach (var eventSystem in FindObjectsOfType<EventSystem>(true))
            {
                if (eventSystem != null
                    && eventSystem != _editorEventSystem
                    && eventSystem.gameObject.activeInHierarchy
                    && eventSystem.enabled)
                {
                    restoredCurrent = eventSystem;
                    break;
                }
            }
        }

        EventSystem.current = restoredCurrent;
        DrawableSuitsDiagnostics.Info($"DrawableSuits editor UI input disabled. restoredEventSystems={_eventSystemRestoreStates.Count}; current={EventSystem.current?.name ?? "null"}");
        _eventSystemRestoreStates.Clear();
        _editorUiInputActive = false;
    }

    private void EnsureOwnedEventSystem()
    {
        if (_editorEventSystemObject != null && _editorEventSystem != null && _editorInputModule != null)
        {
            return;
        }

        _editorEventSystemObject = new GameObject("DrawableSuitsEditorEventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        DontDestroyOnLoad(_editorEventSystemObject);
        _editorEventSystem = _editorEventSystemObject.GetComponent<EventSystem>();
        _editorInputModule = _editorEventSystemObject.GetComponent<InputSystemUIInputModule>();
        ConfigureInputSystemUiModule(_editorInputModule);
        _editorEventSystemObject.SetActive(false);
        DrawableSuitsDiagnostics.Info("Created DrawableSuits-owned EventSystem with InputSystemUIInputModule.");
    }

    private void ConfigureInputSystemUiModule(InputSystemUIInputModule module)
    {
        if (module == null)
        {
            return;
        }

        _editorUiActions = ScriptableObject.CreateInstance<InputActionAsset>();
        _editorUiActions.name = "DrawableSuitsUIActions";
        DontDestroyOnLoad(_editorUiActions);

        var map = _editorUiActions.AddActionMap("UI");
        var point = map.AddAction("Point", InputActionType.PassThrough, "<Pointer>/position");
        var leftClick = map.AddAction("LeftClick", InputActionType.Button, "<Mouse>/leftButton");
        leftClick.AddBinding("<Pointer>/press");
        var scrollWheel = map.AddAction("ScrollWheel", InputActionType.PassThrough, "<Pointer>/scroll");
        var move = map.AddAction("Move", InputActionType.PassThrough);
        move.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/upArrow")
            .With("Down", "<Keyboard>/downArrow")
            .With("Left", "<Keyboard>/leftArrow")
            .With("Right", "<Keyboard>/rightArrow");
        move.AddBinding("<Gamepad>/dpad");
        var submit = map.AddAction("Submit", InputActionType.Button);
        submit.AddBinding("<Keyboard>/enter");
        var cancel = map.AddAction("Cancel", InputActionType.Button);
        cancel.AddBinding("<Keyboard>/escape");
        cancel.AddBinding("<Gamepad>/buttonEast");

        module.actionsAsset = _editorUiActions;
        module.point = InputActionReference.Create(point);
        module.leftClick = InputActionReference.Create(leftClick);
        module.scrollWheel = InputActionReference.Create(scrollWheel);
        module.move = InputActionReference.Create(move);
        module.submit = InputActionReference.Create(submit);
        module.cancel = InputActionReference.Create(cancel);
    }

    private void InitializePointerForOpen(string source)
    {
        _mousePositionAvailable = DrawableSuitsInput.TryGetMousePosition(out _lastMousePosition);
        _lastGamepadStick = Gamepad.current != null ? Gamepad.current.leftStick.ReadValue() : Vector2.zero;
        _usingGamepadPointer = false;
        _pointerSource = "Mouse";
        _cursor = _mousePositionAvailable ? _lastMousePosition : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        DrawableSuitsDiagnostics.Info($"Pointer initialized for editor open. source={source}; pointerSource={_pointerSource}; mouseAvailable={_mousePositionAvailable}; mouse={_lastMousePosition}; gamepadStick={_lastGamepadStick}; cursor={_cursor}; cursorVisible={Cursor.visible}; cursorLock={Cursor.lockState}");
    }

    private void EnsureCursorUnlockedWhileOpen()
    {
        if (!_isOpen)
        {
            return;
        }

        if (!Cursor.visible || Cursor.lockState != CursorLockMode.None)
        {
            DrawableSuitsDiagnostics.Warn($"Editor cursor was recaptured while open; unlocking again. visibleBefore={Cursor.visible}; lockBefore={Cursor.lockState}; pointerSource={_pointerSource}");
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }
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

    private void RestoreCursorState(bool leaveUnlockedForSceneChange)
    {
        if (!_cursorStateCaptured)
        {
            if (leaveUnlockedForSceneChange)
            {
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
            }
            return;
        }

        if (leaveUnlockedForSceneChange)
        {
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }
        else
        {
            Cursor.visible = _previousCursorVisible;
            Cursor.lockState = _previousCursorLockState;
        }

        _cursorStateCaptured = false;
        DrawableSuitsDiagnostics.Info($"Cursor restored after editor close. leaveUnlocked={leaveUnlockedForSceneChange}; visible={Cursor.visible}; lock={Cursor.lockState}");
    }

    private void CaptureAndLockPlayerInput()
    {
        var player = StartOfRound.Instance?.localPlayerController;
        if (player == null)
        {
            DrawableSuitsDiagnostics.Info("Player input lock skipped: local player is null.");
            _playerInputStateCaptured = false;
            _inputLockedPlayer = null;
            return;
        }

        _inputLockedPlayer = player;
        _previousDisableMoveInput = player.disableMoveInput;
        _previousDisableLookInput = player.disableLookInput;
        _previousDisableInteract = player.disableInteract;
        _previousInSpecialMenu = player.inSpecialMenu;
        _previousMovementActionsEnabled = player.playerActions.Movement.enabled;
        _playerInputStateCaptured = true;
        ApplyPlayerInputLock(player, true);
        DrawableSuitsDiagnostics.Info($"Player input locked for editor. player={DrawableSuitsPlugin.DescribeUnityObject(player)}; previousMove={_previousDisableMoveInput}; previousLook={_previousDisableLookInput}; previousInteract={_previousDisableInteract}; previousSpecialMenu={_previousInSpecialMenu}; movementActionsWereEnabled={_previousMovementActionsEnabled}");
    }

    private void ReapplyPlayerInputLock()
    {
        if (!_playerInputStateCaptured)
        {
            return;
        }

        var player = StartOfRound.Instance?.localPlayerController;
        if (player == null)
        {
            return;
        }

        if (_inputLockedPlayer != player)
        {
            DrawableSuitsDiagnostics.Warn($"Player input lock target changed while editor was open. old={DrawableSuitsPlugin.DescribeUnityObject(_inputLockedPlayer)}; new={DrawableSuitsPlugin.DescribeUnityObject(player)}");
            _inputLockedPlayer = player;
            _previousDisableMoveInput = player.disableMoveInput;
            _previousDisableLookInput = player.disableLookInput;
            _previousDisableInteract = player.disableInteract;
            _previousInSpecialMenu = player.inSpecialMenu;
            _previousMovementActionsEnabled = player.playerActions.Movement.enabled;
        }

        ApplyPlayerInputLock(player, false);
    }

    private void RestorePlayerInputState()
    {
        if (!_playerInputStateCaptured)
        {
            return;
        }

        if (_inputLockedPlayer != null)
        {
            _inputLockedPlayer.disableMoveInput = _previousDisableMoveInput;
            _inputLockedPlayer.disableLookInput = _previousDisableLookInput;
            _inputLockedPlayer.disableInteract = _previousDisableInteract;
            _inputLockedPlayer.inSpecialMenu = _previousInSpecialMenu;
            if (_previousMovementActionsEnabled && !_inputLockedPlayer.playerActions.Movement.enabled)
            {
                _inputLockedPlayer.playerActions.Movement.Enable();
            }
            else if (!_previousMovementActionsEnabled && _inputLockedPlayer.playerActions.Movement.enabled)
            {
                _inputLockedPlayer.playerActions.Movement.Disable();
            }

            DrawableSuitsDiagnostics.Info($"Player input restored after editor close. player={DrawableSuitsPlugin.DescribeUnityObject(_inputLockedPlayer)}; move={_inputLockedPlayer.disableMoveInput}; look={_inputLockedPlayer.disableLookInput}; interact={_inputLockedPlayer.disableInteract}; specialMenu={_inputLockedPlayer.inSpecialMenu}; movementActionsEnabled={_inputLockedPlayer.playerActions.Movement.enabled}");
        }
        else
        {
            DrawableSuitsDiagnostics.Info("Player input restore skipped because locked player was destroyed or unavailable.");
        }

        _inputLockedPlayer = null;
        _playerInputStateCaptured = false;
    }

    private static void ApplyPlayerInputLock(PlayerControllerB player, bool logActionMap)
    {
        if (player == null)
        {
            return;
        }

        player.disableMoveInput = true;
        player.disableLookInput = true;
        player.disableInteract = true;
        player.inSpecialMenu = true;
        player.moveInputVector = Vector2.zero;
        player.isHoldingInteract = false;

        if (player.playerActions.Movement.enabled)
        {
            player.playerActions.Movement.Disable();
            if (logActionMap)
            {
                DrawableSuitsDiagnostics.Info("Disabled PlayerActions.Movement while DrawableSuits editor is open.");
            }
        }
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

        if (_brushSizeSlider != null) _brushSizeSlider.SetValue(_brushSize, false);
        if (_brushOpacitySlider != null) _brushOpacitySlider.SetValue(_brushOpacity, false);
        if (_redSlider != null) _redSlider.SetValue(_brushColor.r, false);
        if (_greenSlider != null) _greenSlider.SetValue(_brushColor.g, false);
        if (_blueSlider != null) _blueSlider.SetValue(_brushColor.b, false);
        if (_decalSizeSlider != null) _decalSizeSlider.SetValue(_decalSize, false);
        if (_decalRotationSlider != null) _decalRotationSlider.SetValue(_decalRotation, false);

        if (_diagnosticsLabel != null)
        {
            _diagnosticsLabel.text = BuildDiagnosticsSummary();
        }
        if (_fallbackDiagnosticsLabel != null)
        {
            _fallbackDiagnosticsLabel.text = "DrawableSuits diagnostics\n" + BuildDiagnosticsSummary();
        }

        if (_usingTexturePreview || !DrawableSuitsPlugin.ModConfig.EnableExperimentalModelPreview.Value)
        {
            UseTexturePreview("UpdateUiState", false);
        }

        var hasEditableTexture = _selectedSuitId >= 0 && DrawableSuitsPlugin.Registry.GetEditableTexture(_selectedSuitId) != null;
        SetInteractable(_paintButton, _canPaint);
        SetInteractable(_eraseButton, _canPaint);
        SetInteractable(_decalButton, _canPaint);
        SetInteractable(_applyButton, _canPaint && hasEditableTexture);
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
            $"Preview mode: {_previewMode}",
            $"Editable texture: {DescribeEditableTexture()}",
            $"Preview UI texture: {DescribePreviewImageTexture()}",
            $"Experimental model preview: {DrawableSuitsPlugin.ModConfig.EnableExperimentalModelPreview.Value}",
            $"Preview camera found: {_previewCamera != null}",
            $"Preview collider found: {_hasPreviewCollider}",
            $"Can paint/apply texture: {_canPaint}",
            $"Canvas active: {(_editorCanvasObject != null && _editorCanvasObject.activeSelf)}",
            "Diagnostics log: BepInEx/config/DrawableSuits/Logs/diagnostics.log"
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
            RebuildList(_designListContent, _designFiles, _selectedDesignIndex, index =>
            {
                _selectedDesignIndex = index;
                RefreshListButtons();
                UpdateUiState();
            }, path => Path.GetFileNameWithoutExtension(path));
        }

        if (_decalListContent != null)
        {
            RebuildList(_decalListContent, _decalFiles, _selectedDecalIndex, SelectDecal, Path.GetFileName);
        }

        RebuildSelectableNavigation();
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

                var colors = button.colors;
                colors.normalColor = new Color(0.95f, 0.42f, 0.16f, 1f);
                colors.highlightedColor = new Color(1f, 0.54f, 0.24f, 1f);
                colors.selectedColor = colors.highlightedColor;
                button.colors = colors;
            }
        }
    }

    private void RebuildSelectableNavigation()
    {
        if (_panelRect == null)
        {
            return;
        }

        var selectables = new List<Selectable>();
        foreach (var selectable in _panelRect.GetComponentsInChildren<Selectable>(true))
        {
            if (selectable != null && selectable.gameObject.activeInHierarchy)
            {
                selectables.Add(selectable);
            }
        }

        for (var i = 0; i < selectables.Count; i++)
        {
            var navigation = new Navigation
            {
                mode = Navigation.Mode.Explicit,
                selectOnUp = i > 0 ? selectables[i - 1] : null,
                selectOnDown = i < selectables.Count - 1 ? selectables[i + 1] : null,
                selectOnLeft = i > 0 ? selectables[i - 1] : null,
                selectOnRight = i < selectables.Count - 1 ? selectables[i + 1] : null
            };
            selectables[i].navigation = navigation;
        }

        if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == null && selectables.Count > 0)
        {
            EventSystem.current.SetSelectedGameObject(selectables[0].gameObject);
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
        _mousePositionAvailable = DrawableSuitsInput.TryGetMousePosition(out _lastMousePosition);
        var mouseUsed = DrawableSuitsInput.WasMouseUsedThisFrame();
        var gamepad = Gamepad.current;
        _lastGamepadStick = gamepad != null ? gamepad.leftStick.ReadValue() : Vector2.zero;
        var gamepadMoved = gamepad != null && _lastGamepadStick.sqrMagnitude > 0.0324f;
        var gamepadActivelyPointing = gamepad != null
            && (gamepadMoved
                || gamepad.buttonSouth.isPressed
                || gamepad.rightTrigger.ReadValue() > 0.2f
                || gamepad.leftShoulder.isPressed
                || gamepad.rightShoulder.isPressed);

        if (mouseUsed)
        {
            _usingGamepadPointer = false;
        }
        else if (gamepadActivelyPointing)
        {
            _usingGamepadPointer = true;
        }
        else if (gamepad == null)
        {
            _usingGamepadPointer = false;
        }

        if (_usingGamepadPointer && gamepad != null)
        {
            _pointerSource = "Gamepad";
            var delta = _lastGamepadStick * DrawableSuitsPlugin.ModConfig.ControllerCursorSpeed.Value * Time.unscaledDeltaTime;
            _cursor.x = Mathf.Clamp(_cursor.x + delta.x, 0f, Screen.width);
            _cursor.y = Mathf.Clamp(_cursor.y + delta.y, 0f, Screen.height);
            return;
        }

        _pointerSource = "Mouse";
        if (_mousePositionAvailable)
        {
            _cursor = _lastMousePosition;
        }
    }

    private void HandleVirtualCursorClick()
    {
        var gamepad = Gamepad.current;
        if (gamepad == null || !_editorUiInputActive || _editorCanvasObject == null)
        {
            _virtualPointerDown = false;
            _virtualPointerPressTarget = null;
            _virtualPointerSlider = null;
            return;
        }

        var south = gamepad.buttonSouth;
        if (south.wasPressedThisFrame)
        {
            BeginVirtualPointerPress();
        }

        if (south.isPressed && _virtualPointerSlider != null)
        {
            _virtualPointerSlider.SetValueFromScreenPosition(_cursor, null, true);
        }

        if (south.wasReleasedThisFrame)
        {
            EndVirtualPointerPress();
        }
    }

    private void BeginVirtualPointerPress()
    {
        _virtualPointerDown = true;
        _virtualPointerPressTarget = null;
        _virtualPointerSlider = null;

        var hits = RaycastEditorUi(_cursor);
        if (hits.Count == 0)
        {
            DrawableSuitsDiagnostics.Info($"Virtual cursor A press hit no UI target. cursor={_cursor}");
            return;
        }

        var hitObject = hits[0].gameObject;
        _virtualPointerSlider = hitObject.GetComponentInParent<DrawableSliderControl>();
        var pointerData = CreateVirtualPointerEventData();

        if (_virtualPointerSlider != null)
        {
            EventSystem.current?.SetSelectedGameObject(_virtualPointerSlider.gameObject);
            _virtualPointerSlider.SetValueFromScreenPosition(_cursor, null, true);
            _virtualPointerPressTarget = _virtualPointerSlider.gameObject;
            DrawableSuitsDiagnostics.Info($"Virtual cursor A press captured slider={_virtualPointerSlider.name}; cursor={_cursor}");
            return;
        }

        var eventTarget = ExecuteEvents.GetEventHandler<IPointerClickHandler>(hitObject)
            ?? ExecuteEvents.GetEventHandler<ISubmitHandler>(hitObject)
            ?? ExecuteEvents.GetEventHandler<ISelectHandler>(hitObject);
        if (eventTarget == null)
        {
            DrawableSuitsDiagnostics.Info($"Virtual cursor A press hit non-clickable target={hitObject.name}; cursor={_cursor}");
            return;
        }

        _virtualPointerPressTarget = eventTarget;
        EventSystem.current?.SetSelectedGameObject(eventTarget);
        ExecuteEvents.Execute(eventTarget, pointerData, ExecuteEvents.pointerDownHandler);
        ExecuteEvents.Execute(eventTarget, pointerData, ExecuteEvents.selectHandler);
        DrawableSuitsDiagnostics.Info($"Virtual cursor A press target={eventTarget.name}; hit={hitObject.name}; cursor={_cursor}");
    }

    private void EndVirtualPointerPress()
    {
        if (!_virtualPointerDown)
        {
            return;
        }

        _virtualPointerDown = false;
        var pressTarget = _virtualPointerPressTarget;
        var pointerData = CreateVirtualPointerEventData();
        _virtualPointerPressTarget = null;
        _virtualPointerSlider = null;

        if (pressTarget == null)
        {
            return;
        }

        ExecuteEvents.Execute(pressTarget, pointerData, ExecuteEvents.pointerUpHandler);
        var releaseTarget = TopClickableAtCursor();
        if (releaseTarget == pressTarget)
        {
            ExecuteEvents.Execute(pressTarget, pointerData, ExecuteEvents.pointerClickHandler);
        }

        DrawableSuitsDiagnostics.Info($"Virtual cursor A release target={pressTarget.name}; releaseTarget={releaseTarget?.name ?? "null"}; cursor={_cursor}");
    }

    private PointerEventData CreateVirtualPointerEventData()
    {
        var data = new PointerEventData(EventSystem.current ?? _editorEventSystem)
        {
            pointerId = -1003,
            position = _cursor,
            pressPosition = _cursor,
            button = PointerEventData.InputButton.Left
        };
        return data;
    }

    private GameObject TopClickableAtCursor()
    {
        var hits = RaycastEditorUi(_cursor);
        for (var i = 0; i < hits.Count; i++)
        {
            var target = ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[i].gameObject)
                ?? ExecuteEvents.GetEventHandler<ISubmitHandler>(hits[i].gameObject)
                ?? ExecuteEvents.GetEventHandler<ISelectHandler>(hits[i].gameObject);
            if (target != null)
            {
                return target;
            }
        }

        return null;
    }

    private List<RaycastResult> RaycastEditorUi(Vector2 screenPosition)
    {
        var hits = new List<RaycastResult>();
        var raycaster = _editorCanvasObject != null ? _editorCanvasObject.GetComponent<GraphicRaycaster>() : null;
        var eventSystem = EventSystem.current ?? _editorEventSystem;
        if (raycaster == null || eventSystem == null)
        {
            return hits;
        }

        var pointerData = new PointerEventData(eventSystem) { position = screenPosition };
        raycaster.Raycast(pointerData, hits);
        return hits;
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
        }

        var cursorOverPreview = IsCursorOverPreviewViewport();
        if (cursorOverPreview && DrawableSuitsInput.IsRightMousePressed())
        {
            _previewYaw += DrawableSuitsInput.MouseDeltaX() * 3f;
        }

        var scroll = DrawableSuitsInput.MouseScrollY();
        if (cursorOverPreview && Mathf.Abs(scroll) > 0.01f)
        {
            if (DrawableSuitsInput.IsKeyPressed(Key.LeftCtrl) || DrawableSuitsInput.IsKeyPressed(Key.RightCtrl))
            {
                _previewScale = Mathf.Clamp(_previewScale + scroll * 0.05f, 0.35f, 1.8f);
            }
            else
            {
                _brushSize = Mathf.Clamp(_brushSize + scroll * 2f, 1f, 96f);
                if (_brushSizeSlider != null)
                {
                    _brushSizeSlider.SetValue(_brushSize, false);
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

        var mousePainting = DrawableSuitsInput.IsLeftMousePressed();
        var gamepadPainting = Gamepad.current?.rightTrigger.ReadValue() > 0.55f;
        var painting = mousePainting || gamepadPainting;

        if (!painting)
        {
            _strokeActive = false;
            return;
        }

        if (!IsCursorOverPreviewViewport())
        {
            _strokeActive = false;
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

    private bool IsCursorOverPreviewViewport()
    {
        return _previewViewportRect != null && RectTransformUtility.RectangleContainsScreenPoint(_previewViewportRect, _cursor, null);
    }

    private bool TryGetTexturePreviewUv(Vector2 screenPosition, out Vector2 uv)
    {
        uv = default;
        _lastPreviewUvAvailable = false;
        if (_previewViewportRect == null)
        {
            return false;
        }

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_previewViewportRect, screenPosition, null, out var localPoint))
        {
            return false;
        }

        var rect = _previewViewportRect.rect;
        if (rect.width <= 0f || rect.height <= 0f)
        {
            return false;
        }

        var x = (localPoint.x - rect.xMin) / rect.width;
        var y = (localPoint.y - rect.yMin) / rect.height;
        if (x < 0f || x > 1f || y < 0f || y > 1f)
        {
            return false;
        }

        uv = new Vector2(Mathf.Clamp01(x), Mathf.Clamp01(y));
        _lastPreviewUv = uv;
        _lastPreviewUvAvailable = true;
        return true;
    }

    private void PaintAtCursor()
    {
        var texture = DrawableSuitsPlugin.Registry.GetEditableTexture(_selectedSuitId);
        if (texture == null)
        {
            RefreshEditorReadiness("paint preflight failed");
            UpdateUiState();
            return;
        }

        if (!TryGetTexturePreviewUv(_cursor, out var uv))
        {
            return;
        }

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
        if (_previewMaterial != null)
        {
            _previewMaterial.mainTexture = texture;
        }
        DrawableSuitsPlugin.Registry.ApplyEditedTexture(_selectedSuitId, _designName, false);
        if (_usingTexturePreview || !DrawableSuitsPlugin.ModConfig.EnableExperimentalModelPreview.Value)
        {
            UseTexturePreview("PaintAtCursor", false);
        }
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
            _selectedDesignIndex = _designFiles.FindIndex(path => string.Equals(Path.GetFileNameWithoutExtension(path), TextureTools.SanitizeFileName(_designName), StringComparison.OrdinalIgnoreCase));
            RefreshListButtons();
            UpdateUiState();
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
        RefreshFileLists();
        SetStatus("OS file import is disabled in-game. Place PNG/JPG files in BepInEx/config/DrawableSuits/Decals, then press Refresh Decals.", false);
        DrawableSuitsDiagnostics.Warn($"OS file dialog import is disabled for stability in {PluginInfo.Version}. EnableOsFileDialog config value is ignored. DecalsPath={DrawableSuitsPaths.Decals}");
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
        UpdateUiState();
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
        var editableTexture = _selectedSuitId >= 0 ? DrawableSuitsPlugin.Registry.GetEditableTexture(_selectedSuitId) : null;
        if (editableTexture == null)
        {
            DrawableSuitsDiagnostics.Warn($"Skipping preview setup [{context}] because no editable texture is available. hasEditableSuit={_hasEditableSuit}; selectedSuitId={_selectedSuitId}");
            DestroyPreview();
            UseTexturePreview(context, true);
            _canPaint = false;
            return;
        }

        if (!DrawableSuitsPlugin.ModConfig.EnableExperimentalModelPreview.Value)
        {
            DestroyPreview();
            UseTexturePreview(context, true);
            return;
        }

        if (!_hasPlayerModel)
        {
            DrawableSuitsDiagnostics.Warn($"Experimental model preview skipped [{context}] because local player model is missing; using texture preview instead.");
            DestroyPreview();
            UseTexturePreview(context, true);
            return;
        }

        RebuildPreview();
    }

    private void RebuildPreview()
    {
        DestroyPreview();
        _usingTexturePreview = false;
        _previewMode = "ModelExperimentalPending";
        var player = StartOfRound.Instance?.localPlayerController;
        var source = player?.thisPlayerModel;
        if (source == null)
        {
            SetStatus("Diagnostics: local player model not loaded; using texture preview.", true);
            DrawableSuitsDiagnostics.Warn("RebuildPreview aborted: local player model is null; using texture preview.");
            UseTexturePreview("model source missing", true);
            return;
        }

        DrawableSuitsDiagnostics.Info($"RebuildPreview baking mesh. selectedSuitId={_selectedSuitId}; sourceRenderer={source.name}; verticesBeforeBake={source.sharedMesh?.vertexCount ?? 0}");
        _previewMesh = new Mesh { name = "DrawableSuitsPreviewMesh" };
        source.BakeMesh(_previewMesh, true);
        if (_previewMesh.vertexCount == 0)
        {
            DrawableSuitsDiagnostics.Warn("Preview BakeMesh produced zero vertices; using diagnostic fallback preview mesh.");
            Destroy(_previewMesh);
            _previewMesh = CreateFallbackPreviewMesh();
        }
        var bakedBounds = NormalizePreviewMesh(_previewMesh, out var normalizedBounds);
        _previewLayer = SelectPreviewLayer();

        EnsurePreviewTexture();
        _previewRigRoot = new GameObject("DrawableSuitsPreviewRig");
        _previewRigRoot.hideFlags = HideFlags.HideAndDontSave;
        _previewRigRoot.transform.position = new Vector3(0f, -10000f, 0f);

        _previewPivotRoot = new GameObject("DrawableSuitsPreviewMeshPivot");
        _previewPivotRoot.hideFlags = HideFlags.HideAndDontSave;
        _previewPivotRoot.transform.SetParent(_previewRigRoot.transform, false);
        _previewPivotRoot.transform.localPosition = Vector3.zero;
        _previewPivotRoot.transform.localRotation = Quaternion.identity;
        _previewPivotRoot.transform.localScale = Vector3.one;
        SetLayerRecursively(_previewPivotRoot, _previewLayer);

        _previewRoot = new GameObject("DrawableSuitsSuitPreviewMesh");
        _previewRoot.hideFlags = HideFlags.HideAndDontSave;
        _previewRoot.transform.SetParent(_previewPivotRoot.transform, false);
        _previewRoot.transform.localPosition = Vector3.zero;
        _previewRoot.transform.localRotation = Quaternion.identity;
        _previewRoot.transform.localScale = Vector3.one;
        SetLayerRecursively(_previewRoot, _previewLayer);
        _previewRoot.AddComponent<MeshFilter>().sharedMesh = _previewMesh;
        _previewRenderer = _previewRoot.AddComponent<MeshRenderer>();
        _previewMaterial = CreatePreviewMaterial();
        _previewRenderer.sharedMaterial = _previewMaterial;
        _previewCollider = _previewRoot.AddComponent<MeshCollider>();
        _previewCollider.sharedMesh = _previewMesh;

        var cameraObject = new GameObject("DrawableSuitsPreviewCamera");
        cameraObject.hideFlags = HideFlags.HideAndDontSave;
        cameraObject.transform.SetParent(_previewRigRoot.transform, false);
        cameraObject.transform.localPosition = new Vector3(0f, 0.95f, -3.2f);
        cameraObject.transform.localRotation = Quaternion.identity;
        SetLayerRecursively(cameraObject, _previewLayer);
        _previewCamera = cameraObject.AddComponent<Camera>();
        _previewCamera.enabled = true;
        _previewCamera.clearFlags = CameraClearFlags.SolidColor;
        _previewCamera.backgroundColor = new Color(0.018f, 0.02f, 0.024f, 1f);
        _previewCamera.orthographic = true;
        _previewCamera.orthographicSize = CalculatePreviewOrthographicSize(_previewMesh.bounds);
        _previewCamera.nearClipPlane = 0.01f;
        _previewCamera.farClipPlane = 20f;
        _previewCamera.cullingMask = 1 << _previewLayer;
        _previewCamera.targetTexture = _previewTexture;

        if (_previewMaterial != null && _previewMaterial.shader != null && !_previewMaterial.shader.name.StartsWith("Unlit", StringComparison.OrdinalIgnoreCase))
        {
            var lightObject = new GameObject("DrawableSuitsPreviewLight");
            lightObject.hideFlags = HideFlags.HideAndDontSave;
            lightObject.transform.SetParent(_previewRigRoot.transform, false);
            lightObject.transform.localPosition = new Vector3(0f, 1.7f, -2.2f);
            lightObject.transform.localRotation = Quaternion.Euler(35f, -25f, 0f);
            SetLayerRecursively(lightObject, _previewLayer);
            _previewLight = lightObject.AddComponent<Light>();
            _previewLight.type = LightType.Directional;
            _previewLight.intensity = 1.15f;
            _previewLight.cullingMask = 1 << _previewLayer;
        }

        _hasPreviewCollider = _previewCollider != null;
        _canPaint = _selectedSuitId >= 0 && DrawableSuitsPlugin.Registry.GetEditableTexture(_selectedSuitId) != null;
        if (string.IsNullOrWhiteSpace(_statusMessage) || _statusMessage.StartsWith("Player model unavailable"))
        {
            SetStatus(string.Empty, false);
        }

        UpdatePreviewTransform();
        RenderPreviewFrame();
        if (IsPreviewRenderTextureBlack())
        {
            DrawableSuitsDiagnostics.Warn("Experimental model preview rendered a black center sample; falling back to deterministic texture preview.");
            DestroyPreview();
            UseTexturePreview("black RenderTexture fallback", true);
            return;
        }

        _usingTexturePreview = false;
        _previewMode = "ModelExperimental";
        DrawableSuitsDiagnostics.Info($"RebuildPreview complete. meshVertices={_previewMesh.vertexCount}; bakedBounds={bakedBounds}; normalizedBounds={normalizedBounds}; finalMeshBounds={_previewMesh.bounds}; previewLayer={_previewLayer}; mainCameraMask={Camera.main?.cullingMask.ToString() ?? "null"}; cameraEnabled={_previewCamera.enabled}; cameraMask={_previewCamera.cullingMask}; collider={_previewCollider != null}; material={_previewRenderer.sharedMaterial?.name ?? "null"}; shader={_previewRenderer.sharedMaterial?.shader?.name ?? "null"}; renderTexture={_previewTexture?.width}x{_previewTexture?.height}; rtCreated={_previewTexture?.IsCreated().ToString() ?? "null"}; previewCamera={DrawableSuitsPlugin.DescribeUnityObject(_previewCamera)}; viewport={(_previewViewportRect != null ? _previewViewportRect.rect.ToString() : "null")}");
    }

    private static Mesh CreateFallbackPreviewMesh()
    {
        var mesh = new Mesh { name = "DrawableSuitsFallbackPreviewMesh" };
        mesh.vertices = new[]
        {
            new Vector3(-0.45f, -0.75f, -0.12f), new Vector3(0.45f, -0.75f, -0.12f), new Vector3(0.45f, 0.75f, -0.12f), new Vector3(-0.45f, 0.75f, -0.12f),
            new Vector3(-0.45f, -0.75f, 0.12f), new Vector3(0.45f, -0.75f, 0.12f), new Vector3(0.45f, 0.75f, 0.12f), new Vector3(-0.45f, 0.75f, 0.12f)
        };
        mesh.triangles = new[]
        {
            0, 2, 1, 0, 3, 2,
            4, 5, 6, 4, 6, 7,
            0, 1, 5, 0, 5, 4,
            1, 2, 6, 1, 6, 5,
            2, 3, 7, 2, 7, 6,
            3, 0, 4, 3, 4, 7
        };
        mesh.uv = new[]
        {
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f),
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f)
        };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
    private static Bounds NormalizePreviewMesh(Mesh mesh, out Bounds normalizedBounds)
    {
        normalizedBounds = default;
        if (mesh == null || mesh.vertexCount == 0)
        {
            return default;
        }

        var bakedBounds = mesh.bounds;
        var vertices = mesh.vertices;
        var center = bakedBounds.center;
        for (var i = 0; i < vertices.Length; i++)
        {
            vertices[i] -= center;
        }

        mesh.vertices = vertices;
        mesh.RecalculateBounds();
        var recenteredBounds = mesh.bounds;
        if (recenteredBounds.center.sqrMagnitude > 0.0001f)
        {
            vertices = mesh.vertices;
            var secondaryCenter = recenteredBounds.center;
            for (var i = 0; i < vertices.Length; i++)
            {
                vertices[i] -= secondaryCenter;
            }

            mesh.vertices = vertices;
            mesh.RecalculateBounds();
        }

        try
        {
            mesh.RecalculateNormals();
        }
        catch (Exception ex)
        {
            DrawableSuitsDiagnostics.Exception("Preview mesh normal recalculation failed", ex);
        }

        normalizedBounds = mesh.bounds;
        var safeSize = normalizedBounds.size;
        if (safeSize.x < 0.01f) safeSize.x = 0.01f;
        if (safeSize.y < 0.01f) safeSize.y = 0.01f;
        if (safeSize.z < 0.01f) safeSize.z = 0.01f;
        mesh.bounds = new Bounds(Vector3.zero, safeSize);
        normalizedBounds = mesh.bounds;
        return bakedBounds;
    }

    private static int SelectPreviewLayer()
    {
        var mainCamera = Camera.main;
        var mainMask = mainCamera != null ? mainCamera.cullingMask : -1;
        for (var layer = 30; layer >= 0; layer--)
        {
            if ((mainMask & (1 << layer)) == 0)
            {
                DrawableSuitsDiagnostics.Info($"Selected preview layer {layer}; mainCamera={mainCamera?.name ?? "null"}; mainMask={mainMask}");
                return layer;
            }
        }

        DrawableSuitsDiagnostics.Warn($"No layer outside Camera.main culling mask was available; using layer 2 with normalized off-screen preview rig. mainCamera={mainCamera?.name ?? "null"}; mainMask={mainMask}");
        return 2;
    }

    private static float CalculatePreviewOrthographicSize(Bounds bounds)
    {
        var halfHeight = Mathf.Max(0.25f, bounds.extents.y);
        var halfWidth = Mathf.Max(0.25f, bounds.extents.x);
        var viewportAspect = 420f / 540f;
        var sizeForWidth = halfWidth / Mathf.Max(0.1f, viewportAspect);
        return Mathf.Clamp(Mathf.Max(halfHeight, sizeForWidth) * 1.35f, 0.75f, 6f);
    }

    private void EnsurePreviewTexture()
    {
        if (_previewTexture != null)
        {
            return;
        }

        _previewTexture = new RenderTexture(768, 768, 16, RenderTextureFormat.ARGB32)
        {
            name = "DrawableSuitsPreviewTexture",
            antiAliasing = 2
        };
        _previewTexture.Create();
        if (_previewImage != null)
        {
            _previewImage.texture = _previewTexture;
            _previewImage.uvRect = new Rect(0f, 0f, 1f, 1f);
            _previewImage.color = Color.white;
        }
    }

    private Texture2D EnsureCheckerTexture()
    {
        if (_checkerTexture != null)
        {
            return _checkerTexture;
        }

        _checkerTexture = new Texture2D(32, 32, TextureFormat.RGBA32, false)
        {
            name = "DrawableSuitsCheckerTexture",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Repeat,
            hideFlags = HideFlags.HideAndDontSave
        };

        var dark = new Color32(22, 25, 29, 255);
        var light = new Color32(42, 46, 52, 255);
        for (var y = 0; y < _checkerTexture.height; y++)
        {
            for (var x = 0; x < _checkerTexture.width; x++)
            {
                _checkerTexture.SetPixel(x, y, ((x / 8 + y / 8) % 2) == 0 ? dark : light);
            }
        }

        _checkerTexture.Apply(false, true);
        return _checkerTexture;
    }

    private void UseTexturePreview(string context, bool forceLog)
    {
        var texture = _selectedSuitId >= 0 ? DrawableSuitsPlugin.Registry.GetEditableTexture(_selectedSuitId) : null;
        var assignedTexture = texture != null ? (Texture)texture : EnsureCheckerTexture();
        _usingTexturePreview = true;
        _previewMode = texture != null ? "Texture" : "TextureFallbackNoEditableTexture";
        _canPaint = texture != null;

        if (_previewImage != null)
        {
            _previewImage.texture = assignedTexture;
            _previewImage.uvRect = texture != null ? new Rect(0f, 0f, 1f, 1f) : new Rect(0f, 0f, 10f, 10f);
            _previewImage.color = Color.white;
            _previewImage.raycastTarget = true;
        }

        var assignment = $"{_previewMode}; assigned={assignedTexture?.name ?? "null"}; editable={DescribeEditableTexture()}; rawImage={DescribePreviewImageTexture()}";
        if (forceLog || !string.Equals(_lastPreviewAssignmentLog, assignment, StringComparison.Ordinal))
        {
            DrawableSuitsDiagnostics.Info($"TexturePreview[{context}]: {assignment}; viewport={(_previewViewportRect != null ? _previewViewportRect.rect.ToString() : "null")}");
            _lastPreviewAssignmentLog = assignment;
        }
    }

    private string DescribeEditableTexture()
    {
        var texture = _selectedSuitId >= 0 ? DrawableSuitsPlugin.Registry.GetEditableTexture(_selectedSuitId) : null;
        if (texture == null)
        {
            return "null";
        }

        return $"{texture.name} {texture.width}x{texture.height} {texture.format}";
    }

    private string DescribePreviewImageTexture()
    {
        var texture = _previewImage != null ? _previewImage.texture : null;
        return texture != null ? $"{texture.name} {texture.width}x{texture.height}" : "null";
    }

    private bool IsPreviewRenderTextureBlack()
    {
        if (_previewTexture == null || !_previewTexture.IsCreated())
        {
            return true;
        }

        var width = Mathf.Min(96, _previewTexture.width);
        var height = Mathf.Min(96, _previewTexture.height);
        var x = Mathf.Max(0, (_previewTexture.width - width) / 2);
        var y = Mathf.Max(0, (_previewTexture.height - height) / 2);
        var previousActive = RenderTexture.active;
        Texture2D sample = null;
        try
        {
            RenderTexture.active = _previewTexture;
            sample = new Texture2D(width, height, TextureFormat.RGBA32, false);
            sample.ReadPixels(new Rect(x, y, width, height), 0, 0, false);
            sample.Apply(false, false);
            var pixels = sample.GetPixels32();
            for (var i = 0; i < pixels.Length; i++)
            {
                var pixel = pixels[i];
                if (pixel.a > 8 && pixel.r + pixel.g + pixel.b > 28)
                {
                    DrawableSuitsDiagnostics.Info($"Experimental model preview readback found non-black pixel at index={i}; color=({pixel.r},{pixel.g},{pixel.b},{pixel.a}).");
                    return false;
                }
            }

            DrawableSuitsDiagnostics.Warn($"Experimental model preview readback was black. sampleRect=({x},{y},{width},{height}); renderTexture={_previewTexture.width}x{_previewTexture.height}; camera={DrawableSuitsPlugin.DescribeUnityObject(_previewCamera)}; layer={_previewLayer}");
            return true;
        }
        catch (Exception ex)
        {
            DrawableSuitsDiagnostics.Exception("Experimental model preview black-frame readback failed", ex);
            return false;
        }
        finally
        {
            RenderTexture.active = previousActive;
            if (sample != null)
            {
                Destroy(sample);
            }
        }
    }

    private Material CreatePreviewMaterial()
    {
        var runtimeMaterial = DrawableSuitsPlugin.Registry.GetRuntimeMaterial(_selectedSuitId);
        var shader = Shader.Find("Unlit/Texture") ?? Shader.Find("Unlit/Transparent") ?? Shader.Find("Standard");
        Material material;
        if (shader != null)
        {
            material = new Material(shader) { name = "DrawableSuitsPreviewMaterial" };
        }
        else if (runtimeMaterial != null)
        {
            material = new Material(runtimeMaterial) { name = "DrawableSuitsPreviewMaterial" };
        }
        else
        {
            material = new Material(Shader.Find("Diffuse")) { name = "DrawableSuitsPreviewMaterial" };
        }

        var texture = DrawableSuitsPlugin.Registry.GetEditableTexture(_selectedSuitId);
        if (texture == null)
        {
            texture = runtimeMaterial?.mainTexture as Texture2D;
        }

        material.mainTexture = texture;
        material.color = Color.white;
        if (material.HasProperty("_Cull"))
        {
            material.SetInt("_Cull", 0);
        }
        return material;
    }

    private static void SetLayerRecursively(GameObject target, int layer)
    {
        if (target == null)
        {
            return;
        }

        target.layer = layer;
        foreach (Transform child in target.transform)
        {
            if (child != null)
            {
                SetLayerRecursively(child.gameObject, layer);
            }
        }
    }

    private void UpdatePreviewTransform()
    {
        if (_previewPivotRoot == null || _previewRoot == null || _previewMesh == null)
        {
            return;
        }

        var bounds = _previewMesh.bounds;
        var maxSize = Mathf.Max(0.01f, Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z)));
        _previewPivotRoot.transform.localPosition = Vector3.zero;
        _previewPivotRoot.transform.localRotation = Quaternion.Euler(0f, _previewYaw, 0f);
        _previewPivotRoot.transform.localScale = Vector3.one * _previewScale;
        _previewRoot.transform.localPosition = Vector3.zero;
        _previewRoot.transform.localRotation = Quaternion.identity;
        _previewRoot.transform.localScale = Vector3.one;

        if (_previewCamera != null)
        {
            var distance = Mathf.Clamp(maxSize * 2.4f, 3.2f, 12f);
            _previewCamera.transform.localPosition = new Vector3(0f, 0f, -distance);
            _previewCamera.transform.localRotation = Quaternion.identity;
            _previewCamera.orthographicSize = CalculatePreviewOrthographicSize(bounds) / Mathf.Max(0.2f, _previewScale);
            _previewCamera.enabled = true;
        }
    }

    private void RenderPreviewFrame()
    {
        if (_previewCamera == null || _previewTexture == null)
        {
            return;
        }

        if (!_previewTexture.IsCreated())
        {
            _previewTexture.Create();
        }

        _previewCamera.enabled = true;
        _previewCamera.targetTexture = _previewTexture;
        _previewCamera.cullingMask = 1 << _previewLayer;
        var previousActive = RenderTexture.active;
        try
        {
            _previewCamera.Render();
        }
        catch (Exception ex)
        {
            DrawableSuitsDiagnostics.Exception("Preview camera Render failed", ex);
        }
        finally
        {
            RenderTexture.active = previousActive;
        }
    }

    private void DestroyPreview()
    {
        if (_previewRoot != null)
        {
            Destroy(_previewRoot);
            _previewRoot = null;
        }

        if (_previewCamera != null)
        {
            _previewCamera.enabled = false;
        }

        if (_previewRigRoot != null)
        {
            Destroy(_previewRigRoot);
            _previewRigRoot = null;
        }

        _previewPivotRoot = null;

        if (_previewMesh != null)
        {
            Destroy(_previewMesh);
            _previewMesh = null;
        }

        if (_previewMaterial != null)
        {
            Destroy(_previewMaterial);
            _previewMaterial = null;
        }

        if (_previewTexture != null)
        {
            if (_previewImage != null && ReferenceEquals(_previewImage.texture, _previewTexture))
            {
                _previewImage.texture = EnsureCheckerTexture();
                _previewImage.uvRect = new Rect(0f, 0f, 10f, 10f);
                _previewImage.color = Color.white;
            }
            _previewTexture.Release();
            Destroy(_previewTexture);
            _previewTexture = null;
        }

        _previewCollider = null;
        _previewRenderer = null;
        _previewCamera = null;
        _previewLight = null;
        _hasPreviewCollider = false;
        _canPaint = DrawableSuitsPlugin.Registry != null && _selectedSuitId >= 0 && DrawableSuitsPlugin.Registry.GetEditableTexture(_selectedSuitId) != null;
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
        var previewSize = _previewViewportRect != null ? _previewViewportRect.rect.size.ToString() : "null";
        var canvasSize = _canvasRect != null ? _canvasRect.rect.size.ToString() : "null";
        var activeModule = EventSystem.current?.currentInputModule;
        DrawableSuitsDiagnostics.Info($"CanvasState[{context}]: activeSelf={_editorCanvasObject.activeSelf}; activeInHierarchy={_editorCanvasObject.activeInHierarchy}; canvasMode={canvas?.renderMode.ToString() ?? "null"}; sortingOrder={canvas?.sortingOrder.ToString() ?? "null"}; childCount={_editorCanvasObject.transform.childCount}; canvasSize={canvasSize}; panelSize={panelSize}; previewSize={previewSize}; raycaster={raycaster != null && raycaster.enabled}; eventSystem={eventSystem?.name ?? "null"}; current={EventSystem.current?.name ?? "null"}; inputModule={activeModule?.GetType().Name ?? "null"}; currentSelected={EventSystem.current?.currentSelectedGameObject?.name ?? "null"}");
    }

    private void LogUiInputDiagnosticsIfNeeded()
    {
        if (Time.unscaledTime - _lastUiDiagnosticsTime < 1f)
        {
            return;
        }

        _lastUiDiagnosticsTime = Time.unscaledTime;
        var pointer = _cursor;
        var usingMousePointer = string.Equals(_pointerSource, "Mouse", StringComparison.OrdinalIgnoreCase);

        var hitNames = "none";
        var raycaster = _editorCanvasObject != null ? _editorCanvasObject.GetComponent<GraphicRaycaster>() : null;
        if (raycaster != null && EventSystem.current != null)
        {
            var pointerData = new PointerEventData(EventSystem.current) { position = pointer };
            var hits = new List<RaycastResult>();
            raycaster.Raycast(pointerData, hits);
            if (hits.Count > 0)
            {
                var names = new List<string>();
                for (var i = 0; i < hits.Count && i < 6; i++)
                {
                    names.Add(hits[i].gameObject != null ? hits[i].gameObject.name : "null");
                }
                hitNames = string.Join(", ", names);
            }
        }

        var activeModule = EventSystem.current?.currentInputModule;
        var previewUvSummary = TryGetTexturePreviewUv(pointer, out var previewUv) ? previewUv.ToString() : "none";
        DrawableSuitsDiagnostics.Info($"UiInputDiagnostics: currentEventSystem={EventSystem.current?.name ?? "null"}; activeModule={activeModule?.GetType().Name ?? "null"}; selected={EventSystem.current?.currentSelectedGameObject?.name ?? "null"}; pointerSource={_pointerSource}; pointer={pointer}; usingMousePointer={usingMousePointer}; mouseAvailable={_mousePositionAvailable}; mousePosition={_lastMousePosition}; gamepadStick={_lastGamepadStick}; virtualCursor={_cursor}; cursorVisible={Cursor.visible}; cursorLock={Cursor.lockState}; canvasScale={_editorCanvasObject?.GetComponent<Canvas>()?.scaleFactor.ToString("0.###") ?? "null"}; raycastHits=[{hitNames}]; overPanel={IsCursorOverEditorPanel()}; overPreview={IsCursorOverPreviewViewport()}; previewUv={previewUvSummary}; lastPreviewUv={(_lastPreviewUvAvailable ? _lastPreviewUv.ToString() : "none")}; previewMode={_previewMode}; editableTexture={DescribeEditableTexture()}; previewImageTexture={DescribePreviewImageTexture()}; previewRect={(_previewViewportRect != null ? _previewViewportRect.rect.ToString() : "null")}; previewCamera={DrawableSuitsPlugin.DescribeUnityObject(_previewCamera)}; previewCameraEnabled={_previewCamera?.enabled.ToString() ?? "null"}; previewLayer={_previewLayer}; renderTexture={_previewTexture?.width.ToString() ?? "null"}x{_previewTexture?.height.ToString() ?? "null"}");
    }

    private static void LogEditorControlTree(Transform root)
    {
        if (root == null)
        {
            return;
        }

        var logged = 0;
        foreach (var rect in root.GetComponentsInChildren<RectTransform>(true))
        {
            if (logged >= 32)
            {
                break;
            }

            var text = rect.GetComponent<Text>();
            var image = rect.GetComponent<Image>();
            var selectable = rect.GetComponent<Selectable>();
            var canvasRenderer = rect.GetComponent<CanvasRenderer>();
            var textSummary = text != null ? $"; text='{text.text}'; textColor={text.color}; fontSize={text.fontSize}" : string.Empty;
            var imageSummary = image != null ? $"; imageColor={image.color}" : string.Empty;
            var selectableSummary = selectable != null ? $"; selectable={selectable.GetType().Name}; interactable={selectable.interactable}" : string.Empty;
            var alphaSummary = canvasRenderer != null ? $"; alpha={canvasRenderer.GetAlpha():0.###}" : string.Empty;
            DrawableSuitsDiagnostics.Info($"EditorControl[{logged}]: name={rect.name}; activeSelf={rect.gameObject.activeSelf}; activeInHierarchy={rect.gameObject.activeInHierarchy}; anchors=({rect.anchorMin},{rect.anchorMax}); pos={rect.anchoredPosition}; size={rect.rect.size}{alphaSummary}{textSummary}{imageSummary}{selectableSummary}");
            logged++;
        }
    }

    private static bool WasGamepadPressed(Func<Gamepad, ButtonControl> accessor)
    {
        var gamepad = Gamepad.current;
        return gamepad != null && accessor(gamepad).wasPressedThisFrame;
    }
}
