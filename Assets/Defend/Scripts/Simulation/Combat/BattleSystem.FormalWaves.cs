using System;
using System.Collections.Generic;

namespace Defend.Simulation
{
    public sealed partial class BattleSystem
    {
        private sealed class IndependentGroupSchedule
        {
            internal WaveGroupDefinition Group;
            internal int Remaining, Order;
            internal long NextTick, ActualDueTick;
        }
        private readonly List<IndependentGroupSchedule> independentSchedules = new List<IndependentGroupSchedule>();
        private readonly List<IndependentGroupSchedule> independentDue = new List<IndependentGroupSchedule>();
        private long[] entranceDelays = Array.Empty<long>();
        private bool[] entranceAttempted = Array.Empty<bool>();
        private static readonly Comparison<IndependentGroupSchedule> IndependentScheduleOrder = (a, b) =>
            a.ActualDueTick != b.ActualDueTick ? a.ActualDueTick.CompareTo(b.ActualDueTick) : a.Order.CompareTo(b.Order);

        private void InitializeIndependentWave(WaveDefinition wave)
        {
            independentSchedules.Clear(); independentDue.Clear();
            entranceDelays = new long[SpawnPoints.Count]; entranceAttempted = new bool[SpawnPoints.Count];
            if (!wave.IndependentGroups) return;
            for (int i = 0; i < wave.Groups.Count; i++)
            {
                var group = wave.Groups[i];
                independentSchedules.Add(new IndependentGroupSchedule { Group = group, Remaining = group.Count,
                    Order = i, NextTick = checked(waveStartTick + group.StartTick) });
            }
        }

        private void StepIndependentWave(long tick, WaveDefinition wave)
        {
            if (enemies.Count >= Settings.MaximumLivingEnemies)
            {
                for (int i = 0; i < entranceDelays.Length; i++) entranceDelays[i]++;
                return;
            }
            independentDue.Clear();
            Array.Clear(entranceAttempted, 0, entranceAttempted.Length);
            foreach (var schedule in independentSchedules)
            {
                schedule.ActualDueTick = checked(schedule.NextTick + entranceDelays[schedule.Group.EntranceIndex]);
                if (schedule.Remaining > 0 && tick >= schedule.ActualDueTick) independentDue.Add(schedule);
            }
            // 先到期的组先尝试。堵住的入口只保留自身时间，不拦住其他入口，也不在放开后补发一团敌人。
            independentDue.Sort(IndependentScheduleOrder);
            foreach (var schedule in independentDue)
            {
                if (enemies.Count >= Settings.MaximumLivingEnemies) break;
                var group = schedule.Group;
                if (entranceAttempted[group.EntranceIndex]) continue;
                entranceAttempted[group.EntranceIndex] = true;
                var spawn = SpawnPoints[group.EntranceIndex];
                var definition = Settings.GetEnemy(group.Kind);
                if (!movement.TrySpawn(spawn, definition, out double x, out double z))
                { entranceDelays[group.EntranceIndex]++; continue; }
                enemies.Add(new EnemyState(nextEnemyId++, spawn, wave.EnemyHealth, group.Kind, definition)
                    { X = x, Z = z, CellX = (int)x, CellZ = (int)z });
                schedule.Remaining--; RemainingToSpawn--;
                schedule.NextTick = checked(tick - entranceDelays[group.EntranceIndex] + group.IntervalTicks);
            }
        }
    }
}
