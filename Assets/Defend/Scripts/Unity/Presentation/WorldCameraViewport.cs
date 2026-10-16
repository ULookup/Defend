using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Defend.Unity
{
    /// <summary>以地面焦点驱动地图镜头；由会话在放置与选择射线之前更新。</summary>
    public sealed class WorldCameraViewport : MonoBehaviour
    {
        [SerializeField] private Camera worldCamera;
        [SerializeField] private RectTransform mapViewport;
        [SerializeField, Min(1)] private float mapExtent = 42;
        // 兼容原有正交关卡保存的缩放值。
        [SerializeField, Range(0.28f, 1.4f)] private float zoomFactor = 0.65f;
        [SerializeField, Range(0.05f, 0.5f)] private float zoomSensitivity = 0.22f;
        [SerializeField, Min(1)] private float nearestDistance = 7;
        [SerializeField, Min(1)] private float defaultDistance = 21;
        [SerializeField, Min(1)] private float farthestDistance = 42;
        [SerializeField, Range(20, 60)] private float nearestPitch = 32;
        [SerializeField, Range(30, 75)] private float defaultPitch = 53;
        [SerializeField, Range(45, 85)] private float farthestPitch = 68;
        [SerializeField, Range(30, 75)] private float perspectiveFieldOfView = 50;
        [SerializeField, Min(1)] private float rotationSpeed = 60;
        [SerializeField, Min(0.1f)] private float movementSpeedPerDistance = 0.65f;
        [SerializeField, Min(1)] private float responseSpeed = 12;
        private readonly Vector3[] corners = new Vector3[4];
        private readonly Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
        private readonly List<RaycastResult> interfaceHits = new List<RaycastResult>();
        private PointerEventData pointerEvent;
        private EventSystem pointerEventSystem;
        private Vector2 previousPointerPosition, zoomAnchor;
        private Vector3 focus, defaultFocus, followPosition;
        private float mapWidth, mapDepth, distance, targetDistance, yaw, targetYaw, defaultYaw;
        private float targetZoomFactor, defaultZoomFactor, previousPinch, viewportWidth, viewportHeight;
        private bool initialized, configuredMap, dragging, anchoredZoom;
        private int processedFrame = -1;
        public bool InputBlocked { get; set; }
        public bool IsFollowing { get; private set; }

        public void Configure(Camera camera, RectTransform viewport)
        {
            StopFollowing();
            worldCamera = camera;
            mapViewport = viewport;
            initialized = false;
        }

        /// <summary>绑定新世界时传入实际地图尺寸与基地中心，同时恢复该世界的默认视角。</summary>
        public void ConfigureMap(float width, float depth, Vector2 baseCenter)
        {
            StopFollowing();
            mapWidth = Mathf.Max(1, width);
            mapDepth = Mathf.Max(1, depth);
            defaultFocus = new Vector3(baseCenter.x, 0, baseCenter.y);
            configuredMap = true;
            if (worldCamera == null) return;
            EnsureInitialized();
            RestoreDefaultView();
        }

        public void Focus(float x, float z)
        {
            StopFollowing();
            if (worldCamera == null) return;
            EnsureInitialized();
            focus = new Vector3(x, 0, z);
            anchoredZoom = false;
            dragging = false;
            ClampFocus();
            ApplyView();
        }

        public void BeginFollow(Vector3 position)
        {
            if (worldCamera == null) return;
            EnsureInitialized();
            IsFollowing = true;
            followPosition = GroundFocus(position);
            dragging = anchoredZoom = false;
            previousPinch = 0;
            targetDistance = Mathf.Clamp(defaultDistance, nearestDistance, MaximumDistance());
            if (worldCamera.orthographic) targetZoomFactor = defaultZoomFactor;
        }

        public void StopFollowing()
        {
            if (!IsFollowing) return;
            IsFollowing = false;
            followPosition = focus;
            // 手动接管或目标失效后，不再补完跟随期间的平移、缩放或转向。
            targetDistance = distance;
            targetZoomFactor = zoomFactor;
            targetYaw = yaw;
            anchoredZoom = false;
        }

        /// <summary>在实体表现刷新后跟随画面中的地面位置，不再次处理本帧输入。</summary>
        public void UpdateFollow(Vector3 position, bool blocked, bool typing)
        {
            if (!IsFollowing || worldCamera == null || !isActiveAndEnabled) return;
            followPosition = GroundFocus(position);
            if (blocked || typing || !Application.isFocused) return;
            float blend = 1 - Mathf.Exp(-responseSpeed * Mathf.Min(Time.unscaledDeltaTime, .1f));
            focus = Vector3.Lerp(focus, followPosition, blend);
            ClampFocus();
            ApplyView();
        }

        private void EnsureInitialized()
        {
            if (initialized) return;
            if (!configuredMap)
            {
                mapWidth = mapDepth = mapExtent;
                defaultFocus = GroundCenter();
            }
            defaultYaw = worldCamera.transform.eulerAngles.y;
            focus = defaultFocus;
            yaw = targetYaw = defaultYaw;
            distance = targetDistance = Mathf.Max(nearestDistance, defaultDistance);
            defaultZoomFactor = targetZoomFactor = zoomFactor;
            initialized = true;
            ClampFocus();
        }

        private void LateUpdate()
        {
            // 旧关卡没有会话调用时仍可工作；正式会话已处理的帧不再移动镜头。
            ProcessFrame(InputBlocked, GridInputController.IsTextInputSelected(EventSystem.current));
        }

        public void ProcessFrame(bool blocked, bool typing)
        {
            if (processedFrame == Time.frameCount || worldCamera == null || Screen.width <= 0 || Screen.height <= 0) return;
            processedFrame = Time.frameCount;
            if (!UpdateViewport()) return;
            EnsureInitialized();
            float maximum = MaximumDistance();
            targetDistance = Mathf.Clamp(targetDistance, nearestDistance, maximum);
            distance = Mathf.Clamp(distance, nearestDistance, maximum);
            if (blocked || typing || !Application.isFocused)
            {
                StopInput(IsFollowing);
                return;
            }
            ApplyView();

            float deltaTime = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.homeKey.wasPressedThisFrame)
            {
                RestoreDefaultView();
                return;
            }
            Vector2 move = keyboard == null ? Vector2.zero : new Vector2(
                (keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0),
                (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0));
            if (move.sqrMagnitude > 0) StopFollowing();
            float rotation = keyboard == null ? 0 : (keyboard.eKey.isPressed ? 1 : 0) - (keyboard.qKey.isPressed ? 1 : 0);
            targetYaw = Mathf.Repeat(targetYaw + rotation * rotationSpeed * deltaTime, 360);
            if (rotation != 0) anchoredZoom = false;

            ProcessPointerInput();
            Vector3 anchorBefore = default;
            bool preserveAnchor = anchoredZoom && TryGroundPoint(zoomAnchor, out anchorBefore);
            float blend = 1 - Mathf.Exp(-responseSpeed * deltaTime);
            distance = Mathf.Lerp(distance, targetDistance, blend);
            zoomFactor = Mathf.Lerp(zoomFactor, targetZoomFactor, blend);
            yaw = Mathf.LerpAngle(yaw, targetYaw, blend);
            ApplyView();
            if (preserveAnchor && TryGroundPoint(zoomAnchor, out Vector3 anchorAfter))
            {
                focus += anchorBefore - anchorAfter;
                ClampFocus();
                ApplyView();
            }
            if (Mathf.Abs(distance - targetDistance) < 0.001f && Mathf.Abs(zoomFactor - targetZoomFactor) < 0.0001f)
                anchoredZoom = false;

            if (move.sqrMagnitude > 0)
            {
                Quaternion heading = Quaternion.Euler(0, yaw, 0);
                float scale = worldCamera.orthographic ? worldCamera.orthographicSize : distance;
                Vector3 movement = heading * new Vector3(move.normalized.x, 0, move.normalized.y);
                focus += movement * (scale * movementSpeedPerDistance * deltaTime);
                ClampFocus();
                ApplyView();
            }
        }

        private bool UpdateViewport()
        {
            if (mapViewport != null)
            {
                mapViewport.GetWorldCorners(corners);
                viewportWidth = corners[2].x - corners[0].x;
                viewportHeight = corners[2].y - corners[0].y;
                if (viewportWidth <= 0 || viewportHeight <= 0) return false;
                worldCamera.rect = new Rect(corners[0].x / Screen.width, corners[0].y / Screen.height,
                    viewportWidth / Screen.width, viewportHeight / Screen.height);
            }
            else
            {
                viewportWidth = worldCamera.pixelWidth;
                viewportHeight = worldCamera.pixelHeight;
            }
            return viewportWidth > 0 && viewportHeight > 0;
        }

        private float MaximumDistance() => Mathf.Max(defaultDistance, farthestDistance);

        private void ApplyView()
        {
            if (worldCamera.orthographic)
            {
                float aspect = viewportHeight > 0 ? viewportWidth / viewportHeight : Mathf.Max(.1f, worldCamera.aspect);
                worldCamera.orthographicSize = Mathf.Max(mapDepth * .5f, mapWidth * .5f / aspect) * zoomFactor;
                // 保持正交关卡原有俯角，仅添加绕地面焦点旋转。
                worldCamera.transform.rotation = Quaternion.Euler(worldCamera.transform.eulerAngles.x, yaw, 0);
                float height = Mathf.Max(1, worldCamera.transform.position.y);
                float orbitDistance = height / Mathf.Max(.1f, -worldCamera.transform.forward.y);
                worldCamera.transform.position = focus - worldCamera.transform.forward * orbitDistance;
                EnsureFarClip(orbitDistance);
                return;
            }
            float pitch;
            if (distance <= defaultDistance)
            {
                float progress = Mathf.InverseLerp(nearestDistance, defaultDistance, distance);
                pitch = Mathf.Lerp(nearestPitch, defaultPitch, Mathf.SmoothStep(0, 1, progress));
            }
            else
            {
                float progress = Mathf.InverseLerp(defaultDistance, MaximumDistance(), distance);
                pitch = Mathf.Lerp(defaultPitch, farthestPitch, Mathf.SmoothStep(0, 1, progress));
            }
            worldCamera.fieldOfView = perspectiveFieldOfView;
            worldCamera.nearClipPlane = Mathf.Min(worldCamera.nearClipPlane, .1f);
            worldCamera.transform.rotation = Quaternion.Euler(pitch, yaw, 0);
            worldCamera.transform.position = focus - worldCamera.transform.forward * distance;
            EnsureFarClip(distance);
        }

        private void EnsureFarClip(float viewingDistance)
        {
            // 焦点在边缘时最远地图角可相隔整个对角线；窄视口拉远也不能沿用固定裁切距离。
            float mapDiagonal = Mathf.Sqrt(mapWidth * mapWidth + mapDepth * mapDepth);
            worldCamera.farClipPlane = Mathf.Max(worldCamera.farClipPlane, viewingDistance + mapDiagonal + 10);
        }

        private void ProcessPointerInput()
        {
            Touchscreen screen = Touchscreen.current;
            if (screen != null && screen.primaryTouch.press.isPressed)
            {
                Vector2 first = default, second = default;
                int fingers = 0;
                foreach (var touch in screen.touches)
                {
                    if (!touch.press.isPressed) continue;
                    if (fingers == 0) first = touch.position.ReadValue();
                    else if (fingers == 1) second = touch.position.ReadValue();
                    fingers++;
                }
                if (!PointerAllowed(first) || (fingers >= 2 && !PointerAllowed(second)))
                {
                    dragging = false;
                    previousPinch = 0;
                    return;
                }
                if (fingers >= 2)
                {
                    float pinch = Vector2.Distance(first, second);
                    if (previousPinch > 1 && pinch > 1) ChangeZoom(previousPinch / pinch, (first + second) * .5f);
                    previousPinch = pinch;
                    dragging = false;
                }
                else
                {
                    previousPinch = 0;
                    Drag(first, true);
                }
                return;
            }
            previousPinch = 0;
            Mouse mouse = Mouse.current;
            if (mouse == null) { dragging = false; return; }
            Vector2 position = mouse.position.ReadValue();
            if (!PointerAllowed(position)) { dragging = false; return; }
            float scroll = mouse.scroll.ReadValue().y;
            if (InputSystem.settings.scrollDeltaBehavior == InputSettings.ScrollDeltaBehavior.KeepPlatformSpecificInputRange &&
                (Application.platform == RuntimePlatform.WindowsEditor || Application.platform == RuntimePlatform.WindowsPlayer))
                scroll /= 120f;
            if (Mathf.Abs(scroll) > .001f) ChangeZoom(Mathf.Exp(-scroll * zoomSensitivity), position);
            Drag(position, mouse.middleButton.isPressed);
        }

        private void ChangeZoom(float multiplier, Vector2 anchor)
        {
            targetDistance = Mathf.Clamp(targetDistance * multiplier, nearestDistance, MaximumDistance());
            targetZoomFactor = Mathf.Clamp(targetZoomFactor * multiplier, .28f, 1.4f);
            zoomAnchor = anchor;
            anchoredZoom = !IsFollowing;
        }

        private void Drag(Vector2 position, bool pressed)
        {
            if (!pressed) { dragging = false; return; }
            if (dragging && (position - previousPointerPosition).sqrMagnitude > .0001f &&
                TryGroundPoint(previousPointerPosition, out Vector3 previous) && TryGroundPoint(position, out Vector3 current))
            {
                StopFollowing();
                focus += previous - current;
                anchoredZoom = false;
                ClampFocus();
                ApplyView();
            }
            previousPointerPosition = position;
            dragging = true;
        }

        private bool PointerAllowed(Vector2 position)
        {
            if (!worldCamera.pixelRect.Contains(position)) return false;
            EventSystem events = EventSystem.current;
            if (events == null) return true;
            if (pointerEvent == null || pointerEventSystem != events)
            {
                pointerEvent = new PointerEventData(events);
                pointerEventSystem = events;
            }
            pointerEvent.position = position;
            interfaceHits.Clear();
            events.RaycastAll(pointerEvent, interfaceHits);
            return interfaceHits.Count == 0;
        }

        private void RestoreDefaultView()
        {
            StopFollowing();
            focus = defaultFocus;
            yaw = targetYaw = defaultYaw;
            distance = targetDistance = Mathf.Max(nearestDistance, defaultDistance);
            if (worldCamera.orthographic) zoomFactor = targetZoomFactor = defaultZoomFactor;
            StopInput();
            ClampFocus();
            ApplyView();
        }

        private void StopInput(bool preserveFollowTargets = false)
        {
            dragging = anchoredZoom = false;
            previousPinch = 0;
            if (preserveFollowTargets) return;
            targetDistance = distance;
            targetZoomFactor = zoomFactor;
            targetYaw = yaw;
        }

        private void OnApplicationFocus(bool hasFocus) { if (!hasFocus) StopInput(IsFollowing); }
        private void OnDisable() { StopFollowing(); StopInput(); }

        private void OnValidate()
        {
            nearestDistance = Mathf.Max(1, nearestDistance);
            defaultDistance = Mathf.Max(nearestDistance + .1f, defaultDistance);
            farthestDistance = Mathf.Max(defaultDistance, farthestDistance);
            defaultPitch = Mathf.Max(nearestPitch, defaultPitch);
            farthestPitch = Mathf.Max(defaultPitch, farthestPitch);
        }

        private bool TryGroundPoint(Vector2 screenPosition, out Vector3 point)
        {
            Ray ray = worldCamera.ScreenPointToRay(screenPosition);
            if (groundPlane.Raycast(ray, out float length)) { point = ray.GetPoint(length); return true; }
            point = focus;
            return false;
        }

        private void ClampFocus()
        {
            focus = GroundFocus(focus);
        }

        private Vector3 GroundFocus(Vector3 position) =>
            new Vector3(Mathf.Clamp(position.x, 0, mapWidth), 0, Mathf.Clamp(position.z, 0, mapDepth));

        private Vector3 GroundCenter()
        {
            Ray ray = new Ray(worldCamera.transform.position, worldCamera.transform.forward);
            return groundPlane.Raycast(ray, out float length) ? ray.GetPoint(length) : new Vector3(mapExtent * .5f, 0, mapExtent * .5f);
        }
    }
}
