using System;
using System.Collections.Generic;

namespace Defend.Simulation
{
    /// <summary>
    /// 批次运输事务的唯一写入口。一次发送同时转移实际所有权、登记本跳空间和创建到货事件。
    /// 所有调用都发生在单线程逻辑步中，外部动画不参与这里的结算。
    /// </summary>
    internal sealed class EnergyTransport
    {
        private readonly Dictionary<long, EnergyNodeRuntime> nodes;
        private readonly EnergyLedger ledger;
        private readonly WeightedEnergySchedule allocationSchedule;
        private readonly EnergyRoutePlanner planner;
        private readonly SortedSet<EnergyBatch> arrivals = new SortedSet<EnergyBatch>(new EnergyArrivalComparer());
        private readonly List<EnergyBatch> batches = new List<EnergyBatch>();
        private readonly List<EnergyTransferSnapshot> snapshots = new List<EnergyTransferSnapshot>();
        private readonly Dictionary<Tuple<EnergyHistory, long>, EnergyHistory> histories =
            new Dictionary<Tuple<EnergyHistory, long>, EnergyHistory>();
        private readonly Queue<Tuple<EnergyHistory, long>> historyOrder = new Queue<Tuple<EnergyHistory, long>>();
        private long nextBatchId;
        public IReadOnlyList<EnergyBatch> Batches => batches;
        public IReadOnlyList<EnergyTransferSnapshot> Snapshots { get; }
        internal Action<BuildingState, BuildingState, long, long> TransferCompleted;
        internal Action<BuildingState, long, IReadOnlyList<long>, long> TerminalDeliveryCompleted;
        private readonly List<EnergyTransferSnapshot> departures = new List<EnergyTransferSnapshot>();
        private readonly List<EnergyTransferSnapshot> deliveries = new List<EnergyTransferSnapshot>();
        public IReadOnlyList<EnergyTransferSnapshot> Departures { get; }
        public IReadOnlyList<EnergyTransferSnapshot> Deliveries { get; }
        public void BeginStep() { departures.Clear(); deliveries.Clear(); }

        public EnergyTransport(Dictionary<long, EnergyNodeRuntime> nodes, EnergyLedger ledger, WeightedEnergySchedule allocationSchedule,
            EnergyRoutePlanner planner)
        {
            this.nodes = nodes;
            this.ledger = ledger;
            this.allocationSchedule = allocationSchedule;
            this.planner = planner;
            Snapshots = snapshots.AsReadOnly();
            Departures = departures.AsReadOnly();
            Deliveries = deliveries.AsReadOnly();
        }

        public void AssignAndSend(EnergyNodeRuntime owner, EnergyBatch previous, BuildingState destination,
            EnergyRoute route, long sent, long tick)
        {
            EnergyBatch travelling;
            if (previous != null && previous.Amount == sent)
            {
                travelling = previous;
                owner.Batches.Remove(previous);
            }
            else
            {
                travelling = new EnergyBatch
                {
                    Id = ++nextBatchId,
                    History = previous == null ? owner.InitialHistory : previous.History,
                    StalledTicks = previous == null ? 0 : previous.StalledTicks
                };
                batches.Add(travelling);
                if (previous != null)
                {
                    previous.Amount -= sent;
                    if (EnergyArithmetic.Predict(previous.Amount, route) == 0) previous.BlocksIncomingTraffic = false;
                }
            }
            travelling.Amount = sent;
            travelling.Location = owner.Building;
            travelling.Destination = destination;
            travelling.Route = route;
            travelling.RouteEdge = 0;
            travelling.Commitment = EnergyArithmetic.Predict(sent, route);
            destination.CommittedIncoming = checked(destination.CommittedIncoming + travelling.Commitment);
            travelling.SourceServiceCharge = (owner.Building.Kind == BuildingKind.Reactor || owner.Building.Kind == BuildingKind.Battery) ? travelling.Commitment : 0;
            if (travelling.SourceServiceCharge > 0) allocationSchedule.Spend(destination, travelling.SourceServiceCharge);
            // 旧批次换目标后仍占用同一个中继出口，不能绕过该出口的有效量份额。
            if (owner.Building.Kind == BuildingKind.Relay)
                owner.OutputSchedule.Spend(destination, travelling.Commitment);
            Depart(owner, travelling, route.Edges[0], sent, tick);
        }

