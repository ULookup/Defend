using System;

namespace Defend.Simulation
{
    /// <summary>单波只读配置。生成计划只控制敌人，不清空或补满能源网络。</summary>
    public sealed class WaveDefinition
    {
        public int EnemyCount { get; }
        public int EnemyHealth { get; }
        public int SpawnIntervalTicks { get; }
        public int ClearReward { get; }
        public string Briefing { get; }
        public System.Collections.Generic.IReadOnlyList<EnemyKind> EnemyPattern { get; }
        public int EntranceIndex { get; }
        public System.Collections.Generic.IReadOnlyList<WaveGroupDefinition> Groups { get; }

        public WaveDefinition(int enemyCount = 20, int enemyHealth = 60,
            int spawnIntervalTicks = 10, int clearReward = 300, EnemyKind[] enemyPattern = null,
            int entranceIndex = -1, string briefing = "", WaveGroupDefinition[] groups = null)
        {
            if (enemyCount <= 0 || enemyCount > 100000) throw new ArgumentOutOfRangeException(nameof(enemyCount));
            if (enemyHealth <= 0) throw new ArgumentOutOfRangeException(nameof(enemyHealth));
            if (spawnIntervalTicks <= 0) throw new ArgumentOutOfRangeException(nameof(spawnIntervalTicks));
            if (clearReward < 0) throw new ArgumentOutOfRangeException(nameof(clearReward));
            if (groups != null && groups.Length > 0)
            {
                int total = 0;
                foreach (var group in groups) { if (group == null) throw new ArgumentException("生成组不能为空。"); total = checked(total + group.Count); }
                EnemyCount = total;
                Groups = Array.AsReadOnly((WaveGroupDefinition[])groups.Clone());
            }
            else { EnemyCount = enemyCount; Groups = Array.Empty<WaveGroupDefinition>(); }
            EnemyHealth = enemyHealth;
            SpawnIntervalTicks = spawnIntervalTicks;
            ClearReward = clearReward;
            var pattern = enemyPattern == null || enemyPattern.Length == 0 ? new[] { EnemyKind.Normal } : (EnemyKind[])enemyPattern.Clone();
            foreach (var kind in pattern) if (!Enum.IsDefined(typeof(EnemyKind), kind)) throw new ArgumentException("无效敌人类型。");
            EnemyPattern = Array.AsReadOnly(pattern);
            EntranceIndex = entranceIndex;
            Briefing = briefing ?? "";
        }
    }

    /// <summary>数组顺序就是生成顺序；开始时间以开波为零，受阻后的后续时间整体顺延。</summary>
    public sealed class WaveGroupDefinition
    {
        public EnemyKind Kind { get; }
        public int Count { get; }
        public int EntranceIndex { get; }
        public int StartTick { get; }
        public int IntervalTicks { get; }
        public WaveGroupDefinition(EnemyKind kind, int count, int entranceIndex = -1, int startTick = 0, int intervalTicks = 10)
        {
            if (!Enum.IsDefined(typeof(EnemyKind), kind) || count <= 0 || startTick < 0 || intervalTicks <= 0) throw new ArgumentOutOfRangeException(nameof(count));
            Kind = kind; Count = count; EntranceIndex = entranceIndex; StartTick = startTick; IntervalTicks = intervalTicks;
        }
    }

    public enum BattleState { Preparing, Combat, WaveCleared, Victory, Defeat }

    public readonly struct SpawnPoint
    {
        public int X { get; }
        public int Z { get; }
        public SpawnPoint(int x, int z) { X = x; Z = z; }
    }
}
