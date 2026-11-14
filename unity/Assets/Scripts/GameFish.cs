// Fishing from the dock: keep the fish inside the green zone by holding to lift it, until the catch meter fills.
using UnityEngine;

public sealed partial class Game
{
    sealed class FishingModal : Modal
    {
        readonly string key; readonly Rules.Fish f; readonly float zoneH, speed;
        float zone, vel, fish = 0.5f, target = 0.5f, retarget = 1, prog = 0.4f, grace = 1.2f; int holdFrame = -9;
        string state = "play"; // play | won | lost
        public override (string who, string face)? Speaker => ("fisher", state == "won" ? "laugh" : state == "lost" ? "worried" : "curious");
        public FishingModal(Game g)
        {
            var keys = new System.Collections.Generic.List<string>(Rules.FISH.Keys);
            float total = 0; var w = new float[keys.Count];
            for (int i = 0; i < keys.Count; i++) { var q = Rules.FISH[keys[i]]; w[i] = q.w * (g.V.rod > 1 && q.diff >= 3 ? 1.8f : 1); total += w[i]; }
            float r = Random.value * total; key = keys[0];
            for (int i = 0; i < keys.Count; i++) { r -= w[i]; if (r <= 0) { key = keys[i]; break; } }
            f = Rules.FISH[key]; zoneH = g.V.rod > 1 ? 0.32f : 0.25f; speed = new[] { 0, 0.32f, 0.46f, 0.6f, 0.75f }[f.diff];
            zone = 0.5f - zoneH / 2;
        }
        public override void Tick(Game g, float dt)
        {
            if (state != "play") return;
            bool holding = Input.GetKey(KeyCode.Space) || Time.frameCount - holdFrame <= 2;
            vel = Mathf.Clamp(vel + (holding ? -2.2f : 1.5f) * dt, -0.95f, 0.95f); zone += vel * dt;
            if (zone < 0) { zone = 0; vel = Mathf.Abs(vel) * 0.3f; }
            if (zone > 1 - zoneH) { zone = 1 - zoneH; vel = -Mathf.Abs(vel) * 0.3f; }
            retarget -= dt; if (retarget <= 0) { target = 0.06f + Random.value * 0.88f; retarget = 0.45f + Random.value * (1.5f - f.diff * 0.22f); }
            float d = target - fish; fish += Mathf.Sign(d) * Mathf.Min(Mathf.Abs(d), speed * dt);
            bool inside = fish >= zone && fish <= zone + zoneH;
            grace -= dt;
            prog = Mathf.Clamp01(prog + (inside ? 0.28f : grace > 0 ? 0 : -0.13f) * dt);
            if (prog >= 1) End(g, true); else if (prog <= 0) End(g, false);
        }
        void End(Game g, bool win)
        {
            state = win ? "won" : "lost";
            if (!win) { Sfx.Bad(); return; }
            var V = g.V; g.Quest("fish"); Sfx.Win(); V.fishCaught++;
            if (f.release) g.Reward(f.coins, f.xp); else { SaveData.Add(V.fish, key, 1); g.Reward(0, f.xp); }
            V.Save();
        }
        public override void Draw(Game g)
        {
            if (state == "lost")
            {
                g.Title(Loc.L("Загас зугтлаа!", "The fish got away!")); g.Gap();
                GUILayout.BeginHorizontal(); if (g.Btn(Loc.L("Хаах", "Close"))) g.Later(g.Close); if (g.Btn(Loc.L("Дахин", "Again"), true)) g.Later(() => g.Swap(new FishingModal(g))); GUILayout.EndHorizontal();
                return;
            }
            if (state == "won")
            {
                g.Picture(Px.Icon("fish:" + key), 72);
                GUILayout.Label(Loc.L("Барьлаа!", "Caught!"), new GUIStyle(g.sSmall) { alignment = TextAnchor.MiddleCenter });
                GUILayout.Label(f.name.ToString(), new GUIStyle(g.sTitle) { alignment = TextAnchor.MiddleCenter });
                g.Fact(f.fact.ToString());
                g.Small(f.release ? $"{Loc.L("Буцааж тавилаа. Байгаль хамгаалсны шагнал", "Released! Nature-protector reward")}: +{f.coins} {Loc.L("зоос", "coins")}, +{f.xp} XP" : $"+{f.xp} XP · {Loc.L("Дэлгүүрт", "Sells for")} {f.sell} {Loc.L("зоос", "coins")}");
                GUILayout.BeginHorizontal(); if (g.Btn(Loc.L("Хаах", "Close"))) g.Later(g.Close); if (g.Btn(Loc.L("Дахин", "Again"), true)) g.Later(() => g.Swap(new FishingModal(g))); GUILayout.EndHorizontal();
                return;
            }
            g.Title(Loc.L("Загасчлах", "Fishing"));
            g.Small(Loc.L("Загасыг ногоон хэсэг дотор байлга. Дарж байхад ногоон хэсэг дээшилнэ.", "Keep the fish inside the green zone. Hold to lift the zone."));
            // the water column with the zone and the fish, and the catch meter beside it
            var r = GUILayoutUtility.GetRect(10, g.U(220), GUILayout.ExpandWidth(true));
            var bar = new Rect(r.center.x - g.U(40), r.y, g.U(56), r.height); var meter = new Rect(bar.xMax + g.U(10), r.y, g.U(14), r.height);
            g.Box(bar, Px.Col("#2b1e2e")); var inner = new Rect(bar.x + g.U(3), bar.y + g.U(3), bar.width - g.U(6), bar.height - g.U(6));
            g.Box(inner, Px.Col("#3a88c8")); // the zone moves up when you hold: zone 0 is the top
            bool inside = fish >= zone && fish <= zone + zoneH;
            g.Box(new Rect(inner.x, inner.y + zone * inner.height, inner.width, zoneH * inner.height), inside ? Px.Col("#7fe07a") : Px.Col("#5c9a36"));
            float fs = g.U(28); GUI.DrawTexture(new Rect(inner.center.x - fs / 2, inner.y + fish * inner.height - fs / 2, fs, fs), g.Tex(Px.Icon("ui:fish")));
            g.Box(meter, Px.Col("#2b1e2e")); var mi = new Rect(meter.x + g.U(2), meter.y + g.U(2), meter.width - g.U(4), meter.height - g.U(4));
            g.Box(new Rect(mi.x, mi.yMax - prog * mi.height, mi.width, prog * mi.height), Px.Col("#f7c646"));
            g.Gap();
            bool pressHeld = GUILayout.RepeatButton(Loc.L("Дарж барь (Space)", "Press & hold (Space)"), g.sPrimary, GUILayout.MinHeight(g.U(56))); g.DrawingAt(GUILayoutUtility.GetLastRect()); if (pressHeld) holdFrame = Time.frameCount;
            if (g.Btn(Loc.L("Хаах", "Close"), false, 30)) g.Later(g.Close);
        }
    }
}
