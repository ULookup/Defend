using System.Collections.Generic;
using System.Text;
using Defend.Simulation;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Defend.Unity
{
    /// <summary>读取基础资料、切换已保存页面和展示对象，不创建 UI 或修改模拟。</summary>
    public sealed class OutgameInterface : MonoBehaviour
    {
        [SerializeField] private GameObject homePage, archivePage, helpPage, settingsPage, storyPanel, objectPanel, entryListPanel, storyEntryPanel, imageDisplay, modelDisplay, imagePlaceholder;
        [SerializeField] private Button[] archiveCategories, archiveEntries, helpTopics;
        [SerializeField] private Image[] archiveCategoryIcons, archiveCategoryPlates, archiveEntryImages, helpTopicImages, previewModePlates;
        [SerializeField] private Text[] archiveCategoryLabels, archiveEntryLabels;
        [SerializeField] private RectTransform archiveCategoryStrip, entryListContent;
        [SerializeField] private ScrollRect entryScroll, storyScroll, descriptionScroll, attributesScroll, helpScroll;
        [SerializeField] private Text storyTitle, storyBody, archiveTitle, archiveDescription, archiveAttributes, helpTitle, helpBody, imagePlaceholderLabel, musicPercent, effectsPercent;
        [SerializeField] private Text archiveCategoryHint;
        [SerializeField] private Text homeLevelTitle, homeLevelSummary;
        [SerializeField] private Text archiveRole, archiveKeyFacts, archiveTacticsTitle, archiveTactics;
        [SerializeField] private Image archiveImage;
        [SerializeField] private Button pictureMode, modelMode, resetModel;
        [SerializeField] private ArchiveModelPreview modelPreview;
        [SerializeField] private Sprite entryNormal, entrySelected;
        [SerializeField] private Sprite[] categoryNormalIcons, categorySelectedIcons, buildingPictures, enemyPictures;
        private GameSessionRoot session;
        private DefenseAudio audioSettings;
        private BattleSettings baseBattle;
        private int category;
        private readonly int[] selection = new int[3];
        private readonly float[] listPosition = { 1, 1, 1 };
        private bool archiveWasOpen, helpWasOpen, modelModeSelected;
        private GameObject activePage;
        private Selectable[] controls;
        private readonly List<Selectable> visibleControls = new List<Selectable>();
        private static readonly BuildingKind[] Buildings = { BuildingKind.Reactor, BuildingKind.Relay, BuildingKind.Battery, BuildingKind.Turret, BuildingKind.BurstTurret, BuildingKind.AreaTurret, BuildingKind.Core, BuildingKind.PiercingRailgun, BuildingKind.ResonanceMagneticTower };
        private static readonly EnemyKind[] Enemies = { EnemyKind.Normal, EnemyKind.Demolisher, EnemyKind.Converger, EnemyKind.Fast, EnemyKind.Heavy };
        private static readonly Color Ink = new Color(.035f, .29f, .40f), Cyan = new Color(.17f, .9f, .95f);
        private static readonly string[] HelpNames = { "布置工事", "准备与战斗", "能源与电池", "维修与优先级", "敌人与通路", "镜头与暂停", "重新开始" };
        private static readonly string[] HelpContent = {
            "在准备阶段选择底部建筑，再点击合法位置确认放置。数字键 1–8 和小键盘对应八种建筑，预览不扣款。按 R 从地图上方向下看顺时针旋转 90°，占地和模型一起旋转；连续建造保留朝向，切换建筑或取消后恢复默认朝向。\n\n选中普通建筑可免费移动、出售或付费维修。移动预览也可按 R 旋转，取消会保留原位置与原朝向。贯穿轨道炮的主射击方向随建筑一起转动，并保留主方向按钮的 15° 微调。\n\n共振磁暴塔依靠附近炮塔的实际攻击积累共振，自行修复并释放磁暴；它无需接入电网。\n\n右键或 Esc 取消放置。核心不能购买、移动或出售。",
            "主动点击开始波次进入战斗，建设和优先级调整随即锁定。\n\n成功清波后领取固定与击杀奖励，点击继续准备再整备。\n\n核心生命不能维修，生命归零则失败。开始守望仅进入准备阶段，由你决定何时开始第一波。",
            "反应堆产生能量，中继器延伸网络。实际到货才进入库存或补充核心护盾；预约不等于到货。\n\n电池可同时充放电，经合法线路支援缺能工事，并按储能比例差相互补能。\n\n准备、战斗、波次结算持续供能，主动暂停才停止。",
            "维修价格按缺失耐久向上取整。全部维修先检查总资金，不足则全部不执行。\n\n选中炮塔、核心或电池可在准备阶段调整供能优先级。\n\n维修不补能源，出售按固定价格返还。沙盒模式免除资金限制，能源仍需实际运输与到货。",
            "敌人占据空间，狭路会排队，有空间时可避让。挡路建筑和最终目标分别显示。\n\n战役开放全部 40×40 地面；墙体占格、建筑占地和敌人入口不能建造。墙体会阻挡实际碰撞到它的弹体，炮塔无法命中目标时会停止发射。请将主炮位朝向开敞通路。\n\n点击任何敌人可查看实时生命、行动、目标和攻击属性；定位按钮可把镜头移到它的位置，鼠标双击敌人可持续跟随。目标死亡或离场后自动停止跟随。\n\n垣城档案展示各类敌人的基础资料，实际对局属性以场上详情为准。",
            "鼠标中键拖动，滚轮缩放；Q / E 旋转，W / A / S / D 平移，Home 返回基地视角。镜头距离为 7–42 格，默认 21 格；拉近时视角自动降低。\n\n鼠标双击建筑或敌人，镜头恢复 21 格距离并保留水平朝向，平滑居中并跟随。跟随时滚轮缩放、Q / E 旋转均围绕目标；W / A / S / D、中键拖动、单击其他单位或空白退出跟随。Home 退出跟随并返回基地。\n\n点击空白取消选择。空格打开暂停菜单，Esc 取消放置或打开暂停。暂停保留跟随目标，收起菜单后可继续观察；准备阶段还可继续布置，进入摆放或出生点编辑时退出跟随，开波会解除暂停。\n\n局外页面：Tab 切换焦点，Shift + Tab 反向切换，Enter 或空格激活，Esc 返回上一层。档案模型区支持拖动旋转、滚轮缩放和重置视角。",
            "开始守望进入关卡选择。先通关新手引导的两波训练，解锁第一关，再按顺序完成六关战役。教学和战役通关后会自动保存本机进度，重新打开游戏仍保留成绩；失败、重新部署和沙盒试验不会解锁关卡。\n\n存档只保留通关与解锁进度。暂停菜单中的重新部署会清空当前对局并重开同一关；返回选关或关闭游戏同样不保留局内建筑、资金和波次。返回或重新部署前会显示确认提示。\n\n关卡选择页显示保存状态。失败或完成所有波次后，可重新部署或返回关卡选择。选关页可返回标题，再从独立入口进入沙盒。"
        };

        public void Initialize(GameSessionRoot root, DefenseAudio audio, TextAsset story)
        {
            session = root; audioSettings = audio; baseBattle = root.CreateArchiveBattleSettings();
            if (homeLevelTitle != null) { homeLevelTitle.text = ""; homeLevelTitle.gameObject.SetActive(false); }
            if (homeLevelSummary != null) { homeLevelSummary.text = ""; homeLevelSummary.gameObject.SetActive(false); }
            controls = GetComponentsInChildren<Selectable>(true);
            storyTitle.text = "背景故事"; storyBody.text = story != null ? story.text : "暂无背景资料。";
            for (int i = 0; i < archiveCategories.Length; i++) { int value = i; archiveCategories[i].onClick.AddListener(() => SelectCategory(value)); }
            for (int i = 0; i < archiveEntries.Length; i++) { int value = i; archiveEntries[i].onClick.AddListener(() => SelectEntry(value)); }
            for (int i = 0; i < helpTopics.Length; i++) { int value = i; helpTopics[i].onClick.AddListener(() => SelectHelp(value)); }
            pictureMode.onClick.AddListener(() => SetMode(false)); modelMode.onClick.AddListener(() => SetMode(true));
            resetModel.onClick.AddListener(modelPreview.ResetView);
            SelectHelp(0); SelectCategory(0);
        }

        public void Present(bool home, bool archive, bool help, bool settings, GameObject additionalPage = null)
        {
            GameObject nextPage = additionalPage != null ? additionalPage : archive ? archivePage : help ? helpPage : settings ? settingsPage : home ? homePage : null;
            if (archive && !archiveWasOpen)
            {
                modelModeSelected = false; category = 0;
                for (int i = 0; i < selection.Length; i++) { selection[i] = 0; listPosition[i] = 1; }
                SelectCategory(0);
                storyScroll.verticalNormalizedPosition = 1;
            }
            if (!archive && archiveWasOpen) modelPreview.SetVisible(false);
            if (help && !helpWasOpen) SelectHelp(0);
            archiveWasOpen = archive; helpWasOpen = help;
            if (settings && audioSettings != null)
            {
                musicPercent.text = Mathf.RoundToInt(audioSettings.MusicVolume * 100) + "%";
                effectsPercent.text = Mathf.RoundToInt(audioSettings.EffectsVolume * 100) + "%";
            }
            if (activePage == nextPage) return;
            activePage = nextPage;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }

        private void Update()
        {
            if (activePage == null || !Application.isFocused || Keyboard.current == null || EventSystem.current == null) return;
            var keyboard = Keyboard.current;
            if (activePage == archivePage)
            {
                archiveCategoryHint.text = "";
                for (int i = 0; i < archiveCategories.Length; i++)
                {
                    var button = archiveCategories[i];
                    bool hovered = Mouse.current != null && RectTransformUtility.RectangleContainsScreenPoint((RectTransform)button.transform, Mouse.current.position.ReadValue());
                    if (hovered || EventSystem.current.currentSelectedGameObject == button.gameObject) archiveCategoryHint.text = archiveCategoryLabels[i].text;
                }
            }
            if (keyboard.tabKey.wasPressedThisFrame)
            {
                visibleControls.Clear();
                foreach (var control in controls)
                    if (control != null && control.transform.IsChildOf(activePage.transform) && control.IsActive() && control.IsInteractable()) visibleControls.Add(control);
                if (visibleControls.Count > 0)
                {
                    int current = visibleControls.FindIndex(control => control.gameObject == EventSystem.current.currentSelectedGameObject);
                    int direction = keyboard.shiftKey.isPressed ? -1 : 1;
                    int index = current < 0 ? (direction > 0 ? 0 : visibleControls.Count - 1) : (current + direction + visibleControls.Count) % visibleControls.Count;
                    EventSystem.current.SetSelectedGameObject(visibleControls[index].gameObject);
                    EnsureEntryVisible(visibleControls[index]);
                }
            }
            // InputSystem UI module already owns Enter / gamepad submit; Space is reserved for this page.
            if (keyboard.spaceKey.wasPressedThisFrame)
            {
                var selected = EventSystem.current.currentSelectedGameObject;
                if (selected != null && selected.transform.IsChildOf(activePage.transform))
                    ExecuteEvents.Execute(selected, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
            }
            if (activePage == null) return;
            foreach (var control in controls)
            {
                if (control == null || !control.transform.IsChildOf(activePage.transform)) continue;
                var outline = control.targetGraphic != null ? control.targetGraphic.GetComponent<Outline>() : null;
                if (outline != null) outline.enabled = EventSystem.current.currentSelectedGameObject == control.gameObject;
            }
        }

        private void SelectCategory(int value)
        {
            listPosition[category] = entryScroll.verticalNormalizedPosition;
            category = value;
            float x = 0;
            for (int i = 0; i < archiveCategories.Length; i++)
            {
                bool selected = i == category;
                var rect = (RectTransform)archiveCategories[i].transform;
                rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, selected ? 336 : 100);
                rect.anchoredPosition = new Vector2(x, rect.anchoredPosition.y); x += selected ? 336 : 100;
                archiveCategoryLabels[i].gameObject.SetActive(selected); archiveCategoryPlates[i].enabled = selected;
                archiveCategoryIcons[i].sprite = selected ? categorySelectedIcons[i] : categoryNormalIcons[i];
            }
            storyPanel.SetActive(category == 0); storyEntryPanel.SetActive(category == 0);
            objectPanel.SetActive(category != 0); entryListPanel.SetActive(category != 0);
            int count = category == 0 ? 0 : category == 1 ? Buildings.Length : Enemies.Length;
            for (int i = 0; i < archiveEntries.Length; i++)
            {
                archiveEntries[i].gameObject.SetActive(i < count);
                if (i < count) archiveEntryLabels[i].text = category == 1 ? session.FindDefinition(Buildings[i]).DisplayName : baseBattle.GetEnemy(Enemies[i]).DisplayName;
            }
            entryListContent.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, count * 128);
            SelectEntry(selection[category]);
            Canvas.ForceUpdateCanvases(); entryScroll.verticalNormalizedPosition = listPosition[category];
        }

        private void SelectEntry(int index)
        {
            if (category == 0) { modelPreview.SetVisible(false); return; }
            selection[category] = Mathf.Clamp(index, 0, category == 1 ? Buildings.Length - 1 : Enemies.Length - 1);
            index = selection[category];
            for (int i = 0; i < archiveEntries.Length; i++) archiveEntryImages[i].sprite = i == index ? entrySelected : entryNormal;
            if (category == 1)
            {
                var kind = Buildings[index]; var definition = session.FindDefinition(kind).CreateDefinition();
                archiveTitle.text = definition.DisplayName; archiveDescription.text = DefenseInterface.BuildingDescription(kind);
                var value = new StringBuilder("最大耐久  ").Append(definition.MaximumHealth)
                    .Append("\n占地  ").Append(definition.Width).Append(" × ").Append(definition.Depth);
                if (definition.Resonance == null || definition.Resonance.RequiresEnergy) value.Append("\n储能容量  ").Append(Energy(definition.EnergyCapacity));
                if (definition.Resonance != null) value.Append("\n").Append(DefenseInterface.ResonanceRules(definition.Resonance));
                if (kind == BuildingKind.Core) value.Append("\n核心生命不可维修\n实际到货补充护盾");
                else value.Append("\n购买资金  ").Append(definition.PurchaseCost).Append("\n出售返还  ").Append(definition.SaleRefund).Append("\n维修基价  ").Append(definition.RepairCost);
                if (definition.ProductionPerTick > 0) value.Append("\n产能 / 秒  ").Append(Energy(definition.ProductionPerTick * session.World.Settings.TicksPerSecond));
                if (definition.TransmissionRange > 0)
                {
                    value.Append("\n传输范围  ").Append(definition.TransmissionRange.ToString("0.##")).Append(" 格")
                        .Append("\n每批输出上限  ").Append(Energy(definition.OutputAmount))
                        .Append("\n输出间隔  ").Append((definition.OutputIntervalTicks / (float)session.World.Settings.TicksPerSecond).ToString("0.##")).Append(" 秒");
                }
                if (kind == BuildingKind.Battery) value.Append("\n每秒充电上限  ").Append(Energy(definition.MaximumChargePerTick * session.World.Settings.TicksPerSecond));
                if (kind == BuildingKind.Turret || kind == BuildingKind.BurstTurret || kind == BuildingKind.AreaTurret || kind == BuildingKind.PiercingRailgun)
                {
                    var weapon = baseBattle.GetTurret(kind);
                    value.Append("\n伤害  ").Append(weapon.Damage).Append("\n射程  ").Append(weapon.Range.ToString("0.##")).Append(" 格")
                        .Append("\n最短射击周期  ").Append(((weapon.CooldownTicks + weapon.ChargeTicks) / (float)session.World.Settings.TicksPerSecond).ToString("0.##")).Append(" 秒")
                        .Append("\n每次耗能  ").Append(Energy(weapon.EnergyPerShot));
                    if (weapon.SplashRadius > 0) value.Append("\n爆炸半径  ").Append(weapon.SplashRadius.ToString("0.##")).Append(" 格");
                    if (kind == BuildingKind.PiercingRailgun) value.Append("\n最小射程  ").Append(weapon.MinimumRange.ToString("0.##")).Append(" 格")
                        .Append("\n水平射界  左右各 ").Append(weapon.FireArcHalfDegrees.ToString("0.#")).Append("°")
                        .Append("\n蓄力时间  ").Append((weapon.ChargeTicks / (float)session.World.Settings.TicksPerSecond).ToString("0.##")).Append(" 秒")
                        .Append("\n贯穿上限  ").Append(weapon.MaximumPierceTargets).Append(" 名")
                        .Append("\n每次贯穿保留伤害  ").Append((weapon.PierceDamageMultiplier * 100).ToString("0")).Append("%");
                }
                archiveAttributes.text = value.ToString();
                PresentBuildingSummary(kind, definition);
                archiveImage.sprite = Picture(buildingPictures, (int)kind); modelPreview.Show(kind);
            }
            else
            {
                var kind = Enemies[index]; var definition = baseBattle.GetEnemy(kind);
                archiveTitle.text = definition.DisplayName; archiveDescription.text = definition.Description;
                archiveAttributes.text = "基础生命  " + definition.BaseHealth + "\n攻击方式  " + definition.AttackDescription + "\n伤害  " + definition.AttackDamage
                    + "\n攻击间隔  " + (definition.AttackIntervalTicks / (float)session.World.Settings.TicksPerSecond).ToString("0.##") + " 秒\n攻击距离  " + definition.AttackRange.ToString("0.##") + " 格"
                    + "\n移动速度  " + definition.Speed.ToString("0.##") + " 格 / 秒\n击杀奖励  " + definition.KillReward;
                PresentEnemySummary(kind, definition);
                archiveImage.sprite = Picture(enemyPictures, (int)kind); modelPreview.Show(kind);
            }
            var copy = category == 1 ? DefenseInterface.GetArchiveCopy(Buildings[index]) : DefenseInterface.GetArchiveCopy(Enemies[index]);
            if (copy != null)
            {
                archiveDescription.text = copy.Description;
                archiveAttributes.text = copy.Attributes;
                PresentSummary(copy.Subtitle, copy.Attributes, category == 1 ? "能量行为" : "目标偏好",
                    category == 1 ? copy.Behavior : copy.Attributes.Substring(copy.Attributes.IndexOf("目标偏好 ", System.StringComparison.Ordinal) + "目标偏好 ".Length));
            }
            descriptionScroll.verticalNormalizedPosition = 1; attributesScroll.verticalNormalizedPosition = 1;
            SetMode(modelModeSelected);
        }

        private void PresentBuildingSummary(BuildingKind kind, BuildingDefinition definition)
        {
            string role, tactics;
            switch (kind)
            {
                case BuildingKind.Reactor:
                    role = "能源生产 · 防线源头";
                    tactics = "先把反应堆接入防线，再补充炮塔。让火力覆盖能源源头，避免收束者切断供给。";
                    break;
                case BuildingKind.Relay:
                    role = "线路延伸 · 能源转发";
                    tactics = "将相邻供能范围连成通路，覆盖远处炮塔。保护关键中继器，避免一处断路影响整条防线。";
                    break;
                case BuildingKind.Battery:
                    role = "储能缓冲 · 支援缺能工事";
                    tactics = "准备阶段先积蓄能源，战斗时支援可达的缺能建筑。电池不生产能量，仍需连接反应堆。";
                    break;
                case BuildingKind.Core:
                    role = "守卫目标 · 生命不可恢复";
                    tactics = "持续供能才能补充护盾。核心不发电，也不能移动、出售或维修生命；不要等敌人贴近后再补防线。";
                    break;
                case BuildingKind.BurstTurret:
                    role = "集中火力 · 对付高耐久目标";
                    tactics = "用来拦截镇载者等高耐久敌人。提前补足储能，并搭配持续射击的炮塔处理快速漏网目标。";
                    break;
                case BuildingKind.AreaTurret:
                    role = "范围爆炸 · 压制密集敌群";
                    tactics = "覆盖敌人容易聚集的通路，让爆炸同时打击多个目标。稳定供能比单纯增加数量更重要。";
                    break;
                case BuildingKind.PiercingRailgun:
                    role = "直线贯穿 · 远程重火力";
                    tactics = "沿通路布置，按 R 键旋转并微调朝向。近处敌人交给其他炮塔拦截。";
                    break;
                case BuildingKind.ResonanceMagneticTower:
                    role = "友军共振 · 范围减速";
                    tactics = "放在持续开火的友军炮塔附近，积累共振来修复和释放磁暴。无需接入电网，孤立放置无法持续运作。";
                    break;
                default:
                    role = "持续射击 · 基础拦截";
                    tactics = "适合建立第一道火力覆盖。让敌人经过射程内的开敞通路；缺能时优先检查供电线路与实际到货。";
                    break;
            }
            string facts = "占地 " + definition.Width + " × " + definition.Depth + " 格    耐久 " + definition.MaximumHealth;
            if (kind == BuildingKind.Core) facts += "\n护盾依靠实际能源到货补充";
            else if (definition.Resonance != null) facts += "\n建造资金 " + definition.PurchaseCost + "    无需供电";
            else if (kind == BuildingKind.Reactor) facts += "\n产能 " + Energy(definition.ProductionPerTick * session.World.Settings.TicksPerSecond) + " / 秒    建造资金 " + definition.PurchaseCost;
            else if (kind == BuildingKind.Relay || kind == BuildingKind.Battery) facts += "\n传输范围 " + definition.TransmissionRange.ToString("0.##") + " 格    储能 " + Energy(definition.EnergyCapacity);
            else
            {
                var weapon = baseBattle.GetTurret(kind);
                facts += "\n伤害 " + weapon.Damage + "    射程 " + weapon.Range.ToString("0.##") + " 格    耗能 " + Energy(weapon.EnergyPerShot);
            }
            PresentSummary(role, facts, "部署要点", tactics);
        }

        private void PresentEnemySummary(EnemyKind kind, EnemyDefinition definition)
        {
            string role, tactics;
            switch (kind)
            {
                case EnemyKind.Fast:
                    role = "快速接近 · 消耗防线储能";
                    tactics = "提前建立连续的火力覆盖，用范围攻击处理密集敌群。留意空档和断电，避免它们穿过防线。";
                    break;
                case EnemyKind.Heavy:
                    role = "高耐久推进 · 清除阻挡";
                    tactics = "集中火力尽早削减生命，保持沿路供能。不要只靠一座挡路建筑拖延它。";
                    break;
                case EnemyKind.Demolisher:
                    role = "偏好中继器 · 威胁供电线路";
                    tactics = "让炮塔覆盖关键中继器，避免断路。它会比较拆除收益与进攻代价，目标并非始终固定。";
                    break;
                case EnemyKind.Converger:
                    role = "偏好反应堆 · 威胁能源源头";
                    tactics = "让反应堆处于防线保护之内，预先储能应对断供。它会比较进攻代价，也可能转攻其他工事。";
                    break;
                default:
                    role = "常规推进 · 评估工事价值";
                    tactics = "建立稳定供电与交叉火力。点击场上敌人查看当前目标，再判断需要保护哪座工事。";
                    break;
            }
            string facts = "基础生命 " + definition.BaseHealth + "    速度 " + definition.Speed.ToString("0.##") + " 格 / 秒\n近战伤害 " + definition.AttackDamage + "    击杀奖励 " + definition.KillReward;
            PresentSummary(role, facts, "应对要点", tactics);
        }

        private void PresentSummary(string role, string facts, string tacticsTitle, string tactics)
        {
            if (archiveRole != null) archiveRole.text = role;
            if (archiveKeyFacts != null) archiveKeyFacts.text = facts;
            if (archiveTacticsTitle != null) archiveTacticsTitle.text = tacticsTitle;
            if (archiveTactics != null) archiveTactics.text = tactics;
        }

        private void SetMode(bool model)
        {
            modelModeSelected = model;
            imageDisplay.SetActive(!model); modelDisplay.SetActive(model);
            bool missing = model ? !modelPreview.HasModel : archiveImage.sprite == null;
            imagePlaceholder.SetActive(missing); imagePlaceholderLabel.text = model ? "模型暂不可用" : "图片待补充";
            archiveImage.enabled = archiveImage.sprite != null;
            resetModel.gameObject.SetActive(model); resetModel.interactable = modelPreview.HasModel;
            previewModePlates[0].color = model ? Ink : Cyan; previewModePlates[1].color = model ? Cyan : Ink;
            previewModePlates[0].GetComponentInChildren<Text>().color = model ? new Color(.83f, .93f, .98f) : Ink;
            previewModePlates[1].GetComponentInChildren<Text>().color = model ? Ink : new Color(.83f, .93f, .98f);
            modelPreview.SetVisible(model && category != 0 && archivePage.activeInHierarchy);
        }

        private void SelectHelp(int index)
        {
            helpTitle.text = HelpNames[index]; helpBody.text = HelpContent[index];
            if (index == 1)
            {
                string waves = session.ActiveCampaignLevel != null
                    ? "当前关卡共 " + session.ActiveCampaignLevel.WaveCount + " 波。"
                    : session.CampaignCatalog != null ? "每关的波次数量与入口信息可在选关页查看。" : "正式关卡共 " + baseBattle.Waves.Count + " 波。";
                helpBody.text = waves + "开局只有一座核心。核心不产生能量，需要自行建造反应堆和防御建筑。\n\n" + helpBody.text;
            }
            if (index == 4) helpBody.text = "战场情报卡可展开查看本波兵种与数量，点击入口行可筛选该路敌人。地图入口显示开放状态，提前两波预告新入口。出生区域须保持净空。\n\n" + helpBody.text;
            for (int i = 0; i < helpTopicImages.Length; i++) helpTopicImages[i].color = i == index ? Color.white : new Color(.65f, .72f, .78f);
            helpScroll.verticalNormalizedPosition = 1;
        }
        private void EnsureEntryVisible(Selectable control)
        {
            if (!control.transform.IsChildOf(entryListContent)) return;
            Canvas.ForceUpdateCanvases();
            var rect = (RectTransform)control.transform;
            float top = -rect.anchoredPosition.y;
            float current = entryListContent.anchoredPosition.y;
            float height = entryScroll.viewport.rect.height;
            float target = top < current ? top : top + rect.rect.height > current + height ? top + rect.rect.height - height : current;
            float maximum = Mathf.Max(0, entryListContent.rect.height - height);
            entryListContent.anchoredPosition = new Vector2(entryListContent.anchoredPosition.x, Mathf.Clamp(target, 0, maximum));
        }
        private static Sprite Picture(Sprite[] pictures, int index) => pictures != null && index < pictures.Length ? pictures[index] : null;
        private static string Energy(long amount) => (amount / 1000d).ToString("0.##");
    }
}
