using System;
using System.Collections.Generic;

namespace Defend.Simulation
{
    /// <summary>拥有一局的敌人、射击和波次状态；建筑伤害及经济事务始终交给同一个世界。</summary>
    public sealed partial class BattleSystem
    {
        private readonly GameWorld world;
        private readonly BuildingGrid grid;
        private readonly EnemyNavigation navigation;
        private readonly EnemyBehaviorSystem behavior;
        private readonly EnemyMovementSystem movement;
        private readonly ProjectileSystem projectiles;
        private readonly Dictionary<long, TurretAimState> turretAims = new Dictionary<long, TurretAimState>();
        private readonly List<TurretAimSnapshot> aimSnapshots = new List<TurretAimSnapshot>();
        public IReadOnlyList<ProjectileSnapshot> Projectiles => projectiles.Snapshots;
        public IReadOnlyList<TurretAimSnapshot> TurretAims { get; }
        private readonly List<EnemyState> enemies = new List<EnemyState>();
        private readonly List<ShotSnapshot> shots = new List<ShotSnapshot>();
        private long nextEnemyId = 1;
        private long nextShotId = 1;
        private long nextSpawnTick;
        private int spawnCursor, groupIndex, groupSpawned, lossesAtWaveStart;
        private long waveStartTick, spawnDelay;

        public BattleSettings Settings { get; }
        public BattleState State { get; private set; } = BattleState.Preparing;
        public int WaveNumber { get; private set; }
        public int RemainingToSpawn { get; private set; }
        public int Kills { get; private set; }
        public int LastWaveReward { get; private set; }
        public int PendingKillReward { get; private set; }
        public int LastWaveFixedReward { get; private set; }
        public int LastWaveKillReward { get; private set; }
        public int LastWaveBuildingLosses { get; private set; }
        public int DestroyedBuildingCount => world.DestroyedBuildingCount;
        public long CoreBuildingId { get; private set; }
        public IReadOnlyList<EnemyState> Enemies { get; }
        public IReadOnlyList<ShotSnapshot> Shots { get; }
        public IReadOnlyList<SpawnPoint> SpawnPoints { get; private set; }
        public int NavigationRebuildCount => navigation.RebuildCount;
        public bool IsFinished => State == BattleState.Victory || State == BattleState.Defeat;
        public long CombatTicks { get; private set; }
        public EnemyBehaviorStatistics BehaviorStatistics => behavior.Statistics;

        internal BattleSystem(GameWorld world, BuildingGrid grid, BattleSettings settings)
        {
            this.world = world;
            this.grid = grid;
            Settings = settings;
            if (grid.Width < 5 || grid.Depth < 5)
                throw new ArgumentException("战斗地图至少需要 5 × 5 格，保证核心与敌人出生位置能够分离。");
            var spawnPoints = new List<SpawnPoint>(settings.SpawnPoints);
            if (spawnPoints.Count == 0)
            {
                spawnPoints.Add(new SpawnPoint(grid.Width / 2, 0));
                spawnPoints.Add(new SpawnPoint(grid.Width - 1, grid.Depth / 2));
                spawnPoints.Add(new SpawnPoint(grid.Width / 2, grid.Depth - 1));
                spawnPoints.Add(new SpawnPoint(0, grid.Depth / 2));
            }
            foreach (SpawnPoint point in spawnPoints)
            {
                if (point.X < 0 || point.Z < 0 || point.X >= grid.Width || point.Z >= grid.Depth || grid.Find(point.X, point.Z) < 0)
                    throw new ArgumentException("敌人出生位置必须位于地图内。", nameof(settings));
                for (int z = point.ReservedZ; z < point.ReservedZ + point.ReservedDepth; z++)
                    for (int x = point.ReservedX; x < point.ReservedX + point.ReservedWidth; x++)
                        if (x < 0 || z < 0 || x >= grid.Width || z >= grid.Depth || grid.Find(x, z) < 0)
                            throw new ArgumentException("敌人入口保留区域必须位于可通行地图内。", nameof(settings));
            }
            SpawnPoints = spawnPoints.AsReadOnly();
            Enemies = enemies.AsReadOnly();
            Shots = shots.AsReadOnly();
            navigation = new EnemyNavigation(world, grid);
            behavior = new EnemyBehaviorSystem(world, grid, this, navigation);
            movement = new EnemyMovementSystem(world, grid, this);
            projectiles = new ProjectileSystem(this, world);
            TurretAims = aimSnapshots.AsReadOnly();
            if (world.IsSandbox) InitializeSandbox();
        }

