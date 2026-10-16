using System;
using System.Collections.Generic;

namespace Defend.Simulation
{
    /// <summary>不可公开修改的会话记录。内部持有独立模拟副本，不包含 Unity 对象。</summary>
    public sealed class EnergyCheckpoint
    {
        internal readonly EnergySystem State;
        internal EnergyCheckpoint(EnergySystem state) { State = state; }
    }

    /// <summary>显式重绑身份关系；共享的历史链不可变，所有可变运行对象均另建。</summary>
    internal sealed class EnergyCheckpointCopies
    {
        internal readonly Dictionary<long, BuildingState> Buildings = new Dictionary<long, BuildingState>();
        internal readonly Dictionary<EnergyNodeRuntime, EnergyNodeRuntime> Nodes = new Dictionary<EnergyNodeRuntime, EnergyNodeRuntime>();
        internal readonly Dictionary<EnergyBatch, EnergyBatch> Batches = new Dictionary<EnergyBatch, EnergyBatch>();
        internal readonly Dictionary<EnergyEdge, EnergyEdge> Edges = new Dictionary<EnergyEdge, EnergyEdge>();
        internal readonly Dictionary<EnergyRoute, EnergyRoute> Routes = new Dictionary<EnergyRoute, EnergyRoute>();
        internal readonly Dictionary<EnergyPathSearch, EnergyPathSearch> Searches = new Dictionary<EnergyPathSearch, EnergyPathSearch>();
        internal readonly Dictionary<Dictionary<long, List<EnergyEdge>>, Dictionary<long, List<EnergyEdge>>> Adjacencies =
            new Dictionary<Dictionary<long, List<EnergyEdge>>, Dictionary<long, List<EnergyEdge>>>();
        internal readonly Dictionary<List<EnergyEdge>, List<EnergyEdge>> EdgeLists = new Dictionary<List<EnergyEdge>, List<EnergyEdge>>();
        internal Stack<EnergyPathSearch.Workspace> WorkspacePool;
        internal BuildingState Building(BuildingState original) => original == null ? null : Buildings[original.Id];
        internal EnergyEdge Edge(EnergyEdge original)
        {
            if (original == null) return null;
            if (!Edges.TryGetValue(original, out EnergyEdge copy))
            {
                copy = new EnergyEdge(original, this);
                Edges.Add(original, copy);
            }
            return copy;
        }
        internal List<EnergyEdge> EdgeList(List<EnergyEdge> original)
        {
            if (original == null) return null;
            if (!EdgeLists.TryGetValue(original, out List<EnergyEdge> copy))
            {
                copy = new List<EnergyEdge>(); EdgeLists.Add(original, copy);
                foreach (EnergyEdge edge in original) copy.Add(Edge(edge));
            }
            return copy;
        }
        internal EnergyRoute Route(EnergyRoute original)
        {
            if (original == null) return null;
            if (!Routes.TryGetValue(original, out EnergyRoute copy))
            {
                EnergyEdge[] oldEdges = original.Edges;
                var edges = new EnergyEdge[oldEdges.Length];
                for (int index = 0; index < edges.Length; index++) edges[index] = Edge(oldEdges[index]);
                copy = new EnergyRoute(edges, original.Cost, original.ValidatedTopologyVersion)
                { ValidatedFirstEdge = original.ValidatedFirstEdge };
                Routes.Add(original, copy);
            }
            return copy;
        }
        internal EnergyPathSearch Search(EnergyPathSearch original)
        {
            if (original == null) return null;
            if (!Searches.TryGetValue(original, out EnergyPathSearch copy)) copy = new EnergyPathSearch(original, this);
            return copy;
        }
        internal EnergyBatch Batch(EnergyBatch original)
        {
            if (original == null) return null;
            if (Batches.TryGetValue(original, out EnergyBatch copy)) return copy;
            copy = new EnergyBatch
            {
                Id = original.Id, Amount = original.Amount, Location = Building(original.Location), History = original.History,
                Destination = Building(original.Destination), Commitment = original.Commitment, SourceServiceCharge = original.SourceServiceCharge,
                Route = Route(original.Route), RouteEdge = original.RouteEdge, StalledTicks = original.StalledTicks,
                IsStalled = original.IsStalled, BlocksIncomingTraffic = original.BlocksIncomingTraffic,
                LastStallTick = original.LastStallTick, PlanningDeferredTick = original.PlanningDeferredTick,
                EligibleTick = original.EligibleTick, InFlight = original.InFlight, Retired = original.Retired,
                FlightSourceId = original.FlightSourceId, FlightTargetId = original.FlightTargetId,
                TargetLifeGeneration = original.TargetLifeGeneration, TargetPositionRevision = original.TargetPositionRevision,
                SourceX = original.SourceX, SourceZ = original.SourceZ, TargetX = original.TargetX, TargetZ = original.TargetZ,
                StartTick = original.StartTick, ArrivalTick = original.ArrivalTick
            };
            foreach (EnergyRelayCharge charge in original.RelayServiceCharges)
                copy.RelayServiceCharges.Add(new EnergyRelayCharge(charge.BuildingId, charge.Amount));
            Batches.Add(original, copy); return copy;
        }
    }

