using UnityEngine;

namespace Defend.PhaseFiveEffects
{
    /// <summary>速射炮的连续机械动作；时间只由战斗表现时钟传入，不参与射击结算。</summary>
    public sealed class RapidTurretMotion : MonoBehaviour
    {
        [SerializeField] private Transform turretYaw, barrelPitch, barrelRecoil, barrelSpin, muzzle;
        [SerializeField] private float yawSmoothSeconds = .12f, yawDegreesPerSecond = 300f;
        [SerializeField] private float pitchSmoothSeconds = .10f, pitchDegreesPerSecond = 90f;
        [SerializeField] private float minimumElevation = -6f, maximumElevation = 18f;
        [SerializeField] private float recoilDistance = .012f, recoilReturnSeconds = .10f;
        [SerializeField] private float firingSpinDegreesPerSecond = 1080f;
        [SerializeField] private float spinAcceleration = 3600f, spinDeceleration = 1800f;
        private Quaternion restingYaw, restingPitch, restingSpin;
        private Vector3 restingRecoil, aimTarget;
        private bool hasRestPose, hasAimTarget, authoritativeAim;
        private float yawAngle, elevation, yawVelocity, elevationVelocity;
        private float recoilAge, spinAngle, spinSpeed, firingSeconds;

        public bool IsConfigured => turretYaw != null && barrelPitch != null && barrelRecoil != null && barrelSpin != null && muzzle != null;
        public Transform MuzzleTransform => muzzle;
        public Vector3 MuzzlePosition => muzzle == null ? transform.position : muzzle.position;
        public Quaternion MuzzleRotation => muzzle == null ? transform.rotation : muzzle.rotation;

        public void Configure(Transform yaw, Transform pitch, Transform recoil, Transform spin, Transform muzzleAnchor)
        {
            ResetPose();
            turretYaw = yaw; barrelPitch = pitch; barrelRecoil = recoil; barrelSpin = spin; muzzle = muzzleAnchor;
            hasRestPose = false;
            ResetPose();
        }

        private bool CaptureRestPose()
        {
            if (hasRestPose) return true;
            if (!IsConfigured) return false;
            restingYaw = turretYaw.localRotation;
            restingPitch = barrelPitch.localRotation;
            restingRecoil = barrelRecoil.localPosition;
            restingSpin = barrelSpin.localRotation;
            hasRestPose = true;
            return true;
        }

        public void SetAuthoritativeAim(float yawDegrees, float pitchDegrees)
        {
            if (!CaptureRestPose()) return;
            authoritativeAim = true; hasAimTarget = false;
            yawAngle = yawDegrees; elevation = pitchDegrees;
            turretYaw.rotation = Quaternion.AngleAxis(yawDegrees, Vector3.up);
            barrelPitch.localRotation = restingPitch * Quaternion.AngleAxis(-pitchDegrees, Vector3.right);
        }

        public void SetAimTarget(Vector3 destination)
        {
            aimTarget = destination;
            hasAimTarget = true;
        }

        public bool IsHorizontallyAimed(Vector3 destination, float toleranceDegrees = 6f)
        {
            if (!IsConfigured) return false;
            Vector3 direction = destination - muzzle.position;
            Vector3 forward = muzzle.forward;
            direction.y = forward.y = 0f;
            return direction.sqrMagnitude > .000001f && forward.sqrMagnitude > .000001f &&
                Vector3.Angle(forward, direction) <= toleranceDegrees;
        }

        public void NotifyShot(float shotIntervalSeconds)
        {
            if (!CaptureRestPose()) return;
            firingSeconds = Mathf.Max(firingSeconds, Mathf.Max(.1f, shotIntervalSeconds * 1.15f));
            recoilAge = 0f;
            barrelRecoil.localPosition = restingRecoil + Vector3.back * recoilDistance;
        }

        public void StopFiring() { firingSeconds = 0f; }

        public void Advance(float seconds)
        {
            if (seconds <= 0f || !isActiveAndEnabled || !CaptureRestPose()) return;
            // 小步积分让逻辑追赶和倍速下的回位、自转仍与普通帧推进一致。
            while (seconds > 0f)
            {
                float step = Mathf.Min(seconds, 1f / 120f);
                AdvancePose(step);
                seconds -= step;
            }
        }

        private void AdvancePose(float seconds)
        {
            if (hasAimTarget && !authoritativeAim)
            {
                Vector3 direction = aimTarget - turretYaw.position;
                if (turretYaw.parent != null) direction = turretYaw.parent.InverseTransformDirection(direction);
                direction = Quaternion.Inverse(restingYaw) * direction;
                if (direction.x * direction.x + direction.z * direction.z > .000001f)
                {
                    float desiredYaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
                    yawAngle = Mathf.SmoothDampAngle(yawAngle, desiredYaw, ref yawVelocity,
                        yawSmoothSeconds, yawDegreesPerSecond, seconds);
                    turretYaw.localRotation = restingYaw * Quaternion.AngleAxis(yawAngle, Vector3.up);
                }
                direction = aimTarget - barrelPitch.position;
                if (barrelPitch.parent != null) direction = barrelPitch.parent.InverseTransformDirection(direction);
                direction = Quaternion.Inverse(restingPitch) * direction;
                float horizontalDistance = Mathf.Sqrt(direction.x * direction.x + direction.z * direction.z);
                float desiredElevation = Mathf.Clamp(Mathf.Atan2(direction.y, horizontalDistance) * Mathf.Rad2Deg,
                    minimumElevation, maximumElevation);
                elevation = Mathf.Clamp(Mathf.SmoothDamp(elevation, desiredElevation, ref elevationVelocity,
                    pitchSmoothSeconds, pitchDegreesPerSecond, seconds), minimumElevation, maximumElevation);
                barrelPitch.localRotation = restingPitch * Quaternion.AngleAxis(-elevation, Vector3.right);
            }

            recoilAge += seconds;
            float returnProgress = Mathf.Clamp01(recoilAge / Mathf.Max(.001f, recoilReturnSeconds));
            barrelRecoil.localPosition = restingRecoil + Vector3.back * (recoilDistance * (1f - Mathf.SmoothStep(0f, 1f, returnProgress)));
            bool firing = firingSeconds > 0f;
            firingSeconds = Mathf.Max(0f, firingSeconds - seconds);
            float previousSpeed = spinSpeed;
            spinSpeed = Mathf.MoveTowards(spinSpeed, firing ? firingSpinDegreesPerSecond : 0f,
                (firing ? spinAcceleration : spinDeceleration) * seconds);
            spinAngle = Mathf.Repeat(spinAngle + (previousSpeed + spinSpeed) * .5f * seconds, 360f);
            barrelSpin.localRotation = restingSpin * Quaternion.AngleAxis(spinAngle, Vector3.forward);
        }

        public void ResetPose()
        {
            hasAimTarget = authoritativeAim = false;
            aimTarget = Vector3.zero;
            yawAngle = elevation = yawVelocity = elevationVelocity = spinAngle = spinSpeed = firingSeconds = 0f;
            recoilAge = recoilReturnSeconds;
            if (!CaptureRestPose()) return;
            turretYaw.localRotation = restingYaw;
            barrelPitch.localRotation = restingPitch;
            barrelRecoil.localPosition = restingRecoil;
            barrelSpin.localRotation = restingSpin;
        }

        private void Awake() { ResetPose(); }
        private void OnDisable() { ResetPose(); }
    }
}
