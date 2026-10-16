using System.Collections.Generic;
using Defend.Simulation;
using UnityEngine;
using UnityEngine.Pool;

namespace Defend.Unity
{
    /// <summary>统一同步建筑、连线和运输表现，防止每个能量物体各自执行 Update。</summary>
    public sealed class WorldPresentation : MonoBehaviour
    {
        [SerializeField] private BuildingView[] buildingPrefabs;
        [SerializeField] private bool externalTransferEffects;
        public BuildingView FindView(long id) => buildingViews.TryGetValue(id, out var view) ? view : null;
        [SerializeField] private BuildingView[] authoredBuildings;
        [SerializeField] private EnergyTransferView transferPrefab;
        [SerializeField] private LineRenderer connectionPrefab;
        [SerializeField] private Transform buildingRoot;
        [SerializeField] private Transform energyRoot;
        [SerializeField] private Transform connectionRoot;
        [SerializeField] private Transform placementPreview;
        [SerializeField] private Renderer previewRenderer;
        [SerializeField] private LineRenderer selectionRange;
        [SerializeField] private LineRenderer previewRange;
        public void ConfigureRanges(LineRenderer selected, LineRenderer preview) { selectionRange = selected; previewRange = preview; }
        private readonly Dictionary<long, BuildingView> buildingViews = new Dictionary<long, BuildingView>();
        private readonly List<long> removedBuildings = new List<long>();
        private readonly List<EnergyTransferView> transferViews = new List<EnergyTransferView>();
        private readonly List<LineRenderer> connectionViews = new List<LineRenderer>();
        private ObjectPool<EnergyTransferView> transferPool;
        private readonly Dictionary<BuildingKind, ObjectPool<BuildingView>> buildingPools = new Dictionary<BuildingKind, ObjectPool<BuildingView>>();
        private readonly HashSet<BuildingView> authoredViews = new HashSet<BuildingView>();
        // 与下面的对象池、身份映射同属单次播放会话；禁止热重载单独恢复这个数组。
        [System.NonSerialized] private Vector3[] authoredPositions;
        private MaterialPropertyBlock previewProperties;
        private MaterialPropertyBlock connectionProperties;
        private int maximumVisibleTransfers;
        private int maximumVisibleConnections;
        private long lastAppliedTick = -1;
        private long lastSelectedId = -1;
        public IReadOnlyList<BuildingView> AuthoredBuildings => authoredBuildings;
        public void Invalidate() { lastAppliedTick = -1; }
        public bool ShowNetwork { get; set; } = true;
        public bool ShowRanges { get; set; } = true;
        private bool placementPreviewActive;
        public bool PlacementPreviewActive
        {
            get => placementPreviewActive;
            set { if (placementPreviewActive == value) return; placementPreviewActive = value; Invalidate(); }
        }
        private readonly Queue<BuildingDisplayFrame> displayFrames = new Queue<BuildingDisplayFrame>();
        private readonly Dictionary<long, BuildingDisplayState> displayedBuildings = new Dictionary<long, BuildingDisplayState>();
        private long lastObservedTick = -1;

        private readonly struct BuildingDisplayState
        {
            public readonly BuildingState Source;
            public readonly int Health, MaximumHealth;
            public readonly long Energy, Capacity;
            public readonly bool Alive, Sold;
            public BuildingDisplayState(BuildingState state)
            {
                Source = state; Health = state.Health; MaximumHealth = state.Definition.MaximumHealth;
                Energy = state.StoredEnergy; Capacity = state.Definition.EnergyCapacity;
                Alive = state.IsAlive; Sold = state.IsSold;
            }
            public BuildingDisplayState(BuildingDisplayState state, int health)
            {
                Source = state.Source; Health = health; MaximumHealth = state.MaximumHealth;
                Energy = state.Energy; Capacity = state.Capacity; Alive = health > 0; Sold = state.Sold;
            }
            public BuildingDisplayState(BuildingDisplayState state, long energy)
            {
                Source = state.Source; Health = state.Health; MaximumHealth = state.MaximumHealth;
                Energy = energy; Capacity = state.Capacity; Alive = state.Alive; Sold = state.Sold;
            }
        }

