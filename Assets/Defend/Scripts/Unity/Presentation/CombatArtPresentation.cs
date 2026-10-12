using System;
using System.Collections.Generic;
using Defend.Simulation;
using Defend.PhaseFiveEffects;
using UnityEngine;

namespace Defend.Unity
{
    /// <summary>逐逻辑步消费结算记录；插值只推进表现时钟，不参与命中、到货或寻路。</summary>
    public sealed class CombatArtPresentation : MonoBehaviour
    {
        private const float MaximumRapidShotDelaySeconds = 1.25f;
        [SerializeField] private EffectPool pool;
        private GameWorld world;
        private WorldPresentation buildings;
        private BattlePresentation enemies;
        private long lastTick = -1, sequence, stateTick = -1, stateSelection = -1;
        private double presentationSeconds;
        private readonly Dictionary<long, EffectHandle> muzzleHandles = new Dictionary<long, EffectHandle>();
        private readonly Dictionary<(long id, long tick), EffectHandle> transferHandles = new Dictionary<(long, long), EffectHandle>();
        private readonly HashSet<(long id, long tick)> travelling = new HashSet<(long, long)>();
        private readonly List<(long id, long tick)> retiredTransfers = new List<(long, long)>();
        private readonly Dictionary<(string effect, long id), (Vector3 position, EffectHandle handle)> states =
            new Dictionary<(string, long), (Vector3, EffectHandle)>();
        private readonly Dictionary<(string effect, long id), Vector3> wantedStates = new Dictionary<(string, long), Vector3>();
        private readonly List<(string effect, long id)> retiredStates = new List<(string, long)>();
        private readonly HashSet<long> connected = new HashSet<long>();
        private readonly List<long> retiredMuzzles = new List<long>();
        private sealed class RapidTurretTracking
        {
            public BuildingState Building;
            public RapidTurretMotion Motion;
            public EnemyState Target;
            public long LastShotEnemyId;
            public int LifeGeneration, PositionRevision;
            public ShotSnapshot PendingShot;
            public bool HasPendingShot;
            public float PendingAge, VisualCooldownRemaining;
        }
        private readonly Dictionary<long, RapidTurretTracking> rapidTurrets = new Dictionary<long, RapidTurretTracking>();
        private readonly List<long> retiredRapidTurrets = new List<long>();
        public int ObservedShots { get; private set; }
        public int PlayedRapidShots { get; private set; }
        public int MergedRapidShots { get; private set; }
        public int ExpiredRapidShots { get; private set; }
        public int PendingRapidShots
        {
            get { int count = 0; foreach (var tracking in rapidTurrets.Values) if (tracking.HasPendingShot) count++; return count; }
        }
        public int ObservedDeliveries { get; private set; }
        public int ObservedDeaths { get; private set; }
        public int ObservedBuildingHits { get; private set; }
        public int ObservedRepairs { get; private set; }
        public int ObservedShieldHits { get; private set; }
        public int ObservedShieldBreaks { get; private set; }
        public int SuppressedEnergyEndpoints { get; private set; }
        public int ActiveTransfers => transferHandles.Count;
        public int ActiveStates => states.Count;
        public double PresentationSeconds => presentationSeconds;
        public EffectPool Pool => pool;
        public bool IsBoundTo(GameWorld value) => world == value;

        public void ResetSession()
        {
            pool.StopAll();
            foreach (var tracking in rapidTurrets.Values) if (tracking.Motion != null) tracking.Motion.ResetPose();
            rapidTurrets.Clear(); retiredRapidTurrets.Clear();
            world = null; lastTick = stateTick = stateSelection = -1; sequence = 0; presentationSeconds = 0;
            muzzleHandles.Clear(); transferHandles.Clear(); states.Clear(); wantedStates.Clear();
            travelling.Clear(); retiredTransfers.Clear(); retiredStates.Clear(); connected.Clear();
            ObservedShots = ObservedDeliveries = ObservedDeaths = ObservedBuildingHits = ObservedRepairs = ObservedShieldHits = ObservedShieldBreaks = 0;
            PlayedRapidShots = MergedRapidShots = ExpiredRapidShots = 0;
            SuppressedEnergyEndpoints = 0;
        }

