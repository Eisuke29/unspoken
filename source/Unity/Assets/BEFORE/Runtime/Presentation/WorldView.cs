using Before.App;
using Before.Config;
using Before.Course;
using System.Collections.Generic;
using UnityEngine;

namespace Before.Presentation
{
    /// <summary>
    /// Builds the side-view world from generated art: platform strips with gaps, scan gates,
    /// CORE NODE rings, and three parallax background layers. Rebuilds branch visuals on route selection.
    /// </summary>
    public sealed class WorldView
    {
        private readonly BeforeCourseConfig cfg;
        private readonly Transform root;
        private readonly Sprite tileTop;
        private readonly Sprite tileFill;
        private readonly Sprite gatePost;
        private readonly Sprite gateBeamClosed;
        private readonly Sprite coreArc;
        private readonly Sprite[] bg = new Sprite[3];
        private readonly List<GameObject> branchObjects = new List<GameObject>();
        private readonly List<(ScanGate gate, SpriteRenderer beam)> beams = new List<(ScanGate, SpriteRenderer)>();
        private readonly Transform[] parallax = new Transform[3];
        private static readonly float[] ParallaxFactor = { 0.12f, 0.28f, 0.55f };
        private GameObject coreRoot;

        public WorldView(BeforeCourseConfig courseCfg, Texture2D tiles, Texture2D hazards, Texture2D bgFar, Texture2D bgMid, Texture2D bgNear, Transform parent)
        {
            cfg = courseCfg;
            root = new GameObject("World").transform;
            root.SetParent(parent, false);

            tileTop = Sprite.Create(tiles, new Rect(0, 16, 16, 16), new Vector2(0f, 1f), 16f, 0, SpriteMeshType.FullRect);
            tileFill = Sprite.Create(tiles, new Rect(48, 16, 16, 16), new Vector2(0f, 1f), 16f, 0, SpriteMeshType.FullRect);
            gatePost = Sprite.Create(tiles, new Rect(64, 16, 16, 16), new Vector2(0.5f, 0f), 16f, 0, SpriteMeshType.FullRect);
            gateBeamClosed = Sprite.Create(hazards, new Rect(0, 0, 32, 32), new Vector2(0.5f, 0f), 16f, 0, SpriteMeshType.FullRect);
            coreArc = Sprite.Create(tiles, new Rect(0, 0, 16, 16), new Vector2(0.5f, 0.5f), 16f, 0, SpriteMeshType.FullRect);
            bg[0] = Sprite.Create(bgFar, new Rect(0, 0, 640, 180), new Vector2(0f, 0f), 16f, 0, SpriteMeshType.FullRect);
            bg[1] = Sprite.Create(bgMid, new Rect(0, 0, 640, 180), new Vector2(0f, 0f), 16f, 0, SpriteMeshType.FullRect);
            bg[2] = Sprite.Create(bgNear, new Rect(0, 0, 640, 180), new Vector2(0f, 0f), 16f, 0, SpriteMeshType.FullRect);

            BuildParallax();
        }

        public void BuildStatic(CourseModel course)
        {
            BuildPlatforms(course, -6f, cfg.xBranchOrigin, root, false);
            foreach (var gate in course.Gates)
                if (gate.BoundaryX < cfg.xBranchOrigin) BuildGate(gate, root, false);
            foreach (var gap in course.Gaps)
                if (gap.FarEdge < cfg.xBranchOrigin) BuildGapVisuals(gap, root, false);
            BuildCore();
        }

        // D-096 gap visibility (visual only; the collider/gap width is unchanged). Charcoal far layer behind
        // the void, 2 px collapse cross-sections at the edges, downward cracks, a 1 px underside highlight,
        // and a few debris specks. No semantic color.
        private static readonly Color GapFar = new Color(0.09f, 0.10f, 0.11f, 1f);
        private static readonly Color GapCut = new Color(0.16f, 0.17f, 0.18f, 1f);
        private static readonly Color GapUnder = new Color(0.55f, 0.57f, 0.58f, 1f);

