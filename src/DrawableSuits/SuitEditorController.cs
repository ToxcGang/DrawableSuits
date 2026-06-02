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
        FillBucket,
        Decal,
        Eyedropper,
        Text
    }

    private enum CursorVisualMode
    {
        Dot,
        BrushRing
    }

    private const int EditorCanvasSortingOrder = 32760;
    private const float DotCursorRootSize = 17f;
    private const float DotCursorBackSize = 15f;
    private const float DotCursorFrontSize = 9f;

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
    private EditorTool _previousToolBeforeEyedropper = EditorTool.Paint;
    private Color _brushColor = Color.red;
    private float _brushSize = 16f;
    private float _brushOpacity = 1f;
    private float _fillTolerance = 0.08f;
    private float _decalSize = 128f;
    private float _decalRotation;
    private string _textStampValue = "TEXT";
    private float _textSize = 96f;
    private float _textRotation;
    private bool _mirrorEnabled;
    private bool _strokeActive;
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
    private Button _mirrorButton;
    private Button _applyButton;
    private Button _saveButton;
    private Button _loadButton;
    private Button _resetButton;
    private Button _exportCodeButton;
    private Button _importCodeButton;
    private Button _uvFallbackButton;
    private GameObject _designCodePanelObject;
    private InputField _designCodeInput;
    private Text _designCodeStatusLabel;
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
        internal int SurfaceHits;
        internal int RasterizedCells;
        internal int SeamSkippedCells;
        internal int OffSuitSamples;
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
        ResetNativeCursor("plugin destroy");
        DestroyCursorGraphics();
        if (_textStampRenderer != null)
        {
            _textStampRenderer.Destroy();
            _textStampRenderer = null;
            _textStampTexture = null;
        }

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
        HandleControllerCursor();
        UpdateCanvasCursor(false, "update");
        if (IsWorldThirdPersonMode)
        {
            UpdateWorldPaintProxy(false);
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
        CloseDesignCodePanel();
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

        CreateAnchoredText(panel.transform, "ToolHeader", "Tool", 16, FontStyle.Bold, TextAnchor.MiddleLeft, new Rect(leftX, 326f, leftW, 24f), Color.white);
        _paintButton = CreateAnchoredButton(panel.transform, "Paint", new Rect(leftX, 354f, 64f, 30f), () => SetTool(EditorTool.Paint));
        _eraseButton = CreateAnchoredButton(panel.transform, "Erase", new Rect(leftX + 70f, 354f, 64f, 30f), () => SetTool(EditorTool.Erase));
        _fillButton = CreateAnchoredButton(panel.transform, "Fill", new Rect(leftX + 140f, 354f, 64f, 30f), () => SetTool(EditorTool.FillBucket));
        _mirrorButton = CreateAnchoredButton(panel.transform, "Mirror", new Rect(leftX + 210f, 354f, 64f, 30f), ToggleMirror);
        _decalButton = CreateAnchoredButton(panel.transform, "Decal", new Rect(leftX, 390f, 64f, 30f), () => SetTool(EditorTool.Decal));
        _textButton = CreateAnchoredButton(panel.transform, "Text", new Rect(leftX + 70f, 390f, 64f, 30f), () => SetTool(EditorTool.Text));
        _eyedropperButton = CreateAnchoredButton(panel.transform, "Eyedropper", new Rect(leftX + 140f, 390f, 134f, 30f), () => SetTool(EditorTool.Eyedropper));

        CreateAnchoredText(panel.transform, "BrushHeader", "Brush", 16, FontStyle.Bold, TextAnchor.MiddleLeft, new Rect(leftX, 438f, leftW, 24f), Color.white);
        _brushSizeLabel = CreateAnchoredText(panel.transform, "BrushSizeLabel", string.Empty, 14, FontStyle.Normal, TextAnchor.MiddleLeft, new Rect(leftX, 468f, 94f, 24f), Color.white);
        _brushSizeSlider = CreateAnchoredSlider(panel.transform, "BrushSize", 1f, 96f, _brushSize, new Rect(leftX + 100f, 470f, 174f, 24f), value => _brushSize = value);
        _brushOpacityLabel = CreateAnchoredText(panel.transform, "BrushOpacityLabel", string.Empty, 14, FontStyle.Normal, TextAnchor.MiddleLeft, new Rect(leftX, 502f, 94f, 24f), Color.white);
        _brushOpacitySlider = CreateAnchoredSlider(panel.transform, "BrushOpacity", 0.05f, 1f, _brushOpacity, new Rect(leftX + 100f, 504f, 174f, 24f), value => _brushOpacity = value);
        _fillToleranceLabel = CreateAnchoredText(panel.transform, "FillToleranceLabel", string.Empty, 14, FontStyle.Normal, TextAnchor.MiddleLeft, new Rect(leftX, 536f, 104f, 24f), Color.white);
        _fillToleranceSlider = CreateAnchoredSlider(panel.transform, "FillTolerance", 0f, 0.5f, _fillTolerance, new Rect(leftX + 110f, 538f, 164f, 24f), value => _fillTolerance = value);

        CreateAnchoredText(panel.transform, "ColorHeader", "Color", 16, FontStyle.Bold, TextAnchor.MiddleLeft, new Rect(leftX, 574f, leftW, 24f), Color.white);
        _colorPicker = CreateAnchoredColorPicker(panel.transform, new Rect(leftX, 598f, leftW, 104f), _brushColor, color =>
        {
            _brushColor = color;
            UpdateColorUi();
        }, out _colorSwatch, out _colorHexInput);
        _colorHexInput.onValueChanged.AddListener(PreviewHexInput);
        _colorHexInput.onEndEdit.AddListener(ApplyHexInput);

        _uvFallbackButton = CreateAnchoredButton(panel.transform, "Use UV Fallback", new Rect(rightX, 54f, 150f, 34f), ToggleUvFallback);
        CreateAnchoredText(panel.transform, "WorldHelp", "Third-person mode: aim at the visible suit and hold left mouse or right trigger to paint. Eyedropper samples once, then returns to the previous tool. Right mouse/right stick or bumpers orbit. Wheel or D-pad up/down zooms.", 13, FontStyle.Normal, TextAnchor.UpperLeft, new Rect(rightX, 96f, rightW, 76f), new Color(0.86f, 0.9f, 0.94f, 1f));

        _placementHeaderLabel = CreateAnchoredText(panel.transform, "PlacementHeader", "Decal", 16, FontStyle.Bold, TextAnchor.MiddleLeft, new Rect(rightX, 188f, rightW, 24f), Color.white);
        _decalSizeLabel = CreateAnchoredText(panel.transform, "DecalSizeLabel", string.Empty, 14, FontStyle.Normal, TextAnchor.MiddleLeft, new Rect(rightX, 218f, 112f, 24f), Color.white);
        _decalSizeSlider = CreateAnchoredSlider(panel.transform, "DecalSize", 16f, 512f, _decalSize, new Rect(rightX + 120f, 220f, 160f, 24f), value =>
        {
            if (_tool == EditorTool.Text)
            {
                _textSize = value;
            }
            else
            {
                _decalSize = value;
            }
            InvalidateDecalPreview("placement size changed");
        });
        _decalRotationLabel = CreateAnchoredText(panel.transform, "DecalRotationLabel", string.Empty, 14, FontStyle.Normal, TextAnchor.MiddleLeft, new Rect(rightX, 252f, 112f, 24f), Color.white);
        _decalRotationSlider = CreateAnchoredSlider(panel.transform, "DecalRotation", -180f, 180f, _decalRotation, new Rect(rightX + 120f, 254f, 160f, 24f), value =>
        {
            if (_tool == EditorTool.Text)
            {
                _textRotation = value;
            }
            else
            {
                _decalRotation = value;
            }
            InvalidateDecalPreview("placement rotation changed");
        });
        _textStampInput = CreateAnchoredInputField(panel.transform, "TextStampInput", _textStampValue, new Rect(rightX, 290f, 142f, 34f));
        _textStampInput.characterLimit = 64;
        _textStampInput.lineType = InputField.LineType.SingleLine;
        _textStampInput.onValueChanged.AddListener(value =>
        {
            _textStampValue = NormalizeTextStampValue(value);
            InvalidateTextStampTexture("text input changed");
            InvalidateDecalPreview("text input changed");
        });
        CreateAnchoredButton(panel.transform, "Refresh Decals", new Rect(rightX + 150f, 290f, 136f, 34f), ImportDecalFromDialog);
        _decalListContent = CreateAnchoredScrollList(panel.transform, "DecalList", new Rect(rightX, 334f, rightW, 84f));

        CreateAnchoredText(panel.transform, "DesignHeader", "Design Name", 16, FontStyle.Bold, TextAnchor.MiddleLeft, new Rect(rightX, 590f, rightW, 24f), Color.white);
        _designNameInput = CreateAnchoredInputField(panel.transform, _designName, new Rect(rightX, 620f, rightW, 34f));
        _designNameInput.onValueChanged.AddListener(value => _designName = value);

        _exportCodeButton = CreateAnchoredButton(panel.transform, "Export Code", new Rect(rightX, 662f, 138f, 34f), () => OpenDesignCodePanel(true));
        _importCodeButton = CreateAnchoredButton(panel.transform, "Import Code", new Rect(rightX + 148f, 662f, 138f, 34f), () => OpenDesignCodePanel(false));

        CreateAnchoredButton(panel.transform, "Undo", new Rect(rightX, 708f, 84f, 34f), Undo);
        CreateAnchoredButton(panel.transform, "Redo", new Rect(rightX + 92f, 708f, 84f, 34f), Redo);
        _resetButton = CreateAnchoredButton(panel.transform, "Reset", new Rect(rightX + 184f, 708f, 84f, 34f), () =>
        {
            SaveUndo();
            DrawableSuitsPlugin.Registry.ResetSuit(_selectedSuitId);
            _redo.Clear();
            InvalidateDecalPreview("reset");
            UpdateUiState();
        });

        _applyButton = CreateAnchoredButton(panel.transform, "Apply", new Rect(rightX, 750f, 84f, 34f), () => DrawableSuitsPlugin.Registry.ApplyEditedTexture(_selectedSuitId, _designName, true));
        _saveButton = CreateAnchoredButton(panel.transform, "Save", new Rect(rightX + 92f, 750f, 84f, 34f), SaveDesign);
        _loadButton = CreateAnchoredButton(panel.transform, "Load", new Rect(rightX + 184f, 750f, 84f, 34f), LoadSelectedDesign);

        CreateAnchoredText(panel.transform, "SavedDesignsHeader", "Saved Designs", 16, FontStyle.Bold, TextAnchor.MiddleLeft, new Rect(rightX, 804f, rightW, 24f), Color.white);
        _designListContent = CreateAnchoredScrollList(panel.transform, "DesignList", new Rect(rightX, 834f, rightW, 106f));

        var fallbackPreview = CreateUiObject("PreviewViewport", panel.transform, typeof(RectTransform), typeof(Image));
        _previewViewportRect = fallbackPreview.GetComponent<RectTransform>();
        SetAnchoredRect(_previewViewportRect, new Rect(rightX, 430f, rightW, 142f));
        fallbackPreview.transform.SetSiblingIndex(_decalListContent != null ? _decalListContent.GetSiblingIndex() + 1 : fallbackPreview.transform.GetSiblingIndex());
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

        CreateAnchoredText(panel.transform, "ControllerHelp", "Controller: View/Back+Y open/close, left stick cursor, A clicks UI, RT paints or samples with Eyedropper, right stick/bumpers orbit, D-pad up/down zooms, X undo, Start save.", 13, FontStyle.Normal, TextAnchor.UpperLeft, new Rect(leftX, 954f, 574f, 36f), Color.white);

        BuildDesignCodePanel();

        _editorCanvasObject.SetActive(false);
        RefreshListButtons();
        UpdateUiState();
        RebuildSelectableNavigation();
        LogEditorControlTree(panel.transform);
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
        dialogImage.color = new Color(0.025f, 0.03f, 0.035f, 0.98f);
        dialogImage.raycastTarget = true;

        CreateAnchoredText(dialog.transform, "DesignCodeTitle", "Design Code Import / Export", 22, FontStyle.Bold, TextAnchor.MiddleLeft, new Rect(18f, 14f, 560f, 34f), new Color(1f, 0.62f, 0.25f, 1f));
        CreateAnchoredText(dialog.transform, "DesignCodeHelp", "Export creates a compact shareable DSUIT2 code for the current editable texture. Import accepts DSUIT2 or legacy DSUIT1 codes into the current suit only; press Save or Apply when ready.", 14, FontStyle.Normal, TextAnchor.UpperLeft, new Rect(18f, 54f, 724f, 48f), Color.white);

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

        _designCodeStatusLabel = CreateAnchoredText(dialog.transform, "DesignCodeStatus", string.Empty, 13, FontStyle.Normal, TextAnchor.UpperLeft, new Rect(18f, 366f, 724f, 46f), new Color(1f, 0.74f, 0.42f, 1f));
        _designCodePanelObject.SetActive(false);
        DrawableSuitsDiagnostics.Info("DesignCodePanel built.");
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
        canvas.sortingOrder = EditorCanvasSortingOrder;

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

        _editorCanvasObject.SetActive(false);
        DrawableSuitsDiagnostics.Info("Fallback diagnostics canvas built.");
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
        SetInteractable(_mirrorButton, _canPaint && hasEditableTexture);
        SetInteractable(_applyButton, _canPaint && hasEditableTexture);
        SetInteractable(_saveButton, hasEditableTexture);
        SetInteractable(_exportCodeButton, hasEditableTexture);
        SetInteractable(_importCodeButton, hasEditableTexture);
        SetInteractable(_resetButton, hasEditableTexture);
        SetInteractable(_loadButton, hasEditableTexture && _selectedDesignIndex >= 0);
        if (_uvFallbackButton != null)
        {
            var showFallbackButton = _uvFallbackMode || !IsWorldThirdPersonMode;
            _uvFallbackButton.gameObject.SetActive(showFallbackButton);
            SetButtonLabel(_uvFallbackButton, _uvFallbackMode ? "Use Third Person" : "UV Panel Active");
        }

        UpdateToolButtons();
        UpdateLabels();
        UpdateColorUi();
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
            $"Tool: {_tool}",
            $"Mirror enabled: {_mirrorEnabled}",
            $"Mirror map: {(_mirrorSurfaceMap != null ? $"{_mirrorSurfaceMap.TriangleCount} tris" : "none")}",
            $"Texture-only mode: {_uvFallbackMode}",
            $"UV panel visible: {_previewViewportRect != null && _previewViewportRect.gameObject.activeSelf}",
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
        var fillActive = _tool == EditorTool.FillBucket;
        if (_brushSizeLabel != null)
        {
            _brushSizeLabel.text = $"Size: {Mathf.RoundToInt(_brushSize)} px";
            _brushSizeLabel.gameObject.SetActive(!fillActive);
        }
        if (_brushSizeSlider != null)
        {
            _brushSizeSlider.gameObject.SetActive(!fillActive);
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
        if (_placementHeaderLabel != null) _placementHeaderLabel.text = _tool == EditorTool.Text ? "Text" : "Decal";
        if (_decalSizeLabel != null) _decalSizeLabel.text = _tool == EditorTool.Text ? $"Height: {Mathf.RoundToInt(_textSize)} px" : $"Size: {Mathf.RoundToInt(_decalSize)} px";
        if (_decalRotationLabel != null) _decalRotationLabel.text = $"Rotation: {Mathf.RoundToInt(CurrentPlacementRotation())} deg";
    }

    private float CurrentPlacementSize()
    {
        return _tool == EditorTool.Text ? _textSize : _decalSize;
    }

    private float CurrentPlacementRotation()
    {
        return _tool == EditorTool.Text ? _textRotation : _decalRotation;
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
        SetToolButtonColor(_fillButton, _tool == EditorTool.FillBucket);
        SetToolButtonColor(_decalButton, _tool == EditorTool.Decal);
        SetToolButtonColor(_eyedropperButton, _tool == EditorTool.Eyedropper);
        SetToolButtonColor(_textButton, _tool == EditorTool.Text);
        SetToolButtonColor(_mirrorButton, _mirrorEnabled);
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
        if (tool == EditorTool.Decal)
        {
            InvalidateDecalPreview("decal tool selected");
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
        return tool == EditorTool.Paint || tool == EditorTool.Erase || tool == EditorTool.FillBucket || tool == EditorTool.Decal || tool == EditorTool.Text;
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
            mode = CursorVisualMode.BrushRing;
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
            mode = CursorVisualMode.BrushRing;
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

        var rootSize = mode == CursorVisualMode.BrushRing
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

        if (IsDesignCodePanelOpen())
        {
            targetMode = "DesignCodePanel";
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

            if (!TryGetWorldPaintHit(out var hit, false))
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

        var scaleX = screenSize.x / Mathf.Max(1f, texture.width);
        var scaleY = screenSize.y / Mathf.Max(1f, texture.height);
        return ScreenPixelsToCanvasUnits(_brushSize * 2f * Mathf.Max(scaleX, scaleY));
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
        var du = _brushSize / Mathf.Max(1f, texture.width);
        var dv = _brushSize / Mathf.Max(1f, texture.height);
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

    private bool IsDesignCodePanelOpen()
    {
        return _designCodePanelObject != null && _designCodePanelObject.activeInHierarchy;
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
        var key = $"mode={mode}|tool={_tool}|source={_pointerSource}|target={targetMode}|brush={Mathf.RoundToInt(_brushSize)}|diameter={diameter:0.#}|tri={triangleIndex}|uv={uv.x:0.###},{uv.y:0.###}|fallback={fallbackReason}|screen={screenPosition.x:0.#},{screenPosition.y:0.#}|local={localPosition.x:0.#},{localPosition.y:0.#}|size={rootSize:0.#}|context={context}";
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

        if (!TryGetActivePlacementStamp(out var stampTexture, out var stampFailure))
        {
            if (_tool == EditorTool.Text && string.Equals(stampFailure, "empty text", StringComparison.OrdinalIgnoreCase))
            {
                SetStatus("Enter text before stamping.", false);
            }
            HideDecalPlacementPreview(stampFailure, false);
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
            ShowUvPlacementPreview(texture, panelUv, panelMirrorTarget, stampTexture, "TexturePanel");
            return;
        }

        if (IsWorldThirdPersonMode)
        {
            if (IsCursorOverEditorPanel() || !TryGetWorldPaintHit(out var hit, false))
            {
                HideDecalPlacementPreview("world miss", false);
                return;
            }

            var mirrorTarget = ResolveWorldMirrorTarget(texture, hit, false);
            if (_tool == EditorTool.Text)
            {
                ShowWorldTextPlacementPreview(texture, hit, mirrorTarget, stampTexture);
                return;
            }

            ShowWorldPlacementPreview(texture, hit, mirrorTarget, stampTexture);
            return;
        }

        if (!TryGetTexturePreviewUv(_cursor, out var uv) || !IsCursorOverPreviewViewport())
        {
            HideDecalPlacementPreview("uv miss", false);
            return;
        }

        var uvMirrorTarget = ResolveUvMirrorTarget(texture, uv, false);
        ShowUvPlacementPreview(texture, uv, uvMirrorTarget, stampTexture, "TextureFallback");
    }

    private bool IsPlacementTool(EditorTool tool)
    {
        return tool == EditorTool.Decal || tool == EditorTool.Text;
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

            stampTexture = _loadedDecal;
            return true;
        }

        if (_tool == EditorTool.Text)
        {
            return TryGetTextStampTexture(out stampTexture, out failureReason);
        }

        failureReason = "not a placement tool";
        return false;
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
        if (!string.Equals(key, _lastDecalPreviewKey, StringComparison.Ordinal))
        {
            _decalPreviewTexture.SetPixels32(sourceTexture.GetPixels32());
            var touchedPixels = mirrorTarget.Enabled && mirrorTarget.Available ? new HashSet<int>() : null;
            var primaryChanged = CompositeDecalSurfaceStamp(_decalPreviewTexture, stampTexture, hit.point, hit.normal, _decalRotation, false, Mathf.Clamp01(_brushOpacity * 0.62f), touchedPixels, out primaryStats);
            var mirrorChanged = false;
            if (ShouldApplyMirror(sourceTexture, hit.textureCoord, mirrorTarget) && TryGetMirrorWorldPlacement(mirrorTarget, out var mirrorPoint, out var mirrorNormal))
            {
                mirrorChanged = CompositeDecalSurfaceStamp(_decalPreviewTexture, stampTexture, mirrorPoint, mirrorNormal, -_decalRotation, true, Mathf.Clamp01(_brushOpacity * 0.62f), touchedPixels, out mirrorStats);
            }

            if (!primaryChanged && !mirrorChanged)
            {
                HideDecalPlacementPreview("decal surface preview projected no pixels", false);
                LogDecalSurfacePreviewSkipped(sourceTexture, hit, mirrorTarget, stampTexture, primaryStats, mirrorStats);
                return;
            }

            _decalPreviewTexture.Apply(false, false);
            _lastDecalPreviewKey = key;
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
            LogDecalSurfacePreviewUpdated(sourceTexture, hit, mirrorTarget, stampTexture, primaryStats, mirrorStats);
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

        ConfigureUvPlacementPreview(_uvDecalPreviewRect, _uvDecalPreviewImage, sourceTexture, stampTexture, uv, false);

        if (ShouldApplyMirror(sourceTexture, uv, mirrorTarget))
        {
            ConfigureUvPlacementPreview(_uvMirrorDecalPreviewRect, _uvMirrorDecalPreviewImage, sourceTexture, stampTexture, mirrorTarget.Uv, true);
        }
        else if (_uvMirrorDecalPreviewRect != null)
        {
            _uvMirrorDecalPreviewRect.gameObject.SetActive(false);
        }

        _decalPreviewVisible = true;
        _placementPreviewTool = _tool;
        _lastDecalPreviewKey = BuildDecalPreviewKey(mode, sourceTexture, uv, mirrorTarget);
        SetPlacementPreviewStatus(mirrorTarget);
        LogPlacementPreviewUpdated(mode, sourceTexture, uv, mirrorTarget, stampTexture, false);
    }

    private void ConfigureUvPlacementPreview(RectTransform previewRect, RawImage previewImage, Texture2D sourceTexture, Texture2D stampTexture, Vector2 uv, bool mirrored)
    {
        if (previewRect == null || previewImage == null || _previewViewportRect == null || sourceTexture == null || stampTexture == null)
        {
            return;
        }

        var rect = _previewViewportRect.rect;
        var localX = Mathf.Lerp(rect.xMin, rect.xMax, uv.x);
        var localY = Mathf.Lerp(rect.yMin, rect.yMax, uv.y);
        var stampSize = GetPlacementStampPixelSize(stampTexture);
        var displayWidth = Mathf.Clamp(stampSize.x / Mathf.Max(1f, sourceTexture.width) * rect.width, 4f, rect.width * 1.5f);
        var displayHeight = Mathf.Clamp(stampSize.y / Mathf.Max(1f, sourceTexture.height) * rect.height, 4f, rect.height * 1.5f);

        previewImage.texture = stampTexture;
        previewImage.color = _tool == EditorTool.Text
            ? new Color(_brushColor.r, _brushColor.g, _brushColor.b, mirrored ? 0.5f : 0.62f)
            : mirrored ? new Color(1f, 1f, 1f, 0.5f) : new Color(1f, 1f, 1f, 0.62f);
        previewImage.raycastTarget = false;
        previewImage.uvRect = mirrored ? new Rect(1f, 0f, -1f, 1f) : new Rect(0f, 0f, 1f, 1f);
        previewRect.anchoredPosition = new Vector2(localX, localY);
        previewRect.sizeDelta = new Vector2(displayWidth, displayHeight);
        previewRect.localRotation = Quaternion.Euler(0f, 0f, mirrored ? -CurrentPlacementRotation() : CurrentPlacementRotation());
        previewRect.gameObject.SetActive(true);
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
            || _statusMessage.StartsWith("Previewing text", StringComparison.OrdinalIgnoreCase))
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
        var pointKey = $"{Mathf.RoundToInt(hit.point.x * 1000f)},{Mathf.RoundToInt(hit.point.y * 1000f)},{Mathf.RoundToInt(hit.point.z * 1000f)}";
        var normalKey = $"{Mathf.RoundToInt(hit.normal.x * 1000f)},{Mathf.RoundToInt(hit.normal.y * 1000f)},{Mathf.RoundToInt(hit.normal.z * 1000f)}";
        return $"{_decalPreviewSerial}|{mode}|tool={_tool}|suit={_selectedSuitId}|pixel={px},{py}|point={pointKey}|normal={normalKey}|mirror={DescribeMirrorTarget(sourceTexture, mirrorTarget)}|size={Mathf.RoundToInt(CurrentPlacementSize())}|rot={Mathf.RoundToInt(CurrentPlacementRotation() * 10f)}|opacity={Mathf.RoundToInt(_brushOpacity * 1000f)}|stamp={CurrentPlacementName()}|stampKey={_textStampTextureKey}|color={ColorToHex(_brushColor)}|texture={sourceTexture?.width ?? 0}x{sourceTexture?.height ?? 0}";
    }

    private void SetPlacementPreviewStatus(MirrorPaintTarget mirrorTarget)
    {
        var noun = _tool == EditorTool.Text ? "text" : "decal";
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

        return new Vector2(Mathf.Max(1f, _decalSize), Mathf.Max(1f, _decalSize));
    }

    private string CurrentPlacementName()
    {
        return _tool == EditorTool.Text
            ? $"text:{NormalizeTextStampValue(_textStampValue).Length}"
            : CurrentDecalName();
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

    private void LogPlacementPreviewHidden(string reason, bool force)
    {
        if (_placementPreviewTool == EditorTool.Text || _tool == EditorTool.Text)
        {
            LogTextPreviewHidden(reason, force);
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

    private void LogTextSurfaceStampCommitted(Texture2D texture, RaycastHit hit, MirrorPaintTarget mirrorTarget, Texture2D stampTexture, TextSurfaceStampStats primaryStats, TextSurfaceStampStats mirrorStats)
    {
        if (texture == null)
        {
            return;
        }

        var pixel = TexturePixel(texture, hit.textureCoord);
        DrawableSuitsDiagnostics.Info($"TextSurfaceStampCommitted: mode=WorldThirdPerson; pointerSource={_pointerSource}; uv={hit.textureCoord}; pixel={pixel.x},{pixel.y}; {DescribeMirrorTarget(texture, mirrorTarget)}; textLength={NormalizeTextStampValue(_textStampValue).Length}; fontSize={Mathf.RoundToInt(_textSize)}; rotation={Mathf.RoundToInt(_textRotation)}; color={ColorToHex(_brushColor)}; opacity={_brushOpacity:0.##}; generatedTexture={stampTexture?.width ?? 0}x{stampTexture?.height ?? 0}; hitPoint={hit.point}; hitNormal={hit.normal}; primaryWritten={primaryStats.WrittenPixels}; primarySkipped={primaryStats.SkippedPixels}; primaryAlpha={primaryStats.AlphaPixels}; primaryWorldSize={primaryStats.WorldWidth:0.###}x{primaryStats.WorldHeight:0.###}; mirrorWritten={mirrorStats.WrittenPixels}; mirrorSkipped={mirrorStats.SkippedPixels}; mirrorAlpha={mirrorStats.AlphaPixels}; mirrorWorldSize={mirrorStats.WorldWidth:0.###}x{mirrorStats.WorldHeight:0.###}");
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

    private string CurrentDecalName()
    {
        return _selectedDecalIndex >= 0 && _selectedDecalIndex < _decalFiles.Count
            ? Path.GetFileName(_decalFiles[_selectedDecalIndex])
            : "none";
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
            var cursorOverTexturePanel = IsCursorOverPreviewViewport();
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

            var worldScroll = DrawableSuitsInput.MouseScrollY();
            if (cursorOverTexturePanel && Mathf.Abs(worldScroll) > 0.01f)
            {
                _brushSize = Mathf.Clamp(_brushSize + worldScroll * 2f, 1f, 96f);
                if (_brushSizeSlider != null)
                {
                    _brushSizeSlider.SetValue(_brushSize, false);
                }
                return;
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

        if (_designCodePanelObject != null && _designCodePanelObject.activeInHierarchy)
        {
            _strokeActive = false;
            _decalStampArmed = true;
            _suppressPaintInputUntilRelease = false;
            _suppressDecalPreviewUntilRelease = false;
            HideDecalPlacementPreview("design code panel open", false);
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
            SaveUndo();
            _redo.Clear();
            _strokeActive = true;
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
        var hitAvailable = canTargetWorld && TryGetWorldPaintHit(out hit, true);
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
            SaveUndo();
            _redo.Clear();
            _strokeActive = true;
        }

        PaintWorldSurfaceBrush(texture, hit, mirrorTarget);
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

        SaveUndo();
        _redo.Clear();

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
            if (_undo.Count > 0)
            {
                _undo.Pop();
            }
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
                SetStatus(_tool == EditorTool.Text ? "Aim at your visible suit to stamp text." : "Aim at your visible suit to stamp the decal.", false);
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
            else
            {
                SetStatus("Enter text before stamping.", false);
                LogTextStampSkipped(mode, failureReason);
            }
            return;
        }

        SaveUndo();
        _redo.Clear();
        if (PaintAtCursor(texture, uv, mirrorTarget))
        {
            if (_tool == EditorTool.Text)
            {
                LogTextStampCommitted(mode, texture, uv, mirrorTarget, stampTexture);
            }
            else
            {
                LogDecalStampCommitted(mode, texture, uv, mirrorTarget);
            }
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

        if (_loadedDecal == null)
        {
            WarnMissingDecal("WorldThirdPerson surface stamp");
            _tool = EditorTool.Paint;
            UpdateToolButtons();
            return;
        }

        SaveUndo();
        _redo.Clear();
        var touchedPixels = mirrorTarget.Enabled && mirrorTarget.Available ? new HashSet<int>() : null;
        var primaryChanged = CompositeDecalSurfaceStamp(texture, _loadedDecal, hit.point, hit.normal, _decalRotation, false, _brushOpacity, touchedPixels, out var primaryStats);
        var mirrorChanged = false;
        TextSurfaceStampStats mirrorStats = default;
        if (ShouldApplyMirror(texture, hit.textureCoord, mirrorTarget) && TryGetMirrorWorldPlacement(mirrorTarget, out var mirrorPoint, out var mirrorNormal))
        {
            mirrorChanged = CompositeDecalSurfaceStamp(texture, _loadedDecal, mirrorPoint, mirrorNormal, -_decalRotation, true, _brushOpacity, touchedPixels, out mirrorStats);
        }

        if (!primaryChanged && !mirrorChanged)
        {
            LogDecalSurfaceStampSkipped("WorldThirdPerson", "surface projection wrote no pixels", texture, hit, mirrorTarget, _loadedDecal, primaryStats, mirrorStats);
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
        LogDecalSurfaceStampCommitted(texture, hit, mirrorTarget, _loadedDecal, primaryStats, mirrorStats);
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

        SaveUndo();
        _redo.Clear();
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
        LogTextSurfaceStampCommitted(texture, hit, mirrorTarget, stampTexture, primaryStats, mirrorStats);
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
        var key = $"{reason}|tool={_tool}|over={overPreview}|uv={uvAvailable}:{uv}|pixel={pixel}|{mirror}|brush={Mathf.RoundToInt(_brushSize)}|opacity={_brushOpacity:0.##}|decal={_loadedDecal != null}|source={_pointerSource}";
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

    private void LogPaintApplied(Texture2D texture, Vector2 uv, MirrorPaintTarget mirrorTarget)
    {
        var px = Mathf.RoundToInt(uv.x * (texture.width - 1));
        var py = Mathf.RoundToInt(uv.y * (texture.height - 1));
        var key = $"applied|tool={_tool}|pixel={px},{py}|{DescribeMirrorTarget(texture, mirrorTarget)}|brush={Mathf.RoundToInt(_brushSize)}|opacity={_brushOpacity:0.##}|decal={_loadedDecal != null}";
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
        var key = $"applied|tool={_tool}|pixel={pixel.x},{pixel.y}|mirror={DescribeMirrorTarget(texture, mirrorTarget)}|brush={Mathf.RoundToInt(_brushSize)}|opacity={_brushOpacity:0.##}|primaryWritten={primaryStats.WrittenPixels}|mirrorWritten={mirrorStats.WrittenPixels}|primaryCells={primaryStats.RasterizedCells}|mirrorCells={mirrorStats.RasterizedCells}|primarySeams={primaryStats.SeamSkippedCells}|mirrorSeams={mirrorStats.SeamSkippedCells}|radius={primaryRadius:0.####}";
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
        var key = $"skipped|reason={reason}|tool={_tool}|pixel={pixel.x},{pixel.y}|mirror={DescribeMirrorTarget(texture, mirrorTarget)}|brush={Mathf.RoundToInt(_brushSize)}|primaryWritten={primaryStats.WrittenPixels}|mirrorWritten={mirrorStats.WrittenPixels}|primaryCells={primaryStats.RasterizedCells}|mirrorCells={mirrorStats.RasterizedCells}";
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
        switch (_tool)
        {
            case EditorTool.Paint:
                changed = PaintCircle(texture, uv, _brushColor, _brushSize, _brushOpacity, touchedPixels);
                if (applyMirror)
                {
                    changed |= PaintCircle(texture, mirrorTarget.Uv, _brushColor, _brushSize, _brushOpacity, touchedPixels);
                }
                break;
            case EditorTool.Erase:
                changed = EraseCircle(texture, uv, _brushSize, _brushOpacity, touchedPixels);
                if (applyMirror)
                {
                    changed |= EraseCircle(texture, mirrorTarget.Uv, _brushSize, _brushOpacity, touchedPixels);
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

        LogPaintApplied(texture, uv, mirrorTarget);
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
        var du = _brushSize / Mathf.Max(1f, texture.width);
        var dv = _brushSize / Mathf.Max(1f, texture.height);
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
        var radius = (_brushSize / textureScale) * boundsHeight;
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
        var sampleDiameter = Mathf.Clamp(Mathf.CeilToInt(_brushSize * 1.25f), 10, 96);
        var gridWidth = sampleDiameter + 1;
        var gridHeight = sampleDiameter + 1;
        var grid = new SurfaceStampGridSample[gridWidth * gridHeight];
        var rayOffset = Mathf.Max(0.08f, worldRadius * 3f);
        var rayDistance = rayOffset * 2.5f;
        var projectedSamples = new Dictionary<int, SurfaceStampSample>();
        var seamThreshold = GetBrushProjectionSeamThreshold(target);
        var opacity01 = Mathf.Clamp01(_brushOpacity);

        for (var gy = 0; gy < gridHeight; gy++)
        {
            var v = gy / Mathf.Max(1f, sampleDiameter);
            var localY = (v - 0.5f) * frame.WorldHeight;
            for (var gx = 0; gx < gridWidth; gx++)
            {
                var u = gx / Mathf.Max(1f, sampleDiameter);
                var localX = (u - 0.5f) * frame.WorldWidth;
                var normalizedDistance = Mathf.Sqrt((localX * localX) + (localY * localY)) / Mathf.Max(0.0001f, worldRadius);
                if (normalizedDistance > 1f)
                {
                    continue;
                }

                stats.ProjectionSamples++;
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

                var alpha = _tool == EditorTool.Erase
                    ? opacity01
                    : opacity01 * Mathf.Clamp01(1f - normalizedDistance + 0.25f);
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

        var key = $"tool={_tool}|mirrored={mirrored}|sample={sampleDiameter}|seam={stats.SeamSkippedCells}|cells={stats.RasterizedCells}|threshold={Mathf.RoundToInt(seamThreshold)}|brush={Mathf.RoundToInt(_brushSize)}";
        if (Time.unscaledTime - _lastBrushSurfaceWarningTime < 2f && string.Equals(key, _lastBrushSurfaceWarningKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastBrushSurfaceWarningTime = Time.unscaledTime;
        _lastBrushSurfaceWarningKey = key;
        DrawableSuitsDiagnostics.Warn($"BrushSurfaceProjectionWarning: {key}; projectionSamples={stats.ProjectionSamples}; surfaceHits={stats.SurfaceHits}; offSuit={stats.OffSuitSamples}; written={stats.WrittenPixels}; skipped={stats.SkippedPixels}; pointerSource={_pointerSource}");
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
        if (_loadedDecal == null)
        {
            WarnMissingDecal("ApplyDecal");
            return false;
        }

        return CompositePlacementStamp(target, _loadedDecal, uv, _decalSize, mirrored ? -_decalRotation : _decalRotation, _brushOpacity, mirrored, touchedPixels);
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

    private bool CompositeTextSurfaceStamp(Texture2D target, Texture2D stamp, Vector3 center, Vector3 normal, float rotation, bool mirrored, float opacity, HashSet<int> touchedPixels, out TextSurfaceStampStats stats)
    {
        return CompositeSurfaceStamp(target, stamp, center, normal, rotation, CalculateTextWorldHeight(), mirrored, opacity, touchedPixels, true, out stats);
    }

    private bool CompositeDecalSurfaceStamp(Texture2D target, Texture2D stamp, Vector3 center, Vector3 normal, float rotation, bool mirrored, float opacity, HashSet<int> touchedPixels, out TextSurfaceStampStats stats)
    {
        var sampleHeight = Mathf.Clamp(Mathf.RoundToInt(_decalSize), 12, 160);
        var aspect = stamp != null ? stamp.width / Mathf.Max(1f, (float)stamp.height) : 1f;
        var sampleWidth = Mathf.Clamp(Mathf.RoundToInt(sampleHeight * aspect), 12, 256);
        return CompositeDecalSurfaceCoverageStamp(target, stamp, center, normal, rotation, CalculateDecalWorldHeight(), mirrored, opacity, touchedPixels, sampleWidth, sampleHeight, out stats);
    }

    private bool CompositeDecalSurfaceCoverageStamp(Texture2D target, Texture2D stamp, Vector3 center, Vector3 normal, float rotation, float worldHeight, bool mirrored, float opacity, HashSet<int> touchedPixels, int sampleWidth, int sampleHeight, out TextSurfaceStampStats stats)
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
                AddProjectedStampPixel(target, stamp, sample.StampUv, sample.Pixel, opacity01, projectedSamples, ref stats);
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
                RasterizeProjectedDecalTriangle(target, stamp, s00, s10, s01, opacity01, projectedSamples, ref stats);
                RasterizeProjectedDecalTriangle(target, stamp, s10, s11, s01, opacity01, projectedSamples, ref stats);
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

    private static void RasterizeProjectedDecalTriangle(Texture2D target, Texture2D stamp, SurfaceStampGridSample a, SurfaceStampGridSample b, SurfaceStampGridSample c, float opacity, Dictionary<int, SurfaceStampSample> projectedSamples, ref TextSurfaceStampStats stats)
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
                AddProjectedStampPixel(target, stamp, stampUv, new Vector2(x, y), opacity, projectedSamples, ref stats);
            }
        }
    }

    private static void AddProjectedStampPixel(Texture2D target, Texture2D stamp, Vector2 stampUv, Vector2 pixel, float opacity, Dictionary<int, SurfaceStampSample> projectedSamples, ref TextSurfaceStampStats stats)
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
            Color = sample,
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
        RefreshTexturePanelPreview("Undo", false);
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
        RefreshTexturePanelPreview("Redo", false);
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

    private void OpenDesignCodePanel(bool exportCurrent)
    {
        if (_designCodePanelObject == null)
        {
            return;
        }

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
            SaveUndo();
            if (!DrawableSuitsPlugin.Registry.ImportDecodedDesignCode(_selectedSuitId, payload, texture, out var importedDesignName, out failureReason))
            {
                SetDesignCodeStatus($"Import failed: {failureReason}");
                DrawableSuitsDiagnostics.Warn($"DesignCodeImportFailed: stage=apply; selectedSuitId={_selectedSuitId}; format={info.FormatVersion}; designName={info.DesignName}; sourceSuitId={info.SourceSuitId}; texture={info.Width}x{info.Height}; pngBytes={info.PngBytes}; compressedBytes={info.CompressedBytes}; codeLength={info.CodeLength}; reason={failureReason}");
                return;
            }

            _designName = importedDesignName;
            if (_designNameInput != null)
            {
                _designNameInput.text = _designName;
            }

            _redo.Clear();
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
        DrawableSuitsPlugin.Registry.GetOrCreateState(_selectedSuitId);
        _undo.Clear();
        _redo.Clear();
        DrawableSuitsDiagnostics.Info($"SelectSuit selected suitId={_selectedSuitId}; name={DrawableSuitsPlugin.Registry.GetSuitName(_selectedSuitId)}");
        InvalidateDecalPreview("select suit");
        InvalidateMirrorSurfaceMap("select suit");
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
            || context.IndexOf("SelectSuit", StringComparison.OrdinalIgnoreCase) >= 0
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

            var proxyReady = UpdateWorldPaintProxy(true);
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
                DrawableSuitsDiagnostics.Info($"WorldAvatarProxy updated. mesh={_lastWorldProxyMeshSummary}; renderer={DescribeRendererCandidate(source, "source")}; vertices={_worldPaintMesh.vertexCount}; subMeshes={_worldPaintMesh.subMeshCount}; bounds={_worldPaintMesh.bounds}; proxyPos={_worldPaintProxyObject.transform.position}; proxyRot={_worldPaintProxyObject.transform.rotation.eulerAngles}; proxyScale={_worldPaintProxyObject.transform.localScale}; layer={_worldPaintLayer}; rendererEnabled={_worldAvatarRenderer.enabled}; proxyMaterials=[{DescribeMaterials(_worldAvatarRenderer.sharedMaterials)}]; collider={_worldPaintCollider != null}");
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
            _previewImage.uvRect = texture != null ? new Rect(0f, 0f, 1f, 1f) : new Rect(0f, 0f, 10f, 10f);
            _previewImage.color = Color.white;
            _previewImage.raycastTarget = true;
        }

        var panelMode = IsWorldThirdPersonMode ? "TexturePanel" : _previewMode;
        var assignment = $"{panelMode}; visible={visible}; assigned={assignedTexture?.name ?? "null"}; editable={DescribeEditableTexture()}; rawImage={DescribePreviewImageTexture()}";
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
