// Sign in with Google (server/google-auth.js): link a Google account to this player in Settings, then on any other device
// log in with it from the sign-in page and get the same player (coins, XP, clothes, cloud save).
// The same way on every platform: the server gives Google's address and a short code; the game opens the address in a
// browser, shows the code, and asks the server every 2 seconds until the Google page's Confirm (which shows the same code)
// is pressed. See the comment at the top of server/google-auth.js.
using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/** POST /api/google: start, poll, me, unlink. */
public static class GoogleApi
{
    static string Url(string server) { string s = (server ?? "").Trim().TrimEnd('/'); if (s == "") return null; if (!s.StartsWith("http")) s = Family.SCHEME + s; return s + "/api/google"; }
    /** done always gets an object: the server's answer, or { ok: false, reason: "offline" | "no-server" }. */
    public static IEnumerator Post(string server, string body, Action<J> done)
    {
        string url = Url(server);
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
    /** Why the server said no, in words. */
    public static string Why(J r)
    {
        switch (r["reason"].S)
        {
            case "off": return Loc.L("Сервер дээр Google нэвтрэлт асаагүй байна.", "Google sign-in is not turned on on the server.");
            case "identity": return Loc.L("Энэ төхөөрөмжийн түлхүүр хүчингүй байна.", "This device's key is not valid.");
            case "taken": return Loc.L("Энэ Google бүртгэл өөр дүртэй холбогдсон байна.", "This Google account is linked to another character.");
            case "has-google": return Loc.L("Энэ дүр өөр Google бүртгэлтэй холбогдсон байна.", "This character is linked to another Google account.");
            case "not-linked": return Loc.L("Энэ Google бүртгэл ямар ч дүртэй холбогдоогүй байна. Эхлээд хуучин төхөөрөмжийнхөө Тохиргооноос холбоорой.", "This Google account isn't linked to a character. Link it first in Settings on your old device.");
            case "erased": return Loc.L("Энэ дүрийг устгасан байна.", "This character was erased.");
            case "cancelled": return Loc.L("Цуцлагдлаа.", "Cancelled.");
            case "expired": return Loc.L("Хугацаа дууслаа. Дахин оролдоорой.", "That took too long. Try again.");
            case "google": case "unverified": return Loc.L("Google бүртгэлийг шалгаж чадсангүй.", "Could not check the Google account.");
            case "busy": return Loc.L("Хэт олон хүсэлт. Түр хүлээгээрэй.", "Too many requests. Wait a moment.");
            case "offline": case "no-server": return Loc.L("Сервертэй холбогдож чадсангүй.", "Could not reach the server.");
            default: return Loc.L("Алдаа гарлаа. Дахин оролдоорой.", "Something went wrong. Try again.");
        }
    }
}

public sealed partial class Game
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [System.Runtime.InteropServices.DllImport("__Internal")] static extern void NaadamyOpenOnRelease(string url); // Plugins/WebGL/NaadamyPointer.jslib
#endif

    /** Google's sign-in in a browser, then waiting for the Confirm there. mode "link" (this player) or "login" (this device). */
    public sealed class GoogleModal : Modal
    {
        readonly string mode; readonly Action<J> onDone;
        string step = "", url, poll, code, err = ""; bool busy, opened; float pollAt; Rect openRect;
        public GoogleModal(string mode, Action<J> onDone) { this.mode = mode; this.onDone = onDone; }
        public override void Press(Game g) { }

        void Begin(Game g)
        {
            step = "start"; err = ""; opened = false;
            string body = "{\"act\":\"start\",\"mode\":" + J.Quote(mode);
            if (mode == "link") { SaveData.EnsureTicket(g.V); body += ",\"id\":" + J.Quote(g.V.id) + ",\"ticket\":" + J.Quote(g.V.ticket); }
            g.StartCoroutine(GoogleApi.Post(Family.ServerOf(g, g.V), body + "}", r =>
            {
                if (g.modal != this) return;
                if (!r["ok"].B) { err = GoogleApi.Why(r); step = "error"; Sfx.Bad(); return; }
                url = r["url"].S; poll = r["poll"].S; code = r["code"].S; step = "open"; pollAt = Time.realtimeSinceStartup + 2;
            }));
        }

