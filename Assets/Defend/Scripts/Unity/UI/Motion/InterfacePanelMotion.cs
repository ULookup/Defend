using UnityEngine;

namespace Defend.Unity
{
    /// <summary>Keeps root layout stable and changes input ownership immediately, before visual transitions finish.</summary>
    public sealed class InterfacePanelMotion : MonoBehaviour
    {
        [SerializeField] private InterfaceMotionSettings settings;
        [SerializeField] private CanvasGroup group;
        [SerializeField] private CanvasGroup contentGroup;
        [SerializeField] private RectTransform visual;
        [SerializeField] private bool receiveInput = true;
        [SerializeField, Min(.01f)] private float enterDuration = .22f;
        [SerializeField, Min(.01f)] private float exitDuration = .14f;
        [SerializeField, Min(0)] private float enterDelay;
        [SerializeField] private Vector2 enterOffset = new Vector2(0, -12);
        [SerializeField] private Vector2 exitOffset = new Vector2(0, -8);
        [SerializeField, Range(.8f, 1f)] private float enterScale = 1;
        [SerializeField, Range(.8f, 1f)] private float exitScale = 1;
        [SerializeField, Min(.01f)] private float contentDuration = .16f;
        [SerializeField] private Vector2 contentOffset = new Vector2(6, 0);
        [SerializeField, Range(0, 1)] private float contentOpacity = .65f;
        private bool initialized, targetVisible, animating;
        private Vector2 restPosition, fromPosition, targetPosition;
        private Vector3 restScale, fromScale, targetScale;
        private float fromAlpha, targetAlpha, fromContentAlpha, targetContentAlpha, elapsed, duration, delayRemaining;
        public bool TargetVisible => targetVisible;
        public bool IsAnimating => animating;
        public bool IsExiting => !targetVisible && gameObject.activeInHierarchy &&
            (animating || group != null && group.alpha > .001f);
        public bool AcceptsInput => receiveInput && targetVisible && isActiveAndEnabled &&
            (group == null || group.interactable);

