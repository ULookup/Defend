namespace Defend.Simulation
{
    /// <summary>累计项只记录真实数量变化；库存由当前持有人逐项汇总，独立核对守恒。</summary>
    internal sealed class EnergyLedger
    {
        public long ParameterAdjustmentLoss;
        public long Generated;
        public long Consumed;
        public long TransmissionLoss;
        public long BuildingLoss;
        public long FailedHopLoss;
        public long ExpiredLoss;
    }
}
