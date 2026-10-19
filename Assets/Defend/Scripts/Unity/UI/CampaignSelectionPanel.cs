using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Defend.Unity
{
    /// <summary>六张预先制作的关卡卡片；显示与解锁权限读取真实会话，不创建界面。</summary>
    public sealed class CampaignSelectionPanel : MonoBehaviour
    {
        [SerializeField] private Button[] cards;
        [SerializeField] private CampaignSelectionCardView[] cardViews;
        [SerializeField] private Text[] cardTitles, cardStates;
        [Serializable] private struct CoverBinding
        {
            public string levelId;
            public Sprite cover;
            [TextArea] public string summary;
        }
        [SerializeField] private CoverBinding[] covers;
        [SerializeField] private Text title, description, facts, credits, status, startLabel;
        [SerializeField] private Text progress, saveStatus, chapter, availability;
        [SerializeField] private CampaignCoverTransition coverTransition;
        [SerializeField] private Image[] progressNodes;
        [SerializeField] private Sprite progressCompleted, progressPending;
        [SerializeField] private Image selectedPreview;
        [SerializeField] private Button start, back;
        private GameSessionRoot session;
        private DefenseInterface owner;
        private int selectedIndex = -1;
        private int progressRevision = -1;
        private bool restoreFocus;
        public int SelectedIndex => selectedIndex;
        public void Initialize(GameSessionRoot root, DefenseInterface defense)
        {
            if (session != null) return;
            session = root; owner = defense;
            if (session.CampaignCatalog == null || cards.Length != session.CampaignCatalog.Levels.Count || cardViews.Length != cards.Length || covers.Length != cards.Length)
                throw new InvalidOperationException("关卡选择页面的目录、卡片与封面数量不一致。");
            foreach (var level in session.CampaignCatalog.Levels) FindCover(level.Id);
            for (int i = 0; i < cards.Length; i++) { int index = i; cards[i].onClick.AddListener(() => Select(index)); }
            start.onClick.AddListener(() => session.StartCampaign(selectedIndex));
            back.onClick.AddListener(owner.ShowStart);
        }
        public void Open()
        {
            if (session == null || session.CampaignCatalog == null) return;
            if (selectedIndex < 0)
            {
                selectedIndex = 0;
                for (int i = 0; i < session.CampaignCatalog.Levels.Count; i++)
                    if (session.IsCampaignUnlocked(i) && !session.IsCampaignCompleted(i)) { selectedIndex = i; break; }
            }
            restoreFocus = true;
            Refresh();
        }
        public void Select(int index)
        {
            if (session?.CampaignCatalog == null || index < 0 || index >= session.CampaignCatalog.Levels.Count) return;
            selectedIndex = index; Refresh(); session.PlayInterfaceSound(DefenseSoundCue.Selection);
        }
        private void Update()
        {
            if (session != null && selectedIndex >= 0 && progressRevision != session.CampaignProgressRevision) Refresh();
        }
        private void OnEnable() { if (session != null) restoreFocus = true; }
        private void OnDisable() { restoreFocus = false; }
        private void LateUpdate()
        {
            // 局外页面在 Update 完成切换后才清理旧焦点，因此等到 LateUpdate 再定位当前关卡。
            if (!restoreFocus || selectedIndex < 0 || EventSystem.current == null) return;
            var current = EventSystem.current.currentSelectedGameObject;
            if (current == null || !current.transform.IsChildOf(transform))
                EventSystem.current.SetSelectedGameObject(cards[selectedIndex].gameObject);
            restoreFocus = false;
        }
        private void Refresh()
        {
            progressRevision = session.CampaignProgressRevision;
            var catalog = session.CampaignCatalog;
            bool tutorialCompleted = session.HasCompletedTutorial;
            int completedCount = 0;
            for (int i = 0; i < cards.Length; i++)
            {
                bool exists = i < catalog.Levels.Count; cards[i].gameObject.SetActive(exists); if (!exists) continue;
                var level = catalog.Levels[i]; bool unlocked = session.IsCampaignUnlocked(i), completed = session.IsCampaignCompleted(i);
                cardTitles[i].text = (i + 1).ToString("00") + "  " + level.DisplayName;
                if (completed) completedCount++;
                if (progressNodes != null && i < progressNodes.Length)
                {
                    progressNodes[i].sprite = completed ? progressCompleted : progressPending;
                    progressNodes[i].color = completed ? Color.white : new Color(.72f, .85f, .9f, 1);
                }
                cardStates[i].color = unlocked ? new Color(.35f, 1f, .88f) : new Color(.65f, .75f, .80f);
                cardStates[i].text = !tutorialCompleted ? "先完成引导" : completed ? "已通关" : unlocked ? "可部署" : "待解锁";
                cardViews[i].Present(FindCover(level.Id).cover, i == selectedIndex, unlocked, completed);
            }
            var selected = catalog.Levels[selectedIndex]; bool available = session.IsCampaignUnlocked(selectedIndex);
            if (availability != null)
            {
                availability.text = !tutorialCompleted ? "先完成引导" : available ? session.IsCampaignCompleted(selectedIndex) ? "● 已通关" : "● 可部署" : "待解锁";
                availability.color = available ? new Color(.35f, 1f, .88f) : new Color(.65f, .75f, .8f);
            }
            var content = FindCover(selected.Id);
            title.text = selected.DisplayName;
            if (chapter != null) chapter.text = "关卡  " + (selectedIndex + 1).ToString("00");
            description.text = content.summary;
            if (coverTransition != null) coverTransition.Show(content.cover); else selectedPreview.sprite = content.cover;
            if (progress != null) progress.text = "已完成 " + completedCount.ToString("00") + " / " + catalog.Levels.Count.ToString("00") + " 关";
            facts.text = selected.WaveCount + " 波";
            credits.text = selected.StartingCredits.ToString();
            status.text = !tutorialCompleted ? "请先通关新手引导，再开始战役。" : available ? session.IsCampaignCompleted(selectedIndex) ? "已通关，可重新部署。" :
                selectedIndex == catalog.Levels.Count - 1 ? "守住最终阵地，完成战役。" : "完成本关，解锁下一关。"
                : selectedIndex > 0 ? "完成第 " + selectedIndex + " 关后解锁。" : "完成新手引导后解锁。";
            if (saveStatus != null) saveStatus.text = session.CampaignSaveStatus;
            start.interactable = available;
            startLabel.text = !tutorialCompleted ? "先完成引导" : !available ? "尚未解锁" : session.IsCampaignCompleted(selectedIndex) ? "重新部署" : "开始部署";
            for (int i = 2; i < cards.Length; i += 3)
            {
                var navigation = cards[i].navigation; navigation.selectOnRight = available ? start : back; cards[i].navigation = navigation;
            }
            var backNavigation = back.navigation; backNavigation.selectOnRight = available ? start : cards[cards.Length - 1]; back.navigation = backNavigation;
        }

        private CoverBinding FindCover(string id)
        {
            int match = -1;
            for (int i = 0; i < covers.Length; i++)
                if (covers[i].levelId == id)
                {
                    if (match >= 0) throw new InvalidOperationException("关卡封面标识重复：" + id);
                    match = i;
                }
            if (match < 0 || covers[match].cover == null || string.IsNullOrWhiteSpace(covers[match].summary))
                throw new InvalidOperationException("关卡的新封面或简介缺失：" + id);
            return covers[match];
        }
    }
}
