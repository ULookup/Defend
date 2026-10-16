using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Defend.Simulation;
using UnityEngine;

namespace Defend.Unity
{
    /// <summary>仅保存沙盒布局与参数。解析及整体验证在候选世界中完成，成功后由会话一次替换。</summary>
    public sealed class SandboxPresetStore
    {
        private const int CurrentVersion = 2;
        private const string MapIdentifier = "IndustrialDefense";
        private readonly string directory;
        public SandboxPresetStore(string directory) { this.directory = directory; }
        [Serializable] private sealed class Preset
        {
            public int Version;
            public string Name, Map;
            public int Width, Depth, TicksPerSecond, MaximumLivingEnemies;
            public SandboxBuildingParameters[] Parameters;
            public Layout[] Buildings;
            public Entrance[] SpawnPoints;
            public Group[] Groups;
        }
        [Serializable] private sealed class Layout
        {
            public BuildingKind Kind;
            public int X, Z, Priority, OrientationQuarterTurns, FacingDegrees;
        }
        [Serializable] private sealed class Entrance { public long Id; public int X, Z; public string Name; }
        [Serializable] private sealed class Allocation { public long SpawnPointId; public int Count; }
        [Serializable] private sealed class Group
        {
            public long Id, StartTick;
            public EnemyKind Kind;
            public int Health, Damage, AttackIntervalTicks, SpawnIntervalTicks;
            public double Speed;
            public Allocation[] Allocations;
        }
        public string[] List()
        {
            if (!Directory.Exists(directory)) return Array.Empty<string>();
            var result = new List<string>();
            foreach (string path in Directory.GetFiles(directory, "*.json")) result.Add(Path.GetFileNameWithoutExtension(path));
            result.Sort(StringComparer.CurrentCulture); return result.ToArray();
        }
        public void Save(string name, GameWorld world)
        {
            if (!world.IsSandbox || !world.CanEditBuildings) throw new InvalidOperationException("只能在沙盒准备阶段保存方案。");
            string path = FilePath(name);
            var parameters = new List<SandboxBuildingParameters>();
            foreach (BuildingKind kind in Enum.GetValues(typeof(BuildingKind))) parameters.Add(world.Sandbox.GetBuildingParameters(kind));
            var layout = new List<Layout>();
            foreach (var building in world.Buildings)
                if (building.IsAlive && !building.IsSold) layout.Add(new Layout
                {
                    Kind = building.Kind, X = building.X, Z = building.Z, Priority = building.PriorityWeight,
                    OrientationQuarterTurns = building.OrientationQuarterTurns, FacingDegrees = building.FacingDegrees
                });
            var entrances = new List<Entrance>();
            foreach (var point in world.Battle.SandboxSpawnPoints) entrances.Add(new Entrance { Id = point.Id, X = point.X, Z = point.Z, Name = point.Name });
            var groups = new List<Group>();
            foreach (var group in world.Battle.SandboxGroups)
            {
                var allocations = new List<Allocation>();
                foreach (var value in group.Allocations) allocations.Add(new Allocation { SpawnPointId = value.SpawnPointId, Count = value.Count });
                groups.Add(new Group { Id = group.Id, Kind = group.Kind, Health = group.Health, Damage = group.Damage, Speed = group.Speed,
                    AttackIntervalTicks = group.AttackIntervalTicks, StartTick = group.StartTick, SpawnIntervalTicks = group.SpawnIntervalTicks, Allocations = allocations.ToArray() });
            }
            var preset = new Preset { Version = CurrentVersion, Name = name, Map = MapIdentifier, Width = world.Settings.Width,
                Depth = world.Settings.Depth, TicksPerSecond = world.Settings.TicksPerSecond, MaximumLivingEnemies = world.Battle.SandboxMaximumLivingEnemies,
                Parameters = parameters.ToArray(), Buildings = layout.ToArray(), SpawnPoints = entrances.ToArray(), Groups = groups.ToArray() };
            CreateCandidate(preset, world); // 不把缺失出生点引用等无效草稿写成可载入方案。
            Directory.CreateDirectory(directory);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonUtility.ToJson(preset, true), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        public GameWorld Load(string name, GameWorld world)
        {
            if (!world.IsSandbox || !world.CanEditBuildings) throw new InvalidOperationException("只能在沙盒准备阶段载入方案。");
            string path = FilePath(name);
            if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("方案文件超过 4 MB 的允许大小。");
            var preset = JsonUtility.FromJson<Preset>(File.ReadAllText(path, Encoding.UTF8));
            return CreateCandidate(preset, world);
        }
        private static GameWorld CreateCandidate(Preset preset, GameWorld current)
        {
            if (preset == null || preset.Version < 1 || preset.Version > CurrentVersion) throw new InvalidDataException("方案格式版本不受支持。");
            if (preset.Map != MapIdentifier || preset.Width != current.Settings.Width || preset.Depth != current.Settings.Depth || preset.TicksPerSecond != current.Settings.TicksPerSecond)
                throw new InvalidDataException("方案地图或逻辑步频不匹配。");
            int kindCount = Enum.GetValues(typeof(BuildingKind)).Length;
            if (preset.Parameters == null || preset.Parameters.Length > kindCount || preset.Buildings == null || preset.Buildings.Length > preset.Width * preset.Depth ||
                preset.SpawnPoints == null || preset.SpawnPoints.Length > BattleSystem.SandboxMaximumSpawnPoints || preset.Groups == null || preset.Groups.Length > BattleSystem.SandboxMaximumGroups)
                throw new InvalidDataException("方案内容缺失或超出允许数量。");
            var candidate = current.CreateEmptySandboxWorld();
            candidate.Battle.ReplaceSandboxSpawnPoints(Array.Empty<SandboxSpawnPoint>(), out _);
            var kinds = new HashSet<BuildingKind>();
            foreach (var parameters in preset.Parameters)
            {
                if (parameters == null || !Enum.IsDefined(typeof(BuildingKind), parameters.Kind) || !kinds.Add(parameters.Kind)) throw new InvalidDataException("建筑参数类型无效或重复。");
                if (!candidate.Sandbox.ApplyBuildingParameters(parameters, candidate.Revision, out string error)) throw new InvalidDataException(error);
            }
            // 早期方案没有后来加入的建筑参数。只为明确新增的类型补默认，旧类型缺失仍视为无效内容。
            foreach (BuildingKind kind in Enum.GetValues(typeof(BuildingKind)))
            {
                if (kinds.Contains(kind)) continue;
                if (kind != BuildingKind.PiercingRailgun && kind != BuildingKind.ResonanceMagneticTower)
                    throw new InvalidDataException("方案缺少建筑类型“" + candidate.GetDefinition(kind).DisplayName + "”的参数。");
                var defaults = candidate.Sandbox.GetBuildingParameters(kind, true);
                if (!candidate.Sandbox.ApplyBuildingParameters(defaults, candidate.Revision, out string error)) throw new InvalidDataException(error);
            }
            int coreCount = 0;
            foreach (var building in preset.Buildings)
            {
                if (building == null || !Enum.IsDefined(typeof(BuildingKind), building.Kind) || building.Priority < 1 || building.Priority > 100) throw new InvalidDataException("建筑或优先级无效。");
                // 第一版方案没有朝向字段，按当时的默认方向恢复；新版本拒绝损坏的方向数据。
                int orientation = preset.Version == 1 ? 0 : building.OrientationQuarterTurns;
                int facing = preset.Version == 1 ? 0 : building.FacingDegrees;
                if (orientation < 0 || orientation > 3 || facing < 0 || facing >= 360)
                    throw new InvalidDataException("建筑朝向或主要射击方向无效。");
                if (building.Kind == BuildingKind.Core) coreCount++;
                var result = candidate.Submit(WorldCommand.Place(building.Kind, building.X, building.Z, orientation, facing));
                if (!result.Succeeded)
                {
                    BuildingDefinition definition = candidate.GetDefinition(building.Kind);
                    throw new InvalidDataException("建筑“" + definition.DisplayName + "”在网格坐标 (" + building.X + ", " + building.Z + ") 无法放置，当前占地 " + definition.GetFootprintWidth(orientation) + "×" + definition.GetFootprintDepth(orientation) + "：" + result.Message);
                }
                var state = candidate.FindBuilding(result.BuildingId);
                if (state.IsEnergyDestination)
                {
                    result = candidate.Submit(WorldCommand.SetPriority(state.Id, building.Priority));
                    if (!result.Succeeded) throw new InvalidDataException(result.Message);
                }
            }
            if (coreCount != 1) throw new InvalidDataException("方案必须包含一座存活核心。");
            var points = new List<SandboxSpawnPoint>();
            foreach (var point in preset.SpawnPoints)
            {
                if (point == null || (point.Name != null && point.Name.Length > 60)) throw new InvalidDataException("出生点内容无效。");
                points.Add(new SandboxSpawnPoint(point.Id, point.X, point.Z, point.Name));
            }
            if (!candidate.Battle.ReplaceSandboxSpawnPoints(points, out string pointError)) throw new InvalidDataException(pointError);
            var groups = new List<SandboxEnemyGroup>();
            foreach (var group in preset.Groups)
            {
                if (group == null || group.Allocations == null || group.Allocations.Length > BattleSystem.SandboxMaximumSpawnPoints) throw new InvalidDataException("敌人队伍内容无效。");
                var allocations = new List<SandboxSpawnAllocation>();
                foreach (var allocation in group.Allocations)
                {
                    if (allocation == null) throw new InvalidDataException("出生点分配内容缺失。");
                    allocations.Add(new SandboxSpawnAllocation(allocation.SpawnPointId, allocation.Count));
                }
                groups.Add(new SandboxEnemyGroup(group.Id, group.Kind, group.Health, group.Speed, group.Damage, group.AttackIntervalTicks,
                    group.StartTick, group.SpawnIntervalTicks, allocations));
            }
            if (!candidate.Battle.ConfigureSandboxWave(groups, preset.MaximumLivingEnemies, out string waveError)) throw new InvalidDataException(waveError);
            return candidate;
        }
        private string FilePath(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name != name.Trim() || name.Length > 60 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name == "." || name == "..")
                throw new ArgumentException("方案名称需为 1 至 60 个字符，不能包含文件路径字符或首尾空格。");
            return Path.Combine(directory, name + ".json");
        }
    }
}
