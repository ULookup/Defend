using System;
using System.Collections.Generic;
using Defend.Simulation;
using UnityEngine;

namespace Defend.Unity
{
    /// <summary>
    /// 显示单建筑或已有供能网络的逐格运输效率，不推断实际到货或可用库存。
    /// </summary>
    public sealed class EnergyLossGridOverlay : MonoBehaviour
    {
        [SerializeField]
        private float cellHeight = 0.05f;

        // 使用网格坐标保存生成的 Cell。
        private readonly Dictionary<Vector2Int, Cell> cells =
            new Dictionary<Vector2Int, Cell>();

        private GameWorld world;
        private MaterialPropertyBlock properties;
        private GameObject cellPrefab;
        private GameObject cellsRoot;
        private int width;
        private int depth;

        private BuildingDefinition lastDefinition;
        private double lastCenterX;
        private double lastCenterZ;
        private bool lastDisplayed;
        private bool showingNetwork;
        private long lastExcludedSourceId;
        private readonly List<SourcePosition> networkSources = new List<SourcePosition>();
        private float minimumEfficiency;
        private bool placementComposed;
        private BuildingDefinition lastPreviewDefinition;
        private double lastPreviewCenterX, lastPreviewCenterZ;

        private readonly struct SourcePosition
        {
            private readonly long id;
            private readonly BuildingDefinition definition;
            private readonly double x, z;

            public SourcePosition(BuildingState source)
            { id = source.Id; definition = source.Definition; x = source.CenterX; z = source.CenterZ; }

            public bool Matches(BuildingState source) => id == source.Id && definition == source.Definition &&
                x == source.CenterX && z == source.CenterZ;
        }

        private static readonly int ShouldBeDisplayed =
            Shader.PropertyToID("_ShouldBeDisplayed");

        private static readonly int Percentage =
            Shader.PropertyToID("_Percentage");
        private static readonly int MinimumPercentage = Shader.PropertyToID("_MinimumEfficiency");

        private sealed class Cell
        {
            public readonly GameObject Instance;
            public readonly Renderer Renderer;
            public readonly int X;
            public readonly int Z;
            public float NetworkEfficiency;

            public Cell(
                GameObject instance,
                Renderer renderer,
                int x,
                int z)
            {
                Instance = instance;
                Renderer = renderer;
                X = x;
                Z = z;
            }
        }

        /// <summary>
        /// 供 GameSessionRoot 在创建模拟世界后调用。
        /// </summary>
        public void Initialize(
            GameWorld value,
            GameObject prefab)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            if (prefab == null)
            {
                throw new ArgumentNullException(nameof(prefab));
            }

            if (prefab.GetComponentInChildren<Renderer>(true) == null)
            {
                throw new InvalidOperationException(
                    "EnergyLossGridOverlay 的格子 Prefab 必须包含 Renderer。");
            }

            world = value;

            bool needsRebuild =
                cells.Count > 0 &&
                (
                    width != value.Settings.Width ||
                    depth != value.Settings.Depth ||
                    cellPrefab != prefab
                );

            if (needsRebuild)
            {
                foreach (Cell cell in cells.Values)
                {
                    Destroy(cell.Instance);
                }

                cells.Clear();

                if (cellsRoot != null)
                {
                    Destroy(cellsRoot);
                    cellsRoot = null;
                }
            }

            cellPrefab = prefab;
            width = value.Settings.Width;
            depth = value.Settings.Depth;

            if (properties == null)
            {
                properties = new MaterialPropertyBlock();
            }

            EnsureCellsRoot();
            EnsureCells();

            HideAll();

            lastDefinition = null;
            lastDisplayed = false;
            showingNetwork = false;
            placementComposed = false;
            networkSources.Clear();
        }

        /// <summary>
        /// 已放置建筑使用真实位置；损毁、出售或取消选择后隐藏。
        /// </summary>
        public void Refresh(
            GameWorld value,
            long selectedId)
        {
            BuildingState source = value?.FindBuilding(selectedId);
            if (source == null || !source.IsAlive || source.IsSold) { Hide(); return; }
            RefreshAtPosition(value, source.Definition, source.CenterX, source.CenterZ);
        }

