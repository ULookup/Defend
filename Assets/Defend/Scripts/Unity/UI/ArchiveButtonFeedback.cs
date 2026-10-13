using UnityEngine;
using UnityEngine.UI;

namespace Defend.Unity
{
    /// <summary>依据按钮实际显示的底图调整文字，兼容悬停、导航、鼠标和键盘按压。</summary>
    [RequireComponent(typeof(Button))]
    public sealed class ArchiveButtonFeedback : MonoBehaviour
    {
        [SerializeField] private Text label;
        [SerializeField] private Sprite darkBackground;
        [SerializeField] private Color normalInk = new Color(.17f, .20f, .23f);
        [SerializeField] private Color lightInk = new Color(.85f, 1f, 1f);
        private Image background;

        public void Configure(Text text, Sprite dark, Color normal, Color light)
        {
            label = text; darkBackground = dark; normalInk = normal; lightInk = light;
            Refresh();
        }

        private void OnEnable() { Refresh(); }
        // Button 的键盘提交会在协程中恢复底图；跟随实际底图可避免颜色滞留。
        private void LateUpdate() { Refresh(); }
        private void Refresh()
        {
            if (label == null) return;
            if (background == null) background = GetComponent<Button>().targetGraphic as Image;
            bool dark = darkBackground != null && background != null && background.overrideSprite == darkBackground;
            Color ink = dark ? lightInk : normalInk;
            if (label.color != ink) label.color = ink;
        }
    }
}
