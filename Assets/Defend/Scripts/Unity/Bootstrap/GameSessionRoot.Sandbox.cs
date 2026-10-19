using System;
using System.IO;
using Defend.Simulation;
using UnityEngine;

namespace Defend.Unity
{
    public sealed partial class GameSessionRoot
    {
        private bool sandboxMode;
        public bool IsSandbox => World != null && World.IsSandbox;
        public bool HasSandboxCheckpoint => IsSandbox && World.HasSandboxCheckpoint;
        public BuildingDefinition FindEffectiveDefinition(BuildingKind kind) => World.GetDefinition(kind);
        public void RefreshSandboxState()
        {
            presentation.ObserveCommands(World); presentation.Invalidate();
            presentation.Refresh(World, SelectedBuildingId, World.CurrentTick);
            RefreshPanels();
        }
        public void StartSandbox() { FlushProgressForLifecycle(); ResetTutorialSession(); ActiveCampaignLevel = null; sandboxMode = true; CreateWorld(); defenseInterface?.SessionStarted(); }
        public void StartNormal()
        {
            if (campaignCatalog != null) { ReturnToCampaignSelection(); return; }
            FlushProgressForLifecycle(); ResetTutorialSession(); ActiveCampaignLevel = null; sandboxMode = false; CreateWorld(); defenseInterface?.SessionStarted();
        }
        public void EndSandboxWave()
        {
            if (!IsSandbox || AtStartPage) return;
            var result = Battle.StopSandboxWave();
            if (result.Succeeded)
            {
                StopCameraFollow();
                combatArt?.ResetSession(); battlePresentation?.Initialize();
                combatArt?.Initialize(World, presentation, battlePresentation, false);
                ResetAudioState();
                PlayInterfaceSound(DefenseSoundCue.UiConfirm);
            }
            else PlayInterfaceSound(DefenseSoundCue.UiDenied);
            ShowFeedback(result.Message);
        }
        public void RestoreSandboxCheckpoint()
        {
            if (!IsSandbox || AtStartPage) return;
            var restored = World.RestoreSandboxCheckpoint(out string error);
            if (restored == null) { ShowFeedback(error); PlayInterfaceSound(DefenseSoundCue.UiDenied); return; }
            ReplaceSandboxWorld(restored);
            PlayInterfaceSound(DefenseSoundCue.UiConfirm);
            ShowFeedback("已恢复真实的开战前状态，当前处于暂停准备阶段。");
        }
        private void ReplaceSandboxWorld(GameWorld replacement)
        {
            StopCameraFollow();
            defenseInterface?.CloseForSessionChange();
            combatArt?.ResetSession(); placementPresentation?.ResetSession();
            World = replacement; sandboxMode = true; BindWorld();
            defenseInterface?.SessionStarted();
        }
        private SandboxPresetStore PresetStore => new SandboxPresetStore(Path.Combine(Application.persistentDataPath, "SandboxPresets"));
        public string[] ListSandboxPresets()
        {
            try { return PresetStore.List(); }
            catch (Exception exception) { ShowFeedback("读取方案列表失败：" + exception.Message); return Array.Empty<string>(); }
        }
        public void SaveSandboxPreset(string name)
        {
            if (!CanUsePreset()) return;
            try { PresetStore.Save(name, World); ShowFeedback("沙盒方案已保存：" + name); PlayInterfaceSound(DefenseSoundCue.UiConfirm); }
            catch (Exception exception) { ShowFeedback("保存失败：" + exception.Message); PlayInterfaceSound(DefenseSoundCue.UiDenied); }
        }
        public void LoadSandboxPreset(string name)
        {
            if (!CanUsePreset()) return;
            try { var candidate = PresetStore.Load(name, World); ReplaceSandboxWorld(candidate); ShowFeedback("方案已载入：建筑满生命，库存与在途能源为空，当前暂停准备。"); PlayInterfaceSound(DefenseSoundCue.UiConfirm); }
            catch (Exception exception) { ShowFeedback("载入失败，当前世界保持不变：" + exception.Message); PlayInterfaceSound(DefenseSoundCue.UiDenied); }
        }
        private bool CanUsePreset()
        {
            if (!IsSandbox || AtStartPage || !World.CanEditBuildings) { ShowFeedback("只能在沙盒准备阶段保存或载入方案。"); PlayInterfaceSound(DefenseSoundCue.UiDenied); return false; }
            if (defenseInterface != null && defenseInterface.HasUnappliedSandboxChanges) { ShowFeedback("请先应用或放弃尚未应用的修改。"); PlayInterfaceSound(DefenseSoundCue.UiDenied); return false; }
            SetPaused(true); return true;
        }
    }
}
