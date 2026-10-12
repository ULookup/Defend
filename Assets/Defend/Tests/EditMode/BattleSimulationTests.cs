using System;
using Defend.Simulation;
using NUnit.Framework;

namespace Defend.Tests
{
    /// <summary>通过正式世界命令验证战斗和能源边界，不写入内部生命值、缓存或波次状态。</summary>
    public sealed class BattleSimulationTests
    {
        private static BuildingDefinition Definition(BuildingKind kind, long capacity = 100000,
            long production = 0, int width = 1, int depth = 1)
        {
            bool terminal = kind == BuildingKind.Turret || kind == BuildingKind.Core;
            return new BuildingDefinition(kind, kind.ToString(), width, depth, capacity,
                production, 0, terminal ? 0 : 100000, 1, terminal ? 0 : 30,
                1000, 10, 5, 3);
        }

        private static GameWorld Create(BattleSettings settings = null, long turretCapacity = 100000,
            long production = 10000, BuildingDefinition relay = null)
        {
            return new GameWorld(new[] {
                Definition(BuildingKind.Reactor, production: production),
                relay ?? Definition(BuildingKind.Relay),
                Definition(BuildingKind.Turret, turretCapacity),
                Definition(BuildingKind.Core)
            }, new WorldSettings(16, 16, 20, 10000), new EnergySettings(200, 1, 1000, 0),
                settings ?? Settings());
        }

        private static BattleSettings Settings(int count = 1, int health = 100, int interval = 10,
            int limit = 200, int buildings = 100, double speed = 0.1, int damage = 10,
            int attackInterval = 20, double range = 20, int turretDamage = 20,
            long energy = 2000, int cooldown = 8, WaveDefinition[] waves = null,
            SpawnPoint[] spawns = null)
        {
            return new BattleSettings(waves ?? new[] { new WaveDefinition(count, health, interval, 300) },
                buildings, limit, speed, damage, attackInterval, range, turretDamage, energy, cooldown,
                6, spawns ?? new[] { new SpawnPoint(2, 8) });
        }

        private static BuildingState Place(GameWorld world, BuildingKind kind, int x, int z)
            => SimulationTestWorld.Place(world, kind, x, z);

        private static void Advance(GameWorld world, int count) => SimulationTestWorld.Advance(world, count);

        private static void Start(GameWorld world) => SimulationTestWorld.Execute(world, WorldCommand.StartWave());

        [Test]
        public void BattleDisablesSyntheticLoadAndPreparationDoesNotSpendTurretEnergy()
        {
            GameWorld world = Create();
            Place(world, BuildingKind.Core, 13, 8);
            Place(world, BuildingKind.Reactor, 7, 8);
            BuildingState turret = Place(world, BuildingKind.Turret, 8, 8);
            Advance(world, 100);
            Assert.That(world.TestLoadEnabled, Is.False);
            Assert.That(world.Battle.State, Is.EqualTo(BattleState.Preparing));
            Assert.That(turret.StoredEnergy, Is.GreaterThan(0));
            Assert.That(turret.ShotsFired, Is.Zero);
            Assert.That(world.Energy.GetTotals().Consumed, Is.Zero);
        }

        [Test]
        public void StartRequiresCoreAndRepeatedStartDoesNotDuplicateWave()
        {
            GameWorld world = Create();
            world.Enqueue(WorldCommand.StartWave());
            world.Step();
            Assert.That(world.LastCommandResult.Succeeded, Is.False);
            Place(world, BuildingKind.Core, 13, 8);
            Start(world);
            long sequence = world.Enqueue(WorldCommand.StartWave());
            world.Step();
            Assert.That(world.LastCommandResult.Sequence, Is.EqualTo(sequence));
            Assert.That(world.LastCommandResult.Succeeded, Is.False);
            Assert.That(world.Battle.WaveNumber, Is.EqualTo(1));
            Advance(world, 40);
            Assert.That(world.Battle.Enemies.Count, Is.EqualTo(1));
        }

        [Test]
        public void CoreCannotBeMovedSoldOrReplacedToAvoidDefeat()
        {
            GameWorld world = Create(production: 0);
            BuildingState core = Place(world, BuildingKind.Core, 13, 8);
            foreach (WorldCommand command in new[] {
                WorldCommand.Move(core.Id, 12, 8), WorldCommand.Sell(core.Id),
                WorldCommand.Place(BuildingKind.Core, 12, 8) })
            {
                int money = world.Credits;
                world.Enqueue(command);
                world.Step();
                Assert.That(world.LastCommandResult.Succeeded, Is.False);
                Assert.That(world.Credits, Is.EqualTo(money));
            }
            world.Enqueue(WorldCommand.Damage(core.Id, 1000));
            world.Enqueue(WorldCommand.Repair(core.Id));
            world.Step();
            Assert.That(world.Battle.State, Is.EqualTo(BattleState.Defeat));
            Assert.That(core.IsAlive, Is.False);
            Assert.That(world.LastCommandResult.Succeeded, Is.False);
        }

