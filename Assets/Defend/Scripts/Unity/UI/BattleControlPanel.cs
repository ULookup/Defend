using System;
using System.Text;
using Defend.Simulation;
using UnityEngine;
using UnityEngine.UI;

namespace Defend.Unity
{
    /// <summary>绑定场景中已经保存的战斗界面。按钮提交命令，文字只展示世界状态。</summary>
    public sealed class BattleControlPanel : MonoBehaviour
    {
        [SerializeField] private Text resourcesText;
        [SerializeField] private Text waveText;
        [SerializeField] private Text coreText;
        [SerializeField] private Text selectionTitle;
        [SerializeField] private Text selectionText;
        [SerializeField] private Text outcomeText;
        [SerializeField] private Text feedbackText;
        [SerializeField] private Text modeText;
        [SerializeField] private Text pauseLabel;
        [SerializeField] private Text waveButtonLabel;
        [SerializeField] private Image energyFill;
        [SerializeField] private Button[] placementButtons;
        [SerializeField] private Text[] placementLabels;
        [SerializeField] private Button[] priorityButtons;
        [SerializeField] private Button moveButton;
        [SerializeField] private Button sellButton;
        [SerializeField] private Button repairButton;
        [SerializeField] private Button pauseButton;
        [SerializeField] private Button startWaveButton;
        [SerializeField] private Button restartButton;
        [SerializeField] private Button cancelButton;
        [SerializeField] private GameObject resultPanel;
        [SerializeField] private Text resultDetails;
        [SerializeField] private Text enemyIntentText;
        private GameSessionRoot session;
        private readonly StringBuilder buffer = new StringBuilder(512);
        private static readonly BuildingKind[] ShopKinds = { BuildingKind.Reactor, BuildingKind.Relay, BuildingKind.Turret, BuildingKind.BurstTurret, BuildingKind.AreaTurret };

        public void Initialize(GameSessionRoot root)
        {
            if (resourcesText == null || waveText == null || coreText == null || selectionTitle == null || selectionText == null ||
                outcomeText == null || feedbackText == null || modeText == null || pauseLabel == null || waveButtonLabel == null ||
                energyFill == null || moveButton == null || sellButton == null || repairButton == null || pauseButton == null ||
                startWaveButton == null || restartButton == null || cancelButton == null || placementButtons == null ||
                (placementButtons.Length != 3 && placementButtons.Length != 5) || placementLabels == null || placementLabels.Length != placementButtons.Length ||
                priorityButtons == null || priorityButtons.Length != 3)
                throw new InvalidOperationException("战斗面板引用不完整，请检查保存的 UI Prefab。");
            Unbind();
            session = root;
            placementButtons[0].onClick.AddListener(PlaceReactor);
            placementButtons[1].onClick.AddListener(PlaceRelay);
            placementButtons[2].onClick.AddListener(PlaceTurret);
            if (placementButtons.Length == 5)
            {
                placementButtons[3].onClick.AddListener(PlaceBurstTurret);
                placementButtons[4].onClick.AddListener(PlaceAreaTurret);
            }
            priorityButtons[0].onClick.AddListener(LowPriority);
            priorityButtons[1].onClick.AddListener(NormalPriority);
            priorityButtons[2].onClick.AddListener(HighPriority);
            moveButton.onClick.AddListener(Move);
            sellButton.onClick.AddListener(session.SellSelected);
            repairButton.onClick.AddListener(session.RepairSelected);
            pauseButton.onClick.AddListener(session.TogglePause);
            startWaveButton.onClick.AddListener(AdvanceWave);
            restartButton.onClick.AddListener(session.Restart);
            cancelButton.onClick.AddListener(Cancel);
            for (int index = 0; index < placementButtons.Length; index++)
            {
                BuildingDefinition definition = session.FindDefinition(ShopKinds[index]).CreateDefinition();
                string role = index == 2 ? "持续攻击" : index == 3 ? "集中爆发" : index == 4 ? "范围攻击" : index == 0 ? "产生能量" : "接力供电";
                placementLabels[index].text = definition.DisplayName + " · " + definition.PurchaseCost + "\n" + role;
            }
        }