        internal CommandResult StartWave(long sequence)
        {
            if (State != BattleState.Preparing)
                return new CommandResult(sequence, false, "当前不能开始下一波。", 0);
            BuildingState core = world.FindBuilding(CoreBuildingId);
            if (core == null || !core.IsAlive || core.IsSold)
                return new CommandResult(sequence, false, "需要一座完好的核心才能开始战斗。", 0);
            if (world.IsSandbox) return StartSandboxWave(sequence);
            WaveNumber++;
            RemainingToSpawn = Settings.Waves[WaveNumber - 1].EnemyCount;
            LastWaveReward = LastWaveFixedReward = LastWaveKillReward = PendingKillReward = 0;
            groupIndex = groupSpawned = 0; spawnDelay = 0; waveStartTick = world.CurrentTick;
            InitializeIndependentWave(Settings.Waves[WaveNumber - 1]);
            lossesAtWaveStart = world.DestroyedBuildingCount;
            nextSpawnTick = world.CurrentTick;
            State = BattleState.Combat;
            return new CommandResult(sequence, true, "第 " + WaveNumber + " 波敌人开始进攻。", CoreBuildingId);
        }

        /// <summary>继续准备只恢复操作权限；波次奖励已经结算，能源时间和库存不重置。</summary>
        internal CommandResult ContinuePreparing(long sequence)
        {
            if (State != BattleState.WaveCleared) return new CommandResult(sequence, false, "当前没有等待确认的波次结算。", 0);
            State = BattleState.Preparing;
            return new CommandResult(sequence, true, "可以整备防线并主动开始下一波。", CoreBuildingId);
        }

        internal void RegisterCore(BuildingState building)
        {
            if (building.Kind == BuildingKind.Core) CoreBuildingId = building.Id;
        }

        internal void BuildingDestroyed(BuildingState building)
        {
            CancelCharge(building);
            ClearResonance(building);
            if (building.IsTurret) building.CombatState = TurretCombatState.Searching;
            if (building.Id == CoreBuildingId)
            {
                LastWaveBuildingLosses = world.DestroyedBuildingCount - lossesAtWaveStart;
                State = BattleState.Defeat; PendingKillReward = 0;
                CancelAllCharges();
                projectiles.Clear((double)world.CurrentTick / world.Settings.TicksPerSecond);
            }
        }

        internal string ValidatePlacement(int x, int z, int width, int depth)
        {
            foreach (SpawnPoint spawn in SpawnPoints)
                if (spawn.OverlapsReservedArea(x, z, width, depth)) return "不能占用敌人入口的保留区域。";
            foreach (EnemyState enemy in enemies)
            {
                if (!enemy.IsAlive) continue;
                if (Contains(x, z, width, depth, enemy.CellX, enemy.CellZ) ||
                    Contains(x, z, width, depth, enemy.NextCellX, enemy.NextCellZ))
                    return "敌人正在经过该位置，不能在此放置或恢复建筑。";
            }
            return null;
        }

        internal void StepCombat(long tick)
        {
            behavior.BeginStep(tick);
            projectiles.BeginStep();
            int expired = 0;
            while (expired < shots.Count && tick - shots[expired].Tick >= Settings.ShotDisplayTicks) expired++;
            if (expired > 0) shots.RemoveRange(0, expired);
            if (State != BattleState.Combat) return;

            CombatTicks++;
            double end = (double)tick / world.Settings.TicksPerSecond;
            double start = (double)(tick - 1) / world.Settings.TicksPerSecond;
            AdvanceAim(start, end);
            BuildingState core = world.FindBuilding(CoreBuildingId);
            behavior.Evaluate(core, tick);
            foreach (var enemy in enemies)
            {
                enemy.BeginMotion(start);
                if (enemy.IsAlive) movement.Step(enemy, tick);
                enemy.EndMotion(end);
            }
            // 已在途弹体先按步内时刻结算，再提交仍存活敌人的近战，最后在步末发射新弹体。
            projectiles.Advance(start, end);
            StepResonance(tick);
            foreach (var enemy in enemies)
            {
                if (State != BattleState.Combat) break;
                movement.CommitAttack(enemy, tick);
            }
            if (State == BattleState.Combat) StepTurrets(tick);
            RefreshAimSnapshots(start, end);
            for (int index = enemies.Count - 1; index >= 0; index--)
                if (!enemies[index].IsAlive) enemies.RemoveAt(index);
        }

