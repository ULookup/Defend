using System;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Defend.Unity
{
    [Serializable] public sealed class ArchiveCatalog { public string[] categories; public ArchiveEntry[] entries; }
    [Serializable] public sealed class ArchiveEntry
    {
        public string id, title, subtitle, body, art;
        public int category;
        public ArchiveStat[] stats;
    }
    [Serializable] public sealed class ArchiveStat { public string label, value, icon; public float ratio; }

    /// <summary>只绑定预先制作的档案控件。策划正文与展示数值完全来自配置。</summary>
    public sealed class DefenseArchiveView : MonoBehaviour
    {
        [SerializeField] private TextAsset catalog;
        [SerializeField] private UnityEngine.UI.Button[] categories, entries;
        [SerializeField] private UnityEngine.UI.Button topClose, bottomClose;
        [SerializeField] private UnityEngine.UI.Text title, subtitle, body, entryCount;
        [SerializeField] private UnityEngine.UI.Image portrait;
        [SerializeField] private RectTransform bodyScroll;
        [SerializeField] private UnityEngine.UI.ScrollRect listScroll, detailScroll;
        [SerializeField] private GameObject statsPanel;
        [SerializeField] private GameObject storyQuote;
        [SerializeField] private UnityEngine.UI.Text[] statLabels, statValues;
        [SerializeField] private UnityEngine.UI.Image[] statIcons, statFills;
        [SerializeField] private Sprite[] illustrations, icons;
        [SerializeField] private Sprite normal, selected, pressed;
        [SerializeField] private Sprite storyPlate;
        private ArchiveCatalog data;
        private ArchiveEntry[] current;
        private int category, entry;
        private bool initialized;
        private GameObject previousSelection;
        private GameObject lastSelection;
        public ArchiveEntry SelectedEntry => current?[entry];

        public void Initialize(Action close)
        {
            if (initialized) return;
            data = JsonUtility.FromJson<ArchiveCatalog>(catalog.text);
            if (data?.entries == null || data.categories.Length != categories.Length)
                throw new InvalidOperationException("档案配置或分类绑定不完整。");
            initialized = true;
            for (int i = 0; i < categories.Length; i++) { int index = i; categories[i].onClick.AddListener(() => SelectCategory(index)); }
            for (int i = 0; i < entries.Length; i++) { int index = i; entries[i].onClick.AddListener(() => SelectEntry(index)); }
            topClose.onClick.AddListener(() => close()); bottomClose.onClick.AddListener(() => close());
            SelectCategory(0);
        }

        private void OnEnable()
        {
            if (!initialized) return;
            previousSelection = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            SelectCategory(category);
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(categories[category].gameObject);
        }
        private void OnDisable()
        {
            if (initialized && EventSystem.current != null && previousSelection != null && previousSelection.activeInHierarchy)
                EventSystem.current.SetSelectedGameObject(previousSelection);
        }

        public void SelectCategory(int index)
        {
            if (index < 0 || index >= categories.Length) return;
            category = index; current = data.entries.Where(x => x.category == index).ToArray();
            if (current.Length == 0 || current.Length > entries.Length) throw new InvalidOperationException("档案条目容量不足。");
            float start = .02f;
            for (int i = 0; i < categories.Length; i++)
            {
                bool active=i==index; Style(categories[i],active);
                var rect=(RectTransform)categories[i].transform; float width=active?.62f:.17f;
                Layout(rect,new Vector2(start,.12f),new Vector2(start+width,.88f)); start+=width;
                var label=categories[i].GetComponentInChildren<UnityEngine.UI.Text>(); label.enabled=active;
                Layout(label.rectTransform,new Vector2(.23f,0),Vector2.one);
                var icon=(RectTransform)categories[i].transform.Find("分类图标"); Layout(icon,new Vector2(active?.025f:.15f,.16f),new Vector2(active?.21f:.85f,.84f));
            }
            for (int i = 0; i < entries.Length; i++)
            {
                entries[i].gameObject.SetActive(i < current.Length);
                if (i < current.Length)
                {
                    var label=entries[i].GetComponentInChildren<UnityEngine.UI.Text>(); label.text=current[i].title;
                    bool story=index==0; entries[i].GetComponent<UnityEngine.UI.LayoutElement>().preferredHeight=story?340:64;
                    Layout(label.rectTransform,story?new Vector2(.06f,.68f):Vector2.zero,story?new Vector2(.94f,.88f):Vector2.one);
                }
            }
            entryCount.text = data.categories[index] + "  /  " + current.Length.ToString("00");
            listScroll.StopMovement(); listScroll.verticalNormalizedPosition = 1;
            SelectEntry(0);
            var controls = categories.Concat(entries.Take(current.Length)).Concat(new[] { topClose }).ToArray();
            for (int i = 0; i < controls.Length; i++)
            {
                var navigation = controls[i].navigation; navigation.mode = UnityEngine.UI.Navigation.Mode.Explicit;
                navigation.selectOnUp = controls[(i + controls.Length - 1) % controls.Length];
                navigation.selectOnDown = controls[(i + 1) % controls.Length]; controls[i].navigation = navigation;
            }
        }

        public void SelectEntry(int index)
        {
            if (current == null || index < 0 || index >= current.Length) return;
            entry = index; ArchiveEntry item = current[index];
            for (int i = 0; i < current.Length; i++) Style(entries[i], i == index);
            title.text = item.title; subtitle.text = item.subtitle;
            // 用不可断空格保持列表标记与正文相连；配置文字和可见字符保持原样。
            body.text = item.body.Replace("- ", "-\u00a0");
            bool story = category == 0;
            storyQuote.SetActive(story);
            portrait.gameObject.SetActive(!story); statsPanel.SetActive(!story);
            portrait.sprite = story ? null : illustrations.FirstOrDefault(x => x != null && x.name == item.art);
            // 正文区在故事模式占满详情面板，长文仍使用同一个保存的滚动控件。
            Layout(bodyScroll,story?new Vector2(0,.02f):new Vector2(0,.01f),story?Vector2.one:new Vector2(.615f,.3f));
            Layout(detailScroll.viewport,story?new Vector2(.085f,.07f):new Vector2(.055f,.06f),story?new Vector2(.94f,.80f):new Vector2(.95f,.70f));
            title.text=story?data.categories[0]:item.title;
            Layout(title.rectTransform,story?new Vector2(.085f,.81f):new Vector2(.035f,.229f),story?new Vector2(.9f,.93f):new Vector2(.58f,.29f));
            title.color=new Color(.10f,.46f,.53f); title.fontSize=story?44:27;
            subtitle.gameObject.SetActive(false); title.transform.SetAsLastSibling();
            body.fontSize=story?23:18; body.lineSpacing=.86f;
            for (int i = 0; i < statLabels.Length; i++)
            {
                bool exists = i < item.stats.Length; statLabels[i].transform.parent.gameObject.SetActive(exists);
                if (!exists) continue;
                ArchiveStat stat = item.stats[i]; statLabels[i].text = stat.label; statValues[i].text = stat.value;
                statIcons[i].sprite = icons.FirstOrDefault(x => x.name == "UI_Icon_Archive_Stat_" + stat.icon + "_01");
                statFills[i].fillAmount = stat.ratio;
                statFills[i].transform.parent.gameObject.SetActive(stat.icon != "Target" || stat.label != "目标偏好");
            }
            Canvas.ForceUpdateCanvases(); detailScroll.StopMovement(); detailScroll.verticalNormalizedPosition = 1;
        }

        private static void Layout(RectTransform rect,Vector2 min,Vector2 max)
        {
            rect.anchorMin=min; rect.anchorMax=max; rect.pivot=new Vector2(.5f,.5f); rect.anchoredPosition=Vector2.zero; rect.sizeDelta=Vector2.zero;
        }

        private void Style(UnityEngine.UI.Button button, bool active)
        {
            var image = (UnityEngine.UI.Image)button.targetGraphic;
            bool storyCard=category==0 && entries.Contains(button);
            image.sprite = storyCard ? storyPlate : active ? selected : normal; image.overrideSprite = null;
            image.color = active && !storyCard ? new Color(.57f,.85f,.94f) : Color.white;
            var states = button.spriteState; states.pressedSprite = pressed;
            states.highlightedSprite = states.selectedSprite = storyCard?storyPlate:active ? selected : pressed;
            if(storyCard) states.pressedSprite=storyPlate; button.spriteState = states;
            var label = button.GetComponentInChildren<UnityEngine.UI.Text>();
            label.color = active ? new Color(.04f,.24f,.29f) : new Color(.17f,.20f,.23f);
            var feedback = button.GetComponent<ArchiveButtonFeedback>();
            if (feedback != null) feedback.Configure(label, pressed, label.color, new Color(.85f,1f,1f));
        }

        private void LateUpdate()
        {
            if (!initialized || EventSystem.current == null) return;
            var focused = EventSystem.current.currentSelectedGameObject;
            if (focused == lastSelection) return; lastSelection = focused;
            if (!entries.Any(x => x.gameObject == focused && x.gameObject.activeInHierarchy)) return;
            Canvas.ForceUpdateCanvases(); var corners = new Vector3[4]; ((RectTransform)focused.transform).GetWorldCorners(corners);
            float min = listScroll.viewport.InverseTransformPoint(corners[0]).y, max = listScroll.viewport.InverseTransformPoint(corners[1]).y;
            float delta = min < listScroll.viewport.rect.yMin ? listScroll.viewport.rect.yMin - min : max > listScroll.viewport.rect.yMax ? listScroll.viewport.rect.yMax - max : 0;
            listScroll.StopMovement(); listScroll.content.anchoredPosition += new Vector2(0,delta);
        }
    }
}
