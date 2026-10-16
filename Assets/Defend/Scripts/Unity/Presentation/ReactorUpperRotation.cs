using System;
using UnityEngine;

namespace Defend.Unity
{
    /// <summary>反应堆上部按共同显示时钟旋转，底座及模拟状态保持独立。</summary>
    [DisallowMultipleComponent]
    public sealed class ReactorUpperRotation : MonoBehaviour
    {
        [SerializeField] private Transform pivot;
        [SerializeField, Min(0f)] private float degreesPerSecond = 12f;
        [SerializeField, HideInInspector] private Quaternion authoredRotation = Quaternion.identity;
        private double angleDegrees;
        private bool alive = true;

        public Transform Pivot => pivot;
        public float AngleDegrees => (float)angleDegrees;
        public float DegreesPerSecond => degreesPerSecond;
        public bool IsConfigured => pivot != null && degreesPerSecond > 0f &&
            !float.IsNaN(degreesPerSecond) && !float.IsInfinity(degreesPerSecond);

        public void Configure(Transform upperPivot, float speed)
        {
            if (upperPivot == null) throw new ArgumentNullException(nameof(upperPivot));
            if (speed <= 0f || float.IsNaN(speed) || float.IsInfinity(speed))
                throw new ArgumentOutOfRangeException(nameof(speed));
            pivot = upperPivot;
            degreesPerSecond = speed;
            authoredRotation = pivot.localRotation;
            ResetPose();
        }

        /// <summary>重新部署或对象池复用时恢复初始角度。</summary>
        public void ResetPose()
        {
            angleDegrees = 0d;
            alive = true;
            if (pivot != null) pivot.localRotation = authoredRotation;
        }

        public void SetAlive(bool value)
        {
            if (alive == value) return;
            if (value) ResetPose();
            else alive = false;
        }

        /// <summary>使用模拟显示秒数：暂停不推进，倍速和单步沿用同一时间源。</summary>
        public void Advance(float seconds)
        {
            if (!alive || !IsConfigured || seconds <= 0f || float.IsNaN(seconds) || float.IsInfinity(seconds)) return;
            angleDegrees = (angleDegrees + (double)seconds * degreesPerSecond) % 360d;
            pivot.localRotation = authoredRotation * Quaternion.AngleAxis((float)angleDegrees, Vector3.up);
        }
    }
}
