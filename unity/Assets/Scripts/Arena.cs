// Бөхийн өргөө, the online arena (server/arena.js, accounts.js): a wrestler account with a unique username and a password,
// friends, ranked matchmaking, challenges between friends, sparring with the CPU, and the boards (rating, wins, level, XP).
// The bouts themselves are in ArenaFight.cs. Everything is kept on the server; this side shows it and sends what is asked.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/** POST /api/account: register, log in, rename, change the password, delete. See docs/PROTOCOL.md, "Online arena". */
public static class ArenaApi
{
    static string Url(string server, string path) { string s = (server ?? "").Trim().TrimEnd('/'); if (s == "") return null; if (!s.StartsWith("http")) s = Family.SCHEME + s; return s + path; }
    /** done always gets an object: the server's answer, or { ok: false, reason: "offline" | "no-server" }. */
    public static IEnumerator Post(string server, string body, Action<J> done, string path = "/api/account")
    {
        string url = Url(server, path);
        if (url == null) { done(J.Parse("{\"ok\":false,\"reason\":\"no-server\"}")); yield break; }
        using (var r = new UnityWebRequest(url, "POST"))
        {
            r.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)); r.downloadHandler = new DownloadHandlerBuffer(); r.SetRequestHeader("content-type", "application/json"); r.timeout = 10;
            yield return r.SendWebRequest();
            J answer = null; string text = r.downloadHandler != null ? r.downloadHandler.text : "";
            if (!string.IsNullOrEmpty(text) && text.TrimStart().StartsWith("{")) { try { answer = J.Parse(text); } catch (Exception) { answer = null; } }
            done(answer != null && answer.IsObject ? answer : J.Parse("{\"ok\":false,\"reason\":\"offline\"}"));
        }
    }
    public static string Body(string act, params string[] kv)
    {
        var b = new StringBuilder("{\"act\":" + J.Quote(act));
        for (int i = 0; i + 1 < kv.Length; i += 2) b.Append(",").Append(J.Quote(kv[i])).Append(":").Append(J.Quote(kv[i + 1] ?? ""));
        return b.Append("}").ToString();
    }
    /** Why the server said no, in words. */
    public static string Why(J r)
    {
        switch (r["reason"].S)
        {
            case "taken": return Loc.L("Энэ нэрийг өөр хүн авчихсан байна. Өөр нэр сонгоорой.", "Someone already has this username. Pick another one.");
            case "invalid": return Loc.L("Нэр 3–16 үсэг, тоо эсвэл _ байна (хоосон зайгүй).", "A username is 3–16 letters, digits or _ (no spaces).");
            case "mixed": return Loc.L("Нэрэндээ латин, кирилл үсгийг холихгүй.", "Don't mix Latin and Cyrillic letters in one name.");
            case "reserved": return Loc.L("Энэ нэрийг ашиглах боломжгүй.", "This name can't be used.");
            case "weak-password": return Loc.L("Нууц үг 6-аас дээш тэмдэгттэй, нэрээсээ өөр байна.", "The password needs 6 or more characters and must differ from the username.");
            case "wrong": return Loc.L("Нэр эсвэл нууц үг буруу байна.", "Wrong username or password.") + (r.Has("left") ? Loc.L($" {r["left"].I} оролдлого үлдлээ.", $" {r["left"].I} tries left.") : "");
            case "locked": { int m = Math.Max(1, (r["wait"].I + 59) / 60); return Loc.L($"Олон удаа буруу орууллаа. {m} минутын дараа дахин оролдоорой.", $"Too many wrong tries. Try again in {m} min."); }
            case "has-account": return Loc.L("Энэ дүр аль хэдийн бүртгэлтэй байна.", "This character already has an account.");
            case "identity": return Loc.L("Энэ төхөөрөмжийн түлхүүр хүчингүй байна.", "This device's key is not valid.");
            case "confirm": return Loc.L("Нэрээ яг таг бичнэ үү.", "Type your username exactly.");
            case "busy": return Loc.L("Хэт олон хүсэлт. Түр хүлээгээрэй.", "Too many requests. Wait a moment.");
            case "offline": case "no-server": return Loc.L("Сервертэй холбогдож чадсангүй.", "Could not reach the server.");
            default: return Loc.L("Алдаа гарлаа. Дахин оролдоорой.", "Something went wrong. Try again.");
        }
    }
}

