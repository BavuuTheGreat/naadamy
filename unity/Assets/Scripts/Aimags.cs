// The 21 provinces and Ulaanbaatar, Mongolia's peoples, the aimag passport and the character creator.
// Ported from js/aimags.js, js/ethnic.js, js/passport.js and the travel pass of js/nomin.js (texts in Resources/Data/aimags.json).
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static class Aimags
{
    public sealed class Aimag { public string id, icon; public Tx name, center, fact; }
    public sealed class Ethnic { public string id, hat, deel; public Tx name, fact; public List<string> aimags; }
    public sealed class Mark { public Tx name, desc; }
    public sealed class Trip { public List<string> land; public List<Mark> marks; public Tx history, q; public List<Tx> o; public int a; public bool Has(string k) => land.Contains(k); }
    static J D => Data.Get("aimags");
    static List<Aimag> aimags; static List<Ethnic> ethnic; static readonly Dictionary<string, Trip> trips = new Dictionary<string, Trip>();
    public static List<Aimag> AIMAGS => aimags ??= D["AIMAGS"].Select(a => new Aimag { id = a["id"].S, icon = a["icon"].S, name = a["name"].T, center = a["center"].T, fact = a["fact"].T }).ToList();
    public static List<Ethnic> ETHNIC => ethnic ??= D["ETHNIC"].Select(e => new Ethnic { id = e["id"].S, hat = e["hat"].S, deel = e["deel"].S, name = e["name"].T, fact = e["fact"].T, aimags = e["aimags"].Strings() }).ToList();
    public static List<string> ORDER => D["ORDER"].Strings();
    public static Aimag ById(string id) => AIMAGS.FirstOrDefault(a => a.id == id);
    public static Ethnic EthnicById(string id) => ETHNIC.FirstOrDefault(e => e.id == id);
    public static Trip J(string id)
    {
        if (trips.TryGetValue(id ?? "", out var t)) return t;
        var j = D["J"][id ?? ""]; if (j.IsNull) j = D["J"]["tuv"];
        return trips[id ?? ""] = new Trip { land = j["land"].Strings(), marks = j["marks"].Select(m => new Mark { name = m["name"].T, desc = m["desc"].T }).ToList(), history = j["history"].T, q = j["q"]["q"].T, o = j["q"]["o"].Select(x => x.T).ToList(), a = j["q"]["a"].I };
    }
    public static PxCanvas Icon(string id) => Emo.Icon(ById(id)?.icon) ?? Px.Icon("ui:star");

    /** The province drawn from its real features: sky, mountains, snow, volcano, lakes, rivers, forests, dunes, fields, city. */
    public static void DrawLand(PxCanvas x, string id, float t, int W = 120, int H = 60)
    {
        var j = J(id); bool Has(string k) => j.Has(k);
        void R(double a, double b, double w, double hh, string c) => Px.R(x, Js.Round(a), Js.Round(b), Js.Round(w), Js.Round(hh), c);
        bool desert = Has("desert") || Has("dunes"), snow = Has("snow");
        string[] sky = desert ? new[] { "#f6c98a", "#fbe0b0" } : snow ? new[] { "#9fc8ee", "#d6ebfb" } : new[] { "#8fd0f5", "#c6e9fb" };
        R(0, 0, W, H * 0.35, sky[0]); R(0, H * 0.35, W, H * 0.3, sky[1]);
        R(W - 18, 6, 8, 8, desert ? "#fff1b0" : "#ffe27a");
        int gy = Js.Round(H * 0.62); string ground = desert ? "#e2b86e" : Has("fields") ? "#c9b34a" : "#76b947";
        if (Has("mountains") || snow || Has("volcano") || Has("hills")) for (int X = 0; X < W; X++)
            {
                int top = Js.Round(gy - (snow ? 26 : Has("hills") ? 9 : 18) * Math.Abs(Math.Sin(X / (snow ? 14.0 : 20.0) + 1)) - 4 * Math.Sin(X / 7.0));
                R(X, top, 1, gy - top, desert ? "#b9875a" : "#6f97b8"); if (snow && gy - top > 12) R(X, top, 1, 4, "#ffffff");
            }
        if (Has("volcano")) { for (int r = 0; r < 14; r++) R(30 - r * 1.6, gy - 14 + r, 8 + r * 3.2, 1, "#6a5a5a"); R(30, gy - 15, 8, 2, "#3a2e2e"); }
        if (Has("rocks")) for (int i = 0; i < 4; i++) { R(14 + i * 26, gy - 10 - (i % 2) * 5, 10, 12 + (i % 2) * 5, "#d8c8b0"); R(14 + i * 26, gy - 6, 10, 2, "#c9845a"); }
        R(0, gy, W, H - gy, ground);
        if (Has("dunes")) for (int X = 0; X < W; X++) { int top = Js.Round(gy - 6 - 5 * Math.Sin(X / 9.0 + t * 0.2)); R(X, top, 1, gy - top + 2, "#f0c878"); R(X, top, 1, 1, "#fbe0a0"); }
        if (Has("lake")) { R(16, gy + 4, W - 40, 9, "#4aa6e2"); R(20, gy + 6, W - 52, 1, "#93d4f6"); R(30 + Js.Round((t * 6) % 40), gy + 9, 4, 1, "#e6f7ff"); }
        if (Has("river")) for (int X = 0; X < W; X++) R(X, gy + 10 + Js.Round(2 * Math.Sin(X / 8.0)), 1, 3, "#4aa6e2");
        if (Has("forest") || Has("valley")) for (int i = 0; i < 9; i++) { int fx = 4 + i * 13 + (i % 2) * 4, fy = gy - 2 + (i % 3); for (int r = 0; r < 8; r++) R(fx - r / 2.0, fy - 10 + r, r + 1, 1, "#2f6e40"); R(fx, fy - 2, 1, 3, "#5a3a22"); }
        if (Has("fields")) for (int r = 0; r < 4; r++) R(0, gy + 3 + r * 4, W, 1, "#e8d36a");
        if (Has("city")) for (int i = 0; i < 9; i++) { int bh = 8 + ((i * 7) % 14); R(8 + i * 11, gy - bh, 9, bh, new[] { "#c9c1ad", "#9fb0c8", "#e8dfc8" }[i % 3]); for (int wy = gy - bh + 2; wy < gy - 2; wy += 3) R(10 + i * 11, wy, 1, 1, "#f7c646"); }
        if (Has("steppe") || Has("valley") || Has("fields")) { float gx = (t * 8) % (W + 20) - 10; R(gx, gy + 6, 3, 2, "#c98a4b"); R(gx + 6, gy + 8, 3, 2, "#c98a4b"); } // gazelles / horses running
        x.BlitScaled(Px.GerSprite("#3f76db", "#e14b3d"), W - 30, gy - 10, 14, 15);
    }
}

