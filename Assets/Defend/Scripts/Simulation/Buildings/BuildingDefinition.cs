using System;

namespace Defend.Simulation
{
    public enum BuildingKind
    {
        Reactor,
        Relay,
        Turret,
        Core,
        BurstTurret,
        AreaTurret,
        Battery = 6,
        PiercingRailgun = 7,
        ResonanceMagneticTower = 8
    }

    /// <summary>
    /// 单种建筑的只读规则。Unity 配置资产转换为此对象后，运行状态不能反向修改资产。
    /// 能量统一使用整数最小单位，1000 个最小单位代表 1 点能量。
    /// </summary>
    public sealed class BuildingDefinition
    {
        public BuildingKind Kind { get; }
        public string DisplayName { get; }
        public int Width { get; }
        public int Depth { get; }
        public long EnergyCapacity { get; }
        public long ProductionPerTick { get; }
        public long ConsumptionPerTick { get; }
        public long OutputAmount { get; }
        public long MaximumChargePerTick { get; }
        public int OutputIntervalTicks { get; }
        public double TransmissionRange { get; }
        public int MaximumHealth { get; }
        public int PurchaseCost { get; }
        public int SaleRefund { get; }
        public int RepairCost { get; }
        public ResonanceDefinition Resonance { get; }

        /// <summary>四向摆放后的实际占地；定义本身始终保留未旋转尺寸。</summary>
        public int GetFootprintWidth(int orientationQuarterTurns)
        {
            if (orientationQuarterTurns < 0 || orientationQuarterTurns > 3)
                throw new ArgumentOutOfRangeException(nameof(orientationQuarterTurns));
            return (orientationQuarterTurns & 1) == 0 ? Width : Depth;
        }

        public int GetFootprintDepth(int orientationQuarterTurns)
        {
            if (orientationQuarterTurns < 0 || orientationQuarterTurns > 3)
                throw new ArgumentOutOfRangeException(nameof(orientationQuarterTurns));
            return (orientationQuarterTurns & 1) == 0 ? Depth : Width;
        }

        public BuildingDefinition(BuildingKind kind, string displayName, int width, int depth,
            long energyCapacity, long productionPerTick, long consumptionPerTick,
            long outputAmount, int outputIntervalTicks, double transmissionRange,
            int maximumHealth, int purchaseCost, int saleRefund, int repairCost, long maximumChargePerTick = 8000,
            ResonanceDefinition resonance = null)
        {
            if (!Enum.IsDefined(typeof(BuildingKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (string.IsNullOrWhiteSpace(displayName))
                throw new ArgumentException("建筑名称不能为空。", nameof(displayName));
            if (width <= 0 || depth <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), "建筑占地必须为正整数。");
            if (energyCapacity <= 0 || energyCapacity > long.MaxValue / 1000000)
                throw new ArgumentOutOfRangeException(nameof(energyCapacity));
            if (productionPerTick < 0 || productionPerTick > energyCapacity)
                throw new ArgumentOutOfRangeException(nameof(productionPerTick));
            if (consumptionPerTick < 0 || consumptionPerTick > energyCapacity)
                throw new ArgumentOutOfRangeException(nameof(consumptionPerTick));
            if (outputAmount < 0 || outputAmount > long.MaxValue / 1000000)
                throw new ArgumentOutOfRangeException(nameof(outputAmount));
            if (outputIntervalTicks <= 0)
                throw new ArgumentOutOfRangeException(nameof(outputIntervalTicks));
            if (double.IsNaN(transmissionRange) || double.IsInfinity(transmissionRange) || transmissionRange < 0)
                throw new ArgumentOutOfRangeException(nameof(transmissionRange));
            if (maximumHealth <= 0)
                throw new ArgumentOutOfRangeException(nameof(maximumHealth));
            if (purchaseCost < 0 || saleRefund < 0 || saleRefund > purchaseCost || repairCost < 0)
                throw new ArgumentOutOfRangeException(nameof(purchaseCost), "费用必须非负，出售返还不能超过购买价格。");

            bool isSupplier = kind == BuildingKind.Reactor || kind == BuildingKind.Relay || kind == BuildingKind.Battery;
            if (maximumChargePerTick <= 0 || maximumChargePerTick > EnergyArithmetic.MaximumAmount)
                throw new ArgumentOutOfRangeException(nameof(maximumChargePerTick));
            if (isSupplier && (outputAmount == 0 || transmissionRange == 0))
                throw new ArgumentException("供能建筑必须具有正的输出量和传输范围。");
            if (kind != BuildingKind.Reactor && productionPerTick != 0)
                throw new ArgumentException("只有反应堆可以产生能量。");
            if (isSupplier && consumptionPerTick != 0)
                throw new ArgumentException("当前白盒仅允许炮塔和核心设置自用测试负载。");
            if (!isSupplier && outputAmount != 0)
                throw new ArgumentException("终端用能建筑不能向外转发能量。");
            if (resonance != null && kind != BuildingKind.ResonanceMagneticTower)
                throw new ArgumentException("只有共振磁暴塔可以配置共振规则。", nameof(resonance));
            if (kind == BuildingKind.ResonanceMagneticTower && (width != 2 || depth != 2))
                throw new ArgumentException("共振磁暴塔占地必须为 2 × 2 格。");

            Kind = kind;
            DisplayName = displayName;
            Width = width;
            Depth = depth;
            EnergyCapacity = energyCapacity;
            ProductionPerTick = productionPerTick;
            ConsumptionPerTick = consumptionPerTick;
            OutputAmount = outputAmount;
            MaximumChargePerTick = maximumChargePerTick;
            OutputIntervalTicks = outputIntervalTicks;
            TransmissionRange = transmissionRange;
            MaximumHealth = maximumHealth;
            PurchaseCost = purchaseCost;
            SaleRefund = saleRefund;
            RepairCost = repairCost;
            Resonance = kind == BuildingKind.ResonanceMagneticTower ? resonance ?? new ResonanceDefinition() : null;
        }
    }
}
