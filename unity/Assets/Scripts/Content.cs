// Нутаг content from js/village/rules.js and village.js: quests, shop, fish, food, wrestling, horses, archery, townspeople, Grandma.
using System.Collections.Generic;
using System.Linq;

public static partial class Rules
{
    public static readonly string[] SKINS = { "#F6D3B3", "#EDBB93", "#D9A06E", "#B97A4A" };
    public static readonly string[] DEELS = { "blue", "red", "green", "purple", "pink", "gold", "teal", "brown", "white", "black" };

    // ---------------------------------------------------------------- daily quests
    public sealed class Quest { public int n, c, x; public Tx t; }
    public static readonly Dictionary<string, Quest> QUESTS = new Dictionary<string, Quest>
    {
        ["milk"] = new Quest { n = 3, c = 20, x = 25, t = new Tx("3 удаа мал саах", "Milk the animals 3 times") },
        ["dairy"] = new Quest { n = 2, c = 25, x = 35, t = new Tx("2 цагаан идээ хийх", "Make 2 dairy foods") },
        ["fish"] = new Quest { n = 2, c = 25, x = 30, t = new Tx("2 загас барих", "Catch 2 fish") },
        ["basar"] = new Quest { n = 2, c = 15, x = 25, t = new Tx("Басараас 2 асуулт асуух", "Ask Basar 2 questions") },
        ["quiz"] = new Quest { n = 3, c = 25, x = 35, t = new Tx("Басарын 3 асуултад зөв хариулах", "Answer 3 of Basar’s questions right") },
        ["lesson"] = new Quest { n = 1, c = 30, x = 40, t = new Tx("Өв соёлын 1 хичээл дуусгах", "Finish 1 heritage lesson") },
        ["together"] = new Quest { n = 1, c = 40, x = 50, t = new Tx("Гэр бүлтэйгээ бодит ажил хийх", "Do a real activity with family") },
        ["pet"] = new Quest { n = 3, c = 10, x = 20, t = new Tx("Хонийг 3 удаа илбэх", "Pet the sheep 3 times") },
        ["sell"] = new Quest { n = 5, c = 15, x = 20, t = new Tx("5 зүйл зарах", "Sell 5 items") },
        ["race"] = new Quest { n = 1, c = 25, x = 35, t = new Tx("Морин уралдаанд 1 удаа оролцох", "Ride in 1 horse race") },
        ["archery"] = new Quest { n = 3, c = 25, x = 35, t = new Tx("Сур харваанд 3 бай онох", "Hit 3 archery targets") },
        ["wrestle"] = new Quest { n = 1, c = 25, x = 35, t = new Tx("Бөхийн барилдаанд 1 удаа давах", "Win 1 wrestling round") },
        ["etiquette"] = new Quest { n = 1, c = 30, x = 40, t = new Tx("Эмээгийн гэрт ёсоор зочлох", "Visit Grandma’s ger with good manners") },
        ["defend"] = new Quest { n = 1, c = 40, x = 60, t = new Tx("Нутгаа хамгаалах тулаанд ялах", "Win a defend-the-homeland battle") },
        ["eat"] = new Quest { n = 3, c = 15, x = 20, t = new Tx("3 удаа хоол идэх, ундаа уух", "Eat or drink 3 times") },
        ["stamp"] = new Quest { n = 1, c = 35, x = 45, t = new Tx("Аймагт аялж тамга авах", "Earn a province stamp") },
    };
    public static readonly string[] POOL = { "milk", "dairy", "milk", "fish", "basar", "quiz", "pet", "sell", "basar", "quiz", "eat" };

    // ---------------------------------------------------------------- fish
    public sealed class Fish { public string id; public Tx name, fact; public int sell, xp, coins, diff, w; public bool release; }
    public static readonly Dictionary<string, Fish> FISH = new Dictionary<string, Fish>
    {
        ["perch"] = new Fish { id = "perch", name = new Tx("Алгана", "Perch"), sell = 15, xp = 8, diff = 1, w = 50, fact = new Tx("Алгана Монголын олон нуур, голд түгээмэл байдаг.", "Perch are common in many Mongolian lakes and rivers.") },
        ["pike"] = new Fish { id = "pike", name = new Tx("Цурхай", "Pike"), sell = 30, xp = 14, diff = 2, w = 28, fact = new Tx("Цурхай бол хурдан сэлдэг махчин загас.", "Pike are fast-swimming predators.") },
        ["lenok"] = new Fish { id = "lenok", name = new Tx("Зэвэг", "Lenok"), sell = 45, xp = 20, diff = 3, w = 16, fact = new Tx("Зэвэг цэвэр, хүйтэн уулын голд амьдардаг.", "Lenok live in clean, cold mountain rivers.") },
        ["taimen"] = new Fish { id = "taimen", name = new Tx("Хадран", "Taimen"), sell = 0, xp = 60, coins = 50, diff = 4, w = 6, release = true,
            fact = new Tx("Хадран бол дэлхийн хамгийн том хулд загасны төрөл. Монголд хамгаалагдсан тул барьсан бол буцааж тавина!", "Taimen is the largest salmon species in the world. It is protected in Mongolia, so we always release it!") },
    };

