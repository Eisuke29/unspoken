using NUnit.Framework;

namespace Before.Production.Tests
{
    public class ProductionSlice12Tests
    {
        [Test]
        public void PausedNulStateConsumesCommandWithoutApplyingOrQueuingIt()
        {
            var state = new NulState { Paused = true };
            var command = new CommandPacket { Kind = CommandKind.Jump };
            state.Apply(command, 1.0);
            Assert.AreEqual(0, state.JumpCount);
            state.Paused = false;
            state.Tick(10.0);
            Assert.AreEqual(0, state.JumpCount);
        }
    }
}
