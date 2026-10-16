using System;
namespace Defend.Simulation
{
    /// <summary>现有移动扫掠实际接受的一段路径，绝对模拟秒定位，不以首尾直线替代绕障。</summary>
    public readonly struct EnemyMotionSegment
    {
        public readonly double StartTime, EndTime, StartX, StartZ, EndX, EndZ;
        internal EnemyMotionSegment(double startTime, double endTime, double startX, double startZ, double endX, double endZ)
        { StartTime = startTime; EndTime = endTime; StartX = startX; StartZ = startZ; EndX = endX; EndZ = endZ; }
        public void Sample(double time, out double x, out double z)
        {
            double fraction = EndTime > StartTime ? Math.Max(0, Math.Min(1, (time - StartTime) / (EndTime - StartTime))) : 1;
            x = StartX + (EndX - StartX) * fraction; z = StartZ + (EndZ - StartZ) * fraction;
        }
    }
}
