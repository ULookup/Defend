using System;
using System.Linq;
using Defend.Simulation;
using NUnit.Framework;

namespace Defend.Tests
{
    public sealed class ResonanceMagneticTowerSimulationTests
    {
        private static GameWorld Create(BuildingKind weaponKind = BuildingKind.Turret, int count = 1,
            long shotCost = 0, bool sandbox = false, double speed = .00001, ResonanceDefinition rules = null)
        {
            var weapon = new TurretDefinition(weaponKind, 24, 1, shotCost, 1,
                weaponKind == BuildingKind.AreaTurret ? 1 : 0, yawSpeedDegrees: 10000,
                pitchSpeedDegrees: 10000, maximumLeadSeconds: 0, muzzleForwardOffset: 0,
                minimumRange: 0, fireArcHalfDegrees: 180);
            var points = Enumerable.Range(0, count).Select(index => new SpawnPoint(11 + index, 18)).ToArray();
            var world = new GameWorld(new[] {
                new BuildingDefinition(BuildingKind.Core, "核心", 1, 1, 10000, 0, 0, 0, 1, 0, 100000, 0, 0, 0),
                new BuildingDefinition(BuildingKind.Reactor, "反应堆", 1, 1, 10000, 1000, 0, 1000, 1, 30, 1000, 0, 0, 0),
                new BuildingDefinition(weaponKind, "测试炮塔", 1, 1, 10000, 0, 0, 0, 1, 0, 1000, 0, 0, 0),
                new BuildingDefinition(BuildingKind.ResonanceMagneticTower, "共振磁暴塔", 2, 2, 1000, 0, 0, 0, 1, 0, 520, 280, 140, 70,
                    resonance: rules ?? new ResonanceDefinition())
            }, new WorldSettings(40, 40, 20, 10000), new EnergySettings(200, 1, 1000, 0),
                new BattleSettings(new[] { new WaveDefinition(count, 100000, 1, 100) }, enemySpeedCellsPerSecond: speed,
                    spawnPoints: points, turretDefinitions: new[] { weapon }), sandbox);
            Place(world, BuildingKind.Core, 35, 35);
            return world;
        }
        private static BuildingState Place(GameWorld world, BuildingKind kind, int x, int z)
        {
            var result = world.Submit(WorldCommand.Place(kind, x, z));
            Assert.That(result.Succeeded, Is.True, result.Message);
            return world.FindBuilding(result.BuildingId);
        }
        private static void Start(GameWorld world)
        {
            Assert.That(world.Submit(WorldCommand.StartWave()).Succeeded, Is.True);
            world.Step();
            Assert.That(world.Battle.Enemies.Count, Is.GreaterThan(0));
        }
        private static void Until(GameWorld world, Func<bool> condition, int maximum = 150)
        {
            for (int index = 0; index < maximum && !condition(); index++) world.Step();
            Assert.That(condition(), Is.True, "定向模拟未到达期望状态。");
        }
        private static void Position(EnemyState enemy, double x, double z)
        {
            enemy.X = x; enemy.Z = z;
            enemy.CellX = enemy.NextCellX = (int)x; enemy.CellZ = enemy.NextCellZ = (int)z;
        }
        private static void StopGun(BuildingState source) => source.NextShotTick = long.MaxValue;

        [Test]
        public void ActualShotsContributeOnceByWeaponKindAndRespectCenterDistance()
        {
            foreach (var kind in new[] { BuildingKind.Turret, BuildingKind.BurstTurret, BuildingKind.AreaTurret, BuildingKind.PiercingRailgun })
            {
                var world = Create(kind);
                var tower = Place(world, BuildingKind.ResonanceMagneticTower, 10, 10);
                var outside = Place(world, BuildingKind.ResonanceMagneticTower, 20, 10);
                var source = Place(world, kind, 10, 6);
                Start(world); Until(world, () => source.ShotsFired == 1);
                int expected = kind == BuildingKind.Turret ? 1 : kind == BuildingKind.AreaTurret ? 3 : 5;
                Assert.That(tower.TotalResonanceReceived, Is.EqualTo(expected), kind.ToString());
                Assert.That(outside.TotalResonanceReceived, Is.Zero);
                Assert.That(world.Events.Count(value => value.Kind == WorldEventKind.ResonanceAbsorbed), Is.EqualTo(1));
            }
            var waitingWorld = Create(shotCost: 1000);
            var waitingTower = Place(waitingWorld, BuildingKind.ResonanceMagneticTower, 10, 10);
            var waitingGun = Place(waitingWorld, BuildingKind.Turret, 10, 6);
            Start(waitingWorld);
            for (int index = 0; index < 40; index++) waitingWorld.Step();
            Assert.That(waitingGun.ShotsFired, Is.Zero);
            Assert.That(waitingTower.TotalResonanceReceived, Is.Zero, "只有目标、瞄准和等待能源均不能产生共振。");
        }

