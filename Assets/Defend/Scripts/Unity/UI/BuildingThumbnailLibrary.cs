using System;
using Defend.Simulation;
using UnityEngine;

namespace Defend.Unity
{
    public enum BuildingThumbnailContext { Shop, Detail }

    /// <summary>Persistent art assignments use gameplay kinds, never card order or localized building names.</summary>
    [CreateAssetMenu(menuName = "Defend/UI/Building Thumbnail Library")]
    public sealed class BuildingThumbnailLibrary : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public BuildingKind kind;
            public Sprite shopSprite;
            public Sprite detailSprite;
        }

        [SerializeField] private Entry[] entries = Array.Empty<Entry>();

        public Sprite Find(BuildingKind kind, BuildingThumbnailContext context)
        {
            if (entries == null) return null;
            foreach (Entry entry in entries)
            {
                if (entry.kind != kind) continue;
                Sprite preferred = context == BuildingThumbnailContext.Detail ? entry.detailSprite : entry.shopSprite;
                return preferred != null ? preferred : context == BuildingThumbnailContext.Detail ? entry.shopSprite : entry.detailSprite;
            }
            return null;
        }

        public void Configure(Entry[] assignments)
        {
            entries = assignments != null ? (Entry[])assignments.Clone() : Array.Empty<Entry>();
        }
    }
}
