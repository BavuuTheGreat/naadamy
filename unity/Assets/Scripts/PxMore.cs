// More of js/village/pixel.js: UI icons, Basar's face, wrestlers, the player's ger and the room inside it, race flags, more animals.
using System;
using System.Collections.Generic;
using UnityEngine;

public static partial class Px
{
    static readonly Dictionary<string, PxCanvas> cache2 = new Dictionary<string, PxCanvas>();
    static PxCanvas Cached(string key, Func<PxCanvas> make) { if (!cache2.TryGetValue(key, out var c)) cache2[key] = c = make(); return c; }

    // ------------------------------------------------------------ more animals (facing right; frame 0 stands, 1 and 2 walk)
    static readonly Dictionary<string, (int w, int h, Action<PxCanvas, int> draw)> MORE = new Dictionary<string, (int, int, Action<PxCanvas, int>)>
    {
        ["yak"] = (24, 18, (x, f) => { // shaggy yak: hump, long skirt of hair, pale horns
            Hooves(x, f, new[] { 6, 9, 15, 18 }, 13, 5, "#2b2222", "#14100f", "#201a1a");
            Blob(x, 12, 8, 8, 4, "#3a2c2c", K.outl); Blob(x, 15, 5, 4, 2, "#3a2c2c"); Ell(x, 13, 5, 5, 1, "#5a4848");
            for (int i = 4; i < 21; i++) R(x, i, 11, 1, 3 + ((i * 7) % 3), i % 2 != 0 ? "#2b2222" : "#4a3c3c");
            for (int i = 0; i < 6; i++) P(x, 3 - (i % 2), 6 + i, i % 2 != 0 ? "#f2eee4" : "#d8d0c0");
            Blob(x, 20, 8, 2, 3, "#3a2c2c", K.outl); R(x, 21, 10, 2, 2, "#2b2222"); Eye(x, 21, 7); R(x, 19, 4, 1, 2, "#e8dcc0"); R(x, 22, 4, 1, 2, "#e8dcc0"); P(x, 18, 4, "#e8dcc0"); P(x, 23, 4, "#e8dcc0");
        }),
        ["camel"] = (26, 22, (x, f) => { // two-humped Bactrian camel: woolly humps, long neck, shaggy throat
            const string C = "#c99a5a", D = "#a07444", W = "#7a4f2c";
            Hooves(x, f, new[] { 6, 9, 16, 19 }, 14, 8, C, "#6b4220", D);
            foreach (var (hx, hh) in new[] { (8, 5), (15, 5) }) for (int i = -3; i <= 3; i++) { int top = 9 - Js.Round(hh * Math.Cos((i / 3.6) * (Math.PI / 2))); R(x, hx + i, top - 1, 1, 6, K.outl); }
            Blob(x, 12, 11, 8, 4, C, K.outl);
            foreach (var (hx, hh) in new[] { (8, 5), (15, 5) }) for (int i = -3; i <= 3; i++) { int top = 9 - Js.Round(hh * Math.Cos((i / 3.6) * (Math.PI / 2))); R(x, hx + i, top, 1, 5, C); P(x, hx + i, top, W); if (Math.Abs(i) < 2) P(x, hx + i, top + 1, W); }
            Ell(x, 12, 13, 6, 1, D); Ell(x, 11, 9, 2, 0, Shade(C, 0.15));
            for (int r = 0; r < 8; r++) R(x, 19 + Js.Round(r * 0.55), 11 - r, 3, 2, K.outl);
            for (int r = 0; r < 8; r++) R(x, 20 + Js.Round(r * 0.55), 11 - r, 2, 2, C);
            for (int i = 0; i < 4; i++) { P(x, 19, 11 + i, W); P(x, 20, 12 + i, Shade(W, 0.2)); }
            R(x, 23, 2, 4, 3, K.outl); R(x, 23, 2, 3, 2, C); P(x, 26, 4, K.outl); Eye(x, 24, 2); P(x, 23, 1, K.outl);
            P(x, 2, 10, W); P(x, 2, 11, W); P(x, 3, 12, W);
        }),
        ["reindeer"] = (20, 18, (x, f) => { Hooves(x, f, new[] { 5, 7, 12, 14 }, 11, 7, "#6a5240", "#2b1e14", "#5a4232"); Blob(x, 9, 9, 5, 3, "#8a6a4a", K.outl); Ell(x, 9, 11, 4, 1, "#e8dcc0"); R(x, 13, 6, 3, 4, "#e8dcc0"); Blob(x, 16, 5, 2, 2, "#8a6a4a", K.outl); Eye(x, 16, 4); P(x, 18, 6, "#2b2222");
            foreach (var (a, b) in new[] { (14, 3), (14, 2), (13, 1), (15, 1), (16, 0), (17, 2), (18, 1), (18, 3), (19, 2) }) P(x, a, b, "#c9a87a"); P(x, 3, 8, "#e8dcc0"); }),
        ["gazelle"] = (18, 15, (x, f) => { Hooves(x, f, new[] { 5, 7, 11, 12 }, 9, 6, "#a07444", "#3a2516", "#8a5a33"); Blob(x, 8, 7, 5, 2, "#d8a868", K.outl); Ell(x, 8, 9, 4, 0, "#f6ecd8"); R(x, 4, 8, 9, 1, "#6b4220"); R(x, 3, 6, 2, 3, "#ffffff");
            R(x, 12, 3, 2, 4, "#d8a868"); Blob(x, 14, 2, 2, 1, "#d8a868", K.outl); Eye(x, 14, 1); P(x, 13, 0, "#3a2516"); P(x, 12, 0, "#3a2516"); }),
        ["puppy"] = (13, 11, (x, f) => { Hooves(x, f, new[] { 3, 4, 8, 9 }, 7, 4, "#2d2629", "#1a1618", "#3a3235"); Blob(x, 6, 5, 4, 2, "#2d2629", K.outl); Ell(x, 6, 4, 2, 0, "#4a4044"); Blob(x, 10, 3, 2, 2, "#2d2629", K.outl); P(x, 11, 2, "#c98a4a"); P(x, 9, 2, "#c98a4a"); P(x, 12, 4, "#c98a4a"); Eye(x, 11, 3); P(x, 9, 0, "#2d2629"); P(x, 1, 3, "#2d2629"); P(x, 1, 2, "#2d2629"); }),
        ["kitten"] = (12, 10, (x, f) => { Hooves(x, f, new[] { 3, 4, 7, 8 }, 6, 4, "#f39b2c", "#c46d12", "#e08a20"); Blob(x, 5, 4, 3, 2, "#f39b2c", K.outl); P(x, 4, 3, "#c46d12"); P(x, 6, 3, "#c46d12"); Blob(x, 9, 3, 2, 2, "#f39b2c", K.outl); P(x, 8, 0, "#f39b2c"); P(x, 10, 0, "#f39b2c"); Eye(x, 10, 3); P(x, 1, 2, "#f39b2c"); P(x, 1, 1, "#c46d12"); }),
        ["swan"] = (14, 12, (x, f) => { Blob(x, 6, 8, 5, 3, "#ffffff", "#8a8a94"); Ell(x, 5, 7, 3, 1, "#f2f2f6"); R(x, 10, 2, 1, 6, "#ffffff"); P(x, 9, 2, "#8a8a94"); R(x, 10, 1, 3, 2, "#ffffff"); P(x, 13, 2, "#f39b2c"); P(x, 11, 1, K.eye); if (f != 0) P(x, 2, 6, "#ffffff"); }),
        ["gull"] = (12, 9, (x, f) => { Blob(x, 6, 5, 4, 2, "#f2f2f6", "#77778a"); R(x, 2 - (f != 0 ? 1 : 0), 3, 4, 1, "#a5a5b0"); R(x, 9, 3, 2, 2, "#f2f2f6"); P(x, 11, 4, "#f7c646"); P(x, 10, 3, K.eye); R(x, 5, 7, 1, 2, "#f39b2c"); R(x, 7, 7, 1, 2, "#f39b2c"); }),
        ["bear"] = (22, 16, (x, f) => { // the Gobi bear (mazaalai), one of the rarest bears on Earth
            Hooves(x, f, new[] { 5, 8, 14, 17 }, 11, 5, "#6b4a2a", "#2b1e14", "#5a3a22"); Blob(x, 11, 8, 8, 4, "#8a6a4a", K.outl); Ell(x, 10, 6, 6, 1, "#a88a62"); Blob(x, 18, 6, 3, 3, "#8a6a4a", K.outl); P(x, 17, 3, "#6b4a2a"); P(x, 20, 3, "#6b4a2a"); R(x, 20, 7, 2, 2, "#c9a87a"); P(x, 21, 7, K.outl); Eye(x, 19, 5); }),
    };
    static readonly Dictionary<string, string> PET_ART = new Dictionary<string, string> { ["camel"] = "botgo" }; // pets and village animals use the same drawings
    /** Animal by pet name or beast kind (a pet camel is the baby camel). */
    public static PxCanvas Animal(string kind, int f) => Beast(PET_ART.TryGetValue(kind, out var k) ? k : kind, f);
    static PxCanvas MoreBeast(string kind, int f)
    {
        if (kind == "horse") return Horse("bay", Math.Min(4, f), true);
        if (kind == "takhi") return Horse("dun", Math.Min(4, f), false);
        if (!MORE.TryGetValue(kind, out var m)) return null;
        var x = new PxCanvas(m.w, m.h); m.draw(x, f); return x;
    }

