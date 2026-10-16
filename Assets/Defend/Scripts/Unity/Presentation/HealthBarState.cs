using UnityEngine;

namespace Defend.Unity
{
    /// <summary>仅保存生命条的显示进度，连续伤害不会重置首次等待。</summary>
    public sealed class HealthBarState
    {
        public float Health { get; private set; }
        public float Trail { get; private set; }
        public float Opacity { get; private set; }
        public bool Visible => Opacity > 0;
        private bool initialized;
        private float waitRemaining, chaseSpeed;
        public void Reset() { initialized = false; Health = Trail = Opacity = waitRemaining = chaseSpeed = 0; }
        public void SetHealth(int health, int maximum, float delay)
        {
            float next = maximum <= 0 ? 0 : Mathf.Clamp01((float)health / maximum);
            if (!initialized) { initialized = true; Health = Trail = next; Opacity = next > 0 && next < 1 ? 1 : 0; return; }
            if (next <= 0) { Health = Trail = Opacity = waitRemaining = chaseSpeed = 0; return; }
            if (next > Health) { Trail = next; waitRemaining = chaseSpeed = 0; }
            else if (next < Health && Trail <= Health) waitRemaining = Mathf.Max(0, delay);
            Health = next;
            if (next < 1) Opacity = 1;
        }
        public void Advance(float seconds, float catchupSeconds, float fadeSeconds)
        {
            if (seconds <= 0) return;
            float remaining = seconds;
            if (waitRemaining > 0) { float waited = Mathf.Min(remaining, waitRemaining); waitRemaining -= waited; remaining -= waited; }
            if (remaining > 0 && Trail > Health)
            {
                chaseSpeed = Mathf.Max(chaseSpeed, (Trail - Health) / Mathf.Max(.001f, catchupSeconds));
                Trail = Mathf.MoveTowards(Trail, Health, remaining * chaseSpeed);
                if (Trail <= Health) chaseSpeed = 0;
            }
            if (Health >= 1) Opacity = Mathf.MoveTowards(Opacity, 0, seconds / Mathf.Max(.001f, fadeSeconds));
        }
    }
}

