using Jiten.Core;
using Jiten.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace Jiten.Tests;

using Xunit;
using FluentAssertions;

public class FormSelectionTests
{
    private static IDbContextFactory<JitenDbContext>? _contextFactory;

    private async Task<List<DeckWord>> Parse(string text)
    {
        if (_contextFactory == null)
        {
            var configuration = new ConfigurationBuilder()
                                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                                .AddJsonFile("sharedsettings.json", optional: false)
                                .AddEnvironmentVariables()
                                .Build();

            var optionsBuilder = new DbContextOptionsBuilder<JitenDbContext>();
            optionsBuilder.UseNpgsql(configuration.GetConnectionString("JitenDatabase"));

            _contextFactory = new PooledDbContextFactory<JitenDbContext>(optionsBuilder.Options);
        }

        return await Jiten.Parser.Parser.ParseText(_contextFactory, text);
    }

    public static IEnumerable<object[]> FormSelectionCases()
    {
        // Pure kana word should win over secondary katakana form of a kanji word
        // ママ (mama, 1129240) should beat まま (as-is, 1585410) form ママ
        yield return ["ママ", "ママ", 1129240, (byte)0];

        // Hiragana まま should resolve to まま (as-is, 1585410), not mama
        yield return ["まま", "まま", 1585410, (byte)2];

        // オレ should resolve to 俺 (1576870), not olé (2768550)
        yield return ["オレ", "オレ", 1576870, (byte)3];

        // Pure kana gairaigo: テレビ has only kana forms
        yield return ["テレビ", "テレビ", 1080510, (byte)0];

        // 食べている should resolve to 食べる (1358280)
        yield return ["食べている", "食べている", 1358280, (byte)0];

        // パパ (papa, 1102140) — standalone kana word, no conflict
        yield return ["パパ", "パパ", 1102140, (byte)0];

        // いえない should resolve to 言える (1008860), not 癒える (1538740)
        // Both score identically — tie-broken by lower WordId
        yield return ["いえない", "いえない", 1008860, (byte)2];

        // いえる should resolve to 言える (1008860, "to be able to say"), not 癒える (1538740, "to be healed")
        // 言える has arch on one definition only — IsFullyArchaic=false prevents the -200 penalty
        yield return ["狂信的ともいえる技術信仰", "いえる", 1008860, (byte)2];

        // 身体 in isolation: Sudachi gives reading シンタイ, so しんたい (2830705) wins via ReadingMatchScore.
        // In sentence context where Sudachi gives カラダ, からだ (1409140) wins via EntryPriorityScore.
        yield return ["身体", "身体", 2830705, (byte)0];

        // 石 should resolve to いし/rock (1382440), not こく/unit of volume (1382450)
        // Sudachi reading イシ disambiguates between homographic kanji entries
        yield return ["石", "石", 1382440, (byte)0];

        // 忌々しい should resolve to いまいましい/annoying (1587580), not ゆゆしい/grave (1605760)
        // Sudachi reading イマイマシイ disambiguates between entries sharing the same kanji form
        yield return ["忌々しい", "忌々しい", 1587580, (byte)0];

        // 泥濘 should resolve to ぬかるみ/mud (1437020), not でいねい (2863417)
        // User dic overrides Sudachi reading to ヌカルミ for the common everyday reading
        yield return ["泥濘", "泥濘", 1437020, (byte)0];

        // 表へ出る — 表 should be おもて/surface (1489340), not ひょう/chart (1489350)
        // FixReadingAmbiguity overrides Sudachi ヒョウ→オモテ when followed by directional particle without preceding noun
        yield return ["表へ出る", "表", 1489340, (byte)0];

        // メニュー表 — 表 should be ひょう/chart (1489350) when preceded by a noun
        yield return ["メニュー表を見る", "表", 1489350, (byte)0];

        // 一日 standalone → いちにち (1576260), not ついたち (2225040)
        // FixReadingAmbiguity overrides Sudachi ツイタチ→イチニチ when not preceded by a month
        yield return ["一日でこれだけやれば", "一日", 1576260, (byte)0];

        // １日 (fullwidth numeral) → いちにち (1576260)
        yield return ["忘れられない１日が", "１日", 1576260, (byte)1];

        // 七月一日 → ついたち (2225040) — month context preserves the date reading
        yield return ["七月一日に生まれた", "一日", 2225040, (byte)1];

        // 寒気がする → さむけ/chills (1210410), not かんき/cold air (2866134)
        // FixReadingAmbiguity overrides Sudachi カンキ→サムケ when followed by が+する
        yield return ["寒気がした", "寒気", 1210410, (byte)0];

        // 開いた → ひらく (1202440), not あく (1586270)
        // Stem-based ReadingMatchScore: Sudachi ヒライ stem ひら matches ひらく kana form
        yield return ["本を開いた", "開いた", 1202440, (byte)0];

        // だろう after an adverb — Sudachi tags as 助動詞 (dict form だ), should resolve to
        // だろう expression (1928670), not だる aux-v (2867372)
        yield return ["どうだろう", "だろう", 1928670, (byte)0];

        // 初めまして — Sudachi tags as 感動詞 (Interjection), should resolve to the expression
        // 初めまして (1625780), not deconjugate to 初める aux-v (1342560)
        yield return ["初めまして", "初めまして", 1625780, (byte)0];

        // よくしています — should resolve to 良くする (2257610, uk), not 浴する (2255500)
        // GetPriorityScore uk bonus disambiguates when compound lookup returns both
        yield return ["私はある２つのことをよくしています", "よくしています", 2257610, (byte)3];

        // あの before non-noun → interjection "um" (1000430), not prenominal "that" (1000420)
        yield return ["あのすみません", "あの", 1000430, (byte)0];
        yield return ["あの、もし嫌だったらいいんだけど", "あの", 1000430, (byte)0];

        // あの before noun → prenominal adjective "that" (1000420)
        yield return ["あの時は大変だった", "あの", 1000420, (byte)1];

        // Sudachi misclassifies あの as 感動詞 — FixReadingAmbiguity overrides to PrenounAdjectival
        yield return ["ちなみにあのネバネバ粘液はしっとり濃厚化粧水。", "あの", 1000420, (byte)1];
        yield return ["実はあの〝名無し〟とは少々因縁があってな。", "あの", 1000420, (byte)1];
        yield return ["柔らかく、冷たいあの見慣れた顔。", "あの", 1000420, (byte)1];
        yield return ["音無さんのあの『パンツ』発言。", "あの", 1000420, (byte)1];

        // 禍 standalone → わざわい/disaster (1295080), not か (2844158)
        // FixReadingAmbiguity overrides Sudachi カ→ワザワイ for standalone 禍
        yield return ["大戦の禍に飲み込まれた", "禍", 1295080, (byte)1];

        // 空 as から (empty, 1245280) — Sudachi gives 形状詞/ウツロ, ProcessSpecialCases overrides to noun/カラ
        yield return ["妹のベッドも空なのか？", "空", 1245280, (byte)0];

        // 空 as そら (sky, 1245290) — Sudachi gives 名詞/ソラ, ReadingMatchScore disambiguates
        yield return ["空を見上げた", "空", 1245290, (byte)0];

        // 君 as くん/Mr (1247260), not ぎみ (2697530)
        // Common word with one archaic sense: no arch penalty (has ichi1), ReadingMatchScore disambiguates
        yield return ["６君にはＡｎｃｉｅｎｔ　Ｏｒｄｅｒの廃墟まで行ってもらう。", "君", 1247260, (byte)0];

        // 後 as あと/after (1269320) in kanji — Sudachi reading アト disambiguates vs うしろ (1269410)
        yield return ["一瞬の溜めの後、少女は勢いよく俺を引っ張った。", "後", 1269320, (byte)0];

        // あと in kana — 後/after (1269320, nf01) beats 跡/trace (1383680, nf03) via top-band nf granularity
        yield return ["あとＴｈｉｅｖｅｓ　Ｃｉｒｃｌｅが例のごとくチンケな問題を起こしている。", "あと", 1269320, (byte)1];

        // 後 as あと before numeral — FixReadingAmbiguity overrides Sudachi ゴ→アト
        yield return ["今死んどけば後何年も苦しまずに済むぞ。", "後", 1269320, (byte)0];

        // メンドイ in katakana should select the katakana form (index 2), not the hiragana めんどい (index 1)
        // The katakana form is tagged sk (search-only) but exact surface match preserves it
        yield return ["メンドイ", "メンドイ", 2078360, (byte)2];
    }

    [Theory]
    [MemberData(nameof(FormSelectionCases))]
    public async Task FormSelectionTest(string input, string expectedOriginalText, int expectedWordId, byte expectedReadingIndex)
    {
        var results = await Parse(input);

        var match = results.FirstOrDefault(w => w.OriginalText == expectedOriginalText);
        match.Should().NotBeNull($"expected token '{expectedOriginalText}' in parse results for '{input}'");
        match!.WordId.Should().Be(expectedWordId, $"WordId for '{expectedOriginalText}'");
        match.ReadingIndex.Should().Be(expectedReadingIndex, $"ReadingIndex for '{expectedOriginalText}'");
    }
}
