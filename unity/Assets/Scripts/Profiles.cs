// Profiles: mascot animals, the 3-picture password, family faces, logging in and registering (js/data.js and the auth
// screen of js/app.js). The web version shows emoji here; Unity can't draw colour emoji, so each one is pixel art.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static partial class Px
{
    static readonly string[] MASCOT_EMOJI = { "🐎", "🐫", "🦅", "🐆", "🐺", "🐑", "🐐", "🐂", "🦌", "🦊", "🐿️", "🐻" };
    static readonly string[] PIC_EMOJI = { "⛺", "🐎", "🥛", "🏹", "🌙", "☀️", "🐕", "🦴", "🎻" };
    static readonly string[] FACE_EMOJI = { "👩", "👨", "👵", "👴", "🧑", "👧", "👦", "🧒", "👶", "🧕", "👩‍🦳", "👨‍🦳" };
    public const int FACE_COUNT = 12;
    /** The 12 mascot animals, the 9 pictures of the picture password and the family faces: the web version's pixel icons. */
    public static PxCanvas Mascot(int i) => Emo.Icon(MASCOT_EMOJI[((i % 12) + 12) % 12]);
    public static PxCanvas PicKey(int i) => Emo.Icon(PIC_EMOJI[((i % 9) + 9) % 9]);
    public static PxCanvas Face(int i) => Emo.Icon(FACE_EMOJI[((i % FACE_COUNT) + FACE_COUNT) % FACE_COUNT]);
    public static PxCanvas Portrait(Look l) { var x = new PxCanvas(16, 18); x.Blit(Char(l, 0, 0), 0, -2); return x; }
}

/** A number from 1000 to 99999 in words, for the parent gate: Mongolian (a number before another takes its -н form:
 *  "гурван зуун", "дөчин таван мянга") and English. */
