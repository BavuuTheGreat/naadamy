// Басар: the AI buddy, a Mongolian Bankhar dog who answers questions and gives heritage quizzes. Uses Claude through the
// Naadamy server (node server.js, /api/basar) when a server address is set, and his offline knowledge otherwise.
// Ported from js/basar/basar.js and js/basar/knowledge.js (the knowledge itself is in Resources/Data/basar.json).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

public static class Basar
{
    static J D => Data.Get("basar");
    public static List<Tx> FACTS => D["FACTS"].Select(f => f.T).ToList();
    public static IEnumerable<(string id, Tx name)> Topics => D["TOPICS"].Keys.Select(k => (k, D["TOPICS"][k]["name"].T));
    public static List<Tx> Suggest => D["SUGGEST"].Select(f => f.T).ToList();
    public static bool ai; static bool checkedAi;
    public static readonly List<(string role, string content)> history = new List<(string, string)>();

    /** The language a message is written in: Cyrillic means Mongolian, Latin letters mean English. */
    public static string LangOf(string q)
    {
        int c = Regex.Matches(q ?? "", "[а-яёөү]", RegexOptions.IgnoreCase).Count, l = Regex.Matches(q ?? "", "[a-z]", RegexOptions.IgnoreCase).Count;
        return c > l ? "mn" : l > c ? "en" : Loc.En ? "en" : "mn";
    }
    static string LX(string lang, string mn, string en) => lang == "en" ? en : mn;

    /** Simple arithmetic Basar can do offline: "7 x 8", "what is 12 plus 5", "15 хасах 4 хэд вэ". */
    public static string MathAnswer(string q, string lang)
    {
        string t = " " + q.ToLower() + " ";
        void Wd(string words, string sym) => t = Regex.Replace(t, "(^|\\s)(" + words + ")(?=\\s|$)", " " + sym + " ");
        t = Regex.Replace(t, "[=?!,]", " ");
        Wd("what's|what is|how much is|хэд вэ|хэд болох вэ|хэд болох|хэд|тэнцүү|equals|is", "");
        Wd("plus|нэмэх нь|нэмэхэд|нэмэх|нэмэл", "+"); Wd("minus|хасахад|хасах", "-");
        Wd("multiplied by|times|үржүүлэхэд|үржүүлэх|үржих|x", "*"); Wd("divided by|хуваахад|хуваах", "/");
        t = t.Replace('×', '*').Replace('·', '*').Replace('÷', '/');
        var m = Regex.Match(t.Trim(), @"^(-?\d+(?:\.\d+)?)\s*([-+*/])\s*(-?\d+(?:\.\d+)?)$");
        if (!m.Success) return null;
        double a = double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), b = double.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture); string op = m.Groups[2].Value;
        if (op == "/" && b == 0) return LX(lang, "Вуф! Тэгд хувааж болохгүй шүү!", "Woof! You can’t divide by zero!");
        double r = op == "+" ? a + b : op == "-" ? a - b : op == "*" ? a * b : a / b;
        string rs = r == Math.Floor(r) ? ((long)r).ToString() : Math.Round(r, 3).ToString(System.Globalization.CultureInfo.InvariantCulture);
        string sym = op == "*" ? "×" : op == "/" ? "÷" : op;
        return LX(lang, $"Вуф! {a} {sym} {b} = {rs}. Сайн бодлоо!", $"Woof! {a} {sym} {b} = {rs}. Nice maths!");
    }
    static string Norm(string s) => Regex.Replace(Regex.Replace((s ?? "").ToLower(), "[“”\"«».,!?;:()]", " "), "\\s+", " ");
    /** Best offline answer for a question, or null. */
    public static Tx? LocalAnswer(string q)
    {
        string s = Norm(q); J best = null; int bs = 0;
        foreach (var f in D["FAQ"]) { int sc = 0; foreach (var k in f["k"]) if (s.Contains(k.S)) sc += k.S.Length; if (sc > bs) { bs = sc; best = f; } }
        return best != null ? best["a"].T : (Tx?)null;
    }
    public sealed class QuizQ { public string topic, question, explain, source; public string[] options; public int answer; }
    static readonly List<string> asked = new List<string>();
    /** A quiz question from the offline bank, avoiding recently asked ones. */
    public static QuizQ LocalQuiz(string topic)
    {
        var pool = D["QUIZ"].Where(q => string.IsNullOrEmpty(topic) || topic == "mix" || q["t"].S == topic).ToList();
        var fresh = pool.Where(q => !asked.Contains(q["q"]["mn"].S)).ToList(); var from = fresh.Count > 0 ? fresh : pool;
        var x = from[UnityEngine.Random.Range(0, from.Count)];
        asked.Add(x["q"]["mn"].S); if (asked.Count > 30) asked.RemoveAt(0);
        var idx = new[] { 0, 1, 2, 3 }.OrderBy(_ => UnityEngine.Random.value).ToArray(); // shuffle so the right answer is not always first
        return new QuizQ { topic = x["t"].S, question = x["q"].T.ToString(), options = idx.Select(i => x["o"][i].T.ToString()).ToArray(), answer = Array.IndexOf(idx, x["a"].I), explain = x["e"].T.ToString(), source = "offline" };
    }

    // ---------------------------------------------------------------- talking to the Naadamy server (Claude)
