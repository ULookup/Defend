using UnityEngine;
using UnityEngine.UI;

namespace Defend.Unity
{
    /// <summary>兼容既有文本绑定，以已保存的生图字样显示固定内容。数值及用户输入继续使用字体。</summary>
    public sealed class GeneratedArtLabel : MonoBehaviour
    {
        [SerializeField] private Text source;
        [SerializeField] private Image artwork;
        [SerializeField] private IngameArtLibrary library;
        [SerializeField] private string fixedKey;
        private string last;
        public void Configure(Text text, Image image, IngameArtLibrary art, string key = null)
        { source = text; artwork = image; library = art; fixedKey = key; Refresh(); }
        private void OnEnable() { last = null; Refresh(); }
        private void LateUpdate() { Refresh(); }
        private void Refresh()
        {
            if (source == null || artwork == null || library == null) return;
            string value = string.IsNullOrEmpty(fixedKey) ? source.text.Trim().TrimStart('▶', ' ') : fixedKey;
            if (value == last) return;
            last = value;
            // 单位由数值列与输入规则提供；参数名称使用统一生成字样。
            value = value.Replace("（秒）", "").Replace("（格）", "").Replace("（格/秒）", "");
            if (value == "每秒最大充电量") value = "最大充电量";
            if (value == "每次射击耗能") value = "射击耗能";
            if (value.StartsWith("删除出生点后")) value = "删除后需重新分配出生点";
            if (value.StartsWith("提前结束将")) value = "提前结束将移除敌人与弹丸";
            if (value.StartsWith("恢复开战前的建筑")) value = "恢复开战前的完整状态";
            if (value.StartsWith("载入方案会")) value = "载入后满生命、空储能";
            if (value.StartsWith("重新开始沙盒将") || value.StartsWith("返回开始页将")) value = "当前对局进度将丢失";
            var sprite = library.Find(value);
            artwork.sprite = sprite;
            artwork.enabled = sprite != null;
            source.enabled = sprite == null;
        }
    }
}
