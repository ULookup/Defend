using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Defend.Unity
{
    /// <summary>Moves a visual child only; the Button and its raycast surface remain stationary.</summary>
    public sealed class InterfaceButtonMotion : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler, ISubmitHandler
    {
        [SerializeField] private InterfaceMotionSettings settings;
        [SerializeField] private Button button;
        [SerializeField] private RectTransform visual;
        [SerializeField] private Image highlight;
        [SerializeField] private Image stateSurface;
        [SerializeField] private Sprite normalSprite, hoveredSprite, pressedSprite, selectedSprite, disabledSprite;
        [SerializeField] private bool selected;
        [SerializeField] private Graphic[] foregroundGraphics;
        [SerializeField] private Color[] foregroundColors;
        private Vector3 restScale;
        private Vector2 restPosition;
        private Color highlightColor;
        private bool initialized, hovered, focused, pressed;
        private float pressAmount, highlightAmount, submitTime;
        public bool IsAnimating { get; private set; }
        public bool IsHighlighted => hovered || focused;
        public bool Selected => selected;
        public bool HasStateSprites => stateSurface != null && normalSprite != null;
        private bool Available => isActiveAndEnabled && button != null && button.IsActive() && button.IsInteractable();
        private bool Reduced => settings != null && settings.ReducedMotion;

        public void Configure(InterfaceMotionSettings preferences, Button control, RectTransform visualLayer, Image highlightLayer = null)
        {
            bool reuseHighlight = initialized && highlight != null && highlight == highlightLayer;
            Color restingHighlightColor = highlightColor;
            if (settings != null && isActiveAndEnabled) settings.Changed -= OnPreferenceChanged;
            // A deserialized component has not captured its resting transform yet.
            // Applying the default restScale here would collapse an existing prefab visual to zero.
            if (initialized) ResetVisual();
            settings = preferences;
            button = control;
            visual = visualLayer;
            highlight = highlightLayer;
            initialized = false;
            Initialize();
            if (reuseHighlight) highlightColor = restingHighlightColor;
            if (settings != null && isActiveAndEnabled) settings.Changed += OnPreferenceChanged;
            ResetVisual();
        }

        public void ConfigureStates(Image surface, Sprite normal, Sprite hover, Sprite press, Sprite selection, Sprite disabled)
        {
            stateSurface = surface;
            normalSprite = normal; hoveredSprite = hover; pressedSprite = press;
            selectedSprite = selection; disabledSprite = disabled;
            // One state owner prevents uGUI colour transitions from obscuring authored state art.
            if (button != null) button.transition = Selectable.Transition.None;
            if (stateSurface != null && stateSurface.transform != transform) stateSurface.raycastTarget = false;
            ApplySurface();
        }

        public void SetSelected(bool value) { selected = value; ApplySurface(); }

        public void ConfigureForeground(params Graphic[] graphics)
        {
            foregroundGraphics = graphics;
            foregroundColors = new Color[graphics == null ? 0 : graphics.Length];
            for (int i = 0; i < foregroundColors.Length; i++)
                foregroundColors[i] = graphics[i] != null ? graphics[i].color : Color.white;
            ApplyForeground();
        }

        private void ApplyForeground()
        {
            if (foregroundGraphics == null || foregroundColors == null) return;
            int count = Mathf.Min(foregroundGraphics.Length, foregroundColors.Length);
            for (int i = 0; i < count; i++)
            {
                var graphic = foregroundGraphics[i];
                if (graphic == null || graphic == stateSurface) continue;
                Color color = foregroundColors[i];
                if (!Available) color.a *= .42f;
                graphic.color = color;
            }
        }

        private void Awake() { Initialize(); }
        private void Initialize()
        {
            if (initialized) return;
            initialized = true;
            // A miswired root must never alter the control's hit area.
            if (visual == transform) visual = null;
            if (visual != null) { restScale = visual.localScale; restPosition = visual.anchoredPosition; }
            if (highlight != null) { highlightColor = highlight.color; highlight.raycastTarget = false; }
        }
        private void OnEnable()
        {
            Initialize();
            if (settings != null) settings.Changed += OnPreferenceChanged;
            ResetVisual();
        }
        private void OnDisable()
        {
            if (settings != null) settings.Changed -= OnPreferenceChanged;
            hovered = focused = pressed = false; submitTime = 0;
            ResetVisual();
        }
        private void OnPreferenceChanged(bool value)
        {
            if (!value) return;
            pressAmount = 0;
            highlightAmount = Available && (hovered || focused || pressed || submitTime > 0) ? 1 : 0;
            ApplyVisual();
        }
        private void ResetVisual() { pressAmount = highlightAmount = 0; IsAnimating = false; ApplyVisual(); }
        private void Update()
        {
            float delta = Time.unscaledDeltaTime;
            submitTime = Mathf.Max(0, submitTime - delta);
            if (!Available) { hovered = focused = pressed = false; submitTime = 0; }
            else if (EventSystem.current != null) focused = EventSystem.current.currentSelectedGameObject == gameObject;
            float targetPress = Available && (pressed || submitTime > 0) && !Reduced ? 1 : 0;
            float targetHighlight = Available && (hovered || focused || pressed || submitTime > 0) ? 1 : 0;
            pressAmount = Reduced ? 0 : Mathf.MoveTowards(pressAmount, targetPress, delta / .08f);
            highlightAmount = Reduced ? targetHighlight : Mathf.MoveTowards(highlightAmount, targetHighlight, delta / .12f);
            IsAnimating = !Mathf.Approximately(pressAmount, targetPress) || !Mathf.Approximately(highlightAmount, targetHighlight);
            ApplyVisual();
        }
        private void ApplyVisual()
        {
            ApplySurface();
            if (visual != null)
            {
                visual.localScale = restScale * (1 - .018f * pressAmount);
                visual.anchoredPosition = restPosition + Vector2.down * (1.5f * pressAmount);
            }
            // The authored image covers the complete button surface; input remains on its fixed root.
            if (highlight != null)
            {
                Color color = highlightColor;
                color.a = Mathf.Clamp01(color.a * highlightAmount * (1 + .45f * pressAmount));
                highlight.color = color;
            }
        }

        private void ApplySurface()
        {
            ApplyForeground();
            if (!HasStateSprites) return;
            Sprite next = !Available ? disabledSprite : pressed || submitTime > 0 ? pressedSprite :
                selected ? selectedSprite : hovered || focused ? hoveredSprite : normalSprite;
            if (next == null) next = selected && Available && selectedSprite != null ? selectedSprite : normalSprite;
            if (stateSurface.sprite != next) stateSurface.sprite = next;
        }

        public void OnPointerEnter(PointerEventData data) { hovered = Available; ApplySurface(); }
        public void OnPointerExit(PointerEventData data) { hovered = false; pressed = false; ApplySurface(); }
        public void OnPointerDown(PointerEventData data) { if (data.button == PointerEventData.InputButton.Left && Available) pressed = true; ApplySurface(); }
        public void OnPointerUp(PointerEventData data) { if (data.button == PointerEventData.InputButton.Left) pressed = false; ApplySurface(); }
        public void OnSelect(BaseEventData data) { focused = Available; ApplySurface(); }
        public void OnDeselect(BaseEventData data) { focused = false; pressed = false; ApplySurface(); }
        public void OnSubmit(BaseEventData data) { if (Available) submitTime = .1f; ApplySurface(); }
    }
}
