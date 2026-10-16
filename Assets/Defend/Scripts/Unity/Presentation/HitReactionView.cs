using System.Collections.Generic;
using UnityEngine;

namespace Defend.Unity
{
    /// <summary>每次受击独立恢复，再叠加限幅；连续命中不会把恢复时钟重置到峰值。</summary>
    public sealed class HitReactionView : MonoBehaviour
    {
        [SerializeField] private Transform reactionRoot;
        [SerializeField] private float displacement = .055f, compression = .04f, peakSeconds = .04f, recoverySeconds = .12f;
        private Vector3 restPosition, restScale;
        private bool captured;
        private readonly List<Impulse> impulses = new List<Impulse>();
        private struct Impulse { public Vector3 Direction; public float Age; }
        public void Configure(Transform root, float distance, float squash) { reactionRoot = root; displacement = distance; compression = squash; captured = false; Capture(); }
        private void Capture() { if (captured || reactionRoot == null) return; restPosition = reactionRoot.localPosition; restScale = reactionRoot.localScale; captured = true; }
        public void Play(Vector3 worldDirection)
        {
            Capture(); if (reactionRoot == null) return;
            worldDirection.y = 0;
            Vector3 localDirection = reactionRoot.parent == null ? worldDirection : reactionRoot.parent.InverseTransformDirection(worldDirection);
            impulses.Add(new Impulse { Direction = localDirection.normalized, Age = 0 });
        }
        public void AdvanceFeedback(float seconds)
        {
            Capture(); if (reactionRoot == null) return;
            Vector3 offset = Vector3.zero; float squash = 0;
            for (int index = impulses.Count - 1; index >= 0; index--)
            {
                Impulse impulse = impulses[index]; impulse.Age += Mathf.Max(0, seconds);
                float recovery = (impulse.Age - peakSeconds) / Mathf.Max(.001f, recoverySeconds);
                if (recovery >= 1) { impulses.RemoveAt(index); continue; }
                float envelope = impulse.Age < peakSeconds ? Mathf.Sin(impulse.Age / Mathf.Max(.001f, peakSeconds) * Mathf.PI * .5f) : Mathf.Cos(recovery * Mathf.PI * .5f) * (1 - recovery);
                offset += impulse.Direction * (displacement * envelope);
                float stretch = recovery > .6f ? .10f * Mathf.Sin((recovery - .6f) / .4f * Mathf.PI) : 0;
                squash += compression * (envelope - stretch);
                impulses[index] = impulse;
            }
            offset = Vector3.ClampMagnitude(offset, displacement * 1.5f); squash = Mathf.Clamp(squash, -compression * .15f, compression * 1.5f);
            reactionRoot.localPosition = restPosition + offset;
            reactionRoot.localScale = Vector3.Scale(restScale, new Vector3(1 + squash * .5f, 1 - squash, 1 + squash * .5f));
        }
        public void ResetPose() { impulses.Clear(); Capture(); if (reactionRoot != null) { reactionRoot.localPosition = restPosition; reactionRoot.localScale = restScale; } }
        private void OnDisable() { ResetPose(); }
    }
}