#if UNITY_WEBGL && !UNITY_EDITOR
    const string SCHEME = "https://"; // a web page served over https may only call https
#else
    const string SCHEME = "http://";  // node server.js on the local Wi-Fi
#endif
    static string Url(Game g) { string s = (Family.ServerOf(g, g.V) ?? "").Trim().TrimEnd('/'); if (s == "") return null; if (!s.StartsWith("http")) s = SCHEME + s; return s + "/api/basar"; }
    public static IEnumerator Check(Game g)
    {
        string url = Url(g); if (url == null || checkedAi) yield break;
        checkedAi = true;
        using (var r = UnityWebRequest.Get(url)) { r.timeout = 5; yield return r.SendWebRequest(); ai = r.result == UnityWebRequest.Result.Success && J.Parse(r.downloadHandler.text)["ai"].B; }
    }
    public static void Recheck() { checkedAi = false; ai = false; }
    static IEnumerator Post(Game g, string body, Action<J> done)
    {
        using (var r = new UnityWebRequest(Url(g), "POST"))
        {
            r.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)); r.downloadHandler = new DownloadHandlerBuffer(); r.SetRequestHeader("content-type", "application/json"); r.timeout = 25;
            yield return r.SendWebRequest();
            J res = null; // a 200 that is not JSON must still finish: done(null), or the chat shows "…" forever
            if (r.result == UnityWebRequest.Result.Success) { try { res = J.Parse(r.downloadHandler.text); } catch (Exception) { res = null; } }
            done(res);
        }
    }
    static string Who(Game g, string lang) => $"\"lang\":{J.Quote(lang)},\"name\":{J.Quote(g.MyName)},\"age\":{g.V.age}";

    /** Ask Basar something. done(text, source, lang). */
    public static IEnumerator Ask(Game g, string question, Action<string, string, string> done)
    {
        history.Add(("user", question)); g.V.basarAsked++; g.V.Save(); g.Quest("basar");
        string lang = LangOf(question), text = null, source = "offline";
        if (ai)
        {
            string msgs = string.Join(",", history.Select(h => $"{{\"role\":{J.Quote(h.role)},\"content\":{J.Quote(h.content)}}}"));
            J res = null; yield return Post(g, $"{{\"mode\":\"chat\",\"messages\":[{msgs}],{Who(g, lang)}}}", x => res = x);
            if (res != null && res["ok"].B) { text = res["reply"].S; source = "ai"; }
            else if (res != null && res["reason"].S == "refusal") text = LX(lang, "Вуф… Энэ талаар ээж аав, багшаасаа асуугаарай. Харин би чамд өөр зүйл ярьж өгье!", "Woof… please ask a parent or teacher about that one. Ask me something else!");
        }
        if (text == null) text = MathAnswer(question, lang);
        if (text == null)
        {
            var a = LocalAnswer(question);
            text = a.HasValue ? (lang == "en" ? a.Value.en : a.Value.mn) : LX(lang, "Вуф! Интернэтгүй үед би Монголын өв соёл болон энгийн тооны бодлогод л хариулж чадна. Энэ асуултыг эмээ, өвөө, ээж, аавдаа асуугаарай! Эсвэл гэр, цагаан идээ, Нууц товчоо, наадмын тухай асуу.",
                "Woof! Without internet I can only answer about Mongolian heritage and simple maths. Ask your grandma, grandpa, mom or dad this one! Or ask me about gers, dairy foods, the Secret History or Naadam.");
        }
        history.Add(("assistant", text)); if (history.Count > 20) history.RemoveRange(0, history.Count - 20);
        done(text, source, lang);
    }
    /** One quiz question, from the AI when it is on, otherwise from the offline bank. */
    public static IEnumerator Quiz(Game g, string topic, Action<QuizQ> done)
    {
        if (ai)
        {
            string avoid = string.Join(",", asked.Select(J.Quote)); J res = null;
            yield return Post(g, $"{{\"mode\":\"quiz\",\"topic\":{J.Quote(topic ?? "mix")},\"avoid\":[{avoid}],{Who(g, Loc.En ? "en" : "mn")}}}", x => res = x);
            if (res != null && res["ok"].B)
            {
                var q = res["quiz"]; asked.Add(q["question"].S);
                done(new QuizQ { topic = q["topic"].S ?? topic, question = q["question"].S, options = q["options"].Strings().ToArray(), answer = q["answer"].I, explain = q["explain"].S, source = "ai" });
                yield break;
            }
        }
        done(LocalQuiz(topic));
    }
}

