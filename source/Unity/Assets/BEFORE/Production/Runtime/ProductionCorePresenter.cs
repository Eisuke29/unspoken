using UnityEngine;

namespace Before.Production
{
    /// <summary>
    /// Replaceable CORE presentation. It reads CoreFlow and owns no arrival,
    /// transfer, command, ending, collider, or reset state.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProductionCorePresenter : MonoBehaviour
    {
        public const string CoreHeadline = ProductionTextCatalog.CoreHeadline;
        public const string MaintainLink = ProductionTextCatalog.MaintainLink;
        public const string TransferComplete = ProductionTextCatalog.TransferComplete;
        public const int TemporaryRendererCount = 8;
        public const int DetailRendererCount = 12;

        [SerializeField] private ProductionCoreFlow flow;
        [SerializeField] private bool drawTemporaryCore = true;

        private Transform _visualRoot;
        private SpriteRenderer[] _renderers;
        private SpriteRenderer[] _details;
        private Sprite _unitSprite;
        private GUIStyle _headline;
        private GUIStyle _detail;

        public ProductionCoreFlow Flow { get => flow; set => flow = value; }
        public bool DrawTemporaryCore
        {
            get => drawTemporaryCore;
            set => drawTemporaryCore = value;
        }
        public bool Visible => drawTemporaryCore && flow != null && flow.IsActive;
        public int VisualRendererCount => _renderers == null ? 0 : _renderers.Length;
        public int TotalVisualRendererCount => _renderers == null
            ? 0 : _renderers.Length + _details.Length;
        public int VisualColliderCount => _visualRoot == null
            ? 0 : _visualRoot.GetComponentsInChildren<Collider2D>(true).Length;

        private void Awake()
        {
            if (flow == null) flow = GetComponent<ProductionCoreFlow>();
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

        public static Color StageColor(ProductionCoreStage stage)
        {
            switch (stage)
            {
                case ProductionCoreStage.Unfolding: return new Color(0.72f, 0.72f, 0.68f, 0.72f);
                case ProductionCoreStage.Transferring: return new Color(0.1f, 0.82f, 0.88f, 0.86f);
                case ProductionCoreStage.Disconnecting: return new Color(0.52f, 0.9f, 0.92f, 0.55f);
                case ProductionCoreStage.IndependentHold: return new Color(0.9f, 0.88f, 0.78f, 0.82f);
                default: return Color.clear;
            }
        }

        public static Color IndependentAccent
            => ProductionHudLayout.DirtyWhite;

        public void RefreshNow()
        {
            if (_visualRoot == null) return;
            _visualRoot.gameObject.SetActive(Visible);
            if (!Visible || _renderers == null || flow.Controller == null) return;

            float nulX = flow.Controller.transform.position.x;
            float coreX = nulX + 70f;
            float progress = flow.StageProgress;
            float phase = flow.StageElapsed * 2.5f;
            float breathe = 0.88f + Mathf.Sin(phase * 2f) * 0.12f;
            Color color = StageColor(flow.Stage);
            float unfold = flow.Stage == ProductionCoreStage.Unfolding
                ? Mathf.Lerp(0.12f, 1f, progress) : 1f;

            for (int i = 0; i < 3; i++)
            {
                float size = (24f + i * 10f) * unfold;
                SetPart(_renderers[i], new Vector3(coreX, 30f, 2f),
                    new Vector3(size, 2f + i * 0.4f, 1f),
                    i * 60f + phase * (i % 2 == 0 ? 12f : -9f),
                    WithAlpha(color, color.a * (1f - i * 0.16f)));
            }

            SetPart(_renderers[3], new Vector3(coreX, 28f, 2f),
                new Vector3(5f * breathe, 42f * unfold, 1f), 0f,
                WithAlpha(color, color.a * 0.72f));
            SetPart(_renderers[4], new Vector3(coreX, 6f, 2f),
                new Vector3(34f, 4f, 1f), 0f,
                new Color(ProductionHudLayout.MidGray.r,
                    ProductionHudLayout.MidGray.g,
                    ProductionHudLayout.MidGray.b, 0.92f));

            for (int i = 0; i < 3; i++)
            {
                float t = Mathf.Repeat(progress + i / 3f, 1f);
                if (flow.Stage == ProductionCoreStage.Disconnecting) t = 1f - t;
                SetPart(_renderers[5 + i],
                    new Vector3(Mathf.Lerp(coreX, nulX, t),
                        Mathf.Lerp(30f, 16f, t), 1f),
                    Vector3.one * (3f + i), 45f,
                    flow.Stage == ProductionCoreStage.Unfolding ? Color.clear : color);
            }

            float radius = 23f * unfold;
            for (int i = 0; i < 6; i++)
            {
                float angle = i * 60f + phase * 8f;
                float radians = angle * Mathf.Deg2Rad;
                Vector3 position = new Vector3(coreX + Mathf.Cos(radians) * radius,
                    30f + Mathf.Sin(radians) * radius, 1.5f);
                SetPart(_details[i], position, new Vector3(8f, 2f, 1f),
                    angle + 90f, WithAlpha(color, color.a * 0.72f));
            }

            SetPart(_details[6], new Vector3(coreX - 22f, 15f, 2.2f),
                new Vector3(3f, 28f, 1f), -8f, ProductionHudLayout.Structural);
            SetPart(_details[7], new Vector3(coreX + 22f, 15f, 2.2f),
                new Vector3(3f, 28f, 1f), 8f, ProductionHudLayout.Structural);
            SetPart(_details[8], new Vector3(coreX, 3f, 2.1f),
                new Vector3(48f, 2f, 1f), 0f,
                WithAlpha(ProductionHudLayout.Ash, 0.8f));
            SetPart(_details[9], new Vector3(coreX, 0f, 2.1f),
                new Vector3(62f, 2f, 1f), 0f,
                WithAlpha(ProductionHudLayout.MidGray, 0.9f));

            bool linked = flow.Stage == ProductionCoreStage.Transferring ||
                          flow.Stage == ProductionCoreStage.Disconnecting;
            float linkAlpha = linked ? 0.56f + 0.28f * Mathf.Sin(phase * 5f) : 0f;
            float linkX = (coreX + nulX) * 0.5f;
            float linkWidth = Mathf.Abs(coreX - nulX) - 14f;
            SetPart(_details[10], new Vector3(linkX, 24f, 1f),
                new Vector3(linkWidth, 1f, 1f), 0f,
                WithAlpha(ProductionHudLayout.Cyan, linkAlpha));
            SetPart(_details[11], new Vector3(linkX, 20f, 1f),
                new Vector3(linkWidth * 0.72f, 1f, 1f), 0f,
                WithAlpha(ProductionHudLayout.DirtyWhite, linkAlpha * 0.65f));
        }

        private void OnGUI()
        {
            if (!Visible) return;
            EnsureStyles();
            float scale = Mathf.Max(1f, Screen.height / 180f);
            Rect region = new Rect(12f * scale, 10f * scale, 190f * scale, 24f * scale);
            GUI.Label(new Rect(region.x, region.y, region.width, 10f * scale),
                flow.IndependentPower ? TransferComplete : CoreHeadline, _headline);
            if (!flow.IndependentPower)
                GUI.Label(new Rect(region.x, region.y + 10f * scale,
                    region.width, 9f * scale), MaintainLink, _detail);
        }

        private void EnsureStyles()
        {
            int size = Mathf.Max(13, Mathf.RoundToInt(Screen.height / 36f));
            if (_headline != null && _headline.fontSize == size) return;
            _headline = new GUIStyle(GUI.skin.label)
            {
                fontSize = size,
                fontStyle = FontStyle.Bold,
                normal = { textColor = ProductionHudLayout.DirtyWhite }
            };
            _detail = new GUIStyle(_headline)
            {
                fontStyle = FontStyle.Normal,
                normal = { textColor = new Color(0.7f, 0.82f, 0.84f) }
            };
        }

        private void EnsureVisuals()
        {
            if (!drawTemporaryCore || flow == null) return;
            if (_visualRoot != null && _renderers != null &&
                _renderers.Length == TemporaryRendererCount) return;
            DestroyVisuals();
            _unitSprite = Sprite.Create(Texture2D.whiteTexture,
                new Rect(0f, 0f, Texture2D.whiteTexture.width, Texture2D.whiteTexture.height),
                new Vector2(0.5f, 0.5f), Texture2D.whiteTexture.width);
            var root = new GameObject("ProductionCorePresentation");
            root.transform.SetParent(transform, false);
            _visualRoot = root.transform;
            _renderers = new SpriteRenderer[TemporaryRendererCount];
            _details = new SpriteRenderer[DetailRendererCount];
            for (int i = 0; i < _renderers.Length; i++)
                _renderers[i] = CreateRenderer("CorePart_" + i, i < 5 ? -4 : 2);
            for (int i = 0; i < _details.Length; i++)
                _details[i] = CreateRenderer("CoreDetail_" + i, i < 10 ? -3 : 1);
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
            _renderers = null;
            _details = null;
            _unitSprite = null;
        }

        private void OnDestroy() => DestroyVisuals();
    }
}
