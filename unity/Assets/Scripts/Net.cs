// Playing together (server.js /ws, like js/village/village.js's net): everyone on the same Wi-Fi with the same room code
// shares one Нутаг. You see each other walk, ride and jump, say phrases and emotes, see each other's gers, and can
// challenge a friend to a live wrestling match. The server address is the one in Settings (the same as Basar AI's).
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

public sealed class Net
{
    public const int VERSION = 13;
    public static readonly Tx[] PHRASES =
    {
        new Tx("Сайн уу!", "Hi!"), new Tx("Хамт тоглоё!", "Let's play!"), new Tx("Баярлалаа!", "Thank you!"), new Tx("Энд ир!", "Come here!"),
        new Tx("Саалиа хийе!", "Let's milk!"), new Tx("Уралдъя!", "Let's race!"), new Tx("Загасчилъя!", "Let's fish!"), new Tx("Баяртай!", "Bye!"),
    };
    public static readonly string[] EMOTES = { "👋", "❤️", "😂", "🎉", "👍", "🐎", "😮", "⭐" };

    public sealed class Other { public string id, name; public Look look; public int lvl, dir; public float x, y, px, py, jt = -9; public bool moving; public string bubble; public PxCanvas bubbleIcon; public float bubbleT = -99; }
    public sealed class Lot { public string by, n, roof, door; public int stage; }
    /** A line of the room's chat log: a phrase, an emote, someone arriving or leaving. */
    public sealed class ChatLine { public string k, id, n; public int i; }
    public readonly List<ChatLine> chat = new List<ChatLine>(); public int unread; public bool chatOpen;
    const int CHAT_KEEP = 30;
    void AddChat(Game g, ChatLine l) { chat.Add(l); if (chat.Count > CHAT_KEEP) chat.RemoveRange(0, chat.Count - CHAT_KEEP); if (!chatOpen && (l.k == "p" || l.k == "e") && l.id != g.V.id) unread++; }
    public static string ChatText(ChatLine l) => l.k == "join" ? Loc.L("нутагт ирлээ", "joined the village") : l.k == "leave" ? Loc.L("нутгаас явлаа", "left the village") : l.k == "e" ? null : (l.i >= 0 && l.i < PHRASES.Length ? PHRASES[l.i] : PHRASES[0]).ToString();

    public string status = "solo"; // solo | connecting | online | reconnecting
    public string failWhy;           // why the last connect gave up (shown with "Connect again"), null when fine
    public string room = "", serverUrl;
    string backRoom; int opening;    // backRoom: the room to go back to when a typed code is wrong · opening: which OpenRoom answer still counts
    public readonly Dictionary<string, Other> others = new Dictionary<string, Other>();
    public Lot[] lots; public int total, nutag; public bool replaced;
    public bool Online => status == "online";
    /** The XP leaderboards (server leaderboard.js), asked for when their window opens or its tab changes. */
    public sealed class BoardRow { public int rank, xp, avatar; public string name; public Look look; public bool me; }
    public sealed class Board { public readonly List<BoardRow> rows = new List<BoardRow>(); public string reason; public float at; }
    public readonly Dictionary<string, Board> boards = new Dictionary<string, Board>(); public readonly Dictionary<string, float> boardAsked = new Dictionary<string, float>();
    public const float BOARD_WAIT = 8; // an older server never answers `board`

    ClientWebSocket ws; CancellationTokenSource cts; readonly ConcurrentQueue<string> inbox = new ConcurrentQueue<string>(), outbox = new ConcurrentQueue<string>();
    readonly SemaphoreSlim sendSignal = new SemaphoreSlim(0); int retry; float retryAt = -1, openAt = -1, connectAt; bool closed = true, welcomed;
    float moveT, lookT; Vector2 sent = new Vector2(-99, -99); string sentLook;
    long walletRev = -1; bool syncing; float syncAt = -1, syncSentAt; string serverDay = "", capDay = "";
#if UNITY_WEBGL && !UNITY_EDITOR
    // in a browser there are no threads: the socket is the browser's own (Plugins/WebGL/NaadamyWs.jslib), read every frame in Tick
    [DllImport("__Internal")] static extern void NaadamyWsOpen(string url);
    [DllImport("__Internal")] static extern int NaadamyWsState();
    [DllImport("__Internal")] static extern void NaadamyWsSend(string msg);
    [DllImport("__Internal")] static extern string NaadamyWsPoll();
    [DllImport("__Internal")] static extern void NaadamyWsClose();
    bool webLive; string webJoin;
    bool Live => webLive;
#else
    bool Live => ws != null;
#endif

#if UNITY_WEBGL && !UNITY_EDITOR
    const string WS_SCHEME = "wss://"; // a web page served over https may only open wss
#else
    const string WS_SCHEME = "ws://";
#endif
    static string WsUrl(string server)
    {
        string s = (server ?? "").Trim().TrimEnd('/'); if (s == "") return null;
        if (s.StartsWith("https://")) s = "wss://" + s.Substring(8); else if (s.StartsWith("http://")) s = "ws://" + s.Substring(7); else if (!s.StartsWith("ws")) s = WS_SCHEME + s;
        return s + "/ws";
    }
    /** Connect (again) with the current server address and room. No address: play alone. */
    public void Connect(Game g)
    {
        Close(); replaced = false; welcomed = false; failWhy = null; retry = 0; connectAt = Time.realtimeSinceStartup;
        serverUrl = WsUrl(Family.ServerOf(g, g.V)); room = g.V.room ?? "";
        if (serverUrl == null) { status = "solo"; return; }
        closed = false; status = "connecting";
        if (room == "") OpenRoom(g); else Open(g);
    }
    /** No room yet: the server opens one, and its 6-letter code is what friends type to come in. */
    void OpenRoom(Game g)
    {
        var V = g.V; SaveData.EnsureTicket(V); int mine = ++opening;
        g.StartCoroutine(ArenaApi.Post(Family.ServerOf(g, V), ArenaApi.Body("open", "id", V.id, "ticket", V.ticket), r =>
        {
            if (mine != opening) return; // closed, or connecting anew, in the meantime
            if (!r["ok"].B) { GiveUp(g, Loc.L("Өрөө нээж чадсангүй. Дараа дахин оролдоорой.", "Could not open a room. Try again a bit later.")); return; }
            room = V.room = r["code"].S; V.Save(); Open(g);
        }, "/api/room"));
    }
    public void NewRoom(Game g) { g.V.room = ""; g.V.Save(); Connect(g); }
    public void JoinRoom(Game g, string code) { backRoom = g.V.room; g.V.room = code; g.V.Save(); Connect(g); }
    public void Close()
    {
        closed = true; retryAt = -1; opening++;
#if UNITY_WEBGL && !UNITY_EDITOR
        if (webLive) { webLive = false; NaadamyWsClose(); }
#endif
        try { cts?.Cancel(); } catch (ObjectDisposedException) { }
        var w = ws; ws = null; cts = null;
        if (w != null) Task.Run(async () => { try { await w.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); } catch (Exception) { } w.Dispose(); });
        others.Clear(); lots = null; if (status != "solo") status = "solo";
        while (inbox.TryDequeue(out _)) { } while (outbox.TryDequeue(out _)) { }
    }
    /** Stop trying: a refused join or a server that never answers. Playing alone goes on; "Connect again" tries anew. */
    void GiveUp(Game g, string why) { Close(); failWhy = why; status = "solo"; g.Toast(why); }
    void Open(Game g)
    {
        var V = g.V; SaveData.EnsureTicket(V); openAt = Time.realtimeSinceStartup;
        string join = Obj("t", "join", "room", room, "id", V.id, "ticket", V.ticket, "name", V.name) + "," + Raw("look", LookJson(V.look)) + "," + Raw("lvl", Rules.Level(V.xp).l.ToString()) + "," + Raw("x", F2(g.MyX)) + "," + Raw("y", F2(g.MyY)) + "," + Raw("dir", "0");
        string url = serverUrl;
#if UNITY_WEBGL && !UNITY_EDITOR
        webLive = true; webJoin = "{" + join + "}"; NaadamyWsOpen(url); // join goes out once the browser says the socket is open
#else
        var w = new ClientWebSocket(); var c = new CancellationTokenSource(); ws = w; cts = c;
        Task.Run(async () =>
        {
            try
            {
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(c.Token)) { timeout.CancelAfter(6000); await w.ConnectAsync(new Uri(url), timeout.Token); }
                outbox.Enqueue("{" + join + "}"); sendSignal.Release();
                var sender = SendLoop(w, c.Token);
                var buf = new byte[16 * 1024]; var sb = new List<byte>();
                while (w.State == WebSocketState.Open && !c.IsCancellationRequested)
                {
                    var r = await w.ReceiveAsync(new ArraySegment<byte>(buf), c.Token);
                    if (r.MessageType == WebSocketMessageType.Close) break;
                    for (int i = 0; i < r.Count; i++) sb.Add(buf[i]);
                    if (r.EndOfMessage) { inbox.Enqueue(Encoding.UTF8.GetString(sb.ToArray())); sb.Clear(); }
                }
            }
            catch (Exception) { /* no server, or the Wi-Fi dropped */ }
            if (ws == w) inbox.Enqueue("{\"t\":\"_closed\"}");
        });
