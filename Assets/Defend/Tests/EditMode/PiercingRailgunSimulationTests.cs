using System;
using System.Linq;
using Defend.Simulation;
using NUnit.Framework;

namespace Defend.Tests
{
    public sealed class PiercingRailgunSimulationTests
    {
        private static GameWorld Create(int count = 1, bool sandbox = false, double enemySpeed = .00001)
        {
            var points = Enumerable.Range(0, count).Select(index => new SpawnPoint(3, 14 + index)).ToArray();
            var world = new GameWorld(new[] {
                SimulationTestWorld.Definition(BuildingKind.Core),
                SimulationTestWorld.Definition(BuildingKind.Reactor, production: 10000, range: 30),
                new BuildingDefinition(BuildingKind.PiercingRailgun, "贯穿轨道炮", 2, 3, 24000, 0, 0, 0, 1, 0, 420, 320, 224, 96)
            }, new WorldSettings(30, 30, 20, 10000), new EnergySettings(200, 1, 1000, 0),
                new BattleSettings(new[] { new WaveDefinition(count, 1000, 1, 100) }, enemySpeedCellsPerSecond: enemySpeed,
                    spawnPoints: points), sandbox);
            Place(world, BuildingKind.Core, 26, 26);
            return world;
        }
        private static BuildingState Place(GameWorld world, BuildingKind kind, int x, int z)
        {
            var result = world.Submit(WorldCommand.Place(kind, x, z));
            Assert.That(result.Succeeded, Is.True, result.Message);
            return world.FindBuilding(result.BuildingId);
        }
        private static BuildingState Prepare(GameWorld world)
        {
            Place(world, BuildingKind.Reactor, 0, 2);
            var source = Place(world, BuildingKind.PiercingRailgun, 2, 2);
            SimulationTestWorld.Advance(world, 20);
            return source;
        }
        private static void Spawn(GameWorld world, int count)
        {
            Assert.That(world.Submit(WorldCommand.StartWave()).Succeeded, Is.True);
            for (int index = 0; index < count + 5 && world.Battle.Enemies.Count < count; index++) world.Step();
            Assert.That(world.Battle.Enemies.Count, Is.EqualTo(count));
        }
        private static void Position(EnemyState enemy, double x, double z)
        {
            enemy.X = x; enemy.Z = z; enemy.CellX = enemy.NextCellX = (int)x; enemy.CellZ = enemy.NextCellZ = (int)z;
        }
        private static TurretAimState Ray(BuildingState source, double pitch = 0) => new TurretAimState {
            Turret = source, MuzzleX = source.CenterX, MuzzleY = .45, MuzzleZ = source.CenterZ + 1.79,
            Yaw = 0, Pitch = pitch, TargetX = source.CenterX, TargetY = .45, TargetZ = source.CenterZ + 14 };

        [Test]
        public void BeamSortsRealContactsAndStopsAfterFiveWithDecliningDamage()
        {
            var world = Create(8); var source = Place(world, BuildingKind.PiercingRailgun, 2, 2); Spawn(world, 8);
            // 刻意使身份次序与接触次序相反，另放一名偏离射线的敌人。
            for (int index = 0; index < 6; index++) Position(world.Battle.Enemies[index], source.CenterX, 15 - index * 1.4);
            Position(world.Battle.Enemies[6], source.CenterX + 2, 9);
            Position(world.Battle.Enemies[7], source.CenterX, 20);
            var system = new ProjectileSystem(world.Battle, world);
            var beam = system.Launch(Ray(source), world.GetTurretDefinition(source.Kind), (double)world.CurrentTick / 20);
            var hits = world.Events.Where(value => value.Kind == WorldEventKind.EnemyHit).ToArray();
            Assert.That(hits.Select(value => value.Amount), Is.EqualTo(new[] { 180, 144, 115, 92, 73 }));
            Assert.That(hits.Select(value => value.SubjectId), Is.EqualTo(world.Battle.Enemies.Take(6).Reverse().Take(5).Select(value => value.Id)));
            Assert.That(world.Battle.Enemies[0].Health, Is.EqualTo(1000));
            Assert.That(world.Battle.Enemies[6].Health, Is.EqualTo(1000));
            Assert.That(beam.TargetZ, Is.EqualTo(hits.Last().Hit.Z).Within(1e-8));
            Assert.That(system.Snapshots, Is.Empty, "瞬时激光不再生成一颗重复结算的慢速弹体。");
        }

