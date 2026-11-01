/* =========================================================
   UNSPOKEN Web版：手の認識（Python の BeforeGestureSidecar を JavaScript に書き直したもの）
   - カメラ映像はこのページの中だけで処理し、どこにも送らない・保存しない（元の D-099 と同じ）
   - MediaPipe（gesture_recognizer.task：元と同じモデル）で 21 点の手の骨格を取り、
     personal_pose / motion_primitive / command_engine / production_control と同じ計算で合図を判定する
   - 結果は Python 版と同じ JSON を、UDP の代わりに unityInstance.SendMessage で Unity に渡す
   通信形式は Python 版と共通。Web版では入力の外れ値除去・品質判定と復帰時の再確認を追加。
   ========================================================= */

/* ---------------- 小さな数学の道具（numpy の代わり） ---------------- */
const hypot = Math.hypot;
const clamp = (v, lo, hi) => Math.max(lo, Math.min(hi, v));
const sum = (a) => a.reduce((s, x) => s + x, 0);
const mean = (a) => sum(a) / a.length;

function median(a) {
  const s = [...a].sort((x, y) => x - y);
  const n = s.length;
  if (!n) return NaN;
  return n % 2 ? s[(n - 1) / 2] : (s[n / 2 - 1] + s[n / 2]) / 2;
}

// numpy.median(arr, axis=0)：ベクトルの列ごとの中央値
function medianRows(rows) {
  const d = rows[0].length;
  const out = new Array(d);
  for (let j = 0; j < d; j++) out[j] = median(rows.map((r) => r[j]));
  return out;
}

// numpy.percentile（線形補間）
function percentile(a, p) {
  const s = [...a].sort((x, y) => x - y);
  const idx = ((s.length - 1) * p) / 100;
  const lo = Math.floor(idx);
  const hi = Math.ceil(idx);
  return s[lo] + (s[hi] - s[lo]) * (idx - lo);
}

function norm(v) {
  let s = 0;
  for (const x of v) s += x * x;
  return Math.sqrt(s);
}

function subNorm(a, b, from = 0, to = a.length) {
  let s = 0;
  for (let i = from; i < to; i++) s += (a[i] - b[i]) ** 2;
  return Math.sqrt(s);
}

// Python の round()（偶数への丸め）
function pyRound(x) {
  const r = Math.round(x);
  return Math.abs(x % 1) === 0.5 && r % 2 !== 0 ? r - 1 : r;
}

const pyMod = (a, n) => ((a % n) + n) % n;

// numpy.bincount(...).argmax()（同数なら小さい方）
function bincountArgmax(arr) {
  const c = new Map();
  for (const v of arr) c.set(v, (c.get(v) || 0) + 1);
  let best = -1;
  let bestN = -1;
  for (const [v, n] of [...c.entries()].sort((a, b) => a[0] - b[0])) {
    if (n > bestN) { best = v; bestN = n; }
  }
  return best;
}

