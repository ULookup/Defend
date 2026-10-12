using System;

namespace Defend.Simulation
{
    /// <summary>能源运输的模拟参数。时间均使用逻辑步，暂停不会消耗运输预算。</summary>
    public sealed class EnergySettings
    {
        public int StallTimeoutTicks { get; }
        public int FlightTicksPerCell { get; }
        public long BaseQuantum { get; }
        public double MaximumLossAtRange { get; }
        public int MaximumPathWorkPerTick { get; }
        public int PathWorkSlice { get; }
        public double BatterySupportStartRatio { get; }
        public double BatterySupportStopRatio { get; }
        public double BatteryBalanceStartDifference { get; }
        public double BatteryBalanceStopDifference { get; }

        public EnergySettings(int stallTimeoutTicks = 200, int flightTicksPerCell = 3,
            long baseQuantum = 1000, double maximumLossAtRange = 0.5,
            int maximumPathWorkPerTick = 32768, int pathWorkSlice = 2048,
            double batterySupportStartRatio = 0.3, double batterySupportStopRatio = 0.75,
            double batteryBalanceStartDifference = 0.25, double batteryBalanceStopDifference = 0.1)
        {
            if (stallTimeoutTicks < 1) throw new ArgumentOutOfRangeException(nameof(stallTimeoutTicks));
            if (flightTicksPerCell < 1) throw new ArgumentOutOfRangeException(nameof(flightTicksPerCell));
            if (baseQuantum < 1 || baseQuantum > EnergyArithmetic.MaximumAmount / 1000)
                throw new ArgumentOutOfRangeException(nameof(baseQuantum));
            if (double.IsNaN(maximumLossAtRange) || maximumLossAtRange < 0 || maximumLossAtRange >= 1)
                throw new ArgumentOutOfRangeException(nameof(maximumLossAtRange));
            if (maximumPathWorkPerTick < 1) throw new ArgumentOutOfRangeException(nameof(maximumPathWorkPerTick));
            if (pathWorkSlice < 1) throw new ArgumentOutOfRangeException(nameof(pathWorkSlice));
            if (!(batterySupportStartRatio >= 0 && batterySupportStartRatio < batterySupportStopRatio && batterySupportStopRatio <= 1))
                throw new ArgumentOutOfRangeException(nameof(batterySupportStartRatio));
            if (!(batteryBalanceStopDifference >= 0 && batteryBalanceStopDifference < batteryBalanceStartDifference && batteryBalanceStartDifference <= 1))
                throw new ArgumentOutOfRangeException(nameof(batteryBalanceStopDifference));
            BatterySupportStartRatio = batterySupportStartRatio;
            BatterySupportStopRatio = batterySupportStopRatio;
            BatteryBalanceStartDifference = batteryBalanceStartDifference;
            BatteryBalanceStopDifference = batteryBalanceStopDifference;
            StallTimeoutTicks = stallTimeoutTicks;
            FlightTicksPerCell = flightTicksPerCell;
            BaseQuantum = baseQuantum;
            MaximumLossAtRange = maximumLossAtRange;
            MaximumPathWorkPerTick = maximumPathWorkPerTick;
            PathWorkSlice = pathWorkSlice;
        }
    }
}
