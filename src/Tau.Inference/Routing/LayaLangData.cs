namespace Tau.Inference.Routing;

/// <summary>
/// The constant data of <c>laya.lang</c> 0.3.20, copied verbatim: the script ranges, the stopword lists
/// and the non-English diacritic set. The routing parity fixture's header carries Python's own copy of
/// the lists and the set, and a test checks these against it.
/// </summary>
internal static class LayaLangData
{
    /// <summary>A diacritic rate at or above this is taken as evidence the text is not English.</summary>
    internal const double NonEnDiacriticRate = 0.02;

    /// <summary>Non-Latin share at which non-Latin words override a Latin plurality.</summary>
    internal const double NonLatinFraction = 0.2;

    /// <summary>Lower non-Latin share that also overrides, given enough non-Latin letters.</summary>
    internal const double NonLatinMinFraction = 0.1;

    /// <summary>The number of non-Latin letters that makes <see cref="NonLatinMinFraction"/> enough.</summary>
    internal const int NonLatinMinLetters = 10;

    /// <summary>Named non-Latin scripts in the reference's order; the first range that claims a code point wins.</summary>
    internal static readonly (string Name, (int Lo, int Hi)[] Ranges)[] ScriptRanges =
    [
        ("greek", [(0x0370, 0x03FF), (0x1F00, 0x1FFF)]),
        ("cyrillic", [(0x0400, 0x052F), (0x2DE0, 0x2DFF), (0xA640, 0xA69F)]),
        ("armenian", [(0x0530, 0x058F)]),
        ("hebrew", [(0x0590, 0x05FF)]),
        ("arabic", [(0x0600, 0x06FF), (0x0750, 0x077F), (0x08A0, 0x08FF), (0xFB50, 0xFDFF), (0xFE70, 0xFEFF)]),
        ("devanagari", [(0x0900, 0x097F), (0xA8E0, 0xA8FF)]),
        ("bengali", [(0x0980, 0x09FF)]),
        ("gurmukhi", [(0x0A00, 0x0A7F)]),
        ("gujarati", [(0x0A80, 0x0AFF)]),
        ("oriya", [(0x0B00, 0x0B7F)]),
        ("tamil", [(0x0B80, 0x0BFF)]),
        ("telugu", [(0x0C00, 0x0C7F)]),
        ("kannada", [(0x0C80, 0x0CFF)]),
        ("malayalam", [(0x0D00, 0x0D7F)]),
        ("sinhala", [(0x0D80, 0x0DFF)]),
        ("thai", [(0x0E00, 0x0E7F)]),
        ("lao", [(0x0E80, 0x0EFF)]),
        ("tibetan", [(0x0F00, 0x0FFF)]),
        ("myanmar", [(0x1000, 0x109F)]),
        ("georgian", [(0x10A0, 0x10FF)]),
        ("ethiopic", [(0x1200, 0x137F)]),
        ("khmer", [(0x1780, 0x17FF)]),
        ("hangul", [(0x1100, 0x11FF), (0x3130, 0x318F), (0xAC00, 0xD7AF)]),
        ("kana", [(0x3040, 0x309F), (0x30A0, 0x30FF), (0x31F0, 0x31FF)]),
        ("han", [(0x3400, 0x4DBF), (0x4E00, 0x9FFF), (0xF900, 0xFAFF)]),
    ];

