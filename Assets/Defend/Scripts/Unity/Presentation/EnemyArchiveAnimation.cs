using UnityEngine;

namespace Defend.Unity
{
    /// <summary>档案模型的独立待机预览；不读取战斗状态，也不产生战斗事件。</summary>
    public sealed class EnemyArchiveAnimation : MonoBehaviour
    {
        [SerializeField] private GameObject animationRoot;
        [SerializeField] private AnimationClip idleClip;
        private double previewTime;

        public void Configure(GameObject root, AnimationClip idle)
        { animationRoot = root; idleClip = idle; ResetPose(); }

        private void Update()
        {
            if (animationRoot == null || !animationRoot.activeInHierarchy || idleClip == null) return;
            previewTime += Time.unscaledDeltaTime;
            Sample(idleClip.length > 0 ? (float)(previewTime % idleClip.length) : 0);
        }

        private void Sample(float time)
        {
            if (animationRoot == null || idleClip == null) return;
            Transform root = animationRoot.transform;
            Vector3 position = root.localPosition, scale = root.localScale;
            Quaternion rotation = root.localRotation;
            idleClip.SampleAnimation(animationRoot, time);
            root.SetLocalPositionAndRotation(position, rotation); root.localScale = scale;
        }

        private void ResetPose() { previewTime = 0; Sample(0); }
        private void OnEnable() => ResetPose();
        private void OnDisable() => ResetPose();
    }
}