    // ------------------------------------------------------------ race checkpoint flag (10×16)
    public static PxCanvas RaceFlag(string col, int frame) => Cached("rf:" + col + frame, () =>
    {
        var x = new PxCanvas(10, 16); R(x, 1, 1, 1, 15, K.woodD); R(x, 0, 15, 3, 1, K.outl);
        for (int r = 0; r < 5; r++) R(x, 2, 1 + r, 7 - Math.Abs(r - 2) - (frame != 0 && r % 2 != 0 ? 1 : 0), 1, col);
        Blob(x, 1, 1, 1, 1, K.gold, K.goldD); return x;
    });

    /** Basar's face for the chat window (24×22). mood: 0 happy, 1 talking, 2 thinking. */
    public static PxCanvas BankharFace(int mood) => Cached("bkf:" + mood, () =>
    {
        var x = new PxCanvas(24, 22);
        Blob(x, 12, 12, 8, 7, BKcoat, BKout); Ell(x, 12, 9, 5, 3, BKcoatL);
        foreach (var (ex, i) in new[] { (2, 0), (18, 1) }) { R(x, ex + (i != 0 ? 0 : 1), 6, 3, 9, BKout); R(x, ex + (i != 0 ? 1 : 0), 8, 3, 8, BKout); R(x, ex + 1, 7, 2, 8, i != 0 ? BKcoatL : BKcoat); P(x, ex + (i != 0 ? 1 : 2), 15, BKout); }
        R(x, 8, 8, 2, 1, BKtanL); R(x, 14, 8, 2, 1, BKtanL);
        if (mood == 2) { R(x, 8, 10, 2, 1, BKout); R(x, 14, 10, 2, 1, BKout); }
        else { R(x, 8, 10, 2, 2, BKout); R(x, 14, 10, 2, 2, BKout); P(x, 8, 10, "#fff"); P(x, 14, 10, "#fff"); }
        Blob(x, 12, 15, 4, 3, BKtan, BKout); R(x, 11, 13, 3, 2, BKout);
        if (mood == 1) { R(x, 10, 17, 5, 2, BKout); R(x, 11, 18, 3, 1, BKtongue); }
        else { P(x, 10, 17, BKout); P(x, 14, 17, BKout); R(x, 11, 17, 3, 1, BKout); }
        if (mood == 0) R(x, 12, 18, 2, 2, BKtongue);
        return x;
    });

    // ------------------------------------------------------------ the player's ger (48×50) and the room inside it
    public static readonly Dictionary<string, string> ROOF = new Dictionary<string, string> { ["blue"] = "#3f76db", ["red"] = "#e14b3d", ["green"] = "#2ea35f", ["purple"] = "#9466e0", ["orange"] = "#f39b2c", ["teal"] = "#1fa3a3" };
    public static readonly Dictionary<string, string> DOOR = new Dictionary<string, string> { ["red"] = "#e14b3d", ["blue"] = "#3f76db", ["green"] = "#2ea35f", ["gold"] = "#e9a91f" };
    public static PxCanvas GerSprite(string roof, string door) => Cached("ger:" + roof + door, () =>
    {
        var big = new PxCanvas(48, 52); Ger(big, new WObj { x = 0, y = 0, id = "own" }, roof, door);
        var x = new PxCanvas(48, 50); x.Blit(big, 0, 2); return x; // translate(0, 2) in the web version
    });