/* ---------------- personal_pose.py ---------------- */
const pp = (() => {
  const WRIST = 0, INDEX_MCP = 5, MIDDLE_MCP = 9, PINKY_MCP = 17;
  const FINGER_TIPS = [4, 8, 12, 16, 20];
  const FINGER_MCPS = [2, 5, 9, 13, 17];
  const FINGER_PIPS = [3, 6, 10, 14, 18];
  const PALM_IDS = [0, 5, 9, 13, 17];

  const TAKE_SECONDS = 1.2;
  const TAKES_REQUIRED = 3;
  const MIN_VALID_FRAMES = 20;
  const TRACK_QUALITY_MIN = 0.55;
  const HAND_SIZE_MIN = 0.06;
  const TEACH_MARGIN = 0.05;
  const TEACH_BBOX_MIN = 0.09;
  const SAFE_MARGIN = 0.05;
  const WITHIN_CLASS_DISP_MAX = 0.14;
  const TAKE_INCONSISTENT_MAX = 0.16;
  const REPRO_MARGIN_K = 3.0;
  const MIN_ACCEPT_RADIUS = 0.12;
  const MAX_ACCEPT_RADIUS = 0.45;
  const CLASS_GAP = 0.06;

  const R_ARMED = "Armed", R_DIST = "DistanceTooLarge", R_AMBIG = "ClassAmbiguous", R_NOMODEL = "NoModel";

  function teachingGeometry(lm) {
    const xs = lm.map((p) => p[0]);
    const ys = lm.map((p) => p[1]);
    const lo = TEACH_MARGIN, hi = 1 - TEACH_MARGIN;
    const v = [];
    if (Math.min(...xs) < lo) v.push("MOVE HAND RIGHT");
    if (Math.max(...xs) > hi) v.push("MOVE HAND LEFT");
    if (Math.min(...ys) < lo) v.push("MOVE HAND DOWN");
    if (Math.max(...ys) > hi) v.push("MOVE HAND UP");
    if (v.length >= 2) return [false, "HAND OUT OF FRAME"];
    if (v.length) return [false, v[0]];
    const w = Math.max(...xs) - Math.min(...xs);
    const h = Math.max(...ys) - Math.min(...ys);
    if (Math.max(w, h) < TEACH_BBOX_MIN) return [false, "MOVE CLOSER"];
    return [true, "OK"];
  }

  const palmWidth = (pts) => hypot(pts[INDEX_MCP][0] - pts[PINKY_MCP][0], pts[INDEX_MCP][1] - pts[PINKY_MCP][1]);

  function normalizeLandmarks(lm) {
    const ox = lm[WRIST][0], oy = lm[WRIST][1];
    let pts = lm.map((p) => [p[0] - ox, p[1] - oy]);
    const scale = palmWidth(pts) || 1;
    pts = pts.map(([x, y]) => [x / scale, y / scale]);
    const ax = pts[MIDDLE_MCP][0] - pts[WRIST][0];
    const ay = pts[MIDDLE_MCP][1] - pts[WRIST][1];
    const th = Math.atan2(ax, ay);
    const c = Math.cos(th), s = Math.sin(th);
    // pts @ rot.T（rot = [[c,-s],[s,c]]）
    return pts.map(([x, y]) => [c * x - s * y, s * x + c * y]);
  }

  function angle(a, b, c) {
    const v1 = [a[0] - b[0], a[1] - b[1]];
    const v2 = [c[0] - b[0], c[1] - b[1]];
    const n1 = norm(v1), n2 = norm(v2);
    if (n1 === 0 || n2 === 0) return 0;
    return Math.acos(clamp((v1[0] * v2[0] + v1[1] * v2[1]) / (n1 * n2), -1, 1));
  }

  function featureVector(lm) {
    const pts = normalizeLandmarks(lm);
    const palm = [mean(PALM_IDS.map((i) => pts[i][0])), mean(PALM_IDS.map((i) => pts[i][1]))];
    const shape = pts.flat();                                                    // 42
    const tip = FINGER_TIPS.map((t) => hypot(pts[t][0] - palm[0], pts[t][1] - palm[1]));   // 5
    const ang = FINGER_MCPS.map((m, i) => angle(pts[m], pts[FINGER_PIPS[i]], pts[FINGER_TIPS[i]]));  // 5
    const ext = FINGER_TIPS.map((t, i) => {
      const m = FINGER_MCPS[i];
      return hypot(pts[t][0] - pts[WRIST][0], pts[t][1] - pts[WRIST][1]) /
        (hypot(pts[m][0] - pts[WRIST][0], pts[m][1] - pts[WRIST][1]) + 1e-6);
    });                                                                          // 5
    return [...shape, ...tip, ...ang, ...ext];
  }

  function poseDistance(f1, f2) {
    const shape = subNorm(f1, f2, 0, 42) / Math.sqrt(42);
    const tip = subNorm(f1, f2, 42, 47) / Math.sqrt(5);
    const ang = subNorm(f1, f2, 47, 52) / (Math.PI * Math.sqrt(5));
    const ext = subNorm(f1, f2, 52, 57) / Math.sqrt(5);
    return 0.55 * shape + 0.2 * tip + 0.15 * ang + 0.1 * ext;
  }

  class Recording {
    constructor(frames) { this.frames = frames; }   // [{landmarks, quality}]
    validFrames() {
      return this.frames.filter((fr) => {
        if (fr.quality < TRACK_QUALITY_MIN) return false;
        if (palmWidth(fr.landmarks) < HAND_SIZE_MIN) return false;
        let mn = Infinity, mx = -Infinity;
        for (const p of fr.landmarks) { mn = Math.min(mn, p[0], p[1]); mx = Math.max(mx, p[0], p[1]); }
        return !(mn < SAFE_MARGIN || mx > 1 - SAFE_MARGIN);
      });
    }
  }

  class RegistrationError extends Error {}

  function takeStability(rec) {
    const vf = rec.validFrames();
    if (vf.length < MIN_VALID_FRAMES) return [false, 0, vf.length, "TOO_FEW_FRAMES"];
    const feats = vf.map((f) => featureVector(f.landmarks));
    const c = medianRows(feats);
    const disp = median(feats.map((f) => poseDistance(f, c)));
    if (disp > WITHIN_CLASS_DISP_MAX) return [false, disp, vf.length, "UNSTABLE"];
    return [true, disp, vf.length, "OK"];
  }

  function registerPose(name, recordings) {
    if (recordings.length < TAKES_REQUIRED) throw new RegistrationError(`${name}: need 3 takes`);
    const medoids = [];
    const all = [];
    recordings.forEach((rec, i) => {
      const [ok, disp, n, reason] = takeStability(rec);
      if (!ok) throw new RegistrationError(`${name} take ${i + 1}: ${reason} (${disp.toFixed(3)}, ${n})`);
      const feats = rec.validFrames().map((f) => featureVector(f.landmarks));
      medoids.push(medianRows(feats));
      all.push(...feats);
    });
    const overall = medianRows(medoids);
    const spread = median(medoids.map((c) => poseDistance(c, overall)));
    if (spread > TAKE_INCONSISTENT_MAX) throw new RegistrationError(`${name}: takes inconsistent`);
    const centroid = medianRows(all);
    const p90 = percentile(all.map((f) => poseDistance(f, centroid)), 90);
    const radius = clamp(p90 * REPRO_MARGIN_K, MIN_ACCEPT_RADIUS, MAX_ACCEPT_RADIUS);
    return { name, centroid, radius, takeCount: recordings.length };
  }

  function constrainSeparation(a, b) {
    const d = poseDistance(a.centroid, b.centroid);
    const budget = d - CLASS_GAP;
    if (budget < 2 * MIN_ACCEPT_RADIUS * 0.5) throw new RegistrationError("classes too close");
    if (a.radius + b.radius > budget) {
      const s = budget / (a.radius + b.radius);
      a.radius = Math.max(MIN_ACCEPT_RADIUS * 0.5, a.radius * s);
      b.radius = Math.max(MIN_ACCEPT_RADIUS * 0.5, b.radius * s);
    }
  }

  const isSeparable = (a, b) => poseDistance(a.centroid, b.centroid) >= MIN_ACCEPT_RADIUS + CLASS_GAP;

  function matchPose(feat, models) {
    if (!models.length) return { best: null, bestDist: Infinity, second: null, secondDist: Infinity, margin: 0, armed: false, reason: R_NOMODEL };
    const scored = models.map((m) => [poseDistance(feat, m.centroid), m]).sort((a, b) => a[0] - b[0]);
    const [bestD, bestM] = scored[0];
    const secondD = scored.length > 1 ? scored[1][0] : Infinity;
    const secondName = scored.length > 1 ? scored[1][1].name : null;
    const within = bestD <= bestM.radius;
    const ambiguous = scored.length > 1 && scored[1][0] <= scored[1][1].radius;
    const reason = !within ? R_DIST : ambiguous ? R_AMBIG : R_ARMED;
    return { best: bestM.name, bestDist: bestD, second: secondName, secondDist: secondD, margin: secondD - bestD, armed: within && !ambiguous, reason };
  }

  return {
    PALM_IDS, TAKES_REQUIRED, MIN_VALID_FRAMES, TRACK_QUALITY_MIN, TAKE_INCONSISTENT_MAX,
    R_ARMED, R_DIST, R_AMBIG, teachingGeometry, normalizeLandmarks, featureVector, poseDistance,
    Recording, RegistrationError, takeStability, registerPose, constrainSeparation, isSeparable, matchPose, medianRows,
  };
})();

/* ---------------- motion_primitive.py ---------------- */
const mpr = (() => {
  const quantizeDir8 = (dx, dy) => pyMod(pyRound(Math.atan2(dy, dx) / (Math.PI / 4)), 8);
  const dir8ToUnit = (d) => [Math.cos(d * (Math.PI / 4)), Math.sin(d * (Math.PI / 4))];

  function learnJumpMotion(strokes, dts) {
    if (!strokes.length) throw new Error("no strokes");
    const dirs = [], disps = [], vels = [];
    strokes.forEach((track, i) => {
      const dt = dts[i];
      if (track.length < 2 || dt <= 0) return;
      const dx = track[track.length - 1][0] - track[0][0];
      const dy = track[track.length - 1][1] - track[0][1];
      const disp = hypot(dx, dy);
      dirs.push(quantizeDir8(dx, dy));
      disps.push(disp);
      vels.push(disp / dt);
    });
    if (!disps.length) throw new Error("no usable strokes");
    const dir8 = bincountArgmax(dirs);
    return {
      dir8,
      dispThreshold: median(disps) * 0.55,
      velThreshold: median(vels) * 0.45,
      teachingQuality: mean(dirs.map((d) => (d === dir8 ? 1 : 0))),
      peakSpeed: Math.max(...vels),
      get unit() { return dir8ToUnit(this.dir8); },
    };
  }

  return { quantizeDir8, dir8ToUnit, learnJumpMotion };
})();

