using System;
using System.Collections.Generic;
namespace Defend.Simulation
{
    /// <summary>精确有限窗口中的指数衰减记录。同一步的事件合并，查询不重新遍历整段历史。</summary>
    internal sealed class EnemyObservedActivity
    {
        private readonly List<Sample> samples = new List<Sample>();
        private int first;
        private long lastSampleTick = -1;
        private double signal;
        internal long LastEventTick { get; private set; } = -1;

        internal void Add(double amount, long tick, int memoryTicks)
        {
            Value(tick, memoryTicks);
            signal += amount;
            if (samples.Count > first && samples[samples.Count - 1].Tick == tick)
            {
                var previous = samples[samples.Count - 1];
                samples[samples.Count - 1] = new Sample { Tick = tick, Amount = previous.Amount + amount };
            }
            else samples.Add(new Sample { Tick = tick, Amount = amount });
            LastEventTick = tick;
        }

        internal double Value(long tick, int memoryTicks)
        {
            if (lastSampleTick >= 0 && tick != lastSampleTick) signal *= Math.Exp(-(double)(tick - lastSampleTick) / memoryTicks);
            lastSampleTick = tick;
            while (first < samples.Count && tick - samples[first].Tick >= memoryTicks)
            {
                var expired = samples[first++];
                signal -= expired.Amount * Math.Exp(-(double)(tick - expired.Tick) / memoryTicks);
            }
            if (first == samples.Count) { samples.Clear(); first = 0; signal = 0; }
            else if (first >= 32) { samples.RemoveRange(0, first); first = 0; }
            return Math.Max(0, signal);
        }

        private struct Sample { internal long Tick; internal double Amount; }
    }

}
