using System;
using System.Collections.Generic;
using Defend.Simulation;
using UnityEngine;

namespace Defend.Unity
{
    /// <summary>
    /// 场景组合入口：构造模拟世界，明确驱动输入、逻辑步进与只读表现。
    /// </summary>
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
        [SerializeField] private GameObject energyLossCellPrefab;

        private EnergyLossGridOverlay energyLossGridOverlay;

        private readonly List<InitialPlacement>
            initialPlacements =
                new List<InitialPlacement>();

        private SimulationClock clock;
        private float nextPanelRefresh;
        private long lastReportedCommand;

        public GameWorld World { get; private set; }

        public BattleSystem Battle =>
            World?.Battle;

        public event Action<double>
            SimulationStepMeasured;

        public bool Paused { get; private set; }

        public double Speed { get; private set; } = 1;

        public long SelectedBuildingId { get; private set; }

        public string Feedback { get; private set; } =
            "选择建筑查看供给，或从左侧购买建筑。";

        public GridInputController GridInput =>
            gridInput;

        public BuildingState SelectedBuilding =>
            World?.FindBuilding(SelectedBuildingId);

        public double PendingSimulationSeconds =>
            clock == null
                ? 0
                : clock.PendingSeconds;

        private readonly struct InitialPlacement
        {
            public readonly BuildingKind Kind;
            public readonly int X;
            public readonly int Z;

            public InitialPlacement(
                BuildingView view)
            {
                Kind =
                    view.Definition.Kind;

                X =
                    Mathf.FloorToInt(
                        view.transform.position.x);

                Z =
                    Mathf.FloorToInt(
                        view.transform.position.z);
            }
        }

        public void Configure(
            BuildingDefinitionAsset[] definitions,
            EnergyExperimentSettings settings,
            WorldPresentation worldPresentation,
            GridInputController input,
            EnergyControlPanel panel)
        {
            buildingDefinitions =
                definitions;

            experimentSettings =
                settings;

            presentation =
                worldPresentation;

            gridInput =
                input;

            controlPanel =
                panel;
        }

        public void ConfigureBattle(
            BattleSettingsAsset settings,
            BattlePresentation views,
            BattleControlPanel panel)
        {
            battleSettings =
                settings;

            battlePresentation =
                views;

            battleControlPanel =
                panel;
        }

        private void Start()
        {
            if (experimentSettings == null ||
                presentation == null ||
                gridInput == null ||
                (
                    battleSettings == null
                        ? controlPanel == null
                        : battleControlPanel == null ||
                          battlePresentation == null
                ))
            {
                throw new InvalidOperationException(
                    "场景缺少必需引用，请在 Inspector 中检查 GameSessionRoot。");
            }

            foreach (BuildingView view
                     in presentation.AuthoredBuildings)
            {
                initialPlacements.Add(
                    new InitialPlacement(view));
            }

            CreateWorld();
        }

        private void CreateWorld()
        {
            combatArt?.ResetSession();

            List<BuildingDefinition> definitions =
                new List<BuildingDefinition>();

            foreach (BuildingDefinitionAsset asset
                     in buildingDefinitions)
            {
                definitions.Add(
                    asset.CreateDefinition());
            }

            World =
                new GameWorld(
                    definitions,
                    experimentSettings.CreateWorldSettings(),
                    experimentSettings.CreateEnergySettings(),
                    battleSettings == null
                        ? null
                        : battleSettings.CreateSettings());

            clock =
                new SimulationClock(
                    experimentSettings.TicksPerSecond);

            foreach (InitialPlacement placement
                     in initialPlacements)
            {
                World.Enqueue(
                    WorldCommand.Place(
                        placement.Kind,
                        placement.X,
                        placement.Z));
            }

            if (battleSettings == null)
            {
                World.Enqueue(
                    WorldCommand.SetTestLoad(
                        experimentSettings.EnableTestLoad));
            }

            StepWorld();

            presentation.Initialize(
                World,
                experimentSettings);

            if (energyLossCellPrefab != null)
            {
                if (energyLossGridOverlay == null)
                {
                    energyLossGridOverlay =
                        gameObject.AddComponent<
                            EnergyLossGridOverlay>();
                }

                energyLossGridOverlay.Initialize(
                    World,
                    energyLossCellPrefab);
            }

            gridInput.Initialize(
                this,
                presentation);

            gridInput.Cancel();

            controlPanel?.Initialize(this);
            battlePresentation?.Initialize();
            battleControlPanel?.Initialize(this);

            Paused = false;
            Speed = 1;
            SelectedBuildingId = 0;

            lastReportedCommand =
                World.LastCommandResult.Sequence;

            Feedback =
                Battle == null
                    ? "选择建筑查看供给，或从左侧购买建筑。"
                    : "部署炮塔与供电线路，准备好后开始第一波。";

            presentation.Refresh(
                World,
                SelectedBuildingId,
                World.CurrentTick);

            battlePresentation?.Refresh(
                World,
                World.CurrentTick,
                Paused);

            combatArt?.Initialize(
                World,
                presentation,
                battlePresentation);

            RefreshPanels();
        }

