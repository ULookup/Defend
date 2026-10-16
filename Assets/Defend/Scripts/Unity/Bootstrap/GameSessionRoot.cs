using System;
using System.Collections.Generic;
using Defend.Simulation;
using UnityEngine;

namespace Defend.Unity
{
    /// <summary>场景组合入口：构造模拟世界，明确驱动输入、逻辑步进与只读表现。</summary>
    public sealed partial class GameSessionRoot : MonoBehaviour
    {
        [SerializeField] private BuildingDefinitionAsset[] buildingDefinitions;
        [SerializeField] private EnergyExperimentSettings experimentSettings;
        [SerializeField] private WorldPresentation presentation;
        [SerializeField] private GridInputController gridInput;
        [SerializeField] private EnergyControlPanel controlPanel;
        [SerializeField] private BattleSettingsAsset battleSettings;
        [SerializeField] private BattlePresentation battlePresentation;
        [SerializeField] private BattleControlPanel battleControlPanel;
        [SerializeField] private CombatArtPresentation combatArt;
        [SerializeField] private DefenseInterface defenseInterface;
        [SerializeField] private GameObject energyLossCellPrefab;
        [SerializeField] private PlacementPresentation placementPresentation;
        [SerializeField] private EnvironmentCloudMotion environmentCloudMotion;
        private EnergyLossGridOverlay energyLossGridOverlay;
        private WorldCameraViewport cameraViewport;
        private enum CameraFollowTargetKind { None, Building, Enemy }
        private GameWorld cameraFollowWorld;
        private CameraFollowTargetKind cameraFollowTargetKind;
        private long cameraFollowTargetId;
        private string cameraFollowFeedback;
        private readonly List<InitialPlacement> initialPlacements = new List<InitialPlacement>();
        private SimulationClock clock;
        private float nextPanelRefresh;
        private long lastReportedCommand;
        private double displayedTick;
        public GameWorld World { get; private set; }
        public BattleSystem Battle => World?.Battle;
        /// <summary>仅有性能探针订阅时计时，常规游玩不支付 Stopwatch 记录成本。</summary>
        public event Action<double> SimulationStepMeasured;
        public bool Paused { get; private set; }
        public double Speed { get; private set; } = 1;
        public long SelectedBuildingId { get; private set; }
        public EnemyState SelectedEnemy { get; private set; }
        public bool AtStartPage { get; private set; }
        public bool BlocksWorldInput => AtStartPage || (defenseInterface != null && defenseInterface.BlocksWorldInput);
        public BattlePresentation BattleViews => battlePresentation;
        public WorldPresentation WorldViews => presentation;
        public string Feedback { get; private set; } = "选择建筑查看供给，或从左侧购买建筑。";
        public GridInputController GridInput => gridInput;
        public BuildingState SelectedBuilding => World?.FindBuilding(SelectedBuildingId);
        public double PendingSimulationSeconds => clock == null ? 0 : clock.PendingSeconds;

        private readonly struct InitialPlacement
        {
            public readonly BuildingKind Kind;
            public readonly int X, Z, OrientationQuarterTurns;
            public InitialPlacement(BuildingView view)
            {
                Kind = view.Definition.Kind;
                OrientationQuarterTurns = Mathf.RoundToInt(view.transform.eulerAngles.y / 90f) % 4;
                if (Mathf.Abs(Mathf.DeltaAngle(view.transform.eulerAngles.y, OrientationQuarterTurns * 90)) > .01f)
                    throw new InvalidOperationException("初始建筑“" + view.name + "”的朝向必须为 90° 的整数倍。");
                var definition = view.Definition.CreateDefinition();
                int width = definition.GetFootprintWidth(OrientationQuarterTurns);
                int depth = definition.GetFootprintDepth(OrientationQuarterTurns);
                float originX = view.transform.position.x - width * .5f;
                float originZ = view.transform.position.z - depth * .5f;
                X = Mathf.RoundToInt(originX);
                Z = Mathf.RoundToInt(originZ);
                if (Mathf.Abs(originX - X) > .001f || Mathf.Abs(originZ - Z) > .001f)
                    throw new InvalidOperationException("初始建筑“" + view.name + "”未按 " + width + "×" + depth + " 占地对齐网格，请检查场景中的建筑中心位置。");
            }
        }

