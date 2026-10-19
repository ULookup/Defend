namespace Defend.Simulation
{
    public enum ProjectileEndReason { None, Hit, Explosion, Expired, Ground, MapExit, WaveEnded, Wall }

    /// <summary>本步实际经过的三维轨迹段，结束弹体保留至本步结果交付。</summary>
    public readonly struct ProjectileSnapshot
    {
        public readonly long Id, SourceBuildingId;
        public readonly int SourceGeneration;
        public readonly BuildingKind TurretKind;
        public readonly double PreviousX, PreviousY, PreviousZ, X, Y, Z, StartTime, EndTime, LaunchTime;
        public readonly double TargetX, TargetY, TargetZ, SplashRadius;
        public readonly bool IsEnded;
        public readonly ProjectileEndReason EndReason;
        private readonly double originX, originY, originZ, velocityX, velocityY, velocityZ, duration, arcHeight;
        private readonly bool isArc;
        internal ProjectileSnapshot(ProjectileState projectile, double startTime, double endTime, ProjectileEndReason reason)
        {
            Id = projectile.Id; SourceBuildingId = projectile.SourceBuildingId; SourceGeneration = projectile.SourceGeneration;
            TurretKind = projectile.Weapon.Kind; LaunchTime = projectile.LaunchTime; StartTime = startTime; EndTime = endTime;
            projectile.Position(startTime, out PreviousX, out PreviousY, out PreviousZ);
            projectile.Position(endTime, out X, out Y, out Z);
            TargetX = projectile.TargetX; TargetY = projectile.TargetY; TargetZ = projectile.TargetZ;
            SplashRadius = projectile.Weapon.SplashRadius; EndReason = reason; IsEnded = reason != ProjectileEndReason.None;
            originX = projectile.StartX; originY = projectile.StartY; originZ = projectile.StartZ;
            velocityX = projectile.DirectionX * projectile.Weapon.ProjectileSpeed;
            velocityY = projectile.DirectionY * projectile.Weapon.ProjectileSpeed;
            velocityZ = projectile.DirectionZ * projectile.Weapon.ProjectileSpeed;
            duration = projectile.Duration; arcHeight = projectile.Weapon.AreaArcHeight; isArc = SplashRadius > 0;
        }
        /// <summary>表现直接采样权威公式，并在碰撞时刻截断，不在墙顶附近把抛物线画成直线弦。</summary>
        public void SamplePosition(double seconds, out double x, out double y, out double z)
        {
            double age = System.Math.Max(0, System.Math.Max(StartTime, System.Math.Min(EndTime, seconds)) - LaunchTime);
            if (isArc)
            {
                double progress = System.Math.Min(1, age / duration);
                x = originX + (TargetX - originX) * progress; z = originZ + (TargetZ - originZ) * progress;
                y = originY + (TargetY - originY) * progress + 4 * arcHeight * progress * (1 - progress);
            }
            else { x = originX + velocityX * age; y = originY + velocityY * age; z = originZ + velocityZ * age; }
        }
    }

    /// <summary>弹体保存发射时参数与身份，不持有目标或炮塔视图，来源销毁后仍独立飞行。</summary>
    internal sealed class ProjectileState
    {
        internal long Id, SourceBuildingId;
        internal int SourceGeneration;
        internal TurretDefinition Weapon;
        internal double StartX, StartY, StartZ, TargetX, TargetY, TargetZ, DirectionX, DirectionY, DirectionZ, LaunchTime, Duration;
        internal double ExplosionX, ExplosionY, ExplosionZ;
        internal bool Ended;
        internal void Position(double time, out double x, out double y, out double z)
        {
            double age = System.Math.Max(0, time - LaunchTime);
            if (Weapon.SplashRadius > 0)
            {
                double progress = System.Math.Min(1, age / Duration);
                x = StartX + (TargetX - StartX) * progress; z = StartZ + (TargetZ - StartZ) * progress;
                y = StartY + (TargetY - StartY) * progress + 4 * Weapon.AreaArcHeight * progress * (1 - progress);
            }
            else
            { x = StartX + DirectionX * Weapon.ProjectileSpeed * age; y = StartY + DirectionY * Weapon.ProjectileSpeed * age; z = StartZ + DirectionZ * Weapon.ProjectileSpeed * age; }
        }
    }
}
