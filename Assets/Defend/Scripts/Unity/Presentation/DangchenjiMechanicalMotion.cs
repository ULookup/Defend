using System;
using System.Collections.Generic;
using UnityEngine;

namespace Defend.Unity
{
    /// <summary>荡尘机的附属机械表现；只消费共同显示时钟和已确认的开火，不控制瞄准或伤害。</summary>
    [DisallowMultipleComponent]
    public sealed class DangchenjiMechanicalMotion : MonoBehaviour
    {
        [SerializeField] private Transform coreRotor;
        [SerializeField] private Transform[] recoilNodes = Array.Empty<Transform>();
        [SerializeField] private Transform[] muzzleNodes = Array.Empty<Transform>();
        [SerializeField] private Renderer[] energyRenderers = Array.Empty<Renderer>();
        [SerializeField] private Vector3 rotorLocalAxis = Vector3.forward;
        [SerializeField, Min(0f)] private float recoilTravel = .16f;

        private const float RecoverySeconds = .55f;
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly float[] RecoilTimes = { 0f, .033f, .067f, .1f, .2f, .3f, .4f, RecoverySeconds };
        private static readonly float[] RecoilOffsets = { 0f, -.7f, -1f, -.87f, -.24f, .035f, -.008f, 0f };
        private readonly List<EmissionSlot> emissionSlots = new List<EmissionSlot>();
        private Quaternion restingRotor;
        private Vector3[] restingRecoilPositions;
        private Vector3[] recoilAxesInParent;
        private float[] shotAges;
        private double elapsedSeconds;
        private float dischargeAge = RecoverySeconds;
        private bool cached;
        private bool alive = true;

        private sealed class EmissionSlot
        {
            public Renderer Renderer;
            public int MaterialIndex;
            public Color AuthoredColor;
            public readonly MaterialPropertyBlock Properties = new MaterialPropertyBlock();
        }

        public bool IsConfigured => coreRotor != null && recoilNodes != null && recoilNodes.Length > 0 &&
            recoilNodes[0] != null && muzzleNodes != null && muzzleNodes.Length > 0 && muzzleNodes[0] != null;
        public int ActiveEmitterIndex { get; private set; } = -1;
        public float EnergyFraction { get; private set; }
        public float EmissionIntensity { get; private set; }

        /// <summary>绑定原始骨骼中立姿态；travel 使用 Recoil 父节点单位，轴沿素材骨骼局部 +Y 向外。</summary>
        public void Configure(Transform rotor, Transform[] recoils, Transform[] muzzles,
            Renderer[] emitters, Vector3 rotorAxis, float travel = .16f)
        {
            RestoreMechanicalPose();
            RestoreEmission();
            coreRotor = rotor;
            recoilNodes = recoils ?? Array.Empty<Transform>();
            muzzleNodes = muzzles ?? Array.Empty<Transform>();
            energyRenderers = emitters ?? Array.Empty<Renderer>();
            rotorLocalAxis = rotorAxis.sqrMagnitude > .000001f ? rotorAxis.normalized : Vector3.forward;
            recoilTravel = Mathf.Max(0f, travel);
            cached = false;
            ResetPose();
        }

        public void ResetPose()
        {
            CacheReferences();
            RestoreMechanicalPose();
            elapsedSeconds = 0d;
            dischargeAge = RecoverySeconds;
            ActiveEmitterIndex = -1;
            EnergyFraction = 0f;
            alive = true;
            ApplyEmission(.22f);
        }

        public void SetEnergy(long storedEnergy, long capacity, bool isAlive)
        {
            CacheReferences();
            if (alive != isAlive)
            {
                if (isAlive) ResetPose();
                else
                {
                    RestoreMechanicalPose();
                    dischargeAge = RecoverySeconds;
                    ActiveEmitterIndex = -1;
                }
                alive = isAlive;
            }
            EnergyFraction = alive && capacity > 0 ? Mathf.Clamp01((float)((double)storedEnergy / capacity)) : 0f;
            UpdateEmission();
        }

        /// <summary>仅由已经过身份和生命代次检查的真实发射调用；00 舱与权威弹道炮口保持一致。</summary>
        public void NotifyShot()
        {
            if (!alive || !isActiveAndEnabled || !IsConfigured) return;
            CacheReferences();
            ActiveEmitterIndex = 0;
            shotAges[0] = 0f;
            dischargeAge = 0f;
            ApplyRecoil();
            UpdateEmission();
        }

