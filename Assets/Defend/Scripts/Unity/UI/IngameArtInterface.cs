using System;
using Defend.Simulation;
using UnityEngine;
using UnityEngine.UI;

namespace Defend.Unity
{
    /// <summary>平面局内界面：所有控件与美术已保存到预制体；只读取权威状态更新表现。</summary>
    public sealed class IngameArtInterface : MonoBehaviour
    {
        [Serializable] public sealed class ExactDetailPlacement
        {
            public RectTransform target;
            public Vector2 compact, expanded;
        }
        [SerializeField] private bool exactReferenceLayout;
        [SerializeField] private Image exactDetailBackground;
        [SerializeField] private Sprite exactDetailCompact, exactDetailExpanded;
        [SerializeField] private ExactDetailPlacement[] exactDetailPlacements;
        [SerializeField] private InterfaceDetailLayoutMotion detailLayoutMotion;
        [SerializeField] private InterfacePanelMotion shopCategoryMotion, toolsMotion, tooltipMotion;
        [SerializeField] private ScrollRect exactShopScroll;
        [SerializeField] private InterfaceButtonMotion[] exactCategoryMotions;
        [SerializeField] private Text exactTagline, exactDescription, exactSupplySummary, exactEnergyCaption;
        private int exactCategory, displayedCategory = -1, lastVisibleCard = -1;
        public void ShowDefensiveBuildings() { ShowShopCategory(0); }
        // Keep existing serialized category-button listeners valid after separating the shop.
        public void ShowAllBuildings() { ShowDefensiveBuildings(); }
        public void ShowFunctionalBuildings() { ShowShopCategory(1); }
        private void ShowShopCategory(int category)
        {
            if (exactCategory == category) return;
            // A category change abandons only the unsubmitted building preview, not the world selection.
            if (session != null && session.GridInput != null && session.GridInput.HasPlacement && session.GridInput.PreviewDefinition != null)
                session.GridInput.Cancel();
            exactCategory = category;
            HideTooltip();
        }
        [SerializeField] private bool referenceLayout;
        [SerializeField] private GameObject referenceShopOpen;
        public bool ShopExpanded => true;
        public bool ShowStartWave => true;
        // Retained for existing serialized listeners; the preparation dock now stays available.
        public void ToggleShop() { }
        [SerializeField] private BuildingThumbnail selectedThumbnail;
        [SerializeField] private InterfaceButtonMotion[] shopStateMotions;
        [SerializeField] private Text[] referenceShopNames;
        [SerializeField] private Text referenceSelectionStatus;
        private bool LegacyLayout => approvedLayout && !referenceLayout;
        [SerializeField] private InterfaceValueBarMotion cooldownMotion, waveProgressMotion;
        [SerializeField] private InterfaceValueBarMotion coreHealthMotion, coreShieldMotion, selectedHealthMotion, selectedEnergyMotion, selectedHealthOverviewMotion;
        [SerializeField] private InterfaceCardMotion[] cardMotions;
        [SerializeField] private Sprite corePortrait;
        [SerializeField] private Sprite railgunPortrait;
        [SerializeField] private Text railgunCardName;
        [SerializeField] private Text railgunSummary;
        [SerializeField] private Sprite resonancePortrait;
        [SerializeField] private Text resonanceCardName, resonanceSummary;
        [SerializeField] private GameObject priorityControls;
        [SerializeField] private Image[] shopSelectionMarkers;
        [SerializeField] private Button[] commandAuxiliaryControls;
        [SerializeField] private Image[] sandboxTabIndicators;
        [SerializeField] private InterfacePanelMotion[] sandboxPageMotions;
        private object motionWorld;
        private long motionBuilding, motionEnemy;
        private BuildingDefinition motionPlacementPreview;
        private bool snapSelectionValues;
        [Serializable] public sealed class Readout
        {
            public GameObject root;
            public Image caption, icon, fill;
            public Text value;
        }
        [SerializeField] private IngameArtLibrary library;
        [SerializeField] private RectTransform mapViewport;
        [SerializeField] private GameObject details;
        [SerializeField] private Image phase, selectionIcon, selectionState, settlementHeading, pauseState;
        [SerializeField] private Image coreHealth, coreShield, waveProgress, selectedHealth, selectedEnergy, cooldown;
        [SerializeField] private GameObject cooldownRow, selectedEnergyRow, combatLock, energyThreshold;
        [SerializeField] private Text credits, coreHealthValue, coreShieldValue, waveValue, waveCounts, speedValue, selectedHealthValue, selectedEnergyValue, cooldownValue, selectionName;
        [SerializeField] private Text repairValue, repairAllValue, startValue, feedback;
        [SerializeField] private Text formalCompletion;
        [SerializeField] private Text[] shopPrices;
        [SerializeField] private Readout[] detailRows, resultRows;
        [SerializeField] private Button[] priorities, shopButtons, sandboxTabs;
        [SerializeField] private GameObject[] sandboxPages;
        [SerializeField] private Button networkButton, rangesButton, speedButton, pauseButton, resumeObservation;
        [SerializeField] private GameObject tooltipRoot;
        [SerializeField] private Image tooltipCaption;
        [SerializeField] private Text tooltipText;
        [SerializeField] private Image energyRunning;
        [SerializeField] private bool approvedLayout;
        [SerializeField] private GameObject[] approvedOriginalCards, approvedAlternativeCards;
        [SerializeField] private Image[] approvedCardNames;
        [SerializeField] private Image approvedDock, approvedDetails;
        [SerializeField] private Image selectedHealthOverviewFill;
        [SerializeField] private Button placementCancel;
        [SerializeField] private Sprite approvedNormalDock;
        [SerializeField] private Sprite[] approvedCardNormalSprites, approvedCardSelectedSprites;
        [SerializeField] private Material areaTurretNormalSurface;
        [SerializeField] private Material approvedDockSurface, approvedDetailsSurface;
        [SerializeField] private GameObject approvedTurretHero, approvedTools;
        public void ToggleApprovedTools()
        {
            if (approvedTools == null) return;
            SetToolsVisible(!(toolsMotion != null ? toolsMotion.TargetVisible : approvedTools.activeSelf));
        }
        private void SetToolsVisible(bool visible, bool immediate = false)
        {
            if (toolsMotion != null) toolsMotion.SetVisible(visible, immediate);
            else if (approvedTools != null) approvedTools.SetActive(visible);
        }
        [SerializeField] private Text selectedHealthOverview, liveSummary;
        private GameSessionRoot session;
        private string tooltipKey;
        private RectTransform tooltipSource;
        private float tooltipDue;
        private bool tooltipPending;
        private bool observedToolsState;
        private BattleState previousToolsState;
        private readonly Vector3[] tooltipCorners = new Vector3[4];
        private static readonly BuildingKind[] Kinds = { BuildingKind.Reactor, BuildingKind.Relay, BuildingKind.Battery, BuildingKind.Turret, BuildingKind.BurstTurret, BuildingKind.AreaTurret, BuildingKind.PiercingRailgun, BuildingKind.ResonanceMagneticTower };
        private static readonly Color Green = new Color(.94f, .92f, .87f), Cyan = new Color(.34f, .78f, .83f), Amber = new Color(.83f, .73f, .54f), Red = new Color(.98f, .46f, .36f), Ink = new Color(.94f, .92f, .87f);

