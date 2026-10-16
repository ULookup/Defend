using System;
using System.Collections.Generic;

namespace Defend.Simulation
{
    /// <summary>可由 Unity 配置资产转换的白盒战斗参数，不代表最终平衡。</summary>
    public sealed class BattleSettings
    {
        public IReadOnlyList<WaveDefinition> Waves { get; }
        public IReadOnlyList<SpawnPoint> SpawnPoints { get; }
        public int MaximumBuildings { get; }
        public int MaximumLivingEnemies { get; }
        public double EnemySpeedCellsPerSecond { get; }
        public int EnemyAttackDamage { get; }
        public int EnemyAttackIntervalTicks { get; }
        public double TurretRange { get; }
        public int TurretDamage { get; }
        public long TurretEnergyPerShot { get; }
        public int TurretCooldownTicks { get; }
        public int ShotDisplayTicks { get; }
        public IReadOnlyList<SpawnPoint> BlockedCells { get; }
        public EnemyBehaviorSettings EnemyBehavior { get; }
        private readonly Dictionary<EnemyKind, EnemyDefinition> enemies = new Dictionary<EnemyKind, EnemyDefinition>();
        public EnemyDefinition GetEnemy(EnemyKind kind) => enemies[kind];
        private readonly Dictionary<BuildingKind, TurretDefinition> turrets = new Dictionary<BuildingKind, TurretDefinition>();
        public TurretDefinition GetTurret(BuildingKind kind) => turrets[kind];