    // ---------------------------------------------------------------- food: energy for running, and a taste of Mongolian food
    public sealed class Food { public string id, kind; public Tx name, fact; public int energy, price; }
    static Food F(string id, string kind, string mn, string en, int energy, int price, string fmn, string fen) => new Food { id = id, kind = kind, name = new Tx(mn, en), energy = energy, price = price, fact = new Tx(fmn, fen) };
    public static readonly Food[] FOODS =
    {
        F("buuz", "meat", "Бууз", "Buuz", 30, 12, "Бууз бол Цагаан сарын гол хоол. Айл бүр олон зуун бууз чимхдэг.", "Buuz are the main dish of Tsagaan Sar. Families pinch hundreds of them."),
        F("khuushuur", "meat", "Хуушуур", "Khuushuur", 25, 10, "Хуушуур бол бууз шиг махтай боов, гэхдээ тосонд шардаг. Наадмаар хамгийн их иддэг.", "Khuushuur are like buuz but fried flat in oil. Everyone eats them at Naadam."),
        F("khorkhog", "meat", "Хорхог", "Khorkhog", 45, 25, "Хорхогийг махыг халуун чулуутай хамт саванд битүү жигнэж хийдэг. Баяр ёслолоор хийнэ.", "Khorkhog is meat cooked with hot stones in a closed pot, made for celebrations."),
        F("tsuivan", "meat", "Цуйван", "Tsuivan", 35, 14, "Цуйван: гоймон, мах, ногоог хуурч жигнэдэг. Олон хүн үндэсний хоол гэж нэрлэдэг.", "Tsuivan is noodles stir-fried with meat and vegetables, often called the national dish."),
        F("shul", "meat", "Гурилтай шөл", "Noodle soup", 30, 12, "Гурилтай шөл: мах, төмс, лууван, гоймонтой. Хүйтэн өвөл дулаацуулна.", "Guriltai shul has meat, potato, carrot and noodles. It warms you up in winter."),
        F("borts", "meat", "Борц", "Borts", 20, 8, "Борц бол өвөл салхинд хатаасан мах. Нунтаглаад халуун усанд хийж шөл болгодог.", "Borts is meat dried in the winter wind. Crushed into hot water, it becomes soup."),
        F("salad", "veg", "Ногоон салат", "Vegetable salad", 15, 8, "Байцаа, лууван, өргөст хэмхээр хийсэн салат витаминаар баян.", "A salad of cabbage, carrot and cucumber is full of vitamins."),
        F("tarvas", "veg", "Тарвас", "Watermelon", 15, 10, "Ховд аймгийн Мянгад сумын тарвас алдартай.", "Watermelons from Myangad in Khovd province are famous."),
        F("aaruul", "milk", "Ааруул", "Aaruul", 10, 3, "Ааруулыг ээдмийг нарны гэрэлд хатааж хийдэг. Удаан хадгалагддаг.", "Aaruul is milk curd dried in the sun. It keeps for a very long time."),
        F("boortsog", "milk", "Боорцог", "Boortsog", 12, 4, "Боорцог бол тосонд шарсан гурилан боов. Цагаан сараар өндөр ул боов засдаг.", "Boortsog are fried dough cookies. At Tsagaan Sar they are stacked into a tall ul boov."),
        F("urum", "milk", "Өрөм", "Öröm cream", 15, 6, "Өрөм бол сүүг зөөлөн буцалгаж хөргөхөд дээр нь тогтдог өтгөн цөцгий.", "Öröm is the thick cream that forms when milk is gently boiled and left to cool."),
        F("tarag", "milk", "Тараг", "Tarag yogurt", 15, 6, "Тараг бол исгэсэн сүү. Сарлагийн сүүгээр хийсэн тараг их өтгөн байдаг.", "Tarag is a yogurt. Made from yak milk, it is extra thick."),
        F("baursak", "milk", "Баурсак", "Baursak", 15, 6, "Баурсак бол казахуудын тосонд шарсан зөөлөн боов. Зочинд цайтай хамт тавьдаг.", "Baursak are soft fried dough balls from Kazakh families, served to guests with tea."),
        F("tsai", "drink", "Сүүтэй цай", "Milk tea", 15, 4, "Монгол сүүтэй цайнд бага зэрэг давс хийдэг! Зочинд хамгийн түрүүнд цай өргөнө.", "Mongolian milk tea has a pinch of salt! Guests are always offered tea first."),
        F("airag", "drink", "Айраг", "Airag", 20, 10, "Айргийг гүүний сүүг хөхүүрт мянга гаруй удаа бүлж исгэдэг. Исгэлэн ундаа тул бага багаар уудаг.", "Airag is mare’s milk churned over a thousand times in the khukhuur. It is sour and fermented, so drink only a little."),
        F("khoormog", "drink", "Хоормог", "Khoormog", 20, 12, "Хоормог бол тэмээний сүүгээр исгэсэн говийн ундаа. Айргаас ч илүү тэжээллэг.", "Khoormog is fermented camel milk from the Gobi, even richer than airag."),
        F("chatsargana", "drink", "Чацарганы шүүс", "Sea buckthorn juice", 15, 8, "Чацаргана С витаминаар маш баян. Увс нутагт элбэг ургадаг.", "Sea buckthorn is packed with vitamin C. It grows thickly in Uvs."),
        F("us", "drink", "Цэвэр ус", "Clean water", 5, 0, "Ус бол амьдралын эх. Монголчууд ус, голоо бохирдуулдаггүй, хайрлан гамнадаг.", "Water is the source of life. Mongolians never dirty their rivers and use water with care."),
    };
    public static Food FoodById(string id) => FOODS.FirstOrDefault(f => f.id == id);
    public static readonly (string id, Tx name)[] FOOD_TABS = { ("meat", new Tx("Мах", "Meat")), ("veg", new Tx("Ногоо, жимс", "Veg & fruit")), ("milk", new Tx("Цагаан идээ", "Dairy")), ("drink", new Tx("Уух", "Drinks")) };