    /// <summary>Stopword lists in the reference's dict order (the order <c>max</c> breaks ties in).</summary>
    internal static readonly (string Lang, HashSet<string> Words)[] Stop =
    [
        ("en", Set("the", "and", "is", "are", "was", "were", "to", "of", "in", "for", "with", "that",
            "this", "it", "you", "have", "has", "not", "but", "on", "at", "be", "as", "from",
            "will", "can", "would", "there", "their", "what", "which", "please", "we", "i")),
        ("fr", Set("le", "la", "les", "des", "une", "est", "pour", "dans", "que", "qui", "avec", "sur",
            "pas", "plus", "nous", "vous", "être", "cette", "mais", "sont", "ont", "aux", "ce",
            "et", "du", "au", "ou", "je", "tu", "il", "elle", "ils", "elles", "mon", "ton",
            "ma", "ta", "sa", "mes", "tes", "ses", "ces", "deux", "trois", "très", "bien",
            "tout", "tous", "toute", "fait", "veux", "veut", "peux", "peut", "dois", "doit",
            "merci", "bonjour", "jour", "jours", "mois", "fois", "quand", "comment", "pourquoi",
            "alors", "donc")),
        ("de", Set("der", "die", "das", "und", "ist", "ein", "eine", "den", "dem", "nicht", "mit", "für",
            "auf", "von", "zu", "sich", "auch", "werden", "wurde", "haben", "sind", "oder", "aber",
            "ich", "wir", "mir", "mich", "dir", "dich", "uns", "mein", "meine", "meinen",
            "meinem", "meiner", "diese", "dieser", "diesen", "dieses", "einen", "einem", "einer",
            "wie", "wo", "wann", "welche", "im", "zum", "zur", "aus", "bei", "nach", "noch", "bitte",
            "heute", "jetzt", "kann", "kannst", "habe", "gibt", "wird",
            "in", "was")),
        ("es", Set("el", "los", "las", "que", "por", "con", "para", "una", "es", "se", "del", "como",
            "pero", "son", "está", "este", "esta", "todo", "más", "muy", "hay", "sus",
            "la", "un", "y", "al", "lo", "le", "les", "su", "mi", "tu", "nos",
            "ni", "dos", "tres", "fue", "fueron", "ser", "tiene", "tienen", "tengo", "puede",
            "pueden", "quiero", "necesito", "hemos", "han", "sobre", "entre", "cuando", "donde",
            "porque", "aunque", "también", "ya", "eso", "esto", "esa", "ese", "nada", "algo",
            "aquí", "hoy", "gracias")),
        ("pt", Set("os", "as", "que", "em", "um", "uma", "para", "com", "não", "é", "se", "do", "da",
            "dos", "das", "mas", "são", "está", "este", "esta", "muito", "pelo", "pela",
            "o", "e", "na", "nas", "nos", "ao", "aos", "por", "foi", "era", "ser", "sou",
            "tem", "tenho", "pode", "podem", "quero", "preciso", "eu", "meu", "minha", "seu",
            "sua", "isso", "isto", "aqui", "ali", "como", "quando", "onde", "porque", "mais",
            "já", "ainda", "agora", "hoje", "ontem", "dois", "três", "tudo", "nada", "obrigado",
            "olá",
            "você", "vocês", "voce", "voces", "vc", "vcs", "nao", "sao", "ja", "até", "tá", "pra",
            "gostaria", "obrigada", "também", "tambem", "estou", "estamos", "meus", "minhas",
            "nosso", "nossa", "consigo", "cadê", "boa", "tarde", "noite",
            "depois", "antes", "então", "entao", "ninguém", "ninguem", "alguém", "alguem", "nenhum",
            "nenhuma", "estava", "ficou", "fiz", "deu")),
        ("it", Set("il", "lo", "gli", "che", "di", "per", "con", "non", "è", "si", "del", "della", "sono",
            "questo", "questa", "anche", "come", "più", "sono", "nella", "alla",
            "la", "le", "un", "uno", "una", "e", "ed", "o", "da", "su", "tra", "fra", "mi",
            "ci", "ne", "ho", "hai", "ha", "abbiamo", "avete", "hanno", "era", "stato", "stata",
            "devo", "deve", "devono", "voglio", "vorrei", "mio", "mia", "tuo", "sua", "quando",
            "dove", "perche", "molto", "poco", "sempre", "mai", "già", "ancora", "adesso", "oggi",
            "ieri", "grazie", "ciao", "scusa",
            "nel", "nell", "negli", "sul", "sulla", "sulle", "dal", "dalla", "dallo", "dagli", "dei",
            "delle", "dello", "degli", "agli", "alle", "col")),
        ("nl", Set("het", "een", "van", "is", "op", "te", "dat", "niet", "met", "voor", "zijn", "aan",
            "door", "maar", "ook", "worden", "deze", "naar", "wordt")),
        ("ro", Set("și", "să", "este", "sunt", "care", "pentru", "din", "dar", "după", "până", "fără",
            "ale", "lui", "în", "fost", "acum", "vreau", "trebuie", "foarte", "acest", "această",
            "acesta", "aceasta", "mi", "ți", "vă", "nu")),
        ("bn", Set("ami", "amar", "amake", "amra", "amader", "apni", "apnar", "apnake", "apnara",
            "tumi", "tomar", "tomake", "tomra", "tader", "ota", "eita", "oita",
            "ekta", "ei", "oi", "ki", "keno", "kivabe", "kibhabe", "kothay", "kokhon", "kobe",
            "koto", "kintu", "jodi", "tahole", "ar", "theke", "jonno", "sathe", "shathe", "diye",
            "niye", "moddhe", "kore", "korte", "korchi", "korsi", "korbo", "korechi", "koreche",
            "korun", "koren", "korlam", "hobe", "hoyeche", "hoise", "hocche", "hoyni",
            "chai", "chaina", "lagbe", "parchi", "parbo", "parchina", "peyechi", "paini",
            "dite", "dilam", "diyechi", "nai", "khub", "onek", "ekhon", "akhon", "ekhono",
            "abar", "ekbar", "duibar", "ajke", "kalke", "taka", "bhalo", "valo", "kharap",
            "shomossa", "somossa", "dhonnobad", "bhai", "shob", "keu", "kichu", "bolte", "bolun",
            "parben", "asbe", "jabe", "pabo", "ferot", "dorkar", "hoye", "geche", "gese")),
        ("az", Set("və", "ve", "bir", "bu", "üçün", "ucun", "ilə", "ile", "olan", "olub", "olmasa",
            "var", "yox", "yoxdur", "mən", "sən", "biz", "siz", "onlar", "daha", "çox", "cox",
            "hər", "nə", "kimi", "görə", "sonra", "əgər", "eger", "deyil", "lakin", "amma",
            "ancaq", "artıq", "artiq", "də", "isə", "həm", "yalnız", "yalniz")),
    ];

    /// <summary>Lower-case letters that ordinary English does not use, as code points.</summary>
    internal static readonly HashSet<int> NonEnDiacritics = CodePoints(
        "àâäãáåçéèêëíìîïñóòôöõøúùûüýÿßæœ"
        + "ăâîșțşţ"
        + "ąćęłńśźż"
        + "čďěňřšťůž"
        + "őű"
        + "ğı"
        + "āēģīķļņūž"
        + "đ"
        + "ə");

    /// <summary>Words that more than one list claims; such a word cannot name a language by itself.</summary>
    internal static readonly HashSet<string> SharedWords = BuildShared();

    private static HashSet<string> Set(params string[] words) => new(words, StringComparer.Ordinal);

    private static HashSet<int> CodePoints(string s)
    {
        var set = new HashSet<int>();
        foreach (var rune in s.EnumerateRunes())
        {
            set.Add(rune.Value);
        }

        return set;
    }

    private static HashSet<string> BuildShared()
    {
        var shared = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (_, words) in Stop)
        {
            foreach (var w in words)
            {
                if (Stop.Count(s => s.Words.Contains(w)) > 1)
                {
                    shared.Add(w);
                }
            }
        }

        return shared;
    }
}
