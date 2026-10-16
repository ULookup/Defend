using System;
using System.Collections.Generic;
using UnityEngine;

namespace Defend.PhaseFiveEffects
{
    public enum EffectTimeDomain { Simulation, Feedback }

    public readonly struct EffectHandle
    {
        internal readonly PhaseFiveEffect Effect;
        internal readonly uint Generation;
        internal EffectHandle(PhaseFiveEffect effect) { Effect = effect; Generation = effect.Generation; }
        public bool IsValid => Effect != null && Effect.IsRunning && Effect.Generation == Generation;
        public float Age => IsValid ? Effect.Age : 0;
    }

    public sealed class EffectPool : MonoBehaviour
    {
        public EffectLibrary Library;
        public bool AutomaticAdvance = true;
        public bool Paused;
        public int ShortEffectLimit = 64, DebrisLimit = 128, EnergyPulseLimit = 128, StatusLimit = 32;
        public int PerPrefabLimit = 128;
        public int ActiveBudget(EffectBudgetGroup group) => budget[(int)group];
        public int Dropped { get; private set; }
        public int Duplicates { get; private set; }
        public int Reused { get; private set; }
        public int Created { get; private set; }
        public int PeakActive { get; private set; }
        public int PeakParticles { get; private set; }
        public int ActiveCount => active.Count;
        public int ParticleCount { get { int n = 0; foreach (var item in active) n += item.Effect.ParticleCount; return n; } }
        public readonly int[] PeakBudget = new int[4];
        readonly int[] budget = new int[4];
        sealed class Bucket { public PhaseFiveEffect Prefab; public readonly Stack<PhaseFiveEffect> Free = new Stack<PhaseFiveEffect>(); public int Created; }
        struct Active { public Bucket Bucket; public PhaseFiveEffect Effect; public int Group, Cost; public EffectTimeDomain TimeDomain; }
        readonly Dictionary<string, Bucket> buckets = new Dictionary<string, Bucket>(32, StringComparer.Ordinal);
        readonly List<Active> active = new List<Active>(256);
        readonly HashSet<(string, long)> sequences = new HashSet<(string, long)>();
        readonly Queue<(string, long)> sequenceOrder = new Queue<(string, long)>(512);
        bool initialized;
        public void ResetMetrics()
        {
            Dropped=Duplicates=Reused=PeakParticles=0; PeakActive=active.Count;
            for(int i=0;i<4;i++)PeakBudget[i]=budget[i];
        }

        public int BudgetUsed(EffectBudgetGroup group) => budget[(int)group];
        public int BudgetLimit(EffectBudgetGroup group)
        {
            switch (group) { case EffectBudgetGroup.Debris: return DebrisLimit; case EffectBudgetGroup.EnergyPulse: return EnergyPulseLimit; case EffectBudgetGroup.Status: return StatusLimit; default: return ShortEffectLimit; }
        }
        public void Initialize()
        {
            if (initialized) return;
            if (Library == null) throw new InvalidOperationException("Effect library is required.");
            foreach (var entry in Library.Entries)
                if (entry.Prefab != null && !string.IsNullOrEmpty(entry.Id)) buckets.Add(entry.Id, new Bucket { Prefab = entry.Prefab });
            initialized = true;
        }
        void Awake() { if (Library != null) Initialize(); }
        void Update() { if (AutomaticAdvance) Advance(Time.deltaTime); }

        public EffectHandle TryPlay(string id, EffectRequest request, float duration = 0, EffectTimeDomain timeDomain = EffectTimeDomain.Simulation)
        {
            Initialize();
            if (!buckets.TryGetValue(id, out var bucket) || !bucket.Prefab.CanPlay(request)) { Dropped++; return default; }
            var key = (id, request.EventSequence);
            if (request.EventSequence != 0 && sequences.Contains(key)) { Duplicates++; return default; }
            int group = (int)bucket.Prefab.BudgetGroup;
            int cost = Mathf.Max(1, bucket.Prefab.BudgetUnits);
            if (budget[group] + cost > BudgetLimit(bucket.Prefab.BudgetGroup) || (bucket.Free.Count == 0 && bucket.Created >= PerPrefabLimit)) { Dropped++; return default; }
            PhaseFiveEffect instance;
            if (bucket.Free.Count > 0) { instance = bucket.Free.Pop(); Reused++; }
            else { instance = Instantiate(bucket.Prefab, transform); bucket.Created++; Created++; }
            instance.gameObject.SetActive(true);
            // 运输时长由真实出发与到达逻辑步决定；归还后下次使用恢复 Prefab 时长。
            instance.Lifetime = duration > 0 ? duration : bucket.Prefab.Lifetime;
            instance.Begin(request);
            budget[group] += cost;
            PeakBudget[group] = Mathf.Max(PeakBudget[group], budget[group]);
            active.Add(new Active { Bucket = bucket, Effect = instance, Group = group, Cost = cost, TimeDomain = timeDomain });
            PeakActive = Mathf.Max(PeakActive, active.Count);
            PeakParticles = Mathf.Max(PeakParticles, ParticleCount);
            if (request.EventSequence != 0)
            {
                if (sequenceOrder.Count == 512) sequences.Remove(sequenceOrder.Dequeue());
                sequences.Add(key); sequenceOrder.Enqueue(key);
            }
            return new EffectHandle(instance);
        }
        public void Advance(float deltaTime) => AdvanceDomains(deltaTime, deltaTime);

        public void AdvanceDomains(float simulationSeconds, float feedbackSeconds)
        {
            if (Paused || (simulationSeconds <= 0 && feedbackSeconds <= 0)) return;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                if (active[i].Effect == null) { Release(i); continue; }
                float elapsed = active[i].TimeDomain == EffectTimeDomain.Feedback ? feedbackSeconds : simulationSeconds;
                if (elapsed > 0) active[i].Effect.Advance(elapsed);
                if (!active[i].Effect.IsRunning) Release(i);
            }
            PeakParticles = Mathf.Max(PeakParticles, ParticleCount);
        }
        /// <summary>补齐本帧较早出发的运输年龄，不推进其他对象的时间。</summary>
        public void AdvanceHandle(EffectHandle handle, float seconds)
        {
            if (!handle.IsValid || seconds <= 0f) return;
            for (int index = active.Count - 1; index >= 0; index--)
                if (active[index].Effect == handle.Effect)
                {
                    active[index].Effect.Advance(seconds);
                    if (!active[index].Effect.IsRunning) Release(index);
                    return;
                }
        }
        void Release(int index)
        {
            var item = active[index];
            budget[item.Group] -= item.Cost;
            if (item.Effect != null)
            {
                item.Effect.StopAndReset();
                item.Effect.gameObject.SetActive(false);
                item.Bucket.Free.Push(item.Effect);
            }
            else { item.Bucket.Created--; }
            int last = active.Count - 1; active[index] = active[last]; active.RemoveAt(last);
        }
        public bool Stop(EffectHandle handle)
        {
            if (!handle.IsValid) return false;
            for (int i = active.Count - 1; i >= 0; i--)
                if (active[i].Effect == handle.Effect) { Release(i); return true; }
            return false;
        }
        public void StopAll(bool clearSequenceHistory = true)
        {
            for (int i = active.Count - 1; i >= 0; i--) Release(i);
            if (clearSequenceHistory) { sequences.Clear(); sequenceOrder.Clear(); }
        }
        void OnDisable() { StopAll(); }
        void OnDestroy() { StopAll(); }
    }
}
