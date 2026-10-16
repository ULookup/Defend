using System;
using UnityEngine;

namespace Defend.Unity
{
    /// <summary>仅按共同模拟显示时钟播放共振塔动作；攻击、修复与减速结算仍由 Simulation 负责。</summary>
    [DisallowMultipleComponent]
    public sealed class ResonanceMagneticTowerMotion : MonoBehaviour
    {
        private const float StormImpactSeconds = 27f / 30f;
        [SerializeField] private GameObject animationRoot;
        [SerializeField] private AnimationClip idleClip;
        [SerializeField] private AnimationClip absorptionClip;
        [SerializeField] private AnimationClip repairClip;
        [SerializeField] private AnimationClip stormClip;
        [SerializeField] private AnimationClip stormCoreClip;
        [SerializeField] private Renderer[] absorptionRenderers = Array.Empty<Renderer>();
        [SerializeField] private Renderer[] repairRenderers = Array.Empty<Renderer>();
        [SerializeField] private StormLayer[] stormLayers = Array.Empty<StormLayer>();
        [SerializeField] private float authoredBurstRadius = 1.65f;
        private float burstRadius = 1.65f;

        private double idleSeconds;
        private float absorptionSeconds = -1f;
        private float repairSeconds = -1f;
        private long stormSequence;
        private bool pendingAbsorption;
        private bool pendingRepair;
        private bool alive = true;
        private bool combatActive = true;
        private bool reportedConfigurationError;

        [Serializable]
        private sealed class StormLayer
        {
            public GameObject Root;
            public Renderer[] Renderers;
            public Transform[] RangePivots;
            public Vector3[] AuthoredLocalPositions;
            public Vector3[] AuthoredLocalScales;
            [NonSerialized] public float Seconds = -1;
            [NonSerialized] public long Sequence;
        }

        public bool IsConfigured => animationRoot != null && idleClip != null && absorptionClip != null &&
            repairClip != null && stormClip != null && stormCoreClip != null && stormLayers.Length == 4 &&
            idleClip.length > 0 && absorptionClip.length > 0 && repairClip.length > 0 && stormClip.length > 0;
        public bool IsAbsorbing => absorptionSeconds >= 0;
        public bool IsRepairing => repairSeconds >= 0;
        public bool IsReleasingStorm => ActiveStormLayers > 0;
        public int ActiveStormLayers
        {
            get { int count = 0; foreach (StormLayer layer in stormLayers) if (layer.Seconds >= 0) count++; return count; }
        }
        public double Elapsed => idleSeconds;
        public float StormTime => LatestStormLayer()?.Seconds ?? -1;
        public float BurstRadius => burstRadius;
        public bool IsCombatActive => combatActive;

        public void Configure(GameObject root, AnimationClip idle, AnimationClip absorption, AnimationClip repair,
            AnimationClip storm, AnimationClip stormCore, Renderer[] absorptionMeshes, Renderer[] repairMeshes,
            GameObject[] layerRoots, Renderer[][] layerRenderers, Transform[][] rangePivots)
        {
            animationRoot = root; idleClip = idle; absorptionClip = absorption; repairClip = repair; stormClip = storm;
            absorptionRenderers = absorptionMeshes ?? Array.Empty<Renderer>();
            repairRenderers = repairMeshes ?? Array.Empty<Renderer>();
            stormCoreClip = stormCore;
            stormLayers = new StormLayer[layerRoots.Length];
            for (int index = 0; index < stormLayers.Length; index++)
            {
                var layer = new StormLayer { Root = layerRoots[index], Renderers = layerRenderers[index], RangePivots = rangePivots[index] };
                layer.AuthoredLocalPositions = new Vector3[layer.RangePivots.Length];
                layer.AuthoredLocalScales = new Vector3[layer.RangePivots.Length];
                for (int pivot = 0; pivot < layer.RangePivots.Length; pivot++)
                {
                    layer.AuthoredLocalPositions[pivot] = layer.RangePivots[pivot].localPosition;
                    layer.AuthoredLocalScales[pivot] = layer.RangePivots[pivot].localScale;
                }
                stormLayers[index] = layer;
            }
            reportedConfigurationError = false;
            ResetPose();
        }

