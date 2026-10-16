using System.Collections.Generic;
using UnityEngine;

namespace Defend.Unity
{
    /// <summary>
    /// 敌人受击闪白。
    /// 暂停时冻结，游戏倍速不会影响闪白持续时间。
    /// </summary>
    public sealed class HitFlashEffect : MonoBehaviour
    {
        private static readonly int WhiteOverlayId =
            Shader.PropertyToID("_WhiteOverlay");

        [Header("Hit Flash")]

        /// <summary>
        /// 当前闪白强度。
        /// 外部设置为 1 即可触发闪白。
        /// </summary>
        [Range(0f, 1f)]
        public float WhiteOverlay;

        /// <summary>
        /// 每个现实时间秒衰减的闪白强度。
        /// </summary>
        [Min(0f)]
        public float DecayPerSecond = 6f;

        public bool Paused { get; private set; }

        private readonly List<Material> flashMaterials =
            new List<Material>();

        private readonly List<Material> ownedMaterials =
            new List<Material>();

        private readonly List<RendererBinding> rendererBindings =
            new List<RendererBinding>();

        private float lastAppliedValue = -1f;
        private bool materialsCached;

        private sealed class RendererBinding
        {
            public readonly Renderer Renderer;
            public readonly Material[] OriginalMaterials;
            public readonly Material[] RuntimeMaterials;

            public RendererBinding(
                Renderer renderer,
                Material[] originalMaterials,
                Material[] runtimeMaterials)
            {
                Renderer = renderer;
                OriginalMaterials = originalMaterials;
                RuntimeMaterials = runtimeMaterials;
            }
        }

        private void Awake()
        {
            CacheMaterials();
        }

        private void OnEnable()
        {
            if (!materialsCached)
            {
                CacheMaterials();
            }

            lastAppliedValue = -1f;
            ApplyWhiteOverlay(WhiteOverlay);
        }

        /// <summary>由会话反馈时钟推进，单步和暂停不会再受到第二个 Update 时钟影响。</summary>
        public void AdvanceFeedback(float seconds)
        {
            if (seconds <= 0f) return;
            WhiteOverlay = Mathf.MoveTowards(Mathf.Clamp01(WhiteOverlay), 0f, DecayPerSecond * seconds);
            ApplyWhiteOverlay(WhiteOverlay);
        }

        public void SetPaused(bool paused)
        {
            Paused = paused;
        }

        public void Flash()
        {
            WhiteOverlay = 1f;

            // 立即应用，避免等待下一帧。
            ApplyWhiteOverlay(1f);
        }

        public void ResetFlash()
        {
            WhiteOverlay = 0f;
            ApplyWhiteOverlay(0f);
        }

        private void CacheMaterials()
        {
            if (materialsCached)
            {
                return;
            }

            materialsCached = true;

            flashMaterials.Clear();
            ownedMaterials.Clear();
            rendererBindings.Clear();

            Renderer[] renderers =
                GetComponentsInChildren<Renderer>(true);

            // 同一个原始材质在同一个敌人内部复用同一个运行时实例。
            Dictionary<Material, Material> runtimeBySource =
                new Dictionary<Material, Material>();

            for (int rendererIndex = 0;
                 rendererIndex < renderers.Length;
                 rendererIndex++)
            {
                Renderer renderer =
                    renderers[rendererIndex];

                if (renderer == null)
                {
                    continue;
                }

                // 读取共享材质，不会自动创建实例。
                Material[] originalMaterials =
                    renderer.sharedMaterials;

                Material[] assignedMaterials =
                    (Material[])originalMaterials.Clone();

                Material[] runtimeMaterials =
                    new Material[originalMaterials.Length];

                bool changed = false;

                for (int materialIndex = 0;
                     materialIndex < originalMaterials.Length;
                     materialIndex++)
                {
                    Material sourceMaterial =
                        originalMaterials[materialIndex];

                    if (sourceMaterial == null ||
                        !sourceMaterial.HasProperty(WhiteOverlayId))
                    {
                        continue;
                    }

                    if (!runtimeBySource.TryGetValue(
                            sourceMaterial,
                            out Material runtimeMaterial))
                    {
                        runtimeMaterial =
                            new Material(sourceMaterial)
                            {
                                name =
                                    sourceMaterial.name +
                                    " (Hit Flash Instance)",

                                hideFlags =
                                    HideFlags.DontSave
                            };

                        runtimeBySource.Add(
                            sourceMaterial,
                            runtimeMaterial);

                        ownedMaterials.Add(
                            runtimeMaterial);

                        flashMaterials.Add(
                            runtimeMaterial);
                    }

                    assignedMaterials[materialIndex] =
                        runtimeMaterial;

                    runtimeMaterials[materialIndex] =
                        runtimeMaterial;

                    changed = true;
                }

                if (!changed)
                {
                    continue;
                }

                renderer.sharedMaterials =
                    assignedMaterials;

                rendererBindings.Add(
                    new RendererBinding(
                        renderer,
                        originalMaterials,
                        runtimeMaterials));
            }
        }

        private void ApplyWhiteOverlay(float value)
        {
            value =
                Mathf.Clamp01(value);

            for (int index = 0;
                 index < flashMaterials.Count;
                 index++)
            {
                Material material =
                    flashMaterials[index];

                if (material == null)
                {
                    continue;
                }

                material.SetFloat(
                    WhiteOverlayId,
                    value);
            }

            lastAppliedValue = value;
        }

        private void OnDestroy()
        {
            RestoreOriginalMaterials();
            DestroyOwnedMaterials();

            flashMaterials.Clear();
            ownedMaterials.Clear();
            rendererBindings.Clear();

            materialsCached = false;
        }

        private void RestoreOriginalMaterials()
        {
            for (int bindingIndex = 0;
                 bindingIndex < rendererBindings.Count;
                 bindingIndex++)
            {
                RendererBinding binding =
                    rendererBindings[bindingIndex];

                if (binding.Renderer == null)
                {
                    continue;
                }

                Material[] currentMaterials =
                    binding.Renderer.sharedMaterials;

                int materialCount =
                    Mathf.Min(
                        currentMaterials.Length,
                        binding.RuntimeMaterials.Length);

                bool changed = false;

                for (int materialIndex = 0;
                     materialIndex < materialCount;
                     materialIndex++)
                {
                    Material runtimeMaterial =
                        binding.RuntimeMaterials[materialIndex];

                    if (runtimeMaterial == null)
                    {
                        continue;
                    }

                    // 只恢复仍然由本组件持有的材质槽。
                    if (currentMaterials[materialIndex] !=
                        runtimeMaterial)
                    {
                        continue;
                    }

                    currentMaterials[materialIndex] =
                        materialIndex <
                        binding.OriginalMaterials.Length
                            ? binding.OriginalMaterials[materialIndex]
                            : null;

                    changed = true;
                }

                if (changed)
                {
                    binding.Renderer.sharedMaterials =
                        currentMaterials;
                }
            }
        }

        private void DestroyOwnedMaterials()
        {
            for (int index = 0;
                 index < ownedMaterials.Count;
                 index++)
            {
                Material material =
                    ownedMaterials[index];

                if (material == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Destroy(material);
                }
                else
                {
                    DestroyImmediate(material);
                }
            }
        }
    }
}