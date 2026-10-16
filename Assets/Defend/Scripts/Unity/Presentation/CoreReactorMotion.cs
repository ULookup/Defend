using System;
using System.Collections.Generic;
using UnityEngine;

namespace Defend.Unity
{
    public enum CoreReactorPhase { Construction, Startup, Operation, Destroyed }

    /// <summary>按共同显示时钟采样核心机械动作；生命、护盾和建造权限仍由模拟世界决定。</summary>
    [DisallowMultipleComponent]
    public sealed class CoreReactorMotion : MonoBehaviour
    {
        [SerializeField] private GameObject animationRoot;
        [SerializeField] private AnimationClip constructionClip;
        [SerializeField] private AnimationClip startupClip;
        [SerializeField] private AnimationClip operationClip;
        [SerializeField] private Renderer[] emissiveRenderers = Array.Empty<Renderer>();

        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private readonly List<EmissionSlot> emissionSlots = new List<EmissionSlot>();
        private Transform[] animatedTransforms;
        private Vector3[] authoredScales;
        private double elapsedSeconds;
        private bool alive = true;
        private bool cached;
        private bool reportedConfigurationError;

        private sealed class EmissionSlot
        {
            public Renderer Renderer;
            public int MaterialIndex;
            public Color AuthoredColor;
            public readonly MaterialPropertyBlock Properties = new MaterialPropertyBlock();
        }

        public CoreReactorPhase Phase { get; private set; } = CoreReactorPhase.Construction;
        public double Elapsed => elapsedSeconds;
        public float EmissionIntensity { get; private set; }
        public bool IsConfigured => animationRoot != null && constructionClip != null &&
            startupClip != null && operationClip != null && constructionClip.length > 0f &&
            startupClip.length > 0f && operationClip.length > 0f;

        public void Configure(GameObject root, AnimationClip construction, AnimationClip startup,
            AnimationClip operation, Renderer[] emitters)
        {
            RestoreEmission();
            animationRoot = root;
            constructionClip = construction;
            startupClip = startup;
            operationClip = operation;
            emissiveRenderers = emitters ?? Array.Empty<Renderer>();
            cached = false;
            reportedConfigurationError = false;
            ResetPose();
        }

        /// <summary>重新部署或对象池复用时恢复第一帧；不会移动建筑根或改变其占地尺寸。</summary>
        public void ResetPose()
        {
            elapsedSeconds = 0;
            alive = true;
            Phase = CoreReactorPhase.Construction;
            if (!EnsureConfigured()) return;
            SampleCurrentPose();
        }

        public void SetAlive(bool value)
        {
            if (alive == value) return;
            if (value) { ResetPose(); return; }
            alive = false;
            Phase = CoreReactorPhase.Destroyed;
            CacheReferences();
            ApplyEmission(0f);
        }

        /// <summary>调用方传入模拟显示秒数；暂停传零，倍速和单步沿用同一时间源。</summary>
        public void Advance(float seconds)
        {
            if (!alive || seconds <= 0f || float.IsNaN(seconds) || float.IsInfinity(seconds)) return;
            if (!EnsureConfigured()) return;
            elapsedSeconds += seconds;
            SampleCurrentPose();
        }

        private bool EnsureConfigured()
        {
            CacheReferences();
            if (IsConfigured) return true;
            if (!reportedConfigurationError)
            {
                reportedConfigurationError = true;
                Debug.LogError("核心机械动画缺少模型根或有效的建造、启动、运转片段，请检查核心预制体。", this);
            }
            ApplyEmission(0f);
            return false;
        }

        private void CacheReferences()
        {
            if (cached) return;
            cached = true;
            emissionSlots.Clear();
            animatedTransforms = animationRoot == null ? Array.Empty<Transform>() :
                animationRoot.GetComponentsInChildren<Transform>(true);
            authoredScales = new Vector3[animatedTransforms.Length];
            for (int index = 0; index < animatedTransforms.Length; index++)
                authoredScales[index] = animatedTransforms[index].localScale;

            var visited = new HashSet<Renderer>();
            if (emissiveRenderers == null) return;
            foreach (Renderer renderer in emissiveRenderers)
            {
                if (renderer == null || !visited.Add(renderer)) continue;
                Material[] materials = renderer.sharedMaterials;
                for (int index = 0; index < materials.Length; index++)
                {
                    Material material = materials[index];
                    if (material == null || !material.HasProperty(EmissionColorId)) continue;
                    Color emission = material.GetColor(EmissionColorId);
                    if (emission.maxColorComponent <= 0f) continue;
                    emissionSlots.Add(new EmissionSlot
                    { Renderer = renderer, MaterialIndex = index, AuthoredColor = emission });
                }
            }
        }

        private void SampleCurrentPose()
        {
            double startupAt = constructionClip.length;
            double operationAt = startupAt + startupClip.length;
            AnimationClip clip;
            float clipTime;
            float emission;
            if (elapsedSeconds < startupAt)
            {
                Phase = CoreReactorPhase.Construction;
                clip = constructionClip;
                clipTime = (float)elapsedSeconds;
                emission = .12f;
            }
            else if (elapsedSeconds < operationAt)
            {
                Phase = CoreReactorPhase.Startup;
                clip = startupClip;
                clipTime = (float)(elapsedSeconds - startupAt);
                // 与源动画一致：启动后段达到全亮，余下时间保持稳定。
                float progress = clipTime / startupClip.length;
                emission = Mathf.SmoothStep(.12f, 1f, Mathf.Clamp01(progress / .874f));
            }
            else
            {
                Phase = CoreReactorPhase.Operation;
                clip = operationClip;
                clipTime = (float)((elapsedSeconds - operationAt) % operationClip.length);
                emission = 1f;
            }

            Transform root = animationRoot.transform;
            Vector3 rootPosition = root.localPosition;
            Quaternion rootRotation = root.localRotation;
            Vector3 rootScale = root.localScale;
            clip.SampleAnimation(animationRoot, clipTime);
            // 导入片段即使带有根变换或缩放曲线，也不能覆盖游戏中的落位与制作尺寸。
            root.SetLocalPositionAndRotation(rootPosition, rootRotation);
            for (int index = 0; index < animatedTransforms.Length; index++)
                if (animatedTransforms[index] != null) animatedTransforms[index].localScale = authoredScales[index];
            root.localScale = rootScale;
            ApplyEmission(emission);
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

        private void RestoreEmission()
        {
            foreach (EmissionSlot slot in emissionSlots)
            {
                if (slot.Renderer == null) continue;
                slot.Renderer.GetPropertyBlock(slot.Properties, slot.MaterialIndex);
                slot.Properties.SetColor(EmissionColorId, slot.AuthoredColor);
                slot.Renderer.SetPropertyBlock(slot.Properties, slot.MaterialIndex);
            }
        }

        private void OnDisable() => SetAlive(false);
        private void OnDestroy() => RestoreEmission();
    }
}