        public void Refresh(GameSessionRoot root)
        {
            session = root;
            if (root == null || root.World == null || library == null) return;
            for(int i=0; sandboxTabs!=null && sandboxPages!=null && i<sandboxTabs.Length && i<sandboxPages.Length; i++)
            {
                bool active = sandboxPageMotions != null && i < sandboxPageMotions.Length && sandboxPageMotions[i] != null
                    ? sandboxPageMotions[i].TargetVisible : sandboxPages[i].activeSelf;
                var surface = sandboxTabs[i].targetGraphic as Image;
                if (referenceLayout) SetButtonState(sandboxTabs[i], active);
                else if (surface != null) { surface.sprite = library.Find(active ? "primaryButton" : "iconButton"); surface.color = Color.white; }
                if (sandboxTabIndicators != null && i < sandboxTabIndicators.Length) sandboxTabIndicators[i].enabled = active;
                foreach(var label in sandboxTabs[i].GetComponentsInChildren<Text>(true)) label.color = active ? Ink : new Color(.65f,.65f,.63f);
            }
            var world = root.World; var battle = root.Battle;
            bool snapCoreValues = !ReferenceEquals(motionWorld, world);
            if (snapCoreValues || root.AtStartPage || (observedToolsState && previousToolsState != battle.State))
            {
                SetToolsVisible(false, snapCoreValues || root.AtStartPage);
                HideTooltip(snapCoreValues || root.AtStartPage);
            }
            previousToolsState = battle.State; observedToolsState = true;
            if (exactReferenceLayout && snapCoreValues)
            {
                exactCategory = 0; displayedCategory = -1; lastVisibleCard = -1;
            }
            bool editing = world.CanEditBuildings && !root.AtStartPage;
            if (referenceShopOpen != null) referenceShopOpen.SetActive(false);
            var preview = DefenseInterface.GetPlacementPreview(root);
            var building = preview == null ? root.SelectedBuilding : null;
            var enemy = preview == null ? root.SelectedEnemy : null;
            long buildingId = building?.Id ?? 0, enemyId = enemy?.Id ?? 0;
            bool previewChanged = !ReferenceEquals(motionPlacementPreview, preview);
            snapSelectionValues = snapCoreValues || motionBuilding != buildingId || motionEnemy != enemyId || previewChanged;
            motionWorld = world; motionBuilding = buildingId; motionEnemy = enemyId;
            motionPlacementPreview = preview;
            bool selected = preview != null || building != null || enemy != null;
            if (!snapCoreValues && previewChanged && selected) details.GetComponent<InterfacePanelMotion>()?.ReplayContent();
            if(placementCancel!=null)placementCancel.gameObject.SetActive(root.GridInput.HasPlacement);
            bool originalArt=building!=null && building.Kind==BuildingKind.Turret && !root.GridInput.HasPlacement;
            if(!referenceLayout && approvedDock!=null){approvedDock.sprite=library.Find(originalArt?"replica_originalDock":"replica_cleanDock");approvedDock.material=originalArt?approvedDockSurface:null;}
            if(!referenceLayout && approvedNormalDock!=null){approvedDock.sprite=approvedNormalDock;approvedDock.material=areaTurretNormalSurface;}
            if(!referenceLayout && approvedDetails!=null){approvedDetails.sprite=library.Find(originalArt?"replica_originalDetails":"replica_cleanDetails");approvedDetails.material=originalArt?approvedDetailsSurface:null;}
            var detailsRect = (RectTransform)details.transform;
            float canvasHeight = Math.Max(1, ((RectTransform)detailsRect.parent).rect.height);
            float bottom = (editing ? (referenceLayout ? 218f : approvedLayout ? 207f : 172f) : 32f) / canvasHeight;
            float top = 1 - (referenceLayout ? 78f : approvedLayout ? 72f : 56f) / canvasHeight;
            float detailWidth=referenceLayout ? 454 : approvedLayout?468:328;
            if (exactReferenceLayout)
            {
                bool compact = editing;
                detailsRect.anchorMin = detailsRect.anchorMax = detailsRect.pivot = new Vector2(1, 1);
                detailsRect.anchoredPosition = new Vector2(0, -74);
                if (detailLayoutMotion != null) detailLayoutMotion.SetExpanded(!compact, snapCoreValues);
                else
                {
                    detailsRect.sizeDelta = new Vector2(468, compact ? 670 : 867);
                    if (exactDetailBackground != null)
                    {
                        exactDetailBackground.sprite = compact ? exactDetailCompact : exactDetailExpanded;
                        exactDetailBackground.rectTransform.sizeDelta = detailsRect.sizeDelta;
                    }
                    if (exactDetailPlacements != null)
                        foreach (var placement in exactDetailPlacements)
                            if (placement.target != null) placement.target.anchoredPosition = compact ? placement.compact : placement.expanded;
                }
                RefreshExactShop(root);
            }
            else
            {
                detailsRect.anchorMin = new Vector2(1, bottom);
                detailsRect.anchorMax = new Vector2(1, top);
                detailsRect.offsetMin = new Vector2(-detailWidth, 0); detailsRect.offsetMax = Vector2.zero;
            }
            // 所有界面覆盖在完整战场上，商店和详情显隐不改变相机视口。
            mapViewport.anchorMin = Vector2.zero;
            mapViewport.anchorMax = Vector2.one;
            mapViewport.offsetMin = mapViewport.offsetMax = Vector2.zero;
            SetSprite(phase, editing ? "准备阶段" : battle.IsFinished ? battle.State == BattleState.Victory ? "守望成功" : "防线失守" : battle.State == BattleState.WaveCleared ? "防守完成" : "战斗进行中");
            credits.text = root.IsSandbox ? "∞" : world.Credits.ToString();
            speedValue.text = root.Speed.ToString("0") + "×";
            if (!referenceLayout && approvedTools != null && commandAuxiliaryControls != null && commandAuxiliaryControls.Length > 0)
            {
                bool auxiliaryVisible = false;
                foreach (var control in commandAuxiliaryControls)
                    if (control != null && control.gameObject.activeSelf) { auxiliaryVisible = true; break; }
                var toolsRect = (RectTransform)approvedTools.transform;
                float width = auxiliaryVisible ? 648 : 488;
                if (!Mathf.Approximately(toolsRect.sizeDelta.x, width))
                    toolsRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
            }
            var core = world.FindBuilding(battle.CoreBuildingId);
            SetAnimatedValue(coreHealth, coreHealthMotion, core == null ? 0 : Ratio(core.Health, core.Definition.MaximumHealth), snapCoreValues);
            SetAnimatedValue(coreShield, coreShieldMotion, core == null ? 0 : Ratio(core.StoredEnergy, core.Definition.EnergyCapacity), snapCoreValues);
            coreHealthValue.text = core == null ? "—" : core.Health + " / " + core.Definition.MaximumHealth;
            coreShieldValue.text = core == null ? "—" : Energy(core.StoredEnergy) + " / " + Energy(core.Definition.EnergyCapacity);
            int number = battle.WaveNumber + (editing ? 1 : 0);
            waveValue.text = root.IsSandbox ? number.ToString() : Math.Min(number, battle.Settings.Waves.Count) + " / " + battle.Settings.Waves.Count;
            int total = 0, cleared = 0;
            if (root.IsSandbox)
            {
                total = battle.SandboxSpawnedCount + battle.RemainingToSpawn;
                cleared = battle.SandboxKilledCount;
            }
            else if (battle.WaveNumber > 0)
            {
                total = battle.Settings.Waves[battle.WaveNumber - 1].EnemyCount;
                cleared = Math.Max(0, total - battle.RemainingToSpawn - battle.Enemies.Count);
            }
            SetAnimatedValue(waveProgress, waveProgressMotion, editing ? 0 : Ratio(cleared, total), snapCoreValues);
            waveCounts.text = editing ? PreviewWave(root) : cleared + " / " + total;
            startValue.text = exactReferenceLayout ? root.IsSandbox ? "迎接敌潮，守护核心" : "第 " + (battle.WaveNumber + 1) + " 波 · 共 " + battle.Settings.Waves.Count + " 波" : "—  " + (battle.WaveNumber + 1).ToString() + "  —";
            var quote = world.RepairAllQuote(); repairAllValue.text = root.IsSandbox ? "0" : quote.Cost.ToString();
            for (int i = 0; i < shopPrices.Length; i++)
            {
                if (referenceShopNames != null && i < referenceShopNames.Length && referenceShopNames[i] != null) referenceShopNames[i].text = root.FindEffectiveDefinition(Kinds[i]).DisplayName;
                shopPrices[i].text = root.IsSandbox ? "0" : root.FindEffectiveDefinition(Kinds[i]).PurchaseCost.ToString();
                if(shopButtons!=null && i<shopButtons.Length)
                {
                    bool chosen = root.GridInput.HasPlacement ? root.GridInput.PreviewDefinition != null && root.GridInput.PreviewDefinition.Kind == Kinds[i] : building != null && building.Kind == Kinds[i];
                    if (referenceLayout)
                    {
                        if (shopStateMotions != null && i < shopStateMotions.Length) shopStateMotions[i]?.SetSelected(chosen);
                        continue;
                    }
                    if (shopSelectionMarkers != null && i < shopSelectionMarkers.Length && shopSelectionMarkers[i] != null)
                        shopSelectionMarkers[i].enabled = chosen;
                    if(approvedLayout && approvedOriginalCards!=null && i<approvedOriginalCards.Length){bool original=building!=null && building.Kind==BuildingKind.Turret && !root.GridInput.HasPlacement;approvedOriginalCards[i].SetActive(original);approvedAlternativeCards[i].SetActive(!original);}
                    if(approvedLayout && approvedCardNames!=null && i<approvedCardNames.Length){approvedCardNames[i].enabled=!originalArt;approvedCardNames[i].color=chosen?new Color(.12f,.11f,.09f):Color.white;}
                    var card=(Image)shopButtons[i].targetGraphic;
                    if(approvedCardNormalSprites!=null&&i<approvedCardNormalSprites.Length&&approvedCardNormalSprites[i]!=null){
                        approvedOriginalCards[i].SetActive(false);approvedAlternativeCards[i].SetActive(false);approvedCardNames[i].enabled=false;
                        var nextSprite=chosen?approvedCardSelectedSprites[i]:approvedCardNormalSprites[i];
                        var nextMaterial=i==5&&!chosen?areaTurretNormalSurface:null;
                        card.color=Color.white;card.type=Image.Type.Simple;
                        if(cardMotions!=null&&i<cardMotions.Length&&cardMotions[i]!=null)cardMotions[i].Apply(chosen,nextSprite,nextMaterial);
                        else {card.sprite=nextSprite;card.material=nextMaterial;}
                        if(i==5){approvedCardNames[i].enabled=!chosen;approvedCardNames[i].color=shopButtons[i].IsInteractable()?Color.white:new Color(.6f,.6f,.6f);}
                    }
                    else {
                        card.sprite=library.Find(chosen?"selectedCard":"buildingCard");card.color=chosen?Color.white:new Color(1,1,1,approvedLayout?0:1);
                        if(approvedLayout&&originalArt)card.color=new Color(1,1,1,0);
                        if (Kinds[i] == BuildingKind.PiercingRailgun || Kinds[i] == BuildingKind.ResonanceMagneticTower)
                        {
                            card.color = chosen ? new Color(.94f,.92f,.85f) : new Color(.15f,.16f,.17f);
                            var cardName = Kinds[i] == BuildingKind.PiercingRailgun ? railgunCardName : resonanceCardName;
                            if (cardName != null) cardName.color = chosen ? new Color(.12f,.11f,.09f) : Ink;
                        }
                    }
                    foreach(var text in shopButtons[i].GetComponentsInChildren<Text>()) text.color=chosen?new Color(.12f,.11f,.09f):text==shopPrices[i]?Amber:Ink;
                }
            }
            if(!approvedLayout)SetSprite(pauseState, "pause");
            SetSprite(energyRunning, root.Paused || battle.IsFinished ? "能源已暂停" : "能源运行中");
            SetButtonState(networkButton, root.WorldViews.ShowNetwork); SetButtonState(rangesButton, root.WorldViews.ShowRanges);
            combatLock.SetActive(!editing && selected);
            if (preview != null) RefreshPlacementPreview(preview);
            else if (selected) RefreshSelected(building, enemy, editing);
            else if (priorityControls != null) priorityControls.SetActive(false);
            if(LegacyLayout && building!=null && building.Kind==BuildingKind.Turret)selectionIcon.gameObject.SetActive(false);
            RefreshResult(core);
            if (root.BlocksWorldInput) SetToolsVisible(false);
            if (root.BlocksWorldInput || root.GridInput.HasPlacement) HideTooltip();
            else if (tooltipRoot.activeSelf) RenderTooltip();
        }