        private readonly struct BuildingDisplayFrame
        {
            public readonly long Tick;
            public readonly BuildingDisplayState[] States;
            public BuildingDisplayFrame(GameWorld world)
            {
                Tick = world.CurrentTick;
                States = new BuildingDisplayState[world.Buildings.Count];
                for (int index = 0; index < States.Length; index++) States[index] = new BuildingDisplayState(world.Buildings[index]);
            }
        }

        /// <summary>保存本步的生命和真实储能；表现延迟期间不再读取可变的生命字段。</summary>
        public void ObserveStep(GameWorld world)
        {
            if (lastObservedTick == world.CurrentTick) return;
            displayFrames.Enqueue(new BuildingDisplayFrame(world));
            lastObservedTick = world.CurrentTick;
        }

        public void ObserveCommands(GameWorld world)
        {
            displayFrames.Enqueue(new BuildingDisplayFrame(world));
            Invalidate();
        }

        public void AdvanceFeedback(float seconds)
        {
            foreach (BuildingView view in buildingViews.Values) view.AdvanceFeedback(seconds);
        }

        /// <summary>在炮塔和端口读取 Transform 前同步摆放姿态；生命与能源仍由原显示快照顺序提交。</summary>
        public void SynchronizeBuildingPoses(GameWorld world)
        {
            foreach (var pair in buildingViews)
            {
                var state = world.FindBuilding(pair.Key);
                if (state != null && state.IsAlive && !state.IsSold) pair.Value.ApplyPlacementPose(state);
            }
        }

        public void AdvanceShieldPresentation(float seconds)
        {
            foreach (BuildingView view in buildingViews.Values) view.AdvanceShieldPresentation(seconds);
        }

        public void AdvanceModelPresentation(float seconds)
        {
            foreach (BuildingView view in buildingViews.Values) view.AdvanceModelPresentation(seconds);
        }

        public void ApplyResult(WorldEventSnapshot result)
        {
            if (result.Kind == WorldEventKind.ResonanceAbsorbed || result.Kind == WorldEventKind.MagneticStormCharging)
            {
                if (buildingViews.TryGetValue(result.SubjectId, out BuildingView resonanceView))
                {
                    if (result.Kind == WorldEventKind.ResonanceAbsorbed) resonanceView.ResonanceMotion?.NotifyAbsorption();
                    else resonanceView.ResonanceMotion?.NotifyStorm(false);
                }
                return;
            }
            bool shieldResult = result.Kind == WorldEventKind.ShieldHit || result.Kind == WorldEventKind.ShieldBreak;
            if (!shieldResult && result.Kind != WorldEventKind.BuildingHit && result.Kind != WorldEventKind.Repair && result.Kind != WorldEventKind.ResonanceRepair) return;
            if (!buildingViews.TryGetValue(result.SubjectId, out BuildingView view) ||
                !displayedBuildings.TryGetValue(result.SubjectId, out BuildingDisplayState state)) return;
            if (shieldResult)
            {
                // 先提交真实结算后的储能，再触发表现。破盾不会让空护盾短暂复现。
                view.ApplyVitals(result.SubjectId, state.Health, state.MaximumHealth,
                    result.ShieldAfter, state.Capacity, state.Alive && !state.Sold);
                displayedBuildings[result.SubjectId] = new BuildingDisplayState(state, result.ShieldAfter);
                if (result.Kind == WorldEventKind.ShieldHit && result.ShieldAbsorbed > 0)
                    view.NotifyShieldHit(-new Vector3((float)result.Hit.DirectionX, (float)result.Hit.DirectionY, (float)result.Hit.DirectionZ));
                return;
            }
            view.ApplyVitals(result.SubjectId, result.HealthAfter, result.MaximumHealth,
                state.Energy, state.Capacity, result.HealthAfter > 0);
            displayedBuildings[result.SubjectId] = new BuildingDisplayState(state, result.HealthAfter);
            if (result.Kind == WorldEventKind.ResonanceRepair) view.ResonanceMotion?.NotifyRepair(true);
        }

