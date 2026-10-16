using System.Collections.Generic;
using Defend.Simulation;
using UnityEngine;
using UnityEngine.Pool;

namespace Defend.Unity
{
    /// <summary>一次有效敌人受伤对应一个实例；池容量不限制同时显示的有效数字。</summary>
    public sealed class DamageNumberPresentation : MonoBehaviour
    {
        [SerializeField] private DamageNumberView numberPrefab;
        [SerializeField] private Camera worldCamera;
        [SerializeField] private Transform numberRoot;
        private ObjectPool<DamageNumberView> pool;
        private readonly List<DamageNumberView> active = new List<DamageNumberView>();
        private long sequence;
        public int ActiveCount => active.Count;
        public int DisplayedCount { get; private set; }
        public void Configure(DamageNumberView prefab, Camera camera, Transform root) { numberPrefab = prefab; worldCamera = camera; numberRoot = root; }
        public void Show(WorldEventSnapshot result)
        {
            if (result.Kind != WorldEventKind.EnemyHit || result.Amount <= 0) return;
            double x = result.Hit.SplashRadius > 0 ? result.Hit.EnemyX : result.X;
            double z = result.Hit.SplashRadius > 0 ? result.Hit.EnemyZ : result.Z;
            ShowDamage(result.Amount, new Vector3((float)x, Mathf.Max(.7f, (float)result.Hit.Y), (float)z));
        }
        public void ShowDamage(int amount, Vector3 position)
        {
            if (amount <= 0) return;
            if (pool == null) pool = new ObjectPool<DamageNumberView>(() => Instantiate(numberPrefab, numberRoot),
                item => item.gameObject.SetActive(true), item => { if (item != null) item.gameObject.SetActive(false); }, item => { if (item != null) { if (Application.isPlaying) Destroy(item.gameObject); else DestroyImmediate(item.gameObject); } }, false, 32, int.MaxValue);
            DamageNumberView view = pool.Get(); view.Begin(amount, position, ++sequence, worldCamera); active.Add(view); DisplayedCount++;
        }
        public void AdvanceFeedback(float seconds)
        {
            for (int index = active.Count - 1; index >= 0; index--)
            {
                var view = active[index]; if (view == null) { active.RemoveAt(index); continue; } view.AdvanceFeedback(seconds);
                if (!view.Finished) continue;
                active.RemoveAt(index); pool.Release(view);
            }
        }
        public void ResetSession() { foreach (var item in active) if (item != null) pool.Release(item); active.Clear(); sequence = 0; DisplayedCount = 0; }
        private void OnDestroy() { ResetSession(); pool?.Clear(); }
    }
}



