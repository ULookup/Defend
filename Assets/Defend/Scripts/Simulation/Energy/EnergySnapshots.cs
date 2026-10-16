using System.Collections.Generic;

namespace Defend.Simulation
{
    public readonly struct EnergyPlanningStatistics
    {
        public int WorkUsedThisTick { get; }
        public int PendingSearchCount { get; }
        public long CompletedSearchCount { get; }
        public long CacheHitCount { get; }
        public long SearchStartedCount { get; }
        internal EnergyPlanningStatistics(int work, int pending, long completed, long cacheHits, long started)
        {
            WorkUsedThisTick = work;
            PendingSearchCount = pending;
            CompletedSearchCount = completed;
            CacheHitCount = cacheHits;
            SearchStartedCount = started;
        }
    }

    /// <summary>用于白盒诊断的独立快照，不暴露可修改的批次或历史链。</summary>
    public sealed class EnergyBatchDiagnostic
    {
        public long Id { get; }
        public long BuildingId { get; }
        public bool InFlight { get; }
        public IReadOnlyList<long> History { get; }
        public int StalledTicks { get; }
        public long Amount { get; }
        public long DestinationId { get; }
        internal EnergyBatchDiagnostic(EnergyBatch batch)
        {
            Id = batch.Id;
            BuildingId = batch.Location == null ? 0 : batch.Location.Id;
            InFlight = batch.InFlight;
            StalledTicks = batch.StalledTicks;
            Amount = batch.Amount;
            DestinationId = batch.Destination == null ? 0 : batch.Destination.Id;
            var visited = new List<long>();
            for (EnergyHistory entry = batch.History; entry != null; entry = entry.Previous) visited.Add(entry.BuildingId);
            visited.Reverse();
            History = visited.AsReadOnly();
        }
    }

    /// <summary>只读总账。预约属于未来空间，不计入 Stored 或 InFlight。</summary>
    public readonly struct EnergyTotals
    {
        public long ParameterAdjustmentLoss { get; }
        public long Generated { get; }
        public long Consumed { get; }
        public long TransmissionLoss { get; }
        public long DestructionLoss { get; }
        public long ExpiredLoss { get; }
        public long InFlight { get; }
        public long Stored { get; }
        public long BalanceError { get; }
        public int ActiveBatchCount { get; }

        internal EnergyTotals(long generated, long consumed, long transmissionLoss, long destructionLoss,
            long expiredLoss, long inFlight, long stored, int activeBatchCount, long parameterAdjustmentLoss = 0)
        {
            ParameterAdjustmentLoss = parameterAdjustmentLoss;
            Generated = generated;
            Consumed = consumed;
            TransmissionLoss = transmissionLoss;
            DestructionLoss = destructionLoss;
            ExpiredLoss = expiredLoss;
            InFlight = inFlight;
            Stored = stored;
            ActiveBatchCount = activeBatchCount;
            BalanceError = checked(generated - consumed - transmissionLoss - destructionLoss - expiredLoss - inFlight - stored - parameterAdjustmentLoss);
        }
    }

    public readonly struct EnergyConnection
    {
        public long SourceId { get; }
        public long TargetId { get; }
        public double Efficiency { get; }

        internal EnergyConnection(long sourceId, long targetId, double efficiency)
        {
            SourceId = sourceId;
            TargetId = targetId;
            Efficiency = efficiency;
        }
    }

    /// <summary>表现只读取这份快照；动画结束不能向模拟写回能量。</summary>
    public readonly struct EnergyTransferSnapshot
    {
        public long Id { get; }
        public long SourceId { get; }
        public long TargetId { get; }
        public double SourceX { get; }
        public double SourceZ { get; }
        public double TargetX { get; }
        public double TargetZ { get; }
        public long StartTick { get; }
        public long ArrivalTick { get; }
        public long Amount { get; }

        internal EnergyTransferSnapshot(EnergyBatch batch)
        {
            Id = batch.Id;
            SourceId = batch.FlightSourceId;
            TargetId = batch.FlightTargetId;
            SourceX = batch.SourceX;
            SourceZ = batch.SourceZ;
            TargetX = batch.TargetX;
            TargetZ = batch.TargetZ;
            StartTick = batch.StartTick;
            ArrivalTick = batch.ArrivalTick;
            Amount = batch.Amount;
        }
    }
}
