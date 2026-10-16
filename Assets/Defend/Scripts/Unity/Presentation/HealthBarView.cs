using UnityEngine;
using UnityEngine.UI;

namespace Defend.Unity
{
    /// <summary>预制的世界空间生命条；不参与输入，也不跟随受击节点缩放。</summary>
    public sealed class HealthBarView : MonoBehaviour
    {
        [SerializeField] private CanvasGroup visibility;
        [SerializeField] private Image healthFill, delayedFill;
        [SerializeField] private float waitSeconds = .15f, catchupSeconds = .30f, fadeSeconds = .12f;
        [SerializeField] private float minimumWidthPixels = 32, maximumWidthPixels = 72;
        private readonly HealthBarState state = new HealthBarState();
        private Camera worldCamera;
        public HealthBarState State => state;
        public void Configure(CanvasGroup group, Image immediate, Image delayed) { visibility = group; healthFill = immediate; delayedFill = delayed; }
        public void SetHealth(int health, int maximum) { state.SetHealth(health, maximum, waitSeconds); Render(); }
        public void AdvanceFeedback(float seconds) { state.Advance(seconds, catchupSeconds, fadeSeconds); Render(); FaceCamera(); }
        public void ResetState() { state.Reset(); Render(); }
        private void Render()
        {
            if (visibility != null) { visibility.alpha = state.Opacity; visibility.blocksRaycasts = false; visibility.interactable = false; }
            if (healthFill != null) healthFill.fillAmount = state.Health;
            if (delayedFill != null) delayedFill.fillAmount = state.Trail;
        }
        private void FaceCamera()
        {
            if (worldCamera == null) worldCamera = Camera.main;
            if (worldCamera == null) return;
            transform.rotation = worldCamera.transform.rotation;
            float unitsPerPixel = worldCamera.orthographic ? 2 * worldCamera.orthographicSize / worldCamera.pixelHeight :
                2 * Mathf.Max(.1f, Vector3.Dot(transform.position - worldCamera.transform.position, worldCamera.transform.forward)) * Mathf.Tan(worldCamera.fieldOfView * .5f * Mathf.Deg2Rad) / worldCamera.pixelHeight;
            float width = Mathf.Clamp(0.8f / Mathf.Max(.00001f, unitsPerPixel), minimumWidthPixels, maximumWidthPixels);
            transform.localScale = Vector3.one * (width * unitsPerPixel / 100f);
        }
    }
}
