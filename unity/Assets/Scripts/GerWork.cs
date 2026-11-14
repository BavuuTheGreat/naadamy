// Building your own ger by hand, on your plot in Нутаг (js/village/gerwork.js): you choose what comes next, do every step
// yourself, and Grandpa Craftsman laughs kindly and gives a hint when the order is wrong. Nothing is ever lost.
// The felt comes from your own sheep (shear, beat, lay and wet, roll), the wooden parts from Grandpa Craftsman (for wool and
// dairy foods, or coins), and the white cover from the shop. Also here: what is learned by hand, shown to parents.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/** The rules for building a ger by hand, from js/village/rules.js (Resources/Data/vrules.json). */
public static class GerRules
{
    static J D => Data.Get("vrules");
    static List<string> steps; static Dictionary<string, Dictionary<string, int>> needs;
    public static List<string> STEPS => steps ??= D["GER_STEPS"].Strings();
    public static int DONE => STEPS.Count;
    public static Dictionary<string, Dictionary<string, int>> NEEDS => needs ??= STEPS.ToDictionary(s => s, s => { var d = new Dictionary<string, int>(); var n = D["GER_NEEDS"][s]; foreach (var k in n.Keys) d[k] = n[k].I; return d; });
    public sealed class Trade { public string id; public Dictionary<string, int> gives, goods; public int coins; }
    static List<Trade> trades;
    static Dictionary<string, int> Dict(J j) { var d = new Dictionary<string, int>(); foreach (var k in j.Keys) d[k] = j[k].I; return d; }
    public static List<Trade> TRADES => trades ??= D["TRADES"].Select(t => new Trade { id = t["id"].S, gives = Dict(t["gives"]), goods = Dict(t["goods"]), coins = t["coins"].I }).ToList();
    public static int COVER_PRICE => D["COVER_PRICE"].I;
    public static int SHEAR_WOOL => D["SHEAR_WOOL"].I;
    public static long SHEAR_REGROW_MS => D["SHEAR_REGROW_MS"].I;
    public static int FELT_WOOL => D["FELT_WOOL"].I;
    /** Can this step be done now? stage = how many steps are already done. */
    public static (bool ok, string why, string next, Dictionary<string, int> missing) Try(int stage, string step, Func<string, int> parts)
    {
        if (stage >= DONE) return (false, "done", null, null);
        if (STEPS[stage] != step) return (false, "order", STEPS[stage], null);
        var missing = new Dictionary<string, int>(); foreach (var kv in NEEDS[step]) if (parts(kv.Key) < kv.Value) missing[kv.Key] = kv.Value - parts(kv.Key);
        return missing.Count > 0 ? (false, "parts", null, missing) : (true, null, null, null);
    }
    /** Roof poles go in all around the crown to keep it level: three in a row on one side tilts it. */
    public static bool UnisTilt(List<int> sides) { int n = sides.Count; return n >= 3 && sides[n - 1] == sides[n - 2] && sides[n - 2] == sides[n - 3]; }
    // ---- learned by hand in Нутаг, shown to parents next to the school lessons
    public sealed class HandsOn { public string id, icon, lesson; public bool home; public Tx name; public List<Tx> learned; }
    static List<HandsOn> handsOn;
    public static List<HandsOn> HANDS_ON => handsOn ??= D["HANDS_ON"].Select(a => new HandsOn { id = a["id"].S, icon = a["icon"].S, lesson = a["lesson"].S, home = a["home"].B, name = a["name"].T, learned = a["learned"].Select(t => t.T).ToList() }).ToList();
    /** The hands-on ids a child has done, read from their save (so progress from before this list counts too). */
    public static List<string> Done(SaveData v) => HANDS_ON.Where(a => a.id == "ger" ? v.gerBuilt || v.ger.stage >= DONE : a.id == "emee" ? v.emee.visits > 0 : v.seen.Contains("made_" + a.id)).Select(a => a.id).ToList();
    /** A wrestling move told simply, as you'd explain it to a five-year-old. */
    public static Tx Kid(string move) => D["KID"][move].T;
}