/* ---------------- command_engine.py ---------------- */
const ce = (() => {
  const INDEX_MCP = 5, INDEX_TIP = 8;
  const STABLE_WINDOW = 7, STABLE_NEEDED = 5;
  const LIVE_COS_PASS = 0.85, LIVE_REQ_FRAC = 0.85;
  const START_DISP_FRAC = 0.25, START_SPEED_FRAC = 0.3, START_FORWARD_COS = 0.3;
  const REJECT_DISP_FRAC = 0.25, SETTLE_SPEED_FRAC = 0.15, SETTLE_FRAMES = 4;
  const FIRE_LOCKOUT_S = 0.15;
  const REARM_REVERSE_FRAC = 0.15, REARM_REVERSE_FRAMES = 2;
  const REARM_SETTLE_SPEED_FRAC = 0.25, REARM_SETTLE_FRAMES = 2;
  const STOP_ON_EVIDENCE = 4, STOP_OFF_EVIDENCE = 4, STOP_MATCH_RADIUS_K = 1.0;
  const DIR_INDEX_RATIO_MIN = 1.5, DIR_INDEX_LEN_MIN = 0.4, DIR_LATERAL_DOMINANCE = 1.6, DIR_HOLD_S = 0.45;
  const OTHER_FINGERS = [[12, 9], [16, 13], [20, 17]];

  const S = { SEARCH: "pose_searching", IDLE: "pose_held_idle", TRACK: "stroke_tracking", LOCK: "fired_lockout", REJ: "stroke_rejected" };

  // ジャンプとしゃがむは同じ「形を保って、決めた方向に動かす」仕組み（Python では Engine と CrouchRecognizer に同じ処理がある）
  class StrokeChannel {
    constructor(cmdType, poseKey, motionKey) {
      this.cmdType = cmdType;
      this.poseKey = poseKey;
      this.motionKey = motionKey;
      this.state = S.SEARCH;
      this.origin = null;
      this.lastPalm = null;
      this.lastT = null;
      this.peakLive = 0;
      this.firePoint = null;
      this.fireT = 0;
      this.settleCount = 0;
      this.revFrames = 0;
      this.cycleCount = 0;
      this.resetReason = "None";
      this.lastMatch = null;
      this.poseReason = "TrackingLost";
      this.stable = [];
    }

    get stableCount() { return this.stable.filter(Boolean).length; }

    pushStable(v) {
      this.stable.push(v);
      if (this.stable.length > STABLE_WINDOW) this.stable.shift();
    }

    resetStroke(palm, t) {
      this.origin = palm;
      this.lastPalm = palm;
      this.lastT = t;
      this.peakLive = 0;
      this.firePoint = null;
      this.settleCount = 0;
      this.revFrames = 0;
    }

    fire(t, d8) {
      this.state = S.LOCK;
      this.cycleCount++;
      this.fireT = t;
      this.firePoint = this.lastPalm;
      this.settleCount = 0;
      this.revFrames = 0;
      return { type: this.cmdType, t, motionDir8: d8 };
    }

    step(obs, models, cfg) {
      const pose = cfg[this.poseKey];
      const motion = cfg[this.motionKey];
      if (!pose || !motion) return [];
      const tracked = obs.tracked && obs.landmarks && obs.palm && obs.quality >= pp.TRACK_QUALITY_MIN && pp.teachingGeometry(obs.landmarks)[0];
      if (!tracked) {
        this.stable = [];
        this.poseReason = obs.tracked ? "OutOfFrame" : "TrackingLost";
        if (this.state !== S.SEARCH) {
          this.state = S.SEARCH;
          this.origin = null;
          this.resetReason = "TrackingLost";
        }
        return [];
      }
      const feat = pp.featureVector(obs.landmarks);
      const m = pp.matchPose(feat, models);
      this.lastMatch = m;
      const frameOk = m.best === pose.name && m.bestDist <= pose.radius * STOP_MATCH_RADIUS_K && m.armed;
      this.pushStable(frameOk);
      const stableOk = frameOk && this.stableCount >= STABLE_NEEDED;
      if (m.best !== pose.name) this.poseReason = m.reason === pp.R_AMBIG ? pp.R_AMBIG : pp.R_DIST;
      else if (!frameOk) this.poseReason = m.reason || pp.R_DIST;
      else if (!stableOk) this.poseReason = "Unstable";
      else this.poseReason = pp.R_ARMED;
      return this.strokeStep(obs, motion, stableOk);
    }

    strokeStep(obs, motion, stableOk) {
      const palm = obs.palm, t = obs.t;
      const unit = motion.unit;
      const req = motion.dispThreshold;
      const peak = motion.peakSpeed > 0 ? motion.peakSpeed : Math.max(motion.velThreshold, 1e-6);

      let speed = 0, fwdVel = 0;
      if (this.lastPalm && this.lastT !== null && t > this.lastT) {
        const dvx = palm[0] - this.lastPalm[0], dvy = palm[1] - this.lastPalm[1], dt = t - this.lastT;
        speed = hypot(dvx, dvy) / dt;
        fwdVel = (dvx * unit[0] + dvy * unit[1]) / dt;
      }
      this.lastPalm = palm;
      this.lastT = t;

      if (!stableOk) {
        if (this.state !== S.SEARCH) {
          this.state = S.SEARCH;
          this.origin = null;
          this.resetReason = "PoseLost";
        }
        return [];
      }
      if (this.state === S.SEARCH) {
        this.state = S.IDLE;
        this.resetStroke(palm, t);
      }
      const metrics = (o) => {
        const dx = palm[0] - o[0], dy = palm[1] - o[1];
        const mag = hypot(dx, dy);
        const proj = dx * unit[0] + dy * unit[1];
        return [mag, proj, mag > 1e-6 ? proj / mag : 0, mag > 1e-6 ? mpr.quantizeDir8(dx, dy) : -1];
      };

      if (this.state === S.IDLE) {
        const [mag, , cos] = metrics(this.origin);
        const started = cos >= START_FORWARD_COS && (speed >= START_SPEED_FRAC * peak || mag >= START_DISP_FRAC * req);
        if (!started) {
          this.origin = palm;
          return [];
        }
        this.state = S.TRACK;
        this.peakLive = speed;
        this.resetReason = "None";
      }
      if (this.state === S.TRACK) {
        this.peakLive = Math.max(this.peakLive, speed);
        const [mag, proj, cos, d8] = metrics(this.origin);
        if (cos >= LIVE_COS_PASS && proj >= req * LIVE_REQ_FRAC) return [this.fire(t, d8)];
        if (mag >= REJECT_DISP_FRAC * req && cos < LIVE_COS_PASS) {
          this.state = S.REJ;
          this.resetReason = "WrongDirection";
          this.settleCount = 0;
        }
        return [];
      }
      if (this.state === S.LOCK) {
        this.peakLive = Math.max(this.peakLive, speed);
        let reverse = 0;
        if (this.firePoint) {
          const bx = palm[0] - this.firePoint[0], by = palm[1] - this.firePoint[1];
          reverse = -(bx * unit[0] + by * unit[1]);
        }
        this.revFrames = fwdVel < -1e-4 ? this.revFrames + 1 : 0;
        const rearmA = reverse >= REARM_REVERSE_FRAC * req && this.revFrames >= REARM_REVERSE_FRAMES;
        this.settleCount = speed <= REARM_SETTLE_SPEED_FRAC * peak ? this.settleCount + 1 : 0;
        const rearmB = this.settleCount >= REARM_SETTLE_FRAMES;
        if ((rearmA || rearmB) && t >= this.fireT + FIRE_LOCKOUT_S) {
          this.resetReason = rearmA ? "Reversed" : "Settled";
          this.state = S.IDLE;
          this.resetStroke(palm, t);
        }
        return [];
      }
      if (this.state === S.REJ) {
        this.settleCount = speed <= SETTLE_SPEED_FRAC * peak ? this.settleCount + 1 : 0;
        if (this.settleCount >= SETTLE_FRAMES) {
          this.resetReason = "Settled";
          this.state = S.IDLE;
          this.resetStroke(palm, t);
        }
      }
      return [];
    }
  }

  class Engine {
    constructor(cfg) {
      this.cfg = cfg || { jump_pose: null, jump_motion: null, crouch_pose: null, crouch_motion: null, stop_pose: null, mode_direction: false };
      this.jump = new StrokeChannel("JUMP", "jump_pose", "jump_motion");
      this.crouch = new StrokeChannel("CROUCH", "crouch_pose", "crouch_motion");
      this.stopActive = false;
      this.onEvi = 0;
      this.offEvi = 0;
      this.stopDistance = Infinity;
      this.stopCandidateSince = 0;
      this.stopOffCandidateSince = 0;
      this.stopOnIssued = 0;
      this.stopOffIssued = 0;
      this.stopReject = "no-model";
      this.stopCandidate = false;
      this.dirSign = 0;
      this.dirSince = 0;
      this.dirCommitted = false;
    }

    get resetReason() { return this.jump.resetReason; }

    modelsAll() {
      return [this.cfg.jump_pose, this.cfg.crouch_pose, this.cfg.stop_pose].filter(Boolean);
    }

    step(obs) {
      if (this.cfg.mode_direction) return this.stepDirection(obs);
      return [...this.stepStop(obs), ...this.jump.step(obs, this.modelsAll(), this.cfg), ...this.crouch.step(obs, this.modelsAll(), this.cfg)];
    }

    readiness(signalValid, teachingActive) {
      if (!signalValid || teachingActive || this.cfg.mode_direction) return "";
      if (this.stopActive || this.stopCandidate) return "STOP";
      if ((this.jump.state === S.IDLE || this.jump.state === S.TRACK) && this.jump.poseReason === pp.R_ARMED) return "JUMP";
      if ((this.crouch.state === S.IDLE || this.crouch.state === S.TRACK) && this.crouch.poseReason === pp.R_ARMED) return "CROUCH";
      return "";
    }

    stopDiagnostics(now) {
      const pose = this.cfg.stop_pose;
      const started = this.stopActive && this.stopOffCandidateSince > 0 ? this.stopOffCandidateSince : this.stopCandidateSince;
      return {
        active: this.stopActive,
        distance: this.stopDistance,
        threshold: pose ? pose.radius : 0,
        stableFrames: this.stopActive ? this.offEvi : this.onEvi,
        candidateStartedMs: started > 0 ? started * 1000 : 0,
        holdMs: started > 0 ? Math.max(0, (now - started) * 1000) : 0,
        onIssuedMs: this.stopOnIssued * 1000,
        offIssuedMs: this.stopOffIssued * 1000,
        rejectReason: this.stopReject,
      };
    }

    clearStopEvidence(reason) {
      this.onEvi = 0;
      this.offEvi = 0;
      this.stopCandidateSince = 0;
      this.stopOffCandidateSince = 0;
      this.stopCandidate = false;
      this.stopReject = reason;
    }

    stepStop(obs) {
      const pose = this.cfg.stop_pose;
      if (!pose) { this.stopCandidate = false; this.stopReject = "no-model"; return []; }
      if (!obs.tracked || !obs.landmarks) { this.clearStopEvidence("tracking-recovery"); return []; }   // 見失っても解除しない
      if (obs.quality < pp.TRACK_QUALITY_MIN) { this.clearStopEvidence("low-quality"); return []; }
      if (!pp.teachingGeometry(obs.landmarks)[0]) { this.clearStopEvidence("geometry-invalid"); return []; }
      const feat = pp.featureVector(obs.landmarks);
      const m = pp.matchPose(feat, this.modelsAll());
      const stopDistance = pp.poseDistance(feat, pose.centroid);
      const entryMatched = m.best === pose.name && m.armed;
      const heldMatched = stopDistance <= pose.radius * STOP_MATCH_RADIUS_K;
      const matched = this.stopActive ? heldMatched : entryMatched;
      this.stopDistance = stopDistance;
      this.stopCandidate = matched;
      if (matched) this.stopReject = this.stopActive ? "latched" : "candidate";
      else if (m.best !== pose.name) this.stopReject = m.reason === pp.R_AMBIG ? "ambiguous" : "other-class";
      else this.stopReject = m.reason || "distance";

      const cmds = [];
      if (!this.stopActive) {
        if (matched && this.onEvi === 0) this.stopCandidateSince = obs.t;
        else if (!matched) this.stopCandidateSince = 0;
        this.onEvi = matched ? this.onEvi + 1 : 0;
        this.offEvi = 0;
        this.stopOffCandidateSince = 0;
        if (this.onEvi >= STOP_ON_EVIDENCE) {
          this.stopActive = true;
          this.stopOnIssued = obs.t;
          this.stopReject = "latched";
          cmds.push({ type: "STOP_ON", t: obs.t });
        }
      } else {
        if (!matched && this.offEvi === 0) this.stopOffCandidateSince = obs.t;
        else if (matched) this.stopOffCandidateSince = 0;
        this.offEvi = !matched ? this.offEvi + 1 : 0;
        this.onEvi = 0;
        if (this.offEvi >= STOP_OFF_EVIDENCE) {
          this.stopActive = false;
          this.stopOffIssued = obs.t;
          this.stopCandidateSince = 0;
          this.stopOffCandidateSince = 0;
          this.stopReject = "released";
          cmds.push({ type: "STOP_OFF", t: obs.t });
        }
      }
      return cmds;
    }

    stepDirection(obs) {
      if (!obs.tracked || !obs.landmarks || obs.quality < pp.TRACK_QUALITY_MIN || !pp.teachingGeometry(obs.landmarks)[0]) {
        this.dirSign = 0;
        this.dirSince = 0;
        this.dirCommitted = false;
        return [];
      }
      const pts = obs.landmarks;
      const dx = pts[INDEX_TIP][0] - pts[INDEX_MCP][0];
      const dy = pts[INDEX_TIP][1] - pts[INDEX_MCP][1];
      const hand = pp.normalizeLandmarks(obs.landmarks);
      const idxLen = hypot(hand[INDEX_TIP][0] - hand[INDEX_MCP][0], hand[INDEX_TIP][1] - hand[INDEX_MCP][1]);
      const others = mean(OTHER_FINGERS.map(([t, m]) => hypot(hand[t][0] - hand[m][0], hand[t][1] - hand[m][1])));
      const ratio = idxLen / (others + 1e-6);
      const lateral = Math.abs(dx) >= DIR_LATERAL_DOMINANCE * Math.abs(dy);
      const extended = ratio >= DIR_INDEX_RATIO_MIN && idxLen >= DIR_INDEX_LEN_MIN;
      if (!(lateral && extended)) {
        this.dirSign = 0;
        this.dirSince = 0;
        return [];
      }
      const sign = dx > 0 ? 1 : -1;
      if (sign !== this.dirSign) {
        this.dirSign = sign;
        this.dirSince = obs.t;
        this.dirCommitted = false;
        return [];
      }
      if (!this.dirCommitted && obs.t - this.dirSince >= DIR_HOLD_S) {
        this.dirCommitted = true;
        return [{ type: "DIRECTION", t: obs.t, direction: sign }];
      }
      return [];
    }

    directionHoldProgress(now) {
      if (this.dirSign === 0 || this.dirSince <= 0) return 0;
      if (this.dirCommitted) return 1;
      return clamp((now - this.dirSince) / DIR_HOLD_S, 0, 1);
    }
  }

  return { Engine, StrokeChannel, S };
})();

