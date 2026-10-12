using System;
using System.Collections.Generic;
using Defend.Simulation;
using NUnit.Framework;
namespace Defend.Tests
{
    /// <summary>只保留目标分离、实体占位和出怪顺延三个短规则用例，不模拟完整关卡。</summary>
    public sealed class ConfirmedEnemyRulesTests
    {
        private static BuildingDefinition Building(BuildingKind kind, int health = 1000, long production = 0)
            => new BuildingDefinition(kind, kind.ToString(), 1, 1, 20000, production, 0,
                kind == BuildingKind.Reactor || kind == BuildingKind.Relay ? 1000 : 0, 1,
                kind == BuildingKind.Reactor || kind == BuildingKind.Relay ? 20 : 0, health, 10, 5, 3);
        private static GameWorld Create(int count, int limit = 200, bool turretOpening = false, int interval = 2)
        {
            var blocked = new List<SpawnPoint>();
            for (int z = 0; z < 5; z++) for (int x = 0; x < 9; x++)
                if (z != 2 && !(turretOpening && z == 1 && (x == 6 || x == 7))) blocked.Add(new SpawnPoint(x, z));
            var settings = new BattleSettings(new[] { new WaveDefinition(count, 60, interval, 10, new[] { EnemyKind.Heavy }) },
                maximumLivingEnemies: limit, enemySpeedCellsPerSecond: 4, enemyAttackIntervalTicks: 2,
                turretRange: 1.6, turretDamage: 500, turretEnergyPerShot: 1000, turretCooldownTicks: 100,
                spawnPoints: new[] { new SpawnPoint(0, 2) }, blockedCells: blocked);
            return new GameWorld(new[] { Building(BuildingKind.Core), Building(BuildingKind.Relay, 36),
                Building(BuildingKind.Reactor, production: 1000), Building(BuildingKind.Turret) },
                new WorldSettings(9, 5, 20, 1000), new EnergySettings(200, 1, 1000, 0), settings);
        }
        private static BuildingState Place(GameWorld world, BuildingKind kind, int x, int z)
        {
            world.Enqueue(WorldCommand.Place(kind, x, z)); world.Step();
            Assert.That(world.LastCommandResult.Succeeded, Is.True, world.LastCommandResult.Message);
            return world.FindBuilding(world.LastCommandResult.BuildingId);
        }
        private static void Start(GameWorld world) { world.Enqueue(WorldCommand.StartWave()); world.Step(); Assert.That(world.LastCommandResult.Succeeded, Is.True); }

        [Test]
        public void ClearingObstacleKeepsFinalCoreAndResumesApproach()
        {
            var world = Create(1); var core = Place(world, BuildingKind.Core, 8, 2); var obstacle = Place(world, BuildingKind.Relay, 3, 2);
            Start(world); var enemy = world.Battle.Enemies[0];
            for (int tick = 0; tick < 45 && enemy.CurrentAttackBuildingId != obstacle.Id; tick++) world.Step();
            Assert.That(enemy.CurrentAttackBuildingId, Is.EqualTo(obstacle.Id));
            Assert.That(enemy.FinalTargetBuildingId, Is.EqualTo(core.Id));
            double before = enemy.X;
            for (int tick = 0; tick < 25; tick++) world.Step();
            Assert.That(obstacle.IsAlive, Is.False);
            Assert.That(enemy.FinalTargetBuildingId, Is.EqualTo(core.Id));
            Assert.That(enemy.X, Is.GreaterThan(before));
        }

