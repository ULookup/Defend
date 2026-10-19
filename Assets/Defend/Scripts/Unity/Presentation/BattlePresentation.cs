using System.Collections.Generic;
using Defend.Simulation;
using UnityEngine;
using UnityEngine.Pool;

namespace Defend.Unity
{
    /// <summary>池化敌人，按统一显示时刻采样已交付的真实移动分段，并延迟致命回收。</summary>
    public sealed class BattlePresentation : MonoBehaviour
    {
        [SerializeField] private EnemyView enemyPrefab;
        [SerializeField] private EnemyView fastEnemyPrefab;
        [SerializeField] private EnemyView heavyEnemyPrefab;
        [SerializeField] private EnemyView demolisherEnemyPrefab;
        [SerializeField] private EnemyView convergerEnemyPrefab;
        public void ShowSelection(long id) { foreach (var pair in enemyViews) pair.Value.ShowSelection(pair.Key == id); }
        [SerializeField] private bool externalCombatEffects;
        private readonly Dictionary<EnemyKind, ObjectPool<EnemyView>> variantPools = new Dictionary<EnemyKind, ObjectPool<EnemyView>>();
        public EnemyView FindView(long id) => enemyViews.TryGetValue(id, out var view) ? view : null;
        private ObjectPool<EnemyView> Pool(EnemyKind kind) => variantPools.TryGetValue(kind, out var value) ? value : enemyPool;
        private void AddVariant(EnemyKind kind, EnemyView prefab)
        {
            if (prefab == null || variantPools.ContainsKey(kind)) return;
            variantPools.Add(kind, new ObjectPool<EnemyView>(() => Instantiate(prefab, enemyRoot),
                view => { view.ResetForReuse(); view.gameObject.SetActive(true); }, view => { if (view != null) view.gameObject.SetActive(false); },
                view => { if (view != null) { if (Application.isPlaying) Destroy(view.gameObject); else DestroyImmediate(view.gameObject); } }, false, 16, 256));
        }
        [SerializeField] private LineRenderer shotPrefab;
        [SerializeField] private Transform enemyRoot;
        [SerializeField] private Transform shotRoot;
        [SerializeField, Min(1)] private int maximumVisibleShots = 256;
        [SerializeField] private EnemyTargetView targetMarkerPrefab;
        [SerializeField] private Transform targetMarkerRoot;
        [SerializeField, Range(1, 200)] private int maximumVisibleTargetMarkers = 100;
        private ObjectPool<EnemyView> enemyPool;
        private ObjectPool<LineRenderer> shotPool;
        private ObjectPool<EnemyTargetView> targetMarkerPool;
        private readonly Dictionary<long, EnemyView> enemyViews = new Dictionary<long, EnemyView>();
        private readonly List<(EnemyView view, double endTime)> dyingViews = new List<(EnemyView, double)>();
        public int DyingViewCount => dyingViews.Count;
        private readonly List<LineRenderer> shotViews = new List<LineRenderer>();
        private readonly List<EnemyTargetView> targetMarkers = new List<EnemyTargetView>();
        private readonly Dictionary<(long building, double x, double z, bool searching), int> targetIndices =
            new Dictionary<(long, double, double, bool), int>();
        private readonly List<(double x, double z, bool searching, bool attacking)> targetSnapshots =
            new List<(double, double, bool, bool)>();
        private long lastAppliedTick = -1;
        private double displaySeconds;
        private readonly struct EnemyFacingFrame
        {
            public readonly double Time, AttackTime, AttackDuration, NextAttackTime;
            public readonly Vector3 Target, AttackTarget;
            public readonly bool HasTarget, HasAttack, PreparingAttack;
            public EnemyFacingFrame(EnemyState state, long tick, int ticksPerSecond)
            {
                Time = tick / (double)ticksPerSecond;
                AttackTime = state.LastAttackTick / (double)ticksPerSecond;
                NextAttackTime = state.NextAttackTick / (double)ticksPerSecond;
                AttackDuration = Mathf.Max(1, Mathf.CeilToInt(ticksPerSecond * .22f)) / (double)ticksPerSecond;
                HasTarget = state.TargetBuildingId > 0;
                HasAttack = state.LastAttackTick >= 0 && state.LastAttackTargetBuildingId > 0;
                PreparingAttack = HasAttack && state.CurrentAction == EnemyAction.Attacking &&
                    state.LastAttackTargetBuildingId == state.CurrentAttackBuildingId;
                Target = new Vector3((float)state.TargetX, 0, (float)state.TargetZ);
                AttackTarget = new Vector3((float)state.LastAttackTargetX, 0, (float)state.LastAttackTargetZ);
            }
        }
        private sealed class EnemyDisplay
        {
            public EnemyState Source;
            public double SpawnTime, RemovalTime = double.PositiveInfinity;
            public double SampledUntil, TravelledDistance;
            public readonly List<EnemyMotionSegment> Segments = new List<EnemyMotionSegment>();
            public readonly List<EnemyFacingFrame> Facing = new List<EnemyFacingFrame>();
        }
        private readonly CombatDisplayQueue fallbackResults = new CombatDisplayQueue();
        private readonly Dictionary<long, List<ProjectileSnapshot>> fallbackPaths = new Dictionary<long, List<ProjectileSnapshot>>();
        private readonly List<long> completedPaths = new List<long>();
        private readonly Dictionary<long, EnemyDisplay> displays = new Dictionary<long, EnemyDisplay>();
        public void Configure(EnemyView enemy, LineRenderer shot, Transform enemies, Transform shots)
        { enemyPrefab = enemy; shotPrefab = shot; enemyRoot = enemies; shotRoot = shots; }
        public void ConfigureTargetMarkers(EnemyTargetView prefab, Transform root)
        { targetMarkerPrefab = prefab; targetMarkerRoot = root; }

