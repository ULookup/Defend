using System;
using System.Collections.Generic;
namespace Defend.Simulation
{
    /// <summary>全图共享实际战场活动；每个敌人仅保留自己的目标与路线，不复制观察历史。</summary>
    internal sealed class EnemyBehaviorSystem
    {
        private readonly GameWorld world;
        private readonly BattleSystem battle;
        private readonly BuildingGrid grid;
        private readonly EnemyStrategicNavigation navigation;
        private readonly EnemyBehaviorSettings settings;
        private readonly Dictionary<long, EnemyObservedActivity> damage = new Dictionary<long, EnemyObservedActivity>();
        private readonly Dictionary<long, EnemyObservedActivity> delivered = new Dictionary<long, EnemyObservedActivity>();
        private readonly Dictionary<(long source, long target), EnemyObservedActivity> support = new Dictionary<(long, long), EnemyObservedActivity>();
        private readonly int memoryTicks, evaluationTicks, lockTicks;
        private long cursor;
        internal EnemyBehaviorStatistics Statistics { get; } = new EnemyBehaviorStatistics();
        internal EnemyBehaviorSystem(GameWorld world, BuildingGrid grid, BattleSystem battle, EnemyNavigation unused)
        {
            this.world = world; this.grid = grid; this.battle = battle; settings = battle.Settings.EnemyBehavior;
            memoryTicks = Math.Max(1, (int)(settings.ObservationMemorySeconds * world.Settings.TicksPerSecond));
            evaluationTicks = Math.Max(1, (int)(settings.EvaluationIntervalSeconds * world.Settings.TicksPerSecond));
            lockTicks = Math.Max(0, (int)(settings.MinimumTargetLockSeconds * world.Settings.TicksPerSecond));
            navigation = new EnemyStrategicNavigation(world, grid, battle, this);
            world.Energy.TerminalDeliveryCompleted = ObserveDelivery;
        }
        internal void BeginStep(long tick) => Statistics.BeginStep();
        internal void ObserveDamage(BuildingState turret, double x, double z, int amount, long tick)
        {
            Add(damage, turret.Id, amount, tick);
            Statistics.DamageEventsObservedThisTick++; Statistics.TotalDamageEventsObserved++;
        }
        private void Add(Dictionary<long, EnemyObservedActivity> records, long id, double amount, long tick)
        {
            if (!records.TryGetValue(id, out var activity)) records[id] = activity = new EnemyObservedActivity();
            activity.Add(amount, tick, memoryTicks);
        }
        private void ObserveDelivery(BuildingState terminal, long amount, IReadOnlyList<long> history, long tick)
        {
            Add(delivered, terminal.Id, amount, tick);
            // 每份实际到货对同一建筑只记一次贡献。多级中继各有切断该路线的价值，
            // 但收益按终端实际总到货归一化，不把各跳流量相加成凭空增加的供给。
            var visited = new HashSet<long>();
            foreach (long source in history)
            {
                if (!visited.Add(source)) continue;
                var key = (source, terminal.Id);
                if (!support.TryGetValue(key, out var activity)) support[key] = activity = new EnemyObservedActivity();
                activity.Add(amount, tick, memoryTicks);
            }
            Statistics.DeliveriesObservedThisTick++; Statistics.TotalDeliveriesObserved++;
        }
        private double Damage(long id, long tick) => damage.TryGetValue(id, out var value) ? value.Value(tick, memoryTicks) / settings.ObservationMemorySeconds : 0;
        internal double Threat(BuildingState turret, long tick)
        {
            var weapon = battle.Settings.GetTurret(turret.Kind);
            double energy = Math.Min(1, (double)turret.StoredEnergy / weapon.EnergyPerShot);
            double remainingCooldown = Math.Max(weapon.CooldownTicks, turret.NextShotTick - tick);
            double potential = weapon.Damage * world.Settings.TicksPerSecond / remainingCooldown * energy;
            return Math.Max(potential, Damage(turret.Id, tick));
        }
        internal double Benefit(EnemyState enemy, BuildingState target, long tick)
        {
            double value = settings.BaseDestructionValue + (target.Kind == BuildingKind.Core ? settings.CoreValue : 0) + settings.FirepowerValue * Math.Sqrt(Damage(target.Id, tick));
            if (target.CanOutput)
                foreach (var terminal in world.Buildings)
                {
                    if (!terminal.IsAlive || terminal.IsSold || !terminal.IsConsumer || !delivered.TryGetValue(terminal.Id, out var total)) continue;
                    if (!support.TryGetValue((target.Id, terminal.Id), out var part)) continue;
                    double all = total.Value(tick, memoryTicks), share = part.Value(tick, memoryTicks);
                    if (all <= 0) continue;
                    value += Math.Min(1, share / all) * (settings.SupplyValue + settings.FirepowerValue * Math.Sqrt(Damage(terminal.Id, tick)) + (terminal.Kind == BuildingKind.Core ? settings.CoreValue * .4 : 0));
                }
            double preference = target.Kind == BuildingKind.Relay ? enemy.Definition.RelayPreference : target.Kind == BuildingKind.Reactor ? enemy.Definition.ReactorPreference : 1;
            return value * preference;
        }
        internal void Evaluate(BuildingState core, long tick)
        {
            int count = battle.Enemies.Count, start = 0;
            while (start < count && battle.Enemies[start].Id <= cursor) start++;
            if (start == count) start = 0;
            for (int i = 0; i < count; i++)
            {
                var enemy = battle.Enemies[(start + i) % count];
                if (!enemy.IsAlive) continue;
                var previous = world.FindBuilding(enemy.TargetBuildingId);
                bool invalid = previous == null || !previous.IsAlive || previous.IsSold;
                if (!invalid && tick < enemy.YieldUntilTick) continue;
                if (!invalid && tick < enemy.NextEvaluationTick && enemy.RouteRevision == grid.Revision) continue;
                // 无效目标立即失效。其他敌人公平重评，延期期间仍沿原路线行动。
                if (!invalid && Statistics.EvaluationsThisTick >= settings.MaximumEvaluationsPerTick) { Statistics.Deferred(); continue; }
                cursor = enemy.Id;
                Statistics.EvaluationsThisTick++; Statistics.TotalEvaluations++; Statistics.PathBuildsThisTick++; Statistics.TotalPathBuilds++;
                enemy.LastBehaviorEvaluationTick = tick; enemy.NextEvaluationTick = tick + evaluationTicks;
                bool locked = !invalid && tick - enemy.TargetAssignedTick < lockTicks;
                navigation.Search(enemy, tick);
                BuildingState best = null; EnemyRoute bestRoute = null; double bestScore = -1;
                EnemyRoute oldRoute = null; double oldScore = -1;
                foreach (var target in world.Buildings)
                {
                    if (!target.IsAlive || target.IsSold || enemy.Kind == EnemyKind.Heavy && target.Id != core.Id) continue;
                    if (locked && target.Id != previous.Id) continue;
                    var route = navigation.RouteTo(enemy, target, tick);
                    if (route == null) continue;
                    double score = Benefit(enemy, target, tick) / Math.Max(.1, route.Cost);
                    if (target.Id == enemy.TargetBuildingId) { oldRoute = route; oldScore = score; }
                    if (score > bestScore) { best = target; bestRoute = route; bestScore = score; }
                }
                if (best == null) { enemy.Route = null; enemy.CurrentAction = EnemyAction.WaitingForPassage; continue; }
                if (!invalid && best.Id != previous.Id && oldRoute != null && bestScore < oldScore * settings.SwitchScoreRatio)
                { best = previous; bestRoute = oldRoute; bestScore = oldScore; }
                bool switched = enemy.TargetBuildingId != best.Id;
                if (switched) { enemy.TargetAssignedTick = tick; Statistics.Switched(); }
                enemy.TargetBuildingId = best.Id; enemy.TargetX = best.CenterX; enemy.TargetZ = best.CenterZ; enemy.TargetScore = bestScore;
                enemy.TargetPurpose = best.Kind == BuildingKind.Core ? EnemyTargetPurpose.AdvanceToCore : best.IsTurret ? EnemyTargetPurpose.SuppressTurret : EnemyTargetPurpose.DisruptEnergy;
                // 保留站位，避免每次观察都来回跳动；持续拥堵才重新选择接近方向。
                bool blocked = enemy.BlockedSinceTick >= 0 && tick - enemy.BlockedSinceTick >= evaluationTicks * 2;
                bool improved = !switched && bestRoute.Cost < navigation.RemainingCost(enemy, best) * settings.RouteImprovementRatio;
                if (switched || enemy.Route == null || enemy.RouteRevision != grid.Revision || blocked || improved || enemy.RouteCursor >= enemy.Route.Cells.Count)
                {
                    enemy.Route = bestRoute;
                    // 搜索根节点只代表当前所在格，不是必须返回的航点。避让后重规划若再次追逐
                    // 脚下格中心，数个相切实体会互相堵住该点；直接沿下一条边继续前进。
                    enemy.RouteCursor = bestRoute.Cells.Count > 1 ? 1 : 0; enemy.RouteRevision = grid.Revision;
                    enemy.AttackPositionX = bestRoute.EndX; enemy.AttackPositionZ = bestRoute.EndZ;
                }
            }
        }
    }
}
