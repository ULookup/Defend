using System;
using UnityEngine;

namespace Defend.Unity
{
    /// <summary>只按战斗显示时钟和真实路径长度采样美术片段；不产生伤害、位移或攻击事件。</summary>
    public sealed class EnemyCombatAnimation : MonoBehaviour
    {
        public enum MotionState { Idle, Move, Attack, Hit, Death }

        [SerializeField] private GameObject animationRoot;
        [SerializeField] private AnimationClip idleClip, moveClip, attackClip, hitClip, deathClip;
        [SerializeField, Min(0)] private float attackContactSeconds = .2f;
        [SerializeField, Min(.01f)] private float moveCycleDistance = 1f;
        private double lastHitTime = double.NegativeInfinity;
        private double deathTime = double.PositiveInfinity;
        private double startTime;
        private bool started;
        public MotionState CurrentState { get; private set; }
        public float SampleTime { get; private set; }
        public bool IsConfigured => animationRoot != null && idleClip != null && moveClip != null &&
            attackClip != null && hitClip != null && deathClip != null;
        public float AttackRecoverySeconds => attackClip == null ? 0 : Mathf.Max(0, attackClip.length - ContactTime);
        public float DeathDuration => deathClip == null ? 0 : deathClip.length;
        private float ContactTime => attackClip == null ? 0 : Mathf.Clamp(attackContactSeconds, 0, attackClip.length);

        public void Configure(GameObject root, AnimationClip idle, AnimationClip move, AnimationClip attack,
            AnimationClip hit, AnimationClip death, float contactSeconds, float cycleDistance)
        {
            animationRoot = root; idleClip = idle; moveClip = move; attackClip = attack;
            hitClip = hit; deathClip = death;
            attackContactSeconds = Mathf.Max(0, contactSeconds);
            moveCycleDistance = Mathf.Max(.01f, cycleDistance);
            ResetPose();
        }

        public void Sample(double displayTime, double travelledDistance, bool moving, double lastAttackTime,
            double nextAttackTime, bool hasConfirmedAttack, bool preparingAttack)
        {
            if (!IsConfigured) return;
            if (!started) { startTime = displayTime; started = true; }
            if (!double.IsPositiveInfinity(deathTime)) { SampleDeath(displayTime); return; }

            // 首击已由模拟结算，直接从接触帧开始；后续前摇仅根据同一目标的已确认攻击周期。
            double attackAge = displayTime - lastAttackTime;
            if (hasConfirmedAttack && attackAge >= 0 && attackAge < AttackRecoverySeconds)
            { SampleClip(attackClip, ContactTime + (float)attackAge, MotionState.Attack); return; }
            double remaining = nextAttackTime - displayTime;
            if (hasConfirmedAttack && preparingAttack && remaining > 0 && remaining <= ContactTime)
            { SampleClip(attackClip, ContactTime - (float)remaining, MotionState.Attack); return; }
            double hitAge = displayTime - lastHitTime;
            if (hitAge >= 0 && hitAge < hitClip.length)
            { SampleClip(hitClip, (float)hitAge, MotionState.Hit); return; }

            if (moving)
                SampleClip(moveClip, Loop(travelledDistance / moveCycleDistance * moveClip.length, moveClip.length), MotionState.Move);
            else
                SampleClip(idleClip, Loop(displayTime - startTime, idleClip.length), MotionState.Idle);
        }

        public void Hit(double time)
        {
            if (!double.IsPositiveInfinity(deathTime)) return;
            // 连射期间让一次短受击完整恢复，避免每次命中都重置在同一姿态。
            if (hitClip == null || time - lastHitTime >= hitClip.length) lastHitTime = time;
        }

        public void BeginDeath(double time)
        {
            if (!double.IsPositiveInfinity(deathTime)) return;
            deathTime = time; lastHitTime = double.NegativeInfinity;
            SampleDeath(time);
        }

        public void SampleDeath(double displayTime)
        {
            if (double.IsPositiveInfinity(deathTime)) return;
            SampleClip(deathClip, (float)Math.Max(0, displayTime - deathTime), MotionState.Death);
        }

        public void ResetPose()
        {
            started = false; startTime = 0;
            lastHitTime = double.NegativeInfinity; deathTime = double.PositiveInfinity;
            SampleClip(idleClip, 0, MotionState.Idle);
        }

        private static float Loop(double time, float duration)
            => duration <= 0 ? 0 : (float)(Math.Max(0, time) % duration);

        private void SampleClip(AnimationClip clip, float time, MotionState state)
        {
            CurrentState = state; SampleTime = clip == null ? 0 : Mathf.Clamp(time, 0, clip.length);
            if (animationRoot == null || clip == null) return;
            // 防御性保留实例根变换；片段只允许内部骨骼/零件运动，不能带动权威位置或朝向。
            Transform root = animationRoot.transform;
            Vector3 position = root.localPosition, scale = root.localScale;
            Quaternion rotation = root.localRotation;
            clip.SampleAnimation(animationRoot, SampleTime);
            root.SetLocalPositionAndRotation(position, rotation); root.localScale = scale;
        }

        private void OnDisable() => ResetPose();
    }
}
