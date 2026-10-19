using System;
using System.Collections.Generic;
using Defend.Simulation;
using UnityEngine;

namespace Defend.Unity
{
    public readonly struct CampaignWallPlacement
    {
        public readonly Vector3 Position;
        public readonly int Variant;
        public readonly bool AlongZ;
        public Quaternion Rotation => Quaternion.Euler(0, AlongZ ? 90 : 0, 0);
        public CampaignWallPlacement(Vector3 position, int variant, bool alongZ)
        { Position = position; Variant = variant; AlongZ = alongZ; }
    }

    public static class CampaignWallGeometry
    {
        public const float UniformScale = .5f;
        public const float SegmentPitch = 2f;
        public static List<CampaignWallPlacement> CreatePlacements(IReadOnlyList<CampaignWallRectangle> rectangles, int defaultVariant)
        {
            var result = new List<CampaignWallPlacement>();
            foreach (var rectangle in rectangles)
            {
                bool alongZ = rectangle.Width == 1;
                int length = alongZ ? rectangle.Depth : rectangle.Width;
                if ((!alongZ && rectangle.Depth != 1) || length < 2 || length % 2 != 0)
                    throw new ArgumentException("战役墙体必须为偶数格长度、1 格逻辑厚度的直墙。");
                int variant = rectangle.Variant == 0 ? defaultVariant : rectangle.Variant;
                if (variant < 1 || variant > 3) throw new ArgumentOutOfRangeException(nameof(defaultVariant));
                for (int offset = 0; offset < length; offset += 2)
                    result.Add(new CampaignWallPlacement(new Vector3(
                        rectangle.X + (alongZ ? .5f : offset + 1f), 0,
                        rectangle.Z + (alongZ ? offset + 1f : .5f)), variant, alongZ));
            }
            return result;
        }

        public static List<WallDefinition> CreateWalls(IReadOnlyList<CampaignWallRectangle> rectangles, int defaultVariant)
        {
            var geometry = Resources.Load<CampaignWallGeometryAsset>("CampaignWallGeometry");
            if (geometry == null) throw new InvalidOperationException("请先制作战役墙体资源，导出实际模型包围盒。");
            var result = new List<WallDefinition>();
            foreach (var placement in CreatePlacements(rectangles, defaultVariant))
            {
                Vector3 size = geometry.GetNormalizedSize(placement.Variant) * UniformScale;
                double width = placement.AlongZ ? size.z : size.x;
                double depth = placement.AlongZ ? size.x : size.z;
                result.Add(new WallDefinition(placement.Position.x - width * .5,
                    placement.Position.z - depth * .5, width, depth, size.y));
            }
            return result;
        }
    }
}