        /// <summary>只读候选位置。移动预览不修改建筑位置、库存或在途能量。</summary>
        public void RefreshPreview(GameWorld value, BuildingDefinition definition, int x, int z, int orientationQuarterTurns = 0)
        {
            if (definition == null) { Hide(); return; }
            RefreshAtPosition(value, definition, x + definition.GetFootprintWidth(orientationQuarterTurns) * 0.5,
                z + definition.GetFootprintDepth(orientationQuarterTurns) * 0.5);
        }

        /// <summary>摆放时显示所有现有供能建筑的覆盖；被移动的原建筑不参与最优来源判断。</summary>
        public void RefreshNetwork(GameWorld value, long excludedSourceId)
        {
            if (value == null || value != world || cells.Count == 0) { Hide(); return; }
            // 只比较影响覆盖的源状态。库存、运输进度与无关命令不会使整张底图重算。
            if (lastDisplayed && showingNetwork && lastExcludedSourceId == excludedSourceId &&
                NetworkSourcesUnchanged(value, excludedSourceId)) return;

            networkSources.Clear();
            placementComposed = false;
            minimumEfficiency = 1f;
            foreach (var source in value.Buildings)
            {
                if (!EnergyCoverageQuery.IsSource(source) || source.Id == excludedSourceId) continue;
                networkSources.Add(new SourcePosition(source));
                minimumEfficiency = Mathf.Min(minimumEfficiency,
                    (float)EnergyCoverageQuery.MinimumEfficiency(value, source.Definition));
            }
            foreach (Cell cell in cells.Values)
            {
                EnergyCoverageQuery.BestSource(value, cell.X + .5, cell.Z + .5, excludedSourceId,
                    out double percentage);
                cell.NetworkEfficiency = (float)percentage;
                SetCellDisplay(cell, percentage > 0, (float)percentage);
            }
            lastDefinition = null;
            lastDisplayed = true;
            showingNetwork = true;
            lastExcludedSourceId = excludedSourceId;
        }

        /// <summary>候选范围内突出候选本身的效率，外围保留已有覆盖；共用一层格子，不叠加颜色或效率。</summary>
        public void RefreshPlacement(GameWorld value, long excludedSourceId, BuildingDefinition candidate, int x, int z, bool visible,
            int orientationQuarterTurns = 0)
        {
            RefreshNetwork(value, excludedSourceId);
            if (!lastDisplayed || value != world) return;
            if (!visible || candidate == null || EnergyCoverageQuery.MinimumEfficiency(value, candidate) <= 0) candidate = null;
            double centerX = candidate == null ? 0 : x + candidate.GetFootprintWidth(orientationQuarterTurns) * .5;
            double centerZ = candidate == null ? 0 : z + candidate.GetFootprintDepth(orientationQuarterTurns) * .5;
            if (placementComposed && lastPreviewDefinition == candidate && lastPreviewCenterX == centerX && lastPreviewCenterZ == centerZ) return;
            if (candidate != null) minimumEfficiency = (float)EnergyCoverageQuery.MinimumEfficiency(value, candidate);
            foreach (Cell cell in cells.Values)
            {
                double preview = candidate == null ? 0 : value.Energy.DirectEfficiencyAt(candidate, centerX, centerZ, cell.X + .5, cell.Z + .5);
                float efficiency = preview > 0 ? (float)preview : cell.NetworkEfficiency;
                float opacity = candidate == null ? .36f : preview > 0 ? .44f : .22f;
                SetCellDisplay(cell, efficiency > 0, efficiency, opacity);
            }
            lastPreviewDefinition = candidate; lastPreviewCenterX = centerX; lastPreviewCenterZ = centerZ;
            placementComposed = true;
        }

        private bool NetworkSourcesUnchanged(GameWorld value, long excludedSourceId)
        {
            int index = 0;
            foreach (var source in value.Buildings)
            {
                if (!EnergyCoverageQuery.IsSource(source) || source.Id == excludedSourceId) continue;
                if (index >= networkSources.Count || !networkSources[index].Matches(source)) return false;
                index++;
            }
            return index == networkSources.Count;
        }

