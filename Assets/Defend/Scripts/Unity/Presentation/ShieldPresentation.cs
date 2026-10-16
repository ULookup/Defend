using UnityEngine;
using UnityEngine.UI;

namespace Defend.Unity
{
    /// <summary>持续护罩直接读取真实核心储能，瞬时破裂效果不能改变此状态。</summary>
    public sealed class ShieldPresentation : MonoBehaviour
    {
        [SerializeField] private GameObject shieldVisual;
        [SerializeField] private HexShieldVisual hexShield;
        [SerializeField] private CanvasGroup bar;
        [SerializeField] private Image fill;
        private Camera worldCamera;
        private bool visible;
        public HexShieldVisual Visual => hexShield;
        public void Configure(GameObject visual, CanvasGroup group, Image shieldFill)
        {
            shieldVisual = visual; bar = group; fill = shieldFill;
            hexShield = visual == null ? null : visual.GetComponentInChildren<HexShieldVisual>(true);
        }
        public void Apply(long storedEnergy, long capacity, bool alive)
        {
            visible = alive && storedEnergy > 0 && capacity > 0;
            float fraction = capacity <= 0 ? 0 : Mathf.Clamp01((float)storedEnergy / capacity);
            if (hexShield != null)
            {
                if (!visible) hexShield.ResetState();
                hexShield.SetEnergy(visible ? fraction : 0);
            }
            if (shieldVisual != null) shieldVisual.SetActive(visible);
            if (bar != null) { bar.alpha = visible ? 1 : 0; bar.blocksRaycasts = false; bar.interactable = false; }
            if (fill != null) fill.fillAmount = fraction;
        }
        public void NotifyHit(Vector3 worldDirection)
        {
            if (visible && hexShield != null) hexShield.NotifyHit(worldDirection);
        }
        /// <summary>使用统一显示时间推进，暂停冻结，倍速与单步均随模拟时间。</summary>
        public void AdvancePresentation(float seconds)
        {
            if (visible && hexShield != null) hexShield.Advance(Mathf.Max(0, seconds));
        }
        public void AdvanceFeedback(float seconds)
        {
            if (worldCamera == null) worldCamera = Camera.main;
            if (worldCamera != null && bar != null) bar.transform.rotation = worldCamera.transform.rotation;
        }
        public void ResetState() { Apply(0, 1, false); }
        private void OnDisable() { ResetState(); }
    }
}