        public void Forward(EnergyNodeRuntime owner, EnergyBatch previous, long sent, long tick)
        {
            EnergyBatch travelling = previous;
            long childCommitment = EnergyArithmetic.Predict(sent, previous.Route, previous.RouteEdge);
            if (sent < previous.Amount)
            {
                long retainedAmount = previous.Amount - sent;
                long retainedCommitment = EnergyArithmetic.Predict(retainedAmount, previous.Route, previous.RouteEdge);
                long released = previous.Commitment - childCommitment - retainedCommitment;
                previous.Destination.CommittedIncoming -= released;
                bool chargedAtSource = previous.SourceServiceCharge > 0;
                if (chargedAtSource) allocationSchedule.Refund(previous.Destination, released);
                previous.Amount = retainedAmount;
                previous.Commitment = retainedCommitment;
                previous.SourceServiceCharge = chargedAtSource ? retainedCommitment : 0;
                travelling = new EnergyBatch
                {
                    Id = ++nextBatchId,
                    Amount = sent,
                    Location = previous.Location,
                    History = previous.History,
                    StalledTicks = previous.StalledTicks,
                    Destination = previous.Destination,
                    Route = previous.Route,
                    RouteEdge = previous.RouteEdge,
                    Commitment = childCommitment
                };
                travelling.SourceServiceCharge = chargedAtSource ? childCommitment : 0;
                batches.Add(travelling);
                // 取整后无法再贡献有效到货的残片不保留空承诺，留待改投其他目标或超时。
                if (retainedCommitment == 0)
                {
                    ReleaseCommitment(previous);
                    previous.BlocksIncomingTraffic = false;
                }
            }
            else owner.Batches.Remove(previous);
            owner.OutputSchedule.Spend(travelling.Destination, childCommitment);
            Depart(owner, travelling, travelling.Route.Edges[travelling.RouteEdge], sent, tick);
        }

        private void Depart(EnergyNodeRuntime owner, EnergyBatch batch, EnergyEdge edge, long sent, long tick)
        {
            if (batch.History.Contains(edge.Target.Id))
                throw new InvalidOperationException($"批次 {batch.Id} 尝试回流至建筑 {edge.Target.Id}。");
            long received = EnergyArithmetic.Receive(sent, edge.Efficiency);
            if (received <= 0) throw new InvalidOperationException("不应创建有效量为零的运输批次。");
            owner.Building.StoredEnergy -= sent;
            owner.Building.TotalDischarged = checked(owner.Building.TotalDischarged + sent);
            owner.AvailableOutput -= sent;
            owner.LastOutputTick = tick;
            owner.NextOutputTick = checked(tick + Math.Max(1, owner.Building.Definition.OutputIntervalTicks));
            ledger.TransmissionLoss = checked(ledger.TransmissionLoss + sent - received);
            if (!edge.Target.IsEnergyDestination) edge.Target.ReservedIncoming = checked(edge.Target.ReservedIncoming + received);
            planner.RefreshAvailability(owner.Building, edge.Target);
            batch.Amount = received;
            batch.IsStalled = false;
            batch.BlocksIncomingTraffic = false;
            batch.InFlight = true;
            batch.Location = null;
            batch.FlightSourceId = edge.Source.Id;
            batch.FlightTargetId = edge.Target.Id;
            batch.TargetLifeGeneration = edge.Target.LifeGeneration;
            batch.TargetPositionRevision = edge.Target.PositionRevision;
            batch.SourceX = edge.Source.CenterX;
            batch.SourceZ = edge.Source.CenterZ;
            batch.TargetX = edge.Target.CenterX;
            batch.TargetZ = edge.Target.CenterZ;
            batch.StartTick = tick;
            batch.ArrivalTick = checked(tick + edge.FlightTicks);
            arrivals.Add(batch);
            departures.Add(new EnergyTransferSnapshot(batch));
        }

