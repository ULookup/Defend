using System;
using System.Collections.Generic;
using System.Linq;
using Defend.Simulation;
using NUnit.Framework;

namespace Defend.Tests
{
    public sealed class EnergyTransportTests
    {
        [Test]
        public void FullReactorUsesBackpressureWithoutCreatingUnstoredEnergy()
        {
            var world = SimulationTestWorld.Create(reactor:
                SimulationTestWorld.Definition(BuildingKind.Reactor, capacity: 2500, production: 1000));
            var reactor = SimulationTestWorld.Place(world, BuildingKind.Reactor, 1, 1);

            SimulationTestWorld.Advance(world, 10);

            Assert.That(reactor.StoredEnergy, Is.EqualTo(2500));
            Assert.That(world.Energy.GetTotals().Generated, Is.EqualTo(2500));
            Assert.That(world.Energy.GetTotals().ExpiredLoss, Is.Zero,
                "尚未派发的反应堆库存不属于等待运输的旧批次。");
        }

        [Test]
        public void EveryHopHasPositiveDelayAndRelayCannotDeliverBeforeArrival()
        {
            var world = SimulationTestWorld.Create(
                reactor: SimulationTestWorld.Definition(BuildingKind.Reactor, production: 1000,
                    outputAmount: 1000, range: 2.1),
                relay: SimulationTestWorld.Definition(BuildingKind.Relay, outputAmount: 1000, range: 2.1));
            var relay = SimulationTestWorld.Place(world, BuildingKind.Relay, 2, 0);
            var turret = SimulationTestWorld.Place(world, BuildingKind.Turret, 4, 0);
            var reactor = SimulationTestWorld.Place(world, BuildingKind.Reactor, 0, 0);
            var firstHop = world.Energy.Transfers.First(transfer => transfer.SourceId == reactor.Id);

            Assert.That(firstHop.TargetId, Is.EqualTo(relay.Id));
            Assert.That(firstHop.ArrivalTick, Is.GreaterThan(firstHop.StartTick));
            Assert.That(turret.TotalReceived, Is.Zero);
            while (world.CurrentTick < firstHop.ArrivalTick)
            {
                world.Step();
                SimulationTestWorld.AssertBalanced(world);
                Assert.That(turret.TotalReceived, Is.Zero,
                    "到达中继的逻辑步不能已经完成下一跳的到货。");
            }
            foreach (var transfer in world.Energy.Transfers)
                Assert.That(transfer.ArrivalTick, Is.GreaterThan(transfer.StartTick));

            SimulationTestWorld.AdvanceUntil(world, () => turret.TotalReceived > 0, 10,
                "合法的第二跳应在之后到货。");
        }

        [Test]
        public void MultipleSourcesCannotReserveConsumerCapacityTwice()
        {
            var world = SimulationTestWorld.Create(
                reactor: SimulationTestWorld.Definition(BuildingKind.Reactor,
                    production: 5000, outputAmount: 5000, range: 5),
                turret: SimulationTestWorld.Definition(BuildingKind.Turret, capacity: 2500),
                energySettings: new EnergySettings(100, 4, 1000, 0.35));
            var turret = SimulationTestWorld.Place(world, BuildingKind.Turret, 5, 5);
            SimulationTestWorld.Place(world, BuildingKind.Reactor, 2, 5);
            SimulationTestWorld.Place(world, BuildingKind.Reactor, 5, 2);

            SimulationTestWorld.Advance(world, 60);

            Assert.That(turret.StoredEnergy, Is.EqualTo(2500));
            Assert.That(turret.CommittedIncoming, Is.Zero);
            Assert.That(turret.TotalReceived, Is.EqualTo(2500));
        }

