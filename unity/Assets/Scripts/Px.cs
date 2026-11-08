// Нутаг pixel art, ported from js/village/pixel.js: every tile, sprite and icon is drawn pixel by pixel in code.
// Original art in a cozy farm-game style. No image files, no licenses. Canvas coordinates are top-down like the web canvas.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

/** A tiny software canvas: fillRect with source-over blending, turned into a point-filtered Texture2D at the end. */
public sealed class PxCanvas
{
    public readonly int W, H;
    readonly Color32[] px;
    public PxCanvas(int w, int h) { W = w; H = h; px = new Color32[w * h]; }
    /** A picture from pixels in texture order (rows from the bottom up, as GetPixels32 gives them). */
    public static PxCanvas From(Color32[] bottomUp, int w, int h) { var c = new PxCanvas(w, h); System.Array.Copy(bottomUp, c.px, w * h); return c; }
    /** Copies a whole picture of the same size in one go (fast backgrounds for scenes redrawn every frame). */
    public void CopyFrom(PxCanvas src) { if (src.W == W && src.H == H) System.Array.Copy(src.px, px, px.Length); else { Fill(default); Blit(src, 0, 0); } }

    /** globalAlpha of the web canvas: everything drawn is faded by it. */
    public float Alpha = 1;

    public void Put(int x, int y, Color32 c)
    {
        if (Alpha < 1) c.a = (byte)(c.a * Alpha);
        if (x < 0 || y < 0 || x >= W || y >= H || c.a == 0) return;
        int i = (H - 1 - y) * W + x;
        if (c.a == 255) { px[i] = c; return; }
        Color32 d = px[i];
        float a = c.a / 255f, da = d.a / 255f * (1 - a), oa = a + da;
        px[i] = new Color32((byte)((c.r * a + d.r * da) / oa), (byte)((c.g * a + d.g * da) / oa), (byte)((c.b * a + d.b * da) / oa), (byte)Mathf.RoundToInt(oa * 255));
    }
    public Color32 Get(int x, int y) => x < 0 || y < 0 || x >= W || y >= H ? default : px[(H - 1 - y) * W + x];

    /** drawImage: copies another canvas in, optionally mirrored left to right. */
    public void Blit(PxCanvas src, int X, int Y, bool flip = false)
    {
        for (int y = 0; y < src.H; y++) for (int x = 0; x < src.W; x++) Put(X + x, Y + y, src.Get(flip ? src.W - 1 - x : x, y));
    }

    /** A parallax layer as tall as this canvas: copies src's columns from x = sx on, over the whole width (fast, for scenes). */
    public void BlitRow(PxCanvas src, int sx)
    {
        int h = Math.Min(H, src.H);
        for (int y = 0; y < h; y++)
        {
            int di = (H - 1 - y) * W, si = (src.H - 1 - y) * src.W;
            for (int x = 0; x < W; x++)
            {
                int xs = sx + x; if (xs < 0 || xs >= src.W) continue;
                var c = src.px[si + xs]; if (c.a == 0) continue;
                if (c.a == 255) px[di + x] = c; else Put(x, y, c);
            }
        }
    }

    /** drawImage with scaling (nearest neighbour), from a part of src into a rectangle here. */
    public void BlitScaled(PxCanvas src, float dx, float dy, float dw, float dh, bool flip = false, int sx = 0, int sy = 0, int sw = -1, int sh = -1)
    {
        if (sw < 0) sw = src.W; if (sh < 0) sh = src.H;
        int x0 = Js.Round(dx), y0 = Js.Round(dy), w = Js.Round(dw), h = Js.Round(dh);
        if (w <= 0 || h <= 0) return;
        for (int y = Math.Max(0, y0); y < Math.Min(H, y0 + h); y++)
        {
            int syy = sy + (int)((y - y0 + 0.5f) * sh / h);
            for (int x = Math.Max(0, x0); x < Math.Min(W, x0 + w); x++)
            {
                int sxx = (int)((x - x0 + 0.5f) * sw / w); if (flip) sxx = sw - 1 - sxx;
                Put(x, y, src.Get(sx + sxx, syy));
            }
        }
    }
    public void Fill(Color32 c) { for (int i = 0; i < px.Length; i++) px[i] = c; }
    public void ClearRect(int x, int y, int w, int h) { for (int yy = Math.Max(0, y); yy < Math.Min(H, y + h); yy++) for (int xx = Math.Max(0, x); xx < Math.Min(W, x + w); xx++) px[(H - 1 - yy) * W + xx] = default; }
    /** Fills a polygon (like ctx.beginPath / lineTo / fill), sampling pixel centres. */
    public void FillPoly(IList<Vector2> pts, Color32 c)
    {
        float minY = float.MaxValue, maxY = float.MinValue; foreach (var p in pts) { minY = Math.Min(minY, p.y); maxY = Math.Max(maxY, p.y); }
        var xs = new List<float>();
        for (int y = Math.Max(0, (int)Math.Floor(minY)); y <= Math.Min(H - 1, (int)Math.Ceiling(maxY)); y++)
        {
            float cy = y + 0.5f; xs.Clear();
            for (int i = 0; i < pts.Count; i++)
            {
                Vector2 a = pts[i], b = pts[(i + 1) % pts.Count];
                if ((a.y <= cy && b.y > cy) || (b.y <= cy && a.y > cy)) xs.Add(a.x + (cy - a.y) / (b.y - a.y) * (b.x - a.x));
            }
            xs.Sort();
            for (int k = 0; k + 1 < xs.Count; k += 2) for (int x = Math.Max(0, (int)Math.Ceiling(xs[k] - 0.5f)); x <= Math.Min(W - 1, (int)Math.Floor(xs[k + 1] - 0.5f)); x++) Put(x, y, c);
        }
    }
    /** Copies the picture into an existing texture of the same size (for pictures redrawn every frame). */
    public void Upload(Texture2D t) { t.SetPixels32(px); t.Apply(false, false); }

    public Texture2D ToTexture()
    {
        var t = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        t.SetPixels32(px); t.Apply(false, true);
        return t;
    }
    /** A sprite at 16 pixels per tile; pivot is in pixels from the top-left corner (the feet of a character, say). */
    public Sprite ToSprite(float pivotX, float pivotY)
    {
        var t = ToTexture();
        return Sprite.Create(t, new Rect(0, 0, W, H), new Vector2(pivotX / W, 1 - pivotY / H), 16, 0, SpriteMeshType.FullRect);
    }
}

public static partial class Px
{
    // ------------------------------------------------------------ palette
    public static class K
    {
        public const string outl = "#2b1e2e", grass = "#76b947", grassD = "#5c9a36", grassL = "#94d260",
            leaf = "#57963a", leafD = "#3f7a2c", leafL = "#7cc04e", treeOut = "#2a4f1f", pine = "#3f8a57", pineD = "#2c6a44", pineL = "#5aaa6e",
            dirt = "#d8ab6c", dirtD = "#b78a4f", dirtL = "#ebc792", stone = "#d9cdb5", stoneD = "#b6a68a", stoneL = "#ece3d0",
            wood = "#a46d3b", woodD = "#6b4220", woodL = "#c99257",
            water = "#4aa6e2", waterD = "#3a88c8", waterL = "#93d4f6", foam = "#e6f7ff",
            rock = "#a5a5b0", rockD = "#77778a", rockL = "#d3d3dc",
            white = "#ffffff", felt = "#f4efe3", feltD = "#d8cfba", red = "#e14b3d", redD = "#a8312a", blue = "#3f76db", blueD = "#2b52a5",
            gold = "#f7c646", goldD = "#c98f22", orange = "#f39b2c", orangeD = "#c46d12", pink = "#f492ba", purple = "#a97de9", purpleD = "#7d57c4",
            brown = "#8a5a33", fur = "#ecd2a8", paper = "#f6dca8", eye = "#1e1a22", boot = "#4a3426", hair = "#3a2516", hairL = "#5a3a22", blush = "#f2a0a0",
            sky = "#9fd8f7", crop = "#4fa03a", cropD = "#357a28", cropL = "#86d05a", light = "#8fd0ff";
    }
    public static readonly Dictionary<string, string[]> DEEL = new Dictionary<string, string[]>
    {
        ["blue"] = new[] { "#3f76db", "#2b52a5", "#7fa6ee" }, ["red"] = new[] { "#e14b3d", "#a8312a", "#f08a7e" }, ["green"] = new[] { "#2ea35f", "#1d7442", "#6fd096" },
        ["purple"] = new[] { "#9466e0", "#6a42b5", "#c2a2f2" }, ["pink"] = new[] { "#f06ca8", "#c2427e", "#f8a8cc" }, ["gold"] = new[] { "#e9a91f", "#b07a10", "#f7d071" },
        ["teal"] = new[] { "#1fa3a3", "#137474", "#6fd0cf" }, ["brown"] = new[] { "#8a5a33", "#5e3b1e", "#b98a5a" }, ["white"] = new[] { "#f2eee4", "#c9c1ad", "#ffffff" }, ["black"] = new[] { "#2d2a33", "#17151b", "#5a5566" },
    };
    public static readonly Dictionary<string, string[]> SKIN = new Dictionary<string, string[]>
    {
        ["#F6D3B3"] = new[] { "#f6d3b3", "#e0b18c" }, ["#EDBB93"] = new[] { "#edbb93", "#d29a6e" }, ["#D9A06E"] = new[] { "#d9a06e", "#b9824f" }, ["#B97A4A"] = new[] { "#b97a4a", "#94592f" },
    };

    static readonly Dictionary<string, Color32> colCache = new Dictionary<string, Color32>();
    public static Color32 Col(string s)
    {
        if (colCache.TryGetValue(s, out var c)) return c;
        if (s[0] == '#')
        {
            string h = s.Substring(1);
            if (h.Length == 3) h = new string(new[] { h[0], h[0], h[1], h[1], h[2], h[2] });
            uint n = Convert.ToUInt32(h, 16);
            c = new Color32((byte)(n >> 16), (byte)(n >> 8), (byte)n, 255);
        }
        else
        {
            var p = s.Substring(s.IndexOf('(') + 1).TrimEnd(')').Split(',');
            float a = p.Length > 3 ? float.Parse(p[3], CultureInfo.InvariantCulture) : 1;
            c = new Color32(byte.Parse(p[0].Trim()), byte.Parse(p[1].Trim()), byte.Parse(p[2].Trim()), (byte)Mathf.RoundToInt(a * 255));
        }
        colCache[s] = c; return c;
    }
    public static string Shade(string hex, double f)
    {
        var c = Col(hex);
        int Ch(byte v) => Math.Max(0, Math.Min(255, Js.Round(f < 0 ? v * (1 + f) : v + (255 - v) * f)));
        return $"#{Ch(c.r):x2}{Ch(c.g):x2}{Ch(c.b):x2}";
    }

