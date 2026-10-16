using UnityEngine;

namespace Defend.PhaseFiveEffects
{
    public sealed class WeaponMuzzleEffect : PhaseFiveEffect
    {
        [SerializeField] private Transform flashGeometry;
        [SerializeField] private ParticleSystem sparks;
        [SerializeField] private int sparkCount = 4;
        [SerializeField] private float flashDuration = 0.06f;
        [SerializeField] private float recoilDistance = 0.028f;
        [SerializeField] private bool radialDischarge;
        private WeaponMotionRig motionRig;
        private RapidTurretMotion rapidMotion;
        private uint motionToken;
        private float intensity;

        public void Configure(Transform geometry, ParticleSystem particles, int count,
            float flashSeconds, float recoilMeters, bool radial)
        {
            flashGeometry = geometry;
            sparks = particles;
            sparkCount = count;
            flashDuration = flashSeconds;
            recoilDistance = recoilMeters;
            radialDischarge = radial;
            if (flashGeometry != null) flashGeometry.gameObject.SetActive(false);
        }

        protected override void OnBegin()
        {
            intensity = Mathf.Clamp(Context.Intensity, 0.25f, 1.5f);
            if (Context.Subject != null && Context.Subject.gameObject.activeInHierarchy)
            {
                rapidMotion = Context.Subject.GetComponent<RapidTurretMotion>();
                if (rapidMotion != null && rapidMotion.IsConfigured && !Context.AuthoritativeAim)
                    transform.SetPositionAndRotation(rapidMotion.MuzzlePosition, rapidMotion.MuzzleRotation);
                else
                {
                    rapidMotion = null;
                    motionRig = Context.Subject.GetComponent<WeaponMotionRig>();
                    if (motionRig != null)
                    {
                        motionToken = motionRig.Acquire(Context.Destination, !Context.AuthoritativeAim);
                        motionRig.SetRecoil(motionToken, recoilDistance * intensity);
                    }
                }
            }
            if (radialDischarge) transform.rotation = Quaternion.identity;
            if (flashGeometry != null)
            {
                flashGeometry.gameObject.SetActive(true);
                flashGeometry.localScale = Vector3.one * intensity;
            }
            if (sparks != null) sparks.Emit(sparkCount);
        }

        protected override void OnAdvance(float normalizedTime, float deltaTime)
        {
            if (rapidMotion != null)
                transform.SetPositionAndRotation(rapidMotion.MuzzlePosition, rapidMotion.MuzzleRotation);
            float flashAge = normalizedTime * Lifetime / Mathf.Max(0.001f, flashDuration);
            if (flashGeometry != null)
            {
                bool visible = flashAge < 1f;
                flashGeometry.gameObject.SetActive(visible);
                if (visible)
                {
                    float envelope = radialDischarge
                        ? Mathf.Lerp(0.65f, 1.4f, flashAge)
                        : Mathf.Lerp(1f, 0.12f, flashAge * flashAge);
                    flashGeometry.localScale = Vector3.one * envelope * intensity;
                }
            }
            if (motionRig != null)
            {
                float remaining = 1f - Mathf.SmoothStep(0f, 1f, normalizedTime);
                motionRig.SetRecoil(motionToken, recoilDistance * intensity * remaining);
            }
        }

        protected override void OnReset()
        {
            if (motionRig != null) motionRig.Release(motionToken);
            motionRig = null;
            rapidMotion = null;
            motionToken = 0;
            if (flashGeometry != null)
            {
                flashGeometry.gameObject.SetActive(false);
                flashGeometry.localScale = Vector3.one;
            }
        }
    }
}