#endif
    }
    async Task SendLoop(ClientWebSocket w, CancellationToken tok)
    {
        try
        {
            while (!tok.IsCancellationRequested && w.State == WebSocketState.Open)
            {
                await sendSignal.WaitAsync(tok);
                while (outbox.TryDequeue(out var m)) await w.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(m)), WebSocketMessageType.Text, true, tok);
            }
        }
        catch (Exception) { }
    }
    /** The cloud save goes up when it changed since the server last had it (checked every few seconds). */
    string cloudSent = ""; float cloudT;
    void SaveCloud(Game g)
    {
        if (!Online || g.V == null) return;
        string cloud = Cloud.Of(g.V);
        if (cloud == cloudSent) return;
        cloudSent = cloud;
        Send("{\"t\":\"cloud-save\",\"cloud\":" + cloud + "}");
    }
    public void Send(string json)
    {
        if (!Online) return;
#if UNITY_WEBGL && !UNITY_EDITOR
        NaadamyWsSend(json);
#else
        outbox.Enqueue(json); sendSignal.Release();
#endif
    }

    // ---------------------------------------------------------------- tiny JSON writer
    public static string Q(string s)
    {
        var b = new StringBuilder("\""); foreach (char ch in s ?? "") { if (ch == '"' || ch == '\\') b.Append('\\').Append(ch); else if (ch < 32) b.Append("\\u").Append(((int)ch).ToString("x4")); else b.Append(ch); }
        return b.Append('"').ToString();
    }
    static string F2(float v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    static string Raw(string k, string v) => Q(k) + ":" + v;
    static string Obj(params string[] kv) { var b = new List<string>(); for (int i = 0; i < kv.Length; i += 2) b.Add(Q(kv[i]) + ":" + (kv[i + 1] == null ? "null" : Q(kv[i + 1]))); return string.Join(",", b); }
    public static string LookJson(Look l) => "{" + Obj("deel", l.deel, "skin", l.skin, "hat", l.hat, "face", l.face, "neck", l.neck, "pet", l.pet, "title", l.title, "ride", l.ride, "coat", l.coat, "gender", l.gender) + "}";
    static Look LookOf(J j) => j.IsNull ? new Look() : new Look { deel = j["deel"].S ?? "blue", skin = j["skin"].S ?? "#F6D3B3", hat = j["hat"].S, face = j["face"].S, neck = j["neck"].S, pet = j["pet"].S, title = j["title"].S, ride = j["ride"].S, coat = j["coat"].S, gender = j["gender"].S };
    static Lot LotOf(J j) => j.IsNull ? null : new Lot { by = j["by"].S, n = j["n"].S, roof = j["roof"].S ?? "blue", door = j["door"].S ?? "red", stage = j["stage"].I };
    static ChatLine ChatOf(J j) => new ChatLine { k = j["k"].S, id = j["id"].S, n = j["n"].S, i = j["i"].I };

    // ---------------------------------------------------------------- every frame, on the main thread
    public void Tick(Game g, float dt)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        if (webLive)
        {
            int st = NaadamyWsState();
            if (st == 1 && webJoin != null) { NaadamyWsSend(webJoin); webJoin = null; }
            for (string m; (m = NaadamyWsPoll()) != null;) inbox.Enqueue(m);
            if (st == 2) { webLive = false; NaadamyWsClose(); inbox.Enqueue("{\"t\":\"_closed\"}"); }
        }
#endif
        while (inbox.TryDequeue(out var text)) { J m; try { m = J.Parse(text); } catch (Exception) { continue; } Handle(g, m); }
        if (!closed && !Live && retryAt > 0 && Time.realtimeSinceStartup > retryAt) { retryAt = -1; status = "reconnecting"; Open(g); }
        if (Live && !Online && openAt > 0 && Time.realtimeSinceStartup - openAt > 15) // a socket that never opens (a browser can sit at "connecting"), or a join nobody answers
        {
            openAt = -1;
#if UNITY_WEBGL && !UNITY_EDITOR
            webLive = false; NaadamyWsClose(); inbox.Enqueue("{\"t\":\"_closed\"}"); // drop it: the usual retry follows
#else
            try { cts?.Cancel(); } catch (ObjectDisposedException) { }
#endif
        }
        foreach (var o in others.Values) // others glide to where the server says they are
        {
            float dx = o.x - o.px, dy = o.y - o.py, d = Mathf.Sqrt(dx * dx + dy * dy); o.moving = d > 0.02f;
            if (d > 6) { o.px = o.x; o.py = o.y; } else { float a = Mathf.Min(1, dt * 10); o.px += dx * a; o.py += dy * a; }
        }
        if (!Live && status == "solo" && g.V != null && (saveT += dt) > 30) { saveT = 0; SaveAlone(g); }
        if (!Online || g.V == null) return;
        if (syncing && Time.realtimeSinceStartup - syncSentAt > 8) syncing = false; // no answer: the same upload goes again later
        if (syncAt > 0 && Time.realtimeSinceStartup > syncAt) { syncAt = -1; Sync(g); }
        if ((cloudT += dt) > 5) { cloudT = 0; SaveCloud(g); }
        moveT += dt; lookT += dt;
        var me = new Vector2(g.MyX, g.MyY); float moved = (me - sent).magnitude;
        if ((moveT > 0.1f && moved > 0.03f) || (moved > 0.001f && moveT > 0.4f)) { moveT = 0; sent = me; Send("{" + Obj("t", "move") + "," + Raw("x", F2(me.x)) + "," + Raw("y", F2(me.y)) + "," + Raw("dir", g.MyDir.ToString()) + "}"); }
        if (lookT > 1.5f) { lookT = 0; var l = g.V.look.Copy(); l.ride = g.MyRiding ? "horse" : null; if (l.ride != null) l.coat = g.V.horse.coat; string lj = LookJson(l); if (lj != sentLook) { sentLook = lj; Send("{" + Obj("t", "look") + "," + Raw("look", lj) + "," + Raw("lvl", Rules.Level(g.V.xp).l.ToString()) + "}"); } }
    }
    void AddOther(J p) { var o = new Other { id = p["id"].S, name = p["name"].S, look = LookOf(p["look"]), lvl = p["lvl"].I, x = p["x"].F, y = p["y"].F, dir = p["dir"].I }; o.px = o.x; o.py = o.y; others[o.id] = o; }
    void Handle(Game g, J m)
    {
        string t = m["t"].S;
        if (t != null && (t.StartsWith("ar-") || t.StartsWith("fr-"))) { g.OnArenaMsg(m); return; } // the online arena (Arena.cs)
        switch (t)
        {
            case "_closed":
                ws = null; bool was = Online; others.Clear(); lots = null; g.EndRemoteMatch(); g.ArenaLost(); syncing = false;
                if (closed) { status = "solo"; return; }
                if (replaced) { status = "solo"; closed = true; return; }
                if (!welcomed && Time.realtimeSinceStartup - connectAt > 60) { string at = Family.ServerOf(g, g.V); GiveUp(g, Loc.L($"Сервертэй холбогдож чадсангүй ({at}). Тохиргоонд серверийн хаягаа шалгаад дахин холбогдоорой.", $"Could not reach the server ({at}). Check the server address in Settings and connect again.")); return; } // never got in for a minute (a sleeping server wakes within that): a wrong address, not a dropped line
                status = "reconnecting"; retryAt = Time.realtimeSinceStartup + Mathf.Min(10, 1.5f * ++retry);
                if (was) g.Toast(Loc.L("Сервертэй холболт тасарлаа. Дахин холбогдож байна…", "Lost the server. Reconnecting…"));
                return;
            case "error":
                if (m["why"].S == "erased") // this child's family was erased on the server: forget it here
                {
                    replaced = true; closed = true; status = "solo"; others.Clear();
                    if (g.V != null) Family.Gone(g, g.V);
                    return;
                }
                if (m["why"].S == "identity") // a wrong ticket, or this device's dropped out of the last 8: the family stays
                {
                    replaced = true; closed = true; status = "solo"; others.Clear();
                    g.Toast(Loc.L("Токен хүчингүй байна. Дахин нэвтэрнэ үү.", "Invalid ticket. Please sign in again."));
                    return;
                }
                if (m["why"].S == "no-room" || m["why"].S == "room-locked") // a mistyped code: "Connect again" goes back to the room before it
                {
                    if (g.V != null) { g.V.room = backRoom ?? ""; g.V.Save(); }
                    backRoom = null;
                    int minutes = Mathf.Max(1, Mathf.CeilToInt(m["wait"].I / 60f));
                    GiveUp(g, m["why"].S == "no-room" ? Loc.L("Ийм кодтой өрөө олдсонгүй. Кодоо шалгаарай.", "No room has that code. Check it and try again.")
                        : Loc.L($"Хэт олон удаа буруу код бичлээ. {minutes} минут хүлээгээрэй.", $"Too many wrong codes. Wait {minutes} min."));
                    return;
                }
                if (m["why"].S == "storage")
                {
                    if (!Online) GiveUp(g, Loc.L("Серверийн хадгалалт алдаа гарлаа. Дараа дахин холбогдоорой.", "Server storage error. Connect again a bit later."));
                    else g.Toast(Loc.L("Серверийн хадгалалт алдаа гарлаа.", "Server storage error."));
                    return;
                }
                if (!Online) GiveUp(g, Loc.L("Өрөөнд нэвтэрч чадсангүй. Өрөөний кодоо шалгаарай.", "Could not join the room. Check the room code."));
                else g.Toast(Loc.L("Өрөөнд нэвтэрч чадсангүй.", "Could not join the room."));
                return;
            case "welcome":
                status = "online"; retry = 0; welcomed = true; openAt = -1; failWhy = null; room = m["room"].S; others.Clear(); foreach (var p in m["players"]) AddOther(p); boards.Clear(); boardAsked.Clear();
                lots = m["lots"].Select(LotOf).ToArray(); if (lots.Length < Rules.LOTS) Array.Resize(ref lots, Rules.LOTS);
                sentLook = null; lookT = 99; sent = new Vector2(-99, -99);
                g.Toast(Loc.L("Нутагт холбогдлоо", "Connected to the village") + " · " + room + (others.Count > 0 ? " · " + others.Count + Loc.L(" найз байна", " friends here") : ""));
                if (m["v"].I != VERSION) g.Toast(Loc.L("Сервер хуучин хувилбартай байна. Серверийг дахин асаагаарай.", "The server runs an old version. Please restart it."));
                chat.Clear(); foreach (var c in m["chat"]) chat.Add(ChatOf(c)); if (chat.Count > CHAT_KEEP) chat.RemoveRange(0, chat.Count - CHAT_KEEP);
                // the server wallet, with this device's not-yet-uploaded progress kept on top; an upload still on its way settles it instead
                walletRev = -1; syncing = false; serverDay = m["day"].S ?? SaveData.DayKey();
                if (g.V != null && string.IsNullOrEmpty(g.V.syncOp)) ApplyWallet(g, m["wallet"]);
                ApplyInventory(g, m["inventory"]);
                if (m["look"] != null && !m["look"].IsNull && g.V != null)
                {
                    try { var l = LookOf(m["look"]); if (!string.IsNullOrEmpty(l.deel)) g.V.look = l; } catch (Exception) { }
                }
                if (m["position"] != null && !m["position"].IsNull && g.V != null)
                {
                    try { float pxv = m["position"]["x"].F; float pyv = m["position"]["y"].F; if (pxv >= 0.5f && pxv <= Rules.W - 2 && pyv >= 0.5f && pyv <= Rules.H - 2) { g.V.posX = pxv; g.V.posY = pyv; g.V.hasPos = true; } } catch (Exception) { }
                }
                g.GerSync(); // my ger, started while playing alone, comes along to a free plot
                Sync(g);
                cloudSent = ""; SaveCloud(g); // the server merges this device's lessons, badges… and sends back the whole copy
                g.ArenaHello(); // the arena account (and a bout this player is still in)
                return;
            case "cloud":
                if (g.V == null) return;
                if (Cloud.Apply(g.V, m["cloud"])) g.V.Save(); // cloudSent stays what was sent: anything newer goes up next time
                return;
            case "join": AddOther(m["p"]); AddChat(g, new ChatLine { k = "join", id = m["p"]["id"].S, n = m["p"]["name"].S }); g.Toast(m["p"]["name"].S + Loc.L(" нутагт ирлээ", " joined the village")); return;
            case "leave": if (others.TryGetValue(m["id"].S, out var gone)) AddChat(g, new ChatLine { k = "leave", id = gone.id, n = gone.name }); others.Remove(m["id"].S); return;
            case "move": if (others.TryGetValue(m["id"].S, out var om)) { om.x = m["x"].F; om.y = m["y"].F; om.dir = m["dir"].I; } return;
            case "jump": if (others.TryGetValue(m["id"].S, out var oj)) oj.jt = Time.time; return;
            case "look": if (others.TryGetValue(m["id"].S, out var ol)) { ol.look = LookOf(m["look"]); ol.lvl = m["lvl"].I; } return;
            case "say":
            {
                int i = m["i"].I; bool emote = m["k"].S == "e"; string id = m["id"].S;
                string text = emote ? null : i >= 0 && i < PHRASES.Length ? PHRASES[i].ToString() : ""; PxCanvas icon = emote && i >= 0 && i < EMOTES.Length ? Emo.Icon(EMOTES[i]) : null;
                if (id == g.V.id) { g.MyBubble(text, icon); AddChat(g, new ChatLine { k = emote ? "e" : "p", id = id, n = g.V.name, i = i }); }
                else if (others.TryGetValue(id, out var os)) { os.bubble = text; os.bubbleIcon = icon; os.bubbleT = Time.time; AddChat(g, new ChatLine { k = emote ? "e" : "p", id = id, n = os.name, i = i }); }
                return;
            }
            case "census": total = m["total"].I; nutag = m["nutag"].I; return;
            case "lot":
            {
                int i = m["i"].I; var l = LotOf(m["lot"]); if (lots != null && i >= 0 && i < lots.Length) lots[i] = l;
                if (l != null && l.by != g.V.id && m["act"].S == "claim") g.Toast(l.n + Loc.L(" гэр барьж эхэллээ!", " started building a ger!"));
                else if (l != null && l.by != g.V.id && m["act"].S == "stage" && l.stage >= GerRules.DONE) g.Toast(l.n + Loc.L(" гэрээ барьж дууслаа!", " finished building their ger!"));
                return;
            }
            case "deny": g.Toast(m["why"].S == "own" ? Loc.L("Танд аль хэдийн гэр байна!", "You already have a ger!") : m["why"].S == "taken" ? Loc.L("Энэ газрыг өөр хүн авчихсан байна.", "Someone else already took this lot.") : m["why"].S == "busy" ? Loc.L("Тэр одоо завгүй байна.", "They are busy right now.") : Loc.L("Болохгүй байна.", "Not possible.")); return;
            case "replaced": replaced = true; closed = true; status = "solo"; others.Clear(); g.Toast(Loc.L("Энэ профайл өөр төхөөрөмж дээр нээгдлээ. Тэнд үргэлжлүүлэн тоглоорой.", "This profile was opened on another device. Keep playing there.")); return;
            case "move-correct":
                if (g.V != null)
                {
                    try { float cx = m["x"].F; float cy = m["y"].F; int cdir = m["dir"].I; g.CorrectMove(cx, cy, cdir); /* Game uses px/py/dir fields */ }
                    catch (Exception) { }
                }
                return;
            case "wallet-result":
            case "inventory-result":
                ApplyWallet(g, m["wallet"]); ApplyInventory(g, m["inventory"]); // e.g. a live wrestling prize
                return;
            case "sync-result":
            {
                var V = g.V; if (V == null) return;
                if (string.IsNullOrEmpty(V.syncOp) || m["op"].S != V.syncOp) { ApplyWallet(g, m["wallet"]); ApplyInventory(g, m["inventory"]); return; }
                syncing = false;
                if (m["accepted"].B)
                {
                    int ac = m["applied"]["coins"].I, ax = m["applied"]["xp"].I, sc = V.syncCoins, sx = V.syncXp;
                    V.syncOp = ""; ApplyWallet(g, m["wallet"], ac, ax); ApplyInventory(g, m["inventory"]);
                    if (sc > ac || sx > ax)
                    {
                        if (capDay != serverDay) g.Toast(Loc.L("Өнөөдрийн шагналын дээд хэмжээнд хүрлээ. Үлдсэн нь энэ төхөөрөмж дээр хадгалагдаж, маргааш серверт орно.", "Today’s reward limit is reached. The rest is kept on this device and uploaded tomorrow."));
                        capDay = serverDay;
                    }
                    else if (V.coins != V.srvCoins || V.xp > V.srvXp) QueueSync(); // earned while the upload was on its way
                }
                else if (m["reason"].S == "invalid") { V.syncOp = ""; V.Save(); }
                return;
            }
            case "wr-invite": case "wr-declined": case "wr-start": case "wr-wait": case "wr-result": case "wr-end": g.OnWrestleMsg(m); return;
            case "board":
            {
                string k = m["k"].S; if (k != "room" && k != "family") return;
                var b = new Board { reason = m["reason"].S, at = Time.realtimeSinceStartup };
                foreach (var r in m["rows"]) b.rows.Add(new BoardRow { rank = r["rank"].I, name = (r["name"].S ?? "").Replace("<", "").Replace(">", ""), xp = r["xp"].I, look = r.Has("look") ? LookOf(r["look"]) : null, avatar = r["avatar"].I, me = r["me"].B });
                boards[k] = b; return;
            }
        }
    }
    /** Ask for a board, unless a fresh one is here (the server keeps it 30 s anyway) or the last ask is still on its way. */
    public void AskBoard(string k)
    {
        float now = Time.realtimeSinceStartup; if (!Online) return;
        bool has = boards.TryGetValue(k, out var b), asked = boardAsked.TryGetValue(k, out var a);
        if (has && (b.reason == null || b.reason == "no-family") && now - b.at < 15) return;
        if (asked && (!has || a > b.at) && now - a < BOARD_WAIT) return;
        boardAsked[k] = now; Send("{" + Obj("t", "board", "k", k) + "}");
    }
    public void Say(bool emote, int i) => Send("{" + Obj("t", "say", "k", emote ? "e" : "p") + "," + Raw("i", i.ToString()) + "}");
    public void Jumped() => Send("{" + Obj("t", "jump") + "}");
    /** Laying the floor claims a plot; a ger already part-way up brings its stage along. */
    public void GerClaim(int i, string roof, string door, int stage) => Send("{" + Obj("t", "ger-claim") + "," + Raw("i", i.ToString()) + "," + Obj("roof", roof ?? "blue", "door", door ?? "red") + "," + Raw("stage", stage.ToString()) + "}");
    public void GerStage(int stage) => Send("{" + Obj("t", "ger-stage") + "," + Raw("stage", stage.ToString()) + "}");
    public void StyleGer(string roof, string door, List<string> furn) => Send("{" + Obj("t", "ger-style", "roof", roof, "door", door) + "," + Raw("furn", "[" + string.Join(",", (furn ?? new List<string>()).Select(Q)) + "]") + "}");
    public void Wr(string t, string key, string val) => Send("{" + Obj("t", t, key, val) + "}");
    public void WrMove(string move, float p) => Send("{" + Obj("t", "wr-move", "m", move) + "," + Raw("p", F2(p)) + "}");

    // ---------------------------------------------------------------- uploading progress (like village.js's net.sync)
    // Rewards and spending happen on the device. What it has on top of the server wallet it last saw is uploaded; the server adds
    // it within a daily cap, and what goes over stays here and goes up on a later day.
    static string NewOp()
    {
        var bytes = new byte[16];
        using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create()) rng.GetBytes(bytes);
        var sb = new StringBuilder(32); foreach (var b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
    /** A save from before uploads existed starts from the server's starter account, so everything it earned goes up. */
    static void Base(SaveData V)
    {
        if (V.hasSrv) return;
        V.hasSrv = true; V.srvCoins = 40; V.srvXp = 0; V.srvRod = 1; V.srvOwned = new List<string> { "deel-blue" };
    }
    public void QueueSync() => syncAt = Time.realtimeSinceStartup + 0.8f;
    void Sync(Game g)
    {
        var V = g.V; if (!Online || V == null || syncing) return;
        string fields = SyncFields(V); if (fields == null) return;
        syncing = true; syncSentAt = Time.realtimeSinceStartup;
        Send("{" + Obj("t", "sync") + "," + fields + "}");
    }
    /** What this device earned since the server's wallet, as the fields of a `sync` (the same op until it is answered); null when nothing. */
    string SyncFields(SaveData V)
    {
        Base(V);
        if (string.IsNullOrEmpty(V.syncOp))
        {
            bool capped = capDay == serverDay; // over today's cap: coins and XP wait for tomorrow
            int coins = V.coins - V.srvCoins, xp = Math.Max(0, V.xp - V.srvXp);
            if (capped) { coins = Math.Min(0, coins); xp = 0; }
            bool items = V.owned.Exists(o => !V.srvOwned.Contains(o)) || V.rod > V.srvRod;
            if (coins == 0 && xp == 0 && !items) return null;
            V.syncOp = NewOp(); V.syncCoins = coins; V.syncXp = xp; V.Save();
        }
        return Obj("op", V.syncOp) + "," + Raw("coins", V.syncCoins.ToString()) + "," + Raw("xp", V.syncXp.ToString())
            + "," + Raw("owned", "[" + string.Join(",", V.owned.Select(Q)) + "]") + "," + Raw("rod", V.rod.ToString());
    }
    /** Playing alone opens no socket: every 30 seconds (and on log out) the same upload goes to POST /api/save, and its answers are read as if
     *  they came over the socket. Nothing goes when offline; the next try sends the same op again. */
    float saveT = 25; bool saving;
    public void SaveAlone(Game g)
    {
        var V = g.V; if (V == null || string.IsNullOrEmpty(V.email) || saving) return;
        string server = Family.ServerOf(g, V); if (string.IsNullOrEmpty(server)) return;
        SaveData.EnsureTicket(V);
        if (string.IsNullOrEmpty(serverDay)) serverDay = SaveData.DayKey(); // capDay and serverDay both empty would read as capped
        string fields = SyncFields(V), cloud = Cloud.Of(V); bool cloudNew = cloud != cloudSent;
        if (fields == null && !cloudNew) return;
        string body = "{" + Obj("id", V.id, "ticket", V.ticket) + (fields != null ? "," + fields : "") + (cloudNew ? "," + Raw("cloud", cloud) : "") + "}";
        saving = true; if (fields != null) { syncing = true; syncSentAt = Time.realtimeSinceStartup; }
        g.StartCoroutine(ArenaApi.Post(server, body, r =>
        {
            saving = false; syncing = false;
            if (!r["ok"].B) return;
            if (cloudNew) cloudSent = cloud;
            if (!string.IsNullOrEmpty(r["day"].S)) serverDay = r["day"].S;
            foreach (var m in r["out"]) Handle(g, m);
        }, "/api/save"));
    }
    /** Take a server wallet while keeping what this device has not uploaded yet. `ac`/`ax`: what an upload just added. An older wallet than one
     *  already seen only takes that off. */
    void ApplyWallet(Game g, J w, int ac = 0, int ax = 0)
    {
        var V = g.V; if (V == null || w.IsNull || !w.Has("coins") || !w.Has("xp") || !w.Has("revision")) return;
        int c = w["coins"].I, x = w["xp"].I, rev = w["revision"].I; if (c < 0 || x < 0 || rev < 0) return;
        Base(V);
        if (walletRev >= 0 && rev < walletRev) { V.coins = Math.Max(0, V.coins - ac); V.xp = Math.Max(0, V.xp - ax); }
        else { V.coins = Math.Max(0, c + V.coins - V.srvCoins - ac); V.xp = Math.Max(0, x + V.xp - V.srvXp - ax); V.srvCoins = c; V.srvXp = x; walletRev = rev; }
        V.Save();
    }
    /** Things bought on this device stay; the server's list is what it already has. */
    void ApplyInventory(Game g, J inv)
    {
        var V = g.V; if (V == null || inv.IsNull || !inv["owned"].IsList) return;
        Base(V);
        V.srvOwned = inv["owned"].Strings().Where(o => !string.IsNullOrEmpty(o)).ToList(); V.srvRod = inv["rod"].I == 2 ? 2 : 1;
        foreach (var o in V.srvOwned) if (!V.owned.Contains(o)) V.owned.Add(o);
        if (!V.owned.Contains("deel-blue")) V.owned.Insert(0, "deel-blue");
        V.rod = Math.Max(V.rod, V.srvRod);
        V.Save();
    }
}

