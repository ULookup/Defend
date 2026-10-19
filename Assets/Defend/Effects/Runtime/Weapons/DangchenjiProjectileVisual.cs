using UnityEngine;

namespace Defend.PhaseFiveEffects
{
    /// <summary>只接收 ProjectilePresentation 的位置与显示时钟；无独立弹道、计时器或伤害回调。</summary>
    public sealed class DangchenjiProjectileVisual : MonoBehaviour
    {
        [SerializeField] private ParticleSystem[] particles;
        [SerializeField] private MeshRenderer[] shell;
        [SerializeField] private LineRenderer trail;
        [SerializeField] private float trailSeconds = .12f;
        private readonly Vector3[] positions = new Vector3[48];
        private readonly double[] times = new double[48];
        private int count;
        private double previousSeconds;
        private bool started;

        public void Configure(ParticleSystem[] layers, MeshRenderer[] meshes, LineRenderer shortTrail)
        { particles = layers; shell = meshes; trail = shortTrail; }

        public void SetVisible(bool visible)
        {
            if (shell != null) foreach (var mesh in shell) if (mesh != null) mesh.enabled = visible;
            if (trail != null) trail.enabled = visible && count > 1;
            if (!visible && particles != null)
                foreach (var particle in particles) if (particle != null) particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        public void SetPose(Vector3 head, Vector3 direction, double seconds)
        {
            Quaternion rotation = direction.sqrMagnitude > .000001f
                ? Quaternion.LookRotation(direction, Mathf.Abs(Vector3.Dot(direction.normalized, Vector3.up)) > .999f ? Vector3.forward : Vector3.up)
                : transform.rotation;
            transform.SetPositionAndRotation(head, rotation);
            if (!started)
            {
                started = true; previousSeconds = seconds;
                foreach (var particle in particles) particle.Simulate(.001f, false, true, false);
            }
            float elapsed = Mathf.Max(0f, (float)(seconds - previousSeconds));
            previousSeconds = seconds;
            if (elapsed > 0f) foreach (var particle in particles) particle.Simulate(elapsed, false, false, false);

            // Keep the supplied short cyan trail on the same clock as the shell, including pause and single-step.
            int expired = 0;
            while (expired < count - 1 && times[expired + 1] < seconds - trailSeconds) expired++;
            if (expired > 0)
            {
                for (int i = expired; i < count; i++) { positions[i - expired] = positions[i]; times[i - expired] = times[i]; }
                count -= expired;
            }
            if (count == 0 || seconds > times[count - 1])
            {
                if (count == positions.Length)
                {
                    for (int i = 1; i < count; i++) { positions[i - 1] = positions[i]; times[i - 1] = times[i]; }
                    count--;
                }
                positions[count] = head; times[count] = seconds; count++;
            }
            else positions[count - 1] = head;
            if (count > 1 && times[0] < seconds - trailSeconds && times[1] > times[0])
            {
                double cutoff = seconds - trailSeconds;
                positions[0] = Vector3.Lerp(positions[0], positions[1], (float)((cutoff - times[0]) / (times[1] - times[0])));
                times[0] = cutoff;
            }
            SetVisible(true);
            trail.positionCount = count;
            for (int i = 0; i < count; i++) trail.SetPosition(i, positions[count - 1 - i]);
        }

        private void OnDisable()
        {
            SetVisible(false);
            if (trail != null) trail.positionCount = 0;
            count = 0; started = false; previousSeconds = 0;
        }
    }
}
