using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Defend.PhaseFiveEffects;
using Defend.Simulation;
using Defend.Unity;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// 重制速射炮的限定验证。通过 Unity Pipeline run_script 调用，文件不进入玩家构建。
/// Inspect 只读资产；MotionChecks 使用临时真实预制体；CombatChecks 在工业防线播放会话中运行并重开还原。
/// </summary>
public static class RefinedTurretValidation
{
    private const string ModelPath = "Assets/Defend/Content/Art/RapidTurretRefined/Models/RapidTurretRefined.fbx";
    private const string CanonicalPrefabPath = "Assets/Defend/Content/Art/RapidTurretRefined/Prefabs/RapidTurretRefined.prefab";
    private const string MeshFolder = "Assets/Defend/Content/Art/RapidTurretRefined/Meshes/";
    private const string PrefabFolder = "Assets/Defend/Content/PhaseFiveCombat/Buildings/";
    private const string EvidenceFolder = "Docs/Art/RapidTurretRefined/Checks/Unity/";
    private static readonly string[] MovingNames = { "TurretYaw", "BarrelPitch", "BarrelRecoil", "BarrelSpin" };

    public static object Inspect()
    {
        var checks = new Dictionary<string, bool>();
        var prefab = Require<GameObject>(PrefabFolder + "Turret.prefab");
        var model = Require<GameObject>(ModelPath);
        var canonical = Require<GameObject>(CanonicalPrefabPath);
        var view = prefab.GetComponent<BuildingView>();
        if (view == null || view.VisualRoot == null) throw new Exception("现役速射炮缺少 BuildingView 或模型引用。");
        var visual = view.VisualRoot;
        var anchors = visual.GetComponent<CombatModelAnchors>();
        var motion = visual.GetComponent<RapidTurretMotion>();
        var yaw = Find(visual, "TurretYaw");
        var pitch = Find(visual, "BarrelPitch");
        var recoil = Find(visual, "BarrelRecoil");
        var spin = Find(visual, "BarrelSpin");
        var muzzle = Find(visual, "Muzzle");
        var fixedBase = Find(visual, "FixedBase");
        var filters = visual.GetComponentsInChildren<MeshFilter>(true);
        var renderers = visual.GetComponentsInChildren<Renderer>(true);
        var materials = renderers.SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct().ToArray();
        long triangles = filters.Where(f => f.sharedMesh != null).Sum(f => TriangleCount(f.sharedMesh));
        checks["现役模型引用生产版规范轴向预制体网格"] = UsesProductionMeshes(visual);
        var sourceFilters = model.GetComponentsInChildren<MeshFilter>(true);
        checks["源FBX与规范网格数量和面数一致"] = sourceFilters.Length == filters.Length && sourceFilters.All(f => f.sharedMesh != null) && sourceFilters.Sum(f => TriangleCount(f.sharedMesh)) == triangles;
        checks["速射炮动作组件已绑定现役挂点"] = motion != null && anchors != null && anchors.RapidMotion == motion && anchors.TurretYaw == yaw && anchors.Muzzle == muzzle;
        checks["旋转俯仰后座与自转层级完整"] = yaw != null && pitch != null && recoil != null && spin != null && muzzle != null && pitch.IsChildOf(yaw) && recoil.IsChildOf(pitch) && spin.IsChildOf(recoil) && muzzle.IsChildOf(recoil);
        checks["底座独立于旋转上装"] = fixedBase != null && yaw != null && !fixedBase.IsChildOf(yaw);
        checks["炮口朝向模型正前方"] = muzzle != null && Vector3.Dot(visual.InverseTransformDirection(muzzle.forward).normalized, Vector3.forward) > .995f;
        checks["炮口位于炮管前端"] = muzzle != null && spin != null && Vector3.Dot(muzzle.position - spin.position, muzzle.forward) > .15f;
        checks["无缺失网格材质或脚本"] = filters.All(f => f.sharedMesh != null) && renderers.All(r => r.sharedMaterials.All(m => m != null)) && prefab.GetComponentsInChildren<Transform>(true).Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)) == 0;
        checks["保留选择碰撞体"] = prefab.GetComponent<Collider>() != null;
        checks["网格不超过30000三角面"] = triangles > 0 && triangles <= 30000;
        checks["材质不超过五种且着色器有效"] = materials.Length > 0 && materials.Length <= 5 && materials.All(m => m.shader != null && !ShaderUtil.ShaderHasError(m.shader));
        checks["各子网格都有对应的生产版材质槽"] = renderers.Length > 0 && renderers.All(r =>
            r.GetComponent<MeshFilter>() != null && r.GetComponent<MeshFilter>().sharedMesh != null &&
            r.sharedMaterials.Length == r.GetComponent<MeshFilter>().sharedMesh.subMeshCount &&
            r.sharedMaterials.All(m => m != null && AssetDatabase.GetAssetPath(m).StartsWith("Assets/Defend/Content/Art/RapidTurretRefined/Materials/", StringComparison.Ordinal)));
        bool solidMaterialsValid = materials.Length > 0 && materials.All(m =>
            m.HasProperty("_BaseColor") && m.HasProperty("_Metallic") && m.HasProperty("_Smoothness") &&
            UnitRange(m.GetColor("_BaseColor").r) && UnitRange(m.GetColor("_BaseColor").g) && UnitRange(m.GetColor("_BaseColor").b) &&
            m.GetColor("_BaseColor").a >= .999f && m.GetColor("_BaseColor").a <= 1 &&
            UnitRange(m.GetFloat("_Metallic")) && UnitRange(m.GetFloat("_Smoothness")));
        checks["每种材质的实体色与表面参数有效"] = solidMaterialsValid;
        var textures = materials.Where(m => m.HasProperty("_BaseMap")).Select(m => m.GetTexture("_BaseMap")).Where(t => t != null).Distinct().ToArray();
        checks["纯色材质或有效的颜色纹理"] = textures.Length == 0 ? solidMaterialsValid : textures.All(t => t.width > 0 && t.height > 0 && t.width <= 2048 && t.height <= 2048);
        checks["网格轴缩放统一"] = canonical.transform.localScale == Vector3.one && visual.GetComponentsInChildren<Transform>(true).All(t => Vector3.Distance(t.localScale, Vector3.one) < .001f && Quaternion.Angle(t.localRotation, Quaternion.identity) < .01f);
        var other = new[] { "BurstTurret", "AreaTurret" }.Select(name => Require<GameObject>(PrefabFolder + name + ".prefab")).ToArray();
        checks["其他炮塔未绑定速射炮组件"] = other.All(p => p.GetComponentInChildren<RapidTurretMotion>(true) == null);
        return Finish("AssetInspection", checks, new { triangles, meshCount = filters.Length,
            materialMode = textures.Length == 0 ? "实体色与金属度、光滑度材质" : "带颜色纹理的表面材质",
            materials = materials.Select(m => new { path = AssetDatabase.GetAssetPath(m),
                baseColor = m.HasProperty("_BaseColor") ? new[] { m.GetColor("_BaseColor").r, m.GetColor("_BaseColor").g, m.GetColor("_BaseColor").b, m.GetColor("_BaseColor").a } : null,
                metallic = m.HasProperty("_Metallic") ? m.GetFloat("_Metallic") : -1,
                smoothness = m.HasProperty("_Smoothness") ? m.GetFloat("_Smoothness") : -1 }).ToArray(),
            textures = textures.Select(t => new { path = AssetDatabase.GetAssetPath(t), t.width, t.height }).ToArray() });
    }

    public static object MotionChecks()
    {
        RequirePlayMode();
        var checks = new Dictionary<string, bool>();
        var instance = Object.Instantiate(Require<GameObject>(PrefabFolder + "Turret.prefab"));
        instance.name = "速射炮临时动作验证";
        instance.transform.position = new Vector3(200, 0, 200);
        try
        {
            var view = instance.GetComponent<BuildingView>();
            var motion = view.VisualRoot.GetComponent<RapidTurretMotion>();
            if (motion == null || !motion.IsConfigured) throw new Exception("实际预制体没有正确配置 RapidTurretMotion。");
            var yaw = Find(view.VisualRoot, "TurretYaw");
            var pitch = Find(view.VisualRoot, "BarrelPitch");
            var recoil = Find(view.VisualRoot, "BarrelRecoil");
            var spin = Find(view.VisualRoot, "BarrelSpin");
            var fixedBase = Find(view.VisualRoot, "FixedBase");
            var allMoving = MovingNames.Select(n => Find(view.VisualRoot, n)).ToArray();
            motion.ResetPose();
            var resting = allMoving.Select(t => new Pose(t)).ToArray();
            var basePose = new Pose(fixedBase);
            var baseWorldPosition = fixedBase.position;
            var baseWorldRotation = fixedBase.rotation;

            motion.SetAimTarget(yaw.position + Vector3.right * 10);
            bool targetDoesNotSnap = resting[0].Matches(yaw);
            motion.Advance(.016f);
            float firstYaw = Yaw(yaw);
            Advance(motion, 1.5f);
            checks["转向平滑并最终指向目标"] = targetDoesNotSnap && firstYaw > 0 && firstYaw < 20 && Mathf.Abs(Mathf.DeltaAngle(Yaw(yaw), 90)) < .5f;
            motion.SetAimTarget(yaw.position + Quaternion.Euler(0, 179, 0) * Vector3.forward * 10);
            Advance(motion, 1.5f);
            float beforeWrap = Yaw(yaw);
            motion.SetAimTarget(yaw.position + Quaternion.Euler(0, -179, 0) * Vector3.forward * 10);
            motion.Advance(.016f);
            float wrapStep = Mathf.DeltaAngle(beforeWrap, Yaw(yaw));
            Advance(motion, .8f);
            checks["跨越180度采用最短方向"] = wrapStep > 0 && wrapStep < 2.1f && Mathf.Abs(Mathf.DeltaAngle(Yaw(yaw), -179)) < .5f;

            motion.SetAimTarget(pitch.position + Vector3.forward * 4 + Vector3.up * 20);
            Advance(motion, 2);
            float maximumElevation = Elevation(pitch);
            motion.SetAimTarget(pitch.position + Vector3.forward * 4 + Vector3.down * 20);
            Advance(motion, 2);
            float minimumElevation = Elevation(pitch);
            checks["俯仰限制为下俯6度上仰18度"] = Math.Abs(maximumElevation - 18) < .5f && Math.Abs(minimumElevation + 6) < .5f;

            motion.ResetPose();
            Vector3 restingRecoil = recoil.localPosition;
            motion.NotifyShot(.15f);
            Vector3 displacement = recoil.localPosition - restingRecoil;
            motion.Advance(.02f);
            float firstSpin = Quaternion.Angle(resting[3].Rotation, spin.localRotation);
            motion.SetAimTarget(yaw.position + Vector3.right * 10 + Vector3.up * 3);
            var paused = allMoving.Select(t => new Pose(t)).ToArray();
            motion.Advance(0);
            motion.Advance(-.1f);
            checks["零时间或负时间冻结全部动作"] = allMoving.Select((t, i) => paused[i].Matches(t)).All(v => v);
            Advance(motion, .15f);
            checks["开火后座沿炮轴后退12毫米并恢复"] = Math.Abs(displacement.z + .012f) < .0001f && Math.Abs(displacement.x) < .0001f && Math.Abs(displacement.y) < .0001f && Vector3.Distance(recoil.localPosition, restingRecoil) < .0001f;
            checks["开火触发炮管自转"] = firstSpin > .01f;
            Advance(motion, 2);
            Quaternion settledSpin = spin.localRotation;
            motion.Advance(.05f);
            checks["停火后炮管减速至静止"] = Quaternion.Angle(settledSpin, spin.localRotation) < .001f;
            checks["所有运动保持底座固定"] = basePose.Matches(fixedBase) && Vector3.Distance(baseWorldPosition, fixedBase.position) < .00001f && Quaternion.Angle(baseWorldRotation, fixedBase.rotation) < .001f;

            motion.SetAimTarget(yaw.position + Vector3.right * 10);
            motion.NotifyShot(.15f);
            motion.Advance(.04f);
            instance.SetActive(false);
            view.ResetForReuse();
            instance.SetActive(true);
            checks["回收复用恢复全部关节姿态"] = allMoving.Select((t, i) => resting[i].Matches(t)).All(v => v);
            motion.Advance(.1f);
            checks["复用没有旧目标后座或自转残留"] = allMoving.Select((t, i) => resting[i].Matches(t)).All(v => v);
            motion.NotifyShot(.15f);
            motion.Advance(.04f);
            checks["复用后仍可再次射击"] = Quaternion.Angle(resting[3].Rotation, spin.localRotation) > .01f;

            var effectObject = new GameObject("速射炮临时特效验证");
            try
            {
                var effectPool = effectObject.AddComponent<EffectPool>();
                effectPool.AutomaticAdvance = false;
                effectPool.Library = Require<EffectLibrary>("Assets/Defend/Content/Art/PhaseFiveEffects/EffectLibrary.asset");
                var beforeEffect = allMoving.Select(t => new Pose(t)).ToArray();
                var handle = effectPool.TryPlay("RapidMuzzle", new EffectRequest
                {
                    Subject = view.VisualRoot, Origin = motion.MuzzlePosition, Destination = motion.MuzzlePosition + motion.MuzzleTransform.forward * 5,
                    Rotation = motion.MuzzleRotation, Intensity = 1, Seed = 13
                });
                bool effectBegan = handle.IsValid;
                effectPool.Advance(.04f);
                effectPool.StopAll();
                checks["炮口特效归还不会覆盖连续机械动作"] = effectBegan && allMoving.Select((t, i) => beforeEffect[i].Matches(t)).All(v => v);
            }
            finally { Object.DestroyImmediate(effectObject); }

            foreach (string kind in new[] { "BurstTurret", "AreaTurret" }) CheckLegacyWeapon(kind, checks);
            return Finish("MotionChecks", checks, new { firstYaw, wrapStep, maximumElevation, minimumElevation, recoilMeters = -displacement.z, firstSpinDegrees = firstSpin });
        }
        finally { Object.DestroyImmediate(instance); }
    }

    /// <summary>运行实际关卡、真实射击和会话时钟；仅使用配置的临时副本，结束时恢复原配置并重开。</summary>
    public static Task<object> CombatChecks() => RunCombatChecks(false);

    /// <summary>与战斗验证共用一次临时会话，同时录制真实 Update 下的60帧近景。</summary>
    public static Task<object> CaptureMotion() => RunCombatChecks(true);

    private static async Task<object> RunCombatChecks(bool captureMotion)
    {
        RequirePlayMode();
        if (SceneManager.GetActiveScene().path != Defend.Editor.PhaseFiveCombatSceneBuilder.ScenePath) throw new Exception("请在工业防线关卡中运行真实战斗验证。");
        var session = Object.FindAnyObjectByType<GameSessionRoot>();
        var presentation = Object.FindAnyObjectByType<WorldPresentation>();
        var art = Object.FindAnyObjectByType<CombatArtPresentation>();
        var sessionData = new SerializedObject(session);
        var originalBattle = (BattleSettingsAsset)sessionData.FindProperty("battleSettings").objectReferenceValue;
        var battleCopy = Object.Instantiate(originalBattle);
        bool originalBackground = Application.runInBackground;
        bool originalPaused = session.Paused;
        double originalSpeed = session.Speed;
        var checks = new Dictionary<string, bool>();
        try
        {
            Application.runInBackground = true;
            JsonUtility.FromJsonOverwrite("{\"enemySpeedCellsPerSecond\":0.7,\"waves\":[{\"enemyCount\":15,\"enemyHealth\":4000,\"spawnIntervalTicks\":15,\"clearReward\":0,\"enemyPattern\":[0],\"entranceIndex\":0}],\"spawnPoints\":[{\"x\":18,\"y\":16}]}", battleCopy);
            session.ConfigureBattle(battleCopy, Object.FindAnyObjectByType<BattlePresentation>(), Object.FindAnyObjectByType<BattleControlPanel>());
            session.Restart();
            session.TogglePause();
            for (int i = 0; i < 160; i++) session.SingleStep();
            session.StartWave();
            BuildingState firingBuilding = null;
            for (int i = 0; i < 600; i++)
            {
                session.SingleStep();
                firingBuilding = session.World.Buildings.FirstOrDefault(b => b.Kind == BuildingKind.Turret && b.ShotsFired > 0);
                if (firingBuilding != null) break;
            }
            if (firingBuilding == null) throw new Exception("实际战斗在限定600个逻辑步内没有速射炮开火。");
            var view = presentation.FindView(firingBuilding.Id);
            var motion = view.VisualRoot.GetComponent<RapidTurretMotion>();
            var recoil = Find(view.VisualRoot, "BarrelRecoil");
            var allMoving = MovingNames.Select(n => Find(view.VisualRoot, n)).ToArray();
            var fixedBase = Find(view.VisualRoot, "FixedBase");
            var basePose = new Pose(fixedBase);
            checks["现役实例使用新模型与动作组件"] = motion != null && motion.IsConfigured && UsesProductionMeshes(view.VisualRoot);
            var sourceRecoil = Find(Require<GameObject>(PrefabFolder + "Turret.prefab").transform, "BarrelRecoil");
            // 权威命中与机械转向分开：必须实际等到该炮的已对齐视觉射击，而不是把合法转向延迟判成缺失后座。
            for (int i = 0; i < 120 && !HasMuzzleAt(art, motion); i++) session.SingleStep();
            checks["真实射击对齐后触发后座"] = HasMuzzleAt(art, motion) && Vector3.Distance(recoil.localPosition, sourceRecoil.localPosition) > .005f && art.ObservedShots > 0 && art.PlayedRapidShots > 0;
            var muzzleEffect = art.Pool.GetComponentsInChildren<WeaponMuzzleEffect>().Where(e => e.IsRunning && e.name.StartsWith("RapidMuzzle", StringComparison.Ordinal)).OrderBy(e => Vector3.Distance(e.transform.position, motion.MuzzlePosition)).FirstOrDefault();
            checks["真实炮口闪光使用当前炮口"] = muzzleEffect != null && Vector3.Distance(muzzleEffect.transform.position, motion.MuzzlePosition) < .03f;
            var frozen = allMoving.Select(t => new Pose(t)).ToArray();
            double pausedClock = art.PresentationSeconds;
            await Task.Delay(180);
            checks["会话暂停冻结动作与表现时钟"] = allMoving.Select((t, i) => frozen[i].Matches(t)).All(v => v) && Math.Abs(art.PresentationSeconds - pausedClock) < .00001;

            session.TogglePause();
            var rates = new List<object>();
            foreach (int speed in new[] { 1, 2, 4 })
            {
                SetSpeed(session, speed);
                await Task.Delay(100);
                double beforeClock = art.PresentationSeconds;
                double beforeReal = Time.realtimeSinceStartupAsDouble;
                await Task.Delay(400);
                double realSeconds = Time.realtimeSinceStartupAsDouble - beforeReal;
                double advanced = art.PresentationSeconds - beforeClock;
                double rate = advanced / realSeconds;
                checks[speed + "倍速驱动实际表现时钟"] = Math.Abs(rate - speed) < speed * .3;
                rates.Add(new { speed, realSeconds, advanced, rate });
            }
            session.TogglePause();
            checks["真实战斗转向不移动底座"] = basePose.Matches(fixedBase);
            checks["真实战斗已推动炮管转动"] = Quaternion.Angle(frozen[3].Rotation, allMoving[3].localRotation) > .05f;
            var materials = view.VisualRoot.GetComponentsInChildren<Renderer>().SelectMany(r => r.sharedMaterials).Distinct().ToArray();
            checks["运行时继续使用资产材质"] = materials.All(m => !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(m)));
            string screenshot = Capture(view, "CombatClose.png");
            object recording = null;
            if (captureMotion)
            {
                SetSpeed(session, 1);
                long startingShots = firingBuilding.ShotsFired;
                session.TogglePause();
                recording = await RecordMotion(view, session, art);
                session.TogglePause();
                checks["动作录制期间真实速射炮持续开火"] = firingBuilding.ShotsFired > startingShots;
            }
            var result = new { firingBuilding.Id, firingBuilding.ShotsFired, observedShots = art.ObservedShots, playedRapidShots = art.PlayedRapidShots, tick = session.World.CurrentTick, rates, screenshot, recording };
            session.Restart();
            var restored = presentation.FindView(firingBuilding.Id);
            if (restored != null)
            {
                var source = Require<GameObject>(PrefabFolder + "Turret.prefab");
                checks["会话重开恢复初始关节姿态"] = MovingNames.All(n => new Pose(Find(source.transform, n)).Matches(Find(restored.VisualRoot, n)));
            }
            return Finish("CombatChecks", checks, result);
        }
        finally
        {
            session.ConfigureBattle(originalBattle, Object.FindAnyObjectByType<BattlePresentation>(), Object.FindAnyObjectByType<BattleControlPanel>());
            session.Restart();
            SetSpeed(session, originalSpeed);
            if (session.Paused != originalPaused) session.TogglePause();
            Application.runInBackground = originalBackground;
            Object.DestroyImmediate(battleCopy);
        }
    }

    public static object CaptureCombat()
    {
        RequirePlayMode();
        var view = Object.FindObjectsByType<BuildingView>().FirstOrDefault(v => v.Definition.Kind == BuildingKind.Turret && v.gameObject.activeInHierarchy);
        if (view == null) throw new Exception("当前场景没有速射炮。");
        return new { screenshot = Capture(view, "CurrentCombatClose.png") };
    }

    /// <summary>验证背后突现目标的首次视觉射击、暂停和待播记录的生命周期，全部射击来自真实模拟。</summary>
    public static async Task<object> AlignmentRegression()
    {
        RequirePlayMode();
        if (SceneManager.GetActiveScene().path != Defend.Editor.PhaseFiveCombatSceneBuilder.ScenePath) throw new Exception("请在工业防线播放会话中运行对齐回归。");
        var session = Object.FindAnyObjectByType<GameSessionRoot>();
        var presentation = Object.FindAnyObjectByType<WorldPresentation>();
        var art = Object.FindAnyObjectByType<CombatArtPresentation>();
        var originalBattle = (BattleSettingsAsset)new SerializedObject(session).FindProperty("battleSettings").objectReferenceValue;
        var battleCopy = Object.Instantiate(originalBattle);
        bool originalPaused = session.Paused, originalBackground = Application.runInBackground;
        double originalSpeed = session.Speed;
        var checks = new Dictionary<string, bool>();
        try
        {
            Application.runInBackground = true;
            session.Restart();
            session.TogglePause();
            var authored = session.World.Buildings.First(b => b.Kind == BuildingKind.Turret);
            int spawnZ = -1;
            for (int distance = 3; distance <= 6; distance++)
                if (authored.Z + distance < session.World.Settings.Depth && session.World.FindBuildingAt(authored.X, authored.Z + distance) == null &&
                    !session.Battle.Settings.BlockedCells.Any(p => p.X == authored.X && p.Z == authored.Z + distance))
                { spawnZ = authored.Z + distance; break; }
            if (spawnZ < 0) throw new Exception("现役炮塔前方没有可用的限定验证出生格。");
            JsonUtility.FromJsonOverwrite("{\"enemySpeedCellsPerSecond\":0.1,\"waves\":[{\"enemyCount\":1,\"enemyHealth\":100000,\"spawnIntervalTicks\":500,\"clearReward\":0,\"enemyPattern\":[0],\"entranceIndex\":0}],\"spawnPoints\":[{\"x\":" + authored.X + ",\"y\":" + spawnZ + "}]}", battleCopy);
            session.ConfigureBattle(battleCopy, Object.FindAnyObjectByType<BattlePresentation>(), Object.FindAnyObjectByType<BattleControlPanel>());
            long turretId = authored.Id;
            BuildingView view = PrepareRearShot(session, presentation, turretId);
            var motion = view.VisualRoot.GetComponent<RapidTurretMotion>();
            var recoil = Find(view.VisualRoot, "BarrelRecoil");
            Vector3 restingRecoil = Find(Require<GameObject>(PrefabFolder + "Turret.prefab").transform, "BarrelRecoil").localPosition;
            var initialShot = session.Battle.Shots.Last(s => s.TurretId == turretId);
            var target = new Vector3((float)initialShot.TargetX, .35f, (float)initialShot.TargetZ);
            float initialAngle = HorizontalAngle(motion.MuzzleTransform.forward, target - motion.MuzzlePosition);
            checks["首次权威射击确实来自炮管后方"] = initialAngle >= 150 && art.ObservedShots > 0;
            checks["未对齐首次射击只排队且无后座闪光射线"] = art.PendingRapidShots == 1 && art.PlayedRapidShots == 0 && ActiveRapidShots(art) == 0 && Vector3.Distance(recoil.localPosition, restingRecoil) < .00001f;
            var pivots = MovingNames.Select(n => Find(view.VisualRoot, n)).ToArray();
            var frozen = pivots.Select(t => new Pose(t)).ToArray();
            double frozenClock = art.PresentationSeconds;
            art.Refresh(session.World.CurrentTick + 100, session.SelectedBuildingId, true, .5f);
            await Task.Delay(160);
            checks["暂停冻结待播射击与转向"] = art.PendingRapidShots == 1 && art.PlayedRapidShots == 0 && ActiveRapidShots(art) == 0 && Math.Abs(art.PresentationSeconds - frozenClock) < .00001 && pivots.Select((t, i) => frozen[i].Matches(t)).All(v => v);

            long queuedTick = session.World.CurrentTick;
            bool bounded = true;
            for (int i = 0; i < 100 && art.PlayedRapidShots == 0; i++)
            {
                session.SingleStep();
                bounded &= art.PendingRapidShots <= 1;
            }
            var tracer = art.Pool.GetComponentsInChildren<WeaponTracerEffect>().FirstOrDefault(e => e.IsRunning && e.name.StartsWith("RapidTracer", StringComparison.Ordinal));
            var line = tracer == null ? null : tracer.GetComponentsInChildren<LineRenderer>().FirstOrDefault(l => l.enabled && l.positionCount >= 2);
            float emittedAngle = line == null ? 180 : HorizontalAngle(motion.MuzzleTransform.forward, line.GetPosition(1) - line.GetPosition(0));
            checks["真实射线仅在水平对齐6度内发出"] = art.PlayedRapidShots == 1 && line != null && emittedAngle <= 6.1f && Vector3.Distance(line.GetPosition(0), motion.MuzzlePosition) < .03f;
            checks["对齐后首次射击同时触发炮口与后座"] = HasMuzzleAt(art, motion) && Vector3.Distance(recoil.localPosition, restingRecoil) > .005f;
            checks["转向期间合并待播且不集中补射"] = bounded && art.PendingRapidShots <= 1 && art.PlayedRapidShots == 1;
            var firstEmission = new { initialAngle, emittedAngle, delayTicks = session.World.CurrentTick - queuedTick, observedShots = art.ObservedShots, playedShots = art.PlayedRapidShots, mergedShots = art.MergedRapidShots };

            view = PrepareRearShot(session, presentation, turretId);
            var building = session.World.FindBuilding(turretId);
            int oldRevision = building.PositionRevision;
            var destination = FindDistantVacancy(session.World, building);
            checks["移动回归开始时确有未播射击"] = art.PendingRapidShots == 1 && art.PlayedRapidShots == 0;
            session.Submit(WorldCommand.Move(turretId, destination.x, destination.y));
            session.SingleStep();
            bool moved = building.PositionRevision == oldRevision + 1;
            bool clearedOnMove = art.PendingRapidShots == 0 && art.PlayedRapidShots == 0 && ActiveRapidShots(art) == 0;
            for (int i = 0; i < 30; i++) session.SingleStep();
            checks["移动取消原位置待播且之后不补发"] = moved && clearedOnMove && art.PendingRapidShots == 0 && art.PlayedRapidShots == 0 && ActiveRapidShots(art) == 0;

            PrepareRearShot(session, presentation, turretId);
            checks["重开回归开始时确有未播射击"] = art.PendingRapidShots == 1 && art.PlayedRapidShots == 0;
            session.Restart();
            session.TogglePause();
            for (int i = 0; i < 30; i++) session.SingleStep();
            var restored = presentation.FindView(turretId);
            var source = Require<GameObject>(PrefabFolder + "Turret.prefab");
            checks["重开清空待播并保持初始关节姿态"] = art.PendingRapidShots == 0 && art.PlayedRapidShots == 0 && ActiveRapidShots(art) == 0 && MovingNames.All(n => new Pose(Find(source.transform, n)).Matches(Find(restored.VisualRoot, n)));
            return Finish("AlignmentRegression", checks, new { turretId, spawn = new[] { authored.X, spawnZ },
                initialPoseSetup = "预充能后通过现役RapidTurretMotion.SetAimTarget与Advance建立朝后姿态；新敌人出生、射击与后续推进均由真实会话完成。",
                firstEmission, movedDestination = new[] { destination.x, destination.y } });
        }
        finally
        {
            session.ConfigureBattle(originalBattle, Object.FindAnyObjectByType<BattlePresentation>(), Object.FindAnyObjectByType<BattleControlPanel>());
            session.Restart();
            SetSpeed(session, originalSpeed);
            if (session.Paused != originalPaused) session.TogglePause();
            Application.runInBackground = originalBackground;
            Object.DestroyImmediate(battleCopy);
        }
    }

    private static BuildingView PrepareRearShot(GameSessionRoot session, WorldPresentation presentation, long turretId)
    {
        session.Restart();
        session.TogglePause();
        foreach (var other in session.World.Buildings.Where(b => b.IsTurret && b.Id != turretId)) session.Submit(WorldCommand.Sell(other.Id));
        for (int i = 0; i < 160; i++) session.SingleStep();
        // 场景炮塔背后是障碍带。用实际动作组件安排刚追过另一侧目标的姿态，再从合法前方格生成新敌人。
        // 只安排起始条件；随后不再直接调用动作组件，所有转向和射击都经过真实会话与表现适配器。
        var view = presentation.FindView(turretId);
        var motion = view.VisualRoot.GetComponent<RapidTurretMotion>();
        motion.SetAimTarget(view.transform.position + Vector3.back * 10);
        motion.Advance(2);
        session.StartWave();
        for (int i = 0; i < 100 && session.World.FindBuilding(turretId).ShotsFired == 0; i++) session.SingleStep();
        if (session.World.FindBuilding(turretId).ShotsFired == 0) throw new Exception("限定背后目标场景没有产生真实射击。");
        return view;
    }

    private static Vector2Int FindDistantVacancy(GameWorld world, BuildingState building)
    {
        for (int x = world.Settings.Width - 2; x > 0; x--)
            for (int z = world.Settings.Depth - 2; z > 0; z--)
                if (Math.Abs(x - building.X) + Math.Abs(z - building.Z) > 15 && world.PlacementIssue(BuildingKind.Turret, x, z) == null)
                    return new Vector2Int(x, z);
        throw new Exception("没有可用于移动取消回归的远端空格。");
    }

    private static int ActiveRapidShots(CombatArtPresentation art) => art.Pool.GetComponentsInChildren<PhaseFiveEffect>().Count(e => e.IsRunning &&
        (e.name.StartsWith("RapidMuzzle", StringComparison.Ordinal) || e.name.StartsWith("RapidTracer", StringComparison.Ordinal)));
    private static bool HasMuzzleAt(CombatArtPresentation art, RapidTurretMotion motion) => art.Pool.GetComponentsInChildren<WeaponMuzzleEffect>().Any(e => e.IsRunning && e.name.StartsWith("RapidMuzzle", StringComparison.Ordinal) && Vector3.Distance(e.transform.position, motion.MuzzlePosition) < .03f);
    private static float HorizontalAngle(Vector3 forward, Vector3 direction)
    {
        forward.y = 0;
        direction.y = 0;
        return Vector3.Angle(forward, direction);
    }

    private static bool UnitRange(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0 && value <= 1;

    private static void CheckLegacyWeapon(string kind, Dictionary<string, bool> checks)
    {
        var instance = Object.Instantiate(Require<GameObject>(PrefabFolder + kind + ".prefab"));
        try
        {
            var view = instance.GetComponent<BuildingView>();
            var anchors = view.VisualRoot.GetComponent<CombatModelAnchors>();
            var rig = view.VisualRoot.GetComponent<WeaponMotionRig>();
            var recoil = Find(view.VisualRoot, "BarrelRecoil");
            Vector3 target = instance.transform.position + new Vector3(6, .4f, 3);
            anchors.Aim(target, 1);
            var yawPose = new Pose(anchors.TurretYaw);
            var recoilPose = new Pose(recoil);
            uint first = rig.Acquire(target);
            rig.SetRecoil(first, .028f);
            uint second = rig.Acquire(target);
            rig.SetRecoil(second, .028f);
            var newOwnerPose = new Pose(recoil);
            rig.Release(first);
            bool ownership = newOwnerPose.Matches(recoil);
            rig.Release(second);
            checks[kind + "原有瞄准后座和归还行为保留"] = view.VisualRoot.GetComponent<RapidTurretMotion>() == null && ownership && recoilPose.Matches(recoil) && yawPose.Matches(anchors.TurretYaw);
        }
        finally { Object.DestroyImmediate(instance); }
    }

    private static string Capture(BuildingView view, string name)
    {
        Directory.CreateDirectory(EvidenceFolder);
        var camera = Camera.main;
        if (camera == null) throw new Exception("没有可用于URP截图的主摄像机。");
        var oldPosition = camera.transform.position;
        var oldRotation = camera.transform.rotation;
        var oldRect = camera.rect;
        var oldSize = camera.orthographicSize;
        float oldAspect = camera.aspect;
        var target = new RenderTexture(1400, 1000, 24, RenderTextureFormat.ARGB32);
        target.Create();
        var oldActive = RenderTexture.active;
        var pixels = new Texture2D(1400, 1000, TextureFormat.RGB24, false);
        string path = Path.GetFullPath(EvidenceFolder + name);
        try
        {
            camera.transform.rotation = Quaternion.Euler(38, -28, 0);
            camera.transform.position = view.transform.position + Vector3.up * .3f - camera.transform.forward * 30;
            camera.orthographicSize = 1.15f;
            camera.rect = new Rect(0, 0, 1, 1);
            camera.aspect = 1.4f;
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
            if (!RenderPipeline.SupportsRenderRequest(camera, request)) throw new Exception("当前渲染管线不支持SingleCameraRequest。");
            RenderPipeline.SubmitRenderRequest(camera, request);
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            pixels.Apply();
            File.WriteAllBytes(path, pixels.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = oldActive;
            camera.transform.SetPositionAndRotation(oldPosition, oldRotation);
            camera.orthographicSize = oldSize;
            camera.rect = oldRect;
            camera.aspect = oldAspect;
            Object.DestroyImmediate(pixels);
            target.Release();
            Object.DestroyImmediate(target);
        }
        return path;
    }

    private static async Task<object> RecordMotion(BuildingView view, GameSessionRoot session, CombatArtPresentation art)
    {
        const int width = 640, height = 560, frameCount = 60;
        const string directory = EvidenceFolder + "MotionFrames/";
        Directory.CreateDirectory(directory);
        var camera = Camera.main;
        if (camera == null) throw new Exception("没有可用于动作录制的主摄像机。");
        var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        target.Create();
        var pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
        var yaw = Find(view.VisualRoot, "TurretYaw");
        var pitch = Find(view.VisualRoot, "BarrelPitch");
        var recoil = Find(view.VisualRoot, "BarrelRecoil");
        var spin = Find(view.VisualRoot, "BarrelSpin");
        var frameReports = new List<object>();
        double started = Time.realtimeSinceStartupAsDouble;
        try
        {
            for (int index = 0; index < frameCount; index++)
            {
                // 让真实 GameSessionRoot.Update 推进模拟和表现；录制代码不调用动作组件或模拟单步。
                await Task.Delay(50);
                var oldPosition = camera.transform.position;
                var oldRotation = camera.transform.rotation;
                var oldRect = camera.rect;
                var oldSize = camera.orthographicSize;
                float oldAspect = camera.aspect;
                var oldActive = RenderTexture.active;
                string path = directory + "frame-" + index.ToString("D3") + ".png";
                double realTime = Time.realtimeSinceStartupAsDouble - started;
                try
                {
                    camera.transform.rotation = Quaternion.Euler(38, -28, 0);
                    camera.transform.position = view.transform.position + Vector3.up * .29f - camera.transform.forward * 30;
                    camera.orthographicSize = .93f;
                    camera.rect = new Rect(0, 0, 1, 1);
                    camera.aspect = width / (float)height;
                    var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
                    if (!RenderPipeline.SupportsRenderRequest(camera, request)) throw new Exception("当前渲染管线不支持动作录制。");
                    RenderPipeline.SubmitRenderRequest(camera, request);
                    RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                    pixels.Apply();
                    File.WriteAllBytes(path, pixels.EncodeToPNG());
                    var building = session.World.FindBuilding(view.BuildingId);
                    frameReports.Add(new
                    {
                        index, realTime, simulationSeconds = art.PresentationSeconds, tick = session.World.CurrentTick,
                        shots = building == null ? 0 : building.ShotsFired,
                        yawDegrees = Yaw(yaw), elevationDegrees = Elevation(pitch), recoilLocalZ = recoil.localPosition.z,
                        spinDegrees = Mathf.Repeat(spin.localEulerAngles.z, 360), path
                    });
                }
                finally
                {
                    RenderTexture.active = oldActive;
                    camera.transform.SetPositionAndRotation(oldPosition, oldRotation);
                    camera.orthographicSize = oldSize;
                    camera.rect = oldRect;
                    camera.aspect = oldAspect;
                }
            }
            var result = new { source = "实际工业防线 GameSessionRoot.Update", width, height, frameCount, targetFramesPerSecond = 20,
                realDurationSeconds = Time.realtimeSinceStartupAsDouble - started, speed = session.Speed, buildingId = view.BuildingId, frames = frameReports };
            File.WriteAllText(EvidenceFolder + "MotionFrames.json", Newtonsoft.Json.JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented));
            return new { result.source, result.width, result.height, result.frameCount, result.targetFramesPerSecond,
                result.realDurationSeconds, result.speed, result.buildingId, timingReport = Path.GetFullPath(EvidenceFolder + "MotionFrames.json") };
        }
        finally
        {
            Object.DestroyImmediate(pixels);
            target.Release();
            Object.DestroyImmediate(target);
        }
    }

    private static object Finish(string name, Dictionary<string, bool> checks, object observations)
    {
        var report = new { passed = checks.Values.All(v => v), checkCount = checks.Count, checks, observations };
        Directory.CreateDirectory(EvidenceFolder);
        File.WriteAllText(EvidenceFolder + name + ".json", Newtonsoft.Json.JsonConvert.SerializeObject(report, Newtonsoft.Json.Formatting.Indented));
        if (!report.passed) throw new Exception(name + "未通过：" + string.Join("；", checks.Where(p => !p.Value).Select(p => p.Key)));
        return report;
    }

    private static void Advance(RapidTurretMotion motion, float duration)
    {
        int frames = Mathf.CeilToInt(duration * 60);
        for (int i = 0; i < frames; i++) motion.Advance(duration / frames);
    }
    private static void SetSpeed(GameSessionRoot session, double speed)
    {
        for (int i = 0; i < 3 && session.Speed != speed; i++) session.CycleSpeed();
    }
    private static bool UsesProductionMeshes(Transform visual)
    {
        var actual = visual.GetComponentsInChildren<MeshFilter>(true);
        var expected = Require<GameObject>(CanonicalPrefabPath).GetComponentsInChildren<MeshFilter>(true);
        return actual.Length > 0 && actual.Length == expected.Length && actual.All(f =>
            f.sharedMesh != null && AssetDatabase.GetAssetPath(f.sharedMesh).StartsWith(MeshFolder, StringComparison.Ordinal) &&
            expected.Any(e => e.name == f.name && e.sharedMesh == f.sharedMesh));
    }
    private static float Yaw(Transform pivot) => Mathf.DeltaAngle(0, pivot.localEulerAngles.y);
    private static float Elevation(Transform pivot) => -Mathf.DeltaAngle(0, pivot.localEulerAngles.x);
    private static Transform Find(Transform root, string name) => root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);
    private static T Require<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new Exception("缺少资产：" + path);
    private static void RequirePlayMode() { if (!Application.isPlaying) throw new Exception("此项检查需要播放模式。"); }
    private static long TriangleCount(Mesh mesh)
    {
        long count = 0;
        for (int i = 0; i < mesh.subMeshCount; i++) count += (long)mesh.GetIndexCount(i) / 3;
        return count;
    }
    private readonly struct Pose
    {
        public readonly Vector3 Position;
        public readonly Quaternion Rotation;
        public readonly Vector3 Scale;
        public Pose(Transform t) { Position = t.localPosition; Rotation = t.localRotation; Scale = t.localScale; }
        public bool Matches(Transform t) => Vector3.Distance(Position, t.localPosition) < .00001f && Quaternion.Angle(Rotation, t.localRotation) < .001f && Vector3.Distance(Scale, t.localScale) < .00001f;
    }
}
