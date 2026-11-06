// Family login: the family code a grown-up makes, which lets a child log in on another device with
// their 3 secret pictures and play as the same child there (same coins, XP and clothes). See docs/PROTOCOL.md, "Family login".
using System;
using System.Collections;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine.Networking;

public static class Family
{
#if UNITY_WEBGL && !UNITY_EDITOR
    public const string SCHEME = "https://"; // a web page served over https may only call https
#else
    public const string SCHEME = "http://";  // node server.js on the local Wi-Fi
#endif
    static string Url(string server) { string s = (server ?? "").Trim().TrimEnd('/'); if (s == "") return null; if (!s.StartsWith("http")) s = SCHEME + s; return s + "/api/family"; }
    static J Fail(string reason) => J.Parse("{\"ok\":false,\"reason\":" + J.Quote(reason) + "}");
    /** The grown-up PIN as typed at the parent gate or the PIN pad: in memory only, never saved (the device keeps its hash). */
    public static string pin = "";
    static string PinField(string key, string v) => string.IsNullOrEmpty(v) ? "" : ",\"" + key + "\":" + J.Quote(v);
    /** A child of this device's family that this device plays as (its ticket speaks for the grown-up), or null. */
    public static SaveData Member(Game g)
    {
        string code = g.App.familyCode ?? ""; if (code == "") return null;
        SaveData Kid(string pid) => g.V != null && pid == g.V.id ? g.V : SaveData.Load(pid);
        return g.App.profiles.Select(Kid).FirstOrDefault(p => p.fam == code && !string.IsNullOrEmpty(p.ticket));
    }

