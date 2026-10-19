using System;
using System.Collections.Generic;
using System.IO;
using Defend.Unity;
using UnityEditor;
using UnityEngine;

namespace Defend.Editor
{
    /// <summary>仅接入音效资源库和正式界面的声音引用，不重建界面或改写背景音乐。</summary>
    public static class DefenseSoundLibraryAuthoring
    {
        public const string SoundFolder = "Assets/Defend/Content/Audio/SoundEffects";
        public const string SupplementalFolder = "Assets/Defend/Content/Audio/Supplemental20261015";
        public const string LibraryPath = "Assets/Defend/Content/Audio/DefenseSoundLibrary.asset";

        public static string SupplementalClipPath(DefenseSoundCue cue)
        {
            string file = SupplementalFile(cue);
            return file == null ? null : SupplementalFolder + "/Prepared/" + Path.ChangeExtension(file, ".wav");
        }

        // 用户补充素材优先于原制作变体，重建资源库时也保留这一选择。
        public static string SupplementalFile(DefenseSoundCue cue)
        {
            switch (cue)
            {
                case DefenseSoundCue.RapidFire: return "闪哨机开火 SFX_Turret_Fire_Light.mp3";
                case DefenseSoundCue.HeavyFire: return "崩岩机开火 SFX_Turret_Fire_Heavy.mp3";
                case DefenseSoundCue.AreaFire: return "荡尘机开火 SFX_Turret_Fire_Splash.mp3";
                case DefenseSoundCue.MetalImpact:
                case DefenseSoundCue.HeavyImpact:
                case DefenseSoundCue.RailgunImpact: return "命中敌人 SFX_Hit.mp3";
                case DefenseSoundCue.EnemyDeath:
                case DefenseSoundCue.HeavyEnemyDeath: return "敌人死亡 SFX_Enemy_Die.mp3";
                case DefenseSoundCue.BuildingDestroyed: return "建筑损毁 SFX_Build_Destroy.mp3";
                case DefenseSoundCue.CoreHit: return "核心受击 SFX_Core_Hurt.mp3";
                case DefenseSoundCue.Build: return "建筑落位 SFX_Build_Place.mp3";
                case DefenseSoundCue.Move: return "移动落位 SFX_Build_Move.mp3";
                case DefenseSoundCue.Sell: return "出售 SFX_Build_Sell.mp3";
                case DefenseSoundCue.UiClick: return "按钮点击 SFX_UI_Click.mp3";
                case DefenseSoundCue.UiDenied: return "操作被拒绝 SFX_UI_Deny.mp3";
                case DefenseSoundCue.Selection: return "选中建筑 SFX_Select.mp3";
                case DefenseSoundCue.EnergyStarved: return "炮塔缺能停火 SFX_Build_NoPower.mp3";
                case DefenseSoundCue.WaveStart: return "波次开始 SFX_Wave_Start.mp3";
                case DefenseSoundCue.WaveClear: return "波次清空 SFX_Wave_Clear.mp3";
                default: return null;
            }
        }

