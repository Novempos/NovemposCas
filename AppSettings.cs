using System;
using System.Collections.Generic;
using System.IO;

namespace CasScaleSender
{
    // IP/Port ve diger ayarlar kullaniciya ozel klasorde tutulur:
    //   %APPDATA%\Novempos\ayarlar.txt
    // (Program Files'a kurulunca exe yanina yazilamayacagi icin AppData kullanilir.)
    // Program acilip kapaninca kalir; dosyayi elle de duzenleyebilirsiniz.
    public class AppSettings
    {
        public const int MaxScales = 10;

        // Eski tek-terazi alanlari (geriye donuk uyum + CLI varsayilanlari icin korunur).
        // GUI artik Scales listesini kullanir; kaydederken bu alanlar Scales[0]'a esitlenir.
        public string Ip = "192.168.1.1";
        public int Port = 20304;             // CAS CL serisi standart veri portu
        public int Model = 5000;             // 5000 = CL5000 ailesi (CL3000 protokol uyumlu)
        public string Version = "";          // bos birakilabilir
        public int PluDataType = 98;         // 98 = PLU V06 (gerekirse 97=V05 / 9=V02)
        public string EncodingName = "windows-1254"; // Turkce karakter kod sayfasi
        public string LastExcel = "";
        public string PrinterName = "";      // 80mm liste yazicisi (secilen hatirlanir)
        public int BlankRows = 0;            // yazdirmada sona eklenecek bos satir sayisi

        // Tanimli teraziler (en fazla MaxScales).
        public List<ScaleConfig> Scales = new List<ScaleConfig>();

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
            // scaleN.* satirlarini indexe gore topla: index -> (alan -> deger)
            var scaleFields = new Dictionary<int, Dictionary<string, string>>();
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

                        // scale<index>.<alan> = deger
                        if (k.StartsWith("scale") && k.Contains("."))
                        {
                            int dot = k.IndexOf('.');
                            int idx;
                            if (int.TryParse(k.Substring(5, dot - 5), out idx))
                            {
                                string field = k.Substring(dot + 1);
                                Dictionary<string, string> map;
                                if (!scaleFields.TryGetValue(idx, out map))
                                { map = new Dictionary<string, string>(); scaleFields[idx] = map; }
                                map[field] = v;
                            }
                            continue;
                        }

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

            // Terazileri index sirasina gore kur.
            var indices = new List<int>(scaleFields.Keys);
            indices.Sort();
            foreach (var idx in indices)
            {
                var map = scaleFields[idx];
                var sc = new ScaleConfig();
                string val;
                if (map.TryGetValue("name", out val)) sc.Name = val;
                if (map.TryGetValue("ip", out val)) sc.Ip = val;
                if (map.TryGetValue("port", out val)) int.TryParse(val, out sc.Port);
                if (map.TryGetValue("model", out val)) int.TryParse(val, out sc.Model);
                if (map.TryGetValue("datatype", out val)) int.TryParse(val, out sc.DataType);
                // scaleN.version yoksa (eski ayarlar.txt) eski global "version="a dus,
                // boylece onceden etkili olan versiyon sessizce kaybolmaz.
                sc.Version = map.TryGetValue("version", out val) ? val : s.Version;
                s.Scales.Add(sc);
                if (s.Scales.Count >= MaxScales) break;
            }

            // Hic terazi yoksa eski tek-IP ayarindan bir tane uret (migrasyon).
            if (s.Scales.Count == 0 && !string.IsNullOrWhiteSpace(s.Ip))
            {
                s.Scales.Add(new ScaleConfig
                {
                    Name = "Terazi 1",
                    Ip = s.Ip,
                    Port = s.Port,
                    Model = s.Model,
                    DataType = s.PluDataType,
                    Version = s.Version
                });
            }
            return s;
        }

        public void Save()
        {
            try
            {
                // Eski alanlari ilk teraziye esitle (CLI varsayilanlari bunlari okur).
                if (Scales.Count > 0)
                {
                    Ip = Scales[0].Ip;
                    Port = Scales[0].Port;
                    Model = Scales[0].Model;
                    PluDataType = Scales[0].DataType;
                    Version = Scales[0].Version;
                }

                var lines = new List<string>
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
                    "blankrows=" + BlankRows,
                    "scalecount=" + Scales.Count
                };
                for (int i = 0; i < Scales.Count; i++)
                {
                    var sc = Scales[i];
                    lines.Add("scale" + i + ".name=" + sc.Name);
                    lines.Add("scale" + i + ".ip=" + sc.Ip);
                    lines.Add("scale" + i + ".port=" + sc.Port);
                    lines.Add("scale" + i + ".model=" + sc.Model);
                    lines.Add("scale" + i + ".datatype=" + sc.DataType);
                    lines.Add("scale" + i + ".version=" + sc.Version);
                }

                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllLines(FilePath, lines);
            }
            catch { }
        }
    }
}
