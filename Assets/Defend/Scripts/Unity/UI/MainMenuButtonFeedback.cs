using UnityEngine;
using UnityEngine.EventSystems;

namespace Defend.Unity
{
    /// <summary>金属牌高亮时保持文字对比度，支持鼠标与键盘导航。</summary>
    [RequireComponent(typeof(UnityEngine.UI.Button))]
    public sealed class MainMenuButtonFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler
    {
        [SerializeField] private UnityEngine.UI.Text label;
        [SerializeField] private GameObject focusIndicator;
        [SerializeField] private Color normalInk = new Color(.08f, .12f, .15f);
        [SerializeField] private Color activeInk = new Color(.84f, 1f, 1f);
        private bool hovered, pressed, focused;

        public void Configure(UnityEngine.UI.Text text, Color normal, Color active, GameObject indicator)
        {
            label = text; normalInk = normal; activeInk = active; focusIndicator = indicator; Refresh();
        }

        private void OnEnable()
        {
            hovered = pressed = false;
            focused = EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject;
            Refresh();
        }
        private void OnDisable() { hovered = pressed = focused = false; Refresh(); }
        public void OnPointerEnter(PointerEventData eventData) { hovered = true; Refresh(); }
        public void OnPointerExit(PointerEventData eventData) { hovered = false; Refresh(); }
        public void OnPointerDown(PointerEventData eventData) { if (eventData.button == PointerEventData.InputButton.Left) { pressed = true; Refresh(); } }
        public void OnPointerUp(PointerEventData eventData) { pressed = false; Refresh(); }
        public void OnSelect(BaseEventData eventData) { focused = true; Refresh(); }
        public void OnDeselect(BaseEventData eventData) { focused = false; Refresh(); }
        private void Refresh()
        {
            // 返回弹窗只恢复导航焦点，不能把焦点当成持续按住鼠标。
            bool active = hovered || pressed;
            if (focusIndicator != null) focusIndicator.SetActive(focused && isActiveAndEnabled);
            if (label != null) label.color = active ? activeInk : normalInk;
            var button = GetComponent<UnityEngine.UI.Button>();
            if (button != null && button.transition == UnityEngine.UI.Selectable.Transition.SpriteSwap && button.targetGraphic is UnityEngine.UI.Image image)
                image.overrideSprite = active ? button.spriteState.pressedSprite : image.sprite;
        }
    }
}
