using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Defend.Unity;
using Defend.PhaseFiveEffects;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>将生产模型导入规范轴向，并只替换工业防线的连射炮显示资源。</summary>
public static class RefinedTurretAssetBuilder
{
    public const string Root = "Assets/Defend/Content/Art/RapidTurretRefined";
    public const string ModelPath = Root + "/Models/RapidTurretRefined.fbx";
    public const string PrefabPath = Root + "/Prefabs/RapidTurretRefined.prefab";
    public const string CombatPrefabPath = "Assets/Defend/Content/PhaseFiveCombat/Buildings/Turret.prefab";
    private const string Evidence = "Docs/Art/RapidTurretRefined/Checks";

    private static void Folder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        Folder(Path.GetDirectoryName(path).Replace('\\', '/'));
        AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\', '/'), Path.GetFileName(path));
    }

    private static Material Material(string name, Color linearColor, float metallic, float roughness, Texture2D texture = null, float emissionStrength = 0f)
    {
        string path = Root + "/Materials/" + name + ".mat";
        var result = AssetDatabase.LoadAssetAtPath<Material>(path);
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) throw new InvalidOperationException("没有找到项目使用的 URP Lit 着色器。");
        if (result == null) { result = new Material(shader) { name = name }; AssetDatabase.CreateAsset(result, path); }
        result.shader = shader;
        result.SetTexture("_BaseMap", texture);
        result.SetColor("_BaseColor", linearColor.gamma);
        result.SetColor("_Color", linearColor.gamma);
        result.SetFloat("_Metallic", metallic);
        result.SetFloat("_Smoothness", 1 - roughness);
        result.SetFloat("_Surface", 0);
        result.SetFloat("_AlphaClip", 0);
        result.SetFloat("_Cull", 2);
        if (emissionStrength > 0f) { result.EnableKeyword("_EMISSION"); result.SetColor("_EmissionColor", linearColor.gamma * emissionStrength); }
        else { result.DisableKeyword("_EMISSION"); result.SetColor("_EmissionColor", Color.black); }
        EditorUtility.SetDirty(result);
        AssetDatabase.SaveAssetIfDirty(result);
        return result;
    }

    private sealed class MaterialDefinition
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

    private static void CopyHierarchy(Transform source, Transform target, Matrix4x4 correction, Dictionary<string, Material> materials)
    {
        var sourceFilter = source.GetComponent<MeshFilter>();
        if (sourceFilter != null)
        {
            var mesh = Object.Instantiate(sourceFilter.sharedMesh);
            mesh.name = source.name + "_UnityAxes";
            var toLocal = target.worldToLocalMatrix * correction * source.localToWorldMatrix;
            mesh.vertices = mesh.vertices.Select(toLocal.MultiplyPoint3x4).ToArray();
            var normals = toLocal.inverse.transpose;
            mesh.normals = mesh.normals.Select(n => normals.MultiplyVector(n).normalized).ToArray();
            bool reversed = toLocal.determinant < 0;
            mesh.tangents = mesh.tangents.Select(t =>
            {
                Vector3 direction = toLocal.MultiplyVector(new Vector3(t.x, t.y, t.z)).normalized;
                return new Vector4(direction.x, direction.y, direction.z, reversed ? -t.w : t.w);
            }).ToArray();
            if (reversed)
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    var indices = mesh.GetTriangles(sub);
                    for (int i = 0; i < indices.Length; i += 3) { int swap = indices[i]; indices[i] = indices[i + 2]; indices[i + 2] = swap; }
                    mesh.SetTriangles(indices, sub);
                }
            mesh.RecalculateBounds();
            string path = Root + "/Meshes/" + mesh.name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null) AssetDatabase.CreateAsset(mesh, path);
            else { EditorUtility.CopySerialized(mesh, existing); Object.DestroyImmediate(mesh); mesh = existing; EditorUtility.SetDirty(mesh); AssetDatabase.SaveAssetIfDirty(mesh); }
            target.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = target.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = source.GetComponent<Renderer>().sharedMaterials.Select(m => materials[m.name]).ToArray();
        }
        foreach (Transform child in source)
        {
            var created = new GameObject(child.name).transform;
            created.SetParent(target, false);
            created.position = correction.MultiplyPoint3x4(child.position);
            created.rotation = Quaternion.identity;
            created.localScale = Vector3.one;
            CopyHierarchy(child, created, correction, materials);
        }
    }

    private static Transform Find(Transform root, string name) => root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);
    private static Transform Marker(Transform root, string name, Vector3 position)
    {
        var marker = Find(root, name);
        if (marker == null) { marker = new GameObject(name).transform; marker.SetParent(root, false); }
        marker.localPosition = position;
        return marker;
    }

    public static object Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) throw new InvalidOperationException("请在编译完成且停止播放后导入。");
        if (!File.Exists(ModelPath)) throw new FileNotFoundException("生产版 FBX 尚未完成。", ModelPath);
        AssetDatabase.ImportAsset(Root, ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceSynchronousImport);
        Folder(Root + "/Meshes"); Folder(Root + "/Prefabs");
        var materials = CreateMaterials();
        AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceSynchronousImport);
        var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
        importer.globalScale = 1; importer.useFileScale = true; importer.bakeAxisConversion = true;
        importer.importCameras = false; importer.importLights = false; importer.importAnimation = false;
        importer.isReadable = true; importer.importNormals = ModelImporterNormals.Import;
        importer.importTangents = ModelImporterTangents.CalculateMikk;
        importer.meshCompression = ModelImporterMeshCompression.Off;
        foreach (var item in materials) importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), item.Key), item.Value);
        importer.SaveAndReimport();
        var preview = EditorSceneManager.NewPreviewScene();
        object report;
        try
        {
            var source = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath), preview);
            var forwardMarker = Find(source.transform, "ForwardReference");
            if (forwardMarker == null) throw new InvalidOperationException("生产模型缺少 ForwardReference。");
            Vector3 direction = forwardMarker.position - source.transform.position;
            if (Mathf.Abs(direction.y) > .001f) throw new InvalidOperationException("FBX 的向上轴需要重新核对。");
            var correction = Matrix4x4.Rotate(Quaternion.FromToRotation(direction.normalized, Vector3.forward));
            var target = new GameObject("RapidTurretRefined");
            SceneManager.MoveGameObjectToScene(target, preview);
            CopyHierarchy(source.transform, target.transform, correction, materials);
            var yaw = Find(target.transform, "TurretYaw"); var pitch = Find(target.transform, "BarrelPitch");
            var recoil = Find(target.transform, "BarrelRecoil"); var spin = Find(target.transform, "BarrelSpin");
            var muzzle = Find(target.transform, "Muzzle");
            if (new[] { yaw, pitch, recoil, spin, muzzle }.Any(t => t == null)) throw new InvalidOperationException("生产模型活动节点不完整。");
            if (muzzle.position.z <= 0 || muzzle.forward.z < .999f) throw new InvalidOperationException("炮口必须采用 Unity +Z 朝前。");
            var motion = target.AddComponent<RapidTurretMotion>();
            motion.Configure(yaw, pitch, recoil, spin, muzzle);
            var anchors = target.AddComponent<CombatModelAnchors>();
            anchors.TurretYaw = yaw; anchors.Muzzle = muzzle; anchors.AlternateMuzzle = null; anchors.RapidMotion = motion;
            anchors.EnergyPort = Marker(target.transform, "EnergyPort", new Vector3(0, .50f, -.23f));
            anchors.StatusAnchor = Marker(target.transform, "StatusAnchor", new Vector3(0, .73f, 0));
            Marker(target.transform, "HitAnchor", new Vector3(0, .28f, 0));
            PrefabUtility.SaveAsPrefabAsset(target, PrefabPath);
            var renders = target.GetComponentsInChildren<MeshRenderer>();
            var bounds = renders[0].bounds; foreach (var renderer in renders.Skip(1)) bounds.Encapsulate(renderer.bounds);
            report = new { unityVersion = Application.unityVersion, source = ModelPath, canonicalPrefab = PrefabPath, combatPrefab = CombatPrefabPath,
                triangles = target.GetComponentsInChildren<MeshFilter>().Sum(f => f.sharedMesh.triangles.Length / 3), renderers = renders.Length,
                materialSlots = renders.Sum(r => r.sharedMaterials.Length), materials = renders.SelectMany(r => r.sharedMaterials).Distinct().Select(m => m.name).ToArray(),
                dimensions = new[] { bounds.size.x, bounds.size.y, bounds.size.z }, muzzle = new[] { muzzle.position.x, muzzle.position.y, muzzle.position.z },
                normalizedTransforms = target.GetComponentsInChildren<Transform>().All(t => Quaternion.Angle(t.localRotation, Quaternion.identity) < .01f && Vector3.Distance(t.localScale, Vector3.one) < .0001f),
                sourceForward = new[] { direction.x, direction.y, direction.z } };
        }
        finally { EditorSceneManager.ClosePreviewScene(preview); }

        var combat = PrefabUtility.LoadPrefabContents(CombatPrefabPath);
        try
        {
            var view = combat.GetComponent<BuildingView>();
            if (view == null || view.Definition.Kind != Defend.Simulation.BuildingKind.Turret) throw new InvalidOperationException("现役连射炮预制体不正确。");
            var data = new SerializedObject(view);
            var oldVisual = (Transform)data.FindProperty("visualRoot").objectReferenceValue;
            if (oldVisual == null || oldVisual == combat.transform) throw new InvalidOperationException("不能识别原有显示节点。");
            var replacement = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), oldVisual.parent);
            replacement.transform.localPosition = oldVisual.localPosition;
            replacement.transform.localRotation = Quaternion.identity; replacement.transform.localScale = Vector3.one;
            replacement.transform.SetSiblingIndex(oldVisual.GetSiblingIndex());
            data.FindProperty("visualRoot").objectReferenceValue = replacement.transform;
            data.FindProperty("feedbackRenderer").objectReferenceValue = null;
            data.ApplyModifiedPropertiesWithoutUndo();
            Object.DestroyImmediate(oldVisual.gameObject);
            PrefabUtility.SaveAsPrefabAsset(combat, CombatPrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(combat); }
        Directory.CreateDirectory(Evidence);
        File.WriteAllText(Evidence + "/UnityImport.json", Newtonsoft.Json.JsonConvert.SerializeObject(report, Newtonsoft.Json.Formatting.Indented));
        return report;
    }
}
