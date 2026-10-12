using Defend.Simulation;
using UnityEngine;

namespace Defend.Unity
{
    /// <summary>血量由预制体内的几何条表现，敌人不携带动态 UI。</summary>
    public sealed class EnemyView : MonoBehaviour
    {
        [SerializeField] private Transform healthFill;
        [SerializeField] private Renderer bodyRenderer;
        [SerializeField] private Transform facingRoot;
        [SerializeField] private GameObject strikeFlash;
        [SerializeField] private bool industrialModel;
        [SerializeField] private MechanicalEnemyMotion mechanicalMotion;
        [SerializeField] private GameObject selectionMarker;
        public long EnemyId { get; private set; }
        public void ShowSelection(bool selected) { if (selectionMarker != null) selectionMarker.SetActive(selected); }
        public EnemyKind Kind { get; private set; }
        public Transform ModelRoot => facingRoot;
        private MaterialPropertyBlock bodyProperties;
        private Vector3 facingRestPosition;
        private Quaternion facingRestRotation;
        private bool hasRestPose;
        private int previousHealth;
        private Vector3 previousPosition;
        private Vector3 currentPosition;
        private bool hasPosition;
        public void Configure(Transform fill) { healthFill = fill; }
        public void ConfigureBehaviorVisuals(Transform facing, GameObject strike)
        { facingRoot = facing; strikeFlash = strike; }
        public void ResetForReuse()
        {
            hasPosition = false;
            EnemyId = 0;
            ShowSelection(false);
            previousHealth = 0;
            if (facingRoot != null)
            {
                if (!hasRestPose)
                {
                    facingRestPosition = facingRoot.localPosition;
                    facingRestRotation = facingRoot.localRotation;
                    hasRestPose = true;
                }
                facingRoot.localPosition = facingRestPosition;
                facingRoot.localRotation = facingRestRotation;
            }
            if (strikeFlash != null) strikeFlash.SetActive(false);
        }
        public void Apply(EnemyState state, long tick = 0, int ticksPerSecond = 20)
        {
            Kind = state.Kind;
            EnemyId = state.Id;
            Vector3 nextPosition = new Vector3((float)state.X, 0, (float)state.Z);
            previousPosition = hasPosition ? currentPosition : nextPosition;
            currentPosition = nextPosition;
            hasPosition = true;
            float fraction = Mathf.Clamp01((float)state.Health / state.MaximumHealth);
            healthFill.localScale = new Vector3(Mathf.Max(0.001f, fraction * 0.72f), 0.055f, 0.12f);
            healthFill.localPosition = new Vector3((fraction - 1) * 0.36f, 1.15f, 0);
            transform.localScale = industrialModel ? Vector3.one : state.Kind == EnemyKind.Heavy ? new Vector3(1.5f, 1.25f, 1.5f) :
                state.Kind == EnemyKind.Fast ? new Vector3(0.65f, 0.7f, 0.65f) : Vector3.one;
            ApplyFacing(state, tick, ticksPerSecond);
            if (bodyRenderer != null && !industrialModel)
            {
                if (bodyProperties == null) bodyProperties = new MaterialPropertyBlock();
                Color color = previousHealth > state.Health ? Color.white : state.Kind == EnemyKind.Fast ?
                    new Color(1, 0.8f, 0.15f) : state.Kind == EnemyKind.Heavy ? new Color(0.65f, 0.25f, 0.8f) : new Color(0.95f, 0.25f, 0.2f);
                bodyProperties.SetColor("_BaseColor", color); bodyRenderer.SetPropertyBlock(bodyProperties);
            }
            previousHealth = state.Health;
        }

        private void ApplyFacing(EnemyState state, long tick, int ticksPerSecond)
        {
            bool attacking = IsShowingAttack(state, tick, ticksPerSecond);
            if (strikeFlash != null) strikeFlash.SetActive(attacking);
            if (facingRoot == null) return;
            // 移动时朝向真实行进方向，出手时朝向模拟所知位置，不查询目标的新坐标。
            Vector3 direction = currentPosition - previousPosition;
            if (attacking || direction.sqrMagnitude < 0.000001f)
                direction = attacking ? new Vector3((float)state.LastAttackTargetX, 0, (float)state.LastAttackTargetZ) - currentPosition : state.TargetBuildingId > 0 ? new Vector3((float)state.TargetX, 0, (float)state.TargetZ) - currentPosition : Vector3.zero;
            if (direction.sqrMagnitude > 0.000001f)
                facingRoot.localRotation = Quaternion.LookRotation(direction.normalized) * facingRestRotation;
            facingRoot.localPosition = facingRestPosition + (attacking && !industrialModel ? direction.normalized * 0.16f : Vector3.zero);
        }

        internal static bool IsShowingAttack(EnemyState state, long tick, int ticksPerSecond)
        {
            // 攻击表现绑定本次真正击中的建筑，拆除障碍时不能误用最终目标。
            return state.CurrentAttackBuildingId > 0 && state.LastAttackTick >= 0 &&
                state.LastAttackTargetBuildingId == state.CurrentAttackBuildingId &&
                tick >= state.LastAttackTick && tick - state.LastAttackTick < Mathf.Max(1, Mathf.CeilToInt(ticksPerSecond * 0.22f));
        }

        /// <summary>仅平滑表现坐标，不回写敌人的权威位置或攻击距离。</summary>
        public void Interpolate(float progress) { transform.position = Vector3.Lerp(previousPosition, currentPosition, progress); mechanicalMotion?.ApplyPosition(); }
    }
}