public sealed partial class Game
{
    // ---------------------------------------------------------------- Basar's "Did you know?" while you walk
    public sealed class DogBubble { public Tx fact; public float t0; }
    DogBubble dogBubble; float dogNextFact = 25;
    void DogFacts(float dt)
    {
        dogNextFact -= dt;
        if (dogNextFact <= 0 && modal == null)
        {
            dogNextFact = 45 + UnityEngine.Random.value * 30;
            var sf = Seasons.Current();
            Tx f = UnityEngine.Random.value < 0.5f ? new Tx("Мэдэх үү? " + Rules.Pick(sf.facts).mn, "Did you know? " + Rules.Pick(sf.facts).en) : Rules.Pick(Basar.FACTS);
            dogBubble = new DogBubble { fact = f, t0 = Time.time }; Sfx.Pop();
        }
        if (dogBubble != null && Time.time - dogBubble.t0 > 12) dogBubble = null;
    }
    public void OpenBasar(string context = null, string topic = null)
    {
        var b = dogBubble; dogBubble = null;
        if (context == null && b != null) context = b.fact + " " + Loc.L("Илүү ихийг мэдмээр байна уу? Надаас асуугаарай!", "Want to know more? Ask me!");
        Open(new BasarChat(this, context, topic));
    }

    /** A quiz card: the question, four answers, then whether you were right and why. */
    public sealed class QuizCard
    {
        public readonly Basar.QuizQ q; public int picked = -1; readonly Action<bool> onDone;
        public QuizCard(Basar.QuizQ q, Action<bool> onDone) { this.q = q; this.onDone = onDone; }
        public void Draw(Game g)
        {
            GUILayout.Label($"<b>{q.question}</b>" + (q.source == "ai" ? "  <size=" + Mathf.RoundToInt(g.U(10)) + "><color=#2ea35f>AI</color></size>" : ""), g.sLabel);
            for (int i = 0; i < q.options.Length; i++)
            {
                string mark = picked < 0 ? "" : i == q.answer ? "<color=#2ea35f>√</color> " : i == picked ? "<color=#e14b3d>X</color> " : "";
                bool was = GUI.enabled; GUI.enabled = picked < 0;
                if (GUILayout.Button($"{mark}<b>{"АБВГ"[i]}</b>  {q.options[i]}", g.sBtn, GUILayout.MinHeight(g.U(36)))) { int k = i; g.Later(() => Pick(g, k)); }
                GUI.enabled = was;
            }
            if (picked >= 0)
            {
                bool ok = picked == q.answer;
                GUILayout.BeginVertical(g.sBtn);
                GUILayout.Label("<b>" + (ok ? Loc.L("Зөв! Вуф вуф!", "Correct! Woof woof!") : Loc.L("Бараг л! Зөв хариулт: ", "Almost! The answer is: ") + q.options[q.answer]) + "</b>", g.sLabel);
                GUILayout.Label(q.explain, g.sLabel);
                GUILayout.EndVertical();
            }
        }
        void Pick(Game g, int i)
        {
            if (picked >= 0) return; picked = i; bool ok = i == q.answer;
            if (ok) Sfx.Good(); else Sfx.Bad();
            var V = g.V; V.quizTotal++; if (ok) V.quizRight++;
            if (!string.IsNullOrEmpty(q.topic)) { SaveData.Add(V.quizTopicTotal, q.topic, 1); if (ok) SaveData.Add(V.quizTopicRight, q.topic, 1); }
            V.Save();
            if (ok) g.Quest("quiz"); // any correct answer counts, in chat, lessons or quiz parties
            g.CheckAchievements();
            onDone?.Invoke(ok);
        }
    }

