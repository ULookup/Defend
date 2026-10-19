using System;
using System.Text;
using Defend.Simulation;
using UnityEngine;
using UnityEngine.UI;

namespace Defend.Unity
{
    /// <summary>正式页面只保存导航状态并读取模拟。所有控件由制作工具预先保存，运行时不创建界面。</summary>
    public sealed partial class DefenseInterface : MonoBehaviour
    {
        [SerializeField] private InterfaceMotionCoordinator motionCoordinator;
        [SerializeField] private GameObject hud, startPage, pausePage, helpPage, archivePage, settingsPage, confirmationPage, settlementPage;
        [SerializeField] private GameObject shop, details, buildingActions;
        [SerializeField] private RectTransform mapViewport, selectionScroll;
        [SerializeField] private WorldCameraViewport cameraViewport;
        [SerializeField] private Text resources, wave, core, feedback, selectionTitle, selectionBody, repairLabel, repairAllLabel, startWaveLabel, pauseLabel;
        [SerializeField] private Text settlementTitle, settlementBody, confirmationText, confirmationTitle, archiveTitle, archiveBody;
        [SerializeField] private Text settlementPauseText;
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
        [SerializeField] private EnergyCoverageLegend energyCoverageLegend;
        [SerializeField] private SandboxControlPanel sandboxPanel;
        [SerializeField] private OutgameInterface outgameInterface;
        [SerializeField] private IngameArtInterface ingameArtInterface;
        [SerializeField] private FormalWaveIntelPresenter formalWaveIntel;
        [SerializeField] private GameObject railgunFacingControls;
        [SerializeField] private Button railgunFacingLeft, railgunFacingRight;
        [SerializeField] private Text railgunFacingLabel;
        private int pageTransitionFrame = -1;
        public bool IsSandboxPanelOpen => sandboxPanel != null && sandboxPanel.IsOpen;
        public bool HasUnappliedSandboxChanges => sandboxPanel != null && sandboxPanel.HasUnappliedChanges;
        public void CloseForSessionChange() { sandboxPanel?.CloseForSessionChange(); formalWaveIntel?.Close(); newPlayerGuide?.CloseForSessionChange(); }
        public void ConfigureCoverageLegend(EnergyCoverageLegend legend) { energyCoverageLegend = legend; }
        private GameSessionRoot session;
        private enum Modal { None, Pause, Help, Archive, Settings, Confirmation }
        private Modal modal, returnModal;
        private bool showSelection = true, confirmHome;
        private int archiveCategory;
        private long deadEnemyId;
        private BuildingDefinition observedPlacementPreview;
        private float deathShownAt;
        private readonly StringBuilder text = new StringBuilder(1024);
        private static readonly BuildingKind[] ShopKinds = { BuildingKind.Reactor, BuildingKind.Relay, BuildingKind.Battery, BuildingKind.Turret, BuildingKind.BurstTurret, BuildingKind.AreaTurret, BuildingKind.PiercingRailgun, BuildingKind.ResonanceMagneticTower };
        private static readonly EnemyKind[] EnemyKinds = { EnemyKind.Normal, EnemyKind.Demolisher, EnemyKind.Converger, EnemyKind.Fast, EnemyKind.Heavy };
        public bool BlocksWorldInput => (newPlayerGuide != null && newPlayerGuide.IsManualOpen) || (sandboxPanel != null && sandboxPanel.BlocksWorldInput) || modal != Modal.None
            || (motionCoordinator != null && motionCoordinator.AnyModalExiting)
            || (session != null && (session.AtStartPage || session.Battle.State == BattleState.WaveCleared || session.Battle.IsFinished));