        [Test]
        public void IndependentThresholdsHealOnlySelfKeepOverflowAndDelayDamageEighteenTicks()
        {
            var world = Create(); var tower = Place(world, BuildingKind.ResonanceMagneticTower, 10, 10);
            var gun = Place(world, BuildingKind.Turret, 10, 6);
            world.Submit(WorldCommand.Damage(tower.Id, 80)); world.Submit(WorldCommand.Damage(gun.Id, 40));
            Start(world); Until(world, () => gun.ShotsFired == 27); StopGun(gun);
            Assert.That(tower.Health, Is.EqualTo(476)); Assert.That(gun.Health, Is.EqualTo(960));
            Assert.That(tower.RepairResonance, Is.EqualTo(7)); Assert.That(tower.BurstResonance, Is.EqualTo(27));
            long resolve = tower.ResonanceBurstResolveTick;
            Assert.That(resolve - tower.ResonanceBurstStartedTick, Is.EqualTo(18));
            Until(world, () => world.CurrentTick == resolve - 1);
            Assert.That(tower.ResonanceBurstsReleased, Is.Zero);
            world.Step();
            Assert.That(tower.ResonanceBurstsReleased, Is.EqualTo(1));
            Assert.That(tower.BurstResonance, Is.EqualTo(2)); Assert.That(tower.RepairResonance, Is.EqualTo(7));
        }

        [Test]
        public void StormUsesLiveEnemyCentersRecordsActualDamageRewardsAndCannotFeedAnotherTower()
        {
            var world = Create(count: 3); var tower = Place(world, BuildingKind.ResonanceMagneticTower, 10, 10);
            var isolated = Place(world, BuildingKind.ResonanceMagneticTower, 13, 12);
            var gun = Place(world, BuildingKind.Turret, 10, 6);
            Start(world); Until(world, () => gun.ShotsFired == 25); StopGun(gun);
            Until(world, () => world.CurrentTick == tower.ResonanceBurstResolveTick - 1);
            var inside = world.Battle.Enemies[0]; var boundary = world.Battle.Enemies[1]; var outside = world.Battle.Enemies[2];
            Position(inside, 11, 13.5); inside.Health = 30;
            Position(boundary, 14.5, 11); boundary.Health = 1000;
            Position(outside, 11, 14.501); outside.Health = 1000;
            // 将这一短步的敌人目标清除，精确覆盖边界；真实伤害仍由世界起爆路径完成。
            foreach (var enemy in world.Battle.Enemies)
            { enemy.TargetBuildingId = world.Battle.CoreBuildingId; enemy.Route = null; enemy.NextEvaluationTick = long.MaxValue; }
            world.Step();
            Assert.That(inside.IsAlive, Is.False); Assert.That(boundary.Health, Is.EqualTo(955)); Assert.That(outside.Health, Is.EqualTo(1000));
            Assert.That(tower.DamageDealt, Is.EqualTo(75)); Assert.That(world.Battle.Kills, Is.EqualTo(1));
            Assert.That(world.Battle.PendingKillReward, Is.EqualTo(inside.Definition.KillReward));
            Assert.That(world.Events.Count(value => value.Kind == WorldEventKind.EnemyHit && value.Hit.SourceBuildingId == tower.Id), Is.EqualTo(2));
            Assert.That(isolated.TotalResonanceReceived, Is.Zero, "磁暴不是友方炮塔射击，不能让磁暴塔相互充能。");
        }

