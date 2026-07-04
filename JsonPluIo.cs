using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace CasScaleSender
{
    // PLU satirlarini JSON olarak okur/yazar. Cikti sekli ExcelReader.Read().Rows ile
    // BIREBIR ayni: List<Dictionary<string,string>> (OrdinalIgnoreCase). Boylece
    // PluBuilder.BuildV06 / PluReader.Columns hicbir degisiklik olmadan calisir.
    //
    // Neden JSON: Flutter POS urun katalogunu teraziye gonderirken xlsx uretmek zorunda
    // kalmasin; dogrudan JSON temp dosyasi yazip CLI'ya '--json' ile verir.
    // .NET Framework 4.8 yerlesik JavaScriptSerializer kullanilir (NuGet yok).
    public static class JsonPluIo
    {
        private static JavaScriptSerializer NewSerializer()
        {
            // Buyuk katalog (10K+ PLU) MaxJsonLength varsayilanini asabilir.
            return new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        }

        // JSON dosyasini satir sozluklerine cevirir. Beklenen format: obje dizisi
        // [ { "PLU No": "1", "Name": "ELMA", "Price": "1250", ... }, ... ]
        // Deger tipi ne olursa olsun (sayi/bool/string) string'e cevrilir.
        public static List<Dictionary<string, string>> Read(string path)
        {
            var rows = new List<Dictionary<string, string>>();
            string text = File.ReadAllText(path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(text)) return rows;

            var parsed = NewSerializer().DeserializeObject(text);
            var array = parsed as object[];
            if (array == null)
                throw new Exception("JSON kok ogesi bir dizi (array) olmali.");

            foreach (var item in array)
            {
                var obj = item as Dictionary<string, object>;
                if (obj == null) continue;
                var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in obj)
                    dict[kv.Key] = ValueToString(kv.Value);
                rows.Add(dict);
            }
            return rows;
        }

        // Receive ciktisi: satirlari verilen kolon sirasiyla JSON dizisi olarak yazar.
        // (Flutter xlsx okuyamadigi icin '--json' cikti secenegi.)
        public static void Write(string path, string[] columns, List<Dictionary<string, string>> rows)
        {
            var list = new List<Dictionary<string, string>>(rows.Count);
            foreach (var row in rows)
            {
                var ordered = new Dictionary<string, string>();
                foreach (var col in columns)
                {
                    string v;
                    ordered[col] = (row != null && row.TryGetValue(col, out v)) ? (v ?? "") : "";
                }
                list.Add(ordered);
            }
            string json = NewSerializer().Serialize(list);
            File.WriteAllText(path, json, new UTF8Encoding(false));
        }

        // JSON degeri -> string (kultur-bagimsiz; ondalik/bool guvenli).
        private static string ValueToString(object v)
        {
            if (v == null) return "";
            if (v is string) return (string)v;
            if (v is bool) return ((bool)v) ? "true" : "false";
            if (v is IFormattable) return ((IFormattable)v).ToString(null, CultureInfo.InvariantCulture);
            return Convert.ToString(v, CultureInfo.InvariantCulture) ?? "";
        }
    }
}
