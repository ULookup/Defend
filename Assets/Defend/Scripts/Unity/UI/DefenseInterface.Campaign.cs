using UnityEngine;
using UnityEngine.UI;

namespace Defend.Unity
{
    public sealed partial class DefenseInterface
    {
        [SerializeField] private CampaignSelectionPanel campaignSelection;
        private bool campaignSelectionOpen;
        private Text campaignPauseReturnLabel, campaignResultReturnLabel;
        public void ShowCampaignSelection()
        {
            if (session == null || session.CampaignCatalog == null || campaignSelection == null) { ShowStart(); return; }
            campaignSelectionOpen = true; modal = returnModal = Modal.None; pageTransitionFrame = Time.frameCount;
            motionCoordinator?.ResetPresentation(); campaignSelection.Open(); Refresh();
        }
        private void ReturnFromBattle()
        { if (session.IsCampaign || session.IsTutorial) session.ReturnToCampaignSelection(); else session.ReturnToStart(); }
        private void RefreshCampaignReturnLabels()
        {
            if (campaignPauseReturnLabel == null) campaignPauseReturnLabel = returnHome.GetComponentInChildren<Text>(true);
            if (campaignResultReturnLabel == null) campaignResultReturnLabel = resultHome.GetComponentInChildren<Text>(true);
            string label = session.IsCampaign || session.IsTutorial ? "返回关卡选择" : "返回开始界面";
            if (campaignPauseReturnLabel != null) campaignPauseReturnLabel.text = label;
            if (campaignResultReturnLabel != null) campaignResultReturnLabel.text = label;
        }
    }
}
