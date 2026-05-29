using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
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

    private enum SuitPart
    {
        All,
        Helmet,
        Torso,
        LeftArm,
        RightArm,
        LeftLeg,
        RightLeg,
        Other
    }

    private sealed class PartTriangleRecord
    {
        public int Submesh;
        public int A;
        public int B;
        public int C;
        public SuitPart Part;
        public SuitPart GeometryPart;
        public SuitPart BonePart;
        public bool UsedBones;
        public Vector3 Center;
        public float Height;
        public float Side;
        public float XOffset;
    }

    private sealed class VanillaPresetCorrectionStats
    {
        public int HelmetToTorso;
        public int OtherToTorso;
        public int ArmStrapToTorso;
        public int OtherToArm;

        public override string ToString()
        {
            return $"helmetToTorso={HelmetToTorso},otherToTorso={OtherToTorso},armStrapToTorso={ArmStrapToTorso},otherToArm={OtherToArm}";
        }
    }

    [Serializable]
    private sealed class PartPresetFile
    {
        public string name = string.Empty;
        public string rendererNameContains = string.Empty;
        public string rendererPathContains = string.Empty;
        public string materialNameContains = string.Empty;
        public int vertexCount = 0;
        public int triangleCount = 0;
        public int textureWidth = 0;
        public int textureHeight = 0;
        public PartPresetAssignment[] parts = Array.Empty<PartPresetAssignment>();
    }

    [Serializable]
    private sealed class PartPresetAssignment
    {
        public string part = string.Empty;
        public int[] triangles = Array.Empty<int>();
    }

    private readonly struct TriangleRegion
    {
        public TriangleRegion(Vector3 center, float height, float minHeight, float maxHeight, float xOffset, float side)
        {
            Center = center;
            Height = height;
            MinHeight = minHeight;
            MaxHeight = maxHeight;
            XOffset = xOffset;
            Side = side;
        }

        public Vector3 Center { get; }
        public float Height { get; }
        public float MinHeight { get; }
        public float MaxHeight { get; }
        public float XOffset { get; }
        public float Side { get; }
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
    private SuitPart _selectedPart = SuitPart.All;
    private Color _brushColor = Color.red;
    private float _brushSize = 16f;
    private float _brushOpacity = 1f;
    private float _decalSize = 128f;
    private float _decalRotation;
    private bool _strokeActive;
    private bool _decalStampArmed = true;
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

    private static readonly SuitPart[] IsolatedSuitParts =
    {
        SuitPart.Helmet,
        SuitPart.Torso,
        SuitPart.LeftArm,
        SuitPart.RightArm,
        SuitPart.LeftLeg,
        SuitPart.RightLeg,
        SuitPart.Other
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
    private bool _lastWorldRaycastHit;
    private Vector3 _lastWorldHitPoint;
    private Vector3 _lastWorldHitNormal;
    private Texture2D _decalPreviewTexture;
    private RawImage _uvDecalPreviewImage;
    private RectTransform _uvDecalPreviewRect;
    private bool _worldDecalPreviewApplied;
    private bool _decalPreviewVisible;
    private bool _suppressDecalPreviewUntilRelease;
    private int _decalPreviewSerial;
    private string _lastDecalPreviewKey = string.Empty;
    private string _lastDecalPreviewLogKey = string.Empty;
    private float _lastDecalPreviewLogTime;
    private int _designListPage;
    private int _decalListPage;
    private readonly List<RendererRestoreState> _rendererRestoreStates = new();
    private Texture2D _loadedDecal;
    private readonly Dictionary<SuitPart, int[][]> _partTrianglesBySubmesh = new();
    private readonly Dictionary<SuitPart, bool[]> _partUvMasks = new();
    private readonly Dictionary<SuitPart, int> _partTriangleCounts = new();
    private readonly Dictionary<SuitPart, int> _partMaskPixelCounts = new();
    private readonly Dictionary<SuitPart, Button> _partButtons = new();
    private Texture2D _partFilteredPreviewTexture;
    private bool _partDataReady;
    private bool _partUvOverlapDetected;
    private string _lastWorldProxyMeshSummary = "none";
    private int _partDataTextureWidth;
    private int _partDataTextureHeight;
    private int _partDataSourceId;
    private string _partClassifierSource = "none";

    private GameObject _editorCanvasObject;
    private RectTransform _canvasRect;
    private RectTransform _panelRect;
    private RectTransform _previewViewportRect;
    private RectTransform _brushIndicator;
    private Image _brushIndicatorImage;
    private RectTransform _cursorMarker;
    private RectTransform _designListContent;
    private RectTransform _decalListContent;
    private Text _designEmptyLabel;
    private Text _decalEmptyLabel;
    private Text _designPageLabel;
    private Text _decalPageLabel;
    private Button _designPrevPageButton;
    private Button _designNextPageButton;
    private Button _decalPrevPageButton;
    private Button _decalNextPageButton;
    private readonly List<AnchoredListRow> _designRows = new();
    private readonly List<AnchoredListRow> _decalRows = new();
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
    private DrawableColorPickerControl _colorPicker;
    private InputField _colorHexInput;
    private DrawableSliderControl _decalSizeSlider;
    private DrawableSliderControl _decalRotationSlider;
    private Image _colorSwatch;
    private Button _paintButton;
    private Button _eraseButton;
    private Button _decalButton;
    private Button _otherPartButton;
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

    private struct WorldCameraState
    {
        internal bool Valid;
        internal float Yaw;
        internal float Pitch;
        internal float Distance;
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

        ClearPartIsolationData();
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
        _isOpen = true;
        _selectedPart = SuitPart.All;
        ClearPartIsolationData();
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
        if (_editorCanvasObject != null)
        {
            _editorCanvasObject.SetActive(false);
        }

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
        if (_selectedSuitId < 0)
        {
            _selectedSuitId = localSuitId >= 0 ? localSuitId : FirstKnownSuitId();
        }
        else if (!suitIds.Contains(_selectedSuitId))
        {
            _selectedSuitId = localSuitId >= 0 ? localSuitId : FirstKnownSuitId();
        }
        if (priorSelectedSuitId >= 0 && priorSelectedSuitId != _selectedSuitId)
        {
            _selectedPart = SuitPart.All;
            ClearPartIsolationData();
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

        if (missing.Count > 0)
        {
            return "Diagnostics: " + string.Join("; ", missing);
        }

        var overlapWarning = _partUvOverlapDetected && _selectedPart != SuitPart.All ? " Shared UV regions detected." : string.Empty;
        return $"Ready. Preview: {_previewMode}. Editing: {PartDisplayName(_selectedPart)}.{overlapWarning}";
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
        _panelRect.sizeDelta = new Vector2(620f, 1010f);

        var panelImage = panel.GetComponent<Image>();
        panelImage.color = new Color(0.025f, 0.03f, 0.035f, 0.88f);

        const float leftX = 18f;
        const float leftW = 274f;
        const float rightX = 314f;
        const float rightW = 286f;

        CreateAnchoredText(panel.transform, "Title", $"{PluginInfo.Name} {PluginInfo.Version}", 24, FontStyle.Bold, TextAnchor.MiddleLeft, new Rect(leftX, 12f, 360f, 34f), new Color(1f, 0.62f, 0.25f, 1f));
        CreateAnchoredButton(panel.transform, "Close", new Rect(512f, 14f, 88f, 34f), CloseEditor);
        _suitLabel = CreateAnchoredText(panel.transform, "SuitLabel", string.Empty, 18, FontStyle.Normal, TextAnchor.MiddleLeft, new Rect(leftX, 54f, 420f, 28f), Color.white);
        _statusLabel = CreateAnchoredText(panel.transform, "StatusLabel", string.Empty, 15, FontStyle.Normal, TextAnchor.UpperLeft, new Rect(leftX, 86f, leftW, 46f), new Color(1f, 0.58f, 0.28f, 1f));
        _statusLabel.color = new Color(1f, 0.58f, 0.28f, 1f);
        _diagnosticsLabel = CreateAnchoredText(panel.transform, "DiagnosticsLabel", string.Empty, 12, FontStyle.Normal, TextAnchor.UpperLeft, new Rect(leftX, 138f, leftW, 128f), new Color(0.78f, 0.86f, 1f, 1f));
        _diagnosticsLabel.color = new Color(0.78f, 0.86f, 1f, 1f);

        CreateAnchoredButton(panel.transform, "Previous", new Rect(leftX, 282f, 82f, 34f), () => SelectAdjacentSuit(-1));
        CreateAnchoredButton(panel.transform, "Use Current", new Rect(leftX + 90f, 282f, 112f, 34f), () => SelectSuit(DrawableSuitsPlugin.Registry.GetLocalSuitId()));
        CreateAnchoredButton(panel.transform, "Next", new Rect(leftX + 210f, 282f, 72f, 34f), () => SelectAdjacentSuit(1));

        CreateAnchoredText(panel.transform, "PartHeader", "Part", 16, FontStyle.Bold, TextAnchor.MiddleLeft, new Rect(leftX, 326f, leftW, 24f), Color.white);
        _partButtons[SuitPart.All] = CreateAnchoredButton(panel.transform, "All", new Rect(leftX, 354f, 62f, 30f), () => SelectPart(SuitPart.All));
        _partButtons[SuitPart.Helmet] = CreateAnchoredButton(panel.transform, "Helmet", new Rect(leftX + 68f, 354f, 66f, 30f), () => SelectPart(SuitPart.Helmet));
        _partButtons[SuitPart.Torso] = CreateAnchoredButton(panel.transform, "Torso", new Rect(leftX + 140f, 354f, 62f, 30f), () => SelectPart(SuitPart.Torso));
        _otherPartButton = CreateAnchoredButton(panel.transform, "Other", new Rect(leftX + 208f, 354f, 66f, 30f), () => SelectPart(SuitPart.Other));
        _partButtons[SuitPart.Other] = _otherPartButton;
        _partButtons[SuitPart.LeftArm] = CreateAnchoredButton(panel.transform, "L Arm", new Rect(leftX, 390f, 62f, 30f), () => SelectPart(SuitPart.LeftArm));
        _partButtons[SuitPart.RightArm] = CreateAnchoredButton(panel.transform, "R Arm", new Rect(leftX + 68f, 390f, 66f, 30f), () => SelectPart(SuitPart.RightArm));
        _partButtons[SuitPart.LeftLeg] = CreateAnchoredButton(panel.transform, "L Leg", new Rect(leftX + 140f, 390f, 62f, 30f), () => SelectPart(SuitPart.LeftLeg));
        _partButtons[SuitPart.RightLeg] = CreateAnchoredButton(panel.transform, "R Leg", new Rect(leftX + 208f, 390f, 66f, 30f), () => SelectPart(SuitPart.RightLeg));

        CreateAnchoredText(panel.transform, "ToolHeader", "Tool", 16, FontStyle.Bold, TextAnchor.MiddleLeft, new Rect(leftX, 426f, leftW, 24f), Color.white);
        _paintButton = CreateAnchoredButton(panel.transform, "Paint", new Rect(leftX, 454f, 84f, 34f), () => SetTool(EditorTool.Paint));
        _eraseButton = CreateAnchoredButton(panel.transform, "Erase", new Rect(leftX + 92f, 454f, 84f, 34f), () => SetTool(EditorTool.Erase));
        _decalButton = CreateAnchoredButton(panel.transform, "Decal", new Rect(leftX + 184f, 454f, 84f, 34f), () => SetTool(EditorTool.Decal));

        CreateAnchoredText(panel.transform, "BrushHeader", "Brush", 16, FontStyle.Bold, TextAnchor.MiddleLeft, new Rect(leftX, 506f, leftW, 24f), Color.white);
        _brushSizeLabel = CreateAnchoredText(panel.transform, "BrushSizeLabel", string.Empty, 14, FontStyle.Normal, TextAnchor.MiddleLeft, new Rect(leftX, 536f, 94f, 24f), Color.white);
        _brushSizeSlider = CreateAnchoredSlider(panel.transform, "BrushSize", 1f, 96f, _brushSize, new Rect(leftX + 100f, 538f, 174f, 24f), value => _brushSize = value);
        _brushOpacityLabel = CreateAnchoredText(panel.transform, "BrushOpacityLabel", string.Empty, 14, FontStyle.Normal, TextAnchor.MiddleLeft, new Rect(leftX, 570f, 94f, 24f), Color.white);
        _brushOpacitySlider = CreateAnchoredSlider(panel.transform, "BrushOpacity", 0.05f, 1f, _brushOpacity, new Rect(leftX + 100f, 572f, 174f, 24f), value => _brushOpacity = value);

        CreateAnchoredText(panel.transform, "ColorHeader", "Color", 16, FontStyle.Bold, TextAnchor.MiddleLeft, new Rect(leftX, 610f, leftW, 24f), Color.white);
        _colorPicker = CreateAnchoredColorPicker(panel.transform, new Rect(leftX, 634f, leftW, 104f), _brushColor, color =>
        {
            _brushColor = color;
            UpdateColorUi();
        }, out _colorSwatch, out _colorHexInput);
        _colorHexInput.onValueChanged.AddListener(PreviewHexInput);
        _colorHexInput.onEndEdit.AddListener(ApplyHexInput);

        _uvFallbackButton = CreateAnchoredButton(panel.transform, "Use UV Fallback", new Rect(rightX, 54f, 150f, 34f), ToggleUvFallback);
        CreateAnchoredText(panel.transform, "WorldHelp", "Third-person mode: aim at the visible suit and hold left mouse or right trigger to paint. Right mouse/right stick or bumpers orbit. Wheel or D-pad up/down zooms; Ctrl+wheel changes brush size.", 13, FontStyle.Normal, TextAnchor.UpperLeft, new Rect(rightX, 96f, rightW, 76f), new Color(0.86f, 0.9f, 0.94f, 1f));

        CreateAnchoredText(panel.transform, "DecalHeader", "Decal", 16, FontStyle.Bold, TextAnchor.MiddleLeft, new Rect(rightX, 188f, rightW, 24f), Color.white);
        _decalSizeLabel = CreateAnchoredText(panel.transform, "DecalSizeLabel", string.Empty, 14, FontStyle.Normal, TextAnchor.MiddleLeft, new Rect(rightX, 218f, 112f, 24f), Color.white);
        _decalSizeSlider = CreateAnchoredSlider(panel.transform, "DecalSize", 16f, 512f, _decalSize, new Rect(rightX + 120f, 220f, 160f, 24f), value => _decalSize = value);
        _decalRotationLabel = CreateAnchoredText(panel.transform, "DecalRotationLabel", string.Empty, 14, FontStyle.Normal, TextAnchor.MiddleLeft, new Rect(rightX, 252f, 112f, 24f), Color.white);
        _decalRotationSlider = CreateAnchoredSlider(panel.transform, "DecalRotation", -180f, 180f, _decalRotation, new Rect(rightX + 120f, 254f, 160f, 24f), value => _decalRotation = value);
        CreateAnchoredButton(panel.transform, "Refresh Decals", new Rect(rightX, 290f, 160f, 34f), ImportDecalFromDialog);
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
            InvalidateDecalPreview("reset");
            UpdateUiState();
        });

        _applyButton = CreateAnchoredButton(panel.transform, "Apply", new Rect(rightX, 610f, 84f, 34f), () => DrawableSuitsPlugin.Registry.ApplyEditedTexture(_selectedSuitId, _designName, true));
        _saveButton = CreateAnchoredButton(panel.transform, "Save", new Rect(rightX + 92f, 610f, 84f, 34f), SaveDesign);
        _loadButton = CreateAnchoredButton(panel.transform, "Load", new Rect(rightX + 184f, 610f, 84f, 34f), LoadSelectedDesign);

        CreateAnchoredText(panel.transform, "SavedDesignsHeader", "Saved Designs", 16, FontStyle.Bold, TextAnchor.MiddleLeft, new Rect(rightX, 664f, rightW, 24f), Color.white);
        _designListContent = CreateAnchoredScrollList(panel.transform, "DesignList", new Rect(rightX, 694f, rightW, 142f));

        var fallbackPreview = CreateUiObject("PreviewViewport", panel.transform, typeof(RectTransform), typeof(Image));
        _previewViewportRect = fallbackPreview.GetComponent<RectTransform>();
        SetAnchoredRect(_previewViewportRect, new Rect(leftX, 748f, 274f, 190f));
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

        _brushIndicator = CreateUiObject("BrushIndicator", fallbackPreview.transform, typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
        _brushIndicator.sizeDelta = new Vector2(16f, 16f);
        _brushIndicatorImage = _brushIndicator.GetComponent<Image>();
        _brushIndicatorImage.color = new Color(1f, 1f, 1f, 0.32f);
        _brushIndicatorImage.raycastTarget = false;
        _brushIndicator.gameObject.SetActive(false);
        fallbackPreview.SetActive(false);

        CreateAnchoredText(panel.transform, "ControllerHelp", "Controller: View/Back+Y open/close, left stick cursor, A clicks UI, RT paints only, right stick/bumpers orbit, D-pad up/down zooms, X undo, Start save.", 13, FontStyle.Normal, TextAnchor.UpperLeft, new Rect(leftX, 954f, 574f, 36f), Color.white);

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
        button.onClick.AddListener(() =>
        {
            onClick?.Invoke();
            ClearSelectedNormalButton();
        });

        var colors = button.colors;
        colors.normalColor = image.color;
        colors.highlightedColor = colors.normalColor;
        colors.pressedColor = new Color(0.34f, 0.36f, 0.38f, 1f);
        colors.selectedColor = colors.normalColor;
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

    private static void ClearSelectedNormalButton()
    {
        var eventSystem = EventSystem.current;
        var selected = eventSystem != null ? eventSystem.currentSelectedGameObject : null;
        if (selected != null && selected.GetComponent<InputField>() == null && selected.GetComponent<DrawableSliderControl>() == null && selected.GetComponent<DrawableColorPickerControl>() == null)
        {
            eventSystem.SetSelectedGameObject(null);
        }
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
        if (_colorPicker != null) _colorPicker.SetColor(_brushColor, false);
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
        UpdatePartButtons();
        UpdateLabels();
        UpdateColorUi();
    }

    private string BuildDiagnosticsSummary()
    {
        var camera = Camera.main;
        return string.Join("\n", new[]
        {
            $"Selected suit id: {_selectedSuitId}",
            $"Selected part: {PartDisplayName(_selectedPart)}",
            $"Part classifier: {_partClassifierSource}; UV overlap: {_partUvOverlapDetected}",
            $"Suit count: {_knownSuitCount}",
            $"Local player found: {_hasLocalPlayer}",
            $"Player model found: {_hasPlayerModel}",
            $"Main camera found: {_hasCamera} ({(camera != null ? camera.name : "null")})",
            $"Preview mode: {_previewMode}",
            $"Editable texture: {DescribeEditableTexture()}",
            $"Preview UI texture: {DescribePreviewImageTexture()}",
            $"UV fallback mode: {_uvFallbackMode}",
            $"World camera found: {_worldEditorCamera != null}",
            $"World avatar proxy found: {_worldAvatarRenderer != null}",
            $"World source renderer: {_worldSourceRendererSummary}",
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

    private void UpdateToolButtons()
    {
        SetToolButtonColor(_paintButton, _tool == EditorTool.Paint);
        SetToolButtonColor(_eraseButton, _tool == EditorTool.Erase);
        SetToolButtonColor(_decalButton, _tool == EditorTool.Decal);
    }

    private void UpdatePartButtons()
    {
        foreach (var entry in _partButtons)
        {
            var part = entry.Key;
            var button = entry.Value;
            if (button == null)
            {
                continue;
            }

            var hasTriangles = part == SuitPart.All || GetPartTriangleCount(part) > 0;
            if (part == SuitPart.Other)
            {
                button.gameObject.SetActive(hasTriangles);
            }

            button.interactable = hasTriangles;
            SetToolButtonColor(button, part == _selectedPart);
        }
    }

    private void SelectPart(SuitPart part)
    {
        if (part != SuitPart.All && GetPartTriangleCount(part) <= 0)
        {
            SetStatus($"{PartDisplayName(part)} is not available on this suit.", false);
            UpdateUiState();
            return;
        }

        _selectedPart = part;
        HideDecalPlacementPreview("part changed", false);
        if (IsWorldThirdPersonMode)
        {
            UpdateWorldPaintProxy(true);
        }
        else
        {
            UseTexturePreview("part changed", true);
        }

        var warning = string.Empty;
        if (part != SuitPart.All && GetPartMaskPixelCount(part) <= 0)
        {
            warning = " Visible geometry has no editable UV pixels.";
        }
        else if (_partUvOverlapDetected && part != SuitPart.All)
        {
            warning = " Shared UV pixels may also affect another part.";
        }
        SetStatus($"Editing: {PartDisplayName(part)}.{warning}", false);
        DrawableSuitsDiagnostics.Info($"Suit part selected. part={part}; triangles={GetPartTriangleCount(part)}; maskPixels={GetPartMaskPixelCount(part)}; classifier={_partClassifierSource}; overlap={_partUvOverlapDetected}; mode={_previewMode}");
        InvalidateDecalPreview("part changed");
        UpdateUiState();
    }

    private static string PartDisplayName(SuitPart part)
    {
        switch (part)
        {
            case SuitPart.LeftArm: return "Left Arm";
            case SuitPart.RightArm: return "Right Arm";
            case SuitPart.LeftLeg: return "Left Leg";
            case SuitPart.RightLeg: return "Right Leg";
            default: return part.ToString();
        }
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
            HideDecalPlacementPreview("decal tool missing decal", false);
            UpdateToolButtons();
            UpdateBrushIndicator();
            return;
        }

        _tool = tool;
        _decalStampArmed = true;
        _suppressDecalPreviewUntilRelease = false;
        if (tool == EditorTool.Decal)
        {
            InvalidateDecalPreview("decal tool selected");
        }
        else
        {
            HideDecalPlacementPreview("tool changed", false);
        }
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
                _selectedDesignIndex = index;
                RefreshListButtons();
                UpdateUiState();
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
            pageLabel = CreateAnchoredText(content, $"{listName}Page", string.Empty, 12, FontStyle.Normal, TextAnchor.MiddleCenter, new Rect(54f, content.rect.height - 32f, 64f, 26f), Color.white);
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
            image.color = new Color(0.95f, 0.42f, 0.16f, 1f);
        }

        var colors = button.colors;
        colors.normalColor = new Color(0.95f, 0.42f, 0.16f, 1f);
        colors.highlightedColor = new Color(1f, 0.54f, 0.24f, 1f);
        colors.selectedColor = colors.highlightedColor;
        button.colors = colors;
    }

    private static void ApplyNormalListButtonStyle(Button button)
    {
        if (button == null)
        {
            return;
        }

        var normal = new Color(0.14f, 0.15f, 0.16f, 0.98f);
        var image = button.GetComponent<Image>();
        if (image != null)
        {
            image.color = normal;
        }

        var colors = button.colors;
        colors.normalColor = normal;
        colors.highlightedColor = normal;
        colors.selectedColor = normal;
        colors.pressedColor = new Color(0.34f, 0.36f, 0.38f, 1f);
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

        // The old filled brush preview looked like a second cursor and changed with brush color/size.
        // Keep it hidden until a proper outline-only indicator is added.
        _brushIndicator.gameObject.SetActive(false);
    }

    private void UpdateDecalPlacementPreview()
    {
        if (!_isOpen || _tool != EditorTool.Decal || _loadedDecal == null || !_canPaint || _suppressDecalPreviewUntilRelease)
        {
            HideDecalPlacementPreview("not eligible", false);
            return;
        }

        var texture = DrawableSuitsPlugin.Registry.GetEditableTexture(_selectedSuitId);
        if (texture == null)
        {
            HideDecalPlacementPreview("no editable texture", false);
            return;
        }

        if (IsWorldThirdPersonMode)
        {
            if (IsCursorOverEditorPanel() || !TryGetWorldPaintHit(out var hit, false))
            {
                HideDecalPlacementPreview("world miss", false);
                return;
            }

            ShowWorldDecalPlacementPreview(texture, hit.textureCoord);
            return;
        }

        if (!TryGetTexturePreviewUv(_cursor, out var uv) || !IsCursorOverPreviewViewport())
        {
            HideDecalPlacementPreview("uv miss", false);
            return;
        }

        if (!IsUvTargetInSelectedPart(texture, uv))
        {
            HideDecalPlacementPreview("uv outside selected part", false);
            return;
        }

        ShowUvDecalPlacementPreview(texture, uv);
    }

    private void ShowWorldDecalPlacementPreview(Texture2D sourceTexture, Vector2 uv)
    {
        if (_worldAvatarRenderer == null || _worldSourceRenderer == null || sourceTexture == null || _loadedDecal == null)
        {
            HideDecalPlacementPreview("world dependencies missing", false);
            return;
        }

        if (!EnsureDecalPreviewTexture(sourceTexture))
        {
            HideDecalPlacementPreview("preview texture failed", true);
            return;
        }

        var key = BuildDecalPreviewKey("WorldThirdPerson", sourceTexture, uv);
        if (!string.Equals(key, _lastDecalPreviewKey, StringComparison.Ordinal))
        {
            _decalPreviewTexture.SetPixels32(sourceTexture.GetPixels32());
            CompositeDecal(_decalPreviewTexture, _loadedDecal, uv, _decalSize, _decalRotation, Mathf.Clamp01(_brushOpacity * 0.62f));
            _decalPreviewTexture.Apply(false, false);
            _lastDecalPreviewKey = key;
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
        if (_uvDecalPreviewRect != null)
        {
            _uvDecalPreviewRect.gameObject.SetActive(false);
        }

        SetDecalPreviewStatus();
        LogDecalPreviewUpdated("WorldThirdPerson", sourceTexture, uv, false);
    }

    private void ShowUvDecalPlacementPreview(Texture2D sourceTexture, Vector2 uv)
    {
        if (_uvDecalPreviewRect == null || _uvDecalPreviewImage == null || _previewViewportRect == null || sourceTexture == null || _loadedDecal == null)
        {
            HideDecalPlacementPreview("uv dependencies missing", false);
            return;
        }

        RestoreWorldDecalPreviewMaterial();
        if (_selectedPart != SuitPart.All && EnsureDecalPreviewTexture(sourceTexture))
        {
            _decalPreviewTexture.SetPixels32(sourceTexture.GetPixels32());
            CompositeDecal(_decalPreviewTexture, _loadedDecal, uv, _decalSize, _decalRotation, Mathf.Clamp01(_brushOpacity * 0.62f));
            _decalPreviewTexture.Apply(false, false);
            _previewImage.texture = BuildFilteredPartPreviewTexture(_decalPreviewTexture);
            _uvDecalPreviewRect.gameObject.SetActive(false);
            _decalPreviewVisible = true;
            _lastDecalPreviewKey = BuildDecalPreviewKey("TextureFallback", sourceTexture, uv);
            SetDecalPreviewStatus();
            LogDecalPreviewUpdated("TextureFallback", sourceTexture, uv, false);
            return;
        }

        var rect = _previewViewportRect.rect;
        var localX = Mathf.Lerp(rect.xMin, rect.xMax, uv.x);
        var localY = Mathf.Lerp(rect.yMin, rect.yMax, uv.y);
        var displayWidth = Mathf.Clamp(_decalSize / Mathf.Max(1f, sourceTexture.width) * rect.width, 4f, rect.width * 1.5f);
        var displayHeight = Mathf.Clamp(_decalSize / Mathf.Max(1f, sourceTexture.height) * rect.height, 4f, rect.height * 1.5f);

        _uvDecalPreviewImage.texture = _loadedDecal;
        _uvDecalPreviewImage.color = new Color(1f, 1f, 1f, 0.62f);
        _uvDecalPreviewImage.raycastTarget = false;
        _uvDecalPreviewRect.anchoredPosition = new Vector2(localX, localY);
        _uvDecalPreviewRect.sizeDelta = new Vector2(displayWidth, displayHeight);
        _uvDecalPreviewRect.localRotation = Quaternion.Euler(0f, 0f, _decalRotation);
        _uvDecalPreviewRect.gameObject.SetActive(true);

        _decalPreviewVisible = true;
        _lastDecalPreviewKey = BuildDecalPreviewKey("TextureFallback", sourceTexture, uv);
        SetDecalPreviewStatus();
        LogDecalPreviewUpdated("TextureFallback", sourceTexture, uv, false);
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
        var wasVisible = _decalPreviewVisible || _worldDecalPreviewApplied || (_uvDecalPreviewRect != null && _uvDecalPreviewRect.gameObject.activeSelf);
        if (_uvDecalPreviewRect != null)
        {
            _uvDecalPreviewRect.gameObject.SetActive(false);
        }

        RestoreWorldDecalPreviewMaterial();
        if (_usingTexturePreview && wasVisible)
        {
            UseTexturePreview("decal preview hidden", false);
        }
        _decalPreviewVisible = false;
        if (_statusMessage.StartsWith("Previewing decal", StringComparison.OrdinalIgnoreCase))
        {
            SetStatus(BuildReadinessStatus(), false);
        }

        if (wasVisible || forceLog)
        {
            LogDecalPreviewHidden(reason, forceLog);
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
    }

    private void InvalidateDecalPreview(string reason)
    {
        _decalPreviewSerial++;
        _lastDecalPreviewKey = string.Empty;
        if (_decalPreviewVisible)
        {
            LogDecalPreviewHidden($"invalidated: {reason}", false);
        }
    }

    private string BuildDecalPreviewKey(string mode, Texture2D sourceTexture, Vector2 uv)
    {
        var px = sourceTexture != null ? Mathf.RoundToInt(uv.x * (sourceTexture.width - 1)) : -1;
        var py = sourceTexture != null ? Mathf.RoundToInt(uv.y * (sourceTexture.height - 1)) : -1;
        return $"{_decalPreviewSerial}|{mode}|suit={_selectedSuitId}|pixel={px},{py}|size={Mathf.RoundToInt(_decalSize)}|rot={Mathf.RoundToInt(_decalRotation * 10f)}|opacity={Mathf.RoundToInt(_brushOpacity * 1000f)}|decal={CurrentDecalName()}|texture={sourceTexture?.width ?? 0}x{sourceTexture?.height ?? 0}";
    }

    private void SetDecalPreviewStatus()
    {
        if (!_statusMessage.StartsWith("Previewing decal", StringComparison.OrdinalIgnoreCase))
        {
            SetStatus("Previewing decal. Click/RT to stamp.", false);
        }
    }

    private void LogDecalPreviewUpdated(string mode, Texture2D texture, Vector2 uv, bool force)
    {
        if (texture == null)
        {
            return;
        }

        var px = Mathf.RoundToInt(uv.x * (texture.width - 1));
        var py = Mathf.RoundToInt(uv.y * (texture.height - 1));
        var key = $"updated|mode={mode}|pixel={px},{py}|size={Mathf.RoundToInt(_decalSize)}|rotation={Mathf.RoundToInt(_decalRotation)}|opacity={_brushOpacity:0.##}|decal={CurrentDecalName()}|preview={_decalPreviewTexture?.width ?? 0}x{_decalPreviewTexture?.height ?? 0}";
        if (!force && Time.unscaledTime - _lastDecalPreviewLogTime < 0.5f && string.Equals(key, _lastDecalPreviewLogKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastDecalPreviewLogTime = Time.unscaledTime;
        _lastDecalPreviewLogKey = key;
        DrawableSuitsDiagnostics.Info($"DecalPreviewUpdated: {key}; uv={uv}; pointerSource={_pointerSource}; cursor={_cursor}");
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

    private void LogDecalStampCommitted(string mode, Texture2D texture, Vector2 uv)
    {
        if (texture == null)
        {
            return;
        }

        var px = Mathf.RoundToInt(uv.x * (texture.width - 1));
        var py = Mathf.RoundToInt(uv.y * (texture.height - 1));
        DrawableSuitsDiagnostics.Info($"DecalStampCommitted: mode={mode}; pointerSource={_pointerSource}; uv={uv}; pixel={px},{py}; decal={CurrentDecalName()}; size={Mathf.RoundToInt(_decalSize)}; rotation={Mathf.RoundToInt(_decalRotation)}; opacity={_brushOpacity:0.##}; previewTexture={_decalPreviewTexture?.width ?? 0}x{_decalPreviewTexture?.height ?? 0}");
    }

    private string CurrentDecalName()
    {
        return _selectedDecalIndex >= 0 && _selectedDecalIndex < _decalFiles.Count
            ? Path.GetFileName(_decalFiles[_selectedDecalIndex])
            : "none";
    }

    private void HandleControllerCursor()
    {
        _mousePositionAvailable = DrawableSuitsInput.TryGetMousePosition(out _lastMousePosition);
        var mouseUsed = DrawableSuitsInput.WasMouseUsedThisFrame();
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
            _decalStampArmed = true;
            return;
        }

        if (!_canPaint)
        {
            _strokeActive = false;
            _decalStampArmed = true;
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
            _decalStampArmed = true;
            _suppressDecalPreviewUntilRelease = false;
            return;
        }

        var texture = DrawableSuitsPlugin.Registry.GetEditableTexture(_selectedSuitId);
        var uvAvailable = TryGetTexturePreviewUv(_cursor, out var uv);
        var overPreview = IsCursorOverPreviewViewport() && uvAvailable;
        LogPaintAttemptIfNeeded("paint input", overPreview, uvAvailable, uv, texture, !_strokeActive);

        if (_tool == EditorTool.Decal)
        {
            HandleSingleDecalStampInput(texture, overPreview && IsUvTargetInSelectedPart(texture, uv), uv, "TextureFallback");
            return;
        }

        if (!overPreview || texture == null)
        {
            _strokeActive = false;
            return;
        }

        if (!IsUvTargetInSelectedPart(texture, uv))
        {
            SetStatus($"Aim at the visible {PartDisplayName(_selectedPart)} UV region to paint.", false);
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
            _decalStampArmed = true;
            _suppressDecalPreviewUntilRelease = false;
            return;
        }

        var texture = DrawableSuitsPlugin.Registry.GetEditableTexture(_selectedSuitId);
        var hitAvailable = TryGetWorldPaintHit(out var hit, true);
        var uv = hitAvailable ? hit.textureCoord : default;
        LogPaintAttemptIfNeeded("world paint input", hitAvailable, hitAvailable, uv, texture, !_strokeActive);

        if (_tool == EditorTool.Decal)
        {
            HandleSingleDecalStampInput(texture, hitAvailable, uv, "WorldThirdPerson");
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
            SaveUndo();
            _redo.Clear();
            _strokeActive = true;
        }

        PaintAtCursor(texture, uv);
    }

    private void HandleSingleDecalStampInput(Texture2D texture, bool targetAvailable, Vector2 uv, string mode)
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
                SetStatus("Aim at your visible suit to stamp the decal.", false);
            }
            return;
        }

        if (_loadedDecal == null)
        {
            WarnMissingDecal($"{mode} stamp");
            _tool = EditorTool.Paint;
            UpdateToolButtons();
            return;
        }

        SaveUndo();
        _redo.Clear();
        if (PaintAtCursor(texture, uv))
        {
            LogDecalStampCommitted(mode, texture, uv);
        }
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

        var key = $"{reason}|part={_selectedPart}|tool={_tool}|over={overPreview}|uv={uvAvailable}:{uv}|pixel={pixel}|allowed={(uvAvailable && texture != null ? IsUvTargetInSelectedPart(texture, uv).ToString() : "n/a")}|brush={Mathf.RoundToInt(_brushSize)}|opacity={_brushOpacity:0.##}|decal={_loadedDecal != null}|source={_pointerSource}";
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
        var key = $"applied|part={_selectedPart}|tool={_tool}|pixel={px},{py}|brush={Mathf.RoundToInt(_brushSize)}|opacity={_brushOpacity:0.##}|decal={_loadedDecal != null}";
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

    private bool PaintAtCursor(Texture2D texture, Vector2 uv)
    {
        if (texture == null)
        {
            RefreshEditorReadiness("paint preflight failed");
            UpdateUiState();
            return false;
        }

        var changed = true;
        switch (_tool)
        {
            case EditorTool.Paint:
                changed = PaintCircle(texture, uv, _brushColor, _brushSize, _brushOpacity);
                break;
            case EditorTool.Erase:
                changed = EraseCircle(texture, uv, _brushSize, _brushOpacity);
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
            return false;
        }

        texture.Apply(false, false);
        InvalidateDecalPreview("texture changed by paint");
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
        return true;
    }

    private bool PaintCircle(Texture2D texture, Vector2 uv, Color color, float radius, float opacity)
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
                if (!IsPixelInSelectedPart(texture, x, y))
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

    private bool EraseCircle(Texture2D texture, Vector2 uv, float radius, float opacity)
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
                if (!IsPixelInSelectedPart(texture, x, y))
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

    private bool ApplyDecal(Texture2D target, Vector2 uv)
    {
        if (_loadedDecal == null)
        {
            WarnMissingDecal("ApplyDecal");
            return false;
        }

        return CompositeDecal(target, _loadedDecal, uv, _decalSize, _decalRotation, _brushOpacity);
    }

    private bool CompositeDecal(Texture2D target, Texture2D decal, Vector2 uv, float decalSize, float decalRotation, float opacity)
    {
        if (target == null || decal == null)
        {
            return false;
        }

        var centerX = Mathf.RoundToInt(uv.x * (target.width - 1));
        var centerY = Mathf.RoundToInt(uv.y * (target.height - 1));
        var size = Mathf.RoundToInt(decalSize);
        var half = Mathf.Max(1, size / 2);
        var radians = decalRotation * Mathf.Deg2Rad;
        var cos = Mathf.Cos(radians);
        var sin = Mathf.Sin(radians);
        var changed = false;

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
                if (!IsPixelInSelectedPart(target, tx, ty))
                {
                    continue;
                }

                var decalColor = decal.GetPixelBilinear(u, v);
                if (decalColor.a <= 0.01f)
                {
                    continue;
                }

                var existing = target.GetPixel(tx, ty);
                target.SetPixel(tx, ty, Color.Lerp(existing, decalColor, decalColor.a * Mathf.Clamp01(opacity)));
                changed = true;
            }
        }

        return changed;
    }

    private bool IsUvTargetInSelectedPart(Texture2D texture, Vector2 uv)
    {
        if (_selectedPart == SuitPart.All || texture == null)
        {
            return true;
        }

        var x = Mathf.RoundToInt(Mathf.Clamp01(uv.x) * (texture.width - 1));
        var y = Mathf.RoundToInt(Mathf.Clamp01(uv.y) * (texture.height - 1));
        return IsPixelInSelectedPart(texture, x, y);
    }

    private bool IsPixelInSelectedPart(Texture2D texture, int x, int y)
    {
        if (_selectedPart == SuitPart.All)
        {
            return true;
        }

        if (texture == null
            || !_partUvMasks.TryGetValue(_selectedPart, out var mask)
            || mask == null
            || texture.width != _partDataTextureWidth
            || texture.height != _partDataTextureHeight
            || x < 0 || x >= texture.width || y < 0 || y >= texture.height)
        {
            return false;
        }

        return mask[y * texture.width + x];
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
        InvalidateDecalPreview("undo");
        if (_usingTexturePreview)
        {
            UseTexturePreview("Undo", false);
        }
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
        InvalidateDecalPreview("redo");
        if (_usingTexturePreview)
        {
            UseTexturePreview("Redo", false);
        }
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
            var previouslySelectedDesign = GetSelectedFilePath(_designFiles, _selectedDesignIndex);
            RefreshFileLists();
            _selectedDesignIndex = FindFileIndex(_designFiles, previouslySelectedDesign);
            SetStatus("Design saved.", false);
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
            InvalidateDecalPreview("load design");
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

    private void ImportDecalFromDialog()
    {
        RefreshFileLists();
        SetStatus("Decals refreshed. Add PNG/JPG files to the Decals folder.", false);
        DrawableSuitsDiagnostics.Warn($"OS file dialog import is disabled for stability in {PluginInfo.Version}. EnableOsFileDialog config value is ignored. DecalsPath={DrawableSuitsPaths.Decals}");
    }

    private void SelectDecal(int index)
    {
        if (index < 0 || index >= _decalFiles.Count)
        {
            return;
        }

        _selectedDecalIndex = index;
        InvalidateDecalPreview("select decal");
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
        _selectedPart = SuitPart.All;
        ClearPartIsolationData();
        DrawableSuitsPlugin.Registry.GetOrCreateState(_selectedSuitId);
        _undo.Clear();
        _redo.Clear();
        DrawableSuitsDiagnostics.Info($"SelectSuit selected suitId={_selectedSuitId}; name={DrawableSuitsPlugin.Registry.GetSuitName(_selectedSuitId)}");
        InvalidateDecalPreview("select suit");
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
            || context.IndexOf("SelectSuit", StringComparison.OrdinalIgnoreCase) >= 0;
    }

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
            EnsurePartIsolationDataForCurrentSuit(editableTexture, "UV fallback setup");
            DestroyPreview();
            UseTexturePreview(context, true);
            SetStatus($"Ready. UV fallback preview is active. Editing: {PartDisplayName(_selectedPart)}.", false);
            return;
        }

        if (SetupWorldThirdPersonPreview(context, preserveCamera ? preservedCameraState : default))
        {
            return;
        }

        _uvFallbackMode = true;
        DestroyPreview();
        UseTexturePreview(context, true);
        SetStatus("Third-person setup failed; using UV fallback preview.", true);
    }

    private void EnsurePartIsolationDataForCurrentSuit(Texture2D texture, string context)
    {
        if (texture == null)
        {
            return;
        }

        var player = StartOfRound.Instance?.localPlayerController;
        var source = _worldSourceRenderer ?? FindBestSuitRenderer(player);
        if (source == null)
        {
            DrawableSuitsDiagnostics.Warn($"Part isolation skipped [{context}] because no suit renderer was found.");
            return;
        }

        if (_partDataReady
            && _partDataSourceId == source.GetInstanceID()
            && _partDataTextureWidth == texture.width
            && _partDataTextureHeight == texture.height)
        {
            return;
        }

        var mesh = new Mesh { name = "DrawableSuitsPartClassificationMesh" };
        var previousEnabled = source.enabled;
        try
        {
            source.enabled = true;
            source.BakeMesh(mesh, true);
            BuildPartIsolationData(source, mesh, texture, context);
        }
        catch (Exception ex)
        {
            DrawableSuitsDiagnostics.Exception($"Part isolation mesh bake failed [{context}]", ex);
        }
        finally
        {
            source.enabled = DrawableSuitsPlugin.IsEditorOpen ? false : previousEnabled;
            Destroy(mesh);
        }
    }

    private void BuildPartIsolationData(SkinnedMeshRenderer source, Mesh bakedMesh, Texture2D texture, string context)
    {
        if (source == null || bakedMesh == null || texture == null || bakedMesh.vertexCount == 0)
        {
            return;
        }

        ClearPartIsolationData();
        var subMeshCount = Mathf.Max(1, bakedMesh.subMeshCount);
        foreach (SuitPart part in Enum.GetValues(typeof(SuitPart)))
        {
            _partTrianglesBySubmesh[part] = new int[subMeshCount][];
            _partTriangleCounts[part] = 0;
        }

        var workingTriangles = new Dictionary<SuitPart, List<int>[]>();
        foreach (SuitPart part in Enum.GetValues(typeof(SuitPart)))
        {
            var lists = new List<int>[subMeshCount];
            for (var submesh = 0; submesh < subMeshCount; submesh++)
            {
                lists[submesh] = new List<int>();
            }
            workingTriangles[part] = lists;
        }

        var vertices = bakedMesh.vertices;
        var sourceMesh = source.sharedMesh;
        var boneWeights = sourceMesh != null && sourceMesh.boneWeights != null && sourceMesh.boneWeights.Length == vertices.Length
            ? sourceMesh.boneWeights
            : null;
        var bones = source.bones;
        var fingerprint = DescribePartPresetFingerprint(source, bakedMesh, texture);
        var configPresetName = TryLoadConfigPartPreset(source, bakedMesh, texture, out var configPresetMap);
        var useConfigPreset = configPresetMap != null && configPresetMap.Count > 0;
        var vanillaPresetReason = "none";
        var useVanillaPreset = !useConfigPreset && IsVanillaHumanoidPartPresetMatch(source, bakedMesh, texture, out vanillaPresetReason);
        var records = new List<PartTriangleRecord>();
        var usedBoneClassification = false;
        var recoveredHelmetByGeometry = 0;
        var weakOrAmbiguousBoneTriangles = 0;
        var configPresetTriangles = 0;
        var vanillaPresetTriangles = 0;
        var allTriangleCount = 0;
        for (var submesh = 0; submesh < subMeshCount; submesh++)
        {
            var triangles = bakedMesh.GetTriangles(submesh);
            workingTriangles[SuitPart.All][submesh].AddRange(triangles);
            for (var i = 0; i + 2 < triangles.Length; i += 3)
            {
                var a = triangles[i];
                var b = triangles[i + 1];
                var c = triangles[i + 2];
                var triangleOrdinal = allTriangleCount;
                SuitPart classified;
                bool usedBones;
                SuitPart geometryPart;
                SuitPart bonePart;
                TriangleRegion region;
                bool weakOrAmbiguousBone;
                if (useConfigPreset && configPresetMap.TryGetValue(triangleOrdinal, out var configPart))
                {
                    classified = configPart;
                    ClassifyTriangle(
                        source,
                        bakedMesh.bounds,
                        vertices,
                        boneWeights,
                        bones,
                        a,
                        b,
                        c,
                        out usedBones,
                        out geometryPart,
                        out bonePart,
                        out region,
                        out weakOrAmbiguousBone);
                    configPresetTriangles++;
                }
                else if (useVanillaPreset)
                {
                    classified = ClassifyTriangleByVanillaHumanoidPreset(
                        bakedMesh.bounds,
                        vertices,
                        boneWeights,
                        bones,
                        a,
                        b,
                        c,
                        out usedBones,
                        out geometryPart,
                        out bonePart,
                        out region,
                        out weakOrAmbiguousBone);
                    vanillaPresetTriangles++;
                }
                else
                {
                    classified = ClassifyTriangle(
                        source,
                        bakedMesh.bounds,
                        vertices,
                        boneWeights,
                        bones,
                        a,
                        b,
                        c,
                        out usedBones,
                        out geometryPart,
                        out bonePart,
                        out region,
                        out weakOrAmbiguousBone);
                }
                usedBoneClassification |= usedBones;
                if (weakOrAmbiguousBone)
                {
                    weakOrAmbiguousBoneTriangles++;
                }
                if (classified == SuitPart.Helmet && geometryPart == SuitPart.Helmet && bonePart != SuitPart.Helmet)
                {
                    recoveredHelmetByGeometry++;
                }
                records.Add(new PartTriangleRecord
                {
                    Submesh = submesh,
                    A = a,
                    B = b,
                    C = c,
                    Part = classified,
                    GeometryPart = geometryPart,
                    BonePart = bonePart,
                    UsedBones = usedBones,
                    Center = region.Center,
                    Height = region.Height,
                    Side = region.Side,
                    XOffset = region.XOffset
                });
                allTriangleCount++;
            }
        }

        var rawCounts = CountPartTriangleRecords(records);
        var vanillaCorrectionSummary = useVanillaPreset ? ApplyVanillaHumanoidPresetCorrections(records) : "none";
        var cleanupSummary = CleanupPartTriangleRecords(records);
        var cleanedCounts = CountPartTriangleRecords(records);
        foreach (var record in records)
        {
            workingTriangles[record.Part][record.Submesh].Add(record.A);
            workingTriangles[record.Part][record.Submesh].Add(record.B);
            workingTriangles[record.Part][record.Submesh].Add(record.C);
        }

        _partTriangleCounts[SuitPart.All] = allTriangleCount;
        foreach (SuitPart part in Enum.GetValues(typeof(SuitPart)))
        {
            var destination = _partTrianglesBySubmesh[part];
            var lists = workingTriangles[part];
            for (var submesh = 0; submesh < subMeshCount; submesh++)
            {
                destination[submesh] = lists[submesh].ToArray();
            }
            if (part != SuitPart.All)
            {
                _partTriangleCounts[part] = cleanedCounts.TryGetValue(part, out var count) ? count : 0;
            }
        }

        if (useConfigPreset)
        {
            _partClassifierSource = $"ConfigPreset:{configPresetName}";
        }
        else if (useVanillaPreset)
        {
            _partClassifierSource = "Preset:VanillaHumanoid";
        }
        else
        {
            _partClassifierSource = usedBoneClassification ? "BonesPrimary+BoundsFallback" : "BoundsFallback";
        }
        BuildPartUvMasks(bakedMesh, texture);
        _partDataReady = true;
        _partDataSourceId = source.GetInstanceID();
        _partDataTextureWidth = texture.width;
        _partDataTextureHeight = texture.height;
        var countParts = new List<string>();
        foreach (SuitPart part in Enum.GetValues(typeof(SuitPart)))
        {
            countParts.Add($"{part}=triangles:{GetPartTriangleCount(part)},pixels:{GetPartMaskPixelCount(part)}");
        }
        DrawableSuitsDiagnostics.Info($"PartClassifierBuilt[{context}]: source={_partClassifierSource}; fingerprint={fingerprint}; presetMatch={(useConfigPreset ? configPresetName : useVanillaPreset ? vanillaPresetReason : "none")}; configPresetTriangles={configPresetTriangles}; vanillaPresetTriangles={vanillaPresetTriangles}; renderer={source.name}; texture={texture.width}x{texture.height}; overlap={_partUvOverlapDetected}; raw={DescribePartCounts(rawCounts)}; cleaned={DescribePartCounts(cleanedCounts)}; bounds={DescribePartBounds(records)}; components={DescribePartComponentRanges(records)}; sanity={cleanupSummary}; vanillaCorrections={vanillaCorrectionSummary}; helmetRecoveredByTopCap={recoveredHelmetByGeometry}; weakOrAmbiguousBoneTriangles={weakOrAmbiguousBoneTriangles}; bones={DescribeTopMappedBones(source, bones)}; {string.Join("; ", countParts)}");
        UpdatePartButtons();
    }

    private static string TryLoadConfigPartPreset(SkinnedMeshRenderer source, Mesh mesh, Texture2D texture, out Dictionary<int, SuitPart> triangleMap)
    {
        triangleMap = null;
        try
        {
            Directory.CreateDirectory(DrawableSuitsPaths.PartPresets);
            var files = Directory.GetFiles(DrawableSuitsPaths.PartPresets, "*.json", SearchOption.TopDirectoryOnly);
            for (var fileIndex = 0; fileIndex < files.Length; fileIndex++)
            {
                PartPresetFile preset;
                try
                {
                    preset = JsonUtility.FromJson<PartPresetFile>(File.ReadAllText(files[fileIndex]));
                }
                catch (Exception ex)
                {
                    DrawableSuitsDiagnostics.Exception($"Part preset file could not be parsed. file={files[fileIndex]}", ex);
                    continue;
                }

                var mismatch = "null preset";
                if (preset == null || !ConfigPartPresetMatches(preset, source, mesh, texture, out mismatch))
                {
                    if (preset != null)
                    {
                        DrawableSuitsDiagnostics.Info($"Part preset skipped. name={preset.name ?? Path.GetFileNameWithoutExtension(files[fileIndex])}; reason={mismatch}");
                    }
                    continue;
                }

                var map = new Dictionary<int, SuitPart>();
                var assignments = preset.parts;
                if (assignments != null)
                {
                    for (var assignmentIndex = 0; assignmentIndex < assignments.Length; assignmentIndex++)
                    {
                        var assignment = assignments[assignmentIndex];
                        if (assignment == null || assignment.triangles == null || !TryParseSuitPart(assignment.part, out var part) || part == SuitPart.All)
                        {
                            continue;
                        }

                        for (var i = 0; i < assignment.triangles.Length; i++)
                        {
                            var triangle = assignment.triangles[i];
                            if (triangle >= 0)
                            {
                                map[triangle] = part;
                            }
                        }
                    }
                }

                if (map.Count == 0)
                {
                    DrawableSuitsDiagnostics.Warn($"Part preset matched but has no valid triangle assignments. name={preset.name ?? Path.GetFileNameWithoutExtension(files[fileIndex])}; file={files[fileIndex]}");
                    continue;
                }

                triangleMap = map;
                var name = string.IsNullOrWhiteSpace(preset.name) ? Path.GetFileNameWithoutExtension(files[fileIndex]) : preset.name;
                DrawableSuitsDiagnostics.Info($"Part preset matched. name={name}; assignments={map.Count}; file={files[fileIndex]}");
                return name;
            }
        }
        catch (Exception ex)
        {
            DrawableSuitsDiagnostics.Exception("Part preset loading failed", ex);
        }

        return "none";
    }

    private static bool ConfigPartPresetMatches(PartPresetFile preset, SkinnedMeshRenderer source, Mesh mesh, Texture2D texture, out string mismatch)
    {
        mismatch = "matched";
        if (source == null || mesh == null || texture == null)
        {
            mismatch = "missing source, mesh, or texture";
            return false;
        }

        var rendererName = source.name ?? string.Empty;
        var rendererPath = GetTransformPath(source.transform);
        var materialName = source.sharedMaterial?.name ?? string.Empty;
        if (!StringContainsIfSet(rendererName, preset.rendererNameContains))
        {
            mismatch = $"rendererName does not contain {preset.rendererNameContains}";
            return false;
        }
        if (!StringContainsIfSet(rendererPath, preset.rendererPathContains))
        {
            mismatch = $"rendererPath does not contain {preset.rendererPathContains}";
            return false;
        }
        if (!StringContainsIfSet(materialName, preset.materialNameContains))
        {
            mismatch = $"materialName does not contain {preset.materialNameContains}";
            return false;
        }
        if (preset.vertexCount > 0 && mesh.vertexCount != preset.vertexCount)
        {
            mismatch = $"vertexCount {mesh.vertexCount} != {preset.vertexCount}";
            return false;
        }
        if (preset.triangleCount > 0 && CountMeshTriangles(mesh) != preset.triangleCount)
        {
            mismatch = $"triangleCount {CountMeshTriangles(mesh)} != {preset.triangleCount}";
            return false;
        }
        if (preset.textureWidth > 0 && texture.width != preset.textureWidth)
        {
            mismatch = $"textureWidth {texture.width} != {preset.textureWidth}";
            return false;
        }
        if (preset.textureHeight > 0 && texture.height != preset.textureHeight)
        {
            mismatch = $"textureHeight {texture.height} != {preset.textureHeight}";
            return false;
        }

        return true;
    }

    private static bool IsVanillaHumanoidPartPresetMatch(SkinnedMeshRenderer source, Mesh mesh, Texture2D texture, out string reason)
    {
        reason = "none";
        if (source == null || mesh == null || texture == null)
        {
            return false;
        }

        var rendererPath = GetTransformPath(source.transform);
        var materialName = source.sharedMaterial?.name ?? string.Empty;
        var triangleCount = CountMeshTriangles(mesh);
        var exactVanillaMesh = mesh.vertexCount == 7998 && triangleCount == 8016;
        var vanillaRenderer = string.Equals(source.name, "LOD1", StringComparison.OrdinalIgnoreCase)
            && rendererPath.IndexOf("ScavengerModel/LOD1", StringComparison.OrdinalIgnoreCase) >= 0;
        var vanillaMaterial = materialName.IndexOf("DefaultPlayerSuit", StringComparison.OrdinalIgnoreCase) >= 0;
        var vanillaTexture = texture.width == 1024 && texture.height == 1024;
        if (exactVanillaMesh && vanillaRenderer && vanillaMaterial && vanillaTexture)
        {
            reason = $"VanillaHumanoid vertexCount={mesh.vertexCount}; triangleCount={triangleCount}; rendererPath={rendererPath}; material={materialName}; texture={texture.width}x{texture.height}";
            return true;
        }

        reason = $"no vanilla preset match vertexCount={mesh.vertexCount}; triangleCount={triangleCount}; renderer={source.name}; path={rendererPath}; material={materialName}; texture={texture.width}x{texture.height}";
        return false;
    }

    private static string DescribePartPresetFingerprint(SkinnedMeshRenderer source, Mesh mesh, Texture2D texture)
    {
        return $"renderer={source?.name ?? "null"}; path={GetTransformPath(source?.transform)}; material={source?.sharedMaterial?.name ?? "null"}; meshVertices={mesh?.vertexCount ?? 0}; meshTriangles={(mesh != null ? CountMeshTriangles(mesh) : 0)}; subMeshes={mesh?.subMeshCount ?? 0}; texture={texture?.width ?? 0}x{texture?.height ?? 0}";
    }

    private static bool StringContainsIfSet(string source, string required)
    {
        return string.IsNullOrWhiteSpace(required)
            || (source ?? string.Empty).IndexOf(required, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static int CountMeshTriangles(Mesh mesh)
    {
        if (mesh == null)
        {
            return 0;
        }

        var count = 0;
        for (var submesh = 0; submesh < mesh.subMeshCount; submesh++)
        {
            count += mesh.GetTriangles(submesh).Length / 3;
        }
        return count;
    }

    private static bool TryParseSuitPart(string value, out SuitPart part)
    {
        part = SuitPart.Other;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty);
        foreach (SuitPart candidate in Enum.GetValues(typeof(SuitPart)))
        {
            if (string.Equals(normalized, candidate.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                part = candidate;
                return true;
            }
        }

        return false;
    }

    private SuitPart ClassifyTriangle(
        SkinnedMeshRenderer source,
        Bounds bounds,
        Vector3[] vertices,
        BoneWeight[] weights,
        Transform[] bones,
        int a,
        int b,
        int c,
        out bool usedBones,
        out SuitPart geometryPart,
        out SuitPart bonePart,
        out TriangleRegion region,
        out bool weakOrAmbiguousBone)
    {
        usedBones = false;
        geometryPart = SuitPart.Other;
        bonePart = SuitPart.Other;
        region = default;
        weakOrAmbiguousBone = false;
        if (a < 0 || b < 0 || c < 0 || a >= vertices.Length || b >= vertices.Length || c >= vertices.Length)
        {
            return SuitPart.Other;
        }

        geometryPart = ClassifyTriangleByBounds(bounds, vertices, a, b, c, out region);
        if (weights != null && bones != null && bones.Length > 0)
        {
            bonePart = ClassifyTriangleByBones(weights, bones, a, b, c, out var bestBoneScore);
            if (bonePart != SuitPart.Other && bestBoneScore >= 0.16f)
            {
                usedBones = true;

                if (IsStrongHelmetFallback(region, bonePart))
                {
                    return SuitPart.Helmet;
                }

                return bonePart;
            }

            weakOrAmbiguousBone = bestBoneScore > 0.001f;
        }

        return geometryPart;
    }

    private static SuitPart ClassifyTriangleByVanillaHumanoidPreset(
        Bounds bounds,
        Vector3[] vertices,
        BoneWeight[] weights,
        Transform[] bones,
        int a,
        int b,
        int c,
        out bool usedBones,
        out SuitPart geometryPart,
        out SuitPart bonePart,
        out TriangleRegion region,
        out bool weakOrAmbiguousBone)
    {
        usedBones = false;
        weakOrAmbiguousBone = false;
        geometryPart = SuitPart.Other;
        bonePart = SuitPart.Other;
        region = default;
        if (a < 0 || b < 0 || c < 0 || a >= vertices.Length || b >= vertices.Length || c >= vertices.Length)
        {
            return SuitPart.Other;
        }

        // Vanilla Lethal Company player suits bake with their body height along local Z and side along local X.
        region = BuildTriangleRegionForAxes(bounds, vertices, a, b, c, verticalAxis: 2, sideAxis: 0);
        geometryPart = ClassifyVanillaRegion(region);

        var bestBoneScore = 0f;
        if (weights != null && bones != null && bones.Length > 0)
        {
            bonePart = ClassifyTriangleByBones(weights, bones, a, b, c, out bestBoneScore);
        }

        if (bonePart != SuitPart.Other && bestBoneScore >= 0.16f)
        {
            usedBones = true;
            if (bonePart == SuitPart.Torso && VanillaBonePartFitsRegion(bonePart, region))
            {
                return SuitPart.Torso;
            }

            if ((bonePart == SuitPart.LeftArm || bonePart == SuitPart.RightArm)
                && IsVanillaCentralUpperBodyOrStrap(region)
                && bestBoneScore < 0.55f)
            {
                weakOrAmbiguousBone = true;
                return SuitPart.Torso;
            }

            if (VanillaBonePartFitsRegion(bonePart, region))
            {
                return bonePart;
            }

            weakOrAmbiguousBone = true;
        }
        else if (bestBoneScore > 0.001f)
        {
            weakOrAmbiguousBone = true;
        }

        if (IsVanillaStrictHelmetRegion(region))
        {
            return SuitPart.Helmet;
        }

        return geometryPart;
    }

    private static TriangleRegion BuildTriangleRegionForAxes(Bounds bounds, Vector3[] vertices, int a, int b, int c, int verticalAxis, int sideAxis)
    {
        var center = (vertices[a] + vertices[b] + vertices[c]) / 3f;
        var va = GetAxisValue(vertices[a], verticalAxis);
        var vb = GetAxisValue(vertices[b], verticalAxis);
        var vc = GetAxisValue(vertices[c], verticalAxis);
        var minVertical = Mathf.Min(va, Mathf.Min(vb, vc));
        var maxVertical = Mathf.Max(va, Mathf.Max(vb, vc));
        var boundsMin = GetAxisValue(bounds.min, verticalAxis);
        var boundsMax = GetAxisValue(bounds.max, verticalAxis);
        var height = Mathf.InverseLerp(boundsMin, boundsMax, GetAxisValue(center, verticalAxis));
        var minHeight = Mathf.InverseLerp(boundsMin, boundsMax, minVertical);
        var maxHeight = Mathf.InverseLerp(boundsMin, boundsMax, maxVertical);
        var xOffset = GetAxisValue(center, sideAxis) - GetAxisValue(bounds.center, sideAxis);
        var sideExtent = Mathf.Max(0.001f, GetAxisValue(bounds.extents, sideAxis));
        var side = Mathf.Abs(xOffset) / sideExtent;
        return new TriangleRegion(center, height, minHeight, maxHeight, xOffset, side);
    }

    private static float GetAxisValue(Vector3 value, int axis)
    {
        switch (axis)
        {
            case 0: return value.x;
            case 1: return value.y;
            default: return value.z;
        }
    }

    private static SuitPart ClassifyVanillaRegion(TriangleRegion region)
    {
        if (IsVanillaStrictHelmetRegion(region))
        {
            return SuitPart.Helmet;
        }

        if (region.Height <= 0.43f)
        {
            return region.XOffset <= 0f ? SuitPart.LeftLeg : SuitPart.RightLeg;
        }

        if (IsVanillaCentralUpperBodyOrStrap(region))
        {
            return SuitPart.Torso;
        }

        if (region.Height >= 0.30f && region.Height <= 0.80f && region.Side >= 0.62f)
        {
            return region.XOffset <= 0f ? SuitPart.LeftArm : SuitPart.RightArm;
        }

        if (region.Height >= 0.30f && region.Height <= 0.84f && region.Side <= 0.76f)
        {
            return SuitPart.Torso;
        }

        return SuitPart.Other;
    }

    private static bool VanillaBonePartFitsRegion(SuitPart part, TriangleRegion region)
    {
        switch (part)
        {
            case SuitPart.Helmet:
                return region.Height >= 0.80f && region.Side <= 0.78f;
            case SuitPart.Torso:
                return region.Height >= 0.30f && region.Height <= 0.84f && region.Side <= 0.78f;
            case SuitPart.LeftArm:
            case SuitPart.RightArm:
                return region.Height >= 0.20f && region.Height <= 0.80f && !IsVanillaCentralUpperBodyOrStrap(region) && region.Side >= 0.48f;
            case SuitPart.LeftLeg:
            case SuitPart.RightLeg:
                return region.Height <= 0.52f;
            default:
                return false;
        }
    }

    private static bool IsVanillaStrictHelmetRegion(TriangleRegion region)
    {
        return region.Height >= 0.82f
            && region.MaxHeight >= 0.86f
            && region.MinHeight >= 0.74f
            && region.Side <= 0.74f;
    }

    private static bool IsVanillaCentralUpperBodyOrStrap(TriangleRegion region)
    {
        return region.Height >= 0.44f
            && region.Height <= 0.82f
            && region.Side <= 0.72f;
    }

    private static bool IsVanillaArmStrapLeak(PartTriangleRecord record)
    {
        if (record == null || (record.Part != SuitPart.LeftArm && record.Part != SuitPart.RightArm))
        {
            return false;
        }

        if (record.Height < 0.46f || record.Height > 0.80f)
        {
            return false;
        }

        return record.Side <= 0.68f
            || record.GeometryPart == SuitPart.Torso
            || record.BonePart == SuitPart.Torso;
    }

    private static bool IsVanillaHelmetLeak(PartTriangleRecord record)
    {
        if (record == null || record.Part != SuitPart.Helmet)
        {
            return false;
        }

        return record.Height < 0.80f || record.Side > 0.80f;
    }

    private static bool IsVanillaTorsoAbsorbCandidate(PartTriangleRecord record)
    {
        if (record == null)
        {
            return false;
        }

        return record.Height >= 0.36f
            && record.Height <= 0.84f
            && record.Side <= 0.78f;
    }

    private static bool IsVanillaShoulderFragmentCandidate(PartTriangleRecord record)
    {
        if (record == null)
        {
            return false;
        }

        return record.Height >= 0.48f
            && record.Height <= 0.82f
            && record.Side > 0.78f
            && record.Side <= 0.96f;
    }

    private static string ApplyVanillaHumanoidPresetCorrections(List<PartTriangleRecord> records)
    {
        if (records == null || records.Count == 0)
        {
            return "none";
        }

        var stats = new VanillaPresetCorrectionStats();
        for (var i = 0; i < records.Count; i++)
        {
            var record = records[i];
            if (IsVanillaHelmetLeak(record))
            {
                record.Part = SuitPart.Torso;
                stats.HelmetToTorso++;
                continue;
            }

            if (IsVanillaArmStrapLeak(record))
            {
                record.Part = SuitPart.Torso;
                stats.ArmStrapToTorso++;
                continue;
            }

            if (record.Part != SuitPart.Other)
            {
                continue;
            }

            if (IsVanillaTorsoAbsorbCandidate(record))
            {
                record.Part = SuitPart.Torso;
                stats.OtherToTorso++;
                continue;
            }

            if (IsVanillaShoulderFragmentCandidate(record))
            {
                record.Part = record.XOffset <= 0f ? SuitPart.LeftArm : SuitPart.RightArm;
                stats.OtherToArm++;
            }
        }

        var helmetComponents = BuildPartComponents(records, SuitPart.Helmet);
        for (var componentIndex = 0; componentIndex < helmetComponents.Count; componentIndex++)
        {
            var component = helmetComponents[componentIndex];
            if (component == null || component.Count == 0 || IsPlausibleVanillaHelmetComponent(records, component))
            {
                continue;
            }

            for (var i = 0; i < component.Count; i++)
            {
                var recordIndex = component[i];
                if (recordIndex < 0 || recordIndex >= records.Count || records[recordIndex].Part != SuitPart.Helmet)
                {
                    continue;
                }

                records[recordIndex].Part = SuitPart.Torso;
                stats.HelmetToTorso++;
            }
        }

        return stats.ToString();
    }

    private static bool IsPlausibleVanillaHelmetComponent(List<PartTriangleRecord> records, List<int> component)
    {
        var minHeight = float.MaxValue;
        var maxHeight = float.MinValue;
        var averageHeight = 0f;
        var maxSide = 0f;
        var validCount = 0;
        for (var i = 0; i < component.Count; i++)
        {
            var recordIndex = component[i];
            if (recordIndex < 0 || recordIndex >= records.Count)
            {
                continue;
            }

            var record = records[recordIndex];
            minHeight = Mathf.Min(minHeight, record.Height);
            maxHeight = Mathf.Max(maxHeight, record.Height);
            averageHeight += record.Height;
            maxSide = Mathf.Max(maxSide, record.Side);
            validCount++;
        }

        if (validCount == 0)
        {
            return false;
        }

        averageHeight /= validCount;
        return maxHeight >= 0.86f
            && averageHeight >= 0.82f
            && minHeight >= 0.74f
            && maxSide <= 0.82f;
    }

    private static SuitPart ClassifyTriangleByBounds(Bounds bounds, Vector3[] vertices, int a, int b, int c, out TriangleRegion region)
    {
        var center = (vertices[a] + vertices[b] + vertices[c]) / 3f;
        var minHeight = Mathf.InverseLerp(bounds.min.y, bounds.max.y, Mathf.Min(vertices[a].y, Mathf.Min(vertices[b].y, vertices[c].y)));
        var maxHeight = Mathf.InverseLerp(bounds.min.y, bounds.max.y, Mathf.Max(vertices[a].y, Mathf.Max(vertices[b].y, vertices[c].y)));
        var height = Mathf.InverseLerp(bounds.min.y, bounds.max.y, center.y);
        var xOffset = center.x - bounds.center.x;
        var side = Mathf.Abs(xOffset) / Mathf.Max(0.001f, bounds.extents.x);
        region = new TriangleRegion(center, height, minHeight, maxHeight, xOffset, side);

        if ((height >= 0.78f && side <= 0.88f) || maxHeight >= 0.94f)
        {
            return SuitPart.Helmet;
        }

        if (height <= 0.35f || maxHeight <= 0.40f)
        {
            return xOffset <= 0f ? SuitPart.LeftLeg : SuitPart.RightLeg;
        }

        if (height <= 0.76f && height >= 0.38f && side >= 0.62f)
        {
            return xOffset <= 0f ? SuitPart.LeftArm : SuitPart.RightArm;
        }

        if (height >= 0.28f && height <= 0.86f)
        {
            return SuitPart.Torso;
        }

        if (height < 0.28f)
        {
            return xOffset <= 0f ? SuitPart.LeftLeg : SuitPart.RightLeg;
        }

        return SuitPart.Torso;
    }

    private static bool IsStrongHelmetFallback(TriangleRegion region, SuitPart bonePart)
    {
        return bonePart != SuitPart.Helmet
            && region.Height >= 0.82f
            && region.MaxHeight >= 0.9f
            && region.Side <= 0.8f;
    }

    private static SuitPart ClassifyTriangleByBones(BoneWeight[] weights, Transform[] bones, int a, int b, int c, out float bestScore)
    {
        var scores = new float[Enum.GetValues(typeof(SuitPart)).Length];
        AccumulateVertexBoneScores(weights[a], bones, scores);
        AccumulateVertexBoneScores(weights[b], bones, scores);
        AccumulateVertexBoneScores(weights[c], bones, scores);
        var bestPart = SuitPart.Other;
        bestScore = 0f;
        for (var i = 0; i < IsolatedSuitParts.Length; i++)
        {
            var part = IsolatedSuitParts[i];
            if (scores[(int)part] > bestScore)
            {
                bestScore = scores[(int)part];
                bestPart = part;
            }
        }

        bestScore /= 3f;
        return bestPart;
    }

    private static Dictionary<SuitPart, int> CountPartTriangleRecords(List<PartTriangleRecord> records)
    {
        var counts = new Dictionary<SuitPart, int>();
        foreach (SuitPart part in Enum.GetValues(typeof(SuitPart)))
        {
            counts[part] = 0;
        }

        if (records == null)
        {
            return counts;
        }

        for (var i = 0; i < records.Count; i++)
        {
            var record = records[i];
            counts[record.Part] = counts.TryGetValue(record.Part, out var count) ? count + 1 : 1;
        }

        return counts;
    }

    private static string CleanupPartTriangleRecords(List<PartTriangleRecord> records)
    {
        if (records == null || records.Count == 0)
        {
            return "none";
        }

        var summaries = new List<string>();
        for (var partIndex = 0; partIndex < IsolatedSuitParts.Length; partIndex++)
        {
            var part = IsolatedSuitParts[partIndex];
            var components = BuildPartComponents(records, part);
            if (components.Count == 0)
            {
                summaries.Add($"{part}:components=0");
                continue;
            }

            var largest = 0;
            for (var i = 0; i < components.Count; i++)
            {
                largest = Mathf.Max(largest, components[i].Count);
            }

            var suspiciousTinyThreshold = Mathf.Max(4, Mathf.RoundToInt(largest * 0.015f));
            var suspiciousTinyComponents = 0;
            for (var i = 0; i < components.Count; i++)
            {
                var component = components[i];
                if (component.Count < suspiciousTinyThreshold)
                {
                    suspiciousTinyComponents++;
                }
            }

            summaries.Add($"{part}:components={components.Count},largest={largest},tinyThreshold={suspiciousTinyThreshold},suspiciousTiny={suspiciousTinyComponents}");
        }

        return string.Join("; ", summaries);
    }

    private static List<List<int>> BuildPartComponents(List<PartTriangleRecord> records, SuitPart part)
    {
        var localRecordIndexes = new List<int>();
        var vertexToLocalIndexes = new Dictionary<int, List<int>>();
        for (var i = 0; i < records.Count; i++)
        {
            if (records[i].Part != part)
            {
                continue;
            }

            var localIndex = localRecordIndexes.Count;
            localRecordIndexes.Add(i);
            AddTriangleVertexIndex(vertexToLocalIndexes, records[i].A, localIndex);
            AddTriangleVertexIndex(vertexToLocalIndexes, records[i].B, localIndex);
            AddTriangleVertexIndex(vertexToLocalIndexes, records[i].C, localIndex);
        }

        var components = new List<List<int>>();
        var visited = new bool[localRecordIndexes.Count];
        var stack = new Stack<int>();
        for (var start = 0; start < localRecordIndexes.Count; start++)
        {
            if (visited[start])
            {
                continue;
            }

            var component = new List<int>();
            visited[start] = true;
            stack.Push(start);
            while (stack.Count > 0)
            {
                var localIndex = stack.Pop();
                var recordIndex = localRecordIndexes[localIndex];
                component.Add(recordIndex);
                PushUnvisitedTriangleNeighbors(records[recordIndex].A, vertexToLocalIndexes, visited, stack);
                PushUnvisitedTriangleNeighbors(records[recordIndex].B, vertexToLocalIndexes, visited, stack);
                PushUnvisitedTriangleNeighbors(records[recordIndex].C, vertexToLocalIndexes, visited, stack);
            }

            components.Add(component);
        }

        return components;
    }

    private static void AddTriangleVertexIndex(Dictionary<int, List<int>> vertexToLocalIndexes, int vertexIndex, int localIndex)
    {
        if (!vertexToLocalIndexes.TryGetValue(vertexIndex, out var indexes))
        {
            indexes = new List<int>();
            vertexToLocalIndexes[vertexIndex] = indexes;
        }
        indexes.Add(localIndex);
    }

    private static void PushUnvisitedTriangleNeighbors(int vertexIndex, Dictionary<int, List<int>> vertexToLocalIndexes, bool[] visited, Stack<int> stack)
    {
        if (!vertexToLocalIndexes.TryGetValue(vertexIndex, out var neighbors))
        {
            return;
        }

        for (var i = 0; i < neighbors.Count; i++)
        {
            var neighbor = neighbors[i];
            if (neighbor < 0 || neighbor >= visited.Length || visited[neighbor])
            {
                continue;
            }

            visited[neighbor] = true;
            stack.Push(neighbor);
        }
    }

    private static string DescribePartCounts(Dictionary<SuitPart, int> counts)
    {
        var parts = new List<string>();
        foreach (SuitPart part in Enum.GetValues(typeof(SuitPart)))
        {
            if (part == SuitPart.All)
            {
                continue;
            }

            parts.Add($"{part}:{(counts != null && counts.TryGetValue(part, out var count) ? count : 0)}");
        }

        return string.Join(",", parts);
    }

    private static string DescribePartBounds(List<PartTriangleRecord> records)
    {
        if (records == null || records.Count == 0)
        {
            return "none";
        }

        var parts = new List<string>();
        for (var partIndex = 0; partIndex < IsolatedSuitParts.Length; partIndex++)
        {
            var part = IsolatedSuitParts[partIndex];
            var count = 0;
            var minHeight = float.MaxValue;
            var maxHeight = float.MinValue;
            var maxSide = 0f;
            for (var i = 0; i < records.Count; i++)
            {
                if (records[i].Part != part)
                {
                    continue;
                }

                count++;
                minHeight = Mathf.Min(minHeight, records[i].Height);
                maxHeight = Mathf.Max(maxHeight, records[i].Height);
                maxSide = Mathf.Max(maxSide, records[i].Side);
            }

            if (count == 0)
            {
                parts.Add($"{part}:empty");
            }
            else
            {
                parts.Add($"{part}:tri={count},h={minHeight.ToString("0.00", CultureInfo.InvariantCulture)}-{maxHeight.ToString("0.00", CultureInfo.InvariantCulture)},sideMax={maxSide.ToString("0.00", CultureInfo.InvariantCulture)}");
            }
        }

        return string.Join("; ", parts);
    }

    private static string DescribePartComponentRanges(List<PartTriangleRecord> records)
    {
        if (records == null || records.Count == 0)
        {
            return "none";
        }

        var parts = new List<string>();
        for (var partIndex = 0; partIndex < IsolatedSuitParts.Length; partIndex++)
        {
            var part = IsolatedSuitParts[partIndex];
            var components = BuildPartComponents(records, part);
            if (components.Count == 0)
            {
                parts.Add($"{part}:components=0");
                continue;
            }

            components.Sort((left, right) => right.Count.CompareTo(left.Count));
            var ranges = new List<string>();
            var described = Mathf.Min(3, components.Count);
            for (var componentIndex = 0; componentIndex < described; componentIndex++)
            {
                var component = components[componentIndex];
                var minHeight = float.MaxValue;
                var maxHeight = float.MinValue;
                var maxSide = 0f;
                for (var i = 0; i < component.Count; i++)
                {
                    var recordIndex = component[i];
                    if (recordIndex < 0 || recordIndex >= records.Count)
                    {
                        continue;
                    }

                    var record = records[recordIndex];
                    minHeight = Mathf.Min(minHeight, record.Height);
                    maxHeight = Mathf.Max(maxHeight, record.Height);
                    maxSide = Mathf.Max(maxSide, record.Side);
                }

                ranges.Add($"{component.Count}@h={minHeight.ToString("0.00", CultureInfo.InvariantCulture)}-{maxHeight.ToString("0.00", CultureInfo.InvariantCulture)},sideMax={maxSide.ToString("0.00", CultureInfo.InvariantCulture)}");
            }

            parts.Add($"{part}:components={components.Count}[{string.Join("|", ranges)}]");
        }

        return string.Join("; ", parts);
    }

    private static string DescribeTopMappedBones(SkinnedMeshRenderer source, Transform[] bones)
    {
        if (source == null || bones == null || bones.Length == 0)
        {
            return "none";
        }

        var mapped = new Dictionary<SuitPart, List<string>>();
        for (var i = 0; i < bones.Length; i++)
        {
            var bone = bones[i];
            if (bone == null || !TryMapBoneToPart(bone.name, out var part))
            {
                continue;
            }

            if (!mapped.TryGetValue(part, out var names))
            {
                names = new List<string>();
                mapped[part] = names;
            }

            if (names.Count < 4)
            {
                names.Add(bone.name);
            }
        }

        if (mapped.Count == 0)
        {
            return "none";
        }

        var parts = new List<string>();
        foreach (SuitPart part in Enum.GetValues(typeof(SuitPart)))
        {
            if (mapped.TryGetValue(part, out var names))
            {
                parts.Add($"{part}=[{string.Join(",", names)}]");
            }
        }

        return string.Join("; ", parts);
    }

    private static void AccumulateVertexBoneScores(BoneWeight weight, Transform[] bones, float[] scores)
    {
        AccumulateBoneScore(weight.boneIndex0, weight.weight0, bones, scores);
        AccumulateBoneScore(weight.boneIndex1, weight.weight1, bones, scores);
        AccumulateBoneScore(weight.boneIndex2, weight.weight2, bones, scores);
        AccumulateBoneScore(weight.boneIndex3, weight.weight3, bones, scores);
    }

    private static void AccumulateBoneScore(int boneIndex, float weight, Transform[] bones, float[] scores)
    {
        if (weight <= 0f || boneIndex < 0 || boneIndex >= bones.Length || bones[boneIndex] == null)
        {
            return;
        }

        if (TryMapBoneToPart(bones[boneIndex].name, out var part))
        {
            scores[(int)part] += weight;
        }
    }

    private static bool TryMapBoneToPart(string boneName, out SuitPart part)
    {
        part = SuitPart.Other;
        var lower = (boneName ?? string.Empty).ToLowerInvariant();
        var left = BoneNameHasSideToken(lower, true);
        var right = BoneNameHasSideToken(lower, false);
        if (lower.Contains("head")
            || lower.Contains("neck")
            || lower.Contains("helmet")
            || lower.Contains("skull")
            || lower.Contains("visor")
            || lower.Contains("mask"))
        {
            part = SuitPart.Helmet;
            return true;
        }
        if ((lower.Contains("arm")
                || lower.Contains("shoulder")
                || lower.Contains("clavicle")
                || lower.Contains("elbow")
                || lower.Contains("forearm")
                || lower.Contains("hand")
                || lower.Contains("wrist"))
            && (left || right))
        {
            part = left ? SuitPart.LeftArm : SuitPart.RightArm;
            return true;
        }
        if ((lower.Contains("leg")
                || lower.Contains("thigh")
                || lower.Contains("knee")
                || lower.Contains("shin")
                || lower.Contains("ankle")
                || lower.Contains("foot")
                || lower.Contains("toe")
                || lower.Contains("calf"))
            && (left || right))
        {
            part = left ? SuitPart.LeftLeg : SuitPart.RightLeg;
            return true;
        }
        if (lower.Contains("spine")
            || lower.Contains("chest")
            || lower.Contains("torso")
            || lower.Contains("hips")
            || lower.Contains("pelvis")
            || lower.Contains("abdomen")
            || lower.Contains("waist")
            || lower.Contains("body"))
        {
            part = SuitPart.Torso;
            return true;
        }
        return false;
    }

    private static bool BoneNameHasSideToken(string lowerBoneName, bool left)
    {
        if (string.IsNullOrWhiteSpace(lowerBoneName))
        {
            return false;
        }

        var longToken = left ? "left" : "right";
        if (lowerBoneName.Contains(longToken))
        {
            return true;
        }

        var shortToken = left ? "l" : "r";
        var tokens = lowerBoneName.Split('.', '_', '-', ' ', ':', '/', '\\', '|', '(', ')', '[', ']');
        for (var i = 0; i < tokens.Length; i++)
        {
            if (string.Equals(tokens[i], shortToken, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return lowerBoneName.EndsWith("." + shortToken, StringComparison.Ordinal)
            || lowerBoneName.EndsWith("_" + shortToken, StringComparison.Ordinal)
            || lowerBoneName.EndsWith("-" + shortToken, StringComparison.Ordinal)
            || lowerBoneName.EndsWith(" " + shortToken, StringComparison.Ordinal);
    }

    private void BuildPartUvMasks(Mesh mesh, Texture2D texture)
    {
        var pixelCount = texture.width * texture.height;
        var allMask = new bool[pixelCount];
        for (var i = 0; i < allMask.Length; i++)
        {
            allMask[i] = true;
        }
        _partUvMasks[SuitPart.All] = allMask;
        _partMaskPixelCounts[SuitPart.All] = pixelCount;
        var uv = mesh.uv;
        if (uv == null || uv.Length != mesh.vertexCount)
        {
            DrawableSuitsDiagnostics.Warn("Part UV masks unavailable because the baked suit mesh does not expose matching UV coordinates.");
            return;
        }

        for (var i = 0; i < IsolatedSuitParts.Length; i++)
        {
            var part = IsolatedSuitParts[i];
            var mask = new bool[pixelCount];
            if (_partTrianglesBySubmesh.TryGetValue(part, out var submeshes))
            {
                for (var submesh = 0; submesh < submeshes.Length; submesh++)
                {
                    var triangles = submeshes[submesh];
                    for (var triangle = 0; triangle + 2 < triangles.Length; triangle += 3)
                    {
                        RasterizeUvTriangle(mask, texture.width, texture.height, uv[triangles[triangle]], uv[triangles[triangle + 1]], uv[triangles[triangle + 2]]);
                    }
                }
            }
            _partUvMasks[part] = mask;
            var count = 0;
            for (var pixel = 0; pixel < mask.Length; pixel++)
            {
                if (mask[pixel]) count++;
            }
            _partMaskPixelCounts[part] = count;
        }

        var overlapPixels = 0;
        for (var pixel = 0; pixel < pixelCount; pixel++)
        {
            var owners = 0;
            for (var i = 0; i < IsolatedSuitParts.Length; i++)
            {
                if (_partUvMasks[IsolatedSuitParts[i]][pixel]) owners++;
            }
            if (owners > 1) overlapPixels++;
        }
        var overlapWarningThreshold = Mathf.Max(16, pixelCount / 4096);
        _partUvOverlapDetected = overlapPixels > overlapWarningThreshold;
        if (_partUvOverlapDetected)
        {
            DrawableSuitsDiagnostics.Warn($"Part UV masks share {overlapPixels} texture pixels across body parts. Isolation cannot prevent shared-UV surfaces from displaying the same edited pixel.");
        }
        else if (overlapPixels > 0)
        {
            DrawableSuitsDiagnostics.Info($"Part UV masks had {overlapPixels} edge-overlap pixels below warning threshold {overlapWarningThreshold}; treating isolation as clean.");
        }
    }

    private static void RasterizeUvTriangle(bool[] mask, int width, int height, Vector2 a, Vector2 b, Vector2 c)
    {
        var pa = new Vector2(Mathf.Clamp01(a.x) * (width - 1), Mathf.Clamp01(a.y) * (height - 1));
        var pb = new Vector2(Mathf.Clamp01(b.x) * (width - 1), Mathf.Clamp01(b.y) * (height - 1));
        var pc = new Vector2(Mathf.Clamp01(c.x) * (width - 1), Mathf.Clamp01(c.y) * (height - 1));
        var minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(pa.x, Mathf.Min(pb.x, pc.x))));
        var maxX = Mathf.Min(width - 1, Mathf.CeilToInt(Mathf.Max(pa.x, Mathf.Max(pb.x, pc.x))));
        var minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(pa.y, Mathf.Min(pb.y, pc.y))));
        var maxY = Mathf.Min(height - 1, Mathf.CeilToInt(Mathf.Max(pa.y, Mathf.Max(pb.y, pc.y))));
        var denominator = (pb.y - pc.y) * (pa.x - pc.x) + (pc.x - pb.x) * (pa.y - pc.y);
        if (Mathf.Abs(denominator) < 0.00001f)
        {
            return;
        }

        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                var point = new Vector2(x + 0.5f, y + 0.5f);
                var w1 = ((pb.y - pc.y) * (point.x - pc.x) + (pc.x - pb.x) * (point.y - pc.y)) / denominator;
                var w2 = ((pc.y - pa.y) * (point.x - pc.x) + (pa.x - pc.x) * (point.y - pc.y)) / denominator;
                var w3 = 1f - w1 - w2;
                if (w1 >= -0.001f && w2 >= -0.001f && w3 >= -0.001f)
                {
                    mask[y * width + x] = true;
                }
            }
        }
    }

    private int GetPartTriangleCount(SuitPart part)
    {
        return _partTriangleCounts.TryGetValue(part, out var count) ? count : 0;
    }

    private int GetPartMaskPixelCount(SuitPart part)
    {
        return _partMaskPixelCounts.TryGetValue(part, out var count) ? count : 0;
    }

    private void ClearPartIsolationData()
    {
        _partTrianglesBySubmesh.Clear();
        _partUvMasks.Clear();
        _partTriangleCounts.Clear();
        _partMaskPixelCounts.Clear();
        _partDataReady = false;
        _partUvOverlapDetected = false;
        _partDataTextureWidth = 0;
        _partDataTextureHeight = 0;
        _partDataSourceId = 0;
        _partClassifierSource = "none";
        if (_partFilteredPreviewTexture != null)
        {
            Destroy(_partFilteredPreviewTexture);
            _partFilteredPreviewTexture = null;
        }
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
            SetUvFallbackVisible(false);
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

            var proxyReady = UpdateWorldPaintProxy(true);
            _worldPreviewReady = proxyReady && _worldEditorCamera != null && _worldPaintCollider != null;
            if (_worldPreviewReady)
            {
                UpdateWorldEditorCamera(true);
            }
            _hasPreviewCollider = _worldPaintCollider != null;
            _canPaint = texture != null && _worldPreviewReady;
            SetStatus(_canPaint ? "Ready. Third-person paint mode is active." : "Third-person editor opened, but paint proxy is not ready.", !_canPaint);
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

    private bool UpdateWorldPaintProxy(bool forceLog)
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
            var editableTexture = DrawableSuitsPlugin.Registry.GetEditableTexture(_selectedSuitId);
            if (editableTexture != null
                && (!_partDataReady
                    || _partDataSourceId != source.GetInstanceID()
                    || _partDataTextureWidth != editableTexture.width
                    || _partDataTextureHeight != editableTexture.height))
            {
                BuildPartIsolationData(source, _worldPaintMesh, editableTexture, "world proxy");
            }
            ApplySelectedPartTrianglesToProxy(_worldPaintMesh);
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
                DrawableSuitsDiagnostics.Info($"WorldAvatarProxy updated. part={_selectedPart}; partTriangles={GetPartTriangleCount(_selectedPart)}; mesh={_lastWorldProxyMeshSummary}; renderer={DescribeRendererCandidate(source, "source")}; vertices={_worldPaintMesh.vertexCount}; subMeshes={_worldPaintMesh.subMeshCount}; bounds={_worldPaintMesh.bounds}; proxyPos={_worldPaintProxyObject.transform.position}; proxyRot={_worldPaintProxyObject.transform.rotation.eulerAngles}; proxyScale={_worldPaintProxyObject.transform.localScale}; layer={_worldPaintLayer}; rendererEnabled={_worldAvatarRenderer.enabled}; proxyMaterials=[{DescribeMaterials(_worldAvatarRenderer.sharedMaterials)}]; collider={_worldPaintCollider != null}");
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

    private void ApplySelectedPartTrianglesToProxy(Mesh mesh)
    {
        if (mesh == null || !_partDataReady || !_partTrianglesBySubmesh.TryGetValue(_selectedPart, out var submeshes))
        {
            _lastWorldProxyMeshSummary = "full_or_unavailable";
            return;
        }

        if (_selectedPart == SuitPart.All)
        {
            mesh.RecalculateBounds();
            _lastWorldProxyMeshSummary = $"mode=Full; vertices={mesh.vertexCount}; subMeshes={mesh.subMeshCount}; bounds={mesh.bounds}";
            return;
        }

        CompactSelectedPartMesh(mesh, submeshes);
    }

    private void CompactSelectedPartMesh(Mesh mesh, int[][] submeshes)
    {
        var sourceVertices = mesh.vertices;
        var sourceNormals = mesh.normals;
        var sourceTangents = mesh.tangents;
        var sourceUv = mesh.uv;
        var sourceUv2 = mesh.uv2;
        var sourceColors = mesh.colors32;
        var hasNormals = sourceNormals != null && sourceNormals.Length == sourceVertices.Length;
        var hasTangents = sourceTangents != null && sourceTangents.Length == sourceVertices.Length;
        var hasUv = sourceUv != null && sourceUv.Length == sourceVertices.Length;
        var hasUv2 = sourceUv2 != null && sourceUv2.Length == sourceVertices.Length;
        var hasColors = sourceColors != null && sourceColors.Length == sourceVertices.Length;

        var remap = new Dictionary<int, int>();
        var compactVertices = new List<Vector3>();
        var compactNormals = hasNormals ? new List<Vector3>() : null;
        var compactTangents = hasTangents ? new List<Vector4>() : null;
        var compactUv = hasUv ? new List<Vector2>() : null;
        var compactUv2 = hasUv2 ? new List<Vector2>() : null;
        var compactColors = hasColors ? new List<Color32>() : null;
        var compactSubmeshTriangles = new List<int>[submeshes.Length];
        var sourceTriangleCount = 0;
        for (var submesh = 0; submesh < submeshes.Length; submesh++)
        {
            compactSubmeshTriangles[submesh] = new List<int>();
            var triangles = submeshes[submesh] ?? Array.Empty<int>();
            sourceTriangleCount += triangles.Length / 3;
            for (var i = 0; i < triangles.Length; i++)
            {
                var sourceIndex = triangles[i];
                if (sourceIndex < 0 || sourceIndex >= sourceVertices.Length)
                {
                    continue;
                }

                if (!remap.TryGetValue(sourceIndex, out var compactIndex))
                {
                    compactIndex = compactVertices.Count;
                    remap[sourceIndex] = compactIndex;
                    compactVertices.Add(sourceVertices[sourceIndex]);
                    compactNormals?.Add(sourceNormals[sourceIndex]);
                    compactTangents?.Add(sourceTangents[sourceIndex]);
                    compactUv?.Add(sourceUv[sourceIndex]);
                    compactUv2?.Add(sourceUv2[sourceIndex]);
                    compactColors?.Add(sourceColors[sourceIndex]);
                }

                compactSubmeshTriangles[submesh].Add(compactIndex);
            }
        }

        mesh.Clear();
        mesh.SetVertices(compactVertices);
        if (compactNormals != null)
        {
            mesh.SetNormals(compactNormals);
        }
        if (compactTangents != null)
        {
            mesh.SetTangents(compactTangents);
        }
        if (compactUv != null)
        {
            mesh.SetUVs(0, compactUv);
        }
        if (compactUv2 != null)
        {
            mesh.SetUVs(1, compactUv2);
        }
        if (compactColors != null)
        {
            mesh.SetColors(compactColors);
        }
        mesh.subMeshCount = submeshes.Length;
        for (var submesh = 0; submesh < submeshes.Length; submesh++)
        {
            mesh.SetTriangles(compactSubmeshTriangles[submesh], submesh, false);
        }
        if (compactNormals == null || compactNormals.Count != compactVertices.Count)
        {
            mesh.RecalculateNormals();
        }
        mesh.RecalculateBounds();
        _lastWorldProxyMeshSummary = $"mode=Compact; sourceTriangles={sourceTriangleCount}; compactVertices={compactVertices.Count}; subMeshes={submeshes.Length}; bounds={mesh.bounds}";
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

        if (_tool == EditorTool.Decal && _loadedDecal != null)
        {
            _worldBrushMarker.SetActive(false);
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
        DestroyDecalPlacementPreviewResources();
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
        var assignedTexture = texture != null
            ? (_selectedPart == SuitPart.All ? (Texture)texture : BuildFilteredPartPreviewTexture(texture))
            : EnsureCheckerTexture();
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

    private Texture BuildFilteredPartPreviewTexture(Texture2D texture)
    {
        if (texture == null || _selectedPart == SuitPart.All || !_partUvMasks.TryGetValue(_selectedPart, out var mask) || mask == null)
        {
            return texture;
        }

        if (_partFilteredPreviewTexture == null
            || _partFilteredPreviewTexture.width != texture.width
            || _partFilteredPreviewTexture.height != texture.height)
        {
            if (_partFilteredPreviewTexture != null)
            {
                Destroy(_partFilteredPreviewTexture);
            }
            _partFilteredPreviewTexture = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false)
            {
                name = "DrawableSuitsSelectedPartUvPreview",
                filterMode = texture.filterMode,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        var source = texture.GetPixels32();
        var filtered = new Color32[source.Length];
        for (var i = 0; i < source.Length && i < mask.Length; i++)
        {
            filtered[i] = mask[i] ? source[i] : new Color32(0, 0, 0, 0);
        }
        _partFilteredPreviewTexture.SetPixels32(filtered);
        _partFilteredPreviewTexture.Apply(false, false);
        return _partFilteredPreviewTexture;
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
