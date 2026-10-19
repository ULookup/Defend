using UnityEngine;
using UnityEngine.UI;
namespace Defend.Unity
{
    /// <summary>Retains the former cover over the new one for a brief presentation-only dissolve.</summary>
    public sealed class CampaignCoverTransition : MonoBehaviour
    {
        [SerializeField] private Image current, previous;
        [SerializeField] private InterfaceMotionSettings settings;
        private float remaining;
        public bool IsAnimating => remaining > 0;
        public void Show(Sprite sprite)
        {
            if (current == null || current.sprite == sprite) return;
            if (previous != null) previous.sprite = current.sprite;
            current.sprite = sprite;
            remaining = isActiveAndEnabled && !(settings != null && settings.ReducedMotion) ? .18f : 0;
            Apply();
        }
        private void Update()
        {
            if (remaining <= 0) return;
            remaining = settings != null && settings.ReducedMotion ? 0 : Mathf.Max(0, remaining-Time.unscaledDeltaTime);
            Apply();
        }
        private void Apply() { if (previous != null) previous.color = new Color(1,1,1,remaining/.18f); }
        private void OnDisable() { remaining = 0; Apply(); }
    }
}
