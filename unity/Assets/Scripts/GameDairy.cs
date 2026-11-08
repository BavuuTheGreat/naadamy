// хот айл windows: milking, the dairy stations where the white foods are made, and the daily quest board.
using System.Collections.Generic;
using UnityEngine;

public sealed partial class Game
{
    // ---------------------------------------------------------------- milking: let the young one suckle to start the milk, then squeeze in rhythm in the green
    sealed class MilkModal : Modal
    {
        const int NEED = 5; const float ZA = 0.36f, ZB = 0.64f;
        readonly string id, kind; readonly Rules.Animal A; readonly Live live = new Live(180, 110);
        string phase = "young", msg; int good; float pos, dirn = 1, nuzzle = -9, doneT; bool done, first;
        public override (string who, string face)? Speaker => ("farmer", done ? "laugh" : "happy");
        public MilkModal(string id, string kind) { this.id = id; this.kind = kind; A = Rules.DAIRY_ANIMALS[kind]; SetMsg(); }
        void SetMsg() => msg = phase == "young"
            ? Loc.L($"Эхлээд төлийг нь ({A.young.mn.ToLower()}) хөхүүлж сүүг нь ивлүүлье.", $"First let the {A.young.en.ToLower()} suckle so the milk flows.")
            : Loc.L("Одоо саа! Шар тэмдэг ногоон дотор орох үед дар. Зөөлөн, тайван.", "Now milk! Press when the yellow marker is in the green. Gently and calmly.");
        public override void Tick(Game g, float dt)
        {
            if (phase == "young" && nuzzle > 0 && Time.time - nuzzle > 1.4f) { phase = "milk"; SetMsg(); }
            if (phase == "milk" && !done) { pos += dirn * 0.018f * dt * 60; if (pos > 1) { pos = 1; dirn = -1; } if (pos < 0) { pos = 0; dirn = 1; } }
        }
        public override void Press(Game g)
        {
            if (done) { if (Time.time - doneT > 0.8f) g.Close(); return; } // a press still held from milking doesn't skip the result
            if (phase == "young") { if (nuzzle < 0) { nuzzle = Time.time; Sfx.Pop(); } return; }
            if (pos >= ZA && pos <= ZB) { good++; Sfx.Pour(0.3f + good * 0.1f); msg = Loc.L("Сайн байна!", "Nice!") + " " + new string('●', good); }
            else { good = Mathf.Max(0, good - 1); Sfx.Tap(); msg = Loc.L("Арай эрт/хожуу. Тайван, хэмнэлтэй саа.", "A bit early or late. Calm and steady."); } // a miss takes one back: mashing the button doesn't fill the pail
            if (good >= NEED) Finish(g);
        }
        void Finish(Game g)
        {
            done = true; doneT = Time.time; var V = g.V; V.SetMilked(id, SaveData.NowMs()); V.Stock(A.gives, A.n); g.Quest("milk"); g.Reward(2, 6);
            first = V.FirstTime("milk_" + kind); V.Save(); Sfx.Good();
        }
        public override void Draw(Game g)
        {
            if (done)
            {
                g.Picture(Px.Icon("dairy:" + A.gives), 56);
                GUILayout.Label($"+{A.n} {Rules.DAIRY[A.gives].name}", g.sTitle); g.Fact(A.fact.ToString(), first); g.Gap();
                if (g.Btn(Loc.L("Гайхалтай!", "Great!"), true)) g.Later(g.Close);
                return;
            }
            g.Title(Loc.L($"{A.name.mn} саах", $"Milk the {A.name.en.ToLower()}"));
            var x = live.c; float t = Time.time;
            Px.R(x, 0, 0, 180, 110, "#b89462"); Px.R(x, 0, 0, 180, 34, "#9fd8f7"); Px.R(x, 0, 34, 180, 8, "#76b947");
            var mom = Px.Beast(kind, 0); float sc = kind == "goat" ? 3 : 2.6f;
            x.BlitScaled(mom, 34, 76 - mom.H * sc, mom.W * sc, mom.H * sc);
            // the young one: a foal for the mare; the calf and the kid are their mother, small (the game has no calf or kid sprite)
            var young = kind == "mare" ? Px.Beast("foal", 0) : mom; float ys = kind == "mare" ? 2 : sc * 0.5f;
            bool suck = phase == "young" && nuzzle > 0 && t - nuzzle < 1.4f;
            if (phase == "young" || suck) x.BlitScaled(young, suck ? 70 : 128, 76 - young.H * ys, young.W * ys, young.H * ys);
            Px.R(x, 96, 62, 22, 16, "#2b1e2e"); Px.R(x, 98, 63, 18, 14, "#a5a5b0");
            int fill = Js.Round(good / (float)NEED * 13); Px.R(x, 98, 77 - fill, 18, fill, "#ffffff"); // the pail filling up
            if (phase == "milk")
            {
                const float bx = 14, bw = 152;
                Px.R(x, bx - 2, 88, bw + 4, 14, "#2b1e2e"); Px.R(x, bx, 90, bw, 10, "#e6c48a"); Px.R(x, bx + ZA * bw, 90, (ZB - ZA) * bw, 10, "#5c9a36");
                Px.R(x, Js.Round(bx + pos * bw) - 2, 86, 4, 18, "#f7c646");
            }
            live.Show(g, 200);
            g.Text(msg); g.Gap(6);
            string label = phase == "young" ? Loc.L($"{A.young.mn} тавих", "Let it suckle") : Loc.L("Саах (Space)", "Squeeze (Space)");
            if (g.Btn(label, true, 52, phase == "milk" || nuzzle < 0)) g.Later(() => Press(g));
            if (g.Btn(Loc.L("Хаах", "Close"), false, 32)) g.Later(g.Close);
        }
    }

