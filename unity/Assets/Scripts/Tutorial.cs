// The tutorial level (js/tutorial.js) and the story box (js/dialog.js): Guide Bolormaa walks a new player through Нутаг.
// She talks in the story box (standing beside it, her face and pose changing with what she says, her own voice), then hands over control for small goals:
// walk, jump, call Basar, drink from the well. It starts by itself on the first visit; the "?" button plays it again.
// Later she drops by now and then: a quest to claim, new quests, someone with a task, a tip.
using System;
using System.Collections;
using System.Linq;
using UnityEngine;

public sealed partial class Game
{
    // ================================================================ the story box
    public sealed class Dialog
    {
        public string who, face, text, name; public string[] choices; public bool free, top; public float shown, auto = -1, born;
        public bool done; public int picked = -1;
        public bool Typing => shown < text.Length;
    }
    Dialog dialog;
    string glowKey; (float x, float y, string label)? tutTarget;

    Dialog ShowDialog(string who, string face, string text, string[] choices = null, bool free = true, float auto = -1, bool top = false)
    {
        string name = who == "basar" ? Loc.L("Басар", "Basar") : Loc.L("Хөтөч Болормаа", "Guide Bolormaa");
        return dialog = new Dialog { who = who, face = face, text = text, name = name, choices = choices, free = free, auto = auto, top = top, born = Time.time };
    }
    /** A tap on the box: show all the words, or close it. */
    void DialogNext()
    {
        var d = dialog; if (d == null) return;
        if (d.Typing) { d.shown = d.text.Length; return; }
        if (d.choices != null) return;
        d.done = true; dialog = null; Sfx.Tap();
    }
    void DialogTick(float dt)
    {
        var d = dialog; if (d == null) return;
        if (d.Typing)
        {
            int before = (int)d.shown; char c = d.text[Mathf.Min(before, d.text.Length - 1)];
            float speed = d.who == "basar" ? 36 : 33; if (".!?…".IndexOf(c) >= 0) speed /= 6; else if (",—".IndexOf(c) >= 0) speed /= 3;
            d.shown = Mathf.Min(d.text.Length, d.shown + dt * speed);
            for (int i = before; i < (int)d.shown; i++) if (char.IsLetterOrDigit(d.text[i]) && i % (d.who == "basar" ? 3 : 2) == 0)
                {
                    int j = i; while (j < d.text.Length && ".!?…".IndexOf(d.text[j]) < 0) j++;
                    float mul = j < d.text.Length && d.text[j] == '?' && j - i < 8 ? 1 + (8 - (j - i)) * 0.035f : j < d.text.Length && d.text[j] == '!' ? 1.08f : 1;
                    if (d.who != "basar") mul *= 1 + Mathf.Sin(i * 1.7f) * 0.06f; // the guide's playful sing-song
                    Sfx.Blip(d.who, mul);
                }
        }
        else if (d.auto > 0 && Time.time - d.born > d.auto + d.text.Length / 33f) { d.done = true; dialog = null; }
    }
    void DialogDraw(float vw, float vh)
    {
        TargetDraw();
        GoalDraw(vw);
        var d = dialog; if (d == null) return;
        // the story box, as on the web version: black with a white pixel border and white pixel text typing out. Basar sits in a portrait
        // on the left; Bolormaa stands beside the box from head to toe, her face and pose following what she says
        bool guide = d.who != "basar";
        float k = Mathf.Clamp(AtLeastCss(16, 15) / 16, 1, 2.6f); // on a phone the box, its words and its answers grow to a readable size
        float w = Mathf.Min(vw - 16, 720 * k), pad = 12 * k, pic = (guide ? 112 : 72) * Mathf.Min(k, 1.5f), tx = pad * 2 + pic, tw = w - tx - pad;   // Bolormaa's 112×144 reference portrait
        var st = new GUIStyle(sLabel) { wordWrap = true, alignment = TextAnchor.UpperLeft, fontSize = Mathf.RoundToInt(U(TALK * k)), normal = { textColor = Color.white } }; // NPC talk reads in the body font at TALK (GameUI.cs)
        var opt = k > 1 ? new GUIStyle(sOpt) { fontSize = Mathf.RoundToInt(sOpt.fontSize * k) } : sOpt;
        float line = 24 * k, th = st.CalcHeight(new GUIContent(d.text), U(tw)) / UiScale, n = d.choices != null ? d.choices.Length : 0, oht = 0;
        for (int i = 0; i < n; i++) oht += Mathf.Max(40 * k, opt.CalcHeight(new GUIContent(d.choices[i]), U(tw)) / UiScale) + 6 * k; // an answer grows to fit its (often long) words
        float h = Mathf.Max(guide ? pic * 72 / 56f + pad * 2 : pic + pad * 2, pad + line + th + (n > 0 ? 8 * k + oht : 18 * k) + pad);
        float x = vw / 2 - w / 2, y = d.top ? (guide ? 120 : 60) : vh - h - 70;
        var r = UR(x, y, w, h); Ui(r);
        Box(r, Color.white); Box(UR(x + 3, y + 3, w - 6, h - 6), Px.Col("#08060e"));
        if (n == 0 && GUI.Button(r, GUIContent.none, GUIStyle.none)) Later(DialogNext); // tapping the box goes on; with answers, only the answers do
        bool talking = d.Typing && (int)(Time.time * 9) % 2 == 0, blink = Time.time % 3.7f < 0.12f;
        if (guide)
        {
            // her portrait in a frame on the left of the box: her face follows what she says, her mouth moves as the words type out
            float ph = pic * 72 / 56f, hop = Mathf.Max(0, 1 - (Time.time - d.born) / 0.25f) * 3;
            var fr = UR(x + pad, y + pad - hop, pic, ph);
            Box(new Rect(fr.x - U(3), fr.y - U(3), fr.width + U(6), fr.height + U(6)), Color.white); Box(fr, Px.Col("#34286e"));
            GUI.DrawTexture(fr, Tex(Portrait("bolormaa", d.face, talking, blink)));
        }
        else
        {
            PxCanvas face = Px.BankharFace(d.Typing && (int)(Time.time * 8) % 2 == 0 ? 1 : 0);
            Box(UR(x + pad - 2, y + pad - 2, pic + 4, pic + 4), Color.white); Box(UR(x + pad, y + pad, pic, pic), Px.Col("#2b2140"));
            GUI.DrawTexture(UR(x + pad, y + pad, pic, pic), Tex(face));
        }
        GUI.Label(UR(x + tx, y + pad - 2, tw, line), d.name.ToUpper(), new GUIStyle(sPixel) { alignment = TextAnchor.UpperLeft, fontSize = PxSize(U(16 * k)), normal = { textColor = Px.Col(d.who == "basar" ? "#e9a91f" : "#f7c646") } });
        string vis = d.text.Substring(0, Mathf.Min(d.text.Length, (int)d.shown));
        GUI.Label(UR(x + tx, y + pad + line, tw, th + 4), vis + "<color=#00000000>" + d.text.Substring(vis.Length) + "</color>", st);
        if (!d.Typing && n == 0) GUI.Label(UR(x + w - 30 * k, y + h - 28 * k, 20 * k, 20 * k), (int)(Time.time * 2) % 2 == 0 ? "▼" : "", st);
        if (n > 0 && !d.Typing)
        {
            float oy = y + pad + line + th + 8 * k;
            for (int i = 0; i < n; i++)
            {
                float oh = Mathf.Max(40 * k, opt.CalcHeight(new GUIContent(d.choices[i]), U(tw)) / UiScale);
                if (Button(UR(x + tx, oy, tw, oh), d.choices[i], opt)) { int ci = i; Later(() => { d.picked = ci; d.done = true; if (dialog == d) dialog = null; Sfx.Tap(); }); }
                oy += oh + 6 * k;
            }
        }
    }
    /** A pulsing yellow frame round the button Bolormaa is talking about. */
    void GlowAt(string key, Rect r)
    {
        if (glowKey != key) return;
        float a = 0.55f + Mathf.Sin(Time.time * 6) * 0.45f, t = U(3); var c = new Color(1, 0.84f, 0.2f, a);
        Box(new Rect(r.x - t, r.y - t, r.width + t * 2, t), c); Box(new Rect(r.x - t, r.yMax, r.width + t * 2, t), c);
        Box(new Rect(r.x - t, r.y, t, r.height), c); Box(new Rect(r.xMax, r.y, t, r.height), c);
    }
    /** The yellow arrow that shows the way: over the place, or at the edge of the screen pointing to it. */
    void TargetDraw()
    {
        if (tutTarget == null) return;
        var (tx0, ty0, label) = tutTarget.Value; var p = ToGui(tx0 + 0.5f, ty0 - 0.2f); p -= new Vector2(guiOX, guiOY); p /= UiScale;
        float vw = Screen.safeArea.width / UiScale, vh = Screen.safeArea.height / UiScale, bob = Mathf.Sin(Time.time * 5) * 5;
        bool off = p.x < 20 || p.y < 70 || p.x > vw - 20 || p.y > vh - 80;
        var q = new Vector2(Mathf.Clamp(p.x, 40, vw - 40), Mathf.Clamp(p.y, 90, vh - 100));
        string arrow = !off ? "▼" : p.x < 20 ? "◄" : p.x > vw - 20 ? "►" : p.y < 70 ? "▲" : "▼";
        Outlined(U(q.x), U(q.y - 24 + (off ? 0 : bob)), label, U(13), Px.Col("#ffd23f"));
        Outlined(U(q.x), U(q.y + (off ? 0 : bob)), arrow, U(22), Px.Col("#ffd23f"));
    }

