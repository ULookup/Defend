using Defend.Simulation;
using UnityEngine;

namespace Defend.Unity
{
    public sealed partial class GameSessionRoot
    {
        private DefenseAudio defenseAudio;
        private BattleState audibleBattleState;
        private BattleState pendingBattleState;
        private long pendingBattleTick, coreWarningTick = -1, shieldRestoredTick = -1, previousShieldEnergy;
        private bool audioStateInitialized, coreWarningActive, shieldWasEmpty;
        private bool energyWarningActive;
        private readonly System.Collections.Generic.Dictionary<long, long> energyWarningTurrets = new System.Collections.Generic.Dictionary<long, long>();
        private float energyShortageSince = -1, energyRecoverySince = -1;

        public void ConfigureAudio(DefenseAudio audio)
        {
            defenseAudio = audio;
            combatArt?.ConfigureAudio(audio);
            ResetAudioState();
        }

        public void PlayInterfaceSound(DefenseSoundCue cue) => defenseAudio?.Play(cue);

        private void ResetAudioState()
        {
            defenseAudio?.ResetSession();
            audioStateInitialized = Battle != null;
            if (Battle != null) audibleBattleState = pendingBattleState = Battle.State;
            pendingBattleTick = World == null ? 0 : World.CurrentTick;
            coreWarningTick = shieldRestoredTick = -1;
            energyWarningActive = false;
            energyWarningTurrets.Clear();
            energyShortageSince = energyRecoverySince = -1;
            BuildingState core = Battle == null ? null : World.FindBuilding(Battle.CoreBuildingId);
            coreWarningActive = core != null && core.Health <= core.Definition.MaximumHealth * .25;
            shieldWasEmpty = false;
            previousShieldEnergy = core == null ? 0 : core.StoredEnergy;
            defenseAudio?.SetCombatPaused(Paused || AtStartPage);
        }

        private void PresentCommandSound(WorldCommand command, CommandResult result)
        {
            if (defenseAudio == null) return;
            if (!result.Succeeded) { defenseAudio.Play(DefenseSoundCue.UiDenied); return; }
            BuildingState building = World.FindBuilding(result.BuildingId != 0 ? result.BuildingId : command.BuildingId);
            Vector3 position = building == null ? new Vector3(command.X + .5f, .3f, command.Z + .5f)
                : new Vector3((float)building.CenterX, .3f, (float)building.CenterZ);
            switch (command.Kind)
            {
                case WorldCommandKind.Place: defenseAudio.PlayAt(DefenseSoundCue.Build, position); break;
                case WorldCommandKind.Move: defenseAudio.PlayAt(DefenseSoundCue.Move, position); break;
                case WorldCommandKind.Sell: defenseAudio.PlayAt(DefenseSoundCue.Sell, position); break;
                case WorldCommandKind.SetPriority:
                case WorldCommandKind.SetFacing:
                case WorldCommandKind.ContinuePreparing:
                    defenseAudio.Play(DefenseSoundCue.UiConfirm); break;
                // 维修与受击使用显示队列中的真实结果；开始波次使用状态边沿。
            }
        }