    // ---------------------------------------------------------------- dairy stations: pick a recipe you have the milk for, then do the real steps
    sealed class StationModal : Modal
    {
        readonly string st; Rules.Recipe r; int step, taps; float doneT; bool done, first;
        public StationModal(string st) { this.st = st; }
        string Name() => st == "rack" ? Loc.L("Ааруулын тавиур", "Aaruul drying rack") : st == "tulga" ? Loc.L("Тулга: сүү буцалгах", "Outdoor stove: boil milk") : Loc.L("Хөхүүр: айраг бүлэх", "Khukhuur: churn airag");
        bool Has(Game g, Rules.Recipe q) { foreach (var kv in q.need) if (g.V.Have(kv.Key) < kv.Value) return false; return true; }
        public override void Press(Game g) { if (done) { if (Time.time - doneT > 0.8f) g.Close(); } else if (r != null) Next(g); } // churning taps don't skip the result
        void Next(Game g)
        {
            bool mashing = r.mash && step == 1;
            if (mashing) { taps++; Sfx.Pour(0.2f + 0.6f * taps / r.taps); if (taps < r.taps) return; } else Sfx.Tap();
            step++;
            if (step < r.steps.Length) return;
            foreach (var kv in r.need) g.V.Stock(kv.Key, -kv.Value);
            g.V.Stock(r.id, r.gives); g.Quest("dairy"); g.Reward(3, 10);
            first = g.V.FirstTime("made_" + r.id); g.V.Save(); Sfx.Good(); if (first) g.Confetti(40); done = true; doneT = Time.time;
        }
        public override void Draw(Game g)
        {
            var V = g.V;
            if (r == null)
            {
                g.Title(Name());
                g.Small(Loc.L("Танд: ", "You have: ") + $"{Rules.DAIRY["milk"].name} {V.Have("milk")} · {Rules.DAIRY["mare"].name} {V.Have("mare")}");
                g.Gap();
                foreach (var q in Rules.RECIPES[st])
                {
                    bool ok = Has(g, q); var d = Rules.DAIRY[q.id];
                    var need = new List<string>(); foreach (var kv in q.need) need.Add($"{kv.Value} {Rules.DAIRY[kv.Key].name}");
                    g.Row(Px.Icon("dairy:" + q.id), $"{d.name} ×{q.gives}", string.Join(" + ", need), !ok);
                    if (g.SmallBtn(ok ? Loc.L("Хийх", "Make") : Loc.L("Саах хэрэгтэй", "Milk first"), true, 120, ok)) { var pick = q; string at = st; g.Later(() => { g.Close(); Sfx.Tap(); g.StartCoroutine(g.MakeDairy(at, pick)); }); }
                    g.EndRow();
                }
                g.Gap(); g.Small(Loc.L("Сүү авахын тулд үнээ, ямаа, гүүгээ саагаарай.", "Milk the cows, goats and mares to get milk."));
                if (g.Btn(Loc.L("Хаах", "Close"), false, 32)) g.Later(g.Close);
                return;
            }
            var dd = Rules.DAIRY[r.id];
            if (done)
            {
                g.Picture(Px.Icon("dairy:" + r.id), 56);
                GUILayout.Label($"+{r.gives} {dd.name}", g.sTitle); g.Fact(r.fact.ToString()); g.Gap();
                GUILayout.BeginHorizontal();
                if (g.Btn(Loc.L("Дахиад хийх", "Make more"))) g.Later(() => { r = null; done = false; });
                if (g.Btn(Loc.L("Боллоо", "Done"), true)) g.Later(g.Close);
                GUILayout.EndHorizontal();
                return;
            }
            g.Title(dd.name.ToString());
            for (int i = 0; i < r.steps.Length; i++)
                GUILayout.Label((i < step ? "<color=#2ea35f>√</color> " : i == step ? "<b>► " : "<color=#9a8a7a>") + $"{i + 1}. {r.steps[i]}" + (i < step ? "" : i == step ? "</b>" : "</color>"), g.sLabel);
            bool mashing = r.mash && step == 1;
            if (mashing) { g.Gap(4); g.Bar(GUILayoutUtility.GetRect(10, g.U(14), GUILayout.ExpandWidth(true)), taps / (float)r.taps, Px.Col("#f7c646")); }
            g.Gap();
            if (g.Btn(mashing ? $"{r.verb}! {taps}/{r.taps}" : $"{step + 1}. {r.steps[step]}  √", true, 52)) g.Later(() => Next(g));
        }
    }

