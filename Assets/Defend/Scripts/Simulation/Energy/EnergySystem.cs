using System;
using System.Collections.Generic;

namespace Defend.Simulation
{
    /// <summary>
    /// 独立于波次与 Unity 对象的能源模拟入口。图、调度、运输事务和账本各自负责一种规则。
    /// 白盒阶段逐逻辑步遍历活跃批次；路径和事件使用共享集合，规模上限仍需性能测试确认。
    /// </summary>
    public sealed partial class EnergySystem
    {
        private readonly IReadOnlyList<BuildingState> buildings;
        private readonly EnergySettings settings;
        private readonly Dictionary<long, EnergyNodeRuntime> nodes = new Dictionary<long, EnergyNodeRuntime>();
        private readonly List<EnergyNodeRuntime> orderedNodes = new List<EnergyNodeRuntime>();
        private readonly List<BuildingState> consumers = new List<BuildingState>();
        private readonly EnergyLedger ledger = new EnergyLedger();
        private readonly EnergyRoutePlanner planner;
        private readonly EnergyTransport transport;
        private readonly EnergyDispatch dispatch;
        private readonly WeightedEnergySchedule allocationSchedule;
        private readonly Dictionary<long, long> checkedStored = new Dictionary<long, long>();
        private readonly Dictionary<long, long> checkedReservations = new Dictionary<long, long>();
        private readonly Dictionary<long, long> checkedCommitments = new Dictionary<long, long>();
        private readonly HashSet<long> checkedHistory = new HashSet<long>();
        private long lastTick;

        public IReadOnlyList<EnergyConnection> Connections => planner.Connections;
        public IReadOnlyList<EnergyTransferSnapshot> Transfers => transport.Snapshots;
        public IReadOnlyList<EnergyTransferSnapshot> Departures => transport.Departures;
        public IReadOnlyList<EnergyTransferSnapshot> Deliveries => transport.Deliveries;
        public int ActiveBatchCount => transport.Batches.Count;
        public EnergyPlanningStatistics PlanningStatistics => planner.Statistics;
        /// <summary>供表现层查询某个输出建筑到地图位置的一跳传输效率。</summary>
        public double DirectEfficiencyAt(BuildingState source, double targetX, double targetZ)
        {
            if (source == null || !source.CanOutput) return 0;
            return DirectEfficiencyAt(source.Definition, source.CenterX, source.CenterZ, targetX, targetZ);
        }

        /// <summary>查询候选位置的一跳效率，不创建建筑或改变真实运输网络。</summary>
        public double DirectEfficiencyAt(BuildingDefinition definition, double sourceX, double sourceZ, double targetX, double targetZ)
        {
            if (definition == null || (definition.Kind != BuildingKind.Reactor &&
                definition.Kind != BuildingKind.Relay && definition.Kind != BuildingKind.Battery)) return 0;
            double range = definition.TransmissionRange;
            if (range <= 0) return 0;
            double horizontal = targetX - sourceX;
            double vertical = targetZ - sourceZ;
            double distance = Math.Sqrt(horizontal * horizontal + vertical * vertical);
            if (distance > range) return 0;
            long efficiency = EnergyRoutePlanner.EfficiencyAt(distance, range, settings.MaximumLossAtRange);
            return efficiency <= 0 ? 0 : (double)efficiency / EnergyArithmetic.EfficiencyScale;
        }
        internal Action<BuildingState, BuildingState, long, long> TransferCompleted
        {
            set { transport.TransferCompleted = value; }
        }
        /// <summary>真实终端到货及完整访问链，供现场供能收益记录读取；电池充电不算终端消费。</summary>
        internal Action<BuildingState, long, IReadOnlyList<long>, long> TerminalDeliveryCompleted
        {
            set { transport.TerminalDeliveryCompleted = value; }
        }
        public bool HasStalledEnergy(long buildingId)
        {
            foreach (var batch in transport.Batches)
                if (!batch.Retired && !batch.InFlight && batch.IsStalled && batch.BlocksIncomingTraffic && batch.Location?.Id == buildingId) return true;
            return false;
        }
        /// <summary>按需生成独立调试快照，适合测试和检查器，不建议每帧读取。</summary>
        public IReadOnlyList<EnergyBatchDiagnostic> BatchDiagnostics
        {
            get
            {
                var result = new List<EnergyBatchDiagnostic>(transport.Batches.Count);
                for (int index = 0; index < transport.Batches.Count; index++)
                    result.Add(new EnergyBatchDiagnostic(transport.Batches[index]));
                return result.AsReadOnly();
            }
        }

