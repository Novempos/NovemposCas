using System;
using System.IO;

namespace CasScaleSender
{
    // IP/Port ve diger ayarlar kullaniciya ozel klasorde tutulur:
    //   %APPDATA%\Novempos\ayarlar.txt
    // (Program Files'a kurulunca exe yanina yazilamayacagi icin AppData kullanilir.)
    // Program acilip kapaninca kalir; dosyayi elle de duzenleyebilirsiniz.
    public class AppSettings
    {
        public string Ip = "192.168.1.1";
        public int Port = 20304;             // CAS CL serisi standart veri portu
        public int Model = 5000;             // 5000 = CL5000 ailesi (CL3000 protokol uyumlu)
        public string Version = "";          // bos birakilabilir
        public int PluDataType = 98;         // 98 = PLU V06 (gerekirse 97=V05 / 9=V02)
        public string EncodingName = "windows-1254"; // Turkce karakter kod sayfasi
        public string LastExcel = "";
        public string PrinterName = "";      // 80mm liste yazicisi (secilen hatirlanir)
        public int BlankRows = 0;            // yazdirmada sona eklenecek bos satir sayisi

        private static string FilePath
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Novempos");
                return Path.Combine(dir, "ayarlar.txt");
            }
        }

        public static AppSettings Load()
        {
            var s = new AppSettings();
            try
            {
                if (File.Exists(FilePath))
                {
                    foreach (var line in File.ReadAllLines(FilePath))
                    {
                        var t = line.Trim();
                        if (t.Length == 0 || t.StartsWith("#")) continue;
                        int i = t.IndexOf('=');
                        if (i <= 0) continue;
                        string k = t.Substring(0, i).Trim().ToLowerInvariant();
                        string v = t.Substring(i + 1).Trim();
                        switch (k)
                        {
                            case "ip": s.Ip = v; break;
                            case "port": int.TryParse(v, out s.Port); break;
                            case "model": int.TryParse(v, out s.Model); break;
                            case "version": s.Version = v; break;
                            case "pludatatype": int.TryParse(v, out s.PluDataType); break;
                            case "encoding": s.EncodingName = v; break;
                            case "lastexcel": s.LastExcel = v; break;
                            case "printer": s.PrinterName = v; break;
                            case "blankrows": int.TryParse(v, out s.BlankRows); break;
                        }
                    }
                }
            }
            catch { }
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllLines(FilePath, new[]
                {
                    "# CAS terazi gonderici ayarlari - elle de duzenleyebilirsiniz",
                    "ip=" + Ip,
                    "port=" + Port,
                    "model=" + Model,
                    "version=" + Version,
                    "pludatatype=" + PluDataType,
                    "encoding=" + EncodingName,
                    "lastexcel=" + LastExcel,
                    "printer=" + PrinterName,
                    "blankrows=" + BlankRows
                });
            }
            catch { }
        }
    }
}
