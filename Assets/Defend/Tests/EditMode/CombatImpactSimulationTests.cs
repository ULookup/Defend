using System;
using System.Linq;
using Defend.Simulation;
using NUnit.Framework;
namespace Defend.Tests
{
    /// <summary>仅验证本次发射、接触、归属和结算顺序；不运行完整关卡或性能探针。</summary>
    public sealed class CombatImpactSimulationTests
    {
        private static GameWorld Create(int count = 2, int damage = 20, long production = 10000)
        {
            var weapon = new TurretDefinition(BuildingKind.Turret, 20, damage, 2000, 8);
            var world = new GameWorld(new[] {
                SimulationTestWorld.Definition(BuildingKind.Core),
                SimulationTestWorld.Definition(BuildingKind.Reactor, production: production, range: 30),
                SimulationTestWorld.Definition(BuildingKind.Turret),
                SimulationTestWorld.Definition(BuildingKind.Relay)
            }, new WorldSettings(20, 20, 20, 10000), new EnergySettings(200, 1, 1000, 0),
                new BattleSettings(new[] { new WaveDefinition(count, 60, 1, 100) }, enemySpeedCellsPerSecond: .01,
                    spawnPoints: new[] { new SpawnPoint(2, 8), new SpawnPoint(2, 11), new SpawnPoint(2, 14) },
                    turretDefinitions: new[] { weapon }));
            SimulationTestWorld.Place(world, BuildingKind.Core, 18, 18);
            return world;
        }
        private static void Spawn(GameWorld world, int count)
        {
            Assert.That(world.Submit(WorldCommand.StartWave()).Succeeded, Is.True);
            for (int index = 0; index < count + 2 && world.Battle.Enemies.Count < count; index++) world.Step();
            Assert.That(world.Battle.Enemies.Count, Is.EqualTo(count));
        }
        private static void Path(EnemyState enemy, double x, double z, double endX, double endZ, double duration = 1)
        {
            enemy.X = x; enemy.Z = z; enemy.BeginMotion(0);
            enemy.X = endX; enemy.Z = endZ; enemy.AddMotion(x, z, duration); enemy.EndMotion(1);
        }
        private static ProjectileState Launch(ProjectileSystem projectiles, BuildingState source, TurretDefinition weapon,
            double x = 1, double y = .45, double z = 5, double targetX = 9, double targetZ = 5, double time = 0)
        {
            var aim = new TurretAimState { Turret = source, Yaw = 90, Pitch = 0, MuzzleX = x, MuzzleY = y, MuzzleZ = z,
                TargetX = targetX, TargetY = weapon.SplashRadius > 0 ? 0 : y, TargetZ = targetZ };
            return projectiles.Launch(aim, weapon, time);
        }

        [Test]
        public void TurningCostsNothingAndActualLaunchStartsCooldownWithoutDamage()
        {
            var world = Create(1);
            SimulationTestWorld.Place(world, BuildingKind.Reactor, 8, 7);
            var turret = SimulationTestWorld.Place(world, BuildingKind.Turret, 8, 8);
            SimulationTestWorld.Advance(world, 12); Spawn(world, 1);
            int health = world.Battle.Enemies[0].Health;
            world.Step();
            Assert.That(turret.CombatState, Is.EqualTo(TurretCombatState.Aiming));
            Assert.That(turret.ShotsFired, Is.Zero);
            Assert.That(world.Energy.GetTotals().Consumed, Is.Zero);
            SimulationTestWorld.AdvanceUntil(world, () => turret.ShotsFired > 0, 30, "炮口转向后应发射。");
            Assert.That(world.Battle.Enemies[0].Health, Is.EqualTo(health));
            Assert.That(turret.DamageDealt, Is.Zero);
            Assert.That(world.Energy.GetTotals().Consumed, Is.EqualTo(2000));
            Assert.That(turret.NextShotTick, Is.EqualTo(world.CurrentTick + 8));
            Assert.That(world.Battle.Projectiles.Count, Is.EqualTo(1));
            Assert.That(world.Battle.Projectiles[0].StartTime, Is.EqualTo(world.Battle.Projectiles[0].EndTime));
        }