    public const int ROOM_W = 320, ROOM_H = 210, ROOM_CX = 160;
    public sealed class Prop { public int x, y, b; public PxCanvas c; }
    public sealed class Room { public PxCanvas bg; public List<Prop> props = new List<Prop>(); public Func<float, float, bool> free; }
    /** Inside a ger: 320×210, standing at the south door looking north to the khoimor. West (left) is the men's side, east the women's. */
    public static Room GerRoom(string roof, string door, List<string> furn)
    {
        const int W = ROOM_W, H = ROOM_H, cx = ROOM_CX, fy = 142, frx = 146, fry = 56;
        bool Has(string id) => furn != null && furn.Contains(id);
        const string ORG = "#d9762b", ORGD = "#9a4a1a", ORGL = "#f3a457", BLU = "#2b52a5", BLUL = "#6a92de", GLD = "#f7c646", OUT = "#2a1a10";
        var x = new PxCanvas(W, H);
        double FloorTop(double X) => fy - fry * Math.Sqrt(Math.Max(0, 1 - Math.Pow((X - cx) / frx, 2)));
        double WallTop(double X) => FloorTop(X) - 44;
        const int TY = 16, TRX = 24, TRY = 9; double EDGE = WallTop(cx - frx);
        R(x, 0, 0, W, H, "#1e140c");
        // ceiling: felt lining seen between the roof poles
        for (int X = cx - frx; X <= cx + frx; X++)
        {
            int a = Math.Abs(X - cx); double top = a <= TRX ? TY : TY + ((a - TRX) / (double)(frx - TRX)) * (EDGE - TY), f = a / (double)frx;
            R(x, X, Js.Round(top), 1, Js.Round(WallTop(X) - top) + 1, f < 0.35 ? "#efe4cc" : f < 0.65 ? "#e3d5b8" : f < 0.85 ? "#d3c2a0" : "#bba987");
        }
        // уни: the roof poles, painted orange, running from the toono down to the head of the wall
        for (int j = 0; j < 38; j++)
        {
            int ex = Js.Round(cx - frx + (j * 2.0 * frx) / 37), ey = Js.Round(WallTop(ex)), sx = Js.Round(cx + ((ex - cx) / (double)frx) * (TRX - 3)), sy = TY + 4;
            Line(x, sx, sy, ex, ey, ORG); Line(x, sx + 1, sy, ex + 1, ey, ORGD);
            int bx = Js.Round(sx + (ex - sx) * 0.86), by = Js.Round(sy + (ey - sy) * 0.86); P(x, bx, by, BLU); P(x, bx + 1, by, GLD);
        }
        // sunlight falling through the toono
        x.FillPoly(new[] { new Vector2(cx - 14, TY + 3), new Vector2(cx + 12, TY + 3), new Vector2(cx - 4, fy + 30), new Vector2(cx - 46, fy + 30) }, Col("rgba(255,238,190,.10)"));
        // тооно: the crown, with sky and a cloud showing through and its bent spokes
        Ell(x, cx, TY, TRX + 1, TRY + 1, OUT); Ell(x, cx, TY, TRX, TRY, ORG); Ell(x, cx, TY + 1, TRX, TRY - 1, ORGD); Ell(x, cx, TY, TRX - 4, TRY - 3, "#8fd0ff");
        R(x, cx - 9, TY - 3, 7, 2, "#ffffff"); R(x, cx - 7, TY - 4, 3, 1, "#ffffff");
        R(x, cx - TRX + 4, TY - 1, 2 * TRX - 8, 2, ORG); R(x, cx - 1, TY - TRY + 3, 2, 2 * TRY - 6, ORG);
        foreach (int s in new[] { -1, 1 }) { Line(x, cx + s * 11, TY - 5, cx + s * 8, TY - 1, ORG); Line(x, cx + s * 8, TY + 1, cx + s * 11, TY + 5, ORG); }
        // хана: the lattice wall behind, over its felt
        for (int X = cx - frx; X <= cx + frx; X++)
        {
            int top = Js.Round(WallTop(X)), bot = Js.Round(FloorTop(X)); double f = Math.Abs(X - cx) / (double)frx; string felt = f < 0.5 ? "#efe1c3" : f < 0.8 ? "#e2d0ad" : "#cdb994";
            for (int y = top; y <= bot; y++) { int u = (X + y) % 7, v = ((X - y) % 7 + 7) % 7; P(x, X, y, u == 0 || v == 0 ? (f > 0.75 ? ORGD : ORG) : felt); }
            R(x, X, top, 1, 2, ORGD); R(x, X, bot - 1, 1, 2, "#5a3a22");
        }
        // wall carpet behind the khoimor
        for (int X = 112; X <= 208; X++)
        {
            int top = Js.Round(WallTop(X)) + 6, bot = Js.Round(FloorTop(X)) - 4; bool edge = X < 115 || X > 205;
            R(x, X, top, 1, bot - top, edge ? GLD : "#a8312a"); R(x, X, top, 1, 2, GLD); R(x, X, bot - 2, 1, 2, GLD);
            if (!edge && (X - 112) % 8 < 2) { P(x, X, top + 4, BLUL); P(x, X, bot - 5, BLUL); }
        }
        for (int r = 0; r < 8; r++) { R(x, cx - r, 56 + r, r * 2 + 1, 1, r < 2 ? GLD : BLU); R(x, cx - r, 71 - r, r * 2 + 1, 1, r < 2 ? GLD : BLU); }
        // floor: wooden boards
        Ell(x, cx, fy, frx + 1, fry + 1, OUT);
        for (int dy = -fry; dy <= fry; dy++)
        {
            int hw = Js.Round(frx * Math.Sqrt(Math.Max(0, 1 - (dy * dy) / ((fry + 0.5) * (fry + 0.5))))), y = fy + dy;
            R(x, cx - hw, y, hw * 2 + 1, 1, y % 6 == 0 ? "#9a6c3e" : "#b98a55");
            for (int i = 0; i < 6; i++) { int gx = cx - hw + (int)Math.Floor(Hash(i, y) * hw * 2); P(x, gx, y, y % 6 == 3 ? "#c99a62" : "#a87a48"); }
        }
        for (int X = cx - frx; X <= cx + frx; X++) { int y = Js.Round(fy + fry * Math.Sqrt(Math.Max(0, 1 - Math.Pow((X - cx) / (double)frx, 2)))); if (Math.Abs(X - cx) > 16) R(x, X, y - 2, 1, 4, OUT); }
        // the big carpet in front of the stove
        R(x, 92, 158, 136, 32, OUT); R(x, 93, 159, 134, 30, "#b8322a"); R(x, 95, 161, 130, 26, GLD); R(x, 97, 163, 126, 22, "#b8322a"); R(x, 99, 165, 122, 18, BLU); R(x, 100, 166, 120, 16, "#b8322a");
        for (int i = 0; i < 5; i++) { int mx = 112 + i * 24; for (int r = 0; r < 5; r++) { R(x, mx - r, 169 + r, r * 2 + 1, 1, r % 2 != 0 ? GLD : BLUL); R(x, mx - r, 179 - r, r * 2 + 1, 1, r % 2 != 0 ? GLD : BLUL); } }
        for (int i = 0; i < 33; i++) { P(x, 94 + i * 4, 189, "#f6dca8"); P(x, 94 + i * 4, 157, "#f6dca8"); }
        if (Has("shirdeg")) // quilted felt rug with stitched хээ
        {
            R(x, 66, 136, 58, 22, OUT); R(x, 67, 137, 56, 20, "#ecdcb8"); R(x, 69, 139, 52, 16, "#7a4a2a"); R(x, 70, 140, 50, 14, "#ecdcb8");
            for (int i = 0; i < 4; i++) { int sx = 76 + i * 12; R(x, sx, 143, 6, 1, "#7a4a2a"); R(x, sx + 5, 143, 1, 5, "#7a4a2a"); R(x, sx + 2, 147, 4, 1, "#7a4a2a"); R(x, sx + 2, 145, 1, 3, "#7a4a2a"); }
        }
        Ell(x, cx - 26, fy + 24, 18, 5, "rgba(255,238,190,.16)"); // sun spot on the floor
        if (Has("shagai")) { var sp = new[] { (206, 164), (212, 166), (218, 163), (224, 166) }; for (int i = 0; i < 4; i++) { R(x, sp[i].Item1, sp[i].Item2, 4, 3, OUT); R(x, sp[i].Item1, sp[i].Item2, 3, 2, i % 2 != 0 ? "#f6ecd8" : "#e2cfa8"); } }
        // the door to the south, open to the bright steppe
        int dT = fy + fry - 8;
        R(x, cx - 17, dT - 3, 34, H - dT + 3, OUT); R(x, cx - 16, dT - 2, 32, H - dT + 2, ORGD); R(x, cx - 14, dT, 28, H - dT, "#fff2c8"); R(x, cx - 14, H - 7, 28, 7, "#94d260"); R(x, cx - 14, H - 9, 28, 2, "#c5ec9a");
        for (int i = -14; i <= 13; i += 3) P(x, cx + i, dT - 2, GLD);
        R(x, cx - 16, H - 3, 32, 3, "#6b4220"); R(x, cx - 16, H - 3, 32, 1, "#a46d3b");

        // ---- props: standing things, depth-sorted with the people by their base line (y)
        var room = new Room { bg = x }; var blocks = new List<int[]>();
        void Prop(int x0, int y0, int w, int h2, int b, Action<PxCanvas, int, int> draw, int[] block) { var q = new PxCanvas(w, h2); draw(q, w, h2); room.props.Add(new Prop { x = x0, y = y0, b = b, c = q }); if (block != null) blocks.Add(block); }
        // two багана posts holding up the toono, painted with bands of ornament
        foreach (int px in new[] { 149, 171 }) Prop(px - 3, TY + 6, 7, 136 - TY - 6, 136, (q, w, hh) =>
        {
            R(q, 1, 0, 5, hh, OUT); R(q, 2, 0, 3, hh, ORG); R(q, 2, 0, 1, hh, ORGL); R(q, 4, 0, 1, hh, ORGD);
            R(q, 0, 0, 7, 4, OUT); R(q, 1, 1, 5, 2, GLD);
            for (int y = 12; y < hh - 8; y += 18) { R(q, 2, y, 3, 2, BLU); P(q, 3, y + 2, GLD); R(q, 2, y + 3, 3, 1, "#a8312a"); }
            R(q, 0, hh - 3, 7, 3, OUT); R(q, 1, hh - 3, 5, 2, "#6b4220");
        }, null);
        if (Has("tulga")) Prop(cx - 15, TY + 8, 30, 150 - TY - 8, 150, (q, w, hh) => // iron stove with the tea pot, pipe up through the toono
        {
            int sb = hh - 16;
            R(q, 13, 0, 5, sb - 6, OUT); R(q, 14, 0, 3, sb - 6, "#4a4a55"); R(q, 14, 0, 1, sb - 6, "#7a7a88");
            for (int y = 10; y < sb - 6; y += 22) R(q, 13, y, 5, 1, "#2f2f38");
            Ell(q, 15, sb - 3, 9, 4, OUT); Ell(q, 15, sb - 3, 8, 3, "#8a8a99"); Ell(q, 14, sb - 5, 6, 1, "#b4b4c2"); R(q, 6, sb - 3, 18, 1, "#5d5d6b");
            R(q, 2, sb, 26, 12, OUT); R(q, 3, sb + 1, 24, 10, "#3a3a44"); R(q, 3, sb + 1, 24, 2, "#5d5d6b");
            R(q, 9, sb + 4, 12, 6, OUT); R(q, 10, sb + 5, 10, 4, "#f39b2c"); R(q, 12, sb + 6, 6, 2, "#f7c646");
            R(q, 4, hh - 4, 3, 4, OUT); R(q, 23, hh - 4, 3, 4, OUT);
        }, new[] { 146, 134, 174, 150 });
        else Prop(cx - 15, 136, 30, 14, 150, (q, w, hh) => { Ell(q, 15, 8, 12, 4, "#77778a"); Ell(q, 15, 8, 9, 3, "#4a3e3e"); Ell(q, 15, 8, 5, 1, "#7a6a5a"); }, new[] { 148, 138, 172, 150 });
        // low table with aaruul, boortsog and a cup of milk tea
        Prop(cx - 18, 104, 36, 18, 120, (q, w, hh) =>
        {
            R(q, 1, 3, 34, 8, OUT); R(q, 2, 4, 32, 6, ORG); R(q, 2, 4, 32, 1, ORGL); R(q, 5, 6, 26, 2, BLU); for (int i = 7; i < 30; i += 5) P(q, i, 7, GLD);
            R(q, 3, 11, 2, 6, OUT); R(q, 31, 11, 2, 6, OUT);
            Ell(q, 9, 2, 4, 1, "#ffffff"); P(q, 8, 1, "#f3e6c4"); P(q, 10, 1, "#f3e6c4");
            Ell(q, 19, 2, 4, 1, "#d8b47e"); P(q, 18, 1, "#a46d3b"); P(q, 20, 1, "#a46d3b");
            R(q, 27, 0, 3, 3, "#ffffff"); P(q, 28, 0, "#e8d6b0");
        }, new[] { 142, 110, 178, 121 });
        // хоймор: the place of honour, with the family shrine and photos
        Prop(118, 62, 84, 40, 100, (q, w, hh) =>
        {
            if (Has("avdar")) foreach (int ax in new[] { 0, 44 })
                {
                    R(q, ax, 22, 40, 18, OUT); R(q, ax + 1, 23, 38, 16, ORG); R(q, ax + 1, 23, 38, 2, ORGL);
                    foreach (int px2 in new[] { 4, 22 }) { R(q, ax + px2, 27, 14, 9, BLU); R(q, ax + px2 + 1, 28, 12, 7, BLUL); R(q, ax + px2 + 5, 30, 4, 3, GLD); }
                }
            else { R(q, 14, 26, 56, 14, OUT); R(q, 15, 27, 54, 12, ORG); R(q, 15, 27, 54, 1, ORGL); R(q, 20, 30, 44, 6, BLU); }
            R(q, 26, 6, 32, 17, OUT); R(q, 27, 7, 30, 15, ORGD); R(q, 28, 8, 28, 13, "#a8312a");
            R(q, 30, 10, 7, 9, GLD); R(q, 31, 11, 5, 7, "#fff2c8"); R(q, 33, 13, 1, 3, "#c98f22");
            R(q, 39, 11, 6, 8, "#ffffff"); R(q, 40, 12, 4, 4, "#9fd8f7"); R(q, 40, 16, 4, 2, "#76b947");
            R(q, 47, 10, 7, 9, GLD); R(q, 48, 11, 5, 7, "#f6dca8"); P(q, 50, 13, "#2b1e2e");
            R(q, 20, 18, 3, 4, GLD); P(q, 21, 16, "#ffd34e"); P(q, 21, 17, "#f39b2c");
            R(q, 61, 18, 3, 4, GLD); P(q, 62, 16, "#ffd34e"); P(q, 62, 17, "#f39b2c");
            R(q, 34, 3, 16, 4, "#ffffff"); R(q, 34, 3, 16, 1, "#dce8f5");
        }, new[] { 120, 86, 200, 101 });
        if (Has("morinkhuur")) Prop(100, 60, 12, 44, 104, (q, w, hh) =>
        {
            R(q, 4, 0, 4, 5, OUT); R(q, 5, 1, 3, 3, "#6b4220"); P(q, 7, 2, "#2b1e2e"); P(q, 3, 3, OUT);
            R(q, 5, 5, 2, 22, OUT); R(q, 5, 5, 1, 22, "#8a5a33");
            R(q, 1, 27, 10, 14, OUT); R(q, 2, 28, 8, 12, "#a46d3b"); R(q, 2, 28, 8, 1, "#c99257"); R(q, 5, 28, 2, 12, "#f6dca8");
        }, null);
        if (Has("or")) foreach (var (bx, blanket) in new[] { (22, BLU), (248, "#c2427e") }) Prop(bx, 104, 50, 32, 134, (q, w, hh) =>
            {
                R(q, 0, 0, 50, 12, OUT); R(q, 1, 1, 48, 10, ORG); R(q, 4, 3, 42, 6, BLU); for (int i = 7; i < 44; i += 6) P(q, i, 5, GLD);
                R(q, 0, 12, 50, 18, OUT); R(q, 1, 12, 48, 16, "#f2eee4"); R(q, 1, 17, 48, 11, blanket); R(q, 1, 17, 48, 1, Shade(blanket, 0.3)); R(q, 4, 13, 12, 4, "#ffffff");
                R(q, 0, 28, 50, 4, OUT); R(q, 1, 28, 48, 2, ORGD);
            }, new[] { bx, 114, bx + 50, 135 });
        Prop(52, 158, 30, 22, 180, (q, w, hh) => // эмээл: a saddle on its stand (men's side)
        {
            R(q, 2, 12, 26, 3, OUT); R(q, 3, 12, 24, 2, "#8a5a33"); R(q, 4, 15, 2, 7, OUT); R(q, 24, 15, 2, 7, OUT);
            R(q, 6, 6, 18, 7, OUT); R(q, 7, 7, 16, 5, "#a8312a"); R(q, 4, 2, 5, 9, OUT); R(q, 5, 3, 3, 7, ORG); R(q, 21, 2, 5, 9, OUT); R(q, 22, 3, 3, 7, ORG);
            P(q, 6, 4, "#d3d3dc"); P(q, 23, 4, "#d3d3dc"); P(q, 10, 9, "#d3d3dc"); P(q, 15, 9, "#d3d3dc"); P(q, 20, 9, "#d3d3dc");
            R(q, 9, 13, 1, 5, "#5a3a22"); R(q, 7, 18, 5, 2, "#77778a"); R(q, 20, 13, 1, 5, "#5a3a22"); R(q, 18, 18, 5, 2, "#77778a");
        }, new[] { 54, 170, 80, 181 });
        Prop(32, 128, 22, 32, 160, (q, w, hh) => // хөхүүр: the leather airag bag with its churn stick
        {
            R(q, 10, 0, 2, 14, "#c99257"); R(q, 9, 0, 4, 2, "#a46d3b");
            Blob(q, 11, 18, 8, 9, "#6b4220", OUT); Ell(q, 8, 15, 3, 4, "#8a5a33"); R(q, 4, 27, 14, 3, OUT); R(q, 5, 27, 12, 2, "#a46d3b");
        }, new[] { 34, 150, 52, 161 });
        Prop(248, 140, 32, 36, 176, (q, w, hh) => // painted cupboard with the tea things (women's side)
        {
            R(q, 0, 8, 32, 28, OUT); R(q, 1, 9, 30, 26, ORG); R(q, 1, 9, 30, 2, ORGL);
            foreach (int dx2 in new[] { 3, 17 }) { R(q, dx2, 13, 12, 18, BLU); R(q, dx2 + 1, 14, 10, 16, BLUL); R(q, dx2 + 4, 19, 4, 5, GLD); P(q, dx2 + 5, 21, "#a8312a"); }
            R(q, 3, 1, 5, 8, "#c2302a"); R(q, 3, 1, 5, 1, "#ffffff");
            R(q, 12, 3, 9, 6, "#d6a23a"); R(q, 20, 4, 3, 2, "#d6a23a"); P(q, 16, 2, "#f7d071");
            Ell(q, 27, 7, 3, 1, "#ffffff");
        }, new[] { 248, 162, 280, 177 });
        Prop(216, 172, 20, 22, 194, (q, w, hh) => // water barrel and ladle
        {
            R(q, 2, 4, 16, 18, OUT); R(q, 3, 5, 14, 16, "#a46d3b"); R(q, 3, 5, 3, 16, "#c99257"); R(q, 3, 8, 14, 1, "#77778a"); R(q, 3, 17, 14, 1, "#77778a");
            Ell(q, 10, 4, 7, 2, OUT); Ell(q, 10, 4, 6, 1, "#4aa6e2"); R(q, 12, 0, 1, 5, "#8a5a33"); R(q, 10, 0, 4, 1, "#8a5a33");
        }, new[] { 218, 184, 234, 195 });
        bool InFloor(float px, float py) => Math.Pow((px - cx) / (frx - 8.0), 2) + Math.Pow((py - fy - 1) / (fry - 5.0), 2) <= 1 || (Math.Abs(px - cx) < 10 && py > fy && py < H - 6);
        room.free = (px, py) => InFloor(px, py) && !blocks.Exists(bb => px > bb[0] && px < bb[2] && py > bb[1] && py < bb[3]);
        return room;
    }
    /** The ger interior as one picture (for the decorate panel). */
    public static PxCanvas Interior(string roof, List<string> furn)
    {
        var rm = GerRoom(roof, null, furn); var x = new PxCanvas(ROOM_W, ROOM_H); x.Blit(rm.bg, 0, 0);
        rm.props.Sort((a, b) => a.b.CompareTo(b.b)); foreach (var p in rm.props) x.Blit(p.c, p.x, p.y);
        return x;
    }

