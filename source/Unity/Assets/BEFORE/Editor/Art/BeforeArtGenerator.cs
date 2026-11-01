using System.Collections.Generic;
using System.IO;
using Before.Config;
using UnityEditor;
using UnityEngine;

namespace Before.Editor.Art
{
    /// <summary>
    /// Deterministic project-owned pixel-art generator (art-production.md).
    /// Seed 0xB3F04E. Regeneration deletes only files under the generated output root.
    /// </summary>
    public static class BeforeArtGenerator
    {
        public const int Seed = 0xB3F04E;
        public const string OutputRoot = "Assets/BEFORE/Generated/Art";

        private static readonly Color32 Void = C(0x050608);
        private static readonly Color32 Charcoal = C(0x15181C);
        private static readonly Color32 MidGray = C(0x555B63);
        private static readonly Color32 Ash = C(0xAEB4BA);
        private static readonly Color32 White = C(0xE5E7E8);
        private static readonly Color32 Cyan = C(0x00E5FF);
        private static readonly Color32 Amber = C(0xFFB000);
        private static readonly Color32 Violet = C(0xB26CFF);
        private static readonly Color32 Red = C(0xFF3B4E);
        private static readonly Color32 Clear = new Color32(0, 0, 0, 0);

        private static Color32 C(int rgb) => new Color32((byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF), 255);

        [MenuItem("BEFORE/アート/すべて再生成")]
        public static void RegenerateAll()
        {
            GenerateInto(OutputRoot, true);
            AssetDatabase.Refresh();
            ApplyImportSettings(OutputRoot);
            Debug.Log("[BEFORE] Art regenerated deterministically (seed 0xB3F04E).");
        }

        /// <summary>Generates all PNGs into the given folder. Used by tests with a temporary folder.</summary>
        public static Dictionary<string, string> GenerateInto(string root, bool clearFirst)
        {
            if (clearFirst && Directory.Exists(root))
            {
                foreach (var file in Directory.GetFiles(root, "*.png")) File.Delete(file);
            }
            Directory.CreateDirectory(root);

            var hashes = new Dictionary<string, string>();
            hashes["NUL_Side_48.png"] = Save(root, "NUL_Side_48.png", DrawNulSide());
            hashes["NUL_Top_48.png"] = Save(root, "NUL_Top_48.png", DrawNulTop());
            hashes["WorldTiles_16.png"] = Save(root, "WorldTiles_16.png", DrawWorldTiles());
            hashes["Hazards_32.png"] = Save(root, "Hazards_32.png", DrawHazards());
            hashes["BG_Far_640x180.png"] = Save(root, "BG_Far_640x180.png", DrawBackground(0));
            hashes["BG_Mid_640x180.png"] = Save(root, "BG_Mid_640x180.png", DrawBackground(1));
            hashes["BG_Near_640x180.png"] = Save(root, "BG_Near_640x180.png", DrawBackground(2));
            return hashes;
        }

        private static string Save(string root, string name, (int w, int h, Color32[] pixels) img)
        {
            var tex = new Texture2D(img.w, img.h, TextureFormat.RGBA32, false);
            tex.SetPixels32(img.pixels);
            tex.Apply(false, false);
            byte[] png = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);
            string path = Path.Combine(root, name);
            File.WriteAllBytes(path, png);
            using var sha = System.Security.Cryptography.SHA256.Create();
            return System.Convert.ToBase64String(sha.ComputeHash(png));
        }

        // ------------------------------------------------------------------ pixel helpers

