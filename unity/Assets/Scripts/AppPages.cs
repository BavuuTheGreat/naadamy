// The app around Нутаг (js/app.js): the main menu, the parent dashboard, the family memory book (voices recorded on this
// device), settings and the about page.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

public sealed partial class Game
{
    // ================================================================ the main menu (☰ in Нутаг)
    sealed class MenuModal : Modal
    {
        public override void Draw(Game g)
        {
            var V = g.V; var e = Aimags.EthnicById(V.eth); var a = Aimags.ById(V.aimag);
            GUILayout.BeginHorizontal(g.sBtn); g.IconAt(Px.Mascot(V.avatar), 44);
            GUILayout.BeginVertical(); GUILayout.Label("<b>" + V.name + "</b>", g.sTitle);
            g.Small($"{V.age} {Loc.L("настай", "years old")}" + (e != null ? " · " + e.name : " · " + Profiles.MASCOTS[V.avatar].name) + (a != null ? " · " + a.name : ""));
            GUILayout.EndVertical(); GUILayout.EndHorizontal();
            void Item(string emoji, string label, Action run) // the whole row is the button, not only the arrow at its end
            {
                var r = GUILayoutUtility.GetRect(g.U(10), g.U(46), GUILayout.ExpandWidth(true));
                if (GUI.Button(r, "", g.sBtn)) g.Later(() => { g.Close(); run(); });
                float s = g.U(30); GUI.DrawTexture(new Rect(r.x + g.U(8), r.center.y - s / 2, s, s), g.Tex(Emo.Icon(emoji) ?? Px.Icon("ui:star")));
                GUI.Label(new Rect(r.x + g.U(46), r.y, r.width - g.U(80), r.height), label, new GUIStyle(g.sLabel) { alignment = TextAnchor.MiddleLeft });
                GUI.Label(new Rect(r.xMax - g.U(30), r.y, g.U(22), r.height), ">", new GUIStyle(g.sLabel) { alignment = TextAnchor.MiddleCenter });
            }
            Item("🏹", Loc.L("Түүхийн аялал: Чингис хаан", "Story mode: Chinggis Khaan"), () => g.OpenStory());
            Item("📚", Loc.L("Өв соёлын сургууль", "Heritage school"), () => g.Open(new LearnPage()));
            Item("✍️", Loc.L("Монгол бичгийн академи", "Mongol script academy"), () => g.Open(new BichigPage()));
            Item("🐕", Loc.L("Басартай ярилцах", "Talk with Basar"), () => g.OpenBasar());
            Item("🏅", Loc.L("Миний амжилтууд", "My achievements"), () => g.Open(new AchievementsPage()));
            Item("🏆", Loc.L("Шилдэгүүд", "Leaderboard"), () => g.Open(new BoardModal()));
            Item("🛂", Loc.L("Аймгийн паспорт", "Aimag passport"), () => g.Open(new PassportPage()));
            Item("👕", Loc.L("Хувцас", "Wardrobe"), () => g.Open(new WardrobeModal()));
            Item("🎨", Loc.L("Дүрээ өөрчлөх", "Change my character"), () => g.Open(new CreatePage(false)));
            Item("👨‍👩‍👧", Loc.L("Гэр бүл (том хүн)", "Family (grown-ups)"), () => g.OpenFamily());
            Item("🎙️", Loc.L("Гэр бүлийн дурсамж", "Family memory book"), () => g.Open(new MemoriesPage()));
            Item("❓", Loc.L("Болормаатай Нутгаар аялах", "Tour Nutag with Bolormaa"), () => g.StartTutorial(true));
            Item("⚙️", Loc.L("Тохиргоо", "Settings"), () => g.Open(new SettingsModal()));
            Item("ℹ️", Loc.L("Төслийн тухай", "About the project"), () => g.Open(new AboutPage()));
            if (g.Btn(Loc.L("Гарах / Профайл солих", "Log out / switch profile"), false, 36)) g.Later(g.LogOut);
        }
        public override void Press(Game g) => g.Close();
    }

    sealed class SettingsModal : Modal
    {
        string server;
        public override void Draw(Game g)
        {
            server ??= g.V.server;
            g.Title(Loc.L("Тохиргоо", "Settings"));
            GUILayout.BeginHorizontal(); g.Text(Loc.L("Хэл", "Language")); GUILayout.FlexibleSpace();
            if (GUILayout.Button(Loc.En ? "English → Монгол" : "Монгол → English", g.sBtn, GUILayout.MinHeight(g.U(36)))) { Loc.En = !Loc.En; g.App.en = Loc.En; g.App.Save(); }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal(); g.Text(Loc.L("Дуу", "Sound")); GUILayout.FlexibleSpace();
            if (GUILayout.Button(g.App.sound ? Loc.L("Асаалттай", "On") : Loc.L("Унтраалттай", "Off"), g.App.sound ? g.sPrimary : g.sBtn, GUILayout.MinHeight(g.U(36)))) { g.App.sound = !g.App.sound; Sfx.On = g.App.sound; g.App.Save(); }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal(); g.Text(Loc.L("Түүхийн яриа (дуу хоолой)", "Story voices (Mongolian)")); GUILayout.FlexibleSpace();
            if (GUILayout.Button(g.App.voice ? Loc.L("Асаалттай", "On") : Loc.L("Унтраалттай", "Off"), g.App.voice ? g.sPrimary : g.sBtn, GUILayout.MinHeight(g.U(36)))) { g.App.voice = !g.App.voice; if (!g.App.voice) Voice.Stop(); g.App.Save(); }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal(); g.Text(Loc.L("Арын хөгжим", "Background music")); GUILayout.FlexibleSpace();
            if (GUILayout.Button(Music.On ? Loc.L("Асаалттай", "On") : Loc.L("Унтраалттай", "Off"), Music.On ? g.sPrimary : g.sBtn, GUILayout.MinHeight(g.U(36)))) Music.Toggle();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal(); g.Text(Loc.L("Изометр харагдац", "Isometric view")); GUILayout.FlexibleSpace();
            if (GUILayout.Button(!g.App.flatView ? Loc.L("Асаалттай", "On") : Loc.L("Унтраалттай", "Off"), !g.App.flatView ? g.sPrimary : g.sBtn, GUILayout.MinHeight(g.U(36)))) { g.App.flatView = !g.App.flatView; g.App.Save(); }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal(); g.Text(Loc.L("Суралцах горим", "Learning mode")); GUILayout.FlexibleSpace(); // the choice made after the intro (GameModeModal)
            if (GUILayout.Button(g.StoryMode ? Loc.L("Түүхийн горим", "Story mode") : Loc.L("Чөлөөт горим", "Free roam"), g.sBtn, GUILayout.MinHeight(g.U(36)))) { g.App.gameMode = g.StoryMode ? "roam" : "story"; g.App.Save(); }
            GUILayout.EndHorizontal();
            GUILayout.Label("<b>" + Loc.L("Басар AI (Claude)", "Basar AI (Claude)") + "</b>", g.sLabel);
            g.Small(Loc.L("Компьютер дээр “node server/server.js” ажиллуулж, түүний хаягийг энд бичвэл Басар AI ухаантай болно (ж: 192.168.1.10:8000). Хоосон бол офлайн мэдлэгээр ажиллана.", "Run “node server/server.js” on a computer and write its address here to give Basar AI smarts (e.g. 192.168.1.10:8000). Empty: offline knowledge."));
            GUILayout.BeginHorizontal();
            server = GUILayout.TextField(server, 80, new GUIStyle(g.sBtn) { alignment = TextAnchor.MiddleLeft }, GUILayout.MinHeight(g.U(38)));
            if (GUILayout.Button(Loc.L("Хадгалах", "Save"), g.sPrimary, GUILayout.Width(g.U(100)), GUILayout.MinHeight(g.U(38)))) g.Later(() => { if (server.Trim() != "" && !Family.IsAddress(server)) { g.Toast(Loc.L("Энэ нь серверийн хаяг биш байна (ж: 192.168.1.10:8000). Гэр бүлийн эсвэл өрөөний кодыг энд бичихгүй.", "That isn't a server address (e.g. 192.168.1.10:8000). Family and room codes don't go here.")); return; } g.V.server = server.Trim(); g.V.Save(); if (g.V.server != "") { g.App.server = g.V.server; g.App.Save(); } Basar.Recheck(); if (g.Multi) g.net.Connect(g); g.StartCoroutine(Basar.Check(g)); g.Toast(Loc.L("Хадгаллаа", "Saved")); });
            GUILayout.EndHorizontal();
            g.Small(Basar.ai ? Loc.L("● Басар AI ухаантай ажиллаж байна.", "● Basar is running with AI.") : Loc.L("○ Басар офлайн мэдлэгээр ажиллаж байна.", "○ Basar is using offline knowledge."));
            if (g.Btn(Loc.L("Хаах", "Close"), true, 36)) g.Later(g.Close);
        }
        public override void Press(Game g) { }
    }