        public EnergySystem(IReadOnlyList<BuildingState> buildings, EnergySettings settings)
        {
            this.buildings = buildings ?? throw new ArgumentNullException(nameof(buildings));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            allocationSchedule = new WeightedEnergySchedule(settings.BaseQuantum);
            planner = new EnergyRoutePlanner(settings);
            transport = new EnergyTransport(nodes, ledger, allocationSchedule, planner);
            dispatch = new EnergyDispatch(orderedNodes, consumers, planner, transport, allocationSchedule, settings);
        }

        public void Step(long tick)
        {
            if (tick <= lastTick) throw new ArgumentOutOfRangeException(nameof(tick), "能源逻辑步必须严格递增。");
            lastTick = tick;
            transport.BeginStep();
            RefreshNodes(tick);
            SettleElapsedStalls(tick);
            planner.Refresh(buildings);
            transport.CompleteArrivals(tick);
            planner.RefreshCongestion(transport.Batches);
            planner.BeginStep(tick);
            ProduceEnergy();
            bool deferredByWorkBudget = dispatch.Run(tick);
            UpdateWaiting(tick, deferredByWorkBudget);
            transport.RemoveRetired();
            transport.UpdateSnapshots();
            UpdateStatuses(tick, deferredByWorkBudget);
        }

        public void BeforeBuildingChanged(BuildingState building, bool loseStoredEnergy)
        {
            if (building == null) throw new ArgumentNullException(nameof(building));
            transport.BeforeBuildingChanged(building, loseStoredEnergy);
            if (loseStoredEnergy) allocationSchedule.Forget(building.Id);
        }

        public bool TryConsume(long buildingId, long amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            if (!nodes.TryGetValue(buildingId, out EnergyNodeRuntime node) || !node.Building.IsAlive ||
                !node.Building.IsConsumer || node.Building.StoredEnergy < amount) return false;
            if (amount > 0) Consume(node.Building, amount);
            return true;
        }

        public long ConsumeUpTo(long buildingId, long amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            if (!nodes.TryGetValue(buildingId, out EnergyNodeRuntime node) || !node.Building.IsAlive || !node.Building.IsConsumer) return 0;
            long consumed = Math.Min(amount, node.Building.StoredEnergy);
            Consume(node.Building, consumed);
            return consumed;
        }

        private void Consume(BuildingState building, long amount)
        {
            building.StoredEnergy -= amount;
            ledger.Consumed = checked(ledger.Consumed + amount);
        }

        private void RefreshNodes(long tick)
        {
            consumers.Clear();
            for (int index = 0; index < buildings.Count; index++)
            {
                BuildingState building = buildings[index];
                if (!nodes.TryGetValue(building.Id, out EnergyNodeRuntime node))
                {
                    if (building.Definition.EnergyCapacity > EnergyArithmetic.MaximumAmount)
                        throw new ArgumentOutOfRangeException(nameof(buildings), "建筑容量超过定点计算的安全范围。");
                    node = new EnergyNodeRuntime(building, settings.BaseQuantum);
                    nodes.Add(building.Id, node);
                    orderedNodes.Add(node);
                }
                node.AvailableOutput = building.CanOutput && tick >= node.NextOutputTick ? building.Definition.OutputAmount : 0;
                if (building.IsAlive && !building.IsSold && building.IsEnergyDestination) consumers.Add(building);
            }
        }

        private void ProduceEnergy()
        {
            for (int index = 0; index < orderedNodes.Count; index++)
            {
                BuildingState building = orderedNodes[index].Building;
                if (!building.IsAlive || building.IsSold || building.Kind != BuildingKind.Reactor) continue;
                long produced = Math.Min(building.Definition.ProductionPerTick, building.Definition.EnergyCapacity - building.StoredEnergy);
                building.StoredEnergy = checked(building.StoredEnergy + produced);
                ledger.Generated = checked(ledger.Generated + produced);
            }
        }

