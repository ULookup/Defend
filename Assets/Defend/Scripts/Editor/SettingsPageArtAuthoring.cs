using System;
using System.Linq;
using Defend.Unity;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Defend.Editor
{
    /// <summary>在现有设置页上制作和绑定；不重新生成其他页面或玩法场景。</summary>
    public static class SettingsPageArtAuthoring
    {
        private const string Art = "Assets/Defend/Content/Art/Settings/";
        private const string Atlas = Art + "Textures/UI_Settings_Atlas_01.png";
        private static readonly Color Ink = new Color(.90f, .94f, .96f);
        private static readonly Color Cyan = new Color(.63f, .85f, .90f);

        [MenuItem("不眠垣城/应用设置页美术")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出播放模式。");
            ImportSprites();
            var root = PrefabUtility.LoadPrefabContents(DefenseInterfaceAuthoring.PrefabPath);
            try { ApplyToRoot(root); PrefabUtility.SaveAsPrefabAsset(root, DefenseInterfaceAuthoring.PrefabPath); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
        }

        public static void ApplyToRoot(GameObject root)
        {
            var sprites = AssetDatabase.LoadAllAssetsAtPath(Atlas).OfType<Sprite>().ToDictionary(x => x.name);
            if (sprites.Count == 0) return;
            var controller = new SerializedObject(root.GetComponent<DefenseInterface>());
            var overlay = ((GameObject)controller.FindProperty("settingsPage").objectReferenceValue).GetComponent<RectTransform>();
            var card = (RectTransform)(overlay.Find("页面内容") ?? overlay.Find("设置页构图/页面内容"));
            if (card == null) throw new InvalidOperationException("设置页缺少页面内容。");
            Font font = AssetDatabase.LoadAssetAtPath<Font>(WhiteboxSceneBuilder.ChineseFontPath);
            var shade = overlay.GetComponent<UnityEngine.UI.Image>(); shade.color = new Color(.025f, .04f, .055f, .42f); shade.raycastTarget = true;
            var backdrop = Child(overlay, "模糊垣城背景"); Fill(backdrop);
            var bg = Picture(backdrop, AssetDatabase.LoadAssetAtPath<Sprite>(Art + "Textures/BG_Settings_Backdrop_01.png"));
            const string materialPath = Art + "SettingsBackdrop.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null) { material = new Material(Shader.Find("Defend/UI/SettingsBackdrop")); AssetDatabase.CreateAsset(material, materialPath); }
            material.SetFloat("_BlurRadius", 4.5f); EditorUtility.SetDirty(material);
            bg.material = material; bg.color = new Color(.5f, .53f, .57f, 1); bg.raycastTarget = true;
            var aspect = Get<UnityEngine.UI.AspectRatioFitter>(backdrop.gameObject); aspect.enabled = false;
            aspect.aspectMode = UnityEngine.UI.AspectRatioFitter.AspectMode.EnvelopeParent; aspect.aspectRatio = 16f / 9f; Fill(backdrop); aspect.enabled = true;
            backdrop.SetAsFirstSibling();

            // 相对布局只影响设置页，保留根 Canvas 缩放与原有点击路由。
            var composition = Child(overlay, "设置页构图"); Fill(composition);
            var fit = Get<UnityEngine.UI.AspectRatioFitter>(composition.gameObject); fit.enabled = false;
            fit.aspectMode = UnityEngine.UI.AspectRatioFitter.AspectMode.FitInParent; fit.aspectRatio = 16f / 9f; Fill(composition); fit.enabled = true;
            card.SetParent(composition, false); Place(card, 480, 235, 1040, 640);
            Picture(card, sprites["UI_Panel_Settings_Main_01"], true).pixelsPerUnitMultiplier = 2.5f;

            var pageHeading = Child(composition, "设置标题牌"); Place(pageHeading, 54, 56, 515, 100);
            Picture(pageHeading, sprites["UI_Button_Settings_Close_Bottom_Normal"], true).pixelsPerUnitMultiplier = 2;
            Fill(Label(pageHeading, "设置标题文字", font, "设置", 0, 0, 515, 100, 42, new Color(.16f, .18f, .19f), TextAnchor.MiddleCenter).rectTransform);

            var title = card.Find("页面标题").GetComponent<UnityEngine.UI.Text>();
            title.text = "声音设置"; title.fontSize = 26; title.color = Ink; Position(title.rectTransform, 72, 38, 700, 55);
            card.Find("页面引导").gameObject.SetActive(false);
            var line = Child(card, "UI_Frame_Settings_Line_01"); Position(line, 72, 105, 896, 4);
            Picture(line, sprites["UI_Frame_Settings_Line_01"]);
            var legacyMute = (UnityEngine.UI.Toggle)controller.FindProperty("mute").objectReferenceValue; legacyMute.gameObject.SetActive(false);

            var music = (UnityEngine.UI.Slider)controller.FindProperty("musicVolume").objectReferenceValue;
            var effects = (UnityEngine.UI.Slider)controller.FindProperty("effectsVolume").objectReferenceValue;
            StyleSlider(music, card, font, "背景音乐", "音乐音量", 174, sprites);
            StyleSlider(effects, card, font, "游戏音效", "音效音量", 346, sprites);
            var musicValue = Label(card, "音乐当前数值", font, "70", 785, 228, 82, 52, 28, Cyan, TextAnchor.MiddleCenter);
            var effectsValue = Label(card, "音效当前数值", font, "70", 785, 400, 82, 52, 28, Cyan, TextAnchor.MiddleCenter);
            var musicMute = Button(card, "背景音乐静音", font, "", 900, 223, 65, 65, sprites["UI_Button_Settings_Mute_Normal"], sprites["UI_Button_Settings_Mute_Pressed"]);
            var effectsMute = Button(card, "游戏音效静音", font, "", 900, 395, 65, 65, sprites["UI_Button_Settings_Mute_Normal"], sprites["UI_Button_Settings_Mute_Pressed"]);
            Picture(Child(card, "UI_Icon_Settings_Music_01"), sprites["UI_Icon_Settings_Music_01"]);
            Position((RectTransform)card.Find("UI_Icon_Settings_Music_01"), 73, 174, 34, 34);
            Picture(Child(card, "UI_Icon_Settings_SFX_01"), sprites["UI_Icon_Settings_SFX_01"]);
            Position((RectTransform)card.Find("UI_Icon_Settings_SFX_01"), 73, 346, 34, 34);

            var close = (UnityEngine.UI.Button)controller.FindProperty("closeSettings").objectReferenceValue;
            Position((RectTransform)close.transform, 732, 535, 235, 62);
            StyleButton(close, sprites["UI_Button_Settings_Close_Bottom_Normal"], sprites["UI_Button_Settings_Close_Bottom_Pressed"]);
            var closeLabel = close.GetComponentInChildren<UnityEngine.UI.Text>(); Fill(closeLabel.rectTransform);
            closeLabel.text = "关闭"; closeLabel.fontSize = 24; closeLabel.color = new Color(.1f, .16f, .2f);
            var topClose = Button(card, "关闭设置右上角", font, "×", 928, 36, 52, 52, sprites["UI_Button_Settings_Close_Normal"], sprites["UI_Button_Settings_Close_Pressed"]);
            close.gameObject.SetActive(false);
            var oldHint = card.Find("设置保存提示");
            if (oldHint != null) Object.DestroyImmediate(oldHint.gameObject);
            for (int i = 0; i < 4; i++)
            {
                var corner = Child(card, "UI_Decal_Settings_Corner_01_" + i);
                Picture(corner, sprites["UI_Decal_Settings_Corner_01"]);
                Position(corner, i % 2 == 0 ? 20 : 980, i < 2 ? 20 : 580, 40, 40);
                corner.localScale = new Vector3(i % 2 == 0 ? 1 : -1, i < 2 ? 1 : -1, 1);
            }
            var view = Get<DefenseSettingsView>(overlay.gameObject); var binding = new SerializedObject(view);
            Set(binding, "musicSlider", music); Set(binding, "effectsSlider", effects);
            Set(binding, "musicMute", musicMute); Set(binding, "effectsMute", effectsMute);
            Set(binding, "topClose", topClose); Set(binding, "bottomClose", close);
            Set(binding, "musicValue", musicValue); Set(binding, "effectsValue", effectsValue);
            Set(binding, "muteNormal", sprites["UI_Button_Settings_Mute_Normal"]); Set(binding, "mutePressed", sprites["UI_Button_Settings_Mute_Pressed"]); Set(binding, "muteOff", sprites["UI_Button_Settings_Mute_Off"]);
            binding.ApplyModifiedPropertiesWithoutUndo(); Set(controller, "settingsView", view); controller.ApplyModifiedPropertiesWithoutUndo();
            // 在弹窗内闭环导航，不会选到背景开始页上的按钮。
            UnityEngine.UI.Selectable[] navigation = { music, musicMute, effects, effectsMute, topClose };
            for (int i = 0; i < navigation.Length; i++)
            {
                var nav = navigation[i].navigation; nav.mode = UnityEngine.UI.Navigation.Mode.Explicit;
                nav.selectOnUp = navigation[(i + navigation.Length - 1) % navigation.Length]; nav.selectOnDown = navigation[(i + 1) % navigation.Length]; navigation[i].navigation = nav;
            }
        }

        private static void StyleSlider(UnityEngine.UI.Slider slider, RectTransform parent, Font font, string text, string oldName, float y, System.Collections.Generic.Dictionary<string, Sprite> sprites)
        {
            var label = parent.Find(oldName).GetComponent<UnityEngine.UI.Text>(); label.text = text; label.fontSize = 32; label.color = Ink;
            Position(label.rectTransform, 125, y - 4, 640, 60);
            Position((RectTransform)slider.transform, 92, y + 60, 655, 44);
            slider.minValue = 0; slider.maxValue = 100; slider.wholeNumbers = true; slider.SetValueWithoutNotify(70);
            var track = (RectTransform)slider.transform.Find("滑轨");
            Stretch(track, new Vector2(0,.32f), new Vector2(1,.68f)); Picture(track, sprites["UI_Slider_Settings_Track_01"], true).pixelsPerUnitMultiplier = 1.5f;
            var fillArea = (RectTransform)slider.fillRect.parent; Stretch(fillArea, new Vector2(.028f,.35f), new Vector2(.972f,.65f));
            Picture(slider.fillRect, sprites["UI_Slider_Settings_Fill_01"], true).pixelsPerUnitMultiplier = 1.5f;
            var handleArea = (RectTransform)slider.handleRect.parent; Stretch(handleArea, new Vector2(.028f,.5f), new Vector2(.972f,.5f));
            var handle = slider.handleRect; handle.anchorMin = handle.anchorMax = new Vector2(0,.5f); handle.sizeDelta = new Vector2(42, 42); handle.anchoredPosition = Vector2.zero;
            var handleImage = Picture(handle, sprites["UI_Slider_Settings_Handle_Normal"]); handleImage.raycastTarget = true;
            slider.targetGraphic = handleImage; slider.transition = UnityEngine.UI.Selectable.Transition.SpriteSwap;
            var states = slider.spriteState; states.highlightedSprite = states.pressedSprite = states.selectedSprite = sprites["UI_Slider_Settings_Handle_Pressed"]; slider.spriteState = states;
            // 整行均可点击／拖动，不只响应细轨道或圆形手柄。
            var hit = Get<UnityEngine.UI.Image>(slider.gameObject); hit.color = Color.clear; hit.raycastTarget = true;
        }

        private static void ImportSprites()
        {
            var backdropImporter = (TextureImporter)AssetImporter.GetAtPath(Art + "Textures/BG_Settings_Backdrop_01.png");
            if (backdropImporter == null) throw new InvalidOperationException("缺少设置页背景。");
            backdropImporter.textureType = TextureImporterType.Sprite; backdropImporter.spriteImportMode = SpriteImportMode.Single;
            backdropImporter.mipmapEnabled = true; backdropImporter.filterMode = FilterMode.Trilinear; backdropImporter.wrapMode = TextureWrapMode.Clamp;
            backdropImporter.maxTextureSize = 1024; backdropImporter.textureCompression = TextureImporterCompression.Uncompressed; backdropImporter.npotScale = TextureImporterNPOTScale.None; backdropImporter.SaveAndReimport();
            string[] names = { "UI_Panel_Settings_Main_01", "UI_Frame_Settings_Line_01", "UI_Button_Settings_Close_Normal", "UI_Button_Settings_Close_Pressed", "UI_Decal_Settings_Corner_01", "UI_Icon_Settings_Music_01", "UI_Icon_Settings_SFX_01", "UI_Slider_Settings_Track_01", "UI_Slider_Settings_Fill_01", "UI_Slider_Settings_Handle_Normal", "UI_Slider_Settings_Handle_Pressed", "UI_Button_Settings_Mute_Normal", "UI_Button_Settings_Mute_Pressed", "UI_Button_Settings_Mute_Off", "UI_Button_Settings_Close_Bottom_Normal", "UI_Button_Settings_Close_Bottom_Pressed" };
            // 已核对 1254×1254 图集的透明边界；以下为左上原点的包围框。
            Rect[] rects = { new Rect(60,90,426,239), new Rect(503,218,276,29), new Rect(802,158,170,164), new Rect(1033,158,169,165), new Rect(60,429,150,143), new Rect(376,430,164,182), new Rect(675,453,153,165), new Rect(906,484,310,95), new Rect(45,771,320,67), new Rect(417,718,162,165), new Rect(685,717,168,171), new Rect(955,720,231,170), new Rect(47,983,238,172), new Rect(360,983,177,170), new Rect(598,1006,312,137), new Rect(933,1006,287,137) };
            var importer = (TextureImporter)AssetImporter.GetAtPath(Atlas);
            if (importer == null) throw new InvalidOperationException("缺少设置页图集。");
            importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = 100; importer.mipmapEnabled = false; importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed; importer.npotScale = TextureImporterNPOTScale.None; importer.maxTextureSize = 2048; importer.filterMode = FilterMode.Bilinear;
            var metadata = new SpriteMetaData[names.Length];
            for (int i = 0; i < metadata.Length; i++)
            {
                var rect = rects[i]; rect.y = 1254 - rect.y - rect.height;
                Vector4 border = i == 0 ? new Vector4(38,38,38,38) : i == 7 || i == 8 ? new Vector4(42,16,42,16) : i >= 14 ? new Vector4(38,28,38,28) : Vector4.zero;
                metadata[i] = new SpriteMetaData { name = names[i], rect = rect, border = border, alignment = (int)SpriteAlignment.Center, pivot = new Vector2(.5f,.5f) };
            }
#pragma warning disable CS0618
            importer.spritesheet = metadata;
#pragma warning restore CS0618
            importer.SaveAndReimport();
        }

        private static void Set(SerializedObject data, string field, Object value) { data.FindProperty(field).objectReferenceValue = value; }
        private static T Get<T>(GameObject target) where T : Component { return target.GetComponent<T>() ?? target.AddComponent<T>(); }
        private static RectTransform Child(Transform parent, string name) { var old = parent.Find(name); if (old != null) return (RectTransform)old; var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent,false); return rect; }
        private static UnityEngine.UI.Image Picture(RectTransform rect, Sprite sprite, bool sliced = false) { var image = Get<UnityEngine.UI.Image>(rect.gameObject); image.sprite = sprite; image.color = Color.white; image.raycastTarget = false; image.type = sliced ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple; return image; }
        private static void Fill(RectTransform rect) { Stretch(rect, Vector2.zero, Vector2.one); }
        private static void Stretch(RectTransform rect, Vector2 min, Vector2 max) { rect.anchorMin = min; rect.anchorMax = max; rect.pivot = new Vector2(.5f,.5f); rect.anchoredPosition = Vector2.zero; rect.sizeDelta = Vector2.zero; }
        private static void Place(RectTransform rect, float x, float y, float w, float h) { Stretch(rect, new Vector2(x/1920,1-(y+h)/1080), new Vector2((x+w)/1920,1-y/1080)); }
        // 1040×640 的卡片局部位置，同样使用相对锚点，避免窗口大小变化后溢出。
        private static void Position(RectTransform rect, float x, float y, float w, float h) { Stretch(rect,new Vector2(x/1040,1-(y+h)/640),new Vector2((x+w)/1040,1-y/640)); }
        private static UnityEngine.UI.Text Label(Transform parent, string name, Font font, string content, float x, float y, float w, float h, int size, Color color, TextAnchor alignment)
        {
            var rect = Child(parent,name); Position(rect,x,y,w,h); var label = Get<UnityEngine.UI.Text>(rect.gameObject);
            label.font = font; label.text = content; label.fontSize = size; label.color = color; label.alignment = alignment; label.raycastTarget = false;
            label.verticalOverflow = VerticalWrapMode.Overflow; return label;
        }
        private static UnityEngine.UI.Button Button(Transform parent, string name, Font font, string label, float x, float y, float w, float h, Sprite normal, Sprite pressed)
        {
            var rect = Child(parent,name); Position(rect,x,y,w,h); var button = Get<UnityEngine.UI.Button>(rect.gameObject);
            StyleButton(button,normal,pressed); var text = Label(rect,"按钮文字",font,label,0,0,1040,640,32,Ink,TextAnchor.MiddleCenter); Fill(text.rectTransform); return button;
        }
        private static void StyleButton(UnityEngine.UI.Button button, Sprite normal, Sprite pressed)
        {
            var image = Picture((RectTransform)button.transform,normal); image.raycastTarget = true; button.targetGraphic = image;
            if (normal.border != Vector4.zero) image.type = UnityEngine.UI.Image.Type.Sliced;
            button.transition = UnityEngine.UI.Selectable.Transition.SpriteSwap; var states = button.spriteState; states.highlightedSprite = states.pressedSprite = states.selectedSprite = pressed; button.spriteState = states;
        }
    }
}