        private sealed class Buffer
        {
            public readonly int W, H;
            public readonly Color32[] P;
            public Buffer(int w, int h) { W = w; H = h; P = new Color32[w * h]; for (int i = 0; i < P.Length; i++) P[i] = Clear; }
            public void Set(int x, int y, Color32 c) { if (x >= 0 && x < W && y >= 0 && y < H) P[y * W + x] = c; }
            public Color32 Get(int x, int y) => (x >= 0 && x < W && y >= 0 && y < H) ? P[y * W + x] : Clear;
            public void Rect(int x, int y, int w, int h, Color32 c) { for (int j = 0; j < h; j++) for (int i = 0; i < w; i++) Set(x + i, y + j, c); }
            public void Line(int x0, int y0, int x1, int y1, Color32 c)
            {
                int dx = Mathf.Abs(x1 - x0), dy = -Mathf.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx + dy;
                while (true)
                {
                    Set(x0, y0, c);
                    if (x0 == x1 && y0 == y1) break;
                    int e2 = 2 * err;
                    if (e2 >= dy) { err += dy; x0 += sx; }
                    if (e2 <= dx) { err += dx; y0 += sy; }
                }
            }
        }

        private static readonly int[,] Bayer4 =
        {
            { 0, 8, 2, 10 }, { 12, 4, 14, 6 }, { 3, 11, 1, 9 }, { 15, 7, 13, 5 }
        };

        private static bool Dither(int x, int y, float level) => level * 16f > Bayer4[y & 3, x & 3];

        // ------------------------------------------------------------------ NUL

        private static (int, int, Color32[]) DrawNulSide()
        {
            var buf = new Buffer(48 * 8, 48 * 11);
            for (int row = 0; row < 11; row++)
            {
                int frames = row switch { 0 => 4, 1 => 8, 2 => 4, 3 => 6, 4 => 3, 5 => 6, 6 => 4, 7 => 6, 8 => 8, 9 => 8, 10 => 8, _ => 0 };
                for (int f = 0; f < frames; f++)
                {
                    DrawNulFrame(buf, f * 48, (10 - row) * 48, row, f, frames);
                }
            }
            return (buf.W, buf.H, buf.P);
        }

        private static (int, int, Color32[]) DrawNulTop()
        {
            var buf = new Buffer(48 * 4, 48);
            for (int f = 0; f < 4; f++)
            {
                int ox = f * 48;
                // top-down: rounded shell seen from above with core toward look direction
                buf.Rect(ox + 17, 15, 14, 18, Charcoal);
                Outline(buf, ox + 17, 15, 14, 18, Ash);
                int lookDx = f == 1 ? -3 : f == 2 ? 3 : 0;
                buf.Rect(ox + 23 + lookDx, 23, 2, 2, Cyan);
            }
            return (buf.W, buf.H, buf.P);
        }

        private static void Outline(Buffer b, int x, int y, int w, int h, Color32 c)
        {
            for (int i = 0; i < w; i++) { b.Set(x + i, y, c); b.Set(x + i, y + h - 1, c); }
            for (int j = 0; j < h; j++) { b.Set(x, y + j, c); b.Set(x + w - 1, y + j, c); }
        }

