using System;
using System.Collections.Generic;

namespace Defend.Simulation
{
    public readonly struct EnergyDefinitionChangePreview
    {
        public int AffectedCount { get; }
        public long StoredRemoved { get; }
        public long InFlightRemoved { get; }
        internal EnergyDefinitionChangePreview(int affected, long stored, long inFlight)
        { AffectedCount = affected; StoredRemoved = stored; InFlightRemoved = inFlight; }
    }

    public sealed partial class EnergySystem
    {
        public EnergyDefinitionChangePreview PreviewDefinitionChange(BuildingDefinition candidate)
        {
            if (candidate == null) throw new ArgumentNullException(nameof(candidate));
            int affected = 0;
            long stored = 0, inFlight = 0;
            foreach (BuildingState building in buildings)
            {
                if (building.Kind != candidate.Kind || !building.IsAlive || building.IsSold) continue;
                affected++;
                stored += Math.Max(0, building.StoredEnergy - candidate.EnergyCapacity);
                long space = Math.Max(0, candidate.EnergyCapacity - building.StoredEnergy);
                foreach (EnergyBatch batch in transport.OrderedIncoming(building.Id))
                {
                    long retained = Math.Min(space, batch.Amount);
                    inFlight += batch.Amount - retained;
                    space -= retained;
                }
            }
            return new EnergyDefinitionChangePreview(affected, stored, inFlight);
        }

        /// <summary>只能由世界准备阶段事务调用；不推进时间，也不使用节点摧毁流程。</summary>
        internal EnergyDefinitionChangePreview ApplyDefinitionChange(BuildingDefinition candidate, long tick)
        {
            EnergyDefinitionChangePreview result = PreviewDefinitionChange(candidate);
            foreach (BuildingState building in buildings)
            {
                if (building.Kind != candidate.Kind) continue;
                BuildingDefinition previous = building.Definition;
                building.Definition = candidate;
                if (!building.IsAlive || building.IsSold) continue;
                building.Health = Math.Max(1, Math.Min(candidate.MaximumHealth,
                    (int)(((long)building.Health * candidate.MaximumHealth + previous.MaximumHealth - 1) / previous.MaximumHealth)));
                transport.AdjustCapacity(building);
                if (nodes.TryGetValue(building.Id, out EnergyNodeRuntime node) &&
                    previous.OutputIntervalTicks != candidate.OutputIntervalTicks)
                {
                    node.NextOutputTick = Math.Max(tick + 1, node.LastOutputTick < 0 ? tick + 1 : node.LastOutputTick + candidate.OutputIntervalTicks);
                    node.AvailableOutput = 0;
                    foreach (EnergyBatch batch in node.Batches)
                        batch.EligibleTick = Math.Max(tick + 1, node.NextOutputTick);
                }
            }
            transport.RemoveRetired();
            transport.UpdateSnapshots();
            planner.Refresh(buildings);
            planner.RefreshCongestion(transport.Batches);
            return result;
        }
    }

    internal sealed partial class EnergyTransport
    {
        internal List<EnergyBatch> OrderedIncoming(long buildingId)
        {
            var result = new List<EnergyBatch>();
            foreach (EnergyBatch batch in arrivals)
                if (!batch.Retired && batch.FlightTargetId == buildingId) result.Add(batch);
            return result;
        }

        internal void AdjustCapacity(BuildingState building)
        {
            long capacity = building.Definition.EnergyCapacity;
            if (building.Kind == BuildingKind.Relay || building.Kind == BuildingKind.Battery)
            {
                long remaining = capacity;
                // 节点列表按实际入库顺序排列，边界批次保留身份、历史与滞留状态。
                if (nodes.TryGetValue(building.Id, out EnergyNodeRuntime node))
                    foreach (EnergyBatch batch in new List<EnergyBatch>(node.Batches))
                    {
                        long retained = Math.Min(remaining, batch.Amount);
                        TrimForParameterChange(batch, retained);
                        remaining -= retained;
                    }
            }
            else if (building.StoredEnergy > capacity)
            {
                ledger.ParameterAdjustmentLoss += building.StoredEnergy - capacity;
                building.StoredEnergy = capacity;
            }
            long available = Math.Max(0, capacity - building.StoredEnergy);
            foreach (EnergyBatch batch in OrderedIncoming(building.Id))
            {
                long retained = Math.Min(available, batch.Amount);
                TrimForParameterChange(batch, retained);
                available -= retained;
            }
            if (!building.IsEnergyDestination) return;
            // 直接入站优先。尚未进入该目标的批次只解除过额承诺，真实能量留在当前位置或当前一跳。
            foreach (EnergyBatch batch in batches)
            {
                if (batch.Retired || batch.Destination != building || (batch.InFlight && batch.FlightTargetId == building.Id)) continue;
                if (batch.Commitment <= available) available -= batch.Commitment;
                else
                {
                    RefundRelayService(batch, batch.Commitment);
                    ReleaseCommitment(batch);
                }
            }
        }

        private void RefundRelayService(EnergyBatch batch, long amount)
        {
            if (amount <= 0 || batch.Destination == null) return;
            // 只返还本次承诺实际扣过的出口额度。历史访问节点可能属于已结束的旧目标。
            foreach (EnergyRelayCharge charge in batch.RelayServiceCharges)
            {
                long released = Math.Min(charge.Amount, amount);
                if (released > 0 && nodes.TryGetValue(charge.BuildingId, out EnergyNodeRuntime sender))
                    sender.OutputSchedule.Refund(batch.Destination, released);
                charge.Amount -= released;
            }
        }

        private void TrimForParameterChange(EnergyBatch batch, long retained)
        {
            long removed = batch.Amount - retained;
            if (removed <= 0) return;
            if (batch.Destination != null)
            {
                long commitment = retained == 0 ? 0 : EnergyArithmetic.Predict(retained, batch.Route, batch.RouteEdge + (batch.InFlight ? 1 : 0));
                long released = batch.Commitment - commitment;
                batch.Destination.CommittedIncoming -= released;
                if (batch.SourceServiceCharge > 0)
                {
                    allocationSchedule.Refund(batch.Destination, batch.SourceServiceCharge - commitment);
                    batch.SourceServiceCharge = commitment;
                }
                RefundRelayService(batch, released);
                batch.Commitment = commitment;
            }
            if (batch.InFlight)
            {
                BuildingState target = nodes[batch.FlightTargetId].Building;
                if (!target.IsEnergyDestination) target.ReservedIncoming -= removed;
            }
            else batch.Location.StoredEnergy -= removed;
            batch.Amount = retained;
            ledger.ParameterAdjustmentLoss = checked(ledger.ParameterAdjustmentLoss + removed);
            if (retained != 0)
            {
                if (batch.Commitment == 0 && batch.Destination != null) ReleaseCommitment(batch);
                return;
            }
            arrivals.Remove(batch);
            if (batch.Location != null) nodes[batch.Location.Id].Batches.Remove(batch);
            batch.Destination = null;
            batch.Route = null;
            batch.SourceServiceCharge = 0;
            Retire(batch);
        }
    }
}
