// Бөх: Mongolian wrestling at the Naadam arena. The herald calls both names, you do the eagle dance, then each move is
// done with its own skill (timing, footwork, power, grip, balance). 9 rounds, titles from Nachin to Avarga. The same bout
// is played live with a friend in the village room (wr-*) and in the online arena (ar-*, Arena.cs), where the server
// weighs both moves.
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed partial class Game
{
    const string RED = "#d8342b", BLUE = "#2b5bd8"; // traditional zodog colours
    string MySkin => Rules.SKINS.Contains(V.look.skin) ? V.look.skin : Rules.SKINS[0];

    sealed class ArenaModal : Modal
    {
        public override (string who, string face)? Speaker => ("zasuul", "proud");
        readonly Tx fact = Rules.Pick(Rules.WR_FACTS);
        public override void Draw(Game g)
        {
            var wr = g.V.wr; var t = Rules.TitleById(Rules.TitleFor(wr));
            var next = Rules.TITLES.FirstOrDefault(q => q.id == "avarga" ? wr.champ < 2 : wr.best < q.need);
            GUILayout.BeginHorizontal(); g.IconAt(Px.Wrestler(RED, g.MySkin, "dance", 0), 64);
            GUILayout.BeginVertical(); g.Title(Loc.L("Бөхийн барилдаан", "Mongolian wrestling")); g.Small(t != null ? "♦ " + t.name : Loc.L("Цолгүй бөх", "No title yet")); GUILayout.EndVertical(); GUILayout.EndHorizontal();
            g.Stats((Loc.L("Шилдэг давлага", "Best rounds"), wr.best.ToString()), (Loc.L("Түрүү", "Wins"), wr.champ.ToString()), (Loc.L("Ялалт", "Victories"), (wr.wins + wr.pvpW).ToString()));
            g.Small(next == null ? Loc.L("Та хамгийн дээд цол Аварга хүртсэн!", "You hold the highest title, Avarga!")
                : next.id == "avarga" ? Loc.L("Наадамд дахин түрүүлбэл Аварга цол хүртэнэ!", "Win the Naadam once more to become an Avarga!")
                : Loc.L($"Наадамд {next.need} давбал «{next.name}» цол хүртэнэ.", $"Win {next.need} rounds at the Naadam to earn the \"{next.name}\" title."));
            Tiles(g,
                (Loc.L("Чөлөөтэй барилдах", "Wrestle freely"), Loc.L("Наадамд 9 даваа, цол ав", "9 Naadam rounds, earn titles"), true, () => g.Swap(WrestleModal.Tournament(g, 1))),
                (Loc.L("Барилдаж сурах", "Learn to wrestle"), wr.school > 0 ? Loc.L("Мэхүүдийг дахин үзэх", "Go over the moves again") : Loc.L("Мэх бүрийг энгийнээр", "Every move, made simple"), false, () => g.Swap(new SchoolModal())),
                (Loc.L("Бөхийн өргөө · онлайн", "Wrestling Arena · online"), Loc.L("Найзтайгаа бодит цагт, чансаа", "Live bouts with friends, rankings"), true, () => g.Swap(new ArenaHubModal())),
                (Loc.L("Мэхний ном", "Technique book"), Loc.L($"{Rules.MOVES.Length} мэх, юу юуг давдаг", $"{Rules.MOVES.Length} moves, what beats what"), false, () => g.Open(new TechBookModal())));
            g.Fact(fact.ToString());
        }
        /** The arena's choices as a grid of equal tiles, two to a row: a bold name over one short line. */
        static void Tiles(Game g, params (string title, string sub, bool primary, System.Action act)[] tiles)
        {
            float h = g.U(56), gap = g.U(6);
            for (int i = 0; i < tiles.Length; i += 2)
            {
                var row = GUILayoutUtility.GetRect(10, h, GUILayout.ExpandWidth(true)); float w = (row.width - gap) / 2;
                for (int k = 0; k < 2 && i + k < tiles.Length; k++)
                {
                    var tl = tiles[i + k]; var r = new Rect(row.x + k * (w + gap), row.y, w, h);
                    if (GUI.Button(r, $"<b>{tl.title}</b>\n<size={g.Ts(10)}>{tl.sub}</size>", tl.primary ? g.sPrimary : g.sBtn)) g.Later(tl.act);
                }
                GUILayout.Space(gap);
            }
        }
        public override void Press(Game g) => g.Close();
    }
    // ---------------- wrestling school: the basics and every move, told as you'd tell a five-year-old, then a practice bout
    sealed class SkillKid { public Tx name, how; }
    static readonly Dictionary<string, SkillKid> SKILL_KID = new Dictionary<string, SkillKid>
    {
        ["timing"] = new SkillKid { name = new Tx("Цагаа тааруулах", "Tap on time"), how = new Tx("Жижиг зураас нааш цааш гүйнэ. Ногоон дээр ирэхэд нь дар, эрвээхэй барьж байгаа юм шиг!", "A little line runs back and forth. Tap when it’s in the green, like catching a butterfly!") },
        ["hold"] = new SkillKid { name = new Tx("Дараад барих", "Press and hold"), how = new Tx("Товчоо дараад барь. Аяганд сүү хийх шиг зураас дүүрнэ. Шар дээр хүрэхэд нь тавь. Хэт удаан битгий бариарай!", "Press the button and keep holding. The bar fills up like pouring milk into a cup. Let go when it reaches the yellow. Not too long!") },
        ["combo"] = new SkillKid { name = new Tx("Хөлийн хөдөлгөөн", "Happy feet"), how = new Tx("Сумнууд гарч ирнэ. Яг тэр дарааллаар нь хурдан дар. Хөлөөрөө бүжиглэж байгаа юм шиг!", "Arrows pop up. Press them in the same order, quickly. It’s like a little dance with your feet!") },
        ["mash"] = new SkillKid { name = new Tx("Бүх хүчээрээ", "Super strong"), how = new Tx("Аль болох хурдан дар, дар, дар! Дарах бүрт чи улам хүчтэй болно.", "Tap, tap, tap as fast as you can! Every tap makes you stronger.") },
        ["balance"] = new SkillKid { name = new Tx("Тэнцвэр", "Stay steady"), how = new Tx("Найз чинь чамайг нааш цааш түлхэнэ. ◄ ► дарж зүүгээ голд нь байлга. Гуалин дээгүүр алхаж байгаа юм шиг!", "Your friend pushes you this way and that. Press ◄ ► to keep the needle in the middle, like walking along a log!") },
    };
    sealed class SchoolPage { public string[] pic; public Tx title; public Tx[] say; public string skill; public bool last; }
    static readonly SchoolPage[] SCHOOL = new[]
    {
        new SchoolPage { pic = new[] { "grip", "grip" }, title = new Tx("Бөх гэж юу вэ?", "What is wrestling?"), say = new[] { new Tx("Хоёр бөх бие биенээ барьж аваад түлхэлцэж, татацгаана.", "Two wrestlers hold on to each other, then push and pull."), new Tx("Хэн нь түрүүлж унавал тэр нь ялагдана. Яг л тоглоом шиг!", "Whoever falls down first loses. It’s just like a game!") } },
        new SchoolPage { pic = new[] { "dance", "fall" }, title = new Tx("Яаж ялах вэ?", "How do you win?"), say = new[] { new Tx("Найзынхаа өвдөг, тохой, нуруу эсвэл толгойг газарт хүргэвэл чи ялна.", "If your friend’s knee, elbow, back or head touches the ground, you win."), new Tx("Хөл, гар газарт хүрвэл зүгээр. Тэр унасанд тооцогдохгүй.", "Feet and hands on the ground are okay. That doesn’t count as falling.") } },
        new SchoolPage { pic = new[] { "idle", "idle" }, title = new Tx("Бөхийн хувцас", "Wrestler clothes"), say = new[] { new Tx("Зодог бол урд нь задгай, ханцуйтай жижигхэн цамц.", "The zodog is a little jacket with long sleeves and an open front."), new Tx("Шуудаг бол богинохон өмд. Гутал нь хамар нь дээшээ эргэсэн том гутал.", "The shuudag is tiny shorts, and the gutal are big boots with curly toes.") } },
        new SchoolPage { pic = new[] { "dance", "dance" }, title = new Tx("Бүргэдийн бүжиг", "The eagle dance"), say = new[] { new Tx("Барилдахын өмнө бөхчүүд бүргэд шиг гараа дэвж бүжиглэнэ. Үүнийг “дэвээ” гэдэг.", "Before wrestling, wrestlers flap their arms like a big eagle. This is called “devee”."), new Tx("Тоглоомд товч гэрэлтэх бүрт дар. Сайн бүжиглэвэл эхний мэх чинь илүү хүчтэй болно!", "In the game, tap when the button glows. Dance well and your first move gets stronger!") } },
        new SchoolPage { pic = new[] { "push", "grip" }, title = new Tx("Мэх гэж юу вэ?", "What is a move?"), say = new[] { new Tx("Мэх бол найзаа унагах арга. Хайч, чулуу, даавуу шиг: мэх бүр заримыг нь дийлж, заримд нь дийлдэнэ.", "A move is a trick to make your friend fall. It’s like rock, paper, scissors: every move beats some moves and loses to others."), new Tx("Мэхний доор “>” тэмдгийн ард ямар мэхийг дийлдэг нь бичээстэй.", "Under each move, after the “>” sign, you can see which moves it beats."), new Tx("Нэг мэхийг дахин дахин хийвэл найз чинь таачихна. Өөр өөр мэх хий!", "If you use the same move again and again, your friend will guess it. Mix them up!") } },
        new SchoolPage { skill = "timing" }, new SchoolPage { skill = "hold" }, new SchoolPage { skill = "combo" }, new SchoolPage { skill = "mash" }, new SchoolPage { skill = "balance" },
        new SchoolPage { pic = new[] { "grip", "grip" }, title = new Tx("Одоо туршаад үзье!", "Now let’s try it!"), say = new[] { new Tx("Багштайгаа дасгал барилдаан хийе. Багш зөөлөн барилдана, санаа зовох хэрэггүй.", "Let’s have a practice bout with the coach. The coach goes easy, so don’t worry."), new Tx("Мэх сонгох бүрт тэр мэх юу хийдгийг, яаж хийхийг дахин сануулна.", "Every time you pick a move, you’ll see what it does and how to do it.") }, last = true },
    };
    sealed class SchoolModal : Modal
    {
        int i; Vector2 scroll;
        public override void Draw(Game g)
        {
            var pg = SCHOOL[i]; var sk = pg.skill != null ? SKILL_KID[pg.skill] : null;
            g.Small(Loc.L("Бөхийн сургууль", "Wrestling school") + $" · {i + 1}/{SCHOOL.Length}");
            if (sk != null) GUILayout.Label(SKILL_ICON[pg.skill], new GUIStyle(g.sBig) { alignment = TextAnchor.MiddleCenter });
            else { GUILayout.BeginHorizontal(); GUILayout.FlexibleSpace(); g.IconAt(Px.Wrestler(RED, g.MySkin, pg.pic[0], 0), 56); g.IconAt(Flip(Px.Wrestler(BLUE, Rules.SKINS[2], pg.pic[1], 0)), 56); GUILayout.FlexibleSpace(); GUILayout.EndHorizontal(); }
            GUILayout.Label("<b>" + (sk != null ? sk.name : pg.title) + "</b>", new GUIStyle(g.sTitle) { alignment = TextAnchor.MiddleCenter });
            foreach (var t in sk != null ? new[] { sk.how } : pg.say) g.Text(t.ToString());
            if (sk != null)
            {
                GUILayout.Label(Loc.L("Ингэж хийдэг мэхүүд", "Moves you do this way"), g.sSmall);
                scroll = g.BeginScroll(scroll, Screen.height * 0.3f);
                foreach (var m in Rules.MOVES.Where(m => (Rules.SKILL.TryGetValue(m.id, out var s) ? s : "timing") == pg.skill)) { GUILayout.BeginVertical(g.sBtn); GUILayout.Label("<b>" + m.name + "</b>", g.sLabel); GUILayout.Label(GerRules.Kid(m.id).ToString(), g.sSmall); GUILayout.EndVertical(); }
                g.EndScroll();
            }
            GUILayout.BeginHorizontal(); GUILayout.FlexibleSpace();
            for (int k = 0; k < SCHOOL.Length; k++) GUILayout.Label(k == i ? "<color=#f7c646>●</color>" : k < i ? "<color=#2ea35f>●</color>" : "○", g.sLabel);
            GUILayout.FlexibleSpace(); GUILayout.EndHorizontal();
            if (pg.last && g.V.wr.school == 0) { g.V.wr.school = 1; g.V.Save(); }
            GUILayout.BeginHorizontal();
            if (i > 0) { if (g.Btn("◄ " + Loc.L("Буцах", "Back"))) g.Later(() => i--); } else if (g.Btn(Loc.L("Хаах", "Close"))) g.Later(g.Close);
            if (pg.last) { if (g.Btn(Loc.L("Дасгал барилдаан", "Practice bout"), true)) g.Later(() => g.Swap(WrestleModal.Practice(g))); }
            else if (g.Btn(Loc.L("Дараах", "Next") + " ►", true)) g.Later(() => { i++; Sfx.Tap(); });
            GUILayout.EndHorizontal();
        }
        static PxCanvas Flip(PxCanvas c) { var o = new PxCanvas(c.W, c.H); o.BlitScaled(c, 0, 0, c.W, c.H, true); return o; }
        public override void Press(Game g) { }
    }
    static readonly Dictionary<string, string> SKILL_ICON = new Dictionary<string, string> { ["timing"] = "»", ["hold"] = "■", ["combo"] = "↔", ["mash"] = "!!", ["balance"] = "≈" };
    static string SkillIcon(string move) => SKILL_ICON[Rules.SKILL.TryGetValue(move, out var s) ? s : "timing"];
    sealed class TechBookModal : Modal
    {
        Vector2 scroll;
        public override void Draw(Game g)
        {
            g.Title(Loc.L("Бөхийн мэхний ном", "Wrestling technique book"));
            int half = Rules.MOVES.Length >> 1;
            g.Small(Loc.L($"Мэх бүр дараагийн {half} мэхийг давж, өмнөхөд нь дийлддэг. Мэх бүрийг өөр ур чадвараар хийнэ: » цаг тааруулах, ↔ хөлийн хөдөлгөөн (сумнууд), !! хүч (хурдан дарах), ■ барьцаа (дарж байгаад тавих), ≈ тэнцвэр. Нэг мэхийг давтвал өрсөлдөгч таньдаг.",
                $"Each move beats the next {half} and loses to the ones before it. Each is done with its own skill: » timing, ↔ footwork (arrows), !! power (fast taps), ■ grip (hold and release), ≈ balance. Repeat a move and your opponent reads it."));
            scroll = g.BeginScroll(scroll, Screen.height * 0.55f);
            foreach (var m in Rules.MOVES)
            {
                int used = SaveData.Get(g.V.wr.techs, m.id);
                GUILayout.BeginVertical(g.sBtn);
                GUILayout.Label($"<b>{SkillIcon(m.id)} {m.name}</b>  <size={g.Ts(10)}>×{used}</size>", g.sLabel);
                GUILayout.Label(m.fact.ToString(), g.sSmall);
                GUILayout.Label("√ " + Loc.L("Давна", "Beats") + ": " + string.Join(", ", m.beats.Select(b => Rules.MoveById(b).name.ToString())), g.sSmall);
                GUILayout.EndVertical();
            }
            g.EndScroll();
            if (g.Btn(Loc.L("Хаах", "Close"), false, 32)) g.Later(g.Close);
        }
        public override void Press(Game g) => g.Close();
    }

    sealed class WrestleModal : Modal
    {
        const float DEVEE_BEAT = 0.7f; const int DEVEE_TAPS = 4;
        static readonly string[] ARROWS = { "←", "→", "↑", "↓" };
        readonly int round, need; readonly string oppName, oppSkin; readonly float oppSkill, barSpeed, sharp; readonly bool young, practice; string coachMove;
        readonly Rules.Title oppT;
        // live bout with a friend: the dance and the skill games are played here, the server picks the round's winner
        readonly string matchId, side; bool pvp => matchId != null; bool oppReady, cut, noBout;
        // a bout in the online arena (server/arena.js): the server's clock for each round, what the bout paid, and the room with a friend
        bool ar, roomOpen = true, askedAgain, oppAgain, lateMe, lost, rated; string arMode = "ranked", arReason = ""; int arLv, arCoins, arXp, rBefore, rAfter;
        float deadlineAt = -1, backCheckAt = -1;
        /** A new bout took this one's place, so closing it must not close the room. */
        public bool Replaced;
        public string ArId => ar ? matchId : null;
        readonly Live live = new Live(400, 180);   // the stadium, full screen (wide, so a widescreen shows the whole field)
        Vector2 panelScroll;
        public override bool FullScreen => true;
        int a, b; string phase = "devee", move, lastPlayer, lastMove; float off, target, t0, marker, intro, end, bonus; bool overF;
        string[] pose = { "dance", "dance" }; readonly Dictionary<string, int> used = new Dictionary<string, int>();
        (string text, bool good, float t0)? flash; string msg, herald; bool heraldOn = true;
        int dn, dgood, df; readonly List<bool?> feathers = new List<bool?>();
        // the skill being performed
        string sk; float skT0, skLimit; int[] seq; int si, serr, sn, sneed; bool held; float sv, szc, szw, bx2, bvx, bpush, bgood; int bdir; int holdFrame = -9;
        bool won; (string newTitle, bool champion) res; float opened;

        WrestleModal(Game g, int round, string oppName, float oppSkill, string oppSkin, bool practice = false, string matchId = null, string side = null, string oppTitle = null)
        {
            this.round = round; this.oppName = oppName; this.oppSkill = oppSkill; this.oppSkin = oppSkin; this.practice = practice; this.matchId = matchId; this.side = side;
            need = round >= 7 && matchId == null ? 3 : 2;   // a live bout is first to two falls, as the server counts it
            oppT = Rules.TitleById(matchId != null ? oppTitle : Rules.OPP_TITLE[Mathf.Clamp(round, 0, Rules.OPP_TITLE.Length - 1)]);
            young = g.V.age > 0 && g.V.age <= 9;   // little ones get a slower marker and wider targets
            barSpeed = (2.1f + round * 0.2f) * (young ? 0.85f : 1) * (practice ? 0.8f : 1); sharp = Mathf.Max(1, 1 + round * 0.08f - (young ? 0.1f : 0));
            intro = Time.time + 1.1f; t0 = Time.time; opened = Time.time; Sfx.Crowd(); Sfx.Horn();   // the scene switch: the curtain opens on the stadium, the crowd roars
            var myT = Rules.TitleById(Rules.TitleFor(g.V.wr)); var opT = oppT;
            string Named(Rules.Title t, string n) => (t != null ? t.name + " " : "") + n;
            herald = Loc.L($"«{Named(myT, g.MyName)}, {Named(opT, oppName)} хоёр барилдана! Хүчит бөхчүүд дэвээгээ хийцгээ!»", $"\"{Named(myT, g.MyName)} and {Named(opT, oppName)} will wrestle! Mighty wrestlers, do your eagle dance!\"");
            msg = Loc.L("Дэвээ! Товч гэрэлтэх үед дарж бүргэд шиг дэв (4 удаа).", "Eagle dance! Tap when the button glows (4 times).");
        }
        /** A practice bout at the wrestling school: the slowest round, a gentle coach, the move explained as you pick it, nothing to win or lose. */
        public static WrestleModal Practice(Game g) => new WrestleModal(g, 1, Loc.L("Багш", "Coach"), 0.06f, Rules.SKINS[2], true);
        /** A Naadam round. Round 8 is always Avarga Setsen, the titan whose statue stands by the stands: a little stronger than the rest. */
        public static WrestleModal Tournament(Game g, int round) => round == 8
            ? new WrestleModal(g, round, Rules.SETSEN, 0.12f + round * 0.07f + 0.06f, Rules.SKINS[Rules.SKINS.Length - 1])
            : new WrestleModal(g, round, Rules.WR_NAMES[(round * 5 + Random.Range(0, 4)) % Rules.WR_NAMES.Length], 0.12f + round * 0.07f, Rules.Pick(Rules.SKINS));
        /** A live bout with a friend in the room, after the server's wr-start. `side` is "a" (the one who invited) or "b". */
        public static WrestleModal Pvp(Game g, string matchId, string side, string oppName, Look oppLook) =>
            new WrestleModal(g, 3, oppName, 0, oppLook != null && Rules.SKINS.Contains(oppLook.skin) ? oppLook.skin : Rules.SKINS[0], false, matchId, side, oppLook?.title);
        /** A bout in the online arena, after the server's ar-start. */
        public static WrestleModal Arena(Game g, J m)
        {
            var opp = m["opp"]; var look = ArenaLook(opp["look"]); int lv = opp["cpu"].I; string tier = opp["tier"].S;
            string skin = look != null && Rules.SKINS.Contains(look.skin) ? look.skin : Rules.SKINS[Mathf.Clamp(lv * 2, 0, Rules.SKINS.Length - 1)];
            var w = new WrestleModal(g, 3, lv > 0 ? CpuName(lv) : Clean(opp["u"].S), 0, skin, false, m["id"].S, m["side"].I == 1 ? "b" : "a", tier != "novice" ? tier : null)
                { ar = true, arMode = m["mode"].S ?? "ranked", arLv = lv };
            w.OnStart(m);
            return w;
        }
        public static string CpuName(int lv) => lv == 1 ? Loc.L("Багш", "Coach") : lv == 2 ? Loc.L("Начин", "Falcon") : Loc.L("Аварга", "Titan");
        public override float Width => 560;
        public override bool CanEscape => !pvp;   // leaving a live bout is giving up: only the buttons do it

        // ---------------------------------------------------------------- the online arena
        int Me => side == "b" ? 1 : 0;
        void Clock(J m) { deadlineAt = m.Has("ms") ? Time.realtimeSinceStartup + m["ms"].I / 1000f : -1; }
        /** ar-start: a new bout, or this one again after a dropped connection (then the falls so far, and whether my move is in). */
        public void OnStart(J m)
        {
            Clock(m); lost = false; backCheckAt = -1;
            if (!m["resume"].B) return;
            a = m["w"][Me].I; b = m["w"][1 - Me].I; heraldOn = false; pose = new[] { "grip", "grip" }; bonus = 0;
            target = Mathf.Clamp((a - b) * 12, -26, 26);
            if (m["chose"].B) { phase = "wait"; msg = Loc.L($"Холболт сэргэлээ. {oppName} мэхээ сонгож байна…", $"Back online. {oppName} is choosing a move…"); }
            else if (phase != "timing" && phase != "skill") { phase = "choose"; msg = Loc.L("Холболт сэргэлээ. Мэхээ сонго!", "Back online. Choose your move!"); }
        }
        /** The connection dropped: the server keeps the bout 10 seconds for us. */
        public void OnLost() { roomOpen = false; if (overF) return; lost = true; msg = Loc.L("Холболт тасарлаа. Дахин холбогдож байна…", "The connection dropped. Reconnecting…"); }
        /** Back online and the server said who we are: if it doesn't hand this bout back soon, the bout went on without us. */
        public void OnHello() { if (lost && !overF) backCheckAt = Time.realtimeSinceStartup + 2.5f; }
        public void OnRoomClosed() { roomOpen = false; }
        public void OnAgain() { oppAgain = true; Sfx.Pop(); }
        /** The server's arena messages for this bout: the other one has chosen, a round's result, the end. */
        public void OnArena(Game g, J m)
        {
            if (m["id"].S != matchId) return;
            string t = m["t"].S;
            if (t == "ar-wait") { if (!overF) { oppReady = true; Sfx.Pop(); } }
            else if (t == "ar-res" && !overF)
            {
                int me = Me, op = 1 - me;
                string mm = m["m"][me].S, om = m["m"][op].S; float mp = m["p"][me].F, opw = m["p"][op].F;
                if (!Rules.MOVES.Any(q => q.id == mm) || !Rules.MOVES.Any(q => q.id == om)) return;
                lateMe = m["late"][me].B; bool lateOp = m["late"][op].B;
                if (mm != move || lateMe) bonus = 0;
                int r = m["r"].I * (me == 0 ? 1 : -1);
                Apply(r, mm, mp, om, opw);
                a = m["w"][me].I; b = m["w"][op].I;   // the server's count is the truth
                target = Mathf.Clamp((a - b) * 12, -26, 26) + (r > 0 ? 6 : r < 0 ? -6 : 0);
                if (lateMe && lateOp) msg = Loc.L("Хоёулаа хугацаандаа мэхээ хийсэнгүй. Тэнцлээ!", "Neither of you moved in time. Even!");
                else if (lateMe) msg = Loc.L("Цаг дууслаа! Мэхээ хийж амжсангүй, өрсөлдөгч давууллаа.", "Time ran out! You didn't make your move, so your opponent gains.");
                else if (lateOp) msg = Loc.L($"{oppName} хугацаандаа мэхээ хийсэнгүй. Та давууллаа!", $"{oppName} didn't move in time. You gain the upper hand!");
                oppReady = false; Clock(m);
            }
            else if (t == "ar-end") FinishArena(g, m);
        }
        void FinishArena(Game g, J m)
        {
            if (overF) return;
            int me = Me, winner = m["winner"].I; bool draw = winner == -1;
            arReason = m["reason"].S ?? ""; arCoins = m["coins"].I; arXp = m["xp"].I;
            rated = m.Has("rating"); if (rated) { rBefore = m["rating"]["before"].I; rAfter = m["rating"]["after"].I; }
            if (m.Has("wins")) { a = m["wins"][me].I; b = m["wins"][1 - me].I; }
            overF = true; phase = "done"; end = Time.time; deadlineAt = -1; lost = false; backCheckAt = -1;
            won = winner == me; cut = arReason == "quit" || arReason == "left"; noBout = draw;
            pose = draw ? new[] { "idle", "idle" } : won ? new[] { "dance", "fall" } : new[] { "fall", "dance" };
            target = draw ? 0 : won ? 18 : -18;
            if (won) { Sfx.Win(); g.Confetti(80); } else Sfx.Bad();
            var wr = g.V.wr; if (arMode != "cpu") { if (won) wr.pvpW++; else if (!draw) wr.pvpL++; }   // coins and XP come from the server
            g.V.Save(); g.CheckAchievements();
            msg = draw ? Loc.L("Тэнцлээ! Хоёулаа сайн барилдлаа.", "A draw! You both wrestled well.")
                : cut ? (won ? Loc.L($"{oppName} барилдаанаас гарлаа. Та яллаа!", $"{oppName} left the bout. You win!")
                    : arReason == "quit" ? Loc.L("Та бууж өглөө.", "You gave up.") : Loc.L("Та барилдаанаас холдсон тул ялагдлаа.", "You were away from the bout, so it was lost."))
                : won ? Loc.L($"Та {oppName}-г унагаалаа! Бүргэд шиг дэвээрэй!", $"You threw {oppName}! Do the eagle dance!") : Loc.L($"{oppName} яллаа. Дараагийн удаа!", $"{oppName} won. Next time!");
        }
        /** The bout ended while we were away (the server handed nothing back). */
        void GoneWhileAway()
        {
            if (overF) return;
            overF = true; phase = "done"; end = Time.time; deadlineAt = -1; lost = false; won = false; cut = true; noBout = true; roomOpen = false;
            pose = new[] { "idle", "idle" }; target = 0;
            msg = Loc.L("Холболт удаан тасарсан тул барилдаан дууслаа.", "The connection was gone too long, so the bout is over.");
        }

        /** The server's wrestling messages for this bout: the other one has chosen, a round's result, the end. */
        public void OnNet(Game g, J m)
        {
            string t = m["t"].S;
            if (t == "wr-wait") { if (!overF) { oppReady = true; Sfx.Pop(); } }
            else if (t == "wr-result" && !overF)
            {
                bool isA = side == "a"; int r = m["r"].I * (isA ? 1 : -1);
                string mm = isA ? m["ma"].S : m["mb"].S, om = isA ? m["mb"].S : m["ma"].S;
                float mp = isA ? m["pa"].F : m["pb"].F, op = isA ? m["pb"].F : m["pa"].F;
                if (!Rules.MOVES.Any(q => q.id == mm) || !Rules.MOVES.Any(q => q.id == om)) return;
                if (mm != move) bonus = 0;   // the round ran out before I chose: the server played a weak move for me
                Apply(r, mm, mp, om, op);
                a = isA ? m["score"]["a"].I : m["score"]["b"].I; b = isA ? m["score"]["b"].I : m["score"]["a"].I;   // the server's count is the truth
                target = Mathf.Clamp((a - b) * 12, -26, 26) + (r > 0 ? 6 : r < 0 ? -6 : 0);
                oppReady = false;
            }
            else if (t == "wr-end") FinishPvp(g, m["winner"].S, m["reason"].S);
        }
        void FinishPvp(Game g, string winner, string why)
        {
            if (overF) return; overF = true; phase = "done"; end = Time.time;
            if (g.remote == this) g.remote = null;
            bool someone = !string.IsNullOrEmpty(winner); won = someone && winner == g.V.id;
            cut = why == "left" || why == "disconnected"; noBout = !someone;
            pose = !someone ? new[] { "idle", "idle" } : won ? new[] { "dance", "fall" } : new[] { "fall", "dance" };
            target = !someone ? 0 : won ? 18 : -18;
            if (won) { Sfx.Win(); g.Confetti(80); } else Sfx.Bad();
            var wr = g.V.wr; if (won) wr.pvpW++; else if (someone) wr.pvpL++;   // coins and XP come from the server
            g.V.Save(); g.CheckAchievements();
            msg = !someone ? Loc.L("Холболт тасарлаа. Барилдаан зогслоо.", "The connection dropped. The bout is over.")
                : cut ? (won ? Loc.L($"{oppName} барилдаанаас гарлаа. Та яллаа!", $"{oppName} left the bout. You win!") : Loc.L("Та бууж өглөө.", "You gave up."))
                : won ? Loc.L($"Та {oppName}-г унагаалаа! Бүргэд шиг дэвээрэй!", $"You threw {oppName}! Do the eagle dance!") : Loc.L($"{oppName} яллаа. Дараагийн удаа!", $"{oppName} won. Next time!");
        }
        // the server answers a quit with wr-end (ar-end in the arena); offline there is no one to tell
        void Leave(Game g) { if (pvp && !overF && g.net.Online) g.net.Send(ar ? "{\"t\":\"ar-quit\"}" : "{\"t\":\"wr-quit\"}"); else g.Close(); }
        public override void Closed(Game g)
        {
            if (!pvp) return;
            if (ar)
            {
                if (!overF) g.net.Send("{\"t\":\"ar-quit\"}");
                else if (arMode == "friend" && roomOpen && !Replaced) g.net.Send("{\"t\":\"ar-room-close\"}");   // done with this friend: the room goes
                g.ArenaBoutClosed(this);
                return;
            }
            if (!overF) g.net.Send("{\"t\":\"wr-quit\"}");
            if (g.remote == this) g.remote = null;
        }

        bool OnBeat() { float q = ((Time.time - intro) % DEVEE_BEAT) / DEVEE_BEAT; return q < 0.2f || q > 0.84f; }
        void DeveeTap(Game g)
        {
            if (phase != "devee" || dn >= DEVEE_TAPS) return;
            bool good = OnBeat(); feathers.Add(good); dn++; df ^= 1; if (good) dgood++;
            if (good) Sfx.Chime(); else Sfx.Tap();
            if (dn < DEVEE_TAPS) return;
            bonus = dgood * 0.06f;
            msg = dgood >= 3 ? Loc.L($"Гоё дэвлээ! Бүргэд шиг! Эхний мэх +{Mathf.RoundToInt(bonus * 100)}% хүчтэй.", $"Beautiful dance, like an eagle! First move +{Mathf.RoundToInt(bonus * 100)}% power.")
                : dgood > 0 ? Loc.L($"Дэвээ дууслаа. Эхний мэх +{Mathf.RoundToInt(bonus * 100)}% хүчтэй.", $"Dance done. First move +{Mathf.RoundToInt(bonus * 100)}% power.") : Loc.L("Дэвээ хэмнэлээсээ алдлаа. Одоо барилдъя!", "The dance was off the beat. Now wrestle!");
            phase = "pause"; t0 = Time.time;
        }
        void StartSkill(string id)
        {
            coachMove = id;
            move = id; heraldOn = false;
            string type = Rules.SKILL.TryGetValue(id, out var s) ? s : "timing"; t0 = Time.time; skT0 = Time.time; sk = type;
            if (type == "timing") { phase = "timing"; msg = Loc.L("Ногоон хэсэгт хүрэхэд дар!", "Press when the marker is in the green!"); return; }
            if (type == "combo") { seq = Enumerable.Range(0, 3 + Mathf.Min(2, round / 4)).Select(_ => Random.Range(0, 4)).ToArray(); si = 0; serr = 0; skLimit = Mathf.Max(3.4f, 5.6f - round * 0.18f) * (young ? 1.2f : 1); }
            if (type == "mash") { sn = 0; sneed = 7 + Mathf.CeilToInt(round * 0.6f) - (young ? 1 : 0); skLimit = 2.9f; }
            if (type == "hold") { held = false; sv = 0; szc = 0.78f; szw = (young ? 0.3f : 0.26f) / sharp; }
            if (type == "balance") { bx2 = 0; bvx = 0; bpush = 0; bdir = 0; bgood = 0; skLimit = 2.8f; }
            phase = "skill";
            msg = type == "combo" ? Loc.L("Хөлийн хөдөлгөөн! Сумнуудыг дарааллаар нь хурдан дар.", "Footwork! Press the arrows in order, quickly.") : type == "mash" ? Loc.L("Бүх хүчээ гарга! Хурдан, олон дар!", "All your strength! Tap as fast as you can!")
                : type == "hold" ? Loc.L("Барьцаа ав: дарж байгаад шар хэсэгт хүрэхэд тавь.", "Take the grip: hold, and let go in the yellow zone.") : Loc.L("Тэнцвэрээ хадгал! Зүү голдоо байлгах гэж ← → дар.", "Keep your balance! Use ← → to keep the needle in the middle.");
        }
        void Combo(int i) { if (phase != "skill" || sk != "combo") return; if (seq[si] == i) { si++; Sfx.Tap(); if (si >= seq.Length) { float q = (Time.time - skT0) / skLimit; Lock(Mathf.Clamp(0.45f + 0.55f * (1 - q) - 0.15f * serr, 0.05f, 1)); } } else { serr++; Sfx.Bad(); } }
        void Mash() { if (phase != "skill" || sk != "mash") return; sn++; Sfx.Tap(); }
        void HoldDown() { if (phase != "skill" || sk != "hold" || held) return; held = true; sv = 0; }
        void HoldUp() { if (phase != "skill" || sk != "hold" || !held) return; held = false; Lock(Mathf.Clamp01(1 - Mathf.Abs(sv - szc) * 3.2f * sharp)); }

        public override void Tick(Game g, float dt)
        {
            off += (target - off) * Mathf.Min(1, dt * 8);
            if (backCheckAt > 0 && Time.realtimeSinceStartup > backCheckAt) { backCheckAt = -1; GoneWhileAway(); }
            if (phase == "pause" && Time.time - t0 > 0.9f && !overF) { pose = new[] { "grip", "grip" }; phase = "choose"; msg = Loc.L("Мэхээ сонго!", "Choose your move!"); }
            if (phase == "timing") { marker = (Mathf.Sin((Time.time - t0) * barSpeed) + 1) / 2; if (Time.time - t0 > 5) Lock(-1); }
            if (phase == "skill")
            {
                float el = Time.time - skT0;
                if (sk == "combo" && el > skLimit) Lock(si / (float)seq.Length * 0.5f);
                else if (sk == "mash" && el > skLimit) Lock(Mathf.Min(1, sn / (float)sneed));
                else if (sk == "hold")
                {
                    bool pressing = Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.E) || Time.frameCount - holdFrame <= 2;
                    if (pressing && !held) HoldDown(); else if (!pressing && held) HoldUp();
                    if (held) { sv += dt / (young ? 1.9f : 1.6f); if (sv > 1) { sv = 0; msg = Loc.L("Хэт удаан барилаа! Дахиад.", "Held too long! Again."); } }
                    if (phase == "skill" && el > 6) Lock(0.1f);
                }
                else if (sk == "balance")
                {
                    int kd = (Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A) ? -1 : 0) + (Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D) ? 1 : 0);
                    int d = kd != 0 ? kd : bdir; bdir = 0;
                    bpush += (Random.value - 0.5f) * dt * (4.5f + round * 0.7f); bpush = Mathf.Clamp(bpush, -1.3f, 1.3f); // your opponent shoves you this way and that
                    bvx += (bpush * (0.85f + round * 0.08f) + d * 4.6f) * dt; bvx *= 0.92f; bx2 = Mathf.Clamp(bx2 + bvx * dt, -1, 1);
                    if (Mathf.Abs(bx2) < 0.25f) bgood += dt;
                    if (el > skLimit) Lock(Mathf.Clamp01(bgood / skLimit * 1.15f));
                }
            }
            if (phase == "result" && Time.time - t0 > (a >= need || b >= need ? 0.9f : 1.1f))
            {
                if (a < need && b < need) { phase = "choose"; if (pvp) msg = Loc.L("Мэхээ сонго!", "Choose your move!"); }
                else if (!pvp) Finish(g, a > b);   // a live bout ends on the server's wr-end
            }
        }
        public override void Press(Game g) { }
        public override void Key(Game g)
        {
            bool space = Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.E);
            if (phase == "devee" && space) DeveeTap(g);
            else if (phase == "timing" && space) Lock(-1);
            else if (phase == "skill" && sk == "mash" && space) Mash();
            else if (phase == "skill" && sk == "combo")
            {
                if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A)) Combo(0);
                if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) Combo(1);
                if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W)) Combo(2);
                if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S)) Combo(3);
            }
        }
        void Lock(float given)
        {
            if (phase != "timing" && phase != "skill") return;
            heraldOn = false;
            float power = given >= 0 ? given : Mathf.Max(0, 1 - Mathf.Abs(marker - 0.5f) * 2 * sharp);
            if (bonus > 0) { power = Mathf.Min(1, power + bonus); bonus = 0; } // the eagle dance's power
            if (pvp) // a friend reads you with their own eyes: send the move, the server decides when both have chosen
            {
                if (ar) I.net.Send("{\"t\":\"ar-mv\",\"m\":" + Net.Q(move) + ",\"p\":" + Mathf.Clamp01(power).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "}");
                else I.net.WrMove(move, power);
                phase = "wait"; Sfx.Tap();
                msg = oppReady ? Loc.L("Мэх илгээлээ…", "Move sent…") : Loc.L($"{oppName} мэхээ сонгож байна…", $"{oppName} is choosing a move…");
                return;
            }
            bool repeated = move == lastPlayer;
            if (repeated) power *= 0.8f; // the same move twice in a row is easier to read
            used[move] = (used.TryGetValue(move, out var u) ? u : 0) + 1;
            // the opponent studies you: in later rounds it often picks a move that beats the one you use most
            string fav = used.OrderByDescending(kv => kv.Value).First().Key;
            string read = repeated ? move : (Random.value < 0.5f ? lastPlayer : fav);
            bool smart = read != null && Random.value < Mathf.Min(0.5f, 0.03f + round * 0.05f);
            var counters = Rules.MOVES.Where(q => read != null && q.beats.Contains(read)).ToList();
            string om = smart && counters.Count > 0 ? Rules.Pick(counters).id : Rules.Pick(Rules.MOVES).id;
            float op = Mathf.Clamp(oppSkill + (Random.value - 0.5f) * Mathf.Max(0.18f, 0.5f - round * 0.035f), 0.05f, 1);
            Apply(Rules.Exchange(move, power, om, op), move, power, om, op);
        }
        void Apply(int r, string mm, float mp, string om, float op)
        {
            bool wasRepeat = mm == lastPlayer; var g = I;
            lastPlayer = mm; if (r != 0) lastMove = r > 0 ? mm : om;
            if (r > 0) { a++; Sfx.Good(); SaveData.Add(g.V.wr.techs, mm, 1); g.V.Save(); } else if (r < 0) { b++; Sfx.Bad(); } else Sfx.Tap();
            flash = ((r >= 0 ? Rules.MoveById(mm).name : Rules.MoveById(om).name).ToString().ToUpper() + "!", r > 0, Time.time);
            target = Mathf.Clamp((a - b) * 12, -26, 26) + (r > 0 ? 6 : r < 0 ? -6 : 0);
            msg = $"{Rules.MoveById(mm).name} {Mathf.RoundToInt(mp * 100)}%  ·  {Rules.MoveById(om).name} {Mathf.RoundToInt(op * 100)}%  →  " + (r > 0 ? Loc.L("Та давууллаа!", "You gain the upper hand!") : r < 0 ? Loc.L("Өрсөлдөгч давууллаа!", "Your opponent gains!") : Loc.L("Тэнцлээ!", "Even!"));
            if (wasRepeat && r < 0 && !pvp) msg += " " + Loc.L("(Давтсан мэхийг өрсөлдөгч таньчихлаа!)", "(They saw your repeated move coming!)");
            phase = "result"; t0 = Time.time;
        }
        void Finish(Game g, bool w)
        {
            if (overF) return; overF = true; won = w; phase = "done";
            pose = w ? new[] { "dance", "fall" } : new[] { "fall", "dance" }; target = w ? 18 : -18; end = Time.time;
            if (w) { Sfx.Win(); g.Confetti(60); } else Sfx.Bad();
            if (practice) { msg = w ? Loc.L("Гоё барилдлаа! Одоо жинхэнэ Наадамд барилдаж үзээрэй.", "Great wrestling! Now try the real Naadam.") : Loc.L("Зүгээр дээ, энэ бол дасгал. Дахиад оролдоё!", "That’s fine, it’s only practice. Let’s try again!"); return; }
            var wr = g.V.wr;
            if (w) { wr.wins++; g.Quest("wrestle"); g.Reward(8 + round * 2, 10 + round * 3); } else { wr.losses++; g.Reward(0, 5); }
            int reached = w ? round : round - 1;
            if (w && round == 9) wr.champ++;
            if (reached > wr.best) wr.best = reached;
            string before = g.V.look.title, t = Rules.TitleFor(wr);
            if (t != null && before != t) { g.V.look.title = t; res.newTitle = t; }
            res.champion = w && round == 9; g.V.Save();
            string part = lastMove != null && Rules.WR_PART.TryGetValue(lastMove, out var p) ? p.ToString() : null;
            string touch = part != null ? (w ? Loc.L($"Өрсөлдөгчийн {part} газарт хүрлээ! ", $"Their {part} touched the ground! ") : Loc.L($"Таны {part} газарт хүрлээ. ", $"Your {part} touched the ground. ")) : "";
            msg = touch + (w ? Loc.L("Та яллаа! Бүргэд шиг дэвээрэй!", "You won! Do the eagle dance!") : Loc.L("Уналаа… Дараагийн удаа заавал ялна!", "You fell… you will win next time!"));
        }

        void Scene(Game g)
        {
            var cx = live.c; float ts = Time.time; const int S = 2, ground = 158;
            cx.CopyFrom(Art.Get("naadam_stadium"));
            // the crowd cheers: arms go up all over the stands, more of them when something happens on the field
            bool loud = overF || (flash.HasValue && ts - flash.Value.t0 < 1.4f) || ts - opened < 2.5f;
            int beat = (int)(ts * (loud ? 8 : 3)); var arm = new Color32(232, 180, 138, 255);
            for (int i = 0; i < (loud ? 140 : 40); i++)
            {
                uint h = (uint)(i * 2654435761u ^ (uint)beat * 40503u); int x = (int)(h % 398), row = (int)((h >> 9) % 6), y = new[] { 59, 65, 73, 79, 85, 91 }[row];
                if (x > 164 && x < 236 && row < 4) continue;   // not over the pavilion
                cx.Put(x, y - 1, arm); cx.Put(x, y - 2, arm); cx.Put(x + 3, y - 1, arm); cx.Put(x + 3, y - 2, arm);
            }
            // the herald, in his gold deel, announcing or cheering
            var z = Folk("zasuul", heraldOn ? "proud" : overF ? "laugh" : "curious", heraldOn ? "idle" : overF ? "cheer" : "talk", (int)(ts * 1.5f) % 2, heraldOn && (int)(ts * 8) % 2 == 0, ts % 3.3f < 0.12f);
            Px.R(cx, 318, ground - 2, 30, 3, "rgba(40,20,0,.25)"); cx.BlitScaled(z, 308, ground - 84, 50, 85);
            int f = (int)(ts / 0.26f) % 2, o = Mathf.RoundToInt(off) * S; float ent = Mathf.Clamp01((ts - intro) / 0.6f);
            string myPose = pose[0], opPose = pose[1];
            var A = Px.Wrestler(RED, g.MySkin, myPose == "grip" && f != 0 && phase == "timing" ? "push" : myPose, phase == "devee" ? df : f); var B = Px.Wrestler(BLUE, oppSkin, opPose, f);
            const int cxm = 190; int ax = Mathf.RoundToInt(cxm - 60 - (1 - ent) * 90) + o + (myPose == "dance" ? -24 : 0), bxx = Mathf.RoundToInt(cxm - 8 + (1 - ent) * 90) + o + (opPose == "dance" ? 24 : 0);
            void Draw1(PxCanvas img, int X, bool flip, int dy = 0) { int Y = ground - img.H * S + dy; Px.R(cx, X + 8, ground - 2, img.W * S - 16, 3, "rgba(40,20,0,.28)"); cx.BlitScaled(img, X, Y, img.W * S, img.H * S, flip); }
            float cross = overF && !noBout ? Mathf.Clamp01((ts - end - 1.1f) / 1.6f) : 0;
            if (cross > 0) // custom: the fallen wrestler gets up and passes under the winner's raised arm
            {
                var win = Px.Wrestler(won ? RED : BLUE, won ? g.MySkin : oppSkin, "dance", cross < 1 ? 1 : f); var lose = Px.Wrestler(won ? BLUE : RED, won ? oppSkin : g.MySkin, "idle", 0);
                int from = won ? bxx : ax, to = won ? ax - 52 : bxx + 52;
                Draw1(win, won ? ax : bxx, !won); Draw1(lose, Mathf.RoundToInt(from + (to - from) * cross), won, 4);
            }
            else { Draw1(A, ax - (myPose == "fall" ? 32 : 0), false); Draw1(B, bxx + (opPose == "fall" ? 4 : 0), true); }
        }

        public override void Draw(Game g)
        {
            // the stadium fills the top of the screen, its field running down to the panel with the controls
            g.Box(new Rect(0, 0, Screen.width, Screen.height), Px.Col("#2a1c18"));
            Scene(g);
            float split = Screen.height * 0.6f, K = Mathf.Max(Screen.width / 400f, split / 132f), pw = 400 * K, ph = 180 * K;   // shows the stands down to the ground
            live.ShowAt(new Rect((Screen.width - pw) / 2, split - ph * (166f / 180f), pw, ph));
            if (flash.HasValue && Time.time - flash.Value.t0 < 1.3f)
            {
                float q = (Time.time - flash.Value.t0) / 1.3f; var at = live.At(190, 100 - q * 14);
                g.Outlined(at.x, at.y, flash.Value.text, live.Scale * 9, flash.Value.good ? Px.Col("#f7c646") : Color.white, q > 0.75f ? (1 - q) * 4 : 1);
            }
            var safe = Screen.safeArea; float U = g.UiScale, top = Screen.height - safe.yMax + U * 8;
            // the top bar: the round, both names and their falls, and what the herald calls out
            string arLabel = arMode == "friend" ? Loc.L("Найзтайгаа", "With a friend") : arMode == "cpu" ? Loc.L("Дасгал", "Sparring") : Loc.L("Чансаа", "Ranked");
            string info = (practice ? Loc.L("Бөхийн сургууль · Дасгал", "Wrestling school · Practice") : ar ? $"{Loc.L("Бөхийн өргөө", "Wrestling Arena")} · {arLabel} · {need} {Loc.L("унагаалт", "falls")}" : pvp ? $"{Loc.L("Найзтайгаа барилдах", "Wrestling a friend")} · {need} {Loc.L("унагаалт", "falls")}" : $"{Loc.L("Наадам", "Naadam")} · {Loc.L("Даваа", "Round")} {round}/9 · {need} {Loc.L("унагаалт", "falls")}")
                + $"\n<color=#d8342b><b>{g.MyName}</b></color> {new string('●', a)}{new string('○', Mathf.Max(0, need - a))}   ·   {new string('●', b)}{new string('○', Mathf.Max(0, need - b))} <color=#2b5bd8><b>{(oppT != null ? oppT.name + " " : "")}{oppName}</b></color>"
                + (heraldOn ? $"\n<b>{Loc.L("Засуул", "Herald")}:</b> {herald}" : "");
            var ist = new GUIStyle(g.sNote) { fontSize = g.Ts(12), wordWrap = true }; float iw = Mathf.Min(Screen.width - U * 120, U * 640), ih = ist.CalcHeight(new GUIContent(info), iw) + U * 6;
            GUI.Box(new Rect((Screen.width - iw) / 2, top, iw, ih), info, ist);
            if (phase != "done" && GUI.Button(new Rect(safe.x + U * 8, top, U * 44, U * 44), "×", g.sBtn)) g.Later(() => Leave(g));
            // the controls
            float pwid = Mathf.Min(Screen.width - U * 16, U * 820); var pr = new Rect((Screen.width - pwid) / 2, split - U * 4, pwid, Screen.height - split - U * 4 - (Screen.height - safe.yMax) - safe.y * 0);
            GUILayout.BeginArea(pr); GUILayout.BeginVertical(g.sPanel); panelScroll = g.BeginScroll(panelScroll, pr.height - U * 60);
            Controls(g);
            g.EndScroll(); GUILayout.EndVertical(); GUILayout.EndArea();
            // the scene switch: a black curtain opens on the stadium, with the name of the bout
            float sw = Mathf.Clamp01((Time.time - opened) / 1.3f);
            if (sw < 1)
            {
                float close = 1 - Mathf.SmoothStep(0, 1, Mathf.Clamp01((sw - 0.45f) / 0.55f)), hh = Screen.height / 2f * close;
                g.Box(new Rect(0, 0, Screen.width, hh), Color.black); g.Box(new Rect(0, Screen.height - hh, Screen.width, hh), Color.black);
                float ta = Mathf.Clamp01(sw * 4) * close;
                GUI.Label(new Rect(0, Screen.height / 2 - U * 40, Screen.width, U * 50), "НААДАМ", new GUIStyle(g.sBig) { fontSize = Mathf.RoundToInt(U * 46), normal = { textColor = new Color(1, 0.84f, 0.3f, ta) } });
                GUI.Label(new Rect(0, Screen.height / 2 + U * 10, Screen.width, U * 30), practice ? Loc.L("Бөхийн сургууль", "Wrestling school") : pvp ? Loc.L($"Бөхийн барилдаан · {oppName}", $"Wrestling · {oppName}") : Loc.L($"Бөхийн барилдаан · {round}-р даваа", $"Wrestling · Round {round}"), new GUIStyle(g.sTitle) { alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(1, 1, 1, ta) } });
            }
        }
        void Controls(Game g)
        {
            g.Text(msg);
            if (pvp && oppReady && phase != "wait" && phase != "done") g.Small("<color=#f7c646>" + Loc.L($"{oppName} мэхээ сонгочихлоо!", $"{oppName} has chosen!") + "</color>");
            if (ar && deadlineAt > 0 && !lost && phase != "done" && phase != "wait" && phase != "result")
            {
                int secs = Mathf.Max(0, Mathf.CeilToInt(deadlineAt - Time.realtimeSinceStartup));
                g.Small((secs <= 5 ? "<color=#e14b3d>" : "<color=#b8b0a8>") + Loc.L($"⏱ Мэх хийх хугацаа: {secs} сек", $"⏱ Time to move: {secs}s") + "</color>");
            }
            if (practice && phase != "done") // the school's coach explains as you go
            {
                if (phase == "devee") g.Fact(Loc.L("Товч гэрэлтэх бүрт дар. Сайн бүжиглэвэл эхний мэх чинь хүчтэй болно!", "Tap each time the button glows. A good dance makes your first move stronger!"), true, Loc.L("Бүргэд шиг бүжиглэ", "Dance like an eagle"));
                else if (coachMove != null) { var mv = Rules.MoveById(coachMove); string skl = Rules.SKILL.TryGetValue(coachMove, out var s0) ? s0 : "timing"; g.Fact(GerRules.Kid(coachMove) + "\n" + SKILL_KID[skl].how, true, SkillIcon(coachMove) + " " + mv.name); }
                else g.Fact(Loc.L("Мэх сонго. Сонгох бүрт тэр мэх юу хийдгийг, яаж хийхийг тайлбарлана.", "Pick a move. Each time, the coach explains what it does and how to do it."), true, Loc.L("Багш", "Coach"));
            }
            switch (phase)
            {
                case "devee":
                    GUILayout.BeginHorizontal(); for (int i = 0; i < DEVEE_TAPS; i++) GUILayout.Label(i < feathers.Count ? (feathers[i] == true ? "<color=#2ea35f>●</color>" : "<color=#e14b3d>●</color>") : "○", g.sTitle); GUILayout.EndHorizontal();
                    if (GUILayout.Button(Loc.L("Дэвэх (Space)", "Flap (Space)"), OnBeat() ? new GUIStyle(g.sPrimary) { normal = g.sPrimary.active } : g.sPrimary, GUILayout.MinHeight(g.U(52)))) g.Later(() => DeveeTap(g));
                    break;
                case "choose":
                    {
                        // the moves in equal tiles, each as tall as its words: the name, then the moves it beats in small letters
                        var moves = Rules.MOVES;
                        var st = new GUIStyle(g.sBtn) { fontSize = g.Ts(13), padding = new RectOffset(Mathf.RoundToInt(g.U(8)), Mathf.RoundToInt(g.U(8)), Mathf.RoundToInt(g.U(6)), Mathf.RoundToInt(g.U(8))) };
                        var stOn = new GUIStyle(st) { normal = g.sBtn.active };
                        GUIContent Tile(Rules.Move m) => new GUIContent($"<b>{SkillIcon(m.id)} {m.name}</b>\n<size={g.Ts(10)}>> {string.Join(", ", m.beats.Take(3).Select(x => Rules.MoveById(x).name.ToString()))}…</size>");
                        int picked = -1;
                        g.Grid("wr-moves", moves.Length, 168, (i, w) => Mathf.Max(g.U(46), st.CalcHeight(Tile(moves[i]), w) + g.U(4)), (i, r) => { if (GUI.Button(r, Tile(moves[i]), moves[i].id == lastPlayer ? stOn : st)) picked = i; });
                        if (picked >= 0) { var id = moves[picked].id; g.Later(() => StartSkill(id)); }
                        break;
                    }
                case "timing":
                    {
                        var r = GUILayoutUtility.GetRect(10, g.U(22), GUILayout.ExpandWidth(true)); g.Box(r, Px.Col("#2b1e2e")); var inner = new Rect(r.x + g.U(2), r.y + g.U(2), r.width - g.U(4), r.height - g.U(4)); g.Box(inner, Px.Col("#e6c48a"));
                        float zw = 0.36f / sharp; g.Box(new Rect(inner.x + (0.5f - zw / 2) * inner.width, inner.y, zw * inner.width, inner.height), Px.Col("#5c9a36"));
                        g.Box(new Rect(inner.x + marker * inner.width - g.U(2), r.y - g.U(3), g.U(4), r.height + g.U(6)), Px.Col("#f7c646"));
                        if (g.Btn(Loc.L("Хүч! (Space)", "Power! (Space)"), true, 52)) g.Later(() => Lock(-1));
                        break;
                    }
                case "skill": SkillUI(g); break;
                case "wait": g.Small(new string('.', 1 + (int)(Time.time * 2) % 3)); break;
                case "done" when ar:
                    {
                        if (!noBout) g.Fact(Loc.L("Унасан бөх ялсан бөхийн суган доогуур гарч хүндэтгэл үзүүлдэг. Ялагдсан ч хүндэтгэлтэй байх нь бөхийн ёс.", "The fallen wrestler passes under the winner's arm as a sign of respect. Losing with honour is the wrestler's way."), true, Loc.L("Бөхийн ёс", "Wrestling custom"));
                        string pay = (arCoins > 0 ? $"+{arCoins} {Loc.L("зоос", "coins")} · " : "") + $"+{arXp} XP";
                        if (rated) { int d = rAfter - rBefore; pay += " · " + Loc.L("Оноо", "Rating") + $" {rBefore} → <b>{rAfter}</b> (<color={(d >= 0 ? "#2ea35f" : "#e14b3d")}>{(d >= 0 ? "+" : "")}{d}</color>)"; }
                        if (arXp > 0 || arCoins > 0 || rated) g.Text(pay);
                        if (arMode == "friend")
                        {
                            if (!roomOpen) g.Small(Loc.L("Өрөө хаагдлаа.", "The room has closed."));
                            else if (askedAgain) g.Small(Loc.L($"{oppName}-г хүлээж байна…", $"Waiting for {oppName}…"));
                            else if (oppAgain) g.Small("<color=#f7c646>" + Loc.L($"{oppName} дахин барилдмаар байна!", $"{oppName} wants to wrestle again!") + "</color>");
                        }
                        GUILayout.BeginHorizontal();
                        if (g.Btn(Loc.L("Өргөө рүү", "To the arena"))) g.Later(() => g.Swap(new ArenaHubModal()));
                        if (arMode == "friend") { if (g.Btn(Loc.L("Дахин барилдах", "Wrestle again") + " ►", true, 44, roomOpen && !askedAgain && g.net.Online)) g.Later(() => { askedAgain = true; g.net.Send("{\"t\":\"ar-again\"}"); }); }
                        else if (arMode == "cpu") { if (g.Btn(Loc.L("Дахиад", "Again") + " ►", true, 44, g.net.Online)) { int lv = Mathf.Clamp(arLv, 1, 3); g.Later(() => { g.Swap(new ArenaHubModal()); g.net.Send("{\"t\":\"ar-cpu\",\"lv\":" + lv + "}"); }); } }
                        else if (g.Btn(Loc.L("Дахин хайх", "Find another") + " ►", true, 44, g.net.Online)) g.Later(() => { g.Swap(new ArenaHubModal()); g.net.Send("{\"t\":\"ar-queue\"}"); });
                        GUILayout.EndHorizontal();
                        break;
                    }
                case "done" when pvp:
                    if (!noBout) g.Fact(Loc.L("Унасан бөх ялсан бөхийн суган доогуур гарч хүндэтгэл үзүүлдэг. Ялагдсан ч хүндэтгэлтэй байх нь бөхийн ёс.", "The fallen wrestler passes under the winner's arm as a sign of respect. Losing with honour is the wrestler's way."), true, Loc.L("Бөхийн ёс", "Wrestling custom"));
                    if (g.Btn(Loc.L("Хаах", "Close"), true, 44)) g.Later(g.Close);
                    break;
                case "done":
                    g.Fact(won ? Loc.L("Унасан бөх ялсан бөхийн суган доогуур гарч хүндэтгэл үзүүлдэг. Ялсан бөх дараа нь тугаа тойрон дэвдэг.", "The fallen wrestler passes under the winner's arm as a sign of respect. The winner then dances around the flag.")
                        : Loc.L("Унасан бөх ялсан бөхийн суган доогуур гарч хүндэтгэл үзүүлдэг. Ялагдсан ч хүндэтгэлтэй байх нь бөхийн ёс.", "The fallen wrestler passes under the winner's arm as a sign of respect. Losing with honour is the wrestler's way."), true, Loc.L("Бөхийн ёс", "Wrestling custom"));
                    var t = res.newTitle != null ? Rules.TitleById(res.newTitle) : null;
                    if (t != null) g.Fact(t.fact.ToString(), true, Loc.L($"Засуул: «Хүчит бөх {g.MyName} {t.name} цол хүртлээ!»", $"Herald: \"The mighty wrestler {g.MyName} earns the {t.name} title!\""));
                    if (res.champion) g.Text("<b>" + Loc.L("Та Наадамд түрүүллээ!", "You won the whole Naadam!") + "</b>");
                    GUILayout.BeginHorizontal();
                    if (practice) { if (g.Btn(Loc.L("Дахиад", "Again"))) g.Later(() => g.Swap(Practice(g))); if (g.Btn(Loc.L("Жинхэнэ барилдаан", "Wrestle for real") + " ►", true)) g.Later(() => g.Swap(Tournament(g, 1))); GUILayout.EndHorizontal(); break; }
                    if (g.Btn(Loc.L("Хаах", "Close"))) g.Later(g.Close);
                    if (won && round < 9) { if (g.Btn(Loc.L("Дараагийн даваа", "Next round") + " ►", true)) g.Later(() => g.Swap(Tournament(g, round + 1))); }
                    else if (g.Btn(Loc.L("Шинэ Наадам", "New Naadam"), true)) g.Later(() => g.Swap(Tournament(g, 1)));
                    GUILayout.EndHorizontal();
                    break;
            }
            if (phase != "done" && g.Btn(pvp ? Loc.L("Бууж өгөх", "Give up") : Loc.L("Гарах", "Leave"), false, 28)) g.Later(() => Leave(g));
        }
        void SkillUI(Game g)
        {
            float el = Time.time - skT0;
            Rect Meter() { var r = GUILayoutUtility.GetRect(10, g.U(16), GUILayout.ExpandWidth(true)); return r; }
            switch (sk)
            {
                case "combo":
                    GUILayout.BeginHorizontal(); GUILayout.FlexibleSpace();
                    for (int i = 0; i < seq.Length; i++) GUILayout.Label(i < si ? $"<color=#2ea35f>{ARROWS[seq[i]]}</color>" : i == si ? $"<color=#e14b3d>{ARROWS[seq[i]]}</color>" : ARROWS[seq[i]], new GUIStyle(g.sBig) { fontSize = g.Ts(26) }, GUILayout.Width(g.U(40)));
                    GUILayout.FlexibleSpace(); GUILayout.EndHorizontal();
                    g.Bar(Meter(), Mathf.Max(0, 1 - el / skLimit), Px.Col("#f7c646"));
                    GUILayout.BeginHorizontal(); for (int i = 0; i < 4; i++) if (GUILayout.Button(ARROWS[i], g.sBtn, GUILayout.MinHeight(g.U(48)))) { int k = i; g.Later(() => Combo(k)); } GUILayout.EndHorizontal();
                    break;
                case "mash":
                    g.Bar(Meter(), Mathf.Min(1, sn / (float)sneed), Px.Col("#e14b3d"));
                    if (g.Btn(Loc.L($"Хүч! {sn}/{sneed}", $"Power! {sn}/{sneed}"), true, 56)) g.Later(Mash);
                    break;
                case "hold":
                    {
                        var r = Meter(); g.Box(r, Px.Col("#2b1e2e")); var inner = new Rect(r.x + g.U(2), r.y + g.U(2), r.width - g.U(4), r.height - g.U(4)); g.Box(inner, Px.Col("#5a4a5e"));
                        g.Box(new Rect(inner.x + (szc - szw / 2) * inner.width, inner.y, szw * inner.width, inner.height), Px.Col("#f7c646"));
                        g.Box(new Rect(inner.x, inner.y + inner.height * 0.3f, sv * inner.width, inner.height * 0.4f), Px.Col("#3f76db"));
                        bool gripHeld = GUILayout.RepeatButton(Loc.L("Барих: дарж байгаад тавих (Space)", "Grip: hold, then let go (Space)"), g.sPrimary, GUILayout.MinHeight(g.U(56))); g.DrawingAt(GUILayoutUtility.GetLastRect()); if (gripHeld) holdFrame = Time.frameCount;
                        break;
                    }
                case "balance":
                    {
                        var r = Meter(); g.Box(r, Px.Col("#2b1e2e")); var inner = new Rect(r.x + g.U(2), r.y + g.U(2), r.width - g.U(4), r.height - g.U(4)); g.Box(inner, Px.Col("#e6c48a"));
                        g.Box(new Rect(inner.x + 0.375f * inner.width, inner.y, 0.25f * inner.width, inner.height), Px.Col("#5c9a36"));
                        g.Box(new Rect(inner.x + (0.5f + bx2 * 0.48f) * inner.width - g.U(2), r.y - g.U(3), g.U(4), r.height + g.U(6)), Px.Col("#2b1e2e"));
                        g.Bar(GUILayoutUtility.GetRect(10, g.U(8), GUILayout.ExpandWidth(true)), Mathf.Max(0, 1 - el / skLimit), Px.Col("#f7c646"));
                        GUILayout.BeginHorizontal();
                        bool leftHeld = GUILayout.RepeatButton("◄", g.sBtn, GUILayout.MinHeight(g.U(52))); g.DrawingAt(GUILayoutUtility.GetLastRect()); if (leftHeld) bdir = -1;
                        bool rightHeld = GUILayout.RepeatButton("►", g.sBtn, GUILayout.MinHeight(g.U(52))); g.DrawingAt(GUILayoutUtility.GetLastRect()); if (rightHeld) bdir = 1;
                        GUILayout.EndHorizontal();
                        break;
                    }
            }
        }
    }
}
