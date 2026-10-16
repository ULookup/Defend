using System;
using System.Collections.Generic;

namespace Defend.Simulation
{
    public enum EnemyKind { Normal = 0, Fast = 1, Heavy = 2, Demolisher = 3, Converger = 4 }
    public enum EnemyAction { Advancing, Detouring, WaitingForPassage, Attacking }
    /// <summary>敌人的权威位置以格子为单位；表现对象只读取，不参与移动或伤害结算。</summary>
    public sealed class EnemyState
    {
        public long Id { get; }
        private readonly List<EnemyMotionSegment> motionSegments = new List<EnemyMotionSegment>();
        public IReadOnlyList<EnemyMotionSegment> MotionSegments { get; }
        internal double MotionStartTime, MotionCursorTime, PreviousVelocityX, PreviousVelocityZ, StepStartX, StepStartZ;
        internal void BeginMotion(double startTime)
        { motionSegments.Clear(); MotionStartTime = MotionCursorTime = startTime; StepStartX = X; StepStartZ = Z; }
        internal void AddMotion(double oldX, double oldZ, double duration)
        {
            motionSegments.Add(new EnemyMotionSegment(MotionCursorTime, MotionCursorTime + duration, oldX, oldZ, X, Z));
            MotionCursorTime += duration;
        }
        internal void EndMotion(double endTime)
        {
            if (MotionCursorTime < endTime) motionSegments.Add(new EnemyMotionSegment(MotionCursorTime, endTime, X, Z, X, Z));
            double duration = endTime - MotionStartTime;
            PreviousVelocityX = duration > 0 ? (X - StepStartX) / duration : 0;
            PreviousVelocityZ = duration > 0 ? (Z - StepStartZ) / duration : 0;
        }
        internal void SampleMotion(double time, out double x, out double z)
        {
            foreach (var segment in motionSegments)
                if (time <= segment.EndTime) { segment.Sample(time, out x, out z); return; }
            x = X; z = Z;
        }
        public EnemyKind Kind { get; }
        public EnemyDefinition Definition { get; }
        public long FinalTargetBuildingId => TargetBuildingId;
        public long CurrentAttackBuildingId { get; internal set; }
        public EnemyAction CurrentAction { get; internal set; }
        public double Radius => Definition.Radius;
        internal EnemyRoute Route;
        internal int RouteCursor;
        internal long NextEvaluationTick;
        internal long RouteRevision = -1;
        internal long BlockedSinceTick = -1;
        internal long YieldUntilTick;
        internal long YieldForEnemyId;
        internal double YieldX, YieldZ;
        internal double TargetScore;
        public double AttackPositionX { get; internal set; }
        public double AttackPositionZ { get; internal set; }
        public double X { get; internal set; }
        public double Z { get; internal set; }
        public int Health { get; internal set; }
        public int MaximumHealth { get; }
        public long TargetBuildingId { get; internal set; }
        public EnemyTargetPurpose TargetPurpose { get; internal set; } = EnemyTargetPurpose.AdvanceToCore;
        public double TargetX { get; internal set; }
        public double TargetZ { get; internal set; }
        public long LastAttackTick { get; internal set; } = -1;
        public long LastAttackTargetBuildingId { get; internal set; }
        public double LastAttackTargetX { get; internal set; }
        public double LastAttackTargetZ { get; internal set; }
        public long LastBehaviorEvaluationTick { get; internal set; } = -1;
        public long TargetAssignedTick { get; internal set; } = -1;
        public long NextAttackTick { get; internal set; }
        public long SlowUntilTick { get; internal set; } = -1;
        public double MovementSpeedMultiplier { get; internal set; } = 1;
        public double CurrentMovementSpeed => Definition.Speed * MovementSpeedMultiplier;
        public bool IsAlive => Health > 0;
        internal int CellX;
        internal int CellZ;
        internal int NextCellX;
        internal int NextCellZ;

        internal EnemyState(long id, SpawnPoint spawn, int health, EnemyKind kind = EnemyKind.Normal, EnemyDefinition definition = null, bool exactHealth = false)
        {
            MotionSegments = motionSegments.AsReadOnly();
            Id = id;
            Kind = kind;
            Definition = definition ?? EnemyDefinition.Default(kind);
            CellX = NextCellX = spawn.X;
            CellZ = NextCellZ = spawn.Z;
            X = spawn.X + 0.5;
            Z = spawn.Z + 0.5;
            Health = MaximumHealth = exactHealth ? health : Math.Max(1, (int)Math.Min(int.MaxValue, (long)health * Definition.BaseHealth / 60));
        }
    }
}