    // ================================================================ family / parent dashboard
    float parentUntil = -1;
    /** The parents' menu stays open 10 minutes after the gate, with the typed PIN in memory; then both are forgotten. */
    public void OpenFamily()
    {
        if (Time.realtimeSinceStartup < parentUntil) { Open(new FamilyPage()); return; }
        Family.pin = "";
        Open(new ParentGate(() => { parentUntil = Time.realtimeSinceStartup + 600; Open(new FamilyPage()); }, true));
    }
    sealed class FamilyPage : Page
    {
        protected override string Tab => "family";
        string childId; string famName = ""; int famFace;
        protected override string Title => Loc.L("Эцэг эхийн самбар", "Parent dashboard");
        public override void Closed(Game g) => Family.pin = ""; // the PIN never outlives the parents' menu
        protected override void Body(Game g)
        {
            if (Time.realtimeSinceStartup >= g.parentUntil) Family.pin = ""; // the 10 minutes are over: the next family action asks again
            var app = g.App; var kids = app.profiles.Select(SaveData.Load).ToList();
            var child = kids.FirstOrDefault(k => k.id == childId) ?? (kids.FirstOrDefault(k => k.id == g.V.id) ?? g.V);
            if (child.id == g.V.id) child = g.V;
            int nl = Lessons.Count, done = child.learn.Count(l => l.done), together = child.learn.Count(l => l.family > 0);
            if (kids.Count > 1) { int hit = g.Chips(kids.Select(k => k.name).ToList(), kids.FindIndex(k => k.id == child.id), 80); if (hit >= 0) { var id = kids[hit].id; g.Later(() => childId = id); } }
            var next = Lessons.ALL.FirstOrDefault(l => !(child.learn.Find(x => x.id == l["id"].S)?.family > 0));
            GUILayout.BeginVertical(g.sPanel);
            GUILayout.Label($"<b>{together}/{nl}</b>  " + Loc.L("Хамтдаа хийсэн бодит ажил", "Real-life activities done together"), g.sTitle);
            g.Text(next != null ? Loc.L($"Дараагийнх: “{next["family"]["what"].T.mn}”. Хүүхэд тань “{next["title"].T.mn}” хичээлээр бэлтгэнэ.", $"Next up: “{next["family"]["what"].T.en}”. Your child prepares in the “{next["title"].T.en}” lesson.") : Loc.L("Бүх гэр бүлийн ажлыг хамт хийжээ. Гайхалтай!", "You have done every family activity together. Wonderful!"));
            g.Small(Loc.L("Хүүхэд “Тоглохоо боль” гэхэд “Ээж ээ, хамтдаа гэр барьцгаая!” гэж хариулдаг болох нь бидний зорилго.", "Our goal: when you say “stop playing”, your child answers “Mom, let’s build a ger together!”"));
            GUILayout.EndVertical();
            g.Gap(12); g.Stats((Loc.L("Сурсан хичээл", "Lessons learned"), $"{done}/{nl}"), (Loc.L("Гараараа сурсан", "Learned by hand"), $"{GerRules.Done(child).Count}/{GerRules.HANDS_ON.Count}"), (Loc.L("Басарын асуулт", "Basar quiz"), $"{child.quizRight}/{child.quizTotal}"), (Loc.L("Амжилт", "Badges"), $"{child.ach.Count}/{Achievements.LIST.Count}"));
            g.Gap(12); GUILayout.BeginVertical(g.sPanel);
            GUILayout.Label($"<b>{child.name}: {Loc.L("хичээлүүд", "lessons")}</b>", g.sTitle);
            var ca = Aimags.ById(child.aimag); if (ca != null) g.Small(ca.name + " · " + ca.fact);
            foreach (var l in Lessons.ALL)
            {
                var pr = child.learn.Find(x => x.id == l["id"].S);
                GUILayout.BeginHorizontal(); GUILayout.Label(l["title"].T.ToString(), g.sLabel); GUILayout.FlexibleSpace();
                GUILayout.Label(pr != null && pr.done ? "<color=#2ea35f>√ " + Loc.L("Сурсан", "Learned") + "</color>" : Loc.L("Эхлээгүй", "Not yet"), g.sSmall);
                if (pr != null && pr.family > 0) GUILayout.Label("<color=#c2427e>" + Loc.L("хамт", "together") + "</color>", g.sSmall);
                GUILayout.EndHorizontal();
            }
            g.Small(Loc.L($"Басараас {child.basarAsked} асуулт асуусан. ", $"Asked Basar {child.basarAsked} questions. ") + (Basar.ai ? Loc.L("Басар AI (Claude) ухаантай ажиллаж байна.", "Basar is running with AI (Claude).") : Loc.L("Басар офлайн мэдлэгээр ажиллаж байна.", "Basar is using offline knowledge.")));
            GUILayout.EndVertical();
            g.Gap(12); LearnedReport(g, child);
            g.Gap(12); GUILayout.BeginVertical(g.sPanel);
            GUILayout.Label("<b>" + Loc.L("Гэр бүлийн гишүүд", "Family members") + "</b>", g.sTitle);
            foreach (var f in app.family.ToList())
            {
                g.Row(Px.Face(f.face), f.Name.ToString(), null);
                if (app.family.Count > 1 && g.SmallBtn("X", false, 44)) { var ff = f; g.Later(() => { app.family.Remove(ff); app.Save(); }); }
                g.EndRow();
            }
            GUILayout.BeginHorizontal(); for (int i = 0; i < Px.FACE_COUNT; i++) { var r = GUILayoutUtility.GetRect(g.U(32), g.U(32), GUILayout.Width(g.U(32))); if (GUI.Button(r, "", i == famFace ? g.sSel : g.sBtn)) { int k = i; g.Later(() => famFace = k); } GUI.DrawTexture(new Rect(r.x + 3, r.y + 3, r.width - 6, r.height - 6), g.Tex(Px.Face(i))); } GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            famName = GUILayout.TextField(famName, 16, new GUIStyle(g.sBtn) { alignment = TextAnchor.MiddleLeft }, GUILayout.MinHeight(g.U(38)));
            if (GUILayout.Button("+", g.sPrimary, GUILayout.Width(g.U(48)), GUILayout.MinHeight(g.U(38)))) g.Later(() => { string n = famName.Trim(); if (n == "") return; app.family.Add(new FamilyMember { id = "f" + SaveData.NowMs(), mn = n, en = n, face = famFace }); app.Save(); famName = ""; });
            GUILayout.EndHorizontal();
            g.Small(Loc.L("Нэр (ж: Нагац эгч)", "Name (e.g. Auntie)"));
            GUILayout.EndVertical();
            g.Gap(12); GUILayout.BeginVertical(g.sPanel);
            GUILayout.Label("<b>" + Loc.L("Хүүхдүүд", "Children") + "</b>", g.sTitle);
            foreach (var k in kids)
            {
                g.Row(Px.Mascot(k.avatar), k.name, $"{k.age} {Loc.L("нас", "yrs")}" + (k.code != "" ? " · " + Loc.L("нууцтай", "locked") : ""));
                if (k.code != "" && k.fam == "" && g.SmallBtn(Loc.L("Нууц арилгах", "Remove lock"), false, 120)) { var kk = k; g.Later(() => { var p = kk.id == g.V.id ? g.V : kk; p.code = ""; p.Save(); g.Toast(Loc.L("Нууц зураг арилгалаа", "Picture password removed")); }); }
                if (kids.Count > 1 && k.id != g.V.id && g.SmallBtn(Loc.L("Хасах", "Remove"), false, 90)) { var kk = k; g.Later(() => g.Open(new ConfirmModal(Loc.L($"{kk.name}-г хасах уу?", $"Remove {kk.name}?"), Loc.L("Буцаах боломжгүй.", "This cannot be undone."), () => { app.profiles.Remove(kk.id); SaveData.Delete(kk.id); app.Save(); }))); }
                g.EndRow();
            }
            if (g.Btn("+ " + Loc.L("Хүүхэд нэмэх", "Add a child"))) g.Later(() => { g.LogOut(); });
            GUILayout.EndVertical();
            g.Gap(12); FamilyCard(g, kids);
            g.Gap(12); EmailCard(g);
            g.Gap(12); PinCard(g);
            g.Gap(12);
            GUILayout.BeginVertical(g.sPanel);
            GUILayout.Label("<b>" + Loc.L("Тохиргоо, хүртээмж", "Settings & accessibility") + "</b>", g.sTitle);
            if (g.Btn(Loc.L("Тохиргоо нээх", "Open settings"))) g.Later(() => g.Open(new SettingsModal()));
            GUILayout.EndVertical();
            g.Gap(8);
            if (g.Btn(Loc.L("Бидний зорилго, шударга үнэ", "Our mission & fair pricing"))) g.Later(() => g.Open(new AboutPage()));
            g.Gap(12); Danger(g);
        }
        // the family code: made here, then each child is added with their 3 pictures so they can log in on other devices
        bool busy, needsPin; string joinCode = "", famErr = "", membersCode; List<J> members;
        void FamilyCard(Game g, List<SaveData> kids)
        {
            var app = g.App;
            GUILayout.BeginVertical(g.sPanel);
            GUILayout.Label("<b>" + Loc.L("Гэр бүлийн код", "Family code") + "</b>", g.sTitle);
            if (string.IsNullOrEmpty(app.familyCode))
            {
                g.Text(Loc.L("Код үүсгэвэл хүүхдүүд өөр утас, компьютер дээр нууц зургаараа нэвтэрч, ахицаа үргэлжлүүлнэ.", "With a family code, your children can log in on another phone or computer with their secret pictures and keep their progress."));
                g.Small(Loc.L("Таны том хүний ПИН код гэр бүлийн ПИН код болно: хүүхэд нэмэх, хасах, зураг солиход хэрэгтэй.", "Your grown-up PIN becomes the family PIN: it is needed to add or take out a child or give new pictures."));
                if (g.Btn(busy ? "…" : Loc.L("Гэр бүлийн код үүсгэх", "Make a family code"), true, 44, !busy)) g.Later(() => AddChild(g, kids.FirstOrDefault(k => k.code != "") ?? g.V));
                g.Small(Loc.L("Өөр төхөөрөмж дээр код үүсгэсэн бол:", "Made a code on another device already?"));
                GUILayout.BeginHorizontal();
                joinCode = GUILayout.TextField(joinCode, 9, new GUIStyle(g.sBtn) { alignment = TextAnchor.MiddleLeft }, GUILayout.MinHeight(g.U(38)));
                if (GUILayout.Button(Loc.L("Нэгдэх", "Join"), g.sPrimary, GUILayout.Width(g.U(100)), GUILayout.MinHeight(g.U(38))) && !busy) g.Later(() =>
                {
                    string code = Family.Clean(joinCode); mailErr = false;
                    if (code.Length != 8) { famErr = Loc.L("8 үсэг, тоотой кодоо бичээрэй", "Write the 8-letter code"); return; }
                    busy = true; famErr = "";
                    g.StartCoroutine(Family.Post(Family.ServerOf(g, g.V), "{\"act\":\"members\",\"code\":" + J.Quote(code) + "}", r =>
                    {
                        busy = false;
                        if (!r["ok"].B) { famErr = Family.Why(r); return; }
                        app.familyCode = code; app.server = Family.ServerOf(g, g.V).Trim(); app.Save();
                        // the family's own PIN, for adding this device's children; it becomes this device's PIN once it works.
                        // Not this device's PIN: cancelled, the next "Add to family" asks again
                        Family.pin = "";
                        if (!r["needsPin"].B) g.Later(() => g.Open(new PinModal(Loc.L("Гэр бүлийн ПИН код", "Family PIN"), Loc.L("Энэ гэр бүлийн том хүний ПИН кодыг оруулаарай:", "Enter this family's grown-up PIN:"), pin => Family.pin = pin)));
                    }));
                });
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.Label(Family.Show(app.familyCode), new GUIStyle(g.sBig) { alignment = TextAnchor.MiddleCenter });
                g.Small(Loc.L("Өөр төхөөрөмж дээр: нэвтрэх хуудас → “Гэр бүлийн кодоор нэвтрэх”. Кодыг зөвхөн гэр бүлдээ хэлээрэй.", "On another device: the login page → “Log in with a family code”. Share the code only with your family."));
                // every child of the family, also those only on other devices: new pictures (forgotten ones), or out of the family
                if (membersCode != app.familyCode)
                {
                    string c = membersCode = app.familyCode; members = null;
                    g.StartCoroutine(Family.Post(Family.ServerOf(g, g.V), "{\"act\":\"members\",\"code\":" + J.Quote(c) + "}", r =>
                    {
                        if (membersCode != c) return;
                        if (r["reason"].S == "no-family" && c == app.familyCode) { Family.Gone(g, null); membersCode = null; return; } // erased, or its last child taken out
                        members = r["ok"].B ? r["members"].ToList() : new List<J>(); needsPin = r["needsPin"].B;
                        var em = r["email"]; mail = !r["ok"].B ? -1 : !em.IsObject ? 0 : em["confirmed"].B ? 2 : 1; // members shows only { confirmed }
                        if (mail == 0 && app.emailFam == c) { app.emailTo = ""; app.Save(); } // stopped from a report's link, or on another device
                    }));
                }
                if (members == null) g.Small(Loc.L("Ачаалж байна…", "Loading…"));
                else if (needsPin) // a family made before the PIN: anyone with the code could still change it
                {
                    GUILayout.BeginVertical(g.sBtn);
                    GUILayout.Label("<b>" + Loc.L("Том хүний ПИН код тавих", "Set a grown-up PIN") + "</b>", g.sLabel);
                    g.Small(Loc.L("Энэ гэр бүлд ПИН код алга. Тавьвал зөвхөн том хүн хүүхэд нэмж, хасаж, зураг сольж чадна. Таны том хүний ПИН код гэр бүлийн ПИН код болно.", "This family has no PIN yet. With one, only a grown-up can add or take out children or give new pictures. Your grown-up PIN becomes the family PIN."));
                    if (g.SmallBtn(Loc.L("ПИН код тавих", "Set the PIN"), true, 140, !busy)) g.Later(() => WithPin(g, pin => { string rec = DevicePin.NewRecovery(); Run(g, Family.SetPin(g, pin, null, rec, r => Done(g, r, Loc.L("Гэр бүлд ПИН код тавилаа", "The family has a PIN now"), rec))); }));
                    GUILayout.EndVertical();
                }
                foreach (var m in members ?? new List<J>())
                {
                    string mid = m["id"].S, mname = m["name"].S; int mav = m["avatar"].I; var kid = kids.FirstOrDefault(k => k.id == mid);
                    g.Row(Px.Mascot(mav), mname, kid == null ? Loc.L("өөр төхөөрөмж", "other device") : m["locked"].B ? Loc.L("түр түгжээтэй", "locked for now") : null);
                    if (g.SmallBtn(Loc.L("Шинэ зураг", "New pictures"), false, 110, !busy)) g.Later(() =>
                    {
                        if (kid != null) { var p = kid.id == g.V.id ? g.V : kid; g.Open(new PicturesModal(p.name, p.avatar, code => { p.code = code; p.Save(); AddChild(g, p); })); } // its own device changes them
                        else g.Open(new PicturesModal(mname, mav, code => WithPin(g, _ => Manage(g, "pictures", mid, code, Loc.L($"{mname}-ийн зураг солигдлоо", $"{mname} has new pictures")))));
                    });
                    if (g.SmallBtn(Loc.L("Хасах", "Remove"), false, 80, !busy)) g.Later(() => g.Open(new ConfirmModal(Loc.L($"{mname}-г гэр бүлээс хасах уу?", $"Take {mname} out of the family?"),
                        Loc.L("Өөр төхөөрөмж дээр гэр бүлийн кодоор нэвтэрч чадахгүй болно. Ахиц нь устахгүй.", "They can no longer log in with the family code on another device. Their progress is not erased."),
                        () => WithPin(g, _ => Manage(g, "remove", mid, null, Loc.L($"{mname} гэр бүлээс гарлаа", $"{mname} left the family"))))));
                    g.EndRow();
                }
                foreach (var k in kids.Where(k => members != null && !members.Any(m => m["id"].S == k.id)))
                {
                    g.Row(Px.Mascot(k.avatar), k.name, null);
                    var kk = k;
                    if (g.SmallBtn(Loc.L("Гэр бүлд нэмэх", "Add to family"), true, 130, !busy)) g.Later(() => AddChild(g, kk));
                    g.EndRow();
                }
            }
            if (famErr != "" && !mailErr) GUILayout.Label($"<color=#e14b3d>{famErr}</color>", g.sLabel);
            GUILayout.EndVertical();
        }
        // the weekly email for the grown-ups (Family.Email): -1 not known yet, 0 none, 1 waiting for its confirm link, 2 confirmed
        int mail = -1; string mailText = ""; bool mailErr; // mailErr: famErr is the email's, shown in its card
        void EmailCard(Game g)
        {
            var app = g.App; bool fam = !string.IsNullOrEmpty(app.familyCode);
            string to = app.emailFam == app.familyCode ? app.emailTo : "";
            GUILayout.BeginVertical(g.sPanel);
            GUILayout.Label("<b>" + Loc.L("Том хүнд долоо хоног бүр имэйл", "Weekly email for grown-ups") + "</b>", g.sTitle);
            g.Small(Loc.L("Долоо хоногт нэг удаа хүүхэд бүрийн авсан XP, дуусгасан хичээл, шинэ амжилтыг имэйлээр илгээнэ. Хүссэн үедээ зогсоож болно.", "Once a week: each child's XP, finished lessons and new badges, by email. You can stop it any time."));
            if (!fam) g.Small(Loc.L("Эхлээд дээр гэр бүлийн код үүсгээрэй.", "Make a family code above first."));
            else if (mail < 0) g.Small(Loc.L("Ачаалж байна…", "Loading…"));
            else if (mail == 2)
            {
                GUILayout.Label("<color=#2ea35f>√</color> " + (to != "" ? to : Loc.L("Имэйл хаяг баталгаажсан", "The address is confirmed")), g.sLabel);
                if (g.Btn(Loc.L("Имэйлийг зогсоох", "Stop the email"), false, 36, !busy)) g.Later(RemoveEmail(g));
            }
            else
            {
                if (mail == 1)
                {
                    GUILayout.Label((to != "" ? to + ": " : "") + Loc.L("Имэйлээ шалгаад доторх товчийг дарна уу.", "Check your inbox and press the button in the email."), g.sLabel);
                    if (g.Btn(Loc.L("Дарсан, дахин шалгах", "Done, check again"), false, 36, !busy)) g.Later(() => membersCode = null);
                    g.Small(Loc.L("Имэйл ирээгүй бол хаягаа дахин бичээд илгээгээрэй:", "No email? Write the address again and send it:"));
                }
                mailText = g.Field(mailText, 254, "name@gmail.com");
                if (g.Btn(busy ? "…" : mail == 1 ? Loc.L("Дахин илгээх", "Send again") : Loc.L("Баталгаажуулах имэйл илгээх", "Send confirmation"), true, 40, !busy)) g.Later(() =>
                {
                    string e = mailText.Trim();
                    if (!Family.EmailOk(e)) { famErr = Family.Why(J.Parse("{\"reason\":\"email-invalid\"}")); mailErr = true; return; }
                    WithPin(g, pin => Run(g, Family.Email(g, pin, e, r => Done(g, r, r["email"]["confirmed"].B ? Loc.L("Энэ хаяг аль хэдийн баталгаажсан", "This address is already confirmed")
                        : Loc.L("Имэйл илгээлээ. Доторх товчийг дарж баталгаажуулаарай.", "Email sent. Press the button in it to confirm."), null, true)), true));
                });
                if (mail == 1 && g.Btn(Loc.L("Имэйл хаягийг арилгах", "Remove the address"), false, 36, !busy)) g.Later(RemoveEmail(g));
            }
            if (famErr != "" && mailErr) GUILayout.Label($"<color=#e14b3d>{famErr}</color>", g.sLabel);
            GUILayout.EndVertical();
        }
        Action RemoveEmail(Game g) => () => g.Open(new ConfirmModal(Loc.L("Имэйлийг зогсоох уу?", "Stop the weekly email?"), Loc.L("Имэйл хаягийг серверээс устгана. Дараа нь дахин нэмж болно.", "The address is deleted from the server. You can add one again later."),
            () => WithPin(g, pin => Run(g, Family.Email(g, pin, null, r => Done(g, r, Loc.L("Имэйлийг зогсоолоо", "The weekly email is stopped"), null, true)), true))));
        /** The grown-up PIN: change it (here and for the family). */
        void PinCard(Game g)
        {
            var app = g.App; bool fam = !string.IsNullOrEmpty(app.familyCode);
            GUILayout.BeginVertical(g.sPanel);
            GUILayout.Label("<b>" + Loc.L("Том хүний ПИН код", "Grown-up PIN") + "</b>", g.sTitle);
            g.Small(fam ? Loc.L("Нэг ПИН код: энэ төхөөрөмжийн том хүний хэсэг болон гэр бүлийнх.", "One PIN: for the grown-ups' part of this device and for the family.") : Loc.L("Том хүний хэсэг, худалдан авалтыг хамгаална.", "It guards the grown-ups' part and purchases."));
            // the old PIN is always typed again, never taken from memory
            if (g.Btn(Loc.L("ПИН код солих", "Change PIN"), false, 36, !busy)) g.Later(() => g.Open(new PinModal(Loc.L("Одоогийн ПИН код", "Current PIN"), Loc.L("Одоогийн ПИН кодоо оруулаарай:", "Enter the current PIN:"), old =>
                g.Open(new PinModal(Loc.L("Шинэ ПИН код", "New PIN"), Loc.L("Шинэ ПИН код сонгоорой (4-6 тоо):", "Choose a new PIN (4-6 digits):"), next =>
                {
                    pinAct = null;
                    if (fam && !needsPin) Run(g, Family.SetPin(g, next, old, null, r => Done(g, r, Loc.L("ПИН код солигдлоо", "The PIN is changed"))));
                    else Run(g, ChangeHere(g, old, next));
                }, true)))));
            GUILayout.EndVertical();
        }
        /** One danger zone: wipe this device, or delete the family from the server. */
        void Danger(Game g)
        {
            var app = g.App; bool fam = !string.IsNullOrEmpty(app.familyCode);
            GUILayout.BeginVertical(g.sPanel);
            GUILayout.Label("<b><color=#e14b3d>" + Loc.L("Аюултай хэсэг", "Danger zone") + "</color></b>", g.sTitle);
            g.Small(Loc.L("Профайл, ахиц, тохиргоо зөвхөн энэ утаснаас устна. Сервер дээрх гэр бүлд нөлөөлөхгүй.", "Profiles, progress and settings are erased from this device only. The family on the server is not touched."));
            if (g.Btn(Loc.L("Энэ төхөөрөмжийг цэвэрлэх", "Erase this device"), false, 36)) g.Later(() => g.Open(new ConfirmModal(Loc.L("Энэ төхөөрөмжийг цэвэрлэх үү?", "Erase this device?"), Loc.L("Профайл, ахиц, тохиргоо устна.", "Profiles, progress and settings will be erased."), () => { PlayerPrefs.DeleteAll(); PlayerPrefs.Save(); g.App = new AppState(); g.V = null; g.CloseAll(); })));
            if (fam)
            {
                g.Gap(10);
                g.Small(Loc.L("Сервер дээрх гэр бүлийн код болон хүүхэд бүрийн зоос, XP, хувцас, хадгалсан ахиц, нэвтрэх эрх, Нутаг дахь гэр, чатын мөрийг устгана. Буцаах боломжгүй. Энэ төхөөрөмж дээрх профайлууд үлдэнэ.", "Deletes the family code from the server and, for every child, the coins, XP, clothes, cloud save, other devices' logins, their ger in Nutag and their chat lines. This can't be undone. The profiles on this device stay."));
                if (g.Btn(Loc.L("Гэр бүлийг серверээс устгах", "Delete family from server"), false, 36, !busy)) g.Later(() => g.Open(new EraseModal(this)));
            }
            GUILayout.EndVertical();
        }
        /** Deleting the family: the code typed again, then the PIN (always asked here), then the server. */
        sealed class EraseModal : Modal
        {
            readonly FamilyPage page; string typed = "", err = "";
            public EraseModal(FamilyPage page) { this.page = page; }
            public override void Press(Game g) { }
            public override void Draw(Game g)
            {
                g.Title(Loc.L("Гэр бүлийн бүх өгөгдлийг устгах уу?", "Delete all family data?"));
                g.Small(Loc.L("Буцаах боломжгүй. Хүүхэд бүрийн сервер дээрх зоос, XP, хувцас, хадгалсан ахиц, нэвтрэх эрх, гэр, чат устна. Бусад төхөөрөмж гэр бүлийн кодоор нэвтэрч чадахгүй болно.", "This can't be undone. Every child's coins, XP, clothes, cloud save, logins, ger and chat on the server are deleted. Other devices can no longer log in with the family code."));
                g.Small(Loc.L($"Баталгаажуулахын тулд гэр бүлийн кодоо бичээрэй: {Family.Show(g.App.familyCode)}", $"To confirm, type the family code: {Family.Show(g.App.familyCode)}"));
                typed = g.Field(typed, 9, "ABCD-2345");
                if (err != "") GUILayout.Label($"<color=#e14b3d>{err}</color>", g.sLabel);
                GUILayout.BeginHorizontal();
                if (g.Btn(Loc.L("Болих", "Cancel"))) g.Later(g.Close);
                if (g.Btn(Loc.L("Устгах", "Delete"), true)) g.Later(() =>
                {
                    if (Family.Clean(typed) != g.App.familyCode) { err = Family.Why(J.Parse("{\"reason\":\"confirm\"}")); return; }
                    string confirm = typed; g.Close();
                    g.Open(new PinModal(Loc.L("Том хүний ПИН код", "Grown-up PIN"), Loc.L("Устгахын тулд ПИН кодоо оруулаарай:", "Enter the PIN to delete:"),
                        pin => { page.pinAct = null; page.Run(g, Family.Erase(g, pin, confirm, r => { page.members = null; page.Done(g, r, Loc.L("Гэр бүлийн бүх өгөгдлийг устгалаа", "All family data is deleted")); })); }));
                });
                GUILayout.EndHorizontal();
            }
        }
        /** A PIN change on a device without a family PIN: the old PIN checked here, then the new one saved (a frame apart). */
        System.Collections.IEnumerator ChangeHere(Game g, string old, string next)
        {
            yield return null;
            if (!DevicePin.Match(g.App, old)) { DevicePin.Wrong(g.App); busy = false; famErr = DevicePin.Wait(g.App) > 0 ? DevicePin.Locked(g.App) : Loc.L($"ПИН код таарсангүй. {DevicePin.Left(g.App)} оролдлого үлдлээ.", $"That PIN didn't match. {DevicePin.Left(g.App)} tries left."); yield break; }
            yield return null;
            DevicePin.Set(g.App, next); Family.pin = next; busy = false;
            g.Toast(Loc.L("ПИН код солигдлоо", "The PIN is changed"));
        }
        // the last action that needed the PIN, and the PIN typed for it here (not the gate's): asked again if the server refused it
        Action<string> pinAct; string typedPin;
        /** Runs `act` with the grown-up PIN: the one typed at the gate, or (after a refused one) asked again. */
        void WithPin(Game g, Action<string> act)
        {
            pinAct = act;
            if (Family.pin != "") { typedPin = null; act(Family.pin); return; }
            AskPin(g, Loc.L("Гэр бүлийн ПИН кодоо оруулаарай:", "Enter the family PIN:"));
        }
        void AskPin(Game g, string text) => g.Open(new PinModal(Loc.L("Том хүний ПИН код", "Grown-up PIN"), text, pin => { Family.pin = typedPin = pin; pinAct?.Invoke(pin); }));
        void Run(Game g, System.Collections.IEnumerator request, bool email = false) { busy = true; famErr = ""; mailErr = email; g.StartCoroutine(request); }
        /** A family request answered: a toast, or why not; a new recovery code is shown once. A refused PIN (this device's may be
         *  out of date: changed on another device) is asked again; with `keepPin`, a re-typed one that worked becomes this
         *  device's PIN. */
        void Done(Game g, J r, string ok, string recovery = null, bool keepPin = false)
        {
            busy = false; membersCode = null; // fetch the family again
            if (!r["ok"].B)
            {
                famErr = Family.Why(r);
                if (r["reason"].S == "pin" && pinAct != null) g.Later(() => AskPin(g, Family.Why(r)));
                return;
            }
            if (keepPin && typedPin != null) { string pin = typedPin; g.StartCoroutine(KeepPin(g, pin)); }
            typedPin = null; pinAct = null;
            g.Toast(ok);
            if (recovery != null) g.Later(() => g.Open(new RecoveryModal(recovery)));
        }
        static System.Collections.IEnumerator KeepPin(Game g, string pin) { yield return null; DevicePin.Set(g.App, pin); }
        void Manage(Game g, string act, string id, string pics, string done) => Run(g, Family.Manage(g, act, id, pics, r => Done(g, r, done, null, true)));
        void AddChild(Game g, SaveData k)
        {
            var p = k.id == g.V.id ? g.V : k;
            if (Family.PicsJson(p.code) == null) { g.Open(new PicturesModal(p.name, p.avatar, code => { p.code = code; p.Save(); AddChild(g, p); })); return; }
            bool create = string.IsNullOrEmpty(g.App.familyCode), member = members != null && members.Any(m => m["id"].S == p.id);
            // making the family, or a new child joining one with a PIN, needs the PIN; a child changing their own pictures doesn't
            bool withPin = create || (!member && !needsPin);
            if (withPin && Family.pin == "") { WithPin(g, _ => AddChild(g, p)); return; }
            string rec = create ? DevicePin.NewRecovery() : null;
            Run(g, Family.Link(g, p, r => Done(g, r, Loc.L($"{p.name} гэр бүлд нэмэгдлээ", $"{p.name} is in the family"), rec), rec, withPin));
        }
        /** A child's 3 secret pictures, chosen by a grown-up for the family code. */
        sealed class PicturesModal : Modal
        {
            readonly string name; readonly int avatar; readonly Action<string> picked; readonly List<int> sel = new List<int>();
            public PicturesModal(string name, int avatar, Action<string> picked) { this.name = name; this.avatar = avatar; this.picked = picked; }
            public override void Press(Game g) { }
            public override void Draw(Game g)
            {
                GUILayout.BeginHorizontal(); g.IconAt(Px.Mascot(avatar), 36); GUILayout.Label($"<b>{name}</b>", g.sTitle); GUILayout.EndHorizontal();
                g.Small(Loc.L("3 нууц зураг сонгоорой. Дараалал нь чухал.", "Choose 3 secret pictures. The order matters."));
                g.PicPad(sel, code => { g.Close(); picked(code); });
                if (g.Btn(Loc.L("Болих", "Cancel"))) g.Later(g.Close);
            }
        }
        /** What the child earned and learned, and the latest activity. */
        static void LearnedReport(Game g, SaveData child)
        {
            GUILayout.BeginVertical(g.sPanel);
            GUILayout.Label("<b>" + Loc.L("Амжилт ба сурсан зүйлс", "Achievements & what they learned") + "</b>", g.sTitle);
            g.Small(Loc.L($"{child.name} {child.ach.Count}/{Achievements.LIST.Count} амжилт авсан.", $"{child.name} has {child.ach.Count}/{Achievements.LIST.Count} badges."));
            foreach (var x in child.ach.OrderByDescending(x => x.v)) { var a = Achievements.ById(x.k); if (a == null) continue; g.Row(Emo.Badge(a.emblem, a.cat), a.name.ToString(), DateTimeOffset.FromUnixTimeMilliseconds(x.v).LocalDateTime.ToString("yyyy.MM.dd")); g.EndRow(); }
            GUILayout.Label("<b>" + Loc.L("Сурсан зүйлс", "What they learned") + "</b>", g.sLabel);
            var done = Lessons.ALL.Where(l => child.LessonDone(l["id"].S)).ToList(); var handIds = GerRules.Done(child);
            if (done.Count == 0 && handIds.Count == 0) g.Small(Loc.L("Одоохондоо юу ч сураагүй байна.", "Nothing learned yet."));
            foreach (var a in GerRules.HANDS_ON.Where(x => handIds.Contains(x.id))) // done by hand in Нутаг
            {
                GUILayout.Label("<b>" + a.name + "</b>" + $" <size={g.Ts(10)}>· {Loc.L("Нутагт гараараа", "by hand in Нутаг")}</size>", g.sLabel);
                foreach (var t in a.learned) GUILayout.Label("• " + t, g.sSmall);
            }
            foreach (var l in done)
            {
                var pr = child.learn.Find(x => x.id == l["id"].S);
                string with = string.Join(", ", new[] { ("family", Loc.L("гэр бүлтэйгээ", "with family")), ("friends", Loc.L("найзуудтайгаа", "with friends")), ("solo", Loc.L("ганцаараа", "alone")) }.Where(w => SaveData.Get(pr.with, w.Item1) > 0).Select(w => $"{w.Item2} {SaveData.Get(pr.with, w.Item1)}×"));
                GUILayout.Label("<b>" + l["title"].T + "</b>" + (with != "" ? $" <size={g.Ts(10)}>· {with}</size>" : ""), g.sLabel);
                foreach (var t in l["learned"]) GUILayout.Label("• " + t.T, g.sSmall);
            }
            if (child.quizTopicTotal.Count > 0)
            {
                GUILayout.Label("<b>" + Loc.L("Басарын асуултын сэдвүүд", "Basar quiz topics") + "</b>", g.sLabel);
                foreach (var (id, name) in Basar.Topics) { int r = SaveData.Get(child.quizTopicRight, id), tt = SaveData.Get(child.quizTopicTotal, id); GUILayout.BeginHorizontal(); GUILayout.Label(name.ToString(), g.sSmall, GUILayout.Width(g.U(140))); g.Bar(GUILayoutUtility.GetRect(10, g.U(10), GUILayout.ExpandWidth(true)), tt > 0 ? r / (float)tt : 0, Px.Col("#2ea35f")); GUILayout.Label($"{r}/{tt}", g.sSmall, GUILayout.Width(g.U(50))); GUILayout.EndHorizontal(); }
            }
            var logs = g.App.log.Where(x => x.pid == child.id).Reverse().Take(12).ToList();
            if (logs.Count > 0)
            {
                GUILayout.Label("<b>" + Loc.L("Сүүлийн үйл ажиллагаа", "Recent activity") + "</b>", g.sLabel);
                foreach (var x in logs) GUILayout.Label($"{LogLine(x)}  <size={g.Ts(9)}>{x.d}</size>", g.sSmall);
            }
            GUILayout.EndVertical();
        }
        static string LogLine(LogEntry x)
        {
            switch (x.k)
            {
                case "lesson": { var l = Lessons.ALL.FirstOrDefault(q => q["id"].S == x.id); return Loc.L($"“{(l != null ? l["title"].T.mn : x.id)}” хичээлийг дуусгасан", $"Finished “{(l != null ? l["title"].T.en : x.id)}”"); }
                case "ach": { var a = Achievements.ById(x.id); return Loc.L("Амжилт: ", "Badge: ") + (a != null ? a.name.ToString() : x.id); }
                case "hand": { var a = GerRules.HANDS_ON.Find(q => q.id == x.id); return a != null ? a.name.ToString() : x.id; }
                case "race": return Loc.L($"Морин уралдаанд {x.id}-р байр", $"Horse race: place {x.id}");
                case "stamp": { var a = Aimags.ById(x.id); return Loc.L("Тамга: ", "Stamp: ") + (a != null ? a.name.ToString() : x.id); }
                case "pass": return Loc.L("Аймгийн аяллын эрх авсан", "Got the Aimag Travel Pass");
                case "together": { var p = x.id.Split('|'); var l = Lessons.ALL.FirstOrDefault(q => q["id"].S == p[0]); return (l != null ? l["title"].T.ToString() : Loc.L("Асуулт хариулт", "Quiz")) + (p.Length > 2 ? " · " + p[2] : ""); }
            }
            return x.k;
        }
    }
    public sealed class ConfirmModal : Modal
    {
        readonly string title, text; readonly Action yes; public Action onNo;
        public ConfirmModal(string title, string text, Action yes) { this.title = title; this.text = text; this.yes = yes; }
        public override void Draw(Game g)
        {
            g.Title(title); g.Small(text);
            GUILayout.BeginHorizontal();
            if (g.Btn(Loc.L("Болих", "Cancel"))) g.Later(() => { g.Close(); onNo?.Invoke(); });
            if (g.Btn(Loc.L("Тийм", "Yes"), true)) g.Later(() => { g.Close(); yes(); });
            GUILayout.EndHorizontal();
        }
    }

