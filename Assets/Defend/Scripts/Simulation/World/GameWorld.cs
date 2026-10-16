using System;
using System.Collections.Generic;

namespace Defend.Simulation
{
    /// <summary>
    /// 每局唯一的模拟组合入口。负责命令顺序、建筑生命周期和经济事务；能量结算委托给 EnergySystem。
    /// 可选战斗系统与能源试验负载互斥，原有能源白盒构造和行为保持兼容。
    /// </summary>
    public sealed partial class GameWorld
    {
        private readonly Dictionary<BuildingKind, BuildingDefinition> definitions;
        private readonly Dictionary<long, BuildingState> buildingsById = new Dictionary<long, BuildingState>();
        private readonly List<BuildingState> buildings = new List<BuildingState>();
        private readonly Queue<QueuedCommand> commands = new Queue<QueuedCommand>();
        private readonly BuildingGrid grid;
        private readonly List<WorldEventSnapshot> events = new List<WorldEventSnapshot>();
        public IReadOnlyList<WorldEventSnapshot> Events { get; }
        private long nextEventId = 1;
        internal void Record(WorldEventKind kind, long id, double x, double z, int amount = 0,
            HitContext hit = default, double time = -1, long shieldBefore = 0, long shieldAfter = 0, long shieldAbsorbed = 0)
        {
            bool enemy = kind == WorldEventKind.EnemyHit || kind == WorldEventKind.EnemyDeath || kind == WorldEventKind.Explosion;
            var building = enemy ? null : FindBuilding(id);
            events.Add(new WorldEventSnapshot(kind, id, x, z, amount, nextEventId++, CurrentTick,
                time >= 0 ? time : (double)CurrentTick / Settings.TicksPerSecond, hit,
                enemy ? hit.HealthAfter : building?.Health ?? 0, enemy ? hit.MaximumHealth : building?.Definition.MaximumHealth ?? 0,
                shieldBefore, shieldAfter, shieldAbsorbed));
        }
        private long nextBuildingId = 1;
        private long nextCommandSequence = 1;

        public WorldSettings Settings { get; }
        public long CurrentTick { get; private set; }
        public int Credits { get; private set; }
        public long TotalIncome { get; private set; }
        public int DestroyedBuildingCount { get; private set; }
        public bool CanEditBuildings => Battle == null || Battle.State == BattleState.Preparing;

        public BuildingRepairQuote RepairQuote(long buildingId) => BuildingEconomy.Quote(FindBuilding(buildingId));
        public BuildingRepairQuote RepairAllQuote() => BuildingEconomy.QuoteAll(Buildings);
        public bool TestLoadEnabled { get; private set; } = true;
        public IReadOnlyList<BuildingState> Buildings { get; }
        public EnergySystem Energy { get; }
        public BattleSystem Battle { get; }
        public CommandResult LastCommandResult { get; private set; }

        public GameWorld(IEnumerable<BuildingDefinition> definitions, WorldSettings settings = null,
            EnergySettings energySettings = null, BattleSettings battleSettings = null, bool sandbox = false)
        {
            if (definitions == null)
                throw new ArgumentNullException(nameof(definitions));
            this.definitions = new Dictionary<BuildingKind, BuildingDefinition>();
            foreach (BuildingDefinition definition in definitions)
            {
                if (definition == null || this.definitions.ContainsKey(definition.Kind))
                    throw new ArgumentException("建筑定义不能为空，同一种建筑只能注册一份定义。", nameof(definitions));
                this.definitions.Add(definition.Kind, definition);
            }
            if (this.definitions.Count == 0)
                throw new ArgumentException("世界至少需要一种建筑定义。", nameof(definitions));
            IsSandbox = sandbox;
            originalDefinitions = new Dictionary<BuildingKind, BuildingDefinition>(this.definitions);
            originalEnergySettings = energySettings ?? new EnergySettings();
            originalBattleSettings = battleSettings;
            if (battleSettings != null)
                foreach (BuildingKind kind in new[] { BuildingKind.Turret, BuildingKind.BurstTurret, BuildingKind.AreaTurret, BuildingKind.PiercingRailgun })
                    turretDefinitions[kind] = battleSettings.GetTurret(kind);
            Settings = settings ?? new WorldSettings();
            Credits = Settings.StartingCredits;
            grid = new BuildingGrid(Settings.Width, Settings.Depth);
            if (battleSettings != null) foreach (var cell in battleSettings.BlockedCells) grid.Block(cell.X, cell.Z);
            Buildings = buildings.AsReadOnly();
            Events = events.AsReadOnly();
            Energy = new EnergySystem(Buildings, originalEnergySettings);
            if (battleSettings != null)
            {
                Battle = new BattleSystem(this, grid, battleSettings);
                TestLoadEnabled = false;
            }
            LastCommandResult = new CommandResult(0, true, "白盒世界已就绪。", 0);
            if (sandbox) Sandbox = new SandboxConfigurationService(this);
        }