        private void UpdateWaiting(long tick, bool deferredByWorkBudget)
        {
            // 工程处理预算不足不能伪装成玩法故障，不能为此消耗玩家能量的等待时限。
            if (deferredByWorkBudget) return;
            IReadOnlyList<EnergyBatch> batches = transport.Batches;
            for (int index = 0; index < batches.Count; index++)
            {
                EnergyBatch batch = batches[index];
                if (batch.Retired || batch.InFlight || batch.IsStalled || batch.PlanningDeferredTick == tick) continue;
                if (batch.Location.Kind == BuildingKind.Battery && batch.Destination == null) continue;
                if (!batch.IsStalled && tick < batch.EligibleTick && HasPotentialDestination(batch)) continue;
                if (batch.PlanningDeferredTick == tick) continue;
                // 自身历史无路或取整后不足以产生有效到货的残片，只等待自身超时，不能封锁整栋中继。
                batch.BlocksIncomingTraffic = batch.Destination != null && batch.Commitment > 0;
                batch.IsStalled = true;
                batch.LastStallTick = tick;
                transport.ReleaseCommitment(batch);
            }
        }

        private void SettleElapsedStalls(long tick)
        {
            // 先结清上一时间区间再尝试恢复线路，不能在到期这一刻发货来抹去已经发生的等待。
            IReadOnlyList<EnergyBatch> batches = transport.Batches;
            for (int index = 0; index < batches.Count; index++)
            {
                EnergyBatch batch = batches[index];
                if (batch.Retired || batch.InFlight || !batch.IsStalled) continue;
                long elapsed = tick - batch.LastStallTick;
                long accumulated = batch.StalledTicks + elapsed;
                batch.StalledTicks = (int)Math.Min(settings.StallTimeoutTicks, accumulated);
                batch.LastStallTick = tick;
                if (batch.StalledTicks >= settings.StallTimeoutTicks) transport.LoseBatch(batch, true);
            }
        }

        private bool HasPotentialDestination(EnergyBatch batch)
        {
            if (batch.Destination != null) return true;
            for (int index = 0; index < consumers.Count; index++)
            {
                if (EnergyDispatch.Demand(consumers[index]) <= 0) continue;
                EnergyRoute route = planner.Find(batch.Location, consumers[index], batch.History, true);
                if (planner.LastQueryDeferred) batch.PlanningDeferredTick = lastTick;
                if (route != null && EnergyArithmetic.Predict(batch.Amount, route) > 0) return true;
            }
            return false;
        }

        private void UpdateStatuses(long tick, bool deferredByWorkBudget)
        {
            for (int index = 0; index < orderedNodes.Count; index++)
            {
                EnergyNodeRuntime node = orderedNodes[index];
                BuildingState building = node.Building;
                if (building.IsSold) building.EnergyStatus = "已出售";
                else if (!building.IsAlive) building.EnergyStatus = "已损坏";
                else if (building.Kind == BuildingKind.ResonanceMagneticTower && !building.Definition.Resonance.RequiresEnergy)
                    building.EnergyStatus = "由共振驱动";
                else if (building.Kind == BuildingKind.Battery)
                    building.EnergyStatus = building.ReceivedLastSecond > 0 && building.DischargedLastSecond > 0 ? "充放电中" :
                        building.DischargedLastSecond > 0 ? "放电中" : building.ReceivedLastSecond > 0 ? "充电中" :
                        building.CommittedIncoming > 0 ? "充电在途" : building.StoredEnergy >= building.Definition.EnergyCapacity ? "储能已满" : "储能待命";
                else if (building.IsConsumer)
                    building.EnergyStatus = building.StoredEnergy >= building.Definition.EnergyCapacity ? "缓存已满" :
                        building.CommittedIncoming > 0 ? "供给在途" : "等待供给";
                else if (deferredByWorkBudget) building.EnergyStatus = "调度任务排队";
                else if (node.LastOutputTick == tick) building.EnergyStatus = "正在输出";
                else if (building.StoredEnergy <= 0) building.EnergyStatus = building.Kind == BuildingKind.Reactor ? "等待发电" : "等待输入";
                else if (tick < node.NextOutputTick) building.EnergyStatus = "输出间隔";
                else building.EnergyStatus = building.Kind == BuildingKind.Reactor ? "库存等待需求" : "等待可用路线或接收空间";
            }
        }