public sealed partial class Game
{
    // ---------------------------------------------------------------- what the server told us
    public sealed class ArenaPerson { public string u, tier = "novice"; public int rating = 1000, w, l, d, streak, lvl = 1, xp, rank, hw = -1, hl; public bool on, busy, me; public Look look; }
    public sealed class ArenaHist { public string mode, opp, res; public int delta; }
    public sealed class ArenaInfo
    {
        public bool known; public string u; public int rating = 1000, w, l, d, streak, best, n, xp, lvl = 1; public string tier = "novice";
        public readonly List<ArenaPerson> friends = new List<ArenaPerson>(), inc = new List<ArenaPerson>(), outq = new List<ArenaPerson>();
        public readonly Dictionary<string, List<ArenaPerson>> boards = new Dictionary<string, List<ArenaPerson>>();
        public readonly List<ArenaHist> hist = new List<ArenaHist>();
        public bool queued, friendsKnown; public int queueN; public float queuedAt, helloAt = -99;
        public ArenaFightModal fight;
    }
    public readonly ArenaInfo arena = new ArenaInfo();

    static readonly string[] TIER_IDS = { "novice", "nachin", "khartsaga", "zaan", "garid", "arslan", "avarga" };
    public static string TierName(string id)
    {
        switch (id)
        {
            case "nachin": return Loc.L("Начин", "Falcon"); case "khartsaga": return Loc.L("Харцага", "Hawk"); case "zaan": return Loc.L("Заан", "Elephant");
            case "garid": return Loc.L("Гарьд", "Garuda"); case "arslan": return Loc.L("Арслан", "Lion"); case "avarga": return Loc.L("Аварга", "Titan");
            default: return Loc.L("Шинэ бөх", "Novice");
        }
    }
    public static string TierCol(string id) => id == "avarga" ? "#ff5a3c" : id == "arslan" ? "#f7c646" : id == "garid" ? "#c27cff" : id == "zaan" ? "#3fc1e0" : id == "khartsaga" ? "#5fd36a" : id == "nachin" ? "#c9a46a" : "#b8b0a8";
    public static string TierTag(string id) => $"<color={TierCol(id)}><b>{TierName(id)}</b></color>";
    static Look ArenaLook(J j)
    {
        if (j == null || j.IsNull || !j.IsObject) return null;
        return new Look { deel = j["deel"].S ?? "blue", skin = j["skin"].S ?? "#EDBB93", hat = j["hat"].S, face = j["face"].S, neck = j["neck"].S, gender = j["gender"].S };
    }
    static string Clean(string s) => (s ?? "").Replace("<", "").Replace(">", "");
    static ArenaPerson PersonOf(J r) => new ArenaPerson
    {
        u = Clean(r["u"].S), tier = r["tier"].S ?? "novice", rating = r["rating"].I, w = r["w"].I, l = r["l"].I, d = r["d"].I, streak = r["streak"].I, lvl = Math.Max(1, r["lvl"].I), xp = r["xp"].I,
        rank = r["rank"].I, on = r["on"].B, busy = r["busy"].B, me = r["me"].B, look = ArenaLook(r["look"]), hw = r.Has("hw") ? r["hw"].I : -1, hl = r["hl"].I,
    };

    /** Sends an arena message: t and string fields. */
    public void ArenaSend(string t, params string[] kv)
    {
        var b = new StringBuilder("{\"t\":" + Net.Q(t));
        for (int i = 0; i + 1 < kv.Length; i += 2) b.Append(",").Append(Net.Q(kv[i])).Append(":").Append(Net.Q(kv[i + 1]));
        net.Send(b.Append("}").ToString());
    }
    public void ArenaHello() { arena.helloAt = Time.realtimeSinceStartup; net.Send("{\"t\":\"ar-hello\"}"); }
    /** The connection dropped: the queue is gone (the server forgets it), a bout waits for us to come back. */
    public void ArenaLost() { arena.queued = false; arena.known = false; }