    // ------------------------------------------------------------ pixel helpers (integer coordinates only)
    public static double Hash(double x, double y) => Js.Hash(x, y);
    public static void R(PxCanvas x, double X, double Y, double w, double h, string col)
    {
        if (w <= 0 || h <= 0) return;
        var c = Col(col);
        int x0 = Js.Round(X), y0 = Js.Round(Y), ww = Js.Round(w), hh = Js.Round(h);
        for (int yy = y0; yy < y0 + hh; yy++) for (int xx = x0; xx < x0 + ww; xx++) x.Put(xx, yy, c);
    }
    public static void P(PxCanvas x, double X, double Y, string col) => R(x, X, Y, 1, 1, col);
    public static void Ell(PxCanvas x, int cx, int cy, int rx, int ry, string col, int maxY = int.MaxValue)
    {
        for (int dy = -ry; dy <= ry; dy++)
        {
            if (cy + dy > maxY) continue;
            int hw = Js.Round(rx * Math.Sqrt(Math.Max(0, 1 - (dy * dy) / ((ry + 0.5) * (ry + 0.5)))));
            R(x, cx - hw, cy + dy, hw * 2 + 1, 1, col);
        }
    }
    public static void Line(PxCanvas x, int x0, int y0, int x1, int y1, string col)
    {
        int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx + dy;
        for (int i = 0; i < 200; i++) { P(x, x0, y0, col); if (x0 == x1 && y0 == y1) break; int e2 = 2 * err; if (e2 >= dy) { err += dy; x0 += sx; } if (e2 <= dx) { err += dx; y0 += sy; } }
    }
    /** Outlined blob: dark ring then fill. */
    public static void Blob(PxCanvas x, int cx, int cy, int rx, int ry, string fill, string outl = null) { Ell(x, cx, cy, rx + 1, ry + 1, outl ?? K.outl); Ell(x, cx, cy, rx, ry, fill); }
    /** `v | 0 || 1` from the JS: truncate, and never zero. */
    public static int OrOne(double v) { int i = (int)v; return i == 0 ? 1 : i; }
    public static void Glyph(PxCanvas x, int X, int Y, string[] rows, Dictionary<char, string> map)
    {
        for (int j = 0; j < rows.Length; j++) for (int i = 0; i < rows[j].Length; i++) { char ch = rows[j][i]; if (ch != '.' && map.TryGetValue(ch, out var c)) P(x, X + i, Y + j, c); }
    }

    // ------------------------------------------------------------ terrain tiles (16×16)
    static void Grass(PxCanvas x, int X, int Y, int tx, int ty)
    {
        R(x, X, Y, 16, 16, K.grass);
        for (int py = 0; py < 16; py++) for (int px = 0; px < 16; px++) { double n = Hash(tx * 16 + px, ty * 16 + py); if (n < 0.06) P(x, X + px, Y + py, K.grassD); else if (n > 0.96) P(x, X + px, Y + py, K.grassL); }
        if (Hash(tx, ty) > 0.55)
        {
            int ox = X + 3 + (int)Math.Floor(Hash(ty, tx) * 9), oy = Y + 4 + (int)Math.Floor(Hash(tx + 7, ty) * 8);
            int[,] tuft = { { 0, 2 }, { 1, 1 }, { 1, 2 }, { 2, 0 }, { 2, 1 }, { 2, 2 }, { 3, 1 }, { 3, 2 }, { 4, 2 } };
            for (int i = 0; i < tuft.GetLength(0); i++) P(x, ox + tuft[i, 0], oy + tuft[i, 1], K.grassD);
            P(x, ox + 2, oy, K.grassL);
        }
    }
    static void Dirt(PxCanvas x, int X, int Y, int tx, int ty, bool[] nb)
    {
        R(x, X, Y, 16, 16, K.dirt);
        for (int py = 0; py < 16; py++) for (int px = 0; px < 16; px++) { double n = Hash(tx * 16 + px + 999, ty * 16 + py); if (n < 0.07) P(x, X + px, Y + py, K.dirtD); else if (n > 0.95) P(x, X + px, Y + py, K.dirtL); }
        for (int side = 0; side < 4; side++)
        {
            if (!nb[side]) continue;
            for (int i = 0; i < 16; i++)
            {
                int d = Hash(tx * 31 + i, ty * 17 + side) > 0.55 ? 2 : 1;
                for (int k = 0; k < d; k++)
                {
                    int px = side == 0 ? i : side == 1 ? i : side == 2 ? k : 15 - k, py = side == 0 ? k : side == 1 ? 15 - k : i;
                    P(x, X + px, Y + py, k == d - 1 ? K.grassD : K.grass);
                }
            }
        }
    }
    static void Plaza(PxCanvas x, int X, int Y, int tx, int ty)
    {
        R(x, X, Y, 16, 16, K.stone);
        for (int row = 0; row < 2; row++)
        {
            R(x, X, Y + row * 8, 16, 1, K.stoneD);
            int off = (ty * 2 + row) % 2 != 0 ? 4 : 0;
            foreach (int vx in new[] { off, off + 8 }) R(x, X + (vx % 16), Y + row * 8, 1, 8, K.stoneD);
            for (int i = 0; i < 3; i++) P(x, X + Math.Floor(Hash(tx + i, ty + row) * 15), Y + row * 8 + 2 + Math.Floor(Hash(ty, tx + i) * 4), K.stoneL);
        }
    }
    static void Water(PxCanvas x, int X, int Y, int tx, int ty, bool[] nb)
    {
        R(x, X, Y, 16, 16, K.water);
        for (int py = 0; py < 16; py++) if ((py + tx * 3 + ty * 5) % 9 == 0) for (int px = 0; px < 16; px++) if (Hash(tx * 16 + px, ty * 16 + py) > 0.35) P(x, X + px, Y + py, K.waterD);
        if (nb[0]) { R(x, X, Y, 16, 1, K.foam); R(x, X, Y + 1, 16, 1, K.waterL); }
        if (nb[1]) { R(x, X, Y + 15, 16, 1, K.waterL); }
        if (nb[2]) { R(x, X, Y, 1, 16, K.foam); R(x, X + 1, Y, 1, 16, K.waterL); }
        if (nb[3]) { R(x, X + 15, Y, 1, 16, K.foam); R(x, X + 14, Y, 1, 16, K.waterL); }
    }
    /** Trampled earth of a herding yard, with scattered straw and dung. */
    static void Yard(PxCanvas x, int X, int Y, int tx, int ty, bool[] nb)
    {
        R(x, X, Y, 16, 16, "#b89462");
        for (int py = 0; py < 16; py++) for (int px = 0; px < 16; px++) { double n = Hash(tx * 16 + px + 333, ty * 16 + py); if (n < 0.06) P(x, X + px, Y + py, "#9a7a4e"); else if (n > 0.97) P(x, X + px, Y + py, "#cfae7a"); }
        for (int i = 0; i < 3; i++) { int sx = X + 2 + (int)Math.Floor(Hash(tx + i, ty * 3) * 12), sy = Y + 2 + (int)Math.Floor(Hash(ty + i, tx * 5) * 12); Line(x, sx, sy, sx + 3, sy - 1, "#e8c86a"); }   // straw
        if (Hash(tx, ty + 9) < 0.3) { R(x, X + 9, Y + 10, 3, 2, "#6b4a2a"); P(x, X + 10, Y + 9, "#6b4a2a"); } // dried dung (аргал), the old fuel of the steppe
        for (int side = 0; side < 4; side++)
        {
            if (!nb[side]) continue;
            for (int i = 0; i < 16; i++) { int px = side == 0 ? i : side == 1 ? i : side == 2 ? 0 : 15, py = side == 0 ? 0 : side == 1 ? 15 : i; P(x, X + px, Y + py, Hash(i, side + tx) > 0.5 ? K.grass : K.grassD); }
        }
    }
    static void Sand(PxCanvas x, int X, int Y, int tx, int ty, bool[] nb)
    {
        R(x, X, Y, 16, 16, "#e8c994");
        for (int py = 0; py < 16; py++) for (int px = 0; px < 16; px++) { double n = Hash(tx * 16 + px + 77, ty * 16 + py); if (n < 0.08) P(x, X + px, Y + py, "#d4ae72"); else if (n > 0.95) P(x, X + px, Y + py, "#f6e2b8"); }
        if (nb[0]) R(x, X, Y, 16, 2, "#b98a50"); if (nb[1]) R(x, X, Y + 14, 16, 2, "#b98a50"); if (nb[2]) R(x, X, Y, 2, 16, "#b98a50"); if (nb[3]) R(x, X + 14, Y, 2, 16, "#b98a50");
    }
    static void Fence(PxCanvas x, int X, int Y, bool[] nb)
    {
        if (nb[2]) { R(x, X, Y + 5, 8, 3, K.woodD); R(x, X, Y + 5, 8, 2, K.wood); R(x, X, Y + 10, 8, 3, K.woodD); R(x, X, Y + 10, 8, 2, K.wood); }
        if (nb[3]) { R(x, X + 8, Y + 5, 8, 3, K.woodD); R(x, X + 8, Y + 5, 8, 2, K.wood); R(x, X + 8, Y + 10, 8, 3, K.woodD); R(x, X + 8, Y + 10, 8, 2, K.wood); }
        if (nb[0]) { R(x, X + 6, Y, 4, 4, K.woodD); R(x, X + 7, Y, 2, 4, K.wood); }
        if (nb[1]) { R(x, X + 6, Y + 12, 4, 4, K.woodD); R(x, X + 7, Y + 12, 2, 4, K.wood); }
        R(x, X + 5, Y + 2, 6, 13, K.woodD); R(x, X + 6, Y + 2, 4, 12, K.wood); R(x, X + 6, Y + 2, 1, 12, K.woodL); R(x, X + 6, Y + 2, 4, 1, K.woodL);
    }
    static void Dock(PxCanvas x, int X, int Y)
    {
        R(x, X, Y + 2, 16, 12, K.woodD); R(x, X, Y + 3, 16, 10, K.wood);
        for (int i = 0; i < 16; i += 4) { R(x, X + i, Y + 3, 1, 10, K.woodD); R(x, X + i + 1, Y + 3, 1, 10, K.woodL); }
        R(x, X + 1, Y + 13, 2, 3, K.woodD); R(x, X + 13, Y + 13, 2, 3, K.woodD);
    }
    static void Rock(PxCanvas x, int X, int Y) { Blob(x, X + 8, Y + 10, 5, 3, K.rock); Ell(x, X + 7, Y + 9, 3, 1, K.rockL); P(x, X + 11, Y + 12, K.rockD); P(x, X + 5, Y + 12, K.rockD); }
    static void Flower(PxCanvas x, int X, int Y, string col) { P(x, X, Y - 1, col); P(x, X - 1, Y, col); P(x, X + 1, Y, col); P(x, X, Y + 1, col); P(x, X, Y, K.gold); P(x, X, Y + 2, K.grassD); }

