using UnityEngine;

namespace Defend.Unity
{
    /// <summary>选中范围、候选范围和已有中继器共用同一套地面圆形几何。</summary>
    public static class RangeOutline
    {
        public static void Draw(LineRenderer line, double x, double z, double radius, bool visible)
        {
            if (line == null) return;
            line.gameObject.SetActive(visible && radius > 0);
            if (!visible || radius <= 0) return;
            for (int index = 0; index < line.positionCount; index++)
            {
                double angle = index * System.Math.PI * 2 / (line.positionCount - 1);
                line.SetPosition(index, new Vector3((float)(x + System.Math.Cos(angle) * radius), .12f, (float)(z + System.Math.Sin(angle) * radius)));
            }
        }

        public static void DrawSector(LineRenderer line, double x, double z, double minimum, double maximum,
            double facing, double halfAngle, bool visible)
        {
            if (line == null) return;
            line.gameObject.SetActive(visible);
            if (!visible) return;
            int half = (line.positionCount - 2) / 2;
            for (int i = 0; i < line.positionCount; i++)
            {
                bool outer = i <= half;
                double t = outer ? i / (double)half : (line.positionCount - 2 - i) / (double)(line.positionCount - half - 3);
                double angle = (facing - halfAngle + 2 * halfAngle * t) * System.Math.PI / 180;
                double radius = outer ? maximum : minimum;
                line.SetPosition(i, new Vector3((float)(x + System.Math.Sin(angle) * radius), .12f, (float)(z + System.Math.Cos(angle) * radius)));
            }
            // 最后一点闭合左侧边界，扇形内部空缺就是最小射程。
            line.SetPosition(line.positionCount - 1, line.GetPosition(0));
        }
    }
}
