// The child's Нутаг progress (like the `village` object in js/store.js), saved as JSON in PlayerPrefs.
using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable] public sealed class KV { public string k; public long v; }
[Serializable] public sealed class QuestState { public string k; public int p; public bool done; public bool season; }

/** What a character wears (and rides): the web version's `look`. */
[Serializable]
public sealed class Look
{
    public string deel = "blue", skin = "#F6D3B3", hat, face, neck, pet, title, ride, coat;
    public string gender;   // "girl", or null for a boy (older saves have none)
    public bool Girl => gender == "girl";
    public Look Copy() => (Look)MemberwiseClone();
}
[Serializable] public sealed class HorseSave { public string coat = "bay"; public int races, wins, top5, cls, spd = 1, sta = 1, best; public float dist; public List<KV> bests = new List<KV>(); }
[Serializable] public sealed class ArchSave { public int best, hits, shots, games; }
[Serializable] public sealed class WrSave { public int best, champ, wins, losses, pvpW, pvpL, school; public List<KV> techs = new List<KV>(); }
[Serializable] public sealed class NpcSave { public string id; public int step, p; public bool on; }
[Serializable] public sealed class EmeeSave { public int visits, perfect; }
/** Your own ger on one of the 8 lots. */
/** Building a ger by hand: how many steps are done, its plot, the parts in hand and the wool on its way to felt. */
[Serializable] public sealed class GerSave { public int stage, lot = -1, wool, fluff, mats, mistakes; public bool perfect; public List<KV> parts = new List<KV>(); public List<int> poles = new List<int>(); } // poles: roof poles in so far in the isometric build (slots round the crown, in order)
[Serializable] public sealed class LotSave { public int i = -1; public string roof = "blue", door = "red"; public List<string> furn = new List<string>(); }

/** Progress in one heritage lesson. */
[Serializable] public sealed class LessonProg { public string id; public bool done; public int best, step; public long family; public List<KV> with = new List<KV>(); }

/** One child's profile and all their progress (the web version's profile object with its `village`). */
[Serializable]
public sealed class SaveData
{
    const string KEY = "naadamy.profile.";

    public string id = "";                      // the profile id (also picks the daily quests)
    public string name = "";
    public int age = 8, avatar;                 // avatar: the mascot animal (index into Profiles.MASCOTS)
    public string code = "", aimag = "", eth = "", created = "";   // code: 3 secret pictures, like "0-4-2"
    public List<LessonProg> learn = new List<LessonProg>();
    public List<KV> together = new List<KV>();
    public int coins = 40, xp;
    public float energy = Rules.ENERGY_MAX;
    public bool hasPos, tips, tutorial;
    public string room = "";                    // the private room to connect to (server/room-codes.js); "" until one is opened
    public string email = "";                   // the linked email account; "" for a guest
    public long tutorialSeen, nominLast;
    public List<KV> nominSaid = new List<KV>();
    public float posX, posY;
    public Look look = new Look();
    public List<string> owned = new List<string> { "deel-blue" };
    public int rod = 1, fishCaught;
    public List<KV> produce = new List<KV>();   // milk and the dairy foods you made
    public List<KV> fish = new List<KV>();
    public List<KV> milkT = new List<KV>();     // when each animal was last milked (unix ms)
    public List<KV> ate = new List<KV>();
    public List<string> seen = new List<string>();
    public List<string> pets = new List<string>(); // sheep petted today
    public string questDay = "", questsSeen = "";
    public List<QuestState> quests = new List<QuestState>();
    public HorseSave horse = new HorseSave();
    public ArchSave arch = new ArchSave();
    public WrSave wr = new WrSave();
    public EmeeSave emee = new EmeeSave();
    public List<NpcSave> npc = new List<NpcSave>();
    public LotSave lot = new LotSave();
    public GerSave ger = new GerSave();
    public List<KV> shorn = new List<KV>();             // sheep number → when it was last sheared (ms)
    public bool gerBuilt;
    public string rest = "", ovoo = "";
    public List<KV> homeDay = new List<KV>();    // things you can do at home once a day (k, yyyymmdd)
    public string server = "";                   // the Naadamy server (node server.js) for Basar AI, e.g. 192.168.1.10:8000
    public int basarAsked, quizTotal, quizRight;
    public List<KV> quizTopicTotal = new List<KV>(), quizTopicRight = new List<KV>();
    public List<KV> seasonDone = new List<KV>();
    // the aimag passport and the trips
    public List<KV> stamps = new List<KV>();        // province id → when it was stamped (unix ms)
    public List<string> seenMarks = new List<string>(); // "province:landmark" visited on a trip
    public List<string> travelFam = new List<string>(); // provinces whose herder family you visited
    public long aimagPass;                              // when the travel pass was bought (0: not yet)
    public string travelView = "2d";
    public List<KV> ach = new List<KV>();             // achievement id → when it was earned
    public List<KV> bichig = new List<KV>();          // Mongol script: letter/word id → best score
    public List<string> bichigMem = new List<string>(); // letters written from memory
    public int histStep, histFound;                   // the historian's task chain: current step, and whether its item was found
    public List<string> campWon = new List<string>(); // defend-the-homeland chapters won
    public List<KV> story = new List<KV>();           // story mode: chapter id → the part reached; "<id>:done" once finished
    public string ticket = "";                      // multiplayer identity ticket (64 hex)
    public string fam = "";                         // the family code this child was added to (Family.cs)
    public long charAt;                             // when the character (eth, aimag) was last chosen, for the cloud save
    // the server account this device last saw (Net.Sync): what the device has on top of it is progress still to upload
    public bool hasSrv;
    public int srvCoins, srvXp, srvRod = 1;
    public List<string> srvOwned = new List<string>();
    public string syncOp = "";                      // an upload on its way: sent again with the same id until the server answers
    public int syncCoins, syncXp;