        public void Initialize(GameWorld value, WorldPresentation buildingViews, BattlePresentation enemyViews)
        {
            world = value; buildings = buildingViews; enemies = enemyViews;
            pool.AutomaticAdvance = false; pool.Paused = false; pool.Initialize();
            presentationSeconds = world.CurrentTick / (double)world.Settings.TicksPerSecond;
            ObserveStep(world);
        }

        private void AdvanceTo(double seconds)
        {
            if (seconds <= presentationSeconds) return;
            float elapsed = (float)(seconds - presentationSeconds);
            AdvanceRapidTurrets(elapsed);
            pool.Advance(elapsed);
            presentationSeconds = seconds;
            PlayReadyRapidShots();
        }

        private void AdvanceRapidTurrets(float seconds)
        {
            foreach (var tracking in rapidTurrets.Values)
            {
                if (!CanPresentRapidTurret(tracking)) { tracking.HasPendingShot = false; continue; }
                tracking.VisualCooldownRemaining = Mathf.Max(0f, tracking.VisualCooldownRemaining - seconds);
                if (tracking.HasPendingShot)
                {
                    tracking.PendingAge += seconds;
                    if (tracking.PendingAge > MaximumRapidShotDelaySeconds)
                    {
                        tracking.HasPendingShot = false;
                        ExpiredRapidShots++;
                    }
                }
                if (tracking.HasPendingShot)
                    tracking.Motion.SetAimTarget(Point(tracking.PendingShot.TargetX, tracking.PendingShot.TargetZ, .35f));
                else if (tracking.Target != null && tracking.Target.IsAlive)
                    tracking.Motion.SetAimTarget(EnemyAimPoint(tracking.Target));
                tracking.Motion.Advance(seconds);
            }
        }

        private static bool CanPresentRapidTurret(RapidTurretTracking tracking) =>
            tracking.Motion != null && tracking.Motion.isActiveAndEnabled && tracking.Building.IsAlive && !tracking.Building.IsSold &&
            tracking.LifeGeneration == tracking.Building.LifeGeneration && tracking.PositionRevision == tracking.Building.PositionRevision;

        private void PlayReadyRapidShots()
        {
            foreach (var tracking in rapidTurrets.Values) TryPlayRapidShot(tracking);
        }

        private void TryPlayRapidShot(RapidTurretTracking tracking)
        {
            if (!tracking.HasPendingShot) return;
            if (!CanPresentRapidTurret(tracking)) { tracking.HasPendingShot = false; return; }
            if (tracking.VisualCooldownRemaining > .00001f) return;
            ShotSnapshot shot = tracking.PendingShot;
            Vector3 target = Point(shot.TargetX, shot.TargetZ, .35f);
            if (!tracking.Motion.IsHorizontallyAimed(target)) return;
            tracking.HasPendingShot = false;
            float interval = world.Battle.Settings.GetTurret(shot.TurretKind).CooldownTicks / (float)world.Settings.TicksPerSecond;
            tracking.VisualCooldownRemaining = interval;
            tracking.Motion.NotifyShot(interval);
            PlayedRapidShots++;
            PlayShot(shot);
        }

        private Vector3 EnemyAimPoint(EnemyState enemy)
        {
            var view = enemies == null ? null : enemies.FindView(enemy.Id);
            // 读取已经插值的可见目标，保留高度分量；不把瞄准结果写回模拟。
            return view == null ? Point(enemy.X, enemy.Z, .35f) : view.transform.position + Vector3.up * .35f;
        }

