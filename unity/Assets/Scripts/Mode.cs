// Singleplayer or multiplayer. Singleplayer never opens a connection, so nothing of the network runs at all: no socket, no
// other players to move and draw, no messages to read. Multiplayer connects to the server and plays in a shared village.
// The choice is asked on entering Нутаг and remembered; the online pill at the top right asks again.
using UnityEngine;

public sealed partial class Game
{
    public bool Multi => App.playMode == "multi";
    public void SetPlayMode(bool multi)
    {
        App.playMode = multi ? "multi" : "solo"; App.Save();
        if (multi) net.Connect(this); else net.Close();
    }

    sealed class ModeModal : Modal
    {
        readonly System.Action after;   // what opens once the choice is made (a new player builds their character)
        public ModeModal(System.Action after = null) { this.after = after; }
        public override bool CanEscape => after == null;
        public override float Width => 600;
        public override (string who, string face)? Speaker => ("bolormaa", "curious");
        public override void Draw(Game g)
        {
            g.Title(Loc.L("Хэрхэн тоглох вэ?", "How do you want to play?"));
            g.Small(Loc.L("Дараа нь дээд талын “Ганцаараа / Олуулаа” товчоор сольж болно.", "You can switch later with the Solo / Online button at the top."));
            bool multi = g.Multi;
            var st = new GUIStyle(g.sBtn) { alignment = TextAnchor.UpperCenter, fontSize = g.Ts(13), padding = new RectOffset(Mathf.RoundToInt(g.U(10)), Mathf.RoundToInt(g.U(10)), Mathf.RoundToInt(g.U(10)), Mathf.RoundToInt(g.U(12))) };
            var on = new GUIStyle(st) { normal = g.sPrimary.normal, hover = g.sPrimary.hover };
            string solo = "<b><size=" + g.Ts(17) + ">" + Loc.L("Ганцаараа", "Singleplayer") + "</size></b>\n" + Loc.L("Интернэт хэрэггүй. Хурдан, гацахгүй. Ахиц энэ төхөөрөмж дээр хадгалагдана.", "No internet needed. Fast and smooth. Progress is kept on this device.");
            string many = "<b><size=" + g.Ts(17) + ">" + Loc.L("Олуулаа", "Multiplayer") + "</size></b>\n" + Loc.L("Өрөө нээгээд 6 үсэгтэй кодоо найздаа өгнө, эсвэл найзынхаа кодоор орно. Хамт барилдаж, гэр барина.", "Open a room and give your 6-letter code to friends, or type a friend's code. Wrestle, chat and build together.");
            int picked = -1;
            g.Grid("mode", 2, 200, (i, w) => Mathf.Max(g.U(120), st.CalcHeight(new GUIContent(i == 0 ? solo : many), w) + g.U(46)), (i, r) =>
            {
                bool sel = g.App.playMode == (i == 0 ? "solo" : "multi");
                if (GUI.Button(r, GUIContent.none, sel ? on : st)) picked = i;
                GUI.DrawTexture(new Rect(r.center.x - g.U(16), r.y + g.U(10), g.U(32), g.U(32)), g.Tex(i == 0 ? Px.Icon("ui:ger") : Emo.Icon("🤝") ?? Px.Icon("ui:chat")), ScaleMode.ScaleToFit);
                GUI.Label(new Rect(r.x, r.y + g.U(40), r.width, r.height - g.U(40)), i == 0 ? solo : many, new GUIStyle(st) { normal = { background = null, textColor = st.normal.textColor }, hover = { background = null }, active = { background = null } });
            }, 10);
            if (picked >= 0) { int k = picked; g.Later(() => { g.SetPlayMode(k == 1); Sfx.Chime(); g.Close(); after?.Invoke(); if (k == 1 && after == null) g.Open(new RoomModal()); g.Toast(k == 1 ? Loc.L("Сервертэй холбогдож байна…", "Connecting to the server…") : Loc.L("Ганцаараа тоглож байна", "Playing solo")); }); }
            if (multi && g.net.Online && g.Btn(Loc.L("Өрөө, чат", "Room and chat"), false, 36)) g.Later(() => g.Swap(new RoomModal()));
        }
        public override void Press(Game g) => g.Close();
    }
}
