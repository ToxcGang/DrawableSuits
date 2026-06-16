using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using GameNetcodeStuff;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using DiagnosticsProcess = System.Diagnostics.Process;
using DiagnosticsProcessStartInfo = System.Diagnostics.ProcessStartInfo;

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
        FillBucket,
        Decal,
        Eyedropper,
        Text,
        Sticker
    }

    private enum CursorVisualMode
    {
        Dot,
        BrushRing,
        BrushSquare,
        BrushPixel
    }

    private enum BrushShape
    {
        Circle,
        Square,
        Pixel,
        SprayPaint,
        SoftAirbrush,
        NoiseScatter
    }

    private enum StickerShape
    {
        Circle,
        Square,
        Triangle,
        Diamond,
        Star,
        Heart,
        Arrow,
        LightningBolt,
        Plus,
        Ring,
        Crescent,
        Shield
    }

    private enum PlacementEditTarget
    {
        None,
        Decal,
        Sticker
    }

    private enum PlacementFilter
    {
        None,
        Grayscale,
        Invert,
        Sepia,
        Brightness,
        Contrast,
        Saturation,
        HueShift
    }

    private enum ToolIconKind
    {
        Paint,
        Erase,
        Fill,
        Decal,
        Text,
        Eyedropper,
        Mirror,
        Sticker
    }

    private const int EditorCanvasSortingOrder = 32760;
    private const int MaxRecentColors = 12;
    private const float DotCursorRootSize = 17f;
    private const float DotCursorBackSize = 15f;
    private const float DotCursorFrontSize = 9f;
    private const float WorldPlacementPreviewMinInterval = 0.05f;
    private const float WorldPlacementPreviewIdleDelay = 0.15f;
    private const float WorldPlacementPreviewMoveThresholdPixels = 2f;
    private const int PlacementEditPreviewMaxTextureSize = 160;
    private const float SliderHandleVisualSize = 14f;
    private const float DecalImportPickerTimeoutSeconds = 300f;
    private const float UvPanelMinZoom = 1f;
    private const float UvPanelMaxZoom = 8f;
    private const float UvPanelWheelZoomFactor = 1.18f;
    private const float UvPanelDpadZoomFactorPerSecond = 1.9f;
    private const float UvPanelGamepadPanDeadzone = 0.2f;
    private const float UvPanelGamepadPanSpeed = 1.8f;
    private const float PlacementRotationShortcutStepDegrees = 5f;

    private static readonly Color TerminalPanelColor = new(0.018f, 0.022f, 0.024f, 0.88f);
    private static readonly Color TerminalDialogColor = new(0.022f, 0.024f, 0.026f, 0.98f);
    private static readonly Color TerminalCardColor = new(0.02f, 0.022f, 0.026f, 0.88f);
    private static readonly Color TerminalInputColor = new(0.09f, 0.012f, 0.012f, 0.9f);
    private static readonly Color TerminalButtonColor = new(0.105f, 0.012f, 0.012f, 0.94f);
    private static readonly Color TerminalButtonPressedColor = new(0.45f, 0.035f, 0.025f, 1f);
    private static readonly Color TerminalAccentColor = new(0.72f, 0.055f, 0.035f, 1f);
    private static readonly Color TerminalAccentHotColor = new(1f, 0.22f, 0.12f, 1f);
    private static readonly Color TerminalTextColor = new(0.94f, 0.88f, 0.78f, 1f);
    private static readonly Color TerminalMutedTextColor = new(0.72f, 0.66f, 0.58f, 1f);
    private static readonly Color TerminalStatusColor = new(1f, 0.42f, 0.18f, 1f);
    private static readonly Color TerminalDiagnosticsColor = new(0.86f, 0.75f, 0.64f, 1f);
    private static readonly Color TerminalSliderTrackColor = new(0.13f, 0.015f, 0.015f, 1f);
    private static readonly Color TerminalSliderFillColor = new(0.82f, 0.06f, 0.035f, 1f);
    private static readonly Color TerminalOutlineColor = new(0.42f, 0.025f, 0.02f, 0.9f);
    private static readonly Dictionary<ToolIconKind, Texture2D> ToolIconTextureCache = new();
    private static Texture2D SliderHandleDotTexture;
    private static Sprite SliderHandleDotSprite;
    private static readonly Vector2[] StickerTriangleVertices = { new(0f, 0.9f), new(-0.9f, -0.78f), new(0.9f, -0.78f) };
    private static readonly Vector2[] StickerArrowVertices = { new(-0.9f, -0.28f), new(0.12f, -0.28f), new(0.12f, -0.62f), new(0.9f, 0f), new(0.12f, 0.62f), new(0.12f, 0.28f), new(-0.9f, 0.28f) };
    private static readonly Vector2[] StickerLightningVertices = { new(-0.25f, 0.92f), new(0.55f, 0.92f), new(0.1f, 0.14f), new(0.64f, 0.14f), new(-0.32f, -0.96f), new(-0.02f, -0.24f), new(-0.58f, -0.24f) };
    private static readonly Vector2[] StickerShieldVertices = { new(-0.72f, 0.7f), new(0.72f, 0.7f), new(0.62f, -0.18f), new(0f, -0.92f), new(-0.62f, -0.18f) };
    private static readonly PlacementFilter[] PlacementFilterRows =
    {
        PlacementFilter.Grayscale,
        PlacementFilter.Sepia,
        PlacementFilter.Invert,
        PlacementFilter.Brightness,
        PlacementFilter.Contrast,
        PlacementFilter.Saturation,
        PlacementFilter.HueShift
    };

    private readonly Stack<UndoHistoryEntry> _undo = new();
    private readonly Stack<UndoHistoryEntry> _redo = new();
    private readonly List<string> _designFiles = new();
    private readonly List<string> _decalFiles = new();
    private readonly List<string> _recentColors = new(MaxRecentColors);
    private readonly Dictionary<StickerShape, Texture2D> _stickerStampTextures = new();
    private readonly PlacementEditState _decalPlacementEdit = new();
    private readonly PlacementEditState _stickerPlacementEdit = new();

    private bool _isOpen;
    private Vector2 _cursor;
    private int _selectedSuitId = -1;
    private int _selectedDesignIndex = -1;
    private int _selectedDecalIndex = -1;
    private string _designName = "MyDrawableSuit";
    private EditorTool _tool = EditorTool.Paint;
    private EditorTool _previousToolBeforeEyedropper = EditorTool.Paint;
    private BrushShape _brushShape = BrushShape.Circle;
    private Color _brushColor = Color.red;
    private float _brushSize = 16f;
    private float _brushOpacity = 1f;
    private float _fillTolerance = 0.08f;
    private float _decalSize = 128f;
    private float _decalRotation;
    private string _textStampValue = "TEXT";
    private float _textSize = 96f;
    private float _textRotation;
    private StickerShape _stickerShape = StickerShape.Star;
    private float _stickerSize = 128f;
    private float _stickerRotation;
    private bool _mirrorEnabled;
    private bool _strokeActive;
    private int _brushStrokeSeed = 1;
    private int _brushStrokeSequence;
    private bool _decalStampArmed = true;
    private bool _suppressPaintInputUntilRelease;
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
    private float _lastMirrorDiagnosticsTime;
    private string _lastMirrorDiagnosticsKey = string.Empty;
    private float _lastEyedropperDiagnosticsTime;
    private string _lastEyedropperDiagnosticsKey = string.Empty;
    private float _lastFillBucketDiagnosticsTime;
    private string _lastFillBucketDiagnosticsKey = string.Empty;

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
    private float _uvPanelZoom = 1f;
    private Vector2 _uvPanelCenter = new(0.5f, 0.5f);
    private int _uvPanelTextureWidth;
    private int _uvPanelTextureHeight;
    private string _lastUvPanelViewLogKey = string.Empty;
    private float _lastUvPanelViewLogTime;
    private GameObject _worldEditorCameraObject;
    private Camera _worldEditorCamera;
    private GameObject _worldPaintProxyObject;
    private Mesh _worldPaintMesh;
    private MeshCollider _worldPaintCollider;
    private MeshFilter _worldAvatarMeshFilter;
    private MeshRenderer _worldAvatarRenderer;
    private SkinnedMeshRenderer _worldSourceRenderer;
    private string _worldSourceRendererSummary = "none";
    private GameObject _worldBrushMarker;
    private Material _worldBrushMarkerMaterial;
    private Material _worldHiddenSubmeshMaterial;
    private Material _worldDecalPreviewMaterial;
    private int _worldPaintLayer = 30;
    private float _worldCameraYaw;
    private float _worldCameraPitch = 12f;
    private float _worldCameraDistance = 3.4f;
    private bool _worldPreviewReady;
    private string _lastWorldProxyMaterialLogKey = string.Empty;
    private float _lastWorldProxyMaterialLogTime;
    private Vector2 _lastWorldPaintUv;
    private Vector3 _lastWorldHitPoint;
    private Vector3 _lastWorldHitNormal;
    private Texture2D _decalPreviewTexture;
    private RawImage _uvDecalPreviewImage;
    private RectTransform _uvDecalPreviewRect;
    private RawImage _uvMirrorDecalPreviewImage;
    private RectTransform _uvMirrorDecalPreviewRect;
    private bool _worldDecalPreviewApplied;
    private bool _decalPreviewVisible;
    private bool _suppressDecalPreviewUntilRelease;
    private int _decalPreviewSerial;
    private EditorTool _placementPreviewTool = EditorTool.Decal;
    private string _lastDecalPreviewKey = string.Empty;
    private string _lastDecalPreviewLogKey = string.Empty;
    private float _lastDecalPreviewLogTime;
    private string _lastStickerPreviewLogKey = string.Empty;
    private float _lastStickerPreviewLogTime;
    private string _lastPlacementPreviewThrottleLogKey = string.Empty;
    private float _lastPlacementPreviewThrottleLogTime;
    private string _lastPlacementPreviewReuseLogKey = string.Empty;
    private float _lastPlacementPreviewReuseLogTime;
    private string _lastPlacementPreviewRebuildLogKey = string.Empty;
    private float _lastPlacementPreviewRebuildLogTime;
    private float _lastWorldPlacementPreviewRebuildTime;
    private Vector2 _lastPlacementPreviewCursor;
    private bool _lastPlacementPreviewCursorValid;
    private float _lastPlacementPreviewCursorMoveTime;
    private string _lastPlacementPreviewIdleTargetKey = string.Empty;
    private string _lastPlacementPreviewIdleLogKey = string.Empty;
    private float _lastPlacementPreviewIdleLogTime;
    private Color32[] _placementPreviewBasePixels;
    private Texture2D _placementPreviewBaseSource;
    private int _placementPreviewBaseSerial = -1;
    private string _lastDecalCoverageWarningKey = string.Empty;
    private float _lastDecalCoverageWarningTime;
    private string _lastBrushSurfaceDiagnosticsKey = string.Empty;
    private float _lastBrushSurfaceDiagnosticsTime;
    private string _lastBrushSurfaceWarningKey = string.Empty;
    private float _lastBrushSurfaceWarningTime;
    private string _lastTextPreviewLogKey = string.Empty;
    private float _lastTextPreviewLogTime;
    private string _lastTextProjectionFrameLogKey = string.Empty;
    private float _lastTextProjectionFrameLogTime;
    private int _designListPage;
    private int _decalListPage;
    private readonly List<RendererRestoreState> _rendererRestoreStates = new();
    private Texture2D _loadedDecal;
    private Texture2D _editedDecalStampTexture;
    private Texture2D _editedStickerStampTexture;
    private Texture2D _editedDecalPreviewStampTexture;
    private Texture2D _editedStickerPreviewStampTexture;
    private string _editedDecalStampKey = string.Empty;
    private string _editedStickerStampKey = string.Empty;
    private string _editedDecalPreviewStampKey = string.Empty;
    private string _editedStickerPreviewStampKey = string.Empty;
    private PlacementSourcePixelCache _placementSourcePixelCache;
    private TextStampRenderer _textStampRenderer;
    private Texture2D _textStampTexture;
    private string _textStampTextureKey = string.Empty;
    private string _lastWorldProxyMeshSummary = "none";
    private MirrorSurfaceMap _mirrorSurfaceMap;
    private string _mirrorSurfaceMapKey = string.Empty;

    private GameObject _editorCanvasObject;
    private GameObject _cursorCanvasObject;
    private RectTransform _canvasRect;
    private RectTransform _cursorCanvasRect;
    private GameObject _canvasCursorObject;
    private RectTransform _canvasCursorRect;
    private DrawableCanvasCursorGraphic _canvasCursorGraphic;
    private RectTransform _panelRect;
    private RectTransform _previewViewportRect;
    private RectTransform _brushIndicator;
    private Image _brushIndicatorImage;
    private RectTransform _cursorMarker;
    private RectTransform _cursorBackRect;
    private RectTransform _cursorFrontRect;
    private Image _cursorBackImage;
    private Image _cursorImage;
    private Sprite _cursorDotSprite;
    private Sprite _cursorRingSprite;
    private Texture2D _cursorDotTexture;
    private Texture2D _cursorRingTexture;
    private Texture2D _nativeCursorTexture;
    private string _nativeCursorKey = string.Empty;
    private string _lastNativeCursorLogKey = string.Empty;
    private float _lastNativeCursorLogTime;
    private string _lastNativeCursorFailureKey = string.Empty;
    private float _lastNativeCursorFailureTime;
    private string _lastNativeCursorWarpKey = string.Empty;
    private float _lastNativeCursorWarpTime;
    private float _ignoreMouseInputUntilTime;
    private string _lastCanvasCursorLogKey = string.Empty;
    private float _lastCanvasCursorLogTime;
    private string _lastCanvasCursorHiddenKey = string.Empty;
    private float _lastCanvasCursorHiddenTime;
    private string _lastDynamicCursorLogKey = string.Empty;
    private float _lastDynamicCursorLogTime;
    private string _lastImmediateCursorSkipLog = string.Empty;
    private float _lastImmediateCursorSkipLogTime;
    private RectTransform _designListContent;
    private RectTransform _decalListContent;
    private Text _designEmptyLabel;
    private Text _decalEmptyLabel;
    private Text _undoHistoryEmptyLabel;
    private Text _undoHistorySelectionLabel;
    private Text _designPageLabel;
    private Text _decalPageLabel;
    private Button _designPrevPageButton;
    private Button _designNextPageButton;
    private Button _decalPrevPageButton;
    private Button _decalNextPageButton;
    private readonly List<AnchoredListRow> _designRows = new();
    private readonly List<AnchoredListRow> _decalRows = new();
    private readonly List<UndoHistoryRow> _undoHistoryRows = new();
    private long _nextUndoHistoryId = 1;
    private long _selectedUndoHistoryId;
    private RawImage _previewImage;
    private Text _suitLabel;
    private Text _statusLabel;
    private Text _activeToolLabel;
    private Text _brushSizeLabel;
    private Text _brushOpacityLabel;
    private Text _brushShapeLabel;
    private Text _recentColorsLabel;
    private Text _decalSizeLabel;
    private Text _decalRotationLabel;
    private Text _placementHeaderLabel;
    private InputField _designNameInput;
    private InputField _textStampInput;
    private DrawableSliderControl _brushSizeSlider;
    private DrawableSliderControl _brushOpacitySlider;
    private Text _fillToleranceLabel;
    private DrawableSliderControl _fillToleranceSlider;
    private DrawableColorPickerControl _colorPicker;
    private InputField _colorHexInput;
    private DrawableSliderControl _decalSizeSlider;
    private DrawableSliderControl _decalRotationSlider;
    private Image _colorSwatch;
    private Button _paintButton;
    private Button _eraseButton;
    private Button _fillButton;
    private Button _decalButton;
    private Button _eyedropperButton;
    private Button _textButton;
    private Button _stickerButton;
    private Button _mirrorButton;
    private Button _brushShapeButton;
    private GameObject _brushShapeMenuObject;
    private readonly List<Button> _brushShapeOptionButtons = new();
    private Button _stickerShapeButton;
    private GameObject _stickerShapeMenuObject;
    private readonly List<Button> _stickerShapeOptionButtons = new();
    private Text _selectedDecalLabel;
    private Button _decalsMenuButton;
    private Button _editDecalButton;
    private Text _selectedStickerShapeLabel;
    private Button _stickersMenuButton;
    private Button _editStickerButton;
    private GameObject _decalsPanelObject;
    private Text _decalsStatusLabel;
    private GameObject _stickersPanelObject;
    private Text _stickersStatusLabel;
    private readonly List<Button> _stickerPanelShapeButtons = new();
    private readonly List<Button> _recentColorButtons = new(MaxRecentColors);
    private readonly List<Image> _recentColorImages = new(MaxRecentColors);
    private Button _applyButton;
    private Button _saveButton;
    private Button _savedDesignsButton;
    private Button _loadSelectedDesignButton;
    private Button _deleteSelectedDesignButton;
    private Button _undoToSelectedButton;
    private Button _clearUndoHistoryButton;
    private Button _resetButton;
    private Button _exportCodeButton;
    private Button _importCodeButton;
    private Button _uvFallbackButton;
    private GameObject _designCodePanelObject;
    private InputField _designCodeInput;
    private Text _designCodeStatusLabel;
    private GameObject _savedDesignsPanelObject;
    private Text _savedDesignsStatusLabel;
    private Button _deleteSelectedDecalButton;
    private Button _addDecalButton;
    private GameObject _placementEditPanelObject;
    private PlacementEditTarget _activePlacementEditTarget;
    private RectTransform _placementEditPreviewFrameRect;
    private RectTransform _placementEditPreviewRect;
    private RawImage _placementEditPreviewBackingImage;
    private RawImage _placementEditPreviewImage;
    private Text _placementEditTitleLabel;
    private Text _placementEditSourceLabel;
    private Text _placementEditCropLeftLabel;
    private Text _placementEditCropRightLabel;
    private Text _placementEditCropBottomLabel;
    private Text _placementEditCropTopLabel;
    private Text _placementEditStretchXLabel;
    private Text _placementEditStretchYLabel;
    private Text _placementEditStatusLabel;
    private Button _placementEditFlipXButton;
    private Button _placementEditFlipYButton;
    private DrawableSliderControl _placementEditCropLeftSlider;
    private DrawableSliderControl _placementEditCropRightSlider;
    private DrawableSliderControl _placementEditCropBottomSlider;
    private DrawableSliderControl _placementEditCropTopSlider;
    private DrawableSliderControl _placementEditStretchXSlider;
    private DrawableSliderControl _placementEditStretchYSlider;
    private readonly Dictionary<PlacementFilter, Text> _placementEditFilterValueLabels = new();
    private readonly Dictionary<PlacementFilter, DrawableSliderControl> _placementEditFilterSliders = new();
    private Texture2D _placementEditCheckerTexture;
    private string _lastPlacementEditPreviewLogKey = string.Empty;
    private float _lastPlacementEditPreviewLogTime;
    private string _lastPlacementSourcePixelsLogKey = string.Empty;
    private float _lastPlacementSourcePixelsLogTime;
    private Color32[] _editedDecalPreviewPixelBuffer;
    private DiagnosticsProcess _pendingDecalImportProcess;
    private float _pendingDecalImportStartedAt;
    private int _pendingDecalImportId;
    private string _pendingDeleteDesignPath = string.Empty;
    private string _pendingDeleteDecalPath = string.Empty;
    private GameObject _editorEventSystemObject;
    private EventSystem _editorEventSystem;
    private InputSystemUIInputModule _editorInputModule;
    private InputActionAsset _editorUiActions;
    private bool _editorUiInputActive;
    private bool _virtualPointerDown;
    private GameObject _virtualPointerPressTarget;
    private DrawableSliderControl _virtualPointerSlider;
    private DrawableColorPickerControl _virtualPointerColorPicker;
    private Button _virtualPointerButton;
    private InputField _virtualPointerInput;
    private string _pointerSource = "Mouse";
    private Vector2 _lastMousePosition;
    private Vector2 _lastGamepadStick;
    private bool _mousePositionAvailable;
    private bool _usingGamepadPointer;
    private bool _gamepadClickArmed;
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

    private sealed class PlacementEditState
    {
        internal float CropLeft;
        internal float CropRight;
        internal float CropBottom;
        internal float CropTop;
        internal float StretchX = 1f;
        internal float StretchY = 1f;
        internal bool FlipX;
        internal bool FlipY;
        internal float GrayscaleAmount;
        internal float InvertAmount;
        internal float SepiaAmount;
        internal float BrightnessAmount;
        internal float ContrastAmount;
        internal float SaturationAmount;
        internal float HueShiftAmount;
        internal int Revision;

        internal bool IsDefault =>
            CropLeft <= 0.0001f
            && CropRight <= 0.0001f
            && CropBottom <= 0.0001f
            && CropTop <= 0.0001f
            && Mathf.Abs(StretchX - 1f) <= 0.0001f
            && Mathf.Abs(StretchY - 1f) <= 0.0001f
            && !FlipX
            && !FlipY
            && !HasActiveFilters;

        internal bool HasActiveFilters =>
            GrayscaleAmount > 0.0001f
            || InvertAmount > 0.0001f
            || SepiaAmount > 0.0001f
            || BrightnessAmount > 0.0001f
            || ContrastAmount > 0.0001f
            || SaturationAmount > 0.0001f
            || HueShiftAmount > 0.0001f;

        internal void Reset()
        {
            CropLeft = 0f;
            CropRight = 0f;
            CropBottom = 0f;
            CropTop = 0f;
            StretchX = 1f;
            StretchY = 1f;
            FlipX = false;
            FlipY = false;
            GrayscaleAmount = 0f;
            InvertAmount = 0f;
            SepiaAmount = 0f;
            BrightnessAmount = 0f;
            ContrastAmount = 0f;
            SaturationAmount = 0f;
            HueShiftAmount = 0f;
            Revision++;
        }

        internal Rect CropRect(float minSize)
        {
            var left = Mathf.Clamp01(CropLeft);
            var right = Mathf.Clamp01(CropRight);
            var bottom = Mathf.Clamp01(CropBottom);
            var top = Mathf.Clamp01(CropTop);
            if (left + right > 1f - minSize)
            {
                var scale = (1f - minSize) / Mathf.Max(0.0001f, left + right);
                left *= scale;
                right *= scale;
            }
            if (bottom + top > 1f - minSize)
            {
                var scale = (1f - minSize) / Mathf.Max(0.0001f, bottom + top);
                bottom *= scale;
                top *= scale;
            }

            return new Rect(left, bottom, Mathf.Max(minSize, 1f - left - right), Mathf.Max(minSize, 1f - bottom - top));
        }

        internal string Key()
        {
            return $"crop={CropLeft:0.####},{CropRight:0.####},{CropBottom:0.####},{CropTop:0.####}|stretch={StretchX:0.###},{StretchY:0.###}|flip={FlipX},{FlipY}|filters={GrayscaleAmount:0.###},{SepiaAmount:0.###},{InvertAmount:0.###},{BrightnessAmount:0.###},{ContrastAmount:0.###},{SaturationAmount:0.###},{HueShiftAmount:0.###}|rev={Revision}";
        }

        internal float GetFilterAmount(PlacementFilter filter)
        {
            return filter switch
            {
                PlacementFilter.Grayscale => GrayscaleAmount,
                PlacementFilter.Invert => InvertAmount,
                PlacementFilter.Sepia => SepiaAmount,
                PlacementFilter.Brightness => BrightnessAmount,
                PlacementFilter.Contrast => ContrastAmount,
                PlacementFilter.Saturation => SaturationAmount,
                PlacementFilter.HueShift => HueShiftAmount,
                _ => 0f
            };
        }

        internal void SetFilterAmount(PlacementFilter filter, float amount)
        {
            amount = Mathf.Clamp01(amount);
            switch (filter)
            {
                case PlacementFilter.Grayscale:
                    GrayscaleAmount = amount;
                    break;
                case PlacementFilter.Invert:
                    InvertAmount = amount;
                    break;
                case PlacementFilter.Sepia:
                    SepiaAmount = amount;
                    break;
                case PlacementFilter.Brightness:
                    BrightnessAmount = amount;
                    break;
                case PlacementFilter.Contrast:
                    ContrastAmount = amount;
                    break;
                case PlacementFilter.Saturation:
                    SaturationAmount = amount;
                    break;
                case PlacementFilter.HueShift:
                    HueShiftAmount = amount;
                    break;
            }
        }
    }

    private sealed class PlacementSourcePixelCache
    {
        internal PlacementEditTarget Target = PlacementEditTarget.None;
        internal Texture2D Source;
        internal string SourceName = string.Empty;
        internal int Width;
        internal int Height;
        internal Color32[] Pixels;
    }

    private sealed class PlacementEditSnapshot
    {
        internal float CropLeft;
        internal float CropRight;
        internal float CropBottom;
        internal float CropTop;
        internal float StretchX = 1f;
        internal float StretchY = 1f;
        internal bool FlipX;
        internal bool FlipY;
        internal float GrayscaleAmount;
        internal float InvertAmount;
        internal float SepiaAmount;
        internal float BrightnessAmount;
        internal float ContrastAmount;
        internal float SaturationAmount;
        internal float HueShiftAmount;
        internal int Revision;
        internal string Key = string.Empty;

        internal bool HasActiveFilters =>
            GrayscaleAmount > 0.0001f
            || InvertAmount > 0.0001f
            || SepiaAmount > 0.0001f
            || BrightnessAmount > 0.0001f
            || ContrastAmount > 0.0001f
            || SaturationAmount > 0.0001f
            || HueShiftAmount > 0.0001f;

        internal static PlacementEditSnapshot From(PlacementEditState state)
        {
            return new PlacementEditSnapshot
            {
                CropLeft = state?.CropLeft ?? 0f,
                CropRight = state?.CropRight ?? 0f,
                CropBottom = state?.CropBottom ?? 0f,
                CropTop = state?.CropTop ?? 0f,
                StretchX = state?.StretchX ?? 1f,
                StretchY = state?.StretchY ?? 1f,
                FlipX = state?.FlipX ?? false,
                FlipY = state?.FlipY ?? false,
                GrayscaleAmount = state?.GrayscaleAmount ?? 0f,
                InvertAmount = state?.InvertAmount ?? 0f,
                SepiaAmount = state?.SepiaAmount ?? 0f,
                BrightnessAmount = state?.BrightnessAmount ?? 0f,
                ContrastAmount = state?.ContrastAmount ?? 0f,
                SaturationAmount = state?.SaturationAmount ?? 0f,
                HueShiftAmount = state?.HueShiftAmount ?? 0f,
                Revision = state?.Revision ?? 0,
                Key = state?.Key() ?? string.Empty
            };
        }
    }

    private sealed class RendererRestoreState
    {
        internal Renderer Renderer;
        internal bool Enabled;
        internal int Layer;
        internal bool UpdateWhenOffscreen;
        internal bool HasUpdateWhenOffscreen;
    }

    private sealed class AnchoredListRow
    {
        internal Button Button;
        internal Text Label;
        internal Image Image;
        internal int Index = -1;
    }

    private sealed class UndoHistoryEntry
    {
        internal long Id;
        internal Color32[] Pixels;
        internal string Label;
    }

    private sealed class UndoHistoryRow
    {
        internal GameObject GameObject;
        internal Button Button;
        internal Text Label;
        internal Image Image;
        internal int Index = -1;
        internal long EntryId;
    }

    private struct MirrorPaintTarget
    {
        internal bool Enabled;
        internal bool Available;
        internal Vector2 Uv;
        internal Vector3 PrimaryLocalPoint;
        internal Vector3 ReflectedLocalPoint;
        internal Vector3 MirroredLocalPoint;
        internal Vector3 MirroredLocalNormal;
        internal float Distance;
        internal int TriangleIndex;
        internal string Mode;
        internal string Reason;
    }

    private struct MirrorLookupResult
    {
        internal Vector2 Uv;
        internal Vector3 LocalPoint;
        internal Vector3 ReflectedLocalPoint;
        internal Vector3 LocalNormal;
        internal float Distance;
        internal int TriangleIndex;
        internal string Reason;
    }

    private struct TextProjectionFrame
    {
        internal Vector3 Center;
        internal Vector3 Normal;
        internal Vector3 Right;
        internal Vector3 Up;
        internal float WorldWidth;
        internal float WorldHeight;
    }

    private struct TextSurfaceStampStats
    {
        internal int AlphaPixels;
        internal int WrittenPixels;
        internal int SkippedPixels;
        internal int ProjectionSamples;
        internal int AcceptedSamples;
        internal int SurfaceHits;
        internal int RasterizedCells;
        internal int SeamSkippedCells;
        internal int OffSuitSamples;
        internal int RandomSkippedSamples;
        internal float WorldWidth;
        internal float WorldHeight;
        internal bool Mirrored;
    }

    private struct FillBucketStats
    {
        internal int CheckedPixels;
        internal int MatchedPixels;
        internal int WrittenPixels;
        internal int TouchedSkippedPixels;
        internal Vector2Int SeedPixel;
        internal Color32 SeedColor;
        internal bool Mirrored;
    }

    private struct BrushShapeStats
    {
        internal bool Available;
        internal int CheckedSamples;
        internal int AcceptedSamples;
        internal int RandomSkippedSamples;
        internal int WrittenPixels;
        internal bool Mirrored;
    }

    private struct SurfaceStampSample
    {
        internal Color Color;
        internal float Alpha;
    }

    private struct SurfaceStampGridSample
    {
        internal bool Valid;
        internal Vector2 StampUv;
        internal Vector2 SurfaceUv;
        internal Vector2 Pixel;
        internal float Alpha;
    }

    private sealed class MirrorSurfaceMap
    {
        private readonly MirrorTriangle[] _triangles;
        private readonly Bounds _bounds;
        private readonly float _mirrorPlaneX;
        private readonly float _maxMirrorDistance;

        internal string Key { get; }
        internal int TriangleCount => _triangles.Length;
        internal Bounds Bounds => _bounds;
        internal float MirrorPlaneX => _mirrorPlaneX;

        private MirrorSurfaceMap(string key, MirrorTriangle[] triangles, Bounds bounds)
        {
            Key = key;
            _triangles = triangles;
            _bounds = bounds;
            _mirrorPlaneX = bounds.center.x;
            _maxMirrorDistance = Mathf.Max(0.35f, bounds.size.magnitude * 0.28f);
        }

        internal static MirrorSurfaceMap Build(Mesh mesh, string key)
        {
            if (mesh == null || mesh.vertexCount == 0)
            {
                return null;
            }

            var vertices = mesh.vertices;
            var uvs = mesh.uv;
            if (uvs == null || uvs.Length < vertices.Length)
            {
                return null;
            }

            var triangles = new List<MirrorTriangle>();
            var triangleOrdinal = 0;
            var subMeshCount = Mathf.Max(1, mesh.subMeshCount);
            for (var subMesh = 0; subMesh < subMeshCount; subMesh++)
            {
                var indices = mesh.GetTriangles(subMesh);
                for (var i = 0; i + 2 < indices.Length; i += 3)
                {
                    var i0 = indices[i];
                    var i1 = indices[i + 1];
                    var i2 = indices[i + 2];
                    if (i0 < 0 || i1 < 0 || i2 < 0 || i0 >= vertices.Length || i1 >= vertices.Length || i2 >= vertices.Length)
                    {
                        continue;
                    }

                    var a = vertices[i0];
                    var b = vertices[i1];
                    var c = vertices[i2];
                    if (Vector3.Cross(b - a, c - a).sqrMagnitude < 0.0000001f)
                    {
                        continue;
                    }

                    var uv0 = uvs[i0];
                    var uv1 = uvs[i1];
                    var uv2 = uvs[i2];
                    if (Mathf.Abs(Cross2D(uv1 - uv0, uv2 - uv0)) < 0.0000001f)
                    {
                        continue;
                    }

                    triangles.Add(new MirrorTriangle
                    {
                        A = a,
                        B = b,
                        C = c,
                        UvA = uv0,
                        UvB = uv1,
                        UvC = uv2,
                        Centroid = (a + b + c) / 3f,
                        Normal = Vector3.Cross(b - a, c - a).normalized,
                        SubMesh = subMesh,
                        TriangleIndex = triangleOrdinal
                    });
                    triangleOrdinal++;
                }
            }

            return triangles.Count > 0 ? new MirrorSurfaceMap(key, triangles.ToArray(), mesh.bounds) : null;
        }

        internal bool TryMirrorFromLocalPoint(Vector3 primaryLocalPoint, out MirrorLookupResult result)
        {
            result = default;
            if (_triangles.Length == 0)
            {
                result.Reason = "surface map empty";
                return false;
            }

            var reflected = primaryLocalPoint;
            reflected.x = (_mirrorPlaneX * 2f) - primaryLocalPoint.x;
            var sourceSide = primaryLocalPoint.x - _mirrorPlaneX;
            var targetSign = sourceSide > 0.001f ? -1 : sourceSide < -0.001f ? 1 : 0;
            if (TryFindClosestTriangle(reflected, targetSign, true, out result))
            {
                result.ReflectedLocalPoint = reflected;
                return true;
            }

            result.ReflectedLocalPoint = reflected;
            if (string.IsNullOrWhiteSpace(result.Reason))
            {
                result.Reason = "no reliable opposite mirror target triangle";
            }
            return false;
        }

        internal bool TryLocalPointFromUv(Vector2 uv, out Vector3 localPoint, out int triangleIndex, out string reason)
        {
            localPoint = default;
            triangleIndex = -1;
            reason = string.Empty;
            var bestArea = float.MaxValue;
            var found = false;
            for (var i = 0; i < _triangles.Length; i++)
            {
                var triangle = _triangles[i];
                if (!TryBarycentric2D(uv, triangle.UvA, triangle.UvB, triangle.UvC, out var bary, 0.0015f))
                {
                    continue;
                }

                var area = Mathf.Abs(Cross2D(triangle.UvB - triangle.UvA, triangle.UvC - triangle.UvA));
                if (area >= bestArea)
                {
                    continue;
                }

                bestArea = area;
                localPoint = (triangle.A * bary.x) + (triangle.B * bary.y) + (triangle.C * bary.z);
                triangleIndex = triangle.TriangleIndex;
                found = true;
            }

            reason = found ? "uv mapped to source triangle" : "uv did not map to any mesh triangle";
            return found;
        }

        private bool TryFindClosestTriangle(Vector3 reflectedLocalPoint, int targetSign, bool enforceDistance, out MirrorLookupResult result)
        {
            result = default;
            var bestDistanceSqr = float.MaxValue;
            var bestTriangle = default(MirrorTriangle);
            var bestPoint = Vector3.zero;
            var found = false;
            for (var i = 0; i < _triangles.Length; i++)
            {
                var triangle = _triangles[i];
                if (targetSign != 0)
                {
                    var side = triangle.Centroid.x - _mirrorPlaneX;
                    if (side * targetSign < -0.01f)
                    {
                        continue;
                    }
                }

                var closest = ClosestPointOnTriangle(reflectedLocalPoint, triangle.A, triangle.B, triangle.C);
                var distanceSqr = (closest - reflectedLocalPoint).sqrMagnitude;
                if (distanceSqr >= bestDistanceSqr)
                {
                    continue;
                }

                bestDistanceSqr = distanceSqr;
                bestTriangle = triangle;
                bestPoint = closest;
                found = true;
            }

            if (!found)
            {
                result.Reason = targetSign == 0 ? "no triangle candidates" : "no opposite-side triangle candidates";
                return false;
            }

            var distance = Mathf.Sqrt(bestDistanceSqr);
            if (enforceDistance && distance > _maxMirrorDistance)
            {
                result.Distance = distance;
                result.TriangleIndex = bestTriangle.TriangleIndex;
                result.LocalPoint = bestPoint;
                result.Reason = $"nearest candidate too far {distance:0.###}>{_maxMirrorDistance:0.###}";
                return false;
            }

            if (!TryBarycentric3D(bestPoint, bestTriangle.A, bestTriangle.B, bestTriangle.C, out var bary))
            {
                result.Distance = distance;
                result.TriangleIndex = bestTriangle.TriangleIndex;
                result.LocalPoint = bestPoint;
                result.Reason = "mirror triangle barycentric failed";
                return false;
            }

            result.Uv = ClampUv((bestTriangle.UvA * bary.x) + (bestTriangle.UvB * bary.y) + (bestTriangle.UvC * bary.z));
            result.LocalPoint = bestPoint;
            result.LocalNormal = bestTriangle.Normal;
            result.Distance = distance;
            result.TriangleIndex = bestTriangle.TriangleIndex;
            result.Reason = "surface map";
            return true;
        }
    }

    private struct MirrorTriangle
    {
        internal Vector3 A;
        internal Vector3 B;
        internal Vector3 C;
        internal Vector2 UvA;
        internal Vector2 UvB;
        internal Vector2 UvC;
        internal Vector3 Centroid;
        internal Vector3 Normal;
        internal int SubMesh;
        internal int TriangleIndex;
    }

    private struct WorldCameraState
    {
        internal bool Valid;
        internal float Yaw;
        internal float Pitch;
        internal float Distance;
    }

    private sealed class DrawableToolIconGraphic : Graphic
    {
        private ToolIconKind _kind;

        internal void Configure(ToolIconKind kind, Color iconColor)
        {
            _kind = kind;
            color = iconColor;
            raycastTarget = false;
            SetVerticesDirty();
        }

        internal void SetIconColor(Color iconColor)
        {
            color = iconColor;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var rect = rectTransform.rect;
            if (rect.width <= 0.1f || rect.height <= 0.1f)
            {
                return;
            }

            switch (_kind)
            {
                case ToolIconKind.Paint:
                    AddLine(vh, rect, new Vector2(-0.34f, -0.3f), new Vector2(0.03f, 0.07f), 0.075f, color);
                    AddRotatedBox(vh, rect, new Vector2(0.12f, 0.16f), new Vector2(0.22f, 0.11f), 45f, color);
                    AddPolygon(vh, rect, color,
                        new Vector2(0.2f, 0.22f),
                        new Vector2(0.38f, 0.36f),
                        new Vector2(0.31f, 0.09f));
                    AddLine(vh, rect, new Vector2(-0.36f, -0.38f), new Vector2(-0.12f, -0.38f), 0.04f, color);
                    break;
                case ToolIconKind.Erase:
                    AddLine(vh, rect, new Vector2(-0.26f, -0.05f), new Vector2(0.18f, 0.28f), 0.18f, color);
                    AddLine(vh, rect, new Vector2(-0.32f, -0.24f), new Vector2(0.22f, -0.24f), 0.045f, color);
                    AddLine(vh, rect, new Vector2(0.05f, -0.02f), new Vector2(0.24f, 0.12f), 0.04f, color);
                    break;
                case ToolIconKind.Fill:
                    AddPolygon(vh, rect, color,
                        new Vector2(-0.3f, 0.18f),
                        new Vector2(0.11f, 0.31f),
                        new Vector2(0.3f, -0.05f),
                        new Vector2(-0.12f, -0.23f));
                    AddLine(vh, rect, new Vector2(-0.23f, 0.25f), new Vector2(0.02f, 0.34f), 0.04f, color);
                    AddLine(vh, rect, new Vector2(-0.02f, -0.18f), new Vector2(0.29f, -0.3f), 0.055f, color);
                    AddDrop(vh, rect, new Vector2(0.34f, -0.36f), 0.07f, color);
                    break;
                case ToolIconKind.Decal:
                    AddLine(vh, rect, new Vector2(-0.32f, -0.28f), new Vector2(0.32f, -0.28f), 0.045f, color);
                    AddLine(vh, rect, new Vector2(-0.32f, 0.28f), new Vector2(0.32f, 0.28f), 0.045f, color);
                    AddLine(vh, rect, new Vector2(-0.32f, -0.28f), new Vector2(-0.32f, 0.28f), 0.045f, color);
                    AddLine(vh, rect, new Vector2(0.32f, -0.28f), new Vector2(0.32f, 0.28f), 0.045f, color);
                    AddLine(vh, rect, new Vector2(-0.22f, -0.18f), new Vector2(-0.02f, 0.08f), 0.04f, color);
                    AddLine(vh, rect, new Vector2(-0.02f, 0.08f), new Vector2(0.24f, -0.18f), 0.04f, color);
                    AddBox(vh, rect, new Vector2(0.16f, 0.12f), new Vector2(0.08f, 0.08f), color);
                    break;
                case ToolIconKind.Text:
                    AddBox(vh, rect, new Vector2(0f, 0.28f), new Vector2(0.56f, 0.08f), color);
                    AddBox(vh, rect, new Vector2(0f, 0f), new Vector2(0.1f, 0.64f), color);
                    AddBox(vh, rect, new Vector2(0f, -0.32f), new Vector2(0.28f, 0.07f), color);
                    break;
                case ToolIconKind.Eyedropper:
                    AddFilledCircle(vh, rect, new Vector2(0.25f, 0.28f), 0.085f, color);
                    AddLine(vh, rect, new Vector2(0.18f, 0.2f), new Vector2(-0.18f, -0.16f), 0.08f, color);
                    AddLine(vh, rect, new Vector2(0.04f, 0.24f), new Vector2(0.29f, -0.01f), 0.045f, color);
                    AddPolygon(vh, rect, color,
                        new Vector2(-0.19f, -0.17f),
                        new Vector2(-0.33f, -0.34f),
                        new Vector2(-0.12f, -0.25f));
                    AddDrop(vh, rect, new Vector2(-0.36f, -0.39f), 0.045f, color);
                    break;
                case ToolIconKind.Mirror:
                    AddLine(vh, rect, new Vector2(0f, -0.34f), new Vector2(0f, 0.34f), 0.045f, color);
                    AddLine(vh, rect, new Vector2(-0.32f, 0.12f), new Vector2(-0.08f, 0.12f), 0.055f, color);
                    AddLine(vh, rect, new Vector2(-0.32f, 0.12f), new Vector2(-0.2f, 0.24f), 0.045f, color);
                    AddLine(vh, rect, new Vector2(-0.32f, 0.12f), new Vector2(-0.2f, 0f), 0.045f, color);
                    AddLine(vh, rect, new Vector2(0.08f, -0.12f), new Vector2(0.32f, -0.12f), 0.055f, color);
                    AddLine(vh, rect, new Vector2(0.32f, -0.12f), new Vector2(0.2f, 0f), 0.045f, color);
                    AddLine(vh, rect, new Vector2(0.32f, -0.12f), new Vector2(0.2f, -0.24f), 0.045f, color);
                    break;
                case ToolIconKind.Sticker:
                    AddPolygon(vh, rect, color,
                        new Vector2(-0.3f, 0.36f),
                        new Vector2(0.28f, 0.36f),
                        new Vector2(0.38f, 0.22f),
                        new Vector2(0.38f, -0.34f),
                        new Vector2(-0.3f, -0.34f));
                    AddPolygon(vh, rect, new Color(0f, 0f, 0f, 0.45f),
                        new Vector2(0.2f, 0.36f),
                        new Vector2(0.38f, 0.18f),
                        new Vector2(0.2f, 0.18f));
                    AddFilledCircle(vh, rect, new Vector2(-0.08f, 0f), 0.13f, color);
                    AddPolygon(vh, rect, color,
                        new Vector2(0.12f, 0.12f),
                        new Vector2(0.28f, -0.14f),
                        new Vector2(0f, -0.14f));
                    break;
            }
        }

        private static void AddRotatedBox(VertexHelper vh, Rect rect, Vector2 center, Vector2 size, float angleDegrees, Color color)
        {
            var half = size * 0.5f;
            var radians = angleDegrees * Mathf.Deg2Rad;
            var cos = Mathf.Cos(radians);
            var sin = Mathf.Sin(radians);
            Vector2 Rotate(Vector2 point)
            {
                return center + new Vector2(point.x * cos - point.y * sin, point.x * sin + point.y * cos);
            }

            AddQuad(vh,
                ToRectPoint(rect, Rotate(new Vector2(-half.x, -half.y))),
                ToRectPoint(rect, Rotate(new Vector2(-half.x, half.y))),
                ToRectPoint(rect, Rotate(new Vector2(half.x, half.y))),
                ToRectPoint(rect, Rotate(new Vector2(half.x, -half.y))),
                color);
        }

        private static void AddBox(VertexHelper vh, Rect rect, Vector2 center, Vector2 size, Color color)
        {
            var half = size * 0.5f;
            AddQuad(vh, ToRectPoint(rect, center + new Vector2(-half.x, -half.y)), ToRectPoint(rect, center + new Vector2(-half.x, half.y)), ToRectPoint(rect, center + new Vector2(half.x, half.y)), ToRectPoint(rect, center + new Vector2(half.x, -half.y)), color);
        }

        private static void AddPolygon(VertexHelper vh, Rect rect, Color color, params Vector2[] points)
        {
            if (points == null || points.Length < 3)
            {
                return;
            }

            var start = vh.currentVertCount;
            var vertex = UIVertex.simpleVert;
            vertex.color = color;
            for (var i = 0; i < points.Length; i++)
            {
                vertex.position = ToRectPoint(rect, points[i]);
                vh.AddVert(vertex);
            }

            for (var i = 1; i < points.Length - 1; i++)
            {
                vh.AddTriangle(start, start + i, start + i + 1);
            }
        }

        private static void AddFilledCircle(VertexHelper vh, Rect rect, Vector2 center, float radius, Color color, int segments = 16)
        {
            var start = vh.currentVertCount;
            var vertex = UIVertex.simpleVert;
            vertex.color = color;
            vertex.position = ToRectPoint(rect, center);
            vh.AddVert(vertex);

            for (var i = 0; i <= segments; i++)
            {
                var angle = (i / (float)segments) * Mathf.PI * 2f;
                var point = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                vertex.position = ToRectPoint(rect, point);
                vh.AddVert(vertex);
            }

            for (var i = 1; i <= segments; i++)
            {
                vh.AddTriangle(start, start + i, start + i + 1);
            }
        }

        private static void AddDrop(VertexHelper vh, Rect rect, Vector2 center, float radius, Color color)
        {
            AddFilledCircle(vh, rect, center + new Vector2(0f, -radius * 0.2f), radius * 0.72f, color, 12);
            AddPolygon(vh, rect, color,
                center + new Vector2(0f, radius),
                center + new Vector2(-radius * 0.48f, -radius * 0.12f),
                center + new Vector2(radius * 0.48f, -radius * 0.12f));
        }

        private static void AddLine(VertexHelper vh, Rect rect, Vector2 start, Vector2 end, float thickness, Color color)
        {
            var from = ToRectPoint(rect, start);
            var to = ToRectPoint(rect, end);
            var direction = to - from;
            if (direction.sqrMagnitude <= 0.001f)
            {
                return;
            }

            var normal = new Vector2(-direction.y, direction.x).normalized * (Mathf.Min(rect.width, rect.height) * thickness * 0.5f);
            AddQuad(vh, from - normal, from + normal, to + normal, to - normal, color);
        }

        private static Vector2 ToRectPoint(Rect rect, Vector2 normalized)
        {
            return new Vector2(
                rect.x + (normalized.x + 0.5f) * rect.width,
                rect.y + (normalized.y + 0.5f) * rect.height);
        }

        private static void AddQuad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
        {
            var start = vh.currentVertCount;
            var vertex = UIVertex.simpleVert;
            vertex.color = color;
            vertex.position = a;
            vh.AddVert(vertex);
            vertex.position = b;
            vh.AddVert(vertex);
            vertex.position = c;
            vh.AddVert(vertex);
            vertex.position = d;
            vh.AddVert(vertex);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start + 2, start + 3, start);
        }
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
            _handleRect.sizeDelta = new Vector2(SliderHandleVisualSize, SliderHandleVisualSize);
        }
    }

    private sealed class DrawableColorPickerControl : Selectable, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        private enum DragRegion
        {
            None,
            Hue,
            SaturationValue
        }

        private RectTransform _hueRect;
        private RectTransform _svRect;
        private RectTransform _hueHandle;
        private RectTransform _svHandle;
        private RawImage _hueImage;
        private RawImage _svImage;
        private Image _swatch;
        private Texture2D _hueTexture;
        private Texture2D _svTexture;
        private Action<Color> _onColorChanged;
        private float _hue;
        private float _saturation = 1f;
        private float _value = 1f;
        private DragRegion _dragRegion;
        private float _lastDiagnosticsTime;
        private string _lastDiagnosticsKey = string.Empty;

        internal void Configure(
            RectTransform hueRect,
            RectTransform svRect,
            RectTransform hueHandle,
            RectTransform svHandle,
            RawImage hueImage,
            RawImage svImage,
            Image swatch,
            Color initialColor,
            Action<Color> onColorChanged)
        {
            _hueRect = hueRect;
            _svRect = svRect;
            _hueHandle = hueHandle;
            _svHandle = svHandle;
            _hueImage = hueImage;
            _svImage = svImage;
            _swatch = swatch;
            _onColorChanged = onColorChanged;
            BuildHueTexture();
            SetColor(initialColor, false);
        }

        internal void SetColor(Color color, bool notify)
        {
            Color.RGBToHSV(color, out _hue, out _saturation, out _value);
            UpdateSaturationValueTexture();
            UpdateVisuals();
            if (notify)
            {
                _onColorChanged?.Invoke(CurrentColor());
            }
        }

        internal bool SetValueFromScreenPosition(Vector2 screenPosition, Camera eventCamera, bool notify)
        {
            if (!IsActive() || !IsInteractable())
            {
                return false;
            }

            if (_dragRegion == DragRegion.None)
            {
                _dragRegion = ResolveRegion(screenPosition, eventCamera);
            }

            if (_dragRegion == DragRegion.Hue)
            {
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_hueRect, screenPosition, eventCamera, out var localPoint))
                {
                    return false;
                }

                if (!TryGetHueFromLocalPoint(localPoint, _hueRect.rect, out var hue))
                {
                    return false;
                }

                _hue = hue;
                UpdateSaturationValueTexture();
                UpdateVisuals();
                LogPickerDiagnostics("Hue", screenPosition, localPoint);
                if (notify)
                {
                    _onColorChanged?.Invoke(CurrentColor());
                }

                return true;
            }

            if (_dragRegion == DragRegion.SaturationValue)
            {
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_svRect, screenPosition, eventCamera, out var localPoint))
                {
                    return false;
                }

                GetSaturationValueFromLocalPoint(localPoint, _svRect.rect, out _saturation, out _value);
                UpdateVisuals();
                LogPickerDiagnostics("SaturationValue", screenPosition, localPoint);
                if (notify)
                {
                    _onColorChanged?.Invoke(CurrentColor());
                }

                return true;
            }

            return false;
        }

        public override void OnPointerDown(PointerEventData eventData)
        {
            base.OnPointerDown(eventData);
            _dragRegion = ResolveRegion(eventData.position, eventData.pressEventCamera);
            SetValueFromScreenPosition(eventData.position, eventData.pressEventCamera, true);
        }

        public void OnDrag(PointerEventData eventData)
        {
            SetValueFromScreenPosition(eventData.position, eventData.pressEventCamera, true);
        }

        public override void OnPointerUp(PointerEventData eventData)
        {
            base.OnPointerUp(eventData);
            _dragRegion = DragRegion.None;
        }

        internal void EndVirtualDrag()
        {
            _dragRegion = DragRegion.None;
        }

        private DragRegion ResolveRegion(Vector2 screenPosition, Camera eventCamera)
        {
            if (_svRect != null && RectTransformUtility.RectangleContainsScreenPoint(_svRect, screenPosition, eventCamera))
            {
                return DragRegion.SaturationValue;
            }

            if (_hueRect != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(_hueRect, screenPosition, eventCamera, out var localPoint))
            {
                var rect = _hueRect.rect;
                var radius = Mathf.Min(rect.width, rect.height) * 0.5f;
                var distance = Vector2.Distance(localPoint, rect.center);
                if (distance >= radius * 0.68f && distance <= radius)
                {
                    return DragRegion.Hue;
                }
            }

            return DragRegion.None;
        }

        private Color CurrentColor()
        {
            var color = Color.HSVToRGB(_hue, _saturation, _value);
            color.a = 1f;
            return color;
        }

        private void UpdateVisuals()
        {
            var color = CurrentColor();
            if (_swatch != null)
            {
                _swatch.color = color;
            }

            if (_hueHandle != null && _hueRect != null)
            {
                var rect = _hueRect.rect;
                _hueHandle.anchoredPosition = GetHueHandlePosition(rect);
            }

            if (_svHandle != null && _svRect != null)
            {
                var rect = _svRect.rect;
                _svHandle.anchoredPosition = GetSaturationValueHandlePosition(rect);
            }
        }

        private bool TryGetHueFromLocalPoint(Vector2 localPoint, Rect rect, out float hue)
        {
            var delta = localPoint - rect.center;
            if (delta.sqrMagnitude < 1f)
            {
                hue = _hue;
                return false;
            }

            hue = Mathf.Repeat(Mathf.Atan2(delta.y, delta.x) / (Mathf.PI * 2f), 1f);
            return true;
        }

        private static void GetSaturationValueFromLocalPoint(Vector2 localPoint, Rect rect, out float saturation, out float value)
        {
            saturation = rect.width > 0.01f ? Mathf.Clamp01((localPoint.x - rect.xMin) / rect.width) : 0f;
            value = rect.height > 0.01f ? Mathf.Clamp01((localPoint.y - rect.yMin) / rect.height) : 0f;
        }

        private Vector2 GetHueHandlePosition(Rect rect)
        {
            var radius = Mathf.Min(rect.width, rect.height) * 0.43f;
            var angle = _hue * Mathf.PI * 2f;
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }

        private Vector2 GetSaturationValueHandlePosition(Rect rect)
        {
            return new Vector2(
                (_saturation - 0.5f) * rect.width,
                (_value - 0.5f) * rect.height);
        }

        private void LogPickerDiagnostics(string region, Vector2 screenPosition, Vector2 localPoint)
        {
            if (Time.unscaledTime - _lastDiagnosticsTime < 0.25f)
            {
                return;
            }

            var hueHandle = _hueHandle != null ? _hueHandle.anchoredPosition : Vector2.zero;
            var svHandle = _svHandle != null ? _svHandle.anchoredPosition : Vector2.zero;
            var key = $"{region}:{Mathf.RoundToInt(_hue * 360f)}:{Mathf.RoundToInt(_saturation * 100f)}:{Mathf.RoundToInt(_value * 100f)}:{Mathf.RoundToInt(hueHandle.x)}:{Mathf.RoundToInt(hueHandle.y)}:{Mathf.RoundToInt(svHandle.x)}:{Mathf.RoundToInt(svHandle.y)}";
            if (string.Equals(key, _lastDiagnosticsKey, StringComparison.Ordinal))
            {
                return;
            }

            _lastDiagnosticsTime = Time.unscaledTime;
            _lastDiagnosticsKey = key;
            DrawableSuitsDiagnostics.Info($"ColorPickerInput region={region}; hue={_hue:0.###}; saturation={_saturation:0.###}; value={_value:0.###}; screen={screenPosition}; local={localPoint}; hueHandle={hueHandle}; svHandle={svHandle}");
        }

        private void BuildHueTexture()
        {
            if (_hueImage == null)
            {
                return;
            }

            _hueTexture = new Texture2D(128, 128, TextureFormat.RGBA32, false)
            {
                name = "DrawableSuitsHueRing",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            var center = new Vector2((_hueTexture.width - 1) * 0.5f, (_hueTexture.height - 1) * 0.5f);
            var outer = _hueTexture.width * 0.49f;
            var inner = _hueTexture.width * 0.34f;
            for (var y = 0; y < _hueTexture.height; y++)
            {
                for (var x = 0; x < _hueTexture.width; x++)
                {
                    var delta = new Vector2(x, y) - center;
                    var distance = delta.magnitude;
                    if (distance < inner || distance > outer)
                    {
                        _hueTexture.SetPixel(x, y, Color.clear);
                        continue;
                    }

                    var hue = Mathf.Repeat(Mathf.Atan2(delta.y, delta.x) / (Mathf.PI * 2f), 1f);
                    _hueTexture.SetPixel(x, y, Color.HSVToRGB(hue, 1f, 1f));
                }
            }

            _hueTexture.Apply(false, true);
            _hueImage.texture = _hueTexture;
            _hueImage.color = Color.white;
            _hueImage.raycastTarget = true;
        }

        private void UpdateSaturationValueTexture()
        {
            if (_svImage == null)
            {
                return;
            }

            if (_svTexture == null)
            {
                _svTexture = new Texture2D(64, 64, TextureFormat.RGBA32, false)
                {
                    name = "DrawableSuitsSaturationValue",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };
                _svImage.texture = _svTexture;
                _svImage.raycastTarget = true;
            }

            for (var y = 0; y < _svTexture.height; y++)
            {
                var value = y / (float)(_svTexture.height - 1);
                for (var x = 0; x < _svTexture.width; x++)
                {
                    var saturation = x / (float)(_svTexture.width - 1);
                    _svTexture.SetPixel(x, y, Color.HSVToRGB(_hue, saturation, value));
                }
            }

            _svTexture.Apply(false, false);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (_hueTexture != null)
            {
                Destroy(_hueTexture);
                _hueTexture = null;
            }
            if (_svTexture != null)
            {
                Destroy(_svTexture);
                _svTexture = null;
            }
        }
    }

    private sealed class TextStampRenderer
    {
        private const int RenderFontSize = 128;
        private const int RenderHeight = 256;
        private const int RenderPadding = 64;
        private const int TrimPadding = 8;

        private GameObject _root;
        private Camera _camera;
        private Canvas _canvas;
        private RectTransform _canvasRect;
        private Text _text;
        private RectTransform _textRect;
        private RenderTexture _renderTexture;
        private Texture2D _readbackTexture;
        private Texture2D _texture;
        private string _cachedKey = string.Empty;

        internal Texture2D GetOrRender(string text, out string key, out string failureReason)
        {
            failureReason = string.Empty;
            var normalized = NormalizeTextStampValue(text);
            key = $"{normalized}|Arial|{RenderFontSize}|AlphaMaskV2";
            if (string.IsNullOrWhiteSpace(normalized))
            {
                failureReason = "empty text";
                return null;
            }

            if (_texture != null && string.Equals(key, _cachedKey, StringComparison.Ordinal))
            {
                return _texture;
            }

            try
            {
                var font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                if (font == null)
                {
                    failureReason = "built-in Arial font unavailable";
                    return null;
                }

                font.RequestCharactersInTexture(normalized, RenderFontSize, FontStyle.Normal);

                var rawWidth = CalculateTextureWidth(font, normalized, RenderFontSize);
                const int rawHeight = RenderHeight;
                EnsureObjects(rawWidth, rawHeight);

                _text.font = font;
                _text.text = normalized;
                _text.fontSize = RenderFontSize;
                _text.fontStyle = FontStyle.Normal;
                _text.alignment = TextAnchor.MiddleCenter;
                _text.horizontalOverflow = HorizontalWrapMode.Overflow;
                _text.verticalOverflow = VerticalWrapMode.Overflow;
                _text.supportRichText = false;
                _text.color = Color.white;

                _canvasRect.sizeDelta = new Vector2(rawWidth, rawHeight);
                _textRect.sizeDelta = new Vector2(rawWidth - (RenderPadding * 2f), rawHeight);
                _camera.orthographicSize = rawHeight * 0.5f;
                _camera.aspect = rawWidth / (float)rawHeight;
                _camera.targetTexture = _renderTexture;

                var previousActive = RenderTexture.active;
                var previousRootActive = _root.activeSelf;
                try
                {
                    _root.SetActive(true);
                    Canvas.ForceUpdateCanvases();
                    RenderTexture.active = _renderTexture;
                    GL.Clear(true, true, new Color(0f, 0f, 0f, 0f));
                    _camera.Render();
                    _readbackTexture.ReadPixels(new Rect(0f, 0f, rawWidth, rawHeight), 0, 0, false);
                    _readbackTexture.Apply(false, false);
                }
                finally
                {
                    RenderTexture.active = previousActive;
                    _root.SetActive(previousRootActive);
                }

                if (!TryBuildTrimmedAlphaMask(_readbackTexture, out var trimmedTexture, out var rawBounds, out var finalSize, out var visiblePixels))
                {
                    failureReason = "rendered text texture was empty";
                    DrawableSuitsDiagnostics.Warn($"TextStampSkipped: reason={failureReason}; textLength={normalized.Length}; rawTexture={rawWidth}x{rawHeight}; alphaMode=luminance");
                    return null;
                }

                if (_texture != null && !ReferenceEquals(_texture, trimmedTexture))
                {
                    UnityEngine.Object.Destroy(_texture);
                }

                _texture = trimmedTexture;
                _cachedKey = key;
                DrawableSuitsDiagnostics.Info($"TextStampRendered: textLength={normalized.Length}; rawTexture={rawWidth}x{rawHeight}; glyphBounds={rawBounds}; finalTexture={finalSize.x}x{finalSize.y}; visiblePixels={visiblePixels}; alphaMode=luminance");
                return _texture;
            }
            catch (Exception ex)
            {
                failureReason = ex.Message;
                DrawableSuitsDiagnostics.Exception($"TextStampSkipped: render exception. textLength={normalized.Length}", ex);
                return null;
            }
        }

        internal void Destroy()
        {
            if (_texture != null)
            {
                UnityEngine.Object.Destroy(_texture);
                _texture = null;
            }
            if (_renderTexture != null)
            {
                _renderTexture.Release();
                UnityEngine.Object.Destroy(_renderTexture);
                _renderTexture = null;
            }
            if (_readbackTexture != null)
            {
                UnityEngine.Object.Destroy(_readbackTexture);
                _readbackTexture = null;
            }
            if (_root != null)
            {
                UnityEngine.Object.Destroy(_root);
                _root = null;
            }
            _cachedKey = string.Empty;
        }

        private void EnsureObjects(int rawWidth, int rawHeight)
        {
            if (_root == null)
            {
                _root = new GameObject("DrawableSuitsTextStampRenderer");
                _root.hideFlags = HideFlags.HideAndDontSave;
                _root.transform.position = new Vector3(10000f, 10000f, 10000f);

                var cameraObject = new GameObject("DrawableSuitsTextStampCamera", typeof(Camera));
                cameraObject.hideFlags = HideFlags.HideAndDontSave;
                cameraObject.transform.SetParent(_root.transform, false);
                cameraObject.transform.localPosition = new Vector3(0f, 0f, -10f);
                cameraObject.transform.localRotation = Quaternion.identity;
                _camera = cameraObject.GetComponent<Camera>();
                _camera.enabled = false;
                _camera.clearFlags = CameraClearFlags.SolidColor;
                _camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
                _camera.orthographic = true;
                _camera.nearClipPlane = 0.01f;
                _camera.farClipPlane = 50f;
                _camera.cullingMask = 1 << 2;

                var canvasObject = new GameObject("DrawableSuitsTextStampCanvas", typeof(RectTransform), typeof(Canvas));
                canvasObject.hideFlags = HideFlags.HideAndDontSave;
                canvasObject.transform.SetParent(_root.transform, false);
                canvasObject.transform.localPosition = Vector3.zero;
                canvasObject.transform.localRotation = Quaternion.identity;
                canvasObject.transform.localScale = Vector3.one;
                _canvasRect = canvasObject.GetComponent<RectTransform>();
                _canvasRect.pivot = new Vector2(0.5f, 0.5f);
                _canvas = canvasObject.GetComponent<Canvas>();
                _canvas.renderMode = RenderMode.WorldSpace;
                _canvas.worldCamera = _camera;
                _canvas.pixelPerfect = true;

                var textObject = new GameObject("DrawableSuitsTextStampText", typeof(RectTransform), typeof(Text));
                textObject.hideFlags = HideFlags.HideAndDontSave;
                textObject.transform.SetParent(canvasObject.transform, false);
                _textRect = textObject.GetComponent<RectTransform>();
                _textRect.anchorMin = new Vector2(0.5f, 0.5f);
                _textRect.anchorMax = new Vector2(0.5f, 0.5f);
                _textRect.pivot = new Vector2(0.5f, 0.5f);
                _textRect.anchoredPosition = Vector2.zero;
                _text = textObject.GetComponent<Text>();
                _text.raycastTarget = false;

                SetLayerRecursively(_root, 2);
                _root.SetActive(false);
            }

            if (_renderTexture == null || _renderTexture.width != rawWidth || _renderTexture.height != rawHeight)
            {
                if (_renderTexture != null)
                {
                    _renderTexture.Release();
                    UnityEngine.Object.Destroy(_renderTexture);
                }

                _renderTexture = new RenderTexture(rawWidth, rawHeight, 0, RenderTextureFormat.ARGB32)
                {
                    name = "DrawableSuitsTextStampRenderTexture",
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                _renderTexture.Create();
            }

            if (_readbackTexture == null || _readbackTexture.width != rawWidth || _readbackTexture.height != rawHeight)
            {
                if (_readbackTexture != null)
                {
                    UnityEngine.Object.Destroy(_readbackTexture);
                }

                _readbackTexture = new Texture2D(rawWidth, rawHeight, TextureFormat.RGBA32, false)
                {
                    name = "DrawableSuitsTextStampReadback",
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                _cachedKey = string.Empty;
            }
        }

        private static int CalculateTextureWidth(Font font, string text, int fontSize)
        {
            var settings = new TextGenerationSettings
            {
                font = font,
                fontSize = fontSize,
                fontStyle = FontStyle.Normal,
                color = Color.white,
                textAnchor = TextAnchor.MiddleCenter,
                horizontalOverflow = HorizontalWrapMode.Overflow,
                verticalOverflow = VerticalWrapMode.Overflow,
                richText = false,
                lineSpacing = 1f,
                scaleFactor = 1f,
                generationExtents = new Vector2(2048f, 256f),
                pivot = new Vector2(0.5f, 0.5f),
                updateBounds = true
            };
            var generator = new TextGenerator();
            var preferred = generator.GetPreferredWidth(text, settings);
            var maxSize = Mathf.Clamp(SystemInfo.maxTextureSize, 2048, 8192);
            return Mathf.Clamp(Mathf.CeilToInt(preferred) + (RenderPadding * 2), 256, maxSize);
        }

        private static bool TryBuildTrimmedAlphaMask(Texture2D source, out Texture2D trimmedTexture, out RectInt rawBounds, out Vector2Int finalSize, out int visiblePixels)
        {
            trimmedTexture = null;
            rawBounds = default;
            finalSize = default;
            visiblePixels = 0;

            if (source == null)
            {
                return false;
            }

            var pixels = source.GetPixels32();
            var minX = source.width;
            var minY = source.height;
            var maxX = -1;
            var maxY = -1;

            for (var y = 0; y < source.height; y++)
            {
                var row = y * source.width;
                for (var x = 0; x < source.width; x++)
                {
                    var alpha = AlphaFromLuminance(pixels[row + x]);
                    if (alpha <= 4)
                    {
                        continue;
                    }

                    minX = Mathf.Min(minX, x);
                    minY = Mathf.Min(minY, y);
                    maxX = Mathf.Max(maxX, x);
                    maxY = Mathf.Max(maxY, y);
                    visiblePixels++;
                }
            }

            if (maxX < minX || maxY < minY)
            {
                return false;
            }

            minX = Mathf.Max(0, minX - TrimPadding);
            minY = Mathf.Max(0, minY - TrimPadding);
            maxX = Mathf.Min(source.width - 1, maxX + TrimPadding);
            maxY = Mathf.Min(source.height - 1, maxY + TrimPadding);

            var width = Mathf.Max(1, maxX - minX + 1);
            var height = Mathf.Max(1, maxY - minY + 1);
            var outputPixels = new Color32[width * height];

            for (var y = 0; y < height; y++)
            {
                var sourceRow = (minY + y) * source.width;
                var outputRow = y * width;
                for (var x = 0; x < width; x++)
                {
                    var alpha = AlphaFromLuminance(pixels[sourceRow + minX + x]);
                    outputPixels[outputRow + x] = alpha <= 1
                        ? new Color32(255, 255, 255, 0)
                        : new Color32(255, 255, 255, alpha);
                }
            }

            trimmedTexture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "DrawableSuitsTextStampTexture",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            trimmedTexture.SetPixels32(outputPixels);
            trimmedTexture.Apply(false, false);

            rawBounds = new RectInt(minX, minY, width, height);
            finalSize = new Vector2Int(width, height);
            return true;
        }

        private static byte AlphaFromLuminance(Color32 pixel)
        {
            var luminance = Mathf.Max(pixel.r, Mathf.Max(pixel.g, pixel.b));
            return luminance <= 1 ? (byte)0 : (byte)luminance;
        }

        private static void SetLayerRecursively(GameObject gameObject, int layer)
        {
            if (gameObject == null)
            {
                return;
            }

            gameObject.layer = layer;
            for (var i = 0; i < gameObject.transform.childCount; i++)
            {
                SetLayerRecursively(gameObject.transform.GetChild(i).gameObject, layer);
            }
        }
    }

    private void Start()
    {
        _cursor = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        LoadRecentColors();
        RefreshFileLists();
        DrawableSuitsDiagnostics.Info($"SuitEditorController.Start complete. Screen={Screen.width}x{Screen.height}; designFiles={_designFiles.Count}; decalFiles={_decalFiles.Count}; recentColors={_recentColors.Count}");
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
            ClearPlacementSourcePixelCache(PlacementEditTarget.Decal, "controller destroyed loaded decal");
            Destroy(_loadedDecal);
            _loadedDecal = null;
        }

        if (_checkerTexture != null)
        {
            Destroy(_checkerTexture);
            _checkerTexture = null;
        }
        DestroyPlacementEditResources();
        ResetNativeCursor("plugin destroy");
        DestroyCursorGraphics();
        if (_textStampRenderer != null)
        {
            _textStampRenderer.Destroy();
            _textStampRenderer = null;
            _textStampTexture = null;
        }
        foreach (var stickerTexture in _stickerStampTextures.Values)
        {
            if (stickerTexture != null)
            {
                Destroy(stickerTexture);
            }
        }
        _stickerStampTextures.Clear();
        CancelPendingDecalImport("plugin destroy");

        if (_editorCanvasObject != null)
        {
            Destroy(_editorCanvasObject);
            _editorCanvasObject = null;
        }
        if (_cursorCanvasObject != null)
        {
            DestroyLegacyCursorCanvas("plugin destroy");
        }
        DestroyCanvasCursor("plugin destroy");

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
        PollPendingDecalImport();
        HandleControllerCursor();
        UpdateCanvasCursor(false, "update");
        if (IsWorldThirdPersonMode)
        {
            UpdateWorldEditorCamera(false);
        }
        HandleVirtualCursorClick();
        HandleEditorShortcuts();
        if (IsWorldThirdPersonMode)
        {
            UpdateWorldBrushMarker();
        }
        else
        {
            UpdateBrushIndicator();
        }
        UpdateDecalPlacementPreview();
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
        ResetNativeCursor("open start");
        _isOpen = true;
        _editorCanvasObject.SetActive(true);
        DestroyLegacyCursorCanvas($"open from {source}");
        InitializePointerForOpen(source);
        EnsureCanvasCursor();
        UpdateCanvasCursor(true, $"open from {source}");
        RefreshFileLists();
        _uvFallbackMode = DrawableSuitsPlugin.ModConfig.StartInUvFallbackMode.Value;
        ResetUvPanelView($"open from {source}", false);
        ResetPlacementEdits("editor open", false);
        EnsureValidToolForCurrentState($"open from {source}");
        TryRebuildPreviewForCurrentReadiness(source);
        RefreshEditorReadiness($"after preview ({source})");
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

    internal int RepairClosedEditorState(string reason)
    {
        if (_isOpen)
        {
            return 0;
        }

        var repaired = 0;
        if (_editorCanvasObject != null && _editorCanvasObject.activeSelf)
        {
            _editorCanvasObject.SetActive(false);
            repaired++;
        }

        var hadPreviewArtifacts = _previewRoot != null
            || _previewRigRoot != null
            || _previewCamera != null
            || _previewTexture != null
            || _worldEditorCameraObject != null
            || _worldEditorCamera != null
            || _worldPaintProxyObject != null
            || _worldBrushMarker != null;
        if (hadPreviewArtifacts)
        {
            DestroyPreview();
            repaired++;
        }

        if (_editorUiInputActive)
        {
            EndEditorUiInput();
            repaired++;
        }

        if (_playerInputStateCaptured || _disabledGameplayActions.Count > 0)
        {
            RestorePlayerInputState();
            repaired++;
        }

        if (_cursorStateCaptured)
        {
            RestoreCursorState(IsSceneChangeSafetyReason(reason));
            repaired++;
        }

        if (_cursorCanvasObject != null)
        {
            DestroyLegacyCursorCanvas($"repair closed state {reason}");
            repaired++;
        }
        if (_canvasCursorObject != null && _canvasCursorObject.activeSelf)
        {
            HideCanvasCursor($"repair closed state {reason}", true);
            repaired++;
        }
        if (!string.IsNullOrEmpty(_nativeCursorKey) || _nativeCursorTexture != null)
        {
            ResetNativeCursor($"repair closed state {reason}");
            repaired++;
        }

        if (_rendererRestoreStates.Count > 0)
        {
            RestorePlayerRenderers();
            repaired++;
        }

        if (repaired > 0)
        {
            DrawableSuitsDiagnostics.Info($"ClosedEditorStateAssertion reason={reason}; repaired={repaired}; canvasActive={CanvasActiveForDiagnostics}; editorUiInputActive={_editorUiInputActive}; playerInputCaptured={_playerInputStateCaptured}; cursorCaptured={_cursorStateCaptured}; rendererRestoreStates={_rendererRestoreStates.Count}; cursorVisible={Cursor.visible}; cursorLock={Cursor.lockState}");
        }

        return repaired;
    }

    private static bool IsSceneChangeSafetyReason(string reason)
    {
        return !string.IsNullOrWhiteSpace(reason)
            && (reason.IndexOf("Scene", StringComparison.OrdinalIgnoreCase) >= 0
                || reason.IndexOf("MainMenu", StringComparison.OrdinalIgnoreCase) >= 0
                || reason.IndexOf("InitScene", StringComparison.OrdinalIgnoreCase) >= 0);
    }
    private void CloseEditor(EditorCloseReason reason)
    {
        if (!_isOpen)
        {
            return;
        }

        DrawableSuitsDiagnostics.Info($"Closing DrawableSuits editor. reason={reason}");
        _isOpen = false;
        CancelPendingDecalImport($"close {reason}");
        CloseDesignCodePanel();
        CloseSavedDesignsPanel();
        CloseDecalsPanel();
        CloseStickersPanel();
        ClosePlacementEditPanel();
        ResetPlacementEdits($"close {reason}", true);
        HideCanvasCursor($"close {reason}", true);
        if (_editorCanvasObject != null)
        {
            _editorCanvasObject.SetActive(false);
        }
        DestroyLegacyCursorCanvas($"close {reason}");
        ResetNativeCursor($"close {reason}");

        DestroyPreview();
        _strokeActive = false;
        _virtualPointerDown = false;
        _virtualPointerPressTarget = null;
        _virtualPointerSlider = null;
        _virtualPointerColorPicker = null;
        _virtualPointerButton = null;
        _virtualPointerInput = null;
        _gamepadClickArmed = false;
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
        var priorSelectedSuitId = _selectedSuitId;
        var localSuitId = DrawableSuitsPlugin.Registry.GetLocalSuitId();
        var suitIds = DrawableSuitsPlugin.Registry.GetSuitIds();
        _knownSuitCount = suitIds.Count;
        _selectedSuitId = localSuitId;
        if (priorSelectedSuitId != _selectedSuitId)
        {
            ClearUndoHistory("current suit changed");
            InvalidateDecalPreview("current suit changed");
            InvalidateMirrorSurfaceMap("current suit changed");
            DrawableSuitsDiagnostics.Info($"Current local suit selected by readiness. context={context}; previous={priorSelectedSuitId}; current={_selectedSuitId}; known={suitIds.Contains(_selectedSuitId)}");
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
            missing.Add("local player current suit not available");
        }
        if (!_hasEditableSuit)
        {
            missing.Add("current suit has no editable material/texture");
        }

        if (missing.Count > 0)
        {
            return "Diagnostics: " + string.Join("; ", missing);
        }

        return $"Ready. Preview: {_previewMode}. Full-suit editing.";
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
            DrawableSuitsDiagnostics.Exception("BuildEditorCanvas failed", ex);
            if (_editorCanvasObject != null)
            {
                Destroy(_editorCanvasObject);
                _editorCanvasObject = null;
                _canvasRect = null;
                _panelRect = null;
                _cursorMarker = null;
            }

            failureReason = $"DrawableSuits editor cannot open: failed to build Unity UI overlay ({ex.Message}). Check BepInEx/config/DrawableSuits/Logs/diagnostics.log for details.";
            return false;
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
        canvas.sortingOrder = EditorCanvasSortingOrder;

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
        const float panelWidth = 620f;
        const float panelHeight = 1010f;
        _panelRect.sizeDelta = new Vector2(panelWidth, panelHeight);

        var panelImage = panel.GetComponent<Image>();
        panelImage.color = TerminalPanelColor;
        ApplyTerminalOutline(panel, TerminalOutlineColor);

        const float leftX = 18f;
        const float leftW = 274f;
        const float rightX = 314f;
        const float rightW = 286f;
        const float sectionInset = 8f;
        const float toolsY = 150f;
        const float brushY = 270f;
        const float colorY = 432f;
        const float placementY = 150f;
        const float textureHeaderY = 390f;
        const float texturePanelY = 418f;
        const float texturePanelH = 176f;
        const float designY = 608f;
        const float designCardH = 204f;
        const float undoY = 826f;
        const float footerY = 958f;
        const float buttonGap = 8f;
        const float pairedButtonW = (rightW - buttonGap) * 0.5f;
        const float actionButtonW = (rightW - buttonGap * 2f) / 3f;
        const float rowButtonH = 34f;

        CreateAnchoredText(panel.transform, "Title", $"{PluginInfo.Name} {PluginInfo.Version}", 24, FontStyle.Bold, TextAnchor.MiddleLeft, new Rect(leftX, 12f, 360f, 34f), TerminalStatusColor);
        CreateAnchoredButton(panel.transform, "Close", new Rect(panelWidth - 108f, 14f, 88f, 34f), CloseEditor);
        _suitLabel = CreateAnchoredText(panel.transform, "SuitLabel", string.Empty, 18, FontStyle.Normal, TextAnchor.MiddleLeft, new Rect(leftX, 54f, 420f, 28f), TerminalTextColor);
        _statusLabel = CreateAnchoredText(panel.transform, "StatusLabel", string.Empty, 15, FontStyle.Normal, TextAnchor.UpperLeft, new Rect(leftX, 86f, leftW, 50f), TerminalStatusColor);
        _statusLabel.color = TerminalStatusColor;

        CreateAnchoredText(panel.transform, "WorldHelp", "Aim at suit or UV panel. RT stamps/samples; hold paint/erase. Wheel/D-pad zooms target. D-pad L/R or ,/. rotates decals/stickers. Right-drag/right stick pans UV.", 13, FontStyle.Normal, TextAnchor.UpperLeft, new Rect(rightX, 86f, rightW, 58f), TerminalMutedTextColor);

        CreateSectionCard(panel.transform, new Rect(leftX - sectionInset, toolsY, leftW + sectionInset * 2f, 112f));
        CreateSectionCard(panel.transform, new Rect(leftX - sectionInset, brushY, leftW + sectionInset * 2f, 150f));
        CreateSectionCard(panel.transform, new Rect(leftX - sectionInset, colorY, leftW + sectionInset * 2f, 264f));
        CreateSectionCard(panel.transform, new Rect(rightX - sectionInset, placementY, rightW + sectionInset * 2f, 228f));
        CreateSectionCard(panel.transform, new Rect(rightX - sectionInset, textureHeaderY, rightW + sectionInset * 2f, texturePanelH + 38f));
        CreateSectionCard(panel.transform, new Rect(rightX - sectionInset, designY, rightW + sectionInset * 2f, designCardH));

        CreateSectionDivider(panel.transform, new Rect(leftX, toolsY, leftW, 1f));
        CreateAnchoredText(panel.transform, "ToolHeader", "Tools", 16, FontStyle.Bold, TextAnchor.MiddleLeft, new Rect(leftX, toolsY + 8f, 72f, 24f), TerminalTextColor);
        _activeToolLabel = CreateAnchoredText(panel.transform, "ActiveToolLabel", string.Empty, 13, FontStyle.Normal, TextAnchor.MiddleRight, new Rect(leftX + 86f, toolsY + 8f, leftW - 86f, 24f), TerminalMutedTextColor);
        const float toolIcon = 42f;
        const float toolGap = 8f;
        var toolRow1Y = toolsY + 36f;
        var toolRow2Y = toolsY + 78f;
        _paintButton = CreateAnchoredIconButton(panel.transform, "Paint", ToolIconKind.Paint, new Rect(leftX, toolRow1Y, toolIcon, 34f), () => SetTool(EditorTool.Paint));
        _eraseButton = CreateAnchoredIconButton(panel.transform, "Erase", ToolIconKind.Erase, new Rect(leftX + (toolIcon + toolGap), toolRow1Y, toolIcon, 34f), () => SetTool(EditorTool.Erase));
        _fillButton = CreateAnchoredIconButton(panel.transform, "Fill", ToolIconKind.Fill, new Rect(leftX + (toolIcon + toolGap) * 2f, toolRow1Y, toolIcon, 34f), () => SetTool(EditorTool.FillBucket));
        _mirrorButton = CreateAnchoredIconButton(panel.transform, "Mirror", ToolIconKind.Mirror, new Rect(leftX + (toolIcon + toolGap) * 3f, toolRow1Y, toolIcon, 34f), ToggleMirror);
        _decalButton = CreateAnchoredIconButton(panel.transform, "Decal", ToolIconKind.Decal, new Rect(leftX, toolRow2Y, toolIcon, 34f), () => SetTool(EditorTool.Decal));
        _textButton = CreateAnchoredIconButton(panel.transform, "Text", ToolIconKind.Text, new Rect(leftX + (toolIcon + toolGap), toolRow2Y, toolIcon, 34f), () => SetTool(EditorTool.Text));
        _stickerButton = CreateAnchoredIconButton(panel.transform, "Sticker", ToolIconKind.Sticker, new Rect(leftX + (toolIcon + toolGap) * 2f, toolRow2Y, toolIcon, 34f), () => SetTool(EditorTool.Sticker));
        _eyedropperButton = CreateAnchoredIconButton(panel.transform, "Eyedropper", ToolIconKind.Eyedropper, new Rect(leftX + (toolIcon + toolGap) * 3f, toolRow2Y, toolIcon, 34f), () => SetTool(EditorTool.Eyedropper));

        CreateSectionDivider(panel.transform, new Rect(leftX, brushY, leftW, 1f));
        CreateAnchoredText(panel.transform, "BrushHeader", "Brush", 16, FontStyle.Bold, TextAnchor.MiddleLeft, new Rect(leftX, brushY + 8f, leftW, 24f), TerminalTextColor);
        _brushShapeLabel = CreateAnchoredText(panel.transform, "BrushShapeLabel", "Shape", 14, FontStyle.Normal, TextAnchor.MiddleLeft, new Rect(leftX, brushY + 38f, 94f, 24f), TerminalTextColor);
        _brushShapeButton = CreateAnchoredButton(panel.transform, BrushShapeDisplayName(_brushShape), new Rect(leftX + 100f, brushY + 34f, 174f, 30f), ToggleBrushShapeMenu);
        BuildBrushShapeMenu(panel.transform, new Rect(leftX + 100f, brushY + 66f, 174f, 178f));
        _brushSizeLabel = CreateAnchoredText(panel.transform, "BrushSizeLabel", string.Empty, 14, FontStyle.Normal, TextAnchor.MiddleLeft, new Rect(leftX, brushY + 72f, 94f, 24f), TerminalTextColor);
        _brushSizeSlider = CreateAnchoredSlider(panel.transform, "BrushSize", 1f, 96f, _brushSize, new Rect(leftX + 100f, brushY + 74f, 174f, 24f), value => _brushSize = value);
        _brushOpacityLabel = CreateAnchoredText(panel.transform, "BrushOpacityLabel", string.Empty, 14, FontStyle.Normal, TextAnchor.MiddleLeft, new Rect(leftX, brushY + 106f, 94f, 24f), TerminalTextColor);
        _brushOpacitySlider = CreateAnchoredSlider(panel.transform, "BrushOpacity", 0.05f, 1f, _brushOpacity, new Rect(leftX + 100f, brushY + 108f, 174f, 24f), value => _brushOpacity = value);
        _fillToleranceLabel = CreateAnchoredText(panel.transform, "FillToleranceLabel", string.Empty, 14, FontStyle.Normal, TextAnchor.MiddleLeft, new Rect(leftX, brushY + 138f, 104f, 24f), TerminalTextColor);
        _fillToleranceSlider = CreateAnchoredSlider(panel.transform, "FillTolerance", 0f, 0.5f, _fillTolerance, new Rect(leftX + 110f, brushY + 140f, 164f, 24f), value => _fillTolerance = value);

        CreateSectionDivider(panel.transform, new Rect(leftX, colorY, leftW, 1f));
        CreateAnchoredText(panel.transform, "ColorHeader", "Color", 16, FontStyle.Bold, TextAnchor.MiddleLeft, new Rect(leftX, colorY + 8f, leftW, 24f), TerminalTextColor);
        _colorPicker = CreateAnchoredColorPicker(panel.transform, new Rect(leftX, colorY + 34f, leftW, 104f), _brushColor, color =>
        {
            _brushColor = color;
            UpdateColorUi();
        }, out _colorSwatch, out _colorHexInput);
        _colorHexInput.onValueChanged.AddListener(PreviewHexInput);
        _colorHexInput.onEndEdit.AddListener(ApplyHexInput);
        _recentColorsLabel = CreateAnchoredText(panel.transform, "RecentColorsHeader", "Recent Colors", 14, FontStyle.Bold, TextAnchor.MiddleLeft, new Rect(leftX, colorY + 150f, leftW, 22f), TerminalTextColor);
        var recentColorsRect = new Rect(leftX, colorY + 176f, leftW, 68f);
        BuildRecentColorSwatches(panel.transform, recentColorsRect);

        _uvFallbackButton = CreateAnchoredButton(panel.transform, "Use UV Fallback", new Rect(rightX, 54f, 150f, 34f), ToggleUvFallback);

        CreateSectionDivider(panel.transform, new Rect(rightX, placementY, rightW, 1f));
        _placementHeaderLabel = CreateAnchoredText(panel.transform, "PlacementHeader", "Decal", 16, FontStyle.Bold, TextAnchor.MiddleLeft, new Rect(rightX, placementY + 8f, rightW, 24f), TerminalTextColor);
        _decalSizeLabel = CreateAnchoredText(panel.transform, "DecalSizeLabel", string.Empty, 14, FontStyle.Normal, TextAnchor.MiddleLeft, new Rect(rightX, placementY + 40f, 112f, 24f), TerminalTextColor);
        _decalSizeSlider = CreateAnchoredSlider(panel.transform, "DecalSize", 16f, 512f, _decalSize, new Rect(rightX + 120f, placementY + 42f, 160f, 24f), value =>
        {
            if (_tool == EditorTool.Text)
            {
                _textSize = value;
            }
            else if (_tool == EditorTool.Sticker)
            {
                _stickerSize = value;
            }
            else
            {
                _decalSize = value;
            }
            InvalidateDecalPreview("placement size changed");
        });
        _decalRotationLabel = CreateAnchoredText(panel.transform, "DecalRotationLabel", string.Empty, 14, FontStyle.Normal, TextAnchor.MiddleLeft, new Rect(rightX, placementY + 74f, 112f, 24f), TerminalTextColor);
        _decalRotationSlider = CreateAnchoredSlider(panel.transform, "DecalRotation", -180f, 180f, _decalRotation, new Rect(rightX + 120f, placementY + 76f, 160f, 24f), value =>
        {
            if (_tool == EditorTool.Text)
            {
                _textRotation = value;
            }
            else if (_tool == EditorTool.Sticker)
            {
                _stickerRotation = value;
            }
            else
            {
                _decalRotation = value;
            }
            InvalidateDecalPreview("placement rotation changed");
        });
        _textStampInput = CreateAnchoredInputField(panel.transform, "TextStampInput", _textStampValue, new Rect(rightX, placementY + 116f, 142f, 34f));
        _textStampInput.characterLimit = 64;
        _textStampInput.lineType = InputField.LineType.SingleLine;
        _textStampInput.onValueChanged.AddListener(value =>
        {
            _textStampValue = NormalizeTextStampValue(value);
            InvalidateTextStampTexture("text input changed");
            InvalidateDecalPreview("text input changed");
        });
        _selectedDecalLabel = CreateAnchoredValueLabel(panel.transform, "SelectedDecalLabel", "No decal selected", new Rect(rightX, placementY + 154f, pairedButtonW, rowButtonH));
        _decalsMenuButton = CreateAnchoredButton(panel.transform, "Decals", new Rect(rightX + pairedButtonW + buttonGap, placementY + 116f, pairedButtonW, rowButtonH), OpenDecalsPanel);
        _editDecalButton = CreateAnchoredButton(panel.transform, "Edit Decal", new Rect(rightX + pairedButtonW + buttonGap, placementY + 154f, pairedButtonW, rowButtonH), () => OpenPlacementEditPanel(PlacementEditTarget.Decal));
        _selectedStickerShapeLabel = CreateAnchoredValueLabel(panel.transform, "SelectedStickerShapeLabel", StickerShapeDisplayName(_stickerShape), new Rect(rightX, placementY + 154f, pairedButtonW, rowButtonH));
        _stickersMenuButton = CreateAnchoredButton(panel.transform, "Stickers", new Rect(rightX + pairedButtonW + buttonGap, placementY + 116f, pairedButtonW, rowButtonH), OpenStickersPanel);
        _editStickerButton = CreateAnchoredButton(panel.transform, "Edit Sticker", new Rect(rightX + pairedButtonW + buttonGap, placementY + 154f, pairedButtonW, rowButtonH), () => OpenPlacementEditPanel(PlacementEditTarget.Sticker));
        _stickerShapeButton = _stickersMenuButton;

        CreateSectionDivider(panel.transform, new Rect(rightX, textureHeaderY, rightW, 1f));
        CreateAnchoredText(panel.transform, "UvPanelHeader", "UV Panel", 16, FontStyle.Bold, TextAnchor.MiddleLeft, new Rect(rightX, textureHeaderY + 8f, rightW, 22f), TerminalTextColor);

        CreateSectionDivider(panel.transform, new Rect(rightX, designY, rightW, 1f));
        CreateAnchoredText(panel.transform, "DesignHeader", "Design Name", 16, FontStyle.Bold, TextAnchor.MiddleLeft, new Rect(rightX, designY + 8f, rightW, 24f), TerminalTextColor);
        _designNameInput = CreateAnchoredInputField(panel.transform, _designName, new Rect(rightX, designY + 38f, rightW, 34f));
        _designNameInput.onValueChanged.AddListener(value => _designName = value);

        _exportCodeButton = CreateAnchoredButton(panel.transform, "Export Code", new Rect(rightX, designY + 78f, pairedButtonW, rowButtonH), () => OpenDesignCodePanel(true));
        _importCodeButton = CreateAnchoredButton(panel.transform, "Import Code", new Rect(rightX + pairedButtonW + buttonGap, designY + 78f, pairedButtonW, rowButtonH), () => OpenDesignCodePanel(false));

        CreateAnchoredButton(panel.transform, "Undo", new Rect(rightX, designY + 120f, actionButtonW, rowButtonH), Undo);
        CreateAnchoredButton(panel.transform, "Redo", new Rect(rightX + actionButtonW + buttonGap, designY + 120f, actionButtonW, rowButtonH), Redo);
        _resetButton = CreateAnchoredButton(panel.transform, "Reset", new Rect(rightX + (actionButtonW + buttonGap) * 2f, designY + 120f, actionButtonW, rowButtonH), () =>
        {
            SaveUndo("Reset");
            DrawableSuitsPlugin.Registry.ResetSuit(_selectedSuitId);
            ClearRedoHistory("reset");
            InvalidateDecalPreview("reset");
            UpdateUiState();
        });

        _applyButton = CreateAnchoredButton(panel.transform, "Apply", new Rect(rightX, designY + 160f, actionButtonW, rowButtonH), () => DrawableSuitsPlugin.Registry.ApplyEditedTexture(_selectedSuitId, _designName, true));
        _saveButton = CreateAnchoredButton(panel.transform, "Save", new Rect(rightX + actionButtonW + buttonGap, designY + 160f, actionButtonW, rowButtonH), SaveDesign);
        _savedDesignsButton = CreateAnchoredButton(panel.transform, "Designs", new Rect(rightX + (actionButtonW + buttonGap) * 2f, designY + 160f, actionButtonW, rowButtonH), OpenSavedDesignsPanel);

        CreateSectionDivider(panel.transform, new Rect(rightX, undoY, rightW, 1f));
        CreateAnchoredText(panel.transform, "UndoHistoryHeader", "Undo History", 16, FontStyle.Bold, TextAnchor.MiddleLeft, new Rect(rightX, undoY + 8f, rightW, 24f), TerminalTextColor);
        BuildUndoHistoryPanel(panel.transform, new Rect(rightX, undoY + 34f, rightW, 120f));

        var fallbackPreview = CreateUiObject("PreviewViewport", panel.transform, typeof(RectTransform), typeof(Image));
        _previewViewportRect = fallbackPreview.GetComponent<RectTransform>();
        SetAnchoredRect(_previewViewportRect, new Rect(rightX, texturePanelY, rightW, texturePanelH));
        fallbackPreview.transform.SetSiblingIndex(_decalListContent != null ? _decalListContent.GetSiblingIndex() + 1 : fallbackPreview.transform.GetSiblingIndex());
        var previewBackground = fallbackPreview.GetComponent<Image>();
        previewBackground.color = new Color(0.015f, 0.016f, 0.018f, 1f);
        previewBackground.raycastTarget = true;
        ApplyTerminalOutline(fallbackPreview, TerminalOutlineColor);

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

        _uvDecalPreviewRect = CreateUiObject("UvDecalPlacementPreview", fallbackPreview.transform, typeof(RectTransform), typeof(RawImage)).GetComponent<RectTransform>();
        _uvDecalPreviewRect.anchorMin = new Vector2(0f, 1f);
        _uvDecalPreviewRect.anchorMax = new Vector2(0f, 1f);
        _uvDecalPreviewRect.pivot = new Vector2(0.5f, 0.5f);
        _uvDecalPreviewRect.anchoredPosition = Vector2.zero;
        _uvDecalPreviewRect.sizeDelta = new Vector2(32f, 32f);
        _uvDecalPreviewImage = _uvDecalPreviewRect.GetComponent<RawImage>();
        _uvDecalPreviewImage.color = new Color(1f, 1f, 1f, 0.62f);
        _uvDecalPreviewImage.raycastTarget = false;
        _uvDecalPreviewRect.gameObject.SetActive(false);

        _uvMirrorDecalPreviewRect = CreateUiObject("UvMirrorDecalPlacementPreview", fallbackPreview.transform, typeof(RectTransform), typeof(RawImage)).GetComponent<RectTransform>();
        _uvMirrorDecalPreviewRect.anchorMin = new Vector2(0f, 1f);
        _uvMirrorDecalPreviewRect.anchorMax = new Vector2(0f, 1f);
        _uvMirrorDecalPreviewRect.pivot = new Vector2(0.5f, 0.5f);
        _uvMirrorDecalPreviewRect.anchoredPosition = Vector2.zero;
        _uvMirrorDecalPreviewRect.sizeDelta = new Vector2(32f, 32f);
        _uvMirrorDecalPreviewImage = _uvMirrorDecalPreviewRect.GetComponent<RawImage>();
        _uvMirrorDecalPreviewImage.color = new Color(1f, 1f, 1f, 0.5f);
        _uvMirrorDecalPreviewImage.raycastTarget = false;
        _uvMirrorDecalPreviewRect.gameObject.SetActive(false);

        _brushIndicator = CreateUiObject("BrushIndicator", fallbackPreview.transform, typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
        _brushIndicator.sizeDelta = new Vector2(16f, 16f);
        _brushIndicatorImage = _brushIndicator.GetComponent<Image>();
        _brushIndicatorImage.color = new Color(1f, 1f, 1f, 0.32f);
        _brushIndicatorImage.raycastTarget = false;
        _brushIndicator.gameObject.SetActive(false);
        fallbackPreview.SetActive(false);

        CreateAnchoredText(panel.transform, "ControllerHelp", "Controller: left stick cursor, A click, RT use, X undo, Start save.", 12, FontStyle.Normal, TextAnchor.UpperLeft, new Rect(leftX, footerY, leftW, 34f), TerminalMutedTextColor);

        BuildDesignCodePanel();
        BuildSavedDesignsPanel();
        BuildDecalsPanel();
        BuildStickersPanel();
        BuildPlacementEditPanel();

        _editorCanvasObject.SetActive(false);
        RefreshListButtons();
        UpdateUiState();
        RebuildSelectableNavigation();
        LogEditorControlTree(panel.transform);
        DrawableSuitsDiagnostics.Info($"EditorThemeBuilt: theme=ImperiumInspiredTerminal; iconButtons=True; panelColor={panelImage.color}; accent={TerminalAccentColor}; text={TerminalTextColor}");
        var designActionBottom = designY + 160f + rowButtonH;
        var designCardRect = new Rect(rightX - sectionInset, designY, rightW + sectionInset * 2f, designCardH);
        var undoRect = new Rect(rightX, undoY + 34f, rightW, 120f);
        DrawableSuitsDiagnostics.Info($"EditorLayoutBuilt: panel={panelWidth:0}x{panelHeight:0}; left={leftX:0},{toolsY:0},{leftW:0}; right={rightX:0},{placementY:0},{rightW:0}; sections=tools:{toolsY:0},brush:{brushY:0},color:{colorY:0},placement:{placementY:0},uv:{textureHeaderY:0},design:{designY:0},undo:{undoY:0}; textureRect={rightX:0},{texturePanelY:0},{rightW:0},{texturePanelH:0}; recentColorsRect={recentColorsRect.x:0},{recentColorsRect.y:0},{recentColorsRect.width:0},{recentColorsRect.height:0}; designCard={designCardRect.x:0},{designCardRect.y:0},{designCardRect.width:0},{designCardRect.height:0}; designActionBottom={designActionBottom:0}; undoRect={undoRect.x:0},{undoRect.y:0},{undoRect.width:0},{undoRect.height:0}; pairButtonW={pairedButtonW:0.#}; actionButtonW={actionButtonW:0.#}; footerY={footerY:0}");
        DrawableSuitsDiagnostics.Info($"BuildEditorCanvas complete. childCount={_editorCanvasObject.transform.childCount}; panelChildren={panel.transform.childCount}; graphicRaycaster={_editorCanvasObject.GetComponent<GraphicRaycaster>() != null}; mode=compactThirdPerson");
    }

    private void BuildDesignCodePanel()
    {
        _designCodePanelObject = CreateUiObject("DesignCodePanel", _editorCanvasObject.transform, typeof(RectTransform), typeof(Image));
        var overlayRect = _designCodePanelObject.GetComponent<RectTransform>();
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;
        var overlayImage = _designCodePanelObject.GetComponent<Image>();
        overlayImage.color = new Color(0f, 0f, 0f, 0.28f);
        overlayImage.raycastTarget = true;

        var dialog = CreateUiObject("DesignCodeDialog", _designCodePanelObject.transform, typeof(RectTransform), typeof(Image));
        var dialogRect = dialog.GetComponent<RectTransform>();
        dialogRect.anchorMin = new Vector2(0.5f, 0.5f);
        dialogRect.anchorMax = new Vector2(0.5f, 0.5f);
        dialogRect.pivot = new Vector2(0.5f, 0.5f);
        dialogRect.anchoredPosition = Vector2.zero;
        dialogRect.sizeDelta = new Vector2(760f, 430f);
        var dialogImage = dialog.GetComponent<Image>();
        dialogImage.color = TerminalDialogColor;
        dialogImage.raycastTarget = true;
        ApplyTerminalOutline(dialog, TerminalOutlineColor);

        CreateAnchoredText(dialog.transform, "DesignCodeTitle", "Design Code Import / Export", 22, FontStyle.Bold, TextAnchor.MiddleLeft, new Rect(18f, 14f, 560f, 34f), TerminalStatusColor);
        CreateAnchoredText(dialog.transform, "DesignCodeHelp", "Export creates a compact shareable DSUIT2 code for the current editable texture. Import accepts DSUIT2 or legacy DSUIT1 codes into the current suit only; press Save or Apply when ready.", 14, FontStyle.Normal, TextAnchor.UpperLeft, new Rect(18f, 54f, 724f, 48f), TerminalTextColor);

        _designCodeInput = CreateAnchoredInputField(dialog.transform, "DesignCodeInput", string.Empty, new Rect(18f, 112f, 724f, 194f));
        _designCodeInput.lineType = InputField.LineType.MultiLineNewline;
        _designCodeInput.contentType = InputField.ContentType.Standard;
        _designCodeInput.textComponent.alignment = TextAnchor.UpperLeft;
        _designCodeInput.textComponent.fontSize = 12;
        _designCodeInput.textComponent.horizontalOverflow = HorizontalWrapMode.Wrap;
        _designCodeInput.textComponent.verticalOverflow = VerticalWrapMode.Overflow;

        CreateAnchoredButton(dialog.transform, "Copy Current", new Rect(18f, 320f, 132f, 34f), CopyCurrentDesignCode);
        CreateAnchoredButton(dialog.transform, "Paste", new Rect(160f, 320f, 90f, 34f), PasteDesignCodeFromClipboard);
        CreateAnchoredButton(dialog.transform, "Import", new Rect(260f, 320f, 98f, 34f), ImportDesignCodeFromField);
        CreateAnchoredButton(dialog.transform, "Close", new Rect(644f, 320f, 98f, 34f), CloseDesignCodePanel);

        _designCodeStatusLabel = CreateAnchoredText(dialog.transform, "DesignCodeStatus", string.Empty, 13, FontStyle.Normal, TextAnchor.UpperLeft, new Rect(18f, 366f, 724f, 46f), TerminalStatusColor);
        _designCodePanelObject.SetActive(false);
        DrawableSuitsDiagnostics.Info("DesignCodePanel built.");
    }

    private void BuildSavedDesignsPanel()
    {
        _savedDesignsPanelObject = CreateUiObject("SavedDesignsPanel", _editorCanvasObject.transform, typeof(RectTransform), typeof(Image));
        var overlayRect = _savedDesignsPanelObject.GetComponent<RectTransform>();
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;
        var overlayImage = _savedDesignsPanelObject.GetComponent<Image>();
        overlayImage.color = new Color(0f, 0f, 0f, 0.28f);
        overlayImage.raycastTarget = true;

        var dialog = CreateUiObject("SavedDesignsDialog", _savedDesignsPanelObject.transform, typeof(RectTransform), typeof(Image));
        var dialogRect = dialog.GetComponent<RectTransform>();
        dialogRect.anchorMin = new Vector2(0.5f, 0.5f);
        dialogRect.anchorMax = new Vector2(0.5f, 0.5f);
        dialogRect.pivot = new Vector2(0.5f, 0.5f);
        dialogRect.anchoredPosition = Vector2.zero;
        dialogRect.sizeDelta = new Vector2(520f, 520f);
        var dialogImage = dialog.GetComponent<Image>();
        dialogImage.color = TerminalDialogColor;
        dialogImage.raycastTarget = true;
        ApplyTerminalOutline(dialog, TerminalOutlineColor);

        CreateAnchoredText(dialog.transform, "SavedDesignsTitle", "Saved Designs", 22, FontStyle.Bold, TextAnchor.MiddleLeft, new Rect(18f, 14f, 360f, 34f), TerminalStatusColor);
        CreateAnchoredText(dialog.transform, "SavedDesignsHelp", "Select a saved design, then load it into the current suit. Save and Apply stay explicit.", 14, FontStyle.Normal, TextAnchor.UpperLeft, new Rect(18f, 54f, 484f, 42f), TerminalTextColor);
        _designListContent = CreateAnchoredScrollList(dialog.transform, "DesignList", new Rect(18f, 104f, 484f, 276f));

        _loadSelectedDesignButton = CreateAnchoredButton(dialog.transform, "Load Selected", new Rect(18f, 394f, 124f, 34f), LoadSelectedDesign);
        _deleteSelectedDesignButton = CreateAnchoredButton(dialog.transform, "Delete Selected", new Rect(150f, 394f, 124f, 34f), DeleteSelectedDesign);
        CreateAnchoredButton(dialog.transform, "Refresh", new Rect(282f, 394f, 82f, 34f), RefreshSavedDesignsPanel);
        CreateAnchoredButton(dialog.transform, "Close", new Rect(404f, 394f, 98f, 34f), CloseSavedDesignsPanel);
        _savedDesignsStatusLabel = CreateAnchoredText(dialog.transform, "SavedDesignsStatus", string.Empty, 13, FontStyle.Normal, TextAnchor.UpperLeft, new Rect(18f, 442f, 484f, 48f), TerminalStatusColor);

        _savedDesignsPanelObject.SetActive(false);
        DrawableSuitsDiagnostics.Info("SavedDesignsPanel built.");
    }

    private void BuildDecalsPanel()
    {
        _decalsPanelObject = CreateUiObject("DecalsPanel", _editorCanvasObject.transform, typeof(RectTransform), typeof(Image));
        var overlayRect = _decalsPanelObject.GetComponent<RectTransform>();
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;
        var overlayImage = _decalsPanelObject.GetComponent<Image>();
        overlayImage.color = new Color(0f, 0f, 0f, 0.28f);
        overlayImage.raycastTarget = true;

        var dialog = CreateUiObject("DecalsDialog", _decalsPanelObject.transform, typeof(RectTransform), typeof(Image));
        var dialogRect = dialog.GetComponent<RectTransform>();
        dialogRect.anchorMin = new Vector2(0.5f, 0.5f);
        dialogRect.anchorMax = new Vector2(0.5f, 0.5f);
        dialogRect.pivot = new Vector2(0.5f, 0.5f);
        dialogRect.anchoredPosition = Vector2.zero;
        dialogRect.sizeDelta = new Vector2(520f, 520f);
        var dialogImage = dialog.GetComponent<Image>();
        dialogImage.color = TerminalDialogColor;
        dialogImage.raycastTarget = true;
        ApplyTerminalOutline(dialog, TerminalOutlineColor);

        CreateAnchoredText(dialog.transform, "DecalsTitle", "Decals", 22, FontStyle.Bold, TextAnchor.MiddleLeft, new Rect(18f, 14f, 360f, 34f), TerminalStatusColor);
        CreateAnchoredText(dialog.transform, "DecalsHelp", "Select a decal to load it into the current tool. PNG and JPG files are read from the DrawableSuits Decals folder.", 14, FontStyle.Normal, TextAnchor.UpperLeft, new Rect(18f, 54f, 484f, 42f), TerminalTextColor);
        _decalListContent = CreateAnchoredScrollList(dialog.transform, "DecalList", new Rect(18f, 104f, 484f, 276f));

        _deleteSelectedDecalButton = CreateAnchoredButton(dialog.transform, "Delete Selected", new Rect(18f, 394f, 124f, 34f), DeleteSelectedDecal);
        _addDecalButton = CreateAnchoredButton(dialog.transform, "Add Decal", new Rect(150f, 394f, 102f, 34f), ImportDecalFromDialog);
        CreateAnchoredButton(dialog.transform, "Refresh", new Rect(260f, 394f, 94f, 34f), RefreshDecalsPanel);
        CreateAnchoredButton(dialog.transform, "Close", new Rect(404f, 394f, 98f, 34f), CloseDecalsPanel);
        _decalsStatusLabel = CreateAnchoredText(dialog.transform, "DecalsStatus", string.Empty, 13, FontStyle.Normal, TextAnchor.UpperLeft, new Rect(18f, 442f, 484f, 48f), TerminalStatusColor);

        _decalsPanelObject.SetActive(false);
        DrawableSuitsDiagnostics.Info("DecalsPanel built.");
    }

    private void BuildStickersPanel()
    {
        _stickerPanelShapeButtons.Clear();
        _stickersPanelObject = CreateUiObject("StickersPanel", _editorCanvasObject.transform, typeof(RectTransform), typeof(Image));
        var overlayRect = _stickersPanelObject.GetComponent<RectTransform>();
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;
        var overlayImage = _stickersPanelObject.GetComponent<Image>();
        overlayImage.color = new Color(0f, 0f, 0f, 0.28f);
        overlayImage.raycastTarget = true;

        var dialog = CreateUiObject("StickersDialog", _stickersPanelObject.transform, typeof(RectTransform), typeof(Image));
        var dialogRect = dialog.GetComponent<RectTransform>();
        dialogRect.anchorMin = new Vector2(0.5f, 0.5f);
        dialogRect.anchorMax = new Vector2(0.5f, 0.5f);
        dialogRect.pivot = new Vector2(0.5f, 0.5f);
        dialogRect.anchoredPosition = Vector2.zero;
        dialogRect.sizeDelta = new Vector2(520f, 420f);
        var dialogImage = dialog.GetComponent<Image>();
        dialogImage.color = TerminalDialogColor;
        dialogImage.raycastTarget = true;
        ApplyTerminalOutline(dialog, TerminalOutlineColor);

        CreateAnchoredText(dialog.transform, "StickersTitle", "Stickers", 22, FontStyle.Bold, TextAnchor.MiddleLeft, new Rect(18f, 14f, 360f, 34f), TerminalStatusColor);
        CreateAnchoredText(dialog.transform, "StickersHelp", "Choose a built-in sticker shape. Stickers use the current brush color and opacity, then bake into the suit texture when stamped.", 14, FontStyle.Normal, TextAnchor.UpperLeft, new Rect(18f, 54f, 484f, 42f), TerminalTextColor);

        var values = (StickerShape[])Enum.GetValues(typeof(StickerShape));
        const int columns = 3;
        const float buttonWidth = 150f;
        const float buttonHeight = 34f;
        const float gap = 12f;
        for (var i = 0; i < values.Length; i++)
        {
            var shape = values[i];
            var column = i % columns;
            var row = i / columns;
            var rect = new Rect(18f + column * (buttonWidth + gap), 108f + row * (buttonHeight + gap), buttonWidth, buttonHeight);
            var button = CreateAnchoredButton(dialog.transform, StickerShapeDisplayName(shape), rect, () => SelectStickerShape(shape));
            _stickerPanelShapeButtons.Add(button);
        }

        CreateAnchoredButton(dialog.transform, "Close", new Rect(404f, 346f, 98f, 34f), CloseStickersPanel);
        _stickersStatusLabel = CreateAnchoredText(dialog.transform, "StickersStatus", string.Empty, 13, FontStyle.Normal, TextAnchor.UpperLeft, new Rect(18f, 344f, 380f, 48f), TerminalStatusColor);

        _stickersPanelObject.SetActive(false);
        DrawableSuitsDiagnostics.Info("StickersPanel built.");
    }

    private void BuildPlacementEditPanel()
    {
        _placementEditPanelObject = CreateUiObject("PlacementEditPanel", _editorCanvasObject.transform, typeof(RectTransform), typeof(Image));
        var overlayRect = _placementEditPanelObject.GetComponent<RectTransform>();
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;
        var overlayImage = _placementEditPanelObject.GetComponent<Image>();
        overlayImage.color = new Color(0f, 0f, 0f, 0.28f);
        overlayImage.raycastTarget = true;

        var dialog = CreateUiObject("PlacementEditDialog", _placementEditPanelObject.transform, typeof(RectTransform), typeof(Image));
        var dialogRect = dialog.GetComponent<RectTransform>();
        dialogRect.anchorMin = new Vector2(0.5f, 0.5f);
        dialogRect.anchorMax = new Vector2(0.5f, 0.5f);
        dialogRect.pivot = new Vector2(0.5f, 0.5f);
        dialogRect.anchoredPosition = Vector2.zero;
        dialogRect.sizeDelta = new Vector2(720f, 640f);
        var dialogImage = dialog.GetComponent<Image>();
        dialogImage.color = TerminalDialogColor;
        dialogImage.raycastTarget = true;
        ApplyTerminalOutline(dialog, TerminalOutlineColor);

        _placementEditTitleLabel = CreateAnchoredText(dialog.transform, "PlacementEditTitle", "Edit Placement", 22, FontStyle.Bold, TextAnchor.MiddleLeft, new Rect(18f, 14f, 360f, 34f), TerminalStatusColor);
        _placementEditSourceLabel = CreateAnchoredText(dialog.transform, "PlacementEditSource", string.Empty, 13, FontStyle.Normal, TextAnchor.UpperLeft, new Rect(18f, 50f, 360f, 42f), TerminalTextColor);

        var previewFrame = CreateUiObject("PlacementEditPreviewFrame", dialog.transform, typeof(RectTransform), typeof(Image));
        _placementEditPreviewFrameRect = previewFrame.GetComponent<RectTransform>();
        SetAnchoredRect(_placementEditPreviewFrameRect, new Rect(520f, 50f, 160f, 160f));
        previewFrame.GetComponent<Image>().color = TerminalInputColor;
        ApplyTerminalOutline(previewFrame, TerminalOutlineColor);
        var previewBackingObject = CreateUiObject("PlacementEditPreviewChecker", previewFrame.transform, typeof(RectTransform), typeof(RawImage));
        var backingRect = previewBackingObject.GetComponent<RectTransform>();
        backingRect.anchorMin = Vector2.zero;
        backingRect.anchorMax = Vector2.one;
        backingRect.offsetMin = new Vector2(8f, 8f);
        backingRect.offsetMax = new Vector2(-8f, -8f);
        _placementEditPreviewBackingImage = previewBackingObject.GetComponent<RawImage>();
        _placementEditPreviewBackingImage.texture = GetPlacementEditCheckerTexture();
        _placementEditPreviewBackingImage.color = Color.white;
        _placementEditPreviewBackingImage.raycastTarget = false;
        var previewObject = CreateUiObject("PlacementEditPreview", previewFrame.transform, typeof(RectTransform), typeof(RawImage));
        _placementEditPreviewRect = previewObject.GetComponent<RectTransform>();
        _placementEditPreviewRect.anchorMin = new Vector2(0.5f, 0.5f);
        _placementEditPreviewRect.anchorMax = new Vector2(0.5f, 0.5f);
        _placementEditPreviewRect.pivot = new Vector2(0.5f, 0.5f);
        _placementEditPreviewRect.anchoredPosition = Vector2.zero;
        _placementEditPreviewRect.sizeDelta = new Vector2(144f, 144f);
        _placementEditPreviewImage = previewObject.GetComponent<RawImage>();
        _placementEditPreviewImage.color = Color.white;
        _placementEditPreviewImage.raycastTarget = false;

        CreateAnchoredText(dialog.transform, "PlacementCropHeader", "Crop", 16, FontStyle.Bold, TextAnchor.MiddleLeft, new Rect(18f, 104f, 250f, 24f), TerminalStatusColor);
        _placementEditCropLeftLabel = CreateAnchoredText(dialog.transform, "PlacementCropLeftLabel", string.Empty, 13, FontStyle.Normal, TextAnchor.MiddleLeft, new Rect(18f, 136f, 88f, 22f), TerminalTextColor);
        _placementEditCropLeftSlider = CreateAnchoredSlider(dialog.transform, "PlacementCropLeft", 0f, 0.95f, 0f, new Rect(112f, 136f, 250f, 22f), value =>
        {
            var state = GetPlacementEditState(_activePlacementEditTarget);
            if (state == null)
            {
                return;
            }

            state.CropLeft = value;
            OnPlacementEditChanged("crop left");
        });
        _placementEditCropRightLabel = CreateAnchoredText(dialog.transform, "PlacementCropRightLabel", string.Empty, 13, FontStyle.Normal, TextAnchor.MiddleLeft, new Rect(18f, 166f, 88f, 22f), TerminalTextColor);
        _placementEditCropRightSlider = CreateAnchoredSlider(dialog.transform, "PlacementCropRight", 0f, 0.95f, 0f, new Rect(112f, 166f, 250f, 22f), value =>
        {
            var state = GetPlacementEditState(_activePlacementEditTarget);
            if (state == null)
            {
                return;
            }

            state.CropRight = value;
            OnPlacementEditChanged("crop right");
        });
        _placementEditCropBottomLabel = CreateAnchoredText(dialog.transform, "PlacementCropBottomLabel", string.Empty, 13, FontStyle.Normal, TextAnchor.MiddleLeft, new Rect(18f, 196f, 88f, 22f), TerminalTextColor);
        _placementEditCropBottomSlider = CreateAnchoredSlider(dialog.transform, "PlacementCropBottom", 0f, 0.95f, 0f, new Rect(112f, 196f, 250f, 22f), value =>
        {
            var state = GetPlacementEditState(_activePlacementEditTarget);
            if (state == null)
            {
                return;
            }

            state.CropBottom = value;
            OnPlacementEditChanged("crop bottom");
        });
        _placementEditCropTopLabel = CreateAnchoredText(dialog.transform, "PlacementCropTopLabel", string.Empty, 13, FontStyle.Normal, TextAnchor.MiddleLeft, new Rect(18f, 226f, 88f, 22f), TerminalTextColor);
        _placementEditCropTopSlider = CreateAnchoredSlider(dialog.transform, "PlacementCropTop", 0f, 0.95f, 0f, new Rect(112f, 226f, 250f, 22f), value =>
        {
            var state = GetPlacementEditState(_activePlacementEditTarget);
            if (state == null)
            {
                return;
            }

            state.CropTop = value;
            OnPlacementEditChanged("crop top");
        });

        CreateAnchoredText(dialog.transform, "PlacementTransformHeader", "Shape", 16, FontStyle.Bold, TextAnchor.MiddleLeft, new Rect(18f, 268f, 250f, 24f), TerminalStatusColor);
        _placementEditStretchXLabel = CreateAnchoredText(dialog.transform, "PlacementStretchXLabel", string.Empty, 13, FontStyle.Normal, TextAnchor.MiddleLeft, new Rect(18f, 300f, 88f, 22f), TerminalTextColor);
        _placementEditStretchXSlider = CreateAnchoredSlider(dialog.transform, "PlacementStretchX", 0.25f, 3f, 1f, new Rect(112f, 300f, 250f, 22f), value =>
        {
            var state = GetPlacementEditState(_activePlacementEditTarget);
            if (state == null)
            {
                return;
            }

            state.StretchX = value;
            OnPlacementEditChanged("stretch x");
        });
        _placementEditStretchYLabel = CreateAnchoredText(dialog.transform, "PlacementStretchYLabel", string.Empty, 13, FontStyle.Normal, TextAnchor.MiddleLeft, new Rect(18f, 330f, 88f, 22f), TerminalTextColor);
        _placementEditStretchYSlider = CreateAnchoredSlider(dialog.transform, "PlacementStretchY", 0.25f, 3f, 1f, new Rect(112f, 330f, 250f, 22f), value =>
        {
            var state = GetPlacementEditState(_activePlacementEditTarget);
            if (state == null)
            {
                return;
            }

            state.StretchY = value;
            OnPlacementEditChanged("stretch y");
        });
        _placementEditFlipXButton = CreateAnchoredButton(dialog.transform, "Flip X", new Rect(392f, 298f, 90f, 34f), TogglePlacementEditFlipX);
        _placementEditFlipYButton = CreateAnchoredButton(dialog.transform, "Flip Y", new Rect(492f, 298f, 90f, 34f), TogglePlacementEditFlipY);

        CreateAnchoredText(dialog.transform, "PlacementFilterHeader", "Filters", 16, FontStyle.Bold, TextAnchor.MiddleLeft, new Rect(18f, 374f, 250f, 24f), TerminalStatusColor);
        _placementEditFilterValueLabels.Clear();
        _placementEditFilterSliders.Clear();
        for (var i = 0; i < PlacementFilterRows.Length; i++)
        {
            var filter = PlacementFilterRows[i];
            var y = 402f + (i * 24f);
            CreateAnchoredText(dialog.transform, $"PlacementFilter{filter}Label", PlacementFilterDisplayName(filter), 12, FontStyle.Normal, TextAnchor.MiddleLeft, new Rect(18f, y, 92f, 20f), TerminalTextColor);
            var valueLabel = CreateAnchoredText(dialog.transform, $"PlacementFilter{filter}Value", "0%", 12, FontStyle.Normal, TextAnchor.MiddleLeft, new Rect(350f, y, 56f, 20f), TerminalMutedTextColor);
            var capturedFilter = filter;
            var slider = CreateAnchoredSlider(dialog.transform, $"PlacementFilter{filter}", 0f, 1f, 0f, new Rect(112f, y, 230f, 20f), value =>
            {
                var state = GetPlacementEditState(_activePlacementEditTarget);
                if (state == null)
                {
                    return;
                }

                state.SetFilterAmount(capturedFilter, value);
                OnPlacementEditChanged($"filter {capturedFilter}");
            });
            _placementEditFilterValueLabels[filter] = valueLabel;
            _placementEditFilterSliders[filter] = slider;
        }

        CreateAnchoredButton(dialog.transform, "Reset Edits", new Rect(18f, 586f, 126f, 34f), ResetActivePlacementEdit);
        CreateAnchoredButton(dialog.transform, "Close", new Rect(594f, 586f, 90f, 34f), ClosePlacementEditPanel);
        _placementEditStatusLabel = CreateAnchoredText(dialog.transform, "PlacementEditStatus", string.Empty, 13, FontStyle.Normal, TextAnchor.UpperLeft, new Rect(156f, 584f, 426f, 44f), TerminalStatusColor);

        _placementEditPanelObject.SetActive(false);
        DrawableSuitsDiagnostics.Info("PlacementEditPanel built.");
    }

    private void EnsureCursorCanvas()
    {
        if (_cursorCanvasObject != null && _cursorCanvasRect != null && _cursorMarker != null && _cursorBackImage != null && _cursorImage != null)
        {
            return;
        }

        if (_cursorCanvasObject != null)
        {
            Destroy(_cursorCanvasObject);
        }

        _cursorCanvasObject = new GameObject("DrawableSuitsCursorCanvas", typeof(RectTransform), typeof(Canvas));
        DontDestroyOnLoad(_cursorCanvasObject);
        _cursorCanvasRect = _cursorCanvasObject.GetComponent<RectTransform>();
        _cursorCanvasRect.anchorMin = Vector2.zero;
        _cursorCanvasRect.anchorMax = Vector2.one;
        _cursorCanvasRect.offsetMin = Vector2.zero;
        _cursorCanvasRect.offsetMax = Vector2.zero;

        var canvas = _cursorCanvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 32767;
        if (canvas.sortingOrder != 32767 || canvas.sortingOrder <= EditorCanvasSortingOrder)
        {
            DrawableSuitsDiagnostics.Warn($"Cursor canvas sorting order invalid after assignment. requested=32767; actual={canvas.sortingOrder}; editorOrder={EditorCanvasSortingOrder}");
        }

        CreateDynamicCursorMarker(_cursorCanvasObject.transform);
        _cursorCanvasObject.SetActive(false);
        LogCursorCanvasState("created");
    }

    private void CreateDynamicCursorMarker(Transform parent)
    {
        var cursorObject = CreateUiObject("DrawableSuitsCursor", parent, typeof(RectTransform));
        _cursorMarker = cursorObject.GetComponent<RectTransform>();
        _cursorMarker.anchorMin = Vector2.zero;
        _cursorMarker.anchorMax = Vector2.zero;
        _cursorMarker.pivot = new Vector2(0.5f, 0.5f);
        _cursorMarker.sizeDelta = new Vector2(DotCursorRootSize, DotCursorRootSize);

        _cursorBackRect = CreateUiObject("CursorBack", _cursorMarker, typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
        _cursorBackRect.anchorMin = new Vector2(0.5f, 0.5f);
        _cursorBackRect.anchorMax = new Vector2(0.5f, 0.5f);
        _cursorBackRect.pivot = new Vector2(0.5f, 0.5f);
        _cursorBackRect.anchoredPosition = Vector2.zero;
        _cursorBackRect.sizeDelta = new Vector2(DotCursorBackSize, DotCursorBackSize);
        _cursorBackImage = _cursorBackRect.GetComponent<Image>();
        _cursorBackImage.sprite = EnsureCursorDotSprite();
        _cursorBackImage.color = new Color(0f, 0f, 0f, 0.85f);
        _cursorBackImage.raycastTarget = false;
        _cursorBackImage.preserveAspect = true;
        _cursorBackImage.type = Image.Type.Simple;

        _cursorFrontRect = CreateUiObject("CursorFront", _cursorMarker, typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
        _cursorFrontRect.anchorMin = new Vector2(0.5f, 0.5f);
        _cursorFrontRect.anchorMax = new Vector2(0.5f, 0.5f);
        _cursorFrontRect.pivot = new Vector2(0.5f, 0.5f);
        _cursorFrontRect.anchoredPosition = Vector2.zero;
        _cursorFrontRect.sizeDelta = new Vector2(DotCursorFrontSize, DotCursorFrontSize);
        _cursorImage = _cursorFrontRect.GetComponent<Image>();
        _cursorImage.sprite = EnsureCursorDotSprite();
        _cursorImage.color = Color.white;
        _cursorImage.raycastTarget = false;
        _cursorImage.preserveAspect = true;
        _cursorImage.type = Image.Type.Simple;
    }

    private void SetCursorCanvasVisible(bool visible, string context)
    {
        if (visible)
        {
            DestroyLegacyCursorCanvas($"ignored legacy show request: {context}");
            return;
        }

        DestroyLegacyCursorCanvas($"legacy hide request: {context}");
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

    private static void CreateSectionDivider(Transform parent, Rect rect)
    {
        var go = CreateUiObject("TerminalDivider", parent, typeof(RectTransform), typeof(Image));
        SetAnchoredRect(go.GetComponent<RectTransform>(), rect);
        var image = go.GetComponent<Image>();
        image.color = new Color(TerminalAccentColor.r, TerminalAccentColor.g, TerminalAccentColor.b, 0.45f);
        image.raycastTarget = false;
    }

    private static void CreateSectionCard(Transform parent, Rect rect)
    {
        var go = CreateUiObject("TerminalSectionCard", parent, typeof(RectTransform), typeof(Image));
        SetAnchoredRect(go.GetComponent<RectTransform>(), rect);
        var image = go.GetComponent<Image>();
        image.color = new Color(TerminalCardColor.r, TerminalCardColor.g, TerminalCardColor.b, 0.22f);
        image.raycastTarget = false;
        ApplyTerminalOutline(go, new Color(TerminalOutlineColor.r, TerminalOutlineColor.g, TerminalOutlineColor.b, 0.32f));
    }

    private static void ApplyTerminalOutline(GameObject go, Color color)
    {
        if (go == null)
        {
            return;
        }

        var outline = go.GetComponent<Outline>() ?? go.AddComponent<Outline>();
        outline.effectColor = color;
        outline.effectDistance = new Vector2(1.25f, -1.25f);
        outline.useGraphicAlpha = true;
    }

    private static Text CreateAnchoredValueLabel(Transform parent, string name, string text, Rect rect)
    {
        var go = CreateUiObject(name, parent, typeof(RectTransform), typeof(Image));
        SetAnchoredRect(go.GetComponent<RectTransform>(), rect);
        var image = go.GetComponent<Image>();
        image.color = TerminalInputColor;
        image.raycastTarget = false;
        ApplyTerminalOutline(go, TerminalOutlineColor);

        var label = CreateAnchoredText(go.transform, "Label", text, 14, FontStyle.Normal, TextAnchor.MiddleCenter, new Rect(8f, 0f, rect.width - 16f, rect.height), TerminalTextColor);
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.verticalOverflow = VerticalWrapMode.Truncate;
        return label;
    }

    private static Button CreateAnchoredIconButton(Transform parent, string toolName, ToolIconKind iconKind, Rect rect, Action onClick)
    {
        var go = CreateUiObject(toolName + "IconButton", parent, typeof(RectTransform), typeof(Image), typeof(Button));
        SetAnchoredRect(go.GetComponent<RectTransform>(), rect);

        var image = go.GetComponent<Image>();
        image.color = TerminalButtonColor;
        ApplyTerminalOutline(go, TerminalOutlineColor);

        var button = go.GetComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() =>
        {
            onClick?.Invoke();
            ClearSelectedNormalButton();
        });

        var colors = button.colors;
        colors.normalColor = TerminalButtonColor;
        colors.highlightedColor = new Color(0.2f, 0.02f, 0.018f, 1f);
        colors.pressedColor = TerminalButtonPressedColor;
        colors.selectedColor = colors.normalColor;
        button.colors = colors;

        var iconObject = CreateUiObject("Icon", go.transform, typeof(RectTransform));
        var iconRect = iconObject.GetComponent<RectTransform>();
        iconRect.anchorMin = new Vector2(0.5f, 0.5f);
        iconRect.anchorMax = new Vector2(0.5f, 0.5f);
        iconRect.pivot = new Vector2(0.5f, 0.5f);
        iconRect.anchoredPosition = Vector2.zero;
        iconRect.sizeDelta = new Vector2(28f, 28f);

        var iconTexture = LoadToolIconTexture(iconKind);
        if (iconTexture != null)
        {
            var rawImage = iconObject.AddComponent<RawImage>();
            rawImage.texture = iconTexture;
            rawImage.color = TerminalTextColor;
            rawImage.raycastTarget = false;
            DrawableSuitsDiagnostics.Info($"IconButtonBuilt: name={toolName}; icon={iconKind}; source=EmbeddedPng; texture={iconTexture.width}x{iconTexture.height}; rect={rect}");
        }
        else
        {
            var fallbackIcon = iconObject.AddComponent<DrawableToolIconGraphic>();
            fallbackIcon.Configure(iconKind, TerminalTextColor);
            DrawableSuitsDiagnostics.Warn($"IconButtonBuilt: name={toolName}; icon={iconKind}; source=ProceduralFallback; rect={rect}");
        }

        return button;
    }

    private static Texture2D LoadToolIconTexture(ToolIconKind iconKind)
    {
        if (ToolIconTextureCache.TryGetValue(iconKind, out var cached))
        {
            return cached;
        }

        var fileName = ToolIconFileName(iconKind);
        var requestedResourceName = $"DrawableSuits.Assets.ToolIcons.{fileName}";
        var assembly = typeof(SuitEditorController).Assembly;
        var resourceName = requestedResourceName;
        var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            foreach (var candidate in assembly.GetManifestResourceNames())
            {
                if (candidate.EndsWith($".Assets.ToolIcons.{fileName}", StringComparison.OrdinalIgnoreCase))
                {
                    resourceName = candidate;
                    stream = assembly.GetManifestResourceStream(candidate);
                    break;
                }
            }
        }

        if (stream == null)
        {
            DrawableSuitsDiagnostics.Warn($"ToolIconAssetFallback: icon={iconKind}; requestedResource={requestedResourceName}; reason=resource not found");
            ToolIconTextureCache[iconKind] = null;
            return null;
        }

        using (stream)
        {
            var bytes = new byte[stream.Length];
            var offset = 0;
            while (offset < bytes.Length)
            {
                var read = stream.Read(bytes, offset, bytes.Length - offset);
                if (read <= 0)
                {
                    break;
                }

                offset += read;
            }

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                name = $"DrawableSuitsToolIcon_{iconKind}",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            if (!ImageConversion.LoadImage(texture, bytes, false))
            {
                UnityEngine.Object.Destroy(texture);
                DrawableSuitsDiagnostics.Warn($"ToolIconAssetFallback: icon={iconKind}; resource={resourceName}; bytes={bytes.Length}; reason=LoadImage failed");
                ToolIconTextureCache[iconKind] = null;
                return null;
            }

            ToolIconTextureCache[iconKind] = texture;
            DrawableSuitsDiagnostics.Info($"ToolIconAssetLoaded: icon={iconKind}; resource={resourceName}; texture={texture.width}x{texture.height}; bytes={bytes.Length}");
            return texture;
        }
    }

    private static string ToolIconFileName(ToolIconKind iconKind)
    {
        return iconKind switch
        {
            ToolIconKind.Paint => "paint.png",
            ToolIconKind.Erase => "erase.png",
            ToolIconKind.Fill => "fill.png",
            ToolIconKind.Decal => "decal.png",
            ToolIconKind.Text => "text.png",
            ToolIconKind.Eyedropper => "eyedropper.png",
            ToolIconKind.Mirror => "mirror.png",
            ToolIconKind.Sticker => "sticker.png",
            _ => iconKind.ToString().ToLowerInvariant() + ".png"
        };
    }

    private static Button CreateAnchoredButton(Transform parent, string text, Rect rect, Action onClick)
    {
        var go = CreateUiObject(text + "Button", parent, typeof(RectTransform), typeof(Image), typeof(Button));
        SetAnchoredRect(go.GetComponent<RectTransform>(), rect);

        var image = go.GetComponent<Image>();
        image.color = TerminalButtonColor;
        ApplyTerminalOutline(go, TerminalOutlineColor);

        var button = go.GetComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() =>
        {
            onClick?.Invoke();
            ClearSelectedNormalButton();
        });

        var colors = button.colors;
        colors.normalColor = image.color;
        colors.highlightedColor = colors.normalColor;
        colors.pressedColor = TerminalButtonPressedColor;
        colors.selectedColor = colors.normalColor;
        button.colors = colors;

        var label = CreateAnchoredText(go.transform, "Label", text, 15, FontStyle.Normal, TextAnchor.MiddleCenter, new Rect(0f, 0f, rect.width, rect.height), TerminalTextColor);
        var labelRect = label.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.pivot = new Vector2(0.5f, 0.5f);
        labelRect.anchoredPosition = Vector2.zero;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
        return button;
    }

    private void BuildRecentColorSwatches(Transform parent, Rect rect)
    {
        _recentColorButtons.Clear();
        _recentColorImages.Clear();
        const int columns = 6;
        const float size = 30f;
        const float rowGap = 8f;
        var columnGap = columns > 1 ? (rect.width - columns * size) / (columns - 1) : 0f;
        columnGap = Mathf.Clamp(columnGap, 8f, 20f);
        var totalWidth = columns * size + (columns - 1) * columnGap;
        var startX = rect.x + Mathf.Max(0f, (rect.width - totalWidth) * 0.5f);
        for (var i = 0; i < MaxRecentColors; i++)
        {
            var row = i / columns;
            var column = i % columns;
            var slot = i;
            var x = startX + column * (size + columnGap);
            var y = rect.y + row * (size + rowGap);
            var go = CreateUiObject($"RecentColor{slot + 1}", parent, typeof(RectTransform), typeof(Image), typeof(Button));
            SetAnchoredRect(go.GetComponent<RectTransform>(), new Rect(x, y, size, size));

            var image = go.GetComponent<Image>();
            image.color = TerminalInputColor;
            image.raycastTarget = true;

            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() =>
            {
                SelectRecentColor(slot);
                ClearSelectedNormalButton();
            });

            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.92f);
            colors.pressedColor = new Color(0.78f, 0.78f, 0.78f, 1f);
            colors.selectedColor = colors.normalColor;
            button.colors = colors;

            _recentColorButtons.Add(button);
            _recentColorImages.Add(image);
        }

        UpdateRecentColorSwatches();
    }

    private void BuildUndoHistoryPanel(Transform parent, Rect rect)
    {
        _undoHistoryRows.Clear();
        var panel = CreateUiObject("UndoHistoryPanel", parent, typeof(RectTransform), typeof(Image));
        SetAnchoredRect(panel.GetComponent<RectTransform>(), rect);
        var panelImage = panel.GetComponent<Image>();
        panelImage.color = TerminalCardColor;
        panelImage.raycastTarget = false;
        ApplyTerminalOutline(panel, TerminalOutlineColor);

        _undoHistoryEmptyLabel = CreateAnchoredText(panel.transform, "UndoHistoryEmpty", "No undo history", 13, FontStyle.Normal, TextAnchor.MiddleLeft, new Rect(8f, 6f, rect.width - 16f, 22f), TerminalMutedTextColor);

        const int rowCount = 4;
        const float rowHeight = 16f;
        const float rowSpacing = 2f;
        for (var i = 0; i < rowCount; i++)
        {
            var rowObject = CreateUiObject($"UndoHistoryRow{i + 1}", panel.transform, typeof(RectTransform), typeof(Image), typeof(Button));
            SetAnchoredRect(rowObject.GetComponent<RectTransform>(), new Rect(6f, 6f + i * (rowHeight + rowSpacing), rect.width - 12f, rowHeight));
            var rowImage = rowObject.GetComponent<Image>();
            rowImage.color = TerminalInputColor;
            rowImage.raycastTarget = true;
            var rowButton = rowObject.GetComponent<Button>();
            rowButton.targetGraphic = rowImage;
            rowButton.onClick.RemoveAllListeners();
            ApplyNormalListButtonStyle(rowButton);

            var label = CreateAnchoredText(rowObject.transform, "Label", string.Empty, 12, FontStyle.Normal, TextAnchor.MiddleLeft, new Rect(6f, 0f, rect.width - 24f, rowHeight), TerminalTextColor);
            _undoHistoryRows.Add(new UndoHistoryRow
            {
                GameObject = rowObject,
                Button = rowButton,
                Image = rowImage,
                Label = label,
                Index = -1
            });
        }

        _undoHistorySelectionLabel = CreateAnchoredText(panel.transform, "UndoHistorySelection", "Select a row first.", 10, FontStyle.Normal, TextAnchor.MiddleLeft, new Rect(8f, 76f, rect.width - 16f, 12f), TerminalMutedTextColor);
        _undoToSelectedButton = CreateAnchoredButton(panel.transform, "Undo Selected", new Rect(6f, 92f, 132f, 22f), UndoToSelectedHistory);
        _clearUndoHistoryButton = CreateAnchoredButton(panel.transform, "Clear History", new Rect(146f, 92f, rect.width - 152f, 22f), ClearUndoHistoryByUser);

        UpdateUndoHistoryUi();
    }

    private void BuildBrushShapeMenu(Transform parent, Rect rect)
    {
        _brushShapeOptionButtons.Clear();
        _brushShapeMenuObject = CreateUiObject("BrushShapeDropdown", parent, typeof(RectTransform), typeof(Image));
        SetAnchoredRect(_brushShapeMenuObject.GetComponent<RectTransform>(), rect);
        var image = _brushShapeMenuObject.GetComponent<Image>();
        image.color = TerminalDialogColor;
        image.raycastTarget = true;
        ApplyTerminalOutline(_brushShapeMenuObject, TerminalOutlineColor);

        var values = (BrushShape[])Enum.GetValues(typeof(BrushShape));
        for (var i = 0; i < values.Length; i++)
        {
            var shape = values[i];
            var row = CreateAnchoredButton(_brushShapeMenuObject.transform, BrushShapeDisplayName(shape), new Rect(6f, 6f + i * 28f, rect.width - 12f, 26f), () => SelectBrushShape(shape));
            _brushShapeOptionButtons.Add(row);
        }

        _brushShapeMenuObject.SetActive(false);
    }

    private void BuildStickerShapeMenu(Transform parent, Rect rect)
    {
        _stickerShapeOptionButtons.Clear();
        _stickerShapeMenuObject = CreateUiObject("StickerShapeDropdown", parent, typeof(RectTransform), typeof(Image));
        SetAnchoredRect(_stickerShapeMenuObject.GetComponent<RectTransform>(), rect);
        var image = _stickerShapeMenuObject.GetComponent<Image>();
        image.color = TerminalDialogColor;
        image.raycastTarget = true;
        ApplyTerminalOutline(_stickerShapeMenuObject, TerminalOutlineColor);

        var values = (StickerShape[])Enum.GetValues(typeof(StickerShape));
        const int columns = 3;
        const float gap = 6f;
        var rowHeight = 26f;
        var columnWidth = (rect.width - 12f - (columns - 1) * gap) / columns;
        for (var i = 0; i < values.Length; i++)
        {
            var shape = values[i];
            var column = i % columns;
            var rowIndex = i / columns;
            var buttonRect = new Rect(6f + column * (columnWidth + gap), 6f + rowIndex * (rowHeight + gap), columnWidth, rowHeight);
            var row = CreateAnchoredButton(_stickerShapeMenuObject.transform, StickerShapeShortName(shape), buttonRect, () => SelectStickerShape(shape));
            _stickerShapeOptionButtons.Add(row);
        }

        _stickerShapeMenuObject.SetActive(false);
    }

    private static void ClearSelectedNormalButton()
    {
        var eventSystem = EventSystem.current;
        var selected = eventSystem != null ? eventSystem.currentSelectedGameObject : null;
        if (selected != null && selected.GetComponent<InputField>() == null && selected.GetComponent<DrawableSliderControl>() == null && selected.GetComponent<DrawableColorPickerControl>() == null)
        {
            eventSystem.SetSelectedGameObject(null);
        }
    }

    private bool CanHandlePlacementRotationShortcuts()
    {
        return CanUsePlacementRotationShortcut()
            && !IsTypingInInputField()
            && !IsVirtualPointerEditingUi();
    }

    private bool IsTypingInInputField()
    {
        if (IsInputFocused(_designNameInput)
            || IsInputFocused(_textStampInput)
            || IsInputFocused(_colorHexInput)
            || IsInputFocused(_designCodeInput)
            || IsInputFocused(_virtualPointerInput))
        {
            return true;
        }

        var eventSystem = EventSystem.current ?? _editorEventSystem;
        var selected = eventSystem != null ? eventSystem.currentSelectedGameObject : null;
        if (selected == null)
        {
            return false;
        }

        return IsInputFocused(selected.GetComponent<InputField>())
            || IsInputFocused(selected.GetComponentInParent<InputField>());
    }

    private bool IsVirtualPointerEditingUi()
    {
        return _virtualPointerDown
            || _virtualPointerSlider != null
            || _virtualPointerColorPicker != null;
    }

    private static bool IsInputFocused(InputField input)
    {
        return input != null && input.isFocused;
    }

    private static Sprite GetSliderHandleDotSprite()
    {
        if (SliderHandleDotSprite != null)
        {
            return SliderHandleDotSprite;
        }

        const int size = 24;
        const float radius = 9.5f;
        const float feather = 1.5f;
        var center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
        var pixels = new Color32[size * size];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var distance = Vector2.Distance(new Vector2(x, y), center);
                var alpha = Mathf.Clamp01((radius + feather - distance) / Mathf.Max(0.001f, feather));
                pixels[(y * size) + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
            }
        }

        SliderHandleDotTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "DrawableSuitsSliderHandleDot",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };
        SliderHandleDotTexture.SetPixels32(pixels);
        SliderHandleDotTexture.Apply(false, false);
        SliderHandleDotSprite = Sprite.Create(SliderHandleDotTexture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
        SliderHandleDotSprite.name = "DrawableSuitsSliderHandleDotSprite";
        return SliderHandleDotSprite;
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
        backgroundImage.color = TerminalSliderTrackColor;
        backgroundImage.raycastTarget = true;

        var fill = CreateUiObject("Fill", go.transform, typeof(RectTransform), typeof(Image));
        var fillRect = fill.GetComponent<RectTransform>();
        fillRect.anchorMin = new Vector2(0f, 0.5f);
        fillRect.anchorMax = new Vector2(1f, 0.5f);
        fillRect.pivot = new Vector2(0f, 0.5f);
        fillRect.offsetMin = new Vector2(0f, -4f);
        fillRect.offsetMax = new Vector2(0f, 4f);
        var fillImage = fill.GetComponent<Image>();
        fillImage.color = TerminalSliderFillColor;
        fillImage.raycastTarget = false;

        var handle = CreateUiObject("Handle", go.transform, typeof(RectTransform), typeof(Image));
        var handleRect = handle.GetComponent<RectTransform>();
        handleRect.anchorMin = new Vector2(0f, 0.5f);
        handleRect.anchorMax = new Vector2(0f, 0.5f);
        handleRect.pivot = new Vector2(0.5f, 0.5f);
        handleRect.anchoredPosition = Vector2.zero;
        handleRect.sizeDelta = new Vector2(SliderHandleVisualSize, SliderHandleVisualSize);
        var handleImage = handle.GetComponent<Image>();
        handleImage.sprite = GetSliderHandleDotSprite();
        handleImage.type = Image.Type.Simple;
        handleImage.preserveAspect = true;
        handleImage.color = TerminalAccentHotColor;
        handleImage.raycastTarget = true;

        var slider = go.GetComponent<DrawableSliderControl>();
        slider.targetGraphic = handleImage;
        slider.Configure(bgRect, fillRect, handleRect, min, max, value, onValueChanged);
        DrawableSuitsDiagnostics.Info($"DrawableSliderBuilt name={name}; root={rect}; trackSize={bgRect.rect.size}; fillSize={fillRect.rect.size}; handleSize={handleRect.rect.size}; min={min}; max={max}; value={value}");
        return slider;
    }

    private static DrawableColorPickerControl CreateAnchoredColorPicker(Transform parent, Rect rect, Color initialColor, Action<Color> onColorChanged, out Image swatch, out InputField hexInput)
    {
        var go = CreateUiObject("ColorPicker", parent, typeof(RectTransform), typeof(Image), typeof(DrawableColorPickerControl));
        SetAnchoredRect(go.GetComponent<RectTransform>(), rect);
        var rootImage = go.GetComponent<Image>();
        rootImage.color = new Color(1f, 1f, 1f, 0.001f);
        rootImage.raycastTarget = true;

        var hueObject = CreateUiObject("HueRing", go.transform, typeof(RectTransform), typeof(RawImage));
        var hueRect = hueObject.GetComponent<RectTransform>();
        SetAnchoredRect(hueRect, new Rect(0f, 8f, 88f, 88f));
        var hueImage = hueObject.GetComponent<RawImage>();

        var svObject = CreateUiObject("SaturationValue", go.transform, typeof(RectTransform), typeof(RawImage));
        var svRect = svObject.GetComponent<RectTransform>();
        SetAnchoredRect(svRect, new Rect(100f, 8f, 88f, 88f));
        var svImage = svObject.GetComponent<RawImage>();

        var hueHandle = CreateColorPickerHandle(hueRect, "HueHandle", new Vector2(16f, 16f));
        var svHandle = CreateColorPickerHandle(svRect, "SaturationValueHandle", new Vector2(14f, 14f));

        swatch = CreateAnchoredColorSwatch(go.transform, new Rect(204f, 8f, 54f, 48f));
        hexInput = CreateAnchoredInputField(go.transform, "ColorHexInput", ColorToHex(initialColor), new Rect(192f, 64f, 82f, 30f));
        hexInput.characterLimit = 7;
        hexInput.contentType = InputField.ContentType.Standard;
        hexInput.textComponent.alignment = TextAnchor.MiddleCenter;
        hexInput.textComponent.fontStyle = FontStyle.Bold;

        var picker = go.GetComponent<DrawableColorPickerControl>();
        picker.targetGraphic = rootImage;
        picker.Configure(hueRect, svRect, hueHandle, svHandle, hueImage, svImage, swatch, initialColor, onColorChanged);

        var colors = picker.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = Color.white;
        colors.pressedColor = Color.white;
        colors.selectedColor = Color.white;
        picker.colors = colors;

        DrawableSuitsDiagnostics.Info($"DrawableColorPickerBuilt root={rect}; hueRect={hueRect.rect}; svRect={svRect.rect}; initialColor={initialColor}");
        return picker;
    }

    private static RectTransform CreateColorPickerHandle(Transform parent, string name, Vector2 size)
    {
        var handle = CreateUiObject(name, parent, typeof(RectTransform)).GetComponent<RectTransform>();
        handle.anchorMin = new Vector2(0.5f, 0.5f);
        handle.anchorMax = new Vector2(0.5f, 0.5f);
        handle.pivot = new Vector2(0.5f, 0.5f);
        handle.sizeDelta = size;

        CreateHandleLine(handle, "Top", new Vector2(0f, size.y * 0.5f), new Vector2(size.x, 2f));
        CreateHandleLine(handle, "Bottom", new Vector2(0f, -size.y * 0.5f), new Vector2(size.x, 2f));
        CreateHandleLine(handle, "Left", new Vector2(-size.x * 0.5f, 0f), new Vector2(2f, size.y));
        CreateHandleLine(handle, "Right", new Vector2(size.x * 0.5f, 0f), new Vector2(2f, size.y));
        return handle;
    }

    private static void CreateHandleLine(RectTransform parent, string name, Vector2 position, Vector2 size)
    {
        var line = CreateUiObject(name, parent, typeof(RectTransform), typeof(Image));
        var lineRect = line.GetComponent<RectTransform>();
        lineRect.anchorMin = new Vector2(0.5f, 0.5f);
        lineRect.anchorMax = new Vector2(0.5f, 0.5f);
        lineRect.pivot = new Vector2(0.5f, 0.5f);
        lineRect.anchoredPosition = position;
        lineRect.sizeDelta = size;
        var image = line.GetComponent<Image>();
        image.color = Color.white;
        image.raycastTarget = false;
    }

    private static Image CreateAnchoredColorSwatch(Transform parent, Rect rect)
    {
        var go = CreateUiObject("ColorSwatch", parent, typeof(RectTransform), typeof(Image));
        SetAnchoredRect(go.GetComponent<RectTransform>(), rect);
        return go.GetComponent<Image>();
    }

    private static InputField CreateAnchoredInputField(Transform parent, string value, Rect rect)
    {
        return CreateAnchoredInputField(parent, "DesignNameInput", value, rect);
    }

    private static InputField CreateAnchoredInputField(Transform parent, string name, string value, Rect rect)
    {
        var go = CreateUiObject(name, parent, typeof(RectTransform), typeof(Image), typeof(InputField));
        SetAnchoredRect(go.GetComponent<RectTransform>(), rect);
        go.GetComponent<Image>().color = TerminalInputColor;
        ApplyTerminalOutline(go, TerminalOutlineColor);

        var textObject = CreateUiObject("Text", go.transform, typeof(RectTransform), typeof(Text));
        var textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(8f, 2f);
        textRect.offsetMax = new Vector2(-8f, -2f);

        var text = textObject.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        text.fontSize = 16;
        text.color = TerminalTextColor;
        text.alignment = TextAnchor.MiddleLeft;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.raycastTarget = false;

        var input = go.GetComponent<InputField>();
        input.textComponent = text;
        input.text = value;
        input.lineType = InputField.LineType.SingleLine;
        input.caretColor = TerminalTextColor;
        input.selectionColor = new Color(TerminalAccentHotColor.r, TerminalAccentHotColor.g, TerminalAccentHotColor.b, 0.45f);
        return input;
    }

    private static RectTransform CreateAnchoredScrollList(Transform parent, string name, Rect rect)
    {
        var root = CreateUiObject(name, parent, typeof(RectTransform), typeof(Image));
        var rootRect = root.GetComponent<RectTransform>();
        SetAnchoredRect(rootRect, rect);
        var image = root.GetComponent<Image>();
        image.color = TerminalCardColor;
        image.raycastTarget = true;
        ApplyTerminalOutline(root, TerminalOutlineColor);
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
        _gamepadClickArmed = false;
        _virtualPointerDown = false;
        _virtualPointerPressTarget = null;
        _virtualPointerSlider = null;
        _virtualPointerColorPicker = null;
        _virtualPointerButton = null;
        _virtualPointerInput = null;
        _pointerSource = "Mouse";
        _cursor = _mousePositionAvailable ? _lastMousePosition : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        DrawableSuitsDiagnostics.Info($"Pointer initialized for editor open. source={source}; pointerSource={_pointerSource}; gamepadClickArmed={_gamepadClickArmed}; mouseAvailable={_mousePositionAvailable}; mouse={_lastMousePosition}; gamepadStick={_lastGamepadStick}; cursor={_cursor}; cursorVisible={Cursor.visible}; cursorLock={Cursor.lockState}");
    }

    private void EnsureCursorUnlockedWhileOpen()
    {
        if (!_isOpen)
        {
            return;
        }

        if (Cursor.visible || Cursor.lockState != CursorLockMode.None)
        {
            DrawableSuitsDiagnostics.Warn($"Editor native cursor state changed while open; restoring hidden canvas-cursor mode. visibleBefore={Cursor.visible}; lockBefore={Cursor.lockState}; pointerSource={_pointerSource}");
            Cursor.visible = false;
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

        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.None;
        DrawableSuitsDiagnostics.Info($"Cursor captured for editor canvas cursor. previousVisible={_previousCursorVisible}; previousLock={_previousCursorLockState}; currentVisible={Cursor.visible}; currentLock={Cursor.lockState}");
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
                : "Suit: current suit unavailable";
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
        if (_textStampInput != null && _textStampInput.text != _textStampValue)
        {
            _textStampInput.SetTextWithoutNotify(_textStampValue);
        }

        if (_brushSizeSlider != null) _brushSizeSlider.SetValue(_brushSize, false);
        if (_brushOpacitySlider != null) _brushOpacitySlider.SetValue(_brushOpacity, false);
        if (_fillToleranceSlider != null) _fillToleranceSlider.SetValue(_fillTolerance, false);
        if (_colorPicker != null) _colorPicker.SetColor(_brushColor, false);
        if (_decalSizeSlider != null) _decalSizeSlider.SetValue(CurrentPlacementSize(), false);
        if (_decalRotationSlider != null) _decalRotationSlider.SetValue(CurrentPlacementRotation(), false);

        if (_usingTexturePreview)
        {
            UseTexturePreview("UpdateUiState", false);
        }
        else
        {
            RefreshTexturePanelPreview("UpdateUiState", false);
        }

        var hasEditableTexture = _selectedSuitId >= 0 && DrawableSuitsPlugin.Registry.GetEditableTexture(_selectedSuitId) != null;
        SetInteractable(_paintButton, _canPaint);
        SetInteractable(_eraseButton, _canPaint);
        SetInteractable(_fillButton, _canPaint && hasEditableTexture);
        SetInteractable(_decalButton, _canPaint);
        SetInteractable(_eyedropperButton, _canPaint && hasEditableTexture);
        SetInteractable(_textButton, _canPaint && hasEditableTexture);
        SetInteractable(_stickerButton, _canPaint && hasEditableTexture);
        SetInteractable(_mirrorButton, _canPaint && hasEditableTexture);
        SetInteractable(_brushShapeButton, _canPaint && hasEditableTexture);
        SetInteractable(_stickerShapeButton, _canPaint && hasEditableTexture);
        SetInteractable(_decalsMenuButton, _canPaint && hasEditableTexture);
        SetInteractable(_stickersMenuButton, _canPaint && hasEditableTexture);
        SetInteractable(_editDecalButton, _canPaint && hasEditableTexture && _loadedDecal != null);
        SetInteractable(_editStickerButton, _canPaint && hasEditableTexture);
        SetInteractable(_applyButton, _canPaint && hasEditableTexture);
        SetInteractable(_saveButton, hasEditableTexture);
        SetInteractable(_exportCodeButton, hasEditableTexture);
        SetInteractable(_importCodeButton, hasEditableTexture);
        SetInteractable(_resetButton, hasEditableTexture);
        SetInteractable(_savedDesignsButton, hasEditableTexture);
        SetInteractable(_loadSelectedDesignButton, hasEditableTexture && _selectedDesignIndex >= 0);
        UpdateSavedDesignDeleteButton();
        UpdateDecalDeleteButton();
        UpdateAddDecalButton();
        if (_uvFallbackButton != null)
        {
            var showFallbackButton = _uvFallbackMode || !IsWorldThirdPersonMode;
            _uvFallbackButton.gameObject.SetActive(showFallbackButton);
            SetButtonLabel(_uvFallbackButton, _uvFallbackMode ? "Use Third Person" : "UV Panel Active");
        }
        if ((!_canPaint || !hasEditableTexture) && _brushShapeMenuObject != null && _brushShapeMenuObject.activeSelf)
        {
            _brushShapeMenuObject.SetActive(false);
        }
        if ((!_canPaint || !hasEditableTexture) && _stickerShapeMenuObject != null && _stickerShapeMenuObject.activeSelf)
        {
            _stickerShapeMenuObject.SetActive(false);
        }

        UpdateToolButtons();
        UpdateLabels();
        UpdateColorUi();
        UpdateRecentColorSwatches();
        UpdateUndoHistoryUi();
        RefreshPlacementEditPanelUi();
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
        var fillActive = _tool == EditorTool.FillBucket;
        var stickerActive = _tool == EditorTool.Sticker;
        var textActive = _tool == EditorTool.Text;
        var decalControlsActive = !textActive && !stickerActive;
        var pixelBrush = _brushShape == BrushShape.Pixel;
        if (fillActive)
        {
            HideBrushShapeMenu();
        }
        if (!stickerActive)
        {
            HideStickerShapeMenu();
        }
        if (_brushShapeLabel != null)
        {
            _brushShapeLabel.gameObject.SetActive(!fillActive);
        }
        if (_brushShapeButton != null)
        {
            _brushShapeButton.gameObject.SetActive(!fillActive);
        }
        if (_brushSizeLabel != null)
        {
            _brushSizeLabel.text = pixelBrush ? "Size: 1 px" : $"Size: {Mathf.RoundToInt(_brushSize)} px";
            _brushSizeLabel.gameObject.SetActive(!fillActive);
        }
        if (_brushSizeSlider != null)
        {
            _brushSizeSlider.gameObject.SetActive(!fillActive && !pixelBrush);
        }
        if (_brushOpacityLabel != null) _brushOpacityLabel.text = $"Opacity: {Mathf.RoundToInt(_brushOpacity * 100f)}%";
        if (_fillToleranceLabel != null)
        {
            _fillToleranceLabel.text = $"Tolerance: {Mathf.RoundToInt(_fillTolerance * 100f)}%";
            _fillToleranceLabel.gameObject.SetActive(fillActive);
        }
        if (_fillToleranceSlider != null)
        {
            _fillToleranceSlider.gameObject.SetActive(fillActive);
        }
        if (_placementHeaderLabel != null) _placementHeaderLabel.text = textActive ? "Text" : stickerActive ? "Sticker" : "Decal";
        if (_decalSizeLabel != null) _decalSizeLabel.text = textActive ? $"Height: {Mathf.RoundToInt(_textSize)} px" : stickerActive ? $"Size: {Mathf.RoundToInt(_stickerSize)} px" : $"Size: {Mathf.RoundToInt(_decalSize)} px";
        if (_decalRotationLabel != null) _decalRotationLabel.text = $"Rotation: {Mathf.RoundToInt(CurrentPlacementRotation())} deg";
        if (_textStampInput != null)
        {
            _textStampInput.gameObject.SetActive(textActive);
        }
        if (_stickerShapeButton != null)
        {
            _stickerShapeButton.gameObject.SetActive(stickerActive);
        }
        if (_stickersMenuButton != null)
        {
            _stickersMenuButton.gameObject.SetActive(stickerActive);
        }
        if (_editStickerButton != null)
        {
            _editStickerButton.gameObject.SetActive(stickerActive);
        }
        if (_selectedStickerShapeLabel != null)
        {
            _selectedStickerShapeLabel.gameObject.transform.parent.gameObject.SetActive(stickerActive);
            _selectedStickerShapeLabel.text = StickerShapeDisplayName(_stickerShape);
        }
        if (_decalsMenuButton != null)
        {
            _decalsMenuButton.gameObject.SetActive(decalControlsActive);
        }
        if (_editDecalButton != null)
        {
            _editDecalButton.gameObject.SetActive(decalControlsActive);
        }
        if (_selectedDecalLabel != null)
        {
            _selectedDecalLabel.gameObject.transform.parent.gameObject.SetActive(decalControlsActive);
            _selectedDecalLabel.text = _selectedDecalIndex >= 0 && _selectedDecalIndex < _decalFiles.Count
                ? MiddleEllipsize(Path.GetFileName(_decalFiles[_selectedDecalIndex]), 24)
                : "No decal selected";
        }
    }

    private float CurrentPlacementSize()
    {
        return _tool == EditorTool.Text ? _textSize : _tool == EditorTool.Sticker ? _stickerSize : _decalSize;
    }

    private float CurrentPlacementRotation()
    {
        return _tool == EditorTool.Text ? _textRotation : _tool == EditorTool.Sticker ? _stickerRotation : _decalRotation;
    }

    private bool CanUsePlacementRotationShortcut()
    {
        return (_tool == EditorTool.Decal && _loadedDecal != null) || _tool == EditorTool.Sticker;
    }

    private void ApplyPlacementRotationShortcut(float deltaDegrees, string source)
    {
        if (!CanUsePlacementRotationShortcut())
        {
            return;
        }

        var previous = CurrentPlacementRotation();
        var updated = NormalizePlacementRotation(previous + deltaDegrees);
        if (_tool == EditorTool.Sticker)
        {
            _stickerRotation = updated;
        }
        else
        {
            _decalRotation = updated;
        }

        if (_decalRotationSlider != null)
        {
            _decalRotationSlider.SetValue(updated, false);
        }

        UpdateLabels();
        InvalidateDecalPreview($"placement rotation shortcut {source}");
        var toolName = ToolDisplayName(_tool);
        SetStatus($"{toolName} rotation: {Mathf.RoundToInt(updated)} deg.", false);
        DrawableSuitsDiagnostics.Info($"PlacementRotationShortcutApplied: tool={_tool}; source={source}; previous={previous:0.##}; new={updated:0.##}; delta={deltaDegrees:0.##}; selected={CurrentPlacementName()}; mirror={_mirrorEnabled}; suit={_selectedSuitId}");
    }

    private static float NormalizePlacementRotation(float value)
    {
        while (value > 180f)
        {
            value -= 360f;
        }
        while (value < -180f)
        {
            value += 360f;
        }

        return Mathf.Clamp(value, -180f, 180f);
    }

    private void UpdateColorUi()
    {
        if (_colorSwatch != null)
        {
            _colorSwatch.color = _brushColor;
        }
        if (_colorHexInput != null && !_colorHexInput.isFocused)
        {
            _colorHexInput.SetTextWithoutNotify(ColorToHex(_brushColor));
        }

        UpdateBrushIndicator();
    }

    private void PreviewHexInput(string value)
    {
        var length = string.IsNullOrWhiteSpace(value) ? 0 : value.Trim().Length;
        if (length != 6 && length != 7)
        {
            return;
        }

        if (!TryParseHexColor(value, out var color))
        {
            return;
        }

        _brushColor = color;
        _colorPicker?.SetColor(_brushColor, false);
        if (_colorSwatch != null)
        {
            _colorSwatch.color = _brushColor;
        }
        UpdateBrushIndicator();
    }

    private void ApplyHexInput(string value)
    {
        if (TryParseHexColor(value, out var color))
        {
            _brushColor = color;
            _colorPicker?.SetColor(_brushColor, false);
            UpdateColorUi();
            DrawableSuitsDiagnostics.Info($"Applied brush color from hex input. input={value}; color={ColorToHex(_brushColor)}");
            return;
        }

        SetStatus("Invalid hex color", false);
        if (_colorHexInput != null)
        {
            _colorHexInput.SetTextWithoutNotify(ColorToHex(_brushColor));
        }
        DrawableSuitsDiagnostics.Warn($"Invalid brush color hex input ignored. input={value}");
    }

    private static string ColorToHex(Color color)
    {
        var r = Mathf.RoundToInt(Mathf.Clamp01(color.r) * 255f);
        var g = Mathf.RoundToInt(Mathf.Clamp01(color.g) * 255f);
        var b = Mathf.RoundToInt(Mathf.Clamp01(color.b) * 255f);
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    private static bool TryParseHexColor(string value, out Color color)
    {
        color = Color.white;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim();
        if (normalized.StartsWith("#", StringComparison.Ordinal))
        {
            normalized = normalized.Substring(1);
        }

        if (normalized.Length != 6 || !int.TryParse(normalized, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            return false;
        }

        color = new Color(
            ((rgb >> 16) & 0xFF) / 255f,
            ((rgb >> 8) & 0xFF) / 255f,
            (rgb & 0xFF) / 255f,
            1f);
        return true;
    }

    private void LoadRecentColors()
    {
        _recentColors.Clear();
        var ignored = 0;
        var raw = DrawableSuitsPlugin.ModConfig?.RecentColors?.Value;
        if (!string.IsNullOrWhiteSpace(raw))
        {
            var entries = raw.Split(new[] { ',', ';', ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < entries.Length && _recentColors.Count < MaxRecentColors; i++)
            {
                if (TryNormalizeHexColor(entries[i], out var normalized))
                {
                    if (!_recentColors.Contains(normalized))
                    {
                        _recentColors.Add(normalized);
                    }
                    continue;
                }

                ignored++;
            }
        }

        DrawableSuitsDiagnostics.Info($"RecentColorsLoaded: count={_recentColors.Count}; ignoredInvalid={ignored}; rawLength={(raw ?? string.Empty).Length}");
    }

    private static bool TryNormalizeHexColor(string value, out string normalized)
    {
        normalized = string.Empty;
        if (!TryParseHexColor(value, out var color))
        {
            return false;
        }

        normalized = ColorToHex(color);
        return true;
    }

    private void SaveRecentColors()
    {
        if (DrawableSuitsPlugin.ModConfig?.RecentColors == null)
        {
            return;
        }

        DrawableSuitsPlugin.ModConfig.RecentColors.Value = string.Join(",", _recentColors.ToArray());
    }

    private void AddRecentColorFromBrush(string sourceAction)
    {
        if (!TryNormalizeHexColor(ColorToHex(_brushColor), out var normalized))
        {
            return;
        }

        var existingIndex = _recentColors.IndexOf(normalized);
        var duplicate = existingIndex >= 0;
        if (existingIndex == 0)
        {
            return;
        }

        if (duplicate)
        {
            _recentColors.RemoveAt(existingIndex);
        }

        _recentColors.Insert(0, normalized);
        while (_recentColors.Count > MaxRecentColors)
        {
            _recentColors.RemoveAt(_recentColors.Count - 1);
        }

        SaveRecentColors();
        UpdateRecentColorSwatches();
        DrawableSuitsDiagnostics.Info($"RecentColorAdded: color={normalized}; source={sourceAction}; duplicate={duplicate}; slotCount={_recentColors.Count}");
    }

    private void SelectRecentColor(int index)
    {
        if (index < 0 || index >= _recentColors.Count)
        {
            return;
        }

        var hex = _recentColors[index];
        if (!TryParseHexColor(hex, out var color))
        {
            DrawableSuitsDiagnostics.Warn($"RecentColorSelected ignored invalid stored color. index={index}; color={hex}");
            return;
        }

        _brushColor = color;
        _colorPicker?.SetColor(_brushColor, false);
        UpdateColorUi();
        SetStatus($"Selected recent color {hex}.", false);
        DrawableSuitsDiagnostics.Info($"RecentColorSelected: color={hex}; index={index}; slotCount={_recentColors.Count}");
    }

    private void UpdateRecentColorSwatches()
    {
        for (var i = 0; i < _recentColorButtons.Count; i++)
        {
            var button = _recentColorButtons[i];
            var image = i < _recentColorImages.Count ? _recentColorImages[i] : null;
            if (button == null || image == null)
            {
                continue;
            }

            var color = Color.white;
            var hasColor = i < _recentColors.Count && TryParseHexColor(_recentColors[i], out color);
            button.interactable = hasColor;
            image.color = hasColor ? color : TerminalInputColor;

            var colors = button.colors;
            colors.normalColor = image.color;
            colors.highlightedColor = hasColor ? Color.Lerp(image.color, Color.white, 0.28f) : image.color;
            colors.pressedColor = hasColor ? Color.Lerp(image.color, Color.black, 0.2f) : image.color;
            colors.selectedColor = colors.normalColor;
            button.colors = colors;
        }

        if (_recentColorsLabel != null)
        {
            _recentColorsLabel.gameObject.SetActive(true);
        }
    }

    private void UpdateToolButtons()
    {
        SetToolButtonColor(_paintButton, _tool == EditorTool.Paint);
        SetToolButtonColor(_eraseButton, _tool == EditorTool.Erase);
        SetToolButtonColor(_fillButton, _tool == EditorTool.FillBucket);
        SetToolButtonColor(_decalButton, _tool == EditorTool.Decal);
        SetToolButtonColor(_eyedropperButton, _tool == EditorTool.Eyedropper);
        SetToolButtonColor(_textButton, _tool == EditorTool.Text);
        SetToolButtonColor(_stickerButton, _tool == EditorTool.Sticker);
        SetToolButtonColor(_mirrorButton, _mirrorEnabled);
        if (_activeToolLabel != null)
        {
            _activeToolLabel.text = _mirrorEnabled
                ? $"Active: {ToolDisplayName(_tool)} | Mirror"
                : $"Active: {ToolDisplayName(_tool)}";
        }
        UpdateBrushShapeButton();
        UpdateStickerShapeButton();
    }

    private void UpdateBrushShapeButton()
    {
        if (_brushShapeButton != null)
        {
            SetButtonLabel(_brushShapeButton, BrushShapeDisplayName(_brushShape));
        }

        for (var i = 0; i < _brushShapeOptionButtons.Count; i++)
        {
            var button = _brushShapeOptionButtons[i];
            if (button == null)
            {
                continue;
            }

            var values = (BrushShape[])Enum.GetValues(typeof(BrushShape));
            var selected = i < values.Length && values[i] == _brushShape;
            SetToolButtonColor(button, selected);
        }
    }

    private static void SetToolButtonColor(Button button, bool selected)
    {
        if (button == null)
        {
            return;
        }

        var image = button.GetComponent<Image>();
        var normal = button.interactable ? TerminalButtonColor : new Color(0.04f, 0.02f, 0.02f, 0.72f);
        var selectedColor = button.interactable ? TerminalAccentColor : new Color(0.16f, 0.035f, 0.03f, 0.72f);
        if (image != null)
        {
            image.color = selected ? selectedColor : normal;
        }

        var colors = button.colors;
        colors.normalColor = selected ? selectedColor : normal;
        colors.highlightedColor = selected ? TerminalAccentHotColor : normal;
        colors.selectedColor = colors.normalColor;
        colors.pressedColor = TerminalButtonPressedColor;
        button.colors = colors;

        var iconColor = button.interactable
            ? (selected ? TerminalTextColor : new Color(0.88f, 0.34f, 0.24f, 1f))
            : new Color(0.38f, 0.28f, 0.25f, 0.9f);
        var iconTransform = button.transform.Find("Icon");
        var iconImage = iconTransform != null ? iconTransform.GetComponent<RawImage>() : null;
        if (iconImage != null)
        {
            iconImage.color = iconColor;
        }

        var icon = iconTransform != null ? iconTransform.GetComponent<DrawableToolIconGraphic>() : null;
        if (icon != null)
        {
            icon.SetIconColor(iconColor);
        }
    }

    private static string ToolDisplayName(EditorTool tool)
    {
        return tool switch
        {
            EditorTool.Paint => "Paint",
            EditorTool.Erase => "Erase",
            EditorTool.FillBucket => "Fill",
            EditorTool.Decal => "Decal",
            EditorTool.Eyedropper => "Eyedropper",
            EditorTool.Text => "Text",
            EditorTool.Sticker => "Sticker",
            _ => tool.ToString()
        };
    }

    private void SetTool(EditorTool tool)
    {
        HideBrushShapeMenu();
        if (tool != EditorTool.Sticker)
        {
            HideStickerShapeMenu();
        }
        if (tool == EditorTool.Eyedropper)
        {
            _previousToolBeforeEyedropper = IsReturnableEyedropperTool(_tool) ? _tool : EditorTool.Paint;
        }

        if (tool == EditorTool.Decal && _loadedDecal == null)
        {
            WarnMissingDecal("tool selection");
            _tool = EditorTool.Paint;
            HideDecalPlacementPreview("decal tool missing decal", false);
            UpdateToolButtons();
            UpdateBrushIndicator();
            return;
        }

        _tool = tool;
        _decalStampArmed = true;
        _suppressPaintInputUntilRelease = false;
        _suppressDecalPreviewUntilRelease = false;
        if (tool == EditorTool.Decal || tool == EditorTool.Sticker)
        {
            InvalidateDecalPreview($"{tool} tool selected");
        }
        else
        {
            HideDecalPlacementPreview("tool changed", false);
        }
        if (tool == EditorTool.Text && string.IsNullOrWhiteSpace(_textStampValue))
        {
            SetStatus("Enter text before stamping.", false);
        }
        if (tool != EditorTool.Decal && _statusMessage.StartsWith("Select a decal", StringComparison.OrdinalIgnoreCase))
        {
            SetStatus(BuildReadinessStatus(), false);
        }
        if (tool == EditorTool.Eyedropper)
        {
            SetStatus("Eyedropper active. Aim at the suit to sample a color.", false);
        }
        if (tool == EditorTool.Sticker)
        {
            SetStatus($"Sticker active: {StickerShapeDisplayName(_stickerShape)}. Click/RT to stamp.", false);
        }
        if (tool == EditorTool.FillBucket)
        {
            SetStatus("Fill active. Click or RT a matching color region to fill.", false);
        }

        UpdateToolButtons();
        UpdatePlacementControlsForTool();
        UpdateLabels();
        UpdateBrushIndicator();
    }

    private static bool IsReturnableEyedropperTool(EditorTool tool)
    {
        return tool == EditorTool.Paint || tool == EditorTool.Erase || tool == EditorTool.FillBucket || tool == EditorTool.Decal || tool == EditorTool.Text || tool == EditorTool.Sticker;
    }

    private void UpdatePlacementControlsForTool()
    {
        if (_decalSizeSlider != null)
        {
            _decalSizeSlider.SetValue(CurrentPlacementSize(), false);
        }
        if (_decalRotationSlider != null)
        {
            _decalRotationSlider.SetValue(CurrentPlacementRotation(), false);
        }
        UpdateLabels();
    }

    private void ToggleMirror()
    {
        _mirrorEnabled = !_mirrorEnabled;
        _decalStampArmed = true;
        _suppressDecalPreviewUntilRelease = false;
        InvalidateDecalPreview("mirror toggled");
        if (!_mirrorEnabled && _uvMirrorDecalPreviewRect != null)
        {
            _uvMirrorDecalPreviewRect.gameObject.SetActive(false);
        }

        SetStatus(_mirrorEnabled
            ? "Mirror enabled. Edits duplicate on the opposite suit surface."
            : "Mirror disabled.", false);
        UpdateToolButtons();
        DrawableSuitsDiagnostics.Info($"Mirror painting toggled. enabled={_mirrorEnabled}; tool={_tool}; suit={_selectedSuitId}; previewMode={_previewMode}");
    }

    private void ToggleBrushShapeMenu()
    {
        if (_brushShapeMenuObject == null)
        {
            return;
        }

        var nextState = !_brushShapeMenuObject.activeSelf;
        _brushShapeMenuObject.SetActive(nextState);
        if (nextState)
        {
            _brushShapeMenuObject.transform.SetAsLastSibling();
            _canvasCursorObject?.transform.SetAsLastSibling();
        }
        DrawableSuitsDiagnostics.Info($"Brush shape dropdown toggled. open={nextState}; shape={_brushShape}; tool={_tool}; cursor={_cursor}");
    }

    private void HideBrushShapeMenu()
    {
        if (_brushShapeMenuObject != null && _brushShapeMenuObject.activeSelf)
        {
            _brushShapeMenuObject.SetActive(false);
        }
    }

    private void SelectBrushShape(BrushShape shape)
    {
        _brushShape = shape;
        HideBrushShapeMenu();
        UpdateBrushShapeButton();
        UpdateLabels();
        UpdateCanvasCursor(true, "brush shape changed");
        DrawableSuitsDiagnostics.Info($"Brush shape selected. shape={_brushShape}; tool={_tool}; brushSize={_brushSize:0.#}; suit={_selectedSuitId}");
    }

    private void ToggleStickerShapeMenu()
    {
        OpenStickersPanel();
    }

    private void HideStickerShapeMenu()
    {
        if (_stickerShapeMenuObject != null && _stickerShapeMenuObject.activeSelf)
        {
            _stickerShapeMenuObject.SetActive(false);
        }
    }

    private void UpdateStickerShapeButton()
    {
        if (_stickerShapeButton != null)
        {
            SetButtonLabel(_stickerShapeButton, "Stickers");
        }
        if (_selectedStickerShapeLabel != null)
        {
            _selectedStickerShapeLabel.text = StickerShapeDisplayName(_stickerShape);
        }

        var values = (StickerShape[])Enum.GetValues(typeof(StickerShape));
        for (var i = 0; i < _stickerShapeOptionButtons.Count; i++)
        {
            var button = _stickerShapeOptionButtons[i];
            if (button == null)
            {
                continue;
            }

            var selected = i < values.Length && values[i] == _stickerShape;
            SetToolButtonColor(button, selected);
        }
        for (var i = 0; i < _stickerPanelShapeButtons.Count; i++)
        {
            var button = _stickerPanelShapeButtons[i];
            if (button == null)
            {
                continue;
            }

            var selected = i < values.Length && values[i] == _stickerShape;
            SetToolButtonColor(button, selected);
        }
    }

    private void SelectStickerShape(StickerShape shape)
    {
        var changed = _stickerShape != shape;
        _stickerShape = shape;
        HideStickerShapeMenu();
        CloseStickersPanel();
        if (changed)
        {
            ResetPlacementEdit(PlacementEditTarget.Sticker, "sticker shape changed", true);
        }
        UpdateStickerShapeButton();
        InvalidateDecalPreview("sticker shape changed");
        SetStatus($"Sticker shape: {StickerShapeDisplayName(_stickerShape)}.", false);
        DrawableSuitsDiagnostics.Info($"StickerShapeSelected: shape={_stickerShape}; display={StickerShapeDisplayName(_stickerShape)}; tool={_tool}; size={_stickerSize:0.#}; rotation={_stickerRotation:0.#}; suit={_selectedSuitId}");
    }

    private static string BrushShapeDisplayName(BrushShape shape)
    {
        return shape switch
        {
            BrushShape.Circle => "Circle",
            BrushShape.Square => "Square",
            BrushShape.Pixel => "Pixel",
            BrushShape.SprayPaint => "Spray Paint",
            BrushShape.SoftAirbrush => "Soft Airbrush",
            BrushShape.NoiseScatter => "Noise/Scatter",
            _ => shape.ToString()
        };
    }

    private static string StickerShapeDisplayName(StickerShape shape)
    {
        return shape switch
        {
            StickerShape.Circle => "Circle",
            StickerShape.Square => "Square",
            StickerShape.Triangle => "Triangle",
            StickerShape.Diamond => "Diamond",
            StickerShape.Star => "Star",
            StickerShape.Heart => "Heart",
            StickerShape.Arrow => "Arrow",
            StickerShape.LightningBolt => "Lightning Bolt",
            StickerShape.Plus => "Plus/Cross",
            StickerShape.Ring => "Ring",
            StickerShape.Crescent => "Crescent",
            StickerShape.Shield => "Shield",
            _ => shape.ToString()
        };
    }

    private static string StickerShapeShortName(StickerShape shape)
    {
        return shape switch
        {
            StickerShape.LightningBolt => "Bolt",
            StickerShape.Plus => "Plus",
            _ => StickerShapeDisplayName(shape)
        };
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
            UpdateAnchoredList(
                _designListContent,
                _designFiles,
                _selectedDesignIndex,
                ref _designListPage,
                "Design",
                _designRows,
                ref _designEmptyLabel,
                ref _designPrevPageButton,
                ref _designNextPageButton,
                ref _designPageLabel,
                index =>
            {
                CancelPendingDesignDelete("design selection changed");
                _selectedDesignIndex = index;
                RefreshListButtons();
                UpdateUiState();
                SetSavedDesignsStatus($"Selected {Path.GetFileNameWithoutExtension(_designFiles[index])}.");
                DrawableSuitsDiagnostics.Info($"Design row selected. index={index}; file={(_designFiles.Count > index ? _designFiles[index] : "missing")}");
            }, path => Path.GetFileNameWithoutExtension(path));
        }

        if (_decalListContent != null)
        {
            UpdateAnchoredList(
                _decalListContent,
                _decalFiles,
                _selectedDecalIndex,
                ref _decalListPage,
                "Decal",
                _decalRows,
                ref _decalEmptyLabel,
                ref _decalPrevPageButton,
                ref _decalNextPageButton,
                ref _decalPageLabel,
                SelectDecal,
                Path.GetFileName);
        }

        RebuildSelectableNavigation();
    }

    private void UpdateAnchoredList(
        RectTransform content,
        List<string> files,
        int selectedIndex,
        ref int page,
        string listName,
        List<AnchoredListRow> rowPool,
        ref Text emptyLabel,
        ref Button prevPageButton,
        ref Button nextPageButton,
        ref Text pageLabel,
        Action<int> onSelect,
        Func<string, string> labelSelector)
    {
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

        EnsureAnchoredListPool(content, listName, rowPool, rows, rowHeight, spacing);
        EnsureAnchoredListEmptyLabel(content, listName, ref emptyLabel);
        EnsureAnchoredListPager(content, listName, ref prevPageButton, ref nextPageButton, ref pageLabel);
        DisableLegacyListChildren(content, rowPool, emptyLabel, prevPageButton, nextPageButton, pageLabel);

        if (emptyLabel != null)
        {
            emptyLabel.text = listName == "Decal" ? "No decals found" : "No saved designs";
            emptyLabel.gameObject.SetActive(files.Count == 0);
            SetAnchoredRect(emptyLabel.GetComponent<RectTransform>(), new Rect(8f, 8f, rect.width - 16f, 26f));
        }

        for (var slot = 0; slot < rowPool.Count; slot++)
        {
            var row = rowPool[slot];
            var fileIndex = startIndex + slot;
            var active = fileIndex < endIndex;
            if (row?.Button == null)
            {
                continue;
            }

            row.Button.gameObject.SetActive(active);
            row.Index = active ? fileIndex : -1;
            row.Button.onClick.RemoveAllListeners();
            if (!active)
            {
                continue;
            }

            var index = fileIndex;
            var y = 6f + slot * (rowHeight + spacing);
            SetAnchoredRect(row.Button.GetComponent<RectTransform>(), new Rect(6f, y, rect.width - 12f, rowHeight));
            var label = labelSelector(files[index]);
            SetButtonLabel(row.Button, TruncateListLabel(label));
            row.Button.onClick.AddListener(() =>
            {
                onSelect(index);
                ClearSelectedNormalButton();
            });

            if (index == selectedIndex)
            {
                ApplySelectedListButtonStyle(row.Button);
            }
            else
            {
                ApplyNormalListButtonStyle(row.Button);
            }
        }

        var showPager = maxPage > 0;
        var currentPage = page;
        var currentMaxPage = maxPage;
        if (prevPageButton != null)
        {
            prevPageButton.gameObject.SetActive(showPager);
            SetAnchoredRect(prevPageButton.GetComponent<RectTransform>(), new Rect(6f, Mathf.Max(6f, rect.height - 32f), 42f, 26f));
            prevPageButton.onClick.RemoveAllListeners();
            prevPageButton.onClick.AddListener(() =>
            {
                CancelPendingListDelete(listName, "list page changed");
                SetListPage(listName, Mathf.Max(0, currentPage - 1));
                RefreshListButtons();
                ClearSelectedNormalButton();
            });
        }
        if (pageLabel != null)
        {
            pageLabel.gameObject.SetActive(showPager);
            pageLabel.text = $"{page + 1}/{maxPage + 1}";
            SetAnchoredRect(pageLabel.GetComponent<RectTransform>(), new Rect(54f, Mathf.Max(6f, rect.height - 32f), 64f, 26f));
        }
        if (nextPageButton != null)
        {
            nextPageButton.gameObject.SetActive(showPager);
            SetAnchoredRect(nextPageButton.GetComponent<RectTransform>(), new Rect(124f, Mathf.Max(6f, rect.height - 32f), 42f, 26f));
            nextPageButton.onClick.RemoveAllListeners();
            nextPageButton.onClick.AddListener(() =>
            {
                CancelPendingListDelete(listName, "list page changed");
                SetListPage(listName, Mathf.Min(currentMaxPage, currentPage + 1));
                RefreshListButtons();
                ClearSelectedNormalButton();
            });
        }

        DrawableSuitsDiagnostics.Info($"ListRowsUpdated name={listName}; fileCount={files.Count}; selected={selectedIndex}; page={page}; maxPage={maxPage}; visibleRows={rows}; pooledRows={rowPool.Count}; rect={rect}; childCount={content.childCount}");
    }

    private void EnsureAnchoredListPool(RectTransform content, string listName, List<AnchoredListRow> rowPool, int rows, float rowHeight, float spacing)
    {
        while (rowPool.Count < rows)
        {
            var slot = rowPool.Count;
            var y = 6f + slot * (rowHeight + spacing);
            var button = CreateAnchoredButton(content, $"{listName}Row", new Rect(6f, y, content.rect.width - 12f, rowHeight), null);
            button.name = $"{listName}RowSlot{slot}";
            button.onClick.RemoveAllListeners();
            ApplyNormalListButtonStyle(button);
            rowPool.Add(new AnchoredListRow
            {
                Button = button,
                Label = button.GetComponentInChildren<Text>(true),
                Image = button.GetComponent<Image>(),
                Index = -1
            });
        }

        for (var i = rows; i < rowPool.Count; i++)
        {
            if (rowPool[i]?.Button != null)
            {
                rowPool[i].Button.onClick.RemoveAllListeners();
                rowPool[i].Button.gameObject.SetActive(false);
                rowPool[i].Index = -1;
            }
        }
    }

    private void EnsureAnchoredListEmptyLabel(RectTransform content, string listName, ref Text emptyLabel)
    {
        if (emptyLabel != null)
        {
            return;
        }

        emptyLabel = CreateAnchoredText(content, $"{listName}Empty", string.Empty, 13, FontStyle.Normal, TextAnchor.MiddleLeft, new Rect(8f, 8f, content.rect.width - 16f, 26f), new Color(0.76f, 0.78f, 0.82f, 1f));
        emptyLabel.gameObject.SetActive(false);
    }

    private void EnsureAnchoredListPager(RectTransform content, string listName, ref Button prevPageButton, ref Button nextPageButton, ref Text pageLabel)
    {
        if (prevPageButton == null)
        {
            prevPageButton = CreateAnchoredButton(content, "<", new Rect(6f, content.rect.height - 32f, 42f, 26f), null);
            prevPageButton.name = $"{listName}PrevPageButton";
            prevPageButton.onClick.RemoveAllListeners();
            prevPageButton.gameObject.SetActive(false);
        }

        if (pageLabel == null)
        {
            pageLabel = CreateAnchoredText(content, $"{listName}Page", string.Empty, 12, FontStyle.Normal, TextAnchor.MiddleCenter, new Rect(54f, content.rect.height - 32f, 64f, 26f), TerminalTextColor);
            pageLabel.gameObject.SetActive(false);
        }

        if (nextPageButton == null)
        {
            nextPageButton = CreateAnchoredButton(content, ">", new Rect(124f, content.rect.height - 32f, 42f, 26f), null);
            nextPageButton.name = $"{listName}NextPageButton";
            nextPageButton.onClick.RemoveAllListeners();
            nextPageButton.gameObject.SetActive(false);
        }
    }

    private static void DisableLegacyListChildren(RectTransform content, List<AnchoredListRow> rowPool, Text emptyLabel, Button prevPageButton, Button nextPageButton, Text pageLabel)
    {
        for (var i = content.childCount - 1; i >= 0; i--)
        {
            var child = content.GetChild(i);
            var go = child != null ? child.gameObject : null;
            if (go == null || IsKnownListChild(go, rowPool, emptyLabel, prevPageButton, nextPageButton, pageLabel))
            {
                continue;
            }

            DisableRaycastsAndSelectables(go);
            go.SetActive(false);
            UnityEngine.Object.Destroy(go);
        }
    }

    private static bool IsKnownListChild(GameObject go, List<AnchoredListRow> rowPool, Text emptyLabel, Button prevPageButton, Button nextPageButton, Text pageLabel)
    {
        if (emptyLabel != null && go == emptyLabel.gameObject) return true;
        if (prevPageButton != null && go == prevPageButton.gameObject) return true;
        if (nextPageButton != null && go == nextPageButton.gameObject) return true;
        if (pageLabel != null && go == pageLabel.gameObject) return true;
        for (var i = 0; i < rowPool.Count; i++)
        {
            if (rowPool[i]?.Button != null && go == rowPool[i].Button.gameObject)
            {
                return true;
            }
        }

        return false;
    }

    private static void DisableRaycastsAndSelectables(GameObject go)
    {
        var selectables = go.GetComponentsInChildren<Selectable>(true);
        for (var i = 0; i < selectables.Length; i++)
        {
            if (selectables[i] != null)
            {
                selectables[i].interactable = false;
            }
        }

        var graphics = go.GetComponentsInChildren<Graphic>(true);
        for (var i = 0; i < graphics.Length; i++)
        {
            if (graphics[i] != null)
            {
                graphics[i].raycastTarget = false;
            }
        }
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
            image.color = TerminalAccentColor;
        }

        var colors = button.colors;
        colors.normalColor = TerminalAccentColor;
        colors.highlightedColor = TerminalAccentHotColor;
        colors.selectedColor = colors.highlightedColor;
        colors.pressedColor = TerminalButtonPressedColor;
        button.colors = colors;
    }

    private static void ApplyNormalListButtonStyle(Button button)
    {
        if (button == null)
        {
            return;
        }

        var normal = TerminalButtonColor;
        var image = button.GetComponent<Image>();
        if (image != null)
        {
            image.color = normal;
        }

        var colors = button.colors;
        colors.normalColor = normal;
        colors.highlightedColor = normal;
        colors.selectedColor = normal;
        colors.pressedColor = TerminalButtonPressedColor;
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

        ClearSelectedNormalButton();
    }

    private void DrawImmediateCursor()
    {
        if (!_isOpen)
        {
            LogImmediateCursorDrawSkipped("editor closed");
            return;
        }

        var mode = CursorVisualMode.Dot;
        var diameter = DotCursorFrontSize;
        var color = Color.white;
        var targetMode = "Navigation";
        var triangleIndex = -1;
        var uv = Vector2.zero;
        var fallbackReason = string.Empty;
        if (TryResolveBrushCursor(out var brushDiameter, out var brushColor, out targetMode, out triangleIndex, out uv, out fallbackReason))
        {
            mode = CursorVisualModeForBrushShape(_brushShape);
            diameter = ClampCursorDiameter(brushDiameter);
            color = brushColor;
        }

        var texture = mode == CursorVisualMode.BrushRing ? EnsureCursorRingTexture() : EnsureCursorDotTexture();
        if (texture == null)
        {
            LogImmediateCursorDrawSkipped($"missing texture mode={mode}");
            return;
        }

        var screenX = Mathf.Clamp(_cursor.x, 0f, Screen.width);
        var screenY = Mathf.Clamp(_cursor.y, 0f, Screen.height);
        var guiCenter = new Vector2(screenX, Screen.height - screenY);
        var frontSize = mode == CursorVisualMode.BrushRing ? diameter : DotCursorFrontSize;
        var backSize = mode == CursorVisualMode.BrushRing ? diameter + 6f : DotCursorBackSize;
        var frontRect = RectFromCenter(guiCenter, frontSize);
        var backRect = RectFromCenter(guiCenter, backSize);

        var oldDepth = GUI.depth;
        var oldColor = GUI.color;
        try
        {
            GUI.depth = int.MinValue;
            GUI.color = mode == CursorVisualMode.BrushRing
                ? new Color(0f, 0f, 0f, 0.95f)
                : new Color(0f, 0f, 0f, 0.85f);
            GUI.DrawTexture(backRect, texture, ScaleMode.StretchToFill, true);
            GUI.color = color;
            GUI.DrawTexture(frontRect, texture, ScaleMode.StretchToFill, true);
        }
        finally
        {
            GUI.color = oldColor;
            GUI.depth = oldDepth;
        }

        LogImmediateCursorUpdated(mode, targetMode, diameter, triangleIndex, uv, fallbackReason, frontRect, texture);
    }

    private void EnsureCanvasCursor()
    {
        if (_editorCanvasObject == null || _canvasRect == null)
        {
            return;
        }

        if (_canvasCursorObject != null && _canvasCursorRect != null && _canvasCursorGraphic != null)
        {
            return;
        }

        DestroyCanvasCursor("rebuild");

        _canvasCursorObject = CreateUiObject("DrawableSuitsCanvasCursor", _editorCanvasObject.transform, typeof(RectTransform), typeof(DrawableCanvasCursorGraphic));
        _canvasCursorRect = _canvasCursorObject.GetComponent<RectTransform>();
        _canvasCursorRect.anchorMin = new Vector2(0.5f, 0.5f);
        _canvasCursorRect.anchorMax = new Vector2(0.5f, 0.5f);
        _canvasCursorRect.pivot = new Vector2(0.5f, 0.5f);
        _canvasCursorRect.anchoredPosition = Vector2.zero;
        _canvasCursorRect.sizeDelta = new Vector2(DotCursorRootSize, DotCursorRootSize);

        _canvasCursorGraphic = _canvasCursorObject.GetComponent<DrawableCanvasCursorGraphic>();
        _canvasCursorGraphic.raycastTarget = false;
        _canvasCursorGraphic.SetVisual(CursorVisualMode.Dot, Color.white, DotCursorBackSize);
        _canvasCursorObject.SetActive(false);
        LogCanvasCursorBuilt("created");
    }

    private void UpdateCanvasCursor(bool force, string context)
    {
        if (!_isOpen)
        {
            HideCanvasCursor($"update while closed {context}", false);
            return;
        }

        if (_editorCanvasObject == null || _canvasRect == null)
        {
            LogCanvasCursorHidden($"missing editor canvas {context}", true);
            return;
        }

        EnsureCanvasCursor();
        if (_canvasCursorObject == null || _canvasCursorRect == null || _canvasCursorGraphic == null)
        {
            LogCanvasCursorHidden($"cursor build failed {context}", true);
            return;
        }

        if (Cursor.visible || Cursor.lockState != CursorLockMode.None)
        {
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.None;
        }

        var mode = CursorVisualMode.Dot;
        var diameter = DotCursorBackSize;
        var color = Color.white;
        var targetMode = "Navigation";
        var triangleIndex = -1;
        var uv = Vector2.zero;
        var fallbackReason = string.Empty;
        if (TryResolveBrushCursor(out var brushDiameter, out var brushColor, out targetMode, out triangleIndex, out uv, out fallbackReason))
        {
            mode = CursorVisualModeForBrushShape(_brushShape);
            diameter = ClampCursorDiameter(brushDiameter);
            color = brushColor;
        }

        var screenCursor = new Vector2(Mathf.Clamp(_cursor.x, 0f, Screen.width), Mathf.Clamp(_cursor.y, 0f, Screen.height));
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screenCursor, null, out var localPosition))
        {
            var rootRect = _canvasRect.rect;
            var scaleFactor = Mathf.Max(0.001f, _editorCanvasObject.GetComponent<Canvas>()?.scaleFactor ?? 1f);
            localPosition = new Vector2(screenCursor.x / scaleFactor + rootRect.xMin, screenCursor.y / scaleFactor + rootRect.yMin);
            fallbackReason = AppendFallbackReason(fallbackReason, "screen-to-canvas fallback");
        }

        var rootSize = IsBrushCursorMode(mode)
            ? Mathf.Clamp(diameter + 10f, 22f, 300f)
            : DotCursorRootSize;

        _canvasCursorObject.SetActive(true);
        _canvasCursorObject.transform.SetAsLastSibling();
        _canvasCursorRect.anchoredPosition = localPosition;
        _canvasCursorRect.sizeDelta = new Vector2(rootSize, rootSize);
        _canvasCursorGraphic.raycastTarget = false;
        _canvasCursorGraphic.SetVisual(mode, color, diameter);

        LogCanvasCursorUpdated(force, context, mode, targetMode, diameter, triangleIndex, uv, fallbackReason, screenCursor, localPosition, rootSize);
    }

    private static string AppendFallbackReason(string current, string addition)
    {
        if (string.IsNullOrWhiteSpace(addition))
        {
            return current ?? string.Empty;
        }

        return string.IsNullOrWhiteSpace(current) ? addition : current + "; " + addition;
    }

    private void UpdateNativeCursor(bool force, string context)
    {
        if (!_isOpen)
        {
            return;
        }

        var mode = CursorVisualMode.Dot;
        var diameter = DotCursorBackSize;
        var color = Color.white;
        var targetMode = "Navigation";
        var triangleIndex = -1;
        var uv = Vector2.zero;
        var fallbackReason = string.Empty;
        if (TryResolveBrushCursor(out var brushDiameter, out var brushColor, out targetMode, out triangleIndex, out uv, out fallbackReason))
        {
            mode = CursorVisualMode.BrushRing;
            diameter = ClampCursorDiameter(brushDiameter);
            color = brushColor;
        }

        var roundedDiameter = mode == CursorVisualMode.BrushRing
            ? Mathf.RoundToInt(diameter)
            : Mathf.RoundToInt(DotCursorBackSize);
        var textureSize = mode == CursorVisualMode.BrushRing
            ? Mathf.Clamp(NextPowerOfTwo(roundedDiameter + 12), 32, 512)
            : 32;
        var hotspot = new Vector2(textureSize * 0.5f, textureSize * 0.5f);
        var key = $"mode={mode}|tool={_tool}|target={targetMode}|diameter={roundedDiameter}|color={ColorToHex(color)}|size={textureSize}";

        if (force || !string.Equals(key, _nativeCursorKey, StringComparison.Ordinal) || _nativeCursorTexture == null)
        {
            SetNativeCursorTexture(mode, textureSize, roundedDiameter, color, hotspot, key, context, targetMode, triangleIndex, uv, fallbackReason);
        }

        if (!Cursor.visible || Cursor.lockState != CursorLockMode.None)
        {
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        WarpNativeCursorIfNeeded(context);
    }

    private void SetNativeCursorTexture(CursorVisualMode mode, int textureSize, int diameter, Color color, Vector2 hotspot, string key, string context, string targetMode, int triangleIndex, Vector2 uv, string fallbackReason)
    {
        try
        {
            if (_nativeCursorTexture != null)
            {
                Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
                Destroy(_nativeCursorTexture);
                _nativeCursorTexture = null;
            }

            _nativeCursorTexture = CreateNativeCursorTexture(mode, textureSize, diameter, color);
            Cursor.SetCursor(_nativeCursorTexture, hotspot, CursorMode.ForceSoftware);
            _nativeCursorKey = key;
            LogNativeCursorUpdated(mode, targetMode, diameter, triangleIndex, uv, fallbackReason, textureSize, hotspot, context);
        }
        catch (Exception ex)
        {
            LogNativeCursorSetFailed(key, context, ex);
        }
    }

    private static Texture2D CreateNativeCursorTexture(CursorVisualMode mode, int size, int diameter, Color color)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = mode == CursorVisualMode.BrushRing ? "DrawableSuitsNativeBrushCursor" : "DrawableSuitsNativeDotCursor",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        var pixels = new Color32[size * size];
        var center = (size - 1) * 0.5f;
        if (mode == CursorVisualMode.Dot)
        {
            WriteNativeDotCursorPixels(pixels, size, center);
        }
        else
        {
            WriteNativeBrushCursorPixels(pixels, size, center, diameter, color);
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        return texture;
    }

    private static void WriteNativeDotCursorPixels(Color32[] pixels, int size, float center)
    {
        var outer = 7.5f;
        var inner = 4.25f;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var distance = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                var blackAlpha = 1f - Mathf.SmoothStep(outer - 1f, outer + 1f, distance);
                var whiteAlpha = 1f - Mathf.SmoothStep(inner - 1f, inner + 1f, distance);
                var mixed = CompositeCursorPixel(new Color(0f, 0f, 0f, blackAlpha * 0.95f), new Color(1f, 1f, 1f, whiteAlpha));
                pixels[y * size + x] = mixed;
            }
        }
    }

    private static void WriteNativeBrushCursorPixels(Color32[] pixels, int size, float center, int diameter, Color color)
    {
        var radius = diameter * 0.5f;
        var backThickness = Mathf.Clamp(diameter * 0.14f, 4f, 10f);
        var frontThickness = Mathf.Clamp(diameter * 0.07f, 2f, 6f);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var distance = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                var blackAlpha = RingAlpha(distance, radius, backThickness) * 0.95f;
                var colorAlpha = RingAlpha(distance, radius, frontThickness) * Mathf.Clamp01(color.a);
                var mixed = CompositeCursorPixel(new Color(0f, 0f, 0f, blackAlpha), new Color(color.r, color.g, color.b, colorAlpha));
                pixels[y * size + x] = mixed;
            }
        }
    }

    private static float RingAlpha(float distance, float radius, float thickness)
    {
        var half = thickness * 0.5f;
        var inner = radius - half;
        var outer = radius + half;
        var outerAlpha = 1f - Mathf.SmoothStep(outer - 1f, outer + 1f, distance);
        var innerAlpha = Mathf.SmoothStep(inner - 1f, inner + 1f, distance);
        return Mathf.Clamp01(outerAlpha * innerAlpha);
    }

    private static Color32 CompositeCursorPixel(Color background, Color foreground)
    {
        var outAlpha = foreground.a + background.a * (1f - foreground.a);
        if (outAlpha <= 0.001f)
        {
            return new Color32(0, 0, 0, 0);
        }

        var r = (foreground.r * foreground.a + background.r * background.a * (1f - foreground.a)) / outAlpha;
        var g = (foreground.g * foreground.a + background.g * background.a * (1f - foreground.a)) / outAlpha;
        var b = (foreground.b * foreground.a + background.b * background.a * (1f - foreground.a)) / outAlpha;
        return new Color32(
            (byte)Mathf.RoundToInt(Mathf.Clamp01(r) * 255f),
            (byte)Mathf.RoundToInt(Mathf.Clamp01(g) * 255f),
            (byte)Mathf.RoundToInt(Mathf.Clamp01(b) * 255f),
            (byte)Mathf.RoundToInt(Mathf.Clamp01(outAlpha) * 255f));
    }

    private static int NextPowerOfTwo(int value)
    {
        var result = 1;
        while (result < value)
        {
            result <<= 1;
        }

        return result;
    }

    private void WarpNativeCursorIfNeeded(string context)
    {
        if (!string.Equals(_pointerSource, "Gamepad", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var mouse = Mouse.current;
        if (mouse == null)
        {
            return;
        }

        var target = new Vector2(Mathf.Clamp(_cursor.x, 0f, Screen.width), Mathf.Clamp(_cursor.y, 0f, Screen.height));
        try
        {
            mouse.WarpCursorPosition(target);
            _lastMousePosition = target;
            _mousePositionAvailable = true;
            _ignoreMouseInputUntilTime = Time.unscaledTime + 0.12f;
            LogNativeCursorWarped(target, context);
        }
        catch (Exception ex)
        {
            LogNativeCursorSetFailed($"warp|target={target}", context, ex);
        }
    }

    private void ResetNativeCursor(string context)
    {
        try
        {
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            if (!string.IsNullOrEmpty(_nativeCursorKey) || _nativeCursorTexture != null)
            {
                DrawableSuitsDiagnostics.Info($"NativeCursorReset: context={context}; previousKey={_nativeCursorKey}; previousTexture={DrawableSuitsPlugin.DescribeUnityObject(_nativeCursorTexture)}");
            }
        }
        catch (Exception ex)
        {
            LogNativeCursorSetFailed("reset", context, ex);
        }

        if (_nativeCursorTexture != null)
        {
            Destroy(_nativeCursorTexture);
            _nativeCursorTexture = null;
        }

        _nativeCursorKey = string.Empty;
    }

    private static Rect RectFromCenter(Vector2 center, float size)
    {
        return new Rect(center.x - size * 0.5f, center.y - size * 0.5f, size, size);
    }

    private void DestroyLegacyCursorCanvas(string context)
    {
        if (_cursorCanvasObject == null)
        {
            return;
        }

        DrawableSuitsDiagnostics.Info($"Legacy cursor canvas destroyed. context={context}; canvas={DrawableSuitsPlugin.DescribeUnityObject(_cursorCanvasObject)}");
        Destroy(_cursorCanvasObject);
        _cursorCanvasObject = null;
        _cursorCanvasRect = null;
        _cursorMarker = null;
        _cursorBackRect = null;
        _cursorFrontRect = null;
        _cursorBackImage = null;
        _cursorImage = null;
    }

    private void HideCanvasCursor(string context, bool forceLog)
    {
        var wasActive = _canvasCursorObject != null && _canvasCursorObject.activeSelf;
        if (_canvasCursorObject != null)
        {
            _canvasCursorObject.SetActive(false);
        }

        if (wasActive || forceLog)
        {
            LogCanvasCursorHidden(context, forceLog);
        }
    }

    private void DestroyCanvasCursor(string context)
    {
        if (_canvasCursorObject == null)
        {
            return;
        }

        DrawableSuitsDiagnostics.Info($"CanvasCursorHidden: context={context}; destroyed=True; object={DrawableSuitsPlugin.DescribeUnityObject(_canvasCursorObject)}");
        Destroy(_canvasCursorObject);
        _canvasCursorObject = null;
        _canvasCursorRect = null;
        _canvasCursorGraphic = null;
    }

    private void UpdateCursorMarker()
    {
        DestroyLegacyCursorCanvas("ignored legacy cursor update request");
    }

    private void ApplyDynamicCursorVisual()
    {
        var mode = CursorVisualMode.Dot;
        var diameter = 7f;
        var color = Color.white;
        var targetMode = "Navigation";
        var triangleIndex = -1;
        var uv = Vector2.zero;
        var fallbackReason = string.Empty;

        if (TryResolveBrushCursor(out var brushDiameter, out var brushColor, out targetMode, out triangleIndex, out uv, out fallbackReason))
        {
            mode = CursorVisualMode.BrushRing;
            diameter = brushDiameter;
            color = brushColor;
        }

        var frontSprite = mode == CursorVisualMode.BrushRing ? EnsureCursorRingSprite() : EnsureCursorDotSprite();
        var backSprite = frontSprite;
        if (_cursorImage.sprite != frontSprite)
        {
            _cursorImage.sprite = frontSprite;
        }
        if (_cursorBackImage.sprite != backSprite)
        {
            _cursorBackImage.sprite = backSprite;
        }

        _cursorBackImage.color = mode == CursorVisualMode.BrushRing
            ? new Color(0f, 0f, 0f, 0.95f)
            : new Color(0f, 0f, 0f, 0.85f);
        _cursorImage.color = color;
        _cursorBackImage.raycastTarget = false;
        _cursorImage.raycastTarget = false;
        var canvasDiameter = mode == CursorVisualMode.BrushRing
            ? ClampCursorDiameter(diameter)
            : 7f;
        if (mode == CursorVisualMode.BrushRing)
        {
            _cursorMarker.sizeDelta = new Vector2(canvasDiameter + 8f, canvasDiameter + 8f);
            _cursorBackRect.sizeDelta = new Vector2(canvasDiameter + 6f, canvasDiameter + 6f);
            _cursorFrontRect.sizeDelta = new Vector2(canvasDiameter, canvasDiameter);
        }
        else
        {
            _cursorMarker.sizeDelta = new Vector2(DotCursorRootSize, DotCursorRootSize);
            _cursorBackRect.sizeDelta = new Vector2(DotCursorBackSize, DotCursorBackSize);
            _cursorFrontRect.sizeDelta = new Vector2(DotCursorFrontSize, DotCursorFrontSize);
        }
        LogDynamicCursorUpdated(mode, targetMode, canvasDiameter, triangleIndex, uv, fallbackReason);
    }

    private bool TryResolveBrushCursor(out float diameter, out Color color, out string targetMode, out int triangleIndex, out Vector2 uv, out string fallbackReason)
    {
        diameter = 7f;
        color = Color.white;
        targetMode = "Navigation";
        triangleIndex = -1;
        uv = Vector2.zero;
        fallbackReason = string.Empty;

        if (_tool != EditorTool.Paint && _tool != EditorTool.Erase)
        {
            targetMode = _tool.ToString();
            return false;
        }

        color = _tool == EditorTool.Erase
            ? new Color(0.65f, 0.85f, 1f, 0.95f)
            : new Color(_brushColor.r, _brushColor.g, _brushColor.b, 0.95f);

        if (!_canPaint)
        {
            targetMode = "Invalid";
            fallbackReason = "canPaint false";
            return false;
        }

        if (IsEditorModalOpen())
        {
            targetMode = IsSavedDesignsPanelOpen() ? "SavedDesignsPanel" : "DesignCodePanel";
            return false;
        }

        var texture = _selectedSuitId >= 0 ? DrawableSuitsPlugin.Registry.GetEditableTexture(_selectedSuitId) : null;
        if (texture == null)
        {
            targetMode = "Invalid";
            fallbackReason = "no editable texture";
            return false;
        }

        if (IsWorldThirdPersonMode)
        {
            if (IsCursorOverPreviewViewport())
            {
                targetMode = "TexturePanel";
                if (!TryGetTexturePreviewUv(_cursor, out uv))
                {
                    fallbackReason = "texture panel uv miss";
                    return false;
                }

                diameter = ComputeUvFallbackBrushDiameter(texture);
                return true;
            }

            targetMode = "WorldThirdPerson";
            if (IsCursorOverEditorPanel())
            {
                targetMode = "EditorPanel";
                return false;
            }

            if (!TryGetWorldPaintHit(out var hit))
            {
                fallbackReason = "world miss";
                return false;
            }

            uv = hit.textureCoord;
            triangleIndex = hit.triangleIndex;
            if (!TryComputeWorldBrushCursorDiameter(hit, texture, out diameter, out fallbackReason))
            {
                diameter = EstimateWorldBrushCursorDiameter(hit, texture);
            }

            return true;
        }

        targetMode = "TextureFallback";
        if (!IsCursorOverPreviewViewport() || !TryGetTexturePreviewUv(_cursor, out uv))
        {
            fallbackReason = "uv miss";
            return false;
        }

        diameter = ComputeUvFallbackBrushDiameter(texture);
        return true;
    }

    private float ComputeUvFallbackBrushDiameter(Texture2D texture)
    {
        if (texture == null || _previewViewportRect == null)
        {
            return 18f;
        }

        var screenSize = GetRectTransformScreenSize(_previewViewportRect);
        if (screenSize.x <= 0f || screenSize.y <= 0f)
        {
            return 18f;
        }

        var view = GetUvPanelViewRect();
        var scaleX = screenSize.x / Mathf.Max(1f, texture.width * view.width);
        var scaleY = screenSize.y / Mathf.Max(1f, texture.height * view.height);
        return ScreenPixelsToCanvasUnits(EffectiveBrushRadiusPixels() * 2f * Mathf.Max(scaleX, scaleY));
    }

    private static Vector2 GetRectTransformScreenSize(RectTransform rectTransform)
    {
        if (rectTransform == null)
        {
            return Vector2.zero;
        }

        var corners = new Vector3[4];
        rectTransform.GetWorldCorners(corners);
        var bottomLeft = RectTransformUtility.WorldToScreenPoint(null, corners[0]);
        var topRight = RectTransformUtility.WorldToScreenPoint(null, corners[2]);
        return new Vector2(Mathf.Abs(topRight.x - bottomLeft.x), Mathf.Abs(topRight.y - bottomLeft.y));
    }

    private bool TryComputeWorldBrushCursorDiameter(RaycastHit hit, Texture2D texture, out float diameter, out string fallbackReason)
    {
        diameter = 0f;
        fallbackReason = string.Empty;
        if (_worldEditorCamera == null || _worldPaintProxyObject == null || _worldPaintMesh == null || texture == null)
        {
            fallbackReason = "world cursor dependencies missing";
            return false;
        }

        if (hit.triangleIndex < 0)
        {
            fallbackReason = $"invalid triangle {hit.triangleIndex}";
            return false;
        }

        var triangles = _worldPaintMesh.triangles;
        var vertices = _worldPaintMesh.vertices;
        var uvs = _worldPaintMesh.uv;
        var triangleOffset = hit.triangleIndex * 3;
        if (triangles == null || vertices == null || uvs == null
            || triangleOffset < 0 || triangleOffset + 2 >= triangles.Length)
        {
            fallbackReason = $"triangle array unavailable index={hit.triangleIndex}";
            return false;
        }

        var i0 = triangles[triangleOffset];
        var i1 = triangles[triangleOffset + 1];
        var i2 = triangles[triangleOffset + 2];
        if (i0 < 0 || i1 < 0 || i2 < 0
            || i0 >= vertices.Length || i1 >= vertices.Length || i2 >= vertices.Length
            || i0 >= uvs.Length || i1 >= uvs.Length || i2 >= uvs.Length)
        {
            fallbackReason = $"triangle vertex out of range index={hit.triangleIndex}";
            return false;
        }

        var v0 = vertices[i0];
        var v1 = vertices[i1];
        var v2 = vertices[i2];
        var uv0 = uvs[i0];
        var uv1 = uvs[i1];
        var uv2 = uvs[i2];
        var edge1 = v1 - v0;
        var edge2 = v2 - v0;
        var duv1 = uv1 - uv0;
        var duv2 = uv2 - uv0;
        var determinant = duv1.x * duv2.y - duv1.y * duv2.x;
        if (Mathf.Abs(determinant) < 0.000001f)
        {
            fallbackReason = $"degenerate uv triangle {hit.triangleIndex}";
            return false;
        }

        var inverse = 1f / determinant;
        var dPdu = (edge1 * duv2.y - edge2 * duv1.y) * inverse;
        var dPdv = (edge2 * duv1.x - edge1 * duv2.x) * inverse;
        var localCenter = _worldPaintProxyObject.transform.InverseTransformPoint(hit.point);
        var brushRadius = EffectiveBrushRadiusPixels();
        var du = brushRadius / Mathf.Max(1f, texture.width);
        var dv = brushRadius / Mathf.Max(1f, texture.height);
        var transform = _worldPaintProxyObject.transform;
        var uPlus = ProjectToScreen(transform.TransformPoint(localCenter + dPdu * du));
        var uMinus = ProjectToScreen(transform.TransformPoint(localCenter - dPdu * du));
        var vPlus = ProjectToScreen(transform.TransformPoint(localCenter + dPdv * dv));
        var vMinus = ProjectToScreen(transform.TransformPoint(localCenter - dPdv * dv));
        if (!uPlus.HasValue || !uMinus.HasValue || !vPlus.HasValue || !vMinus.HasValue)
        {
            fallbackReason = $"projection failed triangle={hit.triangleIndex}";
            return false;
        }

        var uDiameter = Vector2.Distance(uPlus.Value, uMinus.Value);
        var vDiameter = Vector2.Distance(vPlus.Value, vMinus.Value);
        var screenDiameter = Mathf.Max(uDiameter, vDiameter);
        if (float.IsNaN(screenDiameter) || float.IsInfinity(screenDiameter) || screenDiameter <= 0.5f)
        {
            fallbackReason = $"invalid projected diameter {screenDiameter:0.###}";
            return false;
        }

        diameter = ScreenPixelsToCanvasUnits(screenDiameter);
        return true;
    }

    private Vector2? ProjectToScreen(Vector3 worldPoint)
    {
        if (_worldEditorCamera == null)
        {
            return null;
        }

        var screenPoint = _worldEditorCamera.WorldToScreenPoint(worldPoint);
        if (screenPoint.z <= Mathf.Max(0.001f, _worldEditorCamera.nearClipPlane))
        {
            return null;
        }

        return new Vector2(screenPoint.x, screenPoint.y);
    }

    private float EstimateWorldBrushCursorDiameter(RaycastHit hit, Texture2D texture)
    {
        if (_worldEditorCamera == null || texture == null)
        {
            return ClampCursorDiameter(_brushSize * 1.25f);
        }

        var maxBounds = _worldPaintMesh != null
            ? Mathf.Max(0.05f, Mathf.Max(_worldPaintMesh.bounds.size.x, Mathf.Max(_worldPaintMesh.bounds.size.y, _worldPaintMesh.bounds.size.z)))
            : 1.8f;
        var worldDiameter = _brushSize * 2f * maxBounds / Mathf.Max(1, Mathf.Max(texture.width, texture.height));
        var axis = _worldEditorCamera.transform.right;
        var center = ProjectToScreen(hit.point);
        var edge = ProjectToScreen(hit.point + axis * (worldDiameter * 0.5f));
        if (!center.HasValue || !edge.HasValue)
        {
            return ClampCursorDiameter(_brushSize * 1.25f);
        }

        return ScreenPixelsToCanvasUnits(Vector2.Distance(center.Value, edge.Value) * 2f);
    }

    private float ScreenPixelsToCanvasUnits(float screenPixels)
    {
        var canvas = _editorCanvasObject != null ? _editorCanvasObject.GetComponent<Canvas>() : null;
        var scaleFactor = Mathf.Max(0.001f, canvas != null ? canvas.scaleFactor : 1f);
        return screenPixels / scaleFactor;
    }

    private static float ClampCursorDiameter(float diameter)
    {
        return Mathf.Clamp(diameter, 18f, 280f);
    }

    private static CursorVisualMode CursorVisualModeForBrushShape(BrushShape shape)
    {
        return shape switch
        {
            BrushShape.Square => CursorVisualMode.BrushSquare,
            BrushShape.Pixel => CursorVisualMode.BrushPixel,
            _ => CursorVisualMode.BrushRing
        };
    }

    private static bool IsBrushCursorMode(CursorVisualMode mode)
    {
        return mode == CursorVisualMode.BrushRing
            || mode == CursorVisualMode.BrushSquare
            || mode == CursorVisualMode.BrushPixel;
    }

    private bool IsDesignCodePanelOpen()
    {
        return _designCodePanelObject != null && _designCodePanelObject.activeInHierarchy;
    }

    private bool IsSavedDesignsPanelOpen()
    {
        return _savedDesignsPanelObject != null && _savedDesignsPanelObject.activeInHierarchy;
    }

    private bool IsDecalsPanelOpen()
    {
        return _decalsPanelObject != null && _decalsPanelObject.activeInHierarchy;
    }

    private bool IsStickersPanelOpen()
    {
        return _stickersPanelObject != null && _stickersPanelObject.activeInHierarchy;
    }

    private bool IsPlacementEditPanelOpen()
    {
        return _placementEditPanelObject != null && _placementEditPanelObject.activeInHierarchy;
    }

    private bool IsEditorModalOpen()
    {
        return IsDesignCodePanelOpen() || IsSavedDesignsPanelOpen() || IsDecalsPanelOpen() || IsStickersPanelOpen() || IsPlacementEditPanelOpen();
    }

    private string DescribeOpenEditorModal()
    {
        if (IsPlacementEditPanelOpen()) return "PlacementEdit";
        if (IsDecalsPanelOpen()) return "Decals";
        if (IsStickersPanelOpen()) return "Stickers";
        if (IsSavedDesignsPanelOpen()) return "SavedDesigns";
        if (IsDesignCodePanelOpen()) return "DesignCode";
        return "None";
    }

    private Texture2D EnsureCursorDotTexture()
    {
        if (_cursorDotTexture == null)
        {
            _cursorDotTexture = CreateCursorTexture("DrawableSuitsImmediateCursorDot", 32, true);
        }

        return _cursorDotTexture;
    }

    private Texture2D EnsureCursorRingTexture()
    {
        if (_cursorRingTexture == null)
        {
            _cursorRingTexture = CreateCursorTexture("DrawableSuitsImmediateCursorRing", 128, false);
        }

        return _cursorRingTexture;
    }

    private Sprite EnsureCursorDotSprite()
    {
        if (_cursorDotSprite != null)
        {
            return _cursorDotSprite;
        }

        _cursorDotTexture = EnsureCursorDotTexture();
        _cursorDotSprite = Sprite.Create(_cursorDotTexture, new Rect(0f, 0f, _cursorDotTexture.width, _cursorDotTexture.height), new Vector2(0.5f, 0.5f), 100f);
        _cursorDotSprite.name = "DrawableSuitsCursorDotSprite";
        return _cursorDotSprite;
    }

    private Sprite EnsureCursorRingSprite()
    {
        if (_cursorRingSprite != null)
        {
            return _cursorRingSprite;
        }

        _cursorRingTexture = EnsureCursorRingTexture();
        _cursorRingSprite = Sprite.Create(_cursorRingTexture, new Rect(0f, 0f, _cursorRingTexture.width, _cursorRingTexture.height), new Vector2(0.5f, 0.5f), 100f);
        _cursorRingSprite.name = "DrawableSuitsCursorRingSprite";
        return _cursorRingSprite;
    }

    private static Texture2D CreateCursorTexture(string name, int size, bool filled)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = name,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        var pixels = new Color32[size * size];
        var center = (size - 1) * 0.5f;
        var outer = size * 0.46f;
        var inner = filled ? 0f : size * 0.36f;
        var edge = Mathf.Max(1f, size * 0.035f);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var dx = x - center;
                var dy = y - center;
                var distance = Mathf.Sqrt(dx * dx + dy * dy);
                float alpha;
                if (filled)
                {
                    alpha = 1f - Mathf.SmoothStep(outer - edge, outer + edge, distance);
                }
                else
                {
                    var outerAlpha = 1f - Mathf.SmoothStep(outer - edge, outer + edge, distance);
                    var innerAlpha = Mathf.SmoothStep(inner - edge, inner + edge, distance);
                    alpha = outerAlpha * innerAlpha;
                }

                pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha) * 255f));
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        return texture;
    }

    private void DestroyCursorGraphics()
    {
        ResetNativeCursor("destroy cursor graphics");
        DestroyCanvasCursor("destroy cursor graphics");
        if (_cursorDotSprite != null)
        {
            Destroy(_cursorDotSprite);
            _cursorDotSprite = null;
        }
        if (_cursorRingSprite != null)
        {
            Destroy(_cursorRingSprite);
            _cursorRingSprite = null;
        }
        if (_cursorDotTexture != null)
        {
            Destroy(_cursorDotTexture);
            _cursorDotTexture = null;
        }
        if (_cursorRingTexture != null)
        {
            Destroy(_cursorRingTexture);
            _cursorRingTexture = null;
        }
    }

    private void LogCanvasCursorBuilt(string context)
    {
        DrawableSuitsDiagnostics.Info($"CanvasCursorBuilt: context={context}; object={DrawableSuitsPlugin.DescribeUnityObject(_canvasCursorObject)}; active={_canvasCursorObject?.activeSelf.ToString() ?? "null"}; root={DrawableSuitsPlugin.DescribeUnityObject(_editorCanvasObject)}; rootChildren={_editorCanvasObject?.transform.childCount.ToString() ?? "null"}; raycastTarget={_canvasCursorGraphic?.raycastTarget.ToString() ?? "null"}; rootRect={(_canvasRect != null ? _canvasRect.rect.ToString() : "null")}");
    }

    private void LogCanvasCursorUpdated(bool force, string context, CursorVisualMode mode, string targetMode, float diameter, int triangleIndex, Vector2 uv, string fallbackReason, Vector2 screenPosition, Vector2 localPosition, float rootSize)
    {
        var siblingIndex = _canvasCursorObject != null ? _canvasCursorObject.transform.GetSiblingIndex() : -1;
        var key = $"mode={mode}|tool={_tool}|shape={_brushShape}|source={_pointerSource}|target={targetMode}|brush={Mathf.RoundToInt(EffectiveBrushRadiusPixels())}|diameter={diameter:0.#}|tri={triangleIndex}|uv={uv.x:0.###},{uv.y:0.###}|fallback={fallbackReason}|screen={screenPosition.x:0.#},{screenPosition.y:0.#}|local={localPosition.x:0.#},{localPosition.y:0.#}|size={rootSize:0.#}|context={context}";
        if (!force && Time.unscaledTime - _lastCanvasCursorLogTime < 0.75f && string.Equals(key, _lastCanvasCursorLogKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastCanvasCursorLogTime = Time.unscaledTime;
        _lastCanvasCursorLogKey = key;
        DrawableSuitsDiagnostics.Info($"CanvasCursorUpdated: {key}; active={_canvasCursorObject?.activeSelf.ToString() ?? "null"}; activeInHierarchy={_canvasCursorObject?.activeInHierarchy.ToString() ?? "null"}; sibling={siblingIndex}; rootChildCount={_editorCanvasObject?.transform.childCount.ToString() ?? "null"}; rectSize={(_canvasCursorRect != null ? _canvasCursorRect.rect.size.ToString() : "null")}; anchored={(_canvasCursorRect != null ? _canvasCursorRect.anchoredPosition.ToString() : "null")}; raycastTarget={_canvasCursorGraphic?.raycastTarget.ToString() ?? "null"}; graphicMode={_canvasCursorGraphic?.Mode.ToString() ?? "null"}; graphicDiameter={_canvasCursorGraphic?.Diameter.ToString("0.#") ?? "null"}; canvasScale={_editorCanvasObject?.GetComponent<Canvas>()?.scaleFactor.ToString("0.###") ?? "null"}; rootRect={(_canvasRect != null ? _canvasRect.rect.ToString() : "null")}; nativeCursorVisible={Cursor.visible}; nativeCursorLock={Cursor.lockState}; overPanel={IsCursorOverEditorPanel()}; previewMode={_previewMode}");
    }

    private void LogCanvasCursorHidden(string context, bool force)
    {
        var key = context ?? "unknown";
        if (!force && Time.unscaledTime - _lastCanvasCursorHiddenTime < 1.5f && string.Equals(key, _lastCanvasCursorHiddenKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastCanvasCursorHiddenTime = Time.unscaledTime;
        _lastCanvasCursorHiddenKey = key;
        DrawableSuitsDiagnostics.Info($"CanvasCursorHidden: context={key}; object={DrawableSuitsPlugin.DescribeUnityObject(_canvasCursorObject)}; active={_canvasCursorObject?.activeSelf.ToString() ?? "null"}; isOpen={_isOpen}; canvasActive={CanvasActiveForDiagnostics}; nativeCursorVisible={Cursor.visible}; nativeCursorLock={Cursor.lockState}");
    }

    private void LogDynamicCursorUpdated(CursorVisualMode mode, string targetMode, float diameter, int triangleIndex, Vector2 uv, string fallbackReason)
    {
        var key = $"mode={mode}|tool={_tool}|source={_pointerSource}|target={targetMode}|brush={Mathf.RoundToInt(_brushSize)}|diameter={diameter:0.#}|tri={triangleIndex}|uv={uv.x:0.###},{uv.y:0.###}|fallback={fallbackReason}";
        if (Time.unscaledTime - _lastDynamicCursorLogTime < 0.75f && string.Equals(key, _lastDynamicCursorLogKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastDynamicCursorLogTime = Time.unscaledTime;
        _lastDynamicCursorLogKey = key;
        DrawableSuitsDiagnostics.Info($"DynamicCursorUpdated: {key}; canPaint={_canPaint}; overPanel={IsCursorOverEditorPanel()}; overPreview={IsCursorOverPreviewViewport()}; screenCursor={_cursor}; anchored={(_cursorMarker != null ? _cursorMarker.anchoredPosition.ToString() : "null")}; rootSize={(_cursorMarker != null ? _cursorMarker.sizeDelta.ToString() : "null")}; frontSize={(_cursorFrontRect != null ? _cursorFrontRect.sizeDelta.ToString() : "null")}; backSize={(_cursorBackRect != null ? _cursorBackRect.sizeDelta.ToString() : "null")}; frontSprite={_cursorImage?.sprite?.name ?? "null"}; backSprite={_cursorBackImage?.sprite?.name ?? "null"}; editorCanvasOrder={EditorCanvasSortingOrder}; canvasCursor={DrawableSuitsPlugin.DescribeUnityObject(_canvasCursorObject)}; previewMode={_previewMode}");
    }

    private void LogImmediateCursorUpdated(CursorVisualMode mode, string targetMode, float diameter, int triangleIndex, Vector2 uv, string fallbackReason, Rect drawRect, Texture2D texture)
    {
        var key = $"mode={mode}|tool={_tool}|source={_pointerSource}|target={targetMode}|brush={Mathf.RoundToInt(_brushSize)}|diameter={diameter:0.#}|tri={triangleIndex}|uv={uv.x:0.###},{uv.y:0.###}|fallback={fallbackReason}";
        if (Time.unscaledTime - _lastDynamicCursorLogTime < 0.75f && string.Equals(key, _lastDynamicCursorLogKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastDynamicCursorLogTime = Time.unscaledTime;
        _lastDynamicCursorLogKey = key;
        DrawableSuitsDiagnostics.Info($"ImmediateCursorUpdated: {key}; canPaint={_canPaint}; overPanel={IsCursorOverEditorPanel()}; overPreview={IsCursorOverPreviewViewport()}; screenCursor={_cursor}; imguiRect={drawRect}; texture={texture?.name ?? "null"}:{texture?.width.ToString() ?? "null"}x{texture?.height.ToString() ?? "null"}; guiDepth={int.MinValue}; systemCursorVisible={Cursor.visible}; cursorLock={Cursor.lockState}; previewMode={_previewMode}");
    }

    private void LogImmediateCursorDrawSkipped(string reason)
    {
        var key = reason ?? "unknown";
        if (Time.unscaledTime - _lastImmediateCursorSkipLogTime < 1.5f && string.Equals(key, _lastImmediateCursorSkipLog, StringComparison.Ordinal))
        {
            return;
        }

        _lastImmediateCursorSkipLogTime = Time.unscaledTime;
        _lastImmediateCursorSkipLog = key;
        DrawableSuitsDiagnostics.Warn($"ImmediateCursorDrawSkipped: reason={key}; isOpen={_isOpen}; eventType={Event.current?.type.ToString() ?? "null"}; cursor={_cursor}; screen={Screen.width}x{Screen.height}; tool={_tool}; pointerSource={_pointerSource}");
    }

    private void LogNativeCursorUpdated(CursorVisualMode mode, string targetMode, int diameter, int triangleIndex, Vector2 uv, string fallbackReason, int textureSize, Vector2 hotspot, string context)
    {
        var key = $"mode={mode}|tool={_tool}|source={_pointerSource}|target={targetMode}|diameter={diameter}|color={(_nativeCursorTexture != null ? _nativeCursorTexture.name : "null")}|tri={triangleIndex}|uv={uv.x:0.###},{uv.y:0.###}|fallback={fallbackReason}|context={context}";
        if (Time.unscaledTime - _lastNativeCursorLogTime < 0.5f && string.Equals(key, _lastNativeCursorLogKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastNativeCursorLogTime = Time.unscaledTime;
        _lastNativeCursorLogKey = key;
        DrawableSuitsDiagnostics.Info($"NativeCursorUpdated: {key}; screenCursor={_cursor}; texture={DrawableSuitsPlugin.DescribeUnityObject(_nativeCursorTexture)}; textureSize={textureSize}; hotspot={hotspot}; cursorVisible={Cursor.visible}; cursorLock={Cursor.lockState}; canPaint={_canPaint}; overPanel={IsCursorOverEditorPanel()}; overPreview={IsCursorOverPreviewViewport()}; warpGuardUntil={_ignoreMouseInputUntilTime:0.###}; previewMode={_previewMode}");
    }

    private void LogNativeCursorWarped(Vector2 target, string context)
    {
        var key = $"{context}|{target.x:0.#},{target.y:0.#}|source={_pointerSource}";
        if (Time.unscaledTime - _lastNativeCursorWarpTime < 0.25f && string.Equals(key, _lastNativeCursorWarpKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastNativeCursorWarpTime = Time.unscaledTime;
        _lastNativeCursorWarpKey = key;
        DrawableSuitsDiagnostics.Info($"NativeCursorWarped: context={context}; target={target}; pointerSource={_pointerSource}; ignoreMouseUntil={_ignoreMouseInputUntilTime:0.###}; mouseAvailable={_mousePositionAvailable}; lastMouse={_lastMousePosition}");
    }

    private void LogNativeCursorSetFailed(string key, string context, Exception ex)
    {
        var logKey = $"{context}|{key}|{ex.GetType().Name}:{ex.Message}";
        if (Time.unscaledTime - _lastNativeCursorFailureTime < 1.5f && string.Equals(logKey, _lastNativeCursorFailureKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastNativeCursorFailureTime = Time.unscaledTime;
        _lastNativeCursorFailureKey = logKey;
        DrawableSuitsDiagnostics.Exception($"NativeCursorSetFailed: context={context}; key={key}; cursor={_cursor}; visible={Cursor.visible}; lock={Cursor.lockState}", ex);
    }

    private void LogCursorCanvasState(string context)
    {
        var canvas = _cursorCanvasObject != null ? _cursorCanvasObject.GetComponent<Canvas>() : null;
        DrawableSuitsDiagnostics.Info($"CursorCanvasState[{context}]: legacyCanvasObject={DrawableSuitsPlugin.DescribeUnityObject(_cursorCanvasObject)}; active={_cursorCanvasObject?.activeSelf.ToString() ?? "null"}; renderMode={canvas?.renderMode.ToString() ?? "null"}; sortingOrder={canvas?.sortingOrder.ToString() ?? "null"}; editorCanvasOrder={EditorCanvasSortingOrder}; overrideSorting={canvas?.overrideSorting.ToString() ?? "null"}; hasRaycaster={(_cursorCanvasObject != null && _cursorCanvasObject.GetComponent<GraphicRaycaster>() != null)}; screen={Screen.width}x{Screen.height}; cursor={_cursor}; marker={DrawableSuitsPlugin.DescribeUnityObject(_cursorMarker)}; markerActive={_cursorMarker?.gameObject.activeSelf.ToString() ?? "null"}; markerPos={(_cursorMarker != null ? _cursorMarker.anchoredPosition.ToString() : "null")}; frontImage={DrawableSuitsPlugin.DescribeUnityObject(_cursorImage)}; backImage={DrawableSuitsPlugin.DescribeUnityObject(_cursorBackImage)}; canvasCursor={DrawableSuitsPlugin.DescribeUnityObject(_canvasCursorObject)}");
    }

    private void UpdateBrushIndicator()
    {
        if (_brushIndicator == null)
        {
            return;
        }

        // The old filled brush preview looked like a second cursor and changed with brush color/size.
        // Keep it hidden until a proper outline-only indicator is added.
        _brushIndicator.gameObject.SetActive(false);
    }

    private void UpdateDecalPlacementPreview()
    {
        if (!_isOpen || !IsPlacementTool(_tool) || !_canPaint || _suppressDecalPreviewUntilRelease)
        {
            HideDecalPlacementPreview("not eligible", false);
            return;
        }

        if (IsEditorModalOpen())
        {
            HideDecalPlacementPreview("editor modal open", false);
            LogPlacementPreviewSkippedForModal();
            return;
        }

        var texture = DrawableSuitsPlugin.Registry.GetEditableTexture(_selectedSuitId);
        if (texture == null)
        {
            HideDecalPlacementPreview("no editable texture", false);
            return;
        }

        if (IsWorldThirdPersonMode && IsCursorOverPreviewViewport())
        {
            if (!TryGetTexturePreviewUv(_cursor, out var panelUv))
            {
                HideDecalPlacementPreview("texture panel miss", false);
                return;
            }

            var panelMirrorTarget = ResolveUvMirrorTarget(texture, panelUv, false, "TexturePanel");
            if (!TryGetActivePlacementStamp(out var panelStampTexture, out var panelStampFailure))
            {
                HandlePlacementStampFailure(panelStampFailure);
                return;
            }

            ShowUvPlacementPreview(texture, panelUv, panelMirrorTarget, panelStampTexture, "TexturePanel");
            return;
        }

        if (IsWorldThirdPersonMode)
        {
            if (IsCursorOverEditorPanel() || !TryGetWorldPaintHit(out var hit))
            {
                HideDecalPlacementPreview("world miss", false);
                return;
            }

            var mirrorTarget = ResolveWorldMirrorTarget(texture, hit, false);
            if (_tool == EditorTool.Text)
            {
                if (!TryGetActivePlacementStamp(out var textStampTexture, out var textStampFailure))
                {
                    HandlePlacementStampFailure(textStampFailure);
                    return;
                }

                ShowWorldTextPlacementPreview(texture, hit, mirrorTarget, textStampTexture);
                return;
            }

            if (ShouldDelayWorldPlacementPreviewUntilIdle(texture, hit, mirrorTarget))
            {
                return;
            }

            if (!TryGetActivePlacementStamp(out var worldStampTexture, out var worldStampFailure))
            {
                HandlePlacementStampFailure(worldStampFailure);
                return;
            }

            ShowWorldPlacementPreview(texture, hit, mirrorTarget, worldStampTexture);
            return;
        }

        if (!TryGetTexturePreviewUv(_cursor, out var uv) || !IsCursorOverPreviewViewport())
        {
            HideDecalPlacementPreview("uv miss", false);
            return;
        }

        var uvMirrorTarget = ResolveUvMirrorTarget(texture, uv, false);
        if (!TryGetActivePlacementStamp(out var fallbackStampTexture, out var fallbackStampFailure))
        {
            HandlePlacementStampFailure(fallbackStampFailure);
            return;
        }

        ShowUvPlacementPreview(texture, uv, uvMirrorTarget, fallbackStampTexture, "TextureFallback");
    }

    private void HandlePlacementStampFailure(string failureReason)
    {
        if (_tool == EditorTool.Text && string.Equals(failureReason, "empty text", StringComparison.OrdinalIgnoreCase))
        {
            SetStatus("Enter text before stamping.", false);
        }

        HideDecalPlacementPreview(failureReason, false);
    }

    private bool IsPlacementTool(EditorTool tool)
    {
        return tool == EditorTool.Decal || tool == EditorTool.Text || tool == EditorTool.Sticker;
    }

    private bool TryGetActivePlacementStamp(out Texture2D stampTexture, out string failureReason)
    {
        stampTexture = null;
        failureReason = string.Empty;
        if (_tool == EditorTool.Decal)
        {
            if (_loadedDecal == null)
            {
                failureReason = "no selected decal";
                return false;
            }

            return TryGetEditedPlacementStamp(PlacementEditTarget.Decal, _loadedDecal, false, out stampTexture, out failureReason);
        }

        if (_tool == EditorTool.Text)
        {
            return TryGetTextStampTexture(out stampTexture, out failureReason);
        }

        if (_tool == EditorTool.Sticker)
        {
            if (!TryGetStickerStampTexture(_stickerShape, out var baseSticker, out failureReason))
            {
                return false;
            }

            return TryGetEditedPlacementStamp(PlacementEditTarget.Sticker, baseSticker, true, out stampTexture, out failureReason);
        }

        failureReason = "not a placement tool";
        return false;
    }

    private bool ShouldDelayWorldPlacementPreviewUntilIdle(Texture2D sourceTexture, RaycastHit hit, MirrorPaintTarget mirrorTarget)
    {
        if (_tool != EditorTool.Decal && _tool != EditorTool.Sticker)
        {
            return false;
        }

        var now = Time.unscaledTime;
        var cursorDelta = _lastPlacementPreviewCursorValid
            ? Vector2.Distance(_cursor, _lastPlacementPreviewCursor)
            : 0f;
        var cursorMoved = _lastPlacementPreviewCursorValid
            && cursorDelta > WorldPlacementPreviewMoveThresholdPixels;
        var targetKey = BuildWorldPlacementPreviewIdleTargetKey(sourceTexture, hit, mirrorTarget);
        var targetChanged = !string.Equals(targetKey, _lastPlacementPreviewIdleTargetKey, StringComparison.Ordinal);

        if (!_lastPlacementPreviewCursorValid || cursorMoved || targetChanged)
        {
            _lastPlacementPreviewCursor = _cursor;
            _lastPlacementPreviewCursorValid = true;
            _lastPlacementPreviewCursorMoveTime = now;
            _lastPlacementPreviewIdleTargetKey = targetKey;
            HideDecalPlacementPreview(cursorMoved ? "preview cursor moving" : "preview target changed", false);
            var eventName = cursorMoved ? "PlacementPreviewHiddenWhileMoving" : "PlacementPreviewWaitingForIdle";
            var reason = cursorMoved ? "cursor moved" : targetChanged ? "target changed" : "cursor initialized";
            LogPlacementPreviewIdleWait(eventName, reason, sourceTexture, hit, mirrorTarget, cursorDelta, 0f);
            LogPlacementPreviewStampDeferred(reason, sourceTexture, hit, mirrorTarget, cursorDelta, 0f);
            SetPlacementPreviewIdleStatus();
            return true;
        }

        var idleTime = now - _lastPlacementPreviewCursorMoveTime;
        if (idleTime < WorldPlacementPreviewIdleDelay)
        {
            HideDecalPlacementPreview("preview waiting for idle", false);
            LogPlacementPreviewIdleWait("PlacementPreviewWaitingForIdle", "idle delay", sourceTexture, hit, mirrorTarget, cursorDelta, idleTime);
            LogPlacementPreviewStampDeferred("idle delay", sourceTexture, hit, mirrorTarget, cursorDelta, idleTime);
            SetPlacementPreviewIdleStatus();
            return true;
        }

        return false;
    }

    private string BuildWorldPlacementPreviewIdleTargetKey(Texture2D sourceTexture, RaycastHit hit, MirrorPaintTarget mirrorTarget)
    {
        var pixel = sourceTexture != null ? TexturePixel(sourceTexture, hit.textureCoord) : new Vector2Int(-1, -1);
        var pixelBucketX = pixel.x >= 0 ? pixel.x / 8 : -1;
        var pixelBucketY = pixel.y >= 0 ? pixel.y / 8 : -1;
        var mirrorTriangle = mirrorTarget.Enabled && mirrorTarget.Available ? mirrorTarget.TriangleIndex : -1;
        return $"{_tool}|suit={_selectedSuitId}|pixelBucket={pixelBucketX},{pixelBucketY}|triangle={hit.triangleIndex}|mirror={mirrorTarget.Enabled}:{mirrorTarget.Available}:{mirrorTriangle}|size={Mathf.RoundToInt(CurrentPlacementSize())}|rot={Mathf.RoundToInt(CurrentPlacementRotation() * 10f)}|opacity={Mathf.RoundToInt(_brushOpacity * 1000f)}|stamp={CurrentPlacementName()}|color={ColorToHex(_brushColor)}|serial={_decalPreviewSerial}";
    }

    private void SetPlacementPreviewIdleStatus()
    {
        var noun = _tool == EditorTool.Sticker ? "sticker" : "decal";
        var message = $"Hold still to preview {noun}. Click/RT still stamps.";
        if (!_statusMessage.StartsWith(message, StringComparison.OrdinalIgnoreCase))
        {
            SetStatus(message, false);
        }
    }

    private void ShowWorldPlacementPreview(Texture2D sourceTexture, RaycastHit hit, MirrorPaintTarget mirrorTarget, Texture2D stampTexture)
    {
        if (_worldAvatarRenderer == null || _worldSourceRenderer == null || sourceTexture == null || stampTexture == null || _worldPaintCollider == null)
        {
            HideDecalPlacementPreview("world dependencies missing", false);
            return;
        }

        if (!EnsureDecalPreviewTexture(sourceTexture))
        {
            HideDecalPlacementPreview("preview texture failed", true);
            return;
        }

        var key = BuildSurfacePlacementPreviewKey("WorldThirdPersonSurfaceDecal", sourceTexture, hit, mirrorTarget);
        TextSurfaceStampStats primaryStats = default;
        TextSurfaceStampStats mirrorStats = default;
        var previewRebuilt = false;
        var keyChanged = !string.Equals(key, _lastDecalPreviewKey, StringComparison.Ordinal);
        if (!keyChanged)
        {
            LogPlacementPreviewReused(key, sourceTexture, hit, stampTexture);
        }
        else if (_decalPreviewVisible
            && _worldDecalPreviewApplied
            && Time.unscaledTime - _lastWorldPlacementPreviewRebuildTime < WorldPlacementPreviewMinInterval)
        {
            LogPlacementPreviewThrottled(key, sourceTexture, hit, stampTexture);
            SetPlacementPreviewStatus(mirrorTarget);
            return;
        }

        if (keyChanged)
        {
            var rebuildStart = Time.realtimeSinceStartup;
            if (!CopySourceTextureToPlacementPreview(sourceTexture, "world placement preview"))
            {
                HideDecalPlacementPreview("preview source copy failed", true);
                return;
            }

            var touchedPixels = mirrorTarget.Enabled && mirrorTarget.Available ? new HashSet<int>() : null;
            var primaryChanged = CompositeDecalSurfaceStamp(_decalPreviewTexture, stampTexture, hit.point, hit.normal, CurrentPlacementRotation(), false, Mathf.Clamp01(_brushOpacity * 0.62f), touchedPixels, out primaryStats, false);
            var mirrorChanged = false;
            if (ShouldApplyMirror(sourceTexture, hit.textureCoord, mirrorTarget) && TryGetMirrorWorldPlacement(mirrorTarget, out var mirrorPoint, out var mirrorNormal))
            {
                mirrorChanged = CompositeDecalSurfaceStamp(_decalPreviewTexture, stampTexture, mirrorPoint, mirrorNormal, -CurrentPlacementRotation(), true, Mathf.Clamp01(_brushOpacity * 0.62f), touchedPixels, out mirrorStats, false);
            }

            if (!primaryChanged && !mirrorChanged)
            {
                HideDecalPlacementPreview("decal surface preview projected no pixels", false);
                if (_tool == EditorTool.Sticker)
                {
                    LogStickerSurfacePreviewSkipped(sourceTexture, hit, mirrorTarget, stampTexture, primaryStats, mirrorStats, "surface preview projected no pixels");
                }
                else
                {
                    LogDecalSurfacePreviewSkipped(sourceTexture, hit, mirrorTarget, stampTexture, primaryStats, mirrorStats);
                }
                return;
            }

            _decalPreviewTexture.Apply(false, false);
            _lastDecalPreviewKey = key;
            _lastWorldPlacementPreviewRebuildTime = Time.unscaledTime;
            LogPlacementPreviewRebuildTiming(key, sourceTexture, stampTexture, (Time.realtimeSinceStartup - rebuildStart) * 1000f);
            previewRebuilt = true;
        }

        var previewMaterial = EnsureWorldDecalPreviewMaterial(sourceTexture);
        if (previewMaterial == null)
        {
            HideDecalPlacementPreview("preview material failed", true);
            return;
        }

        previewMaterial.mainTexture = _decalPreviewTexture;
        var baseMaterials = BuildWorldProxyMaterials(_worldSourceRenderer, false);
        var previewMaterials = new Material[baseMaterials.Length];
        for (var i = 0; i < baseMaterials.Length; i++)
        {
            previewMaterials[i] = ReferenceEquals(baseMaterials[i], _worldHiddenSubmeshMaterial) ? baseMaterials[i] : previewMaterial;
        }

        _worldAvatarRenderer.sharedMaterials = previewMaterials;
        _worldDecalPreviewApplied = true;
        _decalPreviewVisible = true;
        _placementPreviewTool = _tool;
        if (_uvDecalPreviewRect != null)
        {
            _uvDecalPreviewRect.gameObject.SetActive(false);
        }
        if (_uvMirrorDecalPreviewRect != null)
        {
            _uvMirrorDecalPreviewRect.gameObject.SetActive(false);
        }

        SetPlacementPreviewStatus(mirrorTarget);
        if (previewRebuilt)
        {
            if (_tool == EditorTool.Sticker)
            {
                LogStickerSurfacePreviewUpdated(sourceTexture, hit, mirrorTarget, stampTexture, primaryStats, mirrorStats);
            }
            else
            {
                LogDecalSurfacePreviewUpdated(sourceTexture, hit, mirrorTarget, stampTexture, primaryStats, mirrorStats);
            }
        }
    }

    private void ShowWorldTextPlacementPreview(Texture2D sourceTexture, RaycastHit hit, MirrorPaintTarget mirrorTarget, Texture2D stampTexture)
    {
        if (_worldAvatarRenderer == null || _worldSourceRenderer == null || sourceTexture == null || stampTexture == null || _worldPaintCollider == null)
        {
            HideDecalPlacementPreview("world text dependencies missing", false);
            return;
        }

        if (!EnsureDecalPreviewTexture(sourceTexture))
        {
            HideDecalPlacementPreview("text preview texture failed", true);
            return;
        }

        var key = BuildSurfacePlacementPreviewKey("WorldThirdPersonSurfaceText", sourceTexture, hit, mirrorTarget);
        TextSurfaceStampStats primaryStats = default;
        TextSurfaceStampStats mirrorStats = default;
        var previewRebuilt = false;
        if (!string.Equals(key, _lastDecalPreviewKey, StringComparison.Ordinal))
        {
            _decalPreviewTexture.SetPixels32(sourceTexture.GetPixels32());
            var touchedPixels = mirrorTarget.Enabled && mirrorTarget.Available ? new HashSet<int>() : null;
            var primaryChanged = CompositeTextSurfaceStamp(_decalPreviewTexture, stampTexture, hit.point, hit.normal, _textRotation, false, Mathf.Clamp01(_brushOpacity * 0.62f), touchedPixels, out primaryStats);
            var mirrorChanged = false;
            if (ShouldApplyMirror(sourceTexture, hit.textureCoord, mirrorTarget) && TryGetMirrorWorldPlacement(mirrorTarget, out var mirrorPoint, out var mirrorNormal))
            {
                mirrorChanged = CompositeTextSurfaceStamp(_decalPreviewTexture, stampTexture, mirrorPoint, mirrorNormal, -_textRotation, true, Mathf.Clamp01(_brushOpacity * 0.62f), touchedPixels, out mirrorStats);
            }

            if (!primaryChanged && !mirrorChanged)
            {
                HideDecalPlacementPreview("text surface preview projected no pixels", false);
                LogTextSurfacePreviewSkipped(sourceTexture, hit, mirrorTarget, stampTexture, primaryStats, mirrorStats);
                return;
            }

            _decalPreviewTexture.Apply(false, false);
            _lastDecalPreviewKey = key;
            previewRebuilt = true;
        }

        var previewMaterial = EnsureWorldDecalPreviewMaterial(sourceTexture);
        if (previewMaterial == null)
        {
            HideDecalPlacementPreview("text preview material failed", true);
            return;
        }

        previewMaterial.mainTexture = _decalPreviewTexture;
        var baseMaterials = BuildWorldProxyMaterials(_worldSourceRenderer, false);
        var previewMaterials = new Material[baseMaterials.Length];
        for (var i = 0; i < baseMaterials.Length; i++)
        {
            previewMaterials[i] = ReferenceEquals(baseMaterials[i], _worldHiddenSubmeshMaterial) ? baseMaterials[i] : previewMaterial;
        }

        _worldAvatarRenderer.sharedMaterials = previewMaterials;
        _worldDecalPreviewApplied = true;
        _decalPreviewVisible = true;
        _placementPreviewTool = _tool;
        if (_uvDecalPreviewRect != null)
        {
            _uvDecalPreviewRect.gameObject.SetActive(false);
        }
        if (_uvMirrorDecalPreviewRect != null)
        {
            _uvMirrorDecalPreviewRect.gameObject.SetActive(false);
        }

        SetPlacementPreviewStatus(mirrorTarget);
        if (previewRebuilt)
        {
            LogTextSurfacePreviewUpdated(sourceTexture, hit, mirrorTarget, stampTexture, primaryStats, mirrorStats);
        }
    }

    private void ShowUvPlacementPreview(Texture2D sourceTexture, Vector2 uv, MirrorPaintTarget mirrorTarget, Texture2D stampTexture, string mode)
    {
        if (_uvDecalPreviewRect == null || _uvDecalPreviewImage == null || _previewViewportRect == null || sourceTexture == null || stampTexture == null)
        {
            HideDecalPlacementPreview("uv dependencies missing", false);
            return;
        }

        RestoreWorldDecalPreviewMaterial();

        var primaryVisible = ConfigureUvPlacementPreview(_uvDecalPreviewRect, _uvDecalPreviewImage, sourceTexture, stampTexture, uv, false);

        if (ShouldApplyMirror(sourceTexture, uv, mirrorTarget))
        {
            ConfigureUvPlacementPreview(_uvMirrorDecalPreviewRect, _uvMirrorDecalPreviewImage, sourceTexture, stampTexture, mirrorTarget.Uv, true);
        }
        else if (_uvMirrorDecalPreviewRect != null)
        {
            _uvMirrorDecalPreviewRect.gameObject.SetActive(false);
        }

        if (!primaryVisible)
        {
            HideDecalPlacementPreview("uv preview target outside visible panel", false);
            return;
        }

        _decalPreviewVisible = true;
        _placementPreviewTool = _tool;
        _lastDecalPreviewKey = BuildDecalPreviewKey(mode, sourceTexture, uv, mirrorTarget);
        SetPlacementPreviewStatus(mirrorTarget);
        LogPlacementPreviewUpdated(mode, sourceTexture, uv, mirrorTarget, stampTexture, false);
    }

    private bool ConfigureUvPlacementPreview(RectTransform previewRect, RawImage previewImage, Texture2D sourceTexture, Texture2D stampTexture, Vector2 uv, bool mirrored)
    {
        if (previewRect == null || previewImage == null || _previewViewportRect == null || sourceTexture == null || stampTexture == null)
        {
            return false;
        }

        var rect = _previewViewportRect.rect;
        if (!TryTextureUvToPreviewLocal(uv, out var localPoint))
        {
            previewRect.gameObject.SetActive(false);
            return false;
        }

        var view = GetUvPanelViewRect();
        var stampSize = GetPlacementStampPixelSize(stampTexture);
        var displayWidth = Mathf.Clamp(stampSize.x / Mathf.Max(1f, sourceTexture.width * view.width) * rect.width, 4f, rect.width * 1.5f);
        var displayHeight = Mathf.Clamp(stampSize.y / Mathf.Max(1f, sourceTexture.height * view.height) * rect.height, 4f, rect.height * 1.5f);

        previewImage.texture = stampTexture;
        previewImage.color = _tool == EditorTool.Text
            ? new Color(_brushColor.r, _brushColor.g, _brushColor.b, mirrored ? 0.5f : 0.62f)
            : mirrored ? new Color(1f, 1f, 1f, 0.5f) : new Color(1f, 1f, 1f, 0.62f);
        previewImage.raycastTarget = false;
        previewImage.uvRect = mirrored ? new Rect(1f, 0f, -1f, 1f) : new Rect(0f, 0f, 1f, 1f);
        previewRect.anchoredPosition = localPoint;
        previewRect.sizeDelta = new Vector2(displayWidth, displayHeight);
        previewRect.localRotation = Quaternion.Euler(0f, 0f, mirrored ? -CurrentPlacementRotation() : CurrentPlacementRotation());
        previewRect.gameObject.SetActive(true);
        return true;
    }

    private bool EnsureDecalPreviewTexture(Texture2D sourceTexture)
    {
        if (sourceTexture == null)
        {
            return false;
        }

        if (_decalPreviewTexture != null
            && _decalPreviewTexture.width == sourceTexture.width
            && _decalPreviewTexture.height == sourceTexture.height
            && _decalPreviewTexture.format == TextureFormat.RGBA32)
        {
            return true;
        }

        if (_decalPreviewTexture != null)
        {
            Destroy(_decalPreviewTexture);
        }

        _decalPreviewTexture = new Texture2D(sourceTexture.width, sourceTexture.height, TextureFormat.RGBA32, false)
        {
            name = "DrawableSuitsDecalPlacementPreview",
            filterMode = sourceTexture.filterMode,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };
        InvalidateDecalPreview("preview texture recreated");
        return true;
    }

    private bool CopySourceTextureToPlacementPreview(Texture2D sourceTexture, string context)
    {
        if (sourceTexture == null || _decalPreviewTexture == null)
        {
            return false;
        }

        try
        {
            Graphics.CopyTexture(sourceTexture, _decalPreviewTexture);
            return true;
        }
        catch (Exception ex)
        {
            var pixelCount = sourceTexture.width * sourceTexture.height;
            if (_placementPreviewBasePixels == null
                || _placementPreviewBasePixels.Length != pixelCount
                || !ReferenceEquals(_placementPreviewBaseSource, sourceTexture)
                || _placementPreviewBaseSerial != _decalPreviewSerial)
            {
                try
                {
                    _placementPreviewBasePixels = sourceTexture.GetPixels32();
                    _placementPreviewBaseSource = sourceTexture;
                    _placementPreviewBaseSerial = _decalPreviewSerial;
                }
                catch (Exception readEx)
                {
                    DrawableSuitsDiagnostics.Exception($"PlacementPreview source read failed. context={context}; source={sourceTexture.name}; size={sourceTexture.width}x{sourceTexture.height}; copyException={ex.GetType().Name}: {ex.Message}", readEx);
                    return false;
                }
            }

            _decalPreviewTexture.SetPixels32(_placementPreviewBasePixels);
            if (Time.unscaledTime - _lastPlacementPreviewThrottleLogTime > 2f)
            {
                _lastPlacementPreviewThrottleLogTime = Time.unscaledTime;
                DrawableSuitsDiagnostics.Warn($"PlacementPreview source copy fallback. context={context}; source={sourceTexture.name}; preview={_decalPreviewTexture.name}; reason={ex.GetType().Name}: {ex.Message}; cachedPixels={_placementPreviewBasePixels.Length}");
            }
            return true;
        }
    }

    private void LogPlacementPreviewIdleWait(string eventName, string reason, Texture2D sourceTexture, RaycastHit hit, MirrorPaintTarget mirrorTarget, float cursorDelta, float idleTime)
    {
        var pixel = sourceTexture != null ? TexturePixel(sourceTexture, hit.textureCoord) : new Vector2Int(-1, -1);
        var logKey = $"{eventName}|{_tool}|{reason}|{pixel.x / 8},{pixel.y / 8}|{hit.triangleIndex}|{CurrentPlacementName()}|{Mathf.RoundToInt(CurrentPlacementSize())}|{Mathf.RoundToInt(CurrentPlacementRotation() * 10f)}";
        if (string.Equals(logKey, _lastPlacementPreviewIdleLogKey, StringComparison.Ordinal)
            && Time.unscaledTime - _lastPlacementPreviewIdleLogTime < 0.75f)
        {
            return;
        }

        _lastPlacementPreviewIdleLogKey = logKey;
        _lastPlacementPreviewIdleLogTime = Time.unscaledTime;
        DrawableSuitsDiagnostics.Info($"{eventName}: tool={_tool}; pointerSource={_pointerSource}; cursor={_cursor}; cursorDelta={cursorDelta:0.##}; idleTime={idleTime:0.###}; idleDelay={WorldPlacementPreviewIdleDelay:0.###}; moveThresholdPx={WorldPlacementPreviewMoveThresholdPixels:0.##}; target=WorldThirdPerson; uv={hit.textureCoord}; pixel={pixel.x},{pixel.y}; triangle={hit.triangleIndex}; mirror={DescribeMirrorTarget(sourceTexture, mirrorTarget)}; stampDeferred=True; reason={reason}");
    }

    private void LogPlacementPreviewSkippedForModal()
    {
        var modal = DescribeOpenEditorModal();
        var key = $"PlacementPreviewSkippedForModal|tool={_tool}|modal={modal}";
        if (string.Equals(key, _lastPlacementPreviewThrottleLogKey, StringComparison.Ordinal)
            && Time.unscaledTime - _lastPlacementPreviewThrottleLogTime < 0.75f)
        {
            return;
        }

        _lastPlacementPreviewThrottleLogKey = key;
        _lastPlacementPreviewThrottleLogTime = Time.unscaledTime;
        DrawableSuitsDiagnostics.Info($"PlacementPreviewSkippedForModal: tool={_tool}; modal={modal}; pointerSource={_pointerSource}; cursor={_cursor}; suit={_selectedSuitId}");
    }

    private void LogPlacementPreviewStampDeferred(string reason, Texture2D sourceTexture, RaycastHit hit, MirrorPaintTarget mirrorTarget, float cursorDelta, float idleTime)
    {
        var pixel = sourceTexture != null ? TexturePixel(sourceTexture, hit.textureCoord) : new Vector2Int(-1, -1);
        var key = $"PlacementPreviewStampDeferred|tool={_tool}|reason={reason}|pixel={pixel.x / 8},{pixel.y / 8}|triangle={hit.triangleIndex}";
        if (string.Equals(key, _lastPlacementPreviewThrottleLogKey, StringComparison.Ordinal)
            && Time.unscaledTime - _lastPlacementPreviewThrottleLogTime < 0.75f)
        {
            return;
        }

        _lastPlacementPreviewThrottleLogKey = key;
        _lastPlacementPreviewThrottleLogTime = Time.unscaledTime;
        DrawableSuitsDiagnostics.Info($"PlacementPreviewStampDeferred: tool={_tool}; reason={reason}; cursorDelta={cursorDelta:0.##}; idleTime={idleTime:0.###}; idleDelay={WorldPlacementPreviewIdleDelay:0.###}; target=WorldThirdPerson; uv={hit.textureCoord}; pixel={pixel.x},{pixel.y}; triangle={hit.triangleIndex}; mirror={DescribeMirrorTarget(sourceTexture, mirrorTarget)}; stampGenerated=False; suit={_selectedSuitId}");
    }

    private void LogPlacementPreviewReused(string key, Texture2D sourceTexture, RaycastHit hit, Texture2D stampTexture)
    {
        if (string.Equals(key, _lastPlacementPreviewReuseLogKey, StringComparison.Ordinal)
            || Time.unscaledTime - _lastPlacementPreviewReuseLogTime < 0.75f)
        {
            return;
        }

        _lastPlacementPreviewReuseLogKey = key;
        _lastPlacementPreviewReuseLogTime = Time.unscaledTime;
        var pixel = sourceTexture != null
            ? new Vector2Int(Mathf.RoundToInt(hit.textureCoord.x * (sourceTexture.width - 1)), Mathf.RoundToInt(hit.textureCoord.y * (sourceTexture.height - 1)))
            : new Vector2Int(-1, -1);
        DrawableSuitsDiagnostics.Info($"PlacementPreviewReused: tool={_tool}; pixel={pixel.x},{pixel.y}; stamp={stampTexture?.name ?? "null"}; stampSize={stampTexture?.width ?? 0}x{stampTexture?.height ?? 0}; key={key.GetHashCode()}");
    }

    private void LogPlacementPreviewThrottled(string key, Texture2D sourceTexture, RaycastHit hit, Texture2D stampTexture)
    {
        var pixel = sourceTexture != null
            ? new Vector2Int(Mathf.RoundToInt(hit.textureCoord.x * (sourceTexture.width - 1)), Mathf.RoundToInt(hit.textureCoord.y * (sourceTexture.height - 1)))
            : new Vector2Int(-1, -1);
        var logKey = $"{_tool}|{pixel.x},{pixel.y}|{stampTexture?.name ?? "null"}|{Mathf.RoundToInt(CurrentPlacementSize())}|{Mathf.RoundToInt(CurrentPlacementRotation())}";
        if (string.Equals(logKey, _lastPlacementPreviewThrottleLogKey, StringComparison.Ordinal)
            && Time.unscaledTime - _lastPlacementPreviewThrottleLogTime < 0.75f)
        {
            return;
        }

        _lastPlacementPreviewThrottleLogKey = logKey;
        _lastPlacementPreviewThrottleLogTime = Time.unscaledTime;
        DrawableSuitsDiagnostics.Info($"PlacementPreviewThrottled: tool={_tool}; pixel={pixel.x},{pixel.y}; interval={(Time.unscaledTime - _lastWorldPlacementPreviewRebuildTime):0.###}; minInterval={WorldPlacementPreviewMinInterval:0.###}; stamp={stampTexture?.name ?? "null"}; stampSize={stampTexture?.width ?? 0}x{stampTexture?.height ?? 0}; key={key.GetHashCode()}");
    }

    private void LogPlacementPreviewRebuildTiming(string key, Texture2D sourceTexture, Texture2D stampTexture, float elapsedMs)
    {
        var logKey = $"{_tool}|{sourceTexture?.width ?? 0}x{sourceTexture?.height ?? 0}|{stampTexture?.name ?? "null"}|{Mathf.RoundToInt(CurrentPlacementSize())}|{Mathf.RoundToInt(CurrentPlacementRotation())}";
        if (string.Equals(logKey, _lastPlacementPreviewRebuildLogKey, StringComparison.Ordinal)
            && Time.unscaledTime - _lastPlacementPreviewRebuildLogTime < 0.75f)
        {
            return;
        }

        _lastPlacementPreviewRebuildLogKey = logKey;
        _lastPlacementPreviewRebuildLogTime = Time.unscaledTime;
        DrawableSuitsDiagnostics.Info($"PlacementPreviewIdleRebuilt: tool={_tool}; pointerSource={_pointerSource}; source={sourceTexture?.name ?? "null"}; sourceSize={sourceTexture?.width ?? 0}x{sourceTexture?.height ?? 0}; stamp={stampTexture?.name ?? "null"}; stampSize={stampTexture?.width ?? 0}x{stampTexture?.height ?? 0}; elapsedMs={elapsedMs:0.##}; idleTime={(Time.unscaledTime - _lastPlacementPreviewCursorMoveTime):0.###}; idleDelay={WorldPlacementPreviewIdleDelay:0.###}; key={key.GetHashCode()}");
    }

    private Material EnsureWorldDecalPreviewMaterial(Texture2D sourceTexture)
    {
        var baseMaterial = DrawableSuitsPlugin.Registry.GetRuntimeMaterial(_selectedSuitId)
            ?? _worldAvatarRenderer?.sharedMaterial
            ?? _worldSourceRenderer?.sharedMaterial;
        if (baseMaterial == null)
        {
            return null;
        }

        if (_worldDecalPreviewMaterial == null || _worldDecalPreviewMaterial.shader != baseMaterial.shader)
        {
            if (_worldDecalPreviewMaterial != null)
            {
                Destroy(_worldDecalPreviewMaterial);
            }

            _worldDecalPreviewMaterial = new Material(baseMaterial)
            {
                name = "DrawableSuitsWorldDecalPlacementPreviewMaterial",
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        _worldDecalPreviewMaterial.mainTexture = _decalPreviewTexture ?? sourceTexture;
        if (_worldDecalPreviewMaterial.HasProperty("_Color"))
        {
            _worldDecalPreviewMaterial.SetColor("_Color", Color.white);
        }

        return _worldDecalPreviewMaterial;
    }

    private void HideDecalPlacementPreview(string reason, bool forceLog)
    {
        var wasVisible = _decalPreviewVisible
            || _worldDecalPreviewApplied
            || (_uvDecalPreviewRect != null && _uvDecalPreviewRect.gameObject.activeSelf)
            || (_uvMirrorDecalPreviewRect != null && _uvMirrorDecalPreviewRect.gameObject.activeSelf);
        if (_uvDecalPreviewRect != null)
        {
            _uvDecalPreviewRect.gameObject.SetActive(false);
        }
        if (_uvMirrorDecalPreviewRect != null)
        {
            _uvMirrorDecalPreviewRect.gameObject.SetActive(false);
        }

        RestoreWorldDecalPreviewMaterial();
        if (wasVisible)
        {
            RefreshTexturePanelPreview("placement preview hidden", false);
        }
        _decalPreviewVisible = false;
        if (_statusMessage.StartsWith("Previewing decal", StringComparison.OrdinalIgnoreCase)
            || _statusMessage.StartsWith("Previewing text", StringComparison.OrdinalIgnoreCase)
            || _statusMessage.StartsWith("Previewing sticker", StringComparison.OrdinalIgnoreCase))
        {
            SetStatus(BuildReadinessStatus(), false);
        }

        if (wasVisible || forceLog)
        {
            LogPlacementPreviewHidden(reason, forceLog);
        }
    }

    private void RestoreWorldDecalPreviewMaterial()
    {
        if (!_worldDecalPreviewApplied)
        {
            return;
        }

        if (_worldAvatarRenderer != null && _worldSourceRenderer != null)
        {
            _worldAvatarRenderer.sharedMaterials = BuildWorldProxyMaterials(_worldSourceRenderer, false);
        }

        _worldDecalPreviewApplied = false;
    }

    private void DestroyDecalPlacementPreviewResources()
    {
        HideDecalPlacementPreview("destroy preview resources", true);
        if (_decalPreviewTexture != null)
        {
            Destroy(_decalPreviewTexture);
            _decalPreviewTexture = null;
        }
        if (_worldDecalPreviewMaterial != null)
        {
            Destroy(_worldDecalPreviewMaterial);
            _worldDecalPreviewMaterial = null;
        }
        _lastDecalPreviewKey = string.Empty;
        _placementPreviewBasePixels = null;
        _placementPreviewBaseSource = null;
        _placementPreviewBaseSerial = -1;
    }

    private void InvalidateDecalPreview(string reason)
    {
        _decalPreviewSerial++;
        _lastDecalPreviewKey = string.Empty;
        _placementPreviewBaseSource = null;
        _placementPreviewBaseSerial = -1;
        ResetSettledPlacementPreviewState();
        if (_decalPreviewVisible)
        {
            LogDecalPreviewHidden($"invalidated: {reason}", false);
        }
    }

    private void ResetSettledPlacementPreviewState()
    {
        _lastPlacementPreviewCursorValid = false;
        _lastPlacementPreviewCursor = Vector2.zero;
        _lastPlacementPreviewCursorMoveTime = 0f;
        _lastPlacementPreviewIdleTargetKey = string.Empty;
        _lastPlacementPreviewIdleLogKey = string.Empty;
    }

    private string BuildDecalPreviewKey(string mode, Texture2D sourceTexture, Vector2 uv, MirrorPaintTarget mirrorTarget)
    {
        var px = sourceTexture != null ? Mathf.RoundToInt(uv.x * (sourceTexture.width - 1)) : -1;
        var py = sourceTexture != null ? Mathf.RoundToInt(uv.y * (sourceTexture.height - 1)) : -1;
        return $"{_decalPreviewSerial}|{mode}|tool={_tool}|suit={_selectedSuitId}|pixel={px},{py}|mirror={DescribeMirrorTarget(sourceTexture, mirrorTarget)}|size={Mathf.RoundToInt(CurrentPlacementSize())}|rot={Mathf.RoundToInt(CurrentPlacementRotation() * 10f)}|opacity={Mathf.RoundToInt(_brushOpacity * 1000f)}|stamp={CurrentPlacementName()}|stampKey={_textStampTextureKey}|color={ColorToHex(_brushColor)}|texture={sourceTexture?.width ?? 0}x{sourceTexture?.height ?? 0}";
    }

    private string BuildSurfacePlacementPreviewKey(string mode, Texture2D sourceTexture, RaycastHit hit, MirrorPaintTarget mirrorTarget)
    {
        var px = sourceTexture != null ? Mathf.RoundToInt(hit.textureCoord.x * (sourceTexture.width - 1)) : -1;
        var py = sourceTexture != null ? Mathf.RoundToInt(hit.textureCoord.y * (sourceTexture.height - 1)) : -1;
        var pointKey = $"{Mathf.RoundToInt(hit.point.x * 80f)},{Mathf.RoundToInt(hit.point.y * 80f)},{Mathf.RoundToInt(hit.point.z * 80f)}";
        var normalKey = $"{Mathf.RoundToInt(hit.normal.x * 24f)},{Mathf.RoundToInt(hit.normal.y * 24f)},{Mathf.RoundToInt(hit.normal.z * 24f)}";
        return $"{_decalPreviewSerial}|{mode}|tool={_tool}|suit={_selectedSuitId}|pixel={px},{py}|triangle={hit.triangleIndex}|point={pointKey}|normal={normalKey}|mirror={DescribeMirrorTarget(sourceTexture, mirrorTarget)}|size={Mathf.RoundToInt(CurrentPlacementSize())}|rot={Mathf.RoundToInt(CurrentPlacementRotation() * 10f)}|opacity={Mathf.RoundToInt(_brushOpacity * 1000f)}|stamp={CurrentPlacementName()}|stampKey={_textStampTextureKey}|color={ColorToHex(_brushColor)}|texture={sourceTexture?.width ?? 0}x{sourceTexture?.height ?? 0}";
    }

    private void SetPlacementPreviewStatus(MirrorPaintTarget mirrorTarget)
    {
        var noun = _tool == EditorTool.Text ? "text" : _tool == EditorTool.Sticker ? "sticker" : "decal";
        if (mirrorTarget.Enabled && !mirrorTarget.Available)
        {
            if (!_statusMessage.StartsWith($"Previewing {noun}. Mirror target not found", StringComparison.OrdinalIgnoreCase))
            {
                SetStatus($"Previewing {noun}. Mirror target not found; stamp affects primary only.", false);
            }
            return;
        }

        if (!_statusMessage.StartsWith($"Previewing {noun}", StringComparison.OrdinalIgnoreCase))
        {
            SetStatus($"Previewing {noun}. Click/RT to stamp.", false);
        }
    }

    private Vector2 GetPlacementStampPixelSize(Texture2D stampTexture)
    {
        if (_tool == EditorTool.Text && stampTexture != null)
        {
            var height = Mathf.Max(1f, _textSize);
            var aspect = stampTexture.width / Mathf.Max(1f, (float)stampTexture.height);
            return new Vector2(Mathf.Max(1f, height * aspect), height);
        }

        var size = _tool == EditorTool.Sticker ? _stickerSize : _decalSize;
        return new Vector2(Mathf.Max(1f, size), Mathf.Max(1f, size));
    }

    private string CurrentPlacementName()
    {
        return _tool switch
        {
            EditorTool.Text => $"text:{NormalizeTextStampValue(_textStampValue).Length}",
            EditorTool.Sticker => $"sticker:{_stickerShape}:edit={_stickerPlacementEdit.Revision}",
            _ => $"{CurrentDecalName()}:edit={_decalPlacementEdit.Revision}"
        };
    }

    private bool TryGetEditedPlacementStamp(PlacementEditTarget target, Texture2D source, bool tintSticker, out Texture2D texture, out string failureReason)
    {
        texture = null;
        failureReason = string.Empty;
        if (source == null)
        {
            failureReason = "missing placement source";
            return false;
        }

        var state = GetPlacementEditState(target);
        if (state == null)
        {
            failureReason = "invalid placement edit target";
            return false;
        }

        if (target == PlacementEditTarget.Decal && state.IsDefault)
        {
            texture = source;
            return true;
        }

        var key = BuildPlacementStampKey(target, source, tintSticker, state, "full", 0);
        if (target == PlacementEditTarget.Decal
            && _editedDecalStampTexture != null
            && string.Equals(_editedDecalStampKey, key, StringComparison.Ordinal))
        {
            texture = _editedDecalStampTexture;
            return true;
        }

        if (target == PlacementEditTarget.Sticker
            && _editedStickerStampTexture != null
            && string.Equals(_editedStickerStampKey, key, StringComparison.Ordinal))
        {
            texture = _editedStickerStampTexture;
            return true;
        }

        if (!TryGenerateEditedPlacementStamp(target, source, tintSticker, state, 0, false, out var generated, out failureReason, out _))
        {
            return false;
        }

        if (target == PlacementEditTarget.Decal)
        {
            if (_editedDecalStampTexture != null)
            {
                Destroy(_editedDecalStampTexture);
            }

            _editedDecalStampTexture = generated;
            _editedDecalStampKey = key;
        }
        else
        {
            if (_editedStickerStampTexture != null)
            {
                Destroy(_editedStickerStampTexture);
            }

            _editedStickerStampTexture = generated;
            _editedStickerStampKey = key;
        }

        texture = generated;
        LogPlacementEditedStampGenerated(target, source, state, generated, false);
        return true;
    }

    private bool TryGetEditedPlacementPreviewStamp(PlacementEditTarget target, Texture2D source, bool tintSticker, out Texture2D texture, out string failureReason, out bool cacheHit, out bool cpuPixelSamplingUsed)
    {
        texture = null;
        failureReason = string.Empty;
        cacheHit = false;
        cpuPixelSamplingUsed = false;
        if (source == null)
        {
            failureReason = "missing placement source";
            return false;
        }

        var state = GetPlacementEditState(target);
        if (state == null)
        {
            failureReason = "invalid placement edit target";
            return false;
        }

        if (target == PlacementEditTarget.Decal && state.IsDefault)
        {
            texture = source;
            cacheHit = true;
            return true;
        }

        var key = BuildPlacementStampKey(target, source, tintSticker, state, "preview", PlacementEditPreviewMaxTextureSize);
        if (target == PlacementEditTarget.Decal
            && _editedDecalPreviewStampTexture != null
            && string.Equals(_editedDecalPreviewStampKey, key, StringComparison.Ordinal))
        {
            texture = _editedDecalPreviewStampTexture;
            cacheHit = true;
            cpuPixelSamplingUsed = true;
            return true;
        }

        if (target == PlacementEditTarget.Sticker
            && _editedStickerPreviewStampTexture != null
            && string.Equals(_editedStickerPreviewStampKey, key, StringComparison.Ordinal))
        {
            texture = _editedStickerPreviewStampTexture;
            cacheHit = true;
            cpuPixelSamplingUsed = true;
            return true;
        }

        if (!TryGenerateEditedPlacementStamp(target, source, tintSticker, state, PlacementEditPreviewMaxTextureSize, true, out var generated, out failureReason, out cpuPixelSamplingUsed))
        {
            return false;
        }

        if (target == PlacementEditTarget.Decal)
        {
            if (_editedDecalPreviewStampTexture != null)
            {
                Destroy(_editedDecalPreviewStampTexture);
            }

            _editedDecalPreviewStampTexture = generated;
            _editedDecalPreviewStampKey = key;
        }
        else
        {
            if (_editedStickerPreviewStampTexture != null)
            {
                Destroy(_editedStickerPreviewStampTexture);
            }

            _editedStickerPreviewStampTexture = generated;
            _editedStickerPreviewStampKey = key;
        }

        texture = generated;
        return true;
    }

    private bool TryUpdateFastDecalEditPreview(Texture2D source, PlacementEditState state, string reason, float startedAt, out string failureReason)
    {
        failureReason = string.Empty;
        if (source == null)
        {
            failureReason = "missing decal source";
            return false;
        }

        if (state == null)
        {
            failureReason = "missing decal edit state";
            return false;
        }

        if (state.IsDefault)
        {
            _placementEditPreviewImage.texture = source;
            _placementEditPreviewImage.color = Color.white;
            FitPlacementEditPreview(source);
            SetPlacementEditStatus("No temporary edits applied.");
            LogDecalEditPreviewFastCacheHit(source, state, source, reason, false, false, (Time.realtimeSinceStartup - startedAt) * 1000f);
            return true;
        }

        var previewMaxDimension = GetPlacementEditPreviewMaxDimension();
        var key = BuildPlacementStampKey(PlacementEditTarget.Decal, source, false, state, "modalPreview", previewMaxDimension);
        if (_editedDecalPreviewStampTexture != null && string.Equals(_editedDecalPreviewStampKey, key, StringComparison.Ordinal))
        {
            _placementEditPreviewImage.texture = _editedDecalPreviewStampTexture;
            _placementEditPreviewImage.color = Color.white;
            FitPlacementEditPreview(_editedDecalPreviewStampTexture);
            SetPlacementEditStatus("Temporary edit preview ready.");
            LogDecalEditPreviewFastCacheHit(source, state, _editedDecalPreviewStampTexture, reason, true, true, (Time.realtimeSinceStartup - startedAt) * 1000f);
            return true;
        }

        if (!TryGenerateFastDecalEditPreview(source, state, previewMaxDimension, out var previewTexture, out var sourceCacheHit, out var textureReused, out var elapsedMs, out failureReason))
        {
            return false;
        }

        _editedDecalPreviewStampKey = key;
        _placementEditPreviewImage.texture = previewTexture;
        _placementEditPreviewImage.color = Color.white;
        FitPlacementEditPreview(previewTexture);
        SetPlacementEditStatus("Temporary edit preview ready.");
        LogDecalEditPreviewFastUpdated(source, state, previewTexture, reason, sourceCacheHit, textureReused, elapsedMs);
        return true;
    }

    private int GetPlacementEditPreviewMaxDimension()
    {
        var frameSize = Vector2.zero;
        if (_placementEditPreviewFrameRect != null)
        {
            frameSize = _placementEditPreviewFrameRect.rect.size;
            if (frameSize.x <= 1f || frameSize.y <= 1f)
            {
                frameSize = _placementEditPreviewFrameRect.sizeDelta;
            }
        }

        var available = frameSize.x > 1f && frameSize.y > 1f
            ? Mathf.Min(frameSize.x - 16f, frameSize.y - 16f)
            : PlacementEditPreviewMaxTextureSize;
        return Mathf.Clamp(Mathf.RoundToInt(Mathf.Max(48f, available)), 64, PlacementEditPreviewMaxTextureSize);
    }

    private bool TryGenerateFastDecalEditPreview(
        Texture2D source,
        PlacementEditState state,
        int maxOutputDimension,
        out Texture2D texture,
        out bool sourceCacheHit,
        out bool textureReused,
        out float elapsedMs,
        out string failureReason)
    {
        var startedAt = Time.realtimeSinceStartup;
        texture = null;
        sourceCacheHit = false;
        textureReused = false;
        elapsedMs = 0f;
        failureReason = string.Empty;
        if (source == null || state == null)
        {
            failureReason = "missing decal edit source";
            return false;
        }

        if (!TryGetPlacementSourcePixels(PlacementEditTarget.Decal, source, out var pixels, out var width, out var height, out var cacheHit, out failureReason))
        {
            return false;
        }

        sourceCacheHit = cacheHit;
        if (!TryGenerateEditedDecalPixels(
                pixels,
                width,
                height,
                PlacementEditSnapshot.From(state),
                Mathf.Clamp(SystemInfo.maxTextureSize > 0 ? SystemInfo.maxTextureSize : 2048, 256, 8192),
                maxOutputDimension,
                _editedDecalPreviewPixelBuffer,
                out var outputPixels,
                out var outputWidth,
                out var outputHeight,
                out failureReason))
        {
            elapsedMs = (Time.realtimeSinceStartup - startedAt) * 1000f;
            return false;
        }

        _editedDecalPreviewPixelBuffer = outputPixels;
        if (_editedDecalPreviewStampTexture != null
            && _editedDecalPreviewStampTexture.width == outputWidth
            && _editedDecalPreviewStampTexture.height == outputHeight)
        {
            texture = _editedDecalPreviewStampTexture;
            textureReused = true;
        }
        else
        {
            if (_editedDecalPreviewStampTexture != null)
            {
                Destroy(_editedDecalPreviewStampTexture);
            }

            texture = new Texture2D(outputWidth, outputHeight, TextureFormat.RGBA32, false)
            {
                name = "DrawableSuitsEditedDecalPreview",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            _editedDecalPreviewStampTexture = texture;
        }

        texture.SetPixels32(outputPixels);
        texture.Apply(false, false);
        elapsedMs = (Time.realtimeSinceStartup - startedAt) * 1000f;
        return true;
    }

    private string BuildPlacementStampKey(PlacementEditTarget target, Texture2D source, bool tintSticker, PlacementEditState state, string quality, int maxDimension)
    {
        var colorKey = tintSticker ? ColorToHex(_brushColor) : "source";
        return $"{target}|quality={quality}|max={maxDimension}|source={source.name}|size={source.width}x{source.height}|state={state.Key()}|tint={tintSticker}:{colorKey}";
    }

    private bool TryGenerateEditedPlacementStamp(PlacementEditTarget target, Texture2D source, bool tintSticker, PlacementEditState state, int maxOutputDimension, bool preferCpuSourcePixels, out Texture2D texture, out string failureReason, out bool cpuPixelSamplingUsed)
    {
        texture = null;
        failureReason = string.Empty;
        cpuPixelSamplingUsed = false;
        if (source == null)
        {
            failureReason = "missing source texture";
            return false;
        }

        try
        {
            var minCropSize = Mathf.Max(1f / Mathf.Max(1, source.width), 1f / Mathf.Max(1, source.height), 0.01f);
            var crop = state.CropRect(minCropSize);
            var cropPixelsW = Mathf.Max(1, Mathf.RoundToInt(crop.width * source.width));
            var cropPixelsH = Mathf.Max(1, Mathf.RoundToInt(crop.height * source.height));
            var maxTextureSize = Mathf.Clamp(SystemInfo.maxTextureSize > 0 ? SystemInfo.maxTextureSize : 2048, 256, 8192);
            var targetWidth = Mathf.Max(1f, cropPixelsW * Mathf.Max(0.01f, state.StretchX));
            var targetHeight = Mathf.Max(1f, cropPixelsH * Mathf.Max(0.01f, state.StretchY));
            var outputScale = 1f;
            if (maxOutputDimension > 0)
            {
                outputScale = Mathf.Min(1f, maxOutputDimension / Mathf.Max(targetWidth, targetHeight));
            }

            var outputWidth = Mathf.Clamp(Mathf.RoundToInt(targetWidth * outputScale), 1, maxTextureSize);
            var outputHeight = Mathf.Clamp(Mathf.RoundToInt(targetHeight * outputScale), 1, maxTextureSize);
            var sourcePixels = default(Color32[]);
            var sourcePixelsWidth = 0;
            var sourcePixelsHeight = 0;
            if (preferCpuSourcePixels)
            {
                if (!TryGetPlacementSourcePixels(target, source, out sourcePixels, out sourcePixelsWidth, out sourcePixelsHeight, out _, out failureReason))
                {
                    return false;
                }

                cpuPixelSamplingUsed = true;
            }

            var pixels = new Color32[outputWidth * outputHeight];
            for (var y = 0; y < outputHeight; y++)
            {
                var v = (y + 0.5f) / Mathf.Max(1f, outputHeight);
                if (state.FlipY)
                {
                    v = 1f - v;
                }

                var sourceV = crop.yMin + v * crop.height;
                for (var x = 0; x < outputWidth; x++)
                {
                    var u = (x + 0.5f) / Mathf.Max(1f, outputWidth);
                    if (state.FlipX)
                    {
                        u = 1f - u;
                    }

                    var sourceU = crop.xMin + u * crop.width;
                    var color = cpuPixelSamplingUsed
                        ? SamplePlacementSourceBilinear(sourcePixels, sourcePixelsWidth, sourcePixelsHeight, sourceU, sourceV)
                        : source.GetPixelBilinear(Mathf.Clamp01(sourceU), Mathf.Clamp01(sourceV));
                    if (tintSticker)
                    {
                        color = new Color(_brushColor.r, _brushColor.g, _brushColor.b, color.a);
                    }

                    color = ApplyPlacementFilter(color, state);
                    pixels[y * outputWidth + x] = color;
                }
            }

            texture = new Texture2D(outputWidth, outputHeight, TextureFormat.RGBA32, false)
            {
                name = $"DrawableSuitsEdited{target}Stamp",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return true;
        }
        catch (Exception ex)
        {
            failureReason = ex.Message;
            DrawableSuitsDiagnostics.Exception($"PlacementEditedStampGenerated failed. target={target}; source={source.name}; size={source.width}x{source.height}", ex);
            return false;
        }
    }

    private static bool TryGenerateEditedDecalPixels(
        Color32[] sourcePixels,
        int sourceWidth,
        int sourceHeight,
        PlacementEditSnapshot state,
        int maxTextureSize,
        int maxOutputDimension,
        Color32[] reusableOutputPixels,
        out Color32[] outputPixels,
        out int outputWidth,
        out int outputHeight,
        out string failureReason)
    {
        outputPixels = null;
        outputWidth = 0;
        outputHeight = 0;
        failureReason = string.Empty;
        if (sourcePixels == null || sourceWidth <= 0 || sourceHeight <= 0 || sourcePixels.Length < sourceWidth * sourceHeight)
        {
            failureReason = "Decal source pixel data was invalid.";
            return false;
        }

        state ??= new PlacementEditSnapshot();
        var minCropSize = Math.Max(Math.Max(1.0 / Math.Max(1, sourceWidth), 1.0 / Math.Max(1, sourceHeight)), 0.01);
        var left = Clamp01Double(state.CropLeft);
        var right = Clamp01Double(state.CropRight);
        var bottom = Clamp01Double(state.CropBottom);
        var top = Clamp01Double(state.CropTop);
        if (left + right > 1.0 - minCropSize)
        {
            var scale = (1.0 - minCropSize) / Math.Max(0.0001, left + right);
            left *= scale;
            right *= scale;
        }
        if (bottom + top > 1.0 - minCropSize)
        {
            var scale = (1.0 - minCropSize) / Math.Max(0.0001, bottom + top);
            bottom *= scale;
            top *= scale;
        }

        var cropWidth = Math.Max(minCropSize, 1.0 - left - right);
        var cropHeight = Math.Max(minCropSize, 1.0 - bottom - top);
        var cropPixelsW = Math.Max(1, RoundPositiveToInt(cropWidth * sourceWidth));
        var cropPixelsH = Math.Max(1, RoundPositiveToInt(cropHeight * sourceHeight));
        maxTextureSize = ClampInt(maxTextureSize > 0 ? maxTextureSize : 2048, 256, 8192);
        var targetWidth = Math.Max(1.0, cropPixelsW * Math.Max(0.01, state.StretchX));
        var targetHeight = Math.Max(1.0, cropPixelsH * Math.Max(0.01, state.StretchY));
        var outputScale = 1.0;
        if (maxOutputDimension > 0)
        {
            outputScale = Math.Min(1.0, maxOutputDimension / Math.Max(targetWidth, targetHeight));
        }

        outputWidth = ClampInt(RoundPositiveToInt(targetWidth * outputScale), 1, maxTextureSize);
        outputHeight = ClampInt(RoundPositiveToInt(targetHeight * outputScale), 1, maxTextureSize);
        var pixelCount = outputWidth * outputHeight;
        outputPixels = reusableOutputPixels != null && reusableOutputPixels.Length == pixelCount
            ? reusableOutputPixels
            : new Color32[pixelCount];

        for (var y = 0; y < outputHeight; y++)
        {
            var v = (y + 0.5) / Math.Max(1.0, outputHeight);
            if (state.FlipY)
            {
                v = 1.0 - v;
            }

            var sourceV = bottom + v * cropHeight;
            for (var x = 0; x < outputWidth; x++)
            {
                var u = (x + 0.5) / Math.Max(1.0, outputWidth);
                if (state.FlipX)
                {
                    u = 1.0 - u;
                }

                var sourceU = left + u * cropWidth;
                var color = SamplePlacementSourceBilinearColor32(sourcePixels, sourceWidth, sourceHeight, sourceU, sourceV);
                outputPixels[(y * outputWidth) + x] = ApplyPlacementFilterToColor32(color, state);
            }
        }

        return true;
    }

    private static Color32 SamplePlacementSourceBilinearColor32(Color32[] pixels, int width, int height, double u, double v)
    {
        if (pixels == null || width <= 0 || height <= 0 || pixels.Length < width * height)
        {
            return new Color32(0, 0, 0, 0);
        }

        var x = Clamp01Double(u) * Math.Max(0, width - 1);
        var y = Clamp01Double(v) * Math.Max(0, height - 1);
        var x0 = ClampInt((int)Math.Floor(x), 0, width - 1);
        var y0 = ClampInt((int)Math.Floor(y), 0, height - 1);
        var x1 = Math.Min(width - 1, x0 + 1);
        var y1 = Math.Min(height - 1, y0 + 1);
        var tx = x - x0;
        var ty = y - y0;
        var c00 = pixels[(y0 * width) + x0];
        var c10 = pixels[(y0 * width) + x1];
        var c01 = pixels[(y1 * width) + x0];
        var c11 = pixels[(y1 * width) + x1];
        var r0 = LerpDouble(c00.r, c10.r, tx);
        var r1 = LerpDouble(c01.r, c11.r, tx);
        var g0 = LerpDouble(c00.g, c10.g, tx);
        var g1 = LerpDouble(c01.g, c11.g, tx);
        var b0 = LerpDouble(c00.b, c10.b, tx);
        var b1 = LerpDouble(c01.b, c11.b, tx);
        var a0 = LerpDouble(c00.a, c10.a, tx);
        var a1 = LerpDouble(c01.a, c11.a, tx);
        return new Color32(
            ByteFrom255(LerpDouble(r0, r1, ty)),
            ByteFrom255(LerpDouble(g0, g1, ty)),
            ByteFrom255(LerpDouble(b0, b1, ty)),
            ByteFrom255(LerpDouble(a0, a1, ty)));
    }

    private static Color32 ApplyPlacementFilterToColor32(Color32 color, PlacementEditSnapshot state)
    {
        if (state == null || !state.HasActiveFilters)
        {
            return color;
        }

        var r = color.r / 255.0;
        var g = color.g / 255.0;
        var b = color.b / 255.0;
        ApplyPlacementFilterAmount(ref r, ref g, ref b, state.GrayscaleAmount, GrayscaleR(r, g, b), GrayscaleR(r, g, b), GrayscaleR(r, g, b));
        ApplyPlacementFilterAmount(
            ref r,
            ref g,
            ref b,
            state.SepiaAmount,
            Clamp01Double(r * 0.393 + g * 0.769 + b * 0.189),
            Clamp01Double(r * 0.349 + g * 0.686 + b * 0.168),
            Clamp01Double(r * 0.272 + g * 0.534 + b * 0.131));
        ApplyPlacementFilterAmount(ref r, ref g, ref b, state.InvertAmount, 1.0 - r, 1.0 - g, 1.0 - b);
        ApplyPlacementFilterAmount(ref r, ref g, ref b, state.BrightnessAmount, r + 0.35, g + 0.35, b + 0.35);
        ApplyPlacementFilterAmount(
            ref r,
            ref g,
            ref b,
            state.ContrastAmount,
            (r - 0.5) * 2.25 + 0.5,
            (g - 0.5) * 2.25 + 0.5,
            (b - 0.5) * 2.25 + 0.5);
        var gray = GrayscaleR(r, g, b);
        ApplyPlacementFilterAmount(ref r, ref g, ref b, state.SaturationAmount, gray + (r - gray) * 2.75, gray + (g - gray) * 2.75, gray + (b - gray) * 2.75);
        if (state.HueShiftAmount > 0.0001f)
        {
            RgbToHsv(r, g, b, out var hue, out var saturation, out var value);
            HsvToRgb(Repeat01(hue + Clamp01Double(state.HueShiftAmount) * 0.5), saturation, value, out r, out g, out b);
        }

        return new Color32(ToByte01(r), ToByte01(g), ToByte01(b), color.a);
    }

    private static void ApplyPlacementFilterAmount(ref double r, ref double g, ref double b, float amount, double filteredR, double filteredG, double filteredB)
    {
        var t = Clamp01Double(amount);
        if (t <= 0.0001)
        {
            return;
        }

        r = Clamp01Double(LerpDouble(r, filteredR, t));
        g = Clamp01Double(LerpDouble(g, filteredG, t));
        b = Clamp01Double(LerpDouble(b, filteredB, t));
    }

    private static double GrayscaleR(double r, double g, double b)
    {
        return r * 0.299 + g * 0.587 + b * 0.114;
    }

    private static void RgbToHsv(double r, double g, double b, out double h, out double s, out double v)
    {
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        v = max;
        var delta = max - min;
        s = max <= 0.000001 ? 0.0 : delta / max;
        if (delta <= 0.000001)
        {
            h = 0.0;
            return;
        }

        if (Math.Abs(max - r) <= 0.000001)
        {
            h = ((g - b) / delta) % 6.0;
        }
        else if (Math.Abs(max - g) <= 0.000001)
        {
            h = ((b - r) / delta) + 2.0;
        }
        else
        {
            h = ((r - g) / delta) + 4.0;
        }

        h /= 6.0;
        h = Repeat01(h);
    }

    private static void HsvToRgb(double h, double s, double v, out double r, out double g, out double b)
    {
        h = Repeat01(h);
        s = Clamp01Double(s);
        v = Clamp01Double(v);
        var c = v * s;
        var x = c * (1.0 - Math.Abs((h * 6.0) % 2.0 - 1.0));
        var m = v - c;
        var segment = (int)Math.Floor(h * 6.0);
        switch (segment)
        {
            case 0:
                r = c; g = x; b = 0.0;
                break;
            case 1:
                r = x; g = c; b = 0.0;
                break;
            case 2:
                r = 0.0; g = c; b = x;
                break;
            case 3:
                r = 0.0; g = x; b = c;
                break;
            case 4:
                r = x; g = 0.0; b = c;
                break;
            default:
                r = c; g = 0.0; b = x;
                break;
        }

        r = Clamp01Double(r + m);
        g = Clamp01Double(g + m);
        b = Clamp01Double(b + m);
    }

    private static double Repeat01(double value)
    {
        value -= Math.Floor(value);
        return value < 0.0 ? value + 1.0 : value;
    }

    private static double Clamp01Double(double value)
    {
        if (value <= 0.0)
        {
            return 0.0;
        }

        return value >= 1.0 ? 1.0 : value;
    }

    private static int ClampInt(int value, int min, int max)
    {
        if (value < min)
        {
            return min;
        }

        return value > max ? max : value;
    }

    private static int RoundPositiveToInt(double value)
    {
        return (int)Math.Floor(Math.Max(0.0, value) + 0.5);
    }

    private static double LerpDouble(double a, double b, double t)
    {
        return a + (b - a) * t;
    }

    private static byte ByteFrom255(double value)
    {
        return (byte)ClampInt(RoundPositiveToInt(value), 0, 255);
    }

    private static byte ToByte01(double value)
    {
        return ByteFrom255(Clamp01Double(value) * 255.0);
    }

    private bool TryGetPlacementSourcePixels(PlacementEditTarget target, Texture2D source, out Color32[] pixels, out int width, out int height, out bool cacheHit, out string failureReason)
    {
        pixels = null;
        width = 0;
        height = 0;
        cacheHit = false;
        failureReason = string.Empty;
        if (source == null)
        {
            failureReason = "missing source texture";
            return false;
        }

        if (_placementSourcePixelCache != null
            && _placementSourcePixelCache.Target == target
            && ReferenceEquals(_placementSourcePixelCache.Source, source)
            && _placementSourcePixelCache.Width == source.width
            && _placementSourcePixelCache.Height == source.height
            && _placementSourcePixelCache.Pixels != null
            && _placementSourcePixelCache.Pixels.Length == source.width * source.height)
        {
            pixels = _placementSourcePixelCache.Pixels;
            width = _placementSourcePixelCache.Width;
            height = _placementSourcePixelCache.Height;
            cacheHit = true;
            LogPlacementSourcePixels(target, source, true, 0f);
            return true;
        }

        var startedAt = Time.realtimeSinceStartup;
        Color32[] loadedPixels;
        try
        {
            loadedPixels = source.GetPixels32();
        }
        catch (Exception ex)
        {
            failureReason = "Source texture pixels could not be read.";
            DrawableSuitsDiagnostics.Exception($"PlacementSourcePixelsCached failed. target={target}; source={source.name}; size={source.width}x{source.height}", ex);
            return false;
        }

        if (loadedPixels == null || loadedPixels.Length != source.width * source.height)
        {
            failureReason = "Source texture pixel data was invalid.";
            DrawableSuitsDiagnostics.Warn($"PlacementSourcePixelsCached failed. target={target}; source={source.name}; size={source.width}x{source.height}; pixels={loadedPixels?.Length ?? 0}");
            return false;
        }

        _placementSourcePixelCache = new PlacementSourcePixelCache
        {
            Target = target,
            Source = source,
            SourceName = source.name ?? string.Empty,
            Width = source.width,
            Height = source.height,
            Pixels = loadedPixels
        };

        pixels = loadedPixels;
        width = source.width;
        height = source.height;
        LogPlacementSourcePixels(target, source, false, (Time.realtimeSinceStartup - startedAt) * 1000f);
        return true;
    }

    private void ClearPlacementSourcePixelCache(PlacementEditTarget target, string reason)
    {
        if (_placementSourcePixelCache == null)
        {
            return;
        }

        if (target != PlacementEditTarget.None && _placementSourcePixelCache.Target != target)
        {
            return;
        }

        DrawableSuitsDiagnostics.Info($"PlacementSourcePixelsCacheCleared: target={_placementSourcePixelCache.Target}; source={_placementSourcePixelCache.SourceName}; size={_placementSourcePixelCache.Width}x{_placementSourcePixelCache.Height}; reason={reason}; suit={_selectedSuitId}");
        _placementSourcePixelCache = null;
    }

    private void LogPlacementSourcePixels(PlacementEditTarget target, Texture2D source, bool cacheHit, float elapsedMs)
    {
        var eventName = cacheHit ? "PlacementSourcePixelsCacheHit" : "PlacementSourcePixelsCached";
        var key = $"{eventName}|target={target}|source={source?.name}|size={source?.width ?? 0}x{source?.height ?? 0}";
        if (cacheHit && Time.unscaledTime - _lastPlacementSourcePixelsLogTime < 0.75f && string.Equals(key, _lastPlacementSourcePixelsLogKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastPlacementSourcePixelsLogTime = Time.unscaledTime;
        _lastPlacementSourcePixelsLogKey = key;
        DrawableSuitsDiagnostics.Info($"{eventName}: target={target}; source={GetPlacementEditSourceName(target)}; texture={source?.name}; size={source?.width ?? 0}x{source?.height ?? 0}; pixelCount={(source != null ? source.width * source.height : 0)}; elapsedMs={elapsedMs:0.##}; suit={_selectedSuitId}");
    }

    private static Color SamplePlacementSourceBilinear(Color32[] pixels, int width, int height, float u, float v)
    {
        if (pixels == null || width <= 0 || height <= 0 || pixels.Length < width * height)
        {
            return Color.clear;
        }

        var x = Mathf.Clamp01(u) * Mathf.Max(0, width - 1);
        var y = Mathf.Clamp01(v) * Mathf.Max(0, height - 1);
        var x0 = Mathf.Clamp(Mathf.FloorToInt(x), 0, width - 1);
        var y0 = Mathf.Clamp(Mathf.FloorToInt(y), 0, height - 1);
        var x1 = Mathf.Min(width - 1, x0 + 1);
        var y1 = Mathf.Min(height - 1, y0 + 1);
        var tx = x - x0;
        var ty = y - y0;
        var c00 = pixels[(y0 * width) + x0];
        var c10 = pixels[(y0 * width) + x1];
        var c01 = pixels[(y1 * width) + x0];
        var c11 = pixels[(y1 * width) + x1];
        var r0 = Mathf.Lerp(c00.r, c10.r, tx);
        var r1 = Mathf.Lerp(c01.r, c11.r, tx);
        var g0 = Mathf.Lerp(c00.g, c10.g, tx);
        var g1 = Mathf.Lerp(c01.g, c11.g, tx);
        var b0 = Mathf.Lerp(c00.b, c10.b, tx);
        var b1 = Mathf.Lerp(c01.b, c11.b, tx);
        var a0 = Mathf.Lerp(c00.a, c10.a, tx);
        var a1 = Mathf.Lerp(c01.a, c11.a, tx);
        return new Color(
            Mathf.Lerp(r0, r1, ty) / 255f,
            Mathf.Lerp(g0, g1, ty) / 255f,
            Mathf.Lerp(b0, b1, ty) / 255f,
            Mathf.Lerp(a0, a1, ty) / 255f);
    }

    private static Color ApplyPlacementFilter(Color color, PlacementEditState state)
    {
        if (state == null || !state.HasActiveFilters)
        {
            return color;
        }

        var result = new Color(color.r, color.g, color.b, 1f);
        ApplyPlacementFilterAmount(ref result, state.GrayscaleAmount, ToGrayscale(result));
        ApplyPlacementFilterAmount(ref result, state.SepiaAmount, ToSepia(result));
        ApplyPlacementFilterAmount(ref result, state.InvertAmount, new Color(1f - result.r, 1f - result.g, 1f - result.b, 1f));
        ApplyPlacementFilterAmount(ref result, state.BrightnessAmount, new Color(result.r + 0.35f, result.g + 0.35f, result.b + 0.35f, 1f));
        ApplyPlacementFilterAmount(ref result, state.ContrastAmount, AdjustContrast(result, 2.25f));
        ApplyPlacementFilterAmount(ref result, state.SaturationAmount, AdjustSaturation(result, 2.75f));
        if (state.HueShiftAmount > 0.0001f)
        {
            result = ShiftHue(result, Mathf.Clamp01(state.HueShiftAmount) * 0.5f);
        }

        return new Color(Mathf.Clamp01(result.r), Mathf.Clamp01(result.g), Mathf.Clamp01(result.b), color.a);
    }

    private static void ApplyPlacementFilterAmount(ref Color color, float amount, Color filtered)
    {
        amount = Mathf.Clamp01(amount);
        if (amount <= 0.0001f)
        {
            return;
        }

        var result = Color.Lerp(color, filtered, amount);
        color = new Color(Mathf.Clamp01(result.r), Mathf.Clamp01(result.g), Mathf.Clamp01(result.b), 1f);
    }

    private static Color ToGrayscale(Color color)
    {
        var gray = color.r * 0.299f + color.g * 0.587f + color.b * 0.114f;
        return new Color(gray, gray, gray, 1f);
    }

    private static Color ToSepia(Color color)
    {
        return new Color(
            Mathf.Clamp01(color.r * 0.393f + color.g * 0.769f + color.b * 0.189f),
            Mathf.Clamp01(color.r * 0.349f + color.g * 0.686f + color.b * 0.168f),
            Mathf.Clamp01(color.r * 0.272f + color.g * 0.534f + color.b * 0.131f),
            1f);
    }

    private static Color AdjustContrast(Color color, float factor)
    {
        return new Color(
            Mathf.Clamp01((color.r - 0.5f) * factor + 0.5f),
            Mathf.Clamp01((color.g - 0.5f) * factor + 0.5f),
            Mathf.Clamp01((color.b - 0.5f) * factor + 0.5f),
            1f);
    }

    private static Color AdjustSaturation(Color color, float factor)
    {
        var gray = ToGrayscale(color);
        return new Color(
            Mathf.Clamp01(gray.r + (color.r - gray.r) * factor),
            Mathf.Clamp01(gray.g + (color.g - gray.g) * factor),
            Mathf.Clamp01(gray.b + (color.b - gray.b) * factor),
            1f);
    }

    private static Color ShiftHue(Color color, float amount)
    {
        Color.RGBToHSV(color, out var hue, out var saturation, out var value);
        hue = Mathf.Repeat(hue + amount, 1f);
        return Color.HSVToRGB(hue, saturation, value);
    }

    private bool TryGetTextStampTexture(out Texture2D texture, out string failureReason)
    {
        texture = null;
        var text = NormalizeTextStampValue(_textStampValue);
        if (string.IsNullOrWhiteSpace(text))
        {
            failureReason = "empty text";
            return false;
        }

        _textStampRenderer ??= new TextStampRenderer();
        texture = _textStampRenderer.GetOrRender(text, out _textStampTextureKey, out failureReason);
        _textStampTexture = texture;
        return texture != null;
    }

    private bool TryGetStickerStampTexture(StickerShape shape, out Texture2D texture, out string failureReason)
    {
        failureReason = string.Empty;
        if (_stickerStampTextures.TryGetValue(shape, out texture) && texture != null)
        {
            return true;
        }

        texture = GenerateStickerStampTexture(shape);
        if (texture == null)
        {
            failureReason = $"failed to generate {StickerShapeDisplayName(shape)}";
            return false;
        }

        _stickerStampTextures[shape] = texture;
        DrawableSuitsDiagnostics.Info($"StickerStampGenerated: shape={shape}; display={StickerShapeDisplayName(shape)}; texture={texture.width}x{texture.height}");
        return true;
    }

    private static Texture2D GenerateStickerStampTexture(StickerShape shape)
    {
        const int size = 256;
        const int samplesPerAxis = 3;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = $"DrawableSuitsSticker_{shape}",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };

        var pixels = new Color32[size * size];
        var sampleCount = samplesPerAxis * samplesPerAxis;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var covered = 0;
                for (var sy = 0; sy < samplesPerAxis; sy++)
                {
                    for (var sx = 0; sx < samplesPerAxis; sx++)
                    {
                        var px = ((x + (sx + 0.5f) / samplesPerAxis) / size) * 2f - 1f;
                        var py = ((y + (sy + 0.5f) / samplesPerAxis) / size) * 2f - 1f;
                        if (IsInsideStickerShape(shape, new Vector2(px, py)))
                        {
                            covered++;
                        }
                    }
                }

                var alpha = Mathf.RoundToInt(255f * covered / sampleCount);
                pixels[(y * size) + x] = new Color32(255, 255, 255, (byte)alpha);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        return texture;
    }

    private static bool IsInsideStickerShape(StickerShape shape, Vector2 p)
    {
        var ax = Mathf.Abs(p.x);
        var ay = Mathf.Abs(p.y);
        switch (shape)
        {
            case StickerShape.Circle:
                return p.sqrMagnitude <= 0.82f * 0.82f;
            case StickerShape.Square:
                return ax <= 0.78f && ay <= 0.78f;
            case StickerShape.Triangle:
                return PointInPolygon(p, StickerTriangleVertices);
            case StickerShape.Diamond:
                return ax + ay <= 1.05f;
            case StickerShape.Star:
                return IsInsideStar(p);
            case StickerShape.Heart:
                return IsInsideHeart(p);
            case StickerShape.Arrow:
                return PointInPolygon(p, StickerArrowVertices);
            case StickerShape.LightningBolt:
                return PointInPolygon(p, StickerLightningVertices);
            case StickerShape.Plus:
                return (ax <= 0.22f && ay <= 0.8f) || (ay <= 0.22f && ax <= 0.8f);
            case StickerShape.Ring:
            {
                var distance = p.magnitude;
                return distance >= 0.52f && distance <= 0.82f;
            }
            case StickerShape.Crescent:
            {
                var outer = (p + new Vector2(0.12f, 0f)).sqrMagnitude <= 0.78f * 0.78f;
                var inner = (p - new Vector2(0.22f, 0.08f)).sqrMagnitude <= 0.72f * 0.72f;
                return outer && !inner;
            }
            case StickerShape.Shield:
                return PointInPolygon(p, StickerShieldVertices);
            default:
                return false;
        }
    }

    private static bool IsInsideStar(Vector2 p)
    {
        var distance = p.magnitude;
        if (distance > 0.9f)
        {
            return false;
        }

        if (distance < 0.26f)
        {
            return true;
        }

        var angle = Mathf.Atan2(p.y, p.x) - Mathf.PI * 0.5f;
        var sector = Mathf.PI * 2f / 5f;
        var local = Mathf.Abs(Mathf.Repeat(angle + sector * 0.5f, sector) - sector * 0.5f);
        var limit = Mathf.Lerp(0.9f, 0.36f, local / (sector * 0.5f));
        return distance <= limit;
    }

    private static bool IsInsideHeart(Vector2 p)
    {
        var x = p.x * 1.15f;
        var y = (p.y + 0.08f) * 1.18f;
        var a = (x * x) + (y * y) - 0.58f;
        return (a * a * a) - (x * x * y * y * y) <= 0f;
    }

    private static bool PointInPolygon(Vector2 point, Vector2[] polygon)
    {
        var inside = false;
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
        {
            var pi = polygon[i];
            var pj = polygon[j];
            var denominator = pj.y - pi.y;
            if (Mathf.Abs(denominator) < 0.0001f)
            {
                denominator = denominator < 0f ? -0.0001f : 0.0001f;
            }
            if (((pi.y > point.y) != (pj.y > point.y))
                && point.x < (pj.x - pi.x) * (point.y - pi.y) / denominator + pi.x)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private void InvalidateTextStampTexture(string reason)
    {
        _textStampTextureKey = string.Empty;
        _textStampTexture = null;
        DrawableSuitsDiagnostics.Info($"TextStampTexture invalidated. reason={reason}; textLength={NormalizeTextStampValue(_textStampValue).Length}; color={ColorToHex(_brushColor)}");
    }

    private static string NormalizeTextStampValue(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var normalized = value.Replace('\r', ' ').Replace('\n', ' ');
        return normalized.Length <= 64 ? normalized : normalized.Substring(0, 64);
    }

    private void LogPlacementPreviewUpdated(string mode, Texture2D texture, Vector2 uv, MirrorPaintTarget mirrorTarget, Texture2D stampTexture, bool force)
    {
        if (_tool == EditorTool.Text)
        {
            LogTextPreviewUpdated(mode, texture, uv, mirrorTarget, stampTexture, force);
            return;
        }
        if (_tool == EditorTool.Sticker)
        {
            LogStickerPreviewUpdated(mode, texture, uv, mirrorTarget, stampTexture, force);
            return;
        }

        LogDecalPreviewUpdated(mode, texture, uv, mirrorTarget, force);
    }

    private void LogDecalPreviewUpdated(string mode, Texture2D texture, Vector2 uv, MirrorPaintTarget mirrorTarget, bool force)
    {
        if (texture == null)
        {
            return;
        }

        var px = Mathf.RoundToInt(uv.x * (texture.width - 1));
        var py = Mathf.RoundToInt(uv.y * (texture.height - 1));
        var key = $"updated|mode={mode}|pixel={px},{py}|mirror={DescribeMirrorTarget(texture, mirrorTarget)}|size={Mathf.RoundToInt(_decalSize)}|rotation={Mathf.RoundToInt(_decalRotation)}|opacity={_brushOpacity:0.##}|decal={CurrentDecalName()}|preview={_decalPreviewTexture?.width ?? 0}x{_decalPreviewTexture?.height ?? 0}";
        if (!force && Time.unscaledTime - _lastDecalPreviewLogTime < 0.5f && string.Equals(key, _lastDecalPreviewLogKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastDecalPreviewLogTime = Time.unscaledTime;
        _lastDecalPreviewLogKey = key;
        DrawableSuitsDiagnostics.Info($"DecalPreviewUpdated: {key}; uv={uv}; pointerSource={_pointerSource}; cursor={_cursor}");
    }

    private void LogDecalSurfacePreviewUpdated(Texture2D texture, RaycastHit hit, MirrorPaintTarget mirrorTarget, Texture2D stampTexture, TextSurfaceStampStats primaryStats, TextSurfaceStampStats mirrorStats)
    {
        if (texture == null || stampTexture == null)
        {
            return;
        }

        var pixel = TexturePixel(texture, hit.textureCoord);
        var key = $"surfacePreview|pixel={pixel.x},{pixel.y}|mirror={DescribeMirrorTarget(texture, mirrorTarget)}|decal={CurrentDecalName()}|size={Mathf.RoundToInt(_decalSize)}|rotation={Mathf.RoundToInt(_decalRotation)}|opacity={_brushOpacity:0.##}|stampTexture={stampTexture.width}x{stampTexture.height}|primaryWritten={primaryStats.WrittenPixels}|mirrorWritten={mirrorStats.WrittenPixels}|primaryCells={primaryStats.RasterizedCells}|mirrorCells={mirrorStats.RasterizedCells}|primarySeams={primaryStats.SeamSkippedCells}|mirrorSeams={mirrorStats.SeamSkippedCells}";
        if (Time.unscaledTime - _lastDecalPreviewLogTime < 0.5f && string.Equals(key, _lastDecalPreviewLogKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastDecalPreviewLogTime = Time.unscaledTime;
        _lastDecalPreviewLogKey = key;
        DrawableSuitsDiagnostics.Info($"DecalSurfacePreviewUpdated: {key}; hitPoint={hit.point}; hitNormal={hit.normal}; primarySamples={primaryStats.ProjectionSamples}; primaryHits={primaryStats.SurfaceHits}; primaryAlpha={primaryStats.AlphaPixels}; primarySkipped={primaryStats.SkippedPixels}; primaryOffSuit={primaryStats.OffSuitSamples}; primaryWorldSize={primaryStats.WorldWidth:0.###}x{primaryStats.WorldHeight:0.###}; mirrorSamples={mirrorStats.ProjectionSamples}; mirrorHits={mirrorStats.SurfaceHits}; mirrorAlpha={mirrorStats.AlphaPixels}; mirrorSkipped={mirrorStats.SkippedPixels}; mirrorOffSuit={mirrorStats.OffSuitSamples}; mirrorWorldSize={mirrorStats.WorldWidth:0.###}x{mirrorStats.WorldHeight:0.###}; pointerSource={_pointerSource}; cursor={_cursor}");
    }

    private void LogDecalSurfacePreviewSkipped(Texture2D texture, RaycastHit hit, MirrorPaintTarget mirrorTarget, Texture2D stampTexture, TextSurfaceStampStats primaryStats, TextSurfaceStampStats mirrorStats)
    {
        if (texture == null)
        {
            return;
        }

        var pixel = TexturePixel(texture, hit.textureCoord);
        var key = $"surfacePreviewSkipped|pixel={pixel.x},{pixel.y}|mirror={DescribeMirrorTarget(texture, mirrorTarget)}|decal={CurrentDecalName()}|stampTexture={stampTexture?.width ?? 0}x{stampTexture?.height ?? 0}|primarySkipped={primaryStats.SkippedPixels}|mirrorSkipped={mirrorStats.SkippedPixels}|primaryCells={primaryStats.RasterizedCells}|mirrorCells={mirrorStats.RasterizedCells}";
        if (Time.unscaledTime - _lastDecalPreviewLogTime < 0.75f && string.Equals(key, _lastDecalPreviewLogKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastDecalPreviewLogTime = Time.unscaledTime;
        _lastDecalPreviewLogKey = key;
        DrawableSuitsDiagnostics.Info($"DecalSurfacePreviewSkipped: {key}; hitPoint={hit.point}; hitNormal={hit.normal}; primarySamples={primaryStats.ProjectionSamples}; primaryHits={primaryStats.SurfaceHits}; primaryOffSuit={primaryStats.OffSuitSamples}; primarySeams={primaryStats.SeamSkippedCells}; mirrorSamples={mirrorStats.ProjectionSamples}; mirrorHits={mirrorStats.SurfaceHits}; mirrorOffSuit={mirrorStats.OffSuitSamples}; mirrorSeams={mirrorStats.SeamSkippedCells}; pointerSource={_pointerSource}; cursor={_cursor}");
    }

    private void LogTextPreviewUpdated(string mode, Texture2D texture, Vector2 uv, MirrorPaintTarget mirrorTarget, Texture2D stampTexture, bool force)
    {
        if (texture == null || stampTexture == null)
        {
            return;
        }

        var px = Mathf.RoundToInt(uv.x * (texture.width - 1));
        var py = Mathf.RoundToInt(uv.y * (texture.height - 1));
        var stampSize = GetPlacementStampPixelSize(stampTexture);
        var key = $"updated|mode={mode}|pixel={px},{py}|mirror={DescribeMirrorTarget(texture, mirrorTarget)}|textLength={NormalizeTextStampValue(_textStampValue).Length}|fontSize={Mathf.RoundToInt(_textSize)}|rotation={Mathf.RoundToInt(_textRotation)}|color={ColorToHex(_brushColor)}|opacity={_brushOpacity:0.##}|stampTexture={stampTexture.width}x{stampTexture.height}|stampSize={Mathf.RoundToInt(stampSize.x)}x{Mathf.RoundToInt(stampSize.y)}";
        if (!force && Time.unscaledTime - _lastTextPreviewLogTime < 0.5f && string.Equals(key, _lastTextPreviewLogKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastTextPreviewLogTime = Time.unscaledTime;
        _lastTextPreviewLogKey = key;
        DrawableSuitsDiagnostics.Info($"TextPreviewUpdated: {key}; uv={uv}; pointerSource={_pointerSource}; cursor={_cursor}");
    }

    private void LogTextSurfacePreviewUpdated(Texture2D texture, RaycastHit hit, MirrorPaintTarget mirrorTarget, Texture2D stampTexture, TextSurfaceStampStats primaryStats, TextSurfaceStampStats mirrorStats)
    {
        if (texture == null || stampTexture == null)
        {
            return;
        }

        var pixel = TexturePixel(texture, hit.textureCoord);
        var key = $"surfacePreview|pixel={pixel.x},{pixel.y}|mirror={DescribeMirrorTarget(texture, mirrorTarget)}|textLength={NormalizeTextStampValue(_textStampValue).Length}|fontSize={Mathf.RoundToInt(_textSize)}|rotation={Mathf.RoundToInt(_textRotation)}|color={ColorToHex(_brushColor)}|opacity={_brushOpacity:0.##}|stampTexture={stampTexture.width}x{stampTexture.height}|primaryWritten={primaryStats.WrittenPixels}|mirrorWritten={mirrorStats.WrittenPixels}";
        if (Time.unscaledTime - _lastTextPreviewLogTime < 0.5f && string.Equals(key, _lastTextPreviewLogKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastTextPreviewLogTime = Time.unscaledTime;
        _lastTextPreviewLogKey = key;
        DrawableSuitsDiagnostics.Info($"TextSurfacePreviewUpdated: {key}; hitPoint={hit.point}; hitNormal={hit.normal}; primaryAlpha={primaryStats.AlphaPixels}; primarySkipped={primaryStats.SkippedPixels}; primaryWorldSize={primaryStats.WorldWidth:0.###}x{primaryStats.WorldHeight:0.###}; mirrorAlpha={mirrorStats.AlphaPixels}; mirrorSkipped={mirrorStats.SkippedPixels}; mirrorWorldSize={mirrorStats.WorldWidth:0.###}x{mirrorStats.WorldHeight:0.###}; pointerSource={_pointerSource}; cursor={_cursor}");
    }

    private void LogTextSurfacePreviewSkipped(Texture2D texture, RaycastHit hit, MirrorPaintTarget mirrorTarget, Texture2D stampTexture, TextSurfaceStampStats primaryStats, TextSurfaceStampStats mirrorStats)
    {
        if (texture == null)
        {
            return;
        }

        var pixel = TexturePixel(texture, hit.textureCoord);
        var key = $"surfacePreviewSkipped|pixel={pixel.x},{pixel.y}|mirror={DescribeMirrorTarget(texture, mirrorTarget)}|textLength={NormalizeTextStampValue(_textStampValue).Length}|stampTexture={stampTexture?.width ?? 0}x{stampTexture?.height ?? 0}|primarySkipped={primaryStats.SkippedPixels}|mirrorSkipped={mirrorStats.SkippedPixels}";
        if (Time.unscaledTime - _lastTextPreviewLogTime < 0.75f && string.Equals(key, _lastTextPreviewLogKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastTextPreviewLogTime = Time.unscaledTime;
        _lastTextPreviewLogKey = key;
        DrawableSuitsDiagnostics.Info($"TextSurfacePreviewSkipped: {key}; hitPoint={hit.point}; hitNormal={hit.normal}; pointerSource={_pointerSource}; cursor={_cursor}");
    }

    private void LogStickerPreviewUpdated(string mode, Texture2D texture, Vector2 uv, MirrorPaintTarget mirrorTarget, Texture2D stampTexture, bool force)
    {
        if (texture == null || stampTexture == null)
        {
            return;
        }

        var px = Mathf.RoundToInt(uv.x * (texture.width - 1));
        var py = Mathf.RoundToInt(uv.y * (texture.height - 1));
        var key = $"updated|mode={mode}|pixel={px},{py}|mirror={DescribeMirrorTarget(texture, mirrorTarget)}|shape={_stickerShape}|size={Mathf.RoundToInt(_stickerSize)}|rotation={Mathf.RoundToInt(_stickerRotation)}|color={ColorToHex(_brushColor)}|opacity={_brushOpacity:0.##}|stampTexture={stampTexture.width}x{stampTexture.height}";
        if (!force && Time.unscaledTime - _lastStickerPreviewLogTime < 0.5f && string.Equals(key, _lastStickerPreviewLogKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastStickerPreviewLogTime = Time.unscaledTime;
        _lastStickerPreviewLogKey = key;
        DrawableSuitsDiagnostics.Info($"StickerPreviewUpdated: {key}; uv={uv}; pointerSource={_pointerSource}; cursor={_cursor}");
    }

    private void LogStickerSurfacePreviewUpdated(Texture2D texture, RaycastHit hit, MirrorPaintTarget mirrorTarget, Texture2D stampTexture, TextSurfaceStampStats primaryStats, TextSurfaceStampStats mirrorStats)
    {
        if (texture == null || stampTexture == null)
        {
            return;
        }

        var pixel = TexturePixel(texture, hit.textureCoord);
        var key = $"surfacePreview|pixel={pixel.x},{pixel.y}|mirror={DescribeMirrorTarget(texture, mirrorTarget)}|shape={_stickerShape}|size={Mathf.RoundToInt(_stickerSize)}|rotation={Mathf.RoundToInt(_stickerRotation)}|color={ColorToHex(_brushColor)}|opacity={_brushOpacity:0.##}|stampTexture={stampTexture.width}x{stampTexture.height}|primaryWritten={primaryStats.WrittenPixels}|mirrorWritten={mirrorStats.WrittenPixels}|primaryCells={primaryStats.RasterizedCells}|mirrorCells={mirrorStats.RasterizedCells}|primarySeams={primaryStats.SeamSkippedCells}|mirrorSeams={mirrorStats.SeamSkippedCells}";
        if (Time.unscaledTime - _lastStickerPreviewLogTime < 0.5f && string.Equals(key, _lastStickerPreviewLogKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastStickerPreviewLogTime = Time.unscaledTime;
        _lastStickerPreviewLogKey = key;
        DrawableSuitsDiagnostics.Info($"StickerPreviewUpdated: {key}; hitPoint={hit.point}; hitNormal={hit.normal}; primarySamples={primaryStats.ProjectionSamples}; primaryHits={primaryStats.SurfaceHits}; primaryAlpha={primaryStats.AlphaPixels}; primarySkipped={primaryStats.SkippedPixels}; primaryOffSuit={primaryStats.OffSuitSamples}; primaryWorldSize={primaryStats.WorldWidth:0.###}x{primaryStats.WorldHeight:0.###}; mirrorSamples={mirrorStats.ProjectionSamples}; mirrorHits={mirrorStats.SurfaceHits}; mirrorAlpha={mirrorStats.AlphaPixels}; mirrorSkipped={mirrorStats.SkippedPixels}; mirrorOffSuit={mirrorStats.OffSuitSamples}; mirrorWorldSize={mirrorStats.WorldWidth:0.###}x{mirrorStats.WorldHeight:0.###}; pointerSource={_pointerSource}; cursor={_cursor}");
    }

    private void LogStickerSurfacePreviewSkipped(Texture2D texture, RaycastHit hit, MirrorPaintTarget mirrorTarget, Texture2D stampTexture, TextSurfaceStampStats primaryStats, TextSurfaceStampStats mirrorStats, string reason)
    {
        var pixel = texture != null ? TexturePixel(texture, hit.textureCoord) : new Vector2Int(-1, -1);
        var key = $"surfacePreviewSkipped|reason={reason}|pixel={pixel.x},{pixel.y}|mirror={DescribeMirrorTarget(texture, mirrorTarget)}|shape={_stickerShape}|stampTexture={stampTexture?.width ?? 0}x{stampTexture?.height ?? 0}|primarySkipped={primaryStats.SkippedPixels}|mirrorSkipped={mirrorStats.SkippedPixels}|primaryCells={primaryStats.RasterizedCells}|mirrorCells={mirrorStats.RasterizedCells}";
        if (Time.unscaledTime - _lastStickerPreviewLogTime < 0.75f && string.Equals(key, _lastStickerPreviewLogKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastStickerPreviewLogTime = Time.unscaledTime;
        _lastStickerPreviewLogKey = key;
        DrawableSuitsDiagnostics.Info($"StickerPreviewHidden: {key}; hitPoint={hit.point}; hitNormal={hit.normal}; primarySamples={primaryStats.ProjectionSamples}; primaryHits={primaryStats.SurfaceHits}; primaryOffSuit={primaryStats.OffSuitSamples}; primarySeams={primaryStats.SeamSkippedCells}; mirrorSamples={mirrorStats.ProjectionSamples}; mirrorHits={mirrorStats.SurfaceHits}; mirrorOffSuit={mirrorStats.OffSuitSamples}; mirrorSeams={mirrorStats.SeamSkippedCells}; pointerSource={_pointerSource}; cursor={_cursor}");
    }

    private void LogPlacementPreviewHidden(string reason, bool force)
    {
        if (_placementPreviewTool == EditorTool.Text || _tool == EditorTool.Text)
        {
            LogTextPreviewHidden(reason, force);
            return;
        }
        if (_placementPreviewTool == EditorTool.Sticker || _tool == EditorTool.Sticker)
        {
            LogStickerPreviewHidden(reason, force);
            return;
        }

        LogDecalPreviewHidden(reason, force);
    }

    private void LogDecalPreviewHidden(string reason, bool force)
    {
        var key = $"hidden|reason={reason}|mode={_previewMode}|decal={CurrentDecalName()}";
        if (!force && Time.unscaledTime - _lastDecalPreviewLogTime < 0.75f && string.Equals(key, _lastDecalPreviewLogKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastDecalPreviewLogTime = Time.unscaledTime;
        _lastDecalPreviewLogKey = key;
        DrawableSuitsDiagnostics.Info($"DecalPreviewHidden: {key}; pointerSource={_pointerSource}; cursor={_cursor}");
    }

    private void LogTextPreviewHidden(string reason, bool force)
    {
        var key = $"hidden|reason={reason}|mode={_previewMode}|textLength={NormalizeTextStampValue(_textStampValue).Length}";
        if (!force && Time.unscaledTime - _lastTextPreviewLogTime < 0.75f && string.Equals(key, _lastTextPreviewLogKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastTextPreviewLogTime = Time.unscaledTime;
        _lastTextPreviewLogKey = key;
        DrawableSuitsDiagnostics.Info($"TextPreviewHidden: {key}; pointerSource={_pointerSource}; cursor={_cursor}");
    }

    private void LogStickerPreviewHidden(string reason, bool force)
    {
        var key = $"hidden|reason={reason}|mode={_previewMode}|shape={_stickerShape}|size={Mathf.RoundToInt(_stickerSize)}|rotation={Mathf.RoundToInt(_stickerRotation)}";
        if (!force && Time.unscaledTime - _lastStickerPreviewLogTime < 0.75f && string.Equals(key, _lastStickerPreviewLogKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastStickerPreviewLogTime = Time.unscaledTime;
        _lastStickerPreviewLogKey = key;
        DrawableSuitsDiagnostics.Info($"StickerPreviewHidden: {key}; pointerSource={_pointerSource}; cursor={_cursor}");
    }

    private void LogDecalStampCommitted(string mode, Texture2D texture, Vector2 uv, MirrorPaintTarget mirrorTarget)
    {
        if (texture == null)
        {
            return;
        }

        var px = Mathf.RoundToInt(uv.x * (texture.width - 1));
        var py = Mathf.RoundToInt(uv.y * (texture.height - 1));
        DrawableSuitsDiagnostics.Info($"DecalStampCommitted: mode={mode}; pointerSource={_pointerSource}; uv={uv}; pixel={px},{py}; {DescribeMirrorTarget(texture, mirrorTarget)}; decal={CurrentDecalName()}; size={Mathf.RoundToInt(_decalSize)}; rotation={Mathf.RoundToInt(_decalRotation)}; opacity={_brushOpacity:0.##}; previewTexture={_decalPreviewTexture?.width ?? 0}x{_decalPreviewTexture?.height ?? 0}");
    }

    private void LogDecalSurfaceStampCommitted(Texture2D texture, RaycastHit hit, MirrorPaintTarget mirrorTarget, Texture2D stampTexture, TextSurfaceStampStats primaryStats, TextSurfaceStampStats mirrorStats)
    {
        if (texture == null)
        {
            return;
        }

        var pixel = TexturePixel(texture, hit.textureCoord);
        DrawableSuitsDiagnostics.Info($"DecalSurfaceStampCommitted: mode=WorldThirdPerson; pointerSource={_pointerSource}; uv={hit.textureCoord}; pixel={pixel.x},{pixel.y}; {DescribeMirrorTarget(texture, mirrorTarget)}; decal={CurrentDecalName()}; size={Mathf.RoundToInt(_decalSize)}; rotation={Mathf.RoundToInt(_decalRotation)}; opacity={_brushOpacity:0.##}; stampTexture={stampTexture?.width ?? 0}x{stampTexture?.height ?? 0}; hitPoint={hit.point}; hitNormal={hit.normal}; primarySamples={primaryStats.ProjectionSamples}; primaryHits={primaryStats.SurfaceHits}; primaryCells={primaryStats.RasterizedCells}; primarySeams={primaryStats.SeamSkippedCells}; primaryWritten={primaryStats.WrittenPixels}; primarySkipped={primaryStats.SkippedPixels}; primaryOffSuit={primaryStats.OffSuitSamples}; primaryAlpha={primaryStats.AlphaPixels}; primaryWorldSize={primaryStats.WorldWidth:0.###}x{primaryStats.WorldHeight:0.###}; mirrorSamples={mirrorStats.ProjectionSamples}; mirrorHits={mirrorStats.SurfaceHits}; mirrorCells={mirrorStats.RasterizedCells}; mirrorSeams={mirrorStats.SeamSkippedCells}; mirrorWritten={mirrorStats.WrittenPixels}; mirrorSkipped={mirrorStats.SkippedPixels}; mirrorOffSuit={mirrorStats.OffSuitSamples}; mirrorAlpha={mirrorStats.AlphaPixels}; mirrorWorldSize={mirrorStats.WorldWidth:0.###}x{mirrorStats.WorldHeight:0.###}");
    }

    private void LogDecalSurfaceStampSkipped(string mode, string reason, Texture2D texture, RaycastHit hit, MirrorPaintTarget mirrorTarget, Texture2D stampTexture, TextSurfaceStampStats primaryStats, TextSurfaceStampStats mirrorStats)
    {
        var pixel = texture != null ? TexturePixel(texture, hit.textureCoord) : new Vector2Int(-1, -1);
        var key = $"surfaceStampSkipped|mode={mode}|reason={reason}|pixel={pixel.x},{pixel.y}|mirror={DescribeMirrorTarget(texture, mirrorTarget)}|decal={CurrentDecalName()}|stampTexture={stampTexture?.width ?? 0}x{stampTexture?.height ?? 0}|primaryWritten={primaryStats.WrittenPixels}|mirrorWritten={mirrorStats.WrittenPixels}|primaryCells={primaryStats.RasterizedCells}|mirrorCells={mirrorStats.RasterizedCells}";
        if (Time.unscaledTime - _lastDecalPreviewLogTime < 0.75f && string.Equals(key, _lastDecalPreviewLogKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastDecalPreviewLogTime = Time.unscaledTime;
        _lastDecalPreviewLogKey = key;
        DrawableSuitsDiagnostics.Info($"DecalSurfaceStampSkipped: {key}; hitPoint={hit.point}; hitNormal={hit.normal}; primarySamples={primaryStats.ProjectionSamples}; primaryHits={primaryStats.SurfaceHits}; primarySkipped={primaryStats.SkippedPixels}; primaryOffSuit={primaryStats.OffSuitSamples}; primarySeams={primaryStats.SeamSkippedCells}; mirrorSamples={mirrorStats.ProjectionSamples}; mirrorHits={mirrorStats.SurfaceHits}; mirrorSkipped={mirrorStats.SkippedPixels}; mirrorOffSuit={mirrorStats.OffSuitSamples}; mirrorSeams={mirrorStats.SeamSkippedCells}; pointerSource={_pointerSource}; cursor={_cursor}");
    }

    private void LogTextStampCommitted(string mode, Texture2D texture, Vector2 uv, MirrorPaintTarget mirrorTarget, Texture2D stampTexture)
    {
        if (texture == null)
        {
            return;
        }

        var px = Mathf.RoundToInt(uv.x * (texture.width - 1));
        var py = Mathf.RoundToInt(uv.y * (texture.height - 1));
        var stampSize = stampTexture != null ? GetPlacementStampPixelSize(stampTexture) : Vector2.zero;
        DrawableSuitsDiagnostics.Info($"TextStampCommitted: mode={mode}; pointerSource={_pointerSource}; uv={uv}; pixel={px},{py}; {DescribeMirrorTarget(texture, mirrorTarget)}; textLength={NormalizeTextStampValue(_textStampValue).Length}; fontSize={Mathf.RoundToInt(_textSize)}; rotation={Mathf.RoundToInt(_textRotation)}; color={ColorToHex(_brushColor)}; opacity={_brushOpacity:0.##}; generatedTexture={stampTexture?.width ?? 0}x{stampTexture?.height ?? 0}; stampSize={Mathf.RoundToInt(stampSize.x)}x{Mathf.RoundToInt(stampSize.y)}");
    }

    private void LogStickerStampCommitted(string mode, Texture2D texture, Vector2 uv, MirrorPaintTarget mirrorTarget, Texture2D stampTexture)
    {
        if (texture == null)
        {
            return;
        }

        var px = Mathf.RoundToInt(uv.x * (texture.width - 1));
        var py = Mathf.RoundToInt(uv.y * (texture.height - 1));
        DrawableSuitsDiagnostics.Info($"StickerStampCommitted: mode={mode}; pointerSource={_pointerSource}; uv={uv}; pixel={px},{py}; {DescribeMirrorTarget(texture, mirrorTarget)}; shape={_stickerShape}; size={Mathf.RoundToInt(_stickerSize)}; rotation={Mathf.RoundToInt(_stickerRotation)}; color={ColorToHex(_brushColor)}; opacity={_brushOpacity:0.##}; stampTexture={stampTexture?.width ?? 0}x{stampTexture?.height ?? 0}; previewTexture={_decalPreviewTexture?.width ?? 0}x{_decalPreviewTexture?.height ?? 0}");
    }

    private void LogTextSurfaceStampCommitted(Texture2D texture, RaycastHit hit, MirrorPaintTarget mirrorTarget, Texture2D stampTexture, TextSurfaceStampStats primaryStats, TextSurfaceStampStats mirrorStats)
    {
        if (texture == null)
        {
            return;
        }

        var pixel = TexturePixel(texture, hit.textureCoord);
        DrawableSuitsDiagnostics.Info($"TextSurfaceStampCommitted: mode=WorldThirdPerson; pointerSource={_pointerSource}; uv={hit.textureCoord}; pixel={pixel.x},{pixel.y}; {DescribeMirrorTarget(texture, mirrorTarget)}; textLength={NormalizeTextStampValue(_textStampValue).Length}; fontSize={Mathf.RoundToInt(_textSize)}; rotation={Mathf.RoundToInt(_textRotation)}; color={ColorToHex(_brushColor)}; opacity={_brushOpacity:0.##}; generatedTexture={stampTexture?.width ?? 0}x{stampTexture?.height ?? 0}; hitPoint={hit.point}; hitNormal={hit.normal}; primaryWritten={primaryStats.WrittenPixels}; primarySkipped={primaryStats.SkippedPixels}; primaryAlpha={primaryStats.AlphaPixels}; primaryWorldSize={primaryStats.WorldWidth:0.###}x{primaryStats.WorldHeight:0.###}; mirrorWritten={mirrorStats.WrittenPixels}; mirrorSkipped={mirrorStats.SkippedPixels}; mirrorAlpha={mirrorStats.AlphaPixels}; mirrorWorldSize={mirrorStats.WorldWidth:0.###}x{mirrorStats.WorldHeight:0.###}");
    }

    private void LogStickerSurfaceStampCommitted(Texture2D texture, RaycastHit hit, MirrorPaintTarget mirrorTarget, Texture2D stampTexture, TextSurfaceStampStats primaryStats, TextSurfaceStampStats mirrorStats)
    {
        if (texture == null)
        {
            return;
        }

        var pixel = TexturePixel(texture, hit.textureCoord);
        DrawableSuitsDiagnostics.Info($"StickerStampCommitted: mode=WorldThirdPerson; pointerSource={_pointerSource}; uv={hit.textureCoord}; pixel={pixel.x},{pixel.y}; {DescribeMirrorTarget(texture, mirrorTarget)}; shape={_stickerShape}; size={Mathf.RoundToInt(_stickerSize)}; rotation={Mathf.RoundToInt(_stickerRotation)}; color={ColorToHex(_brushColor)}; opacity={_brushOpacity:0.##}; stampTexture={stampTexture?.width ?? 0}x{stampTexture?.height ?? 0}; hitPoint={hit.point}; hitNormal={hit.normal}; primarySamples={primaryStats.ProjectionSamples}; primaryHits={primaryStats.SurfaceHits}; primaryCells={primaryStats.RasterizedCells}; primarySeams={primaryStats.SeamSkippedCells}; primaryWritten={primaryStats.WrittenPixels}; primarySkipped={primaryStats.SkippedPixels}; primaryOffSuit={primaryStats.OffSuitSamples}; primaryAlpha={primaryStats.AlphaPixels}; primaryWorldSize={primaryStats.WorldWidth:0.###}x{primaryStats.WorldHeight:0.###}; mirrorSamples={mirrorStats.ProjectionSamples}; mirrorHits={mirrorStats.SurfaceHits}; mirrorCells={mirrorStats.RasterizedCells}; mirrorSeams={mirrorStats.SeamSkippedCells}; mirrorWritten={mirrorStats.WrittenPixels}; mirrorSkipped={mirrorStats.SkippedPixels}; mirrorOffSuit={mirrorStats.OffSuitSamples}; mirrorAlpha={mirrorStats.AlphaPixels}; mirrorWorldSize={mirrorStats.WorldWidth:0.###}x{mirrorStats.WorldHeight:0.###}");
    }

    private void LogTextSurfaceStampSkipped(string mode, string reason, Texture2D texture, RaycastHit hit, MirrorPaintTarget mirrorTarget, Texture2D stampTexture, TextSurfaceStampStats primaryStats, TextSurfaceStampStats mirrorStats)
    {
        var pixel = texture != null ? TexturePixel(texture, hit.textureCoord) : new Vector2Int(-1, -1);
        var key = $"surfaceStampSkipped|mode={mode}|reason={reason}|pixel={pixel.x},{pixel.y}|mirror={DescribeMirrorTarget(texture, mirrorTarget)}|textLength={NormalizeTextStampValue(_textStampValue).Length}|stampTexture={stampTexture?.width ?? 0}x{stampTexture?.height ?? 0}|primaryWritten={primaryStats.WrittenPixels}|mirrorWritten={mirrorStats.WrittenPixels}";
        if (Time.unscaledTime - _lastTextPreviewLogTime < 0.75f && string.Equals(key, _lastTextPreviewLogKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastTextPreviewLogTime = Time.unscaledTime;
        _lastTextPreviewLogKey = key;
        DrawableSuitsDiagnostics.Info($"TextSurfaceStampSkipped: {key}; hitPoint={hit.point}; hitNormal={hit.normal}; primarySkipped={primaryStats.SkippedPixels}; mirrorSkipped={mirrorStats.SkippedPixels}; pointerSource={_pointerSource}; cursor={_cursor}");
    }

    private void LogTextStampSkipped(string mode, string reason)
    {
        var key = $"skipped|mode={mode}|reason={reason}|textLength={NormalizeTextStampValue(_textStampValue).Length}|color={ColorToHex(_brushColor)}";
        if (Time.unscaledTime - _lastTextPreviewLogTime < 0.75f && string.Equals(key, _lastTextPreviewLogKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastTextPreviewLogTime = Time.unscaledTime;
        _lastTextPreviewLogKey = key;
        DrawableSuitsDiagnostics.Info($"TextStampSkipped: {key}; pointerSource={_pointerSource}; cursor={_cursor}");
    }

    private void LogStickerSurfaceStampSkipped(string mode, string reason, Texture2D texture, RaycastHit hit, MirrorPaintTarget mirrorTarget, Texture2D stampTexture, TextSurfaceStampStats primaryStats, TextSurfaceStampStats mirrorStats)
    {
        var pixel = texture != null ? TexturePixel(texture, hit.textureCoord) : new Vector2Int(-1, -1);
        var key = $"surfaceStampSkipped|mode={mode}|reason={reason}|pixel={pixel.x},{pixel.y}|mirror={DescribeMirrorTarget(texture, mirrorTarget)}|shape={_stickerShape}|stampTexture={stampTexture?.width ?? 0}x{stampTexture?.height ?? 0}|primaryWritten={primaryStats.WrittenPixels}|mirrorWritten={mirrorStats.WrittenPixels}|primaryCells={primaryStats.RasterizedCells}|mirrorCells={mirrorStats.RasterizedCells}";
        if (Time.unscaledTime - _lastStickerPreviewLogTime < 0.75f && string.Equals(key, _lastStickerPreviewLogKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastStickerPreviewLogTime = Time.unscaledTime;
        _lastStickerPreviewLogKey = key;
        DrawableSuitsDiagnostics.Info($"StickerStampSkipped: {key}; hitPoint={hit.point}; hitNormal={hit.normal}; primarySamples={primaryStats.ProjectionSamples}; primaryHits={primaryStats.SurfaceHits}; primarySkipped={primaryStats.SkippedPixels}; primaryOffSuit={primaryStats.OffSuitSamples}; primarySeams={primaryStats.SeamSkippedCells}; mirrorSamples={mirrorStats.ProjectionSamples}; mirrorHits={mirrorStats.SurfaceHits}; mirrorSkipped={mirrorStats.SkippedPixels}; mirrorOffSuit={mirrorStats.OffSuitSamples}; mirrorSeams={mirrorStats.SeamSkippedCells}; pointerSource={_pointerSource}; cursor={_cursor}");
    }

    private void LogStickerStampSkipped(string mode, string reason)
    {
        var key = $"skipped|mode={mode}|reason={reason}|shape={_stickerShape}|size={Mathf.RoundToInt(_stickerSize)}|rotation={Mathf.RoundToInt(_stickerRotation)}|color={ColorToHex(_brushColor)}";
        if (Time.unscaledTime - _lastStickerPreviewLogTime < 0.75f && string.Equals(key, _lastStickerPreviewLogKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastStickerPreviewLogTime = Time.unscaledTime;
        _lastStickerPreviewLogKey = key;
        DrawableSuitsDiagnostics.Info($"StickerStampSkipped: {key}; pointerSource={_pointerSource}; cursor={_cursor}");
    }

    private string CurrentDecalName()
    {
        return _selectedDecalIndex >= 0 && _selectedDecalIndex < _decalFiles.Count
            ? Path.GetFileName(_decalFiles[_selectedDecalIndex])
            : "none";
    }

    private static string MiddleEllipsize(string value, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(value) || maxChars <= 0)
        {
            return string.Empty;
        }

        if (value.Length <= maxChars)
        {
            return value;
        }

        if (maxChars <= 3)
        {
            return value.Substring(0, maxChars);
        }

        const string ellipsis = "...";
        var visibleChars = maxChars - ellipsis.Length;
        var left = Mathf.Max(1, visibleChars / 2);
        var right = Mathf.Max(1, visibleChars - left);
        if (left + right + ellipsis.Length > maxChars)
        {
            right = Mathf.Max(1, maxChars - ellipsis.Length - left);
        }

        return value.Substring(0, left) + ellipsis + value.Substring(value.Length - right);
    }

    private MirrorPaintTarget ResolveWorldMirrorTarget(Texture2D texture, RaycastHit hit, bool allowStatus)
    {
        var target = CreateMirrorTargetShell("WorldThirdPerson");
        if (!target.Enabled || texture == null || _worldPaintProxyObject == null)
        {
            return target;
        }

        if (!EnsureMirrorSurfaceMap(texture, "world mirror"))
        {
            target.Reason = "mirror surface map unavailable";
            LogMirrorTarget(target, texture, hit.textureCoord, allowStatus);
            return target;
        }

        var localPoint = _worldPaintProxyObject.transform.InverseTransformPoint(hit.point);
        target.PrimaryLocalPoint = localPoint;
        if (_mirrorSurfaceMap.TryMirrorFromLocalPoint(localPoint, out var result))
        {
            target.Available = true;
            target.Uv = result.Uv;
            target.ReflectedLocalPoint = result.ReflectedLocalPoint;
            target.MirroredLocalPoint = result.LocalPoint;
            target.MirroredLocalNormal = result.LocalNormal;
            target.Distance = result.Distance;
            target.TriangleIndex = result.TriangleIndex;
            target.Reason = result.Reason;
        }
        else
        {
            target.ReflectedLocalPoint = result.ReflectedLocalPoint;
            target.MirroredLocalPoint = result.LocalPoint;
            target.MirroredLocalNormal = result.LocalNormal;
            target.Distance = result.Distance;
            target.TriangleIndex = result.TriangleIndex;
            target.Reason = result.Reason;
        }

        LogMirrorTarget(target, texture, hit.textureCoord, allowStatus);
        return target;
    }

    private MirrorPaintTarget ResolveUvMirrorTarget(Texture2D texture, Vector2 uv, bool allowStatus, string mode = "TextureFallback")
    {
        var target = CreateMirrorTargetShell(mode);
        if (!target.Enabled || texture == null)
        {
            return target;
        }

        if (!EnsureMirrorSurfaceMap(texture, "uv fallback mirror"))
        {
            target.Reason = "mirror surface map unavailable";
            LogMirrorTarget(target, texture, uv, allowStatus);
            return target;
        }

        if (!_mirrorSurfaceMap.TryLocalPointFromUv(uv, out var localPoint, out var sourceTriangle, out var uvReason))
        {
            target.Reason = uvReason;
            target.TriangleIndex = sourceTriangle;
            LogMirrorTarget(target, texture, uv, allowStatus);
            return target;
        }

        target.PrimaryLocalPoint = localPoint;
        if (_mirrorSurfaceMap.TryMirrorFromLocalPoint(localPoint, out var result))
        {
            target.Available = true;
            target.Uv = result.Uv;
            target.ReflectedLocalPoint = result.ReflectedLocalPoint;
            target.MirroredLocalPoint = result.LocalPoint;
            target.MirroredLocalNormal = result.LocalNormal;
            target.Distance = result.Distance;
            target.TriangleIndex = result.TriangleIndex;
            target.Reason = $"{uvReason}; {result.Reason}";
        }
        else
        {
            target.ReflectedLocalPoint = result.ReflectedLocalPoint;
            target.MirroredLocalPoint = result.LocalPoint;
            target.MirroredLocalNormal = result.LocalNormal;
            target.Distance = result.Distance;
            target.TriangleIndex = result.TriangleIndex;
            target.Reason = result.Reason;
        }

        LogMirrorTarget(target, texture, uv, allowStatus);
        return target;
    }

    private MirrorPaintTarget CreateMirrorTargetShell(string mode)
    {
        return new MirrorPaintTarget
        {
            Enabled = _mirrorEnabled,
            Available = false,
            Mode = mode,
            Reason = _mirrorEnabled ? "not resolved" : "disabled"
        };
    }

    private bool EnsureMirrorSurfaceMap(Texture2D texture, string context)
    {
        if (!_mirrorEnabled)
        {
            return false;
        }

        var source = _worldSourceRenderer ?? FindBestSuitRenderer(StartOfRound.Instance?.localPlayerController);
        var mesh = _worldPaintMesh != null && _worldPaintMesh.vertexCount > 0 ? _worldPaintMesh : null;
        Mesh temporaryMesh = null;
        try
        {
            if (mesh == null && source != null)
            {
                temporaryMesh = new Mesh { name = "DrawableSuitsMirrorSurfaceBake" };
                var previousEnabled = source.enabled;
                try
                {
                    source.enabled = true;
                    source.BakeMesh(temporaryMesh, true);
                }
                finally
                {
                    source.enabled = previousEnabled;
                }

                if (temporaryMesh.vertexCount > 0)
                {
                    temporaryMesh.RecalculateBounds();
                    mesh = temporaryMesh;
                }
            }

            if (mesh == null || mesh.vertexCount == 0)
            {
                InvalidateMirrorSurfaceMap("no mesh");
                return false;
            }

            var key = $"{_selectedSuitId}|{source?.name ?? "unknown"}|v={mesh.vertexCount}|sub={mesh.subMeshCount}|bounds={mesh.bounds}|texture={texture?.width ?? 0}x{texture?.height ?? 0}";
            if (_mirrorSurfaceMap != null && string.Equals(_mirrorSurfaceMapKey, key, StringComparison.Ordinal))
            {
                return true;
            }

            _mirrorSurfaceMap = MirrorSurfaceMap.Build(mesh, key);
            _mirrorSurfaceMapKey = _mirrorSurfaceMap?.Key ?? string.Empty;
            if (_mirrorSurfaceMap == null)
            {
                DrawableSuitsDiagnostics.Warn($"MirrorSurfaceMap build failed. context={context}; key={key}; meshVertices={mesh.vertexCount}; subMeshes={mesh.subMeshCount}; uvCount={mesh.uv?.Length ?? 0}");
                return false;
            }

            DrawableSuitsDiagnostics.Info($"MirrorSurfaceMap built. context={context}; key={key}; triangles={_mirrorSurfaceMap.TriangleCount}; bounds={_mirrorSurfaceMap.Bounds}; mirrorPlaneX={_mirrorSurfaceMap.MirrorPlaneX:0.###}");
            return true;
        }
        catch (Exception ex)
        {
            DrawableSuitsDiagnostics.Exception($"MirrorSurfaceMap build failed. context={context}", ex);
            InvalidateMirrorSurfaceMap("exception");
            return false;
        }
        finally
        {
            if (temporaryMesh != null)
            {
                Destroy(temporaryMesh);
            }
        }
    }

    private void InvalidateMirrorSurfaceMap(string reason)
    {
        if (_mirrorSurfaceMap != null || !string.IsNullOrEmpty(_mirrorSurfaceMapKey))
        {
            DrawableSuitsDiagnostics.Info($"MirrorSurfaceMap invalidated. reason={reason}; previousKey={_mirrorSurfaceMapKey}");
        }

        _mirrorSurfaceMap = null;
        _mirrorSurfaceMapKey = string.Empty;
    }

    private void LogMirrorTarget(MirrorPaintTarget target, Texture2D texture, Vector2 primaryUv, bool allowStatus)
    {
        if (!target.Enabled)
        {
            return;
        }

        var primaryPixel = TexturePixel(texture, primaryUv);
        var mirroredPixel = target.Available ? TexturePixel(texture, target.Uv) : new Vector2Int(-1, -1);
        var key = $"mode={target.Mode}|available={target.Available}|primary={primaryPixel.x},{primaryPixel.y}|mirrored={mirroredPixel.x},{mirroredPixel.y}|tri={target.TriangleIndex}|reason={target.Reason}";
        if (Time.unscaledTime - _lastMirrorDiagnosticsTime < 0.75f && string.Equals(key, _lastMirrorDiagnosticsKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastMirrorDiagnosticsTime = Time.unscaledTime;
        _lastMirrorDiagnosticsKey = key;
        DrawableSuitsDiagnostics.Info($"MirrorSurfaceTarget: {key}; primaryUv={primaryUv}; mirroredUv={(target.Available ? target.Uv.ToString() : "none")}; primaryLocal={target.PrimaryLocalPoint}; reflectedLocal={target.ReflectedLocalPoint}; mirrorLocal={target.MirroredLocalPoint}; distance={target.Distance:0.###}; mapKey={_mirrorSurfaceMapKey}");
        if (allowStatus && !target.Available)
        {
            SetStatus("Mirror target not found; applied primary only.", false);
        }
    }

    private static Vector2Int TexturePixel(Texture2D texture, Vector2 uv)
    {
        if (texture == null)
        {
            return new Vector2Int(-1, -1);
        }

        return new Vector2Int(
            Mathf.RoundToInt(Mathf.Clamp01(uv.x) * (texture.width - 1)),
            Mathf.RoundToInt(Mathf.Clamp01(uv.y) * (texture.height - 1)));
    }

    private static bool ShouldApplyMirror(Texture2D texture, Vector2 primaryUv, MirrorPaintTarget mirrorTarget)
    {
        if (texture == null || !mirrorTarget.Enabled || !mirrorTarget.Available)
        {
            return false;
        }

        var primary = TexturePixel(texture, primaryUv);
        var mirrored = TexturePixel(texture, mirrorTarget.Uv);
        return primary.x != mirrored.x || primary.y != mirrored.y;
    }

    private string DescribeMirrorTarget(Texture2D texture, MirrorPaintTarget mirrorTarget)
    {
        if (!mirrorTarget.Enabled)
        {
            return "mirrorEnabled=False";
        }

        if (!mirrorTarget.Available)
        {
            return $"mirrorEnabled=True; mirrorMode=SurfaceMap; mirrorAvailable=False; reason={mirrorTarget.Reason}";
        }

        var mirroredPixel = TexturePixel(texture, mirrorTarget.Uv);
        return $"mirrorEnabled=True; mirrorMode=SurfaceMap; mirroredUv={mirrorTarget.Uv}; mirroredPixel={mirroredPixel.x},{mirroredPixel.y}; mirrorDistance={mirrorTarget.Distance:0.###}; mirrorTriangle={mirrorTarget.TriangleIndex}; mirrorReason={mirrorTarget.Reason}";
    }

    private void HandleControllerCursor()
    {
        _mousePositionAvailable = DrawableSuitsInput.TryGetMousePosition(out _lastMousePosition);
        var mouseUsed = Time.unscaledTime >= _ignoreMouseInputUntilTime && DrawableSuitsInput.WasMouseUsedThisFrame();
        var gamepad = Gamepad.current;
        _lastGamepadStick = gamepad != null ? gamepad.leftStick.ReadValue() : Vector2.zero;
        var gamepadMoved = gamepad != null && _lastGamepadStick.sqrMagnitude > 0.0324f;
        if (gamepadMoved && !_gamepadClickArmed)
        {
            _gamepadClickArmed = true;
            DrawableSuitsDiagnostics.Info($"Controller virtual cursor armed after stick movement. stick={_lastGamepadStick}; cursor={_cursor}");
        }

        var gamepadActivelyPointing = gamepad != null
            && (gamepadMoved
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
            _virtualPointerColorPicker = null;
            _virtualPointerButton = null;
            _virtualPointerInput = null;
            return;
        }

        var south = gamepad.buttonSouth;
        if (south.wasPressedThisFrame)
        {
            if (!_gamepadClickArmed)
            {
                DrawableSuitsDiagnostics.Info($"Virtual cursor A press ignored before controller cursor was armed. Move the left stick once before clicking. cursor={_cursor}; rawHits=[{DescribeRaycastHits(RaycastEditorUi(_cursor))}]");
                _virtualPointerDown = false;
                _virtualPointerPressTarget = null;
                _virtualPointerSlider = null;
                _virtualPointerColorPicker = null;
                _virtualPointerButton = null;
                _virtualPointerInput = null;
                return;
            }

            BeginVirtualPointerPress();
        }

        if (south.isPressed && _virtualPointerSlider != null)
        {
            _virtualPointerSlider.SetValueFromScreenPosition(_cursor, null, true);
        }
        if (south.isPressed && _virtualPointerColorPicker != null)
        {
            _virtualPointerColorPicker.SetValueFromScreenPosition(_cursor, null, true);
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
        _virtualPointerColorPicker = null;
        _virtualPointerButton = null;
        _virtualPointerInput = null;

        var hits = RaycastEditorUi(_cursor);
        var rawHits = DescribeRaycastHits(hits);
        if (!TryResolveVirtualCursorTarget(hits, out var resolved, out var button, out var input, out var slider, out var colorPicker))
        {
            DrawableSuitsDiagnostics.Info($"Virtual cursor A press hit no actionable UI target. cursor={_cursor}; rawHits=[{rawHits}]");
            return;
        }

        if (slider != null)
        {
            _virtualPointerSlider = slider;
            _virtualPointerPressTarget = slider.gameObject;
            EventSystem.current?.SetSelectedGameObject(slider.gameObject);
            slider.SetValueFromScreenPosition(_cursor, null, true);
            DrawableSuitsDiagnostics.Info($"Virtual cursor A press captured slider={slider.name}; resolved={resolved.name}; cursor={_cursor}; rawHits=[{rawHits}]");
            return;
        }

        if (colorPicker != null)
        {
            _virtualPointerColorPicker = colorPicker;
            _virtualPointerPressTarget = colorPicker.gameObject;
            EventSystem.current?.SetSelectedGameObject(colorPicker.gameObject);
            colorPicker.SetValueFromScreenPosition(_cursor, null, true);
            DrawableSuitsDiagnostics.Info($"Virtual cursor A press captured colorPicker={colorPicker.name}; resolved={resolved.name}; cursor={_cursor}; rawHits=[{rawHits}]");
            return;
        }

        if (input != null)
        {
            _virtualPointerInput = input;
            _virtualPointerPressTarget = input.gameObject;
            EventSystem.current?.SetSelectedGameObject(input.gameObject);
            input.ActivateInputField();
            DrawableSuitsDiagnostics.Info($"Virtual cursor A press focused input={input.name}; resolved={resolved.name}; cursor={_cursor}; rawHits=[{rawHits}]");
            return;
        }

        if (button != null)
        {
            _virtualPointerButton = button;
            _virtualPointerPressTarget = button.gameObject;
            DrawableSuitsDiagnostics.Info($"Virtual cursor A press captured button={button.name}; resolved={resolved.name}; cursor={_cursor}; rawHits=[{rawHits}]");
        }
    }

    private void EndVirtualPointerPress()
    {
        if (!_virtualPointerDown)
        {
            return;
        }

        _virtualPointerDown = false;
        var pressTarget = _virtualPointerPressTarget;
        var pressButton = _virtualPointerButton;
        var pressSlider = _virtualPointerSlider;
        var pressColorPicker = _virtualPointerColorPicker;
        _virtualPointerPressTarget = null;
        _virtualPointerSlider = null;
        _virtualPointerColorPicker = null;
        _virtualPointerButton = null;
        _virtualPointerInput = null;

        if (pressTarget == null)
        {
            return;
        }

        pressColorPicker?.EndVirtualDrag();
        var hits = RaycastEditorUi(_cursor);
        TryResolveVirtualCursorTarget(hits, out var releaseTarget, out var releaseButton, out _, out _, out _);
        if (pressButton != null && releaseButton == pressButton && pressButton.IsActive() && pressButton.IsInteractable())
        {
            pressButton.onClick.Invoke();
            ClearSelectedNormalButton();
        }
        else if (pressSlider != null || pressColorPicker != null)
        {
            EventSystem.current?.SetSelectedGameObject(null);
        }

        DrawableSuitsDiagnostics.Info($"Virtual cursor A release target={pressTarget.name}; releaseTarget={releaseTarget?.name ?? "null"}; pressButton={pressButton?.name ?? "null"}; releaseButton={releaseButton?.name ?? "null"}; cursor={_cursor}; rawHits=[{DescribeRaycastHits(hits)}]");
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
        return TryResolveVirtualCursorTarget(hits, out var target, out _, out _, out _, out _) ? target : null;
    }

    private static bool TryResolveVirtualCursorTarget(List<RaycastResult> hits, out GameObject target, out Button button, out InputField input, out DrawableSliderControl slider, out DrawableColorPickerControl colorPicker)
    {
        target = null;
        button = null;
        input = null;
        slider = null;
        colorPicker = null;

        if (hits == null)
        {
            return false;
        }

        for (var i = 0; i < hits.Count; i++)
        {
            var hitObject = hits[i].gameObject;
            if (hitObject == null || !hitObject.activeInHierarchy)
            {
                continue;
            }

            slider = hitObject.GetComponentInParent<DrawableSliderControl>();
            if (slider != null && slider.IsActive() && slider.IsInteractable())
            {
                target = slider.gameObject;
                return true;
            }

            input = hitObject.GetComponentInParent<InputField>();
            if (input != null && input.IsActive() && input.IsInteractable())
            {
                target = input.gameObject;
                return true;
            }

            button = hitObject.GetComponentInParent<Button>();
            if (button != null && button.IsActive() && button.IsInteractable())
            {
                target = button.gameObject;
                return true;
            }
        }

        for (var i = 0; i < hits.Count; i++)
        {
            var hitObject = hits[i].gameObject;
            if (hitObject == null || !hitObject.activeInHierarchy)
            {
                continue;
            }

            colorPicker = hitObject.GetComponentInParent<DrawableColorPickerControl>();
            if (colorPicker != null && colorPicker.IsActive() && colorPicker.IsInteractable())
            {
                target = colorPicker.gameObject;
                return true;
            }
        }

        return false;
    }

    private static string DescribeRaycastHits(List<RaycastResult> hits)
    {
        if (hits == null || hits.Count == 0)
        {
            return "none";
        }

        var count = Mathf.Min(8, hits.Count);
        var names = new string[count];
        for (var i = 0; i < count; i++)
        {
            names[i] = hits[i].gameObject != null ? hits[i].gameObject.name : "null";
        }

        return string.Join(", ", names);
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
        if (IsEditorModalOpen())
        {
            return;
        }

        var canRotatePlacement = CanHandlePlacementRotationShortcuts();
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

            if (canRotatePlacement)
            {
                if (gamepad.dpad.left.wasPressedThisFrame)
                {
                    ApplyPlacementRotationShortcut(-PlacementRotationShortcutStepDegrees, "GamepadDpadLeft");
                }
                if (gamepad.dpad.right.wasPressedThisFrame)
                {
                    ApplyPlacementRotationShortcut(PlacementRotationShortcutStepDegrees, "GamepadDpadRight");
                }
            }
        }

        if (canRotatePlacement)
        {
            if (DrawableSuitsInput.WasKeyPressed(Key.Comma))
            {
                ApplyPlacementRotationShortcut(-PlacementRotationShortcutStepDegrees, "KeyboardComma");
            }
            if (DrawableSuitsInput.WasKeyPressed(Key.Period))
            {
                ApplyPlacementRotationShortcut(PlacementRotationShortcutStepDegrees, "KeyboardPeriod");
            }
        }

        if (IsWorldThirdPersonMode)
        {
            var cursorOverTexturePanel = IsCursorOverPreviewViewport();
            var worldScroll = DrawableSuitsInput.MouseScrollY();
            if (cursorOverTexturePanel)
            {
                HandleUvPanelViewInput(worldScroll, gamepad, "TexturePanel");
                return;
            }

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

                var dpad = gamepad.dpad.ReadValue();
                if (Mathf.Abs(dpad.y) > 0.35f)
                {
                    _worldCameraDistance = Mathf.Clamp(_worldCameraDistance - dpad.y * Time.unscaledDeltaTime * 2f, 1.5f, 8f);
                }
            }

            if (!IsCursorOverEditorPanel() && DrawableSuitsInput.IsRightMousePressed())
            {
                _worldCameraYaw += DrawableSuitsInput.MouseDeltaX() * 3f;
                _worldCameraPitch -= DrawableSuitsInput.MouseDeltaY() * 2f;
            }

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
        if (cursorOverPreview)
        {
            HandleUvPanelViewInput(DrawableSuitsInput.MouseScrollY(), gamepad, "TextureFallback");
            return;
        }
    }
    private void HandlePaintingInput()
    {
        if (_bootstrapShell)
        {
            _strokeActive = false;
            _decalStampArmed = true;
            return;
        }

        if (IsEditorModalOpen())
        {
            _strokeActive = false;
            _decalStampArmed = true;
            _suppressPaintInputUntilRelease = false;
            _suppressDecalPreviewUntilRelease = false;
            HideDecalPlacementPreview("editor modal open", false);
            return;
        }

        if (!_canPaint)
        {
            _strokeActive = false;
            _decalStampArmed = true;
            return;
        }

        if (IsWorldThirdPersonMode && IsCursorOverPreviewViewport())
        {
            HandleTexturePanelPaintingInput("TexturePanel");
            return;
        }

        if (IsWorldThirdPersonMode)
        {
            HandleWorldPaintingInput();
            return;
        }

        HandleTexturePanelPaintingInput("TextureFallback");
    }

    private void HandleTexturePanelPaintingInput(string mode)
    {
        var mousePainting = DrawableSuitsInput.IsLeftMousePressed();
        var gamepadPainting = Gamepad.current?.rightTrigger.ReadValue() > 0.55f;
        var painting = mousePainting || gamepadPainting;

        if (!painting)
        {
            _strokeActive = false;
            _brushStrokeSeed = 1;
            _decalStampArmed = true;
            _suppressPaintInputUntilRelease = false;
            _suppressDecalPreviewUntilRelease = false;
            return;
        }

        if (_suppressPaintInputUntilRelease)
        {
            return;
        }

        var texture = DrawableSuitsPlugin.Registry.GetEditableTexture(_selectedSuitId);
        var uvAvailable = TryGetTexturePreviewUv(_cursor, out var uv);
        var overPreview = IsCursorOverPreviewViewport() && uvAvailable;

        if (_tool == EditorTool.Eyedropper)
        {
            HandleEyedropperInput(texture, overPreview, uv, mode);
            return;
        }

        var mirrorTarget = overPreview && texture != null ? ResolveUvMirrorTarget(texture, uv, false, mode) : CreateMirrorTargetShell(mode);
        LogPaintAttemptIfNeeded($"{mode} paint input", overPreview, uvAvailable, uv, texture, mirrorTarget, !_strokeActive);

        if (_tool == EditorTool.FillBucket)
        {
            HandleFillBucketInput(texture, overPreview, uv, mirrorTarget, mode);
            return;
        }

        if (IsPlacementTool(_tool))
        {
            HandleSinglePlacementStampInput(texture, overPreview, uv, mirrorTarget, mode);
            return;
        }

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
            BeginBrushStroke();
        }

        PaintAtCursor(texture, uv, mirrorTarget);
    }

    private void HandleWorldPaintingInput()
    {
        var mousePainting = !IsCursorOverEditorPanel() && DrawableSuitsInput.IsLeftMousePressed();
        var gamepadPainting = Gamepad.current?.rightTrigger.ReadValue() > 0.55f;
        var painting = mousePainting || gamepadPainting;
        if (!painting)
        {
            _strokeActive = false;
            _brushStrokeSeed = 1;
            _decalStampArmed = true;
            _suppressPaintInputUntilRelease = false;
            _suppressDecalPreviewUntilRelease = false;
            return;
        }

        if (_suppressPaintInputUntilRelease)
        {
            return;
        }

        var texture = DrawableSuitsPlugin.Registry.GetEditableTexture(_selectedSuitId);
        var canTargetWorld = !IsCursorOverEditorPanel();
        RaycastHit hit = default;
        var hitAvailable = canTargetWorld && TryGetWorldPaintHit(out hit);
        var uv = hitAvailable ? hit.textureCoord : default;

        if (_tool == EditorTool.Eyedropper)
        {
            HandleEyedropperInput(texture, hitAvailable, uv, "WorldThirdPerson");
            return;
        }

        var mirrorTarget = hitAvailable && texture != null ? ResolveWorldMirrorTarget(texture, hit, false) : CreateMirrorTargetShell("WorldThirdPerson");
        LogPaintAttemptIfNeeded("world paint input", hitAvailable, hitAvailable, uv, texture, mirrorTarget, !_strokeActive);

        if (_tool == EditorTool.FillBucket)
        {
            HandleFillBucketInput(texture, hitAvailable, uv, mirrorTarget, "WorldThirdPerson");
            return;
        }

        if (IsPlacementTool(_tool))
        {
            if (_tool == EditorTool.Text)
            {
                HandleWorldTextStampInput(texture, hitAvailable, hit, mirrorTarget);
                return;
            }
            if (_tool == EditorTool.Sticker)
            {
                HandleWorldStickerStampInput(texture, hitAvailable, hit, mirrorTarget);
                return;
            }

            HandleWorldDecalStampInput(texture, hitAvailable, hit, mirrorTarget);
            return;
        }

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
            BeginBrushStroke();
        }

        PaintWorldSurfaceBrush(texture, hit, mirrorTarget);
    }

    private void BeginBrushStroke()
    {
        SaveUndo(_tool == EditorTool.Erase ? "Erase" : "Brush stroke");
        ClearRedoHistory("brush stroke");
        _strokeActive = true;
        unchecked
        {
            _brushStrokeSequence++;
            _brushStrokeSeed = (_brushStrokeSequence * 1103515245)
                ^ Mathf.RoundToInt(Time.unscaledTime * 1000f)
                ^ (_selectedSuitId * 397)
                ^ ((int)_brushShape * 7919);
            if (_brushStrokeSeed == 0)
            {
                _brushStrokeSeed = 1;
            }
        }
    }

    private void HandleEyedropperInput(Texture2D texture, bool targetAvailable, Vector2 uv, string mode)
    {
        _strokeActive = false;
        _decalStampArmed = true;
        _suppressDecalPreviewUntilRelease = false;
        HideDecalPlacementPreview("eyedropper active", false);

        if (!targetAvailable || texture == null)
        {
            SetStatus("Aim at the suit to sample a color.", false);
            LogEyedropperMiss(mode, targetAvailable ? "no editable texture" : "cursor miss");
            return;
        }

        var pixel = TexturePixel(texture, uv);
        if (pixel.x < 0 || pixel.y < 0 || pixel.x >= texture.width || pixel.y >= texture.height)
        {
            SetStatus("Aim at the suit to sample a color.", false);
            LogEyedropperMiss(mode, $"invalid pixel {pixel.x},{pixel.y}");
            return;
        }

        var sampled = texture.GetPixel(pixel.x, pixel.y);
        sampled.a = 1f;
        _brushColor = sampled;
        _colorPicker?.SetColor(_brushColor, false);
        UpdateColorUi();
        if (_worldBrushMarkerMaterial != null)
        {
            _worldBrushMarkerMaterial.color = new Color(_brushColor.r, _brushColor.g, _brushColor.b, 0.85f);
        }

        var returnTool = ResolveEyedropperReturnTool();
        _tool = returnTool;
        _suppressPaintInputUntilRelease = true;
        _suppressDecalPreviewUntilRelease = false;
        if (_tool == EditorTool.Decal)
        {
            InvalidateDecalPreview("eyedropper return to decal");
        }
        else
        {
            HideDecalPlacementPreview("eyedropper sampled", false);
        }

        SetStatus($"Sampled {ColorToHex(_brushColor)}.", false);
        UpdateToolButtons();
        UpdateBrushIndicator();
        LogEyedropperSampled(mode, texture, uv, pixel, returnTool);
    }

    private EditorTool ResolveEyedropperReturnTool()
    {
        if (_previousToolBeforeEyedropper == EditorTool.Decal && _loadedDecal == null)
        {
            return EditorTool.Paint;
        }

        return IsReturnableEyedropperTool(_previousToolBeforeEyedropper)
            ? _previousToolBeforeEyedropper
            : EditorTool.Paint;
    }

    private void LogEyedropperSampled(string mode, Texture2D texture, Vector2 uv, Vector2Int pixel, EditorTool returnTool)
    {
        DrawableSuitsDiagnostics.Info($"EyedropperSampled: mode={mode}; pointerSource={_pointerSource}; uv={uv}; pixel={pixel.x},{pixel.y}; color={ColorToHex(_brushColor)}; returnTool={returnTool}; texture={texture.name} {texture.width}x{texture.height}");
    }

    private void LogEyedropperMiss(string mode, string reason)
    {
        var key = $"{mode}|{reason}|source={_pointerSource}";
        if (Time.unscaledTime - _lastEyedropperDiagnosticsTime < 0.75f && string.Equals(key, _lastEyedropperDiagnosticsKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastEyedropperDiagnosticsTime = Time.unscaledTime;
        _lastEyedropperDiagnosticsKey = key;
        DrawableSuitsDiagnostics.Info($"EyedropperMiss: mode={mode}; pointerSource={_pointerSource}; reason={reason}; cursor={_cursor}");
    }

    private void HandleFillBucketInput(Texture2D texture, bool targetAvailable, Vector2 uv, MirrorPaintTarget mirrorTarget, string mode)
    {
        _strokeActive = false;
        HideDecalPlacementPreview("fill bucket active", false);
        if (!_decalStampArmed)
        {
            return;
        }

        _decalStampArmed = false;
        _suppressDecalPreviewUntilRelease = true;

        if (!targetAvailable || texture == null)
        {
            SetStatus(string.Equals(mode, "WorldThirdPerson", StringComparison.OrdinalIgnoreCase)
                ? "Aim at your visible suit to fill."
                : "Click the texture preview to fill.", false);
            LogFillBucketSkipped(mode, targetAvailable ? "no editable texture" : "cursor miss", texture, uv, mirrorTarget, default, default);
            return;
        }

        var seedPixel = TexturePixel(texture, uv);
        if (seedPixel.x < 0 || seedPixel.y < 0 || seedPixel.x >= texture.width || seedPixel.y >= texture.height)
        {
            SetStatus("Aim at the suit to fill.", false);
            LogFillBucketSkipped(mode, $"invalid seed pixel {seedPixel.x},{seedPixel.y}", texture, uv, mirrorTarget, default, default);
            return;
        }

        Color32[] sourcePixels;
        try
        {
            sourcePixels = texture.GetPixels32();
        }
        catch (Exception ex)
        {
            SetStatus("Fill failed; editable texture could not be read.", true);
            DrawableSuitsDiagnostics.Exception($"FillBucket GetPixels32 failed. mode={mode}; texture={texture.name} {texture.width}x{texture.height}", ex);
            LogFillBucketSkipped(mode, "texture read failed", texture, uv, mirrorTarget, default, default);
            return;
        }

        SaveUndo("Color fill");
        ClearRedoHistory("color fill");

        var workingPixels = (Color32[])sourcePixels.Clone();
        var applyMirror = ShouldApplyMirror(texture, uv, mirrorTarget);
        var touchedPixels = applyMirror ? new HashSet<int>() : null;
        var changed = FloodFillBucket(texture, sourcePixels, workingPixels, seedPixel, false, touchedPixels, out var primaryStats);
        FillBucketStats mirrorStats = default;
        if (applyMirror)
        {
            var mirrorSeed = TexturePixel(texture, mirrorTarget.Uv);
            if (mirrorSeed.x >= 0 && mirrorSeed.y >= 0 && mirrorSeed.x < texture.width && mirrorSeed.y < texture.height)
            {
                changed |= FloodFillBucket(texture, sourcePixels, workingPixels, mirrorSeed, true, touchedPixels, out mirrorStats);
            }
        }

        if (!changed)
        {
            DropLastUndoEntry("Color fill wrote no pixels");
            SetStatus("Fill found no matching pixels to change.", false);
            LogFillBucketSkipped(mode, "no pixels changed", texture, uv, mirrorTarget, primaryStats, mirrorStats);
            return;
        }

        texture.SetPixels32(workingPixels);
        texture.Apply(false, false);
        InvalidateDecalPreview("texture changed by fill bucket");
        if (_previewMaterial != null)
        {
            _previewMaterial.mainTexture = texture;
        }
        DrawableSuitsPlugin.Registry.ApplyEditedTexture(_selectedSuitId, _designName, false);
        RefreshTexturePanelPreview("FillBucket", false);

        if (_mirrorEnabled && !mirrorTarget.Available)
        {
            SetStatus("Mirror target not found; filled primary region only.", false);
        }
        else
        {
            SetStatus($"Filled {primaryStats.WrittenPixels + mirrorStats.WrittenPixels} pixels.", false);
        }

        AddRecentColorFromBrush("Fill");
        LogFillBucketApplied(mode, texture, uv, mirrorTarget, primaryStats, mirrorStats);
        UpdateBrushIndicator();
    }

    private bool FloodFillBucket(Texture2D texture, Color32[] sourcePixels, Color32[] workingPixels, Vector2Int seedPixel, bool mirrored, HashSet<int> touchedPixels, out FillBucketStats stats)
    {
        stats = default;
        stats.Mirrored = mirrored;
        stats.SeedPixel = seedPixel;
        if (texture == null
            || sourcePixels == null
            || workingPixels == null
            || sourcePixels.Length != texture.width * texture.height
            || workingPixels.Length != sourcePixels.Length)
        {
            return false;
        }

        var width = texture.width;
        var height = texture.height;
        var seedIndex = (seedPixel.y * width) + seedPixel.x;
        if (seedIndex < 0 || seedIndex >= sourcePixels.Length)
        {
            return false;
        }

        var seedColor = sourcePixels[seedIndex];
        stats.SeedColor = seedColor;
        var visited = new bool[sourcePixels.Length];
        var queue = new Queue<int>();
        visited[seedIndex] = true;
        queue.Enqueue(seedIndex);

        while (queue.Count > 0)
        {
            var index = queue.Dequeue();
            stats.CheckedPixels++;
            var currentColor = sourcePixels[index];
            if (RgbDistance01(seedColor, currentColor) > _fillTolerance)
            {
                continue;
            }

            stats.MatchedPixels++;
            var x = index % width;
            var y = index / width;
            if (TryMarkTouchedPixel(texture, x, y, touchedPixels))
            {
                var existing = (Color)workingPixels[index];
                var blended = Color.Lerp(existing, _brushColor, Mathf.Clamp01(_brushOpacity));
                blended.a = existing.a;
                if (!ColorsNearlyEqual(existing, blended))
                {
                    workingPixels[index] = blended;
                    stats.WrittenPixels++;
                }
            }
            else
            {
                stats.TouchedSkippedPixels++;
            }

            EnqueueFillNeighbor(index - 1, x > 0, visited, queue);
            EnqueueFillNeighbor(index + 1, x < width - 1, visited, queue);
            EnqueueFillNeighbor(index - width, y > 0, visited, queue);
            EnqueueFillNeighbor(index + width, y < height - 1, visited, queue);
        }

        return stats.WrittenPixels > 0;
    }

    private static void EnqueueFillNeighbor(int index, bool valid, bool[] visited, Queue<int> queue)
    {
        if (!valid || index < 0 || index >= visited.Length || visited[index])
        {
            return;
        }

        visited[index] = true;
        queue.Enqueue(index);
    }

    private static float RgbDistance01(Color32 a, Color32 b)
    {
        var dr = (a.r - b.r) / 255f;
        var dg = (a.g - b.g) / 255f;
        var db = (a.b - b.b) / 255f;
        return Mathf.Sqrt((dr * dr) + (dg * dg) + (db * db)) / 1.7320508f;
    }

    private static bool ColorsNearlyEqual(Color a, Color b)
    {
        return Mathf.Abs(a.r - b.r) < 0.001f
            && Mathf.Abs(a.g - b.g) < 0.001f
            && Mathf.Abs(a.b - b.b) < 0.001f
            && Mathf.Abs(a.a - b.a) < 0.001f;
    }

    private void LogFillBucketApplied(string mode, Texture2D texture, Vector2 uv, MirrorPaintTarget mirrorTarget, FillBucketStats primaryStats, FillBucketStats mirrorStats)
    {
        if (texture == null)
        {
            return;
        }

        var seedPixel = TexturePixel(texture, uv);
        var mirrorDescription = DescribeMirrorTarget(texture, mirrorTarget);
        var key = $"applied|mode={mode}|seed={seedPixel.x},{seedPixel.y}|mirror={mirrorDescription}|tol={_fillTolerance:0.###}|written={primaryStats.WrittenPixels}+{mirrorStats.WrittenPixels}|checked={primaryStats.CheckedPixels}+{mirrorStats.CheckedPixels}|color={ColorToHex(_brushColor)}|opacity={_brushOpacity:0.##}";
        if (Time.unscaledTime - _lastFillBucketDiagnosticsTime < 0.5f && string.Equals(key, _lastFillBucketDiagnosticsKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastFillBucketDiagnosticsTime = Time.unscaledTime;
        _lastFillBucketDiagnosticsKey = key;
        DrawableSuitsDiagnostics.Info($"FillBucketApplied: {key}; tool={_tool}; pointerSource={_pointerSource}; uv={uv}; seedColor={ColorToHex(primaryStats.SeedColor)}; targetColor={ColorToHex(_brushColor)}; tolerance={_fillTolerance:0.###}; primaryMatched={primaryStats.MatchedPixels}; primaryTouchedSkipped={primaryStats.TouchedSkippedPixels}; mirroredSeed={mirrorStats.SeedPixel.x},{mirrorStats.SeedPixel.y}; mirroredSeedColor={ColorToHex(mirrorStats.SeedColor)}; mirrorMatched={mirrorStats.MatchedPixels}; mirrorTouchedSkipped={mirrorStats.TouchedSkippedPixels}; cursor={_cursor}");
    }

    private void LogFillBucketSkipped(string mode, string reason, Texture2D texture, Vector2 uv, MirrorPaintTarget mirrorTarget, FillBucketStats primaryStats, FillBucketStats mirrorStats)
    {
        var seedPixel = texture != null ? TexturePixel(texture, uv) : new Vector2Int(-1, -1);
        var mirrorDescription = texture != null ? DescribeMirrorTarget(texture, mirrorTarget) : $"mirrorEnabled={mirrorTarget.Enabled}; mirrorAvailable={mirrorTarget.Available}; reason={mirrorTarget.Reason}";
        var key = $"skipped|mode={mode}|reason={reason}|seed={seedPixel.x},{seedPixel.y}|mirror={mirrorDescription}|tol={_fillTolerance:0.###}|source={_pointerSource}";
        if (Time.unscaledTime - _lastFillBucketDiagnosticsTime < 0.75f && string.Equals(key, _lastFillBucketDiagnosticsKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastFillBucketDiagnosticsTime = Time.unscaledTime;
        _lastFillBucketDiagnosticsKey = key;
        DrawableSuitsDiagnostics.Info($"FillBucketSkipped: {key}; uv={uv}; checked={primaryStats.CheckedPixels}+{mirrorStats.CheckedPixels}; matched={primaryStats.MatchedPixels}+{mirrorStats.MatchedPixels}; written={primaryStats.WrittenPixels}+{mirrorStats.WrittenPixels}; cursor={_cursor}");
    }

    private void HandleSinglePlacementStampInput(Texture2D texture, bool targetAvailable, Vector2 uv, MirrorPaintTarget mirrorTarget, string mode)
    {
        _strokeActive = false;
        if (!_decalStampArmed)
        {
            return;
        }

        _decalStampArmed = false;
        _suppressDecalPreviewUntilRelease = true;
        HideDecalPlacementPreview("stamp input", false);

        if (!targetAvailable || texture == null)
        {
            if (string.Equals(mode, "WorldThirdPerson", StringComparison.OrdinalIgnoreCase))
            {
                SetStatus(_tool == EditorTool.Text ? "Aim at your visible suit to stamp text." : _tool == EditorTool.Sticker ? "Aim at your visible suit to stamp the sticker." : "Aim at your visible suit to stamp the decal.", false);
            }
            return;
        }

        if (!TryGetActivePlacementStamp(out var stampTexture, out var failureReason))
        {
            if (_tool == EditorTool.Decal)
            {
                WarnMissingDecal($"{mode} stamp");
                _tool = EditorTool.Paint;
                UpdateToolButtons();
            }
            else if (_tool == EditorTool.Sticker)
            {
                SetStatus($"Sticker failed: {failureReason}.", false);
                LogStickerStampSkipped(mode, failureReason);
            }
            else
            {
                SetStatus("Enter text before stamping.", false);
                LogTextStampSkipped(mode, failureReason);
            }
            return;
        }

        var historyLabel = _tool == EditorTool.Text ? "Text placed" : _tool == EditorTool.Sticker ? "Sticker placed" : "Decal placed";
        SaveUndo(historyLabel);
        ClearRedoHistory("placement stamp");
        if (PaintAtCursor(texture, uv, mirrorTarget))
        {
            if (_tool == EditorTool.Text)
            {
                LogTextStampCommitted(mode, texture, uv, mirrorTarget, stampTexture);
            }
            else if (_tool == EditorTool.Sticker)
            {
                LogStickerStampCommitted(mode, texture, uv, mirrorTarget, stampTexture);
            }
            else
            {
                LogDecalStampCommitted(mode, texture, uv, mirrorTarget);
            }
        }
        else
        {
            DropLastUndoEntry($"{historyLabel} wrote no pixels");
        }
    }

    private void HandleWorldDecalStampInput(Texture2D texture, bool targetAvailable, RaycastHit hit, MirrorPaintTarget mirrorTarget)
    {
        _strokeActive = false;
        if (!_decalStampArmed)
        {
            return;
        }

        _decalStampArmed = false;
        _suppressDecalPreviewUntilRelease = true;
        HideDecalPlacementPreview("decal stamp input", false);

        if (!targetAvailable || texture == null)
        {
            SetStatus("Aim at your visible suit to stamp the decal.", false);
            LogDecalSurfaceStampSkipped("WorldThirdPerson", "cursor miss", texture, hit, mirrorTarget, null, default, default);
            return;
        }

        if (!TryGetActivePlacementStamp(out var stampTexture, out var failureReason))
        {
            WarnMissingDecal("WorldThirdPerson surface stamp");
            if (!string.IsNullOrWhiteSpace(failureReason))
            {
                SetStatus($"Decal failed: {failureReason}.", false);
            }
            _tool = EditorTool.Paint;
            UpdateToolButtons();
            return;
        }

        SaveUndo("Decal placed");
        ClearRedoHistory("world decal stamp");
        var touchedPixels = mirrorTarget.Enabled && mirrorTarget.Available ? new HashSet<int>() : null;
        var primaryChanged = CompositeDecalSurfaceStamp(texture, stampTexture, hit.point, hit.normal, _decalRotation, false, _brushOpacity, touchedPixels, out var primaryStats);
        var mirrorChanged = false;
        TextSurfaceStampStats mirrorStats = default;
        if (ShouldApplyMirror(texture, hit.textureCoord, mirrorTarget) && TryGetMirrorWorldPlacement(mirrorTarget, out var mirrorPoint, out var mirrorNormal))
        {
            mirrorChanged = CompositeDecalSurfaceStamp(texture, stampTexture, mirrorPoint, mirrorNormal, -_decalRotation, true, _brushOpacity, touchedPixels, out mirrorStats);
        }

        if (!primaryChanged && !mirrorChanged)
        {
            LogDecalSurfaceStampSkipped("WorldThirdPerson", "surface projection wrote no pixels", texture, hit, mirrorTarget, stampTexture, primaryStats, mirrorStats);
            DropLastUndoEntry("Decal placed wrote no pixels");
            return;
        }

        texture.Apply(false, false);
        InvalidateDecalPreview("texture changed by decal surface stamp");
        DrawableSuitsPlugin.Registry.ApplyEditedTexture(_selectedSuitId, _designName, false);
        RefreshTexturePanelPreview("WorldDecalStamp", false);
        if (_mirrorEnabled && !mirrorTarget.Available)
        {
            SetStatus("Mirror target not found; applied primary only.", false);
        }
        LogDecalSurfaceStampCommitted(texture, hit, mirrorTarget, stampTexture, primaryStats, mirrorStats);
        LogPaintApplied(texture, hit.textureCoord, mirrorTarget);
        UpdateBrushIndicator();
    }

    private void HandleWorldTextStampInput(Texture2D texture, bool targetAvailable, RaycastHit hit, MirrorPaintTarget mirrorTarget)
    {
        _strokeActive = false;
        if (!_decalStampArmed)
        {
            return;
        }

        _decalStampArmed = false;
        _suppressDecalPreviewUntilRelease = true;
        HideDecalPlacementPreview("text stamp input", false);

        if (!targetAvailable || texture == null)
        {
            SetStatus("Aim at your visible suit to stamp text.", false);
            LogTextSurfaceStampSkipped("WorldThirdPerson", "cursor miss", texture, hit, mirrorTarget, null, default, default);
            return;
        }

        if (!TryGetTextStampTexture(out var stampTexture, out var failureReason))
        {
            SetStatus("Enter text before stamping.", false);
            LogTextSurfaceStampSkipped("WorldThirdPerson", failureReason, texture, hit, mirrorTarget, stampTexture, default, default);
            return;
        }

        SaveUndo("Text placed");
        ClearRedoHistory("world text stamp");
        var touchedPixels = mirrorTarget.Enabled && mirrorTarget.Available ? new HashSet<int>() : null;
        var primaryChanged = CompositeTextSurfaceStamp(texture, stampTexture, hit.point, hit.normal, _textRotation, false, _brushOpacity, touchedPixels, out var primaryStats);
        var mirrorChanged = false;
        TextSurfaceStampStats mirrorStats = default;
        if (ShouldApplyMirror(texture, hit.textureCoord, mirrorTarget) && TryGetMirrorWorldPlacement(mirrorTarget, out var mirrorPoint, out var mirrorNormal))
        {
            mirrorChanged = CompositeTextSurfaceStamp(texture, stampTexture, mirrorPoint, mirrorNormal, -_textRotation, true, _brushOpacity, touchedPixels, out mirrorStats);
        }

        if (!primaryChanged && !mirrorChanged)
        {
            LogTextSurfaceStampSkipped("WorldThirdPerson", "surface projection wrote no pixels", texture, hit, mirrorTarget, stampTexture, primaryStats, mirrorStats);
            DropLastUndoEntry("Text placed wrote no pixels");
            return;
        }

        texture.Apply(false, false);
        InvalidateDecalPreview("texture changed by text surface stamp");
        DrawableSuitsPlugin.Registry.ApplyEditedTexture(_selectedSuitId, _designName, false);
        RefreshTexturePanelPreview("WorldTextStamp", false);
        if (_mirrorEnabled && !mirrorTarget.Available)
        {
            SetStatus("Mirror target not found; applied primary only.", false);
        }
        AddRecentColorFromBrush("Text");
        LogTextSurfaceStampCommitted(texture, hit, mirrorTarget, stampTexture, primaryStats, mirrorStats);
        LogPaintApplied(texture, hit.textureCoord, mirrorTarget);
        UpdateBrushIndicator();
    }

    private void HandleWorldStickerStampInput(Texture2D texture, bool targetAvailable, RaycastHit hit, MirrorPaintTarget mirrorTarget)
    {
        _strokeActive = false;
        if (!_decalStampArmed)
        {
            return;
        }

        _decalStampArmed = false;
        _suppressDecalPreviewUntilRelease = true;
        HideDecalPlacementPreview("sticker stamp input", false);

        if (!targetAvailable || texture == null)
        {
            SetStatus("Aim at your visible suit to stamp the sticker.", false);
            LogStickerSurfaceStampSkipped("WorldThirdPerson", "cursor miss", texture, hit, mirrorTarget, null, default, default);
            return;
        }

        if (!TryGetActivePlacementStamp(out var stampTexture, out var failureReason))
        {
            SetStatus($"Sticker failed: {failureReason}.", false);
            LogStickerSurfaceStampSkipped("WorldThirdPerson", failureReason, texture, hit, mirrorTarget, stampTexture, default, default);
            return;
        }

        SaveUndo("Sticker placed");
        ClearRedoHistory("world sticker stamp");
        var touchedPixels = mirrorTarget.Enabled && mirrorTarget.Available ? new HashSet<int>() : null;
        var primaryChanged = CompositeDecalSurfaceStamp(texture, stampTexture, hit.point, hit.normal, _stickerRotation, false, _brushOpacity, touchedPixels, out var primaryStats, false);
        var mirrorChanged = false;
        TextSurfaceStampStats mirrorStats = default;
        if (ShouldApplyMirror(texture, hit.textureCoord, mirrorTarget) && TryGetMirrorWorldPlacement(mirrorTarget, out var mirrorPoint, out var mirrorNormal))
        {
            mirrorChanged = CompositeDecalSurfaceStamp(texture, stampTexture, mirrorPoint, mirrorNormal, -_stickerRotation, true, _brushOpacity, touchedPixels, out mirrorStats, false);
        }

        if (!primaryChanged && !mirrorChanged)
        {
            LogStickerSurfaceStampSkipped("WorldThirdPerson", "surface projection wrote no pixels", texture, hit, mirrorTarget, stampTexture, primaryStats, mirrorStats);
            DropLastUndoEntry("Sticker placed wrote no pixels");
            return;
        }

        texture.Apply(false, false);
        InvalidateDecalPreview("texture changed by sticker surface stamp");
        DrawableSuitsPlugin.Registry.ApplyEditedTexture(_selectedSuitId, _designName, false);
        RefreshTexturePanelPreview("WorldStickerStamp", false);
        if (_mirrorEnabled && !mirrorTarget.Available)
        {
            SetStatus("Mirror target not found; applied primary sticker only.", false);
        }
        AddRecentColorFromBrush("Sticker");
        LogStickerSurfaceStampCommitted(texture, hit, mirrorTarget, stampTexture, primaryStats, mirrorStats);
        LogPaintApplied(texture, hit.textureCoord, mirrorTarget);
        UpdateBrushIndicator();
    }

    private void LogPaintAttemptIfNeeded(string reason, bool overPreview, bool uvAvailable, Vector2 uv, Texture2D texture, MirrorPaintTarget mirrorTarget, bool force)
    {
        var pixel = "none";
        if (uvAvailable && texture != null)
        {
            var px = Mathf.RoundToInt(uv.x * (texture.width - 1));
            var py = Mathf.RoundToInt(uv.y * (texture.height - 1));
            pixel = $"{px},{py}";
        }

        var mirror = uvAvailable ? DescribeMirrorTarget(texture, mirrorTarget) : $"mirrorEnabled={_mirrorEnabled}";
        var key = $"{reason}|tool={_tool}|shape={_brushShape}|seed={_brushStrokeSeed}|over={overPreview}|uv={uvAvailable}:{uv}|pixel={pixel}|{mirror}|brush={Mathf.RoundToInt(EffectiveBrushRadiusPixels())}|opacity={_brushOpacity:0.##}|decal={_loadedDecal != null}|source={_pointerSource}";
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

    private void LogPaintApplied(Texture2D texture, Vector2 uv, MirrorPaintTarget mirrorTarget, BrushShapeStats primaryBrushStats = default, BrushShapeStats mirrorBrushStats = default)
    {
        var px = Mathf.RoundToInt(uv.x * (texture.width - 1));
        var py = Mathf.RoundToInt(uv.y * (texture.height - 1));
        var shapeStats = primaryBrushStats.Available || mirrorBrushStats.Available
            ? $"|primaryAccepted={primaryBrushStats.AcceptedSamples}|primaryRandomSkipped={primaryBrushStats.RandomSkippedSamples}|primaryWritten={primaryBrushStats.WrittenPixels}|mirrorAccepted={mirrorBrushStats.AcceptedSamples}|mirrorRandomSkipped={mirrorBrushStats.RandomSkippedSamples}|mirrorWritten={mirrorBrushStats.WrittenPixels}"
            : string.Empty;
        var key = $"applied|tool={_tool}|shape={_brushShape}|seed={_brushStrokeSeed}|pixel={px},{py}|{DescribeMirrorTarget(texture, mirrorTarget)}|brush={Mathf.RoundToInt(EffectiveBrushRadiusPixels())}|opacity={_brushOpacity:0.##}|decal={_loadedDecal != null}{shapeStats}";
        if (Time.unscaledTime - _lastPaintDiagnosticsTime < 0.5f && string.Equals(key, _lastPaintDiagnosticsKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastPaintDiagnosticsTime = Time.unscaledTime;
        _lastPaintDiagnosticsKey = key;
        DrawableSuitsDiagnostics.Info($"PaintApplied: {key}; texture={texture.name} {texture.width}x{texture.height}; uv={uv}; pointerSource={_pointerSource}");
    }

    private void LogBrushSurfaceStrokeApplied(Texture2D texture, RaycastHit hit, MirrorPaintTarget mirrorTarget, TextSurfaceStampStats primaryStats, TextSurfaceStampStats mirrorStats, float primaryRadius, float mirrorRadius, string fallbackReason)
    {
        if (texture == null)
        {
            return;
        }

        var pixel = TexturePixel(texture, hit.textureCoord);
        var key = $"applied|tool={_tool}|shape={_brushShape}|seed={_brushStrokeSeed}|pixel={pixel.x},{pixel.y}|mirror={DescribeMirrorTarget(texture, mirrorTarget)}|brush={Mathf.RoundToInt(EffectiveBrushRadiusPixels())}|opacity={_brushOpacity:0.##}|primaryWritten={primaryStats.WrittenPixels}|mirrorWritten={mirrorStats.WrittenPixels}|primaryAccepted={primaryStats.AcceptedSamples}|mirrorAccepted={mirrorStats.AcceptedSamples}|primaryRandomSkipped={primaryStats.RandomSkippedSamples}|mirrorRandomSkipped={mirrorStats.RandomSkippedSamples}|primaryCells={primaryStats.RasterizedCells}|mirrorCells={mirrorStats.RasterizedCells}|primarySeams={primaryStats.SeamSkippedCells}|mirrorSeams={mirrorStats.SeamSkippedCells}|radius={primaryRadius:0.####}";
        if (Time.unscaledTime - _lastBrushSurfaceDiagnosticsTime < 0.5f && string.Equals(key, _lastBrushSurfaceDiagnosticsKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastBrushSurfaceDiagnosticsTime = Time.unscaledTime;
        _lastBrushSurfaceDiagnosticsKey = key;
        DrawableSuitsDiagnostics.Info($"BrushSurfaceStrokeApplied: {key}; mode=WorldThirdPerson; pointerSource={_pointerSource}; uv={hit.textureCoord}; hitPoint={hit.point}; hitNormal={hit.normal}; primaryRadius={primaryRadius:0.####}; mirrorRadius={mirrorRadius:0.####}; primarySamples={primaryStats.ProjectionSamples}; primaryHits={primaryStats.SurfaceHits}; primaryAlpha={primaryStats.AlphaPixels}; primarySkipped={primaryStats.SkippedPixels}; primaryOffSuit={primaryStats.OffSuitSamples}; primaryWorldSize={primaryStats.WorldWidth:0.###}x{primaryStats.WorldHeight:0.###}; mirrorSamples={mirrorStats.ProjectionSamples}; mirrorHits={mirrorStats.SurfaceHits}; mirrorAlpha={mirrorStats.AlphaPixels}; mirrorSkipped={mirrorStats.SkippedPixels}; mirrorOffSuit={mirrorStats.OffSuitSamples}; mirrorWorldSize={mirrorStats.WorldWidth:0.###}x{mirrorStats.WorldHeight:0.###}; fallback={fallbackReason}; cursor={_cursor}");
    }

    private void LogBrushSurfaceStrokeSkipped(string reason, Texture2D texture, RaycastHit hit, MirrorPaintTarget mirrorTarget, TextSurfaceStampStats primaryStats, TextSurfaceStampStats mirrorStats, float primaryRadius, float mirrorRadius, string fallbackReason)
    {
        var pixel = texture != null ? TexturePixel(texture, hit.textureCoord) : new Vector2Int(-1, -1);
        var key = $"skipped|reason={reason}|tool={_tool}|shape={_brushShape}|seed={_brushStrokeSeed}|pixel={pixel.x},{pixel.y}|mirror={DescribeMirrorTarget(texture, mirrorTarget)}|brush={Mathf.RoundToInt(EffectiveBrushRadiusPixels())}|primaryWritten={primaryStats.WrittenPixels}|mirrorWritten={mirrorStats.WrittenPixels}|primaryAccepted={primaryStats.AcceptedSamples}|mirrorAccepted={mirrorStats.AcceptedSamples}|primaryRandomSkipped={primaryStats.RandomSkippedSamples}|mirrorRandomSkipped={mirrorStats.RandomSkippedSamples}|primaryCells={primaryStats.RasterizedCells}|mirrorCells={mirrorStats.RasterizedCells}";
        if (Time.unscaledTime - _lastBrushSurfaceDiagnosticsTime < 0.75f && string.Equals(key, _lastBrushSurfaceDiagnosticsKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastBrushSurfaceDiagnosticsTime = Time.unscaledTime;
        _lastBrushSurfaceDiagnosticsKey = key;
        DrawableSuitsDiagnostics.Info($"BrushSurfaceStrokeSkipped: {key}; mode=WorldThirdPerson; pointerSource={_pointerSource}; uv={hit.textureCoord}; hitPoint={hit.point}; hitNormal={hit.normal}; primaryRadius={primaryRadius:0.####}; mirrorRadius={mirrorRadius:0.####}; primarySamples={primaryStats.ProjectionSamples}; primaryHits={primaryStats.SurfaceHits}; primarySkipped={primaryStats.SkippedPixels}; primaryOffSuit={primaryStats.OffSuitSamples}; primarySeams={primaryStats.SeamSkippedCells}; mirrorSamples={mirrorStats.ProjectionSamples}; mirrorHits={mirrorStats.SurfaceHits}; mirrorSkipped={mirrorStats.SkippedPixels}; mirrorOffSuit={mirrorStats.OffSuitSamples}; mirrorSeams={mirrorStats.SeamSkippedCells}; fallback={fallbackReason}; cursor={_cursor}");
    }

    private bool IsCursorOverEditorPanel()
    {
        if (_designCodePanelObject != null
            && _designCodePanelObject.activeInHierarchy
            && RectTransformUtility.RectangleContainsScreenPoint(_designCodePanelObject.GetComponent<RectTransform>(), _cursor, null))
        {
            return true;
        }

        return _panelRect != null && RectTransformUtility.RectangleContainsScreenPoint(_panelRect, _cursor, null);
    }

    private bool IsCursorOverPreviewViewport()
    {
        return _previewViewportRect != null && RectTransformUtility.RectangleContainsScreenPoint(_previewViewportRect, _cursor, null);
    }

    private Rect GetUvPanelViewRect()
    {
        var zoom = Mathf.Clamp(_uvPanelZoom, UvPanelMinZoom, UvPanelMaxZoom);
        var size = 1f / zoom;
        var x = Mathf.Clamp(_uvPanelCenter.x - size * 0.5f, 0f, Mathf.Max(0f, 1f - size));
        var y = Mathf.Clamp(_uvPanelCenter.y - size * 0.5f, 0f, Mathf.Max(0f, 1f - size));
        return new Rect(x, y, size, size);
    }

    private void ResetUvPanelView(string reason, bool forceLog)
    {
        _uvPanelZoom = UvPanelMinZoom;
        _uvPanelCenter = new Vector2(0.5f, 0.5f);
        var texture = _selectedSuitId >= 0 ? DrawableSuitsPlugin.Registry.GetEditableTexture(_selectedSuitId) : null;
        ApplyUvPanelViewToPreviewImage(texture);
        LogUvPanelViewChanged("Reset", default, reason, forceLog);
        InvalidateDecalPreview("uv panel view reset");
    }

    private void EnsureUvPanelViewForTexture(Texture2D texture, string reason)
    {
        if (texture == null)
        {
            _uvPanelTextureWidth = 0;
            _uvPanelTextureHeight = 0;
            return;
        }

        if (_uvPanelTextureWidth != texture.width || _uvPanelTextureHeight != texture.height)
        {
            _uvPanelTextureWidth = texture.width;
            _uvPanelTextureHeight = texture.height;
            ResetUvPanelView($"texture size changed ({reason})", true);
            return;
        }

        SetUvPanelView(_uvPanelZoom, _uvPanelCenter, "Clamp", default, reason, false);
    }

    private void ApplyUvPanelViewToPreviewImage(Texture2D texture)
    {
        if (_previewImage == null)
        {
            return;
        }

        _previewImage.uvRect = texture != null ? GetUvPanelViewRect() : new Rect(0f, 0f, 10f, 10f);
    }

    private void SetUvPanelView(float zoom, Vector2 center, string source, Vector2 anchorUv, string reason, bool forceLog)
    {
        var previousZoom = _uvPanelZoom;
        var previousCenter = _uvPanelCenter;
        _uvPanelZoom = Mathf.Clamp(zoom, UvPanelMinZoom, UvPanelMaxZoom);
        var viewSize = 1f / _uvPanelZoom;
        var minCenter = viewSize * 0.5f;
        var maxCenter = 1f - viewSize * 0.5f;
        _uvPanelCenter = new Vector2(
            Mathf.Clamp(center.x, minCenter, maxCenter),
            Mathf.Clamp(center.y, minCenter, maxCenter));

        var texture = _selectedSuitId >= 0 ? DrawableSuitsPlugin.Registry.GetEditableTexture(_selectedSuitId) : null;
        ApplyUvPanelViewToPreviewImage(texture);

        var changed = !Mathf.Approximately(previousZoom, _uvPanelZoom)
            || (previousCenter - _uvPanelCenter).sqrMagnitude > 0.0000001f;
        if (changed)
        {
            InvalidateDecalPreview("uv panel view changed");
        }

        if (changed || forceLog)
        {
            LogUvPanelViewChanged(source, anchorUv, reason, forceLog);
        }
    }

    private bool ZoomUvPanelAtCursor(float factor, string source, string reason)
    {
        if (!TryGetTexturePreviewNormalized(_cursor, out var panelPoint))
        {
            return false;
        }

        if (!TryGetTexturePreviewUv(_cursor, out var anchorUv))
        {
            return false;
        }

        var newZoom = Mathf.Clamp(_uvPanelZoom * factor, UvPanelMinZoom, UvPanelMaxZoom);
        if (Mathf.Approximately(newZoom, _uvPanelZoom))
        {
            return true;
        }

        var newViewSize = 1f / newZoom;
        var newViewX = anchorUv.x - panelPoint.x * newViewSize;
        var newViewY = anchorUv.y - panelPoint.y * newViewSize;
        var newCenter = new Vector2(newViewX + newViewSize * 0.5f, newViewY + newViewSize * 0.5f);
        SetUvPanelView(newZoom, newCenter, source, anchorUv, reason, false);
        return true;
    }

    private bool PanUvPanelFromMouseDelta(string source)
    {
        if (_previewViewportRect == null)
        {
            return false;
        }

        var screenSize = GetRectTransformScreenSize(_previewViewportRect);
        if (screenSize.x <= 0f || screenSize.y <= 0f)
        {
            return false;
        }

        var delta = new Vector2(DrawableSuitsInput.MouseDeltaX(), DrawableSuitsInput.MouseDeltaY());
        if (delta.sqrMagnitude <= 0.001f)
        {
            return true;
        }

        var view = GetUvPanelViewRect();
        var deltaUv = new Vector2(delta.x / screenSize.x * view.width, delta.y / screenSize.y * view.height);
        SetUvPanelView(_uvPanelZoom, _uvPanelCenter - deltaUv, source, _lastPreviewUvAvailable ? _lastPreviewUv : default, "right mouse pan", false);
        return true;
    }

    private bool PanUvPanelFromGamepadStick(Vector2 stick, string source)
    {
        if (stick.sqrMagnitude <= UvPanelGamepadPanDeadzone * UvPanelGamepadPanDeadzone)
        {
            return false;
        }

        var view = GetUvPanelViewRect();
        var deltaUv = new Vector2(
            stick.x * view.width * UvPanelGamepadPanSpeed * Time.unscaledDeltaTime,
            stick.y * view.height * UvPanelGamepadPanSpeed * Time.unscaledDeltaTime);
        if (deltaUv.sqrMagnitude <= 0.0000001f)
        {
            return false;
        }

        SetUvPanelView(_uvPanelZoom, _uvPanelCenter + deltaUv, source, _lastPreviewUvAvailable ? _lastPreviewUv : default, "right stick pan", false);
        return true;
    }

    private bool HandleUvPanelViewInput(float mouseScroll, Gamepad gamepad, string targetMode)
    {
        if (!IsCursorOverPreviewViewport())
        {
            return false;
        }

        var consumed = false;
        if (Mathf.Abs(mouseScroll) > 0.01f)
        {
            var factor = Mathf.Pow(UvPanelWheelZoomFactor, mouseScroll);
            consumed |= ZoomUvPanelAtCursor(factor, "MouseWheel", targetMode);
        }

        if (gamepad != null)
        {
            var dpad = gamepad.dpad.ReadValue();
            if (Mathf.Abs(dpad.y) > 0.35f)
            {
                var factor = Mathf.Pow(UvPanelDpadZoomFactorPerSecond, dpad.y * Time.unscaledDeltaTime);
                consumed |= ZoomUvPanelAtCursor(factor, "GamepadDpad", targetMode);
            }

            consumed |= PanUvPanelFromGamepadStick(gamepad.rightStick.ReadValue(), "GamepadRightStickPan");
        }

        if (DrawableSuitsInput.IsRightMousePressed())
        {
            consumed |= PanUvPanelFromMouseDelta("MouseRightDrag");
        }

        return consumed;
    }

    private void LogUvPanelViewChanged(string source, Vector2 anchorUv, string reason, bool forceLog)
    {
        var view = GetUvPanelViewRect();
        var key = $"{source}|zoom={_uvPanelZoom:0.###}|center={_uvPanelCenter.x:0.####},{_uvPanelCenter.y:0.####}|view={view.x:0.####},{view.y:0.####},{view.width:0.####},{view.height:0.####}|reason={reason}";
        if (!forceLog && Time.unscaledTime - _lastUvPanelViewLogTime < 0.5f && string.Equals(key, _lastUvPanelViewLogKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastUvPanelViewLogTime = Time.unscaledTime;
        _lastUvPanelViewLogKey = key;
        DrawableSuitsDiagnostics.Info($"UvPanelViewChanged: source={source}; reason={reason}; zoom={_uvPanelZoom:0.###}; center={_uvPanelCenter}; uvRect={view}; anchorUv={(anchorUv == default ? "none" : anchorUv.ToString())}; pointerSource={_pointerSource}; cursor={_cursor}; previewImageUvRect={(_previewImage != null ? _previewImage.uvRect.ToString() : "null")}");
    }

    private bool TryGetTexturePreviewNormalized(Vector2 screenPosition, out Vector2 normalized)
    {
        normalized = default;
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

        normalized = new Vector2(Mathf.Clamp01(x), Mathf.Clamp01(y));
        return true;
    }

    private bool TryTextureUvToPreviewLocal(Vector2 uv, out Vector2 localPoint)
    {
        localPoint = default;
        if (_previewViewportRect == null)
        {
            return false;
        }

        var view = GetUvPanelViewRect();
        const float epsilon = 0.0001f;
        if (uv.x < view.xMin - epsilon || uv.x > view.xMax + epsilon || uv.y < view.yMin - epsilon || uv.y > view.yMax + epsilon)
        {
            return false;
        }

        var normalizedX = Mathf.InverseLerp(view.xMin, view.xMax, uv.x);
        var normalizedY = Mathf.InverseLerp(view.yMin, view.yMax, uv.y);
        var rect = _previewViewportRect.rect;
        localPoint = new Vector2(
            Mathf.Lerp(rect.xMin, rect.xMax, normalizedX),
            Mathf.Lerp(rect.yMin, rect.yMax, normalizedY));
        return true;
    }

    private bool TryGetTexturePreviewUv(Vector2 screenPosition, out Vector2 uv)
    {
        uv = default;
        _lastPreviewUvAvailable = false;
        if (!TryGetTexturePreviewNormalized(screenPosition, out var normalized))
        {
            return false;
        }

        var view = GetUvPanelViewRect();
        uv = new Vector2(
            Mathf.Clamp01(view.xMin + normalized.x * view.width),
            Mathf.Clamp01(view.yMin + normalized.y * view.height));
        _lastPreviewUv = uv;
        _lastPreviewUvAvailable = true;
        return true;
    }

    private bool PaintAtCursor(Texture2D texture, Vector2 uv, MirrorPaintTarget mirrorTarget)
    {
        if (texture == null)
        {
            RefreshEditorReadiness("paint preflight failed");
            UpdateUiState();
            return false;
        }

        var applyMirror = ShouldApplyMirror(texture, uv, mirrorTarget);
        var touchedPixels = applyMirror ? new HashSet<int>() : null;
        var changed = true;
        BrushShapeStats primaryBrushStats = default;
        BrushShapeStats mirrorBrushStats = default;
        switch (_tool)
        {
            case EditorTool.Paint:
                changed = ApplyDirectBrush(texture, uv, false, touchedPixels, out primaryBrushStats);
                if (applyMirror)
                {
                    changed |= ApplyDirectBrush(texture, mirrorTarget.Uv, true, touchedPixels, out mirrorBrushStats);
                }
                break;
            case EditorTool.Erase:
                changed = ApplyDirectBrush(texture, uv, false, touchedPixels, out primaryBrushStats);
                if (applyMirror)
                {
                    changed |= ApplyDirectBrush(texture, mirrorTarget.Uv, true, touchedPixels, out mirrorBrushStats);
                }
                break;
            case EditorTool.Decal:
                changed = ApplyDecal(texture, uv, false, touchedPixels);
                if (applyMirror)
                {
                    changed |= ApplyDecal(texture, mirrorTarget.Uv, true, touchedPixels);
                }
                _strokeActive = false;
                break;
            case EditorTool.Text:
                changed = ApplyTextStamp(texture, uv, false, touchedPixels);
                if (applyMirror)
                {
                    changed |= ApplyTextStamp(texture, mirrorTarget.Uv, true, touchedPixels);
                }
                _strokeActive = false;
                break;
            case EditorTool.Sticker:
                changed = ApplyStickerStamp(texture, uv, false, touchedPixels);
                if (applyMirror)
                {
                    changed |= ApplyStickerStamp(texture, mirrorTarget.Uv, true, touchedPixels);
                }
                _strokeActive = false;
                break;
            default:
                changed = false;
                break;
        }

        if (!changed)
        {
            return false;
        }

        texture.Apply(false, false);
        InvalidateDecalPreview("texture changed by paint");
        if (_previewMaterial != null)
        {
            _previewMaterial.mainTexture = texture;
        }
        DrawableSuitsPlugin.Registry.ApplyEditedTexture(_selectedSuitId, _designName, false);
        RefreshTexturePanelPreview("PaintAtCursor", false);

        if (_mirrorEnabled && !mirrorTarget.Available)
        {
            SetStatus("Mirror target not found; applied primary only.", false);
        }

        if (_tool == EditorTool.Paint)
        {
            AddRecentColorFromBrush("Paint");
        }
        else if (_tool == EditorTool.Text)
        {
            AddRecentColorFromBrush("Text");
        }
        else if (_tool == EditorTool.Sticker)
        {
            AddRecentColorFromBrush("Sticker");
        }

        LogPaintApplied(texture, uv, mirrorTarget, primaryBrushStats, mirrorBrushStats);
        UpdateBrushIndicator();
        return true;
    }

    private static string CombineReasons(params string[] reasons)
    {
        if (reasons == null || reasons.Length == 0)
        {
            return string.Empty;
        }

        var combined = string.Empty;
        for (var i = 0; i < reasons.Length; i++)
        {
            var reason = reasons[i];
            if (string.IsNullOrWhiteSpace(reason))
            {
                continue;
            }

            combined = string.IsNullOrWhiteSpace(combined)
                ? reason
                : $"{combined}; {reason}";
        }

        return combined;
    }

    private bool PaintWorldSurfaceBrush(Texture2D texture, RaycastHit hit, MirrorPaintTarget mirrorTarget)
    {
        if (texture == null)
        {
            RefreshEditorReadiness("world brush preflight failed");
            UpdateUiState();
            return false;
        }

        if (_tool != EditorTool.Paint && _tool != EditorTool.Erase)
        {
            return PaintAtCursor(texture, hit.textureCoord, mirrorTarget);
        }

        if (_tool == EditorTool.Erase)
        {
            var state = DrawableSuitsPlugin.Registry.GetOrCreateState(_selectedSuitId);
            if (state?.BaseTexture == null)
            {
                LogBrushSurfaceStrokeSkipped("base texture missing", texture, hit, mirrorTarget, default, default, 0f, 0f, "base texture missing");
                return false;
            }
        }

        if (_brushShape == BrushShape.Pixel)
        {
            return PaintAtCursor(texture, hit.textureCoord, mirrorTarget);
        }

        var radiusFallback = string.Empty;
        if (!TryComputeWorldBrushRadius(hit, texture, out var worldRadius, out radiusFallback))
        {
            worldRadius = EstimateWorldBrushRadius(texture);
        }

        var applyMirror = ShouldApplyMirror(texture, hit.textureCoord, mirrorTarget);
        var touchedPixels = applyMirror ? new HashSet<int>() : null;
        var changed = CompositeBrushSurfaceStroke(texture, hit.point, hit.normal, worldRadius, false, touchedPixels, out var primaryStats, out var primaryReason);
        TextSurfaceStampStats mirrorStats = default;
        var mirrorRadius = worldRadius;
        var mirrorReason = string.Empty;
        if (applyMirror && TryGetMirrorWorldPlacement(mirrorTarget, out var mirrorPoint, out var mirrorNormal))
        {
            changed |= CompositeBrushSurfaceStroke(texture, mirrorPoint, mirrorNormal, mirrorRadius, true, touchedPixels, out mirrorStats, out mirrorReason);
        }

        var fallbackReason = CombineReasons(radiusFallback, primaryReason, mirrorReason);
        if (!changed)
        {
            LogBrushSurfaceStrokeSkipped("surface projection wrote no pixels", texture, hit, mirrorTarget, primaryStats, mirrorStats, worldRadius, mirrorRadius, fallbackReason);
            return false;
        }

        texture.Apply(false, false);
        InvalidateDecalPreview("texture changed by surface brush stroke");
        if (_previewMaterial != null)
        {
            _previewMaterial.mainTexture = texture;
        }
        DrawableSuitsPlugin.Registry.ApplyEditedTexture(_selectedSuitId, _designName, false);
        RefreshTexturePanelPreview("PaintWorldSurfaceBrush", false);

        if (_mirrorEnabled && !mirrorTarget.Available)
        {
            SetStatus("Mirror target not found; applied primary only.", false);
        }

        if (_tool == EditorTool.Paint)
        {
            AddRecentColorFromBrush("Paint");
        }

        LogBrushSurfaceStrokeApplied(texture, hit, mirrorTarget, primaryStats, mirrorStats, worldRadius, mirrorRadius, fallbackReason);
        LogPaintApplied(texture, hit.textureCoord, mirrorTarget);
        UpdateBrushIndicator();
        return true;
    }

    private bool TryComputeWorldBrushRadius(RaycastHit hit, Texture2D texture, out float radius, out string fallbackReason)
    {
        radius = 0f;
        fallbackReason = string.Empty;
        if (_worldPaintProxyObject == null || _worldPaintMesh == null || texture == null)
        {
            fallbackReason = "world brush dependencies missing";
            return false;
        }

        if (hit.triangleIndex < 0)
        {
            fallbackReason = $"invalid triangle {hit.triangleIndex}";
            return false;
        }

        var triangles = _worldPaintMesh.triangles;
        var vertices = _worldPaintMesh.vertices;
        var uvs = _worldPaintMesh.uv;
        var triangleOffset = hit.triangleIndex * 3;
        if (triangles == null || vertices == null || uvs == null
            || triangleOffset < 0 || triangleOffset + 2 >= triangles.Length)
        {
            fallbackReason = $"triangle array unavailable index={hit.triangleIndex}";
            return false;
        }

        var i0 = triangles[triangleOffset];
        var i1 = triangles[triangleOffset + 1];
        var i2 = triangles[triangleOffset + 2];
        if (i0 < 0 || i1 < 0 || i2 < 0
            || i0 >= vertices.Length || i1 >= vertices.Length || i2 >= vertices.Length
            || i0 >= uvs.Length || i1 >= uvs.Length || i2 >= uvs.Length)
        {
            fallbackReason = $"triangle vertex out of range index={hit.triangleIndex}";
            return false;
        }

        var edge1 = vertices[i1] - vertices[i0];
        var edge2 = vertices[i2] - vertices[i0];
        var duv1 = uvs[i1] - uvs[i0];
        var duv2 = uvs[i2] - uvs[i0];
        var determinant = duv1.x * duv2.y - duv1.y * duv2.x;
        if (Mathf.Abs(determinant) < 0.000001f)
        {
            fallbackReason = $"degenerate uv triangle {hit.triangleIndex}";
            return false;
        }

        var inverse = 1f / determinant;
        var dPdu = (edge1 * duv2.y - edge2 * duv1.y) * inverse;
        var dPdv = (edge2 * duv1.x - edge1 * duv2.x) * inverse;
        var brushRadius = EffectiveBrushRadiusPixels();
        var du = brushRadius / Mathf.Max(1f, texture.width);
        var dv = brushRadius / Mathf.Max(1f, texture.height);
        var transform = _worldPaintProxyObject.transform;
        var radiusU = transform.TransformVector(dPdu * du).magnitude;
        var radiusV = transform.TransformVector(dPdv * dv).magnitude;
        var computedRadius = Mathf.Max(radiusU, radiusV);
        if (float.IsNaN(computedRadius) || float.IsInfinity(computedRadius) || computedRadius <= 0.0001f)
        {
            fallbackReason = $"invalid world radius {computedRadius:0.#####}";
            return false;
        }

        var boundsHeight = _worldAvatarRenderer != null ? _worldAvatarRenderer.bounds.size.y : 1.8f;
        if (boundsHeight <= 0.01f)
        {
            boundsHeight = 1.8f;
        }

        radius = Mathf.Clamp(computedRadius, boundsHeight * 0.002f, boundsHeight * 0.35f);
        return true;
    }

    private float EstimateWorldBrushRadius(Texture2D texture)
    {
        var boundsHeight = _worldAvatarRenderer != null ? _worldAvatarRenderer.bounds.size.y : 1.8f;
        if (boundsHeight <= 0.01f)
        {
            boundsHeight = 1.8f;
        }

        var textureScale = texture != null ? Mathf.Max(1f, Mathf.Max(texture.width, texture.height)) : 1024f;
        var radius = (EffectiveBrushRadiusPixels() / textureScale) * boundsHeight;
        return Mathf.Clamp(radius, boundsHeight * 0.002f, boundsHeight * 0.28f);
    }

    private bool TryBuildBrushProjectionFrame(Vector3 center, Vector3 normal, float worldRadius, out TextProjectionFrame frame)
    {
        frame = default;
        if (_worldEditorCamera == null || worldRadius <= 0.0001f)
        {
            return false;
        }

        if (normal.sqrMagnitude < 0.0001f)
        {
            normal = -_worldEditorCamera.transform.forward;
        }
        normal.Normalize();

        var cameraRight = _worldEditorCamera.transform.right;
        var cameraUp = _worldEditorCamera.transform.up;
        var right = Vector3.ProjectOnPlane(cameraRight, normal);
        if (right.sqrMagnitude < 0.0001f)
        {
            right = Vector3.Cross(Vector3.up, normal);
            if (right.sqrMagnitude < 0.0001f)
            {
                right = Vector3.Cross(Vector3.forward, normal);
            }
        }
        right.Normalize();

        var up = Vector3.ProjectOnPlane(cameraUp, normal);
        up -= right * Vector3.Dot(up, right);
        if (up.sqrMagnitude < 0.0001f)
        {
            up = Vector3.Cross(right, normal);
            if (up.sqrMagnitude < 0.0001f)
            {
                up = Vector3.Cross(normal, right);
            }
        }
        if (Vector3.Dot(up, cameraUp) < 0f)
        {
            up = -up;
        }
        up.Normalize();

        if (Vector3.Dot(right, cameraRight) < 0f)
        {
            right = -right;
        }

        frame = new TextProjectionFrame
        {
            Center = center,
            Normal = normal,
            Right = right,
            Up = up,
            WorldWidth = worldRadius * 2f,
            WorldHeight = worldRadius * 2f
        };
        return true;
    }

    private bool CompositeBrushSurfaceStroke(Texture2D target, Vector3 center, Vector3 normal, float worldRadius, bool mirrored, HashSet<int> touchedPixels, out TextSurfaceStampStats stats, out string fallbackReason)
    {
        stats = default;
        stats.Mirrored = mirrored;
        stats.WorldWidth = worldRadius * 2f;
        stats.WorldHeight = worldRadius * 2f;
        fallbackReason = string.Empty;

        if (target == null || _worldPaintCollider == null || _worldPaintProxyObject == null)
        {
            fallbackReason = "surface brush dependencies missing";
            return false;
        }

        var baseTexture = DrawableSuitsPlugin.Registry.GetOrCreateState(_selectedSuitId)?.BaseTexture;
        if (_tool == EditorTool.Erase && baseTexture == null)
        {
            fallbackReason = "base texture missing";
            return false;
        }

        if (!TryBuildBrushProjectionFrame(center, normal, worldRadius, out var frame))
        {
            fallbackReason = "brush projection frame unavailable";
            return false;
        }

        stats.WorldWidth = frame.WorldWidth;
        stats.WorldHeight = frame.WorldHeight;
        var sampleDiameter = Mathf.Clamp(Mathf.CeilToInt(EffectiveBrushRadiusPixels() * 1.25f), 10, 96);
        var gridWidth = sampleDiameter + 1;
        var gridHeight = sampleDiameter + 1;
        var grid = new SurfaceStampGridSample[gridWidth * gridHeight];
        var rayOffset = Mathf.Max(0.08f, worldRadius * 3f);
        var rayDistance = rayOffset * 2.5f;
        var projectedSamples = new Dictionary<int, SurfaceStampSample>();
        var seamThreshold = GetBrushProjectionSeamThreshold(target);
        var paint = _tool == EditorTool.Paint;
        var shapeStats = new BrushShapeStats { Available = true, Mirrored = mirrored };

        for (var gy = 0; gy < gridHeight; gy++)
        {
            var v = gy / Mathf.Max(1f, sampleDiameter);
            var localY = (v - 0.5f) * frame.WorldHeight;
            for (var gx = 0; gx < gridWidth; gx++)
            {
                var u = gx / Mathf.Max(1f, sampleDiameter);
                var localX = (u - 0.5f) * frame.WorldWidth;
                stats.ProjectionSamples++;
                var previousRandomSkipped = shapeStats.RandomSkippedSamples;
                if (!TryEvaluateBrushShape(localX, localY, worldRadius, gx - sampleDiameter / 2, gy - sampleDiameter / 2, paint, out var alpha, ref shapeStats))
                {
                    if (shapeStats.RandomSkippedSamples > previousRandomSkipped)
                    {
                        stats.RandomSkippedSamples++;
                        stats.SkippedPixels++;
                    }
                    continue;
                }

                stats.AcceptedSamples++;
                var planePoint = frame.Center + (frame.Right * localX) + (frame.Up * localY);
                if (!TryRaycastSurfaceStampPoint(planePoint, frame.Normal, rayOffset, rayDistance, out var surfaceHit))
                {
                    stats.OffSuitSamples++;
                    stats.SkippedPixels++;
                    continue;
                }

                var pixel = TexturePixel(target, surfaceHit.textureCoord);
                if (pixel.x < 0 || pixel.y < 0 || pixel.x >= target.width || pixel.y >= target.height)
                {
                    stats.OffSuitSamples++;
                    stats.SkippedPixels++;
                    continue;
                }

                var sample = new SurfaceStampGridSample
                {
                    Valid = true,
                    StampUv = new Vector2(u, v),
                    SurfaceUv = surfaceHit.textureCoord,
                    Pixel = new Vector2(
                        surfaceHit.textureCoord.x * (target.width - 1),
                        surfaceHit.textureCoord.y * (target.height - 1)),
                    Alpha = alpha
                };
                grid[GridIndex(gx, gy, gridWidth)] = sample;
                stats.SurfaceHits++;
                AddProjectedBrushPixel(target, baseTexture, sample.Pixel, sample.Alpha, projectedSamples, ref stats);
            }
        }

        if (BrushShapeUsesCoverageRasterization(_brushShape))
        {
            for (var y = 0; y < sampleDiameter; y++)
            {
                for (var x = 0; x < sampleDiameter; x++)
                {
                    var s00 = grid[GridIndex(x, y, gridWidth)];
                    var s10 = grid[GridIndex(x + 1, y, gridWidth)];
                    var s01 = grid[GridIndex(x, y + 1, gridWidth)];
                    var s11 = grid[GridIndex(x + 1, y + 1, gridWidth)];
                    if (!s00.Valid || !s10.Valid || !s01.Valid || !s11.Valid)
                    {
                        continue;
                    }

                    if (CellCrossesProjectionSeam(s00, s10, s01, s11, seamThreshold))
                    {
                        stats.SeamSkippedCells++;
                        continue;
                    }

                    stats.RasterizedCells++;
                    RasterizeProjectedBrushTriangle(target, baseTexture, s00, s10, s01, projectedSamples, ref stats);
                    RasterizeProjectedBrushTriangle(target, baseTexture, s10, s11, s01, projectedSamples, ref stats);
                }
            }
        }

        foreach (var pair in projectedSamples)
        {
            var x = pair.Key % target.width;
            var y = pair.Key / target.width;
            if (!TryMarkTouchedPixel(target, x, y, touchedPixels))
            {
                continue;
            }

            var existing = target.GetPixel(x, y);
            var targetColor = new Color(pair.Value.Color.r, pair.Value.Color.g, pair.Value.Color.b, existing.a);
            target.SetPixel(x, y, Color.Lerp(existing, targetColor, pair.Value.Alpha));
            stats.WrittenPixels++;
        }

        LogBrushProjectionCoverageWarning(stats, mirrored, sampleDiameter, seamThreshold);
        return stats.WrittenPixels > 0;
    }

    private static float GetBrushProjectionSeamThreshold(Texture2D target)
    {
        if (target == null)
        {
            return 20f;
        }

        return Mathf.Clamp(Mathf.Max(target.width, target.height) / 36f, 16f, 56f);
    }

    private void RasterizeProjectedBrushTriangle(Texture2D target, Texture2D baseTexture, SurfaceStampGridSample a, SurfaceStampGridSample b, SurfaceStampGridSample c, Dictionary<int, SurfaceStampSample> projectedSamples, ref TextSurfaceStampStats stats)
    {
        var minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.Pixel.x, Mathf.Min(b.Pixel.x, c.Pixel.x))));
        var maxX = Mathf.Min(target.width - 1, Mathf.CeilToInt(Mathf.Max(a.Pixel.x, Mathf.Max(b.Pixel.x, c.Pixel.x))));
        var minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.Pixel.y, Mathf.Min(b.Pixel.y, c.Pixel.y))));
        var maxY = Mathf.Min(target.height - 1, Mathf.CeilToInt(Mathf.Max(a.Pixel.y, Mathf.Max(b.Pixel.y, c.Pixel.y))));
        if (maxX < minX || maxY < minY)
        {
            return;
        }

        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                var point = new Vector2(x + 0.5f, y + 0.5f);
                if (!TryBarycentric2D(point, a.Pixel, b.Pixel, c.Pixel, out var barycentric, 0.001f))
                {
                    continue;
                }

                var alpha = (a.Alpha * barycentric.x) + (b.Alpha * barycentric.y) + (c.Alpha * barycentric.z);
                AddProjectedBrushPixel(target, baseTexture, new Vector2(x, y), alpha, projectedSamples, ref stats);
            }
        }
    }

    private void AddProjectedBrushPixel(Texture2D target, Texture2D baseTexture, Vector2 pixel, float alpha, Dictionary<int, SurfaceStampSample> projectedSamples, ref TextSurfaceStampStats stats)
    {
        if (target == null || projectedSamples == null || alpha <= 0.001f)
        {
            return;
        }

        var x = Mathf.Clamp(Mathf.RoundToInt(pixel.x), 0, target.width - 1);
        var y = Mathf.Clamp(Mathf.RoundToInt(pixel.y), 0, target.height - 1);
        var brushTarget = _tool == EditorTool.Erase && baseTexture != null
            ? baseTexture.GetPixel(x, y)
            : _brushColor;
        var index = (y * target.width) + x;
        var effectiveAlpha = Mathf.Clamp01(alpha);
        var projected = new SurfaceStampSample
        {
            Color = brushTarget,
            Alpha = effectiveAlpha
        };

        stats.AlphaPixels++;
        if (projectedSamples.TryGetValue(index, out var existingSample))
        {
            if (effectiveAlpha > existingSample.Alpha)
            {
                projectedSamples[index] = projected;
            }
        }
        else
        {
            projectedSamples[index] = projected;
        }
    }

    private void LogBrushProjectionCoverageWarning(TextSurfaceStampStats stats, bool mirrored, int sampleDiameter, float seamThreshold)
    {
        var totalCells = stats.RasterizedCells + stats.SeamSkippedCells;
        if (stats.SeamSkippedCells < 6 || totalCells <= 0 || stats.SeamSkippedCells < totalCells * 0.12f)
        {
            return;
        }

        var key = $"tool={_tool}|shape={_brushShape}|mirrored={mirrored}|sample={sampleDiameter}|seam={stats.SeamSkippedCells}|cells={stats.RasterizedCells}|threshold={Mathf.RoundToInt(seamThreshold)}|brush={Mathf.RoundToInt(EffectiveBrushRadiusPixels())}";
        if (Time.unscaledTime - _lastBrushSurfaceWarningTime < 2f && string.Equals(key, _lastBrushSurfaceWarningKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastBrushSurfaceWarningTime = Time.unscaledTime;
        _lastBrushSurfaceWarningKey = key;
        DrawableSuitsDiagnostics.Warn($"BrushSurfaceProjectionWarning: {key}; projectionSamples={stats.ProjectionSamples}; surfaceHits={stats.SurfaceHits}; offSuit={stats.OffSuitSamples}; written={stats.WrittenPixels}; skipped={stats.SkippedPixels}; pointerSource={_pointerSource}");
    }

    private bool ApplyDirectBrush(Texture2D texture, Vector2 uv, bool mirrored, HashSet<int> touchedPixels, out BrushShapeStats stats)
    {
        stats = default;
        stats.Available = true;
        stats.Mirrored = mirrored;
        if (texture == null)
        {
            return false;
        }

        var baseTexture = DrawableSuitsPlugin.Registry.GetOrCreateState(_selectedSuitId)?.BaseTexture;
        if (_tool == EditorTool.Erase && baseTexture == null)
        {
            return false;
        }

        var cx = Mathf.RoundToInt(uv.x * (texture.width - 1));
        var cy = Mathf.RoundToInt(uv.y * (texture.height - 1));
        if (cx < 0 || cy < 0 || cx >= texture.width || cy >= texture.height)
        {
            return false;
        }

        if (_brushShape == BrushShape.Pixel)
        {
            stats.CheckedSamples = 1;
            stats.AcceptedSamples = 1;
            return ApplyDirectBrushPixel(texture, baseTexture, cx, cy, 1f, touchedPixels, ref stats);
        }

        var radius = Mathf.Max(1f, EffectiveBrushRadiusPixels());
        var r = Mathf.CeilToInt(radius);
        var xMin = Mathf.Max(0, cx - r);
        var xMax = Mathf.Min(texture.width - 1, cx + r);
        var yMin = Mathf.Max(0, cy - r);
        var yMax = Mathf.Min(texture.height - 1, cy + r);
        var changed = false;
        for (var y = yMin; y <= yMax; y++)
        {
            for (var x = xMin; x <= xMax; x++)
            {
                var dx = x - cx;
                var dy = y - cy;
                if (!TryEvaluateBrushShape(dx, dy, radius, dx, dy, _tool == EditorTool.Paint, out var alpha, ref stats))
                {
                    continue;
                }

                changed |= ApplyDirectBrushPixel(texture, baseTexture, x, y, alpha, touchedPixels, ref stats);
            }
        }

        return changed;
    }

    private bool ApplyDirectBrushPixel(Texture2D texture, Texture2D baseTexture, int x, int y, float alpha, HashSet<int> touchedPixels, ref BrushShapeStats stats)
    {
        if (texture == null || alpha <= 0.001f || x < 0 || y < 0 || x >= texture.width || y >= texture.height)
        {
            return false;
        }

        if (!TryMarkTouchedPixel(texture, x, y, touchedPixels))
        {
            return false;
        }

        var existing = texture.GetPixel(x, y);
        var targetColor = _tool == EditorTool.Erase && baseTexture != null
            ? baseTexture.GetPixel(x, y)
            : _brushColor;
        texture.SetPixel(x, y, Color.Lerp(existing, targetColor, Mathf.Clamp01(alpha)));
        stats.WrittenPixels++;
        return true;
    }

    private bool TryEvaluateBrushShape(float x, float y, float radius, int sampleX, int sampleY, bool paint, out float alpha, ref BrushShapeStats stats)
    {
        stats.CheckedSamples++;
        alpha = 0f;
        radius = Mathf.Max(0.0001f, radius);
        var absX = Mathf.Abs(x);
        var absY = Mathf.Abs(y);
        var distance = Mathf.Sqrt((x * x) + (y * y));
        var normalized = distance / radius;
        var opacity = Mathf.Clamp01(_brushOpacity);

        switch (_brushShape)
        {
            case BrushShape.Square:
                if (absX > radius || absY > radius)
                {
                    return false;
                }
                alpha = opacity;
                stats.AcceptedSamples++;
                return true;
            case BrushShape.SoftAirbrush:
                if (normalized > 1f)
                {
                    return false;
                }
                alpha = paint
                    ? opacity * Mathf.Pow(Mathf.Clamp01(1f - normalized), 2f)
                    : opacity;
                stats.AcceptedSamples++;
                return alpha > 0.001f;
            case BrushShape.SprayPaint:
                if (normalized > 1f)
                {
                    return false;
                }
                if (BrushRandom01(_brushStrokeSeed, sampleX, sampleY, 17) > Mathf.Lerp(0.36f, 0.12f, Mathf.Clamp01(normalized)))
                {
                    stats.RandomSkippedSamples++;
                    return false;
                }
                alpha = paint
                    ? opacity * Mathf.Lerp(0.5f, 1f, BrushRandom01(_brushStrokeSeed, sampleX, sampleY, 23))
                    : opacity;
                stats.AcceptedSamples++;
                return true;
            case BrushShape.NoiseScatter:
                if (normalized > 1f)
                {
                    return false;
                }
                if (BrushRandom01(_brushStrokeSeed, sampleX, sampleY, 31) > 0.58f)
                {
                    stats.RandomSkippedSamples++;
                    return false;
                }
                alpha = paint
                    ? opacity * Mathf.Lerp(0.35f, 1f, BrushRandom01(_brushStrokeSeed, sampleX, sampleY, 43))
                    : opacity;
                stats.AcceptedSamples++;
                return true;
            case BrushShape.Pixel:
                if (absX > 0.5f || absY > 0.5f)
                {
                    return false;
                }
                alpha = opacity;
                stats.AcceptedSamples++;
                return true;
            case BrushShape.Circle:
            default:
                if (normalized > 1f)
                {
                    return false;
                }
                alpha = paint
                    ? opacity * Mathf.Clamp01(1f - normalized + 0.25f)
                    : opacity;
                stats.AcceptedSamples++;
                return true;
        }
    }

    private static float BrushRandom01(int seed, int x, int y, int salt)
    {
        unchecked
        {
            var hash = (uint)seed;
            hash ^= (uint)(x * 73856093);
            hash ^= (uint)(y * 19349663);
            hash ^= (uint)(salt * 83492791);
            hash ^= hash >> 13;
            hash *= 1274126177u;
            hash ^= hash >> 16;
            return (hash & 0x00FFFFFF) / 16777215f;
        }
    }

    private float EffectiveBrushRadiusPixels()
    {
        return _brushShape == BrushShape.Pixel ? 0.5f : Mathf.Max(1f, _brushSize);
    }

    private static bool BrushShapeUsesCoverageRasterization(BrushShape shape)
    {
        return shape == BrushShape.Circle
            || shape == BrushShape.Square
            || shape == BrushShape.SoftAirbrush;
    }

    private bool PaintCircle(Texture2D texture, Vector2 uv, Color color, float radius, float opacity, HashSet<int> touchedPixels = null)
    {
        var cx = Mathf.RoundToInt(uv.x * (texture.width - 1));
        var cy = Mathf.RoundToInt(uv.y * (texture.height - 1));
        var r = Mathf.RoundToInt(radius);
        var r2 = r * r;
        var xMin = Mathf.Max(0, cx - r);
        var xMax = Mathf.Min(texture.width - 1, cx + r);
        var yMin = Mathf.Max(0, cy - r);
        var yMax = Mathf.Min(texture.height - 1, cy + r);

        var changed = false;
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
                if (!TryMarkTouchedPixel(texture, x, y, touchedPixels))
                {
                    continue;
                }
                var falloff = 1f - Mathf.Sqrt(dx * dx + dy * dy) / Mathf.Max(1f, r);
                var existing = texture.GetPixel(x, y);
                texture.SetPixel(x, y, Color.Lerp(existing, color, opacity * Mathf.Clamp01(falloff + 0.25f)));
                changed = true;
            }
        }
        return changed;
    }

    private bool EraseCircle(Texture2D texture, Vector2 uv, float radius, float opacity, HashSet<int> touchedPixels = null)
    {
        var state = DrawableSuitsPlugin.Registry.GetOrCreateState(_selectedSuitId);
        if (state?.BaseTexture == null)
        {
            return false;
        }

        var cx = Mathf.RoundToInt(uv.x * (texture.width - 1));
        var cy = Mathf.RoundToInt(uv.y * (texture.height - 1));
        var r = Mathf.RoundToInt(radius);
        var r2 = r * r;
        var xMin = Mathf.Max(0, cx - r);
        var xMax = Mathf.Min(texture.width - 1, cx + r);
        var yMin = Mathf.Max(0, cy - r);
        var yMax = Mathf.Min(texture.height - 1, cy + r);

        var changed = false;
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
                if (!TryMarkTouchedPixel(texture, x, y, touchedPixels))
                {
                    continue;
                }
                var existing = texture.GetPixel(x, y);
                var original = state.BaseTexture.GetPixel(x, y);
                texture.SetPixel(x, y, Color.Lerp(existing, original, opacity));
                changed = true;
            }
        }
        return changed;
    }

    private bool ApplyDecal(Texture2D target, Vector2 uv, bool mirrored = false, HashSet<int> touchedPixels = null)
    {
        if (!TryGetActivePlacementStamp(out var stampTexture, out var failureReason))
        {
            WarnMissingDecal("ApplyDecal");
            if (!string.IsNullOrWhiteSpace(failureReason))
            {
                DrawableSuitsDiagnostics.Info($"DecalStampSkipped: mode=ApplyDecal; reason={failureReason}; decal={CurrentDecalName()}; suit={_selectedSuitId}");
            }
            return false;
        }

        return CompositePlacementStamp(target, stampTexture, uv, _decalSize, mirrored ? -_decalRotation : _decalRotation, _brushOpacity, mirrored, touchedPixels);
    }

    private bool ApplyTextStamp(Texture2D target, Vector2 uv, bool mirrored = false, HashSet<int> touchedPixels = null)
    {
        if (!TryGetTextStampTexture(out var stampTexture, out var failureReason))
        {
            LogTextStampSkipped("ApplyTextStamp", failureReason);
            return false;
        }

        return CompositePlacementStamp(target, stampTexture, uv, _textSize, mirrored ? -_textRotation : _textRotation, _brushOpacity, mirrored, touchedPixels, true);
    }

    private bool ApplyStickerStamp(Texture2D target, Vector2 uv, bool mirrored = false, HashSet<int> touchedPixels = null)
    {
        if (!TryGetActivePlacementStamp(out var stampTexture, out var failureReason))
        {
            LogStickerStampSkipped("ApplyStickerStamp", failureReason);
            return false;
        }

        return CompositePlacementStamp(target, stampTexture, uv, _stickerSize, mirrored ? -_stickerRotation : _stickerRotation, _brushOpacity, mirrored, touchedPixels, false);
    }

    private bool CompositeTextSurfaceStamp(Texture2D target, Texture2D stamp, Vector3 center, Vector3 normal, float rotation, bool mirrored, float opacity, HashSet<int> touchedPixels, out TextSurfaceStampStats stats)
    {
        return CompositeSurfaceStamp(target, stamp, center, normal, rotation, CalculateTextWorldHeight(), mirrored, opacity, touchedPixels, true, out stats);
    }

    private bool CompositeDecalSurfaceStamp(Texture2D target, Texture2D stamp, Vector3 center, Vector3 normal, float rotation, bool mirrored, float opacity, HashSet<int> touchedPixels, out TextSurfaceStampStats stats, bool tintWithBrushColor = false)
    {
        var placementSize = _tool == EditorTool.Sticker ? _stickerSize : _decalSize;
        var sampleHeight = Mathf.Clamp(Mathf.RoundToInt(placementSize), 12, 160);
        var aspect = stamp != null ? stamp.width / Mathf.Max(1f, (float)stamp.height) : 1f;
        var sampleWidth = Mathf.Clamp(Mathf.RoundToInt(sampleHeight * aspect), 12, 256);
        var worldHeight = _tool == EditorTool.Sticker ? CalculateStickerWorldHeight() : CalculateDecalWorldHeight();
        return CompositeDecalSurfaceCoverageStamp(target, stamp, center, normal, rotation, worldHeight, mirrored, opacity, touchedPixels, sampleWidth, sampleHeight, out stats, tintWithBrushColor);
    }

    private bool CompositeDecalSurfaceCoverageStamp(Texture2D target, Texture2D stamp, Vector3 center, Vector3 normal, float rotation, float worldHeight, bool mirrored, float opacity, HashSet<int> touchedPixels, int sampleWidth, int sampleHeight, out TextSurfaceStampStats stats, bool tintWithBrushColor = false)
    {
        stats = default;
        stats.Mirrored = mirrored;
        if (target == null || stamp == null || _worldPaintCollider == null || _worldPaintProxyObject == null)
        {
            return false;
        }

        if (!TryBuildProjectionFrame(center, normal, rotation, stamp, worldHeight, mirrored, out var frame))
        {
            stats.SkippedPixels = Mathf.Max(1, sampleWidth) * Mathf.Max(1, sampleHeight);
            return false;
        }

        stats.WorldWidth = frame.WorldWidth;
        stats.WorldHeight = frame.WorldHeight;
        var gridWidth = Mathf.Max(2, sampleWidth + 1);
        var gridHeight = Mathf.Max(2, sampleHeight + 1);
        var grid = new SurfaceStampGridSample[gridWidth * gridHeight];
        var rayOffset = Mathf.Max(0.08f, frame.WorldHeight * 2.5f);
        var rayDistance = rayOffset * 2.5f;
        var projectedSamples = new Dictionary<int, SurfaceStampSample>();
        var seamThreshold = GetDecalProjectionSeamThreshold(target);
        var opacity01 = Mathf.Clamp01(opacity);

        for (var gy = 0; gy < gridHeight; gy++)
        {
            var v = gy / Mathf.Max(1f, sampleHeight);
            var localY = (v - 0.5f) * frame.WorldHeight;
            for (var gx = 0; gx < gridWidth; gx++)
            {
                var u = gx / Mathf.Max(1f, sampleWidth);
                var stampU = mirrored ? 1f - u : u;
                var planePoint = frame.Center + (frame.Right * ((u - 0.5f) * frame.WorldWidth)) + (frame.Up * localY);
                stats.ProjectionSamples++;
                if (!TryRaycastSurfaceStampPoint(planePoint, frame.Normal, rayOffset, rayDistance, out var surfaceHit))
                {
                    stats.OffSuitSamples++;
                    stats.SkippedPixels++;
                    continue;
                }

                var pixel = TexturePixel(target, surfaceHit.textureCoord);
                if (pixel.x < 0 || pixel.y < 0 || pixel.x >= target.width || pixel.y >= target.height)
                {
                    stats.OffSuitSamples++;
                    stats.SkippedPixels++;
                    continue;
                }

                var sample = new SurfaceStampGridSample
                {
                    Valid = true,
                    StampUv = new Vector2(Mathf.Clamp01(stampU), Mathf.Clamp01(v)),
                    SurfaceUv = surfaceHit.textureCoord,
                    Pixel = new Vector2(
                        surfaceHit.textureCoord.x * (target.width - 1),
                        surfaceHit.textureCoord.y * (target.height - 1))
                };
                grid[GridIndex(gx, gy, gridWidth)] = sample;
                stats.SurfaceHits++;
                AddProjectedStampPixel(target, stamp, sample.StampUv, sample.Pixel, opacity01, projectedSamples, ref stats, tintWithBrushColor);
            }
        }

        for (var y = 0; y < sampleHeight; y++)
        {
            for (var x = 0; x < sampleWidth; x++)
            {
                var s00 = grid[GridIndex(x, y, gridWidth)];
                var s10 = grid[GridIndex(x + 1, y, gridWidth)];
                var s01 = grid[GridIndex(x, y + 1, gridWidth)];
                var s11 = grid[GridIndex(x + 1, y + 1, gridWidth)];
                if (!s00.Valid || !s10.Valid || !s01.Valid || !s11.Valid)
                {
                    continue;
                }

                if (CellCrossesProjectionSeam(s00, s10, s01, s11, seamThreshold))
                {
                    stats.SeamSkippedCells++;
                    continue;
                }

                stats.RasterizedCells++;
                RasterizeProjectedDecalTriangle(target, stamp, s00, s10, s01, opacity01, projectedSamples, ref stats, tintWithBrushColor);
                RasterizeProjectedDecalTriangle(target, stamp, s10, s11, s01, opacity01, projectedSamples, ref stats, tintWithBrushColor);
            }
        }

        foreach (var pair in projectedSamples)
        {
            var x = pair.Key % target.width;
            var y = pair.Key / target.width;
            if (!TryMarkTouchedPixel(target, x, y, touchedPixels))
            {
                continue;
            }

            var existing = target.GetPixel(x, y);
            var targetColor = new Color(pair.Value.Color.r, pair.Value.Color.g, pair.Value.Color.b, existing.a);
            target.SetPixel(x, y, Color.Lerp(existing, targetColor, pair.Value.Alpha));
            stats.WrittenPixels++;
        }

        LogDecalProjectionCoverageWarning(stats, mirrored, sampleWidth, sampleHeight, seamThreshold);
        return stats.WrittenPixels > 0;
    }

    private static int GridIndex(int x, int y, int width)
    {
        return (y * width) + x;
    }

    private static float GetDecalProjectionSeamThreshold(Texture2D target)
    {
        if (target == null)
        {
            return 24f;
        }

        return Mathf.Clamp(Mathf.Max(target.width, target.height) / 32f, 18f, 64f);
    }

    private static bool CellCrossesProjectionSeam(SurfaceStampGridSample s00, SurfaceStampGridSample s10, SurfaceStampGridSample s01, SurfaceStampGridSample s11, float seamThreshold)
    {
        var edgeThreshold = Mathf.Max(1f, seamThreshold);
        var diagonalThreshold = edgeThreshold * 1.75f;
        return Vector2.Distance(s00.Pixel, s10.Pixel) > edgeThreshold
            || Vector2.Distance(s10.Pixel, s11.Pixel) > edgeThreshold
            || Vector2.Distance(s11.Pixel, s01.Pixel) > edgeThreshold
            || Vector2.Distance(s01.Pixel, s00.Pixel) > edgeThreshold
            || Vector2.Distance(s00.Pixel, s11.Pixel) > diagonalThreshold
            || Vector2.Distance(s10.Pixel, s01.Pixel) > diagonalThreshold;
    }

    private void RasterizeProjectedDecalTriangle(Texture2D target, Texture2D stamp, SurfaceStampGridSample a, SurfaceStampGridSample b, SurfaceStampGridSample c, float opacity, Dictionary<int, SurfaceStampSample> projectedSamples, ref TextSurfaceStampStats stats, bool tintWithBrushColor)
    {
        var minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.Pixel.x, Mathf.Min(b.Pixel.x, c.Pixel.x))));
        var maxX = Mathf.Min(target.width - 1, Mathf.CeilToInt(Mathf.Max(a.Pixel.x, Mathf.Max(b.Pixel.x, c.Pixel.x))));
        var minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.Pixel.y, Mathf.Min(b.Pixel.y, c.Pixel.y))));
        var maxY = Mathf.Min(target.height - 1, Mathf.CeilToInt(Mathf.Max(a.Pixel.y, Mathf.Max(b.Pixel.y, c.Pixel.y))));
        if (maxX < minX || maxY < minY)
        {
            return;
        }

        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                var point = new Vector2(x + 0.5f, y + 0.5f);
                if (!TryBarycentric2D(point, a.Pixel, b.Pixel, c.Pixel, out var barycentric, 0.001f))
                {
                    continue;
                }

                var stampUv = (a.StampUv * barycentric.x) + (b.StampUv * barycentric.y) + (c.StampUv * barycentric.z);
                AddProjectedStampPixel(target, stamp, stampUv, new Vector2(x, y), opacity, projectedSamples, ref stats, tintWithBrushColor);
            }
        }
    }

    private void AddProjectedStampPixel(Texture2D target, Texture2D stamp, Vector2 stampUv, Vector2 pixel, float opacity, Dictionary<int, SurfaceStampSample> projectedSamples, ref TextSurfaceStampStats stats, bool tintWithBrushColor)
    {
        if (target == null || stamp == null || projectedSamples == null)
        {
            return;
        }

        var x = Mathf.Clamp(Mathf.RoundToInt(pixel.x), 0, target.width - 1);
        var y = Mathf.Clamp(Mathf.RoundToInt(pixel.y), 0, target.height - 1);
        var sample = stamp.GetPixelBilinear(Mathf.Clamp01(stampUv.x), Mathf.Clamp01(stampUv.y));
        if (sample.a <= 0.01f)
        {
            return;
        }

        stats.AlphaPixels++;
        var effectiveAlpha = sample.a * Mathf.Clamp01(opacity);
        var index = (y * target.width) + x;
        var projected = new SurfaceStampSample
        {
            Color = tintWithBrushColor
                ? new Color(_brushColor.r, _brushColor.g, _brushColor.b, 1f)
                : sample,
            Alpha = effectiveAlpha
        };

        if (projectedSamples.TryGetValue(index, out var existingSample))
        {
            if (effectiveAlpha > existingSample.Alpha)
            {
                projectedSamples[index] = projected;
            }
        }
        else
        {
            projectedSamples[index] = projected;
        }
    }

    private void LogDecalProjectionCoverageWarning(TextSurfaceStampStats stats, bool mirrored, int sampleWidth, int sampleHeight, float seamThreshold)
    {
        var totalCells = stats.RasterizedCells + stats.SeamSkippedCells;
        if (stats.SeamSkippedCells < 6 || totalCells <= 0 || stats.SeamSkippedCells < totalCells * 0.12f)
        {
            return;
        }

        var key = $"mirrored={mirrored}|sample={sampleWidth}x{sampleHeight}|seam={stats.SeamSkippedCells}|cells={stats.RasterizedCells}|threshold={Mathf.RoundToInt(seamThreshold)}|decal={CurrentDecalName()}";
        if (Time.unscaledTime - _lastDecalCoverageWarningTime < 2f && string.Equals(key, _lastDecalCoverageWarningKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastDecalCoverageWarningTime = Time.unscaledTime;
        _lastDecalCoverageWarningKey = key;
        DrawableSuitsDiagnostics.Warn($"DecalProjectionCoverageWarning: {key}; projectionSamples={stats.ProjectionSamples}; surfaceHits={stats.SurfaceHits}; offSuit={stats.OffSuitSamples}; written={stats.WrittenPixels}; skipped={stats.SkippedPixels}; pointerSource={_pointerSource}");
    }

    private bool CompositeSurfaceStamp(Texture2D target, Texture2D stamp, Vector3 center, Vector3 normal, float rotation, float worldHeight, bool mirrored, float opacity, HashSet<int> touchedPixels, bool tintWithBrushColor, out TextSurfaceStampStats stats, int sampleWidth = 0, int sampleHeight = 0)
    {
        stats = default;
        stats.Mirrored = mirrored;
        if (target == null || stamp == null || _worldPaintCollider == null || _worldPaintProxyObject == null)
        {
            return false;
        }

        if (!TryBuildProjectionFrame(center, normal, rotation, stamp, worldHeight, mirrored, out var frame))
        {
            stats.SkippedPixels = Mathf.Max(1, sampleWidth > 0 ? sampleWidth : stamp.width) * Mathf.Max(1, sampleHeight > 0 ? sampleHeight : stamp.height);
            return false;
        }

        stats.WorldWidth = frame.WorldWidth;
        stats.WorldHeight = frame.WorldHeight;
        var projectedSamples = new Dictionary<int, SurfaceStampSample>();
        var rayOffset = Mathf.Max(0.08f, frame.WorldHeight * 2.5f);
        var rayDistance = rayOffset * 2.5f;
        var drawWidth = Mathf.Max(1, sampleWidth > 0 ? sampleWidth : stamp.width);
        var drawHeight = Mathf.Max(1, sampleHeight > 0 ? sampleHeight : stamp.height);

        for (var y = 0; y < drawHeight; y++)
        {
            var v = (y + 0.5f) / Mathf.Max(1f, drawHeight);
            var localY = (v - 0.5f) * frame.WorldHeight;
            for (var x = 0; x < drawWidth; x++)
            {
                var u = (x + 0.5f) / Mathf.Max(1f, drawWidth);
                var sampleU = mirrored ? 1f - u : u;
                var sample = stamp.GetPixelBilinear(Mathf.Clamp01(sampleU), Mathf.Clamp01(v));
                if (sample.a <= 0.01f)
                {
                    continue;
                }

                stats.AlphaPixels++;
                var localX = (u - 0.5f) * frame.WorldWidth;
                var planePoint = frame.Center + (frame.Right * localX) + (frame.Up * localY);
                if (!TryRaycastSurfaceStampPoint(planePoint, frame.Normal, rayOffset, rayDistance, out var surfaceHit))
                {
                    stats.SkippedPixels++;
                    continue;
                }

                var pixel = TexturePixel(target, surfaceHit.textureCoord);
                if (pixel.x < 0 || pixel.y < 0 || pixel.x >= target.width || pixel.y >= target.height)
                {
                    stats.SkippedPixels++;
                    continue;
                }

                var index = (pixel.y * target.width) + pixel.x;
                var effectiveAlpha = sample.a * Mathf.Clamp01(opacity);
                var projected = new SurfaceStampSample
                {
                    Color = tintWithBrushColor
                        ? new Color(_brushColor.r, _brushColor.g, _brushColor.b, 1f)
                        : sample,
                    Alpha = effectiveAlpha
                };

                if (projectedSamples.TryGetValue(index, out var existingSample))
                {
                    if (effectiveAlpha > existingSample.Alpha)
                    {
                        projectedSamples[index] = projected;
                    }
                }
                else
                {
                    projectedSamples[index] = projected;
                }
            }
        }

        foreach (var pair in projectedSamples)
        {
            var x = pair.Key % target.width;
            var y = pair.Key / target.width;
            if (!TryMarkTouchedPixel(target, x, y, touchedPixels))
            {
                continue;
            }

            var existing = target.GetPixel(x, y);
            var targetColor = new Color(pair.Value.Color.r, pair.Value.Color.g, pair.Value.Color.b, existing.a);
            target.SetPixel(x, y, Color.Lerp(existing, targetColor, pair.Value.Alpha));
            stats.WrittenPixels++;
        }

        return stats.WrittenPixels > 0;
    }

    private bool TryBuildProjectionFrame(Vector3 center, Vector3 normal, float rotation, Texture2D stamp, float worldHeight, bool mirrored, out TextProjectionFrame frame)
    {
        frame = default;
        if (stamp == null || _worldEditorCamera == null)
        {
            return false;
        }

        var normalMagnitude = normal.sqrMagnitude;
        if (normalMagnitude < 0.0001f)
        {
            normal = _worldEditorCamera.transform.forward * -1f;
        }
        normal.Normalize();

        var cameraRight = _worldEditorCamera.transform.right;
        var cameraUp = _worldEditorCamera.transform.up;
        var right = Vector3.ProjectOnPlane(cameraRight, normal);
        if (right.sqrMagnitude < 0.0001f)
        {
            right = Vector3.Cross(Vector3.up, normal);
            if (right.sqrMagnitude < 0.0001f)
            {
                right = Vector3.Cross(Vector3.forward, normal);
            }
        }
        right.Normalize();

        var up = Vector3.ProjectOnPlane(cameraUp, normal);
        up -= right * Vector3.Dot(up, right);
        if (up.sqrMagnitude < 0.0001f)
        {
            up = Vector3.Cross(right, normal);
            if (up.sqrMagnitude < 0.0001f)
            {
                up = Vector3.Cross(normal, right);
            }
        }

        if (Vector3.Dot(up, cameraUp) < 0f)
        {
            up = -up;
        }
        up.Normalize();

        // Keep positive text X aligned with the editor camera's projected right.
        // Recomputing this with Cross(up, normal) flips the basis for surfaces facing the camera.
        if (Vector3.Dot(right, cameraRight) < 0f)
        {
            right = -right;
        }

        var rotationQuat = Quaternion.AngleAxis(rotation, normal);
        right = rotationQuat * right;
        up = rotationQuat * up;

        var aspect = stamp.width / Mathf.Max(1f, (float)stamp.height);
        frame = new TextProjectionFrame
        {
            Center = center,
            Normal = normal,
            Right = right,
            Up = up,
            WorldHeight = worldHeight,
            WorldWidth = Mathf.Max(worldHeight * 0.1f, worldHeight * aspect)
        };
        LogTextProjectionFrameBuilt(frame, rotation, mirrored, false);
        return true;
    }

    private void LogTextProjectionFrameBuilt(TextProjectionFrame frame, float rotation, bool mirrored, bool force)
    {
        if (_worldEditorCamera == null)
        {
            return;
        }

        var cameraRight = _worldEditorCamera.transform.right;
        var cameraUp = _worldEditorCamera.transform.up;
        var cameraForward = _worldEditorCamera.transform.forward;
        var rightDot = Vector3.Dot(frame.Right.normalized, cameraRight);
        var upDot = Vector3.Dot(frame.Up.normalized, cameraUp);
        var handedness = Vector3.Dot(Vector3.Cross(frame.Right.normalized, frame.Up.normalized), frame.Normal.normalized);
        var cameraFacing = Vector3.Dot(frame.Normal.normalized, cameraForward);
        var sampleOrder = mirrored ? "flipped" : "normal";
        var key = $"side={(mirrored ? "Mirrored" : "Primary")}|rot={Mathf.RoundToInt(rotation * 10f)}|right={rightDot:0.###}|up={upDot:0.###}|hand={handedness:0.###}|normal={frame.Normal.x:0.##},{frame.Normal.y:0.##},{frame.Normal.z:0.##}|size={frame.WorldWidth:0.###}x{frame.WorldHeight:0.###}|tool={_tool}|mirror={_mirrorEnabled}";
        if (!force && Time.unscaledTime - _lastTextProjectionFrameLogTime < 0.75f && string.Equals(key, _lastTextProjectionFrameLogKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastTextProjectionFrameLogTime = Time.unscaledTime;
        _lastTextProjectionFrameLogKey = key;
        DrawableSuitsDiagnostics.Info($"TextProjectionFrameBuilt: {key}; cameraFacing={cameraFacing:0.###}; sampleOrder={sampleOrder}; center={frame.Center}; right={frame.Right}; up={frame.Up}; normal={frame.Normal}; pointerSource={_pointerSource}; cursor={_cursor}");
    }

    private float CalculateTextWorldHeight()
    {
        var boundsHeight = _worldAvatarRenderer != null ? _worldAvatarRenderer.bounds.size.y : 1.8f;
        if (boundsHeight <= 0.01f)
        {
            boundsHeight = 1.8f;
        }

        return Mathf.Clamp((_textSize / 512f) * boundsHeight, boundsHeight * 0.012f, boundsHeight * 0.45f);
    }

    private float CalculateDecalWorldHeight()
    {
        var boundsHeight = _worldAvatarRenderer != null ? _worldAvatarRenderer.bounds.size.y : 1.8f;
        if (boundsHeight <= 0.01f)
        {
            boundsHeight = 1.8f;
        }

        return Mathf.Clamp((_decalSize / 512f) * boundsHeight, boundsHeight * 0.012f, boundsHeight * 0.55f);
    }

    private float CalculateStickerWorldHeight()
    {
        var boundsHeight = _worldAvatarRenderer != null ? _worldAvatarRenderer.bounds.size.y : 1.8f;
        if (boundsHeight <= 0.01f)
        {
            boundsHeight = 1.8f;
        }

        return Mathf.Clamp((_stickerSize / 512f) * boundsHeight, boundsHeight * 0.012f, boundsHeight * 0.55f);
    }

    private bool TryRaycastSurfaceStampPoint(Vector3 planePoint, Vector3 normal, float offset, float distance, out RaycastHit hit)
    {
        hit = default;
        if (_worldPaintCollider == null)
        {
            return false;
        }

        var ray = new Ray(planePoint + normal * offset, -normal);
        if (_worldPaintCollider.Raycast(ray, out hit, distance))
        {
            return true;
        }

        ray = new Ray(planePoint - normal * offset, normal);
        return _worldPaintCollider.Raycast(ray, out hit, distance);
    }

    private bool TryGetMirrorWorldPlacement(MirrorPaintTarget mirrorTarget, out Vector3 point, out Vector3 normal)
    {
        point = default;
        normal = default;
        if (!mirrorTarget.Enabled || !mirrorTarget.Available || _worldPaintProxyObject == null)
        {
            return false;
        }

        point = _worldPaintProxyObject.transform.TransformPoint(mirrorTarget.MirroredLocalPoint);
        normal = mirrorTarget.MirroredLocalNormal.sqrMagnitude > 0.0001f
            ? _worldPaintProxyObject.transform.TransformDirection(mirrorTarget.MirroredLocalNormal).normalized
            : (point - _worldPaintProxyObject.transform.position).normalized;
        if (normal.sqrMagnitude < 0.0001f)
        {
            normal = _worldEditorCamera != null ? -_worldEditorCamera.transform.forward : Vector3.up;
        }

        return true;
    }

    private bool CompositePlacementStamp(Texture2D target, Texture2D stamp, Vector2 uv, float stampHeight, float stampRotation, float opacity, bool flipHorizontal = false, HashSet<int> touchedPixels = null, bool tintWithBrushColor = false)
    {
        if (target == null || stamp == null)
        {
            return false;
        }

        var centerX = Mathf.RoundToInt(uv.x * (target.width - 1));
        var centerY = Mathf.RoundToInt(uv.y * (target.height - 1));
        var stampSize = GetPlacementStampPixelSize(stamp);
        var width = Mathf.Max(1f, stampSize.x);
        var height = Mathf.Max(1f, _tool == EditorTool.Text ? stampSize.y : stampHeight);
        var halfWidth = Mathf.Max(1, Mathf.CeilToInt(width * 0.5f));
        var halfHeight = Mathf.Max(1, Mathf.CeilToInt(height * 0.5f));
        var radians = stampRotation * Mathf.Deg2Rad;
        var cos = Mathf.Cos(radians);
        var sin = Mathf.Sin(radians);
        var changed = false;

        for (var y = -halfHeight; y <= halfHeight; y++)
        {
            for (var x = -halfWidth; x <= halfWidth; x++)
            {
                var u = (x * cos - y * sin) / width + 0.5f;
                var v = (x * sin + y * cos) / height + 0.5f;
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
                if (flipHorizontal)
                {
                    u = 1f - u;
                }

                var stampColor = stamp.GetPixelBilinear(u, v);
                var stampAlpha = stampColor.a;
                if (stampAlpha <= 0.01f)
                {
                    continue;
                }

                if (!TryMarkTouchedPixel(target, tx, ty, touchedPixels))
                {
                    continue;
                }

                var existing = target.GetPixel(tx, ty);
                if (tintWithBrushColor)
                {
                    var targetColor = new Color(_brushColor.r, _brushColor.g, _brushColor.b, existing.a);
                    target.SetPixel(tx, ty, Color.Lerp(existing, targetColor, stampAlpha * Mathf.Clamp01(opacity)));
                }
                else
                {
                    target.SetPixel(tx, ty, Color.Lerp(existing, stampColor, stampAlpha * Mathf.Clamp01(opacity)));
                }
                changed = true;
            }
        }

        return changed;
    }

    private static bool TryMarkTouchedPixel(Texture2D texture, int x, int y, HashSet<int> touchedPixels)
    {
        if (touchedPixels == null || texture == null)
        {
            return true;
        }

        return touchedPixels.Add((y * texture.width) + x);
    }

    private static Vector2 ClampUv(Vector2 uv)
    {
        return new Vector2(Mathf.Clamp01(uv.x), Mathf.Clamp01(uv.y));
    }

    private static float Cross2D(Vector2 a, Vector2 b)
    {
        return (a.x * b.y) - (a.y * b.x);
    }

    private static bool TryBarycentric2D(Vector2 p, Vector2 a, Vector2 b, Vector2 c, out Vector3 barycentric, float epsilon)
    {
        barycentric = default;
        var v0 = b - a;
        var v1 = c - a;
        var v2 = p - a;
        var den = Cross2D(v0, v1);
        if (Mathf.Abs(den) < 0.0000001f)
        {
            return false;
        }

        var v = Cross2D(v2, v1) / den;
        var w = Cross2D(v0, v2) / den;
        var u = 1f - v - w;
        if (u < -epsilon || v < -epsilon || w < -epsilon || u > 1f + epsilon || v > 1f + epsilon || w > 1f + epsilon)
        {
            return false;
        }

        barycentric = new Vector3(u, v, w);
        return true;
    }

    private static bool TryBarycentric3D(Vector3 p, Vector3 a, Vector3 b, Vector3 c, out Vector3 barycentric)
    {
        barycentric = default;
        var v0 = b - a;
        var v1 = c - a;
        var v2 = p - a;
        var d00 = Vector3.Dot(v0, v0);
        var d01 = Vector3.Dot(v0, v1);
        var d11 = Vector3.Dot(v1, v1);
        var d20 = Vector3.Dot(v2, v0);
        var d21 = Vector3.Dot(v2, v1);
        var denom = (d00 * d11) - (d01 * d01);
        if (Mathf.Abs(denom) < 0.0000001f)
        {
            return false;
        }

        var v = ((d11 * d20) - (d01 * d21)) / denom;
        var w = ((d00 * d21) - (d01 * d20)) / denom;
        var u = 1f - v - w;
        barycentric = new Vector3(u, v, w);
        return true;
    }

    private static Vector3 ClosestPointOnTriangle(Vector3 point, Vector3 a, Vector3 b, Vector3 c)
    {
        var ab = b - a;
        var ac = c - a;
        var ap = point - a;
        var d1 = Vector3.Dot(ab, ap);
        var d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0f && d2 <= 0f)
        {
            return a;
        }

        var bp = point - b;
        var d3 = Vector3.Dot(ab, bp);
        var d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0f && d4 <= d3)
        {
            return b;
        }

        var vc = (d1 * d4) - (d3 * d2);
        if (vc <= 0f && d1 >= 0f && d3 <= 0f)
        {
            var v = d1 / (d1 - d3);
            return a + (ab * v);
        }

        var cp = point - c;
        var d5 = Vector3.Dot(ab, cp);
        var d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0f && d5 <= d6)
        {
            return c;
        }

        var vb = (d5 * d2) - (d1 * d6);
        if (vb <= 0f && d2 >= 0f && d6 <= 0f)
        {
            var w = d2 / (d2 - d6);
            return a + (ac * w);
        }

        var va = (d3 * d6) - (d5 * d4);
        if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
        {
            var w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
            return b + ((c - b) * w);
        }

        var denom = 1f / (va + vb + vc);
        var vFace = vb * denom;
        var wFace = vc * denom;
        return a + (ab * vFace) + (ac * wFace);
    }

    private void SaveUndo(string label)
    {
        var texture = DrawableSuitsPlugin.Registry.GetEditableTexture(_selectedSuitId);
        if (texture == null)
        {
            return;
        }

        var normalizedLabel = string.IsNullOrWhiteSpace(label) ? "Edit" : label.Trim();
        ClearUndoHistorySelection("new undo entry", false);
        var entryId = _nextUndoHistoryId++;
        _undo.Push(new UndoHistoryEntry
        {
            Id = entryId,
            Pixels = texture.GetPixels32(),
            Label = normalizedLabel
        });
        while (_undo.Count > DrawableSuitsPlugin.ModConfig.MaxUndoStates.Value)
        {
            var removed = TrimOldest(_undo);
            DrawableSuitsDiagnostics.Info($"UndoHistoryTrimmed: id={removed?.Id ?? 0}; label={removed?.Label ?? "unknown"}; undoCount={_undo.Count}; redoCount={_redo.Count}; max={DrawableSuitsPlugin.ModConfig.MaxUndoStates.Value}");
        }
        UpdateUndoHistoryUi();
        DrawableSuitsDiagnostics.Info($"UndoHistoryPushed: id={entryId}; label={normalizedLabel}; undoCount={_undo.Count}; redoCount={_redo.Count}; max={DrawableSuitsPlugin.ModConfig.MaxUndoStates.Value}");
    }

    private void Undo()
    {
        var texture = DrawableSuitsPlugin.Registry.GetEditableTexture(_selectedSuitId);
        if (texture == null || _undo.Count == 0)
        {
            return;
        }

        var entry = _undo.Pop();
        _redo.Push(new UndoHistoryEntry
        {
            Id = entry.Id,
            Pixels = texture.GetPixels32(),
            Label = entry.Label
        });
        texture.SetPixels32(entry.Pixels);
        texture.Apply(false, false);
        DrawableSuitsPlugin.Registry.ApplyEditedTexture(_selectedSuitId, _designName, false);
        InvalidateDecalPreview("undo");
        RefreshTexturePanelPreview("Undo", false);
        ClearUndoHistorySelection("undo one step", false);
        UpdateUndoHistoryUi();
        DrawableSuitsDiagnostics.Info($"UndoHistoryUndo: id={entry.Id}; label={entry.Label}; undoCount={_undo.Count}; redoCount={_redo.Count}; max={DrawableSuitsPlugin.ModConfig.MaxUndoStates.Value}");
    }

    private void SelectUndoHistoryEntry(long entryId)
    {
        if (IsEditorModalOpen())
        {
            DrawableSuitsDiagnostics.Warn($"UndoHistoryRowSelected ignored because a modal is open. id={entryId}; undoCount={_undo.Count}; redoCount={_redo.Count}");
            return;
        }

        var entries = _undo.ToArray();
        var index = FindUndoHistoryIndexById(entries, entryId);
        if (index < 0)
        {
            DrawableSuitsDiagnostics.Warn($"UndoHistoryRowSelected ignored because id was not found. id={entryId}; undoCount={_undo.Count}; redoCount={_redo.Count}");
            ClearUndoHistorySelection("selected row missing", true);
            return;
        }

        _selectedUndoHistoryId = entryId;
        var label = entries[index].Label;
        SetStatus($"Will undo only {label}.", false);
        UpdateUndoHistoryUi();
        DrawableSuitsDiagnostics.Info($"UndoHistoryRowSelected: id={entryId}; index={index}; label={label}; undoCount={_undo.Count}; redoCount={_redo.Count}; max={DrawableSuitsPlugin.ModConfig.MaxUndoStates.Value}");
    }

    private void UndoToSelectedHistory()
    {
        if (IsEditorModalOpen())
        {
            LogSelectiveUndoSkipped("modal open", _selectedUndoHistoryId, -1, null, 0, 0, 0, 0, 0);
            return;
        }

        if (_selectedUndoHistoryId == 0)
        {
            SetStatus("Select a history row first.", false);
            LogSelectiveUndoSkipped("no row selected", 0, -1, null, 0, 0, 0, 0, 0);
            return;
        }

        var entries = _undo.ToArray();
        var index = FindUndoHistoryIndexById(entries, _selectedUndoHistoryId);
        if (index < 0)
        {
            SetStatus("Selected history row is no longer available.", false);
            LogSelectiveUndoSkipped("selected row stale", _selectedUndoHistoryId, -1, null, 0, 0, 0, 0, 0);
            ClearUndoHistorySelection("selected row stale", true);
            return;
        }

        UndoSelectedHistoryIndex(index, _selectedUndoHistoryId);
    }

    private void UndoSelectedHistoryIndex(int newestFirstIndex, long expectedId)
    {
        var texture = DrawableSuitsPlugin.Registry.GetEditableTexture(_selectedSuitId);
        if (texture == null || newestFirstIndex < 0 || newestFirstIndex >= _undo.Count)
        {
            LogSelectiveUndoSkipped("invalid selection or missing texture", expectedId, newestFirstIndex, null, 0, 0, 0, 0, 0);
            return;
        }

        var entries = _undo.ToArray();
        if (expectedId > 0 && entries[newestFirstIndex].Id != expectedId)
        {
            var resolvedIndex = FindUndoHistoryIndexById(entries, expectedId);
            if (resolvedIndex < 0)
            {
                LogSelectiveUndoSkipped("expected id not found", expectedId, newestFirstIndex, null, 0, 0, 0, 0, 0);
                ClearUndoHistorySelection("expected row missing", true);
                return;
            }

            newestFirstIndex = resolvedIndex;
        }

        var targetEntry = entries[newestFirstIndex];
        var targetLabel = targetEntry.Label;
        Color32[] currentPixels;
        try
        {
            currentPixels = texture.GetPixels32();
        }
        catch (Exception ex)
        {
            DrawableSuitsDiagnostics.Exception($"UndoHistorySelectiveUndo GetPixels32 failed. selectedId={targetEntry.Id}; index={newestFirstIndex}; label={targetLabel}; texture={DrawableSuitsPlugin.DescribeUnityObject(texture)}", ex);
            LogSelectiveUndoSkipped("texture read failed", targetEntry.Id, newestFirstIndex, targetLabel, 0, 0, 0, 0, 0);
            return;
        }

        var beforePixels = targetEntry.Pixels;
        var afterPixels = newestFirstIndex == 0 ? currentPixels : entries[newestFirstIndex - 1].Pixels;
        if (beforePixels == null
            || afterPixels == null
            || beforePixels.Length != currentPixels.Length
            || afterPixels.Length != currentPixels.Length)
        {
            LogSelectiveUndoSkipped(
                $"snapshot dimensions mismatch before={beforePixels?.Length ?? 0} after={afterPixels?.Length ?? 0} current={currentPixels.Length}",
                targetEntry.Id,
                newestFirstIndex,
                targetLabel,
                0,
                0,
                0,
                0,
                0);
            SetStatus("Selected history row cannot be selectively undone because its snapshots do not match.", false);
            return;
        }

        var updatedPixels = (Color32[])currentPixels.Clone();
        var redoPixels = (Color32[])currentPixels.Clone();
        var changedPixels = 0;
        var restoredPixels = 0;
        var preservedOverlapPixels = 0;
        var affectedIndices = new List<int>();
        for (var i = 0; i < currentPixels.Length; i++)
        {
            if (Color32Equals(beforePixels[i], afterPixels[i]))
            {
                continue;
            }

            changedPixels++;
            affectedIndices.Add(i);
            if (Color32Equals(currentPixels[i], afterPixels[i]))
            {
                updatedPixels[i] = beforePixels[i];
                restoredPixels++;
            }
            else
            {
                preservedOverlapPixels++;
            }
        }

        if (changedPixels == 0)
        {
            LogSelectiveUndoSkipped("selected action has no detectable pixel diff", targetEntry.Id, newestFirstIndex, targetLabel, changedPixels, restoredPixels, preservedOverlapPixels, 0, 0);
            SetStatus("Selected history row has no detectable pixel changes.", false);
            return;
        }

        var rewrittenEntries = 0;
        var rewrittenPixels = 0;
        for (var entryIndex = 0; entryIndex < newestFirstIndex; entryIndex++)
        {
            var entry = entries[entryIndex];
            if (entry?.Pixels == null || entry.Pixels.Length != currentPixels.Length)
            {
                continue;
            }

            var changedInEntry = 0;
            for (var affectedIndex = 0; affectedIndex < affectedIndices.Count; affectedIndex++)
            {
                var pixelIndex = affectedIndices[affectedIndex];
                if (Color32Equals(entry.Pixels[pixelIndex], afterPixels[pixelIndex]))
                {
                    entry.Pixels[pixelIndex] = beforePixels[pixelIndex];
                    changedInEntry++;
                }
            }

            if (changedInEntry > 0)
            {
                rewrittenEntries++;
                rewrittenPixels += changedInEntry;
            }
        }

        var retainedEntries = new List<UndoHistoryEntry>(Mathf.Max(0, entries.Length - 1));
        for (var i = 0; i < entries.Length; i++)
        {
            if (i != newestFirstIndex)
            {
                retainedEntries.Add(entries[i]);
            }
        }

        RebuildUndoStackFromNewestFirst(retainedEntries);
        _redo.Push(new UndoHistoryEntry
        {
            Id = targetEntry.Id,
            Pixels = redoPixels,
            Label = targetLabel
        });

        if (restoredPixels > 0)
        {
            texture.SetPixels32(updatedPixels);
            texture.Apply(false, false);
        }

        DrawableSuitsPlugin.Registry.ApplyEditedTexture(_selectedSuitId, _designName, false);
        InvalidateDecalPreview("selective undo");
        RefreshTexturePanelPreview("UndoSelectedHistory", false);
        ClearUndoHistorySelection("undo selected", false);
        UpdateUndoHistoryUi();
        SetStatus(restoredPixels > 0
            ? $"Undid only {targetLabel}. Preserved {preservedOverlapPixels} newer-overlap pixels."
            : $"Removed {targetLabel} from history; newer edits already covered its pixels.", false);
        DrawableSuitsDiagnostics.Info($"UndoHistorySelectiveUndo: selectedId={targetEntry.Id}; index={newestFirstIndex}; label={targetLabel}; changedPixels={changedPixels}; restoredPixels={restoredPixels}; preservedOverlapPixels={preservedOverlapPixels}; rewrittenEntries={rewrittenEntries}; rewrittenPixels={rewrittenPixels}; undoCount={_undo.Count}; redoCount={_redo.Count}; max={DrawableSuitsPlugin.ModConfig.MaxUndoStates.Value}");
        if (rewrittenEntries > 0)
        {
            DrawableSuitsDiagnostics.Info($"UndoHistorySnapshotsRewritten: selectedId={targetEntry.Id}; label={targetLabel}; updatedSnapshotCount={rewrittenEntries}; updatedPixelCount={rewrittenPixels}; undoCount={_undo.Count}; redoCount={_redo.Count}");
        }
    }

    private void Redo()
    {
        var texture = DrawableSuitsPlugin.Registry.GetEditableTexture(_selectedSuitId);
        if (texture == null || _redo.Count == 0)
        {
            return;
        }

        var entry = _redo.Pop();
        _undo.Push(new UndoHistoryEntry
        {
            Id = entry.Id,
            Pixels = texture.GetPixels32(),
            Label = entry.Label
        });
        texture.SetPixels32(entry.Pixels);
        texture.Apply(false, false);
        DrawableSuitsPlugin.Registry.ApplyEditedTexture(_selectedSuitId, _designName, false);
        InvalidateDecalPreview("redo");
        RefreshTexturePanelPreview("Redo", false);
        ClearUndoHistorySelection("redo one step", false);
        UpdateUndoHistoryUi();
        DrawableSuitsDiagnostics.Info($"UndoHistoryRedo: id={entry.Id}; label={entry.Label}; undoCount={_undo.Count}; redoCount={_redo.Count}; max={DrawableSuitsPlugin.ModConfig.MaxUndoStates.Value}");
    }

    private static UndoHistoryEntry TrimOldest(Stack<UndoHistoryEntry> stack)
    {
        var items = stack.ToArray();
        stack.Clear();
        for (var i = items.Length - 2; i >= 0; i--)
        {
            stack.Push(items[i]);
        }

        return items.Length > 0 ? items[items.Length - 1] : null;
    }

    private void ClearUndoHistory(string reason)
    {
        if (_undo.Count == 0 && _redo.Count == 0 && _selectedUndoHistoryId == 0)
        {
            return;
        }

        _undo.Clear();
        _redo.Clear();
        ClearUndoHistorySelection(reason, false);
        UpdateUndoHistoryUi();
        DrawableSuitsDiagnostics.Info($"UndoHistoryCleared: reason={reason}; undoCount=0; redoCount=0; max={DrawableSuitsPlugin.ModConfig.MaxUndoStates.Value}");
    }

    private void ClearUndoHistoryByUser()
    {
        if (_undo.Count == 0 && _redo.Count == 0)
        {
            SetStatus("No undo history to clear.", false);
            return;
        }

        var previousUndoCount = _undo.Count;
        var previousRedoCount = _redo.Count;
        ClearUndoHistory("user clear history");
        SetStatus("Undo history cleared.", false);
        DrawableSuitsDiagnostics.Info($"UndoHistoryClearedByUser: previousUndoCount={previousUndoCount}; previousRedoCount={previousRedoCount}; undoCount={_undo.Count}; redoCount={_redo.Count}; max={DrawableSuitsPlugin.ModConfig.MaxUndoStates.Value}");
    }

    private void ClearRedoHistory(string reason)
    {
        if (_redo.Count == 0)
        {
            return;
        }

        _redo.Clear();
        UpdateUndoHistoryUi();
        DrawableSuitsDiagnostics.Info($"UndoHistoryRedoCleared: reason={reason}; undoCount={_undo.Count}; redoCount=0; max={DrawableSuitsPlugin.ModConfig.MaxUndoStates.Value}");
    }

    private void DropLastUndoEntry(string reason)
    {
        if (_undo.Count == 0)
        {
            return;
        }

        var dropped = _undo.Pop();
        if (dropped.Id == _selectedUndoHistoryId)
        {
            ClearUndoHistorySelection(reason, false);
        }
        UpdateUndoHistoryUi();
        DrawableSuitsDiagnostics.Info($"UndoHistoryDropped: reason={reason}; id={dropped.Id}; label={dropped.Label}; undoCount={_undo.Count}; redoCount={_redo.Count}; max={DrawableSuitsPlugin.ModConfig.MaxUndoStates.Value}");
    }

    private int FindUndoHistoryIndexById(UndoHistoryEntry[] entries, long entryId)
    {
        if (entries == null || entryId == 0)
        {
            return -1;
        }

        for (var i = 0; i < entries.Length; i++)
        {
            if (entries[i] != null && entries[i].Id == entryId)
            {
                return i;
            }
        }

        return -1;
    }

    private static bool Color32Equals(Color32 left, Color32 right)
    {
        return left.r == right.r
            && left.g == right.g
            && left.b == right.b
            && left.a == right.a;
    }

    private void RebuildUndoStackFromNewestFirst(List<UndoHistoryEntry> entries)
    {
        _undo.Clear();
        if (entries == null)
        {
            return;
        }

        for (var i = entries.Count - 1; i >= 0; i--)
        {
            if (entries[i] != null)
            {
                _undo.Push(entries[i]);
            }
        }
    }

    private void LogSelectiveUndoSkipped(string reason, long selectedId, int index, string label, int changedPixels, int restoredPixels, int preservedOverlapPixels, int rewrittenEntries, int rewrittenPixels)
    {
        DrawableSuitsDiagnostics.Warn($"UndoHistorySelectiveUndoSkipped: reason={reason}; selectedId={selectedId}; index={index}; label={label ?? "unknown"}; changedPixels={changedPixels}; restoredPixels={restoredPixels}; preservedOverlapPixels={preservedOverlapPixels}; rewrittenEntries={rewrittenEntries}; rewrittenPixels={rewrittenPixels}; undoCount={_undo.Count}; redoCount={_redo.Count}; max={DrawableSuitsPlugin.ModConfig.MaxUndoStates.Value}");
    }

    private void ClearUndoHistorySelection(string reason, bool updateUi)
    {
        if (_selectedUndoHistoryId == 0)
        {
            return;
        }

        var oldId = _selectedUndoHistoryId;
        _selectedUndoHistoryId = 0;
        if (updateUi)
        {
            UpdateUndoHistoryUi();
        }

        DrawableSuitsDiagnostics.Info($"UndoHistorySelectionCleared: reason={reason}; previousId={oldId}; undoCount={_undo.Count}; redoCount={_redo.Count}; max={DrawableSuitsPlugin.ModConfig.MaxUndoStates.Value}");
    }

    private void UpdateUndoHistoryUi()
    {
        var entries = _undo.ToArray();
        var selectedIndex = FindUndoHistoryIndexById(entries, _selectedUndoHistoryId);
        if (_selectedUndoHistoryId != 0 && selectedIndex < 0)
        {
            ClearUndoHistorySelection("selection no longer in undo stack", false);
        }
        var hasSelection = _selectedUndoHistoryId != 0 && selectedIndex >= 0;
        if (_undoHistoryEmptyLabel != null)
        {
            _undoHistoryEmptyLabel.gameObject.SetActive(entries.Length == 0);
        }
        SetInteractable(_undoToSelectedButton, hasSelection);
        SetInteractable(_clearUndoHistoryButton, _undo.Count > 0 || _redo.Count > 0);
        if (_undoHistorySelectionLabel != null)
        {
            if (hasSelection)
            {
                _undoHistorySelectionLabel.text = $"Will undo only {entries[selectedIndex].Label}.";
                _undoHistorySelectionLabel.color = new Color(1f, 0.68f, 0.36f, 1f);
            }
            else
            {
                _undoHistorySelectionLabel.text = entries.Length > 0 ? "Select a row first." : "No history.";
                _undoHistorySelectionLabel.color = new Color(0.82f, 0.86f, 0.9f, 1f);
            }
        }

        for (var i = 0; i < _undoHistoryRows.Count; i++)
        {
            var row = _undoHistoryRows[i];
            if (row == null || row.GameObject == null)
            {
                continue;
            }

            var hasEntry = i < entries.Length;
            row.GameObject.SetActive(hasEntry);
            row.Index = hasEntry ? i : -1;
            row.EntryId = hasEntry ? entries[i].Id : 0;
            if (row.Button != null)
            {
                row.Button.onClick.RemoveAllListeners();
                row.Button.interactable = hasEntry;
            }
            if (!hasEntry)
            {
                continue;
            }

            var rowEntryId = row.EntryId;
            var rowLabel = entries[i].Label;
            if (row.Button != null)
            {
                row.Button.onClick.AddListener(() =>
                {
                    SelectUndoHistoryEntry(rowEntryId);
                    ClearSelectedNormalButton();
                });
            }
            if (row.Label != null)
            {
                row.Label.text = rowLabel;
            }
            if (row.Button != null)
            {
                if (row.EntryId == _selectedUndoHistoryId)
                {
                    ApplySelectedListButtonStyle(row.Button);
                }
                else
                {
                    ApplyNormalListButtonStyle(row.Button);
                }
            }
            else if (row.Image != null)
            {
                row.Image.color = row.EntryId == _selectedUndoHistoryId
                    ? TerminalAccentColor
                    : TerminalInputColor;
            }
        }
    }

    private void SaveDesign()
    {
        DrawableSuitsDiagnostics.Info($"SaveDesign requested. selectedSuitId={_selectedSuitId}; designName={_designName}");
        if (DrawableSuitsPlugin.Registry.SaveDesign(_selectedSuitId, _designName))
        {
            var previouslySelectedDesign = GetSelectedFilePath(_designFiles, _selectedDesignIndex);
            RefreshFileLists();
            _selectedDesignIndex = FindFileIndex(_designFiles, previouslySelectedDesign);
            SetStatus("Design saved.", false);
            if (IsSavedDesignsPanelOpen())
            {
                SetSavedDesignsStatus($"Saved {_designName}. Select a design to load it.");
            }
            UpdateUiState();
            DrawableSuitsDiagnostics.Info($"SaveDesign succeeded. preservedSelectedDesign={previouslySelectedDesign ?? "none"}; selectedDesignIndex={_selectedDesignIndex}");
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
            SetSavedDesignsStatus("Select a saved design first.");
            return;
        }

        DrawableSuitsDiagnostics.Info($"LoadSelectedDesign requested. file={_designFiles[_selectedDesignIndex]}; selectedSuitId={_selectedSuitId}");
        SaveUndo("Load design");
        if (DrawableSuitsPlugin.Registry.LoadDesign(_selectedSuitId, _designFiles[_selectedDesignIndex]))
        {
            ClearRedoHistory("load design");
            _designName = Path.GetFileNameWithoutExtension(_designFiles[_selectedDesignIndex]);
            if (_designNameInput != null)
            {
                _designNameInput.text = _designName;
            }
            RefreshEditorReadiness("before load design preview");
            InvalidateDecalPreview("load design");
            TryRebuildPreviewForCurrentReadiness("LoadSelectedDesign");
            RefreshEditorReadiness("after load design");
            UpdateUiState();
            SetSavedDesignsStatus($"Loaded {_designName} into the current suit.");
            DrawableSuitsDiagnostics.Info($"SavedDesignsPanelLoaded: designName={_designName}; selectedDesignIndex={_selectedDesignIndex}; file={_designFiles[_selectedDesignIndex]}; undoCount={_undo.Count}; redoCount={_redo.Count}");
            DrawableSuitsDiagnostics.Info("LoadSelectedDesign succeeded.");
        }
        else
        {
            DropLastUndoEntry("Load design failed");
            SetSavedDesignsStatus("Load failed. Check diagnostics.");
            DrawableSuitsDiagnostics.Warn("LoadSelectedDesign failed; registry returned false.");
        }
    }

    private void RefreshFileLists()
    {
        DrawableSuitsPaths.EnsureCreated();
        var selectedDesignPath = GetSelectedFilePath(_designFiles, _selectedDesignIndex);
        var selectedDecalPath = GetSelectedFilePath(_decalFiles, _selectedDecalIndex);
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
        _selectedDesignIndex = FindFileIndex(_designFiles, selectedDesignPath);
        _selectedDecalIndex = FindFileIndex(_decalFiles, selectedDecalPath);
        RefreshListButtons();
        DrawableSuitsDiagnostics.Info($"RefreshFileLists complete. designCount={_designFiles.Count}; decalCount={_decalFiles.Count}; selectedDesign={_selectedDesignIndex}; selectedDecal={_selectedDecalIndex}; savesPath={DrawableSuitsPaths.Saves}; decalsPath={DrawableSuitsPaths.Decals}");
    }

    private static string GetSelectedFilePath(List<string> files, int index)
    {
        return index >= 0 && index < files.Count ? files[index] : null;
    }

    private static int FindFileIndex(List<string> files, string selectedPath)
    {
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return -1;
        }

        for (var i = 0; i < files.Count; i++)
        {
            if (string.Equals(files[i], selectedPath, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private void OpenDesignCodePanel(bool exportCurrent)
    {
        if (_designCodePanelObject == null)
        {
            return;
        }

        CancelPendingFileDeletes("open design code panel");
        _designCodePanelObject.SetActive(true);
        if (_designCodeStatusLabel != null)
        {
            _designCodeStatusLabel.text = exportCurrent
                ? "Generating code for the current editable texture..."
                : "Paste a DSUIT2 or DSUIT1 code, then press Import.";
        }

        if (!exportCurrent && _designCodeInput != null)
        {
            var clipboard = GUIUtility.systemCopyBuffer;
            _designCodeInput.text = DrawableSuitDesignCode.HasSupportedPrefix(clipboard)
                ? clipboard
                : string.Empty;
        }

        if (exportCurrent)
        {
            CopyCurrentDesignCode();
        }

        RebuildSelectableNavigation();
        DrawableSuitsDiagnostics.Info($"DesignCodePanel opened. exportCurrent={exportCurrent}; selectedSuitId={_selectedSuitId}; designName={_designName}");
    }

    private void CloseDesignCodePanel()
    {
        if (_designCodePanelObject != null)
        {
            _designCodePanelObject.SetActive(false);
        }
    }

    private void OpenSavedDesignsPanel()
    {
        if (_savedDesignsPanelObject == null)
        {
            return;
        }

        CancelPendingFileDeletes("open saved designs panel");
        RefreshFileLists();
        _savedDesignsPanelObject.SetActive(true);
        SetSavedDesignsStatus(_designFiles.Count == 0
            ? "No saved designs found. Press Save to create one."
            : "Select a saved design, then press Load Selected.");
        UpdateUiState();
        RebuildSelectableNavigation();
        DrawableSuitsDiagnostics.Info($"SavedDesignsPanelOpened: designCount={_designFiles.Count}; selectedDesignIndex={_selectedDesignIndex}; selectedDesign={GetSelectedFilePath(_designFiles, _selectedDesignIndex) ?? "none"}");
    }

    private void CloseSavedDesignsPanel()
    {
        var wasOpen = IsSavedDesignsPanelOpen();
        CancelPendingDesignDelete("close saved designs panel");
        if (_savedDesignsPanelObject != null)
        {
            _savedDesignsPanelObject.SetActive(false);
        }

        SetSavedDesignsStatus(string.Empty);
        RebuildSelectableNavigation();
        if (wasOpen)
        {
            DrawableSuitsDiagnostics.Info($"SavedDesignsPanelClosed: selectedDesignIndex={_selectedDesignIndex}; selectedDesign={GetSelectedFilePath(_designFiles, _selectedDesignIndex) ?? "none"}");
        }
    }

    private void RefreshSavedDesignsPanel()
    {
        CancelPendingDesignDelete("refresh saved designs panel");
        RefreshFileLists();
        SetSavedDesignsStatus(_designFiles.Count == 0
            ? "No saved designs found."
            : $"Loaded {_designFiles.Count} saved design{(_designFiles.Count == 1 ? string.Empty : "s")}.");
        UpdateUiState();
        DrawableSuitsDiagnostics.Info($"SavedDesignsPanelLoaded: designCount={_designFiles.Count}; selectedDesignIndex={_selectedDesignIndex}; selectedDesign={GetSelectedFilePath(_designFiles, _selectedDesignIndex) ?? "none"}");
    }

    private void OpenDecalsPanel()
    {
        if (_decalsPanelObject == null)
        {
            return;
        }

        CancelPendingFileDeletes("open decals panel");
        RefreshFileLists();
        _decalsPanelObject.SetActive(true);
        SetDecalsStatus(_decalFiles.Count == 0
            ? "No decals found. Add a PNG/JPG file or place one in the Decals folder."
            : "Select a decal to load it, or add a PNG/JPG file.");
        UpdateUiState();
        RebuildSelectableNavigation();
        DrawableSuitsDiagnostics.Info($"DecalsPanelOpened: decalCount={_decalFiles.Count}; selectedDecalIndex={_selectedDecalIndex}; selectedDecal={GetSelectedFilePath(_decalFiles, _selectedDecalIndex) ?? "none"}");
    }

    private void CloseDecalsPanel()
    {
        var wasOpen = IsDecalsPanelOpen();
        CancelPendingDecalImport("close decals panel");
        CancelPendingDecalDelete("close decals panel");
        if (_decalsPanelObject != null)
        {
            _decalsPanelObject.SetActive(false);
        }

        SetDecalsStatus(string.Empty);
        RebuildSelectableNavigation();
        if (wasOpen)
        {
            DrawableSuitsDiagnostics.Info($"DecalsPanelClosed: selectedDecalIndex={_selectedDecalIndex}; selectedDecal={GetSelectedFilePath(_decalFiles, _selectedDecalIndex) ?? "none"}");
        }
    }

    private void RefreshDecalsPanel()
    {
        CancelPendingDecalDelete("refresh decals panel");
        RefreshFileLists();
        SetDecalsStatus(_decalFiles.Count == 0
            ? "No decals found."
            : $"Loaded {_decalFiles.Count} decal{(_decalFiles.Count == 1 ? string.Empty : "s")}.");
        UpdateUiState();
        DrawableSuitsDiagnostics.Info($"DecalsPanelRefreshed: decalCount={_decalFiles.Count}; selectedDecalIndex={_selectedDecalIndex}; selectedDecal={GetSelectedFilePath(_decalFiles, _selectedDecalIndex) ?? "none"}");
    }

    private void SetDecalsStatus(string message)
    {
        if (_decalsStatusLabel != null)
        {
            _decalsStatusLabel.text = message ?? string.Empty;
        }
    }

    private void OpenStickersPanel()
    {
        if (_stickersPanelObject == null)
        {
            return;
        }

        CancelPendingFileDeletes("open stickers panel");
        _stickersPanelObject.SetActive(true);
        UpdateStickerShapeButton();
        SetStickersStatus($"Selected: {StickerShapeDisplayName(_stickerShape)}.");
        RebuildSelectableNavigation();
        DrawableSuitsDiagnostics.Info($"StickersPanelOpened: shape={_stickerShape}; display={StickerShapeDisplayName(_stickerShape)}; tool={_tool}; cursor={_cursor}");
    }

    private void CloseStickersPanel()
    {
        var wasOpen = IsStickersPanelOpen();
        if (_stickersPanelObject != null)
        {
            _stickersPanelObject.SetActive(false);
        }

        SetStickersStatus(string.Empty);
        RebuildSelectableNavigation();
        if (wasOpen)
        {
            DrawableSuitsDiagnostics.Info($"StickersPanelClosed: shape={_stickerShape}; display={StickerShapeDisplayName(_stickerShape)}");
        }
    }

    private void SetStickersStatus(string message)
    {
        if (_stickersStatusLabel != null)
        {
            _stickersStatusLabel.text = message ?? string.Empty;
        }
    }

    private void OpenPlacementEditPanel(PlacementEditTarget target)
    {
        if (_placementEditPanelObject == null)
        {
            return;
        }

        if (target == PlacementEditTarget.Decal && _loadedDecal == null)
        {
            SetStatus("Select a decal before editing it.", false);
            return;
        }

        if (target == PlacementEditTarget.Sticker && !TryGetStickerStampTexture(_stickerShape, out _, out var failureReason))
        {
            SetStatus($"Sticker failed: {failureReason}.", false);
            return;
        }

        CancelPendingFileDeletes("open placement edit panel");
        CloseDecalsPanel();
        CloseStickersPanel();
        _activePlacementEditTarget = target;
        _placementEditPanelObject.SetActive(true);
        RefreshPlacementEditPanelUi(true);
        RebuildSelectableNavigation();
        var state = GetPlacementEditState(target);
        DrawableSuitsDiagnostics.Info($"PlacementEditPanelOpened: target={target}; source={GetPlacementEditSourceName(target)}; crop={DescribePlacementCrop(state)}; stretch={DescribePlacementStretch(state)}; flip={DescribePlacementFlip(state)}; filters={DescribePlacementFilters(state)}; suit={_selectedSuitId}");
    }

    private void ClosePlacementEditPanel()
    {
        var wasOpen = IsPlacementEditPanelOpen();
        var target = _activePlacementEditTarget;
        if (_placementEditPanelObject != null)
        {
            _placementEditPanelObject.SetActive(false);
        }

        _activePlacementEditTarget = PlacementEditTarget.None;
        if (_placementEditPreviewImage != null)
        {
            _placementEditPreviewImage.texture = null;
        }

        RebuildSelectableNavigation();
        if (wasOpen)
        {
            ClearPlacementSourcePixelCache(target, "placement edit panel closed");
            DrawableSuitsDiagnostics.Info($"PlacementEditPanelClosed: target={target}; source={GetPlacementEditSourceName(target)}; suit={_selectedSuitId}");
        }
    }

    private void RefreshPlacementEditPanelUi(bool rebuildPreview = false)
    {
        if (!IsPlacementEditPanelOpen())
        {
            return;
        }

        var target = _activePlacementEditTarget;
        var state = GetPlacementEditState(target);
        if (state == null)
        {
            SetPlacementEditStatus("No editable placement selected.");
            return;
        }

        if (_placementEditTitleLabel != null)
        {
            _placementEditTitleLabel.text = target == PlacementEditTarget.Sticker ? "Edit Sticker" : "Edit Decal";
        }
        if (_placementEditSourceLabel != null)
        {
            _placementEditSourceLabel.text = $"{DisplayPlacementEditSourceName(target)}\nTemporary edits affect preview and future stamps only.";
        }

        SetPlacementEditSliderValues(state);
        if (_placementEditFlipXButton != null)
        {
            SetToolButtonColor(_placementEditFlipXButton, state.FlipX);
        }
        if (_placementEditFlipYButton != null)
        {
            SetToolButtonColor(_placementEditFlipYButton, state.FlipY);
        }

        if (_placementEditPreviewImage != null)
        {
            UpdatePlacementEditPreview(rebuildPreview ? "panel refresh" : "ui refresh");
        }
    }

    private void UpdatePlacementEditPreview(string reason)
    {
        var startedAt = Time.realtimeSinceStartup;
        var target = _activePlacementEditTarget;
        if (!IsPlacementEditPanelOpen() || target == PlacementEditTarget.None)
        {
            LogPlacementEditPreviewSkipped(target, reason, "panel closed or target missing");
            return;
        }

        if (_placementEditPreviewImage == null)
        {
            LogPlacementEditPreviewSkipped(target, reason, "preview image missing");
            return;
        }

        var state = GetPlacementEditState(target);
        if (state == null)
        {
            _placementEditPreviewImage.texture = null;
            SetPlacementEditStatus("No editable placement selected.");
            LogPlacementEditPreviewSkipped(target, reason, "state missing");
            return;
        }

        var sourceFailure = string.Empty;
        var previewFailure = string.Empty;
        if (target == PlacementEditTarget.Decal)
        {
            if (TryGetPlacementEditSourceTexture(target, out var decalSource, out sourceFailure)
                && TryUpdateFastDecalEditPreview(decalSource, state, reason, startedAt, out previewFailure))
            {
                return;
            }

            var decalFailure = string.IsNullOrWhiteSpace(sourceFailure) ? previewFailure : sourceFailure;
            if (_placementEditPreviewImage.texture == null)
            {
                _placementEditPreviewImage.texture = null;
            }
            SetPlacementEditStatus(decalFailure);
            LogDecalEditPreviewFastSkipped(decalSource, state, reason, decalFailure);
            LogPlacementEditPreviewSkipped(target, reason, decalFailure);
            return;
        }

        if (TryGetPlacementEditSourceTexture(target, out var source, out sourceFailure)
            && TryGetEditedPlacementPreviewStamp(target, source, target == PlacementEditTarget.Sticker, out var preview, out previewFailure, out var cacheHit, out var cpuPixelSamplingUsed))
        {
            _placementEditPreviewImage.texture = preview;
            _placementEditPreviewImage.color = Color.white;
            FitPlacementEditPreview(preview);
            SetPlacementEditStatus(state.IsDefault ? "No temporary edits applied." : "Temporary edit preview ready.");
            LogPlacementEditPreviewUpdated(target, source, state, preview, reason, cacheHit, cpuPixelSamplingUsed, (Time.realtimeSinceStartup - startedAt) * 1000f);
            return;
        }

        _placementEditPreviewImage.texture = null;
        var failure = string.IsNullOrWhiteSpace(sourceFailure) ? previewFailure : sourceFailure;
        SetPlacementEditStatus(failure);
        LogPlacementEditPreviewSkipped(target, reason, failure);
    }

    private void SetPlacementEditSliderValues(PlacementEditState state)
    {
        if (state == null)
        {
            return;
        }

        _placementEditCropLeftSlider?.SetValue(state.CropLeft, false);
        _placementEditCropRightSlider?.SetValue(state.CropRight, false);
        _placementEditCropBottomSlider?.SetValue(state.CropBottom, false);
        _placementEditCropTopSlider?.SetValue(state.CropTop, false);
        _placementEditStretchXSlider?.SetValue(state.StretchX, false);
        _placementEditStretchYSlider?.SetValue(state.StretchY, false);
        foreach (var filter in PlacementFilterRows)
        {
            var amount = state.GetFilterAmount(filter);
            if (_placementEditFilterSliders.TryGetValue(filter, out var slider))
            {
                slider.SetValue(amount, false);
            }
            if (_placementEditFilterValueLabels.TryGetValue(filter, out var label))
            {
                label.text = $"{Mathf.RoundToInt(amount * 100f)}%";
                label.color = amount > 0.0001f ? TerminalStatusColor : TerminalMutedTextColor;
            }
        }
        if (_placementEditCropLeftLabel != null) _placementEditCropLeftLabel.text = $"Left {Mathf.RoundToInt(state.CropLeft * 100f)}%";
        if (_placementEditCropRightLabel != null) _placementEditCropRightLabel.text = $"Right {Mathf.RoundToInt(state.CropRight * 100f)}%";
        if (_placementEditCropBottomLabel != null) _placementEditCropBottomLabel.text = $"Bottom {Mathf.RoundToInt(state.CropBottom * 100f)}%";
        if (_placementEditCropTopLabel != null) _placementEditCropTopLabel.text = $"Top {Mathf.RoundToInt(state.CropTop * 100f)}%";
        if (_placementEditStretchXLabel != null) _placementEditStretchXLabel.text = $"Width {state.StretchX:0.##}x";
        if (_placementEditStretchYLabel != null) _placementEditStretchYLabel.text = $"Height {state.StretchY:0.##}x";
    }

    private void OnPlacementEditChanged(string reason)
    {
        var state = GetPlacementEditState(_activePlacementEditTarget);
        if (state == null)
        {
            return;
        }

        state.Revision++;
        InvalidateEditedPlacementStamp(_activePlacementEditTarget, destroyTexture: false);
        InvalidateDecalPreview($"placement edit changed: {reason}");
        RefreshPlacementEditPanelUi();
        DrawableSuitsDiagnostics.Info($"PlacementEditChanged: target={_activePlacementEditTarget}; source={GetPlacementEditSourceName(_activePlacementEditTarget)}; reason={reason}; crop={DescribePlacementCrop(state)}; stretch={DescribePlacementStretch(state)}; flip={DescribePlacementFlip(state)}; filters={DescribePlacementFilters(state)}; revision={state.Revision}; suit={_selectedSuitId}");
    }

    private void ResetActivePlacementEdit()
    {
        ResetPlacementEdit(_activePlacementEditTarget, "user reset edits", true);
        RefreshPlacementEditPanelUi(true);
        SetPlacementEditStatus("Temporary edits reset.");
    }

    private void ResetPlacementEdits(string reason, bool log)
    {
        ResetPlacementEdit(PlacementEditTarget.Decal, reason, log);
        ResetPlacementEdit(PlacementEditTarget.Sticker, reason, log);
    }

    private void ResetPlacementEdit(PlacementEditTarget target, string reason, bool log)
    {
        var state = GetPlacementEditState(target);
        if (state == null)
        {
            return;
        }

        state.Reset();
        InvalidateEditedPlacementStamp(target);
        InvalidateDecalPreview($"placement edit reset: {reason}");
        if (log)
        {
            DrawableSuitsDiagnostics.Info($"PlacementEditReset: target={target}; source={GetPlacementEditSourceName(target)}; reason={reason}; crop={DescribePlacementCrop(state)}; stretch={DescribePlacementStretch(state)}; flip={DescribePlacementFlip(state)}; filters={DescribePlacementFilters(state)}; revision={state.Revision}; suit={_selectedSuitId}");
        }
    }

    private void TogglePlacementEditFlipX()
    {
        var state = GetPlacementEditState(_activePlacementEditTarget);
        if (state == null)
        {
            return;
        }

        state.FlipX = !state.FlipX;
        OnPlacementEditChanged("flip x");
    }

    private void TogglePlacementEditFlipY()
    {
        var state = GetPlacementEditState(_activePlacementEditTarget);
        if (state == null)
        {
            return;
        }

        state.FlipY = !state.FlipY;
        OnPlacementEditChanged("flip y");
    }

    private PlacementEditState GetPlacementEditState(PlacementEditTarget target)
    {
        return target switch
        {
            PlacementEditTarget.Decal => _decalPlacementEdit,
            PlacementEditTarget.Sticker => _stickerPlacementEdit,
            _ => null
        };
    }

    private bool TryGetPlacementEditSourceTexture(PlacementEditTarget target, out Texture2D texture, out string failureReason)
    {
        texture = null;
        failureReason = string.Empty;
        if (target == PlacementEditTarget.Decal)
        {
            if (_loadedDecal == null)
            {
                failureReason = "Select a decal first.";
                return false;
            }

            texture = _loadedDecal;
            return true;
        }

        if (target == PlacementEditTarget.Sticker)
        {
            return TryGetStickerStampTexture(_stickerShape, out texture, out failureReason);
        }

        failureReason = "No placement edit target.";
        return false;
    }

    private void InvalidateEditedPlacementStamp(PlacementEditTarget target, bool destroyTexture = true)
    {
        if (target == PlacementEditTarget.Decal)
        {
            if (destroyTexture && _editedDecalStampTexture != null)
            {
                Destroy(_editedDecalStampTexture);
                _editedDecalStampTexture = null;
            }

            if (destroyTexture && _editedDecalPreviewStampTexture != null)
            {
                Destroy(_editedDecalPreviewStampTexture);
                _editedDecalPreviewStampTexture = null;
            }

            _editedDecalStampKey = string.Empty;
            _editedDecalPreviewStampKey = string.Empty;
            if (destroyTexture)
            {
                _editedDecalPreviewPixelBuffer = null;
                ClearPlacementSourcePixelCache(target, "edited placement stamp invalidated");
            }
            return;
        }

        if (target == PlacementEditTarget.Sticker)
        {
            if (destroyTexture && _editedStickerStampTexture != null)
            {
                Destroy(_editedStickerStampTexture);
                _editedStickerStampTexture = null;
            }

            if (destroyTexture && _editedStickerPreviewStampTexture != null)
            {
                Destroy(_editedStickerPreviewStampTexture);
                _editedStickerPreviewStampTexture = null;
            }

            _editedStickerStampKey = string.Empty;
            _editedStickerPreviewStampKey = string.Empty;
            if (destroyTexture)
            {
                ClearPlacementSourcePixelCache(target, "edited placement stamp invalidated");
            }
        }
    }

    private void DestroyPlacementEditResources()
    {
        InvalidateEditedPlacementStamp(PlacementEditTarget.Decal);
        InvalidateEditedPlacementStamp(PlacementEditTarget.Sticker);
        if (_placementEditCheckerTexture != null)
        {
            Destroy(_placementEditCheckerTexture);
            _placementEditCheckerTexture = null;
        }
    }

    private void SetPlacementEditStatus(string message)
    {
        if (_placementEditStatusLabel != null)
        {
            _placementEditStatusLabel.text = message ?? string.Empty;
        }
    }

    private string GetPlacementEditSourceName(PlacementEditTarget target)
    {
        return target switch
        {
            PlacementEditTarget.Decal => _selectedDecalIndex >= 0 && _selectedDecalIndex < _decalFiles.Count
                ? Path.GetFileName(_decalFiles[_selectedDecalIndex])
                : _loadedDecal != null ? _loadedDecal.name : "No decal selected",
            PlacementEditTarget.Sticker => StickerShapeDisplayName(_stickerShape),
            _ => "None"
        };
    }

    private string DisplayPlacementEditSourceName(PlacementEditTarget target)
    {
        return target == PlacementEditTarget.Decal
            ? MiddleEllipsize(GetPlacementEditSourceName(target), 48)
            : GetPlacementEditSourceName(target);
    }

    private static string PlacementFilterDisplayName(PlacementFilter filter)
    {
        return filter switch
        {
            PlacementFilter.None => "None",
            PlacementFilter.Grayscale => "Grayscale",
            PlacementFilter.Invert => "Invert",
            PlacementFilter.Sepia => "Sepia",
            PlacementFilter.Brightness => "Brightness",
            PlacementFilter.Contrast => "Contrast",
            PlacementFilter.Saturation => "Saturation",
            PlacementFilter.HueShift => "Hue Shift",
            _ => filter.ToString()
        };
    }

    private static string DescribePlacementCrop(PlacementEditState state)
    {
        return state == null
            ? "none"
            : $"{state.CropLeft:0.###},{state.CropRight:0.###},{state.CropBottom:0.###},{state.CropTop:0.###}";
    }

    private static string DescribePlacementCrop(PlacementEditSnapshot state)
    {
        return state == null
            ? "none"
            : $"{state.CropLeft:0.###},{state.CropRight:0.###},{state.CropBottom:0.###},{state.CropTop:0.###}";
    }

    private static string DescribePlacementStretch(PlacementEditState state)
    {
        return state == null ? "none" : $"{state.StretchX:0.###},{state.StretchY:0.###}";
    }

    private static string DescribePlacementStretch(PlacementEditSnapshot state)
    {
        return state == null ? "none" : $"{state.StretchX:0.###},{state.StretchY:0.###}";
    }

    private static string DescribePlacementFlip(PlacementEditState state)
    {
        return state == null ? "none" : $"{state.FlipX},{state.FlipY}";
    }

    private static string DescribePlacementFlip(PlacementEditSnapshot state)
    {
        return state == null ? "none" : $"{state.FlipX},{state.FlipY}";
    }

    private static string DescribePlacementFilters(PlacementEditState state)
    {
        if (state == null)
        {
            return "none";
        }

        return $"gray={state.GrayscaleAmount:0.###},sepia={state.SepiaAmount:0.###},invert={state.InvertAmount:0.###},brightness={state.BrightnessAmount:0.###},contrast={state.ContrastAmount:0.###},saturation={state.SaturationAmount:0.###},hue={state.HueShiftAmount:0.###}";
    }

    private static string DescribePlacementFilters(PlacementEditSnapshot state)
    {
        if (state == null)
        {
            return "none";
        }

        return $"gray={state.GrayscaleAmount:0.###},sepia={state.SepiaAmount:0.###},invert={state.InvertAmount:0.###},brightness={state.BrightnessAmount:0.###},contrast={state.ContrastAmount:0.###},saturation={state.SaturationAmount:0.###},hue={state.HueShiftAmount:0.###}";
    }

    private void FitPlacementEditPreview(Texture2D texture)
    {
        if (_placementEditPreviewRect == null || _placementEditPreviewFrameRect == null || texture == null)
        {
            return;
        }

        var frameSize = _placementEditPreviewFrameRect.rect.size;
        if (frameSize.x <= 1f || frameSize.y <= 1f)
        {
            frameSize = _placementEditPreviewFrameRect.sizeDelta;
        }

        var availableWidth = Mathf.Max(24f, frameSize.x - 16f);
        var availableHeight = Mathf.Max(24f, frameSize.y - 16f);
        var aspect = texture.width / Mathf.Max(1f, (float)texture.height);
        var width = availableWidth;
        var height = width / Mathf.Max(0.0001f, aspect);
        if (height > availableHeight)
        {
            height = availableHeight;
            width = height * aspect;
        }

        _placementEditPreviewRect.anchorMin = new Vector2(0.5f, 0.5f);
        _placementEditPreviewRect.anchorMax = new Vector2(0.5f, 0.5f);
        _placementEditPreviewRect.pivot = new Vector2(0.5f, 0.5f);
        _placementEditPreviewRect.anchoredPosition = Vector2.zero;
        _placementEditPreviewRect.sizeDelta = new Vector2(Mathf.Clamp(width, 8f, availableWidth), Mathf.Clamp(height, 8f, availableHeight));
    }

    private Texture2D GetPlacementEditCheckerTexture()
    {
        if (_placementEditCheckerTexture != null)
        {
            return _placementEditCheckerTexture;
        }

        const int size = 32;
        const int cell = 8;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "DrawableSuitsPlacementEditChecker",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Repeat,
            hideFlags = HideFlags.HideAndDontSave
        };
        var dark = new Color32(18, 8, 8, 255);
        var light = new Color32(42, 18, 18, 255);
        var pixels = new Color32[size * size];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                pixels[(y * size) + x] = ((x / cell) + (y / cell)) % 2 == 0 ? dark : light;
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        _placementEditCheckerTexture = texture;
        return texture;
    }

    private void LogPlacementEditedStampGenerated(PlacementEditTarget target, Texture2D source, PlacementEditState state, Texture2D generated, bool cacheHit)
    {
        var eventName = target == PlacementEditTarget.Decal ? "EditedDecalFullQualityGenerated" : "PlacementEditedStampGenerated";
        DrawableSuitsDiagnostics.Info($"{eventName}: target={target}; source={GetPlacementEditSourceName(target)}; quality=full; sourceSize={source?.width ?? 0}x{source?.height ?? 0}; crop={DescribePlacementCrop(state)}; stretch={DescribePlacementStretch(state)}; flip={DescribePlacementFlip(state)}; filters={DescribePlacementFilters(state)}; cacheHit={cacheHit}; generatedSize={generated?.width ?? 0}x{generated?.height ?? 0}; suit={_selectedSuitId}");
    }

    private void LogPlacementEditPreviewSkipped(PlacementEditTarget target, string reason, string failureReason)
    {
        var key = $"skipped|target={target}|reason={reason}|failure={failureReason}";
        if (Time.unscaledTime - _lastPlacementEditPreviewLogTime < 0.5f && string.Equals(key, _lastPlacementEditPreviewLogKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastPlacementEditPreviewLogTime = Time.unscaledTime;
        _lastPlacementEditPreviewLogKey = key;
        DrawableSuitsDiagnostics.Warn($"PlacementEditPreviewSkipped: target={target}; source={GetPlacementEditSourceName(target)}; reason={reason}; failure={failureReason}; suit={_selectedSuitId}");
    }

    private void LogPlacementEditPreviewUpdated(PlacementEditTarget target, Texture2D source, PlacementEditState state, Texture2D preview, string reason, bool cacheHit, bool cpuPixelSamplingUsed, float elapsedMs)
    {
        var eventName = cacheHit ? "PlacementEditPreviewCacheHit" : "PlacementEditPreviewUpdated";
        var key = $"{eventName}|target={target}|reason={reason}|source={GetPlacementEditSourceName(target)}|preview={preview?.width ?? 0}x{preview?.height ?? 0}|state={state?.Revision ?? -1}";
        if (cacheHit && Time.unscaledTime - _lastPlacementEditPreviewLogTime < 0.5f && string.Equals(key, _lastPlacementEditPreviewLogKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastPlacementEditPreviewLogTime = Time.unscaledTime;
        _lastPlacementEditPreviewLogKey = key;
        DrawableSuitsDiagnostics.Info($"{eventName}: target={target}; source={GetPlacementEditSourceName(target)}; reason={reason}; quality=preview; sourceSize={source?.width ?? 0}x{source?.height ?? 0}; generatedSize={preview?.width ?? 0}x{preview?.height ?? 0}; crop={DescribePlacementCrop(state)}; stretch={DescribePlacementStretch(state)}; flip={DescribePlacementFlip(state)}; filters={DescribePlacementFilters(state)}; cacheHit={cacheHit}; cpuPixels={cpuPixelSamplingUsed}; elapsedMs={elapsedMs:0.##}; suit={_selectedSuitId}");
    }

    private void LogDecalEditPreviewFastUpdated(Texture2D source, PlacementEditState state, Texture2D preview, string reason, bool sourceCacheHit, bool textureReused, float elapsedMs)
    {
        var key = $"DecalEditPreviewFastUpdated|reason={reason}|source={GetPlacementEditSourceName(PlacementEditTarget.Decal)}|preview={preview?.width ?? 0}x{preview?.height ?? 0}|state={state?.Revision ?? -1}";
        if (Time.unscaledTime - _lastPlacementEditPreviewLogTime < 0.25f && string.Equals(key, _lastPlacementEditPreviewLogKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastPlacementEditPreviewLogTime = Time.unscaledTime;
        _lastPlacementEditPreviewLogKey = key;
        DrawableSuitsDiagnostics.Info($"DecalEditPreviewFastUpdated: source={GetPlacementEditSourceName(PlacementEditTarget.Decal)}; reason={reason}; quality=modalPreview; sourceSize={source?.width ?? 0}x{source?.height ?? 0}; previewSize={preview?.width ?? 0}x{preview?.height ?? 0}; crop={DescribePlacementCrop(state)}; stretch={DescribePlacementStretch(state)}; flip={DescribePlacementFlip(state)}; filters={DescribePlacementFilters(state)}; sourceCacheHit={sourceCacheHit}; textureReused={textureReused}; cpuPixels=True; elapsedMs={elapsedMs:0.##}; suit={_selectedSuitId}");
    }

    private void LogDecalEditPreviewFastCacheHit(Texture2D source, PlacementEditState state, Texture2D preview, string reason, bool generatedPreviewCache, bool cpuPixelSamplingUsed, float elapsedMs)
    {
        var key = $"DecalEditPreviewFastCacheHit|reason={reason}|source={GetPlacementEditSourceName(PlacementEditTarget.Decal)}|preview={preview?.width ?? 0}x{preview?.height ?? 0}|state={state?.Revision ?? -1}";
        if (Time.unscaledTime - _lastPlacementEditPreviewLogTime < 0.5f && string.Equals(key, _lastPlacementEditPreviewLogKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastPlacementEditPreviewLogTime = Time.unscaledTime;
        _lastPlacementEditPreviewLogKey = key;
        DrawableSuitsDiagnostics.Info($"DecalEditPreviewFastCacheHit: source={GetPlacementEditSourceName(PlacementEditTarget.Decal)}; reason={reason}; quality=modalPreview; sourceSize={source?.width ?? 0}x{source?.height ?? 0}; previewSize={preview?.width ?? 0}x{preview?.height ?? 0}; crop={DescribePlacementCrop(state)}; stretch={DescribePlacementStretch(state)}; flip={DescribePlacementFlip(state)}; filters={DescribePlacementFilters(state)}; generatedPreviewCache={generatedPreviewCache}; cpuPixels={cpuPixelSamplingUsed}; elapsedMs={elapsedMs:0.##}; suit={_selectedSuitId}");
    }

    private void LogDecalEditPreviewFastSkipped(Texture2D source, PlacementEditState state, string reason, string failureReason)
    {
        var key = $"DecalEditPreviewFastSkipped|reason={reason}|failure={failureReason}|state={state?.Revision ?? -1}";
        if (Time.unscaledTime - _lastPlacementEditPreviewLogTime < 0.5f && string.Equals(key, _lastPlacementEditPreviewLogKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastPlacementEditPreviewLogTime = Time.unscaledTime;
        _lastPlacementEditPreviewLogKey = key;
        DrawableSuitsDiagnostics.Warn($"DecalEditPreviewFastSkipped: source={GetPlacementEditSourceName(PlacementEditTarget.Decal)}; reason={reason}; failure={failureReason}; sourceSize={source?.width ?? 0}x{source?.height ?? 0}; crop={DescribePlacementCrop(state)}; stretch={DescribePlacementStretch(state)}; flip={DescribePlacementFlip(state)}; filters={DescribePlacementFilters(state)}; suit={_selectedSuitId}");
    }

    private void SetSavedDesignsStatus(string message)
    {
        if (_savedDesignsStatusLabel != null)
        {
            _savedDesignsStatusLabel.text = message ?? string.Empty;
        }
    }

    private void DeleteSelectedDesign()
    {
        var selectedPath = GetSelectedFilePath(_designFiles, _selectedDesignIndex);
        if (!TryResolveSelectedManagedFile(_designFiles, _selectedDesignIndex, DrawableSuitsPaths.Saves, ".json", out var safePath, out var failureReason))
        {
            CancelPendingDesignDelete("invalid design delete target");
            SetSavedDesignsStatus(string.IsNullOrWhiteSpace(failureReason) ? "Select a saved design first." : failureReason);
            DrawableSuitsDiagnostics.Warn($"SavedDesignDeleteRejected: selectedIndex={_selectedDesignIndex}; selectedPath={selectedPath ?? "none"}; reason={failureReason}");
            return;
        }

        if (!string.Equals(_pendingDeleteDesignPath, safePath, StringComparison.OrdinalIgnoreCase))
        {
            _pendingDeleteDesignPath = safePath;
            SetSavedDesignsStatus($"Press Confirm Delete to permanently delete {Path.GetFileNameWithoutExtension(safePath)}.");
            UpdateSavedDesignDeleteButton();
            DrawableSuitsDiagnostics.Warn($"SavedDesignDeleteArmed: file={safePath}; selectedIndex={_selectedDesignIndex}; designCount={_designFiles.Count}");
            return;
        }

        var deletedName = Path.GetFileNameWithoutExtension(safePath);
        try
        {
            File.Delete(safePath);
        }
        catch (Exception ex)
        {
            CancelPendingDesignDelete("design delete failed");
            SetSavedDesignsStatus("Delete failed. Check diagnostics.");
            DrawableSuitsDiagnostics.Exception($"SavedDesignDeleted failed. file={safePath}; selectedIndex={_selectedDesignIndex}", ex);
            return;
        }

        _pendingDeleteDesignPath = string.Empty;
        RefreshFileLists();
        SetSavedDesignsStatus($"Deleted {deletedName}.");
        UpdateUiState();
        RebuildSelectableNavigation();
        DrawableSuitsDiagnostics.Info($"SavedDesignDeleted: file={safePath}; designName={deletedName}; designCount={_designFiles.Count}; selectedDesignIndex={_selectedDesignIndex}");
    }

    private void DeleteSelectedDecal()
    {
        var selectedPath = GetSelectedFilePath(_decalFiles, _selectedDecalIndex);
        if (!TryResolveSelectedManagedFile(_decalFiles, _selectedDecalIndex, DrawableSuitsPaths.Decals, null, out var safePath, out var failureReason)
            || !TextureTools.IsImagePath(safePath))
        {
            CancelPendingDecalDelete("invalid decal delete target");
            SetDecalsStatus(string.IsNullOrWhiteSpace(failureReason) ? "Select a decal first." : failureReason);
            DrawableSuitsDiagnostics.Warn($"DecalDeleteRejected: selectedIndex={_selectedDecalIndex}; selectedPath={selectedPath ?? "none"}; reason={failureReason}");
            return;
        }

        if (!string.Equals(_pendingDeleteDecalPath, safePath, StringComparison.OrdinalIgnoreCase))
        {
            _pendingDeleteDecalPath = safePath;
            SetDecalsStatus($"Press Confirm Delete to permanently delete {Path.GetFileName(safePath)}.");
            UpdateDecalDeleteButton();
            DrawableSuitsDiagnostics.Warn($"DecalDeleteArmed: file={safePath}; selectedIndex={_selectedDecalIndex}; decalCount={_decalFiles.Count}");
            return;
        }

        var deletedName = Path.GetFileName(safePath);
        try
        {
            File.Delete(safePath);
        }
        catch (Exception ex)
        {
            CancelPendingDecalDelete("decal delete failed");
            SetDecalsStatus("Delete failed. Check diagnostics.");
            DrawableSuitsDiagnostics.Exception($"DecalDeleted failed. file={safePath}; selectedIndex={_selectedDecalIndex}", ex);
            return;
        }

        _pendingDeleteDecalPath = string.Empty;
        if (_loadedDecal != null)
        {
            ClearPlacementSourcePixelCache(PlacementEditTarget.Decal, "decal deleted");
            Destroy(_loadedDecal);
            _loadedDecal = null;
        }

        ResetPlacementEdit(PlacementEditTarget.Decal, "decal deleted", true);
        InvalidateDecalPreview("decal deleted");
        RefreshFileLists();
        EnsureValidToolForCurrentState("decal deleted");
        SetDecalsStatus($"Deleted {deletedName}.");
        SetStatus($"Deleted decal {deletedName}.", false);
        UpdateUiState();
        RebuildSelectableNavigation();
        DrawableSuitsDiagnostics.Info($"DecalDeleted: file={safePath}; decalName={deletedName}; decalCount={_decalFiles.Count}; selectedDecalIndex={_selectedDecalIndex}; loadedDecal=false; tool={_tool}");
    }

    private void UpdateSavedDesignDeleteButton()
    {
        if (_deleteSelectedDesignButton == null)
        {
            return;
        }

        var selectedPath = GetSelectedFilePath(_designFiles, _selectedDesignIndex);
        var pending = !string.IsNullOrWhiteSpace(selectedPath)
            && string.Equals(_pendingDeleteDesignPath, SafeFullPathOrEmpty(selectedPath), StringComparison.OrdinalIgnoreCase);
        SetButtonLabel(_deleteSelectedDesignButton, pending ? "Confirm Delete" : "Delete Selected");
        SetInteractable(_deleteSelectedDesignButton, _selectedDesignIndex >= 0 && _selectedDesignIndex < _designFiles.Count);
    }

    private void UpdateDecalDeleteButton()
    {
        if (_deleteSelectedDecalButton == null)
        {
            return;
        }

        var selectedPath = GetSelectedFilePath(_decalFiles, _selectedDecalIndex);
        var pending = !string.IsNullOrWhiteSpace(selectedPath)
            && string.Equals(_pendingDeleteDecalPath, SafeFullPathOrEmpty(selectedPath), StringComparison.OrdinalIgnoreCase);
        SetButtonLabel(_deleteSelectedDecalButton, pending ? "Confirm Delete" : "Delete Selected");
        SetInteractable(_deleteSelectedDecalButton, _pendingDecalImportProcess == null && _selectedDecalIndex >= 0 && _selectedDecalIndex < _decalFiles.Count);
    }

    private void UpdateAddDecalButton()
    {
        if (_addDecalButton == null)
        {
            return;
        }

        var pending = _pendingDecalImportProcess != null;
        SetButtonLabel(_addDecalButton, pending ? "Waiting..." : "Add Decal");
        SetInteractable(_addDecalButton, !pending && _decalsPanelObject != null && _decalsPanelObject.activeSelf);
    }

    private void CancelPendingFileDeletes(string reason)
    {
        CancelPendingDesignDelete(reason);
        CancelPendingDecalDelete(reason);
    }

    private void CancelPendingListDelete(string listName, string reason)
    {
        if (string.Equals(listName, "Design", StringComparison.OrdinalIgnoreCase))
        {
            CancelPendingDesignDelete(reason);
        }
        else if (string.Equals(listName, "Decal", StringComparison.OrdinalIgnoreCase))
        {
            CancelPendingDecalDelete(reason);
        }
    }

    private void CancelPendingDesignDelete(string reason)
    {
        if (string.IsNullOrWhiteSpace(_pendingDeleteDesignPath))
        {
            return;
        }

        var oldPath = _pendingDeleteDesignPath;
        _pendingDeleteDesignPath = string.Empty;
        UpdateSavedDesignDeleteButton();
        DrawableSuitsDiagnostics.Info($"SavedDesignDeleteCanceled: reason={reason}; file={oldPath}");
    }

    private void CancelPendingDecalDelete(string reason)
    {
        if (string.IsNullOrWhiteSpace(_pendingDeleteDecalPath))
        {
            return;
        }

        var oldPath = _pendingDeleteDecalPath;
        _pendingDeleteDecalPath = string.Empty;
        UpdateDecalDeleteButton();
        DrawableSuitsDiagnostics.Info($"DecalDeleteCanceled: reason={reason}; file={oldPath}");
    }

    private static bool TryResolveSelectedManagedFile(List<string> files, int index, string rootDirectory, string requiredExtension, out string safePath, out string failureReason)
    {
        safePath = null;
        failureReason = string.Empty;
        var selectedPath = GetSelectedFilePath(files, index);
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            failureReason = "Select a file first.";
            return false;
        }

        try
        {
            var fullPath = Path.GetFullPath(selectedPath);
            var rootPath = Path.GetFullPath(rootDirectory);
            if (!rootPath.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            {
                rootPath += Path.DirectorySeparatorChar;
            }

            if (!fullPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
            {
                failureReason = "Delete target is outside the DrawableSuits folder.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(requiredExtension)
                && !string.Equals(Path.GetExtension(fullPath), requiredExtension, StringComparison.OrdinalIgnoreCase))
            {
                failureReason = $"Delete target must be a {requiredExtension} file.";
                return false;
            }

            if (!File.Exists(fullPath))
            {
                failureReason = "Selected file no longer exists.";
                return false;
            }

            safePath = fullPath;
            return true;
        }
        catch (Exception ex)
        {
            failureReason = "Delete target could not be validated.";
            DrawableSuitsDiagnostics.Exception($"File delete target validation failed. selectedPath={selectedPath}; root={rootDirectory}; requiredExtension={requiredExtension ?? "any"}", ex);
            return false;
        }
    }

    private static string SafeFullPathOrEmpty(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return string.Empty;
        }
    }

    private void CopyCurrentDesignCode()
    {
        DrawableSuitsDiagnostics.Info($"DesignCodeExportRequested: selectedSuitId={_selectedSuitId}; designName={_designName}");
        if (!DrawableSuitsPlugin.Registry.TryExportDesignCode(_selectedSuitId, _designName, out var code, out var info, out var failureReason))
        {
            SetDesignCodeStatus($"Export failed: {failureReason}");
            DrawableSuitsDiagnostics.Warn($"DesignCodeExportFailed: selectedSuitId={_selectedSuitId}; designName={_designName}; reason={failureReason}");
            return;
        }

        if (_designCodeInput != null)
        {
            _designCodeInput.text = code;
        }

        GUIUtility.systemCopyBuffer = code;
        SetDesignCodeStatus($"Code copied. Length {info.CodeLength:n0} chars.");
        DrawableSuitsDiagnostics.Info($"DesignCodeExported: format={info.FormatVersion}; designName={info.DesignName}; baseSuit={info.BaseSuitName}; sourceSuitId={info.SourceSuitId}; texture={info.Width}x{info.Height}; pngBytes={info.PngBytes}; payloadBytes={info.PayloadBytes}; jsonBytes={info.JsonBytes}; compressedBytes={info.CompressedBytes}; codeLength={info.CodeLength}");
    }

    private void PasteDesignCodeFromClipboard()
    {
        var clipboard = GUIUtility.systemCopyBuffer ?? string.Empty;
        if (_designCodeInput != null)
        {
            _designCodeInput.text = clipboard;
        }

        SetDesignCodeStatus(string.IsNullOrWhiteSpace(clipboard)
            ? "Clipboard is empty."
            : $"Pasted {clipboard.Length:n0} characters.");
        DrawableSuitsDiagnostics.Info($"DesignCodePastedFromClipboard: clipboardLength={clipboard.Length}; hasPrefix={DrawableSuitDesignCode.HasSupportedPrefix(clipboard)}");
    }

    private void ImportDesignCodeFromField()
    {
        var code = _designCodeInput != null ? _designCodeInput.text : string.Empty;
        DrawableSuitsDiagnostics.Info($"DesignCodeImportRequested: selectedSuitId={_selectedSuitId}; codeLength={(code?.Length ?? 0)}; hasPrefix={DrawableSuitDesignCode.HasSupportedPrefix(code)}; cameraBefore={_worldCameraYaw:0.##},{_worldCameraPitch:0.##},{_worldCameraDistance:0.##}");
        if (!DrawableSuitDesignCode.TryDecode(code, DrawableSuitsPlugin.ModConfig.MaxTextureSize.Value, out var payload, out var texture, out var info, out var failureReason))
        {
            SetDesignCodeStatus($"Import failed: {failureReason}");
            DrawableSuitsDiagnostics.Warn($"DesignCodeImportFailed: stage=decode; selectedSuitId={_selectedSuitId}; codeLength={(code?.Length ?? 0)}; reason={failureReason}");
            return;
        }

        try
        {
            SaveUndo("Import code");
            if (!DrawableSuitsPlugin.Registry.ImportDecodedDesignCode(_selectedSuitId, payload, texture, out var importedDesignName, out failureReason))
            {
                DropLastUndoEntry("Import code failed");
                SetDesignCodeStatus($"Import failed: {failureReason}");
                DrawableSuitsDiagnostics.Warn($"DesignCodeImportFailed: stage=apply; selectedSuitId={_selectedSuitId}; format={info.FormatVersion}; designName={info.DesignName}; sourceSuitId={info.SourceSuitId}; texture={info.Width}x{info.Height}; pngBytes={info.PngBytes}; compressedBytes={info.CompressedBytes}; codeLength={info.CodeLength}; reason={failureReason}");
                return;
            }

            _designName = importedDesignName;
            if (_designNameInput != null)
            {
                _designNameInput.text = _designName;
            }

            ClearRedoHistory("design code import");
            InvalidateDecalPreview("design code import");
            InvalidateMirrorSurfaceMap("design code import");
            TryRebuildPreviewForCurrentReadiness("ImportDesignCode");
            RefreshEditorReadiness("after design code import");
            UpdateUiState();
            SetStatus("Design code imported. Press Save or Apply when ready.", false);
            SetDesignCodeStatus($"Imported {importedDesignName}. Press Save or Apply when ready.");
            DrawableSuitsDiagnostics.Info($"DesignCodeImported: format={info.FormatVersion}; designName={importedDesignName}; baseSuit={info.BaseSuitName}; sourceSuitId={info.SourceSuitId}; targetSuitId={_selectedSuitId}; texture={info.Width}x{info.Height}; pngBytes={info.PngBytes}; payloadBytes={info.PayloadBytes}; jsonBytes={info.JsonBytes}; compressedBytes={info.CompressedBytes}; codeLength={info.CodeLength}; broadcast=False; cameraAfter={_worldCameraYaw:0.##},{_worldCameraPitch:0.##},{_worldCameraDistance:0.##}");
        }
        finally
        {
            if (texture != null)
            {
                Destroy(texture);
            }
        }
    }

    private void SetDesignCodeStatus(string message)
    {
        if (_designCodeStatusLabel != null)
        {
            _designCodeStatusLabel.text = message ?? string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(message))
        {
            SetStatus(message, false);
        }
    }

    private void ImportDecalFromDialog()
    {
        CancelPendingDecalDelete("add decal");
        if (_pendingDecalImportProcess != null)
        {
            SetDecalsStatus("Waiting for image selection...");
            SetStatus("Waiting for image selection...", false);
            return;
        }

        if (!TryStartExternalDecalImportPicker(out var failureReason))
        {
            SetDecalsStatus(failureReason);
            SetStatus(failureReason, false);
        }
    }

    private void CompleteDecalImportFromPicker(string selectedPath)
    {
        if (!TryImportDecalImage(selectedPath, out var importedPath, out var copied, out var importFailure))
        {
            SetDecalsStatus(importFailure);
            SetStatus(importFailure, false);
            DrawableSuitsDiagnostics.Warn($"DecalImportFailed: stage=import; source={selectedPath}; reason={importFailure}; decalsPath={DrawableSuitsPaths.Decals}");
            return;
        }

        RefreshFileLists();
        var importedIndex = FindFileIndex(_decalFiles, importedPath);
        if (importedIndex < 0)
        {
            SetDecalsStatus("Imported decal, but it was not found after refresh.");
            SetStatus("Imported decal, but it was not found after refresh.", false);
            DrawableSuitsDiagnostics.Warn($"DecalImportFailed: stage=refresh-select; source={selectedPath}; target={importedPath}; copied={copied}; decalCount={_decalFiles.Count}");
            return;
        }

        DrawableSuitsDiagnostics.Info($"DecalImported: source={selectedPath}; target={importedPath}; copied={copied}; selectedIndex={importedIndex}; decalCount={_decalFiles.Count}");
        SelectDecal(importedIndex);
        SetStatus($"Imported decal: {Path.GetFileName(importedPath)}.", false);
    }

    private bool TryStartExternalDecalImportPicker(out string failureReason)
    {
        failureReason = string.Empty;
        if (Application.platform != RuntimePlatform.WindowsPlayer && Application.platform != RuntimePlatform.WindowsEditor)
        {
            failureReason = "File picker is only available on Windows. Add PNG/JPG files to the Decals folder and press Refresh.";
            DrawableSuitsDiagnostics.Warn($"DecalImportPickerFailed: stage=start; reason=non-windows-platform; platform={Application.platform}; decalsPath={DrawableSuitsPaths.Decals}");
            return false;
        }

        try
        {
            var script = string.Join("\r\n", new[]
            {
                "try {",
                "Add-Type -AssemblyName System.Windows.Forms",
                "$dialog = New-Object System.Windows.Forms.OpenFileDialog",
                "$dialog.Title = 'Add DrawableSuits Decal'",
                "$dialog.Filter = 'Image Files (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg|PNG Files (*.png)|*.png|JPEG Files (*.jpg;*.jpeg)|*.jpg;*.jpeg'",
                "$dialog.CheckFileExists = $true",
                "$dialog.CheckPathExists = $true",
                "$dialog.Multiselect = $false",
                "$pictures = [Environment]::GetFolderPath('MyPictures')",
                "if ([System.IO.Directory]::Exists($pictures)) { $dialog.InitialDirectory = $pictures }",
                "$result = $dialog.ShowDialog()",
                "if ($result -eq [System.Windows.Forms.DialogResult]::OK) { [Console]::Out.WriteLine($dialog.FileName) }",
                "exit 0",
                "} catch {",
                "[Console]::Error.WriteLine($_.Exception.Message)",
                "exit 2",
                "}"
            });
            var encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            var startInfo = new DiagnosticsProcessStartInfo
            {
                FileName = ResolveWindowsPowerShellPath(),
                Arguments = $"-NoProfile -STA -ExecutionPolicy Bypass -EncodedCommand {encodedScript}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            var process = DiagnosticsProcess.Start(startInfo);
            if (process == null)
            {
                failureReason = "File picker failed. Add PNG/JPG files to the Decals folder and press Refresh.";
                DrawableSuitsDiagnostics.Warn($"DecalImportPickerFailed: stage=start; reason=process-null; picker={Path.GetFileName(startInfo.FileName)}; decalsPath={DrawableSuitsPaths.Decals}");
                return false;
            }

            _pendingDecalImportProcess = process;
            _pendingDecalImportStartedAt = Time.realtimeSinceStartup;
            _pendingDecalImportId++;
            SetDecalsStatus("Waiting for image selection...");
            SetStatus("Waiting for image selection...", false);
            UpdateAddDecalButton();
            DrawableSuitsDiagnostics.Info($"DecalImportPickerStarted: id={_pendingDecalImportId}; picker={Path.GetFileName(startInfo.FileName)}; decalsPath={DrawableSuitsPaths.Decals}; selectedDecalIndex={_selectedDecalIndex}; decalCount={_decalFiles.Count}; maxTextureSize={DrawableSuitsPlugin.ModConfig.MaxTextureSize.Value}");
            return true;
        }
        catch (Exception ex)
        {
            failureReason = "File picker failed. Add PNG/JPG files to the Decals folder and press Refresh.";
            DrawableSuitsDiagnostics.Exception("DecalImportPickerFailed: stage=start; reason=exception", ex);
            return false;
        }
    }

    private void PollPendingDecalImport()
    {
        var process = _pendingDecalImportProcess;
        if (process == null)
        {
            return;
        }

        var id = _pendingDecalImportId;
        try
        {
            if (!process.HasExited)
            {
                if (Time.realtimeSinceStartup - _pendingDecalImportStartedAt > DecalImportPickerTimeoutSeconds)
                {
                    ClearPendingDecalImportProcess(killProcess: true, out _);
                    var timeoutStatus = "File picker timed out. Add PNG/JPG files to the Decals folder and press Refresh.";
                    SetDecalsStatus(timeoutStatus);
                    SetStatus(timeoutStatus, false);
                    UpdateAddDecalButton();
                    DrawableSuitsDiagnostics.Warn($"DecalImportPickerFailed: id={id}; stage=poll; reason=timeout; timeoutSeconds={DecalImportPickerTimeoutSeconds:0.#}; decalsPath={DrawableSuitsPaths.Decals}");
                }

                return;
            }

            var exitCode = process.ExitCode;
            var stdout = SafeReadProcessStream(process, standardOutput: true);
            var stderr = SafeReadProcessStream(process, standardOutput: false);
            ClearPendingDecalImportProcess(killProcess: false, out _);
            UpdateAddDecalButton();

            var selectedPath = ExtractFirstOutputLine(stdout);
            var stderrSummary = SummarizeProcessText(stderr);
            if (exitCode != 0)
            {
                var failure = "File picker failed. Add PNG/JPG files to the Decals folder and press Refresh.";
                SetDecalsStatus(failure);
                SetStatus(failure, false);
                DrawableSuitsDiagnostics.Warn($"DecalImportPickerFailed: id={id}; stage=completed; exitCode={exitCode}; stderr={stderrSummary}; decalsPath={DrawableSuitsPaths.Decals}");
                return;
            }

            if (string.IsNullOrWhiteSpace(selectedPath))
            {
                SetDecalsStatus("File selection canceled.");
                SetStatus("File selection canceled.", false);
                DrawableSuitsDiagnostics.Info($"DecalImportPickerCanceled: id={id}; reason=user-canceled; exitCode={exitCode}; stderr={stderrSummary}; decalsPath={DrawableSuitsPaths.Decals}");
                return;
            }

            DrawableSuitsDiagnostics.Info($"DecalImportPickerCompleted: id={id}; exitCode={exitCode}; selectedPath={selectedPath}; selectedFile={Path.GetFileName(selectedPath)}; stderr={stderrSummary}");
            CompleteDecalImportFromPicker(selectedPath);
        }
        catch (Exception ex)
        {
            ClearPendingDecalImportProcess(killProcess: true, out _);
            var failure = "File picker failed. Add PNG/JPG files to the Decals folder and press Refresh.";
            SetDecalsStatus(failure);
            SetStatus(failure, false);
            UpdateAddDecalButton();
            DrawableSuitsDiagnostics.Exception($"DecalImportPickerFailed: id={id}; stage=poll; reason=exception", ex);
        }
    }

    private void CancelPendingDecalImport(string reason)
    {
        if (_pendingDecalImportProcess == null)
        {
            return;
        }

        var id = _pendingDecalImportId;
        ClearPendingDecalImportProcess(killProcess: true, out var killed);
        UpdateAddDecalButton();
        DrawableSuitsDiagnostics.Info($"DecalImportPickerCanceled: id={id}; reason={reason}; killed={killed}; decalsPath={DrawableSuitsPaths.Decals}");
    }

    private void ClearPendingDecalImportProcess(bool killProcess, out bool killed)
    {
        killed = false;
        var process = _pendingDecalImportProcess;
        _pendingDecalImportProcess = null;
        _pendingDecalImportStartedAt = 0f;
        if (process == null)
        {
            return;
        }

        try
        {
            if (killProcess && !process.HasExited)
            {
                process.Kill();
                killed = true;
            }
        }
        catch (Exception ex)
        {
            DrawableSuitsDiagnostics.Exception("DecalImportPickerFailed: stage=cleanup; reason=kill-or-exit-check-exception", ex);
        }

        try
        {
            process.Dispose();
        }
        catch (Exception ex)
        {
            DrawableSuitsDiagnostics.Exception("DecalImportPickerFailed: stage=cleanup; reason=dispose-exception", ex);
        }
    }

    private static string ResolveWindowsPowerShellPath()
    {
        try
        {
            var systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
            var powerShellPath = Path.Combine(systemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            if (File.Exists(powerShellPath))
            {
                return powerShellPath;
            }
        }
        catch
        {
        }

        return "powershell.exe";
    }

    private static string SafeReadProcessStream(DiagnosticsProcess process, bool standardOutput)
    {
        try
        {
            return standardOutput
                ? process.StandardOutput.ReadToEnd()
                : process.StandardError.ReadToEnd();
        }
        catch (Exception ex)
        {
            DrawableSuitsDiagnostics.Exception($"DecalImportPickerFailed: stage=stream-read; stream={(standardOutput ? "stdout" : "stderr")}", ex);
            return string.Empty;
        }
    }

    private static string ExtractFirstOutputLine(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return string.Empty;
        }

        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        return lines.Length == 0 ? string.Empty : lines[0].Trim();
    }

    private static string SummarizeProcessText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "none";
        }

        var summary = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return summary.Length <= 180 ? summary : summary.Substring(0, 180) + "...";
    }

    private bool TryImportDecalImage(string sourcePath, out string importedPath, out bool copied, out string failureReason)
    {
        importedPath = null;
        copied = false;
        failureReason = string.Empty;
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            failureReason = "No image file was selected.";
            return false;
        }

        string sourceFullPath;
        try
        {
            sourceFullPath = Path.GetFullPath(sourcePath);
        }
        catch (Exception ex)
        {
            failureReason = "Selected decal path is invalid.";
            DrawableSuitsDiagnostics.Exception($"DecalImportFailed path validation exception. source={sourcePath}", ex);
            return false;
        }

        if (!File.Exists(sourceFullPath) || Directory.Exists(sourceFullPath))
        {
            failureReason = "Selected decal file does not exist.";
            return false;
        }

        if (!TextureTools.IsImagePath(sourceFullPath))
        {
            failureReason = "Selected file must be PNG, JPG, or JPEG.";
            return false;
        }

        Texture2D validationTexture = null;
        try
        {
            validationTexture = TextureTools.LoadImageFile(sourceFullPath, DrawableSuitsPlugin.ModConfig.MaxTextureSize.Value);
            if (validationTexture == null)
            {
                failureReason = "Selected image could not be decoded.";
                return false;
            }
        }
        catch (Exception ex)
        {
            failureReason = "Selected image could not be decoded.";
            DrawableSuitsDiagnostics.Exception($"DecalImportFailed image decode exception. source={sourceFullPath}", ex);
            return false;
        }
        finally
        {
            if (validationTexture != null)
            {
                Destroy(validationTexture);
            }
        }

        try
        {
            DrawableSuitsPaths.EnsureCreated();
            importedPath = CreateUniqueDecalImportPath(sourceFullPath);
            if (!string.Equals(sourceFullPath, importedPath, StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(sourceFullPath, importedPath, false);
                copied = true;
            }

            return true;
        }
        catch (Exception ex)
        {
            failureReason = "Selected image could not be copied into the Decals folder.";
            DrawableSuitsDiagnostics.Exception($"DecalImportFailed copy exception. source={sourceFullPath}; target={importedPath ?? "null"}; decalsPath={DrawableSuitsPaths.Decals}", ex);
            return false;
        }
    }

    private static string CreateUniqueDecalImportPath(string sourceFullPath)
    {
        var decalsRoot = Path.GetFullPath(DrawableSuitsPaths.Decals);
        var extension = Path.GetExtension(sourceFullPath).ToLowerInvariant();
        var baseName = TextureTools.SanitizeFileName(Path.GetFileNameWithoutExtension(sourceFullPath));
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "Decal";
        }

        var sameFolderTarget = Path.GetFullPath(Path.Combine(decalsRoot, baseName + extension));
        if (IsPathUnderRoot(sourceFullPath, decalsRoot)
            && string.Equals(sourceFullPath, sameFolderTarget, StringComparison.OrdinalIgnoreCase))
        {
            return sourceFullPath;
        }

        var candidate = sameFolderTarget;
        var suffix = 1;
        while (File.Exists(candidate))
        {
            candidate = Path.GetFullPath(Path.Combine(decalsRoot, $"{baseName}_{suffix}{extension}"));
            suffix++;
        }

        return candidate;
    }

    private static bool IsPathUnderRoot(string path, string rootDirectory)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var rootPath = Path.GetFullPath(rootDirectory);
            if (!rootPath.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            {
                rootPath += Path.DirectorySeparatorChar;
            }

            return fullPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private void SelectDecal(int index)
    {
        if (index < 0 || index >= _decalFiles.Count)
        {
            return;
        }

        CancelPendingDecalDelete("decal selection changed");
        _selectedDecalIndex = index;
        ResetPlacementEdit(PlacementEditTarget.Decal, "select decal", true);
        InvalidateDecalPreview("select decal");
        if (_loadedDecal != null)
        {
            ClearPlacementSourcePixelCache(PlacementEditTarget.Decal, "select decal");
            Destroy(_loadedDecal);
        }

        _loadedDecal = TextureTools.LoadImageFile(_decalFiles[index], DrawableSuitsPlugin.ModConfig.MaxTextureSize.Value);
        SetTool(EditorTool.Decal);
        CloseDecalsPanel();
        RefreshListButtons();
        UpdateUiState();
        SetStatus($"Decal selected: {Path.GetFileName(_decalFiles[index])}.", false);
        DrawableSuitsDiagnostics.Info($"Selected decal index={index}; file={_decalFiles[index]}; loaded={_loadedDecal != null}");
    }

    private bool IsWorldThirdPersonMode => string.Equals(_previewMode, "WorldThirdPerson", StringComparison.OrdinalIgnoreCase);

    private WorldCameraState CaptureWorldCameraState()
    {
        return new WorldCameraState
        {
            Valid = IsWorldThirdPersonMode && _worldEditorCamera != null && _worldPreviewReady,
            Yaw = _worldCameraYaw,
            Pitch = _worldCameraPitch,
            Distance = _worldCameraDistance
        };
    }

    private static bool ShouldPreserveWorldCameraState(string context, WorldCameraState state)
    {
        if (!state.Valid || string.IsNullOrWhiteSpace(context))
        {
            return false;
        }

        return context.IndexOf("LoadSelectedDesign", StringComparison.OrdinalIgnoreCase) >= 0
            || context.IndexOf("ImportDesignCode", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void ToggleUvFallback()
    {
        _uvFallbackMode = !_uvFallbackMode;
        DrawableSuitsDiagnostics.Info($"Texture-only fallback toggled. enabled={_uvFallbackMode}");
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
        var preservedCameraState = CaptureWorldCameraState();
        var preserveCamera = ShouldPreserveWorldCameraState(context, preservedCameraState);
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
            SetStatus("Ready. Texture-only UV panel is active. Full-suit editing.", false);
            return;
        }

        if (SetupWorldThirdPersonPreview(context, preserveCamera ? preservedCameraState : default))
        {
            RefreshTexturePanelPreview(context, true);
            return;
        }

        _uvFallbackMode = true;
        DestroyPreview();
        UseTexturePreview(context, true);
        SetStatus("Third-person setup failed; using UV fallback preview.", true);
    }

    private bool SetupWorldThirdPersonPreview(string context, WorldCameraState preservedCameraState = default)
    {
        DestroyPreview();
        var texture = _selectedSuitId >= 0 ? DrawableSuitsPlugin.Registry.GetEditableTexture(_selectedSuitId) : null;
        var player = StartOfRound.Instance?.localPlayerController;
        var source = FindBestSuitRenderer(player);
        if (texture == null || player == null || source == null)
        {
            DrawableSuitsDiagnostics.Warn($"WorldThirdPerson setup skipped [{context}]. texture={DescribeEditableTexture()}; player={DrawableSuitsPlugin.DescribeUnityObject(player)}; source={DrawableSuitsPlugin.DescribeUnityObject(source)}");
            return false;
        }

        try
        {
            _usingTexturePreview = false;
            _previewMode = "WorldThirdPerson";
            if (preservedCameraState.Valid)
            {
                _worldCameraYaw = preservedCameraState.Yaw;
                _worldCameraPitch = preservedCameraState.Pitch;
                _worldCameraDistance = preservedCameraState.Distance;
                DrawableSuitsDiagnostics.Info($"World camera state preserved for preview rebuild. context={context}; yaw={_worldCameraYaw:0.##}; pitch={_worldCameraPitch:0.##}; distance={_worldCameraDistance:0.##}");
            }
            else
            {
                _worldCameraDistance = Mathf.Clamp(DrawableSuitsPlugin.ModConfig.ThirdPersonCameraDistance.Value, 1.5f, 8f);
                _worldCameraYaw = player.transform.eulerAngles.y;
                _worldCameraPitch = 12f;
                DrawableSuitsDiagnostics.Info($"World camera state initialized for preview setup. context={context}; yaw={_worldCameraYaw:0.##}; pitch={_worldCameraPitch:0.##}; distance={_worldCameraDistance:0.##}");
            }
            _worldPaintLayer = SelectWorldPaintLayer();
            _worldSourceRenderer = source;
            _worldSourceRendererSummary = DescribeRendererCandidate(source, "selected");
            RefreshTexturePanelPreview($"{context}:world texture panel", true);
            CaptureAndHideLocalPlayerRenderers(player, source);

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
            _worldEditorCamera.cullingMask = BuildWorldEditorCameraMask(mainCamera);
            _worldEditorCamera.enabled = false;

            _worldPaintProxyObject = new GameObject("DrawableSuitsWorldAvatarProxy");
            _worldPaintProxyObject.hideFlags = HideFlags.HideAndDontSave;
            _worldPaintProxyObject.transform.SetParent(source.transform, false);
            _worldPaintProxyObject.layer = _worldPaintLayer;
            _worldPaintMesh = new Mesh { name = "DrawableSuitsWorldPaintMesh" };
            _worldAvatarMeshFilter = _worldPaintProxyObject.AddComponent<MeshFilter>();
            _worldAvatarMeshFilter.sharedMesh = _worldPaintMesh;
            _worldAvatarRenderer = _worldPaintProxyObject.AddComponent<MeshRenderer>();
            _worldAvatarRenderer.sharedMaterials = BuildWorldProxyMaterials(source, true);
            _worldPaintCollider = _worldPaintProxyObject.AddComponent<MeshCollider>();
            _worldPaintCollider.convex = false;

            _worldBrushMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _worldBrushMarker.name = "DrawableSuitsWorldBrushMarker";
            _worldBrushMarker.hideFlags = HideFlags.HideAndDontSave;
            _worldBrushMarker.layer = _worldPaintLayer;
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

            var proxyReady = UpdateWorldPaintProxy(true, context);
            _worldPreviewReady = proxyReady && _worldEditorCamera != null && _worldPaintCollider != null;
            if (_worldPreviewReady)
            {
                UpdateWorldEditorCamera(true);
            }
            _hasPreviewCollider = _worldPaintCollider != null;
            _canPaint = texture != null && _worldPreviewReady;
            SetStatus(_canPaint ? "Ready. Third-person and UV panel are active." : "Third-person editor opened, but paint proxy is not ready.", !_canPaint);
            DrawableSuitsDiagnostics.Info($"WorldThirdPerson setup complete. context={context}; ready={_worldPreviewReady}; player={player.name}; selectedRenderer={_worldSourceRendererSummary}; hiddenRenderers={_rendererRestoreStates.Count}; layer={_worldPaintLayer}; camera={DrawableSuitsPlugin.DescribeUnityObject(_worldEditorCamera)}; cameraMask={_worldEditorCamera.cullingMask}; avatarProxy={DrawableSuitsPlugin.DescribeUnityObject(_worldPaintProxyObject)}; proxyRenderer={DrawableSuitsPlugin.DescribeUnityObject(_worldAvatarRenderer)}; proxyMaterial={_worldAvatarRenderer?.sharedMaterial?.name ?? "null"}; proxyCollider={_worldPaintCollider != null}; editable={DescribeEditableTexture()}");
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

    private int BuildWorldEditorCameraMask(Camera mainCamera)
    {
        var worldMask = mainCamera != null ? mainCamera.cullingMask : ~0;
        var proxyMask = 1 << _worldPaintLayer;
        var mask = worldMask | proxyMask;
        DrawableSuitsDiagnostics.Info($"World editor camera mask built. mainCamera={mainCamera?.name ?? "null"}; mainMask={worldMask}; proxyLayer={_worldPaintLayer}; proxyMask={proxyMask}; finalMask={mask}");
        return mask;
    }

    private SkinnedMeshRenderer FindBestSuitRenderer(PlayerControllerB player)
    {
        if (player == null)
        {
            return null;
        }

        var candidates = new List<SkinnedMeshRenderer>();
        AddRendererCandidate(candidates, player.thisPlayerModel as SkinnedMeshRenderer);
        AddRendererCandidate(candidates, player.thisPlayerModelLOD1 as SkinnedMeshRenderer);
        AddRendererCandidate(candidates, player.thisPlayerModelLOD2 as SkinnedMeshRenderer);

        var childRenderers = player.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (var i = 0; i < childRenderers.Length; i++)
        {
            AddRendererCandidate(candidates, childRenderers[i]);
        }

        SkinnedMeshRenderer best = null;
        var bestScore = int.MinValue;
        for (var i = 0; i < candidates.Count; i++)
        {
            var renderer = candidates[i];
            var score = ScoreSuitRenderer(player, renderer, out var reason, out var rejected);
            DrawableSuitsDiagnostics.Info($"World renderer candidate: {DescribeRendererCandidate(renderer, rejected ? "rejected" : "candidate")}; score={score}; reason={reason}");
            if (!rejected && score > bestScore)
            {
                best = renderer;
                bestScore = score;
            }
        }

        if (best != null)
        {
            DrawableSuitsDiagnostics.Info($"World renderer selected: {DescribeRendererCandidate(best, "selected")}; score={bestScore}");
        }
        else
        {
            DrawableSuitsDiagnostics.Warn($"No valid world suit renderer found. candidates={candidates.Count}");
        }

        return best;
    }

    private static void AddRendererCandidate(List<SkinnedMeshRenderer> candidates, SkinnedMeshRenderer renderer)
    {
        if (renderer == null)
        {
            return;
        }

        for (var i = 0; i < candidates.Count; i++)
        {
            if (ReferenceEquals(candidates[i], renderer))
            {
                return;
            }
        }

        candidates.Add(renderer);
    }

    private int ScoreSuitRenderer(PlayerControllerB player, SkinnedMeshRenderer renderer, out string reason, out bool rejected)
    {
        rejected = false;
        if (renderer == null)
        {
            reason = "renderer null";
            rejected = true;
            return int.MinValue;
        }

        var mesh = renderer.sharedMesh;
        var vertexCount = mesh != null ? mesh.vertexCount : 0;
        var path = GetTransformPath(renderer.transform);
        var lowerPath = path.ToLowerInvariant();
        if (mesh == null || vertexCount < 300)
        {
            reason = $"mesh missing or too small vertices={vertexCount}";
            rejected = true;
            return -10000;
        }

        if (LooksFirstPersonOnly(lowerPath))
        {
            reason = "name/path indicates first-person arms, hands, held item, camera, or viewmodel";
            rejected = true;
            return -9000 + vertexCount / 100;
        }

        var boundsSize = renderer.bounds.size;
        var largest = Mathf.Max(boundsSize.x, Mathf.Max(boundsSize.y, boundsSize.z));
        var smallest = Mathf.Min(boundsSize.x, Mathf.Min(boundsSize.y, boundsSize.z));
        if (largest < 0.45f || smallest < 0.02f)
        {
            reason = $"bounds too small size={boundsSize}";
            rejected = true;
            return -8000 + vertexCount / 100;
        }

        var score = 0;
        if (ReferenceEquals(renderer, player.thisPlayerModel)) score += 220;
        if (ReferenceEquals(renderer, player.thisPlayerModelLOD1)) score += 160;
        if (ReferenceEquals(renderer, player.thisPlayerModelLOD2)) score += 80;
        if (renderer.enabled) score += 20;
        score += Mathf.Clamp(vertexCount / 100, 0, 120);
        score += Mathf.RoundToInt(Mathf.Clamp(largest * 25f, 0f, 90f));
        if (lowerPath.Contains("lod1")) score += 70;
        if (lowerPath.Contains("lod2")) score += 30;
        if (lowerPath.Contains("body") || lowerPath.Contains("player") || lowerPath.Contains("suit")) score += 25;
        if (RendererMaterialLooksCompatible(renderer)) score += 40;
        reason = $"accepted vertices={vertexCount}; bounds={boundsSize}; materialCompatible={RendererMaterialLooksCompatible(renderer)}";
        return score;
    }

    private static bool LooksFirstPersonOnly(string lowerPath)
    {
        return lowerPath.Contains("arms")
            || lowerPath.Contains("arm.")
            || lowerPath.Contains("/arm")
            || lowerPath.Contains("hand")
            || lowerPath.Contains("finger")
            || lowerPath.Contains("firstperson")
            || lowerPath.Contains("first person")
            || lowerPath.Contains("viewmodel")
            || lowerPath.Contains("view model")
            || lowerPath.Contains("helmet")
            || lowerPath.Contains("visor")
            || lowerPath.Contains("mask")
            || lowerPath.Contains("camera")
            || lowerPath.Contains("held")
            || lowerPath.Contains("item")
            || lowerPath.Contains("weapon");
    }

    private bool RendererMaterialLooksCompatible(Renderer renderer)
    {
        if (renderer == null)
        {
            return false;
        }

        var runtimeMaterial = _selectedSuitId >= 0 ? DrawableSuitsPlugin.Registry.GetRuntimeMaterial(_selectedSuitId) : null;
        var materials = renderer.sharedMaterials;
        for (var i = 0; i < materials.Length; i++)
        {
            var material = materials[i];
            if (material == null)
            {
                continue;
            }

            if (runtimeMaterial != null && ReferenceEquals(material, runtimeMaterial))
            {
                return true;
            }

            var texture = material.mainTexture;
            var materialName = material.name ?? string.Empty;
            if (texture != null || materialName.IndexOf("suit", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private void CaptureAndHideLocalPlayerRenderers(PlayerControllerB player, SkinnedMeshRenderer selectedSource)
    {
        RestorePlayerRenderers();
        if (player == null)
        {
            return;
        }

        var hidden = 0;
        hidden += CaptureAndHideRenderer(player.thisPlayerModel, "local thisPlayerModel");
        hidden += CaptureAndHideRenderer(player.thisPlayerModelLOD1, "local thisPlayerModelLOD1");
        hidden += CaptureAndHideRenderer(player.thisPlayerModelLOD2, "local thisPlayerModelLOD2");
        hidden += CaptureAndHideRenderer(player.thisPlayerModelArms, "local first-person arms");

        var playerRenderers = player.GetComponentsInChildren<Renderer>(true);
        for (var i = 0; i < playerRenderers.Length; i++)
        {
            hidden += CaptureAndHideRenderer(playerRenderers[i], "local player child");
        }

        var mainCamera = Camera.main;
        if (mainCamera != null)
        {
            var cameraRenderers = mainCamera.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < cameraRenderers.Length; i++)
            {
                hidden += CaptureAndHideRenderer(cameraRenderers[i], "local camera child");
            }
        }

        if (selectedSource != null)
        {
            CaptureRendererState(selectedSource);
            selectedSource.updateWhenOffscreen = true;
            selectedSource.enabled = false;
        }

        hidden += CaptureAndHideNearbyFirstPersonOverlayRenderers(player, selectedSource);

        DrawableSuitsDiagnostics.Info($"Hidden local renderers for third-person avatar proxy. hidden={hidden}; selectedSource={DescribeRendererCandidate(selectedSource, "selected")}; main={DescribeRendererState(player.thisPlayerModel)}; lod1={DescribeRendererState(player.thisPlayerModelLOD1)}; lod2={DescribeRendererState(player.thisPlayerModelLOD2)}; arms={DescribeRendererState(player.thisPlayerModelArms)}");
    }

    private int CaptureAndHideNearbyFirstPersonOverlayRenderers(PlayerControllerB player, Renderer selectedSource)
    {
        if (player == null)
        {
            return 0;
        }

        var hidden = 0;
        var playerPosition = player.transform.position;
        var camera = Camera.main;
        var renderers = Resources.FindObjectsOfTypeAll<Renderer>();
        for (var i = 0; i < renderers.Length; i++)
        {
            var renderer = renderers[i];
            if (renderer == null
                || ReferenceEquals(renderer, selectedSource)
                || renderer.gameObject.name.IndexOf("DrawableSuits", StringComparison.OrdinalIgnoreCase) >= 0
                || !renderer.gameObject.scene.IsValid()
                || !renderer.gameObject.activeInHierarchy)
            {
                continue;
            }

            var distanceToPlayer = Vector3.Distance(renderer.bounds.center, playerPosition);
            var distanceToCamera = camera != null ? Vector3.Distance(renderer.bounds.center, camera.transform.position) : float.MaxValue;
            if (distanceToPlayer > 4.5f && distanceToCamera > 2.5f)
            {
                continue;
            }

            var path = GetTransformPath(renderer.transform);
            if (!LooksFirstPersonOverlayRenderer(renderer, path))
            {
                continue;
            }

            hidden += CaptureAndHideRenderer(renderer, $"nearby first-person overlay path={path}");
            DrawableSuitsDiagnostics.Info($"Hidden nearby first-person overlay renderer. renderer={DescribeRendererState(renderer)}; distanceToPlayer={distanceToPlayer:0.##}; distanceToCamera={distanceToCamera:0.##}; material={renderer.sharedMaterial?.name ?? "null"}");
        }

        return hidden;
    }

    private static bool LooksFirstPersonOverlayRenderer(Renderer renderer, string path)
    {
        var lowerPath = (path ?? string.Empty).ToLowerInvariant();
        if (LooksFirstPersonOnly(lowerPath)
            || lowerPath.Contains("local")
            || lowerPath.Contains("view")
            || lowerPath.Contains("helmet")
            || lowerPath.Contains("visor")
            || lowerPath.Contains("mask")
            || lowerPath.Contains("head"))
        {
            return true;
        }

        var materialName = renderer?.sharedMaterial?.name ?? string.Empty;
        return materialName.IndexOf("helmet", StringComparison.OrdinalIgnoreCase) >= 0
            || materialName.IndexOf("visor", StringComparison.OrdinalIgnoreCase) >= 0
            || materialName.IndexOf("glass", StringComparison.OrdinalIgnoreCase) >= 0
            || materialName.IndexOf("view", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private int CaptureAndHideRenderer(Renderer renderer, string reason)
    {
        if (renderer == null || renderer.gameObject.name.IndexOf("DrawableSuits", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return 0;
        }

        CaptureRendererState(renderer);
        renderer.enabled = false;
        return 1;
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

        var skinned = renderer as SkinnedMeshRenderer;
        _rendererRestoreStates.Add(new RendererRestoreState
        {
            Renderer = renderer,
            Enabled = renderer.enabled,
            Layer = renderer.gameObject.layer,
            HasUpdateWhenOffscreen = skinned != null,
            UpdateWhenOffscreen = skinned != null && skinned.updateWhenOffscreen
        });
    }

    private void RestorePlayerRenderers()
    {
        for (var i = 0; i < _rendererRestoreStates.Count; i++)
        {
            var state = _rendererRestoreStates[i];
            if (state.Renderer != null)
            {
                state.Renderer.enabled = state.Enabled;
                state.Renderer.gameObject.layer = state.Layer;
                if (state.HasUpdateWhenOffscreen && state.Renderer is SkinnedMeshRenderer skinned)
                {
                    skinned.updateWhenOffscreen = state.UpdateWhenOffscreen;
                }
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
        return renderer != null ? $"{renderer.name}:enabled={renderer.enabled}:active={renderer.gameObject.activeInHierarchy}:layer={renderer.gameObject.layer}:path={GetTransformPath(renderer.transform)}" : "null";
    }

    private static string DescribeRendererCandidate(SkinnedMeshRenderer renderer, string state)
    {
        if (renderer == null)
        {
            return $"{state}:null";
        }

        var mesh = renderer.sharedMesh;
        var material = renderer.sharedMaterial;
        return $"{state}:{renderer.name}:path={GetTransformPath(renderer.transform)}:enabled={renderer.enabled}:active={renderer.gameObject.activeInHierarchy}:layer={renderer.gameObject.layer}:vertices={mesh?.vertexCount ?? 0}:bounds={renderer.bounds}:material={material?.name ?? "null"}";
    }

    private static string GetTransformPath(Transform transform)
    {
        if (transform == null)
        {
            return "null";
        }

        var names = new Stack<string>();
        var current = transform;
        while (current != null)
        {
            names.Push(current.name);
            current = current.parent;
        }

        return string.Join("/", names);
    }

    private bool UpdateWorldPaintProxy(bool forceLog, string context = "explicit rebuild")
    {
        var source = _worldSourceRenderer ?? FindBestSuitRenderer(StartOfRound.Instance?.localPlayerController);
        if (source == null || _worldPaintCollider == null || _worldPaintMesh == null || _worldPaintProxyObject == null || _worldAvatarMeshFilter == null || _worldAvatarRenderer == null)
        {
            return false;
        }

        var previousEnabled = source.enabled;
        try
        {
            _worldSourceRenderer = source;
            _worldPaintMesh.Clear();
            source.enabled = true;
            source.BakeMesh(_worldPaintMesh, true);
            source.enabled = false;
            if (_worldPaintMesh.vertexCount == 0)
            {
                if (forceLog)
                {
                    DrawableSuitsDiagnostics.Warn("World paint proxy BakeMesh produced zero vertices.");
                }
                return false;
            }

            _worldPaintMesh.RecalculateBounds();
            _lastWorldProxyMeshSummary = $"mode=Full; vertices={_worldPaintMesh.vertexCount}; subMeshes={_worldPaintMesh.subMeshCount}; bounds={_worldPaintMesh.bounds}";
            if (_worldPaintProxyObject.transform.parent != source.transform)
            {
                _worldPaintProxyObject.transform.SetParent(source.transform, false);
            }
            _worldPaintProxyObject.transform.localPosition = Vector3.zero;
            _worldPaintProxyObject.transform.localRotation = Quaternion.identity;
            _worldPaintProxyObject.transform.localScale = Vector3.one;
            _worldPaintProxyObject.layer = _worldPaintLayer;
            _worldAvatarMeshFilter.sharedMesh = _worldPaintMesh;
            _worldAvatarRenderer.sharedMaterials = BuildWorldProxyMaterials(source, forceLog);
            _worldPaintCollider.sharedMesh = null;
            _worldPaintCollider.sharedMesh = _worldPaintMesh;
            source.enabled = false;
            if (forceLog)
            {
                DrawableSuitsDiagnostics.Info($"WorldAvatarProxyRebaked: context={context}; mesh={_lastWorldProxyMeshSummary}; renderer={DescribeRendererCandidate(source, "source")}; vertices={_worldPaintMesh.vertexCount}; subMeshes={_worldPaintMesh.subMeshCount}; bounds={_worldPaintMesh.bounds}; proxyPos={_worldPaintProxyObject.transform.position}; proxyRot={_worldPaintProxyObject.transform.rotation.eulerAngles}; proxyScale={_worldPaintProxyObject.transform.localScale}; layer={_worldPaintLayer}; rendererEnabled={_worldAvatarRenderer.enabled}; proxyMaterials=[{DescribeMaterials(_worldAvatarRenderer.sharedMaterials)}]; collider={_worldPaintCollider != null}");
                DrawableSuitsDiagnostics.Info($"WorldAvatarProxyPoseFrozen: context={context}; source={DescribeRendererCandidate(source, "source")}; vertices={_worldPaintMesh.vertexCount}; bounds={_worldPaintMesh.bounds}; collider={_worldPaintCollider != null}; note=proxy mesh will not rebake during normal editor updates");
                LogVisibleEditorCameraRenderers(source);
            }
            return true;
        }
        catch (Exception ex)
        {
            DrawableSuitsDiagnostics.Exception("World paint proxy update failed", ex);
            return false;
        }
        finally
        {
            if (source != null)
            {
                source.enabled = DrawableSuitsPlugin.IsEditorOpen ? false : previousEnabled;
            }
        }
    }

    private Material[] BuildWorldProxyMaterials(SkinnedMeshRenderer source, bool forceLog = false)
    {
        var runtimeMaterial = DrawableSuitsPlugin.Registry.GetRuntimeMaterial(_selectedSuitId) ?? source?.sharedMaterial;
        var sourceMaterials = source?.sharedMaterials;
        var subMeshCount = Mathf.Max(1, _worldPaintMesh != null ? _worldPaintMesh.subMeshCount : (sourceMaterials?.Length ?? 1));
        var result = new Material[subMeshCount];
        var logParts = new string[subMeshCount];
        for (var i = 0; i < result.Length; i++)
        {
            var sourceMaterial = sourceMaterials != null && sourceMaterials.Length > 0
                ? sourceMaterials[Mathf.Min(i, sourceMaterials.Length - 1)]
                : null;
            result[i] = MaterialLooksFirstPersonOverlay(sourceMaterial) ? EnsureWorldHiddenSubmeshMaterial() : runtimeMaterial;
            logParts[i] = $"{i}:{sourceMaterial?.name ?? "null"}->{result[i]?.name ?? "null"}:hidden={ReferenceEquals(result[i], _worldHiddenSubmeshMaterial)}";
        }

        var logKey = $"{_selectedSuitId}:{source?.name ?? "null"}:{string.Join("|", logParts)}";
        if (forceLog
            || !string.Equals(logKey, _lastWorldProxyMaterialLogKey, StringComparison.Ordinal)
            || Time.unscaledTime - _lastWorldProxyMaterialLogTime > 10f)
        {
            _lastWorldProxyMaterialLogKey = logKey;
            _lastWorldProxyMaterialLogTime = Time.unscaledTime;
            DrawableSuitsDiagnostics.Info($"World proxy materials assigned. selectedSuitId={_selectedSuitId}; source={source?.name ?? "null"}; runtime={runtimeMaterial?.name ?? "null"}; slots=[{string.Join(", ", logParts)}]");
        }

        return result;
    }

    private Material EnsureWorldHiddenSubmeshMaterial()
    {
        if (_worldHiddenSubmeshMaterial != null)
        {
            return _worldHiddenSubmeshMaterial;
        }

        var shader = Shader.Find("Unlit/Transparent") ?? Shader.Find("UI/Default") ?? Shader.Find("Standard");
        _worldHiddenSubmeshMaterial = shader != null
            ? new Material(shader) { name = "DrawableSuitsHiddenProxySubmesh" }
            : new Material(Shader.Find("Diffuse")) { name = "DrawableSuitsHiddenProxySubmesh" };
        _worldHiddenSubmeshMaterial.color = new Color(0f, 0f, 0f, 0f);
        if (_worldHiddenSubmeshMaterial.HasProperty("_Color"))
        {
            _worldHiddenSubmeshMaterial.SetColor("_Color", new Color(0f, 0f, 0f, 0f));
        }
        if (_worldHiddenSubmeshMaterial.HasProperty("_Mode"))
        {
            _worldHiddenSubmeshMaterial.SetFloat("_Mode", 3f);
        }
        if (_worldHiddenSubmeshMaterial.HasProperty("_SrcBlend"))
        {
            _worldHiddenSubmeshMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        }
        if (_worldHiddenSubmeshMaterial.HasProperty("_DstBlend"))
        {
            _worldHiddenSubmeshMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        }
        if (_worldHiddenSubmeshMaterial.HasProperty("_ZWrite"))
        {
            _worldHiddenSubmeshMaterial.SetInt("_ZWrite", 0);
        }
        _worldHiddenSubmeshMaterial.renderQueue = 3000;
        return _worldHiddenSubmeshMaterial;
    }

    private static bool MaterialLooksFirstPersonOverlay(Material material)
    {
        if (material == null)
        {
            return false;
        }

        var name = material.name ?? string.Empty;
        return name.IndexOf("helmet", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("visor", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("glass", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("arms", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("hand", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("view", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string DescribeMaterials(Material[] materials)
    {
        if (materials == null || materials.Length == 0)
        {
            return "none";
        }

        var names = new string[materials.Length];
        for (var i = 0; i < materials.Length; i++)
        {
            names[i] = materials[i]?.name ?? "null";
        }

        return string.Join(", ", names);
    }

    private void LogVisibleEditorCameraRenderers(Renderer source)
    {
        if (_worldEditorCamera == null)
        {
            return;
        }

        var player = StartOfRound.Instance?.localPlayerController;
        var center = player != null ? player.transform.position : _worldEditorCamera.transform.position;
        var renderers = Resources.FindObjectsOfTypeAll<Renderer>();
        var logged = 0;
        for (var i = 0; i < renderers.Length && logged < 40; i++)
        {
            var renderer = renderers[i];
            if (renderer == null
                || !renderer.enabled
                || !renderer.gameObject.activeInHierarchy
                || !renderer.gameObject.scene.IsValid()
                || Vector3.Distance(renderer.bounds.center, center) > 5f)
            {
                continue;
            }

            var maskIncludes = (_worldEditorCamera.cullingMask & (1 << renderer.gameObject.layer)) != 0;
            if (!maskIncludes && renderer.gameObject.layer != _worldPaintLayer)
            {
                continue;
            }

            logged++;
            DrawableSuitsDiagnostics.Info($"World editor visible renderer candidate. renderer={DescribeRendererState(renderer)}; path={GetTransformPath(renderer.transform)}; source={ReferenceEquals(renderer, source)}; proxy={renderer.gameObject.name.IndexOf("DrawableSuits", StringComparison.OrdinalIgnoreCase) >= 0}; distance={Vector3.Distance(renderer.bounds.center, center):0.##}; material={renderer.sharedMaterial?.name ?? "null"}");
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
        _worldEditorCamera.enabled = _isOpen && IsWorldThirdPersonMode && _worldPreviewReady;
        if (forceLog)
        {
            DrawableSuitsDiagnostics.Info($"WorldEditorCamera updated. pos={position}; target={target}; yaw={_worldCameraYaw:0.##}; pitch={_worldCameraPitch:0.##}; distance={_worldCameraDistance:0.##}; depth={_worldEditorCamera.depth}; mask={_worldEditorCamera.cullingMask}");
        }
    }

    private bool TryGetWorldPaintHit(out RaycastHit hit)
    {
        hit = default;
        if (!IsWorldThirdPersonMode || _worldEditorCamera == null || _worldPaintCollider == null)
        {
            return false;
        }

        if (IsCursorOverEditorPanel())
        {
            return false;
        }

        // The editor avatar proxy is baked once during setup and then kept static.
        // Do not rebake here; idle breathing animation would move the paint target.

        var ray = _worldEditorCamera.ScreenPointToRay(_cursor);
        if (!Physics.Raycast(ray, out hit, 25f, 1 << _worldPaintLayer, QueryTriggerInteraction.Ignore))
        {
            return false;
        }

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

        // The dynamic UGUI cursor is the authoritative paint/erase preview now.
        // Keep the old world-space sphere hidden so it cannot look like an extra brush blob.
        _worldBrushMarker.SetActive(false);
    }

    private void DestroyWorldThirdPersonPreview(bool restoreRenderers)
    {
        DestroyDecalPlacementPreviewResources();
        InvalidateMirrorSurfaceMap("destroy world third-person preview");
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
        if (_worldHiddenSubmeshMaterial != null)
        {
            Destroy(_worldHiddenSubmeshMaterial);
            _worldHiddenSubmeshMaterial = null;
        }

        _worldEditorCamera = null;
        _worldPaintCollider = null;
        _worldAvatarMeshFilter = null;
        _worldAvatarRenderer = null;
        _worldSourceRenderer = null;
        _worldSourceRendererSummary = "none";
        _worldPreviewReady = false;
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
        _usingTexturePreview = true;
        _previewMode = texture != null ? "TextureFallback" : "TextureFallbackNoEditableTexture";
        _canPaint = texture != null;
        RefreshTexturePanelPreview(context, forceLog);
    }

    private bool RefreshTexturePanelPreview(string context, bool forceLog)
    {
        var texture = _selectedSuitId >= 0 ? DrawableSuitsPlugin.Registry.GetEditableTexture(_selectedSuitId) : null;
        var assignedTexture = texture != null ? (Texture)texture : EnsureCheckerTexture();
        var visible = texture != null || _usingTexturePreview || _uvFallbackMode;
        SetUvFallbackVisible(visible);

        if (_previewImage != null)
        {
            _previewImage.texture = assignedTexture;
            if (texture != null)
            {
                EnsureUvPanelViewForTexture(texture, context);
            }
            ApplyUvPanelViewToPreviewImage(texture);
            _previewImage.color = Color.white;
            _previewImage.raycastTarget = true;
        }

        var panelMode = IsWorldThirdPersonMode ? "TexturePanel" : _previewMode;
        var uvView = GetUvPanelViewRect();
        var assignment = $"{panelMode}; visible={visible}; assigned={assignedTexture?.name ?? "null"}; editable={DescribeEditableTexture()}; rawImage={DescribePreviewImageTexture()}; uvZoom={_uvPanelZoom:0.###}; uvCenter={_uvPanelCenter}; uvRect={uvView}";
        if (forceLog || !string.Equals(_lastPreviewAssignmentLog, assignment, StringComparison.Ordinal))
        {
            DrawableSuitsDiagnostics.Info($"TexturePanel[{context}]: {assignment}; viewport={(_previewViewportRect != null ? _previewViewportRect.rect.ToString() : "null")}; siblingIndex={(_previewViewportRect != null ? _previewViewportRect.GetSiblingIndex().ToString() : "null")}; anchoredPosition={(_previewViewportRect != null ? _previewViewportRect.anchoredPosition.ToString() : "null")}");
            _lastPreviewAssignmentLog = assignment;
        }

        return texture != null;
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
        DrawableSuitsDiagnostics.Info($"UiInputDiagnostics: currentEventSystem={EventSystem.current?.name ?? "null"}; activeModule={activeModule?.GetType().Name ?? "null"}; selected={EventSystem.current?.currentSelectedGameObject?.name ?? "null"}; pointerSource={_pointerSource}; pointer={pointer}; usingMousePointer={usingMousePointer}; mouseAvailable={_mousePositionAvailable}; mousePosition={_lastMousePosition}; gamepadStick={_lastGamepadStick}; virtualCursor={_cursor}; cursorVisible={Cursor.visible}; cursorLock={Cursor.lockState}; canvasScale={_editorCanvasObject?.GetComponent<Canvas>()?.scaleFactor.ToString("0.###") ?? "null"}; raycastHits=[{hitNames}]; overPanel={IsCursorOverEditorPanel()}; overPreview={IsCursorOverPreviewViewport()}; previewUv={previewUvSummary}; lastPreviewUv={(_lastPreviewUvAvailable ? _lastPreviewUv.ToString() : "none")}; previewMode={_previewMode}; uvPanelZoom={_uvPanelZoom:0.###}; uvPanelRect={GetUvPanelViewRect()}; editableTexture={DescribeEditableTexture()}; previewImageTexture={DescribePreviewImageTexture()}; previewRect={(_previewViewportRect != null ? _previewViewportRect.rect.ToString() : "null")}; previewCamera={DrawableSuitsPlugin.DescribeUnityObject(_previewCamera)}; previewCameraEnabled={_previewCamera?.enabled.ToString() ?? "null"}; previewLayer={_previewLayer}; renderTexture={_previewTexture?.width.ToString() ?? "null"}x{_previewTexture?.height.ToString() ?? "null"}");
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

    private sealed class DrawableCanvasCursorGraphic : Graphic
    {
        private const int CircleSegments = 64;
        private CursorVisualMode _mode = CursorVisualMode.Dot;
        private Color _cursorColor = Color.white;
        private float _diameter = DotCursorBackSize;

        public CursorVisualMode Mode => _mode;

        public float Diameter => _diameter;

        public void SetVisual(CursorVisualMode mode, Color cursorColor, float diameter)
        {
            diameter = Mathf.Max(1f, diameter);
            if (_mode == mode
                && Mathf.Abs(_diameter - diameter) < 0.05f
                && Mathf.Abs(_cursorColor.r - cursorColor.r) < 0.003f
                && Mathf.Abs(_cursorColor.g - cursorColor.g) < 0.003f
                && Mathf.Abs(_cursorColor.b - cursorColor.b) < 0.003f
                && Mathf.Abs(_cursorColor.a - cursorColor.a) < 0.003f)
            {
                return;
            }

            _mode = mode;
            _cursorColor = cursorColor;
            _diameter = diameter;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var rect = rectTransform.rect;
            var center = rect.center;
            if (_mode == CursorVisualMode.BrushRing)
            {
                var radius = Mathf.Min(rect.width, rect.height) * 0.5f - 5f;
                radius = Mathf.Max(5f, Mathf.Min(radius, _diameter * 0.5f));
                var backingThickness = Mathf.Clamp(_diameter * 0.14f, 4f, 10f);
                var frontThickness = Mathf.Clamp(_diameter * 0.07f, 2f, 6f);
                AddRing(vh, center, radius, backingThickness, new Color32(0, 0, 0, 245));
                AddRing(vh, center, radius, frontThickness, ToColor32(_cursorColor));
                return;
            }
            if (_mode == CursorVisualMode.BrushSquare || _mode == CursorVisualMode.BrushPixel)
            {
                var size = _mode == CursorVisualMode.BrushPixel
                    ? Mathf.Min(rect.width, rect.height) * 0.72f
                    : Mathf.Max(6f, Mathf.Min(Mathf.Min(rect.width, rect.height) - 10f, _diameter));
                var backingThickness = _mode == CursorVisualMode.BrushPixel ? 5f : Mathf.Clamp(_diameter * 0.13f, 4f, 10f);
                var frontThickness = _mode == CursorVisualMode.BrushPixel ? 2f : Mathf.Clamp(_diameter * 0.065f, 2f, 6f);
                AddSquareRing(vh, center, size, backingThickness, new Color32(0, 0, 0, 245));
                AddSquareRing(vh, center, size, frontThickness, ToColor32(_cursorColor));
                return;
            }

            var outerRadius = Mathf.Min(rect.width, rect.height) * 0.5f;
            var innerRadius = outerRadius * 0.58f;
            AddDisc(vh, center, outerRadius, new Color32(0, 0, 0, 235));
            AddDisc(vh, center, innerRadius, new Color32(255, 255, 255, 255));
        }

        private static void AddDisc(VertexHelper vh, Vector2 center, float radius, Color32 color)
        {
            var centerIndex = vh.currentVertCount;
            AddVertex(vh, center, color);
            for (var segment = 0; segment <= CircleSegments; segment++)
            {
                var angle = segment / (float)CircleSegments * Mathf.PI * 2f;
                AddVertex(vh, center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, color);
            }

            for (var segment = 0; segment < CircleSegments; segment++)
            {
                vh.AddTriangle(centerIndex, centerIndex + segment + 1, centerIndex + segment + 2);
            }
        }

        private static void AddRing(VertexHelper vh, Vector2 center, float radius, float thickness, Color32 color)
        {
            var innerRadius = Mathf.Max(0f, radius - thickness * 0.5f);
            var outerRadius = radius + thickness * 0.5f;
            var startIndex = vh.currentVertCount;
            for (var segment = 0; segment <= CircleSegments; segment++)
            {
                var angle = segment / (float)CircleSegments * Mathf.PI * 2f;
                var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                AddVertex(vh, center + direction * outerRadius, color);
                AddVertex(vh, center + direction * innerRadius, color);
            }

            for (var segment = 0; segment < CircleSegments; segment++)
            {
                var outer0 = startIndex + segment * 2;
                var inner0 = outer0 + 1;
                var outer1 = outer0 + 2;
                var inner1 = outer0 + 3;
                vh.AddTriangle(outer0, outer1, inner1);
                vh.AddTriangle(outer0, inner1, inner0);
            }
        }

        private static void AddSquareRing(VertexHelper vh, Vector2 center, float size, float thickness, Color32 color)
        {
            var halfOuter = Mathf.Max(1f, size * 0.5f + thickness * 0.5f);
            var halfInner = Mathf.Max(0f, size * 0.5f - thickness * 0.5f);
            var start = vh.currentVertCount;
            AddVertex(vh, center + new Vector2(-halfOuter, -halfOuter), color);
            AddVertex(vh, center + new Vector2(halfOuter, -halfOuter), color);
            AddVertex(vh, center + new Vector2(halfOuter, halfOuter), color);
            AddVertex(vh, center + new Vector2(-halfOuter, halfOuter), color);
            AddVertex(vh, center + new Vector2(-halfInner, -halfInner), color);
            AddVertex(vh, center + new Vector2(halfInner, -halfInner), color);
            AddVertex(vh, center + new Vector2(halfInner, halfInner), color);
            AddVertex(vh, center + new Vector2(-halfInner, halfInner), color);
            vh.AddTriangle(start, start + 1, start + 5);
            vh.AddTriangle(start, start + 5, start + 4);
            vh.AddTriangle(start + 1, start + 2, start + 6);
            vh.AddTriangle(start + 1, start + 6, start + 5);
            vh.AddTriangle(start + 2, start + 3, start + 7);
            vh.AddTriangle(start + 2, start + 7, start + 6);
            vh.AddTriangle(start + 3, start, start + 4);
            vh.AddTriangle(start + 3, start + 4, start + 7);
        }

        private static void AddVertex(VertexHelper vh, Vector2 position, Color32 color)
        {
            var vertex = UIVertex.simpleVert;
            vertex.color = color;
            vertex.position = position;
            vh.AddVert(vertex);
        }

        private static Color32 ToColor32(Color color)
        {
            return new Color32(
                (byte)Mathf.RoundToInt(Mathf.Clamp01(color.r) * 255f),
                (byte)Mathf.RoundToInt(Mathf.Clamp01(color.g) * 255f),
                (byte)Mathf.RoundToInt(Mathf.Clamp01(color.b) * 255f),
                (byte)Mathf.RoundToInt(Mathf.Clamp01(color.a) * 255f));
        }
    }

    private static bool WasGamepadPressed(Func<Gamepad, ButtonControl> accessor)
    {
        var gamepad = Gamepad.current;
        return gamepad != null && accessor(gamepad).wasPressedThisFrame;
    }
}
