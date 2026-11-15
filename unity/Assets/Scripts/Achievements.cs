// Амжилтууд: badges a child earns by learning, doing things with family, and playing in Нутаг (from js/achievements.js).
// Names and descriptions are in Resources/Data/achievements.json; the test for each badge is here.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static class Achievements
{
    public sealed class Ach { public string id, cat, emblem; public Tx name, desc; }
    static List<Ach> list;
    public static List<Ach> LIST => list ??= Data.Get("achievements")["LIST"].Select(a => new Ach { id = a["id"].S, cat = a["cat"].S, emblem = a["emblem"].S, name = a["name"].T, desc = a["desc"].T }).ToList();
    public static IEnumerable<(string id, Tx name)> CATS => Data.Get("achievements")["CATS"].Keys.Select(k => (k, Data.Get("achievements")["CATS"][k].T));
    public static Ach ById(string id) => LIST.FirstOrDefault(a => a.id == id);

    static int Lessons(SaveData v) => v.learn.Count(l => l.done);
    static int FamDone(SaveData v) => v.learn.Count(l => l.family > 0);
    static int Bc(SaveData v, string unit = null) => Bichig.ALL.Count(it => (unit == null || it.unit == unit) && SaveData.Get(v.bichig, it.id) > 0);
    static int Lvl(SaveData v) => Rules.Level(v.xp).l;
    static int Topics(SaveData v) => v.quizTopicRight.Count(t => t.v > 0);
    /** Is the badge earned, and how far along is it (null when it is a one-step badge)? */
    public static (bool ok, int[] prog) Test(string id, SaveData v, AppState app)
    {
        int nl = global::Lessons.Count;
        switch (id)
        {
            case "first_lesson": return (Lessons(v) >= 1, null);
            case "all_lessons": return (Lessons(v) >= nl, new[] { Lessons(v), nl });
            case "bichig_first": return (Bc(v) >= 1, null);
            case "bichig_vowels": return (Bc(v, "vowel") >= 7, new[] { Bc(v, "vowel"), 7 });
            case "bichig_master": return (Bc(v) >= Bichig.Count, new[] { Bc(v), Bichig.Count });
            case "bichig_memory": return (v.bichigMem.Count >= 5, new[] { v.bichigMem.Count, 5 });
            case "ger_master": return (v.LessonDone("ger"), null);
            case "dairy": return (v.LessonDone("idee"), null);
            case "manners": return (v.LessonDone("customs"), null);
            case "nuuts": return (v.LessonDone("nuuts"), null);
            case "perfect": return (v.learn.Any(l => l.best >= 3), null);
            case "quiz10": return (v.quizRight >= 10, new[] { v.quizRight, 10 });
            case "quiz50": return (v.quizRight >= 50, new[] { v.quizRight, 50 });
            case "curious": return (v.basarAsked >= 10, new[] { v.basarAsked, 10 });
            case "all_topics": { int n = Basar.Topics.Count(); return (Topics(v) >= n, new[] { Topics(v), n }); }
            case "ger_perfect": return (v.ger.perfect, null);
            case "emee": return (v.emee.visits >= 1, null);
            case "emee_perfect": return (v.emee.perfect >= 1, null);
            case "family1": return (FamDone(v) >= 1, null);
            case "family_all": return (FamDone(v) >= nl, new[] { FamDone(v), nl });
            case "with_family": return (SaveData.Get(v.together, "family") >= 1, null);
            case "with_friends": return (SaveData.Get(v.together, "friends") >= 1, null);
            case "stars20": return (app.familyStars >= 20, new[] { app.familyStars, 20 });
            case "memory": return (app.memories.Count >= 1, null);
            case "character": return (!string.IsNullOrEmpty(v.eth) && v.eth != "skip", null);
            case "passport1": return (v.stamps.Count >= 1, null);
            case "passport11": return (v.stamps.Count >= 11, new[] { v.stamps.Count, 11 });
            case "passport22": return (v.stamps.Count >= 22, new[] { v.stamps.Count, 22 });
            case "seasons4": return (v.seasonDone.Count >= 4, new[] { v.seasonDone.Count, 4 });
            case "level5": return (Lvl(v) >= 5, new[] { Lvl(v), 5 });
            case "taster": return (v.ate.Count >= 8, new[] { v.ate.Count, 8 });
            case "defender": return (v.campWon.Count >= 1, null);
            case "all_chapters": return (v.campWon.Count >= 4, new[] { v.campWon.Count, 4 });
            case "historian": return (v.histStep >= 5, new[] { v.histStep, 5 });
            case "ger_built": return (v.gerBuilt, null);
            case "fisher": return (v.fishCaught >= 5, new[] { v.fishCaught, 5 });
            case "rider": return (v.horse.dist >= 200, new[] { Mathf.FloorToInt(v.horse.dist), 200 });
            case "race_win": return (v.horse.wins >= 1, null);
            case "race_top5": return (v.horse.top5 >= 5, new[] { v.horse.top5, 5 });
            case "mergen": return (v.arch.best >= 6, new[] { v.arch.best, 6 });
            case "ulsyn_mergen": return (v.arch.best >= 12, new[] { v.arch.best, 12 });
            case "ikh_nas": return (v.horse.cls >= 4, new[] { Mathf.Min(v.horse.cls, 4), 4 });
            case "wr_first": return (v.wr.wins + v.wr.pvpW >= 1, null);
            case "techs7": return (v.wr.techs.Count >= 7, new[] { v.wr.techs.Count, 7 });
            case "nachin": return (v.wr.best >= 5, new[] { v.wr.best, 5 });
            case "zaan": return (v.wr.best >= 7, new[] { v.wr.best, 7 });
            case "arslan": return (v.wr.champ >= 1, null);
        }
        return (false, null);
    }
}

