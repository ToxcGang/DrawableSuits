using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DrawableSuits;

internal static class PauseMenuButtonInjector
{
    private const string ButtonName = "DrawableSuitsButton";
    private const string ButtonText = "DrawableSuits";
    private const float MinimumRowSpacing = 36f;
    private const float FallbackRowSpacing = 92f;

    public static void EnsureButton(QuickMenuManager quickMenu)
    {
        if (quickMenu == null || quickMenu.mainButtonsPanel == null)
        {
            DrawableSuitsDiagnostics.Warn($"EnsureButton skipped. quickMenuNull={quickMenu == null}; mainButtonsPanelNull={quickMenu?.mainButtonsPanel == null}");
            return;
        }

        var panel = quickMenu.mainButtonsPanel.GetComponent<RectTransform>();
        if (panel == null)
        {
            DrawableSuitsDiagnostics.Warn("EnsureButton skipped because QuickMenuManager.mainButtonsPanel has no RectTransform.");
            return;
        }

        var rows = CollectRows(panel);
        DrawableSuitsDiagnostics.Info($"EnsureButton started. menuOpen={quickMenu.isMenuOpen}; panel={panel.name}; rowsFound={rows.Count}; labels=[{DescribeRows(rows)}]");
        var buttonObject = FindExistingButton(panel);
        if (buttonObject == null)
        {
            var template = ChooseTemplateRow(rows);
            DrawableSuitsDiagnostics.Info($"No existing DrawableSuits pause button. template={(template != null ? template.LabelText + "/" + template.GameObject.name : "none")}.");
            buttonObject = template != null
                ? CloneTemplate(template)
                : CreateFallbackButton(panel);
        }
        else
        {
            DrawableSuitsDiagnostics.Info($"Reusing existing DrawableSuits pause button. active={buttonObject.activeSelf}; siblingIndex={buttonObject.transform.GetSiblingIndex()}");
        }

        if (buttonObject == null)
        {
            DrawableSuitsDiagnostics.Warn("Could not create pause-menu DrawableSuits button.");
            return;
        }

        RemoveDuplicateButtons(panel, buttonObject);
        ConfigureButton(buttonObject, quickMenu);
        PlaceButtonAndRows(panel, buttonObject);
        RebuildNavigation(panel);
        var buttonRect = buttonObject.GetComponent<RectTransform>();
        DrawableSuitsDiagnostics.Info($"EnsureButton complete. exists={buttonObject != null}; active={buttonObject.activeSelf}; anchoredPosition={buttonRect?.anchoredPosition.ToString() ?? "null"}; siblingIndex={buttonObject.transform.GetSiblingIndex()}; rowsAfter={CollectRows(panel).Count}");
    }

