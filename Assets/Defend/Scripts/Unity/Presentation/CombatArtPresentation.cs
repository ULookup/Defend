using System;
using System.Collections.Generic;
using Defend.Simulation;
using Defend.PhaseFiveEffects;
using UnityEngine;

namespace Defend.Unity
{
    /// <summary>逐逻辑步保存不可变结果，按共同显示时刻触发发射、接触与受击反馈。</summary>
    public sealed class CombatArtPresentation : MonoBehaviour
    {
        [SerializeField] private EffectPool pool;
        [SerializeField] private ProjectilePresentation projectiles;
        [SerializeField] private DamageNumberPresentation damageNumbers;
        [SerializeField] private RailgunLaserPresentation railgunLaserPrefab;
        private readonly Dictionary<long, RailgunLaserPresentation> railgunLasers = new Dictionary<long, RailgunLaserPresentation>();
        private GameWorld world;
        private WorldPresentation buildings;
        private BattlePresentation enemies;
        private DefenseAudio audio;
        private long lastTick = -1, sequence, stateTick = -1, stateSelection = -1;
        private double presentationSeconds;
        private double audioDisplaySeconds;
        private bool presentationPaused;
        private readonly CombatDisplayQueue displayQueue = new CombatDisplayQueue();
        private readonly Dictionary<long, EffectHandle> muzzleHandles = new Dictionary<long, EffectHandle>();
        private readonly Dictionary<(long id, long tick), EffectHandle> transferHandles = new Dictionary<(long, long), EffectHandle>();
        private readonly HashSet<(long id, long tick)> travelling = new HashSet<(long, long)>();
        private readonly List<(long id, long tick)> retiredTransfers = new List<(long, long)>();
        private readonly Dictionary<(string effect, long id), (Vector3 position, EffectHandle handle)> states =
            new Dictionary<(string, long), (Vector3, EffectHandle)>();
        private readonly Dictionary<(string effect, long id), Vector3> wantedStates = new Dictionary<(string, long), Vector3>();
        private readonly List<(string effect, long id)> retiredStates = new List<(string, long)>();
        private readonly HashSet<long> connected = new HashSet<long>();
        private sealed class EnergyFrame
        {
            public double Time;
            public readonly List<EnergyTransferSnapshot> Departures = new List<EnergyTransferSnapshot>();
            public readonly List<EnergyTransferSnapshot> Deliveries = new List<EnergyTransferSnapshot>();
            public readonly List<EnergyTransferSnapshot> Transfers = new List<EnergyTransferSnapshot>();
        }
        private readonly Queue<(double time, long enemyId, Vector3 source, Vector3 target)> heavyContacts =
            new Queue<(double, long, Vector3, Vector3)>();
        private readonly Queue<(double time, EnemyKind kind, Vector3 position)> enemyAttacks =
            new Queue<(double, EnemyKind, Vector3)>();
        private readonly Queue<EnergyFrame> energyFrames = new Queue<EnergyFrame>();
        private readonly Dictionary<long, List<TurretAimSnapshot>> aimFrames = new Dictionary<long, List<TurretAimSnapshot>>();
        private readonly Dictionary<long, double> displayedRailgunShots = new Dictionary<long, double>();
        public int ObservedShots { get; private set; }
        public int PlayedRapidShots { get; private set; }
        // 保留已有诊断消费者，实际发射从不合并、过期或等待表现转向。
        public int MergedRapidShots => 0;
        public int ExpiredRapidShots => 0;
        public int PendingRapidShots => 0;
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
        public void ConfigureFeedback(ProjectilePresentation projectileViews, DamageNumberPresentation numbers)
        { projectiles = projectileViews; damageNumbers = numbers; }
        public void ConfigureAudio(DefenseAudio value) => audio = value;

