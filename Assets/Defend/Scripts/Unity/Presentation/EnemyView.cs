using Defend.Simulation;
using UnityEngine;

namespace Defend.Unity
{
    /// <summary>预制体保存生命条和受击节点；运行时只读取快照并推进显示。</summary>
    public sealed class EnemyView : MonoBehaviour
    {
        [SerializeField] private Transform healthFill;
        [SerializeField] private HealthBarView healthBar;
        [SerializeField] private HitReactionView hitReaction;
        [SerializeField] private Renderer bodyRenderer;
        [SerializeField] private Transform facingRoot;
        [SerializeField] private GameObject strikeFlash;
        [SerializeField] private bool industrialModel;
        [SerializeField] private MechanicalEnemyMotion mechanicalMotion;
        [SerializeField] private ReactorEnemyMotion reactorMotion;
        [SerializeField] private EnemyCombatAnimation combatAnimation;
        [SerializeField] private GameObject selectionMarker;
        public long EnemyId { get; private set; }
        public bool IsDying { get; private set; }
        public void ShowSelection(bool selected)
        {
            selected &= !IsDying;
            if (selectionMarker != null) selectionMarker.SetActive(selected);
            healthBar?.SetSelected(selected);
        }
        public EnemyKind Kind { get; private set; }
        public Transform ModelRoot => facingRoot;
        private MaterialPropertyBlock bodyProperties;
        private Vector3 facingRestPosition;
        private Quaternion facingRestRotation;
        private bool hasRestPose;
        private double travelledDistance;
        private bool moving;
        private Collider[] selectionColliders;
        private bool[] colliderEnabledStates;
        
