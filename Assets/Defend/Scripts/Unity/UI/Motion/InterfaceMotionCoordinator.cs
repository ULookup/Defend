using System.Collections.Generic;
using Defend.Simulation;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Defend.Unity
{
    /// <summary>Coordinates saved presentation components without delaying authoritative game state.</summary>
    public sealed class InterfaceMotionCoordinator : MonoBehaviour
    {
        [SerializeField] private InterfaceMotionSettings settings;
        [SerializeField] private GameObject[] panelRoots;
        [SerializeField] private InterfacePanelMotion[] panelMotions;
        [SerializeField] private GameObject[] modalRoots;
        [SerializeField] private InterfacePanelMotion detailContent;
        [SerializeField] private CanvasGroup waveAnnouncement;
        [SerializeField] private RectTransform waveVisual, waveLine;
        [SerializeField] private Text waveTitle;
        [SerializeField] private GameObject coreDanger;
        [SerializeField] private Text coreDangerText;
        private object observedWorld;
        private BattleState previousState;
        private int previousWave;
        private long selectedBuilding, selectedEnemy;
        private bool observedSelection, announcementActive;
        private float announcementElapsed;
        private Vector2 waveRestPosition;
        private GameObject focusedModal, returnFocus;
        private bool modalFocusPending, returnFocusPending;
        private readonly Dictionary<GameObject, GameObject> modalFocus = new Dictionary<GameObject, GameObject>();
        private bool initialized;
        public bool AnnouncementActive => announcementActive;
        public bool AnyModalExiting
        {
            get
            {
                if (modalRoots == null) return false;
                foreach (var panel in modalRoots)
                {
                    if (panel == null || !panel.activeInHierarchy) continue;
                    var motion = FindPanel(panel);
                    if (motion != null && motion.IsExiting) return true;
                }
                return false;
            }
        }

        private void Awake() { Initialize(); }
        private void Initialize()
        {
            if (initialized) return;
            initialized = true;
            if (waveVisual != null) waveRestPosition = waveVisual.anchoredPosition;
            HideAnnouncement();
        }
        private void OnEnable()
        {
            Initialize();
            if (settings != null) settings.Changed += OnPreferenceChanged;
        }
        private void OnDisable()
        {
            if (settings != null) settings.Changed -= OnPreferenceChanged;
            ResetPresentation();
        }
        private void OnPreferenceChanged(bool reduced)
        {
            if (!reduced || !announcementActive) return;
            if (waveVisual != null) waveVisual.anchoredPosition = waveRestPosition;
            if (waveLine != null) waveLine.localScale = Vector3.one;
            if (waveAnnouncement != null) waveAnnouncement.alpha = 1;
        }
        public void ResetPresentation()
        {
            if (panelMotions != null)
                foreach (var motion in panelMotions)
                    if (motion != null) motion.SetVisible(false, true);
            observedWorld = null;
            observedSelection = false;
            focusedModal = returnFocus = null;
            modalFocusPending = returnFocusPending = false;
            modalFocus.Clear();
            HideAnnouncement();
            if (coreDanger != null) coreDanger.SetActive(false);
        }
        public void SetPanelVisible(GameObject panel, bool visible)
        {
            if (panel == null) return;
            var motion = FindPanel(panel);
            if (!visible && panel == focusedModal) RememberModalFocus();
            bool replacingModal = false;
            bool opening = visible && (!panel.activeSelf || motion != null && !motion.TargetVisible);
            if (opening && IsModal(panel))
            {
                foreach (var candidate in modalRoots)
                {
                    if (candidate == null || candidate == panel || !candidate.activeInHierarchy ||
                        candidate.transform.IsChildOf(panel.transform) || panel.transform.IsChildOf(candidate.transform)) continue;
                    RememberModalFocus();
                    var previousMotion = FindPanel(candidate);
                    if (previousMotion != null) previousMotion.SetVisible(false, true);
                    else candidate.SetActive(false);
                    replacingModal = true;
                }
            }
            // Same-level page changes have one backdrop and one readable page throughout the handoff.
            if (motion != null) motion.SetVisible(visible, false, replacingModal ? 0 : -1, replacingModal ? .16f : -1, replacingModal);
            else panel.SetActive(visible);
        }
        private bool IsModal(GameObject panel)
        {
            if (modalRoots == null) return false;
            foreach (var candidate in modalRoots) if (candidate == panel) return true;
            return false;
        }
        private InterfacePanelMotion FindPanel(GameObject panel)
        {
            if (panelRoots == null || panelMotions == null) return null;
            for (int i = 0; i < panelRoots.Length && i < panelMotions.Length; i++)
                if (panelRoots[i] == panel) return panelMotions[i];
            return null;
        }
        public void Observe(GameSessionRoot root)
        {
            Initialize();
            if (root == null || root.World == null) return;
            var battle = root.Battle;
            bool newWorld = !ReferenceEquals(observedWorld, root.World);
            if (newWorld)
            {
                observedWorld = root.World;
                previousState = battle.State;
                previousWave = battle.WaveNumber;
                observedSelection = false;
                HideAnnouncement();
            }
            if (!root.AtStartPage && battle.State == BattleState.Combat &&
                (previousState != BattleState.Combat || previousWave != battle.WaveNumber))
                ShowAnnouncement(battle.WaveNumber);
            if (root.AtStartPage || battle.State != BattleState.Combat) HideAnnouncement();
            previousState = battle.State; previousWave = battle.WaveNumber;

            long building = root.SelectedBuilding?.Id ?? 0;
            long enemy = root.SelectedEnemy?.Id ?? 0;
            if (observedSelection && (building != selectedBuilding || enemy != selectedEnemy) && (building != 0 || enemy != 0))
                detailContent?.ReplayContent();
            selectedBuilding = building; selectedEnemy = enemy; observedSelection = true;
            var core = root.World.FindBuilding(battle.CoreBuildingId);
            bool danger = !root.AtStartPage && !battle.IsFinished && core != null && core.IsAlive &&
                core.Health <= core.Definition.MaximumHealth * .25;
            if (coreDanger != null) coreDanger.SetActive(danger);
            if (coreDangerText != null && danger) coreDangerText.text = "核心生命危急";
            UpdateModalFocus();
        }
        private void UpdateModalFocus()
        {
            var events = EventSystem.current;
            if (events == null) return;
            GameObject target = null;
            if (modalRoots != null)
                foreach (var candidate in modalRoots)
                {
                    if (candidate == null || !candidate.activeInHierarchy) continue;
                    var motion = FindPanel(candidate);
                    if (motion == null || motion.TargetVisible) target = candidate;
                }
            if (target != focusedModal)
            {
                RememberModalFocus();
                if (target != null && !returnFocusPending)
                {
                    returnFocus = events.currentSelectedGameObject;
                    returnFocusPending = true;
                }
                focusedModal = target;
                modalFocusPending = target != null;
                events.SetSelectedGameObject(null);
            }
            if (target != null && modalFocusPending)
            {
                var motion = FindPanel(target);
                if (motion != null && !motion.AcceptsInput) return;
                GameObject first = null;
                if (modalFocus.TryGetValue(target, out var remembered) && remembered != null &&
                    remembered.transform.IsChildOf(target.transform))
                {
                    var rememberedControl = remembered.GetComponent<Selectable>();
                    if (rememberedControl != null && rememberedControl.IsActive() && rememberedControl.IsInteractable()) first = remembered;
                }
                var controls = target.GetComponentsInChildren<Selectable>(false);
                if (first == null)
                    foreach (var control in controls)
                        if (control.IsActive() && control.IsInteractable()) { first = control.gameObject; break; }
                // A nested confirmation may still keep the returning page's content disabled.
                if (first == null) return;
                events.SetSelectedGameObject(first);
                modalFocusPending = false;
            }
            else if (target == null && returnFocusPending && !AnyModalExiting)
            {
                var control = returnFocus != null ? returnFocus.GetComponent<Selectable>() : null;
                events.SetSelectedGameObject(control != null && control.IsActive() && control.IsInteractable() ? returnFocus : null);
                returnFocus = null;
                returnFocusPending = false;
                modalFocus.Clear();
            }
        }
        private void RememberModalFocus()
        {
            var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (focusedModal != null && selected != null && selected.transform.IsChildOf(focusedModal.transform))
                modalFocus[focusedModal] = selected;
        }
        private void ShowAnnouncement(int waveNumber)
        {
            announcementActive = true; announcementElapsed = 0;
            if (waveTitle != null) waveTitle.text = "第 " + waveNumber + " 波  ·  防守开始";
            if (waveAnnouncement != null)
            {
                waveAnnouncement.gameObject.SetActive(true);
                waveAnnouncement.blocksRaycasts = false; waveAnnouncement.interactable = false;
                waveAnnouncement.alpha = settings != null && settings.ReducedMotion ? 1 : 0;
            }
        }
        private void Update()
        {
            if (!announcementActive) return;
            announcementElapsed += Time.unscaledDeltaTime;
            bool reduced = settings != null && settings.ReducedMotion;
            // Let the dock begin retracting before the wave title takes visual priority.
            float timeline = announcementElapsed - (reduced ? 0 : .1f);
            if (timeline >= 1.5f) { HideAnnouncement(); return; }
            float enter = Mathf.Clamp01(timeline / .36f);
            float eased = 1 - Mathf.Pow(1 - enter, 3);
            float leave = Mathf.Clamp01((timeline - 1.36f) / .14f);
            if (waveAnnouncement != null) waveAnnouncement.alpha = reduced ? 1 : Mathf.Min(eased, 1 - leave);
            if (waveVisual != null) waveVisual.anchoredPosition = waveRestPosition + (reduced ? Vector2.zero : Vector2.down * (8 * (1 - eased)));
            if (waveLine != null) waveLine.localScale = new Vector3(reduced ? 1 : Mathf.Lerp(.2f, 1, eased), 1, 1);
        }
        private void HideAnnouncement()
        {
            announcementActive = false;
            if (waveAnnouncement != null)
            {
                waveAnnouncement.alpha = 0; waveAnnouncement.blocksRaycasts = false; waveAnnouncement.interactable = false;
                waveAnnouncement.gameObject.SetActive(false);
            }
            if (waveVisual != null) waveVisual.anchoredPosition = waveRestPosition;
            if (waveLine != null) waveLine.localScale = Vector3.one;
        }
    }
}
