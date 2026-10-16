using System;
using System.Collections.Generic;
using Defend.Simulation;
using UnityEngine;

namespace Defend.Unity
{
    public sealed partial class BattleSettingsAsset
    {
        /// <summary>保存完整正式关卡计划，同时保留已制作的敌人模型高度、行为与炮塔参数。</summary>
        public void ConfigureFormalLevel(int ticksPerSecond = 20)
        {
            var previous = CreateSettings();
            maximumLivingEnemies = FormalLevelDefinition.MaximumLivingEnemies;
            enableEnemyBehavior = true;
            var enemyList = new List<EnemyAuthoring>();
            foreach (EnemyKind kind in Enum.GetValues(typeof(EnemyKind)))
            {
                var enemy = previous.GetEnemy(kind);
                enemyList.Add(new EnemyAuthoring { kind = kind, displayName = enemy.DisplayName, description = enemy.Description,
                    baseHealth = enemy.BaseHealth, radius = (float)enemy.Radius, speed = (float)enemy.Speed, bodyHeight = (float)enemy.BodyHeight,
                    attackDamage = enemy.AttackDamage, attackIntervalTicks = enemy.AttackIntervalTicks,
                    attackRange = (float)enemy.AttackRange, killReward = FormalLevelDefinition.KillReward(kind),
                    relayPreference = (float)enemy.RelayPreference, reactorPreference = (float)enemy.ReactorPreference, riskWeight = (float)enemy.RiskWeight });
            }
            enemyDefinitions = enemyList.ToArray();
            var entrances = FormalLevelDefinition.CreateEntrances();
            spawnPoints = new Vector2Int[entrances.Length]; entranceMetadata = new EntranceAuthoring[entrances.Length];
            for (int i = 0; i < entrances.Length; i++)
            {
                var entrance = entrances[i]; spawnPoints[i] = new Vector2Int(entrance.X, entrance.Z);
                entranceMetadata[i] = new EntranceAuthoring { displayName = entrance.DisplayName, unlockWave = entrance.UnlockWave,
                    previewWaves = entrance.PreviewWaves, reservedOrigin = new Vector2Int(entrance.ReservedX, entrance.ReservedZ),
                    reservedSize = new Vector2Int(entrance.ReservedWidth, entrance.ReservedDepth) };
            }
            var terrain = new List<Vector2Int>();
            foreach (var cell in blockedCells)
            {
                bool reserved = false;
                foreach (var entrance in entrances) reserved |= entrance.OverlapsReservedArea(cell.x, cell.y, 1, 1);
                if (!reserved) terrain.Add(cell);
            }
            blockedCells = terrain.ToArray();
            var plan = FormalLevelDefinition.CreateWaves(ticksPerSecond);
            waves = new WaveAuthoring[plan.Length];
            for (int i = 0; i < plan.Length; i++)
            {
                var wave = plan[i]; var groups = new WaveGroupAuthoring[wave.Groups.Count];
                for (int j = 0; j < groups.Length; j++)
                {
                    var group = wave.Groups[j];
                    groups[j] = new WaveGroupAuthoring { kind = group.Kind, count = group.Count, entranceIndex = group.EntranceIndex,
                        startTick = group.StartTick, intervalTicks = group.IntervalTicks };
                }
                waves[i] = new WaveAuthoring(wave.EnemyCount, wave.EnemyHealth, wave.SpawnIntervalTicks, wave.ClearReward) {
                    briefing = wave.Briefing, groups = groups, independentGroups = true,
                    plannedSpawnDurationTicks = wave.PlannedSpawnDurationTicks };
            }
        }
    }
}
