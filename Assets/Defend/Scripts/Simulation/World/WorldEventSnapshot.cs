namespace Defend.Simulation
{
    public enum WorldEventKind { EnemyHit, EnemyDeath, BuildingHit, BuildingDestroyed, Repair, ShieldHit, ShieldBreak, Explosion,
        ResonanceAbsorbed, ResonanceRepair, MagneticStormCharging, MagneticStormBurst, ProjectileWallHit }

    /// <summary>真实接触的不可变上下文；非弹道事件使用默认值，ProjectileId 为零。</summary>
    public readonly struct HitContext
    {
        public readonly long ProjectileId, SourceBuildingId;
        public readonly int SourceGeneration;
        public readonly BuildingKind TurretKind;
        public readonly EnemyKind EnemyKind;
        public readonly double X, Y, Z, DirectionX, DirectionY, DirectionZ, ExplosionX, ExplosionY, ExplosionZ, SplashRadius, EnemyX, EnemyZ;
        public readonly int HealthAfter, MaximumHealth;
        public readonly bool IsFatal;
        internal HitContext(ProjectileState projectile, EnemyState enemy, double x, double y, double z, double enemyX, double enemyZ)
        {
            ProjectileId = projectile.Id; SourceBuildingId = projectile.SourceBuildingId; SourceGeneration = projectile.SourceGeneration;
            TurretKind = projectile.Weapon.Kind; EnemyKind = enemy == null ? default : enemy.Kind;
            X = x; Y = y; Z = z; DirectionX = projectile.DirectionX; DirectionY = projectile.DirectionY; DirectionZ = projectile.DirectionZ;
            SplashRadius = projectile.Weapon.SplashRadius;
            ExplosionX = SplashRadius > 0 ? projectile.ExplosionX : projectile.TargetX;
            ExplosionY = SplashRadius > 0 ? projectile.ExplosionY : projectile.TargetY;
            ExplosionZ = SplashRadius > 0 ? projectile.ExplosionZ : projectile.TargetZ;
            EnemyX = enemyX; EnemyZ = enemyZ; HealthAfter = enemy?.Health ?? 0; MaximumHealth = enemy?.MaximumHealth ?? 0;
            IsFatal = enemy != null && !enemy.IsAlive;
        }
        internal HitContext(BuildingState source, EnemyState enemy, double radius)
        {
            ProjectileId = 0; SourceBuildingId = source.Id; SourceGeneration = source.LifeGeneration;
            TurretKind = source.Kind; EnemyKind = enemy.Kind;
            X = EnemyX = enemy.X; Z = EnemyZ = enemy.Z; Y = enemy.Definition.BodyHeight * .55;
            double dx = enemy.X - source.CenterX, dz = enemy.Z - source.CenterZ;
            double distance = System.Math.Sqrt(dx * dx + dz * dz);
            DirectionX = distance > .000001 ? dx / distance : 0;
            DirectionY = 0; DirectionZ = distance > .000001 ? dz / distance : 0;
            ExplosionX = source.CenterX; ExplosionY = 0; ExplosionZ = source.CenterZ; SplashRadius = radius;
            HealthAfter = enemy.Health; MaximumHealth = enemy.MaximumHealth; IsFatal = !enemy.IsAlive;
        }
    }

    /// <summary>结算结果按事件序号去重，逻辑时刻仅用于显示排序；即时命令也拥有独立序号。</summary>
    public readonly struct WorldEventSnapshot
    {
        public readonly WorldEventKind Kind;
        public readonly long SubjectId, EventId, Tick;
        public readonly double X, Z, Time;
        public readonly int Amount, HealthAfter, MaximumHealth;
        public readonly long ShieldBefore, ShieldAfter, ShieldAbsorbed;
        public readonly HitContext Hit;
        public WorldEventSnapshot(WorldEventKind kind, long subjectId, double x, double z, int amount = 0,
            long eventId = 0, long tick = 0, double time = 0, HitContext hit = default,
            int healthAfter = 0, int maximumHealth = 0, long shieldBefore = 0, long shieldAfter = 0, long shieldAbsorbed = 0)
        {
            Kind = kind; SubjectId = subjectId; X = x; Z = z; Amount = amount;
            EventId = eventId; Tick = tick; Time = time; Hit = hit; HealthAfter = healthAfter; MaximumHealth = maximumHealth;
            ShieldBefore = shieldBefore; ShieldAfter = shieldAfter; ShieldAbsorbed = shieldAbsorbed;
        }
    }
}
