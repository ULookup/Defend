using Defend.Simulation;
using SkyCity.MonsterGate;
using UnityEngine;
using UnityEngine.UI;

namespace Defend.Unity
{
    /// <summary>入口标识只读取当前正式关卡的位置与波次；没有独立计时或模拟状态。</summary>
    public sealed class FormalEntrancePresentation : MonoBehaviour
    {
        [SerializeField] private GameSessionRoot session;
        [SerializeField] private Camera worldCamera;
        [SerializeField] private int entranceIndex;
        [SerializeField] private MonsterGateController gate;
        [SerializeField] private Renderer[] surfaces;
        [SerializeField] private Text caption;
        [SerializeField] private Transform billboard;
        private MaterialPropertyBlock properties;
        private GameWorld observedWorld;
        private int lastWave = -1;
        private BattleState lastBattleState;
        private double lastPresentationTime;
        private bool lastVisible;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor"), Emission = Shader.PropertyToID("_EmissionColor");

        private void Awake()
        {
            SetVisible(false);
        }

        private void LateUpdate()
        {
            bool visible = session != null && session.World != null && !session.IsSandbox && !session.AtStartPage
                && session.Battle != null && session.Battle.Settings.Waves.Count > 0
                && entranceIndex >= 0 && entranceIndex < session.Battle.SpawnPoints.Count;
            bool reset = visible != lastVisible || observedWorld != session?.World;
            if (reset)
            {
                SetVisible(visible);
                lastVisible = visible; observedWorld = session?.World; lastWave = -1;
                if (visible)
                {
                    var point = session.Battle.SpawnPoints[entranceIndex];
                    transform.position = new Vector3(point.X + .5f, transform.position.y, point.Z + .5f);
                    transform.rotation = InwardFacingRotation(session.World, point.X, point.Z);
                    lastPresentationTime = session.PresentationTimeSeconds;
                    gate?.ResetVisualState(ResolveState(session.Battle, entranceIndex));
                }
            }
            if (!visible) return;
            if (worldCamera != null && billboard != null) billboard.rotation = worldCamera.transform.rotation;
            int wave = FormalWaveIntelPresenter.PlannedWaveNumber(session.Battle);
            var state = ResolveState(session.Battle, entranceIndex);
            if (gate != null)
            {
                // 终局立即归位。门动画没有任何开波/刷怪回调，完全读取战场状态。
                gate.SetState(state, session.Battle.IsFinished);
                double now = session.PresentationTimeSeconds;
                gate.Advance(session.Battle.IsFinished ? 0 : (float)System.Math.Max(0, now - lastPresentationTime));
                lastPresentationTime = now;
            }
            if (wave == lastWave && lastBattleState == session.Battle.State) return;
            lastWave = wave; lastBattleState = session.Battle.State;
            var entrance = session.Battle.SpawnPoints[entranceIndex];
            bool open = state == MonsterGateController.GateState.Open, announced = state == MonsterGateController.GateState.Warning;
            Color color = open ? new Color(.3f, .82f, 1f) : announced ? new Color(1, .79f, .27f) : new Color(.42f, .51f, .57f);
            if (caption != null)
            {
                int count = FormalWaveIntelPresenter.EntranceCount(session.Battle.Settings.Waves[wave - 1], entranceIndex);
                string description = session.Battle.IsFinished ? "关闭" : !entrance.IsOpenAtWave(wave)
                    ? (announced ? "即将开放" : "关闭") + " · 第 " + entrance.UnlockWave + " 波"
                    : count > 0 ? (open ? "已开放" : "备战预警") + " · " + count + " 名" : "关闭 · 本波无出兵";
                caption.text = FormalWaveIntelPresenter.EntranceName(entrance, entranceIndex) + "\n" + description;
                caption.color = color;
            }
            if (gate != null) return;
            if (properties == null) properties = new MaterialPropertyBlock();
            properties.SetColor(BaseColor, color); properties.SetColor(Emission, color * (open ? 1.5f : announced ? .7f : .08f));
            // 已有入口模型的第四个材质槽是橙色信号，结构材质保持原样。
            if (surfaces != null) foreach (var surface in surfaces)
                if (surface != null && surface.sharedMaterials.Length > 3) surface.SetPropertyBlock(properties, 3);
        }

        /// <summary>沿用正式波次预览与入口解锁配置，不推进或预测生成调度。</summary>
        public static MonsterGateController.GateState ResolveState(BattleSystem battle, int index)
        {
            if (battle == null || battle.IsFinished || battle.Settings.Waves.Count == 0 || index < 0 || index >= battle.SpawnPoints.Count)
                return MonsterGateController.GateState.Closed;
            int wave = FormalWaveIntelPresenter.PlannedWaveNumber(battle);
            var entrance = battle.SpawnPoints[index];
            if (!entrance.IsOpenAtWave(wave))
                return entrance.IsAnnouncedAtWave(wave) ? MonsterGateController.GateState.Warning : MonsterGateController.GateState.Closed;
            if (FormalWaveIntelPresenter.EntranceCount(battle.Settings.Waves[wave - 1], index) == 0)
                return MonsterGateController.GateState.Closed;
            return battle.State == BattleState.Combat ? MonsterGateController.GateState.Open : MonsterGateController.GateState.Warning;
        }

        /// <summary>模型正面为局部 -Z，沿主轴面向本关实际核心；无核心时朝地图内侧。</summary>
        public static Quaternion InwardFacingRotation(GameWorld world, int x, int z)
        {
            var core = world?.Battle == null ? null : world.FindBuilding(world.Battle.CoreBuildingId);
            if (core != null)
            {
                float deltaX = (float)core.CenterX - x - .5f, deltaZ = (float)core.CenterZ - z - .5f;
                if (Mathf.Abs(deltaX) > .001f || Mathf.Abs(deltaZ) > .001f)
                    return Quaternion.LookRotation(Mathf.Abs(deltaX) > Mathf.Abs(deltaZ)
                        ? new Vector3(-Mathf.Sign(deltaX), 0, 0) : new Vector3(0, 0, -Mathf.Sign(deltaZ)));
            }
            int width = world == null ? 40 : world.Settings.Width, depth = world == null ? 40 : world.Settings.Depth;
            int nearest = z; Vector3 inward = Vector3.forward;
            if (depth - 1 - z < nearest) { nearest = depth - 1 - z; inward = Vector3.back; }
            if (x < nearest) { nearest = x; inward = Vector3.right; }
            if (width - 1 - x < nearest) inward = Vector3.left;
            return Quaternion.LookRotation(-inward);
        }

        private void SetVisible(bool visible)
        {
            if (gate != null && gate.gameObject != gameObject) gate.gameObject.SetActive(visible);
            // 保留历史序列字段供旧场景兼容；新门体的各通道只由控制器管理。
            if (surfaces != null) foreach (var surface in surfaces)
                if (surface != null) surface.enabled = visible && gate == null;
            if (billboard != null) billboard.gameObject.SetActive(visible);
        }

        private void OnDisable()
        {
            lastWave = -1; observedWorld = null; lastVisible = false;
            SetVisible(false);
            if (surfaces != null) foreach (var surface in surfaces)
                if (surface != null && surface.sharedMaterials.Length > 3) surface.SetPropertyBlock(null, 3);
        }
    }
}
