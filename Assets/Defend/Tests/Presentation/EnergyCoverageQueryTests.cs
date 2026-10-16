using Defend.Simulation;
using Defend.Unity;
using NUnit.Framework;

namespace Defend.Tests
{
    public sealed class EnergyCoverageQueryTests
    {
        [Test]
        public void CoverageIncludesAllSourceKindsTakesBestEfficiencyAndExcludesMovingSource()
        {
            var world = CreateWorld();
            var reactor = Place(world, BuildingKind.Reactor, 2, 2);
            var relay = Place(world, BuildingKind.Relay, 6, 2);
            var battery = Place(world, BuildingKind.Battery, 10, 2);
            foreach (var source in new[] { reactor, relay, battery })
            {
                Assert.That(EnergyCoverageQuery.BestSource(world, source.CenterX, source.CenterZ, 0,
                    out double efficiency), Is.SameAs(source));
                Assert.That(efficiency, Is.EqualTo(1), "重叠区域应取最高效率，不能把多个来源相加。");
            }
            Assert.That(relay.StoredEnergy, Is.Zero, "覆盖查询不要求已经收到能量。");
            var best = EnergyCoverageQuery.BestSource(world, relay.CenterX, relay.CenterZ, relay.Id,
                out double excludedEfficiency);
            Assert.That(best, Is.SameAs(reactor));
            Assert.That(excludedEfficiency, Is.EqualTo(world.Energy.DirectEfficiencyAt(reactor,
                relay.CenterX, relay.CenterZ)));
            Assert.That(excludedEfficiency, Is.LessThan(1));
        }

        [Test]
        public void CoverageTracksMovementSaleDestructionAndConfiguredEdgeEfficiency()
        {
            var world = CreateWorld(new EnergySettings(maximumLossAtRange: .6));
            var source = Place(world, BuildingKind.Battery, 2, 2);
            Assert.That(EnergyCoverageQuery.MinimumEfficiency(world, source.Definition), Is.EqualTo(.4).Within(.00001));
            Assert.That(world.Submit(WorldCommand.Move(source.Id, 20, 20)).Succeeded, Is.True);
            Assert.That(EnergyCoverageQuery.BestSource(world, 2.5, 2.5, 0, out _), Is.Null);
            Assert.That(EnergyCoverageQuery.BestSource(world, 20.5, 20.5, 0, out _), Is.SameAs(source));
            Assert.That(world.Submit(WorldCommand.Sell(source.Id)).Succeeded, Is.True);
            Assert.That(EnergyCoverageQuery.BestSource(world, 20.5, 20.5, 0, out _), Is.Null);
            var relay = Place(world, BuildingKind.Relay, 20, 20);
            Assert.That(world.Submit(WorldCommand.Damage(relay.Id, relay.Health)).Succeeded, Is.True);
            Assert.That(EnergyCoverageQuery.BestSource(world, 20.5, 20.5, 0, out _), Is.Null);
        }

        private static GameWorld CreateWorld(EnergySettings energySettings = null)
            => new GameWorld(new[] { Definition(BuildingKind.Reactor), Definition(BuildingKind.Relay),
                Definition(BuildingKind.Battery) }, energySettings: energySettings);

        private static BuildingDefinition Definition(BuildingKind kind)
            => new BuildingDefinition(kind, kind.ToString(), 1, 1, 10000, 0, 0, 1000, 4, 8, 100, 0, 0, 0);

        private static BuildingState Place(GameWorld world, BuildingKind kind, int x, int z)
        {
            var result = world.Submit(WorldCommand.Place(kind, x, z));
            Assert.That(result.Succeeded, Is.True, result.Message);
            return world.FindBuilding(result.BuildingId);
        }
    }
}
