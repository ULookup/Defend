using System;
using System.Collections.Generic;

namespace Defend.Simulation
{
    public sealed partial class BattleSystem
    {
        public const int SandboxMaximumGroups = 64;
        public const int SandboxMaximumSpawnPoints = 64;
        public const int SandboxMaximumEnemyCount = 10000;
        public const int SandboxLivingEnemyLimit = 1000;
        public const int SandboxMaximumHealth = 1000000;
        public const double SandboxMaximumSpeed = 20;
        public const int SandboxMaximumDurationSeconds = 3600;
        private readonly List<SandboxSpawnPoint> sandboxSpawnPoints = new List<SandboxSpawnPoint>();
        private readonly List<SandboxEnemyGroup> sandboxGroups = new List<SandboxEnemyGroup>();
        private readonly List<SandboxGroupSchedule> sandboxSchedules = new List<SandboxGroupSchedule>();
        private readonly List<SandboxGroupSchedule> sandboxDue = new List<SandboxGroupSchedule>();
        private readonly Dictionary<long, (long revision, string warning)> sandboxSpawnWarnings = new Dictionary<long, (long, string)>();
        private long nextSandboxSpawnPointId = 1;
        private int sandboxKillsAtWaveStart;
        public IReadOnlyList<SandboxSpawnPoint> SandboxSpawnPoints { get; private set; }
        public IReadOnlyList<SandboxEnemyGroup> SandboxGroups { get; private set; }
        public int SandboxMaximumLivingEnemies { get; private set; } = 200;
        public SandboxWavePlan CurrentSandboxPlan { get; private set; }
        public string SandboxSpawnBlockedReason { get; private set; } = "";
        public int SandboxSpawnedCount { get; private set; }
        public int SandboxKilledCount => Kills - sandboxKillsAtWaveStart;
        public bool LastWaveWasManuallyStopped { get; private set; }

        private sealed class SandboxGroupSchedule
        {
            internal SandboxEnemyGroup Group;
            internal EnemyDefinition Definition;
            internal SpawnPoint[] Points;
            internal int[] Remaining;
            internal int Cursor, Order, TotalRemaining;
            internal long NextTick;
        }

        private void InitializeSandbox()
        {
            SandboxSpawnPoints = sandboxSpawnPoints.AsReadOnly(); SandboxGroups = sandboxGroups.AsReadOnly();
            foreach (var point in SpawnPoints) sandboxSpawnPoints.Add(new SandboxSpawnPoint(nextSandboxSpawnPointId++, point.X, point.Z));
            SandboxMaximumLivingEnemies = Math.Min(SandboxLivingEnemyLimit, Settings.MaximumLivingEnemies);
            if (sandboxSpawnPoints.Count > 0)
            {
                var definition = Settings.GetEnemy(EnemyKind.Normal);
                var ids = new List<long>(); foreach (var point in sandboxSpawnPoints) ids.Add(point.Id);
                sandboxGroups.Add(new SandboxEnemyGroup(1, definition.Kind, definition.BaseHealth, definition.Speed,
                    definition.AttackDamage, definition.AttackIntervalTicks, 0, world.Settings.TicksPerSecond,
                    SandboxEnemyGroup.DivideEvenly(10, ids)));
            }
            // 沙盒使用可编辑的单格出生点，初始化与修改、恢复时采用相同的占地规则。
            UpdateSandboxSpawnPositions();
        }

        private bool CanEditSandbox(out string error)
        {
            error = !world.IsSandbox ? "此操作仅适用于沙盒模式。" : State != BattleState.Preparing ? "仅准备阶段可以修改沙盒配置。" : null;
            return error == null;
        }

