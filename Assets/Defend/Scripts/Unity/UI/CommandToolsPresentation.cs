using UnityEngine;
using UnityEngine.UI;

namespace Defend.Unity
{
    /// <summary>Explains unavailable command groups without changing the simulation or action eligibility.</summary>
    public sealed class CommandToolsPresentation : MonoBehaviour
    {
        [SerializeField] private Button repairAll, sandboxOpen;
        [SerializeField] private GameObject priorityControls;
        [SerializeField] private GameObject maintenanceHint, priorityHint;

        private void OnEnable() { Refresh(); }
        private void LateUpdate() { Refresh(); }
        private void Refresh()
        {
            bool maintenanceAvailable = repairAll != null && repairAll.gameObject.activeSelf
                || sandboxOpen != null && sandboxOpen.gameObject.activeSelf;
            if (maintenanceHint != null) maintenanceHint.SetActive(!maintenanceAvailable);
            if (priorityHint != null) priorityHint.SetActive(priorityControls == null || !priorityControls.activeSelf);
        }
    }
}
