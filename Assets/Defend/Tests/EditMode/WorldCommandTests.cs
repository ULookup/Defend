using Defend.Simulation;
using NUnit.Framework;

namespace Defend.Tests
{
    public sealed class WorldCommandTests
    {
        [Test]
        public void PlacementClaimsEveryCellOfBuildingFootprint()
        {
            var world = SimulationTestWorld.Create(
                relay: SimulationTestWorld.Definition(BuildingKind.Relay, width: 2, depth: 3));
            var building = SimulationTestWorld.Place(world, BuildingKind.Relay, 4, 6);

            for (var x = 4; x < 6; x++)
                for (var z = 6; z < 9; z++)
                    Assert.That(world.FindBuildingAt(x, z), Is.SameAs(building));
            Assert.That(world.FindBuildingAt(6, 6), Is.Null);
            Assert.That(world.FindBuildingAt(4, 9), Is.Null);
        }

        [Test]
        public void OverlappingPlacementDoesNotSpendCreditsOrCreateBuilding()
        {
            var world = SimulationTestWorld.Create();
            var original = SimulationTestWorld.Place(world, BuildingKind.Relay, 4, 6);
            var creditsBefore = world.Credits;
            var countBefore = world.Buildings.Count;

            world.Enqueue(WorldCommand.Place(BuildingKind.Turret, 4, 6));
            world.Step();

            Assert.That(world.LastCommandResult.Succeeded, Is.False);
            Assert.That(world.Credits, Is.EqualTo(creditsBefore));
            Assert.That(world.Buildings.Count, Is.EqualTo(countBefore));
            Assert.That(world.FindBuildingAt(4, 6), Is.SameAs(original));
        }

        [Test]
        public void PlacementOutsideMapDoesNotSpendCredits()
        {
            var world = SimulationTestWorld.Create(
                relay: SimulationTestWorld.Definition(BuildingKind.Relay, width: 2, depth: 2));
            var creditsBefore = world.Credits;

            world.Enqueue(WorldCommand.Place(BuildingKind.Relay, 39, 39));
            world.Step();

            Assert.That(world.LastCommandResult.Succeeded, Is.False);
            Assert.That(world.Credits, Is.EqualTo(creditsBefore));
            Assert.That(world.Buildings, Is.Empty);
        }

        [Test]
        public void PlacementWithoutEnoughCreditsLeavesWorldUnchanged()
        {
            var world = SimulationTestWorld.Create(credits: 99);
            world.Enqueue(WorldCommand.Place(BuildingKind.Relay, 4, 6));
            world.Step();

            Assert.That(world.LastCommandResult.Succeeded, Is.False);
            Assert.That(world.Credits, Is.EqualTo(99));
            Assert.That(world.Buildings, Is.Empty);
        }

        [Test]
        public void MovePreservesIdentityAndReplacesOccupiedCellsAtomically()
        {
            var world = SimulationTestWorld.Create(
                relay: SimulationTestWorld.Definition(BuildingKind.Relay, width: 2));
            var building = SimulationTestWorld.Place(world, BuildingKind.Relay, 4, 6);
            var identity = building.Id;
            var lifeGeneration = building.LifeGeneration;
            var revision = building.PositionRevision;

            // 新旧范围重叠属于合法移动，不应把建筑自身视作障碍物。
            SimulationTestWorld.Execute(world, WorldCommand.Move(identity, 5, 6));

            Assert.That(world.FindBuilding(identity), Is.SameAs(building));
            Assert.That(building.LifeGeneration, Is.EqualTo(lifeGeneration));
            Assert.That(building.PositionRevision, Is.GreaterThan(revision));
            Assert.That(world.FindBuildingAt(4, 6), Is.Null);
            Assert.That(world.FindBuildingAt(5, 6), Is.SameAs(building));
            Assert.That(world.FindBuildingAt(6, 6), Is.SameAs(building));
        }

        [Test]
        public void FailedMoveKeepsPositionRevisionAndOriginalOccupancy()
        {
            var world = SimulationTestWorld.Create();
            var building = SimulationTestWorld.Place(world, BuildingKind.Relay, 4, 6);
            var obstacle = SimulationTestWorld.Place(world, BuildingKind.Turret, 5, 6);
            var revision = building.PositionRevision;
            var credits = world.Credits;

            world.Enqueue(WorldCommand.Move(building.Id, 5, 6));
            world.Step();

            Assert.That(world.LastCommandResult.Succeeded, Is.False);
            Assert.That(building.X, Is.EqualTo(4));
            Assert.That(building.Z, Is.EqualTo(6));
            Assert.That(building.PositionRevision, Is.EqualTo(revision));
            Assert.That(world.FindBuildingAt(4, 6), Is.SameAs(building));
            Assert.That(world.FindBuildingAt(5, 6), Is.SameAs(obstacle));
            Assert.That(world.Credits, Is.EqualTo(credits));
        }

