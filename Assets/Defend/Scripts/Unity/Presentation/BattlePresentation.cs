using System.Collections.Generic;
using Defend.Simulation;
using UnityEngine;
using UnityEngine.Pool;

namespace Defend.Unity
{
    /// <summary>
    /// 池化敌人与瞬时射线，只读取战斗结果，不参与命中和伤害结算。
    /// </summary>
    public sealed class BattlePresentation : MonoBehaviour
    {
        [SerializeField] private EnemyView enemyPrefab;
        [SerializeField] private EnemyView fastEnemyPrefab;
        [SerializeField] private EnemyView heavyEnemyPrefab;
        [SerializeField] private bool externalCombatEffects;

        [SerializeField] private LineRenderer shotPrefab;
        [SerializeField] private Transform enemyRoot;
        [SerializeField] private Transform shotRoot;
        [SerializeField, Min(1)] private int maximumVisibleShots = 256;

        [SerializeField] private EnemyTargetView targetMarkerPrefab;
        [SerializeField] private Transform targetMarkerRoot;
        [SerializeField, Range(1, 200)] private int maximumVisibleTargetMarkers = 100;

        private readonly Dictionary<EnemyKind, ObjectPool<EnemyView>>
            variantPools =
                new Dictionary<EnemyKind, ObjectPool<EnemyView>>();

        private readonly Dictionary<long, EnemyView>
            enemyViews =
                new Dictionary<long, EnemyView>();

        private readonly HashSet<long>
            livingIds =
                new HashSet<long>();

        private readonly List<long>
            removedIds =
                new List<long>();

        private readonly List<LineRenderer>
            shotViews =
                new List<LineRenderer>();

        private readonly List<EnemyTargetView>
            targetMarkers =
                new List<EnemyTargetView>();

        private readonly Dictionary<
            (long building, double x, double z, bool searching),
            int>
            targetIndices =
                new Dictionary<
                    (long, double, double, bool),
                    int>();

        private readonly List<
            (double x, double z, bool searching, bool attacking)>
            targetSnapshots =
                new List<
                    (double, double, bool, bool)>();

        private ObjectPool<EnemyView> enemyPool;
        private ObjectPool<LineRenderer> shotPool;
        private ObjectPool<EnemyTargetView> targetMarkerPool;

        private long lastAppliedTick = -1;

        private MaterialPropertyBlock shotProperties;

        public EnemyView FindView(long id)
        {
            return enemyViews.TryGetValue(
                id,
                out EnemyView view)
                    ? view
                    : null;
        }

        private ObjectPool<EnemyView> Pool(EnemyKind kind)
        {
            return variantPools.TryGetValue(
                kind,
                out ObjectPool<EnemyView> value)
                    ? value
                    : enemyPool;
        }

        private void AddVariant(
            EnemyKind kind,
            EnemyView prefab)
        {
            if (prefab == null ||
                variantPools.ContainsKey(kind))
            {
                return;
            }

            variantPools.Add(
                kind,
                new ObjectPool<EnemyView>(
                    () =>
                        Instantiate(
                            prefab,
                            enemyRoot),

                    view =>
                    {
                        view.ResetForReuse();
                        view.gameObject.SetActive(true);
                    },

                    view =>
                    {
                        view.gameObject.SetActive(false);
                    },

                    view =>
                    {
                        if (view != null)
                        {
                            Destroy(view.gameObject);
                        }
                    },

                    false,
                    16,
                    256));
        }

        public void Configure(
            EnemyView enemy,
            LineRenderer shot,
            Transform enemies,
            Transform shots)
        {
            enemyPrefab = enemy;
            shotPrefab = shot;
            enemyRoot = enemies;
            shotRoot = shots;
        }

        public void ConfigureTargetMarkers(
            EnemyTargetView prefab,
            Transform root)
        {
            targetMarkerPrefab = prefab;
            targetMarkerRoot = root;
        }