        [Test]
        public void RelaySharesOneOutputBudgetAcrossAllRecipients()
        {
            var world = SimulationTestWorld.Create(
                reactor: SimulationTestWorld.Definition(BuildingKind.Reactor, production: 9000,
                    outputAmount: 9000, range: 2.1),
                relay: SimulationTestWorld.Definition(BuildingKind.Relay, outputAmount: 3000,
                    outputInterval: 4, range: 2.5));
            var relay = SimulationTestWorld.Place(world, BuildingKind.Relay, 2, 0);
            var firstTurret = SimulationTestWorld.Place(world, BuildingKind.Turret, 4, 0);
            var secondTurret = SimulationTestWorld.Place(world, BuildingKind.Turret, 4, 1);
            SimulationTestWorld.Place(world, BuildingKind.Reactor, 0, 0);
            var observedTransfers = new HashSet<long>();
            var outputByTick = new SortedDictionary<long, long>();

            for (var index = 0; index < 80; index++)
            {
                world.Step();
                SimulationTestWorld.AssertBalanced(world);
                foreach (var transfer in world.Energy.Transfers)
                {
                    if (transfer.SourceId != relay.Id || !observedTransfers.Add(transfer.Id))
                        continue;
                    outputByTick.TryGetValue(transfer.StartTick, out var previousAmount);
                    outputByTick[transfer.StartTick] = previousAmount + transfer.Amount;
                }
                // 保持两个终端持续有需求，排除满仓造成的假性吞吐下降。
                world.Energy.ConsumeUpTo(firstTurret.Id, 100000);
                world.Energy.ConsumeUpTo(secondTurret.Id, 100000);
            }

            Assert.That(outputByTick.Count, Is.GreaterThan(5));
            long previousTick = long.MinValue;
            foreach (var output in outputByTick)
            {
                Assert.That(output.Value, Is.LessThanOrEqualTo(3000),
                    "一次输出量属于整栋中继，不能给每条出边各分配一整份。");
                if (previousTick != long.MinValue)
                    Assert.That(output.Key - previousTick, Is.GreaterThanOrEqualTo(4));
                previousTick = output.Key;
            }
            Assert.That(firstTurret.TotalReceived, Is.GreaterThan(0));
            Assert.That(secondTurret.TotalReceived, Is.GreaterThan(0));
        }

        [Test]
        public void NormalOutputCooldownDoesNotExpireNewArrivalsButMissedOpportunitiesDo()
        {
            var world = SimulationTestWorld.Create(
                reactor: SimulationTestWorld.Definition(BuildingKind.Reactor,
                    production: 1000, outputAmount: 1000, range: 2.1),
                relay: SimulationTestWorld.Definition(BuildingKind.Relay,
                    outputAmount: 1000, outputInterval: 10, range: 2.1),
                energySettings: new EnergySettings(5, 1, 1000, 0));
            var relay = SimulationTestWorld.Place(world, BuildingKind.Relay, 2, 0);
            SimulationTestWorld.Place(world, BuildingKind.Turret, 4, 0);
            SimulationTestWorld.Place(world, BuildingKind.Reactor, 0, 0);
            SimulationTestWorld.AdvanceUntil(world,
                () => world.Energy.Transfers.Any(transfer => transfer.SourceId == relay.Id), 10,
                "先等中继完成第一次输出，建立正常冷却窗口。");

            SimulationTestWorld.Advance(world, 9);
            Assert.That(world.Energy.GetTotals().ExpiredLoss, Is.Zero,
                "新到批次等待首次正常输出机会，不应因为超时时限短于输出间隔而过期。");

            SimulationTestWorld.Advance(world, 6);
            Assert.That(world.Energy.GetTotals().ExpiredLoss, Is.GreaterThan(0),
                "多个批次错失已经到来的输出机会后，持续拥堵应逐步累计并过期。");
        }

        [Test]
        public void DestroyingSenderDoesNotRecallAnAlreadyDepartedTransfer()
        {
            var world = CreateSingleTransferWorld(out var reactor, out var turret);
            var transfer = world.Energy.Transfers.Single();
            SimulationTestWorld.Execute(world, WorldCommand.Damage(reactor.Id, 100));

            SimulationTestWorld.AdvanceUntil(world, () => world.CurrentTick > transfer.ArrivalTick, 30,
                "测试必须执行到原到货时刻之后。");

            Assert.That(turret.TotalReceived, Is.EqualTo(transfer.Amount));
            Assert.That(world.Energy.GetTotals().DestructionLoss, Is.Zero);
        }

        [Test]
        public void MovingReceiverInvalidatesOldPositionEvenWhenNewPositionIsInRange()
        {
            var world = CreateSingleTransferWorld(out var reactor, out var turret);
            var transfer = world.Energy.Transfers.Single();
            world.Enqueue(WorldCommand.Damage(reactor.Id, 100));
            SimulationTestWorld.Execute(world, WorldCommand.Move(turret.Id, 3, 1));

            SimulationTestWorld.AdvanceUntil(world, () => world.CurrentTick > transfer.ArrivalTick, 30,
                "旧端点的过时到货事件必须被处理或忽略。");

            Assert.That(turret.TotalReceived, Is.Zero);
            Assert.That(turret.CommittedIncoming, Is.Zero);
            Assert.That(turret.ReservedIncoming, Is.Zero);
            Assert.That(world.Energy.GetTotals().DestructionLoss, Is.EqualTo(transfer.Amount));
        }

