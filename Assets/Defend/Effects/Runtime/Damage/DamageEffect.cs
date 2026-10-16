using UnityEngine;

namespace Defend.PhaseFiveEffects
{
    public enum DamageEffectKind
    {
        EnemyHit, EnemyDeath, HeavyContact, BuildingHit, BuildingDestruction, RepairComplete
    }

    /// <summary>Snapshot flashes, bounded debris and reversible mechanical demonstrations.</summary>
    public sealed class DamageEffect : PhaseFiveEffect
    {
        public const int DebrisPieceCount = 8;

        [SerializeField] private DamageEffectKind kind;
        [SerializeField] private Transform flashRoot;
        [SerializeField] private Transform[] debrisPieces = new Transform[0];
        [SerializeField] private ParticleSystem sparks;
        [SerializeField] private ParticleSystem dust;
        [SerializeField] private int sparkCount;
        [SerializeField] private int dustCount;

        private readonly Vector3[] debrisVelocity = new Vector3[DebrisPieceCount];
        private readonly float[] debrisSize = new float[DebrisPieceCount];
        private DamageSubjectMotion subjectMotion;
        private uint motionOwner;
        private float intensity;

        public DamageEffectKind Kind => kind;
        public int ConfiguredDebrisCount => debrisPieces.Length;

        public void Configure(DamageEffectKind effectKind, Transform flash, Transform[] debris,
            ParticleSystem sparkSystem, int emittedSparks, ParticleSystem dustSystem, int emittedDust)
        {
            kind = effectKind;
            flashRoot = flash;
            debrisPieces = debris ?? new Transform[0];
            sparks = sparkSystem;
            dust = dustSystem;
            sparkCount = emittedSparks;
            dustCount = emittedDust;
        }

        protected override void OnBegin()
        {
            intensity = Mathf.Clamp(Context.Intensity <= 0 ? 1 : Context.Intensity, 0.5f, 1.5f);
            EmitSpread(sparks, sparkCount, false);
            EmitSpread(dust, dustCount, true);
            if (flashRoot != null)
            {
                flashRoot.gameObject.SetActive(true);
                flashRoot.localPosition = Vector3.zero;
                flashRoot.localScale = Vector3.one;
            }
            PrepareDebris();
            AcquireSubjectMotion();
        }

        private void EmitSpread(ParticleSystem particles, int count, bool isDust)
        {
            if (particles == null || count <= 0) return;
            float seedAngle = (Context.Seed % 97) * 0.061f;
            for (int index = 0; index < count; index++)
            {
                float angle = seedAngle + index * 2.399963f;
                Vector3 direction = new Vector3(Mathf.Cos(angle),
                    isDust ? 0.20f : 0.40f + (index % 3) * 0.17f, Mathf.Sin(angle)).normalized;
                float speed = (isDust ? 0.30f : 1.15f) * (0.80f + (index % 3) * 0.17f);
                var emission = new ParticleSystem.EmitParams
                {
                    position = Vector3.zero,
                    velocity = direction * speed,
                    applyShapeToPosition = false
                };
                particles.Emit(emission, 1);
            }
        }

        private void PrepareDebris()
        {
            // Fixed geometry count equals the advertised budget; intensity changes speed, not count.
            float seedAngle = (Context.Seed % 97) * 0.061f;
            for (int index = 0; index < debrisPieces.Length && index < DebrisPieceCount; index++)
            {
                float angle = seedAngle + index * Mathf.PI * 0.25f;
                float variation = (Mathf.Sin(index * 2.41f + seedAngle) + 1) * 0.5f;
                float speed = Mathf.Lerp(0.65f, 1.35f, variation) * intensity;
                debrisVelocity[index] = new Vector3(Mathf.Cos(angle) * speed,
                    Mathf.Lerp(0.65f, 1.2f, variation), Mathf.Sin(angle) * speed);
                debrisSize[index] = Mathf.Lerp(0.055f, 0.10f, variation);
                if (debrisPieces[index] == null) continue;
                debrisPieces[index].gameObject.SetActive(true);
                debrisPieces[index].localPosition = Vector3.zero;
                debrisPieces[index].localRotation = Quaternion.identity;
            }
        }