        [Test]
        public void BeamRespectsHeightGroundAndMinimumRangeWithoutProjectedHits()
        {
            var world = Create(3); var source = Place(world, BuildingKind.PiercingRailgun, 2, 2); Spawn(world, 3);
            Position(world.Battle.Enemies[0], source.CenterX, source.CenterZ + 2.8);
            Position(world.Battle.Enemies[1], source.CenterX, source.CenterZ + 6);
            Position(world.Battle.Enemies[2], source.CenterX, source.CenterZ + 10);
            var system = new ProjectileSystem(world.Battle, world);
            var high = Ray(source); high.MuzzleY = 3;
            var elevated = system.Launch(high, world.GetTurretDefinition(source.Kind), 1);
            Assert.That(elevated.TargetZ, Is.EqualTo(source.CenterZ + 14).Within(1e-8), "最远端按建筑中心射程裁剪，不把炮管长度额外加入射程。");
            Assert.That(world.Battle.Enemies.All(enemy => enemy.Health == 1000), Is.True);
            var low = system.Launch(Ray(source, -12), world.GetTurretDefinition(source.Kind), 1);
            Assert.That(low.TargetY, Is.EqualTo(0).Within(1e-8));
            Assert.That(world.Battle.Enemies.All(enemy => enemy.Health == 1000), Is.True);
        }

        [Test]
        public void ChargeWaitsTwentyFourTicksThenConsumesOnceAndStartsSeventyTwoTickCooldown()
        {
            var world = Create(enemySpeed: 1.8); var source = Prepare(world); Spawn(world, 1);
            Position(world.Battle.Enemies[0], source.CenterX, source.CenterZ + 10);
            SimulationTestWorld.AdvanceUntil(world, () => source.CombatState == TurretCombatState.Charging, 40, "应先对准再蓄力。");
            long start = source.ChargeStartedTick, consumed = world.Energy.GetTotals().Consumed;
            SimulationTestWorld.Advance(world, 23);
            Assert.That(source.ShotsFired, Is.Zero); Assert.That(world.Energy.GetTotals().Consumed, Is.EqualTo(consumed));
            Assert.That(source.ChargeProgress, Is.EqualTo(23.0 / 24).Within(.00001));
            var charge = world.Battle.TurretAims.Single(value => value.TurretId == source.Id);
            Assert.That(charge.PreviousChargeProgress, Is.EqualTo(22.0 / 24).Within(.00001));
            Assert.That(charge.ChargeProgress, Is.EqualTo(23.0 / 24).Within(.00001));
            Assert.That(charge.SourceGeneration, Is.EqualTo(source.LifeGeneration));
            world.Step();
            Assert.That(charge.ChargeProgress, Is.EqualTo(23.0 / 24).Within(.00001), "旧快照不能随可变建筑状态提前改变。");
            Assert.That(source.LastShotTick, Is.EqualTo(start + 24));
            Assert.That(source.NextShotTick, Is.EqualTo(source.LastShotTick + 72));
            Assert.That(source.ShotsFired, Is.EqualTo(1)); Assert.That(world.Energy.GetTotals().Consumed, Is.EqualTo(consumed + 12000));
            var shot = world.Battle.Shots.Last();
            Assert.That(shot.TargetX - shot.SourceX, Is.EqualTo(shot.DirectionX * Distance(shot)).Within(1e-7));
            Assert.That(shot.TargetY - shot.SourceY, Is.EqualTo(shot.DirectionY * Distance(shot)).Within(1e-7));
            SimulationTestWorld.Advance(world, 71); Assert.That(source.ShotsFired, Is.EqualTo(1));
        }
        private static double Distance(ShotSnapshot shot) => Math.Sqrt(Math.Pow(shot.TargetX - shot.SourceX, 2) + Math.Pow(shot.TargetY - shot.SourceY, 2) + Math.Pow(shot.TargetZ - shot.SourceZ, 2));

