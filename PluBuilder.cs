using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CasScaleSender
{
    // Excel satirini, OCX'in SendDataString ile bekledigi V06 PLU kaydina cevirir.
    // Alan sirasi/genislikleri resmi CAS ornegindeki MakePLUSampleData_V06 ile birebir aynidir.
    // Excel'de olmayan alanlar guvenli varsayilan degerlerle doldurulur.
    public static class PluBuilder
    {
        public static string BuildV06(Dictionary<string, string> row, Encoding enc)
        {
            Func<string, string, string> C = (name, def) =>
            {
                string v;
                if (row != null && row.TryGetValue(name, out v) && !string.IsNullOrWhiteSpace(v)) return v;
                return def;
            };

            var sb = new StringBuilder();
            sb.Append(Num(C("Department No", "1"), 4));   // Departman No
            sb.Append(Num(C("PLU No", "1"), 6));          // PLU No
            sb.Append(Num(C("PLU Type", "1"), 2));        // PLU tipi (1=tartili, 2=adet)
            sb.Append(Num(C("Unit Weight", "1"), 2));     // Birim agirlik
            sb.Append(Num(C("Price", "0"), 10));          // Birim fiyat (tam sayi, orn 55.00 -> 5500)
            sb.Append(Num(C("Group No", "1"), 4));        // Grup No
            sb.Append(Num(C("ItemCode", "0"), 13));       // Urun kodu
            sb.Append(Num("0", 4));                       // Tare No
            sb.Append(Num("0", 10));                      // Tare
            sb.Append(Num(C("Pieces", "1"), 6));          // Adet
            sb.Append(Num(C("Qty Unit No", "1"), 2));     // Miktar birim sembolu
            sb.Append(Num("0", 6));                       // Sell by date
            sb.Append(Num("0", 6));                       // Sell by time
            sb.Append(Num("0", 6));                       // Pack date
            sb.Append(Num("0", 6));                       // Pack time
            sb.Append(Num("0", 6));                       // Produce date
            sb.Append(Num("0", 6));                       // Ingredient No
            sb.Append(Num(C("Use Fixed Price Type", "0"), 2)); // Sabit fiyat kullan
            sb.Append(Num("0", 6));                       // Traceability No
            sb.Append(Num(C("Origin No", "0"), 6));       // Mensei No
            sb.Append(Num("0", 4));                       // Nutrifact
            sb.Append(Num(C("Label No", "1"), 4));        // Etiket No
            sb.Append(Num(C("Aux Label No", "0"), 4));    // Yardimci etiket No
            sb.Append(Num("1", 4));                       // Barcode No
            sb.Append(Num("0", 4));                       // Barcode2 No
            sb.Append(Num("0", 4));                       // Sale Message
            sb.Append(Num("0", 10));                      // Special Price
            sb.Append(Num(C("FixedWeight", "0"), 10));    // Sabit agirlik
            sb.Append(Num("0", 2));                       // Picture No
            sb.Append(Num("0", 2));                       // Packdate flag
            sb.Append(Num("0", 2));                       // Packtime flag
            sb.Append(Num("0", 2));                       // Sellbydate flag
            sb.Append(Num("0", 2));                       // Sellbytime flag
            sb.Append(Txt("", 8, enc));                   // Reserve
            for (int i = 0; i < 8; i++) sb.Append("01");  // name1..8 font size
            sb.Append(Txt(C("Name", ""), 55, enc));       // name1
            sb.Append(Txt(C("Name2", ""), 55, enc));      // name2
            sb.Append(Txt(C("Name3", ""), 55, enc));      // name3
            sb.Append(Txt("", 55, enc));                  // name4
            sb.Append(Txt("", 55, enc));                  // name5
            sb.Append(Txt("", 55, enc));                  // name6
            sb.Append(Txt("", 55, enc));                  // name7
            sb.Append(Txt("", 55, enc));                  // name8
            sb.Append(Num("0", 2));                       // Korea Traceability flag
            sb.Append(Txt("", 4096, enc));                // Direct Ingredient
            sb.Append(Txt(C("ExeBarcode", ""), 50, enc)); // Ext Barcode
            sb.Append(Txt(C("Prefix", ""), 10, enc));     // Prefix
            sb.Append(Num("0", 2));                       // Tax No
            sb.Append(Num("0", 5));                       // % Tare
            sb.Append(Num("0", 5));                       // Tare % limit
            sb.Append(Num("0", 6));                       // Cook By Date
            sb.Append(Num("0", 6));                       // Bonus
            sb.Append(Num("0", 4));                       // Reference Dept
            sb.Append(Num("0", 6));                       // Reference PLU
            sb.Append(Num("0", 4));                       // Coupled Dept
            sb.Append(Num("0", 6));                       // Coupled PLU
            sb.Append(Num("0", 2));                       // Link PLU Count
            sb.Append(Num("0", 4));                       // Link Dept 1
            sb.Append(Num("0", 6));                       // Link PLU 1
            sb.Append(Num("0", 4));                       // Link Dept 2
            sb.Append(Num("0", 6));                       // Link PLU 2
            sb.Append(Txt("", 50, enc));                  // Image name
            return sb.ToString();
        }

        // Sayisal alan: sadece rakamlar alinir, sola sifir doldurulur.
        // "55.00" ve "5500" ayni sonucu verir (5500) -> fiyat tam sayi birimindedir.
        private static string Num(string s, int width)
        {
            long v = 0;
            if (!string.IsNullOrEmpty(s))
            {
                var digits = new string(s.Where(char.IsDigit).ToArray());
                if (digits.Length > 0) long.TryParse(digits, out v);
            }
            string str = v.ToString();
            if (str.Length > width) str = str.Substring(str.Length - width);
            return str.PadLeft(width, '0');
        }

        // Metin alani: kod sayfasindaki byte uzunluguna gore saga bosluk doldurulur.
        private static string Txt(string s, int width, Encoding enc)
        {
            if (s == null) s = "";
            while (s.Length > 0 && enc.GetByteCount(s) > width) s = s.Substring(0, s.Length - 1);
            int pad = width - enc.GetByteCount(s);
            if (pad < 0) pad = 0;
            return s + new string(' ', pad);
        }
    }
}