        public void Refresh()
        {
            if (session?.Battle == null) return;
            BattleSystem battle = session.Battle;
            bool finished = battle.State == BattleState.Victory || battle.State == BattleState.Defeat;
            int buildings = 0, enemies = 0;
            foreach (BuildingState building in session.World.Buildings) if (!building.IsSold && building.IsAlive) buildings++;
            foreach (EnemyState enemy in battle.Enemies) if (enemy.IsAlive) enemies++;
            resourcesText.text = "资金  " + session.World.Credits + "     建筑  " + buildings;
            waveText.text = "第 " + battle.WaveNumber + " / " + battle.Settings.Waves.Count + " 波    敌人 " + enemies + "    尚未到达 " + battle.RemainingToSpawn;
            BuildingState core = session.World.FindBuilding(battle.CoreBuildingId);
            coreText.text = core == null ? "核心未部署" : "核心耐久  " + core.Health + " / " + core.Definition.MaximumHealth +
                "     护盾  " + Energy(core.StoredEnergy) + " / " + Energy(core.Definition.EnergyCapacity);
            outcomeText.text = StateMessage(battle);
            if (!finished && battle.Settings.Waves.Count > 5)
            {
                int index = battle.State == BattleState.Combat ? battle.WaveNumber - 1 : battle.WaveNumber;
                if (index < battle.Settings.Waves.Count)
                    outcomeText.text += "\n\n" + battle.Settings.Waves[index].Briefing;
            }
            if (resultPanel != null)
            {
                resultPanel.SetActive(finished);
                if (finished) RefreshResult();
            }
            feedbackText.text = session.Feedback;
            modeText.text = session.GridInput.ModeDescription;
            pauseLabel.text = session.Paused ? "继续战斗" : "暂停";
            waveButtonLabel.text = battle.State == BattleState.WaveCleared ? "继续准备" : "开始第 " + (battle.WaveNumber + 1) + " 波";
            startWaveButton.interactable = battle.State == BattleState.Preparing || battle.State == BattleState.WaveCleared;
            pauseButton.interactable = !finished;
            cancelButton.interactable = session.GridInput.HasPlacement;
            foreach (Button button in placementButtons) button.interactable = session.World.CanEditBuildings;
            RefreshSelection(finished);
            RefreshEnemyIntent();
        }