        internal void CompleteStep(long tick)
        {
            if (State != BattleState.Combat) return;
            if (world.IsSandbox) { CompleteSandboxStep(tick); return; }
            WaveDefinition wave = Settings.Waves[WaveNumber - 1];
            if (wave.IndependentGroups) StepIndependentWave(tick, wave);
            else if (RemainingToSpawn > 0)
            {
                WaveGroupDefinition group = wave.Groups.Count == 0 ? null : wave.Groups[groupIndex];
                long scheduled = group == null ? nextSpawnTick : Math.Max(nextSpawnTick, waveStartTick + group.StartTick + spawnDelay);
                if (tick >= scheduled)
                {
                    int entrance = group?.EntranceIndex ?? wave.EntranceIndex;
                    var spawn = SpawnPoints[entrance < 0 ? spawnCursor % SpawnPoints.Count : entrance % SpawnPoints.Count];
                    var kind = group?.Kind ?? wave.EnemyPattern[(wave.EnemyCount - RemainingToSpawn) % wave.EnemyPattern.Count];
                    var definition = Settings.GetEnemy(kind);
                    if (enemies.Count < Settings.MaximumLivingEnemies && movement.TrySpawn(spawn, definition, out double x, out double z))
                    {
                        var enemy = new EnemyState(nextEnemyId++, spawn, wave.EnemyHealth, kind, definition) { X = x, Z = z, CellX = (int)x, CellZ = (int)z };
                        enemies.Add(enemy);
                        spawnCursor = (spawnCursor + 1) % SpawnPoints.Count;
                        RemainingToSpawn--;
                        nextSpawnTick = checked(tick + (group?.IntervalTicks ?? wave.SpawnIntervalTicks));
                        if (group != null && ++groupSpawned == group.Count) { groupIndex++; groupSpawned = 0; }
                    }
                    else
                    {
                        // 受阻的游戏时间进入整个后续序列的顺延，放开后仍一次生成一名。
                        spawnDelay++; nextSpawnTick = tick + 1;
                    }
                }
            }
            if (RemainingToSpawn != 0 || enemies.Count != 0) return;
            // 先离开战斗状态再发奖励，后续逻辑步或重复开始命令都不能再次领取。
            State = WaveNumber == Settings.Waves.Count ? BattleState.Victory : BattleState.WaveCleared;
            CancelAllCharges();
            projectiles.Clear((double)tick / world.Settings.TicksPerSecond);
            LastWaveFixedReward = wave.ClearReward;
            LastWaveKillReward = PendingKillReward;
            LastWaveBuildingLosses = world.DestroyedBuildingCount - lossesAtWaveStart;
            PendingKillReward = 0;
            LastWaveReward = world.GrantBattleReward(checked(LastWaveFixedReward + LastWaveKillReward));
        }