        private void PresentAudioState()
        {
            if (defenseAudio == null || Battle == null || AtStartPage) return;
            if (!audioStateInitialized) { ResetAudioState(); return; }
            BattleState state = Battle.State;
            if (pendingBattleState != state) { pendingBattleState = state; pendingBattleTick = World.CurrentTick; }
            // 结算提示等到末次伤害和击杀已经显示，避免抢先宣告结果。
            bool resultsDisplayed = state == BattleState.Combat || state == BattleState.Preparing
                || displayedTick + 1e-6 >= pendingBattleTick;
            if (audibleBattleState != state && resultsDisplayed)
            {
                audibleBattleState = state;
                switch (state)
                {
                    case BattleState.Combat: defenseAudio.Play(DefenseSoundCue.WaveStart); break;
                    case BattleState.WaveCleared: defenseAudio.Play(DefenseSoundCue.WaveClear); break;
                    case BattleState.Victory: defenseAudio.Play(DefenseSoundCue.Victory); break;
                    case BattleState.Defeat: defenseAudio.Play(DefenseSoundCue.Defeat); break;
                }
            }
            if (Paused || state != BattleState.Combat)
            {
                energyShortageSince = energyRecoverySince = -1;
                if (state != BattleState.Combat)
                {
                    energyWarningActive = false;
                    energyWarningTurrets.Clear();
                    BuildingState restingCore = World.FindBuilding(Battle.CoreBuildingId);
                    coreWarningActive = restingCore != null && restingCore.Health <= restingCore.Definition.MaximumHealth * .25;
                    previousShieldEnergy = restingCore == null ? 0 : restingCore.StoredEnergy;
                    shieldWasEmpty = false;
                    coreWarningTick = shieldRestoredTick = -1;
                }
                return;
            }
            BuildingState core = World.FindBuilding(Battle.CoreBuildingId);
            if (core != null && core.IsAlive)
            {
                bool warning = core.Health <= core.Definition.MaximumHealth * .25;
                if (warning && !coreWarningActive) coreWarningTick = World.CurrentTick;
                if (!warning) coreWarningTick = -1;
                if (coreWarningTick >= 0 && displayedTick + 1e-6 >= coreWarningTick)
                { defenseAudio.Play(DefenseSoundCue.CoreWarning); coreWarningTick = -1; }
                coreWarningActive = warning;
                if (core.StoredEnergy == 0)
                { if (previousShieldEnergy > 0) shieldWasEmpty = true; shieldRestoredTick = -1; }
                else if (shieldWasEmpty && core.StoredEnergy >= System.Math.Max(1, core.Definition.EnergyCapacity / 10))
                {
                    if (shieldRestoredTick < 0) shieldRestoredTick = World.CurrentTick;
                    if (displayedTick + 1e-6 >= shieldRestoredTick)
                    {
                        shieldWasEmpty = false; shieldRestoredTick = -1;
                        defenseAudio.PlayAt(DefenseSoundCue.ShieldRestore, new Vector3((float)core.CenterX, .5f, (float)core.CenterZ));
                    }
                }
                previousShieldEnergy = core.StoredEnergy;
            }
            bool shortage = false;
            foreach (BuildingState building in World.Buildings)
            {
                if (!building.IsAlive || building.IsSold || !building.IsTurret) continue;
                if (building.CombatState == TurretCombatState.WaitingForEnergy)
                {
                    shortage = true;
                    energyWarningTurrets[building.Id] = building.ShotsFired;
                }
            }
            float now = Time.unscaledTime;
            if (shortage)
            {
                energyRecoverySince = -1;
                if (energyShortageSince < 0) energyShortageSince = now;
                if (!energyWarningActive && now - energyShortageSince >= .65f)
                { energyWarningActive = true; defenseAudio.Play(DefenseSoundCue.EnergyStarved); }
            }
            else
            {
                energyShortageSince = -1;
                // 只核对曾缺能的炮塔；其他炮塔开火或目标离开都不能证明它恢复。
                bool recovered = true, survivingTurret = false;
                foreach (var entry in energyWarningTurrets)
                {
                    BuildingState affected = World.FindBuilding(entry.Key);
                    if (affected == null || !affected.IsAlive || affected.IsSold) continue;
                    survivingTurret = true;
                    if (affected.StoredEnergy < World.GetTurretDefinition(affected.Kind).EnergyPerShot
                        && affected.ShotsFired <= entry.Value && affected.CombatState != TurretCombatState.Charging)
                        recovered = false;
                }
                if (!energyWarningActive) { energyWarningTurrets.Clear(); energyRecoverySince = -1; return; }
                if (!survivingTurret) { energyWarningActive = false; energyWarningTurrets.Clear(); return; }
                if (!recovered) { energyRecoverySince = -1; return; }
                if (energyRecoverySince < 0) energyRecoverySince = now;
                if (now - energyRecoverySince >= .4f)
                { energyWarningActive = false; energyWarningTurrets.Clear(); defenseAudio.Play(DefenseSoundCue.EnergyRestored); }
            }
        }
    }
}
