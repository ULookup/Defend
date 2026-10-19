using System;
using System.Collections.Generic;
using Defend.Simulation;
using UnityEngine;

namespace Defend.Unity
{
    [Serializable]
    public struct CampaignWallRectangle
    {
        [SerializeField] private int x, z, width, depth, variant;
        public int X => x;
        public int Z => z;
        public int Width => width;
        public int Depth => depth;
        public int Variant => variant;
        public CampaignWallRectangle(int x, int z, int width, int depth, int variant = 0)
        { this.x = x; this.z = z; this.width = width; this.depth = depth; this.variant = variant; }
    }

    [CreateAssetMenu(menuName = "涌现防线/战役关卡", fileName = "CampaignLevel")]
    public sealed class CampaignLevelAsset : ScriptableObject
    {
        [SerializeField] private string id, displayName;
        [SerializeField, TextArea] private string description;
        [SerializeField] private int startingCredits, wallVariant = 1;
        [SerializeField] private Vector2Int coreOrigin;
        [SerializeField] private BattleSettingsAsset battleSettings;
        [SerializeField] private Sprite preview;
        [SerializeField] private CampaignWallRectangle[] wallRectangles = Array.Empty<CampaignWallRectangle>();
        public string Id => id;
        public string DisplayName => displayName;
        public string Description => description;
        public int StartingCredits => startingCredits;
        public int WallVariant => wallVariant;
        public int WaveCount => battleSettings == null ? 0 : battleSettings.WaveCount;
        public Vector2Int CoreOrigin => coreOrigin;
        public BattleSettingsAsset BattleSettings => battleSettings;
        public Sprite Preview => preview;
        public IReadOnlyList<CampaignWallRectangle> WallRectangles => wallRectangles;
        public WorldSettings CreateWorldSettings(int ticksPerSecond) => new WorldSettings(40, 40, ticksPerSecond, startingCredits);
        public void Configure(string identifier, string title, string summary, int credits, Vector2Int core,
            BattleSettingsAsset battle, int variant, CampaignWallRectangle[] walls, Sprite image)
        {
            if (string.IsNullOrWhiteSpace(identifier) || battle == null) throw new ArgumentException("关卡标识与战斗配置不可为空。");
            id = identifier; displayName = title; description = summary; startingCredits = credits;
            coreOrigin = core; battleSettings = battle; wallVariant = variant;
            wallRectangles = walls ?? Array.Empty<CampaignWallRectangle>(); preview = image;
        }
    }
}
