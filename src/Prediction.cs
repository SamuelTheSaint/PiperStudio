using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PiperStudio
{
    // ================================================================== word prediction
    // Learns the words (and which word usually follows which) from what the person actually says,
    // in Persian and English, and suggests completions while they type. A small built-in word list
    // helps on day one; everything learned stays in words.txt in the settings folder.
    public class WordPredictor
    {
        readonly Dictionary<string, int> uni = new Dictionary<string, int>();
        readonly Dictionary<string, Dictionary<string, int>> bi = new Dictionary<string, Dictionary<string, int>>();
        readonly Dictionary<string, int> seed = new Dictionary<string, int>();
        public int LearnedWords { get { return uni.Count; } }
        public bool Changed;

        static readonly Regex WordRx = new Regex(@"[\p{L}\p{M}‌]+(?:['’][\p{L}]+)?", RegexOptions.Compiled);

        public static string Norm(string w)
        {
            if (w == null) return "";
            w = w.Replace('ي', 'ی').Replace('ى', 'ی').Replace('ك', 'ک').Replace('’', '\'').Trim('‌');
            return w.ToLowerInvariant();
        }

        public static List<string> Words(string text)
        {
            var l = new List<string>();
            foreach (Match m in WordRx.Matches(text ?? "")) l.Add(m.Value);
            return l;
        }

        public void Seed(IEnumerable<string> words)
        {
            int rank = 0;
            foreach (var w in words)
            {
                string n = Norm(w);
                if (n.Length > 0 && !seed.ContainsKey(n)) seed[n] = Math.Max(1, 1000 - rank);   // earlier = more common
                rank++;
            }
        }

        // Learn from one thing the person said. Sentence ends break the word chain.
        public void Learn(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            foreach (var sentence in Regex.Split(text, @"[.!?؟؛;:\n\r،,]+"))
            {
                string prev = null;
                foreach (var raw in Words(sentence))
                {
                    string w = Norm(raw);
                    if (w.Length == 0 || w.Length > 40) { prev = null; continue; }
                    int c; uni.TryGetValue(w, out c); uni[w] = c + 1;
                    if (prev != null)
                    {
                        Dictionary<string, int> d;
                        if (!bi.TryGetValue(prev, out d)) bi[prev] = d = new Dictionary<string, int>();
                        int b; d.TryGetValue(w, out b); d[w] = b + 1;
                    }
                    prev = w;
                }
            }
            Changed = true;
        }

        public void Clear() { uni.Clear(); bi.Clear(); Changed = true; }

        // Splits the text before the caret into (previous word, word being typed).
        public static void Context(string before, out string prev, out string partial)
        {
            prev = null; partial = "";
            before = before ?? "";
            int i = before.Length;
            while (i > 0 && IsWordChar(before[i - 1])) i--;
            partial = before.Substring(i);
            string rest = before.Substring(0, i);
            // a sentence end between the words means "no previous word"
            int j = rest.Length;
            while (j > 0 && (rest[j - 1] == ' ' || rest[j - 1] == ' ')) j--;
            if (j > 0 && IsWordChar(rest[j - 1]))
            {
                int k = j;
                while (k > 0 && IsWordChar(rest[k - 1])) k--;
                prev = Norm(rest.Substring(k, j - k));
            }
        }

        public static bool IsWordChar(char c) { return char.IsLetter(c) || c == '‌' || char.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark || c == '\'' || c == '’'; }

        public List<string> Suggest(string prev, string partial, int n)
        {
            string p = Norm(partial);
            var score = new Dictionary<string, double>();
            Dictionary<string, int> next;
            if (prev != null && bi.TryGetValue(prev, out next))
                foreach (var kv in next)
                    if (kv.Key.StartsWith(p, StringComparison.Ordinal) && kv.Key != p) Add(score, kv.Key, 1000.0 * kv.Value);
            if (p.Length > 0)
            {
                foreach (var kv in uni)
                    if (kv.Key.Length > p.Length && kv.Key.StartsWith(p, StringComparison.Ordinal)) Add(score, kv.Key, 20.0 * kv.Value);
                foreach (var kv in seed)
                    if (kv.Key.Length > p.Length && kv.Key.StartsWith(p, StringComparison.Ordinal)) Add(score, kv.Key, kv.Value / 1000.0);
            }
            var list = score.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key.Length).Take(n).Select(kv => kv.Key).ToList();
            // keep the person's capitalisation: "Hel" → "Hello", "I'm" stays
            if (partial.Length > 0 && char.IsUpper(partial[0]))
                list = list.Select(w => char.ToUpperInvariant(w[0]) + w.Substring(1)).ToList();
            return list.Select(w => w == "i" ? "I" : w.StartsWith("i'") ? "I" + w.Substring(1) : w).ToList();
        }

        static void Add(Dictionary<string, double> d, string k, double v) { double o; d.TryGetValue(k, out o); d[k] = o + v; }

        // words.txt: "1<TAB>word<TAB>count" and "2<TAB>word<TAB>next<TAB>count"
        public void Save(string path)
        {
            var sb = new StringBuilder();
            foreach (var kv in uni.OrderByDescending(kv => kv.Value).Take(30000))
                sb.Append("1\t").Append(kv.Key).Append('\t').Append(kv.Value).Append('\n');
            var pairs = bi.SelectMany(a => a.Value.Select(b => new { a = a.Key, b = b.Key, c = b.Value })).OrderByDescending(x => x.c).Take(80000);
            foreach (var x in pairs) sb.Append("2\t").Append(x.a).Append('\t').Append(x.b).Append('\t').Append(x.c).Append('\n');
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            Changed = false;
        }

        public void Load(string path)
        {
            uni.Clear(); bi.Clear();
            if (!File.Exists(path)) return;
            foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
            {
                var p = line.Split('\t');
                int c;
                if (p.Length == 3 && p[0] == "1" && int.TryParse(p[2], out c)) uni[p[1]] = c;
                else if (p.Length == 4 && p[0] == "2" && int.TryParse(p[3], out c))
                {
                    Dictionary<string, int> d;
                    if (!bi.TryGetValue(p[1], out d)) bi[p[1]] = d = new Dictionary<string, int>();
                    d[p[2]] = c;
                }
            }
            Changed = false;
        }

        // ---------------------------------------------------------------- starter words (most common first)
        public const string SeedEnglish =
            "I you the to a and is it that what yes no please thank thanks okay ok sorry hello hi bye goodbye " +
            "my me we can do don't not have be are was will would like want need know think just so but " +
            "with for of in on at this there here now later today tomorrow yesterday time again " +
            "how why when where who which wait minute moment typing repeat understand mean sure maybe " +
            "good great fine well nice bad tired hungry thirsty help call water food bathroom toilet doctor " +
            "medicine pain hurts hurt feel feeling cold hot home work phone message talk speak said say tell " +
            "listen look see go going come coming get got make made take give put let back up down out " +
            "about all some any more less very really much many little bit lot because if then than also " +
            "your our they them their he she him her his its something nothing anything everything someone " +
            "people person friend family mother father brother sister love happy sad angry excuse please " +
            "morning afternoon evening night week weekend month year day days hour hours minutes " +
            "already still always never sometimes usually often right left wrong true false same different " +
            "question answer problem idea plan meeting appointment money buy pay price open close start stop " +
            "finish done ready busy free call me text later soon slowly quickly louder quieter " +
            "computer internet video game games play watch read write wrote send sent email telegram " +
            "could should might must may shall I'm I'll I've I'd you're you'll it's that's there's let's " +
            "doesn't didn't isn't aren't wasn't can't won't couldn't shouldn't wouldn't haven't " +
            "agree disagree interesting important difficult easy hard possible impossible maybe exactly " +
            "actually honestly probably definitely totally welcome congratulations birthday happy new";

        public const string SeedPersian =
            "من تو او ما شما آنها این آن بله نه آره نخیر لطفاً ممنون مرسی متشکرم سلام خداحافظ ببخشید " +
            "چی چه چرا کی کجا چطور چطوری چند کدام خوب خوبم خوبی بد هست است نیست بود بودم هستم " +
            "می‌خواهم می‌خوام می‌تونم نمی‌تونم می‌دونم نمی‌دونم می‌کنم می‌کنی می‌شه نمی‌شه باید شاید " +
            "الان بعداً امروز فردا دیروز امشب صبح ظهر عصر شب هفته ماه سال روز ساعت دقیقه لحظه " +
            "صبر کن کنید کنم بکن بگو بگید بگم گفتم گفتی تکرار دوباره متوجه نشدم فهمیدم یواش آروم بلندتر " +
            "دارم داری داره داریم دارید ندارم کمک آب غذا دستشویی دکتر دارو درد خسته گرسنه تشنه سرد گرم " +
            "خونه خانه کار تلفن پیام حرف صحبت بنویسم نوشتم دارم تایپ می‌کنم " +
            "با به از در برای که تا اما ولی چون اگر پس هم خیلی کمی یه یک دو سه همه هیچ چیز چیزی " +
            "دوست خانواده مادر پدر برادر خواهر مامان بابا عزیزم دوستت دارم خوشحال ناراحت عصبانی " +
            "موافقم مخالفم درسته اشتباه حتماً واقعاً دقیقاً البته معلومه نمی‌دونم باشه اوکی حله " +
            "بریم بیا برو بیاید رفتم اومدم میام میرم کجایی کجاست کی میای چه خبر خبری نیست " +
            "بازی ویدیو کامپیوتر اینترنت تلگرام پیام بده زنگ بزن زنگ می‌زنم بعداً حرف می‌زنیم " +
            "تولدت مبارک تبریک خسته نباشی خوش اومدی قربونت برم فدات شم خواهش می‌کنم";
    }
}
