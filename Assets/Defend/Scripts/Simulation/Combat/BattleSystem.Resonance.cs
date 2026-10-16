using System;

namespace Defend.Simulation
{
    public sealed partial class BattleSystem
    {
        /// <summary>只在已完成扣能并真实发射之后调用。瞄准、蓄力、命中数和磁暴均不提供额外共振。</summary>
        private void ObserveFriendlyAttack(BuildingState source, long tick)
        {
            if (State != BattleState.Combat || !source.IsAlive || source.IsSold) return;
            foreach (var tower in world.Buildings)
            {
                if (!ActiveResonanceTower(tower)) continue;
                var rules = tower.Definition.Resonance;
                int contribution = rules.Contribution(source.Kind);
                if (contribution == 0) continue;
                double dx = source.CenterX - tower.CenterX, dz = source.CenterZ - tower.CenterZ;
                if (dx * dx + dz * dz > rules.AbsorptionRange * rules.AbsorptionRange) continue;
                tower.RepairResonance = checked(tower.RepairResonance + contribution);
                tower.BurstResonance = checked(tower.BurstResonance + contribution);
                tower.TotalResonanceReceived = checked(tower.TotalResonanceReceived + contribution);
                world.Record(WorldEventKind.ResonanceAbsorbed, tower.Id, tower.CenterX, tower.CenterZ, contribution);
                RepairFromResonance(tower, rules, tick);
                TryBeginResonanceBurst(tower, rules, tick);
            }
        }

        private void StepResonance(long tick)
        {
            if (State != BattleState.Combat) return;
            foreach (var tower in world.Buildings)
            {
                if (!ActiveResonanceTower(tower)) continue;
                var rules = tower.Definition.Resonance;
                RepairFromResonance(tower, rules, tick);
                if (tower.ResonanceBurstResolveTick >= 0 && tick >= tower.ResonanceBurstResolveTick)
                {
                    // 前摇只是准备。正式起爆再次检查余额，并且仅在这一入口扣能、扣共振和造成伤害。
                    if (tower.BurstResonance >= rules.BurstThreshold && ConsumeResonanceEnergy(tower, rules.BurstEnergyCost))
                    {
                        tower.BurstResonance -= rules.BurstThreshold;
                        ReleaseResonanceBurst(tower, rules, tick);
                    }
                    CancelResonanceBurst(tower);
                }
                TryBeginResonanceBurst(tower, rules, tick);
            }
        }

        private void RepairFromResonance(BuildingState tower, ResonanceDefinition rules, long tick)
        {
            while (tower.RepairResonance >= rules.RepairThreshold)
            {
                // 满血时阈值仍通过，不把已获得的修复积累成之后的一次巨额治疗，也不扣无效治疗的电量。
                if (tower.Health >= tower.Definition.MaximumHealth)
                {
                    tower.RepairResonance %= rules.RepairThreshold;
                    break;
                }
                if (!ConsumeResonanceEnergy(tower, rules.RepairEnergyCost)) break;
                tower.RepairResonance -= rules.RepairThreshold;
                int restored = Math.Min(rules.RepairAmount, tower.Definition.MaximumHealth - tower.Health);
                tower.Health += restored;
                tower.LastRepairTick = tick;
                world.Record(WorldEventKind.ResonanceRepair, tower.Id, tower.CenterX, tower.CenterZ, restored);
            }
        }

        private void TryBeginResonanceBurst(BuildingState tower, ResonanceDefinition rules, long tick)
        {
            if (tower.ResonanceBurstResolveTick >= 0 || tick < tower.NextResonanceBurstTick ||
                tower.BurstResonance < rules.BurstThreshold || tower.StoredEnergy < rules.BurstEnergyCost) return;
            tower.ResonanceBurstStartedTick = tick;
            tower.ResonanceBurstResolveTick = checked(tick + ResonanceDefinition.Ticks(rules.BurstWindupSeconds, world.Settings.TicksPerSecond));
            tower.NextResonanceBurstTick = checked(tick + ResonanceDefinition.Ticks(rules.BurstCooldownSeconds, world.Settings.TicksPerSecond));
            world.Record(WorldEventKind.MagneticStormCharging, tower.Id, tower.CenterX, tower.CenterZ);
        }

        private void ReleaseResonanceBurst(BuildingState tower, ResonanceDefinition rules, long tick)
        {
            bool feedback = tower.ResonanceFeedbackReady && tower.LastEnemyDamageTick >= 0 &&
                tick - tower.LastEnemyDamageTick <= ResonanceDefinition.Ticks(rules.RecentDamageSeconds, world.Settings.TicksPerSecond);
            int amount = feedback ? (int)Math.Min(int.MaxValue, Math.Floor(rules.BurstDamage * rules.RecentDamageBonusMultiplier)) : rules.BurstDamage;
            tower.ResonanceFeedbackReady = false;
            tower.ResonanceBurstsReleased++;
            world.Record(WorldEventKind.MagneticStormBurst, tower.Id, tower.CenterX, tower.CenterZ, amount);
            double time = (double)tick / world.Settings.TicksPerSecond;
            foreach (var enemy in enemies)
            {
                if (!enemy.IsAlive) continue;
                double dx = enemy.X - tower.CenterX, dz = enemy.Z - tower.CenterZ;
                if (dx * dx + dz * dz > rules.BurstRadius * rules.BurstRadius) continue;
                int damage = Math.Min(enemy.Health, amount);
                enemy.Health -= damage;
                var hit = new HitContext(tower, enemy, rules.BurstRadius);
                RecordEnemyDamage(enemy, damage, hit, time, enemy.X, enemy.Z, enemy.X, enemy.Z);
                if (!enemy.IsAlive) continue;
                // 多座塔刷新期限并取最强减速，倍率不相乘；在本步移动完成后附加，覆盖接下来的完整时长。
                enemy.MovementSpeedMultiplier = Math.Min(enemy.MovementSpeedMultiplier, rules.SlowMultiplier);
                enemy.SlowUntilTick = Math.Max(enemy.SlowUntilTick,
                    checked(tick + ResonanceDefinition.Ticks(rules.SlowSeconds, world.Settings.TicksPerSecond)));
            }
        }

        private bool ConsumeResonanceEnergy(BuildingState tower, long amount) => amount == 0 || world.Energy.TryConsume(tower.Id, amount);
        private static bool ActiveResonanceTower(BuildingState tower) =>
            tower.Kind == BuildingKind.ResonanceMagneticTower && tower.IsAlive && !tower.IsSold;
        internal static void CancelResonanceBurst(BuildingState tower)
        {
            tower.ResonanceBurstStartedTick = -1;
            tower.ResonanceBurstResolveTick = -1;
        }
        private static void ClearResonance(BuildingState tower)
        {
            CancelResonanceBurst(tower);
            tower.NextResonanceBurstTick = 0;
            tower.RepairResonance = tower.BurstResonance = 0;
            tower.ResonanceFeedbackReady = false;
        }
    }
}
