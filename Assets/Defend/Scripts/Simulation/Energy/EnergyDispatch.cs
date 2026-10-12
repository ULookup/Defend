using System;
using System.Collections.Generic;

namespace Defend.Simulation
{
    /// <summary>目标分配与输出仲裁。所有候选都用真实库存报价，成交时才创建承诺。</summary>
    internal sealed class EnergyDispatch
    {
        private sealed class Offer
        {
            public EnergyNodeRuntime Owner;
            public EnergyRoute Route;
            public long Sent;
            public EnergyBatch StoredBatch;
        }
        private sealed class OfferSearch
        {
            public readonly List<Offer> Offers = new List<Offer>();
            public readonly List<EnergyNodeRuntime> PendingSources = new List<EnergyNodeRuntime>();
            public int PendingCursor;
            public int FirstAvailableOffer;
            public long LastResolutionTick = -1;
            public long LastSelectionTick = -1;
        }
        private const int MaximumTransactionsPerTick = 4096;
        private const int MaximumCandidateChecksPerTick = 8192;
        private readonly List<EnergyNodeRuntime> orderedNodes;
        private readonly List<BuildingState> consumers;
        private readonly EnergyRoutePlanner planner;
        private readonly EnergyTransport transport;
        private readonly WeightedEnergySchedule allocationSchedule;
        private readonly BatterySupplyPolicy batteries;
        private readonly Dictionary<long, OfferSearch> offerSearches = new Dictionary<long, OfferSearch>();
        private readonly List<EnergyNodeRuntime> sources = new List<EnergyNodeRuntime>();
        private long offerTopologyVersion = -1;
        private int transactions;
        private int candidateChecks;
        private int relayCursor;

        public EnergyDispatch(List<EnergyNodeRuntime> orderedNodes, List<BuildingState> consumers,
            EnergyRoutePlanner planner, EnergyTransport transport, WeightedEnergySchedule allocationSchedule, EnergySettings settings)
        {
            this.orderedNodes = orderedNodes;
            this.consumers = consumers;
            this.planner = planner;
            this.transport = transport;
            this.allocationSchedule = allocationSchedule;
            batteries = new BatterySupplyPolicy(settings);
        }

        public bool Run(long tick)
        {
            transactions = 0;
            candidateChecks = 0;
            RefreshSupplyCandidates();
            ReleaseUnusableRoutes();
            for (int visited = 0; visited < orderedNodes.Count && transactions < MaximumTransactionsPerTick &&
                candidateChecks < MaximumCandidateChecksPerTick / 2; visited++)
            {
                int index = relayCursor % orderedNodes.Count;
                relayCursor = (index + 1) % orderedNodes.Count;
                EnergyNodeRuntime node = orderedNodes[index];
                if (node.Building.Kind != BuildingKind.Relay || !node.Building.CanOutput || node.AvailableOutput <= 0) continue;
                DispatchRelay(node, tick);
            }
            AllocateAvailable(tick);
            return transactions >= MaximumTransactionsPerTick || candidateChecks >= MaximumCandidateChecksPerTick;
        }

        private void RefreshSupplyCandidates()
        {
            if (offerTopologyVersion == planner.TopologyVersion) return;
            offerTopologyVersion = planner.TopologyVersion;
            offerSearches.Clear();
            sources.Clear();
            for (int index = 0; index < orderedNodes.Count; index++)
            {
                EnergyNodeRuntime node = orderedNodes[index];
                // 候选描述静态供给资格。暂时空仓或处于冷却中的反应堆仍须保留，成交时再验动态状态。
                if (node.Building.Kind == BuildingKind.Reactor && node.Building.CanOutput) sources.Add(node);
            }
        }

