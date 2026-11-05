// Email and password (server/email-accounts.js): signing up ends with them (Profiles.cs, AuthPage), and with them a player
// logs in on any other device and gets the same player (coins, XP, clothes, cloud save). A forgotten password is reset
// with a 6-digit code the server mails.
using System;
using System.Collections;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

/** POST /api/email-account: register, login, me, forgot, reset. */
public static class EmailApi
{
    static string Url(string server) { string s = (server ?? "").Trim().TrimEnd('/'); if (s == "") return null; if (!s.StartsWith("http")) s = Family.SCHEME + s; return s + "/api/email-account"; }
    /** A quick look before asking the server, which checks properly. */
    public static bool LooksLikeEmail(string s) => s != null && s.Length <= 254 && Regex.IsMatch(s, @"^[^\s@]+@[^\s@]+\.[^\s@]+$");
    /** done always gets an object: the server's answer, or { ok: false, reason: "offline" | "no-server" }. */
    public static IEnumerator Post(string server, string body, Action<J> done)
    {
        string url = Url(server);
        if (url == null) { done(J.Parse("{\"ok\":false,\"reason\":\"no-server\"}")); yield break; }
        using (var r = new UnityWebRequest(url, "POST"))
        {
            r.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)); r.downloadHandler = new DownloadHandlerBuffer(); r.SetRequestHeader("content-type", "application/json"); r.timeout = 20;
            yield return r.SendWebRequest();
            J answer = null; string text = r.downloadHandler != null ? r.downloadHandler.text : "";
            if (!string.IsNullOrEmpty(text) && text.TrimStart().StartsWith("{")) { try { answer = J.Parse(text); } catch (Exception) { answer = null; } }
            done(answer != null && answer.IsObject ? answer : J.Parse("{\"ok\":false,\"reason\":\"offline\"}"));
        }
    }
    /** Why the server said no, in words. */
    public static string Why(J r)
    {
        switch (r["reason"].S)
        {
            case "bad-email": return Loc.L("Зөв email бичээрэй (ж: bat@gmail.com).", "Write a real email (e.g. bat@gmail.com).");
            case "weak-password": return Loc.L("Нууц үг 6-аас дээш тэмдэгттэй, email-ээсээ өөр байна.", "The password needs 6 or more characters and must differ from the email.");
            case "taken": return Loc.L("Энэ email-ээр өөр хүн бүртгүүлсэн байна. Нэвтрэх эсвэл өөр email бичээрэй.", "Someone already signed up with this email. Log in, or use another email.");
            case "has-email": return Loc.L("Энэ дүр аль хэдийн email-тэй байна.", "This character already has an email.");
            case "wrong": return Loc.L("Email эсвэл нууц үг буруу байна.", "Wrong email or password.") + (r.Has("left") ? Loc.L($" {r["left"].I} оролдлого үлдлээ.", $" {r["left"].I} tries left.") : "");
            case "locked": { int m = Math.Max(1, (r["wait"].I + 59) / 60); return Loc.L($"Олон удаа буруу орууллаа. {m} минутын дараа дахин оролдоорой.", $"Too many wrong tries. Try again in {m} min."); }
            case "bad-code": return Loc.L("Код буруу эсвэл хугацаа нь дууссан байна.", "The code is wrong or has run out.") + (r.Has("left") ? Loc.L($" {r["left"].I} оролдлого үлдлээ.", $" {r["left"].I} tries left.") : "");
            case "mail-off": return Loc.L("Сервер имэйл илгээх тохиргоогүй байна. Том хүнээс тусламж аваарай.", "The server can't send email yet. Ask a grown-up for help.");
            case "mail-failed": return Loc.L("Имэйл илгээж чадсангүй. Дараа дахин оролдоорой.", "Could not send the email. Try again later.");
            case "erased": return Loc.L("Энэ дүрийг устгасан байна.", "This character was erased.");
            case "identity": return Loc.L("Энэ төхөөрөмжийн түлхүүр хүчингүй байна.", "This device's key is not valid.");
            case "busy": return Loc.L("Хэт олон хүсэлт. Түр хүлээгээрэй.", "Too many requests. Wait a moment.");
            case "offline": case "no-server": return Loc.L("Сервертэй холбогдож чадсангүй. Интернэтээ шалгаарай.", "Could not reach the server. Check the internet.");
            default: return Loc.L("Алдаа гарлаа. Дахин оролдоорой.", "Something went wrong. Try again.");
        }
    }
}

