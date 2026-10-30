using ShopForge.Shared.Stores;

namespace ShopForge.Catalog.Development;

// A Czech tool shop written the way a real one writes its catalogue: a category tree, attributes, brands, codes and
// descriptions in ordinary Czech. It is the search's test corpus and the development data's third store, so the live
// checks and the screens look at the same shop the tests do (D-225). Every name and brand is made up.
internal static class ToolShopCatalogue
{
    public static IReadOnlyList<ToolShopCategory> Categories { get; } =
    [
        new("naradi", "Nářadí", null),
        new("rucni-naradi", "Ruční nářadí", "naradi"),
        new("sroubovaky", "Šroubováky", "rucni-naradi"),
        new("klice", "Klíče", "rucni-naradi"),
        new("kleste", "Kleště", "rucni-naradi"),
        new("kladiva", "Kladiva", "rucni-naradi"),
        new("pily", "Pily a pilníky", "rucni-naradi"),
        new("elektricke-naradi", "Elektrické nářadí", "naradi"),
        new("vrtacky", "Vrtačky", "elektricke-naradi"),
        new("brusky", "Brusky", "elektricke-naradi"),
        new("spojovaci-material", "Spojovací materiál", null),
        new("srouby", "Šrouby", "spojovaci-material"),
        new("vruty", "Vruty", "spojovaci-material"),
        new("hrebiky", "Hřebíky", "spojovaci-material"),
        new("matice", "Matice a podložky", "spojovaci-material"),
        new("dilna", "Dílna", null),
        new("stoly", "Stoly a ponky", "dilna"),
        new("noze", "Nože a čepele", "dilna"),
        new("darky", "Dárky", "dilna"),
    ];

    public static IReadOnlyList<ToolShopAttribute> Attributes { get; } =
    [
        new("typ-hrotu", "Typ hrotu", "multiSelect", null, ["Plochý", "Křížový", "Torx", "Imbus"], ["sroubovaky"]),
        new("typ-klice", "Typ klíče", "select", null, ["Nastavitelný", "Očkoplochý", "Imbusový", "Momentový"], ["klice"]),
        new("delka", "Délka", "integer", "mm", [], ["sroubovaky", "klice", "kleste", "pily"]),
        new("magneticky-hrot", "Magnetický hrot", "boolean", null, [], ["sroubovaky"]),
        new("material", "Materiál", "text", null, [], []),
    ];