        public void Initialize(GameSessionRoot root)
        {
            session = root;
            startGame.onClick.AddListener(session.StartNormal);
            sandboxPanel?.Initialize(session, cameraViewport);
            openHelp.onClick.AddListener(() => Open(Modal.Help)); openArchive.onClick.AddListener(() => Open(Modal.Archive));
            openSettings.onClick.AddListener(() => Open(Modal.Settings)); quit.onClick.AddListener(session.RequestQuit);
            pause.onClick.AddListener(TogglePause); resume.onClick.AddListener(Resume);
            arrangePaused.onClick.AddListener(() => { modal = Modal.None; Refresh(); });
            pauseHelp.onClick.AddListener(() => Open(Modal.Help)); pauseSettings.onClick.AddListener(() => Open(Modal.Settings));
            redeploy.onClick.AddListener(() => RequestReset(false)); returnHome.onClick.AddListener(() => RequestReset(true));
            closeHelp.onClick.AddListener(CloseModal); closeArchive.onClick.AddListener(CloseModal); closeSettings.onClick.AddListener(CloseModal);
            cancelConfirmation.onClick.AddListener(() => { modal = Modal.Pause; Refresh(); });
            confirm.onClick.AddListener(() => { if (confirmHome) ReturnFromBattle(); else session.Restart(); });
            nextPreparation.onClick.AddListener(() => session.Submit(WorldCommand.ContinuePreparing()));
            resultRestart.onClick.AddListener(session.Restart); resultHome.onClick.AddListener(ReturnFromBattle);
            settlementPause.onClick.AddListener(() => session.SetPaused(!session.Paused));
            startWave.onClick.AddListener(session.StartWave); repairAll.onClick.AddListener(() => session.Submit(WorldCommand.RepairAll()));
            move.onClick.AddListener(() => session.GridInput.BeginMove()); sell.onClick.AddListener(session.SellSelected); repair.onClick.AddListener(session.RepairSelected);
            if (railgunFacingLeft != null) railgunFacingLeft.onClick.AddListener(() => TurnRailgun(-15));
            if (railgunFacingRight != null) railgunFacingRight.onClick.AddListener(() => TurnRailgun(15));
            closeDetails.onClick.AddListener(() => { showSelection = false; });
            showDetails.onClick.AddListener(RevealDetails);
            locateEnemy.onClick.AddListener(() => { var enemy = session.SelectedEnemy; if (enemy != null && enemy.IsAlive) cameraViewport.Focus((float)enemy.X, (float)enemy.Z); });
            cancelPlacement.onClick.AddListener(() => session.GridInput.Cancel(true)); speed.onClick.AddListener(session.CycleSpeed);
            network.onClick.AddListener(() => { session.WorldViews.ShowNetwork = !session.WorldViews.ShowNetwork; session.WorldViews.Invalidate(); });
            ranges.onClick.AddListener(() => { session.WorldViews.ShowRanges = !session.WorldViews.ShowRanges; session.WorldViews.Invalidate(); });
            for (int i = 0; i < shopButtons.Length; i++)
            {
                BuildingKind kind = ShopKinds[i];
                shopButtons[i].onClick.AddListener(() =>
                {
                    session.GridInput.BeginPlacement(kind);
                    if (GetPlacementPreview(session) != null) { RevealDetails(); Refresh(); }
                });
            }
            for (int i = 0; i < priorityButtons.Length; i++) { int weight = 1 << i; priorityButtons[i].onClick.AddListener(() => session.SetSelectedPriority(weight)); }
            if (outgameInterface == null)
            {
                for (int i = 0; i < archiveCategories.Length; i++) { int category = i; archiveCategories[i].onClick.AddListener(() => { archiveCategory = category; RefreshArchive(0); }); }
                for (int i = 0; i < archiveEntries.Length; i++) { int entry = i; archiveEntries[i].onClick.AddListener(() => RefreshArchive(entry)); }
            }
            audioSettings.Load(); musicVolume.SetValueWithoutNotify(audioSettings.MusicVolume); effectsVolume.SetValueWithoutNotify(audioSettings.EffectsVolume); mute.SetIsOnWithoutNotify(audioSettings.Muted);
            session.ConfigureAudio(audioSettings);
            musicVolume.onValueChanged.AddListener(value => audioSettings.SetMusic(value)); effectsVolume.onValueChanged.AddListener(value => audioSettings.SetEffects(value)); mute.onValueChanged.AddListener(value => audioSettings.SetMuted(value));
            foreach (var button in GetComponentsInChildren<Button>(true))
            {
                Button clickedButton = button;
                button.onClick.AddListener(() => PlayButtonFeedback(clickedButton));
            }
            if (outgameInterface != null) outgameInterface.Initialize(session, audioSettings, archiveStory);
            else RefreshArchive(0);
            campaignSelection?.Initialize(session, this);
            newPlayerGuide?.Initialize(session, this);
        }

        private void PlayButtonFeedback(Button button)
        {
            // 这些操作已有事务结果或导航反馈，不能再叠加一次通用点击声。
            if (button == startWave || button == repairAll || button == repair || button == sell || button == move
                || button == pause || button == resume || button == settlementPause || button == cancelPlacement
                || button == openHelp || button == openArchive || button == openSettings || button == pauseHelp
                || button == pauseSettings || button == closeHelp || button == closeArchive || button == closeSettings
                || button == redeploy || button == returnHome || button == nextPreparation
                || button == railgunFacingLeft || button == railgunFacingRight
                || Array.IndexOf(shopButtons, button) >= 0 || Array.IndexOf(priorityButtons, button) >= 0) return;
            if (button == cancelConfirmation) audioSettings.Play(DefenseSoundCue.UiBack);
            else if (button == startGame || button == confirm || button == resultRestart || button == resultHome)
                audioSettings.Play(DefenseSoundCue.UiConfirm);
            else audioSettings.PlayClick();
        }

        public void ShowStart() { newPlayerGuide?.CloseForSessionChange(); campaignSelectionOpen = false; pageTransitionFrame = Time.frameCount; modal = returnModal = Modal.None; motionCoordinator?.ResetPresentation(); Refresh(); }
        public void SessionStarted() { campaignSelectionOpen = false; pageTransitionFrame = Time.frameCount; modal = returnModal = Modal.None; showSelection = true; deadEnemyId = 0; motionCoordinator?.ResetPresentation(); newPlayerGuide?.OnSessionStarted(); Refresh(); }
        public bool ConsumePageShortcut(bool escape)
        {
            if (pageTransitionFrame == Time.frameCount) return true;
            if (newPlayerGuide != null && newPlayerGuide.ConsumeShortcut(escape)) return true;
            if (modal == Modal.Pause || modal == Modal.Confirmation)
            {
                if (escape)
                {
                    if (modal == Modal.Confirmation) { modal = Modal.Pause; audioSettings.Play(DefenseSoundCue.UiBack); Refresh(); }
                    else Resume();
                }
                return true;
            }
            if (modal == Modal.Help || modal == Modal.Archive || modal == Modal.Settings)
            {
                if (escape) CloseModal();
                return true;
            }
            if (campaignSelectionOpen && session != null && session.AtStartPage) { if (escape) ShowStart(); return true; }
            return session != null && session.AtStartPage;
        }
        public void RevealDetails() { showSelection = true; }
        public static BuildingDefinition GetPlacementPreview(GameSessionRoot root)
        {
            var input = root != null ? root.GridInput : null;
            return root != null && !root.AtStartPage && root.World != null && root.World.CanEditBuildings &&
                input != null && input.HasPlacement && input.MovingBuildingId == 0 ? input.PreviewDefinition : null;
        }
        private void TurnRailgun(int delta)
        {
            var building = session?.SelectedBuilding;
            if (building == null || building.Kind != BuildingKind.PiercingRailgun || !session.World.CanEditBuildings || session.GridInput.HasPlacement) return;
            session.Submit(WorldCommand.SetFacing(building.Id, building.FacingDegrees + delta));
        }
        public void TogglePause()
        {
            if (session == null || session.AtStartPage || session.Battle.IsFinished) return;
            if (sandboxPanel != null && sandboxPanel.IsOpen) { sandboxPanel.Close(); return; }
            if (modal == Modal.Pause) { Resume(); return; }
            session.SetPaused(true); modal = Modal.Pause; Refresh();
        }
        private void Resume() { modal = Modal.None; session.SetPaused(false); Refresh(); }
        private void Open(Modal target) { pageTransitionFrame = Time.frameCount; returnModal = modal; modal = target; audioSettings.PlayClick(); Refresh(); }
        private void CloseModal() { pageTransitionFrame = Time.frameCount; modal = returnModal; returnModal = Modal.None; audioSettings.Play(DefenseSoundCue.UiBack); Refresh(); }
        private void RequestReset(bool home)
        {
            confirmHome = home; session.SetPaused(true); modal = Modal.Confirmation;
            audioSettings.PlayClick();
            string returnTitle = session.IsCampaign || session.IsTutorial ? "返回关卡选择" : "返回开始界面";
            if (confirmationTitle != null) confirmationTitle.text = home ? returnTitle : "重新部署";
            confirmationText.text = home ? returnTitle + "后，当前对局的建筑与波次不会保留。\n已完成关卡的通关标记会保留。确定离开本局？" : "重新部署将重置本局建筑、资金和波次。\n确定重新开始？";
            var confirmationLabel = confirm.GetComponentInChildren<Text>(true);
            if (confirmationLabel != null) confirmationLabel.text = home ? returnTitle : "重新部署";
            Refresh();
        }