        public void Initialize()
        {
            if (shotProperties == null)
            {
                shotProperties =
                    new MaterialPropertyBlock();
            }

            if (enemyPool == null)
            {
                enemyPool =
                    new ObjectPool<EnemyView>(
                        () =>
                            Instantiate(
                                enemyPrefab,
                                enemyRoot),

                        view =>
                        {
                            view.ResetForReuse();
                            view.gameObject.SetActive(true);
                        },

                        view =>
                        {
                            view.gameObject.SetActive(false);
                        },

                        view =>
                        {
                            if (view != null)
                            {
                                Destroy(view.gameObject);
                            }
                        },

                        false,
                        32,
                        256);

                shotPool =
                    new ObjectPool<LineRenderer>(
                        () =>
                            Instantiate(
                                shotPrefab,
                                shotRoot),

                        view =>
                        {
                            view.gameObject.SetActive(true);
                        },

                        view =>
                        {
                            view.gameObject.SetActive(false);
                        },

                        view =>
                        {
                            if (view != null)
                            {
                                Destroy(view.gameObject);
                            }
                        },

                        false,
                        32,
                        maximumVisibleShots);
            }

            if (targetMarkerPool == null &&
                targetMarkerPrefab != null &&
                targetMarkerRoot != null)
            {
                targetMarkerPool =
                    new ObjectPool<EnemyTargetView>(
                        () =>
                            Instantiate(
                                targetMarkerPrefab,
                                targetMarkerRoot),

                        view =>
                        {
                            view.gameObject.SetActive(true);
                        },

                        view =>
                        {
                            view.gameObject.SetActive(false);
                        },

                        view =>
                        {
                            if (view != null)
                            {
                                Destroy(view.gameObject);
                            }
                        },

                        false,
                        8,
                        maximumVisibleTargetMarkers);
            }

            AddVariant(
                EnemyKind.Fast,
                fastEnemyPrefab);

            AddVariant(
                EnemyKind.Heavy,
                heavyEnemyPrefab);

            foreach (EnemyView view in enemyViews.Values)
            {
                Pool(view.Kind).Release(view);
            }

            enemyViews.Clear();

            foreach (LineRenderer view in shotViews)
            {
                shotPool.Release(view);
            }

            shotViews.Clear();

            foreach (EnemyTargetView view in targetMarkers)
            {
                targetMarkerPool.Release(view);
            }

            targetMarkers.Clear();
            targetIndices.Clear();
            targetSnapshots.Clear();
            livingIds.Clear();
            removedIds.Clear();

            lastAppliedTick = -1;
        }

        public void Refresh(
            GameWorld world,
            double displayTick,
            bool paused = false)
        {
            if (world.Battle == null)
            {
                return;
            }

            if (lastAppliedTick != world.CurrentTick)
            {
                RefreshSnapshots(world);
                lastAppliedTick = world.CurrentTick;
            }

            float progress =
                Mathf.Clamp01(
                    (float)(
                        displayTick -
                        world.CurrentTick));

            foreach (EnemyView view in enemyViews.Values)
            {
                view.SetPaused(paused);
                view.Interpolate(progress);
            }
        }

        private void RefreshSnapshots(
            GameWorld world)
        {
            livingIds.Clear();

            for (int index = 0;
                 index < world.Battle.Enemies.Count;
                 index++)
            {
                EnemyState state =
                    world.Battle.Enemies[index];

                if (!state.IsAlive)
                {
                    continue;
                }

                livingIds.Add(state.Id);

                if (!enemyViews.TryGetValue(
                        state.Id,
                        out EnemyView view))
                {
                    view =
                        Pool(state.Kind).Get();

                    enemyViews.Add(
                        state.Id,
                        view);
                }

                view.Apply(
                    state,
                    world.CurrentTick,
                    world.Settings.TicksPerSecond);
            }

            removedIds.Clear();

            foreach (
                KeyValuePair<long, EnemyView> item
                in enemyViews)
            {
                if (!livingIds.Contains(item.Key))
                {
                    Pool(item.Value.Kind).Release(item.Value);
                    removedIds.Add(item.Key);
                }
            }

            foreach (long id in removedIds)
            {
                enemyViews.Remove(id);
            }

            if (externalCombatEffects)
            {
                return;
            }

            RefreshTargetMarkers(world);

            int count =
                Mathf.Min(
                    world.Battle.Shots.Count,
                    maximumVisibleShots);

            while (shotViews.Count < count)
            {
                shotViews.Add(
                    shotPool.Get());
            }

            while (shotViews.Count > count)
            {
                int last =
                    shotViews.Count - 1;

                shotPool.Release(
                    shotViews[last]);

                shotViews.RemoveAt(last);
            }

            for (int index = 0;
                 index < count;
                 index++)
            {
                ShotSnapshot shot =
                    world.Battle.Shots[index];

                LineRenderer line =
                    shotViews[index];

                line.positionCount =
                    shot.SplashRadius > 0
                        ? 35
                        : 2;

                Color color =
                    shot.TurretKind ==
                        BuildingKind.AreaTurret
                            ? new Color(
                                0.3f,
                                0.8f,
                                1f)
                            : shot.TurretKind ==
                                BuildingKind.BurstTurret
                                ? new Color(
                                    1f,
                                    0.3f,
                                    0.6f)
                                : new Color(
                                    1f,
                                    0.8f,
                                    0.3f);

                line.startColor =
                    color;

                line.endColor =
                    color;

                shotProperties.SetColor(
                    "_BaseColor",
                    color);

                shotProperties.SetColor(
                    "_Color",
                    color);

                line.SetPropertyBlock(
                    shotProperties);

                line.startWidth =
                    shot.TurretKind ==
                        BuildingKind.BurstTurret
                            ? 0.16f
                            : 0.065f;

                line.endWidth =
                    line.startWidth;

                line.SetPosition(
                    0,
                    new Vector3(
                        (float)shot.SourceX,
                        0.72f,
                        (float)shot.SourceZ));

                line.SetPosition(
                    1,
                    new Vector3(
                        (float)shot.TargetX,
                        0.5f,
                        (float)shot.TargetZ));

                for (int point = 2;
                     point < line.positionCount;
                     point++)
                {
                    double angle =
                        (point - 2) *
                        System.Math.PI *
                        2 /
                        32;

                    line.SetPosition(
                        point,
                        new Vector3(
                            (float)(
                                shot.TargetX +
                                System.Math.Cos(angle) *
                                shot.SplashRadius),

                            0.2f,

                            (float)(
                                shot.TargetZ +
                                System.Math.Sin(angle) *
                                shot.SplashRadius)));
                }
            }
        }

