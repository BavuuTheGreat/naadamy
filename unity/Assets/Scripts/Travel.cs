// Аймагт аялах: a full-screen walkable map for every province (from js/travel.js). The map itself, with the three famous
// landmarks drawn as they really look, is rendered by the web version's code (Resources/Art/trip_*); the layout is in
// Resources/Data/travel.json. Visit all three sights, then the local guide asks the stamp question.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed partial class Game
{
    // ================================================================ the people of Нутаг (drawn by Tools/art/folk.py and hero.py): Bolormaa and the villagers
    static readonly Dictionary<string, PxCanvas> folkArt = new Dictionary<string, PxCanvas>();
    /** Someone from head to toe (200×344): id bolormaa, emee, zasuul, general, historian, herder, fisher, farmer, teacher, trainer, archer,
     *  craftsman. face: neutral, happy, laugh, wink, surprised, think, sad, proud, tease, excited, worried, shy, curious, stern.
     *  pose: idle (their own stance, with what they work with), talk, cheer, think, wave, point, proud, or null for the one that
     *  suits the face. frame 0/1 breathes; open: the mouth while talking; blink: the eyes shut. */
    public static PxCanvas Folk(string id, string face, string pose, int frame, bool open, bool blink)
    {
        var m = Data.Get("folk"); var faces = m["faces"].Strings(); var poses = m["poses"].Strings();
        if (!m["folk"].Has(id)) id = "bolormaa";
        if (!faces.Contains(face)) face = "happy";
        if (pose == null || !poses.Contains(pose)) pose = m["poseFor"][face].S ?? "idle";
        frame &= 1; string key = id + "|" + face + "|" + pose + frame + open + blink;
        if (folkArt.TryGetValue(key, out var c)) return c;
        int W = m["W"].I, H = m["H"].I, fw = m["faceW"].I, fh = m["faceH"].I, row = poses.IndexOf(pose), fi = faces.IndexOf(face) * 4 + (open ? 1 : 0) + (blink ? 2 : 0);
        c = new PxCanvas(W, H);
        c.BlitScaled(Art.Get("folk_" + id + "_body"), 0, 0, W, H, false, frame * W, row * H, W, H);
        var who = m["folk"][id]; int fx = who.Has("faceX") ? who["faceX"].I : m["faceX"].I, fy = who.Has("faceY") ? who["faceY"].I : m["faceY"].I;   // each character's face sits on their own head
        c.BlitScaled(Art.Get("folk_" + id + "_faces"), fx, fy + who["bob"][pose][frame].I, fw, fh, false, fi % 4 * fw, fi / 4 * fh, fw, fh);
        return folkArt[key] = c;
    }
    public static PxCanvas Bolormaa(string face, string pose, int frame, bool open, bool blink) => Folk("bolormaa", face, pose, frame, open, blink);
    static readonly Dictionary<string, PxCanvas> busts = new Dictionary<string, PxCanvas>();
    /** Someone's dialogue portrait (Tools/art/bust.py): a 112×144 pixel-art head and shoulders, with the same 14 faces as Folk,
     *  talking (open) and blinking. Null for someone without one. */
    public static PxCanvas Portrait(string id, string face, bool open, bool blink)
    {
        var m = Data.Get("bust"); if (!m["people"].Strings().Contains(id)) return null;
        var faces = m["faces"].Strings(); if (!faces.Contains(face)) face = "happy";
        string key = id + face + open + blink; if (busts.TryGetValue(key, out var c)) return c;
        int W = m["W"].I, H = m["H"].I, i = faces.IndexOf(face) * 4 + (open ? 1 : 0) + (blink ? 2 : 0);
        c = new PxCanvas(W, H); c.BlitScaled(Art.Get("bust_" + id), 0, 0, W, H, false, i % 4 * W, i / 4 * H, W, H);
        return busts[key] = c;
    }
    /** Bolormaa standing in a scene picture at (X, Y), its top left, 60 pixels tall at scale 1. pose: wait | bye | yay. */
    public void Nomin(PxCanvas x, float X, float Y, string pose, float scale = 1)
    {
        string p = pose == "bye" ? "wave" : pose == "yay" ? "cheer" : "idle", f = pose == "yay" ? "laugh" : pose == "bye" ? "wink" : "happy";
        int fr = (int)(Time.time * (pose == "idle" || pose == "wait" ? 1.6f : 4)) % 2;
        float h = 60 * scale, w = h * 200 / 344f;
        x.BlitScaled(Bolormaa(f, p, fr, false, Time.time % 3.7f < 0.12f), X - (w - 27 * scale) / 2, Y, w, h);
    }

    public void OpenTrip(string id, string view)
    {
        if (PassLocked(id)) { PassGuard(id, () => OpenTrip(id, view)); return; }
        V.travelView = view; V.Save();
        if (view == "fp" && TryFirstPerson(id)) return;
        Open(new TripScene(this, id));
    }

    sealed class TripScene
    {
        static J D => Data.Get("travel");
        static readonly Dictionary<string, float> SIZE = new Dictionary<string, float> { ["sheep"] = 1.0f, ["goat"] = 0.95f, ["horse"] = 1.6f, ["camel"] = 2.1f, ["yak"] = 1.5f, ["reindeer"] = 1.45f, ["gazelle"] = 1.15f };
        sealed class Herd { public string sp; public float x, y, hx, hy, tx, ty, wait; public bool flip, moving; }

        public TripScene(Game g, string id, bool firstPerson = false)
        {
            var a = Aimags.ById(id); var j = Aimags.J(id); var map = D["trips"][id]; var det = map["det"]; int TS = D["TS"].I, MW = map["g"][0].S.Length, MH = map["g"].Count;
            var grid = map["g"].Strings(); var V = g.V;
            var terrain = Art.Get("trip_" + id + (Seasons.Current().id == "autumn" ? "_autumn" : ""));
            bool Stamped() => g.Stamped(id);
            List<int> Seen() => V.seenMarks.Where(s => s.StartsWith(id + ":")).Select(s => int.Parse(s.Substring(id.Length + 1))).ToList();
            bool FamDone() => V.travelFam.Contains(id);
            var eth = Aimags.ETHNIC.FirstOrDefault(e => e.aimags[0] == id) ?? Aimags.ETHNIC.FirstOrDefault(e => e.aimags.Contains(id) && e.id != "khalkh") ?? Aimags.EthnicById("khalkh");
            var guideLook = new Look { deel = eth?.deel ?? "blue", skin = "#D9A06E", hat = eth?.hat ?? "mongol", neck = "khadag" };
            var herderLook = guideLook.Copy(); herderLook.deel = guideLook.deel == "brown" ? "blue" : "brown"; herderLook.neck = null;
            int i0 = 0; var herds = map["herd"].Select(h => { float hx = h["x"].F, hy = h["y"].F; int i = i0++; return new Herd { sp = h["kind"].S, x = hx, y = hy, hx = hx, hy = hy, tx = hx, ty = hy, wait = 1 + (i % 4), flip = i % 2 == 0 }; }).ToList();
            Vector2 At(J p) => new Vector2(p["x"].F * TS + 8, p["y"].F * TS + 12);
            Vector2 AtXY(float tx, float ty) => new Vector2(tx * TS + 8, ty * TS + 12);
            bool Blocked(float px, float py) { int X = Mathf.FloorToInt(px / TS), Y = Mathf.FloorToInt(py / TS); if (Y < 0 || Y >= MH || X < 0 || X >= MW) return true; return "wtmrbLGzO".IndexOf(grid[Y][X]) >= 0; }
            bool Free(float px, float py) => !Blocked(px - 4, py - 2) && !Blocked(px + 4, py - 2) && !Blocked(px - 4, py + 2) && !Blocked(px + 4, py + 2);
            var CAMP = D["CAMP"]; var FAM = D["FAM"]; var OVOO = D["OVOO"]; var SIGN = D["SIGN"];
            string Area() => det["area"].IsNull ? "?" : det["area"].I.ToString("#,0", System.Globalization.CultureInfo.InvariantCulture);
            WalkScene sc = null;
            var things = new List<Thing>();
            var marks = map["marks"].ToList();
            foreach (var m in marks) { var mm = m; var mark = j.marks[m["i"].I]; var p = At(m); things.Add(new Thing { id = "mark", x = p.x, y = p.y, label = () => mark.name.ToString(), run = () => Run("mark", mm) }); }
            var gp = At(map["guide"]); var guideT = new Thing { id = "guide", x = gp.x, y = gp.y, label = () => Loc.L("Хөтөчтэй ярилцах", "Talk to the guide"), run = () => Run("guide", null) }; things.Add(guideT);
            var cp = AtXY(CAMP[0]["x"].F + 1, CAMP[0]["y"].F + 3); things.Add(new Thing { id = "camp", x = cp.x, y = cp.y, label = () => Loc.L("Жуулчны бааз", "Tourist ger camp"), run = () => Run("camp", null) });
            var bp = At(map["start"]); var busT = new Thing { id = "bus", x = bp.x, y = bp.y, label = () => Loc.L("Нутаг руу буцах", "Return to Nutag"), run = () => g.Close() }; things.Add(busT);
            var fp = AtXY(FAM["fx"].F, FAM["fy"].F); var famT = new Thing { id = "family", x = fp.x, y = fp.y, label = () => Loc.L("Малчин айлд орох", "Visit the herder family"), run = () => Run("family", null) }; things.Add(famT);
            var op = AtXY(OVOO["fx"].F, OVOO["fy"].F); things.Add(new Thing { id = "ovoo", x = op.x, y = op.y, label = () => Loc.L("Овоо тахих", "Honour the ovoo"), run = () => Run("ovoo", null) });
            var sp = AtXY(SIGN["x"].F, SIGN["y"].F); things.Add(new Thing { id = "sign", x = sp.x, y = sp.y, label = () => Loc.L("Самбар унших", "Read the sign"), run = () => Run("sign", null) });
            foreach (var hd in herds) { var h2 = hd; things.Add(new Thing { id = "animal", penalty = 8, x = hd.x, y = hd.y, label = () => D["ANAME"][h2.sp].T.ToString(), run = () => Run("animal", null, h2) }); }
            // the historian's task: an item to find, sparkling beside one of the sights
            var hq = g.HistoryTarget(id); Thing art = null;
            if (hq != null)
            {
                var mk = marks.FirstOrDefault(q => q["i"].I == hq.near) ?? marks[0]; int mx0 = mk["x"].I, my0 = mk["y"].I, ax = mx0 + 2, ay = my0;
                foreach (var (dx, dy) in new[] { (2, 0), (-2, 0), (2, 1), (-2, 1), (1, 1), (-1, 1) }) { int yy = my0 + dy, xx = mx0 + dx; if (yy >= 0 && yy < MH && xx >= 0 && xx < MW && (grid[yy][xx] == 'g' || grid[yy][xx] == 'p')) { ax = xx; ay = yy; break; } }
                var apos = AtXY(ax, ay);
                art = new Thing { id = "artifact", x = apos.x, y = apos.y, label = () => hq.item.ToString(), run = null };
                art.run = () => { if (!g.HistoryFound(id)) return; things.Remove(art); sc.Say(Loc.L("Олдлоо! ", "Found it! ") + hq.item + ". " + Loc.L("Нутагтаа буцаад Түүхчид өгөөрэй.", "Take it back to the historian in Nutag.")); };
                things.Add(art);
            }
            void Run(string kind, J m, Herd hd = null)
            {
                if (kind == "mark")
                {
                    int i = m["i"].I; var mark = j.marks[i];
                    if (!Seen().Contains(i)) { V.seenMarks.Add(id + ":" + i); V.Save(); Sfx.Chime(); g.Reward(5, 10); }
                    sc.Say("<b>" + mark.name + "</b>\n" + mark.desc + (Seen().Count == 3 && !Stamped() ? "\n" + Loc.L("Гурван газраар аялчихлаа! Одоо хөтөч дээр очоорой.", "You visited all three places! Now go to the guide.") : ""));
                    Prog();
                }
                else if (kind == "family")
                {
                    var f = Rules.FoodById(det["food"].S);
                    sc.Say("<b>" + Loc.L("Малчин айл", "A herder family") + "</b>\n" + Loc.L("Сайн байна уу, хүүхэд минь! Манай гэрт тавтай морил. ", "Hello, child! Welcome to our ger. ") + det["fact"].T + "\n" + Loc.L("Айлд орохдоо “Нохой хорио!” гэж дуудаад, зочин баруун талаар суудаг.", "Before entering, call out “Hold the dog!”, and guests sit on the west side."));
                    if (f != null && !FamDone()) g.Open(new FamilyFoodModal(f, () => { g.Eat(f.id, true); if (!FamDone()) { V.travelFam.Add(id); V.Save(); g.Reward(5, 10); } }));
                }
                else if (kind == "ovoo") { Sfx.Chime(); sc.Say("<b>" + Loc.L("Овоо", "The ovoo") + "</b>\n" + Loc.L("Овоог нар зөв гурав тойрч, чулуу нэмж, хадаг уядаг. Аянд гарахдаа сайн сайхан ерөөдөг заншилтай.", "Walk around the ovoo three times sunwise, add a stone and tie a khadag, wishing for a safe journey.")); }
                else if (kind == "sign") sc.Say("<b>" + a.name + "</b>\n" + Loc.L($"Төв: {a.center.mn}. Нутаг дэвсгэр: {Area()} км².", $"Centre: {a.center.en}. Area: {Area()} km².") + " " + j.history);
                else if (kind == "animal") { Sfx.Pop(); sc.Say("<b>" + D["ANAME"][hd.sp].T + "</b>\n" + D["ANIMAL"][hd.sp].T + "\n" + Loc.L("Монголчууд адуу, тэмээ, үхэр, хонь, ямаа гэсэн таван хошуу мал малладаг.", "Mongolian herders keep the “five snouts”: horses, camels, cattle (and yaks), sheep and goats.")); }
                else if (kind == "camp") sc.Say("<b>" + Loc.L("Жуулчны гэр бааз", "Tourist ger camp") + "</b>\n" + Loc.L("Монголд ирсэн жуулчид гэр баазад буудаллаж, малчин айлын амьдралтай танилцдаг. Гэрийн хаалга урагшаа хардаг!", "Visitors to Mongolia stay in ger camps and get to know herding life. Every ger door faces south!"));
                else if (kind == "guide")
                {
                    string greet = Loc.L($"Сайн байна уу! {a.name.mn} нутагт тавтай морил. Би {eth?.name.mn} хүн байна.", $"Hello! Welcome to {a.name.en}. I am {eth?.name.en}.");
                    if (Stamped()) { sc.Say("<b>" + Loc.L("Нутгийн хөтөч", "Local guide") + "</b>\n" + greet + " " + j.history + "\n" + Loc.L("Тамга авсан", "Already stamped")); return; }
                    if (Seen().Count < 3) { sc.Say("<b>" + Loc.L("Нутгийн хөтөч", "Local guide") + "</b>\n" + greet + " " + Loc.L("Эхлээд манай нутгийн 3 алдартай газраар аялаад ир. Дараа нь тамга өгье!", "First visit our 3 famous places, then I’ll give you the stamp!")); return; }
                    g.Open(new StampQuizModal(id, () =>
                    {
                        Prog(); string nx = g.NextAimag();
                        sc.Say("<b>" + Loc.L("Тамга авлаа!", "Stamped!") + "</b>" + (nx != null ? "\n" + Loc.L("Дараагийн аймаг: ", "Next province: ") + Aimags.ById(nx).name : "\n" + Loc.L("Бүх аймгаар аялсан!", "You visited every province!")));
                        if (nx != null) sc.buttons.Insert(0, (() => Loc.L("Дараагийн аймаг ►", "Next province ►"), () => { g.Close(); g.OpenTrip(nx, g.V.travelView); }));
                    }));
                }
            }
            string prog = "";
            void Prog() => prog = $"{Seen().Count}/3 · " + (Stamped() ? Loc.L("тамга √", "stamp √") : Loc.L("тамга …", "stamp …"));
            Prog();
            sc = new WalkScene
            {
                W = MW * TS, H = MH * TS, bg = "#2b1e2e", look = V.look, speed = 72, reach = 24, things = things, free = Free,
                title = a.name.ToString(), sub = a.center + "   " + prog,
                info = "<b>" + Loc.L($"{a.name.mn}-д ирлээ!", $"You arrived in {a.name.en}!") + "</b>\n" + Loc.L($"Төв: {a.center.mn} · {Area()} км². ", $"Centre: {a.center.en} · {Area()} km². ") + Loc.L("Шар тугтай 3 үзвэрээр аялж, дараа нь нутгийн хөтөчтэй уулзаарай.", "Visit the 3 sights with yellow flags, then meet the local guide."),
            };
            sc.Start(bp.x, bp.y);
            sc.buttons.Add((() => firstPerson ? Loc.L("Дээрээс харах", "Top view") : Loc.L("3D", "3D"), () => { g.Close(); g.OpenTrip(id, firstPerson ? "2d" : "fp"); }));
            sc.drawBg = (b, t) => b.CopyFrom(terrain);
            sc.step = dt =>
            {
                sc.sub = a.center + "   " + prog;
                foreach (var hd in herds) // animals graze: stand a while, then amble to a nearby open spot
                {
                    float dx = hd.tx - hd.x, dy = hd.ty - hd.y, d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d > 0.8f) { float s = 9 * dt; hd.x += dx / d * Mathf.Min(s, d); hd.y += dy / d * Mathf.Min(s, d); hd.moving = true; if (Mathf.Abs(dx) > 0.3f) hd.flip = dx < 0; }
                    else { hd.moving = false; hd.wait -= dt; if (hd.wait <= 0) { hd.wait = 2 + UnityEngine.Random.value * 5; float nx = hd.hx + (UnityEngine.Random.value - 0.5f) * 70, ny = hd.hy + (UnityEngine.Random.value - 0.5f) * 40; if (Free(nx, ny)) { hd.tx = nx; hd.ty = ny; } } }
                }
                foreach (var th in things) if (th.id == "animal") { var hd = herds[things.Where(q => q.id == "animal").ToList().IndexOf(th)]; th.x = hd.x; th.y = hd.y; }
            };
            sc.ents = t =>
            {
                var list = new List<Ent>();
                if (art != null && things.Contains(art)) list.Add(new Ent { y = art.y, f = b => { int tw = (int)(t * 6) % 4, X = Js.Round(art.x), Y = Js.Round(art.y); Px.R(b, X - 1, Y - 6, 3, 3, "#fff6c8"); if (tw < 2) { Px.R(b, X - 4, Y - 5, 9, 1, "#f7c646"); Px.R(b, X, Y - 9, 1, 9, "#f7c646"); } } });
                foreach (var hd in herds) { var h2 = hd; list.Add(new Ent { y = hd.y, f = b => { var img = Art.Get($"critter_{h2.sp}_{(h2.moving ? (int)(t * 6) % 2 : 0)}"); int X = Js.Round(h2.x - 10), Y = Js.Round(h2.y - 14); Px.R(b, X + 4, Y + 14, 12, 2, "rgba(30,20,10,.22)"); b.BlitScaled(img, X, Y, img.W, img.H, h2.flip); } }); }
                list.Add(new Ent { y = famT.y - 2, f = b => { b.Blit(Px.Char(herderLook, (int)(t / 3) % 4 == 3 ? 2 : 0, 0), Js.Round(famT.x - 8), Js.Round(famT.y - 28)); if (!FamDone()) { int bob = Mathf.RoundToInt(Mathf.Sin(t * 5) * 1.5f); Px.R(b, Js.Round(famT.x - 1), Js.Round(famT.y - 38) + bob, 2, 5, "#f7c646"); Px.R(b, Js.Round(famT.x - 1), Js.Round(famT.y - 32) + bob, 2, 2, "#f7c646"); } } });
                var seen = Seen();
                foreach (var th in things.Where(q => q.id == "mark").ToList()) { int mi = marks[things.Where(q => q.id == "mark").ToList().IndexOf(th)]["i"].I; list.Add(new Ent { y = th.y, f = b => { bool done = seen.Contains(mi); int X = Js.Round(th.x), Y = Js.Round(th.y); Px.R(b, X - 1, Y - 18, 2, 18, "#6b4220"); Px.R(b, X + 1, Y - 18, 8, 6, "#2b1e2e"); Px.R(b, X + 1, Y - 17, 7, 4, done ? "#5c9a36" : "#f7c646"); if (!done && (int)(t * 3) % 2 != 0) Px.R(b, X + 4, Y - 24, 2, 3, "#ffffff"); } }); }
                list.Add(new Ent { y = guideT.y, f = b => { b.Blit(Px.Char(guideLook, 0, 0), Js.Round(guideT.x - 8), Js.Round(guideT.y - 26)); if (!Stamped()) { string c = seen.Count == 3 ? "#7fe07a" : "#f7c646"; int bob = Mathf.RoundToInt(Mathf.Sin(t * 5) * 1.5f); Px.R(b, Js.Round(guideT.x - 1), Js.Round(guideT.y - 36) + bob, 2, 5, c); Px.R(b, Js.Round(guideT.x - 1), Js.Round(guideT.y - 30) + bob, 2, 2, c); } } });
                return list;
            };
            string bird = det["birds"].S;
            sc.drawTop = (b, t) =>
            {
                int WW = MW * TS, HH = MH * TS;
                for (int i = 0; i < 3; i++) { float cx = ((t * 6 + i * 170) % (WW + 120)) - 60, cy = 40 + i * 90 + Mathf.Sin(t * 0.2f + i) * 10; Px.Ell(b, Js.Round(cx), Js.Round(cy), 46, 16, "rgba(20,30,60,.07)"); } // cloud shadows drifting over the land
                if (bird != null) for (int i = 0; i < (bird == "eagle" ? 1 : 5); i++)
                    {
                        int flap = (int)(t * (bird == "eagle" ? 3 : 6) + i) % 2; float bx, by;
                        if (bird == "eagle") { bx = WW * 0.6f + Mathf.Cos(t * 0.4f) * 70; by = 70 + Mathf.Sin(t * 0.4f) * 30; } else { bx = ((t * 14) % (WW + 160)) - 80 - i * 9; by = 50 + Mathf.Abs(i - 2) * 6; }
                        string col = bird == "swan" ? "#ffffff" : bird == "crane" ? "#c9ccd6" : "#5a3a22"; int X = Js.Round(bx), Y = Js.Round(by), w = bird == "eagle" ? 6 : 4;
                        Px.R(b, X - 1, Y, 3, 2, col); Px.R(b, X - w, Y - (flap != 0 ? 2 : -1), w - 1, 1, col); Px.R(b, X + 2, Y - (flap != 0 ? 2 : -1), w - 1, 1, col);
                        if (bird == "crane") Px.R(b, X + 2, Y, 2, 1, "#2b1e2e");
                    }
                if (map["snow"].B || Seasons.Current().id == "winter") for (int i = 0; i < 40; i++) Px.R(b, Js.Round((i * 53 + Mathf.Sin(t + i) * 6) % (MW * TS)), Js.Round((i * 31 + t * 16 * (1 + (i % 3) * 0.3f)) % (MH * TS)), 1, 1, "rgba(255,255,255,.8)");
            };
            sc.mini = new MiniMap { img = terrain, dots = () => { var seen = Seen(); var d = things.Where(q => q.id == "mark").Select((q, k) => (q.x, q.y, seen.Contains(marks[k]["i"].I) ? "#5c9a36" : "#f7c646")).ToList(); d.Add((guideT.x, guideT.y, Stamped() ? "#ffffff" : seen.Count == 3 ? "#7fe07a" : "#f492ba")); d.Add((busT.x, busT.y, "#3f76db")); d.Add((famT.x, famT.y, FamDone() ? "#ffffff" : "#f7c646")); return d; } };
            // the same trip in first person: the province's voxel world, the herds, the guide and the herder family
            sc.fp = new FpSource
            {
                world = id, scale = Voxels.TILE / (float)TS, start = new Vector2(bp.x, bp.y), title = a.name.ToString(), sub = a.center.ToString(),
                info = () => sc.info, things = things, step = dt => { sc.step?.Invoke(dt); sc.fp.sub = sc.sub; }, buttons = sc.buttons,
                hint = () => Loc.L("Шар тугтай газраар аялаарай", "Visit the places with yellow flags"),
                actors = t =>
                {
                    var o = herds.Select(hd => new FpActor { img = Art.Get($"critter_{hd.sp}_{(hd.moving ? (int)(t * 6) % 2 : 0)}"), x = hd.x, y = hd.y, h = SIZE.TryGetValue(hd.sp, out var z) ? z : 1.2f, flip = hd.flip }).ToList();
                    o.Add(new FpActor { img = Px.Char(guideLook, 0, 0), x = guideT.x, y = guideT.y, h = 1.8f });
                    o.Add(new FpActor { img = Px.Char(herderLook, 0, 0), x = famT.x, y = famT.y - 6, h = 1.8f });
                    return o;
                },
                markers = () =>
                {
                    var seen = Seen(); var ms = things.Where(q => q.id == "mark").ToList(); var o = new List<FpMarker>();
                    for (int i = 0; i < ms.Count; i++) o.Add(new FpMarker { x = ms[i].x, y = ms[i].y, c = seen.Contains(marks[i]["i"].I) ? "#5c9a36" : "#f7c646", label = ms[i].label(), lift = 2.6f });
                    if (!Stamped()) o.Add(new FpMarker { x = guideT.x, y = guideT.y, c = seen.Count == 3 ? "#7fe07a" : "#f7c646", label = Loc.L("Хөтөч", "Guide") });
                    if (!FamDone()) o.Add(new FpMarker { x = famT.x, y = famT.y - 6, c = "#f7c646", label = Loc.L("Малчин айл", "Herder family") });
                    if (art != null && things.Contains(art)) o.Add(new FpMarker { x = art.x, y = art.y, c = "#7fe07a", label = hq.item.ToString(), lift = 1.4f });
                    return o;
                },
            };
            Sfx.Pop();
            Scene = sc;
        }
        public readonly WalkScene Scene;
    }
    void Open(TripScene t) => Open(t.Scene);

    sealed class FamilyFoodModal : Modal
    {
        readonly Rules.Food f; readonly Action eat; bool done;
        public FamilyFoodModal(Rules.Food f, Action eat) { this.f = f; this.eat = eat; }
        public override void Draw(Game g)
        {
            g.Title(Loc.L("Малчин айл", "A herder family"));
            g.Text(Loc.L($"Манайхаас {f.name.mn} амсаарай!", $"Have some {f.name.en}!"));
            g.Fact(f.fact.ToString());
            if (!done && g.Btn(Loc.L($"{f.name.mn} амсах (үнэгүй)", $"Taste the {f.name.en} (free)"), true, 46)) g.Later(() => { done = true; eat(); });
            if (g.Btn(Loc.L("Баярлалаа!", "Thank you!"), false, 36)) g.Later(g.Close);
        }
        public override void Press(Game g) => g.Close();
    }
    /** The local guide's question for the province's stamp. */
    sealed class StampQuizModal : Modal
    {
        readonly string id; readonly Action done; readonly List<(Tx o, bool ok)> opts; readonly HashSet<int> wrong = new HashSet<int>(); bool answered;
        public StampQuizModal(string id, Action done) { this.id = id; this.done = done; var j = Aimags.J(id); opts = j.o.Select((o, i) => (o, i == j.a)).OrderBy(_ => UnityEngine.Random.value).ToList(); }
        public override void Draw(Game g)
        {
            var j = Aimags.J(id);
            g.Title(Loc.L("Нутгийн хөтөч", "Local guide")); g.Small(j.history.ToString());
            g.Text("<b>" + j.q + "</b>");
            for (int i = 0; i < opts.Count; i++)
            {
                bool was = GUI.enabled; GUI.enabled = !wrong.Contains(i);
                if (g.Btn(opts[i].o.ToString(), false, 40)) { int k = i; g.Later(() => { if (answered) return; /* a double click: the second Close would shut the trip map too */ if (!opts[k].ok) { wrong.Add(k); Sfx.Bad(); return; } answered = true; g.Stamp(id); g.Confetti(60); g.Close(); done(); }); }
                GUI.enabled = was;
            }
        }
    }
}