    // ------------------------------------------------------------ wrestlers
    /** Mongolian wrestler (32×48, facing right). pose: idle | grip | push | dance | fall (48×32, lying on the back). */
    public static PxCanvas Wrestler(string col, string skin, string pose, int frame) => Cached("w2:" + col + skin + pose + frame, () =>
    {
        if (pose == "fall")
        {
            var s = Wrestler(col, skin, "lie", 0); var m = new PxCanvas(48, 32);
            for (int sy = 0; sy < 48; sy++) for (int sx = 0; sx < 32; sx++) m.Put(sy, 31 - sx, s.Get(sx, sy)); // rotated a quarter turn back
            return m;
        }
        var x = new PxCanvas(32, 48);
        string D = Shade(col, -0.32), Lt = Shade(col, 0.22), sk = skin, skD = Shade(skin, -0.16), skL = Shade(skin, 0.14); const string leather = "#5a3420", leatherD = "#3a2012";
        int lean = pose == "push" ? 3 : pose == "grip" ? 1 : 0; bool dance = pose == "dance", lift = dance && frame == 1;
        void Boot(int bx, int top)
        {
            R(x, bx, top, 8, 46 - top, K.outl); R(x, bx + 1, top + 1, 6, 44 - top, leather); R(x, bx + 6, top + 1, 1, 44 - top, leatherD);
            R(x, bx + 1, top + 1, 6, 1, K.gold); R(x, bx + 1, top + 2, 6, 1, K.red); R(x, bx + 3, top + 3, 2, 42 - top, K.goldD); P(x, bx + 3, top + 5, K.red); P(x, bx + 4, top + 8, K.red);
            R(x, bx + 6, 42, 3, 3, K.outl); R(x, bx + 6, 43, 2, 1, leather); P(x, bx + 9, 41, K.outl); P(x, bx + 9, 40, K.outl); P(x, bx + 8, 42, leather); P(x, bx + 8, 41, K.outl);
            R(x, bx, 45, 9, 2, "#24120a"); P(x, bx + 1, 45, "#4a2a18");
        }
        int backTop = 36, frontTop = lift ? 33 : 36;
        R(x, 8, 28, 7, backTop - 28 + 1, K.outl); R(x, 9, 28, 5, backTop - 28, sk); R(x, 13, 29, 1, backTop - 30, skD); P(x, 10, 31, skL); P(x, 10, 34, skD); P(x, 11, 34, skD);
        if (lift) { R(x, 16, 28, 8, 6, K.outl); R(x, 17, 28, 6, 5, sk); R(x, 17, 32, 6, 1, skD); P(x, 18, 29, skL); }
        else { R(x, 16, 28, 7, frontTop - 28 + 1, K.outl); R(x, 17, 28, 5, frontTop - 28, sk); R(x, 21, 29, 1, frontTop - 30, skD); P(x, 18, 31, skL); P(x, 18, 34, skD); P(x, 19, 34, skD); }
        Boot(7, backTop); Boot(15, frontTop);
        R(x, 8, 25, 16, 1, K.outl); R(x, 8, 26, 16, 1, K.outl); R(x, 9, 26, 14, 1, D);
        R(x, 8, 27, 1, 1, K.outl); R(x, 23, 27, 1, 1, K.outl); R(x, 9, 27, 14, 1, col); P(x, 11, 27, Lt);
        R(x, 9, 28, 2, 1, K.outl); R(x, 21, 28, 2, 1, K.outl); R(x, 11, 28, 10, 1, col); P(x, 15, 28, D);
        R(x, 11, 29, 10, 1, K.outl); R(x, 13, 29, 6, 1, col); R(x, 13, 30, 6, 1, K.outl);
        void Arm(int[][] pts, int fx, int fy)
        {
            foreach (var p in pts) R(x, p[0] - 1, p[1] - 1, p[2] + 2, p[3] + 2, K.outl);
            foreach (var p in pts) { R(x, p[0], p[1], p[2], p[3], col); R(x, p[0], p[1] + p[3] - 1, p[2], 1, D); }
            R(x, fx - 1, fy - 1, 4, 4, K.outl); R(x, fx, fy, 2, 2, sk); R(x, fx - 1, fy, 1, 2, K.gold);
        }
        if (dance) { if (frame != 0) Arm(new[] { new[] { 1, 17, 9, 3 } }, 0, 17); else Arm(new[] { new[] { 1, 13, 4, 3 }, new[] { 5, 14, 5, 3 } }, 0, 12); }
        else if (pose == "idle" || pose == "lie") Arm(new[] { new[] { 9, 16, 3, 8 } }, 10, 24);
        else Arm(new[] { new[] { 11 + lean, 19, 15, 3 } }, 26 + lean, 19);
        R(x, 6 + lean, 14, 7, 12, K.outl); R(x, 7 + lean, 15, 5, 10, col); R(x, 7 + lean, 15, 1, 10, D); R(x, 7 + lean, 24, 5, 1, K.gold); R(x, 11 + lean, 16, 1, 9, K.gold);
        R(x, 11 + lean, 15, 12, 11, K.outl); R(x, 12 + lean, 15, 10, 10, sk);
        R(x, 13 + lean, 19, 3, 1, skD); R(x, 17 + lean, 19, 3, 1, skD); P(x, 16 + lean, 18, skD);
        R(x, 16 + lean, 21, 1, 4, skD); P(x, 14 + lean, 22, skD); P(x, 18 + lean, 22, skD); P(x, 16 + lean, 25, skD);
        P(x, 14 + lean, 16, skL); P(x, 13 + lean, 17, skL); R(x, 21 + lean, 16, 1, 8, skD);
        R(x, 7 + lean, 12, 17, 4, K.outl); R(x, 8 + lean, 13, 15, 2, col); R(x, 8 + lean, 15, 15, 1, D); R(x, 8 + lean, 12, 15, 1, Lt);
        for (int i = 0; i < 9; i++) { int sx = 20 + lean - Js.Round(i * 0.35); R(x, sx, 15 + i, 2, 1, col); P(x, sx - 1, 15 + i, K.gold); }
        R(x, 12 + lean, 23, 10, 1, D); P(x, 18 + lean, 23, K.gold); P(x, 18 + lean, 24, D); P(x, 19 + lean, 25, D);
        if (dance) { if (frame != 0) Arm(new[] { new[] { 21 + lean, 17, 10, 3 } }, 30, 17); else Arm(new[] { new[] { 21, 14, 5, 3 }, new[] { 26, 13, 5, 3 } }, 30, 12); }
        else if (pose == "idle" || pose == "lie") Arm(new[] { new[] { 20, 16, 3, 9 } }, 21, 25);
        else Arm(new[] { new[] { 20 + lean, 16, 9, 3 }, new[] { 27 + lean, 16, 2, 3 } }, 29, 16);
        int hx = 16 + lean, hy = 9;
        Blob(x, hx, hy, 4, 4, sk);
        R(x, hx - 4, hy - 4, 8, 3, K.hair); R(x, hx - 4, hy - 2, 3, 4, K.hair); P(x, hx - 1, hy - 4, K.hairL);
        P(x, hx - 1, hy + 1, skD); P(x, hx + 2, hy, K.eye); R(x, hx + 1, hy - 1, 3, 1, K.hair); P(x, hx + 3, hy + 3, skD); P(x, hx + 4, hy + 1, skD);
        if (dance)
        {
            R(x, hx - 6, hy - 4, 13, 2, K.outl); R(x, hx - 5, hy - 4, 11, 1, "#24307a"); P(x, hx - 6, hy - 5, "#24307a"); P(x, hx + 6, hy - 5, "#24307a");
            for (int r = 0; r < 4; r++) { int w = 4 - r; R(x, hx - w - 1, hy - 5 - r, w * 2 + 3, 1, K.outl); R(x, hx - w, hy - 5 - r, w * 2 + 1, 1, r == 3 ? "#e35a4c" : K.red); }
            R(x, hx - 1, hy - 10, 2, 2, K.gold); P(x, hx, hy - 11, K.goldD);
        }
        return x;
    });

