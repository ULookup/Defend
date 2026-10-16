using UnityEngine;

namespace Defend.PhaseFiveEffects
{
    /// <summary>A compact energy crystal following confirmed, immutable transport endpoints.</summary>
    public sealed class EnergyTransferEffect : PhaseFiveEffect
    {
        [SerializeField] private Transform pulseHead;
        [SerializeField] private LineRenderer shortTrail;
        [SerializeField] private Vector3 headScale = new Vector3(.035f, .025f, .075f);
        [SerializeField] private float trailLength = .18f;
        [SerializeField] private float trailWidth = .018f;
        [SerializeField] private LineRenderer glowTrail;
        [SerializeField] private Renderer[] crystalRenderers;
        [SerializeField] private float glowWidth = .052f;
        [SerializeField] private float rotationDegreesPerSecond = 75f;
        private static readonly int PulseId = Shader.PropertyToID("_Pulse");
        private MaterialPropertyBlock crystalProperties;
        private Vector3 origin;
        private Vector3 destination;
        private Vector3 direction;
        private float distance;
        private bool confirmed;
        private Quaternion flightRotation;

        public void Configure(Transform head, LineRenderer trail)
        {
            pulseHead = head;
            shortTrail = trail;
            headScale = head.localScale;
            OnReset();
        }

        public void ConfigurePolish(Transform head, LineRenderer coreTrail, LineRenderer outerTrail,
            Renderer[] renderers, float length = .44f)
        {
            pulseHead = head;
            shortTrail = coreTrail;
            glowTrail = outerTrail;
            crystalRenderers = renderers;
            headScale = head.localScale;
            trailLength = length;
            trailWidth = .018f;
            glowWidth = .058f;
            OnReset();
        }

        public override bool CanPlay(EffectRequest request)
        {
            if (pulseHead == null || shortTrail == null || !request.ConfirmedTransfer ||
                !Finite(request.Origin) || !Finite(request.Destination)) return false;
            float squaredDistance = (request.Destination - request.Origin).sqrMagnitude;
            return !float.IsInfinity(squaredDistance) && !float.IsNaN(squaredDistance) && squaredDistance > .000001f;
        }

        private static bool Finite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x)
                && !float.IsNaN(value.y) && !float.IsInfinity(value.y)
                && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        protected override void OnBegin()
        {
            // Also suppress direct Begin calls that bypass the pool's CanPlay gate.
            confirmed = CanPlay(Context);
            if (pulseHead != null) pulseHead.gameObject.SetActive(confirmed);
            if (shortTrail != null) shortTrail.enabled = confirmed;
            if (glowTrail != null) glowTrail.enabled = confirmed;
            if (!confirmed) return;
            origin = Context.Origin;
            destination = Context.Destination;
            distance = Vector3.Distance(origin, destination);
            direction = (destination - origin) / distance;
            flightRotation = Quaternion.LookRotation(direction,
                Mathf.Abs(direction.y) > .99f ? Vector3.forward : Vector3.up);
            pulseHead.rotation = flightRotation;
            pulseHead.localScale = headScale;
            shortTrail.useWorldSpace = true;
            shortTrail.positionCount = 2;
            if (glowTrail != null)
            {
                glowTrail.useWorldSpace = true;
                glowTrail.positionCount = 2;
            }
        }

        protected override void OnAdvance(float normalizedTime, float deltaTime)
        {
            if (!confirmed) return;
            Vector3 position = Vector3.Lerp(origin, destination, normalizedTime);
            pulseHead.position = position;
            // The pool's age is the only animation clock: pause, single-step and speed remain coherent.
            float phase = (Context.Seed & 31) * 11.25f;
            pulseHead.rotation = flightRotation * Quaternion.AngleAxis(phase + Age * rotationDegreesPerSecond, Vector3.forward);
            float fade = Mathf.Clamp01((Lifetime - Age) / .075f);
            float reveal = .6f + .4f * Mathf.Clamp01(Age / .04f);
            float pulse = (.94f + .06f * Mathf.Sin(Age * 8f + phase * Mathf.Deg2Rad)) * reveal * fade;
            float length = Mathf.Min(trailLength, distance * normalizedTime);
            SetTrail(shortTrail, position, length * .78f, trailWidth, pulse);
            SetTrail(glowTrail, position, length, glowWidth, pulse * .65f);
            pulseHead.localScale = headScale * (.82f + .18f * reveal);
            SetCrystalPulse(pulse);
        }

        private void SetTrail(LineRenderer line, Vector3 position, float length, float width, float opacity)
        {
            if (line == null) return;
            line.SetPosition(0, position - direction * length);
            line.SetPosition(1, position);
            line.startColor = new Color(1, 1, 1, 0);
            line.endColor = new Color(1, 1, 1, opacity);
            line.widthMultiplier = width;
        }

        private void SetCrystalPulse(float pulse)
        {
            if (crystalRenderers == null || crystalRenderers.Length == 0) return;
            if (crystalProperties == null) crystalProperties = new MaterialPropertyBlock();
            crystalProperties.SetFloat(PulseId, pulse);
            foreach (Renderer renderer in crystalRenderers)
                if (renderer != null) renderer.SetPropertyBlock(crystalProperties);
        }

        protected override void OnReset()
        {
            confirmed = false;
            origin = destination = direction = Vector3.zero;
            distance = 0;
            flightRotation = Quaternion.identity;
            if (pulseHead != null)
            {
                pulseHead.gameObject.SetActive(false);
                pulseHead.localPosition = Vector3.zero;
                pulseHead.localRotation = Quaternion.identity;
                pulseHead.localScale = headScale;
            }
            ResetTrail(shortTrail, trailWidth);
            ResetTrail(glowTrail, glowWidth);
            SetCrystalPulse(0);
        }

        private static void ResetTrail(LineRenderer line, float width)
        {
            if (line == null) return;
            line.enabled = false;
            line.positionCount = 2;
            line.SetPosition(0, Vector3.zero);
            line.SetPosition(1, Vector3.zero);
            line.widthMultiplier = width;
            line.startColor = line.endColor = Color.white;
        }
    }
}
