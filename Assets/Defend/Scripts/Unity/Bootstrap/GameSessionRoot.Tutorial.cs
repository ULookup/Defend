using UnityEngine;

namespace Defend.Unity
{
    public sealed partial class GameSessionRoot
    {
        [SerializeField] private CampaignLevelAsset tutorialLevel;
        private bool tutorialSession;

        public CampaignLevelAsset TutorialLevel => tutorialLevel;
        public bool IsTutorial => tutorialSession && !IsSandbox && tutorialLevel != null && ActiveCampaignLevel == tutorialLevel;
        public bool HasCompletedTutorial => tutorialLevel != null && CampaignProgress.IsCompleted(tutorialLevel.Id);

        /// <summary>进入独立训练场；完整通关后解锁战役，重新进入时总是从头部署。</summary>
        public void StartTutorial()
        {
            if (tutorialLevel == null)
            {
                ShowFeedback("训练场尚未配置，请检查新手引导关卡资源。");
                PlayInterfaceSound(DefenseSoundCue.UiDenied);
                return;
            }
            FlushProgressForLifecycle();
            sandboxMode = false;
            tutorialSession = true;
            ActiveCampaignLevel = tutorialLevel;
            CreateWorld();
            defenseInterface?.SessionStarted();
            PlayInterfaceSound(DefenseSoundCue.UiConfirm);
        }

        private void ResetTutorialSession() => tutorialSession = false;
    }
}