    public void OnArenaMsg(J m)
    {
        var A = arena;
        switch (m["t"].S)
        {
            case "ar-me":
                A.known = true; A.u = m["u"].IsNull ? null : Clean(m["u"].S);
                if (A.u == null) { A.friends.Clear(); A.inc.Clear(); A.outq.Clear(); A.boards.Clear(); A.hist.Clear(); A.friendsKnown = false; return; }
                A.rating = m["rating"].I; A.tier = m["tier"].S ?? "novice"; A.w = m["w"].I; A.l = m["l"].I; A.d = m["d"].I; A.streak = m["streak"].I; A.best = m["best"].I; A.n = m["n"].I; A.xp = m["xp"].I; A.lvl = Math.Max(1, m["lvl"].I);
                return;
            case "fr-list":
                A.friendsKnown = true;
                A.friends.Clear(); foreach (var r in m["friends"]) A.friends.Add(PersonOf(r));
                A.inc.Clear(); foreach (var r in m["inc"]) A.inc.Add(PersonOf(r));
                A.outq.Clear(); foreach (var r in m["out"]) A.outq.Add(PersonOf(r));
                A.friends.Sort((x, y) => (y.on ? 1 : 0) - (x.on ? 1 : 0) != 0 ? (y.on ? 1 : 0) - (x.on ? 1 : 0) : y.rating - x.rating);
                return;
            case "fr-res":
            {
                string u = Clean(m["u"].S), why = m["reason"].S;
                if (m["ok"].B) Toast(why == "sent" ? Loc.L($"{u}-д найзын хүсэлт илгээлээ.", $"Friend request sent to {u}.") : why == "accepted" ? Loc.L($"{u} одоо таны найз!", $"{u} is now your friend!") : Loc.L($"{u}-г жагсаалтаас хаслаа.", $"Removed {u}."));
                else Toast(why == "no-account" ? Loc.L($"«{u}» нэртэй бөх олдсонгүй.", $"No wrestler named \"{u}\".") : why == "self" ? Loc.L("Өөрийгөө найз болгох боломжгүй.", "You can't befriend yourself.") : why == "already" ? Loc.L("Хүсэлт аль хэдийн илгээсэн эсвэл найз болсон байна.", "Already asked, or already friends.") : why == "limit" ? Loc.L("Найзын тоо дээд хэмжээнд хүрлээ.", "The friend list is full.") : Loc.L("Болсонгүй.", "That didn't work."));
                return;
            }
            case "fr-ping":
            {
                string u = Clean(m["u"].S), act = m["act"].S;
                if (act == "sent") { Toast(Loc.L($"{u} тантай найз болохыг хүсэж байна!", $"{u} wants to be your friend!")); Sfx.Chime(); }
                else if (act == "accepted") Toast(Loc.L($"{u} таны найзын хүсэлтийг зөвшөөрлөө!", $"{u} accepted your friend request!"));
                net.Send("{\"t\":\"fr-list\"}");
                return;
            }
            case "ar-queued": if (!A.queued) A.queuedAt = Time.realtimeSinceStartup; A.queued = true; A.queueN = m["n"].I; return;
            case "ar-cancelled": A.queued = false; return;
            case "ar-start":
                A.queued = false; CloseAll();
                A.fight = new ArenaFightModal(this, m); Open(A.fight); Sfx.Horn(); Sfx.Crowd();
                return;
            case "ar-st": A.fight?.OnSnap(m); return;
            case "ar-end": A.fight?.OnEnd(this, m); ArenaHello(); return;
            case "ar-invite":
            {
                string u = Clean(m["u"].S);
                if (A.fight != null && !A.fight.Over) return;
                Sfx.Chime();
                Open(new ConfirmModal(Loc.L("Барилдах урилга!", "Wrestling challenge!"), Loc.L($"{u} ({TierName(m["tier"].S)}, {m["rating"].I}) тантай өргөөнд барилдахыг урьж байна.", $"{u} ({TierName(m["tier"].S)}, {m["rating"].I}) challenges you in the arena."),
                    () => ArenaSend("ar-accept", "u", u)) { onNo = () => ArenaSend("ar-decline", "u", u) });
                return;
            }
            case "ar-sent": Toast(Loc.L($"{Clean(m["u"].S)}-г урилаа. Хариуг хүлээж байна…", $"Challenged {Clean(m["u"].S)}. Waiting for an answer…")); return;
            case "ar-declined": Toast(Loc.L($"{Clean(m["u"].S)} одоохондоо барилдахгүй гэлээ.", $"{Clean(m["u"].S)} declined for now.")); return;
            case "ar-err":
            {
                string why = m["reason"].S, u = Clean(m["u"].S);
                Toast(why == "not-friends" ? Loc.L("Зөвхөн найзаа урьж болно.", "You can only challenge friends.") : why == "offline" ? Loc.L($"{u} одоо сүлжээнд алга.", $"{u} is offline.")
                    : why == "busy" ? Loc.L($"{u} одоо барилдаж байна.", $"{u} is in a bout right now.") : why == "no-invite" ? Loc.L("Урилгын хугацаа дууссан.", "The challenge has expired.")
                    : why == "in-match" ? Loc.L("Та барилдаан дунд байна.", "You are in a bout.") : why == "no-account" ? Loc.L("Эхлээд бөхийн бүртгэл үүсгэнэ үү.", "Make a wrestler account first.") : Loc.L("Серверт алдаа гарлаа.", "Server error."));
                return;
            }
            case "ar-board":
            {
                var list = new List<ArenaPerson>(); foreach (var r in m["rows"]) list.Add(PersonOf(r));
                A.boards[m["k"].S == "friends" ? "friends" : "global"] = list;
                return;
            }
            case "ar-history":
                A.hist.Clear();
                foreach (var h in m["list"]) A.hist.Add(new ArenaHist { mode = h["mode"].S, opp = Clean(h["opp"].S), res = h["res"].S, delta = h["delta"].I });
                return;
        }
    }

    public void OpenArena() => Open(new ArenaHubModal());