        public string ValidateSandboxSpawnPoint(int x, int z, long ignoredId = 0)
        {
            if (!CanEditSandbox(out string error)) return error;
            if (x < 0 || z < 0 || x >= grid.Width || z >= grid.Depth) return "出生点必须位于地图内。";
            long occupied = grid.Find(x, z);
            if (occupied < 0) return "固定障碍处不能设置出生点。";
            if (occupied > 0) return "建筑占地内不能设置出生点。";
            foreach (var point in sandboxSpawnPoints)
                if (point.Id != ignoredId && point.X == x && point.Z == z) return "该格已有出生点。";
            return null;
        }

        public bool AddSandboxSpawnPoint(int x, int z, out string error)
        {
            error = ValidateSandboxSpawnPoint(x, z);
            if (error != null) return false;
            if (sandboxSpawnPoints.Count >= SandboxMaximumSpawnPoints) { error = "最多设置 64 个出生点。"; return false; }
            if (nextSandboxSpawnPointId >= long.MaxValue) { error = "出生点身份已用尽，请重新开始沙盒。"; return false; }
            sandboxSpawnPoints.Add(new SandboxSpawnPoint(nextSandboxSpawnPointId++, x, z));
            UpdateSandboxSpawnPositions(); world.MarkSandboxChanged(); return true;
        }

        public bool MoveSandboxSpawnPoint(long id, int x, int z, out string error)
        {
            error = ValidateSandboxSpawnPoint(x, z, id); if (error != null) return false;
            int index = sandboxSpawnPoints.FindIndex(point => point.Id == id);
            if (index < 0) { error = "出生点已不存在。"; return false; }
            sandboxSpawnPoints[index] = new SandboxSpawnPoint(id, x, z, sandboxSpawnPoints[index].Name);
            UpdateSandboxSpawnPositions(); world.MarkSandboxChanged(); return true;
        }

        public bool DeleteSandboxSpawnPoint(long id, out string error)
        {
            if (!CanEditSandbox(out error)) return false;
            int index = sandboxSpawnPoints.FindIndex(point => point.Id == id);
            if (index < 0) { error = "出生点已不存在。"; return false; }
            sandboxSpawnPoints.RemoveAt(index); // 保留所有队伍中的身份引用，开波校验要求明确重新指定。
            UpdateSandboxSpawnPositions(); world.MarkSandboxChanged(); return true;
        }

        public bool ReplaceSandboxSpawnPoints(IEnumerable<SandboxSpawnPoint> points, out string error)
        {
            if (!CanEditSandbox(out error)) return false;
            if (points == null) { error = "出生点列表无效。"; return false; }
            var values = new List<SandboxSpawnPoint>(); var ids = new HashSet<long>(); var cells = new HashSet<(int, int)>();
            long next = nextSandboxSpawnPointId;
            foreach (var point in points)
            {
                if (point == null || point.Id <= 0 || point.Id >= long.MaxValue || !ids.Add(point.Id) || !cells.Add((point.X, point.Z)))
                { error = "出生点身份或位置重复，或身份无效。"; return false; }
                if (point.X < 0 || point.Z < 0 || point.X >= grid.Width || point.Z >= grid.Depth || grid.Find(point.X, point.Z) != 0)
                { error = "出生点越界或被固定障碍、建筑占用。"; return false; }
                values.Add(point); next = Math.Max(next, point.Id + 1);
                if (values.Count > SandboxMaximumSpawnPoints) { error = "最多设置 64 个出生点。"; return false; }
            }
            sandboxSpawnPoints.Clear(); sandboxSpawnPoints.AddRange(values); nextSandboxSpawnPointId = next;
            UpdateSandboxSpawnPositions(); world.MarkSandboxChanged(); return true;
        }

        private void UpdateSandboxSpawnPositions()
        {
            sandboxSpawnWarnings.Clear();
            var points = new List<SpawnPoint>();
            foreach (var point in sandboxSpawnPoints) points.Add(new SpawnPoint(point.X, point.Z));
            SpawnPoints = points.AsReadOnly();
        }