    // ---------------------------------------------------------------- shop catalogue, furniture
    public sealed class Item { public string id, cat, slot, val; public int price, lvl; public Tx name; }
    static Item I(string id, string cat, string slot, string val, int price, int lvl, string mn, string en) => new Item { id = id, cat = cat, slot = slot, val = val, price = price, lvl = lvl, name = new Tx(mn, en) };
    public static readonly Item[] ITEMS =
    {
        I("deel-blue", "wear", "deel", "blue", 0, 1, "Цэнхэр дээл", "Blue deel"),
        I("deel-red", "wear", "deel", "red", 50, 1, "Улаан дээл", "Red deel"),
        I("deel-green", "wear", "deel", "green", 50, 1, "Ногоон дээл", "Green deel"),
        I("deel-pink", "wear", "deel", "pink", 60, 2, "Ягаан дээл", "Pink deel"),
        I("deel-purple", "wear", "deel", "purple", 90, 2, "Нил ягаан дээл", "Purple deel"),
        I("deel-gold", "wear", "deel", "gold", 140, 3, "Алтан шар дээл", "Golden deel"),
        I("hat-cap", "wear", "hat", "cap", 40, 1, "Малгай", "Cap"),
        I("hat-flower", "wear", "hat", "flower", 45, 1, "Цэцгэн титэм", "Flower crown"),
        I("hat-mongol", "wear", "hat", "mongol", 70, 2, "Монгол малгай", "Mongolian hat"),
        I("hat-loovuuz", "wear", "hat", "loovuuz", 120, 3, "Лоовууз", "Loovuuz fur hat"),
        I("hat-crown", "wear", "hat", "crown", 300, 6, "Алтан титэм", "Golden crown"),
        I("face-glasses", "wear", "face", "glasses", 50, 1, "Нүдний шил", "Glasses"),
        I("face-sun", "wear", "face", "sun", 80, 2, "Нарны шил", "Sunglasses"),
        I("neck-scarf", "wear", "neck", "scarf", 40, 1, "Ороолт", "Scarf"),
        I("neck-khadag", "wear", "neck", "khadag", 70, 2, "Хадаг", "Khadag"),
        I("neck-medal", "wear", "neck", "medal", 200, 5, "Наадмын медаль", "Naadam medal"),
        I("pet-lamb", "pet", "pet", "lamb", 150, 2, "Хурга", "Lamb"),
        I("pet-kitten", "pet", "pet", "kitten", 180, 2, "Муур", "Kitten"),
        I("pet-puppy", "pet", "pet", "puppy", 220, 3, "Банхар гөлөг", "Bankhar puppy"),
        I("pet-camel", "pet", "pet", "camel", 260, 4, "Ботго", "Baby camel"),
        I("pet-foal", "pet", "pet", "foal", 300, 5, "Унага", "Foal"),
        I("rod-2", "tool", "rod", "2", 150, 3, "Сайн загасны саваа", "Better fishing rod"),
    };
    public static string ItemIcon(Item it) => it.slot == "deel" ? "deel:" + it.val : it.cat == "pet" ? "pet:" + it.val : it.cat == "tool" ? "tool:" + it.id : "wear:" + it.slot + "-" + it.val;

    public sealed class Furn { public string id; public int price, lvl; public Tx name, fact; }
    public static readonly Furn[] FURNITURE =
    {
        new Furn { id = "tulga", price = 60, lvl = 1, name = new Tx("Тулга (зуух)", "Stove (tulga)"), fact = new Tx("Гэрийн голд тулга буюу зуух байрладаг. Энэ бол гэр бүлийн галын голомт.", "The stove sits in the center of the ger. It is the heart of the family hearth.") },
        new Furn { id = "shirdeg", price = 80, lvl = 1, name = new Tx("Ширдэг", "Felt rug (shirdeg)"), fact = new Tx("Ширдэг бол хээ угалзаар оёсон эсгий хивс.", "A shirdeg is a felt rug stitched with traditional patterns.") },
        new Furn { id = "or", price = 90, lvl = 2, name = new Tx("Ор", "Bed"), fact = new Tx("Гэрийн ор ихэвчлэн баруун, зүүн хананы дагуу байдаг.", "Ger beds usually stand along the side walls.") },
        new Furn { id = "avdar", price = 110, lvl = 2, name = new Tx("Авдар", "Chest (avdar)"), fact = new Tx("Авдарт гэр бүлийн үнэт зүйлсийг хадгалдаг. Хойморт тавьдаг.", "The avdar chest keeps family treasures. It stands at the back of the ger (khoimor).") },
        new Furn { id = "morinkhuur", price = 150, lvl = 3, name = new Tx("Морин хуур", "Horsehead fiddle"), fact = new Tx("Морин хуурыг гэрийн хойморт хүндэтгэлтэй байршуулдаг.", "The morin khuur is placed with honor at the back of the ger.") },
        new Furn { id = "shagai", price = 40, lvl = 1, name = new Tx("Шагайн уут", "Bag of shagai"), fact = new Tx("Урт өвлийн оройгоор гэр бүлээрээ шагай тоглодог.", "Families play shagai on long winter evenings.") },
    };

    // ---------------------------------------------------------------- wrestling
    public sealed class Title { public string id; public Tx name, fact; public int need; }
    public static readonly Title[] TITLES =
    {
        new Title { id = "nachin", name = new Tx("Начин", "Falcon"), need = 5, fact = new Tx("Начин (шонхор) цол Улсын наадамд 5 давж авдаг.", "The Nachin (falcon) title is earned by winning 5 rounds at the State Naadam.") },
        new Title { id = "khartsaga", name = new Tx("Харцага", "Hawk"), need = 6, fact = new Tx("Харцага (хартсага шувуу) цол 6 давсан бөхөд олгогддог.", "The Khartsaga (hawk) title goes to a wrestler who wins 6 rounds.") },
        new Title { id = "zaan", name = new Tx("Заан", "Elephant"), need = 7, fact = new Tx("Заан цол 7 давсан, хүчирхэг бөхийн цол.", "The Zaan (elephant) title is for winning 7 rounds.") },
        new Title { id = "garid", name = new Tx("Гарьд", "Garuda"), need = 8, fact = new Tx("Гарьд бол домгийн хүчит шувуу. 8 давсан бөхөд олгодог.", "The Garid is a mighty mythical bird, for winning 8 rounds.") },
        new Title { id = "arslan", name = new Tx("Арслан", "Lion"), need = 9, fact = new Tx("Наадамд түрүүлсэн бөх Арслан цол хүртэнэ.", "The wrestler who wins the whole Naadam becomes an Arslan (lion).") },
        new Title { id = "avarga", name = new Tx("Аварга", "Titan"), need = 10, fact = new Tx("Хоёр удаа түрүүлсэн Арслан Аварга цол хүртдэг. Монголын хамгийн дээд бөхийн цол!", "An Arslan who wins twice becomes an Avarga (titan), the highest wrestling title in Mongolia!") },
    };
    public static Title TitleById(string id) => TITLES.FirstOrDefault(t => t.id == id);
    public static string TitleFor(WrSave wr) { if (wr == null) return null; string t = null; foreach (var x in TITLES) if (x.id == "avarga" ? wr.champ >= 2 : wr.best >= x.need) t = x.id; return t; }

