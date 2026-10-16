using System;

namespace Defend.Simulation
{
    /// <summary>全图重评与目标稳定性的不可变参数；旧局部范围与绕行上限字段仅保留序列兼容，不再限制候选。</summary>
    public sealed class EnemyBehaviorSettings
    {
        public bool Enabled { get; }
        public double RouteImprovementRatio { get; }
        public double BaseDestructionValue { get; }
        public double CoreValue { get; }
        public double FirepowerValue { get; }
        public double SupplyValue { get; }
        public double SensingRadius { get; }
        public double ObservationMemorySeconds { get; }
        public double EvaluationIntervalSeconds { get; }
        public double MinimumTargetLockSeconds { get; }
        public double SwitchScoreRatio { get; }
        public int MaximumCandidates { get; }
        public int MaximumEvaluationsPerTick { get; }
        public int MaximumPathBuildsPerTick { get; }
        public int MaximumAdditionalTravelCells { get; }

        public EnemyBehaviorSettings(bool enabled = false, double sensingRadius = 8,
            double observationMemorySeconds = 3, double evaluationIntervalSeconds = 0.5,
            double minimumTargetLockSeconds = 3, double switchScoreRatio = 1.35,
            int maximumCandidates = 6, int maximumEvaluationsPerTick = 20,
            int maximumPathBuildsPerTick = 2, int maximumAdditionalTravelCells = 6, double routeImprovementRatio = .8, double baseDestructionValue = 5, double coreValue = 20, double firepowerValue = 3, double supplyValue = 6)
        {
            PositiveFinite(sensingRadius, nameof(sensingRadius));
            PositiveFinite(observationMemorySeconds, nameof(observationMemorySeconds));
            PositiveFinite(evaluationIntervalSeconds, nameof(evaluationIntervalSeconds));
            if (double.IsNaN(minimumTargetLockSeconds) || double.IsInfinity(minimumTargetLockSeconds) || minimumTargetLockSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(minimumTargetLockSeconds));
            if (double.IsNaN(switchScoreRatio) || double.IsInfinity(switchScoreRatio) || switchScoreRatio < 1)
                throw new ArgumentOutOfRangeException(nameof(switchScoreRatio));
            if (maximumCandidates <= 0 || maximumCandidates > 100) throw new ArgumentOutOfRangeException(nameof(maximumCandidates));
            if (maximumEvaluationsPerTick <= 0) throw new ArgumentOutOfRangeException(nameof(maximumEvaluationsPerTick));
            if (maximumPathBuildsPerTick <= 0) throw new ArgumentOutOfRangeException(nameof(maximumPathBuildsPerTick));
            if (maximumAdditionalTravelCells < 0) throw new ArgumentOutOfRangeException(nameof(maximumAdditionalTravelCells));
            if (routeImprovementRatio <= 0 || routeImprovementRatio >= 1 || double.IsNaN(routeImprovementRatio)) throw new ArgumentOutOfRangeException(nameof(routeImprovementRatio));
            PositiveFinite(baseDestructionValue, nameof(baseDestructionValue)); PositiveFinite(coreValue, nameof(coreValue));
            PositiveFinite(firepowerValue, nameof(firepowerValue)); PositiveFinite(supplyValue, nameof(supplyValue));
            BaseDestructionValue = baseDestructionValue; CoreValue = coreValue; FirepowerValue = firepowerValue; SupplyValue = supplyValue;
            RouteImprovementRatio = routeImprovementRatio;
            Enabled = true;
            SensingRadius = sensingRadius;
            ObservationMemorySeconds = observationMemorySeconds;
            EvaluationIntervalSeconds = evaluationIntervalSeconds;
            MinimumTargetLockSeconds = minimumTargetLockSeconds;
            SwitchScoreRatio = switchScoreRatio;
            MaximumCandidates = maximumCandidates;
            MaximumEvaluationsPerTick = maximumEvaluationsPerTick;
            MaximumPathBuildsPerTick = maximumPathBuildsPerTick;
            MaximumAdditionalTravelCells = maximumAdditionalTravelCells;
        }

        private static void PositiveFinite(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0 || value > 10000)
                throw new ArgumentOutOfRangeException(name);
        }
    }

    public enum EnemyTargetPurpose { AdvanceToCore, ClearObstacle, DisruptEnergy, SuppressTurret, SearchLastKnownPosition }

    /// <summary>只读工作量证据；观察记录不计作完整目标评估或距离场构建。</summary>
    public sealed class EnemyBehaviorStatistics
    {
        public int EvaluationsThisTick { get; internal set; }
        public int PathBuildsThisTick { get; internal set; }
        public int DeferredEvaluationsThisTick { get; internal set; }
        public int DeferredPathsThisTick { get; internal set; }
        public int TargetSwitchesThisTick { get; internal set; }
        public long TotalEvaluations { get; internal set; }
        public long TotalPathBuilds { get; internal set; }
        public long TotalDeferredEvaluations { get; internal set; }
        public long TotalDeferredPaths { get; internal set; }
        public long TotalTargetSwitches { get; internal set; }
        public int CachedPathCount { get; internal set; }
        public int MaximumEvaluationsObserved { get; internal set; }
        public int MaximumPathBuildsObserved { get; internal set; }
        public int BuildingsObservedThisTick { get; internal set; }
        public int DamageEventsObservedThisTick { get; internal set; }
        public int DeliveriesObservedThisTick { get; internal set; }
        public long TotalBuildingsObserved { get; internal set; }
        public long TotalDamageEventsObserved { get; internal set; }
        public long TotalDeliveriesObserved { get; internal set; }

        internal void CopyFrom(EnemyBehaviorStatistics other)
        {
            EvaluationsThisTick = other.EvaluationsThisTick; PathBuildsThisTick = other.PathBuildsThisTick;
            DeferredEvaluationsThisTick = other.DeferredEvaluationsThisTick; DeferredPathsThisTick = other.DeferredPathsThisTick;
            TargetSwitchesThisTick = other.TargetSwitchesThisTick; TotalEvaluations = other.TotalEvaluations;
            TotalPathBuilds = other.TotalPathBuilds; TotalDeferredEvaluations = other.TotalDeferredEvaluations;
            TotalDeferredPaths = other.TotalDeferredPaths; TotalTargetSwitches = other.TotalTargetSwitches;
            CachedPathCount = other.CachedPathCount; MaximumEvaluationsObserved = other.MaximumEvaluationsObserved;
            MaximumPathBuildsObserved = other.MaximumPathBuildsObserved; BuildingsObservedThisTick = other.BuildingsObservedThisTick;
            DamageEventsObservedThisTick = other.DamageEventsObservedThisTick; DeliveriesObservedThisTick = other.DeliveriesObservedThisTick;
            TotalBuildingsObserved = other.TotalBuildingsObserved; TotalDamageEventsObserved = other.TotalDamageEventsObserved;
            TotalDeliveriesObserved = other.TotalDeliveriesObserved;
        }

        internal void BeginStep()
        {
            EvaluationsThisTick = PathBuildsThisTick = DeferredEvaluationsThisTick = TargetSwitchesThisTick = 0;
            DeferredPathsThisTick = 0;
            BuildingsObservedThisTick = DamageEventsObservedThisTick = DeliveriesObservedThisTick = 0;
        }

        internal void Deferred() { DeferredEvaluationsThisTick++; TotalDeferredEvaluations++; }
        internal void DeferredPath() { DeferredPathsThisTick++; TotalDeferredPaths++; }
        internal void Switched() { TargetSwitchesThisTick++; TotalTargetSwitches++; }
    }
}