    // ------------------------------------------------------------ big sprites
    static void Tree(PxCanvas x, int tx, int ty, bool pine)
    {
        int X = tx * 16, Y = ty * 16;
        Ell(x, X + 8, Y + 14, 6, 2, "rgba(20,40,10,.25)");
        R(x, X + 6, Y + 5, 4, 10, K.woodD); R(x, X + 7, Y + 5, 2, 9, K.wood); P(x, X + 7, Y + 7, K.woodL);
        if (pine)
        {
            for (int t = 0; t < 4; t++)
            {
                int top = Y - 12 + t * 5, w = 2 + t * 2;
                for (int r = 0; r < 6; r++) { int hw = Js.Round((w * (r + 1)) / 6.0) + 1; R(x, X + 8 - hw - 1, top + r, hw * 2 + 3, 1, K.treeOut); }
                for (int r = 0; r < 6; r++) { int hw = Js.Round((w * (r + 1)) / 6.0); R(x, X + 8 - hw, top + r, hw * 2 + 1, 1, r < 2 ? K.pineL : K.pine); R(x, X + 8, top + r, hw + 1, 1, r < 2 ? K.pine : K.pineD); }
            }
            return;
        }
        Blob(x, X + 8, Y - 3, 9, 8, K.leafD, K.treeOut);
        Ell(x, X + 7, Y - 5, 7, 6, K.leaf);
        Ell(x, X + 5, Y - 8, 3, 2, K.leafL);
        for (int i = 0; i < 9; i++) P(x, X + 2 + Math.Floor(Hash(tx * 3 + i, ty) * 12), Y - 9 + Math.Floor(Hash(ty * 5 + i, tx) * 10), i % 3 != 0 ? K.leafD : K.leafL);
    }
    public static readonly string[] ICON_HEART = { ".xx.xx.", "xxxxxxx", "xxxxxxx", ".xxxxx.", "..xxx..", "...x..." };
    public static readonly string[] ICON_BOOK = { "kkk.kkk", "kwwkwwk", "kwwkwwk", "kwwkwwk", "kkkkkkk" };
    public static readonly string[] ICON_COIN = { ".yyy.", "yYyYy", "yyYyy", "yYyYy", ".yyy." };
    static readonly Dictionary<char, string> GLYPH_MAP = new Dictionary<char, string> { ['x'] = K.red, ['k'] = K.outl, ['w'] = K.white, ['g'] = K.gold };
    static readonly Dictionary<char, string> COIN_MAP = new Dictionary<char, string> { ['y'] = K.gold, ['Y'] = K.goldD };

