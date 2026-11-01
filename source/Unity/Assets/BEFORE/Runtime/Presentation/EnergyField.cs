using UnityEngine;

namespace Before.Presentation
{
    /// <summary>
    /// D-093 energy particle system as a fixed pre-allocated pool. Pure and deterministic: no per-frame
    /// allocation, no LINQ, no Instantiate/Destroy. The presenter calls Update once per frame then draws the
    /// live particles. Rates, caps, travel, alpha, base-curve alpha, loss fade and reconnect ramp are all
    /// unit-testable without a camera or Play Mode.
    /// </summary>
    public sealed class EnergyField
    {
        public struct Particle
        {
            public bool Alive;
            public double SpawnTime;
            public float Offset;   // signed logical-px offset onto a near-curve within +/- spread
            public bool Big;       // 2 logical px instead of 1
        }

        private readonly Particle[] pool = new Particle[BeforeLayout.EnergyPoolSize];
        private double spawnAccum;
        private int spawnIndex;      // round-robin curve-offset + big cadence
        private double disconnectAt = -1;
        private double reconnectAt = -1;
        private bool wasConnected;

        public long TotalSpawned { get; private set; }

        public Particle[] Pool => pool;

        public int AliveCount
        {
            get { int n = 0; for (int i = 0; i < pool.Length; i++) if (pool[i].Alive) n++; return n; }
        }

        /// <summary>0 while connected; 0..1 fading over EnergyFadeSeconds after the hand is lost.</summary>
        public float Fade(double now)
        {
            if (disconnectAt < 0) return 1f;
            float f = 1f - (float)((now - disconnectAt) / BeforeLayout.EnergyFadeSeconds);
            return f < 0f ? 0f : f;
        }

        public void Reset()
        {
            for (int i = 0; i < pool.Length; i++) pool[i].Alive = false;
            spawnAccum = 0; spawnIndex = 0; disconnectAt = -1; reconnectAt = -1; wasConnected = false;
            TotalSpawned = 0;
        }

        /// <summary>
        /// Advance the pool. <paramref name="connected"/> gates spawning; <paramref name="active"/> selects the
        /// teach/armed/tracking rate+cap. On reconnection the spawn rate ramps up over EnergyReconnectRampSeconds.
        /// </summary>
        public void Update(double now, float dt, bool connected, bool active)
        {
            // retire finished particles (travel time elapsed) or all particles once the fade completes
            float fade = Fade(now);
            for (int i = 0; i < pool.Length; i++)
            {
                if (!pool[i].Alive) continue;
                if (now - pool[i].SpawnTime >= BeforeLayout.EnergyTravelSeconds) pool[i].Alive = false;
            }
            if (!connected && fade <= 0f)
            {
                for (int i = 0; i < pool.Length; i++) pool[i].Alive = false;
            }

            // connection edges
            if (connected && !wasConnected) { reconnectAt = now; disconnectAt = -1; }
            if (!connected && wasConnected) disconnectAt = now;
            if (connected) disconnectAt = -1;
            wasConnected = connected;

            if (!connected) { spawnAccum = 0; return; } // no new spawns while lost (existing ones fade out)

            float ramp = reconnectAt < 0 ? 1f
                : Mathf.Clamp01((float)((now - reconnectAt) / BeforeLayout.EnergyReconnectRampSeconds));
            float rate = (active ? BeforeLayout.EnergyActiveRate : BeforeLayout.EnergyNormalRate) * ramp;
            int cap = active ? BeforeLayout.EnergyActiveCap : BeforeLayout.EnergyNormalCap;

            spawnAccum += dt * rate;
            int guard = 0;
            while (spawnAccum >= 1.0 && guard++ < BeforeLayout.EnergyPoolSize)
            {
                spawnAccum -= 1.0;
                if (AliveCount >= cap) { spawnAccum = 0; break; }
                Spawn(now);
            }
        }

        private void Spawn(double now)
        {
            int slot = -1;
            for (int i = 0; i < pool.Length; i++) if (!pool[i].Alive) { slot = i; break; }
            if (slot < 0) return;
            // five near-curves across +/- spread; every 6th particle is a 2 px "big" one
            int lane = spawnIndex % 5;                       // 0..4
            float t = lane / 4f;                             // 0..1
            float offset = (t * 2f - 1f) * BeforeLayout.EnergyCurveSpreadLogical;
            pool[slot] = new Particle
            {
                Alive = true,
                SpawnTime = now,
                Offset = offset,
                Big = (spawnIndex % BeforeLayout.EnergyBigEvery) == 0
            };
            spawnIndex++;
            TotalSpawned++;
        }
    }
}
