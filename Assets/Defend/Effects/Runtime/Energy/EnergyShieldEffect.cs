using UnityEngine;

namespace Defend.PhaseFiveEffects
{
    /// <summary>Local shield feedback with no material mutation or durability side effects.</summary>
    public sealed class EnergyShieldEffect : PhaseFiveEffect
    {
        [SerializeField] private bool breaking;
        [SerializeField] private Transform geometry;
        [SerializeField] private LineRenderer[] strokes;
        [SerializeField] private float[] widths;
        [SerializeField] private Vector3[] scatterDirections;
        [SerializeField] private ParticleSystem splinters;
        [SerializeField] private int particleCount;
        private float authoredRadius, displayScale = 1f;
        private bool particleScaleCached;
        private ParticleSystemScalingMode authoredParticleScaling;

        public void Configure(bool isBreaking, Transform visual, LineRenderer[] lines,
            Vector3[] directions, ParticleSystem particles, int count)
        {
            breaking = isBreaking;
            geometry = visual;
            strokes = lines;
            scatterDirections = directions;
            splinters = particles;
            particleCount = count;
            widths = new float[lines.Length];
            for (int index = 0; index < lines.Length; index++) widths[index] = lines[index].widthMultiplier;
            OnReset();
        }

        protected override void OnBegin()
        {
            if (authoredRadius <= 0)
            {
                foreach (var stroke in strokes)
                    for (int index = 0; index < stroke.positionCount; index++)
                    {
                        Vector3 point = stroke.GetPosition(index);
                        if (!stroke.useWorldSpace) point = stroke.transform.TransformPoint(point);
                        point = transform.InverseTransformPoint(point);
                        authoredRadius = Mathf.Max(authoredRadius, new Vector2(point.x, point.z).magnitude);
                    }
            }
            displayScale = breaking && Context.Radius > 0 && !float.IsNaN(Context.Radius) &&
                !float.IsInfinity(Context.Radius) && authoredRadius > .0001f ? Context.Radius / authoredRadius : 1f;
            transform.localScale = Vector3.one * displayScale;
            if (splinters != null)
            {
                var main = splinters.main;
                if (!particleScaleCached) { authoredParticleScaling = main.scalingMode; particleScaleCached = true; }
                main.scalingMode = displayScale == 1f ? authoredParticleScaling : ParticleSystemScalingMode.Hierarchy;
            }
            geometry.gameObject.SetActive(true);
            for (int index = 0; index < strokes.Length; index++) strokes[index].enabled = true;
            if (splinters != null && particleCount > 0) splinters.Emit(particleCount);
        }

        protected override void OnAdvance(float normalizedTime, float deltaTime)
        {
            float remaining = 1f - normalizedTime;
            geometry.localScale = Vector3.one * (breaking ? 1f : Mathf.Lerp(.70f, 1.18f, normalizedTime));
            for (int index = 0; index < strokes.Length; index++)
            {
                strokes[index].startColor = strokes[index].endColor = new Color(1, 1, 1, remaining * remaining);
                strokes[index].widthMultiplier = widths[index] * displayScale * (.4f + .6f * remaining);
                if (breaking && scatterDirections != null && index < scatterDirections.Length)
                    strokes[index].transform.localPosition = scatterDirections[index] * (.18f * normalizedTime);
            }
        }

        protected override void OnReset()
        {
            displayScale = 1f;
            transform.localScale = Vector3.one;
            if (splinters != null && particleScaleCached)
            {
                var main = splinters.main;
                main.scalingMode = authoredParticleScaling;
            }
            if (geometry != null)
            {
                geometry.localScale = Vector3.one;
                geometry.gameObject.SetActive(false);
            }
            if (strokes == null) return;
            for (int index = 0; index < strokes.Length; index++)
            {
                if (strokes[index] == null) continue;
                strokes[index].enabled = false;
                strokes[index].transform.localPosition = Vector3.zero;
                strokes[index].startColor = strokes[index].endColor = Color.white;
                if (widths != null && index < widths.Length) strokes[index].widthMultiplier = widths[index];
            }
        }
    }
}
