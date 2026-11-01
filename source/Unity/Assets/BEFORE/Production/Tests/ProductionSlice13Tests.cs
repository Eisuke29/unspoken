using NUnit.Framework;
using UnityEngine;

namespace Before.Production.Tests
{
    public class ProductionSlice13Tests
    {
        [Test]
        public void ControllerExposesNoLandmarkUntilSkeletonPacketArrives()
        {
            var root = new GameObject("slice13-edit");
            root.AddComponent<Rigidbody2D>();
            var controller = root.AddComponent<NulProductionController>();
            Assert.IsFalse(controller.HasLandmarkFrame);
            Assert.IsFalse(controller.TryGetLandmark(0, out _));
            Object.DestroyImmediate(root);
        }
    }
}
