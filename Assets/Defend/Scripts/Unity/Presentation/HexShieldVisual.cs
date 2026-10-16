using UnityEngine;

namespace Defend.Unity
{
    /// <summary>保存的蜂窝板片仅负责表现；时间由战斗的共同显示时钟推进。</summary>
    [DisallowMultipleComponent]
    public sealed class HexShieldVisual : MonoBehaviour
    {
        public const float HitHoldSeconds = 3f;
        [SerializeField] private MeshRenderer shellRenderer;
        [SerializeField] private MeshRenderer particleRenderer;
        [SerializeField, Min(0f)] private float idleRotationSpeed = 12f;
        [SerializeField, Min(.01f)] private float assemblySeconds = .12f;
        [SerializeField, Min(.01f)] private float releaseSeconds = .55f;

        private static readonly int EnergyId = Shader.PropertyToID("_Energy");
        private static readonly int ImpactId = Shader.PropertyToID("_ImpactBlend");
        private static readonly int ClockId = Shader.PropertyToID("_PresentationClock");
        private static readonly int HitAgeId = Shader.PropertyToID("_HitAge");
        private static readonly int HitDirectionId = Shader.PropertyToID("_HitDirection");
        private MaterialPropertyBlock properties;
        private float clock;
        private float hitAge = 20f;
        private Vector3 hitDirection;

        public float HitSecondsRemaining { get; private set; }
        public float ImpactBlend { get; private set; }
        public float EnergyFraction { get; private set; }
        public float PresentationSeconds => clock;
        public float IdleRotationDegrees { get; private set; }
        public MeshRenderer ParticleRenderer => particleRenderer;
        public bool IsConfigured => shellRenderer != null && shellRenderer.sharedMaterial != null;

        public void Configure(MeshRenderer renderer, MeshRenderer particles = null)
        {
            shellRenderer = renderer;
            particleRenderer = particles;
            ApplyProperties();
        }

        public void SetEnergy(float fraction)
        {
            float next = float.IsNaN(fraction) ? 0f : Mathf.Clamp01(fraction);
            if (next <= 0f)
            {
                ResetState();
                return;
            }
            EnergyFraction = next;
            ApplyProperties();
        }

        /// <param name="worldDirection">从护盾中心指向攻击来源的世界方向。</param>
        public void NotifyHit(Vector3 worldDirection)
        {
            if (EnergyFraction <= 0f) return;
            hitDirection = worldDirection.sqrMagnitude > .0001f
                ? (shellRenderer != null ? shellRenderer.transform : transform).InverseTransformDirection(worldDirection.normalized)
                : Vector3.zero;
            HitSecondsRemaining = HitHoldSeconds;
            hitAge = 0f;
            // 接触当帧就能看见收拢，剩余部分在短促的吸附动作中完成。
            ImpactBlend = Mathf.Max(ImpactBlend, .82f);
            ApplyProperties();
        }

        public void Advance(float seconds)
        {
            if (EnergyFraction <= 0f || seconds <= 0f || float.IsNaN(seconds) || float.IsInfinity(seconds)) return;
            // 以360秒为周期，Shader所有漂浮频率均为该周期的整数倍。
            clock = (clock + seconds) % 360f;
            hitAge = Mathf.Min(20f, hitAge + seconds);
            float held = Mathf.Min(seconds, HitSecondsRemaining);
            HitSecondsRemaining = Mathf.Max(0f, HitSecondsRemaining - seconds);
            if (held > 0f) AdvanceMotion(held, 1f, assemblySeconds);
            float recovering = seconds - held;
            if (recovering > 0f) AdvanceMotion(recovering, 0f, releaseSeconds);
            ApplyProperties();
        }

        private void AdvanceMotion(float seconds, float target, float transitionSeconds)
        {
            float before = ImpactBlend;
            float changing = Mathf.Min(seconds, Mathf.Abs(target - before) * Mathf.Max(.01f, transitionSeconds));
            ImpactBlend = Mathf.MoveTowards(before, target, seconds / Mathf.Max(.01f, transitionSeconds));
            // 分别积分过渡与稳定阶段，倍速或单步跨过保持边界时也不会多转或漏转。
            float idleSeconds = changing * (1f - (before + ImpactBlend) * .5f) + (seconds - changing) * (1f - ImpactBlend);
            IdleRotationDegrees = Mathf.Repeat(IdleRotationDegrees + idleRotationSpeed * idleSeconds, 360f);
        }

        public void ResetState()
        {
            EnergyFraction = 0f;
            HitSecondsRemaining = ImpactBlend = clock = IdleRotationDegrees = 0f;
            hitAge = 20f;
            hitDirection = Vector3.zero;
            ApplyProperties();
        }

        private void OnDisable() => ResetState();

        private void ApplyProperties()
        {
            if (properties == null) properties = new MaterialPropertyBlock();
            properties.SetFloat(EnergyId, EnergyFraction);
            properties.SetFloat(ImpactId, ImpactBlend);
            properties.SetFloat(ClockId, clock);
            properties.SetFloat(HitAgeId, hitAge);
            properties.SetVector(HitDirectionId, hitDirection);
            if (shellRenderer != null)
            {
                shellRenderer.transform.localRotation = Quaternion.Euler(0, IdleRotationDegrees, 0);
                shellRenderer.SetPropertyBlock(properties);
            }
            if (particleRenderer != null) particleRenderer.SetPropertyBlock(properties);
        }
    }
}
