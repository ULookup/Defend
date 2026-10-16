using UnityEngine;
using UnityEngine.UI;

namespace Defend.Unity
{
    /// <summary>Crossfades authored card surfaces without replacing the interactive hierarchy.</summary>
    public sealed class InterfaceCardMotion : MonoBehaviour
    {
        [SerializeField] private InterfaceMotionSettings settings;
        [SerializeField] private Image surface;
        [SerializeField] private Image transitionSurface;
        private bool initialized;
        private float remaining;
        private Color surfaceColor;
        private Sprite requestedSprite, previousSprite;
        private Material requestedMaterial, previousMaterial;
        public bool Selected { get; private set; }
        public bool IsAnimating => remaining > 0;

        private void OnEnable() { if (settings != null) settings.Changed += OnPreferenceChanged; }
        private void OnDisable()
        {
            if (settings != null) settings.Changed -= OnPreferenceChanged;
            Finish();
        }
        private void OnPreferenceChanged(bool value) { if (value) Finish(); }
        public void Apply(bool selected, Sprite nextSprite, Material nextMaterial)
        {
            if (surface == null) return;
            if (initialized && Selected == selected && requestedSprite == nextSprite && requestedMaterial == nextMaterial) return;
            bool animate = initialized && transitionSurface != null && isActiveAndEnabled && !(settings != null && settings.ReducedMotion);
            if (!initialized) surfaceColor = surface.color;
            initialized = true;
            Selected = selected;
            if (animate)
            {
                bool reversing = remaining > 0 && previousSprite == nextSprite && previousMaterial == nextMaterial;
                float oldOpacity = remaining / .14f;
                transitionSurface.sprite = surface.sprite;
                transitionSurface.material = surface.material;
                transitionSurface.type = surface.type;
                transitionSurface.preserveAspect = surface.preserveAspect;
                transitionSurface.raycastTarget = false;
                previousSprite = requestedSprite;
                previousMaterial = requestedMaterial;
                // Swapping the two layers and complementing opacity preserves a rapid reversal.
                remaining = reversing ? .14f * (1 - oldOpacity) : .14f;
                transitionSurface.enabled = true;
            }
            surface.sprite = nextSprite;
            surface.material = nextMaterial;
            requestedSprite = nextSprite;
            requestedMaterial = nextMaterial;
            if (animate) ApplyOpacity(); else Finish();
        }
        private void Update()
        {
            if (remaining <= 0) return;
            remaining = Mathf.Max(0, remaining - Time.unscaledDeltaTime);
            ApplyOpacity();
        }
        private void ApplyOpacity()
        {
            if (transitionSurface == null) return;
            Color color = surfaceColor; color.a *= remaining / .14f;
            transitionSurface.color = color;
            transitionSurface.enabled = remaining > 0;
        }
        private void Finish() { remaining = 0; if (transitionSurface != null) transitionSurface.enabled = false; }
    }
}
