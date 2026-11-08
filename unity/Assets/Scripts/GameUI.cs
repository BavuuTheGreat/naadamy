// The screen: touch joystick, tap-to-walk, the HUD, labels over people, toasts, confetti and the window system.
// Drawn with IMGUI so the game needs no UI packages; everything is scaled from a 480-unit-tall layout.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed partial class Game
{
    // ================================================================ pointer: on a phone the joystick sits fixed at the bottom left (StickHome); a quick tap walks there
    const float STICK_R = 56;
    sealed class Drag { public int id; public Vector2 p0, p; public bool on; }
    Drag drag; Vector2 stick; bool stickOn, runLock, sprintHeld; float lastTouch = -9, guiOX, guiOY;
    Rect sprintRect;
    List<Rect> uiRects = new List<Rect>(), uiNext = new List<Rect>();

    bool OverUI(Vector2 screen) { var g = new Vector2(screen.x, Screen.height - screen.y); foreach (var r in uiRects) if (r.Contains(g)) return true; return false; }

    void Pointer()
    {
        if (Input.touchCount > 0)
        {
            lastTouch = Time.unscaledTime;
            for (int i = 0; i < Input.touchCount; i++) { var t = Input.GetTouch(i); PointerEvent(t.fingerId, t.position, t.phase == TouchPhase.Began, t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled); }
        }
        else if (Time.unscaledTime - lastTouch > 0.5f) // touches also arrive as a fake mouse; ignore that one
            PointerEvent(-1, Input.mousePosition, Input.GetMouseButtonDown(0), Input.GetMouseButtonUp(0));
        sprintHeld = false;
        if (sprintRect.width > 0)
        {
            for (int i = 0; i < Input.touchCount; i++) { var tp = Input.GetTouch(i).position; if (sprintRect.Contains(new Vector2(tp.x, Screen.height - tp.y))) sprintHeld = true; }
            if (Input.touchCount == 0 && Input.GetMouseButton(0) && sprintRect.Contains(new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y))) sprintHeld = true;
        }
        if (drag != null && drag.id >= 0 && Input.touchCount == 0) drag = null; // a lift lost to a full-screen window must not leave the stick held
        stickOn = drag != null && drag.on;
        if (stickOn)
        {
            // the ring slides along behind a thumb that goes past it, so pulling back turns around at once
            stick = StickAt(new Vector2(drag.p.x, Screen.height - drag.p.y));
        }
    }
    void PointerEvent(int id, Vector2 pos, bool down, bool up)
    {
        if (modal != null && modal.FullScreen) { modal.Pointer(this, id, pos, down, up); return; }
        if (modal == null && P2Pointer(id, pos, down, up)) return;
        if (GerBuildPointer(id, pos, down, up)) return; // dragging a ger part from the tray (GerBuild.cs)
        // a finger that lands on the fixed joystick steers; the mouse on a computer never does (the keys walk there)
        if (down && drag == null && modal == null && !OverUI(pos)) drag = new Drag { id = id, p0 = pos, p = pos, on = id >= 0 && TouchUI && NearStick(new Vector2(pos.x, Screen.height - pos.y)) };
        if (drag == null || drag.id != id) return;
        drag.p = pos;
        if (drag.on && down) { path.Clear(); pending = null; }
        // a quick tap elsewhere walks to the spot; a swipe elsewhere does nothing
        if (up) { if (!drag.on && modal == null && (id < 0 || (pos - drag.p0).magnitude <= 12 * UiScale / 2)) TapWorld(pos); drag = null; }
    }

    // ================================================================ toasts, floating text, tap rings, confetti
    sealed class Pop { public string text, kind; public float t, life; }
    readonly List<Pop> toasts = new List<Pop>();
    GUIStyle sPop, sPopBasar, sPopHungry, sPopText, sPopWho; // the toast cards: plain, Basar's gold one, the warm "you're hungry" one
    readonly List<(Vector2 at, string text, Color col, float t)> floaters = new List<(Vector2, string, Color, float)>();
    readonly List<(Vector2 at, float t)> rings = new List<(Vector2, float)>();
    sealed class Bit { public Vector2 p, v; public Color c; public float t0, spin; }
    readonly List<Bit> confetti = new List<Bit>();
    /** A popup card at the top. kind: null | "basar" (his face and name) | "hungry" (a wobbling buuz). Long texts stay longer. */
    public void Toast(string text, string kind = null) { toasts.Add(new Pop { text = text, kind = kind, t = Time.time, life = Mathf.Clamp(2.4f + text.Length * 0.05f, 3.6f, 8) }); if (toasts.Count > 3) toasts.RemoveAt(0); }
    public void Float(float x, float y, string text, Color col) => floaters.Add((new Vector2(x, y), text, col, Time.time));
    public void Confetti(int n)
    {
        Color[] cols = { Px.Col("#e14b3d"), Px.Col("#3f76db"), Px.Col("#f7c646"), Px.Col("#2ea35f"), Px.Col("#9466e0"), Color.white };
        for (int i = 0; i < n; i++) confetti.Add(new Bit { p = new Vector2(UnityEngine.Random.value * Screen.width, -U(10) - UnityEngine.Random.value * U(80)), v = new Vector2((UnityEngine.Random.value - 0.5f) * U(60), U(120 + UnityEngine.Random.value * 160)), c = cols[i % cols.Length], t0 = Time.time, spin = UnityEngine.Random.value * 10 });
    }

    // ================================================================ windows
    public abstract class Modal
    {
        public virtual float Width => 640;
        public virtual bool FullScreen => false;
        public virtual bool CanEscape => true;
        public abstract void Draw(Game g);
        public virtual void Tick(Game g, float dt) { }
        public virtual void Press(Game g) { }   // Space / E inside the window
        public virtual void Key(Game g) { }     // any other keys, read in Update
        public virtual void Pointer(Game g, int id, Vector2 pos, bool down, bool up) { } // full-screen scenes take the pointer
        public virtual void Closed(Game g) { }
        public Vector2 winScroll; // a window taller than the screen scrolls (DrawModal)
        /** Who is talking in this window and with what face: they stand beside it from head to toe (Folk). */
        public virtual (string who, string face)? Speaker => null;
    }
    Modal modal; Rect modalPanel; // where the open window is on the screen, for its ✕ and for taps outside it
    public float ModalWidth => modalPanel.width; // 0 on the window's first frame
    readonly Stack<Modal> under = new Stack<Modal>();
    /** A full-screen scene is showing (maybe under a window opened from it), so the village is hidden. */
    bool InScene { get { if (modal == null) return false; if (modal.FullScreen) return true; foreach (var m in under) if (m.FullScreen) return true; return false; } }
    Modal SceneUnder { get { foreach (var m in under) if (m.FullScreen) return m; return null; } }
    public void Open(Modal m) { modalPanel = Rect.zero; if (modal != null && m != null && !modal.FullScreen && m.FullScreen) modal = null; if (modal != null) under.Push(modal); modal = m; drag = null; path.Clear(); pending = null; Sfx.Tap(); }
    /** Closes the top window; a window opened from a full-screen scene goes back to the scene. */
    public void Close() { modalPanel = Rect.zero; var m = modal; modal = under.Count > 0 ? under.Pop() : null; m?.Closed(this); if (modal == null) ctx = ContextAction(); }
    public void CloseAll() { while (modal != null) Close(); }
    /** Replaces the current window with another one. */
    public void Swap(Modal m) { modalPanel = Rect.zero; var old = modal; modal = m; old?.Closed(this); Sfx.Tap(); }
    void KeysInModal()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) { if (modal.CanEscape) Close(); return; }
        if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.E)) modal.Press(this);
        modal?.Key(this);
    }

    // ================================================================ styles (rebuilt when the screen size changes)
    // One unit is one CSS pixel of the web version (screen pixels per CSS pixel, as the browser's devicePixelRatio), so a
    // laptop shows the same sizes as the web page; a short screen scales down so the layout's 520 × 900 units still fit.
    public float UiScale => Mathf.Min(Dpr, Screen.height / 520f, Screen.width / 900f);
#if UNITY_WEBGL && !UNITY_EDITOR
    [System.Runtime.InteropServices.DllImport("__Internal")] static extern float NaadamyDpr(); // Plugins/WebGL/NaadamyPointer.jslib
    static float Dpr => NaadamyDpr();
#else
    static float Dpr => Application.isMobilePlatform && Screen.dpi > 0 ? Mathf.Clamp(Screen.dpi / 160f, 1, 3) : 1;