        public void Refresh()
        {
            if (session?.World == null) return;
            var battle = session.Battle;
            bool home = session.AtStartPage, editing = session.World.CanEditBuildings && !home;
            var preview = GetPlacementPreview(session);
            if (preview != null && !ReferenceEquals(observedPlacementPreview, preview)) showSelection = true;
            observedPlacementPreview = preview;
            audioSettings.Present(home, battle.State);
            bool settling = !home && (battle.State == BattleState.WaveCleared || battle.IsFinished);
            bool guideOpen = newPlayerGuide != null && newPlayerGuide.IsManualOpen;
            bool selecting = home && campaignSelectionOpen && modal == Modal.None && !guideOpen;
            hud.SetActive(!home); startPage.SetActive(home && !campaignSelectionOpen && modal == Modal.None);
            if (campaignSelection != null) campaignSelection.gameObject.SetActive(selecting);
            SetMotionVisible(pausePage, modal == Modal.Pause); SetMotionVisible(helpPage, modal == Modal.Help); SetMotionVisible(archivePage, modal == Modal.Archive);
            SetMotionVisible(settingsPage, modal == Modal.Settings); SetMotionVisible(confirmationPage, modal == Modal.Confirmation);
            outgameInterface?.Present(home && !campaignSelectionOpen && modal == Modal.None, modal == Modal.Archive, modal == Modal.Help, modal == Modal.Settings,
                guideOpen ? newPlayerGuide.ManualRoot : selecting ? campaignSelection.gameObject : null);
            newPlayerGuide?.Present(!home && modal == Modal.None && !settling);
            RefreshCampaignReturnLabels();
            SetMotionVisible(settlementPage, settling && modal == Modal.None);
            bool hasSelection = preview != null || session.SelectedBuilding != null || session.SelectedEnemy != null;
            SetMotionVisible(shop, editing);
            SetMotionVisible(details, showSelection && !home && (ingameArtInterface == null || hasSelection));
            buildingActions.SetActive(editing && preview == null && session.SelectedBuilding != null);
            showDetails.gameObject.SetActive(!showSelection && hasSelection);
            closeDetails.gameObject.SetActive(showSelection && hasSelection);
            cameraViewport.InputBlocked = BlocksWorldInput;
            mapViewport.anchorMin = Vector2.zero; mapViewport.anchorMax = Vector2.one;
            mapViewport.offsetMin = mapViewport.offsetMax = Vector2.zero;
            sandboxPanel?.Refresh();
            formalWaveIntel?.Refresh(session);
            resources.text = (session.IsSandbox ? "沙盒模式 · 无限资金" : "可用资金  " + session.World.Credits) + "     " + (session.Paused ? "已暂停" : "速度 " + session.Speed + " 倍");
            wave.text = session.IsSandbox ? "第 " + (battle.WaveNumber + (editing ? 1 : 0)) + " 波 · 在场 " + battle.Enemies.Count + " · 待生成 " + battle.RemainingToSpawn + " · 已生成 " + battle.SandboxSpawnedCount : "第 " + Math.Min(battle.Settings.Waves.Count, battle.WaveNumber + (editing ? 1 : 0)) + " / " + battle.Settings.Waves.Count + " 波    在场敌人 " + battle.Enemies.Count + "    待入场 " + battle.RemainingToSpawn + "    待结算击杀奖励 " + battle.PendingKillReward;
            var baseCore = session.World.FindBuilding(battle.CoreBuildingId);
            core.text = baseCore == null ? "核心未部署" : "核心生命  " + baseCore.Health + " / " + baseCore.Definition.MaximumHealth + "    护盾  " + Energy(baseCore.StoredEnergy) + " / " + Energy(baseCore.Definition.EnergyCapacity);
            feedback.text = session.GridInput.HasPlacement
                ? session.GridInput.ModeDescription + (session.GridInput.PreviewDefinition != null
                    ? "\n左键确认 · R 旋转 90° · 右键或 Esc 取消\n"
                    : " · 左键确认 · 右键或 Esc 取消\n")
                    + (session.GridInput.MovingBuildingId != 0 ? session.Feedback + "\n" : "")
                    + "亮色填充：候选运输效率 · 淡色填充：已有覆盖 · 金色：攻击范围"
                : session.Feedback;
            energyCoverageLegend?.Refresh(session);
            if (editing && !session.GridInput.HasPlacement)
                feedback.text = formalWaveIntel != null && !session.IsSandbox
                    ? formalWaveIntel.ShowCommandFeedback ? session.Feedback : formalWaveIntel.Summary
                    : feedback.text + "\n" + NextWaveDescription();
            pauseLabel.text = session.Paused ? "暂停菜单" : "暂停";
            pause.interactable = !battle.IsFinished;
            arrangePaused.gameObject.SetActive(editing || battle.State == BattleState.Combat);
            arrangePaused.GetComponentInChildren<Text>(true).text = "查看战场";
            SetMotionVisible(startWave.gameObject, editing); startWave.interactable = editing; repairAll.gameObject.SetActive(editing);
            startWaveLabel.text = "开始第 " + (battle.WaveNumber + 1) + " 波";
            var all = session.World.RepairAllQuote(); repairAllLabel.text = QuoteLabel("全部维修", all.Cost, all.BuildingCount);
            repairAll.interactable = editing && all.CanRepair && (session.IsSandbox || all.Cost <= session.World.Credits);
            cancelPlacement.interactable = session.GridInput.HasPlacement;
            for (int i = 0; i < shopButtons.Length; i++) { var definition = session.FindEffectiveDefinition(ShopKinds[i]); shopLabels[i].text = definition.DisplayName + "\n" + (session.IsSandbox ? "免费" : definition.PurchaseCost + " 资金"); shopButtons[i].interactable = editing && (session.IsSandbox || session.World.Credits >= definition.PurchaseCost); }
            RefreshSelection(editing);
            if (settling) RefreshSettlement();
            ingameArtInterface?.Refresh(session);
            motionCoordinator?.Observe(session);
            if (BlocksWorldInput)
            {
                ingameArtInterface?.HideTooltip();
                feedback.text = "";
            }
            else if (session.Paused) feedback.text = formalWaveIntel != null && editing && !session.IsSandbox
                ? "已暂停 · " + feedback.text : (editing ? "已暂停 · 可继续布置" : "已暂停 · 可观察战场") + "\n" + feedback.text;
            else if (motionCoordinator != null && motionCoordinator.AnnouncementActive) feedback.text = "";
        }

