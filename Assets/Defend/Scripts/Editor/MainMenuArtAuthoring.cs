using System;
using System.Linq;
using Defend.Unity;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Defend.Editor
{
    /// <summary>仅更新开始页的现有控件；保留正式界面与场景的序列化引用。</summary>
    public static class MainMenuArtAuthoring
    {
        private const string Art = "Assets/Defend/Content/Art/MainMenu/Textures/";

        [MenuItem("不眠垣城/应用开始页美术")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出播放模式。");
            ImportArt();
            var root = PrefabUtility.LoadPrefabContents(DefenseInterfaceAuthoring.PrefabPath);
            try { ApplyToRoot(root); PrefabUtility.SaveAsPrefabAsset(root, DefenseInterfaceAuthoring.PrefabPath); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
        }

        public static void ApplyToRoot(GameObject root)
        {
            var background = AssetDatabase.LoadAssetAtPath<Sprite>(Art + "BG_Main_defend_01.png");
            if (background == null) return; // 基础制作工具仍可用于没有美术包的开发项目。
            var controller = new SerializedObject(root.GetComponent<DefenseInterface>());
            var page = ((GameObject)controller.FindProperty("startPage").objectReferenceValue).GetComponent<RectTransform>();
            var content = (RectTransform)page.Find("页面内容");
            if (content == null) throw new InvalidOperationException("开始页缺少页面内容。");
            ClearPanel(page); ClearPanel(content);

            var bg = Child(page, "BG_Main_defend_01");
            var bgImage = Get<UnityEngine.UI.Image>(bg.gameObject);
            bgImage.sprite = background; bgImage.color = Color.white; bgImage.raycastTarget = false;
            var cover = Get<UnityEngine.UI.AspectRatioFitter>(bg.gameObject);
            cover.enabled = false;
            cover.aspectMode = UnityEngine.UI.AspectRatioFitter.AspectMode.EnvelopeParent;
            cover.aspectRatio = background.rect.width / background.rect.height;
            Fill(bg); cover.enabled = true;
            bg.SetAsFirstSibling();

            var composition = Get<UnityEngine.UI.AspectRatioFitter>(content.gameObject);
            composition.enabled = false;
            composition.aspectMode = UnityEngine.UI.AspectRatioFitter.AspectMode.FitInParent;
            composition.aspectRatio = 16f / 9f;
            Fill(content); composition.enabled = true;

            content.Find("游戏名称").GetComponent<UnityEngine.UI.Text>().enabled = false;
            var logo = Child(content, "UI_Logo_Main_01");
            var logoImage = Get<UnityEngine.UI.Image>(logo.gameObject);
            logoImage.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Art + "UI_Logo_Main_01.png");
            logoImage.color = Color.white; logoImage.preserveAspect = true; logoImage.raycastTarget = false;
            Place(logo, 100, 64, 700, 215);

            var tagline = content.Find("开始说明").GetComponent<UnityEngine.UI.Text>();
            tagline.text = "城不靠人管，\n城自己管自己。";
            tagline.fontSize = 26; tagline.color = new Color(.10f, .12f, .14f);
            tagline.alignment = TextAnchor.MiddleLeft; tagline.lineSpacing = 1.3f;
            Place(tagline.rectTransform, 820, 125, 410, 110);
            content.Find("开始简介").gameObject.SetActive(false);

            var sprites = AssetDatabase.LoadAllAssetsAtPath(Art + "UI_Main_ButtonStates_01.png").OfType<Sprite>().ToDictionary(x => x.name);
            string[] fields = { "startGame", "openHelp", "openArchive", "openSettings" };
            string[] names = { "Start", "Guide", "Archive", "Settings" };
            string[] labels = { "开始守望", "操作说明", "垣城档案", "设置" };
            var buttons = new UnityEngine.UI.Button[4];
            for (int i = 0; i < fields.Length; i++)
            {
                var button = (UnityEngine.UI.Button)controller.FindProperty(fields[i]).objectReferenceValue;
                buttons[i] = button;
                var rect = (RectTransform)button.transform;
                if (i == 0) Place(rect, 1370, 462, 510, 182);
                else Place(rect, 1450, 650 + (i - 1) * 124, 420, 132);
                rect.localRotation = Quaternion.Euler(0, 0, i == 0 ? 3 : -6);
                var image = button.GetComponent<UnityEngine.UI.Image>();
                image.sprite = sprites["UI_Button_Main_" + names[i] + "_Normal"];
                image.color = Color.white; image.type = UnityEngine.UI.Image.Type.Simple; image.raycastTarget = true;
                button.transition = UnityEngine.UI.Selectable.Transition.SpriteSwap;
                Sprite pressed = sprites["UI_Button_Main_" + names[i] + "_Pressed"];
                var states = button.spriteState; states.highlightedSprite = pressed; states.pressedSprite = pressed; states.selectedSprite = image.sprite;
                button.spriteState = states;
                var text = button.GetComponentInChildren<UnityEngine.UI.Text>(true);
                Fill(text.rectTransform); text.text = labels[i]; text.fontSize = i == 0 ? 42 : 28;
                text.fontStyle = FontStyle.Bold; text.alignment = TextAnchor.MiddleCenter; text.raycastTarget = false;
                Get<MainMenuButtonFeedback>(button.gameObject).Configure(text,
                    i == 0 ? new Color(0, .55f, .67f) : new Color(.07f, .10f, .13f), new Color(.85f, 1, 1), FocusIndicator(button));
            }
            var quit = (UnityEngine.UI.Button)controller.FindProperty("quit").objectReferenceValue;
            Place((RectTransform)quit.transform, 1690, 35, 190, 52);
            quit.GetComponent<UnityEngine.UI.Image>().color = new Color(.015f, .03f, .045f, .65f);
            var quitText = quit.GetComponentInChildren<UnityEngine.UI.Text>(); Fill(quitText.rectTransform);
            quitText.text = "退出游戏"; quitText.fontSize = 18;
            Get<MainMenuButtonFeedback>(quit.gameObject).Configure(quitText, quitText.color, new Color(.85f, 1, 1), FocusIndicator(quit));
            for (int i = 0; i < buttons.Length; i++)
            {
                var navigation = buttons[i].navigation; navigation.mode = UnityEngine.UI.Navigation.Mode.Explicit;
                navigation.selectOnUp = i == 0 ? quit : buttons[i - 1];
                navigation.selectOnDown = i == 3 ? quit : buttons[i + 1]; buttons[i].navigation = navigation;
            }
            var quitNav = quit.navigation; quitNav.mode = UnityEngine.UI.Navigation.Mode.Explicit;
            quitNav.selectOnDown = buttons[0]; quitNav.selectOnUp = buttons[3]; quit.navigation = quitNav;

            var footer = content.Find("无对局存档说明").GetComponent<UnityEngine.UI.Text>();
            footer.text = "新的守望，从部署防线开始。"; footer.fontSize = 17; footer.color = new Color(.9f, .96f, 1f, .85f);
            Place(footer.rectTransform, 90, 1008, 780, 32);
            var shadow = Get<UnityEngine.UI.Shadow>(footer.gameObject);
            shadow.effectColor = new Color(0, .04f, .08f, .9f); shadow.effectDistance = new Vector2(1, -1);
        }

        public static void ImportArt()
        {
            ImportSingle("BG_Main_defend_01", 2048);
            ImportSingle("UI_Logo_Main_01", 4096);
            string path = Art + "UI_Main_ButtonStates_01.png";
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer == null) throw new InvalidOperationException("缺少按钮美术。");
            Configure(importer, 2048); importer.spriteImportMode = SpriteImportMode.Multiple;
            var definitions = new SpriteMetaData[8];
            string[] names = { "Start", "Guide", "Archive", "Settings" };
            for (int i = 0; i < 4; i++) for (int state = 0; state < 2; state++)
            {
                // 图集尺寸 1254；Sprite 使用左下原点，包含两像素透明边。
                Rect rect = i == 0 ? (state == 0 ? new Rect(7, 758, 614, 222) : new Rect(633, 758, 615, 222))
                    : (state == 0 ? new Rect(10, 171, 604, 198) : new Rect(637, 172, 606, 197));
                definitions[i * 2 + state] = new SpriteMetaData { name = "UI_Button_Main_" + names[i] + (state == 0 ? "_Normal" : "_Pressed"), rect = rect, alignment = (int)SpriteAlignment.Center, pivot = new Vector2(.5f, .5f) };
            }
            // 兼容项目当前未安装 2D Sprite 包的导入器；不为菜单新增包依赖。
#pragma warning disable CS0618
            importer.spritesheet = definitions;
#pragma warning restore CS0618
            importer.SaveAndReimport();
        }

        private static void ImportSingle(string name, int maxSize)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(Art + name + ".png");
            if (importer == null) throw new InvalidOperationException("缺少美术：" + name);
            Configure(importer, maxSize); importer.spriteImportMode = SpriteImportMode.Single; importer.SaveAndReimport();
        }
        private static void Configure(TextureImporter importer, int maxSize)
        {
            importer.textureType = TextureImporterType.Sprite; importer.spritePixelsPerUnit = 100;
            importer.alphaIsTransparency = true; importer.mipmapEnabled = false; importer.isReadable = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed; importer.maxTextureSize = maxSize;
            importer.filterMode = FilterMode.Bilinear; importer.wrapMode = TextureWrapMode.Clamp;
            importer.npotScale = TextureImporterNPOTScale.None;
        }
        private static GameObject FocusIndicator(UnityEngine.UI.Button button)
        {
            var root = Child(button.transform, "键盘焦点边角"); Fill(root);
            for (int i = 0; i < 4; i++)
            {
                var corner = Child(root, "边角" + i);
                corner.anchorMin = corner.anchorMax = new Vector2(i % 2 == 0 ? .04f : .96f, i < 2 ? .14f : .86f);
                corner.pivot = new Vector2(i % 2 == 0 ? 0 : 1, i < 2 ? 0 : 1);
                corner.anchoredPosition = Vector2.zero; corner.sizeDelta = new Vector2(18, 18);
                for (int j = 0; j < 2; j++)
                {
                    var stroke = Child(corner, j == 0 ? "横线" : "竖线"); Fill(stroke);
                    if (j == 0) { stroke.anchorMin = new Vector2(0, i < 2 ? 0 : .84f); stroke.anchorMax = new Vector2(1, i < 2 ? .16f : 1); }
                    else { stroke.anchorMin = new Vector2(i % 2 == 0 ? 0 : .84f, 0); stroke.anchorMax = new Vector2(i % 2 == 0 ? .16f : 1, 1); }
                    var image = Get<UnityEngine.UI.Image>(stroke.gameObject);
                    image.color = new Color(.05f, .78f, .9f); image.raycastTarget = false;
                }
            }
            return root.gameObject;
        }
        private static T Get<T>(GameObject target) where T : Component { return target.GetComponent<T>() ?? target.AddComponent<T>(); }
        private static RectTransform Child(Transform parent, string name)
        {
            var existing = parent.Find(name); if (existing != null) return (RectTransform)existing;
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent, false); return rect;
        }
        private static void ClearPanel(RectTransform rect) { var image = rect.GetComponent<UnityEngine.UI.Image>(); if (image != null) { image.color = Color.clear; image.raycastTarget = false; } }
        private static void Fill(RectTransform rect) { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.pivot = new Vector2(.5f, .5f); rect.anchoredPosition = Vector2.zero; rect.sizeDelta = Vector2.zero; rect.localRotation = Quaternion.identity; }
        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = new Vector2(x / 1920, 1 - (y + height) / 1080);
            rect.anchorMax = new Vector2((x + width) / 1920, 1 - y / 1080);
            rect.pivot = new Vector2(.5f, .5f); rect.anchoredPosition = Vector2.zero; rect.sizeDelta = Vector2.zero;
        }
    }
}