        private void RefreshPlacementPreview(BuildingDefinition definition)
        {
            bool resonance = definition.Resonance != null;
            bool turret = definition.Kind == BuildingKind.Turret || definition.Kind == BuildingKind.BurstTurret ||
                definition.Kind == BuildingKind.AreaTurret || definition.Kind == BuildingKind.PiercingRailgun;
            if (priorityControls != null) priorityControls.SetActive(false);
            foreach (var button in priorities) { button.gameObject.SetActive(false); button.interactable = false; }
            if (resonanceSummary != null) resonanceSummary.gameObject.SetActive(false);
            if (railgunSummary != null) railgunSummary.gameObject.SetActive(false);
            if (approvedTurretHero != null) approvedTurretHero.SetActive(false);
            foreach (var row in detailRows) row.root.SetActive(false);
            cooldownRow.SetActive(false); energyThreshold.SetActive(false);
            selectedEnergyRow.SetActive(!resonance || definition.Resonance.RequiresEnergy);
            selectionName.gameObject.SetActive(true); selectionIcon.gameObject.SetActive(true);
            selectionName.text = definition.DisplayName;
            PresentBuildingPortrait(definition.Kind);
            if (referenceSelectionStatus != null) referenceSelectionStatus.text = "建筑预览 · 尚未建造";
            if (!approvedLayout) SetSprite(selectionState, "准备阶段");
            selectedHealth.color = Color.white;
            SetAnimatedValue(selectedHealth, selectedHealthMotion, 1, snapSelectionValues);
            SetAnimatedValue(selectedHealthOverviewFill, selectedHealthOverviewMotion, 1, snapSelectionValues);
            selectedHealthValue.text = "最大 " + definition.MaximumHealth;
            if (selectedHealthOverview != null) selectedHealthOverview.text = "最大生命 " + definition.MaximumHealth;
            SetAnimatedValue(selectedEnergy, selectedEnergyMotion, 1, snapSelectionValues);
            selectedEnergyValue.text = Energy(definition.EnergyCapacity);
            if (exactEnergyCaption != null) exactEnergyCaption.text = "储能容量";
            repairValue.text = "";
            if (liveSummary != null)
            {
                liveSummary.gameObject.SetActive(!referenceLayout);
                liveSummary.text = "左键确认 · R 旋转 90°\n右键 / Esc 取消 · 确认后扣费";
            }
            if (exactReferenceLayout)
            {
                exactTagline.text = "建筑预览 · 尚未建造";
                exactDescription.text = DefenseInterface.BuildingDescription(definition.Kind);
                exactSupplySummary.text = "建造费用 " + (session.IsSandbox ? "免费" : definition.PurchaseCost + " 资金") +
                    " · 占地 " + session.GridInput.PreviewWidth + " × " + session.GridInput.PreviewDepth + " 格\n左键确认 · R 旋转 90° · 右键 / Esc 取消";
            }
            if (resonance)
            {
                var rules = definition.Resonance;
                SetRow(0, "修复共振", "repair", rules.RepairThreshold + " → " + rules.RepairAmount + " 生命");
                SetRow(1, "磁暴共振", "reactor", rules.BurstThreshold + " / 次");
                SetRow(2, "伤害与范围", "heavyTurret", rules.BurstDamage + " · " + rules.BurstRadius.ToString("0.##") + " 格");
                SetRow(3, "减速效果", "speed", ((1 - rules.SlowMultiplier) * 100).ToString("0.#") + "% · " + rules.SlowSeconds.ToString("0.##") + " 秒");
            }
            else if (turret)
            {
                var weapon = session.World.GetTurretDefinition(definition.Kind);
                SetRow(0, "攻击伤害", "heavyTurret", weapon.Damage.ToString());
                SetRow(1, "攻击范围", "range", weapon.Range.ToString("0.##") + " 格");
                SetRow(2, "射击耗能", "reactor", Energy(weapon.EnergyPerShot));
                SetRow(3, "射击间隔", "next", Seconds(weapon.ChargeTicks + weapon.CooldownTicks) + " 秒");
                if (definition.Kind == BuildingKind.PiercingRailgun)
                {
                    SetRow(0, "伤害与贯穿", "heavyTurret", weapon.Damage + " · 最多 " + weapon.MaximumPierceTargets + " 名");
                    SetRow(1, "射程与射界", "range", weapon.MinimumRange.ToString("0.#") + "–" + weapon.Range.ToString("0.#") + " 格 · ±" + weapon.FireArcHalfDegrees.ToString("0.#") + "°");
                    SetRow(3, "蓄力与周期", "next", Seconds(weapon.ChargeTicks) + " / " + Seconds(weapon.ChargeTicks + weapon.CooldownTicks) + " 秒");
                }
                if (weapon.EnergyPerShot > definition.EnergyCapacity && exactSupplySummary != null)
                    exactSupplySummary.text += "\n储能容量不足，无法开火";
            }
            else
            {
                SetRow(0, definition.Kind == BuildingKind.Reactor ? "每秒产能" : definition.Kind == BuildingKind.Battery ? "每秒最大充电" : "储能容量",
                    definition.Kind == BuildingKind.Battery ? "battery" : "reactor", definition.Kind == BuildingKind.Reactor
                        ? Energy(definition.ProductionPerTick * session.World.Settings.TicksPerSecond)
                        : definition.Kind == BuildingKind.Battery ? Energy(definition.MaximumChargePerTick * session.World.Settings.TicksPerSecond) : Energy(definition.EnergyCapacity));
                SetRow(1, "单次输出量", "next", Energy(definition.OutputAmount));
                SetRow(2, "输出间隔", "speed", Seconds(definition.OutputIntervalTicks) + " 秒");
                SetRow(3, "传输范围", "range", definition.TransmissionRange.ToString("0.##") + " 格");
            }
        }

