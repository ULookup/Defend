using System;
using System.Collections.Generic;

namespace Defend.Simulation
{
    public sealed partial class GameWorld
    {
        private readonly Dictionary<BuildingKind, BuildingDefinition> originalDefinitions;
        private readonly Dictionary<BuildingKind, TurretDefinition> turretDefinitions = new Dictionary<BuildingKind, TurretDefinition>();
        private readonly EnergySettings originalEnergySettings;
        private readonly BattleSettings originalBattleSettings;
        private SandboxCheckpoint sandboxCheckpoint;
        public bool IsSandbox { get; }
        public bool HasUnlimitedFunds => IsSandbox;
        public long Revision { get; private set; }
        public SandboxConfigurationService Sandbox { get; }
        public bool HasSandboxCheckpoint => sandboxCheckpoint != null;
        public BuildingDefinition GetDefinition(BuildingKind kind) => definitions[kind];
        public TurretDefinition GetTurretDefinition(BuildingKind kind) => turretDefinitions[kind];
        internal BuildingDefinition GetDefaultDefinition(BuildingKind kind) => originalDefinitions[kind];
        internal TurretDefinition GetDefaultTurretDefinition(BuildingKind kind) => originalBattleSettings.GetTurret(kind);
        internal void MarkSandboxChanged() { Revision++; }
        internal void ClearSandboxBattleEvents() { events.Clear(); }
        internal void ReplaceSandboxDefinition(BuildingDefinition building, TurretDefinition weapon)
        {
            definitions[building.Kind] = building;
            if (weapon != null)
            {
                var previous = turretDefinitions[building.Kind];
                turretDefinitions[building.Kind] = weapon;
                Battle.OnTurretDefinitionChanged(building.Kind, previous, weapon);
            }
            Revision++;
        }

        private void CaptureSandboxCheckpoint()
        {
            sandboxCheckpoint = new SandboxCheckpoint(this);
        }

        public GameWorld RestoreSandboxCheckpoint(out string error)
        {
            error = null;
            if (!IsSandbox || sandboxCheckpoint == null) { error = "尚无可恢复的开战前状态。"; return null; }
            if (Battle.State == BattleState.Combat) { error = "请先提前结束本波，再恢复开战前状态。"; return null; }
            return sandboxCheckpoint.Restore(this);
        }

        public GameWorld CreateEmptySandboxWorld()
        {
            return new GameWorld(originalDefinitions.Values, Settings, originalEnergySettings, originalBattleSettings, true);
        }

        // 显式保存模拟状态；不序列化 Unity 对象，不以重建空网络替代真实恢复。
        private sealed class SandboxCheckpoint
        {
            private readonly Dictionary<BuildingKind, BuildingDefinition> definitions;
            private readonly Dictionary<BuildingKind, TurretDefinition> weapons;
            private readonly List<BuildingState> buildings = new List<BuildingState>();
            private readonly EnergyCheckpoint energy;
            private readonly BattleCheckpoint battle;
            private readonly long tick, buildingId, commandSequence, eventId, income, revision, gridRevision;
            private readonly int credits, destroyed;
            public SandboxCheckpoint(GameWorld source)
            {
                definitions = new Dictionary<BuildingKind, BuildingDefinition>(source.definitions);
                weapons = new Dictionary<BuildingKind, TurretDefinition>(source.turretDefinitions);
                foreach (var building in source.buildings) buildings.Add(building.CloneForCheckpoint());
                energy = source.Energy.CaptureCheckpoint(); battle = source.Battle.CaptureCheckpoint();
                tick = source.CurrentTick; buildingId = source.nextBuildingId; commandSequence = source.nextCommandSequence;
                eventId = source.nextEventId; income = source.TotalIncome; revision = source.Revision;
                credits = source.Credits; destroyed = source.DestroyedBuildingCount;
                gridRevision = source.grid.Revision;
            }
            public GameWorld Restore(GameWorld source)
            {
                var target = source.CreateEmptySandboxWorld();
                target.definitions.Clear(); foreach (var pair in definitions) target.definitions.Add(pair.Key, pair.Value);
                target.turretDefinitions.Clear(); foreach (var pair in weapons) target.turretDefinitions.Add(pair.Key, pair.Value);
                foreach (var saved in buildings)
                {
                    var copy = saved.CloneForCheckpoint(); target.buildings.Add(copy); target.buildingsById.Add(copy.Id, copy);
                    if (copy.IsAlive && !copy.IsSold) target.grid.Occupy(copy);
                }
                target.CurrentTick = tick; target.nextBuildingId = buildingId; target.nextCommandSequence = commandSequence;
                target.nextEventId = eventId; target.TotalIncome = income; target.Credits = credits;
                target.DestroyedBuildingCount = destroyed; target.Revision = revision;
                target.grid.RestoreRevision(gridRevision);
                target.Energy.RestoreCheckpoint(energy); target.Battle.RestoreCheckpoint(battle);
                target.sandboxCheckpoint = this;
                return target;
            }
        }
    }
}