        private void ReleaseUnusableRoutes()
        {
            IReadOnlyList<EnergyBatch> batches = transport.Batches;
            for (int index = 0; index < batches.Count; index++)
            {
                EnergyBatch batch = batches[index];
                if (batch.Retired || batch.Destination == null) continue;
                // 当前跳已经支付损耗并预约了物理空间，只检验尚未发送的后缀。
                int first = batch.RouteEdge + (batch.InFlight ? 1 : 0);
                bool invalid = !batch.Destination.IsAlive || batch.Destination.IsSold || !planner.IsValid(batch.Route, first);
                if (!invalid && !batch.InFlight && first < batch.Route.Edges.Length)
                {
                    BuildingState next = batch.Route.Edges[first].Target;
                    invalid = planner.IsRelayBlocked(next);
                }
                if (invalid) transport.ReleaseCommitment(batch);
            }
        }

        private void DispatchRelay(EnergyNodeRuntime node, long tick)
        {
            if (node.Batches.Count == 0 || consumers.Count == 0) return;
            int misses = 0;
            while (node.AvailableOutput > 0 && transactions < MaximumTransactionsPerTick && candidateChecks < MaximumCandidateChecksPerTick / 2)
            {
                int index = node.OutputSchedule.Cursor % consumers.Count;
                BuildingState consumer = consumers[index];
                node.OutputSchedule.Cursor = (index + 1) % consumers.Count;
                long credit = node.OutputSchedule.Credit(consumer);
                EnergyBatch selected = null;
                EnergyRoute replacementRoute = null;
                long sent = 0;
                if (credit > 0)
                {
                    for (int batchIndex = 0; batchIndex < node.Batches.Count; batchIndex++)
                    {
                        if (candidateChecks++ >= MaximumCandidateChecksPerTick / 2) break;
                        EnergyBatch batch = node.Batches[batchIndex];
                        if (batch.Destination != consumer || batch.EligibleTick > tick) continue;
                        long available = Math.Min(batch.Amount, node.AvailableOutput);
                        available = LimitForNextHop(available, batch.Route.Edges[batch.RouteEdge], tick);
                        sent = EnergyArithmetic.LimitSend(available, Math.Min(credit, batch.Commitment), batch.Route, batch.RouteEdge);
                        if (sent > 0) { selected = batch; break; }
                    }
                    // 已有承诺与重新选目标的旧能量进入同一个出口轮询，使用同一份有效量额度。
                    if (selected == null && Demand(consumer) > 0)
                    {
                        for (int batchIndex = 0; batchIndex < node.Batches.Count; batchIndex++)
                        {
                            if (candidateChecks++ >= MaximumCandidateChecksPerTick / 2) break;
                            EnergyBatch batch = node.Batches[batchIndex];
                            if (batch.EligibleTick > tick) continue;
                            if (batch.Destination != null && (batch.Destination == consumer ||
                                node.OutputSchedule.Credit(batch.Destination) > 0)) continue;
                            EnergyRoute route = planner.Find(node.Building, consumer, batch.History, false);
                            if (route != null) route = PreferAvailableRoute(node.Building, consumer, batch.History, route);
                            if (route == null)
                            {
                                if (planner.LastQueryDeferred) batch.PlanningDeferredTick = tick;
                                continue;
                            }
                            long available = LimitForNextHop(Math.Min(batch.Amount, node.AvailableOutput), route.Edges[0], tick);
                            sent = EnergyArithmetic.LimitSend(available, Math.Min(credit, Demand(consumer)), route);
                            if (sent > 0) { selected = batch; replacementRoute = route; break; }
                        }
                    }
                }
                if (selected != null)
                {
                    if (replacementRoute == null) transport.Forward(node, selected, sent, tick);
                    else
                    {
                        // 本轮已获足额的目标不能把可转供库存锁死，让尚有份额的目标持续缺电。
                        if (selected.Destination != null) transport.ReleaseCommitment(selected);
                        transport.AssignAndSend(node, selected, consumer, replacementRoute, sent, tick);
                    }
                    if (!selected.InFlight && !selected.Retired && !selected.IsStalled)
                        selected.EligibleTick = node.NextOutputTick;
                    transactions++;
                    misses = 0;
                }
                else
                {
                    misses++;
                    if (misses == consumers.Count) node.OutputSchedule.BeginNextRound();
                    if (misses >= consumers.Count * 2) break;
                }
            }
        }