        public void ResetSession()
        {
            foreach (var laser in railgunLasers.Values)
                if (laser != null) { laser.ResetSession(); Destroy(laser.gameObject); }
            railgunLasers.Clear();
            if (pool != null) pool.StopAll();
            audio?.StopChargeSounds();
            projectiles?.ResetSession(); damageNumbers?.ResetSession();
            if (buildings != null)
                foreach (long id in aimFrames.Keys) buildings.FindView(id)?.ModelAnchors?.ResetPose();
            presentationPaused = false;
            world = null; lastTick = stateTick = stateSelection = -1; sequence = 0; presentationSeconds = 0;
            displayQueue.Clear(); energyFrames.Clear(); aimFrames.Clear(); heavyContacts.Clear(); enemyAttacks.Clear();
            displayedRailgunShots.Clear();
            muzzleHandles.Clear(); transferHandles.Clear(); states.Clear(); wantedStates.Clear();
            travelling.Clear(); retiredTransfers.Clear(); retiredStates.Clear(); connected.Clear();
            ObservedShots = ObservedDeliveries = ObservedDeaths = ObservedBuildingHits = ObservedRepairs = ObservedShieldHits = ObservedShieldBreaks = 0;
            PlayedRapidShots = SuppressedEnergyEndpoints = 0;
        }

        public void Initialize(GameWorld value, WorldPresentation buildingViews, BattlePresentation enemyViews, bool observeCurrentEvents = true)
        {
            world = value; buildings = buildingViews; enemies = enemyViews;
            pool.AutomaticAdvance = false; pool.Paused = false; pool.Initialize();
            presentationSeconds = world.CurrentTick / (double)world.Settings.TicksPerSecond;
            if (observeCurrentEvents) ObserveStep(world);
            else
            {
                lastTick = world.CurrentTick;
                var energy = new EnergyFrame { Time = presentationSeconds };
                energy.Transfers.AddRange(world.Energy.Transfers); energyFrames.Enqueue(energy);
            }
        }

        public void ObserveStep(GameWorld value)
        {
            if (world != value) return;
            displayQueue.Observe(world.Events, world.Battle?.Shots);
            enemies?.ObserveStep(world);
            projectiles?.ObserveStep(world);
            if (lastTick == world.CurrentTick) return;
            lastTick = world.CurrentTick;
            if (world.Battle != null)
                foreach (var aim in world.Battle.TurretAims)
                {
                    if (!aimFrames.TryGetValue(aim.TurretId, out var frames))
                    { frames = new List<TurretAimSnapshot>(); aimFrames.Add(aim.TurretId, frames); }
                    frames.Add(aim);
                }
            if (world.Battle != null)
                foreach (var enemy in world.Battle.Enemies)
                {
                    if (enemy.LastAttackTick == world.CurrentTick)
                        enemyAttacks.Enqueue((world.CurrentTick / (double)world.Settings.TicksPerSecond,
                            enemy.Kind, Point(enemy.X, enemy.Z, .4f)));
                    if (enemy.Kind == EnemyKind.Heavy && enemy.LastAttackTick == world.CurrentTick)
                        heavyContacts.Enqueue((world.CurrentTick / (double)world.Settings.TicksPerSecond, enemy.Id,
                            Point(enemy.X, enemy.Z, .25f), Point(enemy.LastAttackTargetX, enemy.LastAttackTargetZ, .4f)));
                }
            var energy = new EnergyFrame { Time = world.CurrentTick / (double)world.Settings.TicksPerSecond };
            energy.Departures.AddRange(world.Energy.Departures); energy.Deliveries.AddRange(world.Energy.Deliveries);
            energy.Transfers.AddRange(world.Energy.Transfers); energyFrames.Enqueue(energy);
        }

        /// <summary>只推进已显示反馈。先调用再交付新结果，单步的新反馈保持初始状态。</summary>
        public void AdvanceFeedback(float seconds)
        {
            if (seconds <= 0f) return;
            // 终局后模拟时钟停在最后一步，允许已经发出的短效果完成收尾；主动暂停仍由调用者冻结。
            if (world?.Battle != null && world.Battle.IsFinished && presentationSeconds >= world.CurrentTick / (double)world.Settings.TicksPerSecond)
            {
                foreach (var laser in railgunLasers.Values) if (laser != null) laser.Advance(seconds);
                foreach (var id in aimFrames.Keys) buildings.FindView(id)?.ModelAnchors?.RailgunMotion?.Advance(seconds);
            }
            pool?.AdvanceDomains(0f, seconds);
            damageNumbers?.AdvanceFeedback(seconds);
            enemies?.AdvanceFeedback(seconds);
        }

