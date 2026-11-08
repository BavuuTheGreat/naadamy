// Нутаг and the aimag trips in first person (js/fp.js): a Minecraft-style voxel world with mouse look, WASD, Space jump
// with gravity, sprint (Shift, double-tap W or the Run button), auto-jump up one block and swimming. E or a click uses
// whatever you are facing. Phones get a fixed joystick at the bottom left and drag-to-look everywhere else.
// The worlds themselves are built by the web version's js/fpworld.js and exported by Tools/export-web-data.js
// (Resources/World/*.bytes); the block textures are its 16×16 atlas (Resources/Art/fp_atlas).
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

public static class Voxels
{
    public sealed class Block { public string name; public int[] tex; public bool solid, cut, water; }
    static J D => Data.Get("fpworld");
    public static int TILE => D["TILE"].I; public static int M => D["M"].I; public static int G => D["G"].I;
    static List<Block> blocks;
    public static List<Block> BLOCKS => blocks ??= D["BLOCKS"].Select(b => b.IsNull ? null : new Block { name = b["name"].S, tex = b["tex"].Select(t => t.I).ToArray(), solid = b["solid"].B, cut = b["cut"].B, water = b["water"].B }).ToList();
    public static byte Id(string name) => (byte)D["B"][name].I;

    public sealed class World
    {
        public int X, Z, YS; public byte[] vox; public short[] top;
        public bool Inside(int x, int y, int z) => x >= 0 && y >= 0 && z >= 0 && x < X && y < YS && z < Z;
        public byte Get(int x, int y, int z) => Inside(x, y, z) ? vox[(y * Z + z) * X + x] : (byte)0;
        public void Set(int x, int y, int z, byte b) { if (Inside(x, y, z)) vox[(y * Z + z) * X + x] = b; }
        /** The ground height of a column (where you stand), as fpworld's groundAt. */
        public float GroundAt(float x, float z) { int ix = Js.Round(x), iz = Js.Round(z); if (ix < 0 || iz < 0 || ix >= X || iz >= Z) return G; int h = top[iz * X + ix]; return h != 0 ? h : G; }
        public void Box(int x0, int y0, int z0, int x1, int y1, int z1, byte b) { for (int y = Math.Min(y0, y1); y <= Math.Max(y0, y1); y++) for (int z = Math.Min(z0, z1); z <= Math.Max(z0, z1); z++) for (int x = Math.Min(x0, x1); x <= Math.Max(x0, x1); x++) Set(x, y, z, b); }
        public void Cyl(float cx, float cz, float r, int y0, int y1, byte b, bool hollow = false)
        {
            for (int y = y0; y <= y1; y++) for (int z = Mathf.FloorToInt(cz - r); z <= cz + r; z++) for (int x = Mathf.FloorToInt(cx - r); x <= cx + r; x++)
                    { float d = Mathf.Sqrt((x - cx) * (x - cx) + (z - cz) * (z - cz)); if (d <= r + 0.2f && (!hollow || d > r - 1.5f)) Set(x, y, z, b); }
        }
    }
    public static World Load(string name)
    {
        var ta = Resources.Load<TextAsset>("World/" + name); if (ta == null) return null;
        using (var gz = new GZipStream(new MemoryStream(ta.bytes), CompressionMode.Decompress))
        using (var ms = new MemoryStream())
        {
            gz.CopyTo(ms); var b = ms.ToArray();
            var w = new World { X = BitConverter.ToInt32(b, 0), Z = BitConverter.ToInt32(b, 4), YS = BitConverter.ToInt32(b, 8) };
            int n = w.X * w.YS * w.Z; w.vox = new byte[n]; Buffer.BlockCopy(b, 12, w.vox, 0, n);
            w.top = new short[w.X * w.Z]; Buffer.BlockCopy(b, 12 + n, w.top, 0, w.top.Length * 2);
            return w;
        }
    }
    /** The worlds are exported in summer: snow covers the grass in winter and the leaves turn in autumn. */
    public static void Season(World w, string season)
    {
        if (season != "winter" && season != "autumn") return;
        byte grass = Id("grass"), snow = Id("snow"), leaves = Id("leaves"), autumn = Id("autumn"), flower = Id("flower"), tall = Id("tallgrass");
        for (int i = 0; i < w.vox.Length; i++)
        {
            byte v = w.vox[i];
            if (season == "winter") { if (v == grass) w.vox[i] = snow; else if (v == flower || v == tall) w.vox[i] = 0; }
            else if (v == leaves) w.vox[i] = autumn;
        }
    }
    /** A ger like the real thing, walkable inside, door to the south (fpworld.createVillage's ger). */
    public static void Ger(World w, float cx, float cz, float r, byte roofB)
    {
        int y0 = G, wallTop = y0 + 1, peak = y0 + 2 + Math.Max(1, Js.Round(r * 0.75f));
        w.Box(Mathf.FloorToInt(cx - r), y0 - 1, Mathf.FloorToInt(cz - r), Mathf.CeilToInt(cx + r), y0 - 1, Mathf.CeilToInt(cz + r), Id("planks"));
        w.Cyl(cx, cz, r, y0, wallTop, Id("felt"), true); w.Cyl(cx, cz, r, wallTop, wallTop, roofB, true); w.Cyl(cx, cz, r - 1.6f, y0, wallTop, 0); w.Cyl(cx, cz, r - 1.2f, y0 - 1, y0 - 1, Id("carpet"));
        for (int k = 0; peak - k > wallTop; k++) w.Cyl(cx, cz, Mathf.Max(0.6f, (r + 0.3f) * ((k + 1) / (float)(peak - wallTop))), peak - k, peak - k, Id("feltRoof"));
        int icx = Js.Round(cx), icz = Js.Round(cz);
        w.Set(icx, peak, icz, Id("toono")); w.Set(icx + 1, peak + 1, icz, Id("iron"));
        int dz = Js.Round(cz + r); foreach (int z in new[] { dz, dz - 1 }) { w.Set(icx, y0, z, 0); w.Set(icx, y0 + 1, z, 0); }
        w.Set(icx + 1, y0, dz, Id("door")); w.Set(icx + 1, y0 + 1, dz, Id("door"));
        w.Set(icx, y0, icz, Id("iron")); foreach (int dx in new[] { -1, 1 }) w.Box(icx + dx, y0, icz - 1, icx + dx, peak - 1, icz - 1, Id("orange"));
    }

