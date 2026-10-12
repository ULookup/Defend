using System.Linq;
using Defend.Simulation;
using NUnit.Framework;

namespace Defend.Tests
{
    public sealed class BatterySupplyTests
    {
        [Test]
        public void BatteryCommitmentsRespectCapacityAndBalanceStopsBeforeReversal()
        {
            var world = new GameWorld(new[]
            {
                SimulationTestWorld.Definition(BuildingKind.Reactor, capacity: 10000, production: 10000, outputAmount: 10000, range: 1.1),
                SimulationTestWorld.Definition(BuildingKind.Battery, capacity: 10000, outputAmount: 10000, range: 3.1),
                SimulationTestWorld.Definition(BuildingKind.Turret, capacity: 10000)
            }, new WorldSettings(20, 20, 20, 10000), new EnergySettings(20, 1, 10000, 0));
            var first = SimulationTestWorld.Place(world, BuildingKind.Battery, 1, 1);
            var reactor = SimulationTestWorld.Place(world, BuildingKind.Reactor, 0, 1);
            SimulationTestWorld.Advance(world, 3);
            SimulationTestWorld.Execute(world, WorldCommand.Sell(reactor.Id));
            Assert.That(first.StoredEnergy, Is.EqualTo(10000));
            world.Enqueue(WorldCommand.Place(BuildingKind.Battery, 3, 1));
            world.Enqueue(WorldCommand.Place(BuildingKind.Turret, 9, 1));
            world.Step();
            var second = world.FindBuildingAt(3, 1);
            var terminal = world.FindBuildingAt(9, 1);
            SimulationTestWorld.Advance(world, 8);
            Assert.That(first.StoredEnergy, Is.EqualTo(5500));
            Assert.That(second.StoredEnergy, Is.EqualTo(4500));
            long discharged = first.TotalDischarged;
            SimulationTestWorld.Advance(world, 3);
            Assert.That(first.TotalDischarged, Is.EqualTo(discharged), "比例接近后不能继续新增互补。");
            SimulationTestWorld.Execute(world, WorldCommand.Move(terminal.Id, 4, 1));
            SimulationTestWorld.Advance(world, 8);
            Assert.That(terminal.StoredEnergy, Is.GreaterThan(0));
            Assert.That(terminal.StoredEnergy + terminal.CommittedIncoming, Is.LessThanOrEqualTo(7500));
            foreach (var battery in new[] { first, second })
                Assert.That(battery.StoredEnergy + battery.CommittedIncoming, Is.LessThanOrEqualTo(10000));
        }

        [Test]
        public void BatteryKeepsHistoryAndCannotDischargeAnArrivalInTheSameTick()
        {
            var world = new GameWorld(new[]
            {
                SimulationTestWorld.Definition(BuildingKind.Reactor, capacity: 2000, production: 2000, outputAmount: 2000, range: 2.1),
                SimulationTestWorld.Definition(BuildingKind.Relay, capacity: 4000, outputAmount: 2000, range: 2.1),
                SimulationTestWorld.Definition(BuildingKind.Battery, capacity: 4000, outputAmount: 2000, range: 2.1),
                SimulationTestWorld.Definition(BuildingKind.Turret, capacity: 4000)
            }, new WorldSettings(20, 20, 20, 10000), new EnergySettings(10, 1, 2000, 0));
            var relay = SimulationTestWorld.Place(world, BuildingKind.Relay, 2, 1);
            var battery = SimulationTestWorld.Place(world, BuildingKind.Battery, 4, 1);
            var terminal = SimulationTestWorld.Place(world, BuildingKind.Turret, 6, 1);
            var reactor = SimulationTestWorld.Place(world, BuildingKind.Reactor, 0, 1);
            SimulationTestWorld.AdvanceUntil(world, () => battery.TotalReceived > 0, 8, "中继应给电池实际充电。");
            Assert.That(world.Energy.Departures.Any(t => t.SourceId == battery.Id), Is.False);
            var stored = world.Energy.BatchDiagnostics.First(b => b.BuildingId == battery.Id);
            Assert.That(stored.History, Is.EqualTo(new[] { reactor.Id, relay.Id, battery.Id }));
            SimulationTestWorld.Execute(world, WorldCommand.Sell(reactor.Id));
            SimulationTestWorld.Advance(world, 8);
            Assert.That(terminal.TotalReceived, Is.GreaterThan(0));
            // 新建目标只能经已访问过的中继到达，旧储存批次不能回流，也不因正常存储超时丢失。
            SimulationTestWorld.Execute(world, WorldCommand.Sell(terminal.Id));
            SimulationTestWorld.Execute(world, WorldCommand.Place(BuildingKind.Turret, 0, 1));
            long retained = battery.StoredEnergy;
            Assert.That(retained, Is.GreaterThan(0), "保留实际库存才能检查正常存储不老化。");
            SimulationTestWorld.Advance(world, 12);
            Assert.That(battery.StoredEnergy, Is.EqualTo(retained));
            Assert.That(world.Energy.BatchDiagnostics.Where(b => b.BuildingId == battery.Id).All(b =>
                b.History.Contains(relay.Id) && b.History.Contains(battery.Id)), Is.True);
        }
    }
}