    // ================================================================ the family memory book: elders' voices, kept on this device
    static string MemDir { get { string d = Path.Combine(Application.persistentDataPath, "memories"); Directory.CreateDirectory(d); return d; } }
    /** Voice clips kept on the server for the family (docs/PROTOCOL.md, "Voice clips"). Everything is best effort: with no family,
     *  no network or no storage on the server the clip simply stays on this device. */
    static class VoiceSync
    {
        public static readonly Dictionary<string, string> state = new Dictionary<string, string>(); // memory id -> "busy" | "fail" | "local" | "dl"
        public static HashSet<string> remote; // ids the server listed, null until it answered
        public static bool local;             // the server can't be used right now: say "this device only", quietly
        static readonly List<Memory> queue = new List<Memory>(); static bool sending; // one upload at a time, in order
        static string Base(Game g, SaveData me)
        {
            string s = (Family.ServerOf(g, me) ?? "").Trim().TrimEnd('/'); if (s == "") return null;
            if (!s.StartsWith("http")) s = Family.SCHEME + s; return s + "/api/voice";
        }
        static UnityWebRequest Req(Game g, SaveData me, string url, string method, string pin = null)
        {
            var r = new UnityWebRequest(url, method) { downloadHandler = new DownloadHandlerBuffer(), timeout = 120 }; // a 13 MB clip on a slow line
            r.SetRequestHeader("x-family", g.App.familyCode ?? ""); r.SetRequestHeader("x-player", me.id); r.SetRequestHeader("x-ticket", me.ticket);
            if (!string.IsNullOrEmpty(pin)) r.SetRequestHeader("x-pin", pin);
            return r;
        }
        /** The JSON answer, or { ok: false, reason: "offline" }; a clip body (not JSON) counts as ok. */
        static J Answer(UnityWebRequest r, int max = 4096)
        {
            string text = r.downloadHandler != null && r.downloadHandler.data != null && r.downloadHandler.data.Length < max ? r.downloadHandler.text : "";
            if (!string.IsNullOrEmpty(text) && text.TrimStart().StartsWith("{")) { try { var a = J.Parse(text); if (a.IsObject) return a; } catch (Exception) { } }
            return J.Parse(r.result == UnityWebRequest.Result.Success ? "{\"ok\":true}" : "{\"ok\":false,\"reason\":\"offline\"}");
        }
        /** Reasons that mean "this device only" (nothing to retry by hand). */
        static bool Quiet(string why) => why == "offline" || why == "no-server" || why == "storage-off" || why == "storage" || why == "no-family" || why == "identity";
        public static IEnumerator Sync(Game g)
        {
            local = false; foreach (var k in state.Where(kv => kv.Value == "local").Select(kv => kv.Key).ToList()) state.Remove(k);
            var me = Family.Member(g); string b = me == null ? null : Base(g, me);
            if (b == null) { local = true; yield break; }
            using (var r = Req(g, me, b, "GET"))
            {
                yield return r.SendWebRequest(); var a = Answer(r, 1 << 20); // the list grows with the clips: no 4 KB cap here
                if (a["ok"].B && !a.Has("clips")) a = J.Parse("{\"ok\":false,\"reason\":\"offline\"}"); // a cut answer is no list: never take it as "the server has nothing"
                if (!a["ok"].B) { if (Quiet(a["reason"].S)) local = true; yield break; }
                remote = new HashSet<string>(); foreach (var c in a["clips"]) remote.Add(c["id"].S);
            }
            foreach (var id in g.App.voiceDel.ToList()) { if (remote.Contains(id)) yield return g.StartCoroutine(Delete(g, id, null, true)); else g.App.voiceDel.Remove(id); }
            bool save = false;
            foreach (var m in g.App.memories.ToList())
            {
                if (string.IsNullOrEmpty(m.audio) || m.cloud == "x") continue; // "x": the server refused it for good (too long, not a recording)
                if (remote.Contains(m.id)) { if (m.cloud != "up") { m.cloud = "up"; save = true; } }
                else if (File.Exists(Path.Combine(MemDir, m.audio)) && !state.ContainsKey(m.id)) { if (m.cloud != "") { m.cloud = ""; save = true; } Upload(g, m); }
            }
            if (save) g.App.Save();
        }
        /** Puts the clip in line to be sent; the clips go one by one. */
        public static void Upload(Game g, Memory m)
        {
            if (string.IsNullOrEmpty(m.audio) || m.cloud == "x" || state.ContainsKey(m.id) || queue.Contains(m)) return;
            queue.Add(m); if (!sending) g.StartCoroutine(Send(g));
        }
        static IEnumerator Send(Game g)
        {
            sending = true;
            while (queue.Count > 0)
            {
                var m = queue[0]; int tries = 0;
                while (true)
                {
                    string path = Path.Combine(MemDir, m.audio);
                    var me = Family.Member(g); string b = me == null ? null : Base(g, me);
                    if (!File.Exists(path) || b == null || !g.App.memories.Contains(m)) { state.Remove(m.id); break; } // deleted meanwhile, or no family any more
                    state[m.id] = "busy"; string why = null;
                    using (var r = Req(g, me, b + "/" + m.id, "PUT"))
                    {
                        r.uploadHandler = new UploadHandlerRaw(File.ReadAllBytes(path)) { contentType = "audio/wav" };
                        yield return r.SendWebRequest(); var a = Answer(r);
                        if (a["ok"].B) { state.Remove(m.id); m.cloud = "up"; remote?.Add(m.id); g.App.Save(); break; }
                        why = a["reason"].S;
                    }
                    if (why == "busy" && ++tries < 6) { yield return new WaitForSecondsRealtime(5 * tries); continue; } // the server is taking another clip: wait our turn
                    if (why == "too-big" || why == "not-wav" || why == "invalid") { state.Remove(m.id); m.cloud = "x"; g.App.Save(); break; } // trying again won't help
                    state[m.id] = Quiet(why) ? "local" : "fail"; break;
                }
                queue.Remove(m);
            }
            sending = false;
        }
        /** Fetches a clip this device doesn't have and caches it in MemDir; done(true) when it is there. */
        public static IEnumerator Download(Game g, Memory m, Action<bool> done)
        {
            var me = Family.Member(g); string b = me == null ? null : Base(g, me);
            if (b == null) { done(false); yield break; }
            state[m.id] = "dl"; bool ok = false;
            using (var r = Req(g, me, b + "/" + m.id, "GET"))
            {
                yield return r.SendWebRequest();
                if (r.result == UnityWebRequest.Result.Success && r.downloadHandler.data != null && r.downloadHandler.data.Length > 44) { try { File.WriteAllBytes(Path.Combine(MemDir, m.audio), r.downloadHandler.data); ok = true; } catch (Exception) { } }
            }
            state.Remove(m.id); done(ok);
        }
        /** Deletes the server's copy. If it wants the family PIN (the clip is another child's), the PinModal asks once. */
        public static IEnumerator Delete(Game g, string id, string pin = null, bool later = false)
        {
            var me = Family.Member(g); string b = me == null ? null : Base(g, me); if (b == null) { Later(g, id); yield break; }
            string sent = pin ?? Family.pin;
            using (var r = Req(g, me, b + "/" + id, "DELETE", sent))
            {
                yield return r.SendWebRequest(); var a = Answer(r); string why = a["reason"].S;
                if (a["ok"].B || why == "no-clip") { remote?.Remove(id); if (g.App.voiceDel.Remove(id)) g.App.Save(); if (a["ok"].B && !string.IsNullOrEmpty(sent)) Family.pin = sent; }
                else if (Quiet(why) || why == "busy") Later(g, id); // offline: try again at the next sync
                else if (later) { if (g.App.voiceDel.Remove(id)) g.App.Save(); } // a retry that wants the PIN: don't pop a window during a sync
                else if (why == "pin" && string.IsNullOrEmpty(sent)) g.Later(() => g.Open(new PinModal(Loc.L("Том хүний ПИН код", "Grown-up PIN"), Loc.L("Энэ бичлэг бусдынх. Серверээс устгахын тулд ПИН кодоо оруулаарай:", "This clip is someone else's. Enter the PIN to delete it from the server:"), p => g.StartCoroutine(Delete(g, id, p)))));
                else if (why == "pin" || why == "pin-locked" || why == "needs-pin") { Family.pin = ""; g.Later(() => g.Toast(Family.Why(a))); }
            }
        }
        static void Later(Game g, string id) { if (!g.App.voiceDel.Contains(id)) { g.App.voiceDel.Add(id); g.App.Save(); } }
        /** The small mark under a memory with a voice clip, or null. */
        public static string Mark(Game g, Memory m)
        {
            if (string.IsNullOrEmpty(m.audio)) return null;
            state.TryGetValue(m.id, out var s);
            if (m.cloud == "up") return Loc.L("√ Гэр бүлийн сервер дээр хадгалагдсан", "√ Saved for the family");
            if (m.cloud == "x") return Loc.L("Сервер хүлээж авсангүй (хэт урт). Зөвхөн энэ төхөөрөмж дээр хадгалагдсан", "The server can't take it (too long): saved on this device only");
            if (s == "busy") return Loc.L("… Сервер рүү илгээж байна", "… Sending to the family");
            if (s == "fail") return Loc.L("! Илгээж чадсангүй", "! Could not send");
            if (local || s == "local" || string.IsNullOrEmpty(g.App.familyCode)) return Loc.L("Зөвхөн энэ төхөөрөмж дээр хадгалагдсан", "Saved on this device only");
            return Loc.L("… Илгээхийг хүлээж байна", "… Waiting to send");
        }
    }
    sealed class MemoriesPage : Page
    {
        AudioSource player; string playing; bool started, closed;
        protected override string Title => Loc.L("Гэр бүлийн дурсамжийн ном", "Family memory book");
        protected override void Body(Game g)
        {
            if (!started) { started = true; g.Later(() => g.StartCoroutine(VoiceSync.Sync(g))); }
            GUILayout.BeginVertical(g.sPanel);
            g.Text(Loc.L("Эмээ, өвөөгийн дуу хоолой, үлгэр, амьдралын сургамжийг үүрд хадгал. Үүнийг ямар ч хиймэл оюун орлож чадахгүй.", "Keep your grandparents' voices, stories and life lessons forever. No AI can replace this."));
            if (g.Btn(Loc.L("Шинэ дурсамж бичих", "Record a new memory"), true, 48)) g.Later(() => g.Open(new MemoryForm()));
            g.Small(string.IsNullOrEmpty(g.App.familyCode) || VoiceSync.local ? Loc.L("Бүх бичлэг зөвхөн энэ төхөөрөмж дээр хадгалагдана.", "Recordings are stored only on this device.") : Loc.L("Бичлэгүүд энэ төхөөрөмж болон гэр бүлийн сервер дээр хадгалагдана.", "Recordings are kept on this device and on the family's server."));
            GUILayout.EndVertical();
            var list = g.App.memories.OrderByDescending(m => m.ts).ToList();
            if (list.Count == 0) g.Text(Loc.L("Одоохондоо дурсамж алга. Эмээ, өвөөгөөсөө анхны түүхээ асуугаарай!", "No memories yet. Ask your grandparents for the first story!"));
            foreach (var m in list)
            {
                GUILayout.BeginVertical(g.sPanel);
                GUILayout.BeginHorizontal(); g.IconAt(Px.Face(m.whoFace), 36); GUILayout.BeginVertical(); GUILayout.Label("<b>" + m.who + "</b>", g.sLabel); g.Small(DateTimeOffset.FromUnixTimeMilliseconds(m.ts).LocalDateTime.ToString("yyyy.MM.dd")); GUILayout.EndVertical(); GUILayout.EndHorizontal();
                GUILayout.Label("<i>“" + m.prompt + "”</i>", g.sLabel);
                if (!string.IsNullOrEmpty(m.text)) g.Text(m.text);
                string mark = VoiceSync.Mark(g, m); if (mark != null) g.Small(mark);
                GUILayout.BeginHorizontal();
                bool onServer = VoiceSync.remote != null && VoiceSync.remote.Contains(m.id);
                if (!string.IsNullOrEmpty(m.audio) && (File.Exists(Path.Combine(MemDir, m.audio)) || onServer) && g.Btn(playing == m.id ? Loc.L("■ Зогсоох", "■ Stop") : Loc.L("► Сонсох", "► Listen"))) { var mm = m; g.Later(() => Play(g, mm)); }
                if (VoiceSync.state.TryGetValue(m.id, out var cs) && cs == "fail" && g.Btn(Loc.L("↻ Дахин илгээх", "↻ Retry"), false, 34)) { var mm = m; g.Later(() => { VoiceSync.state.Remove(mm.id); VoiceSync.Upload(g, mm); }); }
                if (g.Btn(Loc.L("Устгах", "Delete"), false, 34)) { var mm = m; g.Later(() => g.Open(new ConfirmModal(Loc.L("Энэ дурсамжийг устгах уу?", "Delete this memory?"), Loc.L("Буцаах боломжгүй.", "This cannot be undone."), () => { g.App.memories.Remove(mm); if (!string.IsNullOrEmpty(mm.audio)) { File.Delete(Path.Combine(MemDir, mm.audio)); VoiceSync.state.Remove(mm.id); if (mm.cloud == "up" || (VoiceSync.remote != null && VoiceSync.remote.Contains(mm.id))) g.StartCoroutine(VoiceSync.Delete(g, mm.id)); } g.App.Save(); }))); }
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
            }
        }
        void Play(Game g, Memory m)
        {
            if (closed) return; // a download that finished after the page closed
            if (player == null) player = g.gameObject.AddComponent<AudioSource>();
            if (playing == m.id) { player.Stop(); playing = null; return; }
            string path = Path.Combine(MemDir, m.audio);
            if (!File.Exists(path)) { if (!VoiceSync.state.ContainsKey(m.id)) g.StartCoroutine(VoiceSync.Download(g, m, ok => g.Later(() => { if (ok) Play(g, m); else g.Toast(Loc.L("Бичлэгийг татаж чадсангүй.", "Could not download the recording.")); }))); return; }
            var clip = Wav.Load(path); if (clip == null) return;
            player.clip = clip; player.Play(); playing = m.id;
        }
        public override void Closed(Game g) { closed = true; if (player != null) { player.Stop(); Destroy(player); } player = null; playing = null; }
    }
    sealed class MemoryForm : Modal
    {
        int pi = UnityEngine.Random.Range(0, 100), who; string text = ""; AudioClip rec; float recStart; bool recording; float[] samples; string status = "";
        static List<Tx> Prompts => Data.Get("people")["STORY_PROMPTS"].Select(x => x.T).ToList();
        public MemoryForm() { var fam = I.App.family; who = Mathf.Max(0, fam.FindIndex(f => f.id == "f3")); }
        public override void Press(Game g) { }
        public override void Draw(Game g)
        {
            var prompts = Prompts; var fam = g.App.family;
            g.Title(Loc.L("Шинэ дурсамж", "New memory"));
            GUILayout.Label(Loc.L("Асуулт", "Question"), g.sSmall);
            GUILayout.BeginHorizontal(g.sBtn); GUILayout.Label("<b>" + prompts[pi % prompts.Count] + "</b>", g.sLabel); if (GUILayout.Button(Loc.L("Өөр", "Other"), g.sBtn, GUILayout.Width(g.U(70)))) g.Later(() => pi++); GUILayout.EndHorizontal();
            GUILayout.Label(Loc.L("Хэн ярьж байна?", "Who is telling?"), g.sSmall);
            int hit = g.Chips(fam.Select(f => f.Name.ToString()).ToList(), who, 70); if (hit >= 0) who = hit;
#if UNITY_WEBGL && !UNITY_EDITOR
            GUILayout.Label(Loc.L("Дуу хоолой бичих нь утас, компьютерийн апп дээр ажиллана.", "Voice recording works in the phone and computer app."), g.sSmall); // no microphone in a web build
#else
            if (Microphone.devices.Length > 0)
            {
                GUILayout.Label(Loc.L("Дуу хоолой", "Voice"), g.sSmall);
                GUILayout.BeginHorizontal();
                if (g.Btn(recording ? Loc.L("■ Зогсоох", "■ Stop") : samples != null ? Loc.L("● Дахин бичих", "● Record again") : Loc.L("● Дуу хоолой бичих", "● Record voice"), recording)) g.Later(() => { if (recording) StopRec(); else StartRec(); });
                GUILayout.Label(recording ? $"<color=#e14b3d>● {(int)(Time.time - recStart) / 60}:{(int)(Time.time - recStart) % 60:00}</color>" : status, g.sLabel);
                GUILayout.EndHorizontal();
                if (recording && Time.time - recStart >= 300) StopRec();
            }
#endif
            GUILayout.Label(Loc.L("Тэмдэглэл", "Notes"), g.sSmall);
            text = GUILayout.TextArea(text, 2000, new GUIStyle(g.sBtn) { alignment = TextAnchor.UpperLeft, wordWrap = true }, GUILayout.MinHeight(g.U(80)));
            if (g.Btn(Loc.L("Хадгалах (+5 од)", "Save (+5 stars)"), true, 48)) g.Later(() => Save(g));
            if (g.Btn(Loc.L("Болих", "Cancel"), false, 32)) g.Later(g.Close);
        }
        void StartRec()
        {
#if UNITY_ANDROID
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone)) { UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone); status = Loc.L("Микрофоны зөвшөөрөл өгөөд дахин дар.", "Allow the microphone, then tap again."); return; }
#endif
#if !(UNITY_WEBGL && !UNITY_EDITOR)
            rec = Microphone.Start(null, false, 300, 22050); recStart = Time.time; recording = rec != null;
