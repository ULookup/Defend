using System;
using System.Collections.Generic;
using Defend.Simulation;
using UnityEngine;

namespace Defend.Unity
{
    /// <summary>可编辑的战斗、五类敌人和顺序生成组配置；旧字段保留以兼容已有资产。</summary>
    [CreateAssetMenu(menuName = "涌现防线/战斗配置", fileName = "BattleSettings")]
    public sealed partial class BattleSettingsAsset : ScriptableObject
    {
        [Serializable]
        private sealed class WaveAuthoring
        {
            [Min(1)] public int enemyCount;
            [Min(1)] public int enemyHealth;
            [Min(1)] public int spawnIntervalTicks;
            [Min(0)] public int clearReward;
            public EnemyKind[] enemyPattern = { EnemyKind.Normal };
            public int entranceIndex = -1;
            [TextArea] public string briefing = "";
            public WaveGroupAuthoring[] groups = Array.Empty<WaveGroupAuthoring>();
            public bool independentGroups;
            [Min(0)] public int plannedSpawnDurationTicks;
            public WaveAuthoring(int count, int health, int interval, int reward)
            { enemyCount = count; enemyHealth = health; spawnIntervalTicks = interval; clearReward = reward; }
        }
        [Serializable]
        private sealed class WaveGroupAuthoring
        {
            public EnemyKind kind;
            [Min(1)] public int count = 1;
            public int entranceIndex = -1;
            [Min(0)] public int startTick;
            [Min(1)] public int intervalTicks = 10;
        }
        [Serializable]
        private sealed class EnemyAuthoring
        {
            public EnemyKind kind;
            public string displayName;
            [TextArea] public string description;
            [Min(1)] public int baseHealth = 60;
            [Range(.1f, .49f)] public float radius = .32f;
            [Min(.1f)] public float speed = 1.8f;
            [Min(0)] public float bodyHeight;
            [Min(1)] public int attackDamage = 8;
            [Min(1)] public int attackIntervalTicks = 20;
            [Min(.1f)] public float attackRange = .58f;
            [Min(0)] public int killReward = 5;
            [Min(.1f)] public float relayPreference = 1;
            [Min(.1f)] public float reactorPreference = 1;
            [Min(0)] public float riskWeight = 8;
        }
        [SerializeField] private EnemyAuthoring[] enemyDefinitions = Array.Empty<EnemyAuthoring>();
        [Serializable]
        private sealed class TurretAuthoring
        {
            public BuildingKind kind;
            public float range = 7;
            public int damage = 20;
            public long energyPerShot = 2000;
            public int cooldownTicks = 8;
            public float splashRadius;
            [Min(.1f)] public float projectileSpeed = 20;
            [Min(0)] public float projectileRadius = .035f;
            [Min(0)] public float maximumFlightDistance;
            [Min(.1f)] public float maximumLifetimeSeconds = 3;
            [Min(1)] public float yawSpeedDegrees = 240;
            [Min(1)] public float pitchSpeedDegrees = 180;
            public float minimumPitchDegrees = -65;
            public float maximumPitchDegrees = 80;
            [Min(.01f)] public float alignmentToleranceDegrees = 2;
            [Min(0)] public float maximumLeadSeconds = .6f;
            [Min(.01f)] public float muzzleHeight = .75f;
            public float muzzleForwardOffset = .45f;
            public float muzzleRightOffset;
            public float pivotHeight = .65f;
            public float pivotForwardOffset;
            public float pivotRightOffset;
            [Min(0)] public float areaArcHeight = 1.8f;
            [Min(0)] public float minimumRange;
            [Range(.1f, 180)] public float fireArcHalfDegrees = 180;
            [Min(0)] public int chargeTicks;
            [Range(1, 100)] public int maximumPierceTargets = 1;
            [Range(.01f, 1)] public float pierceDamageMultiplier = 1;
        }
        [SerializeField] private TurretAuthoring[] turretDefinitions = Array.Empty<TurretAuthoring>();
        [SerializeField] private Vector2Int[] spawnPoints = Array.Empty<Vector2Int>();
        [Serializable]
        private sealed class EntranceAuthoring
        {
            public string displayName = "";
            [Min(1)] public int unlockWave = 1;
            [Min(0)] public int previewWaves = 2;
            public Vector2Int reservedOrigin;
            public Vector2Int reservedSize = Vector2Int.one;
        }
        [SerializeField] private EntranceAuthoring[] entranceMetadata = Array.Empty<EntranceAuthoring>();
        [SerializeField] private Vector2Int[] blockedCells = Array.Empty<Vector2Int>();
        [Header("场上数量上限")]
        [SerializeField, HideInInspector, Min(1)] private int maximumBuildings = 100;
        [SerializeField, Min(1)] private int maximumLivingEnemies = 200;
        [Header("敌人")]
        [SerializeField, Min(0.1f)] private float enemySpeedCellsPerSecond = 1.8f;
        [SerializeField, Min(1)] private int enemyAttackDamage = 8;
        [SerializeField, Min(1)] private int enemyAttackIntervalTicks = 20;
        [Header("全图目标判断") ]
        [SerializeField, HideInInspector] private bool enableEnemyBehavior;
        [SerializeField, HideInInspector, Min(0.1f)] private float enemySensingRadius = 8;
        [SerializeField, Min(0.1f)] private float enemyObservationMemorySeconds = 3;
        [SerializeField, Min(0.01f)] private float enemyEvaluationIntervalSeconds = 0.5f;
        [SerializeField, Min(0)] private float enemyMinimumTargetLockSeconds = 3;
        [SerializeField, Min(1)] private float enemySwitchScoreRatio = 1.35f;
        [SerializeField, Range(.1f, .99f)] private float enemyRouteImprovementRatio = .8f;
        [SerializeField, Min(.1f)] private float enemyBaseDestructionValue = 5;
        [SerializeField, Min(.1f)] private float enemyCoreValue = 20;
        [SerializeField, Min(.1f)] private float enemyFirepowerValue = 3;
        [SerializeField, Min(.1f)] private float enemySupplyValue = 6;
        [SerializeField, HideInInspector, Min(1)] private int enemyMaximumCandidates = 6;
        [SerializeField, Min(1)] private int enemyMaximumEvaluationsPerTick = 20;
        [SerializeField, HideInInspector, Min(1)] private int enemyMaximumPathBuildsPerTick = 2;
        [SerializeField, HideInInspector, Min(0)] private int enemyMaximumAdditionalTravelCells = 6;
        [Header("炮塔：一次攻击需要全额能量")]
        [SerializeField, Min(0.1f)] private float turretRange = 7;
        [SerializeField, Min(1)] private int turretDamage = 20;
        [SerializeField, Min(1)] private long turretEnergyPerShot = 2000;
        [SerializeField, Min(1)] private int turretCooldownTicks = 8;
        [SerializeField, Min(1)] private int shotDisplayTicks = 6;
        [SerializeField] private WaveAuthoring[] waves =
        {
            new WaveAuthoring(20, 60, 10, 300), new WaveAuthoring(35, 75, 9, 400),
            new WaveAuthoring(55, 90, 8, 500), new WaveAuthoring(80, 105, 7, 600),
            new WaveAuthoring(110, 120, 6, 800)
        };