    public static void SelectIfNeeded(QuickMenuManager quickMenu)
    {
        if (quickMenu == null || !quickMenu.isMenuOpen || quickMenu.mainButtonsPanel == null || EventSystem.current == null)
        {
            return;
        }

            if (EventSystem.current.currentSelectedGameObject != null)
            {
                return;
            }

        var panel = quickMenu.mainButtonsPanel.GetComponent<RectTransform>();
        var rows = GetRowsInVisualOrder(panel);
        if (rows.Count > 0)
        {
            EventSystem.current.SetSelectedGameObject(rows[0].GameObject);
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

    private static void RemoveDuplicateButtons(Transform panel, GameObject keep)
    {
        var buttons = panel.GetComponentsInChildren<Button>(true);
        foreach (var button in buttons)
        {
            if (button == null || button.gameObject == keep || button.gameObject.name != ButtonName)
            {
                continue;
            }

            button.gameObject.SetActive(false);
            Object.Destroy(button.gameObject);
        }
    }

    private static GameObject CloneTemplate(MenuRow template)
    {
        var clone = Object.Instantiate(template.GameObject, template.RectTransform.parent, false);
        clone.name = ButtonName;
        clone.transform.SetAsLastSibling();
        clone.SetActive(true);
        DrawableSuitsDiagnostics.Info($"Cloned pause-menu template '{template.LabelText}' into {ButtonName}.");

        var cloneRect = clone.GetComponent<RectTransform>();
        if (cloneRect != null)
        {
            cloneRect.anchorMin = template.RectTransform.anchorMin;
            cloneRect.anchorMax = template.RectTransform.anchorMax;
            cloneRect.pivot = template.RectTransform.pivot;
            cloneRect.sizeDelta = template.RectTransform.sizeDelta;
            cloneRect.localScale = template.RectTransform.localScale;
        }

        return clone;
    }

    private static GameObject CreateFallbackButton(Transform panel)
    {
        var root = new GameObject(ButtonName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(LayoutElement));
        root.transform.SetParent(panel, false);
        root.transform.SetAsLastSibling();
        DrawableSuitsDiagnostics.Warn("Created fallback pause-menu DrawableSuits button because no cloneable row template was found.");

        var rect = root.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(260f, 54f);

        var image = root.GetComponent<Image>();
        image.color = new Color(0.08f, 0.08f, 0.08f, 0.86f);

        var layout = root.GetComponent<LayoutElement>();
        layout.preferredWidth = 260f;
        layout.preferredHeight = 54f;
        layout.minHeight = 42f;

        var labelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelObject.transform.SetParent(root.transform, false);
        var labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(8f, 2f);
        labelRect.offsetMax = new Vector2(-8f, -2f);

        var label = labelObject.GetComponent<TextMeshProUGUI>();
        label.alignment = TextAlignmentOptions.Center;
        label.fontSize = 24f;
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
        DrawableSuitsDiagnostics.Info($"Configured pause-menu button listener. button={buttonObject.name}; interactable={button.interactable}; targetQuickMenuNull={quickMenu == null}");

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

    private static void PlaceButtonAndRows(RectTransform panel, GameObject buttonObject)
    {
        var rows = CollectRows(panel);
        var drawable = FindRowFor(buttonObject, rows);
        if (drawable == null)
        {
            DrawableSuitsDiagnostics.Warn("PlaceButtonAndRows could not find the DrawableSuits row after collection.");
            return;
        }

        var layoutGroup = panel.GetComponent<LayoutGroup>();
        if (layoutGroup != null && layoutGroup.enabled)
        {
            DrawableSuitsDiagnostics.Info($"PlaceButtonAndRows using layout group {layoutGroup.GetType().Name}.");
            InsertAfterResumeBySibling(rows, drawable);
            LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
            return;
        }

        DrawableSuitsDiagnostics.Info("PlaceButtonAndRows using explicit anchored positions.");
        PlaceRowsExplicitly(rows, drawable);
    }

    private static void InsertAfterResumeBySibling(List<MenuRow> rows, MenuRow drawable)
    {
        var resume = FindResumeRow(rows) ?? FirstNonDrawableRow(rows);
        if (resume == null)
        {
            drawable.RectTransform.SetAsLastSibling();
            return;
        }

        drawable.RectTransform.SetSiblingIndex(resume.RectTransform.GetSiblingIndex() + 1);
    }

    private static void PlaceRowsExplicitly(List<MenuRow> rows, MenuRow drawable)
    {
        var sameParentRows = new List<MenuRow>();
        foreach (var row in rows)
        {
            if (row.RectTransform.parent == drawable.RectTransform.parent)
            {
                sameParentRows.Add(row);
            }
        }

        if (sameParentRows.Count == 0)
        {
            DrawableSuitsDiagnostics.Warn("PlaceRowsExplicitly found no same-parent rows.");
            return;
        }

        var resume = FindResumeRow(sameParentRows) ?? FirstNonDrawableRow(sameParentRows);
        if (resume == null)
        {
            DrawableSuitsDiagnostics.Warn("PlaceRowsExplicitly found no resume or non-Drawable row.");
            return;
        }

        CopyRectTemplate(resume.RectTransform, drawable.RectTransform);

        sameParentRows.Sort(CompareTopToBottom);
        var order = new List<MenuRow>();
        foreach (var row in sameParentRows)
        {
            if (!row.IsDrawable)
            {
                order.Add(row);
            }
        }

        var resumeIndex = order.IndexOf(resume);
        if (resumeIndex < 0)
        {
            resumeIndex = 0;
        }

        order.Insert(Mathf.Min(resumeIndex + 1, order.Count), drawable);

        var spacing = DetectRowSpacing(sameParentRows);
        var topY = order[0].RectTransform.anchoredPosition.y;
        var firstSibling = order[0].RectTransform.GetSiblingIndex();
        DrawableSuitsDiagnostics.Info($"PlaceRowsExplicitly orderCount={order.Count}; resume={resume.LabelText}; spacing={spacing}; topY={topY}; firstSibling={firstSibling}");
        for (var i = 0; i < order.Count; i++)
        {
            var row = order[i];
            var position = row.RectTransform.anchoredPosition;
            position.y = topY - i * spacing;
            if (row.IsDrawable)
            {
                position.x = resume.RectTransform.anchoredPosition.x;
            }

            row.RectTransform.anchoredPosition = position;
            row.RectTransform.SetSiblingIndex(firstSibling + i);
        }
    }

    private static void CopyRectTemplate(RectTransform source, RectTransform target)
    {
        target.anchorMin = source.anchorMin;
        target.anchorMax = source.anchorMax;
        target.pivot = source.pivot;
        target.sizeDelta = source.sizeDelta;
        target.localScale = source.localScale;
    }

    private static float DetectRowSpacing(List<MenuRow> rows)
    {
        var yValues = new List<float>();
        foreach (var row in rows)
        {
            yValues.Add(row.RectTransform.anchoredPosition.y);
        }

        yValues.Sort((a, b) => b.CompareTo(a));
        var diffs = new List<float>();
        for (var i = 0; i < yValues.Count - 1; i++)
        {
            var diff = Mathf.Abs(yValues[i] - yValues[i + 1]);
            if (diff >= MinimumRowSpacing && diff <= 240f)
            {
                diffs.Add(diff);
            }
        }

        if (diffs.Count > 0)
        {
            diffs.Sort();
            return Mathf.Max(MinimumRowSpacing, diffs[diffs.Count / 2]);
        }

        var maxHeight = 0f;
        foreach (var row in rows)
        {
            maxHeight = Mathf.Max(maxHeight, row.RectTransform.rect.height, row.RectTransform.sizeDelta.y);
        }

        return Mathf.Max(FallbackRowSpacing, maxHeight + 24f);
    }

    private static void RebuildNavigation(RectTransform panel)
    {
        var rows = GetRowsInVisualOrder(panel);
        if (rows.Count == 0)
        {
            DrawableSuitsDiagnostics.Warn("RebuildNavigation skipped because no rows were collected.");
            return;
        }

        for (var i = 0; i < rows.Count; i++)
        {
            var button = rows[i].Button;
            var navigation = button.navigation;
            navigation.mode = Navigation.Mode.Explicit;
            navigation.selectOnUp = i > 0 ? rows[i - 1].Button : null;
            navigation.selectOnDown = i < rows.Count - 1 ? rows[i + 1].Button : null;
            navigation.selectOnLeft = null;
            navigation.selectOnRight = null;
            button.navigation = navigation;
        }
        DrawableSuitsDiagnostics.Info($"RebuildNavigation complete. rowCount={rows.Count}; rows=[{DescribeRows(rows)}]");
    }

    private static List<MenuRow> GetRowsInVisualOrder(RectTransform panel)
    {
        var rows = CollectRows(panel);
        rows.Sort(CompareTopToBottom);
        return rows;
    }

    private static List<MenuRow> CollectRows(Transform root)
    {
        var result = new List<MenuRow>();
        if (root == null)
        {
            return result;
        }

        var buttons = root.GetComponentsInChildren<Button>(true);
        foreach (var button in buttons)
        {
            if (button == null || !button.gameObject.activeSelf)
            {
                continue;
            }

            var rectTransform = button.GetComponent<RectTransform>();
            if (rectTransform == null)
            {
                continue;
            }

            var labelText = GetLabelText(button.gameObject);
            if (string.IsNullOrWhiteSpace(labelText) && button.gameObject.name != ButtonName)
            {
                continue;
            }

            result.Add(new MenuRow(button.gameObject, rectTransform, button, labelText));
        }

        return result;
    }

    private static string GetLabelText(GameObject gameObject)
    {
        var tmp = gameObject.GetComponentInChildren<TextMeshProUGUI>(true);
        if (tmp != null)
        {
            return tmp.text ?? string.Empty;
        }

        var text = gameObject.GetComponentInChildren<Text>(true);
        return text != null ? text.text ?? string.Empty : string.Empty;
    }

    private static MenuRow ChooseTemplateRow(List<MenuRow> rows)
    {
        return FindRowContaining(rows, "settings")
            ?? FindRowContaining(rows, "invite")
            ?? FindRowContaining(rows, "lethalconfig")
            ?? FirstNonDrawableRow(rows);
    }

    private static MenuRow FindResumeRow(List<MenuRow> rows)
    {
        return FindRowContaining(rows, "resume");
    }

    private static MenuRow FindRowContaining(List<MenuRow> rows, string token)
    {
        foreach (var row in rows)
        {
            if (!row.IsDrawable && Normalize(row.LabelText).Contains(token))
            {
                return row;
            }
        }

        return null;
    }

    private static MenuRow FirstNonDrawableRow(List<MenuRow> rows)
    {
        foreach (var row in rows)
        {
            if (!row.IsDrawable)
            {
                return row;
            }
        }

        return null;
    }

    private static MenuRow FindRowFor(GameObject gameObject, List<MenuRow> rows)
    {
        foreach (var row in rows)
        {
            if (row.GameObject == gameObject)
            {
                return row;
            }
        }

        return null;
    }

    private static int CompareTopToBottom(MenuRow a, MenuRow b)
    {
        var yCompare = b.RectTransform.anchoredPosition.y.CompareTo(a.RectTransform.anchoredPosition.y);
        return yCompare != 0 ? yCompare : a.RectTransform.GetSiblingIndex().CompareTo(b.RectTransform.GetSiblingIndex());
    }

    private static string Normalize(string value)
    {
        return (value ?? string.Empty).Replace(" ", string.Empty).Replace("\n", string.Empty).ToLowerInvariant();
    }

    private static void OpenEditorFromPauseMenu(QuickMenuManager quickMenu)
    {
        DrawableSuitsDiagnostics.Info($"Pause-menu DrawableSuits button clicked. quickMenuNull={quickMenu == null}; menuOpenBeforeClose={quickMenu?.isMenuOpen}; cursorVisible={Cursor.visible}; cursorLock={Cursor.lockState}");
        try
        {
            quickMenu?.CloseQuickMenu();
            DrawableSuitsDiagnostics.Info($"Quick menu close requested. menuOpenAfterClose={quickMenu?.isMenuOpen}");
        }
        catch (System.Exception ex)
        {
            DrawableSuitsDiagnostics.Exception("Failed to close quick menu before opening editor", ex);
        }

        var runtimeReady = DrawableSuitsPlugin.EnsureRuntimeReady("PauseMenuButton.Click");
        var editorBefore = DrawableSuitsPlugin.DescribeUnityObject(DrawableSuitsPlugin.Editor);
        if (runtimeReady && DrawableSuitsPlugin.Editor != null)
        {
            DrawableSuitsPlugin.Editor.OpenFromPauseMenuNextFrame(quickMenu);
            DrawableSuitsDiagnostics.Info($"Pause-menu delayed open scheduled. runtimeReady={runtimeReady}; editorBefore={editorBefore}; editorAfter={DrawableSuitsPlugin.DescribeUnityObject(DrawableSuitsPlugin.Editor)}; cursorVisible={Cursor.visible}; cursorLock={Cursor.lockState}");
        }
        else
        {
            DrawableSuitsDiagnostics.Warn($"Pause-menu open could not be scheduled. runtimeReady={runtimeReady}; editorBefore={editorBefore}; editorAfter={DrawableSuitsPlugin.DescribeUnityObject(DrawableSuitsPlugin.Editor)}");
        }
    }

    private static string DescribeRows(List<MenuRow> rows)
    {
        if (rows == null || rows.Count == 0)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        foreach (var row in rows)
        {
            parts.Add($"{row.LabelText}/{row.GameObject.name}@{row.RectTransform.anchoredPosition}");
        }

        return string.Join(", ", parts);
    }

    private sealed class MenuRow
    {
        public MenuRow(GameObject gameObject, RectTransform rectTransform, Button button, string labelText)
        {
            GameObject = gameObject;
            RectTransform = rectTransform;
            Button = button;
            LabelText = labelText ?? string.Empty;
        }

        public GameObject GameObject { get; }
        public RectTransform RectTransform { get; }
        public Button Button { get; }
        public string LabelText { get; }
        public bool IsDrawable => GameObject.name == ButtonName || Normalize(LabelText).Contains("drawablesuits");
    }
}