        public long Enqueue(WorldCommand command)
        {
            // 先落实更早的输入再判断接收权限，例如“继续准备”之后同批到达的购买。
            // 这里同样只处理事务，不借队列切换推进游戏时间。
            ProcessCommands();
            long sequence = nextCommandSequence;
            nextCommandSequence = checked(nextCommandSequence + 1);
            // 非法建设在接收时即拒绝，不能在暂停期间留存到下一准备阶段。
            if (IsBuildingEdit(command.Kind) && !CanEditBuildings)
                LastCommandResult = Result(sequence, false, "只能在准备阶段调整建筑。", command.BuildingId);
            else
                commands.Enqueue(new QueuedCommand(sequence, command));
            return sequence;
        }

        /// <summary>按接收顺序立即完成事务，不推进冷却、能源或游戏时间，供运行与暂停输入共同使用。</summary>
        public CommandResult Submit(WorldCommand command)
        {
            ProcessCommands();
            Enqueue(command);
            ProcessCommands();
            return LastCommandResult;
        }

        /// <summary>只消费本批命令。开波后的后续建设会再次校验阶段，绝不跨阶段补执行。</summary>
        public void ProcessCommands()
        {
            int countAtStart = commands.Count;
            for (int index = 0; index < countAtStart; index++)
            {
                QueuedCommand queued = commands.Dequeue();
                LastCommandResult = Apply(queued.Sequence, queued.Command);
                if (LastCommandResult.Succeeded) Revision++;
            }
        }

        private static bool IsBuildingEdit(WorldCommandKind kind)
            => kind == WorldCommandKind.Place || kind == WorldCommandKind.Move ||
               kind == WorldCommandKind.Sell || kind == WorldCommandKind.Repair ||
               kind == WorldCommandKind.RepairAll || kind == WorldCommandKind.SetPriority || kind == WorldCommandKind.SetFacing;

        public BuildingState FindBuilding(long buildingId)
        {
            buildingsById.TryGetValue(buildingId, out BuildingState building);
            return building;
        }

        public BuildingState FindBuildingAt(int x, int z)
        {
            return FindBuilding(grid.Find(x, z));
        }

        public bool IsTerrainBlocked(int x, int z) => grid.Find(x, z) < 0;