    // ------------------------------------------------------------ UI icons (16×16) for the shop, bag, HUD
    static readonly string[] ICON7_HEART = ICON_HEART;
    public static PxCanvas Icon(string key) => Cached("ic:" + key, () =>
    {
        var x = new PxCanvas(16, 16); int c = key.IndexOf(':'); string kind = key.Substring(0, c), id = key.Substring(c + 1);
        void Fit(PxCanvas a) { double k = Math.Min(16.0 / a.W, 16.0 / a.H); x.BlitScaled(a, Js.Round((16 - a.W * k) / 2), Js.Round((16 - a.H * k) / 2), Js.Round(a.W * k), Js.Round(a.H * k)); }
        void Bowl(string fill, Action top) { R(x, 2, 8, 12, 5, K.outl); R(x, 3, 8, 10, 4, "#f6f2e9"); R(x, 3, 11, 10, 1, "#d8cfba"); R(x, 2, 7, 12, 2, K.outl); R(x, 3, 7, 10, 1, fill); top?.Invoke(); R(x, 5, 13, 6, 1, K.outl); }
        switch (kind)
        {
            case "beast": Fit(Beast(id, 0)); break;
            case "dairy":
                switch (id)
                {
                    case "milk": Bowl("#ffffff", () => P(x, 6, 7, "#e8f0ff")); break;
                    case "mare": R(x, 4, 3, 8, 11, K.outl); R(x, 5, 4, 6, 9, "#a46d3b"); R(x, 5, 4, 6, 2, "#ffffff"); R(x, 6, 1, 4, 3, K.outl); R(x, 7, 2, 2, 1, "#6b4220"); P(x, 6, 7, "#c99257"); break;
                    case "aaruul": foreach (var (ax, ay) in new[] { (2, 8), (7, 9), (5, 4), (10, 5) }) { R(x, ax, ay, 5, 4, K.outl); R(x, ax + 1, ay + 1, 3, 2, "#fff6dc"); P(x, ax + 1, ay + 1, "#ffffff"); } break;
                    case "urum": Bowl("#f7d071", () => { R(x, 4, 6, 8, 1, "#f2c45a"); P(x, 6, 6, "#fde08a"); }); break;
                    case "tarag": Bowl("#f6f2e9", () => { P(x, 7, 6, "#ffffff"); P(x, 8, 6, "#ffffff"); }); break;
                    case "airag": Bowl("#efe8d4", () => { P(x, 5, 6, "#ffffff"); P(x, 9, 6, "#ffffff"); P(x, 7, 5, "#ffffff"); }); break;
                }
                break;
            case "fish":
                {
                    var cols = new Dictionary<string, string[]> { ["perch"] = new[] { "#8fbf4a", "#5e8a2a", "#3a5a1a" }, ["pike"] = new[] { "#7a9a5a", "#4e6e36", "#2e4a20" }, ["lenok"] = new[] { "#c49a6a", "#8a6a42", "#5a3a1e" }, ["taimen"] = new[] { "#d9705a", "#a8483a", "#6e2a20" } };
                    var col = cols[id];
                    Ell(x, 7, 8, 6, 3, K.outl); Ell(x, 7, 8, 5, 2, col[0]); R(x, 2, 9, 10, 1, col[1]);
                    for (int r = -3; r <= 3; r++) R(x, 13, 8 + r, 2, 1, col[1]);
                    R(x, 12, 5, 1, 7, K.outl); R(x, 13, 5, 2, 2, col[1]); R(x, 13, 10, 2, 2, col[1]);
                    P(x, 3, 7, K.eye);
                    if (id == "perch") foreach (int sx in new[] { 5, 8, 10 }) R(x, sx, 6, 1, 4, col[2]);
                    if (id == "lenok" || id == "taimen") foreach (var (a, b) in new[] { (5, 7), (8, 6), (9, 9), (6, 9) }) P(x, a, b, col[2]);
                    break;
                }
            case "deel":
                {
                    var dl = DEEL[id]; string D = dl[0], DD = dl[1], DL = dl[2];
                    R(x, 4, 2, 8, 13, K.outl); R(x, 1, 3, 4, 7, K.outl); R(x, 11, 3, 4, 7, K.outl);
                    R(x, 5, 3, 6, 11, D); R(x, 2, 4, 3, 5, D); R(x, 11, 4, 3, 5, DD); R(x, 9, 3, 2, 11, DD);
                    R(x, 5, 9, 6, 1, K.gold); Line(x, 6, 3, 9, 6, DL);
                    break;
                }
            case "wear":
                {
                    var look = new Look(); string slot = id.Split('-')[0], val = id.Split('-')[1];
                    if (slot == "hat") look.hat = val; else if (slot == "face") look.face = val; else if (slot == "neck") look.neck = val;
                    x.Blit(Char(look, 0, 0), 0, look.neck != null ? -10 : -1);
                    break;
                }
            case "pet": { var a = Animal(id, 0); x.Blit(a, Js.Round((16 - a.W) / 2.0), Js.Round((16 - a.H) / 2.0) + 1); break; }
            case "tool": Line(x, 3, 14, 12, 2, K.woodD); Line(x, 4, 14, 13, 2, K.wood); Line(x, 13, 2, 14, 12, "#cfe8ff"); P(x, 14, 12, K.red); P(x, 14, 13, K.red); break;
            case "furn":
                switch (id)
                {
                    case "tulga": R(x, 3, 5, 10, 9, K.outl); R(x, 4, 6, 8, 7, "#5a5a66"); R(x, 5, 9, 6, 3, K.orange); P(x, 7, 10, K.gold); R(x, 7, 1, 2, 5, "#4a4a55"); break;
                    case "shirdeg": R(x, 1, 4, 14, 9, K.outl); R(x, 2, 5, 12, 7, K.red); R(x, 3, 6, 10, 5, K.gold); R(x, 5, 7, 6, 3, K.blue); P(x, 7, 8, K.white); P(x, 8, 8, K.white); break;
                    case "or": R(x, 1, 5, 14, 8, K.outl); R(x, 2, 6, 12, 6, K.wood); R(x, 3, 6, 10, 4, K.white); R(x, 3, 6, 4, 4, K.light); R(x, 1, 12, 2, 3, K.woodD); R(x, 13, 12, 2, 3, K.woodD); break;
                    case "avdar": R(x, 2, 4, 12, 9, K.outl); R(x, 3, 5, 10, 7, K.orange); R(x, 3, 5, 10, 2, K.red); R(x, 4, 8, 3, 3, K.blue); R(x, 9, 8, 3, 3, K.blue); P(x, 8, 7, K.gold); break;
                    case "morinkhuur": R(x, 7, 1, 2, 10, K.wood); R(x, 6, 0, 4, 2, K.woodD); P(x, 9, 1, K.woodD); Blob(x, 8, 12, 3, 3, K.wood); Line(x, 7, 2, 7, 12, K.white); break;
                    case "shagai": Blob(x, 8, 9, 5, 4, K.woodL); R(x, 6, 3, 4, 3, K.woodD); foreach (var (a, b) in new[] { (6, 9), (9, 8), (8, 11) }) R(x, a, b, 2, 1, K.white); break;
                }
                break;
            case "ui": UiIcon(x, id); break;
            case "part": PartIcon(x, id); break;
        }
        return x;
    });
    static void UiIcon(PxCanvas x, string id)
    {
        switch (id)
        {
            case "coin": Blob(x, 8, 8, 5, 5, K.gold, K.goldD); Ell(x, 8, 8, 3, 3, "#f9d76a"); R(x, 7, 5, 3, 1, K.goldD); R(x, 8, 5, 1, 6, K.goldD); R(x, 6, 7, 5, 1, K.goldD); P(x, 6, 5, K.white); break;
            case "drop":
                for (int r = 0; r < 11; r++) { int w = r < 4 ? r : Js.Round(4 * Math.Sqrt(1 - Math.Pow((r - 7) / 4.5, 2))); R(x, 8 - w - 1, 3 + r, w * 2 + 3, 1, K.outl); }
                for (int r = 1; r < 10; r++) { int w = r < 4 ? r - 1 : Js.Round(3 * Math.Sqrt(Math.Max(0, 1 - Math.Pow((r - 7) / 4.0, 2)))); R(x, 8 - w, 3 + r, w * 2 + 1, 1, K.blue); }
                P(x, 6, 9, K.light); P(x, 6, 10, K.light); break;
            case "bag": R(x, 3, 6, 10, 9, K.outl); R(x, 4, 7, 8, 7, K.wood); R(x, 4, 7, 8, 2, K.woodL); R(x, 6, 3, 4, 4, K.outl); R(x, 7, 4, 2, 2, K.wood); R(x, 7, 9, 2, 2, K.gold); break;
            case "chat": R(x, 2, 3, 12, 8, K.outl); R(x, 3, 4, 10, 6, K.white); R(x, 4, 11, 3, 2, K.outl); P(x, 5, 11, K.white); foreach (int d in new[] { 5, 8, 11 }) P(x, d, 7, K.outl); break;
            case "scroll": R(x, 3, 2, 10, 12, K.outl); R(x, 4, 3, 8, 10, K.paper); R(x, 2, 2, 12, 2, K.woodD); R(x, 2, 12, 12, 2, K.woodD); foreach (int ly in new[] { 5, 7, 9 }) R(x, 5, ly, 6, 1, K.woodL); break;
            case "star": foreach (var (a, b) in new[] { (7, 2), (8, 2), (7, 3), (8, 3), (6, 4), (9, 4), (2, 6), (13, 6), (3, 7), (12, 7), (5, 8), (10, 8), (4, 11), (11, 11), (3, 13), (12, 13) }) P(x, a, b, K.goldD); Ell(x, 7, 8, 4, 4, K.gold); P(x, 7, 6, K.white); break;
            case "house": for (int r = 0; r < 6; r++) R(x, 8 - r - 1, 2 + r, r * 2 + 2, 1, K.red); R(x, 3, 8, 10, 6, K.outl); R(x, 4, 8, 8, 5, K.felt); R(x, 7, 10, 2, 3, K.woodD); break;
            case "shop": for (int r = 0; r < 5; r++) R(x, (int)(8 - r * 1.4 - 1), 2 + r, ((int)(r * 1.4)) * 2 + 3, 1, K.red); R(x, 3, 7, 10, 7, K.outl); for (int i = 0; i < 4; i++) R(x, 4 + i * 2, 8, 2, 5, i % 2 != 0 ? K.white : K.blue); break;
            case "heart": Glyph(x, 4, 5, ICON7_HEART, new Dictionary<char, string> { ['x'] = K.red }); break;
            case "fish": x.Blit(Icon("fish:perch"), 0, 0); break;
            case "lock": R(x, 4, 7, 8, 7, K.outl); R(x, 5, 8, 6, 5, K.gold); R(x, 5, 3, 6, 5, K.outl); x.ClearRect(6, 4, 4, 3); P(x, 8, 10, K.outl); break;
            case "ger": x.BlitScaled(GerSprite(K.blue, K.red), 0, 0, 16, 17); break;
            case "wrestle": x.BlitScaled(Wrestler("#d8342b", "#edbb93", "dance", 0), 2, 0, 11, 16); break;
            case "ovoo":
                { string[] st = { K.rock, K.rockL, K.rockD }; for (int r = 0; r < 5; r++) for (int i = -r; i <= r; i++) R(x, (int)(7 + i * 1.5), 7 + r * 2, 2, 2, st[((i + r + 6) % 3 + 3) % 3]); }
                R(x, 7, 1, 1, 7, K.woodD); Line(x, 7, 2, 3, 7, K.light); Line(x, 8, 2, 12, 6, K.blue); break;
            case "dog": x.BlitScaled(BankharFace(0), 0, 1, 16, 15); break;
            case "book": R(x, 1, 3, 14, 11, K.outl); R(x, 2, 4, 6, 9, K.paper); R(x, 8, 4, 6, 9, "#fff3d6"); R(x, 7, 3, 2, 11, K.redD); foreach (int ly in new[] { 6, 8, 10 }) { R(x, 3, ly, 4, 1, K.woodL); R(x, 9, ly, 4, 1, K.woodL); } break;
            case "menu": foreach (int ly in new[] { 4, 8, 12 }) { R(x, 2, ly - 1, 12, 3, K.outl); R(x, 3, ly, 10, 1, K.paper); } break;
            case "horse": x.BlitScaled(Horse("bay", 0, true), 0, 2, 16, 13); break;
            case "trophy": R(x, 4, 2, 8, 6, K.outl); R(x, 5, 3, 6, 5, K.gold); R(x, 2, 3, 2, 3, K.goldD); R(x, 12, 3, 2, 3, K.goldD); R(x, 7, 8, 2, 3, K.goldD); R(x, 5, 11, 6, 3, K.outl); R(x, 6, 12, 4, 1, K.woodL); P(x, 6, 4, K.white); break;
            case "bow":
                for (int r = 0; r < 13; r++) { int bx = 4 + Js.Round(4 * Math.Sin((r / 12.0) * Math.PI)); P(x, bx, 2 + r, K.woodD); P(x, bx + 1, 2 + r, K.wood); }
                Line(x, 4, 2, 4, 14, K.white); Line(x, 3, 8, 14, 8, K.woodD); R(x, 13, 7, 2, 3, "#8a8a99"); R(x, 2, 7, 2, 1, K.red); R(x, 2, 9, 2, 1, K.red); break;
            case "tree": R(x, 7, 9, 2, 6, K.woodD); Line(x, 8, 10, 3, 5, K.woodD); Line(x, 8, 10, 13, 5, K.woodD); Line(x, 8, 8, 8, 3, K.woodD); foreach (var (a, b) in new[] { (3, 4), (13, 4), (8, 2), (5, 7), (11, 7) }) Blob(x, a, b, 1, 1, K.gold, K.goldD); break;
            case "logo":
                Ell(x, 8, 15, 8, 3, K.grass); Blob(x, 12, 3, 2, 2, K.gold, K.goldD);
                for (int c2 = 2; c2 < 14; c2++) { double dx = (c2 - 8) / 6.0; int top = 9 - Js.Round(4 * Math.Sqrt(Math.Max(0, 1 - dx * dx))); R(x, c2, top, 1, 10 - top, K.white); P(x, c2, top - 1, K.outl); }
                R(x, 3, 9, 10, 4, K.outl); R(x, 4, 9, 8, 3, K.felt); R(x, 4, 9, 8, 1, K.blue); R(x, 7, 10, 2, 3, K.red); break;
        }
    }