    /** A real Mongol ger seen from the south: canvas over felt on a round lattice wall, three wall ropes, a low cone roof with a
        patterned trim band, the toono crown with its felt flap folded back, the stovepipe, and a carved, painted wooden door. */
    static void Ger(PxCanvas x, WObj o) => Ger(x, o, null, null);
    static void Ger(PxCanvas x, WObj o, string roof, string doorCol)
    {
        int X = o.x * 16, Y = o.y * 16, cx = X + 24;
        string acc = roof ?? (o.accent == "#7A5AF8" ? K.purple : o.accent == "#E9A91F" ? "#2f7d5b" : K.blue), accD = Shade(acc, -0.32), dc = doorCol ?? "#e8892b";
        const int WR = 20, RR = 23; // wall and roof-eave half widths
        int Arc(int i, int r, int d) => Js.Round(d * Math.Sqrt(Math.Max(0, 1 - Math.Pow((double)i / r, 2))));
        int EaveY(int i) => Y + 25 + Arc(i, RR, 3);
        int BaseY(int i) => Y + 43 + Arc(i, WR, 2);
        string Tone(double f, string a, string b, string c, string d) => f < -0.55 ? a : f < 0.15 ? b : f < 0.6 ? c : d;
        Ell(x, cx + 2, Y + 46, 24, 3, "rgba(20,40,10,.22)");
        // wall: felt + canvas, shaded round
        for (int i = -WR; i <= WR; i++)
        {
            double f = (double)i / WR; int top = EaveY(i), bot = BaseY(i);
            R(x, cx + i, top, 1, bot - top, Tone(f, "#fbf8f1", "#f2ede1", "#e4dccb", "#d2c7b0"));
            R(x, cx + i, bot - 3, 1, 3, Tone(f, "#b9aa88", "#ab9b7a", "#998a6a", "#857659")); // хаяавч: the skirt round the bottom
            P(x, cx + i, bot, K.outl);
            foreach (int off in new[] { 5, 10, 15 }) P(x, cx + i, top + off, (i + off) % 2 != 0 ? Tone(f, "#a8957a", "#9c896e", "#8a7860", "#776650") : Tone(f, "#c2b296", "#b4a488", "#a29276", "#8e7e64")); // twisted hair ropes
        }
        R(x, cx - WR - 1, EaveY(-WR), 1, BaseY(-WR) - EaveY(-WR) + 1, K.outl); R(x, cx + WR + 1, EaveY(WR), 1, BaseY(WR) - EaveY(WR) + 1, K.outl);
        // roof: shallow cone with slightly rounded shoulders
        for (int i = -RR; i <= RR; i++)
        {
            int a = Math.Abs(i); double t = a <= 5 ? 0 : (a - 5) / (double)(RR - 5);
            int top = Y + 12 + Js.Round(13 * Math.Pow(t, 1.25)), bot = EaveY(i); double f = (double)i / RR;
            P(x, cx + i, top - 1, K.outl);
            R(x, cx + i, top, 1, bot - top, Tone(f, "#ffffff", "#f6f2e9", "#e8e1d2", "#d6ccb7"));
            R(x, cx + i, bot - 3, 1, 3, f > 0.55 ? accD : acc); // trim band of the roof cover
            if ((i + 40) % 4 == 0) P(x, cx + i, bot - 2, "#ffffff"); else if ((i + 40) % 4 == 2) { P(x, cx + i, bot - 3, Shade(acc, 0.45)); P(x, cx + i, bot - 1, Shade(acc, 0.45)); } // хээ pattern
            P(x, cx + i, bot, K.outl);
            if (Math.Abs(i) <= WR) P(x, cx + i, bot + 1, Tone(f, "#d9cfbb", "#cfc4ae", "#bfb39b", "#ad9f86")); // the eave's shadow on the wall
        }
        foreach (int s in new[] { -1, 1 }) foreach (int r in new[] { 8, 16 }) Line(x, cx + s * 5, Y + 13, cx + s * r, EaveY(r) - 4, "#cbbfa5"); // roof ropes running down from the crown
        // toono crown and the felt flap folded back over its north half
        Ell(x, cx, Y + 12, 6, 2, K.outl); Ell(x, cx, Y + 12, 5, 1, "#c9772f"); R(x, cx - 3, Y + 12, 7, 1, "#5a3a22");
        R(x, cx - 6, Y + 8, 12, 4, K.outl); R(x, cx - 5, Y + 8, 10, 3, "#f1ece0"); R(x, cx + 1, Y + 8, 4, 3, "#ddd5c3");
        Line(x, cx - 6, Y + 10, cx - 11, Y + 17, "#9c896e"); Line(x, cx + 6, Y + 10, cx + 11, Y + 17, "#9c896e");
        // stovepipe
        R(x, cx + 2, Y + 1, 4, 11, K.outl); R(x, cx + 3, Y + 2, 2, 10, "#5d5d6b"); P(x, cx + 3, Y + 3, "#8a8a99"); R(x, cx + 1, Y, 6, 2, "#3a3a44");
        // door: carved frame, painted leaf with a gold border and a knot ornament, wooden threshold
        int dB = BaseY(0), dT = dB - 15; string fr = Shade(dc, -0.4);
        R(x, cx - 6, dT - 1, 13, dB - dT + 1, K.outl); R(x, cx - 5, dT, 11, dB - dT, fr);
        R(x, cx - 4, dT + 2, 9, dB - dT - 2, dc); R(x, cx - 4, dT + 2, 9, 1, Shade(dc, 0.3));
        R(x, cx - 3, dT + 3, 7, 1, K.gold); R(x, cx - 3, dB - 2, 7, 1, K.gold); R(x, cx - 3, dT + 3, 1, dB - dT - 5, K.gold); R(x, cx + 3, dT + 3, 1, dB - dT - 5, K.gold);
        P(x, cx, dT + 6, "#2b52a5"); P(x, cx - 1, dT + 7, "#2b52a5"); P(x, cx + 1, dT + 7, "#2b52a5"); P(x, cx, dT + 8, "#2b52a5"); P(x, cx, dT + 7, K.gold); // үлзий knot
        P(x, cx - 1, dT + 11, "#2b52a5"); P(x, cx + 1, dT + 11, "#2b52a5"); P(x, cx + 2, dT + 9, Shade(dc, -0.25));
        for (int i = -5; i <= 5; i += 2) P(x, cx + i, dT, K.gold);
        R(x, cx - 6, dB, 13, 2, "#6b4220"); R(x, cx - 6, dB, 13, 1, "#a46d3b");
        // little plaque for the town gers
        if (o.id == "arcade" || o.id == "home") Glyph(x, cx + 9, Y + 32, o.id == "arcade" ? ICON_BOOK : ICON_HEART, GLYPH_MAP);
    }
    /** Монгол бичгийн академи: a traditional hall with a curved green roof, red posts, carved doors and a signboard in vertical script. */
    static void Academy(PxCanvas x, WObj o)
    {
        int X = o.x * 16, Y = o.y * 16; const string G = "#2f7d5b", GL = "#46a077", GD = "#1d5a40";
        Ell(x, X + 32, Y + 46, 32, 3, "rgba(20,40,10,.25)");
        R(x, X + 2, Y + 40, 60, 7, K.outl); R(x, X + 3, Y + 41, 58, 5, K.stone); R(x, X + 3, Y + 41, 58, 1, K.stoneL); R(x, X + 24, Y + 45, 16, 2, K.stoneD);
        R(x, X + 6, Y + 19, 52, 22, K.outl); R(x, X + 7, Y + 20, 50, 20, "#f2e8d2"); R(x, X + 7, Y + 36, 50, 4, "#c9b48c");
        foreach (int wx in new[] { 11, 45 }) { R(x, X + wx, Y + 24, 8, 9, K.outl); R(x, X + wx + 1, Y + 25, 6, 7, "#7a4a2a"); for (int i = 0; i < 3; i++) R(x, X + wx + 1, Y + 26 + i * 2, 6, 1, K.goldD); R(x, X + wx + 3, Y + 25, 1, 7, K.goldD); }
        foreach (int px in new[] { 7, 21, 40, 54 }) { R(x, X + px, Y + 20, 3, 20, K.redD); R(x, X + px, Y + 20, 1, 20, K.red); }
        R(x, X + 25, Y + 24, 14, 16, K.outl); R(x, X + 26, Y + 25, 12, 15, "#c2302a"); R(x, X + 31, Y + 25, 2, 15, K.redD);
        for (int i = 0; i < 3; i++) { P(x, X + 28, Y + 28 + i * 4, K.gold); P(x, X + 35, Y + 28 + i * 4, K.gold); }
        R(x, X + 5, Y + 18, 54, 3, GD); for (int i = 7; i < 58; i += 4) { P(x, X + i, Y + 19, K.gold); P(x, X + i + 2, Y + 19, "#e14b3d"); }
        for (int i = -2; i <= 66; i++) // curved roof with upturned eaves
        {
            int d = i < 16 ? 16 - i : i > 48 ? i - 48 : 0, top = Y + 5 + Js.Round(d * 0.7), lift = i < 3 ? 3 - i : i > 61 ? i - 61 : 0, bot = Y + 18 - lift;
            P(x, X + i, top - 1, K.outl);
            for (int y = top; y < bot; y++) P(x, X + i, y, (y - Y) % 3 == 0 ? GD : (i + 70) % 4 == 0 ? G : GL);
            P(x, X + i, bot, K.outl); P(x, X + i, bot - 1, K.gold);
        }
        R(x, X + 15, Y + 4, 34, 2, K.outl); R(x, X + 16, Y + 4, 32, 1, K.gold);
        R(x, X + 30, Y - 3, 4, 8, K.outl); R(x, X + 31, Y - 2, 2, 6, K.gold); R(x, X + 29, Y - 5, 6, 3, K.outl); R(x, X + 30, Y - 4, 4, 1, K.gold);
        R(x, X + 22, Y + 9, 20, 9, K.outl); R(x, X + 23, Y + 10, 18, 7, "#24307a"); // signboard: vertical script
        for (int k = 0; k < 4; k++) { int sx = X + 26 + k * 4; R(x, sx, Y + 11, 1, 5, K.gold); P(x, sx - 1, Y + 12 + (k % 2), K.gold); P(x, sx + 1, Y + 14 - (k % 2), K.gold); }
    }
    static void Shop(PxCanvas x, WObj o)
    {
        int X = o.x * 16, Y = o.y * 16;
        Ell(x, X + 32, Y + 46, 30, 2, "rgba(20,40,10,.25)");
        R(x, X + 1, Y + 19, 62, 28, K.outl);
        for (int i = 0; i < 15; i++) R(x, X + 2 + i * 4, Y + 20, 4, 26, i % 2 != 0 ? K.white : K.blue);
        for (int col = 0; col < 64; col++) { int top = Y + 2 + Js.Round((Math.Abs(col - 32) * 18) / 32.0); P(x, X + col, top - 1, K.outl); R(x, X + col, top, 1, Y + 22 - top, col > 32 ? K.redD : K.red); }
        for (int i = 0; i < 8; i++) Ell(x, X + 4 + i * 8, Y + 22, 3, 1, K.redD);
        foreach (var (fx, fy, col) in new[] { (32, 1, K.gold), (2, 19, K.crop), (61, 19, K.blue) }) { R(x, X + fx, Y + fy - 8, 1, 8, K.woodD); for (int r = 0; r < 4; r++) R(x, X + fx + 1, Y + fy - 8 + r, OrOne(4 - Math.Abs(r - 1.5) * 1.5), 1, col); }
        R(x, X + 8, Y + 34, 48, 12, K.outl); R(x, X + 9, Y + 35, 46, 10, K.wood); R(x, X + 9, Y + 35, 46, 1, K.woodL);
        foreach (var (gx, col) in new[] { (12, K.orange), (17, K.red), (22, K.crop), (27, K.gold) }) { R(x, X + gx, Y + 32, 4, 3, col); P(x, X + gx + 1, Y + 31, K.crop); }
        R(x, X + 36, Y + 31, 6, 3, K.red); R(x, X + 35, Y + 33, 8, 1, K.redD);
        R(x, X + 46, Y + 31, 6, 3, K.white); R(x, X + 47, Y + 30, 2, 1, K.eye);
        R(x, X + 22, Y + 23, 20, 8, K.outl); R(x, X + 23, Y + 24, 18, 6, K.paper); Glyph(x, X + 30, Y + 24, ICON_COIN, COIN_MAP);
    }
    static void Board(PxCanvas x, WObj o)
    {
        int X = o.x * 16, Y = o.y * 16;
        R(x, X + 4, Y + 2, 2, 13, K.woodD); R(x, X + 26, Y + 2, 2, 13, K.woodD);
        R(x, X + 1, Y - 8, 30, 13, K.outl); R(x, X + 2, Y - 7, 28, 11, K.wood); R(x, X + 2, Y - 7, 28, 1, K.woodL);
        foreach (var (dx, dy, pin) in new[] { (4, -6, K.red), (12, -5, K.blue), (20, -6, K.crop) }) { R(x, X + dx, Y + dy, 7, 8, K.white); R(x, X + dx + 1, Y + dy + 3, 5, 1, K.feltD); R(x, X + dx + 1, Y + dy + 5, 4, 1, K.feltD); P(x, X + dx + 3, Y + dy + 1, pin); }
    }
    static void Well(PxCanvas x, WObj o)
    {
        int X = o.x * 16, Y = o.y * 16;
        Blob(x, X + 8, Y + 11, 6, 3, K.rock); Ell(x, X + 8, Y + 11, 4, 1, K.waterD); P(x, X + 6, Y + 11, K.waterL);
        R(x, X + 2, Y - 3, 2, 13, K.woodD); R(x, X + 12, Y - 3, 2, 13, K.woodD);
        for (int r = 0; r < 5; r++) R(x, X + 7 - r * 2, Y - 8 + r, 2 + r * 4, 1, r == 0 ? K.outl : K.red);
        R(x, X - 1, Y - 3, 18, 1, K.outl);
    }
    /** Setsen: a stone statue of a muscular man with short hair, flexing both arms, on a stone pedestal with a bronze plaque. */
    static void Statue(PxCanvas x, WObj o)
    {
        int X = o.x * 16, Y = o.y * 16; const string L = "#d6d6cf", M = "#aeaea6", D = "#7c7c74", S = "#5c5c56";
        Ell(x, X + 8, Y + 14, 11, 3, "rgba(30,20,10,.25)");
        // the pedestal: two stone steps, a plaque on the front
        R(x, X - 3, Y + 6, 22, 8, K.outl); R(x, X - 2, Y + 7, 20, 6, M); R(x, X - 2, Y + 7, 20, 1, L); R(x, X - 2, Y + 12, 20, 1, D);
        R(x, X, Y + 2, 16, 5, K.outl); R(x, X + 1, Y + 3, 14, 4, L); R(x, X + 1, Y + 6, 14, 1, D);
        R(x, X + 3, Y + 8, 10, 4, "#8a5a1e"); R(x, X + 4, Y + 9, 8, 2, "#d9a640"); P(x, X + 5, Y + 9, "#fff0a0");
        // legs, planted wide, with wrestling shorts
        R(x, X + 3, Y - 9, 4, 12, K.outl); R(x, X + 9, Y - 9, 4, 12, K.outl);
        R(x, X + 4, Y - 8, 2, 10, M); R(x, X + 10, Y - 8, 2, 10, D); P(x, X + 4, Y - 6, L);
        R(x, X + 3, Y - 12, 10, 5, K.outl); R(x, X + 4, Y - 11, 8, 3, D);
        // a broad V-shaped torso: wide shoulders, chest and the lines of the stomach
        for (int r = 0; r < 12; r++) { int half = 4 + r * 5 / 11; R(x, X + 8 - half - 1, Y - 13 - r, half * 2 + 2, 1, K.outl); R(x, X + 8 - half, Y - 13 - r, half * 2, 1, r > 8 ? L : M); }
        R(x, X + 8, Y - 22, 1, 4, S); P(x, X + 5, Y - 21, D); P(x, X + 11, Y - 21, D);   // the chest
        foreach (var (ax, ay) in new[] { (6, 18), (9, 18), (6, 16), (9, 16) }) P(x, X + ax, Y - ay, D);   // the stomach
        // both arms flexed up: upper arms out to the sides, forearms up, round biceps and fists
        foreach (int side in new[] { -1, 1 })
        {
            int sx = X + 8 + side * 8, ex = X + 8 + side * 12;
            Line(x, sx, Y - 23, ex, Y - 23, K.outl); R(x, Math.Min(sx, ex), Y - 25, 5, 4, K.outl); R(x, Math.Min(sx, ex) + 1, Y - 24, 3, 2, side < 0 ? L : M);
            Blob(x, sx + side * 2, Y - 26, 2, 2, side < 0 ? L : M, K.outl);              // a bicep
            R(x, ex - 1, Y - 31, 3, 8, K.outl); R(x, ex, Y - 30, 1, 6, side < 0 ? M : D);
            Blob(x, ex, Y - 32, 2, 2, side < 0 ? L : M, K.outl);                          // a fist
        }
        // a thick neck, the head and short hair
        R(x, X + 6, Y - 27, 4, 3, K.outl); R(x, X + 7, Y - 26, 2, 2, M);
        Blob(x, X + 8, Y - 31, 4, 4, L, K.outl); P(x, X + 7, Y - 31, S); P(x, X + 9, Y - 31, S); R(x, X + 7, Y - 29, 3, 1, D);
        R(x, X + 4, Y - 36, 9, 3, K.outl); R(x, X + 5, Y - 35, 7, 2, D); P(x, X + 6, Y - 35, M);   // a flat-top crew cut
    }
    static void Lantern(PxCanvas x, WObj o)
    {
        int X = o.x * 16, Y = o.y * 16;
        R(x, X + 7, Y - 2, 2, 17, K.outl);
        R(x, X + 5, Y - 9, 6, 7, K.outl); R(x, X + 6, Y - 8, 4, 5, K.gold); P(x, X + 6, Y - 8, K.white); R(x, X + 4, Y - 10, 8, 1, K.outl);
    }
    static void Lot(PxCanvas x, WObj o)
    {
        int X = o.x * 16, Y = o.y * 16;
        LotPatch(x, o);
        foreach (var (cx, cy) in new[] { (2, 6), (44, 6), (2, 44), (44, 44) }) R(x, X + cx, Y + cy - 4, 2, 6, K.woodD);
        R(x, X + 21, Y + 22, 2, 12, K.woodD); R(x, X + 14, Y + 16, 16, 8, K.outl); R(x, X + 15, Y + 17, 14, 6, K.paper); Glyph(x, X + 20, Y + 17, ICON_COIN, COIN_MAP);
    }
    /** The lot's bare ground, without its stakes and sign. */
    static void LotPatch(PxCanvas x, WObj o)
    {
        int X = o.x * 16, Y = o.y * 16;
        R(x, X + 2, Y + 6, 44, 40, "#c8a06a"); for (int i = 0; i < 40; i++) P(x, X + 3 + Math.Floor(Hash(o.i, i) * 42), Y + 7 + Math.Floor(Hash(i, o.i) * 38), i % 2 != 0 ? "#b38a55" : "#d8b47e");
    }
    static void Bleachers(PxCanvas x, WObj o)
    {
        int X = o.x * 16, Y = o.y * 16 - 10;
        R(x, X, Y, o.w * 16, 42, K.outl); R(x, X + 1, Y + 1, o.w * 16 - 2, 40, K.woodD);
        string[] shirts = { K.red, K.blue, K.gold, K.crop, K.purple, K.white };
        for (int row = 0; row < 3; row++)
        {
            int ry = Y + 4 + row * 12; R(x, X + 2, ry + 8, o.w * 16 - 4, 3, K.wood); R(x, X + 2, ry + 8, o.w * 16 - 4, 1, K.woodL);
            for (int i = 0; i < o.w * 2 - 1; i++)
            {
                if (Hash(i, row + 3) < 0.25) continue;
                int sx = X + 6 + i * 8 + (row % 2) * 3; string shirt = shirts[(int)Math.Floor(Hash(i * 3, row) * 6)];
                R(x, sx - 2, ry + 4, 5, 4, shirt); R(x, sx - 1, ry + 1, 3, 3, Hash(i, row) > 0.5 ? "#edbb93" : "#d9a06e"); R(x, sx - 1, ry, 3, 1, K.hair);
            }
        }
        string[] flags = { K.red, K.blue, K.gold };
        for (int i = 0; i < 6; i++) { int fx = X + 8 + i * 36; R(x, fx, Y - 10, 1, 11, K.woodD); for (int r = 0; r < 4; r++) R(x, fx + 1, Y - 10 + r, OrOne(4 - Math.Abs(r - 1.5) * 1.5), 1, flags[i % 3]); }
    }
    static void Ovoo(PxCanvas x, WObj o)
    {
        int X = o.x * 16, Y = o.y * 16;
        Ell(x, X + 16, Y + 28, 14, 3, "rgba(20,40,10,.25)");
        string[] stones = { K.rock, K.rockL, K.rockD };
        for (int r = 0; r < 10; r++) { int w = (int)(3 + r * 1.3); for (int i = -w; i <= w; i += 3) Blob(x, X + 16 + i + (r % 2), Y + 6 + r * 2, 1, 1, stones[(i + r + 9) % 3]); }
        R(x, X + 15, Y - 10, 2, 18, K.woodD);
        foreach (var (dx, dy, c) in new[] { (-8, 2, K.light), (6, 4, K.blue), (-4, -3, K.light), (3, -6, K.white) }) Line(x, X + 16, Y - 8, X + 16 + dx, Y + dy + 4, c);
        R(x, X + 13, Y - 12, 6, 2, K.blue);
    }
    static void ArenaFlags(PxCanvas x)
    {
        string[] cols = { K.blue, K.red, K.gold, K.white };
        for (int i = 0; i < 16; i++)
        {
            double a = (i / 16.0) * Math.PI * 2; int fx = Js.Round((World.ARENA_CX + Math.Cos(a) * (World.ARENA_RX + 0.6)) * 16), fy = Js.Round((World.ARENA_CY + Math.Sin(a) * (World.ARENA_RY + 0.6)) * 16);
            R(x, fx, fy - 12, 1, 12, K.woodD);
            for (int r = 0; r < 4; r++) R(x, fx + 1, fy - 12 + r, OrOne(5 - Math.Abs(r - 1.5) * 2), 1, cols[i % 4]);
        }
    }
    /** зэл: the rope line between two posts where the foals are tied while their mothers are milked. */
    static void Zel(PxCanvas x, WObj o)
    {
        int X = o.x * 16, Y = o.y * 16, w = o.w * 16;
        R(x, X + 2, Y - 2, 3, 16, K.outl); R(x, X + 3, Y - 1, 1, 15, K.wood); R(x, X + w - 5, Y - 2, 3, 16, K.outl); R(x, X + w - 4, Y - 1, 1, 15, K.wood);
        for (int i = X + 4; i < X + w - 4; i++) P(x, i, Y + 1 + Js.Round(Math.Sin(((i - X) / (double)w) * Math.PI) * 2), "#8a7a64");
    }
    /** хөхүүр: the leather airag bag on its wooden stand, with the churning stick. */
    static void Churn(PxCanvas x, WObj o)
    {
        int X = o.x * 16, Y = o.y * 16;
        R(x, X + 3, Y + 12, 10, 3, K.outl); R(x, X + 4, Y + 12, 8, 2, K.wood);
        Blob(x, X + 8, Y + 6, 5, 6, "#6b4220", K.outl); Ell(x, X + 6, Y + 4, 2, 3, "#8a5a33"); P(x, X + 5, Y + 2, "#a46d3b");
        R(x, X + 9, Y - 8, 2, 10, K.outl); R(x, X + 9, Y - 7, 1, 9, K.woodL); R(x, X + 7, Y - 9, 6, 2, K.woodD);
    }
    /** тулга: the outdoor iron stove with a big pot, for boiling milk. */
    static void Tulga(PxCanvas x, WObj o)
    {
        int X = o.x * 16, Y = o.y * 16;
        foreach (int lx in new[] { 3, 8, 12 }) Line(x, X + lx, Y + 15, X + 8, Y + 6, K.outl);
        R(x, X + 5, Y + 10, 6, 3, "#f39b2c"); P(x, X + 7, Y + 9, "#f7c646"); P(x, X + 9, Y + 11, "#fff2c8");
        Blob(x, X + 8, Y + 4, 6, 3, "#77778a", K.outl); Ell(x, X + 8, Y + 2, 5, 1, "#f6f2e9"); P(x, X + 5, Y + 3, "#a5a5b0");
    }
    /** тавиур: the wooden drying rack with pieces of aaruul drying in the sun. */
    static void Rack(PxCanvas x, WObj o)
    {
        int X = o.x * 16, Y = o.y * 16, w = o.w * 16;
        foreach (int lx in new[] { 2, w - 4 }) { R(x, X + lx, Y - 4, 2, 18, K.outl); R(x, X + lx, Y - 3, 1, 17, K.wood); }
        R(x, X + 1, Y - 2, w - 2, 6, K.outl); R(x, X + 2, Y - 1, w - 4, 4, "#c99257");
        for (int i = 4; i < w - 4; i += 3) for (int j = 0; j < 2; j++) { R(x, X + i, Y - 1 + j * 2, 2, 1, "#fff6dc"); P(x, X + i + 1, Y + j * 2, "#e8d6b0"); }
    }
    // уяа: the hitching line where race horses wait
    static void Uya(PxCanvas x, WObj o)
    {
        int X = o.x * 16, Y = o.y * 16, w = o.w * 16;
        R(x, X + 2, Y + 2, 3, 14, K.outl); R(x, X + 3, Y + 3, 1, 13, K.wood); R(x, X + w - 5, Y + 2, 3, 14, K.outl); R(x, X + w - 4, Y + 3, 1, 13, K.wood);
        R(x, X + 3, Y + 4, w - 6, 1, K.woodD);
        string[] cols = { K.blue, K.red, K.gold, K.white };
        for (int i = 0; i < 4; i++) R(x, X + 8 + i * ((w - 16) / 3), Y + 4, 3, 2, cols[i]);
        x.Blit(Horse("bay", 0, true), X + 3, Y - 10); x.Blit(Horse("white", 0, true), X + w - 31, Y - 9);
    }
    // сурын талбай: a Mongolian archery range with the low wall of leather "сур" targets at the far end
    static void Archery(PxCanvas x, WObj o)
    {
        int X = o.x * 16, Y = o.y * 16, w = o.w * 16, hh = o.h * 16;
        R(x, X, Y + 6, w, hh - 10, K.dirtL); R(x, X, Y + 6, w, 1, K.dirtD); R(x, X, Y + hh - 5, w, 1, K.dirtD);
        for (int i = 0; i < 9; i++) P(x, X + 6 + i * 13, Y + 18 + (i % 3) * 6, K.dirtD);
        R(x, X + 4, Y + 10, 2, 24, K.woodD); R(x, X + 10, Y + 10, 2, 24, K.woodD); R(x, X + 3, Y + 12, 10, 2, K.wood); // bow rack
        Line(x, X + 5, Y + 14, X + 5, Y + 30, K.woodL); Line(x, X + 11, Y + 15, X + 11, Y + 29, K.woodL);
        int tx = X + w - 22; string[] cols = { K.red, K.white, K.blue }; // target wall: three rows of small red, white and blue cylinders
        for (int r = 0; r < 3; r++) for (int c = 0; c < 5; c++) { int cx = tx + c * 3 + (r % 2), cy = Y + 18 + r * 4; R(x, cx, cy, 3, 4, K.outl); R(x, cx, cy, 2, 3, cols[(c + r) % 3]); }
        R(x, tx - 2, Y + 30, 20, 2, K.woodD);
        R(x, X + w - 4, Y + 2, 1, 16, K.woodD); for (int r = 0; r < 5; r++) R(x, X + w - 3, Y + 2 + r, 6 - Math.Abs(r - 2) * 2, 1, K.gold);
        R(x, X + 20, Y + 2, 1, 14, K.woodD); for (int r = 0; r < 5; r++) R(x, X + 21, Y + 2 + r, 6 - Math.Abs(r - 2) * 2, 1, K.blue);
    }