        public void Refresh(double displayTick, long selectedId, bool paused, float frameSeconds)
        {
            if (world == null) return;
            presentationPaused = paused;
            audio?.SetCombatPaused(paused);
            if (!paused) AdvanceFeedback(Mathf.Max(0f, frameSeconds));
            FlushToTick(displayTick, selectedId, advanceModelTime: !paused);
        }

        public void FlushToTick(double displayTick, long selectedId, bool advanceModelTime = true)
        {
            if (world == null) return;
            double seconds = Math.Max(presentationSeconds, displayTick / world.Settings.TicksPerSecond);
            float elapsed = (float)(seconds - presentationSeconds);
            // 护盾收拢与恢复使用显示时钟；新命中在本帧推进后交付，避免单步吃掉初始状态。
            buildings?.AdvanceShieldPresentation(elapsed);
            // 暂停事务仍即时交付结果，但不推进机械动作；显式单步保留默认推进。
            if (advanceModelTime) buildings?.AdvanceModelPresentation(elapsed);
            pool.AdvanceDomains(elapsed, 0f);
            foreach (var laser in railgunLasers.Values) if (laser != null) laser.Advance(elapsed);
            AdvanceAims(seconds, elapsed);
            enemies?.Refresh(world, seconds * world.Settings.TicksPerSecond);
            projectiles?.Refresh(seconds, presentationPaused);
            audioDisplaySeconds = seconds;
            displayQueue.Drain(seconds, PresentResult, Fire);
            while (enemyAttacks.Count > 0 && enemyAttacks.Peek().time <= seconds + 1e-9)
            {
                var attack = enemyAttacks.Dequeue();
                if (seconds - attack.time <= .25)
                    audio?.PlayAt(attack.kind == EnemyKind.Heavy ? DefenseSoundCue.HeavyEnemyAttack : DefenseSoundCue.EnemyAttack,
                        attack.position);
            }
            while (heavyContacts.Count > 0 && heavyContacts.Peek().time <= seconds + 1e-9)
            {
                var contact = heavyContacts.Dequeue();
                pool.TryPlay("HeavyContact", Request(contact.source, contact.target, enemies.FindView(contact.enemyId)?.ModelRoot), 0, EffectTimeDomain.Feedback);
            }
            while (energyFrames.Count > 0 && energyFrames.Peek().Time <= seconds + 1e-9)
                PresentEnergy(energyFrames.Dequeue(), seconds);
            presentationSeconds = seconds;
            if (world.Battle != null && world.Battle.IsFinished && seconds >= world.CurrentTick / (double)world.Settings.TicksPerSecond)
            {
                audio?.StopChargeSounds();
                StopStates();
                foreach (var handle in transferHandles.Values) pool.Stop(handle);
                transferHandles.Clear();
            }
            else RefreshStates(selectedId);
        }

