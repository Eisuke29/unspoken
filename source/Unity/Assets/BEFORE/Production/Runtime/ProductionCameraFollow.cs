using UnityEngine;

namespace Before.Production
{
    /// <summary>
    /// Keeps NUL at the Production V2 logical screen anchor while following only
    /// horizontal progress. The course uses 320x180 logical pixels at PPU 1.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class ProductionCameraFollow : MonoBehaviour
    {
        public const float LogicalWidth = 320f;
        public const float LogicalHeight = 180f;
        public const float RunnerScreenX = 68f;
        public const float GroundScreenY = 150f;

        [SerializeField] private Transform target;
        [SerializeField] private float fixedWorldY = 60f;
        [SerializeField] private bool pixelSnap = true;

        public Transform Target
        {
            get => target;
            set => target = value;
        }

        public float FixedWorldY
        {
            get => fixedWorldY;
            set => fixedWorldY = value;
        }

        public bool PixelSnapEnabled
        {
            get => pixelSnap;
            set => pixelSnap = value;
        }

        public static float CameraXFor(float runnerWorldX)
            => runnerWorldX + LogicalWidth * 0.5f - RunnerScreenX;

        public static float PixelSnappedCameraXFor(float runnerWorldX)
            => Mathf.Round(CameraXFor(runnerWorldX));

        private void Awake()
        {
            if (target == null && transform.parent != null) target = transform.parent;
        }

        private void LateUpdate()
        {
            if (target == null) return;

            Vector3 p = transform.position;
            p.x = pixelSnap
                ? PixelSnappedCameraXFor(target.position.x)
                : CameraXFor(target.position.x);
            p.y = pixelSnap ? Mathf.Round(fixedWorldY) : fixedWorldY;
            transform.position = p;
        }
    }
}
