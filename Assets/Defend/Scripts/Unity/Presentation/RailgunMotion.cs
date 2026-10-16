using UnityEngine;

namespace Defend.Unity
{
    /// <summary>只读权威姿态及蓄力快照，机械表现使用会话显示时钟。</summary>
    public sealed class RailgunMotion : MonoBehaviour
    {
        [SerializeField] private Transform yaw, pitch, recoil;
        [SerializeField] private Transform[] chargeSegments;
        [SerializeField] private Quaternion restingYaw, restingPitch;
        [SerializeField] private Vector3 yawAxis, pitchAxis, recoilAxis, restingRecoil;
        private float dischargeAge = 10f;
        private float chargeProgress;
        public void Configure(Transform yawPivot, Transform pitchPivot, Transform recoilSlide, Transform[] segments)
        {
            yaw = yawPivot; pitch = pitchPivot; recoil = recoilSlide; chargeSegments = segments;
            restingYaw = yaw.localRotation; restingPitch = pitch.localRotation; restingRecoil = recoil.localPosition;
            yawAxis = yaw.parent.InverseTransformDirection(transform.up).normalized;
            pitchAxis = pitch.parent.InverseTransformDirection(transform.right).normalized;
            recoilAxis = recoil.parent.InverseTransformDirection(transform.forward).normalized;
            ResetPose();
        }
        public void SetAuthoritativeAim(float yawDegrees, float pitchDegrees)
        {
            // 模拟给出世界方向，建筑自身的四向旋转只在此换算一次。
            Vector3 direction = transform.InverseTransformDirection(Quaternion.Euler(0, yawDegrees, 0) * Vector3.forward);
            float localYaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            yaw.localRotation = Quaternion.AngleAxis(localYaw, yawAxis) * restingYaw;
            pitch.localRotation = Quaternion.AngleAxis(-pitchDegrees, pitchAxis) * restingPitch;
        }
        public void SetCharge(float progress) { chargeProgress = Mathf.Clamp01(progress); ApplyCharge(); }
        public void NotifyShot() { dischargeAge = 0; ApplyMechanicalPose(); }
        public void Advance(float seconds) { if (seconds > 0) dischargeAge += seconds; ApplyMechanicalPose(); }
        private void ApplyMechanicalPose()
        {
            float travel = dischargeAge < .067f ? Mathf.Lerp(.025f, .075f, dischargeAge / .067f) :
                dischargeAge < .7f ? Mathf.Lerp(.075f, 0, (dischargeAge - .067f) / .633f) : 0;
            recoil.localPosition = restingRecoil - recoilAxis * travel;
            ApplyCharge();
        }
        private void ApplyCharge()
        {
            if (chargeSegments == null) return;
            for (int i = 0; i < chargeSegments.Length; i++)
            {
                float amount = dischargeAge < .45f ? 1f - dischargeAge / .45f : Mathf.Clamp01(chargeProgress * 6f - i);
                chargeSegments[i].localScale = Vector3.one * Mathf.Max(.0001f, amount);
            }
        }
        public void ResetPose()
        {
            dischargeAge = 10; chargeProgress = 0;
            if (yaw == null || pitch == null || recoil == null) return;
            yaw.localRotation = restingYaw; pitch.localRotation = restingPitch; recoil.localPosition = restingRecoil; ApplyCharge();
        }
        private void OnDisable() => ResetPose();
    }
}