public sealed partial class Game
{
    static Tx T2(string mn, string en) => new Tx(mn, en);
    static readonly Dictionary<string, Tx> STEP = new Dictionary<string, Tx>
    {
        ["shal"] = T2("Шалаа тавих", "Lay the floor"), ["khana"] = T2("Ханаа дэлгэх", "Unfold the walls"), ["khaalga"] = T2("Хаалгаа уях", "Tie the door"),
        ["toono"] = T2("Тоноо өргөх", "Lift the toono"), ["uni"] = T2("Униа хатгах", "Push in the roof poles"), ["esgii"] = T2("Эсгий бүрэх", "Wrap the felt"),
        ["buurees"] = T2("Бүрээс хучих", "Pull on the cover"), ["buslvvr"] = T2("Бүслүүр чангалах", "Tighten the ropes"), ["tulga"] = T2("Тулга тавих", "Set up the stove"),
    };
    static readonly Dictionary<string, Tx> PART = new Dictionary<string, Tx>
    {
        ["khana"] = T2("хана", "walls"), ["khaalga"] = T2("хаалга", "a door"), ["toono"] = T2("тооно", "a toono"), ["bagana"] = T2("багана", "posts"), ["uni"] = T2("унь", "roof poles"),
        ["esgii"] = T2("эсгий", "felt"), ["buurees"] = T2("бүрээс", "a cover"), ["buslvvr"] = T2("бүслүүр", "ropes"),
    };
    public static string PartName(string k) => PART.TryGetValue(k, out var t) ? t.ToString() : k;
    // where a missing part comes from
    static readonly Dictionary<string, Tx> FROM = new Dictionary<string, Tx> { ["esgii"] = T2("Хонио хяргаж, ноосоор эсгий хий.", "Shear sheep and make felt from the wool."), ["buurees"] = T2("Дэлгүүрээс авна.", "The shop sells it.") };
    static readonly Tx FROM_GRANDPA = T2("Мужаан өвөөгөөс авна.", "Grandpa Craftsman makes it.");
    // grandpa's hint: what comes next, and why (from the ger lesson)
    static readonly Dictionary<string, Tx> WHY = new Dictionary<string, Tx>
    {
        ["shal"] = T2("Эхлээд тэгш газар шалаа тавина. Тэгш шал гэрийг тэгш, бат бөх болгоно.", "First lay the floor on flat ground: a level floor keeps the ger straight and strong."),
        ["khana"] = T2("Шалан дээр ханаа тойруулан дэлгэнэ. Хана бол эвхэгддэг тор.", "Next, stand the folding lattice walls in a circle on the floor."),
        ["khaalga"] = T2("Хоёр ханын дунд хаалгаа уяна. Хаалга урагшаа харна.", "Tie the door between two walls, facing south."),
        ["toono"] = T2("Одоо тоноо хоёр баганаар тулж өргөнө. Үүнийг хоёр хүн хамт хийдэг.", "Now lift the toono up on its two posts. Two people do this together."),
        ["uni"] = T2("Униа тоононд хатгаад ханын толгойд уяна, дөрвөн зүгт ээлжлэн.", "Push the roof poles into the toono and tie them to the walls, taking turns all around."),
        ["esgii"] = T2("Хана, униа ноосон эсгийгээр бүрнэ. Эсгий өвлийн хүйтнээс хамгаална.", "Wrap the walls and roof in wool felt to keep out the winter cold."),
        ["buurees"] = T2("Эсгийн гадуур цагаан бүрээс хучина: бороо, нарнаас хамгаална.", "Pull the white cover over the felt: it keeps out rain and sun."),
        ["buslvvr"] = T2("Гурван бүслүүрээр бүсэлбэл гэр хүчтэй салхинд ч хөдлөхгүй.", "Three ropes around the ger keep it steady even in strong wind."),
        ["tulga"] = T2("Сүүлд нь голд тулгаа тавьж, анхны цайгаа чанана.", "Last, set the stove in the middle and make the first tea."),
    };

    GerSave G => V.ger;
    int Part(string k) => (int)SaveData.Get(G.parts, k);
    void PartAdd(string k, int n) { var e = G.parts.Find(q => q.k == k); if (e == null) G.parts.Add(e = new KV { k = k }); e.v += n; }
    /** How far my ger is built (9 = finished); 0 = nothing on a plot yet. */
    public int MyStage => G.stage > 0 ? G.stage : V.gerBuilt ? GerRules.DONE : 0;
    bool young => V.age < 9;

