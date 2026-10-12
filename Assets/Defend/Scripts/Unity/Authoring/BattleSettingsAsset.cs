using System;
using System.Collections.Generic;
using Defend.Simulation;
using UnityEngine;

namespace Defend.Unity
{
    /// <summary>可编辑的战斗、五类敌人和顺序生成组配置；旧字段保留以兼容已有资产。</summary>
    [CreateAssetMenu(menuName = "涌现防线/战斗配置", fileName = "BattleSettings")]
    public sealed class BattleSettingsAsset : ScriptableObject
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
        }
        [SerializeField] private TurretAuthoring[] turretDefinitions = Array.Empty<TurretAuthoring>();
        [SerializeField] private Vector2Int[] spawnPoints = Array.Empty<Vector2Int>();
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

        public BattleSettings CreateSettings()
        {
            List<WaveDefinition> definitions = new List<WaveDefinition>();
            foreach (WaveAuthoring wave in waves)
            {
                var groups = new List<WaveGroupDefinition>();
                if (wave.groups != null) foreach (var group in wave.groups)
                    groups.Add(new WaveGroupDefinition(group.kind, group.count, group.entranceIndex, group.startTick, group.intervalTicks));
                definitions.Add(new WaveDefinition(wave.enemyCount, wave.enemyHealth, wave.spawnIntervalTicks, wave.clearReward,
                    wave.enemyPattern, wave.entranceIndex, wave.briefing, groups.ToArray()));
            }
            var enemies = new List<EnemyDefinition>();
            if (enemyDefinitions != null) foreach (var enemy in enemyDefinitions)
                enemies.Add(new EnemyDefinition(enemy.kind, enemy.displayName, enemy.description, enemy.baseHealth, enemy.radius,
                    enemy.speed, enemy.attackDamage, enemy.attackIntervalTicks, enemy.attackRange, enemy.killReward,
                    enemy.relayPreference, enemy.reactorPreference, enemy.riskWeight));
            var weapons = new List<TurretDefinition>();
            foreach (var turret in turretDefinitions) weapons.Add(new TurretDefinition(turret.kind, turret.range, turret.damage,
                turret.energyPerShot, turret.cooldownTicks, turret.splashRadius));
            var entrances = new List<SpawnPoint>();
            foreach (var point in spawnPoints) entrances.Add(new SpawnPoint(point.x, point.y));
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
