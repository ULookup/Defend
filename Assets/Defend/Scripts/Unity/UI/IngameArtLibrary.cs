using System;
using System.Collections.Generic;
using UnityEngine;

namespace Defend.Unity
{
    /// <summary>生图图标与底图索引；普通文字使用字体，资源库不保存玩法状态。</summary>
    public sealed class IngameArtLibrary : ScriptableObject
    {
        [Serializable] public struct Entry { public string key; public Sprite sprite; }
        [SerializeField] private Entry[] entries = Array.Empty<Entry>();
        private Dictionary<string, Sprite> lookup;
        public Sprite Find(string key)
        {
            if (lookup == null)
            {
                lookup = new Dictionary<string, Sprite>(StringComparer.Ordinal);
                foreach (var entry in entries) if (!string.IsNullOrEmpty(entry.key)) lookup[entry.key] = entry.sprite;
            }
            return key != null && lookup.TryGetValue(key, out var sprite) ? sprite : null;
        }
        private void OnEnable() { lookup = null; }
    }
}