        [Test]
        public void SlowChangesRealMovementAndMotionSamplingForTwentyFiveTicksThenExpires()
        {
            var world = Create(speed: 1); var tower = Place(world, BuildingKind.ResonanceMagneticTower, 10, 10);
            var gun = Place(world, BuildingKind.Turret, 10, 6);
            Start(world); Until(world, () => gun.ShotsFired == 25); StopGun(gun);
            Until(world, () => world.CurrentTick == tower.ResonanceBurstResolveTick - 1);
            var enemy = world.Battle.Enemies[0]; Position(enemy, 14, 11);
            world.Step(); long burstTick = world.CurrentTick;
            Assert.That(enemy.SlowUntilTick, Is.EqualTo(burstTick + 25));
            world.Step();
            Assert.That(enemy.MovementSpeedMultiplier, Is.EqualTo(.6));
            var moving = enemy.MotionSegments.Where(segment => segment.StartX != segment.EndX || segment.StartZ != segment.EndZ).ToArray();
            Assert.That(moving, Is.Not.Empty);
            foreach (var segment in moving)
            {
                double distance = Math.Sqrt(Math.Pow(segment.EndX - segment.StartX, 2) + Math.Pow(segment.EndZ - segment.StartZ, 2));
                Assert.That(distance / (segment.EndTime - segment.StartTime), Is.EqualTo(enemy.Definition.Speed * .6).Within(1e-7));
            }
            Until(world, () => world.CurrentTick == burstTick + 25);
            Assert.That(enemy.MovementSpeedMultiplier, Is.EqualTo(.6));
            world.Step(); Assert.That(enemy.MovementSpeedMultiplier, Is.EqualTo(1));
        }

        [Test]
        public void FrontlineBonusRequiresActualEnemyDamageAndIsConsumedByOneBurst()
        {
            var world = Create(); var tower = Place(world, BuildingKind.ResonanceMagneticTower, 10, 10);
            var gun = Place(world, BuildingKind.Turret, 10, 6);
            world.Submit(WorldCommand.Damage(tower.Id, 1));
            Assert.That(tower.ResonanceFeedbackReady, Is.False, "调试伤害不能冒充敌人命中。");
            Start(world); Until(world, () => gun.ShotsFired == 25); StopGun(gun);
            world.ApplyBattleDamage(tower, 1);
            Until(world, () => tower.ResonanceBurstsReleased == 1);
            Assert.That(world.Events.Single(value => value.Kind == WorldEventKind.MagneticStormBurst).Amount, Is.EqualTo(51));
            Assert.That(tower.ResonanceFeedbackReady, Is.False);
            gun.NextShotTick = world.CurrentTick;
            Until(world, () => gun.ShotsFired == 50); StopGun(gun);
            Until(world, () => tower.ResonanceBurstsReleased == 2);
            Assert.That(world.Events.Single(value => value.Kind == WorldEventKind.MagneticStormBurst).Amount, Is.EqualTo(45));
        }

        [Test]
        public void DestroyingChargingTowerClearsPendingBurstAndCannotRepairTheRuin()
        {
            var world = Create(); var tower = Place(world, BuildingKind.ResonanceMagneticTower, 10, 10);
            var gun = Place(world, BuildingKind.Turret, 10, 6);
            Start(world); Until(world, () => gun.ShotsFired == 25);
            world.Submit(WorldCommand.Damage(tower.Id, 1000));
            for (int index = 0; index < 30; index++)
            {
                world.Step();
                Assert.That(world.Events.Any(value => value.SubjectId == tower.Id &&
                    (value.Kind == WorldEventKind.MagneticStormBurst || value.Kind == WorldEventKind.ResonanceRepair)), Is.False);
            }
            Assert.That(tower.ResonanceBurstResolveTick, Is.EqualTo(-1));
            Assert.That(tower.RepairResonance, Is.Zero); Assert.That(tower.BurstResonance, Is.Zero);
            Assert.That(tower.IsAlive, Is.False); Assert.That(tower.Health, Is.Zero);
        }

