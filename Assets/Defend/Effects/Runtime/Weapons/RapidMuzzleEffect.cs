using UnityEngine;

namespace Defend.PhaseFiveEffects
{
    /// <summary>短促燃烧闪光和少量燃气，完全由特效池的表现时钟推进。</summary>
    public sealed class RapidMuzzleEffect : PhaseFiveEffect
    {
        [SerializeField] private Transform flame;
        [SerializeField] private Renderer[] flameRenderers;
        [SerializeField] private ParticleSystem sparks, gas;
        [SerializeField] private float flashSeconds = .065f;
        private MaterialPropertyBlock properties;
        private static readonly int Opacity = Shader.PropertyToID("_Opacity");
        private static readonly int Phase = Shader.PropertyToID("_Phase");
        private float intensity, variation;

        public void Configure(Transform flameGeometry, Renderer[] renderers, ParticleSystem sparkParticles,
            ParticleSystem gasParticles)
        {
            flame = flameGeometry; flameRenderers = renderers; sparks = sparkParticles; gas = gasParticles;
        }

        protected override void OnBegin()
        {
            intensity = Mathf.Clamp(Context.Intensity, .25f, 1.5f);
            variation = .9f + Mathf.Abs(Context.Seed % 13) / 60f;
            if (flame != null) flame.gameObject.SetActive(true);
            if (sparks != null) sparks.Emit(5);
            if (gas != null) gas.Emit(2);
        }

        protected override void OnAdvance(float normalizedTime, float deltaTime)
        {
            float progress = Mathf.Clamp01(Age / Mathf.Max(.001f, flashSeconds));
            if (flame == null) return;
            flame.gameObject.SetActive(progress < 1f);
            if (progress >= 1f) return;
            float envelope = 1f - progress;
            flame.localScale = new Vector3(.72f + .28f * envelope, .72f + .28f * envelope,
                .35f + .65f * envelope) * intensity * variation;
            if (properties == null) properties = new MaterialPropertyBlock();
            properties.Clear();
            properties.SetFloat(Opacity, envelope * envelope);
            properties.SetFloat(Phase, Context.Seed * .37f + progress * 2.3f);
            if (flameRenderers != null)
                foreach (var renderer in flameRenderers) if (renderer != null) renderer.SetPropertyBlock(properties);
        }

        protected override void OnReset()
        {
            if (flame != null) { flame.gameObject.SetActive(false); flame.localScale = Vector3.one; }
            if (flameRenderers != null)
                foreach (var renderer in flameRenderers) if (renderer != null) renderer.SetPropertyBlock(null);
        }
    }
}