/* ---------------- teach_controller.py（本番で使う部分） ---------------- */
class TeachController {
  constructor() { this.resetAll(); }

  resetAll() {
    this.jump_takes = [];
    this.crouch_takes = [];
    this.stop_takes = [];
    this.jump_rejected = this.crouch_rejected = this.stop_rejected = 0;
    this.strokes = [];
    this.dts = [];
    this.crouch_strokes = [];
    this.crouch_dts = [];
    this.jump_model = this.crouch_model = this.stop_model = null;
    this.jump_motion = this.crouch_motion = null;
    this.verified = false;
    this.geom_valid = false;
    this.geom_reason = "";
    this.last_reason = "";
    this.conf = 0;
    this.engine = null;
  }

  ensureEngine() {
    if (!this.engine) this.engine = new ce.Engine();
    return this.engine;
  }

  // 学習が終わったモデルで、毎フレームの合図を判定する
  feed(landmarks, quality, palm, t, tracked) {
    this.conf = quality;
    if (landmarks) [this.geom_valid, this.geom_reason] = pp.teachingGeometry(landmarks);
    else [this.geom_valid, this.geom_reason] = [false, "HAND OUT OF FRAME"];
    const eng = this.ensureEngine();
    Object.assign(eng.cfg, {
      jump_pose: this.jump_model, jump_motion: this.jump_motion,
      crouch_pose: this.crouch_model, crouch_motion: this.crouch_motion,
      stop_pose: this.stop_model,
    });
    if (this.jump_model || this.stop_model || this.crouch_model) {
      return eng.step({ t, tracked, quality, landmarks, palm });
    }
    return [];
  }

