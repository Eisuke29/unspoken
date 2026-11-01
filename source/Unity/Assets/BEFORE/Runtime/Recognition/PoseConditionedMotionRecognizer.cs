using System.Collections.Generic;
using UnityEngine;

namespace Before.Recognition
{
    // D-092 live recognizer: the 7-state Pose -> Motion -> Trigger machine plus compound teaching.
    // Pure and deterministic. A hand-shape alone never fires; a stroke alone never fires; only the taught
    // pose held through the taught stroke fires, and it fires at the stroke prefix (no wait for stillness).

    public sealed class PoseConditionedMotionRecognizer
    {
        public readonly PcmConfig Cfg;
        private readonly PoseModel jumpPose = new PoseModel();
        private readonly PoseModel stopPose = new PoseModel();
        private readonly MotionModel jumpMotion = new MotionModel();
        private readonly MotionModel stopMotion = new MotionModel();

        public PoseModel Pose(SymbolicClass c) => c == SymbolicClass.Jump ? jumpPose : stopPose;
        public MotionModel Motion(SymbolicClass c) => c == SymbolicClass.Jump ? jumpMotion : stopMotion;
        public bool IsConfirmed(SymbolicClass c) => Pose(c).Samples.Count >= 3 && Motion(c).Samples.Count >= 3;
        public bool IsLearned(SymbolicClass c) => Pose(c).Learned && Motion(c).Learned;

        // ---- live state ----
        private PcmState state = PcmState.Neutral;
        private PcmReject lastReject = PcmReject.None;
        private SymbolicClass armedClass;
        private SymbolicClass lockClass;
        private double armedTime, motionStartTime, lastValidPoseTime, invalidSinceTime, releaseSinceTime;
        private bool releasing;
        private Vector2 armedPoint;
        private int speedOkStreak;
        private int otherPoseStreak;
        private int movingFrames;
        private int stillStreak;
        private double prevTime = double.NaN;
        private Vector2 prevPalm;
        private readonly List<Vector2> motionPath = new List<Vector2>(64);
        private readonly List<(SymbolicClass cls, float norm)> history = new List<(SymbolicClass, float)>(16);

        public PcmState State => state;
        public PcmReject LastReject => lastReject;

        public PoseConditionedMotionRecognizer(PcmConfig cfg = null) { Cfg = cfg ?? new PcmConfig(); }

        public void ResetLive()
        {
            state = PcmState.Neutral; lastReject = PcmReject.None;
            history.Clear(); motionPath.Clear();
            prevTime = double.NaN; speedOkStreak = 0; otherPoseStreak = 0; releasing = false;
        }

        public void ResetAll()
        {
            jumpPose.Samples.Clear(); stopPose.Samples.Clear();
            jumpMotion.Samples.Clear(); stopMotion.Samples.Clear();
            jumpPose.Learned = stopPose.Learned = jumpMotion.Learned = stopMotion.Learned = false;
            ResetLive();
        }

        /// <summary>Classify a single pose frame; returns best class and normalized distances.</summary>
        private void ClassifyPose(Vector2[] lm, out SymbolicClass best, out float bestNorm, out float secondNorm, out bool bothLearned)
        {
            var d = PoseDescriptor.Extract(lm);
            float jn = jumpPose.NormalizedDistance(d);
            float sn = stopPose.NormalizedDistance(d);
            bothLearned = jumpPose.Learned && stopPose.Learned;
            if (jn <= sn) { best = SymbolicClass.Jump; bestNorm = jn; secondNorm = sn; }
            else { best = SymbolicClass.Stop; bestNorm = sn; secondNorm = jn; }
        }

