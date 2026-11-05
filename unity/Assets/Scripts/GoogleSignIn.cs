// Sign in with Google (server/google-auth.js): link a Google account to this player in Settings, then on any other device
// log in with it from the sign-in page and get the same player (coins, XP, clothes, cloud save).
// The same way on every platform: the server gives Google's address and a short code; the game opens the address in a
// browser, shows the code, and asks the server every 2 seconds until the Google page's Confirm (which shows the same code)
// is pressed. On Android the phone's own account picker is used instead (one tap, no browser:
// Plugins/Android/NaadamyOneTap.androidlib), with the browser kept as the way out if it can't. See server/google-auth.js.
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
            case "invalid": return Loc.L("Алдаатай хүсэлт. Тоглоомоо шинэчлээд дахин оролдоорой.", "A bad request. Update the game and try again.");
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

    /** Google's sign-in: on Android the phone's account picker; elsewhere (or if that can't) a browser, then waiting for the
     *  Confirm there. mode "link" (this player) or "login" (this device). */
    public sealed class GoogleModal : Modal
    {
        readonly string mode; readonly Action<J> onDone;
        string step = "", url, poll, code, err = ""; bool busy, opened, browser; float pollAt; Rect openRect;
        // the Android picker answers on another thread: kept here under the lock, read in Tick
        readonly object tapLock = new object(); string tapToken, tapError; bool tapAnswered;
        public GoogleModal(string mode, Action<J> onDone) { this.mode = mode; this.onDone = onDone; }
        public override void Press(Game g) { }
        string Who(Game g) => mode == "link" ? ",\"id\":" + J.Quote(g.V.id) + ",\"ticket\":" + J.Quote(g.V.ticket) : "";

        void Begin(Game g)
        {
            if (mode == "link") SaveData.EnsureTicket(g.V);
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!browser) { BeginTap(g); return; }
#endif
            step = "start"; err = ""; opened = false;
            g.StartCoroutine(GoogleApi.Post(Family.ServerOf(g, g.V), "{\"act\":\"start\",\"mode\":" + J.Quote(mode) + Who(g) + "}", r =>
            {
                if (g.modal != this) return;
                if (!r["ok"].B) { err = GoogleApi.Why(r); step = "error"; Sfx.Bad(); return; }
                url = r["url"].S; poll = r["poll"].S; code = r["code"].S; step = "open"; pollAt = Time.realtimeSinceStartup + 2;
            }));
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        sealed class TapCallback : AndroidJavaProxy
        {
            readonly GoogleModal m;
            public TapCallback(GoogleModal m) : base("com.pineconeacademy.naadamy.onetap.OneTapCallback") { this.m = m; }
            public void onResult(string idToken, string error) { lock (m.tapLock) { m.tapToken = idToken ?? ""; m.tapError = error ?? ""; m.tapAnswered = true; } }
        }
        /** A one-time nonce from the server, then the phone's account picker with it. */
        void BeginTap(Game g)
        {
            step = "tap"; err = ""; lock (tapLock) tapAnswered = false;
            g.StartCoroutine(GoogleApi.Post(Family.ServerOf(g, g.V), "{\"act\":\"nonce\"}", r =>
            {
                if (g.modal != this) return;
                if (!r["ok"].B) { err = GoogleApi.Why(r); step = "error"; Sfx.Bad(); return; }
                try
                {
                    using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                    using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                    using (var oneTap = new AndroidJavaClass("com.pineconeacademy.naadamy.onetap.OneTap"))
                        oneTap.CallStatic("signIn", activity, r["clientId"].S, r["nonce"].S, new TapCallback(this));
                }
                catch (Exception e) { Debug.LogWarning("Google one-tap: " + e.Message); browser = true; Begin(g); } // no picker on this phone: the browser
            }));
        }
        /** The picker answered: send the token to the server, or say why not. */
        void TapAnswered(Game g)
        {
            string token, why;
            lock (tapLock) { if (!tapAnswered) return; tapAnswered = false; token = tapToken; why = tapError; }
            if (string.IsNullOrEmpty(token))
            {
                if (why != "cancelled" && why != "no-account") Debug.LogWarning("Google one-tap: " + why);
                err = why == "cancelled" ? Loc.L("Цуцлагдлаа.", "Cancelled.")
                    : why == "no-account" ? Loc.L("Энэ утсанд Google бүртгэл алга. Browser-ээр нэвтэрч болно.", "There's no Google account on this phone. You can use the browser instead.")
                    : Loc.L("Утасны Google нэвтрэлт ажилласангүй. Browser-ээр нэвтэрч болно.", "The phone's Google sign-in didn't work. You can use the browser instead.");
                step = "error"; return;
            }
            step = "start";
            g.StartCoroutine(GoogleApi.Post(Family.ServerOf(g, g.V), "{\"act\":\"native\",\"mode\":" + J.Quote(mode) + ",\"idToken\":" + J.Quote(token) + Who(g) + "}", r =>
            {
                if (g.modal != this) return;
                if (!r["ok"].B) { err = GoogleApi.Why(r); step = "error"; Sfx.Bad(); return; }
                step = "done"; Sfx.Win(); g.Close(); onDone(r);
            }));
        }
#endif

        public override void Tick(Game g, float dt)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (step == "tap") { TapAnswered(g); return; }
#endif
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
            else if (step == "tap") g.Text(Loc.L("Google бүртгэлээ сонгоно уу…", "Pick your Google account…"));
            else if (step == "error")
            {
                g.Text("<color=#e14b3d>" + err + "</color>");
                if (g.Btn(Loc.L("Дахин оролдох", "Try again"), true, 40)) g.Later(() => Begin(g));
#if UNITY_ANDROID && !UNITY_EDITOR
                if (!browser && g.Btn(Loc.L("Browser-ээр нэвтрэх", "Use the browser instead"), false, 36)) g.Later(() => { browser = true; Begin(g); });
#endif
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

    /** A Google login on this device: the player it is linked to comes here, as an email login does (EmailAccount.cs). */
    public void EnterWithGoogle(J r) => EnterWithAccount(r);
}
