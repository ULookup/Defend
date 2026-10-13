using UnityEngine;
using UnityEngine.EventSystems;

namespace Defend.Unity
{
    /// <summary>已保存控件的绑定与状态显示；音量和持久化由 DefenseAudio 统一拥有。</summary>
    public sealed class DefenseSettingsView : MonoBehaviour
    {
        [SerializeField] private UnityEngine.UI.Slider musicSlider, effectsSlider;
        [SerializeField] private UnityEngine.UI.Button musicMute, effectsMute, topClose, bottomClose;
        [SerializeField] private UnityEngine.UI.Text musicValue, effectsValue;
        [SerializeField] private Sprite muteNormal, mutePressed, muteOff;
        private DefenseAudio audioSettings;
        private bool initialized;
        private GameObject previousSelection;

        public void Initialize(DefenseAudio audio, System.Action close)
        {
            if (initialized) return;
            audioSettings = audio; initialized = true;
            musicSlider.onValueChanged.AddListener(value => audioSettings.SetMusic(value / 100f));
            effectsSlider.onValueChanged.AddListener(value => audioSettings.SetEffects(value / 100f));
            musicMute.onClick.AddListener(() => audioSettings.SetMusicMuted(!audioSettings.MusicMuted));
            effectsMute.onClick.AddListener(() => audioSettings.SetEffectsMuted(!audioSettings.EffectsMuted));
            topClose.onClick.AddListener(() => { audioSettings.Flush(); close(); });
            bottomClose.onClick.AddListener(() => { audioSettings.Flush(); close(); });
            if (isActiveAndEnabled) { audioSettings.Changed += Refresh; Focus(); }
            Refresh();
        }
        private void OnEnable() { if (initialized) { audioSettings.Changed += Refresh; Refresh(); Focus(); } }
        private void OnDisable()
        {
            if (initialized)
            {
                audioSettings.Changed -= Refresh; audioSettings.Flush();
                if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(previousSelection != null && previousSelection.activeInHierarchy ? previousSelection : null);
            }
        }
        private void Focus()
        {
            if (EventSystem.current == null) return;
            previousSelection = EventSystem.current.currentSelectedGameObject;
            EventSystem.current.SetSelectedGameObject(musicSlider.gameObject);
        }
        private void OnDestroy() { if (audioSettings != null) audioSettings.Changed -= Refresh; }
        private void Refresh()
        {
            musicSlider.SetValueWithoutNotify(Mathf.RoundToInt(audioSettings.MusicVolume * 100));
            effectsSlider.SetValueWithoutNotify(Mathf.RoundToInt(audioSettings.EffectsVolume * 100));
            musicValue.text = Mathf.RoundToInt(audioSettings.MusicVolume * 100).ToString();
            effectsValue.text = Mathf.RoundToInt(audioSettings.EffectsVolume * 100).ToString();
            ShowMute(musicMute, audioSettings.MusicMuted); ShowMute(effectsMute, audioSettings.EffectsMuted);
        }
        private void ShowMute(UnityEngine.UI.Button button, bool muted)
        {
            var image = (UnityEngine.UI.Image)button.targetGraphic;
            image.sprite = muted ? muteOff : muteNormal;
            var state = button.spriteState;
            state.highlightedSprite = state.pressedSprite = state.selectedSprite = muted ? muteOff : mutePressed;
            button.spriteState = state;
            // 清除先前选择状态的覆盖图，基础图本身始终准确表达静音状态。
            image.overrideSprite = muted ? muteOff : muteNormal;
        }
    }
}
