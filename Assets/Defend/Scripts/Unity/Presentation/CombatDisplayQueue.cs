using System;
using System.Collections.Generic;
using Defend.Simulation;

namespace Defend.Unity
{
    /// <summary>只缓存尚未到显示时刻的不可变结果；事件身份负责同一步命令刷新去重。</summary>
    public sealed class CombatDisplayQueue
    {
        private readonly List<WorldEventSnapshot> results = new List<WorldEventSnapshot>();
        private readonly List<ShotSnapshot> shots = new List<ShotSnapshot>();
        private long latestResult, latestShot;
        public int PendingResults => results.Count;
        public void Observe(IReadOnlyList<WorldEventSnapshot> events, IReadOnlyList<ShotSnapshot> fired)
        {
            foreach (var item in events)
                if (item.EventId > latestResult) { results.Add(item); latestResult = item.EventId; }
            if (fired != null)
                foreach (var item in fired)
                    if (item.Id > latestShot) { shots.Add(item); latestShot = item.Id; }
            results.Sort((a, b) => { int order = a.Time.CompareTo(b.Time); return order != 0 ? order : a.EventId.CompareTo(b.EventId); });
            shots.Sort((a, b) => { int order = a.Time.CompareTo(b.Time); return order != 0 ? order : a.Id.CompareTo(b.Id); });
        }
        public void Drain(double seconds, Action<WorldEventSnapshot> result, Action<ShotSnapshot> shot)
        {
            int resultCount = 0, shotCount = 0;
            // 同时刻先结算在途命中，再显示步末发射，保持权威事务顺序。
            while (true)
            {
                bool hasResult = resultCount < results.Count && results[resultCount].Time <= seconds + 1e-9;
                bool hasShot = shotCount < shots.Count && shots[shotCount].Time <= seconds + 1e-9;
                if (!hasResult && !hasShot) break;
                if (hasResult && (!hasShot || results[resultCount].Time <= shots[shotCount].Time)) result(results[resultCount++]);
                else shot(shots[shotCount++]);
            }
            if (resultCount > 0) results.RemoveRange(0, resultCount);
            if (shotCount > 0) shots.RemoveRange(0, shotCount);
        }
        public void Clear() { results.Clear(); shots.Clear(); latestResult = latestShot = 0; }
    }
}
