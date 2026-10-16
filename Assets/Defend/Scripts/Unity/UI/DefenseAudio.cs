using Defend.Simulation;
using UnityEngine;

namespace Defend.Unity
{
    /// <summary>统一管理阶段音乐与有限声部音效，只持久化用户音量与静音设置。</summary>
    public sealed class DefenseAudio : MonoBehaviour
    {
        [SerializeField] private AudioSource musicSource, effectsSource;
        [SerializeField] private AudioClip outgameMusic, preparationMusic, combatMusic;
        [SerializeField] private DefenseSoundLibrary soundLibrary;
        [SerializeField, Min(0)] private float fadeDuration = .4f;
        private AudioClip requestedMusic;
        private float musicGain;
        private DefenseSoundPlayer soundPlayer;
        public int ActiveEffectVoices => soundPlayer?.ActiveCount ?? 0;
        public int PlayedEffectCount => soundPlayer?.PlayedCount ?? 0;
        public int SuppressedEffectCount => soundPlayer?.SuppressedCount ?? 0;
        public float MusicVolume { get; private set; }
        public float EffectsVolume { get; private set; }
        public bool Muted { get; private set; }
        public void Load()
        {
            MusicVolume = Mathf.Clamp01(PlayerPrefs.GetFloat("Defend.MusicVolume", .35f));
            EffectsVolume = Mathf.Clamp01(PlayerPrefs.GetFloat("Defend.EffectsVolume", .65f));
            Muted = PlayerPrefs.GetInt("Defend.Muted", 0) != 0;
            if (soundPlayer == null && soundLibrary != null)
                soundPlayer = new DefenseSoundPlayer(transform, effectsSource, soundLibrary);
            Apply();
        }
        public void SetMusic(float value) { MusicVolume = Mathf.Clamp01(value); Save(); }
        public void SetEffects(float value) { EffectsVolume = Mathf.Clamp01(value); Save(); PlayClick(); }
        public void SetMuted(bool value) { Muted = value; Save(); }
        private void Save() { PlayerPrefs.SetFloat("Defend.MusicVolume", MusicVolume); PlayerPrefs.SetFloat("Defend.EffectsVolume", EffectsVolume); PlayerPrefs.SetInt("Defend.Muted", Muted ? 1 : 0); PlayerPrefs.Save(); Apply(); }
        public void Present(bool atStartPage, BattleState state)
        {
            requestedMusic = atStartPage || state == BattleState.Victory || state == BattleState.Defeat
                ? outgameMusic : state == BattleState.Combat ? combatMusic : preparationMusic;
        }

        private void Update()
        {
            soundPlayer?.RefreshVolumes();
            if (musicSource == null) return;
            // 使用真实时间淡入淡出，暂停和模拟倍速不会改变音乐节奏。
            float step = fadeDuration <= 0 ? 1 : Time.unscaledDeltaTime / fadeDuration;
            if (musicSource.clip != requestedMusic)
            {
                musicGain = Mathf.MoveTowards(musicGain, 0, step);
                if (musicGain <= 0)
                {
                    musicSource.Stop();
                    musicSource.clip = requestedMusic;
                    musicSource.volume = 0;
                    musicSource.loop = true;
                    musicSource.spatialBlend = 0;
                    musicSource.pitch = 1;
                    if (requestedMusic != null) musicSource.Play();
                }
            }
            else if (requestedMusic != null)
            {
                if (!musicSource.isPlaying) musicSource.Play();
                musicGain = Mathf.MoveTowards(musicGain, 1, step);
            }
            Apply();
        }

        private void OnDisable()
        {
            soundPlayer?.StopAll(true);
            musicGain = 0;
            if (musicSource != null) { musicSource.Stop(); musicSource.volume = 0; }
        }

        private void Apply()
        {
            if (musicSource != null) musicSource.volume = Muted ? 0 : MusicVolume * musicGain;
            if (soundPlayer != null) soundPlayer.SetVolume(EffectsVolume, Muted);
            else if (effectsSource != null) effectsSource.volume = Muted ? 0 : EffectsVolume;
        }
        public void PlayClick() => Play(DefenseSoundCue.UiClick);
        public void Play(DefenseSoundCue cue) => soundPlayer?.Play(cue, Vector3.zero, false);
        public void PlayAt(DefenseSoundCue cue, Vector3 position) => soundPlayer?.Play(cue, position, true);
        public void ResetSession() => soundPlayer?.ResetSession();
        public void SetCombatPaused(bool paused) => soundPlayer?.SetCombatPaused(paused);
        public void BeginPresentationStep() => soundPlayer?.BeginPresentationStep();
        public void EndPresentationStep() => soundPlayer?.EndPresentationStep();
        public void SetRailgunCharge(long turretId, Vector3 position, float progress, bool active)
            => soundPlayer?.SetRailgunCharge(turretId, position, progress, active);
        public void StopChargeSounds() => soundPlayer?.StopCharges();
    }
}