        private void RefreshTargetMarkers(
            GameWorld world)
        {
            if (targetMarkerPool == null)
            {
                return;
            }

            targetIndices.Clear();
            targetSnapshots.Clear();

            if (world.Battle.Settings.EnemyBehavior.Enabled)
            {
                foreach (EnemyState enemy
                         in world.Battle.Enemies)
                {
                    if (!enemy.IsAlive ||
                        enemy.Kind != EnemyKind.Heavy ||
                        enemy.TargetBuildingId <= 0)
                    {
                        continue;
                    }

                    bool searching =
                        enemy.TargetPurpose ==
                        EnemyTargetPurpose.SearchLastKnownPosition;

                    bool attacking =
                        EnemyView.IsShowingAttack(
                            enemy,
                            world.CurrentTick,
                            world.Settings.TicksPerSecond);

                    var key =
                        (
                            enemy.TargetBuildingId,
                            enemy.TargetX,
                            enemy.TargetZ,
                            searching
                        );

                    if (targetIndices.TryGetValue(
                            key,
                            out int index))
                    {
                        if (attacking)
                        {
                            targetSnapshots[index] =
                                (
                                    enemy.TargetX,
                                    enemy.TargetZ,
                                    searching,
                                    true
                                );
                        }

                        continue;
                    }

                    if (targetSnapshots.Count >=
                        maximumVisibleTargetMarkers)
                    {
                        continue;
                    }

                    targetIndices.Add(
                        key,
                        targetSnapshots.Count);

                    targetSnapshots.Add(
                        (
                            enemy.TargetX,
                            enemy.TargetZ,
                            searching,
                            attacking
                        ));
                }
            }

            while (targetMarkers.Count <
                   targetSnapshots.Count)
            {
                targetMarkers.Add(
                    targetMarkerPool.Get());
            }

            while (targetMarkers.Count >
                   targetSnapshots.Count)
            {
                int last =
                    targetMarkers.Count - 1;

                targetMarkerPool.Release(
                    targetMarkers[last]);

                targetMarkers.RemoveAt(last);
            }

            for (int index = 0;
                 index < targetMarkers.Count;
                 index++)
            {
                var snapshot =
                    targetSnapshots[index];

                targetMarkers[index].Apply(
                    snapshot.x,
                    snapshot.z,
                    snapshot.searching,
                    snapshot.attacking);
            }
        }

        private void OnDestroy()
        {
            foreach (ObjectPool<EnemyView> pool
                     in variantPools.Values)
            {
                pool.Clear();
            }

            enemyPool?.Clear();
            shotPool?.Clear();
            targetMarkerPool?.Clear();
        }
    }
}