        public void Initialize()
        {
            if (enemyPool == null)
            {
                enemyPool = new ObjectPool<EnemyView>(() => Instantiate(enemyPrefab, enemyRoot),
                    view => { view.ResetForReuse(); view.gameObject.SetActive(true); }, view => { if (view != null) view.gameObject.SetActive(false); },
                    view => { if (view != null) { if (Application.isPlaying) Destroy(view.gameObject); else DestroyImmediate(view.gameObject); } }, false, 32, 256);
                shotPool = new ObjectPool<LineRenderer>(() => Instantiate(shotPrefab, shotRoot),
                    view => view.gameObject.SetActive(true), view => { if (view != null) view.gameObject.SetActive(false); },
                    view => { if (view != null) { if (Application.isPlaying) Destroy(view.gameObject); else DestroyImmediate(view.gameObject); } }, false, 32, maximumVisibleShots);
            }
            if (targetMarkerPool == null && targetMarkerPrefab != null && targetMarkerRoot != null)
                targetMarkerPool = new ObjectPool<EnemyTargetView>(() => Instantiate(targetMarkerPrefab, targetMarkerRoot),
                    view => view.gameObject.SetActive(true), view => { if (view != null) view.gameObject.SetActive(false); },
                    view => { if (view != null) { if (Application.isPlaying) Destroy(view.gameObject); else DestroyImmediate(view.gameObject); } }, false, 8, maximumVisibleTargetMarkers);
            AddVariant(EnemyKind.Fast, fastEnemyPrefab);
            AddVariant(EnemyKind.Heavy, heavyEnemyPrefab);
            AddVariant(EnemyKind.Demolisher, demolisherEnemyPrefab);
            AddVariant(EnemyKind.Converger, convergerEnemyPrefab);
            foreach (EnemyView view in enemyViews.Values) if (view != null) Pool(view.Kind).Release(view);
            enemyViews.Clear();
            foreach (var dying in dyingViews) if (dying.view != null) Pool(dying.view.Kind).Release(dying.view);
            dyingViews.Clear();
            foreach (LineRenderer view in shotViews) if (view != null) shotPool.Release(view);
            shotViews.Clear();
            foreach (EnemyTargetView view in targetMarkers) if (view != null) targetMarkerPool.Release(view);
            targetMarkers.Clear();
            targetIndices.Clear();
            targetSnapshots.Clear();
            lastAppliedTick = -1; displaySeconds = 0; displays.Clear(); fallbackResults.Clear(); fallbackPaths.Clear();
        }