#endif
    /** Phones and tablets get on-screen buttons and touch hints; a computer uses the keys Bolormaa teaches. */
    public static bool TouchUI => Application.isMobilePlatform;
    float builtFor = -1;
    public const float TALK = 16; // every NPC line (story box, bubbles, teachers, walk-scene notes, task givers) reads at this size
    public GUIStyle sSpeech, sPanel, sBtn, sPrimary, sLabel, sTitle, sSmall, sChip, sChipText, sToast, sCenter, sTag, sBig, sOpt, sSel, sPixel, sTeach, sKicker, sNote, sSlot, sChipHi, sWhite, sNav, sNavOn, sTabOff, sField, sTile, sLogo;
    Texture2D white;
    readonly Dictionary<PxCanvas, Texture2D> texCache = new Dictionary<PxCanvas, Texture2D>();
    public Texture2D Tex(PxCanvas c) { if (!texCache.TryGetValue(c, out var t)) texCache[c] = t = c.ToTexture(); return t; }

    /** A frame drawn like the web version's CSS: rings of colour from the edge inwards (its inset box-shadows), cut corners
     *  (its clip-path) and a darker copy below (a button's press shadow), at u screen pixels per CSS pixel. */
    static Texture2D Frame(string fill, (string col, int w)[] rings, int cut, string drop, int dropH, int u)
    {
        int ring = 0; foreach (var r in rings) ring += r.w;
        int n = 2 * (ring + cut + 1) * u + 2, H = n + dropH * u; var c = new PxCanvas(n, H);
        Color32 F = Px.Col(fill), D = drop != null ? Px.Col(drop) : default;
        Color32? At(int x, int y)
        {
            float dx = Mathf.Min(x, n - 1 - x) / (float)u, dy = Mathf.Min(y, n - 1 - y) / (float)u;
            if (dx + dy < cut) return null;
            float d = Mathf.Min(dx, dy), acc = 0;
            foreach (var r in rings) { acc += r.w; if (d < acc) return Px.Col(r.col); }
            return F;
        }
        for (int y = 0; y < H; y++) for (int x = 0; x < n; x++)
        {
            var top = y < n ? At(x, y) : null;
            if (top != null) c.Put(x, y, top.Value);
            else if (drop != null && y >= dropH * u && At(x, y - dropH * u) != null) c.Put(x, y, D);
        }
        return c.ToTexture();
    }
    /** A style on a Frame texture: its corners and press shadow stay sharp, the middle stretches. */
    GUIStyle Style(Texture2D bg, Texture2D bgOn, Font font, int size, Color text, TextAnchor align, bool wrap = false)
    {
        int e = (bg.width - 2) / 2, drop = bg.height - bg.width;
        var s = new GUIStyle { font = font, fontSize = size, alignment = align, wordWrap = wrap, richText = true, border = new RectOffset(e, e, e, e + drop), padding = new RectOffset(e + size / 3, e + size / 3, e + size / 4, e + drop + size / 4) };
        s.normal.background = bg; s.normal.textColor = text;
        s.hover.background = bg; s.hover.textColor = text;
        s.active.background = bgOn ?? bg; s.active.textColor = text;
        return s;
    }
    void BuildStyles()
    {
        float s = UiScale; if (Mathf.Approximately(s, builtFor) && sPanel != null) return;
        builtFor = s; int u = Mathf.Max(1, Mathf.RoundToInt(s));
        int F(float v) => PxSize(v * s * 1.3f); // pixel letters need to be a bit bigger than smooth ones to read easily
        int T(float css) => Mathf.Max(10, Mathf.RoundToInt(css * s)); // smooth text, in the web version's CSS pixels
        white = Texture2D.whiteTexture;
        Color ink = Px.Col("#4a2a12"), paper = Color.white;
        // the web version's village skin (styles.css): parchment, wood frames, Rubik buttons and headings, Nunito text, Tiny5 labels
        (string, int)[] Ring(string c, int w) => new[] { (c, w) };
        sPanel = Style(Frame("#f6dca8", new[] { ("#5a2d10", 2), ("#e5a157", 3), ("#8a4b1f", 3) }, 6, null, 0, u), null, bodyFont, T(15), ink, TextAnchor.UpperLeft, true);
        sPanel.padding = new RectOffset(T(28), T(28), T(30), T(26));
        sBtn = Style(Frame("#fbe8c2", Ring("#c99a5a", 2), 0, "#8a4b1f", 3, u), Frame("#f1d7a6", Ring("#c99a5a", 2), 0, "#8a4b1f", 3, u), displayFont, T(16), ink, TextAnchor.MiddleCenter, true);
        sPrimary = Style(Frame("#e9a24a", Ring("#f7c98a", 2), 0, "#8a4b1f", 3, u), Frame("#d98f35", Ring("#f7c98a", 2), 0, "#8a4b1f", 3, u), displayFont, T(16), Px.Col("#3a1e08"), TextAnchor.MiddleCenter, true);
        sSel = Style(Frame("#fff3c4", Ring("#e14b3d", 3), 0, null, 0, u), null, displayFont, T(16), ink, TextAnchor.MiddleCenter, true); // a picked tile or swatch: a red outline
        sChip = Style(Frame("#f6dca8", new[] { ("#e5a157", 2), ("#8a4b1f", 2) }, 4, "#5a2d10", 4, u), null, pixelFont, F(11), ink, TextAnchor.MiddleLeft); sChipText = sChip.Clean();
        sToast = Style(Frame("#141b2d", new (string, int)[0], 4, null, 0, u), null, bodyFont, T(15), paper, TextAnchor.MiddleCenter, true); sToast.padding = new RectOffset(T(18), T(18), T(10), T(10));
        sPop = Style(Frame("#fffaf0", new[] { ("#5a2d10", 2), ("#f7c98a", 2) }, 4, "#8a4b1f", 3, u), null, bodyFont, T(15), ink, TextAnchor.MiddleLeft, true);
        sPopBasar = Style(Frame("#fff3c4", new[] { ("#5a2d10", 2), ("#e5a157", 3) }, 4, "#8a4b1f", 3, u), null, bodyFont, T(15), ink, TextAnchor.MiddleLeft, true);
        sPopHungry = Style(Frame("#ffe9dc", new[] { ("#6b1f17", 2), ("#f08a6e", 3) }, 4, "#a33a2a", 3, u), null, bodyFont, T(15), ink, TextAnchor.MiddleLeft, true);
        sPopText = new GUIStyle { font = bodyFont, fontSize = T(TALK), wordWrap = true, richText = true, alignment = TextAnchor.MiddleLeft, normal = { textColor = ink } };
        sPopWho = new GUIStyle { font = pixelFont, fontSize = F(10), alignment = TextAnchor.LowerLeft, normal = { textColor = Px.Col("#a8641e") } };
        sOpt = Style(Frame("#08060e", Ring("#ffffff", 3), 0, null, 0, u), Frame("#08060e", Ring("#f7c646", 3), 0, null, 0, u), bodyFont, T(TALK), paper, TextAnchor.MiddleLeft, true); // an answer in the story box
        sTag = Style(Frame("#fbe8c2", Ring("#c99a5a", 2), 0, null, 0, u), null, pixelFont, F(10), ink, TextAnchor.MiddleCenter); sTag.padding = new RectOffset(T(6), T(6), T(2), T(2));
        sLabel = new GUIStyle { font = bodyFont, fontSize = T(15), wordWrap = true, richText = true, normal = { textColor = ink } };
        sSmall = new GUIStyle(sLabel) { fontSize = T(13), normal = { textColor = Px.Col("#7a5432") } };
        sTitle = new GUIStyle(sLabel) { font = displayFont, fontSize = T(23), normal = { textColor = Px.Col("#5a2d10") } };
        sBig = new GUIStyle(sTitle) { fontSize = T(34), alignment = TextAnchor.MiddleCenter };
        sCenter = new GUIStyle(sLabel) { alignment = TextAnchor.MiddleCenter };
        sPixel = new GUIStyle(sCenter) { font = pixelFont, wordWrap = false }; // the village's labels, as the web version draws them in Tiny5
        sTeach = Style(Frame("#fffaf0", Ring("#c99a5a", 2), 0, null, 0, u), null, bodyFont, T(TALK), ink, TextAnchor.UpperLeft, true); sTeach.padding = new RectOffset(T(12), T(12), T(8), T(8)); // what a person says, in a speech box
        sKicker = new GUIStyle(sLabel) { fontSize = T(12), normal = { textColor = Px.Col("#6b7280") } }; // the small grey line above a heading
        sChipHi = Style(Frame("#fff3c4", new[] { ("#e5a157", 2), ("#8a4b1f", 2) }, 4, "#5a2d10", 4, u), null, pixelFont, F(11), ink, TextAnchor.MiddleLeft); // Basar's button stands out (.v-basar)
        // the app's own pages (the web version's shell): white chips and a white tab bar on a warm grey page
        sWhite = Style(Frame("#ffffff", Ring("#e6e1d6", 1), 3, null, 0, u), Frame("#f3f1ec", Ring("#e6e1d6", 1), 3, null, 0, u), displayFont, T(16), Px.Col("#141b2d"), TextAnchor.MiddleCenter);
        sNav = Style(Frame("#fdfcfa", Ring("#e6e1d6", 1), 8, null, 0, u), null, bodyFont, T(11), ink, TextAnchor.MiddleCenter);
        sNavOn = Style(Frame("#eef3ff", new (string, int)[0], 6, null, 0, u), null, bodyFont, T(11), ink, TextAnchor.MiddleCenter);
        sTabOff = Style(Frame("#c5dbe3", Ring("#b9c7c4", 2), 0, null, 0, u), null, displayFont, T(16), Px.Col("#7c8a8c"), TextAnchor.MiddleCenter); // the other tab on the sign-up screen
        sField = Style(Frame("#fffaf0", Ring("#c99a5a", 2), 0, null, 0, u), null, bodyFont, T(20), ink, TextAnchor.MiddleCenter); // a text box
        sTile = Style(Frame("#fde8c3", Ring("#c99a5a", 2), 0, null, 0, u), Frame("#f7dcb0", Ring("#c99a5a", 2), 0, null, 0, u), bodyFont, T(12), ink, TextAnchor.MiddleCenter); // a tile to pick in a grid
        sLogo = Style(Frame("#2f6bff", new (string, int)[0], 6, null, 0, u), null, bodyFont, T(12), ink, TextAnchor.MiddleCenter);
        sSlot = Style(Frame("#e6c48a", Ring("#c99a5a", 2), 0, null, 0, u), null, pixelFont, F(11), ink, TextAnchor.MiddleCenter);
        sNote = new GUIStyle(sChip) { font = bodyFont, fontSize = T(14), wordWrap = true, alignment = TextAnchor.MiddleCenter }; // a parchment box over a scene: its title, a hint
        sSpeech = new GUIStyle(sNote) { fontSize = T(TALK), padding = new RectOffset(T(10), T(10), T(6), T(8)) }; // what someone says, in a bubble (SpeechBubble)
    }
    public float U(float v) => v * UiScale;
    /** The layout is scaled to fit 900 units across, so on a phone held upright small type turns tiny: this many units, but at
     *  least `css` CSS pixels on the screen. On a computer it is just `units`. */
    public float AtLeastCss(float units, float css) => Mathf.Max(units, css * Dpr / UiScale);
    public float StickR => Mathf.Max(STICK_R * UiScale, 62 * Dpr); // a thumb-sized ring on a phone, however small the layout is
    /** The joystick's fixed centre in GUI pixels (y down): bottom left, right of the village's button column; lift raises it over a scene's bottom row. */
    public Vector2 StickHome(float lift = 0) => new Vector2(U(130) + StickR, Screen.height - U(14 + lift) - StickR);
    /** A finger landing here takes the joystick: a little wider than the ring, so a thumb need not aim. */
    public bool NearStick(Vector2 gui, float lift = 0) => (gui - StickHome(lift)).magnitude < StickR * 1.5f;
    /** Where a finger at gui pushes the joystick: -1..1 on each axis, y down as GUI y. */
    public Vector2 StickAt(Vector2 gui, float lift = 0) { var d = (gui - StickHome(lift)) / StickR; return d.magnitude > 1 ? d.normalized : d; }
    /** The joystick at its home, its knob pushed by v; off is the GUI group's offset when drawn inside one. */
    public void DrawStick(Vector2 v, float lift = 0, Vector2 off = default)
    {
        // a dark glassy ring with arrows, and a gold knob under the thumb with a soft shadow
        var b = StickHome(lift) - off; float R = StickR;
        GUI.DrawTexture(new Rect(b.x - R, b.y - R, R * 2, R * 2), Tex(StickBase()));
        var k = b + v * R; float kr = R * 0.5f, sh = R * 0.08f;
        var old = GUI.color; GUI.color = new Color(0, 0, 0, 0.3f); GUI.DrawTexture(new Rect(k.x - kr + sh, k.y - kr + sh * 1.5f, kr * 2, kr * 2), Tex(Disc())); GUI.color = old;
        GUI.DrawTexture(new Rect(k.x - kr, k.y - kr, kr * 2, kr * 2), Tex(StickKnob()));
    }
    Rect UR(float x, float y, float w, float h) => new Rect(U(x), U(y), U(w), U(h));
    void Ui(Rect r) { if (Event.current.type == EventType.Repaint) uiNext.Add(r); }
    bool Button(Rect r, string text, GUIStyle st = null) { Ui(r); st ??= sBtn; return GUI.Button(r, text, Fit(st, text, r.width)); }
    readonly Dictionary<(Font, int, int, string, int), int> fitCache = new Dictionary<(Font, int, int, string, int), int>();
    /** The style for a text in a box w wide that never breaks a word in two ("Алгаса / х"): when the longest word
     *  doesn't fit on one line, the font shrinks (to 55% at most) until it does. The same style when it already fits. */
    public GUIStyle Fit(GUIStyle st, string text, float w)
    {
        if (string.IsNullOrEmpty(text) || st == null || st.fontSize <= 0) return st;
        var key = (st.font, st.fontSize, st.padding.horizontal, text, Mathf.RoundToInt(w)); // by value: callers often pass a new style each frame
        if (!fitCache.TryGetValue(key, out int size))
        {
            if (fitCache.Count > 1024) fitCache.Clear(); // the texts change with the language and the screen size
            float room = w - st.padding.horizontal;
            var one = new GUIStyle(st) { wordWrap = false, padding = new RectOffset() };
            var words = text.Split(new[] { ' ', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            float Widest(int fs) { one.fontSize = fs; return words.Length == 0 ? 0 : words.Max(x => one.CalcSize(new GUIContent(x)).x); }
            size = st.fontSize; int min = Mathf.Max(8, Mathf.RoundToInt(st.fontSize * 0.55f));
            while (size > min && Widest(size) > room) size--;
            fitCache[key] = size;
        }
        return size == st.fontSize ? st : new GUIStyle(st) { fontSize = size };
    }
    bool IconButton(Rect r, PxCanvas icon, string text = null, GUIStyle st = null)
    {
        bool hit = Button(r, "", st);
        float s = Mathf.Min(r.height - U(10), U(28));
        if (text == null) GUI.DrawTexture(new Rect(r.center.x - s / 2, r.center.y - s / 2, s, s), Tex(icon), ScaleMode.ScaleToFit);
        else { GUI.DrawTexture(new Rect(r.x + U(6), r.center.y - s / 2, s, s), Tex(icon), ScaleMode.ScaleToFit); GUI.Label(new Rect(r.x + U(8) + s, r.y, r.width - s - U(10), r.height), text, new GUIStyle((st ?? sBtn)) { normal = { background = null }, active = { background = null }, hover = { background = null }, alignment = TextAnchor.MiddleLeft, padding = new RectOffset() }); }
        return hit;
    }
    public void Bar(Rect r, float f, Color fill)
    {
        var old = GUI.color; GUI.color = Px.Col("#2b1e2e"); GUI.DrawTexture(r, white);
        var inner = new Rect(r.x + U(1.5f), r.y + U(1.5f), r.width - U(3), r.height - U(3));
        GUI.color = Px.Col("#5a4a5e"); GUI.DrawTexture(inner, white);
        GUI.color = fill; GUI.DrawTexture(new Rect(inner.x, inner.y, inner.width * Mathf.Clamp01(f), inner.height), white); GUI.color = old;
    }
    public void Box(Rect r, Color c) { var old = GUI.color; GUI.color = c; GUI.DrawTexture(r, white); GUI.color = old; }
    /** Text with a dark outline, like the web version's pixelText, centred on (x, y). */
    public void Outlined(float x, float y, string text, float size, Color col, float alpha = 1)
    {
        if (string.IsNullOrEmpty(text)) return;
        var st = new GUIStyle(sPixel) { fontSize = PxSize(size) };
        float o = Mathf.Max(1, Mathf.Round(PxSize(size) / 8f)), hw = st.CalcSize(new GUIContent(text)).x / 2 + o;
        if (GUI.matrix.isIdentity && x > 0 && x < Screen.width) x = Mathf.Clamp(x, hw, Mathf.Max(hw, Screen.width - hw)); if (GUI.matrix.isIdentity && y > 0 && y < Screen.height) y = Mathf.Max(y, PxSize(size) * 0.5f + o); // (not while rotated: the race arrow turns round its rider) a name tag at the screen's edge stays whole (one off the screen stays off it)
        var r = new Rect(x - U(150), y - size, U(300), size * 2);
        st.normal.textColor = new Color(0.17f, 0.12f, 0.18f, alpha);
        foreach (var (dx, dy) in new[] { (-o, 0f), (o, 0f), (0f, -o), (0f, o), (-o, -o), (o, o), (-o, o), (o, -o) }) GUI.Label(new Rect(r.x + dx, r.y + dy, r.width, r.height), text, st);
        st.normal.textColor = new Color(col.r, col.g, col.b, alpha); GUI.Label(r, text, st);
    }

    // ================================================================ OnGUI
    // every text is pixel text, as on the web version: Tiny5 (Resources/Fonts, drawn as hard pixels by Editor/NaadamyFontImport)
    static Font pixelFont, bodyFont, displayFont;
    /** Tiny5 is crisp at multiples of 8 screen pixels. */
    public static int PxSize(float v) => Mathf.Max(16, Mathf.FloorToInt(v / 8f + 0.6f) * 8); // rounds up a little sooner, so small labels stay readable
    /** A size for the smooth fonts, from the old pixel-font units (Nunito reads well a little smaller than Tiny5). */
    public int Ts(float units) => Mathf.Max(9, Mathf.RoundToInt(units * 1.15f * UiScale));
    void OnGUI()
    {
        if (pixelFont == null) { pixelFont = Resources.Load<Font>("Fonts/NaadamyPixel"); bodyFont = Resources.Load<Font>("Fonts/NaadamyBody"); displayFont = Resources.Load<Font>("Fonts/NaadamyDisplay"); }
        if (bodyFont != null) GUI.skin.font = bodyFont;
        BuildStyles(); scrollN = 0; scrollOpen.Clear();
        if (Event.current.type == EventType.Repaint) { var t = drawRects; drawRects = drawNext; drawNext = t; drawNext.Clear(); }
        if (Event.current.type == EventType.Repaint) uiNext.Clear();
        var safe = Screen.safeArea; float sx = safe.x, sy = Screen.height - safe.yMax, vw = safe.width / UiScale, vh = safe.height / UiScale;
        guiOX = sx; guiOY = sy;
        if (!InScene)
        {
            WorldLabels();
            GUI.BeginGroup(new Rect(sx, sy, safe.width, safe.height));
            bool was = GUI.enabled; GUI.enabled = modal == null; // the buttons behind an open window don't answer
            Hud(vw, vh);
            GUI.enabled = was;
            GUI.EndGroup();
        }
        if (!InScene && modal == null) { GUI.BeginGroup(new Rect(sx, sy, safe.width, safe.height)); DialogDraw(vw, vh); BigMap(vw, vh); GUI.EndGroup(); } // the big map sits over everything in the village
        if (modal != null) DrawModal();
        Toasts(sx, sy, vw);
        AchievementPopup();
        ConfettiDraw();
        if (Event.current.type == EventType.Repaint)
        {
            var r = uiNext; uiNext = uiRects; uiRects = r;
            for (int i = 0; i < uiRects.Count; i++) { var q = uiRects[i]; uiRects[i] = new Rect(q.x + sx, q.y + sy, q.width, q.height); }
        }
    }

    Vector2 ToGui(float tileX, float tileY)
    {
        var s = cam.WorldToScreenPoint(new Vector3(tileX, -tileY, 0));
        return new Vector2(s.x, Screen.height - s.y);
    }
    /** Art pixel (X, Y) of the village to a screen point. */
    /** Tile (fx, fy), h tiles up, to a screen point (for things on top of something in the isometric view). */
    Vector2 WorldGui(float fx, float fy, float h) { var s = cam.WorldToScreenPoint(new Vector3(fx, -fy, -h)); return new Vector2(s.x, Screen.height - s.y); }
    Vector2 PxGui(float X, float Y)
    {
        // labels sit above someone standing about 30 art pixels lower: up from those feet, so in the isometric view too
        // they rise straight up the screen instead of sliding along the ground
        var s = cam.WorldToScreenPoint(new Vector3(X / T, -(Y + 30) / T, 0) + cam.transform.up * (30f / T));
        return new Vector2(s.x, Screen.height - s.y);
    }
    float ArtPx => Screen.height / (2 * cam.orthographicSize) / T; // screen pixels per art pixel

    void WorldLabels()
    {
        SeasonFx();
        float ts = Mathf.Max(U(9), 5 * Mathf.Max(2, ArtPx)), t = Time.time;
        Color gold = Px.Col("#f7c646"), green = Px.Col("#7fe07a");
        // tap rings, floating "+XP" and hearts
        for (int i = rings.Count - 1; i >= 0; i--)
        {
            float age = Time.time - rings[i].t; if (age > 0.6f) { rings.RemoveAt(i); continue; }
            var p = ToGui(rings[i].at.x, rings[i].at.y); float r = U(6 + age * 18);
            var old = GUI.color; GUI.color = new Color(1, 1, 1, 1 - age / 0.6f); GUI.DrawTexture(new Rect(p.x - r, p.y - r * 0.5f, r * 2, r), Tex(Ring())); GUI.color = old;
        }
        // names over the townspeople, with ! for a new task and ? when one is done
        foreach (var o in World.objects)
        {
            if (o.kind == "statue") { var sp = PxGui(o.x * T + 8, o.y * T - 40); Outlined(sp.x, sp.y, Loc.L("Сэцэн", "Setsen"), ts * 0.8f, gold); continue; }
            if (o.kind != "npc" && o.kind != "npcq" && o.kind != "emee" && o.kind != "general" && o.kind != "historian") continue;
            var p = PxGui(o.x * T + 8, o.y * T - 16);
            string name = o.kind == "npc" ? Loc.L("Засуул", "Herald") : o.kind == "emee" ? Loc.L("Эмээ", "Grandma") : o.kind == "general" ? Loc.L("Жанжин", "General") : o.kind == "historian" ? Loc.L("Түүхч", "Historian") : LastWord(Rules.NpcById(o.id).name.ToString());
            Outlined(p.x, p.y, name, ts * 0.8f, o.kind == "npcq" ? Color.white : gold);
            string mk = o.kind == "npcq" ? NpcMark(o.id) : o.kind == "emee" && V.emee.visits == 0 ? "!" : o.kind == "historian" && HistoryReady() ? "!" : "";
            if (mk != "") Outlined(p.x, p.y - 9 * ArtPx + Mathf.Round(Mathf.Sin(t * 5.5f + o.x) * 2) * ArtPx / 2, mk, ts * 1.6f, mk == "?" ? green : gold);
        }
        if (V.lot.i >= 0) { var o = World.objects.Find(q => q.kind == "lot" && q.i == V.lot.i); var p = GerTop(o); Outlined(p.x, p.y, Loc.L("Миний гэр", "My ger"), ts, gold); }
        { var p = PxGui(dgx * T + 8, dgy * T - 4); Outlined(p.x, p.y, Loc.L("Басар", "Basar"), ts * 0.8f, gold); if (dogBubble != null) Outlined(p.x + 5 * ArtPx, p.y - 6 * ArtPx + Mathf.Round(Mathf.Sin(t * 5.5f) * 2) * ArtPx / 2, "!", ts * 1.6f, gold); }
        MyLabel(ts);
        if (race != null) RaceLabels(ts);
        BattleOverlay(ts);
        NetLabels(ts);
        P2Label(ts);
        for (int i = floaters.Count - 1; i >= 0; i--)
        {
            var f = floaters[i]; float age = Time.time - f.t; if (age > 1.4f) { floaters.RemoveAt(i); continue; }
            var p = ToGui(f.at.x, f.at.y - age * 0.9f); Outlined(p.x, p.y, f.text, ts * 1.1f, f.col, 1 - age / 1.4f);
        }
        if (TouchUI && modal == null) DrawStick(stickOn ? stick : Vector2.zero); // stick.y grows downwards, as GUI y does
    }
    static string LastWord(string s) { int i = s.LastIndexOf(' '); return i < 0 ? s : s.Substring(i + 1); }
    /** Your level in a gold box and your name in a blue one, with your wrestling title above. */
    void MyLabel(float ts)
    {
        var p = PxGui(px * T + 8, py * T - 16 - (riding ? 10 : 0));
        var st = new GUIStyle(sCenter) { font = displayFont, fontSize = Mathf.RoundToInt(ts), wordWrap = false };
        string lv = Rules.Level(V.xp).l.ToString(), nm = MyName;
        float pad = ts * 0.45f, th = ts + U(4), lw = st.CalcSize(new GUIContent(lv)).x + pad * 2, nw = st.CalcSize(new GUIContent(nm)).x + pad * 2, gap = U(3), x0 = p.x - (lw + gap + nw) / 2, y = p.y - th / 2, u = Mathf.Max(1, U(1));
        void BoxT(float bx, float w, string bg, string fg, string text) { Box(new Rect(bx - u, y - u, w + 2 * u, th + 2 * u), Px.Col("#2b1e2e")); Box(new Rect(bx, y, w, th), Px.Col(bg)); st.normal.textColor = Px.Col(fg); GUI.Label(new Rect(bx, y, w, th), text, st); }
        BoxT(x0, lw, "#f7c646", "#3a2600", lv); BoxT(x0 + lw + gap, nw, "#3f76db", "#ffffff", nm);
        var title = Rules.TitleById(Rules.TitleFor(V.wr));
        if (title != null) Outlined(p.x, y - th / 2 - u * 2, "♦ " + title.name, ts, Px.Col("#f7c646"));
    }
    static PxCanvas ringArt, discArt;
    static PxCanvas Ring() { if (ringArt != null) return ringArt; ringArt = new PxCanvas(32, 32); for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++) { float d = Mathf.Sqrt((x - 15.5f) * (x - 15.5f) + (y - 15.5f) * (y - 15.5f)); if (d > 12 && d < 15.5f) ringArt.Put(x, y, Color.white); } return ringArt; }
    static PxCanvas stickBase, stickKnob;
    /** The joystick's ring: see-through dark glass, a white rim and a little arrow at each side. */
    static PxCanvas StickBase()
    {
        if (stickBase != null) return stickBase; var c = stickBase = new PxCanvas(64, 64);
        for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
        {
            float d = Mathf.Sqrt((x - 31.5f) * (x - 31.5f) + (y - 31.5f) * (y - 31.5f));
            if (d < 31.5f) c.Put(x, y, d > 29.5f ? new Color32(43, 30, 46, 150) : d > 27 ? new Color32(255, 255, 255, 200) : new Color32(20, 14, 30, (byte)(70 + d * 1.6f)));
        }
        var aw = new Color32(255, 255, 255, 190);
        for (int j = 0; j < 4; j++) for (int w = -j; w <= j + 1; w++) // arrows: up, down, left, right
            { int a = 31 + w, e = 6 + j; c.Put(a, e, aw); c.Put(a, 63 - e, aw); c.Put(e, a, aw); c.Put(63 - e, a, aw); }
        return c;
    }
    /** The knob under the thumb: a gold button with a dark rim and a shine at the top left. */
    static PxCanvas StickKnob()
    {
        if (stickKnob != null) return stickKnob; var c = stickKnob = new PxCanvas(32, 32);
        Color32 rim = Px.Col("#5a2d10"), gold = Px.Col("#f7c646"), goldD = Px.Col("#e09a2a"), hi = Px.Col("#fff3c4");
        for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
        {
            float d = Mathf.Sqrt((x - 15.5f) * (x - 15.5f) + (y - 15.5f) * (y - 15.5f));
            if (d < 15.5f) c.Put(x, y, d > 13.6f ? rim : (x - 11f) * (x - 11f) + (y - 10f) * (y - 10f) < 12 ? hi : y + x * 0.3f > 25 ? goldD : gold);
        }
        return c;
    }
    static PxCanvas Disc() { if (discArt != null) return discArt; discArt = new PxCanvas(64, 64); for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) { float d = Mathf.Sqrt((x - 31.5f) * (x - 31.5f) + (y - 31.5f) * (y - 31.5f)); if (d < 31.5f) discArt.Put(x, y, d > 28 ? new Color32(43, 30, 46, 255) : new Color32(255, 255, 255, 200)); } return discArt; }

    static readonly Dictionary<string, PxCanvas> badgeArt = new Dictionary<string, PxCanvas>();
    /** A round pixel badge for a toast's icon: a coloured disc with a dark rim and a light spot top left. */
    static PxCanvas Badge(string fill)
    {
        if (badgeArt.TryGetValue(fill, out var c)) return c;
        c = new PxCanvas(20, 20); Color32 f = Px.Col(fill), rim = Px.Col("#5a2d10"), hi = Color32.Lerp(f, new Color32(255, 255, 255, 255), 0.55f);
        for (int y = 0; y < 20; y++) for (int x = 0; x < 20; x++) { float d = Mathf.Sqrt((x - 9.5f) * (x - 9.5f) + (y - 9.5f) * (y - 9.5f)); if (d < 9.8f) c.Put(x, y, d > 8.4f ? rim : (x - 6.5f) * (x - 6.5f) + (y - 6.5f) * (y - 6.5f) < 5 ? hi : f); }
        return badgeArt[fill] = c;
    }
    static float BackOut(float x) { x -= 1; return 1 + 2.70158f * x * x * x + 1.70158f * x * x; } // overshoots a little, then settles: a pop
    void Toasts(float sx, float sy, float vw)
    {
        float ty = 8, now = Time.time;
        for (int i = toasts.Count - 1; i >= 0; i--) if (now - toasts[i].t > toasts[i].life) toasts.RemoveAt(i);
        var oldM = GUI.matrix; var oldC = GUI.color;
        foreach (var t in toasts)
        {
            float age = now - t.t, inn = Mathf.Clamp01(age / 0.35f), outt = Mathf.Clamp01((t.life - age) / 0.35f);
            bool basar = t.kind == "basar", hungry = t.kind == "hungry";
            var st = basar ? sPopBasar : hungry ? sPopHungry : sPop;
            float k = Mathf.Clamp(AtLeastCss(15, 15) / 15, 1, 2.6f); // bigger cards and words on a phone
            var stText = k > 1 ? new GUIStyle(sPopText) { fontSize = Mathf.RoundToInt(sPopText.fontSize * k) } : sPopText;
            var stWho = k > 1 ? new GUIStyle(sPopWho) { fontSize = PxSize(sPopWho.fontSize * k) } : sPopWho;
            float w = Mathf.Min(460 * k, vw - 32), bw = (basar ? 46 : 38) * k, textW = U(w - bw - 34 * k);
            float h = Mathf.Max(U(52 * k), stText.CalcHeight(new GUIContent(t.text), textW) + U((basar ? 34 : 24) * k));
            float drop = (1 - inn) * U(-18) + (1 - outt) * U(-14); // drops in from above, floats up as it fades
            var r = new Rect(sx + U((vw - w) / 2), sy + U(ty + 44) + drop, U(w), h);
            ty += h / UiScale + 8;
            GUI.matrix = oldM; GUIUtility.ScaleAroundPivot(Vector2.one * Mathf.LerpUnclamped(0.6f, 1, BackOut(inn)), r.center);
            GUI.color = new Color(oldC.r, oldC.g, oldC.b, oldC.a * Mathf.Min(inn * 2, outt));
            GUI.Box(r, GUIContent.none, st);
            // the icon badge, bobbing (a hungry buuz wobbles, a sparkle twinkles)
            var br = new Rect(r.x + U(10 * k), r.y + (r.height - U(bw)) / 2 - U(2) + Mathf.Round(Mathf.Sin(age * 5) * U(2)), U(bw), U(bw));
            GUI.DrawTexture(br, Tex(Badge(basar ? "#f7c646" : hungry ? "#f08a6e" : "#9fd6ff")));
            var icon = basar ? Px.BankharFace(1) : Emo.Icon(hungry ? "buuz" : "sparkles");
            if (icon != null)
            {
                var m = GUI.matrix;
                if (hungry) GUIUtility.RotateAroundPivot(Mathf.Sin(age * 16) * 12 * Mathf.Max(0, 1 - age / 1.4f), br.center);
                if (!basar && !hungry) GUIUtility.ScaleAroundPivot(Vector2.one * (0.9f + 0.1f * Mathf.Sin(age * 7)), br.center);
                float pad = U((basar ? 5 : 8) * k); GUI.DrawTexture(new Rect(br.x + pad, br.y + pad, br.width - 2 * pad, br.height - 2 * pad), Tex(icon), ScaleMode.ScaleToFit);
                GUI.matrix = m;
            }
            float tx = br.xMax + U(12 * k), tw = r.xMax - tx - U(14 * k);
            if (basar) GUI.Label(new Rect(tx, r.y + U(4 * k), tw, U(16 * k)), Loc.L("БАСАР", "BASAR"), stWho);
            GUI.Label(new Rect(tx, r.y + U((basar ? 18 : 4) * k), tw, r.height - U((basar ? 24 : 12) * k)), t.text, stText);
            // how long it stays: a thin line along the bottom that runs out
            Color line = Px.Col(basar ? "#e5a157" : hungry ? "#e14b3d" : "#c99a5a"); line.a = GUI.color.a;
            Box(new Rect(r.x + U(12), r.yMax - U(8), (r.width - U(24)) * Mathf.Clamp01(1 - age / t.life), U(2)), line);
        }
        GUI.matrix = oldM; GUI.color = oldC;
    }
    void ConfettiDraw()
    {
        if (confetti.Count == 0 || Event.current.type != EventType.Repaint) return;
        float dt = Time.deltaTime;
        for (int i = confetti.Count - 1; i >= 0; i--)
        {
            var b = confetti[i]; b.p += b.v * dt; b.v.y += U(80) * dt;
            if (b.p.y > Screen.height + U(20) || Time.time - b.t0 > 5) { confetti.RemoveAt(i); continue; }
            float w = U(6) * Mathf.Abs(Mathf.Sin(Time.time * 6 + b.spin)) + U(1);
            Box(new Rect(b.p.x - w / 2, b.p.y, w, U(6)), b.c);
        }
    }

    // ================================================================ the village HUD, laid out like the web version's (village.js hud(), styles.css .v-*)
    bool moreOpen, miniBig; PxCanvas miniBase;
    static readonly Dictionary<char, string> MINI = new Dictionary<char, string> { ['g'] = "#76b947", ['T'] = "#3f7a2c", ['p'] = "#d8ab6c", ['s'] = "#d9cdb5", ['w'] = "#4aa6e2", ['d'] = "#a46d3b", ['o'] = "#8c5a33", ['f'] = "#a46d3b", ['r'] = "#a5a5b0", ['a'] = "#e8c994" };
    static readonly Dictionary<string, string> MINI_OBJ = new Dictionary<string, string> { ["ger"] = "#f4efe3", ["shop"] = "#e14b3d", ["board"] = "#a46d3b", ["well"] = "#77778a", ["lot"] = "#c8a06a", ["bleachers"] = "#a46d3b", ["ovoo"] = "#a5a5b0", ["uya"] = "#8a5a33", ["archery"] = "#e9a91f", ["academy"] = "#2f7d5b", ["felt"] = "#d8c8a8" };
    /** One pixel per tile: ground, roads, water, buildings. */
    PxCanvas MiniBase()
    {
        if (miniBase != null) return miniBase;
        miniBase = new PxCanvas(World.W, World.H);
        for (int y = 0; y < World.H; y++) for (int x = 0; x < World.W; x++)
        {
            char c = World.At(x, y); var o = c == 'B' ? World.ObjAt(x, y) : null;
            miniBase.Put(x, y, Px.Col(o != null && MINI_OBJ.TryGetValue(o.kind, out var oc) ? oc : MINI.TryGetValue(c, out var tc) ? tc : MINI['g']));
        }
        return miniBase;
    }
    /** The map: lots, people with news for you, friends, you, and what the screen shows. Tap it to make it big; on the big map, tap a place to walk there. */
    void VillageMap(Rect r)
    {
        var e = Event.current;
        var map = new Rect(r.x + U(6), r.y + U(6), r.width - U(12), r.height - U(14));
        float s = map.width / World.W;
        if (miniBig && e.type == EventType.MouseUp && map.Contains(e.mousePosition))
        {
            int tx0 = Mathf.FloorToInt((e.mousePosition.x - map.x) / s), ty0 = Mathf.FloorToInt((e.mousePosition.y - map.y) / s); e.Use();
            Later(() => { miniBig = false; if (!World.Blocked(tx0, ty0)) GoTo(tx0, ty0, null); else { var n = World.NearestOpen(tx0, ty0, mx, my); if (n != null) GoTo(n.Value.x, n.Value.y, null); } });
        }
        if (Button(r, "", sChip) && !miniBig) Later(() => { miniBig = true; Sfx.Tap(); });
        GUI.DrawTexture(map, Tex(MiniBase()));
        void Dot(float x, float y, Color c, float rad = 2) { float d = U(rad); Box(new Rect(map.x + x * s - d - U(1), map.y + y * s - d - U(1), 2 * d + U(2), 2 * d + U(2)), Px.Col("#2b1e2e")); Box(new Rect(map.x + x * s - d, map.y + y * s - d, 2 * d, 2 * d), c); }
        foreach (var o in World.objects)
        {
            if (o.kind == "npcq" && NpcMark(o.id) != "") Dot(o.x + 0.5f, o.y + 0.5f, Px.Col(NpcMark(o.id) == "?" ? "#7fe07a" : "#f7c646"));
            if (o.kind == "emee" && V.emee.visits == 0) Dot(o.x + 0.5f, o.y + 0.5f, Px.Col("#f492ba"));
        }
        foreach (var o in net.others.Values) Dot(o.px + 0.5f, o.py + 0.8f, Color.white);
        if ((int)(Time.time / 0.45f) % 2 == 1) Dot(px + 0.5f, py + 0.8f, Color.white, 3);
        Dot(px + 0.5f, py + 0.8f, Px.Col("#e14b3d"));
        float hh = cam.orthographicSize, hw = hh * cam.aspect, cx = cam.transform.position.x, cy = -cam.transform.position.y; var vr = new Rect(map.x + (cx - hw) * s, map.y + (cy - hh) * s, 2 * hw * s, 2 * hh * s);
        if (IsoOn) // the ground the isometric camera sees, as a box around its four corners
        {
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            foreach (var sc in new[] { new Vector2(0, 0), new Vector2(Screen.width, 0), new Vector2(0, Screen.height), new Vector2(Screen.width, Screen.height) })
            { var gp = Iso.Ground(cam, sc); if (gp == null) continue; x0 = Mathf.Min(x0, gp.Value.x); y0 = Mathf.Min(y0, gp.Value.y); x1 = Mathf.Max(x1, gp.Value.x); y1 = Mathf.Max(y1, gp.Value.y); }
            x0 = Mathf.Max(0, x0); y0 = Mathf.Max(0, y0); x1 = Mathf.Min(World.W, x1); y1 = Mathf.Min(World.H, y1);
            vr = new Rect(map.x + x0 * s, map.y + y0 * s, Mathf.Max(0, x1 - x0) * s, Mathf.Max(0, y1 - y0) * s);
        }
        var wc = new Color(1, 1, 1, 0.85f); float t = Mathf.Max(1, U(1));
        Box(new Rect(vr.x, vr.y, vr.width, t), wc); Box(new Rect(vr.x, vr.yMax - t, vr.width, t), wc); Box(new Rect(vr.x, vr.y, t, vr.height), wc); Box(new Rect(vr.xMax - t, vr.y, t, vr.height), wc);
    }
    static string ActIcon(string type)
    {
        switch (type)
        {
            case "home": return "ui:house"; case "arcade": case "academy": case "historian": return "ui:book"; case "lasso": case "uya": return "ui:horse";
            case "general": case "setsen": return "ui:trophy"; case "shop": return "ui:shop"; case "board": return "ui:scroll"; case "well": return "ui:drop";
            case "zel": return "beast:mare"; case "cow1": case "cow2": return "beast:cow"; case "goat1": case "goat2": return "beast:goat";
            case "churn": return "dairy:airag"; case "tulga": return "dairy:urum"; case "rack": return "dairy:aaruul"; case "fish": return "ui:fish";
            case "sheep": case "emee": return "ui:heart"; case "shear": return "part:wool"; case "felt": return "part:esgii"; case "zasuul": case "arena": return "ui:wrestle";
            case "ovoo": return "ui:ovoo"; case "archery": return "ui:bow"; case "npcq": return "ui:chat"; case "lot": return "ui:ger"; case "basar": return "ui:dog";
        }
        return "ui:star";
    }
    /** The 🔊 button: every sound on or off. Faded with a red stroke across while muted. */
    public bool MuteButton(Rect r)
    {
        bool m = Music.Muted; var was = GUI.color; if (m) GUI.color = new Color(1, 1, 1, 0.5f);
        bool hit = Round(r, Emo.Icon("🔊"), null, m ? sBtn : sChip);
        GUI.color = was;
        if (m)
        {
            var piv = r.center; var mx = GUI.matrix; GUIUtility.RotateAroundPivot(-45, piv);
            GUI.DrawTexture(new Rect(piv.x - r.width * 0.36f, piv.y - U(1.5f), r.width * 0.72f, U(3)), Texture2D.whiteTexture, ScaleMode.StretchToFill, false, 0, Px.Col("#e14b3d"), 0, 0);
            GUI.matrix = mx;
        }
        if (hit) Later(Music.ToggleMute);
        return hit;
    }
    /** A round parchment button (.v-round) with an icon or a short text. */
    public bool Round(Rect r, PxCanvas icon, string text = null, GUIStyle st = null)
    {
        bool hit = Button(r, "", st ?? sChip);
        if (icon != null) { float s = r.width * 0.62f; GUI.DrawTexture(new Rect(r.center.x - s / 2, r.center.y - s / 2 - U(2), s, s), Tex(icon), ScaleMode.ScaleToFit); }
        else if (text != null) GUI.Label(new Rect(r.x, r.y, r.width, r.height - U(4)), text, new GUIStyle(sPixel) { fontSize = PxSize(U(16)), normal = { textColor = Px.Col("#4a2a12") } });
        return hit;
    }
    void Hud(float vw, float vh)
    {
        sprintRect = Rect.zero;
        var lv = Rules.Level(V.xp); var ink = Px.Col("#4a2a12");
        var pix = new GUIStyle(sPixel) { fontSize = PxSize(U(15)), alignment = TextAnchor.MiddleLeft, normal = { textColor = ink } };
        var small = new GUIStyle(sLabel) { fontSize = Mathf.RoundToInt(U(11)), wordWrap = false, alignment = TextAnchor.MiddleLeft };
        // top left: your card (it opens the menu), your home province, the map
        string nm = MyName; float nameW = pix.CalcSize(new GUIContent(nm)).x / UiScale;
        float cw = Mathf.Min(vw * 0.46f, 8 + 30 + 8 + Mathf.Max(nameW + 16, 104) + 12);
        var card = UR(10, 10, cw, 46);
        if (Button(card, "", sChip)) Later(() => Open(new MenuModal()));
        Box(UR(17, 16, 32, 32), Px.Col("#8a4b1f")); Box(UR(18, 17, 30, 30), Px.Col("#9fd8f7")); GUI.DrawTexture(UR(18, 17, 30, 30), Tex(Px.Portrait(V.look)), ScaleMode.ScaleToFit);
        GUI.Label(UR(56, 13, cw - 64, 20), nm + " <size=" + Mathf.RoundToInt(U(10)) + "><color=#8a4b1f>▼</color></size>", new GUIStyle(pix) { richText = true });
        var badge = UR(56, 35, 34, 13); Box(badge, Px.Col("#8a4b1f")); GUI.Label(badge, Loc.L("ТҮВ ", "LV ") + lv.l, new GUIStyle(small) { fontSize = Mathf.RoundToInt(U(9)), alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } });
        Box(UR(95, 38, 66, 8), Px.Col("#8a4b1f")); Box(UR(96, 39, 64, 6), Px.Col("#e6c48a")); Box(UR(96, 39, 64 * Mathf.Clamp01(lv.cur / (float)lv.need), 6), Px.Col("#5c9a36"));
        var ai = Aimags.ById(V.aimag); var et = Aimags.EthnicById(V.eth);
        string loc = ai != null ? ai.name + (et != null ? " · " + et.name : "") : Loc.L("Нутгаа сонго", "Pick home");
        float lw = small.CalcSize(new GUIContent(loc)).x / UiScale + 40; var locR = UR(10, 62, Mathf.Min(lw, vw * 0.46f), 26);
        if (Button(locR, "", sChip)) Later(() => Open(new PassportPage()));
        GUI.DrawTexture(new Rect(locR.x + U(9), locR.y + U(4), U(14), U(14)), Tex(Emo.Icon("📍")), ScaleMode.ScaleToFit);
        GUI.Label(new Rect(locR.x + U(27), locR.y, locR.width - U(30), locR.height - U(4)), loc, small);
        float mh = 126 * World.H / (float)World.W + 14;
        if (!miniBig) VillageMap(UR(10, 94, 126, mh));
        // top right: coins, energy, the season, playing online, together on one screen, the guide
        float py0 = 10;
        void Pill(PxCanvas icon, string text, float h, GUIStyle st, Action click, string glow = null)
        {
            float tw = text != null ? st.CalcSize(new GUIContent(text)).x / UiScale : 0, iw = icon != null ? 20 + (text != null ? 6 : 0) : 0, w = 24 + iw + tw;
            var r = UR(vw - 10 - w, py0, w, h);
            if (Button(r, "", sChip) && click != null) Later(click);
            float x = r.x + U(12);
            if (icon != null) { GUI.DrawTexture(new Rect(x, r.y + (r.height - U(4) - U(18)) / 2, U(18), U(18)), Tex(icon), ScaleMode.ScaleToFit); x += U(iw); }
            if (text != null) GUI.Label(new Rect(x, r.y, U(tw) + U(4), r.height - U(4)), text, st);
            if (glow != null) GlowAt(glow, r);
            py0 += h + 6;
        }
        Pill(Px.Icon("ui:coin"), V.coins.ToString(), 34, pix, () => Open(new ShopModal("wear")), "coins");
        int en = Mathf.RoundToInt(V.energy);
        Pill(Emo.Icon("🍖"), en.ToString(), 34, en < 20 ? new GUIStyle(pix) { normal = { textColor = Px.Col("#c2302a") } } : pix, () => Open(new FoodModal("meat")), "energy");
        var sea = Seasons.Current(); string seaIcon = sea.id == "spring" ? "🌸" : sea.id == "autumn" ? "🍂" : sea.id == "winter" ? "❄️" : "☀️";
        Pill(Emo.Icon(seaIcon), sea.name.ToString(), 28, new GUIStyle(small) { fontSize = Mathf.RoundToInt(U(12)) }, () => Open(new SeasonModal()));
        int n = net.others.Count + 1;
        string netText = net.Online ? $"● {n} · {net.room}" : net.status == "solo" ? "○ " + Loc.L("Ганцаараа", "Solo") : "◌ " + Loc.L("Холбогдож…", "Connecting…");
        Pill(null, netText, 26, new GUIStyle(small) { fontSize = Mathf.RoundToInt(U(10)), normal = { textColor = Px.Col(net.Online ? "#2e7d32" : "#8a6a4a") } }, () => Open(new ModeModal()));
        if (race == null && battle == null) Pill(Emo.Icon("🤝"), null, 34, pix, () => Open(new TogetherModal()));
        Pill(null, "?", 34, new GUIStyle(pix) { fontSize = PxSize(U(22)) }, () => StartTutorial(true), "help");
        if (race != null) { RaceHud(vw, vh); return; }
        if (battle != null)
        {
            GUI.Box(UR(vw / 2 - 210, 56, 420, 34), BattleLabel(), sToast);
            if (TouchUI && Button(UR(vw - 166, vh - 184, 158, 56), Loc.L("Сум харвах (F)", "Shoot (F)"), sPrimary)) ShootNearest();
            else if (!TouchUI) GUI.Box(UR(vw / 2 - 160, 94, 320, 26), Loc.L("F: сум харвах · E: уургадах", "F: shoot · E: lasso"), sToast);
        }
        if (Time.timeSinceLevelLoad < 9 && modal == null && TouchUI) GUI.Box(UR(vw / 2 - 160, vh - 130, 320, 32), Loc.L("Зүүн доорх джойстикоор алх", "Walk with the joystick at the bottom left"), sNote); // on a computer, Bolormaa teaches the keys as you go
        // bottom left: Basar, chat, the bag, and "more" (3D, learn, quests, music)
        float ty = vh - 12 - 50, tx = 10;
        if (Round(UR(tx, ty, 50, 50), null, "•••")) Later(() => { moreOpen = !moreOpen; Sfx.Tap(); });
        var moreR = UR(tx, ty, 50, 50); ty -= 56;
        if (Round(UR(tx, ty, 50, 50), Px.Icon("ui:bag"))) Later(() => Open(new BagModal())); ty -= 56;
        var talkR = UR(tx, ty, 50, 50);
        if (Round(talkR, Px.Icon("ui:chat"))) Later(() => Open(new RoomModal()));
        if (net.unread > 0) { var b = new Rect(talkR.xMax - U(14), talkR.y - U(4), U(18), U(18)); Box(b, Px.Col("#e14b3d")); GUI.Label(b, net.unread.ToString(), new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } }); }
        ty -= 56;
        var basR = UR(tx, ty, 50, 50); if (Round(basR, Px.BankharFace(0), null, sChipHi)) Later(() => OpenBasar()); ty -= 56;
        if (riding && Round(UR(tx, ty, 50, 50), Px.Icon("ui:horse"))) Later(Dismount);
        GlowAt("basar", basR);
        if (moreOpen)
        {
            float my0 = vh - 12 - 50;
            if (Round(UR(tx + 58, my0, 50, 50), Emo.Icon("👁"))) Later(() => { moreOpen = false; TryFirstPerson(null); }); GlowAt("fp", UR(tx + 58, my0, 50, 50)); my0 -= 56;
            if (Round(UR(tx + 58, my0, 50, 50), Px.Icon("ui:book"))) Later(() => { moreOpen = false; Open(new LearnPage()); }); GlowAt("learn", UR(tx + 58, my0, 50, 50)); my0 -= 56;
            if (Round(UR(tx + 58, my0, 50, 50), Px.Icon("ui:scroll"))) Later(() => { moreOpen = false; Open(new QuestModal()); }); GlowAt("quests", UR(tx + 58, my0, 50, 50)); my0 -= 56;
            if (Round(UR(tx + 58, my0, 50, 50), StoryFace("chinggis", "smile", 0))) Later(() => { moreOpen = false; OpenStory(); }); GlowAt("story", UR(tx + 58, my0, 50, 50)); my0 -= 56; // the story mode
            MuteButton(UR(tx + 58, my0, 50, 50));
        }
        else { GlowAt("fp", moreR); GlowAt("learn", moreR); GlowAt("quests", moreR); GlowAt("story", moreR); }
        // bottom right: run, jump and the action button (the web version's v-bottom order)
        float right = vw - 10;
        if (ctx != null && modal == null)
        {
            string label = LabelFor(ctx); var lst = new GUIStyle(pix) { alignment = TextAnchor.MiddleCenter };
            float aw = Mathf.Max(86, lst.CalcSize(new GUIContent(label)).x / UiScale + 20); var ar = UR(right - aw, vh - 12 - 86, aw, 86);
            var here = ctx; if (Button(ar, "", sChip)) Later(() => DoAction(here));
            GUI.DrawTexture(new Rect(ar.center.x - U(18), ar.y + U(8), U(36), U(36)), Tex(Px.Icon(ActIcon(ctx.type))), ScaleMode.ScaleToFit);
            GUI.Label(new Rect(ar.x, ar.y + U(46), ar.width, U(18)), label, lst);
            if (!TouchUI) { var kr = new Rect(ar.center.x - U(9), ar.y + U(64), U(18), U(14)); Box(kr, Px.Col("#8a4b1f")); GUI.Label(kr, "E", new GUIStyle(small) { fontSize = Mathf.RoundToInt(U(9)), alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } }); }
            right -= aw + 8;
        }
        var jr = UR(right - 56, vh - 12 - 56, 56, 56); if (Round(jr, null)) Jump(); GUI.Label(new Rect(jr.x, jr.y, jr.width, jr.height - U(4)), "↑", new GUIStyle(sBig) { fontSize = Mathf.RoundToInt(U(28)), normal = { textColor = Px.Col("#4a2a12") } }); GlowAt("jump", jr); right -= 64;
        var rr = UR(right - 56, vh - 12 - 56, 56, 56); if (Round(rr, Emo.Icon("🏃"), null, runLock ? sSel : sChip)) runLock = !runLock; GlowAt("run", rr); right -= 64;
        // Basar's fact: its own card, bottom right above the button bar (the tallest button is 86 high), slides in from the right edge, out again after 12 s; a tap opens him
        if (dogBubble != null && modal == null)
        {
            float age = Time.time - dogBubble.t0, sl = BackOut(Mathf.Clamp01(age / 0.4f)) * Mathf.Clamp01((12 - age) / 0.4f), cwid = Mathf.Min(300, vw - 20), tW = U(cwid - 46 - 34);
            string ft = dogBubble.fact.ToString(); float whoH = sPopWho.CalcHeight(new GUIContent("БАСАР"), tW), ch = Mathf.Max(U(52), sPopText.CalcHeight(new GUIContent(ft), tW) + whoH + U(18));
            var cr = new Rect(U(vw) - (U(cwid) + U(10)) * sl, U(vh - 12 - 86 - 10) - ch, U(cwid), ch);
            GUI.Box(cr, GUIContent.none, sPopBasar);
            var cb = new Rect(cr.x + U(10), cr.y + (ch - U(46)) / 2, U(46), U(46)); GUI.DrawTexture(cb, Tex(Badge("#f7c646"))); GUI.DrawTexture(new Rect(cb.x + U(5), cb.y + U(5), cb.width - U(10), cb.height - U(10)), Tex(Px.BankharFace(1)), ScaleMode.ScaleToFit);
            GUI.Label(new Rect(cb.xMax + U(12), cr.y + U(8), tW, whoH), Loc.L("БАСАР", "BASAR"), sPopWho);
            GUI.Label(new Rect(cb.xMax + U(12), cr.y + U(8) + whoH, tW, ch - whoH - U(16)), ft, sPopText);
            Ui(cr); if (GUI.Button(cr, GUIContent.none, GUIStyle.none)) Later(() => OpenBasar());
        }
        // bottom middle: your milk and the dairy foods you made. Tap a food to eat it.
        var have = Rules.DAIRY_ORDER.Where(k => V.Have(k) > 0).ToList();
        float left = moreOpen ? 126 : 68, room = right - left;
        if (gbOn && IsoOn) GerBuildGui(left, right, vw, vh); // building my ger: the parts tray takes this row (GerBuild.cs)
        else if (have.Count == 0)
        {
            string go =Loc.L("Саах →", "Go milk →"); float gw = pix.CalcSize(new GUIContent(go)).x / UiScale + 56;
            var gr = UR(left + (room - gw) / 2, vh - 12 - 46, gw, 46);
            if (Button(gr, "", sSlot)) Later(() => GoTo(9, 14, null)); // nothing yet: a shortcut to the dairy yard
            GUI.DrawTexture(new Rect(gr.x + U(8), gr.y + U(9), U(28), U(28)), Tex(Px.Icon("beast:cow")), ScaleMode.ScaleToFit);
            GUI.Label(new Rect(gr.x + U(42), gr.y, gr.width - U(46), gr.height), go, new GUIStyle(pix) { normal = { textColor = Px.Col("#5a2d10") } });
        }
        else
        {
            float hw2 = Mathf.Min(520, have.Count * 50 + 8), x = left + (room - hw2) / 2 + 6;
            foreach (var k in have)
            {
                var r = UR(x, vh - 12 - 46, 46, 46);
                if (Button(r, "", sSlot)) Later(() => { if (Rules.DAIRY[k].food) Eat(k); else Toast($"{Rules.DAIRY[k].name}: " + Loc.L("тавиур, тулга, хөхүүр дээр цагаан идээ хийнэ.", "make dairy foods at the rack, stove or churn.")); });
                GUI.DrawTexture(new Rect(r.x + U(9), r.y + U(9), U(28), U(28)), Tex(Px.Icon("dairy:" + k)), ScaleMode.ScaleToFit);
                Outlined(r.xMax - U(10), r.yMax - U(9), V.Have(k).ToString(), U(10), Color.white);
                x += 50;
            }
        }
    }
    /** The big map in the middle of the screen, over everything else in the village. */
    void BigMap(float vw, float vh)
    {
        if (!miniBig) return;
        Ui(new Rect(-guiOX, -guiOY, Screen.width, Screen.height)); // taps on the dimmed village don't walk there
        Box(new Rect(-guiOX, -guiOY, Screen.width, Screen.height), new Color(0.1f, 0.06f, 0.12f, 0.45f));
        float w = Mathf.Min(vw * 0.92f, 640), h = w * World.H / World.W + 14;
        var e = Event.current;
        var r = UR((vw - w) / 2, (vh - h) / 2, w, h);
        if (e.type == EventType.MouseDown && !r.Contains(e.mousePosition)) { e.Use(); Later(() => miniBig = false); }
        VillageMap(r);
    }

    void DrawModal()
    {
        if (modal.FullScreen) { modal.Draw(this); return; }
        // the scene stays behind its windows. It is drawn in every event, not only on repaint: GUILayout pairs areas by the
        // order they are made in, so a page under a window (its BeginArea) must also be there in the layout pass. Taps, keys,
        // the wheel and drags reach only the window on top: the page sees an Ignore event instead, which still walks its layout.
        var sc = SceneUnder;
        if (sc != null)
        {
            var cur = Event.current;
            if (cur.type != EventType.Layout && cur.type != EventType.Repaint) Event.current = new Event { type = EventType.Ignore, mousePosition = new Vector2(-1e5f, -1e5f) };
            try { sc.Draw(this); } finally { Event.current = cur; }
        }
        // dim the village, then a centred window that grows with its content
        Box(new Rect(0, 0, Screen.width, Screen.height), new Color(0.1f, 0.06f, 0.12f, 0.55f));
        // a tap on the dimmed village outside the window closes it, as the ✕ does
        var e = Event.current;
        if (modal.CanEscape && e.type == EventType.MouseDown && modalPanel.width > 0 && !modalPanel.Contains(e.mousePosition)) { e.Use(); Later(Close); return; }
        float w = Mathf.Min(U(modal.Width), Screen.width - U(24)), h = Screen.height - U(24);
        GUILayout.BeginArea(new Rect((Screen.width - w) / 2, U(12), w, h));
        GUILayout.FlexibleSpace();
        GUILayout.BeginVertical(sPanel);
        var m = modal; m.winScroll = BeginScroll(m.winScroll, h - U(40));
        var sp = m.Speaker; PxCanvas port = sp != null ? Portrait(sp.Value.who, sp.Value.face, spWho == sp.Value.who && Time.time - spAt < 1.6f && (int)(Time.time * 9) % 2 == 0, (Time.time + sp.Value.who.Length) % 3.4f < 0.12f) : null;
        if (port != null)   // the one you are talking to, in a portrait at the window's left
        {
            GUILayout.BeginHorizontal();
            var pr = GUILayoutUtility.GetRect(U(104), U(134), GUILayout.Width(U(104)), GUILayout.Height(U(134)));
            Box(new Rect(pr.x - U(3), pr.y - U(3), pr.width + U(6), pr.height + U(6)), Px.Col("#5a2d10")); Box(pr, Px.Col("#34286e"));
            GUI.DrawTexture(pr, Tex(port), ScaleMode.ScaleToFit);
            GUILayout.Space(U(12)); GUILayout.BeginVertical();
        }
        m.Draw(this);
        if (port != null) { GUILayout.EndVertical(); GUILayout.EndHorizontal(); }
        EndScroll();
        GUILayout.EndVertical();
        if (e.type == EventType.Repaint) { var pr = GUILayoutUtility.GetLastRect(); modalPanel = new Rect(pr.x + (Screen.width - w) / 2, pr.y + U(12), pr.width, pr.height); }
        GUILayout.FlexibleSpace();
        GUILayout.EndArea();
        if (m.CanEscape && modalPanel.width > 0 && GUI.Button(new Rect(modalPanel.xMax - U(44), modalPanel.y + U(10), U(34), U(34)), "×", sBtn)) Later(() => { if (modal == m) Close(); });
        DrawSpeaker(m);
    }
    string spWho, spFace; float spAt = -9;
    /** Notes when the person you are talking to changes face, so their portrait talks for a moment (mouth moving). */
    void DrawSpeaker(Modal m)
    {
        var sp = m.Speaker; if (sp == null) { spWho = null; return; }
        var (who, face) = sp.Value;
        if (who != spWho || face != spFace) { spWho = who; spFace = face; spAt = Time.time; }
    }

    // ---------------------------------------------------------------- scroll views that a finger can drag and that fit their content
    // IMGUI scroll views only follow the mouse wheel and their scrollbar, and they either collapse or stretch instead of
    // fitting what is inside. These remember last frame's content height and view rect (by the order they are drawn in).
    readonly List<Rect> scrollViews = new List<Rect>(); readonly List<float> scrollContent = new List<float>(), scrollWheel = new List<float>(); readonly List<Vector2> scrollAt = new List<Vector2>(); readonly Stack<int> scrollOpen = new Stack<int>();
    int scrollN, scrollArmed = -1; bool scrollMoving; Vector2 scrollFrom;
    // a finger on a picture you draw or play on (Live, below) belongs to it, not to the scroll view around it
    List<Rect> drawRects = new List<Rect>(), drawNext = new List<Rect>();
    bool OnDrawing(Vector2 gui) { var p = GUIUtility.GUIToScreenPoint(gui); foreach (var r in drawRects) if (r.Contains(p)) return true; return false; }
    public void DrawingAt(Rect gui) { if (Event.current.type == EventType.Repaint && scrollOpen.Count > 0) drawNext.Add(GUIUtility.GUIToScreenRect(gui)); }
    /** maxH > 0: as tall as the content, at most maxH (screen pixels); otherwise it fills the space it is given. */
    public Vector2 BeginScroll(Vector2 pos, float maxH = -1)
    {
        int k = scrollN++; scrollOpen.Push(k);
        while (scrollViews.Count <= k) { scrollViews.Add(Rect.zero); scrollContent.Add(-1); scrollWheel.Add(0); scrollAt.Add(Vector2.zero); }
        if (scrollWheel[k] != 0) { pos.y += scrollWheel[k]; scrollWheel[k] = 0; }
        var e = Event.current; var view = scrollViews[k];
        if (e.type == EventType.MouseDown && TouchUI && view.Contains(e.mousePosition) && !OnDrawing(e.mousePosition)) { scrollArmed = k; scrollMoving = false; scrollFrom = e.mousePosition; }
        else if (e.type == EventType.MouseDrag && scrollArmed == k)
        {
            if (!scrollMoving && (e.mousePosition - scrollFrom).magnitude > U(6)) { scrollMoving = true; GUIUtility.hotControl = 0; } // a drag is never a button press
            if (scrollMoving) { pos.y -= e.delta.y; e.Use(); }
        }
        else if (e.type == EventType.MouseUp && scrollArmed == k) { scrollArmed = -1; if (scrollMoving) { scrollMoving = false; e.Use(); } }
        var opts = maxH > 0 ? new[] { GUILayout.Height(Mathf.Min(scrollContent[k] > 0 ? scrollContent[k] + 2 : maxH, maxH)) } : new GUILayoutOption[0];
        pos = GUILayout.BeginScrollView(pos, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, opts); scrollAt[k] = pos;
        // the content is exactly as wide as the view (less the scrollbar): without this one wide row widened everything, and long
        // lines ran past the right edge and were cut off
        float sbw = GUI.skin.verticalScrollbar.fixedWidth > 0 ? GUI.skin.verticalScrollbar.fixedWidth : 15;
        if (view.width > sbw + 20) GUILayout.BeginVertical(GUILayout.Width(view.width - sbw - 2)); else GUILayout.BeginVertical();
        return pos;
    }
