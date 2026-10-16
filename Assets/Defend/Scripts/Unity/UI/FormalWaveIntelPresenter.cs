using System;
using System.Text;
using Defend.Simulation;
using UnityEngine;
using UnityEngine.UI;

namespace Defend.Unity
{
    /// <summary>复用战场情报卡展示配置中的真实队伍；不持有或推进波次状态。</summary>
    public sealed class FormalWaveIntelPresenter : MonoBehaviour
    {
        [SerializeField] private Button toggle;
        [SerializeField] private GameObject panel;
        [SerializeField] private Text heading, enemyHeading, briefing, schedule, toggleHint;
        [SerializeField] private Button[] entranceButtons;
        [SerializeField] private Text[] entranceLabels, enemyNames, enemyCounts;
        [SerializeField] private Image[] enemyPortraits;
        private GameSessionRoot session;
        private GameWorld observedWorld;
        private WaveDefinition plan;
        private int displayedWave = -1, selectedEntrance = -1;
        private bool expanded;
        private long observedCommand = -1;
        private float commandFeedbackUntil;
        private readonly int[] counts = new int[5];
        private readonly StringBuilder builder = new StringBuilder(256);
        private static readonly Color Open = new Color(1f, .68f, .36f), Upcoming = new Color(.98f, .81f, .43f), Closed = new Color(.63f, .69f, .74f);
        public string Summary { get; private set; } = "";
        public bool ShowCommandFeedback => Time.unscaledTime < commandFeedbackUntil;

        private void Awake()
        {
            toggle.onClick.AddListener(Toggle);
            for (int i = 0; i < entranceButtons.Length; i++)
            {
                int index = i;
                entranceButtons[i].onClick.AddListener(() => SelectEntrance(index));
            }
            panel.SetActive(false);
        }

        private void OnDisable() { expanded = false; if (panel != null) panel.SetActive(false); }
        public void Close() { expanded = false; if (panel != null) panel.SetActive(false); }
        private void Toggle()
        {
            if (session == null || session.IsSandbox || session.BlocksWorldInput || session.GridInput.HasPlacement) return;
            expanded = !expanded;
            if (expanded) { selectedEntrance = -1; Render(); }
            panel.SetActive(expanded);
        }
        private void SelectEntrance(int index) { selectedEntrance = selectedEntrance == index ? -1 : index; Render(); }

        public void Refresh(GameSessionRoot root)
        {
            session = root;
            bool available = !root.IsSandbox && root.Battle != null && root.Battle.Settings.Waves.Count > 0;
            toggle.interactable = available && !root.BlocksWorldInput && !root.GridInput.HasPlacement;
            toggleHint.gameObject.SetActive(available && !root.GridInput.HasPlacement);
            if (!available) { Summary = ""; Close(); return; }
            int wave = PlannedWaveNumber(root.Battle);
            if (!ReferenceEquals(observedWorld, root.World))
            {
                observedWorld = root.World; displayedWave = -1; selectedEntrance = -1;
                observedCommand = root.World.LastCommandResult.Sequence; commandFeedbackUntil = 0; Close();
            }
            if (observedCommand != root.World.LastCommandResult.Sequence)
            {
                observedCommand = root.World.LastCommandResult.Sequence;
                commandFeedbackUntil = Time.unscaledTime + 3;
            }
            if (displayedWave != wave)
            {
                displayedWave = wave; selectedEntrance = -1;
                plan = root.Battle.Settings.Waves[wave - 1];
                Render();
            }
            if (root.BlocksWorldInput || root.GridInput.HasPlacement) Close();
            toggleHint.text = expanded ? "收起情报  ‹" : "展开情报  ›";
        }

        public static int PlannedWaveNumber(BattleSystem battle)
        {
            int wave = battle.WaveNumber + (battle.State == BattleState.Preparing || battle.State == BattleState.WaveCleared ? 1 : 0);
            return Math.Max(1, Math.Min(battle.Settings.Waves.Count, wave));
        }

        public static string EntranceName(SpawnPoint entrance, int index)
            => string.IsNullOrWhiteSpace(entrance.DisplayName) ? "入口 " + (index + 1) : entrance.DisplayName;

        public static int EntranceCount(WaveDefinition wave, int index)
        {
            int total = 0;
            foreach (var group in wave.Groups) if (group.EntranceIndex == index) total += group.Count;
            return total;
        }

        public static string EntranceState(SpawnPoint entrance, int wave)
            => entrance.IsOpenAtWave(wave) ? "已开放" : entrance.IsAnnouncedAtWave(wave) ? "即将开放" : "关闭";

        private void Render()
        {
            if (session == null || plan == null) return;
            var battle = session.Battle;
            Array.Clear(counts, 0, counts.Length);
            foreach (var group in plan.Groups)
                if (selectedEntrance < 0 || group.EntranceIndex == selectedEntrance) counts[(int)group.Kind] += group.Count;
            if (plan.Groups.Count == 0 && selectedEntrance < 0)
                for (int i = 0; i < plan.EnemyCount; i++) counts[(int)plan.EnemyPattern[i % plan.EnemyPattern.Count]]++;
            heading.text = "第 " + displayedWave + " 波 · " + plan.EnemyCount + " 名";
            enemyHeading.text = selectedEntrance < 0 ? "全部入口" : EntranceName(battle.SpawnPoints[selectedEntrance], selectedEntrance);
            for (int i = 0; i < counts.Length; i++)
            {
                enemyNames[i].text = battle.Settings.GetEnemy((EnemyKind)i).DisplayName;
                enemyCounts[i].text = counts[i].ToString();
                enemyPortraits[i].color = counts[i] > 0 ? Color.white : new Color(.48f, .48f, .48f, .65f);
            }
            builder.Clear(); int active = 0;
            for (int i = 0; i < entranceButtons.Length; i++)
            {
                bool exists = i < battle.SpawnPoints.Count; entranceButtons[i].gameObject.SetActive(exists);
                if (!exists) continue;
                var entrance = battle.SpawnPoints[i]; int count = EntranceCount(plan, i);
                if (count > 0) active++;
                entranceLabels[i].text = EntranceName(entrance, i) + "  ·  " + EntranceState(entrance, displayedWave) + "  ·  "
                    + (entrance.IsOpenAtWave(displayedWave) ? count + " 名" : "第 " + entrance.UnlockWave + " 波");
                entranceLabels[i].color = selectedEntrance == i ? Color.white : entrance.IsOpenAtWave(displayedWave) ? Open : entrance.IsAnnouncedAtWave(displayedWave) ? Upcoming : Closed;
                if (!entrance.IsOpenAtWave(displayedWave) && entrance.IsAnnouncedAtWave(displayedWave))
                {
                    if (builder.Length > 0) builder.Append(" · ");
                    builder.Append(EntranceName(entrance, i)).Append("第 ").Append(entrance.UnlockWave).Append(" 波开放");
                }
            }
            Summary = "第 " + displayedWave + " 波：" + plan.EnemyCount + " 名 · " + active + " 处入口\n"
                + (builder.Length > 0 ? builder.ToString() : "点击战场情报查看兵种与入口");
            briefing.text = plan.Briefing;
            schedule.text = "预计发兵 " + (plan.PlannedSpawnDurationTicks / (double)session.World.Settings.TicksPerSecond).ToString("0.#")
                + " 秒  ·  固定奖励 " + plan.ClearReward;
        }
    }
}