    // ---------------------------------------------------------------- the stage art: the lesson's ger, at this point of the build
    static PxCanvas LessonGer(int stage, float sub, float t)
    {
        var c = new PxCanvas(Scenes.W, Scenes.H); Scenes.Ger(c, new SceneState { stage = stage, sub = sub }, t); return c;
    }
    static void BlitAlpha(PxCanvas dst, PxCanvas src, int ox, int oy, float a)
    {
        for (int y = 0; y < src.H; y++) for (int x = 0; x < src.W; x++) { var p = src.Get(x, y); if (p.a == 0) continue; var q = dst.Get(ox + x, oy + y); dst.Put(ox + x, oy + y, a >= 1 ? p : (Color32)Color.Lerp(q, p, a * p.a / 255f)); }
    }
    Action<PxCanvas, float, TaskState> GerArt(int stage) => (x, t, st) =>
    {
        float q = st != null && st.n > 0 ? st.progress / (float)st.n : 0; bool sub = stage < GerRules.DONE && (GerRules.STEPS[stage] == "khana" || GerRules.STEPS[stage] == "uni");
        if (sub && st != null && st.kind != "choose") BlitAlpha(x, LessonGer(stage + 1, q, t), 20, 20, 1); // walls and poles go up one by one
        else
        {
            BlitAlpha(x, LessonGer(stage, 0, t), 20, 20, 1);
            if (q > 0 && st.kind != "choose") BlitAlpha(x, LessonGer(stage + 1, 0, t), 20, 20, q);
        }
    };
    /** The hands-on task for one building step. */
    TaskSpec StepSpec(string step)
    {
        int stage = G.stage; var art = GerArt(stage);
        TaskSpec Spec(TaskSpec s) { s.title = STEP[step]; s.art = art; return s; }
        var uniSpots = Enumerable.Range(0, 9).Select(i => { float a = i / 9f * Mathf.PI * 2; return new Vector2(Js.Round(80 + Mathf.Cos(a) * 30), Js.Round(62 + Mathf.Sin(a) * 9)); }).ToArray();
        int SideOf(Vector2 p) => p.x > 96 ? 0 : p.x < 64 ? 2 : p.y > 62 ? 1 : 3;
        switch (step)
        {
            case "shal": return Spec(new TaskSpec { kind = "spots", hint = T2("Шалны банзнуудыг нэг нэгээр нь тавь.", "Lay the floor boards one by one."), spots = new[] { new Vector2(52, 84), new Vector2(68, 85), new Vector2(92, 85), new Vector2(108, 84) } });
            case "khana": return Spec(new TaskSpec { kind = "drag", n = 5, hint = T2("Хана бүрийг шараар нь чирж дэлгэ. 5 хана!", "Drag each lattice open by its gold handle. 5 walls!"), path = new[] { new Vector2(52, 74), new Vector2(108, 74) } });
            case "khaalga": return Spec(new TaskSpec { kind = "turn", start = Mathf.PI, target = Mathf.PI / 2, tol = 0.35f, hint = T2("Хаалгыг эргүүлж урд зүг (доош) харуулаад тавь.", "Turn the door to face south (down), then let go."),
                onMiss = _ => TaskSay(T2("Өвөө: Урд зүгээс нар тусдаг, хойд зүгээс хүйтэн салхи үлээдэг. Хаалгаа урагш харуулъя!", "Grandpa: The sun comes from the south and cold winds from the north. Face the door south!")) });
            case "toono": return Spec(new TaskSpec { kind = "hold", holders = 2, two = "help", ms = 1500, hint = Players == 2 ? T2("Хоёулаа зэрэг дарж байгаад тоноо өргө!", "Both hold at the same time to lift the toono!") : T2("Дарж байгаад өргө. Нөгөө баганыг өвөө барьж байна.", "Hold to lift. Grandpa is holding the other post.") });
            case "uni": return Spec(new TaskSpec { kind = "spots", spots = uniSpots, side = uniSpots.Select(SideOf).ToArray(), rule = GerRules.UnisTilt, hint = T2("Униа дөрвөн зүгт ээлжлэн хатга, тооно тэгш байх ёстой.", "Push the poles in all around, so the toono stays level."),
                onMiss = _ => TaskSay(T2("Өвөө: Тооно хазайчихлаа! Нөгөө талд нь унь хатга.", "Grandpa: The toono is tilting! Put a pole on the other side.")) });
            case "esgii": return Spec(new TaskSpec { kind = "drag", n = 2, hint = T2("Эсгийг гэрийн эргэн тойронд ороо.", "Wrap the felt around the ger."), path = new[] { new Vector2(52, 60), new Vector2(108, 60) } });
            case "buurees": return Spec(new TaskSpec { kind = "drag", n = 1, hint = T2("Цагаан бүрээсийг дээрээс доош тат.", "Pull the white cover down over the felt."), path = new[] { new Vector2(80, 36), new Vector2(80, 84) } });
            case "buslvvr": return Spec(new TaskSpec { kind = "hold", holders = 1, ms = 900, n = 3, hint = T2("Бүслүүр бүрийг дарж байгаад чангал.", "Hold to pull each rope tight.") });
            case "tulga": return Spec(new TaskSpec { kind = "spots", hint = T2("Голд нь тулгаа тавьж, анхны цайгаа чана.", "Set the stove in the middle and make the first tea."), spots = new[] { new Vector2(80, 78) } });
        }
        return null;
    }

