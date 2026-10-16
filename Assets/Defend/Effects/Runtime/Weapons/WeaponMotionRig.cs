using UnityEngine;

namespace Defend.PhaseFiveEffects
{
    /// <summary>Temporary ownership of the delivered model's two mechanical pivots.</summary>
    public sealed class WeaponMotionRig : MonoBehaviour
    {
        private Transform turretYaw;
        private Transform barrelRecoil;
        private Quaternion restingYaw;
        private Vector3 restingRecoil;
        private uint generation;
        private bool ownsPose, ownsYaw;

        public uint Acquire(Vector3 destination, bool rotateAim = true)
        {
            Restore();
            if (turretYaw == null) turretYaw = FindPivot(transform, "TurretYaw");
            if (barrelRecoil == null) barrelRecoil = FindPivot(transform, "BarrelRecoil");
            generation++;
            ownsPose = true;
            ownsYaw = rotateAim;
            if (turretYaw != null && ownsYaw)
            {
                restingYaw = turretYaw.localRotation;
                Vector3 direction = destination - turretYaw.position;
                if (turretYaw.parent != null)
                    direction = turretYaw.parent.InverseTransformDirection(direction);
                direction.y = 0f;
                if (direction.sqrMagnitude > 0.000001f)
                    turretYaw.localRotation = Quaternion.AngleAxis(
                        Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg, Vector3.up);
            }
            if (barrelRecoil != null) restingRecoil = barrelRecoil.localPosition;
            return generation;
        }

        public void SetRecoil(uint token, float distance)
        {
            if (ownsPose && token == generation && barrelRecoil != null)
                barrelRecoil.localPosition = restingRecoil + barrelRecoil.localRotation * (Vector3.back * distance);
        }

        public void Release(uint token)
        {
            if (token == generation) Restore();
        }

        private void Restore()
        {
            if (!ownsPose) return;
            if (turretYaw != null && ownsYaw) turretYaw.localRotation = restingYaw;
            if (barrelRecoil != null) barrelRecoil.localPosition = restingRecoil;
            ownsPose = false;
        }

        private void OnDisable()
        {
            Restore();
            generation++;
        }

        private void OnDestroy() { Restore(); }

        private static Transform FindPivot(Transform parent, string pivotName)
        {
            if (parent.name == pivotName) return parent;
            for (int index = 0; index < parent.childCount; index++)
            {
                Transform match = FindPivot(parent.GetChild(index), pivotName);
                if (match != null) return match;
            }
            return null;
        }
    }
}
