using System;
using System.Collections.Generic;

namespace Defend.Simulation
{
    /// <summary>
    /// 可续算的最短路工作项。反向搜索一次生成通往同一终端的路径树，各供应建筑复用该树。
    /// 每次检查边或取出候选均计入预算；停止时保留当前边游标，下个逻辑步继续。
    /// </summary>
    internal sealed partial class EnergyPathSearch
    {
        internal struct Entry
        {
            public BuildingState Node;
            public double Cost;
            public long Duration;
            public int Hops;
            public long Sequence;
            public double EstimatedCost;
            public long EstimatedDuration;
            public int EstimatedHops;
        }
        private sealed class EntryComparer : IComparer<Entry>
        {
            public int Compare(Entry first, Entry second)
            {
                int order = first.EstimatedCost.CompareTo(second.EstimatedCost);
                if (order != 0) return order;
                order = first.EstimatedDuration.CompareTo(second.EstimatedDuration);
                if (order != 0) return order;
                order = first.EstimatedHops.CompareTo(second.EstimatedHops);
                if (order != 0) return order;
                order = first.Node.Id.CompareTo(second.Node.Id);
                return order != 0 ? order : first.Sequence.CompareTo(second.Sequence);
            }
        }

        /// <summary>连续数组中的二叉最小堆，避免每次松弛边分配候选对象和树节点。</summary>
        internal sealed partial class Frontier
        {
            private readonly List<Entry> entries = new List<Entry>();
            private readonly EntryComparer comparer = new EntryComparer();
            public int Count => entries.Count;
            public void Clear() { entries.Clear(); }
            public void Add(Entry entry)
            {
                int index = entries.Count;
                entries.Add(entry);
                while (index > 0)
                {
                    int parent = (index - 1) / 2;
                    if (comparer.Compare(entries[parent], entry) <= 0) break;
                    entries[index] = entries[parent];
                    index = parent;
                }
                entries[index] = entry;
            }

            public Entry RemoveMinimum()
            {
                Entry result = entries[0];
                int remaining = entries.Count - 1;
                Entry last = entries[remaining];
                entries.RemoveAt(remaining);
                if (remaining == 0) return result;
                int index = 0;
                while (index * 2 + 1 < remaining)
                {
                    int child = index * 2 + 1;
                    if (child + 1 < remaining && comparer.Compare(entries[child + 1], entries[child]) < 0) child++;
                    if (comparer.Compare(last, entries[child]) <= 0) break;
                    entries[index] = entries[child];
                    index = child;
                }
                entries[index] = last;
                return result;
            }
        }

        private readonly Dictionary<long, List<EnergyEdge>> adjacency;
        /// <summary>历史查询完成后保留独立路线，清空并复用临时容器；在途批次不引用这些容器。</summary>
        internal sealed class Workspace
        {
            public readonly Dictionary<long, Entry> Best = new Dictionary<long, Entry>();
            public readonly Dictionary<long, EnergyEdge> Links = new Dictionary<long, EnergyEdge>();
            public readonly Frontier Frontier = new Frontier();
            public readonly HashSet<long> Settled = new HashSet<long>();
            public void Clear() { Best.Clear(); Links.Clear(); Frontier.Clear(); Settled.Clear(); }
        }
        private readonly Workspace workspace;
        private readonly Stack<Workspace> workspacePool;
        private bool workspaceReleased;
        private readonly Dictionary<long, Entry> best;
        private readonly Dictionary<long, EnergyEdge> links;
        private readonly Dictionary<long, EnergyRoute> routes = new Dictionary<long, EnergyRoute>();
        private readonly Frontier frontier;
        private readonly HashSet<long> settled;
        private EnergyRoute completedRoute;
        private readonly HashSet<long> blocked;
        private readonly HashSet<long> forbidden;
        private readonly BuildingState source;
        private readonly BuildingState target;
        private readonly bool reverse;
        private readonly EnergyPathSearch lowerBounds;
        private Entry current;
        private List<EnergyEdge> currentEdges;
        private int currentEdge;
        private long sequence;
        public bool Completed { get; private set; }
        private bool cancelled;
        public bool Cancelled
        {
            get { return cancelled; }
            set { cancelled = value; if (value) ReleaseWorkspace(); }
        }
        public long LastWorkedTick { get; set; } = -1;
        public long AvailabilityVersion { get; }
        public long TopologyVersion { get; }
        public long LastRequestedTick { get; set; }
        public bool AvoidsBlockedRelays => blocked != null;
        public long TargetId => target.Id;
        public BuildingState Target => target;
        public Tuple<long, long, EnergyHistory, bool> ConstraintKey { get; set; }
        public LinkedListNode<EnergyPathSearch> ConstraintAgeNode { get; set; }

        public EnergyPathSearch(Dictionary<long, List<EnergyEdge>> adjacency, BuildingState source,
            BuildingState target, bool reverse, HashSet<long> blocked, EnergyHistory history, long availabilityVersion, long topologyVersion,
            EnergyPathSearch lowerBounds = null, Stack<Workspace> workspacePool = null)
        {
            this.workspacePool = reverse ? null : workspacePool;
            workspace = this.workspacePool != null && this.workspacePool.Count > 0 ? this.workspacePool.Pop() : new Workspace();
            best = workspace.Best;
            links = workspace.Links;
            frontier = workspace.Frontier;
            settled = workspace.Settled;
            this.adjacency = adjacency;
            this.source = source;
            this.target = target;
            this.reverse = reverse;
            this.lowerBounds = lowerBounds;
            this.blocked = blocked == null ? null : new HashSet<long>(blocked);
            AvailabilityVersion = availabilityVersion;
            TopologyVersion = topologyVersion;
            if (history != null)
            {
                forbidden = new HashSet<long>();
                for (EnergyHistory entry = history; entry != null; entry = entry.Previous) forbidden.Add(entry.BuildingId);
            }
            Entry first = new Entry { Node = reverse ? target : source, Sequence = ++sequence };
            Estimate(ref first);
            frontier.Add(first);
            best.Add(first.Node.Id, first);
        }

