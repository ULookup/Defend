using System;

namespace Defend.Simulation
{
    /// <summary>沙盒使用显示单位的类型草稿。构造候选不会改变现役定义或建筑。</summary>
    [Serializable]
    public sealed class SandboxBuildingParameters
    {
        public BuildingKind Kind;
        public int MaximumHealth;
        public double EnergyCapacity;
        public double ProductionPerSecond;
        public double OutputAmount;
        public double OutputIntervalSeconds;
        public double TransmissionRange;
        public double MaximumChargePerSecond;
        public double ShotCooldownSeconds;
        public double EnergyPerShot;
        public double AttackRange;
        public const double MaximumEnergy = 1000000;
        public const double MaximumRange = 200;
        public const double MaximumSeconds = 3600;
        public const int MaximumAllowedHealth = 1000000;

        public SandboxBuildingParameters Clone() => new SandboxBuildingParameters
        {
            Kind = Kind, MaximumHealth = MaximumHealth, EnergyCapacity = EnergyCapacity,
            ProductionPerSecond = ProductionPerSecond, OutputAmount = OutputAmount,
            OutputIntervalSeconds = OutputIntervalSeconds, TransmissionRange = TransmissionRange,
            MaximumChargePerSecond = MaximumChargePerSecond, ShotCooldownSeconds = ShotCooldownSeconds,
            EnergyPerShot = EnergyPerShot, AttackRange = AttackRange
        };

        public static SandboxBuildingParameters FromDefinitions(BuildingDefinition building, TurretDefinition turret, int ticksPerSecond)
        {
            if (building == null || ticksPerSecond <= 0) throw new ArgumentException("缺少有效建筑或逻辑步频。");
            return new SandboxBuildingParameters
            {
                Kind = building.Kind, MaximumHealth = building.MaximumHealth,
                EnergyCapacity = building.EnergyCapacity / 1000.0,
                ProductionPerSecond = building.ProductionPerTick * ticksPerSecond / 1000.0,
                OutputAmount = building.OutputAmount / 1000.0,
                OutputIntervalSeconds = (double)building.OutputIntervalTicks / ticksPerSecond,
                TransmissionRange = building.TransmissionRange,
                MaximumChargePerSecond = building.MaximumChargePerTick * ticksPerSecond / 1000.0,
                ShotCooldownSeconds = turret == null ? 1.0 / ticksPerSecond : (double)turret.CooldownTicks / ticksPerSecond,
                EnergyPerShot = turret == null ? 0 : turret.EnergyPerShot / 1000.0,
                AttackRange = turret == null ? 1 : turret.Range
            };
        }

