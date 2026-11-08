// Horse riding and the Naadam horse race: pick a horse at the уяа, ride freely, or race a lap of the town against three riders.
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed partial class Game
{
    void Mount(string coat = null)
    {
        if (coat != null) V.horse.coat = coat;
        riding = true; V.look.coat = V.horse.coat; V.Save(); Sfx.Pop();
    }
    void Dismount() { if (race != null) return; riding = false; V.Save(); Sfx.Tap(); }

    sealed class UyaModal : Modal
    {
        string coat; int cls;
        public UyaModal(Game g) { coat = g.V.horse.coat; cls = Mathf.Min(g.V.horse.cls, Rules.CLASSES.Length - 1); }
        public override (string who, string face)? Speaker => ("trainer", "happy");
        readonly Tx fact = Rules.Pick(Rules.HORSE_FACTS);
        public override float Width => 560;
        public override void Draw(Game g)
        {
            var hs = g.V.horse;
            GUILayout.BeginHorizontal(); g.IconAt(Px.Horse(coat, 0, true), 88);
            GUILayout.BeginVertical(); g.Title(Loc.L("Морины уяа", "Horse line")); g.Small(Loc.L("Морь унаж Нутгаар чөлөөтэй давхих, эсвэл Наадмын уралдаанд оролцоорой!", "Ride freely around Nutag, or join the Naadam horse race!")); GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            g.Gap(6); GUILayout.Label(Loc.L("Морио сонго", "Pick your horse"), g.sSmall);
            var coats = Px.COATS.Keys.ToList();
            int hit = g.Chips(coats.Select(k => Rules.COAT_NAMES[k].ToString()).ToList(), coats.IndexOf(coat), 70);
            if (hit >= 0) { var c = coats[hit]; g.Later(() => coat = c); }
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.L("Хурд", "Speed"), g.sSmall, GUILayout.Width(g.U(60))); g.Bar(GUILayoutUtility.GetRect(g.U(80), g.U(12), GUILayout.ExpandWidth(true)), hs.spd / 10f, Px.Col("#f7c646")); GUILayout.Label($"{hs.spd}/10", g.sSmall, GUILayout.Width(g.U(40)));
            GUILayout.Label(Loc.L("Тэсвэр", "Stamina"), g.sSmall, GUILayout.Width(g.U(60))); g.Bar(GUILayoutUtility.GetRect(g.U(80), g.U(12), GUILayout.ExpandWidth(true)), hs.sta / 10f, Px.Col("#2ea35f")); GUILayout.Label($"{hs.sta}/10", g.sSmall, GUILayout.Width(g.U(40)));
            GUILayout.EndHorizontal();
            g.Small(Loc.L("Уралдаан бүрийн дараа морь чинь дасгалжиж хүчирхэгжинэ.", "Your horse trains and gets stronger after every race."));
            g.Gap(6); GUILayout.Label(Loc.L("Уралдааны ангилал (морины нас)", "Race class (horse age)"), g.sSmall);
            g.Gap(4);
            for (int i = 0; i < Rules.CLASSES.Length; i++)
            {
                if (i % 3 == 0) { if (i > 0) { GUILayout.EndHorizontal(); g.Gap(6); } GUILayout.BeginHorizontal(); }
                var c = Rules.CLASSES[i]; bool open = i <= hs.cls;
                var cs = new GUIStyle(cls == i ? g.sPrimary : g.sBtn) { wordWrap = true, richText = true, alignment = TextAnchor.MiddleCenter, fontSize = g.Ts(11), padding = new RectOffset(4, 4, 2, 2) };
                var r = GUILayoutUtility.GetRect(g.U(60), g.U(58), GUILayout.ExpandWidth(true), GUILayout.Height(g.U(58)));
                bool was = GUI.enabled; GUI.enabled = open;
                if (GUI.Button(r, $"<b>{c.name}</b>\n<size={g.Ts(9)}>{c.age} · {c.laps} {Loc.L("тойрог", c.laps > 1 ? "laps" : "lap")}</size>", cs)) { int k = i; g.Later(() => cls = k); }
                GUI.enabled = was;
                if (i % 3 != 2 && i < Rules.CLASSES.Length - 1) GUILayout.Space(g.U(6));
            }
            GUILayout.EndHorizontal(); g.Gap(6);
            g.Small(Rules.CLASSES[cls].fact + " " + (hs.cls < Rules.CLASSES.Length - 1 ? Loc.L("Эхний гуравт орж дараагийн ангиллыг нээгээрэй.", "Finish in the top three to unlock the next class.") : ""));
            g.Stats((Loc.L("Уралдаан", "Races"), hs.races.ToString()), (Loc.L("Түрүү", "Wins"), hs.wins.ToString()), (Loc.L("Айргийн тав", "Top five"), hs.top5.ToString()));
            g.Gap(8); GUILayout.BeginHorizontal();
            if (g.riding) { if (g.Btn(Loc.L("Буух", "Get off"))) g.Later(() => { g.Close(); g.Dismount(); }); }
            else if (g.Btn(Loc.L("Морь унах", "Ride"))) g.Later(() => { g.Close(); g.Mount(coat); g.Toast(Loc.L("Чөлөөтэй давхиарай!", "Ride free!")); });
            if (g.Btn(Loc.L("Уралдах", "Race"), true)) g.Later(() => { g.Close(); g.V.horse.coat = coat; g.RaceStart(cls); });
            GUILayout.EndHorizontal();
            g.Fact(fact.ToString());
        }
        public override void Press(Game g) => g.Close();
    }

    // ---------------------------------------------------------------- the race
    sealed class RaceNpc { public string name; public float s, lane, speed, wob, surge, done, px, py; public int face = 2; public Look look; }
    sealed class Race
    {
        public int cls, ci = 1, lap = 1, lastCount = 99; public Rules.RaceClass C; public List<Vector2> P = new List<Vector2>(); public List<float> cum = new List<float>(); public float total;
        public List<RaceNpc> npcs = new List<RaceNpc>(); public string phase = "count", say, leader; public float t0, go, finishT, stam = 1; public bool sprint;
    }
    Race race;

    static Race RacePath(int laps)
    {
        var r = new Race();
        for (int l = 0; l < laps; l++) r.P.AddRange(World.LAP);
        r.P.Add(World.LAP[0]);
        r.cum.Add(0);
        for (int i = 1; i < r.P.Count; i++) r.cum.Add(r.cum[i - 1] + (r.P[i] - r.P[i - 1]).magnitude);
        r.total = r.cum[r.cum.Count - 1];
        return r;
    }
    static (float x, float y, int face) PointAt(Race r, float s, float lane)
    {
        int i = 1; while (i < r.P.Count - 1 && r.cum[i] < s) i++;
        var a = r.P[i - 1]; var b = r.P[i]; float segLen = r.cum[i] - r.cum[i - 1]; if (segLen == 0) segLen = 1;
        float q = Mathf.Max(i == 1 ? -0.5f : 0, Mathf.Min(1, (s - r.cum[i - 1]) / segLen)); // before the line on the start grid
        float dx = (b.x - a.x) / segLen, dy = (b.y - a.y) / segLen;
        return (a.x + (b.x - a.x) * q - dy * lane, a.y + (b.y - a.y) * q + dx * lane, dx < -0.1f ? 1 : dx > 0.1f ? 2 : 0);
    }
    /** Your horse's speed in the race: steady canter, or a "Чү!" sprint that uses stamina. */
    float RaceSpeed()
    {
        if (race == null || race.phase != "go") return 6;
        float cruise = 5.6f + V.horse.spd * 0.09f, sprint = cruise + 2.1f;
        return race.sprint && race.stam > 0.02f ? sprint : race.stam < 0.05f ? cruise - 0.7f : cruise;
    }
    /** A race: the scene switches to the open steppe (NaadamRace). */
    void RaceStart(int clsIdx)
    {
        if (race != null) return;
        CloseAll(); Open(new NaadamRace(this, clsIdx));
    }
    void RaceTick(float dt)
    {
        var r = race; float now = Time.time;
        r.sprint = ShiftHeld || sprintHeld;
        if (r.phase == "count")
        {
            int left = 3 - Mathf.FloorToInt(now - r.t0);
            if (left != r.lastCount) { r.lastCount = left; if (left > 0) Sfx.Tap(); else Sfx.Chime(); }
            if (now - r.t0 >= 3) { r.phase = "go"; r.go = now; r.say = Loc.L("Чү! Уралдаан эхэллээ!", "Chü! The race is on!"); }
            return;
        }
        if (r.phase != "go") return;
        // stamina: sprinting drains it, cantering refills it; a fitter horse tires more slowly
        bool mov = Mathf.Sqrt(vx * vx + vy * vy) > 0.5f;
        if (r.sprint && mov && r.stam > 0) r.stam = Mathf.Max(0, r.stam - dt * (0.3f - V.horse.sta * 0.017f));
        else r.stam = Mathf.Min(1, r.stam + dt * 0.11f);
        float el = now - r.go;
        foreach (var n in r.npcs)
        {
            if (n.done > 0) continue;
            if (n.surge <= 0 && Random.value < dt * 0.12f) n.surge = 2 + Random.value * 1.5f; // rivals make their own dashes
            if (n.surge > 0) n.surge -= dt;
            float sp = n.speed * (1 + 0.05f * Mathf.Sin(el * 1.3f + n.wob)) + (n.surge > 0 ? 1.1f : 0);
            n.s += sp * dt;
            if (n.s >= r.total) { n.s = r.total; n.done = now; }
            var p = PointAt(r, n.s, n.lane); n.px = p.x - 0.5f; n.py = p.y - 0.82f; if (p.face != 0) n.face = p.face;
        }
        float fx = px + 0.5f, fy = py + 0.82f;
        if (r.ci < r.P.Count && (r.P[r.ci] - new Vector2(fx, fy)).magnitude < 1.9f)
        {
            r.ci++; Sfx.Pop();
            int lap = 1 + (r.ci - 1) / World.LAP.Length;
            if (lap > r.lap && r.ci < r.P.Count) { r.lap = lap; r.say = lap == r.C.laps ? Loc.L("Сүүлийн тойрог!", "Final lap!") : Loc.L($"{lap}-р тойрог!", $"Lap {lap}!"); }
            if (r.ci >= r.P.Count) { RaceFinish(false); return; }
        }
        // commentary when the leader changes
        string leader = RacePlace() == 1 ? MyName : r.npcs.OrderByDescending(n => n.s).First().name;
        if (leader != r.leader && el > 2) { r.leader = leader; r.say = leader == MyName ? Loc.L("Та тэргүүлж байна!", "You are in the lead!") : Loc.L($"{leader} тэргүүлж байна!", $"{leader} takes the lead!"); }
        if (el > 40 + r.C.laps * 40) RaceFinish(true);
    }
    int RacePlace()
    {
        var r = race; float fx = px + 0.5f, fy = py + 0.82f;
        float mine = r.ci >= r.P.Count ? r.total + 1 : r.cum[r.ci] - (r.P[r.ci] - new Vector2(fx, fy)).magnitude;
        return 1 + r.npcs.Count(n => (n.done > 0 && (r.finishT == 0 || n.done < r.finishT)) || (n.done == 0 && n.s > mine));
    }
    void RaceFinish(bool timeout)
    {
        var r = race; if (r == null || r.phase == "done") return;
        r.phase = "done"; r.finishT = Time.time; r.sprint = false;
        float ms = (r.finishT - r.go) * 1000; int place = timeout ? r.npcs.Count + 1 : RacePlace();
        var rows = new List<(string name, float t, bool me)> { (MyName, timeout ? float.PositiveInfinity : ms / 1000, true) };
        foreach (var n in r.npcs) rows.Add((n.name, n.done > 0 ? n.done - r.go : float.PositiveInfinity, false));
        RaceResult(r, place, ms, timeout, rows);
    }
    /** The end of any race: the horse gets better, the class may open up, the reward, and the results window. */
    void RaceResult(Race r, int place, float ms, bool timeout, List<(string name, float t, bool me)> rows)
    {
        var hs = V.horse;
        hs.races++; if (place == 1) hs.wins++; if (place <= 5) hs.top5++;
        // training: every race makes the horse a little faster or tougher
        bool spd = Random.value < 0.5f; if (spd) hs.spd = Mathf.Min(10, hs.spd + 1); else hs.sta = Mathf.Min(10, hs.sta + 1);
        Rules.RaceClass unlocked = null;
        if (place <= 3 && r.cls == hs.cls && hs.cls < Rules.CLASSES.Length - 1) { hs.cls++; unlocked = Rules.CLASSES[hs.cls]; }
        string key = "best_" + r.C.id; long best = hs.bests.Find(b => b.k == key)?.v ?? 0; bool newBest = !timeout && (best == 0 || ms < best);
        if (newBest) { hs.bests.RemoveAll(b => b.k == key); hs.bests.Add(new KV { k = key, v = (long)ms }); }
        if (!timeout && (hs.best == 0 || ms < hs.best)) hs.best = (int)ms;
        int[][] pay = { new[] { 40, 60 }, new[] { 25, 40 }, new[] { 15, 30 }, new[] { 5, 15 } }; var pp = place - 1 < pay.Length ? pay[place - 1] : pay[3]; float boost = 1 + r.cls * 0.25f;
        var rw = Reward(Mathf.RoundToInt(pp[0] * boost), Mathf.RoundToInt(pp[1] * boost)); V.Save();
        Quest("race");
        if (place == 1) { Sfx.Win(); Confetti(90); } else Sfx.Chime();
        rows.Sort((a, b) => a.t.CompareTo(b.t));
        Open(new RaceResultModal(r, place, rows, newBest, rw, spd, unlocked));
    }
    sealed class RaceResultModal : Modal
    {
        readonly Race r; readonly int place; readonly List<(string name, float t, bool me)> rows; readonly bool newBest, spd; readonly (int coins, int xp, int level) rw; readonly Rules.RaceClass unlocked;
        public RaceResultModal(Race r, int place, List<(string, float, bool)> rows, bool newBest, (int, int, int) rw, bool spd, Rules.RaceClass unlocked) { this.r = r; this.place = place; this.rows = rows; this.newBest = newBest; this.rw = rw; this.spd = spd; this.unlocked = unlocked; }
        public override void Draw(Game g)
        {
            g.Picture(Px.Icon("ui:trophy"), 56);
            GUILayout.Label($"{r.C.name} · {r.C.age} · {NaadamRace.Km(r.C)} {Loc.L("км", "km")}", new GUIStyle(g.sSmall) { alignment = TextAnchor.MiddleCenter });
            string title = place == 1 ? Loc.L("Түрүү морь!", "Winning horse!") : place <= 5 ? Loc.L($"{place}-р байр · Айргийн тав!", $"Place {place} · top five!") : Loc.L("Баян ходоод!", "Bayan khodood!");
            GUILayout.Label(title, new GUIStyle(g.sTitle) { alignment = TextAnchor.MiddleCenter });
            for (int i = 0; i < rows.Count; i++) { var x = rows[i]; GUILayout.BeginHorizontal(x.me ? g.sPrimary : g.sBtn); GUILayout.Label($"<b>{i + 1}</b>", x.me ? g.sPrimary.Clean() : g.sLabel, GUILayout.Width(g.U(24))); GUILayout.Label(x.name, x.me ? g.sPrimary.Clean() : g.sLabel); GUILayout.FlexibleSpace(); GUILayout.Label(float.IsInfinity(x.t) ? "–" : x.t.ToString("0.0") + Loc.L("с", "s"), x.me ? g.sPrimary.Clean() : g.sSmall); GUILayout.EndHorizontal(); }
            if (newBest) g.Text(Loc.L("Шинэ дээд амжилт!", "New personal best!"));
            g.Text($"+{rw.coins} {Loc.L("зоос", "coins")} · +{rw.xp} XP");
            g.Small(spd ? Loc.L("Морь чинь хурдан боллоо! (Хурд +1)", "Your horse got faster! (Speed +1)") : Loc.L("Морь чинь тэсвэртэй боллоо! (Тэсвэр +1)", "Your horse got tougher! (Stamina +1)"));
            if (unlocked != null) g.Text("<b>" + Loc.L($"Шинэ ангилал нээгдлээ: {unlocked.name}!", $"New class unlocked: {unlocked.name}!") + "</b>");
            g.Fact((place == 1 ? new Tx("Түрүүлсэн морины толгой, сүүл, гуяыг айргаар цацаж ерөөдөг. Унаач хүүхдийг ч магтдаг.", "The winning horse is blessed by sprinkling airag on its head, tail and back, and the young jockey is praised.") : Rules.HORSE_FACTS[place >= 4 ? 2 : 1]).ToString());
            GUILayout.BeginHorizontal();
            if (g.Btn(Loc.L("Чөлөөтэй давхих", "Ride freely"))) g.Later(g.Close);
            if (g.Btn(unlocked != null ? Loc.L("Шинэ ангилалд уралдах", "Race the new class") : Loc.L("Дахин уралдах", "Race again"), true)) { int c = unlocked != null ? g.V.horse.cls : r.cls; g.Later(() => { g.Close(); g.RaceStart(c); }); }
            GUILayout.EndHorizontal();
        }
        public override void Closed(Game g) { g.race = null; }
    }

    void RaceDraw(float t)
    {
        foreach (var n in race.npcs) Rider(n.look, n.face, race.phase == "go" && n.done == 0, n.px, n.py, n.py, 0, false, null, "race" + n.name);
        int fl = (int)(t * 4) % 2;
        for (int i = Mathf.Max(1, race.ci); i < Mathf.Min(race.P.Count, race.ci + 4); i++)
        {
            var p = race.P[i]; bool last = i == race.P.Count - 1;
            Spr(Px.RaceFlag(last ? "#e14b3d" : i == race.ci ? "#f7c646" : "#ffffff", fl), (p.x - 0.3f) * T, (p.y - 1) * T - 15, false, p.y - 0.9f);
        }
    }
    void RaceLabels(float ts)
    {
        if (race.phase == "go") foreach (var n in race.npcs) { var p = PxGui(n.px * T + 8, n.py * T - 22); Outlined(p.x, p.y, n.name, ts * 0.8f, Color.white); }
        if (race.phase == "done") return;
        // an arrow over you points at the next flag
        var nx = race.P[Mathf.Min(race.ci, race.P.Count - 1)]; float ang = Mathf.Atan2(nx.y - (py + 0.82f), nx.x - (px + 0.5f)) * Mathf.Rad2Deg;
        var a = PxGui(px * T + 8, py * T - 36); var m = GUI.matrix;
        GUIUtility.RotateAroundPivot(ang, a);
        Outlined(a.x, a.y, "►", ts * 1.8f, Px.Col("#f7c646"));
        GUI.matrix = m;
    }
    void RaceHud(float vw, float vh)
    {
        var r = race;
        var panel = UR(vw / 2 - 150, 56, 300, r.phase == "count" ? 96 : 84); GUI.Box(panel, GUIContent.none, sToast);
        if (r.phase == "count") { int n = 3 - Mathf.FloorToInt(Time.time - r.t0); GUI.Label(UR(vw / 2 - 150, 60, 300, 50), n > 0 ? n.ToString() : Loc.L("Чү!", "Go!"), new GUIStyle(sBig) { normal = { textColor = Px.Col("#f7c646") } }); }
        else
        {
            float tt = (r.phase == "done" ? r.finishT : Time.time) - r.go;
            var st = new GUIStyle(sChipText) { alignment = TextAnchor.MiddleCenter };
            GUI.Label(UR(vw / 2 - 140, 60, 90, 24), $"{Mathf.Min(r.lap, r.C.laps)}/{r.C.laps}", st);
            GUI.Label(UR(vw / 2 - 45, 60, 90, 24), $"<size={Ts(18)}>{RacePlace()}/{r.npcs.Count + 1}</size>", st);
            GUI.Label(UR(vw / 2 + 50, 60, 90, 24), tt.ToString("0.0"), st);
            Bar(UR(vw / 2 - 130, 88, 260, 10), r.stam, r.stam < 0.2f ? Px.Col("#e14b3d") : Px.Col("#f7c646"));
        }
        var sayS = new GUIStyle(sChipText) { alignment = TextAnchor.UpperCenter, wordWrap = true, fontSize = Ts(11) };
        GUI.Label(new Rect(U(vw / 2 - 145), U(r.phase == "count" ? 110 : 100), U(290), Mathf.Max(U(36), sayS.CalcHeight(new GUIContent(r.say), U(290)))), r.say, sayS);
        sprintRect = Rect.zero;
        if (r.phase == "go")
        {
            // any finger on the button sprints, so one finger can steer with the joystick while another holds this
            var b = UR(vw - 166, vh - 64, 158, 56); Ui(b); sprintRect = new Rect(b.x + guiOX, b.y + guiOY, b.width, b.height);
            GUI.Box(b, Loc.L("Чү! (Shift)", "Chü! (Shift)"), Fit(sprintHeld ? new GUIStyle(sPrimary) { normal = sPrimary.active } : sPrimary, Loc.L("Чү! (Shift)", "Chü! (Shift)"), b.width));
        }
    }
}