    /** POST /api/family. done always gets an object: the server's answer, or { ok: false, reason: "offline" | "no-server" }. */
    public static IEnumerator Post(string server, string body, Action<J> done)
    {
        string url = Url(server);
        if (url == null) { done(Fail("no-server")); yield break; }
        using (var r = new UnityWebRequest(url, "POST"))
        {
            r.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)); r.downloadHandler = new DownloadHandlerBuffer(); r.SetRequestHeader("content-type", "application/json"); r.timeout = 10;
            yield return r.SendWebRequest();
            // a refused request (429, 400) still answers with JSON that says why
            J answer = null; string text = r.downloadHandler != null ? r.downloadHandler.text : "";
            if (!string.IsNullOrEmpty(text) && text.TrimStart().StartsWith("{")) { try { answer = J.Parse(text); } catch (Exception) { answer = null; } }
            done(answer != null && answer.IsObject ? answer : Fail("offline"));
        }
    }

    /** "abcd-2345" → "ABCD2345". */
    public static string Clean(string typed) => new string((typed ?? "").ToUpperInvariant().Where(c => (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')).ToArray());
    public static string Show(string code) => code.Length == 8 ? code.Substring(0, 4) + "-" + code.Substring(4) : code;
    /** A picture password as saved ("0-4-2") → the server's "[0,4,2]", or null. */
    public static string PicsJson(string code)
    {
        var parts = (code ?? "").Split('-');
        if (parts.Length != 3 || parts.Any(p => !int.TryParse(p, out int i) || i < 0 || i > 8)) return null;
        return "[" + string.Join(",", parts.Select(int.Parse)) + "]";
    }
    /** The hosted Naadamy server (Render), built into the app so nobody has to type an address. Empty until it is deployed:
     *  then set it, e.g. "https://naadamy-team8.onrender.com". An address in Settings still wins (a server on the home Wi-Fi). */
    public const string HOSTED = "https://pinequest-s5-episode-2-team-8.onrender.com";
    /** Where this profile reaches the server: its own address, the one this device last used, or the hosted one. "": play alone. */
    public static string ServerOf(Game g, SaveData p)
    {
        if (p != null && IsAddress(p.server)) return p.server; // a code typed into the box by mistake ("m8qhg") is no address: skip it
        return IsAddress(g.App.server) ? g.App.server : HOSTED;
    }
    /** Looks like a server address: localhost, a name or IP with a dot, or anything with a :port (an optional http(s)/ws(s):// in front). */
    public static bool IsAddress(string s)
    {
        s = (s ?? "").Trim(); if (s == "") return false;
        int k = s.IndexOf("://"); if (k >= 0) { string sch = s.Substring(0, k).ToLowerInvariant(); if (sch != "http" && sch != "https" && sch != "ws" && sch != "wss") return false; s = s.Substring(k + 3); }
        int sl = s.IndexOf('/'); string host = sl >= 0 ? s.Substring(0, sl) : s;
        if (host == "" || !host.All(c => char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == ':')) return false;
        return host.StartsWith("localhost") || host.Contains('.') || host.Contains(':');
    }

    public static string Why(J r)
    {
        switch (r["reason"].S)
        {
            case "no-server": return Loc.L("Эхлээд серверийн хаягаа Тохиргоонд бичээрэй.", "Write the server address in Settings first.");
            case "offline": return Loc.L("Сервертэй холбогдож чадсангүй. Хаяг, интернэтээ шалгаарай.", "Could not reach the server. Check the address and the internet.");
            case "no-family": return Loc.L("Ийм гэр бүлийн код олдсонгүй.", "No family has this code.");
            case "full": return Loc.L("Нэг гэр бүлд 8 хүртэл хүүхэд байна.", "A family can have up to 8 children.");
            case "wrong": return Loc.L($"Зураг буруу байна. {r["left"].I} оролдлого үлдлээ.", $"Wrong pictures. {r["left"].I} tries left.");
            case "locked": { int min = Math.Max(1, (r["wait"].I + 59) / 60); return Loc.L($"Олон удаа буруу дарлаа. {min} минутын дараа дахин оролдоорой.", $"Too many wrong tries. Try again in {min} minutes."); }
            case "busy": return Loc.L("Хэт олон оролдлого. Түр хүлээгээрэй.", "Too many tries. Wait a moment.");
            case "no-member": return Loc.L("Энэ хүүхэд гэр бүлд байхгүй байна.", "This child is not in the family.");
            case "erased": return Loc.L("Энэ хүүхдийн гэр бүлийг серверээс устгасан байна.", "This child's family was deleted from the server.");
            case "identity": return Loc.L("Энэ профайлыг сервер танисангүй.", "The server did not recognise this profile.");
            case "pin": return Loc.L($"Том хүний ПИН код таарсангүй. {r["left"].I} оролдлого үлдлээ.", $"That grown-up PIN didn't match. {r["left"].I} tries left.");
            case "recovery": return Loc.L($"Сэргээх код таарсангүй. {r["left"].I} оролдлого үлдлээ.", $"That recovery code didn't match. {r["left"].I} tries left.");
            case "pin-locked": return Loc.L($"Олон удаа буруу орууллаа. {Wait(r["wait"].I)} дараа дахин оролдоорой.", $"Too many wrong tries. Please try again in {Wait(r["wait"].I)}.");
            case "needs-pin": return Loc.L("Эхлээд гэр бүлдээ том хүний ПИН код тавиарай.", "Please set a grown-up PIN for the family first.");
            case "not-first": return Loc.L("Анхны ПИН кодыг гэр бүлд хамгийн түрүүнд нэмэгдсэн хүүхдийн төхөөрөмж дээр тавина.", "The first PIN is set on the device of the child who joined the family first.");
            case "no-recovery": return Loc.L("Энэ гэр бүлд сэргээх код алга.", "This family has no recovery code.");
            case "too-soon": { int d = Math.Max(1, (r["wait"].I + 3599) / 3600); return Loc.L($"ПИН код саяхан тавигдсан тул гэр бүлийг {d} цагийн дараа устгаж болно. Энэ хооронд бусад том хүн үүнийг анзаарах боломжтой.", $"The PIN was set only recently, so the family can be deleted in {d} h. Until then, the other grown-ups have time to notice."); }
            case "join-locked": return Loc.L($"Нэгдэх гэж олон удаа буруу ПИН код орууллаа. {Wait(r["wait"].I)} дараа дахин оролдоорой.", $"Too many wrong PINs when joining. Please try again in {Wait(r["wait"].I)}.");
            case "recovery-locked": return Loc.L($"Сэргээх код олон удаа буруу орууллаа. {Wait(r["wait"].I)} дараа дахин оролдоорой.", $"Too many wrong recovery codes. Please try again in {Wait(r["wait"].I)}.");
            case "confirm": return Loc.L("Гэр бүлийн код таарсангүй. Яг адилхан бичээрэй.", "The family code didn't match. Type it exactly.");
            case "email-off": return Loc.L("Энэ сервер дээр имэйл тайлан хараахан асаагүй байна.", "Email reports aren't turned on on this server yet.");
            case "email-busy": return Loc.L($"Өнөөдөр хэт олон баталгаажуулах имэйл илгээлээ. {Wait(r["wait"].I)} дараа дахин оролдоорой.", $"Too many confirmation emails today. Please try again in {Wait(r["wait"].I)}.");
            case "email-failed": return Loc.L("Имэйл илгээж чадсангүй. Хаягаа шалгаад дараа дахин оролдоорой.", "The email could not be sent. Check the address and try again later.");
            case "email-invalid": return Loc.L("Имэйл хаяг буруу байх шиг байна. Жишээ нь: name@gmail.com", "That email address doesn't look right. For example: name@gmail.com");
            default: return Loc.L("Алдаа гарлаа. Дахин оролдоорой.", "Something went wrong. Try again.");
        }
    }

    static string Wait(int s) => s >= 3600 ? Loc.L($"{(s + 3599) / 3600} цагийн", $"{(s + 3599) / 3600} h") : Loc.L($"{Math.Max(1, (s + 59) / 60)} минутын", $"{Math.Max(1, (s + 59) / 60)} min");
    /** A PIN refused by the server is forgotten here, so the next try asks for it again. A family the server no longer has
     *  (erased, or its last child taken out) is forgotten on this device. */
    static void Forget(Game g, J r, string code)
    {
        if (!r["ok"].B && (r["reason"].S == "pin" || r["reason"].S == "pin-locked")) pin = "";
        if (!r["ok"].B && r["reason"].S == "no-family" && code != "" && code == g.App.familyCode) Gone(g, null);
    }
    /** This device leaves its family: no profile here is in it any more (the server no longer has it). With `p`, the server
     *  said that child was erased with the family, so its ticket goes too: it is no use, and the child plays here alone. */
    public static void Gone(Game g, SaveData p)
    {
        string code = g.App.familyCode ?? "";
        foreach (var pid in g.App.profiles) { var k = g.V != null && pid == g.V.id ? g.V : SaveData.Load(pid); if (code != "" && k.fam == code) { k.fam = ""; k.Save(); } }
        if (p != null) { p.fam = ""; p.ticket = ""; p.Save(); }
        g.App.familyCode = ""; g.App.Save(); pin = "";
        g.Toast(p != null ? Loc.L("Таны гэр бүлийг серверээс устгасан байна. Гэр бүлийн кодоор дахин нэвтрэх эсвэл шинэ гэр бүл үүсгээрэй.", "Your family was deleted from the server. Log in again with a family code, or make a new family.")
            : Loc.L("Энэ гэр бүл сервер дээр байхгүй болжээ. Гэр бүлийн кодоор дахин нэвтрэх эсвэл шинэ гэр бүл үүсгээрэй.", "This family is no longer on the server. Log in again with a family code, or make a new family."));
    }

    /** Adds a profile (that has its 3 pictures) to this device's family, or makes the family with it (with the PIN and a
     *  new `recovery` code). A new child joining needs the PIN (`withPin`); once the server took it, it is this device's PIN too. */
    public static IEnumerator Link(Game g, SaveData p, Action<J> done, string recovery = null, bool withPin = true)
    {
        string pics = PicsJson(p.code);
        if (pics == null) { done(Fail("pictures")); yield break; }
        SaveData.EnsureTicket(p); p.Save();
        string server = ServerOf(g, p), code = g.App.familyCode ?? "", used = withPin ? pin : "";
        string body = "{\"act\":" + J.Quote(code == "" ? "create" : "join") + ",\"code\":" + J.Quote(code) + ",\"id\":" + J.Quote(p.id) + ",\"ticket\":" + J.Quote(p.ticket)
            + ",\"name\":" + J.Quote(p.name) + ",\"age\":" + p.age + ",\"avatar\":" + p.avatar + ",\"pics\":" + pics + PinField("pin", used) + PinField("recovery", recovery) + "}";
        J answer = null;
        yield return Post(server, body, r => answer = r);
        Forget(g, answer, code);
        if (!answer["ok"].B && answer["reason"].S == "erased") Gone(g, p); // a child of an erased family
        if (answer["ok"].B)
        {
            g.App.familyCode = answer["code"].S; g.App.server = server.Trim(); p.fam = answer["code"].S; p.Save(); g.App.Save();
            yield return DevicePin.Store(g.App, used != "" ? used : null, recovery); // one PIN for the grown-up: the family's
        }
        done(answer);
    }

    /** A request from a grown-up on this device: `by` and `ticket` are its family child's, `extra` the rest of the fields. */
    static IEnumerator Ask(Game g, string act, string extra, Action<J> done)
    {
        var me = Member(g); string code = g.App.familyCode ?? "";
        if (me == null) { done(Fail("identity")); yield break; }
        string body = "{\"act\":" + J.Quote(act) + ",\"code\":" + J.Quote(code) + ",\"by\":" + J.Quote(me.id) + ",\"ticket\":" + J.Quote(me.ticket) + extra + "}";
        yield return Post(ServerOf(g, me), body, r => { Forget(g, r, code); done(r); });
    }
    /** Is `typed` the family's PIN? For a gate whose own copy is out of date (the PIN was changed on another device). */
    public static IEnumerator CheckPin(Game g, string typed, Action<J> done) => Ask(g, "check-pin", PinField("pin", typed), done);
    /** Sets the family PIN to `next` (with the `old` one; a family without a PIN gets its first one with a `recovery` code).
     *  This device's PIN becomes the same. */
    public static IEnumerator SetPin(Game g, string next, string old, string recovery, Action<J> done)
    {
        J answer = null;
        yield return Ask(g, "pin", PinField("pin", next) + PinField("old", old) + PinField("recovery", recovery), r => answer = r);
        if (answer["ok"].B) { pin = next; yield return DevicePin.Store(g.App, next, recovery); }
        done(answer);
    }
    /** A forgotten family PIN: the recovery code sets `next`, and `newRecovery` replaces the code. No ticket is needed. The
     *  caller stores the new PIN on this device. */
    public static IEnumerator Recover(Game g, string recovery, string next, string newRecovery, Action<J> done)
    {
        string code = g.App.familyCode ?? "";
        string body = "{\"act\":\"recover\",\"code\":" + J.Quote(code) + PinField("recovery", recovery) + PinField("pin", next) + PinField("newRecovery", newRecovery) + "}";
        yield return Post(ServerOf(g, Member(g)), body, r => { Forget(g, r, code); if (r["ok"].B) pin = next; done(r); });
    }
    /** Deletes the whole family on the server: every child's account there, their gers and chat. Then this device leaves it. */
    public static IEnumerator Erase(Game g, string typedPin, string confirm, Action<J> done) =>
        Ask(g, "erase", PinField("pin", typedPin) + PinField("confirm", Clean(confirm)), r =>
        {
            if (r["ok"].B)
            {
                string code = g.App.familyCode;
                foreach (var pid in g.App.profiles) { var p = g.V != null && pid == g.V.id ? g.V : SaveData.Load(pid); if (p.fam == code) { p.fam = ""; p.Save(); } }
                g.App.familyCode = ""; g.App.Save();
            }
            done(r);
        });

    /** Looks like an email address (something@something.xx, no spaces): the server checks it for real. */
    public static bool EmailOk(string e)
    {
        e = (e ?? "").Trim(); int at = e.IndexOf('@'), dot = e.LastIndexOf('.');
        return e.Length <= 254 && !e.Any(char.IsWhiteSpace) && at > 0 && at == e.LastIndexOf('@') && dot > at + 1 && dot < e.Length - 1;
    }
    /** The weekly email for the grown-ups (PROTOCOL.md, "Parent email"), with the PIN: `email` gets a confirm mail (set-email),
     *  null forgets the address (remove-email). The masked address the server shows is kept for the dashboard. */
    public static IEnumerator Email(Game g, string typedPin, string email, Action<J> done) =>
        Ask(g, email != null ? "set-email" : "remove-email", PinField("pin", typedPin) + PinField("email", email?.Trim()), r =>
        {
            if (r["ok"].B) { g.App.emailTo = email != null ? r["email"]["to"].S ?? "" : ""; g.App.emailFam = g.App.familyCode ?? ""; g.App.Save(); }
            done(r["reason"].S == "invalid" && email != null ? Fail("email-invalid") : r);
        });

    /** A grown-up's change to any child of the family, from this device (which plays as one of its children): new pictures
     *  ("pictures", for a child who forgot them) or taking a child out ("remove"), both with the PIN. This device leaves the
     *  family when none of its children are in it any more. */
    public static IEnumerator Manage(Game g, string act, string id, string picsCode, Action<J> done)
    {
        string code = g.App.familyCode ?? "";
        SaveData Kid(string pid) => g.V != null && pid == g.V.id ? g.V : SaveData.Load(pid);
        var me = g.App.profiles.Select(Kid).FirstOrDefault(p => p.fam == code && !string.IsNullOrEmpty(p.ticket));
        if (me == null) { done(Fail("identity")); yield break; }
        string body = "{\"act\":" + J.Quote(act) + ",\"code\":" + J.Quote(code) + ",\"by\":" + J.Quote(me.id) + ",\"ticket\":" + J.Quote(me.ticket) + ",\"id\":" + J.Quote(id)
            + (picsCode != null ? ",\"pics\":" + PicsJson(picsCode) : "") + PinField("pin", pin) + "}";
        yield return Post(ServerOf(g, me), body, r =>
        {
            Forget(g, r, code);
            if (r["ok"].B && act == "remove")
            {
                foreach (var pid in g.App.profiles) { var p = Kid(pid); if (p.id == id && p.fam != "") { p.fam = ""; p.Save(); } }
                if (r["left"].I == 0 || !g.App.profiles.Any(pid => Kid(pid).fam == code)) { g.App.familyCode = ""; g.App.Save(); }
            }
            done(r);
        });
    }
}

/** The device's grown-up PIN, for the parent gate: only a salted PBKDF2 hash (SHA256, 20,000 rounds: one takes a moment on
 *  a slow phone, so never two in one frame) is kept (AppState), with its recovery code's hash. 5 wrong tries lock it for 5 minutes, then 10, 20… (at most a day). */
public static class DevicePin
{
    const int ITERATIONS = 20000, TRIES = 5; const long LOCK_MS = 5 * 60 * 1000, LOCK_MAX = 24 * 60 * 60 * 1000;
    const string LETTERS = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // as the family code: no I/O/0/1 to mix up
    static byte[] Random(int n) { var b = new byte[n]; using (var r = RandomNumberGenerator.Create()) r.GetBytes(b); return b; }
    static string Hash(string text, string salt) { using (var k = new Rfc2898DeriveBytes(text, Convert.FromBase64String(salt), ITERATIONS, HashAlgorithmName.SHA256)) return Convert.ToBase64String(k.GetBytes(32)); }
    static bool Same(string hash, string text, string salt)
    {
        if (string.IsNullOrEmpty(hash) || string.IsNullOrEmpty(salt)) return false;
        byte[] a = Convert.FromBase64String(hash), b = Convert.FromBase64String(Hash(text, salt)); int diff = a.Length ^ b.Length;
        for (int i = 0; i < Math.Min(a.Length, b.Length); i++) diff |= a[i] ^ b[i]; // every byte, so the time says nothing
        return diff == 0;
    }
    public static bool Valid(string pin) => pin != null && pin.Length >= 4 && pin.Length <= 6 && pin.All(c => c >= '0' && c <= '9');
    public static bool Has(AppState a) => !string.IsNullOrEmpty(a.pinHash);
    public static void Set(AppState a, string pin) { a.pinSalt = Convert.ToBase64String(Random(16)); a.pinHash = Hash(pin, a.pinSalt); a.pinFails = 0; a.pinUntil = 0; a.Save(); }
    /** Saves a new PIN and recovery code (either may be null): one PBKDF2 a frame, so the screen never stalls for two. */
    public static IEnumerator Store(AppState a, string pin, string recovery)
    {
        if (pin != null) Set(a, pin);
        if (pin != null && recovery != null) yield return null;
        if (recovery != null) SetRecovery(a, recovery);
    }
    public static void SetRecovery(AppState a, string code) { a.recSalt = Convert.ToBase64String(Random(16)); a.recHash = Hash(Family.Clean(code), a.recSalt); a.Save(); }
    /** Seconds until the lock ends, or 0. */
    public static int Wait(AppState a) => (int)Math.Max(0, (a.pinUntil - SaveData.NowMs() + 999) / 1000);
    public static int Left(AppState a) => TRIES - a.pinFails;
    /** A right PIN (or recovery code) starts the tries again; checking never counts a wrong one, Wrong does. */
    public static bool Match(AppState a, string pin) { if (Wait(a) > 0 || !Same(a.pinHash, pin, a.pinSalt)) return false; a.pinFails = 0; a.Save(); return true; }
    public static bool MatchRecovery(AppState a, string code) { if (Wait(a) > 0 || !Same(a.recHash, Family.Clean(code), a.recSalt)) return false; a.pinFails = 0; a.Save(); return true; }
    public static void Wrong(AppState a)
    {
        if (++a.pinFails >= TRIES) { a.pinFails = 0; a.pinLocks++; a.pinUntil = SaveData.NowMs() + Math.Min(LOCK_MAX, LOCK_MS << Math.Min(20, a.pinLocks - 1)); }
        a.Save();
    }
    /** A new recovery code: 10 letters, shown once as "ABCDE-FGH23". */
    public static string NewRecovery() => new string(Random(10).Select(b => LETTERS[b % 32]).ToArray());
    public static string Show(string code) => code.Length == 10 ? code.Substring(0, 5) + "-" + code.Substring(5) : code;
    /** How long the gate is locked, in words. */
    public static string Locked(AppState a) { int s = Wait(a), m = Math.Max(1, (s + 59) / 60); return Loc.L($"Олон удаа буруу орууллаа. {m} минутын дараа дахин оролдоорой.", $"Too many wrong tries. Please try again in {m} min."); }
}
