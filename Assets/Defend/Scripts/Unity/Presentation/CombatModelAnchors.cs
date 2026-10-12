using Defend.PhaseFiveEffects;
using UnityEngine;

namespace Defend.Unity
{
    /// <summary>制作阶段绑定交付模型的枢轴，战斗时不扫描层级或修改共享材质。</summary>
    public sealed class CombatModelAnchors : MonoBehaviour
    {
        public Transform TurretYaw, Muzzle, AlternateMuzzle, EnergyPort, StatusAnchor;
        public RapidTurretMotion RapidMotion;
        private Quaternion restingYaw;
        private void Awake() { restingYaw = TurretYaw == null ? Quaternion.identity : TurretYaw.localRotation; }
        public void ResetPose()
        {
            if (RapidMotion != null) RapidMotion.ResetPose();
            else if (TurretYaw != null) TurretYaw.localRotation = restingYaw;
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
