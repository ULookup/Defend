using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Defend.Simulation;
using UnityEngine;
using UnityEngine.UI;

namespace Defend.Unity
{
    /// <summary>预制沙盒控件的绑定层。草稿只在明确应用时提交给模拟事务。</summary>
    public sealed class SandboxControlPanel : MonoBehaviour
    {
        [SerializeField] private GameObject window;
        [SerializeField] private GameObject[] pages;
        [SerializeField] private InterfacePanelMotion windowMotion, confirmationMotion;
        [SerializeField] private InterfacePanelMotion[] pageMotions;
        [SerializeField] private CanvasGroup windowContentGroup;
        [SerializeField] private Button open, close, startSandbox, resultTools;
        [SerializeField] private Button[] tabs, groupRows, spawnRows, buildingRows, presetRows;
        [SerializeField] private Text[] groupLabels, spawnLabels, buildingLabels, parameterLabels, defaultValues, currentValues, allocationLabels, presetLabels;
        [SerializeField] private InputField[] enemyFields, parameterFields, allocationFields;
        [SerializeField] private InputField livingLimit, presetName;
        [SerializeField] private Text status, groupPageLabel, spawnPageLabel, presetPageLabel, enemyTypeLabel, previewText, waveStatus, spawnWarning;
        [SerializeField] private Button groupPrevious, groupNext, addGroup, copyGroup, deleteGroup, enemyType, divideCount, applyEnemies, discardEnemies;
        [SerializeField] private Button spawnPrevious, spawnNext, addSpawn, moveSpawn, deleteSpawn, locateSpawn;
        [SerializeField] private Button previewParameters, applyParameters, discardParameters, defaultParameters;
        [SerializeField] private Button endWave, restore, save, load, reloadFiles, presetPrevious, presetNext, restart, home;
        [SerializeField] private GameObject confirmation;
        [SerializeField] private Text confirmationText;
        [SerializeField] private Button confirm, cancel;
        [SerializeField] private WorldCameraViewport cameraViewport;
        [SerializeField] private GameObject spawnMarkerPrefab;
        private readonly List<GameObject> markers = new List<GameObject>();
        private readonly Dictionary<Button, Image> selectionMarkers = new Dictionary<Button, Image>();
        private readonly List<SandboxEnemyGroup> groups = new List<SandboxEnemyGroup>();
        private readonly List<SandboxSpawnAllocation> allocations = new List<SandboxSpawnAllocation>();
        private readonly Dictionary<long, int> allocationDraft = new Dictionary<long, int>();
        private GameSessionRoot session;
        private Action confirmationAction;
        private bool opened, mapEditing, previousPaused, filling, enemyDirty, buildingDirty;
        private int page, groupPage, spawnPage, presetPage;
        private long selectedGroup, selectedSpawn;
        private long nextGroupId = 1, previewRevision = -1;
        private EnemyKind selectedEnemyKind;
        private BuildingKind selectedBuildingKind = BuildingKind.Reactor;
        private SandboxBuildingParameters buildingDraft;
        private string[] presets = Array.Empty<string>();
        private static readonly EnemyKind[] EnemyKinds = { EnemyKind.Normal, EnemyKind.Demolisher, EnemyKind.Converger, EnemyKind.Fast, EnemyKind.Heavy };
        private static readonly BuildingKind[] BuildingKinds = { BuildingKind.Reactor, BuildingKind.Relay, BuildingKind.Battery, BuildingKind.Turret, BuildingKind.BurstTurret, BuildingKind.AreaTurret, BuildingKind.Core, BuildingKind.PiercingRailgun, BuildingKind.ResonanceMagneticTower };
        private static readonly string[] ParameterNames = { "最大生命值", "储能容量", "每秒产能", "单次输出量", "输出间隔（秒）", "传输范围（格）", "每秒最大充电量", "开火冷却（秒）", "每次射击耗能", "攻击范围（格）" };
        private static readonly string[] ResonanceParameterNames = { "最大生命值", "共振接收半径", "修复共振 / 恢复生命", "磁暴所需共振", "磁暴伤害 / 半径", "减速幅度 / 时间" };
        public bool HasUnappliedChanges => enemyDirty || buildingDirty;
        public bool BlocksWorldInput => (opened && !mapEditing) || (windowMotion != null && windowMotion.IsExiting);
        public bool IsOpen => opened;
        private bool Editable => session != null && session.IsSandbox && session.World.CanEditBuildings && !session.AtStartPage;
        public void Initialize(GameSessionRoot root, WorldCameraViewport viewport)
        {
            if (session != null) return;
            session = root; cameraViewport = viewport;
            foreach (var rows in new[] { groupRows, spawnRows, buildingRows, presetRows })
                foreach (var button in rows)
                {
                    var marker = button.targetGraphic.transform.Find("选中标记");
                    selectionMarkers[button] = marker != null ? marker.GetComponent<Image>() : null;
                }
            presetName.onValueChanged.AddListener(_ => RefreshPresetSelection());
            startSandbox.onClick.AddListener(session.StartSandbox);
            open.onClick.AddListener(Open); resultTools.onClick.AddListener(() => { Open(); SelectPage(2); });
            close.onClick.AddListener(Close);
            for (int i = 0; i < tabs.Length; i++) { int index = i; tabs[i].onClick.AddListener(() => SelectPage(index)); }
            for (int i = 0; i < groupRows.Length; i++) { int row = i; groupRows[i].onClick.AddListener(() => SelectGroupAt(groupPage * groupRows.Length + row)); }
            for (int i = 0; i < spawnRows.Length; i++) { int row = i; spawnRows[i].onClick.AddListener(() => { if (!CaptureAllocations()) return; int n = spawnPage * spawnRows.Length + row; if (n < session.Battle.SandboxSpawnPoints.Count) selectedSpawn = session.Battle.SandboxSpawnPoints[n].Id; RefreshLists(); }); }
            for (int i = 0; i < buildingRows.Length; i++) { int index = i; buildingRows[i].onClick.AddListener(() => SelectBuilding(BuildingKinds[index])); }
            for (int i = 0; i < presetRows.Length; i++) { int row = i; presetRows[i].onClick.AddListener(() => { int n = presetPage * presetRows.Length + row; if (n < presets.Length) { presetName.SetTextWithoutNotify(presets[n]); RefreshPresetSelection(); } }); }
            groupPrevious.onClick.AddListener(() => TurnGroups(-1)); groupNext.onClick.AddListener(() => TurnGroups(1));
            spawnPrevious.onClick.AddListener(() => TurnSpawns(-1)); spawnNext.onClick.AddListener(() => TurnSpawns(1));
            presetPrevious.onClick.AddListener(() => { presetPage = Math.Max(0, presetPage - 1); RefreshLists(); });
            presetNext.onClick.AddListener(() => { presetPage = Math.Min(Math.Max(0, (presets.Length - 1) / presetRows.Length), presetPage + 1); RefreshLists(); });
            addGroup.onClick.AddListener(() => AddGroup(false)); copyGroup.onClick.AddListener(() => AddGroup(true)); deleteGroup.onClick.AddListener(DeleteGroup);
            enemyType.onClick.AddListener(CycleEnemyType); divideCount.onClick.AddListener(DivideCount);
            applyEnemies.onClick.AddListener(ApplyEnemies); discardEnemies.onClick.AddListener(ReloadEnemies);
            addSpawn.onClick.AddListener(() => BeginSpawnEdit(0)); moveSpawn.onClick.AddListener(() => { if (selectedSpawn != 0) BeginSpawnEdit(selectedSpawn); });
            deleteSpawn.onClick.AddListener(() => Ask("删除出生点后，引用它的队伍必须重新指定。确定删除？", DeleteSpawn));
            locateSpawn.onClick.AddListener(() => { foreach (var point in session.Battle.SandboxSpawnPoints) if (point.Id == selectedSpawn) { cameraViewport.Focus(point.X + .5f, point.Z + .5f); break; } });
            previewParameters.onClick.AddListener(PreviewParameters); applyParameters.onClick.AddListener(ApplyParameters);
            discardParameters.onClick.AddListener(ReloadBuilding); defaultParameters.onClick.AddListener(() => { if (!Editable) return; buildingDraft = session.World.Sandbox.GetBuildingParameters(selectedBuildingKind, true); FillBuilding(); buildingDirty = true; previewRevision = -1; });
            endWave.onClick.AddListener(() => Ask("提前结束将移除本波敌人和弹丸，保留建筑损伤与能源状态。", () => { session.EndSandboxWave(); Message("本波已手动终止。"); }));
            restore.onClick.AddListener(() => Ask("恢复开战前的建筑、库存、运输和计时，回到暂停准备状态？", () => { session.RestoreSandboxCheckpoint(); if (opened) Message(session.Feedback); }));
            save.onClick.AddListener(() => { if (!RequireApplied()) return; session.SaveSandboxPreset(presetName.text); RefreshPresets(); Message(session.Feedback); });
            load.onClick.AddListener(() => { if (!RequireApplied()) return; Ask("载入方案会替换当前布局：建筑满生命、储能和在途能量为空，回到暂停准备。", () => { session.LoadSandboxPreset(presetName.text); if (opened) Message(session.Feedback); }); });
            reloadFiles.onClick.AddListener(RefreshPresets);
            restart.onClick.AddListener(() => Ask("重新开始沙盒将丢失当前进度与开战前记录。", session.Restart));
            home.onClick.AddListener(() => Ask("返回开始页将丢失当前对局进度。", session.ReturnToStart));
            cancel.onClick.AddListener(DismissConfirmation);
            confirm.onClick.AddListener(() => { var action = confirmationAction; DismissConfirmation(); action?.Invoke(); });
            foreach (var field in enemyFields) field.onValueChanged.AddListener(_ => { if (!filling) enemyDirty = true; });
            foreach (var field in parameterFields) field.onValueChanged.AddListener(_ => { if (!filling) { buildingDirty = true; previewRevision = -1; } });
            livingLimit.onValueChanged.AddListener(_ => { if (!filling) enemyDirty = true; });
            for (int i = 0; i < allocationFields.Length; i++)
            {
                int row = i;
                allocationFields[i].onValueChanged.AddListener(value =>
                {
                    if (filling || SelectedGroup == null) return;
                    enemyDirty = true;
                    int index = spawnPage * spawnRows.Length + row;
                    if (index >= session.Battle.SandboxSpawnPoints.Count || !int.TryParse(value, out int count) || count < 0 || count > BattleSystem.SandboxMaximumEnemyCount) return;
                    allocationDraft[session.Battle.SandboxSpawnPoints[index].Id] = count;
                    int total = 0; foreach (var pair in allocationDraft) total += pair.Value;
                    enemyFields[0].SetTextWithoutNotify(total.ToString());
                });
            }
            SetPanel(window, windowMotion, false, true); SetPanel(confirmation, confirmationMotion, false, true);
        }
        public void Open()
        {
            if (session == null || !session.IsSandbox || session.AtStartPage || opened) return;
            opened = true; previousPaused = session.Paused; session.GridInput.Cancel(); session.SetPaused(true);
            ReloadEnemies(); ReloadBuilding(); RefreshPresets(); SetPanel(window, windowMotion, true); SelectPage(0); Message(Editable ? "编辑期间已暂停。应用后关闭面板可恢复原暂停状态。" : "当前阶段仅可查看；战斗中可提前结束本波。");
        }
        public void Close()
        {
            if (confirmationMotion != null ? confirmationMotion.TargetVisible : confirmation.activeSelf) { DismissConfirmation(); return; }
            if (mapEditing) { session.GridInput.Cancel(); return; }
            if (HasUnappliedChanges) { Message("仍有未应用修改，请先应用或放弃修改。"); return; }
            bool paused = previousPaused; ResetPanel(false); session.SetPaused(paused);
        }
        public void CloseForSessionChange()
        {
            ResetPanel(true);
        }
        private void ResetPanel(bool immediate)
        {
            opened = mapEditing = enemyDirty = buildingDirty = false; previewRevision = -1;
            SetPanel(window, windowMotion, false, immediate); SetPanel(confirmation, confirmationMotion, false, true); confirmationAction = null;
            if (windowContentGroup != null) windowContentGroup.interactable = true;
            foreach (var marker in markers) if (marker != null) marker.SetActive(false);
        }
        private bool RequireApplied() { if (!HasUnappliedChanges) return true; Message("请先应用或放弃敌人和建筑参数的未应用修改。"); return false; }
        public void ReturnFromMapEdit() { if (!opened) return; mapEditing = false; SetPanel(window, windowMotion, true); RefreshLists(); }
        private void BeginSpawnEdit(long id)
        {
            if (!Editable) return;
            if (!CaptureSelectedGroup()) return;
            mapEditing = true; SetPanel(window, windowMotion, false); session.GridInput.BeginSandboxSpawn(id, ReturnFromMapEdit);
        }
        private void DeleteSpawn()
        {
            if (!Editable || !CaptureSelectedGroup()) return;
            if (session.Battle.DeleteSandboxSpawnPoint(selectedSpawn, out string error)) { Message("出生点已删除；原引用为未指定出生点。请对引用队伍使用均分数量重新分配。"); selectedSpawn = 0; RefreshLists(); } else Message(error);
        }
        private void SelectPage(int index)
        {
            if (index < 0 || index >= pages.Length || (opened && page == 0 && !CaptureSelectedGroup())) return;
            int previous = page; page = index;
            for (int i = 0; i < pages.Length; i++)
                SetPanel(pages[i], pageMotions != null && i < pageMotions.Length ? pageMotions[i] : null, i == page, true);
            RefreshLists();
            if (previous != page && pageMotions != null && page < pageMotions.Length)
                pageMotions[page]?.ReplayContent(new Vector2(page > previous ? 10 : -10, 0));
        }
        private static void SetPanel(GameObject panel, InterfacePanelMotion motion, bool visible, bool immediate = false)
        {
            if (motion != null) motion.SetVisible(visible, immediate);
            else panel.SetActive(visible);
        }
        private void DismissConfirmation()
        {
            confirmationAction = null;
            SetPanel(confirmation, confirmationMotion, false);
        }
        private void Message(string message) { status.text = message; }
        private void Ask(string message, Action action)
        {
            confirmationText.text = message; confirmationAction = action;
            if (windowContentGroup != null) windowContentGroup.interactable = false;
            SetPanel(confirmation, confirmationMotion, true);
        }
        private SandboxEnemyGroup SelectedGroup { get { foreach (var group in groups) if (group.Id == selectedGroup) return group; return null; } }
        private void ReloadEnemies()
        {
            groups.Clear(); groups.AddRange(session.Battle.SandboxGroups); nextGroupId = 1;
            foreach (var group in groups) nextGroupId = Math.Max(nextGroupId, group.Id + 1);
            if (SelectedGroup == null) selectedGroup = groups.Count == 0 ? 0 : groups[0].Id;
            livingLimit.SetTextWithoutNotify(session.Battle.SandboxMaximumLivingEnemies.ToString()); enemyDirty = false; FillGroup(); RefreshLists();
        }
        private void SelectGroupAt(int index) { if (index < 0 || index >= groups.Count || !CaptureSelectedGroup()) return; selectedGroup = groups[index].Id; FillGroup(); RefreshLists(); }
        private void TurnGroups(int direction) { if (!CaptureSelectedGroup()) return; groupPage = Mathf.Clamp(groupPage + direction, 0, Math.Max(0, (groups.Count - 1) / groupRows.Length)); RefreshLists(); }
        private void TurnSpawns(int direction) { if (!CaptureAllocations()) return; spawnPage = Mathf.Clamp(spawnPage + direction, 0, Math.Max(0, (session.Battle.SandboxSpawnPoints.Count - 1) / spawnRows.Length)); RefreshLists(); }
        private void FillGroup()
        {
            filling = true; allocationDraft.Clear(); var group = SelectedGroup;
            if (group != null) { selectedEnemyKind = group.Kind; foreach (var allocation in group.Allocations) allocationDraft[allocation.SpawnPointId] = allocation.Count;
                double ticks = session.World.Settings.TicksPerSecond;
                double[] values = { group.Count, group.Health, group.Speed, group.Damage, group.AttackIntervalTicks / ticks, group.StartTick / ticks, group.SpawnIntervalTicks / ticks };
                for (int i = 0; i < enemyFields.Length; i++) enemyFields[i].SetTextWithoutNotify(Number(values[i]));
            } else foreach (var field in enemyFields) field.SetTextWithoutNotify("");
            enemyTypeLabel.text = group == null ? "请选择队伍" : session.Battle.Settings.GetEnemy(selectedEnemyKind).DisplayName + "（点击切换）";
            filling = false; RefreshLists();
        }
        private void AddGroup(bool copy)
        {
            if (!Editable || !CaptureSelectedGroup()) return; SandboxEnemyGroup group;
            var existing = SelectedGroup;
            if (copy && existing != null) group = new SandboxEnemyGroup(nextGroupId++, existing.Kind, existing.Health, existing.Speed, existing.Damage, existing.AttackIntervalTicks, existing.StartTick, existing.SpawnIntervalTicks, existing.Allocations);
            else { var definition = session.Battle.Settings.GetEnemy(EnemyKind.Normal); var list = new List<SandboxSpawnAllocation>(); if (session.Battle.SandboxSpawnPoints.Count > 0) list.Add(new SandboxSpawnAllocation(session.Battle.SandboxSpawnPoints[0].Id, 1));
                group = new SandboxEnemyGroup(nextGroupId++, definition.Kind, definition.BaseHealth, definition.Speed, definition.AttackDamage, definition.AttackIntervalTicks, 0, session.World.Settings.TicksPerSecond, list); }
            if (groups.Count >= BattleSystem.SandboxMaximumGroups) { Message("最多保存 64 支队伍。"); return; }
            groups.Add(group); selectedGroup = group.Id; groupPage = (groups.Count - 1) / groupRows.Length; enemyDirty = true; FillGroup();
        }
        private void DeleteGroup() { if (!Editable) return; groups.RemoveAll(group => group.Id == selectedGroup); selectedGroup = groups.Count == 0 ? 0 : groups[0].Id; enemyDirty = true; FillGroup(); }
        private void CycleEnemyType()
        {
            if (!Editable || SelectedGroup == null) return;
            selectedEnemyKind = EnemyKinds[(Array.IndexOf(EnemyKinds, selectedEnemyKind) + 1) % EnemyKinds.Length]; var definition = session.Battle.Settings.GetEnemy(selectedEnemyKind);
            enemyTypeLabel.text = definition.DisplayName; enemyFields[1].text = definition.BaseHealth.ToString(); enemyFields[2].text = Number(definition.Speed); enemyFields[3].text = definition.AttackDamage.ToString(); enemyFields[4].text = Number(definition.AttackIntervalTicks / (double)session.World.Settings.TicksPerSecond); enemyDirty = true;
        }
        private bool CaptureAllocations()
        {
            if (SelectedGroup == null) return true;
            for (int i = 0; i < allocationFields.Length; i++) { int n = spawnPage * spawnRows.Length + i; if (n >= session.Battle.SandboxSpawnPoints.Count) continue;
                if (!int.TryParse(allocationFields[i].text, out int count) || count < 0 || count > BattleSystem.SandboxMaximumEnemyCount) { Message("出生点分配数量须为 0 至 10000 的整数。"); return false; }
                allocationDraft[session.Battle.SandboxSpawnPoints[n].Id] = count; }
            return true;
        }
        private bool CaptureSelectedGroup()
        {
            var existing = SelectedGroup; if (existing == null) return true;
            if (!CaptureAllocations()) return false;
            try {
                var values = new double[7]; for (int i = 1; i < values.Length; i++) values[i] = Parse(enemyFields[i].text);
                int health = Integer(values[1], "生命值"), damage = Integer(values[3], "攻击伤害");
                double ticks = session.World.Settings.TicksPerSecond;
                if (values[2] <= 0 || values[2] > BattleSystem.SandboxMaximumSpeed || values[4] < 1 / ticks || values[4] > 3600 || values[5] < 0 || values[5] > 3600 || values[6] < 1 / ticks || values[6] > 3600) throw new ArgumentException("速度须大于 0 且不超过 20；间隔至少一个逻辑步，时间不超过 3600 秒。");
                allocations.Clear(); foreach (var pair in allocationDraft) allocations.Add(new SandboxSpawnAllocation(pair.Key, pair.Value));
                groups[groups.IndexOf(existing)] = new SandboxEnemyGroup(existing.Id, selectedEnemyKind, health, values[2], damage, (int)Math.Ceiling(values[4] * ticks), (long)Math.Ceiling(values[5] * ticks), (int)Math.Ceiling(values[6] * ticks), allocations);
                enemyFields[0].SetTextWithoutNotify(SelectedGroup.Count.ToString()); return true;
            } catch (Exception exception) { Message(exception.Message); return false; }
        }
        private void DivideCount()
        {
            if (!Editable || SelectedGroup == null) return;
            if (!int.TryParse(enemyFields[0].text, out int count) || count <= 0 || count > BattleSystem.SandboxMaximumEnemyCount) { Message("队伍数量须为 1 至 10000 的整数。"); return; }
            int total = session.Battle.SandboxSpawnPoints.Count; if (total == 0) { Message("请先添加出生点。"); return; }
            allocationDraft.Clear(); int n = 0; foreach (var point in session.Battle.SandboxSpawnPoints) { allocationDraft[point.Id] = count / total + (n++ < count % total ? 1 : 0); }
            enemyDirty = true; RefreshLists(); Message("数量已均分至所有出生点；也可逐点填写数量，总数按分配求和。");
        }
        private void ApplyEnemies()
        {
            if (!Editable || !CaptureSelectedGroup()) return;
            if (!int.TryParse(livingLimit.text, out int limit)) { Message("同时在场上限须为整数。"); return; }
            if (session.Battle.ConfigureSandboxWave(groups, limit, out string error)) { enemyDirty = false; FillGroup(); Message("下一波已应用；间隔已向上取整至逻辑步。"); } else Message(error);
        }
        private void SelectBuilding(BuildingKind kind) { if (buildingDirty) { Message("请先应用或放弃当前建筑类型的修改。"); return; } selectedBuildingKind = kind; ReloadBuilding(); }
        private void ReloadBuilding()
        {
            buildingDraft = session.World.Sandbox.GetBuildingParameters(selectedBuildingKind); buildingDirty = false; previewRevision = -1;
            FillBuilding(); RefreshLists();
            if (selectedBuildingKind == BuildingKind.ResonanceMagneticTower)
            {
                var definition = session.FindEffectiveDefinition(selectedBuildingKind);
                var rules = definition.Resonance;
                previewText.text = "可调整最大生命，其余共振规则为固定基准。无需接入电网。\n磁暴预热 " + Number(rules.BurstWindupSeconds) + " 秒；受击后 " + Number(rules.RecentDamageSeconds) + " 秒内，下次磁暴增伤 " + Number((rules.RecentDamageBonusMultiplier - 1) * 100) + "%。\n正式造价 " + definition.PurchaseCost + "，维修基价 " + definition.RepairCost + "；沙盒内免费。";
            }
            else
            {
                previewText.text = "预览后确认应用；扩大容量不会增加能源，生命按原比例调整。";
                if (buildingDraft.EnergyPerShot > buildingDraft.EnergyCapacity) previewText.text += "\n当前储能容量不足，无法开火。";
            }
        }
        private bool IsParameterVisible(int index)
        {
            if (selectedBuildingKind == BuildingKind.ResonanceMagneticTower) return index < ResonanceParameterNames.Length;
            bool turret = selectedBuildingKind == BuildingKind.Turret || selectedBuildingKind == BuildingKind.BurstTurret || selectedBuildingKind == BuildingKind.AreaTurret || selectedBuildingKind == BuildingKind.PiercingRailgun;
            if (index <= 1) return true; if (index == 2) return selectedBuildingKind == BuildingKind.Reactor; if (index >= 7) return turret;
            if (index == 6) return selectedBuildingKind == BuildingKind.Battery;
            return selectedBuildingKind == BuildingKind.Reactor || selectedBuildingKind == BuildingKind.Relay || selectedBuildingKind == BuildingKind.Battery;
        }
        private static double[] Values(SandboxBuildingParameters value) => new[] { (double)value.MaximumHealth, value.EnergyCapacity, value.ProductionPerSecond, value.OutputAmount, value.OutputIntervalSeconds, value.TransmissionRange, value.MaximumChargePerSecond, value.ShotCooldownSeconds, value.EnergyPerShot, value.AttackRange };
        private void FillBuilding()
        {
            if (selectedBuildingKind == BuildingKind.ResonanceMagneticTower) { FillResonanceBuilding(); return; }
            var defaults = Values(session.World.Sandbox.GetBuildingParameters(selectedBuildingKind, true)); var current = Values(session.World.Sandbox.GetBuildingParameters(selectedBuildingKind)); var draft = Values(buildingDraft);
            int row = 0;
            for (int i = 0; i < parameterFields.Length; i++) {
                bool visible = IsParameterVisible(i); parameterFields[i].transform.parent.gameObject.SetActive(visible); if (!visible) continue;
                parameterFields[i].gameObject.SetActive(true);
                ((RectTransform)parameterFields[i].transform.parent).anchoredPosition = new Vector2(250, -145 - row++ * 49);
                parameterLabels[i].text = i == 1 && selectedBuildingKind == BuildingKind.Core ? "护盾容量" : i == 7 && selectedBuildingKind == BuildingKind.PiercingRailgun ? "射后冷却（秒）" : ParameterNames[i];
                defaultValues[i].text = Number(defaults[i]); currentValues[i].text = Number(current[i]); parameterFields[i].SetTextWithoutNotify(Number(draft[i]));
            }
        }
        private void FillResonanceBuilding()
        {
            var baseline = session.FindDefinition(selectedBuildingKind).CreateDefinition();
            var effective = session.FindEffectiveDefinition(selectedBuildingKind);
            var defaults = ResonanceValues(baseline.MaximumHealth, baseline.Resonance);
            var current = ResonanceValues(effective.MaximumHealth, effective.Resonance);
            for (int i = 0; i < parameterFields.Length; i++)
            {
                bool visible = i < ResonanceParameterNames.Length;
                parameterFields[i].transform.parent.gameObject.SetActive(visible);
                if (!visible) continue;
                ((RectTransform)parameterFields[i].transform.parent).anchoredPosition = new Vector2(250, -145 - i * 49);
                parameterLabels[i].text = ResonanceParameterNames[i]; defaultValues[i].text = defaults[i]; currentValues[i].text = current[i];
                // 固定规则仅保留默认值和当前值两列，不展示不能生效的输入框。
                parameterFields[i].gameObject.SetActive(i == 0);
                if (i == 0) parameterFields[i].SetTextWithoutNotify(buildingDraft.MaximumHealth.ToString());
            }
        }
        private static string[] ResonanceValues(int health, ResonanceDefinition rules) => new[] { health.ToString(), Number(rules.AbsorptionRange) + " 格", rules.RepairThreshold + " / " + rules.RepairAmount, rules.BurstThreshold.ToString(), rules.BurstDamage + " / " + Number(rules.BurstRadius) + " 格", Number((1 - rules.SlowMultiplier) * 100) + "% / " + Number(rules.SlowSeconds) + " 秒" };
        private bool CaptureBuilding()
        {
            try { var v = Values(buildingDraft); for (int i = 0; i < v.Length; i++) if (IsParameterVisible(i) && (selectedBuildingKind != BuildingKind.ResonanceMagneticTower || i == 0)) v[i] = Parse(parameterFields[i].text);
                buildingDraft.MaximumHealth = Integer(v[0], "生命值"); buildingDraft.EnergyCapacity = v[1]; buildingDraft.ProductionPerSecond = v[2]; buildingDraft.OutputAmount = v[3]; buildingDraft.OutputIntervalSeconds = v[4]; buildingDraft.TransmissionRange = v[5]; buildingDraft.MaximumChargePerSecond = v[6]; buildingDraft.ShotCooldownSeconds = v[7]; buildingDraft.EnergyPerShot = v[8]; buildingDraft.AttackRange = v[9]; return true;
            } catch (Exception exception) { Message(exception.Message); return false; }
        }
        private void PreviewParameters()
        {
            if (!Editable || !CaptureBuilding()) return;
            if (!session.World.Sandbox.PreviewBuildingParameters(buildingDraft, out var preview, out string error)) { Message(error); previewRevision = -1; return; }
            previewRevision = session.World.Revision;
            var original = session.FindEffectiveDefinition(selectedBuildingKind); var weapon = selectedBuildingKind == BuildingKind.Turret || selectedBuildingKind == BuildingKind.BurstTurret || selectedBuildingKind == BuildingKind.AreaTurret || selectedBuildingKind == BuildingKind.PiercingRailgun ? session.World.GetTurretDefinition(selectedBuildingKind) : null;
            if (!buildingDraft.TryCreateDefinitions(original, weapon, session.World.Settings.TicksPerSecond, out var effectiveBuilding, out var effectiveWeapon, out error)) { Message(error); previewRevision = -1; return; }
            var effective = SandboxBuildingParameters.FromDefinitions(effectiveBuilding, effectiveWeapon, session.World.Settings.TicksPerSecond);
            if (selectedBuildingKind == BuildingKind.ResonanceMagneticTower)
            {
                previewText.text = "影响 " + preview.AffectedCount + " 座共振磁暴塔；最大生命改为 " + effective.MaximumHealth + "。\n现有生命按原比例调整，共振进度保留。共振规则与正式费用不变；沙盒内免费。";
                Message("预览已更新，请确认后应用。");
                return;
            }
            previewText.text = "影响 " + preview.AffectedCount + " 座建筑；库存移除 " + Number(preview.StoredRemoved / 1000d) + "；在途移除 " + Number(preview.InFlightRemoved / 1000d) + "。\n生命保持比例；新容量 " + Number(effective.EnergyCapacity) + "，不会补充能源。";
            if (weapon != null)
            {
                double cycle = effective.ShotCooldownSeconds + effectiveWeapon.ChargeTicks / (double)session.World.Settings.TicksPerSecond;
                previewText.text += "\n最短射击周期 " + Number(cycle) + " 秒；理论连续需求 " + Number(effective.EnergyPerShot / cycle) + " / 秒；射程 " + Number(effective.AttackRange) + (effective.EnergyPerShot > effective.EnergyCapacity ? "\n储能容量不足，无法开火。" : "");
                if (selectedBuildingKind == BuildingKind.PiercingRailgun) previewText.text += "\n包含固定蓄力 " + Number(effectiveWeapon.ChargeTicks / (double)session.World.Settings.TicksPerSecond) + " 秒；最小射程 " + Number(effectiveWeapon.MinimumRange) + " 格。";
            }
            else if (selectedBuildingKind != BuildingKind.Core) previewText.text += "\n实际输出间隔 " + Number(effective.OutputIntervalSeconds) + " 秒；理论输出 " + Number(effective.OutputAmount / effective.OutputIntervalSeconds) + " / 秒；传输范围 " + Number(effective.TransmissionRange) + " 格。";
            if (selectedBuildingKind == BuildingKind.Reactor) previewText.text += "\n实际产能 " + Number(effective.ProductionPerSecond) + " / 秒；单次输出 " + Number(effective.OutputAmount) + " 点。";
            if (selectedBuildingKind == BuildingKind.Battery) previewText.text += "\n实际最大充电 " + Number(effective.MaximumChargePerSecond) + " / 秒；单次输出 " + Number(effective.OutputAmount) + " 点。";
            Message("预览已更新，请确认后应用。实际供能受库存、线路与损耗限制。");
        }
        private void ApplyParameters()
        {
            if (!Editable || !CaptureBuilding()) return;
            if (previewRevision < 0) { PreviewParameters(); return; }
            if (session.World.Sandbox.ApplyBuildingParameters(buildingDraft, previewRevision, out string error)) { ReloadBuilding(); session.RefreshSandboxState(); Message("该类型现有建筑及以后新建建筑均已更新。"); } else { previewRevision = -1; Message(error); }
        }
        private void RefreshPresets() { presets = session.ListSandboxPresets(); presetPage = 0; RefreshLists(); }
        public void Refresh()
        {
            if (session == null) return; bool sandbox = session.IsSandbox && !session.AtStartPage;
            if (windowContentGroup != null)
                windowContentGroup.interactable = confirmationMotion != null
                    ? !confirmationMotion.TargetVisible && !confirmationMotion.IsExiting
                    : !confirmation.activeSelf;
            open.gameObject.SetActive(sandbox); resultTools.gameObject.SetActive(sandbox && (session.Battle.IsFinished || session.Battle.State == BattleState.WaveCleared));
            if (!sandbox) { if (opened) CloseForSessionChange(); foreach (var marker in markers) marker.SetActive(false); return; }
            RefreshMarkers();
            if (!opened) return;
            if (!Editable) foreach (var field in parameterFields) field.interactable = false;
            waveStatus.text = "已生成 " + session.Battle.SandboxSpawnedCount + "　 在场 " + session.Battle.Enemies.Count + "　 已消灭 " + session.Battle.SandboxKilledCount + "　 待生成 " + session.Battle.RemainingToSpawn + "\n" + session.Battle.SandboxSpawnBlockedReason;
            foreach (var field in enemyFields) field.interactable = Editable && SelectedGroup != null;
            foreach (var field in allocationFields) field.interactable = Editable && SelectedGroup != null;
            foreach (var field in parameterFields) field.interactable = Editable;
            livingLimit.interactable = Editable;
            foreach (var button in new[] { addGroup, copyGroup, deleteGroup, enemyType, divideCount, applyEnemies, discardEnemies, addSpawn, moveSpawn, deleteSpawn, previewParameters, applyParameters, discardParameters, defaultParameters, save, load }) button.interactable = Editable;
            moveSpawn.interactable = deleteSpawn.interactable = Editable && selectedSpawn != 0;
            restore.interactable = session.HasSandboxCheckpoint && session.Battle.State != BattleState.Combat;
            endWave.interactable = session.Battle.State == BattleState.Combat;
        }
        private void RefreshLists()
        {
            if (session == null || !session.IsSandbox) return;
            groupPage = Mathf.Clamp(groupPage, 0, Math.Max(0, (groups.Count - 1) / groupRows.Length));
            spawnPage = Mathf.Clamp(spawnPage, 0, Math.Max(0, (session.Battle.SandboxSpawnPoints.Count - 1) / spawnRows.Length));
            for (int i = 0; i < groupRows.Length; i++) { int n = groupPage * groupRows.Length + i; groupRows[i].gameObject.SetActive(n < groups.Count); if (n < groups.Count) { var group = groups[n]; SetListSelection(groupRows[i], group.Id == selectedGroup); groupLabels[i].text = "队伍 " + (n + 1) + " · " + session.Battle.Settings.GetEnemy(group.Kind).DisplayName + " ×" + group.Count; } }
            groupPageLabel.text = (groupPage + 1) + " / " + Math.Max(1, (groups.Count + groupRows.Length - 1) / groupRows.Length);
            for (int i = 0; i < spawnRows.Length; i++) { int n = spawnPage * spawnRows.Length + i; bool exists = n < session.Battle.SandboxSpawnPoints.Count; spawnRows[i].gameObject.SetActive(exists); allocationFields[i].gameObject.SetActive(exists); allocationLabels[i].gameObject.SetActive(exists); if (exists) { var point = session.Battle.SandboxSpawnPoints[n]; SetListSelection(spawnRows[i], point.Id == selectedSpawn); spawnLabels[i].text = point.Name + "（" + point.X + ", " + point.Z + "）"; allocationLabels[i].text = "分配"; allocationFields[i].SetTextWithoutNotify(allocationDraft.TryGetValue(point.Id, out int count) ? count.ToString() : "0"); } }
            int missing = 0;
            foreach (var pair in allocationDraft) { bool found = false; foreach (var point in session.Battle.SandboxSpawnPoints) if (point.Id == pair.Key) { found = true; break; } if (!found) missing++; }
            spawnWarning.text = missing > 0 ? "未指定出生点：" + missing + " 项引用；使用均分数量重新指定。" : selectedSpawn == 0 ? "请选择出生点查看提示。" : session.Battle.GetSandboxSpawnWarning(selectedSpawn) ?? "";
            spawnPageLabel.text = (spawnPage + 1) + " / " + Math.Max(1, (session.Battle.SandboxSpawnPoints.Count + spawnRows.Length - 1) / spawnRows.Length);
            for (int i = 0; i < buildingLabels.Length; i++) { SetListSelection(buildingRows[i], BuildingKinds[i] == selectedBuildingKind); buildingLabels[i].text = session.FindEffectiveDefinition(BuildingKinds[i]).DisplayName; }
            for (int i = 0; i < presetRows.Length; i++) { int n = presetPage * presetRows.Length + i; presetRows[i].gameObject.SetActive(n < presets.Length); if (n < presets.Length) presetLabels[i].text = presets[n]; }
            presetPageLabel.text = (presetPage + 1) + " / " + Math.Max(1, (presets.Length + presetRows.Length - 1) / presetRows.Length);
            RefreshPresetSelection();
        }
        private void SetListSelection(Button button, bool selected)
        {
            var motion = button.GetComponent<InterfaceButtonMotion>();
            if (motion != null && motion.HasStateSprites)
            {
                motion.SetSelected(selected);
                button.targetGraphic.color = Color.white;
                if (selectionMarkers.TryGetValue(button, out var oldMarker) && oldMarker != null) oldMarker.enabled = false;
                return;
            }
            button.targetGraphic.color = selected ? new Color(.18f, .19f, .20f) : new Color(.10f, .11f, .12f);
            if (selectionMarkers.TryGetValue(button, out var marker) && marker != null) marker.enabled = selected;
        }
        private void RefreshPresetSelection()
        {
            for (int i = 0; i < presetRows.Length; i++)
            {
                int index = presetPage * presetRows.Length + i;
                SetListSelection(presetRows[i], index < presets.Length && string.Equals(presetName.text, presets[index], StringComparison.Ordinal));
            }
        }
        private void RefreshMarkers()
        {
            int count = session.Battle.SandboxSpawnPoints.Count;
            while (markers.Count < count) markers.Add(Instantiate(spawnMarkerPrefab));
            for (int i = 0; i < markers.Count; i++) { bool active = i < count; markers[i].SetActive(active); if (active) { var point = session.Battle.SandboxSpawnPoints[i]; markers[i].transform.position = new Vector3(point.X + .5f, .04f, point.Z + .5f); } }
        }
        private void OnDestroy() { foreach (var marker in markers) if (marker != null) Destroy(marker); }
        private static string Number(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
        private static double Parse(string value) { if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) || double.IsNaN(number) || double.IsInfinity(number)) throw new ArgumentException("请输入有限的有效数字，小数使用小数点。"); return number; }
        private static int Integer(double value, string name) { if (value < 1 || value > 1000000 || value != Math.Floor(value)) throw new ArgumentException(name + "须为 1 至 1000000 的整数。"); return (int)value; }
    }
}