    /** The whole village ground with its buildings and trees, as one 896×640 picture (like renderStatic in pixel.js). */
    public static PxCanvas RenderStatic()
    {
        var x = RenderGround(false);
        var draws = new List<(int y, Action f)>();
        foreach (var t in World.trees) { var tt = t; draws.Add((tt.y, () => Tree(x, tt.x, tt.y, tt.pine))); }
        foreach (var o in World.objects) { if (!StaticProp(o)) continue; var oo = o; draws.Add((o.y + o.h - 1, () => PROPS[oo.kind](x, oo))); }
        foreach (var d in draws.OrderBy(d => d.y)) d.f(); // OrderBy is stable, like Array.sort
        return x;
    }

    static readonly HashSet<string> NOT_STATIC = new HashSet<string> { "felt", "npc", "emee", "npcq", "cow", "goat", "general", "historian" };
    /** Things that lie on the ground (an empty lot, the foals' rope line): flat in the isometric view too. */
    static readonly HashSet<string> FLAT = new HashSet<string> { "lot", "zel", "ger", "bleachers", "archery", "shop", "ovoo", "statue", "academy", "uya", "board", "well", "lantern", "churn", "tulga", "rack" }; // (all but the first two are built in 3D by Bodies.cs, and so are the trees)
    static readonly Dictionary<string, Action<PxCanvas, WObj>> PROPS = new Dictionary<string, Action<PxCanvas, WObj>>
    {
        ["ger"] = Ger, ["shop"] = Shop, ["board"] = Board, ["well"] = Well, ["lantern"] = Lantern, ["lot"] = Lot, ["bleachers"] = Bleachers, ["ovoo"] = Ovoo,
        ["uya"] = Uya, ["archery"] = Archery, ["academy"] = Academy, ["zel"] = Zel, ["churn"] = Churn, ["tulga"] = Tulga, ["rack"] = Rack, ["statue"] = Statue,
    };
    static bool StaticProp(WObj o) => !NOT_STATIC.Contains(o.kind);

