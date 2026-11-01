using UnityEngine;

namespace Before.Production
{
    public enum ProductionRecoveryIrisPhase
    {
        None,
        Damage,
        IrisOut,
        Black,
        IrisIn
    }

    /// <summary>
    /// Presentation-only recovery cover. The existing GameLoop still owns lives,
    /// freeze duration, checkpoint movement, judgement rearm, and invulnerability.
    /// Its recovery-completed event occurs while this cover is fully black, then
    /// starts the reveal.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ProductionGameLoop))]
    public sealed class ProductionRecoveryPresenter : MonoBehaviour
    {
        public const float IrisOutSeconds = 0.45f;
        public const float IrisInSeconds = 0.45f;
        public const float PixelStep = 4f;

        [SerializeField] private ProductionGameLoop gameLoop;
        [SerializeField] private Camera worldCamera;

        private int _previousLives = ProductionGameLoop.StartingLives;
        private bool _active;
        private float _damageStartedAt = float.NegativeInfinity;
        private float _irisInStartedAt = float.NegativeInfinity;

        public ProductionGameLoop GameLoop
        {
            get => gameLoop;
            set => gameLoop = value;
        }

        public Camera WorldCamera
        {
            get => worldCamera;
            set => worldCamera = value;
        }

        public bool TransitionActive => _active;

        public ProductionRecoveryIrisPhase CurrentPhase
        {
            get
            {
                if (!_active) return ProductionRecoveryIrisPhase.None;
                if (_irisInStartedAt > float.NegativeInfinity)
                    return ProductionRecoveryIrisPhase.IrisIn;
                float elapsed = Time.unscaledTime - _damageStartedAt;
                if (elapsed < ProductionHud.HeartLossSeconds)
                    return ProductionRecoveryIrisPhase.Damage;
                if (elapsed < ProductionHud.HeartLossSeconds + IrisOutSeconds)
                    return ProductionRecoveryIrisPhase.IrisOut;
                return ProductionRecoveryIrisPhase.Black;
            }
        }

        public float CurrentOpenness
        {
            get
            {
                switch (CurrentPhase)
                {
                    case ProductionRecoveryIrisPhase.Damage: return 1f;
                    case ProductionRecoveryIrisPhase.IrisOut:
                        return 1f - Mathf.Clamp01((Time.unscaledTime -
                            _damageStartedAt - ProductionHud.HeartLossSeconds) /
                            IrisOutSeconds);
                    case ProductionRecoveryIrisPhase.Black: return 0f;
                    case ProductionRecoveryIrisPhase.IrisIn:
                        return Mathf.Clamp01((Time.unscaledTime -
                            _irisInStartedAt) / IrisInSeconds);
                    default: return 1f;
                }
            }
        }

        private void Awake()
        {
            if (gameLoop == null) gameLoop = GetComponent<ProductionGameLoop>();
            if (worldCamera == null) worldCamera = Camera.main;
            _previousLives = gameLoop != null
                ? gameLoop.Lives : ProductionGameLoop.StartingLives;
        }

        private void OnEnable()
        {
            if (gameLoop == null) gameLoop = GetComponent<ProductionGameLoop>();
            if (gameLoop == null) return;
            gameLoop.OnLivesChanged += HandleLivesChanged;
            gameLoop.OnRecoveryCompleted += HandleRecoveryCompleted;
            gameLoop.OnLoopReset += CancelTransition;
            _previousLives = gameLoop.Lives;
        }

        private void OnDisable()
        {
            if (gameLoop != null)
            {
                gameLoop.OnLivesChanged -= HandleLivesChanged;
                gameLoop.OnRecoveryCompleted -= HandleRecoveryCompleted;
                gameLoop.OnLoopReset -= CancelTransition;
            }
            CancelTransition();
        }

        private void Update()
        {
            if (_active && _irisInStartedAt > float.NegativeInfinity &&
                Time.unscaledTime - _irisInStartedAt >= IrisInSeconds)
                CancelTransition();
        }