        public void CompleteArrivals(long tick)
        {
            while (arrivals.Count > 0 && arrivals.Min.ArrivalTick <= tick)
            {
                EnergyBatch batch = arrivals.Min;
                arrivals.Remove(batch);
                if (!nodes.TryGetValue(batch.FlightTargetId, out EnergyNodeRuntime receiver) ||
                    !receiver.Building.IsAlive || receiver.Building.IsSold ||
                    receiver.Building.LifeGeneration != batch.TargetLifeGeneration ||
                    receiver.Building.PositionRevision != batch.TargetPositionRevision)
                {
                    LoseBatch(batch, false);
                    continue;
                }
                BuildingState target = receiver.Building;
                if (!target.IsEnergyDestination) target.ReservedIncoming -= batch.Amount;
                target.StoredEnergy = checked(target.StoredEnergy + batch.Amount);
                target.TotalReceived = checked(target.TotalReceived + batch.Amount);
                deliveries.Add(new EnergyTransferSnapshot(batch));
                if (TransferCompleted != null && nodes.TryGetValue(batch.FlightSourceId, out EnergyNodeRuntime sender) &&
                    sender.Building.CenterX == batch.SourceX && sender.Building.CenterZ == batch.SourceZ)
                    TransferCompleted(sender.Building, target, batch.Amount, tick);
                if (target.IsEnergyDestination)
                {
                    if (batch.Destination != target || batch.Commitment != batch.Amount)
                        throw new InvalidOperationException($"批次 {batch.Id} 的终端到货与承诺不一致。");
                    target.CommittedIncoming -= batch.Commitment;
                    batch.Commitment = 0;
                    batch.SourceServiceCharge = 0;
                    batch.Destination = null;
                    if (target.Kind == BuildingKind.Battery)
                    {
                        // 充电任务在此结束，但能量没有消失；完整历史和累计滞留随批次进入库存。
                        batch.History = AppendHistory(batch.History, target.Id);
                        batch.InFlight = false;
                        batch.Location = target;
                        batch.Route = null;
                        batch.RouteEdge = 0;
                        batch.IsStalled = false;
                        batch.BlocksIncomingTraffic = false;
                        batch.EligibleTick = Math.Max(tick + 1, receiver.NextOutputTick);
                        receiver.Batches.Add(batch);
                    }
                    else
                    {
                        if (TerminalDeliveryCompleted != null)
                        {
                            var history = new List<long>();
                            for (EnergyHistory entry = batch.History; entry != null; entry = entry.Previous) history.Add(entry.BuildingId);
                            history.Reverse();
                            TerminalDeliveryCompleted(target, batch.Amount, history.AsReadOnly(), tick);
                        }
                        Retire(batch);
                    }
                }
                else
                {
                    if (batch.History.Contains(target.Id))
                        throw new InvalidOperationException($"批次 {batch.Id} 重复访问建筑 {target.Id}。");
                    batch.History = AppendHistory(batch.History, target.Id);
                    batch.InFlight = false;
                    batch.Location = target;
                    batch.RouteEdge++;
                    // 首次等待输出要尊重中继既有节拍；其他批次之后抢走机会不能反复推迟此截止时间。
                    batch.EligibleTick = Math.Max(tick, receiver.NextOutputTick);
                    receiver.Batches.Add(batch);
                }
            }
        }

        /// <summary>
        /// 最后一跳出发时，为预计到货的逻辑步保留充电吞吐。已有任务承诺已计入容量，
        /// 这里仅限制到货速率，不能再把同一承诺重复减去一次。
        /// </summary>
        public long AvailableChargingSpace(BuildingState battery, long arrivalTick)
        {
            long remaining = battery.Definition.MaximumChargePerTick;
            foreach (EnergyBatch pending in arrivals)
                if (pending.FlightTargetId == battery.Id && pending.ArrivalTick == arrivalTick) remaining -= pending.Amount;
            return Math.Max(0, remaining);
        }