        public void ObserveStep(GameWorld world)
        {
            if (world.Battle == null || lastAppliedTick == world.CurrentTick) return;
            lastAppliedTick = world.CurrentTick;
            double seconds = world.CurrentTick / (double)world.Settings.TicksPerSecond;
            foreach (var state in world.Battle.Enemies)
            {
                if (!state.IsAlive) continue;
                if (!displays.TryGetValue(state.Id, out var display))
                {
                    var view = Pool(state.Kind).Get();
                    enemyViews.Add(state.Id, view);
                    display = new EnemyDisplay { Source = state, SpawnTime = seconds, SampledUntil = seconds };
                    displays.Add(state.Id, display);
                    view.Apply(state, world.CurrentTick, world.Settings.TicksPerSecond, false, false);
                    view.gameObject.SetActive(false);
                    // 隐藏到出生显示时刻会触发 OnDisable 清理；在隐藏之后写入出生快照。
                    view.ApplyDisplayHealth(state.Health, state.MaximumHealth);
                }
                else enemyViews[state.Id].Apply(state, world.CurrentTick, world.Settings.TicksPerSecond, false, false);
            }
            // 已死亡敌人的对象在模拟清单移除后仍可读取一次，复制分段后不依赖其后续变化。
            foreach (var display in displays.Values)
            {
                display.Facing.Add(new EnemyFacingFrame(display.Source, world.CurrentTick, world.Settings.TicksPerSecond));
                foreach (var segment in display.Source.MotionSegments)
                    if (segment.EndTime >= seconds - 1.0 / world.Settings.TicksPerSecond - 1e-9)
                        display.Segments.Add(segment);
                if (!display.Source.IsAlive && double.IsPositiveInfinity(display.RemovalTime))
                    display.RemovalTime = seconds;
            }
            foreach (var result in world.Events)
                if (result.Kind == WorldEventKind.EnemyDeath && displays.TryGetValue(result.SubjectId, out var display))
                    display.RemovalTime = result.Time;
            if (!externalCombatEffects)
            {
                fallbackResults.Observe(world.Events, null);
                foreach (var projectile in world.Battle.Projectiles)
                {
                    if (!fallbackPaths.TryGetValue(projectile.Id, out var path))
                    { path = new List<ProjectileSnapshot>(); fallbackPaths.Add(projectile.Id, path); }
                    path.Add(projectile);
                }
                RefreshTargetMarkers(world);
            }
        }

        public void Refresh(GameWorld world, double displayTick, bool paused = false)
        {
            if (world.Battle == null) return;
            ObserveStep(world);
            double seconds = displayTick / world.Settings.TicksPerSecond;
            displaySeconds = seconds;
            RefreshDeaths(seconds);
            foreach (var pair in displays)
            {
                var display = pair.Value;
                var view = enemyViews[pair.Key];
                bool visible = seconds + 1e-9 >= display.SpawnTime;
                view.gameObject.SetActive(visible);
                view.SetPaused(paused);
                if (!visible) continue;
                double positionTime = System.Math.Min(seconds, display.RemovalTime);
                var segments = display.Segments;
                // 累加真实分段的已显示部分，低帧率跨越转角也不以首尾直线冒充行进距离。
                if (positionTime > display.SampledUntil)
                    foreach (var travelled in segments)
                    {
                        double start = System.Math.Max(display.SampledUntil, travelled.StartTime);
                        double end = System.Math.Min(positionTime, travelled.EndTime);
                        if (end <= start || travelled.EndTime <= travelled.StartTime) continue;
                        double dx = travelled.EndX - travelled.StartX, dz = travelled.EndZ - travelled.StartZ;
                        display.TravelledDistance += System.Math.Sqrt(dx * dx + dz * dz) *
                            (end - start) / (travelled.EndTime - travelled.StartTime);
                    }
                display.SampledUntil = positionTime;
                while (segments.Count > 1 && segments[0].EndTime < positionTime - 1e-9) segments.RemoveAt(0);
                if (segments.Count == 0)
                {
                    view.SetAnimationMovement(display.TravelledDistance, false);
                    view.Interpolate(1f); ApplyFacing(view, display, seconds, Vector3.zero); continue;
                }
                var segment = segments[0];
                segment.Sample(positionTime, out double x, out double z);
                var position = new Vector3((float)x, 0, (float)z);
                view.SetDisplayPosition(position, position);
                view.Interpolate(1f);
                Vector3 movement = new Vector3((float)(segment.EndX - segment.StartX), 0, (float)(segment.EndZ - segment.StartZ));
                bool moving = positionTime >= segment.StartTime && positionTime <= segment.EndTime + 1e-9 && movement.sqrMagnitude > .00000001f;
                view.SetAnimationMovement(display.TravelledDistance, moving);
                ApplyFacing(view, display, seconds, movement);
            }
            if (!externalCombatEffects)
            {
                fallbackResults.Drain(seconds, ApplyResult, null);
                RefreshFallbackProjectiles(seconds);
            }
        }

        private static void ApplyFacing(EnemyView view, EnemyDisplay display, double seconds, Vector3 movement)
        {
            var frames = display.Facing;
            while (frames.Count > 1 && frames[1].Time <= seconds + 1e-9) frames.RemoveAt(0);
            if (frames.Count == 0) return;
            var frame = frames[0];
            bool attacking = frame.HasAttack && seconds >= frame.AttackTime && seconds < frame.AttackTime + System.Math.Max(frame.AttackDuration, view.AttackRecoverySeconds);
            Vector3 direction = attacking || frame.PreparingAttack ? frame.AttackTarget - view.transform.position :
                movement.sqrMagnitude > .000001f ? movement : frame.HasTarget ? frame.Target - view.transform.position : Vector3.zero;
            view.ApplyDisplayFacing(direction, attacking);
            view.SampleAttackMotion(seconds, frame.AttackTime, frame.NextAttackTime, frame.HasAttack, frame.PreparingAttack);
        }