    // ---------------------------------------------------------------- the arena's front door
    public sealed class ArenaHubModal : Modal
    {
        string tab = "play", boardK = "global";
        string user = "", addName = "", newName = "", confirmName = "";
        string err = "", note = "", availFor = "", avail = "";
        bool busy, askedLists; float checkAt = -1; Vector2 scroll;
        public override float Width => 700;
        public override (string who, string face)? Speaker => ("zasuul", "proud");

        public override void Tick(Game g, float dt)
        {
            var A = g.arena;
            if (g.net.Online && !A.known && Time.realtimeSinceStartup - A.helloAt > 4) g.ArenaHello();
            // is the username being typed free? asked a moment after typing stops
            if (A.u == null && checkAt > 0 && Time.realtimeSinceStartup > checkAt) { checkAt = -1; Check(g); }
            if (A.u != null && !askedLists && g.net.Online) { askedLists = true; g.net.Send("{\"t\":\"fr-list\"}"); g.net.Send("{\"t\":\"ar-history\"}"); g.ArenaSend("ar-board", "k", boardK); }
        }
        void Check(Game g)
        {
            string name = user.Trim(); if (name.Length < 3) { avail = ""; return; }
            availFor = name;
            g.StartCoroutine(ArenaApi.Post(Family.ServerOf(g, g.V), ArenaApi.Body("check", "username", name), r => { if (availFor != name) return; avail = r["available"].B ? "yes" : r["ok"].B ? r["reason"].S : ""; }));
        }
        GUIStyle Field(Game g) => new GUIStyle(g.sBtn) { alignment = TextAnchor.MiddleLeft };

        public override void Draw(Game g)
        {
            var A = g.arena;
            GUILayout.BeginHorizontal();
            g.IconAt(Px.Wrestler("#d8342b", g.MySkin, "dance", (int)(Time.time * 2) % 2), 48);
            GUILayout.BeginVertical(); g.Title(Loc.L("Бөхийн өргөө", "Wrestling Arena")); g.Small(Loc.L("Найз, гэр бүлтэйгээ онлайн барилдаж, ур чадвараараа цол ахиулаарай.", "Wrestle friends and family online. Skill, not luck, wins."));
            GUILayout.EndVertical(); GUILayout.EndHorizontal();
            if (!g.net.Online)
            {
                g.Fact(Loc.L("Өргөөнд орохын тулд сервертэй холбогдоно (олуулаа горим).", "The arena is on the server: connect (multiplayer) to enter."), true, Loc.L("Сервер", "Server"));
                if (g.net.status == "connecting" || g.net.status == "reconnecting") g.Text(Loc.L("Холбогдож байна…", "Connecting…"));
                else if (g.Btn(Loc.L("Сервертэй холбогдох", "Connect to the server"), true, 44)) g.Later(() => g.net.Connect(g));
                if (g.Btn(Loc.L("Хаах", "Close"), false, 32)) g.Later(g.Close);
                return;
            }
            if (!A.known) { g.Text(Loc.L("Ачаалж байна…", "Loading…")); if (g.Btn(Loc.L("Хаах", "Close"), false, 32)) g.Later(g.Close); return; }
            if (A.u == null) { Account(g); return; }
            GUILayout.BeginHorizontal();
            foreach (var (k, label) in new[] { ("play", Loc.L("Барилдах", "Play")), ("friends", Loc.L("Найзууд", "Friends") + (A.inc.Count > 0 ? $" ({A.inc.Count})" : "")), ("ranks", Loc.L("Чансаа", "Rankings")), ("me", Loc.L("Бүртгэл", "Account")), ("how", Loc.L("Заавар", "How to")) })
                if (g.Btn(label, tab == k, 36)) { var kk = k; g.Later(() => { tab = kk; err = note = ""; scroll = Vector2.zero; }); }
            GUILayout.EndHorizontal();
            g.Gap(4);
            scroll = g.BeginScroll(scroll, Screen.height * 0.56f);
            if (tab == "play") Play(g); else if (tab == "friends") Friends(g); else if (tab == "ranks") Ranks(g); else if (tab == "me") Me(g); else How(g);
            g.EndScroll();
            if (err != "") g.Text("<color=#e14b3d>" + err + "</color>");
            if (note != "") g.Text("<color=#2ea35f>" + note + "</color>");
            if (g.Btn(Loc.L("Хаах", "Close"), false, 30)) g.Later(g.Close);
        }