public sealed partial class Game
{
    readonly Queue<Achievements.Ach> achPop = new Queue<Achievements.Ach>(); Achievements.Ach achShow; float achT;
    /** Award any newly earned badges for this child; unless silent, each pops up with its badge. */
    public List<Achievements.Ach> CheckAchievements(bool silent = false)
    {
        var fresh = new List<Achievements.Ach>(); if (V == null) return fresh;
        foreach (var a in Achievements.LIST)
        {
            if (V.ach.Exists(x => x.k == a.id)) continue;
            bool ok; try { ok = Achievements.Test(a.id, V, App).ok; } catch (Exception) { ok = false; }
            if (ok) { V.ach.Add(new KV { k = a.id, v = SaveData.NowMs() }); fresh.Add(a); }
        }
        if (fresh.Count > 0) { V.Save(); foreach (var a in fresh) { App.LogActivity("ach", a.id, V.id); if (!silent) achPop.Enqueue(a); } }
        return fresh;
    }
    /** The toast for a new badge: the badge itself pops in at the top of the screen. */
    void AchievementPopup()
    {
        if (achShow == null && achPop.Count > 0) { achShow = achPop.Dequeue(); achT = Time.time; Sfx.Chime(); }
        if (achShow == null) return;
        float age = Time.time - achT; if (age > 3.6f) { achShow = null; return; }
        float slide = Mathf.Clamp01(age / 0.3f) * Mathf.Clamp01((3.6f - age) / 0.3f);
        float lw = U(300) - U(84); var s1 = new GUIStyle(sChipText) { fontSize = PxSize(U(11f) * 1.3f), normal = { textColor = Color.white } }; var s2 = new GUIStyle(sChipText) { wordWrap = true, normal = { textColor = Px.Col("#f7c646") } }; var nm = "<b>" + achShow.name + "</b>"; // dark ink on the dark toast was unreadable: white title, gold name
        float h1 = s1.CalcHeight(new GUIContent("Ag"), lw), h2 = s2.CalcHeight(new GUIContent(nm), lw); // the pixel font's lines are tall: size the card to its text
        var r = new Rect(Screen.width / 2f - U(150), U(8) - (1 - slide) * U(90), U(300), Mathf.Max(U(70), h1 + h2 + U(16))); GUI.Box(r, "", sToast);
        GUI.DrawTexture(new Rect(r.x + U(8), r.center.y - U(30), U(60), U(60)), Tex(Emo.Badge(achShow.emblem, achShow.cat)));
        GUI.Label(new Rect(r.x + U(76), r.y + U(8), lw, h1), Loc.L("Шинэ амжилт!", "New achievement!"), s1);
        GUI.Label(new Rect(r.x + U(76), r.y + U(8) + h1, lw, h2), nm, s2);
    }
    static PxCanvas Grey(PxCanvas c)
    {
        var g = new PxCanvas(c.W, c.H);
        for (int y = 0; y < c.H; y++) for (int x = 0; x < c.W; x++) { var p = c.Get(x, y); byte l = (byte)((p.r * 0.3f + p.g * 0.59f + p.b * 0.11f) * 0.7f + 60); g.Put(x, y, new Color32(l, l, l, (byte)(p.a * 0.55f))); }
        return g;
    }
    static readonly Dictionary<string, PxCanvas> greyBadges = new Dictionary<string, PxCanvas>();
    public PxCanvas BadgePic(Achievements.Ach a, bool on) { var b = Emo.Badge(a.emblem, a.cat); if (on) return b; if (!greyBadges.TryGetValue(a.id, out var g)) greyBadges[a.id] = g = Grey(b); return g; }

