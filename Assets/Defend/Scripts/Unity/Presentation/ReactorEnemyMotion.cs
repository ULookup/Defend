using UnityEngine;

namespace Defend.Unity
{
    /// <summary>按战斗显示时刻采样机械攻击；动画不会驱动伤害、位移或攻击频率。</summary>
    public sealed class ReactorEnemyMotion : MonoBehaviour
    {
        public const float AnticipationSeconds = .18f;
        public const float RecoverySeconds = .42f;
        [SerializeField] private GameObject animationRoot;
        [SerializeField] private AnimationClip attackClip;

        public void Configure(GameObject root, AnimationClip clip)
        { animationRoot = root; attackClip = clip; ResetPose(); }

        public void Sample(double displayTime, double lastAttackTime, double nextAttackTime,
            bool hasConfirmedAttack, bool preparingAttack)
        {
            if (animationRoot == null || attackClip == null) return;
            double age = displayTime - lastAttackTime;
            float sampleTime = 0;
            if (hasConfirmedAttack && age >= 0 && age < RecoverySeconds)
                sampleTime = AnticipationSeconds + (float)age;
            else if (preparingAttack)
            {
                double remaining = nextAttackTime - displayTime;
                if (remaining > 0 && remaining < AnticipationSeconds)
                    sampleTime = AnticipationSeconds - (float)remaining;
            }
            attackClip.SampleAnimation(animationRoot, sampleTime);
        }

        public void ResetPose()
        {
            if (animationRoot != null && attackClip != null) attackClip.SampleAnimation(animationRoot, 0);
        }
        private void OnEnable() { ResetPose(); }
        private void OnDisable() { ResetPose(); }
    }
}
