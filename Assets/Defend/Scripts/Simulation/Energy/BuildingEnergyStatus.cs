using System;
using System.Collections.Generic;

namespace Defend.Simulation
{
    /// <summary>能源来源状态，与建筑实际储量分开；任何状态都不限制射击或写入模拟。</summary>
    public enum BuildingEnergyStatus
    {
        NotRequired,
        Connected,
        Disconnected,
        WaitingForEnergy,
        BatterySupport,
        SourceDepleted
    }

    /// <summary>
    /// 按整份显示快照读取来源可达性。拓扑只在建筑变化时重建，每次采集共用队列遍历，
    /// 不参与运输规划，也不承诺到货时间或绕过电池批次的访问历史。
    /// </summary>
    public sealed class BuildingEnergyStatusResolver
    {
        [Flags]
        private enum SourcePaths { None = 0, Reactor = 1, Battery = 2, ActiveReactor = 4, ChargedBattery = 8 }

        private sealed class Node
        {
            public BuildingState Building;
            public BuildingDefinition Definition;
            public int X, Z, Orientation;
            public bool Alive;
            public SourcePaths Paths;
            public BuildingEnergyStatus Status;
            public readonly List<Node> Targets = new List<Node>();
        }

        private readonly Dictionary<long, Node> nodes = new Dictionary<long, Node>();
        private readonly List<Node> orderedNodes = new List<Node>();
        private readonly Queue<Node> pending = new Queue<Node>();
        private GameWorld capturedWorld;

        /// <summary>返回上次 Capture 的值；不会重新读取正在向前运行的模拟状态。</summary>
        public BuildingEnergyStatus GetStatus(long buildingId)
            => nodes.TryGetValue(buildingId, out Node node) ? node.Status : BuildingEnergyStatus.NotRequired;

        public void Clear()
        {
            nodes.Clear(); orderedNodes.Clear(); pending.Clear(); capturedWorld = null;
        }

        public void Capture(GameWorld world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (!ReferenceEquals(capturedWorld, world)) { Clear(); capturedWorld = world; }
            bool topologyChanged = nodes.Count != world.Buildings.Count;
            foreach (BuildingState building in world.Buildings)
            {
                if (!nodes.TryGetValue(building.Id, out Node node))
                {
                    node = new Node(); nodes.Add(building.Id, node); orderedNodes.Add(node);
                    topologyChanged = true;
                }
                bool alive = building.IsAlive && !building.IsSold;
                topologyChanged |= !ReferenceEquals(node.Building, building) ||
                    node.Definition != building.Definition || node.X != building.X || node.Z != building.Z ||
                    node.Orientation != building.OrientationQuarterTurns || node.Alive != alive;
                node.Building = building; node.Definition = building.Definition;
                node.X = building.X; node.Z = building.Z; node.Orientation = building.OrientationQuarterTurns;
                node.Alive = alive; node.Paths = SourcePaths.None;
            }
            if (topologyChanged) RebuildTopology(world);
            Propagate(SourcePaths.Reactor);
            Propagate(SourcePaths.Battery);
            Propagate(SourcePaths.ActiveReactor);
            Propagate(SourcePaths.ChargedBattery);
            foreach (Node node in orderedNodes) node.Status = Resolve(world, node);
        }

        private void RebuildTopology(GameWorld world)
        {
            // 暂停中的正式命令不推进 Energy.Step，Connections 可能仍是上一逻辑步的图。
            // 复用模拟的方向、端口资格和效率计算，确保移动、旋转、出售可立即正确采集。
            foreach (Node source in orderedNodes)
            {
                source.Targets.Clear();
                if (!source.Building.CanOutput || source.Definition.OutputAmount <= 0) continue;
                foreach (Node target in orderedNodes)
                    if (target != source && target.Building.CanReceive &&
                        world.Energy.DirectEfficiencyAt(source.Building, target.Building.CenterX, target.Building.CenterZ) > 0)
                        source.Targets.Add(target);
            }
        }

        private void Propagate(SourcePaths path)
        {
            pending.Clear();
            foreach (Node node in orderedNodes)
            {
                if (!node.Alive) continue;
                BuildingState building = node.Building;
                bool source = path == SourcePaths.Reactor ? building.Kind == BuildingKind.Reactor :
                    path == SourcePaths.Battery ? building.Kind == BuildingKind.Battery :
                    path == SourcePaths.ActiveReactor ? building.Kind == BuildingKind.Reactor &&
                        (building.Definition.ProductionPerTick > 0 || building.StoredEnergy > 0) :
                    building.Kind == BuildingKind.Battery && building.StoredEnergy > 0;
                if (!source) continue;
                node.Paths |= path; pending.Enqueue(node);
            }
            while (pending.Count > 0)
            {
                Node source = pending.Dequeue();
                foreach (Node target in source.Targets)
                {
                    if ((target.Paths & path) != 0) continue;
                    target.Paths |= path; pending.Enqueue(target);
                }
            }
        }

        private static BuildingEnergyStatus Resolve(GameWorld world, Node node)
        {
            BuildingState building = node.Building;
            if (!node.Alive || (building.Kind == BuildingKind.ResonanceMagneticTower && !building.Definition.Resonance.RequiresEnergy))
                return BuildingEnergyStatus.NotRequired;
            if ((node.Paths & SourcePaths.ActiveReactor) != 0)
            {
                // 射击先于本步到货；CombatState 可能刚因缺能失败，随后已经收到足够能量。
                // 两个真实条件同时成立才显示等待，避免把本步恢复的库存显示为仍然缺能。
                bool waiting = building.CombatState == TurretCombatState.WaitingForEnergy && building.IsTurret &&
                    world.Battle != null && building.StoredEnergy < world.GetTurretDefinition(building.Kind).EnergyPerShot;
                return waiting ? BuildingEnergyStatus.WaitingForEnergy : BuildingEnergyStatus.Connected;
            }
            if ((node.Paths & SourcePaths.ChargedBattery) != 0) return BuildingEnergyStatus.BatterySupport;
            // 来源耗尽并不清空本地余量，也不否认已有的在途批次；它只说明没有可持续的新来源。
            if ((node.Paths & (SourcePaths.Reactor | SourcePaths.Battery)) != 0) return BuildingEnergyStatus.SourceDepleted;
            return BuildingEnergyStatus.Disconnected;
        }
    }
}