        public void Configure(BuildingDefinitionAsset[] definitions, EnergyExperimentSettings settings,
            WorldPresentation worldPresentation, GridInputController input, EnergyControlPanel panel)
        {
            buildingDefinitions = definitions;
            experimentSettings = settings;
            presentation = worldPresentation;
            gridInput = input;
            controlPanel = panel;
        }

        public void ConfigureBattle(BattleSettingsAsset settings, BattlePresentation views, BattleControlPanel panel)
        {
            battleSettings = settings;
            battlePresentation = views;
            battleControlPanel = panel;
        }

        private void Start()
        {
            if (experimentSettings == null || presentation == null || gridInput == null ||
                (battleSettings == null ? controlPanel == null : battleControlPanel == null || battlePresentation == null))
                throw new InvalidOperationException("场景缺少必需引用，请在 Inspector 中检查 GameSessionRoot。");
            foreach (BuildingView view in presentation.AuthoredBuildings) initialPlacements.Add(new InitialPlacement(view));
            CreateWorld();
            if (defenseInterface != null) { defenseInterface.Initialize(this); ReturnToStart(); }
        }

        private void CreateWorld()
        {
            StopCameraFollow();
            defenseInterface?.CloseForSessionChange();
            combatArt?.ResetSession();
            placementPresentation?.ResetSession();
            List<BuildingDefinition> definitions = new List<BuildingDefinition>();
            foreach (BuildingDefinitionAsset asset in buildingDefinitions) definitions.Add(asset.CreateDefinition());
            World = new GameWorld(definitions, experimentSettings.CreateWorldSettings(), experimentSettings.CreateEnergySettings(),
                battleSettings == null ? null : battleSettings.CreateSettings(), sandboxMode);
            clock = new SimulationClock(experimentSettings.TicksPerSecond);
            foreach (InitialPlacement placement in initialPlacements)
            {
                CommandResult result = World.Submit(WorldCommand.Place(placement.Kind, placement.X, placement.Z, placement.OrientationQuarterTurns));
                if (!result.Succeeded)
                    throw new InvalidOperationException("初始建筑“" + World.GetDefinition(placement.Kind).DisplayName + "”在网格坐标 (" + placement.X + ", " + placement.Z + ") 无法放置：" + result.Message);
            }
            if (battleSettings == null) World.Enqueue(WorldCommand.SetTestLoad(experimentSettings.EnableTestLoad));
            World.ProcessCommands();
            BindWorld();
        }

        private void BindWorld()
        {
            StopCameraFollow();
            clock = new SimulationClock(experimentSettings.TicksPerSecond);
            presentation.Initialize(World, experimentSettings);
            displayedTick = World.CurrentTick;
            environmentCloudMotion?.ResetSession();
            environmentCloudMotion?.Refresh(displayedTick, experimentSettings.TicksPerSecond);
            if (energyLossCellPrefab != null)
            {
                if (energyLossGridOverlay == null)
                    energyLossGridOverlay = gameObject.AddComponent<EnergyLossGridOverlay>();
                energyLossGridOverlay.Initialize(World, energyLossCellPrefab);
            }
            gridInput.Initialize(this, presentation);
            gridInput.Cancel();
            cameraViewport = gridInput.WorldCamera == null ? null : gridInput.WorldCamera.GetComponent<WorldCameraViewport>();
            if (cameraViewport != null)
            {
                cameraViewport.StopFollowing();
                BuildingState core = Battle == null ? null : World.FindBuilding(Battle.CoreBuildingId);
                Vector2 focus = core == null ? new Vector2(World.Settings.Width * .5f, World.Settings.Depth * .5f) :
                    new Vector2((float)core.CenterX, (float)core.CenterZ);
                cameraViewport.ConfigureMap(World.Settings.Width, World.Settings.Depth, focus);
            }
            controlPanel?.Initialize(this);
            battlePresentation?.Initialize();
            if (defenseInterface == null) battleControlPanel?.Initialize(this);
            Paused = IsSandbox;
            Speed = 1;
            SelectedBuildingId = 0;
            SelectedEnemy = null;
            AtStartPage = false;
            lastReportedCommand = World.LastCommandResult.Sequence;
            Feedback = Battle == null ? "选择建筑查看供给，或从左侧购买建筑。" : "部署炮塔与供电线路，准备好后开始第一波。";
            presentation.Refresh(World, SelectedBuildingId, World.CurrentTick);
            battlePresentation?.ObserveStep(World);
            battlePresentation?.Refresh(World, World.CurrentTick, Paused);
            combatArt?.Initialize(World, presentation, battlePresentation);
            ResetAudioState();
            RefreshPanels();
        }