    // Wrestling techniques (мэх) in a circle: each beats the next half of the circle and loses to the half before it.
    public sealed class Move { public string id; public Tx name, fact; public string[] beats; }
    static Move M(string id, string mn, string en, string fmn, string fen) => new Move { id = id, name = new Tx(mn, en), fact = new Tx(fmn, fen) };
    public static readonly Move[] MOVES = MakeMoves();
    static Move[] MakeMoves()
    {
        var m = new[]
        {
            M("push", "Түлхэх", "Push", "Өрсөлдөгчөө мөрөөрөө түлхэж тэнцвэрийг нь алдагдуулна.", "Drive into your opponent with your shoulder to break their balance."),
            M("pull", "Татах", "Pull", "Зодгоос нь татаж урагш унагана.", "Pull them by the zodog so they fall forward."),
            M("hook", "Гадуур дэгээ", "Outside hook", "Хөлөө өрсөлдөгчийн хөлийн гадуур дэгээдэж унагадаг.", "Hook your leg around the outside of theirs and trip them."),
            M("inhook", "Дотуур дэгээ", "Inside hook", "Хөлийн дотуур дэгээдэж хойш нь унагадаг мэх.", "Hook the inside of their leg to topple them backwards."),
            M("hip", "Ташаадах", "Hip throw", "Өрсөлдөгчөө ташаан дээгүүрээ эргүүлэн шиддэг.", "Turn and throw them over your hip."),
            M("leg", "Хөл шүүрэх", "Leg grab", "Бөхийн хөлийг шүүрэн авч өргөөд унагадаг.", "Grab their leg, lift it and bring them down."),
            M("lift", "Өргөж хаях", "Lift and throw", "Хамгийн хүчтэй мэх: өрсөлдөгчөө газраас өргөөд хаядаг.", "The strongest move: lift them off the ground and throw."),
            M("havirah", "Хавирах", "Khavirakh (leg sweep)", "Хөлөөрөө өрсөлдөгчийн хөлийг хавирч, тэнцвэрийг нь алдагдуулан унагадаг мэх.", "Sweep their leg away with yours so they lose balance and fall."),
            M("humruh", "Хөмрөх", "Khömrökh (overturn)", "Өрсөлдөгчөө урагш нь хөмрүүлж, эргүүлэн унагадаг хүчтэй мэх.", "A powerful move that tips your opponent forward and over."),
            M("toigdoh", "Тойгдох", "Toigdokh", "Хав дөрвөлжин барьцнаас хийдэг онцгой сайхан мэх. Тэнцвэр, байрлал чухал.", "A fine technique done from the square grip. Balance and position matter most."),
            M("hongodoh", "Хонгодох", "Khongodokh (leg take)", "Өрсөлдөгчийн хөлийг авч унагадаг, жүдогийн мэхтэй төстэй хөлийн мэх.", "A leg technique that takes their leg away, a little like a judo throw."),
            M("suga", "Сугадах", "Sugadakh (armpit throw)", "Өрсөлдөгчийн сугаар орж, мөрөн дээгүүрээ эргүүлэн хаядаг мэх.", "Get in under their arm and turn them over your shoulder."),
            M("tahim", "Тахимдах", "Takhimdakh (knee grab)", "Өрсөлдөгчийн өвдөгний ар талаас шүүрэн татаж унагадаг мэх.", "Grab behind their knee and pull them down."),
        };
        int half = (m.Length - 1) / 2;
        for (int i = 0; i < m.Length; i++) m[i].beats = Enumerable.Range(0, half).Select(k => m[(i + k + 1) % m.Length].id).ToArray();
        return m;
    }
    public static Move MoveById(string id) => MOVES.First(m => m.id == id);
    public static bool Beats(string x, string y) { var m = MOVES.FirstOrDefault(q => q.id == x); return m != null && m.beats.Contains(y); }
    /** One exchange: 1 if move a wins, -1 if b wins, 0 even. How well each was done (power 0..1) can turn a bad match-up. */
    public static int Exchange(string ma, float pa, string mb, float pb)
    {
        float Base(string x, string y) => x == y ? 0.3f : Beats(x, y) ? 0.6f : 0;
        float A = Base(ma, mb) + 0.8f * UnityEngine.Mathf.Clamp01(pa), B = Base(mb, ma) + 0.8f * UnityEngine.Mathf.Clamp01(pb);
        return System.Math.Abs(A - B) < 0.04f ? 0 : A > B ? 1 : -1;
    }
    public static readonly string[] WR_NAMES = { "Ховдын Батбаяр", "Увсын Ганзориг", "Архангайн Тэмүүлэн", "Дорнодын Энхбат", "Говь-Алтайн Сүхбат", "Сэлэнгийн Мөнх-Эрдэнэ", "Баян-Өлгийн Ержан", "Хэнтийн Төмөрбаатар", "Завханы Оюунболд", "Өвөрхангайн Даваадорж", "Булганы Цогтбаатар", "Сүхбаатарын Ууганбаяр" };
    public static readonly Tx[] WR_FACTS =
    {
        new Tx("Бөхчүүд барилдааны өмнө, дараа бүргэд мэт дэвж хөдөлдөг. Үүнийг \"дэвээ\" гэнэ.", "Wrestlers do an eagle dance (devee) before and after each bout."),
        new Tx("Засуул бөхийн цолыг дуудаж, магтаал хэлдэг бөгөөд барилдааны үеэр бөхийн малгайг барьж зогсоно.", "The zasuul (herald) sings the wrestler's titles and holds his hat during the bout."),
        new Tx("Монгол бөхөд жингийн ангилал байдаггүй. Жижиг бөх ч том бөхийг мэхээрээ дийлж болно.", "Mongolian wrestling has no weight classes. A small wrestler can beat a big one with good technique."),
        new Tx("Тохой, өвдөг, нуруу эсвэл толгой газарт хүрвэл унасанд тооцогдоно.", "A wrestler loses when an elbow, knee, back or head touches the ground."),
        new Tx("Бөхийн хувцас нь зодог (энгэрээ задгай цамц), шуудаг (богино өмд), гутал гурав.", "The wrestling outfit is the zodog (open-chest jacket), the shuudag (briefs) and gutal boots."),
        new Tx("Улсын наадамд 512 эсвэл 1024 бөх барилддаг.", "At the State Naadam, 512 or 1024 wrestlers compete."),
    };
    // what touches the ground after each move
    public static readonly Dictionary<string, Tx> WR_PART = new Dictionary<string, Tx>
    {
        ["push"] = new Tx("нуруу", "back"), ["pull"] = new Tx("өвдөг", "knee"), ["hook"] = new Tx("нуруу", "back"), ["inhook"] = new Tx("нуруу", "back"), ["hip"] = new Tx("мөр", "shoulder"), ["leg"] = new Tx("тохой", "elbow"), ["lift"] = new Tx("нуруу", "back"),
        ["havirah"] = new Tx("ташаа", "hip"), ["humruh"] = new Tx("нуруу", "back"), ["toigdoh"] = new Tx("мөр", "shoulder"), ["hongodoh"] = new Tx("нуруу", "back"), ["suga"] = new Tx("мөр", "shoulder"), ["tahim"] = new Tx("өвдөг", "knee"),
    };
    // each technique is performed with its own skill: footwork combos, rapid power, hold-and-release, balance, or timing
    public static readonly Dictionary<string, string> SKILL = new Dictionary<string, string> { ["push"] = "timing", ["hip"] = "timing", ["tahim"] = "timing", ["pull"] = "hold", ["leg"] = "hold", ["suga"] = "hold", ["hook"] = "combo", ["inhook"] = "combo", ["havirah"] = "combo", ["lift"] = "mash", ["humruh"] = "mash", ["toigdoh"] = "balance", ["hongodoh"] = "balance" };
    public static readonly string[] OPP_TITLE = { "", "", "", "", "nachin", "khartsaga", "zaan", "garid", "avarga", "arslan" }; // round 8: Avarga Setsen
    public const string SETSEN = "Сэцэн";