        private void AllocateAvailable(long tick)
        {
            int misses = 0;
            while (consumers.Count > 0 && transactions < MaximumTransactionsPerTick)
            {
                int index = allocationSchedule.Cursor % consumers.Count;
                BuildingState consumer = consumers[index];
                allocationSchedule.Cursor = (index + 1) % consumers.Count;
                long limit = Math.Min(Demand(consumer), allocationSchedule.Credit(consumer));
                Offer selected = limit > 0 ? FindOffer(consumer, limit, tick) : null;
                if (selected != null)
                {
                    transport.AssignAndSend(selected.Owner, selected.StoredBatch, consumer, selected.Route, selected.Sent, tick);
                    transactions++;
                    misses = 0;
                }
                else
                {
                    // 保留目标轮询游标，预算恢复后从下一目标开始，避免每步重试列表头部。
                    if (candidateChecks >= MaximumCandidateChecksPerTick) break;
                    misses++;
                    if (misses == consumers.Count)
                    {
                        allocationSchedule.BeginNextRound();
                    }
                    if (misses >= consumers.Count * 2) break;
                }
            }
        }

        private Offer FindOffer(BuildingState consumer, long limit, long tick)
        {
            if (!offerSearches.TryGetValue(consumer.Id, out OfferSearch search))
            {
                search = new OfferSearch();
                search.PendingSources.AddRange(sources);
                offerSearches[consumer.Id] = search;
            }
            ResolveSupplyRoutes(search, consumer, tick);
            if (search.LastSelectionTick != tick)
            {
                search.LastSelectionTick = tick;
                search.FirstAvailableOffer = 0;
            }
            Offer selected = null;
            for (int index = search.FirstAvailableOffer; index < search.Offers.Count; index++)
            {
                if (candidateChecks >= MaximumCandidateChecksPerTick) break;
                candidateChecks++;
                Offer offer = search.Offers[index];
                if (offer.Owner.AvailableOutput <= 0 || offer.Owner.Building.StoredEnergy <= 0)
                {
                    // 源头分配阶段只有扣减，已空的前缀本步不必反复扫描。下一步重新检查。
                    if (index == search.FirstAvailableOffer) search.FirstAvailableOffer++;
                    continue;
                }
                if (selected != null && selected.Route.Cost <= offer.Route.Cost) break;
                EnergyRoute route = offer.Route;
                if (!planner.ReuseCachedRoute(route)) continue;
                route = PreferAvailableRoute(offer.Owner.Building, consumer, null, route);
                if (route == null) continue;
                long available = Math.Min(offer.Owner.AvailableOutput, offer.Owner.Building.StoredEnergy);
                available = LimitForNextHop(available, route.Edges[0], tick);
                long sent = EnergyArithmetic.LimitSend(available, limit, route);
                if (sent <= 0 || (selected != null && selected.Route.Cost <= route.Cost)) continue;
                selected = new Offer { Owner = offer.Owner, Route = route, Sent = sent };
            }
            // 电池与反应堆竞争同一个终端额度，不为每块电池复制需求或权重。
            // 每个电池按入库次序寻找首个可服务批次；历史无路的旧批次不会阻塞后续库存。
            foreach (EnergyNodeRuntime node in orderedNodes)
            {
                if (node.Building.Kind != BuildingKind.Battery || !node.Building.CanOutput || node.AvailableOutput <= 0 ||
                    !batteries.CanSupport(node, consumer, tick)) continue;
                foreach (EnergyBatch batch in node.Batches)
                {
                    if (candidateChecks++ >= MaximumCandidateChecksPerTick) break;
                    if (batch.Retired || batch.InFlight || batch.Destination != null || batch.EligibleTick > tick) continue;
                    EnergyRoute route = planner.Find(node.Building, consumer, batch.History, false);
                    if (route != null) route = PreferAvailableRoute(node.Building, consumer, batch.History, route);
                    if (route == null) continue;
                    long available = LimitForNextHop(Math.Min(batch.Amount, node.AvailableOutput), route.Edges[0], tick);
                    long sent = EnergyArithmetic.LimitSend(available, Math.Min(limit, Demand(consumer)), route);
                    sent = batteries.LimitSend(node, consumer, route, sent, tick);
                    if (sent <= 0) continue;
                    if (selected == null || route.Cost < selected.Route.Cost)
                        selected = new Offer { Owner = node, StoredBatch = batch, Route = route, Sent = sent };
                    break;
                }
            }
            return selected;
        }