public sealed partial class Game
{
    public readonly Net net = new Net();
    public float MyX => px; public float MyY => py; public int MyDir => dir; public bool MyRiding => riding;
    public void CorrectMove(float x, float y, int d) { px = x; py = y; dir = d; vx = vy = 0; SavePos(); } // the server put me back where it says I am
    (string text, PxCanvas icon, float t) myBubble = (null, null, -99);
    public void MyBubble(string text, PxCanvas icon) => myBubble = (text, icon, Time.time);
    /** A lot someone else owns on the server (their ger stands there). */
    Net.Lot LotOf(int i) => net.Online && net.lots != null && i >= 0 && i < net.lots.Length && net.lots[i] != null && net.lots[i].by != V.id ? net.lots[i] : null;

    void NetDraw()
    {
        float t = Time.time;
        if (net.lots != null) for (int i = 0; i < net.lots.Length; i++) { var l = LotOf(i); if (l == null) continue; var o = World.objects.Find(q => q.kind == "lot" && q.i == i); if (o != null && !IsoOn) Spr(Px.GerStage(l.stage > 0 ? l.stage : GerRules.DONE, Px.ROOF.TryGetValue(l.roof, out var r) ? r : Px.ROOF["blue"], Px.DOOR.TryGetValue(l.door, out var d) ? d : Px.DOOR["red"]), o.x * T, o.y * T - 2, false, o.y + 2); }
        foreach (var o in net.others.Values)
        {
            float jq = (t - o.jt) / JUMP_S, z = jq >= 0 && jq < 1 ? Mathf.Round(Mathf.Sin(jq * Mathf.PI) * 9) : 0; var look = o.look ?? new Look();
            if (look.ride == "horse") Rider(look, o.dir == 1 ? 1 : 2, o.moving, o.px, o.py, o.py, z, false, null, "net" + o.name);
            else { Shadow(o.px * T, o.py * T, o.py); var lk = look.Copy(); lk.pet = null; Person(lk, o.dir, z != 0 ? 0 : Frame(o.moving, false), o.px, o.py, o.py, z, null, o.moving); }
        }
    }
    /** Where a ger's title goes: over its roof (in the isometric view the ger stands up from its lot), or above the lot when flat. */
    Vector2 GerTop(WObj o) => IsoOn ? WorldGui(o.x + 1.5f, o.y + 2.7f, 3f) : PxGui(o.x * T + 24, o.y * T - 4);
    void NetLabels(float ts)
    {
        float t = Time.time; Color gold = Px.Col("#f7c646");
        if (net.lots != null) for (int i = 0; i < net.lots.Length; i++) { var l = LotOf(i); if (l == null) continue; var o = World.objects.Find(q => q.kind == "lot" && q.i == i); if (o == null) continue; var p = GerTop(o); Outlined(p.x, p.y, l.n ?? "", ts * 0.85f, Color.white); }
        foreach (var o in net.others.Values)
        {
            var p = PxGui(o.px * T + 8, o.py * T - (o.look?.ride == "horse" ? 30 : 16)); Outlined(p.x, p.y, o.name + "  " + Loc.L("Т", "Lv") + o.lvl, ts * 0.8f, Color.white);
            if (t - o.bubbleT < 4) Bubble(p.x, p.y - ts * 2.2f, o.bubble, o.bubbleIcon);
        }
        if (t - myBubble.t < 4) { var p = PxGui(px * T + 8, py * T - (riding ? 44 : 30)); Bubble(p.x, p.y, myBubble.text, myBubble.icon); }
    }
    void Bubble(float x, float y, string text, PxCanvas icon)
    {
        if (icon == null) { SpeechBubble(new Vector2(x, y), "<b>" + text + "</b>", 200); return; }   // words: a bubble that fits them
        float w = U(34), h = U(30);
        var r = new Rect(x - w / 2, y - h, w, h); Box(new Rect(r.x - 2, r.y - 2, r.width + 4, r.height + 4), Px.Col("#2b1e2e")); Box(r, Color.white);
        GUI.DrawTexture(new Rect(r.center.x - U(11), r.y + U(4), U(22), U(22)), Tex(icon));
    }