        // ---------------------------------------------------------------- the wrestler's name
        void Account(Game g)
        {
            g.Small(Loc.L("Хүн бүр өөрийн давтагдашгүй нэртэй. Найзууд чинь энэ нэрээр чамайг олно.", "Everyone has their own unique username. Friends find you by it."));
            GUILayout.Label(Loc.L("Нэр (3–16 үсэг, тоо, _)", "Username (3–16 letters, digits, _)"), g.sSmall);
            string before = user;
            user = GUILayout.TextField(user, 16, Field(g), GUILayout.MinHeight(g.U(38)));
            if (user != before) { avail = ""; checkAt = Time.realtimeSinceStartup + 0.6f; }
            if (avail == "yes") g.Small("<color=#2ea35f>✓ " + Loc.L("Энэ нэр чөлөөтэй!", "This username is free!") + "</color>");
            else if (avail != "") g.Small("<color=#e14b3d>✗ " + ArenaApi.Why(J.Parse("{\"reason\":" + J.Quote(avail) + "}")) + "</color>");
            if (g.Btn(busy ? Loc.L("Түр хүлээнэ үү…", "One moment…") : Loc.L("Нэр авах", "Take this name"), true, 44, !busy)) g.Later(() => Claim(g));
            if (err != "") g.Text("<color=#e14b3d>" + err + "</color>");
            if (g.Btn(Loc.L("Хаах", "Close"), false, 30)) g.Later(g.Close);
        }
        void Claim(Game g)
        {
            err = ""; SaveData.EnsureTicket(g.V); busy = true;
            g.StartCoroutine(ArenaApi.Post(Family.ServerOf(g, g.V), ArenaApi.Body("claim", "id", g.V.id, "ticket", g.V.ticket, "username", user.Trim()), r =>
            {
                busy = false;
                if (!r["ok"].B) { err = ArenaApi.Why(r); Sfx.Bad(); return; }
                Sfx.Win(); g.Confetti(40);
                g.Toast(Loc.L($"Тавтай морил, {r["username"].S}! Таны бөхийн нэр бэлэн боллоо.", $"Welcome, {r["username"].S}! Your wrestler name is ready."));
                g.ArenaHello();
            }));
        }

        // ---------------------------------------------------------------- play
        void Card(Game g)
        {
            var A = g.arena;
            GUILayout.BeginVertical(g.sPanel);
            GUILayout.BeginHorizontal();
            g.IconAt(Px.Char(g.V.look, 0, 0), 44);
            GUILayout.BeginVertical();
            GUILayout.Label($"<b>{A.u}</b>  ·  {TierTag(A.tier)}  ·  {Loc.L("Оноо", "Rating")} <b>{A.rating}</b>", g.sLabel);
            GUILayout.Label(Loc.L($"Түвшин {A.lvl} · {A.xp} XP · {A.w} ялалт · {A.l} ялагдал · {A.d} тэнцээ", $"Level {A.lvl} · {A.xp} XP · {A.w} W · {A.l} L · {A.d} D")
                + (A.streak > 1 ? Loc.L($" · 🔥{A.streak} дараалан", $" · 🔥{A.streak} in a row") : ""), g.sSmall);
            GUILayout.EndVertical(); GUILayout.EndHorizontal();
            var next = NextTier(A.rating);
            if (next.id != null) g.Small(Loc.L($"«{TierName(next.id)}» цол хүртэл {next.need - A.rating} оноо.", $"{next.need - A.rating} rating to \"{TierName(next.id)}\"."));
            GUILayout.EndVertical();
        }
        static (string id, int need) NextTier(int rating)
        {
            var steps = new[] { ("nachin", 1050), ("khartsaga", 1150), ("zaan", 1250), ("garid", 1350), ("arslan", 1500), ("avarga", 1650) };
            foreach (var s in steps) if (rating < s.Item2) return s;
            return (null, 0);
        }
        void Play(Game g)
        {
            var A = g.arena;
            Card(g);
            g.Gap(4);
            if (A.queued)
            {
                int secs = (int)(Time.realtimeSinceStartup - A.queuedAt);
                g.Fact(Loc.L($"Тэнцүү хүчтэй өрсөлдөгч хайж байна… {secs} сек · дараалалд {A.queueN}", $"Looking for an opponent of your level… {secs}s · {A.queueN} in the queue"), true, Loc.L("Чансааны барилдаан", "Ranked bout"));
                if (g.Btn(Loc.L("Болих", "Cancel"), false, 40)) g.Later(() => g.net.Send("{\"t\":\"ar-cancel\"}"));
            }
            else if (g.Btn(Loc.L("Чансааны барилдаан", "Ranked bout") + "\n<size=" + g.Ts(10) + ">" + Loc.L("Ойролцоо оноотой өрсөлдөгчтэй таарч, оноо хожно", "Matched with someone near your rating; win to climb") + "</size>", true, 60))
                g.Later(() => g.net.Send("{\"t\":\"ar-queue\"}"));
            GUILayout.Label(Loc.L("Компьютертэй дасгал (оноонд нөлөөлөхгүй)", "Spar with the CPU (rating unchanged)"), g.sSmall);
            GUILayout.BeginHorizontal();
            for (int lv = 1; lv <= 3; lv++)
            {
                string label = lv == 1 ? Loc.L("Багш (амархан)", "Coach (easy)") : lv == 2 ? Loc.L("Начин (дунд)", "Falcon (medium)") : Loc.L("Аварга (хэцүү)", "Titan (hard)");
                if (g.Btn(label, false, 40, !A.queued)) { int l = lv; g.Later(() => g.net.Send("{\"t\":\"ar-cpu\",\"lv\":" + l + "}")); }
            }
            GUILayout.EndHorizontal();
            g.Small(Loc.L("Найзаа урих бол «Найзууд» хэсэгт ор.", "To challenge a friend, open Friends."));
            if (A.hist.Count > 0)
            {
                GUILayout.Label("<b>" + Loc.L("Сүүлийн барилдаанууд", "Recent bouts") + "</b>", g.sLabel);
                foreach (var h in A.hist)
                {
                    string res = h.res == "win" ? "<color=#2ea35f>" + Loc.L("Ялсан", "Won") + "</color>" : h.res == "loss" ? "<color=#e14b3d>" + Loc.L("Ялагдсан", "Lost") + "</color>" : Loc.L("Тэнцсэн", "Draw");
                    string mode = h.mode == "ranked" ? Loc.L("Чансаа", "Ranked") : h.mode == "friend" ? Loc.L("Найз", "Friend") : Loc.L("Дасгал", "Spar");
                    GUILayout.Label($"{res} · {h.opp} · {mode}" + (h.delta != 0 ? $" · {(h.delta > 0 ? "+" : "")}{h.delta}" : ""), g.sSmall);
                }
            }
        }

