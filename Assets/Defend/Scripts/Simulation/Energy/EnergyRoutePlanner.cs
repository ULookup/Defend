using System;
using System.Collections.Generic;

namespace Defend.Simulation
{
    internal sealed partial class EnergyEdge
    {
        public readonly BuildingState Source;
        public readonly BuildingState Target;
        public readonly long Efficiency;
        public readonly double Cost;
        public readonly int FlightTicks;
        public readonly int SourcePositionRevision;
        public readonly int TargetPositionRevision;
        public readonly int SourceLifeGeneration;
        public readonly int TargetLifeGeneration;
        public EnergyEdge(BuildingState source, BuildingState target, long efficiency, int flightTicks)
        {
            Source = source;
            Target = target;
            Efficiency = efficiency;
            Cost = -Math.Log((double)efficiency / EnergyArithmetic.EfficiencyScale);
            FlightTicks = flightTicks;
            SourcePositionRevision = source.PositionRevision;
            TargetPositionRevision = target.PositionRevision;
            SourceLifeGeneration = source.LifeGeneration;
            TargetLifeGeneration = target.LifeGeneration;
        }
    }

    internal sealed class EnergyRoute
    {
        private EnergyEdge[] edges;
        private EnergyPathSearch search;
        private readonly long sourceId;
        public EnergyEdge[] Edges
        {
            get
            {
                if (edges == null)
                {
                    edges = search.CreateEdges(sourceId);
                    // 在途批次只保留自身路径，不因一条旧路线而留住整棵过期搜索树。
                    search = null;
                }
                return edges;
            }
        }
        public readonly double Cost;
        public long ValidatedTopologyVersion;
        public int ValidatedFirstEdge;
        public EnergyRoute(EnergyEdge[] edges, double cost, long topologyVersion)
        {
            this.edges = edges;
            Cost = cost;
            ValidatedTopologyVersion = topologyVersion;
        }
        public EnergyRoute(EnergyPathSearch search, long sourceId, double cost)
        {
            this.search = search;
            this.sourceId = sourceId;
            Cost = cost;
            ValidatedTopologyVersion = search.TopologyVersion;
        }
    }

    /// <summary>
    /// 图只描述几何连接。容量是查询时的临时过滤，不会永久删掉边。
    /// 共用反向最短路树，历史约束查询在可用时以该树提供 A* 的损耗下界。
    /// </summary>
    internal sealed partial class EnergyRoutePlanner
    {
        private sealed class NodeVersion
        {
            public int X;
            public int Z;
            public int Life;
            public int Position;
            public bool Alive;
            public BuildingDefinition Definition;
        }
        private const int BucketSize = 4;
        private readonly Stack<EnergyPathSearch.Workspace> workspacePool = new Stack<EnergyPathSearch.Workspace>();
        private readonly EnergySettings settings;
        private readonly Dictionary<long, NodeVersion> versions = new Dictionary<long, NodeVersion>();
        private readonly Dictionary<long, List<BuildingState>> buckets = new Dictionary<long, List<BuildingState>>();
        private readonly Dictionary<long, List<EnergyEdge>> outgoing = new Dictionary<long, List<EnergyEdge>>();
        private readonly Dictionary<long, List<EnergyEdge>> incoming = new Dictionary<long, List<EnergyEdge>>();
        private readonly Dictionary<long, EnergyPathSearch> geometricTrees = new Dictionary<long, EnergyPathSearch>();
        private readonly Dictionary<long, EnergyPathSearch> availableTrees = new Dictionary<long, EnergyPathSearch>();
        private readonly Dictionary<Tuple<long, long, EnergyHistory, bool>, EnergyPathSearch> constrainedSearches =
            new Dictionary<Tuple<long, long, EnergyHistory, bool>, EnergyPathSearch>();
        private readonly LinkedList<EnergyPathSearch> constrainedAge = new LinkedList<EnergyPathSearch>();
        private readonly Queue<EnergyPathSearch> pending = new Queue<EnergyPathSearch>();
        private readonly List<EnergyConnection> connections = new List<EnergyConnection>();
        private readonly HashSet<long> congestedRelays = new HashSet<long>();
        private readonly HashSet<long> blockedSnapshot = new HashSet<long>();
        private readonly HashSet<long> nextBlockedSnapshot = new HashSet<long>();
        private readonly HashSet<EnergyHistory> waitingHistories = new HashSet<EnergyHistory>();
        private IReadOnlyList<BuildingState> currentBuildings;
        private long currentTick;
        private long availabilityVersion;
        private long topologyVersion;
        private long completedSearches;
        private long cacheHits;
        private long startedSearches;
        private int workUsed;
        public bool LastQueryDeferred { get; private set; }
        public long TopologyVersion => topologyVersion;
        public IReadOnlyList<EnergyConnection> Connections { get; }
        public EnergyPlanningStatistics Statistics
        {
            get
            {
                int count = 0;
                foreach (EnergyPathSearch search in pending)
                    if (!search.Completed && !search.Cancelled) count++;
                return new EnergyPlanningStatistics(workUsed, count, completedSearches, cacheHits, startedSearches);
            }
        }

