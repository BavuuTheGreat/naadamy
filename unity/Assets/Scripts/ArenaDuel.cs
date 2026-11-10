// The 5-second duel with a friend in the online arena (server/arena.js): two wrestlers in a room with a 5-letter code play
// the same skill game, chosen by the server. Each device runs the game and reports how well it went (0 to 1); the server
// adds a little for being quick and names the winner. A duel never changes a rating.
using System.Globalization;
using UnityEngine;

public sealed partial class Game
{
    public sealed class ArenaDuelModal : Modal
    {
        static readonly string[] ARROWS = { "←", "→", "↑", "↓" };
        const int MASH_FULL = 60;   // taps in 5 seconds that score full marks
        const float MARKER_SPEED = 3.2f, HOLD_SECONDS = 1.6f, HOLD_CENTER = 0.78f, HOLD_WIDTH = 0.26f;

        readonly string id, skill, oppName; readonly int side, seed; readonly int[] arrows; readonly float goAt, limit;
        float marker, holdValue, needle, needleSpeed, inMiddle; int arrowAt, arrowErrors, taps, needleDir, holdFrame = -9; bool held, sent;
        bool ended, roomOpen = true, askedAgain, oppAgain, cut; int winner = -2, myScore, oppScore, coins, xp; string reason = "";
        /** A new duel took this one's place, so closing it must not close the room. */
        public bool Replaced;
        public bool Over => ended;

        public override float Width => 560;
        public override bool CanEscape => false;
        public override (string who, string face)? Speaker => ("zasuul", "proud");

        public ArenaDuelModal(J m)
        {
            id = m["id"].S; side = m["side"].I == 1 ? 1 : 0; skill = m["skill"].S ?? "timing"; seed = m["seed"].I; oppName = Clean(m["opp"]["u"].S);
            var list = new System.Collections.Generic.List<int>(); foreach (var a in m["arrows"]) list.Add(Mathf.Clamp(a.I, 0, ARROWS.Length - 1));
            arrows = list.ToArray();
            limit = Mathf.Max(1, m["ms"].I) / 1000f; goAt = Time.realtimeSinceStartup + m["lead"].I / 1000f;
        }
        float Elapsed => Time.realtimeSinceStartup - goAt;
        bool Playing => !ended && !sent && Elapsed >= 0;

        // ---------------------------------------------------------------- from the server
        public void OnEnd(Game g, J m)
        {
            if (m["id"].S != id || ended) return;
            ended = true; winner = m["winner"].I; reason = m["reason"].S ?? ""; coins = m["coins"].I; xp = m["xp"].I;
            myScore = m["scores"][side].I; oppScore = m["scores"][1 - side].I;
            if (winner == side) { Sfx.Win(); g.Confetti(60); } else if (winner >= 0) Sfx.Bad();
        }
        public void OnAgain() { oppAgain = true; Sfx.Pop(); }
        public void OnRoomClosed() { roomOpen = false; }
        /** The connection dropped: the server has given this duel up, and no result will come. */
        public void OnLost() { roomOpen = false; if (!ended) { ended = true; cut = true; } }

        // ---------------------------------------------------------------- the skill game
        void Send(Game g, float q)
        {
            if (sent) return;
            sent = true; Sfx.Tap();
            g.net.Send("{\"t\":\"ar-duel-score\",\"q\":" + Mathf.Clamp01(q).ToString("0.###", CultureInfo.InvariantCulture) + "}");
        }
        void Arrow(Game g, int i)
        {
            if (!Playing || skill != "combo") return;
            if (arrows[arrowAt] != i) { arrowErrors++; Sfx.Bad(); return; }
            arrowAt++; Sfx.Tap();
            if (arrowAt >= arrows.Length) Send(g, Mathf.Max(0.05f, 1 - 0.15f * arrowErrors));
        }
        void Mash() { if (Playing && skill == "mash") { taps++; Sfx.Tap(); } }
        void HitMarker(Game g) { if (Playing && skill == "timing") Send(g, 1 - Mathf.Abs(marker - 0.5f) * 2.4f); }

