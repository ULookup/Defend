namespace Defend.Simulation
{
    /// <summary>身份与显示顺序分离；删除后其他出生点不会继承该身份。</summary>
    public sealed class SandboxSpawnPoint
    {
        public long Id { get; }
        public int X { get; }
        public int Z { get; }
        public string Name { get; }
        public SandboxSpawnPoint(long id, int x, int z, string name = null)
        { Id = id; X = x; Z = z; Name = name ?? "出生点 " + id; }
    }

    public readonly struct SandboxSpawnAllocation
    {
        public long SpawnPointId { get; }
        public int Count { get; }
        public SandboxSpawnAllocation(long spawnPointId, int count)
        { SpawnPointId = spawnPointId; Count = count; }
    }
}