        private void PresentBuildingPortrait(BuildingKind kind)
        {
            SetSprite(selectionIcon, kind == BuildingKind.Core ? "core" : (approvedLayout ? "replica_portrait_" : "portrait_") + Icon(kind));
            Sprite portrait = kind == BuildingKind.Core ? corePortrait : kind == BuildingKind.PiercingRailgun ? railgunPortrait : kind == BuildingKind.ResonanceMagneticTower ? resonancePortrait : null;
            if (portrait != null) { selectionIcon.sprite = portrait; selectionIcon.enabled = true; selectionIcon.preserveAspect = true; }
            if (referenceLayout) selectedThumbnail?.SetBuilding(kind);
        }

        private void RefreshSelected(BuildingState building, EnemyState enemy, bool editing)
        {
            bool resonance = building != null && building.Kind == BuildingKind.ResonanceMagneticTower;
            bool resonanceProgress = resonance && exactReferenceLayout;
            bool showPriority = !resonance || building.IsEnergyDestination;
            if (priorityControls != null) priorityControls.SetActive(showPriority);
            foreach (var button in priorities) button.gameObject.SetActive(showPriority);
            if (resonanceSummary != null) resonanceSummary.gameObject.SetActive(resonance);
            bool showRailgunSummary = LegacyLayout && building != null && building.Kind == BuildingKind.PiercingRailgun && railgunSummary != null;
            if (railgunSummary != null) railgunSummary.gameObject.SetActive(showRailgunSummary);
            if (liveSummary != null) liveSummary.gameObject.SetActive(!showRailgunSummary);
            foreach (var row in detailRows) row.root.SetActive(false);
            selectedEnergyRow.SetActive(building != null && (resonanceProgress || !resonance || building.Definition.Resonance.RequiresEnergy));
            cooldownRow.SetActive(building != null && (building.IsTurret || resonanceProgress));
            energyThreshold.SetActive(false);
            if (enemy != null)
            {
                if(approvedTurretHero!=null)approvedTurretHero.SetActive(false);
                selectionName.gameObject.SetActive(true);selectionIcon.gameObject.SetActive(true);
                selectionName.text = enemy.Definition.DisplayName;
                if (referenceLayout) repairValue.text = "";
                if (exactReferenceLayout)
                {
                    exactTagline.text = "敌方单位";
                    exactDescription.text = "正在逼近防线。\n保护能源网络与核心。";
                    exactSupplySummary.text = "";
                }
                if (referenceLayout && referenceSelectionStatus != null) referenceSelectionStatus.text = enemy.IsAlive ? "敌方单位" : "目标已消灭";
                SetSprite(selectionIcon, "enemy");
                if (!approvedLayout) SetSprite(selectionState, enemy.IsAlive ? "战斗进行中" : "防守完成");
                if(approvedLayout){selectedHealthOverview.text=enemy.Health+" / "+enemy.MaximumHealth;liveSummary.text="";}
                float healthRatio = Ratio(enemy.Health, enemy.MaximumHealth);
                SetAnimatedValue(selectedHealth, selectedHealthMotion, healthRatio, snapSelectionValues);
                SetAnimatedValue(selectedHealthOverviewFill, selectedHealthOverviewMotion, healthRatio, snapSelectionValues);
                selectedHealth.color = Red; selectedHealthValue.text = enemy.Health + " / " + enemy.MaximumHealth;
                SetRow(0, "攻击伤害", "heavyTurret", enemy.Definition.AttackDamage.ToString());
                SetRow(1, "攻击间隔", "speed", Seconds(enemy.Definition.AttackIntervalTicks) + " s");
                SetRow(2, "攻击范围", "range", enemy.Definition.AttackRange.ToString("0.##"));
                SetRow(3, "移动速度", "move", enemy.CurrentMovementSpeed.ToString("0.##"));
                SetRow(4, null, "locate", TargetName(enemy.CurrentAttackBuildingId));
                SetRow(5, null, "core", TargetName(enemy.FinalTargetBuildingId));
                if(approvedLayout){detailRows[4].root.SetActive(false);detailRows[5].root.SetActive(false);}
                return;
            }
            if(approvedTurretHero!=null)approvedTurretHero.SetActive(!referenceLayout && building.Kind==BuildingKind.Turret);
            if(LegacyLayout){selectionName.gameObject.SetActive(building.Kind!=BuildingKind.Turret);selectionIcon.gameObject.SetActive(building.Kind!=BuildingKind.Turret);}
            selectionName.text = building.Definition.DisplayName;
            PresentBuildingPortrait(building.Kind);
            selectedHealth.color = Color.white;
            float buildingHealthRatio = Ratio(building.Health, building.Definition.MaximumHealth);
            SetAnimatedValue(selectedHealth, selectedHealthMotion, buildingHealthRatio, snapSelectionValues);
            selectedHealthValue.text = building.Health + " / " + building.Definition.MaximumHealth;
            if (selectedHealthOverview != null) selectedHealthOverview.text = selectedHealthValue.text;
            SetAnimatedValue(selectedHealthOverviewFill, selectedHealthOverviewMotion, buildingHealthRatio, snapSelectionValues);
            if (!resonance)
            {
                SetAnimatedValue(selectedEnergy, selectedEnergyMotion, Ratio(building.StoredEnergy, building.Definition.EnergyCapacity), snapSelectionValues);
                selectedEnergyValue.text = Energy(building.StoredEnergy) + " / " + Energy(building.Definition.EnergyCapacity);
                if (exactEnergyCaption != null) exactEnergyCaption.text = "当前储能";
            }
            SetRow(0, "有效在途供给", "network", Energy(building.CommittedIncoming));
            SetRow(1, "实际到货", "relay", Energy(building.ReceivedLastSecond) + " /s");
            string state = "实际储能";
            if (building.IsTurret)
            {
                var weapon = session.World.GetTurretDefinition(building.Kind);
                long remaining = Math.Max(0, building.NextShotTick - session.World.CurrentTick);
                SetAnimatedValue(cooldown, cooldownMotion, 1 - Ratio(remaining, weapon.CooldownTicks), snapSelectionValues);
                cooldownValue.text = Seconds(remaining) + " s";
                state = DefenseInterface.RailgunChargeStatus(building) ?? (building.CombatState == TurretCombatState.Charging ? "正在蓄力 " + (building.ChargeProgress * 100).ToString("0") + "%" : building.CombatState == TurretCombatState.WaitingForEnergy ? "等待供能" : building.CombatState == TurretCombatState.Aiming ? "正在瞄准" : building.CombatState == TurretCombatState.CoolingDown ? "攻击冷却" : building.CombatState == TurretCombatState.Fired ? "正在射击" : "搜索目标");
                if (building.ChargeStartedTick >= 0) { SetAnimatedValue(cooldown, cooldownMotion, (float)building.ChargeProgress, snapSelectionValues); cooldownValue.text = building.ChargeProgress >= 1 ? "蓄力就绪" : ((1 - building.ChargeProgress) * weapon.ChargeTicks / session.World.Settings.TicksPerSecond).ToString("0.0") + " s"; }
                SetRow(2, "攻击伤害", "heavyTurret", weapon.Damage.ToString());
                SetRow(3, "攻击范围", "range", weapon.Range.ToString("0.##"));
                SetRow(4, "射击耗能", "reactor", Energy(weapon.EnergyPerShot));
                if (weapon.EnergyPerShot <= building.Definition.EnergyCapacity)
                {
                    energyThreshold.SetActive(true);
                    var marker = (RectTransform)energyThreshold.transform;
                    float fraction = Ratio(weapon.EnergyPerShot, building.Definition.EnergyCapacity);
                    marker.anchorMin = new Vector2(fraction, exactReferenceLayout ? .5f : 0); marker.anchorMax = new Vector2(fraction, exactReferenceLayout ? .5f : 1);
                }
                else { state = "等待供能"; SetRow(5, "储能容量", "warning", "< " + Energy(weapon.EnergyPerShot)); }
            }
            else if (building.Kind == BuildingKind.Battery)
            {
                SetRow(2, null, "next", Energy(building.DischargedLastSecond) + " /s");
                SetRow(3, null, "battery", (building.NetEnergyChangeLastSecond > 0 ? "+" : "") + Energy(building.NetEnergyChangeLastSecond) + " /s");
                SetRow(4, "传输范围", "range", building.Definition.TransmissionRange.ToString("0.##"));
            }
            else if (building.Kind != BuildingKind.Core)
            {
                SetRow(2, "单次输出量", "next", Energy(building.Definition.OutputAmount));
                SetRow(3, "传输范围", "range", building.Definition.TransmissionRange.ToString("0.##"));
                if (building.Kind == BuildingKind.Reactor) SetRow(4, "每秒产能", "reactor", Energy(building.Definition.ProductionPerTick * session.World.Settings.TicksPerSecond) + " /s");
            }
            if(LegacyLayout)
            {
                selectedHealthOverview.text=selectedHealthValue.text;
                liveSummary.text=(building.IsTurret ? state + " · 冷却 " + cooldownValue.text : building.Kind == BuildingKind.Core ? "储能用于抵挡伤害" : string.IsNullOrEmpty(building.EnergyStatus) ? "等待供能" : building.EnergyStatus) + "\n有效在途 " + Energy(building.CommittedIncoming) + " · 实际到货 " + Energy(building.ReceivedLastSecond) + "/秒";
                for(int i=0;i<detailRows.Length;i++)detailRows[i].root.SetActive(false);
                if(building.IsTurret){var weapon=session.World.GetTurretDefinition(building.Kind);SetRow(0,"攻击伤害","heavyTurret",weapon.Damage.ToString());SetRow(1,"攻击范围","range",weapon.Range.ToString("0.##"));SetRow(2,"射击耗能","reactor",Energy(weapon.EnergyPerShot));SetRow(3,"射击周期","next",Seconds(weapon.CooldownTicks+weapon.ChargeTicks)+" 秒");}
                else if(building.Kind==BuildingKind.Battery){SetRow(0,"实际放电","next",Energy(building.DischargedLastSecond)+"/秒");SetRow(1,"储能变化","battery",(building.NetEnergyChangeLastSecond>0?"+":"")+Energy(building.NetEnergyChangeLastSecond)+"/秒");SetRow(2,"传输范围","range",building.Definition.TransmissionRange.ToString("0.##"));SetRow(3,"单次输出量","next",Energy(building.Definition.OutputAmount));}
                else if(building.Kind==BuildingKind.Reactor){SetRow(0,"每秒产能","reactor",Energy(building.Definition.ProductionPerTick*session.World.Settings.TicksPerSecond));SetRow(1,"实际输出","next",Energy(building.DischargedLastSecond)+"/秒");SetRow(2,"传输范围","range",building.Definition.TransmissionRange.ToString("0.##"));SetRow(3,"单次输出量","next",Energy(building.Definition.OutputAmount));}
                else if(building.Kind==BuildingKind.Core){SetRow(0,"护盾容量","core",Energy(building.Definition.EnergyCapacity));SetRow(1,"有效在途供给","network",Energy(building.CommittedIncoming));SetRow(2,"实际到货","relay",Energy(building.ReceivedLastSecond)+"/秒");liveSummary.text="核心不能移动、出售或维修\n优先保障能源供给";}
                else {SetRow(0,"单次输出量","next",Energy(building.Definition.OutputAmount));SetRow(1,"传输范围","range",building.Definition.TransmissionRange.ToString("0.##"));SetRow(2,"实际输出","relay",Energy(building.DischargedLastSecond)+"/秒");SetRow(3,"供能优先级","priority",building.PriorityWeight.ToString());}
                cooldownRow.SetActive(resonanceProgress);
            }
            if (exactReferenceLayout)
            {
                foreach (var row in detailRows) row.root.SetActive(false);
                if (building.IsTurret)
                {
                    var weapon = session.World.GetTurretDefinition(building.Kind);
                    SetRow(0, "攻击伤害", "heavyTurret", weapon.Damage.ToString());
                    SetRow(1, "攻击范围", "range", weapon.Range.ToString("0.##"));
                    SetRow(2, "射击耗能", "reactor", Energy(weapon.EnergyPerShot));
                    SetRow(3, "射击间隔", "next", Seconds(weapon.CooldownTicks + weapon.ChargeTicks) + " 秒");
                }
                else if (building.Kind == BuildingKind.Reactor)
                {
                    SetRow(0, "每秒产能", "reactor", Energy(building.Definition.ProductionPerTick * session.World.Settings.TicksPerSecond));
                    SetRow(1, "实际输出", "next", Energy(building.DischargedLastSecond) + " /秒");
                    SetRow(2, "传输范围", "range", building.Definition.TransmissionRange.ToString("0.##"));
                    SetRow(3, "单次输出量", "next", Energy(building.Definition.OutputAmount));
                }
                else if (building.Kind == BuildingKind.Battery)
                {
                    SetRow(0, "实际放电", "next", Energy(building.DischargedLastSecond) + " /秒");
                    SetRow(1, "储能变化", "battery", (building.NetEnergyChangeLastSecond > 0 ? "+" : "") + Energy(building.NetEnergyChangeLastSecond) + " /秒");
                    SetRow(2, "传输范围", "range", building.Definition.TransmissionRange.ToString("0.##"));
                    SetRow(3, "单次输出量", "next", Energy(building.Definition.OutputAmount));
                }
                else
                {
                    SetRow(0, "单次输出量", "next", Energy(building.Definition.OutputAmount));
                    SetRow(1, "传输范围", "range", building.Definition.TransmissionRange.ToString("0.##"));
                    SetRow(2, "实际到货", "relay", Energy(building.ReceivedLastSecond) + " /秒");
                    SetRow(3, "有效在途供给", "network", Energy(building.CommittedIncoming));
                }
                exactSupplySummary.text = "有效在途 " + Energy(building.CommittedIncoming) + " · 实际到货 " + Energy(building.ReceivedLastSecond) + " /秒";
                exactTagline.text = building.Kind == BuildingKind.Reactor ? "能源供给 · 稳定输送" : building.Kind == BuildingKind.Relay ? "连接网络 · 扩展供能" : building.Kind == BuildingKind.Battery ? "蓄能缓冲 · 持续供给" : building.Kind == BuildingKind.Turret ? "迅捷锁敌 · 精准守护" : building.Kind == BuildingKind.BurstTurret ? "重火压制 · 稳定输出" : building.Kind == BuildingKind.AreaTurret ? "范围打击 · 阵地清扫" : building.Kind == BuildingKind.PiercingRailgun ? "直线贯穿 · 蓄力重击" : "核心防御 · 护盾守护";
                exactDescription.text = building.IsTurret ? "连接供能网络。\n储能满足条件后自动射击。" : building.Kind == BuildingKind.Core ? "储能用于抵挡伤害。\n优先保障能源供给。" : "构筑防线的供能网络。\n让每一份能源抵达前线。";
            }
            if (building.Kind == BuildingKind.PiercingRailgun)
            {
                var weapon = session.World.GetTurretDefinition(building.Kind);
                SetRow(0, "伤害与贯穿", "heavyTurret", weapon.Damage + " · 最多 " + weapon.MaximumPierceTargets + " 名");
                SetRow(1, "射程与射界", "range", weapon.MinimumRange.ToString("0.#") + "–" + weapon.Range.ToString("0.#") + " 格 · ±" + weapon.FireArcHalfDegrees.ToString("0.#") + "°");
                SetRow(2, "射击耗能", "reactor", Energy(weapon.EnergyPerShot));
                SetRow(3, "蓄力与周期", "next", Seconds(weapon.ChargeTicks) + " / " + Seconds(weapon.ChargeTicks + weapon.CooldownTicks) + " 秒");
                if (LegacyLayout) (railgunSummary != null ? railgunSummary : liveSummary).text = state + " · 主方向 " + building.FacingDegrees + "°\n贯穿后保留 " + (weapon.PierceDamageMultiplier * 100).ToString("0") + "% · 到货 " + Energy(building.ReceivedLastSecond) + "/秒";
            }
            if (referenceLayout)
            {
                selectionName.gameObject.SetActive(true);
                selectionIcon.gameObject.SetActive(true);
                selectedThumbnail?.SetBuilding(building.Kind);
                if (referenceSelectionStatus != null)
                    referenceSelectionStatus.text = building.IsTurret ?
                        building.CombatState == TurretCombatState.Charging ? "正在蓄力" :
                        building.CombatState == TurretCombatState.WaitingForEnergy ? "等待供能" :
                        building.CombatState == TurretCombatState.Aiming ? "正在瞄准" :
                        building.CombatState == TurretCombatState.CoolingDown ? "攻击冷却" :
                        building.CombatState == TurretCombatState.Fired ? "正在射击" : "搜索目标"
                        : building.Kind == BuildingKind.Core ? "核心防御" : building.EnergyStatus;
                cooldownRow.SetActive(building.IsTurret || resonanceProgress);
                if (liveSummary != null) liveSummary.text = "";
            }
            else if (!approvedLayout) SetSprite(selectionState, state);
            if (resonance) RefreshResonanceDetails(building);
            if (session.GridInput.MovingBuildingId == building.Id)
            {
                if (exactReferenceLayout)
                    exactSupplySummary.text = "移动预览 · 占地 " + session.GridInput.PreviewWidth + " × " + session.GridInput.PreviewDepth +
                        " 格\n左键确认 · R 旋转 90° · 右键 / Esc 取消";
                else if (liveSummary != null)
                {
                    if (railgunSummary != null) railgunSummary.gameObject.SetActive(false);
                    if (resonanceSummary != null) resonanceSummary.gameObject.SetActive(false);
                    liveSummary.gameObject.SetActive(true);
                    liveSummary.text = "左键确认 · R 旋转 90°\n右键 / Esc 取消 · 保留原位置与朝向";
                }
            }
            long repairCost = session.IsSandbox ? 0 : session.World.RepairQuote(building.Id).Cost;
            repairValue.text = building.Kind == BuildingKind.Core ? "核心无法维修" : !editing ? "战斗期间无法维修" : building.Health >= building.Definition.MaximumHealth ? "生命值已满" : !session.IsSandbox && repairCost > session.World.Credits ? "维修资金不足，还需 " + (repairCost-session.World.Credits) : "维修费用 " + repairCost;
            for (int i = 0; i < priorities.Length; i++) SetButtonState(priorities[i], building.PriorityWeight == (1 << i));
        }