        private void AdvanceAim(double start, double end)
        {
            aimSnapshots.Clear();
            foreach (BuildingState turret in world.Buildings)
            {
                if (!turret.IsTurret || !turret.IsAlive || turret.IsSold) continue;
                TurretDefinition weapon = world.GetTurretDefinition(turret.Kind);
                if (!turretAims.TryGetValue(turret.Id, out var aim))
                {
                    double initialYaw = turret.Kind == BuildingKind.PiercingRailgun ? turret.FacingDegrees : turret.OrientationDegrees;
                    aim = new TurretAimState { Turret = turret, Yaw = initialYaw, PreviousYaw = initialYaw };
                    turretAims.Add(turret.Id, aim);
                }
                aim.PreviousChargeProgress = turret.ChargeProgress;
                if (weapon.IsPiercing)
                {
                    if (turret.ChargeStartedTick < 0 || !aim.CanTarget(weapon, aim.Target))
                    {
                        EnemyState farthest = null; double farthestDistance = -1;
                        foreach (var enemy in enemies)
                        {
                            if (!aim.CanTarget(weapon, enemy)) continue;
                            double dx = enemy.X - turret.CenterX, dz = enemy.Z - turret.CenterZ, distance = dx * dx + dz * dz;
                            if (distance > farthestDistance || (distance == farthestDistance && enemy.Id < farthest.Id))
                            { farthest = enemy; farthestDistance = distance; }
                        }
                        aim.Target = farthest;
                    }
                    // 瞬时射线不做弹道提前量。等本步敌人运动完成后再推进一次瞄准，
                    // 避免把一整个逻辑步的位置滞后误当成无法跟踪而反复中断蓄力。
                    aimSnapshots.Add(new TurretAimSnapshot(aim, start, end));
                    continue;
                }
                EnemyState nearest = null;
                double nearestDistance = weapon.Range * weapon.Range;
                foreach (var enemy in enemies)
                {
                    if (!enemy.IsAlive) continue;
                    double dx = enemy.X - turret.CenterX, dz = enemy.Z - turret.CenterZ, distance = dx * dx + dz * dz;
                    if (distance > nearestDistance || (distance == nearestDistance && nearest != null && enemy.Id > nearest.Id)) continue;
                    nearest = enemy; nearestDistance = distance;
                }
                aim.Target = nearest;
                aim.Advance(weapon, end - start);
                aimSnapshots.Add(new TurretAimSnapshot(aim, start, end));
            }
        }
        private void StepTurrets(long tick)
        {
            foreach (BuildingState turret in world.Buildings)
            {
                if (!turret.IsTurret || !turret.IsAlive || turret.IsSold || !turretAims.TryGetValue(turret.Id, out var aim)) continue;
                TurretDefinition weapon = world.GetTurretDefinition(turret.Kind);
                if (weapon.IsPiercing)
                {
                    if (!aim.CanTarget(weapon, aim.Target)) aim.Target = null;
                    aim.Advance(weapon, 1.0 / world.Settings.TicksPerSecond);
                    if (turret.ChargeStartedTick >= 0 && weapon.ChargeTicks > 0)
                    {
                        // 蓄力属于炮塔，不属于当前目标；换目标、重瞄准和短暂失去目标时继续积累。
                        // 能源失效仍立即中断；满蓄力仅保持待发，不能绕过后面的合法目标与对准检查。
                        if (turret.StoredEnergy < weapon.EnergyPerShot)
                        { CancelCharge(turret); turret.CombatState = TurretCombatState.WaitingForEnergy; turret.EnergyWaitTicks++; continue; }
                        turret.ChargeProgress = Math.Min(1, (double)(tick - turret.ChargeStartedTick) / weapon.ChargeTicks);
                    }
                }
                var target = aim.Target;
                if (target == null || !target.IsAlive || (weapon.IsPiercing && !aim.CanTarget(weapon, target)))
                { if (!weapon.IsPiercing) CancelCharge(turret); turret.CombatState = TurretCombatState.Searching; continue; }
                double dx = target.X - turret.CenterX, dz = target.Z - turret.CenterZ;
                if (dx * dx + dz * dz > weapon.Range * weapon.Range)
                { if (!weapon.IsPiercing) CancelCharge(turret); turret.CombatState = TurretCombatState.Searching; continue; }
                if (tick < turret.NextShotTick) { turret.CombatState = TurretCombatState.CoolingDown; continue; }
                // 移动已经完成；只重新核对目标方向，不在发射时额外推进炮口旋转。
                aim.RecheckAlignment(weapon);
                int aimIndex = aimSnapshots.FindIndex(value => value.TurretId == turret.Id);
                if (aimIndex >= 0) aimSnapshots[aimIndex] = new TurretAimSnapshot(aim,
                    (double)(tick - 1) / world.Settings.TicksPerSecond, (double)tick / world.Settings.TicksPerSecond);
                if (!aim.Aligned) { if (!weapon.IsPiercing) CancelCharge(turret); turret.CombatState = TurretCombatState.Aiming; continue; }
                if (weapon.ChargeTicks > 0)
                {
                    // 蓄力只是准备；储量不足立即中断，完成后仍由正式扣能入口重新核对。
                    if (turret.StoredEnergy < weapon.EnergyPerShot)
                    { CancelCharge(turret); turret.CombatState = TurretCombatState.WaitingForEnergy; turret.EnergyWaitTicks++; continue; }
                    if (turret.ChargeStartedTick < 0) turret.ChargeStartedTick = tick;
                    turret.ChargeProgress = Math.Min(1, (double)(tick - turret.ChargeStartedTick) / weapon.ChargeTicks);
                    if (tick - turret.ChargeStartedTick < weapon.ChargeTicks)
                    { turret.CombatState = TurretCombatState.Charging; continue; }
                }
                if (!world.Energy.TryConsume(turret.Id, weapon.EnergyPerShot))
                {
                    CancelCharge(turret);
                    turret.CombatState = TurretCombatState.WaitingForEnergy;
                    turret.EnergyWaitTicks++; continue;
                }
                turret.NextShotTick = checked(tick + weapon.CooldownTicks);
                turret.LastShotTick = tick;
                turret.ShotsFired++;
                turret.CombatState = TurretCombatState.Fired;
                CancelCharge(turret);
                var projectile = projectiles.Launch(aim, weapon, (double)tick / world.Settings.TicksPerSecond);
                shots.Add(new ShotSnapshot(nextShotId++, tick, projectile, target.Id));
                ObserveFriendlyAttack(turret, tick);
            }
        }
        internal void ResolveProjectileHit(ProjectileState projectile, EnemyState victim, double time, double x, double y, double z, int damageOverride = -1)
        {
            if (State != BattleState.Combat || !victim.IsAlive) return;
            int damage = Math.Min(victim.Health, damageOverride >= 0 ? damageOverride : projectile.Weapon.Damage);
            if (damage <= 0) return;
            victim.Health -= damage;
            victim.SampleMotion(time, out double enemyX, out double enemyZ);
            var hit = new HitContext(projectile, victim, x, y, z, enemyX, enemyZ);
            RecordEnemyDamage(victim, damage, hit, time, x, z, enemyX, enemyZ);
        }