    // ================================================================ goals: Bolormaa hands you control until you've done it
    string goalText; Func<string> goalProgress; bool goalSkip;
    void GoalDraw(float vw)
    {
        if (goalText == null) return;
        var r = UR(vw / 2 - 180, 56, 360, 40); Ui(r);
        GUI.Box(r, GUIContent.none, sToast);
        GUI.Label(UR(vw / 2 - 170, 56, 260, 40), "<b>" + goalText + "</b>" + (goalProgress != null ? "  " + goalProgress() : ""), new GUIStyle(sToast) { alignment = TextAnchor.MiddleLeft });
        if (Button(UR(vw / 2 + 96, 61, 78, 30), Loc.L("Алгасах", "Skip"))) Later(() => { goalSkip = true; Sfx.Tap(); });
    }

    // ================================================================ the lesson
    int tutRun; Coroutine tutCo;
    public bool TutorialActive => tutRun != 0;
    public void StartTutorial(bool replay)
    {
        if (V == null || tutRun != 0 || race != null || battle != null) return;
        CloseAll();
        if (!replay && V.tutorialSeen == 0) { V.tutorialSeen = SaveData.NowMs(); V.Save(); } // seen once: never starts by itself again
        path.Clear(); tutRun = UnityEngine.Random.Range(1, int.MaxValue);
        tutCo = StartCoroutine(Lesson(tutRun, replay));
    }
    void StopTutorial()
    {
        if (tutRun == 0) return;
        tutRun = 0; if (tutCo != null) StopCoroutine(tutCo); tutCo = null;
        dialog = null; glowKey = null; goalText = null; tutTarget = null;
        dropNext = Mathf.Max(dropNext, Time.realtimeSinceStartup + GAP_SHORT); // no drop-in visit right after she has just talked
    }
    int askAnswer;
    IEnumerator Say(string face, string mn, string en, string glow = null, string who = "guide")
    {
        glowKey = glow; var d = ShowDialog(who, face, Loc.L(mn, en), null, true, -1, glow == "jump" || glow == "run"); // her box moves to the top when she points at a button below
        while (!d.done) yield return null;
    }
    IEnumerator Ask(string face, string mn, string en, params Tx[] choices)
    {
        glowKey = null; var d = ShowDialog("guide", face, Loc.L(mn, en), choices.Select(c => c.ToString()).ToArray(), false);
        while (!d.done) yield return null;
        askAnswer = d.picked;
    }
    IEnumerator Goal(Tx text, Func<bool> done, Func<string> progress = null, bool allowModal = false)
    {
        dialog = null; goalText = text.ToString(); goalProgress = progress; goalSkip = false;
        while (true)
        {
            yield return new WaitForSeconds(0.15f);
            if (goalSkip) break;
            if (modal != null && !allowModal) continue; // wait while a window is open
            if (done()) { Sfx.Good(); break; }
        }
        goalText = null; goalProgress = null;
    }
    static Tx Tx2(string mn, string en) => new Tx(mn, en);

