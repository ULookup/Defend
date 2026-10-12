using UnityEngine;

namespace Defend.Unity
{
    /// <summary>只持久化用户音量；不保存或恢复本局状态。两个通道即时应用设置。</summary>
    public sealed class DefenseAudio : MonoBehaviour
    {
        [SerializeField] private AudioSource musicSource, effectsSource;
        public float MusicVolume { get; private set; }
        public float EffectsVolume { get; private set; }
        public bool Muted { get; private set; }
        public void Load() { MusicVolume = Mathf.Clamp01(PlayerPrefs.GetFloat("Defend.MusicVolume", .35f)); EffectsVolume = Mathf.Clamp01(PlayerPrefs.GetFloat("Defend.EffectsVolume", .65f)); Muted = PlayerPrefs.GetInt("Defend.Muted", 0) != 0; Apply(); }
        public void SetMusic(float value) { MusicVolume = Mathf.Clamp01(value); Save(); }
        public void SetEffects(float value) { EffectsVolume = Mathf.Clamp01(value); Save(); PlayClick(); }
        public void SetMuted(bool value) { Muted = value; Save(); }
        private void Save() { PlayerPrefs.SetFloat("Defend.MusicVolume", MusicVolume); PlayerPrefs.SetFloat("Defend.EffectsVolume", EffectsVolume); PlayerPrefs.SetInt("Defend.Muted", Muted ? 1 : 0); PlayerPrefs.Save(); Apply(); }
        private void Apply() { if (musicSource != null) { musicSource.volume = Muted ? 0 : MusicVolume; if (!musicSource.isPlaying && musicSource.clip != null) musicSource.Play(); } if (effectsSource != null) effectsSource.volume = Muted ? 0 : EffectsVolume; }
        public void PlayClick() { if (effectsSource != null && effectsSource.clip != null) effectsSource.PlayOneShot(effectsSource.clip); }
    }
}