        private void SetMotionVisible(GameObject panel, bool visible)
        {
            if (motionCoordinator != null) motionCoordinator.SetPanelVisible(panel, visible);
            else panel.SetActive(visible);
        }

        private string NextWaveDescription()
        {
            if (session.IsSandbox) return "点击右上方更多操作中的“沙盒配置”，编辑下一波、建筑参数与出生点。";
            if (session.Battle.WaveNumber >= session.Battle.Settings.Waves.Count) return "";
            var next = session.Battle.Settings.Waves[session.Battle.WaveNumber];
            var preview = new StringBuilder("下一波：");
            foreach (var group in next.Groups) preview.Append(session.Battle.Settings.GetEnemy(group.Kind).DisplayName).Append(" ").Append(group.Count).Append("（").Append(group.EntranceIndex < 0 ? "交替入口" : FormalWaveIntelPresenter.EntranceName(session.Battle.SpawnPoints[group.EntranceIndex], group.EntranceIndex)).Append("） ");
            if (next.Groups.Count == 0) preview.Append(next.EnemyCount).Append(" 名敌人");
            return preview.ToString();
        }
        private string QuoteLabel(string prefix, long cost, int count) => count == 0 ? "无需维修" : session.IsSandbox ? prefix + "（免费）" : prefix + " " + cost + (cost > session.World.Credits ? "（还差 " + (cost - session.World.Credits) + "）" : "");
        private void RefreshSelection(bool editing)
        {
            var preview = GetPlacementPreview(session);
            var enemy = preview == null ? session.SelectedEnemy : null;
            var building = preview == null ? session.SelectedBuilding : null;
            bool railgun = building != null && building.Kind == BuildingKind.PiercingRailgun;
            bool resonance = building != null && building.Kind == BuildingKind.ResonanceMagneticTower;
            bool moving = building != null && session.GridInput.MovingBuildingId == building.Id;
            foreach (var button in priorityButtons) button.gameObject.SetActive(preview == null && (!resonance || building.IsEnergyDestination));
            if (railgunFacingControls != null) railgunFacingControls.SetActive(railgun && editing);
            if (railgunFacingLabel != null && railgun) railgunFacingLabel.text = moving
                ? "预览主方向 " + session.GridInput.PreviewFacingDegrees + "°"
                : "主方向 " + building.FacingDegrees + "°";
            if (railgunFacingLeft != null) railgunFacingLeft.interactable = railgun && editing && !session.GridInput.HasPlacement;
            if (railgunFacingRight != null) railgunFacingRight.interactable = railgun && editing && !session.GridInput.HasPlacement;
            // 已保存的滚动区随对象类型分配空间，建筑不为敌人头像留出空白。
            selectionScroll.offsetMin = new Vector2(20, editing && building != null ? 180 : enemy != null ? 90 : 24);
            selectionScroll.offsetMax = new Vector2(-20, enemy != null ? -164 : -88);
            if (enemyPortrait != null) { enemyPortrait.gameObject.SetActive(enemy != null); if (enemy != null && enemyPortraits != null && (int)enemy.Kind < enemyPortraits.Length) enemyPortrait.sprite = enemyPortraits[(int)enemy.Kind]; }
            locateEnemy.gameObject.SetActive(enemy != null && enemy.IsAlive);
            if (preview != null)
            {
                move.interactable = sell.interactable = repair.interactable = false;
                foreach (var button in priorityButtons) button.interactable = false;
                selectionTitle.text = preview.DisplayName + " · 建筑预览";
                healthFill.fillAmount = 1;
                text.Clear().Append("尚未建造 · 确认放置后扣费\n最大生命 ").Append(preview.MaximumHealth)
                    .Append("\n占地 ").Append(session.GridInput.PreviewWidth).Append(" × ").Append(session.GridInput.PreviewDepth).Append(" 格")
                    .Append("\n建造费用 ").Append(session.IsSandbox ? "免费" : preview.PurchaseCost + " 资金")
                    .Append("\n左键确认 · R 旋转 90°\n右键 / Esc 取消");
                if (preview.Resonance != null) text.Append("\n\n").Append(ResonanceRules(preview.Resonance));
                else text.Append("\n储能容量 ").Append(Energy(preview.EnergyCapacity));
                bool previewTurret = preview.Kind == BuildingKind.Turret || preview.Kind == BuildingKind.BurstTurret ||
                    preview.Kind == BuildingKind.AreaTurret || preview.Kind == BuildingKind.PiercingRailgun;
                if (previewTurret)
                {
                    var weapon = session.World.GetTurretDefinition(preview.Kind);
                    text.Append("\n攻击伤害 ").Append(weapon.Damage).Append("\n攻击范围 ").Append(weapon.Range.ToString("0.##")).Append(" 格")
                        .Append("\n射击耗能 ").Append(Energy(weapon.EnergyPerShot)).Append("\n最短射击周期 ")
                        .Append(((weapon.ChargeTicks + weapon.CooldownTicks) / (double)session.World.Settings.TicksPerSecond).ToString("0.##")).Append(" 秒");
                }
                else if (preview.Resonance == null)
                {
                    if (preview.Kind == BuildingKind.Reactor)
                        text.Append("\n每秒产能 ").Append(Energy(preview.ProductionPerTick * session.World.Settings.TicksPerSecond));
                    if (preview.Kind == BuildingKind.Battery)
                        text.Append("\n每秒最大充电 ").Append(Energy(preview.MaximumChargePerTick * session.World.Settings.TicksPerSecond));
                    text.Append("\n单次输出量 ").Append(Energy(preview.OutputAmount)).Append("\n传输范围 ").Append(preview.TransmissionRange.ToString("0.##")).Append(" 格")
                        .Append("\n输出间隔 ").Append((preview.OutputIntervalTicks / (double)session.World.Settings.TicksPerSecond).ToString("0.##")).Append(" 秒");
                }
                text.Append("\n\n").Append(BuildingDescription(preview.Kind));
                selectionBody.text = text.ToString();
                repairLabel.text = "尚未建造";
                return;
            }
            if (enemy != null)
            {
                var definition = enemy.Definition;
                selectionTitle.text = definition.DisplayName + (enemy.IsAlive ? "" : " · 已消灭");
                healthFill.fillAmount = (float)enemy.Health / enemy.MaximumHealth;
                selectionBody.text = "生命 " + enemy.Health + " / " + enemy.MaximumHealth + "\n" + definition.AttackDescription + "\n伤害 " + definition.AttackDamage + "   间隔 " + (definition.AttackIntervalTicks / (float)session.World.Settings.TicksPerSecond).ToString("0.##") + " 秒\n攻击距离 " + definition.AttackRange.ToString("0.##") + "   移速 " + enemy.CurrentMovementSpeed.ToString("0.##") + "\n\n最终目标：" + BuildingName(enemy.FinalTargetBuildingId) + "\n当前攻击：" + BuildingName(enemy.CurrentAttackBuildingId) + "\n当前行动：" + ActionDescription(enemy.CurrentAction) + "\n\n" + definition.Description;
                if (!enemy.IsAlive) { if (deadEnemyId != enemy.Id) { deadEnemyId = enemy.Id; deathShownAt = Time.unscaledTime; } if (Time.unscaledTime - deathShownAt > 2) { session.ClearSelection(); showSelection = false; } }
                return;
            }
            healthFill.fillAmount = building == null ? 0 : (float)building.Health / building.Definition.MaximumHealth;
            if (building == null) { selectionTitle.text = "防线详情"; selectionBody.text = "点击建筑或敌人查看状态。\n鼠标双击单位居中并跟随。\n\n准备阶段可建设和" + (session.World.HasUnlimitedFunds ? "免费维修" : "付费维修") + "；战斗阶段专注观察。\n\n中键拖动　滚轮缩放（7–42 格）\nQ/E：旋转　WASD：平移\n跟随时平移、拖动或点选其他对象退出\nHome：返回基地视角\n空格：暂停菜单\nEsc：取消放置或暂停"; return; }
            selectionTitle.text = building.Definition.DisplayName;
            text.Clear().Append("耐久 ").Append(building.Health).Append(" / ").Append(building.Definition.MaximumHealth);
            if (!resonance || building.Definition.Resonance.RequiresEnergy)
                text.Append("\n实际储能 ").Append(Energy(building.StoredEnergy)).Append(" / ").Append(Energy(building.Definition.EnergyCapacity))
                    .Append("\n有效在途供给 ").Append(Energy(building.CommittedIncoming)).Append("\n实际到货 ").Append(Energy(building.ReceivedLastSecond)).Append(" / 秒");
            if (resonance)
                text.Append("\n\n").Append(ResonanceStatus(building, session.World.CurrentTick, session.World.Settings.TicksPerSecond))
                    .Append("\n修复共振 ").Append(building.RepairResonance).Append(" / ").Append(building.Definition.Resonance.RepairThreshold)
                    .Append("\n磁暴共振 ").Append(building.BurstResonance).Append(" / ").Append(building.Definition.Resonance.BurstThreshold)
                    .Append("\n累计获得共振 ").Append(building.TotalResonanceReceived).Append("\n磁暴释放 ").Append(building.ResonanceBurstsReleased).Append(" 次")
                    .Append("\n累计造成伤害 ").Append(building.DamageDealt)
                    .Append("\n前线反馈 ").Append(HasResonanceFeedback(building, session.World.CurrentTick, session.World.Settings.TicksPerSecond) ? "已就绪" : "未激活")
                    .Append("\n\n").Append(ResonanceRules(building.Definition.Resonance));
            if (building.Kind == BuildingKind.Battery) text.Append("\n实际放电 ").Append(Energy(building.DischargedLastSecond)).Append(" / 秒\n净变化 ").Append(Energy(building.NetEnergyChangeLastSecond)).Append(" / 秒\n").Append(building.ReceivedLastSecond > 0 && building.DischargedLastSecond > 0 ? "充放电中" : building.DischargedLastSecond > 0 ? "放电中" : building.ReceivedLastSecond > 0 ? "充电中" : "储能待命");
            if (building.IsTurret)
            {
                var weapon = session.World.GetTurretDefinition(building.Kind);
                string action = RailgunChargeStatus(building) ?? (building.CombatState == TurretCombatState.Charging ? "正在蓄力 " + (building.ChargeProgress * 100).ToString("0") + "%" :
                    building.CombatState == TurretCombatState.Aiming ? "正在转向瞄准" :
                    building.CombatState == TurretCombatState.WaitingForEnergy ? "能量不足，等待供给" :
                    building.CombatState == TurretCombatState.CoolingDown ? "准备下一次攻击" :
                    building.CombatState == TurretCombatState.Fired ? "正在射击" : "寻找射程内的敌人");
                text.Append("\n\n").Append(action).Append("\n单次伤害 ").Append(weapon.Damage)
                    .Append("\n攻击范围 ").Append(weapon.Range).Append("\n每次耗能 ").Append(Energy(weapon.EnergyPerShot));
                if (railgun) text.Append("\n最小射程 ").Append(weapon.MinimumRange).Append(" 格")
                    .Append("\n主方向 ").Append(building.FacingDegrees).Append("°，左右各 ").Append(weapon.FireArcHalfDegrees).Append("°")
                    .Append("\n最多贯穿 ").Append(weapon.MaximumPierceTargets).Append(" 名，每次保留 ").Append((weapon.PierceDamageMultiplier * 100).ToString("0")).Append("% 伤害")
                    .Append("\n蓄力 ").Append((weapon.ChargeTicks / (double)session.World.Settings.TicksPerSecond).ToString("0.##")).Append(" 秒")
                    .Append("\n最短射击周期 ").Append(((weapon.ChargeTicks + weapon.CooldownTicks) / (double)session.World.Settings.TicksPerSecond).ToString("0.##")).Append(" 秒");
                if (weapon.EnergyPerShot > building.Definition.EnergyCapacity) text.Append("\n储能容量不足，无法开火。");
            }
            if (moving) text.Append("\n\n左键确认 · R 旋转 90°\n右键 / Esc 取消");
            text.Append("\n\n").Append(BuildingDescription(building.Kind)); selectionBody.text = text.ToString();
            var quote = session.World.RepairQuote(building.Id); repairLabel.text = QuoteLabel("维修", quote.Cost, quote.BuildingCount);
            repair.interactable = editing && quote.CanRepair && (session.IsSandbox || quote.Cost <= session.World.Credits);
            move.interactable = sell.interactable = editing && building.Kind != BuildingKind.Core && building.IsAlive;
            foreach (var button in priorityButtons) button.interactable = editing && building.IsEnergyDestination;
        }
        public static string RailgunChargeStatus(BuildingState building)
        {
            if (building == null || building.Kind != BuildingKind.PiercingRailgun || building.ChargeStartedTick < 0) return null;
            string charge = building.ChargeProgress >= 1 ? "蓄力就绪" : "正在蓄力 " + (building.ChargeProgress * 100).ToString("0") + "%";
            return charge + (building.CombatState == TurretCombatState.Searching ? " · 等待目标" :
                building.CombatState == TurretCombatState.Aiming ? " · 重新瞄准" : "");
        }
        public static bool HasResonanceFeedback(BuildingState building, long tick, int ticksPerSecond)
        {
            var rules = building?.Definition.Resonance;
            return rules != null && building.ResonanceFeedbackReady && building.LastEnemyDamageTick >= 0 &&
                tick - building.LastEnemyDamageTick <= Math.Ceiling(rules.RecentDamageSeconds * ticksPerSecond);
        }
        public static string ResonanceStatus(BuildingState building, long tick, int ticksPerSecond)
        {
            if (building == null || building.Definition.Resonance == null) return "";
            if (!building.IsAlive || building.IsSold) return "已停止共振";
            if (building.ResonanceBurstResolveTick >= 0)
                return "磁暴预热 · " + (Math.Max(0, building.ResonanceBurstResolveTick - tick) / (double)ticksPerSecond).ToString("0.0") + " 秒";
            if (building.Definition.Resonance.RequiresEnergy && building.BurstResonance >= building.Definition.Resonance.BurstThreshold && building.StoredEnergy < building.Definition.Resonance.BurstEnergyCost)
                return "共振就绪 · 等待供能";
            return building.BurstResonance >= building.Definition.Resonance.BurstThreshold ? "磁暴就绪" : "接收友方攻击共振";
        }
        public static string ResonanceRules(ResonanceDefinition rules)
        {
            if (rules == null) return "";
            return "共振接收半径 " + rules.AbsorptionRange.ToString("0.##") + " 格"
                + "\n每 " + rules.RepairThreshold + " 共振修复自身 " + rules.RepairAmount + " 生命"
                + "\n每 " + rules.BurstThreshold + " 共振释放一次磁暴，预热 " + rules.BurstWindupSeconds.ToString("0.##") + " 秒"
                + "\n磁暴伤害 " + rules.BurstDamage + " · 半径 " + rules.BurstRadius.ToString("0.##") + " 格"
                + "\n减速 " + ((1 - rules.SlowMultiplier) * 100).ToString("0.#") + "% · 持续 " + rules.SlowSeconds.ToString("0.##") + " 秒"
                + "\n受敌人攻击后 " + rules.RecentDamageSeconds.ToString("0.##") + " 秒内，下次磁暴增伤 " + ((rules.RecentDamageBonusMultiplier - 1) * 100).ToString("0.#") + "%"
                + (rules.RequiresEnergy ? "\n修复耗能 " + Energy(rules.RepairEnergyCost) + " · 磁暴耗能 " + Energy(rules.BurstEnergyCost) : "\n由共振驱动，无需接入电网");
        }
        private string BuildingName(long id) { var building = session.World.FindBuilding(id); return building == null || !building.IsAlive || building.IsSold ? "无" : building.Definition.DisplayName + "（" + building.X + ", " + building.Z + "）"; }
        private void RefreshSettlement()
        {
            var battle = session.Battle; bool ordinary = battle.State == BattleState.WaveCleared;
            settlementTitle.text = session.IsSandbox && battle.LastWaveWasManuallyStopped ? "本波已手动终止" : ordinary ? "第 " + battle.WaveNumber + " 波防守完成" : battle.State == BattleState.Victory ? "守望成功" : "防线失守";
            var baseCore = session.World.FindBuilding(battle.CoreBuildingId); var quote = session.World.RepairAllQuote();
            settlementBody.text = ordinary ? "固定奖励 " + battle.LastWaveFixedReward + "\n击杀奖励 " + battle.LastWaveKillReward + "\n合计收入 " + battle.LastWaveReward + "\n\n本波建筑损失 " + battle.LastWaveBuildingLosses + "\n受损建筑 " + quote.BuildingCount + "    全部维修 " + quote.Cost + "\n核心生命 " + (baseCore?.Health ?? 0) + "\n\n" + (session.Paused ? "已暂停能源运行" : "能源网络继续运行，护盾按实际供能恢复。") : "完成波次 " + (battle.State == BattleState.Victory ? battle.WaveNumber : Math.Max(0, battle.WaveNumber - 1)) + "\n击杀数 " + battle.Kills + "\n累计收入 " + session.World.TotalIncome + "\n建筑损失 " + session.World.DestroyedBuildingCount + "\n核心剩余生命 " + (baseCore?.Health ?? 0);
            if (session.IsSandbox) settlementBody.text = "本波建筑损失 " + battle.LastWaveBuildingLosses + "\n核心生命 " + (baseCore?.Health ?? 0) + "\n\n损伤、库存与运输保持当前状态。\n可通过沙盒试验工具恢复开战前状态。";
            if ((session.IsCampaign || session.IsTutorial) && battle.State == BattleState.Victory)
            {
                if (session.IsTutorial && session.HasCompletedTutorial) settlementBody.text += "\n\n训练场已通关，第一关已解锁。";
                settlementBody.text += "\n" + session.CampaignSaveStatus;
            }
            nextPreparation.gameObject.SetActive(ordinary); settlementPause.gameObject.SetActive(ordinary);
            if (settlementPauseText != null) settlementPauseText.text = session.Paused ? "恢复能源运转" : "暂停能源运转";
            resultRestart.gameObject.SetActive(!ordinary); resultHome.gameObject.SetActive(!ordinary);
        }
        private void RefreshArchive(int entry)
        {
            int count = archiveCategory == 0 ? 1 : archiveCategory == 1 ? ShopKinds.Length + 1 : 5;
            for (int i = 0; i < archiveEntries.Length; i++) { archiveEntries[i].gameObject.SetActive(i < count); if (i >= count) continue; archiveEntryLabels[i].text = archiveCategory == 0 ? "守望垣城" : archiveCategory == 1 ? session.FindDefinition(i == ShopKinds.Length ? BuildingKind.Core : ShopKinds[i]).DisplayName : session.Battle.Settings.GetEnemy(EnemyKinds[i]).DisplayName; }
            entry = Math.Min(entry, count - 1); archiveTitle.text = archiveEntryLabels[entry].text;
            if (archiveCategory == 0) archiveBody.text = archiveStory != null ? archiveStory.text : "暂无背景资料。";
            else if (archiveCategory == 1) { var kind = entry == ShopKinds.Length ? BuildingKind.Core : ShopKinds[entry]; var definition = session.FindEffectiveDefinition(kind); archiveBody.text = BuildingDescription(kind) + "\n\n最大耐久 " + definition.MaximumHealth + (definition.Resonance == null ? "\n储能容量 " + Energy(definition.EnergyCapacity) : "\n" + ResonanceRules(definition.Resonance)) + (kind == BuildingKind.Core ? "\n核心不可购买、移动、出售或维修生命。" : session.World.HasUnlimitedFunds ? "\n购买与维修免费；出售不累计资金。" : "\n购买 " + definition.PurchaseCost + "    出售 " + definition.SaleRefund); }
            else { var definition = session.Battle.Settings.GetEnemy(EnemyKinds[entry]); archiveBody.text = definition.Description + "\n\n" + definition.AttackDescription + "\n伤害 " + definition.AttackDamage + "\n攻击间隔 " + (definition.AttackIntervalTicks / (float)session.World.Settings.TicksPerSecond).ToString("0.##") + " 秒\n移动速度 " + definition.Speed.ToString("0.##"); }
            var copy = archiveCategory == 1
                ? GetArchiveCopy(entry == ShopKinds.Length ? BuildingKind.Core : ShopKinds[entry])
                : archiveCategory == 2 ? GetArchiveCopy(EnemyKinds[entry]) : null;
            if (copy != null) archiveBody.text = copy.Body;
        }
        // Archive copy from the planning document; combat definitions remain authoritative for gameplay.
        internal sealed class ArchiveCopy
        {
            public readonly string Subtitle, Description, Behavior, Attributes;
            public ArchiveCopy(string subtitle, string description, string behavior, string attributes)
            {
                Subtitle = subtitle; Description = description; Behavior = behavior; Attributes = attributes;
            }
            public string Body => Subtitle + "\n\n" + Description
                + (string.IsNullOrEmpty(Behavior) ? "" : "\n\n能量行为：" + Behavior) + "\n\n" + Attributes;
        }

