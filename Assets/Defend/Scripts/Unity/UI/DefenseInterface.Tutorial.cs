using UnityEngine;

namespace Defend.Unity
{
    public sealed partial class DefenseInterface
    {
        [SerializeField] private NewPlayerGuide newPlayerGuide;
        public NewPlayerGuide NewPlayerGuide => newPlayerGuide;
        public bool IsNewPlayerGuideOpen => newPlayerGuide != null && newPlayerGuide.IsManualOpen;
        public void OpenNewPlayerGuide() => newPlayerGuide?.OpenManual();
    }
}