        [MenuItem("涌现防线/音频/接入工业科幻音效")]
        public static void Integrate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请先结束播放，再接入音效资源。");
            var definitions = new List<DefenseSoundDefinition>();
            int clipCount = 0;
            foreach (DefenseSoundCue cue in Enum.GetValues(typeof(DefenseSoundCue)))
            {
                string supplemental = SupplementalFile(cue);
                var paths = supplemental == null
                    ? Directory.GetFiles(SoundFolder, cue + "_*.wav")
                    : new[] { SupplementalClipPath(cue) };
                Array.Sort(paths, StringComparer.Ordinal);
                if (paths.Length == 0) throw new InvalidOperationException("缺少音效资源：" + cue);
                var clips = new AudioClip[paths.Length];
                for (int index = 0; index < paths.Length; index++)
                {
                    string path = paths[index].Replace('\\', '/');
                    ConfigureImport(path);
                    clips[index] = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                    if (clips[index] == null || clips[index].length <= 0)
                        throw new InvalidOperationException("音效未能正常导入：" + path);
                }
                definitions.Add(CreateDefinition(cue, clips)); clipCount += clips.Length;
            }
            var library = AssetDatabase.LoadAssetAtPath<DefenseSoundLibrary>(LibraryPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<DefenseSoundLibrary>();
                AssetDatabase.CreateAsset(library, LibraryPath);
            }
            else Undo.RecordObject(library, "更新音效资源库");
            library.Configure(definitions.ToArray()); EditorUtility.SetDirty(library);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DefenseInterfaceAuthoring.PrefabPath);
            var prefabAudio = prefab == null ? null : prefab.GetComponentInChildren<DefenseAudio>(true);
            if (prefabAudio == null) throw new InvalidOperationException("正式界面没有 DefenseAudio 组件。");
            // 已经绑定同一资源库时不重存整个界面预制体。
            if (new SerializedObject(prefabAudio).FindProperty("soundLibrary").objectReferenceValue != library)
            {
                var root = PrefabUtility.LoadPrefabContents(DefenseInterfaceAuthoring.PrefabPath);
                try
                {
                    var serialized = new SerializedObject(root.GetComponentInChildren<DefenseAudio>(true));
                    serialized.FindProperty("soundLibrary").objectReferenceValue = library;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    PrefabUtility.SaveAsPrefabAsset(root, DefenseInterfaceAuthoring.PrefabPath);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssetIfDirty(library);
            Debug.Log("工业科幻音效已接入：" + definitions.Count + " 类声音，" + clipCount +
                " 个变体。仅更新音效库与正式界面引用，背景音乐保持原有设置。");
        }

        private static void ConfigureImport(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null) throw new InvalidOperationException("请先让 Unity 导入新音效：" + path);
            var settings = importer.defaultSampleSettings;
            bool changed = settings.loadType != AudioClipLoadType.DecompressOnLoad ||
                settings.compressionFormat != AudioCompressionFormat.PCM ||
                settings.sampleRateSetting != AudioSampleRateSetting.PreserveSampleRate ||
                !settings.preloadAudioData || importer.forceToMono || importer.loadInBackground;
            if (!changed) return;
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.PCM;
            settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            settings.preloadAudioData = true;
            importer.defaultSampleSettings = settings;
            // 保留成品原有声道与电平，避免强制混音和默认标准化改变素材。
            importer.forceToMono = false;
            importer.loadInBackground = false;
            importer.SaveAndReimport();
        }

