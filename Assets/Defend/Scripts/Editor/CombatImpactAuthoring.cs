using System;
using System.Collections.Generic;
using System.Linq;
using Defend.Simulation;
using Defend.PhaseFiveEffects;
using Defend.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Defend.Editor
{
    /// <summary>定向补齐现役预制体和当前关卡引用，不重建场景内容。</summary>
    public static class CombatImpactAuthoring
    {
        public const string Root = "Assets/Defend/Content/PhaseFiveCombat/Impact";
        public const string HexShieldPrefabPath = "Assets/Defend/Content/Prefabs/Effects/HexShield.prefab";
        private const string Materials = "Assets/Defend/Content/Art/PhaseFiveEffects/Materials/";
        private static readonly Color FriendlyHealthColor = new Color(.22f, .86f, .36f, 1);
        private static readonly Color EnemyHealthColor = new Color(.95f, .32f, .18f, 1);

        [MenuItem("涌现防线/接入六边形核心护盾")]
        public static void IntegrateHexShield()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先结束播放。");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("打开的场景有未保存修改，未执行核心护盾接入。");
            Require<GameObject>(HexShieldPrefabPath);
            var scene = SceneManager.GetActiveScene();
            if (scene.path != PhaseFiveCombatSceneBuilder.ScenePath) scene = EditorSceneManager.OpenScene(PhaseFiveCombatSceneBuilder.ScenePath);
            var world = Find<WorldPresentation>(scene);
            var paths = new HashSet<string>();
            var prefabs = new SerializedObject(world).FindProperty("buildingPrefabs");
            for (int i = 0; i < prefabs.arraySize; i++)
            {
                var view = prefabs.GetArrayElementAtIndex(i).objectReferenceValue as BuildingView;
                if (view != null && view.Definition.Kind == BuildingKind.Core) paths.Add(AssetDatabase.GetAssetPath(view));
            }
            foreach (var view in world.AuthoredBuildings)
                if (view != null && view.Definition.Kind == BuildingKind.Core)
                {
                    string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(view.gameObject);
                    if (!string.IsNullOrEmpty(path)) paths.Add(path);
                }
            foreach (string path in paths)
            {
                var contents = PrefabUtility.LoadPrefabContents(path);
                try { ConfigureCoreShield(contents.GetComponent<BuildingView>()); PrefabUtility.SaveAsPrefabAsset(contents, path); }
                finally { PrefabUtility.UnloadPrefabContents(contents); }
            }
            foreach (var view in world.AuthoredBuildings)
                if (view != null && view.Definition.Kind == BuildingKind.Core) ConfigureCoreShield(view);
            AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("核心护盾引用未能保存到现役关卡。");
            Debug.Log("六边形核心护盾已保存到现役核心预制体与场景，储能、生命和命中均由真实模拟结果驱动。");
        }

        [MenuItem("涌现防线/修正己方建筑生命条颜色")]
        public static void IntegrateFriendlyHealthColors()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先结束播放。");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("打开的场景有未保存修改，未执行生命条颜色迁移。");
            var scene = SceneManager.GetActiveScene();
            if (scene.path != PhaseFiveCombatSceneBuilder.ScenePath) scene = EditorSceneManager.OpenScene(PhaseFiveCombatSceneBuilder.ScenePath);
            var kinds = new[] { BuildingKind.Reactor, BuildingKind.Relay, BuildingKind.Turret, BuildingKind.Core, BuildingKind.BurstTurret, BuildingKind.AreaTurret, BuildingKind.Battery };
            foreach (var kind in kinds)
            {
                string path = ConfirmedDefenseAuthoring.Root + "/Buildings/" + kind + ".prefab";
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    ConfigureBuildingHealthColor(root.GetComponent<BuildingView>());
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            var world = Find<WorldPresentation>(scene);
            foreach (var building in world.AuthoredBuildings) ConfigureBuildingHealthColor(building);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("己方建筑生命条颜色未能保存到场景。");
            Debug.Log("已将七种正式建筑及现役场景实例的即时生命条保存为绿色；敌人生命条、延迟白条与核心护盾未修改。");
        }

        private static void ConfigureBuildingHealthColor(BuildingView view)
        {
            if (view == null) throw new InvalidOperationException("缺少建筑表现组件。");
            var health = (HealthBarView)new SerializedObject(view).FindProperty("healthBar").objectReferenceValue;
            if (health == null) throw new InvalidOperationException("建筑缺少已绑定生命条：" + view.name);
            ConfigureHealthColor(health, FriendlyHealthColor);
        }

        private static void ConfigureHealthColor(HealthBarView health, Color color)
        {
            var fill = (Image)new SerializedObject(health).FindProperty("healthFill").objectReferenceValue;
            if (fill == null) throw new InvalidOperationException("生命条缺少即时生命填充：" + health.name);
            fill.color = color;
            EditorUtility.SetDirty(fill);
            if (PrefabUtility.IsPartOfPrefabInstance(fill)) PrefabUtility.RecordPrefabInstancePropertyModifications(fill);
        }

        [MenuItem("涌现防线/接入射击命中反馈")]
        public static void Integrate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先结束播放。");
            var scene = SceneManager.GetActiveScene();
            if (scene.isDirty) throw new InvalidOperationException("当前场景有未保存修改，请先保存。");
            if (scene.path != PhaseFiveCombatSceneBuilder.ScenePath) scene = EditorSceneManager.OpenScene(PhaseFiveCombatSceneBuilder.ScenePath);
            IntegrateScene(scene);
            AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        }

        public static void IntegrateScene(Scene scene)
        {
            WhiteboxSceneBuilder.EnsureFolder(Root);
            var session = Find<GameSessionRoot>(scene);
            var world = Find<WorldPresentation>(scene);
            var battle = Find<BattlePresentation>(scene);
            var art = Find<CombatArtPresentation>(scene);
            var worldData = new SerializedObject(world);
            var paths = new HashSet<string>();
            var buildingPrefabs = worldData.FindProperty("buildingPrefabs");
            for (int i = 0; i < buildingPrefabs.arraySize; i++) paths.Add(AssetDatabase.GetAssetPath(buildingPrefabs.GetArrayElementAtIndex(i).objectReferenceValue));
            foreach (var placed in world.AuthoredBuildings)
            {
                string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(placed.gameObject);
                if (!string.IsNullOrEmpty(path)) paths.Add(path);
            }
            foreach (string path in paths) UpgradeBuildingPrefab(path);
            foreach (var placed in world.AuthoredBuildings)
            {
                UpgradeBuilding(placed);
                PrefabUtility.RecordPrefabInstancePropertyModifications(placed);
            }
            string[] enemyFields = { "enemyPrefab", "fastEnemyPrefab", "heavyEnemyPrefab", "demolisherEnemyPrefab", "convergerEnemyPrefab" };
            EnemyKind[] kinds = { EnemyKind.Normal, EnemyKind.Fast, EnemyKind.Heavy, EnemyKind.Demolisher, EnemyKind.Converger };
            var battleData = new SerializedObject(battle);
            for (int i = 0; i < enemyFields.Length; i++)
            {
                var reference = battleData.FindProperty(enemyFields[i]).objectReferenceValue;
                if (reference != null) UpgradeEnemyPrefab(AssetDatabase.GetAssetPath(reference), kinds[i]);
            }
            var camera = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Camera>(true)).First(x => x.CompareTag("MainCamera"));
            var numbers = art.GetComponent<DamageNumberPresentation>() ?? art.gameObject.AddComponent<DamageNumberPresentation>();
            numbers.Configure(CreateNumberPrefab(), camera, EnsureChild(art.transform, "独立伤害数字"));
            var projectiles = art.GetComponent<ProjectilePresentation>() ?? art.gameObject.AddComponent<ProjectilePresentation>();
            projectiles.Configure(CreateProjectile("RapidProjectile", .035f, "WarmGlow"), CreateProjectile("HeavyProjectile", .07f, "AmberGlow"), CreateProjectile("AreaProjectile", .09f, "CyanGlow"), EnsureChild(art.transform, "权威弹体"));
            var artData = new SerializedObject(art);
            artData.FindProperty("damageNumbers").objectReferenceValue = numbers;
            artData.FindProperty("projectiles").objectReferenceValue = projectiles;
            artData.ApplyModifiedPropertiesWithoutUndo();
            var sessionData = new SerializedObject(session);
            var settings = (BattleSettingsAsset)sessionData.FindProperty("battleSettings").objectReferenceValue;
            for (int i = 0; i < buildingPrefabs.arraySize; i++)
            {
                var view = (BuildingView)buildingPrefabs.GetArrayElementAtIndex(i).objectReferenceValue;
                if (view.Definition.Kind != BuildingKind.Turret && view.Definition.Kind != BuildingKind.BurstTurret && view.Definition.Kind != BuildingKind.AreaTurret) continue;
                var anchors = view.ModelAnchors;
                if (anchors == null || anchors.Muzzle == null) throw new InvalidOperationException("炮塔缺少真实炮口：" + view.name);
                Vector3 muzzle = view.transform.InverseTransformPoint(anchors.Muzzle.position);
                Transform pivot = anchors.TurretPitch != null ? anchors.TurretPitch : anchors.TurretYaw;
                Vector3 pivotPosition = pivot == null ? new Vector3(0, muzzle.y, 0) : view.transform.InverseTransformPoint(pivot.position);
                float pivotHeight = pivotPosition.y;
                settings.ConfigureTurretMuzzle(view.Definition.Kind, muzzle.y, muzzle.z, pivotHeight, muzzle.x, pivotPosition.z, pivotPosition.x);
                Debug.Log($"射击配置 {view.Definition.Kind}：炮口高度 {muzzle.y:F4}，前向 {muzzle.z:F4}，横向 {muzzle.x:F4}，枢轴高度 {pivotHeight:F4}");
            }
            for (int i = 0; i < enemyFields.Length; i++)
            {
                var view = (EnemyView)battleData.FindProperty(enemyFields[i]).objectReferenceValue;
                if (view == null) continue;
                float bodyHeight = .1f;
                foreach (var renderer in view.ModelRoot.GetComponentsInChildren<MeshRenderer>(true))
                    bodyHeight = Mathf.Max(bodyHeight, renderer.bounds.max.y - view.transform.position.y);
                settings.ConfigureEnemyBodyHeight(kinds[i], bodyHeight);
                Debug.Log($"敌人受击体积 {kinds[i]}：模型高度 {bodyHeight:F4}");
            }
            EditorUtility.SetDirty(settings); EditorUtility.SetDirty(numbers); EditorUtility.SetDirty(projectiles);
        }

        public static void UpgradeBuildingPrefab(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            var contents = PrefabUtility.LoadPrefabContents(path);
            try { UpgradeBuilding(contents.GetComponent<BuildingView>()); PrefabUtility.SaveAsPrefabAsset(contents, path); }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }
        public static void UpgradeEnemyPrefab(string path, EnemyKind kind)
        {
            var contents = PrefabUtility.LoadPrefabContents(path);
            try { UpgradeEnemy(contents.GetComponent<EnemyView>(), kind); PrefabUtility.SaveAsPrefabAsset(contents, path); }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }
        public static void UpgradeBuilding(BuildingView view)
        {
            var anchors = view.ModelAnchors;
            if (anchors != null && anchors.Muzzle != null)
            {
                if (anchors.RapidMotion != null)
                {
                    var motion = new SerializedObject(anchors.RapidMotion);
                    anchors.TurretPitch = (Transform)motion.FindProperty("barrelPitch").objectReferenceValue;
                }
                else if (anchors.TurretPitch == null && anchors.TurretYaw != null)
                {
                    Transform barrel = Descendant(anchors.TurretYaw, "BarrelRecoil");
                    if (barrel != null)
                    {
                        anchors.TurretPitch = barrel;
                    }
                }
                if (anchors.RapidMotion == null && anchors.GetComponent<WeaponMotionRig>() == null) anchors.gameObject.AddComponent<WeaponMotionRig>();
                EditorUtility.SetDirty(anchors);
                if (PrefabUtility.IsPartOfPrefabInstance(anchors)) PrefabUtility.RecordPrefabInstancePropertyModifications(anchors);
            }
            var health = view.GetComponentInChildren<HealthBarView>(true);
            if (health == null) health = AddHealthBar(view.transform, 1.75f);
            ConfigureHealthColor(health, FriendlyHealthColor);
            ShieldPresentation shield = null;
            if (view.Definition.Kind == BuildingKind.Core)
                shield = ConfigureCoreShield(view);
            view.ConfigureImpact(health, shield); EditorUtility.SetDirty(view);
        }

        public static ShieldPresentation ConfigureCoreShield(BuildingView view)
        {
            if (view == null || view.Definition.Kind != BuildingKind.Core)
                throw new InvalidOperationException("六边形护盾只能配置到核心建筑。");
            var shield = view.GetComponent<ShieldPresentation>() ?? view.gameObject.AddComponent<ShieldPresentation>();
            Transform shieldRoot = EnsureChild(view.transform, "持续核心护罩");
            // 只关闭旧制作入口生成的轮廓，保留场景中的其他人工内容。
            for (int ring = 0; ring < 3; ring++) DisableNamed(shieldRoot, "护罩轮廓" + ring);
            var hexShield = shieldRoot.GetComponentInChildren<HexShieldVisual>(true);
            if (hexShield == null)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(Require<GameObject>(HexShieldPrefabPath), shieldRoot);
                instance.name = "六边形能量护盾";
                hexShield = instance.GetComponent<HexShieldVisual>();
            }
            hexShield.transform.localPosition = new Vector3(0, .65f, 0);
            hexShield.transform.localRotation = Quaternion.identity;
            hexShield.transform.localScale = Vector3.one * .85f;
            Transform shieldBar = view.transform.Find("核心护盾条");
            if (shieldBar == null)
            {
                var canvas = CreateCanvas("核心护盾条", view.transform, new Vector2(100, 7)); shieldBar = canvas.transform; shieldBar.localPosition = new Vector3(0, 1.57f, 0); shieldBar.localScale = Vector3.one * .008f;
                AddImage(shieldBar, "背景", new Color(.025f, .055f, .065f, .9f), false);
                AddImage(shieldBar, "实际护盾", new Color(.3f, .88f, 1), true);
            }
            shield.Configure(shieldRoot.gameObject, shieldBar.GetComponent<CanvasGroup>(), shieldBar.Find("实际护盾").GetComponent<Image>());
            var health = (HealthBarView)new SerializedObject(view).FindProperty("healthBar").objectReferenceValue;
            view.ConfigureImpact(health, shield);
            DisableNamed(view.transform, "EnergyBackground"); DisableNamed(view.transform, "EnergyFill");
            EditorUtility.SetDirty(shield); EditorUtility.SetDirty(view);
            if (PrefabUtility.IsPartOfPrefabInstance(shield)) PrefabUtility.RecordPrefabInstancePropertyModifications(shield);
            if (PrefabUtility.IsPartOfPrefabInstance(view)) PrefabUtility.RecordPrefabInstancePropertyModifications(view);
            if (PrefabUtility.IsPartOfPrefabInstance(hexShield.transform)) PrefabUtility.RecordPrefabInstancePropertyModifications(hexShield.transform);
            return shield;
        }
        public static void UpgradeEnemy(EnemyView view, EnemyKind kind)
        {
            var data = new SerializedObject(view);
            var facing = (Transform)data.FindProperty("facingRoot").objectReferenceValue;
            if (facing == null) throw new InvalidOperationException("敌人缺少模型朝向节点：" + view.name);
            var reaction = view.GetComponent<HitReactionView>() ?? view.gameObject.AddComponent<HitReactionView>();
            Transform reactionRoot = view.transform.Find("受击视觉位移");
            if (reactionRoot == null)
            {
                reactionRoot = EnsureChild(view.transform, "受击视觉位移"); facing.SetParent(reactionRoot, true);
            }
            Transform impact = Descendant(facing, "ImpactPivot");
            if (impact != null && impact.GetComponent<DamageSubjectMotion>() == null) impact.gameObject.AddComponent<DamageSubjectMotion>();
            float distance = kind == EnemyKind.Heavy || kind == EnemyKind.Converger ? .025f : kind == EnemyKind.Fast ? .065f : .055f;
            float squash = kind == EnemyKind.Heavy || kind == EnemyKind.Converger ? .02f : kind == EnemyKind.Fast ? .05f : .04f;
            reaction.Configure(reactionRoot, distance, squash);
            var health = view.GetComponentInChildren<HealthBarView>(true);
            if (health == null) health = AddHealthBar(view.transform, kind == EnemyKind.Heavy ? 1.45f : 1.35f);
            ConfigureHealthColor(health, EnemyHealthColor);
            DisableNamed(view.transform, "HealthBackground"); DisableNamed(view.transform, "HealthFill");
            view.ConfigureImpact(health, reaction); EditorUtility.SetDirty(view);
        }
        private static HealthBarView AddHealthBar(Transform parent, float height)
        {
            var asset = CreateHealthPrefab();
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset.gameObject, parent);
            instance.name = "生命状态"; instance.transform.localPosition = Vector3.up * height;
            PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
            return instance.GetComponent<HealthBarView>();
        }
        private static HealthBarView CreateHealthPrefab()
        {
            WhiteboxSceneBuilder.EnsureFolder(Root);
            string path = Root + "/HealthBar.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
            {
                var existingView = existing.GetComponent<HealthBarView>();
                ConfigureDelayedHealthFill(existingView);
                return existingView;
            }
            var canvas = CreateCanvas("统一生命条", null, new Vector2(100, 8));
            try
            {
                AddImage(canvas.transform, "背景", new Color(.045f, .055f, .06f, .95f), false);
                var delayed = AddImage(canvas.transform, "延迟白条", Color.white, true);
                var immediate = AddImage(canvas.transform, "实际生命", EnemyHealthColor, true);
                var view = canvas.gameObject.AddComponent<HealthBarView>(); view.Configure(canvas.GetComponent<CanvasGroup>(), immediate, delayed);
                ConfigureDelayedHealthFill(view);
                canvas.transform.localScale = Vector3.one * .008f;
                return PrefabUtility.SaveAsPrefabAsset(canvas.gameObject, path).GetComponent<HealthBarView>();
            }
            finally { Object.DestroyImmediate(canvas.gameObject); }
        }

        public static void ConfigureDelayedHealthFill(HealthBarView health)
        {
            const string spritePath = "Assets/Defend/Content/Art/IngameUI/CommandRefinement/SolidWhite.png";
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
            if (sprite == null) throw new InvalidOperationException("缺少生命损失白条素材：" + spritePath);
            var delayed = (Image)new SerializedObject(health).FindProperty("delayedFill").objectReferenceValue;
            if (delayed == null) throw new InvalidOperationException("生命条缺少损失延迟填充：" + health.name);
            delayed.sprite = sprite;
            delayed.color = Color.white;
            delayed.type = Image.Type.Filled;
            delayed.fillMethod = Image.FillMethod.Horizontal;
            delayed.fillOrigin = 0;
            EditorUtility.SetDirty(delayed);
            if (PrefabUtility.IsPartOfPrefabInstance(delayed)) PrefabUtility.RecordPrefabInstancePropertyModifications(delayed);
        }
        private static DamageNumberView CreateNumberPrefab()
        {
            string path = Root + "/DamageNumber.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path); if (existing != null) return existing.GetComponent<DamageNumberView>();
            var canvas = CreateCanvas("独立伤害数字", null, new Vector2(160, 60)); canvas.sortingOrder = 30;
            try
            {
                var child = new GameObject("伤害数值", typeof(RectTransform), typeof(Text), typeof(Outline)); child.transform.SetParent(canvas.transform, false);
                var rect = (RectTransform)child.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
                var text = child.GetComponent<Text>(); text.font = Require<Font>("Assets/Defend/Content/Art/Fonts/NotoSansCJKsc-Regular.otf"); text.fontSize = 40; text.alignment = TextAnchor.MiddleCenter;
                text.color = new Color(1, .95f, .75f); text.raycastTarget = false; text.horizontalOverflow = HorizontalWrapMode.Overflow; text.verticalOverflow = VerticalWrapMode.Overflow;
                var outline = child.GetComponent<Outline>(); outline.effectColor = new Color(.025f, .025f, .035f, .95f); outline.effectDistance = new Vector2(2, -2);
                var view = canvas.gameObject.AddComponent<DamageNumberView>(); view.Configure(text, canvas.GetComponent<CanvasGroup>());
                return PrefabUtility.SaveAsPrefabAsset(canvas.gameObject, path).GetComponent<DamageNumberView>();
            }
            finally { Object.DestroyImmediate(canvas.gameObject); }
        }
        private static LineRenderer CreateProjectile(string name, float width, string material)
        {
            string path = Root + "/" + name + ".prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path); if (existing != null) return existing.GetComponent<LineRenderer>();
            var root = new GameObject(name, typeof(LineRenderer));
            try
            {
                var line = root.GetComponent<LineRenderer>(); line.useWorldSpace = true; line.positionCount = 2; line.startWidth = width; line.endWidth = width * .45f;
                line.numCapVertices = 3; line.sharedMaterial = Require<Material>(Materials + material + ".mat"); line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; line.receiveShadows = false;
                return PrefabUtility.SaveAsPrefabAsset(root, path).GetComponent<LineRenderer>();
            }
            finally { Object.DestroyImmediate(root); }
        }
        private static Canvas CreateCanvas(string name, Transform parent, Vector2 size)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup)); root.transform.SetParent(parent, false);
            var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.sortingOrder = 20;
            ((RectTransform)root.transform).sizeDelta = size;
            var group = root.GetComponent<CanvasGroup>(); group.blocksRaycasts = false; group.interactable = false;
            return canvas;
        }
        private static Image AddImage(Transform parent, string name, Color color, bool filled)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(Image)); root.transform.SetParent(parent, false);
            var rect = (RectTransform)root.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = new Vector2(1, 1); rect.offsetMax = new Vector2(-1, -1);
            var image = root.GetComponent<Image>(); image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"); image.color = color; image.raycastTarget = false;
            if (filled) { image.type = Image.Type.Filled; image.fillMethod = Image.FillMethod.Horizontal; image.fillOrigin = 0; }
            return image;
        }
        private static Transform EnsureChild(Transform parent, string name) { var existing = parent.Find(name); if (existing != null) return existing; var child = new GameObject(name).transform; child.SetParent(parent, false); return child; }
        private static Transform Descendant(Transform root, string name) => root.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == name);
        private static void DisableNamed(Transform parent, string name) { var child = parent.Find(name); if (child != null) child.gameObject.SetActive(false); }
        private static T Find<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<T>(true)).Single();
        private static T Require<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new InvalidOperationException("缺少资源：" + path);
    }
}