        private void BuildGapVisuals(GapHazard gap, Transform parent, bool branch)
        {
            float w = gap.FarEdge - gap.NearEdge;
            // charcoal far layer behind the void
            AddRect(parent, gap.NearEdge, -0.5f, w, 3.2f, GapFar, 2, branch);
            // 2 logical-px (0.125 u) collapse cross-sections at both edges + downward cracks
            AddRect(parent, gap.NearEdge - 0.125f, 0f, 0.125f, 1.6f, GapCut, 4, branch);
            AddRect(parent, gap.FarEdge, 0f, 0.125f, 1.6f, GapCut, 4, branch);
            AddRect(parent, gap.NearEdge - 0.0625f, 0f, 0.0625f, 0.9f, GapFar, 4, branch); // crack under near lip
            AddRect(parent, gap.FarEdge + 0.0625f, 0f, 0.0625f, 0.7f, GapFar, 4, branch);  // crack under far lip
            // 1 px underside highlight on the two lips
            AddRect(parent, gap.NearEdge - 0.25f, 0.0625f, 0.25f, 0.0625f, GapUnder, 6, branch);
            AddRect(parent, gap.FarEdge, 0.0625f, 0.25f, 0.0625f, GapUnder, 6, branch);
            // debris specks in the void
            AddRect(parent, gap.NearEdge + w * 0.35f, -1.2f, 0.0625f, 0.0625f, GapUnder, 3, branch);
            AddRect(parent, gap.NearEdge + w * 0.6f, -1.8f, 0.0625f, 0.0625f, GapCut, 3, branch);
            AddRect(parent, gap.NearEdge + w * 0.5f, -2.4f, 0.0625f, 0.0625f, GapUnder, 3, branch);
        }

