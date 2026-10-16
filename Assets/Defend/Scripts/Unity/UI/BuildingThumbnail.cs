using Defend.Simulation;
using UnityEngine;
using UnityEngine.UI;

namespace Defend.Unity
{
    /// <summary>Independent replaceable portrait. This never owns button state, cost, or building selection.</summary>
    [DisallowMultipleComponent]
    public sealed class BuildingThumbnail : MonoBehaviour
    {
        [SerializeField] private Image image;
        [SerializeField] private BuildingThumbnailLibrary library;
        [SerializeField] private BuildingThumbnailContext context;
        [SerializeField] private BuildingKind kind;
        [SerializeField] private Sprite overrideSprite;
        [SerializeField] private Sprite authoredSprite;

        public BuildingKind Kind => kind;
        public Sprite DisplayedSprite => image != null ? image.sprite : null;
        public Image Output => image;

        public void Configure(Image output, BuildingThumbnailLibrary assignments = null,
            BuildingThumbnailContext usage = BuildingThumbnailContext.Shop)
        {
            if (image != output && output != null) authoredSprite = output.sprite;
            image = output;
            library = assignments;
            context = usage;
            Refresh();
        }

        public void SetBuilding(BuildingKind value)
        {
            kind = value;
            Refresh();
        }

        /// <summary>Set a per-control art override; null returns to the shared building library.</summary>
        public void SetSprite(Sprite value) { overrideSprite = value; Refresh(); }

        public void Refresh()
        {
            if (image == null) return;
            image.sprite = ResolveSprite();
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.enabled = image.sprite != null;
        }

        private Sprite ResolveSprite()
        {
            if (overrideSprite != null) return overrideSprite;
            return library != null ? library.Find(kind, context) : authoredSprite;
        }

        private void OnEnable() { Refresh(); }
        private void OnValidate() { Refresh(); }
    }
}
