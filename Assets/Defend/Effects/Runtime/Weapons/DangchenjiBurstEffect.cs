using UnityEngine;

namespace Defend.PhaseFiveEffects
{
    /// <summary>供应美术的烘焙粒子由现有特效池推进，发射和爆炸只响应权威事件。</summary>
    public sealed class DangchenjiBurstEffect : PhaseFiveEffect
    {
        [SerializeField] private ParticleSystem[] layers;
        [SerializeField] private Light flash;
        [SerializeField] private bool impact;
        [SerializeField] private float artScale = 1f;
        [SerializeField] private float authoredImpactRadius = 1.45f;
        public void Configure(ParticleSystem[] particles, Light light, bool isImpact, float scale)
        { layers = particles; flash = light; impact = isImpact; artScale = scale; }

        protected override void OnBegin()
        {
            float scale = impact && Context.Radius > 0f ? Context.Radius / authoredImpactRadius : artScale;
            transform.localScale = Vector3.one * scale;
            if (impact) transform.rotation = Quaternion.identity;
            // Simulate keeps these systems paused between explicit simulation-clock advances.
            foreach (var particle in layers)
                if (particle != null) particle.Simulate(0f, false, true, false);
            if (flash != null)
            {
                flash.range = (impact ? 2.5f : 1.8f) * scale;
                flash.enabled = true;
            }
        }

        protected override void OnAdvance(float normalizedTime, float deltaTime)
        {
            if (flash == null) return;
            flash.intensity = (impact ? 3.4f : 2.8f) * Mathf.Clamp01(1f - Age / (impact ? .09f : .085f));
            flash.enabled = IsRunning && flash.intensity > 0f;
        }

        protected override void OnReset()
        {
            if (flash != null) { flash.enabled = false; flash.intensity = 0f; }
        }
    }
}
