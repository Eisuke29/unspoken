using UnityEngine;

namespace Before.Production
{
    /// <summary>
    /// Replaceable branch-world presentation. It reads BranchFlow and creates
    /// non-colliding route landmarks only; route and gameplay state stay in runtime.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProductionBranchPresenter : MonoBehaviour
    {
        public const int TemporaryRendererCount = 6;
        public const int DetailRendererCount = TemporaryRendererCount * 2;

        private static readonly Color LeftPalette = new Color(0.08f, 0.68f, 0.78f, 0.24f);
        private static readonly Color RightPalette = new Color(0.72f, 0.18f, 0.68f, 0.24f);

        [SerializeField] private ProductionBranchFlow flow;
        [SerializeField] private bool drawTemporaryBranch = true;

        private Transform _visualRoot;
        private SpriteRenderer[] _renderers;
        private SpriteRenderer[] _details;
        private Sprite _unitSprite;

        public ProductionBranchFlow Flow { get => flow; set => flow = value; }
        public bool DrawTemporaryBranch
        {
            get => drawTemporaryBranch;
            set => drawTemporaryBranch = value;
        }
        public bool Visible => drawTemporaryBranch && flow != null && flow.BranchStarted &&
                               flow.Controller != null && !flow.Controller.Nul.Paused;
        public int VisualRendererCount => _renderers == null ? 0 : _renderers.Length;
        public int TotalVisualRendererCount => _renderers == null
            ? 0 : _renderers.Length + _details.Length;
        public int VisualColliderCount => _visualRoot == null
            ? 0 : _visualRoot.GetComponentsInChildren<Collider2D>(true).Length;

        private void Awake()
        {
            if (flow == null) flow = GetComponent<ProductionBranchFlow>();
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

        public static Color PaletteFor(ProductionRoute route)
            => route == ProductionRoute.Left ? LeftPalette
                : route == ProductionRoute.Right ? RightPalette : Color.clear;

        public static Color LandmarkAccentFor(ProductionRoute route)
            => route == ProductionRoute.Left ? ProductionHudLayout.Cyan
                : route == ProductionRoute.Right ? ProductionHudLayout.Magenta
                : Color.clear;

        public void RefreshNow()
        {
            if (_visualRoot == null) return;
            _visualRoot.gameObject.SetActive(Visible);
            if (!Visible || _renderers == null) return;

            ProductionRoute route = flow.ActiveRoute;
            float runnerX = flow.Controller.transform.position.x;
            float centerX = ProductionCameraFollow.CameraXFor(runnerX);
            Color palette = PaletteFor(route);
            Color accent = LandmarkAccentFor(route);
            for (int i = 0; i < _renderers.Length; i++)
            {
                float x = centerX - 145f + i * 58f;
                SpriteRenderer body = _renderers[i];
                SpriteRenderer first = _details[i * 2];
                SpriteRenderer second = _details[i * 2 + 1];
                if (route == ProductionRoute.Left)
                {
                    float height = i % 2 == 0 ? 17f : 25f;
                    SetPart(body, new Vector3(x, height * 0.5f, 8f),
                        new Vector3(66f, height, 1f), 0f,
                        new Color(palette.r, palette.g, palette.b,
                            Mathf.Max(0.1f, palette.a - i * 0.014f)));
                    SetPart(first, new Vector3(x, height + 1f, 7.8f),
                        new Vector3(70f, 2f, 1f), 0f,
                        new Color(accent.r, accent.g, accent.b, 0.38f));
                    SetPart(second, new Vector3(x - 22f + (i % 3) * 22f,
                            height * 0.45f, 7.7f),
                        new Vector3(3f, height * 0.65f, 1f), 0f,
                        new Color(ProductionHudLayout.DirtyWhite.r,
                            ProductionHudLayout.DirtyWhite.g,
                            ProductionHudLayout.DirtyWhite.b, 0.18f));
                }
                else
                {
                    float height = 44f + (i % 3) * 15f;
                    float width = i == 5 ? 10f : 24f + (i % 2) * 8f;
                    float angle = i == 5 ? 45f : i % 2 == 0 ? -4f : 5f;
                    SetPart(body, new Vector3(x, height * 0.5f, 8f),
                        new Vector3(width, i == 5 ? 10f : height, 1f), angle,
                        i == 5
                            ? new Color(ProductionHudLayout.Amber.r,
                                ProductionHudLayout.Amber.g,
                                ProductionHudLayout.Amber.b, 0.78f)
                            : new Color(palette.r, palette.g, palette.b,
                                Mathf.Max(0.1f, palette.a - i * 0.012f)));
                    SetPart(first, new Vector3(x, height * 0.64f, 7.7f),
                        new Vector3(i == 5 ? 15f : 3f,
                            i == 5 ? 2f : height * 0.72f, 1f),
                        i == 5 ? 0f : angle,
                        new Color(accent.r, accent.g, accent.b, i == 5 ? 0.72f : 0.42f));
                    SetPart(second, new Vector3(x + (i % 2 == 0 ? -6f : 7f),
                            height + 7f, 7.6f),
                        new Vector3(2f, i == 5 ? 12f : 14f + i, 1f),
                        i % 2 == 0 ? -12f : 10f,
                        new Color(ProductionHudLayout.DirtyWhite.r,
                            ProductionHudLayout.DirtyWhite.g,
                            ProductionHudLayout.DirtyWhite.b, 0.26f));
                }
            }
        }

        private void EnsureVisuals()
        {
            if (!drawTemporaryBranch || flow == null) return;
            if (_visualRoot != null && _renderers != null &&
                _renderers.Length == TemporaryRendererCount) return;
            DestroyVisuals();

            _unitSprite = Sprite.Create(Texture2D.whiteTexture,
                new Rect(0f, 0f, Texture2D.whiteTexture.width, Texture2D.whiteTexture.height),
                new Vector2(0.5f, 0.5f), Texture2D.whiteTexture.width);
            var root = new GameObject("ProductionBranchWorld");
            root.transform.SetParent(transform, false);
            _visualRoot = root.transform;
            _renderers = new SpriteRenderer[TemporaryRendererCount];
            _details = new SpriteRenderer[DetailRendererCount];
            for (int i = 0; i < _renderers.Length; i++)
                _renderers[i] = CreateRenderer("BranchLandmark_" + i, -9);
            for (int i = 0; i < _details.Length; i++)
                _details[i] = CreateRenderer("BranchDetail_" + i, -8);
        }

        private SpriteRenderer CreateRenderer(string objectName, int sortingOrder)
        {
            var visual = new GameObject(objectName);
            visual.transform.SetParent(_visualRoot, false);
            var renderer = visual.AddComponent<SpriteRenderer>();
            renderer.sprite = _unitSprite;
            renderer.sortingOrder = sortingOrder;
            return renderer;
        }

        private static void SetPart(SpriteRenderer renderer, Vector3 position,
            Vector3 scale, float angle, Color color)
        {
            renderer.transform.SetPositionAndRotation(position,
                Quaternion.Euler(0f, 0f, angle));
            renderer.transform.localScale = scale;
            renderer.color = color;
        }

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
            _renderers = null;
            _details = null;
            _unitSprite = null;
        }

        private void OnDestroy() => DestroyVisuals();
    }
}
