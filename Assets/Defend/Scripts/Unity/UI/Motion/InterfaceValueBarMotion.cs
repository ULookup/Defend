using UnityEngine;
using UnityEngine.UI;

namespace Defend.Unity
{
    /// <summary>Displays recent loss separately from the authoritative value supplied by the presenter.</summary>
    public sealed class InterfaceValueBarMotion : MonoBehaviour
    {
        [SerializeField] private InterfaceMotionSettings settings;
        [SerializeField] private Image fill, trail, flash;
        private bool initialized;
        private float displayed, target, trailValue, from, elapsed, duration, trailDelay, trailFrom, trailElapsed, flashRemaining;
        private Color flashColor;
        private float flashCooldown;
        private bool visualsInitialized;
        public float TargetRatio => target;
        public float DisplayedRatio => displayed;
        public float TrailRatio => trailValue;
        public bool IsAnimating => !Mathf.Approximately(displayed, target) || !Mathf.Approximately(trailValue, target) || flashRemaining > 0;

        public void Configure(InterfaceMotionSettings preferences, Image valueFill, Image lossTrail = null, Image valueFlash = null)
        {
            bool reuseFlash = visualsInitialized && flash != null && flash == valueFlash;
            Color restingFlashColor = flashColor;
            if (settings != null && isActiveAndEnabled) settings.Changed -= OnPreferenceChanged;
            settings = preferences;
            fill = valueFill; trail = lossTrail; flash = valueFlash;
            ConfigureImage(fill); ConfigureImage(trail); ConfigureImage(flash);
            visualsInitialized = false;
            InitializeVisuals();
            if (reuseFlash) flashColor = restingFlashColor;
            if (settings != null && isActiveAndEnabled) settings.Changed += OnPreferenceChanged;
            Snap();
        }

        private static void ConfigureImage(Image image)
        {
            if (image == null) return;
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Horizontal;
            image.fillOrigin = (int)Image.OriginHorizontal.Left;
            image.raycastTarget = false;
        }

        private void Awake() { InitializeVisuals(); }
        private void InitializeVisuals()
        {
            if (visualsInitialized) return;
            visualsInitialized = true;
            if (flash != null) flashColor = flash.color;
        }
        private void OnEnable() { InitializeVisuals(); if (settings != null) settings.Changed += OnPreferenceChanged; }
        private void OnDisable()
        {
            if (settings != null) settings.Changed -= OnPreferenceChanged;
            Snap(); initialized = false;
        }
        private void OnPreferenceChanged(bool value) { if (value) Snap(); }
        public void SetValue(float ratio, bool snap = false)
        {
            InitializeVisuals();
            if (float.IsNaN(ratio) || float.IsInfinity(ratio)) ratio = 0;
            ratio = Mathf.Clamp01(ratio);
            if (!initialized || snap || !isActiveAndEnabled || (settings != null && settings.ReducedMotion))
            {
                initialized = true; target = ratio; Snap(); return;
            }
            if (Mathf.Approximately(target, ratio)) return;
            bool loss = ratio < target;
            bool wasLosing = trailDelay > 0 || trailValue > displayed + .001f;
            target = ratio; from = displayed; elapsed = 0; duration = loss ? .09f : .26f;
            if (loss)
            {
                trailValue = Mathf.Max(trailValue, displayed);
                // Damage streams share the existing hold window instead of delaying the trail indefinitely.
                if (!wasLosing) trailDelay = .12f;
                trailFrom = trailValue; trailElapsed = 0;
                if (flashCooldown <= 0) { flashRemaining = .15f; flashCooldown = .5f; }
            }
            else { trailDelay = 0; trailFrom = trailValue; trailElapsed = 0; }
        }
        private void Update()
        {
            if (!initialized) return;
            float delta = Time.unscaledDeltaTime;
            flashCooldown = Mathf.Max(0, flashCooldown - delta);
            elapsed += delta;
            float t = duration <= 0 ? 1 : Mathf.Clamp01(elapsed / duration);
            displayed = Mathf.Lerp(from, target, 1 - (1 - t) * (1 - t));
            if (target >= trailValue) trailValue = displayed;
            else if (trailDelay > 0) trailDelay = Mathf.Max(0, trailDelay - delta);
            else { trailElapsed += delta; trailValue = Mathf.Max(displayed, Mathf.Lerp(trailFrom, target, Mathf.Clamp01(trailElapsed / .32f))); }
            flashRemaining = Mathf.Max(0, flashRemaining - delta);
            Apply();
        }
        private void Snap()
        {
            displayed = trailValue = from = trailFrom = target;
            elapsed = duration = trailDelay = trailElapsed = flashRemaining = flashCooldown = 0;
            Apply();
        }
        private void Apply()
        {
            if (fill != null) fill.fillAmount = displayed;
            if (trail != null)
            {
                trail.fillAmount = trailValue;
                // A translucent authored fill must keep its original resting colour.
                trail.enabled = trailValue > displayed + .001f;
            }
            if (flash != null)
            {
                Color color = flashColor; color.a *= flashRemaining / .15f; flash.color = color;
                flash.enabled = flashRemaining > 0;
                flash.fillAmount = Mathf.Max(displayed, trailValue);
            }
        }
    }
}
