using System;
using System.Collections.Generic;

namespace Defend.Simulation
{
    /// <summary>静态墙体的实体边界。通行占格仍由 BlockedCells 独立表达，数值与可见墙体同源。</summary>
    public readonly struct WallDefinition
    {
        public double X { get; }
        public double Z { get; }
        public double Width { get; }
        public double Depth { get; }
        public double Height { get; }
        public double BaseHeight { get; }
        public double MinX => X;
        public double MinY => BaseHeight;
        public double MinZ => Z;
        public double MaxX => X + Width;
        public double MaxY => BaseHeight + Height;
        public double MaxZ => Z + Depth;
        internal bool IsValid => Finite(X) && Finite(Z) && Finite(BaseHeight) &&
            Finite(Width) && Finite(Depth) && Finite(Height) && Width > 0 && Depth > 0 && Height > 0 &&
            Finite(MaxX) && Finite(MaxY) && Finite(MaxZ);

        public WallDefinition(double x, double z, double width, double depth, double height, double baseHeight = 0)
        {
            X = x; Z = z; Width = width; Depth = depth; Height = height; BaseHeight = baseHeight;
            if (!IsValid) throw new ArgumentOutOfRangeException(nameof(width), "墙体必须具有有限且为正的实体尺寸。");
        }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    /// <summary>以统一参数求直线或二次高度轨迹的首次接触，不用逻辑步端点连线代替抛物线。</summary>
    internal static class WallCollision
    {
        private const double Epsilon = 1e-9;
        internal static bool FirstContact(IReadOnlyList<WallDefinition> walls,
            double x, double y, double z, double velocityX, double velocityY, double velocityZ, double quadraticY,
            double from, double to, double radius, out double contact)
        {
            contact = to;
            bool found = false;
            foreach (var wall in walls)
            {
                double enter = from, leave = contact;
                if (!Clip(x, velocityX, wall.MinX - radius, wall.MaxX + radius, ref enter, ref leave) ||
                    !Clip(z, velocityZ, wall.MinZ - radius, wall.MaxZ + radius, ref enter, ref leave)) continue;
                double minimum = wall.MinY - radius, maximum = wall.MaxY + radius;
                double height = y + velocityY * enter + quadraticY * enter * enter;
                if (height >= minimum - Epsilon && height <= maximum + Epsilon)
                { contact = enter; found = true; continue; }
                double earliest = double.PositiveInfinity;
                Boundary(y - minimum, velocityY, quadraticY, enter, leave, ref earliest);
                Boundary(y - maximum, velocityY, quadraticY, enter, leave, ref earliest);
                if (double.IsPositiveInfinity(earliest)) continue;
                contact = earliest; found = true;
            }
            return found;
        }

        private static bool Clip(double origin, double velocity, double minimum, double maximum, ref double enter, ref double leave)
        {
            if (Math.Abs(velocity) < Epsilon) return origin >= minimum - Epsilon && origin <= maximum + Epsilon;
            double first = (minimum - origin) / velocity, second = (maximum - origin) / velocity;
            enter = Math.Max(enter, Math.Min(first, second));
            leave = Math.Min(leave, Math.Max(first, second));
            return enter <= leave + Epsilon;
        }

        private static void Boundary(double constant, double linear, double quadratic, double enter, double leave, ref double earliest)
        {
            if (Math.Abs(quadratic) < Epsilon)
            {
                if (Math.Abs(linear) >= Epsilon) Candidate(-constant / linear, enter, leave, ref earliest);
                return;
            }
            double discriminant = linear * linear - 4 * quadratic * constant;
            if (discriminant < -Epsilon) return;
            double root = Math.Sqrt(Math.Max(0, discriminant));
            Candidate((-linear - root) / (2 * quadratic), enter, leave, ref earliest);
            Candidate((-linear + root) / (2 * quadratic), enter, leave, ref earliest);
        }

        private static void Candidate(double value, double enter, double leave, ref double earliest)
        {
            if (value >= enter - Epsilon && value <= leave + Epsilon)
                earliest = Math.Min(earliest, Math.Max(enter, Math.Min(leave, value)));
        }
    }
}
