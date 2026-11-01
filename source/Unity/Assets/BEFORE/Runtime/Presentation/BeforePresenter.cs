using System.Collections.Generic;
using Before.App;
using Before.Capture;
using Before.Config;
using Before.Course;
using Before.Planning;
using Before.Recognition;
using UnityEngine;

namespace Before.Presentation
{
    /// <summary>
    /// IMGUI presentation of the logical 320x180 frame: world render target, system text,
    /// hand panel showing only the 21-landmark skeleton on void-black (D-088; never camera RGB),
    /// orb queue, energy link, fork map, operator overlays, and the read-only F1 debug overlay.
    /// Observes the director; decides nothing.
    /// </summary>
    public sealed class BeforePresenter
    {
        private readonly BeforePresentationConfig cfg;
        private readonly Font font;
        private readonly Texture2D white;
        private readonly Texture2D panelTexture;
        private readonly Color32[] panelPixels;
        private GUIStyle statusStyle;
        private GUIStyle instructionStyle;
        private GUIStyle debugStyle;
        private double textShownAt;
        private PublicTextId lastTextId = PublicTextId.None;
        private readonly List<Vector2> energySeeds = new List<Vector2>();
        private System.Random energyRng = new System.Random(1);
        private Vector2 nulCoreLogical = BeforeLayout.NulCore;

        /// <summary>Set the live on-screen NUL core position (logical coords) so energy connects to NUL's body, including jumps.</summary>
        public void SetNulCore(Vector2 logical) => nulCoreLogical = logical;

        public BeforePresenter(BeforePresentationConfig presentationCfg, Font dotGothic)
        {
            cfg = presentationCfg;
            font = dotGothic;
            white = Texture2D.whiteTexture;
            panelTexture = new Texture2D(BeforeLayout.PanelW, BeforeLayout.PanelH, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point
            };
            panelPixels = new Color32[BeforeLayout.PanelW * BeforeLayout.PanelH];
            // D-088: the hand panel background is always pure void-black; no camera RGB is ever shown.
            Color32 vb = cfg.voidBlack;
            for (int i = 0; i < panelPixels.Length; i++) panelPixels[i] = vb;
            panelTexture.SetPixels32(panelPixels);
            panelTexture.Apply(false, false);
        }

        // Standard BlazeHand/MediaPipe 21-landmark connection topology.
        private static readonly int[,] HandConnections =
        {
            {0,1},{1,2},{2,3},{3,4},
            {0,5},{5,6},{6,7},{7,8},
            {5,9},{9,10},{10,11},{11,12},
            {9,13},{13,14},{14,15},{15,16},
            {13,17},{17,18},{18,19},{19,20},
            {0,17}
        };
        private static readonly int[] PalmAnchorIdx = {0,5,9,13,17};
        private static readonly int[] FingertipIdx = {4,8,12,16,20};

        private int styleS = -1;
        private void EnsureStyles()
        {
            if (statusStyle != null && styleS == S) return;
            styleS = S;
            // font sizes in logical px * integer scale (5 logical -> 30 px at 1920x1080; 4 logical for debug)
            statusStyle = new GUIStyle { fontSize = BeforeLayout.SystemTextFontPx * S, normal = { textColor = cfg.dirtyWhite }, clipping = TextClipping.Overflow };
            instructionStyle = new GUIStyle { fontSize = BeforeLayout.SystemTextFontPx * S, normal = { textColor = cfg.signalCyan }, clipping = TextClipping.Overflow };
            debugStyle = new GUIStyle { fontSize = BeforeLayout.DebugFontPx * S, normal = { textColor = cfg.dirtyWhite }, clipping = TextClipping.Overflow };
            if (font != null)
            {
                statusStyle.font = font;
                instructionStyle.font = font;
                debugStyle.font = font;
            }
        }

        private int S = 6; // logical->screen integer scale (6 at 1920x1080), recomputed each frame in Draw
        private Rect L(float x, float y, float w, float h) => new Rect(x * S + originX, y * S + originY, w * S, h * S);
        private float originX;
        private float originY;