  resetCommand(kind) {
    if (!["jump", "crouch", "stop"].includes(kind)) return false;
    this.last_reason = "";
    if (kind === "jump") {
      this.jump_takes = []; this.jump_rejected = 0; this.strokes = []; this.dts = [];
      this.jump_model = null; this.jump_motion = null; this.verified = false;
    } else if (kind === "crouch") {
      this.crouch_takes = []; this.crouch_rejected = 0; this.crouch_strokes = []; this.crouch_dts = [];
      this.crouch_model = null; this.crouch_motion = null;
    } else {
      this.stop_takes = []; this.stop_rejected = 0; this.stop_model = null;
    }
    const e = this.engine;
    if (e) {
      if (kind === "jump") {
        e.cfg.jump_pose = null; e.cfg.jump_motion = null;
        e.jump = new ce.StrokeChannel("JUMP", "jump_pose", "jump_motion");
      } else if (kind === "crouch") {
        e.cfg.crouch_pose = null; e.cfg.crouch_motion = null;
        e.crouch = new ce.StrokeChannel("CROUCH", "crouch_pose", "crouch_motion");
      } else {
        e.cfg.stop_pose = null; e.stopActive = false; e.onEvi = 0; e.offEvi = 0;
      }
    }
    return true;
  }
}

/* ---------------- production_control.py ---------------- */
const KINDS = ["jump", "crouch", "stop"];
const MIN_STROKE_DISPLACEMENT = 0.03;

class ProductionTeachingRuntime {
  constructor(teach) {
    this.teach = teach;
    this.active = false;
    this.kind = "";
    this.repair = false;
    this.required = 0;
    this.accepted = 0;
    this.phase = "idle";
    this.reason = "";
    this.event_id = 0;
    this.finalized = false;
    this.direction_mode = false;
    this.ack_action = "";
    this.ack_target = "";
    this.ack_id = 0;
    this.sessionTakes = [];
    this.sessionStrokes = [];
    this.sessionDts = [];
    this.clearCapture();
  }

  snapshot() {
    const { active, kind, repair, required, accepted, phase, reason, event_id, finalized, direction_mode, ack_action, ack_target, ack_id } = this;
    return { active, kind, repair, required, accepted, phase, reason, event_id, finalized, direction_mode, ack_action, ack_target, ack_id, rgb_frames_sent: 0 };
  }

  begin(kind, repair) {
    if (!KINDS.includes(kind)) return;
    this.clearCapture();
    Object.assign(this, { active: true, kind, repair: !!repair, required: repair ? 1 : pp.TAKES_REQUIRED, accepted: 0, phase: "ready", reason: "", finalized: false });
    this.sessionTakes = []; this.sessionStrokes = []; this.sessionDts = [];
  }

  cancel() {
    this.clearCapture();
    Object.assign(this, { active: false, kind: "", repair: false, required: 0, accepted: 0, phase: "idle", reason: "", finalized: false });
    this.sessionTakes = []; this.sessionStrokes = []; this.sessionDts = [];
  }

  reset() {
    this.cancel();
    this.direction_mode = false;
    this.ack_action = ""; this.ack_target = ""; this.ack_id = 0;
    this.teach.resetAll();
  }

  resetCommand(target, requestId) {
    target = String(target).toLowerCase();
    if (!KINDS.includes(target) || !(requestId > 0)) return false;
    this.clearCapture();
    if (!this.teach.resetCommand(target)) return false;
    Object.assign(this, { active: true, kind: target, repair: false, required: pp.TAKES_REQUIRED, accepted: 0, phase: "ready", reason: "", finalized: false });
    this.sessionTakes = []; this.sessionStrokes = []; this.sessionDts = [];
    this.ack_action = "reset_command"; this.ack_target = target; this.ack_id = requestId;
    return true;
  }

  setDirectionMode(enabled) {
    this.direction_mode = !!enabled;
    this.teach.ensureEngine().cfg.mode_direction = this.direction_mode;
  }

  press(t) {
    if (!this.active || this.pressed || this.finalized) return;
    this.clearCapture();
    this.pressed = true;
    this.phase = "stabilizing";
    this.reason = "";
    this.trackStarted = t;
  }

  release(t) {
    if (!this.active || !this.pressed) return;
    this.pressed = false;
    if (this.lost) { this.reject("tracking_lost"); return; }
    if (!this.pose) {
      if (this.frames.length >= pp.MIN_VALID_FRAMES) {
        const [, , , reason] = pp.takeStability(this.recording());
        this.reject(reason === "UNSTABLE" ? "unstable" : "early_release");
      } else if (this.invalidReason) {
        this.reject(ProductionTeachingRuntime.geometryReason(this.invalidReason));
      } else {
        this.reject("early_release");
      }
      return;
    }
    if (this.kind !== "stop" && (this.track.length < 2 || this.strokeDisplacement() < MIN_STROKE_DISPLACEMENT)) {
      this.reject("early_release");
      return;
    }
    this.acceptCandidate(t);
  }