        /// <summary>One 48x48 frame. Invariants: shell 18x14 at (24,25) ±1, core near (28,25), hip (23,18), feet baseline y=4.</summary>
        private static void DrawNulFrame(Buffer buf, int ox, int oy, int row, int f, int frames)
        {
            bool collapsed = row == 8 && f >= 5;
            bool recharge = row == 9;
            bool coreLit = !(row == 8 && f >= 3) && !(recharge && f < 5);

            int shellDy = 0;
            int coreDx = 0;
            float t = frames > 1 ? f / (float)(frames - 1) : 0f;

            switch (row)
            {
                case 0: shellDy = f == 1 || f == 2 ? 1 : 0; break;                       // idle breathing
                case 1: shellDy = (f == 1 || f == 5) ? -1 : (f == 3 || f == 7) ? 1 : 0; break; // run bob <=2px
                case 2: shellDy = f; coreDx = -1; break;                                  // brake lean
                case 3: shellDy = f == 0 ? 2 : f == 1 ? 1 : f >= 4 ? 1 : -1; break;       // jump charge/stretch
                case 4: shellDy = f == 0 ? 2 : f == 1 ? 1 : 0; break;                     // land
                case 5: coreDx = f < 2 ? -2 : f < 4 ? 2 : 0; break;                       // look/hesitate
                case 6: shellDy = f == 1 || f == 2 ? 1 : 0; break;                        // nod
                case 7: coreDx = -2; shellDy = f % 2; break;                              // refuse (turn away)
                case 8: shellDy = Mathf.Min(6, f); break;                                 // collapse sink
                case 9: shellDy = Mathf.Max(0, 6 - f); break;                             // recharge rise
                case 10: shellDy = f == 3 || f == 4 ? 4 : 0; coreDx = f < 2 ? -2 : 0; break; // farewell bow
            }

            int shellX = ox + 15;
            int shellY = oy + 18 - shellDy;
            if (collapsed) { shellY = oy + 8; }

            // legs: hip (23,18-shellDy). two 6px segments, feet 5x2 at baseline y=4 when grounded
            int hipX = ox + 23, hipY = oy + 18 - shellDy;
            if (!collapsed)
            {
                int stride = row == 1 ? Mathf.RoundToInt(Mathf.Sin(t * Mathf.PI * 2f) * 4f) : 0;
                bool airborne = row == 3 && f >= 2 && f <= 3;
                DrawLeg(buf, hipX, hipY, -2 + stride, airborne ? 3 : 0, oy);
                DrawLeg(buf, hipX, hipY, 3 - stride, airborne ? 4 : 0, oy);
            }

            // shell 18x14 with ash edge
            buf.Rect(shellX, shellY, 18, 14, Charcoal);
            Outline(buf, shellX, shellY, 18, 14, Ash);
            // <=4 dirty white highlights
            buf.Set(shellX + 3, shellY + 12, White);
            buf.Set(shellX + 4, shellY + 12, White);
            buf.Set(shellX + 2, shellY + 10, White);
            // chipped shell pixel
            buf.Set(shellX + 17, shellY + 3, Clear);

            // core (2x2) near (28,25)
            if (coreLit)
            {
                buf.Rect(ox + 27 + coreDx, shellY + 6, 2, 2, Cyan);
            }

            // up to 3 cable strands anchored at fixed shell pixels
            int sway = row == 1 ? (f % 4 < 2 ? 0 : 1) : 0;
            buf.Line(shellX + 5, shellY, shellX + 4 + sway, shellY - 4, MidGray);
            buf.Line(shellX + 9, shellY, shellX + 9 - sway, shellY - 3, MidGray);
        }

        private static void DrawLeg(Buffer buf, int hipX, int hipY, int footDx, int footLift, int oy)
        {
            int footY = oy + 4 + footLift;
            int footX = hipX + footDx;
            int kneeX = (hipX + footX) / 2 + 1;
            int kneeY = (hipY + footY) / 2;
            buf.Line(hipX, hipY, kneeX, kneeY, MidGray);
            buf.Line(kneeX, kneeY, footX, footY + 2, MidGray);
            buf.Rect(footX - 2, footY, 5, 2, Charcoal);
            buf.Set(footX - 2, footY + 1, Ash);
        }

        // ------------------------------------------------------------------ tiles / hazards

