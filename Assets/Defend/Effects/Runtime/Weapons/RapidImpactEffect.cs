using UnityEngine;

namespace Defend.PhaseFiveEffects
{
    /// <summary>真实接触点处的白热闪光和沿入射反方向飞散的金属火花。</summary>
    public sealed class RapidImpactEffect : PhaseFiveEffect
    {
        [SerializeField] private Transform contactFlash;
        [SerializeField] private Renderer[] flashRenderers;
        [SerializeField] private LineRenderer pressureRing;
        [SerializeField] private ParticleSystem sparks, fragments, smoke;
        private MaterialPropertyBlock properties;
        private static readonly int Opacity = Shader.PropertyToID("_Opacity");
        private float intensity;

        public void Configure(Transform flash, Renderer[] renderers, LineRenderer ring,
            ParticleSystem sparkParticles, ParticleSystem fragmentParticles, ParticleSystem smokeParticles)
        {
            contactFlash = flash; flashRenderers = renderers; pressureRing = ring;
            sparks = sparkParticles; fragments = fragmentParticles; smoke = smokeParticles;
        }

        protected override void OnBegin()
        {
            // Destination 是已确认接触点；Rotation 是捕获的入射反方向，不追踪受击对象。
            transform.position = Context.Destination;
            intensity = Mathf.Clamp(Context.Intensity, .25f, 1.5f);
            if (contactFlash != null) contactFlash.gameObject.SetActive(true);
            if (sparks != null) sparks.Emit(8);
            if (fragments != null) fragments.Emit(3);
            if (smoke != null) smoke.Emit(2);
        }

        protected override void OnAdvance(float normalizedTime, float deltaTime)
        {
            float flashProgress = Mathf.Clamp01(Age / .055f);
            if (contactFlash != null)
            {
                contactFlash.gameObject.SetActive(flashProgress < 1f);
                contactFlash.localScale = Vector3.one * intensity * Mathf.Lerp(1f, .35f, flashProgress);
                if (properties == null) properties = new MaterialPropertyBlock();
                properties.Clear(); properties.SetFloat(Opacity, (1f - flashProgress) * (1f - flashProgress));
                if (flashRenderers != null)
                    foreach (var renderer in flashRenderers) if (renderer != null) renderer.SetPropertyBlock(properties);
            }
            if (pressureRing != null)
            {
                float progress = Mathf.Clamp01(Age / .13f);
                pressureRing.enabled = progress < 1f;
                pressureRing.transform.localScale = Vector3.one * Mathf.Lerp(.025f, .15f, 1f - (1f - progress) * (1f - progress));
                pressureRing.widthMultiplier = .009f * intensity * (1f - progress);
                var color = new Color(1f, .72f, .3f, (1f - progress) * .7f);
                pressureRing.startColor = pressureRing.endColor = color;
            }
        }

        protected override void OnReset()
        {
            if (contactFlash != null) { contactFlash.gameObject.SetActive(false); contactFlash.localScale = Vector3.one; }
            if (flashRenderers != null)
                foreach (var renderer in flashRenderers) if (renderer != null) renderer.SetPropertyBlock(null);
            if (pressureRing != null)
            {
                pressureRing.enabled = false; pressureRing.transform.localScale = Vector3.one;
                pressureRing.widthMultiplier = .009f; pressureRing.startColor = pressureRing.endColor = Color.white;
            }
        }
    }
}