    IEnumerator Lesson(int r, bool replay)
    {
        string name = V.name; bool touch = Game.TouchUI;
        if (replay)
        {
            yield return Ask("happy", $"Дахиад уулзлаа, {name}! Юу сонсмоор байна?", $"Hi again, {name}! What would you like to hear about?",
                Tx2("Бүгдийг эхнээс нь", "Everything from the start"), Tx2("Удирдлага: алхах, үсрэх", "Controls: walking, jumping"), Tx2("Монголын түүх", "Mongolian history"), Tx2("Надад асуулт байна", "I have a question"), Tx2("Юу ч үгүй, баярлалаа", "Nothing, thanks"));
            if (askAnswer == 1) { yield return Controls(touch); yield return Finish(false, false); yield break; }
            if (askAnswer == 2) { yield return OfferStory(name, true); yield break; }
            if (askAnswer == 3) { yield return Questions(); yield return Finish(false, false); yield break; }
            if (askAnswer != 0) { yield return Finish(false, false); yield break; }
        }
        // hello
        yield return Say("wink", $"Сайн уу, {name}! Би Болормаа, энэ Нутгийн хөтөч.", $"Hello, {name}! I’m Bolormaa, the guide of this village.");
        yield return Say("happy", "Энд мал саах, бөх барилдах, морь уралдуулах, монгол гэр барих бүгдийг тоглож сурна.", "Here you learn by playing: milking, wrestling, horse racing, building a ger and more.");
        if (!storyAsked)
        {
            storyAsked = true;
            yield return OfferStory(name, false);
            if (tutRun != r) yield break;   // off to the story: the tour waits until they are back
        }
        yield return Ask("tease", "Тэгэхээр... надтай хамт Нутгаар нэг тойрох уу?", "So... want to take a little tour with me?", Tx2("Тийм ээ, явцгаая!", "Yes, let’s go!"), Tx2("Нутаг гэж юу вэ?", "What is Nutag?"), Tx2("Би өөрөө мэднэ", "I already know how"));
        if (askAnswer == 1)
        {
            yield return Say("think", "“Нутаг” гэдэг нь төрсөн газар, гэр орон гэсэн утгатай. Монголчууд нутгаа их хайрладаг.", "“Nutag” means your homeland, the place you come from. Mongolians love their nutag dearly.");
            yield return Say("happy", "Энэ бол бидний тосгон. Найзууд чинь ч энд хамт тоглож болно!", "This is our village. Your friends can play here with you too!");
        }
        else if (askAnswer == 2)
        {
            yield return Ask("surprised", "Өө, үнэхээр үү? Тэгвэл алгасах уу?", "Oh, really? Shall we skip it then?", Tx2("Тийм, алгасъя", "Yes, skip it"), Tx2("Үгүй, үзье", "No, show me"));
            if (askAnswer == 0) { yield return Say("proud", "За за! Мартвал дээд талын ? товчийг дараарай. Сайхан тоглоорой!", "Okay! If you forget anything, tap the ? at the top. Have fun!", "help"); yield return Finish(true, true); yield break; }
        }
        yield return Controls(touch);

        // Basar
        yield return Say("happy", "Хараач, энэ миний найз Басар! Монгол банхар нохой. Тэр маш ухаантай.", "Look, this is my friend Basar! A Mongolian Bankhar dog. He is very clever.");
        yield return Say("happy", "Вуф! Вуф-вуф! Сайн уу! Надаас юу ч асуугаарай!", "Woof! Woof-woof! Hi! Ask me anything!", null, "basar");
        yield return Say("wink", touch ? "Басар дээр эсвэл зүүн дээд талын нохойн товч дээр дарвал түүнтэй ярилцана. Оролдоод үз дээ!" : "Басар дээр дарах, B товч эсвэл нохойн товчоор түүнтэй ярилцана. Оролдоод үз дээ!",
            touch ? "Tap Basar or the dog button at the top left to talk to him. Give it a try!" : "Click Basar, press B, or use the dog button to talk to him. Try it!", "basar");
        bool opened = false;
        yield return Goal(Tx2("Басартай ярилц", "Talk to Basar"), () => { if (modal is BasarChat) opened = true; return opened && modal == null; }, null, true);
        glowKey = null;
        yield return Say("laugh", "Ха-ха, Басар чамд дуртай боллоо! Түүнээс хэзээ ч асууж болно.", "Ha-ha, Basar likes you already! You can ask him anything, any time.");

        // using things: the well
        yield return Say("neutral", touch ? "Аливаа зүйлийн хажууд очиход доод талд товч гарч ирнэ. Түүнийг дарвал ашиглана." : "Аливаа зүйлийн хажууд очиход доод талд товч гарч ирнэ. E товч дарж (эсвэл товчийг дарж) ашиглана.",
            touch ? "When you stand next to something, a button pops up at the bottom. Tap it to use it." : "When you stand next to something, a button pops up at the bottom. Press E (or click the button) to use it.");
        yield return Say("tease", "Тэр худаг руу очоод ус уугаад ир. Шар сум замыг заана!", "Go to the well and have a drink. The yellow arrow shows the way!");
        var well = World.objects.FirstOrDefault(o => o.id == "well"); long drank0 = SaveData.Get(V.ate, "us");
        if (well != null) tutTarget = (well.x, well.y, Loc.L("Худаг", "Well"));
        yield return Goal(Tx2("Худгаас ус уу", "Drink from the well"), () => SaveData.Get(V.ate, "us") > drank0);
        tutTarget = null;
        yield return Say("happy", "Ам цангаагаа тайллаа! Гэр, мал, дэлгүүр, хүмүүс бүгд ингэж ажилладаг.", "Refreshing! Gers, animals, the shop and people all work like that.");

        // the buttons
        yield return Say("proud", "Одоо товчнуудаа танилцъя. Энэ чиний зоос. Даалгавар хийж зоос цуглуулаад дэлгүүрээс дээл, малгай, тэжээвэр ав.", "Now the buttons. These are your coins. Do tasks, earn coins and buy deels, hats and pets at the shop.", "coins");
        yield return Say("worried", "Энэ ногоон зурвас бол хүч. Гүйхэд багасна. Ядарвал хоол идэж, ундаа уугаарай.", "This green bar is your energy. Running uses it up. When you’re tired, eat or drink something.", "energy");
        yield return Say("happy", "Ном дээр дарвал Өв соёлын сургууль нээгдэнэ: гэр барих, цагаан идээ хийх, Нууц товчоо...", "The book opens the Heritage School: building a ger, making dairy, the Secret History...", "learn");
        yield return Say("wink", "Энэ товчийг дарвал өдрийн даалгаврууд гарна. Өдөр бүр шинэ!", "This button shows your daily quests. New ones every day!", "quests");
        yield return Say("excited", "Энэ нүд товч бол хамгийн гайхалтай нь: бүх Нутгийг 3D-ээр өөрийн нүдээр харна!", "And the eye button is the coolest: see the whole village in 3D, through your own eyes!", "fp");
        yield return Say("tease", "Мартчихвал энэ ? товчийг дар. Би дахиад гүйгээд ирнэ!", "If you forget anything, tap this ?. I’ll come running back!", "help");
        glowKey = null;
        yield return Questions();
        yield return Finish(true, false);
    }
    // ================================================================ the story mode: Bolormaa asks if you want to live history first hand
    bool storyAsked, storyFromTutorial;
    IEnumerator OfferStory(string name, bool replay)
    {
        yield return Ask("curious", $"Хүлээгээрэй, {name}! Монголын түүхийг өөрийн нүдээр үзэж, өөрөө амьдарч үзмээр байна уу? Бяцхан Тэмүжин хэрхэн Чингис хаан болсныг туулна шүү!",
            $"Wait, {name}! Would you like to experience Mongolian history first hand? You'll live how young Temüjin became Chinggis Khaan!",
            Tx2("Тийм ээ, түүх рүү!", "Yes, take me into history!"), Tx2("Дараа, эхлээд Нутгаа үзье", "Later, show me the village first"));
        if (askAnswer == 0)
        {
            yield return Say("excited", "Гоё! Тэмүжин өөрөө чамд түүхээ ярина. Тулалдаанд орж, түүний хүүхэд насыг туулна. Буцаж ирэхэд чинь би энд хүлээж байя!",
                "Great! Temüjin himself will tell you his story. You'll ride into battle and live his childhood. I'll be waiting right here when you're back!");
            if (!replay) storyFromTutorial = true;
            V.tutorialSeen = Math.Max(V.tutorialSeen, 1); V.Save();
            PlayStory(CHAPTERS[0], StoryPart(CHAPTERS[0].id));
            StopTutorial();   // this ends the lesson coroutine we are in, so it comes last
            yield break;
        }
        yield return Say("wink", "За за! Түүхийн аялал ••• товчинд, Чингис хааны зурагтай товч дээр хүлээж байгаа шүү.", "Okay! The story mode waits under the ••• button, the one with Chinggis Khaan's face.", replay ? "story" : null);
        if (replay) yield return Finish(false, false);
    }
    /** Back from the story they chose in the tour: Bolormaa offers to carry on with it. */
    IEnumerator AfterStory()
    {
        dropBusy = true;
        yield return new WaitForSeconds(0.8f);
        var d = ShowDialog("guide", "laugh", Loc.L($"Түүхээс буцаад ирлээ, {V.name}! Ямар байв? Одоо Нутгаараа аялах уу?", $"You're back from history, {V.name}! How was it? Shall we tour the village now?"),
            new[] { Tx2("Тийм, явцгаая!", "Yes, let's go!").ToString(), Tx2("Дараа", "Later").ToString() }, false);
        while (!d.done) yield return null;
        dropBusy = false;
        if (d.picked == 0) StartTutorial(false);
        else { V.tutorial = true; V.Save(); }
    }

