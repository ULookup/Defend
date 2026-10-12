using UnityEngine;
using UnityEngine.InputSystem;

namespace Defend.Unity
{
    /// <summary>把世界摄像机限制在编辑器预留的地图区域，保持窄屏上的按钮与网格互不遮挡。</summary>
    public sealed class WorldCameraViewport : MonoBehaviour
    {
        [SerializeField] private Camera worldCamera;
        [SerializeField] private RectTransform mapViewport;
        [SerializeField, Min(1)] private float mapExtent = 42;
        [SerializeField, Range(0.28f, 1.4f)] private float zoomFactor = 0.65f;
        [SerializeField, Range(0.05f, 0.5f)] private float zoomSensitivity = 0.22f;
        private readonly Vector3[] corners = new Vector3[4];
        private Vector2 previousPointerPosition;
        private bool dragging;
        public bool InputBlocked { get; set; }
        private float previousPinch;
        public void Focus(float x, float z) { worldCamera.transform.position += new Vector3(x, 0, z) - GroundCenter(); ClampCameraCenter(); }
        private readonly Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
        public void Configure(Camera camera, RectTransform viewport) { worldCamera = camera; mapViewport = viewport; }

        private void LateUpdate()
        {
            if (worldCamera == null || mapViewport == null || Screen.width <= 0 || Screen.height <= 0) return;
            mapViewport.GetWorldCorners(corners);
            float width = corners[2].x - corners[0].x;
            float height = corners[2].y - corners[0].y;
            if (width <= 0 || height <= 0) return;
            worldCamera.rect = new Rect(corners[0].x / Screen.width, corners[0].y / Screen.height,
                width / Screen.width, height / Screen.height);
            ApplyZoom(width, height);
            ProcessCameraInput(width, height);
        }

        private void ApplyZoom(float width, float height)
        {
            float halfHeight = Mathf.Max(mapExtent * 0.5f, mapExtent * 0.5f * height / width) * zoomFactor;
            if (worldCamera.orthographic)
            {
                worldCamera.orthographicSize = halfHeight;
                return;
            }

            // 保持地面焦点和视角，以沿视线移动的方式缩放透视镜头。
            Vector3 focus = GroundCenter();
            float distance = halfHeight / Mathf.Tan(worldCamera.fieldOfView * Mathf.Deg2Rad * 0.5f);
            worldCamera.transform.position = focus - worldCamera.transform.forward * distance;
        }

        private void ProcessCameraInput(float width, float height)
        {
            if (InputBlocked) { dragging = false; previousPinch = 0; return; }
            var screen = Touchscreen.current;
            if (screen != null && screen.primaryTouch.press.isPressed)
            {
                Vector2 touchPosition = screen.primaryTouch.position.ReadValue();
                if (!worldCamera.pixelRect.Contains(touchPosition)) { dragging = false; return; }
                int fingers = 0; Vector2 first = default, second = default;
                foreach (var touch in screen.touches) if (touch.press.isPressed) { if (fingers == 0) first = touch.position.ReadValue(); else if (fingers == 1) second = touch.position.ReadValue(); fingers++; }
                if (fingers >= 2) { float pinch = Vector2.Distance(first, second); if (previousPinch > 1 && pinch > 1) zoomFactor = Mathf.Clamp(zoomFactor * previousPinch / pinch, .28f, 1.4f); previousPinch = pinch; dragging = false; ApplyZoom(width, height); return; }
                previousPinch = 0;
                if (!dragging) { previousPointerPosition = touchPosition; dragging = true; }
                worldCamera.transform.position += GroundPoint(previousPointerPosition) - GroundPoint(touchPosition);
                ClampCameraCenter(); previousPointerPosition = touchPosition; return;
            }
            previousPinch = 0;
            Mouse mouse = Mouse.current;
            if (mouse == null) return;
            Vector2 position = mouse.position.ReadValue();
            if (!worldCamera.pixelRect.Contains(position)) { dragging = false; return; }

            float scroll = mouse.scroll.ReadValue().y;
            // 新版 Input System 默认将每格滚轮归一化为 1；仅在 Windows 保留原始输入时换算 120 单位。
            if (InputSystem.settings.scrollDeltaBehavior == InputSettings.ScrollDeltaBehavior.KeepPlatformSpecificInputRange &&
                (Application.platform == RuntimePlatform.WindowsEditor || Application.platform == RuntimePlatform.WindowsPlayer))
                scroll /= 120f;
            if (Mathf.Abs(scroll) > 0.001f)
            {
                // 缩放保持鼠标指向的地面位置，玩家可精确选择一格，而无需反复把目标拖回中心。
                Vector3 before = GroundPoint(position);
                zoomFactor = Mathf.Clamp(zoomFactor * Mathf.Exp(-scroll * zoomSensitivity), 0.28f, 1.4f);
                ApplyZoom(width, height);
                Vector3 after = GroundPoint(position);
                worldCamera.transform.position += before - after;
                ClampCameraCenter();
            }
            if (mouse.middleButton.wasPressedThisFrame)
            {
                dragging = true;
                previousPointerPosition = position;
            }
            if (!mouse.middleButton.isPressed) dragging = false;
            if (!dragging) return;
            // 拖动只改变摄像机；不修改建筑位置、能源状态或当前放置预览的逻辑命令。
            Vector3 previousGround = GroundPoint(previousPointerPosition);
            Vector3 currentGround = GroundPoint(position);
            worldCamera.transform.position += previousGround - currentGround;
            ClampCameraCenter();
            previousPointerPosition = position;
        }

        private Vector3 GroundPoint(Vector2 screenPosition)
        {
            Ray ray = worldCamera.ScreenPointToRay(screenPosition);
            return groundPlane.Raycast(ray, out float distance) ? ray.GetPoint(distance) : worldCamera.transform.position;
        }

        private void ClampCameraCenter()
        {
            // 斜俯视时摄像机自身位于地图南侧。限位必须作用于镜头中心的地面焦点。
            Vector3 focus = GroundCenter();
            Vector3 clamped = new Vector3(Mathf.Clamp(focus.x, 0, mapExtent - 2), 0,
                Mathf.Clamp(focus.z, 0, mapExtent - 2));
            worldCamera.transform.position += clamped - focus;
        }

        private Vector3 GroundCenter()
        {
            // 局部视口边界可能落在小数像素上，投影 API 的像素取整会让中心射线偏离光轴。
            Ray ray = new Ray(worldCamera.transform.position, worldCamera.transform.forward);
            return groundPlane.Raycast(ray, out float distance) ? ray.GetPoint(distance) : worldCamera.transform.position;
        }
    }
}
