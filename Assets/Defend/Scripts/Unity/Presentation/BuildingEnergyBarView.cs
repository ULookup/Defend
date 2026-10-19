using Defend.Simulation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Defend.Unity
{
    /// <summary>局内储能仪表只显示已到达的真实存量，连通状态单独表达。</summary>
    public sealed class BuildingEnergyBarView : MonoBehaviour
    {
        [SerializeField] private CanvasGroup visibility;
        [SerializeField] private Image energyFill, arrivalHighlight, statusIcon;
        [SerializeField] private TMP_Text amountText;
        [SerializeField] private Sprite disconnectedSprite, shortageSprite, batterySupportSprite;
        // 最小/最大宽度为 1080 高度参考像素；历史 screenOffsetPixels 是 100 宽设计坐标。
        [SerializeField] private float worldWidth = 1.2f, minimumWidthPixels = 52, maximumWidthPixels = 200, screenOffsetPixels;
        // 留白随条等比缩放；selectedStackClearancePixels 仅保留旧资源兼容，不再用于选中布局。
        [SerializeField] private float stackClearancePixels, selectedStackClearancePixels;
        [SerializeField] private float arrivalSeconds = .24f;
        [SerializeField] private float statusEmphasisSeconds = .25f;
        private readonly WorldStatusBarLayout layout = new WorldStatusBarLayout();
        private BuildingEnergyStatus status = BuildingEnergyStatus.Connected;
        private long storedEnergy, energyCapacity;
        private bool initialized, alive, selected;
        private float arrivalRemaining, statusEmphasisRemaining;
        public long StoredEnergy => storedEnergy;
        public BuildingEnergyStatus Status => status;
        public bool IsVisible => visibility != null && visibility.alpha > 0;

        public void Configure(CanvasGroup group, Image fill, Image arrival, Image icon, TMP_Text valueText,
            Sprite disconnected, Sprite shortage, Sprite batterySupport)
        {
            HideAmountText();
            visibility = group; energyFill = fill; arrivalHighlight = arrival; statusIcon = icon; amountText = valueText;
            disconnectedSprite = disconnected; shortageSprite = shortage; batterySupportSprite = batterySupport;
            ResetState();
        }
        public void ConfigureLayout(float width = 1.2f, float minPixels = 52, float maxPixels = 200)
        { worldWidth = width; minimumWidthPixels = minPixels; maximumWidthPixels = maxPixels; }
        public void ConfigureScreenOffset(float pixels) { screenOffsetPixels = pixels; }
        public void ConfigureStackClearance(float restingPixels, float selectedPixels)
        { stackClearancePixels = restingPixels; selectedStackClearancePixels = selectedPixels; }
        public void SetSelected(bool value) { if (selected == value) return; selected = value; Render(); }
        public void SetStatus(BuildingEnergyStatus value)
        {
            if (status == value) return;
            status = value;
            statusEmphasisRemaining = value == BuildingEnergyStatus.Connected || value == BuildingEnergyStatus.NotRequired ? 0 : statusEmphasisSeconds;
            Render();
        }
        public void SetEnergy(long energy, long capacity, bool isAlive)
        {
            energy = System.Math.Max(0, energy); capacity = System.Math.Max(0, capacity);
            if (initialized && isAlive && alive && energy > storedEnergy && capacity == energyCapacity)
                arrivalRemaining = arrivalSeconds;
            storedEnergy = energy; energyCapacity = capacity; alive = isAlive; initialized = true;
            if (!alive) arrivalRemaining = statusEmphasisRemaining = 0;
            Render();
        }
        public void AdvanceFeedback(float seconds)
        {
            float delta = Mathf.Max(0, seconds);
            arrivalRemaining = Mathf.Max(0, arrivalRemaining - delta);
            statusEmphasisRemaining = Mathf.Max(0, statusEmphasisRemaining - delta);
            Render(); FaceCamera();
        }
        public void ResetState()
        {
            initialized = alive = selected = false; storedEnergy = energyCapacity = 0;
            arrivalRemaining = statusEmphasisRemaining = 0; status = BuildingEnergyStatus.Connected;
            Render();
        }
        private void Render()
        {
            bool shown = alive && energyCapacity > 0 && status != BuildingEnergyStatus.NotRequired;
            float fraction = energyCapacity <= 0 ? 0 : Mathf.Clamp01((float)storedEnergy / energyCapacity);
            if (visibility != null)
            {
                visibility.alpha = shown ? 1 : 0;
                visibility.interactable = visibility.blocksRaycasts = false;
            }
            if (energyFill != null) energyFill.fillAmount = fraction;
            if (arrivalHighlight != null)
            {
                arrivalHighlight.fillAmount = fraction;
                Color color = arrivalHighlight.color;
                color.a = shown ? Mathf.Clamp01(arrivalRemaining / Mathf.Max(.001f, arrivalSeconds)) * .8f : 0;
                arrivalHighlight.color = color;
            }
            HideAmountText();
            if (statusIcon != null)
            {
                bool disconnected = status == BuildingEnergyStatus.Disconnected;
                bool shortage = status == BuildingEnergyStatus.WaitingForEnergy || status == BuildingEnergyStatus.SourceDepleted;
                bool battery = status == BuildingEnergyStatus.BatterySupport;
                statusIcon.enabled = shown && (disconnected || shortage || battery);
                statusIcon.sprite = disconnected ? disconnectedSprite : battery ? batterySupportSprite : shortageSprite;
                Color statusColor = battery ? new Color(.37f, .9f, 1, 1) : new Color(1, .70f, .22f, 1);
                float emphasis = Mathf.Clamp01(statusEmphasisRemaining / Mathf.Max(.001f, statusEmphasisSeconds));
                // 只在状态切换时强调一次，随后静置；不让整片建筑持续闪烁。
                statusIcon.color = Color.Lerp(statusColor, Color.white, emphasis * .65f);
            }
        }
        private void FaceCamera() => layout.Apply(transform, worldWidth,
            selected ? Mathf.Min(maximumWidthPixels, Mathf.Max(64, minimumWidthPixels)) : minimumWidthPixels,
            maximumWidthPixels, screenOffsetPixels + stackClearancePixels);
        private void HideAmountText() { if (amountText != null && amountText.gameObject.activeSelf) amountText.gameObject.SetActive(false); }
        private void OnEnable() => HideAmountText();
        private void LateUpdate() => FaceCamera();
        private void OnDisable() => ResetState();
    }
}
