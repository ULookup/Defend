using System;
using System.Collections.Generic;
using Defend.Simulation;
using UnityEngine;

namespace Defend.Unity
{
    /// <summary>场景组合入口：构造模拟世界，明确驱动输入、逻辑步进与只读表现。</summary>
    public sealed class GameSessionRoot : MonoBehaviour
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
        private EnergyLossGridOverlay energyLossGridOverlay;
        private readonly List<InitialPlacement> initialPlacements = new List<InitialPlacement>();
        private SimulationClock clock;
        private float nextPanelRefresh;
        private long lastReportedCommand;
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
            public readonly int X, Z;
            public InitialPlacement(BuildingView view)
            {
                Kind = view.Definition.Kind;
                X = Mathf.FloorToInt(view.transform.position.x);
                Z = Mathf.FloorToInt(view.transform.position.z);
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
            combatArt?.ResetSession();
            List<BuildingDefinition> definitions = new List<BuildingDefinition>();
            foreach (BuildingDefinitionAsset asset in buildingDefinitions) definitions.Add(asset.CreateDefinition());
            World = new GameWorld(definitions, experimentSettings.CreateWorldSettings(), experimentSettings.CreateEnergySettings(),
                battleSettings == null ? null : battleSettings.CreateSettings());
            clock = new SimulationClock(experimentSettings.TicksPerSecond);
            foreach (InitialPlacement placement in initialPlacements)
            {
                World.Enqueue(WorldCommand.Place(placement.Kind, placement.X, placement.Z));
            }
            if (battleSettings == null) World.Enqueue(WorldCommand.SetTestLoad(experimentSettings.EnableTestLoad));
            World.ProcessCommands();
            presentation.Initialize(World, experimentSettings);
            if (energyLossCellPrefab != null)
            {
                if (energyLossGridOverlay == null)
                    energyLossGridOverlay = gameObject.AddComponent<EnergyLossGridOverlay>();
                energyLossGridOverlay.Initialize(World, energyLossCellPrefab);
            }
            gridInput.Initialize(this, presentation);
            gridInput.Cancel();
            controlPanel?.Initialize(this);
            battlePresentation?.Initialize();
            if (defenseInterface == null) battleControlPanel?.Initialize(this);
            Paused = false;
            Speed = 1;
            SelectedBuildingId = 0;
            SelectedEnemy = null;
            AtStartPage = false;
            lastReportedCommand = World.LastCommandResult.Sequence;
            Feedback = Battle == null ? "选择建筑查看供给，或从左侧购买建筑。" : "部署炮塔与供电线路，准备好后开始第一波。";
            presentation.Refresh(World, SelectedBuildingId, World.CurrentTick);
            battlePresentation?.Refresh(World, World.CurrentTick, Paused);
            combatArt?.Initialize(World, presentation, battlePresentation);
            RefreshPanels();
        }

        private void Update()
        {
            if (World == null) return;
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
            presentation.Refresh(World, SelectedBuildingId, World.CurrentTick + interpolation);
            energyLossGridOverlay?.Refresh(World, SelectedBuildingId);
            battlePresentation?.Refresh(World, World.CurrentTick + interpolation, Paused || terminal || AtStartPage);
            battlePresentation?.ShowSelection(SelectedEnemy != null && SelectedEnemy.IsAlive ? SelectedEnemy.Id : 0);
            combatArt?.Refresh(World.CurrentTick + interpolation, SelectedBuildingId, Paused, Time.unscaledDeltaTime);
            if (Time.unscaledTime >= nextPanelRefresh)
            {
                nextPanelRefresh = Time.unscaledTime + 0.1f;
                RefreshPanels();
            }
        }

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
                return;
            }
            if (AtStartPage) return;
            // 所有输入即时经过同一权威事务；暂停只停止时间，非法输入不会留到下一个阶段。
            Feedback = World.Submit(command).Message;
            presentation.Invalidate();
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
            if (combatArt != null && combatArt.IsBoundTo(World))
            {
                presentation.Refresh(World, SelectedBuildingId, World.CurrentTick);
                battlePresentation?.Refresh(World, World.CurrentTick, Paused);
                combatArt.ObserveStep(World);
            }
        }
        public void Select(long id) { SelectedEnemy = null; SelectedBuildingId = id; defenseInterface?.RevealDetails(); RefreshPanels(); }
        public void SelectEnemy(EnemyState enemy) { SelectedBuildingId = 0; SelectedEnemy = enemy; defenseInterface?.RevealDetails(); RefreshPanels(); }
        public void ClearSelection() { SelectedEnemy = null; SelectedBuildingId = 0; RefreshPanels(); }
        public void ShowFeedback(string message) { Feedback = message; RefreshPanels(); }
        public void TogglePause() { if (defenseInterface != null) defenseInterface.TogglePause(); else SetPaused(!Paused); }
        public void SetPaused(bool value) { Paused = value; RefreshPanels(); }
        public void SingleStep() { Paused = true; StepWorld(); RefreshPanels(); }
        public void CycleSpeed() { Speed = Speed == 1 ? 2 : Speed == 2 ? 4 : 1; RefreshPanels(); }
        public void StartWave() { if (Battle == null || !World.CanEditBuildings || AtStartPage) return; gridInput.Cancel(); Submit(WorldCommand.StartWave()); if (Battle.State == BattleState.Combat) SetPaused(false); }
        public void Restart() { if (World != null) { CreateWorld(); defenseInterface?.SessionStarted(); } }
        public void ReturnToStart() { if (!AtStartPage && World != null) CreateWorld(); gridInput.Cancel(); ClearSelection(); AtStartPage = true; Paused = true; defenseInterface?.ShowStart(); }
        public void ToggleTestLoad() => Submit(WorldCommand.SetTestLoad(!World.TestLoadEnabled));
        public void SellSelected() { if (SelectedBuilding != null) Submit(WorldCommand.Sell(SelectedBuildingId)); }
        public void DamageSelected() { if (SelectedBuilding != null) Submit(WorldCommand.Damage(SelectedBuildingId, SelectedBuilding.Definition.MaximumHealth)); }
        public void RepairSelected() { if (SelectedBuilding != null) Submit(WorldCommand.Repair(SelectedBuildingId)); }
        public void SetSelectedPriority(int weight) { if (SelectedBuilding != null) Submit(WorldCommand.SetPriority(SelectedBuildingId, weight)); }
    }
}