        /// <summary>预览与实际操作共用边界条件；调用只读取状态，不扣费也不修改世界。</summary>
        public string PlacementIssue(BuildingKind kind, int x, int z, long movingId = 0, int? orientationQuarterTurns = null)
        {
            if (!definitions.TryGetValue(kind, out var definition)) return "尚未配置这种建筑。";
            if (!CanEditBuildings) return "只能在准备阶段调整建筑。";
            int orientation = orientationQuarterTurns ?? 0;
            if (movingId != 0)
            {
                BuildingState moving = FindBuilding(movingId);
                if (moving == null || !moving.IsAlive || moving.IsSold) return "已毁或出售建筑不能移动。";
                if (moving.Kind != kind) return "移动建筑的类型不匹配。";
                orientation = orientationQuarterTurns ?? moving.OrientationQuarterTurns;
            }
            if (orientation < 0 || orientation > 3) return "建筑方向必须为四个网格方向之一。";
            int width = definition.GetFootprintWidth(orientation), depth = definition.GetFootprintDepth(orientation);
            if (Battle != null && kind == BuildingKind.Core && (movingId != 0 || Battle.CoreBuildingId != 0)) return "核心不能移动或重复建造。";
            if (!grid.CanPlace(x, z, width, depth, movingId)) return "位置超出地图、位于岩体或已被占用。";
            if (Battle != null)
            {
                string reason = Battle.ValidatePlacement(x, z, width, depth);
                if (reason != null) return reason;
            }
            return movingId == 0 && !HasUnlimitedFunds && Credits < definition.PurchaseCost ? "资金不足。" : null;
        }

        public void Step()
        {
            events.Clear();
            // 命令先于到货：同一步出售或移动接收建筑，旧端点不能再接收能量。
            ProcessCommands();
            if (Battle != null && Battle.IsFinished) return;
            CurrentTick = checked(CurrentTick + 1);
            Revision++;

            if (TestLoadEnabled)
            {
                for (int index = 0; index < buildings.Count; index++)
                {
                    BuildingState building = buildings[index];
                    if (building.IsAlive && !building.IsSold && building.IsConsumer)
                        Energy.ConsumeUpTo(building.Id, building.Definition.ConsumptionPerTick);
                }
            }
            Battle?.StepCombat(CurrentTick);
            // 核心在本步被摧毁就立即冻结；失败之后不再发电、运输或发放当波奖励。
            if (Battle != null && Battle.IsFinished) return;
            Energy.Step(CurrentTick);
            if (CurrentTick % Settings.TicksPerSecond == 0)
                foreach (var building in buildings)
                {
                    building.ReceivedLastSecond = building.TotalReceived - building.ReceivedAtLastSample;
                    building.ReceivedAtLastSample = building.TotalReceived;
                    building.DischargedLastSecond = building.TotalDischarged - building.DischargedAtLastSample;
                    building.DischargedAtLastSample = building.TotalDischarged;
                }
            Battle?.CompleteStep(CurrentTick);
        }

        private CommandResult Apply(long sequence, WorldCommand command)
        {
            if (Battle != null && Battle.IsFinished)
                return Result(sequence, false, "本局已经结束，请重新开始。", command.BuildingId);
            if (command.Kind == WorldCommandKind.StartWave)
            {
                if (IsSandbox && Battle != null && Battle.State == BattleState.Preparing)
                {
                    if (!Battle.ValidateSandboxWave(out string issue)) return Result(sequence, false, issue, 0);
                    CaptureSandboxCheckpoint();
                }
                return Battle == null ? Result(sequence, false, "能源试验场没有战斗波次。", 0) : Battle.StartWave(sequence);
            }
            if (command.Kind == WorldCommandKind.ContinuePreparing)
                return Battle == null ? Result(sequence, false, "能源试验场没有战斗波次。", 0) : Battle.ContinuePreparing(sequence);
            if (IsBuildingEdit(command.Kind) && !CanEditBuildings)
                return Result(sequence, false, "只能在准备阶段调整建筑。", command.BuildingId);
            if (command.Kind == WorldCommandKind.Place)
                return Place(sequence, command);
            if (command.Kind == WorldCommandKind.RepairAll)
                return RepairAll(sequence);
            if (command.Kind == WorldCommandKind.SetTestLoad)
            {
                if (Battle != null && command.Value != 0)
                    return Result(sequence, false, "战斗中由真实射击消耗能量，不能开启持续测试负载。", 0);
                TestLoadEnabled = command.Value != 0;
                return Result(sequence, true, TestLoadEnabled ? "持续测试负载已开启。" : "持续测试负载已关闭。", 0);
            }

            BuildingState building = FindBuilding(command.BuildingId);
            if (building == null || building.IsSold || !building.IsAlive)
                return Result(sequence, false, "建筑不存在、已出售或已被摧毁。", command.BuildingId);

            switch (command.Kind)
            {
                case WorldCommandKind.SetFacing:
                    if (building.Kind != BuildingKind.PiercingRailgun)
                        return Result(sequence, false, "只有贯穿轨道炮需要设定主要射击方向。", building.Id);
                    building.FacingDegrees = (command.Value % 360 + 360) % 360;
                    Battle?.ResetTurretAim(building);
                    return Result(sequence, true, "主要射击方向已更新。", building.Id);
                case WorldCommandKind.Move:
                    return Move(sequence, building, command.X, command.Z, command.OrientationQuarterTurns, command.FacingDegrees);
                case WorldCommandKind.Sell:
                    return Sell(sequence, building);
                case WorldCommandKind.Damage:
                    return Damage(sequence, building, command.Value);
                case WorldCommandKind.Repair:
                    return Repair(sequence, building);
                case WorldCommandKind.SetPriority:
                    if (!building.IsEnergyDestination || command.Value <= 0 || command.Value > 100)
                        return Result(sequence, false, "仅用能建筑可设置优先级，权重必须在 1 到 100 之间。", building.Id);
                    building.PriorityWeight = command.Value;
                    return Result(sequence, true, "能量优先级已更新。", building.Id);
                default:
                    return Result(sequence, false, "无法识别的世界命令。", building.Id);
            }
        }

