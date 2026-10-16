using System;

namespace Defend.Simulation
{
    /// <summary>保存活跃建筑的多格占用。校验完整占地后才写入，避免失败移动留下半栋建筑。</summary>
    internal sealed class BuildingGrid
    {
        private readonly long[,] occupants;

        public int Width => occupants.GetLength(0);
        public int Depth => occupants.GetLength(1);
        public long Revision { get; private set; }
        internal void RestoreRevision(long revision) { Revision = revision; }

        public BuildingGrid(int width, int depth)
        {
            occupants = new long[width, depth];
        }

        public void Block(int x, int z)
        {
            if (x < 0 || z < 0 || x >= Width || z >= Depth) throw new ArgumentOutOfRangeException(nameof(x));
            occupants[x, z] = -1;
            Revision++;
        }

        public bool CanPlace(int x, int z, int width, int depth, long ignoredBuildingId = 0)
        {
            if (x < 0 || z < 0 || width > Width || depth > Depth || x > Width - width || z > Depth - depth)
                return false;
            for (int column = x; column < x + width; column++)
            for (int row = z; row < z + depth; row++)
            {
                long occupant = occupants[column, row];
                if (occupant != 0 && occupant != ignoredBuildingId)
                    return false;
            }
            return true;
        }

        public long Find(int x, int z)
        {
            return x >= 0 && z >= 0 && x < Width && z < Depth ? occupants[x, z] : 0;
        }

        public void Occupy(BuildingState building)
        {
            if (!CanPlace(building.X, building.Z, building.FootprintWidth, building.FootprintDepth, building.Id))
                throw new InvalidOperationException("建筑占地未通过校验，不能写入格子。");
            bool changed = false;
            for (int column = building.X; column < building.X + building.FootprintWidth; column++)
            for (int row = building.Z; row < building.Z + building.FootprintDepth; row++)
            {
                changed |= occupants[column, row] != building.Id;
                occupants[column, row] = building.Id;
            }
            if (changed) Revision = checked(Revision + 1);
        }

        public void Release(BuildingState building)
        {
            bool changed = false;
            for (int column = building.X; column < building.X + building.FootprintWidth; column++)
            for (int row = building.Z; row < building.Z + building.FootprintDepth; row++)
                if (occupants[column, row] == building.Id)
                {
                    occupants[column, row] = 0;
                    changed = true;
                }
            if (changed) Revision = checked(Revision + 1);
        }
    }
}