    /** The isometric view's standing things: each tree and building cut out of the map on its own, with its feet
     *  (the bottom middle of the picture) at tile (fx, fy). The flat ones stay in RenderGround(true). */
    public static List<(PxCanvas art, float fx, float fy)> RenderProps()
    {
        var list = new List<(PxCanvas, float, float)>();
        var scratch = new PxCanvas(World.W * 16, World.H * 16);
        void Cut(Action draw, int x0, int y0, int x1, int y1)
        {
            x0 = Math.Max(0, x0 * 16); y0 = Math.Max(0, y0 * 16); x1 = Math.Min(scratch.W, x1 * 16); y1 = Math.Min(scratch.H, y1 * 16);
            draw();
            int bx0 = int.MaxValue, by0 = int.MaxValue, bx1 = -1, by1 = -1;
            for (int y = y0; y < y1; y++) for (int x = x0; x < x1; x++) if (scratch.Get(x, y).a != 0) { bx0 = Math.Min(bx0, x); by0 = Math.Min(by0, y); bx1 = Math.Max(bx1, x); by1 = Math.Max(by1, y); }
            if (bx1 >= 0)
            {
                var c = new PxCanvas(bx1 - bx0 + 1, by1 - by0 + 1);
                // see-through dark pixels are the shadow painted on the ground: standing up they'd be a grey wall, so they go
                // (the isometric ground gets its own shadow under each prop, Game.ShowMap)
                for (int y = by0; y <= by1; y++) for (int x = bx0; x <= bx1; x++) { var k = scratch.Get(x, y); if (k.a >= 110) c.Put(x - bx0, y - by0, k); }
                list.Add((c, (bx0 + bx1 + 1) / 32f, (by1 + 1) / 16f));
            }
            scratch.ClearRect(x0, y0, x1 - x0, y1 - y0);
        }
        foreach (var o in World.objects) { if (!StaticProp(o) || FLAT.Contains(o.kind)) continue; var oo = o; Cut(() => PROPS[oo.kind](scratch, oo), o.x - 3, o.y - 6, o.x + o.w + 3, o.y + o.h + 2); }
        return list;
    }

    /** The ground: tiles, flowers, fences, rocks, the dock and the arena's flags. flat (the isometric view): with the lots
     *  and rope lines on it, and without the fences, rocks, dock and flags, which stand up as boxes there (Bodies.cs). */
    public static PxCanvas RenderGround(bool flat)
    {
        int w = World.W, h = World.H; var x = new PxCanvas(w * 16, h * 16);
        bool Grassy(char ch) => ch != '\0' && "gTrfB".IndexOf(ch) >= 0;
        for (int ty = 0; ty < h; ty++) for (int tx = 0; tx < w; tx++)
        {
            char ch = World.g[ty, tx]; int X = tx * 16, Y = ty * 16;
            char[] nb4 = { World.At(tx, ty - 1), World.At(tx, ty + 1), World.At(tx - 1, ty), World.At(tx + 1, ty) };
            bool inPlaza = tx >= 15 && tx <= 20 && ty >= 10 && ty <= 15;
            if (ch == 'p') Dirt(x, X, Y, tx, ty, nb4.Select(Grassy).ToArray());
            else if (ch == 's' || (ch == 'B' && inPlaza)) Plaza(x, X, Y, tx, ty);
            else if (ch == 'w' || ch == 'd') { Water(x, X, Y, tx, ty, nb4.Select(n => n != '\0' && n != 'w' && n != 'd').ToArray()); if (ch == 'd' && !flat) Dock(x, X, Y); }
            else if (ch == 'k' || (ch == 'B' && tx >= 4 && tx <= 13 && ty >= 16 && ty <= 22)) Yard(x, X, Y, tx, ty, nb4.Select(n => n != 'k' && n != 'B').ToArray());
            else if (ch == 'a') Sand(x, X, Y, tx, ty, nb4.Select(n => n != 'a').ToArray());
            else { Grass(x, X, Y, tx, ty); if (!flat && ch == 'f') Fence(x, X, Y, nb4.Select(n => n == 'f').ToArray()); if (!flat && ch == 'r') Rock(x, X, Y); } // flat: they stand up in Bodies.cs
        }
        string[] fcols = { K.pink, K.gold, K.white, K.purple };
        for (int ty = 2; ty < h - 1; ty++) for (int tx = 1; tx < w - 1; tx++)
            if (World.g[ty, tx] == 'g' && Hash(tx * 3, ty * 7) < 0.1) Flower(x, tx * 16 + 4 + (int)Math.Floor(Hash(tx, ty) * 8), ty * 16 + 5 + (int)Math.Floor(Hash(ty, tx) * 7), fcols[(int)Math.Floor(Hash(tx, ty * 5) * 4)]);
        if (!flat) ArenaFlags(x);
        if (flat)
        {
            foreach (var o in World.objects.Where(o => o.kind == "lot")) LotPatch(x, o); // its stakes and sign stand up in Bodies.cs, like the rope line
            foreach (var o in World.objects)
            {
                int X = o.x * 16, Y = o.y * 16;
                if (o.kind == "archery") { R(x, X, Y + 6, o.w * 16, o.h * 16 - 10, K.dirtL); R(x, X, Y + 6, o.w * 16, 1, K.dirtD); R(x, X, Y + o.h * 16 - 5, o.w * 16, 1, K.dirtD); for (int i = 0; i < 9; i++) P(x, X + 6 + i * 13, Y + 18 + (i % 3) * 6, K.dirtD); }
                else if (o.kind == "ger") Ell(x, X + 28, Y + 30, 23, 13, "rgba(20,30,10,.3)"); // the 3D ger's shadow
                else if (o.kind == "bleachers") R(x, X + 4, Y + o.h * 16 - 2, o.w * 16, 5, "rgba(20,30,10,.3)");
                else if (o.kind == "shop") R(x, X + 6, Y + 28, o.w * 16 - 4, 22, "rgba(20,30,10,.3)");
                else if (o.kind == "ovoo") Ell(x, X + 19, Y + 23, 17, 9, "rgba(20,30,10,.3)");
                else if (o.kind == "statue") R(x, X - 1, Y + 3, 22, 14, "rgba(20,30,10,.3)");
                else if (o.kind == "academy") R(x, X + 4, Y + 30, o.w * 16, 22, "rgba(20,30,10,.3)");
                else if (o.kind == "well" || o.kind == "churn" || o.kind == "tulga" || o.kind == "lantern") Ell(x, X + 10, Y + 13, o.kind == "lantern" ? 4 : 8, 3, "rgba(20,30,10,.3)");
                else if (o.kind == "board" || o.kind == "rack") R(x, X + 4, Y + 11, o.w * 16, 5, "rgba(20,30,10,.3)");
            }
            foreach (var t in World.trees) Ell(x, t.x * 16 + 11, t.y * 16 + 12, t.pine ? 12 : 13, 6, "rgba(20,30,10,.3)"); // the 3D trees' shadows
            // the boxes' shadows on the ground, falling away from the light at the screen's upper left
            for (int ty = 0; ty < h; ty++) for (int tx = 0; tx < w; tx++)
            {
                char ch = World.g[ty, tx];
                if (ch == 'f') Ell(x, tx * 16 + 10, ty * 16 + 10, 4, 2, "rgba(20,30,10,.28)");
                else if (ch == 'r') Ell(x, tx * 16 + 10, ty * 16 + 12, 7, 3, "rgba(20,30,10,.3)");
            }
        }
        return x;
    }

