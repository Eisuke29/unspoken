using UnityEngine;

namespace Before.Production
{
    /// <summary>
    /// Replaceable farewell/ending presentation. Runtime state remains in
    /// ProductionEndingFlow; this component owns visuals only.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProductionEndingPresenter : MonoBehaviour
    {
        public const string CompleteCopy = ProductionTextCatalog.EndingComplete;
        public const string TerminatedCopy = ProductionTextCatalog.LinkTerminated;
        public const string RestartCopy = ProductionTextCatalog.Restart;
        public const int TemporaryRendererCount = 4;
        public const int DetailRendererCount = 12;

        private static readonly Color[] TraceColors =
        {
            new Color(0.08f, 0.86f, 0.9f, 0.88f),
            new Color(0.96f, 0.66f, 0.18f, 0.88f),
            new Color(0.7f, 0.38f, 0.96f, 0.88f)
        };

        [SerializeField] private ProductionEndingFlow flow;
        [SerializeField] private bool drawTemporaryEnding = true;

        private Transform _visualRoot;
        private SpriteRenderer[] _renderers;
        private SpriteRenderer[] _details;
        private Sprite _unitSprite;
        private GUIStyle _center;
        private GUIStyle _detail;

        public ProductionEndingFlow Flow { get => flow; set => flow = value; }
        public bool DrawTemporaryEnding
        {
            get => drawTemporaryEnding;
            set => drawTemporaryEnding = value;
        }
        public bool Visible => drawTemporaryEnding && flow != null && flow.IsActive;
        public int VisualRendererCount => _renderers == null ? 0 : _renderers.Length;
        public int TotalVisualRendererCount => _renderers == null
            ? 0 : _renderers.Length + _details.Length;
        public int VisualColliderCount => _visualRoot == null
            ? 0 : _visualRoot.GetComponentsInChildren<Collider2D>(true).Length;
        public float WorldFadeAlpha => FadeAlphaFor(flow != null ? flow.Stage :
            ProductionEndingStage.None, flow != null ? flow.StageProgress : 0f);

        private void Awake()
        {
            if (flow == null) flow = GetComponent<ProductionEndingFlow>();
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

        public static Color TraceColorAt(int index)
            => index >= 0 && index < TraceColors.Length ? TraceColors[index] : Color.clear;

        public static float FadeAlphaFor(ProductionEndingStage stage, float progress)
        {
            if (stage == ProductionEndingStage.WorldFade) return Mathf.Clamp01(progress);
            if (stage == ProductionEndingStage.TraceFade ||
                stage == ProductionEndingStage.CompleteMessage ||
                stage == ProductionEndingStage.EndingHold) return 1f;
            return 0f;
        }

        public static int VisibleTraceCount(ProductionEndingStage stage, float elapsed)
        {
            if (stage < ProductionEndingStage.MemoryTraces) return 0;
            if (stage > ProductionEndingStage.MemoryTraces) return TraceColors.Length;
            float interval = ProductionEndingFlow.TraceDrawSeconds +
                             ProductionEndingFlow.TraceGapSeconds;
            return Mathf.Clamp(Mathf.FloorToInt(elapsed / interval) + 1,
                0, TraceColors.Length);
        }

        public static Rect FullBlackoutRect(float screenWidth, float screenHeight)
            => new Rect(0f, 0f, screenWidth, screenHeight);

        public void RefreshNow()
        {
            if (_visualRoot == null) return;
            _visualRoot.gameObject.SetActive(Visible);
            if (!Visible || _renderers == null || flow.Controller == null) return;

            float nulX = flow.Controller.transform.position.x;
            int visibleTraces = VisibleTraceCount(flow.Stage, flow.StageElapsed);
            float traceAlpha = flow.Stage == ProductionEndingStage.TraceFade
                ? 1f - flow.StageProgress
                : flow.Stage >= ProductionEndingStage.CompleteMessage ? 0f : 1f;
            for (int i = 0; i < 3; i++)
            {
                Color color = TraceColorAt(i);
                color.a *= i < visibleTraces ? traceAlpha : 0f;
                float baseX = nulX + 18f + i * 19f;
                float baseY = 42f + i * 8f;
                SetPart(_renderers[i], new Vector3(baseX, baseY, 1f),
                    new Vector3(22f, 2f, 1f), i == 2 ? 22f : -16f + i * 16f,
                    color);

                int detail = i * 3;
                SetPart(_details[detail], new Vector3(baseX + 11f, baseY + 7f, 1f),
                    new Vector3(2f, 13f, 1f), 0f, WithAlpha(color, color.a * 0.82f));
                SetPart(_details[detail + 1],
                    new Vector3(baseX + 19f, baseY + 13f, 1f),
                    new Vector3(16f, 2f, 1f), 0f, WithAlpha(color, color.a * 0.68f));
                SetPart(_details[detail + 2],
                    new Vector3(baseX + 27f, baseY + 17f, 1f),
                    new Vector3(5f, 5f, 1f), 45f, color);
            }

            bool departing = flow.Stage == ProductionEndingStage.Departing;
            float departureProgress = departing ? flow.StageProgress : 0f;
            float departureX = nulX + Mathf.Lerp(0f, 130f, departureProgress);
            SetPart(_renderers[3], new Vector3(departureX, 16f, 1f),
                new Vector3(7f, 7f, 1f), 45f,
                departing ? new Color(0.9f, 0.88f, 0.78f, 0.86f) : Color.clear);
            for (int i = 0; i < 3; i++)
            {
                float alpha = departing ? Mathf.Max(0f, 0.5f - i * 0.12f) : 0f;
                SetPart(_details[9 + i],
                    new Vector3(departureX - 8f - i * 9f, 16f + (i % 2) * 3f, 1f),
                    Vector3.one * (5f - i), 45f,
                    new Color(ProductionHudLayout.DirtyWhite.r,
                        ProductionHudLayout.DirtyWhite.g,
                        ProductionHudLayout.DirtyWhite.b, alpha));
            }
        }

        private void OnGUI()
        {
            if (!Visible) return;
            float scale = Mathf.Max(1f, Screen.height / 180f);
            int previousDepth = GUI.depth;
            Color previousColor = GUI.color;
            GUI.depth = -220;

            float fade = WorldFadeAlpha;
            if (fade > 0f)
            {
                GUI.color = new Color(0f, 0f, 0f, fade);
                GUI.DrawTexture(FullBlackoutRect(
                    Screen.width, Screen.height), Texture2D.whiteTexture);
            }

            EnsureStyles(scale);
            GUI.color = Color.white;
            if (flow.Stage == ProductionEndingStage.CompleteMessage)
            {
                GUI.Label(new Rect(0f, Screen.height * 0.43f,
                    Screen.width, 12f * scale), CompleteCopy, _center);
            }
            else if (flow.GameplayEnded)
            {
                GUI.Label(new Rect(0f, Screen.height * 0.40f,
                    Screen.width, 12f * scale), TerminatedCopy, _center);
                GUI.Label(new Rect(0f, Screen.height * 0.40f + 13f * scale,
                    Screen.width, 9f * scale), RestartCopy, _detail);
            }

            GUI.color = previousColor;
            GUI.depth = previousDepth;
        }

        private void EnsureStyles(float scale)
        {
            int size = Mathf.Max(13, Mathf.RoundToInt(5f * scale));
            if (_center != null && _center.fontSize == size) return;
            _center = new GUIStyle(GUI.skin.label)
            {
                fontSize = size,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = ProductionHudLayout.DirtyWhite }
            };
            _detail = new GUIStyle(_center)
            {
                fontStyle = FontStyle.Normal,
                normal = { textColor = new Color(0.7f, 0.72f, 0.7f) }
            };
        }

        private void EnsureVisuals()
        {
            if (!drawTemporaryEnding || flow == null) return;
            if (_visualRoot != null && _renderers != null &&
                _renderers.Length == TemporaryRendererCount) return;
            DestroyVisuals();
            _unitSprite = Sprite.Create(Texture2D.whiteTexture,
                new Rect(0f, 0f, Texture2D.whiteTexture.width, Texture2D.whiteTexture.height),
                new Vector2(0.5f, 0.5f), Texture2D.whiteTexture.width);
            var root = new GameObject("ProductionEndingPresentation");
            root.transform.SetParent(transform, false);
            _visualRoot = root.transform;
            _renderers = new SpriteRenderer[TemporaryRendererCount];
            _details = new SpriteRenderer[DetailRendererCount];
            for (int i = 0; i < _renderers.Length; i++)
                _renderers[i] = CreateRenderer("FarewellPart_" + i, 4);
            for (int i = 0; i < _details.Length; i++)
                _details[i] = CreateRenderer("FarewellDetail_" + i, 3);
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