public sealed partial class Game
{
    /** Log in with an email and password; or, forgotten, get a code by email and choose a new password. */
    public sealed class EmailLoginModal : Modal
    {
        string step = "login", email = "", pass = "", pass2 = "", code = "", err = "", note = ""; bool busy;
        public override void Press(Game g) { }
        public override void Draw(Game g)
        {
            g.Title(step == "login" ? Loc.L("Email-ээр нэвтрэх", "Log in with email") : Loc.L("Нууц үг сэргээх", "Reset the password"));
            if (step == "login")
            {
                email = g.Field(email, 254, "Email"); g.Gap(6);
                pass = g.PassField(pass, 64, Loc.L("Нууц үг", "Password")); g.Gap(8);
                if (g.Btn(busy ? Loc.L("Түр хүлээнэ үү…", "One moment…") : Loc.L("Нэвтрэх", "Log in"), true, 48, !busy)) g.Later(() => Login(g));
                if (g.Btn(Loc.L("Нууц үгээ мартсан?", "Forgot the password?"), false, 34, !busy)) g.Later(() => { step = "forgot"; err = note = ""; });
            }
            else if (step == "forgot")
            {
                g.Small(Loc.L("Бүртгүүлсэн email-ээ бич. Тэр рүү 6 оронтой код илгээнэ.", "Write the email you signed up with. We'll send a 6-digit code to it."));
                email = g.Field(email, 254, "Email"); g.Gap(8);
                if (g.Btn(busy ? Loc.L("Түр хүлээнэ үү…", "One moment…") : Loc.L("Код илгээх", "Send the code"), true, 48, !busy)) g.Later(() => Forgot(g));
                if (g.Btn(Loc.L("Буцах", "Back"), false, 34, !busy)) g.Later(() => { step = "login"; err = note = ""; });
            }
            else
            {
                g.Small(Loc.L($"{email} рүү илгээсэн кодоо бичээд шинэ нууц үгээ сонго. Имэйл ирээгүй бол Spam хавтсаа шалгаарай.", $"Write the code sent to {email} and choose a new password. No email? Check the spam folder."));
                code = new string(g.Field(code, 6, Loc.L("6 оронтой код", "6-digit code")).Where(char.IsDigit).ToArray()); g.Gap(6);
                pass = g.PassField(pass, 64, Loc.L("Шинэ нууц үг (6+ тэмдэгт)", "New password (6+ characters)")); g.Gap(6);
                pass2 = g.PassField(pass2, 64, Loc.L("Шинэ нууц үгээ давтана уу", "New password again")); g.Gap(8);
                if (g.Btn(busy ? Loc.L("Түр хүлээнэ үү…", "One moment…") : Loc.L("Нууц үг солих", "Change the password"), true, 48, !busy)) g.Later(() => Reset(g));
                if (g.Btn(Loc.L("Кодыг дахин илгээх", "Send the code again"), false, 34, !busy)) g.Later(() => { step = "forgot"; err = note = ""; });
            }
            if (err != "") g.Text("<color=#e14b3d>" + err + "</color>");
            if (note != "") g.Text("<color=#2ea35f>" + note + "</color>");
            if (g.Btn(Loc.L("Хаах", "Close"), false, 30)) g.Later(g.Close);
        }
        string Server(Game g) => Family.ServerOf(g, null);
        void Login(Game g)
        {
            string e = email.Trim();
            if (!EmailApi.LooksLikeEmail(e) || pass == "") { err = Loc.L("Email, нууц үгээ бичээрэй.", "Write your email and password."); Sfx.Bad(); return; }
            busy = true; err = "";
            g.StartCoroutine(EmailApi.Post(Server(g), "{\"act\":\"login\",\"email\":" + J.Quote(e) + ",\"password\":" + J.Quote(pass) + "}", r =>
            {
                busy = false;
                if (g.modal != this) return;
                if (!r["ok"].B) { err = EmailApi.Why(r); Sfx.Bad(); return; }
                pass = ""; g.Close(); g.EnterWithAccount(r);
            }));
        }
        void Forgot(Game g)
        {
            string e = email.Trim();
            if (!EmailApi.LooksLikeEmail(e)) { err = EmailApi.Why(J.Parse("{\"reason\":\"bad-email\"}")); Sfx.Bad(); return; }
            busy = true; err = note = "";
            g.StartCoroutine(EmailApi.Post(Server(g), "{\"act\":\"forgot\",\"email\":" + J.Quote(e) + "}", r =>
            {
                busy = false;
                if (g.modal != this) return;
                if (!r["ok"].B) { err = EmailApi.Why(r); Sfx.Bad(); return; }
                step = "reset"; code = pass = pass2 = "";
                note = Loc.L("Энэ email бүртгэлтэй бол код илгээгдлээ.", "If this email has an account, the code is on its way.");
            }));
        }
        void Reset(Game g)
        {
            if (code.Length != 6) { err = Loc.L("6 оронтой кодоо бичээрэй.", "Write the 6-digit code."); Sfx.Bad(); return; }
            if (pass.Length < 6) { err = Loc.L("Нууц үг 6-аас дээш тэмдэгттэй байна.", "The password needs 6 or more characters."); Sfx.Bad(); return; }
            if (pass != pass2) { err = Loc.L("Хоёр нууц үг таарахгүй байна.", "The two passwords don't match."); Sfx.Bad(); return; }
            busy = true; err = note = "";
            g.StartCoroutine(EmailApi.Post(Server(g), "{\"act\":\"reset\",\"email\":" + J.Quote(email.Trim()) + ",\"code\":" + J.Quote(code) + ",\"password\":" + J.Quote(pass) + "}", r =>
            {
                busy = false;
                if (g.modal != this) return;
                if (!r["ok"].B) { err = EmailApi.Why(r); Sfx.Bad(); return; }
                pass = pass2 = ""; g.Close(); g.Toast(Loc.L("Нууц үг солигдлоо.", "Password changed.")); g.EnterWithAccount(r);
            }));
        }
    }

