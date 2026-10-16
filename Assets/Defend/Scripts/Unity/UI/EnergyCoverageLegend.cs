using Defend.Simulation;
using UnityEngine;
using UnityEngine.UI;

namespace Defend.Unity
{
    /// <summary>摆放期间解释效率底图和落点最佳来源；连接与储能描述的是来源当前真实状态。</summary>
    public sealed class EnergyCoverageLegend : MonoBehaviour
    {
        [SerializeField] private Text[] scaleLabels;
        [SerializeField] private RectTransform colorStrip;
        [SerializeField] private Text pointLabel, sourceState;
        private BuildingState lastSource;
        private GameWorld lastWorld;
        private long lastTick = -1, lastCommand = -1;
        private SupplyAssessment assessment;
        private double lastMinimum = -1;
        public void Configure(RectTransform strip, Text[] labels, Text point, Text state)
        { colorStrip = strip; scaleLabels = labels; pointLabel = point; sourceState = state; }

        public void Refresh(GameSessionRoot session)
        {
            GridInputController input = session.GridInput;
            bool show = input.HasPlacement && input.PreviewDefinition != null && !session.BlocksWorldInput;
            gameObject.SetActive(show);
            if (!show) return;
            BuildingDefinition definition = input.PreviewDefinition;
            BuildingDefinition sourceDefinition = definition;
            if (definition.Kind != BuildingKind.Reactor && definition.Kind != BuildingKind.Relay && definition.Kind != BuildingKind.Battery)
                sourceDefinition = session.FindEffectiveDefinition(BuildingKind.Relay);
            double minimum = EnergyCoverageQuery.MinimumEfficiency(session.World, sourceDefinition);
            if (minimum != lastMinimum) RefreshScale(minimum);
            if (!input.HasVisiblePlacementPreview)
            {
                pointLabel.text = "将鼠标移到地图，查看落点运输效率";
                sourceState.text = "底色取已有来源的最高单次运输效率";
                return;
            }
            double x = input.PreviewX + input.PreviewWidth * .5, z = input.PreviewZ + input.PreviewDepth * .5;
            BuildingState source = EnergyCoverageQuery.BestSource(session.World, x, z, input.MovingBuildingId, out double efficiency);
            if (source == null)
            {
                pointLabel.text = definition.Kind == BuildingKind.Reactor ? "落点附近：没有已有能源运输覆盖" : "此落点不在已有供能建筑运输范围内";
                sourceState.text = "可调整落点，或增建供能建筑";
                return;
            }
            pointLabel.text = (definition.Kind == BuildingKind.Reactor ? "附近覆盖：" : "已有最佳来源：") + source.Definition.DisplayName
                + "（" + source.X + ", " + source.Z + "）  " + (efficiency * 100).ToString("0.##") + "%";
            if (lastSource != source || lastWorld != session.World || lastTick != session.World.CurrentTick || lastCommand != session.World.LastCommandResult.Sequence)
            {
                assessment = SupplyAssessment.Read(session.World, source);
                lastSource = source; lastWorld = session.World; lastTick = session.World.CurrentTick; lastCommand = session.World.LastCommandResult.Sequence;
            }
            sourceState.text = "来源当前：" + (assessment.Connected ? "已接发电网络" : "未接发电网络")
                + " · 储能 " + (source.StoredEnergy / 1000d).ToString("0.#");
        }

        private void RefreshScale(double minimum)
        {
            double[] efficiencies = { 1, .9, .8, .7, minimum };
            for (int i = 0; i < scaleLabels.Length; i++)
            {
                Text label = scaleLabels[i]; double efficiency = efficiencies[i];
                bool visible = i == 0 || (minimum < .9999 && (i == 4 || efficiency > minimum + .025));
                label.gameObject.SetActive(visible);
                label.text = (efficiency * 100).ToString("0.#") + "%";
                float fraction = minimum >= .9999 ? 0 : (float)((1 - efficiency) / (1 - minimum));
                label.rectTransform.anchoredPosition = new Vector2(fraction * colorStrip.rect.width, -14);
            }
            lastMinimum = minimum;
        }
    }
}