    // ---------------------------------------------------------------- horses
    public static readonly Dictionary<string, Tx> COAT_NAMES = new Dictionary<string, Tx> { ["bay"] = new Tx("Хээр", "Bay"), ["black"] = new Tx("Хар", "Black"), ["white"] = new Tx("Цагаан", "White"), ["chestnut"] = new Tx("Зээрд", "Chestnut"), ["gray"] = new Tx("Саарал", "Gray") };
    public static readonly Tx[] HORSE_FACTS =
    {
        new Tx("Монгол адуу намхан ч маш тэсвэртэй. Өвлийн хүйтэнд ч гадаа бэлчдэг.", "Mongolian horses are short but incredibly tough. They graze outside even in winter."),
        new Tx("Наадамд түрүүлсэн морийг “түрүү морь”, эхний тавд орсныг “айргийн тав” гэдэг.", "At Naadam the winning horse is the “türüü mori”, and the top five are the “airgiin tav”."),
        new Tx("Хамгийн сүүлд ирсэн даага “баян ходоод” гэж магтуулж, ирэх жил сайн давхина гэж ерөөдөг.", "The last young horse is praised as “bayan khodood” (full belly) and wished a better race next year."),
        new Tx("Наадмын морь уралдаанд 5–12 насны хүүхэд унаач 10–30 км давхидаг.", "In Naadam races, child jockeys aged 5–12 ride 10–30 km."),
        new Tx("Монголд хүнээс олон адуу бий. 3 сая гаруй адуутай!", "Mongolia has more horses than people: over 3 million!"),
    };
    public static readonly string[] RIDERS = { "Бат", "Сараа", "Тэмүүжин", "Номин", "Ганбаатар", "Энхжин", "Мөнх", "Ану" };
    // Naadam race classes by the horse's age (real Naadam categories). Higher classes ride more laps against faster rivals.
    public sealed class RaceClass { public string id; public Tx name, age, fact; public int laps; public float pace; }
    public static readonly RaceClass[] CLASSES =
    {
        new RaceClass { id = "daaga", name = new Tx("Даага", "Daaga"), age = new Tx("2 настай", "2 years"), laps = 1, pace = 5.2f, fact = new Tx("Даага бол 2 настай морь. Наадмын хамгийн бага насны уралдаан.", "A daaga is a 2-year-old horse: the youngest Naadam race.") },
        new RaceClass { id = "shudlen", name = new Tx("Шүдлэн", "Shüdlen"), age = new Tx("3 настай", "3 years"), laps = 1, pace = 5.55f, fact = new Tx("Шүдлэн бол 3 настай морь.", "A shüdlen is a 3-year-old horse.") },
        new RaceClass { id = "khyazaalan", name = new Tx("Хязаалан", "Khyazaalan"), age = new Tx("4 настай", "4 years"), laps = 2, pace = 5.75f, fact = new Tx("Хязаалан бол 4 настай морь.", "A khyazaalan is a 4-year-old horse.") },
        new RaceClass { id = "soyolon", name = new Tx("Соёолон", "Soyolon"), age = new Tx("5 настай", "5 years"), laps = 2, pace = 5.95f, fact = new Tx("Соёолон бол 5 настай морь.", "A soyolon is a 5-year-old horse.") },
        new RaceClass { id = "ikhnas", name = new Tx("Их нас", "Ikh nas"), age = new Tx("6+ настай", "6+ years"), laps = 3, pace = 6.15f, fact = new Tx("Их насны морьд хамгийн урт зайд, 25 гаруй км давхидаг.", "Ikh nas horses (6+) race the longest distance, over 25 km.") },
        new RaceClass { id = "azarga", name = new Tx("Азарга", "Azarga"), age = new Tx("азарга", "stallions"), laps = 3, pace = 6.35f, fact = new Tx("Азаргын уралдаанд адууны сүргийн эзэн азарганууд уралддаг.", "In the azarga race, the stallions who lead the herds compete.") },
    };

    // ---------------------------------------------------------------- archery
    public static readonly Tx[] ARCH_FACTS =
    {
        new Tx("Бай онох бүрт шүүгчид гараа өргөн “Уухай!” гэж дуулдаг.", "Each time the target is hit, the judges raise their arms and sing “Uukhai!”."),
        new Tx("Сур харваанд эрэгтэйчүүд 75 метр, эмэгтэйчүүд 60 метрээс харвадаг.", "In Mongolian archery men shoot from 75 metres and women from 60 metres."),
        new Tx("Бай нь олон жижиг “сур”-ыг хана шиг өрсөн хэлбэртэй.", "The target is a low wall made of many small leather “sur”."),
        new Tx("Чингисийн чулууны бичээст Есүнгэ мэргэн 335 алд газар харвасан тухай бичсэн байдаг.", "The Chinggis Stone records that the archer Yesüngge shot an arrow 335 alds (over 500 m)."),
        new Tx("Шилдэг харваачдыг “мэргэн” гэж цоллодог.", "The best archers earn the title “mergen” (sharpshooter)."),
    };
    public static readonly (int need, Tx t)[] ARCH_TITLES = { (12, new Tx("Улсын мэргэн", "State Sharpshooter")), (9, new Tx("Гарамгай мэргэн", "Excellent Sharpshooter")), (6, new Tx("Мэргэн", "Sharpshooter")) };

