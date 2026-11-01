using System;
using UnityEngine;

namespace Before.Production
{
    /// <summary>
    /// Owns the post-fork branch layout and progress only. Input recognition,
    /// obstacle judgement, and presentation remain in their existing components.
    /// Stable IDs and pass counts are preserved while beats 23-27 are profiled.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProductionBranchFlow : MonoBehaviour
    {
        public const int FirstBranchBeatId = 23;
        public const int LastBranchBeatId = 27;
        public const int BranchDemandCount = 12;
        public const float ForkToBranchDistance = ProductionCourseDefinition.EarlyBeatSpacing;
        public const float LeftDemandSpacing = 84f;
        public const float LeftBeatSpacing = 120f;
        public const float RightDemandSpacing = 60f;
        public const float RightBeatSpacing = 96f;
        public const float CoreApproachDistance = ProductionCourseDefinition.LateBeatSpacing +
                                                  ProductionCourseDefinition.FinalSafeRunDistance;

        private static readonly int[] DemandsPerBeat = { 2, 2, 2, 3, 3 };

        private static readonly ProductionObstacleKind[] LeftKinds =
        {
            ProductionObstacleKind.Crouch, ProductionObstacleKind.Stop,
            ProductionObstacleKind.Crouch, ProductionObstacleKind.Jump,
            ProductionObstacleKind.Stop, ProductionObstacleKind.Crouch,
            ProductionObstacleKind.Crouch, ProductionObstacleKind.Jump,
            ProductionObstacleKind.Stop, ProductionObstacleKind.Jump,
            ProductionObstacleKind.Stop, ProductionObstacleKind.Crouch
        };

        private static readonly ProductionObstacleKind[] RightKinds =
        {
            ProductionObstacleKind.Jump, ProductionObstacleKind.Stop,
            ProductionObstacleKind.Stop, ProductionObstacleKind.Jump,
            ProductionObstacleKind.Jump, ProductionObstacleKind.Crouch,
            ProductionObstacleKind.Jump, ProductionObstacleKind.Stop,
            ProductionObstacleKind.Crouch, ProductionObstacleKind.Stop,
            ProductionObstacleKind.Crouch, ProductionObstacleKind.Jump
        };

        [SerializeField] private ProductionGameLoop gameLoop;
        [SerializeField] private ProductionCourseLoop course;
        [SerializeField] private NulProductionController controller;

        private bool _completionRaised;

        public event Action<ProductionRoute> OnBranchStarted;
        public event Action<ProductionRoute> OnBranchCompleted;

        public ProductionGameLoop GameLoop { get => gameLoop; set => gameLoop = value; }
        public ProductionCourseLoop Course { get => course; set => course = value; }
        public NulProductionController Controller { get => controller; set => controller = value; }
        public ProductionRoute ActiveRoute { get; private set; }
        public bool BranchStarted => ActiveRoute != ProductionRoute.None;
        public bool BranchCompleted { get; private set; }
        public float BranchStartWorldX { get; private set; }
        public float CoreApproachWorldX { get; private set; }
        public float BranchProgress01 => !BranchStarted || controller == null
            ? 0f
            : Mathf.InverseLerp(BranchStartWorldX, CoreApproachWorldX,
                controller.transform.position.x);

        private void Awake()
        {
            if (gameLoop == null) gameLoop = GetComponent<ProductionGameLoop>();
            if (course == null) course = GetComponent<ProductionCourseLoop>();
            if (controller == null) controller = GetComponent<NulProductionController>();
        }

        private void OnEnable()
        {
            if (gameLoop == null) gameLoop = GetComponent<ProductionGameLoop>();
            if (gameLoop != null) gameLoop.OnRouteSelected += HandleRouteSelected;
        }

        private void OnDisable()
        {
            if (gameLoop != null) gameLoop.OnRouteSelected -= HandleRouteSelected;
        }

        private void Update()
        {
            if (!BranchStarted || BranchCompleted || controller == null) return;
            if (controller.transform.position.x < CoreApproachWorldX) return;
            BranchCompleted = true;
            if (!_completionRaised)
            {
                _completionRaised = true;
                OnBranchCompleted?.Invoke(ActiveRoute);
            }
        }

        public static ProductionObstacleKind KindAt(ProductionRoute route, int branchIndex)
        {
            if (branchIndex < 0 || branchIndex >= BranchDemandCount)
                throw new ArgumentOutOfRangeException(nameof(branchIndex));
            if (route == ProductionRoute.Left) return LeftKinds[branchIndex];
            if (route == ProductionRoute.Right) return RightKinds[branchIndex];
            throw new ArgumentOutOfRangeException(nameof(route));
        }

        public static float DemandSpacingFor(ProductionRoute route)
            => route == ProductionRoute.Left ? LeftDemandSpacing
                : route == ProductionRoute.Right ? RightDemandSpacing : 0f;

        public static float BeatSpacingFor(ProductionRoute route)
            => route == ProductionRoute.Left ? LeftBeatSpacing
                : route == ProductionRoute.Right ? RightBeatSpacing : 0f;

        public bool ApplyRoute(ProductionRoute route)
        {
            if (route == ProductionRoute.None)
            {
                ClearBranchState();
                return true;
            }
            if (course == null || course.Slots == null) return false;

            ProductionCourseLoop.ObstacleSlot[] slots = course.Slots;
            var branchIndices = new int[BranchDemandCount];
            int count = 0;
            float forkX = 0f;
            bool foundFork = false;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].BeatId == ProductionForkFlow.ForkBeatId)
                {
                    forkX = slots[i].WorldX;
                    foundFork = true;
                }
                if (slots[i].BeatId < FirstBranchBeatId ||
                    slots[i].BeatId > LastBranchBeatId) continue;
                if (count >= branchIndices.Length) return false;
                branchIndices[count++] = i;
            }
            if (!foundFork || count != BranchDemandCount) return false;

            float demandSpacing = DemandSpacingFor(route);
            float beatSpacing = BeatSpacingFor(route);
            float worldX = forkX + ForkToBranchDistance;
            int branchIndex = 0;
            float lastWorldX = worldX;
            for (int beatOffset = 0; beatOffset < DemandsPerBeat.Length; beatOffset++)
            {
                int demandCount = DemandsPerBeat[beatOffset];
                for (int demand = 0; demand < demandCount; demand++)
                {
                    int slotIndex = branchIndices[branchIndex];
                    ProductionCourseLoop.ObstacleSlot slot = slots[slotIndex];
                    slot.Kind = KindAt(route, branchIndex);
                    slot.WorldX = worldX;
                    slots[slotIndex] = slot;
                    lastWorldX = worldX;
                    branchIndex++;
                    if (demand < demandCount - 1) worldX += demandSpacing;
                }
                if (beatOffset < DemandsPerBeat.Length - 1) worldX += beatSpacing;
            }

            ActiveRoute = route;
            BranchCompleted = false;
            _completionRaised = false;
            BranchStartWorldX = forkX;
            CoreApproachWorldX = lastWorldX + CoreApproachDistance;
            OnBranchStarted?.Invoke(route);
            return true;
        }

        private void HandleRouteSelected(ProductionRoute route)
        {
            ApplyRoute(route);
        }

        private void ClearBranchState()
        {
            ActiveRoute = ProductionRoute.None;
            BranchCompleted = false;
            _completionRaised = false;
            BranchStartWorldX = 0f;
            CoreApproachWorldX = 0f;
        }
    }
}
