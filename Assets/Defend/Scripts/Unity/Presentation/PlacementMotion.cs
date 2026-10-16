using UnityEngine;

namespace Defend.Unity
{
    /// <summary>只计算模型的显示姿态；落点和建造判定始终使用输入给出的网格坐标。</summary>
    public sealed class PlacementMotion
    {
        public const float MaximumOffset = .2f;
        public const float MaximumTilt = 7f;
        private Vector3 velocity, previousTarget;
        private Vector2 tilt, tiltVelocity, desiredTilt, returnVelocity;
        public Vector3 Position { get; private set; }
        public Quaternion Rotation => Quaternion.Euler(tilt.x, 0, tilt.y);

        public void Reset(Vector3 target)
        {
            Position = previousTarget = target;
            velocity = Vector3.zero;
            tilt = tiltVelocity = desiredTilt = returnVelocity = Vector2.zero;
        }

        public void Advance(Vector3 target, float seconds)
        {
            if (seconds <= 0) return;
            seconds = Mathf.Min(seconds, .05f);
            // 先限制平移距离再平滑，跨很多格时也不会积累过大的跟随速度。
            Vector3 start = target + Vector3.ClampMagnitude(Position - target, MaximumOffset);
            Vector3 next = Vector3.SmoothDamp(start, target, ref velocity, .1f, Mathf.Infinity, seconds);
            Position = target + Vector3.ClampMagnitude(next - target, MaximumOffset);
            Vector3 movement = target - previousTarget;
            previousTarget = target;
            if (movement.sqrMagnitude > .000001f)
            {
                desiredTilt = Vector2.ClampMagnitude(new Vector2(movement.z, -movement.x) / seconds, MaximumTilt);
                returnVelocity = Vector2.zero;
            }
            else desiredTilt = Vector2.SmoothDamp(desiredTilt, Vector2.zero, ref returnVelocity, .18f, Mathf.Infinity, seconds);
            tilt = Vector2.SmoothDamp(tilt, desiredTilt, ref tiltVelocity, .07f, Mathf.Infinity, seconds);
        }
    }
}