        /// <summary>对象复用、重开和卖出时恢复所有效果节点及动画时钟。</summary>
        public void ResetPose()
        {
            idleSeconds = 0;
            absorptionSeconds = repairSeconds = -1;
            stormSequence = 0;
            pendingAbsorption = pendingRepair = false;
            alive = true;
            combatActive = true;
            SetVisible(absorptionRenderers, false); SetVisible(repairRenderers, false);
            foreach (StormLayer layer in stormLayers) { layer.Seconds = -1; layer.Sequence = 0; SetVisible(layer.Renderers, false); }
            if (!IsConfigured) return;
            Sample(absorptionClip, 0); Sample(repairClip, 0);
            foreach (StormLayer layer in stormLayers) SampleStorm(layer, 0);
            Sample(stormCoreClip, 0); Sample(idleClip, 0);
        }

        /// <summary>从实际建筑定义读取范围，只调整外扩电弧与冲击波，不拉伸核心、蓄能或建筑。</summary>
        public void ConfigureBurstRadius(float radius)
        {
            if (radius <= 0 || float.IsNaN(radius) || float.IsInfinity(radius)) return;
            if (Mathf.Approximately(burstRadius, radius)) return;
            burstRadius = radius;
            if (!IsConfigured) return;
            foreach (StormLayer layer in stormLayers)
                SampleStorm(layer, layer.Seconds >= 0 ? layer.Seconds : 0);
        }

        public void SetAlive(bool value)
        {
            if (alive == value) return;
            ResetPose();
            alive = value;
        }

        /// <summary>离开战斗时取消未击穿的预热；已经释放的波面继续按共同时间自然消散。</summary>
        public void SetCombatActive(bool value)
        {
            if (combatActive == value) return;
            bool wasActive = combatActive;
            combatActive = value;
            if (!wasActive || value || !IsConfigured) return;
            foreach (StormLayer layer in stormLayers)
            {
                if (layer.Seconds < 0 || layer.Seconds >= StormImpactSeconds) continue;
                SampleStorm(layer, 0);
                layer.Seconds = -1; SetVisible(layer.Renderers, false);
            }
            if (alive) SamplePose();
        }

        public void NotifyAbsorption()
        {
            if (!alive || !EnsureConfigured()) return;
            // 密集友军射击合并为至多一次补播，不让高射速反复重置能量到达动作。
            if (IsAbsorbing) { pendingAbsorption = true; return; }
            absorptionSeconds = 0; SetVisible(absorptionRenderers, true); SamplePose();
        }

        public void NotifyRepair(bool startAtImpact = true)
        {
            if (!alive || !EnsureConfigured()) return;
            if (IsRepairing) { pendingRepair = true; return; }
            repairSeconds = startAtImpact ? 15f / 30f : 0;
            SetVisible(repairRenderers, true); SamplePose();
        }

        /// <summary>即时结算事件从击穿帧开始；有权威预热事件时传 false 播放完整蓄能。</summary>
        public void NotifyStorm(bool startAtImpact = true)
        {
            if (!alive || !combatActive || !EnsureConfigured()) return;
            StormLayer next = null;
            foreach (StormLayer layer in stormLayers) if (layer.Seconds < 0) { next = layer; break; }
            // 权威预热最短 0.9 秒，三秒素材同时最多占用四层；异常密集调用也保持固定对象上限。
            if (next == null)
            {
                next = stormLayers[0];
                foreach (StormLayer layer in stormLayers) if (layer.Sequence < next.Sequence) next = layer;
            }
            next.Seconds = startAtImpact ? StormImpactSeconds : 0;
            next.Sequence = ++stormSequence;
            SetVisible(next.Renderers, true); SamplePose();
        }

        /// <summary>暂停传零；本组件没有独立 Update、协程或 Unity 时间驱动。</summary>
        public void Advance(float simulationSeconds)
        {
            if (!alive || simulationSeconds <= 0 || float.IsNaN(simulationSeconds) || float.IsInfinity(simulationSeconds)) return;
            if (!EnsureConfigured()) return;
            idleSeconds += simulationSeconds;
            AdvanceAction(ref absorptionSeconds, absorptionClip, absorptionRenderers, simulationSeconds);
            AdvanceAction(ref repairSeconds, repairClip, repairRenderers, simulationSeconds);
            foreach (StormLayer layer in stormLayers)
            {
                if (layer.Seconds < 0) continue;
                layer.Seconds += simulationSeconds;
                if (layer.Seconds < stormClip.length) continue;
                SampleStorm(layer, stormClip.length); layer.Seconds = -1; SetVisible(layer.Renderers, false);
            }
            if (!IsAbsorbing && pendingAbsorption) { pendingAbsorption = false; absorptionSeconds = 0; SetVisible(absorptionRenderers, true); }
            if (!IsRepairing && pendingRepair) { pendingRepair = false; repairSeconds = 15f / 30f; SetVisible(repairRenderers, true); }
            SamplePose();
        }