    // ------------------------------------------------------------ characters (16×28, feet at y=27)
    static readonly Dictionary<string, PxCanvas> cache = new Dictionary<string, PxCanvas>();
    /** dir: 0 front, 1 left, 2 right, 3 back. frame: 0 stands, 1 and 2 walk. */
    public static PxCanvas Char(Look look, int dir, int frame)
    {
        string key = string.Join("|", "c", look.deel, look.skin, look.hat, look.face, look.neck, look.gender, dir, frame);
        if (cache.TryGetValue(key, out var hit)) return hit;
        PxCanvas outc;
        if (dir == 1) { var r = Char(look, 2, frame); outc = new PxCanvas(16, 28); outc.Blit(r, 0, 0, true); }
        else outc = DrawChar(look, dir, frame);
        cache[key] = outc; return outc;
    }
    static PxCanvas DrawChar(Look look, int dir, int frame)
    {
        var x = new PxCanvas(16, 28);
        var dl = DEEL.TryGetValue(look.deel ?? "", out var dd) ? dd : DEEL["blue"]; string D = dl[0], DD = dl[1], DL = dl[2];
        var sk = SKIN.TryGetValue(look.skin ?? "", out var ss) ? ss : SKIN["#F6D3B3"]; string S = sk[0];
        bool front = dir == 0, back = dir == 3, side = dir == 2;
        int Lift(int n) => frame == n ? 1 : 0;
        // boots
        if (side) { R(x, 6, 24 - Lift(1), 3, 3, K.boot); R(x, 8, 24 - Lift(2), 3, 3, "#3a2a1e"); }
        else { R(x, 5, 24 - Lift(1), 3, 3, K.boot); R(x, 8, 24 - Lift(2), 3, 3, K.boot); }
        // deel body
        int bx = side ? 4 : 3, bw = side ? 9 : 10;
        R(x, bx, 15, bw, 10, K.outl); R(x, bx + 1, 16, bw - 2, 8, D); R(x, bx + bw - 3, 16, 2, 8, DD); R(x, bx + 1, 23, bw - 2, 1, DD);
        R(x, bx + 1, 20, bw - 2, 1, K.gold);
        if (front) { P(x, 7, 16, DL); P(x, 8, 17, DL); P(x, 9, 18, DL); P(x, 10, 19, DL); }
        if (side) { P(x, 10, 16, DL); P(x, 11, 17, DL); }
        // arms
        if (side) { int sw = frame == 1 ? -1 : frame == 2 ? 1 : 0; R(x, 7 + sw, 16, 3, 6, K.outl); R(x, 8 + sw, 16, 1, 5, DD); P(x, 8 + sw, 21, S); }
        else { R(x, 1, 16, 3, 6, K.outl); R(x, 12, 16, 3, 6, K.outl); R(x, 2, 16, 1, 5, D); R(x, 13, 16, 1, 5, DD); if (front) { P(x, 2, 21, S); P(x, 13, 21, S); } }
        // neck accessories under the head
        if (look.neck == "scarf") { R(x, bx + 1, 15, bw - 2, 2, K.red); if (front) R(x, 9, 17, 2, 3, K.redD); }
        if (look.neck == "khadag" && !back)
        {
            foreach (var (a, b) in new[] { (4, 15), (5, 16), (6, 17), (7, 18), (8, 18), (9, 17), (10, 16), (11, 15) }) { if (side && a < 7) continue; R(x, a, b, 1, 2, K.light); }
            if (front) { R(x, 4, 17, 1, 5, K.light); R(x, 11, 17, 1, 5, K.light); }
        }
        if (look.neck == "medal" && !back) { Line(x, 6, 15, 8, 18, K.blue); Line(x, 10, 15, 8, 18, K.blue); R(x, 7, 18, 3, 2, K.gold); P(x, 8, 18, K.white); }
        // head
        Blob(x, 8, 10, 4, 4, S);
        if (back) Ell(x, 8, 10, 4, 4, K.hair);
        else
        {
            Ell(x, 8, 8, 4, 2, K.hair, 8); P(x, 4, 9, K.hair); P(x, 12, 9, K.hair);
            if (side) { Ell(x, 6, 10, 2, 3, K.hair); P(x, 11, 11, K.eye); P(x, 12, 12, K.blush); P(x, 7, 9, K.hairL); }
            else { P(x, 6, 11, K.eye); P(x, 10, 11, K.eye); P(x, 5, 12, K.blush); P(x, 11, 12, K.blush); P(x, 6, 7, K.hairL); }
            if (look.face == "glasses")
            {
                if (side) { R(x, 10, 10, 3, 1, K.outl); R(x, 10, 12, 3, 1, K.outl); P(x, 12, 11, K.outl); }
                else foreach (var (a, b) in new[] { (5, 10), (6, 10), (7, 10), (5, 12), (6, 12), (7, 12), (5, 11), (7, 11), (9, 10), (10, 10), (11, 10), (9, 12), (10, 12), (11, 12), (9, 11), (11, 11), (8, 11) }) P(x, a, b, K.outl);
            }
            if (look.face == "sun") { if (side) R(x, 10, 10, 3, 2, K.outl); else { R(x, 5, 10, 3, 2, K.outl); R(x, 9, 10, 3, 2, K.outl); P(x, 8, 10, K.outl); P(x, 6, 10, "#5b6b8a"); P(x, 10, 10, "#5b6b8a"); } }
        }
        // a girl's two long braids, tied with red at the ends
        if (look.Girl)
        {
            void Braid(int bx2, int top, int len) { R(x, bx2, top, 2, len, K.hair); for (int i = top + 1; i < top + len; i += 2) P(x, bx2 + (i / 2) % 2, i, K.hairL); R(x, bx2, top + len, 2, 1, K.red); }
            if (front) { Braid(3, 9, 10); Braid(12, 9, 10); }
            else if (side) Braid(4, 10, 9);
            else { Braid(5, 12, 8); Braid(10, 12, 8); }
        }
        // hats
        switch (look.hat)
        {
            case "mongol": R(x, 2, 6, 12, 2, K.redD); Ell(x, 8, 5, 4, 3, K.red, 6); P(x, 6, 3, "#f08a7e"); R(x, 7, 0, 2, 2, K.gold); if (back) { R(x, 6, 8, 1, 5, K.redD); R(x, 9, 8, 1, 5, K.redD); } break;
            case "cap": Ell(x, 8, 7, 5, 3, K.blue, 7); P(x, 6, 5, "#7fa6ee"); if (front) R(x, 4, 8, 8, 1, K.blueD); if (side) R(x, 11, 7, 4, 2, K.blueD); break;
            case "flower": { string[] fc = { K.pink, K.gold, K.white }; for (int i = 3; i <= 13; i++) P(x, i, 6, i % 2 != 0 ? fc[i % 3] : K.crop); P(x, 5, 5, K.pink); P(x, 9, 5, K.gold); P(x, 12, 5, K.white); break; }
            case "loovuuz": Ell(x, 8, 6, 6, 4, K.brown, 7); P(x, 6, 3, "#a8784a"); R(x, 2, 7, 12, 2, K.fur); if (!side) { R(x, 2, 8, 2, 5, K.fur); R(x, 12, 8, 2, 5, K.fur); } else R(x, 3, 8, 2, 5, K.fur); P(x, 8, 1, K.red); break;
            // traditional hats of Mongolia's peoples
            case "kazakh": Ell(x, 8, 5, 5, 4, K.white, 7); R(x, 3, 6, 10, 2, "#c9c1ad"); for (int i = 4; i <= 12; i += 2) P(x, i, 4, K.red); P(x, 6, 2, K.gold); P(x, 10, 2, K.gold); Line(x, 9, 1, 12, -2, "#4a3426"); P(x, 12, -1, "#d8cfba"); break; // embroidered hat with an owl feather
            case "buriad": for (int r = 0; r < 5; r++) R(x, 8 - r, 2 + r, r * 2 + 1, 1, K.blue); R(x, 2, 7, 12, 2, K.fur); P(x, 8, 1, K.red); P(x, 8, 0, K.red); if (back) { R(x, 7, 8, 1, 4, K.red); R(x, 9, 8, 1, 4, K.red); } break; // pointed hat, fur brim, red top
            case "oirat": Ell(x, 8, 5, 4, 3, K.blueD, 6); R(x, 2, 6, 12, 2, K.gold); R(x, 7, 0, 2, 3, K.red); P(x, 8, -1, K.red); if (back) { R(x, 6, 8, 1, 6, K.red); R(x, 9, 8, 1, 6, K.red); } break; // tall western hat with a red tassel
            case "fur": Ell(x, 8, 6, 6, 4, "#7a5a3a", 7); P(x, 6, 3, "#a8784a"); P(x, 10, 4, "#a8784a"); if (!side) { R(x, 2, 8, 3, 6, "#7a5a3a"); R(x, 11, 8, 3, 6, "#7a5a3a"); } else R(x, 3, 8, 3, 6, "#7a5a3a"); break; // reindeer-fur hat with ear flaps
            case "crown": R(x, 4, 4, 9, 3, K.gold); R(x, 4, 6, 9, 1, K.goldD); foreach (var (a, b) in new[] { (4, 2), (4, 3), (8, 1), (8, 2), (8, 3), (12, 2), (12, 3) }) P(x, a, b, K.gold); P(x, 8, 5, K.red); break;
        }
        return x;
    }

    // ------------------------------------------------------------ animals, facing right; frame 0 stands, 1 and 2 walk
    public static void Hooves(PxCanvas x, int f, int[] legs, int top, int len, string col, string hoof, string dark)
    {
        for (int i = 0; i < legs.Length; i++)
        {
            int lx = legs[i], lift = f != 0 && (i + f) % 2 != 0 ? 1 : 0, sw = f != 0 ? ((i + f) % 2 != 0 ? 1 : -1) * (i < 2 ? 1 : -1) : 0;
            R(x, lx + sw, top, 2, len - lift, K.outl); R(x, lx + sw, top, 1, len - lift - 1, i % 2 != 0 ? dark : col); P(x, lx + sw, top + len - lift - 1, hoof);
        }
    }
    public static void Eye(PxCanvas x, int X, int Y) { P(x, X, Y, K.eye); P(x, X, Y - 1, Shade(K.eye, 0.5)); }
    public static void Wool(PxCanvas x, int cx, int cy, int rx, int ry, string b, int s)
    {
        for (int dy = -ry; dy <= ry; dy++) for (int dx = -rx; dx <= rx; dx++)
            if (Math.Pow(dx / (rx + 0.5), 2) + Math.Pow(dy / (ry + 0.5), 2) <= 1 && Hash(dx + s, dy + 7 * s) < 0.3) P(x, cx + dx, cy + dy, (dx + dy) % 2 != 0 ? Shade(b, -0.12) : Shade(b, 0.25));
    }
    /** Any animal sprite, cached: sheep, lamb, goat, cow, foal, mare. */
    public static PxCanvas Beast(string kind, int f)
    {
        string key = "b:" + kind + f; if (cache.TryGetValue(key, out var hit)) return hit;
        PxCanvas x = MoreBeast(kind, f);
        if (x != null) { cache[key] = x; return x; }
        switch (kind)
        {
            case "mare": x = Horse("chestnut", Math.Min(4, f), false); break;
            case "goat": // long-haired white goat with a beard and swept-back horns
                x = new PxCanvas(18, 16);
                Hooves(x, f, new[] { 4, 6, 10, 12 }, 10, 6, "#8a7a64", "#3a3030", "#6a5a4a");
                Blob(x, 8, 7, 5, 3, "#f2eee4", "#7a7064"); for (int i = 3; i < 14; i++) if (i % 2 != 0) R(x, i, 10, 1, 2 + (i % 3), "#e2dccc"); // shaggy fringe
                Ell(x, 7, 5, 3, 1, "#ffffff"); P(x, 2, 4, "#f2eee4"); P(x, 2, 3, "#f2eee4"); // tail up
                R(x, 12, 4, 3, 3, "#f2eee4"); Blob(x, 15, 4, 2, 2, "#f2eee4", K.outl); R(x, 16, 7, 1, 3, "#d8d0c0"); Eye(x, 15, 3); // neck, head, beard
                P(x, 14, 1, "#8a7a64"); P(x, 13, 0, "#8a7a64"); P(x, 13, 1, "#6a5a4a"); P(x, 12, 0, "#6a5a4a"); // horns
                break;
            case "cow": // brown and white cow with an udder
                x = new PxCanvas(24, 18);
                Hooves(x, f, new[] { 5, 8, 15, 18 }, 12, 6, "#5a3a22", "#1e1a1e", "#4a2e1a");
                Blob(x, 11, 8, 8, 4, "#8a5a33", K.outl); Ell(x, 10, 6, 6, 1, "#a46d3b"); Blob(x, 8, 8, 3, 2, "#f2eee4"); Blob(x, 14, 9, 2, 2, "#f2eee4");
                R(x, 11, 12, 3, 2, "#f2a0a0"); P(x, 11, 14, "#e88080"); P(x, 13, 14, "#e88080"); // udder
                R(x, 2, 6, 1, 7, "#5a3a22"); R(x, 1, 12, 3, 2, "#2b1e14"); // tail with tuft
                Blob(x, 20, 7, 3, 3, "#8a5a33", K.outl); R(x, 21, 9, 3, 2, "#f2d8c8"); P(x, 23, 10, "#5a3a22"); Eye(x, 21, 6); R(x, 18, 4, 2, 1, "#6b4220"); R(x, 21, 3, 1, 2, "#e8dcc0"); R(x, 23, 3, 1, 2, "#e8dcc0");
                break;
            case "foal":
                x = new PxCanvas(16, 15);
                Hooves(x, f, new[] { 4, 6, 10, 11 }, 9, 6, "#a46d3b", "#2b1e14", "#8a5a33"); Blob(x, 7, 7, 4, 2, "#b98a55", K.outl); Ell(x, 6, 6, 3, 1, "#d0a46a"); R(x, 10, 3, 2, 4, "#b98a55"); Blob(x, 13, 3, 2, 1, "#b98a55", K.outl); Eye(x, 13, 2); R(x, 10, 1, 1, 3, "#6b4220"); P(x, 2, 6, "#6b4220"); P(x, 2, 7, "#6b4220");
                break;
            case "lamb":
                x = new PxCanvas(14, 12);
                Hooves(x, f, new[] { 3, 5, 8, 10 }, 8, 4, "#c9bda6", "#6a5e52", "#b6a890"); Blob(x, 6, 5, 4, 3, "#fbf6ec", "#8a8274"); Wool(x, 6, 5, 4, 3, "#fbf6ec", 5); Blob(x, 11, 3, 2, 2, "#f2dcc8", "#8a8274"); Eye(x, 12, 2); P(x, 13, 4, "#e8a0a0"); R(x, 9, 1, 2, 1, "#e8c8b0");
                break;
            case "botgo": // a baby camel
                x = new PxCanvas(16, 15);
                Hooves(x, f, new[] { 4, 6, 10, 11 }, 9, 6, "#d9b07a", "#8a5a33", "#c99a5a"); Blob(x, 7, 7, 4, 2, "#e2c08a", K.outl); P(x, 5, 4, "#c99a5a"); P(x, 9, 4, "#c99a5a"); R(x, 11, 2, 2, 5, "#e2c08a"); Blob(x, 14, 2, 2, 1, "#e2c08a", K.outl); Eye(x, 14, 1);
                break;
            default: // fat-tailed Mongolian sheep: cream wool, dark brown head
                x = new PxCanvas(20, 16);
                Hooves(x, f, new[] { 5, 8, 12, 14 }, 10, 6, "#3b3236", "#1e1a1e", "#2b2427");
                Blob(x, 9, 7, 6, 4, "#efe6d4", "#6a5e52"); Ell(x, 9, 9, 5, 1, "#d8ccb6"); Ell(x, 8, 5, 4, 1, "#fbf6ec"); Wool(x, 9, 7, 6, 4, "#efe6d4", 3);
                Blob(x, 3, 9, 2, 2, "#e6dcc6", "#6a5e52"); P(x, 2, 10, "#c9bda6"); // the fat tail
                Blob(x, 16, 5, 2, 3, "#3b3236", K.outl); R(x, 17, 6, 2, 2, "#4a4044"); P(x, 18, 7, "#2b2427"); Eye(x, 17, 4); R(x, 14, 3, 2, 1, "#2b2427"); P(x, 14, 4, "#3b3236");
                break;
        }
        cache[key] = x; return x;
    }