    // ---------------------------------------------------------------- the room window: who's here, phrases and emotes, a wrestling challenge
    public sealed class RoomModal : Modal
    {
        string code;
        public override void Draw(Game g)
        {
            var n = g.net; code ??= "";
            g.Title(Loc.L("Хамт тоглох өрөө", "Play-together room"));
            if (string.IsNullOrEmpty(Family.ServerOf(g, g.V))) { g.Text(Loc.L("Найзуудтайгаа хамт тоглохын тулд том хүнээс ☰ → Тохиргоо хэсэгт серверийн хаягийг бичүүлээрэй.", "To play with friends, ask a grown-up to add the server address in ☰ → Settings.")); if (g.Btn(Loc.L("Тохиргоо", "Settings"), true, 40)) g.Later(() => g.Swap(new SettingsModal())); }
            if (!string.IsNullOrEmpty(Family.ServerOf(g, g.V)))
            {
                if (n.Online) GUILayout.Label(n.room, new GUIStyle(g.sBig) { alignment = TextAnchor.MiddleCenter });
                g.Small(n.Online ? Loc.L($"Энэ өрөөнд {n.others.Count + 1} хүн байна. Энэ кодыг найздаа өгвөл тэд орж ирнэ.", $"{n.others.Count + 1} people in this room. Give this code to your friends so they can come in.")
                    : n.status == "solo" ? n.failWhy ?? Loc.L("Сервертэй холбогдоогүй байна.", "Not connected to the server.") : Loc.L("Холбогдож байна…", "Connecting…"));
                GUILayout.BeginHorizontal();
                code = GUILayout.TextField(code ?? "", 6, new GUIStyle(g.sBtn) { alignment = TextAnchor.MiddleCenter }, GUILayout.MinHeight(g.U(40))).ToUpperInvariant();
                if (GUILayout.Button(Loc.L("Нэгдэх", "Join"), g.sPrimary, GUILayout.Width(g.U(100)), GUILayout.MinHeight(g.U(40)))) g.Later(() =>
                {
                    string c = (code ?? "").Trim().ToUpperInvariant(); if (c.Length != 6 || !c.All(ch => ch >= 'A' && ch <= 'Z')) { g.Toast(Loc.L("Код 6 латин үсэгтэй байна.", "The code has 6 letters.")); return; }
                    code = ""; n.JoinRoom(g, c);
                });
                GUILayout.EndHorizontal();
                if (n.status == "online" || n.status == "solo") { if (g.Btn(Loc.L("Шинэ өрөө нээх", "Open a new room"), false, 34)) g.Later(() => n.NewRoom(g)); }
                if (n.status == "solo" && g.Btn(Loc.L("Дахин холбогдох", "Connect again"), false, 34)) g.Later(() => n.Connect(g));
                if (n.Online && g.Btn(Loc.L("Шилдэгүүд", "Leaderboard"), false, 34)) g.Later(() => g.Swap(new BoardModal()));
            }
            n.chatOpen = true; n.unread = 0;
            if (n.Online) // the room's recent chat sits on top of the phrases, so reading and answering is one window
            {
                GUILayout.BeginVertical(g.sBtn);
                if (n.chat.Count == 0) g.Small(Loc.L("Одоохондоо мессеж алга.", "No messages yet."));
                foreach (var l in n.chat.Skip(Math.Max(0, n.chat.Count - 8)))
                {
                    GUILayout.BeginHorizontal(); string txt = Net.ChatText(l);
                    GUILayout.Label("<b>" + l.n + "</b> " + (l.k == "join" || l.k == "leave" ? "<color=#9a8a7a>" + txt + "</color>" : txt ?? ""), g.sLabel);
                    if (l.k == "e" && l.i >= 0 && l.i < Net.EMOTES.Length) { var ic = Emo.Icon(Net.EMOTES[l.i]); if (ic != null) g.IconAt(ic, 22); }
                    GUILayout.FlexibleSpace(); GUILayout.EndHorizontal();
                }
                GUILayout.EndVertical();
            }
            GUILayout.Label("<b>" + Loc.L("Хэлэх", "Say") + "</b>", g.sLabel);
            for (int row = 0; row < 2; row++)
            {
                GUILayout.BeginHorizontal();
                for (int i = row * 4; i < row * 4 + 4; i++) { if (GUILayout.Button(Net.PHRASES[i].ToString(), g.sBtn, GUILayout.MinHeight(g.U(34)))) { int k = i; g.Later(() => { if (n.Online) n.Say(false, k); else g.MyBubble(Net.PHRASES[k].ToString(), null); }); } }
                GUILayout.EndHorizontal();
            }
            GUILayout.BeginHorizontal();
            for (int i = 0; i < Net.EMOTES.Length; i++) { var r = GUILayoutUtility.GetRect(g.U(40), g.U(40), GUILayout.Width(g.U(40))); if (GUI.Button(r, "", g.sBtn)) { int k = i; g.Later(() => { if (n.Online) n.Say(true, k); else g.MyBubble(null, Emo.Icon(Net.EMOTES[k])); }); } var ic = Emo.Icon(Net.EMOTES[i]); if (ic != null) GUI.DrawTexture(new Rect(r.x + 6, r.y + 6, r.width - 12, r.height - 12), g.Tex(ic)); }
            GUILayout.EndHorizontal();
            if (!n.Online) return;
            GUILayout.Label("<b>" + Loc.L("Энд байгаа найзууд", "Friends here") + "</b>", g.sLabel);
            if (n.others.Count == 0) g.Small(Loc.L("Одоохондоо ганцаараа байна. Найздаа өрөөний кодоо хэлээрэй!", "Nobody else yet. Tell your friends the room code!"));
            foreach (var o in n.others.Values.ToList())
            {
                g.Row(Px.Char(o.look ?? new Look(), 0, 0), o.name, Loc.L("Түвшин ", "Level ") + o.lvl);
                if (g.SmallBtn(Loc.L("Барилдах", "Wrestle"), false, 100)) { var id = o.id; g.Later(() => { n.Wr("wr-invite", "to", id); g.Toast(Loc.L("Урилга илгээлээ!", "Challenge sent!")); }); }
                g.EndRow();
            }
        }
        public override void Press(Game g) { }
        public override void Closed(Game g) { g.net.chatOpen = false; }
    }