        private void Update()
        {
            if (World == null) { StopCameraFollow(); return; }
            defenseAudio?.SetCombatPaused(Paused || AtStartPage);
            bool typing = gridInput.IsTypingText;
            gridInput.ProcessShortcuts(typing);
            cameraViewport?.ProcessFrame(BlocksWorldInput, typing);
            gridInput.ProcessInput();
            bool terminal = Battle != null && (Battle.State == BattleState.Victory || Battle.State == BattleState.Defeat);
            if (terminal && gridInput.HasPlacement) gridInput.Cancel();
            clock.Advance(Time.unscaledDeltaTime, Paused || terminal || AtStartPage, Speed, StepWorld);
            if (SelectedBuilding != null && (!SelectedBuilding.IsAlive || SelectedBuilding.IsSold)) SelectedBuildingId = 0;
            CommandResult result = World.LastCommandResult;
            if (result.Sequence != 0 && result.Sequence != lastReportedCommand)
            {
                lastReportedCommand = result.Sequence;
                Feedback = result.Message;
            }
            // 暂停时保留现有插值位置；时钟不再积累时间，表现也不会退回上一个逻辑位置。
            double interpolation = Math.Min(1, clock.PendingSeconds * experimentSettings.TicksPerSecond);
            double targetDisplayTick = Math.Max(0, World.CurrentTick - 1 + interpolation);
            // 整局结束后仍补齐最后一个显示区间；主动暂停不推进任何画面时间。
            if (!Paused && terminal) targetDisplayTick = Math.Min(World.CurrentTick,
                displayedTick + Time.unscaledDeltaTime * experimentSettings.TicksPerSecond * Speed);
            if (!Paused && !AtStartPage) displayedTick = Math.Max(displayedTick, targetDisplayTick);
            if (!Paused && !AtStartPage) environmentCloudMotion?.Refresh(displayedTick, experimentSettings.TicksPerSecond);
            float feedbackSeconds = Paused || AtStartPage ? 0 : Time.unscaledDeltaTime;
            presentation.SynchronizeBuildingPoses(World);
            presentation.AdvanceFeedback(feedbackSeconds);
            if (combatArt == null) battlePresentation?.AdvanceFeedback(feedbackSeconds);
            placementPresentation?.Refresh(World, BlocksWorldInput ? null : gridInput.PreviewDefinition,
                gridInput.PreviewX, gridInput.PreviewZ, gridInput.MovingBuildingId,
                gridInput.HasVisiblePlacementPreview, gridInput.PreviewValid, Time.unscaledDeltaTime,
                gridInput.PreviewOrientationQuarterTurns, gridInput.PreviewFacingDegrees);
            if (energyLossGridOverlay != null)
            {
                if (BlocksWorldInput || (gridInput.HasPlacement && gridInput.PreviewDefinition == null) || (!gridInput.HasPlacement && !presentation.ShowRanges)) energyLossGridOverlay.Hide();
                else if (gridInput.HasPlacement)
                {
                    energyLossGridOverlay.RefreshPlacement(World, gridInput.MovingBuildingId, gridInput.PreviewDefinition,
                        gridInput.PreviewX, gridInput.PreviewZ, gridInput.HasVisiblePlacementPreview,
                        gridInput.PreviewOrientationQuarterTurns);
                }
                else energyLossGridOverlay.Refresh(World, SelectedBuildingId);
            }
            battlePresentation?.Refresh(World, displayedTick, Paused || AtStartPage);
            battlePresentation?.ShowSelection(SelectedEnemy != null && SelectedEnemy.IsAlive ? SelectedEnemy.Id : 0);
            combatArt?.Refresh(displayedTick, SelectedBuildingId, Paused || AtStartPage, feedbackSeconds);
            presentation.Refresh(World, SelectedBuildingId, displayedTick);
            UpdateCameraFollow(typing);
            PresentAudioState();
            if (Time.unscaledTime >= nextPanelRefresh)
            {
                nextPanelRefresh = Time.unscaledTime + 0.1f;
                RefreshPanels();
            }
        }

