using System;
using UnityEngine;

namespace Defend.Unity
{
    /// <summary>由导入模型实际 Renderer.bounds 生成；模型与模拟共用，不包含地板或装饰。</summary>
    public sealed class CampaignWallGeometryAsset : ScriptableObject
    {
        [SerializeField] private Bounds[] importedBounds = Array.Empty<Bounds>();
        public Bounds GetImportedBounds(int variant)
        {
            if (variant < 1 || variant > importedBounds.Length) throw new ArgumentOutOfRangeException(nameof(variant));
            return importedBounds[variant - 1];
        }
        public Vector3 GetNormalizedSize(int variant) => GetImportedBounds(variant).size;
        public void Configure(Bounds[] values) => importedBounds = (Bounds[])values.Clone();
    }
}
