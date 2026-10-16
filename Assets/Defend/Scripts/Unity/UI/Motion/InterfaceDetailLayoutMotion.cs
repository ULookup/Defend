using UnityEngine;
using UnityEngine.UI;

namespace Defend.Unity
{
    /// <summary>Expands the saved detail layout without scaling text, controls, or either background texture.</summary>
    public sealed class InterfaceDetailLayoutMotion : MonoBehaviour
    {
        [SerializeField] private InterfaceMotionSettings settings;
        [SerializeField] private RectTransform panel;
        [SerializeField] private Image compactBackground, expandedBackground;
        [SerializeField] private IngameArtInterface.ExactDetailPlacement[] placements;
        [SerializeField] private Vector2 compactSize = new Vector2(468, 670);
        [SerializeField] private Vector2 expandedSize = new Vector2(468, 867);
        [SerializeField, Min(.01f)] private float duration = .24f;
        private bool initialized, targetExpanded, animating;
        private float expansion, fromExpansion, elapsed;
        private Color compactColor, expandedColor;

        public bool TargetExpanded => targetExpanded;
        public bool IsAnimating => animating;
        public float Expansion => expansion;

        private void Awake() { Initialize(); }
        private void Initialize()
        {
            if (initialized) return;
            initialized = true;
            targetExpanded = panel != null && panel.rect.height > (compactSize.y + expandedSize.y) * .5f;
            expansion = targetExpanded ? 1 : 0;
            if (compactBackground != null)
            {
                compactColor = compactBackground.color;
                compactBackground.raycastTarget = false;
                SetBackgroundSize(compactBackground, compactSize);
            }
            if (expandedBackground != null)
            {
                expandedColor = expandedBackground.color;
                expandedBackground.raycastTarget = false;
                SetBackgroundSize(expandedBackground, expandedSize);
            }
            Apply();
        }
        private static void SetBackgroundSize(Image background, Vector2 size)
        {
            // The authoring layer anchors both textures to the same top edge inside a background-only mask.
            background.preserveAspect = true;
            background.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, size.x);
            background.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, size.y);
        }
        private void OnEnable()
        {
            Initialize();
            if (settings != null) settings.Changed += OnPreferenceChanged;
        }
        private void OnDisable()
        {
            if (settings != null) settings.Changed -= OnPreferenceChanged;
            if (initialized) Complete();
        }
        private void OnPreferenceChanged(bool reduced)
        {
            if (reduced) Complete();
        }

        public void SetExpanded(bool expanded, bool immediate = false)
        {
            Initialize();
            bool changed = targetExpanded != expanded;
            targetExpanded = expanded;
            if (immediate || !isActiveAndEnabled || (settings != null && settings.ReducedMotion))
            {
                Complete();
                return;
            }
            if (!changed) return;
            // Rapid reversals continue at the current height and current row positions.
            fromExpansion = expansion;
            elapsed = 0;
            animating = true;
        }
        private void Update()
        {
            if (!animating) return;
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed / Mathf.Max(.01f, duration));
            float eased = 1 - Mathf.Pow(1 - progress, 3);
            expansion = Mathf.Lerp(fromExpansion, targetExpanded ? 1 : 0, eased);
            Apply();
            if (progress >= 1) Complete();
        }
        private void Complete()
        {
            animating = false;
            expansion = targetExpanded ? 1 : 0;
            Apply();
        }
        private void Apply()
        {
            if (panel != null) panel.sizeDelta = Vector2.Lerp(compactSize, expandedSize, expansion);
            if (placements != null)
                foreach (var placement in placements)
                    if (placement != null && placement.target != null)
                        placement.target.anchoredPosition = Vector2.Lerp(placement.compact, placement.expanded, expansion);
            SetBackgroundOpacity(compactBackground, compactColor, 1 - expansion);
            // The expanded texture stays opaque beneath the fading compact texture, so the world cannot bleed through.
            SetBackgroundOpacity(expandedBackground, expandedColor, expansion > 0 ? 1 : 0);
        }
        private static void SetBackgroundOpacity(Image background, Color color, float opacity)
        {
            if (background == null) return;
            color.a *= opacity;
            background.color = color;
            background.enabled = opacity > 0;
        }
    }
}
