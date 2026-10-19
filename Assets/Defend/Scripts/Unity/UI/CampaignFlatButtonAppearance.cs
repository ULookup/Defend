using UnityEngine;
using UnityEngine.UI;

namespace Defend.Unity
{
    /// <summary>选关页的平面按钮外观；复用输入状态，不执行缩放、弹跳或发光。</summary>
    public sealed class CampaignFlatButtonAppearance : MonoBehaviour
    {
        [SerializeField] private Button button;
        [SerializeField] private Image surface;
        [SerializeField] private Text label;
        [SerializeField] private InterfaceButtonMotion inputState;
        [SerializeField] private bool primary;

        private void LateUpdate()
        {
            if (button == null || surface == null || label == null) return;
            bool available = button.IsInteractable();
            bool highlighted = available && inputState != null && inputState.IsHighlighted;
            surface.color = !available ? new Color(.3f, .36f, .4f, 1) :
                highlighted ? new Color(.9f, 1, .98f, 1) : Color.white;
            label.color = !available ? new Color(.72f, .78f, .82f, 1) :
                primary ? new Color(.025f, .12f, .13f, 1) : new Color(.89f, .93f, .95f, 1);
        }
    }
}