        public void Configure(BuildingView[] prefabs, BuildingView[] placed, EnergyTransferView energyPrefab,
            LineRenderer linePrefab, Transform buildings, Transform energy, Transform connections,
            Transform preview, Renderer previewShape)
        {
            buildingPrefabs = prefabs;
            authoredBuildings = placed;
            transferPrefab = energyPrefab;
            connectionPrefab = linePrefab;
            buildingRoot = buildings;
            energyRoot = energy;
            connectionRoot = connections;
            placementPreview = preview;
            previewRenderer = previewShape;
        }

        public void Initialize(GameWorld world, EnergyExperimentSettings settings)
        {
            if (authoredBuildings == null || authoredBuildings.Length == 0)
                throw new System.InvalidOperationException("初始建筑列表为空，请在编辑模式配置场景中的建筑引用。");
            if (authoredPositions != null && authoredPositions.Length != authoredBuildings.Length)
                throw new System.InvalidOperationException("播放期间初始建筑列表发生变化。请退出播放后编辑布局，再重新进入播放；不能用运行中的位置替换初始布局。");
            if (authoredPositions == null)
            {
                authoredPositions = new Vector3[authoredBuildings.Length];
                for (int index = 0; index < authoredBuildings.Length; index++)
                {
                    if (authoredBuildings[index] == null)
                        throw new System.InvalidOperationException("初始建筑引用丢失，请检查 authoredBuildings 第 " + (index + 1) + " 项。");
                    authoredPositions[index] = authoredBuildings[index].transform.position;
                    authoredViews.Add(authoredBuildings[index]);
                }
                foreach (BuildingView prefab in buildingPrefabs)
                {
                    BuildingView captured = prefab;
                    buildingPools.Add(prefab.Definition.Kind, new ObjectPool<BuildingView>(
                        () => Instantiate(captured, buildingRoot), view => { view.ResetForReuse(); view.gameObject.SetActive(true); },
                        view => view.gameObject.SetActive(false), view => { if (view != null) Destroy(view.gameObject); }, false, 4, 256));
                }
            }
            // 初始场景实例保留，动态实例归还池；重新开始不会采用运行时移动后的布局。
            foreach (BuildingView view in buildingViews.Values)
                if (!authoredViews.Contains(view)) buildingPools[view.Definition.Kind].Release(view);
            buildingViews.Clear();
            displayFrames.Clear(); displayedBuildings.Clear(); lastObservedTick = -1;
            foreach (BuildingState state in world.Buildings) displayedBuildings[state.Id] = new BuildingDisplayState(state);
            foreach (EnergyTransferView view in transferViews) transferPool.Release(view);
            transferViews.Clear();
            lastAppliedTick = -1;
            lastSelectedId = -1;
            maximumVisibleTransfers = settings.MaximumVisibleTransfers;
            maximumVisibleConnections = settings.MaximumVisibleConnections;
            if (previewProperties == null) previewProperties = new MaterialPropertyBlock();
            if (connectionProperties == null) connectionProperties = new MaterialPropertyBlock();
            if (transferPool == null) transferPool = new ObjectPool<EnergyTransferView>(
                () => Instantiate(transferPrefab, energyRoot),
                view => view.gameObject.SetActive(true),
                view => view.gameObject.SetActive(false),
                view => { if (view != null) Destroy(view.gameObject); }, false, 16, maximumVisibleTransfers);
            for (int index = 0; index < authoredBuildings.Length; index++)
            {
                BuildingView view = authoredBuildings[index];
                view.ResetForReuse();
                view.transform.position = authoredPositions[index];
                view.gameObject.SetActive(true);
                int x = Mathf.FloorToInt(view.transform.position.x);
                int z = Mathf.FloorToInt(view.transform.position.z);
                BuildingState state = world.FindBuildingAt(x, z);
                if (world.IsSandbox && (state == null || state.Kind != view.Definition.Kind || buildingViews.ContainsKey(state.Id)))
                { view.gameObject.SetActive(false); continue; }
                if (state == null)
                    throw new System.InvalidOperationException("初始建筑未能放置，请检查场景位置与初始资金：" + view.name);
                buildingViews.Add(state.Id, view);
            }
        }