    // ---------------------------------------------------------------- the plot
    /** My plot follows my build; the room hears of it when online. */
    void PutLot(int i, bool first)
    {
        if (V.lot.i < 0 || first) V.lot.i = i;
        if (net.Online)
        {
            bool mine = net.lots != null && net.lots.Any(l => l != null && l.by == V.id);
            if (first || !mine) { net.GerClaim(V.lot.i, V.lot.roof, V.lot.door, G.stage); for (int s = 2; s <= G.stage; s++) net.GerStage(s); } else net.GerStage(G.stage); // a claim lands at stage 1: send the rest in order
        }
        V.Save();
    }
    /** On joining a room: a ger part-way up comes along to a free plot. */
    public void GerSync()
    {
        int stage = MyStage; if (stage < 1 || V.lot.i < 0 || !net.Online || net.lots == null) return;
        int at = Array.FindIndex(net.lots, l => l != null && l.by == V.id);
        if (at >= 0) { V.lot.i = at; int srv = net.lots[at].stage > 0 ? net.lots[at].stage : GerRules.DONE; for (int s = srv + 1; s <= stage; s++) net.GerStage(s); /* the server takes one stage at a time */ V.Save(); return; }
        int i = net.lots.Length > V.lot.i && net.lots[V.lot.i] == null ? V.lot.i : Array.FindIndex(net.lots, l => l == null);
        if (i < 0) { Toast(Loc.L("Энэ өрөөнд сул газар алга. Гэр чинь зөвхөн чамд харагдана.", "No free plot in this room. Your ger shows only for you.")); return; }
        V.lot.i = i; V.Save(); net.GerClaim(i, V.lot.roof, V.lot.door, stage); for (int s = 2; s <= stage; s++) net.GerStage(s); // the claim starts at stage 1; the server takes the rest one by one
    }
    void LotAction(int i)
    {
        var other = LotOf(i);
        if (other != null) { if (other.stage > 0 && other.stage < GerRules.DONE) Toast(other.n + Loc.L(" гэрээ барьж байна. Дараа нь зочлоорой!", " is still building their ger. Visit when it is done!")); else Toast(other.n + Loc.L("-ийн гэр", "’s ger")); return; }
        if (V.lot.i >= 0 && V.lot.i != i) { Toast(Loc.L("Танд аль хэдийн гэр байна!", "You already have a ger!")); return; }
        if (MyStage >= GerRules.DONE && V.lot.i == i) { EnterGer(); return; }
        if (!building) { if (IsoOn) GerBuildOpen(i); else StartCoroutine(BuildGer(i)); } // isometric: build on the plot (GerBuild.cs)
    }