        public int Advance(int maximumWork)
        {
            int used = 0;
            while (!Completed && !Cancelled && used < maximumWork)
            {
                if (currentEdges == null)
                {
                    if (frontier.Count == 0) { Completed = true; break; }
                    current = frontier.RemoveMinimum();
                    used++;
                    if (!settled.Add(current.Node.Id)) continue;
                    if (!reverse && current.Node.Id == target.Id) { Completed = true; break; }
                    // 被拥堵标记的节点仍可作为出发点，但反向树不能让上游把它当作过路节点。
                    if (reverse && current.Node.Id != target.Id && current.Node.IsEnergyDestination) continue;
                    if (reverse && current.Node.Id != target.Id && blocked != null && blocked.Contains(current.Node.Id)) continue;
                    if (!adjacency.TryGetValue(current.Node.Id, out currentEdges)) continue;
                    currentEdge = 0;
                    if (used >= maximumWork) continue;
                }
                if (currentEdge >= currentEdges.Count) { currentEdges = null; continue; }
                EnergyEdge edge = currentEdges[currentEdge++];
                used++;
                BuildingState next = reverse ? edge.Source : edge.Target;
                if (settled.Contains(next.Id)) continue;
                if (!reverse && next.IsEnergyDestination && next.Id != target.Id) continue;
                if (!reverse && forbidden != null && forbidden.Contains(next.Id)) continue;
                if (!reverse && next.Id != target.Id && blocked != null && blocked.Contains(next.Id)) continue;
                double cost = current.Cost + edge.Cost;
                long duration = current.Duration + edge.FlightTicks;
                int hops = current.Hops + 1;
                if (best.TryGetValue(next.Id, out Entry previous))
                {
                    int comparison = cost.CompareTo(previous.Cost);
                    if (comparison > 0 || (comparison == 0 && (duration > previous.Duration ||
                        (duration == previous.Duration && hops >= previous.Hops)))) continue;
                }
                var candidate = new Entry { Node = next, Cost = cost, Duration = duration, Hops = hops, Sequence = ++sequence };
                Estimate(ref candidate);
                best[next.Id] = candidate;
                links[next.Id] = edge;
                frontier.Add(candidate);
            }
            if (Completed && !reverse)
            {
                if (settled.Contains(target.Id) && best.TryGetValue(target.Id, out Entry found))
                    completedRoute = new EnergyRoute(CreateEdges(source.Id), found.Cost, TopologyVersion);
                ReleaseWorkspace();
            }
            return used;
        }

        private void ReleaseWorkspace()
        {
            if (reverse || workspaceReleased) return;
            workspaceReleased = true;
            workspace.Clear();
            // 只保存有限数量的空工作集合，避免一次压力峰值变成永久保留的池容量。
            if (workspacePool != null && workspacePool.Count < 64) workspacePool.Push(workspace);
        }

        private void Estimate(ref Entry entry)
        {
            entry.EstimatedCost = entry.Cost;
            entry.EstimatedDuration = entry.Duration;
            entry.EstimatedHops = entry.Hops;
            // 完成的无约束反向树给出剩余损耗的下界。禁止历史节点只会增加代价，
            // 因而受约束查询可用 A* 收窄展开范围，仍按损耗、行程、跳数依次择优。
            if (lowerBounds == null || !lowerBounds.best.TryGetValue(entry.Node.Id, out Entry remaining)) return;
            entry.EstimatedCost += remaining.Cost;
            entry.EstimatedDuration += remaining.Duration;
            entry.EstimatedHops += remaining.Hops;
        }

        public EnergyRoute RouteFrom(BuildingState start)
        {
            // Dijkstra 已确定节点的后继链不会再变化，不必等整张图搜索完才服务近处供应者。
            if (Cancelled || (!reverse && !Completed)) return null;
            if (!reverse) return completedRoute;
            if (routes.TryGetValue(start.Id, out EnergyRoute route)) return route;
            long endpoint = reverse ? start.Id : target.Id;
            if (!settled.Contains(endpoint) || !best.TryGetValue(endpoint, out Entry found)) return null;
            route = new EnergyRoute(this, start.Id, found.Cost);
            routes.Add(start.Id, route);
            return route;
        }

        public EnergyEdge[] CreateEdges(long startingId)
        {
            var result = new List<EnergyEdge>();
            if (reverse)
            {
                long node = startingId;
                while (node != target.Id)
                {
                    EnergyEdge edge = links[node];
                    result.Add(edge);
                    node = edge.Target.Id;
                }
            }
            else
            {
                long node = target.Id;
                while (node != source.Id)
                {
                    EnergyEdge edge = links[node];
                    result.Add(edge);
                    node = edge.Source.Id;
                }
                result.Reverse();
            }
            return result.ToArray();
        }
    }
}