        public override void Tick(Game g, float dt)
        {
            if (step != "open" || busy || Time.realtimeSinceStartup < pollAt) return;
            busy = true;
            g.StartCoroutine(GoogleApi.Post(Family.ServerOf(g, g.V), "{\"act\":\"poll\",\"poll\":" + J.Quote(poll) + "}", r =>
            {
                busy = false; pollAt = Time.realtimeSinceStartup + 2;
                if (g.modal != this || step != "open") return;
                if (r["ok"].B && r["status"].S == "waiting") return;
                if (!r["ok"].B) { if (r["reason"].S == "offline") return; err = GoogleApi.Why(r); step = "error"; Sfx.Bad(); return; } // a dropped request: ask again
                step = "done"; Sfx.Win(); g.Close(); onDone(r);
            }));
        }

        public override void Draw(Game g)
        {
            if (step == "") Begin(g);
            var center = new GUIStyle(g.sLabel) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
            GUILayout.Label(mode == "link" ? Loc.L("Google бүртгэл холбох", "Link a Google account") : Loc.L("Google-ээр нэвтрэх", "Sign in with Google"), new GUIStyle(g.sTitle) { alignment = TextAnchor.MiddleCenter });
            if (step == "start") g.Text(Loc.L("Түр хүлээнэ үү…", "One moment…"));
            else if (step == "error")
            {
                g.Text("<color=#e14b3d>" + err + "</color>");
                if (g.Btn(Loc.L("Дахин оролдох", "Try again"), true, 40)) g.Later(() => Begin(g));
            }
            else if (step == "open")
            {
                GUILayout.Label(Loc.L("Google хуудас дээр энэ код харагдана. Таарч байвал «Тийм, зөвшөөрөх» дарна уу:", "The Google page will show this code. If it matches, press “Yes”:"), center);
                GUILayout.Label($"<size={g.Ts(34)}><b>{code}</b></size>", center);
                // a browser opens a tab only inside a click, which Unity hears a frame late: arm it on the press, open on the release
                var e = Event.current;
                bool pressed = e.type == EventType.MouseDown && openRect.Contains(e.mousePosition);
#if UNITY_WEBGL && !UNITY_EDITOR
                if (pressed) { NaadamyOpenOnRelease(url); opened = true; }
                g.Btn(opened ? Loc.L("Google хуудсыг дахин нээх", "Open the Google page again") : Loc.L("Google хуудсыг нээх", "Open the Google page"), true, 48);
#else
                if (g.Btn(opened ? Loc.L("Google хуудсыг дахин нээх", "Open the Google page again") : Loc.L("Google хуудсыг нээх", "Open the Google page"), true, 48)) { Application.OpenURL(url); opened = true; }
#endif
                if (e.type == EventType.Repaint) openRect = GUILayoutUtility.GetLastRect();
                if (opened) g.Small(Loc.L("Google дээр нэвтэрч дуусаад энд буцаж ирээрэй. Тоглоом өөрөө үргэлжилнэ.", "Finish on the Google page, then come back here. The game carries on by itself."));
            }
            if (g.Btn(Loc.L("Болих", "Cancel"), false, 32)) g.Later(g.Close);
        }
    }

    /** A Google login on this device: the player it is linked to comes here (as an arena login does) and the game enters it. */
    public void EnterWithGoogle(J r)
    {
        string server = Family.ServerOf(this, V), pid = r["id"].S;
        string who = !string.IsNullOrEmpty(r["username"].S) ? r["username"].S : !string.IsNullOrEmpty(r["name"].S) ? r["name"].S : Loc.L("Тоглогч", "Player");
        if (V != null && pid == V.id) { V.ticket = r["ticket"].S; V.Save(); Toast(Loc.L("Google-ээр нэвтэрлээ.", "Signed in with Google.")); return; }
        bool known = App.profiles.Contains(pid);
        var p = SaveData.Load(pid);
        if (!known) { p.name = Clean(who); p.created = SaveData.DayKey(); }
        p.ticket = r["ticket"].S;
        Cloud.Apply(p, r["cloud"]);
        if (string.IsNullOrWhiteSpace(p.server)) p.server = server.Trim();
        p.Save();
        if (!known) App.profiles.Add(pid);
        App.server = server.Trim(); App.Save();
        Toast(Loc.L($"{p.name} болж нэвтэрлээ.", $"Signed in as {p.name}."));
        Later(() => Enter(p));
    }
}