        // ---------------------------------------------------------------- friends
        void Friends(Game g)
        {
            var A = g.arena;
            GUILayout.Label(Loc.L("Нэрээр нь найз нэмэх", "Add a friend by username"), g.sSmall);
            GUILayout.BeginHorizontal();
            addName = GUILayout.TextField(addName, 16, Field(g), GUILayout.MinHeight(g.U(38)));
            if (g.SmallBtn(Loc.L("Нэмэх", "Add"), true, 90, addName.Trim().Length >= 3)) { string u = addName.Trim(); g.Later(() => { g.ArenaSend("fr-add", "u", u); addName = ""; }); }
            GUILayout.EndHorizontal();
            if (A.inc.Count > 0)
            {
                GUILayout.Label("<b>" + Loc.L("Найзын хүсэлтүүд", "Friend requests") + "</b>", g.sLabel);
                foreach (var f in A.inc)
                {
                    GUILayout.BeginHorizontal(g.sPanel); Person(g, f, false);
                    if (g.SmallBtn(Loc.L("Зөвшөөрөх", "Accept"), true, 100)) { var u = f.u; g.Later(() => g.ArenaSend("fr-accept", "u", u)); }
                    if (g.SmallBtn(Loc.L("Татгалзах", "Decline"), false, 90)) { var u = f.u; g.Later(() => g.ArenaSend("fr-remove", "u", u)); }
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.Label("<b>" + Loc.L("Найзууд", "Friends") + $"</b> ({A.friends.Count})", g.sLabel);
            if (!A.friendsKnown) g.Small(Loc.L("Ачаалж байна…", "Loading…"));
            else if (A.friends.Count == 0) g.Small(Loc.L("Одоохондоо найз алга. Найзынхаа нэрийг асуугаад нэмээрэй!", "No friends yet. Ask a friend for their username and add them!"));
            foreach (var f in A.friends)
            {
                GUILayout.BeginHorizontal(g.sPanel); Person(g, f, true);
                if (g.SmallBtn(Loc.L("Урих", "Challenge"), true, 100, f.on && !f.busy)) { var u = f.u; g.Later(() => g.ArenaSend("ar-challenge", "u", u)); }
                if (g.SmallBtn("✕", false, 40)) { var u = f.u; g.Later(() => g.Open(new ConfirmModal(Loc.L("Найзаас хасах уу?", "Remove friend?"), u, () => g.ArenaSend("fr-remove", "u", u)))); }
                GUILayout.EndHorizontal();
            }
            if (A.outq.Count > 0)
            {
                GUILayout.Label("<b>" + Loc.L("Илгээсэн хүсэлтүүд", "Sent requests") + "</b>", g.sLabel);
                foreach (var f in A.outq)
                {
                    GUILayout.BeginHorizontal(g.sPanel); Person(g, f, false);
                    if (g.SmallBtn(Loc.L("Цуцлах", "Cancel"), false, 90)) { var u = f.u; g.Later(() => g.ArenaSend("fr-remove", "u", u)); }
                    GUILayout.EndHorizontal();
                }
            }
            if (g.Btn(Loc.L("Шинэчлэх", "Refresh"), false, 30)) g.Later(() => g.net.Send("{\"t\":\"fr-list\"}"));
        }
        static void Person(Game g, ArenaPerson f, bool detail)
        {
            g.IconAt(Px.Char(f.look ?? new Look(), 0, 0), 32);
            GUILayout.BeginVertical();
            string dot = f.on ? (f.busy ? "<color=#f7c646>●</color> " : "<color=#2ea35f>●</color> ") : "<color=#8a8a8a>●</color> ";
            GUILayout.Label(dot + $"<b>{f.u}</b> · {TierTag(f.tier)} · {f.rating}", g.sLabel);
            string sub = Loc.L($"Түвшин {f.lvl} · {f.w} ялалт · {f.l} ялагдал", $"Lv {f.lvl} · {f.w} W · {f.l} L");
            if (detail && f.hw >= 0) sub += Loc.L($" · Та {f.hw}–{f.hl} байна", $" · You vs them {f.hw}–{f.hl}");
            if (detail) sub += " · " + (f.busy ? Loc.L("барилдаж байна", "in a bout") : f.on ? Loc.L("сүлжээнд", "online") : Loc.L("сүлжээнд алга", "offline"));
            GUILayout.Label(sub, g.sSmall);
            GUILayout.EndVertical(); GUILayout.FlexibleSpace();
        }

        // ---------------------------------------------------------------- the boards
        void Ranks(Game g)
        {
            var A = g.arena;
            GUILayout.BeginHorizontal();
            foreach (var (k, label) in new[] { ("global", Loc.L("Бүх бөхчүүд", "Everyone")), ("friends", Loc.L("Найзууд", "Friends")) })
                if (g.Btn(label, boardK == k, 34)) { var kk = k; g.Later(() => { boardK = kk; g.ArenaSend("ar-board", "k", kk); }); }
            GUILayout.EndHorizontal();
            if (!A.boards.TryGetValue(boardK, out var rows)) { g.Small(Loc.L("Ачаалж байна…", "Loading…")); return; }
            if (rows.Count == 0) g.Small(Loc.L("Одоохондоо хэн ч барилдаагүй байна. Эхнийх нь бол!", "Nobody has wrestled yet. Be the first!"));
            var head = new GUIStyle(g.sSmall) { fontStyle = FontStyle.Bold };
            GUILayout.BeginHorizontal(); GUILayout.Label("#", head, GUILayout.Width(g.U(34))); GUILayout.Label(Loc.L("Бөх", "Wrestler"), head); GUILayout.FlexibleSpace();
            GUILayout.Label(Loc.L("Оноо", "Rating"), head, GUILayout.Width(g.U(60))); GUILayout.Label(Loc.L("Я-Ял", "W-L"), head, GUILayout.Width(g.U(64))); GUILayout.Label(Loc.L("Түв.", "Lv"), head, GUILayout.Width(g.U(40))); GUILayout.EndHorizontal();
            foreach (var r in rows)
            {
                GUILayout.BeginHorizontal(r.me ? g.sPanel : GUIStyle.none);
                GUILayout.Label("<b>" + r.rank + "</b>", g.sLabel, GUILayout.Width(g.U(34)));
                g.IconAt(Px.Char(r.look ?? new Look(), 0, 0), 26);
                GUILayout.Label((r.on ? "<color=#2ea35f>●</color> " : "") + $"<b>{r.u}</b>" + (r.me ? " · " + Loc.L("Би", "Me") : "") + "\n" + TierTag(r.tier), g.sSmall);
                GUILayout.FlexibleSpace();
                GUILayout.Label("<b>" + r.rating + "</b>", g.sLabel, GUILayout.Width(g.U(60)));
                GUILayout.Label($"{r.w}-{r.l}", g.sSmall, GUILayout.Width(g.U(64)));
                GUILayout.Label(r.lvl.ToString(), g.sSmall, GUILayout.Width(g.U(40)));
                GUILayout.EndHorizontal();
            }
            if (g.Btn(Loc.L("Шинэчлэх", "Refresh"), false, 30)) g.Later(() => g.ArenaSend("ar-board", "k", boardK));
        }

        // ---------------------------------------------------------------- the account
        void Me(Game g)
        {
            var A = g.arena;
            g.Fact(Loc.L($"Таны нэр: <b>{A.u}</b>. Найзууд тань энэ нэрээр олно.", $"Your username: <b>{A.u}</b>. Friends find you by it."), true, Loc.L("Бүртгэл", "Account"));
            GUILayout.Label("<b>" + Loc.L("Нэр солих", "Change username") + "</b>", g.sLabel);
            g.Small(Loc.L("Хуучин нэр чинь бусдад чөлөөтэй болно.", "Your old name becomes free for others."));
            newName = GUILayout.TextField(newName, 16, Field(g), GUILayout.MinHeight(g.U(36)));
            if (g.Btn(Loc.L("Нэр солих", "Change username"), false, 36, !busy && newName.Trim().Length >= 3)) g.Later(() => Act(g, ArenaApi.Body("rename", "id", g.V.id, "ticket", g.V.ticket, "username", newName.Trim()), Loc.L("Нэр солигдлоо.", "Username changed."), () => newName = ""));
            g.Gap(10);
            GUILayout.Label("<b><color=#e14b3d>" + Loc.L("Бөхийн нэр устгах", "Delete wrestler name") + "</color></b>", g.sLabel);
            g.Small(Loc.L("Нэр, найзууд, оноо, түүх устна. Дүр, зоос, XP чинь үлдэнэ.", "Your username, friends, rating and record are deleted. Your character, coins and XP stay."));
            GUILayout.Label(Loc.L("Баталгаажуулахын тулд нэрээ бичнэ үү", "Type your username to confirm"), g.sSmall);
            confirmName = GUILayout.TextField(confirmName, 16, Field(g), GUILayout.MinHeight(g.U(36)));
            if (g.Btn(Loc.L("Бүрмөсөн устгах", "Delete for good"), false, 36, !busy && confirmName.Trim().Length >= 3))
                g.Later(() => Act(g, ArenaApi.Body("delete", "id", g.V.id, "ticket", g.V.ticket, "confirm", confirmName.Trim()), Loc.L("Устлаа.", "Deleted."), () => { confirmName = ""; tab = "play"; }));
        }
        void Act(Game g, string body, string okText, Action after)
        {
            busy = true; err = note = "";
            g.StartCoroutine(ArenaApi.Post(Family.ServerOf(g, g.V), body, r =>
            {
                busy = false;
                if (!r["ok"].B) { err = ArenaApi.Why(r); Sfx.Bad(); return; }
                note = okText; Sfx.Good(); after?.Invoke(); g.ArenaHello();
            }));
        }

        // ---------------------------------------------------------------- how to wrestle
        void How(Game g)
        {
            g.Fact(Loc.L("Тоглогч бүр яг адилхан бөх. Санамсаргүй зүйл байхгүй: цаг тааруулж, өрсөлдөгчөө уншиж, зөв хариулсан нь ялна.", "Every wrestler is the same. Nothing is random: timing, reading your opponent and the right answer win."), true, Loc.L("Ур чадвар", "Skill"));
            g.Text(Loc.L("<b>Удирдлага</b> (гар): ← → алхах (хоёр дарвал гүйх) · <b>J</b> түлхэх · <b>K</b> шилбэ (→K мөрөөр дайрах) · <b>L</b> барьцаа (→L шүүрэх) · <b>Space/I</b> тэнцвэр барих · <b>O</b> аваргын мэх. Утсан дээр дэлгэцийн товчнууд.",
                "<b>Controls</b> (keyboard): ← → walk (double-tap to dash) · <b>J</b> shove (→J push) · <b>K</b> sweep (→K shoulder charge) · <b>L</b> grab (→L lunge) · <b>Space/I</b> brace · <b>O</b> champion's throw. On a phone, the buttons on screen."));
            g.Text(Loc.L("<b>Хайч, чулуу, даавуу шиг:</b>\n• Түлхэлт барьцааг таслана.\n• Тэнцвэр барих (brace) түлхэлт, шилбийг зогсооно. Цохилт ирэхийн яг өмнө дарвал няцааж, өрсөлдөгч чинь ганхана.\n• Барьцаа тэнцвэр барьсныг давж барилцана.",
                "<b>Like rock, paper, scissors:</b>\n• A shove interrupts a grab.\n• Bracing stops shoves and sweeps. Brace just before the hit lands to parry: your opponent staggers.\n• A grab goes through a brace into the clinch."));
            g.Text(Loc.L("<b>Барьцаанд:</b> барьсан бөх мэх хийнэ: →K дэгээ, ←K гуя, K өргөлт. Мэх бүр өөр хөдөлгөөнтэй эхэлдэг. Нөгөө бөх нь тэр мэхийг таниад яг адилхан чиглэл + L дарвал мэхийг эргүүлж, барьцааг өөрөө авна. Эрт таах гэвэл J (татах) хийж барина. J дарвал түлхээд мултарна.",
                "<b>In the clinch:</b> the one holding the grip throws: →K hook, ←K hip toss, K lift. Each throw starts with its own tell. The other wrestler reads it and answers with the same direction + L to turn it over and take the grip. Guess too early and a J jolt punishes it. J also shoves you out of the clinch."));
            g.Text(Loc.L("<b>Тэнцвэр</b> (дээд шар зураас) дуусвал унана. <b>Тэсвэр</b> (ногоон) мэх бүрт зарцуулагдана. <b>Бүргэд</b> (цэнхэр) дүүрвэл O дарж аваргын мэх хий. Хоёр даваа түрүүлж авсан нь ялна.",
                "<b>Balance</b> (the yellow bar) gone means a fall. <b>Stamina</b> (green) pays for every move. When the <b>eagle</b> meter (blue) is full, O throws the champion's throw. Two rounds win the bout."));
        }
        public override void Press(Game g) { }
    }
}
