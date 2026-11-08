// The Naadam horse race as its own scene: the riders leave the village for the open steppe. Before the start the child
// jockeys circle and sing “Гийнгоо”, then 3-2-1 and away across the steppe past gers, herds and families who came to
// watch, to the finish line and its crowd. Hold “Чү!” to sprint (it tires the horse), change lanes to pass and to miss the
// marmot holes and rocks, or jump them. People along the way shout what Mongolians shout at a race.
// The pictures are Tools/art/naadam_bg.py's naadam_race_0..3; the results are the village race's (RaceResult).
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed partial class Game
{
    public sealed class NaadamRace : Modal
    {
        const int VW = 320, VH = 180, TILE = 1500, FINISH_IN_LAYER = 1760; const float Y0 = 140, Y1 = 168;
        public override bool FullScreen => true;
        readonly Game g; readonly int cls; readonly Rules.RaceClass C; readonly float D;
        readonly Live live = new Live(VW, VH); readonly PxCanvas[] L;
        sealed class Rider { public string name; public Look look; public float x, y, ty, speed, surge, wob, done, jump = -9, stumble; public bool me; }
        readonly List<Rider> riders = new List<Rider>(); Rider me;
        sealed class Obstacle { public float x, y; public bool rock, hit; }
        readonly List<Obstacle> obstacles = new List<Obstacle>();
        sealed class Shout { public float x, t; public string text; public bool finish; }
        readonly List<Shout> shouts = new List<Shout>();
        sealed class Dust { public float x, y, vx, life; }
        readonly List<Dust> dust = new List<Dust>();
        string phase = "giingoo"; float t0, go, finishT, stam = 1, camX, nextShout, opened; bool sprint; int lastCount = 99; string say;
        // touch controls
        Rect upR, downR, chuR, jumpR, exitR; readonly HashSet<int> chuIds = new HashSet<int>();

        public static int Km(Rules.RaceClass c) => c.laps == 1 ? 10 + (c.pace > 5.4f ? 5 : 0) : c.laps == 2 ? 20 : 25;
        static readonly (string mn, string en)[] CHEERS =
        {
            ("Чү! Чү!", "Go! Go!"), ("Гийнгоо!", "Giingoo!"), ("Хурдлаарай!", "Faster!"), ("Түрүүлээрэй!", "Take the lead!"), ("Уухай!", "Hooray!"),
            ("Хурдан морь!", "Swift horse!"), ("Сайн байна, хүү минь!", "Well done, kid!"), ("Чү, хүрэн морь!", "Go, bay horse!"),
        };
        static readonly (string mn, string en)[] FINISH = { ("Ирж байна! Ирж байна!", "Here they come!"), ("Айргийн тав!", "The top five!"), ("Түрүү морь!", "The winning horse!"), ("Уухай! Уухай!", "Hooray! Hooray!") };

        public NaadamRace(Game g, int clsIdx)
        {
            this.g = g; cls = Mathf.Clamp(clsIdx, 0, Rules.CLASSES.Length - 1); C = Rules.CLASSES[cls];
            D = FINISH_IN_LAYER + TILE * (1 + C.laps);   // a longer course for the older horses; the finish lines up with the picture's
            L = Enumerable.Range(0, 4).Select(i => Art.Get("naadam_race_" + i)).ToArray();
            var hs = g.V.horse;
            me = new Rider { name = g.MyName, me = true, look = new Look { deel = g.V.look.deel, skin = g.V.look.skin, hat = g.V.look.hat ?? "mongol", coat = hs.coat, gender = g.V.look.gender }, x = 40, y = 154, ty = 154 };
            riders.Add(me);
            var names = Rules.RIDERS.OrderBy(_ => Random.value).Take(5).ToArray(); var coats = Px.COATS.Keys.ToArray(); string[] hats = { null, "mongol", "cap", "fur" };
            float pace = C.pace * 13.5f + Mathf.Min(6, hs.wins * 0.8f);
            for (int i = 0; i < names.Length; i++)
                riders.Add(new Rider { name = names[i], look = new Look { deel = Rules.Pick(Rules.DEELS), skin = Rules.Pick(Rules.SKINS), hat = hats[i % hats.Length], coat = coats[i % coats.Length] },
                    x = 40 - (i % 3) * 14, y = Y0 + 2 + i * 5, ty = Y0 + 2 + i * 5, speed = pace * (0.92f + Random.value * 0.12f), wob = Random.value * 6 });
            // marmot holes and rocks scattered along the course (none at the start or in the last stretch)
            var rnd = new System.Random(cls * 97 + 3);
            for (float x = 420; x < D - 260; x += 140 + rnd.Next(0, 160)) obstacles.Add(new Obstacle { x = x, y = Y0 + 3 + rnd.Next(0, (int)(Y1 - Y0 - 6)), rock = rnd.Next(0, 3) == 0 });
            t0 = opened = Time.time; say = Loc.L("Гийнгоо! Унаач хүүхдүүд морьдоо тойруулан гийнгоолж байна…", "Giingoo! The child jockeys circle their horses and sing to them…");
            Sfx.Crowd(0.5f); Sfx.Horn();
        }

        int Place(Rider r) => 1 + riders.Count(o => o != r && (o.done > 0 && (r.done == 0 || o.done < r.done) || o.done == 0 && r.done == 0 && o.x > r.x));

        public override void Tick(Game gg, float dt)
        {
            float now = Time.time;
            if (phase == "giingoo")
            {
                // the riders walk their horses in a little circle, singing
                for (int i = 0; i < riders.Count; i++) { var r = riders[i]; float a = (now - t0) * 0.9f + i * 1.05f; r.x = 70 + Mathf.Cos(a) * 34; r.y = 154 + Mathf.Sin(a) * 10; }
                if (now - t0 > 4.5f) { phase = "count"; t0 = now; for (int i = 0; i < riders.Count; i++) { riders[i].x = 40; riders[i].y = riders[i].ty = Y0 + 2 + i * 5; } me.y = me.ty = 154; }
                camX = 0; return;
            }
            if (phase == "count")
            {
                int left = 3 - Mathf.FloorToInt(now - t0);
                if (left != lastCount) { lastCount = left; if (left > 0) Sfx.Tap(); else Sfx.Chime(); }
                if (now - t0 >= 3) { phase = "go"; go = now; say = Loc.L("Чү! Уралдаан эхэллээ!", "Chü! The race is on!"); Sfx.Crowd(0.6f); }
                return;
            }
            if (phase == "go" || phase == "done")
            {
                // you: lanes, sprint, jump
                float ky = g.KeyY(); bool up = ky < 0 || Held(upR), down = ky > 0 || Held(downR);
                if (phase == "go") { if (up) me.ty -= 46 * dt; if (down) me.ty += 46 * dt; }
                me.ty = Mathf.Clamp(me.ty, Y0, Y1);
                sprint = phase == "go" && (g.ShiftHeld || Input.GetKey(KeyCode.J) || chuIds.Count > 0);
                if (phase == "go" && (Input.GetKeyDown(KeyCode.Space) || jumpPressed) && now - me.jump > 0.7f) { me.jump = now; Sfx.Pop(); }
                jumpPressed = false;
                var hs = g.V.horse; float cruise = 64 + hs.spd * 3.2f, top = cruise + 34;
                if (sprint && stam > 0.02f) stam = Mathf.Max(0, stam - dt * (0.3f - hs.sta * 0.017f)); else stam = Mathf.Min(1, stam + dt * 0.11f);
                float mySpeed = me.done > 0 ? 30 : sprint && stam > 0.02f ? top : stam < 0.05f ? cruise - 10 : cruise;
                foreach (var r in riders)
                {
                    float sp = r.me ? mySpeed : RivalSpeed(r, dt, now);
                    if (r.stumble > 0) { r.stumble -= dt; sp *= 0.45f; }
                    r.x += sp * dt; r.y = Mathf.MoveTowards(r.y, r.ty, 40 * dt);
                    if (r.done == 0 && r.x >= D) { r.done = now; if (r.me) { finishT = now; phase = "done"; say = Loc.L($"Барианд орлоо! {Place(r)}-р байр!", $"Across the line! Place {Place(r)}!"); Sfx.Crowd(0.9f); if (Place(r) == 1) { Sfx.Win(); g.Confetti(80); } } }
                    // hooves throw up dust
                    if (Random.value < dt * (sp > 80 ? 14 : 8)) dust.Add(new Dust { x = r.x - 10, y = r.y - 1, vx = -20 - Random.value * 20, life = 0.6f });
                    // a hole or a rock in your lane trips the horse, unless it jumps
                    foreach (var o in obstacles) if (!o.hit && Mathf.Abs(o.x - r.x) < 7 && Mathf.Abs(o.y - r.y) < 5 && now - r.jump > 0.55f)
                        {
                            if (r.me) { o.hit = true; r.stumble = 0.8f; Sfx.Thud(); say = o.rock ? Loc.L("Чулуунд бүдэрлээ!", "Tripped on a rock!") : Loc.L("Тарвагын нүхэнд бүдэрлээ!", "Tripped in a marmot hole!"); }
                            else if (Random.value < 0.5f) { r.ty = Mathf.Clamp(r.ty + (Random.value < 0.5f ? -12 : 12), Y0, Y1); } else r.stumble = 0.5f;
                        }
                }
                // the cheering: families along the way, and the big crowd at the finish
                nextShout -= dt;
                if (nextShout <= 0)
                {
                    bool atEnd = me.x > D - 420; nextShout = atEnd ? 0.55f + Random.value * 0.5f : 1.1f + Random.value * 1.1f;
                    var c = atEnd ? FINISH[Random.Range(0, FINISH.Length)] : CHEERS[Random.Range(0, CHEERS.Length)];
                    float sx = atEnd ? D - 200 + Random.Range(-140, 160) : camX + Random.Range(40, VW - 40);
                    shouts.Add(new Shout { x = sx, t = now, text = Loc.En ? c.mn + " — " + c.en : c.mn, finish = atEnd });
                    if (Random.value < 0.4f) Sfx.Crowd(0.25f);
                }
                shouts.RemoveAll(s => now - s.t > 1.8f);
                foreach (var d in dust) { d.x += d.vx * dt; d.life -= dt; }
                dust.RemoveAll(d => d.life <= 0);
                // who leads
                var lead = riders.OrderByDescending(r => r.done > 0 ? D + 1000 - r.done : r.x).First();
                if (phase == "go" && now - go > 2 && Random.value < dt * 0.4f) say = lead.me ? Loc.L("Та тэргүүлж байна!", "You are in the lead!") : Loc.L($"{lead.name} тэргүүлж байна!", $"{lead.name} is in the lead!");
                if (phase == "done" && (riders.All(r => r.done > 0) || now - finishT > 6)) Finish();
                if (phase == "go" && now - go > 240) { me.done = now; finishT = now; phase = "done"; }
            }
            camX = Mathf.Clamp(Mathf.Lerp(camX, me.x - 110, Mathf.Min(1, dt * 5)), 0, D + 120 - VW);
        }
        float RivalSpeed(Rider r, float dt, float now)
        {
            if (r.done > 0) return 28;
            if (r.surge <= 0 && Random.value < dt * 0.14f) r.surge = 1.5f + Random.value * 1.8f;   // rivals make their own dashes
            if (r.surge > 0) r.surge -= dt;
            if (Random.value < dt * 0.25f) r.ty = Mathf.Clamp(r.ty + Random.Range(-10f, 10f), Y0, Y1);
            return r.speed * (1 + 0.05f * Mathf.Sin((now - go) * 1.3f + r.wob)) + (r.surge > 0 ? 18 : 0);
        }
        bool finished;
        void Finish()
        {
            if (finished) return; finished = true;
            var race = new Race { cls = cls, C = C };
            var rows = riders.Select(r => (r.name, r.done > 0 ? r.done - go : float.PositiveInfinity, r.me)).ToList();
            int place = Place(me); float ms = (finishT - go) * 1000;
            g.Later(() => { g.Close(); g.RaceResult(race, place, ms, me.done == 0, rows); });
        }

        // ---------------------------------------------------------------- input
        bool jumpPressed; readonly Dictionary<int, Vector2> ptr = new Dictionary<int, Vector2>();
        bool Held(Rect r) { foreach (var p in ptr.Values) if (r.Contains(p)) return true; return false; }
        public override void Pointer(Game gg, int id, Vector2 pos, bool down, bool up)
        {
            var gp = new Vector2(pos.x, Screen.height - pos.y);
            if (down) { if (exitR.Contains(gp)) { g.Later(g.Close); return; } if (jumpR.Contains(gp)) jumpPressed = true; if (chuR.Contains(gp)) chuIds.Add(id); }
            if (down || ptr.ContainsKey(id)) ptr[id] = gp;
            if (up) { ptr.Remove(id); chuIds.Remove(id); }
        }
        public override void Press(Game gg) { }

        // ---------------------------------------------------------------- drawing
        public override void Draw(Game gg)
        {
            var c = live.c; float t = Time.time;
            c.CopyFrom(L[0]);
            c.BlitRow(L[1], Mathf.RoundToInt(camX * 0.025f));
            c.BlitRow(L[2], Mathf.RoundToInt(camX * 0.1f));
            // the track repeats its first stretch until the finish comes into view, where the picture's own finish lines up
            float c0 = D - FINISH_IN_LAYER; int off = camX >= c0 ? Mathf.RoundToInt(camX - c0) : Mathf.RoundToInt(camX % TILE);
            if (camX < c0 && off > TILE - VW) { c.BlitRow(L[3], off); c.BlitRow(L[3], off - TILE); } else c.BlitRow(L[3], off);
            foreach (var o in obstacles)
            {
                int x = Mathf.RoundToInt(o.x - camX), y = Mathf.RoundToInt(o.y); if (x < -10 || x > VW + 10) continue;
                if (o.rock) { Px.Ell(c, x, y - 2, 4, 3, "#7a7a74"); Px.Ell(c, x - 1, y - 3, 2, 1, "#a6a69e"); }
                else { Px.Ell(c, x, y, 4, 2, "#3a2a1a"); Px.Ell(c, x, y - 1, 3, 1, "#1e140c"); Px.R(c, x + 4, y - 3, 2, 3, "#c49a5a"); }   // a marmot peeks out
            }
            foreach (var d in dust) { int a = (int)(140 * d.life / 0.6f); c.Put(Mathf.RoundToInt(d.x - camX), Mathf.RoundToInt(d.y), new Color32(214, 186, 130, (byte)a)); c.Put(Mathf.RoundToInt(d.x - camX) + 1, Mathf.RoundToInt(d.y) - 1, new Color32(214, 186, 130, (byte)(a / 2))); }
            foreach (var r in riders.OrderBy(r => r.y))
            {
                float jq = (t - r.jump) / 0.55f, z = jq >= 0 && jq < 1 ? Mathf.Sin(jq * Mathf.PI) * 9 : 0;
                bool moving = phase == "giingoo" || phase == "go" || (phase == "done" && r.done == 0) || r.done > 0;
                int face = phase == "giingoo" ? (Mathf.Sin((t - t0) * 0.9f + riders.IndexOf(r) * 1.05f) > 0 ? 1 : 2) : 2;
                StoryScene.DrawPerson(c, r.look, r.x - camX, r.y - z, face, moving, true, t, phase == "go" && (r.me ? sprint : r.surge > 0), r.stumble > 0 && (int)(t * 16) % 2 == 0);
            }
            g.Box(new Rect(0, 0, Screen.width, Screen.height), Color.black);
            float K = Mathf.Min(Screen.width / (float)VW, Screen.height / (float)VH);
            var pic = new Rect((Screen.width - VW * K) / 2, (Screen.height - VH * K) / 2, VW * K, VH * K);
            live.ShowAt(pic);
            Vector2 At(float wx, float wy) => new Vector2(pic.x + (wx - camX) * K, pic.y + wy * K);
            // names over the riders, and what the crowd shouts
            foreach (var r in riders) { var p = At(r.x, r.y - 40); g.Outlined(p.x, p.y, (r.me ? "★ " : "") + r.name, g.U(11), r.me ? Px.Col("#ffd23f") : Color.white); }
            foreach (var s in shouts)
            {
                float a = Mathf.Clamp01((1.8f - (t - s.t)) * 2);
                g.SpeechBubble(At(s.x, (s.finish ? 112 : 124) - (t - s.t) * 6), s.text, 200, a);   // a shout from the people watching
            }
            Hud(gg, pic);
            // the scene switch: from the village to the open steppe
            float sw = Mathf.Clamp01((t - opened) / 1.4f);
            if (sw < 1)
            {
                float close = 1 - Mathf.SmoothStep(0, 1, Mathf.Clamp01((sw - 0.45f) / 0.55f)), ww = Screen.width / 2f * close;
                g.Box(new Rect(0, 0, ww, Screen.height), Color.black); g.Box(new Rect(Screen.width - ww, 0, ww, Screen.height), Color.black);
                float ta = Mathf.Clamp01(sw * 4) * close;
                GUI.Label(new Rect(0, Screen.height / 2 - g.U(40), Screen.width, g.U(50)), Loc.L("МОРЬ УРАЛДААН", "HORSE RACE"), new GUIStyle(g.sBig) { fontSize = Mathf.RoundToInt(g.U(40)), normal = { textColor = new Color(1, 0.84f, 0.3f, ta) } });
                GUI.Label(new Rect(0, Screen.height / 2 + g.U(10), Screen.width, g.U(30)), $"{C.name} · {C.age} · {Km(C)} {Loc.L("км", "km")}", new GUIStyle(g.sTitle) { alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(1, 1, 1, ta) } });
            }
        }
        void Hud(Game gg, Rect pic)
        {
            float U = g.UiScale; var safe = Screen.safeArea; float ox = safe.x, oy = Screen.height - safe.yMax, vw = safe.width / U, vh = safe.height / U, t = Time.time;
            GUI.BeginGroup(new Rect(ox, oy, safe.width, safe.height));
            exitR = new Rect(ox + U * 8, oy + U * 8, U * 44, U * 44); GUI.Box(g.URect(8, 8, 44, 44), "×", g.sBtn);
            // the course: a bar with everyone's place on it
            var bar = g.URect(64, 14, Mathf.Min(vw - 140, 520), 14); g.Box(bar, new Color(0, 0, 0, 0.55f));
            foreach (var r in riders) { float k = Mathf.Clamp01(r.x / D); g.Box(new Rect(bar.x + k * (bar.width - U * 6), bar.y - U * (r.me ? 4 : 1), U * 6, bar.height + U * (r.me ? 8 : 2)), r.me ? Px.Col("#ffd23f") : Color.white); }
            g.Box(new Rect(bar.xMax - U * 3, bar.y - U * 4, U * 3, bar.height + U * 8), Px.Col("#d83a2a"));
            GUI.Label(g.URect(64, 32, 400, 22), $"{C.name} · {Mathf.RoundToInt(Mathf.Clamp01(me.x / D) * Km(C))}/{Km(C)} {Loc.L("км", "km")} · {Loc.L("Байр", "Place")} {Place(me)}/{riders.Count}", new GUIStyle(g.sPixel) { fontSize = PxSize(U * 13), alignment = TextAnchor.MiddleLeft, normal = { textColor = Color.white } });
            // stamina
            var sb = g.URect(64, 56, 160, 10); g.Box(sb, new Color(0, 0, 0, 0.55f)); g.Box(new Rect(sb.x + U, sb.y + U, (sb.width - 2 * U) * stam, sb.height - 2 * U), stam < 0.2f ? Px.Col("#e14b3d") : Px.Col("#5c9a36"));
            GUI.Label(g.URect(230, 52, 200, 18), Loc.L("Хүч", "Stamina"), new GUIStyle(g.sPixel) { fontSize = PxSize(U * 11), alignment = TextAnchor.MiddleLeft, normal = { textColor = Color.white } });
            // what is happening
            string line = phase == "count" ? (3 - Mathf.FloorToInt(t - t0)).ToString() : say;
            if (!string.IsNullOrEmpty(line)) GUI.Box(g.URect(vw / 2 - 220, vh - 60, 440, 40), line, new GUIStyle(g.sNote) { fontSize = phase == "count" ? g.Ts(22) : g.Ts(12) });
            if (phase == "go") GUI.Label(g.URect(8, vh - 26, 420, 20), Loc.L(TouchUI ? "▲▼ эгнээ · Чү! хурдлах · Үсрэх" : "W/S эгнээ · Shift/J Чү! · Space үсрэх", TouchUI ? "▲▼ lane · Chü! sprint · Jump" : "W/S lane · Shift/J Chü! · Space jump"), new GUIStyle(g.sPixel) { fontSize = PxSize(U * 11), alignment = TextAnchor.MiddleLeft, normal = { textColor = new Color(1, 1, 1, 0.8f) } });
            // touch buttons: lanes on the left, Chü! and jump on the right
            Rect B(float x, float y, float w, float h) { var r = g.URect(x, y, w, h); return r; }
            var ru = B(12, vh - 160, 60, 56); var rd = B(12, vh - 96, 60, 56); var rc = B(vw - 104, vh - 104, 92, 92); var rj = B(vw - 200, vh - 70, 88, 58);
            GUI.Box(ru, "▲", Held(upR) ? g.sPrimary : g.sBtn); GUI.Box(rd, "▼", Held(downR) ? g.sPrimary : g.sBtn);
            GUI.Box(rc, "<b>Чү!</b>", g.Fit(sprint ? g.sPrimary : g.sBtn, "<b>Чү!</b>", rc.width)); GUI.Box(rj, Loc.L("Үсрэх", "Jump"), g.Fit(g.sBtn, Loc.L("Үсрэх", "Jump"), rj.width));
            upR = Shift(ru, ox, oy); downR = Shift(rd, ox, oy); chuR = Shift(rc, ox, oy); jumpR = Shift(rj, ox, oy);
            GUI.EndGroup();
        }
        static Rect Shift(Rect r, float ox, float oy) => new Rect(r.x + ox, r.y + oy, r.width, r.height);
    }
}
