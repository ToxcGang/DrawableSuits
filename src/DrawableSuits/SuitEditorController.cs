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
    private float _lastPaintDiagnosticsTime;
    private string _lastPaintDiagnosticsKey = string.Empty;
    private float _lastMissingDecalWarningTime;
    private float _lastGameplayActionRelockLogTime;

    private static readonly string[] GameplayInputActionNames =
    {
        "Move",
        "Look",
        "Jump",
        "Crouch",
        "Interact",
        "ItemSecondaryUse",
        "ItemTertiaryUse",
        "ActivateItem",
        "Discard",
        "SwitchItem",
        "ScrollMouse",
        "ItemScroll",
        "NextItem",
        "PreviousItem",
        "PingScan",
        "Scan",
        "InspectItem",
        "UseUtilitySlot",
        "ItemPrimaryUse",
        "ItemInteract",
        "BuildMode",
        "ConfirmBuildMode",
        "Delete",
        "Emote1",
        "Emote2",
        "SetFreeCamera",
        "SpeedCheat"
    };

    private readonly List<DisabledGameplayActionState> _disabledGameplayActions = new();

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
    private bool _uvFallbackMode;
    private GameObject _worldEditorCameraObject;
    private Camera _worldEditorCamera;
    private GameObject _worldPaintProxyObject;
    private Mesh _worldPaintMesh;
    private MeshCollider _worldPaintCollider;
    private GameObject _worldBrushMarker;
    private Material _worldBrushMarkerMaterial;
    private int _worldPaintLayer = 30;
    private float _worldCameraYaw;
    private float _worldCameraPitch = 12f;
    private float _worldCameraDistance = 3.4f;
    private bool _worldPreviewReady;
    private Vector2 _lastWorldPaintUv;
    private bool _lastWorldRaycastHit;
    private Vector3 _lastWorldHitPoint;
    private Vector3 _lastWorldHitNormal;
    private int _designListPage;
    private int _decalListPage;
    private readonly List<RendererRestoreState> _rendererRestoreStates = new();
    private Texture2D _loadedDecal;

    private GameObject _editorCanvasObject;
    private RectTransform _canvasRect;
    private RectTransform _panelRect;
    private RectTransform _previewViewportRect;
    private RectTransform _brushIndicator;
    private Image _brushIndicatorImage;
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
    private Button _uvFallbackButton;
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

    private sealed class RendererRestoreState
    {
        internal Renderer Renderer;
        internal bool Enabled;
    }

    private sealed class DisabledGameplayActionState    {
        internal InputAction Action;
        internal bool WasEnabled;
        internal string Name;
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
        if (IsWorldThirdPersonMode)
        {
            UpdateWorldPaintProxy(false);
            UpdateWorldEditorCamera(false);
            UpdateWorldBrushMarker();
        }
        else
        {
            UpdateBrushIndicator();
        }
        HandlePaintingInput();
        if (DrawableSuitsPlugin.ModConfig.EnableExperimentalModelPreview.Value && !_usingTexturePreview && !IsWorldThirdPersonMode)
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
        _uvFallbackMode = DrawableSuitsPlugin.ModConfig.StartInUvFallbackMode.Value;
        EnsureValidToolForCurrentState($"open from {source}");
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
        _hasPreviewCollider = _previewCollider != null || _worldPaintCollider != null;
        var editableTexture = _selectedSuitId >= 0 ? DrawableSuitsPlugin.Registry.GetEditableTexture(_selectedSuitId) : null;
        _hasEditableSuit = editableTexture != null;
        _canPaint = editableTexture != null && (_usingTexturePreview || _worldPreviewReady || _previewCollider != null);

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
        _panelRect.sizeDelta = new Vector2(620f, 900f);

        var panelImage = panel.GetComponent<Image>();
        panelImage.color = new Color(0.025f, 0.03f, 0.035f, 0.88f);

        const float leftX = 18f;
        const float leftW = 274f;
        const float rightX = 314f;
        const float rightW = 286f;

        CreateAnchoredText(panel.transform, "Title", $"{PluginInfo.Name} {PluginInfo.Version}", 24, FontStyle.Bold, TextAnchor.MiddleLeft, new Rect(leftX, 12f, 360f, 34f), new Color(1f, 0.62f, 0.25f, 1f));
        CreateAnchoredButton(panel.transform, "Close", new Rect(512f, 14f, 88f, 34f), CloseEditor);
        _suitLabel = CreateAnchoredText(panel.transform, "SuitLabel", string.Empty, 18, FontStyle.Normal, TextAnchor.MiddleLeft, new Rect(leftX, 54f, 420f, 28f), Color.white);
        _statusLabel = CreateAnchoredText(panel.transform, "StatusLabel", string.Empty, 15, FontStyle.Normal, TextAnchor.UpperLeft, new Rect(leftX, 86f, 570f, 46f), new Color(1f, 0.58f, 0.28f, 1f));
        _statusLabel.color = new Color(1f, 0.58f, 0.28f, 1f);
        _diagnosticsLabel = CreateAnchoredText(panel.transform, "DiagnosticsLabel", string.Empty, 12, FontStyle.Normal, TextAnchor.UpperLeft, new Rect(leftX, 138f, leftW, 128f), new Color(0.78f, 0.86f, 1f, 1f));
        _diagnosticsLabel.color = new Color(0.78f, 0.86f, 1f, 1f);

        CreateAnchoredButton(panel.transform, "Previous", new Rect(leftX, 282f, 82f, 34f), () => SelectAdjacentSuit(-1));
        CreateAnchoredButton(panel.transform, "Use Current", new Rect(leftX + 90f, 282f, 112f, 34f), () => SelectSuit(DrawableSuitsPlugin.Registry.GetLocalSuitId()));
        CreateAnchoredButton(panel.transform, "Next", new Rect(leftX + 210f, 282f, 72f, 34f), () => SelectAdjacentSuit(1));

        CreateAnchoredText(panel.transform, "ToolHeader", "Tool", 16, FontStyle.Bold, TextAnchor.MiddleLeft, new Rect(leftX, 334f, leftW, 24f), Color.white);
        _paintButton = CreateAnchoredButton(panel.transform, "Paint", new Rect(leftX, 362f, 84f, 34f), () => SetTool(EditorTool.Paint));
        _eraseButton = CreateAnchoredButton(panel.transform, "Erase", new Rect(leftX + 92f, 362f, 84f, 34f), () => SetTool(EditorTool.Erase));
        _decalButton = CreateAnchoredButton(panel.transform, "Decal", new Rect(leftX + 184f, 362f, 84f, 34f), () => SetTool(EditorTool.Decal));

        CreateAnchoredText(panel.transform, "BrushHeader", "Brush", 16, FontStyle.Bold, TextAnchor.MiddleLeft, new Rect(leftX, 414f, leftW, 24f), Color.white);
        _brushSizeLabel = CreateAnchoredText(panel.transform, "BrushSizeLabel", string.Empty, 14, FontStyle.Normal, TextAnchor.MiddleLeft, new Rect(leftX, 444f, 94f, 24f), Color.white);
        _brushSizeSlider = CreateAnchoredSlider(panel.transform, "BrushSize", 1f, 96f, _brushSize, new Rect(leftX + 100f, 446f, 174f, 24f), value => _brushSize = value);
        _brushOpacityLabel = CreateAnchoredText(panel.transform, "BrushOpacityLabel", string.Empty, 14, FontStyle.Normal, TextAnchor.MiddleLeft, new Rect(leftX, 478f, 94f, 24f), Color.white);
        _brushOpacitySlider = CreateAnchoredSlider(panel.transform, "BrushOpacity", 0.05f, 1f, _brushOpacity, new Rect(leftX + 100f, 480f, 174f, 24f), value => _brushOpacity = value);

        CreateAnchoredText(panel.transform, "ColorHeader", "Color", 16, FontStyle.Bold, TextAnchor.MiddleLeft, new Rect(leftX, 518f, leftW, 24f), Color.white);
        CreateAnchoredText(panel.transform, "RedLabel", "Red", 13, FontStyle.Normal, TextAnchor.MiddleLeft, new Rect(leftX, 548f, 54f, 22f), Color.white);
        _redSlider = CreateAnchoredSlider(panel.transform, "Red", 0f, 1f, _brushColor.r, new Rect(leftX + 58f, 548f, 174f, 24f), value => { _brushColor.r = value; UpdateColorUi(); });
        CreateAnchoredText(panel.transform, "GreenLabel", "Green", 13, FontStyle.Normal, TextAnchor.MiddleLeft, new Rect(leftX, 582f, 54f, 22f), Color.white);
        _greenSlider = CreateAnchoredSlider(panel.transform, "Green", 0f, 1f, _brushColor.g, new Rect(leftX + 58f, 582f, 174f, 24f), value => { _brushColor.g = value; UpdateColorUi(); });
        CreateAnchoredText(panel.transform, "BlueLabel", "Blue", 13, FontStyle.Normal, TextAnchor.MiddleLeft, new Rect(leftX, 616f, 54f, 22f), Color.white);
        _blueSlider = CreateAnchoredSlider(panel.transform, "Blue", 0f, 1f, _brushColor.b, new Rect(leftX + 58f, 616f, 174f, 24f), value => { _brushColor.b = value; UpdateColorUi(); });
        _colorSwatch = CreateAnchoredColorSwatch(panel.transform, new Rect(leftX + 238f, 548f, 44f, 92f));

        _uvFallbackButton = CreateAnchoredButton(panel.transform, "Use UV Fallback", new Rect(rightX, 54f, 150f, 34f), ToggleUvFallback);
        CreateAnchoredText(panel.transform, "WorldHelp", "Third-person mode: aim at the visible suit and hold left mouse or right trigger to paint. Right mouse/right stick or bumpers orbit. Wheel zooms; Ctrl+wheel changes brush size.", 13, FontStyle.Normal, TextAnchor.UpperLeft, new Rect(rightX, 96f, rightW, 76f), new Color(0.86f, 0.9f, 0.94f, 1f));

        CreateAnchoredText(panel.transform, "DecalHeader", "Decal", 16, FontStyle.Bold, TextAnchor.MiddleLeft, new Rect(rightX, 188f, rightW, 24f), Color.white);
        _decalSizeLabel = CreateAnchoredText(panel.transform, "DecalSizeLabel", string.Empty, 14, FontStyle.Normal, TextAnchor.MiddleLeft, new Rect(rightX, 218f, 112f, 24f), Color.white);
        _decalSizeSlider = CreateAnchoredSlider(panel.transform, "DecalSize", 16f, 512f, _decalSize, new Rect(rightX + 120f, 220f, 160f, 24f), value => _decalSize = value);
        _decalRotationLabel = CreateAnchoredText(panel.transform, "DecalRotationLabel", string.Empty, 14, FontStyle.Normal, TextAnchor.MiddleLeft, new Rect(rightX, 252f, 112f, 24f), Color.white);
        _decalRotationSlider = CreateAnchoredSlider(panel.transform, "DecalRotation", -180f, 180f, _decalRotation, new Rect(rightX + 120f, 254f, 160f, 24f), value => _decalRotation = value);
        CreateAnchoredButton(panel.transform, "Refresh", new Rect(rightX, 290f, 96f, 34f), RefreshFileLists);
        CreateAnchoredButton(panel.transform, "Refresh Decals", new Rect(rightX + 104f, 290f, 134f, 34f), ImportDecalFromDialog);
        _decalListContent = CreateAnchoredScrollList(panel.transform, "DecalList", new Rect(rightX, 334f, rightW, 132f));

        CreateAnchoredText(panel.transform, "DesignHeader", "Design Name", 16, FontStyle.Bold, TextAnchor.MiddleLeft, new Rect(rightX, 486f, rightW, 24f), Color.white);
        _designNameInput = CreateAnchoredInputField(panel.transform, _designName, new Rect(rightX, 516f, rightW, 34f));
        _designNameInput.onValueChanged.AddListener(value => _designName = value);

        CreateAnchoredButton(panel.transform, "Undo", new Rect(rightX, 568f, 84f, 34f), Undo);
        CreateAnchoredButton(panel.transform, "Redo", new Rect(rightX + 92f, 568f, 84f, 34f), Redo);
        _resetButton = CreateAnchoredButton(panel.transform, "Reset", new Rect(rightX + 184f, 568f, 84f, 34f), () =>
        {
            SaveUndo();
            DrawableSuitsPlugin.Registry.ResetSuit(_selectedSuitId);
            _redo.Clear();
            UpdateUiState();
        });

        _applyButton = CreateAnchoredButton(panel.transform, "Apply", new Rect(rightX, 610f, 84f, 34f), () => DrawableSuitsPlugin.Registry.ApplyEditedTexture(_selectedSuitId, _designName, true));
        _saveButton = CreateAnchoredButton(panel.transform, "Save", new Rect(rightX + 92f, 610f, 84f, 34f), SaveDesign);
        _loadButton = CreateAnchoredButton(panel.transform, "Load", new Rect(rightX + 184f, 610f, 84f, 34f), LoadSelectedDesign);

        CreateAnchoredText(panel.transform, "SavedDesignsHeader", "Saved Designs", 16, FontStyle.Bold, TextAnchor.MiddleLeft, new Rect(rightX, 664f, rightW, 24f), Color.white);
        _designListContent = CreateAnchoredScrollList(panel.transform, "DesignList", new Rect(rightX, 694f, rightW, 142f));

        var fallbackPreview = CreateUiObject("PreviewViewport", panel.transform, typeof(RectTransform), typeof(Image));
        _previewViewportRect = fallbackPreview.GetComponent<RectTransform>();
        SetAnchoredRect(_previewViewportRect, new Rect(leftX, 654f, 274f, 190f));
        var previewBackground = fallbackPreview.GetComponent<Image>();
        previewBackground.color = new Color(0.025f, 0.028f, 0.032f, 1f);
        previewBackground.raycastTarget = true;

        var previewImageObject = CreateUiObject("TexturePreviewImage", fallbackPreview.transform, typeof(RectTransform), typeof(RawImage));
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

        _brushIndicator = CreateUiObject("BrushIndicator", fallbackPreview.transform, typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
        _brushIndicator.sizeDelta = new Vector2(16f, 16f);
        _brushIndicatorImage = _brushIndicator.GetComponent<Image>();
        _brushIndicatorImage.color = new Color(1f, 1f, 1f, 0.32f);
        _brushIndicatorImage.raycastTarget = false;
        _brushIndicator.gameObject.SetActive(false);
        fallbackPreview.SetActive(false);

        CreateAnchoredText(panel.transform, "ControllerHelp", "Controller: View/Back+Y open/close, left stick cursor, A clicks UI, right trigger paints suit, right stick/bumpers orbit, X undo, Start save.", 13, FontStyle.Normal, TextAnchor.UpperLeft, new Rect(leftX, 858f, 574f, 36f), Color.white);

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
        DrawableSuitsDiagnostics.Info($"BuildEditorCanvas complete. childCount={_editorCanvasObject.transform.childCount}; panelChildren={panel.transform.childCount}; graphicRaycaster={_editorCanvasObject.GetComponent<GraphicRaycaster>() != null}; mode=compactThirdPerson");
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
        var root = CreateUiObject(name, parent, typeof(RectTransform), typeof(Image));
        var rootRect = root.GetComponent<RectTransform>();
        SetAnchoredRect(rootRect, rect);
        var image = root.GetComponent<Image>();
        image.color = new Color(0.06f, 0.065f, 0.07f, 0.9f);
        image.raycastTarget = true;
        return rootRect;
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

    private void CaptureAndLockGameplayActions()
    {
        _disabledGameplayActions.Clear();
        var asset = InputSystem.actions;
        if (asset == null)
        {
            DrawableSuitsDiagnostics.Info("Global gameplay action lock skipped: InputSystem.actions is null.");
            return;
        }

        var found = 0;
        var disabled = 0;
        var missing = new List<string>();
        for (var i = 0; i < GameplayInputActionNames.Length; i++)
        {
            var name = GameplayInputActionNames[i];
            InputAction action = null;
            try
            {
                action = asset.FindAction(name, false);
            }
            catch (Exception ex)
            {
                DrawableSuitsDiagnostics.Exception($"Global gameplay action lookup failed for '{name}'", ex);
            }

            if (action == null)
            {
                missing.Add(name);
                continue;
            }

            found++;
            var wasEnabled = action.enabled;
            _disabledGameplayActions.Add(new DisabledGameplayActionState
            {
                Action = action,
                WasEnabled = wasEnabled,
                Name = name
            });

            if (!wasEnabled)
            {
                continue;
            }

            try
            {
                action.Disable();
                disabled++;
            }
            catch (Exception ex)
            {
                DrawableSuitsDiagnostics.Exception($"Failed to disable global gameplay action '{name}'", ex);
            }
        }

        DrawableSuitsDiagnostics.Info($"Global gameplay actions locked for editor. found={found}; disabled={disabled}; missing=[{string.Join(", ", missing)}]");
    }

    private void ReapplyGameplayActionLock()
    {
        if (_disabledGameplayActions.Count == 0)
        {
            return;
        }

        var relocked = 0;
        for (var i = 0; i < _disabledGameplayActions.Count; i++)
        {
            var state = _disabledGameplayActions[i];
            if (state?.Action == null || !state.Action.enabled)
            {
                continue;
            }

            try
            {
                state.Action.Disable();
                relocked++;
            }
            catch (Exception ex)
            {
                DrawableSuitsDiagnostics.Exception($"Failed to re-disable global gameplay action '{state.Name}'", ex);
            }
        }

        if (relocked > 0 && Time.unscaledTime - _lastGameplayActionRelockLogTime > 0.75f)
        {
            _lastGameplayActionRelockLogTime = Time.unscaledTime;
            DrawableSuitsDiagnostics.Warn($"Re-disabled {relocked} global gameplay actions while DrawableSuits editor is open.");
        }
    }

    private void RestoreGameplayActions()
    {
        if (_disabledGameplayActions.Count == 0)
        {
            return;
        }

        var restoredEnabled = 0;
        var restoredDisabled = 0;
        for (var i = 0; i < _disabledGameplayActions.Count; i++)
        {
            var state = _disabledGameplayActions[i];
            if (state?.Action == null)
            {
                continue;
            }

            try
            {
                if (state.WasEnabled && !state.Action.enabled)
                {
                    state.Action.Enable();
                    restoredEnabled++;
                }
                else if (!state.WasEnabled && state.Action.enabled)
                {
                    state.Action.Disable();
                    restoredDisabled++;
                }
            }
            catch (Exception ex)
            {
                DrawableSuitsDiagnostics.Exception($"Failed to restore global gameplay action '{state.Name}'", ex);
            }
        }

        DrawableSuitsDiagnostics.Info($"Global gameplay actions restored after editor close. tracked={_disabledGameplayActions.Count}; reEnabled={restoredEnabled}; reDisabled={restoredDisabled}");
        _disabledGameplayActions.Clear();
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
        CaptureAndLockGameplayActions();
        ApplyPlayerInputLock(player, true);
        DrawableSuitsDiagnostics.Info($"Player input locked for editor. player={DrawableSuitsPlugin.DescribeUnityObject(player)}; previousMove={_previousDisableMoveInput}; previousLook={_previousDisableLookInput}; previousInteract={_previousDisableInteract}; previousSpecialMenu={_previousInSpecialMenu}; movementActionsWereEnabled={_previousMovementActionsEnabled}");
    }

    private void ReapplyPlayerInputLock()
    {
        ReapplyGameplayActionLock();
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
            RestoreGameplayActions();
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

        RestoreGameplayActions();
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

        if (_usingTexturePreview)
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
        SetButtonLabel(_uvFallbackButton, _uvFallbackMode ? "Use Third Person" : "Use UV Fallback");

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
            $"UV fallback mode: {_uvFallbackMode}",
            $"World camera found: {_worldEditorCamera != null}",
            $"World paint collider found: {_worldPaintCollider != null}",
            $"World raycast hit: {_lastWorldRaycastHit}",
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

    private static void SetButtonLabel(Button button, string label)
    {
        if (button == null)
        {
            return;
        }

        var text = button.GetComponentInChildren<Text>(true);
        if (text != null)
        {
            text.text = label;
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

        UpdateBrushIndicator();
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
        if (tool == EditorTool.Decal && _loadedDecal == null)
        {
            WarnMissingDecal("tool selection");
            _tool = EditorTool.Paint;
            UpdateToolButtons();
            UpdateBrushIndicator();
            return;
        }

        _tool = tool;
        if (tool != EditorTool.Decal && _statusMessage.StartsWith("Select a decal", StringComparison.OrdinalIgnoreCase))
        {
            SetStatus(BuildReadinessStatus(), false);
        }

        UpdateToolButtons();
        UpdateBrushIndicator();
    }

    private void CycleTool()
    {
        if (_tool == EditorTool.Paint)
        {
            SetTool(EditorTool.Erase);
            return;
        }

        if (_tool == EditorTool.Erase)
        {
            SetTool(_loadedDecal != null ? EditorTool.Decal : EditorTool.Paint);
            return;
        }

        SetTool(EditorTool.Paint);
    }

    private void EnsureValidToolForCurrentState(string context)
    {
        if (_tool != EditorTool.Decal || _loadedDecal != null)
        {
            return;
        }

        _tool = EditorTool.Paint;
        SetStatus("Ready. Paint is selected because no decal is loaded.", false);
        UpdateToolButtons();
        DrawableSuitsDiagnostics.Info($"Tool reset to Paint. context={context}; reason=no loaded decal");
    }

    private void WarnMissingDecal(string context)
    {
        SetStatus("Select a decal before using Decal. Paint mode is active until a decal is selected.", false);
        if (Time.unscaledTime - _lastMissingDecalWarningTime > 0.75f)
        {
            _lastMissingDecalWarningTime = Time.unscaledTime;
            DrawableSuitsDiagnostics.Warn($"Decal tool unavailable because no decal is loaded. context={context}; decalCount={_decalFiles.Count}; selectedDecalIndex={_selectedDecalIndex}");
        }
    }

    private void RefreshListButtons()
    {
        if (_designListContent != null)
        {
            RebuildAnchoredList(_designListContent, _designFiles, _selectedDesignIndex, _designListPage, "Design", index =>
            {
                _selectedDesignIndex = index;
                RefreshListButtons();
                UpdateUiState();
            }, path => Path.GetFileNameWithoutExtension(path));
        }

        if (_decalListContent != null)
        {
            RebuildAnchoredList(_decalListContent, _decalFiles, _selectedDecalIndex, _decalListPage, "Decal", SelectDecal, Path.GetFileName);
        }

        RebuildSelectableNavigation();
    }

    private void RebuildAnchoredList(RectTransform content, List<string> files, int selectedIndex, int page, string listName, Action<int> onSelect, Func<string, string> labelSelector)
    {
        for (var i = content.childCount - 1; i >= 0; i--)
        {
            Destroy(content.GetChild(i).gameObject);
        }

        var rect = content.rect;
        var rowHeight = 30f;
        var spacing = 4f;
        var rows = Mathf.Max(1, Mathf.FloorToInt((rect.height - 34f) / (rowHeight + spacing)));
        if (files.Count <= rows)
        {
            rows = Mathf.Max(1, Mathf.FloorToInt(rect.height / (rowHeight + spacing)));
        }

        var maxPage = Mathf.Max(0, Mathf.CeilToInt(files.Count / (float)rows) - 1);
        page = Mathf.Clamp(page, 0, maxPage);
        var startIndex = page * rows;
        var endIndex = Mathf.Min(files.Count, startIndex + rows);

        if (files.Count == 0)
        {
            CreateAnchoredText(content, $"{listName}Empty", listName == "Decal" ? "No decals found" : "No saved designs", 13, FontStyle.Normal, TextAnchor.MiddleLeft, new Rect(8f, 8f, rect.width - 16f, 26f), new Color(0.76f, 0.78f, 0.82f, 1f));
        }
        else
        {
            for (var i = startIndex; i < endIndex; i++)
            {
                var index = i;
                var y = 6f + (i - startIndex) * (rowHeight + spacing);
                var label = labelSelector(files[i]);
                var button = CreateAnchoredButton(content, TruncateListLabel(label), new Rect(6f, y, rect.width - 12f, rowHeight), () => onSelect(index));
                button.name = $"{listName}Row{index}";
                if (i == selectedIndex)
                {
                    ApplySelectedListButtonStyle(button);
                }
            }
        }

        if (maxPage > 0)
        {
            var y = Mathf.Max(6f, rect.height - 32f);
            CreateAnchoredButton(content, "<", new Rect(6f, y, 42f, 26f), () =>
            {
                SetListPage(listName, Mathf.Max(0, page - 1));
                RefreshListButtons();
            });
            CreateAnchoredText(content, $"{listName}Page", $"{page + 1}/{maxPage + 1}", 12, FontStyle.Normal, TextAnchor.MiddleCenter, new Rect(54f, y, 64f, 26f), Color.white);
            CreateAnchoredButton(content, ">", new Rect(124f, y, 42f, 26f), () =>
            {
                SetListPage(listName, Mathf.Min(maxPage, page + 1));
                RefreshListButtons();
            });
        }

        DrawableSuitsDiagnostics.Info($"ListRowsBuilt name={listName}; fileCount={files.Count}; selected={selectedIndex}; page={page}; maxPage={maxPage}; rows={rows}; rect={rect}; childCount={content.childCount}");
    }

    private void SetListPage(string listName, int page)
    {
        if (string.Equals(listName, "Design", StringComparison.OrdinalIgnoreCase))
        {
            _designListPage = page;
            return;
        }

        _decalListPage = page;
    }
    private static string TruncateListLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return "(unnamed)";
        }

        return label.Length <= 28 ? label : label.Substring(0, 25) + "...";
    }

    private static void ApplySelectedListButtonStyle(Button button)
    {
        if (button == null)
        {
            return;
        }

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

    private void UpdateBrushIndicator()
    {
        if (_brushIndicator == null)
        {
            return;
        }

        if (IsWorldThirdPersonMode)
        {
            _brushIndicator.gameObject.SetActive(false);
            return;
        }

        var uv = Vector2.zero;
        var show = _isOpen
            && _canPaint
            && (_tool == EditorTool.Paint || _tool == EditorTool.Erase)
            && TryGetTexturePreviewUv(_cursor, out uv);
        _brushIndicator.gameObject.SetActive(show);
        if (!show)
        {
            return;
        }

        var rect = _previewViewportRect.rect;
        _brushIndicator.anchorMin = new Vector2(0f, 0f);
        _brushIndicator.anchorMax = new Vector2(0f, 0f);
        _brushIndicator.pivot = new Vector2(0.5f, 0.5f);
        _brushIndicator.anchoredPosition = new Vector2(
            Mathf.Lerp(rect.xMin, rect.xMax, uv.x),
            Mathf.Lerp(rect.yMin, rect.yMax, uv.y));

        var texture = _selectedSuitId >= 0 ? DrawableSuitsPlugin.Registry.GetEditableTexture(_selectedSuitId) : null;
        var textureWidth = texture != null ? texture.width : 1024;
        var textureHeight = texture != null ? texture.height : 1024;
        var width = Mathf.Clamp((_brushSize * 2f / Mathf.Max(1f, textureWidth)) * rect.width, 4f, 180f);
        var height = Mathf.Clamp((_brushSize * 2f / Mathf.Max(1f, textureHeight)) * rect.height, 4f, 180f);
        _brushIndicator.sizeDelta = new Vector2(width, height);
        if (_brushIndicatorImage != null)
        {
            _brushIndicatorImage.color = _tool == EditorTool.Erase
                ? new Color(0.82f, 0.92f, 1f, 0.34f)
                : new Color(_brushColor.r, _brushColor.g, _brushColor.b, 0.34f);
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
            if (gamepad.buttonNorth.wasPressedThisFrame)
            {
                CycleTool();
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

        if (IsWorldThirdPersonMode)
        {
            if (gamepad != null)
            {
                if (gamepad.leftShoulder.isPressed)
                {
                    _worldCameraYaw -= 90f * Time.unscaledDeltaTime;
                }
                if (gamepad.rightShoulder.isPressed)
                {
                    _worldCameraYaw += 90f * Time.unscaledDeltaTime;
                }

                var rightStick = gamepad.rightStick.ReadValue();
                if (rightStick.sqrMagnitude > 0.01f)
                {
                    _worldCameraYaw += rightStick.x * 140f * Time.unscaledDeltaTime;
                    _worldCameraPitch -= rightStick.y * 90f * Time.unscaledDeltaTime;
                }

                var zoomDelta = gamepad.leftTrigger.ReadValue() - gamepad.rightTrigger.ReadValue();
                if (Mathf.Abs(zoomDelta) > 0.35f && !DrawableSuitsInput.IsLeftMousePressed())
                {
                    _worldCameraDistance = Mathf.Clamp(_worldCameraDistance + zoomDelta * Time.unscaledDeltaTime * 2f, 1.5f, 8f);
                }
            }

            if (!IsCursorOverEditorPanel() && DrawableSuitsInput.IsRightMousePressed())
            {
                _worldCameraYaw += DrawableSuitsInput.MouseDeltaX() * 3f;
                _worldCameraPitch -= DrawableSuitsInput.MouseDeltaY() * 2f;
            }

            var worldScroll = DrawableSuitsInput.MouseScrollY();
            if (!IsCursorOverEditorPanel() && Mathf.Abs(worldScroll) > 0.01f)
            {
                if (DrawableSuitsInput.IsKeyPressed(Key.LeftCtrl) || DrawableSuitsInput.IsKeyPressed(Key.RightCtrl))
                {
                    _brushSize = Mathf.Clamp(_brushSize + worldScroll * 2f, 1f, 96f);
                    if (_brushSizeSlider != null)
                    {
                        _brushSizeSlider.SetValue(_brushSize, false);
                    }
                }
                else
                {
                    _worldCameraDistance = Mathf.Clamp(_worldCameraDistance - worldScroll * 0.25f, 1.5f, 8f);
                }
            }

            return;
        }

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

        if (IsWorldThirdPersonMode)
        {
            HandleWorldPaintingInput();
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

        var texture = DrawableSuitsPlugin.Registry.GetEditableTexture(_selectedSuitId);
        var uvAvailable = TryGetTexturePreviewUv(_cursor, out var uv);
        var overPreview = IsCursorOverPreviewViewport() && uvAvailable;
        LogPaintAttemptIfNeeded("paint input", overPreview, uvAvailable, uv, texture, !_strokeActive);

        if (!overPreview || texture == null)
        {
            _strokeActive = false;
            return;
        }

        if (_tool == EditorTool.Decal && _loadedDecal == null)
        {
            WarnMissingDecal("paint input");
            _tool = EditorTool.Paint;
            UpdateToolButtons();
            UpdateBrushIndicator();
            _strokeActive = false;
            return;
        }

        if (!_strokeActive)
        {
            SaveUndo();
            _redo.Clear();
            _strokeActive = true;
        }

        PaintAtCursor(texture, uv);
    }

    private void HandleWorldPaintingInput()
    {
        var mousePainting = !IsCursorOverEditorPanel() && DrawableSuitsInput.IsLeftMousePressed();
        var gamepadPainting = Gamepad.current?.rightTrigger.ReadValue() > 0.55f;
        var painting = mousePainting || gamepadPainting;
        if (!painting)
        {
            _strokeActive = false;
            return;
        }

        var texture = DrawableSuitsPlugin.Registry.GetEditableTexture(_selectedSuitId);
        var hitAvailable = TryGetWorldPaintHit(out var hit, true);
        var uv = hitAvailable ? hit.textureCoord : default;
        LogPaintAttemptIfNeeded("world paint input", hitAvailable, hitAvailable, uv, texture, !_strokeActive);
        if (!hitAvailable || texture == null)
        {
            if (!_statusMessage.StartsWith("Aim at your suit", StringComparison.OrdinalIgnoreCase))
            {
                SetStatus("Aim at your visible suit to paint.", false);
                UpdateUiState();
            }
            _strokeActive = false;
            return;
        }

        if (_statusMessage.StartsWith("Aim at your suit", StringComparison.OrdinalIgnoreCase))
        {
            SetStatus("Ready. Third-person paint mode is active.", false);
        }

        if (_tool == EditorTool.Decal && _loadedDecal == null)
        {
            WarnMissingDecal("world paint input");
            _tool = EditorTool.Paint;
            UpdateToolButtons();
            _strokeActive = false;
            return;
        }

        if (!_strokeActive)
        {
            SaveUndo();
            _redo.Clear();
            _strokeActive = true;
        }

        PaintAtCursor(texture, uv);
    }
    private void LogPaintAttemptIfNeeded(string reason, bool overPreview, bool uvAvailable, Vector2 uv, Texture2D texture, bool force)
    {
        var pixel = "none";
        if (uvAvailable && texture != null)
        {
            var px = Mathf.RoundToInt(uv.x * (texture.width - 1));
            var py = Mathf.RoundToInt(uv.y * (texture.height - 1));
            pixel = $"{px},{py}";
        }

        var key = $"{reason}|tool={_tool}|over={overPreview}|uv={uvAvailable}:{uv}|pixel={pixel}|brush={Mathf.RoundToInt(_brushSize)}|opacity={_brushOpacity:0.##}|decal={_loadedDecal != null}|source={_pointerSource}";
        if (!force && Time.unscaledTime - _lastPaintDiagnosticsTime < 0.75f && string.Equals(key, _lastPaintDiagnosticsKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastPaintDiagnosticsTime = Time.unscaledTime;
        _lastPaintDiagnosticsKey = key;
        var gamepad = Gamepad.current;
        var trigger = gamepad != null ? gamepad.rightTrigger.ReadValue().ToString("0.###") : "null";
        DrawableSuitsDiagnostics.Info($"PaintAttempt: {key}; cursor={_cursor}; texture={DescribeEditableTexture()}; mouseDown={DrawableSuitsInput.IsLeftMousePressed()}; trigger={trigger}");
    }

    private void LogPaintApplied(Texture2D texture, Vector2 uv)
    {
        var px = Mathf.RoundToInt(uv.x * (texture.width - 1));
        var py = Mathf.RoundToInt(uv.y * (texture.height - 1));
        var key = $"applied|tool={_tool}|pixel={px},{py}|brush={Mathf.RoundToInt(_brushSize)}|opacity={_brushOpacity:0.##}|decal={_loadedDecal != null}";
        if (Time.unscaledTime - _lastPaintDiagnosticsTime < 0.5f && string.Equals(key, _lastPaintDiagnosticsKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastPaintDiagnosticsTime = Time.unscaledTime;
        _lastPaintDiagnosticsKey = key;
        DrawableSuitsDiagnostics.Info($"PaintApplied: {key}; texture={texture.name} {texture.width}x{texture.height}; uv={uv}; pointerSource={_pointerSource}");
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

    private void PaintAtCursor(Texture2D texture, Vector2 uv)
    {
        if (texture == null)
        {
            RefreshEditorReadiness("paint preflight failed");
            UpdateUiState();
            return;
        }

        var changed = true;
        switch (_tool)
        {
            case EditorTool.Paint:
                PaintCircle(texture, uv, _brushColor, _brushSize, _brushOpacity);
                break;
            case EditorTool.Erase:
                EraseCircle(texture, uv, _brushSize, _brushOpacity);
                break;
            case EditorTool.Decal:
                changed = ApplyDecal(texture, uv);
                _strokeActive = false;
                break;
            default:
                changed = false;
                break;
        }

        if (!changed)
        {
            return;
        }

        texture.Apply(false, false);
        if (_previewMaterial != null)
        {
            _previewMaterial.mainTexture = texture;
        }
        DrawableSuitsPlugin.Registry.ApplyEditedTexture(_selectedSuitId, _designName, false);
        if (_usingTexturePreview)
        {
            UseTexturePreview("PaintAtCursor", false);
        }

        LogPaintApplied(texture, uv);
        UpdateBrushIndicator();
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

    private bool ApplyDecal(Texture2D target, Vector2 uv)
    {
        if (_loadedDecal == null)
        {
            WarnMissingDecal("ApplyDecal");
            return false;
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

        return true;
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
        _designFiles.Sort(StringComparer.OrdinalIgnoreCase);
        _decalFiles.Clear();
        foreach (var file in Directory.GetFiles(DrawableSuitsPaths.Decals))
        {
            if (TextureTools.IsImagePath(file))
            {
                _decalFiles.Add(file);
            }
        }

        _decalFiles.Sort(StringComparer.OrdinalIgnoreCase);
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

    private bool IsWorldThirdPersonMode => string.Equals(_previewMode, "WorldThirdPerson", StringComparison.OrdinalIgnoreCase);

    private void ToggleUvFallback()
    {
        _uvFallbackMode = !_uvFallbackMode;
        DrawableSuitsDiagnostics.Info($"UV fallback toggled. enabled={_uvFallbackMode}");
        TryRebuildPreviewForCurrentReadiness("ToggleUvFallback");
        RefreshEditorReadiness("after UV fallback toggle");
        UpdateUiState();
    }

    private void SetUvFallbackVisible(bool visible)
    {
        if (_previewViewportRect != null)
        {
            _previewViewportRect.gameObject.SetActive(visible);
        }

        if (_brushIndicator != null && !visible)
        {
            _brushIndicator.gameObject.SetActive(false);
        }
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

        if (_uvFallbackMode)
        {
            DestroyPreview();
            UseTexturePreview(context, true);
            SetStatus("Ready. UV fallback preview is active.", false);
            return;
        }

        if (SetupWorldThirdPersonPreview(context))
        {
            return;
        }

        _uvFallbackMode = true;
        DestroyPreview();
        UseTexturePreview(context, true);
        SetStatus("Third-person setup failed; using UV fallback preview.", true);
    }

    private bool SetupWorldThirdPersonPreview(string context)
    {
        DestroyPreview();
        var texture = _selectedSuitId >= 0 ? DrawableSuitsPlugin.Registry.GetEditableTexture(_selectedSuitId) : null;
        var player = StartOfRound.Instance?.localPlayerController;
        var source = player?.thisPlayerModel;
        if (texture == null || player == null || source == null)
        {
            DrawableSuitsDiagnostics.Warn($"WorldThirdPerson setup skipped [{context}]. texture={DescribeEditableTexture()}; player={DrawableSuitsPlugin.DescribeUnityObject(player)}; source={DrawableSuitsPlugin.DescribeUnityObject(source)}");
            return false;
        }

        try
        {
            _usingTexturePreview = false;
            _previewMode = "WorldThirdPerson";
            _worldCameraDistance = Mathf.Clamp(DrawableSuitsPlugin.ModConfig.ThirdPersonCameraDistance.Value, 1.5f, 8f);
            _worldCameraYaw = player.transform.eulerAngles.y;
            _worldCameraPitch = 12f;
            _worldPaintLayer = SelectWorldPaintLayer();
            SetUvFallbackVisible(false);
            CaptureAndForcePlayerRenderers(player);

            _worldEditorCameraObject = new GameObject("DrawableSuitsThirdPersonCamera");
            _worldEditorCameraObject.hideFlags = HideFlags.HideAndDontSave;
            _worldEditorCamera = _worldEditorCameraObject.AddComponent<Camera>();
            var mainCamera = Camera.main;
            _worldEditorCamera.depth = (mainCamera != null ? mainCamera.depth : 0f) + 50f;
            _worldEditorCamera.clearFlags = CameraClearFlags.Skybox;
            _worldEditorCamera.backgroundColor = mainCamera != null ? mainCamera.backgroundColor : Color.black;
            _worldEditorCamera.fieldOfView = 62f;
            _worldEditorCamera.nearClipPlane = 0.03f;
            _worldEditorCamera.farClipPlane = 150f;
            _worldEditorCamera.cullingMask = (mainCamera != null ? mainCamera.cullingMask : ~0) | (1 << source.gameObject.layer);
            _worldEditorCamera.enabled = true;

            _worldPaintProxyObject = new GameObject("DrawableSuitsWorldPaintProxy");
            _worldPaintProxyObject.hideFlags = HideFlags.HideAndDontSave;
            _worldPaintProxyObject.transform.SetParent(source.transform, false);
            _worldPaintProxyObject.layer = _worldPaintLayer;
            _worldPaintMesh = new Mesh { name = "DrawableSuitsWorldPaintMesh" };
            _worldPaintCollider = _worldPaintProxyObject.AddComponent<MeshCollider>();
            _worldPaintCollider.convex = false;

            _worldBrushMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _worldBrushMarker.name = "DrawableSuitsWorldBrushMarker";
            _worldBrushMarker.hideFlags = HideFlags.HideAndDontSave;
            var markerCollider = _worldBrushMarker.GetComponent<Collider>();
            if (markerCollider != null)
            {
                Destroy(markerCollider);
            }
            var shader = Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
            _worldBrushMarkerMaterial = shader != null ? new Material(shader) { name = "DrawableSuitsWorldBrushMarkerMaterial" } : null;
            if (_worldBrushMarkerMaterial != null)
            {
                _worldBrushMarkerMaterial.color = new Color(_brushColor.r, _brushColor.g, _brushColor.b, 0.85f);
                _worldBrushMarker.GetComponent<Renderer>().sharedMaterial = _worldBrushMarkerMaterial;
            }
            _worldBrushMarker.SetActive(false);

            var proxyReady = UpdateWorldPaintProxy(true);
            UpdateWorldEditorCamera(true);
            _worldPreviewReady = proxyReady && _worldEditorCamera != null && _worldPaintCollider != null;
            _hasPreviewCollider = _worldPaintCollider != null;
            _canPaint = texture != null && _worldPreviewReady;
            SetStatus(_canPaint ? "Ready. Third-person paint mode is active." : "Third-person editor opened, but paint proxy is not ready.", !_canPaint);
            DrawableSuitsDiagnostics.Info($"WorldThirdPerson setup complete. context={context}; ready={_worldPreviewReady}; player={player.name}; renderer={source.name}; rendererEnabled={source.enabled}; layer={_worldPaintLayer}; camera={DrawableSuitsPlugin.DescribeUnityObject(_worldEditorCamera)}; cameraMask={_worldEditorCamera.cullingMask}; proxyCollider={_worldPaintCollider != null}; editable={DescribeEditableTexture()}");
            return _worldPreviewReady;
        }
        catch (Exception ex)
        {
            DrawableSuitsDiagnostics.Exception($"WorldThirdPerson setup failed. context={context}", ex);
            DestroyWorldThirdPersonPreview(true);
            return false;
        }
    }

    private static int SelectWorldPaintLayer()
    {
        var mainCamera = Camera.main;
        var mainMask = mainCamera != null ? mainCamera.cullingMask : -1;
        for (var layer = 30; layer >= 0; layer--)
        {
            if ((mainMask & (1 << layer)) == 0)
            {
                DrawableSuitsDiagnostics.Info($"Selected world paint proxy layer {layer}; mainCamera={mainCamera?.name ?? "null"}; mainMask={mainMask}");
                return layer;
            }
        }

        DrawableSuitsDiagnostics.Warn($"No layer outside Camera.main culling mask was available for paint proxy; using layer 2. mainCamera={mainCamera?.name ?? "null"}; mainMask={mainMask}");
        return 2;
    }

    private void CaptureAndForcePlayerRenderers(PlayerControllerB player)
    {
        RestorePlayerRenderers();
        if (player == null)
        {
            return;
        }

        CaptureRendererState(player.thisPlayerModel);
        CaptureRendererState(player.thisPlayerModelLOD1);
        CaptureRendererState(player.thisPlayerModelLOD2);

        if (player.thisPlayerModel != null)
        {
            player.thisPlayerModel.enabled = true;
        }
        if (player.thisPlayerModelLOD1 != null)
        {
            player.thisPlayerModelLOD1.enabled = false;
        }
        if (player.thisPlayerModelLOD2 != null)
        {
            player.thisPlayerModelLOD2.enabled = false;
        }

        DrawableSuitsDiagnostics.Info($"Forced local player renderer visibility for third-person editor. main={DescribeRendererState(player.thisPlayerModel)}; lod1={DescribeRendererState(player.thisPlayerModelLOD1)}; lod2={DescribeRendererState(player.thisPlayerModelLOD2)}");
    }

    private void CaptureRendererState(Renderer renderer)
    {
        if (renderer == null)
        {
            return;
        }

        for (var i = 0; i < _rendererRestoreStates.Count; i++)
        {
            if (_rendererRestoreStates[i].Renderer == renderer)
            {
                return;
            }
        }

        _rendererRestoreStates.Add(new RendererRestoreState { Renderer = renderer, Enabled = renderer.enabled });
    }

    private void RestorePlayerRenderers()
    {
        for (var i = 0; i < _rendererRestoreStates.Count; i++)
        {
            var state = _rendererRestoreStates[i];
            if (state.Renderer != null)
            {
                state.Renderer.enabled = state.Enabled;
            }
        }

        if (_rendererRestoreStates.Count > 0)
        {
            DrawableSuitsDiagnostics.Info($"Restored player renderer states. count={_rendererRestoreStates.Count}");
        }
        _rendererRestoreStates.Clear();
    }

    private static string DescribeRendererState(Renderer renderer)
    {
        return renderer != null ? $"{renderer.name}:enabled={renderer.enabled}:layer={renderer.gameObject.layer}" : "null";
    }

    private bool UpdateWorldPaintProxy(bool forceLog)
    {
        var source = StartOfRound.Instance?.localPlayerController?.thisPlayerModel;
        if (source == null || _worldPaintCollider == null || _worldPaintMesh == null || _worldPaintProxyObject == null)
        {
            return false;
        }

        try
        {
            _worldPaintMesh.Clear();
            source.BakeMesh(_worldPaintMesh, true);
            if (_worldPaintMesh.vertexCount == 0)
            {
                if (forceLog)
                {
                    DrawableSuitsDiagnostics.Warn("World paint proxy BakeMesh produced zero vertices.");
                }
                return false;
            }

            _worldPaintMesh.RecalculateBounds();
            if (_worldPaintProxyObject.transform.parent != source.transform)
            {
                _worldPaintProxyObject.transform.SetParent(source.transform, false);
            }
            _worldPaintProxyObject.transform.localPosition = Vector3.zero;
            _worldPaintProxyObject.transform.localRotation = Quaternion.identity;
            _worldPaintProxyObject.transform.localScale = Vector3.one;
            _worldPaintProxyObject.layer = _worldPaintLayer;
            _worldPaintCollider.sharedMesh = null;
            _worldPaintCollider.sharedMesh = _worldPaintMesh;
            if (forceLog)
            {
                DrawableSuitsDiagnostics.Info($"WorldPaintProxy updated. renderer={source.name}; vertices={_worldPaintMesh.vertexCount}; bounds={_worldPaintMesh.bounds}; proxyPos={_worldPaintProxyObject.transform.position}; proxyRot={_worldPaintProxyObject.transform.rotation.eulerAngles}; proxyScale={_worldPaintProxyObject.transform.localScale}; layer={_worldPaintLayer}; collider={_worldPaintCollider != null}");
            }
            return true;
        }
        catch (Exception ex)
        {
            DrawableSuitsDiagnostics.Exception("World paint proxy update failed", ex);
            return false;
        }
    }

    private void UpdateWorldEditorCamera(bool forceLog)
    {
        var player = StartOfRound.Instance?.localPlayerController;
        if (player == null || _worldEditorCamera == null)
        {
            return;
        }

        var target = player.transform.position + Vector3.up * 1.05f;
        _worldCameraPitch = Mathf.Clamp(_worldCameraPitch, -20f, 55f);
        _worldCameraDistance = Mathf.Clamp(_worldCameraDistance, 1.5f, 8f);
        var rotation = Quaternion.Euler(_worldCameraPitch, _worldCameraYaw, 0f);
        var position = target + rotation * new Vector3(0f, 0.15f, -_worldCameraDistance);
        _worldEditorCamera.transform.position = position;
        _worldEditorCamera.transform.rotation = Quaternion.LookRotation(target - position, Vector3.up);
        _worldEditorCamera.enabled = true;
        if (forceLog)
        {
            DrawableSuitsDiagnostics.Info($"WorldEditorCamera updated. pos={position}; target={target}; yaw={_worldCameraYaw:0.##}; pitch={_worldCameraPitch:0.##}; distance={_worldCameraDistance:0.##}; depth={_worldEditorCamera.depth}; mask={_worldEditorCamera.cullingMask}");
        }
    }

    private bool TryGetWorldPaintHit(out RaycastHit hit, bool updateProxy)
    {
        hit = default;
        _lastWorldRaycastHit = false;
        if (!IsWorldThirdPersonMode || _worldEditorCamera == null || _worldPaintCollider == null)
        {
            return false;
        }

        if (IsCursorOverEditorPanel())
        {
            return false;
        }

        if (updateProxy)
        {
            UpdateWorldPaintProxy(false);
        }

        var ray = _worldEditorCamera.ScreenPointToRay(_cursor);
        if (!Physics.Raycast(ray, out hit, 25f, 1 << _worldPaintLayer, QueryTriggerInteraction.Ignore))
        {
            return false;
        }

        _lastWorldRaycastHit = true;
        _lastWorldPaintUv = hit.textureCoord;
        _lastWorldHitPoint = hit.point;
        _lastWorldHitNormal = hit.normal;
        return true;
    }

    private void UpdateWorldBrushMarker()
    {
        if (_worldBrushMarker == null)
        {
            return;
        }

        if (!IsWorldThirdPersonMode || !_canPaint || !TryGetWorldPaintHit(out var hit, false))
        {
            _worldBrushMarker.SetActive(false);
            return;
        }

        _worldBrushMarker.SetActive(true);
        _worldBrushMarker.transform.position = hit.point + hit.normal * 0.015f;
        _worldBrushMarker.transform.rotation = Quaternion.LookRotation(hit.normal, Vector3.up);
        var scale = Mathf.Clamp(_brushSize / 180f, 0.035f, 0.45f);
        _worldBrushMarker.transform.localScale = new Vector3(scale, scale, scale);
        if (_worldBrushMarkerMaterial != null)
        {
            _worldBrushMarkerMaterial.color = _tool == EditorTool.Erase
                ? new Color(0.65f, 0.85f, 1f, 0.85f)
                : new Color(_brushColor.r, _brushColor.g, _brushColor.b, 0.85f);
        }
    }

    private void DestroyWorldThirdPersonPreview(bool restoreRenderers)
    {
        if (_worldEditorCamera != null)
        {
            _worldEditorCamera.enabled = false;
        }
        if (_worldEditorCameraObject != null)
        {
            Destroy(_worldEditorCameraObject);
            _worldEditorCameraObject = null;
        }
        if (_worldPaintProxyObject != null)
        {
            Destroy(_worldPaintProxyObject);
            _worldPaintProxyObject = null;
        }
        if (_worldPaintMesh != null)
        {
            Destroy(_worldPaintMesh);
            _worldPaintMesh = null;
        }
        if (_worldBrushMarker != null)
        {
            Destroy(_worldBrushMarker);
            _worldBrushMarker = null;
        }
        if (_worldBrushMarkerMaterial != null)
        {
            Destroy(_worldBrushMarkerMaterial);
            _worldBrushMarkerMaterial = null;
        }

        _worldEditorCamera = null;
        _worldPaintCollider = null;
        _worldPreviewReady = false;
        _lastWorldRaycastHit = false;
        if (restoreRenderers)
        {
            RestorePlayerRenderers();
        }
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

        _hasPreviewCollider = _previewCollider != null || _worldPaintCollider != null;
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
        _previewMode = texture != null ? "TextureFallback" : "TextureFallbackNoEditableTexture";
        _canPaint = texture != null;
        SetUvFallbackVisible(true);

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
        DestroyWorldThirdPersonPreview(true);

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