        public bool ConfigureSandboxWave(IEnumerable<SandboxEnemyGroup> groups, int maximumLivingEnemies, out string error)
        {
            if (!CanEditSandbox(out error)) return false;
            if (groups == null) { error = "队伍列表无效。"; return false; }
            var values = new List<SandboxEnemyGroup>();
            foreach (var group in groups)
            {
                values.Add(group);
                if (values.Count > SandboxMaximumGroups) { error = "最多配置 64 支队伍。"; return false; }
            }
            if (!ValidateSandboxGroups(values, maximumLivingEnemies, false, out error)) return false;
            sandboxGroups.Clear(); sandboxGroups.AddRange(values); SandboxMaximumLivingEnemies = maximumLivingEnemies;
            world.MarkSandboxChanged(); return true;
        }

        public bool ValidateSandboxWave(out string error)
        {
            if (!CanEditSandbox(out error)) return false;
            var core = world.FindBuilding(CoreBuildingId);
            if (core == null || !core.IsAlive || core.IsSold) { error = "需要一座完好的核心才能开始战斗。"; return false; }
            return ValidateSandboxGroups(sandboxGroups, SandboxMaximumLivingEnemies, true, out error);
        }

        private bool ValidateSandboxGroups(IReadOnlyList<SandboxEnemyGroup> groups, int limit, bool requireEnemies, out string error)
        {
            error = null;
            if (limit < 1 || limit > SandboxLivingEnemyLimit) { error = "同时在场上限须为 1 至 1000。"; return false; }
            if (groups.Count > SandboxMaximumGroups || requireEnemies && groups.Count == 0)
            { error = "开波需要 1 至 64 支有效队伍。"; return false; }
            long total = 0, maximumTicks = (long)SandboxMaximumDurationSeconds * world.Settings.TicksPerSecond;
            var ids = new HashSet<long>();
            foreach (var group in groups)
            {
                if (group == null || group.Id <= 0 || !ids.Add(group.Id)) { error = "队伍身份无效或重复。"; return false; }
                if (!Enum.IsDefined(typeof(EnemyKind), group.Kind) || group.Health < 1 || group.Health > SandboxMaximumHealth || group.Damage < 1 || group.Damage > SandboxMaximumHealth)
                { error = "敌人类型无效；生命和攻击伤害须为 1 至 1000000。"; return false; }
                if (double.IsNaN(group.Speed) || double.IsInfinity(group.Speed) || group.Speed <= 0 || group.Speed > SandboxMaximumSpeed)
                { error = "移动速度须大于 0 且不超过 20 格/秒。"; return false; }
                if (group.AttackIntervalTicks < 1 || group.AttackIntervalTicks > maximumTicks || group.SpawnIntervalTicks < 1 || group.SpawnIntervalTicks > maximumTicks || group.StartTick < 0 || group.StartTick > maximumTicks)
                { error = "开始时间须为 0 至 3600 秒；攻击与出生间隔须为一个逻辑步至 3600 秒。"; return false; }
                if (group.Allocations.Count > SandboxMaximumSpawnPoints) { error = "每支队伍最多分配 64 个出生点。"; return false; }
                var assigned = new HashSet<long>(); long count = 0;
                foreach (var allocation in group.Allocations)
                {
                    if (allocation.Count < 0 || allocation.Count > SandboxMaximumEnemyCount || !assigned.Add(allocation.SpawnPointId))
                    { error = "出生点分配不能重复；数量须为 0 至 10000。"; return false; }
                    var point = sandboxSpawnPoints.Find(value => value.Id == allocation.SpawnPointId);
                    if (point == null) { error = "队伍存在未指定出生点，请重新指定后应用。"; return false; }
                    if (allocation.Count > 0 && !movement.CanFitSpawn(new SpawnPoint(point.X, point.Z), Settings.GetEnemy(group.Kind)))
                    { error = point.Name + " 无法容纳该类型敌人的体积。"; return false; }
                    count += allocation.Count;
                }
                if (count < 1 || count > SandboxMaximumEnemyCount) { error = "每支队伍数量须为 1 至 10000。"; return false; }
                total += count;
                if (total > SandboxMaximumEnemyCount) { error = "本波总数不能超过 10000。"; return false; }
            }
            return true;
        }

