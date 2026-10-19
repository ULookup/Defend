using System;
using System.Collections.Generic;
using UnityEngine;

namespace Defend.Unity
{
    [CreateAssetMenu(menuName = "涌现防线/战役目录", fileName = "CampaignCatalog")]
    public sealed class CampaignCatalogAsset : ScriptableObject
    {
        [SerializeField] private CampaignLevelAsset[] levels = Array.Empty<CampaignLevelAsset>();
        public IReadOnlyList<CampaignLevelAsset> Levels => levels;
        public int IndexOf(string id)
        { for (int i = 0; i < levels.Length; i++) if (levels[i] != null && levels[i].Id == id) return i; return -1; }
        public void Configure(CampaignLevelAsset[] values)
        {
            if (values == null || values.Length == 0) throw new ArgumentException("战役至少需要一关。");
            var ids = new HashSet<string>();
            foreach (var level in values)
                if (level == null || !ids.Add(level.Id)) throw new ArgumentException("战役关卡缺失或标识重复。");
            levels = (CampaignLevelAsset[])values.Clone();
        }
    }
}