        public EnergyRoutePlanner(EnergySettings settings)
        {
            this.settings = settings;
            Connections = connections.AsReadOnly();
        }

        public bool Refresh(IReadOnlyList<BuildingState> buildings)
        {
            currentBuildings = buildings;
            bool changed = versions.Count != buildings.Count;
            for (int index = 0; index < buildings.Count; index++)
            {
                BuildingState building = buildings[index];
                if (!versions.TryGetValue(building.Id, out NodeVersion version))
                {
                    version = new NodeVersion();
                    versions.Add(building.Id, version);
                    changed = true;
                }
                bool alive = building.IsAlive && !building.IsSold;
                if (version.X != building.X || version.Z != building.Z || version.Life != building.LifeGeneration ||
                    version.Position != building.PositionRevision || version.Alive != alive || version.Definition == null ||
                    version.Definition.TransmissionRange != building.Definition.TransmissionRange) changed = true;
                version.X = building.X;
                version.Z = building.Z;
                version.Life = building.LifeGeneration;
                version.Position = building.PositionRevision;
                version.Alive = alive;
                version.Definition = building.Definition;
            }
            if (!changed) return false;
            topologyVersion++;

            buckets.Clear();
            outgoing.Clear();
            incoming.Clear();
            connections.Clear();
            geometricTrees.Clear();
            availableTrees.Clear();
            constrainedSearches.Clear();
            constrainedAge.Clear();
            pending.Clear();
            for (int index = 0; index < buildings.Count; index++)
            {
                BuildingState building = buildings[index];
                if (!building.IsAlive || building.IsSold) continue;
                long key = BucketKey(Bucket(building.CenterX), Bucket(building.CenterZ));
                if (!buckets.TryGetValue(key, out List<BuildingState> members))
                {
                    members = new List<BuildingState>();
                    buckets.Add(key, members);
                }
                members.Add(building);
                if (building.CanOutput) outgoing.Add(building.Id, new List<EnergyEdge>());
            }
            for (int index = 0; index < buildings.Count; index++)
            {
                BuildingState source = buildings[index];
                if (!outgoing.TryGetValue(source.Id, out List<EnergyEdge> edges)) continue;
                double range = source.Definition.TransmissionRange;
                if (range <= 0 || source.Definition.OutputAmount <= 0) continue;
                int minimumX = Bucket(source.CenterX - range);
                int maximumX = Bucket(source.CenterX + range);
                int minimumZ = Bucket(source.CenterZ - range);
                int maximumZ = Bucket(source.CenterZ + range);
                for (int x = minimumX; x <= maximumX; x++)
                for (int z = minimumZ; z <= maximumZ; z++)
                {
                    if (!buckets.TryGetValue(BucketKey(x, z), out List<BuildingState> members)) continue;
                    for (int member = 0; member < members.Count; member++)
                    {
                        BuildingState target = members[member];
                        if (target.Id == source.Id || !target.CanReceive) continue;
                        double horizontal = target.CenterX - source.CenterX;
                        double vertical = target.CenterZ - source.CenterZ;
                        double distance = Math.Sqrt(horizontal * horizontal + vertical * vertical);
                        if (distance > range) continue;
                        long efficiency = EfficiencyAt(distance, range, settings.MaximumLossAtRange);
                        if (efficiency <= 0) continue;
                        int flightTicks = Math.Max(1, checked((int)Math.Ceiling(distance * settings.FlightTicksPerCell)));
                        EnergyEdge edge = new EnergyEdge(source, target, efficiency, flightTicks);
                        edges.Add(edge);
                        if (!incoming.TryGetValue(target.Id, out List<EnergyEdge> predecessors))
                        {
                            predecessors = new List<EnergyEdge>();
                            incoming.Add(target.Id, predecessors);
                        }
                        predecessors.Add(edge);
                        connections.Add(new EnergyConnection(source.Id, target.Id, (double)efficiency / EnergyArithmetic.EfficiencyScale));
                    }
                }
                edges.Sort((first, second) => first.Target.Id.CompareTo(second.Target.Id));
            }
            return true;
        }

