using System.Reflection;
using NUnit.Framework;

namespace Before.Production.Tests
{
    public class ProductionSlice33Tests
    {
        [Test]
        public void ControllerTransportOwnersRemainReadonlyAcrossGameplayRestart()
        {
            FieldInfo router = typeof(NulProductionController).GetField("Router");
            FieldInfo bus = typeof(NulProductionController).GetField("Bus");
            FieldInfo nul = typeof(NulProductionController).GetField("Nul");

            Assert.IsNotNull(router);
            Assert.IsNotNull(bus);
            Assert.IsNotNull(nul);
            Assert.IsTrue(router.IsInitOnly);
            Assert.IsTrue(bus.IsInitOnly);
            Assert.IsTrue(nul.IsInitOnly);
            Assert.AreEqual(typeof(CommandRouter), router.FieldType);
            Assert.AreEqual(typeof(CommandBus), bus.FieldType);
            Assert.AreEqual(typeof(NulState), nul.FieldType);
        }
    }
}
