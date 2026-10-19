using UnityEngine;
namespace Defend.Unity
{
    /// <summary>Groups enter on unscaled time; the authored layout is restored on disable and reduced motion.</summary>
    public sealed class CampaignSelectionEntrance : MonoBehaviour
    {
        [SerializeField] private CanvasGroup group;
        [SerializeField] private InterfaceMotionSettings settings;
        [SerializeField] private float delay;
        private RectTransform rectangle;
        private Vector2 rest;
        private float elapsed;
        private bool captured;
        public bool IsAnimating => isActiveAndEnabled && elapsed < delay + .28f && !(settings != null && settings.ReducedMotion);
        private void OnEnable()
        {
            if (!captured) { rectangle = (RectTransform)transform; rest = rectangle.anchoredPosition; captured = true; }
            elapsed = 0; Apply();
        }
        private void Update() { if (!IsAnimating) { Apply(); return; } elapsed += Time.unscaledDeltaTime; Apply(); }
        private void Apply()
        {
            if (group == null || rectangle == null) return;
            float t = settings != null && settings.ReducedMotion ? 1 : Mathf.Clamp01((elapsed-delay)/.28f);
            float eased = 1-Mathf.Pow(1-t, 3);
            group.alpha = eased;
            group.interactable = group.blocksRaycasts = eased >= .6f;
            rectangle.anchoredPosition = rest + Vector2.down * (12*(1-eased));
        }
        private void OnDisable()
        {
            if (rectangle != null) rectangle.anchoredPosition = rest;
            if (group != null) { group.alpha = 1; group.interactable = group.blocksRaycasts = true; }
            elapsed = delay + .28f;
        }
    }
}