  // 教えている間（SPACE を押している間）だけ、カメラの観測を使う
  feed(landmarks, quality, palm, t, tracked) {
    if (landmarks) [this.teach.geom_valid, this.teach.geom_reason] = pp.teachingGeometry(landmarks);
    else [this.teach.geom_valid, this.teach.geom_reason] = [false, "HAND OUT OF FRAME"];
    this.teach.conf = quality;
    if (!this.active || !this.pressed) return;
    if (!tracked || !landmarks || !palm) { this.lost = true; return; }
    const [geomOk, geomReason] = pp.teachingGeometry(landmarks);
    if (!geomOk || quality < pp.TRACK_QUALITY_MIN) {
      this.invalidReason = !geomOk ? geomReason : "TRACKING LOST";
      // 記録途中の欠落をまたいで別の動きを1つの見本として採用しない。
      if (this.pose) this.lost = true;
      else this.frames = [];
      return;
    }
    if (!this.pose) {
      this.frames.push({ landmarks: landmarks.map((p) => [...p]), quality });
      if (this.frames.length < pp.MIN_VALID_FRAMES) return;
      const rec = this.recording();
      if (!pp.takeStability(rec)[0]) return;
      this.pose = rec;
      this.track = [[palm[0], palm[1]]];
      this.trackStarted = t;
      this.phase = this.kind === "stop" ? "release" : "moving";
      return;
    }
    if (this.kind !== "stop") {
      this.track.push([palm[0], palm[1]]);
      if (this.strokeDisplacement() >= MIN_STROKE_DISPLACEMENT) this.phase = "release";
    }
  }

  recording() { return new pp.Recording(this.frames.map((f) => ({ landmarks: f.landmarks, quality: f.quality }))); }

  strokeDisplacement() {
    if (this.track.length < 2) return 0;
    const a = this.track[0], b = this.track[this.track.length - 1];
    return hypot(b[0] - a[0], b[1] - a[1]);
  }

  acceptCandidate(t) {
    const take = this.pose;
    const stroke = [...this.track];
    const dt = Math.max(1e-3, t - this.trackStarted);
    let refTakes = this.sessionTakes, refStrokes = this.sessionStrokes;
    if (this.repair) [refTakes, refStrokes] = this.storedSamples();
    if (refTakes.length && !ProductionTeachingRuntime.poseConsistent(take, refTakes)) { this.reject("inconsistent"); return; }
    if (this.kind !== "stop" && refStrokes.length && !ProductionTeachingRuntime.motionConsistent(stroke, refStrokes)) { this.reject("inconsistent"); return; }
    let takes = [...this.sessionTakes, take];
    let strokes = [...this.sessionStrokes, ...(this.kind !== "stop" ? [stroke] : [])];
    let dts = [...this.sessionDts, ...(this.kind !== "stop" ? [dt] : [])];
    const completing = takes.length >= this.required;
    if (completing) {
      if (this.repair) {
        const [oldTakes, oldStrokes, oldDts] = this.storedSamples();
        if (oldTakes.length < 2 || (this.kind !== "stop" && oldStrokes.length < 2)) { this.reject("inconsistent"); return; }
        takes = [...oldTakes.slice(-2), take];
        if (this.kind !== "stop") {
          strokes = [...oldStrokes.slice(-2), stroke];
          dts = [...oldDts.slice(-2), dt];
        }
      }
      let candidate;
      try {
        candidate = this.buildCandidate(takes, strokes, dts);
      } catch (e) {
        this.reject("inconsistent");
        return;
      }
      this.commit(takes, strokes, dts, candidate);
    }
    this.sessionTakes.push(take);
    if (this.kind !== "stop") { this.sessionStrokes.push(stroke); this.sessionDts.push(dt); }
    this.accepted++;
    this.finalized = completing;
    this.phase = "accepted";
    this.reason = "";
    this.event_id++;
    this.clearCapture(true);
  }

  storedSamples() {
    const tc = this.teach;
    if (this.kind === "jump") return [[...tc.jump_takes], [...tc.strokes], [...tc.dts]];
    if (this.kind === "crouch") return [[...tc.crouch_takes], [...tc.crouch_strokes], [...tc.crouch_dts]];
    return [[...tc.stop_takes], [], []];
  }

  static poseConsistent(candidate, references) {
    const medoid = (rec) => pp.medianRows(rec.validFrames().map((f) => pp.featureVector(f.landmarks)));
    const target = medoid(candidate);
    const reference = pp.medianRows(references.map(medoid));
    return pp.poseDistance(target, reference) <= pp.TAKE_INCONSISTENT_MAX;
  }

  static motionConsistent(candidate, references) {
    const dir = (tr) => mpr.quantizeDir8(tr[tr.length - 1][0] - tr[0][0], tr[tr.length - 1][1] - tr[0][1]);
    return dir(candidate) === bincountArgmax(references.map(dir));
  }

  buildCandidate(takes, strokes, dts) {
    const copy = (m) => (m ? { ...m, centroid: [...m.centroid] } : null);
    let jump = copy(this.teach.jump_model), crouch = copy(this.teach.crouch_model), stop = copy(this.teach.stop_model);
    let motion = null;
    const pose = pp.registerPose(this.kind.toUpperCase(), takes);
    if (this.kind === "jump") {
      motion = mpr.learnJumpMotion(strokes, dts);
      if (crouch && !pp.isSeparable(pose, crouch)) throw new pp.RegistrationError("JUMP too close to CROUCH");
      if (stop && !pp.isSeparable(pose, stop)) throw new pp.RegistrationError("JUMP too close to STOP");
      if (crouch) pp.constrainSeparation(pose, crouch);
      if (stop) pp.constrainSeparation(pose, stop);
      jump = pose;
    } else if (this.kind === "crouch") {
      motion = mpr.learnJumpMotion(strokes, dts);
      if (jump && !pp.isSeparable(pose, jump)) throw new pp.RegistrationError("CROUCH too close to JUMP");
      if (stop && !pp.isSeparable(pose, stop)) throw new pp.RegistrationError("CROUCH too close to STOP");
      if (jump) pp.constrainSeparation(jump, pose);
      if (stop) pp.constrainSeparation(stop, pose);
      crouch = pose;
    } else {
      if (crouch && !pp.isSeparable(pose, crouch)) throw new pp.RegistrationError("STOP too close to CROUCH");
      if (jump && !pp.isSeparable(pose, jump)) throw new pp.RegistrationError("STOP too close to JUMP");
      if (crouch) pp.constrainSeparation(pose, crouch);
      if (jump) pp.constrainSeparation(jump, pose);
      stop = pose;
    }
    return [jump, crouch, stop, motion];
  }

  commit(takes, strokes, dts, [jump, crouch, stop, motion]) {
    const tc = this.teach;
    tc.jump_model = jump;
    tc.crouch_model = crouch;
    tc.stop_model = stop;
    if (this.kind === "jump") {
      tc.jump_takes = [...takes]; tc.strokes = [...strokes]; tc.dts = [...dts]; tc.jump_motion = motion; tc.verified = true;
    } else if (this.kind === "crouch") {
      tc.crouch_takes = [...takes]; tc.crouch_strokes = [...strokes]; tc.crouch_dts = [...dts]; tc.crouch_motion = motion;
    } else {
      tc.stop_takes = [...takes];
    }
  }

  reject(reason) {
    this.reason = reason;
    this.phase = "rejected";
    this.finalized = false;
    this.event_id++;
    if (this.kind === "jump") this.teach.jump_rejected++;
    else if (this.kind === "crouch") this.teach.crouch_rejected++;
    else if (this.kind === "stop") this.teach.stop_rejected++;
    this.teach.last_reason = reason;
    this.clearCapture(true);
  }

  clearCapture(keepPhase = false) {
    this.pressed = false;
    this.lost = false;
    this.invalidReason = "";
    this.frames = [];
    this.pose = null;
    this.track = [];
    this.trackStarted = 0;
    if (!keepPhase && this.active) this.phase = "ready";
  }

