using System;

namespace Defend.Simulation
{
    /// <summary>武器与数值化炮口共用的权威配置。距离为格，速度为格每秒，角度为度。</summary>
    public sealed class TurretDefinition
    {
        public BuildingKind Kind { get; }
        public double Range { get; }
        public int Damage { get; }
        public long EnergyPerShot { get; }
        public int CooldownTicks { get; }
        public double SplashRadius { get; }
        public double ProjectileSpeed { get; }
        public double ProjectileRadius { get; }
        public double MaximumFlightDistance { get; }
        public double MaximumLifetimeSeconds { get; }
        public double YawSpeedDegrees { get; }
        public double PitchSpeedDegrees { get; }
        public double MinimumPitchDegrees { get; }
        public double MaximumPitchDegrees { get; }
        public double AlignmentToleranceDegrees { get; }
        public double MaximumLeadSeconds { get; }
        public double MuzzleHeight { get; }
        public double MuzzleForwardOffset { get; }
        public double MuzzleRightOffset { get; }
        public double PivotHeight { get; }
        public double AreaArcHeight { get; }
        public double PivotForwardOffset { get; }
        public double PivotRightOffset { get; }
        public double MinimumRange { get; }
        public double FireArcHalfDegrees { get; }
        public int ChargeTicks { get; }
        public int MaximumPierceTargets { get; }
        public double PierceDamageMultiplier { get; }
        public bool IsPiercing => Kind == BuildingKind.PiercingRailgun;
        public TurretDefinition(BuildingKind kind, double range, int damage, long energyPerShot,
            int cooldownTicks, double splashRadius = 0, double projectileSpeed = 20,
            double projectileRadius = .035, double maximumFlightDistance = 0, double maximumLifetimeSeconds = 3,
            double yawSpeedDegrees = 240, double pitchSpeedDegrees = 180, double minimumPitchDegrees = -65,
            double maximumPitchDegrees = 80, double alignmentToleranceDegrees = 2, double maximumLeadSeconds = .6,
            double muzzleHeight = .75, double muzzleForwardOffset = .45, double pivotHeight = .65,
            double areaArcHeight = 1.8, double muzzleRightOffset = 0, double pivotForwardOffset = 0, double pivotRightOffset = 0,
            double minimumRange = 0, double fireArcHalfDegrees = 180, int chargeTicks = 0,
            int maximumPierceTargets = 1, double pierceDamageMultiplier = 1)
        {
            if (kind != BuildingKind.Turret && kind != BuildingKind.BurstTurret && kind != BuildingKind.AreaTurret && kind != BuildingKind.PiercingRailgun)
                throw new ArgumentException("攻击配置只能绑定炮塔。");
            double[] finite = { range, splashRadius, projectileSpeed, projectileRadius, maximumFlightDistance,
                maximumLifetimeSeconds, yawSpeedDegrees, pitchSpeedDegrees, minimumPitchDegrees, maximumPitchDegrees,
                alignmentToleranceDegrees, maximumLeadSeconds, muzzleHeight, muzzleForwardOffset, pivotHeight, areaArcHeight, muzzleRightOffset, pivotForwardOffset, pivotRightOffset,
                minimumRange, fireArcHalfDegrees, pierceDamageMultiplier };
            foreach (double value in finite) if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentOutOfRangeException(nameof(range));
            if (range <= 0 || damage <= 0 || energyPerShot < 0 || cooldownTicks <= 0 || splashRadius < 0 ||
                projectileSpeed <= 0 || projectileRadius < 0 || maximumFlightDistance < 0 || maximumLifetimeSeconds <= 0 ||
                yawSpeedDegrees <= 0 || pitchSpeedDegrees <= 0 || minimumPitchDegrees < -89 || maximumPitchDegrees > 89 ||
                maximumPitchDegrees < minimumPitchDegrees || alignmentToleranceDegrees <= 0 || maximumLeadSeconds < 0 ||
                muzzleHeight <= 0 || pivotHeight < 0 || areaArcHeight < 0 || minimumRange < 0 || minimumRange >= range ||
                fireArcHalfDegrees <= 0 || fireArcHalfDegrees > 180 || chargeTicks < 0 || maximumPierceTargets < 1 || maximumPierceTargets > 100 ||
                pierceDamageMultiplier <= 0 || pierceDamageMultiplier > 1 || (kind == BuildingKind.PiercingRailgun && splashRadius > 0))
                throw new ArgumentOutOfRangeException(nameof(range));
            Kind = kind; Range = range; Damage = damage; EnergyPerShot = energyPerShot;
            CooldownTicks = cooldownTicks; SplashRadius = splashRadius; ProjectileSpeed = projectileSpeed;
            ProjectileRadius = projectileRadius; MaximumFlightDistance = maximumFlightDistance > 0 ? maximumFlightDistance : range + 3;
            MaximumLifetimeSeconds = maximumLifetimeSeconds; YawSpeedDegrees = yawSpeedDegrees; PitchSpeedDegrees = pitchSpeedDegrees;
            MinimumPitchDegrees = minimumPitchDegrees; MaximumPitchDegrees = maximumPitchDegrees;
            AlignmentToleranceDegrees = alignmentToleranceDegrees; MaximumLeadSeconds = maximumLeadSeconds;
            MuzzleHeight = muzzleHeight; MuzzleForwardOffset = muzzleForwardOffset; PivotHeight = pivotHeight;
            AreaArcHeight = areaArcHeight; MuzzleRightOffset = muzzleRightOffset;
            PivotForwardOffset = pivotForwardOffset; PivotRightOffset = pivotRightOffset;
            MinimumRange = minimumRange; FireArcHalfDegrees = fireArcHalfDegrees; ChargeTicks = chargeTicks;
            MaximumPierceTargets = maximumPierceTargets; PierceDamageMultiplier = pierceDamageMultiplier;
        }
    }
}
