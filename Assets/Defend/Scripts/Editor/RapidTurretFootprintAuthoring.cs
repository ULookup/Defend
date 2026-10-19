using System;
using System.Linq;
using Defend.Simulation;
using Defend.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Defend.Editor
{
    /// <summary>定向迁移正式速射炮及现役实例，不重建场景或修改旧关卡的共享预制体。</summary>
    public static class RapidTurretFootprintAuthoring
    {
        private const string DefinitionPath = ConfirmedDefenseAuthoring.Root + "/Turret.asset";
        private const string PrefabPath = ConfirmedDefenseAuthoring.Root + "/Buildings/Turret.prefab";
        private const string OldHelp = "电脑使用中键拖动与滚轮缩放；触屏使用拖动与双指缩放。";
        private const string NewHelp = "电脑使用中键拖动、滚轮缩放，Q/E旋转，WASD平移，Home返回基地视角；拉近时视角自动降低。触屏使用拖动与双指缩放。";

        [MenuItem("涌现防线/接入速射炮四格占地与镜头帮助")]
        public static void Integrate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先结束播放。");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("打开的场景有未保存修改，未执行迁移。");
            var scene = SceneManager.GetActiveScene();
            if (scene.path != PhaseFiveCombatSceneBuilder.ScenePath) scene = EditorSceneManager.OpenScene(PhaseFiveCombatSceneBuilder.ScenePath);
            IntegrateScene(scene);
            UpdateHelpPrefab();
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("速射炮迁移后未能保存场景。");
        }

        public static void IntegrateScene(Scene scene)
        {
            var definition = AssetDatabase.LoadAssetAtPath<BuildingDefinitionAsset>(DefinitionPath);
            if (definition == null) throw new InvalidOperationException("缺少正式速射炮定义。");
            var world = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<WorldPresentation>(true)).Single();
            var placed = world.AuthoredBuildings.Where(x => x.Definition.Kind == BuildingKind.Turret).ToArray();
            // 半整数是原有一格中心；已迁移的整数中心保持不动，避免重跑继续偏移。
            var positions = placed.Select(x => new Vector3(MigratedCenter(x.transform.position.x), x.transform.position.y, MigratedCenter(x.transform.position.z))).ToArray();
            var data = new SerializedObject(definition);
            data.FindProperty("width").intValue = 2;
            data.FindProperty("depth").intValue = 2;
            data.ApplyModifiedPropertiesWithoutUndo();

            var contents = PrefabUtility.LoadPrefabContents(PrefabPath);
            try { ApplyGeometry(contents.GetComponent<BuildingView>(), definition); PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath); }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath).GetComponent<BuildingView>();
            var worldData = new SerializedObject(world);
            var prefabs = worldData.FindProperty("buildingPrefabs");
            for (int i = 0; i < prefabs.arraySize; i++)
            {
                var view = (BuildingView)prefabs.GetArrayElementAtIndex(i).objectReferenceValue;
                if (view.Definition.Kind == BuildingKind.Turret) prefabs.GetArrayElementAtIndex(i).objectReferenceValue = source;
            }
            worldData.ApplyModifiedPropertiesWithoutUndo(); Record(world);
            for (int i = 0; i < placed.Length; i++)
            {
                Vector3 oldPosition = placed[i].transform.position;
                if (positions[i] != oldPosition) MoveFloorPad(scene, oldPosition, positions[i]);
                placed[i].transform.position = positions[i]; Record(placed[i].transform);
                ApplyGeometry(placed[i], definition);
            }

            var session = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<GameSessionRoot>(true)).Single();
            var sessionData = new SerializedObject(session);
            var settings = (BattleSettingsAsset)sessionData.FindProperty("battleSettings").objectReferenceValue;
            SynchronizeMuzzle(source, settings);
            var ghost = PlacementPreviewAuthoring.UpdateBuildingGhost(source);
            var preview = (PlacementPresentation)sessionData.FindProperty("placementPresentation").objectReferenceValue;
            if (preview == null) throw new InvalidOperationException("场景缺少摆放预览，请先接入建筑摆放预览。");
            var previewData = new SerializedObject(preview);
            var ghosts = previewData.FindProperty("ghostPrefabs");
            bool replaced = false;
            for (int i = 0; i < ghosts.arraySize; i++)
                if (((PlacementGhostView)ghosts.GetArrayElementAtIndex(i).objectReferenceValue).Kind == BuildingKind.Turret)
                { ghosts.GetArrayElementAtIndex(i).objectReferenceValue = ghost; replaced = true; }
            if (!replaced) { ghosts.arraySize++; ghosts.GetArrayElementAtIndex(ghosts.arraySize - 1).objectReferenceValue = ghost; }
            previewData.ApplyModifiedPropertiesWithoutUndo(); Record(preview);
            foreach (var root in scene.GetRootGameObjects()) UpdateHelp(root);
            Debug.Log($"速射炮已接入 2×2 占地与模型等比例两倍，更新现役实例 {placed.Length} 座；旧关卡共享预制体保持原尺寸。");
        }

        public static void ApplyGeometry(BuildingView view, BuildingDefinitionAsset definition)
        {
            var data = new SerializedObject(view);
            data.FindProperty("definition").objectReferenceValue = definition;
            data.FindProperty("authoredVisualScale").vector3Value = Vector3.one * 2;
            view.VisualRoot.localScale = Vector3.one * 2; Record(view.VisualRoot);
            Bounds bounds = ModelBounds(view);
            float energyHeight = Mathf.Max(1.5f, bounds.max.y + .18f);
            data.FindProperty("energyBarHeight").floatValue = energyHeight;
            var selection = (GameObject)data.FindProperty("selectionRoot").objectReferenceValue;
            selection.transform.localScale = new Vector3(2.02f, .035f, 2.02f); Record(selection.transform);
            var collider = view.GetComponent<BoxCollider>() ?? view.gameObject.AddComponent<BoxCollider>();
            collider.center = bounds.center;
            collider.size = new Vector3(Mathf.Max(2, bounds.size.x), Mathf.Max(.1f, bounds.size.y), Mathf.Max(2, bounds.size.z));
            Record(collider);
            var health = (HealthBarView)data.FindProperty("healthBar").objectReferenceValue;
            if (health != null) SetHeight(health.transform, Mathf.Max(health.transform.localPosition.y, energyHeight + .25f));
            var fill = (Transform)data.FindProperty("energyFill").objectReferenceValue;
            if (fill != null) SetHeight(fill, energyHeight);
            var background = view.transform.Find("EnergyBackground");
            if (background != null) SetHeight(background, energyHeight);
            data.ApplyModifiedPropertiesWithoutUndo(); Record(view);
        }

        private static Bounds ModelBounds(BuildingView view)
        {
            Bounds result = default;
            bool any = false;
            foreach (var filter in view.VisualRoot.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                Bounds mesh = filter.sharedMesh.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    var point = mesh.center + Vector3.Scale(mesh.extents, new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                    point = view.transform.InverseTransformPoint(filter.transform.TransformPoint(point));
                    if (!any) { result = new Bounds(point, Vector3.zero); any = true; } else result.Encapsulate(point);
                }
            }
            if (!any) throw new InvalidOperationException("速射炮模型没有可计算的网格。");
            return result;
        }

        private static void SynchronizeMuzzle(BuildingView source, BattleSettingsAsset settings)
        {
            var anchors = source.ModelAnchors;
            if (anchors == null || anchors.Muzzle == null) throw new InvalidOperationException("速射炮缺少炮口节点。");
            Vector3 muzzle = source.transform.InverseTransformPoint(anchors.Muzzle.position);
            var pivot = anchors.TurretPitch != null ? anchors.TurretPitch : anchors.TurretYaw;
            if (pivot == null) throw new InvalidOperationException("速射炮缺少旋转枢轴。");
            Vector3 center = source.transform.InverseTransformPoint(pivot.position);
            settings.ConfigureTurretMuzzle(BuildingKind.Turret, muzzle.y, muzzle.z, center.y, muzzle.x, center.z, center.x);
            EditorUtility.SetDirty(settings);
            Debug.Log($"速射炮几何：模型局部缩放 {source.VisualRoot.localScale}，炮口 {muzzle}，枢轴 {center}，模型边界 {ModelBounds(source)}");
        }

        private static float MigratedCenter(float coordinate)
        {
            if (Mathf.Abs(coordinate - Mathf.Round(coordinate)) < .001f) return Mathf.Round(coordinate);
            if (Mathf.Abs(coordinate - Mathf.Floor(coordinate) - .5f) < .001f) return Mathf.Floor(coordinate) + 1;
            throw new InvalidOperationException($"速射炮原位置 {coordinate} 未对齐网格，不能自动迁移。");
        }
        private static void MoveFloorPad(Scene scene, Vector3 oldPosition, Vector3 position)
        {
            foreach (var tile in scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Transform>(true)))
                if (tile.name == "IndustrialFloor" && Mathf.Abs(tile.position.x - oldPosition.x) < .001f && Mathf.Abs(tile.position.z - oldPosition.z) < .001f)
                { tile.position = new Vector3(position.x, tile.position.y, position.z); tile.localScale = new Vector3(2, tile.localScale.y, 2); Record(tile); }
        }
        private static void SetHeight(Transform target, float height)
        { var position = target.localPosition; position.y = height; target.localPosition = position; Record(target); }
        private static void Record(UnityEngine.Object target)
        { EditorUtility.SetDirty(target); if (PrefabUtility.IsPartOfPrefabInstance(target)) PrefabUtility.RecordPrefabInstancePropertyModifications(target); }
        private static void UpdateHelp(GameObject root)
        {
            foreach (var text in root.GetComponentsInChildren<Text>(true))
            {
                string updated = text.text.Replace(OldHelp, NewHelp).Replace("准备暂停时可收起菜单继续布置，开波会解除暂停。", "暂停时可收起菜单继续观察；准备阶段还可继续布置，开波会解除暂停。");
                if (updated != text.text) { text.text = updated; Record(text); }
            }
        }
        private static void UpdateHelpPrefab()
        {
            string path = DefenseInterfaceAuthoring.PrefabPath;
            var root = PrefabUtility.LoadPrefabContents(path);
            try { UpdateHelp(root); PrefabUtility.SaveAsPrefabAsset(root, path); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }
}
