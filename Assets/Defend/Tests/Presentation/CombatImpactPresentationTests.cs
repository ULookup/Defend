using System.Collections.Generic;
using Defend.Simulation;
using Defend.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace Defend.Tests
{
    public sealed class CombatImpactPresentationTests
    {
        [Test]
        public void DamageNumbersKeepIndividualEnemyHitsAndIgnoreBuildings()
        {
            var root = new GameObject("数字显示测试");
            var prefab = new GameObject("数字预制体测试", typeof(RectTransform), typeof(CanvasGroup), typeof(Text), typeof(DamageNumberView));
            try
            {
                var view = prefab.GetComponent<DamageNumberView>();
                view.Configure(prefab.GetComponent<Text>(), prefab.GetComponent<CanvasGroup>());
                var numbers = root.AddComponent<DamageNumberPresentation>();
                numbers.Configure(view, null, root.transform);
                numbers.Show(new WorldEventSnapshot(WorldEventKind.EnemyHit, 7, 2, 3, 10));
                numbers.Show(new WorldEventSnapshot(WorldEventKind.EnemyHit, 7, 2, 3, 15));
                numbers.Show(new WorldEventSnapshot(WorldEventKind.BuildingHit, 4, 2, 3, 15));
                Assert.That(numbers.ActiveCount, Is.EqualTo(2));
                numbers.AdvanceFeedback(0);
                Assert.That(numbers.ActiveCount, Is.EqualTo(2));
                numbers.AdvanceFeedback(.7f);
                Assert.That(numbers.ActiveCount, Is.Zero);
                numbers.Show(new WorldEventSnapshot(WorldEventKind.EnemyHit, 8, 2, 3, 1));
                numbers.ResetSession();
                Assert.That(numbers.ActiveCount, Is.Zero);
                Assert.That(numbers.DisplayedCount, Is.Zero);
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(prefab); }
        }

        [Test]
        public void ShieldStateFollowsActualEnergyAndRecoversAfterBreak()
        {
            var root = new GameObject("护盾显示测试");
            var visual = new GameObject("持续护罩");
            var bar = new GameObject("护盾条", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
            try
            {
                visual.transform.SetParent(root.transform); bar.transform.SetParent(root.transform);
                var shield = root.AddComponent<ShieldPresentation>();
                shield.Configure(visual, bar.GetComponent<CanvasGroup>(), bar.GetComponent<Image>());
                shield.Apply(0, 1000, true);
                Assert.That(visual.activeSelf, Is.False);
                shield.Apply(500, 1000, true);
                Assert.That(visual.activeSelf, Is.True);
                Assert.That(bar.GetComponent<Image>().fillAmount, Is.EqualTo(.5f));
                shield.Apply(0, 1000, true);
                shield.Apply(200, 1000, true);
                Assert.That(visual.activeSelf, Is.True);
                Assert.That(bar.GetComponent<Image>().fillAmount, Is.EqualTo(.2f));
                shield.Apply(200, 1000, false);
                Assert.That(visual.activeSelf, Is.False);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void HitReactionNeverMovesWorldRootAndResetsOnReuse()
        {
            var root = new GameObject("敌人位置根节点");
            var model = new GameObject("受击视觉节点");
            try
            {
                root.transform.position = new Vector3(4, 0, 6);
                model.transform.SetParent(root.transform, false);
                var reaction = root.AddComponent<HitReactionView>();
                reaction.Configure(model.transform, .06f, .04f);
                reaction.Play(Vector3.forward);
                reaction.AdvanceFeedback(.04f);
                Assert.That(model.transform.localPosition.z, Is.GreaterThan(0));
                Assert.That(root.transform.position, Is.EqualTo(new Vector3(4, 0, 6)));
                reaction.AdvanceFeedback(0);
                reaction.ResetPose();
                Assert.That(model.transform.localPosition, Is.EqualTo(Vector3.zero));
                Assert.That(model.transform.localScale, Is.EqualTo(Vector3.one));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void ContinuousDamageDoesNotRestartWhiteBarDelay()
        {
            var state = new HealthBarState();
            state.SetHealth(100, 100, .15f);
            Assert.That(state.Visible, Is.False);
            state.SetHealth(80, 100, .15f);
            Assert.That(state.Health, Is.EqualTo(.8f));
            Assert.That(state.Trail, Is.EqualTo(1));
            state.Advance(.1f, .3f, .15f);
            state.SetHealth(60, 100, .15f);
            state.Advance(.1f, .3f, .15f);
            Assert.That(state.Trail, Is.LessThan(1));
            state.Advance(.3f, .3f, .15f);
            Assert.That(state.Trail, Is.EqualTo(.6f));
            Assert.That(state.Visible, Is.True);
        }

        [Test]
        public void RepairPauseAndReuseClearPreviousHealthFeedback()
        {
            var state = new HealthBarState();
            state.SetHealth(100, 100, .15f);
            state.SetHealth(25, 100, .15f);
            state.Advance(0, .3f, .15f);
            Assert.That(state.Trail, Is.EqualTo(1));
            state.SetHealth(75, 100, .15f);
            Assert.That(state.Health, Is.EqualTo(.75f));
            Assert.That(state.Trail, Is.EqualTo(.75f));
            state.SetHealth(100, 100, .15f);
            state.Advance(.2f, .3f, .15f);
            Assert.That(state.Visible, Is.False);
            state.Reset();
            state.SetHealth(40, 80, .15f);
            Assert.That(state.Trail, Is.EqualTo(.5f));
            Assert.That(state.Visible, Is.True);
            state.SetHealth(0, 80, .15f);
            Assert.That(state.Visible, Is.False);
        }

        [Test]
        public void DisplayQueueRetainsEveryHitAndDeduplicatesRepeatedRefresh()
        {
            var queue = new CombatDisplayQueue();
            var received = new List<WorldEventSnapshot>();
            var events = new[]
            {
                new WorldEventSnapshot(WorldEventKind.EnemyHit, 9, 2, 3, 10, eventId: 1, tick: 4, time: .16),
                new WorldEventSnapshot(WorldEventKind.EnemyHit, 9, 2, 3, 20, eventId: 2, tick: 4, time: .18),
                new WorldEventSnapshot(WorldEventKind.EnemyDeath, 9, 2, 3, eventId: 3, tick: 4, time: .18)
            };
            queue.Observe(events, null);
            queue.Observe(events, null);
            queue.Drain(.15, received.Add, _ => { });
            Assert.That(received, Is.Empty);
            queue.Drain(.2, received.Add, _ => { });
            Assert.That(received.Count, Is.EqualTo(3));
            Assert.That(received[0].Amount, Is.EqualTo(10));
            Assert.That(received[1].Amount, Is.EqualTo(20));
            queue.Observe(events, null);
            queue.Drain(.3, received.Add, _ => { });
            Assert.That(received.Count, Is.EqualTo(3));
        }

        [Test]
        public void SameTickCommandAndNewSessionAreNotDiscarded()
        {
            var queue = new CombatDisplayQueue();
            var received = new List<WorldEventSnapshot>();
            var first = new WorldEventSnapshot(WorldEventKind.BuildingHit, 1, 0, 0, 10, eventId: 1, tick: 3, time: .15);
            queue.Observe(new[] { first }, null);
            queue.Drain(.15, received.Add, _ => { });
            var repair = new WorldEventSnapshot(WorldEventKind.Repair, 1, 0, 0, 10, eventId: 2, tick: 3, time: .15);
            queue.Observe(new[] { first, repair }, null);
            queue.Drain(.15, received.Add, _ => { });
            Assert.That(received.Count, Is.EqualTo(2));
            queue.Clear();
            queue.Observe(new[] { first }, null);
            queue.Drain(.15, received.Add, _ => { });
            Assert.That(received.Count, Is.EqualTo(3));
            Assert.That(queue.PendingResults, Is.Zero);
        }
    }
}
