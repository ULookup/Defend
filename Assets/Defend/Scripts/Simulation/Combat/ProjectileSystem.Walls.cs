using System;

namespace Defend.Simulation
{
    internal sealed partial class ProjectileSystem
    {
        private readonly TurretAimState targetingAim = new TurretAimState();
        private readonly ProjectileState targetingProjectile = new ProjectileState();

        internal bool CanReachTarget(TurretAimState aim, TurretDefinition weapon, EnemyState enemy)
        {
            if (battle.Settings.Walls.Count == 0) return true;
            targetingAim.Turret = aim.Turret; targetingAim.Target = enemy;
            targetingAim.Yaw = aim.Yaw; targetingAim.Pitch = aim.Pitch;
            // 只求候选最终可达的炮口与提前量，不推进实际炮塔，也不产生发射或能源事务。
            targetingAim.Advance(weapon, double.PositiveInfinity);
            targetingAim.Advance(weapon, double.PositiveInfinity);
            targetingAim.RecheckAlignment(weapon);
            return targetingAim.Aligned && HasClearShot(targetingAim, weapon);
        }

        internal bool HasClearShot(TurretAimState aim, TurretDefinition weapon)
        {
            if (battle.Settings.Walls.Count == 0) return true;
            double yaw = aim.Yaw * Math.PI / 180, pitch = aim.Pitch * Math.PI / 180;
            var projectile = targetingProjectile;
            projectile.Weapon = weapon; projectile.LaunchTime = 0;
            projectile.StartX = aim.MuzzleX; projectile.StartY = aim.MuzzleY; projectile.StartZ = aim.MuzzleZ;
            projectile.TargetX = aim.TargetX; projectile.TargetY = aim.TargetY; projectile.TargetZ = aim.TargetZ;
            projectile.DirectionX = Math.Sin(yaw) * Math.Cos(pitch);
            projectile.DirectionY = Math.Sin(pitch); projectile.DirectionZ = Math.Cos(yaw) * Math.Cos(pitch);
            double dx = aim.TargetX - aim.MuzzleX, dy = aim.TargetY - aim.MuzzleY, dz = aim.TargetZ - aim.MuzzleZ;
            projectile.Duration = Math.Max(.001, Math.Sqrt(dx * dx + dz * dz) / weapon.ProjectileSpeed);
            double end = weapon.SplashRadius > 0 ? projectile.Duration : Math.Sqrt(dx * dx + dy * dy + dz * dz) / weapon.ProjectileSpeed;
            return !FirstWallContact(projectile, 0, end, out _);
        }

        private bool FirstWallContact(ProjectileState projectile, double start, double end, out double time)
        {
            time = end;
            if (end < start || battle.Settings.Walls.Count == 0) return false;
            var weapon = projectile.Weapon;
            double ageStart = Math.Max(0, start - projectile.LaunchTime), ageEnd = Math.Max(0, end - projectile.LaunchTime);
            bool found;
            if (weapon.SplashRadius > 0)
            {
                found = WallCollision.FirstContact(battle.Settings.Walls, projectile.StartX, projectile.StartY, projectile.StartZ,
                    projectile.TargetX - projectile.StartX, projectile.TargetY - projectile.StartY + 4 * weapon.AreaArcHeight,
                    projectile.TargetZ - projectile.StartZ, -4 * weapon.AreaArcHeight,
                    Math.Min(1, ageStart / projectile.Duration), Math.Min(1, ageEnd / projectile.Duration), weapon.ProjectileRadius, out double progress);
                time = projectile.LaunchTime + progress * projectile.Duration;
            }
            else
            {
                found = WallCollision.FirstContact(battle.Settings.Walls, projectile.StartX, projectile.StartY, projectile.StartZ,
                    projectile.DirectionX * weapon.ProjectileSpeed, projectile.DirectionY * weapon.ProjectileSpeed,
                    projectile.DirectionZ * weapon.ProjectileSpeed, 0, ageStart, ageEnd, weapon.ProjectileRadius, out double age);
                time = projectile.LaunchTime + age;
            }
            return found;
        }

        private void RecordWallHit(ProjectileState projectile, double time, double x, double y, double z)
        {
            var hit = new HitContext(projectile, null, x, y, z, 0, 0);
            world.Record(WorldEventKind.ProjectileWallHit, projectile.Id, x, z, hit: hit, time: time);
        }
    }
}
