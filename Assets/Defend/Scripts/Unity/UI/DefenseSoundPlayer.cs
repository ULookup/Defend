using System;
using System.Collections.Generic;
using UnityEngine;

namespace Defend.Unity
{
    /// <summary>固定声部播放器。只消费已经显示的结果，不保存待播队列或参与战斗结算。</summary>
    internal sealed class DefenseSoundPlayer
    {
        private const int VoiceLimit = 24;
        private sealed class CueState
        {
            public DefenseSoundDefinition Definition;
            public double NextAllowedTime;
            public int LastVariant = -1;
        }
        private sealed class Voice
        {
            public AudioSource Source;
            public DefenseSoundDefinition Definition;
            public double StartedAt;
            public long SubjectId;
            public float Gain;
        }

        private readonly Dictionary<DefenseSoundCue, CueState> cues = new Dictionary<DefenseSoundCue, CueState>();
        private readonly Voice[] voices = new Voice[VoiceLimit];
        private readonly System.Random variation = new System.Random();
        private bool combatPaused;
        private bool presentationStep;
        private double weaponDuckUntil;
        private double lastMixTime;
        private float mixGain = 1;
        private float effectsVolume;
        private bool muted;
        public int PlayedCount { get; private set; }
        public int SuppressedCount { get; private set; }
        public int ActiveCount
        {
            get { int count = 0; foreach (var voice in voices) if (voice.Source.isPlaying) count++; return count; }
        }

        public DefenseSoundPlayer(Transform parent, AudioSource existingSource, DefenseSoundLibrary library)
        {
            foreach (var definition in library.Sounds)
                if (definition != null) cues[definition.Cue] = new CueState { Definition = definition };
            for (int index = 0; index < voices.Length; index++)
            {
                AudioSource source;
                if (index == 0 && existingSource != null) source = existingSource;
                else
                {
                    var root = new GameObject("音效声部 " + (index + 1), typeof(AudioSource));
                    root.transform.SetParent(parent, false);
                    source = root.GetComponent<AudioSource>();
                }
                source.Stop(); source.playOnAwake = false; source.loop = false;
                source.dopplerLevel = 0; source.rolloffMode = AudioRolloffMode.Linear;
                source.minDistance = 12; source.maxDistance = 90;
                source.spatialBlend = 0; source.reverbZoneMix = .1f;
                if (existingSource != null) source.outputAudioMixerGroup = existingSource.outputAudioMixerGroup;
                voices[index] = new Voice { Source = source };
            }
        }

        public void SetVolume(float value, bool isMuted)
        {
            effectsVolume = value; muted = isMuted;
            // 静音或拉至零时结束瞬态，恢复音量后不补播静音期间的声音。
            if (muted || effectsVolume <= 0) StopAll(false);
            RefreshVolumes();
        }

        public void SetCombatPaused(bool paused)
        {
            if (combatPaused == paused) return;
            combatPaused = paused;
            if (paused)
                foreach (var voice in voices)
                    if (voice.Definition != null && voice.Definition.Combat) Stop(voice);
        }

        public void BeginPresentationStep() => presentationStep = true;
        public void EndPresentationStep() => presentationStep = false;

        public bool Play(DefenseSoundCue cue, Vector3 position, bool spatial, long subjectId = 0)
        {
            if (!cues.TryGetValue(cue, out var state)) return false;
            var definition = state.Definition;
            double now = Time.realtimeSinceStartupAsDouble;
            if (muted || effectsVolume <= 0 || (combatPaused && definition.Combat && (!presentationStep || definition.Loop)) || now < state.NextAllowedTime)
            { SuppressedCount++; return false; }
            if (definition.Clips == null || definition.Clips.Count == 0) return false;
            int concurrent = 0;
            Voice available = null, weakest = null, oldestMatching = null;
            foreach (var voice in voices)
            {
                // 阶段提示较长，新阶段到来后结束旧提示，避免开波、清波与结算叠播。
                if (IsPhaseAnnouncement(cue) && voice.Definition != null &&
                    voice.Definition.Cue != cue && IsPhaseAnnouncement(voice.Definition.Cue)) Stop(voice);
                if (!voice.Source.isPlaying)
                { if (available == null) available = voice; continue; }
                if (voice.Definition != null && voice.Definition.Cue == cue)
                {
                    concurrent++;
                    if (oldestMatching == null || voice.StartedAt < oldestMatching.StartedAt) oldestMatching = voice;
                }
                if (voice.Definition != null && (weakest == null || voice.Definition.Priority < weakest.Definition.Priority ||
                    (voice.Definition.Priority == weakest.Definition.Priority && voice.StartedAt < weakest.StartedAt))) weakest = voice;
            }
            if (concurrent >= definition.MaximumConcurrent)
            {
                // 点击与选择保留即时反馈；复用旧声部，不因素材尾音占满通道而漏掉新操作。
                if ((cue == DefenseSoundCue.UiClick || cue == DefenseSoundCue.Selection) && oldestMatching != null)
                    available = oldestMatching;
                else { SuppressedCount++; return false; }
            }
            if (available == null)
            {
                if (weakest == null || weakest.Definition.Priority >= definition.Priority)
                { SuppressedCount++; return false; }
                available = weakest;
            }
            int variant = NextVariant(state, definition.Clips.Count);
            AudioClip clip = definition.Clips[variant];
            if (clip == null) return false;
            Stop(available);
            available.Definition = definition; available.StartedAt = now;
            available.SubjectId = subjectId; available.Gain = definition.Gain;
            available.Source.transform.position = position;
            available.Source.clip = clip;
            available.Source.loop = definition.Loop;
            available.Source.spatialBlend = spatial ? definition.SpatialBlend : 0;
            available.Source.priority = 256 - Mathf.Clamp(definition.Priority * 2, 0, 256);
            available.Source.pitch = Mathf.Lerp(definition.PitchRange.x, definition.PitchRange.y, (float)variation.NextDouble());
            state.LastVariant = variant;
            state.NextAllowedTime = now + definition.MinimumInterval;
            if (definition.DuckWeapons) weaponDuckUntil = Math.Max(weaponDuckUntil, now + .18);
            RefreshVolumes(available);
            available.Source.Play(); PlayedCount++;
            return true;
        }

