using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Before.Production
{
    [Serializable]
    public sealed class SidecarTeachingStatus
    {
        public bool active;
        public string kind;
        public bool repair;
        public int required;
        public int accepted;
        public string phase;
        public string reason;
        public int event_id;
        public bool finalized;
        public bool direction_mode;
        public string ack_action;
        public string ack_target;
        public int ack_id;
        public int rgb_frames_sent;
    }

    public readonly struct SidecarTeachingEvent
    {
        public readonly string Kind;
        public readonly int Accepted;
        public readonly int Required;
        public readonly string Reason;
        public readonly bool Finalized;
        public bool WasAccepted => string.IsNullOrEmpty(Reason);

        public SidecarTeachingEvent(SidecarTeachingStatus status)
        {
            Kind = status.kind ?? string.Empty;
            Accepted = status.accepted;
            Required = status.required;
            Reason = status.reason ?? string.Empty;
            Finalized = status.finalized;
        }
    }

    /// <summary>
    /// Editor-only Sidecar control-plane client. It is hard-bound to IPv4
    /// loopback and sends semantic actions, never synthetic key events or RGB.
    /// The frozen gameplay UDP receiver/session/dedup path is not used here.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SidecarTeachingControlClient : MonoBehaviour
    {
        public const int DefaultControlPort = 8766;
        public const string ControlPath = "/v1/teaching";
        private const float PollSeconds = 0.1f;
        private const double ReadyTimeoutSeconds = 1.5;

        [SerializeField] private int controlPort = DefaultControlPort;
        [SerializeField] private bool transportEnabled = true;

        private SidecarTeachingStatus _status = new SidecarTeachingStatus();
        private bool _requestInFlight;
        private int _consumedEventId;
        private int _consumedAckId;
        private int _nextResetRequestId;
        private double _lastResponseTime = -999.0;
        private string _contextSignature = string.Empty;

        public int ControlPort { get => controlPort; set => controlPort = Mathf.Clamp(value, 1, 65535); }
        public bool TransportEnabled { get => transportEnabled; set => transportEnabled = value; }
        public bool IsReady => transportEnabled &&
            Time.realtimeSinceStartupAsDouble - _lastResponseTime <= ReadyTimeoutSeconds;
        public string Phase => _status.phase ?? "idle";
        public string RejectionReason => _status.reason ?? string.Empty;
        public int Accepted => _status.accepted;
        public int Required => _status.required;
        public bool DirectionMode => _status.direction_mode;
        public int RgbFramesSent => _status.rgb_frames_sent;
        public string LastQueuedAction { get; private set; } = string.Empty;
        public bool LastDirectionEnabled { get; private set; }
        public int RequestCount { get; private set; }
        public string LastResetTarget { get; private set; } = string.Empty;
        public int LastResetRequestId { get; private set; }

        private string Url => "http://127.0.0.1:" + controlPort + ControlPath;

        private void OnEnable()
        {
            if (Application.isPlaying) StartCoroutine(PollLoop());
        }

        public void BeginTeaching(ProductionObstacleKind kind, bool repair)
        {
            string sidecarKind = KindName(kind);
            string signature = sidecarKind + ":" + repair;
            if (_contextSignature == signature) return;
            _contextSignature = signature;
            Post(new ControlRequest { action = "begin", kind = sidecarKind, repair = repair });
        }

        public void EndTeaching()
        {
            if (string.IsNullOrEmpty(_contextSignature)) return;
            _contextSignature = string.Empty;
            Post(new ControlRequest { action = "cancel" });
        }

        public void PressSpace() => Post(new ControlRequest { action = "press" });
        public void ReleaseSpace() => Post(new ControlRequest { action = "release" });

        public void ResetLearning()
        {
            _contextSignature = string.Empty;
            Post(new ControlRequest { action = "reset" });
        }

        public int ResetCommand(ProductionObstacleKind kind)
        {
            string target = KindName(kind);
            int requestId = ++_nextResetRequestId;
            _contextSignature = target + ":False";
            LastResetTarget = target;
            LastResetRequestId = requestId;
            Post(new ControlRequest
            {
                action = "reset_command",
                target = target,
                request_id = requestId
            });
            return requestId;
        }

        public void SetDirectionMode(bool enabled)
        {
            LastDirectionEnabled = enabled;
            Post(new ControlRequest { action = "direction", enabled = enabled });
        }

        public bool TryConsumeEvent(out SidecarTeachingEvent teachingEvent)
        {
            if (_status.event_id <= _consumedEventId)
            {
                teachingEvent = default;
                return false;
            }
            _consumedEventId = _status.event_id;
            teachingEvent = new SidecarTeachingEvent(_status);
            return true;
        }

        public bool TryConsumeResetCommandAck(out string target, out int requestId)
        {
            if (_status.ack_action != "reset_command" || _status.ack_id <= 0 ||
                _status.ack_id <= _consumedAckId)
            {
                target = string.Empty;
                requestId = 0;
                return false;
            }
            _consumedAckId = _status.ack_id;
            _consumedEventId = Mathf.Max(_consumedEventId, _status.event_id);
            target = _status.ack_target ?? string.Empty;
            requestId = _status.ack_id;
            return true;
        }

        public bool ApplyStatusJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return false;
            SidecarTeachingStatus parsed;
            try { parsed = JsonUtility.FromJson<SidecarTeachingStatus>(json); }
            catch { return false; }
            if (parsed == null || parsed.rgb_frames_sent != 0) return false;
            _status = parsed;
            _lastResponseTime = Time.realtimeSinceStartupAsDouble;
            return true;
        }

        private IEnumerator PollLoop()
        {
            var wait = new WaitForSecondsRealtime(PollSeconds);
            while (enabled)
            {
                if (transportEnabled && !_requestInFlight)
                    yield return Request(null);
                yield return wait;
            }
        }

        private void Post(ControlRequest request)
        {
            LastQueuedAction = request.action;
            RequestCount++;
            if (!isActiveAndEnabled || !gameObject.activeInHierarchy ||
                !transportEnabled) return;
            StartCoroutine(Request(JsonUtility.ToJson(request)));
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern string BeforeSidecarControl(string json);

        // Web版：HTTP の代わりに、ページの JavaScript へ直接渡して状態を受け取る
        private IEnumerator Request(string json)
        {
            ApplyStatusJson(BeforeSidecarControl(json ?? string.Empty));
            yield break;
        }
#else
        private IEnumerator Request(string json)
        {
            if (json == null && _requestInFlight) yield break;
            while (json != null && _requestInFlight) yield return null;
            _requestInFlight = true;
            using (var request = new UnityWebRequest(Url,
                       json == null ? UnityWebRequest.kHttpVerbGET : UnityWebRequest.kHttpVerbPOST))
            {
                request.downloadHandler = new DownloadHandlerBuffer();
                if (json != null)
                {
                    request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                    request.SetRequestHeader("Content-Type", "application/json");
                }
                request.timeout = 1;
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success)
                    ApplyStatusJson(request.downloadHandler.text);
            }
            _requestInFlight = false;
        }
#endif

        private static string KindName(ProductionObstacleKind kind)
        {
            switch (kind)
            {
                case ProductionObstacleKind.Jump: return "jump";
                case ProductionObstacleKind.Crouch: return "crouch";
                default: return "stop";
            }
        }

        [Serializable]
        private sealed class ControlRequest
        {
            public string action;
            public string kind;
            public bool repair;
            public bool enabled;
            public string target;
            public int request_id;
        }
    }
}