        private void RefreshSelection(bool finished)
        {
            BuildingState building = session.SelectedBuilding;
            bool available = session.World.CanEditBuildings && building != null && !building.IsSold && building.IsAlive;
            moveButton.interactable = available && building.Kind != BuildingKind.Core;
            sellButton.interactable = available && building.Kind != BuildingKind.Core;
            var quote = session.World.RepairQuote(building?.Id ?? 0);
            repairButton.interactable = available && quote.CanRepair && quote.Cost <= session.World.Credits;
            foreach (Button button in priorityButtons) button.interactable = available && building.IsConsumer && building.IsAlive;
            if (building == null || building.IsSold || !building.IsAlive)
            {
                selectionTitle.text = "未选择建筑";
                selectionText.text = "单击建筑查看供电与实际到货。\n圆环：传输或攻击范围\n亮线：选中建筑的供电连接\n\n连射炮：持续拦截快速敌人\n重击炮：集中打击高耐久敌人\n范围炮：打击密集敌群\n\n红色敌人：普通\n黄色敌人：快速\n紫色大体型：重装\n\n维修即时付费，波末不补耐久。\n岩体不能建造或拆除。\n预览绿色可放置，红色不可放置。";
                if (session.Battle.Settings.EnemyBehavior.Enabled)
                    selectionText.text = "单击建筑查看供电与重装目标。\n圆环：传输或攻击范围\n亮线：选中建筑的供电连接\n\n镇载者始终向核心推进；\n其他敌人按类型选择战略目标。\n\n中继损毁会影响后续供给，\n炮塔仍可消耗已有缓存。\n\n提高供给优先级只调整能源份额；\n炮塔仍攻击射程内最近的敌人。\n\n保护节点或建立独立备用路线。\n维修即时付费，波末不补耐久。\n岩体不能建造或拆除。";
                energyFill.fillAmount = 0;
                return;
            }
            selectionTitle.text = building.Definition.DisplayName;
            buffer.Clear();
            buffer.Append("运行中");
            buffer.Append("\n耐久  ").Append(building.Health).Append(" / ").Append(building.Definition.MaximumHealth);
            buffer.Append("\n储能  ").Append(Energy(building.StoredEnergy)).Append(" / ").Append(Energy(building.Definition.EnergyCapacity));
            buffer.Append("\n已预约供给  ").Append(Energy(building.IsConsumer ? building.CommittedIncoming : building.ReservedIncoming)).Append("（尚未到货）");
            buffer.Append("\n累计实际到货  ").Append(Energy(building.TotalReceived));
            buffer.Append("\n最近一秒到货  ").Append(Energy(building.ReceivedLastSecond));
            var supply = SupplyAssessment.Read(session.World, building);
            buffer.Append("\n\n").Append(supply.Summary);
            buffer.Append("\n连接：输入 ").Append(supply.IncomingConnections).Append(" / 输出 ").Append(supply.OutgoingConnections);
            if (supply.Connected) buffer.Append("\n额定发电 ").Append(supply.GenerationPerSecond.ToString("0.#"))
                .Append(" · 满速耗能 ").Append(supply.FullFireDemandPerSecond.ToString("0.#")).Append(" /秒");
            if (building.CanOutput)
                buffer.Append("\n传输范围 ").Append(building.Definition.TransmissionRange.ToString("0.#")).Append(" 格 · 输出 ")
                    .Append(Energy(building.Definition.OutputAmount)).Append(" / ").Append((building.Definition.OutputIntervalTicks / (double)session.World.Settings.TicksPerSecond).ToString("0.#")).Append("秒");
            if (building.IsTurret)
            {
                var weapon = session.World.GetTurretDefinition(building.Kind);
                buffer.Append("\n\n").Append(DefenseInterface.RailgunChargeStatus(building) ?? CombatStatus(building.CombatState));
                buffer.Append("\n单次能耗 ").Append(Energy(weapon.EnergyPerShot)).Append(" · 伤害 ").Append(weapon.Damage);
                buffer.Append("\n射程 ").Append(weapon.Range.ToString("0.#")).Append(" · 最短射击周期 ").Append(((weapon.CooldownTicks + weapon.ChargeTicks) / (double)session.World.Settings.TicksPerSecond).ToString("0.#")).Append("秒");
                if (weapon.IsPiercing) buffer.Append("\n蓄力 ").Append((weapon.ChargeTicks / (double)session.World.Settings.TicksPerSecond).ToString("0.#"))
                    .Append("秒 · 射后冷却 ").Append((weapon.CooldownTicks / (double)session.World.Settings.TicksPerSecond).ToString("0.#")).Append("秒");
                buffer.Append("\n实际伤害 ").Append(building.DamageDealt).Append(" · 缺能 ").Append((building.EnergyWaitTicks / (double)session.World.Settings.TicksPerSecond).ToString("0.#")).Append("秒");
            }
            if (building.IsConsumer)
                buffer.Append("\n供给优先级  ").Append(building.PriorityWeight == 4 ? "高" : building.PriorityWeight == 2 ? "中" : "低");
            buffer.Append("\n维修 ").Append(quote.Cost);
            if (building.Kind != BuildingKind.Core) buffer.Append(" · 出售 ").Append(building.Definition.SaleRefund);
            selectionText.text = buffer.ToString();
            energyFill.fillAmount = building.Definition.EnergyCapacity == 0 ? 0 : (float)building.StoredEnergy / building.Definition.EnergyCapacity;
        }