        [Test]
        public void SandboxStopMoveCheckpointAndPausedCommandsPreserveResonanceProgress()
        {
            var world = Create(sandbox: true); var tower = Place(world, BuildingKind.ResonanceMagneticTower, 10, 10);
            var gun = Place(world, BuildingKind.Turret, 10, 6);
            var point = world.Battle.SandboxSpawnPoints[0];
            Assert.That(world.Battle.ConfigureSandboxWave(new[] { new SandboxEnemyGroup(1, EnemyKind.Normal, 100000, .00001, 1, 20, 0, 20,
                new[] { new SandboxSpawnAllocation(point.Id, 1) }) }, 200, out string issue), Is.True, issue);
            Start(world); Until(world, () => gun.ShotsFired == 27);
            Assert.That(world.Battle.StopSandboxWave().Succeeded, Is.True);
            Assert.That(world.Submit(WorldCommand.ContinuePreparing()).Succeeded, Is.True);
            Assert.That(tower.BurstResonance, Is.EqualTo(27)); Assert.That(tower.RepairResonance, Is.EqualTo(7));
            Assert.That(tower.NextResonanceBurstTick, Is.Zero, "取消前摇不能遗留一个旧的额外等待期。");
            Assert.That(world.Submit(WorldCommand.Move(tower.Id, 12, 10)).Succeeded, Is.True);
            long tick = world.CurrentTick;
            for (int index = 0; index < 50; index++) world.ProcessCommands();
            Assert.That(world.CurrentTick, Is.EqualTo(tick)); Assert.That(tower.ResonanceBurstResolveTick, Is.EqualTo(-1));
            var parameters = world.Sandbox.GetBuildingParameters(tower.Kind); parameters.MaximumHealth = 600;
            Assert.That(world.Sandbox.ApplyBuildingParameters(parameters, world.Revision, out issue), Is.True, issue);
            Assert.That(tower.Definition.Resonance.BurstDamage, Is.EqualTo(45));
            Start(world); Until(world, () => tower.ResonanceBurstsReleased > 0);
            Assert.That(world.Battle.StopSandboxWave().Succeeded, Is.True);
            var restored = world.RestoreSandboxCheckpoint(out issue);
            Assert.That(restored, Is.Not.Null, issue);
            var copy = restored.FindBuilding(tower.Id);
            Assert.That(copy.BurstResonance, Is.EqualTo(27)); Assert.That(copy.RepairResonance, Is.EqualTo(7));
            Assert.That(copy.TotalResonanceReceived, Is.EqualTo(27)); Assert.That(copy.ResonanceBurstsReleased, Is.Zero);
            Assert.That(copy.Definition.MaximumHealth, Is.EqualTo(600)); Assert.That(copy.Definition.Resonance.BurstWindupSeconds, Is.EqualTo(.9));
            Assert.That(copy.X, Is.EqualTo(12)); Assert.That(copy.ResonanceBurstResolveTick, Is.EqualTo(-1));
        }

        [Test]
        public void PureResonanceTowerNeverAimsOrReceivesElectricityAndFullHealthDoesNotBankHealing()
        {
            var world = Create(); var tower = Place(world, BuildingKind.ResonanceMagneticTower, 10, 10);
            var gun = Place(world, BuildingKind.Turret, 10, 6);
            Place(world, BuildingKind.Reactor, 5, 5);
            Start(world); Until(world, () => gun.ShotsFired == 20); StopGun(gun);
            Assert.That(tower.IsTurret, Is.False); Assert.That(tower.IsConsumer, Is.False); Assert.That(tower.CanReceive, Is.False);
            Assert.That(tower.StoredEnergy, Is.Zero); Assert.That(tower.TotalReceived, Is.Zero);
            Assert.That(world.Battle.TurretAims.Any(value => value.TurretId == tower.Id), Is.False);
            Assert.That(tower.RepairResonance, Is.Zero);
            world.Submit(WorldCommand.Damage(tower.Id, 100)); world.Step();
            Assert.That(tower.Health, Is.EqualTo(420), "满血期间越过的修复阈值不能储存为受伤后的瞬间治疗。");
            world.Energy.AssertInvariants();
        }
    }
}