public static class Words
{
    static readonly string[] MN1 = { "", "нэг", "хоёр", "гурав", "дөрөв", "тав", "зургаа", "долоо", "найм", "ес" };
    static readonly string[] MN1N = { "", "нэг", "хоёр", "гурван", "дөрвөн", "таван", "зургаан", "долоон", "найман", "есөн" };
    static readonly string[] MN10 = { "", "арав", "хорь", "гуч", "дөч", "тавь", "жар", "дал", "ная", "ер" };
    static readonly string[] MN10N = { "", "арван", "хорин", "гучин", "дөчин", "тавин", "жаран", "далан", "наян", "ерэн" };
    static string Mn99(int n, bool before) { int t = n / 10, o = n % 10; return o == 0 ? (before ? MN10N : MN10)[t] : (t > 0 ? MN10N[t] + " " : "") + (before ? MN1N : MN1)[o]; }
    static string Mn999(int n, bool before) { int h = n / 100, r = n % 100; return h == 0 ? Mn99(r, before) : MN1N[h] + (r > 0 || before ? " зуун" : " зуу") + (r > 0 ? " " + Mn99(r, before) : ""); }
    public static string Mn(int n) => Mn999(n / 1000, true) + " мянга" + (n % 1000 > 0 ? " " + Mn999(n % 1000, false) : "");
    static readonly string[] EN1 = { "", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen" };
    static readonly string[] EN10 = { "", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety" };
    static string En99(int n) => n < 20 ? EN1[n] : EN10[n / 10] + (n % 10 > 0 ? "-" + EN1[n % 10] : "");
    static string En999(int n) => n >= 100 ? EN1[n / 100] + " hundred" + (n % 100 > 0 ? " " + En99(n % 100) : "") : En99(n);
    public static string En(int n) => En999(n / 1000) + " thousand" + (n % 1000 > 0 ? " " + En999(n % 1000) : "");
}

public static class Profiles
{
    public sealed class Mascot { public Tx name, fact; }
    static Mascot M(string mn, string en, string fmn, string fen) => new Mascot { name = new Tx(mn, en), fact = new Tx(fmn, fen) };
    public static readonly Mascot[] MASCOTS =
    {
        M("Хурдан морь", "Swift horse", "Монголд хүнээс олон адуу бий!", "Mongolia has more horses than people!"),
        M("Тэвчээртэй тэмээ", "Patient camel", "Хоёр бөхтэй тэмээ Говьд амьдардаг.", "Two-humped camels live in the Gobi."),
        M("Сэргэлэн бүргэд", "Clever eagle", "Бүргэдчид бүргэдээр ан хийдэг.", "Eagle hunters hunt with golden eagles."),
        M("Цоохор ирвэс", "Snow leopard", "Ирвэс өндөр ууланд нуугдаж амьдардаг.", "Snow leopards hide high in the mountains."),
        M("Бөртэ чоно", "Blue wolf", "Нууц товчоо Бөртэ чоноор эхэлдэг.", "The Secret History begins with a blue wolf."),
        M("Номхон хонь", "Gentle sheep", "Хонины ноосоор эсгий хийдэг.", "Felt for the ger is made from sheep’s wool."),
        M("Сэргэг ямаа", "Lively goat", "Ямааны ноолуур дэлхийд алдартай.", "Mongolian cashmere is famous worldwide."),
        M("Хүчит сарлаг", "Strong yak", "Сарлагийн сүү маш тослог.", "Yak milk is very creamy."),
        M("Цаа буга", "Reindeer", "Цаатан ард цаа бугаа маллдаг.", "The Tsaatan people herd reindeer."),
        M("Ухаалаг үнэг", "Clever fox", "Үнэг ардын үлгэрт их гардаг.", "Foxes star in many folk tales."),
        M("Зурам", "Ground squirrel", "Зурам тал нутагт нүх ухаж амьдардаг.", "Ground squirrels dig burrows on the steppe."),
        M("Мазаалай", "Gobi bear", "Мазаалай зөвхөн Монголын Говьд бий.", "The Gobi bear lives only in Mongolia’s Gobi."),
    };
}

public sealed partial class Game
{
    public AppState App;

    /** Switches to a child's profile: their progress, their place in Нутаг, their language stays the device's. */
    public void Enter(SaveData p)
    {
        V = p; App.activeId = p.id; App.loggedIn = true; App.Save();
        bool okPos = V.hasPos && !World.Blocked(Mathf.FloorToInt(V.posX + 0.5f), Mathf.FloorToInt(V.posY + 0.82f));
        px = okPos ? V.posX : 17; py = okPos ? V.posY : 13; vx = vy = 0; path.Clear(); pending = null; riding = false; race = null;
        mx = Mathf.FloorToInt(px + 0.5f); my = Mathf.FloorToInt(py + 0.82f); camPos = new Vector2(px, py); petPos = new Vector2(px, py + 0.6f); dgx = px + 0.7f; dgy = py + 0.2f;
        Basar.history.Clear();
        CloseAll(); Quests(); Sfx.Win(); net.Close();   // nothing connects until the player picks multiplayer (ModeModal)
        Action proceed = () =>
        {
            if (string.IsNullOrEmpty(p.eth)) { Open(new ModeModal(() => Open(new CreatePage(true)))); return; } // new: pick how to play, then build your character (ethnic group, home province, outfit)
            Toast(Loc.L($"Тавтай морил, {p.name}!", $"Welcome, {p.name}!"));
            if (!V.tips && V.tutorialSeen > 0) Open(new TipsModal());
            Open(new ModeModal());   // singleplayer or multiplayer, on top
            if (string.IsNullOrEmpty(p.email)) Open(new EmailLinkModal(true)); // an older save with no email: its progress is kept on the server only with one
        };
        if (string.IsNullOrEmpty(App.gameMode))     // the first launch asks the player how they want to learn (IntroCinematic.cs)
        {
            if (!App.introSeen) OpenIntroCinematic(proceed);
            else Open(new GameModeModal(proceed));
            return;
        }
        proceed();
    }
    public void LogOut() { Family.pin = ""; net.SaveAlone(this); net.Close(); if (V != null) { SavePos(); V.Save(); } App.loggedIn = false; App.Save(); CloseAll(); V = null; Open(new AuthPage()); }

    /** Grown-ups only: the device's grown-up PIN. Before there is one, a family device asks for the family's PIN (check-pin);
     *  any other device (or one that can't reach the server) has a grown-up type a number written in words (hard for a little
     *  one), choose the PIN twice and see its recovery code once. "Forgot PIN?" takes the recovery code. On a family device
     *  whose PIN was changed elsewhere, a PIN this device doesn't know is asked of the server. `family`: the parents' menu,
     *  which keeps the typed PIN in memory for its 10 minutes (Family.pin); other gates forget it. */
    public sealed class ParentGate : Modal
    {
        // pin: the PIN · words: the number in words · new, again: choosing a PIN · rec: the recovery code · show: the new recovery code
        readonly Action onPass; readonly bool family; string step, input = "", first = "", rec = "", shown = "", err = ""; bool busy, recovering, locked;
        readonly int number = UnityEngine.Random.Range(1000, 100000);
        public ParentGate(Action onPass, bool family = false) { this.onPass = onPass; this.family = family; step = DevicePin.Has(I.App) || Family.Member(I) != null ? "pin" : "words"; }
        public override void Press(Game g) { }
        /** Only while this gate is still the open window: cancelled during "Checking…", a late answer opens nothing. */
        void Pass(Game g, string pin) { if (g.modal != this) return; if (family) Family.pin = pin; g.Close(); onPass(); }
        public override void Draw(Game g)
        {
            var app = g.App; var center = new GUIStyle(g.sLabel) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
            // the clock moves between IMGUI's layout and repaint passes: read it once, on layout
            if (Event.current.type == EventType.Layout) locked = DevicePin.Wait(app) > 0 && (step == "pin" || step == "rec");
            g.Picture(Px.Icon("ui:lock"), 48);
            GUILayout.Label(Loc.L("Том хүний хэсэг", "Grown-ups only"), new GUIStyle(g.sTitle) { alignment = TextAnchor.MiddleCenter });
            if (step == "pin")
            {
                GUILayout.Label(DevicePin.Has(app) ? Loc.L("Том хүний ПИН кодоо оруулаарай:", "Enter the grown-up PIN:") : Loc.L("Гэр бүлийн том хүний ПИН кодоо оруулаарай:", "Enter the family's grown-up PIN:"), center);
                input = g.NumPad(input, 6, true);
            }
            else if (step == "words")
            {
                GUILayout.Label(Loc.L("Энэ тоог цифрээр бичээрэй:", "Type this number in digits:"), center);
                GUILayout.Label("<b>" + Loc.L(Words.Mn(number), Words.En(number)) + "</b>", center);
                input = g.NumPad(input, 5);
            }
            else if (step == "new" || step == "again")
            {
                GUILayout.Label(step == "new" ? Loc.L("Том хүний ПИН код сонгоорой (4-6 тоо). Хүүхдүүддээ битгий хэлээрэй.", "Choose a grown-up PIN (4-6 digits). Keep it from the children.") : Loc.L("ПИН кодоо дахин оруулаарай:", "Enter the PIN once more:"), center);
                input = g.NumPad(input, 6, true);
            }
            else if (step == "rec")
            {
                GUILayout.Label(Loc.L("Бичиж авсан сэргээх кодоо оруулаарай:", "Type the recovery code you wrote down:"), center);
                rec = g.Field(rec, 11, "ABCDE-FGH23");
            }
            else
            {
                RecoveryCard(g, shown);
                if (g.Btn(Loc.L("Бичиж авлаа", "I wrote it down"), true, 44)) g.Later(() => Pass(g, first));
                return;
            }
            if (locked) GUILayout.Label($"<color=#e14b3d>{DevicePin.Locked(app)}</color>", center);
            else if (err != "") GUILayout.Label($"<color=#e14b3d>{err}</color>", center);
            GUILayout.BeginHorizontal();
            if (g.Btn(Loc.L("Болих", "Cancel"), false, 40, !busy)) g.Later(g.Close);
            if (g.Btn(busy ? Loc.L("Шалгаж байна…", "Checking…") : step == "pin" ? Loc.L("Нэвтрэх", "Enter") : Loc.L("Цааш →", "Next →"), true, 40, !busy && !locked)) g.Later(() => Next(g));
            GUILayout.EndHorizontal();
            if (step == "pin" && g.Btn(Loc.L("ПИН кодоо мартсан уу?", "Forgot the PIN?"), false, 32, !busy)) g.Later(() => { step = "rec"; rec = ""; err = ""; });
        }
        void Next(Game g)
        {
            string typed = input; input = ""; err = "";
            switch (step)
            {
                case "pin": busy = true; g.StartCoroutine(CheckPin(g, typed)); return;
                case "words":
                    if (typed == number.ToString()) step = "new"; else { err = Loc.L("Тоо таарсангүй. Дахин оролдоорой.", "That number doesn't match. Try again."); Sfx.Bad(); }
                    return;
                case "new":
                    if (DevicePin.Valid(typed)) { first = typed; step = "again"; } else err = Loc.L("4-6 тоо оруулаарай.", "Use 4 to 6 digits.");
                    return;
                case "again":
                    if (typed != first) { err = Loc.L("Хоёр ПИН код зөрлөө. Дахин сонгоорой.", "The two PINs differ. Choose again."); step = "new"; Sfx.Bad(); return; }
                    busy = true; g.StartCoroutine(Done(g));
                    return;
                case "rec":
                    if (Family.Clean(rec).Length != 10) { err = Loc.L("10 үсэг, тоотой кодоо бичээрэй.", "Write the 10-letter code."); return; }
                    recovering = true; step = "new";
                    return;
            }
        }
        static bool Unreached(J r) => r["reason"].S == "offline" || r["reason"].S == "no-server" || r["reason"].S == "busy" || r["reason"].S == "storage";
        static string UNREACHED => Loc.L("Сервертэй холбогдож чадсангүй. Интернэтээ шалгаад дахин оролдоорой.", "Couldn't reach the server. Check the internet and try again.");
        /** The local hash first (a frame later, so "Checking…" shows); a family device then asks the server. */
        System.Collections.IEnumerator CheckPin(Game g, string typed)
        {
            var app = g.App; yield return null;
            bool has = DevicePin.Has(app);
            if (has && DevicePin.Match(app, typed)) { busy = false; Pass(g, typed); yield break; }
            if (Family.Member(g) == null) { busy = false; Wrong(g); yield break; }
            J answer = null;
            yield return Family.CheckPin(g, typed, r => answer = r);
            if (answer["ok"].B) { yield return null; DevicePin.Set(app, typed); busy = false; Pass(g, typed); yield break; } // one PIN: the family's
            busy = false;
            string why = answer["reason"].S;
            if (!has && (why == "needs-pin" || Unreached(answer))) { step = "words"; err = why == "needs-pin" ? "" : UNREACHED; yield break; } // no PIN anywhere this device can see
            if (Unreached(answer)) // the local check really failed: counted, so Wi-Fi off can't mean endless guesses
            {
                DevicePin.Wrong(g.App); Sfx.Bad();
                err = DevicePin.Wait(g.App) > 0 ? "" : Loc.L("Сервертэй холбогдож чадсангүй, мөн энэ ПИН код энэ төхөөрөмж дээр хадгалсантай таарахгүй байна.", "Couldn't reach the server, and this PIN doesn't match the one saved on this device.");
            }
            else if (why == "pin-locked") err = Family.Why(answer);
            else Wrong(g);
        }
        void Wrong(Game g) { DevicePin.Wrong(g.App); Sfx.Bad(); err = DevicePin.Wait(g.App) > 0 ? "" : Loc.L($"ПИН код таарсангүй. {DevicePin.Left(g.App)} оролдлого үлдлээ.", $"That PIN didn't match. {DevicePin.Left(g.App)} tries left."); }
        /** The new PIN is chosen: saved with a new recovery code; after "Forgot?", only once the recovery code is right. One
         *  PBKDF2 a frame, so the screen keeps moving. */
        System.Collections.IEnumerator Done(Game g)
        {
            var app = g.App; string next = DevicePin.NewRecovery();
            yield return null;
            if (recovering && Family.Member(g) != null)
            {
                // a family device: the family's recovery code resets the family PIN, and this device's with it
                J answer = null;
                yield return Family.Recover(g, rec, first, next, r => answer = r);
                if (answer["ok"].B) { yield return DevicePin.Store(app, first, next); busy = false; shown = next; step = "show"; yield break; }
                busy = false; Sfx.Bad(); step = "rec";
                if (Unreached(answer)) { err = UNREACHED; yield break; }
                yield return null;
                // this device's own old code: it must not leave the device and the family with different PINs
                err = DevicePin.MatchRecovery(app, rec) ? Loc.L("Энэ бол энэ төхөөрөмжийн хуучин код. Гэр бүлийн ПИН код өөрчлөгдөөгүй. Гэр бүлийн сэргээх кодыг оруулаарай.", "That is this device's old code. The family PIN is unchanged: please use the family's recovery code.") : Family.Why(answer);
                yield break;
            }
            if (recovering && !DevicePin.MatchRecovery(app, rec))
            {
                DevicePin.Wrong(app); busy = false; Sfx.Bad(); step = "rec";
                err = DevicePin.Wait(app) > 0 ? "" : Loc.L($"Сэргээх код таарсангүй. {DevicePin.Left(app)} оролдлого үлдлээ.", $"That recovery code didn't match. {DevicePin.Left(app)} tries left.");
                yield break;
            }
            if (recovering) yield return null;
            yield return DevicePin.Store(app, first, next);
            busy = false; shown = next; step = "show";
        }
    }
    /** A recovery code, shown once: the only way to reset a forgotten PIN. */
    public static void RecoveryCard(Game g, string code)
    {
        var center = new GUIStyle(g.sLabel) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
        GUILayout.Label(Loc.L("Таны сэргээх код:", "Your recovery code:"), center);
        GUILayout.Label(DevicePin.Show(code), new GUIStyle(g.sBig) { alignment = TextAnchor.MiddleCenter });
        GUILayout.Label(Loc.L("Үүнийг бичиж аваарай, эсвэл зургийг нь дараарай. ПИН кодоо мартвал зөвхөн энэ кодоор шинэчилнэ. Дахин харагдахгүй.", "Write this down or take a photo: it's the only way to reset the PIN. It won't be shown again."), center);
    }
    /** The grown-up PIN on big digit buttons, for a family action (joining, new pictures, taking a child out, erasing). With
     *  `twice` a new PIN is chosen: typed again to be sure. */
    public sealed class PinModal : Modal
    {
        readonly string title, text; readonly Action<string> done; readonly bool twice; string input = "", first, err = "";
        public PinModal(string title, string text, Action<string> done, bool twice = false) { this.title = title; this.text = text; this.done = done; this.twice = twice; }
        public override void Press(Game g) { }
        public override void Draw(Game g)
        {
            var center = new GUIStyle(g.sLabel) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
            g.Picture(Px.Icon("ui:lock"), 40);
            GUILayout.Label(title, new GUIStyle(g.sTitle) { alignment = TextAnchor.MiddleCenter });
            GUILayout.Label(first == null ? text : Loc.L("ПИН кодоо дахин оруулаарай:", "Enter the PIN once more:"), center);
            input = g.NumPad(input, 6, true);
            if (err != "") GUILayout.Label($"<color=#e14b3d>{err}</color>", center);
            GUILayout.BeginHorizontal();
            if (g.Btn(Loc.L("Болих", "Cancel"))) g.Later(g.Close);
            if (g.Btn(Loc.L("Болсон", "OK"), true)) g.Later(() =>
            {
                string typed = input; input = ""; err = "";
                if (!DevicePin.Valid(typed)) { err = Loc.L("4-6 тоо оруулаарай.", "Use 4 to 6 digits."); return; }
                if (twice && first == null) { first = typed; return; }
                if (twice && typed != first) { first = null; err = Loc.L("Хоёр ПИН код зөрлөө. Дахин сонгоорой.", "The two PINs differ. Choose again."); Sfx.Bad(); return; }
                g.Close(); done(typed);
            });
            GUILayout.EndHorizontal();
        }
    }
    /** A recovery code to write down, shown once, then `next`. */
    public sealed class RecoveryModal : Modal
    {
        readonly string code; readonly Action next;
        public RecoveryModal(string code, Action next = null) { this.code = code; this.next = next; }
        public override void Press(Game g) { }
        public override void Draw(Game g)
        {
            g.Picture(Px.Icon("ui:lock"), 40);
            RecoveryCard(g, code);
            if (g.Btn(Loc.L("Бичиж авлаа", "I wrote it down"), true, 44)) g.Later(() => { g.Close(); next?.Invoke(); });
        }
    }
    /** The 3×3 secret pictures: tapped ones fill the 3 dots; the third calls onPick with the code ("0-4-2"). */
    public void PicPad(List<int> sel, Action<string> onPick)
    {
        GUILayout.BeginHorizontal(); GUILayout.FlexibleSpace();
        for (int k = 0; k < 3; k++) { var r = GUILayoutUtility.GetRect(U(36), U(36), GUILayout.Width(U(36))); GUI.Box(r, "", sBtn); if (k < sel.Count) GUI.DrawTexture(new Rect(r.x + U(4), r.y + U(4), r.width - U(8), r.height - U(8)), Tex(Px.PicKey(sel[k]))); }
        GUILayout.FlexibleSpace(); GUILayout.EndHorizontal();
        for (int row = 0; row < 3; row++)
        {
            GUILayout.BeginHorizontal();
            for (int c = 0; c < 3; c++)
            {
                int i = row * 3 + c; var r = GUILayoutUtility.GetRect(U(60), U(56), GUILayout.ExpandWidth(true));
                if (GUI.Button(r, "", sTile) && sel.Count < 3) { Sfx.Tap(); Later(() => { sel.Add(i); if (sel.Count == 3) { string code = string.Join("-", sel); onPick(code); } }); }
                float s = Mathf.Min(r.height, r.width) - U(14); GUI.DrawTexture(new Rect(r.center.x - s / 2, r.center.y - s / 2, s, s), Tex(Px.PicKey(i)));
            }
            GUILayout.EndHorizontal();
        }
    }
    /** Big number buttons (easier than the phone keyboard for little ones), up to `max` digits; `hide` shows a PIN as stars. */
    public string NumPad(string v, int max = 3, bool hide = false)
    {
        GUILayout.Label(v == "" ? " " : hide ? new string('*', v.Length) : v, new GUIStyle(sBtn) { fontSize = Ts(22), fontStyle = FontStyle.Bold }, GUILayout.MinHeight(U(44)));
        string[] keys = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "←", "0", "OK" };
        for (int r = 0; r < 4; r++)
        {
            GUILayout.BeginHorizontal();
            for (int c = 0; c < 3; c++) { string k = keys[r * 3 + c]; if (k == "OK") { GUILayout.Label("", GUILayout.MinHeight(U(40))); continue; } if (GUILayout.Button(k, sBtn, GUILayout.MinHeight(U(40)))) { Sfx.Tap(); v = k == "←" ? (v.Length > 0 ? v.Substring(0, v.Length - 1) : "") : v.Length < max ? v + k : v; } }
            GUILayout.EndHorizontal();
        }
        return v;
    }

    // ================================================================ pages: full-screen screens with a title bar and scrolling content
    public abstract class Page : Modal
    {
        public override bool FullScreen => true;
        protected Vector2 scroll; protected virtual bool Back => true; protected virtual string Bg => "#f6f4ef";
        protected abstract string Title { get; }
        protected abstract void Body(Game g);
        protected virtual void Header(Game g) { }
        /** Pages of the app (web version's shell): the header bar and the tab bar. The sign-up and the character creator stand alone. */
        protected virtual bool Shell => true;
        /** The page opens with its own hero card, so no title line above it (as on the web version). */
        protected virtual bool Hero => false;
        /** Which tab of the tab bar this page belongs to. */
        protected virtual string Tab => null;
        public override void Press(Game g) { }
        public override void Draw(Game g)
        {
            g.Box(new Rect(0, 0, Screen.width, Screen.height), Px.Col(Bg));
            var safe = Screen.safeArea; var area = new Rect(safe.x, Screen.height - safe.yMax, safe.width, safe.height);
            if (!Shell)
            {
                float w0 = Mathf.Min(area.width - g.U(16), g.U(760));
                GUILayout.BeginArea(new Rect(area.x + (area.width - w0) / 2, area.y + g.U(8), w0, area.height - g.U(16)));
                GUILayout.BeginHorizontal();
                if (Back && GUILayout.Button("←", g.sBtn, GUILayout.Width(g.U(48)), GUILayout.Height(g.U(44)))) g.Later(g.Close);
                GUILayout.Label(Title, new GUIStyle(g.sTitle) { alignment = TextAnchor.MiddleLeft }, GUILayout.Height(g.U(44)));
                GUILayout.FlexibleSpace(); Header(g);
                GUILayout.EndHorizontal();
                scroll = g.BeginScroll(scroll); Body(g); g.EndScroll();
                GUILayout.EndArea();
                return;
            }
            // the header bar: (back), the logo that goes home, then the language and your profile (it opens the menu)
            float hb = g.U(62);
            g.Box(new Rect(0, 0, Screen.width, area.y + hb), Px.Col("#f2f0ea")); g.Box(new Rect(0, area.y + hb - g.U(1), Screen.width, g.U(1)), Px.Col("#e3ded3"));
            GUILayout.BeginArea(new Rect(area.x + g.U(16), area.y + g.U(12), area.width - g.U(32), g.U(40)));
            GUILayout.BeginHorizontal();
            bool nested = false; foreach (var m in g.under) if (m is Page) nested = true;
            if (nested && Back) { if (GUILayout.Button("←", g.sWhite, GUILayout.Width(g.U(38)), GUILayout.Height(g.U(38)))) g.Later(g.Close); GUILayout.Space(g.U(10)); }
            var br = GUILayoutUtility.GetRect(g.U(150), g.U(38), GUILayout.Width(g.U(150)));
            if (GUI.Button(br, "", GUIStyle.none)) g.Later(g.CloseAll);
            g.Logo(new Rect(br.x, br.y + g.U(2), g.U(34), g.U(34)));
            GUI.Label(new Rect(br.x + g.U(44), br.y, br.width - g.U(44), br.height), "Naadamy", new GUIStyle(g.sTitle) { fontSize = Mathf.RoundToInt(g.U(21)), alignment = TextAnchor.MiddleLeft, normal = { textColor = Px.Col("#141b2d") } });
            GUILayout.FlexibleSpace(); Header(g); GUILayout.Space(g.U(8));
            var chip = new GUIStyle(g.sWhite) { fontSize = Mathf.RoundToInt(g.U(13)), alignment = TextAnchor.MiddleLeft, normal = { textColor = Px.Col("#344054") } };
            var lr = GUILayoutUtility.GetRect(g.U(66), g.U(38), GUILayout.Width(g.U(66)));
            if (GUI.Button(lr, "", g.sWhite)) { Loc.En = !Loc.En; g.App.en = Loc.En; g.App.Save(); }
            GUI.DrawTexture(new Rect(lr.x + g.U(12), lr.y + g.U(11), g.U(16), g.U(16)), g.Tex(Emo.Icon("🌐")), ScaleMode.ScaleToFit);
            GUI.Label(new Rect(lr.x + g.U(32), lr.y, g.U(32), lr.height), Loc.En ? "МН" : "EN", chip);
            GUILayout.Space(g.U(8));
            var V = g.V; float nw = Mathf.Min(150, chip.CalcSize(new GUIContent(V.name)).x / g.UiScale + 52);
            var pr = GUILayoutUtility.GetRect(g.U(nw), g.U(38), GUILayout.Width(g.U(nw)));
            if (GUI.Button(pr, "", g.sWhite)) g.Later(() => g.Open(new MenuModal()));
            g.Box(new Rect(pr.x + g.U(4), pr.y + g.U(4), g.U(30), g.U(30)), Px.Col("#ffe8c4")); GUI.DrawTexture(new Rect(pr.x + g.U(7), pr.y + g.U(7), g.U(24), g.U(24)), g.Tex(Px.Mascot(V.avatar)), ScaleMode.ScaleToFit);
            GUI.Label(new Rect(pr.x + g.U(40), pr.y, pr.width - g.U(44), pr.height), V.name, new GUIStyle(chip) { fontSize = Mathf.RoundToInt(g.U(14)), normal = { textColor = Px.Col("#141b2d") } });
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
            // the page: up to 1000 wide, with room at the bottom for the tab bar
            float w = Mathf.Min(area.width - g.U(32), g.U(968));
            GUILayout.BeginArea(new Rect(area.x + (area.width - w) / 2, area.y + hb + g.U(18), w, area.height - hb - g.U(18)));
            scroll = g.BeginScroll(scroll);
            if (!Hero) GUILayout.Label(Title, new GUIStyle(g.sTitle) { fontSize = Mathf.RoundToInt(g.U(28)), normal = { textColor = Px.Col("#141b2d") } });
            Body(g);
            GUILayout.Space(g.U(96));
            g.EndScroll();
            GUILayout.EndArea();
            // the tab bar, floating at the bottom
            float tw = Mathf.Min(area.width - g.U(24), g.U(560)), th = g.U(64);
            var nav = new Rect(area.x + (area.width - tw) / 2, area.yMax - g.U(10) - th, tw, th); g.UiAdd(nav);
            GUI.Box(nav, "", g.sNav);
            var tabs = new (string id, PxCanvas icon, string name)[] { ("village", Emo.Icon("🌱"), Loc.L("Нутаг", "Nutag")), ("learn", Px.Icon("ui:book"), Loc.L("Сурах", "Learn")), ("basar", Px.BankharFace(0), Loc.L("Басар", "Basar")), ("achievements", Emo.Icon("✨"), Loc.L("Амжилт", "Badges")), ("family", Emo.Icon("👨‍👩‍👧"), Loc.L("Гэр бүл", "Family")) };
            float cw = (tw - g.U(12)) / tabs.Length;
            for (int i = 0; i < tabs.Length; i++)
            {
                var (id, icon, name) = tabs[i]; bool on = Tab == id;
                var r = new Rect(nav.x + g.U(6) + i * cw, nav.y + g.U(6), cw - g.U(2), th - g.U(12));
                if (GUI.Button(r, "", on ? g.sNavOn : GUIStyle.none)) g.Later(() =>
                {
                    if (id == "basar") { g.OpenBasar(); return; }
                    g.CloseAll();
                    if (id == "learn") g.Open(new LearnPage()); else if (id == "achievements") g.Open(new AchievementsPage()); else if (id == "family") g.OpenFamily();
                });
                GUI.DrawTexture(new Rect(r.center.x - g.U(11), r.y + g.U(5), g.U(22), g.U(22)), g.Tex(icon), ScaleMode.ScaleToFit);
                GUI.Label(new Rect(r.x, r.y + g.U(29), r.width, g.U(16)), name, new GUIStyle(g.sLabel) { fontSize = Mathf.RoundToInt(g.U(11)), alignment = TextAnchor.MiddleCenter, wordWrap = false, clipping = TextClipping.Clip, normal = { textColor = Px.Col(on ? "#2f6bff" : "#6b7280") } });
            }
        }
    }

    // ================================================================ log in or register
    public sealed class AuthPage : Page
    {
        string tab;
        // registering
        int step; string name = "", err = ""; const int age = 8; int avatar; readonly Live live = new Live(160, 90);
        // logging in with an email account (server/email-auth.js); acctId: a confirmed account whose steps were left unfinished
        string email = "", pass = "", pass2 = "", acctId = "", acctTicket = ""; bool busy;
        J acct; bool adding; // acct: the login's answer, while choosing one of its 2 players · adding: the steps make the second player
        readonly SaveData guest; // a player on this device with no email: only an older save can land here, since a guest has no log out
        public AuthPage()
        {
            tab = I.App.profiles.Count > 0 ? "login" : "register";
            guest = I.App.profiles.Select(SaveData.Load).OrderByDescending(p => p.id == I.App.activeId).FirstOrDefault(p => p.email == "");
        }
        protected override bool Back => false;
        public override bool CanEscape => false;
        protected override bool Shell => false;
        protected override string Title => "Naadamy";
        protected override string Bg => "#76b947";
        protected override void Header(Game g) { if (GUILayout.Button(Loc.En ? "МН" : "EN", g.sBtn, GUILayout.Width(g.U(56)), GUILayout.Height(g.U(40)))) { Loc.En = !Loc.En; g.App.en = Loc.En; g.App.Save(); } }
        public override void Draw(Game g)
        {
            // the animated steppe behind: gers, galloping foals, sheep and Basar running along
            var x = live.c; float t = Time.time; var bg = new PxCanvas(Scenes.W, Scenes.H); Scenes.Bg(bg, t); x.BlitScaled(bg, 0, 0, 160, 90);
            x.BlitScaled(Px.GerSprite("#3f76db", "#e14b3d"), 18, 50, 26, 27); x.BlitScaled(Px.GerSprite("#e14b3d", "#e9a91f"), 112, 54, 22, 23);
            int[][] sh = { new[] { 60, 74 }, new[] { 76, 78 }, new[] { 92, 72 } }; for (int i = 0; i < 3; i++) x.Blit(Px.Animal("sheep", (int)(t * 2 + i) % 2), sh[i][0], sh[i][1]);
            float run = (t * 22) % 220 - 30; int f = 1 + (int)(t * 8) % 2;
            x.Blit(Px.Animal("foal", f), Js.Round(run), 64); x.Blit(Px.Animal("foal", 3 - f), Js.Round(run - 18), 67); x.Blit(Px.Bankhar(f), Js.Round(run - 42), 66);
            float k = Mathf.Max(Screen.width / 160f, Screen.height / 90f); live.ShowAt(new Rect((Screen.width - 160 * k) / 2, (Screen.height - 90 * k) / 2, 160 * k, 90 * k));
            // as the web version's sign-up: the logo, a big title over the steppe, two tabs, then the card
            var safe = Screen.safeArea; float top = Screen.height - safe.yMax, w = Mathf.Min(safe.width - g.U(32), g.U(460));
            var er = new Rect(safe.xMax - g.U(16) - g.U(52), top + g.U(16), g.U(52), g.U(38));
            if (GUI.Button(er, Loc.En ? "МН" : "EN", new GUIStyle(g.sWhite) { fontSize = Mathf.RoundToInt(g.U(13)) })) { Loc.En = !Loc.En; g.App.en = Loc.En; g.App.Save(); }
            GUILayout.BeginArea(new Rect(safe.x + (safe.width - w) / 2, top + g.U(16), w, safe.height - g.U(32)));
            GUILayout.FlexibleSpace();
            var lr = GUILayoutUtility.GetRect(w, g.U(72)); g.Logo(new Rect(lr.center.x - g.U(36), lr.y, g.U(72), g.U(72)));
            void Shadowed(string text, GUIStyle st, float h) { var r = GUILayoutUtility.GetRect(w, g.U(h)); var sh = new GUIStyle(st) { normal = { textColor = new Color(0.17f, 0.12f, 0.18f, 0.55f) } }; GUI.Label(new Rect(r.x, r.y + g.U(2), r.width, r.height), text, sh); GUI.Label(r, text, st); }
            Shadowed("Naadamy", new GUIStyle(g.sBig) { fontSize = Mathf.RoundToInt(g.U(46)), normal = { textColor = Color.white } }, 56);
            Shadowed(Loc.L("Монгол өв соёлоо гэр бүл, найзуудтайгаа хамт сурцгаая", "Learn Mongolian heritage together with family and friends"), new GUIStyle(g.sCenter) { fontSize = Mathf.RoundToInt(g.U(14)), normal = { textColor = Color.white } }, 22);
            g.Gap(12);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Loc.L("Нэвтрэх", "Log in"), tab == "login" ? g.sBtn : g.sTabOff, GUILayout.MinHeight(g.U(44)))) g.Later(() => { tab = "login"; adding = false; err = ""; });
            GUILayout.Space(g.U(8));
            if (GUILayout.Button(Loc.L("Шинээр тоглох", "New player"), tab == "register" ? g.sBtn : g.sTabOff, GUILayout.MinHeight(g.U(44)))) g.Later(() => { tab = "register"; adding = false; step = 0; err = ""; });
            GUILayout.EndHorizontal();
            g.Gap(10);
            GUILayout.BeginVertical(g.sPanel);
            scroll = g.BeginScroll(scroll, safe.height - g.U(330)); // whatever is left under the header; the rest scrolls
            if (tab == "login") LogIn(g); else Register(g);
            g.EndScroll();
            GUILayout.EndVertical();
            g.Gap(8);
            if (GUILayout.Button("<u>" + Loc.L("Төслийн тухай", "About the project") + "</u>", new GUIStyle(g.sCenter) { fontSize = Mathf.RoundToInt(g.U(14)), normal = { textColor = Color.white } })) g.Later(() => g.Open(new AboutPage()));
            GUILayout.FlexibleSpace();
            GUILayout.EndArea();
        }
        protected override void Body(Game g) { }
        GUIStyle Box(Game g) => new GUIStyle(g.sBtn) { alignment = TextAnchor.MiddleLeft };
        void Error(Game g) { if (err != "") GUILayout.Label($"<color=#e14b3d>{err}</color>", g.sLabel); }
        public static string Why(J r)
        {
            switch (r["reason"].S)
            {
                case "weak-password": return Loc.L("Нууц үг 8-аас дээш тэмдэгттэй байна.", "The password needs 8 or more characters.");
                case "taken": return Loc.L("Энэ имэйл бүртгэлтэй байна. Нэвтрэх хэсгээр орно уу.", "This email already has an account. Please log in.");
                case "wrong": return Loc.L("Имэйл эсвэл нууц үг буруу байна.", "Wrong email or password.") + (r.Has("left") ? Loc.L($" {r["left"].I} оролдлого үлдлээ.", $" {r["left"].I} tries left.") : "");
                case "code": return Loc.L($"Код буруу байна. {r["left"].I} оролдлого үлдлээ.", $"Wrong code. {r["left"].I} tries left.");
                case "expired": return Loc.L("Кодын хугацаа дууссан. Шинэ код аваарай.", "That code has expired. Get a new one.");
                case "full": return Loc.L("Нэг имэйлд 2 хүртэл профайл байна.", "An email can have up to 2 profiles.");
                default: return Family.Why(r);
            }
        }
        void Ask(Game g, string body, Action<J> done)
        {
            busy = true; err = "";
            g.StartCoroutine(ArenaApi.Post(Family.ServerOf(g, null), body, r => { busy = false; if (r["ok"].B) done(r); else { err = Why(r); Sfx.Bad(); } }, "/api/auth"));
        }
        /** The address and password. The server makes the player at once, and the steps fill it in. */
        void SignUp(Game g)
        {
            GUILayout.Label("<b>" + Loc.L("Имэйлээр бүртгүүлэх", "Sign up with email") + "</b>", g.sLabel);
            email = g.Field(email, 254, Loc.L("Имэйл хаяг", "Email address")); g.Gap(6);
            g.Small(Loc.L("Нууц үг (8+ тэмдэгт)", "Password (8+ characters)"));
            pass = GUILayout.PasswordField(pass, '•', 64, Box(g), GUILayout.MinHeight(g.U(38)));
            g.Small(Loc.L("Нууц үгээ давтана уу", "Password again"));
            pass2 = GUILayout.PasswordField(pass2, '•', 64, Box(g), GUILayout.MinHeight(g.U(38))); g.Gap(10);
            Error(g);
            if (g.Btn(busy ? "…" : Loc.L("Бүртгүүлэх", "Sign up"), true, 48, !busy)) g.Later(() =>
            {
                if (!Family.EmailOk(email.Trim())) { err = Family.Why(J.Parse("{\"reason\":\"email-invalid\"}")); return; }
                if (pass != pass2) { err = Loc.L("Хоёр нууц үг таарахгүй байна.", "The two passwords don't match."); return; }
                Ask(g, ArenaApi.Body("register", "email", email.Trim(), "password", pass), r => { acctId = r["id"].S; acctTicket = r["ticket"].S; pass = pass2 = ""; step = 0; });
            });
        }
        void LogIn(Game g)
        {
            if (acct != null) { Choose(g); return; }
            if (guest != null)
            {
                if (g.Btn(Loc.L($"{guest.name}-ээр үргэлжлүүлэх", $"Continue as {guest.name}"), true, 44)) g.Later(() => g.Enter(guest));
                g.Gap(10);
            }
            EmailLogin(g);
        }
        /** After the login: which of the account's players plays, or make the second one. */
        void Choose(Game g)
        {
            GUILayout.Label("<b>" + Loc.L("Профайлаа сонгоно уу", "Choose a profile") + "</b>", g.sLabel);
            void Row(J p)
            {
                GUILayout.BeginHorizontal(g.sBtn);
                g.IconAt(Px.Mascot(p["avatar"].I), 40);
                GUILayout.Label($"<b>{p["name"].S}</b>", g.sLabel);
                GUILayout.FlexibleSpace();
                if (g.SmallBtn(Loc.L("Тоглох", "Play"))) g.Later(() => Play(g, p));
                GUILayout.EndHorizontal();
            }
            Row(acct);
            if (acct.Has("second")) Row(acct["second"]);
            else if (g.Btn(Loc.L("+ 2 дахь профайл нэмэх", "+ Add a second profile"), false, 40)) g.Later(() => { adding = true; tab = "register"; step = 0; name = ""; avatar = 0; err = ""; });
        }
        /** Into the game as one player of the login's answer. */
        void Play(Game g, J a)
        {
            string server = Family.ServerOf(g, null), pid = a["id"].S; bool known = g.App.profiles.Contains(pid);
            var p = SaveData.Load(pid);
            if (!known) { p.name = a["name"].S; p.age = a["age"].I; p.avatar = a["avatar"].I; p.created = SaveData.DayKey(); }
            p.ticket = a["ticket"].S; // this device's own ticket
            p.email = email.Trim();
            Cloud.Apply(p, a["cloud"]); // character, lessons, badges… from the other device
            if (string.IsNullOrWhiteSpace(p.server)) p.server = server.Trim();
            p.Save();
            if (!known) g.App.profiles.Add(pid);
            g.App.server = server.Trim(); g.App.Save();
            g.Enter(p);
        }
        void EmailLogin(Game g)
        {
            GUILayout.Label("<b>" + Loc.L("Имэйлээр нэвтрэх", "Log in with email") + "</b>", g.sLabel);
            email = g.Field(email, 254, Loc.L("Имэйл хаяг", "Email address")); g.Gap(6);
            g.Small(Loc.L("Нууц үг", "Password"));
            pass = GUILayout.PasswordField(pass, '•', 64, Box(g), GUILayout.MinHeight(g.U(38))); g.Gap(10);
            Error(g);
            if (g.Btn(busy ? "…" : Loc.L("Нэвтрэх", "Log in"), true, 48, !busy)) g.Later(() => Ask(g, ArenaApi.Body("login", "email", email.Trim(), "password", pass), r =>
            {
                pass = "";
                if (!r["ready"].B) { acctId = r["id"].S; acctTicket = r["ticket"].S; tab = "register"; step = 0; return; } // confirmed, but the steps were left unfinished
                acct = r;
            }));
        }
        void Basar(Game g, string text) { GUILayout.BeginHorizontal(); g.IconAt(Px.BankharFace(1), 40); g.Gap(10); GUILayout.Label(text, g.sTeach); GUILayout.EndHorizontal(); g.Gap(8); }
        void Register(Game g)
        {
            if (acctId == "" && !adding) { SignUp(g); return; } // an email first: every player is kept on the server
            g.Steps(step, 2);
            if (step == 0)
            {
                Basar(g, Loc.L("Вуф! Би Басар. Чиний нэрийг хэн гэдэг вэ?", "Woof! I’m Basar. What’s your name?"));
                name = g.Field(name, 20, Loc.L("Нэрээ бич...", "Write your name...")); g.Gap(14);
                if (err != "") GUILayout.Label($"<color=#e14b3d>{err}</color>", g.sLabel);
                if (g.Btn(Loc.L("Цааш →", "Next →"), true, 54)) g.Later(() => { if (name.Trim() == "") err = Loc.L("Нэрээ бичээрэй", "Please write your name"); else { err = ""; name = name.Trim(); step = 1; } });
            }
            else
            {
                Basar(g, Loc.L("Өөрийн сахиус амьтнаа сонго! ", "Pick your mascot animal! ") + Profiles.MASCOTS[avatar].fact);
                for (int r = 0; r < 3; r++)
                {
                    GUILayout.BeginHorizontal();
                    for (int c = 0; c < 4; c++)
                    {
                        int i = r * 4 + c; var rr = GUILayoutUtility.GetRect(g.U(70), g.U(72), GUILayout.ExpandWidth(true));
                        if (GUI.Button(rr, "", i == avatar ? g.sSel : g.sTile)) { avatar = i; Sfx.Pop(); }
                        GUI.DrawTexture(new Rect(rr.center.x - g.U(18), rr.y + g.U(4), g.U(36), g.U(36)), g.Tex(Px.Mascot(i)));
                        GUI.Label(new Rect(rr.x + g.U(2), rr.y + g.U(40), rr.width - g.U(4), g.U(30)), Profiles.MASCOTS[i].name.ToString(), new GUIStyle(g.sLabel) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(g.U(10.5f)) });
                    }
                    GUILayout.EndHorizontal();
                }
                Error(g);
                GUILayout.BeginHorizontal();
                if (g.Btn("←")) g.Later(() => step = 0);
                if (g.Btn(busy ? "…" : Loc.L("Тоглох →", "Play →"), true, 40, !busy)) g.Later(() => Finish(g));
                GUILayout.EndHorizontal();
            }
        }
        void Finish(Game g)
        {
            string server = Family.ServerOf(g, null);
            void Start(SaveData p, bool account)
            {
                p.server = server.Trim(); if (account) p.email = email.Trim(); p.Save();
                g.App.profiles.Add(p.id); g.App.server = server.Trim(); g.App.Save();
                g.Enter(p);
            }
            if (adding)
            {
                string body = "{\"act\":\"add\",\"id\":" + J.Quote(acct["id"].S) + ",\"ticket\":" + J.Quote(acct["ticket"].S) + ",\"name\":" + J.Quote(name) + ",\"age\":" + age + ",\"avatar\":" + avatar + "}";
                Ask(g, body, r => Start(SaveData.Create(name, age, avatar, "", r["id"].S, r["ticket"].S), true));
                return;
            }
            var p = SaveData.Create(name, age, avatar, "", acctId, acctTicket);
            // kept on the server for the next device; if this is lost, the next login asks these steps again
            g.StartCoroutine(ArenaApi.Post(server, ProfileBody(p), r => { }, "/api/auth"));
            Start(p, true);
        }
        public override void Closed(Game g) { }
    }

    /** What POST /api/auth keeps for the next device: the name and mascot. */
    static string ProfileBody(SaveData p) => "{\"act\":\"profile\",\"id\":" + J.Quote(p.id) + ",\"ticket\":" + J.Quote(p.ticket) + ",\"name\":" + J.Quote(p.name) + ",\"age\":" + p.age + ",\"avatar\":" + p.avatar + "}";

    /** A guest links an email: the address and password. The player stays the same, and can now log in on any device. */
    public sealed class EmailLinkModal : Modal
    {
        string email = "", pass = "", pass2 = "", err = ""; bool busy;
        readonly bool must; // an older save with no email: it can't be closed until one is linked
        public EmailLinkModal(bool must = false) { this.must = must; }
        public override bool CanEscape => !must;
        void Ask(Game g, string body, Action<J> done)
        {
            busy = true; err = "";
            g.StartCoroutine(ArenaApi.Post(Family.ServerOf(g, g.V), body, r => { busy = false; if (r["ok"].B) done(r); else { err = AuthPage.Why(r); Sfx.Bad(); } }, "/api/auth"));
        }
        public override void Draw(Game g)
        {
            var box = new GUIStyle(g.sBtn) { alignment = TextAnchor.MiddleLeft };
            void Error() { if (err != "") GUILayout.Label($"<color=#e14b3d>{err}</color>", g.sLabel); }
            g.Title(Loc.L("Имэйл холбох", "Link an email"));
            g.Small(must ? Loc.L("Тоглоомоо хадгалахын тулд имэйл, нууц үгээ бичээрэй.", "Write an email and password to keep your game.")
                : Loc.L("Холбовол өөр төхөөрөмж дээр нэвтэрч, зоос, XP, хувцсаа авч болно.", "Link one to log in on another device and keep your coins, XP and clothes."));
            email = g.Field(email, 254, Loc.L("Имэйл хаяг", "Email address")); g.Gap(6);
            g.Small(Loc.L("Нууц үг (8+ тэмдэгт)", "Password (8+ characters)"));
            pass = GUILayout.PasswordField(pass, '•', 64, box, GUILayout.MinHeight(g.U(38)));
            g.Small(Loc.L("Нууц үгээ давтана уу", "Password again"));
            pass2 = GUILayout.PasswordField(pass2, '•', 64, box, GUILayout.MinHeight(g.U(38))); g.Gap(10);
            Error();
            if (g.Btn(busy ? "…" : Loc.L("Холбох", "Link"), true, 48, !busy)) g.Later(() =>
            {
                if (!Family.EmailOk(email.Trim())) { err = Family.Why(J.Parse("{\"reason\":\"email-invalid\"}")); return; }
                if (pass != pass2) { err = Loc.L("Хоёр нууц үг таарахгүй байна.", "The two passwords don't match."); return; }
                var V = g.V; SaveData.EnsureTicket(V);
                Ask(g, ArenaApi.Body("register", "email", email.Trim(), "password", pass, "id", V.id, "ticket", V.ticket), r =>
                {
                    V.email = email.Trim(); V.Save(); pass = pass2 = "";
                    g.StartCoroutine(ArenaApi.Post(Family.ServerOf(g, V), ProfileBody(V), a => { }, "/api/auth"));
                    g.Toast(Loc.L("Имэйл холбогдлоо!", "Email linked!")); g.Close();
                });
            });
        }
    }
}