        private void AdvanceAims(double seconds, float elapsed)
        {
            foreach (var pair in aimFrames)
            {
                var frames = pair.Value;
                while (frames.Count > 1 && frames[0].EndTime < seconds - 1e-9) frames.RemoveAt(0);
                var aim = frames[0];
                var sourceState = world.FindBuilding(pair.Key);
                if (sourceState != null && world.Battle.State == BattleState.Preparing)
                {
                    var preparing = buildings.FindView(pair.Key)?.ModelAnchors;
                    preparing?.ApplyAuthoritativeAim(sourceState.Kind == BuildingKind.PiercingRailgun
                        ? sourceState.FacingDegrees : sourceState.OrientationDegrees, 0);
                    preparing?.RailgunMotion?.SetCharge(0);
                    preparing?.RailgunMotion?.Advance(elapsed);
                    audio?.SetRailgunCharge(pair.Key, Vector3.zero, 0, false);
                    continue;
                }
                if (sourceState != null && sourceState.Kind == BuildingKind.PiercingRailgun)
                {
                    float chargeProgress = aim.EndTime > aim.StartTime
                        ? Mathf.Lerp((float)aim.PreviousChargeProgress, (float)aim.ChargeProgress,
                            Mathf.Clamp01((float)((seconds - aim.StartTime) / (aim.EndTime - aim.StartTime))))
                        : (float)aim.ChargeProgress;
                    audio?.SetRailgunCharge(pair.Key, new Vector3((float)aim.MuzzleX, (float)aim.MuzzleY, (float)aim.MuzzleZ),
                        chargeProgress, !presentationPaused && sourceState.IsAlive && !sourceState.IsSold &&
                        sourceState.LifeGeneration == aim.SourceGeneration && world.Battle.State == BattleState.Combat &&
                        (aim.CombatState == TurretCombatState.Charging || aim.ChargeProgress > 0) &&
                        (!displayedRailgunShots.TryGetValue(pair.Key, out double firedAt) || aim.EndTime > firedAt + 1e-9) &&
                        seconds >= aim.StartTime && seconds <= aim.EndTime + .1);
                }
                var anchors = buildings.FindView(pair.Key)?.ModelAnchors;
                if (anchors == null || seconds < aim.StartTime) continue;
                float progress = aim.EndTime > aim.StartTime ? Mathf.Clamp01((float)((seconds - aim.StartTime) / (aim.EndTime - aim.StartTime))) : 1f;
                anchors.ApplyAuthoritativeAim(Mathf.LerpAngle((float)aim.PreviousYawDegrees, (float)aim.YawDegrees, progress),
                    Mathf.Lerp((float)aim.PreviousPitchDegrees, (float)aim.PitchDegrees, progress));
                anchors.RapidMotion?.Advance(elapsed);
                if (anchors.RailgunMotion != null)
                {
                    if (sourceState == null || !sourceState.IsAlive || sourceState.IsSold || sourceState.LifeGeneration != aim.SourceGeneration)
                    { anchors.RailgunMotion.ResetPose(); continue; }
                    anchors.RailgunMotion.SetCharge(Mathf.Lerp((float)aim.PreviousChargeProgress, (float)aim.ChargeProgress, progress));
                    anchors.RailgunMotion.Advance(elapsed);
                }
            }
        }

        private void PresentResult(WorldEventSnapshot result)
        {
            Vector3 position = Point(result.X, result.Z, .35f);
            float shieldRadius = 0;
            if (result.Kind == WorldEventKind.ShieldBreak)
            {
                var shield = buildings.FindView(result.SubjectId)?.Shield?.Visual;
                if (shield != null && shield.IsConfigured)
                {
                    position = shield.transform.position;
                    // 现役护罩由单位球面制作，水平世界缩放就是破裂环应覆盖的半径。
                    Vector3 scale = shield.transform.lossyScale;
                    shieldRadius = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
                }
            }
            PresentResultAudio(result, position);
            string effect;
            switch (result.Kind)
            {
                case WorldEventKind.ResonanceAbsorbed:
                case WorldEventKind.ResonanceRepair:
                case WorldEventKind.MagneticStormCharging:
                    buildings.ApplyResult(result); return;
                case WorldEventKind.MagneticStormBurst:
                    return;
                case WorldEventKind.EnemyHit:
                    if (result.Amount <= 0) return;
                    position = result.Hit.SplashRadius > 0
                        ? Point(result.Hit.EnemyX, result.Hit.EnemyZ, .35f)
                        : new Vector3((float)result.Hit.X, (float)result.Hit.Y, (float)result.Hit.Z);
                    damageNumbers?.Show(result);
                    enemies?.ApplyResult(result);
                    // 范围攻击在圆心只有一次完整冲击；每名受伤敌人只产生小型接触火花。
                    effect = result.Hit.SplashRadius > 0 ? "EnemyHit" :
                        result.Hit.TurretKind == BuildingKind.BurstTurret ? "HeavyImpact" : "RapidImpact";
                    break;
                case WorldEventKind.Explosion:
                    var explosion = Request(new Vector3((float)result.Hit.ExplosionX, 0, (float)result.Hit.ExplosionZ), position);
                    explosion.Radius = (float)result.Hit.SplashRadius;
                    pool.TryPlay("AreaImpact", explosion, 0, EffectTimeDomain.Feedback); return;
                case WorldEventKind.EnemyDeath:
                    effect = "EnemyDeath"; ObservedDeaths++; enemies?.ApplyResult(result); break;
                case WorldEventKind.BuildingDestroyed:
                    effect = "BuildingDestruction"; StopMuzzle(result.SubjectId); buildings.ApplyResult(result); break;
                case WorldEventKind.BuildingHit:
                    effect = "BuildingHit"; ObservedBuildingHits++; buildings.ApplyResult(result); break;
                case WorldEventKind.Repair:
                    effect = "RepairComplete"; ObservedRepairs++; buildings.ApplyResult(result); break;
                case WorldEventKind.ShieldHit:
                    ObservedShieldHits++; buildings.ApplyResult(result);
                    if (buildings.FindView(result.SubjectId)?.Shield?.Visual?.IsConfigured == true) return;
                    effect = "ShieldHit"; break;
                case WorldEventKind.ShieldBreak:
                    ObservedShieldBreaks++; buildings.ApplyResult(result); effect = "ShieldBreak"; break;
                default: return;
            }
            Vector3 direction = new Vector3((float)result.Hit.DirectionX, (float)result.Hit.DirectionY, (float)result.Hit.DirectionZ);
            var feedback = Request(position, position + direction);
            if (effect == "ShieldBreak" && shieldRadius > 0)
            {
                feedback.Radius = shieldRadius;
                // 破裂环与球形护罩的水平截面一致，不沿攻击方向倾倒整个护罩。
                feedback.Rotation = Quaternion.identity;
            }
            if (effect == "RapidImpact" || effect == "HeavyImpact")
            {
                // 接触点与喷射方向分别传递，不能把方向向量当成命中特效的位置偏移。
                feedback.Destination = position;
                feedback.Rotation = direction.sqrMagnitude > .000001f
                    ? Quaternion.LookRotation(-direction.normalized) : Quaternion.identity;
            }
            pool.TryPlay(effect, feedback, 0, EffectTimeDomain.Feedback);
        }

