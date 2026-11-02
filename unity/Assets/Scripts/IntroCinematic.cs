// A short intro shown once on first launch, then a Story / Free Roam picker (AppState.gameMode).
// Story mode locks the heritage lessons in order (LearnPage reads StoryMode and LessonUnlocked); Free roam leaves them open.
using System;
using UnityEngine;

public sealed partial class Game
{
    public bool StoryMode => App.gameMode == "story";

    /** Lessons.ALL in file order are the chapter order; a story-mode player must finish the earlier ones before the next opens. */
    public static bool LessonUnlocked(Game g, string id)
    {
        if (!g.StoryMode) return true;
        foreach (var l in Lessons.ALL)
        {
            string lid = l["id"].S;
            if (lid == id) return true;
            var pr = g.V.learn.Find(x => x.id == lid);
            if (pr == null || !pr.done) return false;
        }
        return true;
    }

    public void OpenGameModePicker() => Open(new GameModeModal());
    public void OpenIntroCinematic(Action after = null) => Open(new IntroCinematicModal(after));

    sealed class IntroCinematicModal : Modal
    {
        readonly Action after;
        readonly string[] mn, en;
        int page; float t;
        const float PAGE_DUR = 3.2f;

        public IntroCinematicModal(Action after)
        {
            this.after = after;
            mn = new[]
            {
                "Нутаг — төрсөн тал нутаг чинь.",
                "Басартайгаа явж, өвөг дээдсийнхээ соёлыг сур.",
                "Хэрхэн суралцахаа одоо сонго.",
            };
            en = new[]
            {
                "Nutag — the land you were born into.",
                "Walk with Basar and learn the ways of your ancestors.",
                "Choose now how you want to learn.",
            };
        }

        public override float Width => 720;
        public override bool CanEscape => false;   // Esc or a tap outside would close it without ever calling after (a new player would skip making a character); Skip is the way out
        public override (string who, string face)? Speaker => ("bolormaa", "smile");

        public override void Tick(Game g, float dt)
        {
            t += dt;
            if (t >= PAGE_DUR) { t = 0; if (page + 1 >= mn.Length) g.Later(() => Finish(g)); else page++; }
        }

        void Finish(Game g) { if (g.modal is IntroCinematicModal) g.Swap(new GameModeModal(after)); }

        public override void Press(Game g)
        {
            g.Later(() => { if (page + 1 >= mn.Length) Finish(g); else { page++; t = 0; } });
        }

        public override void Draw(Game g)
        {
            int i = Mathf.Min(page, mn.Length - 1);
            g.Title(Loc.L("Наадамы", "Naadamy"));
            g.Small(Loc.L("Товч эхлэл", "A short opening"));
            g.Gap(16);
            float alpha = Mathf.Clamp01(t / 0.4f) * Mathf.Clamp01((PAGE_DUR - t) / 0.4f);
            var baseCol = g.sTitle.normal.textColor;
            var st = new GUIStyle(g.sTitle) { alignment = TextAnchor.MiddleCenter, wordWrap = true, fontSize = Mathf.RoundToInt(g.U(22)) };
            st.normal.textColor = new Color(baseCol.r, baseCol.g, baseCol.b, Mathf.Max(0.2f, alpha));
            GUILayout.Label(Loc.L(mn[i], en[i]), st, GUILayout.MinHeight(g.U(118)));
            g.Gap(8);
            GUILayout.Label($"{i + 1} / {mn.Length}", new GUIStyle(g.sSmall) { alignment = TextAnchor.MiddleCenter });
            g.Gap(6);
            GUILayout.BeginHorizontal();
            if (g.Btn(Loc.L("Алгасах", "Skip"), false, 36)) g.Later(() => Finish(g));
            GUILayout.FlexibleSpace();
            if (g.Btn(i + 1 == mn.Length ? Loc.L("Үргэлжлүүлэх →", "Continue →") : Loc.L("Цааш →", "Next →"), true, 36))
                g.Later(() => { if (page + 1 >= mn.Length) Finish(g); else { page++; t = 0; } });
            GUILayout.EndHorizontal();
        }
    }

    sealed class GameModeModal : Modal
    {
        readonly Action after;
        public GameModeModal(Action after = null) { this.after = after; }
        public override bool CanEscape => after == null;
        public override float Width => 640;
        public override (string who, string face)? Speaker => ("bolormaa", "curious");
        public override void Press(Game g) { if (after == null) g.Close(); }

        public override void Draw(Game g)
        {
            g.Title(Loc.L("Хэрхэн суралцах вэ?", "How would you like to learn?"));
            g.Small(Loc.L("Дараа нь цэснээс сольж болно.", "You can change this later from the menu."));
            string story = "<b><size=" + g.Ts(17) + ">" + Loc.L("Түүхийн горим", "Story mode") + "</size></b>\n" +
                           Loc.L("Хичээлийг нэг нэгээр нь дараалан сур. Өмнөх нь дуусмагц дараагийнх онгойно.",
                                 "Learn the lessons one by one. The next one opens when the previous is finished.");
            string roam = "<b><size=" + g.Ts(17) + ">" + Loc.L("Чөлөөт горим", "Free roam") + "</size></b>\n" +
                          Loc.L("Бүх хичээл нээлттэй. Юуг эхэнд сурахаа өөрөө шийд.",
                                "Every lesson is open. You choose what to learn first.");
            var st = new GUIStyle(g.sBtn) { alignment = TextAnchor.UpperCenter, fontSize = g.Ts(13), padding = new RectOffset(Mathf.RoundToInt(g.U(10)), Mathf.RoundToInt(g.U(10)), Mathf.RoundToInt(g.U(10)), Mathf.RoundToInt(g.U(12))) };
            var on = new GUIStyle(st) { normal = g.sPrimary.normal, hover = g.sPrimary.hover };
            int picked = -1;
            g.Grid("gameMode", 2, 210, (i, w) => Mathf.Max(g.U(140), st.CalcHeight(new GUIContent(i == 0 ? story : roam), w) + g.U(50)), (i, r) =>
            {
                bool sel = g.App.gameMode == (i == 0 ? "story" : "roam");
                if (GUI.Button(r, GUIContent.none, sel ? on : st)) picked = i;
                var ic = Emo.Icon(i == 0 ? "📖" : "🧭") ?? Px.Icon("ui:ger");
                if (ic != null) GUI.DrawTexture(new Rect(r.center.x - g.U(16), r.y + g.U(12), g.U(32), g.U(32)), g.Tex(ic), ScaleMode.ScaleToFit);
                GUI.Label(new Rect(r.x, r.y + g.U(46), r.width, r.height - g.U(46)), i == 0 ? story : roam,
                    new GUIStyle(st) { normal = { background = null, textColor = st.normal.textColor }, hover = { background = null }, active = { background = null } });
            }, 10);
            if (picked >= 0)
            {
                int k = picked;
                g.Later(() =>
                {
                    g.App.gameMode = k == 0 ? "story" : "roam";
                    g.App.introSeen = true; g.App.Save();
                    Sfx.Chime(); g.Close(); after?.Invoke();
                    g.Toast(k == 0 ? Loc.L("Түүхийн горим эхэллээ", "Story mode started") : Loc.L("Чөлөөт горим эхэллээ", "Free roam started"));
                });
            }
        }
    }
}