    // ---------------------------------------------------------------- who has the most XP: in this room, or in my family
    public sealed class BoardModal : Modal
    {
        static readonly string[] MEDAL = { "#f7c646", "#c9ccd3", "#d08a4e" };
        string k = "room"; bool asked; float now = Time.realtimeSinceStartup; // `now` is set in Tick, so Layout and Repaint of one frame agree
        GUIStyle sRank, sXp, sFor;
        public override void Tick(Game g, float dt) { var n = g.net; now = Time.realtimeSinceStartup; if (!n.Online) asked = false; else if (!asked) { asked = true; n.AskBoard(k); } } // again after a reconnect
        public override void Draw(Game g)
        {
            var n = g.net;
            if (sFor != g.sLabel) { sFor = g.sLabel; sRank = new GUIStyle(g.sLabel) { alignment = TextAnchor.MiddleCenter, wordWrap = false }; sXp = new GUIStyle(g.sLabel) { wordWrap = false }; }
            g.Title(Loc.L("Шилдэгүүд", "Leaderboard"));
            int hit = g.Chips(new[] { Loc.L("Өрөө", "Room"), Loc.L("Гэр бүл", "Family") }, k == "room" ? 0 : 1, 100, true);
            if (hit >= 0 && (hit == 0) != (k == "room")) { string nk = hit == 0 ? "room" : "family"; g.Later(() => { k = nk; asked = false; }); }
            if (!n.Online)
            {
                g.Fact(Loc.L("Шилдэгүүдийг харахын тулд сервертэй холбогдож, найзуудтайгаа хамт тоглох хэрэгтэй.", "To see the leaderboard, connect to the server and play together with friends."), true, Loc.L("Холбогдоогүй байна", "Not connected"));
                if (string.IsNullOrEmpty(Family.ServerOf(g, g.V))) { if (g.Btn(Loc.L("Тохиргоо", "Settings"), true, 40)) g.Later(() => g.Swap(new SettingsModal())); }
                else if (n.status == "solo") { if (g.Btn(Loc.L("Олуулаа тоглох", "Play online"), true, 40)) g.Later(() => g.Swap(new ModeModal())); }
                else g.Small(Loc.L("Холбогдож байна…", "Connecting…"));
            }
            else
            {
                bool has = n.boards.TryGetValue(k, out var b), sent = n.boardAsked.TryGetValue(k, out var a), waiting = sent && (!has || a > b.at);
                if (waiting && now - a < Net.BOARD_WAIT || !has && !sent) g.Text(Loc.L("Ачаалж байна…", "Loading…"));
                else if (!has) { g.Fact(Loc.L("Шилдэгүүд энэ серверт одоохондоо алга. Удахгүй нэмэгдэнэ!", "The leaderboard is not available on this server yet. Coming soon!"), false); if (g.Btn(Loc.L("Дахин оролдох", "Try again"), false, 34)) g.Later(() => n.AskBoard(k)); }
                else if (b.reason == "no-family")
                {
                    g.Fact(Loc.L("Чи одоохондоо гэр бүлд нэгдээгүй байна. Том хүнээс гэр бүл үүсгээд чамайг нэмэхийг хүсээрэй, тэгвэл ах дүүстэйгээ уралдана!", "You are not in a family yet. Ask a grown-up to make a family and add you, then you can race your brothers and sisters!"), false);
                    if (g.Btn(Loc.L("Гэр бүл (том хүн)", "Family (grown-ups)"), true, 40)) g.Later(() => { g.Close(); g.OpenFamily(); });
                }
                else if (b.reason != null) { g.Fact(Loc.L("Шилдэгүүдийг ачаалж чадсангүй. Жаахан хүлээгээд дахин оролдоорой.", "Could not load the leaderboard. Wait a little and try again."), false); if (g.Btn(Loc.L("Дахин оролдох", "Try again"), false, 34)) g.Later(() => n.AskBoard(k)); }
                else if (b.rows.Count == 0) g.Text(k == "room" ? Loc.L("Энэ өрөөнд одоохондоо хэн ч алга. Тоглож XP цуглуулаад анхных нь болоорой!", "Nobody here yet. Play, earn XP and be the first!") : Loc.L("Гэр бүлд одоохондоо хэн ч алга.", "Nobody in the family yet."));
                else
                {
                    if (waiting) g.Small(Loc.L("Шинэчилж чадсангүй. Өмнөх жагсаалт:", "Could not refresh. The last list:"));
                    for (int i = 0; i < b.rows.Count; i++)
                    {
                        var r = b.rows[i]; if (i > 0 && r.me && r.rank > b.rows[i - 1].rank + 1) g.Small("…"); // a gap before my own row, under the top 10
                        GUILayout.BeginHorizontal(r.me ? g.sSel : g.sBtn);
                        var mr = GUILayoutUtility.GetRect(g.U(34), g.U(34), GUILayout.Width(g.U(34)), GUILayout.Height(g.U(34)));
                        if (r.rank >= 1 && r.rank <= 3) { var old = GUI.color; GUI.color = Px.Col(MEDAL[r.rank - 1]); GUI.DrawTexture(mr, g.Tex(Disc())); GUI.color = old; }
                        GUI.Label(mr, "<b>" + r.rank + "</b>", sRank);
                        var ic = r.look != null ? Px.Char(r.look, 0, 0) : Px.Mascot(r.avatar); if (ic != null) g.IconAt(ic, 32);
                        GUILayout.Label("<b>" + r.name + "</b>" + (r.me ? " · " + Loc.L("Би", "Me") : ""), g.sLabel);
                        GUILayout.FlexibleSpace(); GUILayout.Label("<b>" + r.xp + "</b> XP", sXp);
                        GUILayout.EndHorizontal();
                    }
                }
            }
            if (g.Btn(Loc.L("Хаах", "Close"), false, 30)) g.Later(g.Close);
        }
        public override void Press(Game g) => g.Close();
    }

