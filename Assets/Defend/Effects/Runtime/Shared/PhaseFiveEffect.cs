using System;
using UnityEngine;

namespace Defend.PhaseFiveEffects
{
    public enum EffectBudgetGroup { ShortEffect, Debris, EnergyPulse, Status }

    [Serializable]
    public struct EffectRequest
    {
        public Vector3 Origin, Destination;
        public Quaternion Rotation;
        public Transform Subject;
        public float Intensity;
        public int Seed;
        public bool ConfirmedTransfer;
        public bool AuthoritativeAim;
        public long EventSequence;
        public float Radius;
    }

    /// <summary>Presentation only. All time, including particles, comes from the owner pool.</summary>
    public abstract class PhaseFiveEffect : MonoBehaviour
    {
        public float Lifetime = .25f;
        public bool Looping;
        public EffectBudgetGroup BudgetGroup;
        public int BudgetUnits = 1;
        protected EffectRequest Context { get; private set; }
        public float Age { get; private set; }
        public bool IsRunning { get; private set; }
        public uint Generation { get; private set; }
        ParticleSystem[] particles;
        LineRenderer[] lines;
        bool initialized;

        public int ParticleCount
        {
            get { int count = 0; if (particles != null) foreach (var p in particles) if (p != null) count += p.particleCount; return count; }
        }

        void Initialize()
        {
            if (initialized) return;
            particles = GetComponentsInChildren<ParticleSystem>(true);
            lines = GetComponentsInChildren<LineRenderer>(true);
            initialized = true;
        }
        public virtual bool CanPlay(EffectRequest request) => true;

        public void Begin(EffectRequest request)
        {
            Initialize();
            StopAndReset();
            Context = request;
            Age = 0;
            Generation++;
            if (Generation == 0) Generation++;
            transform.SetPositionAndRotation(request.Origin,
                request.Rotation == default ? Quaternion.identity : request.Rotation);
            transform.localScale = Vector3.one;
            foreach (var p in particles)
            {
                p.useAutoRandomSeed = false;
                p.randomSeed = (uint)Mathf.Max(1, request.Seed);
            }
            IsRunning = true;
            foreach (var line in lines) line.enabled = true;
            OnBegin();
            OnAdvance(0, 0);
        }

        public void Advance(float deltaTime)
        {
            if (!IsRunning || deltaTime <= 0) return;
            float duration = Mathf.Max(.001f, Lifetime);
            float step = Looping ? deltaTime : Mathf.Min(deltaTime, Mathf.Max(0, duration - Age));
            Age += step;
            foreach (var p in particles) if (p != null) p.Simulate(step, false, false, false);
            OnAdvance(Looping ? (Age % duration) / duration : Mathf.Clamp01(Age / duration), step);
            if (!Looping && Age >= duration) StopAndReset();
        }

        public void StopAndReset()
        {
            Initialize();
            IsRunning = false;
            OnReset();
            foreach (var p in particles) if (p != null) p.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            foreach (var line in lines) if (line != null) line.enabled = false;
            Age = 0;
            Context = default;
        }
        protected virtual void OnBegin() { }
        protected virtual void OnAdvance(float normalizedTime, float deltaTime) { }
        protected virtual void OnReset() { }
        protected virtual void OnDisable() { if (initialized) StopAndReset(); }
    }
}