        private void AdvanceAction(ref float time, AnimationClip clip, Renderer[] renderers, float seconds)
        {
            if (time < 0) return;
            time += seconds;
            if (time < clip.length) return;
            Sample(clip, clip.length);
            time = -1; SetVisible(renderers, false);
        }

        private void SamplePose()
        {
            Sample(idleClip, (float)(idleSeconds % idleClip.length));
            if (IsAbsorbing) Sample(absorptionClip, absorptionSeconds);
            if (IsRepairing) Sample(repairClip, repairSeconds);
            foreach (StormLayer layer in stormLayers)
                if (layer.Seconds >= 0) SampleStorm(layer, layer.Seconds);
            StormLayer latest = LatestStormLayer();
            if (latest != null) Sample(stormCoreClip, latest.Seconds);
        }

        private StormLayer LatestStormLayer()
        {
            StormLayer latest = null;
            foreach (StormLayer layer in stormLayers)
                if (layer.Seconds >= 0 && (latest == null || layer.Sequence > latest.Sequence)) latest = layer;
            return latest;
        }

        private void SampleStorm(StormLayer layer, float seconds)
        {
            // FBX 导入会省略恒定位置/缩放曲线。先恢复制作值，再采样变化通道，避免范围系数逐帧累乘。
            if (layer.AuthoredLocalPositions == null || layer.AuthoredLocalScales == null ||
                layer.AuthoredLocalPositions.Length != layer.RangePivots.Length || layer.AuthoredLocalScales.Length != layer.RangePivots.Length)
            {
                SetVisible(layer.Renderers, false);
                layer.Seconds = -1;
                if (!reportedConfigurationError)
                { reportedConfigurationError = true; Debug.LogError("共振磁暴塔缺少原始范围枢轴数据，请重新制作视觉预制体。", this); }
                return;
            }
            for (int index = 0; index < layer.RangePivots.Length; index++)
            {
                Transform pivot = layer.RangePivots[index];
                if (pivot == null) continue;
                pivot.localPosition = layer.AuthoredLocalPositions[index];
                pivot.localScale = layer.AuthoredLocalScales[index];
            }
            Sample(stormClip, seconds, layer.Root);
            ApplyBurstRadius(layer);
        }

        private void ApplyBurstRadius(StormLayer layer)
        {
            float factor = burstRadius / Mathf.Max(.001f, authoredBurstRadius);
            foreach (Transform pivot in layer.RangePivots)
            {
                if (pivot == null) continue;
                Vector3 scale = pivot.localScale; Vector3 position = pivot.localPosition;
                // 导入层保留 Blender 的 Z-up 局部轴，其 XY 面经 FBX 根旋转后对应 Unity 世界 XZ。
                pivot.localScale = new Vector3(scale.x * factor, scale.y * factor, scale.z);
                pivot.localPosition = new Vector3(position.x * factor, position.y * factor, position.z);
            }
        }

        private void Sample(AnimationClip clip, float seconds, GameObject target = null)
        {
            if (target == null) target = animationRoot;
            Transform root = target.transform;
            Vector3 position = root.localPosition; Quaternion rotation = root.localRotation; Vector3 scale = root.localScale;
            clip.SampleAnimation(target, Mathf.Clamp(seconds, 0, clip.length));
            root.SetLocalPositionAndRotation(position, rotation); root.localScale = scale;
            // 内部效果缩放曲线是资产的实际动画，不能像固定机械件那样统一还原缩放。
        }

        private bool EnsureConfigured()
        {
            if (IsConfigured) return true;
            if (!reportedConfigurationError)
            { reportedConfigurationError = true; Debug.LogError("共振磁暴塔缺少模型根或四组独立动作，请重新制作视觉预制体。", this); }
            return false;
        }

        private static void SetVisible(Renderer[] renderers, bool visible)
        {
            if (renderers == null) return;
            foreach (Renderer item in renderers) if (item != null) item.enabled = visible;
        }

        private void OnDisable()
        {
            ResetPose();
            alive = false;
        }

        private void Awake() => ResetPose();
    }
}
