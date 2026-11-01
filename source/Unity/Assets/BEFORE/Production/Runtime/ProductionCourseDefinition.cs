using System;
using System.Collections.Generic;
using UnityEngine;

namespace Before.Production
{
    [CreateAssetMenu(fileName = "ProductionCourseV2", menuName = "BEFORE/Production Course V2")]
    public sealed class ProductionCourseDefinition : ScriptableObject
    {
        public const int RequiredBeatCount = 28;
        public const float EarlyBeatSpacing = 216f;
        public const float AlternatingDemandSpacing = 120f;
        public const float AlternatingBeatSpacing = 192f;
        public const float LateDemandSpacing = 72f;
        public const float LateBeatSpacing = 144f;
        public const float FinalSafeRunDistance = 5f * NulProductionController.RunSpeed;

        // The judge resolves a slot when NUL's centre reaches slot.WorldX. At that
        // instant the occupied horizontal contact span is the existing standing
        // collider width. Presenters use this helper so their boundary markers and
        // hazard art cannot drift away from the same course/judge coordinate.
        public const float ObstacleJudgementWidth = NulProductionController.StandColliderW;

        public static Vector2 ObstacleJudgementBounds(float worldX)
        {
            float halfWidth = ObstacleJudgementWidth * 0.5f;
            return new Vector2(worldX - halfWidth, worldX + halfWidth);
        }

        [Serializable]
        public struct Beat
        {
            public int Id;
            public ProductionObstacleKind[] Demands;
            public float DemandSpacing;
            public float DistanceToNextBeat;
            public bool TeachingBeat;
            public float SafeRunAfterBeat;
        }

        [SerializeField] private Beat[] beats = Array.Empty<Beat>();

        public IReadOnlyList<Beat> Beats => beats;

        public void LoadProductionDefaults() => beats = BuildProductionDefaults();

        public static Beat[] BuildProductionDefaults()
        {
            var result = new Beat[RequiredBeatCount];

            Set(result, 1, EarlyBeatSpacing, 0f, true, ProductionObstacleKind.Jump);
            Set(result, 2, EarlyBeatSpacing, 0f, false, ProductionObstacleKind.Jump);
            Set(result, 3, EarlyBeatSpacing, 0f, false, ProductionObstacleKind.Jump);
            Set(result, 4, EarlyBeatSpacing, 0f, true, ProductionObstacleKind.Crouch);
            Set(result, 5, EarlyBeatSpacing, 0f, false, ProductionObstacleKind.Crouch);
            Set(result, 6, EarlyBeatSpacing, 0f, false, ProductionObstacleKind.Crouch);

            Set(result, 7, AlternatingBeatSpacing, AlternatingDemandSpacing, false,
                ProductionObstacleKind.Jump, ProductionObstacleKind.Crouch);
            Set(result, 8, AlternatingBeatSpacing, AlternatingDemandSpacing, false,
                ProductionObstacleKind.Crouch, ProductionObstacleKind.Jump);
            Set(result, 9, AlternatingBeatSpacing, AlternatingDemandSpacing, false,
                ProductionObstacleKind.Jump, ProductionObstacleKind.Crouch);
            Set(result, 10, AlternatingBeatSpacing, AlternatingDemandSpacing, false,
                ProductionObstacleKind.Crouch, ProductionObstacleKind.Jump);
            Set(result, 11, AlternatingBeatSpacing, AlternatingDemandSpacing, false,
                ProductionObstacleKind.Jump, ProductionObstacleKind.Crouch);
            Set(result, 12, AlternatingBeatSpacing, AlternatingDemandSpacing, false,
                ProductionObstacleKind.Crouch, ProductionObstacleKind.Jump);

            Set(result, 13, EarlyBeatSpacing, 0f, true, ProductionObstacleKind.Stop);
            Set(result, 14, EarlyBeatSpacing, 0f, false, ProductionObstacleKind.Stop);
            Set(result, 15, EarlyBeatSpacing, 0f, false, ProductionObstacleKind.Stop);

            Set(result, 16, LateBeatSpacing, LateDemandSpacing, false,
                ProductionObstacleKind.Jump, ProductionObstacleKind.Crouch, ProductionObstacleKind.Stop);
            Set(result, 17, LateBeatSpacing, LateDemandSpacing, false,
                ProductionObstacleKind.Stop, ProductionObstacleKind.Jump, ProductionObstacleKind.Crouch);
            Set(result, 18, LateBeatSpacing, LateDemandSpacing, false,
                ProductionObstacleKind.Crouch, ProductionObstacleKind.Stop, ProductionObstacleKind.Jump);
            Set(result, 19, LateBeatSpacing, LateDemandSpacing, false,
                ProductionObstacleKind.Jump, ProductionObstacleKind.Stop, ProductionObstacleKind.Crouch);
            Set(result, 20, LateBeatSpacing, LateDemandSpacing, false,
                ProductionObstacleKind.Crouch, ProductionObstacleKind.Jump, ProductionObstacleKind.Stop);
            Set(result, 21, LateBeatSpacing, LateDemandSpacing, false,
                ProductionObstacleKind.Stop, ProductionObstacleKind.Crouch, ProductionObstacleKind.Jump);

            Set(result, 22, EarlyBeatSpacing, 0f, false, ProductionObstacleKind.Direction);
            Set(result, 23, LateBeatSpacing, LateDemandSpacing, false,
                ProductionObstacleKind.Jump, ProductionObstacleKind.Stop);
            Set(result, 24, LateBeatSpacing, LateDemandSpacing, false,
                ProductionObstacleKind.Crouch, ProductionObstacleKind.Jump);
            Set(result, 25, LateBeatSpacing, LateDemandSpacing, false,
                ProductionObstacleKind.Stop, ProductionObstacleKind.Crouch);
            Set(result, 26, LateBeatSpacing, LateDemandSpacing, false,
                ProductionObstacleKind.Jump, ProductionObstacleKind.Crouch, ProductionObstacleKind.Stop);
            Set(result, 27, LateBeatSpacing, LateDemandSpacing, false,
                ProductionObstacleKind.Crouch, ProductionObstacleKind.Stop, ProductionObstacleKind.Jump);
            Set(result, 28, 0f, 0f, false);
            Beat final = result[27];
            final.SafeRunAfterBeat = FinalSafeRunDistance;
            result[27] = final;

            return result;
        }