        private static bool IsPhaseAnnouncement(DefenseSoundCue cue) =>
            cue == DefenseSoundCue.WaveStart || cue == DefenseSoundCue.WaveClear ||
            cue == DefenseSoundCue.Victory || cue == DefenseSoundCue.Defeat;

        private int NextVariant(CueState state, int count)
        {
            if (count <= 1) return 0;
            if (state.LastVariant < 0) return variation.Next(count);
            int result = variation.Next(count - 1);
            return result >= state.LastVariant ? result + 1 : result;
        }

        public void SetRailgunCharge(long subjectId, Vector3 position, float progress, bool active)
        {
            Voice current = null;
            foreach (var voice in voices)
                if (voice.Source.isPlaying && voice.SubjectId == subjectId && voice.Definition?.Cue == DefenseSoundCue.RailgunCharge)
                { current = voice; break; }
            if (!active || combatPaused)
            { if (current != null) Stop(current); return; }
            if (current == null)
            {
                if (!Play(DefenseSoundCue.RailgunCharge, position, true, subjectId)) return;
                foreach (var voice in voices)
                    if (voice.Source.isPlaying && voice.SubjectId == subjectId && voice.Definition?.Cue == DefenseSoundCue.RailgunCharge)
                    { current = voice; break; }
            }
            if (current == null) return;
            current.Source.transform.position = position;
            current.Source.pitch = Mathf.Lerp(.82f, 1.18f, Mathf.Clamp01(progress));
            current.Gain = current.Definition.Gain * Mathf.Lerp(.45f, 1, Mathf.Clamp01(progress));
        }

        public void StopCharges()
        {
            foreach (var voice in voices)
                if (voice.Definition?.Cue == DefenseSoundCue.RailgunCharge) Stop(voice);
        }

        public void RefreshVolumes() => RefreshVolumes(null);

        private void RefreshVolumes(Voice pending)
        {
            double now = Time.realtimeSinceStartupAsDouble;
            bool duck = now < weaponDuckUntil;
            float gainSum = 0;
            foreach (var voice in voices)
            {
                if (voice != pending && voice.Definition != null && !voice.Source.isPlaying) Stop(voice);
                if (voice.Definition != null) gainSum += voice.Gain;
            }
            // 各声部相加仍留出余量。新瞬态立即获得余量，结束后缓慢释放，避免击杀声被后续枪声淹没。
            float targetGain = .72f / Mathf.Max(.72f, gainSum);
            float release = (float)Math.Max(0, now - lastMixTime) * 2;
            mixGain = targetGain < mixGain ? targetGain : Mathf.MoveTowards(mixGain, targetGain, release);
            lastMixTime = now;
            foreach (var voice in voices)
            {
                var cue = voice.Definition?.Cue;
                bool weapon = cue == DefenseSoundCue.RapidFire || cue == DefenseSoundCue.HeavyFire ||
                    cue == DefenseSoundCue.AreaFire || cue == DefenseSoundCue.RailgunFire || cue == DefenseSoundCue.RailgunCharge;
                voice.Source.volume = muted ? 0 : effectsVolume * voice.Gain * mixGain * (duck && weapon ? .55f : 1);
            }
        }

        public void ResetSession()
        {
            StopAll(true); combatPaused = presentationStep = false; weaponDuckUntil = 0; mixGain = 1;
            PlayedCount = SuppressedCount = 0;
        }

        public void StopAll(bool clearHistory)
        {
            foreach (var voice in voices) Stop(voice);
            if (clearHistory)
                foreach (var state in cues.Values) { state.NextAllowedTime = 0; state.LastVariant = -1; }
        }

        private static void Stop(Voice voice)
        {
            voice.Source.Stop(); voice.Source.clip = null; voice.Source.loop = false;
            voice.Definition = null; voice.SubjectId = 0; voice.Gain = 0;
        }
    }
}