        private CommandResult Place(long sequence, WorldCommand command)
        {
            if (!definitions.TryGetValue(command.BuildingKind, out BuildingDefinition definition))
                return Result(sequence, false, "尚未配置这种建筑。", 0);
            int orientation = command.OrientationQuarterTurns ?? 0;
            if (orientation < 0 || orientation > 3)
                return Result(sequence, false, "建筑方向必须为四个网格方向之一。", 0);
            int width = definition.GetFootprintWidth(orientation), depth = definition.GetFootprintDepth(orientation);
            if (!grid.CanPlace(command.X, command.Z, width, depth))
                return Result(sequence, false, "位置超出地图或与其他建筑重叠。", 0);
            if (Battle != null)
            {
                if (definition.Kind == BuildingKind.Core && Battle.CoreBuildingId != 0)
                    return Result(sequence, false, "每局只能有一座核心。", 0);
                string reason = Battle.ValidatePlacement(command.X, command.Z, width, depth);
                if (reason != null) return Result(sequence, false, reason, 0);
            }
            if (!HasUnlimitedFunds && Credits < definition.PurchaseCost)
                return Result(sequence, false, "资源不足，无法购买建筑。", 0);

            long identifier = nextBuildingId;
            long followingIdentifier = checked(nextBuildingId + 1);
            var building = new BuildingState(identifier, definition, command.X, command.Z)
            {
                OrientationQuarterTurns = orientation,
                FacingDegrees = NormalizeFacing(command.FacingDegrees ?? orientation * 90)
            };
            grid.Occupy(building);
            buildings.Add(building);
            buildingsById.Add(building.Id, building);
            nextBuildingId = followingIdentifier;
            if (!HasUnlimitedFunds) Credits -= definition.PurchaseCost;
            Battle?.RegisterCore(building);
            return Result(sequence, true, definition.DisplayName + "已放置。", building.Id);
        }