        [Test]
        public void LosingAllTargetsKeepsChargingAndReadyShotWaitsUntilTargetReturns()
        {
            var world = Create(); var source = Prepare(world); Spawn(world, 1);
            var enemy = world.Battle.Enemies[0]; Position(enemy, source.CenterX, source.CenterZ + 10);
            SimulationTestWorld.AdvanceUntil(world, () => source.ChargeStartedTick >= 0, 40, "未开始蓄力。");
            SimulationTestWorld.Advance(world, 12); long oldStart = source.ChargeStartedTick;
            Position(enemy, source.CenterX + 10, source.CenterZ); world.Step();
            Assert.That(source.ChargeStartedTick, Is.EqualTo(oldStart)); Assert.That(source.ChargeProgress, Is.GreaterThan(.5));
            SimulationTestWorld.Advance(world, 20);
            Assert.That(source.ChargeProgress, Is.EqualTo(1)); Assert.That(source.ShotsFired, Is.Zero);
            Assert.That(source.CombatState, Is.EqualTo(TurretCombatState.Searching));
            Assert.That(world.Energy.GetTotals().Consumed, Is.Zero);
            Position(enemy, source.CenterX, source.CenterZ + 10); world.Step();
            Assert.That(source.ShotsFired, Is.EqualTo(1));
            Assert.That(source.ChargeStartedTick, Is.EqualTo(-1));
            Assert.That(world.Energy.GetTotals().Consumed, Is.EqualTo(12000));
        }

        [Test]
        public void SwitchingAcrossArcKeepsProgressAndFullChargeCannotFireBeforeAlignment()
        {
            var world = Create(2); var source = Prepare(world); Spawn(world, 2);
            var first = world.Battle.Enemies[0]; var second = world.Battle.Enemies[1];
            Position(first, source.CenterX - 4.067, source.CenterZ + 9.135);
            Position(second, source.CenterX + 3.660, source.CenterZ + 8.222);
            SimulationTestWorld.AdvanceUntil(world, () => source.ChargeStartedTick >= 0, 50, "未开始蓄力。");
            long start = source.ChargeStartedTick;
            SimulationTestWorld.Advance(world, 18);
            Position(first, source.CenterX - 10, source.CenterZ);
            world.Step();
            Assert.That(world.Battle.TurretAims.Single().TargetEnemyId, Is.EqualTo(second.Id));
            Assert.That(source.ChargeStartedTick, Is.EqualTo(start));
            Assert.That(source.ChargeProgress, Is.EqualTo(19.0 / 24).Within(.00001));
            SimulationTestWorld.Advance(world, 5);
            Assert.That(source.ChargeProgress, Is.EqualTo(1));
            Assert.That(world.Battle.TurretAims.Single().IsAligned, Is.False);
            Assert.That(source.ShotsFired, Is.Zero); Assert.That(world.Energy.GetTotals().Consumed, Is.Zero);
            SimulationTestWorld.AdvanceUntil(world, () => source.ShotsFired == 1, 40, "满蓄力完成转向后应立即发射。");
            Assert.That(source.LastShotTick, Is.GreaterThan(start + 24));
            Assert.That(world.Battle.Shots.Last().EnemyId, Is.EqualTo(second.Id));
            Assert.That(world.Energy.GetTotals().Consumed, Is.EqualTo(12000));
        }

        [Test]
        public void KilledTargetTransfersExistingChargeToTheNextLivingTarget()
        {
            var world = Create(2); var source = Prepare(world); Spawn(world, 2);
            var first = world.Battle.Enemies[0]; var second = world.Battle.Enemies[1];
            Position(first, source.CenterX - 2, source.CenterZ + 11);
            Position(second, source.CenterX + 2, source.CenterZ + 9);
            SimulationTestWorld.AdvanceUntil(world, () => source.ChargeStartedTick >= 0, 50, "未开始蓄力。");
            long start = source.ChargeStartedTick;
            SimulationTestWorld.Advance(world, 10);
            var lethal = new ProjectileState { Id = 10000, SourceBuildingId = source.Id, SourceGeneration = source.LifeGeneration,
                Weapon = new TurretDefinition(BuildingKind.Turret, 20, 2000, 0, 1), DirectionZ = 1 };
            world.Battle.ResolveProjectileHit(lethal, first, world.CurrentTick / 20.0, first.X, .45, first.Z);
            world.Step();
            Assert.That(first.IsAlive, Is.False);
            Assert.That(world.Battle.TurretAims.Single().TargetEnemyId, Is.EqualTo(second.Id));
            Assert.That(source.ChargeStartedTick, Is.EqualTo(start));
            Assert.That(source.ChargeProgress, Is.EqualTo(11.0 / 24).Within(.00001));
            SimulationTestWorld.AdvanceUntil(world, () => source.ShotsFired == 1, 40, "旧目标死亡后应沿用已有蓄力。");
            Assert.That(world.Battle.Shots.Last().EnemyId, Is.EqualTo(second.Id));
            Assert.That(world.Energy.GetTotals().Consumed, Is.EqualTo(12000));
        }