    public sealed partial class EnergySystem
    {
        public EnergyCheckpoint CaptureCheckpoint()
        {
            var detachedBuildings = new List<BuildingState>(buildings.Count);
            foreach (BuildingState building in buildings) detachedBuildings.Add(building.CloneForCheckpoint());
            var copy = new EnergySystem(detachedBuildings, settings);
            CopyCheckpointTo(copy);
            return new EnergyCheckpoint(copy);
        }

        internal void RestoreCheckpoint(EnergyCheckpoint checkpoint)
        {
            if (checkpoint == null) throw new ArgumentNullException(nameof(checkpoint));
            checkpoint.State.CopyCheckpointTo(this);
        }

        private void CopyCheckpointTo(EnergySystem destination)
        {
            var copies = new EnergyCheckpointCopies();
            foreach (BuildingState building in destination.buildings) copies.Buildings.Add(building.Id, building);
            foreach (BuildingState building in buildings)
                if (!copies.Buildings.ContainsKey(building.Id)) throw new InvalidOperationException("能源恢复缺少对应建筑。");
            destination.lastTick = lastTick;
            destination.nodes.Clear(); destination.orderedNodes.Clear(); destination.consumers.Clear();
            foreach (EnergyNodeRuntime node in orderedNodes)
            {
                var copy = new EnergyNodeRuntime(copies.Building(node.Building), settings.BaseQuantum, node.InitialHistory)
                { NextOutputTick = node.NextOutputTick, LastOutputTick = node.LastOutputTick, AvailableOutput = node.AvailableOutput };
                copies.Nodes.Add(node, copy); destination.nodes.Add(copy.Building.Id, copy); destination.orderedNodes.Add(copy);
            }
            foreach (BuildingState consumer in consumers) destination.consumers.Add(copies.Building(consumer));
            destination.planner.CopyCheckpointFrom(planner, copies);
            destination.transport.CopyCheckpointFrom(transport, copies);
            foreach (EnergyNodeRuntime node in orderedNodes)
            {
                EnergyNodeRuntime copy = copies.Nodes[node];
                foreach (EnergyBatch batch in node.Batches) copy.Batches.Add(copies.Batch(batch));
                foreach (BuildingState consumer in node.ActiveOutputConsumers) copy.ActiveOutputConsumers.Add(copies.Building(consumer));
                foreach (long id in node.ActiveOutputConsumerIds) copy.ActiveOutputConsumerIds.Add(id);
                copy.OutputSchedule.CopyCheckpointFrom(node.OutputSchedule, copies);
            }
            destination.allocationSchedule.CopyCheckpointFrom(allocationSchedule, copies);
            destination.dispatch.CopyCheckpointFrom(dispatch, copies);
            destination.ledger.Generated = ledger.Generated; destination.ledger.Consumed = ledger.Consumed;
            destination.ledger.TransmissionLoss = ledger.TransmissionLoss; destination.ledger.BuildingLoss = ledger.BuildingLoss;
            destination.ledger.FailedHopLoss = ledger.FailedHopLoss; destination.ledger.ExpiredLoss = ledger.ExpiredLoss;
            destination.ledger.ParameterAdjustmentLoss = ledger.ParameterAdjustmentLoss;
            destination.checkedStored.Clear(); destination.checkedReservations.Clear(); destination.checkedCommitments.Clear(); destination.checkedHistory.Clear();
        }
    }