        /// <summary>正式关卡内容入口：仅写入敌人、生成组与奖励，保留已认可的地图、入口及炮塔参数。</summary>
        public void ConfigureConfirmedGameplay()
        {
            maximumLivingEnemies = 200;
            enableEnemyBehavior = true;
            var definitions = new List<EnemyAuthoring>();
            foreach (EnemyKind kind in Enum.GetValues(typeof(EnemyKind)))
            {
                var enemy = EnemyDefinition.Default(kind);
                definitions.Add(new EnemyAuthoring { kind = kind, displayName = enemy.DisplayName, description = enemy.Description,
                    baseHealth = enemy.BaseHealth, radius = (float)enemy.Radius, speed = (float)enemy.Speed,
                    attackDamage = enemy.AttackDamage, attackIntervalTicks = enemy.AttackIntervalTicks,
                    attackRange = (float)enemy.AttackRange, killReward = enemy.KillReward,
                    relayPreference = (float)enemy.RelayPreference, reactorPreference = (float)enemy.ReactorPreference, riskWeight = (float)enemy.RiskWeight });
            }
            enemyDefinitions = definitions.ToArray();
            waves = new[]
            {
                ConfirmedWave(60, 120, "巡猎者沿入口靠近，观察工事火力与供能。", Group(EnemyKind.Normal, 8, 0), Group(EnemyKind.Fast, 4, 1)),
                ConfirmedWave(62, 145, "清扫虫群从另一入口集结，保持炮塔供能。", Group(EnemyKind.Normal, 6, 0), Group(EnemyKind.Fast, 10, 1)),
                ConfirmedWave(64, 165, "拆迁者加入进攻，留意中继网络的薄弱处。", Group(EnemyKind.Normal, 8, 1), Group(EnemyKind.Demolisher, 4, 0), Group(EnemyKind.Fast, 6, 1)),
                ConfirmedWave(68, 185, "收束者寻找反应堆，尝试用备用路线维持火力。", Group(EnemyKind.Fast, 10, 0), Group(EnemyKind.Converger, 4, 1), Group(EnemyKind.Normal, 6, 0)),
                ConfirmedWave(72, 205, "镇载者始终逼近核心，周围敌人会寻找其他突破口。", Group(EnemyKind.Normal, 8, 0), Group(EnemyKind.Heavy, 2, 1), Group(EnemyKind.Demolisher, 4, 0), Group(EnemyKind.Fast, 10, 1)),
                ConfirmedWave(76, 225, "两端网络同时承压，电池与炮塔库存能争取喘息。", Group(EnemyKind.Fast, 12, 1), Group(EnemyKind.Converger, 5, 0), Group(EnemyKind.Demolisher, 5, 1), Group(EnemyKind.Normal, 6, 0)),
                ConfirmedWave(80, 250, "混编敌群连续进攻，维修并保住关键供能节点。", Group(EnemyKind.Normal, 10, 0), Group(EnemyKind.Demolisher, 5, 1), Group(EnemyKind.Heavy, 3, 0), Group(EnemyKind.Converger, 4, 1), Group(EnemyKind.Fast, 10, 0)),
                ConfirmedWave(84, 300, "最后一波，五类守城者从两处入口依次入场。", Group(EnemyKind.Fast, 14, 1), Group(EnemyKind.Normal, 10, 0), Group(EnemyKind.Demolisher, 4, 1), Group(EnemyKind.Converger, 4, 0), Group(EnemyKind.Heavy, 4, 1))
            };
        }
        private static WaveGroupAuthoring Group(EnemyKind kind, int count, int entrance)
            => new WaveGroupAuthoring { kind = kind, count = count, entranceIndex = entrance, intervalTicks = kind == EnemyKind.Fast ? 8 : 16 };
        private static WaveAuthoring ConfirmedWave(int health, int reward, string briefing, params WaveGroupAuthoring[] groups)
        {
            int total = 0, next = 0;
            var pattern = new List<EnemyKind>();
            foreach (var group in groups) { group.startTick = next; next += group.count * group.intervalTicks; total += group.count; pattern.Add(group.kind); }
            return new WaveAuthoring(total, health, 16, reward) { briefing = briefing, groups = groups, enemyPattern = pattern.ToArray() };
        }