    // ---------------------------------------------------------------- townspeople with quest chains
    public sealed class NQ { public string k; public int n, c, x; public Tx t; }
    public sealed class Npc { public string id; public Tx name, hello, done, tic, bio; public Look look; public NQ[] chain; }
    static NQ Q(string k, int n, string mn, string en, int c, int x) => new NQ { k = k, n = n, t = new Tx(mn, en), c = c, x = x };
    public static readonly Npc[] NPCS =
    {
        new Npc { id = "herder", name = new Tx("Малчин Батбаяр", "Herder Batbayar"), look = new Look { deel = "brown", skin = "#D9A06E", hat = "mongol" },
            hello = new Tx("Сайн уу, хүү/охин минь! Мал маллах амаргүй ч сайхан ажил шүү.", "Hello, child! Herding is hard work, but wonderful."),
            chain = new[] { Q("pet", 3, "Хониддоо дасгая: тэднийг 3 удаа илбээрэй.", "Help the sheep get to know you: pet them 3 times.", 20, 25), Q("milk", 2, "Хот айлд 2 удаа мал саагаад ирээрэй.", "Milk the animals in the dairy yard twice.", 25, 30), Q("etiquette", 1, "Эмээгийн гэрт ёсоор зочлоод ир. Малчин хүн ёсоо мэддэг байх ёстой!", "Visit Grandma’s ger with good manners. A herder knows the customs!", 35, 40) },
            done = new Tx("Чи жинхэнэ малчин болох нь! Малчид хонь бүрээ таньдаг, гэр бүл шиг хайрладаг.", "You’ll make a real herder! Herders know every sheep and love them like family."),
            tic = new Tx("Аа, ", "Ah, "), bio = new Tx("Аа, би 300 хоньтой. Өглөө бүр нараас өмнө босож малаа бэлчээрт гаргадаг. Хонь бүрийг нэрээр нь мэднэ шүү!", "Ah, I have 300 sheep. Every morning I’m up before the sun to take them to pasture. I know each one by name!") },
        new Npc { id = "fisher", name = new Tx("Загасчин Дорж өвөө", "Grandpa Dorj the fisher"), look = new Look { deel = "teal", skin = "#B97A4A", hat = "cap" },
            hello = new Tx("Нуур тайван байна. Загасчлахад тэвчээр хэрэгтэй.", "The lake is calm today. Fishing takes patience."),
            chain = new[] { Q("fish", 1, "Нэг загас барьж үзээрэй. Гүүрэн дээрээс загасчилна.", "Catch your first fish from the dock.", 15, 20), Q("fish", 3, "Одоо 3 загас бариарай. Хадран барьвал буцааж тавиарай!", "Now catch 3 fish. If you catch a taimen, let it go!", 30, 35), Q("basar", 2, "Басараас Монголын гол мөрөн, нууруудын тухай 2 асуулт асуу.", "Ask Basar 2 questions about Mongolia’s rivers and lakes.", 25, 30) },
            done = new Tx("Сайн загасчин байгалиа хайрладаг. Хөвсгөл нуур бол Монголын хамгийн гүн нуур шүү!", "A good fisher loves nature. Lake Khövsgöl is Mongolia’s deepest lake!"),
            tic = new Tx("Хм… ", "Hmm… "), bio = new Tx("Хм… Би энэ нуурын эрэг дээр жаран жил загасчилсан… Хадраныг үргэлж буцааж тавьдаг. Байгаль бидэнд зээлээр л өгдөг юм.", "Hmm… I’ve fished this shore for sixty years… I always let the taimen go. Nature only lends to us.") },
        new Npc { id = "farmer", name = new Tx("Саальчин Сарнай эгч", "Sarnai the milkmaid"), look = new Look { deel = "green", skin = "#EDBB93", hat = "flower" },
            hello = new Tx("Хот айлд ажил их байна! Мал саахад туслах уу?", "There’s lots to do in the dairy yard! Will you help me milk?"),
            chain = new[] { Q("milk", 3, "Үнээ, ямаа, гүүгээ 3 удаа саагаарай.", "Milk the cows, goats and mares 3 times.", 20, 25), Q("dairy", 2, "Ааруул, өрөм, тараг эсвэл айраг 2 удаа хийгээрэй.", "Make aaruul, öröm, tarag or airag twice.", 25, 30), Q("sell", 3, "Цагаан идээгээ дэлгүүрт 3 удаа зараарай.", "Sell your dairy foods at the shop 3 times.", 30, 35) },
            done = new Tx("Баярлалаа! Монголчууд зун цагаан идээ, өвөл улаан идээ (мах) голлон иддэг.", "Thank you! Mongolians eat mostly white foods (dairy) in summer and red foods (meat) in winter."),
            tic = new Tx("Ой! ", "Oh! "), bio = new Tx("Ой, би өдөрт хоёр удаа саадаг! Ааруулаа наранд хатаахад шувууд хулгайлах гээд байдаг юм, хи-хи!", "Oh, I milk twice a day! When I dry aaruul in the sun the birds keep trying to steal it, hee-hee!") },
        new Npc { id = "teacher", name = new Tx("Багш Оюун", "Teacher Oyun"), look = new Look { deel = "blue", skin = "#F6D3B3", neck = "scarf", face = "glasses" },
            hello = new Tx("Сурах бичиг уншихаас гадна тоглож сурах нь бас гоё!", "Besides books, learning by playing is great fun!"),
            chain = new[] { Q("lesson", 1, "Сургуулийн гэрт нэг хичээл дуусгаарай.", "Finish one lesson in the school ger.", 30, 40), Q("quiz", 3, "Басарын 3 асуултад зөв хариулаарай.", "Answer 3 of Basar’s questions correctly.", 25, 35), Q("stamp", 1, "Аймгийн паспортоороо нэг аймагт аялж тамга аваарай.", "Travel to a province with your passport and earn a stamp.", 35, 45) },
            done = new Tx("Гайхалтай сурагч! “Эрдэм ухаан, эрдэнийн сан” гэдэг шүү.", "An amazing student! As we say, “knowledge is a treasure”."),
            tic = new Tx("За, ", "Now then, "), bio = new Tx("За, би хөдөөний сургуульд хорин жил багшилсан. Хамгийн дуртай хичээл маань Монгол бичиг. Дээрээс доош бичдэг нь ямар гоё вэ!", "Now then, I taught at a countryside school for twenty years. My favourite subject is the Mongol script. Writing from top to bottom is so lovely!") },
        new Npc { id = "trainer", name = new Tx("Уяач Ганбаатар", "Horse trainer Ganbaatar"), look = new Look { deel = "gold", skin = "#D9A06E", hat = "loovuuz" },
            hello = new Tx("Сайн уяач морио хайрлаж, сайн мэддэг. Хурдан морь уяачаас эхэлдэг.", "A good trainer loves and knows their horse. Every fast horse starts with its trainer."),
            chain = new[] { Q("race", 1, "Морин уралдаанд нэг удаа оролцоорой.", "Ride in one horse race.", 25, 30), Q("race", 2, "Дахиад 2 уралдаанд уралдаж мориндоо дасгал хийлгээрэй.", "Ride 2 more races to train your horse.", 35, 40), Q("wrestle", 1, "Наадмын талбайд нэг даваа давж үзээрэй.", "Win one round at the wrestling arena.", 30, 35) },
            done = new Tx("Чи морио сайн мэдэх болжээ! Уяачид морио уралдааны өмнө “сойдог”, өөрөөр хэлбэл тусгайлан бэлтгэдэг.", "You know your horse well now! Before a race, trainers “soi” their horses: they prepare them in a special way."),
            tic = new Tx("Чү! ", "Chü! "), bio = new Tx("Чү! Миний хээр морь Наадамд гурван удаа түрүүлсэн! Уяач хүн морио хайрлаж, сайн арчлах ёстой.", "Chü! My bay horse has won the Naadam three times! A trainer must love their horse and care for it well.") },
        new Npc { id = "archer", name = new Tx("Мэргэн Тулга ах", "Tulga the archer"), look = new Look { deel = "red", skin = "#B97A4A", hat = "mongol", neck = "medal" },
            hello = new Tx("Сайн харваач амьсгалаа тайвшруулж, салхиа ажигладаг.", "A good archer breathes calmly and watches the wind."),
            chain = new[] { Q("archery", 3, "Сурын талбайд 3 бай онооорой.", "Hit 3 targets at the archery range.", 25, 30), Q("archery", 6, "Одоо 6 бай онооорой. Салхийг тооцоорой!", "Now hit 6 targets. Mind the wind!", 35, 40), Q("together", 1, "Гэр бүлтэйгээ нэг бодит ажил хамт хийгээрэй.", "Do one real-life activity with your family.", 40, 45) },
            done = new Tx("Чи мэргэн болж байна! Сайн харваачийг монголчууд “мэргэн” гэж цоллодог.", "You’re becoming a mergen! That is what Mongols call a great archer."),
            tic = new Tx("Ш-ш… ", "Shh… "), bio = new Tx("Ш-ш… Тайван. Сур харваа бол тэвчээрийн спорт. Аав маань надад гурван настайгаас нум заасан.", "Shh… calm. Archery is a sport of patience. My father taught me the bow when I was three.") },
        new Npc { id = "craftsman", name = new Tx("Мужаан өвөө Тулга", "Grandpa Craftsman Tulga"), look = new Look { deel = "brown", skin = "#C98E5E", hat = "mongol" },
            hello = new Tx("Сайн уу! Би гэрийн мод хийдэг мужаан. Хана, хаалга, тооно, униа бүгдийг гараараа хийнэ.", "Hello! I am a craftsman: I make every wooden part of a ger by hand, walls, door, toono and poles."),
            chain = new[] { Q("shear", 1, "Эсгий хийхэд ноос хэрэгтэй. Нэг хонь хяргаад ир.", "Felt needs wool. Go and shear a sheep.", 15, 20), Q("felt", 1, "Хонины хашааны хажууд эсгий хийх газар бий. Нэг эсгий өнхрүүлээд ир.", "There is a felt spot by the sheep pasture. Roll one felt.", 25, 30), Q("ger", 1, "Одоо гэрээ өөрийн гараар бариад үзүүлээрэй!", "Now build your own ger with your own hands and show me!", 40, 50) },
            done = new Tx("Гайхалтай! Гэрээ өөрөө барьсан хүүхэд гэрийнхээ хэсэг бүрийг мэддэг болно.", "Wonderful! A child who builds their own ger knows every part of it."),
            tic = new Tx("Хм, ", "Hmm, "), bio = new Tx("Хм, би 50 жил гэрийн мод хийлээ. Хана бүрийг бургасаар, униаг шинэс модоор хийдэг. Хэзээ ч хадаас хэрэглэдэггүй, бүгдийг арьсан оосроор уяна!", "Hmm, I have made ger frames for 50 years. Walls from willow, poles from larch. Never a nail: everything is tied with leather straps!") },
    };
    public static Npc NpcById(string id) => NPCS.FirstOrDefault(n => n.id == id);
    public static readonly Look EMEE = new Look { deel = "purple", skin = "#EDBB93", hat = "mongol", face = "glasses", neck = "khadag" };
    public static readonly Look ZASUUL = new Look { deel = "gold", skin = "#D9A06E", hat = "mongol", neck = "khadag" };
    public static readonly Look GENERAL = new Look { deel = "green", skin = "#B97A4A", hat = "loovuuz" };
    public static readonly Look HISTORIAN = new Look { deel = "purple", skin = "#EDBB93", hat = "mongol", face = "glasses" };

