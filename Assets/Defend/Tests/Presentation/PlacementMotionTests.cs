using Defend.Unity;
using NUnit.Framework;
using UnityEngine;

namespace Defend.Tests
{
    public sealed class PlacementMotionTests
    {
        [Test]
        public void FastPointerMovementStaysNearLandingCellAndSettles()
        {
            var motion = new PlacementMotion(); motion.Reset(Vector3.zero);
            var target = new Vector3(15, 0, 20);
            motion.Advance(target, 1f / 60);
            Assert.That(Vector3.Distance(motion.Position, target), Is.LessThanOrEqualTo(PlacementMotion.MaximumOffset + .001f));
            Assert.That(Quaternion.Angle(Quaternion.identity, motion.Rotation), Is.GreaterThan(0));
            for (int i = 0; i < 180; i++)
            {
                motion.Advance(target, 1f / 60);
                Assert.That(Quaternion.Angle(Quaternion.identity, motion.Rotation), Is.LessThanOrEqualTo(PlacementMotion.MaximumTilt + .1f));
            }
            Assert.That(Vector3.Distance(motion.Position, target), Is.LessThan(.001f));
            Assert.That(Quaternion.Angle(Quaternion.identity, motion.Rotation), Is.LessThan(.1f));
        }

        [Test]
        public void ReenteringPreviewClearsMotionAndZeroTimeDoesNotAdvance()
        {
            var motion = new PlacementMotion(); motion.Reset(Vector3.zero);
            motion.Advance(Vector3.right, .02f);
            motion.Reset(new Vector3(4, 0, 9));
            motion.Advance(Vector3.zero, 0);
            Assert.That(motion.Position, Is.EqualTo(new Vector3(4, 0, 9)));
            Assert.That(motion.Rotation, Is.EqualTo(Quaternion.identity));
        }
    }
}
