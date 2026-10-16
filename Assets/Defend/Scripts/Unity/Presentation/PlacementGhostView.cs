using Defend.Simulation;
using UnityEngine;

namespace Defend.Unity
{
    /// <summary>编辑期提取的纯模型预览，没有碰撞、战斗脚本或真实建筑状态。</summary>
    public sealed class PlacementGhostView : MonoBehaviour
    {
        [SerializeField] private BuildingKind kind;
        [SerializeField] private Renderer[] surfaces;
        [SerializeField, Range(.2f, .7f)] private float hoverHeight = .4f;
        [SerializeField] private Transform facingPivot;
        [SerializeField] private Quaternion restingFacingRotation = Quaternion.identity;
        private readonly PlacementMotion motion = new PlacementMotion();
        private MaterialPropertyBlock properties;
        private float age;
        private bool? lastValid;
        public BuildingKind Kind => kind;
        public void Configure(BuildingKind value, Renderer[] renderers, Transform pivot = null)
        {
            kind = value; surfaces = renderers; facingPivot = pivot;
            restingFacingRotation = pivot == null ? Quaternion.identity : pivot.localRotation;
        }
        public void ResetPose(Vector3 target) { motion.Reset(target); age = 0; lastValid = null; }
        public void Refresh(Vector3 target, bool valid, float seconds, int orientationQuarterTurns = 0, int facingDegrees = 0)
        {
            motion.Advance(target, seconds);
            age += Mathf.Min(seconds, .05f);
            float orientation = orientationQuarterTurns * 90f;
            transform.SetPositionAndRotation(motion.Position + Vector3.up * (hoverHeight + Mathf.Sin(age * 2.5f) * .025f),
                motion.Rotation * Quaternion.Euler(0, orientation, 0));
            // 整体朝向旋转底座；轨道炮枢轴只补偿保留的主方向微调，避免重复叠加 90°。
            if (facingPivot != null)
                facingPivot.localRotation = restingFacingRotation * Quaternion.Euler(0, facingDegrees - orientation, 0);
            if (lastValid == valid) return;
            if (properties == null) properties = new MaterialPropertyBlock();
            properties.SetColor("_PlacementTint", valid ? new Color(.25f, .95f, 1) : new Color(1, .22f, .15f));
            foreach (Renderer surface in surfaces) surface.SetPropertyBlock(properties);
            lastValid = valid;
        }
    }
}