        private CommandResult Move(long sequence, BuildingState building, int x, int z,
            int? orientationQuarterTurns, int? facingDegrees)
        {
            if (building.Kind == BuildingKind.Core)
                return Result(sequence, false, "核心不能移动。", building.Id);
            int orientation = orientationQuarterTurns ?? building.OrientationQuarterTurns;
            if (orientation < 0 || orientation > 3)
                return Result(sequence, false, "建筑方向必须为四个网格方向之一。", building.Id);
            int facing = NormalizeFacing(facingDegrees ?? building.FacingDegrees + (orientation - building.OrientationQuarterTurns) * 90);
            int width = building.Definition.GetFootprintWidth(orientation), depth = building.Definition.GetFootprintDepth(orientation);
            bool geometryChanged = building.X != x || building.Z != z || building.FootprintWidth != width || building.FootprintDepth != depth;
            bool directionChanged = building.OrientationQuarterTurns != orientation || building.FacingDegrees != facing;
            if (!geometryChanged && !directionChanged)
                return Result(sequence, true, "建筑位置和方向未改变。", building.Id);
            if (!grid.CanPlace(x, z, width, depth, building.Id))
                return Result(sequence, false, "目标位置超出地图或已被占用。", building.Id);
            if (Battle != null)
            {
                string reason = Battle.ValidatePlacement(x, z, width, depth);
                if (reason != null) return Result(sequence, false, reason, building.Id);
            }

            // 原地旋转且占地未变时，能源端点仍然有效；不能损失已发出的接收批次。
            int nextPositionRevision = geometryChanged ? checked(building.PositionRevision + 1) : building.PositionRevision;
            if (geometryChanged)
            {
                Energy.BeforeBuildingChanged(building, false);
                grid.Release(building); // 必须在写入新方向之前释放旧占地。
            }
            building.X = x;
            building.Z = z;
            building.OrientationQuarterTurns = orientation;
            building.FacingDegrees = facing;
            building.PositionRevision = nextPositionRevision;
            if (geometryChanged)
            {
                BattleSystem.CancelResonanceBurst(building);
                building.NextResonanceBurstTick = 0;
                grid.Occupy(building);
            }
            if (building.IsTurret) Battle?.ResetTurretAim(building);
            return Result(sequence, true, geometryChanged ? "建筑已移动，旧位置的未完成接收已取消。" : "建筑方向已更新。", building.Id);
        }

        private static int NormalizeFacing(int degrees) => (degrees % 360 + 360) % 360;

        private CommandResult Sell(long sequence, BuildingState building)
        {
            if (building.Kind == BuildingKind.Core)
                return Result(sequence, false, "核心不能出售。", building.Id);
            if (!HasUnlimitedFunds && Credits > int.MaxValue - building.Definition.SaleRefund)
                return Result(sequence, false, "资源数量达到上限，无法完成出售。", building.Id);
            Energy.BeforeBuildingChanged(building, true);
            grid.Release(building);
            building.IsSold = true;
            building.IsAlive = false;
            building.Health = 0;
            Battle?.ResetTurretAim(building);
            building.CombatState = TurretCombatState.None;
            if (!HasUnlimitedFunds) { Credits += building.Definition.SaleRefund; TotalIncome += building.Definition.SaleRefund; }
            return Result(sequence, true, "建筑已出售，内部储能已损失。", building.Id);
        }