    // ---------------------------------------------------------------- meshing: visible faces only, with Minecraft-style corner shading
    static readonly int[][] FN = { new[] { 0, 1, 0 }, new[] { 0, -1, 0 }, new[] { 1, 0, 0 }, new[] { -1, 0, 0 }, new[] { 0, 0, 1 }, new[] { 0, 0, -1 } };
    static readonly int[][][] FC =
    {
        new[] { new[] { 0, 1, 1 }, new[] { 1, 1, 1 }, new[] { 1, 1, 0 }, new[] { 0, 1, 0 } },
        new[] { new[] { 0, 0, 0 }, new[] { 1, 0, 0 }, new[] { 1, 0, 1 }, new[] { 0, 0, 1 } },
        new[] { new[] { 1, 0, 1 }, new[] { 1, 0, 0 }, new[] { 1, 1, 0 }, new[] { 1, 1, 1 } },
        new[] { new[] { 0, 0, 0 }, new[] { 0, 0, 1 }, new[] { 0, 1, 1 }, new[] { 0, 1, 0 } },
        new[] { new[] { 0, 0, 1 }, new[] { 1, 0, 1 }, new[] { 1, 1, 1 }, new[] { 0, 1, 1 } },
        new[] { new[] { 1, 0, 0 }, new[] { 0, 0, 0 }, new[] { 0, 1, 0 }, new[] { 1, 1, 0 } },
    };
    static readonly float[] FS = { 1.0f, 0.5f, 0.82f, 0.7f, 0.9f, 0.62f };
    static readonly int[] FT = { 0, 2, 1, 1, 1, 1 };
    static readonly float[] AO = { 0.52f, 0.68f, 0.84f, 1 };
    public sealed class MeshData
    {
        public readonly List<Vector3> v = new List<Vector3>(); public readonly List<Vector2> uv = new List<Vector2>(); public readonly List<Color32> c = new List<Color32>(); public readonly List<int> t = new List<int>();
        public void Quad(Vector3[] p, Vector4 r, float[] l, bool flip)
        {
            int b = v.Count; var uvs = new[] { new Vector2(r.x, r.w), new Vector2(r.z, r.w), new Vector2(r.z, r.y), new Vector2(r.x, r.y) };
            for (int i = 0; i < 4; i++) { v.Add(new Vector3(p[i].x, p[i].y, -p[i].z)); uv.Add(new Vector2(uvs[i].x, 1 - uvs[i].y)); byte k = (byte)Mathf.Clamp(Mathf.RoundToInt(l[i] * 255), 0, 255); c.Add(new Color32(k, k, k, 255)); }
            foreach (int i in flip ? new[] { 1, 2, 3, 1, 3, 0 } : new[] { 0, 1, 2, 0, 2, 3 }) t.Add(b + i);
        }
        public Mesh ToMesh()
        {
            var m = new Mesh(); if (v.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(v); m.SetUVs(0, uv); m.SetColors(c); m.SetTriangles(t, 0); m.RecalculateBounds(); return m;
        }
    }
    public static Vector4 UV(int tex) { const float U = 1 / 16f, E = 0.002f; return new Vector4((tex % 16) * U + E, (tex / 16) * U + E, (tex % 16 + 1) * U - E, (tex / 16 + 1) * U - E); }
    /** Visible faces of one chunk (x0..x0+n-1, z0..z0+n-1): the land and plants, and the water apart. */
    public static (MeshData land, MeshData water) Build(World w, int x0, int z0, int n)
    {
        var BL = BLOCKS; var opq = new MeshData(); var wat = new MeshData();
        bool SolidB(byte b) => b != 0 && BL[b].solid && !BL[b].cut;
        int Occ(int x, int y, int z) => y < 4 ? 1 : SolidB(w.Get(x, y, z)) ? 1 : 0;
        var pts = new Vector3[4]; var l = new float[4];
        for (int y = 4; y < w.YS; y++) for (int z = z0; z < Math.Min(w.Z, z0 + n); z++) for (int x = x0; x < Math.Min(w.X, x0 + n); x++)
                {
                    byte b = w.Get(x, y, z); if (b == 0) continue; var B = BL[b];
                    if (!B.solid && B.cut) // plants (wheat, reeds, flowers): two crossing quads
                    {
                        var uv = UV(B.tex[1]); var l9 = new[] { 0.9f, 0.9f, 0.9f, 0.9f };
                        opq.Quad(new[] { new Vector3(x + 0.15f, y, z + 0.15f), new Vector3(x + 0.85f, y, z + 0.85f), new Vector3(x + 0.85f, y + 1, z + 0.85f), new Vector3(x + 0.15f, y + 1, z + 0.15f) }, uv, l9, false);
                        opq.Quad(new[] { new Vector3(x + 0.85f, y, z + 0.15f), new Vector3(x + 0.15f, y, z + 0.85f), new Vector3(x + 0.15f, y + 1, z + 0.85f), new Vector3(x + 0.85f, y + 1, z + 0.15f) }, uv, l9, false);
                        continue;
                    }
                    for (int f = 0; f < 6; f++)
                    {
                        var Fn = FN[f]; int nx = x + Fn[0], ny = y + Fn[1], nz = z + Fn[2]; byte nb = w.Get(nx, ny, nz); var N = nb != 0 ? BL[nb] : null;
                        if (B.water) { if (N != null && (N.water || N.solid)) continue; if (Fn[1] == -1) continue; }
                        else if (N != null && N.solid && !N.cut) continue;
                        else if (N != null && N.cut && nb == b) continue;
                        bool waterTop = B.water && !(w.Get(x, y + 1, z) != 0 && BL[w.Get(x, y + 1, z)].water);
                        for (int i = 0; i < 4; i++)
                        {
                            var c = FC[f][i]; pts[i] = new Vector3(x + c[0], y + (waterTop && c[1] == 1 ? 0.86f : c[1]), z + c[2]);
                            if (B.water) { l[i] = FS[f]; continue; }
                            // corner occlusion from the 3 blocks touching this corner, in front of the face
                            int a0, a1; if (Fn[0] != 0) { a0 = 1; a1 = 2; } else if (Fn[1] != 0) { a0 = 0; a1 = 2; } else { a0 = 0; a1 = 1; }
                            int[] d = { c[0] * 2 - 1, c[1] * 2 - 1, c[2] * 2 - 1 }, o1 = new int[3], o2 = new int[3]; o1[a0] = d[a0]; o2[a1] = d[a1];
                            int s1 = Occ(nx + o1[0], ny + o1[1], nz + o1[2]), s2 = Occ(nx + o2[0], ny + o2[1], nz + o2[2]), cc = Occ(nx + o1[0] + o2[0], ny + o1[1] + o2[1], nz + o1[2] + o2[2]);
                            int ao = s1 == 1 && s2 == 1 ? 0 : 3 - (s1 + s2 + cc);
                            l[i] = FS[f] * AO[ao];
                        }
                        (B.water ? wat : opq).Quad(pts, UV(B.tex[FT[f]]), l, l[0] + l[2] < l[1] + l[3]);
                    }
                }
        return (opq, wat);
    }
    /** fpworld's hash (JS number semantics), for the clouds. */
    public static double Hash(int x, int y, int s)
    {
        int n = Js.ToInt32((double)x * 374761393 + (double)y * 668265263 + (double)s * 2147483647);
        int a = n ^ (n >> 13); int b = Js.ToInt32((double)a * 1274126177);
        return (uint)(b ^ (b >> 16)) / 4294967295.0;
    }
    public static MeshData Clouds(World w)
    {
        var o = new MeshData(); var uv = UV(D["cloud"].I); int cell = 6, y = G + 34;
        for (int gz = -12; gz < w.Z / cell + 12; gz++) for (int gx = -24; gx < w.X / cell + 24; gx++)
            {
                if (Hash(gx, gz, 77) > 0.24) continue;
                int x0 = gx * cell, z0 = gz * cell, x1 = x0 + cell * (1 + (int)Math.Floor(Hash(gx, gz, 5) * 2)), z1 = z0 + cell;
                o.Quad(new[] { new Vector3(x0, y + 2, z1), new Vector3(x1, y + 2, z1), new Vector3(x1, y + 2, z0), new Vector3(x0, y + 2, z0) }, uv, new[] { 1f, 1, 1, 1 }, false);
                o.Quad(new[] { new Vector3(x0, y, z0), new Vector3(x1, y, z0), new Vector3(x1, y, z1), new Vector3(x0, y, z1) }, uv, new[] { 0.8f, 0.8f, 0.8f, 0.8f }, false);
            }
        return o;
    }
}

public sealed partial class Game
{
    public struct FpActor { public PxCanvas img; public float x, y, h, lift; public bool flip; }
    public struct FpMarker { public float x, y, lift; public string c, label; }
    /** What a first-person view shows: the world, where you start, the things to use, the people and animals. Positions are in
     *  the caller's map units (village tiles, or trip map pixels); `scale` turns them into blocks. */
    public sealed class FpSource
    {
        public string world, title, sub; public float scale = 4; public Vector2 start; public bool village;
        public Func<string> info; public List<Thing> things; public Func<float, List<FpActor>> actors; public Func<List<FpMarker>> markers;
        public Action<float> step; public List<(Func<string> label, Action run)> buttons = new List<(Func<string>, Action)>();
        public Func<string> hint;
    }

