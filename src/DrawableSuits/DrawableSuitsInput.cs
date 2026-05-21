using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DrawableSuits;

internal static class DrawableSuitsInput
{
    private static bool _legacyUnavailable;
    private static bool _legacyUnavailableLogged;

    internal static bool WasKeyPressed(Key defaultKey, KeyCode configuredKey)
    {
        var key = MapKeyCode(configuredKey, defaultKey);
        if (Keyboard.current != null)
        {
            return WasInputSystemKeyPressed(key);
        }

        return WasLegacyKeyPressedOnceGuarded(configuredKey);
    }

    internal static bool WasKeyPressed(Key key)
    {
        return WasInputSystemKeyPressed(key);
    }

    internal static bool IsKeyPressed(Key key)
    {
        try
        {
            var keyboard = Keyboard.current;
            return keyboard != null && keyboard[key].isPressed;
        }
        catch (Exception ex)
        {
            DrawableSuitsDiagnostics.Exception($"Input System key held polling failed for {key}", ex);
            return false;
        }
    }

    internal static bool TryGetMousePosition(out Vector2 position)
    {
        try
        {
            var mouse = Mouse.current;
            if (mouse != null)
            {
                position = mouse.position.ReadValue();
                return true;
            }
        }
        catch (Exception ex)
        {
            DrawableSuitsDiagnostics.Exception("Input System mouse position polling failed", ex);
        }

        position = default;
        return false;
    }

    internal static bool IsLeftMousePressed()
    {
        return IsMouseButtonPressed(mouse => mouse.leftButton.isPressed, "left");
    }

    internal static bool IsRightMousePressed()
    {
        return IsMouseButtonPressed(mouse => mouse.rightButton.isPressed, "right");
    }

    internal static float MouseDeltaX()
    {
        try
        {
            var mouse = Mouse.current;
            return mouse != null ? mouse.delta.ReadValue().x : 0f;
        }
        catch (Exception ex)
        {
            DrawableSuitsDiagnostics.Exception("Input System mouse delta polling failed", ex);
            return 0f;
        }
    }

    internal static float MouseDeltaY()
    {
        try
        {
            var mouse = Mouse.current;
            return mouse != null ? mouse.delta.ReadValue().y : 0f;
        }
        catch (Exception ex)
        {
            DrawableSuitsDiagnostics.Exception("Input System mouse delta polling failed", ex);
            return 0f;
        }
    }
    internal static bool WasMouseUsedThisFrame()
    {
        try
        {
            var mouse = Mouse.current;
            if (mouse == null)
            {
                return false;
            }

            return mouse.delta.ReadValue().sqrMagnitude > 0.01f
                || mouse.leftButton.isPressed
                || mouse.rightButton.isPressed
                || Mathf.Abs(mouse.scroll.ReadValue().y) > 0.01f;
        }
        catch (Exception ex)
        {
            DrawableSuitsDiagnostics.Exception("Input System mouse activity polling failed", ex);
            return false;
        }
    }

    internal static float MouseScrollY()
    {
        try
        {
            var mouse = Mouse.current;
            if (mouse == null)
            {
                return 0f;
            }

            var scroll = mouse.scroll.ReadValue().y;
            return Mathf.Abs(scroll) > 10f ? scroll / 120f : scroll;
        }
        catch (Exception ex)
        {
            DrawableSuitsDiagnostics.Exception("Input System mouse scroll polling failed", ex);
            return 0f;
        }
    }

    private static bool IsMouseButtonPressed(Func<Mouse, bool> accessor, string buttonName)
    {
        try
        {
            var mouse = Mouse.current;
            return mouse != null && accessor(mouse);
        }
        catch (Exception ex)
        {
            DrawableSuitsDiagnostics.Exception($"Input System {buttonName} mouse button polling failed", ex);
            return false;
        }
    }

    private static bool WasInputSystemKeyPressed(Key key)
    {
        try
        {
            var keyboard = Keyboard.current;
            return keyboard != null && keyboard[key].wasPressedThisFrame;
        }
        catch (Exception ex)
        {
            DrawableSuitsDiagnostics.Exception($"Input System key polling failed for {key}", ex);
            return false;
        }
    }

    private static bool WasLegacyKeyPressedOnceGuarded(KeyCode key)
    {
        if (_legacyUnavailable)
        {
            return false;
        }

        try
        {
            return Input.GetKeyDown(key);
        }
        catch (InvalidOperationException ex)
        {
            _legacyUnavailable = true;
            if (!_legacyUnavailableLogged)
            {
                _legacyUnavailableLogged = true;
                DrawableSuitsDiagnostics.Warn($"Legacy UnityEngine.Input is disabled; using Unity Input System only. First legacy failure for {key}: {ex.Message}");
            }

            return false;
        }
        catch (Exception ex)
        {
            DrawableSuitsDiagnostics.Exception($"Legacy key polling failed once for {key}", ex);
            return false;
        }
    }

    private static Key MapKeyCode(KeyCode keyCode, Key fallback)
    {
        if (TryMapSpecialKey(keyCode, out var mapped))
        {
            return mapped;
        }

        if (Enum.TryParse<Key>(keyCode.ToString(), true, out mapped))
        {
            return mapped;
        }

        return fallback;
    }

    private static bool TryMapSpecialKey(KeyCode keyCode, out Key key)
    {
        switch (keyCode)
        {
            case KeyCode.Alpha0: key = Key.Digit0; return true;
            case KeyCode.Alpha1: key = Key.Digit1; return true;
            case KeyCode.Alpha2: key = Key.Digit2; return true;
            case KeyCode.Alpha3: key = Key.Digit3; return true;
            case KeyCode.Alpha4: key = Key.Digit4; return true;
            case KeyCode.Alpha5: key = Key.Digit5; return true;
            case KeyCode.Alpha6: key = Key.Digit6; return true;
            case KeyCode.Alpha7: key = Key.Digit7; return true;
            case KeyCode.Alpha8: key = Key.Digit8; return true;
            case KeyCode.Alpha9: key = Key.Digit9; return true;
            case KeyCode.Return: key = Key.Enter; return true;
            case KeyCode.Escape: key = Key.Escape; return true;
            case KeyCode.Backspace: key = Key.Backspace; return true;
            case KeyCode.Tab: key = Key.Tab; return true;
            case KeyCode.Space: key = Key.Space; return true;
            case KeyCode.LeftShift: key = Key.LeftShift; return true;
            case KeyCode.RightShift: key = Key.RightShift; return true;
            case KeyCode.LeftControl: key = Key.LeftCtrl; return true;
            case KeyCode.RightControl: key = Key.RightCtrl; return true;
            case KeyCode.LeftAlt: key = Key.LeftAlt; return true;
            case KeyCode.RightAlt: key = Key.RightAlt; return true;
            case KeyCode.UpArrow: key = Key.UpArrow; return true;
            case KeyCode.DownArrow: key = Key.DownArrow; return true;
            case KeyCode.LeftArrow: key = Key.LeftArrow; return true;
            case KeyCode.RightArrow: key = Key.RightArrow; return true;
            case KeyCode.Delete: key = Key.Delete; return true;
            case KeyCode.Insert: key = Key.Insert; return true;
            case KeyCode.Home: key = Key.Home; return true;
            case KeyCode.End: key = Key.End; return true;
            case KeyCode.PageUp: key = Key.PageUp; return true;
            case KeyCode.PageDown: key = Key.PageDown; return true;
            default:
                key = Key.None;
                return false;
        }
    }
}