        private static (int, int, Color32[]) DrawWorldTiles()
        {
            var buf = new Buffer(16 * 8, 16 * 2);
            // row 1 (top): 0 solid top, 1 edge L, 2 edge R, 3 fill, 4 gate post, 5 fork mark, 6 cable anchor, 7 blank
            for (int t = 0; t < 7; t++)
            {
                int ox = t * 16, oy = 16;
                if (t <= 3)
                {
                    buf.Rect(ox, oy, 16, 16, Charcoal);
                    if (t != 3) buf.Rect(ox, oy + 14, 16, 2, MidGray);
                    if (t == 0) for (int i = 0; i < 16; i += 3) buf.Set(ox + i, oy + 15, Ash);
                    if (t == 1) { for (int j = 0; j < 6; j++) buf.Set(ox, oy + 10 + (j % 6), Ash); buf.Rect(ox, oy, 1, 16, MidGray); }
                    if (t == 2) buf.Rect(ox + 15, oy, 1, 16, MidGray);
                    if (t == 3) for (int j = 0; j < 16; j++) for (int i = 0; i < 16; i++) if (Dither(i, j, 0.15f)) buf.Set(ox + i, oy + j, Void);
                }
                else if (t == 4)
                {
                    buf.Rect(ox + 6, oy, 4, 16, Charcoal);
                    Outline(buf, ox + 6, oy, 4, 16, MidGray);
                    buf.Set(ox + 7, oy + 14, Ash);
                }
                else if (t == 5)
                {
                    buf.Line(ox + 8, oy + 2, ox + 2, oy + 12, Ash);
                    buf.Line(ox + 8, oy + 2, ox + 14, oy + 12, Ash);
                }
                else
                {
                    buf.Rect(ox + 7, oy + 12, 2, 4, MidGray);
                    buf.Set(ox + 8, oy + 11, Ash);
                }
            }
            // row 0 (bottom): CORE NODE arc segments (4) + center + spares
            for (int t = 0; t < 4; t++)
            {
                int ox = t * 16;
                for (int a = 0; a < 90; a += 6)
                {
                    float rad = (a + t * 90) * Mathf.Deg2Rad;
                    int x = ox + 8 + Mathf.RoundToInt(Mathf.Cos(rad) * 6f);
                    int y = 8 + Mathf.RoundToInt(Mathf.Sin(rad) * 6f);
                    buf.Set(x, y, Ash);
                }
            }
            buf.Rect(4 * 16 + 5, 5, 6, 6, Charcoal);
            Outline(buf, 4 * 16 + 5, 5, 6, 6, MidGray);
            return (buf.W, buf.H, buf.P);
        }

        private static (int, int, Color32[]) DrawHazards()
        {
            var buf = new Buffer(32 * 6, 32);
            // 0: gate closed (red beam), 1: gate open, 2: gap icon, 3: gate icon (map), 4: route node, 5: checkpoint mark
            for (int c = 0; c < 6; c++)
            {
                int ox = c * 32;
                switch (c)
                {
                    case 0:
                        buf.Rect(ox + 4, 0, 4, 32, Charcoal);
                        buf.Rect(ox + 24, 0, 4, 32, Charcoal);
                        for (int j = 2; j < 30; j += 2) buf.Rect(ox + 9, j, 14, 1, Red);
                        break;
                    case 1:
                        buf.Rect(ox + 4, 0, 4, 32, Charcoal);
                        buf.Rect(ox + 24, 0, 4, 32, Charcoal);
                        buf.Set(ox + 6, 30, Ash);
                        buf.Set(ox + 26, 30, Ash);
                        break;
                    case 2:
                        buf.Rect(ox + 2, 6, 10, 4, Charcoal);
                        buf.Rect(ox + 20, 6, 10, 4, Charcoal);
                        for (int i = 12; i < 20; i++) if (Dither(i, 4, 0.5f)) buf.Set(ox + i, 4, MidGray);
                        break;
                    case 3:
                        buf.Rect(ox + 8, 4, 3, 24, Charcoal);
                        buf.Rect(ox + 21, 4, 3, 24, Charcoal);
                        for (int j = 8; j < 24; j += 3) buf.Rect(ox + 12, j, 8, 1, MidGray);
                        break;
                    case 4:
                        Outline(buf, ox + 12, 12, 8, 8, Ash);
                        break;
                    case 5:
                        buf.Line(ox + 8, 16, ox + 14, 8, Ash);
                        buf.Line(ox + 14, 8, ox + 24, 24, Ash);
                        break;
                }
            }
            return (buf.W, buf.H, buf.P);
        }