        private void Awake() { Initialize(); }
        private void Initialize()
        {
            if (initialized) return;
            initialized = true;
            if (contentGroup == group) contentGroup = null;
            if (visual == transform) visual = null;
            if (visual != null) { restPosition = visual.anchoredPosition; restScale = visual.localScale; }
            targetVisible = gameObject.activeSelf;
        }
        private void OnEnable()
        {
            Initialize();
            if (settings != null) settings.Changed += OnPreferenceChanged;
        }
        private void OnDisable()
        {
            if (settings != null) settings.Changed -= OnPreferenceChanged;
            animating = false; elapsed = delayRemaining = 0;
            // An ancestor can hide a panel mid-transition; reopening must not retain partial opacity.
            if (group != null) group.alpha = targetVisible ? 1 : 0;
            if (contentGroup != null) contentGroup.alpha = 1;
            UpdateInput();
            RestoreTransform();
        }
        private void OnPreferenceChanged(bool value) { if (value) Complete(); }
        public void SetVisible(bool visible, bool immediate = false, float delay = -1f, float transitionDuration = -1f, bool contentOnly = false)
        {
            Initialize();
            bool wasActive = gameObject.activeSelf;
            bool changed = targetVisible != visible || (visible && !wasActive);
            bool preserveBackdrop = visible && contentOnly && contentGroup != null;
            targetVisible = visible;
            if (visible && !wasActive)
            {
                if (group != null) group.alpha = preserveBackdrop ? 1 : 0;
                if (contentGroup != null) contentGroup.alpha = preserveBackdrop ? 0 : 1;
                if (visual != null)
                {
                    visual.anchoredPosition = restPosition + enterOffset;
                    visual.localScale = restScale * enterScale;
                }
                gameObject.SetActive(true);
            }
            else if (changed && preserveBackdrop && group != null)
            {
                // Keep partially exiting content continuous while replacing the previous page's backdrop.
                contentGroup.alpha *= group.alpha;
                group.alpha = 1;
            }
            UpdateInput();
            if (immediate || !isActiveAndEnabled || (settings != null && settings.ReducedMotion)) { Complete(); return; }
            if (!changed) return;
            if (!visible && group != null && group.alpha <= .001f) { Complete(); return; }
            Vector2 destination = visible ? restPosition : restPosition + exitOffset;
            Vector3 scale = restScale * (visible ? 1 : exitScale);
            // A close during content entry keeps that content's opacity until the root has faded away.
            float contentDestination = visible || contentGroup == null ? 1 : contentGroup.alpha;
            float distance = RemainingDistance(visible ? 1 : 0, contentDestination, destination, scale);
            if (distance <= .001f) { Complete(); return; }
            float seconds = transitionDuration > 0 ? transitionDuration : visible ? enterDuration : exitDuration;
            // Reversals travel only the remaining distance; an already-visible panel never waits again.
            float wait = visible && !wasActive ? Mathf.Max(0, delay >= 0 ? delay : enterDelay) : 0;
            StartTransition(visible ? 1 : 0, destination, scale, seconds * Mathf.Max(.2f, distance), wait, contentDestination);
        }
        public void ReplayContent() { ReplayContent(contentOffset); }
        public void ReplayContent(Vector2 offset)
        {
            Initialize();
            if (!targetVisible || !isActiveAndEnabled || (settings != null && settings.ReducedMotion)) return;
            // Repeated changes start from the current display, never queue stale content.
            if (!animating)
            {
                if (contentGroup != null) contentGroup.alpha = contentOpacity;
                else if (group != null) group.alpha = contentOpacity;
                if (visual != null) visual.anchoredPosition = restPosition + offset;
            }
            StartTransition(1, restPosition, restScale, contentDuration);
            UpdateInput();
        }
        private float RemainingDistance(float alpha, float contentAlpha, Vector2 position, Vector3 scale)
        {
            float remaining = group != null ? Mathf.Abs(alpha - group.alpha) : 0;
            if (contentGroup != null) remaining = Mathf.Max(remaining, Mathf.Abs(contentAlpha - contentGroup.alpha));
            if (visual != null)
            {
                float positionRange = Mathf.Max(enterOffset.magnitude, exitOffset.magnitude);
                if (positionRange > .001f) remaining = Mathf.Max(remaining, Vector2.Distance(visual.anchoredPosition, position) / positionRange);
                float scaleRange = restScale.magnitude * Mathf.Max(1 - enterScale, 1 - exitScale);
                if (scaleRange > .001f) remaining = Mathf.Max(remaining, Vector3.Distance(visual.localScale, scale) / scaleRange);
            }
            return Mathf.Clamp01(remaining);
        }
        private void StartTransition(float alpha, Vector2 position, Vector3 scale, float seconds, float delay = 0, float contentAlpha = 1)
        {
            fromAlpha = group != null ? group.alpha : 1;
            fromContentAlpha = contentGroup != null ? contentGroup.alpha : 1;
            fromPosition = visual != null ? visual.anchoredPosition : restPosition;
            fromScale = visual != null ? visual.localScale : restScale;
            targetAlpha = alpha; targetPosition = position; targetScale = scale;
            targetContentAlpha = contentAlpha;
            duration = Mathf.Max(.01f, seconds); elapsed = 0; delayRemaining = delay; animating = true;
        }
        private void Update()
        {
            if (!animating) return;
            float delta = Time.unscaledDeltaTime;
            if (delayRemaining > 0)
            {
                delayRemaining -= delta;
                if (delayRemaining > 0) return;
                delta = -delayRemaining;
                delayRemaining = 0;
            }
            elapsed += delta;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = targetVisible ? 1 - Mathf.Pow(1 - t, 3) : t * t * (3 - 2 * t);
            if (group != null) group.alpha = Mathf.Lerp(fromAlpha, targetAlpha, eased);
            if (contentGroup != null) contentGroup.alpha = Mathf.Lerp(fromContentAlpha, targetContentAlpha, eased);
            if (visual != null)
            {
                visual.anchoredPosition = Vector2.LerpUnclamped(fromPosition, targetPosition, eased);
                visual.localScale = Vector3.LerpUnclamped(fromScale, targetScale, eased);
            }
            UpdateInput();
            if (t >= 1) Complete();
        }
        private void UpdateInput()
        {
            if (group == null) return;
            group.blocksRaycasts = receiveInput && targetVisible;
            float opacity = group.alpha * (contentGroup != null ? contentGroup.alpha : 1);
            group.interactable = receiveInput && targetVisible && opacity >= .6f;
        }
        private void Complete()
        {
            animating = false; elapsed = delayRemaining = 0;
            if (group != null) group.alpha = targetVisible ? 1 : 0;
            if (contentGroup != null) contentGroup.alpha = 1;
            UpdateInput();
            RestoreTransform();
            if (!targetVisible && gameObject.activeSelf) gameObject.SetActive(false);
        }
        private void RestoreTransform()
        {
            if (visual == null) return;
            visual.anchoredPosition = restPosition;
            visual.localScale = restScale;
        }
    }
}
