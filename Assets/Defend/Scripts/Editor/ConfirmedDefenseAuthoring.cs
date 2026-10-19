using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Defend.Simulation;
using Defend.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Defend.Editor
{
    /// <summary>在现有工业关卡上添加正式玩法资源，不重建环境、现役炮塔或镜头。</summary>
    public static class ConfirmedDefenseAuthoring
    {
        public const string Root = "Assets/Defend/Content/EmergentDefense";
        [MenuItem("涌现防线/接入已确认玩法")]
        public static void Integrate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先结束播放。");
            var active = SceneManager.GetActiveScene();
            if (active.isDirty) throw new InvalidOperationException("当前场景有未保存修改，未执行接入。");
            var scene = EditorSceneManager.OpenScene(PhaseFiveCombatSceneBuilder.ScenePath);
            var session = Find<GameSessionRoot>(scene); var world = Find<WorldPresentation>(scene); var battle = Find<BattlePresentation>(scene);
            var sessionData = new SerializedObject(session); var worldData = new SerializedObject(world);
            WhiteboxSceneBuilder.EnsureFolder(Root + "/Buildings"); WhiteboxSceneBuilder.EnsureFolder(Root + "/Enemies"); WhiteboxSceneBuilder.EnsureFolder(Root + "/Portraits");
            var definitions = new Dictionary<BuildingKind, BuildingDefinitionAsset>();
            var oldDefinitions = sessionData.FindProperty("buildingDefinitions");
            for (int i = 0; i < oldDefinitions.arraySize; i++)
            {
                var source = (BuildingDefinitionAsset)oldDefinitions.GetArrayElementAtIndex(i).objectReferenceValue;
                var asset = CloneAsset(source, Root + "/" + source.Kind + ".asset");
                var data = new SerializedObject(asset);
                string title = source.Kind == BuildingKind.Turret ? "闪哨机" : source.Kind == BuildingKind.BurstTurret ? "崩岩机" : source.Kind == BuildingKind.AreaTurret ? "荡尘机" : source.DisplayName;
                if (source.Kind == BuildingKind.BurstTurret || source.Kind == BuildingKind.AreaTurret)
                {
                    data.FindProperty("width").intValue = 2;
                    data.FindProperty("depth").intValue = 2;
                }
                data.FindProperty("displayName").stringValue = title; data.ApplyModifiedPropertiesWithoutUndo(); definitions[source.Kind] = asset;
            }
            var battery = AssetDatabase.LoadAssetAtPath<BuildingDefinitionAsset>(Root + "/Battery.asset");
            if (battery == null) { battery = ScriptableObject.CreateInstance<BuildingDefinitionAsset>(); battery.ConfigureWhitebox(BuildingKind.Battery, "电池", 160000, 0, 0, 8000, 4, 160, 80, 60); AssetDatabase.CreateAsset(battery, Root + "/Battery.asset"); var data = new SerializedObject(battery); data.FindProperty("maximumHealth").intValue = 180; data.FindProperty("maximumChargePerTick").longValue = 8000; data.ApplyModifiedPropertiesWithoutUndo(); }
            definitions[BuildingKind.Battery] = battery;
            var sources = worldData.FindProperty("buildingPrefabs"); var views = new Dictionary<BuildingKind, BuildingView>();
            for (int i = 0; i < sources.arraySize; i++)
            {
                var source = (BuildingView)sources.GetArrayElementAtIndex(i).objectReferenceValue;
                string path = Root + "/Buildings/" + source.Definition.Kind + ".prefab";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(source), path);
                var instance = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var data = new SerializedObject(instance.GetComponent<BuildingView>());
                    Set(data, "definition", definitions[source.Definition.Kind]);
                    if (source.Definition.Kind == BuildingKind.BurstTurret || source.Definition.Kind == BuildingKind.AreaTurret)
                    {
                        var selection = (GameObject)data.FindProperty("selectionRoot").objectReferenceValue;
                        Vector3 selectionScale = selection.transform.localScale;
                        selection.transform.localScale = new Vector3(2.02f, selectionScale.y, 2.02f);
                        var collider = instance.GetComponent<BoxCollider>();
                        Vector3 size = collider.size;
                        collider.size = new Vector3(Mathf.Max(2, size.x), size.y, Mathf.Max(2, size.z));
                    }
                    data.ApplyModifiedPropertiesWithoutUndo();
                    PrefabUtility.SaveAsPrefabAsset(instance, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(instance); }
                views[source.Definition.Kind] = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<BuildingView>();
            }
            if (!views.ContainsKey(BuildingKind.Battery)) views[BuildingKind.Battery] = MakeBattery(views[BuildingKind.Relay], battery);
            AssignArray(sessionData.FindProperty("buildingDefinitions"), definitions.OrderBy(x => (int)x.Key).Select(x => (Object)x.Value));
            AssignArray(worldData.FindProperty("buildingPrefabs"), views.OrderBy(x => (int)x.Key).Select(x => (Object)x.Value));
            foreach (var placed in world.AuthoredBuildings) { var data = new SerializedObject(placed); Set(data, "definition", definitions[placed.Definition.Kind]); data.ApplyModifiedPropertiesWithoutUndo(); PrefabUtility.RecordPrefabInstancePropertyModifications(placed); }
            worldData.ApplyModifiedPropertiesWithoutUndo();
            var battleAsset = CloneAsset((BattleSettingsAsset)sessionData.FindProperty("battleSettings").objectReferenceValue, Root + "/DefenseBattleSettings.asset");
            battleAsset.ConfigureConfirmedGameplay(); EditorUtility.SetDirty(battleAsset); Set(sessionData, "battleSettings", battleAsset);
            var energyAsset = CloneAsset((EnergyExperimentSettings)sessionData.FindProperty("experimentSettings").objectReferenceValue, Root + "/DefenseEnergySettings.asset"); Set(sessionData, "experimentSettings", energyAsset);
            var battleData = new SerializedObject(battle);
            var standard = (EnemyView)battleData.FindProperty("enemyPrefab").objectReferenceValue;
            var fast = (EnemyView)battleData.FindProperty("fastEnemyPrefab").objectReferenceValue;
            var heavy = (EnemyView)battleData.FindProperty("heavyEnemyPrefab").objectReferenceValue;
            var enemyViews = new[] { MakeEnemy(standard, EnemyKind.Normal, new Color(.22f,.78f,.72f), 1), MakeEnemy(fast, EnemyKind.Fast, new Color(1,.77f,.2f), .72f), MakeEnemy(heavy, EnemyKind.Heavy, new Color(.72f,.35f,.85f), 1), MakeEnemy(standard, EnemyKind.Demolisher, new Color(1,.38f,.15f), 1.12f), MakeEnemy(heavy, EnemyKind.Converger, new Color(.2f,.62f,1), .8f) };
            string[] enemyFields = { "enemyPrefab", "fastEnemyPrefab", "heavyEnemyPrefab", "demolisherEnemyPrefab", "convergerEnemyPrefab" };
            for (int i = 0; i < enemyFields.Length; i++) Set(battleData, enemyFields[i], enemyViews[i]); battleData.ApplyModifiedPropertiesWithoutUndo();
            GameObject uiPrefab = DefenseInterfaceAuthoring.CreatePrefab();
            var previous = FindOptional<DefenseInterface>(scene); if (previous != null) Object.DestroyImmediate(previous.gameObject);
            var oldPanel = (BattleControlPanel)sessionData.FindProperty("battleControlPanel").objectReferenceValue;
            var oldCanvas = oldPanel.GetComponentInParent<Canvas>(true); oldCanvas.gameObject.SetActive(false);
            var ui = (GameObject)PrefabUtility.InstantiatePrefab(uiPrefab); var controller = ui.GetComponent<DefenseInterface>();
            var controllerData = new SerializedObject(controller); var viewport = Find<WorldCameraViewport>(scene);
            Set(controllerData, "cameraViewport", viewport); Set(controllerData, "archiveStory", AssetDatabase.LoadAssetAtPath<TextAsset>(Root + "/ArchiveStory.txt"));
            var portraits = new List<Object>(); foreach (var enemy in enemyViews) portraits.Add(RenderPortrait(enemy)); AssignArray(controllerData.FindProperty("enemyPortraits"), portraits);
            controllerData.ApplyModifiedPropertiesWithoutUndo();
            var viewportData = new SerializedObject(viewport); Set(viewportData, "mapViewport", controllerData.FindProperty("mapViewport").objectReferenceValue); viewportData.ApplyModifiedPropertiesWithoutUndo();
            Set(sessionData, "defenseInterface", controller); sessionData.ApplyModifiedPropertiesWithoutUndo();
            var audio = ui.GetComponentInChildren<DefenseAudio>(true); var audioData = new SerializedObject(audio);
            var music = (AudioSource)audioData.FindProperty("musicSource").objectReferenceValue; var effects = (AudioSource)audioData.FindProperty("effectsSource").objectReferenceValue;
            music.clip = null; music.loop = true; music.playOnAwake = false;
            audioData.FindProperty("outgameMusic").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(Root + "/Audio/局外BGM.mp3");
            audioData.FindProperty("preparationMusic").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(Root + "/Audio/准备阶段BGM.mp3");
            audioData.FindProperty("combatMusic").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(Root + "/Audio/战斗阶段BGM.mp3");
            audioData.FindProperty("soundLibrary").objectReferenceValue = AssetDatabase.LoadAssetAtPath<DefenseSoundLibrary>(DefenseSoundLibraryAuthoring.LibraryPath);
            audioData.ApplyModifiedPropertiesWithoutUndo();
            effects.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(Root + "/Audio/界面反馈.wav"); effects.playOnAwake = false;
            PrefabUtility.RecordPrefabInstancePropertyModifications(controller); PrefabUtility.RecordPrefabInstancePropertyModifications(music); PrefabUtility.RecordPrefabInstancePropertyModifications(effects);
            // 正式入口唯一启用，开发关卡保留列表但不作为启动入口。
            var buildScenes = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(PhaseFiveCombatSceneBuilder.ScenePath, true) };
            foreach (var existing in EditorBuildSettings.scenes) if (existing.path != PhaseFiveCombatSceneBuilder.ScenePath) buildScenes.Add(new EditorBuildSettingsScene(existing.path, false));
            EditorBuildSettings.scenes = buildScenes.ToArray(); CombatImpactAuthoring.IntegrateScene(scene); PlacementPreviewAuthoring.IntegrateScene(scene); RapidTurretFootprintAuthoring.IntegrateScene(scene); AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        }

        private static BuildingView MakeBattery(BuildingView source, BuildingDefinitionAsset definition)
        {
            string path = Root + "/Buildings/Battery.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) return AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<BuildingView>();
            var instance = Object.Instantiate(source.gameObject);
            try
            {
                instance.name = "电池"; var data = new SerializedObject(instance.GetComponent<BuildingView>()); Set(data, "definition", definition);
                var previous = (Transform)data.FindProperty("visualRoot").objectReferenceValue; Object.DestroyImmediate(previous.gameObject);
                var model = new GameObject("蓄能柜"); model.transform.SetParent(instance.transform, false);
                var material = CreateMaterial("BatteryShell", new Color(.12f, .23f, .28f)); var glow = CreateMaterial("BatteryIndicator", new Color(.2f, .95f, .62f));
                Cube(model.transform, "柜体", new Vector3(0,.55f,0), new Vector3(.75f,1.1f,.66f), material);
                for (int i=0;i<3;i++) Cube(model.transform,"储能单元"+i,new Vector3(0,.3f+i*.28f,-.35f),new Vector3(.58f,.16f,.06f),glow);
                Set(data,"visualRoot",model.transform); data.ApplyModifiedPropertiesWithoutUndo(); return PrefabUtility.SaveAsPrefabAsset(instance,path).GetComponent<BuildingView>();
            }
            finally { Object.DestroyImmediate(instance); }
        }
        private static EnemyView MakeEnemy(EnemyView source, EnemyKind kind, Color color, float scale)
        {
            string path = Root + "/Enemies/" + kind + ".prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
            {
                EnsureEnemyHitFlash(path);
                return AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<EnemyView>();
            }
            var instance = Object.Instantiate(source.gameObject);
            try
            {
                if (instance.GetComponentInChildren<HitFlashEffect>(true) == null) instance.AddComponent<HitFlashEffect>();
                instance.name = kind.ToString(); var view = instance.GetComponent<EnemyView>(); var data = new SerializedObject(view);
                var facing = (Transform)data.FindProperty("facingRoot").objectReferenceValue; facing.localScale *= scale;
                if (kind == EnemyKind.Demolisher) Cube(facing,"拆线臂",new Vector3(.45f,.6f,.24f),new Vector3(.16f,.8f,.16f),CreateMaterial(kind+"Signal",color));
                if (kind == EnemyKind.Converger) Cube(facing,"回收斗",new Vector3(0,.3f,.66f),new Vector3(1.05f,.26f,.4f),CreateMaterial(kind+"Signal",color));
                var collider = instance.GetComponent<BoxCollider>(); if (collider == null) collider = instance.AddComponent<BoxCollider>(); collider.center = new Vector3(0,.5f,0); collider.size = new Vector3(kind == EnemyKind.Fast ? .55f : .8f,1.1f,kind == EnemyKind.Fast ? .55f : .8f);
                var marker = new GameObject("选中标记",typeof(LineRenderer)); marker.transform.SetParent(instance.transform,false); var line = marker.GetComponent<LineRenderer>(); line.useWorldSpace=false; line.positionCount=33; line.startWidth=line.endWidth=.035f; line.sharedMaterial=CreateMaterial("Selection",new Color(.35f,1, .8f));
                for(int i=0;i<33;i++){ float angle=i*Mathf.PI*2/32; line.SetPosition(i,new Vector3(Mathf.Cos(angle)*.57f,.045f,Mathf.Sin(angle)*.57f)); }
                marker.SetActive(false); Set(data,"selectionMarker",marker); data.ApplyModifiedPropertiesWithoutUndo(); return PrefabUtility.SaveAsPrefabAsset(instance,path).GetComponent<EnemyView>();
            }
            finally { Object.DestroyImmediate(instance); }
        }
        public static void EnsureEnemyHitFlash(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null || prefab.GetComponentInChildren<HitFlashEffect>(true) != null) return;
            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                contents.AddComponent<HitFlashEffect>();
                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }

        private static Sprite RenderPortrait(EnemyView source)
        {
            string path=Root+"/Portraits/"+source.name+".png";
            if (!File.Exists(path))
            {
                var preview=new PreviewRenderUtility(); preview.camera.backgroundColor=new Color(.045f,.08f,.11f); preview.camera.clearFlags=CameraClearFlags.SolidColor;
                var instance=Object.Instantiate(source.gameObject); preview.AddSingleGO(instance); instance.transform.position=Vector3.zero;
                preview.camera.transform.position=new Vector3(2,1.9f,2.5f); preview.camera.transform.LookAt(new Vector3(0,.6f,0)); preview.camera.nearClipPlane=.1f; preview.camera.farClipPlane=20; preview.camera.fieldOfView=35;
                preview.lights[0].intensity=1.5f; preview.lights[0].transform.rotation=Quaternion.Euler(40,30,0); preview.lights[1].intensity=.8f; preview.ambientColor=Color.gray;
                try { preview.BeginStaticPreview(new Rect(0,0,256,256)); preview.camera.Render(); var texture=preview.EndStaticPreview(); File.WriteAllBytes(path,texture.EncodeToPNG()); Object.DestroyImmediate(texture); }
                finally { preview.Cleanup(); }
                AssetDatabase.ImportAsset(path); var importer=(TextureImporter)AssetImporter.GetAtPath(path); importer.textureType=TextureImporterType.Sprite; importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
        private static void Cube(Transform parent,string name,Vector3 position,Vector3 scale,Material material) { var cube=GameObject.CreatePrimitive(PrimitiveType.Cube); cube.name=name; cube.transform.SetParent(parent,false); cube.transform.localPosition=position; cube.transform.localScale=scale; cube.GetComponent<Renderer>().sharedMaterial=material; Object.DestroyImmediate(cube.GetComponent<Collider>()); }
        private static Material CreateMaterial(string name,Color color) { string path=Root+"/"+name+".mat"; var material=AssetDatabase.LoadAssetAtPath<Material>(path); if(material!=null)return material; material=new Material(Shader.Find("Universal Render Pipeline/Lit")); material.color=color; AssetDatabase.CreateAsset(material,path); return material; }
        private static T CloneAsset<T>(T source,string path) where T:Object { var asset=AssetDatabase.LoadAssetAtPath<T>(path); if(asset!=null)return asset; if(!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(source),path))throw new InvalidOperationException("无法创建配置 "+path); return AssetDatabase.LoadAssetAtPath<T>(path); }
        private static T Find<T>(Scene scene) where T:Component => FindOptional<T>(scene) ?? throw new InvalidOperationException("场景缺少 "+typeof(T).Name);
        private static T FindOptional<T>(Scene scene) where T:Component => scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<T>(true)).FirstOrDefault();
        private static void Set(SerializedObject data,string name,Object value) { data.FindProperty(name).objectReferenceValue=value; }
        private static void AssignArray(SerializedProperty property,IEnumerable<Object> objects) { var values=objects.ToArray(); property.arraySize=values.Length; for(int i=0;i<values.Length;i++)property.GetArrayElementAtIndex(i).objectReferenceValue=values[i]; }
    }
}

