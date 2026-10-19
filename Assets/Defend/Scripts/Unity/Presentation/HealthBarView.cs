using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Defend.Unity
{
    /// <summary>预制的世界空间生命条；不参与输入，也不跟随受击节点缩放。</summary>
    public sealed class HealthBarView : MonoBehaviour
    {
        [SerializeField] private CanvasGroup visibility;
        [SerializeField] private Image healthFill, delayedFill;
        [SerializeField] private float waitSeconds = .15f, catchupSeconds = .30f, fadeSeconds = .12f;
        [SerializeField] private float minimumWidthPixels = 40, maximumWidthPixels = 160;
        [SerializeField] private TMP_Text amountText;
        // 保留历史 Pixels 字段名；偏移与留白现按 100 宽设计坐标随条等比缩放。
        [SerializeField] private float worldWidth = 1f, screenOffsetPixels;
        // selectedStackClearancePixels 仅保留序列化兼容；选中不再为数字抬高仪表。
        [SerializeField] private float stackClearancePixels, selectedStackClearancePixels;
        private readonly HealthBarState state = new HealthBarState();
        private readonly WorldStatusBarLayout layout = new WorldStatusBarLayout();
        private bool selected;
        public HealthBarState State => state;
        public void Configure(CanvasGroup group, Image immediate, Image delayed) { visibility = group; healthFill = immediate; delayedFill = delayed; }
        public void ConfigurePresentation(TMP_Text valueText, float width = 1f)
        { HideAmountText(); amountText = valueText; worldWidth = width; HideAmountText(); }
        public void ConfigureLayout(float width = 1f, float minPixels = 40, float maxPixels = 160)
        { worldWidth = width; minimumWidthPixels = minPixels; maximumWidthPixels = maxPixels; }
        public void ConfigureScreenOffset(float pixels) { screenOffsetPixels = pixels; }
        public void ConfigureStackClearance(float restingPixels, float selectedPixels)
        { stackClearancePixels = restingPixels; selectedStackClearancePixels = selectedPixels; }
        public void SetSelected(bool value) { if (selected == value) return; selected = value; Render(); }
        public void SetHealth(int health, int maximum)
        {
            state.SetHealth(health, maximum, waitSeconds); Render();
        }
        public void AdvanceFeedback(float seconds) { state.Advance(seconds, catchupSeconds, fadeSeconds); Render(); FaceCamera(); }
        public void ResetState() { selected = false; state.Reset(); Render(); }
        private void Render()
        {
            if (visibility != null) { visibility.alpha = selected && state.Health > 0 ? 1 : state.Opacity; visibility.blocksRaycasts = false; visibility.interactable = false; }
            if (healthFill != null) healthFill.fillAmount = state.Health;
            if (delayedFill != null) delayedFill.fillAmount = state.Trail;
            HideAmountText();
        }
        private void FaceCamera() => layout.Apply(transform, worldWidth,
            selected ? Mathf.Min(maximumWidthPixels, Mathf.Max(64, minimumWidthPixels)) : minimumWidthPixels,
            maximumWidthPixels, screenOffsetPixels + stackClearancePixels);
        // 保留旧资源的序列化引用和配置接口，但头顶仪表始终只显示条形图。
        private void HideAmountText() { if (amountText != null && amountText.gameObject.activeSelf) amountText.gameObject.SetActive(false); }
        private void OnEnable() => HideAmountText();
        // 镜头在暂停时仍可移动；这里只对齐挂点，不推进生命反馈时间。
        private void LateUpdate() => FaceCamera();
        private void OnDisable() => ResetState();
    }
}
