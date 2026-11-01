// Unity から届く学習状態と、実際の認識結果で案内を更新します。
const names = { jump: "ジャンプ", crouch: "しゃがむ", stop: "止まる", JUMP: "ジャンプ", CROUCH: "しゃがむ", STOP: "止まる", STOP_ON: "止まる", STOP_OFF: "停止を解除", LEFT: "左を選ぶ", RIGHT: "右を選ぶ" };
const shapes = { Open_Palm: "開いた手", Closed_Fist: "握った手", Pointing_Up: "人差し指を立てた手", Thumb_Up: "親指を上に向けた手", Thumb_Down: "親指を下に向けた手", Victory: "V の形", ILoveYou: "親指・人差し指・小指を立てた手" };
const errors = {
  tracking_lost: "手を見失いました。片手全体を映し、最初から記録し直します。",
  too_far: "手が小さく映っています。カメラに少し近づけてやり直します。",
  out_of_frame: "手が端に近すぎます。中央に戻してやり直します。",
  early_release: "キーを離すのが早すぎました。次の案内が出るまで押し続けます。",
  unstable: "手の形が揺れています。まず形を保ってから動かします。",
  inconsistent: "前の見本と違うか、別の合図と似ています。同じ形・方向でやり直します。形を変えるときは R を押します。",
};
const links = [[0,1,2,3,4],[0,5,6,7,8],[5,9,10,11,12],[9,13,14,15,16],[13,17,18,19,20],[0,17]];

