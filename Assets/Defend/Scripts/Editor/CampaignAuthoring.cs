using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Defend.Simulation;
using Defend.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Defend.Editor
{
    /// <summary>由集成入口明确调用，生成六关资产；不打开场景，不改旧制作器或共享数值。</summary>
    public static partial class CampaignAuthoring
    {
        public const string ContentPath = "Assets/Defend/Content/Campaign/";
        public const string CatalogPath = ContentPath + "CampaignCatalog.asset";
        private const string BaseBattlePath = "Assets/Defend/Content/EmergentDefense/DefenseBattleSettings.asset";
        [Serializable] private sealed class Source { public int version, ticksPerSecond; public LevelSource[] levels; }
        [Serializable] private sealed class LevelSource
        {
            public string id, title, description;
            public int startingCredits, coreX, coreZ, wallVariant, maximumLivingEnemies;
            public WallSource[] walls; public EntranceSource[] entrances; public WaveSource[] waves;
        }
        [Serializable] private sealed class WallSource { public int x, z, width, depth, variant; }
        [Serializable] private sealed class EntranceSource
        { public string name; public int x, z, unlockWave, reservedX, reservedZ, reservedWidth, reservedDepth; }
        [Serializable] private sealed class WaveSource
        { public int enemyCount, health, reward, durationTicks; public string briefing; public GroupSource[] groups; }
        [Serializable] private sealed class GroupSource { public int kind, count, entranceIndex, startTick, intervalTicks; }

        public static string BuildData()
        {
            RequireEditMode();
            var source = JsonUtility.FromJson<Source>(File.ReadAllText(ContentPath + "AuthoringSource.json"));
            if (source.version != 1 || source.ticksPerSecond != 20 || source.levels.Length != 6) throw new InvalidOperationException("六关制作输入版本或数量错误。");
            var template = AssetDatabase.LoadAssetAtPath<BattleSettingsAsset>(BaseBattlePath);
            if (template == null) throw new InvalidOperationException("现役战斗配置缺失。");
            WhiteboxSceneBuilder.EnsureFolder(ContentPath.TrimEnd('/'));
            var levels = new CampaignLevelAsset[source.levels.Length];
            for (int i = 0; i < levels.Length; i++)
            {
                var input = source.levels[i]; string stem = "Level" + (i + 1).ToString("00");
                string battlePath = ContentPath + stem + "Battle.asset";
                var battle = AssetDatabase.LoadAssetAtPath<BattleSettingsAsset>(battlePath);
                if (battle == null) { battle = Object.Instantiate(template); battle.name = stem + "Battle"; AssetDatabase.CreateAsset(battle, battlePath); }
                else EditorUtility.CopySerialized(template, battle);
                var walls = input.walls.Select(w => new CampaignWallRectangle(w.x, w.z, w.width, w.depth, w.variant)).ToArray();
                var entrances = input.entrances.Select(e => new SpawnPoint(e.x, e.z, e.name, e.unlockWave, 2,
                    e.reservedX, e.reservedZ, e.reservedWidth, e.reservedDepth)).ToArray();
                var waves = input.waves.Select(w => new WaveDefinition(w.enemyCount, w.health, 1, w.reward,
                    briefing: w.briefing, groups: w.groups.Select(g => new WaveGroupDefinition((EnemyKind)g.kind, g.count,
                        g.entranceIndex, g.startTick, g.intervalTicks)).ToArray(), independentGroups: true,
                    plannedSpawnDurationTicks: w.durationTicks)).ToArray();
                battle.ConfigureCampaign(waves, entrances, walls, input.maximumLivingEnemies);
                battle.ConfigureCampaignWalls(CampaignWallGeometry.CreateWalls(walls, input.wallVariant));
                var level = LoadOrCreate<CampaignLevelAsset>(ContentPath + stem + ".asset");
                var preview = LoadPreview(stem);
                level.Configure(input.id, input.title, input.description, input.startingCredits,
                    new Vector2Int(input.coreX, input.coreZ), battle, input.wallVariant, walls, preview);
                EditorUtility.SetDirty(battle); EditorUtility.SetDirty(level); levels[i] = level;
            }
            var catalog = LoadOrCreate<CampaignCatalogAsset>(CatalogPath); catalog.Configure(levels); EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            return ValidateAssets(false);
        }

        private static Sprite LoadPreview(string stem)
        {
            string path = ContentPath + "Previews/" + stem + ".png";
            if (!File.Exists(path)) return null;
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false; importer.alphaIsTransparency = true; importer.maxTextureSize = 1024;
            importer.SaveAndReimport(); return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        public static string BindScene()
        {
            RequireEditMode();
            var sessions = Object.FindObjectsByType<GameSessionRoot>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (sessions.Length != 1) throw new InvalidOperationException("当前正式场景应有且只有一个会话入口。");
            var catalog = AssetDatabase.LoadAssetAtPath<CampaignCatalogAsset>(CatalogPath);
            if (catalog == null) throw new InvalidOperationException("先制作六关数据。");
            var environment = Object.FindFirstObjectByType<CampaignEnvironmentPresentation>(FindObjectsInactive.Include);
            if (environment == null) throw new InvalidOperationException("先接入战役环境表现组件。");
            var session = sessions[0]; var data = new SerializedObject(session);
            data.FindProperty("campaignCatalog").objectReferenceValue = catalog;
            data.FindProperty("campaignEnvironment").objectReferenceValue = environment; data.ApplyModifiedPropertiesWithoutUndo();
            var markers = Object.FindObjectsByType<FormalEntrancePresentation>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .OrderBy(m => new SerializedObject(m).FindProperty("entranceIndex").intValue).ToList();
            if (markers.Count == 0) throw new InvalidOperationException("现役入口美术标识缺失。");
            while (markers.Count < 6)
            {
                var clone = Object.Instantiate(markers[0].gameObject, markers[0].transform.parent);
                clone.name = "战役敌人入口 " + (markers.Count + 1); markers.Add(clone.GetComponent<FormalEntrancePresentation>());
            }
            for (int i = 0; i < markers.Count; i++)
            {
                var marker = new SerializedObject(markers[i]); marker.FindProperty("entranceIndex").intValue = i;
                marker.FindProperty("session").objectReferenceValue = session; marker.ApplyModifiedPropertiesWithoutUndo();
            }
            EditorSceneManager.MarkSceneDirty(session.gameObject.scene);
            return "六关目录、环境与六个入口已绑定；当前场景尚未保存。";
        }

        public static string ValidateAssets(bool requirePreviews = true)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<CampaignCatalogAsset>(CatalogPath);
            if (catalog == null || catalog.Levels.Count != 6) throw new InvalidOperationException("六关目录不完整。");
            int waveCount = 0, enemyCount = 0, wallCount = 0;
            foreach (var level in catalog.Levels)
            {
                var settings = level.BattleSettings.CreateSettings(); var world = level.CreateWorldSettings(20);
                if (world.Width != 40 || world.Depth != 40) throw new InvalidOperationException("战场必须完整40×40。");
                if (requirePreviews && level.Preview == null) throw new InvalidOperationException("缺少真实关卡预览：" + level.DisplayName);
                var cells = new HashSet<Vector2Int>();
                foreach (var wall in level.WallRectangles)
                    for (int z = wall.Z; z < wall.Z + wall.Depth; z++) for (int x = wall.X; x < wall.X + wall.Width; x++) cells.Add(new Vector2Int(x, z));
                if (settings.BlockedCells.Count != cells.Count || settings.BlockedCells.Any(c => !cells.Contains(new Vector2Int(c.X, c.Z))))
                    throw new InvalidOperationException("存在墙体之外的阻挡区域：" + level.DisplayName);
                wallCount += settings.Walls.Count;
                for (int i = 0; i < settings.Waves.Count; i++)
                {
                    var wave = settings.Waves[i]; waveCount++; enemyCount += wave.EnemyCount;
                    if (!wave.IndependentGroups || wave.Groups.Sum(g => g.Count) != wave.EnemyCount) throw new InvalidOperationException("波次总数不一致。");
                    foreach (var group in wave.Groups)
                        if (!settings.SpawnPoints[group.EntranceIndex].IsOpenAtWave(i + 1)) throw new InvalidOperationException("未开放入口有出兵计划。");
                }
            }
            if (waveCount != 68 || enemyCount != 4146) throw new InvalidOperationException("战役必须为68波、4146名敌人。");
            return "已核对6关、68波、4146名敌人；完整40×40、仅墙格阻挡；实体墙盒 " + wallCount + "。";
        }
        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        { var asset = AssetDatabase.LoadAssetAtPath<T>(path); if (asset != null) return asset; asset = ScriptableObject.CreateInstance<T>(); AssetDatabase.CreateAsset(asset, path); return asset; }
        private static void RequireEditMode()
        { if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请在编辑模式制作六关资产。"); }
    }
}