        private void RecordEnemyDamage(EnemyState victim, int damage, HitContext hit, double time,
            double x, double z, double enemyX, double enemyZ)
        {
            world.Record(WorldEventKind.EnemyHit, victim.Id, x, z, damage, hit, time);
            if (!victim.IsAlive) world.Record(WorldEventKind.EnemyDeath, victim.Id, enemyX, enemyZ, hit: hit, time: time);
            var source = world.FindBuilding(hit.SourceBuildingId);
            if (source != null && source.LifeGeneration == hit.SourceGeneration)
            {
                source.DamageDealt += damage;
                if (source.IsAlive && !source.IsSold) behavior.ObserveDamage(source, enemyX, enemyZ, damage, world.CurrentTick);
            }
            if (!victim.IsAlive) { Kills++; if (!world.IsSandbox) PendingKillReward = checked(PendingKillReward + victim.Definition.KillReward); }
        }

        internal void ResetTurretAim(BuildingState building)
        {
            if (!building.IsAlive || building.IsSold) ClearResonance(building);
            CancelCharge(building); turretAims.Remove(building.Id);
            aimSnapshots.RemoveAll(value => value.TurretId == building.Id);
            building.CombatState = TurretCombatState.Searching;
        }
        private static void CancelCharge(BuildingState building)
        { building.ChargeStartedTick = -1; building.ChargeProgress = 0; }
        private void CancelAllCharges()
        {
            foreach (var building in world.Buildings)
            {
                CancelCharge(building);
                CancelResonanceBurst(building);
                building.NextResonanceBurstTick = 0;
                if (building.IsTurret) building.CombatState = TurretCombatState.Searching;
            }
            double end = (double)world.CurrentTick / world.Settings.TicksPerSecond;
            RefreshAimSnapshots(end, end);
        }
        private void RefreshAimSnapshots(double start, double end)
        {
            for (int index = 0; index < aimSnapshots.Count; index++)
                if (turretAims.TryGetValue(aimSnapshots[index].TurretId, out var aim))
                    aimSnapshots[index] = new TurretAimSnapshot(aim, start, end);
        }

        internal static bool InAttackRange(EnemyState enemy, BuildingState building)
        {
            double closestX = Math.Max(building.X, Math.Min(enemy.X, building.X + building.FootprintWidth));
            double closestZ = Math.Max(building.Z, Math.Min(enemy.Z, building.Z + building.FootprintDepth));
            double differenceX = enemy.X - closestX;
            double differenceZ = enemy.Z - closestZ;
            return differenceX * differenceX + differenceZ * differenceZ <= enemy.Definition.AttackRange * enemy.Definition.AttackRange;
        }

        private static bool Contains(int x, int z, int width, int depth, int cellX, int cellZ) =>
            cellX >= x && cellZ >= z && cellX < x + width && cellZ < z + depth;
    }
}
