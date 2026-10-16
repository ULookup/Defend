namespace Defend.Simulation
{
    /// <summary>
    /// 一栋已拥有建筑的权威状态。外部只能读取，修改必须经过世界命令或能源系统。
    /// 稳定身份与生命、位置版本分开，移动拒收旧端点运输；建筑摧毁后不能恢复。
    /// </summary>
    public sealed class BuildingState
    {
        public long Id { get; }
        public BuildingDefinition Definition { get; internal set; }
        public int X { get; internal set; }
        public int Z { get; internal set; }
        public int OrientationQuarterTurns { get; internal set; }
        public int OrientationDegrees => OrientationQuarterTurns * 90;
        public int FootprintWidth => Definition.GetFootprintWidth(OrientationQuarterTurns);
        public int FootprintDepth => Definition.GetFootprintDepth(OrientationQuarterTurns);
        public int Health { get; internal set; }
        public int LifeGeneration { get; internal set; }
        public int PositionRevision { get; internal set; }
        public int PriorityWeight { get; internal set; }
        public bool IsAlive { get; internal set; }
        public bool IsSold { get; internal set; }
        public long StoredEnergy { get; internal set; }
        public long ReservedIncoming { get; internal set; }
        public long CommittedIncoming { get; internal set; }
        public long TotalReceived { get; internal set; }
        public long ReceivedLastSecond { get; internal set; }
        internal long ReceivedAtLastSample;
        /// <summary>实际离开建筑的能源总量；预约、报价均不计入放电。</summary>
        public long TotalDischarged { get; internal set; }
        public long DischargedLastSecond { get; internal set; }
        internal long DischargedAtLastSample;
        public long NetEnergyChangeLastSecond => ReceivedLastSecond - DischargedLastSecond;
        public string EnergyStatus { get; internal set; }
        public long NextShotTick { get; internal set; }
        public long LastShotTick { get; internal set; } = -1;
        public long ShotsFired { get; internal set; }
        public int FacingDegrees { get; internal set; }
        public long ChargeStartedTick { get; internal set; } = -1;
        public double ChargeProgress { get; internal set; }
        public TurretCombatState CombatState { get; internal set; }
        public long DamageDealt { get; internal set; }
        public long EnergyWaitTicks { get; internal set; }
        public int DestructionCount { get; internal set; }
        public long LastDamageTick { get; internal set; } = -100;
        public long LastRepairTick { get; internal set; } = -100;
        public long RepairResonance { get; internal set; }
        public long BurstResonance { get; internal set; }
        public long TotalResonanceReceived { get; internal set; }
        public long ResonanceBurstStartedTick { get; internal set; } = -1;
        public long ResonanceBurstResolveTick { get; internal set; } = -1;
        public long NextResonanceBurstTick { get; internal set; }
        public long ResonanceBurstsReleased { get; internal set; }
        public long LastEnemyDamageTick { get; internal set; } = -1;
        public bool ResonanceFeedbackReady { get; internal set; }

        public BuildingKind Kind => Definition.Kind;
        public bool CanOutput => IsAlive && !IsSold && (Kind == BuildingKind.Reactor || Kind == BuildingKind.Relay || Kind == BuildingKind.Battery);
        public bool CanReceive => IsAlive && !IsSold && Kind != BuildingKind.Reactor &&
            (Kind != BuildingKind.ResonanceMagneticTower || Definition.Resonance.RequiresEnergy);
        public bool IsTurret => Kind == BuildingKind.Turret || Kind == BuildingKind.BurstTurret || Kind == BuildingKind.AreaTurret || Kind == BuildingKind.PiercingRailgun;
        public bool IsConsumer => IsTurret || Kind == BuildingKind.Core ||
            (Kind == BuildingKind.ResonanceMagneticTower && Definition.Resonance.RequiresEnergy);
        public bool IsEnergyDestination => IsConsumer || Kind == BuildingKind.Battery;
        public double CenterX => X + FootprintWidth * 0.5;
        public double CenterZ => Z + FootprintDepth * 0.5;

        internal BuildingState CloneForCheckpoint()
        {
            return new BuildingState(Id, Definition, X, Z)
            {
                X = X,
                Z = Z,
                OrientationQuarterTurns = OrientationQuarterTurns,
                Health = Health,
                LifeGeneration = LifeGeneration,
                PositionRevision = PositionRevision,
                PriorityWeight = PriorityWeight,
                IsAlive = IsAlive,
                IsSold = IsSold,
                StoredEnergy = StoredEnergy,
                ReservedIncoming = ReservedIncoming,
                CommittedIncoming = CommittedIncoming,
                TotalReceived = TotalReceived,
                ReceivedLastSecond = ReceivedLastSecond,
                ReceivedAtLastSample = ReceivedAtLastSample,
                TotalDischarged = TotalDischarged,
                DischargedLastSecond = DischargedLastSecond,
                DischargedAtLastSample = DischargedAtLastSample,
                EnergyStatus = EnergyStatus,
                NextShotTick = NextShotTick,
                LastShotTick = LastShotTick,
                ShotsFired = ShotsFired,
                FacingDegrees = FacingDegrees,
                ChargeStartedTick = ChargeStartedTick,
                ChargeProgress = ChargeProgress,
                CombatState = CombatState,
                DamageDealt = DamageDealt,
                EnergyWaitTicks = EnergyWaitTicks,
                DestructionCount = DestructionCount,
                LastDamageTick = LastDamageTick,
                LastRepairTick = LastRepairTick,
                RepairResonance = RepairResonance,
                BurstResonance = BurstResonance,
                TotalResonanceReceived = TotalResonanceReceived,
                ResonanceBurstStartedTick = ResonanceBurstStartedTick,
                ResonanceBurstResolveTick = ResonanceBurstResolveTick,
                NextResonanceBurstTick = NextResonanceBurstTick,
                ResonanceBurstsReleased = ResonanceBurstsReleased,
                LastEnemyDamageTick = LastEnemyDamageTick,
                ResonanceFeedbackReady = ResonanceFeedbackReady
            };
        }

        internal BuildingState(long id, BuildingDefinition definition, int x, int z)
        {
            Id = id;
            Definition = definition;
            X = x;
            Z = z;
            Health = definition.MaximumHealth;
            LifeGeneration = 1;
            PositionRevision = 1;
            PriorityWeight = definition.Kind == BuildingKind.Battery ? 1 : 2;
            IsAlive = true;
            EnergyStatus = "等待供给";
        }
    }
}
