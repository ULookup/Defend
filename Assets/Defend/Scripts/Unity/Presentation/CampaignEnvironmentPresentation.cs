using System;
using UnityEngine;

namespace Defend.Unity
{
    /// <summary>战役环境的纯表现消费者。完整甲板保持可建造，不创建额外禁建区。</summary>
    public sealed class CampaignEnvironmentPresentation : MonoBehaviour
    {
        [SerializeField] private CampaignCatalogAsset catalog;
        [SerializeField] private GameObject[] wallPrefabs = Array.Empty<GameObject>();
        [SerializeField] private GameObject[] sceneryPrefabs = Array.Empty<GameObject>();
        [SerializeField] private Renderer[] legacyDeckDecorations = Array.Empty<Renderer>();
        private bool[] legacyEnabled;
        private GameObject content;
        private CampaignLevelAsset shownLevel;

        public void Configure(CampaignCatalogAsset campaign, GameObject[] walls, GameObject[] scenery)
        { catalog = campaign; wallPrefabs = walls; sceneryPrefabs = scenery; }
        public void ConfigureLegacyDecorations(Renderer[] renderers) => legacyDeckDecorations = renderers;

        public void Present(CampaignLevelAsset level, GameSessionRoot session)
        {
            if (level == shownLevel && content != null) return;
            Clear();
            if (level == null) return;
            if (wallPrefabs.Length != 3) throw new InvalidOperationException("战役墙体预制体尚未配置。");
            shownLevel = level;
            legacyEnabled = new bool[legacyDeckDecorations.Length];
            for (int i = 0; i < legacyDeckDecorations.Length; i++)
            {
                var renderer = legacyDeckDecorations[i];
                if (renderer == null) continue;
                legacyEnabled[i] = renderer.enabled; renderer.enabled = false;
            }
            content = new GameObject("CampaignEnvironment_" + level.Id);
            // 网格世界使用绝对坐标，不继承安装对象的位移与缩放。
            content.transform.SetParent(transform, true);
            content.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            foreach (var placement in CampaignWallGeometry.CreatePlacements(level.WallRectangles, level.WallVariant))
            {
                var wall = Instantiate(wallPrefabs[placement.Variant - 1], placement.Position, placement.Rotation, content.transform);
                wall.name = "Wall_L" + placement.Variant;
                wall.transform.localScale = Vector3.one * CampaignWallGeometry.UniformScale;
            }
            int index = catalog == null ? -1 : catalog.IndexOf(level.Id);
            if (index >= 0 && index < sceneryPrefabs.Length && sceneryPrefabs[index] != null)
                Instantiate(sceneryPrefabs[index], Vector3.zero, Quaternion.identity, content.transform);
        }

        private void Clear()
        {
            shownLevel = null;
            if (legacyEnabled != null)
            {
                for (int i = 0; i < legacyEnabled.Length; i++)
                    if (legacyDeckDecorations[i] != null) legacyDeckDecorations[i].enabled = legacyEnabled[i];
                legacyEnabled = null;
            }
            if (content == null) return;
            content.SetActive(false);
            if (Application.isPlaying) Destroy(content); else DestroyImmediate(content);
            content = null;
        }
        private void OnDestroy() => Clear();
    }
}
