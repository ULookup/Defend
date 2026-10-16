using System;
using System.Collections.Generic;

namespace Defend.Simulation
{
    /// <summary>已应用的不可变队伍；编辑者构造新值，再由战斗系统一次校验并替换。</summary>
    public sealed class SandboxEnemyGroup
    {
        public long Id { get; }
        public EnemyKind Kind { get; }
        public int Health { get; }
        public double Speed { get; }
        public int Damage { get; }
        public int AttackIntervalTicks { get; }
        public long StartTick { get; }
        public int SpawnIntervalTicks { get; }
        public IReadOnlyList<SandboxSpawnAllocation> Allocations { get; }
        public int Count { get; }

        public SandboxEnemyGroup(long id, EnemyKind kind, int health, double speed, int damage,
            int attackIntervalTicks, long startTick, int spawnIntervalTicks, IEnumerable<SandboxSpawnAllocation> allocations)
        {
            Id = id; Kind = kind; Health = health; Speed = speed; Damage = damage;
            AttackIntervalTicks = attackIntervalTicks; StartTick = startTick; SpawnIntervalTicks = spawnIntervalTicks;
            var values = new List<SandboxSpawnAllocation>(allocations ?? Array.Empty<SandboxSpawnAllocation>());
            Allocations = values.AsReadOnly();
            long total = 0;
            foreach (var value in values) total += value.Count;
            // 无效草稿仍可表达，权威入口给出中文校验反馈，不在界面构造阶段溢出。
            Count = total < int.MinValue ? int.MinValue : total > int.MaxValue ? int.MaxValue : (int)total;
        }

        public SandboxEnemyGroup Copy(long id) => new SandboxEnemyGroup(id, Kind, Health, Speed, Damage,
            AttackIntervalTicks, StartTick, SpawnIntervalTicks, Allocations);

        public static IReadOnlyList<SandboxSpawnAllocation> DivideEvenly(int total, IReadOnlyList<long> spawnPointIds)
        {
            if (total < 1 || spawnPointIds == null || spawnPointIds.Count == 0)
                throw new ArgumentException("均分需要正整数数量和至少一个出生点。");
            var result = new List<SandboxSpawnAllocation>();
            for (int index = 0; index < spawnPointIds.Count; index++)
                result.Add(new SandboxSpawnAllocation(spawnPointIds[index], total / spawnPointIds.Count + (index < total % spawnPointIds.Count ? 1 : 0)));
            return result.AsReadOnly();
        }
    }
}
