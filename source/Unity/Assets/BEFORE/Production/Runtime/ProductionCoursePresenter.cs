using UnityEngine;

namespace Before.Production
{
    /// <summary>
    /// Renders the course from read-only course/controller state. It owns no layout,
    /// recycling, obstacle judgement, command, collider, or route decisions.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProductionCoursePresenter : MonoBehaviour
    {
        public const int GroundRendererCount = 5;
        public const int VisualPartsPerObstacle = 6;

        private const int ObstacleDetailCount = VisualPartsPerObstacle - 1;
        private static readonly Color GroundNeutral = new Color(0.08f, 0.11f, 0.14f);
        private static readonly Color GroundLeft = new Color(0.06f, 0.18f, 0.2f);
        private static readonly Color GroundRight = new Color(0.18f, 0.08f, 0.18f);
        private static readonly Color StopClosed = new Color(0.62f, 0.35f, 0.9f, 1f);

        [SerializeField] private ProductionCourseLoop course;
        [SerializeField] private NulProductionController controller;
        [SerializeField] private bool drawTemporaryCourse = true;

        private Transform _visualRoot;
        private SpriteRenderer _groundRenderer;
        private SpriteRenderer[] _groundDetails;
        private SpriteRenderer[] _obstacleRenderers;
        private SpriteRenderer[][] _obstacleDetails;
        private Sprite _unitSprite;

        public ProductionCourseLoop Course
        {
            get => course;
            set => course = value;
        }

        public NulProductionController Controller
        {
            get => controller;
            set => controller = value;
        }

        public bool DrawTemporaryCourse
        {
            get => drawTemporaryCourse;
            set => drawTemporaryCourse = value;
        }

        public int ObstacleRendererCount => _obstacleRenderers == null
            ? 0 : _obstacleRenderers.Length;
        public int VisualRendererCount => _obstacleRenderers == null
            ? 0 : GroundRendererCount + _obstacleRenderers.Length * VisualPartsPerObstacle;
        public int VisualColliderCount => _visualRoot == null
            ? 0 : _visualRoot.GetComponentsInChildren<Collider2D>(true).Length;
        public Color GroundColor => _groundRenderer == null
            ? Color.clear : _groundRenderer.color;

        public Color ObstacleColorAt(int index)
        {
            if (_obstacleRenderers == null || index < 0 ||
                index >= _obstacleRenderers.Length) return Color.clear;
            return _obstacleRenderers[index].color;
        }

        private void Awake()
        {
            if (course == null) course = GetComponent<ProductionCourseLoop>();
            if (controller == null) controller = GetComponent<NulProductionController>();
        }

        private void Start()
        {
            EnsureVisuals();
            RefreshNow();
        }

        private void LateUpdate()
        {
            EnsureVisuals();
            RefreshNow();
        }

        public static Color GroundColorFor(ProductionRoute route)
        {
            switch (route)
            {
                case ProductionRoute.Left: return GroundLeft;
                case ProductionRoute.Right: return GroundRight;
                default: return GroundNeutral;
            }
        }

        public static Color ObstacleColorFor(ProductionObstacleKind kind,
            bool wallOpen = false)
        {
            switch (kind)
            {
                case ProductionObstacleKind.Jump: return new Color(0.9f, 0.38f, 0.25f);
                case ProductionObstacleKind.Crouch: return new Color(0.95f, 0.72f, 0.24f);
                case ProductionObstacleKind.Stop: return wallOpen ? Color.clear : StopClosed;
                case ProductionObstacleKind.Direction: return ProductionHudLayout.Violet;
                default: return new Color(0.25f, 0.78f, 0.72f);
            }
        }

        public static Color RouteAccentFor(ProductionRoute route)
        {
            switch (route)
            {
                case ProductionRoute.Left: return ProductionHudLayout.Cyan;
                case ProductionRoute.Right: return ProductionHudLayout.Magenta;
                default: return ProductionHudLayout.Ash;
            }
        }

        public void RefreshNow()
        {
            if (_groundRenderer == null || course == null || course.Runner == null) return;
            ProductionCourseLoop.ObstacleSlot[] slots = course.Slots;
            if (_obstacleRenderers == null || _obstacleRenderers.Length != slots.Length) return;

            float centerX = ProductionCameraFollow.CameraXFor(course.Runner.position.x);
            Color routeColor = GroundColorFor(course.SelectedRoute);
            Color routeAccent = RouteAccentFor(course.SelectedRoute);
            SetPart(_groundRenderer, new Vector3(centerX, -15f, 5f),
                new Vector3(640f, 30f, 1f), 0f, routeColor);
            SetPart(_groundDetails[0], new Vector3(centerX, 0f, 4f),
                new Vector3(640f, 2f, 1f), 0f, WithAlpha(routeAccent, 0.82f));
            SetPart(_groundDetails[1], new Vector3(centerX, -4f, 4f),
                new Vector3(640f, 1f, 1f), 0f, WithAlpha(routeAccent, 0.24f));
            SetPart(_groundDetails[2], new Vector3(centerX - 166f, -12f, 4f),
                new Vector3(250f, 1f, 1f), 0f,
                WithAlpha(ProductionHudLayout.MidGray, 0.7f));
            SetPart(_groundDetails[3], new Vector3(centerX + 166f, -12f, 4f),
                new Vector3(250f, 1f, 1f), 0f,
                WithAlpha(ProductionHudLayout.MidGray, 0.7f));

            float courseTime = controller != null ? controller.GameplayTime : Time.time;
            for (int i = 0; i < slots.Length; i++)
            {
                bool wallOpen = slots[i].Kind == ProductionObstacleKind.Stop &&
                                ProductionCourseLoop.EnergyWallIsOpen(courseTime);
                LayoutObstacle(slots[i], wallOpen, _obstacleRenderers[i],
                    _obstacleDetails[i]);
            }
        }

        private void EnsureVisuals()
        {
            if (!drawTemporaryCourse || course == null || course.Slots == null) return;
            if (_visualRoot != null && _obstacleRenderers != null &&
                _obstacleRenderers.Length == course.Slots.Length) return;

            DestroyVisuals();
            _unitSprite = Sprite.Create(Texture2D.whiteTexture,
                new Rect(0f, 0f, Texture2D.whiteTexture.width, Texture2D.whiteTexture.height),
                new Vector2(0.5f, 0.5f), Texture2D.whiteTexture.width);

            var root = new GameObject("ProductionCourseVisuals");
            root.transform.SetParent(transform, false);
            _visualRoot = root.transform;
            _groundRenderer = CreateRenderer("RoadBed", -7, GroundNeutral);
            _groundDetails = new SpriteRenderer[GroundRendererCount - 1];
            for (int i = 0; i < _groundDetails.Length; i++)
                _groundDetails[i] = CreateRenderer("RoadDetail_" + i, -6,
                    ProductionHudLayout.MidGray);

            _obstacleRenderers = new SpriteRenderer[course.Slots.Length];
            _obstacleDetails = new SpriteRenderer[course.Slots.Length][];
            for (int i = 0; i < course.Slots.Length; i++)
            {
                ProductionCourseLoop.ObstacleSlot slot = course.Slots[i];
                _obstacleRenderers[i] = CreateRenderer(
                    "Hazard_" + slot.StableId + "_" + slot.Kind,
                    6, ObstacleColorFor(slot.Kind));
                _obstacleDetails[i] = new SpriteRenderer[ObstacleDetailCount];
                for (int detail = 0; detail < ObstacleDetailCount; detail++)
                    _obstacleDetails[i][detail] = CreateRenderer(
                        "Hazard_" + slot.StableId + "_Detail_" + detail,
                        detail < 3 ? 7 : 8, Color.clear);
            }
        }

        private SpriteRenderer CreateRenderer(string objectName, int sortingOrder,
            Color color)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(_visualRoot, false);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = _unitSprite;
            renderer.color = color;
            renderer.sortingOrder = sortingOrder;
            return renderer;
        }

