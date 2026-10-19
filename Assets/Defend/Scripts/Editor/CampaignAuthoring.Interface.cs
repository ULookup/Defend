using System;
using System.Collections.Generic;
using Defend.Unity;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Defend.Editor
{
    public static partial class CampaignAuthoring
    {
        private static readonly Color CampaignInk = new Color(.89f, .95f, .98f);
        private static readonly Color CampaignCyan = new Color(.35f, 1f, .88f);
        private static Font campaignFont;
        private static readonly string[] SelectionSummaries = {
            "利用中央隔断组织补射，先建立能持续开火的防线。",
            "同时支援两条接敌路线，平衡前沿火力与后方供能。",
            "为四面逐步接近的敌人留出扩建位置，组织相互支援的防线。",
            "在多处进攻面之间分配供能，避免单一源头失守牵动整条防线。",
            "正面压力持续存在，提前为东南侧翼预留资金、空间和电力。",
            "用有限预算守住核心，在维修、扩建与备用供能之间作出取舍。"
        };

        public static string BuildInterface()
        {
            RequireEditMode(); ImportSelectionArt();
            campaignFont = AssetDatabase.LoadAssetAtPath<Font>(WhiteboxSceneBuilder.ChineseFontPath);
            var catalog = AssetDatabase.LoadAssetAtPath<CampaignCatalogAsset>(CatalogPath);
            if (catalog == null || catalog.Levels.Count != 6) throw new InvalidOperationException("先制作完整六关目录。");
            var root = PrefabUtility.LoadPrefabContents(DefenseInterfaceAuthoring.PrefabPath);
            try
            {
                var defense = new SerializedObject(root.GetComponent<DefenseInterface>());
                var previous = defense.FindProperty("campaignSelection").objectReferenceValue as CampaignSelectionPanel;
                if (previous != null) Object.DestroyImmediate(previous.gameObject);
                var page = CampaignRect("六关战役选择", root.transform, 0, 0, 0, 0);
                page.anchorMin = Vector2.zero; page.anchorMax = Vector2.one; page.offsetMin = page.offsetMax = Vector2.zero;
                var safe = CampaignRect("电脑安全画布", page, 0, 0, 1920, 1080);
                safe.anchorMin = safe.anchorMax = safe.pivot = new Vector2(.5f, .5f); safe.anchoredPosition = Vector2.zero;
                var fit = new SerializedObject(page.gameObject.AddComponent<OutgameCanvasLayout>());
                fit.FindProperty("safeArea").objectReferenceValue = safe;
                fit.FindProperty("referenceSize").vector2Value = new Vector2(1920, 1080); fit.ApplyModifiedPropertiesWithoutUndo();
                var backdrop = CampaignImage("全新战役背景", safe, SelectionSprite("Background", 16f / 9), 0, 0, 1920, 1080);
                backdrop.raycastTarget = true;
                backdrop.color = new Color(.56f, .65f, .7f, 1);
                CampaignImage("战役徽标", safe, SelectionSprite("Emblem", 80f/96), 52, 20, 80, 96);
                CampaignLabel("战役英文标题", safe, "C A M P A I G N", 156, 20, 500, 32, 18);
                CampaignLabel("页面标题", safe, "战役部署", 152, 44, 1000, 76, 50).fontStyle = FontStyle.Bold;
                CampaignImage("页眉分隔线", safe, SelectionSprite("HeaderRule", 1840f/24), 40, 124, 1840, 24);
                CampaignLabel("关卡列表标题", safe, "选择关卡", 60, 154, 300, 52, 32).fontStyle = FontStyle.Bold;
                CampaignImage("关卡列表分隔线", safe, SelectionSprite("SectionRule", 816f/8), 232, 186, 816, 8);
                var orderHint = CampaignLabel("顺序解锁提示", safe, "按顺序解锁", 1024, 166, 160, 40, 24); orderHint.alignment = TextAnchor.MiddleRight;
                var progress = CampaignLabel("本机战役进度", safe, "", 1480, 44, 384, 44, 24);
                progress.alignment = TextAnchor.MiddleRight;
                var motionSettings = root.GetComponentInChildren<InterfaceMotionSettings>(true);
                if (motionSettings == null) throw new InvalidOperationException("现役界面动效设置缺失。");
                var panel = page.gameObject.AddComponent<CampaignSelectionPanel>(); var data = new SerializedObject(panel);
                var buttons = new Button[6]; var cardViews = new CampaignSelectionCardView[6];
                var titles = new Text[6]; var states = new Text[6];
                var coverBindings = data.FindProperty("covers"); coverBindings.arraySize = 6;
                Sprite cardSurface = SelectionSprite("CardSurface", 376f / 340), accent = SelectionSprite("CardAccent", 376f / 340);
                Sprite availableIcon = SelectionSprite("IconAvailable", 1), lockedIcon = SelectionSprite("IconLocked", 1), completedIcon = SelectionSprite("IconCompleted", 1);
                var pendingIcon = SelectionSprite("IconPending", 1);
                var progressNodes = new Image[6];
                for (int i = 0; i < progressNodes.Length; i++)
                    progressNodes[i] = CampaignImage("通关进度节点 " + (i + 1), safe, availableIcon, 1664 + i * 36, 88, 32, 32);
                for (int i = 0; i < 6; i++)
                {
                    var cover = SelectionSprite("Cover" + (i + 1).ToString("00"), 8f / 5);
                    var binding = coverBindings.GetArrayElementAtIndex(i);
                    binding.FindPropertyRelative("levelId").stringValue = catalog.Levels[i].Id;
                    binding.FindPropertyRelative("cover").objectReferenceValue = cover;
                    binding.FindPropertyRelative("summary").stringValue = SelectionSummaries[i];
                    var hit = CampaignImage("关卡卡片 " + (i + 1), safe, cardSurface, 56 + i % 3 * 388, 216 + i / 3 * 364, 376, 340);
                    hit.rectTransform.localScale = Vector3.one * (360f / 376f);
                    hit.color = Color.clear; hit.raycastTarget = true;
                    var visual = CampaignRect("固定比例视觉层", hit.transform, 0, 0, 376, 340);
                    var coverImage = CampaignImage("全新关卡封面", visual, cover, 0, 0, 376, 235);
                    var surface = CampaignImage("平面卡片信息底板", visual, cardSurface, 0, 0, 376, 340);
                    var hoverAccent = CampaignImage("悬停与焦点轮廓", visual, accent, 0, 0, 376, 340);
                    hoverAccent.color = new Color(.68f, .89f, .93f, .65f);
                    var selectedAccent = CampaignImage("选中轮廓", visual, accent, 0, 0, 376, 340);
                    selectedAccent.color = CampaignCyan; selectedAccent.gameObject.SetActive(false);
                    var selectedTab = CampaignImage("当前选择标签", selectedAccent.transform, SelectionSprite("SelectedTab", 160f/44), 0, 0, 160, 44);
                    var selectedText = CampaignLabel("当前选择文字", selectedTab.transform, "当前选择", 14, 0, 136, 40, 22);
                    selectedText.color = new Color(.02f, .14f, .16f);
                    BindCampaignEntrance(hit.gameObject, motionSettings, .04f + i * .035f);
                    titles[i] = CampaignLabel("关卡标题", visual, "", 24, 244, 332, 46, 28);
                    var stateIcon = CampaignImage("关卡状态图标", visual, availableIcon, 24, 296, 28, 28);
                    states[i] = CampaignLabel("通关与锁定状态", visual, "", 64, 288, 134, 40, 24);
                    var focus = CampaignLabel("键盘焦点标记", visual, "回车选择", 204, 288, 152, 40, 20);
                    focus.alignment = TextAnchor.MiddleRight; focus.gameObject.SetActive(false);
                    buttons[i] = hit.gameObject.AddComponent<Button>(); buttons[i].targetGraphic = surface;
                    var motion = hit.gameObject.AddComponent<InterfaceButtonMotion>();
                    motion.Configure(motionSettings, buttons[i], visual, hoverAccent);
                    motion.ConfigureStates(surface, cardSurface, cardSurface, cardSurface, cardSurface, cardSurface);
                    BindCampaignFocus(hit.gameObject, focus.gameObject);
                    cardViews[i] = hit.gameObject.AddComponent<CampaignSelectionCardView>();
                    var view = new SerializedObject(cardViews[i]);
                    SetCampaignReference(view, "cover", coverImage); SetCampaignReference(view, "stateIcon", stateIcon);
                    SetCampaignReference(view, "surface", surface);
                    SetCampaignReference(view, "availableIcon", availableIcon); SetCampaignReference(view, "lockedIcon", lockedIcon);
                    SetCampaignReference(view, "completedIcon", completedIcon); SetCampaignReference(view, "motion", motion);
                    SetCampaignReference(view, "selectionIndicator", selectedAccent.gameObject); view.ApplyModifiedPropertiesWithoutUndo();
                }
                var detail = CampaignImage("关卡详情面板", safe, SelectionSprite("DetailSurface", 676f/806), 1216, 152, 676, 806);
                BindCampaignEntrance(detail.gameObject, motionSettings, .12f);
                var chapter = CampaignLabel("所选关卡编号", detail.transform, "", 40, 12, 300, 44, 26);
                var availability = CampaignLabel("所选关卡状态", detail.transform, "", 420, 14, 220, 40, 24);
                availability.alignment = TextAnchor.MiddleRight;
                var title = CampaignLabel("所选关卡名称", detail.transform, "", 40, 50, 604, 76, 48);
                title.fontStyle = FontStyle.Bold;
                var preview = CampaignImage("所选关卡完整封面", detail.transform, SelectionSprite("Cover01", 8f / 5), 28, 128, 620, 387.5f);
                var previousPreview = CampaignImage("封面淡出层", detail.transform, SelectionSprite("Cover01", 8f / 5), 28, 128, 620, 387.5f);
                previousPreview.color = Color.clear;
                var coverTransition = detail.gameObject.AddComponent<CampaignCoverTransition>();
                var transitionData = new SerializedObject(coverTransition);
                SetCampaignReference(transitionData, "current", preview); SetCampaignReference(transitionData, "previous", previousPreview);
                SetCampaignReference(transitionData, "settings", motionSettings); transitionData.ApplyModifiedPropertiesWithoutUndo();
                CampaignImage("封面细边框", detail.transform, SelectionSprite("PreviewFrame", 8f/5), 28, 128, 620, 387.5f);
                CampaignImage("波次图标", detail.transform, SelectionSprite("IconWaves", 1), 40, 530, 64, 64);
                CampaignImage("资金图标", detail.transform, SelectionSprite("IconCredits", 1), 372, 530, 64, 64);
                CampaignLabel("防守波次标题", detail.transform, "防守波次", 124, 530, 220, 38, 24);
                CampaignLabel("初始资金标题", detail.transform, "初始资金", 456, 530, 190, 38, 24);
                var facts = CampaignLabel("所选关卡波次", detail.transform, "", 124, 568, 220, 76, 48);
                var credits = CampaignLabel("所选关卡资金", detail.transform, "", 456, 568, 190, 76, 48);
                CampaignImage("关卡数据分隔线", detail.transform, SelectionSprite("VerticalRule", 2f/88), 324, 538, 2, 88);
                CampaignImage("部署提示分隔线", detail.transform, SelectionSprite("DetailRule", 600f/4), 40, 644, 600, 4);
                CampaignLabel("部署提示标题", detail.transform, "部署提示", 40, 654, 604, 40, 24);
                var description = CampaignLabel("所选关卡战术", detail.transform, "", 40, 698, 604, 70, 24);
                var status = CampaignLabel("所选关卡解锁说明", safe, "", 752, 986, 460, 32, 20);
                var saveStatus = CampaignLabel("通关保存状态", safe, "", 752, 1024, 620, 32, 20);
                var start = CampaignButton("开始所选关卡", safe, "开始部署", "PrimaryButton", 1444, 976, 448, 80, 32, motionSettings, out var startLabel);
                var back = CampaignButton("返回标题", safe, "返回标题", "ReturnButton", 44, 996, 256, 56, 26, motionSettings, out _);
                SetCampaignReference(data, "availability", availability); SetCampaignReference(data, "chapter", chapter); SetCampaignReference(data, "coverTransition", coverTransition);
                SetCampaignReference(data, "title", title); SetCampaignReference(data, "description", description);
                SetCampaignReference(data, "facts", facts); SetCampaignReference(data, "status", status); SetCampaignReference(data, "saveStatus", saveStatus);
                SetCampaignReference(data, "credits", credits);
                SetCampaignReference(data, "selectedPreview", preview); SetCampaignReference(data, "start", start);
                SetCampaignReference(data, "back", back); SetCampaignReference(data, "startLabel", startLabel); SetCampaignReference(data, "progress", progress);
                SetCampaignArray(data, "progressNodes", progressNodes); SetCampaignReference(data, "progressCompleted", completedIcon); SetCampaignReference(data, "progressPending", pendingIcon);
                SetCampaignArray(data, "cards", buttons); SetCampaignArray(data, "cardViews", cardViews);
                SetCampaignArray(data, "cardTitles", titles); SetCampaignArray(data, "cardStates", states); data.ApplyModifiedPropertiesWithoutUndo();
                SetCampaignNavigation(buttons, start, back);
                defense.FindProperty("campaignSelection").objectReferenceValue = panel; defense.ApplyModifiedPropertiesWithoutUndo();
                NewPlayerGuideAuthoring.Build(root);
                ValidateSelectionText(panel, catalog); ValidateSelectionImages(page.gameObject);
                page.gameObject.SetActive(false);
                PrefabUtility.SaveAsPrefabAsset(root, DefenseInterfaceAuthoring.PrefabPath); AssetDatabase.SaveAssets();
                return "全新战役选择页已保存：六张生成封面、独立比例素材、真实进度及完整鼠标和键盘状态。";
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        public static string ValidateInterfaceText()
        {
            RequireEditMode();
            var root = PrefabUtility.LoadPrefabContents(DefenseInterfaceAuthoring.PrefabPath);
            try
            {
                var panel = root.GetComponentInChildren<CampaignSelectionPanel>(true);
                var catalog = AssetDatabase.LoadAssetAtPath<CampaignCatalogAsset>(CatalogPath);
                if (panel == null || catalog == null) throw new InvalidOperationException("关卡目录或选择页缺失。");
                string text = ValidateSelectionText(panel, catalog); ValidateSelectionImages(panel.gameObject); return text;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static string ValidateSelectionText(CampaignSelectionPanel panel, CampaignCatalogAsset catalog)
        {
            var originalValues = new Dictionary<Text, string>();
            foreach (var label in panel.GetComponentsInChildren<Text>(true)) originalValues.Add(label, label.text);
            try
            {
            var data = new SerializedObject(panel); var titles = data.FindProperty("cardTitles"); var states = data.FindProperty("cardStates");
            var description = (Text)data.FindProperty("description").objectReferenceValue;
            var detailsTitle = (Text)data.FindProperty("title").objectReferenceValue;
            var facts = (Text)data.FindProperty("facts").objectReferenceValue;
            var credits = (Text)data.FindProperty("credits").objectReferenceValue;
            var status = (Text)data.FindProperty("status").objectReferenceValue;
            var saveStatus = (Text)data.FindProperty("saveStatus").objectReferenceValue;
            var covers = data.FindProperty("covers"); var results = new List<string>();
            for (int i = 0; i < catalog.Levels.Count; i++)
            {
                var level = catalog.Levels[i]; var title = (Text)titles.GetArrayElementAtIndex(i).objectReferenceValue;
                var state = (Text)states.GetArrayElementAtIndex(i).objectReferenceValue;
                title.text = (i + 1).ToString("00") + "  " + level.DisplayName;
                detailsTitle.text = level.DisplayName; description.text = covers.GetArrayElementAtIndex(i).FindPropertyRelative("summary").stringValue;
                facts.text = level.WaveCount + " 波"; credits.text = level.StartingCredits.ToString();
                CheckCampaignText(title); CheckCampaignText(detailsTitle); CheckCampaignText(description); CheckCampaignText(facts); CheckCampaignText(credits);
                foreach (string phrase in new[] { "可部署", "待解锁", "已通关" }) { state.text = phrase; CheckCampaignText(state); }
                foreach (string phrase in new[] { "已通关，可重新部署。", "完成本关，解锁下一关。", "守住最终阵地，完成战役。",
                    i == 0 ? "" : "完成第 " + i + " 关后解锁。" })
                { status.text = phrase; CheckCampaignText(status); }
                saveStatus.text = "通关记录正在重试保存，请暂勿关闭游戏。"; CheckCampaignText(saveStatus);
                results.Add(level.DisplayName + "：标题 " + title.preferredHeight.ToString("0.##") + "/44，战术 " + description.preferredHeight.ToString("0.##") + "/96");
            }
            foreach (var label in originalValues.Keys) CheckCampaignText(label);
            return string.Join("\n", results);
            }
            finally { foreach (var pair in originalValues) pair.Key.text = pair.Value; }
        }
        private static void CheckCampaignText(Text text)
        {
            if (text.preferredHeight > text.rectTransform.rect.height + .1f)
                throw new InvalidOperationException(text.name + "文本超过区域：" + text.preferredHeight + "/" + text.rectTransform.rect.height + "；" + text.text);
        }
        private static void SetCampaignNavigation(Button[] cards, Button start, Button back)
        {
            for (int i = 0; i < cards.Length; i++)
            {
                cards[i].navigation = new Navigation { mode = Navigation.Mode.Explicit,
                    selectOnLeft = i % 3 == 0 ? back : cards[i - 1], selectOnRight = i % 3 == 2 ? start : cards[i + 1],
                    selectOnUp = i < 3 ? cards[i] : cards[i - 3], selectOnDown = i < 3 ? cards[i + 3] : back };
            }
            start.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = cards[5], selectOnUp = cards[2], selectOnDown = back };
            back.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnRight = start, selectOnUp = cards[3] };
        }
        private static RectTransform CampaignRect(string name, Transform parent, float x, float y, float width, float height)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1); rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height); return rect;
        }
        private static Image CampaignImage(string name, Transform parent, Sprite sprite, float x, float y, float width, float height)
        {
            var image = CampaignRect(name, parent, x, y, width, height).gameObject.AddComponent<Image>();
            image.sprite = sprite; image.preserveAspect = true; image.raycastTarget = false; return image;
        }
        private static Text CampaignLabel(string name, Transform parent, string value, float x, float y, float width, float height, int size)
        {
            var text = CampaignRect(name, parent, x, y, width, height).gameObject.AddComponent<Text>(); text.font = campaignFont;
            text.text = value; text.fontSize = size; text.color = CampaignInk; text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Truncate; text.raycastTarget = false; return text;
        }
        private static Button CampaignButton(string name, Transform parent, string value, string art, float x, float y, float width, float height, int size,
            InterfaceMotionSettings settings, out Text label)
        {
            var sprite = SelectionSprite(art, width / height);
            var hit = CampaignImage(name, parent, sprite, x, y, width, height); hit.color = Color.clear; hit.raycastTarget = true;
            var visual = CampaignRect("固定比例视觉层", hit.transform, 0, 0, width, height);
            var surface = CampaignImage("全新按钮底图", visual, sprite, 0, 0, width, height);
            var button = hit.gameObject.AddComponent<Button>(); button.targetGraphic = surface;
            label = CampaignLabel("按钮文字", visual, value, 12, 0, width - 24, height, size); label.alignment = TextAnchor.MiddleCenter;
            var motion = hit.gameObject.AddComponent<InterfaceButtonMotion>(); motion.Configure(settings, button, visual, null);
            motion.ConfigureStates(surface, sprite, sprite, sprite, sprite, sprite);
            bool primary = art == "PrimaryButton";
            label.color = primary ? new Color(.025f, .12f, .13f, 1) : CampaignInk;
            var appearance = new SerializedObject(hit.gameObject.AddComponent<CampaignFlatButtonAppearance>());
            SetCampaignReference(appearance, "button", button); SetCampaignReference(appearance, "surface", surface);
            SetCampaignReference(appearance, "label", label); SetCampaignReference(appearance, "inputState", motion);
            appearance.FindProperty("primary").boolValue = primary; appearance.ApplyModifiedPropertiesWithoutUndo();
            var directionIcon = CampaignImage("按钮方向图标", visual, SelectionSprite(primary ? "ArrowRight" : "ArrowLeft", 1), primary ? width - 116 : 30, (height-32)/2, 32, 32);
            if (primary) { directionIcon.color = new Color(.025f, .12f, .13f, 1); label.fontStyle = FontStyle.Bold; }
            label.rectTransform.anchoredPosition = new Vector2(primary ? 28 : 64, 0);
            label.rectTransform.sizeDelta = new Vector2(primary ? width - 176 : width - 76, height);
            BindCampaignEntrance(hit.gameObject, settings, .24f);
            var focus = CampaignLabel("键盘焦点标记", hit.transform, primary ? "ENTER" : "回车确认",
                primary ? width - 76 : 0, primary ? 20 : -40, primary ? 68 : width, 40, primary ? 16 : 20);
            focus.alignment = TextAnchor.MiddleCenter; focus.gameObject.SetActive(false); BindCampaignFocus(hit.gameObject, focus.gameObject); return button;
        }
        private static void BindCampaignEntrance(GameObject target, InterfaceMotionSettings settings, float delay)
        {
            var group = target.AddComponent<CanvasGroup>();
            var entry = new SerializedObject(target.AddComponent<CampaignSelectionEntrance>());
            SetCampaignReference(entry, "group", group); SetCampaignReference(entry, "settings", settings);
            entry.FindProperty("delay").floatValue = delay; entry.ApplyModifiedPropertiesWithoutUndo();
        }
        private static void BindCampaignFocus(GameObject control, GameObject indicator)
        {
            var data = new SerializedObject(control.AddComponent<CampaignSelectionFocus>());
            SetCampaignReference(data, "indicator", indicator); data.ApplyModifiedPropertiesWithoutUndo();
        }
        private static void SetCampaignReference(SerializedObject data, string field, Object value) => data.FindProperty(field).objectReferenceValue = value;
        private static void SetCampaignArray<T>(SerializedObject data, string field, IReadOnlyList<T> values) where T : Object
        { var array = data.FindProperty(field); array.arraySize = values.Count; for (int i = 0; i < values.Count; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = values[i]; }
    }
}

