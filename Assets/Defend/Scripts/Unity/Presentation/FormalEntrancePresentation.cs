using Defend.Simulation;
using UnityEngine;
using UnityEngine.UI;

namespace Defend.Unity
{
    /// <summary>四处入口的标识只读取正式波次；没有独立计时或模拟状态。</summary>
    public sealed class FormalEntrancePresentation : MonoBehaviour
    {
        [SerializeField] private GameSessionRoot session;
        [SerializeField] private Camera worldCamera;
        [SerializeField] private int entranceIndex;
        [SerializeField] private Renderer[] surfaces;
        [SerializeField] private Text caption;
        [SerializeField] private Transform billboard;
        private MaterialPropertyBlock properties;
        private GameWorld observedWorld;
        private int lastWave = -1;
        private bool lastVisible;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor"), Emission = Shader.PropertyToID("_EmissionColor");

        private void Awake()
        {
            foreach (var surface in surfaces) surface.enabled = false;
            billboard.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            bool visible = session != null && session.World != null && !session.IsSandbox && !session.AtStartPage
                && session.Battle != null && entranceIndex < session.Battle.SpawnPoints.Count;
            if (visible != lastVisible || observedWorld != session?.World)
            {
                foreach (var surface in surfaces) surface.enabled = visible;
                billboard.gameObject.SetActive(visible);
                lastVisible = visible; observedWorld = session?.World; lastWave = -1;
            }
            if (!visible) return;
            if (worldCamera != null) billboard.rotation = worldCamera.transform.rotation;
            int wave = FormalWaveIntelPresenter.PlannedWaveNumber(session.Battle);
            if (wave == lastWave) return;
            lastWave = wave;
            var entrance = session.Battle.SpawnPoints[entranceIndex];
            bool open = entrance.IsOpenAtWave(wave), announced = entrance.IsAnnouncedAtWave(wave);
            Color color = open ? new Color(1, .41f, .19f) : announced ? new Color(1, .79f, .27f) : new Color(.42f, .51f, .57f);
            caption.text = FormalWaveIntelPresenter.EntranceName(entrance, entranceIndex) + "\n"
                + FormalWaveIntelPresenter.EntranceState(entrance, wave)
                + (open ? " · " + FormalWaveIntelPresenter.EntranceCount(session.Battle.Settings.Waves[wave - 1], entranceIndex) + " 名" : " · 第 " + entrance.UnlockWave + " 波");
            caption.color = color;
            if (properties == null) properties = new MaterialPropertyBlock();
            properties.SetColor(BaseColor, color); properties.SetColor(Emission, color * (open ? 1.5f : announced ? .7f : .08f));
            // 已有入口模型的第四个材质槽是橙色信号，结构材质保持原样。
            foreach (var surface in surfaces) if (surface.sharedMaterials.Length > 3) surface.SetPropertyBlock(properties, 3);
        }

        private void OnDisable()
        {
            lastWave = -1; observedWorld = null; lastVisible = false;
            if (billboard != null) billboard.gameObject.SetActive(false);
            if (surfaces != null) foreach (var surface in surfaces)
                if (surface != null && surface.sharedMaterials.Length > 3) surface.SetPropertyBlock(null, 3);
        }
    }
}