    /** A login on this device (email, Google): the player it belongs to comes here, with its profile and cloud save, and the
     *  game enters it. A profile already on this device keeps what it has and only takes the new ticket. */
    public void EnterWithAccount(J r)
    {
        string server = Family.ServerOf(this, V), pid = r["id"].S;
        string who = !string.IsNullOrEmpty(r["name"].S) ? r["name"].S : !string.IsNullOrEmpty(r["username"].S) ? r["username"].S : Loc.L("Тоглогч", "Player");
        if (V != null && pid == V.id) { V.ticket = r["ticket"].S; V.Save(); Toast(Loc.L("Нэвтэрлээ.", "Logged in.")); return; }
        bool known = App.profiles.Contains(pid);
        var p = SaveData.Load(pid);
        if (!known)
        {
            p.name = Clean(who); p.created = SaveData.DayKey();
            if (r.Has("age") && r["age"].I >= 3) p.age = r["age"].I;
            if (r.Has("avatar") && r["avatar"].I >= 0 && r["avatar"].I < Profiles.MASCOTS.Length) p.avatar = r["avatar"].I;
        }
        p.ticket = r["ticket"].S;
        if (!string.IsNullOrEmpty(r["email"].S) && r.Has("age")) p.email = r["email"].S; // an email login; a Google one isn't this player's login email
        Cloud.Apply(p, r["cloud"]);
        if (string.IsNullOrWhiteSpace(p.server)) p.server = server.Trim();
        p.Save();
        if (!known) App.profiles.Add(pid);
        App.server = server.Trim(); App.Save();
        Toast(Loc.L($"{p.name} болж нэвтэрлээ.", $"Logged in as {p.name}."));
        Later(() => Enter(p));
    }
}