        /// <summary>Advance the machine by one live frame.</summary>
        public PcmResult Step(PcmFrame f)
        {
            var r = new PcmResult { State = state, Reject = PcmReject.None, PoseNorm = float.MaxValue, PoseSecondNorm = float.MaxValue };

            // ---- pose pipeline (per-frame classification; screen motion never enters this) ----
            SymbolicClass best = SymbolicClass.None; float bestNorm = float.MaxValue, secondNorm = float.MaxValue; bool bothLearned = false;
            bool poseValid = f.Valid && f.Lm != null && f.Quality >= Cfg.QualityMin && (jumpPose.Learned || stopPose.Learned);
            if (poseValid)
            {
                ClassifyPose(f.Lm, out best, out bestNorm, out secondNorm, out bothLearned);
                if (bestNorm > Cfg.PoseArmMaxNorm) best = SymbolicClass.None;
            }
            PushHistory(best, bestNorm);
            int stableCount = StableCount(best);
            float margin = (secondNorm >= float.MaxValue * 0.5f) ? float.MaxValue : (secondNorm - bestNorm);
            bool marginOk = !bothLearned || margin >= Cfg.PoseMargin;
            bool armedOk = poseValid && best != SymbolicClass.None && stableCount >= Cfg.PoseStableNeeded
                           && bestNorm <= Cfg.PoseArmMaxNorm && marginOk;

            r.PoseNorm = bestNorm; r.PoseSecondNorm = secondNorm; r.PoseMargin = margin;
            r.PoseStableCount = stableCount;

            // ---- motion pipeline (raw low-latency screen palm) ----
            float dt = double.IsNaN(prevTime) ? 0f : Mathf.Max(1e-4f, (float)(f.Time - prevTime));
            float palmWidth = (f.Lm != null) ? GestureV2Builder.PalmWidth(f.Lm) : 0.15f;

            switch (state)
            {
                case PcmState.Neutral:
                case PcmState.PoseCandidate:
                    if (armedOk) EnterArmed(best, f);
                    else state = (poseValid && best != SymbolicClass.None) ? PcmState.PoseCandidate : PcmState.Neutral;
                    break;

                case PcmState.PoseArmed:
                    StepArmed(f, dt, best, armedOk);
                    break;

                case PcmState.MotionTracking:
                    StepMotion(f, dt, best, poseValid, palmWidth, ref r);
                    break;

                case PcmState.Triggered:
                    // one-frame state; move to rearm-lock
                    state = PcmState.Rearming; releasing = false;
                    break;

                case PcmState.Rejected:
                    // a rejected motion attempt does NOT lock re-fire (only a Trigger does). If the pose is
                    // still held, re-arm and allow another stroke; a lost/changed pose returns to Neutral.
                    if (lastReject == PcmReject.TrackingLost || lastReject == PcmReject.NeedsRearm)
                        state = PcmState.Neutral;
                    else if (armedOk && best == armedClass)
                        EnterArmed(armedClass, f);
                    else
                        state = PcmState.Neutral;
                    break;

                case PcmState.Rearming:
                    StepRearm(f, armedOk, best);
                    break;
            }

            prevTime = f.Time; prevPalm = f.PalmScreen;

            r.State = state; r.Reject = lastReject;
            if (state == PcmState.PoseArmed || state == PcmState.MotionTracking) r.ArmedClass = armedClass;
            return r;
        }

        private void EnterArmed(SymbolicClass c, PcmFrame f)
        {
            state = PcmState.PoseArmed;
            armedClass = c; armedTime = f.Time; armedPoint = f.PalmScreen;
            lastValidPoseTime = f.Time; invalidSinceTime = f.Time;
            speedOkStreak = 0; otherPoseStreak = 0;
            motionPath.Clear();
            // lastReject is intentionally NOT cleared here: it stays as the last diagnostic reason (F1) until
            // the next fire (which clears it). Re-arming after a rejected stroke keeps the reason visible.
        }

        private void StepArmed(PcmFrame f, float dt, SymbolicClass best, bool armedOk)
        {
            // pose lost before any motion -> fall back
            if (!armedOk || best != armedClass)
            {
                if (f.Time - armedTime > Cfg.MotionStartWait) { lastReject = PcmReject.PoseNoMatch; state = PcmState.Neutral; }
                else if (best != armedClass && best != SymbolicClass.None) { state = PcmState.PoseCandidate; }
                return;
            }

            float speed = dt > 0f ? Vector2.Distance(f.PalmScreen, prevPalm) / dt / Cfg.Diagonal : 0f;
            if (speed > Cfg.MotionStartSpeed) speedOkStreak++; else speedOkStreak = 0;
            float dispFromArm = Vector2.Distance(f.PalmScreen, armedPoint) / Cfg.Diagonal;

            if (speedOkStreak >= 2 || dispFromArm >= Cfg.MotionStartDisp)
            {
                state = PcmState.MotionTracking;
                motionStartTime = f.Time;
                lastValidPoseTime = f.Time;
                motionPath.Clear();
                motionPath.Add(f.PalmScreen);
                movingFrames = 0; stillStreak = 0;
            }
            else if (f.Time - armedTime > Cfg.MotionStartWait)
            {
                lastReject = PcmReject.MotionTimeout; state = PcmState.Rejected;
            }
        }

