using System;
using System.Collections.Generic;
namespace Defend.Simulation
{
    /// <summary>只处理独立弹道和最早接触；奖励、伤害与能源由既有世界和战斗入口负责。</summary>
    internal sealed partial class ProjectileSystem
    {
        private readonly BattleSystem battle;
        private readonly GameWorld world;
        private readonly List<ProjectileState> active = new List<ProjectileState>();
        private readonly List<ProjectileSnapshot> snapshots = new List<ProjectileSnapshot>();
        private readonly List<Contact> contacts = new List<Contact>();
        private long nextId = 1;
        internal IReadOnlyList<ProjectileSnapshot> Snapshots { get; }
        internal long CaptureNextIdentifier()
        {
            if (active.Count != 0) throw new InvalidOperationException("只能记录没有在途弹体的开战前状态。");
            return nextId;
        }
        internal void RestoreNextIdentifier(long identifier)
        { active.Clear(); snapshots.Clear(); contacts.Clear(); nextId = identifier; }
        internal ProjectileSystem(BattleSystem battle, GameWorld world)
        { this.battle = battle; this.world = world; Snapshots = snapshots.AsReadOnly(); }
        internal ProjectileState Launch(TurretAimState aim, TurretDefinition weapon, double time)
        {
            double yaw = aim.Yaw * Math.PI / 180, pitch = aim.Pitch * Math.PI / 180;
            var projectile = new ProjectileState { Id = nextId++, SourceBuildingId = aim.Turret.Id, SourceGeneration = aim.Turret.LifeGeneration,
                Weapon = weapon, StartX = aim.MuzzleX, StartY = aim.MuzzleY, StartZ = aim.MuzzleZ,
                TargetX = aim.TargetX, TargetY = aim.TargetY, TargetZ = aim.TargetZ, LaunchTime = time,
                DirectionX = Math.Sin(yaw) * Math.Cos(pitch), DirectionY = Math.Sin(pitch), DirectionZ = Math.Cos(yaw) * Math.Cos(pitch) };
            if (weapon.IsPiercing)
            {
                ResolvePiercingRay(projectile, aim.Turret, time);
                return projectile;
            }
            double dx = projectile.TargetX - projectile.StartX, dz = projectile.TargetZ - projectile.StartZ;
            projectile.Duration = Math.Max(.001, Math.Sqrt(dx * dx + dz * dz) / weapon.ProjectileSpeed);
            active.Add(projectile); snapshots.Add(new ProjectileSnapshot(projectile, time, time, ProjectileEndReason.None));
            return projectile;
        }
        internal void BeginStep() => snapshots.Clear();
        internal void Advance(double start, double end)
        {
            contacts.Clear();
            foreach (var projectile in active) contacts.Add(FindContact(projectile, start, end));
            while (contacts.Count > 0)
            {
                int earliest = 0;
                for (int index = 1; index < contacts.Count; index++)
                    if (contacts[index].Time < contacts[earliest].Time ||
                        (contacts[index].Time == contacts[earliest].Time && contacts[index].Projectile.Id < contacts[earliest].Projectile.Id)) earliest = index;
                Contact contact = contacts[earliest]; contacts.RemoveAt(earliest);
                if (contact.Enemy != null && !contact.Enemy.IsAlive)
                {
                    // 前一颗弹体已击杀候选目标时，从该时刻重查余下真实路径，不让子弹凭旧候选消失。
                    contacts.Add(FindContact(contact.Projectile, contact.Time, end)); continue;
                }
                var projectile = contact.Projectile;
                if (contact.Reason == ProjectileEndReason.Hit)
                {
                    projectile.Position(contact.Time, out double x, out double y, out double z);
                    battle.ResolveProjectileHit(projectile, contact.Enemy, contact.Time, x, y, z);
                }
                else if (contact.Reason == ProjectileEndReason.Explosion)
                {
                    var hit = new HitContext(projectile, null, projectile.TargetX, 0, projectile.TargetZ, 0, 0);
                    world.Record(WorldEventKind.Explosion, projectile.Id, projectile.TargetX, projectile.TargetZ, hit: hit, time: contact.Time);
                    foreach (var enemy in battle.Enemies)
                    {
                        if (!enemy.IsAlive) continue;
                        enemy.SampleMotion(contact.Time, out double x, out double z);
                        double dx = x - projectile.TargetX, dz = z - projectile.TargetZ;
                        if (dx * dx + dz * dz <= projectile.Weapon.SplashRadius * projectile.Weapon.SplashRadius)
                            battle.ResolveProjectileHit(projectile, enemy, contact.Time, x, enemy.Definition.BodyHeight * .55, z);
                    }
                }
                projectile.Ended = contact.Reason != ProjectileEndReason.None;
                snapshots.Add(new ProjectileSnapshot(projectile, start, contact.Time, contact.Reason));
            }
            active.RemoveAll(projectile => projectile.Ended);
        }
        internal void Clear(double time)
        {
            foreach (var projectile in active)
            {
                int index = snapshots.FindIndex(value => value.Id == projectile.Id);
                double start = index >= 0 ? snapshots[index].StartTime : time;
                var ended = new ProjectileSnapshot(projectile, start, time, ProjectileEndReason.WaveEnded);
                if (index >= 0) snapshots[index] = ended; else snapshots.Add(ended);
            }
            active.Clear();
        }
        private Contact FindContact(ProjectileState projectile, double start, double end)
        {
            double stop = end;
            var reason = ProjectileEndReason.None;
            double lifetime = projectile.LaunchTime + Math.Min(projectile.Weapon.MaximumLifetimeSeconds,
                projectile.Weapon.MaximumFlightDistance / projectile.Weapon.ProjectileSpeed);
            if (lifetime <= stop) { stop = lifetime; reason = ProjectileEndReason.Expired; }
            if (projectile.Weapon.SplashRadius > 0)
            {
                double landing = projectile.LaunchTime + projectile.Duration;
                if (landing <= stop) { stop = landing; reason = ProjectileEndReason.Explosion; }
                return new Contact(projectile, null, stop, reason);
            }
            double speed = projectile.Weapon.ProjectileSpeed;
            ClipExit(projectile.StartX, projectile.DirectionX * speed, world.Settings.Width, projectile.LaunchTime, ref stop, ref reason);
            ClipExit(projectile.StartZ, projectile.DirectionZ * speed, world.Settings.Depth, projectile.LaunchTime, ref stop, ref reason);
            if (projectile.DirectionY < 0)
            {
                double ground = projectile.LaunchTime - projectile.StartY / (projectile.DirectionY * speed);
                if (ground <= stop) { stop = ground; reason = ProjectileEndReason.Ground; }
            }
            EnemyState nearest = null;
            foreach (var enemy in battle.Enemies)
            {
                if (!enemy.IsAlive) continue;
                foreach (var segment in enemy.MotionSegments)
                {
                    double from = Math.Max(start, segment.StartTime), to = Math.Min(stop, segment.EndTime);
                    if (to < from) continue;
                    if (!TryContact(projectile, enemy, segment, from, to, out double time)) continue;
                    if (time < stop || nearest == null || (time == stop && enemy.Id < nearest.Id))
                    { nearest = enemy; stop = time; reason = ProjectileEndReason.Hit; }
                }
            }
            return new Contact(projectile, nearest, Math.Max(start, stop), reason);
        }
        private static void ClipExit(double position, double velocity, double maximum, double launch, ref double stop, ref ProjectileEndReason reason)
        {
            if (Math.Abs(velocity) < .0000001) return;
            double exit = launch + ((velocity > 0 ? maximum : 0) - position) / velocity;
            if (exit <= stop) { stop = exit; reason = ProjectileEndReason.MapExit; }
        }
        private static bool TryContact(ProjectileState projectile, EnemyState enemy, EnemyMotionSegment segment, double from, double to, out double time)
        {
            projectile.Position(from, out double x, out double y, out double z);
            segment.Sample(from, out double enemyX, out double enemyZ);
            double segmentDuration = segment.EndTime - segment.StartTime;
            double vx = projectile.DirectionX * projectile.Weapon.ProjectileSpeed - (segmentDuration > 0 ? (segment.EndX - segment.StartX) / segmentDuration : 0);
            double vz = projectile.DirectionZ * projectile.Weapon.ProjectileSpeed - (segmentDuration > 0 ? (segment.EndZ - segment.StartZ) / segmentDuration : 0);
            double dx = x - enemyX, dz = z - enemyZ, radius = enemy.Radius + projectile.Weapon.ProjectileRadius;
            double a = vx * vx + vz * vz, b = 2 * (dx * vx + dz * vz), c = dx * dx + dz * dz - radius * radius;
            double enter = 0, leave = to - from;
            time = 0;
            if (a < .000000001) { if (c > 0) return false; }
            else
            {
                double discriminant = b * b - 4 * a * c;
                if (discriminant < 0) return false;
                double root = Math.Sqrt(discriminant);
                enter = Math.Max(enter, (-b - root) / (2 * a)); leave = Math.Min(leave, (-b + root) / (2 * a));
            }
            double verticalSpeed = projectile.DirectionY * projectile.Weapon.ProjectileSpeed;
            double minimum = -projectile.Weapon.ProjectileRadius, maximum = enemy.Definition.BodyHeight + projectile.Weapon.ProjectileRadius;
            if (Math.Abs(verticalSpeed) < .000000001) { if (y < minimum || y > maximum) return false; }
            else
            {
                double first = (minimum - y) / verticalSpeed, second = (maximum - y) / verticalSpeed;
                enter = Math.Max(enter, Math.Min(first, second)); leave = Math.Min(leave, Math.Max(first, second));
            }
            if (enter > leave + .000000001) return false;
            time = from + enter; return true;
        }
        private readonly struct Contact
        {
            internal readonly ProjectileState Projectile;
            internal readonly EnemyState Enemy;
            internal readonly double Time;
            internal readonly ProjectileEndReason Reason;
            internal Contact(ProjectileState projectile, EnemyState enemy, double time, ProjectileEndReason reason)
            { Projectile = projectile; Enemy = enemy; Time = time; Reason = reason; }
        }
    }
}