        private static DefenseSoundDefinition CreateDefinition(DefenseSoundCue cue, AudioClip[] clips)
        {
            float gain = .6f, spatial = .35f, interval = .08f, variation = .035f;
            int concurrent = 3, priority = 55;
            bool combat = false, duck = false, loop = false;
            switch (cue)
            {
                case DefenseSoundCue.RapidFire:
                    gain = .48f; interval = .045f; concurrent = 4; priority = 35; combat = true; break;
                case DefenseSoundCue.HeavyFire:
                    gain = .64f; interval = .075f; priority = 45; combat = true; break;
                case DefenseSoundCue.AreaFire:
                    gain = .6f; interval = .09f; priority = 43; combat = true; break;
                case DefenseSoundCue.RailgunFire:
                    gain = .72f; interval = .07f; priority = 68; combat = true; break;
                case DefenseSoundCue.RailgunCharge:
                    gain = .38f; interval = 0; concurrent = 3; priority = 25; combat = true; loop = true; variation = 0; break;
                case DefenseSoundCue.MetalImpact:
                    gain = .72f; spatial = .22f; interval = .045f; concurrent = 4; priority = 62; combat = true; break;
                case DefenseSoundCue.HeavyImpact:
                case DefenseSoundCue.RailgunImpact:
                    gain = .78f; spatial = .25f; interval = .055f; priority = 68; combat = true; break;
                case DefenseSoundCue.AreaExplosion:
                    gain = .72f; interval = .09f; priority = 64; combat = true; break;
                case DefenseSoundCue.EnemyDeath:
                    gain = .88f; spatial = .12f; interval = .065f; concurrent = 4; priority = 82; combat = true; duck = true; break;
                case DefenseSoundCue.HeavyEnemyDeath:
                    gain = .9f; spatial = .18f; interval = .09f; concurrent = 3; priority = 86; combat = true; duck = true; break;
                case DefenseSoundCue.BuildingHit:
                    gain = .7f; interval = .11f; priority = 72; combat = true; break;
                case DefenseSoundCue.CoreHit:
                    gain = .8f; spatial = .1f; interval = .14f; concurrent = 2; priority = 88; combat = true; duck = true; break;
                case DefenseSoundCue.BuildingDestroyed:
                    gain = .86f; spatial = .2f; interval = .12f; priority = 90; combat = true; duck = true; break;
                case DefenseSoundCue.ShieldHit:
                    gain = .66f; spatial = .2f; interval = .12f; priority = 70; combat = true; break;
                case DefenseSoundCue.ShieldBreak:
                    gain = .85f; spatial = .1f; interval = .6f; concurrent = 1; priority = 92; combat = true; duck = true; break;
                case DefenseSoundCue.ShieldRestore:
                    gain = .6f; spatial = .1f; interval = 2; concurrent = 1; priority = 76; break;
                case DefenseSoundCue.EnergyStarved:
                case DefenseSoundCue.EnergyRestored:
                    gain = .56f; spatial = 0; interval = 3; concurrent = 1; priority = 70; break;
                case DefenseSoundCue.Build:
                case DefenseSoundCue.Repair:
                case DefenseSoundCue.Move:
                case DefenseSoundCue.Sell:
                    gain = .68f; spatial = .2f; interval = .08f; priority = 74; break;
                case DefenseSoundCue.UiClick:
                    gain = .55f; spatial = 0; interval = .055f; concurrent = 2; priority = 60; variation = .02f; break;
                case DefenseSoundCue.Selection:
                    gain = .25f; spatial = 0; interval = .08f; concurrent = 2; priority = 60; variation = .02f; break;
                case DefenseSoundCue.UiBack:
                case DefenseSoundCue.UiConfirm:
                case DefenseSoundCue.Pause:
                case DefenseSoundCue.Resume:
                    gain = .66f; spatial = 0; interval = .09f; concurrent = 2; priority = 78; variation = .015f; break;
                case DefenseSoundCue.UiDenied:
                    gain = .72f; spatial = 0; interval = .35f; concurrent = 1; priority = 80; variation = .015f; break;
                case DefenseSoundCue.WaveStart:
                case DefenseSoundCue.WaveClear:
                    gain = .76f; spatial = 0; interval = .8f; concurrent = 1; priority = 94; duck = true; variation = 0; break;
                case DefenseSoundCue.Victory:
                case DefenseSoundCue.Defeat:
                    gain = .85f; spatial = 0; interval = 2; concurrent = 1; priority = 100; duck = true; variation = 0; break;
                case DefenseSoundCue.CoreWarning:
                    gain = .8f; spatial = 0; interval = 4; concurrent = 1; priority = 96; duck = true; variation = 0; break;
                case DefenseSoundCue.EnemyAttack:
                    gain = .48f; interval = .14f; concurrent = 2; priority = 30; combat = true; break;
                case DefenseSoundCue.HeavyEnemyAttack:
                    gain = .58f; interval = .18f; concurrent = 2; priority = 40; combat = true; break;
                default: throw new ArgumentOutOfRangeException(nameof(cue), cue, "尚未定义声音混音规则。");
            }
            return new DefenseSoundDefinition(cue, clips, gain, spatial, interval, concurrent, priority, combat, duck, loop, variation);
        }
    }
}
