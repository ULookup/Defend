using UnityEngine;

namespace Defend.PhaseFiveEffects
{
    /// <summary>Send and arrival are separate visual requests, never inferred from transport time.</summary>
    public sealed class EnergyEndpointEffect : PhaseFiveEffect
    {
        [SerializeField] private bool arrival;
        [SerializeField] private Transform geometry;
        [SerializeField] private LineRenderer[] strokes;
        [SerializeField] private float[] widths;
        [SerializeField] private Renderer glow;
        private static readonly int PulseId = Shader.PropertyToID("_Pulse");
        private MaterialPropertyBlock glowProperties;

        public void Configure(bool isArrival, Transform visual, LineRenderer[] lines)
        {
            arrival = isArrival;
            geometry = visual;
            strokes = lines;
            widths = new float[lines.Length];
            for (int index = 0; index < lines.Length; index++) widths[index] = lines[index].widthMultiplier;
            OnReset();
        }

        public void ConfigurePolish(bool isArrival, Transform visual, LineRenderer[] lines, Renderer softGlow)
        {
            glow = softGlow;
            Configure(isArrival, visual, lines);
        }

        protected override void OnBegin()
        {
            geometry.gameObject.SetActive(true);
            for (int index = 0; index < strokes.Length; index++) strokes[index].enabled = true;
        }

        protected override void OnAdvance(float normalizedTime, float deltaTime)
        {
            float eased = 1f - Mathf.Pow(1f - normalizedTime, 2f);
            geometry.localScale = Vector3.one * (arrival
                ? Mathf.Lerp(1.12f, .24f, eased)
                : Mathf.Lerp(.38f, 1.3f, eased));
            geometry.localRotation = Quaternion.Euler(0, (arrival ? -32f : 25f) * eased, 0);
            float remaining = 1f - normalizedTime;
            Color tint = new Color(1, 1, 1, remaining * remaining);
            for (int index = 0; index < strokes.Length; index++)
            {
                strokes[index].startColor = strokes[index].endColor = tint;
                strokes[index].widthMultiplier = widths[index] * (.5f + .5f * remaining);
            }
            SetGlowPulse(remaining * remaining * .85f);
        }

        private void SetGlowPulse(float pulse)
        {
            if (glow == null) return;
            if (glowProperties == null) glowProperties = new MaterialPropertyBlock();
            glowProperties.SetFloat(PulseId, pulse);
            glow.SetPropertyBlock(glowProperties);
        }

        protected override void OnReset()
        {
            if (geometry != null)
            {
                geometry.localScale = Vector3.one;
                geometry.localRotation = Quaternion.identity;
                geometry.gameObject.SetActive(false);
            }
            SetGlowPulse(0);
            if (strokes == null) return;
            for (int index = 0; index < strokes.Length; index++)
            {
                if (strokes[index] == null) continue;
                strokes[index].enabled = false;
                strokes[index].startColor = strokes[index].endColor = Color.white;
                if (widths != null && index < widths.Length) strokes[index].widthMultiplier = widths[index];
            }
        }
    }
}