    // Mongolian horse for riding and racing (28×22, facing right). frame 0 stands, 1–4 gallop. saddle: draw a saddle.
    public static readonly Dictionary<string, string[]> COATS = new Dictionary<string, string[]>
    {
        ["bay"] = new[] { "#8a5a33", "#6b4220", "#2b1e14" }, ["black"] = new[] { "#3a2c2c", "#241a1a", "#120c0c" }, ["white"] = new[] { "#ece4d4", "#c9bda6", "#8a7a64" },
        ["chestnut"] = new[] { "#b5652e", "#8a4a1e", "#5a2a10" }, ["gray"] = new[] { "#a0a0aa", "#7a7a86", "#3a3a44" },
    };
    static readonly int[][][] HORSE_LEGS =
    {
        new[] { new[] { 7, 0 }, new[] { 9, 0 }, new[] { 17, 0 }, new[] { 19, 0 } }, new[] { new[] { 5, -2 }, new[] { 8, 1 }, new[] { 18, 2 }, new[] { 21, -1 } },
        new[] { new[] { 7, 1 }, new[] { 10, -1 }, new[] { 16, -1 }, new[] { 19, 1 } }, new[] { new[] { 9, -1 }, new[] { 6, 2 }, new[] { 20, 1 }, new[] { 17, -2 } },
        new[] { new[] { 8, 2 }, new[] { 10, 0 }, new[] { 18, -2 }, new[] { 16, 1 } },
    };
    public static PxCanvas Horse(string coat, int frame, bool saddle)
    {
        string key = "h:" + coat + frame + saddle; if (cache.TryGetValue(key, out var hit)) return hit;
        var x = new PxCanvas(28, 22); var cc = COATS.TryGetValue(coat, out var c0) ? c0 : coat == "dun" ? new[] { "#c99a5a", "#a07444", "#3a2516" } : COATS["bay"]; /* dun: the takhi (Przewalski's horse) */ string B = cc[0], BD = cc[1], MN = cc[2]; int bob = frame == 2 || frame == 4 ? 1 : 0;
        var legs = HORSE_LEGS[frame];
        for (int i = 0; i < legs.Length; i++)
        {
            int lx = legs[i][0], sw0 = legs[i][1], top = 13 + bob, len = 7 - (sw0 < 0 ? 1 : 0);
            Line(x, lx, top, lx + sw0, top + len, K.outl); Line(x, lx + 1, top, lx + 1 + sw0, top + len, i % 2 != 0 ? BD : B); P(x, lx + sw0, top + len, MN); P(x, lx + 1 + sw0, top + len, MN);
        }
        int sw = frame != 0 ? (frame % 2 != 0 ? 1 : -1) : 0; // tail swish
        Line(x, 6, 9 + bob, 2 + sw, 15 + bob, K.outl); Line(x, 5, 9 + bob, 1 + sw, 14 + bob, MN); Line(x, 6, 10 + bob, 3 + sw, 16 + bob, MN);
        Blob(x, 13, 10 + bob, 8, 4, B, K.outl); Ell(x, 12, 9 + bob, 6, 1, Shade(B, 0.15));
        for (int r = 0; r < 7; r++) R(x, 18 + Js.Round(r * 0.7), 9 + bob - r, 4, 3, B); // neck
        Line(x, 18, 8 + bob, 22, 2 + bob, MN); Line(x, 19, 8 + bob, 23, 2 + bob, MN); // mane
        Blob(x, 24, 3 + bob, 3, 2, B, K.outl); R(x, 25, 4 + bob, 3, 2, BD); P(x, 27, 4 + bob, K.outl); P(x, 23, 2 + bob, K.eye); P(x, 22, 0 + bob, B); P(x, 22, 1 + bob, K.outl);
        if (saddle) { R(x, 10, 5 + bob, 8, 3, K.outl); R(x, 11, 5 + bob, 6, 2, K.red); R(x, 11, 7 + bob, 6, 1, K.gold); R(x, 13, 8 + bob, 1, 4, K.woodD); P(x, 13, 12 + bob, K.gold); }
        cache[key] = x; return x;
    }

    // Басар: a Mongolian Bankhar dog. Big, fluffy, black coat with tan "four-eye" spots above the eyes,
    // tan muzzle, chest and paws, and a bushy tail curled over the back. Faces right; frames 0–2 walk, 3 sit.
    const string BKout = "#1a1418", BKcoat = "#2a2326", BKcoatL = "#4a3f44", BKtan = "#c98a4b", BKtanL = "#e2ab6c", BKtongue = "#e8687a";
    public static PxCanvas Bankhar(int frame)
    {
        string key = "bk:" + frame; if (cache.TryGetValue(key, out var hit)) return hit;
        var x = new PxCanvas(22, 18); bool sit = frame == 3;
        // tail: a fluffy curl over the back
        Blob(x, 4, sit ? 9 : 5, 3, 2, BKcoat, BKout); P(x, 2, sit ? 8 : 4, BKcoatL); P(x, 6, sit ? 8 : 4, BKtanL);
        if (sit)
        {
            Blob(x, 9, 11, 5, 4, BKcoat, BKout); // haunch
            R(x, 4, 15, 9, 2, BKout); R(x, 5, 15, 7, 1, BKtan); // folded back leg
            Blob(x, 14, 8, 3, 5, BKcoat, BKout); // upright chest
            R(x, 15, 9, 2, 4, BKtan);
            foreach (int lx in new[] { 13, 16 }) { R(x, lx, 12, 2, 5, BKout); R(x, lx, 12, 1, 4, BKcoat); R(x, lx, 15, 2, 2, BKtan); }
        }
        else
        {
            int b = frame == 2 ? 1 : 0;
            foreach (var (lx, ph) in new[] { (5, 0), (7, 1), (12, 1), (14, 0) })
            {
                int lift = frame != 0 && (ph ^ (frame - 1)) != 0 ? 1 : 0;
                R(x, lx, 11, 2, 6 - lift, BKout); R(x, lx, 11, 1, 4 - lift, BKcoat); R(x, lx, 15 - lift, 2, 1, BKtan);
            }
            Blob(x, 10, 9 - b, 6, 3, BKcoat, BKout); // fluffy body
            Ell(x, 9, 8 - b, 4, 1, BKcoatL);
            Blob(x, 15, 8 - b, 3, 4, BKcoat, BKout); // thick mane around the neck
            R(x, 16, 10 - b, 2, 2, BKtan); // tan chest
        }
        int hy = sit ? 0 : (frame == 2 ? 1 : 2);
        Blob(x, 17, hy + 4, 3, 3, BKcoat, BKout); // head
        R(x, 14, hy + 2, 2, 5, BKout); R(x, 15, hy + 3, 1, 3, BKcoatL); // floppy ear
        R(x, 19, hy + 5, 3, 2, BKout); R(x, 19, hy + 5, 2, 2, BKtan); P(x, 21, hy + 5, BKout); // muzzle + nose
        P(x, 18, hy + 4, BKout); P(x, 18, hy + 3, BKtanL); // eye + the tan "second eye" above it
        if (sit || frame == 0) P(x, 20, hy + 7, BKtongue);
        cache[key] = x; return x;
    }

    // ------------------------------------------------------------ small things for the screen
    /** Soft oval shadow under characters and animals. */
    public static PxCanvas Shadow(int rx)
    {
        string key = "sh:" + rx; if (cache.TryGetValue(key, out var hit)) return hit;
        var x = new PxCanvas(rx * 2 + 3, 5); Ell(x, rx + 1, 2, rx, 1, "rgba(20,40,10,.28)"); cache[key] = x; return x;
    }
    /** 14×14 icons for the dairy foods (the web game draws them in icons.js). */
    public static PxCanvas DairyIcon(string id)
    {
        string key = "di:" + id; if (cache.TryGetValue(key, out var hit)) return hit;
        var x = new PxCanvas(14, 14);
        switch (id)
        {
            case "milk": R(x, 4, 3, 6, 10, K.outl); R(x, 5, 4, 4, 8, K.white); R(x, 5, 4, 4, 2, K.blue); R(x, 5, 1, 4, 2, K.outl); R(x, 6, 2, 2, 1, K.blue); P(x, 5, 7, K.felt); break;
            case "mare": Blob(x, 7, 8, 4, 4, "#f4efe3", K.outl); R(x, 5, 2, 4, 3, K.outl); R(x, 6, 3, 2, 2, "#f4efe3"); R(x, 4, 8, 7, 1, K.brown); P(x, 5, 6, K.white); break;
            case "aaruul": foreach (var (ax, ay) in new[] { (1, 7), (6, 8), (4, 3), (9, 4) }) { R(x, ax, ay, 5, 4, K.outl); R(x, ax + 1, ay + 1, 3, 2, "#fff6dc"); P(x, ax + 1, ay + 1, K.white); } break;
            case "urum": Ell(x, 7, 9, 6, 3, K.outl); Ell(x, 7, 9, 5, 2, K.blue); Ell(x, 7, 7, 5, 1, "#f7e08a"); P(x, 4, 7, "#fff2c8"); break;
            case "tarag": Ell(x, 7, 9, 6, 3, K.outl); Ell(x, 7, 9, 5, 2, K.red); Ell(x, 7, 7, 5, 1, K.white); P(x, 9, 7, K.felt); break;
            case "airag": Ell(x, 7, 9, 6, 3, K.outl); Ell(x, 7, 9, 5, 2, K.wood); Ell(x, 7, 7, 5, 1, "#fbf8f1"); P(x, 5, 7, K.feltD); P(x, 9, 6, K.white); break;
        }
        cache[key] = x; return x;
    }
    public static PxCanvas CoinIcon()
    {
        if (cache.TryGetValue("coin", out var hit)) return hit;
        var x = new PxCanvas(9, 9); Blob(x, 4, 4, 3, 3, K.gold, K.goldD); P(x, 3, 3, K.white); R(x, 4, 3, 1, 3, K.goldD); cache["coin"] = x; return x;
    }
}
