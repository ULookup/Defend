namespace Defend.Simulation
{
    public enum ProjectileEndReason { None, Hit, Explosion, Expired, Ground, MapExit, WaveEnded }

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
        internal ProjectileSnapshot(ProjectileState projectile, double startTime, double endTime, ProjectileEndReason reason)
        {
            Id = projectile.Id; SourceBuildingId = projectile.SourceBuildingId; SourceGeneration = projectile.SourceGeneration;
            TurretKind = projectile.Weapon.Kind; LaunchTime = projectile.LaunchTime; StartTime = startTime; EndTime = endTime;
            projectile.Position(startTime, out PreviousX, out PreviousY, out PreviousZ);
            projectile.Position(endTime, out X, out Y, out Z);
            TargetX = projectile.TargetX; TargetY = projectile.TargetY; TargetZ = projectile.TargetZ;
            SplashRadius = projectile.Weapon.SplashRadius; EndReason = reason; IsEnded = reason != ProjectileEndReason.None;
        }
    }

    /// <summary>弹体保存发射时参数与身份，不持有目标或炮塔视图，来源销毁后仍独立飞行。</summary>
    internal sealed class ProjectileState
    {
        internal long Id, SourceBuildingId;
        internal int SourceGeneration;
        internal TurretDefinition Weapon;
        internal double StartX, StartY, StartZ, TargetX, TargetY, TargetZ, DirectionX, DirectionY, DirectionZ, LaunchTime, Duration;
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