public sealed partial class Game
{
    // ================================================================ the passport: stamps in route order, the first two provinces free
    public const int PASS_FREE = 2, PASS_PRICE = 9900;
    public int StampCount() => V.stamps.Count;
    public bool Stamped(string id) => V.stamps.Exists(s => s.k == id);
    public string NextAimag() => Aimags.ORDER.FirstOrDefault(id => !Stamped(id));
    bool AimagUnlocked(string id) => Stamped(id) || id == NextAimag();
    /** A province beyond the free ones, not stamped before, and no travel pass yet. */
    public bool PassLocked(string id) { if (V.aimagPass > 0) return false; int i = Aimags.ORDER.IndexOf(id); if (i < PASS_FREE) return false; return !Stamped(id); }
    /** Give the province's stamp (from the travel map's guide). */
    public void Stamp(string id)
    {
        if (Stamped(id)) return;
        V.stamps.Add(new KV { k = id, v = SaveData.NowMs() }); V.Save(); Sfx.Win();
        Reward(25, 35); App.LogActivity("stamp", id, V.id); Quest("stamp"); CheckAchievements();
    }
    /** Run go() if the province is open, otherwise show the travel pass (and run go() after a purchase). */
    public void PassGuard(string id, Action go) { if (PassLocked(id)) Open(new PassModal(id, go)); else go(); }
    static string Fmt(int n) => n.ToString("#,0", System.Globalization.CultureInfo.InvariantCulture) + "₮";

