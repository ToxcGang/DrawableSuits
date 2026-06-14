using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace DrawableSuits;

internal sealed class DrawableSuitsRuntimeHost : MonoBehaviour
{
    private bool _firstUpdateLogged;
    private bool _controllerOpenChordWasHeld;

    private void Awake()
    {
        DrawableSuitsDiagnostics.Info($"RuntimeHost Awake. host={DrawableSuitsPlugin.DescribeUnityObject(this)}; gameObject={DrawableSuitsPlugin.DescribeUnityObject(gameObject)}");
    }

    private void Start()
    {
        DrawableSuitsDiagnostics.Info($"RuntimeHost Start. editor={DrawableSuitsPlugin.DescribeUnityObject(DrawableSuitsPlugin.Editor)}");
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
    }

    private void HandleInput()
    {
        if (DrawableSuitsInput.WasKeyPressed(Key.F10, DrawableSuitsPlugin.ModConfig.EmergencyOpenKey.Value))
        {
            DrawableSuitsDiagnostics.Info($"RuntimeHost F10 emergency open. editor={DrawableSuitsPlugin.DescribeUnityObject(DrawableSuitsPlugin.Editor)}");
            DrawableSuitsPlugin.RequestOpenEditor("F10Emergency");
        }

        if (DrawableSuitsInput.WasKeyPressed(Key.F8, DrawableSuitsPlugin.ModConfig.OpenEditorKey.Value))
        {
            DrawableSuitsDiagnostics.Info($"RuntimeHost F8 toggle. editor={DrawableSuitsPlugin.DescribeUnityObject(DrawableSuitsPlugin.Editor)}");
            RequestGameplayToggle("F8Keyboard");
        }

        if (WasControllerOpenChordPressed())
        {
            DrawableSuitsDiagnostics.Info($"RuntimeHost controller open chord. editor={DrawableSuitsPlugin.DescribeUnityObject(DrawableSuitsPlugin.Editor)}");
            RequestGameplayToggle("ControllerViewBackY");
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
