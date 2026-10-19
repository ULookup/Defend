using System;
using UnityEngine;

namespace Defend.Unity
{
    /// <summary>观察窗液位只读取已提交的真实储能，不自行计时、产能或推算在途能量。</summary>
    [DisallowMultipleComponent]
    public sealed class BatteryChargePresentation : MonoBehaviour
    {
        private static readonly int EnergyFractionId = Shader.PropertyToID("_EnergyFraction");
        [SerializeField] private Renderer[] energyDisplayRenderers = Array.Empty<Renderer>();
        private MaterialPropertyBlock properties;
        private float appliedFraction = -1f;

        public float EnergyFraction => Mathf.Max(0f, appliedFraction);

        /// <summary>同时绑定详细和远景观察窗；每个观察窗使用独立网格和专用材质。</summary>
        public void Configure(Renderer[] displays)
        {
            if (displays == null) throw new ArgumentNullException(nameof(displays));
            ResetState();
            energyDisplayRenderers = displays;
            ResetState();
        }

        public void SetEnergy(long storedEnergy, long capacity, bool alive)
        {
            float fraction = alive && capacity > 0
                ? Mathf.Clamp01((float)((double)storedEnergy / capacity)) : 0f;
            ApplyFraction(fraction);
        }

        public void ResetState()
        {
            appliedFraction = -1f;
            ApplyFraction(0f);
        }

        private void ApplyFraction(float fraction)
        {
            if (appliedFraction == fraction) return;
            appliedFraction = fraction;
            if (properties == null) properties = new MaterialPropertyBlock();
            foreach (Renderer display in energyDisplayRenderers)
            {
                if (display == null) continue;
                // 保留同一 Renderer 上的其他表现参数，且不修改共享材质。
                properties.Clear();
                display.GetPropertyBlock(properties);
                properties.SetFloat(EnergyFractionId, fraction);
                display.SetPropertyBlock(properties);
            }
        }

        private void OnEnable() => ResetState();
        private void OnDisable() => ResetState();
    }
}