        private void RefreshFallbackProjectiles(double seconds)
        {
            completedPaths.Clear();
            int count = 0;
            foreach (var pair in fallbackPaths)
            {
                var path = pair.Value;
                while (path.Count > 1 && path[0].EndTime < seconds - 1e-9) path.RemoveAt(0);
                var segment = path[0];
                if (seconds < segment.StartTime) continue;
                while (shotViews.Count <= count) shotViews.Add(shotPool.Get());
                var line = shotViews[count++];
                ProjectilePresentation.SetTrajectoryTrail(line, segment, segment.StartTime, System.Math.Min(seconds, segment.EndTime));
                if (segment.IsEnded && seconds >= segment.EndTime) completedPaths.Add(pair.Key);
            }
            foreach (long id in completedPaths) fallbackPaths.Remove(id);
            while (shotViews.Count > count)
            { int index = shotViews.Count - 1; shotPool.Release(shotViews[index]); shotViews.RemoveAt(index); }
        }

        public void ApplyResult(WorldEventSnapshot result)
        {
            if (!enemyViews.TryGetValue(result.SubjectId, out var view)) return;
            if (result.Kind == WorldEventKind.EnemyHit)
            {
                view.ApplyHit(result);
            }
            else if (result.Kind == WorldEventKind.EnemyDeath)
            {
                enemyViews.Remove(result.SubjectId); displays.Remove(result.SubjectId);
                Vector3 position = new Vector3((float)result.X, 0, (float)result.Z);
                view.SetDisplayPosition(position, position); view.Interpolate(1f);
                float duration = view.BeginDeath(result.Time);
                if (duration > 0 && displaySeconds < result.Time + duration)
                {
                    view.SampleDeath(displaySeconds);
                    dyingViews.Add((view, result.Time + duration));
                }
                else ReleaseView(view);
            }
        }

        private void RefreshDeaths(double seconds)
        {
            for (int index = dyingViews.Count - 1; index >= 0; index--)
            {
                var dying = dyingViews[index];
                dying.view.SampleDeath(seconds);
                if (seconds + 1e-9 < dying.endTime) continue;
                ReleaseView(dying.view); dyingViews.RemoveAt(index);
            }
        }

        private void ReleaseView(EnemyView view)
        { view.ResetForReuse(); Pool(view.Kind).Release(view); }

        public void AdvanceFeedback(float seconds)
        {
            foreach (var view in enemyViews.Values) view.AdvanceFeedback(seconds);
        }

        private void RefreshTargetMarkers(GameWorld world)
        {
            if (targetMarkerPool == null) return;
            targetIndices.Clear();
            targetSnapshots.Clear();
            if (world.Battle.Settings.EnemyBehavior.Enabled)
                foreach (EnemyState enemy in world.Battle.Enemies)
                {
                    if (!enemy.IsAlive || enemy.Kind != EnemyKind.Heavy || enemy.TargetBuildingId <= 0) continue;
                    bool searching = enemy.TargetPurpose == EnemyTargetPurpose.SearchLastKnownPosition;
                    bool attacking = EnemyView.IsShowingAttack(enemy, world.CurrentTick, world.Settings.TicksPerSecond);
                    var key = (enemy.TargetBuildingId, enemy.TargetX, enemy.TargetZ, searching);
                    if (targetIndices.TryGetValue(key, out int index))
                    {
                        if (attacking) targetSnapshots[index] = (enemy.TargetX, enemy.TargetZ, searching, true);
                        continue;
                    }
                    if (targetSnapshots.Count >= maximumVisibleTargetMarkers) continue;
                    targetIndices.Add(key, targetSnapshots.Count);
                    targetSnapshots.Add((enemy.TargetX, enemy.TargetZ, searching, attacking));
                }
            while (targetMarkers.Count < targetSnapshots.Count) targetMarkers.Add(targetMarkerPool.Get());
            while (targetMarkers.Count > targetSnapshots.Count)
            {
                int last = targetMarkers.Count - 1;
                targetMarkerPool.Release(targetMarkers[last]);
                targetMarkers.RemoveAt(last);
            }
            for (int index = 0; index < targetMarkers.Count; index++)
            {
                var snapshot = targetSnapshots[index];
                targetMarkers[index].Apply(snapshot.x, snapshot.z, snapshot.searching, snapshot.attacking);
            }
        }

        private void OnDestroy() { foreach (var pool in variantPools.Values) pool.Clear(); enemyPool?.Clear(); shotPool?.Clear(); targetMarkerPool?.Clear(); }
    }
}