        public bool IsValid(EnergyRoute route, int firstEdge)
        {
            if (route == null) return false;
            if (route.ValidatedTopologyVersion == topologyVersion && firstEdge >= route.ValidatedFirstEdge) return true;
            for (int index = firstEdge; index < route.Edges.Length; index++)
            {
                EnergyEdge edge = route.Edges[index];
                if (!edge.Source.IsAlive || edge.Source.IsSold || !edge.Target.IsAlive || edge.Target.IsSold) return false;
                if (edge.Source.PositionRevision != edge.SourcePositionRevision || edge.Target.PositionRevision != edge.TargetPositionRevision ||
                    edge.Source.LifeGeneration != edge.SourceLifeGeneration || edge.Target.LifeGeneration != edge.TargetLifeGeneration) return false;
                if (!outgoing.TryGetValue(edge.Source.Id, out List<EnergyEdge> edges)) return false;
                bool found = false;
                for (int next = 0; next < edges.Count; next++)
                    if (edges[next].Target.Id == edge.Target.Id && edges[next].Efficiency == edge.Efficiency && edges[next].FlightTicks == edge.FlightTicks)
                    { found = true; break; }
                if (!found) return false;
            }
            route.ValidatedTopologyVersion = topologyVersion;
            route.ValidatedFirstEdge = firstEdge;
            return true;
        }

        public bool ReuseCachedRoute(EnergyRoute route)
        {
            if (!IsValid(route, 0)) return false;
            cacheHits++;
            return true;
        }

        public void RefreshCongestion(IReadOnlyList<EnergyBatch> batches)
        {
            congestedRelays.Clear();
            waitingHistories.Clear();
            for (int index = 0; index < batches.Count; index++)
            {
                EnergyBatch batch = batches[index];
                if (!batch.Retired && !batch.InFlight) waitingHistories.Add(batch.History);
                if (!batch.Retired && !batch.InFlight && batch.IsStalled && batch.BlocksIncomingTraffic)
                    congestedRelays.Add(batch.Location.Id);
            }
        }

        public bool IsRelayBlocked(BuildingState building)
        {
            // 持续抢不到输出的中继即使尚未满仓，也允许上游尝试备用路。正常冷却不会进入此集合。
            return building.Kind == BuildingKind.Relay && (congestedRelays.Contains(building.Id) ||
                building.StoredEnergy + building.ReservedIncoming >= building.Definition.EnergyCapacity);
        }

        public void BeginStep(long tick)
        {
            currentTick = tick;
            workUsed = 0;
            RefreshAvailabilitySnapshot();
            int waitingAtStart = pending.Count;
            for (int index = 0; index < waitingAtStart && workUsed < settings.MaximumPathWorkPerTick; index++)
            {
                EnergyPathSearch search = pending.Dequeue();
                if (search.Cancelled || search.Completed) continue;
                if (IsAbandoned(search))
                {
                    CancelSearch(search);
                    continue;
                }
                Advance(search);
                if (!search.Completed) pending.Enqueue(search);
            }
        }

