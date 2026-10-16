using Defend.Simulation;
using UnityEngine;

namespace Defend.Unity
{
    /// <summary>策划编辑的建筑配置。开始会话时转换成只读定义，不保存游戏中的当前储能。</summary>
    [CreateAssetMenu(menuName = "涌现防线/建筑定义", fileName = "BuildingDefinition")]
    public sealed class BuildingDefinitionAsset : ScriptableObject
    {
        [SerializeField] private BuildingKind kind;
        [SerializeField] private string displayName;
        [SerializeField, Min(1)] private int width = 1;
        [SerializeField, Min(1)] private int depth = 1;
        [Header("能量单位：1 点 = 1000 个最小单位")]
        [SerializeField, Min(0)] private long energyCapacity = 100000;
        [SerializeField, Min(0)] private long productionPerTick;
        [SerializeField, Min(0)] private long consumptionPerTick;
        [SerializeField, Min(0)] private long outputAmount = 8000;
        [SerializeField, Min(1)] private long maximumChargePerTick = 8000;
        [SerializeField, Min(1)] private int outputIntervalTicks = 4;
        [SerializeField, Min(0)] private float transmissionRange = 7;
        [Header("白盒经济与耐久参数")]
        [SerializeField, Min(1)] private int maximumHealth = 100;
        [SerializeField, Min(0)] private int purchaseCost = 100;
        [SerializeField, Min(0)] private int saleRefund = 50;
        [SerializeField, Min(0)] private int repairCost = 25;
        [Header("共振磁暴塔")]
        [SerializeField, Min(.1f)] private float resonanceAbsorptionRange = 5;
        [SerializeField, Min(1)] private int resonanceRepairThreshold = 10;
        [SerializeField, Min(1)] private int resonanceRepairAmount = 18;
        [SerializeField, Min(1)] private int resonanceBurstThreshold = 25;
        [SerializeField, Min(.1f)] private float resonanceBurstRadius = 3.5f;
        [SerializeField, Min(1)] private int resonanceBurstDamage = 45;
        [SerializeField, Range(.01f, 1)] private float resonanceSlowMultiplier = .6f;
        [SerializeField, Min(.05f)] private float resonanceSlowSeconds = 1.25f;
        [SerializeField, Min(.05f)] private float resonanceWindupSeconds = .9f;
        [SerializeField, Min(.05f)] private float resonanceRecentDamageSeconds = 3;
        [SerializeField, Min(1)] private float resonanceDamageBonusMultiplier = 1.15f;

        public BuildingKind Kind => kind;
        public string DisplayName => displayName;
        public int Width => width;
        public int Depth => depth;

        public BuildingDefinition CreateDefinition()
        {
            // 正式资产不允许维修比出售重买更贵；历史纯模拟定义保留兼容。
            if (kind != BuildingKind.Core && repairCost > purchaseCost - saleRefund)
                throw new System.InvalidOperationException($"{displayName} 的维修基价不能超过购买价格减出售返还。");
            return new BuildingDefinition(kind, displayName, width, depth, energyCapacity,
                productionPerTick, consumptionPerTick, outputAmount, outputIntervalTicks,
                transmissionRange, maximumHealth, purchaseCost, saleRefund, repairCost, maximumChargePerTick,
                kind == BuildingKind.ResonanceMagneticTower ? new ResonanceDefinition(
                    resonanceAbsorptionRange, resonanceRepairThreshold, resonanceRepairAmount,
                    resonanceBurstThreshold, resonanceBurstRadius, resonanceBurstDamage,
                    System.Math.Round(resonanceSlowMultiplier, 4), System.Math.Round(resonanceSlowSeconds, 4), System.Math.Round(resonanceWindupSeconds, 4),
                    System.Math.Round(resonanceWindupSeconds, 4), 0, 0, resonanceRecentDamageSeconds,
                    System.Math.Round(resonanceDamageBonusMultiplier, 4)) : null);
        }

        /// <summary>仅供资产首次制作时赋初值；不会在游戏运行中修改共享配置。</summary>
        public void ConfigureWhitebox(BuildingKind buildingKind, string buildingName,
            long capacity, long production, long consumption, long output, int interval,
            int cost, int refund, int repair)
        {
            kind = buildingKind;
            displayName = buildingName;
            energyCapacity = capacity;
            productionPerTick = production;
            consumptionPerTick = consumption;
            outputAmount = output;
            outputIntervalTicks = interval;
            purchaseCost = cost;
            saleRefund = refund;
            repairCost = repair;
        }
    }
}