    // ---------------------------------------------------------------- Эмээгийн гэр: etiquette with Grandma (gentle, never punishing)
    public sealed class Opt { public Tx t, why; public bool ok; }
    public sealed class EmeeStep { public Tx say; public string hl, zone; public Opt[] opts; public Dictionary<string, Tx> zones; }
    static Opt O2(string mn, string en, bool ok, string wmn = null, string wen = null) => new Opt { t = new Tx(mn, en), ok = ok, why = wmn != null ? new Tx(wmn, wen) : default };
    public static readonly EmeeStep[] EMEE_STEPS =
    {
        new EmeeStep { say = new Tx("Миний хүү/охин ирлээ! Эмээгийн гэрт тавтай морил. Хаалгаар яаж орох вэ?", "My dear child, welcome to Grandma’s ger! How do you come in through the door?"), hl = "door",
            opts = new[] { O2("Босгыг алхаж давна", "Step over the threshold", true), O2("Босгон дээр гишгэнэ", "Step on the threshold", false, "Хүүхэд минь, босго бол гэрийн хүндэт хил. Түүн дээр гишгэдэггүй, харин алхаж давдаг юм. Дахиад оролдоод үз дээ.", "Dear, the threshold is the honoured border of the home. We never step on it; we step over it. Try again."), O2("Гүйж орно", "Run in", false, "Гэрт тайван, аажуухан ордог юм. Гэрийн эзэнд хүндэтгэл үзүүлж байгаа хэрэг шүү.", "We come into a ger calmly and slowly. It shows respect to the family.") } },
        new EmeeStep { say = new Tx("Сайн байна уу гэж мэндлэх үү? Эмээ чинь сонсож байна.", "Will you greet me? Grandma is listening."),
            opts = new[] { O2("“Сайн байна уу, эмээ!”", "“Sain baina uu, Grandma!”", true), O2("Юу ч хэлэхгүй", "Say nothing", false, "Гэрт ормогц эхлээд ахмадтайгаа мэндэлдэг. “Сайн байна уу” гэвэл эмээ чинь их баярлана.", "When we enter, we first greet the elders. A “sain baina uu” makes Grandma very happy.") } },
        new EmeeStep { say = new Tx("Одоо гэр дотор аль чигээр явах вэ?", "Which way do you walk inside the ger?"), hl = "west",
            opts = new[] { O2("Нар зөв: орохдоо зүүн гар тал руугаа", "Sunwise: towards your left as you enter", true), O2("Нар буруу: баруун гар тал руугаа", "Against the sun: towards your right", false, "Гэрт нарны явахтай адил нар зөв эргэдэг юм. Орж ирээд зүүн гар тал руугаа явбал зөв шүү.", "Inside a ger we move the way the sun travels. Going to your left as you enter is right."), O2("Голоор нь шууд гатална", "Cut straight across the middle", false, "Гал голомт, хоёр баганын дундуур гаталдаггүй юм. Тойруулж нар зөв яваарай.", "We never cut between the hearth and the two posts. Walk around, sunwise.") } },
        new EmeeStep { say = new Tx("Эмээ чамд халуун цай аягаллаа. Яаж авах вэ?", "Grandma pours you hot milk tea. How do you take it?"),
            opts = new[] { O2("Баруун гараараа авч, зүүн гараараа тохойгоо түшинэ", "Take it with your right hand, left hand under your right elbow", true), O2("Зүүн гараараа авна", "Take it with your left hand", false, "Ахмадаас юм авахдаа баруун гар эсвэл хоёр гараараа авдаг юм. Зүүн гараараа тохойгоо түшвэл бүр ёсорхуу.", "We take things from elders with the right hand or both hands. Supporting the elbow with your left hand is even more polite."), O2("“Ширээн дээр тавь” гэнэ", "Say “put it on the table”", false, "Эмээгийн өгсөн цайг гараараа хүндэтгэлтэй авбал эмээ чинь баярлана.", "Taking Grandma’s tea from her hands, respectfully, makes her happy.") } },
        new EmeeStep { say = new Tx("Зочин хүү/охин минь хаана суух вэ? Зураг дээр дараарай.", "And where does a guest sit? Tap the picture."), zone = "west",
            zones = new Dictionary<string, Tx> { ["khoimor"] = new Tx("Хоймор бол ахмад, хүндтэй зочны суудал. Эмээ урих хүртэл хүлээнэ дээ.", "The khoimor is for elders and honoured guests. Wait until Grandma invites you."), ["east"] = new Tx("Энэ бол гэрийн эзэгтэйн тал, гал тогоо байдаг. Зочин баруун талд суудаг юм.", "That is the hostess’s side with the kitchen. Guests sit on the west side."), ["hearth"] = new Tx("Гал голомт бол гэрийн зүрх. Түүний дэргэд сууж болохгүй шүү.", "The hearth is the heart of the home. We don’t sit right by it."), ["door"] = new Tx("Хаалганы дэргэд сууж болох ч дотогшоо ороод баруун талд суувал илүү ёстой.", "You could sit by the door, but come in and sit on the west side; that is proper.") } },
        new EmeeStep { say = new Tx("Сайхан зочиллоо! Гарахдаа яах вэ?", "What a lovely visit! How do you leave?"),
            opts = new[] { O2("“Баярлалаа, эмээ!” гээд босго алхаж гарна", "Say “Thank you, Grandma!” and step over the threshold", true), O2("Юу ч хэлэлгүй гүйгээд гарна", "Run out without a word", false, "Гарахдаа гэрийн эзэндээ баярлалаа, баяртай гэж хэлдэг юм. Босгоо ч бас алхаж давна шүү.", "When leaving, we thank the family and say goodbye, and step over the threshold again.") } },
    };

    // ---------------------------------------------------------------- inside your own ger
    public static readonly Tx[] GER_TEA = { new Tx("Сүүтэй цайг сүү, ус, давс, цайгаар чанадаг. Зочинд хамгийн түрүүнд цай өргөдөг.", "Milk tea is made with milk, water, salt and tea leaves. Guests are always offered tea first."), new Tx("Цайгаа шанагаар өндрөөс самарвал илүү амттай болдог гэдэг.", "Ladling the tea from high up is said to make it tastier.") };
    public static readonly Tx[] SNACKS = { new Tx("Ааруул шүдийг бэхжүүлдэг гэж ярьдаг. Ням!", "People say aaruul makes teeth strong. Crunch!"), new Tx("Борцог бол гурилаар хийсэн шарсан боов.", "Boortsog are fried dough pastries."), new Tx("Өрөмтэй боорцог бол зочны дуртай амттан.", "Boortsog with öröm cream is a guest’s favourite treat.") };

    public static T Pick<T>(IList<T> list) => list[UnityEngine.Random.Range(0, list.Count)];
}
