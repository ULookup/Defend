using System;
using UnityEngine;

namespace Defend.Unity
{
    /// <summary>岛下云层仅消费会话显示时间；网格位置、碰撞和模拟世界保持不变。</summary>
    [DisallowMultipleComponent]
    public sealed class EnvironmentCloudMotion : MonoBehaviour
    {
        [SerializeField] private Renderer[] cloudRenderers = Array.Empty<Renderer>();
        [SerializeField] private Vector2 windDirection = new Vector2(1f, .28f);
        [SerializeField, Min(0f)] private float metresPerSecond = .42f;
        [SerializeField, Min(1f)] private float worldTextureSize = 76f;

        private const double SecondaryScale = 1.63;
        private const double SecondarySpeed = .92;
        private static readonly int FlowOffsetsId = Shader.PropertyToID("_CloudFlowOffsets");
        private static readonly int WorldTextureSizeId = Shader.PropertyToID("_WorldTextureSize");
        private MaterialPropertyBlock[] originalProperties;
        private MaterialPropertyBlock[] workingProperties;
        private Vector4[] flowOffsets;
        private bool propertiesApplied;

        public int CloudCount => cloudRenderers == null ? 0 : cloudRenderers.Length;
        public double PresentationSeconds { get; private set; }

        public void Configure(Renderer[] renderers, Vector2 direction, float speed = .42f, float textureSize = 76f)
        {
            if (renderers == null || renderers.Length == 0)
                throw new ArgumentException("移动云层必须显式绑定云网格Renderer。", nameof(renderers));
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null)
                    throw new ArgumentException("移动云层包含空Renderer引用。", nameof(renderers));
                for (int previous = 0; previous < i; previous++)
                    if (renderers[previous] == renderers[i])
                        throw new ArgumentException("移动云层不能重复绑定同一个Renderer。", nameof(renderers));
            }
            if (!Finite(direction.x) || !Finite(direction.y) || direction.sqrMagnitude < .0001f ||
                !Finite(speed) || speed < 0f || !Finite(textureSize) || textureSize < 1f)
                throw new ArgumentException("移动云层的风向、速度和纹理世界尺寸必须为有效值。");

            RestoreProperties();
            cloudRenderers = (Renderer[])renderers.Clone();
            windDirection = direction.normalized;
            metresPerSecond = speed;
            worldTextureSize = textureSize;
            ResetSession();
        }

        public void ResetSession()
        {
            PresentationSeconds = 0d;
            if (isActiveAndEnabled) ApplyProperties();
        }

        public void Refresh(double displayedTick, double ticksPerSecond)
        {
            if (!isActiveAndEnabled || double.IsNaN(displayedTick) || double.IsInfinity(displayedTick) ||
                double.IsNaN(ticksPerSecond) || double.IsInfinity(ticksPerSecond) || ticksPerSecond <= 0d) return;
            double seconds = Math.Max(0d, displayedTick / ticksPerSecond);
            if (propertiesApplied && seconds == PresentationSeconds) return;
            PresentationSeconds = seconds;
            ApplyProperties();
        }

        public Vector4 GetFlowOffsets(int cloudIndex)
        {
            if (cloudIndex < 0 || cloudIndex >= CloudCount) throw new ArgumentOutOfRangeException(nameof(cloudIndex));
            return flowOffsets == null ? Vector4.zero : flowOffsets[cloudIndex];
        }

        // 禁用只释放渲染属性的所有权；暂停中重新启用仍应恢复同一显示相位。
        private void OnEnable() => ApplyProperties();

        private void OnDisable()
        {
            RestoreProperties();
        }

        private void OnDestroy() => RestoreProperties();

        private void ApplyProperties()
        {
            if (CloudCount == 0) return;
            if (workingProperties == null)
            {
                originalProperties = new MaterialPropertyBlock[CloudCount];
                workingProperties = new MaterialPropertyBlock[CloudCount];
                flowOffsets = new Vector4[CloudCount];
                for (int i = 0; i < CloudCount; i++)
                {
                    originalProperties[i] = new MaterialPropertyBlock();
                    workingProperties[i] = new MaterialPropertyBlock();
                    if (cloudRenderers[i] == null) continue;
                    cloudRenderers[i].GetPropertyBlock(originalProperties[i]);
                    cloudRenderers[i].GetPropertyBlock(workingProperties[i]);
                }
            }

            Vector2 direction = windDirection.sqrMagnitude > .0001f ? windDirection.normalized : Vector2.right;
            double textureSize = Math.Max(1d, worldTextureSize);
            double travelledPeriods = PresentationSeconds * Math.Max(0d, metresPerSecond) / textureSize;
            for (int i = 0; i < CloudCount; i++)
            {
                if (cloudRenderers[i] == null) continue;
                double layerSpeed = .86d + .28d * ((i * 3) % 7) / 6d;
                double travel = travelledPeriods * layerSpeed;
                // 在CPU双精度空间内仅保留整周期余数；Repeat纹理采样跨周期时连续。
                Vector4 offsets = new Vector4(
                    Fraction(travel * direction.x + i * .193d),
                    Fraction(travel * direction.y + i * .317d),
                    Fraction(travel * direction.x * SecondaryScale * SecondarySpeed + i * .271d + .37d),
                    Fraction(travel * direction.y * SecondaryScale * SecondarySpeed + i * .139d + .63d));
                flowOffsets[i] = offsets;
                workingProperties[i].SetVector(FlowOffsetsId, offsets);
                workingProperties[i].SetFloat(WorldTextureSizeId, (float)textureSize);
                cloudRenderers[i].SetPropertyBlock(workingProperties[i]);
            }
            propertiesApplied = true;
        }

        private void RestoreProperties()
        {
            if (originalProperties != null)
                for (int i = 0; i < originalProperties.Length; i++)
                    if (cloudRenderers != null && i < cloudRenderers.Length && cloudRenderers[i] != null)
                        cloudRenderers[i].SetPropertyBlock(originalProperties[i].isEmpty ? null : originalProperties[i]);
            originalProperties = workingProperties = null;
            flowOffsets = null;
            propertiesApplied = false;
        }

        private static float Fraction(double value) => (float)(value - Math.Floor(value));
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
