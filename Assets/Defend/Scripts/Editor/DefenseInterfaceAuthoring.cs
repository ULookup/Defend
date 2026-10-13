using System;
using System.Collections.Generic;
using Defend.Unity;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Defend.Editor
{
    /// <summary>只在编辑器制作正式界面。所有可交互对象、页面和滚动内容均持久保存到预制体。</summary>
    public static class DefenseInterfaceAuthoring
    {
        public const string PrefabPath = "Assets/Defend/Content/Prefabs/UI/DefenseInterface.prefab";
        private static readonly Color Back = new Color(.035f, .065f, .105f, .97f);
        private static readonly Color Panel = new Color(.065f, .105f, .16f, .98f);
        private static readonly Color Card = new Color(.10f, .16f, .22f, 1);
        private static readonly Color Accent = new Color(.18f, .72f, .70f, 1);
        private static readonly Color Ink = new Color(.90f, .94f, .97f, 1);
        private static readonly Color Muted = new Color(.59f, .70f, .77f, 1);

        /// <summary>返回保存后的资产；不打开、不保存或替换玩法场景，由场景接入方绑定镜头。</summary>
        public static GameObject CreatePrefab()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请退出播放模式后制作界面。");
            Font font = AssetDatabase.LoadAssetAtPath<Font>(WhiteboxSceneBuilder.ChineseFontPath);
            if (font == null) throw new InvalidOperationException("缺少项目中文字体。");
            WhiteboxSceneBuilder.EnsureFolder("Assets/Defend/Content/Prefabs/UI");
            var root = new GameObject("DefenseInterface", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            try
            {
                root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
                root.GetComponent<Canvas>().sortingOrder = 20;
                var scaler = root.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1280, 800);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
                scaler.matchWidthOrHeight = .5f;
                var controller = root.AddComponent<DefenseInterface>();
                var bindings = new Dictionary<string, Object>();
                var data = new SerializedObject(controller);
                RectTransform hud = Full("局内界面", root.transform);
                bindings["hud"] = hud.gameObject;
                RectTransform viewport = Full("地图可视区域", hud);
                viewport.anchorMin = new Vector2(0, .23f); viewport.anchorMax = new Vector2(.73f, .88f);
                bindings["mapViewport"] = viewport;

                RectTransform header = Box("顶部状态", hud, 0, 0, 1280, 96, Back);
                header.anchorMax = new Vector2(1, 1); header.sizeDelta = new Vector2(0, 96);
                Label("品牌", header, font, "涌现防线", 20, 10, 145, 30, 25, Accent);
                bindings["resources"] = Label("可用资金", header, font, "可用资金", 184, 12, 540, 26, 19);
                bindings["core"] = Label("核心状态", header, font, "核心生命与护盾", 184, 45, 640, 27, 17);
                bindings["wave"] = Label("波次状态", header, font, "第 1 波准备", 20, 73, 1160, 22, 15, Muted);
                bindings["showDetails"] = Button("展开详情", header, font, "查看详情", 985, 14, 118, 44, out _);
                bindings["pause"] = Button("暂停", header, font, "暂停", 1117, 14, 138, 44, out Text pauseText);
                bindings["pauseLabel"] = pauseText;

                RectTransform details = Box("共用对象详情", hud, 0, 96, 346, 520, Panel);
                details.anchorMin = new Vector2(.73f, .23f); details.anchorMax = new Vector2(1, .88f);
                details.offsetMin = Vector2.zero; details.offsetMax = Vector2.zero;
                bindings["details"] = details.gameObject;
                bindings["selectionTitle"] = Label("对象名称", details, font, "防线详情", 20, 14, 248, 34, 24);
                bindings["closeDetails"] = Button("关闭详情", details, font, "收起", 272, 12, 58, 36, out _, false, 15);
                var health = Box("生命条底", details, 20, 60, 306, 9, new Color(.17f, .22f, .28f));
                Image fill = Box("当前生命", health, 0, 0, 306, 9, Accent).GetComponent<Image>();
                fill.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
                fill.type = Image.Type.Filled; fill.fillMethod = Image.FillMethod.Horizontal; fill.fillOrigin = 0;
                fill.raycastTarget = false; bindings["healthFill"] = fill;
                Image portrait = Box("敌人图像", details, 20, 80, 76, 76, Color.white).GetComponent<Image>();
                portrait.preserveAspect = true; portrait.raycastTarget = false;
                bindings["enemyPortrait"] = portrait;
                BindArray(data, "enemyPortraits", new Sprite[5]);
                Text selectionText = ScrollText("实时详情", details, font, "点击建筑或敌人查看详情。", 20, 164, 306, 174, 17);
                bindings["selectionBody"] = selectionText;
                var selectionScroll = (RectTransform)selectionText.transform.parent.parent;
                selectionScroll.anchorMin = Vector2.zero; selectionScroll.anchorMax = Vector2.one;
                selectionScroll.offsetMin = new Vector2(20, 180); selectionScroll.offsetMax = new Vector2(-20, -88);
                bindings["selectionScroll"] = selectionScroll;
                bindings["archiveStory"] = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Defend/Content/EmergentDefense/ArchiveStory.txt");
                if (bindings["archiveStory"] == null) throw new InvalidOperationException("缺少垣城档案背景资料。");
                var locateButton = Button("定位敌人", details, font, "定位敌人", 20, 356, 306, 42, out _);
                bindings["locateEnemy"] = locateButton;
                var locateRect = (RectTransform)locateButton.transform;
                locateRect.anchorMin = Vector2.zero; locateRect.anchorMax = new Vector2(1,0);
                locateRect.pivot = Vector2.zero; locateRect.offsetMin = new Vector2(20,24); locateRect.offsetMax = new Vector2(-20,66);
                RectTransform actions = Rect("建筑操作", details, 20, 350, 306, 154);
                actions.anchorMin = actions.anchorMax = Vector2.zero; actions.pivot = Vector2.zero; actions.anchoredPosition = new Vector2(20,12);
                bindings["buildingActions"] = actions.gameObject;
                bindings["move"] = Button("移动建筑", actions, font, "移动", 0, 0, 93, 40, out _);
                bindings["sell"] = Button("出售建筑", actions, font, "出售", 103, 0, 93, 40, out _);
                bindings["repair"] = Button("维修建筑", actions, font, "维修", 206, 0, 100, 40, out Text repairText);
                repairText.fontSize = 14; bindings["repairLabel"] = repairText;
                Label("优先级说明", actions, font, "供能优先级", 0, 49, 306, 24, 16, Muted);
                var priorities = new Button[3];
                string[] priorityNames = { "较低", "普通", "较高" };
                for (int i = 0; i < priorities.Length; i++) priorities[i] = Button("优先级" + i, actions, font, priorityNames[i], i * 104, 80, 98, 40, out _);
                BindArray(data, "priorityButtons", priorities);

                RectTransform shop = Box("六项建造栏", hud, 0, 0, 1280, 104, Panel);
                shop.anchorMin = shop.anchorMax = Vector2.zero; shop.pivot = Vector2.zero; shop.anchoredPosition = new Vector2(0, 80);
                shop.anchorMax = new Vector2(1, 0); shop.sizeDelta = new Vector2(0, 104);
                bindings["shop"] = shop.gameObject;
                var shopButtons = new Button[6]; var shopLabels = new Text[6];
                string[] names = { "反应堆", "中继器", "电池", "闪哨机", "崩岩机", "荡尘机" };
                for (int i = 0; i < 6; i++) shopButtons[i] = Button("建造" + names[i], shop, font, names[i], 14 + i * 140, 14, 130, 74, out shopLabels[i]);
                bindings["repairAll"] = Button("全部维修", shop, font, "全部维修", 866, 14, 180, 74, out Text allText);
                bindings["repairAllLabel"] = allText;
                bindings["startWave"] = Button("开始波次", shop, font, "开始第 1 波", 1060, 14, 202, 74, out Text startText, true);
                bindings["startWaveLabel"] = startText;
                BindArray(data, "shopButtons", shopButtons); BindArray(data, "shopLabels", shopLabels);

                RectTransform footer = Box("公共操作", hud, 0, 0, 1280, 80, Back);
                footer.anchorMin = Vector2.zero; footer.anchorMax = new Vector2(1, 0); footer.pivot = Vector2.zero;
                footer.anchoredPosition = Vector2.zero; footer.sizeDelta = new Vector2(0, 80);
                bindings["speed"] = Button("调整速度", footer, font, "速度", 14, 16, 76, 48, out _);
                bindings["network"] = Button("供能线路", footer, font, "供能线路", 98, 16, 100, 48, out _);
                bindings["ranges"] = Button("范围显示", footer, font, "范围", 206, 16, 76, 48, out _);
                bindings["cancelPlacement"] = Button("取消放置", footer, font, "取消放置", 290, 16, 100, 48, out _);
                bindings["feedback"] = Label("操作反馈", footer, font, "准备防线，主动开始下一波。", 410, 8, 850, 66, 16, Muted);

                RectTransform start = Page("开始页面", root.transform, bindings, "startPage", 1080, 640);
                Label("游戏名称", start, font, "涌现防线", 72, 100, 850, 78, 56, Accent);
                Label("开始说明", start, font, "部署工事  ·  连接能源  ·  守望垣城", 77, 196, 850, 36, 24, Muted);
                Label("开始简介", start, font, "以能源维系防线，让每一次部署回应来敌。\n准备就绪后主动开波；守住核心，迎接下一次进攻。", 78, 265, 855, 86, 22);
                bindings["startGame"] = Button("开始守望", start, font, "开始守望", 78, 400, 260, 68, out _, true, 24);
                bindings["openHelp"] = Button("操作说明", start, font, "操作说明", 366, 400, 182, 68, out _);
                bindings["openArchive"] = Button("垣城档案", start, font, "垣城档案", 568, 400, 182, 68, out _);
                bindings["openSettings"] = Button("打开设置", start, font, "设置", 770, 400, 120, 68, out _);
                bindings["quit"] = Button("退出游戏", start, font, "退出游戏", 78, 492, 160, 48, out _);
                Label("无对局存档说明", start, font, "每次开始均为新的守望。音量设置会保留。", 78, 568, 880, 30, 17, Muted);

                RectTransform pause = Page("暂停菜单", root.transform, bindings, "pausePage", 620, 660);
                Heading(pause, font, "已暂停", "游戏时间已停止；关闭说明或设置仍保持暂停。");
                bindings["resume"] = Button("继续游戏", pause, font, "继续游戏", 50, 140, 520, 60, out _, true);
                bindings["arrangePaused"] = Button("保持暂停布置", pause, font, "保持暂停，返回布置", 50, 220, 520, 60, out _);
                bindings["pauseHelp"] = Button("暂停说明", pause, font, "操作说明", 50, 300, 250, 58, out _);
                bindings["pauseSettings"] = Button("暂停设置", pause, font, "设置", 320, 300, 250, 58, out _);
                bindings["redeploy"] = Button("重新部署", pause, font, "重新部署", 50, 390, 520, 58, out _);
                bindings["returnHome"] = Button("返回开始界面", pause, font, "返回开始界面", 50, 470, 520, 58, out _);

                RectTransform help = Page("操作说明页面", root.transform, bindings, "helpPage", 1000, 650);
                Heading(help, font, "操作说明", "布置与整备决定防线，战斗期间可观察目标与供能。");
                ScrollText("说明正文", help, font,
                    "一、布置工事\n在准备阶段选择底部建筑，再点击合法位置确认放置。预览不扣款。选中普通建筑可免费移动、出售或付费维修。被摧毁的建筑永久消失，重新购买会建立新建筑。\n\n" +
                    "二、准备与战斗\n主动点击开始波次进入战斗，建设和优先级调整随即锁定。成功清波后领取固定与击杀奖励，点击继续准备再整备。核心生命不能维修，生命归零则失败。\n\n" +
                    "三、能源与电池\n反应堆产生能量，中继器延伸网络。实际到货才进入库存或补充核心护盾；预约不等于到货。电池可同时充放电，经合法线路支援缺能工事，并按储能比例差相互补能。准备、战斗、波次结算持续供能，主动暂停才停止。\n\n" +
                    "四、维修与优先级\n维修价格按缺失耐久向上取整。全部维修先检查总资金，不足则全部不执行。选中炮塔、核心或电池可在准备阶段调整供能优先级。维修不补能源，出售按固定价格返还。\n\n" +
                    "五、敌人与通路\n敌人占据空间，狭路会排队，有空间时可避让。挡路建筑和最终目标分别显示。点击任何敌人可查看实时生命、行动、目标和攻击属性；定位按钮可把镜头移到它的位置。\n\n" +
                    "六、镜头与暂停\n电脑使用中键拖动与滚轮缩放；触屏使用拖动与双指缩放。点击空白取消选择。空格打开暂停菜单，Esc取消放置或打开暂停。准备暂停时可收起菜单继续布置，开波会解除暂停。\n\n" +
                    "七、重新开始\n本作不保存进行中的对局。未结束时重新部署或返回开始界面需要确认；结束后可直接重新部署。音量设置会在下次启动保留。", 42, 122, 916, 424, 20);
                bindings["closeHelp"] = Button("关闭说明", help, font, "返回", 760, 570, 198, 52, out _, true);

                RectTransform archive = Page("垣城档案页面", root.transform, bindings, "archivePage", 1120, 660);
                Heading(archive, font, "垣城档案", "分类、条目与资料；敌人实时状态可在战场点击查看。");
                var categories = new Button[3]; string[] categoryNames = { "背景故事", "防御工事", "守城者" };
                for (int i = 0; i < 3; i++) categories[i] = Button("分类" + i, archive, font, categoryNames[i], 30, 136 + 66 * i, 164, 54, out _);
                var entries = new Button[7]; var entryLabels = new Text[7];
                for (int i = 0; i < 7; i++) entries[i] = Button("条目" + i, archive, font, "条目", 215, 136 + 60 * i, 190, 50, out entryLabels[i]);
                BindArray(data, "archiveCategories", categories); BindArray(data, "archiveEntries", entries); BindArray(data, "archiveEntryLabels", entryLabels);
                var archiveDetail = Box("档案资料", archive, 428, 124, 662, 432, Back);
                bindings["archiveTitle"] = Label("档案标题", archiveDetail, font, "守望垣城", 24, 18, 610, 42, 28, Accent);
                bindings["archiveBody"] = ScrollText("档案正文", archiveDetail, font, "", 24, 78, 610, 328, 20);
                bindings["closeArchive"] = Button("关闭档案", archive, font, "返回", 882, 587, 208, 48, out _, true);

                RectTransform settings = Page("设置页面", root.transform, bindings, "settingsPage", 700, 540);
                Heading(settings, font, "声音设置", "设置立即生效，并在下次启动时保留。");
                bindings["musicVolume"] = Volume("音乐音量", settings, font, 150);
                bindings["effectsVolume"] = Volume("音效音量", settings, font, 255);
                bindings["mute"] = MuteToggle(settings, font, 48, 360);
                bindings["closeSettings"] = Button("关闭设置", settings, font, "返回", 446, 452, 205, 52, out _, true);
                var audio = root.AddComponent<DefenseAudio>(); bindings["audioSettings"] = audio;
                var audioData = new SerializedObject(audio);
                var musicSource = new GameObject("音乐播放").AddComponent<AudioSource>(); musicSource.transform.SetParent(root.transform, false);
                musicSource.playOnAwake = false; musicSource.loop = true; musicSource.spatialBlend = 0;
                var effectsSource = new GameObject("音效播放").AddComponent<AudioSource>(); effectsSource.transform.SetParent(root.transform, false);
                effectsSource.playOnAwake = false; effectsSource.spatialBlend = 0;
                if (audioData.FindProperty("musicSource") != null) audioData.FindProperty("musicSource").objectReferenceValue = musicSource;
                if (audioData.FindProperty("effectsSource") != null) audioData.FindProperty("effectsSource").objectReferenceValue = effectsSource;
                audioData.ApplyModifiedPropertiesWithoutUndo();

                RectTransform settlement = Page("结算页面", root.transform, bindings, "settlementPage", 760, 660);
                bindings["settlementTitle"] = Label("结算标题", settlement, font, "防守完成", 48, 35, 665, 54, 34, Accent);
                bindings["settlementBody"] = ScrollText("结算内容", settlement, font, "", 48, 112, 665, 385, 22);
                // 普通清波和整局结束按钮占用同一行，由权威阶段控制互斥显隐。
                bindings["nextPreparation"] = Button("继续准备", settlement, font, "继续准备", 48, 552, 320, 64, out _, true);
                bindings["settlementPause"] = Button("结算暂停", settlement, font, "暂停 / 恢复能源", 392, 552, 320, 64, out _);
                bindings["resultRestart"] = Button("结束重新部署", settlement, font, "重新部署", 48, 552, 320, 64, out _, true);
                bindings["resultHome"] = Button("结束返回开始", settlement, font, "返回开始界面", 392, 552, 320, 64, out _);

                RectTransform confirmation = Page("离开确认", root.transform, bindings, "confirmationPage", 660, 360);
                Heading(confirmation, font, "确认离开", "");
                bindings["confirmationText"] = Label("确认内容", confirmation, font, "当前对局进度将丢失。", 42, 114, 576, 100, 24);
                bindings["cancelConfirmation"] = Button("取消离开", confirmation, font, "取消", 42, 258, 275, 60, out _);
                bindings["confirm"] = Button("确认离开", confirmation, font, "确认", 343, 258, 275, 60, out _, true);

                foreach (var binding in bindings)
                {
                    SerializedProperty property = data.FindProperty(binding.Key);
                    if (property == null) throw new InvalidOperationException("界面字段不存在：" + binding.Key);
                    property.objectReferenceValue = binding.Value;
                }
                data.ApplyModifiedPropertiesWithoutUndo();
                // 启动资产只显示开始页，避免首帧短暂露出未初始化的对局与其他覆盖层。
                hud.gameObject.SetActive(false);
                foreach (string page in new[] { "pausePage", "helpPage", "archivePage", "settingsPage", "confirmationPage", "settlementPage" }) ((GameObject)bindings[page]).SetActive(false);
                MainMenuArtAuthoring.ApplyToRoot(root);
                SettingsPageArtAuthoring.ApplyToRoot(root);
                ArchivePageArtAuthoring.ApplyToRoot(root);
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                if (prefab == null) throw new InvalidOperationException("正式界面预制体保存失败。");
                AssetDatabase.SaveAssets();
                return prefab;
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void BindArray<T>(SerializedObject data, string name, T[] values) where T : Object
        {
            var array = data.FindProperty(name);
            if (array == null) throw new InvalidOperationException("界面数组字段不存在：" + name);
            array.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        private static RectTransform Rect(string name, Transform parent, float x, float y, float width, float height)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height);
            return rect;
        }
        private static RectTransform Full(string name, Transform parent)
        {
            var rect = Rect(name, parent, 0, 0, 0, 0); rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero; return rect;
        }
        private static RectTransform Box(string name, Transform parent, float x, float y, float width, float height, Color color)
        {
            var rect = Rect(name, parent, x, y, width, height); var image = rect.gameObject.AddComponent<Image>(); image.color = color; return rect;
        }
        private static RectTransform Page(string name, Transform parent, Dictionary<string, Object> bindings, string field, float width, float height)
        {
            var overlay = Full(name, parent); var shade = overlay.gameObject.AddComponent<Image>(); shade.color = new Color(.015f, .035f, .06f, .86f);
            bindings[field] = overlay.gameObject;
            var card = Box("页面内容", overlay, 0, 0, width, height, Panel);
            card.anchorMin = card.anchorMax = card.pivot = new Vector2(.5f, .5f); card.anchoredPosition = Vector2.zero;
            return card;
        }
        private static Text Label(string name, Transform parent, Font font, string content, float x, float y, float width, float height, int size, Color? color = null)
        {
            var rect = Rect(name, parent, x, y, width, height); var text = rect.gameObject.AddComponent<Text>();
            text.font = font; text.text = content; text.fontSize = size; text.color = color ?? Ink;
            text.alignment = TextAnchor.UpperLeft; text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Overflow;
            text.lineSpacing = 1.13f; text.raycastTarget = false; return text;
        }
        private static void Heading(Transform parent, Font font, string title, string subtitle)
        {
            float width = ((RectTransform)parent).sizeDelta.x - 84;
            Label("页面标题", parent, font, title, 42, 30, width, 46, 32, Accent);
            Label("页面引导", parent, font, subtitle, 42, 84, width, 40, 17, Muted);
        }
        private static Button Button(string name, Transform parent, Font font, string content, float x, float y, float width, float height, out Text label, bool primary = false, int size = 18)
        {
            var rect = Box(name, parent, x, y, width, height, primary ? new Color(.12f, .37f, .39f) : Card);
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = rect.GetComponent<Image>();
            var colors = button.colors; colors.highlightedColor = new Color(.74f, .96f, 1); colors.pressedColor = new Color(.52f, .78f, .83f); colors.disabledColor = new Color(.48f, .51f, .56f, .72f); button.colors = colors;
            var navigation = button.navigation; navigation.mode = Navigation.Mode.None; button.navigation = navigation;
            label = Label("按钮文字", rect, font, content, 5, 3, width - 10, height - 6, size); label.alignment = TextAnchor.MiddleCenter;
            return button;
        }
        private static Text ScrollText(string name, Transform parent, Font font, string content, float x, float y, float width, float height, int size)
        {
            var root = Rect(name, parent, x, y, width, height); var scroll = root.gameObject.AddComponent<ScrollRect>();
            var viewport = Full("裁切区域", root); viewport.offsetMax = new Vector2(-16, 0);
            viewport.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, .001f); viewport.gameObject.AddComponent<RectMask2D>();
            var text = Label("可滚动正文", viewport, font, content, 0, 0, width - 20, height, size);
            text.rectTransform.anchorMax = new Vector2(1, 1); text.rectTransform.sizeDelta = new Vector2(-4, height);
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport; scroll.content = text.rectTransform; scroll.horizontal = false; scroll.vertical = true; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 32;
            var track = Box("滚动条", root, width - 10, 0, 8, height, new Color(.12f, .18f, .23f));
            track.anchorMin = new Vector2(1,0); track.anchorMax = Vector2.one; track.offsetMin = new Vector2(-10,0); track.offsetMax = new Vector2(-2,0);
            var scrollbar = track.gameObject.AddComponent<Scrollbar>(); scrollbar.direction = Scrollbar.Direction.BottomToTop;
            var handle = Full("滚动手柄", track); var handleImage = handle.gameObject.AddComponent<Image>(); handleImage.color = Muted;
            scrollbar.handleRect = handle; scrollbar.targetGraphic = handleImage; scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            return text;
        }
        private static Slider Volume(string title, Transform parent, Font font, float y)
        {
            Label(title, parent, font, title, 48, y - 32, 580, 30, 21);
            var rect = Rect(title + "滑块", parent, 48, y + 4, 604, 34); var slider = rect.gameObject.AddComponent<Slider>();
            Box("滑轨", rect, 0, 12, 604, 10, Back);
            var area = Rect("填充区域", rect, 0, 12, 604, 10); var fill = Full("音量", area); fill.gameObject.AddComponent<Image>().color = Accent;
            var handleArea = Rect("手柄区域", rect, 14, 0, 576, 34);
            var handle = Box("手柄", handleArea, 0, 0, 28, 34, Ink); handle.pivot = new Vector2(.5f, .5f);
            slider.fillRect = fill; slider.handleRect = handle; slider.targetGraphic = handle.GetComponent<Image>(); slider.minValue = 0; slider.maxValue = 1; slider.value = .7f;
            return slider;
        }
        private static Toggle MuteToggle(Transform parent, Font font, float x, float y)
        {
            var root = Rect("静音开关", parent, x, y, 360, 48); var toggle = root.gameObject.AddComponent<Toggle>();
            var box = Box("开关背景", root, 0, 6, 34, 34, Card);
            var mark = Box("静音标记", box, 7, 7, 20, 20, Accent).GetComponent<Image>();
            toggle.targetGraphic = box.GetComponent<Image>(); toggle.graphic = mark;
            Label("静音文字", root, font, "静音", 54, 8, 270, 34, 22); return toggle;
        }
    }
}