    public bool TryFirstPerson(string id)
    {
        if (race != null) { Toast(Loc.L("Уралдааны үеэр 3D харагдац ажиллахгүй.", "Not during a race.")); return false; }
        FpSource src = id == null ? VillageSource() : new TripScene(this, id, true).Scene.fp;
        if (src == null || Resources.Load<TextAsset>("World/" + src.world) == null) { Toast(Loc.L("3D ертөнц ачаалж чадсангүй.", "Could not build the 3D world.")); return false; }
        CloseAll(); Open(new FirstPerson(this, src));
        return true;
    }

    static PxCanvas arrowImg;
    FpSource VillageSource()
    {
        const float CHAR_H = 1.8f; float t0 = 0;
        Look NoRide(Look l) { var c = l.Copy(); c.ride = null; c.pet = null; return c; }
        arrowImg ??= MakeArrow();
        var src = new FpSource
        {
            world = "village", scale = Voxels.TILE, village = true, start = new Vector2(px + 0.5f, py + 0.82f),
            title = Loc.L("Нутаг", "Nutag"), sub = Loc.L("Өөрийн нүдээр", "First person"),
            hint = () => Loc.L("Ойртож харвал E", "Face something and press E"),
        };
        string welcome = "<b>" + Loc.L("Нутгаа өөрийн нүдээр!", "Nutag in first person!") + "</b>\n" + (Game.TouchUI
            ? Loc.L("Зүүн доорх джойстикоор алхана, бусад газар чирж эргэн харна. Морины уяанаас морь унаж давхиж болно!", "Walk with the joystick at the bottom left; drag anywhere else to look around. Take a horse from the hitching line and ride!")
            : Loc.L("WASD алхах, хулганаар харах, Space үсрэх, Shift гүйх, E харилцах. Морины уяанаас морь унаж давхиж болно!", "WASD walk, mouse to look, Space jump, Shift sprint, E interact. Take a horse from the hitching line and ride!"));
        src.info = () => Time.time - t0 < 9 ? welcome : null;
        t0 = Time.time;
        src.buttons.Add((() => Loc.L("Дээрээс харах", "Top view"), () => Close()));
        src.actors = t =>
        {
            var o = new List<FpActor>(); void Add(PxCanvas img, float x, float y, float h, bool flip = false, float lift = 0) => o.Add(new FpActor { img = img, x = x, y = y, h = h, flip = flip, lift = lift });
            foreach (var np in World.objects)
            {
                if (np.kind == "npcq" && Rules.NpcById(np.id) != null) Add(Px.Char(NoRide(Rules.NpcById(np.id).look), 0, 0), np.x + 0.5f, np.y + 0.9f, CHAR_H);
                if (np.kind == "npc") Add(Px.Char(Rules.ZASUUL, 0, 0), np.x + 0.5f, np.y + 0.9f, CHAR_H);
                if (np.kind == "emee") Add(Px.Char(Rules.EMEE, 0, 0), np.x + 0.5f, np.y + 0.9f, CHAR_H * 0.92f);
                if (np.kind == "general") Add(Px.Char(Rules.GENERAL, 0, 0), np.x + 0.5f, np.y + 0.9f, CHAR_H);
                if (np.kind == "historian") Add(Px.Char(Rules.HISTORIAN, 0, 0), np.x + 0.5f, np.y + 0.9f, CHAR_H);
                if (np.kind == "cow" || np.kind == "goat") Add(Px.Beast(np.kind, (int)(t * 2 + np.y) % 5 == 0 ? 1 : 0), np.x + (np.kind == "cow" ? 1 : 0.5f), np.y + 0.6f, np.kind == "cow" ? 1.4f : 0.95f);
                if (np.kind == "zel") { var z = new[] { (0.6f, "foal", 1.05f), (2.2f, "foal", 1.05f), (1.4f, "mare", 1.6f), (3.2f, "mare", 1.6f) }; for (int i = 0; i < z.Length; i++) Add(Px.Beast(z[i].Item2, 0), np.x + z[i].Item1, np.y + (z[i].Item2 == "mare" ? 1.1f : 0.5f), z[i].Item3, i % 2 == 1); }
                if (np.kind == "uya") { Add(Px.Horse("bay", 0, true), np.x + 0.8f, np.y + 0.6f, 1.6f); Add(Px.Beast("mare", 0), np.x + np.w - 0.8f, np.y + 0.6f, 1.6f, true); }
            }
            foreach (var q in sheep) { var shp = Px.Beast("sheep", (int)(t * 2 + q.i) % 2); Add(Shearable(q.i) ? shp : Px.Shorn(shp), q.px + 0.5f, q.py + 0.7f, 0.95f, !q.right); }
            foreach (var p in net.others.Values) // friends playing in the same room
            {
                var lk = p.look ?? new Look(); bool rides = lk.ride == "horse";
                if (rides) Add(Px.Horse(lk.coat ?? "bay", p.moving ? 1 + (int)(t * 8) % 4 : 0, true), p.px + 0.5f, p.py + 0.82f, 1.6f, p.dir == 1);
                Add(Px.Char(NoRide(lk), p.dir, p.moving ? 1 + (int)(t * 8) % 2 : 0), p.px + 0.5f, p.py + 0.82f, CHAR_H, false, rides ? 0.9f : 0);
            }
            var bt = battle;
            if (bt != null)
            {
                foreach (var e in bt.enemies) { Add(Px.Horse(e.look.coat ?? "bay", 1 + (int)(t * 9 + e.x) % 4, true), e.x, e.y, 1.6f, e.face == 1); Add(Px.Char(NoRide(e.look), e.face == 1 ? 1 : 2, 0), e.x, e.y, CHAR_H, false, 0.85f); }
                foreach (var a in bt.allies) { Add(Px.Horse(a.look.coat ?? "bay", 1 + (int)(t * 9 + a.x) % 4, true), a.x, a.y, 1.6f, a.face == 1); Add(Px.Char(NoRide(a.look), a.face == 1 ? 1 : 2, 0), a.x, a.y, CHAR_H, false, 0.85f); }
                foreach (var a in bt.arrows) Add(arrowImg, a.x, a.y, 0.18f, a.dx < 0, 1.4f);
            }
            return o;
        };
        src.markers = () =>
        {
            var o = new List<FpMarker>();
            foreach (var np in World.objects)
            {
                if (np.kind == "npcq" && Rules.NpcById(np.id) != null) { string mk = NpcMark(np.id); o.Add(new FpMarker { x = np.x + 0.5f, y = np.y + 0.9f, c = mk == "?" ? "#7fe07a" : mk != "" ? "#f7c646" : null, label = Rules.NpcById(np.id).name.ToString().Split(' ').Last(), lift = 2.4f }); }
                if (np.kind == "emee") o.Add(new FpMarker { x = np.x + 0.5f, y = np.y + 0.9f, c = V.emee.visits > 0 ? null : "#f7c646", label = Loc.L("Эмээ", "Grandma"), lift = 2.2f });
                if (np.kind == "npc") o.Add(new FpMarker { x = np.x + 0.5f, y = np.y + 0.9f, label = Loc.L("Засуул", "Herald"), lift = 2.4f });
                if (np.kind == "general") o.Add(new FpMarker { x = np.x + 0.5f, y = np.y + 0.9f, label = Loc.L("Жанжин", "General"), lift = 2.4f });
                if (np.kind == "historian") o.Add(new FpMarker { x = np.x + 0.5f, y = np.y + 0.9f, c = HistoryReady() ? "#7fe07a" : null, label = Loc.L("Түүхч", "Historian"), lift = 2.4f });
            }
            foreach (var p in net.others.Values) o.Add(new FpMarker { x = p.px + 0.5f, y = p.py + 0.82f, label = p.name, lift = p.look?.ride == "horse" ? 3.2f : 2.3f });
            if (battle != null) foreach (var e in battle.enemies) o.Add(new FpMarker { x = e.x, y = e.y, label = e.flee ? ":(" : new string('♥', Mathf.Max(0, e.courage)) + new string('◊', Mathf.Max(0, e.max - e.courage)), lift = 3.3f });
            return o;
        };
        return src;
    }
    static PxCanvas MakeArrow() { var c = new PxCanvas(12, 3); Px.R(c, 0, 1, 10, 1, "#6b4220"); Px.R(c, 9, 0, 3, 3, "#f7c646"); Px.R(c, 0, 0, 2, 3, "#ffffff"); return c; }

