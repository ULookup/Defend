using UnityEngine;
using UnityEngine.UI;

namespace Defend.Unity
{
    /// <summary>独立数字锚定命中位置；摄像机移动时重新投影，不追踪敌人对象。</summary>
    public sealed class DamageNumberView : MonoBehaviour
    {
        [SerializeField] private Text label;
        [SerializeField] private CanvasGroup visibility;
        [SerializeField] private float lifetimeSeconds = .65f, minimumHeightPixels = 18, maximumHeightPixels = 27;
        private Vector3 anchor, side;
        private Camera worldCamera;
        private float age;
        public bool Finished => age >= lifetimeSeconds;
        public void Configure(Text text, CanvasGroup group) { label = text; visibility = group; }
        public void Begin(int amount, Vector3 position, long sequence, Camera camera)
        {
            worldCamera = camera; age = 0;
            float offset = ((sequence % 7) - 3) * .065f;
            side = camera == null ? Vector3.right : camera.transform.right;
            anchor = position + Vector3.up * .32f + side * (.22f + offset);
            side *= ((sequence % 3) - 1) * .16f;
            label.text = amount.ToString(); visibility.alpha = 1; Render();
        }
        public void AdvanceFeedback(float seconds) { age += Mathf.Max(0, seconds); Render(); }
        private void Render()
        {
            float progress = Mathf.Clamp01(age / Mathf.Max(.001f, lifetimeSeconds));
            transform.position = anchor + Vector3.up * (.7f * progress) + side * progress;
            visibility.alpha = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.35f, 1, progress));
            if (worldCamera == null) return;
            transform.rotation = worldCamera.transform.rotation;
            float unitsPerPixel = worldCamera.orthographic ? 2 * worldCamera.orthographicSize / worldCamera.pixelHeight :
                2 * Mathf.Max(.1f, Vector3.Dot(transform.position - worldCamera.transform.position, worldCamera.transform.forward)) * Mathf.Tan(worldCamera.fieldOfView * .5f * Mathf.Deg2Rad) / worldCamera.pixelHeight;
            float pixels = Mathf.Clamp(.28f / Mathf.Max(.00001f, unitsPerPixel), minimumHeightPixels, maximumHeightPixels);
            float pop = 1 + .13f * Mathf.Sin(Mathf.Clamp01(progress / .22f) * Mathf.PI);
            transform.localScale = Vector3.one * (pixels * unitsPerPixel / 40 * pop);
        }
    }
}
