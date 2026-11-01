// BEFORE Goal 2 — launches the project-local .venv MediaPipe sidecar in the
// Editor on Play (D-125: Editor-only, no standalone) and stops it on Play exit.
// Best-effort: any failure is logged (never an error/warning) so Console stays
// clean; the recognizers are frozen (D-129) — this only starts the transport.
// Web版（WebGL）では Python の代わりに、ページの JavaScript でカメラと手の認識を動かす。
using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace Before.Production
{
    public sealed class SidecarProcessManager : MonoBehaviour
    {
        public int udpPort = 8765;
        public int controlPort = SidecarTeachingControlClient.DefaultControlPort;
        private Process _proc;

#if UNITY_WEBGL && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern void BeforeSidecarStart();
        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern void BeforeSidecarStop();
        private bool _webRunning;

        public bool IsRunning => _webRunning;
        public int ProcessId => 0;
#else
        public bool IsRunning => _proc != null && !_proc.HasExited;
        public int ProcessId => IsRunning ? _proc.Id : 0;
#endif
        public bool LaunchAttempted { get; private set; }
        public string LastStatus { get; private set; } = "idle";

        public static string ScriptPathFor(string projectRoot)
            => Path.Combine(projectRoot, "Tools", "BeforeGestureSidecar", "run_bench.ps1");

        public static ProcessStartInfo CreateStartInfo(string projectRoot, int port)
            => CreateStartInfo(projectRoot, port, SidecarTeachingControlClient.DefaultControlPort);

        public static ProcessStartInfo CreateStartInfo(string projectRoot, int port, int teachingPort)
        {
            string ps1 = ScriptPathFor(projectRoot);
            return new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + ps1 +
                            "\" --udp " + port + " --control " + teachingPort,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = projectRoot,
            };
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        public void Launch()
        {
            if (_webRunning) return;
            LaunchAttempted = true;
            BeforeSidecarStart();
            _webRunning = true;
            LastStatus = "running";
        }

        public void Stop()
        {
            if (_webRunning) BeforeSidecarStop();
            _webRunning = false;
            LastStatus = "stopped";
        }
#else
        // projectRoot/Tools/BeforeGestureSidecar/run_bench.ps1 --udp <port>
        public void Launch()
        {
            if (IsRunning) return;
            LaunchAttempted = true;
            try
            {
                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                string ps1 = ScriptPathFor(projectRoot);
                if (!File.Exists(ps1))
                {
                    LastStatus = "script-missing";
                    UnityEngine.Debug.Log("[CommandLab] sidecar script not found: " + ps1);
                    return;
                }
                _proc = Process.Start(CreateStartInfo(projectRoot, udpPort, controlPort));
                LastStatus = _proc != null ? "running" : "launch-returned-null";
                UnityEngine.Debug.Log("[CommandLab] sidecar launched (udp " + udpPort + ")");
            }
            catch (Exception e)
            {
                LastStatus = "launch-failed";
                UnityEngine.Debug.Log("[CommandLab] sidecar launch skipped: " + e.Message);
                _proc = null;
            }
        }

        public void Stop()
        {
            try
            {
                if (_proc != null && !_proc.HasExited)
                {
                    if (Application.platform == RuntimePlatform.WindowsEditor)
                    {
                        var stop = Process.Start(new ProcessStartInfo
                        {
                            FileName = "taskkill.exe",
                            Arguments = "/PID " + _proc.Id + " /T /F",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        });
                        stop?.WaitForExit(1000);
                        stop?.Dispose();
                    }
                    if (!_proc.HasExited) _proc.Kill();
                }
            }
            catch { }
            finally
            {
                _proc?.Dispose();
                _proc = null;
                LastStatus = "stopped";
            }
        }
#endif

        private void OnDisable() => Stop();
        private void OnDestroy() => Stop();
        private void OnApplicationQuit() => Stop();
    }
}