        private void HandleLivesChanged(int lives)
        {
            if (lives < _previousLives && lives > 0)
            {
                _active = true;
                _damageStartedAt = Time.unscaledTime;
                _irisInStartedAt = float.NegativeInfinity;
            }
            else if (lives <= 0)
            {
                CancelTransition();
            }
            _previousLives = lives;
        }

        private void HandleRecoveryCompleted()
        {
            if (!_active) return;
            _irisInStartedAt = Time.unscaledTime;
        }

        private void CancelTransition()
        {
            _active = false;
            _damageStartedAt = float.NegativeInfinity;
            _irisInStartedAt = float.NegativeInfinity;
        }

        private void OnGUI()
        {
            if (!_active || CurrentPhase == ProductionRecoveryIrisPhase.Damage ||
                Event.current.type != EventType.Repaint) return;

            Rect content = ProductionHudLayout.ContentRect(Screen.width, Screen.height);
            float openness = CurrentOpenness;
            int previousDepth = GUI.depth;
            Color previousColor = GUI.color;
            GUI.depth = -190;
            GUI.color = Color.black;

            if (openness <= 0.001f)
            {
                GUI.DrawTexture(content, Texture2D.whiteTexture);
            }
            else
            {
                Vector2 centre = NulScreenPosition(content);
                float scale = ProductionHudLayout.Scale(Screen.width, Screen.height);
                float step = Mathf.Max(1f, PixelStep * scale);
                float maxHalfWidth = Mathf.Max(centre.x - content.xMin,
                    content.xMax - centre.x);
                float maxHalfHeight = Mathf.Max(centre.y - content.yMin,
                    content.yMax - centre.y);
                float halfWidth = QuantizeUp(maxHalfWidth * openness, step);
                float halfHeight = QuantizeUp(maxHalfHeight * openness, step);
                Rect aperture = Rect.MinMaxRect(
                    Mathf.Max(content.xMin, centre.x - halfWidth),
                    Mathf.Max(content.yMin, centre.y - halfHeight),
                    Mathf.Min(content.xMax, centre.x + halfWidth),
                    Mathf.Min(content.yMax, centre.y + halfHeight));
                DrawOutside(content, aperture);
            }

            GUI.color = previousColor;
            GUI.depth = previousDepth;
        }

        private Vector2 NulScreenPosition(Rect content)
        {
            if (worldCamera == null) worldCamera = Camera.main;
            if (worldCamera == null || gameLoop == null ||
                gameLoop.Controller == null) return content.center;
            Vector3 viewport = worldCamera.WorldToViewportPoint(
                gameLoop.Controller.transform.position);
            return new Vector2(
                Mathf.Clamp(content.x + viewport.x * content.width,
                    content.xMin, content.xMax),
                Mathf.Clamp(content.y + (1f - viewport.y) * content.height,
                    content.yMin, content.yMax));
        }

        private static float QuantizeUp(float value, float step)
            => Mathf.Ceil(value / step) * step;

        private static void DrawOutside(Rect outer, Rect aperture)
        {
            DrawRect(new Rect(outer.x, outer.y, outer.width,
                Mathf.Max(0f, aperture.yMin - outer.yMin)));
            DrawRect(new Rect(outer.x, aperture.yMax, outer.width,
                Mathf.Max(0f, outer.yMax - aperture.yMax)));
            DrawRect(new Rect(outer.x, aperture.yMin,
                Mathf.Max(0f, aperture.xMin - outer.xMin), aperture.height));
            DrawRect(new Rect(aperture.xMax, aperture.yMin,
                Mathf.Max(0f, outer.xMax - aperture.xMax), aperture.height));
        }

        private static void DrawRect(Rect rect)
        {
            if (rect.width > 0.01f && rect.height > 0.01f)
                GUI.DrawTexture(rect, Texture2D.whiteTexture);
        }
    }
}