        private void RefreshEnemyIntent()
        {
            if (enemyIntentText == null) return;
            bool enabled = session.Battle.Settings.EnemyBehavior.Enabled;
            enemyIntentText.gameObject.SetActive(enabled);
            if (!enabled) return;
            BuildingState selected = session.SelectedBuilding;
            if (selected == null || selected.IsSold)
            {
                enemyIntentText.text = "重装目标：紫色方框表示锁定，红色方框表示实际出手，琥珀色十字表示搜索旧位置。\n选中建筑查看重装数量；保护供电节点，备用路线应避开同一个薄弱节点。";
                return;
            }
            int approaching = 0, attacking = 0, searching = 0, energyTargets = 0, turretTargets = 0;
            foreach (EnemyState enemy in session.Battle.Enemies)
            {
                if (!enemy.IsAlive || enemy.Kind != EnemyKind.Heavy || enemy.TargetBuildingId != selected.Id) continue;
                if (enemy.TargetPurpose == EnemyTargetPurpose.SearchLastKnownPosition) { searching++; continue; }
                if (EnemyView.IsShowingAttack(enemy, session.World.CurrentTick, session.World.Settings.TicksPerSecond)) attacking++;
                else approaching++;
                if (enemy.TargetPurpose == EnemyTargetPurpose.DisruptEnergy) energyTargets++;
                if (enemy.TargetPurpose == EnemyTargetPurpose.SuppressTurret) turretTargets++;
            }
            buffer.Clear();
            buffer.Append(selected.Definition.DisplayName).Append(" · 重装锁定 ").Append(approaching + attacking)
                .Append("（接近或等待 ").Append(approaching).Append(" / 正在出手 ").Append(attacking)
                .Append("） · 搜索旧位置 ").Append(searching);
            buffer.Append("\n");
            if (energyTargets > 0) buffer.Append("破坏供电 ").Append(energyTargets).Append(" · ");
            if (turretTargets > 0) buffer.Append("压制炮塔 ").Append(turretTargets).Append(" · ");
            buffer.Append(searching > 0 ? "搜索者只知道旧位置，尚未重新发现此处。" :
                approaching + attacking > 0 ? "保护接近路线；红色标记与实际攻击同步。" : "当前没有重装锁定此建筑。");
            enemyIntentText.text = buffer.ToString();
        }

        private void RefreshResult()
        {
            if (resultDetails == null) return;
            buffer.Clear();
            int destroyed = 0; long waiting = 0;
            foreach (var building in session.World.Buildings) { destroyed += building.DestructionCount; waiting += building.EnergyWaitTicks; }
            var totals = session.World.Energy.GetTotals();
            buffer.Append(session.Battle.State == BattleState.Victory ? "防守成功" : "核心失守");
            buffer.Append("   ·   完成 ").Append(session.Battle.State == BattleState.Victory ? session.Battle.WaveNumber : Math.Max(0, session.Battle.WaveNumber - 1)).Append(" / ").Append(session.Battle.Settings.Waves.Count).Append(" 波");
            buffer.Append("\n建筑被摧毁 ").Append(destroyed).Append(" 次 · 击败敌人 ").Append(session.Battle.Kills);
            foreach (var kind in new[] { BuildingKind.Turret, BuildingKind.BurstTurret, BuildingKind.AreaTurret })
            {
                long damage = 0, shots = 0, wait = 0;
                foreach (var building in session.World.Buildings) if (building.Kind == kind)
                { damage += building.DamageDealt; shots += building.ShotsFired; wait += building.EnergyWaitTicks; }
                buffer.Append("\n").Append(kind == BuildingKind.Turret ? "连射炮" : kind == BuildingKind.BurstTurret ? "重击炮" : "范围炮")
                    .Append("：实际伤害 ").Append(damage).Append(" · 射击 ").Append(shots).Append(" · 缺能等待 ").Append((wait / (double)session.World.Settings.TicksPerSecond).ToString("0.#")).Append("秒");
            }
            buffer.Append("\n\n能源损耗：传输 ").Append(Energy(totals.TransmissionLoss)).Append(" · 损毁与断跳 ").Append(Energy(totals.DestructionLoss)).Append(" · 滞留超时 ").Append(Energy(totals.ExpiredLoss));
            buffer.Append("\n缺能时间为各炮塔等待时间之和；已摧毁建筑永久损失。");
            buffer.Append("\n\n").Append(waiting > session.World.Settings.TicksPerSecond * 10 ? "缺能等待明显：缩短线路、增加反应堆，并为拥堵中继增设备用通道。" :
                destroyed > 0 ? "防线有建筑损毁：把供电建筑移到炮塔后方，保留维修资金。" : "供电稳定：比较各类炮塔的实际输出，尝试另一种布置与建设顺序。");
            buffer.Append("\n点击下方“重新开始”，恢复场景保存的初始防线。");
            resultDetails.text = buffer.ToString();
        }

