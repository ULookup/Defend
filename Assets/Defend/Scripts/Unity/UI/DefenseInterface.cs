using System;
using System.Text;
using Defend.Simulation;
using UnityEngine;
using UnityEngine.UI;

namespace Defend.Unity
{
    /// <summary>正式页面只保存导航状态并读取模拟。所有控件由制作工具预先保存，运行时不创建界面。</summary>
    public sealed class DefenseInterface : MonoBehaviour
    {
        [SerializeField] private GameObject hud, startPage, pausePage, helpPage, archivePage, settingsPage, confirmationPage, settlementPage;
        [SerializeField] private GameObject shop, details, buildingActions;
        [SerializeField] private RectTransform mapViewport, selectionScroll;
        [SerializeField] private WorldCameraViewport cameraViewport;
        [SerializeField] private Text resources, wave, core, feedback, selectionTitle, selectionBody, repairLabel, repairAllLabel, startWaveLabel, pauseLabel;
        [SerializeField] private Text settlementTitle, settlementBody, confirmationText, archiveTitle, archiveBody;
        [SerializeField] private Image healthFill;
        [SerializeField] private Button startGame, openHelp, openArchive, openSettings, quit;
        [SerializeField] private Button pause, resume, arrangePaused, pauseHelp, pauseSettings, redeploy, returnHome;
        [SerializeField] private Button closeHelp, closeArchive, closeSettings, confirm, cancelConfirmation;
        [SerializeField] private Button nextPreparation, resultRestart, resultHome, settlementPause;
        [SerializeField] private Button startWave, repairAll, move, sell, repair, closeDetails, showDetails, locateEnemy, cancelPlacement, speed, network, ranges;
        [SerializeField] private Button[] shopButtons, priorityButtons, archiveCategories, archiveEntries;
        [SerializeField] private Text[] shopLabels, archiveEntryLabels;
        [SerializeField] private Slider musicVolume, effectsVolume;
        [SerializeField] private Toggle mute;
        [SerializeField] private DefenseAudio audioSettings;
        [SerializeField] private TextAsset archiveStory;
        [SerializeField] private Image enemyPortrait;
        [SerializeField] private Sprite[] enemyPortraits;
        private GameSessionRoot session;
        private enum Modal { None, Pause, Help, Archive, Settings, Confirmation }
        private Modal modal, returnModal;
        private bool showSelection = true, confirmHome;
        private int archiveCategory;
        private long deadEnemyId;
        private float deathShownAt;
        private readonly StringBuilder text = new StringBuilder(1024);
        private static readonly BuildingKind[] ShopKinds = { BuildingKind.Reactor, BuildingKind.Relay, BuildingKind.Battery, BuildingKind.Turret, BuildingKind.BurstTurret, BuildingKind.AreaTurret };
        private static readonly EnemyKind[] EnemyKinds = { EnemyKind.Normal, EnemyKind.Demolisher, EnemyKind.Converger, EnemyKind.Fast, EnemyKind.Heavy };
        public bool BlocksWorldInput => modal != Modal.None || (session != null && (session.AtStartPage || session.Battle.State == BattleState.WaveCleared || session.Battle.IsFinished));