        public bool BeginCameraFollowBuilding(long id) => BeginCameraFollow(CameraFollowTargetKind.Building, id);
        public bool BeginCameraFollowEnemy(long id) => BeginCameraFollow(CameraFollowTargetKind.Enemy, id);
        public bool IsFollowingBuilding(long id) => IsFollowing(CameraFollowTargetKind.Building, id);
        public bool IsFollowingEnemy(long id) => IsFollowing(CameraFollowTargetKind.Enemy, id);

        private bool BeginCameraFollow(CameraFollowTargetKind kind, long id)
        {
            if (World == null || cameraViewport == null || gridInput == null || BlocksWorldInput ||
                gridInput.HasPlacement || gridInput.IsTypingText ||
                !TryResolveCameraFollowTarget(World, kind, id, out Vector3 position, out string displayName)) return false;

            cameraViewport.BeginFollow(position);
            if (!cameraViewport.IsFollowing) return false;
            cameraFollowWorld = World;
            cameraFollowTargetKind = kind;
            cameraFollowTargetId = id;
            cameraFollowFeedback = "跟随：" + displayName + " · WASD/中键退出";
            ShowFeedback(cameraFollowFeedback);
            return true;
        }

        public void StopCameraFollow()
        {
            cameraFollowWorld = null;
            cameraFollowTargetKind = CameraFollowTargetKind.None;
            cameraFollowTargetId = 0;
            cameraViewport?.StopFollowing();
            // 只替换仍属于跟随的提示，不覆盖随后产生的建造、结算或错误消息。
            if (cameraFollowFeedback != null && Feedback == cameraFollowFeedback) Feedback = "已停止镜头跟随。";
            cameraFollowFeedback = null;
        }

        private bool IsFollowing(CameraFollowTargetKind kind, long id)
        {
            return cameraViewport != null && cameraViewport.IsFollowing && cameraFollowTargetKind == kind &&
                cameraFollowTargetId == id && TryResolveCameraFollowTarget(cameraFollowWorld, kind, id, out _, out _);
        }

        private void UpdateCameraFollow(bool typing)
        {
            if (cameraFollowTargetKind == CameraFollowTargetKind.None) return;
            if (cameraViewport == null || !cameraViewport.IsFollowing || gridInput.HasPlacement ||
                !TryResolveCameraFollowTarget(cameraFollowWorld, cameraFollowTargetKind, cameraFollowTargetId,
                    out Vector3 position, out _))
            {
                StopCameraFollow();
                return;
            }
            // 暂停保留身份。模态页面或文字输入只冻结镜头，目标身份仍在每帧校验。
            cameraViewport.UpdateFollow(position, BlocksWorldInput, typing);
            if (!cameraViewport.IsFollowing) StopCameraFollow();
        }