    // ---------------------------------------------------------------- building a ger by hand (pixel.js: shorn, feltMat, gerStage, the "part" icons)
    /** The parts of a ger, and the wool its felt is made from. */
    static void PartIcon(PxCanvas x, string id)
    {
        string O = K.outl, ORG = "#d9762b", ORGD = "#9a4a1a", FELT = "#d8c8a8", FELTD = "#b8a47e";
        switch (id)
        {
            case "shal": R(x, 1, 6, 14, 6, O); R(x, 2, 7, 12, 4, "#c9a26b"); foreach (int X in new[] { 5, 9, 12 }) R(x, X, 7, 1, 4, "#8a5a33"); break;
            case "khana": R(x, 1, 3, 14, 11, O); R(x, 2, 4, 12, 9, "#f0e2c2"); for (int X = 2; X < 14; X++) for (int Y = 4; Y < 13; Y++) if ((X + Y) % 4 == 0 || (X - Y + 16) % 4 == 0) P(x, X, Y, ORG); break;
            case "khaalga": R(x, 4, 1, 8, 14, O); R(x, 5, 2, 6, 12, "#e14b3d"); R(x, 6, 4, 4, 7, "#f7c646"); P(x, 7, 7, "#2b52a5"); P(x, 8, 7, "#2b52a5"); break;
            case "toono": Ell(x, 8, 8, 7, 5, O); Ell(x, 8, 8, 6, 4, ORG); Ell(x, 8, 8, 4, 2, "#8fd0ff"); R(x, 3, 8, 10, 1, ORGD); R(x, 8, 4, 1, 9, ORGD); break;
            case "bagana": foreach (int X in new[] { 4, 10 }) { R(x, X, 1, 3, 14, O); R(x, X + 1, 2, 1, 12, ORG); R(x, X + 1, 3, 1, 1, "#f7c646"); R(x, X + 1, 11, 1, 1, "#2b52a5"); } break;
            case "uni": for (int i = 0; i < 5; i++) Line(x, 2 + i * 2, 14, 6 + i * 2, 1, i % 2 == 1 ? ORGD : ORG); break;
            case "esgii": R(x, 1, 5, 12, 8, O); R(x, 2, 6, 10, 6, FELT); R(x, 2, 11, 10, 1, FELTD); Ell(x, 12, 9, 3, 4, O); Ell(x, 12, 9, 2, 3, FELTD); P(x, 12, 9, FELT); break;
            case "buurees": R(x, 1, 3, 14, 10, O); R(x, 2, 4, 12, 8, "#ffffff"); R(x, 2, 6, 12, 1, "#3f76db"); R(x, 2, 11, 12, 1, "#d8dde6"); break;
            case "buslvvr": Ell(x, 8, 8, 6, 6, O); Ell(x, 8, 8, 5, 5, "#a46d3b"); Ell(x, 8, 8, 3, 3, O); Ell(x, 8, 8, 2, 2, "#c99257"); P(x, 13, 13, O); P(x, 14, 14, "#a46d3b"); break;
            case "tulga": R(x, 3, 6, 10, 8, O); R(x, 4, 7, 8, 6, "#5a5a66"); R(x, 5, 9, 6, 3, "#f39b2c"); R(x, 7, 1, 2, 5, O); R(x, 6, 14, 1, 2, O); R(x, 9, 14, 1, 2, O); break;
            case "wool":
                foreach (var (bx, by, r) in new[] { (5, 9, 4), (10, 9, 4), (8, 6, 4) }) Ell(x, bx, by, r, r - 1, O);
                foreach (var (bx, by, r) in new[] { (5, 9, 3), (10, 9, 3), (8, 6, 3) }) Ell(x, bx, by, r, r - 1, "#f6f2e9");
                P(x, 7, 5, "#ffffff"); break;
        }
    }
    static readonly Dictionary<PxCanvas, PxCanvas> shornCache = new Dictionary<PxCanvas, PxCanvas>();
    /** A sheep just after shearing: its white wool becomes short, pinkish fleece. */
    public static PxCanvas Shorn(PxCanvas img)
    {
        if (shornCache.TryGetValue(img, out var c)) return c;
        c = new PxCanvas(img.W, img.H); c.Blit(img, 0, 0);
        for (int y = 0; y < c.H; y++) for (int X = 0; X < c.W; X++)
            {
                var p = c.Get(X, y); if (p.a == 0 || p.r <= 200 || p.g <= 195 || p.b <= 180) continue;
                bool k = p.r <= 235; c.Put(X, y, k ? new Color32(214, 178, 160, p.a) : new Color32(236, 204, 186, p.a));
            }
        return shornCache[img] = c;
    }
    /** The felt-making spot by the pasture: a hide on the ground, a wool pile, a water bucket and the roller. */
    public static PxCanvas FeltMat() => Cached("feltMat", () =>
    {
        var x = new PxCanvas(32, 16); string O = K.outl;
        Ell(x, 14, 10, 13, 5, O); Ell(x, 14, 10, 12, 4, "#a46d3b"); Ell(x, 14, 9, 10, 3, "#b98250");
        foreach (var (X, Y) in new[] { (8, 8), (13, 7), (18, 8) }) { R(x, X - 1, Y - 1, 5, 4, O); R(x, X, Y, 3, 2, "#f6f2e9"); }
        R(x, 26, 6, 6, 8, O); R(x, 27, 7, 4, 6, "#8a5a33"); R(x, 27, 7, 4, 2, "#4aa6e2");
        R(x, 2, 13, 22, 3, O); R(x, 3, 14, 20, 1, "#d8c8a8");
        return x;
    });
    /** A ger part-way up, as it stands on its plot while being built (stage = steps done; 9 is the finished ger). */
    public static PxCanvas GerStage(int stage, string roof, string door)
    {
        if (stage >= 9) return GerSprite(roof, door);
        return Cached("gerStage:" + stage + roof + door, () =>
        {
            var x = new PxCanvas(48, 50); string OUT = K.outl, ORG = "#d9762b", ORGD = "#9a4a1a", WOOD = "#c9a26b", WOODD = "#8a5a33";
            void Floor() { R(x, 3, 43, 42, 5, OUT); R(x, 4, 44, 40, 3, WOOD); for (int X = 7; X < 44; X += 5) R(x, X, 44, 1, 3, WOODD); R(x, 4, 46, 40, 1, WOODD); }
            void Posts() { foreach (int X in new[] { 21, 26 }) { R(x, X, 14, 2, 31, OUT); R(x, X, 15, 1, 29, ORG); } }
            void Lattice()
            {
                for (int X = 3; X <= 45; X++) for (int Y = 30; Y <= 43; Y++) if ((X + Y) % 5 == 0 || (X - Y + 50) % 5 == 0) P(x, X, Y, (X + Y) % 10 == 0 ? ORGD : ORG);
                R(x, 3, 29, 43, 1, ORGD); R(x, 2, 29, 1, 15, OUT); R(x, 46, 29, 1, 15, OUT);
            }
            void Doorway() { R(x, 17, 30, 14, 18, OUT); R(x, 18, 31, 12, 16, ORG); R(x, 20, 33, 8, 13, door); R(x, 22, 36, 4, 6, "#f7c646"); }
            void Crown() { Ell(x, 24, 13, 8, 3, OUT); Ell(x, 24, 13, 7, 2, ORG); R(x, 21, 13, 6, 1, "#8fd0ff"); }
            void Poles() { for (int j = 0; j < 9; j++) { int ex = 3 + Js.Round(j * 42 / 8.0); Line(x, 24 + Js.Round((ex - 24) / 5.0), 14, ex, 29, ORG); } }
            if (stage <= 5) { Floor(); if (stage >= 4) Posts(); if (stage >= 2) Lattice(); if (stage >= 5) Poles(); if (stage >= 3) Doorway(); if (stage >= 4) Crown(); }
            else
            {
                // felt, then the white cover, then the ropes: the finished shape without its stove pipe yet
                x.Blit(GerSprite(roof, door), 0, 0); x.ClearRect(24, 0, 8, 10);
                if (stage == 6) // the brown felt shows before the white cover goes on
                {
                    var tint = new Color32(150, 118, 78, 255);
                    for (int y = 0; y < 50; y++) for (int X = 0; X < 48; X++) { var p = x.Get(X, y); if (p.a == 0) continue; x.Put(X, y, Color32.Lerp(p, tint, 0.55f)); }
                    Doorway();
                }
                if (stage == 8) foreach (int Y in new[] { 33, 38, 43 }) { R(x, 4, Y, 13, 1, "#5a3a22"); R(x, 31, Y, 13, 1, "#5a3a22"); }
            }
            return x;
        });
    }
}
