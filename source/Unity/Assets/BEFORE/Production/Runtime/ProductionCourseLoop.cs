using System;
using UnityEngine;

namespace Before.Production
{
    public enum ProductionObstacleKind
    {
        Jump,
        Crouch,
        Stop,
        Direction
    }

    public enum ProductionRoute
    {
        None,
        Left,
        Right
    }

    /// <summary>
    /// Continuous data-driven course state. It owns only course layout, recycling,
    /// route state, and energy-wall timing; presentation lives in ProductionCoursePresenter.
    /// Command interpretation remains in NulProductionController.
    /// Obstacles recycle ahead of NUL so the run has no stage or automatic ending.
    /// </summary>
    public sealed class ProductionCourseLoop : MonoBehaviour
    {
        public const int ObstacleCount = 4;
        public const float FirstObstacleX = 140f;
        public const float ObstacleSpacing = 120f;
        public const float CycleLength = ObstacleCount * ObstacleSpacing;
        public const float RecycleBehindDistance = 72f;
        public const float EnergyWallTelegraphSeconds = 1.2f;
        public const float EnergyWallClosedSeconds = 1.8f;
        public const float EnergyWallOpenSeconds = 2.2f;
        public const float EnergyWallPeriod = EnergyWallTelegraphSeconds +
                                              EnergyWallClosedSeconds +
                                              EnergyWallOpenSeconds;

        [Serializable]
        public struct ObstacleSlot
        {
            public int StableId;
            public int BeatId;
            public int DemandIndex;
            public ProductionObstacleKind Kind;
            public float WorldX;
            public int PassCount;
        }

        [SerializeField] private Transform runner;
        [SerializeField] private ProductionCourseDefinition definition;

        private ObstacleSlot[] _slots = new ObstacleSlot[ObstacleCount];
        private float _activeCycleLength = CycleLength;

        public Transform Runner
        {
            get => runner;
            set => runner = value;
        }

        public ProductionCourseDefinition Definition
        {
            get => definition;
            set
            {
                definition = value;
                ResetCourse(runner != null ? runner.position.x : transform.position.x);
            }
        }

        public ObstacleSlot[] Slots => _slots;
        public float ActiveCycleLength => _activeCycleLength;
        public ProductionRoute SelectedRoute { get; private set; }

        private void Awake()
        {
            if (runner == null) runner = transform;
            ResetCourse(runner != null ? runner.position.x : 0f);
        }

        private void OnEnable()
        {
            if (runner == null) runner = transform;
            if (_slots == null || _slots.Length == 0 || _slots[0].StableId == 0)
                ResetCourse(runner != null ? runner.position.x : 0f);
        }

        private void Update()
        {
            if (runner == null) return;
            RecyclePassedSlots(runner.position.x);
        }

        public void ResetCourse(float runnerWorldX)
        {
            SelectedRoute = ProductionRoute.None;
            if (definition != null &&
                ProductionCourseDefinition.Validate(definition.Beats).Count == 0)
            {
                BuildDefinitionCourse(runnerWorldX);
                return;
            }

            if (_slots == null || _slots.Length != ObstacleCount)
                _slots = new ObstacleSlot[ObstacleCount];
            for (int i = 0; i < ObstacleCount; i++)
            {
                _slots[i] = new ObstacleSlot
                {
                    StableId = i + 1,
                    BeatId = i + 1,
                    DemandIndex = 0,
                    Kind = (ProductionObstacleKind)i,
                    WorldX = runnerWorldX + FirstObstacleX + i * ObstacleSpacing,
                    PassCount = 0
                };
            }
            _activeCycleLength = CycleLength;
        }

        public static ProductionRoute RouteForDirection(int direction)
            => direction < 0 ? ProductionRoute.Left
                : direction > 0 ? ProductionRoute.Right
                : ProductionRoute.None;

        public void SetSelectedRoute(int direction)
        {
            SelectedRoute = RouteForDirection(direction);
        }

        private void BuildDefinitionCourse(float runnerWorldX)
        {
            int count = 0;
            for (int i = 0; i < definition.Beats.Count; i++)
                count += definition.Beats[i].Demands.Length;

            _slots = new ObstacleSlot[count];
            float x = runnerWorldX + FirstObstacleX;
            int slotIndex = 0;
            for (int i = 0; i < definition.Beats.Count; i++)
            {
                ProductionCourseDefinition.Beat beat = definition.Beats[i];
                for (int demand = 0; demand < beat.Demands.Length; demand++)
                {
                    _slots[slotIndex] = new ObstacleSlot
                    {
                        StableId = slotIndex + 1,
                        BeatId = beat.Id,
                        DemandIndex = demand,
                        Kind = beat.Demands[demand],
                        WorldX = x,
                        PassCount = 0
                    };
                    slotIndex++;
                    if (demand < beat.Demands.Length - 1) x += beat.DemandSpacing;
                }

                if (beat.Demands.Length > 0) x += beat.DistanceToNextBeat;
                x += beat.SafeRunAfterBeat;
            }
            _activeCycleLength = Mathf.Max(CycleLength, x - (runnerWorldX + FirstObstacleX));
        }

        public void RecyclePassedSlots(float runnerWorldX)
        {
            float cutoff = runnerWorldX - RecycleBehindDistance;
            for (int i = 0; i < _slots.Length; i++)
            {
                ObstacleSlot slot = _slots[i];
                while (slot.WorldX < cutoff)
                {
                    slot.WorldX += _activeCycleLength;
                    slot.PassCount++;
                }
                _slots[i] = slot;
            }
        }

        public static bool EnergyWallIsOpen(float elapsedSeconds)
        {
            float phase = Mathf.Repeat(elapsedSeconds, EnergyWallPeriod);
            return phase >= EnergyWallTelegraphSeconds + EnergyWallClosedSeconds;
        }

    }
}
