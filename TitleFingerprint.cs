using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AniTV;

public sealed record TitleFingerprint(string Text, string Numbers)
{
    static readonly Dictionary<string, int> Words = BuildWords();
    static readonly Regex WordSequence = new(@"\b(?:" + string.Join("|", Words.Keys.OrderByDescending(s => s.Length)) + @")(?:[\s-]+(?:" + string.Join("|", Words.Keys.OrderByDescending(s => s.Length)) + @"))*\b", RegexOptions.Compiled);
    static Dictionary<string,int> BuildWords()
    {
        var result = new Dictionary<string,int>();
        void Add(int n, string words) { foreach (var w in words.Split(' ')) result[w] = n; }
        Add(0,"zero ноль нулевой нулевая нулевое");
        Add(1,"one first один одна одно первый первая первое первого первой");
        Add(2,"two second два две второй вторая второе второго");
        Add(3,"three third три третий третья третье третьего третьей");
        Add(4,"four fourth четыре четвертый четвертая четвертое четвертого четвертой");
        Add(5,"five fifth пять пятый пятая пятое пятого пятой");
        Add(6,"six sixth шесть шестой шестая шестое шестого");
        Add(7,"seven seventh семь седьмой седьмая седьмое седьмого");
        Add(8,"eight eighth восемь восьмой восьмая восьмое восьмого");
        Add(9,"nine ninth девять девятый девятая девятое девятого девятой");
        Add(10,"ten tenth десять десятый десятая десятое десятого десятой");
        Add(11,"eleven eleventh одиннадцать одиннадцатый одиннадцатая одиннадцатого");
        Add(12,"twelve twelfth двенадцать двенадцатый двенадцатая двенадцатого");
        Add(13,"thirteen thirteenth тринадцать тринадцатый тринадцатая тринадцатого");
        Add(14,"fourteen fourteenth четырнадцать четырнадцатый четырнадцатая четырнадцатого");
        Add(15,"fifteen fifteenth пятнадцать пятнадцатый пятнадцатая пятнадцатого");
        Add(16,"sixteen sixteenth шестнадцать шестнадцатый шестнадцатая шестнадцатого");
        Add(17,"seventeen seventeenth семнадцать семнадцатый семнадцатая семнадцатого");
        Add(18,"eighteen eighteenth восемнадцать восемнадцатый восемнадцатая восемнадцатого");
        Add(19,"nineteen nineteenth девятнадцать девятнадцатый девятнадцатая девятнадцатого");
        Add(20,"twenty twentieth двадцать двадцатый двадцатая двадцатого");
        Add(30,"thirty thirtieth тридцать тридцатый тридцатая тридцатого");
        Add(40,"forty fortieth сорок сороковой"); Add(50,"fifty fiftieth пятьдесят пятидесятый");
        Add(60,"sixty sixtieth шестьдесят шестидесятый"); Add(70,"seventy seventieth семьдесят семидесятый");
        Add(80,"eighty eightieth восемьдесят восьмидесятый"); Add(90,"ninety ninetieth девяносто девяностый");
        Add(100,"hundred hundredth сто сотый сотого"); Add(200,"двести"); Add(300,"триста");
        Add(400,"четыреста"); Add(500,"пятьсот"); Add(600,"шестьсот"); Add(700,"семьсот"); Add(800,"восемьсот"); Add(900,"девятьсот");
        Add(1000,"thousand thousandth тысяча тысячи тысяч тысячный");
        return result;
    }
    public static TitleFingerprint Create(string title)
    {
        var value = title.Normalize(NormalizationForm.FormKC);
        value = Regex.Replace(value,@"\b[IVXLCDM]+\b",m =>
        {
            if(!Regex.IsMatch(m.Value.ToLowerInvariant(),@"^m{0,3}(cm|cd|d?c{0,3})(xc|xl|l?x{0,3})(ix|iv|v?i{0,3})$")) return m.Value;
            int total=0,previous=0;
            foreach(var c in m.Value.ToLowerInvariant().Reverse()) { var n=c switch {'i'=>1,'v'=>5,'x'=>10,'l'=>50,'c'=>100,'d'=>500,_=>1000}; total+=n<previous?-n:n;previous=n; }
            return total.ToString(CultureInfo.InvariantCulture);
        });
        value = value.ToLowerInvariant().Replace('ё','е');
        value = WordSequence.Replace(value, m =>
        {
            var sum=0; var group=0;
            foreach (var word in Regex.Split(m.Value,@"[\s-]+"))
            {
                var n=Words[word];
                if(n==100 && word.StartsWith("hundred")) group=Math.Max(1,group)*100;
                else if(n==1000) { sum+=Math.Max(1,group)*1000; group=0; }
                else group+=n;
            }
            return (sum+group).ToString(CultureInfo.InvariantCulture);
        });
        value = Regex.Replace(value,@"(?<=\d)(?:st|nd|rd|th)\b","");
        var numbers = Regex.Matches(value,@"\d+").Select(m => m.Value.TrimStart('0') is { Length: > 0 } n ? n : "0");
        var sequence=string.Join(",",numbers);
        value=Regex.Replace(value,@"\d+", " ");
        value=Regex.Replace(value,@"\b(?:сезон(?:а|ы|ов)?|часть|части|частей|season|seasons|part|parts)\b", "");
        return new(Regex.Replace(value,@"[^\p{L}]", ""),sequence);
    }
}