        private HitFlashEffect hitFlashEffect;
        private void Awake()
        {
            hitFlashEffect = GetComponent<HitFlashEffect>();
            if (hitFlashEffect == null) hitFlashEffect = GetComponentInChildren<HitFlashEffect>(true);
            CacheAnimation();
        }
        private void CacheAnimation()
        { if (combatAnimation == null) combatAnimation = GetComponentInChildren<EnemyCombatAnimation>(true); }
        public void ConfigureCombatAnimation(EnemyCombatAnimation animation) { combatAnimation = animation; }
        public void SetPaused(bool paused) { hitFlashEffect?.SetPaused(paused); }
        private Vector3 previousPosition;
        private Vector3 currentPosition;
        private bool hasPosition;
        public void Configure(Transform fill) { healthFill = fill; }
        public void ConfigureBehaviorVisuals(Transform facing, GameObject strike)
        { facingRoot = facing; strikeFlash = strike; }
        public void ResetForReuse()
        {
            CacheAnimation();
            IsDying = false; travelledDistance = 0; moving = false;
            if (selectionColliders != null)
                for (int index = 0; index < selectionColliders.Length; index++)
                    if (selectionColliders[index] != null) selectionColliders[index].enabled = colliderEnabledStates[index];
            combatAnimation?.ResetPose();
            hasPosition = false;
            EnemyId = 0;
            ShowSelection(false);
            healthBar?.ResetState(); hitReaction?.ResetPose();
            hitFlashEffect?.ResetFlash();
            if (combatAnimation == null || !combatAnimation.IsConfigured) reactorMotion?.ResetPose();
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
        public void Apply(EnemyState state, long tick = 0, int ticksPerSecond = 20, bool updateHealth = true, bool updateFacing = true)
        {

            Kind = state.Kind;
            EnemyId = state.Id;
            Vector3 nextPosition = new Vector3((float)state.X, 0, (float)state.Z);
            previousPosition = hasPosition ? currentPosition : nextPosition;
            currentPosition = nextPosition;
            hasPosition = true;
            if (updateHealth) ApplyDisplayHealth(state.Health, state.MaximumHealth);
            transform.localScale = industrialModel ? Vector3.one : state.Kind == EnemyKind.Heavy ? new Vector3(1.5f, 1.25f, 1.5f) :
                state.Kind == EnemyKind.Fast ? new Vector3(0.65f, 0.7f, 0.65f) : Vector3.one;
            if (updateFacing) ApplyFacing(state, tick, ticksPerSecond);
            if (bodyRenderer != null && !industrialModel)
            {
                if (bodyProperties == null) bodyProperties = new MaterialPropertyBlock();
                Color color = state.Kind == EnemyKind.Fast ?
                    new Color(1, 0.8f, 0.15f) : state.Kind == EnemyKind.Heavy ? new Color(0.65f, 0.25f, 0.8f) : new Color(0.95f, 0.25f, 0.2f);
                bodyProperties.SetColor("_BaseColor", color); bodyRenderer.SetPropertyBlock(bodyProperties);
            }

        }

        public void ConfigureImpact(HealthBarView health, HitReactionView reaction) { healthBar = health; hitReaction = reaction; }
        public void ApplyDisplayHealth(int health, int maximumHealth)
        {
            if (healthBar != null) { healthBar.SetHealth(health, maximumHealth); return; }
            if (healthFill == null) return;
            float fraction = maximumHealth <= 0 ? 0 : Mathf.Clamp01((float)health / maximumHealth);
            healthFill.gameObject.SetActive(fraction > 0 && fraction < 1);
            healthFill.localScale = new Vector3(Mathf.Max(.001f, fraction * .72f), .055f, .12f);
            healthFill.localPosition = new Vector3((fraction - 1) * .36f, 1.15f, 0);
        }
        public void SetDisplayPosition(Vector3 previous, Vector3 current)
        { previousPosition = previous; currentPosition = current; hasPosition = true; }
        public void ApplyHit(WorldEventSnapshot result)
        {
            if (IsDying || result.Kind != WorldEventKind.EnemyHit || result.Amount <= 0) return;
            if (combatAnimation != null && combatAnimation.IsConfigured) combatAnimation.Hit(result.Time);
            Vector3 direction = new Vector3((float)result.Hit.DirectionX, 0, (float)result.Hit.DirectionZ);
            if (result.Hit.SplashRadius > 0)
            {
                Vector3 radial = new Vector3((float)(result.Hit.EnemyX - result.Hit.ExplosionX), 0, (float)(result.Hit.EnemyZ - result.Hit.ExplosionZ));
                if (radial.sqrMagnitude > .000001f) direction = radial;
            }
            if (combatAnimation == null || !combatAnimation.IsConfigured) hitReaction?.Play(direction);
            hitFlashEffect?.Flash();
            ApplyDisplayHealth(result.HealthAfter, result.MaximumHealth);
        }
        public void AdvanceFeedback(float seconds)
        {
            healthBar?.AdvanceFeedback(seconds);
            if (combatAnimation == null || !combatAnimation.IsConfigured) hitReaction?.AdvanceFeedback(seconds);
            hitFlashEffect?.AdvanceFeedback(seconds);
        }
        private void ApplyFacing(EnemyState state, long tick, int ticksPerSecond)
        {
            bool attacking = IsShowingAttack(state, tick, ticksPerSecond);
            // 移动时朝向真实行进方向，出手时朝向模拟所知位置，不查询目标的新坐标。
            Vector3 direction = currentPosition - previousPosition;
            if (attacking || direction.sqrMagnitude < 0.000001f)
                direction = attacking ? new Vector3((float)state.LastAttackTargetX, 0, (float)state.LastAttackTargetZ) - currentPosition : state.TargetBuildingId > 0 ? new Vector3((float)state.TargetX, 0, (float)state.TargetZ) - currentPosition : Vector3.zero;
            ApplyDisplayFacing(direction, attacking);
        }
        public void ApplyDisplayFacing(Vector3 direction, bool attacking)
        {
            if (strikeFlash != null) strikeFlash.SetActive(attacking);
            if (facingRoot == null) return;
            direction.y = 0;
            if (direction.sqrMagnitude > 0.000001f)
                facingRoot.localRotation = Quaternion.LookRotation(direction.normalized) * facingRestRotation;
            facingRoot.localPosition = facingRestPosition + (attacking && !industrialModel ? direction.normalized * 0.16f : Vector3.zero);
        }

        public float AttackRecoverySeconds => combatAnimation != null && combatAnimation.IsConfigured ?
            combatAnimation.AttackRecoverySeconds : reactorMotion == null ? .22f : ReactorEnemyMotion.RecoverySeconds;
        public void SetAnimationMovement(double distance, bool isMoving)
        { travelledDistance = distance; moving = isMoving; }
        public void SampleAttackMotion(double displayTime, double lastAttackTime, double nextAttackTime,
            bool hasConfirmedAttack, bool preparingAttack)
        {
            if (combatAnimation != null && combatAnimation.IsConfigured)
                combatAnimation.Sample(displayTime, travelledDistance, moving, lastAttackTime, nextAttackTime, hasConfirmedAttack, preparingAttack);
            else reactorMotion?.Sample(displayTime, lastAttackTime, nextAttackTime, hasConfirmedAttack, preparingAttack);
        }

        public float BeginDeath(double displayTime)
        {
            IsDying = true; ShowSelection(false);
            healthBar?.ResetState();
            if (healthFill != null) healthFill.gameObject.SetActive(false);
            hitReaction?.ResetPose(); hitFlashEffect?.ResetFlash();
            if (strikeFlash != null) strikeFlash.SetActive(false);
            if (selectionColliders == null)
            {
                selectionColliders = GetComponentsInChildren<Collider>(true);
                colliderEnabledStates = new bool[selectionColliders.Length];
                for (int index = 0; index < selectionColliders.Length; index++)
                    colliderEnabledStates[index] = selectionColliders[index].enabled;
            }
            foreach (var collider in selectionColliders) if (collider != null) collider.enabled = false;
            if (combatAnimation == null || !combatAnimation.IsConfigured) return 0;
            combatAnimation.BeginDeath(displayTime);
            return combatAnimation.DeathDuration;
        }

        public void SampleDeath(double displayTime) { combatAnimation?.SampleDeath(displayTime); }

        internal static bool IsShowingAttack(EnemyState state, long tick, int ticksPerSecond)
        {
            // 攻击表现绑定本次真正击中的建筑，拆除障碍时不能误用最终目标。
            return state.CurrentAttackBuildingId > 0 && state.LastAttackTick >= 0 &&
                state.LastAttackTargetBuildingId == state.CurrentAttackBuildingId &&
                tick >= state.LastAttackTick && tick - state.LastAttackTick < Mathf.Max(1, Mathf.CeilToInt(ticksPerSecond * 0.22f));
        }

        /// <summary>仅平滑表现坐标，不回写敌人的权威位置或攻击距离。</summary>
        public void Interpolate(float progress)
        {
            transform.position = Vector3.Lerp(previousPosition, currentPosition, progress);
            if (combatAnimation == null || !combatAnimation.IsConfigured) mechanicalMotion?.ApplyPosition();
        }
    }
}



