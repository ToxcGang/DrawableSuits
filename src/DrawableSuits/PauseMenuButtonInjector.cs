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
    private const string ButtonTextWithInlinePrefix = "> DrawableSuits";
    private const string PrefixText = ">";
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
        var template = ChooseTemplateRow(rows);
        DrawableSuitsDiagnostics.Info($"Pause-menu DrawableSuits template selected: label={template?.LabelText ?? "none"}; object={template?.GameObject.name ?? "none"}; sibling={template?.RectTransform.GetSiblingIndex().ToString() ?? "none"}");
        var buttonObject = FindExistingButton(panel);
        if (buttonObject != null && template != null)
        {
            DrawableSuitsDiagnostics.Info("Replacing existing DrawableSuits pause button with a fresh clone so native prefix/arrow children match the current menu template.");
            buttonObject.SetActive(false);
            Object.Destroy(buttonObject);
            buttonObject = null;
        }

        if (buttonObject == null)
        {
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
        ConfigureButton(buttonObject, quickMenu, template);
        PlaceButtonAndRows(panel, buttonObject);
        RebuildNavigation(panel);
        var buttonRect = buttonObject.GetComponent<RectTransform>();
        var button = buttonObject.GetComponent<Button>();
        DrawableSuitsDiagnostics.Info($"EnsureButton complete. exists={buttonObject != null}; active={buttonObject.activeSelf}; anchoredPosition={buttonRect?.anchoredPosition.ToString() ?? "null"}; siblingIndex={buttonObject.transform.GetSiblingIndex()}; nav={DescribeNavigation(button)}; colors={DescribeButtonColors(button)}; rowsAfter={CollectRows(panel).Count}");
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

    private static void ConfigureButton(GameObject buttonObject, QuickMenuManager quickMenu, MenuRow template)
    {
        buttonObject.name = ButtonName;
        buttonObject.SetActive(true);
        if (template != null)
        {
            ApplyTemplateStyle(template, buttonObject);
        }

        SetButtonText(buttonObject, template?.LabelText);
        EnsureNativePrefix(buttonObject, template);

        var button = buttonObject.GetComponent<Button>() ?? buttonObject.AddComponent<Button>();
        button.interactable = true;
        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.AddListener(() => OpenEditorFromPauseMenu(quickMenu));
        DrawableSuitsDiagnostics.Info($"Configured pause-menu button listener. button={buttonObject.name}; interactable={button.interactable}; targetQuickMenuNull={quickMenu == null}");

        var navigation = button.navigation;
        navigation.mode = Navigation.Mode.Automatic;
        button.navigation = navigation;
    }

    private static void ApplyTemplateStyle(MenuRow template, GameObject buttonObject)
    {
        var targetRect = buttonObject.GetComponent<RectTransform>();
        if (targetRect != null)
        {
            CopyRectTemplate(template.RectTransform, targetRect);
        }

        var templateButton = template.Button;
        var targetButton = buttonObject.GetComponent<Button>() ?? buttonObject.AddComponent<Button>();
        targetButton.transition = templateButton.transition;
        targetButton.colors = templateButton.colors;
        targetButton.spriteState = templateButton.spriteState;
        targetButton.animationTriggers = templateButton.animationTriggers;
        targetButton.targetGraphic = FindMatchingGraphic(templateButton.targetGraphic, template.GameObject.transform, buttonObject.transform)
            ?? buttonObject.GetComponent<Graphic>()
            ?? buttonObject.GetComponentInChildren<Graphic>(true);

        var templateImage = template.GameObject.GetComponent<Image>();
        var targetImage = buttonObject.GetComponent<Image>();
        if (templateImage != null && targetImage != null)
        {
            targetImage.sprite = templateImage.sprite;
            targetImage.overrideSprite = templateImage.overrideSprite;
            targetImage.type = templateImage.type;
            targetImage.preserveAspect = templateImage.preserveAspect;
            targetImage.fillCenter = templateImage.fillCenter;
            targetImage.fillMethod = templateImage.fillMethod;
            targetImage.fillAmount = templateImage.fillAmount;
            targetImage.fillClockwise = templateImage.fillClockwise;
            targetImage.fillOrigin = templateImage.fillOrigin;
            targetImage.color = templateImage.color;
            targetImage.material = templateImage.material;
            targetImage.raycastTarget = templateImage.raycastTarget;
        }

        var templateLayout = template.GameObject.GetComponent<LayoutElement>();
        var targetLayout = buttonObject.GetComponent<LayoutElement>();
        if (templateLayout != null)
        {
            targetLayout ??= buttonObject.AddComponent<LayoutElement>();
            targetLayout.ignoreLayout = templateLayout.ignoreLayout;
            targetLayout.minWidth = templateLayout.minWidth;
            targetLayout.minHeight = templateLayout.minHeight;
            targetLayout.preferredWidth = templateLayout.preferredWidth;
            targetLayout.preferredHeight = templateLayout.preferredHeight;
            targetLayout.flexibleWidth = templateLayout.flexibleWidth;
            targetLayout.flexibleHeight = templateLayout.flexibleHeight;
            targetLayout.layoutPriority = templateLayout.layoutPriority;
        }

        DrawableSuitsDiagnostics.Info($"Applied pause-menu template style. template={template.LabelText}/{template.GameObject.name}; target={buttonObject.name}; colors={DescribeButtonColors(targetButton)}");
    }

    private static void SetButtonText(GameObject buttonObject, string templateLabelText)
    {
        var tmpLabels = buttonObject.GetComponentsInChildren<TextMeshProUGUI>(true);
        var primaryTmp = FindPrimaryTmpLabel(tmpLabels, templateLabelText);
        var preserved = new List<string>();
        foreach (var label in tmpLabels)
        {
            if (label == null)
            {
                continue;
            }

            if (label == primaryTmp)
            {
                label.text = ButtonText;
                label.enableWordWrapping = false;
            }
            else
            {
                preserved.Add(label.text ?? string.Empty);
            }
        }

        var legacyLabels = buttonObject.GetComponentsInChildren<Text>(true);
        var primaryLegacy = primaryTmp == null ? FindPrimaryLegacyLabel(legacyLabels, templateLabelText) : null;
        foreach (var label in legacyLabels)
        {
            if (label == null)
            {
                continue;
            }

            if (label == primaryLegacy)
            {
                label.text = ButtonText;
            }
            else
            {
                preserved.Add(label.text ?? string.Empty);
            }
        }

        DrawableSuitsDiagnostics.Info($"Pause-menu DrawableSuits text configured. templateLabel={templateLabelText ?? "none"}; primaryTmp={DescribeTmpLabel(primaryTmp)}; primaryLegacy={DescribeLegacyLabel(primaryLegacy)}; preserved=[{string.Join(", ", preserved)}]");
    }

    private static void EnsureNativePrefix(GameObject buttonObject, MenuRow template)
    {
        var existingPrefix = FindPrefixText(buttonObject);
        if (existingPrefix != null)
        {
            var primary = FindPrimaryTmpLabel(buttonObject.GetComponentsInChildren<TextMeshProUGUI>(true), ButtonText);
            DrawableSuitsDiagnostics.Info($"PauseMenuPrefixMode mode=PreservedChild; prefix={existingPrefix.gameObject.name}; prefixText='{existingPrefix.text}'; primary={DescribeTmpLabel(primary)}; rowRect={DescribeRect(buttonObject.GetComponent<RectTransform>())}; sibling={buttonObject.transform.GetSiblingIndex()}");
            return;
        }

        var existingLegacyPrefix = FindPrefixLegacyText(buttonObject);
        if (existingLegacyPrefix != null)
        {
            var primary = FindPrimaryLegacyLabel(buttonObject.GetComponentsInChildren<Text>(true), ButtonText);
            DrawableSuitsDiagnostics.Info($"PauseMenuPrefixMode mode=PreservedChild; prefix={existingLegacyPrefix.gameObject.name}; prefixText='{existingLegacyPrefix.text}'; primary={DescribeLegacyLabel(primary)}; rowRect={DescribeRect(buttonObject.GetComponent<RectTransform>())}; sibling={buttonObject.transform.GetSiblingIndex()}");
            return;
        }

        var primaryTmp = FindPrimaryTmpLabel(buttonObject.GetComponentsInChildren<TextMeshProUGUI>(true), ButtonText);
        if (primaryTmp != null)
        {
            primaryTmp.text = ButtonTextWithInlinePrefix;
            primaryTmp.enableWordWrapping = false;
            DrawableSuitsDiagnostics.Info($"PauseMenuPrefixMode mode=InlineLabel; displayed='{primaryTmp.text}'; primary={DescribeTmpLabel(primaryTmp)}; rowRect={DescribeRect(buttonObject.GetComponent<RectTransform>())}; sibling={buttonObject.transform.GetSiblingIndex()}");
            return;
        }

        var primaryLegacy = FindPrimaryLegacyLabel(buttonObject.GetComponentsInChildren<Text>(true), ButtonText);
        if (primaryLegacy != null)
        {
            primaryLegacy.text = ButtonTextWithInlinePrefix;
            DrawableSuitsDiagnostics.Info($"PauseMenuPrefixMode mode=InlineLabel; displayed='{primaryLegacy.text}'; primary={DescribeLegacyLabel(primaryLegacy)}; rowRect={DescribeRect(buttonObject.GetComponent<RectTransform>())}; sibling={buttonObject.transform.GetSiblingIndex()}");
            return;
        }

        DrawableSuitsDiagnostics.Warn($"PauseMenuPrefixMode mode=SkippedNoPrimaryLabel; template={template?.LabelText ?? "none"}; button={buttonObject.name}; rowRect={DescribeRect(buttonObject.GetComponent<RectTransform>())}; sibling={buttonObject.transform.GetSiblingIndex()}");
    }

    private static TextMeshProUGUI FindPrefixText(GameObject buttonObject)
    {
        var labels = buttonObject.GetComponentsInChildren<TextMeshProUGUI>(true);
        foreach (var label in labels)
        {
            if (label != null && Normalize(label.text) == PrefixText)
            {
                return label;
            }
        }

        return null;
    }

    private static Text FindPrefixLegacyText(GameObject buttonObject)
    {
        var labels = buttonObject.GetComponentsInChildren<Text>(true);
        foreach (var label in labels)
        {
            if (label != null && Normalize(label.text) == PrefixText)
            {
                return label;
            }
        }

        return null;
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
        var nativeOrder = new List<MenuRow>();
        foreach (var row in sameParentRows)
        {
            if (!row.IsDrawable)
            {
                nativeOrder.Add(row);
            }
        }

        var resumeIndex = nativeOrder.IndexOf(resume);
        if (resumeIndex < 0)
        {
            resumeIndex = 0;
        }

        var spacing = DetectRowSpacing(nativeOrder);
        var nextRow = resumeIndex + 1 < nativeOrder.Count ? nativeOrder[resumeIndex + 1] : null;
        var gapToNext = nextRow != null ? Mathf.Abs(resume.RectTransform.anchoredPosition.y - nextRow.RectTransform.anchoredPosition.y) : float.PositiveInfinity;
        var shouldShiftLowerRows = nextRow != null && gapToNext < spacing * 1.45f;
        var insertY = resume.RectTransform.anchoredPosition.y - spacing;
        var insertSibling = resume.RectTransform.GetSiblingIndex() + 1;

        var drawablePosition = resume.RectTransform.anchoredPosition;
        drawablePosition.y = insertY;
        drawable.RectTransform.anchoredPosition = drawablePosition;
        drawable.RectTransform.SetSiblingIndex(insertSibling);

        var shiftedRows = 0;
        if (shouldShiftLowerRows)
        {
            for (var i = resumeIndex + 1; i < nativeOrder.Count; i++)
            {
                var row = nativeOrder[i];
                var position = row.RectTransform.anchoredPosition;
                position.y -= spacing;
                row.RectTransform.anchoredPosition = position;
                row.RectTransform.SetSiblingIndex(insertSibling + 1 + (i - resumeIndex - 1));
                shiftedRows++;
            }
        }

        DrawableSuitsDiagnostics.Info($"PlaceRowsExplicitly nativeCount={nativeOrder.Count}; resume={resume.LabelText}; next={nextRow?.LabelText ?? "none"}; spacing={spacing}; gapToNext={gapToNext}; shiftedLowerRows={shiftedRows}; drawablePosition={drawable.RectTransform.anchoredPosition}; drawableSibling={drawable.RectTransform.GetSiblingIndex()}");
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

    private static Graphic FindMatchingGraphic(Graphic templateGraphic, Transform templateRoot, Transform targetRoot)
    {
        if (templateGraphic == null || templateRoot == null || targetRoot == null)
        {
            return null;
        }

        var path = GetRelativePath(templateRoot, templateGraphic.transform);
        var target = string.IsNullOrEmpty(path) ? targetRoot : targetRoot.Find(path);
        var graphic = target != null ? target.GetComponent<Graphic>() : null;
        DrawableSuitsDiagnostics.Info($"Pause-menu targetGraphic mapping. templateGraphic={templateGraphic.name}; path={path}; mapped={graphic?.name ?? "none"}");
        return graphic;
    }

    private static string GetRelativePath(Transform root, Transform child)
    {
        if (root == null || child == null || child == root)
        {
            return string.Empty;
        }

        var names = new List<string>();
        var current = child;
        while (current != null && current != root)
        {
            names.Insert(0, current.name);
            current = current.parent;
        }

        return current == root ? string.Join("/", names) : string.Empty;
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
        var tmp = FindPrimaryTmpLabel(gameObject.GetComponentsInChildren<TextMeshProUGUI>(true), null);
        if (tmp != null)
        {
            return tmp.text ?? string.Empty;
        }

        var text = FindPrimaryLegacyLabel(gameObject.GetComponentsInChildren<Text>(true), null);
        return text != null ? text.text ?? string.Empty : string.Empty;
    }

    private static MenuRow ChooseTemplateRow(List<MenuRow> rows)
    {
        return FindResumeRow(rows)
            ?? FindRowContaining(rows, "invite")
            ?? FindRowContaining(rows, "settings")
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

    private static TextMeshProUGUI FindPrimaryTmpLabel(TextMeshProUGUI[] labels, string preferredText)
    {
        TextMeshProUGUI best = null;
        var bestScore = float.MinValue;
        var preferred = Normalize(preferredText);
        foreach (var label in labels)
        {
            if (label == null)
            {
                continue;
            }

            var score = ScoreLabelCandidate(label.text, label.rectTransform, label.fontSize, preferred);
            if (score > bestScore)
            {
                bestScore = score;
                best = label;
            }
        }

        return best;
    }

    private static Text FindPrimaryLegacyLabel(Text[] labels, string preferredText)
    {
        Text best = null;
        var bestScore = float.MinValue;
        var preferred = Normalize(preferredText);
        foreach (var label in labels)
        {
            if (label == null)
            {
                continue;
            }

            var score = ScoreLabelCandidate(label.text, label.rectTransform, label.fontSize, preferred);
            if (score > bestScore)
            {
                bestScore = score;
                best = label;
            }
        }

        return best;
    }

    private static float ScoreLabelCandidate(string text, RectTransform rect, float fontSize, string preferred)
    {
        var normalized = Normalize(text);
        if (string.IsNullOrEmpty(normalized))
        {
            return -100000f;
        }

        var score = 0f;
        if (!string.IsNullOrEmpty(preferred) && normalized.Contains(preferred))
        {
            score += 100000f;
        }

        if (HasLetterOrDigit(normalized))
        {
            score += 1000f + normalized.Length * 25f;
        }
        else
        {
            score -= 1000f;
        }

        if (rect != null)
        {
            score += Mathf.Max(0f, rect.rect.width) * 0.2f;
            score += Mathf.Max(0f, rect.rect.height) * 0.1f;
        }

        score += fontSize;
        return score;
    }

    private static bool HasLetterOrDigit(string value)
    {
        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c))
            {
                return true;
            }
        }

        return false;
    }

    private static string DescribeTmpLabel(TextMeshProUGUI label)
    {
        return label == null ? "none" : $"{label.gameObject.name}='{label.text}' rect={label.rectTransform.rect}";
    }

    private static string DescribeLegacyLabel(Text label)
    {
        return label == null ? "none" : $"{label.gameObject.name}='{label.text}' rect={label.rectTransform.rect}";
    }

    private static string DescribeRect(RectTransform rect)
    {
        return rect == null ? "rect=null" : $"rect={rect.rect}; anchored={rect.anchoredPosition}; sizeDelta={rect.sizeDelta}";
    }

    private static string DescribeButtonColors(Button button)
    {
        if (button == null)
        {
            return "button=null";
        }

        var colors = button.colors;
        return $"transition={button.transition}; normal={colors.normalColor}; highlighted={colors.highlightedColor}; selected={colors.selectedColor}; pressed={colors.pressedColor}; disabled={colors.disabledColor}; targetGraphic={button.targetGraphic?.name ?? "null"}";
    }

    private static string DescribeNavigation(Button button)
    {
        if (button == null)
        {
            return "button=null";
        }

        var navigation = button.navigation;
        return $"mode={navigation.mode}; up={navigation.selectOnUp?.gameObject.name ?? "null"}; down={navigation.selectOnDown?.gameObject.name ?? "null"}";
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