        private bool IsAbandoned(EnergyPathSearch search)
        {
            // 历史专用查询只有实际停留的批次会继续使用。出发、死亡、超时后无需替它算完旧路线。
            EnergyHistory history = search.ConstraintKey?.Item3;
            if (history != null) return !waitingHistories.Contains(history);
            // 几何树始终保留。无批次身份的动态查询仅在终端已无缺口时撤销，
            // 不能用“多久没轮询到”判定失效，否则预算紧张时仍存活的工作会反复从零开始。
            BuildingState target = search.Target;
            return search.AvoidsBlockedRelays && (!target.IsAlive || target.IsSold || EnergyDispatch.Demand(target) == 0);
        }

        private void CancelSearch(EnergyPathSearch search)
        {
            search.Cancelled = true;
            if (search.ConstraintKey != null)
            {
                if (constrainedSearches.TryGetValue(search.ConstraintKey, out EnergyPathSearch stored) && stored == search)
                    constrainedSearches.Remove(search.ConstraintKey);
                if (search.ConstraintAgeNode != null && search.ConstraintAgeNode.List != null)
                    constrainedAge.Remove(search.ConstraintAgeNode);
                search.ConstraintAgeNode = null;
            }
            else if (availableTrees.TryGetValue(search.TargetId, out EnergyPathSearch stored) && stored == search)
                availableTrees.Remove(search.TargetId);
        }

        private void RefreshAvailabilitySnapshot()
        {
            nextBlockedSnapshot.Clear();
            for (int index = 0; index < currentBuildings.Count; index++)
            {
                BuildingState building = currentBuildings[index];
                if (building.Kind == BuildingKind.Relay && building.IsAlive && IsRelayBlocked(building)) nextBlockedSnapshot.Add(building.Id);
            }
            if (blockedSnapshot.SetEquals(nextBlockedSnapshot)) return;
            blockedSnapshot.Clear();
            foreach (long id in nextBlockedSnapshot) blockedSnapshot.Add(id);
            availabilityVersion++;
        }

        /// <summary>
        /// 同一步内只有实际发送会改变中继的库存与预约之和。只刷新受影响的两个端点，
        /// 避免每次候选报价重新扫描整张图；下一步开始仍完整采集故障与到货后的状态。
        /// </summary>
        public void RefreshAvailability(BuildingState source, BuildingState target)
        {
            bool changed = RefreshRelayAvailability(source);
            changed |= RefreshRelayAvailability(target);
            if (changed) availabilityVersion++;
        }

        private bool RefreshRelayAvailability(BuildingState building)
        {
            if (building.Kind != BuildingKind.Relay) return false;
            if (building.IsAlive && !building.IsSold && IsRelayBlocked(building))
                return blockedSnapshot.Add(building.Id);
            return blockedSnapshot.Remove(building.Id);
        }