    // ---------------------------------------------------------------- building
    bool building, together;
    /** Build (or keep building) a ger on plot i, one hands-on step at a time. */
    IEnumerator BuildGer(int i)
    {
        if (building || G.stage >= GerRules.DONE) yield break;
        if (G.stage > 0 && V.lot.i >= 0) i = V.lot.i; // your ger is already going up on its own plot
        building = true; together = false;
        int wrongHere = 0; Tx? say = null;
        try
        {
            while (G.stage < GerRules.DONE)
            {
                var options = GerRules.STEPS.Where((s, k) => k >= G.stage && GerRules.NEEDS[s].All(kv => Part(kv.Key) >= kv.Value))
                    .Select(s => new TaskOption { id = s, icon = Px.Icon("part:" + (s == "shal" || s == "tulga" ? s : GerRules.NEEDS[s].Keys.First())), label = STEP[s] }).ToList();
                var w = Task(new TaskSpec { kind = "choose", title = T2("Дараа нь юу хийх вэ?", "What comes next?"), hint = T2("Гэрийнхээ дараагийн хэсгийг сонго.", "Choose the next part of your ger."), options = options, art = GerArt(G.stage), say = say, shake = say != null });
                yield return w; say = null;
                if (!w.r.done) yield break;
                var res = GerRules.Try(G.stage, w.r.pick, Part);
                if (!res.ok && res.why == "order") // a gentle sag: grandpa laughs and helps, nothing is lost
                {
                    G.mistakes++; wrongHere++; V.Save();
                    bool name = wrongHere >= (young ? 1 : 2);
                    say = LessonDone("ger") ? T2($"Өвөө: Дараа нь “{STEP[res.next].mn}”.", $"Grandpa: Next comes “{STEP[res.next].en}”.")
                        : name ? T2($"Өвөө: Хэ хэ, гэр унжчихлаа! {WHY[res.next].mn}", $"Grandpa: Ha ha, the ger is sagging! {WHY[res.next].en}")
                        : T2("Өвөө: Хэ хэ, гэр унжчихлаа! Юуг нь эхэлж хийдэг билээ?", "Grandpa: Ha ha, the ger is sagging! What comes first?");
                    continue;
                }
                if (!res.ok) { var miss = res.missing.Keys.ToList(); Toast(Loc.L("Дутуу: ", "Missing: ") + string.Join(", ", miss.Select(PartName)) + ". " + (FROM.TryGetValue(miss[0], out var f) ? f : FROM_GRANDPA)); yield break; }
                wrongHere = 0;
                string step = w.r.pick; var sw = Task(StepSpec(step)); yield return sw;
                if (!sw.r.done) yield break;
                if (sw.r.together) together = true;
                foreach (var kv in GerRules.NEEDS[step]) PartAdd(kv.Key, -kv.Value);
                G.stage++; G.mistakes += sw.r.mistakes; G.poles?.Clear(); // poles half-placed in the isometric view are done with
                PutLot(i, G.stage == 1);
            }
            FinishGer();
        }
        finally { building = false; }
    }
    void FinishGer()
    {
        V.gerBuilt = true; G.perfect = G.mistakes == 0;
        HandsOnFirst("ger");
        Reward(0, 30); Quest("ger"); Sfx.Win(); Confetti(70);
        if (together && p2 != null) App.LogActivity("together", "ger|family|" + p2.name, V.id);
        V.Save(); CheckAchievements();
        Open(new GerReadyModal());
    }
    sealed class GerReadyModal : Modal
    {
        public override void Draw(Game g)
        {
            g.Picture(Px.Icon("ui:ger"), 72);
            GUILayout.Label("<b>" + Loc.L("Гэр бэлэн боллоо!", "Your ger is ready!") + "</b>", new GUIStyle(g.sTitle) { alignment = TextAnchor.MiddleCenter });
            g.Fact(g.G.perfect ? Loc.L("Нэг ч алдаагүй барилаа! Монгол гэрийг гэр бүлээрээ 1–2 цагт барьдаг.", "Built without a single slip! A family puts up a Mongol ger in 1–2 hours.")
                : Loc.L("Монгол гэрийг гэр бүлээрээ 1–2 цагт барьдаг. Нүүдэлчид жилдээ хэдэн удаа нүүдэг.", "A family puts up a Mongol ger in 1–2 hours. Herders move several times a year."), true, Loc.L("Мэдсэн үү?", "Did you know?"));
            g.HandsOnCard("ger");
            if (g.Btn(Loc.L("Өнгийг нь сонгох", "Choose its colours"), true, 46)) g.Later(() => g.Swap(new GerColoursModal()));
        }
    }
    /** Pick the roof band and door colours of a finished ger. */
    sealed class GerColoursModal : Modal
    {
        string roof, door;
        public override void Draw(Game g)
        {
            roof ??= g.V.lot.roof ?? "blue"; door ??= g.V.lot.door ?? "red";
            g.Title(Loc.L("Гэрийнхээ өнгийг сонго", "Choose your ger’s colours"));
            g.Picture(Px.GerSprite(Px.ROOF[roof], Px.DOOR[door]), 120);
            GUILayout.Label(Loc.L("Дээврийн хээ", "Roof band"), g.sSmall); Swatches(g, Px.ROOF, roof, k => roof = k);
            GUILayout.Label(Loc.L("Хаалга", "Door"), g.sSmall); Swatches(g, Px.DOOR, door, k => door = k);
            if (g.Btn(Loc.L("Болсон", "Done"), true, 46)) g.Later(() => { g.V.lot.roof = roof; g.V.lot.door = door; g.V.Save(); g.net.StyleGer(roof, door, g.V.lot.furn); g.Close(); });
        }
    }

