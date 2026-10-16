using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Defend.Unity
{
    /// <summary>只控制按下反馈；唯一开始动作由 Button.onClick 负责。</summary>
    public sealed class StartButtonFeedback : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler, ISubmitHandler
    {
        [SerializeField] private Image surface, cracks;
        [SerializeField] private Sprite normalSprite, pressedSprite;
        private float restoreAt;
        public void OnPointerDown(PointerEventData data) { if (data.button == PointerEventData.InputButton.Left) SetPressed(true); }
        public void OnPointerUp(PointerEventData data) { SetPressed(false); }
        public void OnPointerExit(PointerEventData data) { SetPressed(false); }
        public void OnSubmit(BaseEventData data) { SetPressed(true); restoreAt = Time.unscaledTime + .12f; }
        private void Update() { if (restoreAt > 0 && Time.unscaledTime >= restoreAt) { restoreAt = 0; SetPressed(false); } }
        private void OnDisable() { restoreAt = 0; SetPressed(false); }
        private void SetPressed(bool pressed)
        {
            if (surface != null) surface.sprite = pressed ? pressedSprite : normalSprite;
            if (cracks != null) { cracks.gameObject.SetActive(pressed); cracks.enabled = pressed; }
        }
    }
}