        private CommandResult Damage(long sequence, BuildingState building, int amount)
        {
            if (!building.IsAlive || amount <= 0)
                return Result(sequence, false, "建筑已经损坏，或伤害数值无效。", building.Id);
            int healthDamage = amount;
            if (building.Kind == BuildingKind.Core)
            {
                // 白盒阶段按 1 点能量吸收 1 点伤害；生命值为整数，剩余伤害向上取整。
                long shieldBefore = building.StoredEnergy;
                long absorbed = Energy.ConsumeUpTo(building.Id, (long)amount * 1000);
                healthDamage = (int)(((long)amount * 1000 - absorbed + 999) / 1000);
                if (absorbed > 0)
                {
                    Record(WorldEventKind.ShieldHit, building.Id, building.CenterX, building.CenterZ, shieldBefore: shieldBefore, shieldAfter: building.StoredEnergy, shieldAbsorbed: absorbed);
                    if (building.StoredEnergy == 0) Record(WorldEventKind.ShieldBreak, building.Id, building.CenterX, building.CenterZ, shieldBefore: shieldBefore, shieldAfter: 0, shieldAbsorbed: absorbed);
                }
            }
            int actualDamage = Math.Min(healthDamage, building.Health);
            building.Health = Math.Max(0, building.Health - healthDamage);
            if (actualDamage > 0) Record(WorldEventKind.BuildingHit, building.Id, building.CenterX, building.CenterZ, actualDamage);
            if (healthDamage > 0) building.LastDamageTick = CurrentTick;
            if (building.Health == 0)
            {
                building.DestructionCount++;
                if (building.Kind != BuildingKind.Core) DestroyedBuildingCount++;
                Record(WorldEventKind.BuildingDestroyed, building.Id, building.CenterX, building.CenterZ);
                Energy.BeforeBuildingChanged(building, true);
                building.IsAlive = false;
                building.EnergyStatus = "建筑已摧毁";
                building.CombatState = TurretCombatState.None;
                grid.Release(building);
                Battle?.BuildingDestroyed(building);
            }
            if (!building.IsAlive && Battle != null && building.Kind == BuildingKind.Core)
                return Result(sequence, true, "核心已被摧毁，本局结束。", building.Id);
            return Result(sequence, true, building.IsAlive ? "测试伤害已结算。" : "建筑已永久摧毁，可在空地重新建造。", building.Id);
        }

        private CommandResult Repair(long sequence, BuildingState building)
        {
            BuildingRepairQuote quote = BuildingEconomy.Quote(building);
            if (!quote.CanRepair) return Result(sequence, false, quote.Issue, building.Id);
            if (!HasUnlimitedFunds && Credits < quote.Cost) return Result(sequence, false, "资金不足，无法维修建筑。", building.Id);
            if (!HasUnlimitedFunds) Credits -= (int)quote.Cost;
            RestoreHealth(building);
            return Result(sequence, true, "建筑已维修，储能保持不变。", building.Id);
        }

        private CommandResult RepairAll(long sequence)
        {
            // 单线程事务先重新检查全部对象与总价，足额后一次扣款；失败不能留下部分修复。
            BuildingRepairQuote quote = RepairAllQuote();
            if (!quote.CanRepair) return Result(sequence, false, quote.Issue, 0);
            if (!HasUnlimitedFunds && Credits < quote.Cost) return Result(sequence, false, "资金不足，全部维修未执行。", 0);
            if (!HasUnlimitedFunds) Credits -= (int)quote.Cost;
            foreach (BuildingState building in buildings)
                if (BuildingEconomy.Quote(building).CanRepair) RestoreHealth(building);
            return Result(sequence, true, "已维修全部受损建筑。", 0);
        }

        private void RestoreHealth(BuildingState building)
        {
            // 仅恢复存活普通建筑的耐久，身份、能源历史与到货承诺均保持不变。
            building.Health = building.Definition.MaximumHealth;
            building.LastRepairTick = CurrentTick;
            Record(WorldEventKind.Repair, building.Id, building.CenterX, building.CenterZ);
        }

        internal void ApplyBattleDamage(BuildingState building, int amount)
        {
            int before = building.Health;
            Damage(0, building, amount);
            if (building.Kind == BuildingKind.ResonanceMagneticTower && building.IsAlive && building.Health < before)
            {
                building.LastEnemyDamageTick = CurrentTick;
                building.ResonanceFeedbackReady = true;
            }
        }

        internal int GrantBattleReward(int amount)
        {
            if (HasUnlimitedFunds) return 0;
            int actual = Math.Min(Math.Max(0, amount), int.MaxValue - Credits);
            Credits += actual;
            TotalIncome += actual;
            return actual;
        }

        private static CommandResult Result(long sequence, bool succeeded, string message, long buildingId)
        {
            return new CommandResult(sequence, succeeded, message, buildingId);
        }

        private readonly struct QueuedCommand
        {
            public long Sequence { get; }
            public WorldCommand Command { get; }

            public QueuedCommand(long sequence, WorldCommand command)
            {
                Sequence = sequence;
                Command = command;
            }
        }
    }
}