        [Test]
        public void PreparingTransactionsDoNotAdvanceTimeAndCombatEditsCannotRemainQueued()
        {
            GameWorld world = Create(Settings(buildings: 1));
            world.Submit(WorldCommand.Place(BuildingKind.Core, 13, 8));
            long tick = world.CurrentTick;
            int money = world.Credits;
            Assert.That(world.Submit(WorldCommand.Place(BuildingKind.Relay, 2, 8)).Succeeded, Is.False);
            Assert.That(world.Credits, Is.EqualTo(money));
            Assert.That(world.Submit(WorldCommand.Place(BuildingKind.Relay, 5, 5)).Succeeded, Is.True);
            Assert.That(world.CurrentTick, Is.EqualTo(tick));
            Assert.That(world.Buildings.Count, Is.EqualTo(2), "旧建筑数量配置不得限制购买。");

            world.Enqueue(WorldCommand.StartWave());
            world.Enqueue(WorldCommand.Place(BuildingKind.Relay, 6, 5));
            world.ProcessCommands();
            Assert.That(world.LastCommandResult.Succeeded, Is.False);
            Assert.That(world.FindBuildingAt(6, 5), Is.Null);
            money = world.Credits;
            world.Enqueue(WorldCommand.Place(BuildingKind.Relay, 7, 5));
            Assert.That(world.LastCommandResult.Succeeded, Is.False);
            world.ProcessCommands();
            Assert.That(world.CurrentTick, Is.EqualTo(tick));
            Assert.That(world.Credits, Is.EqualTo(money));
            Assert.That(world.FindBuildingAt(7, 5), Is.Null);
        }

        [Test]
        public void InsufficientEnergyNeverPartiallyPaysOrDamagesEnemy()
        {
            GameWorld world = Create(Settings(health: 10000), turretCapacity: 1000);
            Place(world, BuildingKind.Core, 13, 8);
            Place(world, BuildingKind.Reactor, 7, 8);
            BuildingState turret = Place(world, BuildingKind.Turret, 8, 8);
            Advance(world, 100);
            Start(world);
            Advance(world, 100);
            Assert.That(turret.StoredEnergy, Is.EqualTo(1000));
            Assert.That(turret.ShotsFired, Is.Zero);
            Assert.That(turret.CombatState, Is.EqualTo(TurretCombatState.WaitingForEnergy));
            Assert.That(world.Battle.Enemies[0].Health, Is.EqualTo(10000));
            Assert.That(world.Energy.GetTotals().Consumed, Is.Zero);
        }

        [Test]
        public void ShotsSpendExactlyTheirEnergyAndRespectCooldown()
        {
            const long cost = 2000;
            const int cooldown = 8;
            GameWorld world = Create(Settings(health: 10000, energy: cost, cooldown: cooldown));
            Place(world, BuildingKind.Core, 13, 8);
            Place(world, BuildingKind.Reactor, 7, 8);
            BuildingState turret = Place(world, BuildingKind.Turret, 8, 8);
            Advance(world, 100);
            Start(world);
            long lastShotTick = -1000;
            long previousShots = turret.ShotsFired;
            if (previousShots != 0) lastShotTick = world.CurrentTick;
            for (int index = 0; index < 100; index++)
            {
                world.Step();
                if (turret.ShotsFired != previousShots)
                {
                    Assert.That(turret.ShotsFired - previousShots, Is.EqualTo(1));
                    Assert.That(world.CurrentTick - lastShotTick, Is.GreaterThanOrEqualTo(cooldown));
                    lastShotTick = world.CurrentTick;
                    previousShots = turret.ShotsFired;
                }
                SimulationTestWorld.AssertBalanced(world);
            }
            Assert.That(turret.ShotsFired, Is.GreaterThan(1));
            Assert.That(world.Energy.GetTotals().Consumed, Is.EqualTo(turret.ShotsFired * cost));
        }

        [Test]
        public void TargetsOutsideRangeDoNotCauseEnergyConsumption()
        {
            GameWorld world = Create(Settings(range: 1));
            Place(world, BuildingKind.Core, 13, 8);
            Place(world, BuildingKind.Reactor, 7, 8);
            BuildingState turret = Place(world, BuildingKind.Turret, 8, 8);
            Advance(world, 100);
            Start(world);
            Advance(world, 100);
            Assert.That(turret.ShotsFired, Is.Zero);
            Assert.That(world.Energy.GetTotals().Consumed, Is.Zero);
        }