        private void RefreshRapidTurretTargets()
        {
            retiredRapidTurrets.Clear();
            foreach (var item in rapidTurrets)
                if (item.Value.Motion == null || item.Value.Building.IsSold || !item.Value.Building.IsAlive)
                {
                    if (item.Value.Motion != null) item.Value.Motion.ResetPose();
                    retiredRapidTurrets.Add(item.Key);
                }
            foreach (long id in retiredRapidTurrets) rapidTurrets.Remove(id);
            if (world.Battle == null) return;
            foreach (BuildingState building in world.Buildings)
            {
                if (building.Kind != BuildingKind.Turret || !building.IsAlive || building.IsSold) continue;
                if (!rapidTurrets.TryGetValue(building.Id, out var tracking))
                {
                    var view = buildings.FindView(building.Id);
                    var motion = view == null || view.ModelAnchors == null ? null : view.ModelAnchors.RapidMotion;
                    if (motion == null || !motion.IsConfigured) continue;
                    motion.ResetPose();
                    tracking = new RapidTurretTracking { Building = building, Motion = motion,
                        LifeGeneration = building.LifeGeneration, PositionRevision = building.PositionRevision };
                    rapidTurrets.Add(building.Id, tracking);
                }
                if (tracking.LifeGeneration != building.LifeGeneration || tracking.PositionRevision != building.PositionRevision)
                {
                    StopMuzzle(building.Id);
                    tracking.Motion.ResetPose(); tracking.Target = null; tracking.LastShotEnemyId = 0;
                    tracking.HasPendingShot = false; tracking.PendingAge = tracking.VisualCooldownRemaining = 0f;
                    tracking.LifeGeneration = building.LifeGeneration; tracking.PositionRevision = building.PositionRevision;
                }
                // 冷却期间沿用仍有效的真实射击目标；其他时段按模拟的距离、身份顺序预瞄。
                // 额外一格只提前转向，不改变射程、目标选择、能量消耗或伤害。
                double trackingRange = world.Battle.Settings.GetTurret(building.Kind).Range + 1;
                double nearestDistance = trackingRange * trackingRange;
                bool retainShotTarget = world.CurrentTick < building.NextShotTick && tracking.Target != null &&
                    tracking.Target.IsAlive && tracking.Target.Id == tracking.LastShotEnemyId &&
                    EnemyDistanceSquared(building, tracking.Target) <= nearestDistance;
                if (!retainShotTarget)
                {
                    tracking.Target = null;
                    foreach (EnemyState enemy in world.Battle.Enemies)
                    {
                        if (!enemy.IsAlive) continue;
                        double distance = EnemyDistanceSquared(building, enemy);
                        if (distance > nearestDistance || (distance == nearestDistance && tracking.Target != null && enemy.Id > tracking.Target.Id)) continue;
                        tracking.Target = enemy; nearestDistance = distance;
                    }
                }
                if (!tracking.HasPendingShot && tracking.Target != null) tracking.Motion.SetAimTarget(EnemyAimPoint(tracking.Target));
                if (tracking.Target == null || building.CombatState == TurretCombatState.WaitingForEnergy ||
                    building.CombatState == TurretCombatState.Searching)
                    tracking.Motion.StopFiring();
            }
        }

        private static double EnemyDistanceSquared(BuildingState building, EnemyState enemy)
        {
            double x = enemy.X - building.CenterX, z = enemy.Z - building.CenterZ;
            return x * x + z * z;
        }

        public void Refresh(double displayTick, long selectedId, bool paused, float frameSeconds)
        {
            if (world == null) return;
            // 结局允许最后一次爆炸自然结束；用户暂停始终冻结所有效果。
            if (world.Battle != null && world.Battle.IsFinished)
            {
                StopStates();
                foreach (var handle in transferHandles.Values) pool.Stop(handle);
                transferHandles.Clear();
                if (!paused)
                {
                    float elapsed = Mathf.Min(frameSeconds, .1f);
                    AdvanceRapidTurrets(elapsed);
                    pool.Advance(elapsed);
                    PlayReadyRapidShots();
                }
            }
            else
            {
                // 普通刷新以暂停标志为准；显式单步仍由 ObserveStep 推进一次表现时间。
                if (!paused) AdvanceTo(displayTick / world.Settings.TicksPerSecond);
                RefreshStates(selectedId);
            }
        }

