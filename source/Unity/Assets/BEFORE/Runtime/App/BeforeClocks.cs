namespace Before.App
{
    /// <summary>Unscaled monotonic clock for camera, inference, UI, recovery, and diagnostics.</summary>
    public interface IRealtimeClock
    {
        double Now { get; }
    }

    /// <summary>Advances only while gameplay is not paused/suspended. Owns NUL/hazard/story action timing.</summary>
    public interface IGameplayClock
    {
        double Now { get; }
        bool Paused { get; }
    }

    public sealed class RealtimeClock : IRealtimeClock
    {
        public double Now { get; private set; }
        public void Advance(double now) { Now = now; }
    }

    public sealed class GameplayClock : IGameplayClock
    {
        public double Now { get; private set; }
        public bool Paused { get; private set; }
        private int suspendCount;

        public void Tick(double deltaSeconds)
        {
            if (!Paused && suspendCount == 0 && deltaSeconds > 0)
            {
                Now += deltaSeconds;
            }
        }

        public void SetOperatorPause(bool paused) { Paused = paused; }
        public void PushSuspend() { suspendCount++; }
        public void PopSuspend() { if (suspendCount > 0) suspendCount--; }
        public bool Suspended => suspendCount > 0;
        public void ResetTo(double now) { Now = now; suspendCount = 0; Paused = false; }
    }
}
