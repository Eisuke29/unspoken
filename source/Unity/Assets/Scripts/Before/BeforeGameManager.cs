using UnityEngine;
using UnityEngine.InputSystem;

namespace BeforeExperience
{
    [RequireComponent(typeof(CursorTrajectorySampler))]
    [RequireComponent(typeof(OnlineIntentPredictor))]
    [RequireComponent(typeof(FeedbackController))]
    [RequireComponent(typeof(TargetSpawner))]
    [RequireComponent(typeof(InterventionController))]
    [RequireComponent(typeof(BeforeHUD))]
    public sealed class BeforeGameManager : MonoBehaviour
    {
        private CursorTrajectorySampler sampler;
        private OnlineIntentPredictor predictor;
        private FeedbackController feedback;
        private TargetSpawner spawner;
        private InterventionController intervention;
        private BeforeHUD hud;
        private Camera viewCamera;

        public IntentFrame LatestFrame { get; private set; }

        private void Awake()
        {
            sampler = GetComponent<CursorTrajectorySampler>();
            predictor = GetComponent<OnlineIntentPredictor>();
            feedback = GetComponent<FeedbackController>();
            spawner = GetComponent<TargetSpawner>();
            intervention = GetComponent<InterventionController>();
            hud = GetComponent<BeforeHUD>();
        }

        private void Start()
        {
            viewCamera = Camera.main;
            feedback.Initialize();
            spawner.Initialize(viewCamera, feedback);
            hud.Initialize(this, predictor, intervention);
        }

        private void Update()
        {
            sampler.Tick();
            LatestFrame = predictor.BuildFrame(sampler.Motion, spawner.Targets);
            double now = Time.unscaledTimeAsDouble;

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.dKey.wasPressedThisFrame)
            {
                hud.ToggleDebug();
            }

            Mouse mouse = Mouse.current;
            bool clicked = mouse != null && mouse.leftButton.wasPressedThisFrame;
            if (clicked)
            {
                Vector2 screenPoint = mouse.position.ReadValue();
                if (IsOverResetButton(screenPoint))
                {
                    ResetAll();
                    return;
                }

                HandleGameplayClick(ScreenToWorld(screenPoint), now);
            }

            if (intervention.CheckTimeout(now))
            {
                hud.SetDebugEvent("AI timeout (no click)");
            }

            TryIntervene(now);
        }

        private void HandleGameplayClick(Vector2 worldPoint, double now)
        {
            if (intervention.HasPending)
            {
                if (intervention.IsWithinSavedRegion(worldPoint))
                {
                    predictor.Learn(intervention.SavedFrame, intervention.PendingTargetId);
                    hud.SetDebugEvent($"AI success on target {intervention.PendingTargetId}");
                    intervention.ResolveSuccess(now);
                    return;
                }

                TargetNode mistaken = spawner.FindTargetAtWorldPoint(worldPoint);
                if (mistaken != null)
                {
                    hud.SetDebugEvent(
                        $"AI failure: predicted {intervention.PendingTargetId}, clicked {mistaken.TargetId}");
                    intervention.ResolveFailure(now);
                    mistaken.React(BeforeEventSource.Human);
                    predictor.Learn(LatestFrame, mistaken.TargetId);
                }

                return;
            }

            TargetNode hit = spawner.FindTargetAtWorldPoint(worldPoint);
            if (hit == null)
            {
                return;
            }

            hit.React(BeforeEventSource.Human);
            predictor.Learn(LatestFrame, hit.TargetId);
        }

        private void TryIntervene(double now)
        {
            if (intervention.HasPending || LatestFrame == null)
            {
                return;
            }

            TargetNode best = FindTargetById(LatestFrame.BestTargetId);
            if (best == null)
            {
                return;
            }

            if (intervention.TryBegin(LatestFrame, sampler.Motion, predictor, best, now))
            {
                best.React(BeforeEventSource.AI);
                hud.SetDebugEvent($"AI intervention on target {best.TargetId}");
            }
        }

        public void ResetAll()
        {
            predictor.ResetModel();
            sampler.ResetSampler();
            intervention.ResetState();
            spawner.ResetLayout();
            hud.ResetHud();
        }

        private TargetNode FindTargetById(int id)
        {
            var targets = spawner.Targets;
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i] != null && targets[i].TargetId == id)
                {
                    return targets[i];
                }
            }

            return null;
        }

        private bool IsOverResetButton(Vector2 screenPoint)
        {
            Vector2 guiPoint = new Vector2(screenPoint.x, Screen.height - screenPoint.y);
            return hud.ResetButtonRect.Contains(guiPoint);
        }

        private Vector2 ScreenToWorld(Vector2 screenPoint)
        {
            if (viewCamera == null)
            {
                return Vector2.zero;
            }

            Vector3 world = viewCamera.ScreenToWorldPoint(
                new Vector3(screenPoint.x, screenPoint.y, -viewCamera.transform.position.z));
            return new Vector2(world.x, world.y);
        }
    }
}
