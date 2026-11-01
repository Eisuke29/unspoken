using Before.Audio;
using Before.Capture;
using Before.Config;
using Before.Course;
using Before.Presentation;
using Before.Recognition;
using Unity.InferenceEngine;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Before.App
{
    /// <summary>
    /// Composition root. Validates required Inspector references in Awake, constructs the plain C#
    /// director and views, adapts Unity input/time/rendering, and performs ordered shutdown.
    /// No gameplay component performs per-frame Find lookups after composition.
    /// </summary>
    public sealed class BeforeApp : MonoBehaviour
    {
        [Header("Configs")]
        [SerializeField] private BeforeRuntimeConfig runtimeConfig;
        [SerializeField] private BeforeRecognitionConfig recognitionConfig;
        [SerializeField] private BeforeCourseConfig courseConfig;
        [SerializeField] private BeforePresentationConfig presentationConfig;

        [Header("Pinned models")]
        [SerializeField] private ModelAsset handDetector;
        [SerializeField] private ModelAsset handLandmarker;
        [SerializeField] private TextAsset anchorsCsv;

        [Header("Generated art")]
        [SerializeField] private Texture2D nulSideSheet;
        [SerializeField] private Texture2D worldTiles;
        [SerializeField] private Texture2D hazardsSheet;
        [SerializeField] private Texture2D bgFar;
        [SerializeField] private Texture2D bgMid;
        [SerializeField] private Texture2D bgNear;

        [Header("Typography")]
        [SerializeField] private Font dotGothic;

        [Header("Mode")]
        [SerializeField] private bool useFakeHandStream;

        public BeforeDirector Director { get; private set; }
        public FakeHandSource FakeHand { get; private set; }

        private SystemAudio audioSystem;
        private BeforePresenter presenter;
        private NulView nulView;
        private WorldView worldView;
        private UnityEngine.Camera worldCamera;
        private UnityEngine.Camera displayCamera;
        private RenderTexture worldRT;
        private FinalRoute lastBuiltRoute = FinalRoute.None;
        private float smoothedFps = 60f;

        private void Awake()
        {
            string missing = null;
            if (runtimeConfig == null) missing = nameof(runtimeConfig);
            else if (recognitionConfig == null) missing = nameof(recognitionConfig);
            else if (courseConfig == null) missing = nameof(courseConfig);
            else if (presentationConfig == null) missing = nameof(presentationConfig);
            else if (nulSideSheet == null) missing = nameof(nulSideSheet);
            else if (worldTiles == null) missing = nameof(worldTiles);
            else if (hazardsSheet == null) missing = nameof(hazardsSheet);
            else if (bgFar == null || bgMid == null || bgNear == null) missing = "background strips";
            else if (dotGothic == null) missing = nameof(dotGothic);
            else if (!useFakeHandStream && (handDetector == null || handLandmarker == null || anchorsCsv == null)) missing = "hand model assets";
            if (missing != null)
            {
                Debug.LogError($"[BEFORE] Missing required reference: {missing}. Gameplay disabled.");
                enabled = false;
                return;
            }

            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            Cursor.visible = false;

            IHandSignalSource hand;
            if (useFakeHandStream)
            {
                FakeHand = new FakeHandSource();
                hand = FakeHand;
            }
            else
            {
                hand = new WebcamHandSource(runtimeConfig, handDetector, handLandmarker, anchorsCsv,
                    new OneEuroFilter2(recognitionConfig.oneEuroMinCutoff, recognitionConfig.oneEuroBeta, recognitionConfig.oneEuroDCutoff));
            }

            Director = new BeforeDirector(runtimeConfig, recognitionConfig, courseConfig, hand);
            audioSystem = gameObject.AddComponent<SystemAudio>();
            audioSystem.Initialize();
            Director.OnCue += audioSystem.Play;
            Director.RequestEditorExit = StopPlayModeViaAdapter;
            Director.OnFullReset += HandleFullReset;

            BuildRendering();
            presenter = new BeforePresenter(presentationConfig, dotGothic);
            nulView = new NulView(nulSideSheet, transform);
            worldView = new WorldView(courseConfig, worldTiles, hazardsSheet, bgFar, bgMid, bgNear, transform);
            worldView.BuildStatic(Director.Course);
            worldView.RebuildBranch(Director.Course);
        }

        private void BuildRendering()
        {
            worldRT = new RenderTexture(runtimeConfig.logicalWidth, runtimeConfig.logicalHeight, 16)
            {
                filterMode = FilterMode.Point
            };
            var camGo = new GameObject("LogicalWorldCamera");
            camGo.transform.SetParent(transform, false);
            worldCamera = camGo.AddComponent<UnityEngine.Camera>();
            worldCamera.orthographic = true;
            worldCamera.orthographicSize = runtimeConfig.logicalHeight / 16f / 2f; // 5.625 units
            worldCamera.clearFlags = CameraClearFlags.SolidColor;
            worldCamera.backgroundColor = presentationConfig.voidBlack;
            worldCamera.targetTexture = worldRT; // renders the logical world to the RenderTexture only

            // Fix A (Display 1): a dedicated display camera outputs to Display 1 (targetTexture=null) so the
            // Game view never shows "No cameras rendering". It clears to void-black and renders nothing itself
            // (cullingMask Nothing) - the 320x180 frame + HUD are drawn once by the IMGUI presenter on top,
            // so the logical world is never double-drawn. Exactly one AudioListener lives here.
            var dispGo = new GameObject("DisplayCamera");
            dispGo.transform.SetParent(transform, false);
            dispGo.tag = "MainCamera";
            dispGo.AddComponent<AudioListener>();
            displayCamera = dispGo.AddComponent<UnityEngine.Camera>();
            displayCamera.orthographic = true;
            displayCamera.orthographicSize = runtimeConfig.logicalHeight / 16f / 2f;
            displayCamera.clearFlags = CameraClearFlags.SolidColor;
            displayCamera.backgroundColor = presentationConfig.voidBlack;
            displayCamera.cullingMask = 0; // render nothing; IMGUI overlays the frame
            displayCamera.targetTexture = null;
            displayCamera.targetDisplay = 0;
            displayCamera.depth = 0;
        }

        private void HandleFullReset()
        {
            lastBuiltRoute = FinalRoute.None;
            worldView.RebuildBranch(Director.Course);
        }

        private void Update()
        {
            var kb = Keyboard.current;
            bool space = kb != null && kb.spaceKey.isPressed;
            bool pauseToggle = kb != null && kb.pKey.wasPressedThisFrame;
            bool resetHeld = kb != null && kb.rKey.isPressed;
            bool debugToggle = kb != null && kb.f1Key.wasPressedThisFrame;
            bool muteToggle = kb != null && kb.mKey.wasPressedThisFrame;
            bool esc = kb != null && kb.escapeKey.wasPressedThisFrame;
            bool enter = kb != null && kb.enterKey.wasPressedThisFrame;
            if (muteToggle) audioSystem.SetMuted(!audioSystem.Muted);

            // F2 operator Recognition QA (never part of the public experience). J/S set the expected label.
            if (kb != null && kb.f2Key.wasPressedThisFrame) Director.ToggleRecognitionQa();
            if (kb != null && kb.jKey.wasPressedThisFrame) Director.SetQaExpected(Before.Recognition.SymbolicClass.Jump);
            if (kb != null && kb.sKey.wasPressedThisFrame) Director.SetQaExpected(Before.Recognition.SymbolicClass.Stop);

            Director.Tick(Time.realtimeSinceStartupAsDouble, space, pauseToggle, resetHeld, debugToggle, muteToggle, esc, enter);

            smoothedFps = Mathf.Lerp(smoothedFps, 1f / Mathf.Max(Time.unscaledDeltaTime, 1e-4f), 0.05f);

            if (Director.Course.SelectedRoute != lastBuiltRoute)
            {
                lastBuiltRoute = Director.Course.SelectedRoute;
                worldView.RebuildBranch(Director.Course);
            }

            nulView.UpdateFrom(Director, Time.realtimeSinceStartupAsDouble);
            worldView.Tick(Director.Gameplay.Now, worldCamera.transform.position.x);
            UpdateCore();
            UpdateCameraFollow();
            UpdateNulCoreHud();
        }

        private void UpdateCore()
        {
            var s = Director.Story.State;
            if (s == BeforeStoryState.CoreTransfer)
            {
                double inState = 0;
                float align = Mathf.Clamp01((float)(Director.Gameplay.Now % 100.0) * 0f + 1f);
                worldView.SetCoreActivation(1f, Mathf.PingPong(Time.realtimeSinceStartup * 2f, 1f));
                _ = inState; _ = align;
            }
            else if (s == BeforeStoryState.Farewell || s == BeforeStoryState.CompleteHold)
            {
                worldView.SetCoreActivation(1f, 0f);
            }
        }

        private void UpdateCameraFollow()
        {
            // NUL foot contact at logical (96,144): camera x = nul.x + offset so nul sits at logical x=96
            float halfWidth = runtimeConfig.logicalWidth / 16f / 2f; // 10 units
            float offset = halfWidth - 96f / 16f; // 4 units
            float target = Mathf.Max(Director.Motor.Position.x + offset, halfWidth - 2f);
            float snapped = Mathf.Round(target * 16f) / 16f;
            // fixed vertical framing: place grounded NUL foot (world y=0) at logical y=144.
            // worldY(v) = camY + (v-0.5)*2*ortho; solve camY so worldY=0 at footViewport=1-144/H.
            float footViewport = 1f - 144f / runtimeConfig.logicalHeight;
            float camY = (0.5f - footViewport) * 2f * worldCamera.orthographicSize;
            worldCamera.transform.position = new Vector3(snapped, camY, -10f);
        }

        private void UpdateNulCoreHud()
        {
            // project NUL's live world position to logical screen coords; core center is ~20px above the foot pivot
            Vector3 vp = worldCamera.WorldToViewportPoint(nulView.Root.transform.position);
            float lx = vp.x * runtimeConfig.logicalWidth;
            float ly = (1f - vp.y) * runtimeConfig.logicalHeight - 20f;
            presenter.SetNulCore(new Vector2(lx, ly));
        }

        private void OnGUI()
        {
            if (presenter == null || Director == null) return;
            presenter.Draw(Director, worldRT, Time.realtimeSinceStartupAsDouble, smoothedFps);
        }

        private void StopPlayModeViaAdapter()
        {
            // Editor-only exit bridge without an unconditional UnityEditor reference
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void OnDestroy()
        {
            Director?.Hand?.Dispose();
            if (worldRT != null)
            {
                if (worldCamera != null) worldCamera.targetTexture = null;
                worldRT.Release();
                Destroy(worldRT);
            }
            Cursor.visible = true;
        }
    }
}
