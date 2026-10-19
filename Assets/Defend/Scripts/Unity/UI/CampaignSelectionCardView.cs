using UnityEngine;
using UnityEngine.UI;

namespace Defend.Unity
{
    /// <summary>只表现关卡状态，解锁权限由会话判断；按钮输入与动效复用现役组件。</summary>
    public sealed class CampaignSelectionCardView : MonoBehaviour
    {
        [SerializeField] private Image cover, stateIcon, surface;
        [SerializeField] private Sprite availableIcon, lockedIcon, completedIcon;
        [SerializeField] private InterfaceButtonMotion motion;
        [SerializeField] private GameObject selectionIndicator;

        public void Present(Sprite image, bool selected, bool unlocked, bool completed)
        {
            cover.sprite = image;
            cover.color = unlocked ? Color.white : new Color(.92f, .95f, .96f, 1);
            stateIcon.sprite = completed ? completedIcon : unlocked ? availableIcon : lockedIcon;
            stateIcon.color = unlocked ? new Color(.35f, 1f, .88f) : new Color(.65f, .75f, .80f);
            if (selectionIndicator != null) selectionIndicator.SetActive(selected);
            motion?.SetSelected(selected);
        }
        private void LateUpdate()
        {
            if (surface != null) surface.color = motion != null && motion.IsHighlighted
                ? new Color(.9f, .97f, .98f, 1) : Color.white;
        }
    }
}
