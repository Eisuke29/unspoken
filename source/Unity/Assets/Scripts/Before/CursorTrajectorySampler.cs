using UnityEngine;
using UnityEngine.InputSystem;

namespace BeforeExperience
{
    public sealed class CursorTrajectorySampler : MonoBehaviour
    {
        private const int Capacity = 128;
        private const double WindowSeconds = 0.5;
        private const double MinimumSampleStep = 1.0 / 500.0;

        private struct Sample
        {
            public Vector2 Position;
            public double Time;
        }

        private readonly Sample[] samples = new Sample[Capacity];
        private int start;
        private int count;

        public CursorMotionState Motion { get; private set; }

        public void Tick()
        {
            Mouse mouse = Mouse.current;
            int width = Mathf.Max(1, Screen.width);
            int height = Mathf.Max(1, Screen.height);

            if (mouse == null)
            {
                Motion = CreateInactiveMotion();
                return;
            }

            Vector2 pixel = mouse.position.ReadValue();
            bool inside = pixel.x >= 0f && pixel.x <= width && pixel.y >= 0f && pixel.y <= height;
            if (!inside)
            {
                Motion = CreateInactiveMotion();
                return;
            }

            Vector2 normalized = new Vector2(
                Mathf.Clamp01(pixel.x / width),
                Mathf.Clamp01(pixel.y / height));
            double now = Time.unscaledTimeAsDouble;

            if (count == 0 || now - GetSample(count - 1).Time >= MinimumSampleStep)
            {
                AddSample(new Sample { Position = normalized, Time = now });
            }

            while (count > 2 && now - GetSample(0).Time > WindowSeconds)
            {
                start = (start + 1) % Capacity;
                count--;
            }

            Motion = CalculateMotion(inside);
        }

        public void ResetSampler()
        {
            start = 0;
            count = 0;
            Motion = CreateInactiveMotion();
        }

        private CursorMotionState CalculateMotion(bool inside)
        {
            CursorMotionState result = new CursorMotionState
            {
                IsInsideScreen = inside,
                DirectionStability = 0f,
                Curvature = 0f,
                Hesitation = 1f
            };

            if (count == 0)
            {
                return result;
            }

            Sample latest = GetSample(count - 1);
            result.Position = latest.Position;
            if (count < 2)
            {
                return result;
            }

            int recentIndex = FindIndexBefore(latest.Time - 0.08);
            Sample recent = GetSample(recentIndex);
            float recentDt = SafeDelta(latest.Time - recent.Time);
            Vector2 recentVelocity = (latest.Position - recent.Position) / recentDt;

            int previousIndex = FindIndexBefore(recent.Time - 0.08);
            Sample previous = GetSample(previousIndex);
            float previousDt = SafeDelta(recent.Time - previous.Time);
            Vector2 previousVelocity = (recent.Position - previous.Position) / previousDt;
            float accelerationDt = Mathf.Max(0.001f, (recentDt + previousDt) * 0.5f);

            result.Velocity = Sanitize(recentVelocity, 8f);
            result.Speed = Mathf.Clamp(result.Velocity.magnitude, 0f, 8f);
            result.Direction = result.Speed > 0.0001f ? result.Velocity / result.Speed : Vector2.zero;
            result.Acceleration = Sanitize((recentVelocity - previousVelocity) / accelerationDt, 40f);

            Vector2 directionSum = Vector2.zero;
            int directionCount = 0;
            float totalTurn = 0f;
            Vector2 lastDirection = Vector2.zero;
            bool hasLastDirection = false;

            for (int i = 1; i < count; i++)
            {
                Sample a = GetSample(i - 1);
                Sample b = GetSample(i);
                if (latest.Time - b.Time > 0.25)
                {
                    continue;
                }

                Vector2 segment = b.Position - a.Position;
                if (segment.sqrMagnitude < 0.00000001f)
                {
                    continue;
                }

                Vector2 direction = segment.normalized;
                directionSum += direction;
                directionCount++;
                if (hasLastDirection)
                {
                    totalTurn += Mathf.Acos(Mathf.Clamp(Vector2.Dot(lastDirection, direction), -1f, 1f));
                }

                lastDirection = direction;
                hasLastDirection = true;
            }

            if (directionCount > 0 && result.Direction.sqrMagnitude > 0f)
            {
                Vector2 averageDirection = directionSum / directionCount;
                result.DirectionStability = Mathf.Clamp01(averageDirection.magnitude)
                    * Mathf.Clamp01((Vector2.Dot(averageDirection.normalized, result.Direction) + 1f) * 0.5f);
            }

            result.Curvature = Mathf.Clamp01(totalTurn / (Mathf.PI * 2f));
            float stopAmount = 1f - Mathf.Clamp01(result.Speed / 0.35f);
            result.Hesitation = Mathf.Clamp01(stopAmount * 0.7f + (1f - result.DirectionStability) * 0.2f + result.Curvature * 0.1f);
            return result;
        }

        private int FindIndexBefore(double time)
        {
            int found = 0;
            for (int i = count - 1; i >= 0; i--)
            {
                if (GetSample(i).Time <= time)
                {
                    found = i;
                    break;
                }
            }

            return found;
        }

        private void AddSample(Sample sample)
        {
            if (count == Capacity)
            {
                start = (start + 1) % Capacity;
                count--;
            }

            samples[(start + count) % Capacity] = sample;
            count++;
        }

        private Sample GetSample(int logicalIndex)
        {
            return samples[(start + Mathf.Clamp(logicalIndex, 0, count - 1)) % Capacity];
        }

        private static float SafeDelta(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return 0.001f;
            }

            return Mathf.Max(0.001f, (float)value);
        }

        private static Vector2 Sanitize(Vector2 value, float limit)
        {
            if (!float.IsFinite(value.x) || !float.IsFinite(value.y))
            {
                return Vector2.zero;
            }

            return new Vector2(Mathf.Clamp(value.x, -limit, limit), Mathf.Clamp(value.y, -limit, limit));
        }

        private static CursorMotionState CreateInactiveMotion()
        {
            return new CursorMotionState
            {
                IsInsideScreen = false,
                Hesitation = 1f
            };
        }
    }
}

