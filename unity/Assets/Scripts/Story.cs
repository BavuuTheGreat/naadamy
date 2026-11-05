// Түүхийн аялал, the story mode: live Temüjin's life as the Secret History of the Mongols tells it. A chapter is a script of
// cinematic parts (painted panoramas that pan, people acting in them, a title card, Temüjin's narration with his portrait)
// and playable stages (a battle, a day by the Onon, an escape at night). Everything is drawn into a 320×180 pixel picture
// scaled to the screen; the words are drawn over it in the game's own fonts. The art is made by Tools/art/story_bg.py and
// Tools/art/portrait.py. Progress is saved after every part, so a chapter can be continued where it was left.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed partial class Game
{
    public void OpenStory() => Open(new StoryMenu());

    // ================================================================ the people of the story
    public sealed class Who { public Tx name; public string voice; }
    public static readonly Dictionary<string, Who> WHO = new Dictionary<string, Who>
    {
        ["temujin_child"] = new Who { name = new Tx("Тэмүжин", "Temüjin"), voice = "child" },
        ["temujin"] = new Who { name = new Tx("Тэмүжин", "Temüjin"), voice = "temujin" },
        ["chinggis"] = new Who { name = new Tx("Чингис хаан", "Chinggis Khaan"), voice = "chinggis" },
        ["borte"] = new Who { name = new Tx("Бөртэ", "Börte"), voice = "woman" },
        ["hoelun"] = new Who { name = new Tx("Өэлүн эх", "Mother Hö'elün"), voice = "woman" },
        ["yesugei"] = new Who { name = new Tx("Есүхэй баатар", "Yesügei Baatar"), voice = "man" },
        ["dei_sechen"] = new Who { name = new Tx("Дэй сэцэн", "Dei Sechen"), voice = "elder" },
        ["jamukha"] = new Who { name = new Tx("Жамуха", "Jamukha"), voice = "man" },
        ["toghrul"] = new Who { name = new Tx("Тоорил хан", "Toghrul Khan"), voice = "elder" },
        ["sorkhan_shira"] = new Who { name = new Tx("Сорхон шар", "Sorkhan-Shira"), voice = "elder" },
        ["boorchu"] = new Who { name = new Tx("Боорчу", "Bo'orchu"), voice = "child" },
        ["merkit"] = new Who { name = new Tx("Мэргид дайчин", "Merkit warrior"), voice = "man" },
    };
    static readonly Dictionary<string, PxCanvas> storyFaces = new Dictionary<string, PxCanvas>();
    /** A story portrait (64×64): expr neutral | smile | stern | sad | shout | surprised; col 0 mouth shut, 1 open, 2 blinking. */
    public static PxCanvas StoryFace(string id, string expr, int col)
    {
        string key = id + expr + col; if (storyFaces.TryGetValue(key, out var c)) return c;
        var m = Data.Get("story_faces"); int s = m["size"].I, row = Mathf.Max(0, m["exprs"].Strings().IndexOf(expr));
        c = new PxCanvas(s, s); c.BlitScaled(Art.Get("story_face_" + id), 0, 0, s, s, false, col * s, row * s, s, s);
        return storyFaces[key] = c;
    }

    // ================================================================ choosing a chapter
    public sealed class StoryChapter { public string id; public Tx name, years, blurb; public Func<StoryScene, IEnumerator>[] parts; }
    public static List<StoryChapter> CHAPTERS => chapters ??= new List<StoryChapter> { StoryCh1.Chapter() };
    static List<StoryChapter> chapters;
    int StoryPart(string ch) => (int)SaveData.Get(V.story, ch);
    sealed class StoryMenu : Modal
    {
        public override void Draw(Game g)
        {
            GUILayout.BeginHorizontal(); g.IconAt(StoryFace("chinggis", "smile", 0), 56); GUILayout.BeginVertical();
            g.Title(Loc.L("Түүхийн аялал", "Story mode")); g.Small(Loc.L("Монголын түүхийг өөрийн нүдээр", "Mongolian history, first hand"));
            GUILayout.EndVertical(); GUILayout.EndHorizontal();
            g.Text(Loc.L("Тэмүжин хэрхэн Чингис хаан болсныг “Монголын нууц товчоо”-нд бичсэнээр нь амьдарч үзээрэй. Тулалдаанд ор, түүний хүүхэд насыг туулж, түүхийн жинхэнэ баримтуудыг сур.",
                "Live how Temüjin became Chinggis Khaan, as the Secret History of the Mongols tells it. Ride into battle, live through his childhood and learn the real history."));
            foreach (var ch in CHAPTERS)
            {
                int part = g.StoryPart(ch.id); bool done = SaveData.Get(g.V.story, ch.id + ":done") > 0;
                g.Row(StoryFace("temujin", "stern", 0), ch.name.ToString(), ch.years + (done ? " · " + Loc.L("Дууссан", "Finished") + " √" : part > 0 ? " · " + Loc.L($"{part}/{ch.parts.Length}-р хэсэг", $"part {part}/{ch.parts.Length}") : ""));
                g.EndRow();
                g.Small(ch.blurb.ToString());
                if (part > 0 && part < ch.parts.Length && g.Btn(Loc.L("Үргэлжлүүлэх", "Continue"), true, 44)) { var c2 = ch; int p2 = part; g.Later(() => g.PlayStory(c2, p2)); }
                if (g.Btn(part > 0 ? Loc.L("Эхнээс нь", "From the start") : Loc.L("Эхлэх", "Begin"), part == 0, 44)) { var c2 = ch; g.Later(() => g.PlayStory(c2, 0)); }
            }
            g.Small(Loc.L("Удахгүй: 2-р бүлэг. Түүхийн бүх баримтыг Нууц товчооноос авсан.", "Coming next: chapter 2. Every story fact comes from the Secret History."));
        }
        public override void Press(Game g) => g.Close();
    }
    public void PlayStory(StoryChapter ch, int from) { CloseAll(); Open(new StoryScene(this, ch, from)); }

    // ================================================================ the scene: cinematics and stages
    public abstract class Stage
    {
        public bool done, won; public float camX; public int W = 960;
        /** The on-screen buttons this stage wants, in order from the right edge: (id, label). */
        public virtual (string id, Tx label)[] Buttons => Array.Empty<(string, Tx)>();
        public abstract void Tick(StoryScene s, float dt);
        public abstract void Draw(StoryScene s, PxCanvas c);
        /** Words over the picture (hearts, goals, hints), in UI units inside the safe area. */
        public virtual void Hud(StoryScene s, float vw, float vh) { }
    }
    public sealed class Actor
    {
        public string id; public Look look; public bool mounted, moving, run; public float x, y, tx, ty, speed = 40, z; public int face = 2; public bool hidden;
        public string bubble; public float bubbleT;
    }
    sealed class Speck { public float x, y, vx, vy, life, max; public Color32 col; public int size; public bool world, glow; }

    public sealed class StoryScene : Modal
    {
        public const int VW = 320, VH = 180;
        public override bool FullScreen => true;
        public override bool CanEscape => true;   // Esc leaves; the part you were in is saved
        public readonly Game g; readonly StoryChapter ch; int partIx;
        readonly Live live = new Live(VW, VH);
        // the place
        string bgId; PxCanvas[] layers; public float camX; float camA, camB, camT0 = -9, camDur; public float shake;
        public string fx; readonly List<Speck> parts = new List<Speck>(); float fxAcc;
        public readonly List<Actor> actors = new List<Actor>();
        public Stage stage;
        // what is shown over it
        sealed class Line { public string who, expr, text; public float shown, born; public bool done; public string[] choices; public int picked = -1; public AudioClip clip; } // clip: the line spoken (Voice.cs)
        Line line; string titleBig, titleSmall; float titleT0 = -99, titleDur;
        string cardTitle, cardText, cardSrc; bool cardDone = true;
        float lb, lbTo = 1, fade = 1, fadeTo; Color fadeCol = Color.black;
        public bool skipping; Coroutine co; public int lastPick;
        // controls: the fixed joystick at the bottom left (GameUI.StickHome) and the stage's buttons
        public Vector2 stick; int stickId = -999;
        readonly Dictionary<string, Rect> btnRects = new Dictionary<string, Rect>();
        readonly HashSet<string> held = new HashSet<string>(), pressed = new HashSet<string>();
        readonly Dictionary<int, string> pointerBtn = new Dictionary<int, string>();
        Rect exitR, skipR; bool tapped;

        public StoryScene(Game g, StoryChapter ch, int from) { this.g = g; this.ch = ch; partIx = Mathf.Clamp(from, 0, ch.parts.Length - 1); co = g.StartCoroutine(Run()); }
        IEnumerator Run()
        {
            while (partIx < ch.parts.Length)
            {
                if (partIx > g.StoryPart(ch.id)) { SaveData.Set(g.V.story, ch.id, partIx); g.V.Save(); }   // the furthest part reached (it travels in the cloud save)
                skipping = false; stage = null; actors.Clear(); line = null; cardDone = true;
                yield return ch.parts[partIx](this);
                partIx++;
            }
            bool first = SaveData.Get(g.V.story, ch.id + ":done") == 0;
            SaveData.Set(g.V.story, ch.id + ":done", 1); SaveData.Set(g.V.story, ch.id, ch.parts.Length); g.V.Save();
            if (first) { var got = g.Reward(80, 120); g.Toast(Loc.L($"Бүлэг дууслаа! +{got.coins} зоос", $"Chapter finished! +{got.coins} coins")); g.Confetti(90); Sfx.Win(); }
            g.Later(g.Close);
        }
        public override void Closed(Game g) { if (co != null) g.StopCoroutine(co); Voice.Stop(); }

        // ---------------------------------------------------------------- script commands
        public void Bg(string id, float cam = 0, string fx = null) { bgId = id; layers = Enumerable.Range(0, 4).Select(i => Art.Get($"story_{id}_{i}")).ToArray(); camX = camA = camB = cam; camT0 = -9; this.fx = fx; parts.Clear(); }
        /** Pans the camera to x over dur seconds (smoothly), without waiting. */
        public void Pan(float to, float dur) { camA = camX; camB = to; camT0 = Time.time; camDur = Mathf.Max(0.01f, dur); }
        public IEnumerator Wait(float s) { float t = 0; while (t < s && !skipping) { t += Time.deltaTime; yield return null; } }
        public IEnumerator Fade(bool toBlack, float dur = 0.8f, Color? col = null)
        {
            fadeCol = col ?? Color.black; fadeTo = toBlack ? 1 : 0;
            if (skipping) { fade = fadeTo; yield break; }
            float from = fade, t = 0; while (t < dur) { t += Time.deltaTime; fade = Mathf.Lerp(from, fadeTo, t / dur); yield return null; }
            fade = fadeTo;
        }
        public void Letterbox(bool on) => lbTo = on ? 1 : 0;
        public IEnumerator Title(Tx big, Tx small, float dur = 3.2f)
        {
            if (skipping) yield break;
            titleBig = big.ToString(); titleSmall = small.ToString(); titleT0 = Time.time; titleDur = dur; Sfx.Chime();
            yield return Wait(dur); titleT0 = -99;
        }
        public IEnumerator Say(string who, string expr, string mn, string en)
        {
            if (skipping) yield break;
            var l = line = new Line { who = who, expr = expr, text = Loc.L(mn, en), born = Time.time, clip = Voice.Speak(who, mn) };
            while (!l.done && !skipping) yield return null;
            if (l.clip != null) Voice.Stop();
            if (line == l) line = null;
        }
        /** A question with answers; lastPick is the answer's index. */
        public IEnumerator Ask(string who, string expr, Tx q, params Tx[] answers)
        {
            var l = line = new Line { who = who, expr = expr, text = q.ToString(), born = Time.time, choices = answers.Select(a => a.ToString()).ToArray(), clip = Voice.Speak(who, q.mn) };
            while (!l.done) yield return null;
            if (l.clip != null) Voice.Stop();
            lastPick = l.picked; if (line == l) line = null;
        }
        /** A parchment card with a fact from the sources, and where it is written. */
        public IEnumerator Fact(Tx title, Tx text, string source)
        {
            if (skipping) yield break;
            cardTitle = title.ToString(); cardText = text.ToString(); cardSrc = source; cardDone = false; Sfx.Pop();
            while (!cardDone && !skipping) yield return null;
            cardDone = true;
        }
        /** Hands the screen to a playable stage until it is done; returns when it is. */
        public IEnumerator Play(Stage st)
        {
            skipping = false; line = null; Letterbox(false); stage = st; held.Clear(); pressed.Clear();
            while (!st.done) yield return null;
            stage = null; held.Clear(); pressed.Clear(); stick = Vector2.zero; stickId = -999;
        }
        public Actor Put(string id, Look look, float x, float y, int face = 2, bool mounted = false)
        {
            actors.RemoveAll(a => a.id == id);
            var a = new Actor { id = id, look = look, x = x, y = y, tx = x, ty = y, face = face, mounted = mounted }; actors.Add(a); return a;
        }
        public Actor Get(string id) => actors.FirstOrDefault(a => a.id == id);
        public void Walk(string id, float x, float y, float speed = 40, bool run = false) { var a = Get(id); if (a == null) return; a.tx = x; a.ty = y; a.speed = speed; a.run = run; }
        public void Bubble(string id, string text) { var a = Get(id); if (a != null) { a.bubble = text; a.bubbleT = Time.time; } }

        // ---------------------------------------------------------------- input
        public bool Held(string b) => held.Contains(b) || KeyHeld(b);
        public bool Pressed(string b) => pressed.Contains(b);
        static readonly Dictionary<string, KeyCode[]> KEYS = new Dictionary<string, KeyCode[]>
        {
            ["strike"] = new[] { KeyCode.E, KeyCode.Space },           // melee: E (new standard) + Space on keyboards without an E (and advances dialog, same as before)
            ["shoot"] = new[] { KeyCode.R },                           // arrow: R (new standard)
            ["dash"] = new[] { KeyCode.LeftShift, KeyCode.RightShift },// Shift still dodges; a short burst with brief i-frames
            ["roll"] = new[] { KeyCode.C },                            // C rolls: a longer tumble with a long i-frame window and a bigger cooldown
            ["rally"] = new[] { KeyCode.T },                           // moved off R so Arrow owns R
            ["act"] = new[] { KeyCode.E, KeyCode.Return }, ["call"] = new[] { KeyCode.E, KeyCode.Return },
            ["hide"] = new[] { KeyCode.H, KeyCode.E }, ["fish"] = new[] { KeyCode.E, KeyCode.Space },
        };
        bool KeyHeld(string b) => KEYS.TryGetValue(b, out var ks) && ks.Any(Input.GetKey);
        public Vector2 Move()
        {
            float x = g.KeyX(), y = g.KeyY();
            var v = new Vector2(x, y); if (v.sqrMagnitude > 1) v.Normalize();
            return v.sqrMagnitude > 0.01f ? v : stick;
        }
        public override void Key(Game g)
        {
            foreach (var kv in KEYS) if (kv.Value.Any(Input.GetKeyDown)) pressed.Add(kv.Key);
            if (stage == null && (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.E))) Advance();
        }
        public override void Press(Game g) { }
        public override void Pointer(Game g, int id, Vector2 pos, bool down, bool up)
        {
            var gp = new Vector2(pos.x, Screen.height - pos.y);
            if (down)
            {
                if (exitR.Contains(gp)) { g.Later(g.Close); return; }
                if (skipR.Contains(gp) && stage == null) { skipping = true; Sfx.Tap(); return; }
                foreach (var kv in btnRects) if (kv.Value.Contains(gp)) { pointerBtn[id] = kv.Key; held.Add(kv.Key); pressed.Add(kv.Key); return; }
                if (stage != null && line == null && Game.TouchUI && g.NearStick(gp)) { stickId = id; stick = g.StickAt(gp); return; }
                if (stage == null && line?.choices == null) tapped = true;
                else if (stage == null && !cardDone) tapped = true;
            }
            if (id == stickId)
            {
                stick = g.StickAt(gp);
                if (up) { stickId = -999; stick = Vector2.zero; }
            }
            if (up && pointerBtn.TryGetValue(id, out var b)) { held.Remove(b); pointerBtn.Remove(id); }
        }
        void Advance()
        {
            if (!cardDone) { cardDone = true; Sfx.Tap(); return; }
            var l = line; if (l == null || l.choices != null) return;
            if (l.shown < l.text.Length) { l.shown = l.text.Length; return; }
            l.done = true; Sfx.Tap();
        }

        // ---------------------------------------------------------------- every frame
        public override void Tick(Game g, float dt)
        {
            if (tapped) { tapped = false; Advance(); }
            if (camT0 > 0) { float t = Mathf.Clamp01((Time.time - camT0) / camDur); t = t * t * (3 - 2 * t); camX = Mathf.Lerp(camA, camB, t); }
            lb = Mathf.MoveTowards(lb, stage != null ? 0 : lbTo, dt * 2);
            shake = Mathf.Max(0, shake - dt * 8);
            // the people acting in a cinematic walk to where they are sent
            foreach (var a in actors)
            {
                float dx = a.tx - a.x, dy = a.ty - a.y, d = Mathf.Sqrt(dx * dx + dy * dy); a.moving = d > 0.5f;
                if (a.moving) { float s = Mathf.Min(d, a.speed * dt); a.x += dx / d * s; a.y += dy / d * s; if (Mathf.Abs(dx) > 0.3f) a.face = dx < 0 ? 1 : 2; }
            }
            var l = line;
            if (l != null && l.shown < l.text.Length)
            {
                int before = (int)l.shown; char c = l.text[Mathf.Min(before, l.text.Length - 1)];
                float sp = 38; if (".!?…".IndexOf(c) >= 0) sp /= 6; else if (",—;:".IndexOf(c) >= 0) sp /= 3;
                if (l.clip != null) sp = l.text.Length / Mathf.Max(0.5f, l.clip.length * 0.9f); // spoken: the words keep pace with the voice
                l.shown = Mathf.Min(l.text.Length, l.shown + dt * sp);
                string v = WHO.TryGetValue(l.who, out var w) ? w.voice : "guide";
                if (l.clip == null) for (int i = before; i < (int)l.shown; i++) if (char.IsLetterOrDigit(l.text[i]) && i % 3 == 0) Sfx.Blip(v, 1 + Mathf.Sin(i * 1.3f) * 0.05f);
            }
            if (stage != null) { stage.Tick(this, dt); camX = stage.camX; }
            Weather(dt);
            for (int i = parts.Count - 1; i >= 0; i--)
            {
                var p = parts[i]; p.life -= dt; if (p.life <= 0) { parts.RemoveAt(i); continue; }
                p.x += p.vx * dt; p.y += p.vy * dt;
            }
            pressed.Clear();
        }
        /** The weather of the place: drifting embers, snow, falling larch needles, fireflies, steppe dust. */
        void Weather(float dt)
        {
            if (fx == null) return;
            float rate = fx == "embers" ? 10 : fx == "snow" ? 30 : fx == "leaves" ? 6 : fx == "fireflies" ? 3 : fx == "dust" ? 8 : 0;
            fxAcc += rate * dt;
            while (fxAcc >= 1 && parts.Count < 160)
            {
                fxAcc -= 1; var r = UnityEngine.Random.value;
                switch (fx)
                {
                    case "embers": parts.Add(new Speck { x = UnityEngine.Random.Range(0, VW), y = VH + 2, vx = UnityEngine.Random.Range(-6, 10), vy = -UnityEngine.Random.Range(14, 30), life = 6, max = 6, col = new Color32(255, (byte)(140 + r * 80), 60, 255), size = 1, glow = true }); break;
                    case "snow": parts.Add(new Speck { x = UnityEngine.Random.Range(-20, VW), y = -2, vx = UnityEngine.Random.Range(4, 14), vy = UnityEngine.Random.Range(12, 26), life = 12, max = 12, col = new Color32(240, 244, 255, 255), size = r < 0.2f ? 2 : 1 }); break;
                    case "leaves": parts.Add(new Speck { x = UnityEngine.Random.Range(0, VW + 40), y = -2, vx = -UnityEngine.Random.Range(10, 26), vy = UnityEngine.Random.Range(10, 22), life = 14, max = 14, col = r < 0.5f ? new Color32(217, 166, 74, 255) : new Color32(168, 120, 46, 255), size = 1 }); break;
                    case "fireflies": parts.Add(new Speck { x = UnityEngine.Random.Range(0, VW), y = UnityEngine.Random.Range(110, 170), vx = UnityEngine.Random.Range(-5, 5), vy = UnityEngine.Random.Range(-4, 2), life = 5, max = 5, col = new Color32(220, 255, 140, 255), size = 1, glow = true }); break;
                    case "dust": parts.Add(new Speck { x = -2, y = UnityEngine.Random.Range(120, 178), vx = UnityEngine.Random.Range(30, 60), vy = UnityEngine.Random.Range(-3, 3), life = 9, max = 9, col = new Color32(230, 210, 160, 120), size = 1 }); break;
                }
            }
        }
        public void Burst(float wx, float wy, Color32 col, int n, float speed = 40, bool glow = false)
        {
            for (int i = 0; i < n; i++) { float a = UnityEngine.Random.value * Mathf.PI * 2, s = speed * (0.4f + UnityEngine.Random.value * 0.6f); parts.Add(new Speck { x = wx, y = wy, vx = Mathf.Cos(a) * s, vy = Mathf.Sin(a) * s - speed * 0.3f, life = 0.45f, max = 0.45f, col = col, size = 1, world = true, glow = glow }); }
        }

        // ---------------------------------------------------------------- drawing
        public override void Draw(Game g)
        {
            var c = live.c; float t = Time.time;
            int sx = shake > 0 ? Mathf.RoundToInt(UnityEngine.Random.Range(-1f, 1f) * shake * 2) : 0;
            if (layers != null)
            {
                int SW = stage != null ? stage.W : 960;
                for (int i = 0; i < 4; i++)
                {
                    var L = layers[i]; if (L.W <= 1) continue;
                    float k = SW > VW ? (L.W - VW) / (float)(SW - VW) : 0;
                    if (i == 0) c.CopyFrom(L); else c.BlitRow(L, Mathf.RoundToInt(camX * Mathf.Min(1, k)) - sx);
                }
            }
            else c.Fill(new Color32(8, 6, 14, 255));
            // people, then the stage's own world
            foreach (var a in actors.OrderBy(a => a.y)) if (!a.hidden) DrawActor(c, a, t);
            stage?.Draw(this, c);
            foreach (var p in parts)
            {
                float px = p.world ? p.x - camX : p.x; byte al = (byte)(p.col.a * Mathf.Clamp01(p.life / p.max * 2));
                var col = new Color32(p.col.r, p.col.g, p.col.b, al);
                if (fx == "fireflies" && !p.world) col.a = (byte)(al * (0.5f + 0.5f * Mathf.Sin((t + p.x) * 6)));
                for (int yy = 0; yy < p.size; yy++) for (int xx = 0; xx < p.size; xx++) c.Put(Mathf.RoundToInt(px) + xx, Mathf.RoundToInt(p.y) + yy, col);
                if (p.glow) { var gl = new Color32(col.r, col.g, col.b, (byte)(col.a / 4)); c.Put(Mathf.RoundToInt(px) - 1, Mathf.RoundToInt(p.y), gl); c.Put(Mathf.RoundToInt(px) + 1, Mathf.RoundToInt(p.y), gl); }
            }
            // the whole picture on the screen, as big as fits (black bars on screens of another shape): the ground you walk on is never cut off
            g.Box(new Rect(0, 0, Screen.width, Screen.height), Color.black);
            float K = Mathf.Min(Screen.width / (float)VW, Screen.height / (float)VH);
            var pic = new Rect((Screen.width - VW * K) / 2, (Screen.height - VH * K) / 2, VW * K, VH * K);
            live.ShowAt(pic); picRect = pic; picK = K;
            foreach (var a in actors) if (a.bubble != null && Time.time - a.bubbleT < 3.4f) SayOver(g, a.x, a.y - (a.mounted ? 40 : 30), a.bubble);
            Overlay(g, pic, K);
        }
        public void DrawActor(PxCanvas c, Actor a, float t) => DrawPerson(c, a.look, a.x - camX, a.y - a.z, a.face, a.moving, a.mounted, t, a.run);
        Rect picRect; float picK = 1;
        /** Where a point of the picture is on the screen (GUI coordinates), for words over people. */
        public Vector2 ToScreen(float px, float py) => new Vector2(picRect.x + px * picK, picRect.y + py * picK);
        /** A speech bubble over someone in the picture; wx is in the stage's world. */
        public void SayOver(Game g, float wx, float wy, string text, Color? col = null)
        {
            g.SpeechBubble(ToScreen(wx - camX, wy), text, 220, col?.a ?? 1);
        }
        /** Draws someone standing (or riding) with their feet at (x, y) in the picture. */
        public static void DrawPerson(PxCanvas c, Look look, float x, float y, int face, bool moving, bool mounted, float t, bool fast = false, bool blink = false)
        {
            if (blink) return;
            int X = Mathf.RoundToInt(x), Y = Mathf.RoundToInt(y);
            for (int i = -5; i <= 5; i++) c.Put(X + i, Y, new Color32(20, 14, 10, (byte)(Mathf.Abs(i) > 3 ? 40 : 70)));   // a soft shadow
            if (mounted)
            {
                int hf = moving ? 1 + (int)(t * (fast ? 17 : 12)) % 4 : 0; var hs = Px.Horse(string.IsNullOrEmpty(look.coat) ? "bay" : look.coat, hf, true);
                for (int i = -10; i <= 10; i++) c.Put(X + i, Y, new Color32(20, 14, 10, 50));
                var rl = look.Copy(); rl.pet = null; rl.ride = null;
                c.Blit(Px.Char(rl, face == 1 ? 1 : 2, 0), X - 8, Y - 37);
                c.Blit(hs, X - 14, Y - 22, face == 1);
            }
            else
            {
                int fr = moving ? 1 + (int)(t * (fast ? 13 : 8)) % 2 : 0;
                c.Blit(Px.Char(look, face, fr), X - 8, Y - 27);
            }
        }

        // ---------------------------------------------------------------- words, buttons and cards over the picture
        void Overlay(Game g, Rect pic, float K)
        {
            var safe = Screen.safeArea; float ox = safe.x, oy = Screen.height - safe.yMax, U = g.UiScale, vw = safe.width / U, vh = safe.height / U;
            // letterbox bars for the cinematics
            if (lb > 0.01f) { float bh = Screen.height * 0.1f * lb; g.Box(new Rect(0, 0, Screen.width, bh), Color.black); g.Box(new Rect(0, Screen.height - bh, Screen.width, bh), Color.black); }
            GUI.BeginGroup(new Rect(ox, oy, safe.width, safe.height));
            btnRects.Clear();
            if (stage != null) { stage.Hud(this, vw, vh); StageButtons(g, vw, vh, ox, oy); }
            // a title card: the year and the place, fading in and out
            if (titleT0 > 0)
            {
                float tt = Time.time - titleT0, a = Mathf.Clamp01(tt / 0.6f) * Mathf.Clamp01((titleDur - tt) / 0.6f);
                var big = new GUIStyle(g.sTitle) { fontSize = Mathf.RoundToInt(U * 34), alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(1, 0.93f, 0.75f, a) } };
                var sm = new GUIStyle(g.sPixel) { fontSize = PxSize(U * 16), alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.97f, 0.78f, 0.27f, a) } };
                g.Box(g.URect(0, vh * 0.36f, vw, 96), new Color(0, 0, 0, 0.45f * a));
                GUI.Label(g.URect(0, vh * 0.36f + 8, vw, 50), titleBig, big);
                if (titleSmall != null) GUI.Label(g.URect(0, vh * 0.36f + 58, vw, 28), titleSmall, sm);
            }
            if (line != null) DrawLine(g, line, vw, vh);
            if (!cardDone) DrawCard(g, vw, vh);
            // leave, and skip the cinematic
            exitR = new Rect(ox + U * 8, oy + U * 8, U * 40, U * 40);
            GUI.Box(g.URect(8, 8, 40, 40), "×", g.sBtn);
            skipR = Rect.zero;
            if (stage == null && (line != null || titleT0 > 0 || layers != null))
            {
                skipR = new Rect(ox + (vw - 128) * U, oy + U * 8, U * 120, U * 36);
                GUI.Box(g.URect(vw - 128, 8, 120, 36), Loc.L("Алгасах »", "Skip »"), g.sBtn);
            }
            GUI.EndGroup();
            if (fade > 0.001f) g.Box(new Rect(0, 0, Screen.width, Screen.height), new Color(fadeCol.r, fadeCol.g, fadeCol.b, fade));
        }
        void DrawLine(Game g, Line l, float vw, float vh)
        {
            float U = g.UiScale, w = Mathf.Min(vw - 20, 760), pic = Mathf.Min(120, vh * 0.26f), pad = 14, tx = pic + pad * 2, tw = w - tx - pad;
            var st = new GUIStyle(g.sLabel) { fontSize = Mathf.RoundToInt(U * 17), wordWrap = true, normal = { textColor = new Color(1, 0.97f, 0.9f) } };
            float th = st.CalcHeight(new GUIContent(l.text), tw * U) / U, n = l.choices?.Length ?? 0, oht = 0;
            for (int i = 0; i < n; i++) oht += Mathf.Max(38, g.sOpt.CalcHeight(new GUIContent(l.choices[i]), tw * U) / U) + 6; // an answer grows to fit its words
            float h = Mathf.Max(pic * 0.72f + pad, pad + 26 + th + (n > 0 ? 10 + oht : 20) + pad), x = vw / 2 - w / 2, y = vh - h - 10;
            // a dark box with a gold frame; the portrait stands on its left, a little taller than the box
            g.Box(g.URect(x - 2, y - 2, w + 4, h + 4), Px.Col("#e0b04a")); g.Box(g.URect(x, y, w, h), new Color(0.04f, 0.03f, 0.07f, 0.94f));
            // the story_face sheet's mouth-open and blink cells draw the head at slightly different pixel offsets than the base cell, so toggling creates a ghost that reads as an extra mouth / extra pair of eyes (same bug the dialog bust had). Freeze the face at the closed-mouth cell until the sheet is re-authored with pixel-perfect head placement across all three cells.
            if (l.who != null && WHO.ContainsKey(l.who))
            {
                var fr = g.URect(x + pad, y - pic * 0.28f, pic, pic);
                g.Box(new Rect(fr.x - U * 3, fr.y - U * 3, fr.width + U * 6, fr.height + U * 6), Px.Col("#e0b04a"));
                g.Box(fr, Px.Col(l.who == "merkit" ? "#3a1c22" : "#1c2440"));
                GUI.DrawTexture(fr, g.Tex(StoryFace(l.who, l.expr, 0)));
                GUI.Label(g.URect(x + tx, y + pad - 4, tw, 24), WHO[l.who].name.ToString().ToUpper(), new GUIStyle(g.sPixel) { alignment = TextAnchor.UpperLeft, fontSize = PxSize(U * 16), normal = { textColor = Px.Col("#f7c646") } });
            }
            string vis = l.text.Substring(0, Mathf.Min(l.text.Length, (int)l.shown));
            GUI.Label(g.URect(x + tx, y + pad + 22, tw, th + 4), vis + "<color=#00000000>" + l.text.Substring(vis.Length) + "</color>", st);
            if (n == 0 && l.shown >= l.text.Length) GUI.Label(g.URect(x + w - 30, y + h - 26, 20, 20), (int)(Time.time * 2) % 2 == 0 ? "▼" : "", new GUIStyle(st) { alignment = TextAnchor.MiddleCenter });
            if (n > 0 && l.shown >= l.text.Length)
            {
                float oy = y + pad + 26 + th + 10;
                for (int i = 0; i < n; i++)
                {
                    float oh = Mathf.Max(38, g.sOpt.CalcHeight(new GUIContent(l.choices[i]), tw * U) / U);
                    if (GUI.Button(g.URect(x + tx, oy, tw, oh), l.choices[i], g.sOpt)) { int k = i; g.Later(() => { l.picked = k; l.done = true; Sfx.Tap(); }); }
                    oy += oh + 6;
                }
            }
        }
        void DrawCard(Game g, float vw, float vh)
        {
            float w = Mathf.Min(vw - 40, 520), U = g.UiScale;
            var st = new GUIStyle(g.sLabel) { fontSize = Mathf.RoundToInt(U * 16), wordWrap = true };
            float th = st.CalcHeight(new GUIContent(cardText), (w - 40) * U) / U, h = th + 110, x = vw / 2 - w / 2, y = vh / 2 - h / 2;
            g.Box(g.URect(0, 0, vw, vh), new Color(0, 0, 0, 0.45f));
            GUI.Box(g.URect(x, y, w, h), GUIContent.none, g.sPanel);
            GUI.Label(g.URect(x + 20, y + 18, w - 40, 28), "<b>" + cardTitle + "</b>", new GUIStyle(g.sTitle) { fontSize = Mathf.RoundToInt(U * 20) });
            GUI.Label(g.URect(x + 20, y + 52, w - 40, th + 4), cardText, st);
            GUI.Label(g.URect(x + 20, y + h - 40, w - 40, 22), Loc.L("Эх сурвалж: Нууц товчоо ", "Source: The Secret History ") + cardSrc + "   ·   " + Loc.L("дарж үргэлжлүүл", "tap to go on"), g.sSmall);
        }
        void StageButtons(Game g, float vw, float vh, float ox, float oy)
        {
            float U = g.UiScale, big = 84, sm = 62, gap = 10, R = vw - 12, B = vh - 12;
            // the main action big in the corner, the others round it: left of it, above it, and up and to the left
            Rect[] at = { g.URect(R - big, B - big, big, big), g.URect(R - big - gap - sm, B - sm, sm, sm), g.URect(R - sm - (big - sm) / 2, B - big - gap - sm, sm, sm), g.URect(R - big - gap - sm, B - sm * 2 - gap, sm, sm) };
            var bs = stage.Buttons;
            for (int i = 0; i < bs.Length && i < at.Length; i++)
            {
                var (id, label) = bs[i]; var r = at[i]; bool on = Held(id);
                GUI.Box(r, GUIContent.none, on ? g.sPrimary : g.sBtn);
                GUI.Label(r, "<b>" + label + "</b>", new GUIStyle(g.sPixel) { fontSize = PxSize(U * (i == 0 ? 13 : 11)), alignment = TextAnchor.MiddleCenter, wordWrap = true, normal = { textColor = Px.Col("#4a2a12") } });
                btnRects[id] = new Rect(r.x + ox, r.y + oy, r.width, r.height);
            }
            // the fixed joystick, bottom left (GameUI.StickHome)
            if (Game.TouchUI && line == null) g.DrawStick(stickId != -999 ? stick : Vector2.zero, 0, new Vector2(ox, oy)); // hidden while someone talks: the box covers it
        }
    }
}
