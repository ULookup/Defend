using System;
using System.Collections.Generic;
using Defend.Simulation;
using UnityEngine;

namespace Defend.Unity
{
    public sealed partial class GameSessionRoot
    {
        [SerializeField] private CampaignCatalogAsset campaignCatalog;
        [SerializeField] private CampaignEnvironmentPresentation campaignEnvironment;
        private CampaignProgressStore campaignProgress;
        private GameWorld observedCampaignWorld;
        private float nextCampaignSaveAttempt;
        public CampaignCatalogAsset CampaignCatalog => campaignCatalog;
        public CampaignLevelAsset ActiveCampaignLevel { get; private set; }
        public bool IsCampaign => ActiveCampaignLevel != null && !IsSandbox && !IsTutorial;
        public bool CampaignSavePending => CampaignProgress.PendingCount > 0;
        public int CampaignProgressRevision => CampaignProgress.Revision;
        public string CampaignSaveStatus
        {
            get
            {
                var store = CampaignProgress;
                if (!string.IsNullOrEmpty(store.LoadWarning))
                    return store.LoadWarning + (store.PendingCount > 0 ? " 通关记录待保存。" : "");
                if (store.PendingCount > 0)
                    return store.LastSaveFailed ? "通关记录正在重试保存，请暂勿关闭游戏。" : "通关与解锁记录正在保存。";
                return HasCompletedTutorial ? "通关与解锁已保存；每次进入都重新部署。" : "仅保存通关与解锁；每次进入都重新部署。";
            }
        }
        private CampaignProgressStore CampaignProgress => campaignProgress ??
            (campaignProgress = new CampaignProgressStore(knownLevelIds: KnownProgressLevelIds()));
        private IEnumerable<string> KnownProgressLevelIds()
        {
            if (tutorialLevel != null) yield return tutorialLevel.Id;
            if (campaignCatalog == null) yield break;
            foreach (var level in campaignCatalog.Levels) if (level != null) yield return level.Id;
        }
        public bool IsCampaignUnlocked(int index) => HasCompletedTutorial && CampaignProgress.IsUnlocked(campaignCatalog, index);
        public bool IsCampaignCompleted(int index) => campaignCatalog != null && index >= 0 && index < campaignCatalog.Levels.Count &&
            CampaignProgress.IsCompleted(campaignCatalog.Levels[index].Id);

        public void StartCampaign(int index)
        {
            FlushProgressForLifecycle();
            if (!IsCampaignUnlocked(index))
            {
                ShowFeedback(HasCompletedTutorial ? "请先完成前一关，再开始此关。" : "请先通关新手引导，再开始战役。");
                PlayInterfaceSound(DefenseSoundCue.UiDenied); return;
            }
            ResetTutorialSession(); sandboxMode = false; ActiveCampaignLevel = campaignCatalog.Levels[index];
            CreateWorld(); defenseInterface?.SessionStarted();
            PlayInterfaceSound(DefenseSoundCue.UiConfirm);
        }
        public void ReturnToCampaignSelection()
        {
            ReturnToStart(); defenseInterface?.ShowCampaignSelection();
        }
        public void RequestQuit()
        {
            FlushProgressForLifecycle();
            Application.Quit();
        }
        private void ObserveCampaignVictory()
        {
            bool newlyCompleted = QueueCurrentVictory();
            SaveCampaignProgress(newlyCompleted);
        }
        private bool QueueCurrentVictory()
        {
            if (AtStartPage || (!IsCampaign && !IsTutorial) || World == null || World == observedCampaignWorld ||
                Battle == null || Battle.State != BattleState.Victory) return false;
            if (!CampaignProgress.QueueVictory(ActiveCampaignLevel, World)) return false;
            observedCampaignWorld = World;
            return true;
        }
        // 生命周期边界不能依赖下一帧：先读取模拟的最终状态，再立即尝试保存通关标记。
        private void FlushProgressForLifecycle()
        {
            QueueCurrentVictory();
            SaveCampaignProgress(true);
        }
        private void SaveCampaignProgress(bool immediate)
        {
            if (campaignProgress == null || campaignProgress.PendingCount == 0 ||
                (!immediate && Time.unscaledTime < nextCampaignSaveAttempt)) return;
            try
            {
                campaignProgress.FlushPending();
                nextCampaignSaveAttempt = 0;
            }
            catch (Exception exception)
            {
                nextCampaignSaveAttempt = Time.unscaledTime + 2;
                Debug.LogWarning("本机关卡通关标记暂未保存，将重试：" + exception.Message, this);
                Feedback = "通关已完成，正在重试保存本机进度。";
                if (isActiveAndEnabled && World != null) RefreshPanels();
            }
        }
        private void OnApplicationPause(bool paused) { if (paused) FlushProgressForLifecycle(); }
        private void OnApplicationFocus(bool focused) { if (!focused) FlushProgressForLifecycle(); }
        private void OnApplicationQuit() => FlushProgressForLifecycle();
    }
}
