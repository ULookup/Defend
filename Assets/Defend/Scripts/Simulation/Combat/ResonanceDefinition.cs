using System;

namespace Defend.Simulation
{
    /// <summary>共振磁暴塔的只读规则。时间使用秒，进入模拟时按当前逻辑步频向上取整。</summary>
    public sealed class ResonanceDefinition
    {
        public double AbsorptionRange { get; }
        public int RepairThreshold { get; }
        public int RepairAmount { get; }
        public int BurstThreshold { get; }
        public double BurstRadius { get; }
        public int BurstDamage { get; }
        public double SlowMultiplier { get; }
        public double SlowSeconds { get; }
        public double BurstWindupSeconds { get; }
        public double BurstCooldownSeconds { get; }
        public long RepairEnergyCost { get; }
        public long BurstEnergyCost { get; }
        public double RecentDamageSeconds { get; }
        public double RecentDamageBonusMultiplier { get; }
        public bool RequiresEnergy => RepairEnergyCost > 0 || BurstEnergyCost > 0;

        public ResonanceDefinition(double absorptionRange = 5, int repairThreshold = 10, int repairAmount = 18,
            int burstThreshold = 25, double burstRadius = 3.5, int burstDamage = 45,
            double slowMultiplier = .6, double slowSeconds = 1.25, double burstWindupSeconds = .9,
            double burstCooldownSeconds = .9, long repairEnergyCost = 0, long burstEnergyCost = 0,
            double recentDamageSeconds = 3, double recentDamageBonusMultiplier = 1.15)
        {
            if (!PositiveFinite(absorptionRange) || !PositiveFinite(burstRadius))
                throw new ArgumentOutOfRangeException(nameof(absorptionRange));
            if (repairThreshold <= 0 || repairAmount <= 0 || burstThreshold <= 0 || burstDamage <= 0)
                throw new ArgumentOutOfRangeException(nameof(repairThreshold));
            if (!PositiveFinite(slowMultiplier) || slowMultiplier > 1 || !PositiveFinite(slowSeconds) ||
                !PositiveFinite(burstWindupSeconds) || !PositiveFinite(burstCooldownSeconds) ||
                !PositiveFinite(recentDamageSeconds) || !PositiveFinite(recentDamageBonusMultiplier) || recentDamageBonusMultiplier < 1)
                throw new ArgumentOutOfRangeException(nameof(slowMultiplier));
            if (repairEnergyCost < 0 || burstEnergyCost < 0 || repairEnergyCost > EnergyArithmetic.MaximumAmount || burstEnergyCost > EnergyArithmetic.MaximumAmount)
                throw new ArgumentOutOfRangeException(nameof(repairEnergyCost));
            AbsorptionRange = absorptionRange; RepairThreshold = repairThreshold; RepairAmount = repairAmount;
            BurstThreshold = burstThreshold; BurstRadius = burstRadius; BurstDamage = burstDamage;
            SlowMultiplier = slowMultiplier; SlowSeconds = slowSeconds; BurstWindupSeconds = burstWindupSeconds;
            BurstCooldownSeconds = burstCooldownSeconds; RepairEnergyCost = repairEnergyCost; BurstEnergyCost = burstEnergyCost;
            RecentDamageSeconds = recentDamageSeconds; RecentDamageBonusMultiplier = recentDamageBonusMultiplier;
        }

        public int Contribution(BuildingKind attacker)
        {
            switch (attacker)
            {
                case BuildingKind.Turret: return 1;
                case BuildingKind.BurstTurret:
                case BuildingKind.PiercingRailgun: return 5;
                case BuildingKind.AreaTurret: return 3;
                default: return 0;
            }
        }

        internal static long Ticks(double seconds, int frequency) => Math.Max(1, checked((long)Math.Ceiling(seconds * frequency)));
        private static bool PositiveFinite(double value) => value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
