/// Tests taken from https://github.com/tshatrov/ichiran/blob/master/tests.lisp

using Jiten.Core;
using Jiten.Core.Data;
using Jiten.Parser;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace Jiten.Tests;

using Xunit;
using FluentAssertions;

public class MorphologicalAnalyserTests
{
    private static IDbContextFactory<JitenDbContext>? _contextFactory;

    private async Task<IEnumerable<string>> Parse(string text)
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

        var result = await Jiten.Parser.Parser.ParseText(_contextFactory, text);
        return result.Select(w => w.OriginalText);
    }


    public static IEnumerable<object[]> SegmentationCases()
    {
        yield return ["ご注文はうさぎですか", new[] { "ご注文", "は", "うさぎ", "ですか" }];
        yield return ["しませんか", new[] { "しません", "か" }];
        yield return ["ドンマイ", new[] { "ドンマイ" }];
        yield return ["みんな土足でおいで", new[] { "みんな", "土足で", "おいで" }];
        yield return ["おもわぬオチ提供中", new[] { "おもわぬ", "オチ", "提供", "中" }];
        yield return ["わたし", new[] { "わたし" }];
        yield return ["お姉ちゃんにまかせて地球まるごと", new[] { "お姉ちゃん", "に", "まかせて", "地球", "まるごと" }];
        yield return ["名人になってるはず", new[] { "名人", "に", "なってる", "はず" }];
        yield return ["いいとこ", new[] { "いいとこ" }];
        yield return ["はしゃいじゃう", new[] { "はしゃいじゃう" }];
        yield return ["分かっちゃうのよ", new[] { "分かっちゃう", "の", "よ" }];
        yield return ["懐かしく新しいまだそしてまた", new[] { "懐かしく", "新しい", "まだ", "そして", "また" }];
        yield return ["あたりまえみたいに思い出いっぱい", new[] { "あたりまえ", "みたい", "に", "思い出", "いっぱい" }];
        yield return ["何でもない日々とっておきのメモリアル", new[] { "何でもない", "日々", "とっておき", "の", "メモリアル" }];
        yield return ["しつれいしなければならないんです", new[] { "しつれいし", "なければならない", "んです" }];
        yield return ["だけど気付けば馴染んじゃってる", new[] { "だけど", "気付けば", "馴染んじゃってる" }];
        yield return ["飲んで笑っちゃえば", new[] { "飲んで", "笑っちゃえば" }];
        yield return ["なんで", new[] { "なんで" }];
        yield return ["遠慮しないでね", new[] { "遠慮しないで", "ね" }];
        yield return ["出かけるまえに", new[] { "出かける", "まえに" }];
        yield return ["感じたいでしょ", new[] { "感じたい", "でしょ" }];
        yield return ["まじで", new[] { "まじで" }];
        yield return ["その山を越えたとき", new[] { "その", "山", "を", "越えた", "とき" }];
        yield return ["遊びたいのに", new[] { "遊びたい", "のに" }];
        yield return ["しながき", new[] { "しながき" }];
        yield return ["楽しさ求めて", new[] { "楽しさ", "求めて" }];
        yield return ["日常のなかにも", new[] { "日常", "の", "なかにも" }];
        yield return ["ほんとは好きなんだと", new[] { "ほんと", "は", "好き", "なんだと" }];
        yield return ["内緒なの", new[] { "内緒", "なの" }];
        yield return ["魚が好きじゃない", new[] { "魚", "が", "好き", "じゃない" }];
        yield return ["物語になってく", new[] { "物語", "に", "なってく" }];
        yield return ["書いてきてくださった", new[] { "書いてきてくださった" }];
        yield return ["今日は何の日", new[] { "今日", "は", "何の", "日" }];
        yield return ["何から話そうか", new[] { "何", "から", "話そう", "か" }];
        yield return ["進化してく友情", new[] { "進化してく", "友情" }];
        yield return ["私に任せてくれ", new[] { "私", "に", "任せてくれ" }];
        yield return ["時までに帰ってくると約束してくれるのなら外出してよろしい", new[] { "時", "まで", "に", "帰ってくる", "と", "約束してくれる", "の", "なら", "外出して", "よろしい" }];
        yield return ["雨が降りそうな気がします", new[] { "雨が降りそう", "な", "気がします" }];
        yield return ["新しそうだ", new[] { "新しそう", "だ" }];
        yield return ["本を読んだりテレビを見たりします", new[] { "本", "を", "読んだり", "テレビ", "を", "見たり", "します" }];
        yield return ["今日母はたぶんうちにいるでしょう", new[] { "今日", "母", "は", "たぶん", "うち", "に", "いる", "でしょう" }];
        yield return ["赤かったろうです", new[] { "赤かったろう", "です" }];
        yield return ["そう呼んでくれていい", new[] { "そう", "呼んでくれて", "いい" }];
        yield return ["払わなくてもいい", new[] { "払わなくて", "も", "いい" }];
        yield return ["体に悪いと知りながらタバコをやめることはできない", new[] { "体", "に", "悪い", "と", "知りながら", "タバコをやめる", "こと", "は", "できない" }];
        yield return ["いつもどうり", new[] { "いつもどうり" }];
        yield return ["微笑みはまぶしすぎる", new[] { "微笑み", "は", "まぶしすぎる" }];
        yield return ["優しすぎそのうえカッコいいの", new[] { "優しすぎ", "そのうえ", "カッコいい", "の" }];
        yield return ["この本は複雑すぎるから", new[] { "この", "本", "は", "複雑", "すぎる", "から" }];
        yield return ["かわいいです", new[] { "かわいい", "です" }];
        yield return ["なんだから", new[] { "なんだ", "から" }];
        yield return ["名付けたい", new[] { "名付けたい" }];
        yield return ["切なくなってしまう", new[] { "切なく", "なってしまう" }];
        yield return ["誰かいなくなった", new[] { "誰か", "いなくなった" }];
        yield return ["思い出すな", new[] { "思い出す", "な" }];
        yield return ["かなって思ったら", new[] { "かな", "って", "思ったら" }];
        yield return ["法律にかなっているさま", new[] { "法律", "に", "かなっている", "さま" }];
        yield return ["ことすら難しい", new[] { "こと", "すら", "難しい" }];
        yield return ["投下しました", new[] { "投下しました" }];
        yield return ["そんなのでいいと思ってるの", new[] { "そんな", "ので", "いい", "と", "思ってる", "の" }];
        yield return ["だけが墓参りしてた", new[] { "だけ", "が", "墓参り", "してた" }];
        yield return ["はいいんだけどな", new[] { "は", "いい", "ん", "だけど", "な" }];
        yield return ["反論は認めません", new[] { "反論", "は", "認めません" }];
        yield return ["見たような気がする", new[] { "見た", "ような気がする" }];
        yield return ["幽霊を見たような顔つきをしていた", new[] { "幽霊", "を", "見た", "ような", "顔つき", "を", "していた" }];
        yield return ["元気になる", new[] { "元気", "に", "なる" }];
        yield return ["半端なかった", new[] { "半端なかった" }];
        yield return ["一人ですね", new[] { "一人", "です", "ね" }];
        yield return ["行事がある", new[] { "行事", "が", "ある" }];
        yield return ["当てられたものになる", new[] { "当てられた", "ものになる" }];
        yield return ["ことができず", new[] { "ことができず" }];
        yield return ["一生一度だけの忘られぬ約束", new[] { "一生一度", "だけ", "の", "忘られぬ", "約束" }];
        yield return ["やらずにこの路線でよかったのに", new[] { "やらずに", "この", "路線", "で", "よかった", "のに" }];
        yield return ["歌ってしまいそう", new[] { "歌ってしまいそう" }];
        yield return ["しまいそう", new[] { "しまいそう" }];
        yield return ["何ですか", new[] { "何", "ですか" }];
        yield return ["浮かれたいから", new[] { "浮かれたい", "から" }];
        yield return ["なくなっちゃう", new[] { "なくなっちゃう" }];
        yield return ["になりそうだけど", new[] { "に", "なりそう", "だけど" }];
        yield return ["これは辛い選択になりそうだな", new[] { "これ", "は", "辛い", "選択", "に", "なりそう", "だ", "な" }];
        yield return ["はっきりしそうだな", new[] { "はっきりしそう", "だ", "な" }];
        yield return ["泣きそうなんだけど", new[] { "泣きそう", "なんだ", "けど" }];
        yield return ["これですね", new[] { "これ", "です", "ね" }];
        yield return ["忘れなく", new[] { "忘れなく" }];
        yield return ["じゃないですか", new[] { "じゃない", "ですか" }];
        yield return ["純粋さ健気さ", new[] { "純粋さ", "健気さ" }];
        yield return ["着てたからね", new[] { "着てた", "から", "ね" }];
        yield return ["仕出かすからだと思います", new[] { "仕出かす", "から", "だ", "と", "思います" }];
        yield return ["みんながした", new[] { "みんな", "が", "した" }];
        yield return ["ほうが速いと", new[] { "ほう", "が", "速い", "と" }];
        yield return ["注意してください", new[] { "注意してください" }];
        yield return ["昨日といいどうしてこう", new[] { "昨日", "といい", "どうして", "こう" }];
        yield return ["いっぱいきそう", new[] { "いっぱい", "きそう" }];
        yield return ["仲良しになったら", new[] { "仲良し", "に", "なったら" }];
        yield return ["全くといっていい", new[] { "全く", "と", "いって", "いい" }];
        yield return ["発狂しそうなんだ", new[] { "発狂し", "そう", "なんだ" }];
        yield return ["引き上げられた", new[] { "引き上げられた" }];
        yield return ["をつかむため", new[] { "を", "つかむ", "ため" }];
        yield return ["ときが自分", new[] { "とき", "が", "自分" }];
        yield return ["もうこころ", new[] { "もう", "こころ" }];
        yield return ["届けしたら", new[] { "届け", "したら" }];
        yield return ["おまえら低いんだよ", new[] { "おまえら", "低い", "んだ", "よ" }];
        yield return ["すべてがかかっていると思いながら", new[] { "すべて", "が", "かかっている", "と", "思いながら" }];
        yield return ["がいないとこの", new[] { "が", "いない", "と", "この" }];
        yield return ["エロいと思っちゃう", new[] { "エロい", "と", "思っちゃう" }];
        yield return ["変わり映えしない", new[] { "変わり映え", "しない" }];
        yield return ["あなたがいなきゃこんな計画思いつかなかった", new[] { "あなた", "が", "いなきゃ", "こんな", "計画", "思いつかなかった" }];
        yield return ["見たかったです", new[] { "見たかった", "です" }];
        yield return ["出来て楽しかったな", new[] { "出来て", "楽しかった", "な" }];
        yield return ["つかってください", new[] { "つかってください" }];
        yield return ["誰もが思ってた", new[] { "誰も", "が", "思ってた" }];
        yield return ["参考にしたらしい", new[] { "参考にした", "らしい" }];
        yield return ["狙いやすそうで", new[] { "狙い", "やすそう", "で" }];
        yield return ["予定はございませんので", new[] { "予定", "は", "ございません", "ので" }];
        yield return ["犬はトラックにはねられた", new[] { "犬", "は", "トラック", "に", "はねられた" }];
        yield return ["仕事してください", new[] { "仕事してください" }];
        yield return ["おいかけっこしましょ", new[] { "おいかけっこ", "しましょ" }];
        yield return ["イラストカードが付きます", new[] { "イラスト", "カード", "が", "付きます" }];
        yield return ["じゃないかしら", new[] { "じゃない", "かしら" }];
        yield return ["いつか本当に", new[] { "いつか", "本当に" }];
        yield return ["言い方もします", new[] { "言い方", "も", "します" }];
        yield return ["何でこれ", new[] { "何で", "これ" }];
        yield return ["こういう物語ができるんだ", new[] { "こういう", "物語", "が", "できる", "んだ" }];
        yield return ["といったところでしょうか", new[] { "といった", "ところ", "でしょうか" }];
        yield return ["広めたいと思っている", new[] { "広めたい", "と", "思っている" }];
        yield return ["やらしいです", new[] { "やらしい", "です" }];
        yield return ["荒いとこもある", new[] { "荒い", "とこ", "も", "ある" }];
        yield return ["あったかいとこ行こう", new[] { "あったかい", "とこ", "行こう" }];
        yield return ["ぶっちゃけ話", new[] { "ぶっちゃけ", "話" }];
        yield return ["いけないわー", new[] { "いけない", "わー" }];
        yield return ["社長としてやっていけないわ", new[] { "社長", "として", "やっていけない", "わ" }];
        yield return ["よくわかんないけど", new[] { "よく", "わかんない", "けど" }];
        yield return ["ほうがいいんじゃないの", new[] { "ほうがいい", "ん", "じゃない", "の" }];
        yield return ["こんなんじゃ", new[] { "こんなん", "じゃ" }];
        yield return ["増やしたほうがいいな", new[] { "増やした", "ほうがいい", "な" }];
        yield return ["屈しやすいものだ", new[] { "屈し", "やすい", "もの", "だ" }];
        yield return ["目をもっている", new[] { "目", "を", "もっている" }];
        yield return ["これが君のなすべきものだ", new[] { "これ", "が", "君", "の", "なすべき", "もの", "だ" }];
        yield return ["泥棒をつかまえた", new[] { "泥棒", "を", "つかまえた" }];
        yield return ["金もないし友達もいません", new[] { "金", "も", "ない", "し", "友達", "も", "いません" }];
        yield return ["出来たからほら見てよ", new[] { "出来た", "から", "ほら", "見て", "よ" }];
        yield return ["眠いからもう寝るね", new[] { "眠い", "から", "もう", "寝る", "ね" }];
        yield return ["見本通りに", new[] { "見本", "通り", "に" }];
        yield return ["不適応", new[] { "不", "適応" }];
        yield return ["良いそうです", new[] { "良い", "そうです" }];
        yield return ["むらむらとわいた", new[] { "むらむら", "と", "わいた" }];
        yield return ["否定しちゃいけない", new[] { "否定しちゃ", "いけない" }];
        yield return ["観たいです", new[] { "観たい", "です" }];
        yield return ["あんたはわからん", new[] { "あんた", "は", "わからん" }];
        yield return ["見られたくないとこ", new[] { "見られたくない", "とこ" }];
        yield return ["三十八", new[] { "三十八" }];
        yield return ["エロそうだヤバそうだ", new[] { "エロそう", "だ", "ヤバそう", "だ" }];
        yield return ["睡眠を十分にとってください", new[] { "睡眠", "を", "十分", "にとって", "ください" }];
        yield return ["そうなんだけど", new[] { "そう", "なんだ", "けど" }];
        yield return ["進んでない", new[] { "進んでない" }];
        yield return ["一回だけであとは言わない", new[] { "一回", "だけ", "で", "あと", "は", "言わない" }];
        yield return ["ご親切に恐縮しております", new[] { "ご親切に", "恐縮しております" }];
        yield return ["官吏となっておる者がある", new[] { "官吏", "と", "なっておる", "者", "が", "ある" }];
        yield return ["間違えておられたようですね", new[] { "間違えておられた", "ようです", "ね" }];
        yield return ["人気のせいな", new[] { "人気", "の", "せい", "な" }];
        yield return ["コレはアレ", new[] { "コレ", "は", "アレ" }];
        yield return ["上に文字があったり", new[] { "上", "に", "文字", "が", "あったり" }];
        yield return ["言っただろ", new[] { "言った", "だろ" }];
        yield return ["嵐が起ころうとしている", new[] { "嵐", "が", "起ころうとしている" }];
        yield return ["知らないでしょう", new[] { "知らない", "でしょう" }];
        yield return ["読まないでしょう", new[] { "読まない", "でしょう" }];
        yield return ["来ないでしょう", new[] { "来ない", "でしょう" }];
        yield return ["何もかもがめんどい", new[] { "何もかも", "が", "めんどい" }];
        yield return ["なにもかもがめんどい", new[] { "なにもかも", "が", "めんどい" }];
        yield return ["あいつ規制されりゃいいのに", new[] { "あいつ", "規制", "されりゃ", "いい", "のに" }];
        yield return ["塗ってみようと思って", new[] { "塗って", "みよう", "と", "思って" }];
        yield return ["肩を並べられなかった", new[] { "肩を並べられなかった" }];
        yield return ["じゃなくて良かった", new[] { "じゃなくて", "良かった" }];
        yield return ["申し訳なさそう", new[] { "申し訳なさそう" }];
        yield return ["決まってたし", new[] { "決まってた", "し" }];
        yield return ["決まっている", new[] { "決まっている" }];
        yield return ["恐れ入りました", new[] { "恐れ入りました" }];
        yield return ["はうまい", new[] { "は", "うまい" }];
        yield return ["弾け飛びました", new[] { "弾け飛びました" }];
        yield return ["ぶっこんでいるようで", new[] { "ぶっこんでいる", "よう", "で" }];
        yield return ["じゃないけど下手に", new[] { "じゃない", "けど", "下手", "に" }];
        yield return ["的にそうではない", new[] { "的", "に", "そう", "ではない" }];
        yield return ["入り込めなかった", new[] { "入り込めなかった" }];
        yield return ["がいまいちなんだよ", new[] { "が", "いまいち", "なんだ", "よ" }];
        yield return ["脱がしにかかってる", new[] { "脱がし", "に", "かかってる" }];
        yield return ["必死になってる", new[] { "必死", "に", "なってる" }];
        yield return ["安心させた", new[] { "安心", "させた" }];
        yield return ["人が好きそうだ", new[] { "人", "が", "好きそう", "だ" }];
        yield return ["もっていこうとする", new[] { "もっていこう", "とする" }];
        yield return ["増やして", new[] { "増やして" }];
        yield return ["ぜいたくで", new[] { "ぜいたく", "で" }];
        yield return ["したくらいで", new[] { "した", "くらい", "で" }];
        yield return ["でもうまく人", new[] { "でも", "うまく", "人" }];
        yield return ["好き嫌いもしないように", new[] { "好き嫌い", "も", "しない", "ように" }];
        yield return ["のどこが思える", new[] { "の", "どこ", "が", "思える" }];
        yield return ["調子にのらないほうが", new[] { "調子にのらない", "ほう", "が" }];
        yield return ["こなさそう", new[] { "こなさそう" }];
        yield return ["伸びてこなさそう", new[] { "伸びてこなさそう" }];
        yield return ["手にとって", new[] { "手にとって" }];
        yield return ["早く杯を手にしてほしいのだがな", new[] { "早く", "杯", "を", "手にして", "ほしい", "の", "だが", "な" }];
        yield return ["私にとっては少しおかしいです", new[] { "私", "にとって", "は", "少し", "おかしい", "です" }];
        yield return ["パーティーは", new[] { "パーティー", "は" }];
        yield return ["彼以上のばかはいない", new[] { "彼", "以上", "の", "ばか", "は", "いない" }];
        yield return ["君がいないと淋しい", new[] { "君", "が", "いない", "と", "淋しい" }];
        yield return ["思いきって", new[] { "思いきって" }];
        yield return ["思いきっている", new[] { "思いきっている" }];
        yield return ["大事になります", new[] { "大事", "に", "なります" }];
        yield return ["元気にします", new[] { "元気", "に", "します" }];
        yield return ["ご迷惑おかけしてすみません", new[] { "ご迷惑", "おかけして", "すみません" }];
        yield return ["不便をおかけすることを謝ります", new[] { "不便", "を", "おかけする", "こと", "を", "謝ります" }];
        yield return ["お手数おかけし申し訳ないが", new[] { "お手数", "おかけし", "申し訳ない", "が" }];
        yield return ["私はあなたにお手数をおかけました", new[] { "私", "は", "あなた", "に", "お手数", "を", "お", "かけました" }];
        yield return ["ここにおかけなさい", new[] { "ここ", "に", "お", "かけなさい" }];
        yield return ["弾き出されてる", new[] { "弾き出されてる" }];
        yield return ["ぶっちゃけ", new[] { "ぶっちゃけ" }];
        yield return ["賢人たち", new[] { "賢人", "たち" }];
        yield return ["差ついた", new[] { "差", "ついた" }];
        yield return ["ですら", new[] { "ですら" }];
        yield return ["でさえ", new[] { "でさえ" }];
        yield return ["みごとにやってのける", new[] { "みごと", "に", "やってのける" }];
        yield return ["いる", new[] { "いる" }];
        yield return ["お下がり", new[] { "お下がり" }];
        yield return ["みんなにうらやましがられている", new[] { "みんな", "に", "うらやましがられている" }];
        yield return ["悪がられて", new[] { "悪がられて" }];
        yield return ["期待されがちなので男女", new[] { "期待され", "がち", "なので", "男女" }];
        yield return ["とぎれがちに話す", new[] { "とぎれがち", "に", "話す" }];
        yield return ["さほど", new[] { "さほど" }];
        yield return ["大きさほどもある", new[] { "大きさ", "ほど", "も", "ある" }];
        yield return ["しかいない", new[] { "しか", "いない" }];
        yield return ["掴めていない", new[] { "掴めていない" }];
        yield return ["振り回されたいな", new[] { "振り回されたい", "な" }];
        yield return ["さぼっている", new[] { "さぼっている" }];
        yield return ["のままで来る", new[] { "の", "まま", "で", "来る" }];
        yield return ["彼はどなりすぎて声をからした", new[] { "彼", "は", "どなり", "すぎて", "声", "を", "からした" }];
        yield return ["そうしたいからしただけだ", new[] { "そう", "したい", "から", "した", "だけ", "だ" }];
        yield return ["推し続けている", new[] { "推し", "続けている" }];
        yield return ["少し直せたら", new[] { "少し", "直せたら" }];
        yield return ["良いほう", new[] { "良い", "ほう" }];
        yield return ["いいえ", new[] { "いいえ" }];
        yield return ["割り当てられた", new[] { "割り当てられた" }];
        yield return ["綺麗だけど近よりがたいよね", new[] { "綺麗", "だけど", "近より", "がたい", "よね" }];
        yield return ["そうなんじゃない", new[] { "そう", "なん", "じゃない" }];
        yield return ["なんというかすみません", new[] { "なんというか", "すみません" }];
        yield return ["めんどくそがる", new[] { "めんどくそがる" }];
        yield return ["がなんで終わった", new[] { "が", "なんで", "終わった" }];
        yield return
        [
            "てか最近ファン層は円盤すら買わないからそいつらから金とるってのは無謀",
            new[] { "てか", "最近", "ファン", "層", "は", "円盤", "すら", "買わない", "から", "そいつら", "から", "金", "とる", "ってのは", "無謀" }
        ];
        yield return ["とろいな", new[] { "とろい", "な" }];
        yield return ["なんでもかんでも", new[] { "なんでもかんでも" }];
        yield return ["しないかい", new[] { "しない", "かい" }];
        yield return
        [
            "参拝しちゃいかんという人がいます",
            new[] { "参拝しちゃ", "いかん", "という", "人", "が", "います" }
        ];
        yield return ["人をひやかしちゃいやよ", new[] { "人", "を", "ひやかしちゃ", "いや", "よ" }];
        yield return ["しちゃいたい", new[] { "しちゃいたい" }];
        yield return ["けがなどをしないように", new[] { "けが", "など", "を", "しない", "ように" }];
        yield return ["買い支えたいと思う", new[] { "買い支えたい", "と", "思う" }];
        yield return ["おじゃましています", new[] { "おじゃましています" }];
        yield return ["とかいらんから", new[] { "とか", "いらん", "から" }];
        yield return ["ということだろうけど", new[] { "という", "こと", "だろう", "けど" }];
        yield return ["のはわからなくもない", new[] { "の", "は", "わからなく", "も", "ない" }];
        yield return ["変わっていくだろう", new[] { "変わっていく", "だろう" }];
        yield return ["見せなきゃいけなくなって", new[] { "見せ", "なきゃ", "いけ", "なくなって" }];
        yield return ["私じゃなくなるような瞬間があって", new[] { "私", "じゃ", "なくなる", "ような", "瞬間", "が", "あって" }];
        yield return ["効いててかなりぬくい", new[] { "効いてて", "かなり", "ぬくい" }];
        yield return ["撮影してていつもは", new[] { "撮影してて", "いつも", "は" }];
        yield return ["むしろいないほうが珍しい", new[] { "むしろ", "いない", "ほう", "が", "珍しい" }];
        yield return ["旅行にいきたい", new[] { "旅行", "に", "いきたい" }];
        yield return ["見ててこんな話あったっけ", new[] { "見てて", "こんな", "話", "あった", "っけ" }];
        yield return ["いじめとかある", new[] { "いじめ", "とか", "ある" }];
        yield return ["となったらしい", new[] { "となった", "らしい" }];
        yield return ["基地外が必死過ぎ", new[] { "基地外", "が", "必死", "過ぎ" }];
        yield return ["調整のせいとか", new[] { "調整", "の", "せい", "とか" }];
        yield return ["はっしていない", new[] { "はっしていない" }];
        yield return ["無理さえしなければ", new[] { "無理", "さえ", "しなければ" }];
        yield return ["ところで", new[] { "ところで" }];
        yield return ["外に出て", new[] { "外", "に", "出て" }];
        yield return ["大人しそうな顔", new[] { "大人しそう", "な", "顔" }];
        yield return ["おとなしそうなようすにだまされた", new[] { "おとなしそう", "な", "ようす", "に", "だまされた" }];
        yield return ["勝手に入る", new[] { "勝手に", "入る" }];
        yield return ["後継ぎする", new[] { "後継ぎ", "する" }];
        yield return ["なすまん", new[] { "な", "すまん" }];
        yield return ["強いんだね", new[] { "強い", "んだ", "ね" }];
        yield return ["次がある", new[] { "次", "が", "ある" }];
        yield return ["のせいですね", new[] { "の", "せい", "です", "ね" }];
        yield return ["それただの怪しい人ですし", new[] { "それ", "ただ", "の", "怪しい", "人", "です", "し" }];
        yield return ["ごときが知る", new[] { "ごとき", "が", "知る" }];
        yield return ["山にはさまれて", new[] { "山", "に", "はさまれて" }];
        yield return ["物がぼんやりとかすんで見える", new[] { "物", "が", "ぼんやり", "と", "かすんで", "見える" }];
        yield return ["どなた様でございましょうか", new[] { "どなた", "様", "でございましょう", "か" }];
        yield return ["読んでくださりありがとうございました", new[] { "読んでくださり", "ありがとうございました" }];
        yield return ["ふざけんな", new[] { "ふざけんな" }];
        yield return ["観終わってた", new[] { "観", "終わってた" }];
        yield return ["意味深終わり", new[] { "意味深", "終わり" }];
        yield return ["今日とて居残りです", new[] { "今日", "とて", "居残り", "です" }];
        yield return ["堪能させていただきます", new[] { "堪能させて", "いただきます" }];
        yield return ["わからんからそう思った", new[] { "わからん", "から", "そう", "思った" }];
        yield return ["うちからそうなっても", new[] { "うち", "から", "そう", "なって", "も" }];
        yield return ["上映会やな", new[] { "上映", "会", "や", "な" }];
        yield return ["以上書いてください", new[] { "以上", "書いてください" }];
        yield return ["してしまったのがいまだに忘れられないし", new[] { "してしまった", "の", "が", "いまだに", "忘れられない", "し" }];
        yield return ["彼ははんぱじゃなく", new[] { "彼", "は", "はんぱじゃなく" }];
        yield return ["許さないじゃなくてさ", new[] { "許さない", "じゃなくて", "さ" }];
        yield return ["じゃなかったです", new[] { "じゃなかった", "です" }];
        yield return ["彼女は苦しげにうめいて横たわった", new[] { "彼女", "は", "苦し", "げ", "に", "うめいて", "横たわった" }];
        yield return ["わたしにはちょっとわかりかねますので", new[] { "わたし", "には", "ちょっと", "わかりかねます", "ので" }];
        yield return ["腕をつかまれて路地", new[] { "腕", "を", "つかまれて", "路地" }];
        yield return ["別にマイナスにならん", new[] { "別に", "マイナス", "に", "ならん" }];
        yield return ["遊びばかりはだめだよ", new[] { "遊び", "ばかり", "は", "だめ", "だ", "よ" }];
        yield return ["最中でも", new[] { "最中", "でも" }];
        yield return ["小動物好き物好き", new[] { "小動物", "好き", "物好き" }];
        yield return ["知れないですか", new[] { "知れない", "ですか" }];
        yield return ["かも知れないですね", new[] { "かも知れない", "です", "ね" }];
        yield return ["匙ですくう", new[] { "匙", "で", "すくう" }];
        yield return ["デカかったクドくない", new[] { "デカかった", "クドくない" }];
        yield return ["決めたらしい教われたらしい", new[] { "決めた", "らしい", "教われた", "らしい" }];
        yield return ["臆病なくせにとてもよい仲間だった", new[] { "臆病な", "くせに", "とても", "よい", "仲間", "だった" }];
        yield return ["あのねあのさ", new[] { "あのね", "あのさ" }];
        yield return ["これまでになかったような名優", new[] { "これまで", "に", "なかった", "ような", "名優" }];
        yield return ["確かめてちゃんと", new[] { "確かめて", "ちゃんと" }];
        yield return ["ことにしましょうってなった", new[] { "ことにしましょう", "って", "なった" }];
        yield return ["見てござる", new[] { "見て", "ござる" }];
        yield return ["彼がいうことはわけがわからない", new[] { "彼", "が", "いう", "こと", "は", "わけがわからない" }];
        yield return ["わけのわからないことをくどくど言う", new[] { "わけのわからない", "こと", "を", "くどくど", "言う" }];
        yield return ["ごくまれに", new[] { "ごくまれ", "に" }];
        yield return ["癒やされたかった", new[] { "癒やされたかった" }];
        yield return ["７時には帰ってきなさい", new[] { "７時", "には", "帰ってきなさい" }];
        yield return ["人はいますか", new[] { "人", "は", "います", "か" }];
        yield return ["トマトづくし", new[] { "トマト", "づくし" }];
        yield return ["見えざる関係性", new[] { "見えざる", "関係性" }];
        yield return ["だめだったら", new[] { "だめ", "だったら" }];
        yield return ["万事不都合の無いようにはからってくれ", new[] { "万事", "不都合", "の", "無い", "ように", "はからってくれ" }];
        yield return ["ではみなさん", new[] { "では", "みなさん" }];
        yield return ["鉄とはがね", new[] { "鉄", "と", "はがね" }];
        yield return ["抹茶とは", new[] { "抹茶", "とは" }];
        yield return ["当たりとはずれ", new[] { "当たり", "と", "はずれ" }];
        yield return ["工夫がされる", new[] { "工夫", "が", "される" }];
        yield return ["うまいことしたね", new[] { "うまいこと", "した", "ね" }];
        yield return
        [
            "ことしは新成人１４人のうち８人が避難先などから村の村民会館に集まりました",
            new[] { "ことし", "は", "新成人", "１４人", "の", "うち", "８人", "が", "避難先", "など", "から", "村", "の", "村民", "会館", "に", "集まりました" }
        ];
        yield return ["鬱が悪化する", new[] { "鬱", "が", "悪化する" }];
        yield return ["一部が手に入ればことし１年の願いがかなうとされています", new[] { "一部", "が", "手に入れば", "ことし", "１年", "の", "願い", "が", "かなう", "とされています" }];
        yield return ["汗を流しました", new[] { "汗を流しました" }];
        yield return ["気がついてる", new[] { "気がついてる" }];
        yield return ["ガスがついている", new[] { "ガス", "が", "ついている" }];
        yield return ["再開通", new[] { "再", "開通" }];
        yield return ["謝罪はあったにせよ", new[] { "謝罪", "は", "あった", "にせよ" }];
        yield return ["うそではないにしろ", new[] { "うそ", "ではない", "にしろ" }];
        yield return ["普段着てる服", new[] { "普段", "着てる", "服" }];
        yield return ["エレガントなお洋服", new[] { "エレガントな", "お", "洋服" }];
        yield return ["老いてなお元気なこと", new[] { "老いて", "なお", "元気な", "こと" }];
        yield return ["何も口にせぬ", new[] { "何も", "口", "に", "せぬ" }];
        yield return ["切ねぇ", new[] { "切ねぇ" }];
        yield return ["何故人気がある", new[] { "何故", "人気がある" }];
        yield return ["バラしちゃってる", new[] { "バラしちゃってる" }];
        yield return ["気を使わせている", new[] { "気を使わせている" }];
        yield return ["一段上がる", new[] { "一段", "上がる" }];
        yield return ["一段落ちる", new[] { "一段", "落ちる" }];
        yield return ["恐怖ですくむ", new[] { "恐怖", "で", "すくむ" }];
        yield return ["全員がたちすくみました", new[] { "全員", "が", "たちすくみました" }];
        yield return ["雪がないため", new[] { "雪", "が", "ない", "ため" }];
        yield return ["雪がなく", new[] { "雪", "が", "なく" }];
        yield return ["零れ落ちてる", new[] { "零れ落ちてる" }];
        yield return ["使い物にならんだろ", new[] { "使い物", "に", "ならん", "だろ" }];
        yield return ["私とならんで走った", new[] { "私", "と", "ならんで", "走った" }];
        yield return ["のうえに", new[] { "の", "うえ", "に" }];
        yield return ["皇位についたが", new[] { "皇位", "に", "ついた", "が" }];
        yield return ["疱瘡がついたか", new[] { "疱瘡", "が", "ついた", "か" }];
        yield return ["折りたたみ式ついたて", new[] { "折りたたみ式", "ついたて" }];
        yield return ["いろいろな部分をもんだりこすったりすること", new[] { "いろいろな", "部分", "を", "もんだり", "こすったり", "する", "こと" }];
        yield return ["たまにはいいもんだよ", new[] { "たまに", "は", "いい", "もん", "だ", "よ" }];
        yield return ["歩みをはやめるのだった", new[] { "歩み", "を", "はやめる", "の", "だった" }];
        yield return ["たばこはやめると誓います", new[] { "たばこ", "は", "やめる", "と", "誓います" }];
        yield return
        [
            "私個人の生活についてとやかくうるさくいうのはやめてください",
            new[] { "私", "個人", "の", "生活", "について", "とやかく", "うるさく", "いう", "の", "は", "やめてください" }
        ];
        yield return ["こもりがちな人", new[] { "こもり", "がちな", "人" }];
        yield return ["長くはかからないでしょう", new[] { "長く", "は", "かからない", "でしょう" }];
        yield return ["人はいないでしょうね", new[] { "人", "は", "いない", "でしょう", "ね" }];
        yield return ["人はいないですね", new[] { "人", "は", "いない", "です", "ね" }];
        yield return ["猛者どもの集い", new[] { "猛者", "ども", "の", "集い" }];
        yield return ["うまいかまずいか", new[] { "うまい", "か", "まずい", "か" }];
        yield return ["守衛にとがめられた", new[] { "守衛", "に", "とがめられた" }];
        yield return ["問い合わせがたくさん", new[] { "問い合わせ", "が", "たくさん" }];
        yield return ["楽しみがたくさん", new[] { "楽しみ", "が", "たくさん" }];
        yield return ["ふくろうは", new[] { "ふくろう", "は" }];
        yield return ["筋をもんでくれ", new[] { "筋", "を", "もんでくれ" }];
        yield return ["いわきからさいたままで", new[] { "いわき", "から", "さいたま", "まで" }];
        yield return ["新型コロナウイルス", new[] { "新型コロナウイルス" }];
        yield return ["新型コロナウィルス", new[] { "新型コロナウィルス" }];
        yield return ["映画を見るとか食事をするとか", new[] { "映画", "を", "見る", "とか", "食事", "を", "する", "とか" }];
        yield return ["さもうれしそうに笑う", new[] { "さも", "うれしそう", "に", "笑う" }];
        yield return ["出しなに客が来る", new[] { "出しな", "に", "客", "が", "来る" }];
        yield return ["出しながら飛んで", new[] { "出しながら", "飛んで" }];
        yield return ["正直言いたい", new[] { "正直", "言いたい" }];
        yield return ["おとめにふさわしい振る舞い", new[] { "おとめ", "に", "ふさわしい", "振る舞い" }];
        yield return ["気がないのよ", new[] { "気がない", "の", "よ" }];
        yield return ["口論のあげくに殴り合いになった", new[] { "口論", "の", "あげく", "に", "殴り合い", "に", "なった" }];
        yield return ["お手数おかけします", new[] { "お手数", "おかけします" }];
        yield return ["３０分後におかけ直しください", new[] { "３０分", "後", "に", "お", "かけ直し", "ください" }];
        yield return ["わかりきった", new[] { "わかりきった" }];
        yield return ["最良の方法は何だと思いますか", new[] { "最良", "の", "方法", "は", "何", "だ", "と", "思います", "か" }];
        yield return ["どうせいやがらせでする", new[] { "どうせ", "いやがらせ", "で", "する" }];
        yield return ["芝居もどきのせりふを言う", new[] { "芝居", "もどき", "の", "せりふ", "を", "言う" }];
        yield return ["がんもどきという食品", new[] { "がんもどき", "という", "食品" }];
        yield return ["落ちこぼれている", new[] { "落ちこぼれている" }];
        yield return ["忙しくてろくに更新もできず", new[] { "忙しくて", "ろくに", "更新", "も", "できず" }];
        yield return ["だまってろって", new[] { "だまってろ", "って" }];
        yield return ["しっぽく蕎麦", new[] { "しっぽく", "蕎麦" }];
        yield return ["猫はしっぽをぴんと立てて歩いた", new[] { "猫", "は", "しっぽ", "を", "ぴんと", "立てて", "歩いた" }];
        yield return ["やる気はない", new[] { "やる気", "は", "ない" }];
        yield return ["あけましておめでとうございます", new[] { "あけましておめでとうございます" }];
        yield return ["おれたちは行くのにおまえたちは行かぬ", new[] { "おれたち", "は", "行く", "のに", "おまえたち", "は", "行かぬ" }];
        yield return ["よろしくおねがいします", new[] { "よろしくおねがいします" }];
        yield return ["気を遣ってくれてるのかと思ってました", new[] { "気を遣ってくれてる", "の", "か", "と", "思ってました" }];
        yield return ["太陽をかたどったしるし", new[] { "太陽", "を", "かたどった", "しるし" }];
        yield return ["間違えていらっしゃるのかしら", new[] { "間違えて", "いらっしゃる", "の", "かしら" }];
        yield return ["ヤツはいそうにないな", new[] { "ヤツ", "は", "いそうにない", "な" }];
        yield return ["確認をとっています", new[] { "確認", "を", "とっています" }];
        yield return
        [
            "人口１０万人以上の都市の中で唯一旅客を扱う鉄道駅が存在せず",
            new[] { "人口", "１０万人", "以上", "の", "都市", "の", "中", "で", "唯一", "旅客", "を", "扱う", "鉄道駅", "が", "存在", "せず" }
        ];
        yield return ["だしはおいしい", new[] { "だし", "は", "おいしい" }];
        yield return ["だして", new[] { "だして" }];
        yield return ["だしといて", new[] { "だしといて" }];
        yield return ["見てあの平民の娘泣きだしましたわ", new[] { "見て", "あの", "平民", "の", "娘", "泣きだしました", "わ" }];
        yield return ["割り切れたら", new[] { "割り切れたら" }];
        yield return ["あり得なかったり", new[] { "あり得なかったり" }];
        yield return ["代わり映え", new[] { "代わり映え" }];
        yield return ["器用なのですぐ上達しますよ", new[] { "器用", "なので", "すぐ", "上達します", "よ" }];
        yield return ["おにいちゃん", new[] { "おにいちゃん" }];
        yield return ["動画につまってる", new[] { "動画", "に", "つまってる" }];
        yield return ["出来そう", new[] { "出来そう" }];
        yield return ["その上着貸してください", new[] { "その", "上着", "貸してください" }];
        yield return ["幸多き", new[] { "幸", "多き" }];
        yield return ["きっと気に入っていつかまた来てくれるよ", new[] { "きっと", "気に入って", "いつか", "また", "来てくれる", "よ" }];
        yield return ["私がいそうな場所知ってたんだから", new[] { "私", "が", "いそう", "な", "場所", "知ってた", "んだ", "から" }];
        yield return ["うまくハメられた", new[] { "うまく", "ハメられた" }];
        yield return ["してるとこだから", new[] { "してる", "とこ", "だから" }];
        yield return ["下記のとおりです", new[] { "下記", "の", "とおり", "です" }];
        yield return ["すぐに終わってしまった", new[] { "すぐに", "終わってしまった" }];
        yield return ["間違いない", new[] { "間違いない" }];
        yield return ["として", new[] { "として" }];
        yield return ["自分でも信じられないような気分だった", new[] { "自分でも", "信じられない", "ような", "気分", "だった" }];
        yield return ["必要な", new[] { "必要な" }];
        yield return ["大切な", new[] { "大切な" }];
        yield return ["飽き始める", new[] { "飽き", "始める" }];
        yield return ["教えてあげましょう", new[] { "教えてあげましょう" }];
        yield return ["でもなければ難しいだろう無ければ飽きを自覚しにくい", new[] { "でも", "なければ", "難しい", "だろう", "無ければ", "飽き", "を", "自覚し", "にくい" }];
        yield return ["引っ張り上げて貰って", new[] { "引っ張り上げて貰って" }];
        yield return ["信じて貰えなかった", new[] { "信じて貰えなかった" }];
        yield return ["生きて行けばいい", new[] { "生きて行けば", "いい" }];
        yield return ["持てそうだ", new[] { "持てそう", "だ" }];
        yield return ["引けなくなってしまって", new[] { "引けなく", "なってしまって" }];
        yield return ["ぶつけるべき", new[] { "ぶつける", "べき" }];
        yield return ["助けてもらえる", new[] { "助けてもらえる" }];
        yield return ["近づいて来ている", new[] { "近づいて", "来ている" }];
        yield return ["教えてくれるだろうけれど", new[] { "教えてくれる", "だろう", "けれど" }];
        yield return ["通用しない果てしない遠慮しない", new[] { "通用", "しない", "果てしない", "遠慮", "しない" }];
        yield return ["痛み出したり", new[] { "痛み", "出したり" }];
        yield return ["急にこれを食べさせられちゃったって言われてもちょっと困るなあ", new[] { "急に", "これ", "を", "食べさせられちゃった", "って", "言われて", "も", "ちょっと", "困る", "なあ" }];
        yield return ["俺は奴の民主主義ぶった欺瞞を指弾する", new[] { "俺", "は", "奴", "の", "民主主義", "ぶった", "欺瞞", "を", "指弾", "する" }];
        yield return ["俺はどこか背徳的な昂揚感", new[] { "俺", "は", "どこか", "背徳", "的な", "昂揚", "感" }];
        yield return ["欠陥品め", new[] { "欠陥品", "め" }];
        yield return ["本人たちは面白いと思ったのかもしれない", new[] { "本人", "たち", "は", "面白い", "と", "思った", "の", "かもしれない" }];
        yield return ["わかりかねさせられない", new[] { "わかりかねさせられない" }];
        yield return ["読み切れなかった", new[] { "読み切れなかった" }];
        yield return ["話し合っている", new[] { "話し合っている" }];
        // ん negative contraction tests
        yield return ["知らんだ", new[] { "知らん", "だ" }];
        yield return ["わからんよ", new[] { "わからん", "よ" }];
        yield return ["言わんでくれ", new[] { "言わんでくれ" }];
        // Past tense んだ tests (む/ぬ/ぶ/ぐ verbs)
        yield return ["睨んだが", new[] { "睨んだ", "が" }];
        yield return ["読んだけど", new[] { "読んだ", "けど" }];
        yield return ["飲んだから", new[] { "飲んだ", "から" }];
        yield return ["遊んだし", new[] { "遊んだ", "し" }];
        yield return ["客を待ってるんだけど", new[] { "客", "を", "待ってる", "んだ", "けど" }];
        yield return ["学生さんだって", new[] { "学生", "さん", "だって" }];
        yield return ["ちょっと休憩ーなんて言って", new[] { "ちょっと", "休憩", "なんて", "言って" }];
        yield return ["絶対に戻らなきゃいけない", new[] { "絶対", "に", "戻らなきゃ", "いけない" }];
        yield return ["とてもいい品が買えました", new[] { "とても", "いい", "品", "が", "買えました" }];
        // JMDict compound noun tests (Mode B refactor)
        // Single JMDict entries - should remain as one token
        yield return ["人種差別", new[] { "人種差別" }];
        yield return ["総合病院", new[] { "総合病院" }];
        yield return ["ソビエト連邦", new[] { "ソビエト連邦" }];
        yield return ["胚性幹細胞", new[] { "胚性幹細胞" }];
        yield return ["国際連合", new[] { "国際連合" }];
        yield return ["高等学校", new[] { "高等学校" }];
        yield return ["原子力発電所", new[] { "原子力発電所" }];
        yield return ["環境問題", new[] { "環境問題" }];
        // Long compound chains - should split into JMDict-valid components
        yield return ["人種差別撤廃宣言", new[] { "人種差別", "撤廃", "宣言" }];
        yield return ["ソビエト連邦人民代議員大会", new[] { "ソビエト連邦", "人民", "代議員", "大会" }];
        yield return ["西横浜国際総合病院", new[] { "西横浜", "国際", "総合病院" }];
        // Proper nouns with particles - should not over-combine across particles
        yield return ["人道の港", new[] { "人道", "の", "港" }];
        // JMnedict celebrity names - keep as single token (in JMnedict)
        yield return ["加藤紀子", new[] { "加藤紀子" }];
        yield return ["わかってねえじゃねえか", new[] { "わかってねえ", "じゃねえ", "か" }];
        yield return ["なんにもしたくないときもある", new[] { "なんにも", "したくない", "とき", "も", "ある" }];
        yield return ["いやあんま外出ないから", new[] { "いや", "あんま", "外", "出ない", "から" }];
        yield return ["外出ない", new[] { "外", "出ない" }];
        yield return ["家出なかった", new[] { "家", "出なかった" }];
        yield return ["普通は驚いたり恐がったり無視したりするものなのに", new[] { "普通", "は", "驚いたり", "恐がったり", "無視したり", "する", "もの", "なのに" }];
        // Vowel elongation tests - verb + う elongation
        // Pattern 1: Token ending in るう misparsed as adjective ウ音便 (e.g., かるう → 軽い)
        yield return ["ぶつかるう", new[] { "ぶつかる", "う" }];  // ぶつ + かるう → ぶつかる + う
        yield return ["とまるう", new[] { "とまる", "う" }];  // と + まるう → とまる + う
        // Pattern 2: Standalone るう token misparsed as name
        yield return ["わかるう", new[] { "わかる", "う" }];  // わか + るう → わかる + う
        yield return ["やるう", new[] { "やる", "う" }];  // や + るう → やる + う
        yield return ["あたるう", new[] { "あたる", "う" }];  // あた + るう → あたる + う
        yield return ["はしるう", new[] { "はしる", "う" }];  // はし + るう → はしる + う
        // Vowel elongation tests - verb past tense + あ elongation
        // Pattern 3: Token + たあ misparsed as particle と
        yield return ["おきたあ", new[] { "おきた", "あ" }];  // おき + たあ → おきた + あ (past of 起きる)
        yield return ["でたあ", new[] { "でた", "あ" }];  // で + たあ → でた + あ (past of 出る)
        yield return ["ねたあ", new[] { "ねた", "あ" }];  // ね + たあ → ねた + あ (past of 寝る)
        // Pattern 4: Token ending in た + ああ where token is misparsed as non-verb
        yield return ["いきたああ", new[] { "いきた", "ああ" }];  // いきた (nominal adj) + ああ → いきた (verb past) + ああ
        // Vowel elongation tests - verb + ー (long vowel mark)
        // Pattern 5: Verb + separate ー token (handled by RepairLongVowelTokens in Parser)
        yield return ["ぶつかるー", new[] { "ぶつかる" }];  // ぶつ + か + る + ー → ぶつかる (ー stripped, word doesn't contain it)
        yield return ["わかるー", new[] { "わかる" }];  // わか + る + ー → わかる (ー stripped, word doesn't contain it)
        // Emphatic っ tests - sokuon at clause boundaries causing misparses
        // っ is filtered as SupplementarySymbol, so it won't appear in output
        yield return ["止まらないっ", new[] { "止まらない" }];  // Sudachi misparsed as 止まら + な + いっ (行く)
        yield return ["これでどうですかっ", new[] { "これ", "で", "どう", "ですか" }];  // Sudachi misparsed で + すかっ
        yield return ["だめっ", new[] { "だめ" }];  // Simple case - っ should be separated
        yield return ["行くっ", new[] { "行く" }];  // Verb + emphatic っ
        yield return ["止まらないっ！", new[] { "止まらない" }];  // With punctuation
        yield return ["だめっ、それは違う", new[] { "だめ", "それ", "は", "違う" }];  // Mid-sentence emphatic っ
        yield return ["しょうがないな", new[] { "しょうがない", "な" }];
        yield return ["この手紙を書いた", new[] { "この", "手紙", "を", "書いた" }];
        // Emphatic ぶっち → ぶち normalisation (colloquial gemination)
        yield return ["ぶっち切れてる", new[] { "ぶち切れてる" }];  // ぶっち切れる → ぶち切れる (to become enraged)
        // 少女の手 misparse fix - Sudachi was parsing as 少 (prefix) + 女の手 (expression)
        yield return ["少女の手によって", new[] { "少女", "の", "手", "によって" }];
        // 手を抜く compound expression - Sudachi classifies 手 as suffix, but should match exp entry
        yield return ["手を抜いているんですか", new[] { "手を抜いている", "んです", "か" }];
        yield return ["水魔法", new[] { "水", "魔法" }];
        // ておく (te-form + おく subsidiary verb) should combine, not match おいた (mischief)
        yield return ["それはまだ秘密にしておいたほうが", new[] { "それ", "は", "まだ", "秘密", "に", "しておいた", "ほう", "が" }];
        yield return ["姉さんの所にちゃんと届けておいたから", new[] { "姉さん", "の", "所", "に", "ちゃんと", "届けておいた", "から" }];
        yield return ["いっぱいおいたしてるもの", new[] { "いっぱい", "おいたしてる", "もの" }];
        yield return ["全てをやる", new[] { "全て", "を", "やる" }];
        yield return ["続きがある", new[] { "続き", "が", "ある" }];
        // Long vowel mark (ー) repair tests
        // Broken cases: hiragana + ー that Sudachi over-segments must be repaired; ー stripped when word doesn't contain it
        yield return ["あなたー", new[] { "あなた" }];
        yield return ["おまえー", new[] { "おまえ" }];
        yield return ["わたしー", new[] { "わたし" }];
        yield return ["ばかー", new[] { "ばか" }];
        yield return ["うそー", new[] { "うそ" }];
        yield return ["すごいー", new[] { "すごい" }];
        // Multi-word sentence: あなたー must not merge with following words (no たーそこ tokens)
        yield return ["あなたーそこにいるの", new[] { "あなた", "そこ", "に", "いる", "の" }];
        // Must not regress: these are valid JMDict entries with ー — ー is part of the word
        yield return ["すげー", new[] { "すげー" }];
        yield return ["やべー", new[] { "やべー" }];
        yield return ["うるせー", new[] { "うるせー" }];
        yield return ["かわいー", new[] { "かわいー" }];
        yield return ["コーヒー", new[] { "コーヒー" }];
        // Bar run normalisation: multiple ー collapse to single ー, then word matched without ー
        yield return ["あなたーー", new[] { "あなた" }];
        // Kanji + ー: ー stripped since 休憩 doesn't contain it
        yield return ["休憩ー", new[] { "休憩" }];
    }

    [Theory]
    [MemberData(nameof(SegmentationCases))]
    public async Task SegmentationTest(string text, string[] expectedResult)
    {
        (await Parse(text)).Should().Equal(expectedResult);
    }
}
