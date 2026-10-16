namespace Defend.Simulation
{
    public enum TurretCombatState { None, Searching, CoolingDown, WaitingForEnergy, Fired, Aiming, Charging }

    /// <summary>完整扣能后产生的实际发射记录，不代表命中或伤害。</summary>
    public readonly struct ShotSnapshot
    {
        public long Id { get; }
        public long Tick { get; }
        public long ProjectileId { get; }
        public int SourceGeneration { get; }
        public double Time { get; }
        public long TurretId { get; }
        public long EnemyId { get; }
        public double SourceX { get; }
        public double SourceY { get; }
        public double SourceZ { get; }
        public double TargetX { get; }
        public double TargetY { get; }
        public double TargetZ { get; }
        public double DirectionX { get; }
        public double DirectionY { get; }
        public double DirectionZ { get; }
        public BuildingKind TurretKind { get; }
        public double SplashRadius { get; }
        internal ShotSnapshot(long id, long tick, ProjectileState projectile, long enemyId)
        {
            Id = id; Tick = tick; SourceGeneration = projectile.SourceGeneration; ProjectileId = projectile.Id; Time = projectile.LaunchTime;
            TurretId = projectile.SourceBuildingId; EnemyId = enemyId; TurretKind = projectile.Weapon.Kind;
            SplashRadius = projectile.Weapon.SplashRadius;
            SourceX = projectile.StartX; SourceY = projectile.StartY; SourceZ = projectile.StartZ;
            TargetX = projectile.TargetX; TargetY = projectile.TargetY; TargetZ = projectile.TargetZ;
            DirectionX = projectile.DirectionX; DirectionY = projectile.DirectionY; DirectionZ = projectile.DirectionZ;
        }
    }
}