#endif
            if (!recording) status = Loc.L("Микрофон ашиглах зөвшөөрөл алга.", "Microphone permission denied.");
        }
        void StopRec()
        {
            if (!recording) return;
#if !(UNITY_WEBGL && !UNITY_EDITOR)
            int pos = Microphone.GetPosition(null); Microphone.End(null); recording = false;
#else
            int pos = 0; recording = false;
#endif
            if (rec != null && pos > 0) { samples = new float[pos * rec.channels]; rec.GetData(samples, 0); status = "√"; }
        }
        void Save(Game g)
        {
            if (recording) { g.Toast(Loc.L("Эхлээд бичлэгээ зогсоогоорой.", "Stop the recording first.")); return; }
            if (text.Trim() == "" && samples == null) { g.Toast(Loc.L("Бичвэр эсвэл дуу хоолой нэмээрэй.", "Add text or a voice recording.")); return; }
            var f = g.App.family[Mathf.Clamp(who, 0, g.App.family.Count - 1)]; string id = "m" + SaveData.NowMs(); string audio = null;
            if (samples != null) { audio = id + ".wav"; Wav.Save(Path.Combine(MemDir, audio), samples, rec.frequency, rec.channels); }
            var mem = new Memory { id = id, pid = g.V.id, ts = SaveData.NowMs(), d = SaveData.DayKey(), prompt = Prompts[pi % Prompts.Count].ToString(), who = f.Name.ToString(), whoFace = f.face, text = text.Trim(), audio = audio }; g.App.memories.Add(mem); if (audio != null) VoiceSync.Upload(g, mem);
            g.App.familyStars += 5; g.App.Save(); g.Close(); Sfx.Win(); g.Confetti(40); g.CheckAchievements();
            g.Toast(Loc.L("Дурсамж хадгалагдлаа! +5 од", "Memory saved! +5 stars"));
        }
        public override void Closed(Game g)
        {
#if !(UNITY_WEBGL && !UNITY_EDITOR)
            if (recording) Microphone.End(null);
#endif
        }
    }

    // ================================================================ about / pitch
    sealed class AboutPage : Page
    {
        protected override string Title => "Naadamy";
        protected override void Body(Game g)
        {
            GUILayout.BeginVertical(g.sPanel);
            g.Small("Pinequest · Fine-Tune for the Future");
            GUILayout.Label("<b>" + Loc.L("“Ээж ээ, тоглохоо больё. Хамтдаа монгол гэр барьцгаая!”", "“Mom, let’s stop playing — let’s build a Mongol ger together!”") + "</b>", g.sTitle);
            g.Text(Loc.L("Naadamy бол хүүхэд, гэр бүл, найзууд Монголын өв соёлыг хамтдаа сурдаг тоглоом. Дэлгэц дээр сурсан зүйлээ бодит амьдрал дээр гэр бүлээрээ хийдэг.", "Naadamy is a game where children, families and friends learn Mongolian heritage together, then do it for real at home."));
            GUILayout.EndVertical();
            Section(g, Loc.L("Асуудал", "The problem"), new[] {
                ("📱", Loc.L("Ганцаардмал дэлгэц", "Lonely screens"), Loc.L("Хүүхдүүд дэлгэцийн өмнө ганцаараа өнгөрүүлэх цаг ихэсч, гэр бүлийн харилцаа багасч байна.", "Children spend more time alone on screens and less with family.")),
                ("🏙️", Loc.L("Мартагдаж буй өв", "Fading heritage"), Loc.L("Хотын олон хүүхэд гэр барих, цагаан идээ хийх, Нууц товчоог мэддэггүй болж байна.", "Many city children no longer know how to build a ger, make dairy foods or the Secret History.")),
                ("💸", Loc.L("Үнэтэй, гадаад", "Expensive, foreign"), Loc.L("Ихэнх суралцах апп үнэтэй, гадаад хэл дээр, монгол соёлгүй.", "Most learning apps are costly, foreign and lack Mongolian culture.")) });
            Section(g, Loc.L("Шийдэл", "Our solution"), new[] {
                ("⛺", Loc.L("Нутаг", "Nutag"), Loc.L("Найзуудтайгаа хамт тоглох пиксел тосгон. Өөрийн монгол гэрээ барих, бөх барилдах. Нэг Wi-Fi-д олуулаа тоглоно.", "A pixel village to play in with friends. Build your own ger, wrestle at Naadam. Play together on the same Wi-Fi.")),
                ("🐕", Loc.L("Басар — AI найз", "Basar, the AI buddy"), Loc.L("Монгол банхар нохой үргэлж хажууд чинь. Түүх, Нууц товчоо, ёс заншлыг асуухад хариулна. Интернэтгүй ч ажиллана.", "A Mongolian Bankhar dog always by your side. Answers questions on history, the Secret History and customs. Works offline too.")),
                ("📚", Loc.L("Өв соёлын сургууль", "Heritage school"), Loc.L("Гэр барих, цагаан идээ хийх, гэрийн ёс, Нууц товчооны үлгэрүүд. Хичээл бүр гэр бүлийн бодит ажлаар төгсөнө.", "Building a ger, dairy foods, ger manners, tales from the Secret History. Every lesson ends with a real family activity.")) });
            Section(g, Loc.L("Яагаад хиймэл оюун үүнийг орлож чадахгүй вэ?", "Why AI can't replace this"), new[] {
                ("👨‍👩‍👧", Loc.L("Басар заана, гэр бүл хийнэ", "Basar teaches, family does"), Loc.L("AI нохой мэдлэг өгдөг ч гэрээ ээж аавтайгаа хамт барина.", "The AI dog shares knowledge, but you build the ger with Mom and Dad.")),
                ("🌍", Loc.L("Тоглоом дэлгэцээс гадна дуусна", "Every lesson ends off-screen"), Loc.L("Бяцхан гэр барих, тараг исгэх, таван сумны туршилт — гэр бүлээрээ.", "A mini ger, homemade yogurt, the five arrows test — all as a family.")),
                ("🎙️", Loc.L("Эмээгийн дуу хоолой", "Grandma's voice"), Loc.L("Ахмадуудын ярьсан ёс заншил, түүхийг Дурсамжийн номонд хадгална.", "Customs and stories from elders are saved in the memory book.")),
                ("🗣️", Loc.L("Басар “эмээгээсээ асуу” гэдэг", "Basar says “ask Grandma”"), Loc.L("Мэдэхгүй зүйлээ Басар ахмадуудаас асуухыг урамшуулдаг.", "When unsure, Basar encourages asking the elders.")) });
            Section(g, Loc.L("Хүн бүрт тэгш хүртээмж", "Equal access for everyone"), new[] {
                ("📶", "", Loc.L("Офлайн ажиллана: Басар интернэтгүй ч мэдлэгээ хуваалцана", "Works offline: Basar shares his knowledge even without internet")),
                ("📱", "", Loc.L("Хуучин, хямд утсан дээр ажиллана", "Runs on old, low-cost phones")),
                ("🇲🇳", "", Loc.L("Монгол хэл, монгол соёл дээр суурилсан", "Built on Mongolian language and culture")),
                ("🚫", "", Loc.L("Зар сурталчилгаагүй, өгөгдөл худалдахгүй", "No ads, no selling data")),
                ("🔒", "", Loc.L("Профайл зөвхөн таны төхөөрөмж дээр, AI-д нэр хадгалагдахгүй", "Profiles stay on your device; the AI keeps no names")),
                ("🧒", "", Loc.L("Хүүхдэд аюулгүй AI: хувийн мэдээлэл асуудаггүй", "Kid-safe AI: never asks for personal details")) });
            GUILayout.BeginVertical(g.sPanel);
            GUILayout.Label("<b>" + Loc.L("Шударга үнэ", "Fair pricing") + "</b>", g.sTitle);
            Price(g, Loc.L("Үнэгүй", "Free"), "0₮" + Loc.L(" / үүрд", " / forever"), new[] { Loc.L("Нутаг, бүх хичээл", "Nutag and every lesson"), Loc.L("Басарын офлайн мэдлэг", "Basar’s offline knowledge"), Loc.L("Дурсамжийн ном", "Memory book") }, null);
            Price(g, Loc.L("Гэр бүл", "Family"), "3,900₮" + Loc.L(" / сар", " / month"), new[] { Loc.L("Басар AI ухаалаг горим", "Basar smart AI mode"), Loc.L("Сар бүр шинэ өв соёлын хичээл", "New heritage lessons every month"), Loc.L("Хэвлэх гэр бүлийн картууд", "Printable family activity cards") }, Loc.L("Нэг гэр бүлийн төлбөр = хөдөөгийн нэг хүүхдэд үнэгүй эрх", "One family plan = free access for one rural child"));
            Price(g, Loc.L("Сургууль, сум", "Schools & soums"), "0₮" + Loc.L(" / үнэгүй", " / free"), new[] { Loc.L("Төрийн сургууль, цэцэрлэг", "Public schools & kindergartens"), Loc.L("Багшийн самбар", "Teacher dashboard"), Loc.L("Ангиараа Нутагт хамт тоглох", "Whole class plays together in Nutag") }, null);
            GUILayout.EndVertical();
            GUILayout.BeginVertical(g.sPanel);
            GUILayout.Label("<b>" + Loc.L("Цаашдын төлөвлөгөө", "Roadmap") + "</b>", g.sTitle);
            foreach (var (when, what) in new[] { (Loc.L("Одоо", "Now"), Loc.L("Нутаг, Басар AI нохой, өв соёлын хичээлүүд, Unity апп", "Nutag, Basar the AI dog, heritage lessons, the Unity app")), (Loc.L("Дараа", "Next"), Loc.L("Монгол бичиг, морин хуур, айраг бүлэх, Цагаан сарын хичээлүүд", "Lessons on Mongol script, morin khuur, airag and Tsagaan Sar")), (Loc.L("Удахгүй", "Soon"), Loc.L("Басарын дуу хоолой, өвөө эмээгийн бичсэн үлгэрүүд", "Basar’s voice and stories recorded by grandparents")), (Loc.L("Ирээдүй", "Future"), Loc.L("Сургууль, сум, музейтэй хамтран ажиллах", "Partnerships with schools, soums and museums")) })
                GUILayout.Label($"<b>{when}</b> — {what}", g.sLabel);
            GUILayout.EndVertical();
            if (Music.Credits.Count > 0)
            {
                GUILayout.BeginVertical(g.sPanel);
                GUILayout.Label("<b>" + Loc.L("Хөгжмийн зохиогчид", "Music credits") + "</b>", g.sTitle);
                g.Small(Loc.L("Арын хөгжим бүгд CC0 (нийтийн өмч) лицензтэй, OpenGameArt.org-оос.", "All background music is CC0 (public domain) from OpenGameArt.org."));
                foreach (var c in Music.Credits) GUILayout.Label($"<b>{c.title}</b> — {c.artist} · {c.license}", g.sSmall);
                GUILayout.EndVertical();
            }
            g.Small(Loc.L("Монгол хүүхэд бүрт ♥ · Pinecone Academy · Pinequest", "For every Mongolian child ♥ · Pinecone Academy · Pinequest"));
        }
        static void Section(Game g, string title, (string icon, string head, string text)[] items)
        {
            GUILayout.BeginVertical(g.sPanel); GUILayout.Label("<b>" + title + "</b>", g.sTitle);
            foreach (var (icon, head, text) in items) { g.Row(Emo.Icon(icon), head == "" ? text : head, head == "" ? null : text); g.EndRow(); }
            GUILayout.EndVertical();
        }
        static void Price(Game g, string name, string amount, string[] items, string note)
        {
            GUILayout.BeginVertical(note != null ? g.sPrimary : g.sBtn);
            var st = note != null ? g.sPrimary.Clean() : g.sLabel;
            GUILayout.Label($"<b>{name}</b>  {amount}", st);
            foreach (var i in items) GUILayout.Label("√ " + i, new GUIStyle(st) { fontSize = g.Ts(11) });
            if (note != null) GUILayout.Label(note, new GUIStyle(st) { fontSize = g.Ts(10) });
            GUILayout.EndVertical();
        }
    }
}

/** 16-bit PCM WAV files for the memory book's recordings. */
public static class Wav
{
    public static void Save(string path, float[] samples, int rate, int channels)
    {
        using (var w = new BinaryWriter(File.Create(path)))
        {
            int bytes = samples.Length * 2;
            w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + bytes); w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
            w.Write(16); w.Write((short)1); w.Write((short)channels); w.Write(rate); w.Write(rate * channels * 2); w.Write((short)(channels * 2)); w.Write((short)16);
            w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write(bytes);
            foreach (var s in samples) w.Write((short)Mathf.Clamp(s * 32767f, -32768f, 32767f));
        }
    }
    public static AudioClip Load(string path)
    {
        try
        {
            var b = File.ReadAllBytes(path); int channels = BitConverter.ToInt16(b, 22), rate = BitConverter.ToInt32(b, 24), dataLen = BitConverter.ToInt32(b, 40), n = dataLen / 2;
            var data = new float[n]; for (int i = 0; i < n; i++) data[i] = BitConverter.ToInt16(b, 44 + i * 2) / 32768f;
            var clip = AudioClip.Create("memory", n / channels, channels, rate, false); clip.SetData(data, 0); return clip;
        }
        catch (Exception) { return null; }
    }
}
