using Defend.Simulation;
using UnityEngine;

namespace Defend.Unity
{
    /// <summary>独立的白盒试验参数，避免把初始平衡值藏在运行时代码中。</summary>
    [CreateAssetMenu(menuName = "涌现防线/能源试验配置", fileName = "EnergyExperimentSettings")]
    public sealed class EnergyExperimentSettings : ScriptableObject
    {
        [SerializeField, Min(1)] private int width = 40;
        [SerializeField, Min(1)] private int depth = 40;
        [SerializeField, Range(1, 100)] private int ticksPerSecond = 20;
        [SerializeField, Min(0)] private int startingCredits = 3000;
        [SerializeField, Min(1)] private int stallTimeoutTicks = 200;
        [SerializeField, Min(1)] private int flightTicksPerCell = 3;
        [SerializeField, Min(1)] private long baseQuantum = 1000;
        [SerializeField, Range(0, 0.99f)] private float maximumLossAtRange = 0.5f;
        [SerializeField] private bool enableTestLoad = true;
        [SerializeField, Range(16, 1024)] private int maximumVisibleTransfers = 160;
        [SerializeField, Range(16, 2048)] private int maximumVisibleConnections = 600;

        [Header("电池供给启停阈值")]
        [SerializeField, Range(0, 1)] private float batterySupportStartRatio = 0.3f;
        [SerializeField, Range(0, 1)] private float batterySupportStopRatio = 0.75f;
        [SerializeField, Range(0, 1)] private float batteryBalanceStartDifference = 0.25f;
        [SerializeField, Range(0, 1)] private float batteryBalanceStopDifference = 0.1f;

        public int TicksPerSecond => ticksPerSecond;
        public bool EnableTestLoad => enableTestLoad;
        public int MaximumVisibleTransfers => maximumVisibleTransfers;
        public int MaximumVisibleConnections => maximumVisibleConnections;
        public WorldSettings CreateWorldSettings() => new WorldSettings(width, depth, ticksPerSecond, startingCredits);
        public EnergySettings CreateEnergySettings() => new EnergySettings(stallTimeoutTicks,
            flightTicksPerCell, baseQuantum, maximumLossAtRange,
            batterySupportStartRatio: batterySupportStartRatio, batterySupportStopRatio: batterySupportStopRatio,
            batteryBalanceStartDifference: batteryBalanceStartDifference, batteryBalanceStopDifference: batteryBalanceStopDifference);
    }
}