    static long Find(List<KV> l, string k) { foreach (var e in l) if (e.k == k) return e.v; return 0; }
    static void Put(List<KV> l, string k, long v) { foreach (var e in l) if (e.k == k) { e.v = v; return; } l.Add(new KV { k = k, v = v }); }
    public static int Get(List<KV> l, string k) => (int)Find(l, k);
    public static void Add(List<KV> l, string k, int n) => Put(l, k, Math.Max(0, Find(l, k) + n));
    public static void Set(List<KV> l, string k, int v) => Put(l, k, v);

    public int Have(string k) => (int)Find(produce, k);
    public void Stock(string k, int n) => Put(produce, k, Math.Max(0, Have(k) + n));
    public long MilkedAt(string id) => Find(milkT, id);
    public void SetMilked(string id, long t) => Put(milkT, id, t);
    /** True the first time a key is marked, so a fact card can say "Did you know?" instead of "Remember". */
    public bool FirstTime(string k) { if (seen.Contains(k)) return false; seen.Add(k); return true; }
    public NpcSave Npc(string id) { var s = npc.Find(n => n.id == id); if (s == null) npc.Add(s = new NpcSave { id = id }); return s; }
    /** True once a day for a home activity (tea, music…). */
    public bool DayOnce(string k) { long d = long.Parse(DayKey().Replace("-", "")); if (Find(homeDay, k) == d) return false; Put(homeDay, k, d); return true; }

    public static string DayKey() => DateTime.Now.ToString("yyyy-MM-dd");
    public static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public LessonProg Lesson(string id) { var p = learn.Find(l => l.id == id); if (p == null) learn.Add(p = new LessonProg { id = id }); return p; }
    public bool LessonDone(string id) { var p = learn.Find(l => l.id == id); return p != null && p.done; }

    public static SaveData Load(string id)
    {
        try
        {
            string raw = PlayerPrefs.GetString(KEY + id, "");
            var v = string.IsNullOrEmpty(raw) ? null : JsonUtility.FromJson<SaveData>(raw);
            if (v != null) return v.Fix();
        }
        catch (Exception e) { Debug.LogWarning("Naadamy: could not read the save, starting fresh. " + e.Message); }
        return new SaveData { id = id }.Fix();
    }
    /** id and ticket: the player the server made for an email account. */
    public static SaveData Create(string name, int age, int avatar, string code, string id = null, string ticket = null)
    {
        var v = new SaveData { id = id ?? "p" + NowMs().ToString("x"), ticket = ticket ?? "", name = name, age = age, avatar = avatar, code = code ?? "", created = DayKey() }.Fix();
        EnsureTicket(v);
        v.Save(); return v;
    }

    public static void EnsureTicket(SaveData v)
    {
        if (v == null) return;
        if (!string.IsNullOrEmpty(v.ticket) && System.Text.RegularExpressions.Regex.IsMatch(v.ticket, "^[a-f0-9]{64}$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return;
        var bytes = new byte[32];
        using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create()) rng.GetBytes(bytes);
        System.Text.StringBuilder sb = new System.Text.StringBuilder(64);
        for (int i = 0; i < bytes.Length; i++) sb.Append(bytes[i].ToString("x2"));
        v.ticket = sb.ToString();
    }
    public static void Delete(string id) { PlayerPrefs.DeleteKey(KEY + id); PlayerPrefs.Save(); }
    /** Fills in anything an older save didn't have. */
    SaveData Fix()
    {
        ger ??= new GerSave(); shorn ??= new List<KV>(); fam ??= ""; server ??= ""; email ??= "";
        if (room == null || room == "NUTAG") room = ""; // older saves: nobody lands in the main village any more
        if (gerBuilt && ger.stage == 0) { ger.stage = 9; ger.lot = lot.i; } // a ger bought before gers were built by hand
        if (string.IsNullOrEmpty(id)) id = Guid.NewGuid().ToString("N").Substring(0, 12);
        look ??= new Look(); horse ??= new HorseSave(); arch ??= new ArchSave(); wr ??= new WrSave(); emee ??= new EmeeSave(); lot ??= new LotSave();
        if (owned == null || owned.Count == 0) owned = new List<string> { "deel-blue" };
        if (string.IsNullOrEmpty(look.deel)) look.deel = "blue";
        if (string.IsNullOrEmpty(look.skin)) look.skin = Rules.SKINS[0];
        look.ride = null;
        return this;
    }
    public void Save() { PlayerPrefs.SetString(KEY + id, JsonUtility.ToJson(this)); PlayerPrefs.Save(); }
}