        private bool TryResolveCameraFollowTarget(GameWorld targetWorld, CameraFollowTargetKind kind, long id,
            out Vector3 position, out string displayName)
        {
            position = default;
            displayName = null;
            if (id <= 0 || targetWorld == null || !ReferenceEquals(targetWorld, World)) return false;
            if (kind == CameraFollowTargetKind.Building)
            {
                BuildingState building = targetWorld.FindBuilding(id);
                BuildingView view = presentation == null ? null : presentation.FindView(id);
                if (building == null || !building.IsAlive || building.IsSold || view == null ||
                    view.BuildingId != id || !view.gameObject.activeInHierarchy) return false;
                position = new Vector3((float)building.CenterX, 0, (float)building.CenterZ);
                displayName = building.Definition.DisplayName;
                return true;
            }
            if (kind != CameraFollowTargetKind.Enemy || targetWorld.Battle == null) return false;
            EnemyState enemy = null;
            foreach (EnemyState candidate in targetWorld.Battle.Enemies)
                if (candidate.Id == id) { enemy = candidate; break; }
            EnemyView enemyView = battlePresentation == null ? null : battlePresentation.FindView(id);
            if (enemy == null || !enemy.IsAlive || enemyView == null || enemyView.EnemyId != id ||
                !enemyView.gameObject.activeInHierarchy) return false;
            // 读取本帧插值后的实体根节点，避开受击/攻击动作的模型子节点；不缓存池化视图。
            position = enemyView.transform.position;
            displayName = enemy.Definition.DisplayName;
            return true;
        }

        private void OnDisable() => StopCameraFollow();

        public BuildingDefinitionAsset FindDefinition(BuildingKind kind)
        {
            foreach (BuildingDefinitionAsset definition in buildingDefinitions)
                if (definition.Kind == kind) return definition;
            throw new InvalidOperationException("缺少建筑定义：" + kind);
        }

        public void Submit(WorldCommand command)
        {
            if (Battle != null && (Battle.State == BattleState.Victory || Battle.State == BattleState.Defeat))
            {
                ShowFeedback("本次防守已结束，请重新开始。");
                PlayInterfaceSound(DefenseSoundCue.UiDenied);
                return;
            }
            if (AtStartPage) return;
            // 所有输入即时经过同一权威事务；暂停只停止时间，非法输入不会留到下一个阶段。
            CommandResult commandResult = World.Submit(command);
            Feedback = commandResult.Message;
            PresentCommandSound(command, commandResult);
            presentation.Invalidate();
            presentation.ObserveCommands(World);
            combatArt?.ObserveStep(World);
            if (World.LastCommandResult.Succeeded)
            {
                displayedTick = World.CurrentTick;
                presentation.SynchronizeBuildingPoses(World);
                combatArt?.FlushToTick(displayedTick, SelectedBuildingId, advanceModelTime: !Paused);
                presentation.Refresh(World, SelectedBuildingId, displayedTick);
            }
            PresentAudioState();
            RefreshPanels();
        }