    /** The badge grid, by category (also shown on the parent dashboard). */
    public void BadgeGrid(SaveData v)
    {
        foreach (var (cat, name) in Achievements.CATS)
        {
            var list = Achievements.LIST.Where(a => a.cat == cat).ToList();
            GUILayout.Label($"<b>{name}</b>  <size={Ts(11)}>{list.Count(a => v.ach.Exists(x => x.k == a.id))}/{list.Count}</size>", sTitle);
            // equal tiles, each as tall as its words: the badge and its name, what to do for it, then the date or how far along you are
            var nameSt = new GUIStyle(sLabel) { fontSize = Ts(13) }; var desc = new GUIStyle(sSmall) { fontSize = Ts(11) };
            float pad = U(10), ic = U(40);
            float Measure(int i, float w)
            {
                var a = list[i]; bool on = v.ach.Exists(x => x.k == a.id);
                float nh = nameSt.CalcHeight(new GUIContent("<b>" + a.name + "</b>"), w - pad * 2 - ic - U(6)), dh = desc.CalcHeight(new GUIContent(a.desc.ToString()), w - pad * 2);
                bool bar = !on && Achievements.Test(a.id, v, App).prog != null;
                return pad + Mathf.Max(ic, nh) + U(4) + dh + (on ? desc.CalcHeight(new GUIContent("√ 2026.01.01"), w) + U(2) : bar ? U(12) : 0) + pad;
            }
            Grid("badges-" + cat, list.Count, 200, Measure, (i, r) =>
            {
                var a = list[i]; var got = v.ach.Find(x => x.k == a.id); bool on = got != null;
                GUI.Box(r, GUIContent.none, on ? sPanel : sBtn);
                GUI.DrawTexture(new Rect(r.x + pad, r.y + pad, ic, ic), Tex(BadgePic(a, on)), ScaleMode.ScaleToFit);
                float tx = r.x + pad + ic + U(6), tw = r.width - pad * 2 - ic - U(6);
                var nc = new GUIContent("<b>" + a.name + "</b>"); float nh = nameSt.CalcHeight(nc, tw);
                GUI.Label(new Rect(tx, r.y + pad + Mathf.Max(0, (ic - nh) / 2), tw, nh), nc, nameSt);
                float y = r.y + pad + Mathf.Max(ic, nh) + U(4); var dc = new GUIContent(a.desc.ToString()); float dh = desc.CalcHeight(dc, r.width - pad * 2);
                GUI.Label(new Rect(r.x + pad, y, r.width - pad * 2, dh), dc, desc); y += dh + U(2);
                if (on) GUI.Label(new Rect(r.x + pad, y, r.width - pad * 2, U(18)), "√ " + DateTimeOffset.FromUnixTimeMilliseconds(got.v).LocalDateTime.ToString("yyyy.MM.dd"), desc);
                else { var pr = Achievements.Test(a.id, v, App).prog; if (pr != null) Bar(new Rect(r.x + pad, y + U(2), r.width - pad * 2, U(8)), Mathf.Min(1, pr[0] / (float)pr[1]), Px.Col("#2ea35f")); }
            }, 8);
        }
    }
    sealed class AchievementsPage : Page
    {
        protected override string Tab => "achievements";
        protected override string Title => Loc.L("Миний амжилтууд", "My achievements");
        protected override void Body(Game g)
        {
            GUILayout.BeginHorizontal(g.sPanel); g.IconAt(Emo.Icon("🏅"), 56);
            GUILayout.BeginVertical(); GUILayout.Label($"<b>{g.V.ach.Count} / {Achievements.LIST.Count}</b>", g.sBig);
            g.Small(Loc.L("Сурч, гэр бүлтэйгээ хамт хийж, Нутагт тоглож амжилт цуглуулаарай. Ээж, аав чинь ч харж байгаа шүү!", "Learn, do things with your family and play in Nutag to collect badges. Your parents can see them too!"));
            GUILayout.EndVertical(); GUILayout.EndHorizontal();
            g.BadgeGrid(g.V);
        }
    }
}