        private static double Positive(float value, double fallback) => value > 0 ? value : fallback;

        /// <summary>制作时从真实预制体提取零姿态的炮口及旋转枢轴，运行时模拟不读取 Transform。</summary>
        public void ConfigureTurretMuzzle(BuildingKind kind, float muzzleHeight, float muzzleForwardOffset,
            float pivotHeight, float muzzleRightOffset = 0, float pivotForwardOffset = 0, float pivotRightOffset = 0)
        {
            var list = new List<TurretAuthoring>(turretDefinitions ?? Array.Empty<TurretAuthoring>());
            var turret = list.Find(value => value.kind == kind);
            if (turret == null)
            {
                var definition = CreateSettings().GetTurret(kind);
                turret = new TurretAuthoring { kind = kind, range = (float)definition.Range, damage = definition.Damage,
                    energyPerShot = definition.EnergyPerShot, cooldownTicks = definition.CooldownTicks, splashRadius = (float)definition.SplashRadius,
                    projectileSpeed = kind == BuildingKind.AreaTurret ? 10 : kind == BuildingKind.BurstTurret ? 16 : 20,
                    maximumFlightDistance = (float)definition.MaximumFlightDistance,
                    yawSpeedDegrees = (float)definition.YawSpeedDegrees, pitchSpeedDegrees = (float)definition.PitchSpeedDegrees,
                    minimumPitchDegrees = (float)definition.MinimumPitchDegrees, maximumPitchDegrees = (float)definition.MaximumPitchDegrees,
                    alignmentToleranceDegrees = (float)definition.AlignmentToleranceDegrees, maximumLeadSeconds = (float)definition.MaximumLeadSeconds,
                    projectileRadius = (float)definition.ProjectileRadius, minimumRange = (float)definition.MinimumRange,
                    fireArcHalfDegrees = (float)definition.FireArcHalfDegrees, chargeTicks = definition.ChargeTicks,
                    maximumPierceTargets = definition.MaximumPierceTargets, pierceDamageMultiplier = (float)definition.PierceDamageMultiplier };
                list.Add(turret);
            }
            // 旧资产缺少飞行字段时，在制作阶段保存明确数值，运行时构造的兼容默认值仍保留。
            if (turret.projectileSpeed <= 0) turret.projectileSpeed = kind == BuildingKind.AreaTurret ? 10 : kind == BuildingKind.BurstTurret ? 16 : 20;
            if (turret.maximumFlightDistance <= 0) turret.maximumFlightDistance = turret.range + 3;
            turret.maximumLifetimeSeconds = Math.Max(turret.maximumLifetimeSeconds, turret.maximumFlightDistance / turret.projectileSpeed + .1f);
            turret.muzzleHeight = muzzleHeight; turret.muzzleForwardOffset = muzzleForwardOffset;
            turret.pivotHeight = pivotHeight; turret.muzzleRightOffset = muzzleRightOffset;
            turret.pivotForwardOffset = pivotForwardOffset; turret.pivotRightOffset = pivotRightOffset;
            turretDefinitions = list.ToArray();
        }