    public static IReadOnlyList<ToolShopProduct> Products { get; } =
    [
        new("TS-SP-55125", "Šroubovák plochý 5,5 × 125 mm", "Plochý šroubovák s dvousložkovou rukojetí.", 129m, "sroubovaky", "Kovomat",
            Values(("typ-hrotu", "Plochý"), ("delka", 125), ("magneticky-hrot", false), ("material", "Chrom-vanadová ocel"))),
        new("TS-SK-PH2100", "Šroubovák křížový PH2 × 100 mm", "Křížový šroubovák s magnetickým hrotem.", 139m, "sroubovaky", "Kovomat",
            Values(("typ-hrotu", "Křížový"), ("delka", 100), ("magneticky-hrot", true), ("material", "Chrom-vanadová ocel"))),
        new("TS-ST-T20100", "Šroubovák Torx T20 × 100 mm", "Šroubovák pro šrouby a vruty s hlavou Torx.", 149m, "sroubovaky", "Silák",
            Values(("typ-hrotu", "Torx"), ("delka", 100), ("magneticky-hrot", true))),
        new("TS-SS-6", "Sada šroubováků 6 dílů", "Tři ploché a tři křížové šroubováky v plastovém pouzdře.", 449m, "sroubovaky", "Kovomat",
            Values(("typ-hrotu", "Plochý, Křížový"), ("magneticky-hrot", false))),
        new("TS-SB-32", "Sada bitů 32 dílů s magnetickým držákem", "Bity PH, PZ, Torx a imbus do aku-vrtačky i do šroubováku s výměnnými hroty.", 299m, "sroubovaky", "Silák",
            Values(("typ-hrotu", "Křížový, Torx, Imbus"), ("magneticky-hrot", true))),
        new("TS-NH-PH2", "Náhradní hrot PH2 k TS-SK-PH2100", "Hrot do šroubováku TS-SK-PH2100 a dalších s výměnnými hroty PH2, balení po dvou.", 49m, "sroubovaky", "Kovomat",
            Values(("typ-hrotu", "Křížový"))),
        new("TS-SR-1", "Ráčnový šroubovák s výměnnými bity", "Ráčna se zpětným chodem a šesti bity v rukojeti.", 349m, "sroubovaky", "Vrtex",
            Values(("typ-hrotu", "Plochý, Křížový, Torx"))),
        new("TS-KN-250", "Klíč nastavitelný 250 mm", "Stavitelný klíč s rozevřením čelistí do 30 mm.", 289m, "klice", "Kovomat",
            Values(("typ-klice", "Nastavitelný"), ("delka", 250))),
        new("TS-KO-13", "Klíč očkoplochý 13 mm", "Kombinovaný klíč s očkem vyhnutým o 15°.", 89m, "klice", "Silák",
            Values(("typ-klice", "Očkoplochý"), ("delka", 170))),
        new("TS-SKO-819", "Sada klíčů očkoplochých 8–19 mm", "Dvanáct očkoplochých klíčů v textilní rolce.", 799m, "klice", "Silák",
            Values(("typ-klice", "Očkoplochý"))),
        new("TS-KI-6", "Imbusový klíč 6 mm", "Zalomený šestihranný klíč s kulovou hlavou.", 39m, "klice", "Pevnost",
            Values(("typ-klice", "Imbusový"))),
        new("TS-KM-12", "Momentový klíč 1/2\" 40–210 Nm", "Cvakací momentový klíč s ráčnou na kola osobních aut.", 1490m, "klice", "Vrtex",
            Values(("typ-klice", "Momentový"))),
        new("TS-KK-180", "Kleště kombinované 180 mm", "Univerzální kleště s izolovanými rukojeťmi do 1000 V.", 249m, "kleste", "Kovomat",
            Values(("delka", 180))),
        new("TS-KS-160", "Kleště štípací boční 160 mm", "Štípací kleště na měděný a hliníkový drát.", 219m, "kleste", "Kovomat",
            Values(("delka", 160))),
        new("TS-KSI-250", "Kleště SIKO 250 mm", "Instalatérské kleště se stavitelnou čelistí.", 329m, "kleste", "Silák",
            Values(("delka", 250))),
        new("TS-KZ-500", "Kladivo zámečnické 500 g", "Kované kladivo s násadou z jasanového dřeva.", 259m, "kladiva", "Pevnost", Values(), SortOrder: -1),
        new("TS-PG-680", "Palice gumová 680 g", "Gumová palice na dlažbu a nábytek.", 199m, "kladiva", "Pevnost", Values()),
        new("TS-KT-600", "Kladivo tesařské 600 g", "Tesařské kladivo s vytahovačem hřebíků. Kladivo s ocelovou násadou nepovolí.", 389m, "kladiva", "Silák", Values()),
        new("TS-PO-400", "Pila ocaska 400 mm", "Ruční pila na dřevo s kalenými zuby.", 279m, "pily", "Pevnost", Values(("delka", 400))),
        new("TS-PZ-300", "Pilka na železo 300 mm", "Rámová pilka na kov s napínacím šroubem.", 239m, "pily", "Kovomat", Values(("delka", 300))),
        new("TS-PP-200", "Pilník plochý 200 mm", "Plochý pilník na kov, sek 2.", 119m, "pily", "Kovomat", Values(("delka", 200))),
        new("TS-AV-18", "Aku-vrtačka 18 V se 2 bateriemi", "Aku vrtačka a šroubovák v jednom, s rychloupínacím sklíčidlem.", 2490m, "vrtacky", "Vrtex", Values()),
        new("TS-PV-750", "Příklepová vrtačka 750 W", "Vrtačka s příklepem do betonu a cihel.", 1890m, "vrtacky", "Vrtex", Values()),
        new("TS-VB-8120", "Vrták do betonu 8 × 120 mm", "Vrták s karbidovou destičkou pro příklepové vrtačky.", 69m, "vrtacky", "Silák", Values()),
        new("TS-UB-125", "Úhlová bruska 125 mm 900 W", "Bruska s ochranným krytem a přídavnou rukojetí.", 1290m, "brusky", "Vrtex", Values()),
        new("TS-SM-840", "Šroub M8 × 40 pozinkovaný (50 ks)", "Šroub se šestihrannou hlavou, pozinkovaný.", 89m, "srouby", "Pevnost", Values()),
        new("TS-SD-440", "Šrouby do dřeva 4x40 mm (200 ks)", "Šrouby se zápustnou hlavou a křížovou drážkou.", 129m, "srouby", "Pevnost", Values()),
        new("TS-SM3-10", "Šroubek M3 × 10 nerez (100 ks)", "Drobné šroubky z nerezové oceli.", 99m, "srouby", "Pevnost", Values()),
        new("TS-VD-560", "Vrut do dřeva 5 × 60 mm, Torx (100 ks)", "Vruty s hlavou Torx T25 do dřeva.", 149m, "vruty", "Pevnost", Values()),
        new("TS-VS-3535", "Vruty do sádrokartonu 3,5 × 35 mm (500 ks)", "Černé fosfátované vruty.", 179m, "vruty", "Pevnost", Values()),
        new("TS-HS-70", "Hřebíky stavební 70 mm (1 kg)", "Hřebíky s plochou hlavou.", 69m, "hrebiky", "Pevnost", Values()),
        new("TS-MM8", "Matice M8 pozinkovaná (100 ks)", "Šestihranná matice k šroubům M8.", 79m, "matice", "Pevnost", Values()),
        new("TS-PS-120", "Pracovní stůl s deskou z buku", "Dílenský stůl se zásuvkou, deska z bukového dřeva.", 4990m, "stoly", "Pevnost", Values()),
        new("TS-SSD-2", "Skládací stoly do dílny, 2 kusy", "Dva lehké skládací stoly na kozy.", 1590m, "stoly", "Pevnost", Values()),
        new("TS-NZ-18", "Nůž zalamovací 18 mm", "Odlamovací nůž s kovovým vedením čepele.", 89m, "noze", "Silák", Values()),
        new("TS-NC-18", "Náhradní čepele do nožů 18 mm (10 ks)", "Čepele k zalamovacím nožům.", 59m, "noze", "Silák", Values()),
        new("TS-HL-1", "Hrnek s logem dílny", "Keramický hrnek na kávu, 300 ml.", 149m, "darky", "Pevnost", Values()),
        new("TS-MD-50", "Magnetický držák nářadí 50 cm", "Lišta na zeď pro šroubováky a klíče.", 299m, "dilna", "Pevnost", Values()),
        new("TS-ZS-1", "Šroubovák zkušební", "Vzorek, který se zatím neprodává.", 59m, "sroubovaky", "Kovomat", Values(), Visible: false),
    ];

    private static IReadOnlyDictionary<string, object> Values(params (string Code, object Value)[] values) =>
        values.ToDictionary(value => value.Code, value => value.Value);
}

internal sealed record ToolShopCategory(string Slug, string Name, string? ParentSlug);

// An attribute and the categories that offer it as a filter.
internal sealed record ToolShopAttribute(string Code, string Name, string Type, string? Unit, IReadOnlyList<string> Options, IReadOnlyList<string> FilterIn);

internal sealed record ToolShopProduct(
    string Sku,
    string Name,
    string Description,
    decimal Price,
    string Category,
    string Brand,
    IReadOnlyDictionary<string, object> Values,
    bool Visible = true,
    int SortOrder = 0)
{
    public string Slug => Slugs.Create(Name);
}