        public EnergyTotals GetTotals()
        {
            long stored = 0;
            long inFlight = 0;
            for (int index = 0; index < buildings.Count; index++) stored = checked(stored + buildings[index].StoredEnergy);
            for (int index = 0; index < transport.Batches.Count; index++)
                if (transport.Batches[index].InFlight) inFlight = checked(inFlight + transport.Batches[index].Amount);
            return new EnergyTotals(ledger.Generated, ledger.Consumed, ledger.TransmissionLoss,
                checked(ledger.BuildingLoss + ledger.FailedHopLoss), ledger.ExpiredLoss, inFlight, stored, ActiveBatchCount, ledger.ParameterAdjustmentLoss);
        }

        public void AssertInvariants()
        {
            EnergyTotals totals = GetTotals();
            if (totals.BalanceError != 0) Fail($"守恒差额为 {totals.BalanceError}。");
            checkedStored.Clear();
            checkedReservations.Clear();
            checkedCommitments.Clear();
            for (int index = 0; index < transport.Batches.Count; index++)
            {
                EnergyBatch batch = transport.Batches[index];
                if (batch.Amount <= 0 || batch.Retired) Fail($"活动批次 {batch.Id} 的数量或生命周期无效。");
                checkedHistory.Clear();
                for (EnergyHistory entry = batch.History; entry != null; entry = entry.Previous)
                    if (!checkedHistory.Add(entry.BuildingId)) Fail($"批次 {batch.Id} 历史重复经过建筑 {entry.BuildingId}。");
                if (batch.InFlight)
                {
                    if (checkedHistory.Contains(batch.FlightTargetId)) Fail($"批次 {batch.Id} 正在回流。");
                    if (batch.ArrivalTick <= batch.StartTick) Fail($"批次 {batch.Id} 没有正向运输时间。");
                    if (!nodes[batch.FlightTargetId].Building.IsEnergyDestination) Add(checkedReservations, batch.FlightTargetId, batch.Amount);
                }
                else
                {
                    if (batch.Location == null || !batch.Location.IsAlive || batch.History.BuildingId != batch.Location.Id)
                        Fail($"批次 {batch.Id} 的实际持有位置无效。");
                    Add(checkedStored, batch.Location.Id, batch.Amount);
                }
                if (batch.Destination != null)
                {
                    int firstEdge = batch.RouteEdge + (batch.InFlight ? 1 : 0);
                    long predicted = EnergyArithmetic.Predict(batch.Amount, batch.Route, firstEdge);
                    if (predicted != batch.Commitment) Fail($"批次 {batch.Id} 承诺与逐跳有效量不符。");
                    Add(checkedCommitments, batch.Destination.Id, batch.Commitment);
                }
                else if (batch.Commitment != 0) Fail($"批次 {batch.Id} 没有目标但仍占用承诺。");
            }
            for (int index = 0; index < buildings.Count; index++)
            {
                BuildingState building = buildings[index];
                if (building.StoredEnergy < 0 || building.ReservedIncoming < 0 || building.CommittedIncoming < 0)
                    Fail($"建筑 {building.Id} 有负数账目。");
                long occupied = checked(building.StoredEnergy + (building.IsEnergyDestination ? building.CommittedIncoming : building.ReservedIncoming));
                if (occupied > building.Definition.EnergyCapacity) Fail($"建筑 {building.Id} 储量和预约超过容量。");
                if ((!building.IsAlive || building.IsSold) && (building.StoredEnergy != 0 || building.ReservedIncoming != 0 || building.CommittedIncoming != 0))
                    Fail($"失效建筑 {building.Id} 仍有库存或预约。");
                checkedStored.TryGetValue(building.Id, out long actualStored);
                checkedReservations.TryGetValue(building.Id, out long actualReserved);
                checkedCommitments.TryGetValue(building.Id, out long actualCommitted);
                if ((building.Kind == BuildingKind.Relay || building.Kind == BuildingKind.Battery) && building.StoredEnergy != actualStored)
                    Fail($"中继 {building.Id} 批次数量与库存不一致。");
                if (building.ReservedIncoming != actualReserved || building.CommittedIncoming != actualCommitted)
                    Fail($"建筑 {building.Id} 的预约与活动运输记录不一致。");
            }
        }

        private static void Add(Dictionary<long, long> amounts, long buildingId, long amount)
        {
            amounts.TryGetValue(buildingId, out long previous);
            amounts[buildingId] = checked(previous + amount);
        }

        private void Fail(string reason) { throw new InvalidOperationException($"能源模拟第 {lastTick} 步：{reason}"); }
    }
}
