using Defend.PhaseFiveEffects;
using UnityEngine;

namespace Defend.Unity
{
    /// <summary>制作阶段绑定交付模型的枢轴，战斗时不扫描层级或修改共享材质。</summary>
    public sealed class CombatModelAnchors : MonoBehaviour
    {
        public Transform TurretYaw, TurretPitch, Muzzle, AlternateMuzzle, EnergyPort, StatusAnchor;
        public RapidTurretMotion RapidMotion;
        public RailgunMotion RailgunMotion;
        private Quaternion restingYaw, restingPitch;
        private bool hasRestPose;
        private Transform capturedYaw, capturedPitch;
        private void Awake() => CaptureRestPose();
        private void CaptureRestPose()
        {
            if (hasRestPose && capturedYaw == TurretYaw && capturedPitch == TurretPitch) return;
            capturedYaw = TurretYaw; capturedPitch = TurretPitch;
            restingYaw = TurretYaw == null ? Quaternion.identity : TurretYaw.localRotation;
            restingPitch = TurretPitch == null ? Quaternion.identity : TurretPitch.localRotation;
            hasRestPose = true;
        }
        public void ResetPose()
        {
            CaptureRestPose();
            if (RailgunMotion != null) RailgunMotion.ResetPose();
            else if (RapidMotion != null) RapidMotion.ResetPose();
            else { if (TurretYaw != null) TurretYaw.localRotation = restingYaw; if (TurretPitch != null) TurretPitch.localRotation = restingPitch; }
        }
        public void ApplyAuthoritativeAim(float yawDegrees, float pitchDegrees)
        {
            CaptureRestPose();
            if (RailgunMotion != null) { RailgunMotion.SetAuthoritativeAim(yawDegrees, pitchDegrees); return; }
            if (RapidMotion != null) { RapidMotion.SetAuthoritativeAim(yawDegrees, pitchDegrees); return; }
            // 输入始终是世界瞄准方向，局部枢轴不再叠加建筑根节点的朝向。
            Vector3 direction = transform.InverseTransformDirection(Quaternion.Euler(0, yawDegrees, 0) * Vector3.forward);
            float localYaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            if (TurretYaw != null) TurretYaw.localRotation = restingYaw * Quaternion.Euler(0, localYaw, 0);
            if (TurretPitch != null) TurretPitch.localRotation = restingPitch * Quaternion.Euler(-pitchDegrees, 0, 0);
        }
        public Vector3 PortPosition => EnergyPort == null ? transform.position + Vector3.up * .25f : EnergyPort.position;
        public Vector3 StatusPosition => StatusAnchor == null ? transform.position + Vector3.up * 1.1f : StatusAnchor.position;
        public Vector3 Aim(Vector3 destination, long shotId)
        {
            if (RapidMotion != null && RapidMotion.IsConfigured)
            {
                RapidMotion.SetAimTarget(destination);
                return RapidMotion.MuzzlePosition;
            }
            if (TurretYaw != null)
            {
                Vector3 direction = TurretYaw.parent.InverseTransformDirection(destination - TurretYaw.position);
                direction.y = 0;
                if (direction.sqrMagnitude > .000001f) TurretYaw.localRotation = Quaternion.LookRotation(direction);
            }
            Transform muzzle = AlternateMuzzle != null && shotId % 2 == 0 ? AlternateMuzzle : Muzzle;
            return muzzle != null ? muzzle.position : transform.position + Vector3.up * .7f;
        }
    }
}