        [Test]
        public void RepairQuotesRoundPerBuildingAndAllRepairIsAtomic()
        {
            var world = SimulationTestWorld.Create(credits: 401);
            var first = SimulationTestWorld.Place(world, BuildingKind.Turret, 4, 6);
            var second = SimulationTestWorld.Place(world, BuildingKind.Turret, 5, 6);
            var destroyed = SimulationTestWorld.Place(world, BuildingKind.Relay, 7, 6);
            var core = SimulationTestWorld.Place(world, BuildingKind.Core, 9, 6);
            world.Submit(WorldCommand.Damage(first.Id, 1));
            world.Submit(WorldCommand.Damage(second.Id, 1));
            world.Submit(WorldCommand.Damage(destroyed.Id, 100));
            world.Submit(WorldCommand.Damage(core.Id, 1));

            Assert.That(world.RepairQuote(first.Id).Cost, Is.EqualTo(1));
            Assert.That(world.RepairAllQuote().Cost, Is.EqualTo(2));
            Assert.That(world.RepairAllQuote().BuildingCount, Is.EqualTo(2));
            Assert.That(world.Submit(WorldCommand.RepairAll()).Succeeded, Is.False);
            Assert.That(first.Health, Is.EqualTo(99));
            Assert.That(second.Health, Is.EqualTo(99));
            Assert.That(world.Credits, Is.EqualTo(1));
            Assert.That(world.Submit(WorldCommand.Repair(first.Id)).Succeeded, Is.True);
            Assert.That(first.Health, Is.EqualTo(100));
            Assert.That(world.Credits, Is.Zero);
            Assert.That(world.Submit(WorldCommand.Repair(destroyed.Id)).Succeeded, Is.False);
            Assert.That(world.Submit(WorldCommand.Sell(destroyed.Id)).Succeeded, Is.False);
            Assert.That(world.Submit(WorldCommand.Repair(core.Id)).Succeeded, Is.False);
            Assert.That(core.Health, Is.EqualTo(99));
            Assert.That(world.FindBuildingAt(7, 6), Is.Null);
        }

        [Test]
        public void RepairOfLivingBuildingDoesNotAdvanceLifeGeneration()
        {
            var world = SimulationTestWorld.Create();
            var building = SimulationTestWorld.Place(world, BuildingKind.Relay, 4, 6);
            var generation = building.LifeGeneration;
            SimulationTestWorld.Execute(world, WorldCommand.Damage(building.Id, 20));
            SimulationTestWorld.Execute(world, WorldCommand.Repair(building.Id));

            Assert.That(building.LifeGeneration, Is.EqualTo(generation));
            Assert.That(building.Health, Is.EqualTo(building.Definition.MaximumHealth));
        }

        [Test]
        public void BlockedRepairDoesNotSpendCreditsOrDisplaceNewBuilding()
        {
            var world = SimulationTestWorld.Create();
            var building = SimulationTestWorld.Place(world, BuildingKind.Relay, 4, 6);
            SimulationTestWorld.Execute(world, WorldCommand.Damage(building.Id, 100));
            var replacement = SimulationTestWorld.Place(world, BuildingKind.Turret, 4, 6);
            var credits = world.Credits;
            var generation = building.LifeGeneration;

            world.Enqueue(WorldCommand.Repair(building.Id));
            world.Step();

            Assert.That(world.LastCommandResult.Succeeded, Is.False);
            Assert.That(world.Credits, Is.EqualTo(credits));
            Assert.That(building.LifeGeneration, Is.EqualTo(generation));
            Assert.That(building.IsAlive, Is.False);
            Assert.That(world.FindBuildingAt(4, 6), Is.SameAs(replacement));
        }

        [Test]
        public void SoldBuildingCannotBeRepairedAndReplacementGetsNewIdentity()
        {
            var world = SimulationTestWorld.Create();
            var building = SimulationTestWorld.Place(world, BuildingKind.Relay, 4, 6);
            SimulationTestWorld.Execute(world, WorldCommand.Sell(building.Id));
            var credits = world.Credits;
            world.Enqueue(WorldCommand.Repair(building.Id));
            world.Step();

            Assert.That(world.LastCommandResult.Succeeded, Is.False);
            Assert.That(world.Credits, Is.EqualTo(credits));
            Assert.That(building.IsSold, Is.True);
            var replacement = SimulationTestWorld.Place(world, BuildingKind.Relay, 4, 6);
            Assert.That(replacement.Id, Is.Not.EqualTo(building.Id));
        }

        [Test]
        public void PriorityCanChangeOnlyForFinalConsumers()
        {
            var world = SimulationTestWorld.Create();
            var relay = SimulationTestWorld.Place(world, BuildingKind.Relay, 4, 6);
            var turret = SimulationTestWorld.Place(world, BuildingKind.Turret, 5, 6);

            world.Enqueue(WorldCommand.SetPriority(relay.Id, 4));
            world.Step();
            Assert.That(world.LastCommandResult.Succeeded, Is.False);
            SimulationTestWorld.Execute(world, WorldCommand.SetPriority(turret.Id, 4));
            Assert.That(turret.PriorityWeight, Is.EqualTo(4));
        }
    }
}
