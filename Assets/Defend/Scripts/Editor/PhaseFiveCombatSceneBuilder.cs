using System;
using System.Collections.Generic;
using System.Linq;
using Defend.Simulation;
using Defend.Unity;
using Defend.PhaseFiveEffects;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Defend.Editor
{
    public static class PhaseFiveCombatSceneBuilder
    {
        public const string ScenePath = "Assets/Defend/Content/Scenes/Dev/IndustrialDefense.unity";
        public const string Content = "Assets/Defend/Content/PhaseFiveCombat";
        private const string Models = "Assets/Defend/Content/Art/PhaseFiveEffects/Prefabs/Models/";
        private const string Environment = "Assets/Defend/Content/Art/PhaseFive/Prefabs/";
        private const string RapidModel = "Assets/Defend/Content/Art/FuxiTurret/Prefabs/FuxiTurret.prefab";

        [MenuItem("涌现防线/创建或打开工业防线关卡")]
        public static void CreateOrOpen()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出播放模式。");
            if (SceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("当前场景有未保存修改，请先保存。");
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null) { EditorSceneManager.OpenScene(ScenePath); return; }
            WhiteboxSceneBuilder.EnsureFolder(Content + "/Buildings");
            WhiteboxSceneBuilder.EnsureFolder(Content + "/Enemies");
            if (!AssetDatabase.CopyAsset(PhaseFourSceneBuilder.CombatScenePath, ScenePath)) throw new InvalidOperationException("复制能源防线失败。");
            var scene = EditorSceneManager.OpenScene(ScenePath);
            var session = Find<GameSessionRoot>(scene);
            var world = Find<WorldPresentation>(scene);
            var battle = Find<BattlePresentation>(scene);
            var worldData = new SerializedObject(world);
            var prefabs = worldData.FindProperty("buildingPrefabs");
            var byKind = new Dictionary<BuildingKind, BuildingView>();
            for (int i = 0; i < prefabs.arraySize; i++)
            {
                var source = (BuildingView)prefabs.GetArrayElementAtIndex(i).objectReferenceValue;
                var replacement = BuildingPrefab(source);
                byKind.Add(replacement.Definition.Kind, replacement);
                prefabs.GetArrayElementAtIndex(i).objectReferenceValue = replacement;
            }
            var placed = worldData.FindProperty("authoredBuildings");
            for (int i = 0; i < placed.arraySize; i++)
            {
                var old = (BuildingView)placed.GetArrayElementAtIndex(i).objectReferenceValue;
                var replacement = (GameObject)PrefabUtility.InstantiatePrefab(byKind[old.Definition.Kind].gameObject, old.transform.parent);
                replacement.transform.SetPositionAndRotation(old.transform.position, old.transform.rotation);
                replacement.name = old.Definition.DisplayName;
                placed.GetArrayElementAtIndex(i).objectReferenceValue = replacement.GetComponent<BuildingView>();
                Object.DestroyImmediate(old.gameObject);
            }
            worldData.FindProperty("externalTransferEffects").boolValue = true;
            worldData.ApplyModifiedPropertiesWithoutUndo();

            var battleData = new SerializedObject(battle);
            var sourceEnemy = (EnemyView)battleData.FindProperty("enemyPrefab").objectReferenceValue;
            Set(battleData, "enemyPrefab", EnemyPrefab(sourceEnemy, "StandardEnemy"));
            Set(battleData, "fastEnemyPrefab", EnemyPrefab(sourceEnemy, "FastEnemy"));
            Set(battleData, "heavyEnemyPrefab", EnemyPrefab(sourceEnemy, "HeavyEnemy"));
            battleData.FindProperty("externalCombatEffects").boolValue = true;
            battleData.ApplyModifiedPropertiesWithoutUndo();

            var effectRoot = new GameObject("工业战斗反馈", typeof(EffectPool), typeof(CombatArtPresentation));
            var pool = effectRoot.GetComponent<EffectPool>();
            pool.Library = Require<EffectLibrary>("Assets/Defend/Content/Art/PhaseFiveEffects/EffectLibrary.asset");
            pool.AutomaticAdvance = false;
            var adapter = effectRoot.GetComponent<CombatArtPresentation>();
            var adapterData = new SerializedObject(adapter); Set(adapterData, "pool", pool); adapterData.ApplyModifiedPropertiesWithoutUndo();
            var sessionData = new SerializedObject(session); Set(sessionData, "combatArt", adapter); sessionData.ApplyModifiedPropertiesWithoutUndo();
            DressEnvironment(scene, world.AuthoredBuildings);
            foreach (var text in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Text>(true)))
            {
                if (text.name == "MapTitle") text.text = "工业防线";
                if (text.name == "MapHelp") text.text = "西侧双入口 · 保护供电节点 · 滚轮缩放 · 中键拖动";
                PrefabUtility.RecordPrefabInstancePropertyModifications(text);
            }
            var viewportData = new SerializedObject(Find<WorldCameraViewport>(scene));
            viewportData.FindProperty("zoomFactor").floatValue = .4f;
            viewportData.ApplyModifiedPropertiesWithoutUndo();
            var camera = (Camera)viewportData.FindProperty("worldCamera").objectReferenceValue;
            camera.name = "主摄像机 · 60度透视俯视";
            camera.orthographic = false;
            camera.fieldOfView = 50;
            camera.transform.rotation = Quaternion.Euler(60, 0, 0);
            camera.transform.position = new Vector3(26, 0, 20) - camera.transform.forward * (8.4f / Mathf.Tan(25 * Mathf.Deg2Rad));
            CombatImpactAuthoring.IntegrateScene(scene); PlacementPreviewAuthoring.IntegrateScene(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("工业防线未能保存。");
            AssetDatabase.SaveAssets();
        }

        private static BuildingView BuildingPrefab(BuildingView source)
        {
            string path = Content + "/Buildings/" + source.Definition.Kind + ".prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing.GetComponent<BuildingView>();
            var root = Object.Instantiate(source.gameObject);
            try
            {
                root.name = source.Definition.DisplayName;
                var view = root.GetComponent<BuildingView>();
                var data = new SerializedObject(view);
                var old = (Transform)data.FindProperty("visualRoot").objectReferenceValue;
                var feedback = (Renderer)data.FindProperty("feedbackRenderer").objectReferenceValue;
                Set(data, "feedbackRenderer", null); data.ApplyModifiedPropertiesWithoutUndo();
                if (feedback != null && !feedback.transform.IsChildOf(old)) Object.DestroyImmediate(feedback.gameObject);
                Object.DestroyImmediate(old.gameObject);
                string[] names = { "Reactor", "Relay", "RapidTurret", "Core", "HeavyTurret", "AreaTurret" };
                bool useRapidModel = source.Definition.Kind == BuildingKind.Turret && AssetDatabase.LoadAssetAtPath<GameObject>(RapidModel) != null;
                var model = InstantiateModel(useRapidModel ? RapidModel : Models + names[(int)source.Definition.Kind] + "EffectModel.prefab", root.transform);
                var anchors = model.GetComponent<CombatModelAnchors>() ?? model.AddComponent<CombatModelAnchors>();
                anchors.TurretYaw = Pivot(model.transform, "TurretYaw");
                anchors.Muzzle = Pivot(model.transform, "Muzzle") ?? Pivot(model.transform, "MuzzleLeft");
                anchors.AlternateMuzzle = Pivot(model.transform, "MuzzleRight");
                anchors.EnergyPort = Pivot(model.transform, "EnergyPort");
                anchors.StatusAnchor = Pivot(model.transform, "StatusAnchor");
                anchors.RapidMotion = model.GetComponent<RapidTurretMotion>();
                if (anchors.Muzzle != null && anchors.RapidMotion == null && model.GetComponent<WeaponMotionRig>() == null) model.AddComponent<WeaponMotionRig>();
                DisableBarShadows(root, model.transform);
                data.Update(); Set(data, "visualRoot", model.transform); data.ApplyModifiedPropertiesWithoutUndo();
                return PrefabUtility.SaveAsPrefabAsset(root, path).GetComponent<BuildingView>();
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static EnemyView EnemyPrefab(EnemyView source, string name)
        {
            string path = Content + "/Enemies/" + name + ".prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing.GetComponent<EnemyView>();
            var root = Object.Instantiate(source.gameObject);
            try
            {
                root.name = name;
                var view = root.GetComponent<EnemyView>();
                var data = new SerializedObject(view);
                var old = (Transform)data.FindProperty("facingRoot").objectReferenceValue;
                if (old == null) old = ((Renderer)data.FindProperty("bodyRenderer").objectReferenceValue).transform.parent;
                Set(data, "bodyRenderer", null); Set(data, "strikeFlash", null); Set(data, "facingRoot", null);
                data.ApplyModifiedPropertiesWithoutUndo(); Object.DestroyImmediate(old.gameObject);
                var model = InstantiateModel(Models + name + "EffectModel.prefab", root.transform);
                DisableBarShadows(root, model.transform);
                var motion = root.AddComponent<MechanicalEnemyMotion>();
                motion.Legs = new[] { "LegFrontLeftPivot", "LegFrontRightPivot", "LegRearLeftPivot", "LegRearRightPivot" }
                    .Select(pivot => Pivot(model.transform, pivot)).Where(pivot => pivot != null).ToArray();
                data.Update(); Set(data, "facingRoot", model.transform); Set(data, "mechanicalMotion", motion);
                data.FindProperty("industrialModel").boolValue = true; data.ApplyModifiedPropertiesWithoutUndo();
                return PrefabUtility.SaveAsPrefabAsset(root, path).GetComponent<EnemyView>();
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void DressEnvironment(Scene scene, IReadOnlyList<BuildingView> placed)
        {
            var all = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true)).ToArray();
            foreach (var item in all)
            {
                if (item == null) continue;
                if (item.name.StartsWith("岩体 "))
                {
                    var rock = InstantiateModel(Environment + (((int)item.position.x + (int)item.position.z) % 2 == 0 ? "RockLowA" : "RockLowB") + ".prefab", item.parent);
                    rock.name = item.name; rock.transform.position = new Vector3(item.position.x, 0, item.position.z);
                    rock.transform.rotation = Quaternion.Euler(0, ((int)item.position.x % 4) * 90, 0);
                    Object.DestroyImmediate(item.gameObject);
                }
                else if (item.name == "敌人入口")
                {
                    var marker = InstantiateModel(Environment + "EntranceMarker.prefab", item.parent);
                    marker.name = "敌人入口"; marker.transform.position = new Vector3(item.position.x, .015f, item.position.z);
                    marker.transform.rotation = Quaternion.Euler(0, 90, 0); Object.DestroyImmediate(item.gameObject);
                }
                else if (item.name == "Ground")
                {
                    var original = item.GetComponent<Renderer>().sharedMaterial;
                    var material = new Material(original) { name = "IndustrialBattleGround" };
                    material.SetColor("_BaseColor", new Color(.13f, .155f, .17f));
                    AssetDatabase.CreateAsset(material, Content + "/IndustrialBattleGround.mat");
                    item.GetComponent<Renderer>().sharedMaterial = material;
                }
                else if (item.name.StartsWith("网格线"))
                    foreach (var line in item.GetComponentsInChildren<LineRenderer>()) line.startWidth = line.endWidth = .018f;
            }
            var pads = new GameObject("工业基础平台").transform;
            foreach (var building in placed)
            {
                var tile = InstantiateModel(Environment + "IndustrialFloor.prefab", pads);
                tile.transform.position = building.transform.position + Vector3.down * .085f;
            }
            RenderSettings.ambientLight = new Color(.52f, .59f, .67f);
            foreach (var light in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Light>()))
                if (light.type == LightType.Directional) { light.intensity = 1.7f; light.transform.rotation = Quaternion.Euler(48, -35, 0); }
        }

        private static GameObject InstantiateModel(string path, Transform parent)
        {
            var model = (GameObject)PrefabUtility.InstantiatePrefab(Require<GameObject>(path), parent);
            model.transform.localPosition = Vector3.zero; model.transform.localRotation = Quaternion.identity; model.transform.localScale = Vector3.one;
            return model;
        }
        private static void DisableBarShadows(GameObject root, Transform model)
        {
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                if (!renderer.transform.IsChildOf(model))
                { renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false; }
        }
        private static Transform Pivot(Transform root, string name) => root.GetComponentsInChildren<Transform>(true).FirstOrDefault(item => item.name == name);
        private static T Find<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).Single();
        private static T Require<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new InvalidOperationException("缺少资源：" + path);
        private static void Set(SerializedObject data, string name, Object value) => data.FindProperty(name).objectReferenceValue = value;
    }
}

