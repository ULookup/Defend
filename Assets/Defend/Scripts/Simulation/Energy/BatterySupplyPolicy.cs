using System;
using System.Collections.Generic;

namespace Defend.Simulation
{
    /// <summary>
    /// 电池仅决定新任务的供给资格和互补数量。已有合法承诺继续运输，不能被启停阈值撤销。
    /// 调度器每笔报价都读取当前库存与承诺；共享调度额度仍由原能源调度器拥有。
    /// </summary>
    internal sealed class BatterySupplyPolicy
    {
        private readonly EnergySettings settings;
        private readonly HashSet<long> supportedTerminals = new HashSet<long>();
        private readonly HashSet<Tuple<long, long>> balancingPairs = new HashSet<Tuple<long, long>>();

        public BatterySupplyPolicy(EnergySettings settings) { this.settings = settings; }

        public static long AvailableStored(EnergyNodeRuntime source, long tick)
        {
            long available = 0;
            foreach (EnergyBatch batch in source.Batches)
                if (!batch.Retired && !batch.InFlight && batch.Destination == null && batch.EligibleTick <= tick)
                    available = checked(available + batch.Amount);
            return available;
        }

        public bool CanSupport(EnergyNodeRuntime source, BuildingState target, long tick)
        {
            if (source.Building == target || !target.IsAlive || target.IsSold || EnergyDispatch.Demand(target) <= 0) return false;
            double receivedRatio = (double)(target.StoredEnergy + target.CommittedIncoming) / target.Definition.EnergyCapacity;
            if (target.Kind == BuildingKind.Battery)
            {
                var pair = Tuple.Create(source.Building.Id, target.Id);
                double difference = (double)AvailableStored(source, tick) / source.Building.Definition.EnergyCapacity - receivedRatio;
                double threshold = balancingPairs.Contains(pair) ? settings.BatteryBalanceStopDifference : settings.BatteryBalanceStartDifference;
                if (difference > threshold) { balancingPairs.Add(pair); return true; }
                balancingPairs.Remove(pair);
                return false;
            }
            if (!target.IsConsumer) return false;
            if (receivedRatio >= settings.BatterySupportStopRatio) { supportedTerminals.Remove(target.Id); return false; }
            bool lacksEnergy = target.StoredEnergy == 0 || target.CombatState == TurretCombatState.WaitingForEnergy;
            if (receivedRatio < settings.BatterySupportStartRatio || lacksEnergy) supportedTerminals.Add(target.Id);
            return supportedTerminals.Contains(target.Id);
        }

        public long LimitSend(EnergyNodeRuntime source, BuildingState target, EnergyRoute route, long proposed, long tick)
        {
            if (!CanSupport(source, target, tick)) return 0;
            if (target.Kind != BuildingKind.Battery)
            {
                long missing = Math.Max(0, (long)Math.Ceiling(target.Definition.EnergyCapacity * settings.BatterySupportStopRatio)
                    - target.StoredEnergy - target.CommittedIncoming);
                return EnergyArithmetic.LimitSend(proposed, missing, route);
            }
            long available = AvailableStored(source, tick);
            long receiving = target.StoredEnergy + target.CommittedIncoming;
            long lower = 0, upper = Math.Min(proposed, available);
            // 逐跳取整后的有效到货参与比例计算。单调二分不会把一次互补推过停止差值，
            // 也不会把本步刚充入、尚不可输出的库存提前出售。
            while (lower < upper)
            {
                long middle = lower + (upper - lower + 1) / 2;
                double difference = (double)(available - middle) / source.Building.Definition.EnergyCapacity
                    - (double)(receiving + EnergyArithmetic.Predict(middle, route)) / target.Definition.EnergyCapacity;
                if (difference >= settings.BatteryBalanceStopDifference) lower = middle;
                else upper = middle - 1;
            }
            return EnergyArithmetic.LimitSend(lower, EnergyDispatch.Demand(target), route);
        }
    }
}