    /** Every step done by hand on the task stage (DairyWork); the milk is used only once the food is made. */
    System.Collections.IEnumerator MakeDairy(string st, Rules.Recipe r)
    {
        bool made = false; yield return DairyWork.Make(this, r.id, ok => made = ok);
        if (!made || V == null) yield break;
        foreach (var kv in r.need) if (V.Have(kv.Key) < kv.Value) { Toast(Loc.L("Сүү хүрэлцэхгүй байна.", "Not enough milk.")); yield break; }
        foreach (var kv in r.need) V.Stock(kv.Key, -kv.Value);
        V.Stock(r.id, r.gives); Quest("dairy"); Reward(3, 10);
        bool first = V.FirstTime("made_" + r.id); V.Save(); Sfx.Good(); if (first) Confetti(40);
        if (first) HandsOnFirst(r.id);
        CheckAchievements();
        Open(new DairyDoneModal(st, r));
    }
    sealed class DairyDoneModal : Modal
    {
        readonly string st; readonly Rules.Recipe r; public DairyDoneModal(string st, Rules.Recipe r) { this.st = st; this.r = r; }
        public override void Draw(Game g)
        {
            g.Picture(Px.Icon("dairy:" + r.id), 56);
            GUILayout.Label($"+{r.gives} {Rules.DAIRY[r.id].name}", new GUIStyle(g.sTitle) { alignment = TextAnchor.MiddleCenter }); g.Fact(r.fact.ToString(), true, Loc.L("Мэдсэн үү?", "Did you know?"));
            g.HandsOnCard(r.id);
            GUILayout.BeginHorizontal();
            if (g.Btn(Loc.L("Дахиад хийх", "Make more"))) g.Later(() => g.Swap(new StationModal(st)));
            if (g.Btn(Loc.L("Боллоо", "Done"), true)) g.Later(g.Close);
            GUILayout.EndHorizontal();
        }
        public override void Press(Game g) => g.Close();
    }

    // ---------------------------------------------------------------- the quest board
    sealed class QuestModal : Modal
    {
        public override void Draw(Game g)
        {
            g.V.questsSeen = SaveData.DayKey();
            g.Title(Loc.L("Өнөөдрийн даалгавар", "Today's quests"));
            g.Small(Loc.L("Даалгавар өдөр бүр шинэчлэгдэнэ.", "Quests refresh every day."));
            if (g.Btn(Loc.L($"Улирал: {Seasons.Current().name}", $"Season: {Seasons.Current().name}"), false, 30)) g.Later(() => g.Open(new SeasonModal()));
            g.Gap();
            foreach (var q in g.Quests())
            {
                var d = Rules.QUESTS[q.k]; bool ready = q.p >= d.n && !q.done;
                GUILayout.BeginHorizontal(g.sBtn);
                g.IconAt(Px.Icon(q.done ? "ui:star" : "ui:scroll"), 28);
                GUILayout.BeginVertical();
                GUILayout.Label((q.done ? "<color=#2ea35f>√</color> " : "") + $"<b>{d.t}</b>" + (q.season ? $"  <size={g.Ts(10)}><color=#c46d12>{Seasons.Current().name} ×1.5</color></size>" : ""), g.sLabel);
                g.Bar(GUILayoutUtility.GetRect(10, g.U(10), GUILayout.ExpandWidth(true)), q.p / (float)d.n, Px.Col("#2ea35f"));
                float mul = q.season ? 1.5f : 1;
                GUILayout.Label($"{q.p}/{d.n} · +{Mathf.RoundToInt(d.c * mul)} {Loc.L("зоос", "coins")} · +{Mathf.RoundToInt(d.x * mul)} XP", g.sSmall);
                GUILayout.EndVertical();
                if (ready && g.SmallBtn(Loc.L("Авах", "Claim"), true, 90)) { var qq = q; g.Later(() => { if (qq.done) return; qq.done = true; float m = qq.season ? 1.5f : 1; if (qq.season) SaveData.Add(g.V.seasonDone, Seasons.Current().id, 1); g.Reward(Mathf.RoundToInt(d.c * m), Mathf.RoundToInt(d.x * m)); g.Confetti(40); Sfx.Good(); g.CheckAchievements(); }); }
                GUILayout.EndHorizontal();
            }
            g.Gap();
            if (g.Btn(Loc.L("Хаах", "Close"), false, 32)) g.Later(g.Close);
        }
        public override void Press(Game g) => g.Close();
    }
}