        private void RefreshResonanceDetails(BuildingState building)
        {
            var rules = building.Definition.Resonance;
            bool feedbackReady = DefenseInterface.HasResonanceFeedback(building, session.World.CurrentTick, session.World.Settings.TicksPerSecond);
            int nextDamage = feedbackReady ? (int)Math.Min(int.MaxValue, Math.Floor(rules.BurstDamage * rules.RecentDamageBonusMultiplier)) : rules.BurstDamage;
            string status = DefenseInterface.ResonanceStatus(building, session.World.CurrentTick, session.World.Settings.TicksPerSecond);
            foreach (var row in detailRows) row.root.SetActive(false);
            SetRow(0, "修复共振", "repair", building.RepairResonance + " / " + rules.RepairThreshold);
            SetRow(1, "磁暴共振", "reactor", building.BurstResonance + " / " + rules.BurstThreshold);
            SetRow(2, "伤害与范围", "heavyTurret", nextDamage + " · " + rules.BurstRadius.ToString("0.##") + " 格");
            SetRow(3, "减速效果", "speed", ((1 - rules.SlowMultiplier) * 100).ToString("0.#") + "% · " + rules.SlowSeconds.ToString("0.##") + " 秒");
            selectedEnergyRow.SetActive(exactReferenceLayout || rules.RequiresEnergy);
            cooldownRow.SetActive(exactReferenceLayout); energyThreshold.SetActive(false);
            string support = "接收 " + rules.AbsorptionRange.ToString("0.##") + " 格 · 每次修复 " + rules.RepairAmount + " 生命";
            string history = "磁暴 " + building.ResonanceBurstsReleased + " 次 · 累计共振 " + building.TotalResonanceReceived;
            string feedback = feedbackReady ? "前线反馈已就绪 · 下次伤害 " + nextDamage : "受击后 " + rules.RecentDamageSeconds.ToString("0.##") + " 秒内，下次磁暴增伤 " + ((rules.RecentDamageBonusMultiplier - 1) * 100).ToString("0.#") + "%";
            if (resonanceSummary != null) resonanceSummary.text = status + "\n" + support + "\n" + history + "\n" + feedback;
            if (liveSummary != null) { liveSummary.gameObject.SetActive(resonanceSummary == null); liveSummary.text = status + "\n" + support + "\n" + history + "\n" + feedback; }
            if (referenceSelectionStatus != null) referenceSelectionStatus.text = status;
            if (exactReferenceLayout)
            {
                exactTagline.text = "共振联动 · 前线守护";
                exactDescription.text = "每 " + rules.RepairThreshold + " 共振自修 " + rules.RepairAmount + " 生命\n每 " + rules.BurstThreshold + " 共振释放磁暴";
                exactSupplySummary.text = status + " · 已释放 " + building.ResonanceBurstsReleased + " 次";
                SetRow(0, "接收半径", "range", rules.AbsorptionRange.ToString("0.##") + " 格");
                SetRow(1, "单次修复", "repair", rules.RepairAmount + " 生命");
                if (exactEnergyCaption != null) exactEnergyCaption.text = "修复共振";
                selectedEnergyValue.text = building.RepairResonance + " / " + rules.RepairThreshold;
                referenceSelectionStatus.text = "磁暴共振";
                cooldownValue.text = building.BurstResonance + " / " + rules.BurstThreshold;
                SetAnimatedValue(selectedEnergy, selectedEnergyMotion, Ratio(building.RepairResonance, rules.RepairThreshold), snapSelectionValues);
                SetAnimatedValue(cooldown, cooldownMotion, Ratio(building.BurstResonance, rules.BurstThreshold), snapSelectionValues);
                if (liveSummary != null) liveSummary.gameObject.SetActive(false);
            }
            else if (!approvedLayout) SetSprite(selectionState, status);
        }

