using System;
namespace Defend.Simulation
{
    public readonly struct TurretAimSnapshot
    {
        public readonly long TurretId, TargetEnemyId;
        public readonly int SourceGeneration;
        public readonly double PreviousChargeProgress, ChargeProgress;
        public readonly TurretCombatState CombatState;
        public readonly double YawDegrees, PitchDegrees, PreviousYawDegrees, PreviousPitchDegrees, StartTime, EndTime, MuzzleX, MuzzleY, MuzzleZ;
        public readonly bool IsAligned;
        internal TurretAimSnapshot(TurretAimState aim, double start, double end)
        {
            TurretId = aim.Turret.Id; TargetEnemyId = aim.Target?.Id ?? 0;
            SourceGeneration = aim.Turret.LifeGeneration;
            PreviousChargeProgress = aim.PreviousChargeProgress; ChargeProgress = aim.Turret.ChargeProgress;
            CombatState = aim.Turret.CombatState;
            YawDegrees = aim.Yaw; PitchDegrees = aim.Pitch; PreviousYawDegrees = aim.PreviousYaw; PreviousPitchDegrees = aim.PreviousPitch;
            StartTime = start; EndTime = end; MuzzleX = aim.MuzzleX; MuzzleY = aim.MuzzleY; MuzzleZ = aim.MuzzleZ; IsAligned = aim.Aligned;
        }
    }

