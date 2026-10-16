using System;
using System.Collections.Generic;
using System.Linq;
using Defend.Simulation;
using NUnit.Framework;

namespace Defend.Tests
{
    public sealed class FormalLevelWaveTests
    {
        private static BuildingDefinition Definition(BuildingKind kind) => new BuildingDefinition(kind, kind.ToString(),
            1, 1, 1000, 0, 0, 0, 1, 0, 1000000, 0, 0, 0);
        private static GameWorld World(WaveDefinition[] waves, SpawnPoint[] entrances, int size = 12, int limit = 200)
            => new GameWorld(new[] {Definition(BuildingKind.Core), Definition(BuildingKind.Turret)}, new WorldSettings(size,size,20,0),
                battleSettings: new BattleSettings(waves, maximumLivingEnemies: limit, spawnPoints: entrances,
                    enemyDefinitions: new[] { new EnemyDefinition(EnemyKind.Normal,"巡猎者","",60,.32,.00001,1,20,.58,2) }));
        private static void PlaceCore(GameWorld world)
            => Assert.That(world.Submit(WorldCommand.Place(BuildingKind.Core,world.Settings.Width-1,world.Settings.Depth-1)).Succeeded, Is.True);
        private static WaveDefinition Wave(params WaveGroupDefinition[] groups)
            => new WaveDefinition(1,60,1,7,groups:groups,independentGroups:true);

        [Test]
        public void FormalPlanConservesApprovedEconomyAndEntranceUnlocks()
        {
            var waves = FormalLevelDefinition.CreateWaves(); var entrances = FormalLevelDefinition.CreateEntrances();
            Assert.That(waves.Length, Is.EqualTo(30)); Assert.That(waves.Sum(w=>w.EnemyCount), Is.EqualTo(2642));
            Assert.That(waves.Sum(w=>w.ClearReward), Is.EqualTo(1970));
            int[] kinds = new int[5]; int incomeBeforeLast = 1500;
            for (int i=0;i<waves.Length;i++)
            {
                Assert.That(waves[i].Groups.Max(g=>g.StartTick), Is.EqualTo(waves[i].PlannedSpawnDurationTicks));
                int count=0;
                foreach (var group in waves[i].Groups)
                {
                    count+=group.Count; kinds[(int)group.Kind]+=group.Count;
                    Assert.That(entrances[group.EntranceIndex].IsOpenAtWave(i+1), Is.True);
                    if (i<29) incomeBeforeLast+=group.Count*FormalLevelDefinition.KillReward(group.Kind);
                    if (i+1==entrances[group.EntranceIndex].UnlockWave && group.EntranceIndex>0)
                        Assert.That(group.Kind, Is.EqualTo(EnemyKind.Normal));
                }
                if (i<29) incomeBeforeLast+=waves[i].ClearReward;
                Assert.That(count, Is.EqualTo(waves[i].EnemyCount));
            }
            Assert.That(kinds, Is.EqualTo(new[]{568,1000,320,400,354})); Assert.That(incomeBeforeLast, Is.EqualTo(9582));
            Assert.That(entrances.Select(e=>e.UnlockWave), Is.EqualTo(new[]{1,5,11,19}));
            Assert.That(entrances[1].IsAnnouncedAtWave(2), Is.False); Assert.That(entrances[1].IsAnnouncedAtWave(3), Is.True);
        }

        [Test]
        public void BlockedEntranceDoesNotHoldTheOtherEntranceOrLoseQuota()
        {
            var world=World(new[]{Wave(new WaveGroupDefinition(EnemyKind.Normal,2,0,0,5),
                new WaveGroupDefinition(EnemyKind.Normal,2,1,0,5))},new[]{new SpawnPoint(0,2),new SpawnPoint(8,8)});
            PlaceCore(world);
            foreach(var cell in new[]{new SpawnPoint(0,1),new SpawnPoint(1,1),new SpawnPoint(1,2),new SpawnPoint(0,3),new SpawnPoint(1,3)})
                Assert.That(world.Submit(WorldCommand.Place(BuildingKind.Turret,cell.X,cell.Z)).Succeeded,Is.True);
            Assert.That(world.Submit(WorldCommand.StartWave()).Succeeded,Is.True);
            for(int i=0;i<12;i++) world.Step();
            Assert.That(world.Battle.Enemies.Count,Is.EqualTo(3)); Assert.That(world.Battle.RemainingToSpawn,Is.EqualTo(1));
            var blocked = world.Battle.Enemies.Single(e=>e.X<2); blocked.Health=0;
            world.Step();
            Assert.That(world.Battle.RemainingToSpawn,Is.Zero); Assert.That(world.Battle.Enemies.Count(e=>e.X<2),Is.EqualTo(1));
        }