    /** The travel pass: Bolormaa stands beside it and explains, the road barrier is down in front of the province, and a
     *  card shows the price and what the pass opens. A grown-up unlocks it behind the parent check. */
    sealed class PassModal : Modal
    {
        readonly string id; readonly Action after; string say; bool done, later;
        public PassModal(string id, Action after)
        {
            this.id = id; this.after = after;
            var g = I; say = Loc.L($"Хөөе, {g.V.name}! Чи аль хэдийн {g.StampCount()} аймгаар аялчихлаа, ямар гоё вэ! Цааш хамт аялахын тулд Аймгийн аяллын эрх хэрэгтэй юм. Ээж, аавдаа үзүүлээд асуучих уу?",
                $"Hey {g.V.name}! You’ve already explored {g.StampCount()} provinces, how wonderful! To keep travelling together we need the Aimag Travel Pass. Shall we show a grown-up?");
        }
        public override float Width => 560;
        public override (string who, string face)? Speaker => ("bolormaa", done ? "laugh" : "happy");
        readonly Live land = new Live(160, 70);
        public override void Draw(Game g)
        {
            var a = Aimags.ById(id);
            GUILayout.Label(Loc.L($"Аймгийн паспорт · {g.StampCount()}/{Aimags.ORDER.Count} тамга", $"Aimag passport · {g.StampCount()}/{Aimags.ORDER.Count} stamps"), g.sKicker);
            g.Title(a != null ? Loc.L($"{a.name.mn} руу явах уу?", $"Off to {a.name.en}?") : Loc.L("Цааш аялах уу?", "Travel further?"));
            Aimags.DrawLand(land.c, id, Time.time, 160, 70);
            if (!done) { Px.R(land.c, 10, 54, 140, 5, "#2b1e2e"); for (int i = 0; i < 14; i++) Px.R(land.c, 11 + i * 10, 55, 9, 3, i % 2 != 0 ? "#ffffff" : "#e14b3d"); } // the road barrier, gone once the pass is bought
            land.Show(g, 120);
            g.Gap(6);
            GUILayout.Label(say, g.sLabel);
            g.Gap(6);
            if (done)
            {
                if (g.Btn(a != null ? Loc.L($"{a.name.mn} руу аялах", $"Travel to {a.name.en}") : Loc.L("Аялах", "Travel"), true, 48)) g.Later(() => { g.Close(); after?.Invoke(); });
                return;
            }
            // the offer: what it is and what it costs, then what it opens
            GUILayout.BeginVertical(g.sPanel);
            GUILayout.BeginHorizontal();
            g.IconAt(Px.Icon("ui:scroll"), 40); g.Gap(8);
            GUILayout.BeginVertical();
            GUILayout.Label("<b>" + Loc.L("Аймгийн аяллын эрх", "Aimag Travel Pass") + "</b>", g.sLabel);
            GUILayout.Label(Loc.L("Нэг удаа төлнө · сарын төлбөргүй", "One-time payment · no subscription"), g.sSmall);
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            GUILayout.Label(Fmt(PASS_PRICE), new GUIStyle(g.sTitle) { alignment = TextAnchor.MiddleRight, wordWrap = false }, GUILayout.ExpandWidth(false));
            GUILayout.EndHorizontal();
            g.Gap(4);
            int left = Aimags.ORDER.Count - PASS_FREE;
            foreach (var (icon, line) in new[]
            {
                ("ui:ovoo", Loc.L($"Үлдсэн {left} аймаг бүгд нээгдэнэ", $"All {left} remaining provinces open")),
                ("ui:horse", Loc.L("Аймаг бүрээр 3D-ээр аялна", "Travel every province in 3D")),
                ("ui:coin", Loc.L("Тамга бүрт зоос, XP", "Coins and XP for every stamp")),
                ("ui:star", Loc.L("Нэг эрх = хөдөөгийн нэг хүүхдэд үнэгүй эрх", "Each pass gives a free pass to a rural child")),
            })
            {
                GUILayout.BeginHorizontal(); g.IconAt(Px.Icon(icon), 20); g.Gap(6); GUILayout.Label(line, g.sSmall); GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();
            g.Gap(6);
            if (g.Btn(Loc.L("Том хүнээс зөвшөөрөл авах", "Ask a grown-up to unlock"), true, 48, !later)) g.Later(() => g.Open(new ParentGate(() => g.Open(new PayModal(this)))));
            if (g.Btn(Loc.L("Дараа", "Maybe later"), false, 36, !later)) g.Later(() => { later = true; say = Loc.L("Зүгээр ээ! Бэлэн болохоор хамт аялцгаая. Тэр хооронд Нутагтаа тоглоод байя.", "That’s okay! We’ll travel together whenever you’re ready. Let’s play in Nutag for now."); g.StartCoroutine(CloseSoon(g)); });
            GUILayout.Label(Loc.L("Төлөхөөс өмнө том хүн эцэг эхийн шалгалтаар баталгаажуулна.", "A grown-up confirms with the parent check before anything is paid."), new GUIStyle(g.sKicker) { alignment = TextAnchor.MiddleCenter });
        }
        System.Collections.IEnumerator CloseSoon(Game g) { yield return new WaitForSeconds(1.5f); if (g.modal == this) g.Close(); }
        /** Payment goes through here. A real payment provider plugs in as Game.Charge; without one the purchase is recorded on this device after the parental gate. */
        public void Paid(Game g)
        {
            g.V.aimagPass = SaveData.NowMs(); g.V.Save(); g.App.LogActivity("pass", "aimag", g.V.id);
            done = true; g.Confetti(80); Sfx.Win();
            say = Loc.L("Ура, баярлалаа! Одоо бүх Монголоор хамт аялна. Явцгаая!", "Hooray, thank you! Now we can travel all of Mongolia together. Let’s go!");
        }
    }
    /** The grown-up's checkout: what is bought, for whom, the total, then pay or cancel. */
    sealed class PayModal : Modal
    {
        readonly PassModal pass; public PayModal(PassModal pass) { this.pass = pass; }
        public override float Width => 480;
        public override void Draw(Game g)
        {
            GUILayout.Label(Loc.L("Том хүнд", "For the grown-up"), g.sKicker);
            g.Title(Loc.L("Төлбөр", "Checkout"));
            GUILayout.BeginVertical(g.sPanel);
            GUILayout.BeginHorizontal();
            g.IconAt(Px.Icon("ui:scroll"), 36); g.Gap(8);
            GUILayout.BeginVertical();
            GUILayout.Label("<b>" + Loc.L("Аймгийн аяллын эрх", "Aimag Travel Pass") + "</b>", g.sLabel);
            GUILayout.Label(Loc.L($"{g.V.name}-д · бүх аймаг · нэг удаа", $"For {g.V.name} · every province · one-time"), g.sSmall);
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            GUILayout.Label(Fmt(PASS_PRICE), new GUIStyle(g.sLabel) { alignment = TextAnchor.MiddleRight, wordWrap = false }, GUILayout.ExpandWidth(false));
            GUILayout.EndHorizontal();
            g.Gap(6); var line = GUILayoutUtility.GetRect(10, g.U(1), GUILayout.ExpandWidth(true)); g.Box(line, new Color(0, 0, 0, 0.15f)); g.Gap(6);
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>" + Loc.L("Нийт", "Total") + "</b>", g.sLabel);
            GUILayout.FlexibleSpace();
            GUILayout.Label(Fmt(PASS_PRICE), new GUIStyle(g.sTitle) { alignment = TextAnchor.MiddleRight, wordWrap = false }, GUILayout.ExpandWidth(false));
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            g.Gap(4);
            GUILayout.BeginHorizontal(); g.IconAt(Px.Icon("ui:lock"), 16); g.Gap(4);
            GUILayout.Label(Loc.L("Нэг удаагийн төлбөр. Дахин төлбөр авахгүй.", "One-time payment. Nothing is charged again."), g.sKicker); GUILayout.EndHorizontal();
            g.Gap(6);
            if (g.Btn(Loc.L($"{Fmt(PASS_PRICE)} төлөх", $"Pay {Fmt(PASS_PRICE)}"), true, 48)) g.Later(() => { g.Close(); if (Charge == null || Charge()) pass.Paid(g); else g.Toast(Loc.L("Төлбөр амжилтгүй боллоо. Дахин оролдоорой.", "The payment did not go through. Please try again.")); });
            if (g.Btn(Loc.L("Болих", "Cancel"), false, 36)) g.Later(g.Close);
        }
    }
    /** A real payment provider (QPay, card, store billing) can be plugged in here; it returns true when the payment went through. */
    public static Func<bool> Charge;