    internal sealed partial class WeightedEnergySchedule
    {
        internal void CopyCheckpointFrom(WeightedEnergySchedule source, EnergyCheckpointCopies copies)
        {
            accounts.Clear(); Cursor = source.Cursor; madeProgress = source.madeProgress;
            foreach (var pair in source.accounts)
                accounts.Add(pair.Key, new Account { Credit = pair.Value.Credit, Weight = pair.Value.Weight, Consumer = copies.Building(pair.Value.Consumer) });
        }
    }

    internal sealed partial class EnergyTransport
    {
        internal void CopyCheckpointFrom(EnergyTransport source, EnergyCheckpointCopies copies)
        {
            nextBatchId = source.nextBatchId;
            arrivals.Clear(); batches.Clear(); histories.Clear(); historyOrder.Clear();
            snapshots.Clear(); departures.Clear(); deliveries.Clear();
            foreach (EnergyBatch batch in source.batches) batches.Add(copies.Batch(batch));
            foreach (EnergyBatch batch in source.arrivals) arrivals.Add(copies.Batch(batch));
            foreach (var pair in source.histories) histories.Add(pair.Key, pair.Value);
            foreach (var entry in source.historyOrder) historyOrder.Enqueue(entry);
            UpdateSnapshots();
            // 运输记录保留；瞬时事件不重放上一轮表现。
        }
    }

    internal sealed partial class EnergyDispatch
    {
        internal void CopyCheckpointFrom(EnergyDispatch source, EnergyCheckpointCopies copies)
        {
            offerTopologyVersion = source.offerTopologyVersion; transactions = source.transactions;
            candidateChecks = source.candidateChecks; relayCursor = source.relayCursor;
            sources.Clear(); offerSearches.Clear();
            foreach (EnergyNodeRuntime node in source.sources) sources.Add(copies.Nodes[node]);
            foreach (var pair in source.offerSearches)
            {
                OfferSearch original = pair.Value;
                var copy = new OfferSearch { PendingCursor = original.PendingCursor, FirstAvailableOffer = original.FirstAvailableOffer,
                    LastResolutionTick = original.LastResolutionTick, LastSelectionTick = original.LastSelectionTick };
                foreach (EnergyNodeRuntime node in original.PendingSources) copy.PendingSources.Add(copies.Nodes[node]);
                foreach (Offer offer in original.Offers)
                    copy.Offers.Add(new Offer { Owner = copies.Nodes[offer.Owner], Route = copies.Route(offer.Route), Sent = offer.Sent, StoredBatch = copies.Batch(offer.StoredBatch) });
                offerSearches.Add(pair.Key, copy);
            }
            batteries.CopyCheckpointFrom(source.batteries);
        }
    }

    internal sealed partial class BatterySupplyPolicy
    {
        internal void CopyCheckpointFrom(BatterySupplyPolicy source)
        {
            supportedTerminals.Clear(); balancingPairs.Clear();
            foreach (long id in source.supportedTerminals) supportedTerminals.Add(id);
            foreach (var pair in source.balancingPairs) balancingPairs.Add(pair);
        }
    }

    internal sealed partial class EnergyEdge
    {
        internal EnergyEdge(EnergyEdge source, EnergyCheckpointCopies copies)
        {
            Source = copies.Building(source.Source); Target = copies.Building(source.Target);
            Efficiency = source.Efficiency; Cost = source.Cost; FlightTicks = source.FlightTicks;
            SourcePositionRevision = source.SourcePositionRevision; TargetPositionRevision = source.TargetPositionRevision;
            SourceLifeGeneration = source.SourceLifeGeneration; TargetLifeGeneration = source.TargetLifeGeneration;
        }
    }
}
