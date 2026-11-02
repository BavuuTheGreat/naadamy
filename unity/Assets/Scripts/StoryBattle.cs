// The story's battles: a side-on fight on a painted panorama, in the depth of the ground like a beat-'em-up, made to be fair
// and readable. Every enemy shows what it is about to do before it does it (a red “!”, a drawn bow with its aim line, a red
// lane before a charge), only two may attack at once, and every attack leaves an opening. Temüjin strikes (three blows make
// a combo that staggers), looses whistling arrows, dodges through attacks, and when the allies' horn is full calls Jamukha's
// and Toghrul's riders to sweep the field. Nobody is hurt: enemies lose their courage (♥) and flee, as in the village battle.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed partial class Game
{
    public sealed class Fighter
    {
        public string kind, name; public Look look; public bool mounted;
        public float x, y, vx, vy, face = 1; public int hp, max;
        public string state = "idle"; public float st, cd, hitT = -9, iframe, knock, aimY, chargeTo;
        public bool fled, gone, token, armored; public int combo; public float comboT;
        public float lineT = -9; public string line, move;   // line: what they shout; move: the captain's next attack
        public int Face => face < 0 ? 1 : 2;
    }

    /** The night raid at the Buura steppe (about 1184): cross the Khilok, break the Merkit lines, and find Börte. */
    public sealed class NightRaid : Stage
    {
        const float Y0 = 126, Y1 = 172, HURT_I = 0.9f;
        readonly Game g; readonly List<Fighter> foes = new List<Fighter>(), allies = new List<Fighter>(), folk = new List<Fighter>();
        sealed class Shot { public float x, y, vx, life; public bool mine; public int dmg; }
        readonly List<Shot> shots = new List<Shot>();
        sealed class Drop { public float x, y, t; }
        readonly List<Drop> drops = new List<Drop>();
        sealed class Floater { public float x, y, t; public string text; public Color col; }
        readonly List<Floater> floats = new List<Floater>();
        public Fighter me; int seg = -1, arrows = 8; float arrowT, rally, rallyT = -9, lockL, lockR = 320, goT = -9, hintT, deadT = -9, callT = -9, end = -9;
        int spawned; float spawnT; Fighter cart, borte; bool found;
        public int downs;

        public NightRaid(Game g)
        {
            this.g = g; W = 960;
            me = new Fighter { kind = "me", name = "temujin", look = new Look { deel = "blue", hat = "mongol", skin = "#D9A06E", coat = "bay" }, mounted = true, x = 60, y = 150, hp = 6, max = 6 };
            allies.Add(new Fighter { kind = "ally", name = "jamukha", look = new Look { deel = "purple", hat = "fur", skin = "#EDBB93", coat = "black" }, mounted = true, x = 24, y = 136, hp = 9, max = 9 });
            allies.Add(new Fighter { kind = "ally", name = "kereit", look = new Look { deel = "red", hat = "loovuuz", skin = "#D9A06E", coat = "gray" }, mounted = true, x = 20, y = 164, hp = 9, max = 9 });
            NextSeg();
        }
        // the stage draws at most four buttons: while the allies' horn is full, Allies takes Roll's place so a touch screen can blow it (T on a keyboard)
        public override (string id, Tx label)[] Buttons => seg == 3
            ? new[] { ("call", new Tx("Дуудах", "Call")), ("dash", new Tx("Бултах", "Dodge")), ("roll", new Tx("Эргэх", "Roll")) }
            : new[] { ("strike", new Tx("Цохих", "Strike")), ("shoot", new Tx("Сум", "Arrow")), ("dash", new Tx("Бултах", "Dodge")), rally >= 1 ? ("rally", new Tx("Холбоо", "Allies")) : ("roll", new Tx("Эргэх", "Roll")) };

        // ---------------------------------------------------------------- the four parts of the fight
        void NextSeg()
        {
            seg++; spawned = 0; spawnT = 0.8f; foes.Clear(); shots.Clear();
            switch (seg)
            {
                case 0: lockL = 0; lockR = 320; break;
                case 1: lockL = 260; lockR = 640; break;
                case 2: lockL = 580; lockR = 960; break;
                case 3:
                    lockL = 600; lockR = 960;
                    // the Merkit camp flees in the moonlight; Börte is on one of the carts
                    string[] decs = { "brown", "green", "black", "teal", "brown" };
                    for (int i = 0; i < 5; i++) folk.Add(new Fighter { kind = "folk", look = new Look { deel = decs[i], hat = i % 2 == 0 ? "fur" : null, skin = "#D9A06E" }, x = 700 + i * 46, y = 132 + (i * 17) % 38, vx = -14 - i * 2 });
                    cart = new Fighter { kind = "cart", x = 880, y = 150, vx = -10 };
                    break;
            }
            if (seg > 0) { goT = -9; Sfx.Horn(); }
        }
        (string kind, int n)[] Wave => seg == 0 ? new[] { ("foot", 3) } : seg == 1 ? new[] { ("foot", 2), ("archer", 2), ("rider", 1) } : seg == 2 ? new[] { ("captain", 1), ("archer", 1) } : new (string, int)[0];
        int WaveTotal => Wave.Sum(w => w.n);
        void SpawnNext()
        {
            int k = spawned, acc = 0; string kind = null; foreach (var (kd, n) in Wave) { if (k < acc + n) { kind = kd; break; } acc += n; }
            if (kind == null) return;
            spawned++;
            bool fromLeft = kind == "foot" && seg == 1 && spawned % 2 == 0;
            float x = fromLeft ? lockL - 20 : lockR + 24, y = UnityEngine.Random.Range(Y0 + 4, Y1 - 4);
            var f = new Fighter { kind = kind, look = new Look { deel = kind == "captain" ? "brown" : "black", hat = kind == "captain" ? "loovuuz" : "fur", skin = "#D9A06E", coat = kind == "captain" ? "dun" : "black" }, x = x, y = y, face = fromLeft ? 1 : -1 };
            f.mounted = kind == "rider" || kind == "captain";
            f.hp = f.max = kind == "foot" ? 3 : kind == "archer" ? 2 : kind == "rider" ? 4 : 16;
            f.armored = kind == "captain"; f.cd = 0.6f + UnityEngine.Random.value * 0.6f;
            foes.Add(f);
            if (kind == "captain") { f.name = "Хаатай Дармала"; Say(f, Loc.L("Мэргидийн хүмүүс, над руу!", "Merkits, to me!")); }
        }

        // ---------------------------------------------------------------- every frame
        public override void Tick(StoryScene s, float dt)
        {
            float t = Time.time;
            if (end > 0) { if (t - end > 1.2f) { won = true; done = true; } UpdateCam(dt); return; }
            if (deadT > 0) { if (t - deadT > 1.6f) Retry(); return; }
            Hints(t);
            PlayerTick(s, dt, t);
            // the wave of this part arrives one by one
            if (seg < 3 && spawned < WaveTotal) { spawnT -= dt; if (spawnT <= 0) { SpawnNext(); spawnT = seg == 0 ? 2.2f : 1.6f; } }
            foreach (var f in foes.ToList()) FoeTick(s, f, dt, t);   // a captain may call more Merkits in
            foes.RemoveAll(f => f.gone);
            foreach (var a in allies) AllyTick(s, a, dt, t);
            ShotsTick(s, dt);
            if (seg < 3 && spawned >= WaveTotal && foes.All(f => f.fled))
            {
                if (goT < 0) { goT = t; Sfx.Good(); if (seg == 2) Say(allies[0], Loc.L("Мэргид зугтлаа! Анд минь, Бөртийг ол!", "The Merkits are fleeing! Find Börte, my anda!")); }
                if (me.x > lockR - 70 || seg == 2) NextSeg();
            }
            if (seg == 3) FindBorte(s, dt, t);
            drops.RemoveAll(d => { if (Mathf.Abs(d.x - me.x) < 12 && Mathf.Abs(d.y - me.y) < 10) { me.hp = Mathf.Min(me.max, me.hp + 1); Float(d.x, d.y - 20, "+♥", Px.Col("#ff7a7a")); Sfx.Good(); return true; } return t - d.t > 14; });
            floats.RemoveAll(f => t - f.t > 1.1f);
            UpdateCam(dt);
        }
        void UpdateCam(float dt)
        {
            float want = Mathf.Clamp(me.x - 130, lockL, Mathf.Max(lockL, lockR - StoryScene.VW));
            if (goT > 0 && seg < 3) want = Mathf.Clamp(me.x - 130, lockL, Mathf.Min(W - StoryScene.VW, lockR + 120 - StoryScene.VW));
            camX = Mathf.Lerp(camX, want, Mathf.Min(1, dt * 4));
        }
        void Hints(float t)
        {
            hintT += Time.deltaTime;
        }
        string Hint => seg == 0 && hintT < 32 ? (hintT < 6 ? Loc.L(TouchUI ? "Зүүн талд чирж морио жолоод" : "WASD / сумаар морио жолоод", TouchUI ? "Drag on the left to ride" : "Ride with WASD / the arrow keys")
            : hintT < 13 ? Loc.L(TouchUI ? "“Цохих”-ыг 3 удаа дараалан дар: 3 дахь нь хүчтэй" : "E / Space: цохих. 3 удаа дараалан дарвал хүчтэй", TouchUI ? "Tap Strike three times: the third blow is strong" : "E / Space: strike. Three in a row is a strong blow")
            : hintT < 20 ? Loc.L(TouchUI ? "Улаан “!” гарвал “Бултах”-ыг дар" : "Улаан “!” гарвал Shift-ээр булт", TouchUI ? "When a red “!” shows, tap Dodge" : "When a red “!” shows, dodge with Shift")
            : hintT < 26 ? Loc.L(TouchUI ? "“Сум”: исгэрэх сумаа хол руу харва" : "R: исгэрэх сумаа харва", TouchUI ? "Arrow: loose a whistling arrow at enemies far away" : "R: loose a whistling arrow")
            : Loc.L(TouchUI ? "“Эргэх”: удаан бултаад илүү удаан хамгаалагдана" : "C: удаан эргэж бултна, илүү удаан хамгаалагдана", TouchUI ? "Roll: a longer tumble with a longer safety window" : "C: a longer tumble with a longer safety window")) : null;

        void PlayerTick(StoryScene s, float dt, float t)
        {
            var m = s.Move(); me.iframe -= dt; me.cd -= dt; me.st += dt; me.comboT -= dt;
            arrowT += dt; if (arrowT > 1.3f && arrows < 8) { arrowT = 0; arrows++; }
            if (me.knock != 0) { me.x += me.knock * dt; me.knock = Mathf.MoveTowards(me.knock, 0, 300 * dt); }
            bool busy = me.state == "strike" || me.state == "dash" || me.state == "roll";
            if (me.state == "dash") { me.x += me.vx * dt; me.y += me.vy * dt; if (me.st > 0.22f) me.state = "idle"; }
            else if (me.state == "roll")
            {
                // a tumble: longer than a dash, cannot be cancelled, slower lateral speed but a long i-frame window
                float decel = 1f - Mathf.Clamp01(me.st / 0.48f);
                me.x += me.vx * decel * dt; me.y += me.vy * decel * dt;
                if (me.st > 0.48f) me.state = "idle";
            }
            else if (!busy || me.state == "strike")
            {
                float sp = me.state == "strike" ? 0.35f : 1;
                me.x += m.x * 82 * sp * dt; me.y += m.y * 48 * sp * dt;
                if (Mathf.Abs(m.x) > 0.2f && me.state != "strike") me.face = Mathf.Sign(m.x);
            }
            me.x = Mathf.Clamp(me.x, lockL + 10, (goT > 0 && seg < 3 ? Mathf.Min(W, lockR + 120) : lockR) - 10); me.y = Mathf.Clamp(me.y, Y0, Y1);
            if (me.state == "strike")
            {
                if (me.st >= 0.09f && me.st - dt < 0.09f) StrikeHits(s);
                if (me.st > (me.combo == 3 ? 0.42f : 0.26f)) { me.state = "idle"; if (me.combo == 3) me.combo = 0; }
            }
            if (seg < 3)
            {
                if (s.Pressed("strike") && me.state != "dash" && me.state != "roll" && (me.state != "strike" || me.st > 0.18f))
                {
                    me.combo = me.comboT > 0 && me.combo < 3 ? me.combo + 1 : 1; me.comboT = 0.55f; me.state = "strike"; me.st = 0; Sfx.Swish();
                }
                if (s.Pressed("shoot") && me.state != "dash" && me.state != "roll")
                {
                    if (arrows > 0 && me.cd <= 0)
                    {
                        arrows--; me.cd = 0.35f; Sfx.Twang();
                        // the arrow flies along your lane, nudged towards the nearest enemy in front of you
                        var tg = foes.Where(f => !f.fled && Mathf.Sign(f.x - me.x) == me.face && Mathf.Abs(f.y - me.y) < 16).OrderBy(f => Mathf.Abs(f.x - me.x)).FirstOrDefault();
                        shots.Add(new Shot { x = me.x + me.face * 10, y = tg != null ? tg.y : me.y, vx = me.face * 230, life = 1.6f, mine = true, dmg = 1 });
                    }
                    else if (arrows == 0) Float(me.x, me.y - 44, Loc.L("Сум дууслаа", "No arrows"), Color.white);
                }
                if (s.Pressed("rally") && rally >= 1) Rally(s);
            }
            if (s.Pressed("dash") && me.state != "dash" && me.state != "roll" && me.cd <= 0.2f)
            {
                var d = m.sqrMagnitude > 0.04f ? m.normalized : new Vector2(me.face, 0);
                me.state = "dash"; me.st = 0; me.vx = d.x * 230; me.vy = d.y * 120; me.iframe = Mathf.Max(me.iframe, 0.32f); me.cd = 0.55f; Sfx.Swish();
            }
            // C key: a longer tumble with a bigger i-frame window and a bigger cooldown, cannot interrupt itself
            if (s.Pressed("roll") && me.state != "roll" && me.state != "dash" && me.cd <= 0.3f)
            {
                var d = m.sqrMagnitude > 0.04f ? m.normalized : new Vector2(me.face, 0);
                me.state = "roll"; me.st = 0; me.vx = d.x * 150; me.vy = d.y * 80; me.iframe = Mathf.Max(me.iframe, 0.5f); me.cd = 0.9f; Sfx.Pop();
            }
        }
        void StrikeHits(StoryScene s)
        {
            bool strong = me.combo == 3; float reach = strong ? 34 : 28; int hits = 0;
            foreach (var f in foes)
            {
                if (f.fled) continue;
                float dx = (f.x - me.x) * me.face;
                if (dx > -4 && dx < reach && Mathf.Abs(f.y - me.y) < 11) { Hurt(s, f, strong ? 2 : 1, strong ? 140 * me.face : 50 * me.face, strong); hits++; }
            }
            if (hits > 0) { Sfx.Hit(strong ? 0.8f : 1); s.shake = strong ? 1.2f : 0.5f; }
        }
        void Rally(StoryScene s)
        {
            rally = 0; rallyT = Time.time; Sfx.Horn(); s.shake = 1.5f;
            Say(allies[0], Loc.L("Анд минь, бид энд байна!", "My anda, we are here!"));
            foreach (var f in foes.Where(f => !f.fled).ToList()) Hurt(s, f, 3, 160, true);
        }
        public void Hurt(StoryScene s, Fighter f, int dmg, float knock, bool stagger)
        {
            if (f.fled) return;
            f.hp -= dmg; f.hitT = Time.time; f.knock = f.armored && !stagger ? knock * 0.2f : knock;
            s.Burst(f.x, f.y - (f.mounted ? 26 : 16), new Color32(255, 230, 140, 255), 6, 50, true);
            Float(f.x, f.y - (f.mounted ? 44 : 34), "-" + new string('♥', Mathf.Max(1, dmg)), Px.Col("#ffd23f"));
            rally = Mathf.Min(1, rally + 0.07f * dmg);
            // a blow interrupts what they were about to do (the captain only gives way to a strong blow)
            if ((!f.armored || stagger) && f.state != "flee") { if (f.token) { f.token = false; } f.state = "hurt"; f.st = 0; }
            if (f.hp <= 0)
            {
                f.fled = true; f.state = "flee"; f.token = false; Sfx.Good();
                Float(f.x, f.y - 50, Loc.L("Зориг нь мохлоо!", "Lost courage!"), Color.white);
                if (UnityEngine.Random.value < 0.35f || f.kind == "captain") drops.Add(new Drop { x = f.x, y = f.y, t = Time.time });
                if (f.kind == "captain") Say(f, Loc.L("Ухрацгаа!", "Fall back!"));
            }
        }
        void HurtMe(StoryScene s, int dmg, float knock)
        {
            if (me.iframe > 0 || deadT > 0 || end > 0) return;
            me.hp -= dmg; me.iframe = HURT_I; me.knock = knock; me.hitT = Time.time; me.state = "idle"; Sfx.Bad(); s.shake = 1;
            if (me.hp <= 0) { deadT = Time.time; downs++; Sfx.Thud(); }
        }
        void Retry()
        {
            deadT = -9; me.hp = me.max; me.iframe = 1.5f; arrows = 8; rally = Mathf.Max(rally, 0.5f);
            // start this part again
            foes.Clear(); shots.Clear(); spawned = 0; spawnT = 1; goT = -9; me.x = lockL + 40; me.y = 150; camX = lockL;
            Say(allies[0], Loc.L("Бос, анд минь! Дахиад хамтдаа!", "Up, my anda! Together again!"));
        }

        // ---------------------------------------------------------------- the Merkits
        int Tokens => foes.Count(f => f.token);
        void FoeTick(StoryScene s, Fighter f, float dt, float t)
        {
            f.st += dt; f.cd -= dt;
            if (f.knock != 0) { f.x += f.knock * dt; f.knock = Mathf.MoveTowards(f.knock, 0, 400 * dt); }
            float dx = me.x - f.x, dy = me.y - f.y, adx = Mathf.Abs(dx);
            switch (f.state)
            {
                case "flee":
                    f.x += (f.x < me.x ? -1 : 1) * (f.mounted ? 120 : 70) * dt; f.face = f.x < me.x ? -1 : 1;
                    if (f.x < lockL - 60 || f.x > lockR + 60 || f.x < -40 || f.x > W + 40) f.gone = true; return;
                case "hurt": if (f.st > (f.armored ? 0.5f : 0.3f)) { f.state = "idle"; f.cd = Mathf.Max(f.cd, 0.35f); } return;
                case "windup":
                    if (f.kind == "rider" || (f.kind == "captain" && f.move == "charge")) { /* holds still, the lane flashes */ }
                    else f.face = Mathf.Sign(dx);
                    if (f.kind == "archer" || (f.kind == "captain" && f.move == "volley")) f.aimY = Mathf.MoveTowards(f.aimY, me.y, 30 * dt);
                    if (f.st >= WindupTime(f)) { f.state = "attack"; f.st = 0; Attack(s, f); }
                    return;
                case "attack":
                    if (f.kind == "foot") { f.x += f.face * 150 * dt; if (f.st < 0.16f) LungeHit(s, f); if (f.st > 0.18f) Recover(f, 0.65f); }
                    else if (f.kind == "rider" || (f.kind == "captain" && f.move == "charge"))
                    {
                        f.x += f.face * 210 * dt;
                        if (Mathf.Abs(f.x - me.x) < 14 && Mathf.Abs(f.y - me.y) < 10) HurtMe(s, 1, f.face * 160);
                        if ((f.face > 0 && f.x > f.chargeTo) || (f.face < 0 && f.x < f.chargeTo)) { f.face = -f.face; Recover(f, 0.7f); }
                    }
                    else Recover(f, 0.85f);
                    return;
                case "recover": if (f.st > f.cd) { f.state = "idle"; f.cd = 0.5f + UnityEngine.Random.value * 0.5f; } return;
            }
            // idle: get into place, then ask for a turn to attack (only two at a time, and never right after another)
            float spd = f.mounted ? 46 : 32, wantX, wantY = me.y;
            if (f.kind == "foot") wantX = me.x - Mathf.Sign(dx == 0 ? 1 : dx) * 22;
            else if (f.kind == "archer") { float side = f.x < me.x ? -1 : 1; wantX = me.x + side * 120; }
            else { float side = f.x < me.x ? -1 : 1; wantX = me.x + side * 100; }
            // keep a little apart from each other
            foreach (var o in foes) if (o != f && !o.fled && Mathf.Abs(o.x - f.x) < 14 && Mathf.Abs(o.y - f.y) < 8) wantY += (f.y < o.y ? -10 : 10);
            float mx = wantX - f.x, my = wantY - f.y;
            if (Mathf.Abs(mx) > 3) f.x += Mathf.Sign(mx) * Mathf.Min(Mathf.Abs(mx), spd * dt);
            if (Mathf.Abs(my) > 2) f.y += Mathf.Sign(my) * Mathf.Min(Mathf.Abs(my), spd * 0.6f * dt);
            f.y = Mathf.Clamp(f.y, Y0, Y1); f.x = Mathf.Clamp(f.x, lockL - 30, lockR + 30);
            if (Mathf.Abs(dx) > 2) f.face = Mathf.Sign(dx);
            bool inPlace = f.kind == "foot" ? adx < 30 && Mathf.Abs(dy) < 8 : f.kind == "archer" ? adx > 70 && Mathf.Abs(dy) < 20 : adx > 60 && Mathf.Abs(dy) < 12;
            if (f.kind == "captain") inPlace = adx > 50 && Mathf.Abs(dy) < 14;
            if (inPlace && f.cd <= 0 && Tokens < 2 && f.x > lockL - 4 && f.x < lockR + 4)
            {
                f.token = true; f.state = "windup"; f.st = 0; f.aimY = f.y; Sfx.Pop();
                if (f.kind == "captain") f.move = f.hp <= 8 && foes.Count(o => o.kind == "foot" && !o.fled) == 0 && UnityEngine.Random.value < 0.4f ? "call" : UnityEngine.Random.value < 0.5f ? "charge" : "volley";
                if (f.kind == "rider" || f.move == "charge") { f.face = Mathf.Sign(dx); f.chargeTo = me.x + f.face * 90; }
            }
        }
        float WindupTime(Fighter f) => f.kind == "foot" ? 0.55f : f.kind == "archer" ? 0.75f : f.kind == "rider" ? 0.8f : 0.95f;
        void Recover(Fighter f, float time) { f.state = "recover"; f.st = 0; f.cd = time; f.token = false; }
        void Attack(StoryScene s, Fighter f)
        {
            if (f.kind == "foot") Sfx.Swish();
            if (f.kind == "archer") { shots.Add(new Shot { x = f.x + f.face * 8, y = f.aimY, vx = f.face * 150, life = 2.6f, dmg = 1 }); Sfx.Twang(); }
            if (f.kind == "rider" || f.move == "charge") Sfx.Thud();
            if (f.kind == "captain" && f.move == "volley") { foreach (var oy in new[] { -12f, 0, 12f }) shots.Add(new Shot { x = f.x + f.face * 10, y = Mathf.Clamp(f.aimY + oy, Y0, Y1), vx = f.face * 140, life = 2.8f, dmg = 1 }); Sfx.Twang(); }
            if (f.kind == "captain" && f.move == "call")
            {
                for (int i = 0; i < 2; i++) foes.Add(new Fighter { kind = "foot", look = new Look { deel = "black", hat = "fur", skin = "#D9A06E" }, x = lockR + 20 + i * 14, y = Y0 + 10 + i * 24, face = -1, hp = 3, max = 3, cd = 1 });
                Say(f, Loc.L("Тусламж ир!", "To me!"));
            }
        }
        void LungeHit(StoryScene s, Fighter f)
        {
            float dx = (me.x - f.x) * f.face;
            if (dx > -2 && dx < 20 && Mathf.Abs(me.y - f.y) < 9) HurtMe(s, 1, f.face * 120);
        }

        // ---------------------------------------------------------------- the allies: Jamukha and Toghrul's Kereit rider
        void AllyTick(StoryScene s, Fighter a, float dt, float t)
        {
            a.cd -= dt; a.st += dt;
            var tg = foes.Where(f => !f.fled).OrderBy(f => Mathf.Abs(f.x - a.x) + Mathf.Abs(f.y - a.y) * 2).FirstOrDefault();
            float wantX, wantY;
            if (tg == null) { wantX = me.x - 30 - (a.name == "kereit" ? 10 : 0); wantY = Mathf.Clamp(me.y + (a.name == "kereit" ? 16 : -16), Y0, Y1); }
            else { wantX = tg.x - Mathf.Sign(tg.x - a.x == 0 ? 1 : tg.x - a.x) * 24; wantY = tg.y; }
            if (Time.time - rallyT < 1.4f) { wantX = lockR + 40; wantY = a.y; }   // the charge sweeps across the field
            float mx = wantX - a.x, my = wantY - a.y, sp = Time.time - rallyT < 1.4f ? 260 : 64;
            if (Mathf.Abs(mx) > 3) { a.x += Mathf.Sign(mx) * Mathf.Min(Mathf.Abs(mx), sp * dt); a.face = Mathf.Sign(mx); }
            if (Mathf.Abs(my) > 2) a.y += Mathf.Sign(my) * Mathf.Min(Mathf.Abs(my), 40 * dt);
            if (Time.time - rallyT > 1.4f && rallyT > 0 && a.x > lockR) a.x = lockL - 20;
            if (tg != null && a.cd <= 0 && Mathf.Abs(tg.x - a.x) < 30 && Mathf.Abs(tg.y - a.y) < 10)
            {
                a.cd = 2.4f + UnityEngine.Random.value; a.state = "strike"; a.st = 0; a.face = Mathf.Sign(tg.x - a.x);
                Hurt(s, tg, 1, a.face * 60, false); Sfx.Hit(1.2f);
            }
            if (a.state == "strike" && a.st > 0.25f) a.state = "idle";
        }
        void ShotsTick(StoryScene s, float dt)
        {
            foreach (var sh in shots)
            {
                sh.x += sh.vx * dt; sh.life -= dt;
                if (sh.mine) { foreach (var f in foes) if (!f.fled && Mathf.Abs(f.x - sh.x) < 9 && Mathf.Abs(f.y - sh.y) < 8) { Hurt(s, f, sh.dmg, Mathf.Sign(sh.vx) * 40, false); sh.life = 0; Sfx.Hit(1.3f); break; } }
                else if (Mathf.Abs(me.x - sh.x) < 9 && Mathf.Abs(me.y - sh.y) < 7 && me.iframe <= 0) { HurtMe(s, sh.dmg, Mathf.Sign(sh.vx) * 80); sh.life = 0; }
            }
            shots.RemoveAll(sh => sh.life <= 0);
        }

        // ---------------------------------------------------------------- finding Börte among the fleeing camp
        void FindBorte(StoryScene s, float dt, float t)
        {
            foreach (var f in folk) { f.x += f.vx * dt; f.face = -1; }
            if (cart != null && borte == null) cart.x = Mathf.Max(lockL + 60, cart.x + cart.vx * dt);
            if (borte == null && s.Pressed("call") && t - callT > 1.2f)
            {
                callT = t; Say(me, Loc.L("Бөртэ! Бөртэ!", "Börte! Börte!"));
                if (cart != null && Mathf.Abs(cart.x - me.x) < 80)
                {
                    // she knows his voice, jumps down from the cart and runs to him
                    borte = new Fighter { kind = "borte", look = new Look { deel = "red", hat = "flower", skin = "#F6D3B3" }, x = cart.x, y = cart.y + 6 };
                    Sfx.Chime();
                }
                else Float(me.x, me.y - 52, Loc.L("Хариу алга... Тэргэнүүд рүү ойрт", "No answer... ride closer to the carts"), Color.white);
            }
            if (borte != null && !found)
            {
                float dx = me.x - borte.x, dy = me.y - borte.y, d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d > 16) { borte.x += dx / d * 52 * dt; borte.y += dy / d * 52 * dt; borte.face = Mathf.Sign(dx); }
                else { found = true; end = t; Sfx.Win(); Say(borte, Loc.L("Тэмүжин!", "Temüjin!")); }
            }
        }

        // ---------------------------------------------------------------- drawing
        public override void Draw(StoryScene s, PxCanvas c)
        {
            float t = Time.time; var all = new List<(float y, Action draw)>();
            foreach (var d in drops) { var dd = d; all.Add((d.y, () => { int dx = Mathf.RoundToInt(dd.x - camX), dy = Mathf.RoundToInt(dd.y - 16 + Mathf.Sin(t * 4) * 2); c.Blit(Px.DairyIcon("aaruul"), dx - 8, dy); if ((int)(t * 4) % 2 == 0) { var gl = new Color32(255, 240, 160, 255); c.Put(dx, dy - 3, gl); c.Put(dx - 1, dy - 2, gl); c.Put(dx + 1, dy - 2, gl); c.Put(dx, dy - 1, gl); } })); }   // dried curd: a heart back
            if (cart != null) { var cc = cart; all.Add((cart.y, () => DrawCart(c, cc.x - camX, cc.y, t))); }
            foreach (var f in folk) { var ff = f; all.Add((f.y, () => StoryScene.DrawPerson(c, ff.look, ff.x - camX, ff.y, 1, true, false, t, true))); }
            if (borte != null) { var b = borte; all.Add((b.y, () => StoryScene.DrawPerson(c, b.look, b.x - camX, b.y, b.Face, !found, false, t, true))); }
            foreach (var f in foes) { var ff = f; all.Add((f.y, () => DrawFighter(s, c, ff, t))); }
            foreach (var a in allies) { var aa = a; all.Add((a.y, () => DrawFighter(s, c, aa, t))); }
            all.Add((me.y, () => DrawFighter(s, c, me, t)));
            foreach (var (_, draw) in all.OrderBy(e => e.y)) draw();
            foreach (var sh in shots) DrawArrow(c, sh.x - camX, sh.y - 20, Mathf.Sign(sh.vx), sh.mine);
            // telegraphs over everything, so they are never hidden
            foreach (var f in foes.Where(f => f.state == "windup"))
            {
                float k = Mathf.Clamp01(f.st / WindupTime(f)); bool on = (int)(t * (8 + k * 10)) % 2 == 0;
                int fx = Mathf.RoundToInt(f.x - camX), fy = Mathf.RoundToInt(f.y - (f.mounted ? 46 : 36));
                if (on) Bang(c, fx, fy);
                if (f.kind == "archer" || f.move == "volley") for (int x = 0; x < 150; x += 4) { var col = new Color32(255, 70, 60, (byte)(70 + 120 * k)); c.Put(Mathf.RoundToInt(f.x - camX + f.face * (10 + x)), Mathf.RoundToInt(f.aimY - 20), col); if (f.move == "volley") { c.Put(Mathf.RoundToInt(f.x - camX + f.face * (10 + x)), Mathf.RoundToInt(f.aimY - 32), col); c.Put(Mathf.RoundToInt(f.x - camX + f.face * (10 + x)), Mathf.RoundToInt(f.aimY - 8), col); } }
                if (f.kind == "rider" || f.move == "charge") for (int x = 0; x < Mathf.Abs(f.chargeTo - f.x); x += 3) { c.Put(Mathf.RoundToInt(f.x - camX + f.face * x), Mathf.RoundToInt(f.y + 1), new Color32(255, 60, 50, (byte)(60 + 140 * k))); c.Put(Mathf.RoundToInt(f.x - camX + f.face * x), Mathf.RoundToInt(f.y - 1), new Color32(255, 60, 50, (byte)(40 + 100 * k))); }
            }
            if (goT > 0 && seg < 3 && (int)(t * 3) % 2 == 0) { int ax = StoryScene.VW - 14, ay = 92; for (int i = 0; i < 6; i++) { c.Put(ax - i, ay - i, Color.white); c.Put(ax - i, ay + i, Color.white); c.Put(ax - i - 4, ay - i, Color.white); c.Put(ax - i - 4, ay + i, Color.white); } }   // » ride on
        }
        void DrawFighter(StoryScene s, PxCanvas c, Fighter f, float t)
        {
            bool blink = (t - f.hitT < 0.3f && (int)(t * 30) % 2 == 0) || (f == me && me.iframe > 0 && me.iframe < HURT_I && (int)(t * 16) % 2 == 0);
            bool moving = f.state == "attack" || f.state == "flee" || f.state == "dash" || f.state == "roll" || f.knock != 0 || (f.state == "idle" && f.kind != "archer");
            if (f == me) moving = s.Move().sqrMagnitude > 0.02f || f.state == "dash" || f.state == "roll";
            float x = f.x - camX, y = f.y;
            // the roll draws a bit closer to the ground: a short vertical tuck during the tumble sells the motion without new art
            if (f.state == "roll") y -= Mathf.Sin(Mathf.Clamp01(f.st / 0.48f) * Mathf.PI) * 3f;
            if (f.state == "flee") c.Alpha = 0.75f;
            StoryScene.DrawPerson(c, f.look, x, y, f.Face, moving, f.mounted, t, f.state == "attack" || f.state == "dash" || f.state == "roll", blink);
            c.Alpha = 1;
            if (blink) return;
            int hy = Mathf.RoundToInt(y - (f.mounted ? 30 : 18)), hx = Mathf.RoundToInt(x + f.face * 6);
            // weapons: a saber swung in an arc, a spear thrust, a drawn bow
            if (f.state == "strike")
            {
                float k = Mathf.Clamp01(f.st / 0.12f); int r = f == me && me.combo == 3 ? 16 : 12;
                for (int i = 0; i <= 10; i++) { float a = Mathf.Lerp(-1.3f, 0.9f, i / 10f * k); int px = Mathf.RoundToInt(hx + f.face * Mathf.Cos(a) * r), py = Mathf.RoundToInt(hy + Mathf.Sin(a) * r); c.Put(px, py, new Color32(255, 250, 220, (byte)(120 + i * 13))); c.Put(px, py + 1, new Color32(255, 220, 120, 90)); }
            }
            else if (f.kind == "foot") { int len = f.state == "attack" ? 18 : f.state == "windup" ? 8 : 12; for (int i = 0; i < len; i++) c.Put(hx + Mathf.RoundToInt(f.face * i), hy + 4 - (f.state == "windup" ? 2 : 0), new Color32(120, 90, 60, 255)); c.Put(hx + Mathf.RoundToInt(f.face * len), hy + 4, new Color32(220, 220, 230, 255)); }
            else if (f.kind == "archer" || f.mounted && f != me && f.kind != "ally")
            {
                for (int i = -5; i <= 5; i++) c.Put(hx + Mathf.RoundToInt(f.face * (2 + Mathf.Abs(i) * 0.25f * (f.state == "windup" ? -1 : 1))), hy + i, new Color32(110, 70, 40, 255));
            }
            // courage hearts over the Merkits
            if (f.kind != "me" && f.kind != "ally" && !f.fled && f.max > 0)
            {
                int n = Mathf.Min(f.max, 8), w = n * 3; int bx = Mathf.RoundToInt(x - w / 2f), by = Mathf.RoundToInt(y - (f.mounted ? 42 : 32));
                if (f.max > 8) { for (int i = 0; i < 24; i++) { c.Put(bx - 8 + i, by, new Color32(40, 20, 20, 200)); if (i < 24 * f.hp / f.max) c.Put(bx - 8 + i, by, new Color32(230, 70, 60, 255)); } }
                else for (int i = 0; i < n; i++) { var col = i < f.hp ? new Color32(230, 70, 60, 255) : new Color32(60, 40, 40, 160); c.Put(bx + i * 3, by, col); c.Put(bx + i * 3 + 1, by, col); }
            }
        }
        static void Bang(PxCanvas c, int x, int y)
        {
            var red = new Color32(255, 60, 50, 255); var w = new Color32(255, 255, 255, 255);
            for (int i = -1; i <= 1; i++) for (int j = 0; j < 7; j++) c.Put(x + i, y + j - 4, j == 5 ? default : red);
            c.Put(x, y + 1, w); c.Put(x, y - 3, w);
        }
        static void DrawArrow(PxCanvas c, float x, float y, float dir, bool mine)
        {
            int X = Mathf.RoundToInt(x), Y = Mathf.RoundToInt(y);
            for (int i = 0; i < 9; i++) c.Put(X - Mathf.RoundToInt(dir * i), Y, new Color32(90, 60, 30, 255));
            c.Put(X + Mathf.RoundToInt(dir), Y, mine ? new Color32(255, 214, 80, 255) : new Color32(240, 80, 60, 255));
            c.Put(X - Mathf.RoundToInt(dir * 8), Y - 1, new Color32(240, 240, 240, 255)); c.Put(X - Mathf.RoundToInt(dir * 8), Y + 1, new Color32(240, 240, 240, 255));
        }
        static void DrawCart(PxCanvas c, float x, float y, float t)
        {
            int X = Mathf.RoundToInt(x), Y = Mathf.RoundToInt(y);
            Px.R(c, X - 12, Y - 12, 24, 6, "#5a3f2a"); Px.R(c, X - 12, Y - 12, 24, 1, "#7a5a3a"); Px.R(c, X - 10, Y - 20, 18, 8, "#d9cdb4"); Px.R(c, X - 10, Y - 20, 18, 1, "#f0e6d0");
            int sp = (int)(t * 6) % 4; Px.Ell(c, X - 6, Y - 4, 4, 4, "#3a2516"); Px.P(c, X - 6 + (sp < 2 ? 1 : -1), Y - 4, "#7a5a3a");
            Px.Line(c, X + 12, Y - 8, X + 22, Y - 6, "#5a3f2a");
        }
        void Say(Fighter f, string text) { f.line = text; f.lineT = Time.time; }
        void Float(float x, float y, string text, Color col) => floats.Add(new Floater { x = x, y = y, text = text, t = Time.time, col = col });

        public override void Hud(StoryScene s, float vw, float vh)
        {
            float U = g.UiScale, t = Time.time;
            // hearts, arrows and the allies' horn
            for (int i = 0; i < me.max; i++) GUI.Label(g.URect(60 + i * 24, 10, 24, 26), i < me.hp ? "<color=#ff5a4a>♥</color>" : "<color=#5a4040>♥</color>", new GUIStyle(g.sPixel) { fontSize = PxSize(U * 22), alignment = TextAnchor.MiddleCenter });
            GUI.Label(g.URect(60, 38, 200, 20), Loc.L("Сум ", "Arrows ") + arrows + "/8", new GUIStyle(g.sPixel) { fontSize = PxSize(U * 13), alignment = TextAnchor.MiddleLeft, normal = { textColor = Color.white } });
            if (seg < 3)
            {
                var hr = g.URect(60, 58, 150, 12); g.Box(hr, new Color(0, 0, 0, 0.6f)); g.Box(new Rect(hr.x + U, hr.y + U, (hr.width - 2 * U) * rally, hr.height - 2 * U), rally >= 1 && (int)(t * 4) % 2 == 0 ? Px.Col("#fff2a8") : Px.Col("#e0b04a"));
                GUI.Label(g.URect(214, 54, 300, 20), rally >= 1 ? Loc.L(TouchUI ? "“Холбоо” — бүрээ дуудах!" : "T — холбоотнуудаа дууд!", TouchUI ? "Allies — blow the horn!" : "T — call your allies!") : Loc.L("Холбоотнуудын бүрээ", "Allies' horn"), new GUIStyle(g.sPixel) { fontSize = PxSize(U * 12), alignment = TextAnchor.MiddleLeft, normal = { textColor = Color.white } });
            }
            // what to do now
            string goal = seg == 3 ? (borte == null ? Loc.L("Зугтаж буй хүмүүсийн дундаас Бөртийг ол: тэргэнд ойртоод “Дуудах”", "Find Börte among the fleeing people: ride near the carts and Call") : Loc.L("Бөртэ!", "Börte!"))
                : goT > 0 ? Loc.L("Цааш давхи →", "Ride on →")
                : seg == 2 ? Loc.L($"Мэргидийн ноён Хаатай Дармала", "The Merkit noble Qa'atai Darmala") : Loc.L($"Мэргидийн дайчдын зоригийг мохоо: {foes.Count(f => f.fled) + (spawned - foes.Count)}/{WaveTotal}", $"Break the Merkits' courage: {foes.Count(f => f.fled) + (spawned - foes.Count)}/{WaveTotal}");
            var gr = g.URect(vw / 2 - 210, 10, 420, 34); GUI.Box(gr, goal, new GUIStyle(g.sNote) { fontSize = g.Ts(12) });
            var hint = Hint; if (hint != null) GUI.Box(g.URect(vw / 2 - 210, vh - 54, 420, 34), hint, new GUIStyle(g.sNote) { fontSize = g.Ts(12) });
            if (deadT > 0) GUI.Box(g.URect(vw / 2 - 200, vh / 2 - 30, 400, 60), Loc.L("Тэмүжин мориноосоо унав... Дахиад!", "Temüjin was thrown from his horse... Again!"), new GUIStyle(g.sNote) { fontSize = g.Ts(14) });
            GUI.EndGroup();
            // floating words and speech, at their place in the picture
            foreach (var fl in floats) { var p = s.ToScreen(fl.x - camX, fl.y - (t - fl.t) * 18); g.Outlined(p.x, p.y, fl.text, g.U(13), fl.col, Mathf.Clamp01(1.4f - (t - fl.t))); }
            foreach (var f in foes.Concat(allies).Append(me).Concat(borte != null ? new[] { borte } : new Fighter[0]))
                if (f.line != null && t - f.lineT < 2.6f) s.SayOver(g, f.x, f.y - (f.mounted ? 46 : 34), f.line);
            var safe = Screen.safeArea; GUI.BeginGroup(new Rect(safe.x, Screen.height - safe.yMax, safe.width, safe.height));
        }
    }
}
