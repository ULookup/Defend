using Defend.Simulation;
using UnityEngine;

namespace Defend.Unity
{
    /// <summary>建筑表现只读取模拟状态。碰撞体和能量条都不参与逻辑结算。</summary>
    public sealed class BuildingView : MonoBehaviour
    {
        [SerializeField] private BuildingDefinitionAsset definition;
        [SerializeField] private Transform visualRoot;
        [SerializeField] private GameObject selectionRoot;
        [SerializeField] private Transform energyFill;
        [SerializeField] private GameObject destroyedMarker;
        [SerializeField] private Renderer feedbackRenderer;
        private MaterialPropertyBlock feedbackProperties;
        private CombatModelAnchors modelAnchors;
        public BuildingDefinitionAsset Definition => definition;
        public Transform VisualRoot => visualRoot;
        public CombatModelAnchors ModelAnchors => modelAnchors != null ? modelAnchors : modelAnchors = visualRoot.GetComponent<CombatModelAnchors>();
        public void ResetForReuse() { ModelAnchors?.ResetPose(); }
        public long BuildingId { get; private set; }

        public void Configure(BuildingDefinitionAsset asset, Transform visual, GameObject selection,
            Transform fill, GameObject destroyed)
        {
            definition = asset;
            visualRoot = visual;
            modelAnchors = null;
            selectionRoot = selection;
            energyFill = fill;
            destroyedMarker = destroyed;
        }

        public void Apply(BuildingState state, bool selected, long tick = 0)
        {
            BuildingId = state.Id;
            transform.position = new Vector3((float)state.CenterX, 0, (float)state.CenterZ);
            selectionRoot.SetActive(selected);
            destroyedMarker.SetActive(!state.IsAlive);
            visualRoot.localScale = state.IsAlive ? Vector3.one : new Vector3(1, 0.25f, 1);
            float fraction = state.Definition.EnergyCapacity == 0 ? 0 :
                Mathf.Clamp01((float)state.StoredEnergy / state.Definition.EnergyCapacity);
            energyFill.localScale = new Vector3(Mathf.Max(0.001f, fraction * 0.8f), 0.06f, 0.12f);
            energyFill.localPosition = new Vector3((fraction - 1) * 0.4f, 1.5f, 0);
            if (feedbackRenderer != null)
            {
                if (feedbackProperties == null) feedbackProperties = new MaterialPropertyBlock();
                Color color = !state.IsAlive ? new Color(0.65f, 0.08f, 0.08f) :
                    tick - state.LastRepairTick < 12 ? Color.green : tick - state.LastDamageTick < 8 ? Color.red :
                    state.CombatState == TurretCombatState.WaitingForEnergy ? new Color(1, 0.55f, 0.06f) :
                    state.CombatState == TurretCombatState.Fired ? Color.white : new Color(0.2f, 0.8f, 0.6f);
                feedbackProperties.SetColor("_BaseColor", color); feedbackProperties.SetColor("_Color", color);
                feedbackRenderer.SetPropertyBlock(feedbackProperties);
            }
        }
    }
}