        private void ResolveSupplyRoutes(OfferSearch search, BuildingState consumer, long tick)
        {
            if (search.LastResolutionTick == tick || search.PendingSources.Count == 0) return;
            search.LastResolutionTick = tick;
            // 未就绪的远处供应者不能使已有近处路线停供；每目标每步限量推进，保留全局候选预算。
            int remaining = Math.Min(16, search.PendingSources.Count);
            bool changed = false;
            while (remaining-- > 0 && candidateChecks < MaximumCandidateChecksPerTick && search.PendingSources.Count > 0)
            {
                if (search.PendingCursor >= search.PendingSources.Count) search.PendingCursor = 0;
                EnergyNodeRuntime node = search.PendingSources[search.PendingCursor];
                candidateChecks++;
                EnergyRoute route = planner.Find(node.Building, consumer, null, false);
                if (route == null && planner.LastQueryDeferred) { search.PendingCursor++; continue; }
                search.PendingSources.RemoveAt(search.PendingCursor);
                if (route == null) continue;
                search.Offers.Add(new Offer { Owner = node, Route = route });
                changed = true;
            }
            if (!changed) return;
            search.Offers.Sort((first, second) =>
            {
                int comparison = first.Route.Cost.CompareTo(second.Route.Cost);
                return comparison != 0 ? comparison : first.Owner.Building.Id.CompareTo(second.Owner.Building.Id);
            });
            search.FirstAvailableOffer = 0;
        }

        private bool HasBlockedRelay(EnergyRoute route)
        {
            for (int index = 0; index < route.Edges.Length; index++)
            {
                BuildingState node = route.Edges[index].Target;
                if (planner.IsRelayBlocked(node)) return true;
            }
            return false;
        }

        private EnergyRoute PreferAvailableRoute(BuildingState source, BuildingState consumer, EnergyHistory history, EnergyRoute original)
        {
            if (!HasBlockedRelay(original)) return original;
            EnergyRoute alternative = planner.Find(source, consumer, history, true);
            if (alternative != null) return alternative;
            if (planner.LastQueryDeferred) return null;
            // 软拥堵是改选备用路的理由，不等于拆断线路。无备用路时仍可按真实下一跳容量排队。
            return original;
        }

        private long LimitForNextHop(long available, EnergyEdge edge, long tick)
        {
            if (edge.Target.IsConsumer) return available;
            long space = edge.Target.Kind == BuildingKind.Battery
                ? transport.AvailableChargingSpace(edge.Target, tick + edge.FlightTicks) : FreeSpace(edge.Target);
            if (space <= 0) return 0;
            if (space >= available) return available;
            // 保持本跳 received <= space。反向上取整得到第一个超额发送量，再减一。
            long maximumSent = checked((space + 1) * EnergyArithmetic.EfficiencyScale - 1) / edge.Efficiency;
            return Math.Min(available, maximumSent);
        }

        public static long Demand(BuildingState consumer)
        {
            return Math.Max(0, consumer.Definition.EnergyCapacity - consumer.StoredEnergy - consumer.CommittedIncoming);
        }

        private static long FreeSpace(BuildingState building)
        {
            return Math.Max(0, building.Definition.EnergyCapacity - building.StoredEnergy - building.ReservedIncoming);
        }
    }
}