        [Test]
        public void IndependentSpawnsRespectLivingLimitAndResumeWithoutLosingEnemies()
        {
            var entrances=new List<SpawnPoint>(); var groups=new List<WaveGroupDefinition>();
            for(int z=0;z<10;z++) for(int x=0;x<20;x++)
            { groups.Add(new WaveGroupDefinition(EnemyKind.Normal,2,entrances.Count,0,1)); entrances.Add(new SpawnPoint(x,z)); }
            var world=World(new[]{Wave(groups.ToArray())},entrances.ToArray(),30); PlaceCore(world);
            world.Submit(WorldCommand.StartWave()); world.Step();
            Assert.That(world.Battle.Enemies.Count,Is.EqualTo(200)); Assert.That(world.Battle.RemainingToSpawn,Is.EqualTo(200));
            world.Step(); Assert.That(world.Battle.Enemies.Count,Is.EqualTo(200));
            world.Battle.Enemies[0].Health=0; world.Step();
            Assert.That(world.Battle.Enemies.Count,Is.EqualTo(200)); Assert.That(world.Battle.RemainingToSpawn,Is.EqualTo(199));
        }

        [Test]
        public void CapacityGoesToEarlierActualArrivalAfterEntranceDelay()
        {
            var world=World(new[]{Wave(new WaveGroupDefinition(EnemyKind.Normal,1,0,10),
                new WaveGroupDefinition(EnemyKind.Normal,1,1,20))},new[]{new SpawnPoint(0,2),new SpawnPoint(8,8)},limit:1);
            PlaceCore(world); world.Submit(WorldCommand.StartWave());
            // 复现一个入口已经累计等待 20 步，而另一个未受阻的调度边界，隔离寻路干扰。
            var delays=(long[])typeof(BattleSystem).GetField("entranceDelays",
                System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(world.Battle);
            delays[0]=20;
            world.Battle.CompleteStep(30);
            Assert.That(world.Battle.Enemies.Count,Is.EqualTo(1));
            Assert.That(world.Battle.Enemies[0].X,Is.EqualTo(8.5));
            Assert.That(world.Battle.RemainingToSpawn,Is.EqualTo(1));
        }

        [Test]
        public void WaveClearRewardsOnceAndNewSessionStartsWithEmptySchedules()
        {
            var waves=new[]{Wave(new WaveGroupDefinition(EnemyKind.Normal,1,0)),Wave(new WaveGroupDefinition(EnemyKind.Normal,1,0))};
            var entrances=new[]{new SpawnPoint(0,0)}; var world=World(waves,entrances); PlaceCore(world);
            for(int wave=0;wave<2;wave++)
            {
                Assert.That(world.Submit(WorldCommand.StartWave()).Succeeded,Is.True); world.Step();
                Assert.That(world.Battle.Enemies.Count,Is.EqualTo(1)); world.Battle.Enemies[0].Health=0; world.Step();
                Assert.That(world.Credits,Is.EqualTo((wave+1)*7));
                for(int i=0;i<5;i++) world.Step();
                Assert.That(world.Credits,Is.EqualTo((wave+1)*7));
                if(wave==0) world.Submit(WorldCommand.ContinuePreparing());
            }
            Assert.That(world.Battle.State,Is.EqualTo(BattleState.Victory)); Assert.That(world.Submit(WorldCommand.StartWave()).Succeeded,Is.False);
            var restarted=World(waves,entrances); PlaceCore(restarted);
            Assert.That(restarted.Battle.WaveNumber,Is.Zero); Assert.That(restarted.Battle.RemainingToSpawn,Is.Zero);
            Assert.That(restarted.Credits,Is.Zero); restarted.Submit(WorldCommand.StartWave()); restarted.Step();
            Assert.That(restarted.Battle.Enemies.Count,Is.EqualTo(1));
        }

        [Test]
        public void AllThirtyWavesDrainTheirActualSpawnQueues()
        {
            var plan = FormalLevelDefinition.CreateWaves();
            var world = World(plan, FormalLevelDefinition.CreateEntrances(), 40);
            PlaceCore(world);
            var observedIdentifiers = new HashSet<long>();
            int totalSpawned = 0;
            for (int waveIndex = 0; waveIndex < plan.Length; waveIndex++)
            {
                Assert.That(world.Submit(WorldCommand.StartWave()).Succeeded, Is.True);
                int[] actualCounts = new int[5];
                int[] expectedCounts = new int[5];
                foreach (var group in plan[waveIndex].Groups) expectedCounts[(int)group.Kind] += group.Count;
                int maximumSteps = plan[waveIndex].PlannedSpawnDurationTicks + 3;
                for (int step = 0; step < maximumSteps && world.Battle.State == BattleState.Combat; step++)
                {
                    world.Step();
                    // 这里只验证真实发兵队列与跨波状态：夹具记录后清除敌人，不代表战斗或平衡通过。
                    foreach (var enemy in world.Battle.Enemies)
                    {
                        if (!enemy.IsAlive) continue;
                        Assert.That(observedIdentifiers.Add(enemy.Id), Is.True, "敌人身份在跨波后重复。");
                        actualCounts[(int)enemy.Kind]++; totalSpawned++;
                        enemy.Health = 0;
                    }
                }
                Assert.That(actualCounts, Is.EqualTo(expectedCounts), "第 " + (waveIndex + 1) + " 波实际发兵配比不符。");
                Assert.That(world.Battle.WaveNumber, Is.EqualTo(waveIndex + 1));
                Assert.That(world.Battle.RemainingToSpawn, Is.Zero);
                Assert.That(world.Battle.Enemies.Count, Is.Zero);
                if (waveIndex < plan.Length - 1)
                {
                    Assert.That(world.Battle.State, Is.EqualTo(BattleState.WaveCleared));
                    Assert.That(world.Submit(WorldCommand.ContinuePreparing()).Succeeded, Is.True);
                }
            }
            Assert.That(totalSpawned, Is.EqualTo(2642));
            Assert.That(world.Battle.State, Is.EqualTo(BattleState.Victory));
            Assert.That(world.Battle.WaveNumber, Is.EqualTo(30));
            Assert.That(world.Battle.RemainingToSpawn, Is.Zero);
            Assert.That(world.Battle.Enemies.Count, Is.Zero);
        }

        [Test]
        public void SandboxCheckpointPreservesSingleCellSpawnReservations()
        {
            var world = new GameWorld(new[] { Definition(BuildingKind.Core), Definition(BuildingKind.Turret) },
                new WorldSettings(40,40,20,0), battleSettings: new BattleSettings(FormalLevelDefinition.CreateWaves(),
                    spawnPoints: FormalLevelDefinition.CreateEntrances()), sandbox: true);
            PlaceCore(world);
            // 正式入口旁格在沙盒始终允许建造；出生格本身始终保留。
            Assert.That(world.PlacementIssue(BuildingKind.Turret,1,10), Is.Null);
            Assert.That(world.PlacementIssue(BuildingKind.Turret,0,10), Is.Not.Null);
            Assert.That(world.Submit(WorldCommand.StartWave()).Succeeded, Is.True);
            Assert.That(world.Battle.StopSandboxWave().Succeeded, Is.True);
            var restored = world.RestoreSandboxCheckpoint(out string error);
            Assert.That(error, Is.Null); Assert.That(restored, Is.Not.Null);
            Assert.That(restored.PlacementIssue(BuildingKind.Turret,1,10), Is.Null);
            Assert.That(restored.PlacementIssue(BuildingKind.Turret,0,10), Is.Not.Null);
            Assert.That(restored.Submit(WorldCommand.Place(BuildingKind.Turret,1,10)).Succeeded, Is.True);
        }

        [Test]
        public void FutureEntrancesReserveTheirEntireWalkableAreaBeforeUnlock()
        {
            var entrances=FormalLevelDefinition.CreateEntrances();
            var world=World(FormalLevelDefinition.CreateWaves(),entrances,40); PlaceCore(world);
            foreach(var entrance in entrances)
                for(int z=entrance.ReservedZ;z<entrance.ReservedZ+3;z++)
                    for(int x=entrance.ReservedX;x<entrance.ReservedX+3;x++)
                    {
                        Assert.That(world.IsTerrainBlocked(x,z),Is.False);
                        Assert.That(world.Submit(WorldCommand.Place(BuildingKind.Turret,x,z)).Succeeded,Is.False);
                    }
            Assert.That(world.Submit(WorldCommand.Place(BuildingKind.Turret,3,10)).Succeeded,Is.True);
            Assert.Throws<ArgumentException>(()=>new BattleSettings(new[]{Wave(new WaveGroupDefinition(EnemyKind.Normal,1,1))},spawnPoints:entrances));
        }
    }
}
