using Defend.Simulation;

namespace Defend.Unity
{
    /// <summary>供能范围的只读表现查询。距离覆盖不代表已接通或当前能够发出能量。</summary>
    public static class EnergyCoverageQuery
    {
        public static bool IsSource(BuildingState source) => source != null && source.CanOutput &&
            source.Definition.OutputAmount > 0 && source.Definition.TransmissionRange > 0;

        /// <summary>重叠范围取最高单跳运输效率，不累加多个来源的效率。</summary>
        public static BuildingState BestSource(GameWorld world, double x, double z,
            long excludedSourceId, out double efficiency)
        {
            efficiency = 0;
            BuildingState best = null;
            if (world == null) return null;
            foreach (var source in world.Buildings)
            {
                if (!IsSource(source) || source.Id == excludedSourceId) continue;
                double candidate = world.Energy.DirectEfficiencyAt(source, x, z);
                if (candidate <= efficiency) continue;
                efficiency = candidate;
                best = source;
            }
            return best;
        }

        /// <summary>从模拟接口读取范围边缘效率，色带与图例无需复制衰减公式。</summary>
        public static double MinimumEfficiency(GameWorld world, BuildingDefinition definition)
            => world == null || definition == null ? 0 : world.Energy.DirectEfficiencyAt(
                definition, 0, 0, definition.TransmissionRange, 0);
    }
}