        public EnergyRoute Find(BuildingState start, BuildingState target, EnergyHistory history, bool avoidBlockedRelays)
        {
            LastQueryDeferred = false;
            if (!start.IsAlive || !target.IsAlive || start.Id == target.Id) return null;
            if (history != null && history.Contains(target.Id)) return null;
            Dictionary<long, EnergyPathSearch> trees = avoidBlockedRelays ? availableTrees : geometricTrees;
            if (!trees.TryGetValue(target.Id, out EnergyPathSearch tree) ||
                (avoidBlockedRelays && tree.Completed && tree.AvailabilityVersion != availabilityVersion))
            {
                tree = StartSearch(start, target, null, true, avoidBlockedRelays);
                trees[target.Id] = tree;
            }
            tree.LastRequestedTick = currentTick;
            Advance(tree);
            EnergyRoute candidate = tree.RouteFrom(start);
            if (candidate == null && !tree.Completed) { LastQueryDeferred = true; return null; }
            if (candidate == null && (!avoidBlockedRelays || tree.AvailabilityVersion == availabilityVersion)) return null;
            if (candidate != null && Compatible(candidate, history) && (!avoidBlockedRelays || Available(candidate)))
            { cacheHits++; return candidate; }

            // 历史与共享树冲突才进行受约束查询；相同批次的重复报价复用工作项，不反复从零寻路。
            var key = Tuple.Create(start.Id, target.Id, history, avoidBlockedRelays);
            if (!constrainedSearches.TryGetValue(key, out EnergyPathSearch constrained) ||
                (avoidBlockedRelays && constrained.Completed && constrained.AvailabilityVersion != availabilityVersion))
            {
                // 替换同一查询时同步移除旧年龄记录，不能让旧版本挤走仍有效的其他缓存。
                if (constrained != null) CancelSearch(constrained);
                constrained = StartSearch(start, target, history, false, avoidBlockedRelays);
                constrained.ConstraintKey = key;
                constrainedSearches[key] = constrained;
                constrained.ConstraintAgeNode = constrainedAge.AddLast(constrained);
                while (constrainedAge.Count > 2048)
                {
                    EnergyPathSearch oldest = constrainedAge.First.Value;
                    constrainedAge.RemoveFirst();
                    oldest.ConstraintAgeNode = null;
                    oldest.Cancelled = true;
                    if (constrainedSearches.TryGetValue(oldest.ConstraintKey, out EnergyPathSearch stored) && stored == oldest)
                        constrainedSearches.Remove(oldest.ConstraintKey);
                }
            }
            constrained.LastRequestedTick = currentTick;
            Advance(constrained);
            if (!constrained.Completed) { LastQueryDeferred = true; return null; }
            candidate = constrained.RouteFrom(start);
            if (candidate != null && avoidBlockedRelays && !Available(candidate))
            { LastQueryDeferred = true; return null; }
            return candidate;
        }

        private static bool Compatible(EnergyRoute route, EnergyHistory history)
        {
            if (history == null) return true;
            for (int index = 0; index < route.Edges.Length; index++)
                if (history.Contains(route.Edges[index].Target.Id)) return false;
            return true;
        }

        private bool Available(EnergyRoute route)
        {
            for (int index = 0; index < route.Edges.Length; index++)
                if (IsRelayBlocked(route.Edges[index].Target)) return false;
            return true;
        }

        private EnergyPathSearch StartSearch(BuildingState source, BuildingState target, EnergyHistory history, bool reverse, bool avoidBlocked)
        {
            // 只有整棵无约束树完成后才使用其下界，避免部分已知距离破坏启发函数的一致性。
            geometricTrees.TryGetValue(target.Id, out EnergyPathSearch lowerBounds);
            if (reverse || lowerBounds == null || !lowerBounds.Completed) lowerBounds = null;
            var search = new EnergyPathSearch(reverse ? incoming : outgoing, source, target, reverse,
                avoidBlocked ? blockedSnapshot : null, history, availabilityVersion, topologyVersion, lowerBounds, workspacePool);
            startedSearches++;
            pending.Enqueue(search);
            return search;
        }

        private void Advance(EnergyPathSearch search)
        {
            if (search.Completed || search.Cancelled || search.LastWorkedTick == currentTick || workUsed >= settings.MaximumPathWorkPerTick) return;
            search.LastWorkedTick = currentTick;
            int allowance = Math.Min(settings.PathWorkSlice, settings.MaximumPathWorkPerTick - workUsed);
            workUsed += search.Advance(allowance);
            if (search.Completed) completedSearches++;
        }

        private static int Bucket(double position) { return (int)Math.Floor(position / BucketSize); }
        private static long BucketKey(int x, int z) { return ((long)x << 32) | (uint)z; }

        internal static long EfficiencyAt(double distance, double range, double maximumLossAtRange)
        {
            if (range <= 0 || distance > range) return 0;
            double fraction = distance / range;
            return (long)Math.Round((1 - maximumLossAtRange * fraction * fraction) * EnergyArithmetic.EfficiencyScale);
        }
    }
}