        [Test]
        public void ContinuousDirectContactHitsFirstEnemyOnceAndIgnoresFriendlyBuildings()
        {
            var world = Create(); var source = SimulationTestWorld.Place(world, BuildingKind.Turret, 1, 5);
            SimulationTestWorld.Place(world, BuildingKind.Relay, 3, 5); Spawn(world, 2);
            Path(world.Battle.Enemies[0], 4, 5, 4, 5); Path(world.Battle.Enemies[1], 7, 5, 7, 5);
            var projectiles = new ProjectileSystem(world.Battle, world);
            Launch(projectiles, source, new TurretDefinition(BuildingKind.Turret, 15, 20, 1000, 1, projectileSpeed: 200));
            projectiles.BeginStep(); projectiles.Advance(0, .05);
            Assert.That(world.Battle.Enemies[0].Health, Is.EqualTo(40));
            Assert.That(world.Battle.Enemies[1].Health, Is.EqualTo(60));
            Assert.That(world.Events.Count(value => value.Kind == WorldEventKind.EnemyHit), Is.EqualTo(1));
            Assert.That(projectiles.Snapshots[0].EndReason, Is.EqualTo(ProjectileEndReason.Hit));
            projectiles.BeginStep(); projectiles.Advance(.05, .1);
            Assert.That(world.Battle.Enemies[0].Health, Is.EqualTo(40));
        }

        [TestCase(true, false)]
        [TestCase(false, false)]
        [TestCase(false, true)]
        public void RelativeMotionAndBodyHeightDetermineContact(bool crossesPath, bool aboveBody)
        {
            var world = Create(1); var source = SimulationTestWorld.Place(world, BuildingKind.Turret, 1, 5); Spawn(world, 1);
            var enemy = world.Battle.Enemies[0];
            if (crossesPath) Path(enemy, 5, 3, 5, 7); else if (aboveBody) Path(enemy, 5, 5, 5, 5); else Path(enemy, 5, 5, 5, 8);
            var projectiles = new ProjectileSystem(world.Battle, world);
            Launch(projectiles, source, new TurretDefinition(BuildingKind.Turret, 15, 20, 1000, 1, projectileSpeed: 8), y: aboveBody ? 3 : .45);
            projectiles.BeginStep(); projectiles.Advance(0, 1);
            Assert.That(enemy.Health, Is.EqualTo(crossesPath ? 40 : 60));
        }

        [Test]
        public void SecondProjectileRechecksPathAfterFirstKillsOriginalTarget()
        {
            var world = Create(); var source = SimulationTestWorld.Place(world, BuildingKind.Turret, 1, 5); Spawn(world, 2);
            Path(world.Battle.Enemies[0], 4, 5, 4, 5); Path(world.Battle.Enemies[1], 7, 5, 7, 5);
            var projectiles = new ProjectileSystem(world.Battle, world);
            var weapon = new TurretDefinition(BuildingKind.Turret, 15, 100, 1000, 1, projectileSpeed: 20);
            Launch(projectiles, source, weapon); Launch(projectiles, source, weapon);
            world.Submit(WorldCommand.Damage(source.Id, 1000));
            projectiles.BeginStep(); projectiles.Advance(0, .5);
            Assert.That(world.Battle.Enemies.All(value => !value.IsAlive), Is.True);
            Assert.That(world.Battle.Kills, Is.EqualTo(2));
            Assert.That(source.DamageDealt, Is.EqualTo(120), "已损毁来源仍记录实际生命伤害，过量部分不计入。");
            var hits = world.Events.Where(value => value.Kind == WorldEventKind.EnemyHit).ToArray();
            Assert.That(hits.Length, Is.EqualTo(2)); Assert.That(hits[0].Amount, Is.EqualTo(60));
            Assert.That(hits[0].EventId, Is.Not.EqualTo(hits[1].EventId));
            Assert.That(hits[0].Time, Is.LessThan(hits[1].Time));
        }

        [Test]
        public void SeparateNonlethalHitsRemainSeparateEventsOnOneEnemy()
        {
            var world = Create(1); var source = SimulationTestWorld.Place(world, BuildingKind.Turret, 1, 5); Spawn(world, 1);
            Path(world.Battle.Enemies[0], 4, 5, 4, 5);
            var projectiles = new ProjectileSystem(world.Battle, world);
            var weapon = new TurretDefinition(BuildingKind.Turret, 15, 20, 1000, 1);
            Launch(projectiles, source, weapon); Launch(projectiles, source, weapon);
            projectiles.BeginStep(); projectiles.Advance(0, .2);
            var hits = world.Events.Where(value => value.Kind == WorldEventKind.EnemyHit).ToArray();
            Assert.That(hits.Length, Is.EqualTo(2));
            Assert.That(hits[0].HealthAfter, Is.EqualTo(40)); Assert.That(hits[1].HealthAfter, Is.EqualTo(20));
            Assert.That(hits[0].EventId, Is.LessThan(hits[1].EventId));
            Assert.That(hits[0].Hit.ProjectileId, Is.LessThan(hits[1].Hit.ProjectileId));
        }