        public void Draw(BeforeDirector director, RenderTexture worldRT, double realNow, float uiFps)
        {
            // centered, integer-scaled logical viewport with void-black letterboxing (Fix B / LogicalViewport)
            S = LogicalViewport.IntegerScale(Screen.width, Screen.height);
            EnsureStyles(); // rebuilds font sizes when the integer scale changes
            var view = LogicalViewport.Viewport(Screen.width, Screen.height);
            float viewW = view.width, viewH = view.height;
            originX = view.x;
            originY = view.y;

            GUI.color = cfg.voidBlack;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), white);
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(originX, originY, viewW, viewH), worldRT);

            // world dim behind map
            bool mapOpen = director.Story.State == BeforeStoryState.DirectionTeachMap || director.Story.State == BeforeStoryState.FinalPlanMap;
            if (mapOpen)
            {
                GUI.color = new Color(0f, 0f, 0f, 0.45f);
                GUI.DrawTexture(new Rect(originX, originY, viewW, viewH), white);
                GUI.color = Color.white;
            }

            DrawSystemText(director, realNow);
            DrawHandPanel(director, realNow);
            DrawEnergy(director, realNow); // behind the queue so orbs stay readable over the connection line
            DrawQueue(director, realNow);
            if (mapOpen) DrawMap(director, realNow);
            DrawOperator(director, realNow);
            if (director.Story.State == BeforeStoryState.Farewell || director.Story.State == BeforeStoryState.CompleteHold)
            {
                DrawFarewellTraces(director, realNow);
            }
            if (director.DebugVisible) DrawDebug(director, realNow, uiFps);
            if (director.RecognitionQaActive) DrawRecognitionQa(director);
        }

        // F2 operator Recognition QA overlay (never shown unless the operator opens it; the expected label
        // is only a tally input, never a classifier input). D-091.
        private void DrawRecognitionQa(BeforeDirector d)
        {
            var panel = L(8, 8, 176, 104);
            GUI.color = new Color(cfg.charcoal.r, cfg.charcoal.g, cfg.charcoal.b, 0.92f);
            GUI.DrawTexture(panel, white);
            GUI.color = Color.white;
            string exp = d.QaExpected == SymbolicClass.Jump ? "JUMP(J)" : "STOP(S)";
            // A/B/C/D compound QA: A=correct-class fire; x=wrong-class fire; nf=no-fire; arm=reached PoseArmed
            var lines = new[]
            {
                $"F2 QA  expect:{exp}",
                $"last chosen:{d.QaLastChosen} armed:{(d.QaLastArmed ? 1 : 0)}",
                $"reject:{d.QaLastReject} ttt:{d.QaLastTimeToTriggerMs:F0}ms",
                $"JUMP A:{d.QaCorrect(SymbolicClass.Jump)} x:{d.QaCross(SymbolicClass.Jump)} nf:{d.QaNoFire(SymbolicClass.Jump)} arm:{d.QaArmed(SymbolicClass.Jump)} /{d.QaTrials(SymbolicClass.Jump)}",
                $"STOP A:{d.QaCorrect(SymbolicClass.Stop)} x:{d.QaCross(SymbolicClass.Stop)} nf:{d.QaNoFire(SymbolicClass.Stop)} arm:{d.QaArmed(SymbolicClass.Stop)} /{d.QaTrials(SymbolicClass.Stop)}",
            };
            int x = 10, y = 10;
            for (int i = 0; i < lines.Length; i++)
                GUI.Label(L(x, y + i * BeforeLayout.DebugLineStep, 172, BeforeLayout.DebugLineStep + 2), lines[i], debugStyle);
        }

        // ------------------------------------------------------------ text

        private static (string, string) TextLines(PublicTextId id)
        {
            switch (id)
            {
                case PublicTextId.BootWait: return (BeforeTextCatalog.BOOT_WAIT_1, BeforeTextCatalog.BOOT_WAIT_2);
                case PublicTextId.CameraDenied: return (BeforeTextCatalog.CAMERA_DENIED_1, BeforeTextCatalog.CAMERA_DENIED_2);
                case PublicTextId.LowLight: return (BeforeTextCatalog.LOW_LIGHT_1, BeforeTextCatalog.LOW_LIGHT_2);
                case PublicTextId.Linking: return (BeforeTextCatalog.LINKING_1, null);
                case PublicTextId.LinkOk: return (BeforeTextCatalog.LINK_OK_1, null);
                case PublicTextId.EdgeWarn: return (BeforeTextCatalog.EDGE_WARN_1, BeforeTextCatalog.EDGE_WARN_2);
                case PublicTextId.SignalLost: return (BeforeTextCatalog.SIGNAL_LOST_1, BeforeTextCatalog.SIGNAL_LOST_2);
                case PublicTextId.TeachBegin: return (BeforeTextCatalog.TEACH_BEGIN_1, BeforeTextCatalog.TEACH_BEGIN_2);
                case PublicTextId.PoseConfirm: return (BeforeTextCatalog.POSE_CONFIRM_1, BeforeTextCatalog.POSE_CONFIRM_2);
                case PublicTextId.SampleOne: return (BeforeTextCatalog.SAMPLE_ONE_1, BeforeTextCatalog.SAMPLE_ONE_2);
                case PublicTextId.SampleShort: return (BeforeTextCatalog.SAMPLE_SHORT_1, BeforeTextCatalog.SAMPLE_SHORT_2);
                case PublicTextId.SampleConflict: return (BeforeTextCatalog.SAMPLE_CONFLICT_1, BeforeTextCatalog.SAMPLE_CONFLICT_2);
                case PublicTextId.SignalOverlap: return (BeforeTextCatalog.SIGNAL_OVERLAP_1, BeforeTextCatalog.SIGNAL_OVERLAP_2);
                case PublicTextId.Learned: return (BeforeTextCatalog.LEARNED_1, null);
                case PublicTextId.QueueFull: return (BeforeTextCatalog.QUEUE_FULL_1, null);
                case PublicTextId.PlanWait: return (BeforeTextCatalog.PLAN_WAIT_1, BeforeTextCatalog.PLAN_WAIT_2);
                case PublicTextId.PlanFinal: return (BeforeTextCatalog.PLAN_FINAL_1, BeforeTextCatalog.PLAN_FINAL_2);
                case PublicTextId.Rewind: return (BeforeTextCatalog.REWIND_1, null);
                case PublicTextId.Independent: return (BeforeTextCatalog.INDEPENDENT_1, BeforeTextCatalog.INDEPENDENT_2);
                case PublicTextId.Complete: return (BeforeTextCatalog.COMPLETE_1, null);
                case PublicTextId.Paused: return (BeforeTextCatalog.PAUSED_1, null);
                case PublicTextId.EndReset: return (BeforeTextCatalog.END_RESET_1, null);
                case PublicTextId.ExitConfirm: return (BeforeTextCatalog.EXIT_CONFIRM_1, BeforeTextCatalog.EXIT_CONFIRM_2);
                default: return (null, null);
            }
        }

        private void DrawSystemText(BeforeDirector director, double realNow)
        {
            var id = director.VisibleText;
            if (id != lastTextId)
            {
                lastTextId = id;
                textShownAt = realNow;
            }
            string status, instruction;
            if (id == PublicTextId.TeachRecord)
            {
                // adaptive 3+1 running count "動作を記録 N/M" (D-091)
                status = $"{BeforeTextCatalog.TEACH_RECORD_1} {director.Recognition.TeachCollected}/{director.Recognition.TeachTarget}";
                instruction = BeforeTextCatalog.TEACH_RECORD_2;
            }
            else
            {
                (status, instruction) = TextLines(id);
            }
            if (status == null) return;

            // D-089 system text: x=12, w=220 (right edge x=232), two lines at y=10 and y=17, 5 logical px.
            // Horizontal scan wipe over 0.12 s clips the group width; height is generous + Overflow so the
            // glyph top/bottom are never cut.
            float wipe = Mathf.Clamp01((float)((realNow - textShownAt) / cfg.textScanWipeSeconds));
            float clip = BeforeLayout.SystemText.width * wipe;
            DrawScanLine(status, BeforeLayout.SystemTextRow1Y, clip, statusStyle);
            if (instruction != null)
            {
                bool warn = id == PublicTextId.EdgeWarn || id == PublicTextId.LowLight || id == PublicTextId.SignalLost;
                instructionStyle.normal.textColor = warn ? cfg.waitAmber : cfg.signalCyan;
                DrawScanLine(instruction, BeforeLayout.SystemTextRow2Y, clip, instructionStyle);
            }
        }

        private void DrawScanLine(string text, int logicalY, float clipWidthLogical, GUIStyle style)
        {
            float lineH = BeforeLayout.SystemTextLineStep + 1; // logical; room for descenders
            GUI.BeginGroup(L(BeforeLayout.SystemText.x, logicalY, clipWidthLogical, lineH));
            GUI.Label(new Rect(0, 0, BeforeLayout.SystemText.width * S, lineH * S), text, style);
            GUI.EndGroup();
        }

        // ------------------------------------------------------------ hand panel

        // D-088/D-089: 48x64 panel at x=264,y=36. Pure void-black background + 21-landmark skeleton only.
        // No camera RGB is ever displayed (public or F1); WebCamTexture is Sentis input only. Landmarks are
        // the actual mirrored in-camera normalized positions mapped directly into the panel (no per-frame
        // bounding-box fit). All skeleton drawing is clipped to the panel so nothing escapes its border.
        private void DrawHandPanel(BeforeDirector director, double realNow)
        {
            var panelRect = L(BeforeLayout.PanelX, BeforeLayout.PanelY, BeforeLayout.PanelW, BeforeLayout.PanelH);

            // void-black background (never any camera imagery)
            GUI.DrawTexture(panelRect, panelTexture);

            // stepped border: dirty white; amber inside safe margin; fault red after sustained loss
            Color border = cfg.dirtyWhite;
            if (director.Recovery == TrackingRecoveryState.SuspendedRewound || director.Recovery == TrackingRecoveryState.Collapsed)
            {
                border = cfg.faultRed;
            }
            else if (director.Hand.Tracked
                && LogicalViewport.NearSafeBorder(director.Hand.PalmMirrored, BeforeLayout.PanelW, BeforeLayout.PanelSafeMargin))
            {
                border = cfg.waitAmber;
            }
            DrawBorder(panelRect, border);

            // clip every landmark mark to the panel interior
            GUI.BeginGroup(panelRect);
            var local = new Rect(0, 0, panelRect.width, panelRect.height);

            var lms = director.Hand.LandmarksMirrored;
            if (director.Hand.Tracked && lms != null && lms.Length >= 21)
            {
                for (int i = 0; i < HandConnections.GetLength(0); i++)
                {
                    DrawPanelLine(local, lms[HandConnections[i, 0]], lms[HandConnections[i, 1]], cfg.dirtyWhite);
                }
                for (int i = 0; i < 21; i++) DrawPanelDot(local, lms[i], cfg.dirtyWhite, 1);
                DrawPanelDot(local, lms[0], cfg.signalCyan, 1); // wrist
                foreach (int i in FingertipIdx) DrawPanelDot(local, lms[i], cfg.signalCyan, 1);
                DrawPanelDot(local, LogicalViewport.PalmCenter(lms), cfg.signalCyan, 2); // computed palm center
            }

            // teaching trace (luminous trajectory during Space capture)
            var capture = director.Recognition.CurrentTeachingCapture;
            if (director.Recognition.TeachingCaptureOpen && capture.Count > 1)
            {
                for (int i = 1; i < capture.Count; i++)
                {
                    DrawPanelDot(local, capture[i].Position, cfg.signalCyan, 1);
                }
            }

            // D-092 Jump pose half-ring + D-095 Stop static-sign half-ring (hand panel only; never the energy)
            if (director.Hand.Tracked && lms != null && lms.Length >= 21)
            {
                Vector2 palmN = LogicalViewport.PalmCenter(lms);
                DrawPoseRing(local, palmN, director.Recognition.LastPcm);
                DrawStopRing(local, palmN, director.Recognition.StopSign.State);
            }
            GUI.EndGroup();
        }

        // D-095: the Stop static sign's half-ring: candidate (forming) -> held (closed) -> releasing (opening).
        private void DrawStopRing(Rect local, Vector2 palmNorm, Recognition.StaticState state)
        {
            float sweep;
            switch (state)
            {
                case Recognition.StaticState.Candidate: sweep = 0.5f; break;   // forming
                case Recognition.StaticState.Held: sweep = 1.0f; break;        // closed
                case Recognition.StaticState.Releasing: sweep = 0.5f; break;   // opening again
                default: return;
            }
            float cx = local.x + palmNorm.x * local.width;
            float cy = local.y + (1f - palmNorm.y) * local.height;
            float radius = 11f * S; // outer ring so it reads distinctly from the Jump ring
            int steps = 44; int drawn = Mathf.CeilToInt(steps * sweep);
            GUI.color = cfg.waitAmber;
            for (int i = 0; i <= drawn; i++)
            {
                float ang = Mathf.PI * 0.25f + (i / (float)steps) * Mathf.PI * 2f;
                float x = cx + Mathf.Cos(ang) * radius;
                float y = cy - Mathf.Sin(ang) * radius;
                GUI.DrawTexture(new Rect(x - S * 0.5f, y - S * 0.5f, S, S), white);
            }
            GUI.color = Color.white;
        }

        // Neutral: no ring. PoseArmed: a half-ring (Jump cyan / Stop amber). MotionTracking: the ring closes
        // toward a full circle as the stroke matches. PoseCandidate on an unregistered shape: a small gray
        // broken ring. Triggered: a full ring (the queue orb + success pulse carry the rest).
        private void DrawPoseRing(Rect local, Vector2 palmNorm, Recognition.PcmResult pcm)
        {
            Color color; float startFrac, sweepFrac; int dashes = 0;
            switch (pcm.State)
            {
                case Recognition.PcmState.PoseArmed:
                    color = pcm.ArmedClass == SymbolicClass.Jump ? cfg.signalCyan : cfg.waitAmber;
                    startFrac = 0f; sweepFrac = 0.5f;
                    break;
                case Recognition.PcmState.MotionTracking:
                    color = pcm.ArmedClass == SymbolicClass.Jump ? cfg.signalCyan : cfg.waitAmber;
                    startFrac = 0f;
                    sweepFrac = Mathf.Clamp01(0.5f + 0.5f * Mathf.Clamp01(pcm.MotionDisp / 0.12f));
                    break;
                case Recognition.PcmState.Triggered:
                    color = pcm.FiredClass == SymbolicClass.Jump ? cfg.signalCyan : cfg.waitAmber;
                    startFrac = 0f; sweepFrac = 1f;
                    break;
                case Recognition.PcmState.PoseCandidate:
                    color = cfg.midGray; startFrac = 0f; sweepFrac = 0.5f; dashes = 4; // broken gray ring
                    break;
                default:
                    return;
            }

            float cx = local.x + palmNorm.x * local.width;
            float cy = local.y + (1f - palmNorm.y) * local.height;
            float radius = 8f * S;
            int steps = 40;
            int drawn = Mathf.CeilToInt(steps * sweepFrac);
            GUI.color = color;
            for (int i = 0; i <= drawn; i++)
            {
                if (dashes > 0 && (i % (steps / dashes)) < (steps / dashes) / 2) continue; // dashed = broken
                float ang = Mathf.PI * (0.25f) + (startFrac + i / (float)steps) * Mathf.PI * 2f;
                float x = cx + Mathf.Cos(ang) * radius;
                float y = cy - Mathf.Sin(ang) * radius;
                GUI.DrawTexture(new Rect(x - S * 0.5f, y - S * 0.5f, S, S), white);
            }
            GUI.color = Color.white;
        }

        private void DrawPanelLine(Rect panelRect, Vector2 a, Vector2 b, Color color)
        {
            Vector2 pa = new Vector2(panelRect.x + a.x * panelRect.width, panelRect.y + (1f - a.y) * panelRect.height);
            Vector2 pb = new Vector2(panelRect.x + b.x * panelRect.width, panelRect.y + (1f - b.y) * panelRect.height);
            GUI.color = color;
            int steps = Mathf.Max(2, Mathf.CeilToInt(Vector2.Distance(pa, pb) / S));
            for (int i = 0; i <= steps; i++)
            {
                Vector2 p = Vector2.Lerp(pa, pb, i / (float)steps);
                GUI.DrawTexture(new Rect(p.x - S * 0.5f, p.y - S * 0.5f, S, S), white);
            }
            GUI.color = Color.white;
        }

        private void DrawPanelDot(Rect panelRect, Vector2 normalized, Color color, int logicalSize)
        {
            float x = panelRect.x + normalized.x * panelRect.width;
            float y = panelRect.y + (1f - normalized.y) * panelRect.height;
            GUI.color = color;
            GUI.DrawTexture(new Rect(x - logicalSize * S * 0.5f, y - logicalSize * S * 0.5f, logicalSize * S, logicalSize * S), white);
            GUI.color = Color.white;
        }

        private void DrawBorder(Rect r, Color color)
        {
            GUI.color = color;
            GUI.DrawTexture(new Rect(r.x - S, r.y - S, r.width + 2 * S, S), white);
            GUI.DrawTexture(new Rect(r.x - S, r.yMax, r.width + 2 * S, S), white);
            GUI.DrawTexture(new Rect(r.x - S, r.y, S, r.height), white);
            GUI.DrawTexture(new Rect(r.xMax, r.y, S, r.height), white);
            GUI.color = Color.white;
        }

        // ------------------------------------------------------------ queue / energy

        private Color OrbColor(Intent intent)
        {
            switch (intent.Type)
            {
                case IntentType.Jump: return cfg.signalCyan;
                case IntentType.Stop: return cfg.waitAmber;
                default: return cfg.directionViolet;
            }
        }

        private void DrawQueue(BeforeDirector director, double realNow)
        {
            // D-089: x=248, 8x8 slots centered at y=88/68/48; index 0 = bottom = NEXT (pulses up to 10x10).
            for (int slot = 0; slot < IntentQueue.Capacity; slot++)
            {
                int cy = BeforeLayout.QueueSlotCenterY[slot];
                int size = BeforeLayout.QueueSlotSize;
                var r = L(BeforeLayout.QueueX, cy - size * 0.5f, size, size);
                // empty slot: dim frame only
                GUI.color = new Color(cfg.midGray.r, cfg.midGray.g, cfg.midGray.b, 0.22f);
                GUI.DrawTexture(r, white);
                var intent = director.Queue.GetSlot(slot);
                if (intent != null)
                {
                    // NEXT slot may pulse from 8x8 up to 10x10
                    float grow = slot == 0 ? (BeforeLayout.QueueNextMaxSize - size) * (0.5f + 0.5f * Mathf.Sin((float)realNow * 6f)) : 0f;
                    var fill = L(BeforeLayout.QueueX - grow * 0.5f, cy - (size + grow) * 0.5f, size + grow, size + grow);
                    Color c = OrbColor(intent);
                    if (intent.Confidence == IntentConfidence.Unstable)
                    {
                        c = Color.Lerp(c, cfg.midGray, 0.35f + 0.25f * Mathf.Sin((float)realNow * 17f));
                    }
                    GUI.color = c;
                    GUI.DrawTexture(fill, white);
                    // internal light: rising for jump, centered for stop, flowing for direction
                    GUI.color = cfg.dirtyWhite;
                    float inner = (float)((realNow * 1.3) % 1.0);
                    float u = S; // 1 logical px
                    if (intent.Type == IntentType.Jump)
                        GUI.DrawTexture(new Rect(fill.center.x - u * 0.5f, fill.yMax - u - inner * (fill.height - 2 * u), u, u), white);
                    else if (intent.Type == IntentType.Stop)
                        GUI.DrawTexture(new Rect(fill.center.x - u * 0.5f, fill.center.y - u * 0.5f, u, u), white);
                    else
                        GUI.DrawTexture(new Rect(fill.x + u + (intent.DirectionSign > 0 ? inner : 1f - inner) * (fill.width - 3 * u), fill.center.y - u * 0.5f, u, u), white);
                }
                GUI.color = Color.white;
            }
        }

        // D-089 energy: a continuous 1px signal-cyan curve (palm -> NUL core) always drawn while connected,
        // with particles travelling along the same curve. Endpoints are exact (t=0 palm, t=1 NUL core);
        // a core flash fires as each particle arrives. All coordinates are logical -> screen via L/S only.
        private double successPulseAt = -1;
        private int lastQueueCount = -1;
        private readonly EnergyField energy = new EnergyField();
        private double lastEnergyNow = -1;
        // D-090 quiet energy: intermittent dim particles, no normal beam; faint curve only during teaching;
        // a single weak success pulse; and a 0.6 s fade of particles+line on hand loss.
        private void DrawEnergy(BeforeDirector director, double realNow)
        {
            bool connected = director.Hand.Tracked
                && director.Recovery != TrackingRecoveryState.SuspendedRewound
                && director.Recovery != TrackingRecoveryState.Collapsed
                && director.Story.State != BeforeStoryState.CompleteHold
                && director.Story.State != BeforeStoryState.Farewell
                && director.Story.State != BeforeStoryState.Boot
                && director.Story.State != BeforeStoryState.CameraUnavailable
                && director.Story.State != BeforeStoryState.AwaitHand;

            // recognition-success / Triggered pulse: an intent reaching the queue (a recognized command)
            int qc = 0;
            for (int i = 0; i < IntentQueue.Capacity; i++) if (director.Queue.GetSlot(i) != null) qc++;
            if (lastQueueCount >= 0 && qc > lastQueueCount) successPulseAt = realNow;
            lastQueueCount = qc;

            // D-093 pooled particle system (fixed pool, no per-frame allocation)
            var pcm = director.Recognition.LastPcm;
            bool teaching = director.Recognition.TeachingCaptureOpen;
            bool armed = pcm.State == Recognition.PcmState.PoseArmed;
            bool tracking = pcm.State == Recognition.PcmState.MotionTracking;
            bool active = teaching || armed || tracking;

            float dt = lastEnergyNow < 0 ? 0f : Mathf.Clamp((float)(realNow - lastEnergyNow), 0f, 0.25f);
            lastEnergyNow = realNow;
            energy.Update(realNow, dt, connected, active);

            float fade = energy.Fade(realNow);
            if (!connected && fade <= 0f) { DrawSuccessPulse(director, realNow); return; }

            Vector2 startLogical = BeforeLayout.PalmLogical(director.Hand.PalmMirrored);
            Vector2 endLogical = nulCoreLogical;
            Vector2 ctrlLogical = BeforeLayout.EnergyControl(startLogical, endLogical);
            // perpendicular (logical) for spreading particles across near-curves within +/- spread
            Vector2 dir = (endLogical - startLogical);
            Vector2 perp = dir.sqrMagnitude > 1e-4f ? new Vector2(-dir.y, dir.x).normalized : new Vector2(1f, 0f);

            // thin base curve (always present while connected/fading): normal 0.06 / active 0.14 alpha
            float baseAlpha = (active ? BeforeLayout.EnergyActiveBaseAlpha : BeforeLayout.EnergyNormalBaseAlpha) * fade;
            DrawEnergyCurve(startLogical, ctrlLogical, endLogical, baseAlpha);

            float particleAlpha = active ? BeforeLayout.EnergyActiveAlpha : BeforeLayout.EnergyNormalAlpha;
            float bright = tracking ? BeforeLayout.EnergyTrackingBrightness : 0f;
            var pool = energy.Pool;
            for (int i = 0; i < pool.Length; i++)
            {
                if (!pool[i].Alive) continue;
                float phase = (float)((realNow - pool[i].SpawnTime) / BeforeLayout.EnergyTravelSeconds);
                if (phase < 0f || phase > 1f) continue;
                // PoseArmed: converge near the palm first (hold early phase closer to the palm briefly)
                float ph = armed ? phase * phase : phase;
                Vector2 basePt = BeforeLayout.EnergyPoint(startLogical, ctrlLogical, endLogical, ph);
                Vector2 lp = basePt + perp * pool[i].Offset * (1f - Mathf.Abs(ph - 0.5f) * 2f); // spread widest mid-flight
                Vector2 p = ToScreen(lp);
                float a = Mathf.Clamp01((particleAlpha * Mathf.Sin(phase * Mathf.PI) + bright) * fade);
                GUI.color = new Color(cfg.signalCyan.r, cfg.signalCyan.g, cfg.signalCyan.b, a);
                // D-096: particles are mostly 2x2 logical px, rarely 3x3 (density/rate unchanged)
                float sz = pool[i].Big ? S * 3f : S * 2f;
                GUI.DrawTexture(new Rect(p.x - sz * 0.5f, p.y - sz * 0.5f, sz, sz), white);
            }
            GUI.color = Color.white;

            DrawSuccessPulse(director, realNow);
        }

        private void DrawEnergyCurve(Vector2 startLogical, Vector2 ctrlLogical, Vector2 endLogical, float alpha)
        {
            if (alpha <= 0f) return;
            int seg = BeforeLayout.EnergyCurveSegments;
            GUI.color = new Color(cfg.signalCyan.r, cfg.signalCyan.g, cfg.signalCyan.b, alpha);
            Vector2 prev = ToScreen(BeforeLayout.EnergyPoint(startLogical, ctrlLogical, endLogical, 0f));
            for (int i = 1; i <= seg; i++)
            {
                Vector2 cur = ToScreen(BeforeLayout.EnergyPoint(startLogical, ctrlLogical, endLogical, i / (float)seg));
                int sub = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(prev, cur) / S));
                for (int k = 0; k <= sub; k++)
                {
                    Vector2 p = Vector2.Lerp(prev, cur, k / (float)sub);
                    GUI.DrawTexture(new Rect(p.x - S * 0.5f, p.y - S * 0.5f, S, S), white);
                }
                prev = cur;
            }
            GUI.color = Color.white;
        }

        private void DrawSuccessPulse(BeforeDirector director, double realNow)
        {
            if (successPulseAt < 0) return;
            float t = Mathf.Clamp01((float)(realNow - successPulseAt) / BeforeLayout.EnergySuccessPulseSeconds);
            if (t >= 1f) return;
            float env = Mathf.Sin(t * Mathf.PI);                 // 0 -> 1 -> 0
            float scale = 1f + (BeforeLayout.EnergySuccessPulseScale - 1f) * env;
            float r = 3f * S * scale;
            Vector2 c = ToScreen(nulCoreLogical);
            GUI.color = new Color(cfg.signalCyan.r, cfg.signalCyan.g, cfg.signalCyan.b, 0.35f * env);
            GUI.DrawTexture(new Rect(c.x - r * 0.5f, c.y - r * 0.5f, r, r), white);
            GUI.color = Color.white;
        }

        private Vector2 ToScreen(Vector2 logical) => new Vector2(logical.x * S + originX, logical.y * S + originY);

        // ------------------------------------------------------------ map

        private void DrawMap(BeforeDirector director, double realNow)
        {
            var mapRect = L(36, 24, 180, 132);
            GUI.color = cfg.voidBlack;
            GUI.DrawTexture(mapRect, white);
            GUI.color = Color.white;
            DrawBorder(mapRect, cfg.midGray);

            Vector2 nul = new Vector2(mapRect.center.x, mapRect.yMax - 12 * S);
            Vector2 leftEnd = new Vector2(mapRect.x + 30 * S, mapRect.y + 18 * S);
            Vector2 rightEnd = new Vector2(mapRect.xMax - 30 * S, mapRect.y + 18 * S);

            bool final = director.Story.State == BeforeStoryState.FinalPlanMap;
            FinalRoute planned = director.PlannedRoute;

            DrawMapRoute(nul, leftEnd, planned == FinalRoute.Left || !final || planned == FinalRoute.None ? 1f : 0.25f,
                planned == FinalRoute.Left ? cfg.directionViolet : cfg.ashGray);
            DrawMapRoute(nul, rightEnd, planned == FinalRoute.Right || !final || planned == FinalRoute.None ? 1f : 0.25f,
                planned == FinalRoute.Right ? cfg.directionViolet : cfg.ashGray);

            // NUL map icon (top-down)
            GUI.color = cfg.charcoal;
            GUI.DrawTexture(new Rect(nul.x - 4 * S, nul.y - 4 * S, 8 * S, 8 * S), white);
            GUI.color = cfg.signalCyan;
            GUI.DrawTexture(new Rect(nul.x - S, nul.y - 2 * S, 2 * S, 2 * S), white);
            GUI.color = Color.white;

            if (final)
            {
                // hazard order icons per route (left: gate then gap; right: gap then gate), no command words
                DrawMapHazard(Vector2.Lerp(nul, leftEnd, 0.45f), true);
                DrawMapHazard(Vector2.Lerp(nul, leftEnd, 0.75f), false);
                DrawMapHazard(Vector2.Lerp(nul, rightEnd, 0.45f), false);
                DrawMapHazard(Vector2.Lerp(nul, rightEnd, 0.75f), true);

                // review ring while stillness accumulates
                if (director.Queue.IsFullPlan)
                {
                    float progress = Mathf.Clamp01((float)(director.PlanStillnessAccumulated / 2.0));
                    GUI.color = cfg.dirtyWhite;
                    float radius = Mathf.Lerp(10f, 5f, progress) * S;
                    for (int a = 0; a < 24; a++)
                    {
                        float ang = a / 24f * Mathf.PI * 2f;
                        Vector2 p = nul + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * radius;
                        GUI.DrawTexture(new Rect(p.x, p.y, S, S), white);
                    }
                    GUI.color = Color.white;
                }
            }
        }

        private void DrawMapRoute(Vector2 from, Vector2 to, float alpha, Color color)
        {
            GUI.color = new Color(color.r, color.g, color.b, alpha);
            int steps = 24;
            for (int i = 0; i <= steps; i++)
            {
                Vector2 p = Vector2.Lerp(from, to, i / (float)steps);
                GUI.DrawTexture(new Rect(p.x, p.y, S, S), white);
            }
            GUI.color = Color.white;
        }

        private void DrawMapHazard(Vector2 at, bool gate)
        {
            GUI.color = gate ? cfg.faultRed : cfg.midGray;
            if (gate)
            {
                GUI.DrawTexture(new Rect(at.x - 2 * S, at.y - 3 * S, S, 6 * S), white);
                GUI.DrawTexture(new Rect(at.x + S, at.y - 3 * S, S, 6 * S), white);
            }
            else
            {
                GUI.DrawTexture(new Rect(at.x - 3 * S, at.y, 2 * S, S), white);
                GUI.DrawTexture(new Rect(at.x + S, at.y, 2 * S, S), white);
            }
            GUI.color = Color.white;
        }

        // ------------------------------------------------------------ farewell / operator / debug

        private void DrawFarewellTraces(BeforeDirector director, double realNow)
        {
            DrawTrace(director.LearnedJumpTrace, cfg.signalCyan, new Vector2(120, 60));
            DrawTrace(director.LearnedStopTrace, cfg.waitAmber, new Vector2(160, 60));
            // direction trace: straight signed sweep
            GUI.color = cfg.directionViolet;
            for (int i = 0; i < 20; i++)
            {
                float x = director.ChosenDirectionSign >= 0 ? 200 + i : 220 - i;
                GUI.DrawTexture(L(x, 66, 1, 1), white);
            }
            GUI.color = Color.white;
        }

        private void DrawTrace(GestureFeature feature, Color color, Vector2 originLogical)
        {
            if (feature == null) return;
            GUI.color = color;
            foreach (var p in feature.Points)
            {
                GUI.DrawTexture(L(originLogical.x + p.x * 24f, originLogical.y - p.y * 24f, 1, 1), white);
            }
            GUI.color = Color.white;
        }

        private void DrawOperator(BeforeDirector director, double realNow)
        {
            if (director.ResetHoldProgress > 0f)
            {
                GUI.color = cfg.dirtyWhite;
                GUI.DrawTexture(L(12, 30, 96 * director.ResetHoldProgress, 1), white);
                GUI.color = Color.white;
            }
            if (director.OperatorPaused)
            {
                // sparse stopped scan line on the world layer only
                GUI.color = new Color(1f, 1f, 1f, 0.06f);
                for (int y = 0; y < 180; y += 8) GUI.DrawTexture(L(0, y, 220, 1), white);
                GUI.color = Color.white;
            }
        }

        private void DrawDebug(BeforeDirector director, double realNow, float fps)
        {
            // D-089 F1 panel: x=8,y=74,w=232,h=96; 4 logical px font, 6 logical line step; every line drawn
            // separately at a fixed step and truncated so nothing crosses x=240 or y=170.
            var panel = L(BeforeLayout.DebugPanel.x, BeforeLayout.DebugPanel.y, BeforeLayout.DebugPanel.width, BeforeLayout.DebugPanel.height);
            GUI.color = new Color(cfg.charcoal.r, cfg.charcoal.g, cfg.charcoal.b, 0.85f);
            GUI.DrawTexture(panel, white);
            GUI.color = Color.white;

            var d = director;
            var lines = new[]
            {
                $"CAM {Abbrev(d.Hand.DeviceName, 16)} {(d.Hand.CameraAvailable ? "OK" : "NG")} {d.Hand.CameraWidth}x{d.Hand.CameraHeight}@{d.Hand.CameraFps:F0} perm:{(d.Hand.PermissionGranted ? "Y" : "N")}",
                $"WANT {Abbrev(d.RuntimeCfg.preferredDeviceName, 16)} luma:{d.Hand.MeanLuma:F2}",
                $"NOTE {Abbrev(d.Hand.StatusNote, 40)}",
                $"HAND {(d.Hand.Tracked ? "P" : "-")} q:{d.Hand.Quality:F2} palm:{d.Hand.PalmMirrored.x:F2},{d.Hand.PalmMirrored.y:F2} rec:{d.Recovery}",
                $"INFER cur:{d.Hand.LastInferenceMs:F1}ms med:{d.Hand.MedianHz:F1}hz p95:{d.Hand.InferenceP95Ms:F1}ms",
                $"POSE arm:{d.LastPcm.ArmedClass} st:{d.LastPcm.State} n1:{d.LastPcm.PoseNorm:F2} n2:{d.LastPcm.PoseSecondNorm:F2} mrg:{d.LastPcm.PoseMargin:F2} stab:{d.LastPcm.PoseStableCount}/7 tpl:{d.JumpTemplateCount}/{d.StopTemplateCount}",
                $"MOTION {d.LastPcm.State} dsp:{d.LastPcm.MotionDisp:F2} cos:{d.LastPcm.MotionDirCos:F2} el:{d.LastPcm.MotionElapsed:F2} fr:{d.LastPcm.MotionFrames} pfx:{d.LastPcm.PrefixDist:F2}",
                $"FUSE fired:{d.LastFiredClass} reject:{d.LastRejectReason} jspd:{JumpSpeedRatio(d.LastPcm):F2} resp:{d.LastRecognitionResponseMs:F0}ms",
                $"POINT own:{d.Recognition.InputOwner} dir:{d.Recognition.PointerPreview} hold:{d.Recognition.Pointer.HoldProgress:F2} stopHeld:{(d.Recognition.StopActive ? 1 : 0)}",
                $"CONTEXT literal:{d.LiteralExecutionCount} refuse:{d.RefusalCount}",
                $"QUEUE {(d.Queue.GetSlot(0)?.Type.ToString() ?? "-")}/{(d.Queue.GetSlot(1)?.Type.ToString() ?? "-")}/{(d.Queue.GetSlot(2)?.Type.ToString() ?? "-")}",
                $"STORY {Abbrev(d.Story.State.ToString(), 22)} pause:{d.OperatorPaused}",
                $"PERF {fps:F0}fps note:{Abbrev(d.LastEventNote ?? "-", 18)}",
            };
            int startX = (int)BeforeLayout.DebugPanel.x + 2;
            int startY = (int)BeforeLayout.DebugPanel.y + 2;
            for (int i = 0; i < lines.Length; i++)
            {
                GUI.Label(L(startX, startY + i * BeforeLayout.DebugLineStep, BeforeLayout.DebugPanel.width - 4, BeforeLayout.DebugLineStep + 2), lines[i], debugStyle);
            }
        }

        // D-096: Jump motion-speed ratio is a DIAGNOSTIC ONLY (never fed to jump velocity/gravity/landing/planner).
        private static float JumpSpeedRatio(Recognition.PcmResult p)
        {
            if (p.MotionElapsed <= 1e-3f) return 0f;
            return p.MotionDisp / p.MotionElapsed; // panel-diagonals per second at the moment evidence formed
        }

        private static string Abbrev(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "-";
            return s.Length <= max ? s : s.Substring(0, max - 1) + "…";
        }
    }
}
