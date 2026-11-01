// Web版（WebGL）用：Unity から、ページの JavaScript で動く手の認識（window.beforeSidecar）を呼ぶ
mergeInto(LibraryManager.library, {
  // 制御（教える・リセットなど）を送り、今の状態の JSON を受け取る（json が空なら状態だけ受け取る）
  BeforeSidecarControl: function (jsonPtr) {
    var json = UTF8ToString(jsonPtr);
    var out = "";
    try {
      if (window.beforeSidecar) out = window.beforeSidecar.control(json) || "";
    } catch (e) {
      out = "";
    }
    var size = lengthBytesUTF8(out) + 1;
    var buf = _malloc(size);
    stringToUTF8(out, buf, size);
    return buf;
  },

  // カメラと認識を始める（Python の Sidecar を起動していたところ）
  BeforeSidecarStart: function () {
    try {
      if (window.beforeSidecar) window.beforeSidecar.start();
    } catch (e) {}
  },

  BeforeSidecarStop: function () {
    try {
      if (window.beforeSidecar) window.beforeSidecar.stop();
    } catch (e) {}
  },
});
