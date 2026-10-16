using System;
using System.Collections.Generic;

namespace Defend.Simulation
{
    internal sealed partial class ProjectileSystem
    {
        private readonly List<RayContact> rayContacts = new List<RayContact>();

        // 与直射弹共用敌人的竖直圆柱受击体。此射线在发射时刻一次结算，不投影到地面补伤害。
        private void ResolvePiercingRay(ProjectileState projectile, BuildingState source, double time)
        {
            var weapon = projectile.Weapon;
            double length = weapon.MaximumFlightDistance;
            double sx = projectile.StartX - source.CenterX, sz = projectile.StartZ - source.CenterZ;
            double horizontal = projectile.DirectionX * projectile.DirectionX + projectile.DirectionZ * projectile.DirectionZ;
            double projection = sx * projectile.DirectionX + sz * projectile.DirectionZ;
            double discriminant = projection * projection - horizontal * (sx * sx + sz * sz - weapon.Range * weapon.Range);
            if (horizontal > 1e-10 && discriminant >= 0)
                length = Math.Min(length, Math.Max(0, (-projection + Math.Sqrt(discriminant)) / horizontal));
            ClipRayExit(projectile.StartX, projectile.DirectionX, world.Settings.Width, ref length);
            ClipRayExit(projectile.StartZ, projectile.DirectionZ, world.Settings.Depth, ref length);
            if (projectile.DirectionY < -1e-10)
                length = Math.Min(length, Math.Max(0, -projectile.StartY / projectile.DirectionY));

            rayContacts.Clear();
            foreach (var enemy in battle.Enemies)
            {
                if (!enemy.IsAlive) continue;
                double dx = enemy.X - source.CenterX, dz = enemy.Z - source.CenterZ;
                double distanceSquared = dx * dx + dz * dz;
                if (distanceSquared < weapon.MinimumRange * weapon.MinimumRange || distanceSquared > weapon.Range * weapon.Range) continue;
                if (TryRayContact(projectile, enemy, length, out double distance)) rayContacts.Add(new RayContact(enemy, distance));
            }
            rayContacts.Sort(CompareRayContacts);
            int count = Math.Min(weapon.MaximumPierceTargets, rayContacts.Count);
            if (count == weapon.MaximumPierceTargets) length = Math.Min(length, rayContacts[count - 1].Distance);
            projectile.TargetX = projectile.StartX + projectile.DirectionX * length;
            projectile.TargetY = projectile.StartY + projectile.DirectionY * length;
            projectile.TargetZ = projectile.StartZ + projectile.DirectionZ * length;
            projectile.Ended = true;
            int damage = weapon.Damage;
            for (int index = 0; index < count; index++)
            {
                var contact = rayContacts[index];
                battle.ResolveProjectileHit(projectile, contact.Enemy, time,
                    projectile.StartX + projectile.DirectionX * contact.Distance,
                    projectile.StartY + projectile.DirectionY * contact.Distance,
                    projectile.StartZ + projectile.DirectionZ * contact.Distance, damage);
                damage = Math.Max(1, (int)Math.Floor(damage * weapon.PierceDamageMultiplier));
            }
        }

        private static int CompareRayContacts(RayContact first, RayContact second)
        {
            int order = first.Distance.CompareTo(second.Distance);
            return order != 0 ? order : first.Enemy.Id.CompareTo(second.Enemy.Id);
        }
        private static void ClipRayExit(double start, double direction, double maximum, ref double length)
        {
            if (Math.Abs(direction) < 1e-10) return;
            length = Math.Min(length, Math.Max(0, ((direction > 0 ? maximum : 0) - start) / direction));
        }
        private static bool TryRayContact(ProjectileState projectile, EnemyState enemy, double length, out double distance)
        {
            double dx = projectile.StartX - enemy.X, dz = projectile.StartZ - enemy.Z;
            double radius = enemy.Radius + projectile.Weapon.ProjectileRadius;
            double a = projectile.DirectionX * projectile.DirectionX + projectile.DirectionZ * projectile.DirectionZ;
            double b = 2 * (dx * projectile.DirectionX + dz * projectile.DirectionZ), c = dx * dx + dz * dz - radius * radius;
            double enter = 0, leave = length; distance = 0;
            if (a < 1e-10) { if (c > 0) return false; }
            else
            {
                double discriminant = b * b - 4 * a * c;
                if (discriminant < 0) return false;
                double root = Math.Sqrt(discriminant);
                enter = Math.Max(enter, (-b - root) / (2 * a)); leave = Math.Min(leave, (-b + root) / (2 * a));
            }
            double minimum = -projectile.Weapon.ProjectileRadius, maximum = enemy.Definition.BodyHeight + projectile.Weapon.ProjectileRadius;
            if (Math.Abs(projectile.DirectionY) < 1e-10)
            { if (projectile.StartY < minimum || projectile.StartY > maximum) return false; }
            else
            {
                double first = (minimum - projectile.StartY) / projectile.DirectionY;
                double second = (maximum - projectile.StartY) / projectile.DirectionY;
                enter = Math.Max(enter, Math.Min(first, second)); leave = Math.Min(leave, Math.Max(first, second));
            }
            if (enter > leave + 1e-9) return false;
            distance = enter; return true;
        }
        private readonly struct RayContact
        {
            internal readonly EnemyState Enemy;
            internal readonly double Distance;
            internal RayContact(EnemyState enemy, double distance) { Enemy = enemy; Distance = distance; }
        }
    }
}