        /// <summary>使用战斗显示秒数；暂停、倍速和单步均由调用方决定。</summary>
        public void Advance(float seconds)
        {
            if (!alive || !isActiveAndEnabled || seconds <= 0f || float.IsNaN(seconds) || float.IsInfinity(seconds)) return;
            CacheReferences();
            elapsedSeconds += seconds;
            if (coreRotor != null)
                coreRotor.localRotation = restingRotor * Quaternion.AngleAxis((float)(elapsedSeconds % 4d) * 90f, rotorLocalAxis);
            dischargeAge = Mathf.Min(RecoverySeconds, dischargeAge + seconds);
            for (int index = 0; index < shotAges.Length; index++)
                shotAges[index] = Mathf.Min(RecoverySeconds, shotAges[index] + seconds);
            ApplyRecoil();
            UpdateEmission();
        }

        private void CacheReferences()
        {
            if (cached) return;
            cached = true;
            restingRotor = coreRotor != null ? coreRotor.localRotation : Quaternion.identity;
            int count = recoilNodes == null ? 0 : recoilNodes.Length;
            restingRecoilPositions = new Vector3[count];
            recoilAxesInParent = new Vector3[count];
            shotAges = new float[count];
            for (int index = 0; index < count; index++)
            {
                shotAges[index] = RecoverySeconds;
                if (recoilNodes[index] == null) continue;
                restingRecoilPositions[index] = recoilNodes[index].localPosition;
                recoilAxesInParent[index] = recoilNodes[index].localRotation * Vector3.up;
            }
            emissionSlots.Clear();
            var visited = new HashSet<Renderer>();
            foreach (Renderer renderer in energyRenderers)
            {
                if (renderer == null || !visited.Add(renderer)) continue;
                Material[] materials = renderer.sharedMaterials;
                for (int index = 0; index < materials.Length; index++)
                {
                    Material material = materials[index];
                    if (material == null || !material.HasProperty(EmissionColorId)) continue;
                    Color authored = material.GetColor(EmissionColorId);
                    if (authored.maxColorComponent <= 0f) continue;
                    emissionSlots.Add(new EmissionSlot { Renderer = renderer, MaterialIndex = index, AuthoredColor = authored });
                }
            }
        }

        private void ApplyRecoil()
        {
            for (int index = 0; index < shotAges.Length; index++)
            {
                if (recoilNodes[index] == null) continue;
                float offset = 0f;
                float age = shotAges[index];
                for (int sample = 1; sample < RecoilTimes.Length; sample++)
                {
                    if (age > RecoilTimes[sample]) continue;
                    float progress = Mathf.InverseLerp(RecoilTimes[sample - 1], RecoilTimes[sample], age);
                    // 分段平滑插值保持源动作采样范围，避免额外的曲线过冲。
                    offset = Mathf.Lerp(RecoilOffsets[sample - 1], RecoilOffsets[sample], Mathf.SmoothStep(0f, 1f, progress));
                    break;
                }
                recoilNodes[index].localPosition = restingRecoilPositions[index] + recoilAxesInParent[index] * (offset * recoilTravel);
            }
        }

        private void RestoreMechanicalPose()
        {
            if (!cached) return;
            if (coreRotor != null) coreRotor.localRotation = restingRotor;
            for (int index = 0; index < shotAges.Length; index++)
            {
                shotAges[index] = RecoverySeconds;
                if (recoilNodes[index] != null) recoilNodes[index].localPosition = restingRecoilPositions[index];
            }
        }

        private void UpdateEmission()
        {
            float breathing = 1f + .06f * Mathf.Sin((float)(elapsedSeconds % 4d) * Mathf.PI * .5f);
            float flash = .6f * (1f - Mathf.Clamp01(dischargeAge / .2f));
            ApplyEmission(alive ? (.22f + .78f * EnergyFraction) * breathing + flash : 0f);
        }

        private void ApplyEmission(float intensity)
        {
            EmissionIntensity = intensity;
            foreach (EmissionSlot slot in emissionSlots)
            {
                if (slot.Renderer == null) continue;
                slot.Renderer.GetPropertyBlock(slot.Properties, slot.MaterialIndex);
                Color color = slot.AuthoredColor * intensity;
                color.a = slot.AuthoredColor.a;
                slot.Properties.SetColor(EmissionColorId, color);
                slot.Renderer.SetPropertyBlock(slot.Properties, slot.MaterialIndex);
            }
        }

        private void RestoreEmission() => ApplyEmission(1f);
        private void Awake() => CacheReferences();
        private void OnEnable() => ResetPose();
        private void OnDisable()
        {
            RestoreMechanicalPose();
            alive = false;
            EnergyFraction = 0f;
            dischargeAge = RecoverySeconds;
            ActiveEmitterIndex = -1;
            ApplyEmission(0f);
        }
        private void OnDestroy() => RestoreEmission();
    }
}
