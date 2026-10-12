using System;
namespace Defend.Simulation
{
    /// <summary>唯一的敌人位置写入点：圆形占位、连续扫掠和稳定让行共同约束移动与出生。</summary>
    internal sealed class EnemyMovementSystem
    {
        private readonly GameWorld world;
        private readonly BuildingGrid grid;
        private readonly BattleSystem battle;
        internal EnemyMovementSystem(GameWorld world, BuildingGrid grid, BattleSystem battle)
        { this.world = world; this.grid = grid; this.battle = battle; }
        internal bool TrySpawn(SpawnPoint entrance, EnemyDefinition definition, out double x, out double z)
        {
            // 入口附近按固定顺序寻找真实空位；失败只保留队列，不生成重叠实例。
            for (int ring = 0; ring <= 1; ring++)
            for (int dz = -ring; dz <= ring; dz++)
            for (int dx = -ring; dx <= ring; dx++)
            {
                if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != ring) continue;
                x = entrance.X + dx + .5; z = entrance.Z + dz + .5;
                if (CanStand(null, x, z, definition.Radius)) return true;
            }
            x = z = 0; return false;
        }
        internal void Step(EnemyState enemy, long tick)
        {
            enemy.CurrentAttackBuildingId = 0;
            var final = world.FindBuilding(enemy.TargetBuildingId);
            if (final == null || !final.IsAlive || final.IsSold) { enemy.CurrentAction = EnemyAction.WaitingForPassage; return; }
            var route = enemy.Route;
            if (route == null) { Wait(enemy, tick); return; }
            while (enemy.RouteCursor < route.Cells.Count)
            {
                var cell = route.Cells[enemy.RouteCursor];
                double dx = cell.X + .5 - enemy.X, dz = cell.Z + .5 - enemy.Z;
                if (dx * dx + dz * dz > .015 * .015) break;
                enemy.RouteCursor++;
            }
            // 只跳过无建筑且整条扫掠可行的中间航点，不为追逐格中心倒走。
            // 同类仍由后续连续碰撞约束处理；不能跳过尚待拆除的障碍节点。
            if (tick >= enemy.YieldUntilTick)
            {
                for (int lookAhead = 0; lookAhead < 2 && enemy.RouteCursor + 1 < route.Cells.Count; lookAhead++)
                {
                    var current = route.Cells[enemy.RouteCursor]; var following = route.Cells[enemy.RouteCursor + 1];
                    if (grid.Find(current.X, current.Z) != 0 || grid.Find(following.X, following.Z) != 0 ||
                        !TerrainSegmentFree(enemy.X, enemy.Z, following.X + .5, following.Z + .5, enemy.Radius)) break;
                    enemy.RouteCursor++;
                }
            }
            BuildingState attack = null;
            if (BattleSystem.InAttackRange(enemy, final)) attack = final;
            else if (enemy.RouteCursor < route.Cells.Count)
            {
                var cell = route.Cells[enemy.RouteCursor];
                long obstacle = grid.Find(cell.X, cell.Z);
                if (obstacle > 0) attack = world.FindBuilding(obstacle);
            }
            if (attack != null && attack.IsAlive && BattleSystem.InAttackRange(enemy, attack))
            {
                // 当前拆障对象独立于最终目标；仅真实站到近战距离的前排能够造成伤害。
                enemy.CurrentAttackBuildingId = attack.Id; enemy.CurrentAction = EnemyAction.Attacking;
                enemy.BlockedSinceTick = -1;
                if (tick >= enemy.NextAttackTick)
                {
                    world.ApplyBattleDamage(attack, enemy.Definition.AttackDamage);
                    enemy.LastAttackTick = tick; enemy.LastAttackTargetBuildingId = attack.Id;
                    enemy.LastAttackTargetX = attack.CenterX; enemy.LastAttackTargetZ = attack.CenterZ;
                    enemy.NextAttackTick = tick + enemy.Definition.AttackIntervalTicks;
                }
                return;
            }
            if (enemy.RouteCursor >= route.Cells.Count) { Wait(enemy, tick); return; }
            var next = route.Cells[enemy.RouteCursor];
            double goalX = next.X + .5, goalZ = next.Z + .5;
            bool yielding = tick < enemy.YieldUntilTick;
            if (yielding)
            {
                EnemyState priority = null;
                foreach (var other in battle.Enemies) if (other.Id == enemy.YieldForEnemyId && other.IsAlive) { priority = other; break; }
                double px = priority == null ? 10 : enemy.X - priority.X, pz = priority == null ? 10 : enemy.Z - priority.Z;
                if (px * px + pz * pz > 2.25)
                { enemy.YieldUntilTick = 0; enemy.NextEvaluationTick = 0; yielding = false; }
                else
                {
                    goalX = enemy.YieldX; goalZ = enemy.YieldZ;
                    double gx = goalX - enemy.X, gz = goalZ - enemy.Z;
                    // 到让行点后短暂保持，不立即返回原路线把刚腾出的通道堵住。
                    if (gx * gx + gz * gz < .015 * .015) { Wait(enemy, tick); return; }
                }
            }
            double remaining = enemy.Definition.Speed / world.Settings.TicksPerSecond;
            bool moved = false, detoured = yielding;
            while (remaining > .000001)
            {
                double dx = goalX - enemy.X, dz = goalZ - enemy.Z, distance = Math.Sqrt(dx * dx + dz * dz);
                if (distance < .001) break;
                double step = Math.Min(Math.Min(.08, remaining), distance);
                dx /= distance; dz /= distance;
                if (TryMove(enemy, dx * step, dz * step, out var blocker)) { moved = true; remaining -= step; continue; }
                if (blocker != null) RequestYield(enemy, blocker, dx, dz, tick);
                bool escaped = false;
                // 固定右侧优先，随后左侧；短暂拥堵先避让，持续拥堵交给路线重评。
                foreach (double angle in AvoidanceAngles)
                {
                    double cosine = Math.Cos(angle), sine = Math.Sin(angle);
                    double vx = dx * cosine - dz * sine, vz = dx * sine + dz * cosine;
                    if (!TryMove(enemy, vx * step, vz * step, out _)) continue;
                    moved = escaped = detoured = true; remaining -= step; break;
                }
                if (!escaped) break;
            }
            enemy.CellX = (int)enemy.X; enemy.CellZ = (int)enemy.Z;
            enemy.NextCellX = next.X; enemy.NextCellZ = next.Z;
            if (moved)
            {
                enemy.CurrentAction = detoured ? EnemyAction.Detouring : EnemyAction.Advancing;
                if (!detoured) enemy.BlockedSinceTick = -1;
                else if (enemy.BlockedSinceTick < 0) enemy.BlockedSinceTick = tick;
            }
            else Wait(enemy, tick);
        }
        private static readonly double[] AvoidanceAngles = { -.6, .6, -1.05, 1.05, -1.57, 1.57 };
        private void RequestYield(EnemyState enemy, EnemyState blocker, double dx, double dz, long tick)
        {
            // 双方都能发起协商，始终由稳定身份较大者退让；不能依赖低身份者恰好先撞上对方。
            var yielding = enemy.Id > blocker.Id ? enemy : blocker;
            var priority = enemy.Id > blocker.Id ? blocker : enemy;
            if (yielding.CurrentAction == EnemyAction.Attacking || tick < yielding.YieldUntilTick) return;
            double bx = blocker.AttackPositionX - blocker.X, bz = blocker.AttackPositionZ - blocker.Z;
            bool prolonged = enemy.BlockedSinceTick >= 0 && tick - enemy.BlockedSinceTick >= world.Settings.TicksPerSecond;
            if (!prolonged && bx * dx + bz * dz >= 0) return;
            double awayX = yielding.X - priority.X, awayZ = yielding.Z - priority.Z;
            double length = Math.Sqrt(awayX * awayX + awayZ * awayZ);
            if (length < .0001) return;
            awayX /= length; awayZ /= length;
            // 侧退、斜退和后退都要整段可走；仅检查终点会反复选择隔墙空位。
            foreach (double distance in YieldDistances)
            foreach (double angle in YieldAngles)
            {
                double cosine = Math.Cos(angle), sine = Math.Sin(angle);
                double x = yielding.X + (awayX * cosine - awayZ * sine) * distance;
                double z = yielding.Z + (awayX * sine + awayZ * cosine) * distance;
                if (!CanStand(yielding, x, z, yielding.Radius) ||
                    !CanMove(yielding, x - yielding.X, z - yielding.Z, out _)) continue;
                yielding.YieldX = x; yielding.YieldZ = z;
                yielding.YieldForEnemyId = priority.Id;
                yielding.YieldUntilTick = tick + world.Settings.TicksPerSecond * 3;
                return;
            }
        }
        private static readonly double[] YieldDistances = { .8, 1.2, 1.6 };
        private static readonly double[] YieldAngles = { -1.2, 1.2, -.6, .6, 0 };
        private static void Wait(EnemyState enemy, long tick)
        { enemy.CurrentAction = EnemyAction.WaitingForPassage; if (enemy.BlockedSinceTick < 0) enemy.BlockedSinceTick = tick; }
        private bool TryMove(EnemyState enemy, double dx, double dz, out EnemyState blocker)
        {
            if (!CanMove(enemy, dx, dz, out blocker)) return false;
            enemy.X += dx; enemy.Z += dz; return true;
        }
        private bool CanMove(EnemyState enemy, double dx, double dz, out EnemyState blocker)
        {
            blocker = null;
            double x = enemy.X + dx, z = enemy.Z + dz;
            if (!TerrainSegmentFree(enemy.X, enemy.Z, x, z, enemy.Radius)) return false;
            foreach (var other in battle.Enemies)
            {
                if (!other.IsAlive || other.Id == enemy.Id) continue;
                double denominator = dx * dx + dz * dz;
                double fraction = denominator > 0 ? Math.Max(0, Math.Min(1, ((other.X - enemy.X) * dx + (other.Z - enemy.Z) * dz) / denominator)) : 0;
                double ox = other.X - enemy.X - dx * fraction, oz = other.Z - enemy.Z - dz * fraction;
                double radius = enemy.Radius + other.Radius;
                if (ox * ox + oz * oz < radius * radius - .0000001) { blocker = other; return false; }
            }
            return true;
        }
        private bool TerrainSegmentFree(double fromX, double fromZ, double toX, double toZ, double radius)
        {
            double dx = toX - fromX, dz = toZ - fromZ;
            int steps = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(dx * dx + dz * dz) / .08));
            for (int i = 1; i <= steps; i++)
                if (!TerrainFree(fromX + dx * i / steps, fromZ + dz * i / steps, radius)) return false;
            return dx * dx + dz * dz < .00000001 || SweepCorners(fromX, fromZ, toX, toZ, radius);
        }
        private bool SweepCorners(double fromX, double fromZ, double toX, double toZ, double radius)
        {
            // 小步长阻止跨越整格；角点到运动线段的距离再阻止擦角时从圆角之间漏过。
            double dx = toX - fromX, dz = toZ - fromZ, lengthSquared = dx * dx + dz * dz;
            for (int z = (int)Math.Floor(Math.Min(fromZ, toZ) - radius); z <= (int)Math.Floor(Math.Max(fromZ, toZ) + radius); z++)
            for (int x = (int)Math.Floor(Math.Min(fromX, toX) - radius); x <= (int)Math.Floor(Math.Max(fromX, toX) + radius); x++)
            {
                if (grid.Find(x, z) == 0) continue;
                for (int cornerZ = z; cornerZ <= z + 1; cornerZ++)
                for (int cornerX = x; cornerX <= x + 1; cornerX++)
                {
                    double fraction = Math.Max(0, Math.Min(1, ((cornerX - fromX) * dx + (cornerZ - fromZ) * dz) / lengthSquared));
                    double ox = cornerX - fromX - dx * fraction, oz = cornerZ - fromZ - dz * fraction;
                    if (ox * ox + oz * oz < radius * radius - .0000001) return false;
                }
            }
            return true;
        }
        private bool CanStand(EnemyState ignored, double x, double z, double radius)
        {
            if (!TerrainFree(x, z, radius)) return false;
            foreach (var other in battle.Enemies)
            {
                if (!other.IsAlive || other == ignored) continue;
                double dx = x - other.X, dz = z - other.Z, combined = radius + other.Radius;
                if (dx * dx + dz * dz < combined * combined) return false;
            }
            return true;
        }
        private bool TerrainFree(double x, double z, double radius)
        {
            if (x < radius || z < radius || x > grid.Width - radius || z > grid.Depth - radius) return false;
            for (int cellZ = (int)Math.Floor(z - radius); cellZ <= (int)Math.Floor(z + radius); cellZ++)
            for (int cellX = (int)Math.Floor(x - radius); cellX <= (int)Math.Floor(x + radius); cellX++)
            {
                if (grid.Find(cellX, cellZ) == 0) continue;
                double dx = x - Math.Max(cellX, Math.Min(x, cellX + 1));
                double dz = z - Math.Max(cellZ, Math.Min(z, cellZ + 1));
                if (dx * dx + dz * dz < radius * radius - .0000001) return false;
            }
            return true;
        }
    }
}