        private static (int, int, Color32[]) DrawBackground(int layer)
        {
            var buf = new Buffer(640, 180);
            var rng = new System.Random(Seed + layer);
            // sky stays void black (transparent over camera clear also void)
            if (layer == 0)
            {
                // far: severed tower silhouettes and crater negative space
                int x = 0;
                while (x < 640)
                {
                    int w = 24 + rng.Next(40);
                    int h = 30 + rng.Next(55);
                    bool crater = rng.Next(5) == 0;
                    if (!crater)
                    {
                        for (int i = 0; i < w && x + i < 640; i++)
                            for (int j = 0; j < h; j++)
                                if (Dither(x + i, j, 0.92f)) buf.Set(x + i, j, Charcoal);
                        // broken top
                        for (int i = 0; i < w && x + i < 640; i += 2 + rng.Next(3)) buf.Set(x + i, h, Charcoal);
                    }
                    x += w + 8 + rng.Next(30);
                }
            }
            else if (layer == 1)
            {
                // mid: disconnected bridges, antenna ribs, fog band
                for (int b = 0; b < 7; b++)
                {
                    int bx = rng.Next(640);
                    int by = 20 + rng.Next(50);
                    int bw = 40 + rng.Next(60);
                    for (int i = 0; i < bw; i++)
                    {
                        int xx = (bx + i) % 640;
                        if (i > bw / 3 && i < bw / 3 + 6) continue; // broken span
                        buf.Set(xx, by, MidGray);
                        if (Dither(xx, by - 1, 0.4f)) buf.Set(xx, by - 1, Charcoal);
                    }
                    for (int p = 0; p < 3; p++)
                    {
                        int px = (bx + rng.Next(bw)) % 640;
                        for (int j = 0; j < by; j++) if (Dither(px, j, 0.8f)) buf.Set(px, j, Charcoal);
                    }
                }
                for (int i = 0; i < 640; i++) if (Dither(i, 12, 0.25f)) buf.Set(i, 12 + (i / 40) % 3, MidGray);
            }
            else
            {
                // near: black debris occluders and hanging cable fragments
                for (int d = 0; d < 10; d++)
                {
                    int dx = rng.Next(640);
                    int dw = 10 + rng.Next(26);
                    int dh = 6 + rng.Next(16);
                    for (int i = 0; i < dw; i++)
                        for (int j = 0; j < dh; j++)
                            buf.Set((dx + i) % 640, j, Void);
                }
                for (int cbl = 0; cbl < 6; cbl++)
                {
                    int cx = rng.Next(640);
                    int len = 14 + rng.Next(30);
                    for (int j = 0; j < len; j++) buf.Set((cx + j / 6) % 640, 179 - j, Charcoal);
                }
            }
            return (buf.W, buf.H, buf.P);
        }

        // ------------------------------------------------------------------ import + validation

        public static void ApplyImportSettings(string root)
        {
            foreach (var file in Directory.GetFiles(root, "*.png"))
            {
                string assetPath = file.Replace('\\', '/');
                var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                if (importer == null) continue;
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.filterMode = FilterMode.Point;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.spritePixelsPerUnit = 16;
                importer.SaveAndReimport();
            }
        }

        /// <summary>Palette validation: every opaque pixel must be a locked-palette color.</summary>
        public static List<string> ValidatePalette(string root)
        {
            var allowed = new HashSet<uint>();
            foreach (var c in new[] { Void, Charcoal, MidGray, Ash, White, Cyan, Amber, Violet, Red })
            {
                allowed.Add(Pack(c));
            }
            var errors = new List<string>();
            foreach (var file in Directory.GetFiles(root, "*.png"))
            {
                byte[] bytes = File.ReadAllBytes(file);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.LoadImage(bytes);
                var pixels = tex.GetPixels32();
                foreach (var p in pixels)
                {
                    if (p.a == 0) continue;
                    if (!allowed.Contains(Pack(p)))
                    {
                        errors.Add($"{Path.GetFileName(file)}: non-palette pixel {p.r},{p.g},{p.b}");
                        break;
                    }
                }
                Object.DestroyImmediate(tex);
            }
            return errors;
        }

        private static uint Pack(Color32 c) => ((uint)c.r << 16) | ((uint)c.g << 8) | c.b;
    }
}