        public bool TryCreateDefinitions(BuildingDefinition original, TurretDefinition weapon, int ticksPerSecond,
            out BuildingDefinition building, out TurretDefinition turret, out string error)
        {
            building = null; turret = null; error = null;
            try
            {
                if (original == null || original.Kind != Kind || ticksPerSecond <= 0) throw new ArgumentException("建筑类型或逻辑步频无效。");
                if (MaximumHealth < 1 || MaximumHealth > MaximumAllowedHealth) throw new ArgumentException("最大生命值必须在 1 至 1000000 之间。");
                long capacity = Amount(EnergyCapacity, false, "储能容量");
                bool supplier = Kind == BuildingKind.Reactor || Kind == BuildingKind.Relay || Kind == BuildingKind.Battery;
                long production = Kind == BuildingKind.Reactor ? Rate(ProductionPerSecond, ticksPerSecond, true, "每秒产能") : original.ProductionPerTick;
                long output = supplier ? Amount(OutputAmount, false, "单次输出量") : original.OutputAmount;
                int outputTicks = supplier ? Ticks(OutputIntervalSeconds, ticksPerSecond, "输出间隔") : original.OutputIntervalTicks;
                double range = supplier ? Distance(TransmissionRange, "传输范围") : original.TransmissionRange;
                long charge = Kind == BuildingKind.Battery ? Rate(MaximumChargePerSecond, ticksPerSecond, false, "每秒最大充电量") : original.MaximumChargePerTick;
                if (production > capacity) throw new ArgumentException("单个逻辑步的产能不能超过储能容量，请降低每秒产能或提高容量。");
                if (original.ConsumptionPerTick > capacity) throw new ArgumentException("储能容量不能低于当前单步基础耗能。");
                building = new BuildingDefinition(Kind, original.DisplayName, original.Width, original.Depth, capacity,
                    production, original.ConsumptionPerTick, output, outputTicks, range, MaximumHealth,
                    original.PurchaseCost, original.SaleRefund, original.RepairCost, charge, original.Resonance);
                if (Kind == BuildingKind.Turret || Kind == BuildingKind.BurstTurret || Kind == BuildingKind.AreaTurret || Kind == BuildingKind.PiercingRailgun)
                {
                    if (weapon == null || weapon.Kind != Kind) throw new ArgumentException("缺少该建筑的武器配置。");
                    int cooldown = Ticks(ShotCooldownSeconds, ticksPerSecond, "开火冷却");
                    long shot = Amount(EnergyPerShot, true, "每次射击耗能");
                    double attack = Distance(AttackRange, "攻击范围");
                    double flight = Math.Max(weapon.MaximumFlightDistance, attack + 3 + weapon.AreaArcHeight * 2);
                    double lifetime = Math.Max(weapon.MaximumLifetimeSeconds, flight / weapon.ProjectileSpeed + 1.0 / ticksPerSecond);
                    turret = new TurretDefinition(Kind, attack, weapon.Damage, shot, cooldown, weapon.SplashRadius,
                        weapon.ProjectileSpeed, weapon.ProjectileRadius, flight, lifetime, weapon.YawSpeedDegrees,
                        weapon.PitchSpeedDegrees, weapon.MinimumPitchDegrees, weapon.MaximumPitchDegrees,
                        weapon.AlignmentToleranceDegrees, weapon.MaximumLeadSeconds, weapon.MuzzleHeight,
                        weapon.MuzzleForwardOffset, weapon.PivotHeight, weapon.AreaArcHeight, weapon.MuzzleRightOffset,
                        weapon.PivotForwardOffset, weapon.PivotRightOffset, weapon.MinimumRange, weapon.FireArcHalfDegrees,
                        weapon.ChargeTicks, weapon.MaximumPierceTargets, weapon.PierceDamageMultiplier);
                }
                return true;
            }
            catch (ArgumentException exception) { building = null; turret = null; error = exception.Message; return false; }
            catch (OverflowException) { building = null; turret = null; error = "输入超出安全数值范围。"; return false; }
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static long Amount(double value, bool allowZero, string label)
        {
            if (!Finite(value) || value < 0 || value > MaximumEnergy || (!allowZero && value == 0))
                throw new ArgumentException(label + "必须" + (allowZero ? "非负" : "为正数") + "，且不超过 1000000 点。");
            if (value > 0 && value < .001) throw new ArgumentException(label + "不能低于 0.001 点的可表示精度。");
            return checked((long)Math.Round(value * 1000, MidpointRounding.AwayFromZero));
        }
        private static long Rate(double value, int ticks, bool allowZero, string label)
        {
            if (!Finite(value) || value < 0 || value > MaximumEnergy || (!allowZero && value == 0))
                throw new ArgumentException(label + "必须" + (allowZero ? "非负" : "为正数") + "，且不超过 1000000 点。");
            if (value > 0 && value * 1000 < ticks) throw new ArgumentException(label + "低于每步 0.001 点，请提高输入。");
            return checked((long)Math.Round(value * 1000 / ticks, MidpointRounding.AwayFromZero));
        }
        private static int Ticks(double seconds, int frequency, string label)
        {
            if (!Finite(seconds) || seconds < 1.0 / frequency || seconds > MaximumSeconds)
                throw new ArgumentException(label + "至少为一个逻辑步，且不超过 3600 秒。");
            return checked((int)Math.Ceiling(seconds * frequency - 1e-9));
        }
        private static double Distance(double value, string label)
        {
            if (!Finite(value) || value <= 0 || value > MaximumRange) throw new ArgumentException(label + "必须在 0 至 200 格之间且大于零。");
            return value;
        }
    }
}
