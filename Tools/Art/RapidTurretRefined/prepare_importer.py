"""Create the refined asset importer from the established project workflow."""
from pathlib import Path

root = Path(__file__).resolve().parents[3]
text = (root / 'Tools/Art/RapidTurret/RapidTurretAssetBuilder.cs').read_text(encoding='utf-8-sig')
text = text.replace('RapidTurretAssetBuilder', 'RefinedTurretAssetBuilder')
text = text.replace('Assets/Defend/Content/Art/RapidTurret"', 'Assets/Defend/Content/Art/RapidTurretRefined"')
text = text.replace('/Models/RapidTurret.fbx', '/Models/RapidTurretRefined.fbx')
text = text.replace('/Prefabs/RapidTurret.prefab', '/Prefabs/RapidTurretRefined.prefab')
text = text.replace('Docs/Art/RapidTurret/Checks', 'Docs/Art/RapidTurretRefined/Checks')
text = text.replace('new GameObject("RapidTurret")', 'new GameObject("RapidTurretRefined")')
text = text.replace('bool emission = false', 'float emissionStrength = 0f')
text = text.replace('if (emission) {', 'if (emissionStrength > 0f) {')
text = text.replace('linearColor.gamma * .8f', 'linearColor.gamma * emissionStrength')
start = text.index('    private static Dictionary<string, Material> CreateMaterials()')
end = text.index('    private static void CopyHierarchy', start)
text = text[:start] + '''    private sealed class MaterialDefinition
    {
        public string name { get; set; }
        public float[] baseColor { get; set; }
        public float metallic { get; set; }
        public float roughness { get; set; }
        public float emissionStrength { get; set; }
    }

    private static Dictionary<string, Material> CreateMaterials()
    {
        Folder(Root + "/Materials");
        var manifest = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Evidence + "/AssetManifest.json"));
        var definitions = manifest["materials"].ToObject<MaterialDefinition[]>();
        if (definitions == null || definitions.Length == 0 || definitions.Length > 5)
            throw new InvalidOperationException("材质清单需要包含一至五种明确的材质。");
        var result = new Dictionary<string, Material>();
        foreach (var item in definitions)
        {
            if (item.baseColor == null || item.baseColor.Length != 4) throw new InvalidOperationException("材质颜色需要四个线性分量。");
            var color = new Color(item.baseColor[0], item.baseColor[1], item.baseColor[2], item.baseColor[3]);
            result.Add(item.name, Material(item.name, color, item.metallic, item.roughness, null, item.emissionStrength));
        }
        return result;
    }

''' + text[end:]
text = text.replace('new Vector3(0, .57f, -.14f)', 'new Vector3(0, .50f, -.23f)')
text = text.replace('new Vector3(0, .66f, 0)', 'new Vector3(0, .73f, 0)')
(root / 'Tools/Art/RapidTurretRefined/RefinedTurretAssetBuilder.cs').write_text(text, encoding='utf-8')
print('Created RefinedTurretAssetBuilder.cs')
