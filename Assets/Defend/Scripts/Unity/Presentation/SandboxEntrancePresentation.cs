using Defend.Simulation;
using SkyCity.MonsterGate;
using UnityEngine;

namespace Defend.Unity
{
    /// <summary>沙盒出生点按稳定身份读取已应用分配；面板草稿与门动画均不能改变生成计划。</summary>
    public sealed class SandboxEntrancePresentation : MonoBehaviour
    {
        [SerializeField] private MonsterGateController gate;
        private GameSessionRoot session;
        private GameWorld observedWorld;
        private long spawnPointId, lastRevision = -1;
        private int lastWave = -1;
        private BattleState lastBattleState;
        private double lastPresentationTime;

        private void Awake()
        {
            if (gate == null) gate = GetComponentInChildren<MonsterGateController>(true);
        }

        public void Initialize(GameSessionRoot root, long pointId)
        {
            if (session != root || spawnPointId != pointId)
            {
                observedWorld = null;
                lastRevision = -1;
            }
            session = root;
            spawnPointId = pointId;
        }

        private void LateUpdate()
        {
            if (session == null || session.World == null || session.Battle == null || !session.IsSandbox || session.AtStartPage)
            {
                gameObject.SetActive(false);
                return;
            }
            var world = session.World;
            var battle = session.Battle;
            bool reset = observedWorld != world;
            if (reset || lastRevision != world.Revision || lastBattleState != battle.State || lastWave != battle.WaveNumber)
            {
                SandboxSpawnPoint point = null;
                foreach (var candidate in battle.SandboxSpawnPoints)
                    if (candidate.Id == spawnPointId) { point = candidate; break; }
                if (point == null) { gameObject.SetActive(false); return; }
                transform.SetPositionAndRotation(new Vector3(point.X + .5f, .04f, point.Z + .5f),
                    FormalEntrancePresentation.InwardFacingRotation(world, point.X, point.Z));
                var state = ResolveState(battle, spawnPointId);
                if (reset)
                {
                    gate?.ResetVisualState(state);
                    lastPresentationTime = session.PresentationTimeSeconds;
                }
                // 沙盒编辑处于暂停中，应用分配后直接给出正确预警姿态；动画时间仍保持冻结。
                else gate?.SetState(state, battle.IsFinished || battle.State == BattleState.Preparing);
                observedWorld = world;
                lastRevision = world.Revision;
                lastBattleState = battle.State;
                lastWave = battle.WaveNumber;
            }
            double now = session.PresentationTimeSeconds;
            gate?.Advance(battle.IsFinished ? 0 : (float)System.Math.Max(0, now - lastPresentationTime));
            lastPresentationTime = now;
        }

        public static MonsterGateController.GateState ResolveState(BattleSystem battle, long pointId)
        {
            if (battle == null || battle.IsFinished || battle.State == BattleState.WaveCleared)
                return MonsterGateController.GateState.Closed;
            var groups = battle.State == BattleState.Combat ? battle.CurrentSandboxPlan?.Groups : battle.SandboxGroups;
            if (groups != null)
                foreach (var group in groups)
                    foreach (var allocation in group.Allocations)
                        if (allocation.SpawnPointId == pointId && allocation.Count > 0)
                            return battle.State == BattleState.Combat ? MonsterGateController.GateState.Open : MonsterGateController.GateState.Warning;
            return MonsterGateController.GateState.Closed;
        }

        private void OnDisable()
        {
            observedWorld = null;
            lastRevision = -1;
            lastWave = -1;
        }
    }
}