        public static List<string> Validate(IReadOnlyList<Beat> candidate)
        {
            var errors = new List<string>();
            if (candidate == null || candidate.Count != RequiredBeatCount)
            {
                errors.Add("course must contain exactly 28 beats");
                return errors;
            }

            for (int i = 0; i < candidate.Count; i++)
            {
                Beat beat = candidate[i];
                if (beat.Id != i + 1) errors.Add("beat IDs must be sequential at index " + i);
                if (beat.Demands == null) errors.Add("beat " + beat.Id + " has null demands");
                if (beat.Demands != null && beat.Demands.Length > 1)
                {
                    float minimum = ContainsStop(beat.Demands) ? 48f : 36f;
                    if (beat.DemandSpacing < minimum)
                        errors.Add("beat " + beat.Id + " demand spacing is below " + minimum);
                }
                if (beat.Id < RequiredBeatCount && beat.DistanceToNextBeat < 72f)
                    errors.Add("beat " + beat.Id + " lacks the 72 px safe run-up");
            }

            if (candidate[21].Demands == null || candidate[21].Demands.Length != 1 ||
                candidate[21].Demands[0] != ProductionObstacleKind.Direction)
                errors.Add("beat 22 must be the Direction fork");
            if (candidate[27].Demands == null || candidate[27].Demands.Length != 0 ||
                candidate[27].SafeRunAfterBeat != FinalSafeRunDistance)
                errors.Add("beat 28 must be the five-second safe run marker");
            return errors;
        }

        private static void Set(Beat[] destination, int id, float nextBeat, float demandSpacing,
                                bool teaching, params ProductionObstacleKind[] demands)
        {
            destination[id - 1] = new Beat
            {
                Id = id,
                Demands = demands ?? Array.Empty<ProductionObstacleKind>(),
                DemandSpacing = demandSpacing,
                DistanceToNextBeat = nextBeat,
                TeachingBeat = teaching,
                SafeRunAfterBeat = 0f
            };
        }

        private static bool ContainsStop(ProductionObstacleKind[] demands)
        {
            for (int i = 0; i < demands.Length; i++)
                if (demands[i] == ProductionObstacleKind.Stop) return true;
            return false;
        }
    }
}
