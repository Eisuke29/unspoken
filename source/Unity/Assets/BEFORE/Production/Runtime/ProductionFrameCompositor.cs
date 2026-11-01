using UnityEngine;

namespace Before.Production
{
    /// <summary>
    /// Owns only the final screen blit of the 320x180 world surface. It is saved
    /// after all public HUD Presenters on the composition root because legacy
    /// OnGUI components are submitted in reverse component order. This makes the
    /// compositor submit first and independent HUD/story layers draw afterward.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProductionFrameCompositor : MonoBehaviour
    {
        [SerializeField] private ProductionLogicalRenderPresenter logicalSurface;
        [SerializeField] private ProductionShellPresenter shellOverlay;
        [SerializeField] private ProductionEnergyLinkPresenter energyLink;

        public ProductionLogicalRenderPresenter LogicalSurface
        {
            get => logicalSurface;
            set => logicalSurface = value;
        }

        public ProductionShellPresenter ShellOverlay
        {
            get => shellOverlay;
            set
            {
                if (shellOverlay != null) shellOverlay.ComposedExternally = false;
                shellOverlay = value;
                if (shellOverlay != null) shellOverlay.ComposedExternally = true;
            }
        }

        public ProductionEnergyLinkPresenter EnergyLink
        {
            get => energyLink;
            set
            {
                if (energyLink != null) energyLink.ComposedExternally = false;
                energyLink = value;
                if (energyLink != null) energyLink.ComposedExternally = true;
            }
        }

        public int GuiRepaintCount { get; private set; }

        private void Awake()
        {
            if (logicalSurface == null)
                logicalSurface = GetComponentInChildren<ProductionLogicalRenderPresenter>();
            if (shellOverlay == null) shellOverlay = GetComponent<ProductionShellPresenter>();
            if (shellOverlay != null) shellOverlay.ComposedExternally = true;
            if (energyLink == null) energyLink = GetComponent<ProductionEnergyLinkPresenter>();
            if (energyLink != null) energyLink.ComposedExternally = true;
        }

        private void OnGUI()
        {
            if (Event.current.type == EventType.Repaint) GuiRepaintCount++;
            if (logicalSurface == null || !logicalSurface.RenderToLogicalSurface ||
                logicalSurface.LogicalSurface == null ||
                Event.current.type != EventType.Repaint) return;

            Color previousColor = GUI.color;
            GUI.color = Color.white;
            GUI.DrawTexture(logicalSurface.ContentRectFor(Screen.width, Screen.height),
                logicalSurface.LogicalSurface, ScaleMode.StretchToFill, false);
            GUI.color = previousColor;
            if (shellOverlay != null) shellOverlay.DrawOverlay();
            if (energyLink != null) energyLink.DrawOverlay();
        }
    }
}
