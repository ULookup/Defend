using System;
using System.Collections.Generic;
using UnityEngine;

namespace Defend.Unity
{
    public enum DefenseSoundCue
    {
        RapidFire, HeavyFire, AreaFire, RailgunFire, RailgunCharge,
        MetalImpact, HeavyImpact, AreaExplosion, RailgunImpact,
        EnemyDeath, HeavyEnemyDeath, BuildingHit, BuildingDestroyed,
        ShieldHit, ShieldBreak, ShieldRestore, EnergyStarved, EnergyRestored,
        Build, Repair, Move, Sell, UiClick, UiBack, UiDenied,
        WaveStart, WaveClear, Victory, Defeat, CoreWarning,
        UiConfirm, Pause, Resume, Selection, EnemyAttack, HeavyEnemyAttack,
        CoreHit
    }

    [Serializable]
    public sealed class DefenseSoundDefinition
    {
        [SerializeField] private DefenseSoundCue cue;
        [SerializeField] private AudioClip[] clips;
        [SerializeField, Range(0, 1)] private float gain = .7f;
        [SerializeField, Range(0, 1)] private float spatialBlend = .45f;
        [SerializeField, Min(0)] private float minimumInterval = .06f;
        [SerializeField, Range(1, 24)] private int maximumConcurrent = 3;
        [SerializeField, Range(0, 100)] private int priority = 50;
        [SerializeField] private bool combat;
        [SerializeField] private bool duckWeapons;
        [SerializeField] private bool loop;
        [SerializeField] private Vector2 pitchRange = new Vector2(.97f, 1.03f);

        public DefenseSoundCue Cue => cue;
        public IReadOnlyList<AudioClip> Clips => clips;
        public float Gain => gain;
        public float SpatialBlend => spatialBlend;
        public float MinimumInterval => minimumInterval;
        public int MaximumConcurrent => maximumConcurrent;
        public int Priority => priority;
        public bool Combat => combat;
        public bool DuckWeapons => duckWeapons;
        public bool Loop => loop;
        public Vector2 PitchRange => pitchRange;

        public DefenseSoundDefinition(DefenseSoundCue cue, AudioClip[] clips, float gain, float spatialBlend,
            float minimumInterval, int maximumConcurrent, int priority, bool combat,
            bool duckWeapons = false, bool loop = false, float pitchVariation = .035f)
        {
            this.cue = cue; this.clips = clips; this.gain = gain; this.spatialBlend = spatialBlend;
            this.minimumInterval = minimumInterval; this.maximumConcurrent = maximumConcurrent;
            this.priority = priority; this.combat = combat; this.duckWeapons = duckWeapons; this.loop = loop;
            pitchRange = new Vector2(1 - pitchVariation, 1 + pitchVariation);
        }
    }

    /// <summary>保存可编辑的声音、变体与混音规则；运行时不读取磁盘目录。</summary>
    [CreateAssetMenu(menuName = "涌现防线/音效资源库")]
    public sealed class DefenseSoundLibrary : ScriptableObject
    {
        [SerializeField] private DefenseSoundDefinition[] sounds = Array.Empty<DefenseSoundDefinition>();
        public IReadOnlyList<DefenseSoundDefinition> Sounds => sounds;
        public void Configure(DefenseSoundDefinition[] definitions) => sounds = definitions;
    }
}
