using System.IO;
using System;
using System.Text;
using Defend.Unity;
using UnityEditor;
using UnityEngine;

namespace Defend.Editor
{
    /// <summary>开始页画面验收入口；只读取对象，不保存运行中的场景。</summary>
    public static class MainMenuReview
    {
        [MenuItem("不眠垣城/检查当前画面", true)]
        private static bool CanInspect() { return EditorApplication.isPlaying && !EditorApplication.isCompiling; }

        [MenuItem("不眠垣城/检查当前画面")]
        public static void Inspect()
        {
            var ui = UnityEngine.Object.FindAnyObjectByType<DefenseInterface>();
            if (ui == null) throw new InvalidOperationException("当前场景没有正式界面。");
            var canvas = ui.GetComponent<Canvas>();
            var builder = new StringBuilder();
            builder.AppendLine($"Playing={Application.isPlaying} Canvas={canvas.enabled} Active={canvas.gameObject.activeInHierarchy} Mode={canvas.renderMode} Display={canvas.targetDisplay} Screen={Screen.width}x{Screen.height}");
            foreach (var image in ui.GetComponentsInChildren<UnityEngine.UI.Image>())
            {
                if (!image.name.Contains("Main") && image.name != "开始守望") continue;
                if (float.IsNaN(image.rectTransform.rect.width) || float.IsNaN(image.rectTransform.rect.height))
                    throw new InvalidOperationException("开始页尺寸无效：" + image.name);
                builder.AppendLine($"{image.name}: enabled={image.enabled} active={image.gameObject.activeInHierarchy} rect={image.rectTransform.rect} cull={image.canvasRenderer.cull} alpha={image.canvasRenderer.GetAlpha()} material={image.material.shader.name} sprite={image.sprite?.name}");
            }
            string path = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Docs/Art/MainMenu/Checks/Canvas.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, builder.ToString());
            Debug.Log(builder.ToString());
        }
    }
}