    IEnumerator Controls(bool touch)
    {
        yield return Say("happy", touch ? "Эхлээд алхаж үзье! Зүүн доод буланд байгаа джойстикоор алхана. Очих газраа товшсон ч болно." : "Эхлээд алхаж үзье! WASD эсвэл сум товчоор алхана. Газар дээр дарвал тийшээ очно.",
            touch ? "Let’s walk first! Use the joystick at the bottom left to walk. You can also just tap where to go." : "Let’s walk first! Use WASD or the arrow keys. You can also click the ground to walk there.");
        float w0 = walked;
        yield return Goal(Tx2("Алхаж үзээрэй", "Take a walk"), () => walked - w0 >= 6, () => $"{Mathf.Min(6, Mathf.FloorToInt(walked - w0))}/6");
        yield return Say("laugh", "Ёстой гоё! Чи хурдан хөлтэй юм аа!", "Great job! You’ve got quick feet!");
        yield return Say("tease", touch ? "Одоо үсэрцгээе! Баруун доод талын “Үсрэх” товчийг дар." : "Одоо үсэрцгээе! Space товч дар.", touch ? "Now let’s jump! Tap the “Jump” button at the bottom right." : "Now let’s jump! Press Space.", touch ? "jump" : null);
        float j0 = jumpT;
        yield return Goal(Tx2("Үсэр!", "Jump!"), () => jumpT > j0);
        glowKey = null;
        yield return Say("excited", "Вау, ямар өндөр үсрэв! Замд чулуу таарвал үсрээд давчихна.", "Whoa, what a jump! If a rock is in the way, you hop right over it.");
        yield return Say("neutral", touch ? "Яарч байвал “Гүйх” товчийг асаавал гүйнэ. Гэхдээ гүйхэд хүч зарцуулдаг шүү." : "Яарч байвал Shift товчийг дарж байгаад гүйнэ. Гэхдээ гүйхэд хүч зарцуулдаг шүү.",
            touch ? "In a hurry? Switch on the “Run” button to run. Running uses up energy, though." : "In a hurry? Hold Shift to run. Running uses up energy, though.", touch ? "run" : null);
        glowKey = null;
    }
    IEnumerator Questions()
    {
        var left = new System.Collections.Generic.List<(Tx q, Func<IEnumerator> a)>
        {
            (Tx2("Бөх яаж барилдах вэ?", "How do I wrestle?"), () => Seq(
                Say("proud", "Зүүн талд Наадмын талбай бий. Засуултай уулзаад барилд. Мэхээ сонгоод товч дарахад л болно.", "The Naadam arena is to the east. Talk to the herald, pick a move and press the button. That’s all!"),
                Say("happy", "Мэх бүр өөр: зарим нь цагаа тааруулах, зарим нь хурдан дарах, зарим нь дараад барих. Эхний даваанууд амархан шүү!", "Each move is a little game: tap on time, tap fast, or hold and let go. The first rounds are easy!"),
                Say("wink", "Мэх мэдэхгүй бол «Барилдаж сурах»-ыг сонго. Бүгдийг нь энгийнээр тайлбарлаад, багштай дасгал хийлгэнэ.", "Not sure about the moves? Choose “Learn to wrestle”. Everything is explained simply, then you practise with a coach."),
                Say("tease", "Ялсан хойноо бүргэд шиг дэвэхээ мартуузай!", "And when you win, don’t forget the eagle dance!"))),
            (Tx2("Морь яаж унах вэ?", "How do I ride a horse?"), () => Seq(
                Say("happy", "Гудамжны зүүн талд морины уяа бий. Морио сонгоод унана, тэгээд Нутгаар давхина!", "There’s a horse line on the east side of the street. Pick a horse, hop on and gallop around!"),
                Say("wink", "Уралдаанд Shift (эсвэл “Чү!” товч) дарж морио давхиулна. Харин морь ядарвал бага зэрэг амраагаарай.", "In a race, hold Shift (or the “Chü!” button) to sprint. Let your horse rest a little when it gets tired."))),
            (Tx2("Юунаас эхлэх вэ?", "Where should I start?"), () => Seq(
                Say("think", "Толгой дээрээ шар “!” тэмдэгтэй хүмүүсийг хай. Тэд чамд даалгавар өгнө.", "Look for people with a yellow “!” over their heads. They have tasks for you."),
                Say("happy", "Эмээгийн гэрт ч зочлоорой. Эмээ ёс заншил зааж, ааруул өгдөг!", "Visit Grandma’s ger too. She teaches good manners and gives you aaruul!"))),
            (Tx2("Чи хэн бэ?", "Who are you?"), () => Seq(
                Say("proud", "Би Болормаа! Хөдөө өссөн, морь унах, бөх барилдахыг үзэх их дуртай.", "I’m Bolormaa! I grew up in the countryside, and I love riding and watching wrestling."),
                Say("shy", "Нууцхан хэлэхэд... Басарыг надаас ч илүү ухаантай гэж боддог. Түүнд битгий хэлээрэй!", "Between you and me... I think Basar is even smarter than me. Don’t tell him!"))),
        };
        bool first = true;
        while (true)
        {
            var opts = left.Select(q => q.q).Append(Tx2("Асуулт алга, тоглоцгооё!", "No more questions, let’s play!")).ToArray();
            yield return first ? Ask("happy", "Надаас юу асуумаар байна?", "What would you like to ask me?", opts) : Ask("wink", "Өөр юу асуух вэ?", "Anything else?", opts);
            first = false;
            if (askAnswer < 0 || askAnswer >= left.Count) yield break;
            var a = left[askAnswer].a; left.RemoveAt(askAnswer);
            yield return a();
            if (left.Count == 0) yield break;
        }
    }
    static IEnumerator Seq(params IEnumerator[] steps) { foreach (var s in steps) yield return s; }
    IEnumerator Finish(bool full, bool skipped)
    {
        bool firstTime = !V.tutorial;
        V.tutorial = true; V.tips = true; V.Save();
        if (full && firstTime && !skipped)
        {
            var got = Reward(30, 30); Confetti(50); Sfx.Win();
            yield return Say("laugh", $"Чи бэлэн боллоо! Энэ миний бэлэг: +{got.coins} зоос. Сайхан тоглоорой!", $"You’re all set! Here’s a gift: +{got.coins} coins. Have fun!");
        }
        else if (!skipped) yield return Say("happy", "За, сайхан тоглоорой! Би энд л байна.", "Okay, have fun! I’m right here.");
        StopTutorial();
    }