        public override void Tick(Game g, float dt)
        {
            if (!Playing) return;
            float el = Elapsed;
            if (skill == "timing") marker = (Mathf.Sin(el * MARKER_SPEED - Mathf.PI / 2) + 1) / 2;
            else if (skill == "hold")
            {
                bool pressing = Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.E) || Time.frameCount - holdFrame <= 2;
                if (pressing && !held) { held = true; holdValue = 0; }
                else if (!pressing && held) { held = false; Send(g, 1 - Mathf.Abs(holdValue - HOLD_CENTER) * 3.2f); return; }
                if (held) { holdValue += dt / HOLD_SECONDS; if (holdValue > 1) holdValue = 0; }
            }
            else if (skill == "balance")
            {
                int keys = (Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A) ? -1 : 0) + (Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D) ? 1 : 0);
                int dir = keys != 0 ? keys : needleDir; needleDir = 0;
                float push = Mathf.Sin(el * 1.7f + seed) * 2.4f + Mathf.Sin(el * 3.1f + seed * 2) * 1.2f;   // the same shoves for both wrestlers
                needleSpeed = (needleSpeed + (push + dir * 4.6f) * dt) * Mathf.Pow(0.92f, dt * 60);
                needle = Mathf.Clamp(needle + needleSpeed * dt, -1, 1);
                if (Mathf.Abs(needle) < 0.25f) inMiddle += dt;
            }
            if (el < limit) return;
            if (skill == "mash") Send(g, taps / (float)MASH_FULL);
            else if (skill == "balance") Send(g, inMiddle / limit * 1.15f);
            else if (skill == "combo") Send(g, arrowAt / (float)arrows.Length * 0.5f);
            else Send(g, 0);
        }
        public override void Key(Game g)
        {
            bool space = Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.E);
            if (space) { HitMarker(g); Mash(); }
            if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A)) Arrow(g, 0);
            if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) Arrow(g, 1);
            if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W)) Arrow(g, 2);
            if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S)) Arrow(g, 3);
        }
        public override void Press(Game g) { }
        public override void Closed(Game g)
        {
            if (g.arena.duel == this) g.arena.duel = null;
            if (Replaced) return;
            if (!ended) g.net.Send("{\"t\":\"ar-quit\"}");
            else if (roomOpen) g.net.Send("{\"t\":\"ar-room-close\"}");
        }

        // ---------------------------------------------------------------- every frame
        public override void Draw(Game g)
        {
            g.Title(Loc.L($"Найзын тэмцээн · {oppName}", $"Friend duel · {oppName}"));
            if (ended) { Results(g); return; }
            var kid = SKILL_KID.TryGetValue(skill, out var found) ? found : SKILL_KID["timing"];
            GUILayout.Label($"<b>{kid.name}</b>", new GUIStyle(g.sTitle) { alignment = TextAnchor.MiddleCenter });
            float el = Elapsed;
            if (el < 0)
            {
                g.Text(kid.how.ToString());
                GUILayout.Label(Mathf.CeilToInt(-el).ToString(), new GUIStyle(g.sBig) { alignment = TextAnchor.MiddleCenter, fontSize = g.Ts(48) });
            }
            else if (sent) g.Text(Loc.L($"Дууслаа! {oppName}-г хүлээж байна…", $"Done! Waiting for {oppName}…"));
            else
            {
                g.Bar(GUILayoutUtility.GetRect(10, g.U(10), GUILayout.ExpandWidth(true)), Mathf.Clamp01(1 - el / limit), Px.Col("#f7c646"));
                g.Gap(4);
                Game_(g);
            }
            g.Gap(4);
            if (g.Btn(Loc.L("Бууж өгөх", "Give up"), false, 30)) g.Later(g.Close);
        }
        void Game_(Game g)
        {
            Rect Meter() => GUILayoutUtility.GetRect(10, g.U(22), GUILayout.ExpandWidth(true));
            Rect Inside(Rect r, string back) { g.Box(r, Px.Col("#2b1e2e")); var inner = new Rect(r.x + g.U(2), r.y + g.U(2), r.width - g.U(4), r.height - g.U(4)); g.Box(inner, Px.Col(back)); return inner; }
            switch (skill)
            {
                case "timing":
                {
                    var r = Meter(); var inner = Inside(r, "#e6c48a");
                    g.Box(new Rect(inner.x + 0.32f * inner.width, inner.y, 0.36f * inner.width, inner.height), Px.Col("#5c9a36"));
                    g.Box(new Rect(inner.x + marker * inner.width - g.U(2), r.y - g.U(3), g.U(4), r.height + g.U(6)), Px.Col("#f7c646"));
                    if (g.Btn(Loc.L("Хүч! (Space)", "Power! (Space)"), true, 56)) g.Later(() => HitMarker(g));
                    break;
                }
                case "hold":
                {
                    var inner = Inside(Meter(), "#5a4a5e");
                    g.Box(new Rect(inner.x + (HOLD_CENTER - HOLD_WIDTH / 2) * inner.width, inner.y, HOLD_WIDTH * inner.width, inner.height), Px.Col("#f7c646"));
                    g.Box(new Rect(inner.x, inner.y + inner.height * 0.3f, holdValue * inner.width, inner.height * 0.4f), Px.Col("#3f76db"));
                    if (GUILayout.RepeatButton(Loc.L("Дарж байгаад шар дээр тавь (Space)", "Hold, let go on the yellow (Space)"), g.sPrimary, GUILayout.MinHeight(g.U(56)))) holdFrame = Time.frameCount;
                    break;
                }
                case "combo":
                    GUILayout.BeginHorizontal(); GUILayout.FlexibleSpace();
                    for (int i = 0; i < arrows.Length; i++) GUILayout.Label(i < arrowAt ? $"<color=#2ea35f>{ARROWS[arrows[i]]}</color>" : i == arrowAt ? $"<color=#e14b3d>{ARROWS[arrows[i]]}</color>" : ARROWS[arrows[i]], new GUIStyle(g.sBig) { fontSize = g.Ts(26) }, GUILayout.Width(g.U(40)));
                    GUILayout.FlexibleSpace(); GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                    for (int i = 0; i < ARROWS.Length; i++) if (GUILayout.Button(ARROWS[i], g.sBtn, GUILayout.MinHeight(g.U(52)))) { int k = i; g.Later(() => Arrow(g, k)); }
                    GUILayout.EndHorizontal();
                    break;
                case "mash":
                    if (g.Btn(Loc.L($"Дар! {taps}", $"Tap! {taps}") + " (Space)", true, 72)) g.Later(Mash);
                    break;
                case "balance":
                {
                    var r = Meter(); var inner = Inside(r, "#e6c48a");
                    g.Box(new Rect(inner.x + 0.375f * inner.width, inner.y, 0.25f * inner.width, inner.height), Px.Col("#5c9a36"));
                    g.Box(new Rect(inner.x + (0.5f + needle * 0.48f) * inner.width - g.U(2), r.y - g.U(3), g.U(4), r.height + g.U(6)), Px.Col("#2b1e2e"));
                    GUILayout.BeginHorizontal();
                    if (GUILayout.RepeatButton("◄", g.sBtn, GUILayout.MinHeight(g.U(56)))) needleDir = -1;
                    if (GUILayout.RepeatButton("►", g.sBtn, GUILayout.MinHeight(g.U(56)))) needleDir = 1;
                    GUILayout.EndHorizontal();
                    break;
                }
            }
        }
        void Results(Game g)
        {
            bool won = winner == side, draw = winner == -1;
            string head = cut ? Loc.L("Холболт тасарлаа", "Connection lost") : draw ? Loc.L("ТЭНЦЛЭЭ", "DRAW") : won ? Loc.L("ЯЛАЛТ!", "VICTORY!") : Loc.L("ЯЛАГДАЛ", "DEFEAT");
            GUILayout.Label(head, new GUIStyle(g.sBig) { alignment = TextAnchor.MiddleCenter, fontSize = g.Ts(30), normal = { textColor = won ? Px.Col("#f7c646") : draw || cut ? Color.white : Px.Col("#e14b3d") } });
            if (reason == "quit" || reason == "left") g.Text(won ? Loc.L($"{oppName} тэмцээнээс гарлаа.", $"{oppName} left the duel.") : Loc.L("Та тэмцээнээс гарлаа.", "You left the duel."));
            else if (!cut) g.Text(Loc.L($"Та <b>{myScore}</b> : <b>{oppScore}</b> {oppName}", $"You <b>{myScore}</b> : <b>{oppScore}</b> {oppName}"));
            if (!cut) g.Text($"+{xp} XP" + (coins > 0 ? $" · +{coins} " + Loc.L("зоос", "coins") : ""));
            if (oppAgain && roomOpen && !askedAgain) g.Small("<color=#f7c646>" + Loc.L($"{oppName} дахин тоглохыг хүсэж байна!", $"{oppName} wants to play again!") + "</color>");
            if (!roomOpen && !cut) g.Small(Loc.L("Өрөө хаагдлаа.", "The room has closed."));
            GUILayout.BeginHorizontal();
            if (g.Btn(Loc.L("Өргөө рүү", "Back to arena"), false, 44)) g.Later(() => g.Swap(new ArenaHubModal()));
            if (roomOpen && g.Btn(askedAgain ? Loc.L("Хүлээж байна…", "Waiting…") : Loc.L("Дахин", "Play again"), true, 44, !askedAgain)) g.Later(() => { askedAgain = true; g.net.Send("{\"t\":\"ar-duel-again\"}"); });
            GUILayout.EndHorizontal();
        }
    }
}