    // ---------------------------------------------------------------- gathering: wool, felt and the wooden parts
    public bool Shearable(int i) { string k = i.ToString(); var e = V.shorn.Find(q => q.k == k); return e == null || SaveData.NowMs() - e.v > GerRules.SHEAR_REGROW_MS; }
    /** While a ger is still to be built, a sheep with its wool grown is for shearing; otherwise for petting. */
    bool ShearSheep(Sheep q) => MyStage < GerRules.DONE && Shearable(q.i);
    /** Shear one sheep: blunt shears, a calm sheep, two handfuls of wool. */
    IEnumerator Shear(int i)
    {
        if (!Shearable(i)) { Toast(Loc.L("Энэ хонины ноос дахин ургаж байна. Өөр хонь хярга!", "This sheep’s wool is still growing back. Try another sheep!")); yield break; }
        var sheepImg = Px.Animal("sheep", 0);
        var w = Task(new TaskSpec
        {
            kind = "drag", n = 3, title = T2("Хонь хяргах", "Shear a sheep"), path = new[] { new Vector2(44, 52), new Vector2(116, 52) },
            hint = T2("Хайч нь мохоо үзүүртэй, хонь тайван байна. Нуруугаар нь чирж ноосыг нь хярга.", "The shears have blunt tips and the sheep is calm and unharmed. Drag along its back to shear the wool."),
            art = (x, t, st) =>
            {
                const int sc = 5; int sw = sheepImg.W * sc, sh = sheepImg.H * sc, X = Js.Round(80 - sw / 2f), Y = 30; float q = st.progress / (float)st.n;
                Px.R(x, 0, 84, 160, 36, "#76b947"); x.BlitScaled(sheepImg, X, Y, sw, sh);
                int cut = Mathf.RoundToInt(sheepImg.W * q); if (cut > 0) x.BlitScaled(Px.Shorn(sheepImg), X, Y, cut * sc, sh, false, 0, 0, cut, sheepImg.H);
            },
        });
        yield return w; if (!w.r.done) yield break;
        var e = V.shorn.Find(k => k.k == i.ToString()); if (e == null) V.shorn.Add(e = new KV { k = i.ToString() }); e.v = SaveData.NowMs();
        G.wool += GerRules.SHEAR_WOOL;
        Quest("shear"); Reward(0, 4); Toast(Loc.L($"+{GerRules.SHEAR_WOOL} ноос. Ноосоор эсгий хийнэ.", $"+{GerRules.SHEAR_WOOL} wool. Wool becomes felt.")); V.Save();
    }
    static Action<PxCanvas, float, TaskState> FluffArt(Func<TaskState, int> n) => (x, t, st) =>
    {
        Px.R(x, 24, 70, 112, 26, "#a46d3b"); Px.R(x, 24, 94, 112, 2, "#8a5a33");
        int k = n(st); for (int i = 0; i < k; i++) { int X = 40 + (i % 5) * 18, Y = 62 - (i / 5) * 8; Px.R(x, X - 1, Y - 1, 16, 10, "#2b1e2e"); Px.R(x, X, Y, 14, 8, "#f6f2e9"); }
    };
    (string step, TaskSpec spec)? FeltSpec()
    {
        bool two = Players == 2;
        if (G.mats >= 1) return ("roll", new TaskSpec
        {
            kind = "rhythm", n = 10, water = true, two = "help", speed = 0.9f, win = 0.24f, title = T2("Эсгий өнхрүүлэх", "Roll the felt"),
            hint = two ? T2("Нэг нь хэмнэлтэй өнхрүүл, нөгөө нь хатахад ус цац! Эрт үед эсгийг морьдоор чирж өнхрүүлдэг байжээ.", "One rolls in rhythm, the other sprinkles water when it dries! Long ago, horses pulled the felt roll.")
                : T2("Басар өнхрүүлж байна. Хатахад нь цэнхэр дусал дээр дарж ус цац!", "Basar pulls the roll. When it dries, tap the blue drop to sprinkle water!"),
            art = (x, t, st) =>
            {
                Px.R(x, 0, 70, 160, 50, "#76b947"); int rx = 40 + Js.Round((t * 20) % 60);
                Px.R(x, rx - 13, 55, 34, 22, "#2b1e2e"); Px.R(x, rx - 12, 56, 32, 20, "#d8c8a8"); for (int i = 0; i < 4; i++) Px.R(x, rx - 12 + i * 8, 56, 2, 20, "#b8a47e");
                if (!two) x.Blit(Px.Bankhar(1 + (int)(t * 6) % 2), rx + 26, 58);
            },
        });
        if (G.fluff >= GerRules.FELT_WOOL) return ("lay", new TaskSpec
        {
            kind = "spots", title = T2("Ноос дэлгэж норгох", "Lay and wet the wool"),
            hint = T2("Ноосоо давхарлаж дэлгээд, дээрээс нь ус цац. Ус, дулаан ноосыг хооронд нь наалдуулна.", "Spread the wool in layers, then sprinkle water on top. Water and warmth make the fibres stick together."),
            spots = new[] { new Vector2(48, 74), new Vector2(72, 74), new Vector2(96, 74), new Vector2(120, 74), new Vector2(56, 50), new Vector2(84, 46), new Vector2(112, 50) },
            art = FluffArt(st => Mathf.Min(4, st.progress) * 2),
        });
        if (G.wool >= 1) return ("beat", new TaskSpec
        {
            kind = "rhythm", n = 8, speed = 0.9f, win = 0.24f, title = T2("Ноос цохих", "Beat the wool"),
            hint = T2("Хоёр саваагаар ноосоо хэмнэлтэй цохиж сэвсийлгэ. Тэгвэл эсгий жигд болно.", "Beat the wool with two sticks, in rhythm, to fluff it up so the felt comes out even."),
            art = (x, t, st) => { FluffArt(s => Mathf.Max(2, 8 - s.progress))(x, t, st); int up = (int)(t * 6) % 2; Px.R(x, 46, 30 + up * 8, 4, 30, "#6b4220"); Px.R(x, 110, 38 - up * 8, 4, 30, "#6b4220"); },
        });
        return null;
    }
    /** The felt spot does the next felt step: beat the wool, lay and wet it, or roll it into felt. */
    IEnumerator Felt()
    {
        var f = FeltSpec();
        if (f == null) { Toast(Loc.L("Эхлээд хонио хяргаж ноос ав.", "Shear a sheep first to get wool.")); yield break; }
        var w = Task(f.Value.spec); yield return w; if (!w.r.done) yield break;
        if (f.Value.step == "beat") { G.fluff += G.wool; G.wool = 0; Toast(Loc.L("Ноос сэвсийлээ!", "The wool is fluffy!")); }
        else if (f.Value.step == "lay") { G.fluff -= GerRules.FELT_WOOL; G.mats++; Toast(Loc.L("Нойтон ноос бэлэн. Одоо өнхрүүлнэ!", "Wet wool ready. Now roll it!")); }
        else
        {
            G.mats--; PartAdd("esgii", 1); Quest("felt"); Reward(5, 10);
            if (w.r.together && p2 != null) App.LogActivity("together", "ger|family|" + p2.name, V.id);
            Toast(Loc.L($"Эсгий бэлэн боллоо! ({Part("esgii")}/2)", $"A felt is ready! ({Part("esgii")}/2)"));
        }
        V.Save();
    }
    string FeltLabel() { var f = FeltSpec(); return f == null ? Loc.L("Эсгий хийх", "Make felt") : f.Value.step == "roll" ? Loc.L("Эсгий өнхрүүлэх", "Roll felt") : f.Value.step == "lay" ? Loc.L("Ноос дэлгэх", "Lay wool") : Loc.L("Ноос цохих", "Beat wool"); }

