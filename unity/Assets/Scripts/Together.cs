// Two players on one screen (village.js "together"): a family member or a friend joins on the same phone or computer.
// Player 2 walks with the arrow keys (or the right half of the screen as a joystick), runs with right Shift, and uses
// Enter to shear the sheep beside them or work the felt. Some hands-on tasks are done together: lifting the toono,
// rolling felt. Player 2 steps aside while player 1 rides, races, fights or has a window open.
using System.Linq;
using UnityEngine;

public sealed partial class Game
{
    public sealed class Player2 { public string name; public Look look; public float px, py, vx, vy; public int dir, face = 2; public bool moving, running, hidden; public Vector2 stick; public int stickId = -99; public Vector2 s0, sp; }
    public Player2 p2;

    Vector2 BesideMe()
    {
        foreach (var (dx, dy) in new[] { (1.2f, 0.2f), (-1.2f, 0.2f), (0f, 1f), (1.2f, 1f), (-1.2f, 1f), (2f, 0.2f), (-2f, 0.2f) })
            if (FreeAt(px + dx + 0.5f, py + dy + 0.82f)) return new Vector2(px + dx, py + dy);
        return new Vector2(px, py);
    }
    /** A family member (or a friend) joins on this screen; null sends them home. */
    public void Together(FamilyMember f, bool friend = false)
    {
        if (f == null && !friend) { if (p2 != null) Toast(p2.name + Loc.L(" баяртай!", " says bye!")); p2 = null; return; }
        var deels = Rules.DEELS.ToList(); var look = V.look.Copy(); look.deel = deels[(deels.IndexOf(V.look.deel) + 3 + deels.Count) % deels.Count]; look.pet = null; look.ride = null; look.title = null;
        var at = BesideMe();
        p2 = new Player2 { name = f != null ? f.Name.ToString() : Loc.L("Найз", "Friend"), look = look, px = at.x, py = at.y };
        Sfx.Chime();
        Toast(p2.name + (Game.TouchUI ? Loc.L(" хамт тоглож байна! Дэлгэцийн баруун тал бол түүний джойстик.", " is playing too! The right half of the screen is their joystick.")
            : Loc.L(" хамт тоглож байна! Сум товч, баруун Shift, Enter: тэр.", " is playing too! Arrow keys, right Shift and Enter are theirs.")));
    }
    bool P2Busy => riding || race != null || battle != null || modal != null;
    /** Player 2 walks and steps aside while player 1 is busy. */
    void MoveP2(float dt)
    {
        var p = p2; if (p == null) return;
        if (P2Busy) { p.hidden = true; p.vx = p.vy = 0; p.moving = false; p.stick = Vector2.zero; p.stickId = -99; return; } // a window ate the lift: drop the stick too
        if (p.hidden) { var at = BesideMe(); p.px = at.x; p.py = at.y; p.hidden = false; }
        float ix = (Input.GetKey(KeyCode.RightArrow) ? 1 : 0) - (Input.GetKey(KeyCode.LeftArrow) ? 1 : 0), iy = (Input.GetKey(KeyCode.DownArrow) ? 1 : 0) - (Input.GetKey(KeyCode.UpArrow) ? 1 : 0), mag = 1, tvx = 0, tvy = 0;
        float sm = p.stick.magnitude; if (ix == 0 && iy == 0 && sm > 0.18f) { ix = p.stick.x; iy = p.stick.y; mag = Mathf.Clamp((sm - 0.18f) / 0.45f, 0.45f, 1); }
        p.running = Input.GetKey(KeyCode.RightShift);
        float SP = p.running ? RUN_SPEED : SPEED;
        if (ix != 0 || iy != 0) { float n = Mathf.Sqrt(ix * ix + iy * iy); tvx = ix / n * SP * mag; tvy = iy / n * SP * mag; }
        float a = Mathf.Min(1, dt * ACCEL); p.vx += (tvx - p.vx) * a; p.vy += (tvy - p.vy) * a;
        if (Mathf.Abs(p.vx) < 0.01f) p.vx = 0; if (Mathf.Abs(p.vy) < 0.01f) p.vy = 0;
        bool over = !FreeAt(p.px + 0.5f, p.py + 0.82f); float d0 = Dist(p.px, p.py, px, py);
        bool Ok(float x, float y) => Dist(x, y, px, py) <= 14 || Dist(x, y, px, py) < d0; // a gentle tether: never wander off the shared screen
        if (p.vx != 0 && FreeAt(p.px + 0.5f + p.vx * dt, p.py + 0.82f, over) && Ok(p.px + p.vx * dt, p.py)) p.px += p.vx * dt; else p.vx = 0;
        if (p.vy != 0 && FreeAt(p.px + 0.5f, p.py + 0.82f + p.vy * dt, over) && Ok(p.px, p.py + p.vy * dt)) p.py += p.vy * dt; else p.vy = 0;
        p.moving = Mathf.Sqrt(p.vx * p.vx + p.vy * p.vy) > 0.3f;
        if (p.moving) p.dir = Mathf.Abs(p.vx) > Mathf.Abs(p.vy) * 0.9f ? (p.vx < 0 ? 1 : 2) : (p.vy < 0 ? 3 : 0);
        if (Mathf.Abs(p.vx) > 0.3f) p.face = p.vx < 0 ? 1 : 2;
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) P2Action();
    }
    /** Player 2's hands: shear the sheep beside them, or work the felt. */
    void P2Action()
    {
        var p = p2; if (p == null || p.hidden) return;
        float fx = p.px + 0.5f, fy = p.py + 0.82f; var felt = World.objects.Find(o => o.kind == "felt");
        if (felt != null && Dist(fx, fy, felt.doorX + 0.5f, felt.doorY + 0.7f) < 1.4f) { StartCoroutine(Felt()); return; }
        var sh = sheep.Find(q => Dist(fx, fy, q.px + 0.5f, q.py + 0.7f) < 1.4f);
        if (sh != null && ShearSheep(sh)) { StartCoroutine(Shear(sh.i)); return; }
        Toast(p.name + Loc.L(": хонь эсвэл эсгий хийх газрын дэргэд Enter дар.", ": press Enter next to a sheep or the felt spot."));
    }
    /** The right half of the screen is player 2's joystick (touch). Returns true when the finger is theirs. */
    bool P2Pointer(int id, Vector2 pos, bool down, bool up)
    {
        var p = p2; if (p == null || p.hidden || id < 0) return false;
        if (down) { if (pos.x <= Screen.width / 2f || OverUI(pos)) return false; p.stickId = id; p.s0 = p.sp = pos; return true; }
        if (p.stickId != id) return false;
        p.sp = pos; var d = (pos - p.s0) / (STICK_R * UiScale); if (d.magnitude > 1) d.Normalize(); p.stick = new Vector2(d.x, -d.y);
        if (up) { p.stickId = -99; p.stick = Vector2.zero; }
        return true;
    }
    void P2Draw(float t)
    {
        var p = p2; if (p == null || p.hidden || InScene) return;
        Shadow(p.px * T, p.py * T, p.py); Person(p.look, p.dir, Frame(p.moving, p.running), p.px, p.py, p.py, 0, null, p.moving, p.running);
    }
    void P2Label(float ts)
    {
        var p = p2; if (p == null || p.hidden) return;
        var pt = PxGui(p.px * T + 8, p.py * T - 16); Outlined(pt.x, pt.y, p.name, ts * 0.85f, Px.Col("#7fe07a"));
        if (p.stickId != -99)
        {
            var b = new Vector2(p.s0.x, Screen.height - p.s0.y); float R = STICK_R * UiScale;
            var old = GUI.color; GUI.color = new Color(0.6f, 1, 0.6f, 0.35f); GUI.DrawTexture(new Rect(b.x - R, b.y - R, R * 2, R * 2), Tex(Disc()));
            var k = b + new Vector2(p.stick.x, -p.stick.y) * R; GUI.color = new Color(0.6f, 1, 0.6f, 0.8f); GUI.DrawTexture(new Rect(k.x - R * 0.42f, k.y - R * 0.42f, R * 0.84f, R * 0.84f), Tex(Disc())); GUI.color = old;
        }
    }
    sealed class TogetherModal : Modal
    {
        public override void Draw(Game g)
        {
            g.Title(Loc.L("Хамтдаа тоглох", "Play together"));
            g.Text(Loc.L("Нэг утас, таблет дээр хоёулаа: тоноо хамт өргөж, эсгийгээ хамт өнхрүүлээрэй!", "Two of you on one phone or tablet: lift the toono and roll felt together!"));
            foreach (var f in g.App.family) { g.Row(Px.Face(f.face), f.Name.ToString(), null); if (g.SmallBtn(Loc.L("Хамт", "Join"), true, 90)) { var ff = f; g.Later(() => { g.Close(); g.Together(ff); }); } g.EndRow(); }
            g.Row(Px.Mascot(0), Loc.L("Найз", "Friend"), null); if (g.SmallBtn(Loc.L("Хамт", "Join"), true, 90)) g.Later(() => { g.Close(); g.Together(null, true); }); g.EndRow();
            if (g.p2 != null && g.Btn(Loc.L("Хамтдаа тоглохоо болих", "Stop playing together"), false, 36)) g.Later(() => { g.Close(); g.Together(null); });
            if (g.Btn(Loc.L("Хаах", "Close"), false, 30)) g.Later(g.Close);
        }
        public override void Press(Game g) => g.Close();
    }
}