        public string GetSandboxSpawnWarning(long id)
        {
            var point = sandboxSpawnPoints.Find(value => value.Id == id);
            if (point == null) return "未指定出生点";
            if (sandboxSpawnWarnings.TryGetValue(id, out var cached) && cached.revision == grid.Revision) return cached.warning;
            // 仅作提示：检查整个空地连通区域，外围围墙也应被发现；不以此拒绝启动。
            var pending = new Queue<SpawnPoint>(); var visited = new HashSet<(int, int)>();
            pending.Enqueue(new SpawnPoint(point.X, point.Z)); visited.Add((point.X, point.Z));
            bool open = false;
            while (pending.Count > 0 && !open)
            {
                var cell = pending.Dequeue();
                if (cell.X == 0 || cell.Z == 0 || cell.X == grid.Width - 1 || cell.Z == grid.Depth - 1) { open = true; break; }
                Enqueue(cell.X - 1, cell.Z); Enqueue(cell.X + 1, cell.Z); Enqueue(cell.X, cell.Z - 1); Enqueue(cell.X, cell.Z + 1);
            }
            string warning = open ? "" : "出生点所在区域被建筑或固定障碍包围；允许开波，敌人将沿用原有绕路与拆除行为。";
            sandboxSpawnWarnings[id] = (grid.Revision, warning); return warning;

            void Enqueue(int x, int z)
            {
                if (grid.Find(x, z) == 0 && visited.Add((x, z))) pending.Enqueue(new SpawnPoint(x, z));
            }
        }

        private CommandResult StartSandboxWave(long sequence)
        {
            if (!ValidateSandboxWave(out string error)) return new CommandResult(sequence, false, error, 0);
            CurrentSandboxPlan = new SandboxWavePlan(sandboxGroups, sandboxSpawnPoints, SandboxMaximumLivingEnemies);
            sandboxSchedules.Clear(); sandboxDue.Clear();
            waveStartTick = world.CurrentTick;
            for (int order = 0; order < CurrentSandboxPlan.Groups.Count; order++)
            {
                var group = CurrentSandboxPlan.Groups[order]; var original = Settings.GetEnemy(group.Kind);
                var schedule = new SandboxGroupSchedule { Group = group, Order = order, TotalRemaining = group.Count,
                    NextTick = checked(waveStartTick + group.StartTick), Points = new SpawnPoint[group.Allocations.Count], Remaining = new int[group.Allocations.Count],
                    Definition = new EnemyDefinition(group.Kind, original.DisplayName, original.Description, group.Health,
                        original.Radius, group.Speed, group.Damage, group.AttackIntervalTicks, original.AttackRange,
                        original.KillReward, original.RelayPreference, original.ReactorPreference, original.RiskWeight, original.BodyHeight) };
                for (int index = 0; index < group.Allocations.Count; index++)
                {
                    var allocation = group.Allocations[index]; var point = sandboxSpawnPoints.Find(value => value.Id == allocation.SpawnPointId);
                    schedule.Points[index] = new SpawnPoint(point.X, point.Z); schedule.Remaining[index] = allocation.Count;
                }
                sandboxSchedules.Add(schedule);
            }
            WaveNumber++; RemainingToSpawn = CurrentSandboxPlan.EnemyCount; SandboxSpawnedCount = 0;
            sandboxKillsAtWaveStart = Kills;
            LastWaveReward = LastWaveFixedReward = LastWaveKillReward = PendingKillReward = 0;
            lossesAtWaveStart = world.DestroyedBuildingCount; LastWaveWasManuallyStopped = false; SandboxSpawnBlockedReason = "";
            State = BattleState.Combat;
            return new CommandResult(sequence, true, "第 " + WaveNumber + " 波沙盒试验开始。", CoreBuildingId);
        }

