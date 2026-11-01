using UnityEngine;

namespace BeforeExperience
{
    public sealed class BeforeHUD : MonoBehaviour
    {
        private const string IntroMessage = "気になる光に触れてください";
        private const float IntroHoldSeconds = 3f;
        private const float IntroFadeSeconds = 2f;

        private BeforeGameManager manager;
        private OnlineIntentPredictor predictor;
        private InterventionController intervention;
        private Font uiFont;
        private GUIStyle introStyle;
        private GUIStyle buttonStyle;
        private GUIStyle debugStyle;
        private float introStartTime;
        private string lastDebugEvent = "";

        public bool DebugVisible { get; private set; }

        public Rect ResetButtonRect => new Rect(Screen.width - 122f, 14f, 106f, 34f);

        public void Initialize(
            BeforeGameManager gameManager,
            OnlineIntentPredictor predictorComponent,
            InterventionController interventionComponent)
        {
            manager = gameManager;
            predictor = predictorComponent;
            intervention = interventionComponent;
            uiFont = Font.CreateDynamicFontFromOSFont(
                new[] { "Yu Gothic UI", "Meiryo UI", "Meiryo", "MS Gothic" }, 22);
            ResetHud();
        }

        public void ToggleDebug()
        {
            DebugVisible = !DebugVisible;
        }

        public void SetDebugEvent(string text)
        {
            lastDebugEvent = text ?? "";
        }

        public void ResetHud()
        {
            introStartTime = Time.unscaledTime;
            lastDebugEvent = "";
        }

        private void EnsureStyles()
        {
            if (introStyle != null)
            {
                return;
            }

            introStyle = new GUIStyle
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 26
            };
            buttonStyle = new GUIStyle
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 16
            };
            debugStyle = new GUIStyle
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = 14,
                wordWrap = false
            };

            if (uiFont != null)
            {
                introStyle.font = uiFont;
                buttonStyle.font = uiFont;
                debugStyle.font = uiFont;
            }
        }

        private void OnGUI()
        {
            EnsureStyles();
            DrawIntro();
            DrawResetButton();
            if (DebugVisible)
            {
                DrawDebugPanel();
            }
        }

        private void DrawIntro()
        {
            float elapsed = Time.unscaledTime - introStartTime;
            float alpha = elapsed <= IntroHoldSeconds
                ? 1f
                : 1f - Mathf.Clamp01((elapsed - IntroHoldSeconds) / IntroFadeSeconds);
            if (alpha <= 0f)
            {
                return;
            }

            introStyle.normal.textColor = new Color(0.82f, 0.88f, 0.98f, alpha);
            Rect rect = new Rect(0f, Screen.height * 0.44f, Screen.width, 44f);
            GUI.Label(rect, IntroMessage, introStyle);
        }

        private void DrawResetButton()
        {
            Rect rect = ResetButtonRect;
            Color previous = GUI.color;
            GUI.color = new Color(0.18f, 0.24f, 0.36f, 0.85f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
            buttonStyle.normal.textColor = new Color(0.85f, 0.9f, 1f, 0.95f);
            GUI.Label(rect, "RESET", buttonStyle);
        }

        private void DrawDebugPanel()
        {
            IntentFrame frame = manager != null ? manager.LatestFrame : null;
            System.Text.StringBuilder text = new System.Text.StringBuilder(512);
            text.AppendLine("DEBUG (D)");

            if (predictor != null)
            {
                text.AppendLine($"learned: {predictor.LearnedSelectionCount}  maturity: {predictor.Maturity:F3}");
                if (intervention != null)
                {
                    text.AppendLine(
                        $"threshold: {intervention.CurrentConfidenceThreshold(predictor.Maturity):F3}  " +
                        $"lead: {intervention.CurrentLeadTime(predictor.Maturity):F3}s");
                }
            }

            if (intervention != null)
            {
                text.AppendLine(
                    $"interventions: {intervention.InterventionCount}  ok: {intervention.SuccessCount}  " +
                    $"miss: {intervention.FailureCount}  timeout: {intervention.TimeoutCount}");
                text.AppendLine(intervention.HasPending
                    ? $"pending: target {intervention.PendingTargetId}"
                    : "pending: none");
            }

            if (frame != null)
            {
                for (int id = 0; id < OnlineIntentPredictor.CandidateCount; id++)
                {
                    string marker = id == frame.BestTargetId ? "*" : " ";
                    text.AppendLine($"{marker}p[{id}] = {frame.Probabilities[id]:F3}  eta {frame.Metrics[id].EstimatedArrivalTime:F2}s");
                }
            }

            if (!string.IsNullOrEmpty(lastDebugEvent))
            {
                text.AppendLine($"last: {lastDebugEvent}");
            }

            Rect panel = new Rect(12f, 12f, 380f, 260f);
            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = previous;
            debugStyle.normal.textColor = new Color(0.8f, 0.92f, 0.8f, 1f);
            GUI.Label(new Rect(panel.x + 8f, panel.y + 6f, panel.width - 16f, panel.height - 12f), text.ToString(), debugStyle);
        }
    }
}