    /// <summary>角度以正前方 +Z 为零，正俯仰朝上。模型只读取此姿态。</summary>
    internal sealed class TurretAimState
    {
        internal BuildingState Turret;
        internal EnemyState Target;
        internal double Yaw, Pitch, PreviousYaw, PreviousPitch, MuzzleX, MuzzleY, MuzzleZ, TargetX, TargetY, TargetZ;
        internal double PreviousChargeProgress;
        internal bool Aligned;
        private const double Degrees = 180 / Math.PI;
        internal void Advance(TurretDefinition weapon, double seconds)
        {
            PreviousYaw = Yaw; PreviousPitch = Pitch;
            CalculateMuzzle(weapon);
            if (Target == null) { Aligned = false; return; }
            double lead = InterceptTime(Target.X - MuzzleX, Target.Z - MuzzleZ, Target.PreviousVelocityX,
                Target.PreviousVelocityZ, weapon.ProjectileSpeed, weapon.MaximumLeadSeconds);
            TargetX = Target.X + Target.PreviousVelocityX * lead;
            TargetZ = Target.Z + Target.PreviousVelocityZ * lead;
            TargetY = weapon.SplashRadius > 0 ? 0 : Target.Definition.BodyHeight * .55;
            bool reachable = SolveDirection(weapon, out double desiredYaw, out double desiredPitch);
            reachable = reachable && desiredPitch >= weapon.MinimumPitchDegrees && desiredPitch <= weapon.MaximumPitchDegrees;
            if (weapon.FireArcHalfDegrees < 180)
            {
                double relative = AngleDelta(Turret.FacingDegrees, desiredYaw);
                reachable = reachable && Math.Abs(relative) <= weapon.FireArcHalfDegrees;
                desiredYaw = Turret.FacingDegrees + Math.Max(-weapon.FireArcHalfDegrees, Math.Min(weapon.FireArcHalfDegrees, relative));
            }
            Yaw += Math.Max(-weapon.YawSpeedDegrees * seconds, Math.Min(weapon.YawSpeedDegrees * seconds, AngleDelta(Yaw, desiredYaw)));
            double clampedPitch = Math.Max(weapon.MinimumPitchDegrees, Math.Min(weapon.MaximumPitchDegrees, desiredPitch));
            Pitch += Math.Max(-weapon.PitchSpeedDegrees * seconds, Math.Min(weapon.PitchSpeedDegrees * seconds, clampedPitch - Pitch));
            CalculateMuzzle(weapon);
            Aligned = reachable && Math.Abs(AngleDelta(Yaw, desiredYaw)) <= weapon.AlignmentToleranceDegrees &&
                Math.Abs(Pitch - desiredPitch) <= weapon.AlignmentToleranceDegrees;
        }
        internal void RecheckAlignment(TurretDefinition weapon)
        {
            double previousYaw = PreviousYaw, previousPitch = PreviousPitch;
            Advance(weapon, 0);
            PreviousYaw = previousYaw; PreviousPitch = previousPitch;
        }
        internal void CalculateMuzzle(TurretDefinition weapon) => Muzzle(weapon, Yaw, Pitch, out MuzzleX, out MuzzleY, out MuzzleZ);
        internal bool CanTarget(TurretDefinition weapon, EnemyState candidate)
        {
            if (candidate == null || !candidate.IsAlive) return false;
            double dx = candidate.X - Turret.CenterX, dz = candidate.Z - Turret.CenterZ, distance = dx * dx + dz * dz;
            if (distance < weapon.MinimumRange * weapon.MinimumRange || distance > weapon.Range * weapon.Range) return false;
            double savedX = TargetX, savedY = TargetY, savedZ = TargetZ;
            TargetX = candidate.X; TargetY = candidate.Definition.BodyHeight * .55; TargetZ = candidate.Z;
            bool reachable = SolveDirection(weapon, out double yaw, out double pitch);
            TargetX = savedX; TargetY = savedY; TargetZ = savedZ;
            return reachable && pitch >= weapon.MinimumPitchDegrees && pitch <= weapon.MaximumPitchDegrees &&
                Math.Abs(AngleDelta(Turret.FacingDegrees, yaw)) <= weapon.FireArcHalfDegrees;
        }
        private bool SolveDirection(TurretDefinition weapon, out double yaw, out double pitch)
        {
            yaw = Yaw; pitch = Pitch;
            double dx = TargetX - Turret.CenterX, dz = TargetZ - Turret.CenterZ;
            double horizontalSquared = dx * dx + dz * dz;
            double right = weapon.MuzzleRightOffset;
            if (horizontalSquared <= right * right + .00000001) return false;

            // 将目标转换到炮塔局部平面，一次求出炮口射线穿过目标的姿态。
            // 近距离反复用旧炮口重算方向会振荡，甚至把朝天的炮管误判为已对准。
            yaw = (Math.Atan2(dx, dz) - Math.Asin(right / Math.Sqrt(horizontalSquared))) * Degrees;
            double forward = Math.Sqrt(horizontalSquared - right * right) - weapon.PivotForwardOffset;
            // 范围弹道的起始切线包含抛物线抬升项，与 ProjectileState.Position 一致。
            double height = TargetY + (weapon.SplashRadius > 0 ? 4 * weapon.AreaArcHeight : 0) - weapon.PivotHeight;
            double vertical = weapon.MuzzleHeight - weapon.PivotHeight;
            double radiusSquared = forward * forward + height * height;
            if (radiusSquared <= vertical * vertical + .00000001) return false;
            pitch = (Math.Atan2(height, forward) - Math.Asin(vertical / Math.Sqrt(radiusSquared))) * Degrees;
            // 目标已在炮口后方时不能沿反方向发射，保留瞄准但等待可达目标。
            double forwardDistance = Math.Sqrt(radiusSquared - vertical * vertical) -
                (weapon.MuzzleForwardOffset - weapon.PivotForwardOffset);
            return forwardDistance > .000001;
        }
        private void Muzzle(TurretDefinition weapon, double yaw, double pitch, out double x, out double y, out double z)
        {
            double sine = Math.Sin(pitch / Degrees), cosine = Math.Cos(pitch / Degrees);
            double vertical = weapon.MuzzleHeight - weapon.PivotHeight;
            double forward = weapon.PivotForwardOffset + (weapon.MuzzleForwardOffset - weapon.PivotForwardOffset) * cosine - vertical * sine;
            y = weapon.PivotHeight + (weapon.MuzzleForwardOffset - weapon.PivotForwardOffset) * sine + vertical * cosine;
            x = Turret.CenterX + forward * Math.Sin(yaw / Degrees) + weapon.MuzzleRightOffset * Math.Cos(yaw / Degrees);
            z = Turret.CenterZ + forward * Math.Cos(yaw / Degrees) - weapon.MuzzleRightOffset * Math.Sin(yaw / Degrees);
        }
        internal static double AngleDelta(double from, double to) => ((to - from + 540) % 360 + 360) % 360 - 180;
        private static double InterceptTime(double x, double z, double vx, double vz, double speed, double maximum)
        {
            double a = vx * vx + vz * vz - speed * speed, b = 2 * (x * vx + z * vz), c = x * x + z * z;
            double result = double.PositiveInfinity;
            if (Math.Abs(a) < .000001) { if (Math.Abs(b) > .000001 && -c / b > 0) result = -c / b; }
            else
            {
                double discriminant = b * b - 4 * a * c;
                if (discriminant >= 0)
                {
                    double root = Math.Sqrt(discriminant), first = (-b - root) / (2 * a), second = (-b + root) / (2 * a);
                    if (first > 0) result = first;
                    if (second > 0) result = Math.Min(result, second);
                }
            }
            return double.IsInfinity(result) ? 0 : Math.Min(maximum, result);
        }
    }
}