  static geometryReason(r) {
    if (r === "HAND OUT OF FRAME" || r === "TRACKING LOST") return "tracking_lost";
    if (r === "MOVE CLOSER") return "too_far";
    return "out_of_frame";
  }
}

/* ---------------- net_protocol.py / udp_sender.py（送り先が Unity の SendMessage になる） ---------------- */
const nowUs = () => Math.round((performance.timeOrigin + performance.now()) * 1000);
const r4 = (x) => Math.round(x * 1e4) / 1e4;
const finite = (v, fb = 0) => (Number.isFinite(v) ? v : fb);

class WebSender {
  constructor(deliver) {
    this.deliver = deliver;
    this.session = Array.from(crypto.getRandomValues(new Uint8Array(6)), (b) => b.toString(16).padStart(2, "0")).join("");
    this.sstart = nowUs();
    this.cmdId = 0;
  }

  sendCommand(ctype, direction) {
    this.cmdId++;
    const t = nowUs();
    for (let seq = 1; seq <= 3; seq++) {
      const p = { v: 1, kind: "cmd", session: this.session, sstart: this.sstart, cmdId: this.cmdId, seq, tUs: t, type: ctype, phase: "fire", conf: 1, poseD: 0, motion: 1, track: 1 };
      if (direction !== undefined && direction !== null) p.dir = direction;
      this.deliver(JSON.stringify(p));
    }
  }

  sendLandmarks(pts, d) {
    const flat = [];
    if (pts) for (const p of pts) flat.push(r4(p[0]), r4(p[1]));
    const p = {
      v: 1, kind: "lm", session: this.session, sstart: this.sstart, tUs: nowUs(), pts: flat,
      ready: ["JUMP", "CROUCH", "STOP"].includes(d.readiness) ? d.readiness : "",
      signal: d.signal,
      trackQ: r4(d.quality),
      dir: d.dir === -1 || d.dir === 1 ? d.dir : 0,
      dirHold: r4(clamp(d.dirHold, 0, 1)),
      owner: d.owner,
      stopD: Math.round(finite(d.stop ? d.stop.distance : -1, -1) * 1e5) / 1e5,
      stopTh: Math.round(finite(d.stop ? d.stop.threshold : 0) * 1e5) / 1e5,
      stopCandidateMs: Math.round(finite(d.stop ? d.stop.candidateStartedMs : 0) * 1e3) / 1e3,
      stopStable: d.stop ? d.stop.stableFrames : 0,
      stopHoldMs: Math.round(Math.max(0, finite(d.stop ? d.stop.holdMs : 0)) * 1e3) / 1e3,
      stopOnMs: Math.round(Math.max(0, finite(d.stop ? d.stop.onIssuedMs : 0)) * 1e3) / 1e3,
      stopOffMs: Math.round(Math.max(0, finite(d.stop ? d.stop.offIssuedMs : 0)) * 1e3) / 1e3,
      stopReject: String(d.stop ? d.stop.rejectReason : "").slice(0, 48),
      stopActive: !!(d.stop && d.stop.active),
    };
    this.deliver(JSON.stringify(p));
  }
}

/* ---------------- bench_app.py の毎フレームの処理（カメラ → 認識 → Unity） ---------------- */
// 3フレームの中央値で単発の座標飛びを除去。欠落・時間の空白では履歴を捨てる。
// 左右は勝手に統合せず、教えた手で再現する（方向の意味を保つ）。
class LandmarkFilter {
  constructor() { this.reset(); }
  reset() { this.frames = []; this.lastTime = null; }
  update(lm, now) {
    if (!lm || lm.length !== 21 || lm.some(p => p.length < 2 || !p.every(Number.isFinite))) {
      this.reset(); return null;
    }
    if (this.lastTime !== null && (now - this.lastTime > 0.2 || now <= this.lastTime)) this.reset();
    this.lastTime = now;
    this.frames.push(lm);
    if (this.frames.length > 3) this.frames.shift();
    if (this.frames.length < 3) return null;
    return lm.map((p, i) => p.map((_, j) => median(this.frames.map(f => f[i][j]))));
  }
}

