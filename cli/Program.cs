using System;
using System.Collections.Generic;
using System.Drawing.Printing;
using System.IO;
using System.Text;

namespace CasScaleSender.Cli
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            try { return Run(args); }
            catch (Exception ex) { Console.Error.WriteLine("HATA: " + ex.Message); return 1; }
        }

        private static int Run(string[] args)
        {
            if (args.Length == 0) { PrintHelp(); return 0; }
            string cmd = args[0].ToLowerInvariant();
            if (cmd == "help" || cmd == "-h" || cmd == "--help" || cmd == "/?" || cmd == "?")
            { PrintHelp(); return 0; }
            if (cmd == "version" || cmd == "--version" || cmd == "-v")
            { Console.WriteLine("novempos-cli 1.7.0"); return 0; }

            var o = ParseOpts(args, 1);
            var cfg = AppSettings.Load();

            switch (cmd)
            {
                case "send": case "gonder": return DoSend(o, cfg);
                case "receive": case "al": case "read": return DoReceive(o, cfg);
                case "print": case "yazdir": return DoPrint(o, cfg);
                default:
                    Console.Error.WriteLine("Bilinmeyen komut: " + cmd + "  ('novempos-cli help' yazin)");
                    return 1;
            }
        }

        // --excel veya --json'dan PLU satirlarini yukler. Ikisi de yoksa/gecersizse
        // hata basar ve null doner (cagiran cikis kodu 2 verir). JSON ve Excel ayni
        // satir seklini (Dictionary<string,string>) uretir; downstream ayni.
        private static List<Dictionary<string, string>> LoadPluRows(Dictionary<string, string> o)
        {
            string json = Get(o, "json", null);
            string excel = Get(o, "excel", null);
            if (!string.IsNullOrWhiteSpace(json))
            {
                if (!File.Exists(json)) { Console.Error.WriteLine("JSON dosyasi bulunamadi: " + json); return null; }
                return JsonPluIo.Read(json);
            }
            if (!string.IsNullOrWhiteSpace(excel))
            {
                if (!File.Exists(excel)) { Console.Error.WriteLine("Excel dosyasi bulunamadi: " + excel); return null; }
                return ExcelReader.Read(excel).Rows;
            }
            Console.Error.WriteLine("Girdi verin: --json <dosya> veya --excel <dosya.xlsx>.");
            return null;
        }

        // ---------- SEND ----------
        private static int DoSend(Dictionary<string, string> o, AppSettings cfg)
        {
            var rows = LoadPluRows(o);
            if (rows == null) return 2;
            if (rows.Count == 0) { Console.Error.WriteLine("Gonderilecek PLU satiri yok."); return 2; }

            string ip = Get(o, "ip", cfg.Ip);
            int port = GetInt(o, "port", cfg.Port);
            int model = GetInt(o, "model", cfg.Model);
            int dataType = GetInt(o, "datatype", cfg.PluDataType);
            int timeout = GetInt(o, "timeout", 10);
            Encoding enc; try { enc = Encoding.GetEncoding(Get(o, "encoding", cfg.EncodingName)); } catch { enc = Encoding.ASCII; }

            var recs = new List<string>(); var names = new List<string>();
            foreach (var row in rows)
            {
                recs.Add(PluBuilder.BuildV06(row, enc));
                string n; row.TryGetValue("Name", out n); names.Add(n ?? "");
            }
            Console.WriteLine("Gonderiliyor: " + recs.Count + " PLU -> " + ip + ":" + port);

            using (var host = new ScaleHost(Console.WriteLine))
            {
                if (!host.Init()) return 3;
                HookCancel(host);
                var r = host.RunSend(ip, port, model, cfg.Version, dataType, recs, names, timeout * 1000);
                Console.WriteLine(string.Format("TAMAMLANDI. Basarili: {0}, Basarisiz: {1}", r[0], r[1]));
                return r[1] == 0 ? 0 : 4;
            }
        }

        // ---------- RECEIVE ----------
        private static int DoReceive(Dictionary<string, string> o, AppSettings cfg)
        {
            string outp = Get(o, "out", null);
            string jsonOut = Get(o, "json", null);
            if (string.IsNullOrWhiteSpace(outp) && string.IsNullOrWhiteSpace(jsonOut))
            { Console.Error.WriteLine("Kaydedilecek dosyayi verin: --out <dosya.xlsx> veya --json <dosya.json>"); return 2; }

            string ip = Get(o, "ip", cfg.Ip);
            int port = GetInt(o, "port", cfg.Port);
            int timeout = GetInt(o, "timeout", 10);
            int from = GetInt(o, "from", 1);
            int to = GetInt(o, "to", 100);
            int dept = GetInt(o, "dept", 1);
            if (from < 1) from = 1;
            if (to < from) { Console.Error.WriteLine("--to, --from'dan kucuk olamaz."); return 2; }

            Console.WriteLine(string.Format("Okunuyor: PLU {0}-{1} (departman {2}) <- {3}:{4}", from, to, dept, ip, port));

            // Okuma: CL-Works'un ASCII protokolu ile dogrudan TCP (OCX gerekmez).
            var got = CasNetReader.Read(ip, port, from, to, dept, timeout * 1000, Console.WriteLine);

            Console.WriteLine("Bulunan PLU: " + got.Count);
            if (got.Count == 0) { Console.WriteLine("Kaydedilecek PLU yok."); return 0; }
            if (!string.IsNullOrWhiteSpace(jsonOut))
            {
                JsonPluIo.Write(jsonOut, PluReader.Columns, got);
                Console.WriteLine("Kaydedildi (JSON): " + jsonOut + " (" + got.Count + " PLU)");
            }
            if (!string.IsNullOrWhiteSpace(outp))
            {
                XlsxWriter.Write(outp, PluReader.Columns, got);
                Console.WriteLine("Kaydedildi: " + outp + " (" + got.Count + " PLU)");
            }
            return 0;
        }

        // ---------- PRINT ----------
        private static int DoPrint(Dictionary<string, string> o, AppSettings cfg)
        {
            var rows = LoadPluRows(o);
            if (rows == null) return 2;

            int blank = GetInt(o, "blank", 0);
            float font = GetFloat(o, "font", 22f);
            string printer = Get(o, "printer", cfg.PrinterName);

            var items = new List<KeyValuePair<string, string>>();
            foreach (var row in rows)
            {
                string plu, name;
                row.TryGetValue("PLU No", out plu);
                row.TryGetValue("Name", out name);
                items.Add(new KeyValuePair<string, string>(plu ?? "", name ?? ""));
            }
            if (items.Count == 0) { Console.Error.WriteLine("Yazdirilacak PLU yok."); return 2; }

            int realCount = items.Count;
            if (blank < 0) blank = 0; if (blank > 500) blank = 500;
            if (blank > 0)
            {
                int last = 0;
                for (int i = realCount - 1; i >= 0; i--) { int v = DigitsToInt(items[i].Key); if (v > 0) { last = v; break; } }
                for (int k = 1; k <= blank; k++) items.Add(new KeyValuePair<string, string>((last + k).ToString(), ""));
            }

            var pp = new PluListPrinter(items, "PLU LISTESI", DateTime.Now, realCount) { BodyPointSize = font };
            using (var doc = pp.BuildDocument())
            {
                if (!string.IsNullOrEmpty(printer)) doc.PrinterSettings.PrinterName = printer;
                if (!doc.PrinterSettings.IsValid)
                { Console.Error.WriteLine("Yazici bulunamadi: " + doc.PrinterSettings.PrinterName + "  (--printer <ad> verin)"); return 2; }
                doc.Print();
                Console.WriteLine(string.Format("Yazdirildi: {0} PLU{1} -> {2}",
                    realCount, blank > 0 ? " + " + blank + " bos" : "", doc.PrinterSettings.PrinterName));
            }
            return 0;
        }

        // ---------- yardimcilar ----------
        private static void HookCancel(ScaleHost host)
        {
            Console.CancelKeyPress += (s, e) => { e.Cancel = true; host.RequestCancel(); };
        }

        private static Dictionary<string, string> ParseOpts(string[] args, int start)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = start; i < args.Length; i++)
            {
                string a = args[i];
                if (a.StartsWith("--")) a = a.Substring(2);
                else if (a.StartsWith("-")) a = a.Substring(1);
                else continue;
                string val = (i + 1 < args.Length && !args[i + 1].StartsWith("-")) ? args[++i] : "true";
                d[a] = val;
            }
            return d;
        }

        private static string Get(Dictionary<string, string> o, string k, string def)
        { string v; return o.TryGetValue(k, out v) ? v : def; }
        private static int GetInt(Dictionary<string, string> o, string k, int def)
        { string v; int r; return (o.TryGetValue(k, out v) && int.TryParse(v, out r)) ? r : def; }
        private static float GetFloat(Dictionary<string, string> o, string k, float def)
        { string v; float r; return (o.TryGetValue(k, out v) && float.TryParse(v, out r)) ? r : def; }

        private static int DigitsToInt(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            var sb = new StringBuilder();
            foreach (char c in s) if (c >= '0' && c <= '9') sb.Append(c);
            int v; return (sb.Length > 0 && int.TryParse(sb.ToString(), out v)) ? v : 0;
        }

        private static void PrintHelp()
        {
            Console.WriteLine(@"
NOVEMPOS TERAZI - Terminal (novempos-cli)
=========================================
CAS CL serisi terazilere Excel'den PLU gonderir, teraziden PLU okur ve
80mm fis yazicisina PLU listesi basar.

KULLANIM:
  novempos-cli <komut> [secenekler]

KOMUTLAR:
  send      Excel'deki PLU'lari teraziye gonderir   (GONDER)
  receive   Teraziden PLU okuyup .xlsx kaydeder      (AL)
  print     Excel'deki PLU listesini yaziciya basar
  help      Bu yardimi gosterir
  version   Surumu gosterir

ORTAK SECENEKLER (terazi):
  --ip <ip>          Terazi IP        (varsayilan: ayarlardan)
  --port <n>         Port             (varsayilan 20304)
  --model <n>        Model            (varsayilan 5000 = CL5000/CL3000)
  --datatype <n>     PLU veri tipi    (98=V06, 97=V05, 9=V02)
  --encoding <ad>    Kod sayfasi      (varsayilan windows-1254)
  --timeout <sn>     Yanit bekleme    (varsayilan 10)
  (Terazi ayarlari verilmezse GUI'nin kaydettigi ayarlar.txt'ten okunur.)

send:
  --excel <dosya>    Gonderilecek .xlsx           (--excel VEYA --json)
  --json <dosya>     Gonderilecek .json           (obje dizisi; POS entegrasyonu)

receive:
  --out <dosya>      Kaydedilecek .xlsx           (--out VEYA --json, ikisi de olur)
  --json <dosya>     Kaydedilecek .json           (POS entegrasyonu)
  --from <n>         Baslangic PLU No             (varsayilan 1)
  --to <n>           Bitis PLU No                 (varsayilan 100)
  --dept <n>         Departman No                 (varsayilan 1)

print:
  --excel <dosya>    Yazdirilacak .xlsx           (--excel VEYA --json)
  --json <dosya>     Yazdirilacak .json           (POS entegrasyonu)
  --printer <ad>     Yazici adi          (varsayilan: sistem varsayilani)
  --blank <n>        Sona bos satir (son PLU'dan devam, ad bos)  (varsayilan 0)
  --font <punto>     Yazi puntosu                 (varsayilan 22)

ORNEKLER:
  novempos-cli send --excel C:\plu\liste.xlsx --ip 192.168.1.1 --port 20304
  novempos-cli receive --out C:\plu\terazi.xlsx --from 1 --to 500
  novempos-cli print --excel C:\plu\liste.xlsx --printer ""XP-80C"" --blank 10
  novempos-cli print --excel C:\plu\liste.xlsx --font 18

NOTLAR:
  * Terazi islemleri icin CasScale.ocx kayitli olmali (kurulum otomatik yapar).
  * Islemi yarida kesmek icin Ctrl+C (AL'da o ana kadar okunanlar kaydedilmez;
    kaydetmek icin islemi tamamlayin).
  * Cikis kodu: 0=basarili, digerleri=hata/eksik.
");
        }
    }
}
