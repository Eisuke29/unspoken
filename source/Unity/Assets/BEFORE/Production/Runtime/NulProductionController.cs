// BEFORE Goal 4A — BEFORE_ProductionV2 NUL vertical slice.
// Reuses the frozen Goal 2 receive path unchanged: Sidecar -> UDP ->
// CommandRouter (session + dedup) -> CommandBus -> NulState (D-113 immediate).
// This file adds only the NUL presentation/physics layer: Animator state, the
// standing/crouch collider swap, and the foot-anchored vertical squash.
// Recognition, thresholds, teaching, and the sidecar are NOT touched.
//
// Art: worker-a A-set only (D-138). 32x32 cells imported at PPU 1, so one world
// unit == one logical pixel and the D-114 course numbers are used verbatim.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Before.Production
{
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class NulProductionController : MonoBehaviour
    {
        // ---- D-114 course/game-feel constants (NOT changed by Goal 4A) ----
        public const float RunSpeed = 24f;      // logical px/s
        public const float JumpApex = 26f;      // logical px
        public const float AscentTime = 0.44f;  // s
        public const float DescentTime = 0.36f; // s  (total airtime 0.80 s)

        // Derived jump physics that reproduce the constants above exactly.
        public static float JumpVelocity => 2f * JumpApex / AscentTime;
        public static float AscentGravity => 2f * JumpApex / (AscentTime * AscentTime);
        public static float DescentGravity => 2f * JumpApex / (DescentTime * DescentTime);

        // ---- presentation constants ----
        public const float VisualScale = 1.2f;   // one common scale for every clip
        public const int FootOffsetPx = 4;       // opaque foot row sits 4 px above cell bottom
        public const float CrouchSquash = 0.78f;
        public const float SquashSeconds = 0.10f;
        public const float StandColliderH = 24f * VisualScale;
        public const float StandColliderW = 14f * VisualScale;
        public const float CrouchColliderRatio = 0.55f;
        public const float LandingClipSeconds = 4f / 15f;   // Land 4 @ 15 fps (D-117)
        public const float ReadinessFreshSeconds = 0.12f;
        public const float TrackingPacketFreshSeconds = 0.30f;

        public int udpPort = 8765;
        public int sidecarControlPort = SidecarTeachingControlClient.DefaultControlPort;
        public bool autoLaunchSidecar = false;   // production default; user enables
        public bool autoRun = true;              // empty-lot forward motion
        public bool MovementSuspended { get; private set; }
        public bool StorySuspended { get; private set; }
        public bool EnergySuspended { get; private set; }
        public float EnergyMovementScale { get; private set; } = 1f;

        // Frozen Goal 2 pieces, reused as-is.
        public readonly CommandRouter Router = new CommandRouter();
        public readonly CommandBus Bus = new CommandBus();
        public readonly NulState Nul = new NulState();

        public enum NulAnimState { Idle = 0, Run = 1, Jump = 2, Landing = 3, Stop = 4, Crouch = 5 }
        public NulAnimState State { get; private set; } = NulAnimState.Idle;

        [SerializeField] private Transform visualRoot;
        [SerializeField] private Animator animator;
        [SerializeField] private BoxCollider2D standingCollider;
        [SerializeField] private BoxCollider2D crouchCollider;
        [SerializeField] private Transform groundCheck;
        [SerializeField] private Transform effectsAnchor;
        [SerializeField] private LayerMask headBlockMask;

        private Rigidbody2D _rb;
        private UdpCommandReceiver _rx;
        private SidecarProcessManager _sidecar;
        private SidecarTeachingControlClient _teachingControl;
        private float[] _landmarks;
        private ProductionReadinessKind _readiness;
        private ProductionHandSignal _handSignal;
        private float _trackingQuality;
        private int _directionPreview;
        private float _directionHoldProgress;
        private string _recognitionOwner = "live";
        private LandmarkPacket.StopStatus _stopStatus;
        private float _lastReadinessAt = float.NegativeInfinity;
        private long _lastLandmarkTimestampUs = long.MinValue;
        private float _lastLandmarkReceivedAt = float.NegativeInfinity;
        private CommandPacket _lastStopCommand;
        private long _lastStopUdpReceivedUs;
        private long _lastStopStateEnteredUs;
        private int _presentationFacing = 1;

        private bool _airborne;
        private float _landingUntil;
        private float _squash = 1f;          // current vertical squash factor
        private float _groundY;              // world Y of the foot line when grounded
        private double _gameplayTime;
        private bool _pauseVelocityCaptured;
        private Vector2 _velocityBeforePause;

        private static readonly int StateHash = Animator.StringToHash("State");

        // ---- test / inspection surface ----
        public bool Airborne => _airborne;
        public bool CrouchColliderActive => crouchCollider != null && crouchCollider.enabled;
        public bool StandingColliderActive => standingCollider != null && standingCollider.enabled;
        public float SquashFactor => _squash;
        public float HorizontalSpeed => _rb != null ? _rb.linearVelocity.x : 0f;
        public bool ReceiverListening => _rx != null && _rx.Listening;
        public bool SidecarLaunchAttempted => _sidecar != null && _sidecar.LaunchAttempted;
        public bool SidecarRunning => _sidecar != null && _sidecar.IsRunning;
        public string SidecarStatus => _sidecar != null ? _sidecar.LastStatus : "not-created";
        public Transform EffectsAnchor => effectsAnchor;
        public float GameplayTime => (float)_gameplayTime;
        public bool HasLandmarkFrame => _landmarks != null && _landmarks.Length == 42;
        public float PacketAgeSeconds => _lastLandmarkReceivedAt >
            float.NegativeInfinity
                ? Mathf.Max(0f, Time.unscaledTime - _lastLandmarkReceivedAt)
                : float.PositiveInfinity;
        public bool LandmarkPacketStale =>
            PacketAgeSeconds > TrackingPacketFreshSeconds;
        public bool TrackedFresh => HasLandmarkFrame &&
            _handSignal == ProductionHandSignal.Ok && !LandmarkPacketStale;
        public float TrackingLossSeconds => TrackedFresh ? 0f :
            _lastLandmarkReceivedAt > float.NegativeInfinity
                ? PacketAgeSeconds : float.PositiveInfinity;
        public long LastLandmarkTimestampUs => _lastLandmarkTimestampUs;
        public bool HasReceivedLandmarkPacket =>
            _lastLandmarkTimestampUs > long.MinValue;
        public bool ReadinessFresh =>
            Time.unscaledTime - _lastReadinessAt <= ReadinessFreshSeconds;
        public ProductionReadinessKind Readiness => ReadinessFresh
            ? _readiness : ProductionReadinessKind.None;
        public ProductionHandSignal HandSignal => ReadinessFresh
            ? _handSignal
            : _lastReadinessAt > float.NegativeInfinity
                ? ProductionHandSignal.TrackingLost
                : ProductionHandSignal.Unknown;
        public SidecarTeachingControlClient TeachingControl => _teachingControl;
        public float TrackingQuality => ReadinessFresh ? _trackingQuality : 0f;
        public int DirectionPreview => ReadinessFresh ? _directionPreview : 0;
        public float DirectionHoldProgress => ReadinessFresh
            ? _directionHoldProgress : 0f;
        public string RecognitionOwner => ReadinessFresh
            ? _recognitionOwner : "tracking-recovery";
        public LandmarkPacket.StopStatus StopDiagnostics => _stopStatus;
        public CommandPacket LastStopCommand => _lastStopCommand;
        public long LastStopUdpReceivedUs => _lastStopUdpReceivedUs;
        public long LastStopStateEnteredUs => _lastStopStateEnteredUs;
        public int PresentationFacing => _presentationFacing;
        public float LastStopTransportMs => ElapsedMilliseconds(
            _lastStopCommand.TimestampUs, _lastStopUdpReceivedUs);
        public float LastStopUnityApplyMs => ElapsedMilliseconds(
            _lastStopUdpReceivedUs, _lastStopStateEnteredUs);

        public float StandingBottomY => standingCollider == null ? 0f
            : standingCollider.transform.position.y + standingCollider.offset.y - standingCollider.size.y * 0.5f;
        public float CrouchBottomY => crouchCollider == null ? 0f
            : crouchCollider.transform.position.y + crouchCollider.offset.y - crouchCollider.size.y * 0.5f;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _rb.gravityScale = 0f;               // gravity is applied explicitly below
            _rb.freezeRotation = true;
            _groundY = transform.position.y;
            _gameplayTime = Time.timeAsDouble;

            _rx = gameObject.AddComponent<UdpCommandReceiver>();
            _rx.port = udpPort;
            _sidecar = gameObject.AddComponent<SidecarProcessManager>();
            _sidecar.udpPort = udpPort;
            _teachingControl = gameObject.GetComponent<SidecarTeachingControlClient>();
            if (_teachingControl == null)
                _teachingControl = gameObject.AddComponent<SidecarTeachingControlClient>();
            _teachingControl.ControlPort = sidecarControlPort;
            _sidecar.controlPort = sidecarControlPort;

            ApplyCollider(false);
            ApplyVisual();
        }

        private void OnEnable()
        {
            Bus.OnCommand -= OnBusCommand;
            Bus.OnCommand += OnBusCommand;
            EnsureServices();
        }

        private void Start() => EnsureServices();

        private void EnsureServices()
        {
            if (_rx == null || _sidecar == null) return;
            if (_rx.Listening && _rx.port != udpPort) _rx.StopListening();
            _rx.port = udpPort;
            _sidecar.udpPort = udpPort;
            _sidecar.controlPort = sidecarControlPort;
            _teachingControl.ControlPort = sidecarControlPort;
            _rx.StartListening();
            if (autoLaunchSidecar && !_sidecar.IsRunning) _sidecar.Launch();
        }

        private void OnDisable()
        {
            Bus.OnCommand -= OnBusCommand;
            _rx?.StopListening();
            _sidecar?.Stop();
        }

        // Commands arrive already session-gated and deduped. NulState owns pause
        // suppression, Direction exclusivity, and the 0.75 s crouch refresh.
        private void OnBusCommand(CommandPacket p)
        {
            bool wasCrouching = Nul.Crouching;
            Nul.Apply(p, _gameplayTime);
            if (p.Kind == CommandKind.StopOn || p.Kind == CommandKind.StopOff)
            {
                _lastStopCommand = p;
                _lastStopStateEnteredUs = UtcNowUs();
            }

            if (Nul.Paused || Nul.DirectionMode || StorySuspended) return;

            // Jump starts only from the ground: an extra airborne JUMP is ignored
            // rather than queued, so there is no double jump.
            if (p.Kind == CommandKind.Jump && !_airborne && !Nul.Crouching)
            {
                _airborne = true;
                _rb.linearVelocity = new Vector2(_rb.linearVelocity.x, JumpVelocity);
            }
            if (p.Kind == CommandKind.Crouch && !wasCrouching) ApplyCollider(true);
        }

        private void Update()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null)
            {
                if (kb.pKey.wasPressedThisFrame) Nul.Paused = !Nul.Paused;
                if (kb.dKey.wasPressedThisFrame) Nul.DirectionMode = !Nul.DirectionMode;
            }