        private void RefreshPanels() { controlPanel?.Refresh(); if (defenseInterface == null) battleControlPanel?.Refresh(); defenseInterface?.Refresh(); }
        private void StepWorld()
        {
            Action<double> measured = SimulationStepMeasured;
            double milliseconds = 0;
            UnityEngine.Profiling.Profiler.BeginSample("Defend.Simulation.Step");
            long started = measured == null ? 0 : System.Diagnostics.Stopwatch.GetTimestamp();
            try { World.Step(); }
            finally
            {
                if (measured != null)
                    milliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                UnityEngine.Profiling.Profiler.EndSample();
            }
            // 探针回调位于采样区间外，性能记录自身不会被计入世界步进耗时。
            measured?.Invoke(milliseconds);
            presentation.ObserveStep(World);
            battlePresentation?.ObserveStep(World);
            if (combatArt != null && combatArt.IsBoundTo(World)) combatArt.ObserveStep(World);
        }
        public void Select(long id) { if (id != 0 && (SelectedBuildingId != id || SelectedEnemy != null)) PlayInterfaceSound(DefenseSoundCue.Selection); SelectedEnemy = null; SelectedBuildingId = id; defenseInterface?.RevealDetails(); RefreshPanels(); }
        public void SelectEnemy(EnemyState enemy) { if (enemy != null && SelectedEnemy != enemy) PlayInterfaceSound(DefenseSoundCue.Selection); SelectedBuildingId = 0; SelectedEnemy = enemy; defenseInterface?.RevealDetails(); RefreshPanels(); }
        public void ClearSelection() { SelectedEnemy = null; SelectedBuildingId = 0; RefreshPanels(); }
        public void ShowFeedback(string message) { Feedback = message; RefreshPanels(); }
        public void TogglePause() { if (defenseInterface != null) defenseInterface.TogglePause(); else SetPaused(!Paused); }
        public bool ConsumePageShortcut(bool escape) => defenseInterface != null && defenseInterface.ConsumePageShortcut(escape);
        public BattleSettings CreateArchiveBattleSettings() => battleSettings.CreateSettings();
        public void SetPaused(bool value)
        {
            if (!value && defenseInterface != null && defenseInterface.IsSandboxPanelOpen) return;
            bool changed = Paused != value;
            Paused = value;
            defenseAudio?.SetCombatPaused(Paused || AtStartPage);
            if (changed && !AtStartPage) PlayInterfaceSound(value ? DefenseSoundCue.Pause : DefenseSoundCue.Resume);
            RefreshPanels();
        }
        public void SingleStep()
        {
            if (defenseInterface != null && defenseInterface.IsSandboxPanelOpen) return;
            Paused = true;
            defenseAudio?.SetCombatPaused(true);
            float seconds = 1f / experimentSettings.TicksPerSecond;
            presentation.AdvanceFeedback(seconds);
            combatArt?.AdvanceFeedback(seconds);
            if (combatArt == null) battlePresentation?.AdvanceFeedback(seconds);
            StepWorld();
            displayedTick = World.CurrentTick;
            environmentCloudMotion?.Refresh(displayedTick, experimentSettings.TicksPerSecond);
            battlePresentation?.Refresh(World, displayedTick, true);
            defenseAudio?.BeginPresentationStep();
            try { combatArt?.FlushToTick(displayedTick, SelectedBuildingId); }
            finally { defenseAudio?.EndPresentationStep(); }
            presentation.Refresh(World, SelectedBuildingId, displayedTick);
            PresentAudioState();
            RefreshPanels();
        }
        public void CycleSpeed() { Speed = Speed == 1 ? 2 : Speed == 2 ? 4 : 1; RefreshPanels(); }
        public void StartWave()
        {
            if (Battle == null || !World.CanEditBuildings || AtStartPage) return;
            if (IsSandbox && defenseInterface != null && defenseInterface.HasUnappliedSandboxChanges)
            { ShowFeedback("请先应用或放弃沙盒面板中尚未应用的修改。"); PlayInterfaceSound(DefenseSoundCue.UiDenied); return; }
            if (IsSandbox && defenseInterface != null && defenseInterface.IsSandboxPanelOpen)
            { ShowFeedback("请先完成出生点操作并关闭沙盒配置，再开始本波。"); PlayInterfaceSound(DefenseSoundCue.UiDenied); return; }
            gridInput.Cancel(); Submit(WorldCommand.StartWave()); if (Battle.State == BattleState.Combat) SetPaused(false);
        }
        public void Restart() { if (World != null) { CreateWorld(); defenseInterface?.SessionStarted(); } }
        public void ReturnToStart() { StopCameraFollow(); sandboxMode = false; if (!AtStartPage && World != null) CreateWorld(); gridInput.Cancel(); ClearSelection(); AtStartPage = true; Paused = true; defenseInterface?.ShowStart(); }
        public void ToggleTestLoad() => Submit(WorldCommand.SetTestLoad(!World.TestLoadEnabled));
        public void SellSelected() { if (SelectedBuilding != null) Submit(WorldCommand.Sell(SelectedBuildingId)); }
        public void DamageSelected() { if (SelectedBuilding != null) Submit(WorldCommand.Damage(SelectedBuildingId, SelectedBuilding.Definition.MaximumHealth)); }
        public void RepairSelected() { if (SelectedBuilding != null) Submit(WorldCommand.Repair(SelectedBuildingId)); }
        public void SetSelectedPriority(int weight) { if (SelectedBuilding != null) Submit(WorldCommand.SetPriority(SelectedBuildingId, weight)); }
    }
}