        public void Initialize(GameSessionRoot root)
        {
            session = root;
            startGame.onClick.AddListener(session.Restart);
            openHelp.onClick.AddListener(() => Open(Modal.Help)); openArchive.onClick.AddListener(() => Open(Modal.Archive));
            openSettings.onClick.AddListener(() => Open(Modal.Settings)); quit.onClick.AddListener(Application.Quit);
            pause.onClick.AddListener(TogglePause); resume.onClick.AddListener(Resume);
            arrangePaused.onClick.AddListener(() => { modal = Modal.None; Refresh(); });
            pauseHelp.onClick.AddListener(() => Open(Modal.Help)); pauseSettings.onClick.AddListener(() => Open(Modal.Settings));
            redeploy.onClick.AddListener(() => RequestReset(false)); returnHome.onClick.AddListener(() => RequestReset(true));
            closeHelp.onClick.AddListener(CloseModal); closeArchive.onClick.AddListener(CloseModal); closeSettings.onClick.AddListener(CloseModal);
            cancelConfirmation.onClick.AddListener(() => { modal = Modal.Pause; Refresh(); });
            confirm.onClick.AddListener(() => { if (confirmHome) session.ReturnToStart(); else session.Restart(); });
            nextPreparation.onClick.AddListener(() => session.Submit(WorldCommand.ContinuePreparing()));
            resultRestart.onClick.AddListener(session.Restart); resultHome.onClick.AddListener(session.ReturnToStart);
            settlementPause.onClick.AddListener(() => session.SetPaused(!session.Paused));
            startWave.onClick.AddListener(session.StartWave); repairAll.onClick.AddListener(() => session.Submit(WorldCommand.RepairAll()));
            move.onClick.AddListener(() => session.GridInput.BeginMove()); sell.onClick.AddListener(session.SellSelected); repair.onClick.AddListener(session.RepairSelected);
            closeDetails.onClick.AddListener(() => { showSelection = false; session.ClearSelection(); });
            showDetails.onClick.AddListener(RevealDetails);
            locateEnemy.onClick.AddListener(() => { var enemy = session.SelectedEnemy; if (enemy != null && enemy.IsAlive) cameraViewport.Focus((float)enemy.X, (float)enemy.Z); });
            cancelPlacement.onClick.AddListener(() => session.GridInput.Cancel()); speed.onClick.AddListener(session.CycleSpeed);
            network.onClick.AddListener(() => { session.WorldViews.ShowNetwork = !session.WorldViews.ShowNetwork; session.WorldViews.Invalidate(); });
            ranges.onClick.AddListener(() => { session.WorldViews.ShowRanges = !session.WorldViews.ShowRanges; session.WorldViews.Invalidate(); });
            for (int i = 0; i < shopButtons.Length; i++) { BuildingKind kind = ShopKinds[i]; shopButtons[i].onClick.AddListener(() => session.GridInput.BeginPlacement(kind)); }
            for (int i = 0; i < priorityButtons.Length; i++) { int weight = 1 << i; priorityButtons[i].onClick.AddListener(() => session.SetSelectedPriority(weight)); }
            for (int i = 0; i < archiveCategories.Length; i++) { int category = i; archiveCategories[i].onClick.AddListener(() => { archiveCategory = category; RefreshArchive(0); }); }
            for (int i = 0; i < archiveEntries.Length; i++) { int entry = i; archiveEntries[i].onClick.AddListener(() => RefreshArchive(entry)); }
            audioSettings.Load(); musicVolume.SetValueWithoutNotify(audioSettings.MusicVolume); effectsVolume.SetValueWithoutNotify(audioSettings.EffectsVolume); mute.SetIsOnWithoutNotify(audioSettings.Muted);
            musicVolume.onValueChanged.AddListener(value => audioSettings.SetMusic(value)); effectsVolume.onValueChanged.AddListener(value => audioSettings.SetEffects(value)); mute.onValueChanged.AddListener(value => audioSettings.SetMuted(value));
            foreach (var button in GetComponentsInChildren<Button>(true)) button.onClick.AddListener(audioSettings.PlayClick);
            RefreshArchive(0);
        }

        public void ShowStart() { modal = Modal.None; Refresh(); }
        public void SessionStarted() { modal = Modal.None; showSelection = true; deadEnemyId = 0; Refresh(); }
        public void RevealDetails() { showSelection = true; }
        public void TogglePause()
        {
            if (session == null || session.AtStartPage || session.Battle.IsFinished) return;
            if (modal == Modal.Pause) { Resume(); return; }
            session.SetPaused(true); modal = Modal.Pause; Refresh();
        }
        private void Resume() { modal = Modal.None; session.SetPaused(false); Refresh(); }
        private void Open(Modal target) { returnModal = modal; modal = target; Refresh(); }
        private void CloseModal() { modal = returnModal; returnModal = Modal.None; Refresh(); }
        private void RequestReset(bool home)
        {
            confirmHome = home; session.SetPaused(true); modal = Modal.Confirmation;
            confirmationText.text = "当前对局进度将丢失。\n" + (home ? "返回开始界面？" : "重新部署防线？"); Refresh();
        }