        [Test]
        public void RepairCannotResurrectTransferCancelledByReceiverDestruction()
        {
            var world = CreateSingleTransferWorld(out var reactor, out var turret);
            var transfer = world.Energy.Transfers.Single();
            world.Enqueue(WorldCommand.Damage(reactor.Id, 100));
            SimulationTestWorld.Execute(world, WorldCommand.Damage(turret.Id, 100));
            SimulationTestWorld.Execute(world, WorldCommand.Repair(turret.Id));

            SimulationTestWorld.AdvanceUntil(world, () => world.CurrentTick > transfer.ArrivalTick, 30,
                "修复必须早于旧到货事件，才能覆盖生命代次误命中的风险。");

            Assert.That(turret.IsAlive, Is.True);
            Assert.That(turret.TotalReceived, Is.Zero);
            Assert.That(turret.StoredEnergy, Is.Zero);
            Assert.That(world.Energy.GetTotals().DestructionLoss, Is.EqualTo(transfer.Amount));
            Assert.That(world.Energy.GetTotals().InFlight, Is.Zero);
        }

        [Test]
        public void NewBuildingAtSameCellCannotReceiveSoldBuildingsTransfer()
        {
            var world = CreateSingleTransferWorld(out var reactor, out var turret);
            var transfer = world.Energy.Transfers.Single();
            world.Enqueue(WorldCommand.Damage(reactor.Id, 100));
            SimulationTestWorld.Execute(world, WorldCommand.Sell(turret.Id));
            var replacement = SimulationTestWorld.Place(world, BuildingKind.Turret, 3, 0);

            SimulationTestWorld.AdvanceUntil(world, () => world.CurrentTick > transfer.ArrivalTick, 30,
                "测试应跨过发给旧建筑的到货时刻。");

            Assert.That(replacement.Id, Is.Not.EqualTo(turret.Id));
            Assert.That(replacement.StoredEnergy, Is.Zero);
            Assert.That(replacement.TotalReceived, Is.Zero);
            Assert.That(world.Energy.GetTotals().DestructionLoss, Is.EqualTo(transfer.Amount));
        }

        [Test]
        public void StoredConsumerEnergyDoesNotExpireAndIsConsumedBeforeRequestingReplacement()
        {
            var world = CreateSingleTransferWorld(out var reactor, out var turret);
            var transfer = world.Energy.Transfers.Single();
            SimulationTestWorld.Execute(world, WorldCommand.Damage(reactor.Id, 100));
            SimulationTestWorld.Advance(world, 250);

            Assert.That(turret.StoredEnergy, Is.EqualTo(transfer.Amount));
            Assert.That(world.Energy.GetTotals().ExpiredLoss, Is.Zero);
            Assert.That(world.Energy.TryConsume(turret.Id, transfer.Amount + 1), Is.False);
            Assert.That(turret.StoredEnergy, Is.EqualTo(transfer.Amount),
                "不足额的原子消耗不能先扣除一部分。");
            Assert.That(world.Energy.ConsumeUpTo(turret.Id, transfer.Amount + 1), Is.EqualTo(transfer.Amount));
            Assert.That(turret.StoredEnergy, Is.Zero);
            SimulationTestWorld.AssertBalanced(world);
        }

        [Test]
        public void DestroyingConsumerLosesLocalBufferWithoutReturningItToNetwork()
        {
            var world = CreateSingleTransferWorld(out var reactor, out var turret);
            var transfer = world.Energy.Transfers.Single();
            SimulationTestWorld.Execute(world, WorldCommand.Damage(reactor.Id, 100));
            SimulationTestWorld.AdvanceUntil(world, () => turret.StoredEnergy > 0, 30,
                "终端应先获得自用缓存。");

            SimulationTestWorld.Execute(world, WorldCommand.Damage(turret.Id, 100));

            Assert.That(turret.StoredEnergy, Is.Zero);
            Assert.That(world.Energy.GetTotals().DestructionLoss, Is.EqualTo(transfer.Amount));
            Assert.That(world.Energy.GetTotals().Stored, Is.Zero);
            Assert.That(world.Energy.GetTotals().InFlight, Is.Zero);
        }

