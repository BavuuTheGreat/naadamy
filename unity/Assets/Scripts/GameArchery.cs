// сур харваа: Mongolian archery. 3 rounds of 4 arrows: set the angle, then the power, and mind the wind.
using UnityEngine;

public sealed partial class Game
{
    sealed class ArcheryInfoModal : Modal
    {
        public override (string who, string face)? Speaker => ("archer", "proud");
        readonly Tx fact = Rules.Pick(Rules.ARCH_FACTS);
        public override void Draw(Game g)
        {
            var a = g.V.arch; string title = null; foreach (var x in Rules.ARCH_TITLES) if (a.best >= x.need) { title = x.t.ToString(); break; }
            GUILayout.BeginHorizontal(); g.IconAt(Px.Icon("ui:bow"), 56);
            GUILayout.BeginVertical(); g.Title(Loc.L("Сур харваа", "Mongolian archery")); g.Small(title != null ? "♦ " + title : Loc.L("Цолгүй харваач", "No title yet")); GUILayout.EndVertical(); GUILayout.EndHorizontal();
            g.Stats((Loc.L("Шилдэг оноо", "Best score"), $"{a.best}/12"), (Loc.L("Нийт оносон", "Total hits"), a.hits.ToString()), (Loc.L("Тоглолт", "Games"), a.games.ToString()));
            g.Small(Loc.L("3 үе, үе бүрт 4 сум. Эхлээд өнцгөө, дараа нь хүчээ сонгоно. Салхийг анхаараарай! 6 онвол “Мэргэн”, 9 бол “Гарамгай мэргэн”, 12 бүгдийг онвол “Улсын мэргэн” цол.", "3 rounds of 4 arrows. Set the angle, then the power, and watch the wind! Hit 6 for “Mergen”, 9 for “Garamgai mergen”, all 12 for “Ulsyn mergen”."));
            if (g.Btn(Loc.L("Харваж эхлэх", "Start shooting"), true, 52)) g.Later(() => g.Swap(new ArcheryModal(g)));
            g.Fact(fact.ToString());
        }
        public override void Press(Game g) => g.Swap(new ArcheryModal(g));
    }

