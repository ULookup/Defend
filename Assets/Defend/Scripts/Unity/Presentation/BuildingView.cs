using Defend.Simulation;
using UnityEngine;

namespace Defend.Unity
{
    /// <summary>建筑表现只读取模拟快照，生命和护盾由统一显示时刻提交。</summary>
    public sealed class BuildingView : MonoBehaviour
    {
        [SerializeField] private BuildingDefinitionAsset definition;
        [SerializeField] private Transform visualRoot;
        [SerializeField] private GameObject selectionRoot;
        [SerializeField] private Transform energyFill;
        [SerializeField] private GameObject destroyedMarker;
        [SerializeField] private Renderer feedbackRenderer;
        [SerializeField] private HealthBarView healthBar;
        [SerializeField] private BuildingEnergyBarView worldEnergyBar;
        [SerializeField] private ShieldPresentation shieldPresentation;
        [SerializeField] private CoreReactorMotion coreReactorMotion;
        [SerializeField] private ResonanceMagneticTowerMotion resonanceMotion;
        [SerializeField] private ReactorUpperRotation importedReactorRotation;
        [SerializeField] private BatteryChargePresentation batteryCharge;
        [SerializeField] private DangchenjiMechanicalMotion dangchenjiMotion;
        [SerializeField] private Vector3 authoredVisualScale = Vector3.one;
        [SerializeField] private float energyBarHeight = 1.5f;
        private MaterialPropertyBlock feedbackProperties;
        private CombatModelAnchors modelAnchors;
        private int appliedOrientationQuarterTurns = -1;
        public BuildingDefinitionAsset Definition => definition;
        public ShieldPresentation Shield => shieldPresentation;
        public CoreReactorMotion ReactorMotion => coreReactorMotion;
        public ResonanceMagneticTowerMotion ResonanceMotion => resonanceMotion;
        public ReactorUpperRotation ImportedReactorRotation => importedReactorRotation;
        public DangchenjiMechanicalMotion DangchenjiMotion => dangchenjiMotion;
        public Transform VisualRoot => visualRoot;
        public bool HasWorldEnergyBar => worldEnergyBar != null || (shieldPresentation != null && shieldPresentation.HasWorldEnergyBar);
        public CombatModelAnchors ModelAnchors => modelAnchors != null ? modelAnchors : modelAnchors = visualRoot.GetComponent<CombatModelAnchors>();
        public long BuildingId { get; private set; }
        public void ResetForReuse()
        {
            transform.rotation = Quaternion.identity;
            ModelAnchors?.ResetPose(); healthBar?.ResetState(); worldEnergyBar?.ResetState(); shieldPresentation?.ResetState();
            coreReactorMotion?.ResetPose(); resonanceMotion?.ResetPose(); importedReactorRotation?.ResetPose();
            batteryCharge?.ResetState();
            dangchenjiMotion?.ResetPose();
            BuildingId = 0; appliedOrientationQuarterTurns = -1;
        }
        public void Configure(BuildingDefinitionAsset asset, Transform visual, GameObject selection, Transform fill, GameObject destroyed)
        { definition = asset; visualRoot = visual; modelAnchors = null; selectionRoot = selection; energyFill = fill; destroyedMarker = destroyed; }
        public void ConfigureImpact(HealthBarView health, ShieldPresentation shield) { healthBar = health; shieldPresentation = shield; }
        public void ConfigureWorldEnergyBar(BuildingEnergyBarView bar) { worldEnergyBar = bar; }
        public void ConfigureBatteryCharge(BatteryChargePresentation charge) { batteryCharge = charge; }
        public void ConfigureDangchenjiMotion(DangchenjiMechanicalMotion motion) { dangchenjiMotion = motion; }
        public void ApplyEnergyStatus(BuildingEnergyStatus status)
        {
            if (definition.Kind == BuildingKind.Core && shieldPresentation != null) shieldPresentation.SetEnergyStatus(status);
            else worldEnergyBar?.SetStatus(status);
        }
        public void Apply(BuildingState state, bool selected, long tick = 0)
        {
            ApplyVisualState(state, selected, tick);
            ApplyVitals(state.Id, state.Health, state.Definition.MaximumHealth, state.StoredEnergy, state.Definition.EnergyCapacity, state.IsAlive && !state.IsSold);
        }
        /// <summary>只同步建筑空间姿态，允许在战斗表现读取挂点前执行，不提前提交生命或护盾。</summary>
        public void ApplyPlacementPose(BuildingState state)
        {
            bool poseChanged = BuildingId != state.Id || appliedOrientationQuarterTurns != state.OrientationQuarterTurns;
            BuildingId = state.Id;
            // 根节点带动模型、碰撞体、选择框和端口；炮塔随后仍接收世界瞄准角。
            transform.SetPositionAndRotation(new Vector3((float)state.CenterX, 0, (float)state.CenterZ),
                Quaternion.Euler(0, state.OrientationDegrees, 0));
            if (poseChanged)
                ModelAnchors?.ApplyAuthoritativeAim(state.Kind == BuildingKind.PiercingRailgun ? state.FacingDegrees : state.OrientationDegrees, 0);
            appliedOrientationQuarterTurns = state.OrientationQuarterTurns;
        }
        public void ApplyVisualState(BuildingState state, bool selected, long tick)
        {
            ApplyPlacementPose(state);
            if (state.Definition.Resonance != null) resonanceMotion?.ConfigureBurstRadius((float)state.Definition.Resonance.BurstRadius);
            selectionRoot.SetActive(selected);
            healthBar?.SetSelected(selected);
            worldEnergyBar?.SetSelected(selected);
            shieldPresentation?.SetSelected(selected);
            if (feedbackRenderer != null)
            {
                if (feedbackProperties == null) feedbackProperties = new MaterialPropertyBlock();
                Color color = state.CombatState == TurretCombatState.WaitingForEnergy ? new Color(1, .55f, .06f) :
                    state.CombatState == TurretCombatState.Fired ? Color.white : new Color(.2f, .8f, .6f);
                feedbackProperties.SetColor("_BaseColor", color); feedbackProperties.SetColor("_Color", color);
                feedbackRenderer.SetPropertyBlock(feedbackProperties);
            }
        }
        public void ApplyVitals(long id, int health, int maxHealth, long storedEnergy, long capacity, bool alive)
        {
            BuildingId = id;
            destroyedMarker.SetActive(!alive);
            coreReactorMotion?.SetAlive(alive);
            resonanceMotion?.SetAlive(alive);
            importedReactorRotation?.SetAlive(alive);
            batteryCharge?.SetEnergy(storedEnergy, capacity, alive);
            dangchenjiMotion?.SetEnergy(storedEnergy, capacity, alive);
            visualRoot.localScale = Vector3.Scale(authoredVisualScale, alive ? Vector3.one : new Vector3(1, .25f, 1));
            healthBar?.SetHealth(alive ? health : 0, maxHealth);
            if (definition.Kind == BuildingKind.Core && shieldPresentation != null)
            {
                shieldPresentation.Apply(storedEnergy, capacity, alive);
                worldEnergyBar?.SetEnergy(0, 0, false);
                if (energyFill != null) energyFill.gameObject.SetActive(false);
            }
            else if (worldEnergyBar != null)
            {
                bool requiresEnergy = definition.Kind != BuildingKind.ResonanceMagneticTower;
                worldEnergyBar.SetEnergy(storedEnergy, capacity, alive && requiresEnergy);
                if (energyFill != null) energyFill.gameObject.SetActive(false);
            }
            else if (energyFill != null)
            {
                if (definition.Kind == BuildingKind.ResonanceMagneticTower)
                { energyFill.gameObject.SetActive(false); return; }
                float fraction = capacity == 0 ? 0 : Mathf.Clamp01((float)storedEnergy / capacity);
                energyFill.localScale = new Vector3(Mathf.Max(.001f, fraction * .8f), .06f, .12f);
                energyFill.localPosition = new Vector3((fraction - 1) * .4f, energyBarHeight, 0);
            }
        }
        public void AdvanceFeedback(float seconds) { healthBar?.AdvanceFeedback(seconds); worldEnergyBar?.AdvanceFeedback(seconds); shieldPresentation?.AdvanceFeedback(seconds); }
        public void AdvanceShieldPresentation(float seconds) { shieldPresentation?.AdvancePresentation(seconds); }
        public void AdvanceModelPresentation(float seconds) { coreReactorMotion?.Advance(seconds); resonanceMotion?.Advance(seconds); importedReactorRotation?.Advance(seconds); dangchenjiMotion?.Advance(seconds); }
        public void NotifyShieldHit(Vector3 worldDirection)
        {
            if (definition.Kind == BuildingKind.Core) shieldPresentation?.NotifyHit(worldDirection);
        }
    }
}
