using System;
using System.Collections.Generic;

namespace Defend.Simulation
{
    /// <summary>拥有一局的敌人、射击和波次状态；建筑伤害及经济事务始终交给同一个世界。</summary>
    public sealed class BattleSystem
    {
        private const int MaximumShotRecords = 2048;
        private readonly GameWorld world;
        private readonly BuildingGrid grid;
        private readonly EnemyNavigation navigation;
        private readonly EnemyBehaviorSystem behavior;
        private readonly EnemyMovementSystem movement;
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
        public IReadOnlyList<SpawnPoint> SpawnPoints { get; }
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
                if (point.X < 0 || point.Z < 0 || point.X >= grid.Width || point.Z >= grid.Depth || grid.Find(point.X, point.Z) < 0)
                    throw new ArgumentException("敌人出生位置必须位于地图内。", nameof(settings));
            SpawnPoints = spawnPoints.AsReadOnly();
            Enemies = enemies.AsReadOnly();
            Shots = shots.AsReadOnly();
            navigation = new EnemyNavigation(world, grid);
            behavior = new EnemyBehaviorSystem(world, grid, this, navigation);
            movement = new EnemyMovementSystem(world, grid, this);
        }

        internal CommandResult StartWave(long sequence)
        {
            if (State != BattleState.Preparing)
                return new CommandResult(sequence, false, "当前不能开始下一波。", 0);
            BuildingState core = world.FindBuilding(CoreBuildingId);
            if (core == null || !core.IsAlive || core.IsSold)
                return new CommandResult(sequence, false, "需要一座完好的核心才能开始战斗。", 0);
            WaveNumber++;
            RemainingToSpawn = Settings.Waves[WaveNumber - 1].EnemyCount;
            LastWaveReward = LastWaveFixedReward = LastWaveKillReward = PendingKillReward = 0;
            groupIndex = groupSpawned = 0; spawnDelay = 0; waveStartTick = world.CurrentTick;
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
            if (building.Id == CoreBuildingId) { State = BattleState.Defeat; PendingKillReward = 0; }
        }

        internal string ValidatePlacement(int x, int z, int width, int depth)
        {
            foreach (SpawnPoint spawn in SpawnPoints)
                if (Contains(x, z, width, depth, spawn.X, spawn.Z)) return "不能占用敌人的出生位置。";
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
            int expired = 0;
            while (expired < shots.Count && tick - shots[expired].Tick >= Settings.ShotDisplayTicks) expired++;
            if (expired > 0) shots.RemoveRange(0, expired);
            if (State != BattleState.Combat) return;

            CombatTicks++;
            // 同一步先结算炮塔，再移动及结算仍存活的敌人；排序均为稳定创建顺序。
            StepTurrets(tick);
            BuildingState core = world.FindBuilding(CoreBuildingId);
            behavior.Evaluate(core, tick);
            for (int index = 0; index < enemies.Count && State == BattleState.Combat; index++)
                if (enemies[index].IsAlive) movement.Step(enemies[index], tick);
            for (int index = enemies.Count - 1; index >= 0; index--)
                if (!enemies[index].IsAlive) enemies.RemoveAt(index);
        }

        internal void CompleteStep(long tick)
        {
            if (State != BattleState.Combat) return;
            WaveDefinition wave = Settings.Waves[WaveNumber - 1];
            if (RemainingToSpawn > 0)
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
            LastWaveFixedReward = wave.ClearReward;
            LastWaveKillReward = PendingKillReward;
            LastWaveBuildingLosses = world.DestroyedBuildingCount - lossesAtWaveStart;
            PendingKillReward = 0;
            LastWaveReward = world.GrantBattleReward(checked(LastWaveFixedReward + LastWaveKillReward));
        }

        private void StepTurrets(long tick)
        {
            foreach (BuildingState turret in world.Buildings)
            {
                if (!turret.IsTurret || !turret.IsAlive || turret.IsSold) continue;
                TurretDefinition weapon = Settings.GetTurret(turret.Kind);
                double rangeSquared = weapon.Range * weapon.Range;
                if (tick < turret.NextShotTick)
                {
                    turret.CombatState = TurretCombatState.CoolingDown;
                    continue;
                }
                EnemyState nearest = null;
                double nearestDistance = rangeSquared;
                foreach (EnemyState enemy in enemies)
                {
                    if (!enemy.IsAlive) continue;
                    double differenceX = enemy.X - turret.CenterX;
                    double differenceZ = enemy.Z - turret.CenterZ;
                    double distance = differenceX * differenceX + differenceZ * differenceZ;
                    if (distance > nearestDistance || (distance == nearestDistance && nearest != null && enemy.Id > nearest.Id)) continue;
                    nearest = enemy;
                    nearestDistance = distance;
                }
                if (nearest == null)
                {
                    turret.CombatState = TurretCombatState.Searching;
                    continue;
                }
                if (!world.Energy.TryConsume(turret.Id, weapon.EnergyPerShot))
                {
                    turret.CombatState = TurretCombatState.WaitingForEnergy;
                    turret.EnergyWaitTicks++;
                    continue;
                }
                // 射击只在完整扣能成功后发生。缺能期间不滚动冷却，也不积累以后补射的次数。
                turret.NextShotTick = checked(tick + weapon.CooldownTicks);
                turret.ShotsFired++;
                turret.CombatState = TurretCombatState.Fired;
                if (shots.Count == MaximumShotRecords) shots.RemoveAt(0);
                shots.Add(new ShotSnapshot(nextShotId++, tick, turret, nearest, weapon.SplashRadius));
                // 范围攻击按目标位置结算，不受表现、帧率或碰撞回调影响；实际伤害不包含过量伤害。
                foreach (EnemyState victim in enemies)
                {
                    if (!victim.IsAlive) continue;
                    double x = victim.X - nearest.X, z = victim.Z - nearest.Z;
                    if (victim != nearest && (weapon.SplashRadius <= 0 || x * x + z * z > weapon.SplashRadius * weapon.SplashRadius)) continue;
                    int damage = Math.Min(victim.Health, weapon.Damage);
                    victim.Health -= damage;
                    world.Record(WorldEventKind.EnemyHit, victim.Id, victim.X, victim.Z, damage);
                    if (!victim.IsAlive) world.Record(WorldEventKind.EnemyDeath, victim.Id, victim.X, victim.Z);
                    turret.DamageDealt += damage;
                    behavior.ObserveDamage(turret, victim.X, victim.Z, damage, tick);
                    if (!victim.IsAlive) { Kills++; PendingKillReward = checked(PendingKillReward + victim.Definition.KillReward); }
                }
            }
        }

        internal static bool InAttackRange(EnemyState enemy, BuildingState building)
        {
            double closestX = Math.Max(building.X, Math.Min(enemy.X, building.X + building.Definition.Width));
            double closestZ = Math.Max(building.Z, Math.Min(enemy.Z, building.Z + building.Definition.Depth));
            double differenceX = enemy.X - closestX;
            double differenceZ = enemy.Z - closestZ;
            return differenceX * differenceX + differenceZ * differenceZ <= enemy.Definition.AttackRange * enemy.Definition.AttackRange;
        }

        private static bool Contains(int x, int z, int width, int depth, int cellX, int cellZ) =>
            cellX >= x && cellZ >= z && cellX < x + width && cellZ < z + depth;
    }
}
