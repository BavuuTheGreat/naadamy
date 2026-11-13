// Монгол бичгийн академи (js/bichig.js): a master scribe teaches the traditional Mongol script. Watch the letter being written
// top to bottom, trace it with a finger, then write it from memory. Tracing is checked against the real letter shape: how
// much of the letter you covered, how well you stayed on it, and that you wrote downwards. The letters are rendered with
// Noto Sans Mongolian ahead of time (Unity can't shape Mongol script), into Resources/Art/bichig_glyphs.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static class Bichig
{
    public sealed class Item { public string id, g, cyr, lat, unit; public Tx tip; public int cell; }
    public sealed class Unit { public string id; public Tx name; public List<string> items; public int iconCell; }
    static J D => Data.Get("bichig");
    static List<Item> all; static List<Unit> units;
    public static List<Item> ALL => all ??= D["ALL"].Select((x, i) => new Item { id = x["id"].S, g = x["g"].S, cyr = x["cyr"].S, lat = x["lat"].S, unit = x["unit"].S, tip = x["tip"].T, cell = i }).ToList();
    public static List<Unit> UNITS => units ??= D["UNITS"].Select((u, i) => new Unit { id = u["id"].S, name = u["name"].T, items = u["items"].Strings(), iconCell = D["ALL"].Count + i }).ToList();
    public static int Count => ALL.Count;
    public static int W => D["W"].I; public static int H => D["H"].I;

    /** A letter's shape as a mask on the W×H writing area, and where it sits (from the real font). */
    public sealed class Glyph { public bool[] ink; public int top, bot, left, right; public float spine; public bool[] wide; }
    static readonly Dictionary<int, Glyph> glyphs = new Dictionary<int, Glyph>();
    public static Glyph Get(int cell)
    {
        if (glyphs.TryGetValue(cell, out var gl)) return gl;
        var sheet = Art.Get("bichig_glyphs"); int w = W, h = H, cols = D["COLS"].I, ox = (cell % cols) * w, oy = (cell / cols) * h;
        var ink = new bool[w * h]; int x0 = w, y0 = h, x1 = 0, y1 = 0;
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) { var p = sheet.Get(ox + x, oy + y); bool on = p.a > 0 && (p.r + p.g + p.b) / 3 < 140; ink[y * w + x] = on; if (on) { x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); y0 = Math.Min(y0, y); y1 = Math.Max(y1, y); } }
        int spX = x0, best = -1; for (int x = x0; x <= x1; x++) { int n = 0; for (int y = y0; y <= y1; y++) if (ink[y * w + x]) n++; if (n > best) { best = n; spX = x; } } // the spine: the column with the most ink
        return glyphs[cell] = new Glyph { ink = ink, top = y0, bot = y1, left = x0, right = x1, spine = spX };
    }
    /** The mask grown by r pixels all round (to be forgiving about staying on the letter). */
    public static bool[] Dilate(bool[] m, int r)
    {
        int w = W, h = H; var o = new bool[w * h]; var offs = new List<(int, int)>(); for (int dy = -r; dy <= r; dy++) for (int dx = -r; dx <= r; dx++) if (dx * dx + dy * dy <= r * r) offs.Add((dx, dy));
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) if (m[y * w + x]) foreach (var (dx, dy) in offs) { int X = x + dx, Y = y + dy; if (X >= 0 && Y >= 0 && X < w && Y < h) o[Y * w + X] = true; }
        return o;
    }
    /** Brush strokes as a mask: a round brush of the given width along every stroke. */
    public static bool[] Ink(List<List<Vector2>> strokes, float width)
    {
        int w = W, h = H; var o = new bool[w * h]; float r = width / 2;
        void Dot(float cx, float cy) { for (int y = Mathf.FloorToInt(cy - r); y <= Mathf.CeilToInt(cy + r); y++) for (int x = Mathf.FloorToInt(cx - r); x <= Mathf.CeilToInt(cx + r); x++) if (x >= 0 && y >= 0 && x < w && y < h && (x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy) <= r * r) o[y * w + x] = true; }
        foreach (var s in strokes) { if (s.Count == 1) Dot(s[0].x, s[0].y); for (int i = 1; i < s.Count; i++) { var a = s[i - 1]; var b = s[i]; int n = Mathf.CeilToInt((b - a).magnitude) + 1; for (int k = 0; k <= n; k++) { var p = Vector2.Lerp(a, b, k / (float)n); Dot(p.x, p.y); } } }
        return o;
    }
    const float BRUSH = 13, TOL = 9;
    public struct Score { public float cov, prec; public bool down, top, empty; }
    /** Score a tracing: coverage of the letter, staying on the letter, writing from the top downwards. */
    public static Score Check(int cell, List<List<Vector2>> strokes)
    {
        int pts = strokes.Sum(s => s.Count); if (pts < 4) return new Score { empty = true };
        var gl = Get(cell); gl.wide ??= Dilate(gl.ink, (int)TOL);
        var ik = Ink(strokes, BRUSH); var ip = Ink(strokes, BRUSH + TOL * 2);
        int g = 0, gHit = 0, k = 0, kHit = 0;
        for (int i = 0; i < gl.ink.Length; i += 2) { if (gl.ink[i]) { g++; if (ip[i]) gHit++; } if (ik[i]) { k++; if (gl.wide[i]) kHit++; } }
        var first = strokes[0][0]; float hgt = Mathf.Max(1, gl.bot - gl.top), dy = 0;
        foreach (var s in strokes) if (s.Count > 1) dy += s[s.Count - 1].y - s[0].y;
        return new Score { cov = g > 0 ? gHit / (float)g : 0, prec = k > 0 ? kHit / (float)k : 0, top = first.y < gl.top + hgt * 0.4f, down = dy >= 0 };
    }
    static readonly Dictionary<string, PxCanvas> tiles = new Dictionary<string, PxCanvas>();
    /** A small pixel-art version of a letter (letter tiles, headers): the glyph shrunk to w×h pixels, in one colour. */
    public static PxCanvas Pixel(int cell, int w, int h, string color = "#2b1e2e")
    {
        string key = cell + ":" + w + "x" + h + color; if (tiles.TryGetValue(key, out var c)) return c;
        var gl = Get(cell); c = new PxCanvas(w, h); var col = Px.Col(color);
        int bw = gl.right - gl.left + 1, bh = gl.bot - gl.top + 1; float s = Mathf.Min(w * 0.9f / bw, h * 0.9f / bh); float ox = (w - bw * s) / 2, oy = (h - bh * s) / 2;
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
        {
            int n = 0, on = 0; // a pixel is ink when enough of the area it covers is ink
            for (float sy = 0.25f; sy < 1; sy += 0.5f) for (float sx = 0.25f; sx < 1; sx += 0.5f) { int gx = gl.left + Mathf.FloorToInt((x + sx - ox) / s), gy = gl.top + Mathf.FloorToInt((y + sy - oy) / s); n++; if (gx >= 0 && gy >= 0 && gx < W && gy < H && gl.ink[gy * W + gx]) on++; }
            if (on * 2 >= n) c.Put(x, y, col);
        }
        return tiles[key] = c;
    }
}

