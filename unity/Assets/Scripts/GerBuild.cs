// Building your ger ON YOUR PLOT in the isometric Нутаг (the hands-on steps in GerWork.cs stay for the flat view): the ger grows on
// its plot piece by piece (BodiesGer.cs); a tray at the bottom holds the parts you own and you DRAG one onto the ger (mouse or finger).
// The right next part snaps in (GerRules.Try, the same save and multiplayer sync as before); the wrong one bounces back and
// Grandpa Craftsman says why. A missing part shows where to get it, points the way in the village and offers "Go there".
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed partial class Game
{
    bool gbOn; int gbLot = -1, gbWrong;
    static readonly List<int> gbNoSlots = new List<int>();
    List<int> gbSlots => V?.ger == null ? gbNoSlots : V.ger.poles ??= new List<int>();   // roof poles placed so far (slots round the crown, in order), kept in the save
    static float PoleA(int j) => j * Mathf.PI * 2 / Bodies.GER_POLES;
    List<int> GbSides() => gbSlots.Select(j => SideOfAngle(PoleA(j))).ToList();   // which side of the ger each pole is on
    Tx gbSay; float gbSayT = -99; bool gbList;
    sealed class GbHold { public int id; public string part; public Vector2 gui, from; }
    GbHold gbHold;
    readonly List<(string part, Rect r)> gbChips = new List<(string, Rect)>();   // tray slots in screen GUI coordinates
    sealed class GbFly { public string part; public Vector2 from, to; public float t0; public bool bad; }
    readonly List<GbFly> gbFlies = new List<GbFly>();
    sealed class GbAnim { public int step, slot = -1, baseStage; public List<int> baseSlots; public float t0; }
    GbAnim gbAnim;
    GameObject gbRoot, gbBaseGo, gbPieceGo, gbOthersRoot; int gbKLot = -9, gbKStage = -9, gbKSig = -9; string gbKRoof, gbKDoor;
    sealed class GbOther { public int stage; public string roof, door; public GameObject go; }
    readonly Dictionary<int, GbOther> gbOthers = new Dictionary<int, GbOther>();
    readonly Dictionary<int, bool> gbSignOn = new Dictionary<int, bool>();
    GUIStyle gbChipLbl, gbBody, gbTitle, gbMat; GUIStyle gbStyleFor;
    static PxCanvas gbArrow;

    static readonly Tx GB_FLOOR = T2("шал", "the floor boards"), GB_STOVE = T2("тулга", "the stove"), GB_FLOOR_CHIP = T2("шал", "floor");
    WObj GbLotObj => World.objects.Find(q => q.kind == "lot" && q.i == (V.lot.i >= 0 ? V.lot.i : gbLot));

    // ---------------------------------------------------------------- opening and the pointer
    /** Tapping my plot in the isometric view: the tray opens and the ger is built there. */
    void GerBuildOpen(int i)
    {
        if (gbOn) return;
        gbLot = V.lot.i >= 0 ? V.lot.i : i; gbOn = true; gbWrong = 0; gbSayT = -99; gbList = false; together = false;
        if (G.stage != 4) gbSlots.Clear(); // poles only wait at the roof-pole step
        Sfx.Tap();
        Toast(Loc.L("Хэсгүүдээ тавиураас гэр рүүгээ чир!", "Drag the parts from the tray onto your ger!"));
    }
    string StepOfPart(string part) => part == "shal" || part == "tulga" ? part : GerRules.STEPS.First(s => GerRules.NEEDS[s].ContainsKey(part));
    string PartLabel(string k) => k == "shal" ? GB_FLOOR_CHIP.ToString() : k == "tulga" ? GB_STOVE.ToString() : PartName(k);
    int PartCount(string k) => k == "shal" || k == "tulga" ? 1 : Part(k) - (k == "uni" && G.stage == 4 ? gbSlots.Count : 0);

    /** Called with every pointer event; true when the tray's drag takes it. pos: screen pixels, y up. */
    bool GerBuildPointer(int id, Vector2 pos, bool down, bool up)
    {
        if (gbHold != null && (modal != null || !IsoOn)) gbHold = null;
        if (!gbOn || !IsoOn || modal != null) return false;
        var gui = new Vector2(pos.x, Screen.height - pos.y);
        if (gbHold == null)
        {
            if (!down) return false;
            foreach (var (part, r) in gbChips) if (r.Contains(gui)) { gbHold = new GbHold { id = id, part = part, gui = gui, from = gui }; drag = null; Sfx.Tap(); return true; }
            return false;
        }
        if (gbHold.id != id) return false;
        gbHold.gui = gui;
        if (up) { var h = gbHold; gbHold = null; GbDrop(h, pos); }
        return true;
    }

    /** Whether a screen point (y up) is over my ger on its plot. */
    bool OnMyGer(Vector2 screen)
    {
        var o = GbLotObj; if (o == null) return false;
        var c = cam.WorldToScreenPoint(new Vector3(o.x + o.w / 2f, -(o.y + o.h / 2f), -1.1f)); float u = ScreenPpu;
        float dx = (screen.x - c.x) / (1.9f * u), dy = (screen.y - c.y) / (2.1f * u);
        return dx * dx + dy * dy < 1;
    }
    void GbFlyBack(GbHold h, bool bad)
    {
        var chip = gbChips.Find(c => c.part == h.part); if (chip.part == null) return;
        gbFlies.Add(new GbFly { part = h.part, from = h.gui, to = chip.r.center, t0 = Time.time, bad = bad });
    }
    void GbSaySet(Tx t) { gbSay = t; gbSayT = Time.time; }
    void GbAnimStart(GbAnim a) { gbAnim = a; if (gbPieceGo != null) { Destroy(gbPieceGo); gbPieceGo = null; } }

    // ---------------------------------------------------------------- a part dropped
    void GbDrop(GbHold h, Vector2 screen)
    {
        if (G.stage >= GerRules.DONE) return;
        if (!OnMyGer(screen))
        {
            if ((h.gui - h.from).magnitude < U(10)) Toast(Loc.L("Чирж гэр рүүгээ аваач.", "Drag it onto your ger."));
            GbFlyBack(h, false); return;
        }
        string step = StepOfPart(h.part);
        var res = GerRules.Try(G.stage, step, Part);
        if (!res.ok && res.why == "order") // a gentle sag: grandpa laughs and helps, nothing is lost
        {
            G.mistakes++; gbWrong++; V.Save(); Sfx.Bad(); GbFlyBack(h, true);
            bool name = gbWrong >= (young ? 1 : 2);
            GbSaySet(LessonDone("ger") ? T2($"Өвөө: Дараа нь “{STEP[res.next].mn}”.", $"Grandpa: Next comes “{STEP[res.next].en}”.")
                : name ? T2($"Өвөө: Хэ хэ, гэр унжчихлаа! {WHY[res.next].mn}", $"Grandpa: Ha ha, the ger is sagging! {WHY[res.next].en}")
                : T2("Өвөө: Хэ хэ, гэр унжчихлаа! Юуг нь эхэлж хийдэг билээ?", "Grandpa: Ha ha, the ger is sagging! What comes first?"));
            return;
        }
        if (!res.ok) { Sfx.Bad(); GbFlyBack(h, true); GbSaySet(GbMissingTx(res.missing)); return; }
        gbWrong = 0;
        if (step == "uni") { GbPole(h, screen); return; } // placing the poles round the crown is the hands-on part here
        StartCoroutine(GbHands(step, h));
    }
    /** The right part: now do the step by hand (GerWork.StepSpec: lay the boards, turn the door south, lift the toono…). Only a
     *  finished task builds it; backing out sends the part back to the tray and nothing is lost. */
    IEnumerator GbHands(string step, GbHold h)
    {
        if (building) yield break;
        building = true; int st = G.stage; TaskWait sw;
        try { sw = Task(StepSpec(step)); yield return sw; }
        finally { building = false; }
        if (!sw.r.done || G.stage != st) { GbFlyBack(h, false); yield break; }
        if (sw.r.together) together = true;
        G.mistakes += sw.r.mistakes;
        GbCommit(step);
        GbAnimStart(new GbAnim { step = st, baseStage = st, t0 = Time.time });
    }
    Tx GbMissingTx(Dictionary<string, int> missing)
    {
        var miss = missing.Keys.ToList(); var from = FROM.TryGetValue(miss[0], out var f) ? f : FROM_GRANDPA;
        return T2("Дутуу: " + string.Join(", ", miss.Select(k => PART[k].mn)) + ". " + from.mn, "Missing: " + string.Join(", ", miss.Select(PartName)) + ". " + from.en);
    }
    /** The step is done: its parts are used up, the stage goes on and the room hears of it. */
    void GbCommit(string step)
    {
        foreach (var kv in GerRules.NEEDS[step]) PartAdd(kv.Key, -kv.Value);
        G.stage++; gbSlots.Clear(); gbSayT = -99;
        PutLot(gbLot, G.stage == 1);
        Sfx.Good(); Float(px + 0.5f, py - 0.6f, "✓", new Color(0.5f, 0.9f, 0.4f)); Confetti(10);
        if (G.stage >= GerRules.DONE) { gbOn = false; gbHold = null; FinishGer(); } // at once, so a reload can't leave a finished ger without its reward
    }

    /** Which side of the ger (0 right, 1 down, 2 left, 3 up on the screen) a direction on the grid is on. */
    static int SideOfAngle(float a) { int bin = Mathf.FloorToInt(Mathf.Repeat(a * Mathf.Rad2Deg + 180, 360) / 90); return bin == 0 ? 3 : bin == 1 ? 0 : bin == 2 ? 1 : 2; }
    /** One roof pole: it goes in the free slot nearest where it was dropped; three in a row on one side tilts the toono (UnisTilt). */
    void GbPole(GbHold h, Vector2 screen)
    {
        var o = GbLotObj; var c = cam.WorldToScreenPoint(new Vector3(o.x + o.w / 2f, -(o.y + o.h / 2f), -0.9f)); float u = ScreenPpu * 0.70710678f;
        float a = (screen.x - c.x) / u, b = -(screen.y - c.y) / (u * 0.5f); // the drop as a push on the ground: a = x - y, b = x + y
        float ang = Mathf.Atan2((b - a) / 2, (b + a) / 2);
        var free = Enumerable.Range(0, Bodies.GER_POLES).Where(j => !gbSlots.Contains(j)).OrderBy(j => Mathf.Abs(Mathf.DeltaAngle(PoleA(j) * Mathf.Rad2Deg, ang * Mathf.Rad2Deg))).ToList();
        int slot = free[0];
        var sides = GbSides(); bool Tilts(int j) => GerRules.UnisTilt(new List<int>(sides) { SideOfAngle(PoleA(j)) });
        if (Tilts(slot) && free.Any(j => !Tilts(j))) // only when another pole could still go in elsewhere
        {
            G.mistakes++; V.Save(); Sfx.Bad(); GbFlyBack(h, true);
            GbSaySet(T2("Өвөө: Тооно хазайчихлаа! Нөгөө талд нь унь хатга.", "Grandpa: The toono is tilting! Put a pole on the other side.")); return;
        }
        var before = new List<int>(gbSlots);
        gbSlots.Add(slot); V.Save(); // a pole in stays in, even if the tray closes or the game does
        GbAnimStart(new GbAnim { step = 4, slot = slot, baseStage = 4, baseSlots = before, t0 = Time.time });
        if (gbSlots.Count >= Bodies.GER_POLES) GbCommit("uni"); else { Sfx.Good(); Float(px + 0.5f, py - 0.6f, gbSlots.Count + "/" + Bodies.GER_POLES, new Color(1f, 0.85f, 0.3f)); }
    }

    // ---------------------------------------------------------------- the ger standing on the plot
    /** Draws my ger on its plot in the isometric view: what is built so far, and the part that is just dropping in. */
    void GerBodyDraw()
    {
        GerOthersDraw();
        bool show = IsoOn && !InScene && V != null && V.lot.i >= 0 && MyStage > 0;
        if (gbRoot == null) { if (!show) return; gbRoot = new GameObject("Ger on my plot"); }
        if (gbRoot.activeSelf != show) gbRoot.SetActive(show);
        if (!show) return;
        var o = GbLotObj; if (o == null) return;
        string roof = Px.ROOF.TryGetValue(V.lot.roof ?? "blue", out var rr) ? rr : Px.ROOF["blue"], door = Px.DOOR.TryGetValue(V.lot.door ?? "red", out var dd) ? dd : Px.DOOR["red"];
        float lift = 0;
        if (gbAnim != null) { float q = (Time.time - gbAnim.t0) / 0.5f; if (q >= 1) gbAnim = null; else lift = 2.8f * (1 - q * q); }
        int baseStage = gbAnim != null ? gbAnim.baseStage : MyStage;
        var slots = gbAnim != null ? gbAnim.baseSlots : baseStage == 4 ? gbSlots : null;
        bool partial = baseStage == 4 && slots != null && slots.Count > 0;
        int sig = 0; if (partial) foreach (var sl in slots) sig |= 1 << sl;
        if (o.i != gbKLot || baseStage != gbKStage || sig != gbKSig || roof != gbKRoof || door != gbKDoor)
        {
            gbKLot = o.i; gbKStage = baseStage; gbKSig = sig; gbKRoof = roof; gbKDoor = door; if (gbBaseGo != null) Destroy(gbBaseGo); gbBaseGo = null;
            int s1 = partial ? 5 : baseStage;
            if (s1 > 0) { var (art, px0, py0, dep, at) = Bodies.GerPicture(o, 0, s1, partial ? slots : null, roof, door); gbBaseGo = Billboard.Make("Ger", art, px0, py0, dep, at, gbRoot.transform); }
        }
        if (gbAnim != null)
        {
            if (gbPieceGo == null)
            {
                var (art, px0, py0, dep, at) = Bodies.GerPicture(o, gbAnim.step, gbAnim.step + 1, gbAnim.slot >= 0 ? new List<int> { gbAnim.slot } : null, roof, door);
                gbPieceGo = Billboard.Make("Ger piece", art, px0, py0, dep, at, gbRoot.transform);
            }
            gbPieceGo.transform.position = Bodies.GerAt(o) + new Vector3(0, 0, -lift);
        }
        else if (gbPieceGo != null) { Destroy(gbPieceGo); gbPieceGo = null; }
    }

    /** Other players' gers in the isometric view: the same model at their stage and colours. Also drops the "for sale" sign of every taken plot. */
    void GerOthersDraw()
    {
        bool show = IsoOn && !InScene && V != null;
        if (gbOthersRoot == null) { if (!show) return; gbOthersRoot = new GameObject("Other gers"); }
        if (gbOthersRoot.activeSelf != show) gbOthersRoot.SetActive(show);
        if (!show) return;
        for (int i = 0; i < Rules.LOTS; i++)
        {
            var l = LotOf(i); var o = World.objects.Find(q => q.kind == "lot" && q.i == i);
            bool taken = l != null || (V.lot.i == i && MyStage > 0);
            if (Bodies.signs.TryGetValue(i, out var sign) && (!gbSignOn.TryGetValue(i, out var on) || on == taken)) { gbSignOn[i] = !taken; sign.SetActive(!taken); }
            gbOthers.TryGetValue(i, out var e);
            if (l == null || o == null) { if (e != null) { Destroy(e.go); gbOthers.Remove(i); } continue; }
            int stage = l.stage > 0 ? l.stage : GerRules.DONE; string roof = Px.ROOF.TryGetValue(l.roof, out var rr) ? rr : Px.ROOF["blue"], door = Px.DOOR.TryGetValue(l.door, out var dd) ? dd : Px.DOOR["red"];
            if (e != null && e.stage == stage && e.roof == roof && e.door == door) continue;
            if (e != null) Destroy(e.go);
            var (art, px0, py0, dep, at) = Bodies.GerPicture(o, 0, stage, null, roof, door);
            gbOthers[i] = new GbOther { stage = stage, roof = roof, door = door, go = Billboard.Make("Ger " + i, art, px0, py0, dep, at, gbOthersRoot.transform) };
        }
    }

    // ---------------------------------------------------------------- where to get a missing part
    /** Where a missing part comes from in the village: its name, where it stands (tiles), the tile to walk to and what to do there. */
    (Tx name, Vector2 at, int tx, int ty, Act act)? GbGuide(string key)
    {
        WObj Obj(string id) => World.objects.Find(q => q.id == id);
        if (key == "buurees") { var o = Obj("shop"); return (T2("Дэлгүүр", "The shop"), new Vector2(o.x + o.w / 2f, o.y), o.doorX, o.doorY, new Act { type = "shop" }); }
        if (key == "esgii")
        {
            if (G.wool > 0 || G.fluff > 0 || G.mats > 0) { var o = Obj("felt"); return (T2("Эсгий хийх газар", "The felt spot"), new Vector2(o.x + 1, o.y), o.doorX, o.doorY, new Act { type = "felt" }); }
            Sheep best = null; float bd = 1e9f;
            foreach (var q in sheep) { float d = Dist(px, py, q.px, q.py) + (Shearable(q.i) ? 0 : 50); if (d < bd) { bd = d; best = q; } }
            if (best == null) return null;
            var n = World.NearestOpen(best.x, best.y, mx, my); if (n == null) return null;
            if (!Shearable(best.i)) return (T2("Хонины ноос ургаж байна, түр хүлээ", "The wool is growing back, wait a little"), new Vector2(best.px + 0.5f, best.py), n.Value.x, n.Value.y, new Act { type = "sheep", s = best });
            return (T2("Хонь (ноос хяргана)", "A sheep (shear its wool)"), new Vector2(best.px + 0.5f, best.py), n.Value.x, n.Value.y, new Act { type = Shearable(best.i) ? "shear" : "sheep", s = best });
        }
        var g = Obj("craftsman"); return (T2("Мужаан өвөө", "Grandpa Craftsman"), new Vector2(g.x + 0.5f, g.y), g.doorX, g.doorY, new Act { type = "npcq", id = "craftsman" });
    }
    void GbGoThere((Tx name, Vector2 at, int tx, int ty, Act act) t) { Sfx.Tap(); GoTo(t.tx, t.ty, t.act); }

    // ---------------------------------------------------------------- the screen: tray, hint, arrow
    void GbArrowAt(Vector2 centre, float deg, float size)
    {
        if (gbArrow == null)
        {
            gbArrow = new PxCanvas(16, 16); Color32 gold = Px.Col("#f7c646"), dark = Px.Col("#2b1e2e");
            for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++) { float half = (15 - x) / 15f * 8; float dy = Mathf.Abs(y - 7.5f); if (dy <= half) gbArrow.Put(x, y, dy > half - 1.3f || x < 1 ? dark : gold); }
        }
        var old = GUI.matrix; GUIUtility.RotateAroundPivot(deg, centre);
        GUI.DrawTexture(new Rect(centre.x - size / 2, centre.y - size / 2, size, size), Tex(gbArrow), ScaleMode.ScaleToFit); GUI.matrix = old;
    }
    /** A bobbing arrow over the place to go (or at the screen's edge, pointing to it), with its name and distance. */
    void GbMarker(Vector2 at, Tx name, float vw, float vh)
    {
        var s = cam.WorldToScreenPoint(new Vector3(at.x, -at.y, -2.3f)); var p = new Vector2(s.x, Screen.height - s.y) - new Vector2(guiOX, guiOY);
        float m = U(46), bob = Mathf.Sin(Time.time * 6) * U(5); var area = new Rect(m, m + U(40), U(vw) - 2 * m, U(vh) - 2 * m - U(150));
        string label = name + " · " + Mathf.RoundToInt(Dist(px, py, at.x, at.y)) + Loc.L(" м", " m");
        if (area.Contains(p)) { GbArrowAt(new Vector2(p.x, p.y + bob - U(10)), 90, U(30)); Outlined(p.x, p.y - U(40), label, U(10), Color.white); return; }
        var c = area.center; var d = p - c; if (d.sqrMagnitude < 1) return;
        float k = Mathf.Min(area.width / 2 / Mathf.Max(Mathf.Abs(d.x), 1e-3f), area.height / 2 / Mathf.Max(Mathf.Abs(d.y), 1e-3f)); var e = c + d * k;
        GbArrowAt(e + d.normalized * bob, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, U(30));
        var lp = e - d.normalized * U(34); Outlined(Mathf.Clamp(lp.x, U(90), U(vw) - U(90)), lp.y, label, U(10), Color.white);
    }

    /** Over the village while building: the parts tray in the bottom row (between the left and right buttons), Grandpa's hint,
     *  the way to a missing part, and the part being dragged. left/right: the free span of the bottom row, in layout units. */
    void GerBuildGui(float left, float right, float vw, float vh)
    {
        if (!IsoOn || G.stage >= GerRules.DONE) { gbOn = false; gbHold = null; return; }
        if (gbStyleFor != sLabel) // the card's text styles, rebuilt only when the screen size rebuilds the others
        {
            gbStyleFor = sLabel;
            gbChipLbl = new GUIStyle(sSmall) { fontSize = Mathf.RoundToInt(U(9)), alignment = TextAnchor.MiddleCenter, wordWrap = false, clipping = TextClipping.Clip };
            gbBody = new GUIStyle(sLabel) { fontSize = Mathf.RoundToInt(U(12)), wordWrap = true };
            gbTitle = new GUIStyle(sLabel) { fontSize = Mathf.RoundToInt(U(14)), wordWrap = false };
            gbMat = new GUIStyle(sLabel) { fontSize = Mathf.RoundToInt(U(10)), wordWrap = true };
        }
        bool rep = Event.current.type == EventType.Repaint; int stage = G.stage; string next = GerRules.STEPS[stage];
        Color gold = Px.Col("#f7c646"); var off = new Vector2(guiOX, guiOY);
        // the parts you own, then the two that need none: the floor boards and the stove
        var parts = new List<string>(); if (stage == 0) parts.Add("shal");
        foreach (var s in GerRules.STEPS) foreach (var k in GerRules.NEEDS[s].Keys) if (PartCount(k) > 0 && !parts.Contains(k)) parts.Add(k);
        parts.Add("tulga");
        float cw = 48, ch = 50, room = Mathf.Max(cw, right - left - 8); int perRow = Mathf.Max(1, (int)(room / cw)), rows = (parts.Count + perRow - 1) / perRow;
        float trayW = Mathf.Min(parts.Count, perRow) * cw + 10, trayH = rows * ch + 10, tx0 = left + (right - left - trayW) / 2, ty0 = vh - 12 - trayH;
        GUI.Box(UR(tx0, ty0, trayW, trayH), "", sPanel); Ui(UR(tx0, ty0, trayW, trayH));
        gbChips.Clear(); bool hint = young || gbWrong >= 2;
        for (int i = 0; i < parts.Count; i++)
        {
            string k = parts[i]; var r = UR(tx0 + 5 + (i % perRow) * cw + 2, ty0 + 5 + (i / perRow) * ch + 2, cw - 4, ch - 4);
            gbChips.Add((k, new Rect(r.x + off.x, r.y + off.y, r.width, r.height)));
            bool held = gbHold != null && gbHold.part == k;
            if (hint && StepOfPart(k) == next) Box(new Rect(r.x - U(2), r.y - U(2), r.width + U(4), r.height + U(4)), new Color(gold.r, gold.g, gold.b, 0.55f + 0.4f * Mathf.Sin(Time.time * 6)));
            var oc = GUI.color; if (held) GUI.color = new Color(1, 1, 1, 0.35f);
            GUI.Box(r, "", sSlot);
            GUI.DrawTexture(new Rect(r.center.x - U(13), r.y + U(3), U(26), U(26)), Tex(Px.Icon("part:" + k)), ScaleMode.ScaleToFit);
            GUI.Label(new Rect(r.x - U(2), r.y + U(29), r.width + U(4), U(14)), PartLabel(k), gbChipLbl);
            if (k != "shal" && k != "tulga") Outlined(r.xMax - U(8), r.y + U(9), "×" + PartCount(k), U(9), Color.white);
            GUI.color = oc;
        }
        // the hint card: the next step, what to drag, or what is missing and where to get it
        var res = GerRules.Try(stage, next, Part); bool saying = Time.time - gbSayT < 10 && !string.IsNullOrEmpty(gbSay.mn);
        string title = $"{stage + 1}/{GerRules.DONE}  " + STEP[next];
        var guide = !res.ok && res.why == "parts" ? GbGuide(res.missing.Keys.First()) : null;
        string body = saying ? gbSay.ToString()
            : res.ok ? Loc.L("Хэсгээ гэр рүүгээ чир: ", "Drag it onto your ger: ") + (next == "shal" ? GB_FLOOR : next == "tulga" ? GB_STOVE : T2(string.Join(", ", GerRules.NEEDS[next].Keys.Select(k => PART[k].mn)), string.Join(", ", GerRules.NEEDS[next].Keys.Select(PartName)))) + (next == "uni" ? $" ({gbSlots.Count}/{Bodies.GER_POLES})" : "")
            : GbMissingTx(res.missing).ToString();
        float cardX = left + 2, cardW = Mathf.Min(vw - cardX - 10, 520); // the card starts past the HUD's "more" column, not over it
        var bs = gbBody;
        float textH = bs.CalcHeight(new GUIContent(body), U(cardW - 28)) / UiScale, cardH = 34 + textH + 8 + 38, cardY = Mathf.Min(ty0 - 6, vh - 100) - cardH;
        var card = UR(cardX, cardY, cardW, cardH); GUI.Box(card, "", sPanel); Ui(card);
        GUI.Label(UR(cardX + 14, cardY + 8, cardW - 70, 20), "<b>" + title + "</b>", gbTitle);
        if (Button(UR(cardX + cardW - 34, cardY + 6, 28, 26), "✕", sBtn)) Later(() => { gbOn = false; gbHold = null; Sfx.Tap(); }); // the poles in so far stay (they are in the save)
        GUI.Label(UR(cardX + 14, cardY + 30, cardW - 28, textH + 4), body, bs);
        float by = cardY + 34 + textH + 4, bx = cardX + 14;
        float goW = 0, goH = 32; // the button fits its text: 190..300 wide, taller only if the text wraps
        if (guide != null) { var gd = guide.Value; string gt = Loc.L("Тийшээ очих · ", "Go there · ") + gd.name; goW = Mathf.Clamp(sPrimary.CalcSize(new GUIContent(gt)).x / UiScale + 16, 190, 300); goH = Mathf.Max(32, sPrimary.CalcHeight(new GUIContent(gt), U(goW)) / UiScale); if (Button(UR(bx, by, goW, goH), gt, sPrimary)) Later(() => GbGoThere(gd)); }
        else { var lo = GbLotObj; if (lo != null && Dist(px, py, lo.doorX, lo.doorY) > 12 && Button(UR(bx, by, 150, 32), Loc.L("Гэр рүүгээ", "Back to my ger"), sPrimary)) Later(() => GoTo(lo.doorX, lo.doorY, null)); }
        float lx = Mathf.Max(cardX + cardW - 198, bx + (guide != null ? goW : 150) + 8); // 184 wide at the card's right edge, or right of the button beside it
        if (Button(UR(lx, by, 184, 32), gbList ? Loc.L("Хаах", "Hide list") : Loc.L("Материал хаанаас?", "Where to get things?"), sBtn)) Later(() => gbList = !gbList);
        if (guide != null) GbMarker(guide.Value.at, guide.Value.name, vw, vh);
        if (gbList) GbMaterials(cardX, cardY, cardW);
        // the dragged part, the ger lighting up under it, and parts flying back to the tray
        if (gbHold != null)
        {
            var hp = gbHold.gui - off; bool on = OnMyGer(new Vector2(gbHold.gui.x, Screen.height - gbHold.gui.y)); var lo = GbLotObj;
            if (on && lo != null)
            {
                var c = WorldGui(lo.x + lo.w / 2f, lo.y + lo.h / 2f, 1.1f) - off; var oc = GUI.color; float u = ScreenPpu;
                GUI.color = new Color(1f, 0.85f, 0.3f, 0.5f + 0.3f * Mathf.Sin(Time.time * 8)); GUI.DrawTexture(new Rect(c.x - 1.9f * u, c.y - 2.1f * u, 3.8f * u, 4.2f * u), Tex(Ring()), ScaleMode.StretchToFill); GUI.color = oc;
            }
            GUI.DrawTexture(new Rect(hp.x - U(24), hp.y - U(42), U(48), U(48)), Tex(Px.Icon("part:" + gbHold.part)), ScaleMode.ScaleToFit);
        }
        for (int i = gbFlies.Count - 1; i >= 0; i--)
        {
            var f = gbFlies[i]; float q = (Time.time - f.t0) / 0.35f; if (q >= 1) { if (rep) gbFlies.RemoveAt(i); continue; }
            var fp = Vector2.Lerp(f.from, f.to, 1 - (1 - q) * (1 - q)) - off; fp.y -= Mathf.Sin(q * Mathf.PI) * U(26); var oc = GUI.color; if (f.bad) GUI.color = new Color(1, 0.7f, 0.7f, 1);
            GUI.DrawTexture(new Rect(fp.x - U(24), fp.y - U(24), U(48), U(48)), Tex(Px.Icon("part:" + f.part)), ScaleMode.ScaleToFit); GUI.color = oc;
        }
    }

    /** "Where do I get things?": every material, where it comes from, and a button that goes there. */
    void GbMaterials(float x, float cardY, float w)
    {
        var rows = new List<(string icon, Tx what, string key, Tx how)>
        {
            ("part:khana", T2("Хана, хаалга, тооно, унь, бүслүүр", "Walls, door, toono, roof poles, ropes"), "khana", T2("Мужаан өвөөгөөс ноос, ааруул, өрөм эсвэл зоосоор.", "From Grandpa Craftsman for wool, aaruul, öröm or coins.")),
            ("part:esgii", T2("Эсгий", "Felt"), "esgii", T2("Хонио хяргаж, ноосоо цохиж, дэлгэж, өнхрүүлнэ.", "Shear a sheep, then beat, lay and roll the wool.")),
            ("part:buurees", T2("Цагаан бүрээс", "White cover"), "buurees", T2($"Дэлгүүрээс {GerRules.COVER_PRICE} зоосоор авна.", $"The shop sells it for {GerRules.COVER_PRICE} coins.")),
        };
        float rh = 46, h = rows.Count * rh + 12, y0 = cardY - 6 - h; var panel = UR(x, y0, w, h); GUI.Box(panel, "", sPanel); Ui(panel);
        for (int i = 0; i < rows.Count; i++)
        {
            var (icon, what, key, how) = rows[i]; float y = y0 + 8 + i * rh;
            GUI.DrawTexture(UR(x + 12, y + 4, 28, 28), Tex(Px.Icon(icon)), ScaleMode.ScaleToFit);
            GUI.Label(UR(x + 46, y, w - 190, rh), "<b>" + what + "</b>\n" + how, gbMat);
            var g = GbGuide(key);
            if (g != null && Button(UR(x + w - 134, y + 4, 122, 30), Loc.L("Тийшээ очих", "Go there"), sPrimary)) { var gd = g.Value; Later(() => GbGoThere(gd)); }
        }
    }
}