        private void CompleteSandboxStep(long tick)
        {
            sandboxDue.Clear(); SandboxSpawnBlockedReason = "";
            foreach (var schedule in sandboxSchedules)
                if (schedule.TotalRemaining > 0 && tick >= schedule.NextTick) sandboxDue.Add(schedule);
            sandboxDue.Sort((first, second) => first.NextTick != second.NextTick ? first.NextTick.CompareTo(second.NextTick) : first.Order.CompareTo(second.Order));
            foreach (var schedule in sandboxDue)
            {
                if (enemies.Count >= CurrentSandboxPlan.MaximumLivingEnemies)
                { SandboxSpawnBlockedReason = "已达到同时在场上限，剩余敌人等待。"; break; }
                while (schedule.Remaining[schedule.Cursor] == 0) schedule.Cursor = (schedule.Cursor + 1) % schedule.Remaining.Length;
                var spawn = schedule.Points[schedule.Cursor];
                if (!movement.TrySpawnExact(spawn, schedule.Definition, out double x, out double z))
                { SandboxSpawnBlockedReason = "队伍 " + (schedule.Order + 1) + " 的出生位置被占用，等待空位。"; continue; }
                enemies.Add(new EnemyState(nextEnemyId++, spawn, schedule.Group.Health, schedule.Group.Kind, schedule.Definition, true)
                    { X = x, Z = z, CellX = (int)x, CellZ = (int)z });
                schedule.Remaining[schedule.Cursor]--; schedule.TotalRemaining--; RemainingToSpawn--; SandboxSpawnedCount++;
                schedule.Cursor = (schedule.Cursor + 1) % schedule.Remaining.Length;
                schedule.NextTick = checked(tick + schedule.Group.SpawnIntervalTicks);
            }
            if (RemainingToSpawn == 0 && enemies.Count == 0) FinishSandboxWave(false);
        }

        public CommandResult StopSandboxWave(long sequence = 0)
        {
            if (!world.IsSandbox || State != BattleState.Combat) return new CommandResult(sequence, false, "仅沙盒战斗中可以提前结束本波。", 0);
            enemies.Clear(); RemainingToSpawn = 0; sandboxSchedules.Clear(); FinishSandboxWave(true);
            world.ClearSandboxBattleEvents(); world.MarkSandboxChanged();
            return new CommandResult(sequence, true, "本波已手动终止，建筑战损与真实能源状态保留。", CoreBuildingId);
        }

        private void FinishSandboxWave(bool manuallyStopped)
        {
            State = BattleState.WaveCleared; LastWaveWasManuallyStopped = manuallyStopped;
            CancelAllCharges();
            LastWaveReward = LastWaveFixedReward = LastWaveKillReward = PendingKillReward = 0;
            LastWaveBuildingLosses = world.DestroyedBuildingCount - lossesAtWaveStart;
            SandboxSpawnBlockedReason = ""; projectiles.Clear((double)world.CurrentTick / world.Settings.TicksPerSecond);
            if (manuallyStopped) { shots.Clear(); aimSnapshots.Clear(); foreach (var aim in turretAims.Values) { aim.Target = null; aim.Aligned = false; } }
        }

        internal void OnTurretDefinitionChanged(BuildingKind kind, TurretDefinition previous, TurretDefinition current)
        {
            if (previous.CooldownTicks == current.CooldownTicks) return;
            foreach (var building in world.Buildings)
                if (building.Kind == kind && building.IsAlive && !building.IsSold && building.LastShotTick >= 0)
                    building.NextShotTick = Math.Max(world.CurrentTick + 1, building.LastShotTick + current.CooldownTicks);
        }
    }
}