        public void Refresh(GameWorld world, long selectedId, double displayTick)
        {
            bool changed = false;
            while (displayFrames.Count > 0 && displayFrames.Peek().Tick <= displayTick + 0.000001)
            {
                BuildingDisplayFrame frame = displayFrames.Dequeue();
                foreach (BuildingDisplayState state in frame.States) displayedBuildings[state.Source.Id] = state;
                changed = true;
            }
            // 静止建筑与连接只随逻辑状态或选择变化刷新，运输插值仍然每帧执行。
            if (changed || lastAppliedTick != world.CurrentTick || lastSelectedId != selectedId)
            {
                lastSelectedId = selectedId;
                RefreshBuildings(world, selectedId);
                RefreshConnections(world);
                var selected = world.FindBuilding(selectedId);
                RangeOutline.Draw(selectionRange, selected == null ? 0 : selected.CenterX, selected == null ? 0 : selected.CenterZ,
                    selected == null ? 0 : Range(world, selected.Definition), ShowRanges && !PlacementPreviewActive && selected != null && selected.IsAlive && !selected.IsSold);
                if (selected != null && selected.Kind == BuildingKind.PiercingRailgun)
                {
                    var weapon = world.GetTurretDefinition(selected.Kind);
                    RangeOutline.DrawSector(selectionRange, selected.CenterX, selected.CenterZ, weapon.MinimumRange, weapon.Range,
                        selected.FacingDegrees, weapon.FireArcHalfDegrees, ShowRanges && !PlacementPreviewActive && selected.IsAlive && !selected.IsSold);
                }
                lastAppliedTick = world.CurrentTick;
                lastSelectedId = selectedId;
            }
            if (!externalTransferEffects) RefreshTransfers(world, displayTick);
        }

        private void RefreshBuildings(GameWorld world, long selectedId)
        {
            removedBuildings.Clear();
            foreach (KeyValuePair<long, BuildingView> item in buildingViews)
            {
                if (!displayedBuildings.TryGetValue(item.Key, out BuildingDisplayState state) || state.Sold || !state.Alive)
                {
                    if (authoredViews.Contains(item.Value)) item.Value.gameObject.SetActive(false);
                    else buildingPools[item.Value.Definition.Kind].Release(item.Value);
                    removedBuildings.Add(item.Key);
                }
            }
            foreach (long id in removedBuildings) buildingViews.Remove(id);
            foreach (BuildingDisplayState display in displayedBuildings.Values)
            {
                BuildingState state = display.Source;
                if (display.Sold || !display.Alive) continue;
                if (!buildingViews.TryGetValue(state.Id, out BuildingView view))
                {
                    view = buildingPools[state.Kind].Get();
                    view.name = state.Definition.DisplayName + " " + state.Id;
                    buildingViews.Add(state.Id, view);
                }
                view.ApplyVisualState(state, state.Id == selectedId, world.CurrentTick);
                if (state.Kind == BuildingKind.PiercingRailgun && world.Battle.State == BattleState.Preparing)
                    view.ModelAnchors?.ApplyAuthoritativeAim(state.FacingDegrees, 0);
                view.ApplyVitals(state.Id, display.Health, display.MaximumHealth, display.Energy, display.Capacity, display.Alive);
                view.ResonanceMotion?.SetCombatActive(world.Battle != null && world.Battle.State == BattleState.Combat);
            }
        }

        public void SetPreview(bool visible, int x, int z, int width, int depth, bool valid)
        {
            placementPreview.gameObject.SetActive(visible);
            if (!visible && previewRange != null) previewRange.gameObject.SetActive(false);
            if (!visible) return;
            placementPreview.position = new Vector3(x + width * 0.5f, 0.06f, z + depth * 0.5f);
            placementPreview.localScale = new Vector3(width * 0.95f, 0.08f, depth * 0.95f);
            previewProperties.SetColor("_BaseColor", valid ? new Color(0.18f, 0.8f, 0.65f) : new Color(0.95f, 0.3f, 0.26f));
            previewProperties.SetColor("_Color", valid ? Color.green : Color.red);
            previewRenderer.SetPropertyBlock(previewProperties);
        }

