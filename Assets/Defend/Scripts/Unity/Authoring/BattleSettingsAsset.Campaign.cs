using System;
using System.Collections.Generic;
using Defend.Simulation;
using UnityEngine;

namespace Defend.Unity
{
    public sealed partial class BattleSettingsAsset
    {
        [Serializable]
        private struct CampaignWallBox
        {
            public float x, z, width, depth, height, baseHeight;
            public WallDefinition Create() => new WallDefinition(x, z, width, depth, height, baseHeight);
        }
        [SerializeField] private CampaignWallBox[] campaignWalls = Array.Empty<CampaignWallBox>();
        public int WaveCount => waves == null ? 0 : waves.Length;
        private IEnumerable<WallDefinition> CreateCampaignWalls()
        { foreach (var wall in campaignWalls) yield return wall.Create(); }

        /// <summary>写入关卡空间和波次，保留当前敌人、炮塔、行为与模型几何参数。</summary>
        public void ConfigureCampaign(IReadOnlyList<WaveDefinition> plan, IReadOnlyList<SpawnPoint> entrances,
            IReadOnlyList<CampaignWallRectangle> rectangles, int livingLimit)
        {
            maximumLivingEnemies = livingLimit;
            spawnPoints = new Vector2Int[entrances.Count]; entranceMetadata = new EntranceAuthoring[entrances.Count];
            for (int i = 0; i < entrances.Count; i++)
            {
                var point = entrances[i]; spawnPoints[i] = new Vector2Int(point.X, point.Z);
                entranceMetadata[i] = new EntranceAuthoring { displayName = point.DisplayName, unlockWave = point.UnlockWave,
                    previewWaves = point.PreviewWaves, reservedOrigin = new Vector2Int(point.ReservedX, point.ReservedZ),
                    reservedSize = new Vector2Int(point.ReservedWidth, point.ReservedDepth) };
            }
            var blocked = new List<Vector2Int>();
            foreach (var rect in rectangles)
            {
                for (int z = rect.Z; z < rect.Z + rect.Depth; z++)
                    for (int x = rect.X; x < rect.X + rect.Width; x++) blocked.Add(new Vector2Int(x, z));
            }
            blockedCells = blocked.ToArray();
            waves = new WaveAuthoring[plan.Count];
            for (int i = 0; i < plan.Count; i++)
            {
                var wave = plan[i]; var groups = new WaveGroupAuthoring[wave.Groups.Count];
                for (int j = 0; j < groups.Length; j++)
                {
                    var group = wave.Groups[j]; groups[j] = new WaveGroupAuthoring { kind = group.Kind, count = group.Count,
                        entranceIndex = group.EntranceIndex, startTick = group.StartTick, intervalTicks = group.IntervalTicks };
                }
                waves[i] = new WaveAuthoring(wave.EnemyCount, wave.EnemyHealth, wave.SpawnIntervalTicks, wave.ClearReward)
                    { briefing = wave.Briefing, groups = groups, independentGroups = true, plannedSpawnDurationTicks = wave.PlannedSpawnDurationTicks };
            }
        }
        public void ConfigureCampaignWalls(IReadOnlyList<WallDefinition> walls)
        {
            campaignWalls = new CampaignWallBox[walls.Count];
            for (int i = 0; i < walls.Count; i++)
            {
                var box = walls[i]; campaignWalls[i] = new CampaignWallBox { x = (float)box.X, z = (float)box.Z,
                    width = (float)box.Width, depth = (float)box.Depth, height = (float)box.Height, baseHeight = (float)box.BaseHeight };
            }
        }
    }
}
