using System.Collections.Generic;

namespace Defend.Simulation
{
    /// <summary>开战时锁定的队伍与坐标。计划只读，剩余配额和时钟保存在独立调度状态中。</summary>
    public sealed class SandboxWavePlan
    {
        public IReadOnlyList<SandboxEnemyGroup> Groups { get; }
        public IReadOnlyList<SandboxSpawnPoint> SpawnPoints { get; }
        public int MaximumLivingEnemies { get; }
        public int EnemyCount { get; }
        internal SandboxWavePlan(IEnumerable<SandboxEnemyGroup> groups, IEnumerable<SandboxSpawnPoint> points, int limit)
        {
            Groups = new List<SandboxEnemyGroup>(groups).AsReadOnly();
            SpawnPoints = new List<SandboxSpawnPoint>(points).AsReadOnly(); MaximumLivingEnemies = limit;
            foreach (var group in Groups) EnemyCount += group.Count;
        }
    }
}