        private void RefreshAtPosition(GameWorld value, BuildingDefinition definition, double centerX, double centerZ)
        {
            if (value == null || value != world || cells.Count == 0 ||
                (definition.Kind != BuildingKind.Reactor && definition.Kind != BuildingKind.Relay &&
                 definition.Kind != BuildingKind.Battery))
            { Hide(); return; }

            // 鼠标在同一格内移动时不重复更新材质；换格、开关或切换建筑后更新。
            if (lastDisplayed && !showingNetwork && lastDefinition == definition && lastCenterX == centerX && lastCenterZ == centerZ) return;
            lastDefinition = definition;
            lastCenterX = centerX;
            lastCenterZ = centerZ;
            lastDisplayed = true;
            showingNetwork = false;
            placementComposed = false;
            minimumEfficiency = (float)EnergyCoverageQuery.MinimumEfficiency(value, definition);

            foreach (Cell cell in cells.Values)
            {
                double percentage =
                    value.Energy.DirectEfficiencyAt(
                        definition,
                        centerX,
                        centerZ,
                        cell.X + 0.5,
                        cell.Z + 0.5);

                SetCellDisplay(
                    cell,
                    percentage > 0,
                    (float)percentage);
            }
        }

        public void Hide()
        {
            if (lastDisplayed) HideAll();
            lastDefinition = null;
            lastDisplayed = false;
            showingNetwork = false;
            placementComposed = false;
        }

        private void EnsureCells()
        {
            if (cells.Count == width * depth)
            {
                return;
            }

            EnsureCellsRoot();

            for (int z = 0; z < depth; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    GameObject instance =
                        Instantiate(cellPrefab);

                    instance.name =
                        "EnergyLossCell " + x + "," + z;

                    // 所有 Cell 都挂到统一根对象下。
                    instance.transform.SetParent(
                        cellsRoot.transform,
                        true);

                    instance.SetActive(true);

                    Renderer renderer =
                        instance.GetComponentInChildren<Renderer>(true);

                    if (renderer == null)
                    {
                        Destroy(instance);

                        throw new InvalidOperationException(
                            "EnergyLossGridOverlay 的 cellPrefab 必须包含 Renderer。");
                    }

                    Vector3 cellPosition =
                        new Vector3(
                            x + 0.5f,
                            cellHeight,
                            z + 0.5f);

                    Quaternion cellRotation =
                        Quaternion.Euler(
                            -90f,
                            0f,
                            0f);

                    // 根对象使用世界坐标，避免父级变换影响位置。
                    instance.transform.SetPositionAndRotation(
                        cellPosition,
                        cellRotation);

                    instance.transform.localPosition =
                        cellPosition;

                    renderer.transform.SetPositionAndRotation(
                        cellPosition,
                        cellRotation);

                    Vector2Int coordinate =
                        new Vector2Int(x, z);

                    cells.Add(
                        coordinate,
                        new Cell(
                            instance,
                            renderer,
                            x,
                            z));
                }
            }
        }

        private void EnsureCellsRoot()
        {
            if (cellsRoot != null)
            {
                return;
            }

            cellsRoot =
                new GameObject(
                    "EnergyLossGridOverlayCells");

            cellsRoot.transform.SetPositionAndRotation(
                Vector3.zero,
                Quaternion.identity);

            cellsRoot.transform.localScale =
                Vector3.one;
        }

        private void HideAll()
        {
            foreach (Cell cell in cells.Values)
            {
                SetCellDisplay(
                    cell,
                    false,
                    0f);
            }
        }

        /// <summary>
        /// 同时设置材质参数和 GameObject 的 active 状态。
        /// </summary>
        private void SetCellDisplay(
            Cell cell,
            bool shouldDisplay,
            float percentage, float opacity = .36f)
        {
            cell.Instance.SetActive(
                shouldDisplay);

            properties.Clear();

            properties.SetFloat(
                ShouldBeDisplayed,
                shouldDisplay ? 1f : 0f);

            properties.SetFloat(
                Percentage,
                shouldDisplay ? percentage : 0f);
            properties.SetFloat(MinimumPercentage, minimumEfficiency);
            properties.SetFloat("_FillOpacity", opacity);

            cell.Renderer.SetPropertyBlock(
                properties);
        }

        private void OnDestroy()
        {
            cells.Clear();

            if (cellsRoot != null)
            {
                Destroy(cellsRoot);
                cellsRoot = null;
            }
        }
    }
}