    // ================================================================ Bolormaa drops by
    static readonly Tx[] TIPS =
    {
        new Tx("Ядарсан уу? Хоолны товч дээр дараад нэг юм идчих. Хүч чинь буцаад дүүрнэ шүү.", "Feeling tired? Tap the food button and grab a snack. Your energy fills right back up."),
        new Tx("Эмээд зочлохоо мартаагүй биз? Ёсоор мэндэлбэл ааруул өгнө шүү, хи хи.", "Have you visited Grandma yet? Greet her nicely and she’ll share her aaruul, hee hee."),
        new Tx("Морины уяанаас нэг морь аваад давхиж үзээч. Салхи нүүрэнд чинь ч гоё байдаг шүү!", "Why not take a horse from the hitching line for a ride? The wind on your face feels amazing!"),
        new Tx("Ямар нэг юм сонирхож байвал Басараас асуугаарай. Тэр бүхнийг мэддэг, надаас ч илүү!", "Curious about something? Ask Basar. He knows everything, even more than me!"),
        new Tx("3D товчийг дарвал Нутгаа 3D-ээр харна. Би анх үзэхдээ их гайхаж билээ.", "Tap the 3D button to see Nutag in 3D. It amazed me the first time I saw it."),
        new Tx("Наадмын талбайд Засуул чамайг хүлээж байна. Бөхийн мэх сурмаар байна уу?", "The herald is waiting at the Naadam field. Want to learn a wrestling move?"),
    };
    static readonly string[] TIP_FACES = { "worried", "happy", "excited", "shy", "curious", "proud" }; // a face that fits each tip
    const float GAP = 5 * 60, GAP_SHORT = 2 * 60;
    float dropNext = 3 * 60, dropCheck; bool dropBusy;
    void TutorialTick(float dt)
    {
        DialogTick(dt);
        if (V == null) return;
        if (V.tutorialSeen == 0 && !V.tutorial && tutRun == 0 && modal == null && Time.timeSinceLevelLoad > 1.5f) { StartTutorial(false); return; } // the first visit
        if (storyFromTutorial && modal == null && tutRun == 0 && !dropBusy) { storyFromTutorial = false; StartCoroutine(AfterStory()); return; }
        dropCheck -= dt; if (dropCheck > 0) return; dropCheck = 3;
        if (!(V.tutorial || V.tutorialSeen > 0) || tutRun != 0 || dropBusy || race != null || battle != null || dialog != null || modal != null) return;
        float now = Time.realtimeSinceStartup; long ms = SaveData.NowMs(); if (now < dropNext || ms - V.nominLast < GAP_SHORT * 1000) return;
        bool Said(string k) => V.nominSaid.Exists(x => x.k == k);
        void Mark(string k, float gap) { V.nominSaid.Add(new KV { k = k, v = ms }); if (V.nominSaid.Count > 200) V.nominSaid.RemoveAt(0); V.nominLast = ms; dropNext = now + gap; V.Save(); }
        var qs = Quests(); string nm = V.name;
        var ready = qs.FirstOrDefault(q => !q.done && q.p >= Rules.QUESTS[q.k].n && !Said("ready:" + V.questDay + q.k));
        if (ready != null)
        {
            Mark("ready:" + V.questDay + ready.k, GAP_SHORT); var t = Rules.QUESTS[ready.k].t;
            StartCoroutine(DropBy("laugh", new Tx($"{nm}, чи “{t.mn}”-ийг хийчихжээ! Ёстой шаргуу юм аа. Даалгаврын товчоос шагналаа аваарай.", $"{nm}, you finished “{t.en}”! So proud of you. Your reward is waiting under the quests button."), "quests")); return;
        }
        if (ms - V.nominLast < GAP * 1000) return;
        if (V.questsSeen != V.questDay && !Said("new:" + V.questDay))
        {
            Mark("new:" + V.questDay, GAP); var t = Rules.QUESTS[qs[0].k].t;
            StartCoroutine(DropBy("wink", new Tx($"Өглөөний мэнд, {nm}! Өнөөдөр шинэ даалгаврууд гарсан шүү. Нэг нь “{t.mn}”. Хамт хийцгээе үү?", $"Hi again, {nm}! There are new quests today. One is “{t.en}”. Shall we give it a go?"), "quests")); return;
        }
        var npc = Rules.NPCS.FirstOrDefault(n => NpcMark(n.id) == "!" && !Said("npc:" + n.id));
        if (npc != null)
        {
            Mark("npc:" + npc.id, GAP); var o = World.objects.FirstOrDefault(x => x.kind == "npcq" && x.id == npc.id);
            StartCoroutine(DropBy("curious", new Tx($"Сонсоо, {npc.name.mn} чамаас тусламж хүсэж байна. Шар сум замыг заана, би энд л байна.", $"Psst, {npc.name.en} could use your help. The yellow arrow shows the way, and I’m right here if you need me."), null, o != null ? (o.x, o.y, npc.name.ToString()) : ((float, float, string)?)null)); return;
        }
        if (SaveData.Get(V.story, CHAPTERS[0].id) == 0 && SaveData.Get(V.story, CHAPTERS[0].id + ":done") == 0 && !Said("story"))
        {
            Mark("story", GAP);
            StartCoroutine(DropBy("excited", new Tx($"{nm}, Монголын түүхийг өөрөө амьдарч үзмээр байна уу? ••• товчны Чингис хааны зурагтай товчийг дараарай!", $"{nm}, want to live Mongolian history yourself? Tap the button with Chinggis Khaan's face under •••!"), "story")); return;
        }
        if (ms - V.nominLast > (15 * 60 + UnityEngine.Random.value * 10 * 60) * 1000) // a friendly tip, only now and then
        {
            int i = Array.FindIndex(TIPS, t => !Said("tip:" + Array.IndexOf(TIPS, t))); if (i < 0) i = UnityEngine.Random.Range(0, TIPS.Length);
            Mark("tip:" + i, GAP);
            StartCoroutine(DropBy(TIP_FACES[i], TIPS[i]));
        }
    }
    IEnumerator DropBy(string face, Tx text, string glow = null, (float, float, string)? target = null)
    {
        dropBusy = true; glowKey = glow; if (target != null) tutTarget = target;
        var d = ShowDialog("guide", face, text.ToString(), null, true, 5.6f);
        while (!d.done && dialog == d) yield return null;
        yield return new WaitForSeconds(4); if (glowKey == glow) glowKey = null;
        dropBusy = false;
        if (target != null) { yield return new WaitForSeconds(16); if (tutRun == 0 && tutTarget.Equals(target)) tutTarget = null; }
    }
}