        [Test]
        public void LivingEnemyLimitDelaysRemainingSpawnsWithoutDiscardingThem()
        {
            GameWorld world = Create(Settings(count: 7, interval: 1, limit: 2), production: 0);
            Place(world, BuildingKind.Core, 13, 8);
            Start(world);
            Advance(world, 100);
            Assert.That(world.Battle.Enemies.Count, Is.EqualTo(2));
            Assert.That(world.Battle.RemainingToSpawn, Is.EqualTo(5));
            Assert.That(world.Battle.State, Is.EqualTo(BattleState.Combat));
        }

        [Test]
        public void WavesRewardOnceWithoutRestoringCoreAndRequireContinuePreparing()
        {
            GameWorld world = new GameWorld(new[] {
                new BuildingDefinition(BuildingKind.Reactor, "反应堆", 1, 1, 100000, 10000, 0, 100000, 1, 3, 1000, 10, 5, 3),
                Definition(BuildingKind.Turret), Definition(BuildingKind.Core)
            }, new WorldSettings(16, 16, 20, 10000), new EnergySettings(200, 1, 1000, 0),
                Settings(turretDamage: 100, waves: new[] {
                    new WaveDefinition(1, 20, 2, 300), new WaveDefinition(1, 20, 2, 400) }));
            BuildingState core = Place(world, BuildingKind.Core, 15, 15);
            world.Submit(WorldCommand.Damage(core.Id, 100));
            Place(world, BuildingKind.Reactor, 1, 1);
            BuildingState turret = Place(world, BuildingKind.Turret, 2, 1);
            Advance(world, 10);
            int before = world.Credits;
            Start(world);
            SimulationTestWorld.AdvanceUntil(world, () => world.Battle.State == BattleState.WaveCleared,
                100, "第一波应清场。");
            int reward = world.Battle.LastWaveReward;
            Assert.That(reward, Is.EqualTo(world.Battle.LastWaveFixedReward + world.Battle.LastWaveKillReward));
            Assert.That(world.Credits, Is.EqualTo(before + reward));
            Assert.That(core.Health, Is.EqualTo(900));
            // 核心离反应堆的直线距离超过传输范围；没有到货就不能凭清波得到护盾。
            Assert.That(core.StoredEnergy, Is.Zero);
            long generated = world.Energy.GetTotals().Generated;
            Advance(world, 3);
            Assert.That(world.Credits, Is.EqualTo(before + reward));
            Assert.That(world.Submit(WorldCommand.StartWave()).Succeeded, Is.False);
            Assert.That(world.Submit(WorldCommand.ContinuePreparing()).Succeeded, Is.True);
            Assert.That(world.CanEditBuildings, Is.True);
            Assert.That(world.FindBuildingAt(7, 5), Is.Null);
            Assert.That(world.Energy.GetTotals().Generated, Is.GreaterThanOrEqualTo(generated));
            Assert.That(world.Credits, Is.EqualTo(before + reward));
        }

        [Test]
        public void WaveClearDoesNotAutomaticallyRepairBuildings()
        {
            GameWorld world = Create(Settings(health: 20));
            Place(world, BuildingKind.Core, 13, 8);
            Place(world, BuildingKind.Reactor, 7, 8);
            BuildingState turret = Place(world, BuildingKind.Turret, 8, 8);
            SimulationTestWorld.Execute(world, WorldCommand.Damage(turret.Id, 200));
            Advance(world, 100);
            Start(world);
            SimulationTestWorld.AdvanceUntil(world, () => world.Battle.State == BattleState.Victory,
                300, "应击败最后的敌人。");
            Assert.That(turret.Health, Is.EqualTo(800));
        }

        [Test]
        public void OpenRouteGoesAroundBuildingsWithoutAttackingThem()
        {
            GameWorld world = Create(Settings(speed: 3, range: 0.5), production: 0);
            BuildingState core = Place(world, BuildingKind.Core, 13, 8);
            BuildingState wall = Place(world, BuildingKind.Relay, 6, 8);
            Start(world);
            SimulationTestWorld.AdvanceUntil(world, () => core.Health < 1000, 300,
                "敌人应绕开单个阻挡建筑并到达核心。");
            Assert.That(wall.Health, Is.EqualTo(1000));
        }

        [Test]
        public void ClosedRouteAttacksOneMultiCellBuildingAndThenAdvances()
        {
            GameWorld world = Create(Settings(speed: 3, damage: 250, attackInterval: 2), production: 0,
                relay: Definition(BuildingKind.Relay, width: 2, depth: 16));
            BuildingState core = Place(world, BuildingKind.Core, 13, 8);
            BuildingState wall = Place(world, BuildingKind.Relay, 6, 0);
            Start(world);
            SimulationTestWorld.AdvanceUntil(world, () => !wall.IsAlive, 300,
                "完全封路时必须攻击可接近的阻挡建筑。");
            Assert.That(core.Health, Is.EqualTo(1000));
            SimulationTestWorld.AdvanceUntil(world, () => core.Health < 1000, 300,
                "拆除后应更新路线并抵达核心。");
        }
    }
}