        [Test, Category("BuildingRotation")]
        public void PlacementAndRotationPreserveRailgunFineFacingAndCheckpointGeometry()
        {
            var world = Create(sandbox: true);
            var placed = world.Submit(WorldCommand.Place(BuildingKind.PiercingRailgun, 2, 2, 1));
            Assert.That(placed.Succeeded, Is.True, placed.Message);
            var source = world.FindBuilding(placed.BuildingId);
            Assert.That(source.FacingDegrees, Is.EqualTo(90));
            Assert.That(world.Submit(WorldCommand.SetFacing(source.Id, 105)).Succeeded, Is.True);
            Assert.That(world.Submit(WorldCommand.Move(source.Id, 2, 2, 2)).Succeeded, Is.True);
            Assert.That(source.FacingDegrees, Is.EqualTo(195), "建筑旋转须保留原有 15 度微调。");
            Assert.That(world.Submit(WorldCommand.Move(source.Id, 2, 2, 3, 285)).Succeeded, Is.True);
            Assert.That(source.FootprintWidth, Is.EqualTo(3));
            Assert.That(source.FootprintDepth, Is.EqualTo(2));
            Assert.That(source.FacingDegrees, Is.EqualTo(285));

            Assert.That(world.Submit(WorldCommand.StartWave()).Succeeded, Is.True);
            world.Step();
            Assert.That(world.Battle.TurretAims.Single(value => value.TurretId == source.Id).YawDegrees, Is.EqualTo(285));
            Assert.That(world.Battle.StopSandboxWave().Succeeded, Is.True);
            var restored = world.RestoreSandboxCheckpoint(out string error);
            Assert.That(restored, Is.Not.Null, error);
            var saved = restored.FindBuilding(source.Id);
            Assert.That(saved.OrientationQuarterTurns, Is.EqualTo(3));
            Assert.That(saved.FacingDegrees, Is.EqualTo(285));
            Assert.That(restored.FindBuildingAt(4, 3), Is.SameAs(saved));
            Assert.That(restored.FindBuildingAt(2, 4), Is.Null);
        }

        [Test]
        public void FacingIsPreparationOnlyAndTargetsMustStayInsideArcAndReachablePitch()
        {
            var world = Create(); var source = Prepare(world);
            Assert.That(world.Submit(WorldCommand.SetFacing(source.Id, 450)).Succeeded, Is.True);
            Assert.That(source.FacingDegrees, Is.EqualTo(90));
            Spawn(world, 1);
            Assert.That(world.Submit(WorldCommand.SetFacing(source.Id, 0)).Succeeded, Is.False);
            var aim = new TurretAimState { Turret = source }; var weapon = world.GetTurretDefinition(source.Kind);
            var enemy = world.Battle.Enemies[0]; Position(enemy, source.CenterX + 10, source.CenterZ);
            Assert.That(aim.CanTarget(weapon, enemy), Is.True);
            Position(enemy, source.CenterX, source.CenterZ + 10); Assert.That(aim.CanTarget(weapon, enemy), Is.False);
            Position(enemy, source.CenterX + 2.8, source.CenterZ); Assert.That(aim.CanTarget(weapon, enemy), Is.False);
            SimulationTestWorld.Advance(world, 30); Assert.That(source.ShotsFired, Is.Zero);
            Assert.That(source.ChargeStartedTick, Is.EqualTo(-1), "没有首次合法对准不能凭空启动蓄力。");
            Assert.That(source.ChargeProgress, Is.Zero);
        }