public sealed partial class Game
{
    public int BichigDone() => Bichig.ALL.Count(it => SaveData.Get(V.bichig, it.id) > 0);
    static readonly Look MASTER = new Look { deel = "brown", skin = "#D9A06E", hat = "mongol" };

    sealed class BichigPage : Page
    {
        protected override bool Hero => true; protected override string Tab => "learn";
        int idx; string mode = "trace"; float demoT = -1; string say; readonly List<List<Vector2>> strokes = new List<List<Vector2>>(); List<Vector2> cur;
        Bichig.Score? result; bool resultOk; int resultStars;
        const int CELL = 3; readonly Live live; readonly int lw, lh;
        public BichigPage()
        {
            var g = I; lw = Mathf.CeilToInt(Bichig.W / (float)CELL); lh = Mathf.CeilToInt(Bichig.H / (float)CELL); live = new Live(lw, lh);
            var all = Bichig.ALL; idx = all.FindIndex(it => Unlocked(g, all.IndexOf(it)) && SaveData.Get(g.V.bichig, it.id) == 0); if (idx < 0) idx = 0;
            say = Tip(Item);
        }
        static bool Unlocked(Game g, int i) => i == 0 || SaveData.Get(g.V.bichig, Bichig.ALL[i - 1].id) > 0;
        Bichig.Item Item => Bichig.ALL[idx];
        protected override string Title => Loc.L("Монгол бичгийн академи", "Mongol Script Academy");
        void Select(Game g, int i)
        {
            if (!Unlocked(g, i)) { Sfx.Bad(); say = Loc.L("Эхлээд өмнөх үсгээ бичээрэй.", "Write the one before first."); return; }
            idx = i; strokes.Clear(); mode = "trace"; result = null; demoT = -1;
            say = Tip(Item);
        }
        static string Tip(Bichig.Item it) => it.tip + " " + Loc.L("Эхлээд “Багш үзүүлэх” дээр дараад, дараа нь дээрээс доош даган бич.", "Press “Show me” first, then trace it from the top down.");
        void Paint()
        {
            var x = live.c; var gl = Bichig.Get(Item.cell); float t = Time.time;
            x.Fill(Px.Col("#fffaf0"));
            for (int gy = 6; gy < lh; gy += 6) Px.R(x, 0, gy, lw, 1, "rgba(201,154,90,.35)"); // paper lines on the pixel grid
            int sx0 = Mathf.FloorToInt(gl.spine / CELL); for (int gy = 2; gy < lh - 2; gy += 3) Px.R(x, sx0, gy, 1, 2, "rgba(225,75,61,.5)"); // the spine, as a dashed pixel line
            void Glyph(float upto, string col)
            {
                var c = Px.Col(col);
                for (int y = 0; y < lh; y++) { if (y * CELL > upto) break; for (int X = 0; X < lw; X++) { int n = 0; for (int sy = 0; sy < CELL; sy++) for (int sx = 0; sx < CELL; sx++) { int px = X * CELL + sx, py = y * CELL + sy; if (px < Bichig.W && py < Bichig.H && gl.ink[py * Bichig.W + px]) n++; } if (n * 3 > CELL * CELL) x.Put(X, y, c); } }
            }
            if (mode == "trace") Glyph(1e9f, "rgba(90,60,40,.2)");
            if (demoT >= 0) // the master writes it from the top down
            {
                float q = Mathf.Min(1, (t - demoT) / 2.2f), yy = gl.top - 6 + (gl.bot - gl.top + 12) * q;
                Glyph(yy, "#2b1e2e"); Px.R(x, sx0 - 1, Mathf.FloorToInt(yy / CELL) - 1, 3, 3, "#e14b3d"); // the master's brush
                if (q >= 1 && t - demoT > 2.7f) demoT = -1;
            }
            if (strokes.Count > 0)
            {
                var ink = Bichig.Ink(strokes, 13); var c = Px.Col("#1d1a2a");
                for (int y = 0; y < lh; y++) for (int X = 0; X < lw; X++) { int n = 0; for (int sy = 0; sy < CELL; sy++) for (int sx = 0; sx < CELL; sx++) { int px = X * CELL + sx, py = y * CELL + sy; if (px < Bichig.W && py < Bichig.H && ink[py * Bichig.W + px]) n++; } if (n * 3 > CELL * CELL) x.Put(X, y, c); }
            }
        }
        void CheckIt(Game g)
        {
            var r = Bichig.Check(Item.cell, strokes); float loose = mode == "memory" ? 0.07f : 0;
            if (r.empty) { Sfx.Bad(); say = Loc.L("Эхлээд хуруугаараа бичээрэй.", "Write it first with your finger."); return; }
            bool ok = r.cov >= 0.55f - loose && r.prec >= 0.6f - loose && r.down && r.top;
            int stars = !ok ? 0 : r.cov >= 0.82f - loose && r.prec >= 0.82f - loose ? 3 : r.cov >= 0.68f - loose && r.prec >= 0.7f - loose ? 2 : 1;
            result = r; resultOk = ok; resultStars = stars;
            if (!ok)
            {
                Sfx.Bad();
                say = !r.top || !r.down ? Loc.L("Монгол бичгийг дээрээс доош бичдэг. Дээд талаас нь эхлээд дахиад оролдоорой!", "Mongol script is written from the top down. Start at the top and try again!")
                    : r.cov < 0.55f - loose ? Loc.L("Үсгийн зарим хэсгийг алгассан байна. Шүд, сүүлийг нь бүгдийг нь бичээрэй.", "You missed part of the letter. Write every tooth and the tail too.")
                    : Loc.L("Бага зэрэг шугамнаас гарчээ. Аажуухан, загварыг даган бичээрэй.", "A little off the lines. Go slowly and follow the shape.");
                return;
            }
            var V = g.V; int was = SaveData.Get(V.bichig, Item.id); bool first = was == 0;
            V.bichig.RemoveAll(k => k.k == Item.id); V.bichig.Add(new KV { k = Item.id, v = Math.Max(was, stars) });
            if (mode == "memory" && !V.bichigMem.Contains(Item.id)) V.bichigMem.Add(Item.id);
            V.Save(); Sfx.Good(); if (stars == 3) g.Confetti(40);
            if (first)
            {
                g.Reward(5, 10);
                var u = Bichig.UNITS.First(q => q.id == Item.unit);
                if (u.items.All(id => SaveData.Get(V.bichig, id) > 0)) { g.Confetti(90); Sfx.Win(); g.Quest("lesson"); g.Toast(Loc.L($"“{u.name.mn}” бүлгийг дуусгалаа!", $"You finished “{u.name.en}”!")); }
            }
            g.CheckAchievements();
            say = (stars == 3 ? Loc.L("Гайхалтай! Жинхэнэ бичээч шиг бичлээ. ", "Wonderful! You wrote it like a real scribe. ") : stars == 2 ? Loc.L("Сайн байна! ", "Well done! ") : Loc.L("Болж байна! Дахиад бичвэл улам сайжирна. ", "Good! Write it again and it gets even better. "))
                + (mode == "trace" ? Loc.L("Одоо “Санаж бичих” дээр дараад загваргүй бичээд үз.", "Now try “From memory” without the guide.") : "");
        }
        protected override void Body(Game g)
        {
            var V = g.V; var item = Item; int st = SaveData.Get(V.bichig, item.id);
            GUILayout.BeginHorizontal(g.sPanel); g.IconAt(Px.Char(MASTER, 0, 0), 64); g.Gap(12);
            GUILayout.BeginVertical(); GUILayout.Label(Loc.L("МОНГОЛ БИЧГИЙН АКАДЕМИ", "MONGOL SCRIPT ACADEMY"), g.sKicker); GUILayout.Label(Loc.L("Бичээч багш", "The Master Scribe"), g.sTitle); GUILayout.Label(say, g.sTeach); GUILayout.EndVertical(); GUILayout.EndHorizontal();
            GUILayout.BeginVertical(g.sPanel);
            GUILayout.Label(Loc.L("Багшийн зөвлөгөө", "The master’s rules"), new GUIStyle(g.sLabel) { font = g.sTitle.font });
            foreach (var r in new[] { Loc.L("Монгол бичгийг дээрээс доош, мөрийг зүүнээс баруун тийш бичнэ.", "Write from top to bottom; columns go left to right."), Loc.L("Үсэг бүр “нуруу” гэдэг босоо шугамд өлгөөтэй.", "Every letter hangs on a vertical line called the spine (нуруу)."), Loc.L("Үсгийн хэсгүүд: титэм (эхэнд), шүд, гэдэс, сүүл (төгсгөлд).", "Parts of a letter: crown (титэм) at the start, teeth (шүд), bellies (гэдэс), tail (сүүл) at the end.") }) GUILayout.Label("  •  " + r, g.sLabel);
            GUILayout.EndVertical();
            GUILayout.BeginVertical(g.sPanel);
            GUILayout.BeginHorizontal(); g.IconAt(Bichig.Pixel(item.cell, 22, 48), 44); g.Gap(12);
            GUILayout.BeginVertical(); GUILayout.Label($"{item.cyr} • {item.lat}", g.sTitle);
            g.Small($"{Bichig.UNITS.First(u => u.id == item.unit).name} · {idx + 1}/{Bichig.Count}" + (V.bichigMem.Contains(item.id) ? "  · " + Loc.L("санаж бичсэн", "from memory") : ""));
            GUILayout.Label("<color=#f2a516>" + new string('★', st) + new string('☆', 3 - st) + "</color>", new GUIStyle(g.sLabel) { fontSize = Mathf.RoundToInt(g.U(20)) });
            GUILayout.EndVertical(); GUILayout.EndHorizontal();
            // the writing area: draw with a finger, pen or mouse
            Paint();
            float maxH = Mathf.Min(420, Screen.height / g.UiScale * 0.55f);
            var rr = live.Show(g, maxH);
            if (!(demoT >= 0)) { var tl = live.At(Bichig.Get(item.cell).right / (float)CELL + 4, Bichig.Get(item.cell).top / (float)CELL); GUI.Label(new Rect(Mathf.Min(rr.xMax - g.U(70), tl.x + g.U(8)), tl.y - g.U(10), g.U(80), g.U(20)), Loc.L("эхлэл ↓", "start ↓"), new GUIStyle(g.sLabel) { font = g.sTitle.font, fontSize = g.Ts(11), wordWrap = false, normal = { textColor = Px.Col("#e14b3d") } }); }
            var e = Event.current;
            Vector2 ToW(Vector2 m) { var p = live.Pick(m); return new Vector2(p.x * CELL, p.y * CELL); }
            if (e.type == EventType.MouseDown && rr.Contains(e.mousePosition) && demoT < 0) { cur = new List<Vector2> { ToW(e.mousePosition) }; strokes.Add(cur); result = null; e.Use(); }
            else if (e.type == EventType.MouseDrag && cur != null) { var p = ToW(e.mousePosition); if ((p - cur[cur.Count - 1]).magnitude > 1.5f) cur.Add(p); e.Use(); }
            else if (e.type == EventType.MouseUp && cur != null) { cur = null; e.Use(); }
            GUILayout.BeginHorizontal();
            if (g.Btn(Loc.L("► Багш үзүүлэх", "► Show me"))) g.Later(() => { strokes.Clear(); result = null; demoT = Time.time; Sfx.Pop(); say = Loc.L("Хараарай: дээрээс эхлээд нуруугаа даган доош бичнэ. Монгол бичгийн мөр зүүнээс баруун тийш явдаг.", "Watch: start at the top and write down along the spine. Columns of Mongol script go from left to right."); });
            if (g.Btn("↻ " + Loc.L("Арилгах", "Clear"))) g.Later(() => { strokes.Clear(); result = null; });
            if (g.Btn(mode == "trace" ? Loc.L("Санаж бичих", "From memory") : Loc.L("Даган бичих", "Trace"))) g.Later(() => { mode = mode == "trace" ? "memory" : "trace"; strokes.Clear(); result = null; say = mode == "memory" ? Loc.L("Одоо загваргүйгээр санаж бичээд үз! Улаан тасархай шугам бол нуруу.", "Now write it from memory, with no guide! The red dashed line is the spine.") : item.tip.ToString(); });
            if (g.Btn("✓ " + Loc.L("Шалгах", "Check"), true)) g.Later(() => CheckIt(g));
            GUILayout.EndHorizontal();
            if (result.HasValue && !result.Value.empty)
            {
                var r = result.Value; string Pct(float v) => Mathf.RoundToInt(v * 100) + "%";
                GUILayout.BeginHorizontal(g.sBtn);
                GUILayout.Label($"{Loc.L("Бүрхсэн", "Covered")} <b>{Pct(r.cov)}</b>", g.sLabel); GUILayout.Label($"{Loc.L("Шугамандаа", "On the line")} <b>{Pct(r.prec)}</b>", g.sLabel); GUILayout.Label($"{Loc.L("Чиглэл", "Direction")} <b>{(r.down && r.top ? "↓ ✓" : "✕")}</b>", g.sLabel);
                GUILayout.EndHorizontal();
                if (resultOk && idx + 1 < Bichig.Count && g.Btn(Loc.L("Дараагийнх →", "Next →"), true, 44)) g.Later(() => Select(g, idx + 1));
            }
            if (g.Btn(Loc.L("Басараас асуух", "Ask Basar"), false, 34)) g.Later(() => g.Open(new BasarChat(g, Loc.L($"Би “{item.cyr}” гэж Монгол бичгээр бичиж сурч байна.", $"I’m learning to write “{item.lat}” in Mongol script."), null)));
            GUILayout.EndVertical();
            GUILayout.Label(Loc.L($"<b>Бичсэн:</b> {g.BichigDone()}/{Bichig.Count}", $"<b>Written:</b> {g.BichigDone()}/{Bichig.Count}"), g.sCenter);
            foreach (var u in Bichig.UNITS)
            {
                GUILayout.BeginVertical(); g.Gap(10);
                GUILayout.BeginHorizontal(); g.IconAt(Bichig.Pixel(u.iconCell, 12, 26), 22); g.Gap(8); GUILayout.Label(u.name.ToString(), new GUIStyle(g.sLabel) { font = g.sTitle.font, fontSize = g.Ts(14) }); GUILayout.EndHorizontal();
                int cols = Mathf.Max(4, Mathf.FloorToInt(Mathf.Min(Screen.safeArea.width, g.U(720)) / g.U(70)));
                for (int i = 0; i < u.items.Count; i += cols)
                {
                    GUILayout.BeginHorizontal();
                    for (int k = i; k < Mathf.Min(u.items.Count, i + cols); k++)
                    {
                        var it = Bichig.ALL.First(q => q.id == u.items[k]); int ai = Bichig.ALL.IndexOf(it), s2 = SaveData.Get(V.bichig, it.id); bool lockd = !Unlocked(g, ai);
                        var r = GUILayoutUtility.GetRect(g.U(62), g.U(96), GUILayout.Width(g.U(62)));
                        if (GUI.Button(r, "", ai == idx ? g.sSel : g.sBtn)) { int j = ai; g.Later(() => Select(g, j)); }
                        var old = GUI.color; if (lockd) GUI.color = new Color(1, 1, 1, 0.4f);
                        GUI.DrawTexture(new Rect(r.center.x - g.U(13), r.y + g.U(6), g.U(26), g.U(56)), g.Tex(Bichig.Pixel(it.cell, 16, 34, "#2b1e2e")));
                        if (lockd) { var dim = GUI.color; GUI.color = old; GUI.DrawTexture(new Rect(r.center.x - g.U(8), r.y + g.U(64), g.U(16), g.U(16)), g.Tex(Px.Icon("ui:lock"))); GUI.color = dim; } // still locked: write the one before first
                        else GUI.Label(new Rect(r.x, r.y + g.U(62), r.width, g.U(18)), it.cyr, new GUIStyle(g.sCenter) { wordWrap = false });
                        if (s2 > 0) GUI.Label(new Rect(r.x, r.y + g.U(78), r.width, g.U(16)), "<color=#f2a516>" + new string('★', s2) + "</color>", new GUIStyle(g.sSmall) { alignment = TextAnchor.MiddleCenter });
                        GUI.color = old;
                    }
                    GUILayout.EndHorizontal();
                }
                GUILayout.EndVertical();
            }
        }
    }
}