        private void AddRect(Transform parent, float x, float yTop, float w, float h, Color color, int order, bool branch)
        {
            var go = new GameObject("gapv");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(x, yTop, 0f);
            go.transform.localScale = new Vector3(w, h, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = tileFill; // 16px @16ppu = 1 unit; scaled to size (pivot top-left)
            sr.color = color;
            sr.sortingOrder = order;
            if (branch) branchObjects.Add(go);
        }

        /// <summary>Enable the selected branch visuals (called on final plan commit or reset).</summary>
        public void RebuildBranch(CourseModel course)
        {
            foreach (var go in branchObjects) Object.Destroy(go);
            branchObjects.Clear();
            for (int i = beams.Count - 1; i >= 0; i--)
            {
                if (beams[i].beam == null) beams.RemoveAt(i);
            }
            if (course.SelectedRoute == FinalRoute.None)
            {
                // no route yet: cover the branch region with plain ground so pre-fork travel reads normally
                var cover = new GameObject("BranchCover");
                cover.transform.SetParent(root, false);
                branchObjects.Add(cover);
                BuildPlatforms(course, cfg.xBranchOrigin, cfg.xCoreChamber + 6f, cover.transform, true);
                return;
            }
            var branch = new GameObject("Branch");
            branch.transform.SetParent(root, false);
            branchObjects.Add(branch);
            BuildPlatforms(course, cfg.xBranchOrigin, cfg.xCoreChamber + 6f, branch.transform, true);
            foreach (var gate in course.Gates)
            {
                if (gate.BoundaryX > cfg.xBranchOrigin)
                {
                    BuildGate(gate, branch.transform, true);
                }
            }
            foreach (var gap in course.Gaps)
            {
                if (gap.NearEdge > cfg.xBranchOrigin) BuildGapVisuals(gap, branch.transform, true);
            }
        }

        private void BuildPlatforms(CourseModel course, float fromX, float toX, Transform parent, bool branchOnly)
        {
            for (float x = fromX; x < toX; x += 1f)
            {
                if (!course.HasGroundAt(x + 0.5f)) continue;
                var top = new GameObject("t");
                top.transform.SetParent(parent, false);
                top.transform.position = new Vector3(x, 0f, 0f);
                var sr = top.AddComponent<SpriteRenderer>();
                sr.sprite = tileTop;
                sr.sortingOrder = 5;
                var fill = new GameObject("f");
                fill.transform.SetParent(parent, false);
                fill.transform.position = new Vector3(x, -1f, 0f);
                var fr = fill.AddComponent<SpriteRenderer>();
                fr.sprite = tileFill;
                fr.sortingOrder = 5;
            }
        }

        private void BuildGate(ScanGate gate, Transform parent, bool branch)
        {
            var postL = new GameObject("gatePostL");
            postL.transform.SetParent(parent, false);
            postL.transform.position = new Vector3(gate.BoundaryX - cfg.gatePassageWidth * 0.5f, 0f, 0f);
            var pl = postL.AddComponent<SpriteRenderer>();
            pl.sprite = gatePost; pl.sortingOrder = 6;
            postL.transform.localScale = new Vector3(1f, 2.2f, 1f);

            var postR = new GameObject("gatePostR");
            postR.transform.SetParent(parent, false);
            postR.transform.position = new Vector3(gate.BoundaryX + cfg.gatePassageWidth * 0.5f, 0f, 0f);
            var pr = postR.AddComponent<SpriteRenderer>();
            pr.sprite = gatePost; pr.sortingOrder = 6;
            postR.transform.localScale = new Vector3(1f, 2.2f, 1f);

            var beam = new GameObject("gateBeam");
            beam.transform.SetParent(parent, false);
            beam.transform.position = new Vector3(gate.BoundaryX, 0f, 0f);
            var br = beam.AddComponent<SpriteRenderer>();
            br.sprite = gateBeamClosed; br.sortingOrder = 7;
            beams.Add((gate, br));
            if (branch)
            {
                branchObjects.Add(postL);
                branchObjects.Add(postR);
                branchObjects.Add(beam);
            }
        }

        private void BuildCore()
        {
            coreRoot = new GameObject("CoreNode");
            coreRoot.transform.SetParent(root, false);
            coreRoot.transform.position = new Vector3(cfg.xCoreChamber, 2.6f, 0f);
            float[] scales = { 3.5f, 2.5f, 1.5f };
            for (int ring = 0; ring < 3; ring++)
            {
                for (int seg = 0; seg < 3; seg++)
                {
                    var arc = new GameObject($"arc{ring}_{seg}");
                    arc.transform.SetParent(coreRoot.transform, false);
                    var sr = arc.AddComponent<SpriteRenderer>();
                    sr.sprite = coreArc;
                    sr.sortingOrder = 8;
                    sr.color = new Color(0.68f, 0.71f, 0.73f, 1f);
                    float ang = seg * 120f + ring * 30f;
                    arc.transform.localScale = Vector3.one * scales[ring] / 2f;
                    arc.transform.localPosition = Vector3.zero;
                    arc.transform.localRotation = Quaternion.Euler(0, 0, ang);
                }
            }
        }

        private void BuildParallax()
        {
            for (int layer = 0; layer < 3; layer++)
            {
                var holder = new GameObject($"bg{layer}").transform;
                holder.SetParent(root, false);
                parallax[layer] = holder;
                for (int copy = 0; copy < 4; copy++)
                {
                    var go = new GameObject("strip");
                    go.transform.SetParent(holder, false);
                    go.transform.localPosition = new Vector3(copy * 40f, -1.0f, 0f);
                    var sr = go.AddComponent<SpriteRenderer>();
                    sr.sprite = bg[layer];
                    sr.sortingOrder = layer;
                }
            }
        }

        public void Tick(double gameplayNow, float cameraX)
        {
            foreach (var (gate, beam) in beams)
            {
                if (beam == null) continue;
                var phase = gate.PhaseAt(gameplayNow);
                beam.enabled = phase != GatePhase.Open;
                float alpha = phase == GatePhase.Opening || phase == GatePhase.Closing ? 0.5f : 1f;
                beam.color = new Color(1f, 1f, 1f, alpha);
            }
            for (int layer = 0; layer < 3; layer++)
            {
                float offset = -cameraX * (1f - ParallaxFactor[layer]);
                float wrapped = Mathf.Repeat(offset, 40f) - 40f;
                parallax[layer].position = new Vector3(cameraX + wrapped - 20f, parallax[layer].position.y, 0f);
            }
        }

        public void SetCoreActivation(float alignProgress, float pulse)
        {
            if (coreRoot == null) return;
            int ring = 0;
            foreach (Transform arc in coreRoot.transform)
            {
                int ringIndex = ring / 3;
                float target = ringIndex / 3f;
                var sr = arc.GetComponent<SpriteRenderer>();
                bool aligned = alignProgress >= (ringIndex + 1) / 3f;
                Color baseColor = aligned ? new Color(0.9f, 0.91f, 0.91f) : new Color(0.68f, 0.71f, 0.73f);
                if (pulse > 0f) baseColor = Color.Lerp(baseColor, new Color(0f, 0.9f, 1f), pulse * 0.6f);
                sr.color = baseColor;
                ring++;
            }
        }
    }
}
