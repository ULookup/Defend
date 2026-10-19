using UnityEngine;

namespace Defend.Unity
{
    /// <summary>从静态挂点计算局内仪表的朝向与透视尺寸，设计偏移跟随仪表等比缩放。</summary>
    internal sealed class WorldStatusBarLayout
    {
        private Camera worldCamera;
        private Vector3 localAnchor;
        private bool hasAnchor;

        public void Apply(Transform target, float worldWidth, float minimumPixels, float maximumPixels, float verticalDesignUnits)
        {
            if (!hasAnchor) { localAnchor = target.localPosition; hasAnchor = true; }
            if (worldCamera == null) worldCamera = Camera.main;
            if (worldCamera == null || worldCamera.pixelHeight <= 0) return;
            Transform parent = target.parent;
            Vector3 anchor = parent == null ? localAnchor : parent.TransformPoint(localAnchor);
            float unitsPerPixel = worldCamera.orthographic ? 2 * worldCamera.orthographicSize / worldCamera.pixelHeight :
                2 * Mathf.Max(.1f, Vector3.Dot(anchor - worldCamera.transform.position, worldCamera.transform.forward)) *
                Mathf.Tan(worldCamera.fieldOfView * .5f * Mathf.Deg2Rad) / worldCamera.pixelHeight;
            unitsPerPixel = Mathf.Max(.00001f, unitsPerPixel);
            // 上下限以 1080 高度为参考；中间范围保留透视缩放，拉近镜头时仪表自然增大。
            float resolutionScale = worldCamera.pixelHeight / 1080f;
            float widthPixels = Mathf.Clamp(worldWidth / unitsPerPixel, minimumPixels * resolutionScale, maximumPixels * resolutionScale);
            float scale = widthPixels * unitsPerPixel / 100f;
            // 100 设计单位对应条宽，条间距与底部留白随同一比例放大，避免近景重叠。
            target.SetPositionAndRotation(anchor + worldCamera.transform.up * (verticalDesignUnits * scale), worldCamera.transform.rotation);
            Vector3 parentScale = parent == null ? Vector3.one : parent.lossyScale;
            target.localScale = new Vector3(scale / Mathf.Max(.00001f, Mathf.Abs(parentScale.x)),
                scale / Mathf.Max(.00001f, Mathf.Abs(parentScale.y)), scale / Mathf.Max(.00001f, Mathf.Abs(parentScale.z)));
        }
    }
}
