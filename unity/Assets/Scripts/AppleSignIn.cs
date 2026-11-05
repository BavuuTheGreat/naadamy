// Sign in with Apple on iPhone and iPad (server/apple-auth.js, Plugins/iOS/NaadamyAppleSignIn.mm): link an Apple ID to
// this player in Settings, then log in with it on any other Apple device and get the same player. Apple's own sheet asks
// for Face ID or the passcode; the server checks the identity token it gives. Other platforms don't show it.
using System;
using System.Collections;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/** POST /api/apple: nonce, native, me, unlink. */
public static class AppleApi
{
    static string Url(string server) { string s = (server ?? "").Trim().TrimEnd('/'); if (s == "") return null; if (!s.StartsWith("http")) s = Family.SCHEME + s; return s + "/api/apple"; }
    /** Shown only where Apple's sheet exists: an iPhone or iPad build. */
#if UNITY_IOS && !UNITY_EDITOR
    public const bool Available = true;
#else
    public const bool Available = false;
#endif
    public static IEnumerator Post(string server, string body, Action<J> done)
    {
        string url = Url(server);
        if (url == null) { done(J.Parse("{\"ok\":false,\"reason\":\"no-server\"}")); yield break; }
        using (var r = new UnityWebRequest(url, "POST"))
        {
            r.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)); r.downloadHandler = new DownloadHandlerBuffer(); r.SetRequestHeader("content-type", "application/json"); r.timeout = 15;
            yield return r.SendWebRequest();
            J answer = null; string text = r.downloadHandler != null ? r.downloadHandler.text : "";
            if (!string.IsNullOrEmpty(text) && text.TrimStart().StartsWith("{")) { try { answer = J.Parse(text); } catch (Exception) { answer = null; } }
            done(answer != null && answer.IsObject ? answer : J.Parse("{\"ok\":false,\"reason\":\"offline\"}"));
        }
    }
    public static string Why(J r)
    {
        switch (r["reason"].S)
        {
            case "identity": return Loc.L("Энэ төхөөрөмжийн түлхүүр хүчингүй байна.", "This device's key is not valid.");
            case "taken": return Loc.L("Энэ Apple ID өөр дүртэй холбогдсон байна.", "This Apple ID is linked to another character.");
            case "has-apple": return Loc.L("Энэ дүр өөр Apple ID-тай холбогдсон байна.", "This character is linked to another Apple ID.");
            case "not-linked": return Loc.L("Энэ Apple ID ямар ч дүртэй холбогдоогүй байна. Эхлээд хуучин төхөөрөмжийнхөө Тохиргооноос холбоорой.", "This Apple ID isn't linked to a character. Link it first in Settings on your old device.");
            case "erased": return Loc.L("Энэ дүрийг устгасан байна.", "This character was erased.");
            case "expired": return Loc.L("Хугацаа дууслаа. Дахин оролдоорой.", "That took too long. Try again.");
            case "unverified": return Loc.L("Apple ID-г шалгаж чадсангүй.", "Could not check the Apple ID.");
            case "busy": return Loc.L("Хэт олон хүсэлт. Түр хүлээгээрэй.", "Too many requests. Wait a moment.");
            case "offline": case "no-server": return Loc.L("Сервертэй холбогдож чадсангүй.", "Could not reach the server.");
            default: return Loc.L("Алдаа гарлаа. Дахин оролдоорой.", "Something went wrong. Try again.");
        }
    }
    public static string Sha256Hex(string text)
    {
        using (var sha = SHA256.Create())
        {
            var b = new StringBuilder(64);
            foreach (byte x in sha.ComputeHash(Encoding.UTF8.GetBytes(text))) b.Append(x.ToString("x2"));
            return b.ToString();
        }
    }
}