        private static string StateMessage(BattleSystem battle)
        {
            switch (battle.State)
            {
                case BattleState.Preparing: return "准备防线\n布置建筑，等待储能充足后迎敌。";
                case BattleState.Combat: return "守住核心\n战斗期间不可建设、维修或调整供能优先级。";
                case BattleState.WaveCleared: return "本波已清场\n获得 " + battle.LastWaveReward + " 资金。继续准备后可维修并补充防线。";
                case BattleState.Victory: return "防守成功\n所有来袭敌人已被击败。可重新开始。";
                case BattleState.Defeat: return "核心已被摧毁\n本次防守结束。重新部署，再次迎敌。";
                default: return string.Empty;
            }
        }
        private static string CombatStatus(TurretCombatState state)
        {
            switch (state)
            {
                case TurretCombatState.WaitingForEnergy: return "能量不足，等待供给";
                case TurretCombatState.CoolingDown: return "准备下一次攻击";
                case TurretCombatState.Fired: return "正在射击";
                case TurretCombatState.Aiming: return "正在转向瞄准";
                case TurretCombatState.Charging: return "正在蓄力";
                default: return "寻找射程内的敌人";
            }
        }
        private static string Energy(long amount) => (amount / 1000.0).ToString("0.#");
        private void PlaceReactor() => session.GridInput.BeginPlacement(BuildingKind.Reactor);
        private void PlaceRelay() => session.GridInput.BeginPlacement(BuildingKind.Relay);
        private void PlaceTurret() => session.GridInput.BeginPlacement(BuildingKind.Turret);
        private void PlaceBurstTurret() => session.GridInput.BeginPlacement(BuildingKind.BurstTurret);
        private void PlaceAreaTurret() => session.GridInput.BeginPlacement(BuildingKind.AreaTurret);
        private void LowPriority() => session.SetSelectedPriority(1);
        private void NormalPriority() => session.SetSelectedPriority(2);
        private void HighPriority() => session.SetSelectedPriority(4);
        private void Move() => session.GridInput.BeginMove();
        private void Cancel() => session.GridInput.Cancel();
        private void AdvanceWave() { if (session.Battle.State == BattleState.WaveCleared) session.Submit(WorldCommand.ContinuePreparing()); else session.StartWave(); }
        private void OnDestroy() { Unbind(); }
        private void Unbind()
        {
            if (session == null) return;
            placementButtons[0].onClick.RemoveListener(PlaceReactor);
            placementButtons[1].onClick.RemoveListener(PlaceRelay);
            placementButtons[2].onClick.RemoveListener(PlaceTurret);
            if (placementButtons.Length == 5)
            {
                placementButtons[3].onClick.RemoveListener(PlaceBurstTurret);
                placementButtons[4].onClick.RemoveListener(PlaceAreaTurret);
            }
            priorityButtons[0].onClick.RemoveListener(LowPriority);
            priorityButtons[1].onClick.RemoveListener(NormalPriority);
            priorityButtons[2].onClick.RemoveListener(HighPriority);
            moveButton.onClick.RemoveListener(Move);
            sellButton.onClick.RemoveListener(session.SellSelected);
            repairButton.onClick.RemoveListener(session.RepairSelected);
            pauseButton.onClick.RemoveListener(session.TogglePause);
            startWaveButton.onClick.RemoveListener(AdvanceWave);
            restartButton.onClick.RemoveListener(session.Restart);
            cancelButton.onClick.RemoveListener(Cancel);
            session = null;
        }
    }
}
