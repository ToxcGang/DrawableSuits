using System;
using System.Collections.Generic;
using System.IO;
using GameNetcodeStuff;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

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
    private Rect _windowRect = new(24, 24, 360, 640);
    private Vector2 _cursor;
    private Vector2 _designScroll;
    private Vector2 _decalScroll;
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

    private GameObject _previewRoot;
    private Mesh _previewMesh;
    private MeshCollider _previewCollider;
    private MeshRenderer _previewRenderer;
    private float _previewYaw;
    private float _previewScale = 0.9f;

    private Texture2D _loadedDecal;

    private void Start()
    {
        _cursor = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        RefreshFileLists();
    }

    private void Update()
    {
        if (UnityEngine.Input.GetKeyDown(DrawableSuitsPlugin.ModConfig.OpenEditorKey.Value) || WasControllerOpenPressed())
        {
            SetOpen(!_isOpen);
        }

        if (!_isOpen)
        {
            return;
        }

        if (UnityEngine.Input.GetKeyDown(KeyCode.Escape) || WasGamepadPressed(g => g.buttonEast))
        {
            SetOpen(false);
            return;
        }

        HandleControllerCursor();
        HandleEditorShortcuts();
        UpdatePreviewTransform();
        HandlePaintingInput();
    }

    private void OnGUI()
    {
        if (!_isOpen)
        {
            return;
        }

        GUI.depth = 0;
        _windowRect = GUI.Window(667110, _windowRect, DrawWindow, "DrawableSuits");
        DrawVirtualCursor();
    }

    private void SetOpen(bool value)
    {
        _isOpen = value;
        if (_isOpen)
        {
            var localSuitId = DrawableSuitsPlugin.Registry.GetLocalSuitId();
            _selectedSuitId = localSuitId >= 0 ? localSuitId : FirstKnownSuitId();
            DrawableSuitsPlugin.Registry.GetOrCreateState(_selectedSuitId);
            _cursor = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            RefreshFileLists();
            RebuildPreview();
        }
        else
        {
            DestroyPreview();
            _strokeActive = false;
        }
    }

    private void DrawWindow(int id)
    {
        GUILayout.Label($"Suit: {DrawableSuitsPlugin.Registry.GetSuitName(_selectedSuitId)} ({_selectedSuitId})");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Previous"))
        {
            SelectAdjacentSuit(-1);
        }
        if (GUILayout.Button("Use Current"))
        {
            SelectSuit(DrawableSuitsPlugin.Registry.GetLocalSuitId());
        }
        if (GUILayout.Button("Next"))
        {
            SelectAdjacentSuit(1);
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(6);
        GUILayout.Label("Tool");
        _tool = (EditorTool)GUILayout.SelectionGrid((int)_tool, new[] { "Paint", "Erase", "Decal" }, 3);

        GUILayout.Space(6);
        GUILayout.Label("Brush");
        _brushSize = GUILayout.HorizontalSlider(_brushSize, 1f, 96f);
        GUILayout.Label($"Size: {Mathf.RoundToInt(_brushSize)} px");
        _brushOpacity = GUILayout.HorizontalSlider(_brushOpacity, 0.05f, 1f);
        GUILayout.Label($"Opacity: {Mathf.RoundToInt(_brushOpacity * 100f)}%");
        DrawColorControls();

        GUILayout.Space(6);
        GUILayout.Label("Decal");
        _decalSize = GUILayout.HorizontalSlider(_decalSize, 16f, 512f);
        GUILayout.Label($"Size: {Mathf.RoundToInt(_decalSize)} px");
        _decalRotation = GUILayout.HorizontalSlider(_decalRotation, -180f, 180f);
        GUILayout.Label($"Rotation: {Mathf.RoundToInt(_decalRotation)} deg");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Refresh"))
        {
            RefreshFileLists();
        }
        if (GUILayout.Button("Import File"))
        {
            ImportDecalFromDialog();
        }
        GUILayout.EndHorizontal();
        DrawDecalList();

        GUILayout.Space(6);
        GUILayout.Label("Design Name");
        _designName = GUILayout.TextField(_designName, 64);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Undo"))
        {
            Undo();
        }
        if (GUILayout.Button("Redo"))
        {
            Redo();
        }
        if (GUILayout.Button("Reset"))
        {
            SaveUndo();
            DrawableSuitsPlugin.Registry.ResetSuit(_selectedSuitId);
        }
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Apply"))
        {
            DrawableSuitsPlugin.Registry.ApplyEditedTexture(_selectedSuitId, _designName, true);
        }
        if (GUILayout.Button("Save"))
        {
            SaveDesign();
        }
        if (GUILayout.Button("Load"))
        {
            LoadSelectedDesign();
        }
        GUILayout.EndHorizontal();

        DrawDesignList();

        GUILayout.Space(8);
        GUILayout.Label("Controller: View/Back+Y open, left stick cursor, right trigger paint, bumpers rotate, Y tool, X undo, Start save, A apply.");
        GUI.DragWindow(new Rect(0, 0, 10000, 20));
    }

    private void DrawColorControls()
    {
        GUILayout.Label("Color");
        _brushColor.r = GUILayout.HorizontalSlider(_brushColor.r, 0f, 1f);
        _brushColor.g = GUILayout.HorizontalSlider(_brushColor.g, 0f, 1f);
        _brushColor.b = GUILayout.HorizontalSlider(_brushColor.b, 0f, 1f);
        var oldColor = GUI.color;
        GUI.color = _brushColor;
        GUILayout.Box(" ", GUILayout.Height(18));
        GUI.color = oldColor;
    }

    private void DrawDesignList()
    {
        GUILayout.Label("Saved Designs");
        _designScroll = GUILayout.BeginScrollView(_designScroll, GUILayout.Height(92));
        for (var i = 0; i < _designFiles.Count; i++)
        {
            var selected = i == _selectedDesignIndex;
            if (GUILayout.Toggle(selected, Path.GetFileNameWithoutExtension(_designFiles[i]), "Button"))
            {
                _selectedDesignIndex = i;
            }
        }
        GUILayout.EndScrollView();
    }

    private void DrawDecalList()
    {
        _decalScroll = GUILayout.BeginScrollView(_decalScroll, GUILayout.Height(80));
        for (var i = 0; i < _decalFiles.Count; i++)
        {
            var selected = i == _selectedDecalIndex;
            if (GUILayout.Toggle(selected, Path.GetFileName(_decalFiles[i]), "Button"))
            {
                SelectDecal(i);
            }
        }
        GUILayout.EndScrollView();
    }

    private void DrawVirtualCursor()
    {
        var rect = new Rect(_cursor.x - 6f, Screen.height - _cursor.y - 6f, 12f, 12f);
        var previous = GUI.color;
        GUI.color = _tool == EditorTool.Erase ? Color.white : _brushColor;
        GUI.Box(rect, GUIContent.none);
        GUI.color = previous;
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
                _tool = (EditorTool)(((int)_tool + 1) % Enum.GetValues(typeof(EditorTool)).Length);
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

        if (_windowRect.Contains(new Vector2(_cursor.x, Screen.height - _cursor.y)))
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
        _tool = EditorTool.Decal;
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

    private static bool WasControllerOpenPressed()
    {
        var gamepad = Gamepad.current;
        return gamepad != null && gamepad.selectButton.isPressed && gamepad.buttonNorth.wasPressedThisFrame;
    }

    private static bool WasGamepadPressed(Func<Gamepad, ButtonControl> accessor)
    {
        var gamepad = Gamepad.current;
        return gamepad != null && accessor(gamepad).wasPressedThisFrame;
    }
}
