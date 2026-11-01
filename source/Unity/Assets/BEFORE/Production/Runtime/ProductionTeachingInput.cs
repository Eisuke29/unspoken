using UnityEngine;

namespace Before.Production
{
    /// <summary>
    /// Owns the one production teaching input. SPACE is observed only while a
    /// Teaching/Repair interaction is active; normal running ignores it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProductionTeachingInput : MonoBehaviour
    {
        [SerializeField] private ProductionTeachingFlow flow;
        [SerializeField] private SidecarTeachingControlClient control;
        private bool _contextActive;
        private int _beatId;
        private bool _repair;

        public ProductionTeachingFlow Flow { get => flow; set => flow = value; }
        public SidecarTeachingControlClient Control { get => control; set => control = value; }

        private void Awake() => Resolve();

        private void Update()
        {
            Resolve();
            SyncContext();
#if ENABLE_INPUT_SYSTEM
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null)
                TickInput(keyboard.spaceKey.wasPressedThisFrame,
                          keyboard.spaceKey.wasReleasedThisFrame,
                          keyboard.rKey.wasPressedThisFrame);
#endif
        }

        public void TickInput(bool pressedThisFrame, bool releasedThisFrame,
                              bool resetThisFrame = false)
        {
            if (flow == null || !flow.IsActive || flow.Controller == null)
                return;
            if (resetThisFrame)
            {
                flow.RequestCurrentSignReset();
                return;
            }
            if (control == null || flow.ResetPending || flow.Controller.Nul.Paused) return;
            if (pressedThisFrame) control.PressSpace();
            if (releasedThisFrame) control.ReleaseSpace();
        }

        public void SyncContext()
        {
            if (flow == null || control == null) return;
            if (!flow.IsActive)
            {
                if (_contextActive) control.EndTeaching();
                _contextActive = false;
                _beatId = 0;
                return;
            }

            if (!_contextActive || _beatId != flow.ActiveBeatId || _repair != flow.IsRepair)
            {
                _contextActive = true;
                _beatId = flow.ActiveBeatId;
                _repair = flow.IsRepair;
                control.BeginTeaching(flow.TargetKind, flow.IsRepair);
            }
        }

        public void ClearContext()
        {
            _contextActive = false;
            _beatId = 0;
            _repair = false;
        }

        private void Resolve()
        {
            if (flow == null) flow = GetComponent<ProductionTeachingFlow>();
            if (control == null) control = GetComponent<SidecarTeachingControlClient>();
        }
    }
}