        public void ObserveStep(GameWorld value)
        {
            if (world != value || lastTick == value.CurrentTick) return;
            AdvanceTo(world.CurrentTick / (double)world.Settings.TicksPerSecond);
            lastTick = world.CurrentTick;
            RefreshRapidTurretTargets();
            retiredMuzzles.Clear();
            foreach (var item in muzzleHandles)
            {
                var building = world.FindBuilding(item.Key);
                if (!item.Value.IsValid || building == null || building.IsSold || !building.IsAlive)
                { pool.Stop(item.Value); retiredMuzzles.Add(item.Key); }
            }
            foreach (long id in retiredMuzzles) muzzleHandles.Remove(id);
            // 先处理关键结算，最后处理运输与常驻效果，避免装饰挤掉实际伤害反馈。
            foreach (WorldEventSnapshot result in world.Events)
            {
                Vector3 position = Point(result.X, result.Z, .35f);
                string effect;
                switch (result.Kind)
                {
                    case WorldEventKind.EnemyDeath: effect = "EnemyDeath"; ObservedDeaths++; break;
                    case WorldEventKind.BuildingDestroyed: effect = "BuildingDestruction"; StopMuzzle(result.SubjectId); break;
                    case WorldEventKind.BuildingHit: effect = "BuildingHit"; ObservedBuildingHits++; break;
                    case WorldEventKind.Repair: effect = "RepairComplete"; ObservedRepairs++; break;
                    case WorldEventKind.ShieldHit: effect = "ShieldHit"; ObservedShieldHits++; break;
                    case WorldEventKind.ShieldBreak: effect = "ShieldBreak"; ObservedShieldBreaks++; break;
                    default: effect = "EnemyHit"; break;
                }
                // 死亡不借用即将回收的模型；建筑损毁姿态由 BuildingView 的存活状态持续控制。
                Play(effect, position, position);
            }
            if (world.Battle != null)
            {
                foreach (ShotSnapshot shot in world.Battle.Shots) if (shot.Tick == lastTick) Fire(shot);
                foreach (EnemyState enemy in world.Battle.Enemies)
                    if (enemy.Kind == EnemyKind.Heavy && enemy.LastAttackTick == lastTick)
                    {
                        var view = enemies.FindView(enemy.Id);
                        Play("HeavyContact", Point(enemy.X, enemy.Z, .25f), Point(enemy.LastAttackTargetX, enemy.LastAttackTargetZ, .4f),
                            view == null ? null : view.ModelRoot);
                    }
            }
            foreach (EnergyTransferSnapshot arrival in world.Energy.Deliveries)
            {
                ObservedDeliveries++;
                PlayEnergyEndpoint("EnergyArrival", Port(arrival.TargetId, arrival.TargetX, arrival.TargetZ));
            }
            travelling.Clear();
            foreach (EnergyTransferSnapshot transfer in world.Energy.Transfers) travelling.Add((transfer.Id, transfer.StartTick));
            retiredTransfers.Clear();
            foreach (var item in transferHandles)
                if (!travelling.Contains(item.Key)) { pool.Stop(item.Value); retiredTransfers.Add(item.Key); }
            foreach (var key in retiredTransfers) transferHandles.Remove(key);
            foreach (EnergyTransferSnapshot departure in world.Energy.Departures)
            {
                Vector3 from = Port(departure.SourceId, departure.SourceX, departure.SourceZ);
                Vector3 to = Port(departure.TargetId, departure.TargetX, departure.TargetZ);
                var request = Request(from, to);
                request.ConfirmedTransfer = true;
                var handle = pool.TryPlay("EnergyTransfer", request,
                    (departure.ArrivalTick - departure.StartTick) / (float)world.Settings.TicksPerSecond);
                if (handle.IsValid) transferHandles[(departure.Id, departure.StartTick)] = handle;
                PlayEnergyEndpoint("PortSend", from);
            }
        }

