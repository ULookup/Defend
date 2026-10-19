using System.Collections.Generic;
using Defend.Simulation;
using Defend.PhaseFiveEffects;
using UnityEngine;
using UnityEngine.Pool;

namespace Defend.Unity
{
    /// <summary>按权威路径段和统一显示时间绘制弹体。显示数量不受装饰特效预算限制。</summary>
    public sealed class ProjectilePresentation : MonoBehaviour
    {
        [SerializeField] private LineRenderer rapidPrefab, heavyPrefab, areaPrefab;
        [SerializeField] private Transform projectileRoot;
        private sealed class Flight
        {
            public LineRenderer View;
            public RapidProjectileVisual RapidVisual;
            public DangchenjiProjectileVisual DangchenjiVisual;
            public Vector3 LaunchPosition;
            public BuildingKind Kind;
            public readonly List<ProjectileSnapshot> Segments = new List<ProjectileSnapshot>();
            public int LastVisibleFrame = -1;
        }
        private readonly Dictionary<BuildingKind, ObjectPool<LineRenderer>> pools = new Dictionary<BuildingKind, ObjectPool<LineRenderer>>();
        private readonly Dictionary<long, Flight> flights = new Dictionary<long, Flight>();
        private readonly List<long> retired = new List<long>();
        private long observedTick = -1;
        private readonly Dictionary<long, (double start, double end, bool ended)> observedSegments = new Dictionary<long, (double, double, bool)>();
        public int ActiveCount => flights.Count;
        public void Configure(LineRenderer rapid, LineRenderer heavy, LineRenderer area, Transform root)
        { rapidPrefab = rapid; heavyPrefab = heavy; areaPrefab = area; projectileRoot = root; }
        private ObjectPool<LineRenderer> Pool(BuildingKind kind)
        {
            if (pools.TryGetValue(kind, out var pool)) return pool;
            var prefab = kind == BuildingKind.AreaTurret ? areaPrefab : kind == BuildingKind.BurstTurret ? heavyPrefab : rapidPrefab;
            if (prefab == null) throw new System.InvalidOperationException("缺少实际弹体预制体：" + kind);
            pool = new ObjectPool<LineRenderer>(() => Instantiate(prefab, projectileRoot == null ? transform : projectileRoot),
                view => { view.gameObject.SetActive(true); view.enabled = true; }, view => { if (view != null) view.gameObject.SetActive(false); },
                view => { if (view != null) { if (Application.isPlaying) Destroy(view.gameObject); else DestroyImmediate(view.gameObject); } }, false, 32, 4096);
            pools.Add(kind, pool); return pool;
        }
        public void ObserveStep(GameWorld world)
        {
            if (world.Battle == null) return;
            if (observedTick != world.CurrentTick) { observedTick = world.CurrentTick; observedSegments.Clear(); }
            foreach (var segment in world.Battle.Projectiles)
            {
                if (observedSegments.TryGetValue(segment.Id, out var observed) && observed.start == segment.StartTime &&
                    observed.end == segment.EndTime && observed.ended == segment.IsEnded) continue;
                observedSegments[segment.Id] = (segment.StartTime, segment.EndTime, segment.IsEnded);
                if (!flights.TryGetValue(segment.Id, out var flight))
                {
                    var pool = Pool(segment.TurretKind);
                    if (pool == null) continue;
                    flight = new Flight { Kind = segment.TurretKind, View = pool.Get() };
                    if (segment.TurretKind == BuildingKind.Turret)
                        flight.RapidVisual = flight.View.GetComponent<RapidProjectileVisual>();
                    if (segment.TurretKind == BuildingKind.AreaTurret)
                        flight.DangchenjiVisual = flight.View.GetComponent<DangchenjiProjectileVisual>();
                    flight.LaunchPosition = new Vector3((float)segment.PreviousX, (float)segment.PreviousY, (float)segment.PreviousZ);
                    flight.View.enabled = false;
                    if (flight.RapidVisual != null) flight.RapidVisual.SetVisible(false);
                    if (flight.DangchenjiVisual != null) flight.DangchenjiVisual.SetVisible(false);
                    flights.Add(segment.Id, flight);
                }
                if (flight.Segments.Count > 0)
                {
                    var previous = flight.Segments[flight.Segments.Count - 1];
                    if (previous.StartTime == segment.StartTime && previous.EndTime == segment.EndTime)
                    {
                        // 同一步即时命令可终止弹药；更新结束状态，不重复增加路径段。
                        flight.Segments[flight.Segments.Count - 1] = segment;
                        continue;
                    }
                }
                flight.Segments.Add(segment);
            }
        }
        public void Refresh(double seconds, bool paused = false)
        {
            retired.Clear();
            foreach (var pair in flights)
            {
                var flight = pair.Value;
                while (flight.Segments.Count > 1 && flight.Segments[0].EndTime < seconds - 1e-9)
                    flight.Segments.RemoveAt(0);
                var segment = flight.Segments[0];
                if (seconds < segment.StartTime)
                {
                    flight.View.enabled = false;
                    if (flight.RapidVisual != null) flight.RapidVisual.SetVisible(false);
                    if (flight.DangchenjiVisual != null) flight.DangchenjiVisual.SetVisible(false);
                    continue;
                }
                bool ended = segment.IsEnded && seconds >= segment.EndTime - 1e-9;
                // 一帧跨过整段飞行时保留真实短路径一帧；接触点不再向旧目标延长。
                if (ended && flight.LastVisibleFrame >= 0 && flight.LastVisibleFrame < Time.frameCount && !paused)
                { retired.Add(pair.Key); continue; }
                var from = new Vector3((float)segment.PreviousX, (float)segment.PreviousY, (float)segment.PreviousZ);
                var to = new Vector3((float)segment.X, (float)segment.Y, (float)segment.Z);
                // 快照采样权威曲线，避免抛物线在墙顶附近被线性插值画进墙内。
                segment.SamplePosition(seconds, out double headX, out double headY, out double headZ);
                var head = new Vector3((float)headX, (float)headY, (float)headZ);
                Vector3 tail = ended ? from : Vector3.Lerp(from, head, .65f);
                if (flight.Kind == BuildingKind.Turret)
                {
                    // 固定短尾迹跨逻辑段连续，但始终裁切在真实出发点与真实弹头之间。
                    Vector3 travelled = head - flight.LaunchPosition;
                    float length = travelled.magnitude;
                    tail = length > .00001f ? head - travelled / length * Mathf.Min(.48f, length) : head;
                }
                if (flight.DangchenjiVisual != null) flight.DangchenjiVisual.SetPose(head, to - from, seconds);
                else if (flight.RapidVisual != null) flight.RapidVisual.SetSegment(tail, head);
                else
                {
                    if (segment.SplashRadius > 0)
                    {
                        double visibleEnd = System.Math.Min(seconds, segment.EndTime);
                        double visibleStart = ended ? segment.StartTime : segment.StartTime + (visibleEnd - segment.StartTime) * .65;
                        SetTrajectoryTrail(flight.View, segment, visibleStart, visibleEnd);
                    }
                    else
                    {
                        flight.View.positionCount = 2;
                        flight.View.SetPosition(0, tail); flight.View.SetPosition(1, head);
                    }
                    flight.View.enabled = true;
                }
                if (ended && flight.LastVisibleFrame < 0) flight.LastVisibleFrame = Time.frameCount;
            }
            foreach (long id in retired) { var flight = flights[id]; Pool(flight.Kind).Release(flight.View); flights.Remove(id); }
        }
        internal static void SetTrajectoryTrail(LineRenderer line, ProjectileSnapshot segment, double fromTime, double toTime)
        {
            // 对范围弹的短尾迹也采样同一曲线，不能用整步直线弦穿过墙顶。
            int count = segment.SplashRadius > 0 ? 5 : 2;
            line.positionCount = count;
            for (int index = 0; index < count; index++)
            {
                double time = fromTime + (toTime - fromTime) * index / (count - 1);
                segment.SamplePosition(time, out double x, out double y, out double z);
                line.SetPosition(index, new Vector3((float)x, (float)y, (float)z));
            }
        }
        public void ResetSession()
        {
            foreach (var flight in flights.Values) if (flight.View != null) Pool(flight.Kind)?.Release(flight.View);
            flights.Clear(); retired.Clear(); observedSegments.Clear(); observedTick = -1;
        }
        private void OnDisable() { ResetSession(); }
        private void OnDestroy() { ResetSession(); foreach (var pool in pools.Values) pool.Clear(); }
    }
}