    sealed class ArcheryModal : Modal
    {
        public override float Width => 4000; // as wide as the screen allows (DrawModal caps it)
        static readonly int[] DIST = { 92, 118, 142 }, TOL = { 13, 10, 8 };
        const int X0 = 34, GROUND = 94; const float G = 120;
        readonly Live live = new Live(192, 108); readonly Look look;
        int round, arrow, hits, wind; string phase = "aim", msg; float angle = 0.3f, power = 0.5f, t0, lockA = -1, cheer = -9, waitUntil;
        bool over, flying; float overT, sx, sy, svx, svy; readonly System.Collections.Generic.List<Vector2> trail = new System.Collections.Generic.List<Vector2>();
        readonly System.Collections.Generic.List<(float x, bool hit)> marks = new System.Collections.Generic.List<(float, bool)>();
        (int coins, int xp, int level) rw; string titleNow; bool newBest; Tx fact;
        public ArcheryModal(Game g)
        {
            look = g.V.look.Copy(); look.pet = null; look.ride = null;
            t0 = Time.time; NewWind(); msg = Loc.L("Зүү хамгийн зөв өнцөгт ирэхэд дар!", "Press when the line is at a good angle!");
        }
        void NewWind() { int m = new[] { 6, 14, 24 }[round]; wind = Mathf.RoundToInt((Random.value * 2 - 1) * m); }
        static float AngleOf(float q) => 0.12f + q * 0.75f; // ≈ 7°–50°
        public override void Tick(Game g, float dt)
        {
            float k = Time.time - t0;
            if (phase == "aim") angle = (Mathf.Sin(k * (1.3f + round * 0.25f)) + 1) / 2;
            if (phase == "power") power = (Mathf.Sin(k * (1.8f + round * 0.35f) - Mathf.PI / 2) + 1) / 2;
            if (phase == "fly" && flying)
            {
                for (int s = 0; s < Mathf.Max(1, Mathf.RoundToInt(dt * 120)); s++) { const float d2 = 1 / 60f; svx += wind * d2; svy += G * d2; sx += svx * d2; sy += svy * d2; trail.Add(new Vector2(sx, sy)); }
                if (sy >= GROUND - 2 || sx > 200) Land(g);
            }
            if (phase == "wait" && Time.time >= waitUntil) NextArrow(g);
        }
        public override void Press(Game g)
        {
            if (over) { if (Time.time - overT > 0.8f) g.Close(); return; } // the press that shot the last arrow doesn't skip the score
            if (phase == "aim") { lockA = angle; phase = "power"; t0 = Time.time; msg = Loc.L("Одоо хүчээ сонго!", "Now choose your power!"); }
            else if (phase == "power")
            {
                float a2 = AngleOf(lockA), v0 = 70 + power * 115;
                sx = X0; sy = GROUND - 16; svx = Mathf.Cos(a2) * v0; svy = -Mathf.Sin(a2) * v0; trail.Clear(); flying = true; phase = "fly"; Sfx.Pop(); g.V.arch.shots++;
            }
        }
        void Land(Game g)
        {
            phase = "wait"; waitUntil = Time.time + 1.2f;
            int TX = X0 + DIST[round], tol = TOL[round]; float off = sx - TX; bool hit = Mathf.Abs(off) <= tol;
            marks.Add((Mathf.Min(190, sx), hit));
            if (hit) { hits++; g.V.arch.hits++; cheer = Time.time; Sfx.Win(); g.Quest("archery"); msg = Loc.L("Оносон! Уухай!", "Hit! Uukhai!"); }
            else { Sfx.Bad(); msg = off < 0 ? Loc.L("Дутлаа. Өнцөг эсвэл хүчээ нэмээрэй.", "Short. Add more angle or power.") : Loc.L("Хэтэрлээ. Хүчээ бага зэрэг сулруулаарай.", "Too far. Use a little less power."); if (Mathf.Abs(wind) > 8) msg += " " + Loc.L("Салхийг тооцоорой!", "Mind the wind!"); }
        }
        void NextArrow(Game g)
        {
            flying = false; arrow++;
            if (arrow >= 4) { arrow = 0; round++; marks.Clear(); if (round >= 3) { Finish(g); return; } NewWind(); msg = Loc.L($"{round + 1}-р үе: бай холдож, салхи хүчтэй боллоо!", $"Round {round + 1}: the target is further and the wind stronger!"); }
            phase = "aim"; lockA = -1; t0 = Time.time;
        }
        void Finish(Game g)
        {
            over = true; overT = Time.time; phase = "done"; var a = g.V.arch;
            a.games++; newBest = hits > a.best; if (newBest) a.best = hits;
            foreach (var x in Rules.ARCH_TITLES) if (hits >= x.need) { titleNow = x.t.ToString(); break; }
            rw = g.Reward(hits * 4, hits * 6); g.V.Save(); fact = Rules.Pick(Rules.ARCH_FACTS);
            if (hits >= 6) { g.Confetti(70); Sfx.Win(); }
        }
        public override void Draw(Game g)
        {
            var x = live.c; float ts = Time.time; int r = Mathf.Min(round, 2);
            // scene: sky, steppe, judges, the low wall of сур, the archer
            Px.R(x, 0, 0, 192, 60, "#9fd8f7"); Px.R(x, 0, 40, 192, 20, "#b9e3f8");
            for (int X = 0; X < 192; X++) { int top = Js.Round(58 - 6 * Mathf.Abs(Mathf.Sin(X / 19f))); Px.R(x, X, top, 1, 62 - top, "#86b1cf"); }
            Px.R(x, 0, 60, 192, 48, "#76b947"); Px.R(x, X0 - 6, GROUND - 2, 192, 4, "#d8ab6c");
            int TX = X0 + DIST[r], tol = TOL[r];
            Px.R(x, TX - tol, GROUND - 2, tol * 2, 4, "rgba(92,154,54,.55)");
            string[] sur = { "#e14b3d", "#ffffff", "#3f76db" };
            for (int rr = 0; rr < 3; rr++) for (int c = 0; c < 6; c++) { Px.R(x, TX - 9 + c * 3, GROUND - 6 - rr * 3, 3, 3, "#2b1e2e"); Px.R(x, TX - 9 + c * 3, GROUND - 6 - rr * 3, 2, 2, sur[(c + rr) % 3]); }
            bool ch = ts - cheer < 1.4f;
            foreach (var (jx, col) in new[] { (TX + 18, "#2b5bd8"), (TX + 26, "#d8342b") })
            {
                Px.R(x, jx, GROUND - 10, 4, 6, col); Px.R(x, jx + 1, GROUND - 13, 2, 3, "#edbb93");
                if (ch) { Px.R(x, jx - 1, GROUND - 14 - (int)(ts / 0.15f) % 2, 1, 4, "#edbb93"); Px.R(x, jx + 4, GROUND - 14 - (int)(ts / 0.15f + 1) % 2, 1, 4, "#edbb93"); }
            }
            // wind flag
            Px.R(x, 96, 64, 1, 18, "#4a2a14"); int wl = Mathf.RoundToInt(wind / 3f); for (int k = 0; k < 4; k++) Px.R(x, wind >= 0 ? 97 : 96 + wl, 64 + k, Mathf.Abs(wl) + 1 - (k % 2), 1, "#e14b3d");
            foreach (var (mx2, hit) in marks) Px.R(x, Mathf.Round(mx2), GROUND - 4, 1, 4, hit ? "#f7c646" : "#6b4220");
            x.Blit(Px.Char(look, 2, 0), X0 - 14, GROUND - 28);
            float ang = AngleOf(phase == "aim" ? angle : lockA >= 0 ? lockA : angle); int bx = X0, by = GROUND - 16;
            for (float a = -ang - 1.2f; a <= -ang + 1.2f; a += 0.08f) Px.P(x, Mathf.Round(bx - 2 + Mathf.Cos(a) * 6), Mathf.Round(by + Mathf.Sin(a) * 6), "#6b4220"); // the bow
            if (phase == "aim" || phase == "power") for (int i = 0; i < 26; i++) if (i % 4 < 2) Px.P(x, Mathf.Round(bx + Mathf.Cos(ang) * i), Mathf.Round(by - Mathf.Sin(ang) * i), "rgba(43,30,46,.6)");
            if (phase == "power") { Px.R(x, 6, 30, 6, 52, "#2b1e2e"); int ph = Mathf.RoundToInt(power * 50); Px.R(x, 7, 81 - ph, 4, ph, "#f7c646"); }
            if (flying && trail.Count > 0)
            {
                var p = trail[trail.Count - 1]; var q = trail[Mathf.Max(0, trail.Count - 4)];
                int n = Mathf.CeilToInt((p - q).magnitude) + 1; for (int i = 0; i <= n; i++) { var m = Vector2.Lerp(q, p, i / (float)n); Px.P(x, Mathf.Round(m.x), Mathf.Round(m.y), "#4a2a14"); }
                Px.P(x, Mathf.Round(q.x), Mathf.Round(q.y), "#e14b3d");
            }
            g.Small(Loc.L("Сур харваа", "Mongolian archery"));
            live.Show(g, Mathf.Max(210, Screen.height / g.U(1) - 250)); // the field fills the screen, leaving room for the rows and buttons below
            if (ch) { var at = live.At(TX + 23, GROUND - 23); g.Outlined(at.x, at.y, Loc.L("Уухай!", "Uukhai!"), live.Scale * 9, Px.Col("#f7c646")); }
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>{Loc.L("Үе", "Round")} {Mathf.Min(round + 1, 3)}/3 · {Loc.L("Сум", "Arrow")} {Mathf.Min(arrow + 1, 4)}/4</b>", g.sLabel);
            GUILayout.FlexibleSpace();
            GUILayout.Label($"{Loc.L("Оносон", "Hits")} {hits} · {Loc.L("Салхи", "Wind")} {(wind == 0 ? "0" : (wind > 0 ? "→ " : "← ") + Mathf.Abs(wind))}", g.sLabel);
            GUILayout.EndHorizontal();
            g.Text(msg);
            if (!over)
            {
                string label = phase == "aim" ? Loc.L("Өнцөг сонгох (Space)", "Set angle (Space)") : phase == "power" ? Loc.L("Харвах! (Space)", "Shoot! (Space)") : "…";
                if (g.Btn(label, true, 52, phase == "aim" || phase == "power")) g.Later(() => Press(g));
                if (g.Btn(Loc.L("Хаах", "Close"), false, 30)) g.Later(g.Close);
                return;
            }
            GUILayout.BeginVertical(g.sBtn);
            GUILayout.Label($"<b>{hits}/12</b>" + (titleNow != null ? " · " + Loc.L($"«{titleNow}» цол хүртлээ!", $"You earned the “{titleNow}” title!") : ""), g.sLabel);
            if (newBest) g.Text(Loc.L("Шинэ дээд амжилт!", "New best score!"));
            g.Text($"+{rw.coins} {Loc.L("зоос", "coins")} · +{rw.xp} XP");
            GUILayout.EndVertical();
            g.Fact(fact.ToString());
            GUILayout.BeginHorizontal();
            if (g.Btn(Loc.L("Хаах", "Close"))) g.Later(g.Close);
            if (g.Btn(Loc.L("Дахин харвах", "Shoot again"), true)) g.Later(() => g.Swap(new ArcheryModal(g)));
            GUILayout.EndHorizontal();
        }
    }
}