    // ================================================================ the first-person view
    public sealed class FirstPerson : Modal
    {
        const float HW = 0.3f, HT = 1.8f, FAR = 150;
        readonly FpSource src; readonly Voxels.World world; readonly int M; readonly float PXB;
        GameObject root; Camera fcam; Material landMat, waterMat, cloudMat, skyMat, sunMat; Texture2D atlas; Transform cloudT; Mesh skyMesh; Transform skyT, sunT;
        readonly List<(MeshRenderer r, MeshFilter f)> pool = new List<(MeshRenderer, MeshFilter)>(); Mesh quad; MaterialPropertyBlock mpb;
        System.Collections.IEnumerator building; int built, chunks;
        // the player, in blocks: x east, z south, y up
        float Px_, Py, Pz, vx, vy, vz, yaw = Mathf.PI / 2, pitch = -0.08f, fov = 1.25f, bob; bool ground, sprint, inWater, runLock, locked;
        const float STICK_LIFT = 64; // above the row of buttons along the bottom
        float lastW = -9, dogX, dogZ, lockAt; bool sawLock; Vector2 move; readonly Dictionary<int, (bool left, Vector2 p0, Vector2 p)> touches = new Dictionary<int, (bool, Vector2, Vector2)>();
        Thing near; Act ctx; float ctxT; Color fog, lo, hi; bool snowing; string season;
        bool oldFog; FogMode oldMode; Color oldColor; float oldStart, oldEnd;

        public override bool FullScreen => true;

#if UNITY_WEBGL && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern int NaadamyPointerLock(); // Plugins/WebGL/NaadamyPointer.jslib
#else
        static int NaadamyPointerLock() => Cursor.lockState == CursorLockMode.Locked ? 1 : Application.isFocused ? 0 : -1;
#endif
        /** The mouse is ours only while the browser says so. A browser takes it a moment after the click, and gives it
         *  back on Esc without passing the key on, so that Esc leaves the view here; losing focus only frees the mouse. */
        void CheckLock(Game g)
        {
            int s = NaadamyPointerLock();
            if (s == 1) { sawLock = true; return; }
            if (!sawLock && Time.unscaledTime - lockAt < 1.5f) return;
            Free();
            if (sawLock && s == 0) g.Later(() => { if (g.modal == this) g.Close(); });
        }
        void Free() { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; locked = false; }

        public FirstPerson(Game g, FpSource src)
        {
            this.src = src; world = Voxels.Load(src.world); M = Voxels.M; PXB = src.scale;
            season = Seasons.Current().id; Voxels.Season(world, season);
            if (src.village && g.MyStage >= GerRules.DONE && g.V.lot.i >= 0) // the ger you built on your lot
            {
                var lot = World.objects.FirstOrDefault(o => o.kind == "lot" && o.i == g.V.lot.i);
                if (lot != null)
                {
                    int x0 = M + lot.x * Voxels.TILE, z0 = M + lot.y * Voxels.TILE, w = lot.w * Voxels.TILE, d = lot.h * Voxels.TILE, G = Voxels.G;
                    world.Box(x0, G - 1, z0, x0 + w - 1, G - 1, z0 + d - 1, Voxels.Id(season == "winter" ? "snow" : "grass")); world.Box(x0, G, z0, x0 + w - 1, G + 12, z0 + d - 1, 0);
                    var ROOF = new Dictionary<string, string> { ["blue"] = "trim", ["red"] = "red", ["green"] = "greenTile", ["purple"] = "trim", ["orange"] = "orange", ["teal"] = "trim" };
                    Voxels.Ger(world, x0 + (w >> 1), z0 + (d >> 1) - 1, 4, Voxels.Id(ROOF.TryGetValue(g.V.lot.roof ?? "blue", out var rb) ? rb : "trim"));
                }
            }
            Px_ = M + src.start.x * PXB; Pz = M + src.start.y * PXB; Py = world.GroundAt(Px_, Pz) + 0.01f; dogX = Px_ - 1.2f; dogZ = Pz + 0.8f;
            if (!src.village) yaw = 0;
            snowing = season == "winter" || (src.world != "village" && Data.Get("travel")["trips"][src.world]["snow"].B);
            bool desert = src.world != "village" && Data.Get("travel")["trips"][src.world]["desert"].B;
            if (Seasons.Night()) { lo = new Color(0.16f, 0.18f, 0.34f); hi = new Color(0.03f, 0.04f, 0.13f); }
            else if (snowing) { lo = new Color(0.86f, 0.9f, 0.95f); hi = new Color(0.55f, 0.68f, 0.85f); }
            else if (desert) { lo = new Color(0.93f, 0.86f, 0.72f); hi = new Color(0.36f, 0.62f, 0.92f); }
            else { lo = new Color(0.78f, 0.88f, 0.97f); hi = new Color(0.3f, 0.56f, 0.92f); }
            fog = lo;
            Setup(g);
            Sfx.Pop();
        }

