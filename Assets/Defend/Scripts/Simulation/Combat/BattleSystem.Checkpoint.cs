using System;
using System.Collections.Generic;

namespace Defend.Simulation
{
    /// <summary>仅准备阶段的显式战斗记录；含真实观察历史、顺序游标、炮口姿态和身份序列。</summary>
    public sealed class BattleCheckpoint
    {
        internal long NextEnemyId, NextShotId, NextProjectileId, NextSpawnTick, WaveStartTick, SpawnDelay;
        internal long NextSpawnPointId, CoreBuildingId, CombatTicks;
        internal int SpawnCursor, GroupIndex, GroupSpawned, LossesAtWaveStart, WaveNumber, RemainingToSpawn, Kills;
        internal int LastWaveReward, PendingKillReward, LastWaveFixedReward, LastWaveKillReward, LastWaveBuildingLosses;
        internal int MaximumLivingEnemies, SpawnedCount, KillsAtWaveStart;
        internal bool ManuallyStopped;
        internal SandboxWavePlan Plan;
        internal EnemyBehaviorSystem.Checkpoint Behavior;
        internal readonly List<SandboxSpawnPoint> SpawnPoints = new List<SandboxSpawnPoint>();
        internal readonly List<SandboxEnemyGroup> Groups = new List<SandboxEnemyGroup>();
        internal readonly List<AimRecord> Aims = new List<AimRecord>();
        internal readonly struct AimRecord
        {
            internal readonly long BuildingId;
            internal readonly double Yaw, Pitch, PreviousYaw, PreviousPitch, MuzzleX, MuzzleY, MuzzleZ, TargetX, TargetY, TargetZ;
            internal AimRecord(TurretAimState aim)
            {
                BuildingId = aim.Turret.Id; Yaw = aim.Yaw; Pitch = aim.Pitch; PreviousYaw = aim.PreviousYaw; PreviousPitch = aim.PreviousPitch;
                MuzzleX = aim.MuzzleX; MuzzleY = aim.MuzzleY; MuzzleZ = aim.MuzzleZ; TargetX = aim.TargetX; TargetY = aim.TargetY; TargetZ = aim.TargetZ;
            }
            internal TurretAimState Restore(BuildingState building) => new TurretAimState {
                Turret = building, Yaw = Yaw, Pitch = Pitch, PreviousYaw = PreviousYaw, PreviousPitch = PreviousPitch,
                MuzzleX = MuzzleX, MuzzleY = MuzzleY, MuzzleZ = MuzzleZ, TargetX = TargetX, TargetY = TargetY, TargetZ = TargetZ };
        }
    }

    public sealed partial class BattleSystem
    {
        public BattleCheckpoint CaptureCheckpoint()
        {
            if (!world.IsSandbox || State != BattleState.Preparing || enemies.Count != 0)
                throw new InvalidOperationException("只能记录沙盒准备阶段的开战前状态。");
            var state = new BattleCheckpoint {
                NextEnemyId = nextEnemyId, NextShotId = nextShotId, NextProjectileId = projectiles.CaptureNextIdentifier(),
                NextSpawnTick = nextSpawnTick, WaveStartTick = waveStartTick, SpawnDelay = spawnDelay,
                NextSpawnPointId = nextSandboxSpawnPointId, CoreBuildingId = CoreBuildingId, CombatTicks = CombatTicks,
                SpawnCursor = spawnCursor, GroupIndex = groupIndex, GroupSpawned = groupSpawned, LossesAtWaveStart = lossesAtWaveStart,
                WaveNumber = WaveNumber, RemainingToSpawn = RemainingToSpawn, Kills = Kills, LastWaveReward = LastWaveReward,
                PendingKillReward = PendingKillReward, LastWaveFixedReward = LastWaveFixedReward, LastWaveKillReward = LastWaveKillReward,
                LastWaveBuildingLosses = LastWaveBuildingLosses, MaximumLivingEnemies = SandboxMaximumLivingEnemies,
                SpawnedCount = SandboxSpawnedCount, ManuallyStopped = LastWaveWasManuallyStopped, Plan = CurrentSandboxPlan,
                KillsAtWaveStart = sandboxKillsAtWaveStart,
                Behavior = behavior.CaptureCheckpoint()
            };
            state.SpawnPoints.AddRange(sandboxSpawnPoints); state.Groups.AddRange(sandboxGroups);
            foreach (var aim in turretAims.Values) state.Aims.Add(new BattleCheckpoint.AimRecord(aim));
            return state;
        }

        public void RestoreCheckpoint(BattleCheckpoint state)
        {
            if (state == null || !world.IsSandbox) throw new ArgumentException("沙盒开战前记录无效。", nameof(state));
            if (State == BattleState.Combat) throw new InvalidOperationException("请先提前结束本波，再恢复开战前状态。");
            nextEnemyId = state.NextEnemyId; nextShotId = state.NextShotId; projectiles.RestoreNextIdentifier(state.NextProjectileId);
            nextSpawnTick = state.NextSpawnTick; waveStartTick = state.WaveStartTick; spawnDelay = state.SpawnDelay;
            nextSandboxSpawnPointId = state.NextSpawnPointId; CoreBuildingId = state.CoreBuildingId; CombatTicks = state.CombatTicks;
            spawnCursor = state.SpawnCursor; groupIndex = state.GroupIndex; groupSpawned = state.GroupSpawned; lossesAtWaveStart = state.LossesAtWaveStart;
            WaveNumber = state.WaveNumber; RemainingToSpawn = state.RemainingToSpawn; Kills = state.Kills;
            LastWaveReward = state.LastWaveReward; PendingKillReward = state.PendingKillReward; LastWaveFixedReward = state.LastWaveFixedReward;
            LastWaveKillReward = state.LastWaveKillReward; LastWaveBuildingLosses = state.LastWaveBuildingLosses;
            SandboxMaximumLivingEnemies = state.MaximumLivingEnemies; SandboxSpawnedCount = state.SpawnedCount;
            sandboxKillsAtWaveStart = state.KillsAtWaveStart;
            LastWaveWasManuallyStopped = state.ManuallyStopped; CurrentSandboxPlan = state.Plan;
            sandboxSpawnPoints.Clear(); sandboxSpawnPoints.AddRange(state.SpawnPoints); UpdateSandboxSpawnPositions();
            sandboxGroups.Clear(); sandboxGroups.AddRange(state.Groups); sandboxSchedules.Clear(); sandboxDue.Clear();
            SandboxSpawnBlockedReason = ""; enemies.Clear(); shots.Clear(); aimSnapshots.Clear(); turretAims.Clear();
            foreach (var record in state.Aims)
            {
                var building = world.FindBuilding(record.BuildingId);
                if (building != null) turretAims.Add(record.BuildingId, record.Restore(building));
            }
            behavior.RestoreCheckpoint(state.Behavior); State = BattleState.Preparing;
        }
    }
}
