using UnityEngine;

namespace Defend.PhaseFiveEffects
{
    /// <summary>同一枚真实弹体的橙金外辉与白热核心，共用相同的权威端点。</summary>
    public sealed class RapidProjectileVisual : MonoBehaviour
    {
        [SerializeField] private LineRenderer glow, core;
        public int RendererCount => (glow != null ? 1 : 0) + (core != null ? 1 : 0);
        public void Configure(LineRenderer outerGlow, LineRenderer brightCore) { glow = outerGlow; core = brightCore; }
        public void SetVisible(bool visible)
        {
            if (glow != null) glow.enabled = visible;
            if (core != null) core.enabled = visible;
        }
        public void SetSegment(Vector3 tail, Vector3 head)
        {
            SetLine(glow, tail, head); SetLine(core, tail, head);
        }
        private static void SetLine(LineRenderer line, Vector3 tail, Vector3 head)
        {
            if (line == null) return;
            line.positionCount = 2; line.SetPosition(0, tail); line.SetPosition(1, head); line.enabled = true;
        }
        private void OnDisable()
        {
            SetVisible(false);
            if (glow != null) { glow.SetPosition(0, Vector3.zero); glow.SetPosition(1, Vector3.zero); }
            if (core != null) { core.SetPosition(0, Vector3.zero); core.SetPosition(1, Vector3.zero); }
        }
    }
}