        private static void LayoutObstacle(ProductionCourseLoop.ObstacleSlot slot,
            bool wallOpen, SpriteRenderer body, SpriteRenderer[] details)
        {
            for (int i = 0; i < details.Length; i++)
                SetPart(details[i], Vector3.zero, Vector3.zero, 0f, Color.clear);

            float x = slot.WorldX;
            Vector2 judgementBounds =
                ProductionCourseDefinition.ObstacleJudgementBounds(slot.WorldX);
            float left = judgementBounds.x;
            float right = judgementBounds.y;
            float span = right - left;
            const float boundaryWidth = 1f;
            float leftMarkerX = left + boundaryWidth * 0.5f;
            float rightMarkerX = right - boundaryWidth * 0.5f;
            Color semantic = ObstacleColorFor(slot.Kind, wallOpen);
            switch (slot.Kind)
            {
                case ProductionObstacleKind.Crouch:
                    SetPart(body, new Vector3(x, 28f, 0f), new Vector3(span, 7f, 1f),
                        0f, semantic);
                    SetPart(details[0], new Vector3(x, 32f, -0.1f),
                        new Vector3(span, 2f, 1f), 0f, ProductionHudLayout.Structural);
                    SetPart(details[1], new Vector3(leftMarkerX, 27f, -0.1f),
                        new Vector3(boundaryWidth, 12f, 1f), 0f,
                        ProductionHudLayout.DirtyWhite);
                    SetPart(details[2], new Vector3(rightMarkerX, 27f, -0.1f),
                        new Vector3(boundaryWidth, 12f, 1f), 0f,
                        ProductionHudLayout.DirtyWhite);
                    break;

                case ProductionObstacleKind.Stop:
                    SetPart(body, new Vector3(x, 22f, 0f), new Vector3(8f, 44f, 1f),
                        0f, semantic);
                    SetPart(details[0], new Vector3(x - 7f, 22f, 0.2f),
                        new Vector3(3f, 48f, 1f), 0f, ProductionHudLayout.Structural);
                    SetPart(details[1], new Vector3(x + 7f, 22f, 0.2f),
                        new Vector3(3f, 48f, 1f), 0f, ProductionHudLayout.Structural);
                    for (int band = 0; band < 3; band++)
                    {
                        float y = 10f + band * 12f;
                        Color bandColor = wallOpen ? Color.clear
                            : WithAlpha(ProductionHudLayout.Violet,
                                0.92f - band * 0.16f);
                        SetPart(details[2 + band], new Vector3(x, y, -0.2f),
                            new Vector3(wallOpen ? 0f : 13f, 2f, 1f), 0f,
                            bandColor);
                    }
                    break;

                case ProductionObstacleKind.Direction:
                    SetPart(body, new Vector3(x, 17f, 0f), new Vector3(3f, 34f, 1f),
                        0f, semantic);
                    SetPart(details[0], new Vector3(x - 8f, 22f, 0f),
                        new Vector3(18f, 2f, 1f), 35f, ProductionHudLayout.Cyan);
                    SetPart(details[1], new Vector3(x + 8f, 22f, 0f),
                        new Vector3(18f, 2f, 1f), -35f, ProductionHudLayout.Magenta);
                    SetPart(details[2], new Vector3(x - 14f, 28f, 0f),
                        new Vector3(5f, 5f, 1f), 45f, ProductionHudLayout.Cyan);
                    SetPart(details[3], new Vector3(x + 14f, 28f, 0f),
                        new Vector3(5f, 5f, 1f), 45f, ProductionHudLayout.Magenta);
                    SetPart(details[4], new Vector3(x, 2f, 0f),
                        new Vector3(17f, 2f, 1f), 0f, ProductionHudLayout.DirtyWhite);
                    break;

                default:
                    SetPart(body, new Vector3(x, 1f, 0f), new Vector3(span, 2f, 1f),
                        0f, semantic);
                    SetPart(details[0], new Vector3(x, -2f, 0.2f),
                        new Vector3(span, 7f, 1f), 0f, ProductionHudLayout.VoidBlack);
                    SetPart(details[1], new Vector3(leftMarkerX, 3f, -0.1f),
                        new Vector3(boundaryWidth, 6f, 1f), 0f,
                        ProductionHudLayout.DirtyWhite);
                    SetPart(details[2], new Vector3(rightMarkerX, 3f, -0.1f),
                        new Vector3(boundaryWidth, 6f, 1f), 0f,
                        ProductionHudLayout.DirtyWhite);
                    break;
            }
        }

        private static void SetPart(SpriteRenderer renderer, Vector3 position,
            Vector3 scale, float angle, Color color)
        {
            renderer.transform.SetPositionAndRotation(position,
                Quaternion.Euler(0f, 0f, angle));
            renderer.transform.localScale = scale;
            renderer.color = color;
        }

        private static Color WithAlpha(Color color, float alpha)
            => new Color(color.r, color.g, color.b, alpha);

        private void DestroyVisuals()
        {
            if (_visualRoot != null)
            {
                if (Application.isPlaying) Destroy(_visualRoot.gameObject);
                else DestroyImmediate(_visualRoot.gameObject);
            }
            if (_unitSprite != null)
            {
                if (Application.isPlaying) Destroy(_unitSprite);
                else DestroyImmediate(_unitSprite);
            }
            _visualRoot = null;
            _groundRenderer = null;
            _groundDetails = null;
            _obstacleRenderers = null;
            _obstacleDetails = null;
            _unitSprite = null;
        }

        private void OnDestroy() => DestroyVisuals();
    }
}