        [Test, Category("BuildingRotation")]
        public void RotatingSquareRelayInPlacePreservesItsIncomingEnergyAndPositionRevision()
        {
            var world = SimulationTestWorld.Create(
                reactor: SimulationTestWorld.Definition(BuildingKind.Reactor, capacity: 1000,
                    production: 1000, outputAmount: 1000, range: 3),
                relay: SimulationTestWorld.Definition(BuildingKind.Relay, capacity: 1000, outputAmount: 1000, range: 3),
                turret: SimulationTestWorld.Definition(BuildingKind.Turret, capacity: 1000),
                energySettings: new EnergySettings(100, 4, 1000, 0));
            SimulationTestWorld.Place(world, BuildingKind.Turret, 6, 0);
            var relay = SimulationTestWorld.Place(world, BuildingKind.Relay, 3, 0);
            var reactor = SimulationTestWorld.Place(world, BuildingKind.Reactor, 0, 0);
            var transfer = world.Energy.Transfers.Single();
            Assert.That(transfer.TargetId, Is.EqualTo(relay.Id));
            int revision = relay.PositionRevision;
            long incoming = relay.ReservedIncoming, tick = world.CurrentTick;

            Assert.That(world.Submit(WorldCommand.Move(relay.Id, relay.X, relay.Z, 1)).Succeeded, Is.True);
            Assert.That(relay.OrientationQuarterTurns, Is.EqualTo(1));
            Assert.That(relay.PositionRevision, Is.EqualTo(revision));
            Assert.That(relay.ReservedIncoming, Is.EqualTo(incoming));
            Assert.That(world.Energy.Transfers.Single().Id, Is.EqualTo(transfer.Id));
            Assert.That(world.Energy.GetTotals().DestructionLoss, Is.Zero);
            Assert.That(world.CurrentTick, Is.EqualTo(tick));

            Assert.That(world.Submit(WorldCommand.Damage(reactor.Id, 100)).Succeeded, Is.True);
            SimulationTestWorld.AdvanceUntil(world, () => relay.TotalReceived > 0, 30, "原地旋转后原接收批次仍须按时到货。");
            Assert.That(relay.TotalReceived, Is.EqualTo(transfer.Amount));
            SimulationTestWorld.AssertBalanced(world);
        }

        [TestCase(1, true)]
        [TestCase(2, false)]
        [Category("BuildingRotation")]
        public void RectangularReceiverRotationCancelsIncomingOnlyWhenFootprintChanges(int orientation, bool footprintChanges)
        {
            var world = SimulationTestWorld.Create(
                reactor: SimulationTestWorld.Definition(BuildingKind.Reactor, capacity: 1000,
                    production: 1000, outputAmount: 1000, range: 8),
                turret: SimulationTestWorld.Definition(BuildingKind.Turret, capacity: 1000, width: 2, depth: 3),
                energySettings: new EnergySettings(100, 4, 1000, 0));
            var turret = SimulationTestWorld.Place(world, BuildingKind.Turret, 3, 0);
            var reactor = SimulationTestWorld.Place(world, BuildingKind.Reactor, 0, 0);
            var transfer = world.Energy.Transfers.Single();
            int revision = turret.PositionRevision;

            Assert.That(world.Submit(WorldCommand.Move(turret.Id, turret.X, turret.Z, orientation)).Succeeded, Is.True);
            Assert.That(turret.PositionRevision, Is.EqualTo(revision + (footprintChanges ? 1 : 0)));
            Assert.That(world.Energy.GetTotals().DestructionLoss, Is.EqualTo(footprintChanges ? transfer.Amount : 0));
            Assert.That(world.Submit(WorldCommand.Damage(reactor.Id, 100)).Succeeded, Is.True);
            SimulationTestWorld.AdvanceUntil(world, () => world.CurrentTick > transfer.ArrivalTick, 40, "检查原接收批次到货时刻。");
            Assert.That(turret.TotalReceived, Is.EqualTo(footprintChanges ? 0 : transfer.Amount));
            SimulationTestWorld.AssertBalanced(world);
        }

        private static GameWorld CreateSingleTransferWorld(
            out BuildingState reactor, out BuildingState turret)
        {
            var world = SimulationTestWorld.Create(
                reactor: SimulationTestWorld.Definition(BuildingKind.Reactor, capacity: 1000,
                    production: 1000, outputAmount: 1000, range: 5),
                turret: SimulationTestWorld.Definition(BuildingKind.Turret, capacity: 1000),
                energySettings: new EnergySettings(100, 4, 1000, 0));
            turret = SimulationTestWorld.Place(world, BuildingKind.Turret, 3, 0);
            reactor = SimulationTestWorld.Place(world, BuildingKind.Reactor, 0, 0);
            Assert.That(world.Energy.Transfers.Count, Is.EqualTo(1),
                "本场景以单个尚未到货的运输批次作为生命周期测试起点。");
            return world;
        }
    }
}