        public void ShowPreviewRange(GameWorld world, BuildingDefinition definition, int x, int z, bool visible, int facingDegrees = 0,
            int orientationQuarterTurns = 0)
        {
            if (previewRange == null) return;
            bool energy = definition.Kind == BuildingKind.Reactor || definition.Kind == BuildingKind.Relay || definition.Kind == BuildingKind.Battery;
            previewRange.startColor = previewRange.endColor = energy ? new Color(.3f, .95f, 1, .8f) : new Color(1, .7f, .22f, .8f);
            double centerX = x + definition.GetFootprintWidth(orientationQuarterTurns) * .5;
            double centerZ = z + definition.GetFootprintDepth(orientationQuarterTurns) * .5;
            RangeOutline.Draw(previewRange, centerX, centerZ, Range(world, definition), visible && (PlacementPreviewActive || ShowRanges));
            if (definition.Kind == BuildingKind.PiercingRailgun)
            {
                var weapon = world.GetTurretDefinition(definition.Kind);
                RangeOutline.DrawSector(previewRange, centerX, centerZ, weapon.MinimumRange,
                    weapon.Range, facingDegrees, weapon.FireArcHalfDegrees, visible && (PlacementPreviewActive || ShowRanges));
            }
        }

        private static double Range(GameWorld world, BuildingDefinition definition) =>
            definition.Resonance != null ? definition.Resonance.AbsorptionRange :
            definition.Kind == BuildingKind.Reactor || definition.Kind == BuildingKind.Relay || definition.Kind == BuildingKind.Battery ? definition.TransmissionRange :
            world.Battle != null && definition.Kind != BuildingKind.Core ? world.GetTurretDefinition(definition.Kind).Range : 0;

        private void RefreshTransfers(GameWorld world, double displayTick)
        {
            int count = Mathf.Min(world.Energy.Transfers.Count, maximumVisibleTransfers);
            while (transferViews.Count < count) transferViews.Add(transferPool.Get());
            while (transferViews.Count > count)
            {
                int last = transferViews.Count - 1;
                transferPool.Release(transferViews[last]);
                transferViews.RemoveAt(last);
            }
            for (int index = 0; index < count; index++) transferViews[index].Apply(world.Energy.Transfers[index], displayTick);
        }

        private void RefreshConnections(GameWorld world)
        {
            int count = Mathf.Min(world.Energy.Connections.Count, maximumVisibleConnections);
            while (connectionViews.Count < count) connectionViews.Add(Instantiate(connectionPrefab, connectionRoot));
            for (int index = 0; index < connectionViews.Count; index++)
            {
                LineRenderer line = connectionViews[index];
                line.gameObject.SetActive(ShowNetwork && index < count);
                if (index >= count) continue;
                EnergyConnection connection = world.Energy.Connections[index];
                BuildingState source = world.FindBuilding(connection.SourceId);
                BuildingState target = world.FindBuilding(connection.TargetId);
                if (source == null || target == null) { line.gameObject.SetActive(false); continue; }
                bool related = connection.SourceId == lastSelectedId || connection.TargetId == lastSelectedId;
                Color color = related ? new Color(0.4f, 1f, 0.8f, 1) : new Color(0.12f, 0.22f, 0.26f, 1);
                connectionProperties.SetColor("_BaseColor", color); connectionProperties.SetColor("_Color", color);
                line.SetPropertyBlock(connectionProperties);
                line.startWidth = line.endWidth = related ? 0.075f : 0.018f;
                line.SetPosition(0, new Vector3((float)source.CenterX, 0.11f, (float)source.CenterZ));
                line.SetPosition(1, new Vector3((float)target.CenterX, 0.11f, (float)target.CenterZ));
            }
        }

        private void OnDestroy()
        {
            // 场景退出时子物体可能先被销毁；销毁回调必须检查 Unity 对象是否仍然存在。
            transferPool?.Clear();
            foreach (ObjectPool<BuildingView> pool in buildingPools.Values) pool.Clear();
        }
    }
}
