using System;
using Defend.Unity;
using Defend.Simulation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Defend.Editor
{
    /// <summary>在现役界面中保存新手图解、选关入口与实战任务卡。</summary>
    public static class NewPlayerGuideAuthoring
    {
        private const string Art = "Assets/Defend/Content/Art/CampaignSelectionReference20261019/";
        private static Font font;
        private static readonly Color Ink = new Color(.89f, .95f, .98f);
        private static readonly Color Cyan = new Color(.35f, 1f, .88f);
        private static readonly Color Amber = new Color(1f, .77f, .46f);
        private static readonly string[] Chapters = { "守住核心", "观察战场", "部署建筑", "连接供电", "迎击敌人", "整备再战" };

        [MenuItem("Defend/界面/制作新手引导")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请在编辑模式制作新手引导。");
            var root = PrefabUtility.LoadPrefabContents(DefenseInterfaceAuthoring.PrefabPath);
            try
            {
                Build(root);
                PrefabUtility.SaveAsPrefabAsset(root, DefenseInterfaceAuthoring.PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        public static void Build(GameObject root)
        {
            font = AssetDatabase.LoadAssetAtPath<Font>(WhiteboxSceneBuilder.ChineseFontPath);
            var selection = root.GetComponentInChildren<CampaignSelectionPanel>(true);
            if (selection == null) throw new InvalidOperationException("必须先建立选关页面。");
            var old = root.GetComponent<NewPlayerGuide>();
            if (old != null)
            {
                var oldData = new SerializedObject(old);
                foreach (string field in new[] { "manualRoot", "practiceRoot", "openManual" })
                {
                    var value = oldData.FindProperty(field).objectReferenceValue;
                    var target = value is GameObject go ? go : value is Component component ? component.gameObject : null;
                    if (target != null) Object.DestroyImmediate(target);
                }
                Object.DestroyImmediate(old);
            }
            var guide = root.AddComponent<NewPlayerGuide>();
            var data = new SerializedObject(guide);
            var layout = new SerializedObject(selection.GetComponent<OutgameCanvasLayout>());
            var selectionSafe = (RectTransform)layout.FindProperty("safeArea").objectReferenceValue;
            var entry = Button("新手引导入口", selectionSafe, "新手引导 · 必修", 1100, 42, 292, 64, true);
            var entryHint = Label("首次游玩提示", entry.transform, "完成训练，解锁第一关", 0, 66, 292, 28, 18, TextAnchor.MiddleCenter); entryHint.color = Cyan;
            Bind(data, "openManual", entry);
            Bind(data, "entryLabel", entry.GetComponentInChildren<Text>()); Bind(data, "entryHint", entryHint);

            var manual = FullPage("新手引导", root.transform, out var safe);
            var backdrop = Image("引导背景", safe, Sprite("Background"), 0, 0, 1920, 1080);
            backdrop.color = new Color(.56f, .65f, .7f); backdrop.raycastTarget = true;
            Image("引导徽标", safe, Sprite("Emblem"), 52, 20, 80, 96);
            Label("引导副标题", safe, "守望入门 · 作战手册", 156, 14, 1000, 42, 22).color = Cyan;
            Label("引导标题", safe, "新手引导", 152, 44, 900, 80, 48).fontStyle = FontStyle.Bold;
            Label("引导阅读说明", safe, "独立训练场 · 完整通关后解锁第一关", 1060, 60, 790, 44, 26, TextAnchor.MiddleRight);
            Image("页眉分隔线", safe, Sprite("HeaderRule"), 40, 124, 1840, 24);
            Label("章节栏标题", safe, "六步建立你的防线", 60, 168, 300, 42, 26).color = Cyan;
            var chapters = new Button[6]; var indicators = new Image[6]; var pages = new GameObject[6];
            var pictures = new SerializedObject(root.GetComponent<OutgameInterface>()).FindProperty("buildingPictures");
            var enemyPictures = new SerializedObject(root.GetComponent<OutgameInterface>()).FindProperty("enemyPictures");
            var buildingArt = new Sprite[9];
            for (int i = 0; i < buildingArt.Length; i++) buildingArt[i] = pictures.GetArrayElementAtIndex(i).objectReferenceValue as Sprite;
            for (int i = 0; i < 6; i++)
            {
                chapters[i] = Button("引导章节" + (i + 1), safe, (i + 1).ToString("00") + "   " + Chapters[i], 58, 238 + i * 98, 310, 68, false);
                indicators[i] = Image("章节状态图标", chapters[i].transform, Sprite("IconPending"), 14, 24, 20, 20);
                var page = Rect("图解 " + Chapters[i], safe, 412, 232, 1452, 696);
                pages[i] = page.gameObject;
                Image("图解与说明分隔", page, Sprite("VerticalRule"), 882, 8, 10, 440);
                ComposePage(i, page, buildingArt, enemyPictures.GetArrayElementAtIndex(0).objectReferenceValue as Sprite);
                page.gameObject.SetActive(i == 0);
            }
            var title = Label("当前教学主题", safe, Chapters[0], 412, 154, 1050, 64, 40);
            title.fontStyle = FontStyle.Bold;
            var counter = Label("教学页数", safe, "01 / 06", 1660, 164, 196, 44, 28, TextAnchor.MiddleRight); counter.color = Cyan;
            Label("可重复学习说明", safe, "只保存通关与解锁状态；离开未完成的训练后，下次重新开始。", 58, 824, 308, 116, 23);
            Image("页脚分隔线", safe, Sprite("HeaderRule"), 40, 946, 1840, 24);
            var close = Button("返回选关", safe, "返回选关", 58, 992, 256, 56, false);
            var previous = Button("上一页", safe, "上一页", 412, 988, 256, 56, false);
            var next = Button("下一页", safe, "下一页", 692, 988, 256, 56, false);
            var begin = Button("开始训练场引导", safe, "进入训练场 · 开始引导", 1408, 980, 448, 80, true);
            Bind(data, "manualRoot", manual); Bind(data, "manualTitle", title); Bind(data, "manualCounter", counter);
            Bind(data, "previousPage", previous); Bind(data, "nextPage", next); Bind(data, "closeManual", close); Bind(data, "beginPractice", begin);
            Bind(data, "chapterSelected", Sprite("IconCompleted")); Bind(data, "chapterNormal", Sprite("IconPending"));
            Array(data, "manualPages", pages); Array(data, "chapterButtons", chapters); Array(data, "chapterIndicators", indicators);
            BuildPractice(root.transform, data);
            data.ApplyModifiedPropertiesWithoutUndo();
            var owner = new SerializedObject(root.GetComponent<DefenseInterface>()); Bind(owner, "newPlayerGuide", guide); owner.ApplyModifiedPropertiesWithoutUndo();
            manual.SetActive(false);
        }

        private static void ComposePage(int index, Transform page, Sprite[] models, Sprite enemy)
        {
            switch (index)
            {
                case 0:
                    Label("示意标题", page, "防线围绕核心展开", 0, 0, 848, 48, 30, TextAnchor.MiddleCenter).color = Cyan;
                    Image("核心实物图", page, models[(int)BuildingKind.Core], 192, 40, 468, 420);
                    ModelCaption(page, "核心", "护盾先承受攻击，生命必须守住", 80, 444, 696);
                    Flow(page, new[] { "准备防线", "抵挡敌人", "完成全部波次" }, 20, 570, 256);
                    Notes(page, "你的任务", "守住核心，完成当前关卡的全部波次。核心生命归零，本次防守失败。",
                        "两层防护", "护盾依靠实际到货的能量补充。核心生命不能通过维修恢复。",
                        "进入独立训练场", "点击下方“进入训练场”，进入守望训练场。这里有独立的场地与两波练习，任务会逐项检查实际操作。");
                    break;
                case 1:
                    Label("训练场示意标题", page, "守望训练场 · 单向来袭 · 两波练习", 0, 4, 848, 58, 29, TextAnchor.MiddleCenter).color = Cyan;
                    Image("训练场上侧围挡左段", page, Sprite("HeaderRule"), 24, 126, 280, 10);
                    Image("训练场上侧围挡右段", page, Sprite("HeaderRule"), 544, 126, 280, 10);
                    Label("上侧围挡说明", page, "北侧围挡", 24, 100, 800, 62, 24, TextAnchor.MiddleCenter);
                    Model(page, models[(int)BuildingKind.Core], "核心", "向前布置供能与火力", 8, 188, 252);
                    Image("训练场部署建筑示意", page, models[(int)BuildingKind.Turret], 324, 216, 210, 190);
                    Label("部署区说明", page, "开放部署区", 290, 414, 280, 48, 27, TextAnchor.MiddleCenter).color = Cyan;
                    Model(page, enemy, "东侧入口", "敌人向核心推进", 612, 188, 220);
                    var direction = Image("敌人推进方向", page, Sprite("ArrowLeft"), 548, 286, 48, 48); direction.color = Amber;
                    Image("训练场下侧围挡左段", page, Sprite("HeaderRule"), 24, 510, 280, 10);
                    Image("训练场下侧围挡右段", page, Sprite("HeaderRule"), 544, 510, 280, 10);
                    Label("下侧围挡说明", page, "南侧围挡", 24, 484, 800, 62, 24, TextAnchor.MiddleCenter);
                    Key(page, "W A S D", "平移", 12, 568, 180); Key(page, "Q / E", "旋转", 224, 568, 180);
                    Key(page, "滚轮", "缩放", 436, 568, 180); Key(page, "Home", "回到核心", 648, 568, 180);
                    Notes(page, "先找到核心与入口", "单击单位查看详情。双击建筑或敌人，镜头会居中并跟随。Home 回到基地。",
                        "读懂通路", "查看敌人入口和下一波情报。墙体占格，也会挡住碰到它的弹体；炮位应朝向开敞通路。",
                        "随时停下来观察", "空格打开暂停菜单。鼠标中键拖动也可平移镜头；滚轮可拉近检查建筑。");
                    break;
                case 2:
                    Model(page, models[(int)BuildingKind.Reactor], "反应堆", "先建立能量来源", 4, 26, 392);
                    Model(page, models[(int)BuildingKind.Turret], "闪哨机", "再布置防御火力", 452, 26, 392);
                    Flow(page, new[] { "选择建筑", "查看预览", "左键放置" }, 20, 482, 256);
                    Key(page, "R", "旋转 90°", 100, 584, 256); Key(page, "右键 / Esc", "取消放置", 484, 584, 256);
                    Notes(page, "底部商店选择建筑", "准备阶段点击建筑卡片，或用数字键 1–8 选择。查看价格与占地后，再决定位置。",
                        "预览不扣资金", "把预览移动到合法空位，按 R 旋转，左键确认后才真正建造。入口、墙体及已有建筑不能重叠。",
                        "放错后可以调整", "准备阶段选中普通建筑可移动或出售。核心不能购买、移动或出售。");
                    break;
                case 3:
                    Label("供电图标题", page, "让能量真正送达炮塔", 0, 0, 848, 52, 30, TextAnchor.MiddleCenter).color = Cyan;
                    Model(page, models[(int)BuildingKind.Reactor], "反应堆", "产生能量", 0, 100, 236);
                    Model(page, models[(int)BuildingKind.Relay], "中继器", "延伸线路", 308, 100, 236);
                    Model(page, models[(int)BuildingKind.Turret], "闪哨机", "储能并开火", 616, 100, 236);
                    Arrow(page, 250, 206, 44); Arrow(page, 558, 206, 44);
                    Image("电池示意", page, models[(int)BuildingKind.Battery], 24, 450, 200, 180);
                    Label("电池说明标题", page, "电池负责缓冲与支援", 260, 476, 560, 44, 30).color = Cyan;
                    Label("电池说明", page, "电池可储存并转送能量。先保证线路连通，再安排备用储能。", 260, 532, 550, 100, 26);
                    Notes(page, "先用覆盖范围摆放", "反应堆可直接为附近建筑供能；超出范围时加入中继器。摆放时查看候选供能覆盖。",
                        "检查实际储能", "打开供电网络，选中炮塔查看储能。线路预约和运输中的能量，还没有进入库存。",
                        "不要只堆炮塔", "能源不足时武器无法持续开火。缩短线路、补充反应堆或电池，比继续加炮更有效。");
                    break;
                case 4:
                    Model(page, models[(int)BuildingKind.Turret], "防御建筑", "自动攻击范围内的目标", 4, 12, 356);
                    Arrow(page, 390, 180, 64);
                    Model(page, enemy, "来袭敌人", "向核心推进", 492, 12, 356);
                    Flow(page, new[] { "准备部署", "开始波次", "自动战斗" }, 20, 476, 256);
                    Label("开波前检查", page, "开波前：有火力 · 有供电 · 射线畅通", 16, 584, 816, 60, 30, TextAnchor.MiddleCenter).color = Amber;
                    Notes(page, "由你决定何时开始", "完成布防后点击“开始波次”。不要只放一座炮就急着开战，先检查入口方向与供电。",
                        "战斗中观察弱点", "战斗时建设、维修和优先级调整锁定。观察敌人的行进路线，点击敌人查看生命与目标。",
                        "速度和暂停", "新手建议保持 1 倍速。需要思考时使用暂停；暂停后可查看战场，恢复后继续自动战斗。");
                    break;
                case 5:
                    Label("整备循环标题", page, "每一波都是一次调整机会", 0, 0, 848, 52, 30, TextAnchor.MiddleCenter).color = Cyan;
                    Flow(page, new[] { "领取波次奖励", "继续准备", "维修与扩建" }, 20, 110, 256);
                    Model(page, models[(int)BuildingKind.Battery], "检查供能", "储能不足就补充线路", 0, 240, 248);
                    Model(page, models[(int)BuildingKind.Turret], "修复防线", "普通建筑可付费维修", 300, 240, 248);
                    Model(page, models[(int)BuildingKind.Core], "保护核心", "核心生命不能维修", 600, 240, 248);
                    Notes(page, "先继续准备", "清波结算后点击“继续准备”，恢复建造。查看下一波情报，再决定维修、扩建或调整朝向。",
                        "维修与资金", "维修消耗资金，只恢复普通建筑耐久。全部维修需要总资金足够；出售按固定价格返还。",
                        "完整通关才能解锁", "击退第一波并继续准备，完成基础操作。再守住第二波，才能通关训练场并解锁第一关；通关与解锁会自动保存。");
                    break;
            }
        }

        private static void BuildPractice(Transform root, SerializedObject data)
        {
            var practice = FullPage("训练场实践引导", root, out var safe);
            // 顶部中间的卡片避开两侧状态栏、右侧详情和底部商店；不遮挡整张地图。
            var expanded = Rect("展开的任务卡", safe, 518, 136, 884, 264.2f);
            Image("任务卡美术底图", expanded, AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Defend/Content/Art/IngameUI/ReferenceExactV8/PanelIntel.png"), 0, 0, 884, 264.2f).raycastTarget = true;
            var heading = Label("实践目标", expanded, "认识核心", 26, 16, 660, 42, 29); heading.fontStyle = FontStyle.Bold;
            var collapse = Button("收起引导", expanded, "收起", 746, 12, 110, 40, false);
            var instruction = Label("实践操作", expanded, "", 26, 66, 828, 80, 24);
            var status = Label("实际操作状态", expanded, "", 26, 146, 828, 48, 21); status.color = Cyan;
            var progress = Label("实践进度", expanded, "0 / 6", 28, 207, 110, 36, 23);
            var nodes = new Image[6];
            for (int i = 0; i < 6; i++) nodes[i] = Image("实践步骤状态 " + (i + 1), expanded, Sprite("IconPending"), 152 + i * 46, 210, 30, 30);
            var track = Image("实践进度底线", expanded, Sprite("HeaderRule"), 28, 194, 828, 10.8f);
            track.color = new Color(.35f, .51f, .58f);
            var fill = Image("实践进度填充", expanded, Sprite("HeaderRule"), 28, 194, 828, 10.8f);
            fill.type = UnityEngine.UI.Image.Type.Filled; fill.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
            fill.preserveAspect = false; fill.color = Cyan; fill.fillAmount = 0;
            var exit = Button("结束引导", expanded, "关闭提示", 662, 212, 190, 42, false);
            var collapsed = Rect("收起的任务卡", safe, 760, 136, 400, 68);
            var expand = Button("展开引导", collapsed, "查看当前引导目标", 0, 0, 400, 68, false);
            Bind(data, "practiceRoot", practice); Bind(data, "practiceExpanded", expanded.gameObject); Bind(data, "practiceCollapsed", collapsed.gameObject);
            Bind(data, "practiceTitle", heading); Bind(data, "practiceInstruction", instruction); Bind(data, "practiceStatus", status); Bind(data, "practiceProgress", progress);
            Bind(data, "practiceProgressFill", fill); Bind(data, "collapsePractice", collapse); Bind(data, "expandPractice", expand); Bind(data, "exitPractice", exit);
            Array(data, "practiceStepIndicators", nodes); Bind(data, "stepCompleted", Sprite("IconCompleted"));
            Bind(data, "stepCurrent", Sprite("IconAvailable")); Bind(data, "stepPending", Sprite("IconPending"));
            practice.SetActive(false);
        }

        private static void Notes(Transform page, params string[] notes)
        {
            for (int i = 0; i < 3; i++)
            {
                int y = 12 + i * 222;
                Label("说明编号 " + i, page, (i + 1).ToString("00"), 922, y, 50, 38, 22).color = Cyan;
                Label("说明标题 " + i, page, notes[i * 2], 986, y - 3, 450, 46, 29).fontStyle = FontStyle.Bold;
                Label("说明内容 " + i, page, notes[i * 2 + 1], 922, y + 58, 498, 148, 25);
                if (i != 2) Image("说明分隔 " + i, page, Sprite("DetailRule"), 922, y + 212, 498, 3.32f);
            }
        }
        private static void Model(Transform parent, Sprite sprite, string name, string detail, float x, float y, float size)
        {
            Image(name + "实物", parent, sprite, x, y, size, size * .82f);
            ModelCaption(parent, name, detail, x, y + size * .84f, size);
        }
        private static void ModelCaption(Transform parent, string title, string detail, float x, float y, float width)
        {
            Label(title + "名称", parent, title, x, y, width, 44, 30, TextAnchor.MiddleCenter).color = Cyan;
            Label(title + "用途", parent, detail, x, y + 50, width, 62, 24, TextAnchor.UpperCenter);
        }
        private static void Key(Transform parent, string key, string purpose, float x, float y, float width)
        {
            Image(key + "键位底图", parent, Sprite("ReturnButton"), x, y, width, 52);
            Label(key + "键位", parent, key, x + 8, y, width - 16, 52, 26, TextAnchor.MiddleCenter).color = Cyan;
            Label(purpose + "操作", parent, purpose, x, y + 60, width, 40, 24, TextAnchor.MiddleCenter);
        }
        private static void Flow(Transform parent, string[] items, float x, float y, float width)
        {
            for (int i = 0; i < items.Length; i++)
            {
                float start = x + i * (width + 24);
                Image("流程底图 " + items[i], parent, Sprite("ReturnButton"), start, y, width, 56);
                Label("流程 " + items[i], parent, items[i], start + 10, y, width - 20, 56, 25, TextAnchor.MiddleCenter);
                if (i < items.Length - 1) Arrow(parent, start + width + 2, y + 18, 20);
            }
        }
        private static void Arrow(Transform parent, float x, float y, float size) => Image("方向箭头", parent, Sprite("ArrowRight"), x, y, size, size).color = Cyan;
        private static Sprite Sprite(string name) => AssetDatabase.LoadAssetAtPath<Sprite>(Art + name + ".png");
        private static GameObject FullPage(string name, Transform parent, out RectTransform safe)
        {
            var page = Rect(name, parent, 0, 0, 0, 0); page.anchorMin = Vector2.zero; page.anchorMax = Vector2.one; page.offsetMin = page.offsetMax = Vector2.zero;
            safe = Rect("引导安全画布", page, 0, 0, 1920, 1080); safe.anchorMin = safe.anchorMax = safe.pivot = new Vector2(.5f, .5f); safe.anchoredPosition = Vector2.zero;
            var layout = new SerializedObject(page.gameObject.AddComponent<OutgameCanvasLayout>());
            Bind(layout, "safeArea", safe); layout.FindProperty("referenceSize").vector2Value = new Vector2(1920, 1080); layout.ApplyModifiedPropertiesWithoutUndo();
            return page.gameObject;
        }
        private static RectTransform Rect(string name, Transform parent, float x, float y, float width, float height)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1); rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height); return rect;
        }
        private static Image Image(string name, Transform parent, Sprite sprite, float x, float y, float width, float height)
        {
            if (sprite == null) throw new InvalidOperationException("引导美术资源缺失：" + name);
            var image = Rect(name, parent, x, y, width, height).gameObject.AddComponent<Image>(); image.sprite = sprite; image.preserveAspect = true; image.raycastTarget = false; return image;
        }
        private static Text Label(string name, Transform parent, string text, float x, float y, float width, float height, int size, TextAnchor alignment = TextAnchor.UpperLeft)
        {
            var label = Rect(name, parent, x, y, width, height).gameObject.AddComponent<Text>(); label.text = text; label.font = font;
            label.fontSize = size; label.color = Ink; label.alignment = alignment; label.lineSpacing = 1.12f; label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Wrap; label.verticalOverflow = VerticalWrapMode.Truncate; return label;
        }
        private static Button Button(string name, Transform parent, string text, float x, float y, float width, float height, bool primary)
        {
            var image = Image(name, parent, Sprite(primary ? "PrimaryButton" : "ReturnButton"), x, y, width, height); image.raycastTarget = true;
            var button = image.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            var colors = button.colors; colors.normalColor = Color.white; colors.highlightedColor = new Color(.63f, 1f, 1f); colors.pressedColor = new Color(.42f, .76f, .8f); colors.selectedColor = new Color(.65f, 1f, 1f); button.colors = colors;
            var label = Label("按钮文字", image.transform, text, 12, 0, width - 24, height, height >= 68 ? 28 : height < 50 ? 20 : 25, TextAnchor.MiddleCenter);
            if (primary) { label.color = new Color(.025f, .12f, .13f); label.fontStyle = FontStyle.Bold; }
            var outline = image.gameObject.AddComponent<Outline>(); outline.effectColor = Cyan; outline.effectDistance = new Vector2(1, 1); outline.enabled = false;
            return button;
        }
        private static void Bind(SerializedObject data, string name, Object value) => data.FindProperty(name).objectReferenceValue = value;
        private static void Array<T>(SerializedObject data, string name, T[] values) where T : Object
        {
            var array = data.FindProperty(name); array.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }
    }
}
