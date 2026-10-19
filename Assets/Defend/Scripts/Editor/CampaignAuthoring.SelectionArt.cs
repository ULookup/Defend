using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Defend.Editor
{
    public static partial class CampaignAuthoring
    {
        public const string SelectionArtPath = "Assets/Defend/Content/Art/CampaignSelectionReference20261019/";
        public static string ImportSelectionArt()
        {
            RequireEditMode();
            string[] names = { "Background", "CardSurface", "CardAccent", "DetailSurface", "PreviewFrame", "PrimaryButton", "ReturnButton",
                "SelectedTab", "HeaderRule", "SectionRule", "DetailRule", "VerticalRule", "Emblem", "IconWaves", "IconCredits", "ArrowLeft", "ArrowRight",
                "IconAvailable", "IconPending", "IconLocked", "IconCompleted",
                "Cover01", "Cover02", "Cover03", "Cover04", "Cover05", "Cover06" };
            foreach (string name in names)
            {
                string path = SelectionArtPath + name + ".png";
                if (!File.Exists(path)) throw new InvalidOperationException("全新选关素材缺失：" + path);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) throw new InvalidOperationException("无法读取新选关图片：" + path);
                importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false; importer.alphaIsTransparency = true; importer.sRGBTexture = true;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.wrapMode = TextureWrapMode.Clamp; importer.filterMode = FilterMode.Bilinear;
                importer.textureCompression = TextureImporterCompression.Uncompressed; importer.maxTextureSize = 4096;
                var textureSettings = new TextureImporterSettings();
                importer.ReadTextureSettings(textureSettings);
                textureSettings.spriteMeshType = SpriteMeshType.FullRect;
                importer.SetTextureSettings(textureSettings);
                importer.SaveAndReimport();
            }
            return names.Length + " 张参考设计选关素材已作为独立 Sprite 导入。";
        }
        private static Sprite SelectionSprite(string name, float aspect)
        {
            string path = SelectionArtPath + name + ".png";
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null) throw new InvalidOperationException("全新选关素材缺失：" + path);
            float actual = sprite.rect.width / sprite.rect.height;
            if (Mathf.Abs(actual / aspect - 1) > .005f)
                throw new InvalidOperationException("素材比例不匹配，禁止裁切或拉伸：" + name + " 实际 " + actual + "，需要 " + aspect);
            return sprite;
        }
        private static void ValidateSelectionImages(GameObject page)
        {
            foreach (var image in page.GetComponentsInChildren<Image>(true))
            {
                string path = image.sprite == null ? "" : AssetDatabase.GetAssetPath(image.sprite);
                if (!path.StartsWith(SelectionArtPath, StringComparison.Ordinal))
                    throw new InvalidOperationException("选关页面不能引用旧素材或空白占位：" + image.name + " " + path);
                var rectangle = image.rectTransform.rect;
                float expected = rectangle.width / rectangle.height;
                float actual = image.sprite.rect.width / image.sprite.rect.height;
                if (!image.preserveAspect || Mathf.Abs(actual / expected - 1) > .005f)
                    throw new InvalidOperationException("选关图片显示比例不匹配：" + image.name);
            }
        }
    }
}
