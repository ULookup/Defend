using System;
using UnityEngine;

namespace Defend.Unity
{
    /// <summary>复用同一束体积激光，仅显示模拟系统已经结算的射击线段。</summary>
    public sealed class RailgunLaserPresentation : MonoBehaviour
    {
        [SerializeField] private Transform beamRoot;
        [SerializeField] private MeshRenderer coreRenderer;
        [SerializeField] private MeshRenderer haloRenderer;
        [SerializeField, Min(0.02f)] private float duration = 0.24f;
        private static readonly int ProgressProperty = Shader.PropertyToID("_Progress");
        private static readonly int EffectSecondsProperty = Shader.PropertyToID("_EffectSeconds");
        private static readonly int DistanceProperty = Shader.PropertyToID("_Distance");
        private MaterialPropertyBlock properties;
        private float elapsed;
        private float length;
        private bool active;

        public bool IsPlaying => active;
        public float Elapsed => elapsed;
        public float Duration => duration;
        public Vector3 StartPosition { get; private set; }
        public Vector3 EndPosition { get; private set; }

        public void Configure(Transform root, MeshRenderer core, MeshRenderer halo, float seconds = 0.24f)
        {
            beamRoot = root;
            coreRenderer = core;
            haloRenderer = halo;
            duration = Mathf.Max(0.02f, seconds);
            Initialize();
        }

        public void Initialize()
        {
            if (beamRoot == null || coreRenderer == null || haloRenderer == null)
                throw new InvalidOperationException("贯穿轨道炮缺少体积激光预制体引用。");
            if (properties == null) properties = new MaterialPropertyBlock();
            ResetSession();
        }

        public void Play(Vector3 start, Vector3 end)
        {
            if (properties == null) Initialize();
            ResetSession();
            if (!Finite(start) || !Finite(end)) return;
            length = Vector3.Distance(start, end);
            if (length < 0.001f) return;
            StartPosition = start;
            EndPosition = end;
            beamRoot.SetPositionAndRotation(start, Quaternion.LookRotation((end - start) / length));
            // World-space effect containers must have unit scale; never parent this to the recoiling barrel.
            beamRoot.localScale = new Vector3(1f, 1f, length);
            elapsed = 0f;
            active = true;
            coreRenderer.enabled = true;
            haloRenderer.enabled = true;
            ApplyEnvelope();
        }

        /// <summary>由会话显示时钟传入本次增量；暂停传 0，倍速与单步沿用同一时钟。</summary>
        public void Advance(float simulationElapsed)
        {
            if (!active || simulationElapsed <= 0f || float.IsNaN(simulationElapsed) || float.IsInfinity(simulationElapsed)) return;
            elapsed += simulationElapsed;
            if (elapsed >= duration) { ResetSession(); return; }
            ApplyEnvelope();
        }

        private void ApplyEnvelope()
        {
            float progress = Mathf.Clamp01(elapsed / duration);
            float envelope = progress < 0.12f ? 1f : Mathf.Pow(1f - (progress - 0.12f) / 0.88f, 1.35f);
            properties.SetFloat(ProgressProperty, envelope);
            properties.SetFloat(EffectSecondsProperty, elapsed);
            properties.SetFloat(DistanceProperty, length);
            coreRenderer.SetPropertyBlock(properties);
            haloRenderer.SetPropertyBlock(properties);
        }

        public void ResetSession()
        {
            active = false;
            elapsed = 0f;
            length = 0f;
            StartPosition = Vector3.zero;
            EndPosition = Vector3.zero;
            if (coreRenderer != null) { coreRenderer.enabled = false; coreRenderer.SetPropertyBlock(null); }
            if (haloRenderer != null) { haloRenderer.enabled = false; haloRenderer.SetPropertyBlock(null); }
        }

        private static bool Finite(Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
            !float.IsNaN(value.z) && !float.IsInfinity(value.z);

        private void OnDisable() { ResetSession(); }
    }
}