        public void Refresh()
        {
            if (session?.World == null) return;
            var battle = session.Battle;
            bool home = session.AtStartPage, editing = session.World.CanEditBuildings && !home;
            bool settling = !home && (battle.State == BattleState.WaveCleared || battle.IsFinished);
            hud.SetActive(!home); startPage.SetActive(home);
            pausePage.SetActive(modal == Modal.Pause); helpPage.SetActive(modal == Modal.Help); archivePage.SetActive(modal == Modal.Archive);
            settingsPage.SetActive(modal == Modal.Settings); confirmationPage.SetActive(modal == Modal.Confirmation);
            settlementPage.SetActive(settling && modal == Modal.None);
            shop.SetActive(editing); details.SetActive(showSelection && !home); buildingActions.SetActive(editing && session.SelectedBuilding != null);
            cameraViewport.InputBlocked = BlocksWorldInput;
            mapViewport.anchorMin = new Vector2(0, editing ? .23f : .10f); mapViewport.anchorMax = new Vector2(showSelection ? .73f : 1, .88f);
            resources.text = "可用资金  " + session.World.Credits + "     " + (session.Paused ? "已暂停" : "速度 " + session.Speed + " 倍");
            wave.text = "第 " + Math.Min(battle.Settings.Waves.Count, battle.WaveNumber + (editing ? 1 : 0)) + " / " + battle.Settings.Waves.Count + " 波    在场敌人 " + battle.Enemies.Count + "    待入场 " + battle.RemainingToSpawn + "    待结算击杀奖励 " + battle.PendingKillReward;
            var baseCore = session.World.FindBuilding(battle.CoreBuildingId);
            core.text = baseCore == null ? "核心未部署" : "核心生命  " + baseCore.Health + " / " + baseCore.Definition.MaximumHealth + "    护盾  " + Energy(baseCore.StoredEnergy) + " / " + Energy(baseCore.Definition.EnergyCapacity);
            feedback.text = session.Feedback;
            if (editing && !session.GridInput.HasPlacement) feedback.text += "\n" + NextWaveDescription();
            pauseLabel.text = session.Paused ? "暂停菜单" : "暂停";
            pause.interactable = !battle.IsFinished;
            arrangePaused.gameObject.SetActive(editing); startWave.gameObject.SetActive(editing); repairAll.gameObject.SetActive(editing);
            startWaveLabel.text = "开始第 " + (battle.WaveNumber + 1) + " 波";
            var all = session.World.RepairAllQuote(); repairAllLabel.text = QuoteLabel("全部维修", all.Cost, all.BuildingCount);
            repairAll.interactable = editing && all.CanRepair && all.Cost <= session.World.Credits;
            cancelPlacement.interactable = session.GridInput.HasPlacement;
            for (int i = 0; i < shopButtons.Length; i++) { var definition = session.FindDefinition(ShopKinds[i]).CreateDefinition(); shopLabels[i].text = definition.DisplayName + "\n" + definition.PurchaseCost + " 资金"; shopButtons[i].interactable = editing && session.World.Credits >= definition.PurchaseCost; }
            RefreshSelection(editing);
            if (settling) RefreshSettlement();
        }

