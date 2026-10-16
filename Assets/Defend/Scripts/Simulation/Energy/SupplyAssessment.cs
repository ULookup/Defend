using System.Collections.Generic;

namespace Defend.Simulation
{
    /// <summary>只读供电诊断。额定能力用于解释供需压力；到货量始终读取实际账目。</summary>
    public sealed class SupplyAssessment
    {
        public bool Connected { get; private set; }
        public bool BatterySupportAvailable { get; private set; }
        public bool GenerationInsufficient { get; private set; }
        public bool RelayCongested { get; private set; }
        public int IncomingConnections { get; private set; }
        public int OutgoingConnections { get; private set; }
        public double GenerationPerSecond { get; private set; }
        public double FullFireDemandPerSecond { get; private set; }
        public string Summary { get; private set; }

        public static SupplyAssessment Read(GameWorld world, BuildingState selected)
        {
            var result = new SupplyAssessment();
            if (selected == null || !selected.IsAlive || selected.IsSold) { result.Summary = "建筑已损毁"; return result; }
            var upstream = new HashSet<long> { selected.Id };
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (var edge in world.Energy.Connections)
                    if (upstream.Contains(edge.TargetId)) changed |= upstream.Add(edge.SourceId);
            }
            var supplied = new HashSet<long>();
            foreach (var building in world.Buildings)
            {
                if (!building.IsAlive || building.IsSold || !upstream.Contains(building.Id)) continue;
                if (building.Kind == BuildingKind.Battery && building.StoredEnergy > 0)
                    result.BatterySupportAvailable = true;
                if (building.Kind == BuildingKind.Reactor)
                {
                    result.Connected = true;
                    supplied.Add(building.Id);
                    result.GenerationPerSecond += building.Definition.ProductionPerTick * world.Settings.TicksPerSecond / 1000.0;
                }
                if (building.Kind == BuildingKind.Relay && world.Energy.HasStalledEnergy(building.Id)) result.RelayCongested = true;
            }
            changed = true;
            while (changed)
            {
                changed = false;
                foreach (var edge in world.Energy.Connections)
                    if (supplied.Contains(edge.SourceId)) changed |= supplied.Add(edge.TargetId);
            }
            foreach (var building in world.Buildings)
                if (world.Battle != null && building.IsTurret && building.IsAlive && !building.IsSold && supplied.Contains(building.Id))
                {
                    var weapon = world.GetTurretDefinition(building.Kind);
                    result.FullFireDemandPerSecond += weapon.EnergyPerShot * (double)world.Settings.TicksPerSecond / weapon.CooldownTicks / 1000;
                }
            foreach (var edge in world.Energy.Connections)
            {
                if (edge.SourceId == selected.Id) result.OutgoingConnections++;
                if (edge.TargetId == selected.Id) result.IncomingConnections++;
            }
            result.GenerationInsufficient = result.Connected && result.GenerationPerSecond < result.FullFireDemandPerSecond;
            result.Summary = !result.Connected ? (result.BatterySupportAvailable ? "电池储能支援：当前没有持续发电" : "线路中断：未连接存活反应堆或有储能的电池") :
                result.RelayCongested ? "中继拥堵：上游能量正在滞留" :
                result.GenerationInsufficient ? "发电不足风险：按满速攻击估算" :
                selected.CombatState == TurretCombatState.WaitingForEnergy ? "等待充能：尚未凑足一次攻击" :
                selected.StoredEnergy >= selected.Definition.EnergyCapacity ? "供电正常：缓存已满" : "供电连接正常";
            return result;
        }
    }
}