        [Test]
        public void NarrowPassageNeverOverlapsAndDeathReleasesSpace()
        {
            var world = Create(2, turretOpening: true); Place(world, BuildingKind.Core, 8, 2);
            Place(world, BuildingKind.Reactor, 7, 1); Place(world, BuildingKind.Turret, 6, 1); Start(world);
            EnemyState follower = null; double positionBeforeDeath = 0;
            for (int tick = 0; tick < 100 && world.Battle.Kills == 0; tick++)
            {
                world.Step();
                if (world.Battle.Enemies.Count != 2) continue;
                var first = world.Battle.Enemies[0]; var second = world.Battle.Enemies[1];
                double dx = first.X - second.X, dz = first.Z - second.Z;
                Assert.That(dx * dx + dz * dz, Is.GreaterThanOrEqualTo(Math.Pow(first.Radius + second.Radius, 2) - .000001));
                follower = first.X < second.X ? first : second; positionBeforeDeath = follower.X;
            }
            Assert.That(world.Battle.Kills, Is.EqualTo(1)); Assert.That(follower, Is.Not.Null);
            for (int tick = 0; tick < 8; tick++) world.Step();
            Assert.That(follower.IsAlive, Is.True); Assert.That(follower.X, Is.GreaterThan(positionBeforeDeath));

            // 实际第二波的三人卡死回归：相切实体各自追逐脚下格中心，曾在重规划后永久互卡。
            // 只布置这一份已观察位置，不增加新的测试用例或长局自动运行。
            var terrain = new List<SpawnPoint>();
            for (int z = 0; z < 5; z++) for (int x = 0; x < 9; x++)
                if (z != 2) terrain.Add(new SpawnPoint(x, z));
            var collisionSettings = new BattleSettings(new[] { new WaveDefinition(3, 60, 1, 0,
                new[] { EnemyKind.Normal, EnemyKind.Normal, EnemyKind.Fast }) },
                spawnPoints: new[] { new SpawnPoint(0, 2) }, blockedCells: terrain);
            var collisionWorld = new GameWorld(new[] { Building(BuildingKind.Core) }, new WorldSettings(9, 5, 20, 1000),
                new EnergySettings(200, 1, 1000, 0), collisionSettings);
            Place(collisionWorld, BuildingKind.Core, 8, 2); Start(collisionWorld);
            for (int i = 0; i < 20 && collisionWorld.Battle.Enemies.Count < 3; i++) collisionWorld.Step();
            Assert.That(collisionWorld.Battle.Enemies.Count, Is.EqualTo(3));
            var positions = new[] { (3.596, 2.511), (2.960, 2.421), (2.400, 2.554) };
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            for (int i = 0; i < 3; i++)
            {
                var entity = collisionWorld.Battle.Enemies[i];
                typeof(EnemyState).GetProperty(nameof(EnemyState.X)).SetValue(entity, positions[i].Item1);
                typeof(EnemyState).GetProperty(nameof(EnemyState.Z)).SetValue(entity, positions[i].Item2);
                typeof(EnemyState).GetField("CellX", flags).SetValue(entity, (int)positions[i].Item1);
                typeof(EnemyState).GetField("CellZ", flags).SetValue(entity, (int)positions[i].Item2);
                typeof(EnemyState).GetField("Route", flags).SetValue(entity, null);
                typeof(EnemyState).GetField("NextEvaluationTick", flags).SetValue(entity, 0L);
            }
            for (int tick = 0; tick < 40; tick++)
            {
                collisionWorld.Step();
                for (int i = 0; i < 3; i++) for (int j = i + 1; j < 3; j++)
                {
                    var a = collisionWorld.Battle.Enemies[i]; var b = collisionWorld.Battle.Enemies[j];
                    double dx = a.X - b.X, dz = a.Z - b.Z;
                    Assert.That(dx * dx + dz * dz, Is.GreaterThanOrEqualTo(Math.Pow(a.Radius + b.Radius, 2) - .000001));
                }
            }
            for (int i = 0; i < 3; i++) Assert.That(collisionWorld.Battle.Enemies[i].X, Is.GreaterThan(positions[i].Item1 + 1));
        }

        [Test]
        public void BlockedSpawnRetainsQueueAndResumesOriginalIntervals()
        {
            var groups = new[] { new WaveGroupDefinition(EnemyKind.Normal, 2, 0, 0, 5), new WaveGroupDefinition(EnemyKind.Fast, 1, 0, 0, 5) };
            var settings = new BattleSettings(new[] { new WaveDefinition(3, 60, 5, 0, groups: groups) }, maximumLivingEnemies: 1,
                enemySpeedCellsPerSecond: .1, turretRange: 20, turretDamage: 500, turretEnergyPerShot: 10000, turretCooldownTicks: 1,
                spawnPoints: new[] { new SpawnPoint(0, 2) });
            var world = new GameWorld(new[] { Building(BuildingKind.Core), Building(BuildingKind.Reactor, production: 1000), Building(BuildingKind.Turret) },
                new WorldSettings(9, 5, 20, 1000), new EnergySettings(200, 1, 1000, 0), settings);
            Place(world, BuildingKind.Core, 8, 2); Place(world, BuildingKind.Reactor, 7, 1); Place(world, BuildingKind.Turret, 6, 1); Start(world);
            Assert.That(world.Battle.RemainingToSpawn, Is.EqualTo(2));
            for (int i = 0; i < 5; i++) world.Step();
            Assert.That(world.Battle.RemainingToSpawn, Is.EqualTo(2)); Assert.That(world.Battle.State, Is.EqualTo(BattleState.Combat));
            int remaining = 2; long lastSpawn = world.CurrentTick - 5; int generated = 1;
            for (int i = 0; i < 50 && remaining > 0; i++)
            {
                world.Step();
                if (world.Battle.RemainingToSpawn == remaining) continue;
                Assert.That(world.Battle.RemainingToSpawn, Is.EqualTo(remaining - 1));
                Assert.That(world.CurrentTick - lastSpawn, Is.GreaterThanOrEqualTo(5));
                Assert.That(world.Battle.Enemies[0].Kind, Is.EqualTo(generated == 1 ? EnemyKind.Normal : EnemyKind.Fast));
                generated++; lastSpawn = world.CurrentTick; remaining--;
            }
            Assert.That(generated, Is.EqualTo(3)); Assert.That(world.Battle.RemainingToSpawn, Is.Zero);
        }
    }
}