        private string NextWaveDescription()
        {
            if (session.Battle.WaveNumber >= session.Battle.Settings.Waves.Count) return "";
            var next = session.Battle.Settings.Waves[session.Battle.WaveNumber];
            var preview = new StringBuilder("下一波：");
            foreach (var group in next.Groups) preview.Append(session.Battle.Settings.GetEnemy(group.Kind).DisplayName).Append(" ").Append(group.Count).Append("（入口").Append(group.EntranceIndex < 0 ? "交替" : (group.EntranceIndex + 1).ToString()).Append("） ");
            if (next.Groups.Count == 0) preview.Append(next.EnemyCount).Append(" 名敌人");
            return preview.ToString();
        }
        private string QuoteLabel(string prefix, long cost, int count) => count == 0 ? "无需维修" : prefix + " " + cost + (cost > session.World.Credits ? "（还差 " + (cost - session.World.Credits) + "）" : "");
        private void RefreshSelection(bool editing)
        {
            var enemy = session.SelectedEnemy; var building = session.SelectedBuilding;
            // 已保存的滚动区随对象类型分配空间，建筑不为敌人头像留出空白。
            selectionScroll.offsetMin = new Vector2(20, editing && building != null ? 180 : enemy != null ? 90 : 24);
            selectionScroll.offsetMax = new Vector2(-20, enemy != null ? -164 : -88);
            if (enemyPortrait != null) { enemyPortrait.gameObject.SetActive(enemy != null); if (enemy != null && enemyPortraits != null && (int)enemy.Kind < enemyPortraits.Length) enemyPortrait.sprite = enemyPortraits[(int)enemy.Kind]; }
            locateEnemy.gameObject.SetActive(enemy != null && enemy.IsAlive);
            if (enemy != null)
            {
                var definition = enemy.Definition;
                selectionTitle.text = definition.DisplayName + (enemy.IsAlive ? "" : " · 已消灭");
                healthFill.fillAmount = (float)enemy.Health / enemy.MaximumHealth;
                selectionBody.text = "生命 " + enemy.Health + " / " + enemy.MaximumHealth + "\n" + definition.AttackDescription + "\n伤害 " + definition.AttackDamage + "   间隔 " + (definition.AttackIntervalTicks / (float)session.World.Settings.TicksPerSecond).ToString("0.##") + " 秒\n攻击距离 " + definition.AttackRange.ToString("0.##") + "   移速 " + definition.Speed.ToString("0.##") + "\n\n最终目标：" + BuildingName(enemy.FinalTargetBuildingId) + "\n当前攻击：" + BuildingName(enemy.CurrentAttackBuildingId) + "\n当前行动：" + ActionDescription(enemy.CurrentAction) + "\n\n" + definition.Description;
                if (!enemy.IsAlive) { if (deadEnemyId != enemy.Id) { deadEnemyId = enemy.Id; deathShownAt = Time.unscaledTime; } if (Time.unscaledTime - deathShownAt > 2) { session.ClearSelection(); showSelection = false; } }
                return;
            }
            healthFill.fillAmount = building == null ? 0 : (float)building.Health / building.Definition.MaximumHealth;
            if (building == null) { selectionTitle.text = "防线详情"; selectionBody.text = "点击建筑查看耐久与供能。\n点击敌人查看实时状态与目标。\n\n准备阶段可建设和付费维修；战斗阶段专注观察。\n\n拖动镜头：鼠标中键或手指\n缩放镜头：滚轮或双指\n空格：暂停菜单\nEsc：取消放置或暂停"; return; }
            selectionTitle.text = building.Definition.DisplayName;
            text.Clear().Append("耐久 ").Append(building.Health).Append(" / ").Append(building.Definition.MaximumHealth)
                .Append("\n实际储能 ").Append(Energy(building.StoredEnergy)).Append(" / ").Append(Energy(building.Definition.EnergyCapacity))
                .Append("\n有效在途供给 ").Append(Energy(building.CommittedIncoming)).Append("\n实际到货 ").Append(Energy(building.ReceivedLastSecond)).Append(" / 秒");
            if (building.Kind == BuildingKind.Battery) text.Append("\n实际放电 ").Append(Energy(building.DischargedLastSecond)).Append(" / 秒\n净变化 ").Append(Energy(building.NetEnergyChangeLastSecond)).Append(" / 秒\n").Append(building.ReceivedLastSecond > 0 && building.DischargedLastSecond > 0 ? "充放电中" : building.DischargedLastSecond > 0 ? "放电中" : building.ReceivedLastSecond > 0 ? "充电中" : "储能待命");
            if (building.IsTurret) { var weapon = session.Battle.Settings.GetTurret(building.Kind); text.Append("\n\n单次伤害 ").Append(weapon.Damage).Append("\n攻击范围 ").Append(weapon.Range).Append("\n每次耗能 ").Append(Energy(weapon.EnergyPerShot)); }
            text.Append("\n\n").Append(BuildingDescription(building.Kind)); selectionBody.text = text.ToString();
            var quote = session.World.RepairQuote(building.Id); repairLabel.text = QuoteLabel("维修", quote.Cost, quote.BuildingCount);
            repair.interactable = editing && quote.CanRepair && quote.Cost <= session.World.Credits;
            move.interactable = sell.interactable = editing && building.Kind != BuildingKind.Core && building.IsAlive;
            foreach (var button in priorityButtons) button.interactable = editing && building.IsEnergyDestination;
        }
        private string BuildingName(long id) { var building = session.World.FindBuilding(id); return building == null || !building.IsAlive || building.IsSold ? "无" : building.Definition.DisplayName + "（" + building.X + ", " + building.Z + "）"; }
        private void RefreshSettlement()
        {
            var battle = session.Battle; bool ordinary = battle.State == BattleState.WaveCleared;
            settlementTitle.text = ordinary ? "第 " + battle.WaveNumber + " 波防守完成" : battle.State == BattleState.Victory ? "守望成功" : "防线失守";
            var baseCore = session.World.FindBuilding(battle.CoreBuildingId); var quote = session.World.RepairAllQuote();
            settlementBody.text = ordinary ? "固定奖励 " + battle.LastWaveFixedReward + "\n击杀奖励 " + battle.LastWaveKillReward + "\n合计收入 " + battle.LastWaveReward + "\n\n本波建筑损失 " + battle.LastWaveBuildingLosses + "\n受损建筑 " + quote.BuildingCount + "    全部维修 " + quote.Cost + "\n核心生命 " + (baseCore?.Health ?? 0) + "\n\n" + (session.Paused ? "已暂停能源运行" : "能源网络继续运行，护盾按实际供能恢复。") : "完成波次 " + (battle.State == BattleState.Victory ? battle.WaveNumber : Math.Max(0, battle.WaveNumber - 1)) + "\n击杀数 " + battle.Kills + "\n累计收入 " + session.World.TotalIncome + "\n建筑损失 " + session.World.DestroyedBuildingCount + "\n核心剩余生命 " + (baseCore?.Health ?? 0);
            nextPreparation.gameObject.SetActive(ordinary); settlementPause.gameObject.SetActive(ordinary);
            resultRestart.gameObject.SetActive(!ordinary); resultHome.gameObject.SetActive(!ordinary);
        }
        private void RefreshArchive(int entry)
        {
            int count = archiveCategory == 0 ? 1 : archiveCategory == 1 ? 7 : 5;
            for (int i = 0; i < archiveEntries.Length; i++) { archiveEntries[i].gameObject.SetActive(i < count); if (i >= count) continue; archiveEntryLabels[i].text = archiveCategory == 0 ? "守望垣城" : archiveCategory == 1 ? session.FindDefinition(i == 6 ? BuildingKind.Core : ShopKinds[i]).DisplayName : session.Battle.Settings.GetEnemy(EnemyKinds[i]).DisplayName; }
            entry = Math.Min(entry, count - 1); archiveTitle.text = archiveEntryLabels[entry].text;
            if (archiveCategory == 0) archiveBody.text = archiveStory != null ? archiveStory.text : "暂无背景资料。";
            else if (archiveCategory == 1) { var kind = entry == 6 ? BuildingKind.Core : ShopKinds[entry]; var definition = session.FindDefinition(kind).CreateDefinition(); archiveBody.text = BuildingDescription(kind) + "\n\n最大耐久 " + definition.MaximumHealth + "\n储能容量 " + Energy(definition.EnergyCapacity) + (kind == BuildingKind.Core ? "\n核心不可购买、移动、出售或维修生命。" : "\n购买 " + definition.PurchaseCost + "    出售 " + definition.SaleRefund); }
            else { var definition = session.Battle.Settings.GetEnemy(EnemyKinds[entry]); archiveBody.text = definition.Description + "\n\n" + definition.AttackDescription + "\n伤害 " + definition.AttackDamage + "\n攻击间隔 " + (definition.AttackIntervalTicks / (float)session.World.Settings.TicksPerSecond).ToString("0.##") + " 秒\n移动速度 " + definition.Speed.ToString("0.##"); }
        }
        public static string BuildingDescription(BuildingKind kind)
        {
            switch (kind) {
                case BuildingKind.Reactor: return "产生能量，经低损耗路线供给防线。保护源头才能保持续航。";
                case BuildingKind.Relay: return "延伸供电线路，按有效需求分配输出。断路后，下游已有储能仍可使用。";
                case BuildingKind.Battery: return "储存到达的能量，支援全网可达的缺能建筑。可同时充放电，也可按储能比例差为其他电池补能。";
                case BuildingKind.Core: return "防线核心。护盾由实际到货补充，生命损失无法恢复；生命归零则本局结束。";
                case BuildingKind.BurstTurret: return "崩岩机以集中火力对付高耐久敌人，单次耗能较高。";
                case BuildingKind.AreaTurret: return "荡尘机攻击目标周围的敌群，需要稳定的能源供给。";
                default: return "闪哨机连续拦截近处敌人，缺少能量时等待供给。";
            }
        }
        private static string ActionDescription(EnemyAction action)
        {
            switch (action) { case EnemyAction.Attacking: return "正在攻击"; case EnemyAction.Detouring: return "正在绕行"; case EnemyAction.WaitingForPassage: return "等待通行"; default: return "正在行进"; }
        }
        private static string Energy(long amount) => (amount / 1000d).ToString("0.#");
    }
}