        private void StepMotion(PcmFrame f, float dt, SymbolicClass best, bool poseValid, float palmWidth, ref PcmResult r)
        {
            // ---- pose latch maintenance ----
            if (poseValid && best == armedClass) { lastValidPoseTime = f.Time; invalidSinceTime = f.Time; otherPoseStreak = 0; }
            else if (poseValid && best != SymbolicClass.None && best != armedClass && IsLearned(best))
            {
                otherPoseStreak++;
                if (otherPoseStreak >= 2) { lastReject = PcmReject.NeedsRearm; state = PcmState.Rejected; return; }
            }
            else { otherPoseStreak = 0; }

            if (!f.Valid) { if (f.Time - invalidSinceTime > Cfg.TrackLostRelease) { lastReject = PcmReject.TrackingLost; state = PcmState.Rejected; return; } }
            else invalidSinceTime = f.Time;

            if (f.Time - lastValidPoseTime > Cfg.PoseLatch)
            {
                lastReject = PcmReject.NeedsRearm; state = PcmState.Rejected; return;
            }

            float frameSpeed = dt > 0f ? Vector2.Distance(f.PalmScreen, prevPalm) / dt / Cfg.Diagonal : 0f;
            if (frameSpeed > Cfg.MotionStartSpeed * 0.5f) { movingFrames++; stillStreak = 0; } else stillStreak++;

            motionPath.Add(f.PalmScreen);
            float elapsed = (float)(f.Time - motionStartTime);
            int frames = movingFrames;
            Vector2 disp = f.PalmScreen - motionPath[0];
            float dispNorm = disp.magnitude / Cfg.Diagonal;
            var model = Motion(armedClass);
            Vector2 dir = disp.sqrMagnitude > 1e-8f ? disp.normalized : Vector2.zero;
            float cos = model.Learned ? Vector2.Dot(dir, model.Direction) : 0f;

            float prefixDist = float.MaxValue;
            if (model.Learned && motionPath.Count >= 2)
            {
                var live = MotionModel.NormalizeScale(MotionModel.Resample(motionPath));
                prefixDist = MotionModel.PathDistance(live, model.PrefixTemplate(Cfg.PrefixFraction));
            }

            r.MotionDisp = dispNorm; r.MotionDirCos = cos; r.MotionElapsed = elapsed;
            r.MotionFrames = frames; r.PrefixDist = prefixDist;

            float dispThreshold = Mathf.Max(Cfg.FireMinDisp * Cfg.Diagonal, Cfg.FirePalmWidthFactor * palmWidth);
            bool fire = frames >= Cfg.MotionMinFrames
                        && disp.magnitude >= dispThreshold
                        && cos >= Cfg.FireDirCos
                        && prefixDist <= model.Radius
                        && elapsed >= Cfg.MotionMinElapsed
                        && elapsed <= Cfg.MotionMaxElapsed;

            if (fire)
            {
                state = PcmState.Triggered; lastReject = PcmReject.None;
                r.Fired = true; r.FiredClass = armedClass; r.State = PcmState.Triggered;
                lockClass = armedClass; releasing = false;
                return;
            }

            // motion ended (hand settled) before firing, or ran past the window: classify the failure now
            bool settled = stillStreak >= 2 && movingFrames > 0;
            if (settled || elapsed > Cfg.MotionMaxElapsed)
            {
                lastReject = (frames < Cfg.MotionMinFrames || elapsed < Cfg.InsufficientElapsed)
                    ? PcmReject.InsufficientFrames
                    : (elapsed > Cfg.MotionMaxElapsed ? PcmReject.MotionTimeout
                        : (cos < Cfg.FireDirCos ? PcmReject.WrongDirection : PcmReject.PrefixNoMatch));
                state = PcmState.Rejected;
            }
        }

        private void StepRearm(PcmFrame f, bool armedOk, SymbolicClass best)
        {
            bool stillHolding = armedOk && best == lockClass;
            if (stillHolding) { releasing = false; return; }
            if (!releasing) { releasing = true; releaseSinceTime = f.Time; }
            if (f.Time - releaseSinceTime >= Cfg.RearmRelease)
            {
                state = PcmState.Neutral; lastReject = PcmReject.None; releasing = false;
            }
        }

        private void PushHistory(SymbolicClass c, float norm)
        {
            history.Add((c, norm));
            while (history.Count > Cfg.PoseHistory) history.RemoveAt(0);
        }

        private int StableCount(SymbolicClass c)
        {
            if (c == SymbolicClass.None) return 0;
            int n = 0;
            for (int i = 0; i < history.Count; i++) if (history[i].cls == c) n++;
            return n;
        }

        // ---- teaching: one Space-hold capture split into a stable-pose stage then a single-stroke motion ----
        public struct TeachOutcome
        {
            public bool Accepted;         // this sample was valid and added
            public bool Confirmed;        // class now has >=3 samples
            public PcmReject Reason;      // when not accepted
            public int StablePoseFrames;
            public int MotionFrames;
            public float MotionDisp;
        }