        private void AcquireSubjectMotion()
        {
            subjectMotion = null;
            motionOwner = 0;
            if (Context.Subject == null || !Context.Subject.gameObject.activeInHierarchy) return;
            Transform target = null;
            if (kind == DamageEffectKind.HeavyContact)
                target = FindNamedTransform(Context.Subject, "ImpactPivot");
            else if (kind == DamageEffectKind.EnemyDeath || kind == DamageEffectKind.BuildingDestruction)
                target = Context.Subject;
            if (target == null) return;
            subjectMotion = target.GetComponent<DamageSubjectMotion>();
            if (subjectMotion == null) return;
            motionOwner = subjectMotion.Acquire();
        }

        private static Transform FindNamedTransform(Transform parent, string expectedName)
        {
            if (parent.name == expectedName) return parent;
            for (int index = 0; index < parent.childCount; index++)
            {
                Transform found = FindNamedTransform(parent.GetChild(index), expectedName);
                if (found != null) return found;
            }
            return null;
        }

        protected override void OnAdvance(float normalizedTime, float deltaTime)
        {
            float elapsed = Mathf.Clamp01(normalizedTime) * Lifetime;
            AdvanceFlash(normalizedTime, elapsed);
            AdvanceDebris(normalizedTime, elapsed);
            if (subjectMotion == null) return;
            if (kind == DamageEffectKind.HeavyContact)
            {
                float reach = elapsed < 0.075f ? Mathf.SmoothStep(0, 1, elapsed / 0.075f) :
                    1 - Mathf.SmoothStep(0, 1, (elapsed - 0.075f) / 0.175f);
                subjectMotion.Apply(motionOwner, Vector3.forward * (0.085f * reach),
                    Quaternion.identity, Vector3.one);
            }
            else
            {
                float collapse = Mathf.SmoothStep(0, 1, elapsed / 0.25f);
                float finalHeight = kind == DamageEffectKind.EnemyDeath ? 0.18f : 0.24f;
                subjectMotion.Apply(motionOwner, Vector3.zero,
                    Quaternion.Euler(3 * collapse, 0, -2 * collapse),
                    new Vector3(1 + 0.06f * collapse, Mathf.Lerp(1, finalHeight, collapse), 1));
            }
        }

        private void AdvanceFlash(float normalizedTime, float elapsed)
        {
            if (flashRoot == null) return;
            bool repair = kind == DamageEffectKind.RepairComplete;
            float flashTime = repair ? Lifetime : 0.08f;
            float progress = Mathf.Clamp01(elapsed / Mathf.Max(0.001f, flashTime));
            flashRoot.gameObject.SetActive(progress < 1);
            float scale = repair ? Mathf.Lerp(0.72f, 1.10f, normalizedTime) * (1 - progress * progress) :
                Mathf.Lerp(1.05f, 0.12f, progress) * intensity;
            flashRoot.localScale = Vector3.one * scale;
            flashRoot.localPosition = repair ? Vector3.up * (0.3f * normalizedTime) : Vector3.zero;
        }

        private void AdvanceDebris(float normalizedTime, float elapsed)
        {
            float fade = 1 - Mathf.SmoothStep(0.52f, 1, normalizedTime);
            float originHeight = kind == DamageEffectKind.BuildingDestruction ? 0.34f : 0.17f;
            for (int index = 0; index < debrisPieces.Length && index < DebrisPieceCount; index++)
            {
                Transform piece = debrisPieces[index];
                if (piece == null) continue;
                Vector3 position = debrisVelocity[index] * elapsed;
                position.y = Mathf.Max(0.025f, originHeight + position.y - 2.8f * elapsed * elapsed);
                piece.localPosition = position;
                piece.localRotation = Quaternion.Euler((90 + index * 17) * elapsed,
                    index * 41 + elapsed * 140, (index % 2 == 0 ? 1 : -1) * elapsed * 160);
                piece.localScale = new Vector3(1, 0.65f, 1.3f) * (debrisSize[index] * fade);
            }
        }

        protected override void OnReset()
        {
            if (subjectMotion != null) subjectMotion.Release(motionOwner);
            subjectMotion = null;
            motionOwner = 0;
            if (flashRoot != null)
            {
                flashRoot.gameObject.SetActive(false);
                flashRoot.localPosition = Vector3.zero;
                flashRoot.localScale = Vector3.one;
            }
            for (int index = 0; index < debrisPieces.Length; index++)
            {
                Transform piece = debrisPieces[index];
                if (piece == null) continue;
                piece.gameObject.SetActive(false);
                piece.localPosition = Vector3.zero;
                piece.localRotation = Quaternion.identity;
                piece.localScale = Vector3.zero;
            }
        }
    }
}