#endif
            IngestUdp();
            if (!Nul.Paused && !StorySuspended && !EnergySuspended)
            {
                _gameplayTime += Time.unscaledDeltaTime;
                Nul.Tick(_gameplayTime);
            }
            SyncPausePhysics();

            // A crouch that has timed out still holds while the head is blocked.
            if (!Nul.Crouching && CrouchColliderActive && !HeadBlocked()) ApplyCollider(false);

            float target = CrouchHeld ? CrouchSquash : 1f;
            if (!Nul.Paused && !StorySuspended && !EnergySuspended)
            {
                float step = Time.unscaledDeltaTime / SquashSeconds * (1f - CrouchSquash);
                _squash = Mathf.MoveTowards(_squash, target, step);
            }

            UpdateState();
            ApplyVisual();
        }

        private bool CrouchHeld => Nul.Crouching || (CrouchColliderActive && HeadBlocked());

        private void FixedUpdate()
        {
            if (Nul.Paused || StorySuspended || EnergySuspended)
            {
                _rb.linearVelocity = Vector2.zero;
                return;
            }
            float vx = (Nul.StopActive || Nul.Paused || MovementSuspended ||
                        EnergySuspended || !autoRun)
                ? 0f : RunSpeed * EnergyMovementScale;
            float vy = _rb.linearVelocity.y;

            if (_airborne)
            {
                float g = vy > 0f ? AscentGravity : DescentGravity;
                vy -= g * Time.fixedDeltaTime;
            }

            _rb.linearVelocity = new Vector2(vx, vy);

            // Empty-lot ground plane: the foot line never passes below _groundY, so
            // Jump -> Landing -> Run reconnects with no foot teleport.
            if (_airborne && transform.position.y <= _groundY && vy <= 0f)
            {
                _rb.position = new Vector2(_rb.position.x, _groundY);
                _rb.linearVelocity = new Vector2(vx, 0f);
                _airborne = false;
                _landingUntil = GameplayTime + LandingClipSeconds;
            }
        }

        private void UpdateState()
        {
            NulAnimState next;
            if (_airborne) next = NulAnimState.Jump;
            else if (GameplayTime < _landingUntil) next = NulAnimState.Landing;
            else if (CrouchHeld) next = NulAnimState.Crouch;
            else if (Nul.StopActive) next = NulAnimState.Stop;
            else if (Nul.Paused || StorySuspended || MovementSuspended ||
                     EnergySuspended || !autoRun) next = NulAnimState.Idle;
            else next = NulAnimState.Run;

            if (next == State) return;
            State = next;
            // Presentation is optional in hermetic/runtime fallback rigs. Unity logs
            // an error when SetInteger is called on an Animator without a controller.
            if (animator != null && animator.runtimeAnimatorController != null)
                animator.SetInteger(StateHash, (int)State);
        }

        // Scaling happens about VisualRoot's origin, and the sprite is offset so
        // its opaque foot row lands exactly on that origin — so the foot world Y
        // is invariant under the squash.
        private void ApplyVisual()
        {
            if (visualRoot == null) return;
            visualRoot.localScale = new Vector3(
                VisualScale * _presentationFacing, VisualScale * _squash, 1f);
            visualRoot.localPosition = new Vector3(0f, -FootOffsetPx * VisualScale * _squash, 0f);
        }

        private void SyncPausePhysics()
        {
            if (Nul.Paused && !_pauseVelocityCaptured)
            {
                _velocityBeforePause = _rb != null ? _rb.linearVelocity : Vector2.zero;
                if (_rb != null) _rb.linearVelocity = Vector2.zero;
                _pauseVelocityCaptured = true;
            }
            else if (!Nul.Paused && _pauseVelocityCaptured)
            {
                if (_rb != null && !MovementSuspended && !StorySuspended &&
                    !EnergySuspended)
                    _rb.linearVelocity = _velocityBeforePause;
                _pauseVelocityCaptured = false;
            }
        }

        private void ApplyCollider(bool crouch)
        {
            if (standingCollider == null || crouchCollider == null) return;
            standingCollider.enabled = !crouch;
            crouchCollider.enabled = crouch;
        }

        private bool HeadBlocked()
        {
            if (headBlockMask.value == 0) return false;
            float h = StandColliderH;
            var centre = (Vector2)transform.position + new Vector2(0f, h * 0.5f);
            return Physics2D.OverlapBox(centre, new Vector2(StandColliderW, h), 0f, headBlockMask) != null;
        }

        private void IngestUdp()
        {
            if (_rx == null) return;
            List<string> msgs = _rx.Poll();
            for (int i = 0; i < msgs.Count; i++) InjectDatagram(msgs[i]);
        }

        // Feed a raw datagram directly (tests, no socket). Session gate + dedup are
        // applied here exactly as in the live path.
        public void InjectDatagram(string json)
        {
            if (CommandPacket.TryParseCommand(json, out var pkt))
            {
                if (Router.Ingest(pkt))
                {
                    if (pkt.Kind == CommandKind.StopOn ||
                        pkt.Kind == CommandKind.StopOff)
                        _lastStopUdpReceivedUs = UtcNowUs();
                    Bus.Publish(pkt);
                }
            }
            else if (LandmarkPacket.TryParseStatus(json, out var status) &&
                     status.TimestampUs >= _lastLandmarkTimestampUs)
            {
                _lastLandmarkTimestampUs = status.TimestampUs;
                _lastLandmarkReceivedAt = Time.unscaledTime;
                _landmarks = status.Points != null && status.Points.Length == 42
                    ? status.Points : null;
                _readiness = status.Readiness;
                _handSignal = status.Signal;
                _trackingQuality = status.TrackingQuality;
                _directionPreview = status.DirectionPreview;
                _directionHoldProgress = status.DirectionHoldProgress;
                _recognitionOwner = status.RecognitionOwner;
                _stopStatus = status.Stop;
                _lastReadinessAt = Time.unscaledTime;
            }
        }

        /// <summary>
        /// Clears transient reaction/physics state at a recovery checkpoint.
        /// The receive, recognition, session, and dedup state deliberately stay intact.
        /// </summary>
        public void RecoverAt(Vector2 worldPosition)
        {
            _airborne = false;
            _landingUntil = 0f;
            _groundY = worldPosition.y;
            _squash = 1f;

            Nul.StopActive = false;
            Nul.Crouching = false;
            Nul.CrouchUntil = 0.0;
            Nul.DirectionMode = false;
            Nul.LastDirection = 0;
            Nul.Paused = false;
            _pauseVelocityCaptured = false;
            _velocityBeforePause = Vector2.zero;
            _presentationFacing = 1;
            EnergySuspended = false;
            EnergyMovementScale = 1f;

            ApplyCollider(false);
            transform.position = worldPosition;
            if (_rb != null)
            {
                _rb.position = worldPosition;
                _rb.linearVelocity = Vector2.zero;
            }

            UpdateState();
            ApplyVisual();
        }

        public void SetPresentationFacing(int direction)
        {
            _presentationFacing = direction < 0 ? -1 : 1;
            ApplyVisual();
        }

        public void SetMovementSuspended(bool suspended)
        {
            MovementSuspended = suspended;
            if (suspended && _rb != null)
                _rb.linearVelocity = new Vector2(0f, _rb.linearVelocity.y);
        }

        public void SetEnergySupply(bool suspended, float movementScale = 1f)
        {
            EnergySuspended = suspended;
            EnergyMovementScale = Mathf.Clamp01(movementScale);
            if (suspended && _rb != null)
                _rb.linearVelocity = Vector2.zero;
        }

        /// <summary>
        /// Freezes gameplay time and physical reaction while a story-exclusive
        /// interaction is active. UDP ingest, session gating, dedup, and command
        /// publication continue unchanged; this is deliberately separate from
        /// Pause so commands received during Teaching can be acknowledged.
        /// </summary>
        public void SetStorySuspended(bool suspended)
        {
            StorySuspended = suspended;
            if (suspended && _rb != null) _rb.linearVelocity = Vector2.zero;
        }

        public bool TryGetLandmark(int index, out Vector2 point)
        {
            if (index < 0 || index >= 21 || !HasLandmarkFrame)
            {
                point = default;
                return false;
            }
            point = new Vector2(_landmarks[index * 2], _landmarks[index * 2 + 1]);
            return true;
        }

        private static long UtcNowUs()
            => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000L;

        private static float ElapsedMilliseconds(long startUs, long endUs)
        {
            if (startUs <= 0L || endUs < startUs) return -1f;
            return (endUs - startUs) / 1000f;
        }
    }
}
