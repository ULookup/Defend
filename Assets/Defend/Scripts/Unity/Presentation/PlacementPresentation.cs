using System.Collections.Generic;
using Defend.Simulation;
using UnityEngine;

namespace Defend.Unity
{
    /// <summary>统一驱动悬浮模型、已有供能边界，只读取权威世界。</summary>
    public sealed class PlacementPresentation : MonoBehaviour
    {
        [SerializeField] private PlacementGhostView[] ghostPrefabs;
        [SerializeField] private LineRenderer relayRangePrefab;
        private readonly Dictionary<BuildingKind, PlacementGhostView> ghosts = new Dictionary<BuildingKind, PlacementGhostView>();
        private readonly List<LineRenderer> relayRanges = new List<LineRenderer>();
        private PlacementGhostView currentGhost;
        private BuildingDefinition currentDefinition;
        private MaterialPropertyBlock properties;
        private long lastTick = -1, lastCommand = -1, lastMovingId = -1;
        private Vector3 lastTarget;
        private bool lastVisible;
        public void Configure(PlacementGhostView[] prefabs, LineRenderer range) { ghostPrefabs = prefabs; relayRangePrefab = range; }
        public void ResetSession()
        {
            if (currentGhost != null) currentGhost.gameObject.SetActive(false);
            currentGhost = null; currentDefinition = null;
            foreach (LineRenderer range in relayRanges) range.gameObject.SetActive(false);
            lastTick = lastCommand = lastMovingId = -1;
            lastVisible = false;
        }

        public void Refresh(GameWorld world, BuildingDefinition definition, int x, int z, long movingId, bool visible, bool valid, float seconds,
            int orientationQuarterTurns = 0, int facingDegrees = 0)
        {
            if (definition == null) { ResetSession(); return; }
            Vector3 target = new Vector3(x + definition.GetFootprintWidth(orientationQuarterTurns) * .5f, 0,
                z + definition.GetFootprintDepth(orientationQuarterTurns) * .5f);
            bool changed = currentDefinition != definition;
            if (changed || !visible)
            {
                if (currentGhost != null) currentGhost.gameObject.SetActive(false);
                currentGhost = null;
            }
            if (visible)
            {
                if (currentGhost == null)
                {
                    if (!ghosts.TryGetValue(definition.Kind, out currentGhost))
                    {
                        foreach (PlacementGhostView prefab in ghostPrefabs)
                            if (prefab.Kind == definition.Kind) { currentGhost = Instantiate(prefab, transform); break; }
                        if (currentGhost == null) throw new System.InvalidOperationException("缺少建筑悬浮预览：" + definition.DisplayName);
                        ghosts.Add(definition.Kind, currentGhost);
                    }
                    currentGhost.ResetPose(target);
                    currentGhost.gameObject.SetActive(true);
                }
                currentGhost.Refresh(target, valid, seconds, orientationQuarterTurns, facingDegrees);
            }
            if (changed || lastTick != world.CurrentTick || lastCommand != world.LastCommandResult.Sequence || lastTarget != target || lastMovingId != movingId || lastVisible != visible)
                RefreshRelayRanges(world, definition, target, movingId, visible);
            currentDefinition = definition; lastTick = world.CurrentTick; lastCommand = world.LastCommandResult.Sequence;
            lastTarget = target; lastMovingId = movingId; lastVisible = visible;
        }

        private void RefreshRelayRanges(GameWorld world, BuildingDefinition candidate, Vector3 target, long movingId, bool visible)
        {
            if (properties == null) properties = new MaterialPropertyBlock();
            int count = 0;
            foreach (BuildingState relay in world.Buildings)
            {
                if (!EnergyCoverageQuery.IsSource(relay)) continue;
                if (count == relayRanges.Count) relayRanges.Add(Instantiate(relayRangePrefab, transform));
                LineRenderer range = relayRanges[count++];
                bool moving = relay.Id == movingId;
                // 范围只说明距离允许运输；不把覆盖误报为实际收到供电。
                bool related = !moving && visible && candidate.Kind != BuildingKind.Reactor &&
                    (candidate.Resonance == null || candidate.Resonance.RequiresEnergy) && world.Energy.DirectEfficiencyAt(relay, target.x, target.z) > 0;
                Color color = moving ? new Color(.65f, .75f, .8f, .38f) : related ? new Color(.75f, .95f, 1f, .7f) : new Color(.66f, .8f, .85f, .5f);
                properties.SetColor("_BaseColor", color);
                properties.SetFloat("_Dashed", moving ? 1 : 0);
                range.SetPropertyBlock(properties);
                range.startWidth = range.endWidth = related ? .05f : .04f;
                RangeOutline.Draw(range, relay.CenterX, relay.CenterZ, relay.Definition.TransmissionRange, true);
            }
            for (int index = count; index < relayRanges.Count; index++) relayRanges[index].gameObject.SetActive(false);
        }

    }
}