export function createGuide() {
  const el = id => document.getElementById(id);
  const text = (id, value) => { if (el(id).textContent !== value) el(id).textContent = value; };
  let status = "idle", seenHand = false, used = false, lastPaint = -Infinity, lastEvent = -1;
  const ctx = el("handView").getContext("2d");
  function skeleton(lm) {
    ctx.clearRect(0, 0, 240, 150);
    ctx.strokeStyle = "#26314a";
    ctx.strokeRect(12, 8, 216, 134);
    if (!lm) { ctx.fillStyle = "#8f98ab"; ctx.font = "12px sans-serif"; ctx.textAlign = "center"; ctx.fillText("片手全体を枠の中へ", 120, 80); return; }
    ctx.strokeStyle = "#6fa8ff"; ctx.lineWidth = 2;
    for (const chain of links) {
      ctx.beginPath();
      chain.forEach((i, j) => { const [x, y] = lm[i]; ctx[j ? "lineTo" : "moveTo"](x * 240, y * 150); });
      ctx.stroke();
    }
    ctx.fillStyle = "#e9edf5";
    for (const [x, y] of lm) { ctx.beginPath(); ctx.arc(x * 240, y * 150, 2.2, 0, Math.PI * 2); ctx.fill(); }
  }
  function tutorial(step) {
    document.querySelectorAll(".tutorial li").forEach((item, i) => {
      item.classList.toggle("done", i < step);
      if (i === step) item.setAttribute("aria-current", "step");
      else item.removeAttribute("aria-current");
    });
  }
  function next(stage, title, action) { text("stageLabel", stage); text("nextTitle", title); text("nextAction", action); }
  skeleton(null);
  return {
    unavailable(message) { next("利用環境の確認", "PC で開きます", message); },
    status(value, error = "") {
      status = value; lastPaint = -Infinity;
      if (value === "ready") {
        next("準備 · DEFINE", "2. 片手全体を映します", "骨格が表示されたら、ゲーム画面の案内に沿って始めます。");
        tutorial(1);
      } else {
        const message = value === "camera" ? "ブラウザのカメラ使用を「許可」にします。" : value === "model" ? "手の認識を準備しています。そのまま待ちます。" : error || "「カメラを使ってはじめる」または「カメラを再開する」を押します。";
        next("準備 · DEFINE", value === "error" ? "カメラを確認します" : "1. カメラを許可します", message);
        text("handLabel", value === "error" ? "カメラを使用できません" : value === "idle" ? "カメラメニューから再開できます" : "カメラを準備しています");
        text("shapeLabel", "—"); text("matchLabel", "認識は停止中です"); el("matchMeter").value = 0;
        skeleton(null); tutorial(0);
      }
    },
    update(d) {
      if (status !== "ready") return;
      const p = d.teaching;
      if (d.now - lastPaint < .1 && p.event_id === lastEvent) return;
      lastPaint = d.now; lastEvent = p.event_id;
      const ok = d.handState === "ok";
      seenHand ||= ok;
      used = !!d.lastCommand;
      const learned = d.learned.some(x => x.learned);
      tutorial(used ? 4 : learned ? 3 : seenHand ? 2 : 1);
      skeleton(d.landmarks);
      const handMessages = {
        missing: ["手が映っていません", "片手全体をカメラに向けます。"],
        invalid: ["手を確認できません", "片手を中央に戻し、形を保ちます。"],
        edge: ["手が枠から切れています", "指先まで入るよう、手を中央に戻します。"],
        small: ["手が小さく映っています", "手全体が入る範囲で、少し近づけます。"],
        recovering: ["手を確認しています", "そのまま少し形を保ちます。"],
        ok: ["手を認識しています", "教えたときと同じ手を使います。"],
      };
      const [label, hint] = handMessages[d.handState];
      text("handLabel", label);
      text("handHint", d.brightness !== null && d.brightness < 45 ? "映像が暗い可能性があります。照明を明るくし、逆光を避けます。" : hint);
      const shape = d.gesture;
      text("shapeLabel", ok ? shapes[shape?.categoryName] && shape.score >= .6 ? shapes[shape.categoryName] : "自由な手の形" : "—");
      const percent = Math.round(d.confidence * 100);
      text("matchLabel", !ok ? "手の認識を待っています" : !d.pose ? "まだ合図を教えていません" : `${names[d.pose]} · ${percent}%${d.matchReason === "ClassAmbiguous" ? "（似た合図があります）" : d.matchReason === "DistanceTooLarge" ? "（形を合わせます）" : ""}`);
      el("matchMeter").value = ok ? percent : 0;
      text("commandLabel", d.lastCommand ? `最後に発動した合図：${names[d.lastCommand.type] || d.lastCommand.type}` : "発動した合図：まだありません");
      for (const item of d.learned) {
        const row = document.querySelector(`[data-kind="${item.kind}"]`);
        row.classList.toggle("learned-ok", item.learned);
        const label = `${item.learned ? "登録済み" : "未登録"} · ${item.takes} / ${item.required}${p.active && p.kind === item.kind ? "（教え中）" : ""}`;
        if (row.lastElementChild.textContent !== label) row.lastElementChild.textContent = label;
      }
      if (!ok) { next(`${d.stage} · 手を確認します`, "片手全体を映します", el("handHint").textContent); return; }
      if (p.active) {
        const kind = names[p.kind];
        let title = `${kind}を教えます · ${p.accepted} / ${p.required}`;
        let action = "ゲーム画面をクリックし、決めた手の形で Space を押し続けます。";
        if (p.phase === "stabilizing") { title = "形を保ちます"; action = "Space を押したまま、次の案内が出るまで手の形を保ちます。"; }
        if (p.phase === "moving") { title = "決めた方向に動かします"; action = "Space を押したまま、同じ手の形で動かします。"; }
        if (p.phase === "release") { title = "Space を離します"; action = "見本を記録できました。Space を離して確定します。"; }
        if (p.phase === "accepted") action = p.finalized ? "登録できました。ゲーム画面の案内に沿って、合図を使います。" : `見本を受け取りました。同じ形と動きで、あと ${p.required - p.accepted} 回教えます。`;
        if (p.phase === "rejected") action = errors[p.reason] || "見本を記録できませんでした。手の形を保って、もう一度教えます。";
        next("II · TEACH / 教える", title, action);
      } else if (d.teaching.direction_mode) {
        next("USE · 方向を選びます", "人差し指で左右を示します", "人差し指を横に向け、少し保ちます。ゲーム画面の案内に合わせます。");
      } else if (learned) {
        next("III · USE / 伝える", d.readiness ? `${names[d.readiness]}の形を認識しています` : "教えた合図を使います", d.readiness === "STOP" ? "止まる形を保ちます。別の形に変えると停止を解除します。" : "教えた手の形を保ち、教えた方向に動かします。必要な場面はゲーム画面で確認します。");
      } else {
        next("I · DEFINE / 考える", "3. 自分の合図を決めます", "ジャンプ・しゃがむ・止まるを別々の形にします。ゲーム画面をクリックし、画面の案内に沿って教え始めます。");
      }
    },
  };
}