        void Setup(Game g)
        {
            root = new GameObject("First person"); root.transform.position = new Vector3(0, -10000, 0); // far from the village's sprites
            var ta = Resources.Load<TextAsset>("Art/fp_atlas.png"); atlas = new Texture2D(2, 2, TextureFormat.RGBA32, false); atlas.LoadImage(ta.bytes); atlas.filterMode = FilterMode.Point; atlas.wrapMode = TextureWrapMode.Clamp;
            Shader S(string n) => Resources.Load<Shader>("Shaders/" + n) ?? Shader.Find("Naadamy/" + n.Replace("Naadamy", ""));
            landMat = new Material(S("NaadamyVoxel")) { mainTexture = atlas };
            waterMat = new Material(S("NaadamyVoxelClear")) { mainTexture = atlas }; waterMat.SetFloat("_Alpha", 0.78f);
            cloudMat = new Material(S("NaadamyVoxelClear")) { mainTexture = atlas }; cloudMat.SetFloat("_Alpha", 0.82f); cloudMat.SetFloat("_Fog", 0);
            sunMat = new Material(S("NaadamyVoxelClear")); sunMat.SetFloat("_Alpha", 1); sunMat.SetFloat("_Fog", 0);
            skyMat = new Material(S("NaadamySky"));
            var camGo = new GameObject("First person camera"); camGo.transform.SetParent(root.transform, false);
            fcam = camGo.AddComponent<Camera>(); fcam.clearFlags = CameraClearFlags.SolidColor; fcam.backgroundColor = fog; fcam.nearClipPlane = 0.05f; fcam.farClipPlane = 260; fcam.depth = 5;
            if (g.cam != null) g.cam.enabled = false;
            oldFog = RenderSettings.fog; oldMode = RenderSettings.fogMode; oldColor = RenderSettings.fogColor; oldStart = RenderSettings.fogStartDistance; oldEnd = RenderSettings.fogEndDistance;
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear; RenderSettings.fogColor = fog; RenderSettings.fogStartDistance = FAR * 0.38f; RenderSettings.fogEndDistance = FAR;
            // the sky: a strip in front of the camera, coloured from the horizon up every frame
            skyMesh = new Mesh(); var sv = new List<Vector3>(); var st = new List<int>(); const int N = 24;
            for (int j = 0; j <= N; j++) { float yy = -1.05f + 2.1f * j / N; sv.Add(new Vector3(-1.1f, yy, 1)); sv.Add(new Vector3(1.1f, yy, 1)); if (j < N) { int b = j * 2; st.AddRange(new[] { b, b + 2, b + 1, b + 1, b + 2, b + 3 }); } }
            skyMesh.SetVertices(sv); skyMesh.SetTriangles(st, 0); skyMesh.SetColors(Enumerable.Repeat((Color32)Color.white, sv.Count).ToList()); skyMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e5f);
            var sky = new GameObject("Sky"); sky.transform.SetParent(camGo.transform, false); sky.AddComponent<MeshFilter>().sharedMesh = skyMesh; sky.AddComponent<MeshRenderer>().sharedMaterial = skyMat; skyT = sky.transform;
            // the sun (or the moon), far away
            var sunC = new PxCanvas(8, 8); if (Seasons.Night()) { Px.R(sunC, 1, 1, 6, 6, "#dfe6f5"); Px.R(sunC, 2, 3, 2, 2, "#b9c3da"); Px.R(sunC, 5, 5, 1, 1, "#b9c3da"); } else { Px.R(sunC, 0, 0, 8, 8, "#fff6c8"); Px.R(sunC, 2, 2, 4, 4, "#ffffff"); }
            sunMat.mainTexture = sunC.ToTexture();
            quad = new Mesh(); quad.SetVertices(new List<Vector3> { new Vector3(-0.5f, 0, 0), new Vector3(0.5f, 0, 0), new Vector3(0.5f, 1, 0), new Vector3(-0.5f, 1, 0) });
            quad.SetUVs(0, new List<Vector2> { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) }); quad.SetColors(Enumerable.Repeat((Color32)Color.white, 4).ToList()); quad.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0); quad.RecalculateBounds();
            var sun = new GameObject("Sun"); sun.transform.SetParent(root.transform, false); sun.AddComponent<MeshFilter>().sharedMesh = quad; sun.AddComponent<MeshRenderer>().sharedMaterial = sunMat; sunT = sun.transform;
            mpb = new MaterialPropertyBlock();
            // clouds drift slowly overhead
            var cl = new GameObject("Clouds"); cl.transform.SetParent(root.transform, false); cl.AddComponent<MeshFilter>().sharedMesh = Voxels.Clouds(world).ToMesh(); cl.AddComponent<MeshRenderer>().sharedMaterial = cloudMat; cloudT = cl.transform;
            building = Build();
        }
        /** Mesh the world a few chunks per frame, nearest first, so the view opens at once. */
        System.Collections.IEnumerator Build()
        {
            const int C = 16; var list = new List<(int x, int z)>();
            for (int z = 0; z < world.Z; z += C) for (int x = 0; x < world.X; x += C) list.Add((x, z));
            chunks = list.Count; list.Sort((a, b) => ((a.x + 8 - Px_) * (a.x + 8 - Px_) + (a.z + 8 - Pz) * (a.z + 8 - Pz)).CompareTo((b.x + 8 - Px_) * (b.x + 8 - Px_) + (b.z + 8 - Pz) * (b.z + 8 - Pz)));
            float t0 = Time.realtimeSinceStartup;
            foreach (var (x, z) in list)
            {
                var (land, water) = Voxels.Build(world, x, z, C);
                if (land.v.Count > 0) { var go = new GameObject("Land"); go.transform.SetParent(root.transform, false); go.AddComponent<MeshFilter>().sharedMesh = land.ToMesh(); go.AddComponent<MeshRenderer>().sharedMaterial = landMat; }
                if (water.v.Count > 0) { var go = new GameObject("Water"); go.transform.SetParent(root.transform, false); go.AddComponent<MeshFilter>().sharedMesh = water.ToMesh(); go.AddComponent<MeshRenderer>().sharedMaterial = waterMat; }
                built++;
                if (Time.realtimeSinceStartup - t0 > 0.012f) { yield return null; t0 = Time.realtimeSinceStartup; }
            }
        }
        public override void Closed(Game g)
        {
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            if (g.cam != null) g.cam.enabled = true;
            RenderSettings.fog = oldFog; RenderSettings.fogMode = oldMode; RenderSettings.fogColor = oldColor; RenderSettings.fogStartDistance = oldStart; RenderSettings.fogEndDistance = oldEnd;
            if (root != null) { foreach (var mf in root.GetComponentsInChildren<MeshFilter>()) if (mf.sharedMesh != quad) Destroy(mf.sharedMesh); Destroy(root); }
            Destroy(quad); Destroy(skyMesh); Destroy(atlas); foreach (var m in new[] { landMat, waterMat, cloudMat, skyMat, sunMat }) Destroy(m);
            g.CheckAchievements();
        }

        // ---------------------------------------------------------------- moving: Minecraft's walk and sprint, gravity, auto-jump, swimming
        // past the world's sides is a wall: the edge hills go up in one-block steps that auto-jump climbs, and beyond them is nothing to stand on
        bool SolidAt(int x, int y, int z) { if (x < 0 || z < 0 || x >= world.X || z >= world.Z) return true; byte b = world.Get(x, y, z); return b != 0 && Voxels.BLOCKS[b].solid; }
        bool WaterAt(float x, float y, float z) { byte b = world.Get(Mathf.FloorToInt(x), Mathf.FloorToInt(y), Mathf.FloorToInt(z)); return b != 0 && Voxels.BLOCKS[b].water; }
        bool Hits(float x, float y, float z)
        {
            for (int bx = Mathf.FloorToInt(x - HW); bx <= Mathf.FloorToInt(x + HW); bx++) for (int by = Mathf.FloorToInt(y); by <= Mathf.FloorToInt(y + HT - 0.01f); by++) for (int bz = Mathf.FloorToInt(z - HW); bz <= Mathf.FloorToInt(z + HW); bz++) if (SolidAt(bx, by, bz)) return true;
            return false;
        }
        bool MoveAxis(int ax, float d)
        {
            int n = Mathf.CeilToInt(Mathf.Abs(d) / 0.1f); if (n == 0) return false; float st = d / n;
            for (int i = 0; i < n; i++)
            {
                float nx = Px_ + (ax == 0 ? st : 0), ny = Py + (ax == 1 ? st : 0), nz = Pz + (ax == 2 ? st : 0);
                if (Hits(nx, ny, nz)) return true;
                Px_ = nx; Py = ny; Pz = nz;
            }
            return false;
        }
        void DoJump() { if (ground) { vy = 8.6f; ground = false; } else if (inWater) vy = 4; }
        void Physics(Game g, float dt)
        {
            for (int i = 0; i < 24 && Hits(Px_, Py, Pz); i++) Py += 1; // placed inside something: step up out of it
            float f = -g.KeyY() + move.y, s = g.KeyX() + move.x, len = Mathf.Sqrt(f * f + s * s); if (len > 1) { f /= len; s /= len; }
            sprint = f > 0.3f && (sprint || g.ShiftHeld || Input.GetKey(KeyCode.LeftControl) || runLock);
            if (f <= 0.3f) sprint = false;
            inWater = WaterAt(Px_, Py + 0.4f, Pz);
            float speed = inWater ? 2.2f : (sprint ? 5.6f : 4.3f) * (g.riding ? 1.7f : 1);
            float fx = Mathf.Sin(yaw), fz = -Mathf.Cos(yaw), rx = Mathf.Cos(yaw), rz = Mathf.Sin(yaw);
            float tvx = (fx * f + rx * s) * speed, tvz = (fz * f + rz * s) * speed, acc = Mathf.Min(1, dt * (ground ? 14 : 3.5f));
            vx += (tvx - vx) * acc; vz += (tvz - vz) * acc;
            if (Input.GetKey(KeyCode.Space) && inWater) vy = Mathf.Min(vy + 30 * dt, 3.5f);
            vy -= (inWater ? 9 : 28) * dt; vy = Mathf.Max(vy, inWater ? -3 : -40);
            bool bx = MoveAxis(0, vx * dt), bz = MoveAxis(2, vz * dt);
            if ((bx || bz) && ground && Mathf.Abs(tvx) + Mathf.Abs(tvz) > 0.5f && !Hits(Px_ + Mathf.Sign(vx) * 0.35f, Py + 1.05f, Pz + Mathf.Sign(vz) * 0.35f)) vy = 8.6f; // auto-jump up one block
            if (bx) vx = 0; if (bz) vz = 0;
            bool wasDown = vy < 0; ground = false;
            if (MoveAxis(1, vy * dt)) { if (wasDown) ground = true; vy = 0; }
            if (Py < 2) { Py = world.GroundAt(Px_, Pz) + 1; vy = 0; } // fell out of the world: back on the ground
            float moving = Mathf.Sqrt(vx * vx + vz * vz);
            bob += ground ? moving * dt * 1.9f : 0;
            fov += ((sprint ? 1.4f : 1.25f) - fov) * Mathf.Min(1, dt * 8); // the view widens when you sprint
        }
        Vector2 Me => new Vector2((Px_ - M) / PXB, (Pz - M) / PXB);
        Vector3 W(float x, float y, float z) => root.transform.position + new Vector3(x, y, -z);
        /** Things you can use: the one you face, or the one right next to you. */
        Thing Pick()
        {
            if (src.things == null) return null;
            float fx = Mathf.Sin(yaw), fz = -Mathf.Cos(yaw), bs = 1e9f; Thing best = null;
            foreach (var t in src.things)
            {
                float x = M + t.x * PXB, z = M + t.y * PXB, dx = x - Px_, dz = z - Pz, d = Mathf.Sqrt(dx * dx + dz * dz);
                if (d > 4.5f) continue;
                float cos = d < 0.01f ? 1 : (dx * fx + dz * fz) / d;
                if (d > 1.7f && cos < 0.82f) continue;
                float sc = d * (2 - cos) + (t.penalty > 0 ? 3 : 0);
                if (sc < bs) { bs = sc; best = t; }
            }
            return best;
        }
        void Interact(Game g)
        {
            if (src.village) { if (ctx != null) { var a = ctx; g.Later(() => g.DoAction(a)); } }
            else if (near != null) { var n = near; g.Later(() => n.run()); }
        }
        string NearLabel(Game g) => src.village ? (ctx != null ? g.LabelFor(ctx) : null) : near?.label();
        void Shoot(Game g)
        {
            float dx = Mathf.Sin(yaw) * Mathf.Cos(pitch), dz = -Mathf.Cos(yaw) * Mathf.Cos(pitch), n = Mathf.Max(0.001f, Mathf.Sqrt(dx * dx + dz * dz));
            g.ShootAt(g.px + 0.5f + dx / n * 10, g.py + 0.82f + dz / n * 10);
        }

        public override void Tick(Game g, float dt)
        {
            if (Game.TouchUI && Input.touchCount == 0 && touches.Count > 0) { touches.Clear(); move = Vector2.zero; } // a lift lost to a window that opened over the view
            if (building != null && !building.MoveNext()) building = null;
            if (locked) CheckLock(g);
            if (locked)
            {
                float mx = 0, my = 0; try { mx = Input.GetAxisRaw("Mouse X"); my = Input.GetAxisRaw("Mouse Y"); } catch (ArgumentException) { }
                yaw += mx * 10 * 0.0026f; pitch = Mathf.Clamp(pitch + my * 10 * 0.0026f, -1.55f, 1.55f);
            }
            Physics(g, dt);
            src.step?.Invoke(dt);
            if (src.village)
            {
                // your position back into the village, so quests and the people there know where you are
                float nx = (Px_ - M) / PXB - 0.5f, ny = (Pz - M) / PXB - 0.82f; g.moving = Mathf.Abs(nx - g.px) + Mathf.Abs(ny - g.py) > 0.002f;
                if (Mathf.Abs(nx - g.px) > 0.002f) g.face = nx < g.px ? 1 : 2;
                g.px = nx; g.py = ny; g.vx = g.vy = 0; g.path.Clear(); g.camPos = new Vector2(nx, ny);
                if (g.battle != null) g.BattleTick(dt);
                ctxT -= dt; if (ctxT <= 0) { ctxT = 0.2f; ctx = g.ContextAction(); }
            }
            near = Pick();
            // Basar trots behind you
            float dd = Mathf.Sqrt((Px_ - 1.1f * Mathf.Sin(yaw) - dogX) * (Px_ - 1.1f * Mathf.Sin(yaw) - dogX) + (Pz + 1.1f * Mathf.Cos(yaw) - dogZ) * (Pz + 1.1f * Mathf.Cos(yaw) - dogZ));
            if (dd > 1.4f) { float q = Mathf.Min(1, dt * 3); dogX += (Px_ - Mathf.Sin(yaw) * 1.4f - dogX) * q; dogZ += (Pz + Mathf.Cos(yaw) * 1.4f - dogZ) * q; }
            if (Mathf.Abs(Px_ - dogX) + Mathf.Abs(Pz - dogZ) > 10) { dogX = Px_; dogZ = Pz + 1; }
            Render(g);
        }
        public override void Key(Game g)
        {
            if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow)) { if (Time.time - lastW < 0.3f) sprint = true; lastW = Time.time; } // double-tap W to sprint
            if (Input.GetKeyDown(KeyCode.Space)) DoJump();
            if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Return)) Interact(g);
            if (Input.GetKeyDown(KeyCode.F) && g.battle != null) g.ShootNearest();
        }
        public override void Press(Game g) { }
        public override void Pointer(Game g, int id, Vector2 pos, bool down, bool up)
        {
            if (id < 0) // the mouse: the first click takes it for looking around, then clicks use things (or shoot, in a battle)
            {
                if (!down || g.OverUI(pos) || Game.TouchUI && Input.touchCount > 0) return;
                if (!locked) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; locked = true; lockAt = Time.unscaledTime; sawLock = false; return; }
                if (g.battle != null && !g.battle.over) Shoot(g); else Interact(g);
                return;
            }
            // touch: the fixed joystick moves (raised over the bottom button row), anywhere else looks
            var gp = new Vector2(pos.x, Screen.height - pos.y);
            if (down) { if (g.OverUI(pos)) return; bool st = g.NearStick(gp, STICK_LIFT); touches[id] = (st, pos, pos); if (st) { var v0 = g.StickAt(gp, STICK_LIFT); move = new Vector2(v0.x, -v0.y); } return; }
            if (!touches.TryGetValue(id, out var s)) return;
            if (s.left) { var v = g.StickAt(gp, STICK_LIFT); move = new Vector2(v.x, -v.y); }
            else { var d = pos - s.p; yaw += d.x * 0.0055f / g.UiScale * 2; pitch = Mathf.Clamp(pitch + d.y * 0.0055f / g.UiScale * 2, -1.55f, 1.55f); }
            touches[id] = (s.left, s.p0, pos);
            if (up) { if (s.left) move = Vector2.zero; touches.Remove(id); }
        }

        // ---------------------------------------------------------------- drawing: the camera, the sky, billboards for people and animals
        void Render(Game g)
        {
            float t = Time.time; bool riding = g.riding; float bb = ground ? Mathf.Sin(bob * 2) * (riding ? 0.12f : 0.05f) : 0;
            var eye = W(Px_, Py + (riding ? 2.55f : 1.62f) + bb, Pz);
            fcam.transform.position = eye; fcam.transform.rotation = Quaternion.Euler(-pitch * Mathf.Rad2Deg, yaw * Mathf.Rad2Deg, 0);
            fcam.fieldOfView = fov * Mathf.Rad2Deg; fcam.aspect = Screen.width / (float)Mathf.Max(1, Screen.height);
            // the sky strip fills the view; the horizon sits where the camera sees it
            float th = Mathf.Tan(fov / 2), d = 200; skyT.localPosition = Vector3.zero; skyT.localRotation = Quaternion.identity; skyT.localScale = new Vector3(d * th * fcam.aspect, d * th, d);
            float h = -Mathf.Tan(pitch) / th; var cols = new Color32[skyMesh.vertexCount];
            for (int i = 0; i < cols.Length; i++) { float yy = -1.05f + 2.1f * (i / 2) / 24f, q = Mathf.SmoothStep(0, 1, Mathf.Clamp01((yy - h) / 1.3f)); cols[i] = Color.Lerp(lo, hi, q); }
            skyMesh.colors32 = cols;
            sunT.position = eye + new Vector3(70, 95, 110); sunT.rotation = Quaternion.LookRotation(sunT.position - eye); sunT.localScale = new Vector3(16, 16, 1);
            cloudT.localPosition = new Vector3(((t * 1.1f) % 300) - 150, 0, 0);
            // people and animals: billboards that turn to face you
            var list = src.actors != null ? src.actors(t) : new List<FpActor>();
            list.Add(new FpActor { img = Px.Bankhar(Mathf.Sqrt(vx * vx + vz * vz) > 0.5f ? 1 + (int)(t * 8) % 2 : 3), x = (dogX - M) / PXB, y = (dogZ - M) / PXB, h = 0.95f });
            var rot = Quaternion.Euler(0, yaw * Mathf.Rad2Deg, 0);
            int used = 0;
            foreach (var a in list)
            {
                if (a.img == null) continue;
                if (used == pool.Count) { var go = new GameObject("Billboard"); go.transform.SetParent(root.transform, false); var mf = go.AddComponent<MeshFilter>(); mf.sharedMesh = quad; var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = landMat; pool.Add((mr, mf)); }
                var (r, _) = pool[used++]; r.enabled = true;
                float bx = M + a.x * PXB, bz = M + a.y * PXB, w = a.h * a.img.W / (float)a.img.H;
                r.transform.position = W(bx, world.GroundAt(bx, bz) + a.lift, bz); r.transform.rotation = rot; r.transform.localScale = new Vector3(a.flip ? -w : w, a.h, 1);
                mpb.SetTexture("_MainTex", g.Tex(a.img)); r.SetPropertyBlock(mpb);
            }
            for (int i = used; i < pool.Count; i++) pool[i].r.enabled = false;
        }

        public override void Draw(Game g)
        {
            if (fcam == null) return;
            if (g.modal != this && locked) Free(); // a window opened over the view: give the mouse back
            float t = Time.time; int sw = Screen.width, sh = Screen.height;
            // season tint and evening darkness, like the top view
            if (season == "winter") g.Box(new Rect(0, 0, sw, sh), new Color(0.92f, 0.95f, 0.98f, 0.16f));
            else if (season == "autumn") g.Box(new Rect(0, 0, sw, sh), new Color(1, 0.75f, 0.43f, 0.14f));
            else if (season == "spring") g.Box(new Rect(0, 0, sw, sh), new Color(1, 0.78f, 0.9f, 0.06f));
            if (Seasons.Night()) g.Box(new Rect(0, 0, sw, sh), new Color(0.07f, 0.09f, 0.27f, 0.38f));
            if (g.riding) // your horse's neck, mane and ears in front of you, bobbing as it moves
            {
                float u = Mathf.Max(6, Mathf.Round(Mathf.Min(sw, sh) / 70f)), bb = Mathf.Round(Mathf.Sin(bob * 2) * u), cx = sw / 2f, by = sh + u * 2 + bb;
                var coat = Px.COATS.TryGetValue(g.V.horse.coat ?? "bay", out var cs) ? cs : Px.COATS["bay"]; Color co = Px.Col(coat[0]), dk = Px.Col(coat[2]), ol = Px.Col("#2b1e2e");
                g.Box(new Rect(cx - u * 7, by - u * 18, u * 14, u * 18), ol); g.Box(new Rect(cx - u * 6, by - u * 17, u * 12, u * 17), co); g.Box(new Rect(cx - u * 6, by - u * 17, u * 3, u * 17), new Color(1, 1, 1, 0.12f));
                g.Box(new Rect(cx - u, by - u * 19, u * 2, u * 19), dk); for (int i = 0; i < 6; i++) g.Box(new Rect(cx - u * 2 + (i % 2) * u * 3, by - u * (18 - i * 3), u, u * 2), dk);
                g.Box(new Rect(cx - u * 5, by - u * 22, u * 3, u * 5), ol); g.Box(new Rect(cx + u * 2, by - u * 22, u * 3, u * 5), ol); g.Box(new Rect(cx - u * 4, by - u * 21, u, u * 3), co); g.Box(new Rect(cx + u * 3, by - u * 21, u, u * 3), co);
            }
            // falling snow, leaves or petals; butterflies in summer
            float pu = Mathf.Max(2, g.UiScale * 2);
            if (snowing) for (int i = 0; i < 70; i++) { float fx = Mod(i * 97 + Mathf.Sin(t + i) * 20 - yaw * 300, sw), fy = Mod(i * 53 + t * (40 + (i % 5) * 12) * g.UiScale, sh); g.Box(new Rect(fx, fy, pu, pu), new Color(1, 1, 1, 0.8f)); }
            else if (season == "autumn" || season == "spring")
            {
                var cols = season == "autumn" ? new[] { "#e8752a", "#c9461e", "#f2b33d" } : new[] { "#ffb7d5", "#ffffff", "#ff8fb1" };
                for (int i = 0; i < 40; i++) { float fx = Mod(i * 97 + Mathf.Sin(t * 0.8f + i) * 30 - yaw * 300, sw), fy = Mod(i * 53 + t * (24 + (i % 4) * 8) * g.UiScale, sh); g.Box(new Rect(fx, fy, season == "autumn" ? pu * 2 : pu, pu), Px.Col(cols[i % 3])); }
            }
            else if (season == "summer")
                for (int i = 0; i < 10; i++)
                {
                    float fx = Mod(i * 131 + Mathf.Sin(t * 0.8f + i) * 40 - yaw * 300, sw), fy = sh * (0.62f + ((i * 37) % 30) / 100f) + Mathf.Sin(t * 1.7f + i) * 10 * g.UiScale;
                    g.Box(new Rect(fx, fy, pu, pu), Color.white); if ((int)(t * 6 + i) % 2 == 1) { var c = Px.Col(i % 2 == 1 ? "#ffd34e" : "#b49cff"); g.Box(new Rect(fx - pu, fy, pu, pu), c); g.Box(new Rect(fx + pu, fy, pu, pu), c); }
                }
            // markers over people and places
            foreach (var m in src.markers != null ? src.markers() : new List<FpMarker>())
            {
                float bx = M + m.x * PXB, bz = M + m.y * PXB; var p = fcam.WorldToScreenPoint(W(bx, world.GroundAt(bx, bz) + (m.lift > 0 ? m.lift : 2.4f) + Mathf.Sin(t * 4) * 0.08f, bz));
                if (p.z <= 0.1f || p.z > 40) continue;
                float x = p.x, y = sh - p.y, sz = Mathf.Max(10, 26 - p.z * 0.4f) * g.UiScale;
                if (m.c != null)
                {
                    var ol = Px.Col("#2b1e2e"); var c = Px.Col(m.c);
                    g.Box(new Rect(x - sz * 0.18f, y - sz, sz * 0.36f, sz * 0.75f), ol); g.Box(new Rect(x - sz * 0.18f, y - sz * 0.15f, sz * 0.36f, sz * 0.3f), ol);
                    g.Box(new Rect(x - sz * 0.12f, y - sz * 0.94f, sz * 0.24f, sz * 0.63f), c); g.Box(new Rect(x - sz * 0.12f, y - sz * 0.09f, sz * 0.24f, sz * 0.18f), c);
                }
                if (m.label != null && p.z < 16) g.Outlined(x, y - sz - g.U(12), m.label, g.U(12), Color.white);
            }
            // the crosshair and what you are facing
            float ccx = sw / 2f, ccy = sh / 2f, s = 9 * g.UiScale, k = Mathf.Max(1, g.UiScale);
            g.Box(new Rect(ccx - s, ccy - k, s * 2, k * 2), new Color(1, 1, 1, 0.85f)); g.Box(new Rect(ccx - k, ccy - s, k * 2, s * 2), new Color(1, 1, 1, 0.85f));
            string nl = NearLabel(g);
            if (nl != null) { string lab = nl + (Game.TouchUI ? "" : " · E"); var r = new Rect(ccx - g.U(130), ccy + g.U(18), g.U(260), g.U(26)); GUI.Box(r, "<color=#f7c646><b>" + lab + "</b></color>", g.sToast); }
            if (building != null) GUI.Box(new Rect(ccx - g.U(130), ccy - g.U(60), g.U(260), g.U(30)), Loc.L($"Ертөнц бүтээж байна… {built * 100 / Mathf.Max(1, chunks)}%", $"Building the world… {built * 100 / Mathf.Max(1, chunks)}%"), g.sToast);
            // the buttons, like the 2D scenes
            var safe = Screen.safeArea; float sx = safe.x, sy = sh - safe.yMax, vw = safe.width / g.UiScale, vh = safe.height / g.UiScale;
            GUI.BeginGroup(new Rect(sx, sy, safe.width, safe.height));
            if (g.Button(g.URect(8, 8, 44, 44), "×")) g.Later(g.Close);
            var tr = g.URect(64, 8, Mathf.Min(360, vw - 72), 44); GUI.Box(tr, $"<b>{src.title}</b>" + (src.sub != null ? $"  <size={g.Ts(11)}>{src.sub}</size>" : ""), g.sNote);
            if (g.battle != null) GUI.Box(g.URect(vw / 2 - 210, 58, 420, 30), g.BattleLabel(), g.sToast);
            if (!Game.TouchUI && building == null) GUI.Box(g.URect(vw / 2 - 190, 94, 380, 30), locked ? Loc.L("Esc: Нутаг руу буцах", "Esc: back to Nutag") : Loc.L("Дарж эргэн харна · Esc: гарах", "Click to look around · Esc: leave"), g.sNote);
            string info = src.info?.Invoke();
            if (!string.IsNullOrEmpty(info))   // what is said here, in a box as tall as the words
            {
                var ist = new GUIStyle(g.sNote) { alignment = TextAnchor.MiddleLeft, fontSize = g.Ts(12), wordWrap = true }; float iw = Mathf.Min(520, vw - 16), ih = Mathf.Max(56, ist.CalcHeight(new GUIContent(info), g.U(iw)) / g.UiScale + 4);
                // phones: under the title, clear of the joystick
                var ir = g.URect(8, Game.TouchUI ? 56 : vh - 68 - ih, iw, ih); g.UiAdd(ir); GUI.Box(ir, info, ist);
            }
            float bx0 = 8;
            foreach (var (label, run) in src.buttons) { string l = label(); float w = Mathf.Max(90, l.Length * 10 + 32); if (g.Button(g.URect(bx0, vh - 60, w, 52), l)) g.Later(run); bx0 += w + 6; }
            if (g.riding && g.Button(g.URect(bx0, vh - 60, 110, 52), Loc.L("Мориноос буух", "Get off"))) g.Later(g.Dismount);
            if (g.Button(g.URect(vw - 340, vh - 60, 82, 52), runLock ? Loc.L("Алхах", "Walk") : Loc.L("Гүйх", "Run"), runLock ? g.sPrimary : g.sBtn)) runLock = !runLock;
            if (g.Button(g.URect(vw - 256, vh - 60, 82, 52), Loc.L("Үсрэх", "Jump"))) DoJump();
            if (g.battle != null && Game.TouchUI && g.Button(g.URect(vw - 166, vh - 124, 158, 52), Loc.L("Сум харвах", "Shoot"), g.sPrimary)) g.Later(g.ShootNearest);
            bool was = GUI.enabled; GUI.enabled = nl != null;
            string act = nl != null ? nl + (Game.TouchUI ? "" : " (E)") : Game.TouchUI ? Loc.L("Ойртож хараад товчийг дар", "Face something and tap the button") : src.hint?.Invoke() ?? Loc.L("Ойртож харвал E", "Face something and press E");
            if (g.Button(g.URect(vw - 166, vh - 64, 158, 56), act, g.sPrimary)) Interact(g);
            GUI.enabled = was;
            if (Game.TouchUI && g.modal == this) g.DrawStick(touches.Values.Any(q => q.left) ? new Vector2(move.x, -move.y) : Vector2.zero, STICK_LIFT, new Vector2(sx, sy));
            GUI.EndGroup();
        }
        static float Mod(float a, float m) => m <= 0 ? 0 : ((a % m) + m) % m;
    }
}
