using System;
using System.Collections.Generic;
namespace Defend.Simulation
{
    internal sealed class EnemyRoute
    {
        internal readonly List<SpawnPoint> Cells = new List<SpawnPoint>();
        internal double Cost, EndX, EndZ;
    }
    /// <summary>一次从敌人位置搜索全图，边代价同时包含时间、占位等待、拆除与火力风险。</summary>
    internal sealed class EnemyStrategicNavigation
    {
        private readonly GameWorld world;
        private readonly BuildingGrid grid;
        private readonly BattleSystem battle;
        private readonly EnemyBehaviorSystem observation;
        private readonly double[] costs, danger, congestion;
        private readonly int[] parents;
        private readonly SortedSet<Entry> open = new SortedSet<Entry>();
        private static readonly int[] OffsetX = { 0, 1, 0, -1 };
        private static readonly int[] OffsetZ = { 1, 0, -1, 0 };
        private long dangerTick = -1;
        internal EnemyStrategicNavigation(GameWorld world, BuildingGrid grid, BattleSystem battle, EnemyBehaviorSystem observation)
        {
            this.world = world; this.grid = grid; this.battle = battle; this.observation = observation;
            int size = grid.Width * grid.Depth;
            costs = new double[size]; danger = new double[size]; congestion = new double[size]; parents = new int[size];
        }
        internal void Search(EnemyState enemy, long tick)
        {
            open.Clear();
            for (int i = 0; i < costs.Length; i++) { costs[i] = double.PositiveInfinity; parents[i] = -1; congestion[i] = 0; }
            if (dangerTick != tick)
            {
                Array.Clear(danger, 0, danger.Length); dangerTick = tick;
                foreach (var turret in world.Buildings)
                {
                    if (!turret.IsAlive || turret.IsSold || !turret.IsTurret) continue;
                    double range = battle.Settings.GetTurret(turret.Kind).Range, rate = observation.Threat(turret, tick);
                    for (int z = Math.Max(0, (int)(turret.CenterZ - range)); z < Math.Min(grid.Depth, turret.CenterZ + range + 1); z++)
                    for (int x = Math.Max(0, (int)(turret.CenterX - range)); x < Math.Min(grid.Width, turret.CenterX + range + 1); x++)
                    {
                        double dx = x + .5 - turret.CenterX, dz = z + .5 - turret.CenterZ;
                        if (dx * dx + dz * dz <= range * range) danger[z * grid.Width + x] += rate;
                    }
                }
            }
            foreach (var other in battle.Enemies)
            {
                if (!other.IsAlive || other.Id == enemy.Id) continue;
                int cell = (int)other.Z * grid.Width + (int)other.X;
                congestion[cell] += Math.Max(.3, (enemy.Radius + other.Radius) / other.Definition.Speed);
                int reservation = (int)other.AttackPositionZ * grid.Width + (int)other.AttackPositionX;
                if (other.TargetBuildingId != 0 && reservation >= 0 && reservation < congestion.Length) congestion[reservation] += 2;
            }
            int start = (int)enemy.Z * grid.Width + (int)enemy.X;
            costs[start] = 0; open.Add(new Entry(start, 0));
            while (open.Count > 0)
            {
                var entry = open.Min; open.Remove(entry);
                if (entry.Cost > costs[entry.Cell]) continue;
                int x = entry.Cell % grid.Width, z = entry.Cell / grid.Width;
                for (int direction = 0; direction < 4; direction++)
                {
                    int nx = x + OffsetX[direction], nz = z + OffsetZ[direction];
                    if (nx < 0 || nz < 0 || nx >= grid.Width || nz >= grid.Depth || grid.Find(nx, nz) < 0) continue;
                    int next = nz * grid.Width + nx;
                    // 允许规划拆除路线，但从不允许实体穿过建筑；真正移动由体积约束执行。
                    long buildingId = grid.Find(nx, nz);
                    double demolition = 0;
                    if (buildingId > 0 && !PassedBuilding(entry.Cell, buildingId))
                    {
                        var building = world.FindBuilding(buildingId);
                        demolition = Demolition(enemy, building);
                    }
                    double duration = 1 / enemy.Definition.Speed + congestion[next] + demolition;
                    double cost = entry.Cost + duration * (1 + danger[next] * enemy.Definition.RiskWeight / enemy.MaximumHealth);
                    if (cost >= costs[next]) continue;
                    costs[next] = cost; parents[next] = entry.Cell; open.Add(new Entry(next, cost));
                }
            }
        }
        private bool PassedBuilding(int cell, long id)
        {
            // 大建筑跨多个格子仍只付一次拆除费用；按稳定建筑身份检查当前路径。
            for (int current = cell; current >= 0; current = parents[current])
                if (grid.Find(current % grid.Width, current / grid.Width) == id) return true;
            return false;
        }
        private double Demolition(EnemyState enemy, BuildingState building)
        {
            double durability = building.Health;
            if (building.Kind == BuildingKind.Core) durability += building.StoredEnergy / 1000.0;
            return Math.Ceiling(durability / enemy.Definition.AttackDamage) * enemy.Definition.AttackIntervalTicks / world.Settings.TicksPerSecond;
        }
        internal EnemyRoute RouteTo(EnemyState enemy, BuildingState target, long tick)
        {
            int chosen = -1; double best = double.PositiveInfinity;
            for (int z = Math.Max(0, target.Z - 1); z <= Math.Min(grid.Depth - 1, target.Z + target.Definition.Depth); z++)
            for (int x = Math.Max(0, target.X - 1); x <= Math.Min(grid.Width - 1, target.X + target.Definition.Width); x++)
            {
                long occupant = grid.Find(x, z);
                if (occupant < 0 || occupant == target.Id) continue;
                double dx = x + .5 - Math.Max(target.X, Math.Min(x + .5, target.X + target.Definition.Width));
                double dz = z + .5 - Math.Max(target.Z, Math.Min(z + .5, target.Z + target.Definition.Depth));
                if (dx * dx + dz * dz > enemy.Definition.AttackRange * enemy.Definition.AttackRange) continue;
                int cell = z * grid.Width + x;
                // 不能先穿过最终目标占地再抵达其背面攻击；目标的拆除只在终点计费。
                if (PassedBuilding(cell, target.Id)) continue;
                double standing = Demolition(enemy, target) + congestion[cell];
                double cost = costs[cell] + standing * (1 + danger[cell] * enemy.Definition.RiskWeight / enemy.MaximumHealth);
                if (cost < best) { best = cost; chosen = cell; }
            }
            if (chosen < 0) return null;
            var route = new EnemyRoute { Cost = best, EndX = chosen % grid.Width + .5, EndZ = chosen / grid.Width + .5 };
            for (int cell = chosen; cell >= 0; cell = parents[cell]) route.Cells.Add(new SpawnPoint(cell % grid.Width, cell / grid.Width));
            route.Cells.Reverse();
            return route;
        }
        internal double RemainingCost(EnemyState enemy, BuildingState target)
        {
            if (enemy.Route == null) return double.PositiveInfinity;
            var removed = new HashSet<long>();
            double total = 0;
            for (int index = enemy.RouteCursor; index < enemy.Route.Cells.Count; index++)
            {
                var point = enemy.Route.Cells[index]; int cell = point.Z * grid.Width + point.X;
                long buildingId = grid.Find(point.X, point.Z);
                if (buildingId < 0) return double.PositiveInfinity;
                double duration = 1 / enemy.Definition.Speed + congestion[cell];
                if (buildingId > 0 && removed.Add(buildingId)) duration += Demolition(enemy, world.FindBuilding(buildingId));
                total += duration * (1 + danger[cell] * enemy.Definition.RiskWeight / enemy.MaximumHealth);
            }
            int end = (int)enemy.Route.EndZ * grid.Width + (int)enemy.Route.EndX;
            return total + (Demolition(enemy, target) + congestion[end]) * (1 + danger[end] * enemy.Definition.RiskWeight / enemy.MaximumHealth);
        }
        private readonly struct Entry : IComparable<Entry>
        {
            internal readonly int Cell; internal readonly double Cost;
            internal Entry(int cell, double cost) { Cell = cell; Cost = cost; }
            public int CompareTo(Entry other) { int result = Cost.CompareTo(other.Cost); return result == 0 ? Cell.CompareTo(other.Cell) : result; }
        }
    }
}
