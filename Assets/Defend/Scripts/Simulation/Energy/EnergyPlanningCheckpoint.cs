using System;
using System.Collections.Generic;

namespace Defend.Simulation
{
    internal sealed partial class EnergyRoutePlanner
    {
        internal void CopyCheckpointFrom(EnergyRoutePlanner source, EnergyCheckpointCopies copies)
        {
            workspacePool.Clear(); copies.WorkspacePool = workspacePool;
            versions.Clear(); buckets.Clear(); outgoing.Clear(); incoming.Clear();
            geometricTrees.Clear(); availableTrees.Clear(); constrainedSearches.Clear(); constrainedAge.Clear(); pending.Clear();
            connections.Clear(); congestedRelays.Clear(); blockedSnapshot.Clear(); nextBlockedSnapshot.Clear(); waitingHistories.Clear();
            copies.Adjacencies.Add(source.outgoing, outgoing); copies.Adjacencies.Add(source.incoming, incoming);
            foreach (var pair in source.versions)
            {
                NodeVersion version = pair.Value;
                versions.Add(pair.Key, new NodeVersion { X = version.X, Z = version.Z, Life = version.Life,
                    Position = version.Position, Alive = version.Alive, Definition = version.Definition });
            }
            foreach (var pair in source.buckets)
            {
                var list = new List<BuildingState>();
                foreach (BuildingState building in pair.Value) list.Add(copies.Building(building));
                buckets.Add(pair.Key, list);
            }
            foreach (var pair in source.outgoing) outgoing.Add(pair.Key, copies.EdgeList(pair.Value));
            foreach (var pair in source.incoming) incoming.Add(pair.Key, copies.EdgeList(pair.Value));
            foreach (var pair in source.geometricTrees) geometricTrees.Add(pair.Key, copies.Search(pair.Value));
            foreach (var pair in source.availableTrees) availableTrees.Add(pair.Key, copies.Search(pair.Value));
            foreach (var pair in source.constrainedSearches) constrainedSearches.Add(pair.Key, copies.Search(pair.Value));
            foreach (EnergyPathSearch search in source.constrainedAge)
            {
                EnergyPathSearch copy = copies.Search(search);
                copy.ConstraintAgeNode = constrainedAge.AddLast(copy);
            }
            foreach (EnergyPathSearch search in source.pending) pending.Enqueue(copies.Search(search));
            connections.AddRange(source.connections);
            foreach (long id in source.congestedRelays) congestedRelays.Add(id);
            foreach (long id in source.blockedSnapshot) blockedSnapshot.Add(id);
            foreach (long id in source.nextBlockedSnapshot) nextBlockedSnapshot.Add(id);
            foreach (EnergyHistory history in source.waitingHistories) waitingHistories.Add(history);
            if (source.currentBuildings != null)
            {
                var list = new List<BuildingState>();
                foreach (BuildingState building in source.currentBuildings) list.Add(copies.Building(building));
                currentBuildings = list;
            }
            else currentBuildings = null;
            currentTick = source.currentTick; availabilityVersion = source.availabilityVersion; topologyVersion = source.topologyVersion;
            completedSearches = source.completedSearches; cacheHits = source.cacheHits; startedSearches = source.startedSearches;
            workUsed = source.workUsed; LastQueryDeferred = source.LastQueryDeferred;
        }
    }

    internal sealed partial class EnergyPathSearch
    {
        internal EnergyPathSearch(EnergyPathSearch original, EnergyCheckpointCopies copies)
        {
            copies.Searches.Add(original, this);
            adjacency = copies.Adjacencies[original.adjacency];
            reverse = original.reverse;
            source = copies.Building(original.source); target = copies.Building(original.target);
            lowerBounds = copies.Search(original.lowerBounds);
            blocked = original.blocked == null ? null : new HashSet<long>(original.blocked);
            forbidden = original.forbidden == null ? null : new HashSet<long>(original.forbidden);
            workspacePool = original.workspacePool == null ? null : copies.WorkspacePool;
            workspace = new Workspace(); best = workspace.Best; links = workspace.Links; frontier = workspace.Frontier; settled = workspace.Settled;
            workspaceReleased = original.workspaceReleased;
            // 已释放的工作集合可能已借给其他搜索，不能把那份内容误当成旧搜索状态。
            if (!workspaceReleased)
            {
                foreach (var pair in original.best) best.Add(pair.Key, CopyEntry(pair.Value, copies));
                foreach (var pair in original.links) links.Add(pair.Key, copies.Edge(pair.Value));
                frontier.CopyCheckpointFrom(original.frontier, copies);
                foreach (long id in original.settled) settled.Add(id);
            }
            foreach (var pair in original.routes) routes.Add(pair.Key, copies.Route(pair.Value));
            completedRoute = copies.Route(original.completedRoute);
            current = CopyEntry(original.current, copies); currentEdges = copies.EdgeList(original.currentEdges);
            currentEdge = original.currentEdge; sequence = original.sequence;
            Completed = original.Completed; cancelled = original.cancelled;
            LastWorkedTick = original.LastWorkedTick; AvailabilityVersion = original.AvailabilityVersion;
            TopologyVersion = original.TopologyVersion; LastRequestedTick = original.LastRequestedTick; ConstraintKey = original.ConstraintKey;
        }
        private static Entry CopyEntry(Entry source, EnergyCheckpointCopies copies)
        {
            source.Node = copies.Building(source.Node); return source;
        }
        internal sealed partial class Frontier
        {
            internal void CopyCheckpointFrom(Frontier source, EnergyCheckpointCopies copies)
            {
                entries.Clear();
                foreach (Entry entry in source.entries) entries.Add(CopyEntry(entry, copies));
            }
        }
    }
}
