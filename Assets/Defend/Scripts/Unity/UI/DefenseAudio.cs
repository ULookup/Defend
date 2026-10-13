using UnityEngine;

namespace Defend.Unity
{
    /// <summary>只持久化用户音量；不保存或恢复本局状态。两个通道即时应用设置。</summary>
    public sealed class DefenseAudio : MonoBehaviour
    {
        [SerializeField] private AudioSource musicSource, effectsSource;
        public float MusicVolume { get; private set; }
        public float EffectsVolume { get; private set; }
        public bool MusicMuted { get; private set; }
        public bool EffectsMuted { get; private set; }
        public bool Muted => MusicMuted && EffectsMuted;
        public event System.Action Changed;
        private bool pendingSave;
        private float saveAfter;

        public void Load()
        {
            MusicVolume = SafeVolume(PlayerPrefs.GetFloat("Defend.MusicVolume", .7f));
            EffectsVolume = SafeVolume(PlayerPrefs.GetFloat("Defend.EffectsVolume", .7f));
            bool legacy = PlayerPrefs.GetInt("Defend.AudioSettingsVersion", 0) < 2 && PlayerPrefs.GetInt("Defend.Muted", 0) != 0;
            MusicMuted = PlayerPrefs.GetInt("Defend.MusicMuted", legacy ? 1 : 0) != 0;
            EffectsMuted = PlayerPrefs.GetInt("Defend.EffectsMuted", legacy ? 1 : 0) != 0;
            Save();
        }
        public void SetMusic(float value) { MusicVolume = SafeVolume(value); MusicMuted = false; Save(); }
        public void SetEffects(float value) { EffectsVolume = SafeVolume(value); EffectsMuted = false; Save(); PlayClick(); }
        public void SetMusicMuted(bool value) { MusicMuted = value; Save(); }
        public void SetEffectsMuted(bool value) { EffectsMuted = value; Save(); }
        // 保留旧总静音 API，供开发工具兼容调用；正式页面使用独立开关。
        public void SetMuted(bool value) { MusicMuted = EffectsMuted = value; Save(); }
        private static float SafeVolume(float value) { return float.IsNaN(value) || float.IsInfinity(value) ? .7f : Mathf.Clamp01(value); }
        private void Save()
        {
            PlayerPrefs.SetFloat("Defend.MusicVolume", MusicVolume); PlayerPrefs.SetFloat("Defend.EffectsVolume", EffectsVolume);
            PlayerPrefs.SetInt("Defend.MusicMuted", MusicMuted ? 1 : 0); PlayerPrefs.SetInt("Defend.EffectsMuted", EffectsMuted ? 1 : 0);
            PlayerPrefs.SetInt("Defend.AudioSettingsVersion", 2);
            pendingSave = true; saveAfter = Time.unscaledTime + .35f;
            Apply(); Changed?.Invoke();
        }
        public void Flush() { if (pendingSave) { PlayerPrefs.Save(); pendingSave = false; } }
        private void Update() { if (pendingSave && Time.unscaledTime >= saveAfter) Flush(); }
        private void OnDisable() { Flush(); }
        private void OnApplicationPause(bool paused) { if (paused) Flush(); }
        private void OnApplicationQuit() { Flush(); }
        private void Apply()
        {
            if (musicSource != null) { musicSource.volume = MusicVolume; musicSource.mute = MusicMuted; if (!musicSource.isPlaying && musicSource.clip != null) musicSource.Play(); }
            if (effectsSource != null) { effectsSource.volume = EffectsVolume; effectsSource.mute = EffectsMuted; }
        }
        public void PlayClick() { if (effectsSource != null && effectsSource.clip != null) effectsSource.PlayOneShot(effectsSource.clip); }
    }
}