public sealed partial class Game
{
#if UNITY_IOS && !UNITY_EDITOR
    delegate void AppleCallback(string idToken, string givenName, string error);
    [System.Runtime.InteropServices.DllImport("__Internal")] static extern void NaadamyAppleSignIn(string hashedNonce, AppleCallback callback); // Plugins/iOS/NaadamyAppleSignIn.mm
    static readonly object appleLock = new object(); static bool appleAnswered; static string appleToken, appleName, appleError;
    [AOT.MonoPInvokeCallback(typeof(AppleCallback))]
    static void OnApple(string idToken, string givenName, string error) { lock (appleLock) { appleToken = idToken ?? ""; appleName = givenName ?? ""; appleError = error ?? ""; appleAnswered = true; } }
#endif

    /** Apple's sheet, then the server. mode "link" (this player) or "login" (this device). */
    public sealed class AppleModal : Modal
    {
        readonly string mode; readonly Action<J> onDone;
        string step = "", err = "";
        public AppleModal(string mode, Action<J> onDone) { this.mode = mode; this.onDone = onDone; }
        public override void Press(Game g) { }

        void Begin(Game g)
        {
            step = "start"; err = "";
            if (mode == "link") SaveData.EnsureTicket(g.V);
            g.StartCoroutine(AppleApi.Post(Family.ServerOf(g, g.V), "{\"act\":\"nonce\"}", r =>
            {
                if (g.modal != this) return;
                if (!r["ok"].B) { err = AppleApi.Why(r); step = "error"; Sfx.Bad(); return; }
#if UNITY_IOS && !UNITY_EDITOR
                lock (appleLock) appleAnswered = false;
                step = "sheet";
                NaadamyAppleSignIn(AppleApi.Sha256Hex(r["nonce"].S), OnApple);
#else
                err = Loc.L("Apple-ээр нэвтрэх нь зөвхөн iPhone, iPad дээр ажиллана.", "Sign in with Apple works only on iPhone and iPad."); step = "error";
#endif
            }));
        }

        public override void Tick(Game g, float dt)
        {
#if UNITY_IOS && !UNITY_EDITOR
            if (step != "sheet") return;
            string token, name, why;
            lock (appleLock) { if (!appleAnswered) return; appleAnswered = false; token = appleToken; name = appleName; why = appleError; }
            if (string.IsNullOrEmpty(token))
            {
                if (why != "cancelled") Debug.LogWarning("Sign in with Apple: " + why);
                err = why == "cancelled" ? Loc.L("Цуцлагдлаа.", "Cancelled.") : Loc.L("Apple-ээр нэвтэрч чадсангүй. Дахин оролдоорой.", "Sign in with Apple didn't work. Try again.");
                step = "error"; return;
            }
            step = "start";
            string who = mode == "link" ? ",\"id\":" + J.Quote(g.V.id) + ",\"ticket\":" + J.Quote(g.V.ticket) : "";
            g.StartCoroutine(AppleApi.Post(Family.ServerOf(g, g.V), "{\"act\":\"native\",\"mode\":" + J.Quote(mode) + ",\"idToken\":" + J.Quote(token) + ",\"name\":" + J.Quote(name) + who + "}", r =>
            {
                if (g.modal != this) return;
                if (!r["ok"].B) { err = AppleApi.Why(r); step = "error"; Sfx.Bad(); return; }
                step = "done"; Sfx.Win(); g.Close(); onDone(r);
            }));
#endif
        }

        public override void Draw(Game g)
        {
            if (step == "") Begin(g);
            GUILayout.Label(mode == "link" ? Loc.L("Apple ID холбох", "Link an Apple ID") : Loc.L("Apple-ээр нэвтрэх", "Sign in with Apple"), new GUIStyle(g.sTitle) { alignment = TextAnchor.MiddleCenter });
            if (step == "start") g.Text(Loc.L("Түр хүлээнэ үү…", "One moment…"));
            else if (step == "sheet") g.Text(Loc.L("Apple-ийн цонхонд баталгаажуулна уу…", "Confirm in Apple's sheet…"));
            else if (step == "error")
            {
                g.Text("<color=#e14b3d>" + err + "</color>");
                if (AppleApi.Available && g.Btn(Loc.L("Дахин оролдох", "Try again"), true, 40)) g.Later(() => Begin(g));
            }
            if (g.Btn(Loc.L("Болих", "Cancel"), false, 32)) g.Later(g.Close);
        }
    }
}
