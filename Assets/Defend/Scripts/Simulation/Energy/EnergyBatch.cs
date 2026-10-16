using System.Collections.Generic;

namespace Defend.Simulation
{
    /// <summary>不可变实际访问链。拆分共享前缀，计划经过的节点从不加入历史。</summary>
    internal sealed class EnergyHistory
    {
        public readonly long BuildingId;
        public readonly EnergyHistory Previous;
        public EnergyHistory(long buildingId, EnergyHistory previous = null)
        {
            BuildingId = buildingId;
            Previous = previous;
        }
        public bool Contains(long buildingId)
        {
            for (EnergyHistory entry = this; entry != null; entry = entry.Previous)
                if (entry.BuildingId == buildingId) return true;
            return false;
        }
    }

    internal sealed class EnergyRelayCharge
    {
        internal long BuildingId;
        internal long Amount;
        internal EnergyRelayCharge(long buildingId, long amount) { BuildingId = buildingId; Amount = amount; }
    }

    internal sealed class EnergyBatch
    {
        public long Id;
        public long Amount;
        public BuildingState Location;
        public EnergyHistory History;
        public BuildingState Destination;
        public long Commitment;
        public long SourceServiceCharge;
        internal readonly List<EnergyRelayCharge> RelayServiceCharges = new List<EnergyRelayCharge>();
        public EnergyRoute Route;
        public int RouteEdge;
        public int StalledTicks;
        public bool IsStalled;
        public bool BlocksIncomingTraffic;
        public long LastStallTick;
        public long PlanningDeferredTick;
        public long EligibleTick;
        public bool InFlight;
        public bool Retired;
        public long FlightSourceId;
        public long FlightTargetId;
        public int TargetLifeGeneration;
        public int TargetPositionRevision;
        public double SourceX;
        public double SourceZ;
        public double TargetX;
        public double TargetZ;
        public long StartTick;
        public long ArrivalTick;
    }

    internal sealed class EnergyArrivalComparer : IComparer<EnergyBatch>
    {
        public int Compare(EnergyBatch first, EnergyBatch second)
        {
            int time = first.ArrivalTick.CompareTo(second.ArrivalTick);
            return time != 0 ? time : first.Id.CompareTo(second.Id);
        }
    }

    internal sealed class EnergyNodeRuntime
    {
        public readonly BuildingState Building;
        public readonly EnergyHistory InitialHistory;
        public readonly List<EnergyBatch> Batches = new List<EnergyBatch>();
        public readonly List<BuildingState> ActiveOutputConsumers = new List<BuildingState>();
        public readonly HashSet<long> ActiveOutputConsumerIds = new HashSet<long>();
        public readonly WeightedEnergySchedule OutputSchedule;
        public long NextOutputTick;
        public long AvailableOutput;
        public long LastOutputTick = -1;
        public EnergyNodeRuntime(BuildingState building, long quantum, EnergyHistory initialHistory = null)
        {
            Building = building;
            InitialHistory = initialHistory ?? new EnergyHistory(building.Id);
            OutputSchedule = new WeightedEnergySchedule(quantum);
        }
    }
}