    sealed class PassportPage : Page
    {
        protected override bool Hero => true;
        protected override string Title => Loc.L("Аймгийн паспорт", "Aimag Passport");
        protected override void Body(Game g)
        {
            var order = Aimags.ORDER; string nx = g.NextAimag();
            GUILayout.BeginHorizontal(g.sPanel);
            g.IconAt(Emo.Icon("🛂"), 56);
            GUILayout.BeginVertical();
            GUILayout.Label($"<b>{g.StampCount()} / {order.Count}</b>", g.sBig);
            g.Small(Loc.L("Улаанбаатараас эхлээд Монголын 21 аймгаар аялаарай. Аймаг бүрийн байгаль, алдартай газрууд, түүхтэй танилцаад тамга авбал дараагийн аймаг нээгдэнэ.", "Start in Ulaanbaatar and travel through all 21 provinces. Discover each one’s nature, landmarks and history, earn its stamp and the next one opens."));
            if (nx != null) { if (g.Btn(Loc.L("Дараагийн аялал: ", "Next trip: ") + Aimags.ById(nx).name, true, 40)) g.Later(() => g.PassGuard(nx, () => g.Open(new AimagPage(nx)))); }
            else g.Text("<b>" + Loc.L("Та Монголоор бүрэн аялсан!", "You have travelled all of Mongolia!") + "</b>");
            GUILayout.EndVertical(); GUILayout.EndHorizontal();
            int cols = g.GridCols(order.Select(id => "<b>" + Aimags.ById(id).name + "</b>"), new GUIStyle(g.sLabel) { wordWrap = false, fontSize = g.Ts(11) }, 16, 7);
            for (int i = 0; i < order.Count; i += cols)
            {
                GUILayout.BeginHorizontal();
                for (int k = i; k < Mathf.Min(order.Count, i + cols); k++)
                {
                    string id = order[k]; var a = Aimags.ById(id); bool got = g.Stamped(id), open = g.AimagUnlocked(id), home = g.V.aimag == id, paid = !g.PassLocked(id);
                    var r = GUILayoutUtility.GetRect(g.U(110), g.U(96), GUILayout.ExpandWidth(true));
                    bool was = GUI.enabled; GUI.enabled = open;
                    if (GUI.Button(r, "", got ? g.sPrimary : g.sBtn)) { var id2 = id; g.Later(() => g.PassGuard(id2, () => g.Open(new AimagPage(id2)))); }
                    GUI.enabled = was;
                    var old = GUI.color; if (!open) GUI.color = new Color(1, 1, 1, 0.45f);
                    GUI.Label(new Rect(r.x + g.U(6), r.y + g.U(4), g.U(30), g.U(18)), (k + 1).ToString(), g.sSmall);
                    GUI.DrawTexture(new Rect(r.center.x - g.U(16), r.y + g.U(10), g.U(32), g.U(32)), g.Tex(Aimags.Icon(id)));
                    var st = new GUIStyle(got ? g.sPrimary.Clean() : g.sLabel) { alignment = TextAnchor.MiddleCenter, wordWrap = false, fontSize = g.Ts(11) };
                    GUI.Label(new Rect(r.x, r.y + g.U(44), r.width, g.U(20)), "<b>" + a.name + "</b>", st);
                    string sub = got ? DateTimeOffset.FromUnixTimeMilliseconds(g.V.stamps.Find(s => s.k == id).v).LocalDateTime.ToString("yyyy.MM.dd") : open ? Loc.L("Аялах →", "Visit →") : !paid && k == PASS_FREE ? Fmt(PASS_PRICE) : "";
                    GUI.Label(new Rect(r.x + g.U(2), r.y + g.U(61), r.width - g.U(4), g.U(32)), sub + (home ? " · " + Loc.L("Миний нутаг", "Home") : ""), new GUIStyle(st) { fontSize = g.Ts(9), wordWrap = true }); // a stamped home province wraps to two lines
                    GUI.color = old;
                }
                g.PadRow(i + cols - Mathf.Min(order.Count, i + cols), 110, 96);
                GUILayout.EndHorizontal();
            }
        }
    }