        /// <summary>五类敌人的简化受击高度由制作后的模型范围明确写入。</summary>
        public void ConfigureEnemyBodyHeight(EnemyKind kind, float bodyHeight)
        {
            var list = new List<EnemyAuthoring>(enemyDefinitions ?? Array.Empty<EnemyAuthoring>());
            var enemy = list.Find(value => value.kind == kind);
            if (enemy == null)
            {
                var definition = CreateSettings().GetEnemy(kind);
                enemy = new EnemyAuthoring { kind = kind, displayName = definition.DisplayName, description = definition.Description,
                    baseHealth = definition.BaseHealth, radius = (float)definition.Radius, speed = (float)definition.Speed,
                    attackDamage = definition.AttackDamage, attackIntervalTicks = definition.AttackIntervalTicks,
                    attackRange = (float)definition.AttackRange, killReward = definition.KillReward,
                    relayPreference = (float)definition.RelayPreference, reactorPreference = (float)definition.ReactorPreference, riskWeight = (float)definition.RiskWeight };
                list.Add(enemy);
            }
            enemy.bodyHeight = bodyHeight; enemyDefinitions = list.ToArray();
        }

        public BattleSettings CreateSettings()
        {
            List<WaveDefinition> definitions = new List<WaveDefinition>();
            foreach (WaveAuthoring wave in waves)
            {
                var groups = new List<WaveGroupDefinition>();
                if (wave.groups != null) foreach (var group in wave.groups)
                    groups.Add(new WaveGroupDefinition(group.kind, group.count, group.entranceIndex, group.startTick, group.intervalTicks));
                definitions.Add(new WaveDefinition(wave.enemyCount, wave.enemyHealth, wave.spawnIntervalTicks, wave.clearReward,
                    wave.enemyPattern, wave.entranceIndex, wave.briefing, groups.ToArray(), wave.independentGroups, wave.plannedSpawnDurationTicks));
            }
            var enemies = new List<EnemyDefinition>();
            if (enemyDefinitions != null) foreach (var enemy in enemyDefinitions)
                enemies.Add(new EnemyDefinition(enemy.kind, enemy.displayName, enemy.description, enemy.baseHealth, enemy.radius,
                    enemy.speed, enemy.attackDamage, enemy.attackIntervalTicks, enemy.attackRange, enemy.killReward,
                    enemy.relayPreference, enemy.reactorPreference, enemy.riskWeight, enemy.bodyHeight));
            var weapons = new List<TurretDefinition>();
            foreach (var turret in turretDefinitions) weapons.Add(new TurretDefinition(turret.kind, turret.range, turret.damage,
                turret.energyPerShot, turret.cooldownTicks, turret.splashRadius,
                Positive(turret.projectileSpeed, 20), Math.Max(0, turret.projectileRadius), Math.Max(0, turret.maximumFlightDistance),
                Positive(turret.maximumLifetimeSeconds, 3), Positive(turret.yawSpeedDegrees, 240), Positive(turret.pitchSpeedDegrees, 180),
                turret.minimumPitchDegrees == 0 && turret.maximumPitchDegrees == 0 ? -65 : turret.minimumPitchDegrees,
                turret.minimumPitchDegrees == 0 && turret.maximumPitchDegrees == 0 ? 80 : turret.maximumPitchDegrees,
                Positive(turret.alignmentToleranceDegrees, 2), Math.Max(0, turret.maximumLeadSeconds), Positive(turret.muzzleHeight, .75),
                turret.muzzleForwardOffset, turret.pivotHeight, Math.Max(0, turret.areaArcHeight), turret.muzzleRightOffset,
                turret.pivotForwardOffset, turret.pivotRightOffset, Math.Max(0, turret.minimumRange),
                Positive(turret.fireArcHalfDegrees, 180), Math.Max(0, turret.chargeTicks), Math.Max(1, turret.maximumPierceTargets),
                Positive(turret.pierceDamageMultiplier, 1)));
            var entrances = new List<SpawnPoint>();
            for (int index = 0; index < spawnPoints.Length; index++)
            {
                var point = spawnPoints[index];
                var metadata = entranceMetadata != null && index < entranceMetadata.Length ? entranceMetadata[index] : null;
                entrances.Add(metadata == null ? new SpawnPoint(point.x, point.y) :
                    new SpawnPoint(point.x, point.y, metadata.displayName, metadata.unlockWave, metadata.previewWaves,
                        metadata.reservedOrigin.x, metadata.reservedOrigin.y, metadata.reservedSize.x, metadata.reservedSize.y));
            }
            var terrain = new List<SpawnPoint>();
            foreach (var point in blockedCells) terrain.Add(new SpawnPoint(point.x, point.y));
            return new BattleSettings(definitions, maximumBuildings, maximumLivingEnemies, enemySpeedCellsPerSecond,
                enemyAttackDamage, enemyAttackIntervalTicks, turretRange, turretDamage, turretEnergyPerShot,
                turretCooldownTicks, shotDisplayTicks, entrances, weapons, terrain,
                enemyBehavior: new EnemyBehaviorSettings(enabled: enableEnemyBehavior,
                    sensingRadius: enemySensingRadius, observationMemorySeconds: enemyObservationMemorySeconds,
                    evaluationIntervalSeconds: enemyEvaluationIntervalSeconds, minimumTargetLockSeconds: enemyMinimumTargetLockSeconds,
                    switchScoreRatio: enemySwitchScoreRatio, maximumCandidates: enemyMaximumCandidates,
                    maximumEvaluationsPerTick: enemyMaximumEvaluationsPerTick, maximumPathBuildsPerTick: enemyMaximumPathBuildsPerTick,
                    maximumAdditionalTravelCells: enemyMaximumAdditionalTravelCells, routeImprovementRatio: enemyRouteImprovementRatio, baseDestructionValue: enemyBaseDestructionValue, coreValue: enemyCoreValue,
                    firepowerValue: enemyFirepowerValue, supplyValue: enemySupplyValue), enemyDefinitions: enemies);
        }
    }
}
