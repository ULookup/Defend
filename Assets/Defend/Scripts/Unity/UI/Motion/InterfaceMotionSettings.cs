using System;
using UnityEngine;
using UnityEngine.UI;

namespace Defend.Unity
{
    /// <summary>Owns the local accessibility preference; presentation components receive this reference explicitly.</summary>
    public sealed class InterfaceMotionSettings : MonoBehaviour
    {
        private const string PreferenceKey = "Defend.Interface.ReducedMotion";
        [SerializeField] private Toggle[] toggles;
        private bool initialized, reducedMotion;
        public event Action<bool> Changed;
        public bool ReducedMotion { get { Initialize(); return reducedMotion; } }

        private void Awake() { Initialize(); }
        private void Initialize()
        {
            if (initialized) return;
            initialized = true;
            reducedMotion = PlayerPrefs.GetInt(PreferenceKey, 0) != 0;
        }

        private void OnEnable()
        {
            Initialize();
            if (toggles == null) return;
            foreach (Toggle toggle in toggles)
            {
                if (toggle == null) continue;
                toggle.SetIsOnWithoutNotify(reducedMotion);
                toggle.onValueChanged.AddListener(SetReducedMotion);
            }
        }

        private void OnDisable()
        {
            if (toggles == null) return;
            foreach (Toggle toggle in toggles)
                if (toggle != null) toggle.onValueChanged.RemoveListener(SetReducedMotion);
        }

        public void SetReducedMotion(bool value)
        {
            Initialize();
            if (toggles != null)
                foreach (Toggle toggle in toggles)
                    if (toggle != null) toggle.SetIsOnWithoutNotify(value);
            if (reducedMotion == value) return;
            reducedMotion = value;
            PlayerPrefs.SetInt(PreferenceKey, value ? 1 : 0);
            PlayerPrefs.Save();
            Changed?.Invoke(value);
        }
    }
}