        private EnergyHistory AppendHistory(EnergyHistory previous, long buildingId)
        {
            // 相同实际访问前缀共享不可变对象，使受约束寻路可以复用结果。
            // 容量只限制复用索引；淘汰索引绝不修改批次持有的完整历史或滞留预算。
            var key = Tuple.Create(previous, buildingId);
            if (histories.TryGetValue(key, out EnergyHistory history)) return history;
            history = new EnergyHistory(buildingId, previous);
            histories.Add(key, history);
            historyOrder.Enqueue(key);
            if (historyOrder.Count > 4096) histories.Remove(historyOrder.Dequeue());
            return history;
        }

        public void ReleaseCommitment(EnergyBatch batch)
        {
            if (batch.Destination != null)
            {
                batch.Destination.CommittedIncoming -= batch.Commitment;
                if (batch.SourceServiceCharge > 0) allocationSchedule.Refund(batch.Destination, batch.SourceServiceCharge);
            }
            batch.Commitment = 0;
            batch.SourceServiceCharge = 0;
            batch.Destination = null;
            batch.Route = null;
            batch.RouteEdge = 0;
        }

        public void BeforeBuildingChanged(BuildingState building, bool loseStoredEnergy)
        {
            // 接收端失效优先结算；发送端在能量离开后已不再拥有该批次。
            for (int index = batches.Count - 1; index >= 0; index--)
            {
                EnergyBatch batch = batches[index];
                if (batch.Retired) continue;
                if (batch.InFlight && batch.FlightTargetId == building.Id)
                { LoseBatch(batch, false); continue; }
                if (!batch.InFlight && batch.Location.Id == building.Id && loseStoredEnergy)
                { LoseBatch(batch, false); continue; }
                if (batch.Destination == building || RemainingRouteTouches(batch, building.Id)) ReleaseCommitment(batch);
            }
            if (loseStoredEnergy && building.StoredEnergy > 0)
            {
                ledger.BuildingLoss = checked(ledger.BuildingLoss + building.StoredEnergy);
                building.StoredEnergy = 0;
            }
            RemoveRetired();
            UpdateSnapshots();
        }

        private static bool RemainingRouteTouches(EnergyBatch batch, long buildingId)
        {
            if (batch.Route == null) return false;
            int first = batch.RouteEdge + (batch.InFlight ? 1 : 0);
            for (int index = first; index < batch.Route.Edges.Length; index++)
                if (batch.Route.Edges[index].Source.Id == buildingId || batch.Route.Edges[index].Target.Id == buildingId) return true;
            return false;
        }

        public void LoseBatch(EnergyBatch batch, bool expired)
        {
            if (batch.Retired) return;
            ReleaseCommitment(batch);
            if (batch.InFlight)
            {
                arrivals.Remove(batch);
                if (nodes.TryGetValue(batch.FlightTargetId, out EnergyNodeRuntime receiver) && !receiver.Building.IsEnergyDestination)
                    receiver.Building.ReservedIncoming -= batch.Amount;
                ledger.FailedHopLoss = checked(ledger.FailedHopLoss + batch.Amount);
            }
            else
            {
                EnergyNodeRuntime owner = nodes[batch.Location.Id];
                owner.Building.StoredEnergy -= batch.Amount;
                owner.Batches.Remove(batch);
                if (expired) ledger.ExpiredLoss = checked(ledger.ExpiredLoss + batch.Amount);
                else ledger.BuildingLoss = checked(ledger.BuildingLoss + batch.Amount);
            }
            Retire(batch);
        }

        private static void Retire(EnergyBatch batch)
        {
            batch.Retired = true;
            batch.InFlight = false;
            batch.Amount = 0;
        }

        public void RemoveRetired()
        {
            for (int index = batches.Count - 1; index >= 0; index--)
                if (batches[index].Retired) batches.RemoveAt(index);
        }

        public void UpdateSnapshots()
        {
            snapshots.Clear();
            foreach (EnergyBatch batch in arrivals) snapshots.Add(new EnergyTransferSnapshot(batch));
        }
    }
}