        private void Fire(ShotSnapshot shot)
        {
            ObservedShots++;
            var source = new Vector3((float)shot.SourceX, (float)shot.SourceY, (float)shot.SourceZ);
            audio?.SetRailgunCharge(shot.TurretId, source, 0, false);
            if (shot.TurretKind == BuildingKind.PiercingRailgun) displayedRailgunShots[shot.TurretId] = shot.Time;
            if (audioDisplaySeconds - shot.Time <= .25)
                audio?.PlayAt(shot.TurretKind == BuildingKind.PiercingRailgun ? DefenseSoundCue.RailgunFire :
                    shot.TurretKind == BuildingKind.BurstTurret ? DefenseSoundCue.HeavyFire :
                    shot.TurretKind == BuildingKind.AreaTurret ? DefenseSoundCue.AreaFire : DefenseSoundCue.RapidFire, source);
            var target = source + new Vector3((float)shot.DirectionX, (float)shot.DirectionY, (float)shot.DirectionZ);
            var sourceView = buildings.FindView(shot.TurretId);
            var sourceState = world.FindBuilding(shot.TurretId);
            bool matchesSource = sourceView != null && sourceView.gameObject.activeInHierarchy &&
                sourceView.BuildingId == shot.TurretId && sourceState != null && sourceState.LifeGeneration == shot.SourceGeneration;
            var anchors = matchesSource ? sourceView?.ModelAnchors : null;
            if (shot.TurretKind == BuildingKind.PiercingRailgun)
            {
                anchors?.RailgunMotion?.NotifyShot();
                if (railgunLaserPrefab != null)
                {
                    if (!railgunLasers.TryGetValue(shot.TurretId, out var laser) || laser == null)
                    {
                        laser = Instantiate(railgunLaserPrefab, transform);
                        laser.Initialize(); railgunLasers[shot.TurretId] = laser;
                    }
                    laser.Play(source, new Vector3((float)shot.TargetX, (float)shot.TargetY, (float)shot.TargetZ));
                }
                return;
            }
            if (anchors?.RapidMotion != null)
            {
                float interval = world.GetTurretDefinition(shot.TurretKind).CooldownTicks / (float)world.Settings.TicksPerSecond;
                anchors.RapidMotion.NotifyShot(interval); PlayedRapidShots++;
            }
            string prefix = shot.TurretKind == BuildingKind.BurstTurret ? "Heavy" : shot.TurretKind == BuildingKind.AreaTurret ? "Area" : "Rapid";
            // 枪口采用权威发射位置，源建筑之后损毁也不改变已经发射的路径。
            StopMuzzle(shot.TurretId);
            var request = Request(source, target, anchors == null ? null : sourceView.VisualRoot);
            request.AuthoritativeAim = true;
            muzzleHandles[shot.TurretId] = pool.TryPlay(prefix == "Area" ? "AreaDischarge" : prefix + "Muzzle",
                request, 0, EffectTimeDomain.Feedback);
        }

