using UnityEngine;
using UnityEngine.EventSystems;

namespace Defend.Unity
{
    /// <summary>独立焦点标记可与已选中状态并存，不改变按钮布局或命中区域。</summary>
    public sealed class CampaignSelectionFocus : MonoBehaviour, ISelectHandler, IDeselectHandler
    {
        [SerializeField] private GameObject indicator;
        public void OnSelect(BaseEventData eventData) { if (indicator != null) indicator.SetActive(true); }
        public void OnDeselect(BaseEventData eventData) { if (indicator != null) indicator.SetActive(false); }
        private void OnEnable()
        { if (indicator != null) indicator.SetActive(EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject); }
        private void OnDisable() { if (indicator != null) indicator.SetActive(false); }
    }
}