        private void RefreshResult(BuildingState core)
        {
            var battle = session.Battle;
            if (battle.State != BattleState.WaveCleared && !battle.IsFinished) return;
            bool ordinary = battle.State == BattleState.WaveCleared;
            if (formalCompletion != null)
            {
                bool victory = !session.IsSandbox && battle.State == BattleState.Victory;
                formalCompletion.text = victory
                    ? session.IsTutorial ? "教学通关 · 第一关已解锁" : "已守住全部 " + battle.Settings.Waves.Count + " 波进攻 · 正式关卡完成"
                    : session.IsTutorial ? "守望训练场 · 完整通关后解锁第一关" : "战况与防线";
                if (victory && (session.IsTutorial || session.IsCampaign))
                    formalCompletion.text += session.CampaignSavePending ? " · 保存待重试" : " · 记录已保存";
            }
            SetSprite(settlementHeading, session.IsSandbox && battle.LastWaveWasManuallyStopped ? "本波已手动终止" : ordinary ? "防守完成" : battle.State == BattleState.Victory ? "守望成功" : "防线失守");
            foreach (var row in resultRows) row.root.SetActive(false);
            if (session.IsSandbox)
            {
                Result(0, "建筑损失", "delete", battle.LastWaveBuildingLosses.ToString());
                Result(1, "核心生命", "health", (core?.Health ?? 0).ToString());
                Result(2, "累计击杀", "enemy", battle.SandboxKilledCount.ToString());
            }
            else if (ordinary)
            {
                Result(0, "固定奖励", "credits", battle.LastWaveFixedReward.ToString());
                Result(1, "击杀奖励", "enemy", battle.LastWaveKillReward.ToString());
                Result(2, "合计收入", "credits", battle.LastWaveReward.ToString());
                Result(3, "建筑损失", "delete", battle.LastWaveBuildingLosses.ToString());
                Result(4, "全部维修", "repair", session.World.RepairAllQuote().Cost.ToString());
                Result(5, "核心生命", "health", (core?.Health ?? 0).ToString());
            }
            else
            {
                Result(0, "完成波次", "wave", (battle.State == BattleState.Victory ? battle.WaveNumber : Math.Max(0, battle.WaveNumber - 1)).ToString());
                Result(1, "累计击杀", "enemy", battle.Kills.ToString());
                Result(2, "累计收入", "credits", session.World.TotalIncome.ToString());
                Result(3, "建筑损失", "delete", session.World.DestroyedBuildingCount.ToString());
                Result(4, "核心生命", "health", (core?.Health ?? 0).ToString());
            }
        }
        private void RefreshExactShop(GameSessionRoot root)
        {
            if (exactShopScroll == null || exactShopScroll.content == null || exactShopScroll.viewport == null || shopButtons == null) return;
            int chosen = -1;
            for (int i = 0; i < Kinds.Length && i < shopButtons.Length; i++)
                if (root.GridInput.HasPlacement ? root.GridInput.PreviewDefinition != null && root.GridInput.PreviewDefinition.Kind == Kinds[i] : root.SelectedBuilding != null && root.SelectedBuilding.Kind == Kinds[i]) { chosen = i; break; }
            // The global 1–8 mapping stays unchanged; placement selects its actual kind's category.
            if (root.GridInput.HasPlacement && chosen >= 0) exactCategory = ShopCategory(Kinds[chosen]);
            if (displayedCategory != exactCategory)
            {
                bool changingCategory = displayedCategory >= 0;
                float direction = exactCategory > displayedCategory ? 1 : -1;
                displayedCategory = exactCategory;
                float nextPosition = 0;
                const float cardGap = 10;
                for (int i = 0; i < shopButtons.Length; i++)
                {
                    var button = shopButtons[i];
                    if (button == null) continue;
                    bool show = i < Kinds.Length && ShopCategory(Kinds[i]) == exactCategory;
                    button.gameObject.SetActive(show);
                    if (!show) continue;
                    var rect = (RectTransform)button.transform;
                    rect.anchoredPosition = new Vector2(nextPosition, 0);
                    nextPosition += rect.rect.width + cardGap;
                }
                exactShopScroll.StopMovement();
                exactShopScroll.content.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Max(0, nextPosition - cardGap));
                exactShopScroll.content.anchoredPosition = new Vector2(0, exactShopScroll.content.anchoredPosition.y);
                exactShopScroll.horizontalNormalizedPosition = 0;
                lastVisibleCard = -1;
                if (changingCategory) shopCategoryMotion?.ReplayContent(Vector2.right * (8 * direction));
            }
            if (exactCategoryMotions != null)
                for (int i = 0; i < exactCategoryMotions.Length; i++) exactCategoryMotions[i]?.SetSelected(i == exactCategory);
            if (chosen < 0 || chosen >= shopButtons.Length || shopButtons[chosen] == null || !shopButtons[chosen].gameObject.activeSelf)
            {
                lastVisibleCard = -1;
                return;
            }
            if (chosen == lastVisibleCard) return;
            lastVisibleCard = chosen;
            float overflow = exactShopScroll.content.rect.width - exactShopScroll.viewport.rect.width;
            if (overflow <= 0)
            {
                exactShopScroll.StopMovement(); exactShopScroll.horizontalNormalizedPosition = 0;
                return;
            }
            var chosenRect = (RectTransform)shopButtons[chosen].transform;
            float left = chosenRect.anchoredPosition.x, right = left + chosenRect.rect.width;
            float current = exactShopScroll.horizontalNormalizedPosition * overflow;
            exactShopScroll.StopMovement();
            if (left < current) exactShopScroll.horizontalNormalizedPosition = Mathf.Clamp01(left / overflow);
            else if (right > current + exactShopScroll.viewport.rect.width) exactShopScroll.horizontalNormalizedPosition = Mathf.Clamp01((right - exactShopScroll.viewport.rect.width) / overflow);
        }
        private static int ShopCategory(BuildingKind kind)
        {
            switch (kind)
            {
                case BuildingKind.Reactor:
                case BuildingKind.Relay:
                case BuildingKind.Battery:
                    return 1;
                case BuildingKind.Turret:
                case BuildingKind.BurstTurret:
                case BuildingKind.AreaTurret:
                case BuildingKind.PiercingRailgun:
                case BuildingKind.ResonanceMagneticTower:
                    return 0;
                default:
                    return -1;
            }
        }
        private static void SetAnimatedValue(Image image, InterfaceValueBarMotion motion, float value, bool snap)
        {
            if (motion != null) motion.SetValue(value, snap);
            else if (image != null) image.fillAmount = value;
        }
        private void SetRow(int index, string label, string icon, string value) { Fill(detailRows[index], label, icon, value); }
        private void Result(int index, string label, string icon, string value) { Fill(resultRows[index], label, icon, value); }
        private void Fill(Readout row, string label, string icon, string value)
        {
            row.root.SetActive(true); SetSprite(row.caption, label);
            if (exactReferenceLayout && (label == "射击间隔" || label == "蓄力与周期" || label == "攻击间隔")) icon = "cooldown";
            SetSprite(row.icon, icon);
            if(LegacyLayout && (label=="攻击伤害" || label=="攻击范围" || label=="射击耗能"))row.icon.enabled=false;
            row.value.text = value;
        }
        private void SetSprite(Image image, string key)
        {
            if (image == null) return;
            image.gameObject.SetActive(key != null);
            var caption = image.GetComponentInChildren<Text>(true);
            string fixedKey = LegacyLayout ? key=="准备阶段"?"replica_phasePreparation":key=="攻击伤害"?"replica_detailDamageLabel":key=="攻击范围"?"replica_detailRangeLabel":key=="射击耗能"?"replica_detailEnergyCostLabel":null:null;
            if(fixedKey!=null){if(caption!=null)caption.enabled=false;image.sprite=library.Find(fixedKey);image.enabled=true;image.material=null;image.color=Color.white;return;}
            if(caption!=null)caption.enabled=true;
            if (caption != null) { caption.text = key; image.enabled = false; }
            else
            {
                string exactIcon = key == "heavyTurret" ? "damage" : key == "reactor" ? "energy" : key == "speed" ? "cooldown" : key;
                image.sprite = exactReferenceLayout ? library.Find("exact8_icon_" + exactIcon) ?? library.Find(key) : library.Find(key);
            }
        }
        [SerializeField] private Sprite commandControlSurface;
        private void SetButtonState(Button button, bool active)
        {
            if (button == null) return;
            if (referenceLayout)
            {
                button.GetComponent<InterfaceButtonMotion>()?.SetSelected(active);
                return;
            }
            var image = (Image)button.targetGraphic;
            image.sprite = commandControlSurface != null ? commandControlSurface : library.Find(active ? "primaryButton" : "iconButton");
            image.color = commandControlSurface != null ? (active ? new Color(.94f,.92f,.85f) : new Color(.16f,.175f,.18f)) : Color.white;
            foreach(var label in button.GetComponentsInChildren<Text>()) label.color=active?new Color(.13f,.12f,.1f):Ink;
            foreach (var symbol in button.GetComponentsInChildren<Image>(true))
                if (symbol != image && symbol.transform != button.transform && symbol.name == "美术_图标")
                    symbol.color = active ? new Color(.13f,.12f,.1f) : Color.white;
        }
        private string TargetName(long id) { var target = session.World.FindBuilding(id); return target == null || !target.IsAlive ? "—" : "(" + target.X + ", " + target.Z + ")"; }
        private string Seconds(long ticks) => ((double)ticks / session.World.Settings.TicksPerSecond).ToString("0.0");
        private static float Ratio(long value, long total) => total <= 0 ? 0 : Mathf.Clamp01((float)((double)value / total));
        private static string Energy(long value) => (value / 1000d).ToString("0.#");
        public static string Icon(BuildingKind kind)
        { switch (kind) { case BuildingKind.Reactor: return "reactor"; case BuildingKind.Relay: return "relay"; case BuildingKind.Battery: return "battery"; case BuildingKind.Core: return "core"; case BuildingKind.BurstTurret: return "heavyTurret"; case BuildingKind.AreaTurret: return "areaTurret"; case BuildingKind.PiercingRailgun: return "railgun"; case BuildingKind.ResonanceMagneticTower: return "resonanceTower"; default: return "rapidTurret"; } }
        private static string PreviewWave(GameSessionRoot root)
        {
            if (root.IsSandbox) { int count = 0; foreach (var group in root.Battle.SandboxGroups) count += group.Count; return count.ToString(); }
            return root.Battle.WaveNumber < root.Battle.Settings.Waves.Count ? root.Battle.Settings.Waves[root.Battle.WaveNumber].EnemyCount.ToString() : "—";
        }
        public void ShowTooltip(string key)
        {
            tooltipKey = key; tooltipPending = false;
            RenderTooltip();
        }
        public void RequestTooltip(string key, RectTransform source)
        {
            if (tooltipRoot == null || session == null || session.World == null || !ShopExpanded ||
                !session.World.CanEditBuildings || session.BlocksWorldInput || session.GridInput.HasPlacement) return;
            bool alreadyVisible = tooltipMotion != null ? tooltipMotion.TargetVisible : tooltipRoot.activeSelf;
            tooltipKey = key; tooltipSource = source;
            tooltipDue = Time.unscaledTime + .18f; tooltipPending = true;
            if (alreadyVisible) { tooltipPending = false; RenderTooltip(); }
        }
        private void Update()
        {
            if (tooltipPending && Time.unscaledTime >= tooltipDue) { tooltipPending = false; RenderTooltip(); }
        }
        private void RenderTooltip()
        {
            if (tooltipRoot == null || session == null) return;
            if (session.World == null || !ShopExpanded || !session.World.CanEditBuildings) { HideTooltip(true); return; }
            if (session.BlocksWorldInput || session.GridInput.HasPlacement || string.IsNullOrEmpty(tooltipKey)) { HideTooltip(); return; }
            int index = -1;
            for (int i = 0; i < Kinds.Length; i++)
            {
                var definition = session.FindEffectiveDefinition(Kinds[i]);
                if (definition.DisplayName == tooltipKey) { index = i; break; }
            }
            // Decorative labels and modal controls do not need empty description panels.
            if (index < 0) { HideTooltip(); return; }
            var item = session.FindEffectiveDefinition(Kinds[index]);
            SetSprite(tooltipCaption, tooltipKey);
            tooltipText.text = "占地 " + item.Width + " × " + item.Depth + "\n建造费用 " + (session.IsSandbox ? "0" : item.PurchaseCost.ToString());
            if (!session.World.CanEditBuildings) tooltipText.text += "\n战斗期间无法建造";
            else if (!session.IsSandbox && item.PurchaseCost > session.World.Credits) tooltipText.text += "\n资金不足，还需 " + (item.PurchaseCost - session.World.Credits);
            RectTransform source = tooltipSource != null ? tooltipSource : (RectTransform)shopButtons[index].transform;
            var rect = (RectTransform)tooltipRoot.transform;
            var parent = (RectTransform)rect.parent;
            source.GetWorldCorners(tooltipCorners);
            Vector3 top = parent.InverseTransformPoint((tooltipCorners[1] + tooltipCorners[2]) * .5f);
            rect.anchorMin = rect.anchorMax = new Vector2(.5f,.5f); rect.pivot = new Vector2(.5f,0);
            float x = Mathf.Clamp(top.x, parent.rect.xMin + rect.rect.width * .5f + 16, parent.rect.xMax - rect.rect.width * .5f - 16);
            float y = Mathf.Min(top.y + 12, parent.rect.yMax - rect.rect.height - 16);
            rect.anchoredPosition = new Vector2(x,y) - parent.rect.center;
            if (tooltipMotion != null) tooltipMotion.SetVisible(true);
            else tooltipRoot.SetActive(true);
        }
        public void HideTooltip(RectTransform source) { if (tooltipSource == source) HideTooltip(); }
        public void HideTooltip() { HideTooltip(false); }
        private void HideTooltip(bool immediate)
        {
            if (tooltipMotion != null) tooltipMotion.SetVisible(false, immediate);
            else if (tooltipRoot != null) tooltipRoot.SetActive(false);
            tooltipKey = null; tooltipSource = null; tooltipPending = false;
        }
        private void OnDisable()
        {
            HideTooltip(true);
            SetToolsVisible(false, true);
            observedToolsState = false;
        }
    }
}
