using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DrawableSuits;

internal static class PauseMenuButtonInjector
{
    private const string ButtonName = "DrawableSuitsButton";
    private const string ButtonText = "DrawableSuits";

    public static void EnsureButton(QuickMenuManager quickMenu)
    {
        if (quickMenu == null || quickMenu.mainButtonsPanel == null)
        {
            return;
        }

        var panel = quickMenu.mainButtonsPanel.transform;
        var buttonObject = FindExistingButton(panel);
        if (buttonObject == null)
        {
            buttonObject = CloneExistingButton(panel) ?? CreateFallbackButton(panel);
        }

        if (buttonObject == null)
        {
            DrawableSuitsPlugin.ModLogger.LogWarning("Could not create pause-menu DrawableSuits button.");
            return;
        }

        ConfigureButton(buttonObject, quickMenu);
    }

    public static void SelectIfNeeded(QuickMenuManager quickMenu)
    {
        if (quickMenu == null || !quickMenu.isMenuOpen || EventSystem.current == null)
        {
            return;
        }

        if (EventSystem.current.currentSelectedGameObject != null)
        {
            return;
        }

        var buttonObject = quickMenu.mainButtonsPanel != null
            ? FindExistingButton(quickMenu.mainButtonsPanel.transform)
            : null;
        if (buttonObject != null)
        {
            EventSystem.current.SetSelectedGameObject(buttonObject);
        }
    }

    private static GameObject FindExistingButton(Transform panel)
    {
        var buttons = panel.GetComponentsInChildren<Button>(true);
        foreach (var button in buttons)
        {
            if (button != null && button.gameObject.name == ButtonName)
            {
                return button.gameObject;
            }
        }

        return null;
    }

    private static GameObject CloneExistingButton(Transform panel)
    {
        var buttons = panel.GetComponentsInChildren<Button>(true);
        foreach (var button in buttons)
        {
            if (button == null || button.gameObject.name == ButtonName)
            {
                continue;
            }

            var clone = Object.Instantiate(button.gameObject, panel, false);
            clone.name = ButtonName;
            clone.transform.SetAsLastSibling();
            clone.SetActive(true);
            return clone;
        }

        return null;
    }

    private static GameObject CreateFallbackButton(Transform panel)
    {
        var root = new GameObject(ButtonName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(LayoutElement));
        root.transform.SetParent(panel, false);
        root.transform.SetAsLastSibling();

        var rect = root.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(220f, 42f);

        var image = root.GetComponent<Image>();
        image.color = new Color(0.08f, 0.08f, 0.08f, 0.86f);

        var layout = root.GetComponent<LayoutElement>();
        layout.preferredWidth = 220f;
        layout.preferredHeight = 42f;
        layout.minHeight = 36f;

        var labelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelObject.transform.SetParent(root.transform, false);
        var labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(8f, 2f);
        labelRect.offsetMax = new Vector2(-8f, -2f);

        var label = labelObject.GetComponent<TextMeshProUGUI>();
        label.alignment = TextAlignmentOptions.Center;
        label.fontSize = 20f;
        label.color = Color.white;

        var templateLabel = panel.GetComponentInChildren<TextMeshProUGUI>(true);
        if (templateLabel != null)
        {
            label.font = templateLabel.font;
            label.fontSharedMaterial = templateLabel.fontSharedMaterial;
            label.fontSize = templateLabel.fontSize;
            label.color = templateLabel.color;
        }

        return root;
    }

    private static void ConfigureButton(GameObject buttonObject, QuickMenuManager quickMenu)
    {
        buttonObject.name = ButtonName;
        buttonObject.SetActive(true);
        SetButtonText(buttonObject);

        var button = buttonObject.GetComponent<Button>() ?? buttonObject.AddComponent<Button>();
        button.interactable = true;
        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.AddListener(() => OpenEditorFromPauseMenu(quickMenu));

        var navigation = button.navigation;
        navigation.mode = Navigation.Mode.Automatic;
        button.navigation = navigation;
    }

    private static void SetButtonText(GameObject buttonObject)
    {
        foreach (var label in buttonObject.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            label.text = ButtonText;
            label.enableWordWrapping = false;
        }

        foreach (var label in buttonObject.GetComponentsInChildren<Text>(true))
        {
            label.text = ButtonText;
        }
    }

    private static void OpenEditorFromPauseMenu(QuickMenuManager quickMenu)
    {
        try
        {
            quickMenu?.CloseQuickMenu();
        }
        catch (System.Exception ex)
        {
            DrawableSuitsPlugin.ModLogger.LogWarning($"Failed to close quick menu before opening editor: {ex.Message}");
        }

        DrawableSuitsPlugin.Editor?.OpenEditor();
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }
}