    /** One province: landscape, landmarks, history, then the trip for the stamp. */
    sealed class AimagPage : Page
    {
        readonly string id; readonly Live land = new Live(160, 80);
        public AimagPage(string id) { this.id = id; }
        protected override string Title => $"{Aimags.ORDER.IndexOf(id) + 1} / {Aimags.ORDER.Count}";
        protected override void Body(Game g)
        {
            var a = Aimags.ById(id); var j = Aimags.J(id); bool got = g.Stamped(id);
            var eth = Aimags.ETHNIC.Where(e => e.aimags[0] == id || (e.aimags.Contains(id) && e.id != "khalkh")).ToList();
            int seen = g.V.seenMarks.Count(s => s.StartsWith(id + ":"));
            GUILayout.BeginVertical(g.sPanel);
            Aimags.DrawLand(land.c, id, Time.time, 160, 80); land.Show(g, 170);
            GUILayout.BeginHorizontal(); g.IconAt(Aimags.Icon(id), 40); GUILayout.BeginVertical(); GUILayout.Label("<b>" + a.name + "</b>", g.sTitle);
            g.Small($"{Loc.L("Төв", "Centre")}: {a.center}" + (g.V.aimag == id ? " · " + Loc.L("Миний нутаг", "My home province") : "")); GUILayout.EndVertical(); GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUILayout.BeginVertical(g.sPanel);
            if (got) g.Text("<b>" + Loc.L("Тамга авсан!", "Stamped!") + "</b> " + DateTimeOffset.FromUnixTimeMilliseconds(g.V.stamps.Find(s => s.k == id).v).LocalDateTime.ToString("yyyy.MM.dd"));
            if (g.Btn(got ? Loc.L("Дахин аялах", "Travel there again") : Loc.L($"{a.name.mn} руу аялах", $"Travel to {a.name.en}"), true, 50)) g.Later(() => g.OpenTrip(id, "2d"));
            if (g.Btn(Loc.L("Өөрийн нүдээр аялах (3D)", "Travel in first person (3D)"), false, 40)) g.Later(() => g.OpenTrip(id, "fp"));
            g.Small(got ? Loc.L($"{seen}/3 газраар аялсан.", $"Visited {seen}/3 places.") : Loc.L("Тэнд очиж 3 алдартай газраар аялаад, нутгийн хөтчөөс тамгаа аваарай.", "Go there, visit the 3 famous places and get your stamp from the local guide."));
            GUILayout.EndVertical();
            GUILayout.BeginVertical(g.sPanel);
            GUILayout.Label("<b>" + Loc.L("Алдартай газрууд", "Famous places") + "</b>", g.sLabel);
            foreach (var m in j.marks) { GUILayout.Label("<b>" + m.name + "</b>", g.sLabel); GUILayout.Label(m.desc.ToString(), g.sSmall); }
            GUILayout.EndVertical();
            GUILayout.BeginVertical(g.sPanel);
            GUILayout.Label("<b>" + Loc.L("Түүх", "History") + "</b>", g.sLabel); g.Text(j.history.ToString()); g.Small(a.fact.ToString());
            if (eth.Count > 0) g.Small(Loc.L("Энд амьдардаг ястнууд: ", "Peoples living here: ") + string.Join(", ", eth.Select(e => e.name.ToString())));
            GUILayout.EndVertical();
            if (g.Btn(Loc.L("Басараас энэ аймгийн тухай асуух", "Ask Basar about this province"))) g.Later(() => g.Open(new BasarChat(g, null, null, Loc.L($"{a.name} аймгийн тухай сонирхолтой зүйл хэлээч", $"Tell me something interesting about {a.name} province"))));
        }
    }