export function createSidecar({ deliver, video, onStatus = () => {}, onDiagnostics = () => {} }) {
  const teach = new TeachController();
  const production = new ProductionTeachingRuntime(teach);
  const sender = new WebSender(deliver);
  const pending = [];
  let recognizer = null;
  let running = false;
  let starting = null;
  let generation = 0;
  let frameId = 0;
  let lastVideoTime = -1;
  let fpsTimes = [];
  const filter = new LandmarkFilter();
  let diagnostics = null;
  let lastCommand = null;
  let lastBrightnessAt = -Infinity;
  let brightness = null;
  let brightnessCanvas = null;
  let brightnessContext = null;
  let lastStepAt = null;

  function resetEvidence(now) {
    filter.reset();
    const e = teach.engine;
    if (e) {
      // STOP は元作品と同じく保持。カメラの欠落だけでは走り出さない。
      e.jump = new ce.StrokeChannel("JUMP", "jump_pose", "jump_motion");
      e.crouch = new ce.StrokeChannel("CROUCH", "crouch_pose", "crouch_motion");
      e.clearStopEvidence("tracking-recovery");
      e.dirSign = 0; e.dirSince = 0; e.dirCommitted = false;
    }
    if (production.pressed) production.reject("tracking_lost");
  }

  function sampleBrightness(nowMs) {
    if (nowMs - lastBrightnessAt < 500) return;
    lastBrightnessAt = nowMs;
    if (!brightnessCanvas) {
      brightnessCanvas = document.createElement("canvas");
      brightnessCanvas.width = 16; brightnessCanvas.height = 12;
      brightnessContext = brightnessCanvas.getContext("2d", { willReadFrequently: true });
    }
    brightnessContext.drawImage(video, 0, 0, 16, 12);
    const pixels = brightnessContext.getImageData(0, 0, 16, 12).data;
    let total = 0;
    for (let i = 0; i < pixels.length; i += 4) total += .2126 * pixels[i] + .7152 * pixels[i + 1] + .0722 * pixels[i + 2];
    brightness = total / (16 * 12); // 暗さの目安だけ。手の信頼度として扱わない。
    brightnessContext.clearRect(0, 0, 16, 12);
  }

  // Unity からの制御（Python の HTTP 制御の代わり）。次のフレームの最初にまとめて処理する
  function control(json) {
    if (json) {
      try {
        const msg = JSON.parse(json);
        if (msg && typeof msg === "object") pending.push(msg);
      } catch (e) {
        return JSON.stringify({ error: "bad_request" });
      }
    }
    return JSON.stringify(production.snapshot());
  }

  function processPending(t) {
    while (pending.length) {
      const msg = pending.shift();
      const action = String(msg.action || "");
      if (action === "begin") production.begin(String(msg.kind || "").toLowerCase(), !!msg.repair);
      else if (action === "press") production.press(t);
      else if (action === "release") production.release(t);
      else if (action === "cancel") production.cancel();
      else if (action === "reset") { production.reset(); filter.reset(); lastCommand = null; }
      else if (action === "reset_command") production.resetCommand(String(msg.target || ""), parseInt(msg.request_id || 0, 10));
      else if (action === "direction") production.setDirectionMode(!!msg.enabled);
    }
  }

  function start() {
    if (running) return Promise.resolve(true);
    if (starting) return starting;
    const attempt = ++generation;
    const task = (async () => {
    try {
      onStatus("camera");
      const stream = await navigator.mediaDevices.getUserMedia({ video: { width: 640, height: 480, facingMode: "user" }, audio: false });
      if (attempt !== generation) { stream.getTracks().forEach(t => t.stop()); return false; }
      video.srcObject = stream;
      await video.play();
      if (attempt !== generation) return false;
      onStatus("model");
      const { FilesetResolver, GestureRecognizer } = await import("./mediapipe/vision_bundle.js");
      const fileset = await FilesetResolver.forVisionTasks("./mediapipe/wasm");
      if (attempt !== generation) return false;
      const model = await GestureRecognizer.createFromOptions(fileset, {
        baseOptions: { modelAssetPath: "./mediapipe/gesture_recognizer.task", delegate: "GPU" },
        runningMode: "VIDEO",
        numHands: 1,
        minHandDetectionConfidence: 0.6,
        minHandPresenceConfidence: 0.6,
        minTrackingConfidence: 0.6,
      });
      if (attempt !== generation) { model.close(); return false; }
      recognizer = model;
      for (const track of stream.getVideoTracks()) track.addEventListener("ended", () => {
        if (running) { stop(); onStatus("error", { name: "NotReadableError" }); }
      });
      running = true;
      lastVideoTime = -1;
      onStatus("ready");
      frameId = requestAnimationFrame(loop);
      return true;
    } catch (e) {
      if (attempt === generation) { stop(); onStatus("error", e); }
      return false;
    }
    })();
    starting = task;
    task.finally(() => { if (starting === task) starting = null; });
    return task;
  }

  function stop() {
    generation++;
    starting = null;
    running = false;
    cancelAnimationFrame(frameId);
    const s = video.srcObject;
    if (s) s.getTracks().forEach((tr) => tr.stop());
    video.srcObject = null;
    recognizer?.close();
    recognizer = null;
    lastVideoTime = -1;
    fpsTimes = [];
    pending.length = 0;
    resetEvidence(performance.now() / 1000);
    lastStepAt = null;
    brightness = null;
    lastBrightnessAt = -Infinity;
    diagnostics = null;
    sender.sendLandmarks(null, { readiness: "", signal: "tracking_lost", quality: 0, owner: "live", dir: 0, dirHold: 0, stop: teach.engine?.stopDiagnostics(performance.now() / 1000) });
    onStatus("idle");
  }

  function loop() {
    if (!running) return;
    if (!document.hidden && video.readyState >= 2 && video.currentTime !== lastVideoTime) {
      lastVideoTime = video.currentTime;
      const nowMs = performance.now();
      try { sampleBrightness(nowMs); step(recognizer.recognizeForVideo(video, nowMs), nowMs / 1000); }
      catch (error) { stop(); onStatus("error", error); return; }
      fpsTimes.push(nowMs);
      if (fpsTimes.length > 30) fpsTimes.shift();
    }
    frameId = requestAnimationFrame(loop);
  }

  // 1フレーム分：Python の bench_app.run() のループの中身と同じ順番
  function step(res, now) {
    if (lastStepAt !== null && now - lastStepAt > 0.2) resetEvidence(now);
    lastStepAt = now;
    const detected = !!(res.landmarks && res.landmarks.length);
    // 鏡のように左右反転した座標にする（Python は反転した映像をそのまま認識していた）
    const raw = detected ? res.landmarks[0].map((p) => [1 - p.x, p.y, p.z ?? 0]) : null;
    const rawValid = raw && raw.length === 21 && raw.every(p => p.every(Number.isFinite));
    const geometry = rawValid ? pp.teachingGeometry(raw) : [false, "TRACKING LOST"];
    // 画像外の手と小さすぎる手は履歴に混ぜない。handedness.score は手の検出確率ではない。
    const handSize = rawValid ? hypot(raw[5][0] - raw[17][0], raw[5][1] - raw[17][1]) : 0;
    const quality = geometry[0] ? clamp(handSize / .09, 0, 1) : 0;
    const lm = filter.update(geometry[0] && handSize >= .06 ? raw : null, now);
    const tracked = !!lm;
    const palm = lm ? [mean(pp.PALM_IDS.map((i) => lm[i][0])), mean(pp.PALM_IDS.map((i) => lm[i][1]))] : null;

    processPending(now);
    let cmds = [];
    if (production.active) production.feed(lm, quality, palm, now, tracked);
    else cmds = teach.feed(lm, quality, palm, now, tracked);
    const engine = teach.engine;

    for (const c of cmds) {
      let key = c.type;
      if (key === "DIRECTION") key = c.direction < 0 ? "LEFT" : "RIGHT";
      sender.sendCommand(key, c.type === "DIRECTION" ? c.direction : null);
      lastCommand = { type: key, at: now };
    }

    let signal = "ok";
    if (!tracked || !lm) signal = "tracking_lost";
    else if (quality < pp.TRACK_QUALITY_MIN) signal = "low_quality";
    else if (!teach.geom_valid) signal = "out_of_frame";
    const readiness = engine ? engine.readiness(signal === "ok", production.active) : "";
    const owner = production.active ? "teaching" : engine && engine.cfg.mode_direction ? "direction" : "live";
    sender.sendLandmarks(lm ? lm.map((p) => [p[0], p[1]]) : null, {
      readiness, signal, quality, owner,
      dir: engine ? engine.dirSign : 0,
      dirHold: engine ? engine.directionHoldProgress(now) : 0,
      stop: engine ? engine.stopDiagnostics(now) : null,
    });
    const models = [teach.jump_model, teach.crouch_model, teach.stop_model].filter(Boolean);
    const match = lm ? pp.matchPose(pp.featureVector(lm), models) : null;
    const bestModel = models.find(m => m.name === match?.best);
    // 距離に基づく一致度。確率ではなく、しきい値上は50%、完全一致は100%。
    const confidence = bestModel ? clamp(1 - match.bestDist / (2 * bestModel.radius), 0, 1) : 0;
    let handState = !detected ? "missing" : !rawValid ? "invalid" : !geometry[0] ? geometry[1] === "MOVE CLOSER" ? "small" : "edge" : handSize < .06 ? "small" : !tracked ? "recovering" : "ok";
    diagnostics = {
      now, handState, landmarks: lm, quality, brightness, teaching: production.snapshot(),
      captureFrames: production.frames.length, requiredFrames: pp.MIN_VALID_FRAMES,
      pose: match?.best ?? null, confidence, matchReason: match?.reason ?? "NoModel", readiness,
      gesture: res.gestures?.[0]?.[0] ?? null, lastCommand,
      stage: production.active ? "TEACH" : models.length ? "USE" : "DEFINE",
      learned: KINDS.map(kind => ({ kind, learned: !!teach[kind + "_model"],
        takes: production.active && production.kind === kind ? production.accepted : teach[kind + "_takes"].length,
        required: production.active && production.kind === kind ? production.required : pp.TAKES_REQUIRED })),
    };
    onDiagnostics(diagnostics);
  }

  const fps = () => (fpsTimes.length > 1 ? ((fpsTimes.length - 1) * 1000) / (fpsTimes[fpsTimes.length - 1] - fpsTimes[0]) : 0);

  return { start, stop, control, fps, diagnostics: () => diagnostics, _test: { pp, mpr, ce, teach, production, step, LandmarkFilter } };
}