    /** The chat window: ask anything, or pick a topic for a quiz. */
    sealed class BasarChat : Modal
    {
        sealed class Line { public bool me; public string text, source; public QuizCard quiz; public bool again; public string topic; }
        readonly List<Line> log = new List<Line>(); string input = ""; bool busy; Vector2 scroll; int lastCount;
        public BasarChat(Game g, string context, string topic, string prompt = null)
        {
            if (prompt != null) g.Later(() => Send(g, prompt));
            g.StartCoroutine(Basar.Check(g));
            log.Add(new Line { text = context ?? Loc.L($"Вуф! Сайн уу, {g.MyName}! Би Басар. Юу ч асуугаарай: Монголын түүх, Нууц товчоо, гэр, байгаль, тоо бодлого… Монголоор ч, англиар ч ойлгоно. Эсвэл доороос сэдэв сонгоод асуулт хариулт тоглоорой!",
                $"Woof! Hi {g.MyName}! I’m Basar. Ask me anything: Mongolian history, the Secret History, gers, nature, maths… I understand English and Mongolian. Or pick a topic below for a quiz!") });
            if (topic != null) QuizIn(g, topic);
        }
        public override float Width => 600;
        public override void Press(Game g) { }
        void Send(Game g, string text)
        {
            string q = (text ?? input).Trim(); if (q == "" || busy) return;
            busy = true; input = ""; log.Add(new Line { me = true, text = q }); var wait = new Line { text = "…" }; log.Add(wait);
            g.StartCoroutine(Basar.Ask(g, q, (t, src, lang) => { wait.text = t; wait.source = src; busy = false; Sfx.Pop(); }));
        }
        void QuizIn(Game g, string topic)
        {
            var holder = new Line { text = "…" }; log.Add(holder);
            g.StartCoroutine(Basar.Quiz(g, topic, q => { holder.text = null; holder.quiz = new QuizCard(q, ok => { if (ok) g.Reward(5, 8); log.Add(new Line { text = Loc.L("Дахиад нэг асуулт уу?", "Another one?"), again = true, topic = topic }); }); }));
        }
        public override void Draw(Game g)
        {
            GUILayout.BeginHorizontal(); g.IconAt(Px.BankharFace(busy ? 2 : 0), 56);
            GUILayout.BeginVertical(); g.Title(Loc.L("Басар", "Basar")); g.Small(Loc.L("Монгол банхар нохой · Таны сурах найз", "Mongolian Bankhar · your learning buddy") + "   " + (Basar.ai ? Loc.L("● AI ухаантай", "● AI smart mode") : Loc.L("○ Офлайн мэдлэг", "○ Offline knowledge")));
            GUILayout.EndVertical(); GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            if (log.Count != lastCount) { lastCount = log.Count; scroll.y = float.MaxValue; }
            scroll = g.BeginScroll(scroll, Screen.height * 0.42f);
            foreach (var l in log)
            {
                GUILayout.BeginHorizontal();
                if (l.me) GUILayout.FlexibleSpace(); else g.IconAt(Px.BankharFace(1), 28);
                GUILayout.BeginVertical(l.me ? g.sPrimary : g.sBtn, GUILayout.MaxWidth(g.U(440)));
                if (l.quiz != null) l.quiz.Draw(g);
                else GUILayout.Label(l.text + (l.source == "ai" ? "  <color=#2ea35f>AI</color>" : ""), l.me ? g.sPrimary.Clean() : g.sLabel);
                if (l.again && GUILayout.Button(Loc.L("Тийм!", "Yes!"), g.sPrimary)) { var tp = l.topic; l.again = false; g.Later(() => QuizIn(g, tp)); }
                GUILayout.EndVertical();
                if (!l.me) GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
            g.EndScroll();
            GUILayout.Label(Loc.L("Асуулт хариулт:", "Quiz me on:"), g.sSmall);
            var topics = Basar.Topics.ToList();
            int hit = g.Chips(topics.Select(t => t.name.ToString()).ToList(), -1, 80);
            if (hit >= 0) { var tp = topics[hit].id; g.Later(() => QuizIn(g, tp)); }
            if (log.Count == 0) // suggestions only before the first question: otherwise they push the input row off a phone's screen
            {
                GUILayout.Label(Loc.L("Асууж болох зүйлс:", "Try asking:"), g.sSmall);
                var sug = Basar.Suggest.Select(s => s.ToString()).ToList(); int sh = -1; for (int i = 0; i < sug.Count; i++) if (g.Btn(sug[i], false, 34)) sh = i; // whole questions: one per line
                if (sh >= 0) { var s = sug[sh]; g.Later(() => Send(g, s)); }
            }
            GUILayout.BeginHorizontal();
            GUI.SetNextControlName("basar-input");
            input = GUILayout.TextField(input, 300, new GUIStyle(g.sBtn) { alignment = TextAnchor.MiddleLeft }, GUILayout.MinHeight(g.U(40)));
            bool enter = Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter) && GUI.GetNameOfFocusedControl() == "basar-input";
            if (GUILayout.Button(Loc.L("Илгээх", "Send"), g.Fit(g.sPrimary, Loc.L("Илгээх", "Send"), g.U(90)), GUILayout.Width(g.U(90)), GUILayout.MinHeight(g.U(40))) || enter) g.Later(() => Send(g, null));
            GUILayout.EndHorizontal();
        }
    }
}