    // ================================================================ character creator: ethnic group → home province → outfit → welcome
    sealed class CreatePage : Page
    {
        protected override bool Shell => false;
        public override void Draw(Game g)
        {
            g.Box(new Rect(0, 0, Screen.width, Screen.height), Px.Col("#f6f4ef"));
            var safe = Screen.safeArea; float top = Screen.height - safe.yMax, w = Mathf.Min(safe.width - g.U(24), g.U(560));
            if (Back && GUI.Button(new Rect(safe.x + g.U(16), top + g.U(16), g.U(44), g.U(40)), "←", g.sWhite)) g.Later(g.Close);
            if (first && GUI.Button(new Rect(safe.xMax - g.U(16) - g.U(120), top + g.U(16), g.U(120), g.U(40)), Loc.L("Дараа нь", "Later"), g.sWhite)) g.Later(Later);
            var tr = new Rect(safe.x, top + g.U(16), safe.width, g.U(64)); var ts = new GUIStyle(g.sBig) { fontSize = Mathf.RoundToInt(g.U(44)), normal = { textColor = new Color(0.17f, 0.12f, 0.18f, 0.6f) } };
            GUI.Label(new Rect(tr.x, tr.y + g.U(3), tr.width, tr.height), Title, ts); ts.normal.textColor = Color.white; GUI.Label(tr, Title, ts);
            GUILayout.BeginArea(new Rect(safe.x + (safe.width - w) / 2, top + g.U(92), w, safe.height - g.U(100)));
            GUILayout.BeginVertical(g.sPanel);
            scroll = g.BeginScroll(scroll, safe.height - g.U(160)); Body(g); g.EndScroll();
            GUILayout.EndVertical();
            GUILayout.EndArea();
        }
        void Later() { var g = I; if (string.IsNullOrEmpty(g.V.eth)) g.V.eth = "skip"; g.V.Save(); g.Close(); if (!g.V.tips && g.V.tutorialSeen > 0) g.Open(new TipsModal()); }
        int step; string eth, aimag; readonly Look look; bool showAll; readonly bool first; readonly Live land = new Live(160, 70);
        static readonly string[] DEELS = { "blue", "red", "green", "purple", "teal", "gold", "brown", "white", "black", "pink" };
        static readonly string[] HATS = { null, "mongol", "kazakh", "buriad", "oirat", "fur" };
        public CreatePage(bool first)
        {
            this.first = first; var V = I.V; eth = string.IsNullOrEmpty(V.eth) || V.eth == "skip" ? null : V.eth; aimag = string.IsNullOrEmpty(V.aimag) ? null : V.aimag; look = V.look.Copy(); step = eth != null ? 2 : 0;
        }
        public override bool CanEscape => !first;
        protected override bool Back => !first;
        protected override string Title => Loc.L("Дүрээ бүтээ", "Create your character");
        void Basar(Game g, string t) { GUILayout.BeginHorizontal(); g.IconAt(Px.BankharFace(1), 40); g.Gap(10); GUILayout.Label(t, g.sTeach); GUILayout.EndHorizontal(); g.Gap(10); }
        void Nav(Game g, int back, Action next, string label = null, bool enabled = true)
        {
            GUILayout.BeginHorizontal();
            if (back >= 0 && g.Btn("←", false, 54)) g.Later(() => step = back);
            if (g.Btn(label ?? Loc.L("Цааш →", "Next →"), true, 54, enabled)) g.Later(next);
            GUILayout.EndHorizontal();
        }
        protected override void Body(Game g)
        {
            var V = g.V; var e = Aimags.EthnicById(eth);
            g.Steps(step, 4);
            if (step == 0)
            {
                Basar(g, Loc.L($"{V.name}, чи аль ястных вэ? Монголд 20 гаруй ястан амьдардаг!", $"{V.name}, which ethnic group are you from? Over 20 groups live in Mongolia!"));
                int cols = 5;
                for (int i = 0; i < Aimags.ETHNIC.Count; i += cols)
                {
                    GUILayout.BeginHorizontal();
                    for (int k = i; k < Mathf.Min(Aimags.ETHNIC.Count, i + cols); k++)
                    {
                        var x = Aimags.ETHNIC[k]; var r = GUILayoutUtility.GetRect(g.U(90), g.U(86), GUILayout.ExpandWidth(true));
                        if (GUI.Button(r, "", eth == x.id ? g.sSel : g.sTile)) { var xx = x; g.Later(() => { eth = xx.id; look.hat = xx.hat; look.deel = xx.deel; if (!xx.aimags.Contains(aimag)) aimag = xx.aimags[0]; Sfx.Tap(); }); }
                        var l2 = look.Copy(); l2.hat = x.hat; l2.deel = x.deel;
                        GUI.DrawTexture(new Rect(r.center.x - g.U(14), r.y + g.U(6), g.U(28), g.U(49)), g.Tex(Px.Char(l2, 0, 0)));
                        GUI.Label(new Rect(r.x + g.U(2), r.y + g.U(56), r.width - g.U(4), g.U(28)), x.name.ToString(), new GUIStyle(g.sLabel) { alignment = TextAnchor.UpperCenter, fontSize = Mathf.RoundToInt(g.U(12)) });
                    }
                    g.PadRow(i + cols - Mathf.Min(Aimags.ETHNIC.Count, i + cols), 90, 86);
                    GUILayout.EndHorizontal();
                }
                g.Gap(8); GUILayout.BeginVertical(g.sTeach);
                if (e != null) { GUILayout.Label("<b>" + e.name + "</b>", g.sLabel); GUILayout.Label(e.fact.ToString(), g.sLabel); } else g.Small(Loc.L("Ястнаа сонгоход тухайн ястны тухай баримт гарна.", "Pick your ethnic group to read about it."));
                GUILayout.EndVertical();
                Nav(g, -1, () => step = 1, null, eth != null);
            }
            else if (step == 1)
            {
                Basar(g, Loc.L($"{e.name.mn} ард түмэн ихэвчлэн эдгээр нутагт амьдардаг. Чи хаана төрсөн бэ?", $"The {e.name.en} people mostly live here. Where were you born?"));
                if (aimag != null) { Aimags.DrawLand(land.c, aimag, Time.time, 160, 70); land.Show(g, 230); g.Gap(8); }
                var allowed = showAll ? Aimags.AIMAGS.Select(a => a.id).ToList() : e.aimags;
                var nameSt = new GUIStyle(g.sLabel) { alignment = TextAnchor.UpperCenter, wordWrap = false, fontSize = Mathf.RoundToInt(g.U(12)) };
                int cols = 5;
                for (int i = 0; i < allowed.Count; i += cols)
                {
                    GUILayout.BeginHorizontal();
                    for (int k = i; k < Mathf.Min(allowed.Count, i + cols); k++)
                    {
                        var x = Aimags.ById(allowed[k]); var r = GUILayoutUtility.GetRect(g.U(90), g.U(58), GUILayout.ExpandWidth(true));
                        if (GUI.Button(r, "", aimag == x.id ? g.sSel : g.sTile)) { var id = x.id; g.Later(() => { aimag = id; Sfx.Tap(); }); }
                        GUI.DrawTexture(new Rect(r.center.x - g.U(11), r.y + g.U(6), g.U(22), g.U(22)), g.Tex(Aimags.Icon(x.id)), ScaleMode.ScaleToFit);
                        GUI.Label(new Rect(r.x + g.U(2), r.y + g.U(32), r.width - g.U(4), g.U(22)), x.name.ToString(), nameSt);
                    }
                    g.PadRow(i + cols - Mathf.Min(allowed.Count, i + cols), 90, 58);
                    GUILayout.EndHorizontal();
                }
                if (!showAll && e.aimags.Count < 22 && GUILayout.Button(Loc.L("Өөр аймагт төрсөн", "I was born somewhere else"), g.sSmall)) g.Later(() => showAll = true);
                var a = Aimags.ById(aimag);
                if (a != null) { g.Gap(8); GUILayout.BeginVertical(g.sTeach); GUILayout.Label($"<b>{a.name} · {a.center}</b>", g.sLabel); GUILayout.Label(a.fact.ToString(), g.sLabel); GUILayout.EndVertical(); }
                Nav(g, 0, () => step = 2, null, aimag != null);
            }
            else if (step == 2)
            {
                Basar(g, Loc.L("Одоо дүрээ хувцаслая! " + (e != null ? e.name.mn + " ястны уламжлалт малгай, дээлээр эхэллээ." : ""), "Now dress your character! " + (e != null ? "We started with the traditional " + e.name.en + " hat and deel." : "")));
                var pv = GUILayoutUtility.GetRect(g.U(100), g.U(216), GUILayout.ExpandWidth(true));
                g.Box(pv, Px.Col("#c99a5a")); g.Box(new Rect(pv.x + g.U(2), pv.y + g.U(2), pv.width - g.U(4), pv.height - g.U(4)), Px.Col("#a2d8f4"));
                float chH = g.U(184), chW = chH * 16 / 28f; GUI.DrawTexture(new Rect(pv.center.x - chW - g.U(6), pv.y + g.U(14), chW, chH), g.Tex(Px.Char(look, 0, 0)));
                GUI.Label(new Rect(pv.center.x + g.U(10), pv.center.y - g.U(26), pv.width / 2 - g.U(16), g.U(28)), V.name, new GUIStyle(g.sTitle) { fontSize = Mathf.RoundToInt(g.U(20)), wordWrap = false });
                GUI.Label(new Rect(pv.center.x + g.U(10), pv.center.y + g.U(2), pv.width / 2 - g.U(16), g.U(38)), string.Join(" · ", new[] { e?.name.ToString(), Aimags.ById(aimag)?.name.ToString() }.Where(s => !string.IsNullOrEmpty(s))), new GUIStyle(g.sLabel) { fontSize = Mathf.RoundToInt(g.U(13)) });
                g.Gap(10);
                GUILayout.Label(Loc.L("Би бол", "I am a"), g.sSmall);
                int gi = g.Chips(new[] { Loc.L("Хүү", "Boy"), Loc.L("Охин", "Girl") }, look.Girl ? 1 : 0, 70);
                if (gi >= 0) g.Later(() => { look.gender = gi == 1 ? "girl" : null; Sfx.Tap(); });
                GUILayout.Label(Loc.L("Арьсны өнгө", "Skin tone"), g.sSmall);
                GUILayout.BeginHorizontal(); foreach (var sk in Rules.SKINS) if (g.Swatch(Px.Col(sk), look.skin == sk)) { var s2 = sk; g.Later(() => { look.skin = s2; Sfx.Tap(); }); } GUILayout.FlexibleSpace(); GUILayout.EndHorizontal();
                GUILayout.Label(Loc.L("Дээл", "Deel"), g.sSmall);
                GUILayout.BeginHorizontal(); foreach (var d in DEELS) if (g.Swatch(Px.Col(Px.DEEL[d][0]), look.deel == d, 32)) { var d2 = d; g.Later(() => { look.deel = d2; Sfx.Tap(); }); } GUILayout.FlexibleSpace(); GUILayout.EndHorizontal();
                GUILayout.Label(Loc.L("Малгай", "Hat"), g.sSmall);
                string HatName(string h) => h == null ? Loc.L("Малгайгүй", "No hat") : h == "mongol" ? Loc.L("Монгол", "Mongol") : h == "kazakh" ? Loc.L("Казах", "Kazakh") : h == "buriad" ? Loc.L("Буриад", "Buryat") : h == "oirat" ? Loc.L("Ойрад", "Oirat") : Loc.L("Үслэг", "Fur");
                int hh = g.Chips(HATS.Select(HatName).ToList(), Array.IndexOf(HATS, string.IsNullOrEmpty(look.hat) ? null : look.hat), 70);
                if (hh >= 0) g.Later(() => { look.hat = HATS[hh]; Sfx.Tap(); });
                Nav(g, 1, () => step = 3);
            }
            else
            {
                var a = Aimags.ById(aimag);
                if (a != null) { Aimags.DrawLand(land.c, aimag, Time.time, 160, 70); land.Show(g, 230); g.Gap(8); }
                GUILayout.BeginHorizontal(g.sBtn); g.IconAt(Px.Char(look, 0, 0), 80); g.Gap(10);
                GUILayout.BeginVertical();
                GUILayout.Label("<b>" + Loc.L($"Сайн уу, {V.name}!", $"Hello, {V.name}!") + "</b>", g.sTitle);
                g.Text(Loc.L($"Чи {a?.name.mn}-д төрсөн {e?.name.mn} хүн.", $"You are a {e?.name.en} person born in {a?.name.en}."));
                if (a != null) g.Small(a.fact.ToString());
                GUILayout.EndVertical(); GUILayout.EndHorizontal();
                Basar(g, Loc.L("Нутагт хамтдаа явцгаая! Аймгийн паспортоор Монголоор аялж, нутгаа ч бас олоорой.", "Let’s go to Nutag together! Travel Mongolia with your aimag passport and find your home province too."));
                Nav(g, 2, () =>
                {
                    V.eth = eth; V.aimag = aimag; V.charAt = SaveData.NowMs(); V.look.deel = look.deel; V.look.skin = look.skin; V.look.hat = look.hat; V.look.gender = look.gender; V.Save();
                    g.CheckAchievements(true); g.Close();
                    g.Toast(Loc.L("Нутагт тавтай морил!", "Welcome to Nutag!"));
                    if (!V.tips && V.tutorialSeen > 0) g.Open(new TipsModal());
                }, Loc.L("Нутаг руу орох ►", "Enter Nutag ►"));
            }
        }
    }
}
