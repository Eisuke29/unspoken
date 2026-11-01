using UnityEngine;

namespace Before.Production
{
    /// <summary>
    /// Final public game-loop HUD. It reads game-loop state only and never owns
    /// command, course, judgement, life, recovery, or restart decisions.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProductionHud : MonoBehaviour
    {
        public const float HeartLossSeconds = 0.45f;
        public const float ZeroWarningSeconds = 1.2f;
        public const string ZeroWarning = ProductionTextCatalog.ZeroLives;

        [SerializeField] private ProductionGameLoop gameLoop;
        [SerializeField] private Texture2D fullHeart;
        [SerializeField] private Font publicFont;
        [SerializeField] private ProductionShellFlow shellFlow;

        private GUIStyle _labelStyle;
        private GUIStyle _centerStyle;
        private int _previousLives = ProductionGameLoop.StartingLives;
        private int _lostHeartIndex = -1;
        private float _lossStartedAt = float.NegativeInfinity;
        private float _zeroWarningUntil = float.NegativeInfinity;

        public ProductionGameLoop GameLoop
        {
            get => gameLoop;
            set => gameLoop = value;
        }

        public Texture2D FullHeart { get => fullHeart; set => fullHeart = value; }
        public Font PublicFont { get => publicFont; set { publicFont = value; ClearStyles(); } }
        public ProductionShellFlow ShellFlow { get => shellFlow; set => shellFlow = value; }
        public bool HeartAssetReady => fullHeart != null;
        public bool ZeroWarningVisible => Time.unscaledTime < _zeroWarningUntil;
        public int LostHeartIndex => _lostHeartIndex;
        public int GuiRepaintCount { get; private set; }

        public int DisplayedLives => gameLoop == null ? 0 : ClampHeartCount(gameLoop.Lives);
        public bool ShowGameOver => gameLoop != null && gameLoop.IsGameOver;
        public bool ShowPaused => ShouldShowPause(
            gameLoop != null && gameLoop.Controller != null && gameLoop.Controller.Nul.Paused,
            ShowGameOver) && (shellFlow == null || !shellFlow.ExitConfirmationVisible);
        public ProductionRoute DisplayedRoute => gameLoop == null
            ? ProductionRoute.None : gameLoop.SelectedRoute;

        private void Awake()
        {
            if (gameLoop == null) gameLoop = GetComponent<ProductionGameLoop>();
            _previousLives = gameLoop != null ? gameLoop.Lives : ProductionGameLoop.StartingLives;
        }

        private void OnEnable()
        {
            if (gameLoop != null) gameLoop.OnLivesChanged += HandleLivesChanged;
        }

        private void OnDisable()
        {
            if (gameLoop != null) gameLoop.OnLivesChanged -= HandleLivesChanged;
        }

        private void Update()
        {
            if (_lostHeartIndex >= 0 &&
                Time.unscaledTime - _lossStartedAt >= HeartLossSeconds)
                _lostHeartIndex = -1;
        }

        public static int ClampHeartCount(int lives)
            => Mathf.Clamp(lives, 0, ProductionGameLoop.StartingLives);

        public static bool ShouldShowPause(bool paused, bool gameOver)
            => paused && !gameOver;

        public static Rect HeartRowBounds(float screenWidth, float screenHeight)
            => ProductionHudLayout.HeartRowRect(screenWidth, screenHeight);

        public static Rect HeartBounds(int index, float screenWidth, float screenHeight)
        {
            Rect row = HeartRowBounds(screenWidth, screenHeight);
            float scale = ProductionHudLayout.Scale(screenWidth, screenHeight);
            return new Rect(row.x + index * 10f * scale, row.y,
                8f * scale, 7f * scale);
        }

        private void EnsureStyles(float scale)
        {
            int fontSize = Mathf.Max(12, Mathf.RoundToInt(5f * scale));
            if (_labelStyle != null && _labelStyle.fontSize == fontSize &&
                _labelStyle.font == publicFont) return;
            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                font = publicFont,
                fontSize = fontSize,
                normal = { textColor = ProductionHudLayout.DirtyWhite }
            };
            _centerStyle = new GUIStyle(_labelStyle)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold
            };
        }

        private void OnGUI()
        {
            if (Event.current.type == EventType.Repaint) GuiRepaintCount++;
            if (gameLoop == null) return;
            if (shellFlow != null && shellFlow.BlocksGameplayPresentation) return;
            int previousDepth = GUI.depth;
            GUI.depth = -100;
            float scale = ProductionHudLayout.Scale(Screen.width, Screen.height);
            EnsureStyles(scale);

            if (fullHeart != null)
            {
                for (int i = 0; i < DisplayedLives; i++)
                    GUI.DrawTexture(HeartBounds(i, Screen.width, Screen.height),
                        fullHeart, ScaleMode.StretchToFill, true);
                DrawLostHeart(scale);
            }

            if (ZeroWarningVisible)
            {
                Rect warning = ProductionHudLayout.LogicalRect(
                    new Rect(12f, 24f, 220f, 10f), Screen.width, Screen.height);
                Color previousWarningColor = GUI.color;
                GUI.color = ProductionHudLayout.Fault;
                GUI.Label(warning, ZeroWarning, _labelStyle);
                GUI.color = previousWarningColor;
            }

            if (ShowPaused)
            {
                Rect message = ProductionHudLayout.CenterMessageRect(
                    Screen.width, Screen.height);
                GUI.Label(new Rect(message.x, message.y, message.width, 10f * scale),
                    ProductionTextCatalog.Pause, _centerStyle);
                GUI.Label(new Rect(message.x, message.y + 11f * scale,
                    message.width, 8f * scale), ProductionTextCatalog.Resume, _centerStyle);
                GUI.depth = previousDepth;
                return;
            }

            if (!ShowGameOver)
            {
                GUI.depth = previousDepth;
                return;
            }
            Color previous = GUI.color;
            Rect gameOver = ProductionHudLayout.CenterMessageRect(
                Screen.width, Screen.height);
            GUI.color = ProductionHudLayout.Fault;
            GUI.Label(new Rect(gameOver.x, gameOver.y, gameOver.width, 10f * scale),
                ProductionTextCatalog.GameOver, _centerStyle);
            GUI.color = ProductionHudLayout.DirtyWhite;
            GUI.Label(new Rect(gameOver.x, gameOver.y + 11f * scale,
                gameOver.width, 8f * scale), ProductionTextCatalog.Restart, _centerStyle);
            GUI.color = previous;
            GUI.depth = previousDepth;
        }

        private void HandleLivesChanged(int lives)
        {
            int clamped = ClampHeartCount(lives);
            if (clamped < _previousLives)
            {
                _lostHeartIndex = clamped;
                _lossStartedAt = Time.unscaledTime;
                if (clamped == 0)
                    _zeroWarningUntil = Time.unscaledTime + ZeroWarningSeconds;
            }
            else if (clamped > _previousLives)
            {
                _lostHeartIndex = -1;
                _lossStartedAt = float.NegativeInfinity;
                _zeroWarningUntil = float.NegativeInfinity;
            }
            _previousLives = clamped;
        }

        private void DrawLostHeart(float scale)
        {
            if (_lostHeartIndex < 0 || fullHeart == null) return;
            float progress = (Time.unscaledTime - _lossStartedAt) / HeartLossSeconds;
            if (progress >= 1f) return;

            Rect heart = HeartBounds(_lostHeartIndex, Screen.width, Screen.height);
            Color previous = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 1f - progress);
            const int slices = 4;
            for (int slice = 0; slice < slices; slice++)
            {
                float yMin = slice / (float)slices;
                float yMax = (slice + 1f) / slices;
                float jitter = ((slice & 1) == 0 ? 1f : -1f) *
                               Mathf.Round(progress * 3f) * scale;
                Rect target = new Rect(heart.x + jitter,
                    heart.y + yMin * heart.height,
                    heart.width, heart.height / slices);
                GUI.DrawTextureWithTexCoords(target, fullHeart,
                    new Rect(0f, 1f - yMax, 1f, yMax - yMin), true);
            }
            GUI.color = previous;
        }

        private void ClearStyles()
        {
            _labelStyle = null;
            _centerStyle = null;
        }
    }
}
