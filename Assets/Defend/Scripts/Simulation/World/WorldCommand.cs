namespace Defend.Simulation
{
    public enum WorldCommandKind
    {
        Place,
        Move,
        Sell,
        Damage,
        Repair,
        SetPriority,
        SetTestLoad,
        StartWave,
        RepairAll,
        ContinuePreparing,
        SetFacing
    }

    /// <summary>输入提交的不可变意图。命令事务与时间推进分开，暂停时也由世界验证和执行。</summary>
    public readonly struct WorldCommand
    {
        public WorldCommandKind Kind { get; }
        public BuildingKind BuildingKind { get; }
        public long BuildingId { get; }
        public int X { get; }
        public int Z { get; }
        public int Value { get; }
        public int? OrientationQuarterTurns { get; }
        public int? FacingDegrees { get; }

        private WorldCommand(WorldCommandKind kind, BuildingKind buildingKind = BuildingKind.Reactor,
            long buildingId = 0, int x = 0, int z = 0, int value = 0,
            int? orientationQuarterTurns = null, int? facingDegrees = null)
        {
            Kind = kind;
            BuildingKind = buildingKind;
            BuildingId = buildingId;
            X = x;
            Z = z;
            Value = value;
            OrientationQuarterTurns = orientationQuarterTurns;
            FacingDegrees = facingDegrees;
        }

        public static WorldCommand Place(BuildingKind kind, int x, int z, int orientationQuarterTurns = 0, int? facingDegrees = null) =>
            new WorldCommand(WorldCommandKind.Place, kind, x: x, z: z,
                orientationQuarterTurns: orientationQuarterTurns, facingDegrees: facingDegrees);
        public static WorldCommand Move(long buildingId, int x, int z, int? orientationQuarterTurns = null, int? facingDegrees = null) =>
            new WorldCommand(WorldCommandKind.Move, buildingId: buildingId, x: x, z: z,
                orientationQuarterTurns: orientationQuarterTurns, facingDegrees: facingDegrees);
        public static WorldCommand Sell(long buildingId) =>
            new WorldCommand(WorldCommandKind.Sell, buildingId: buildingId);
        public static WorldCommand Damage(long buildingId, int amount) =>
            new WorldCommand(WorldCommandKind.Damage, buildingId: buildingId, value: amount);
        public static WorldCommand Repair(long buildingId) =>
            new WorldCommand(WorldCommandKind.Repair, buildingId: buildingId);
        public static WorldCommand SetPriority(long buildingId, int weight) =>
            new WorldCommand(WorldCommandKind.SetPriority, buildingId: buildingId, value: weight);
        public static WorldCommand SetTestLoad(bool enabled) =>
            new WorldCommand(WorldCommandKind.SetTestLoad, value: enabled ? 1 : 0);
        public static WorldCommand StartWave() => new WorldCommand(WorldCommandKind.StartWave);
        public static WorldCommand RepairAll() => new WorldCommand(WorldCommandKind.RepairAll);
        public static WorldCommand ContinuePreparing() => new WorldCommand(WorldCommandKind.ContinuePreparing);
        public static WorldCommand SetFacing(long buildingId, int yawDegrees) =>
            new WorldCommand(WorldCommandKind.SetFacing, buildingId: buildingId, value: yawDegrees);
    }
}