#if UNITY_WEBGL && !UNITY_EDITOR
    [System.Runtime.InteropServices.DllImport("__Internal")] static extern float NaadamyWheel(); // Plugins/WebGL/NaadamyPointer.jslib
    /** The browser counts the distance before Unity sees the event (its listener runs first), and Unity may hand the event over a
     *  frame later: keep the distance until an event takes it, and drop it when nothing did (the wheel turned over something that
     *  doesn't scroll). Read every frame, and again when an event comes. */
    void ReadWheel()
    {
        float v = NaadamyWheel() * Dpr;
        if (v != 0) { wheelNow += v; wheelT = Time.unscaledTime; } else if (Time.unscaledTime - wheelT > 0.25f) wheelNow = 0;
    }
#else
    void ReadWheel() { }
#endif
    float wheelNow, wheelT; // screen pixels the browser would have scrolled, not yet used
    /** In a browser Unity gives every wheel event the same size, a mouse notch and a trackpad's tiny nudge alike, so a trackpad
     *  raced down the page: scroll half as far as the browser itself would (a mouse notch is about 50 CSS pixels). */
    float WheelStep(Event e)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        ReadWheel(); float w = wheelNow; wheelNow = 0; return w * 0.5f;
#else
        return e.delta.y * U(16);
#endif
    }
    public void EndScroll()
    {
        int k = scrollOpen.Count > 0 ? scrollOpen.Pop() : 0;
        GUILayout.EndVertical();
        if (Event.current.type == EventType.Repaint) scrollContent[k] = GUILayoutUtility.GetLastRect().height;
        // the wheel scrolls the innermost view under the mouse, by how far it really moved (WheelStep)
        var e = Event.current;
        if (e.type == EventType.ScrollWheel && scrollViews[k].Contains(e.mousePosition + scrollViews[k].position - scrollAt[k])) { scrollWheel[k] += WheelStep(e); e.Use(); }
        GUILayout.EndScrollView();
        if (Event.current.type == EventType.Repaint) scrollViews[k] = GUILayoutUtility.GetLastRect();
    }

    // little layout helpers for the windows
    public void Title(string t) => GUILayout.Label(t, sTitle);
    public void Text(string t) => GUILayout.Label(t, sLabel);
    public void Small(string t) => GUILayout.Label(t, sSmall);
    public void Gap(float v = 8) => GUILayout.Space(U(v));
    /** A text box like the web version's big input, with a grey hint while it is empty. */
    public string Field(string v, int max, string hint)
    {
        var r = GUILayoutUtility.GetRect(U(100), U(56), GUILayout.ExpandWidth(true));
        v = GUI.TextField(r, v, max, sField);
        if (string.IsNullOrEmpty(v)) GUI.Label(r, hint, new GUIStyle(sField) { normal = { background = null, textColor = Px.Col("#a8957e") } });
        return v;
    }
    /** The app's logo, as the web version's blue tile with the ger. */
    public void Logo(Rect r) { GUI.Box(r, GUIContent.none, sLogo); GUI.DrawTexture(new Rect(r.x + r.width * 0.14f, r.y + r.height * 0.12f, r.width * 0.72f, r.height * 0.72f), Tex(Px.Icon("ui:logo")), ScaleMode.ScaleToFit); }
    /** The step bar along the top of a card: green for the steps done. */
    public void Steps(int step, int n)
    {
        var r = GUILayoutUtility.GetRect(U(100), U(6), GUILayout.ExpandWidth(true)); float gap = U(6), w = (r.width - gap * (n - 1)) / n;
        for (int k = 0; k < n; k++) Box(new Rect(r.x + k * (w + gap), r.y, w, r.height), Px.Col(k <= step ? "#5c9a36" : "#e6c48a"));
        Gap(12);
    }
    public bool Btn(string t, bool primary = false, float h = 40, bool enabled = true)
    {
        bool was = GUI.enabled; GUI.enabled = enabled;
        bool hit = GUILayout.Button(t, primary ? sPrimary : sBtn, GUILayout.MinHeight(U(h)));
        GUI.enabled = was; return hit;
    }
    public bool SmallBtn(string t, bool primary = true, float w = 96, bool enabled = true)
    {
        bool was = GUI.enabled; GUI.enabled = enabled;
        var st = primary ? sPrimary : sBtn; float need = st.CalcSize(new GUIContent(t)).x + U(4); // never narrower than its word: a word broken in two looks broken
        bool hit = GUILayout.Button(t, st, GUILayout.Width(Mathf.Max(U(w), need)), GUILayout.MinHeight(U(34)));
        GUI.enabled = was; return hit;
    }
    public void Fact(string text, bool first = true, string head = null)
    {
        GUILayout.BeginVertical(sBtn);
        GUILayout.Label("<b>" + (head ?? (first ? Loc.L("Мэдсэн үү?", "Did you know?") : Loc.L("Сануулга", "Remember"))) + "</b>", sLabel);
        GUILayout.Label(text, sLabel);
        GUILayout.EndVertical();
    }
    public void Picture(PxCanvas art, float height)
    {
        var r = GUILayoutUtility.GetRect(U(height) * art.W / art.H, U(height), GUILayout.ExpandWidth(true));
        float w = r.height * art.W / art.H; GUI.DrawTexture(new Rect(r.center.x - w / 2, r.y, w, r.height), Tex(art));
    }
    /** A picture fitted into a size × size box, keeping its shape: people are taller than they are wide. */
    public void IconAt(PxCanvas art, float size)
    {
        float k = U(size) / Mathf.Max(art.W, art.H), w = art.W * k, h = art.H * k;
        var r = GUILayoutUtility.GetRect(w, h, GUILayout.Width(w), GUILayout.Height(h)); GUI.DrawTexture(r, Tex(art));
    }
    /** Empty cells that finish a short last row of a grid, so its buttons stay as wide as the ones above. */
    public void PadRow(int cells, float w, float h) { for (int i = 0; i < cells; i++) GUILayoutUtility.GetRect(U(w), U(h), GUILayout.ExpandWidth(true)); }
    /** How many grid cells fit across a page when each must hold the longest of these labels plus `extra` units. */
    public int GridCols(IEnumerable<string> labels, GUIStyle st, float extra, int max = 6)
    {
        float w = 0; foreach (var l in labels) w = Mathf.Max(w, st.CalcSize(new GUIContent(l)).x);
        return Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(Screen.safeArea.width - U(16), U(760)) - U(24)) / (w + U(extra))), 2, max);
    }
    /** A row in a list: icon, a bold name and a small line, and room for a button after (call EndRow). */
    public void Row(PxCanvas icon, string name, string sub, bool dim = false)
    {
        GUILayout.BeginHorizontal(sBtn);
        var old = GUI.color; if (dim) GUI.color = new Color(1, 1, 1, 0.55f);
        if (icon != null) IconAt(icon, 32);
        GUILayout.BeginVertical();
        GUILayout.Label("<b>" + name + "</b>", sLabel);
        if (!string.IsNullOrEmpty(sub)) GUILayout.Label(sub, sSmall);
        GUILayout.EndVertical();
        GUI.color = old;
    }
    public void EndRow() => GUILayout.EndHorizontal();
    /** What someone says, in a bubble with its tail pointing at them (tip: the speaker's head, in screen GUI coordinates). The words
     *  wrap at maxW units and the bubble is as big as they are; it stays on the screen, and near the top it hangs below the speaker. */
    public Rect SpeechBubble(Vector2 tip, string text, float maxW = 230, float alpha = 1)
    {
        var gc = new GUIContent(text); float w = Mathf.Clamp(sSpeech.CalcSize(gc).x + U(2), U(40), U(maxW)), h = sSpeech.CalcHeight(gc, w), tail = U(7);
        float x = Mathf.Clamp(tip.x - w / 2, U(4), Screen.width - w - U(4)), y = tip.y - tail - h; bool below = y < U(4);
        if (below) y = tip.y + tail;
        var old = GUI.color; GUI.color = new Color(1, 1, 1, alpha);
        GUI.Box(new Rect(x, y, w, h), text, sSpeech);
        GUI.color = old;
        // the tail: a little triangle in the bubble's colours, pointing at whoever is talking
        float tx = Mathf.Clamp(tip.x, x + U(12), x + w - U(12)); Color edge = Px.Col("#5a2d10"), fill = Px.Col("#f6dca8"); edge.a = fill.a = alpha;
        for (int i = 0; i < 4; i++)
        {
            float half = (4 - i) * U(1.6f), yy = below ? y - (i + 1) * tail / 4 : y + h - U(2) + i * tail / 4;
            Box(new Rect(tx - half - U(1.5f), yy, half * 2 + U(3), tail / 4 + 1), edge); Box(new Rect(tx - half, yy, half * 2, tail / 4 + 1), fill);
        }
        return new Rect(x, y, w, h);
    }
    readonly Dictionary<string, float> gridW = new Dictionary<string, float>();
    /** Cells of equal width in rows, each row as tall as its tallest cell. measure(i, w) gives cell i's height at width w (measure
     *  wrapped text with CalcHeight), draw(i, rect) draws it with GUI calls. GUILayout guesses the height of wrapped text in a row
     *  from the wrong width, so text spilled out of buttons and tiles; this measures it at the width each cell really gets. */
    public void Grid(string key, int n, float minCell, Func<int, float, float> measure, Action<int, Rect> draw, float gap = 6)
    {
        if (n <= 0) return;
        gap = U(gap); float w = gridW.TryGetValue(key, out var gw) && gw > 1 ? gw : U(420);
        int cols = Mathf.Clamp(Mathf.FloorToInt((w + gap) / (U(minCell) + gap)), 1, n);
        float cw = (w - gap * (cols - 1)) / cols;
        var rows = new List<float>();
        for (int i = 0; i < n; i += cols) { float h = 0; for (int k = i; k < Mathf.Min(n, i + cols); k++) h = Mathf.Max(h, measure(k, cw)); rows.Add(h); }
        float total = 0; foreach (var h in rows) total += h; total += gap * (rows.Count - 1);
        var area = GUILayoutUtility.GetRect(10, total, GUILayout.ExpandWidth(true));
        if (Event.current.type == EventType.Repaint && area.width > 1) gridW[key] = area.width;
        float y = area.y;
        for (int r = 0; r < rows.Count; r++)
        {
            for (int c = 0; c < cols && r * cols + c < n; c++) draw(r * cols + c, new Rect(area.x + c * (cw + gap), y, cw, rows[r]));
            y += rows[r] + gap;
        }
    }
    public void Stats(params (string k, string v)[] items)
    {
        GUILayout.BeginHorizontal();
        foreach (var (k, v) in items) { GUILayout.BeginVertical(sBtn); GUILayout.Label(k, sSmall); GUILayout.Label("<b>" + v + "</b>", sTitle); GUILayout.EndVertical(); }
        GUILayout.EndHorizontal();
    }
    /** Chips to pick one of several values; returns the picked index or -1. */
    public int Chips(IList<string> labels, int current, float minW = 60, bool tabs = false)
    {
        // three to a row on one line each: pixel letters are wide, and wrapped chips spill over each other
        int hit = -1; GUILayout.BeginHorizontal(); GUIStyle on = new GUIStyle(tabs ? sPrimary : sSel) { wordWrap = false }, off = new GUIStyle(sBtn) { wordWrap = false }; // tabs fill orange, a picked option gets a red outline
        float w = U(minW); foreach (var l in labels) w = Mathf.Max(w, off.CalcSize(new GUIContent(l)).x); // all as wide as the widest, so the rows line up
        for (int i = 0; i < labels.Count; i++) { if (i > 0 && i % 3 == 0) { GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); } if (GUILayout.Button(labels[i], i == current ? on : off, GUILayout.MinWidth(w), GUILayout.MinHeight(U(36)))) hit = i; }
        if (labels.Count > 3) for (int i = labels.Count % 3; i > 0 && i < 3; i++) GUILayoutUtility.GetRect(w, U(36), GUILayout.ExpandWidth(true)); // a short last row keeps the same chip width
        GUILayout.EndHorizontal(); return hit;
    }
    public bool Swatch(Color c, bool on, float size = 36)
    {
        var r = GUILayoutUtility.GetRect(U(size), U(size), GUILayout.Width(U(size)), GUILayout.Height(U(size)));
        bool hit = GUI.Button(r, "", on ? sSel : sTile); Box(new Rect(r.x + U(5), r.y + U(5), r.width - U(10), r.height - U(10)), c); GUILayout.Space(U(6)); return hit;
    }

    // ---------------------------------------------------------------- a picture redrawn every frame (mini-games, scenes)
    public sealed class Live
    {
        public readonly PxCanvas c; readonly Texture2D tex; public Rect last;
        public Live(int w, int h) { c = new PxCanvas(w, h); tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp }; }
        /** Draws the picture into a rectangle taken from the layout, as big as fits the width (keeping its shape). */
        public Rect Show(Game g, float maxH)
        {
            var r = GUILayoutUtility.GetRect(10, g.U(maxH), GUILayout.ExpandWidth(true));
            float s = Mathf.Min(r.width / c.W, r.height / c.H); var rr = new Rect(r.center.x - c.W * s / 2, r.y, c.W * s, c.H * s);
            ShowAt(rr); return rr;
        }
        public void ShowAt(Rect rr) { last = rr; I?.DrawingAt(rr); if (Event.current.type == EventType.Repaint) { c.Upload(tex); GUI.DrawTexture(rr, tex); } }
        /** Canvas pixel to screen (GUI) point, for text over the picture. */
        public Vector2 At(float x, float y) => new Vector2(last.x + x * last.width / c.W, last.y + y * last.height / c.H);
        /** Screen (GUI) point to canvas pixel. */
        public Vector2 Pick(Vector2 gui) => new Vector2((gui.x - last.x) * c.W / last.width, (gui.y - last.y) * c.H / last.height);
        public float Scale => last.width / c.W;
    }

    // ---------------------------------------------------------------- simple messages and fact cards
    public sealed class InfoModal : Modal
    {
        readonly string title, text;
        public InfoModal(string title, string text) { this.title = title; this.text = text; }
        public override void Draw(Game g) { g.Title(title); g.Text(text); g.Gap(); if (g.Btn(Loc.L("Ойлголоо", "OK"), true)) g.Later(g.Close); }
        public override void Press(Game g) => g.Close();
    }
    public sealed class FactModal : Modal
    {
        readonly PxCanvas pic; readonly string title, fact, head;
        public FactModal(PxCanvas pic, string title, string fact, string head = null) { this.pic = pic; this.title = title; this.fact = fact; this.head = head; }
        public override void Draw(Game g)
        {
            if (pic != null) g.Picture(pic, 72);
            if (head != null) GUILayout.Label(head, new GUIStyle(g.sSmall) { alignment = TextAnchor.MiddleCenter });
            GUILayout.Label(title, new GUIStyle(g.sTitle) { alignment = TextAnchor.MiddleCenter });
            g.Fact(fact); g.Gap();
            if (g.Btn(Loc.L("Гайхалтай!", "Cool!"), true)) g.Later(g.Close);
        }
        public override void Press(Game g) => g.Close();
    }
    /** Basar introduces himself the first time you come to Нутаг. */
    sealed class TipsModal : Modal
    {
        public override void Draw(Game g)
        {
            g.Picture(Px.BankharFace(0), 72);
            g.Title(Loc.L($"Вуф! Сайн уу, {g.MyName}!", $"Woof! Hi {g.MyName}!"));
            g.Text(Loc.L("Би Басар, монгол банхар нохой. Нутагт үргэлж хажууд чинь явна. Намайг дарж юу ч асуугаарай!", "I’m Basar, a Mongolian Bankhar dog. I’ll walk beside you everywhere in Nutag. Tap me to ask me anything!"));
            foreach (var (ic, t) in new[]
            {
                ("ui:star", Loc.L("Газар дээр дарж алх. Компьютер дээр сум эсвэл WASD товч.", "Tap the ground to walk. On a computer use arrows or WASD.")),
                ("beast:cow", Loc.L("Хот айлд үнээ, ямаа, гүүгээ саагаад ааруул, өрөм, тараг, айраг хий.", "In the dairy yard milk the cows, goats and mares, then make aaruul, öröm, tarag and airag.")),
                ("ui:book", Loc.L("Сургуулийн гэрт гэр барих, цагаан идээ хийх, Нууц товчоог сур. Сурснаа гэр бүлтэйгээ хий!", "In the school ger learn to build a ger, make dairy foods and hear the Secret History. Then do it with your family!")),
                ("ui:ger", Loc.L("Гэр хороололд өөрийн гэрээ бариарай.", "Build your own ger in the ger neighbourhood.")),
                ("ui:horse", Loc.L("Морины уяанаас морь унаж давхих, эсвэл уралдаанд оролцоорой!", "Take a horse from the horse line to ride freely or race!")),
            }) { GUILayout.BeginHorizontal(g.sBtn); g.IconAt(Px.Icon(ic), 32); GUILayout.Label(t, new GUIStyle(g.sLabel) { alignment = TextAnchor.MiddleLeft }); GUILayout.EndHorizontal(); }
            g.Gap();
            if (g.Btn(Loc.L("Эхлэх", "Let's go"), true, 48)) g.Later(g.Close);
        }
        public override void Closed(Game g) { g.V.tips = true; g.V.Save(); }
        public override void Press(Game g) => g.Close();
    }
}

static class GuiStyleExt
{
    /** The same text style without the background, for labels drawn on top of a chip. */
    public static GUIStyle Clean(this GUIStyle s) { var c = new GUIStyle(s) { padding = new RectOffset(0, 0, 0, 0) }; c.normal.background = null; return c; }
}