        public TeachOutcome SubmitTeaching(SymbolicClass c, IReadOnlyList<PcmFrame> frames)
        {
            var outc = new TeachOutcome { Reason = PcmReject.None };
            if (frames == null || frames.Count < 6) { outc.Reason = PcmReject.InsufficientFrames; return outc; }

            double total = frames[frames.Count - 1].Time - frames[0].Time;
            // quality gate
            for (int i = 0; i < frames.Count; i++)
                if (!frames[i].Valid || frames[i].Lm == null || frames[i].Quality < Cfg.QualityMin) { outc.Reason = PcmReject.LowQuality; return outc; }
            if (total < 0.80 || total > 2.20) { outc.Reason = PcmReject.InsufficientFrames; return outc; }

            // find the leading still segment (pose stage): consecutive low-speed frames from the start
            int stableEnd = 0;
            for (int i = 1; i < frames.Count; i++)
            {
                float dt = Mathf.Max(1e-4f, (float)(frames[i].Time - frames[i - 1].Time));
                float sp = Vector2.Distance(frames[i].PalmScreen, frames[i - 1].PalmScreen) / dt / Cfg.Diagonal;
                if (sp < Cfg.MotionStartSpeed) stableEnd = i; else break;
            }
            int stableFrames = stableEnd + 1;
            if (stableFrames < 5) { outc.Reason = PcmReject.PoseNoMatch; return outc; }

            // motion stage = from stable end (ignoring keyboard micro-jitter) to the last frame
            var motionPts = new List<Vector2>();
            for (int i = stableEnd; i < frames.Count; i++) motionPts.Add(frames[i].PalmScreen);
            if (motionPts.Count < Cfg.MotionMinFrames) { outc.Reason = PcmReject.InsufficientFrames; return outc; }
            float motionDisp = Vector2.Distance(motionPts[motionPts.Count - 1], motionPts[0]) / Cfg.Diagonal;
            if (motionDisp < 0.10f) { outc.Reason = PcmReject.InsufficientFrames; return outc; }

            // build a medoid pose descriptor over the stable frames
            PoseDescriptor pose = AveragePose(frames, 0, stableEnd);
            Vector2[] motion = MotionModel.Resample(motionPts);

            Pose(c).AddSample(pose);
            Motion(c).AddSample(motion);
            PruneOutliers(c);

            outc.Accepted = true;
            outc.Confirmed = IsConfirmed(c);
            outc.StablePoseFrames = stableFrames;
            outc.MotionFrames = motionPts.Count;
            outc.MotionDisp = motionDisp;
            return outc;
        }

        private static PoseDescriptor AveragePose(IReadOnlyList<PcmFrame> frames, int from, int to)
        {
            // medoid over the stable window keeps normalization intact (averaging canonical shapes is fine here)
            var descs = new List<PoseDescriptor>();
            for (int i = from; i <= to; i++) descs.Add(PoseDescriptor.Extract(frames[i].Lm));
            int best = 0; float bestSum = float.MaxValue;
            for (int i = 0; i < descs.Count; i++)
            {
                float sum = 0f;
                for (int j = 0; j < descs.Count; j++) if (i != j) sum += PoseDescriptor.Distance(descs[i], descs[j]);
                if (sum < bestSum) { bestSum = sum; best = i; }
            }
            return descs[best];
        }

        // Adaptive 3+1: evaluate pose and motion clusters SEPARATELY, dropping at most one outlier in each,
        // capping each at 5 samples.
        private void PruneOutliers(SymbolicClass c)
        {
            DropPoseOutlier(Pose(c));
            DropMotionOutlier(Motion(c));
        }

        private static void DropPoseOutlier(PoseModel m)
        {
            if (m.Samples.Count < 4) return;
            int worst = 0; float worstSum = -1f, mean = 0f;
            var sums = new float[m.Samples.Count];
            for (int i = 0; i < m.Samples.Count; i++)
            {
                float sum = 0f;
                for (int j = 0; j < m.Samples.Count; j++) if (i != j) sum += PoseDescriptor.Distance(m.Samples[i], m.Samples[j]);
                sums[i] = sum; mean += sum;
                if (sum > worstSum) { worstSum = sum; worst = i; }
            }
            mean /= m.Samples.Count;
            if (m.Samples.Count > 5 || (mean > 0f && worstSum > mean * 1.8f))
            {
                m.Samples.RemoveAt(worst); m.Recompute();
            }
        }

        private static void DropMotionOutlier(MotionModel m)
        {
            if (m.Samples.Count < 4) return;
            int worst = 0; float worstSum = -1f, mean = 0f;
            for (int i = 0; i < m.Samples.Count; i++)
            {
                float sum = 0f;
                for (int j = 0; j < m.Samples.Count; j++) if (i != j) sum += MotionModel.PathDistance(m.Samples[i], m.Samples[j]);
                mean += sum;
                if (sum > worstSum) { worstSum = sum; worst = i; }
            }
            mean /= m.Samples.Count;
            if (m.Samples.Count > 5 || (mean > 0f && worstSum > mean * 1.8f))
            {
                m.Samples.RemoveAt(worst); m.Recompute();
            }
        }
    }
}
