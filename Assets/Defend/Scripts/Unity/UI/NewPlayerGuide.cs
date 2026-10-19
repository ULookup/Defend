using System;
using Defend.Simulation;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Defend.Unity
{
    /// <summary>读取独立训练场真实状态的新手引导。所有界面预先制作，不改变模拟、建设权限或战役通关记录。</summary>
    public sealed class NewPlayerGuide : MonoBehaviour
    {
        private static readonly string[] ManualTitles = { "守住核心", "观察战场", "部署建筑", "连接供电", "迎击敌人", "整备再战" };
        private static readonly string[] PracticeTitles = { "认识核心", "建造反应堆", "部署防御建筑", "确认实际供电", "开始第一波", "清波后继续准备" };
        private static readonly string[] PracticeInstructions = {
            "点击战场中央的核心，查看生命和护盾。核心不发电，生命无法维修；守住它才能完成关卡。",
            "在底部选择功能建筑中的反应堆，点击合法地面放置。先把它放在核心附近，再按右键取消连续放置。",
            "在底部选择防御建筑，部署闪哨机等炮塔。朝向开敞通路，避开墙体遮挡；预览时可按 R 旋转，右键取消。",
            "让反应堆同时为核心和炮塔供电。距离较远时增设中继器。观察能源覆盖和实际储能，等能量真正到货。",
            "检查入口、射程和供能后，点击底部“开始第 1 波”。战斗中无法建造或维修；可以暂停观察，或切换 1、2、4 倍速度。",
            "守住第一波，结算后点击“继续准备”。普通建筑可维修，核心生命无法维修；补足供电与火力，再迎接下一波。"
        };

        [SerializeField] private GameObject manualRoot, practiceRoot, practiceExpanded, practiceCollapsed;
        [SerializeField] private GameObject[] manualPages;
        [SerializeField] private Button[] chapterButtons;
        [SerializeField] private Image[] chapterIndicators, practiceStepIndicators;
        [SerializeField] private Sprite chapterSelected, chapterNormal, stepCompleted, stepCurrent, stepPending;
        [SerializeField] private Text manualTitle, manualCounter, practiceTitle, practiceInstruction, practiceProgress, practiceStatus;
        [SerializeField] private Text entryLabel, entryHint;
        [SerializeField] private Image practiceProgressFill;
        [SerializeField] private Button openManual, previousPage, nextPage, closeManual, beginPractice, collapsePractice, expandPractice, exitPractice;
        private readonly bool[] completedSteps = new bool[6];
        private readonly BuildingEnergyStatusResolver energyStatus = new BuildingEnergyStatusResolver();
        private GameSessionRoot session;
        private DefenseInterface owner;
        private GameWorld practiceWorld;
        private GameObject previousFocus;
        private UnityEngine.Events.UnityAction[] chapterActions;
        private bool practiceRequested, collapsed, practiceCompleted, restoreManualFocus;
        private int manualPage, lastStep = -1, entryProgressRevision = -1;
        private bool entryTutorialCompleted;
        private float nextObservation;

        public bool IsManualOpen { get; private set; }
        public bool IsPracticeActive { get; private set; }
        public bool HasCompletedPractice => session != null && session.HasCompletedTutorial;
        public bool IsPracticeComplete => practiceCompleted;
        public int ManualPage => manualPage;
        public int PracticeStep => FirstIncompleteStep();
        public GameObject ManualRoot => manualRoot;

        public void Initialize(GameSessionRoot root, DefenseInterface defense)
        {
            if (session != null) return;
            session = root; owner = defense;
            if (manualRoot == null || practiceRoot == null || manualPages == null || manualPages.Length != ManualTitles.Length ||
                chapterButtons == null || chapterButtons.Length != ManualTitles.Length)
                throw new InvalidOperationException("新手引导需要完整的六页内容、章节按钮和实践面板。");
            chapterActions = new UnityEngine.Events.UnityAction[chapterButtons.Length];
            for (int i = 0; i < chapterButtons.Length; i++)
            {
                int page = i;
                chapterActions[i] = () => SelectPage(page);
                chapterButtons[i].onClick.AddListener(chapterActions[i]);
            }
            Add(openManual, OpenManual); Add(previousPage, PreviousPage); Add(nextPage, NextPage);
            Add(closeManual, CloseManual); Add(beginPractice, BeginPractice);
            Add(collapsePractice, CollapsePractice); Add(expandPractice, ExpandPractice); Add(exitPractice, ExitPractice);
            manualRoot.SetActive(false); practiceRoot.SetActive(false);
        }

        public void OpenManual()
        {
            // 训练场会开始独立对局；入口只在选关页开放，避免从局内无提示重开。
            if (session == null || !session.AtStartPage) return;
            previousFocus = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            IsManualOpen = true; manualRoot.SetActive(true); restoreManualFocus = true;
            SelectPage(0, false); session.PlayInterfaceSound(DefenseSoundCue.UiConfirm);
            owner.Refresh();
        }

        public void CloseManual()
        {
            if (!IsManualOpen) return;
            IsManualOpen = false; restoreManualFocus = false; manualRoot.SetActive(false);
            session?.PlayInterfaceSound(DefenseSoundCue.UiBack);
            owner?.Refresh();
            if (EventSystem.current != null && previousFocus != null && previousFocus.activeInHierarchy)
                EventSystem.current.SetSelectedGameObject(previousFocus);
            previousFocus = null;
        }

        public void BeginPractice()
        {
            if (session == null || !session.AtStartPage || session.TutorialLevel == null) return;
            IsManualOpen = false; restoreManualFocus = false; manualRoot.SetActive(false); previousFocus = null;
            practiceRequested = true;
            session.StartTutorial();
        }

        public void OnSessionStarted()
        {
            IsManualOpen = false; manualRoot?.SetActive(false);
            if (!practiceRequested || !session.IsTutorial) { ClearPractice(); return; }
            Array.Clear(completedSteps, 0, completedSteps.Length);
            practiceWorld = session.World; IsPracticeActive = true; practiceCompleted = false; collapsed = false;
            nextObservation = 0; lastStep = -1; energyStatus.Clear();
            Present(true);
        }

        public void CloseForSessionChange()
        {
            IsManualOpen = false; restoreManualFocus = false; previousFocus = null;
            manualRoot?.SetActive(false); practiceRoot?.SetActive(false);
            // CreateWorld 在重新部署时调用这里；保留训练场实践意图，在 OnSessionStarted 中从头观察。
            // 返回标题或切换其他关卡时 ActiveCampaignLevel 已先改变，因此清除实践意图。
            if (session != null && !session.IsTutorial) practiceRequested = false;
            IsPracticeActive = false; practiceCompleted = false; practiceWorld = null;
            Array.Clear(completedSteps, 0, completedSteps.Length); energyStatus.Clear();
        }

        public bool ConsumeShortcut(bool escape)
        {
            if (!IsManualOpen) return false;
            if (escape) CloseManual();
            return true;
        }

        public void Present(bool canShowPractice)
        {
            if (session == null) return;
            bool completed = session.HasCompletedTutorial;
            if (entryProgressRevision != session.CampaignProgressRevision || entryTutorialCompleted != completed)
            {
                entryProgressRevision = session.CampaignProgressRevision;
                entryTutorialCompleted = completed;
                if (entryLabel != null) entryLabel.text = completed ? "新手引导 · 重温" : "新手引导 · 必修";
                if (entryHint != null) entryHint.text = completed ? "教学已通关 · 可再次训练" : "完成训练，解锁第一关";
            }
            if (IsManualOpen && !session.AtStartPage) { IsManualOpen = false; restoreManualFocus = false; }
            if (manualRoot != null) manualRoot.SetActive(IsManualOpen);
            if (session.AtStartPage && IsPracticeActive) ClearPractice();
            bool visible = IsPracticeActive && !session.AtStartPage && practiceWorld == session.World && canShowPractice && !IsManualOpen;
            if (practiceRoot != null) practiceRoot.SetActive(visible);
            if (!IsPracticeActive || practiceWorld == null || practiceWorld != session.World) return;
            if (Time.unscaledTime >= nextObservation)
            {
                nextObservation = Time.unscaledTime + .15f;
                ObservePractice();
            }
            if (practiceExpanded != null) practiceExpanded.SetActive(!collapsed);
            if (practiceCollapsed != null) practiceCollapsed.SetActive(collapsed);
        }

        private void ObservePractice()
        {
            var world = session.World; var battle = session.Battle;
            if (world == null || battle == null) return;
            var core = world.FindBuilding(battle.CoreBuildingId);
            bool reactorExists = false, turretExists = false, poweredTurret = false;
            energyStatus.Capture(world);
            foreach (var building in world.Buildings)
            {
                if (!building.IsAlive || building.IsSold) continue;
                if (building.Kind == BuildingKind.Reactor) reactorExists = true;
                if (!building.IsTurret) continue;
                turretExists = true;
                if (building.TotalReceived > 0 && energyStatus.GetStatus(building.Id) == BuildingEnergyStatus.Connected)
                    poweredTurret = true;
            }
            bool poweredCore = core != null && core.TotalReceived > 0 &&
                energyStatus.GetStatus(core.Id) == BuildingEnergyStatus.Connected;
            completedSteps[0] |= core != null && session.SelectedBuildingId == core.Id;
            completedSteps[1] |= reactorExists;
            completedSteps[2] |= turretExists;
            completedSteps[3] |= reactorExists && poweredCore && poweredTurret;
            completedSteps[4] |= battle.WaveNumber >= 1;
            completedSteps[5] |= (battle.WaveNumber >= 1 && battle.State == BattleState.Preparing) ||
                battle.WaveNumber > 1 || battle.State == BattleState.Victory;

            int count = 0;
            for (int i = 0; i < completedSteps.Length; i++) if (completedSteps[i]) count++;
            int step = FirstIncompleteStep();
            if (count == completedSteps.Length && !practiceCompleted)
            {
                practiceCompleted = true;
                session.PlayInterfaceSound(DefenseSoundCue.UiConfirm);
            }
            if (practiceTitle != null) practiceTitle.text = practiceCompleted ? "基础操作已完成" :
                "步骤 " + (step + 1).ToString("00") + "  " + PracticeTitles[step];
            if (practiceInstruction != null) practiceInstruction.text = practiceCompleted
                ? "你已完成布防、供电、迎敌和整备。继续并守住第二波，完整通关训练场后才会解锁第一关。离开未完成的训练后，下次将重新开始。"
                : PracticeInstructions[step];
            if (practiceProgress != null) practiceProgress.text = count + " / " + completedSteps.Length;
            if (practiceProgressFill != null) practiceProgressFill.fillAmount = count / (float)completedSteps.Length;
            if (practiceStatus != null) practiceStatus.text = Status(step, battle, reactorExists, turretExists, poweredCore, poweredTurret);
            for (int i = 0; practiceStepIndicators != null && i < practiceStepIndicators.Length; i++)
            {
                if (practiceStepIndicators[i] == null || i >= completedSteps.Length) continue;
                var sprite = completedSteps[i] ? stepCompleted : i == step ? stepCurrent : stepPending;
                if (sprite != null) practiceStepIndicators[i].sprite = sprite;
                practiceStepIndicators[i].color = completedSteps[i] ? new Color(.32f, 1f, .84f) :
                    i == step ? Color.white : new Color(.48f, .63f, .7f);
            }
            if (lastStep != step) { lastStep = step; collapsed = false; }
        }

        private string Status(int step, BattleSystem battle, bool reactor, bool turret, bool poweredCore, bool poweredTurret)
        {
            if (battle.State == BattleState.Defeat) return "核心已被摧毁。重新部署后，引导会从第一步重新开始。";
            if (battle.State == BattleState.Victory) return "训练场已通关，第一关已解锁。";
            if (practiceCompleted) return "基础操作完成 6 / 6 · 仍需守住第二波才能通关。";
            if (step == 0) return "已选中核心 0 / 1";
            if (step == 1) return "已部署反应堆 " + (reactor ? "1" : "0") + " / 1";
            if (step == 2) return "已部署炮塔 " + (turret ? "1" : "0") + " / 1";
            if (step == 3) return (session.Paused ? "已暂停，恢复时间后才能运输能源。\n" : "") +
                "核心实际到货 " + (poweredCore ? "1" : "0") + " / 1   炮塔实际到货 " + (poweredTurret ? "1" : "0") + " / 1";
            if (step == 4) return "供电已确认。准备完成后由你开始第一波。";
            if (battle.State == BattleState.WaveCleared) return "第一波已击退。点击结算窗口中的“继续准备”。";
            return "第 " + battle.WaveNumber + " 波 · 在场 " + battle.Enemies.Count + " · 待入场 " + battle.RemainingToSpawn;
        }

        private int FirstIncompleteStep()
        {
            for (int i = 0; i < completedSteps.Length; i++) if (!completedSteps[i]) return i;
            return completedSteps.Length - 1;
        }

        private void ClearPractice()
        {
            practiceRequested = false; IsPracticeActive = false; practiceCompleted = false; practiceWorld = null;
            Array.Clear(completedSteps, 0, completedSteps.Length); energyStatus.Clear(); practiceRoot?.SetActive(false);
        }

        private void SelectPage(int page) => SelectPage(page, true);
        private void SelectPage(int page, bool sound)
        {
            manualPage = Mathf.Clamp(page, 0, ManualTitles.Length - 1);
            for (int i = 0; i < manualPages.Length; i++) if (manualPages[i] != null) manualPages[i].SetActive(i == manualPage);
            for (int i = 0; chapterIndicators != null && i < chapterIndicators.Length; i++)
            {
                if (chapterIndicators[i] == null) continue;
                var sprite = i == manualPage ? chapterSelected : chapterNormal;
                if (sprite != null) chapterIndicators[i].sprite = sprite;
                chapterIndicators[i].color = i == manualPage ? Color.white : new Color(.68f, .79f, .84f);
            }
            if (manualTitle != null) manualTitle.text = ManualTitles[manualPage];
            if (manualCounter != null) manualCounter.text = (manualPage + 1).ToString("00") + " / 06";
            if (previousPage != null) previousPage.interactable = manualPage > 0;
            if (nextPage != null) nextPage.interactable = manualPage < ManualTitles.Length - 1;
            if (sound) session?.PlayInterfaceSound(DefenseSoundCue.Selection);
        }

        private void PreviousPage() => SelectPage(manualPage - 1);
        private void NextPage() => SelectPage(manualPage + 1);
        private void CollapsePractice() { collapsed = true; Present(true); }
        private void ExpandPractice() { collapsed = false; Present(true); }
        private void ExitPractice() { ClearPractice(); session?.PlayInterfaceSound(DefenseSoundCue.UiBack); }
        private void LateUpdate()
        {
            if (!restoreManualFocus || !IsManualOpen || EventSystem.current == null) return;
            restoreManualFocus = false;
            EventSystem.current.SetSelectedGameObject(chapterButtons[manualPage].gameObject);
        }

        private static void Add(Button button, UnityEngine.Events.UnityAction action) { if (button != null) button.onClick.AddListener(action); }
        private static void Remove(Button button, UnityEngine.Events.UnityAction action) { if (button != null) button.onClick.RemoveListener(action); }
        private void OnDestroy()
        {
            if (chapterActions != null)
                for (int i = 0; i < chapterActions.Length; i++) if (chapterButtons[i] != null) chapterButtons[i].onClick.RemoveListener(chapterActions[i]);
            Remove(openManual, OpenManual); Remove(previousPage, PreviousPage); Remove(nextPage, NextPage);
            Remove(closeManual, CloseManual); Remove(beginPractice, BeginPractice);
            Remove(collapsePractice, CollapsePractice); Remove(expandPractice, ExpandPractice); Remove(exitPractice, ExitPractice);
        }
    }
}