        internal static ArchiveCopy GetArchiveCopy(BuildingKind kind)
        {
            switch (kind)
            {
                case BuildingKind.Core: return new ArchiveCopy("基地的保护目标",
                    "垣城留下的最后一座枢纽。它不生产能量,只把送来的能量化作护盾;护盾破碎之前,伤害不会触及它的生命。",
                    "接收能量补充护盾,不主动消耗;被攻击时优先扣护盾。",
                    "占位：3X3\n耐久 600 / 护盾上限 160");
                case BuildingKind.Reactor: return new ArchiveCopy("持续产能",
                    "仍在按旧指令运转的产能装置。它每隔一段时间凝出一粒能量,向三格内可接收的节点轮流送出。",
                    "周围没有可接收的节点时,它会停下等待,不会积攒。",
                    "占位：2X2\n耐久 180 / 储能 64 / 产能 8 点每 0.55 秒");
                case BuildingKind.Relay: return new ArchiveCopy("轮流转发",
                    "最不起眼的部件,也是整张网络的关节。它不判断能量该去哪里,只把收到的能量依次分给所有能接收的邻居。",
                    "轮流转发,不复制、不寻路;同一粒能量走过的节点不会回头;自身储能满了就拒收,让上游等待。",
                    "占位：1X1\n耐久 90 / 储能 48 / 感知 3 格");
                case BuildingKind.Battery: return new ArchiveCopy("缓冲储能",
                    "只在身边有节点缺能时放电的蓄能柜。它看不见全局,只看得到感知范围内的事——所以它能救急,也可能在错误的位置白白蓄满。",
                    "接收并储存附近送来的能量,按局部缺能情况决定是否释放,不需要玩家手动控制。",
                    "占位：2X1\n耐久 150 / 储能 120");
                case BuildingKind.Turret: return new ArchiveCopy("快速单体",
                    "旧城的信号塔,如今打出去的是光。它自动锁定射程内最近的守城者,每发只耗很少的能量,射速很快。",
                    "储能不足时停火,等待网络补充。",
                    "占位：2X1\n耐久 240 / 储能 72 / 攻击 16 / 射程 7.5 格 / 耗能 3 每发");
                case BuildingKind.BurstTurret: return new ArchiveCopy("慢速重击",
                    "灾后用来敲碎坍塌路段的重锤,如今用来对付重甲。它每开一炮都要先喝下大量能量。",
                    "单发耗能极高,需要一条稳定、低损耗的供电线才回得起枪;供能跟不上就会长时间停火。",
                    "占位：3X2\n耐久 300 / 储能 120 / 攻击 120 / 射程 6 格 / 耗能 20 每发");
                case BuildingKind.AreaTurret: return new ArchiveCopy("范围溅射",
                    "广场除尘的旧机组,一吹就能清走一片积灰。它一次攻击会波及目标周围的一小片区域。",
                    "对成群的敌人最划算,对单个硬目标则很浪费——所以供能要算性价比。",
                    "占位：3X1\n耐久 260 / 储能 96 / 攻击 18 / 溅射 2.5 格 / 射程 5.5 格 / 耗能 10 每发");
                default: return null;
            }
        }