        private void Fire(ShotSnapshot shot)
        {
            ObservedShots++;
            if (rapidTurrets.TryGetValue(shot.TurretId, out var tracking))
            {
                tracking.LastShotEnemyId = shot.EnemyId;
                tracking.Target = null;
                foreach (EnemyState enemy in world.Battle.Enemies)
                    if (enemy.Id == shot.EnemyId && enemy.IsAlive) { tracking.Target = enemy; break; }
                // 只保留最新一发未播放快照；转向耗时较长时合并表现，不积压或补射伤害。
                if (tracking.HasPendingShot) MergedRapidShots++;
                tracking.PendingShot = shot;
                tracking.HasPendingShot = true;
                tracking.PendingAge = 0f;
                tracking.Motion.SetAimTarget(Point(shot.TargetX, shot.TargetZ, .35f));
                TryPlayRapidShot(tracking);
                return;
            }
            var view = buildings.FindView(shot.TurretId);
            // 新模型失去有效跟踪记录意味着已损毁或回收，不借用其旧姿态播放本步射击。
            if (view != null && view.ModelAnchors != null && view.ModelAnchors.RapidMotion != null) return;
            PlayShot(shot);
        }

        private void PlayShot(ShotSnapshot shot)
        {
            var view = buildings.FindView(shot.TurretId);
            if (view == null) return;
            StopMuzzle(shot.TurretId);
            bool alive = world.FindBuilding(shot.TurretId)?.IsAlive == true;
            var anchors = alive ? view.ModelAnchors : null;
            Vector3 target = Point(shot.TargetX, shot.TargetZ, .35f);
            Vector3 source = anchors == null ? Point(shot.SourceX, shot.SourceZ, .7f) : anchors.Aim(target, shot.Id);
            string prefix = shot.TurretKind == BuildingKind.BurstTurret ? "Heavy" : shot.TurretKind == BuildingKind.AreaTurret ? "Area" : "Rapid";
            muzzleHandles[shot.TurretId] = Play(prefix == "Area" ? "AreaDischarge" : prefix + "Muzzle", source, target, alive ? view.VisualRoot : null);
            if (prefix != "Area") Play(prefix + "Tracer", source, target);
            var impact = Request(target, target);
            impact.Radius = (float)shot.SplashRadius;
            pool.TryPlay(prefix + "Impact", impact);
        }

        private void StopMuzzle(long id)
        {
            if (!muzzleHandles.TryGetValue(id, out var handle)) return;
            pool.Stop(handle); muzzleHandles.Remove(id);
        }
        private void PlayEnergyEndpoint(string id, Vector3 position)
        {
            // 为枪口、命中与护盾保留一半短效果容量；供电高峰只省略端口装饰，不影响账目或在途脉冲。
            if (pool.ActiveBudget(EffectBudgetGroup.ShortEffect) >= pool.ShortEffectLimit / 2) { SuppressedEnergyEndpoints++; return; }
            Play(id, position, position);
        }

        private Vector3 Port(long id, double x, double z)
        {
            var view = buildings.FindView(id);
            // 仅当建筑仍在快照坐标时读取挂点，移动不能改变在途端点。
            if (view != null && (view.transform.position - Point(x, z, 0)).sqrMagnitude < .001f)
            {
                var anchors = view.VisualRoot.GetComponent<CombatModelAnchors>();
                if (anchors != null) return anchors.PortPosition;
            }
            return Point(x, z, .25f);
        }