    // ---------------------------------------------------------------- live wrestling with a friend: the Naadam stadium bout (GameWrestle.cs), the server decides each round
    WrestleModal remote;
    public void OnWrestleMsg(J m)
    {
        string t = m["t"].S;
        if (t == "wr-invite") { string from = m["from"].S, name = m["name"].S; Open(new ConfirmModal(Loc.L("Барилдах урилга!", "Wrestling challenge!"), name + Loc.L(" тантай барилдахыг хүсэж байна.", " wants to wrestle you."), () => net.Wr("wr-accept", "from", from)) { onNo = () => net.Wr("wr-decline", "from", from) }); Sfx.Chime(); }
        else if (t == "wr-declined") Toast(m["name"].S + Loc.L(" одоохондоо барилдахгүй гэлээ.", " declined for now."));
        else if (t == "wr-start")
        {
            bool a = m["a"]["id"].S == V.id; var opp = a ? m["b"] : m["a"]; var ol = opp["look"];
            var oppLook = ol.IsNull ? new Look() : new Look { deel = ol["deel"].S ?? "blue", skin = ol["skin"].S ?? "#D9A06E", title = ol["title"].S };
            remote = null; CloseAll();   // remote is cleared first, so closing an older bout does not quit this new one
            remote = WrestleModal.Pvp(this, m["id"].S, a ? "a" : "b", opp["name"].S, oppLook);
            Open(remote);
        }
        else remote?.OnNet(this, m);
    }
    public void EndRemoteMatch() { remote?.OnNet(this, J.Parse("{\"t\":\"wr-end\",\"winner\":null,\"reason\":\"disconnected\"}")); }
}