        [Test]
        public void DetourSegmentsDoNotInventContactAlongEndpointShortcut()
        {
            var world = Create(1); var source = SimulationTestWorld.Place(world, BuildingKind.Turret, 1, 5); Spawn(world, 1);
            var enemy = world.Battle.Enemies[0];
            enemy.X = 5; enemy.Z = 3; enemy.BeginMotion(0);
            enemy.X = 8; enemy.Z = 3; enemy.AddMotion(5, 3, .4);
            enemy.X = 8; enemy.Z = 7; enemy.AddMotion(8, 3, .4);
            enemy.X = 5; enemy.Z = 7; enemy.AddMotion(8, 7, .2); enemy.EndMotion(1);
            var projectiles = new ProjectileSystem(world.Battle, world);
            Launch(projectiles, source, new TurretDefinition(BuildingKind.Turret, 15, 20, 1000, 1, projectileSpeed: 8));
            projectiles.BeginStep(); projectiles.Advance(0, 1);
            Assert.That(enemy.Health, Is.EqualTo(60), "绕障真实分段避开子弹，首尾捷径则会错误命中。");
        }

        [Test]
        public void AreaShellKeepsLandingPointAndSamplesEnemiesAtLandingTime()
        {
            var world = Create(3); var source = SimulationTestWorld.Place(world, BuildingKind.Turret, 1, 5); Spawn(world, 3);
            var leaving = world.Battle.Enemies[0]; var entering = world.Battle.Enemies[1]; var boundary = world.Battle.Enemies[2];
            Path(leaving, 5, 5, 10, 5, .5); Path(entering, 10, 5, 5, 5, .5); Path(boundary, 7, 5, 7, 5);
            var projectiles = new ProjectileSystem(world.Battle, world);
            Launch(projectiles, source, new TurretDefinition(BuildingKind.AreaTurret, 15, 20, 1000, 1, 2,
                projectileSpeed: 8), targetX: 5);
            projectiles.BeginStep(); projectiles.Advance(0, .49);
            Assert.That(world.Battle.Enemies.All(value => value.Health == 60), Is.True);
            projectiles.BeginStep(); projectiles.Advance(.49, .5);
            Assert.That(leaving.Health, Is.EqualTo(60)); Assert.That(entering.Health, Is.EqualTo(40)); Assert.That(boundary.Health, Is.EqualTo(40));
            Assert.That(world.Events.Count(value => value.Kind == WorldEventKind.Explosion), Is.EqualTo(1));
            Assert.That(projectiles.Snapshots[0].X, Is.EqualTo(5));
        }

        [Test]
        public void ExpiredMissDoesNotDamageAndWaveCleanupRetainsTerminalSnapshot()
        {
            var world = Create(1); var source = SimulationTestWorld.Place(world, BuildingKind.Turret, 1, 5); Spawn(world, 1);
            Path(world.Battle.Enemies[0], 10, 10, 10, 10);
            var projectiles = new ProjectileSystem(world.Battle, world);
            Launch(projectiles, source, new TurretDefinition(BuildingKind.Turret, 15, 20, 1000, 1,
                projectileSpeed: 8, maximumLifetimeSeconds: .1));
            projectiles.BeginStep(); projectiles.Advance(0, .2);
            Assert.That(projectiles.Snapshots[0].EndReason, Is.EqualTo(ProjectileEndReason.Expired));
            Assert.That(world.Battle.Enemies[0].Health, Is.EqualTo(60));
            Launch(projectiles, source, new TurretDefinition(BuildingKind.Turret, 15, 20, 1000, 1), time: .2);
            projectiles.Clear(.2);
            Assert.That(projectiles.Snapshots.Last().EndReason, Is.EqualTo(ProjectileEndReason.WaveEnded));
        }

        [Test]
        public void EnemyKilledByProjectileCannotCommitPendingMelee()
        {
            var world = Create(1); var source = SimulationTestWorld.Place(world, BuildingKind.Turret, 1, 5); Spawn(world, 1);
            var enemy = world.Battle.Enemies[0]; var core = world.FindBuilding(world.Battle.CoreBuildingId);
            Path(enemy, 17.8, 18.5, 17.8, 18.5);
            enemy.CurrentAttackBuildingId = core.Id;
            var projectiles = new ProjectileSystem(world.Battle, world);
            Launch(projectiles, source, new TurretDefinition(BuildingKind.Turret, 20, 100, 1000, 1), x: 14, z: 18.5);
            projectiles.BeginStep(); projectiles.Advance(0, .3);
            Assert.That(enemy.IsAlive, Is.False);
            var grid = new BuildingGrid(20, 20);
            new EnemyMovementSystem(world, grid, world.Battle).CommitAttack(enemy, world.CurrentTick);
            Assert.That(core.Health, Is.EqualTo(core.Definition.MaximumHealth));
            Assert.That(enemy.LastAttackTick, Is.EqualTo(-1));
        }
    }
}