        private void Update()
        {
            if (World == null)
            {
                return;
            }

            gridInput.ProcessInput();

            bool terminal =
                Battle != null &&
                (
                    Battle.State == BattleState.Victory ||
                    Battle.State == BattleState.Defeat
                );

            if (terminal &&
                gridInput.HasPlacement)
            {
                gridInput.Cancel();
            }

            clock.Advance(
                Time.unscaledDeltaTime,
                Paused || terminal,
                Speed,
                StepWorld);

            CommandResult result =
                World.LastCommandResult;

            if (result.Sequence != 0 &&
                result.Sequence != lastReportedCommand)
            {
                lastReportedCommand =
                    result.Sequence;

                Feedback =
                    result.Message;
            }

            double interpolation =
                Math.Min(
                    1,
                    clock.PendingSeconds *
                    experimentSettings.TicksPerSecond);

            presentation.Refresh(
                World,
                SelectedBuildingId,
                World.CurrentTick +
                interpolation);

            energyLossGridOverlay?.Refresh(
                World,
                SelectedBuildingId);

            battlePresentation?.Refresh(
                World,
                World.CurrentTick +
                interpolation,
                Paused || terminal);

            combatArt?.Refresh(
                World.CurrentTick +
                interpolation,
                SelectedBuildingId,
                Paused,
                Time.unscaledDeltaTime);

            if (Time.unscaledTime >=
                nextPanelRefresh)
            {
                nextPanelRefresh =
                    Time.unscaledTime +
                    0.1f;

                RefreshPanels();
            }
        }

        public BuildingDefinitionAsset FindDefinition(
            BuildingKind kind)
        {
            foreach (BuildingDefinitionAsset definition
                     in buildingDefinitions)
            {
                if (definition.Kind == kind)
                {
                    return definition;
                }
            }

            throw new InvalidOperationException(
                "缺少建筑定义：" + kind);
        }

        public void Submit(
            WorldCommand command)
        {
            if (Battle != null &&
                (
                    Battle.State == BattleState.Victory ||
                    Battle.State == BattleState.Defeat
                ))
            {
                ShowFeedback(
                    "本次防守已结束，请重新开始。");

                return;
            }

            World.Enqueue(command);

            Feedback =
                Paused
                    ? Battle == null
                        ? "操作已排队；单步或继续模拟后生效。"
                        : "操作已排队，继续后生效。"
                    : "操作已提交。";

            RefreshPanels();
        }

        private void RefreshPanels()
        {
            controlPanel?.Refresh();
            battleControlPanel?.Refresh();
        }

        private void StepWorld()
        {
            Action<double> measured =
                SimulationStepMeasured;

            double milliseconds = 0;

            UnityEngine.Profiling.Profiler.BeginSample(
                "Defend.Simulation.Step");

            long started =
                measured == null
                    ? 0
                    : System.Diagnostics.Stopwatch.GetTimestamp();

            try
            {
                World.Step();
            }
            finally
            {
                if (measured != null)
                {
                    milliseconds =
                        (
                            System.Diagnostics.Stopwatch.GetTimestamp() -
                            started
                        ) *
                        1000.0 /
                        System.Diagnostics.Stopwatch.Frequency;
                }

                UnityEngine.Profiling.Profiler.EndSample();
            }

            measured?.Invoke(milliseconds);

            if (combatArt != null &&
                combatArt.IsBoundTo(World))
            {
                presentation.Refresh(
                    World,
                    SelectedBuildingId,
                    World.CurrentTick);

                battlePresentation?.Refresh(
                    World,
                    World.CurrentTick,
                    Paused);

                combatArt.ObserveStep(World);
            }
        }

        public void Select(long id)
        {
            SelectedBuildingId =
                id;

            RefreshPanels();
        }

        public void ShowFeedback(string message)
        {
            Feedback =
                message;

            RefreshPanels();
        }

        public void TogglePause()
        {
            Paused =
                !Paused;

            RefreshPanels();
        }

        public void SingleStep()
        {
            Paused = true;
            StepWorld();
            RefreshPanels();
        }

        public void CycleSpeed()
        {
            Speed =
                Speed == 1
                    ? 2
                    : Speed == 2
                        ? 4
                        : 1;

            RefreshPanels();
        }

        public void StartWave()
        {
            if (Battle != null)
            {
                Submit(
                    WorldCommand.StartWave());
            }
        }

        public void Restart()
        {
            if (World != null)
            {
                CreateWorld();
            }
        }

        public void ToggleTestLoad()
        {
            Submit(
                WorldCommand.SetTestLoad(
                    !World.TestLoadEnabled));
        }

        public void SellSelected()
        {
            if (SelectedBuilding != null)
            {
                Submit(
                    WorldCommand.Sell(
                        SelectedBuildingId));
            }
        }

        public void DamageSelected()
        {
            if (SelectedBuilding != null)
            {
                Submit(
                    WorldCommand.Damage(
                        SelectedBuildingId,
                        SelectedBuilding.Definition.MaximumHealth));
            }
        }

        public void RepairSelected()
        {
            if (SelectedBuilding != null)
            {
                Submit(
                    WorldCommand.Repair(
                        SelectedBuildingId));
            }
        }

        public void SetSelectedPriority(int weight)
        {
            if (SelectedBuilding != null)
            {
                Submit(
                    WorldCommand.SetPriority(
                        SelectedBuildingId,
                        weight));
            }
        }
    }
}