        private void RefreshStates(long selectedId)
        {
            if (stateTick == world.CurrentTick && stateSelection == selectedId) return;
            stateTick = world.CurrentTick; stateSelection = selectedId;
            wantedStates.Clear();
            if (world.Battle != null)
                foreach (EnemyState enemy in world.Battle.Enemies)
                    if (enemy.Kind == EnemyKind.Heavy && enemy.TargetBuildingId > 0 && wantedStates.Count < 16)
                        Want(enemy.TargetPurpose == EnemyTargetPurpose.SearchLastKnownPosition ? "SearchMarker" : "HeavyLock",
                            enemy.TargetBuildingId, Point(enemy.TargetX, enemy.TargetZ, .05f));
            connected.Clear();
            foreach (var building in world.Buildings)
                if (building.IsAlive && !building.IsSold && building.Kind == BuildingKind.Reactor) connected.Add(building.Id);
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (var edge in world.Energy.Connections)
                    if (connected.Contains(edge.SourceId)) changed |= connected.Add(edge.TargetId);
            }
            AddBuildingState(world.FindBuilding(selectedId), true);
            foreach (var building in world.Buildings) if (building.Id != selectedId) AddBuildingState(building, true);
            foreach (var building in world.Buildings) AddBuildingState(building, false);
            retiredStates.Clear();
            foreach (var item in states)
                if (!wantedStates.TryGetValue(item.Key, out var position) || (position - item.Value.position).sqrMagnitude > .0001f)
                { pool.Stop(item.Value.handle); retiredStates.Add(item.Key); }
            foreach (var key in retiredStates) states.Remove(key);
            foreach (var item in wantedStates)
                if (!states.TryGetValue(item.Key, out var state) || !state.handle.IsValid)
                    states[item.Key] = (item.Value, Play(item.Key.effect, item.Value, item.Value));
        }

        private void AddBuildingState(BuildingState building, bool warning)
        {
            if (building == null || building.IsSold || !building.IsAlive || wantedStates.Count >= (warning ? 24 : 32)) return;
            var view = buildings.FindView(building.Id);
            if (view == null) return;
            var anchors = view.VisualRoot.GetComponent<CombatModelAnchors>();
            if (anchors == null) return;
            if (warning)
            {
                if (!connected.Contains(building.Id)) Want("EnergyDisconnected", building.Id, anchors.StatusPosition);
                else if (building.CombatState == TurretCombatState.WaitingForEnergy) Want("EnergyShortage", building.Id, anchors.StatusPosition);
            }
            else if (connected.Contains(building.Id))
            {
                if (building.Kind == BuildingKind.Reactor) Want("ReactorRunning", building.Id, anchors.PortPosition);
                else if (building.Kind == BuildingKind.Relay && (building.StoredEnergy > 0 || building.ReceivedLastSecond > 0))
                    Want("RelayRunning", building.Id, anchors.PortPosition);
            }
        }

        private void Want(string effect, long id, Vector3 position) => wantedStates[(effect, id)] = position;
        private void StopStates() { foreach (var state in states.Values) pool.Stop(state.handle); states.Clear(); }
        private EffectRequest Request(Vector3 origin, Vector3 destination, Transform subject = null)
        {
            Vector3 direction = destination - origin;
            return new EffectRequest { Origin = origin, Destination = destination, Subject = subject, Intensity = 1,
                EventSequence = ++sequence, Seed = (int)(sequence % int.MaxValue),
                Rotation = direction.sqrMagnitude > .000001f ? Quaternion.LookRotation(direction) : Quaternion.identity };
        }
        private EffectHandle Play(string id, Vector3 origin, Vector3 destination, Transform subject = null)
            => pool.TryPlay(id, Request(origin, destination, subject));
        private static Vector3 Point(double x, double z, float height) => new Vector3((float)x, height, (float)z);
        private void OnDisable() { if (pool != null) ResetSession(); }
    }
}