        internal static ArchiveCopy GetArchiveCopy(EnemyKind kind)
        {
            switch (kind)
            {
                case EnemyKind.Normal: return new ArchiveCopy("四足清扫单元",
                    "旧城留下的清障装置。它按最省力的路线走向最近的可到达建筑,到达后开始拆解。它不憎恨你,只是在执行几百年前的最后一道命令。",
                    "",
                    "耐久 48 / 速度 0.85 / 攻击 12 / 目标偏好 最近建筑");
                case EnemyKind.Demolisher: return new ArchiveCopy("双足工程单元",
                    "比巡猎者更专注的拆解者。它优先寻找中继器——城市最初造它们,是为了切断纠缠的线路,如今它切断的是你的。",
                    "",
                    "耐久 75 / 速度 0.95 / 攻击 19 / 目标偏好 中继器");
                case EnemyKind.Converger: return new ArchiveCopy("履带回收单元",
                    "厚重的履带机体,身前挂着回收斗。它绕过一切挡路的建筑,径直扑向反应堆。它一倒下,整条供能线都会跟着缺电。",
                    "",
                    "耐久 140 / 速度 0.55 / 攻击 25 / 目标偏好 反应堆");
                case EnemyKind.Fast: return new ArchiveCopy("小型清扫虫",
                    "只有巴掌大的清扫虫,成群从崖缝里涌出。它们很难造成威胁,却会逼你的炮塔为每一只都开一枪——真正被消耗的,是你的储能。",
                    "",
                    "耐久 12 / 速度 1.3 / 攻击 4 / 目标偏好 最近建筑");
                case EnemyKind.Heavy: return new ArchiveCopy("重型碾压单元",
                    "只会朝核心直线前进的重型机械。它不挑选目标,只清除挡路的建筑。想拦住它,唯一的办法是用布局把它困在火力网里。",
                    "",
                    "耐久 400 / 速度 0.35 / 攻击 40 / 目标偏好 防线核心");
                default: return null;
            }
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
                case BuildingKind.PiercingRailgun: return "蓄力后贯穿同一直线上的多个敌人，后续命中伤害逐次降低。准备阶段确定主方向，在有限射界内自动瞄准；沿通道布置收益更高，近处漏网目标需要其他炮塔处理。";
                case BuildingKind.ResonanceMagneticTower: return "接收周围友方实际攻击产生的共振，独立积累自修与磁暴进度。磁暴伤害并减缓周围敌人；短时间内受击会强化下一次爆发。缺少友方持续火力时无法自行恢复或积累共振。";
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
