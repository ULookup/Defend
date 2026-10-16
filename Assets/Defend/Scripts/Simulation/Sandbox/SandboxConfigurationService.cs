namespace Defend.Simulation
{
    /// <summary>准备阶段的类型参数事务。候选值先完整校验，版本匹配后才更新真实世界。</summary>
    public sealed class SandboxConfigurationService
    {
        private readonly GameWorld world;
        internal SandboxConfigurationService(GameWorld world) { this.world = world; }
        public SandboxBuildingParameters GetBuildingParameters(BuildingKind kind, bool defaults = false)
        {
            var building = defaults ? world.GetDefaultDefinition(kind) : world.GetDefinition(kind);
            bool turret = kind == BuildingKind.Turret || kind == BuildingKind.BurstTurret || kind == BuildingKind.AreaTurret || kind == BuildingKind.PiercingRailgun;
            var weapon = turret ? (defaults ? world.GetDefaultTurretDefinition(kind) : world.GetTurretDefinition(kind)) : null;
            return SandboxBuildingParameters.FromDefinitions(building, weapon, world.Settings.TicksPerSecond);
        }
        public bool PreviewBuildingParameters(SandboxBuildingParameters parameters, out EnergyDefinitionChangePreview preview, out string error)
        {
            preview = default;
            if (!CanEdit(out error)) return false;
            if (!CreateCandidate(parameters, out var building, out _, out error)) return false;
            preview = world.Energy.PreviewDefinitionChange(building);
            return true;
        }
        public bool ApplyBuildingParameters(SandboxBuildingParameters parameters, long expectedRevision, out string error)
        {
            if (!CanEdit(out error)) return false;
            if (world.Revision != expectedRevision) { error = "世界状态已经变化，请重新预览后应用。"; return false; }
            if (!CreateCandidate(parameters, out var building, out var weapon, out error)) return false;
            world.Energy.ApplyDefinitionChange(building, world.CurrentTick);
            world.ReplaceSandboxDefinition(building, weapon);
            return true;
        }
        private bool CreateCandidate(SandboxBuildingParameters parameters, out BuildingDefinition building, out TurretDefinition weapon, out string error)
        {
            building = null; weapon = null; error = null;
            if (parameters == null) { error = "建筑参数不能为空。"; return false; }
            if (!System.Enum.IsDefined(typeof(BuildingKind), parameters.Kind)) { error = "建筑类型无效。"; return false; }
            bool turret = parameters.Kind == BuildingKind.Turret || parameters.Kind == BuildingKind.BurstTurret || parameters.Kind == BuildingKind.AreaTurret || parameters.Kind == BuildingKind.PiercingRailgun;
            return parameters.TryCreateDefinitions(world.GetDefaultDefinition(parameters.Kind), turret ? world.GetDefaultTurretDefinition(parameters.Kind) : null,
                world.Settings.TicksPerSecond, out building, out weapon, out error);
        }
        private bool CanEdit(out string error)
        {
            error = !world.IsSandbox ? "此操作仅适用于沙盒模式。" : !world.CanEditBuildings ? "只能在准备阶段调整沙盒参数。" : null;
            return error == null;
        }
    }
}
