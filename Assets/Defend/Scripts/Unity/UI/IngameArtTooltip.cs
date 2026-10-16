using UnityEngine;
using UnityEngine.EventSystems;

namespace Defend.Unity
{
    public sealed class IngameArtTooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
    {
        [SerializeField] private IngameArtInterface owner;
        [SerializeField] private string caption;
        public void Configure(IngameArtInterface view, string value) { owner = view; caption = value; }
        public void OnPointerEnter(PointerEventData eventData) { owner?.RequestTooltip(caption, (RectTransform)transform); }
        public void OnPointerExit(PointerEventData eventData) { owner?.HideTooltip((RectTransform)transform); }
        public void OnSelect(BaseEventData eventData) { owner?.RequestTooltip(caption, (RectTransform)transform); }
        public void OnDeselect(BaseEventData eventData) { owner?.HideTooltip((RectTransform)transform); }
        private void OnDisable() { owner?.HideTooltip((RectTransform)transform); }
    }
}
