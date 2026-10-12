using System.Collections.Generic;

namespace Defend.Simulation
{
    /// <summary>报价仅描述当前可维修对象，不授予阶段权限；执行时必须重新报价。</summary>
    public readonly struct BuildingRepairQuote
    {
        public long Cost { get; }
        public int BuildingCount { get; }
        public string Issue { get; }
        public bool CanRepair => BuildingCount > 0 && Issue == null;

        internal BuildingRepairQuote(long cost, int buildingCount, string issue = null)
        {
            Cost = cost;
            BuildingCount = buildingCount;
            Issue = issue;
        }
    }

    /// <summary>维修资格与计价的唯一来源。界面报价和实际事务共用，避免显示价与扣款价分离。</summary>
    internal static class BuildingEconomy
    {
        internal static BuildingRepairQuote Quote(BuildingState building)
        {
            if (building == null || building.IsSold || !building.IsAlive)
                return new BuildingRepairQuote(0, 0, "建筑不存在、已出售或已被摧毁。");
            if (building.Kind == BuildingKind.Core)
                return new BuildingRepairQuote(0, 0, "核心生命不能维修。");
            int missingHealth = building.Definition.MaximumHealth - building.Health;
            if (missingHealth <= 0) return new BuildingRepairQuote(0, 0, "无需维修。");
            // 先以长整数相乘，再逐座向上取整；全部维修不得先汇总耐久后取整。
            long numerator = (long)building.Definition.RepairCost * missingHealth;
            long cost = (numerator + building.Definition.MaximumHealth - 1) / building.Definition.MaximumHealth;
            return new BuildingRepairQuote(cost, 1);
        }

        internal static BuildingRepairQuote QuoteAll(IReadOnlyList<BuildingState> buildings)
        {
            long total = 0;
            int count = 0;
            foreach (BuildingState building in buildings)
            {
                BuildingRepairQuote quote = Quote(building);
                if (!quote.CanRepair) continue;
                total += quote.Cost;
                count++;
            }
            return new BuildingRepairQuote(total, count, count == 0 ? "无需维修。" : null);
        }
    }
}
