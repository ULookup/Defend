using UnityEngine;

namespace Defend.Unity
{
    /// <summary>局外页面独立适配，不修改共享 Canvas 或局内布局。</summary>
    public sealed class OutgameCanvasLayout : MonoBehaviour
    {
        [SerializeField] private RectTransform safeArea;
        [SerializeField] private Vector2 referenceSize = new Vector2(1674, 943);
        private void OnEnable() { Fit(); }
        private void OnRectTransformDimensionsChange() { Fit(); }
        private void Fit()
        {
            if (safeArea == null || !(transform is RectTransform parent)) return;
            float scale = Mathf.Min(parent.rect.width / referenceSize.x, parent.rect.height / referenceSize.y);
            safeArea.sizeDelta = referenceSize;
            safeArea.localScale = Vector3.one * Mathf.Max(.01f, scale);
            safeArea.anchoredPosition = Vector2.zero;
        }
    }
}
