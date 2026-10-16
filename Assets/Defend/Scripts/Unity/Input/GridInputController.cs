using System.Collections.Generic;
using Defend.Simulation;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Defend.Unity
{
    /// <summary>白盒鼠标输入适配。预览只改变表现，确认后才把命令交给模拟验证。</summary>
    public sealed class GridInputController : MonoBehaviour
    {
        [SerializeField] private Camera worldCamera;
        [SerializeField] private EventSystem eventSystem;
        private GameSessionRoot session;
        private WorldPresentation presentation;
        private BuildingKind? placingKind;
        private long movingBuildingId;
        private bool editingSpawn;
        private long movingSpawnId;
        private System.Action spawnEditFinished;
        private BuildingDefinition previewDefinition;
        private readonly Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
        private static readonly BuildingKind[] ShortcutBuildings = { BuildingKind.Reactor, BuildingKind.Relay, BuildingKind.Battery, BuildingKind.Turret, BuildingKind.BurstTurret, BuildingKind.AreaTurret, BuildingKind.PiercingRailgun, BuildingKind.ResonanceMagneticTower };
        private static readonly Key[] NumberKeys = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8 };
        private static readonly Key[] KeypadKeys = { Key.Numpad1, Key.Numpad2, Key.Numpad3, Key.Numpad4, Key.Numpad5, Key.Numpad6, Key.Numpad7, Key.Numpad8 };
        private readonly List<RaycastResult> interfaceHits = new List<RaycastResult>();
        private PointerEventData pointerEvent;
        private Vector2 pressPosition;
        private bool pointerDragged;
        private bool worldPressAuthorized;
        private const double DoubleClickSeconds = .3;
        private GameWorld lastClickWorld;
        private long lastClickedEntityId;
        private bool lastClickedEnemy;
        private double lastClickTime;
        public bool HasPlacement => editingSpawn || placingKind.HasValue || movingBuildingId != 0;
        public bool HasVisiblePlacementPreview { get; private set; }
        public bool PreviewValid { get; private set; }
        public long MovingBuildingId => movingBuildingId;
        public BuildingDefinition PreviewDefinition => previewDefinition;
        public int PreviewX { get; private set; }
        public int PreviewZ { get; private set; }
        public int PreviewOrientationQuarterTurns { get; private set; }
        public int PreviewFacingDegrees { get; private set; }
        public int PreviewWidth => previewDefinition?.GetFootprintWidth(PreviewOrientationQuarterTurns) ?? 1;
        public int PreviewDepth => previewDefinition?.GetFootprintDepth(PreviewOrientationQuarterTurns) ?? 1;
        public string ModeDescription { get; private set; } = "浏览 / 选择";
        public Camera WorldCamera => worldCamera;
        public bool IsTypingText => IsTextInputSelected(eventSystem);
        public static bool IsTextInputSelected(EventSystem events)
        {
            GameObject selected = events != null ? events.currentSelectedGameObject : null;
            return selected != null && selected.GetComponentInParent<UnityEngine.UI.InputField>() != null;
        }
        public void Configure(Camera camera, EventSystem events) { worldCamera = camera; eventSystem = events; }
        public void Initialize(GameSessionRoot root, WorldPresentation views)
        {
            session = root;
            presentation = views;
            pointerEvent = new PointerEventData(eventSystem);
            worldPressAuthorized = false;
            ResetDoubleClick();
        }

        public void BeginPlacement(BuildingKind kind)
        {
            if (session.BlocksWorldInput || !session.World.CanEditBuildings) return;
            var definition = session.FindEffectiveDefinition(kind);
            if (!session.IsSandbox && session.World.Credits < definition.PurchaseCost)
            {
                session.ShowFeedback("资金不足，建造" + definition.DisplayName + "还需 " + (definition.PurchaseCost - session.World.Credits) + " 资金。");
                session.PlayInterfaceSound(DefenseSoundCue.UiDenied);
                return;
            }
            int orientation = placingKind == kind ? PreviewOrientationQuarterTurns : 0;
            int facing = placingKind == kind ? PreviewFacingDegrees : 0;
            Cancel();
            if (session.BlocksWorldInput) return;
            session.StopCameraFollow();
            placingKind = kind;
            movingBuildingId = 0;
            previewDefinition = session.FindEffectiveDefinition(kind);
            PreviewOrientationQuarterTurns = orientation;
            PreviewFacingDegrees = facing;
            HasVisiblePlacementPreview = false;
            presentation.PlacementPreviewActive = true;
            ModeDescription = "放置 " + session.FindDefinition(kind).DisplayName;
            session.PlayInterfaceSound(DefenseSoundCue.Selection);
            session.ShowFeedback("左键确认 · R 顺时针旋转 90° · 右键或 Esc 取消。");
        }

        public void BeginMove()
        {
            BuildingState building = session.SelectedBuilding;
            if (session.BlocksWorldInput || !session.World.CanEditBuildings || building == null || building.IsSold || !building.IsAlive) return;
            if (session.Battle != null && building.Kind == BuildingKind.Core) { session.ShowFeedback("核心不能移动。"); session.PlayInterfaceSound(DefenseSoundCue.UiDenied); return; }
            Cancel();
            if (session.BlocksWorldInput) return;
            session.StopCameraFollow();
            placingKind = null;
            movingBuildingId = building.Id;
            previewDefinition = building.Definition;
            PreviewOrientationQuarterTurns = building.OrientationQuarterTurns;
            PreviewFacingDegrees = building.FacingDegrees;
            HasVisiblePlacementPreview = false;
            presentation.PlacementPreviewActive = true;
            ModeDescription = "移动 " + building.Definition.DisplayName;
            session.PlayInterfaceSound(DefenseSoundCue.Selection);
            long atRisk = 0;
            foreach (EnergyTransferSnapshot transfer in session.World.Energy.Transfers)
                if (transfer.TargetId == building.Id) atRisk += transfer.Amount;
            session.ShowFeedback("实际移动后重算连接；发往旧位置的在途能量可能损失 " + (atRisk / 1000.0).ToString("0.###") + " 点。R 顺时针旋转 90°；取消保留原位置与原朝向。");
        }

        public void BeginSandboxSpawn(long id, System.Action completed)
        {
            if (!session.IsSandbox || !session.World.CanEditBuildings) return;
            Cancel(); session.StopCameraFollow(); editingSpawn = true; movingSpawnId = id; spawnEditFinished = completed;
            ModeDescription = id == 0 ? "添加出生点" : "移动出生点";
            session.PlayInterfaceSound(DefenseSoundCue.Selection);
            session.ShowFeedback("单击合法网格确认出生点；右键或 Esc 取消。出生点可以在地图内部。允许被建筑包围，敌人将沿用拆除行为。");
        }

        public void Cancel() { Cancel(false); }
        public void Cancel(bool notifyPlayer)
        {
            bool hadPlacement = HasPlacement;
            worldPressAuthorized = false;
            ResetDoubleClick();
            var completed = spawnEditFinished; spawnEditFinished = null; editingSpawn = false; movingSpawnId = 0;
            placingKind = null;
            movingBuildingId = 0;
            previewDefinition = null;
            PreviewOrientationQuarterTurns = 0;
            PreviewFacingDegrees = 0;
            PreviewValid = false;
            HasVisiblePlacementPreview = false;
            presentation.PlacementPreviewActive = false;
            ModeDescription = "浏览 / 选择";
            presentation.SetPreview(false, 0, 0, 1, 1, false);
            if (notifyPlayer && hadPlacement)
            { session.ShowFeedback("已取消放置。请选择建筑或开始波次。"); session.PlayInterfaceSound(DefenseSoundCue.UiBack); }
            completed?.Invoke();
        }

        public void ProcessShortcuts(bool typing)
        {
            if (!Application.isFocused || typing) return;
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (session.ConsumePageShortcut(keyboard.escapeKey.wasPressedThisFrame)) return;
                if (keyboard.escapeKey.wasPressedThisFrame) { if (HasPlacement) Cancel(true); else session.TogglePause(); }
                else if (keyboard.spaceKey.wasPressedThisFrame) session.TogglePause();
                else if (!session.BlocksWorldInput && session.World.CanEditBuildings)
                    for (int i = 0; i < ShortcutBuildings.Length; i++)
                        if (keyboard[NumberKeys[i]].wasPressedThisFrame || keyboard[KeypadKeys[i]].wasPressedThisFrame)
                        { BeginPlacement(ShortcutBuildings[i]); break; }
            }
        }

        public void ProcessInput()
        {
            HasVisiblePlacementPreview = false;
            if (!Application.isFocused || session.BlocksWorldInput || IsTypingText)
            {
                worldPressAuthorized = false;
                ResetDoubleClick();
                presentation.SetPreview(false, 0, 0, 1, 1, false);
                return;
            }
            if (HasPlacement && !session.World.CanEditBuildings) Cancel();
            Mouse mouse = Mouse.current;
            var touch = Touchscreen.current?.primaryTouch;
            bool usingTouch = touch != null && (touch.press.isPressed || touch.press.wasReleasedThisFrame);
            if (mouse == null && !usingTouch)
            { worldPressAuthorized = false; ResetDoubleClick(); presentation.SetPreview(false, 0, 0, 1, 1, false); return; }
            if (usingTouch || HasPlacement || lastClickWorld != session.World ||
                Time.unscaledTimeAsDouble - lastClickTime > DoubleClickSeconds || HasCameraNavigationInput(mouse))
                ResetDoubleClick();
            if (mouse != null && mouse.rightButton.wasPressedThisFrame) Cancel(true);
            Vector2 pointerPosition = usingTouch ? touch.position.ReadValue() : mouse.position.ReadValue();
            bool pressed = usingTouch ? touch.press.wasPressedThisFrame : mouse.leftButton.wasPressedThisFrame;
            bool released = usingTouch ? touch.press.wasReleasedThisFrame : mouse.leftButton.wasReleasedThisFrame;
            if (pressed) { pressPosition = pointerPosition; pointerDragged = false; worldPressAuthorized = false; }
            if ((pointerPosition - pressPosition).sqrMagnitude > 144) pointerDragged = true;
            if (usingTouch) { int touches = 0; foreach (var finger in Touchscreen.current.touches) if (finger.press.isPressed) touches++; if (touches > 1) pointerDragged = true; }
            if (pointerDragged && (usingTouch || mouse.leftButton.isPressed || released)) ResetDoubleClick();
            // 显式射线检测不依赖 EventSystem.Update 的先后顺序，避免刚移到按钮上的点击穿透。
            pointerEvent.position = pointerPosition;
            interfaceHits.Clear();
            eventSystem.RaycastAll(pointerEvent, interfaceHits);
            if (interfaceHits.Count > 0 || !worldCamera.pixelRect.Contains(pointerPosition))
            {
                worldPressAuthorized = false;
                ResetDoubleClick();
                presentation.SetPreview(false, 0, 0, 1, 1, false);
                return;
            }
            if (pressed) worldPressAuthorized = true;
            // 先验证按下所在区域，再消费释放；低帧率时按下和释放可能落在同一更新帧。
            bool clicked = released && !pointerDragged && worldPressAuthorized;
            if (released) worldPressAuthorized = false;
            Ray ray = worldCamera.ScreenPointToRay(pointerPosition);
            if (clicked && !HasPlacement && TrySelectEntity(ray, pointerPosition, usingTouch)) return;
            if (!groundPlane.Raycast(ray, out float distance))
            {
                if (clicked && !HasPlacement && !usingTouch) HandleEntityClick(null, null, false);
                presentation.SetPreview(false, 0, 0, 1, 1, false); return;
            }
            Vector3 point = ray.GetPoint(distance);
            int x = Mathf.FloorToInt(point.x);
            int z = Mathf.FloorToInt(point.z);
            bool inside = x >= 0 && z >= 0 && x < session.World.Settings.Width && z < session.World.Settings.Depth;
            if (editingSpawn)
            {
                string issue = session.Battle.ValidateSandboxSpawnPoint(x, z, movingSpawnId);
                bool valid = inside && issue == null;
                presentation.SetPreview(true, x, z, 1, 1, valid);
                ModeDescription = (movingSpawnId == 0 ? "添加出生点" : "移动出生点") + (valid ? " · 可放置" : " · " + (issue ?? "地图外不能放置"));
                if (clicked && valid)
                {
                    string error;
                    bool success = movingSpawnId == 0 ? session.Battle.AddSandboxSpawnPoint(x, z, out error) : session.Battle.MoveSandboxSpawnPoint(movingSpawnId, x, z, out error);
                    if (success) { Cancel(); session.ShowFeedback("出生点已更新。允许被建筑包围，敌人沿用绕路与拆除行为。"); session.PlayInterfaceSound(DefenseSoundCue.UiConfirm); }
                    else { session.ShowFeedback(error); session.PlayInterfaceSound(DefenseSoundCue.UiDenied); }
                }
            }
            else if (HasPlacement)
            {
                BuildingState moving = movingBuildingId == 0 ? null : session.World.FindBuilding(movingBuildingId);
                if (movingBuildingId != 0 && (moving == null || moving.IsSold || !moving.IsAlive)) { Cancel(); return; }
                BuildingDefinition definition = session.FindEffectiveDefinition(placingKind ?? moving.Kind);
                previewDefinition = definition;
                if (Keyboard.current?.rKey.wasPressedThisFrame == true)
                {
                    PreviewOrientationQuarterTurns = (PreviewOrientationQuarterTurns + 1) % 4;
                    PreviewFacingDegrees = (PreviewFacingDegrees + 90) % 360;
                    session.PlayInterfaceSound(DefenseSoundCue.Selection);
                }
                // 各种占地都以中心吸附；偶数格建筑中心落在网格交点，单格仍落在格子中心。
                x = Mathf.FloorToInt(point.x - PreviewWidth * .5f + .5f);
                z = Mathf.FloorToInt(point.z - PreviewDepth * .5f + .5f);
                string issue = session.World.PlacementIssue(definition.Kind, x, z, movingBuildingId, PreviewOrientationQuarterTurns);
                bool valid = inside && issue == null;
                PreviewValid = valid;
                PreviewX = x;
                PreviewZ = z;
                HasVisiblePlacementPreview = inside;
                presentation.SetPreview(inside, x, z, PreviewWidth, PreviewDepth, valid);
                presentation.ShowPreviewRange(session.World, previewDefinition, x, z, inside,
                    PreviewFacingDegrees, PreviewOrientationQuarterTurns);
                ModeDescription = (movingBuildingId != 0 ? "移动：" : "建造：") + definition.DisplayName + " · 朝向 " +
                    PreviewOrientationQuarterTurns * 90 + "°" +
                    (valid ? " · 可放置" : " · " + (issue ?? "地图外不能放置"));
                if (clicked && inside)
                {
                    session.Submit(placingKind.HasValue
                        ? WorldCommand.Place(placingKind.Value, x, z, PreviewOrientationQuarterTurns, PreviewFacingDegrees)
                        : WorldCommand.Move(movingBuildingId, x, z, PreviewOrientationQuarterTurns, PreviewFacingDegrees));
                    // 连续购买保留建筑选择；移动完成后返回选择模式。
                    if (movingBuildingId != 0 && session.World.LastCommandResult.Succeeded) Cancel();
                }
            }
            else if (clicked)
            {
                BuildingState selected = inside ? session.World.FindBuildingAt(x, z) : null;
                HandleEntityClick(selected != null && selected.IsAlive && !selected.IsSold ? selected : null, null, usingTouch);
            }
        }

        private bool TrySelectEntity(Ray ray, Vector2 pointerPosition, bool usingTouch)
        {
            // 按实际碰撞体距离选择最前面的有效实体，不能用其后方地面格覆盖建筑命中。
            BuildingState pickedBuilding = null;
            EnemyState pickedEnemy = null;
            float maximumDistance = groundPlane.Raycast(ray, out float groundDistance) ? groundDistance + .01f : worldCamera.farClipPlane;
            float nearestHit = float.PositiveInfinity;
            foreach (RaycastHit hit in Physics.RaycastAll(ray, maximumDistance))
            {
                if (hit.distance >= nearestHit) continue;
                BuildingView buildingView = hit.collider.GetComponentInParent<BuildingView>();
                BuildingState building = buildingView == null ? null : session.World.FindBuilding(buildingView.BuildingId);
                if (building != null && building.IsAlive && !building.IsSold)
                {
                    nearestHit = hit.distance; pickedBuilding = building; pickedEnemy = null;
                    continue;
                }
                EnemyView enemyView = hit.collider.GetComponentInParent<EnemyView>();
                if (enemyView == null || session.Battle == null) continue;
                foreach (EnemyState enemy in session.Battle.Enemies)
                    if (enemy.Id == enemyView.EnemyId && enemy.IsAlive)
                    {
                        nearestHit = hit.distance; pickedEnemy = enemy; pickedBuilding = null;
                        break;
                    }
            }
            if (pickedBuilding != null) { HandleEntityClick(pickedBuilding, null, usingTouch); return true; }
            if (pickedEnemy == null && usingTouch && session.Battle != null)
            {
                float best = 32 * 32;
                foreach (EnemyState enemy in session.Battle.Enemies)
                {
                    Vector3 screen = worldCamera.WorldToScreenPoint(new Vector3((float)enemy.X, .5f, (float)enemy.Z));
                    float square = ((Vector2)screen - pointerPosition).sqrMagnitude;
                    if (enemy.IsAlive && screen.z > 0 && square < best) { best = square; pickedEnemy = enemy; }
                }
            }
            if (pickedEnemy == null) return false;
            HandleEntityClick(null, pickedEnemy, usingTouch);
            return true;
        }

        private void HandleEntityClick(BuildingState building, EnemyState enemy, bool usingTouch)
        {
            bool isEnemy = enemy != null;
            long identifier = isEnemy ? enemy.Id : building?.Id ?? 0;
            bool followingTarget = identifier != 0 && (isEnemy
                ? session.IsFollowingEnemy(identifier) : session.IsFollowingBuilding(identifier));
            if (!followingTarget) session.StopCameraFollow();
            if (isEnemy) session.SelectEnemy(enemy);
            else session.Select(identifier);

            // 单击立即保留原选择反馈；只有同一世界、同一类型、同一身份的第二次鼠标点击才开始跟随。
            if (usingTouch || identifier == 0) { ResetDoubleClick(); return; }
            double now = Time.unscaledTimeAsDouble;
            bool doubleClicked = lastClickWorld == session.World && lastClickedEntityId == identifier &&
                lastClickedEnemy == isEnemy && now - lastClickTime <= DoubleClickSeconds;
            ResetDoubleClick();
            if (doubleClicked)
            {
                if (isEnemy) session.BeginCameraFollowEnemy(identifier);
                else session.BeginCameraFollowBuilding(identifier);
                return;
            }
            lastClickWorld = session.World;
            lastClickedEntityId = identifier;
            lastClickedEnemy = isEnemy;
            lastClickTime = now;
        }

        private void ResetDoubleClick()
        {
            lastClickWorld = null;
            lastClickedEntityId = 0;
            lastClickedEnemy = false;
            lastClickTime = 0;
        }

        private static bool HasCameraNavigationInput(Mouse mouse)
        {
            Keyboard keyboard = Keyboard.current;
            return (keyboard != null && (keyboard.qKey.isPressed || keyboard.eKey.isPressed ||
                keyboard.wKey.isPressed || keyboard.aKey.isPressed || keyboard.sKey.isPressed || keyboard.dKey.isPressed ||
                keyboard.homeKey.isPressed)) ||
                (mouse != null && (mouse.middleButton.isPressed || Mathf.Abs(mouse.scroll.ReadValue().y) > .001f));
        }

        private void OnApplicationFocus(bool focused)
        { if (!focused) { worldPressAuthorized = false; ResetDoubleClick(); } }
        private void OnDisable() { worldPressAuthorized = false; ResetDoubleClick(); }
    }
}
