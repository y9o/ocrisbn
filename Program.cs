using CommandLine;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;

namespace ocrisbn
{
    class Program
    {
        struct textLine
        {
            public string Text;
            public Windows.Foundation.Rect Rect;
        }
        class Options
        {
            [Option("head", Required = false, Default = 2, HelpText = "前方ファイル数")]
            public int Head { get; set; }

            [Option("tail", Required = false, Default = 16, HelpText = "後方ファイル数")]
            public int Tail { get; set; }

            [Option('l', "lang", Required = false, Default = "en-US", HelpText = "OCR言語")]
            public string Lang { get; set; }

            [Option('V', "view", Required = false, HelpText = "OCR結果を表示")]
            public bool ViewOCR { get; set; }

            [Option('s', "stop", Required = false, HelpText = "ISBNを見つけたら残り画像は無視する")]
            public bool Skip { get; set; }

            [Option('o', "out", Required = false, HelpText = "指定ファイルにISBN13番号を書き出す")]
            public string Out { get; set; }

            [Value(0, MetaName = "Input", Required = true, HelpText = "OCR対象の画像があるフォルダ")]
            public string Input { get; set; }
        }

        static Options op;

        static void Main(string[] args)
        {
            Parser.Default.ParseArguments<Options>(args).WithParsed(scanFolder);
        }
        static void scanFolder(Options _op)
        {
            op = _op;

            //OCRエンジン初期化
            OcrEngine engine = null;
            try
            {
                var lang = new Windows.Globalization.Language(op.Lang);
                if (!OcrEngine.IsLanguageSupported(lang))
                {
                    Console.Write("-lang {0}はOCRに対応していない言語です。\n", op.Lang);

                    foreach (var l in OcrEngine.AvailableRecognizerLanguages)
                    {
                        Console.Write("\t-l {0}\t{1}\n", l.LanguageTag, l.DisplayName);
                    }
                    Console.WriteLine("ms-settings:regionlanguage\n設定＞時刻と言語＞言語＞＋優先する言語を追加する＞English(United States)\n設定からen-USを追加・ダウンロードしてください。");
                    Environment.Exit(1);
                }
                engine = OcrEngine.TryCreateFromLanguage(lang);
                if (engine == null)
                {
                    Console.WriteLine("OCR初期化に失敗しました。");
                    Environment.Exit(1);
                }
            }
            catch (Exception e)
            {
                Console.Write("Error\n{0}\n", e);
                Environment.Exit(1);
            }

            //指定フォルダ
            if (System.IO.Directory.Exists(op.Input))
            {

                var isbns = new List<ISBN>();
                var filelist = System.IO.Directory.GetFiles(op.Input);
                var lastfile = "";

                //先頭から指定ファイル数をOCR
                var count = op.Head;
                foreach (var file in filelist)
                {
                    if (count <= 0) break;
                    lastfile = file;
                    Console.Write("Scan: {0}\n", file);
                    try
                    {
                        var result = scanFromImage(System.IO.Path.GetFullPath(file), engine);
                        result.Wait();
                        var n = findISBN(result.Result);
                        if (n.Count > 0)
                        {
                            isbns.AddRange(n);
                            if (op.Skip)
                            {
                                break;
                            }
                        }
                        count--;
                    }
                    catch (Exception e)
                    {

                        if ((UInt32)e.InnerException.HResult == (UInt32)0x88982F50)
                        {
                            //画像出ない
                        }
                        else
                        {
                            Console.Write("error\n{0}\n", e);
                        }
                    }
                }
                //最後尾から指定ファイル数をOCR
                Array.Reverse(filelist);
                count = op.Tail;
                foreach (var file in filelist)
                {
                    if (count <= 0) break;
                    if (op.Skip && isbns.Count > 0) break;
                    if (file == lastfile) break;

                    Console.Write("Scan: {0}\n", file);
                    try
                    {
                        var result = scanFromImage(System.IO.Path.GetFullPath(file), engine);
                        result.Wait();
                        var n = findISBN(result.Result);
                        if (n.Count > 0)
                            isbns.AddRange(n);

                        count--;
                    }
                    catch (Exception e)
                    {
                        if ((UInt32)e.InnerException.HResult == (UInt32)0x88982F50)
                        {
                            //画像ではない
                        }
                        else
                        {
                            Console.Write("error\n{0}\n", e);
                        }
                    }
                }
                //HITしたISBNをまとめて
                var isbnMap = new Dictionary<string, int>();
                foreach (var isbn in isbns)
                {
                    if (isbnMap.ContainsKey(isbn.ISBN13))
                    {
                        isbnMap[isbn.ISBN13] += isbn.Rank;
                    }
                    else
                    {
                        isbnMap[isbn.ISBN13] = isbn.Rank;
                    }
                }
                //ランクが高いISBNを一つ選択する
                {
                    string isbn = "";
                    int max = 0;
                    foreach (var k in isbnMap)
                    {
                        if (k.Value > max)
                        {
                            isbn = k.Key;
                            max = k.Value;
                        }
                    }
                    if (max > 0)
                    {
                        Console.WriteLine("ISBN: " + isbn);
                        if (op.Out != null && op.Out != "")
                        {
                            System.IO.File.WriteAllText(op.Out, isbn);
                            Console.WriteLine("output: " + op.Out);
                        }
                    }
                }
            }
            else
            {
                Console.WriteLine("対象フォルダを指定してください");
                Environment.Exit(1);
            }
        }
        static async Task<string> scanFromImage(string image, OcrEngine engine)
        {
            var file = await StorageFile.GetFileFromPathAsync(image);
            var ret = "";
            using (var stream = await file.OpenReadAsync())
            {
                var decoder = await BitmapDecoder.CreateAsync(stream);
                var r = ocrFromBMP(decoder, engine, op.ViewOCR);
                ret = r.Result;
                stream.Seek(0);
                var ro = rotateBMP(decoder);
                var mem = ro.Result;
                var rotateDecoder = await BitmapDecoder.CreateAsync(mem);
                r = ocrFromBMP(rotateDecoder, engine, false);
                r.Wait();
                mem.Dispose();
                ret += r.Result;
            }
            return ret;
        }
        static async Task<IRandomAccessStream> rotateBMP(BitmapDecoder decoder)
        {
            var mem = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateForTranscodingAsync(mem, decoder);
            encoder.BitmapTransform.Rotation = BitmapRotation.Clockwise270Degrees;
            await encoder.FlushAsync();
            return mem;
        }
        static async Task<string> ocrFromBMP(BitmapDecoder decoder, OcrEngine engine, bool view)
        {
            string ret = "";
            using (var bmp = await decoder.GetSoftwareBitmapAsync())
            {
                var result = await engine.RecognizeAsync(bmp);

                var list = new List<textLine>();
                foreach (var tmp in result.Lines)
                {
                    foreach (var word in tmp.Words)
                    {
                        bool flag = false;
                        for (var i = 0; i < list.Count; i++)
                        {
                            var t = list[i];
                            var y1 = Math.Abs(t.Rect.Y - word.BoundingRect.Y);
                            if (word.BoundingRect.Height - y1 > ((double)word.BoundingRect.Height) * 0.5)
                            {
                                var width = word.BoundingRect.Width / word.Text.Length;
                                var x1 = (t.Rect.X + t.Rect.Width) - (word.BoundingRect.X - width);
                                if (x1 >= 0 && x1 < width)
                                {
                                    t.Text += word.Text;
                                    t.Rect = word.BoundingRect;
                                    list[i] = t;
                                    flag = true;
                                }

                            }
                        }
                        if (!flag)
                        {
                            textLine t;
                            t.Text = word.Text;
                            t.Rect = word.BoundingRect;
                            list.Add(t);
                        }
                    }
                }
                foreach (var tmp in list)
                {
                    ret += tmp.Text + "\n";
                }
                var ret2 = "";
                foreach (var tmp in result.Lines)
                {
                    foreach (var word in tmp.Words)
                    {
                        ret2 += word.Text;
                    }
                    ret2 += "\n";
                }
                if (view)
                {
                    Console.WriteLine(ret2);
                }
                ret += ret2;
            }
            return ret;
        }
        static Regex regexD = new Regex(@"\D", RegexOptions.Compiled);
        static Regex regexISBN = new Regex(@"ISBN\D*(?:\d\d\d\D)?\d+\D\d+\D\d+\D\d", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        static List<ISBN> findISBN(string str)
        {
            var txts = str.Split('\n');
            var ret = new List<ISBN>();
            foreach (var txt in txts)
            {
                var r = isISBN(txt);
                if (r != null)
                {
                    ret.Add(r);
                }
                else
                {
                    var re = regexISBN.Match(txt);
                    if (re.Success)
                    {
                        r = isISBN(re.Value);
                        if (r != null)
                        {
                            ret.Add(r);
                        }
                    }
                }
            }
            return ret;
        }

        static ISBN isISBN(string txt)
        {
            var no = regexD.Replace(txt, "");
            if (no.Length == 10)
            {
                int i, s = 0, t = 0;
                for (i = 0; i < 10; i++)
                {
                    t += no[i] - '0';
                    s += t;
                }
                if (s % 11 == 0)
                {
                    var n = new ISBN(no);
                    if (regexISBN.IsMatch(txt))
                    {
                        n.Rank = 100;
                    }
                    else if (txt.Length == 10)
                    {
                        n.Rank = 10;
                    }
                    else
                    {
                        n.Rank = 1;
                    }
                    return n;
                }
            }
            else if (no.Length == 13)
            {
                if (no[0] != '9' || no[1] != '7' || no[2] < '8')
                {
                    return null;
                }
                int sum = 0;
                for (int i = 0; i < 13; i++)
                {
                    sum += (no[i] - '0') * (i % 2 == 0 ? 1 : 3);
                }
                if (sum % 10 == 0)
                {
                    var n = new ISBN(no);
                    if (regexISBN.IsMatch(txt))
                    {
                        n.Rank = 130;
                    }
                    else if (txt.Length == 13)
                    {
                        n.Rank = 13;
                    }
                    else
                    {
                        n.Rank = 1;
                    }
                    return n;
                }
            }
            return null;
        }
    }

}