        [Test]
        public void InsufficientEnergyAtChargeCompletionCancelsWithoutFiringOrStartingCooldown()
        {
            var world = Create(); var source = Prepare(world); Spawn(world, 1);
            Position(world.Battle.Enemies[0], source.CenterX, source.CenterZ + 10);
            SimulationTestWorld.AdvanceUntil(world, () => source.ChargeStartedTick >= 0, 40, "未开始蓄力。");
            SimulationTestWorld.Advance(world, 23);
            Position(world.Battle.Enemies[0], source.CenterX + 10, source.CenterZ);
            world.Energy.ConsumeUpTo(source.Id, source.StoredEnergy);
            long consumed = world.Energy.GetTotals().Consumed; world.Step();
            Assert.That(source.ShotsFired, Is.Zero); Assert.That(source.NextShotTick, Is.Zero);
            Assert.That(source.ChargeStartedTick, Is.EqualTo(-1)); Assert.That(world.Energy.GetTotals().Consumed, Is.EqualTo(consumed));
        }

        [Test]
        public void DestroyingChargedBuildingDiscardsItsPendingShot()
        {
            var world = Create(); var source = Prepare(world); Spawn(world, 1);
            Position(world.Battle.Enemies[0], source.CenterX, source.CenterZ + 10);
            SimulationTestWorld.AdvanceUntil(world, () => source.ChargeStartedTick >= 0, 40, "未开始蓄力。");
            Assert.That(world.Submit(WorldCommand.Damage(source.Id, 1000)).Succeeded, Is.True);
            SimulationTestWorld.Advance(world, 30);
            Assert.That(source.ChargeStartedTick, Is.EqualTo(-1)); Assert.That(source.ShotsFired, Is.Zero);
        }

        [Test]
        public void SandboxChangesAndCheckpointPreserveWeaponRulesAndFacing()
        {
            var world = Create(sandbox: true); var source = Prepare(world);
            Assert.That(world.Submit(WorldCommand.SetFacing(source.Id, 90)).Succeeded, Is.True);
            var parameters = world.Sandbox.GetBuildingParameters(source.Kind); parameters.AttackRange = 16;
            Assert.That(world.Sandbox.ApplyBuildingParameters(parameters, world.Revision, out string error), Is.True, error);
            var weapon = world.GetTurretDefinition(source.Kind);
            Assert.That(weapon.MinimumRange, Is.EqualTo(3.5)); Assert.That(weapon.ChargeTicks, Is.EqualTo(24));
            Assert.That(weapon.MaximumPierceTargets, Is.EqualTo(5)); Assert.That(weapon.PierceDamageMultiplier, Is.EqualTo(.8));
            Assert.That(world.Submit(WorldCommand.StartWave()).Succeeded, Is.True);
            world.Step();
            Position(world.Battle.Enemies[0], source.CenterX + 10, source.CenterZ);
            SimulationTestWorld.AdvanceUntil(world, () => source.ChargeStartedTick >= 0, 50, "沙盒炮塔应开始蓄力。");
            SimulationTestWorld.Advance(world, 5);
            Assert.That(source.ChargeProgress, Is.GreaterThan(0));
            Assert.That(world.Battle.StopSandboxWave().Succeeded, Is.True);
            Assert.That(source.ChargeStartedTick, Is.EqualTo(-1)); Assert.That(source.ChargeProgress, Is.Zero);
            var restored = world.RestoreSandboxCheckpoint(out error);
            Assert.That(restored, Is.Not.Null, error);
            var saved = restored.FindBuilding(source.Id);
            Assert.That(saved.FacingDegrees, Is.EqualTo(90)); Assert.That(saved.IsConsumer, Is.True);
            Assert.That(saved.ChargeStartedTick, Is.EqualTo(-1)); Assert.That(restored.GetTurretDefinition(source.Kind).Range, Is.EqualTo(16));
            Assert.That(restored.Energy.GetTotals().BalanceError, Is.Zero);
            Assert.That(restored.Submit(WorldCommand.StartWave()).Succeeded, Is.True);
            restored.Step(); // 先创建旧主方向的瞄准缓存，不能只验证首次建造的情况。
            Assert.That(restored.Battle.StopSandboxWave().Succeeded, Is.True);
            Assert.That(restored.Submit(WorldCommand.ContinuePreparing()).Succeeded, Is.True);
            Assert.That(restored.Submit(WorldCommand.SetFacing(saved.Id, 270)).Succeeded, Is.True);
            Assert.That(restored.Submit(WorldCommand.StartWave()).Succeeded, Is.True);
            restored.Step();
            Assert.That(restored.Battle.TurretAims.Single(value => value.TurretId == saved.Id).YawDegrees, Is.EqualTo(270));
        }
    }
}