        private void PresentResultAudio(WorldEventSnapshot result, Vector3 position)
        {
            if (audio == null) return;
            if (result.Kind == WorldEventKind.BuildingDestroyed)
                audio.SetRailgunCharge(result.SubjectId, position, 0, false);
            // 卡顿或快进时只保留靠近当前显示时刻的声音，不补播一串已经过去的攻击。
            if (audioDisplaySeconds - result.Time > .25) return;
            switch (result.Kind)
            {
                case WorldEventKind.EnemyHit:
                    if (result.Amount <= 0) return;
                    position = result.Hit.SplashRadius > 0 ? Point(result.Hit.EnemyX, result.Hit.EnemyZ, .35f) :
                        new Vector3((float)result.Hit.X, (float)result.Hit.Y, (float)result.Hit.Z);
                    audio.PlayAt(result.Hit.TurretKind == BuildingKind.PiercingRailgun ? DefenseSoundCue.RailgunImpact :
                        result.Hit.TurretKind == BuildingKind.BurstTurret ? DefenseSoundCue.HeavyImpact : DefenseSoundCue.MetalImpact, position);
                    break;
                case WorldEventKind.Explosion:
                    audio.PlayAt(DefenseSoundCue.AreaExplosion, Point(result.Hit.ExplosionX, result.Hit.ExplosionZ, .25f));
                    break;
                case WorldEventKind.EnemyDeath:
                    audio.PlayAt(result.Hit.EnemyKind == EnemyKind.Heavy || result.Hit.EnemyKind == EnemyKind.Demolisher
                        ? DefenseSoundCue.HeavyEnemyDeath : DefenseSoundCue.EnemyDeath, position);
                    break;
                case WorldEventKind.BuildingHit:
                    if (result.Amount > 0)
                    {
                        // 护盾吸收有独立事件；这里只反馈已经发生的生命损失。
                        bool coreHit = world.FindBuilding(result.SubjectId)?.Kind == BuildingKind.Core;
                        audio.PlayAt(coreHit ? DefenseSoundCue.CoreHit : DefenseSoundCue.BuildingHit, position);
                    }
                    break;
                case WorldEventKind.BuildingDestroyed: audio.PlayAt(DefenseSoundCue.BuildingDestroyed, position); break;
                case WorldEventKind.Repair: audio.PlayAt(DefenseSoundCue.Repair, position); break;
                case WorldEventKind.ShieldHit: audio.PlayAt(DefenseSoundCue.ShieldHit, position); break;
                case WorldEventKind.ShieldBreak: audio.PlayAt(DefenseSoundCue.ShieldBreak, position); break;
            }
        }

        private void PresentEnergy(EnergyFrame frame, double displaySeconds)
        {
            foreach (EnergyTransferSnapshot arrival in frame.Deliveries)
            { ObservedDeliveries++; PlayEnergyEndpoint("EnergyArrival", Port(arrival.TargetId, arrival.TargetX, arrival.TargetZ)); }
            travelling.Clear();
            foreach (EnergyTransferSnapshot transfer in frame.Transfers) travelling.Add((transfer.Id, transfer.StartTick));
            retiredTransfers.Clear();
            foreach (var item in transferHandles)
                if (!travelling.Contains(item.Key)) { pool.Stop(item.Value); retiredTransfers.Add(item.Key); }
            foreach (var key in retiredTransfers) transferHandles.Remove(key);
            foreach (EnergyTransferSnapshot departure in frame.Departures)
            {
                Vector3 from = Port(departure.SourceId, departure.SourceX, departure.SourceZ);
                Vector3 to = Port(departure.TargetId, departure.TargetX, departure.TargetZ);
                var request = Request(from, to); request.ConfirmedTransfer = true;
                var handle = pool.TryPlay("EnergyTransfer", request,
                    (departure.ArrivalTick - departure.StartTick) / (float)world.Settings.TicksPerSecond);
                pool.AdvanceHandle(handle, (float)Math.Max(0, displaySeconds - frame.Time));
                if (handle.IsValid) transferHandles[(departure.Id, departure.StartTick)] = handle;
                PlayEnergyEndpoint("PortSend", from);
            }
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