        public BattleSettings(IEnumerable<WaveDefinition> waves = null, int maximumBuildings = 100,
            int maximumLivingEnemies = 200, double enemySpeedCellsPerSecond = 1.8,
            int enemyAttackDamage = 8, int enemyAttackIntervalTicks = 20, double turretRange = 7,
            int turretDamage = 20, long turretEnergyPerShot = 2000, int turretCooldownTicks = 8,
            int shotDisplayTicks = 6, IEnumerable<SpawnPoint> spawnPoints = null,
            IEnumerable<TurretDefinition> turretDefinitions = null, IEnumerable<SpawnPoint> blockedCells = null,
            EnemyBehaviorSettings enemyBehavior = null, IEnumerable<EnemyDefinition> enemyDefinitions = null)
        {
            if (maximumBuildings <= 0 || maximumBuildings > 10000) throw new ArgumentOutOfRangeException(nameof(maximumBuildings));
            if (maximumLivingEnemies <= 0 || maximumLivingEnemies > 10000) throw new ArgumentOutOfRangeException(nameof(maximumLivingEnemies));
            if (double.IsNaN(enemySpeedCellsPerSecond) || double.IsInfinity(enemySpeedCellsPerSecond) || enemySpeedCellsPerSecond <= 0 || enemySpeedCellsPerSecond > 100)
                throw new ArgumentOutOfRangeException(nameof(enemySpeedCellsPerSecond));
            if (enemyAttackDamage <= 0) throw new ArgumentOutOfRangeException(nameof(enemyAttackDamage));
            if (enemyAttackIntervalTicks <= 0) throw new ArgumentOutOfRangeException(nameof(enemyAttackIntervalTicks));
            if (double.IsNaN(turretRange) || double.IsInfinity(turretRange) || turretRange <= 0 || turretRange > 1024)
                throw new ArgumentOutOfRangeException(nameof(turretRange));
            if (turretDamage <= 0) throw new ArgumentOutOfRangeException(nameof(turretDamage));
            if (turretEnergyPerShot <= 0 || turretEnergyPerShot > long.MaxValue / 1000000) throw new ArgumentOutOfRangeException(nameof(turretEnergyPerShot));
            if (turretCooldownTicks <= 0) throw new ArgumentOutOfRangeException(nameof(turretCooldownTicks));
            if (shotDisplayTicks <= 0 || shotDisplayTicks > 1000) throw new ArgumentOutOfRangeException(nameof(shotDisplayTicks));

            var configuredWaves = waves == null ? new List<WaveDefinition>
            {
                new WaveDefinition(20, 60, 10, 300), new WaveDefinition(35, 75, 9, 400),
                new WaveDefinition(55, 90, 8, 500), new WaveDefinition(80, 105, 7, 600),
                new WaveDefinition(110, 120, 6, 800)
            } : new List<WaveDefinition>(waves);
            if (configuredWaves.Count == 0 || configuredWaves.Exists(wave => wave == null))
                throw new ArgumentException("战斗至少需要一波有效的敌人配置。", nameof(waves));
            Waves = configuredWaves.AsReadOnly();
            SpawnPoints = new List<SpawnPoint>(spawnPoints ?? Array.Empty<SpawnPoint>()).AsReadOnly();
            for (int waveIndex = 0; waveIndex < Waves.Count; waveIndex++)
                if (Waves[waveIndex].IndependentGroups)
                    foreach (var group in Waves[waveIndex].Groups)
                    {
                        if (group.EntranceIndex < 0 || group.EntranceIndex >= SpawnPoints.Count)
                            throw new ArgumentException("独立生成队伍必须引用有效入口。", nameof(waves));
                        if (!SpawnPoints[group.EntranceIndex].IsOpenAtWave(waveIndex + 1))
                            throw new ArgumentException("队伍不能早于入口开放波次生成。", nameof(waves));
                    }
            MaximumBuildings = maximumBuildings;
            MaximumLivingEnemies = maximumLivingEnemies;
            EnemySpeedCellsPerSecond = enemySpeedCellsPerSecond;
            EnemyAttackDamage = enemyAttackDamage;
            EnemyAttackIntervalTicks = enemyAttackIntervalTicks;
            TurretRange = turretRange;
            TurretDamage = turretDamage;
            TurretEnergyPerShot = turretEnergyPerShot;
            TurretCooldownTicks = turretCooldownTicks;
            ShotDisplayTicks = shotDisplayTicks;
            BlockedCells = new List<SpawnPoint>(blockedCells ?? Array.Empty<SpawnPoint>()).AsReadOnly();
            EnemyBehavior = enemyBehavior ?? new EnemyBehaviorSettings();
            foreach (EnemyKind kind in Enum.GetValues(typeof(EnemyKind)))
                enemies.Add(kind, EnemyDefinition.Default(kind, enemySpeedCellsPerSecond, enemyAttackDamage, enemyAttackIntervalTicks));
            if (enemyDefinitions != null) foreach (var enemy in enemyDefinitions) enemies[enemy.Kind] = enemy;
            turrets.Add(BuildingKind.Turret, new TurretDefinition(BuildingKind.Turret, turretRange, turretDamage, turretEnergyPerShot, turretCooldownTicks));
            turrets.Add(BuildingKind.BurstTurret, new TurretDefinition(BuildingKind.BurstTurret, 9, 100, 10000, 50));
            turrets.Add(BuildingKind.AreaTurret, new TurretDefinition(BuildingKind.AreaTurret, 7, 32, 8000, 32, 2.2));
            turrets.Add(BuildingKind.PiercingRailgun, new TurretDefinition(BuildingKind.PiercingRailgun, 14, 180, 12000, 72,
                projectileRadius: .06, maximumFlightDistance: 14, yawSpeedDegrees: 35, pitchSpeedDegrees: 30,
                minimumPitchDegrees: -12, maximumPitchDegrees: 12, alignmentToleranceDegrees: .35, maximumLeadSeconds: 0,
                muzzleHeight: 1.07, muzzleForwardOffset: 1.79, pivotHeight: 1.04, pivotForwardOffset: -.62,
                minimumRange: 3.5, fireArcHalfDegrees: 25, chargeTicks: 24, maximumPierceTargets: 5, pierceDamageMultiplier: .8));
            if (turretDefinitions != null) foreach (var turret in turretDefinitions) turrets[turret.Kind] = turret;
        }
    }
}
