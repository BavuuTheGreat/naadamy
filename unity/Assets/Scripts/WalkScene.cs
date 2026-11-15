// Full-screen walkable scenes (inside a ger, a trip to an aimag), ported from js/scene.js: a pixel-art room drawn into a
// buffer and scaled to fill the screen. Move with WASD / arrows or by tapping, Space jumps, E uses what is next to you.
using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class Game
{
    public sealed class Thing { public string id; public float x, y, penalty; public Func<string> label; public Action run; }
    /** A small map of the whole scene in the corner, with coloured dots; tap it to walk there. */
    public sealed class MiniMap { public PxCanvas img; public Func<List<(float x, float y, string c)>> dots; public Rect rect; }
    public sealed class Ent { public float y; public Action<PxCanvas> f; }

    public sealed class WalkScene : Modal
    {
        public int W, H; public float speed = 64, reach = 22;
        public string title, sub, bg = "#1e140c"; public Look look;
        public Func<float, float, bool> free;
        public List<Thing> things = new List<Thing>();
        public Action<PxCanvas, float> drawBg, drawTop;
        public Func<float, List<Ent>> ents;
        public List<(Func<string> label, Action run)> buttons = new List<(Func<string>, Action)>();
        public Action<float> step;
        public Action onClose;
        public string info;
        public MiniMap mini;
        public FpSource fp; // the same place in first person (aimag trips)

        public float mx, my, tx, ty, jt = -9, z; int dir = 3; bool moving, running, runLock;
        float dogX, dogY; Thing near, pending;
        Live live; float k = 1, camX, camY;
        public override bool FullScreen => true;

        public void Start(float x, float y) { mx = tx = x; my = ty = y; dogX = x + 14; dogY = y + 4; }
        public void Say(string text) => info = text;

        public override void Tick(Game g, float dt)
        {
            if (live == null) live = new Live(W, H);
            float ix = g.KeyX(), iy = g.KeyY();
            if (ix != 0 || iy != 0) { float n = Mathf.Sqrt(ix * ix + iy * iy); tx = mx + ix / n * 8; ty = my + iy / n * 8; pending = null; }
            float dx = tx - mx, dy = ty - my, d = Mathf.Sqrt(dx * dx + dy * dy);
            moving = d > 1;
            if (moving)
            {
                running = g.ShiftHeld || runLock || g.runHeld;
                float sp = speed * (running ? 1.75f : 1) * dt, sx = dx / d * Mathf.Min(sp, d), sy = dy / d * Mathf.Min(sp, d);
                bool okX = free(mx + sx, my), okY = free(mx, my + sy);
                if (okX) mx += sx; if (okY) my += sy;
                if (!okX && !okY) { tx = mx; ty = my; }
                dir = Mathf.Abs(dx) > Mathf.Abs(dy) ? (dx < 0 ? 1 : 2) : (dy < 0 ? 3 : 0);
            }
            if (g.jumpHeld && Time.time - jt >= JUMP_S) { jt = Time.time; Sfx.Pop(); } // the Jump button, held
            float jq = (Time.time - jt) / JUMP_S; z = jq >= 0 && jq < 1 ? Mathf.Round(Mathf.Sin(jq * Mathf.PI) * 9) : 0;
            dogX += (mx + 14 - dogX) * Mathf.Min(1, dt * 2.6f); dogY += (my + 4 - dogY) * Mathf.Min(1, dt * 2.6f);
            near = null; float bd = reach;
            foreach (var t in things) { float dd = Mathf.Sqrt((t.x - mx) * (t.x - mx) + (t.y - my) * (t.y - my)) + t.penalty; if (dd < bd) { bd = dd; near = t; } } // penalty: wandering animals give way to places and people
            if (pending != null && near == pending) { var p = pending; pending = null; tx = mx; ty = my; p.run(); }
            step?.Invoke(dt);
        }
        public override void Key(Game g)
        {
            if (Input.GetKeyDown(KeyCode.Space)) { if (Time.time - jt >= JUMP_S) { jt = Time.time; Sfx.Pop(); } }
            if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Return)) near?.run();
        }
        public override void Press(Game g) { }
        public override void Closed(Game g) => onClose?.Invoke();
        public override void Pointer(Game g, int id, Vector2 pos, bool down, bool up)
        {
            if (!down || live == null) return;
            var gui = new Vector2(pos.x, Screen.height - pos.y);
            if (mini != null && mini.rect.Contains(gui)) { tx = (gui.x - mini.rect.x) / mini.rect.width * W; ty = (gui.y - mini.rect.y) / mini.rect.height * H; pending = null; return; }
            if (g.OverUI(pos)) return;
            float px = (gui.x - live.last.x) / k, py = (gui.y - live.last.y) / k;
            Thing hit = null; foreach (var t in things) if (Mathf.Sqrt((t.x - px) * (t.x - px) + (t.y - 14 - py) * (t.y - 14 - py)) < 18) { hit = t; break; }
            if (hit != null && Mathf.Sqrt((hit.x - mx) * (hit.x - mx) + (hit.y - my) * (hit.y - my)) < reach) { hit.run(); return; }
            if (hit != null) { tx = hit.x; ty = hit.y + 6; pending = hit; } else { tx = px; ty = py; pending = null; }
        }

        public override void Draw(Game g)
        {
            if (live == null) return;
            float t = Time.time; var b = live.c;
            b.Fill(Px.Col(bg));
            drawBg?.Invoke(b, t);
            var list = ents != null ? ents(t) : new List<Ent>();
            int dfr = Mathf.Sqrt((mx + 14 - dogX) * (mx + 14 - dogX) + (my + 4 - dogY) * (my + 4 - dogY)) > 3 ? 1 + (int)(t / 0.12f) % 2 : 3;
            list.Add(new Ent { y = dogY, f = x => x.Blit(Px.Bankhar(dfr), Js.Round(dogX - 11), Js.Round(dogY - 16)) });
            var me = look.Copy(); me.ride = null;
            list.Add(new Ent { y = my, f = x => { int sw = z > 4 ? 4 : 5; Px.R(x, Js.Round(mx - sw), Js.Round(my - 1), sw * 2, 2, "rgba(30,20,10,.28)"); x.Blit(Px.Char(me, dir, moving && z == 0 ? 1 + (int)(t / (running ? 0.075f : 0.125f)) % 2 : 0), Js.Round(mx - 8), Js.Round(my - 26 - z)); } });
            list.Sort((a, c) => a.y.CompareTo(c.y)); foreach (var e in list) e.f(b);
            if (near != null) { int bob = Mathf.RoundToInt(Mathf.Sin(t / 0.16f) * 1.5f); Px.R(b, Js.Round(near.x) - 1, Js.Round(near.y) - 40 + bob, 3, 3, "#f7c646"); Px.R(b, Js.Round(near.x), Js.Round(near.y) - 36 + bob, 1, 2, "#f7c646"); }
            drawTop?.Invoke(b, t);
            // scale it onto the screen: the whole room if it fits, otherwise follow you
            g.Box(new Rect(0, 0, Screen.width, Screen.height), Px.Col(bg));
            // HEAD: a person is drawn up to this far above their feet, so the view keeps that much room above the top row
            // (standing at the top edge, the head was cut off by the top of the screen)
            const float HEAD = 22;
            k = Mathf.Min(Screen.width / (float)W, Screen.height / (H + HEAD));
            if (Screen.width < Screen.height) k = Mathf.Max(k, Screen.height / (H * 1.6f));
            float vw = Screen.width / k, vh = Screen.height / k;
            camX = vw >= W ? (W - vw) / 2 : Mathf.Clamp(mx - vw / 2, 0, W - vw);
            camY = vh >= H + HEAD ? (H - HEAD - vh) / 2 : Mathf.Clamp(my - vh * 0.45f, -HEAD, H - vh);
            live.ShowAt(new Rect(-camX * k, -camY * k, W * k, H * k));
            // top: leave and the title; bottom: info, buttons and the action
            var safe = Screen.safeArea; float sx = safe.x, sy = Screen.height - safe.yMax, sw2 = safe.width / g.UiScale, sh = safe.height / g.UiScale;
            GUI.BeginGroup(new Rect(sx, sy, safe.width, safe.height));
            if (g.Button(g.URect(8, 8, 44, 44), "×")) g.Later(g.Close);
            string ttl = $"<b>{title}</b>" + (sub != null ? $"  <size={g.Ts(11)}>{sub}</size>" : ""); float ttw = Mathf.Min(360, sw2 - 72); // the title box grows with its words
            float tth = Mathf.Max(44, g.sNote.CalcHeight(new GUIContent(ttl), g.U(ttw)) / g.UiScale + 4);
            var tr = g.URect(64, 8, ttw, tth); GUI.Box(tr, ttl, g.sNote);
            if (!string.IsNullOrEmpty(info))   // what is said here, in a box as tall as the words
            {
                var ist = new GUIStyle(g.sNote) { alignment = TextAnchor.MiddleLeft, fontSize = Mathf.RoundToInt(g.UiScale * Game.TALK), wordWrap = true }; float iw = Mathf.Min(520, sw2 - 16), ih = Mathf.Max(48, ist.CalcHeight(new GUIContent(info), g.U(iw)) / g.UiScale + 4);
                var ir = g.URect(8, sh - 68 - ih, iw, ih); g.UiAdd(ir); GUI.Box(ir, info, ist);
            }
            float bx = 8;
            foreach (var (label, run) in buttons) { string l = label(); float w = Mathf.Max(90, g.sBtn.CalcSize(new GUIContent(l)).x / g.UiScale + 16); /* measured, so a long label fits */ if (g.Button(g.URect(bx, sh - 60, w, 52), l)) g.Later(run); bx += w + 6; }
            if (Game.TouchUI) g.Hold(g.URect(sw2 - 340, sh - 60, 82, 52), false, Loc.L("Гүйх", "Run"));
            else if (g.Button(g.URect(sw2 - 340, sh - 60, 82, 52), runLock ? Loc.L("Алхах", "Walk") : Loc.L("Гүйх", "Run"), runLock ? g.sPrimary : g.sBtn)) runLock = !runLock;
            g.Hold(g.URect(sw2 - 256, sh - 60, 82, 52), true, Loc.L("Үсрэх", "Jump")); // held: the tick above jumps
            bool was = GUI.enabled; GUI.enabled = near != null;
            string act = near != null ? near.label() + (Game.TouchUI ? "" : " (E)") : Loc.L("Ойртоод E дар", "Walk up and press E");
            var nn = near; if (g.Button(g.URect(sw2 - 166, sh - 64, 158, 56), act, g.sPrimary)) g.Later(() => nn?.run());
            GUI.enabled = was;
            if (mini != null) DrawMini(g, sw2);
            GUI.EndGroup();
        }
        void DrawMini(Game g, float sw2)
        {
            float mw = Mathf.Min(g.U(150), sw2 * 0.3f), mh = mw * H / W; var r = g.URect(0, 0, 0, 0); r = new Rect(sw2 * g.UiScale - mw - g.U(8), g.U(58), mw, mh);
            mini.rect = new Rect(r.x + Screen.safeArea.x, r.y + (Screen.height - Screen.safeArea.yMax), r.width, r.height); g.UiAdd(r);
            g.Box(new Rect(r.x - g.U(2), r.y - g.U(2), r.width + g.U(4), r.height + g.U(4)), Px.Col("#2b1e2e"));
            GUI.DrawTexture(r, g.Tex(mini.img));
            float s = mw / W, d = Mathf.Max(3, g.U(4));
            foreach (var (x, y, c) in mini.dots()) { g.Box(new Rect(r.x + x * s - d / 2 - 1, r.y + y * s - d / 2 - 1, d + 2, d + 2), Px.Col("#2b1e2e")); g.Box(new Rect(r.x + x * s - d / 2, r.y + y * s - d / 2, d, d), Px.Col(c)); }
            if ((int)(Time.time / 0.4f) % 2 != 0) g.Box(new Rect(r.x + mx * s - d, r.y + my * s - d, d * 2, d * 2), Color.white);
            g.Box(new Rect(r.x + mx * s - d / 2, r.y + my * s - d / 2, d, d), Px.Col("#e14b3d"));
        }
    }
    public Rect URect(float x, float y, float w, float h) => UR(x, y, w, h);
    public void UiAdd(Rect r) => Ui(r);
}