    // Grandpa Craftsman's trades: goods from your herd, or coins
    static readonly Dictionary<string, Tx> GOOD = new Dictionary<string, Tx> { ["wool"] = T2("ноос", "wool"), ["aaruul"] = T2("ааруул", "aaruul"), ["urum"] = T2("өрөм", "öröm"), ["coins"] = T2("зоос", "coins") };
    static readonly Dictionary<string, Tx> MADE = new Dictionary<string, Tx>
    {
        ["khana"] = T2("Бургасаар хийсэн эвхэгддэг тор", "Folding lattice made of willow"), ["khaalga"] = T2("Урагш харах будагтай хаалга", "A painted door that faces south"),
        ["toono"] = T2("Тооно, хоёр багана, гурван бүслүүр", "The crown, two posts and three ropes"), ["uni"] = T2("Шинэс модоор хийсэн 9 унь", "Nine roof poles of larch wood"),
    };
    int HaveGood(string k) => k == "wool" ? G.wool : k == "coins" ? V.coins : V.Have(k);
    bool CanPay(Dictionary<string, int> cost) => cost.All(kv => HaveGood(kv.Key) >= kv.Value);
    void Pay(Dictionary<string, int> cost) { foreach (var kv in cost) { if (kv.Key == "wool") G.wool -= kv.Value; else if (kv.Key == "coins") V.coins -= kv.Value; else V.Stock(kv.Key, -kv.Value); } }
    /** The trade list in Grandpa Craftsman's talk. */
    public void TradeList()
    {
        string CostTxt(Dictionary<string, int> c) => string.Join(" + ", c.Select(kv => $"{kv.Value} {GOOD[kv.Key]}"));
        foreach (var t in GerRules.TRADES)
        {
            var goods = t.goods; var coins = new Dictionary<string, int> { ["coins"] = t.coins }; bool have = t.gives.All(kv => Part(kv.Key) >= kv.Value);
            // the name and its line wrap across the row; the two prices sit side by side under them, their words wrapping too
            GUILayout.BeginVertical(sBtn);
            GUILayout.BeginHorizontal(); IconAt(Px.Icon("part:" + t.id), 32);
            GUILayout.BeginVertical(); GUILayout.Label("<b>" + string.Join(", ", t.gives.Select(kv => $"{PartName(kv.Key)} ×{kv.Value}")) + "</b>", sLabel, GUILayout.ExpandWidth(true)); GUILayout.Label(have ? "√ " + Loc.L("Танд байгаа", "You have these") : MADE[t.id].ToString(), sSmall, GUILayout.ExpandWidth(true)); GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            foreach (var cost in new[] { goods, coins })
            {
                var c2 = cost; var t2 = t; bool was = GUI.enabled; GUI.enabled = CanPay(cost);
                var bs = new GUIStyle(cost == goods ? sPrimary : sBtn) { wordWrap = true, alignment = TextAnchor.MiddleCenter, fontSize = Ts(11) };
                if (GUILayout.Button(CostTxt(cost), bs, GUILayout.ExpandWidth(true), GUILayout.MinHeight(U(38)))) Later(() => { if (!CanPay(c2)) return; Pay(c2); foreach (var kv in t2.gives) PartAdd(kv.Key, kv.Value); Sfx.Chime(); Toast(string.Join(", ", t2.gives.Keys.Select(PartName)) + Loc.L(" авлаа!", " — got it!")); V.Save(); });
                GUI.enabled = was;
            }
            GUILayout.EndHorizontal(); GUILayout.EndVertical(); Gap(4);
        }
    }
    /** The white cover in the shop's tools tab. */
    public void CoverRow()
    {
        bool need = G.stage < 7 && Part("buurees") <= 0 && MyStage < GerRules.DONE;
        Row(Px.Icon("part:buurees"), Loc.L("Гэрийн цагаан бүрээс", "White ger cover"), need ? Loc.L("Эсгийн гадуур хучиж, бороо, нарнаас хамгаална.", "Goes over the felt and keeps out rain and sun.") : "√ " + Loc.L("Танд байгаа", "You have it"));
        if (need && SmallBtn(GerRules.COVER_PRICE.ToString())) Later(() => { if (Part("buurees") > 0) return; /* a double click: one cover, paid once */ if (V.coins < GerRules.COVER_PRICE) { Toast(Loc.L("Зоос хүрэлцэхгүй байна.", "Not enough coins.")); return; } V.coins -= GerRules.COVER_PRICE; PartAdd("buurees", 1); Sfx.Chime(); Toast(Loc.L("Бүрээс худалдаж авлаа!", "You bought the cover!")); V.Save(); });
        EndRow();
    }
    /** Ger parts in the bag: wool, felt in the making, and the parts. */
    public IEnumerable<(string icon, string name, int n)> GerBag()
    {
        yield return ("part:wool", Loc.L("Ноос", "Wool"), G.wool);
        yield return ("part:wool", Loc.L("Сэвсгэр ноос", "Fluffed wool"), G.fluff);
        yield return ("part:esgii", Loc.L("Нойтон ноос", "Wet wool"), G.mats);
        foreach (var p in G.parts) if (p.v > 0) yield return ("part:" + p.k, PartName(p.k), (int)p.v);
    }

    // ---------------------------------------------------------------- learned by hand
    /** Done by hand for the first time: the parents' log hears of it. */
    public void HandsOnFirst(string id) => App.LogActivity("hand", id, V.id);
    /** The real-life family activity that goes with it, until it is done at home. */
    public void HandsOnCard(string id)
    {
        var a = GerRules.HANDS_ON.Find(x => x.id == id); if (a == null || !a.home) return;
        var l = Lessons.ALL.FirstOrDefault(q => q["id"].S == a.lesson); if (l == null || !l.Has("family") || V.Lesson(a.lesson).family > 0) return;
        FamilyCard(l);
    }
}
