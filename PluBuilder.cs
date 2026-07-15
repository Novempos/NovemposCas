using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CasScaleSender
{
    // Bir PLU alaninin CAS protokolundeki sabit genisligi astigini bildirir.
    // Eskiden Num() bu durumda degeri SESSIZCE sondan kirpip oyle kodluyordu; bu da
    // teraziye farkli bir PLU numarasi/degeri gonderilip yanlislikla baska bir kaydin
    // uzerine yazilmasina yol acabiliyordu (orn. 7 haneli bir "PLU No", 6 haneli alana
    // kirpilinca sessizce baska bir PLU'ya donusuyordu). Artik bu durumda BuildV06 bu
    // hatayi firlatir; GUI/CLI yakalayip hangi alanin/degerin tastigini gosterir ve
    // gonderimi durdurur.
    public sealed class PluFieldOverflowException : Exception
    {
        public readonly string FieldName;
        public readonly string Value;
        public readonly int MaxWidth;

        public PluFieldOverflowException(string fieldName, string value, int maxWidth)
            : base(BuildMessage(fieldName, value, maxWidth))
        {
            FieldName = fieldName;
            Value = value;
            MaxWidth = maxWidth;
        }

        private static string BuildMessage(string fieldName, string value, int maxWidth)
        {
            int len = value == null ? 0 : value.Length;
            return string.Format(
                "'{0}' alani izin verilen {1} haneyi asiyor (deger: '{2}', {3} hane).",
                fieldName, maxWidth, value, len);
        }
    }

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
            sb.Append(Num(C("Department No", "1"), 4, "Department No"));   // Departman No
            sb.Append(Num(C("PLU No", "1"), 6, "PLU No"));                 // PLU No
            sb.Append(Num(C("PLU Type", "1"), 2, "PLU Type"));             // PLU tipi (1=tartili, 2=adet)
            sb.Append(Num(C("Unit Weight", "1"), 2, "Unit Weight"));       // Birim agirlik
            sb.Append(Num(C("Price", "0"), 10, "Price"));                  // Birim fiyat (tam sayi, orn 55.00 -> 5500)
            sb.Append(Num(C("Group No", "1"), 4, "Group No"));             // Grup No
            sb.Append(Num(C("ItemCode", "0"), 13, "ItemCode"));            // Urun kodu
            sb.Append(Num("0", 4, "Tare No"));                             // Tare No
            sb.Append(Num("0", 10, "Tare"));                               // Tare
            sb.Append(Num(C("Pieces", "1"), 6, "Pieces"));                 // Adet
            sb.Append(Num(C("Qty Unit No", "1"), 2, "Qty Unit No"));       // Miktar birim sembolu
            sb.Append(Num("0", 6, "Sell by date"));                        // Sell by date
            sb.Append(Num("0", 6, "Sell by time"));                        // Sell by time
            sb.Append(Num("0", 6, "Pack date"));                           // Pack date
            sb.Append(Num("0", 6, "Pack time"));                           // Pack time
            sb.Append(Num("0", 6, "Produce date"));                        // Produce date
            sb.Append(Num("0", 6, "Ingredient No"));                       // Ingredient No
            sb.Append(Num(C("Use Fixed Price Type", "0"), 2, "Use Fixed Price Type")); // Sabit fiyat kullan
            sb.Append(Num("0", 6, "Traceability No"));                     // Traceability No
            sb.Append(Num(C("Origin No", "0"), 6, "Origin No"));           // Mensei No
            sb.Append(Num("0", 4, "Nutrifact"));                           // Nutrifact
            sb.Append(Num(C("Label No", "1"), 4, "Label No"));             // Etiket No
            sb.Append(Num(C("Aux Label No", "0"), 4, "Aux Label No"));     // Yardimci etiket No
            sb.Append(Num("1", 4, "Barcode No"));                          // Barcode No
            sb.Append(Num("0", 4, "Barcode2 No"));                         // Barcode2 No
            sb.Append(Num("0", 4, "Sale Message"));                        // Sale Message
            sb.Append(Num("0", 10, "Special Price"));                      // Special Price
            sb.Append(Num(C("FixedWeight", "0"), 10, "FixedWeight"));      // Sabit agirlik
            sb.Append(Num("0", 2, "Picture No"));                          // Picture No
            sb.Append(Num("0", 2, "Packdate flag"));                       // Packdate flag
            sb.Append(Num("0", 2, "Packtime flag"));                       // Packtime flag
            sb.Append(Num("0", 2, "Sellbydate flag"));                     // Sellbydate flag
            sb.Append(Num("0", 2, "Sellbytime flag"));                     // Sellbytime flag
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
            sb.Append(Num("0", 2, "Korea Traceability flag"));             // Korea Traceability flag
            sb.Append(Txt("", 4096, enc));                // Direct Ingredient
            sb.Append(Txt(C("ExeBarcode", ""), 50, enc)); // Ext Barcode
            sb.Append(Txt(C("Prefix", ""), 10, enc));     // Prefix
            sb.Append(Num("0", 2, "Tax No"));                               // Tax No
            sb.Append(Num("0", 5, "% Tare"));                               // % Tare
            sb.Append(Num("0", 5, "Tare % limit"));                         // Tare % limit
            sb.Append(Num("0", 6, "Cook By Date"));                         // Cook By Date
            sb.Append(Num("0", 6, "Bonus"));                                // Bonus
            sb.Append(Num("0", 4, "Reference Dept"));                       // Reference Dept
            sb.Append(Num("0", 6, "Reference PLU"));                        // Reference PLU
            sb.Append(Num("0", 4, "Coupled Dept"));                         // Coupled Dept
            sb.Append(Num("0", 6, "Coupled PLU"));                          // Coupled PLU
            sb.Append(Num("0", 2, "Link PLU Count"));                       // Link PLU Count
            sb.Append(Num("0", 4, "Link Dept 1"));                          // Link Dept 1
            sb.Append(Num("0", 6, "Link PLU 1"));                           // Link PLU 1
            sb.Append(Num("0", 4, "Link Dept 2"));                          // Link Dept 2
            sb.Append(Num("0", 6, "Link PLU 2"));                           // Link PLU 2
            sb.Append(Txt("", 50, enc));                  // Image name
            return sb.ToString();
        }

        // Sayisal alan: sadece rakamlar alinir, sola sifir doldurulur.
        // "55.00" ve "5500" ayni sonucu verir (5500) -> fiyat tam sayi birimindedir.
        //
        // Deger, alanin sabit protokol genisligine (width) sigmiyorsa ARTIK SESSIZCE
        // KIRPILMAZ: PluFieldOverflowException firlatilir. Eski davranis (en sagdaki
        // `width` haneyi tutup fazlasini bastan atmak) terazide farkli bir PLU
        // numarasi/degeri uretip baska bir kaydin uzerine sessizce yazilmasina yol
        // acabiliyordu — bu metot artik boyle bir veriyi asla teraziye gondermez.
        private static string Num(string s, int width, string fieldName)
        {
            string digits = "";
            if (!string.IsNullOrEmpty(s))
                digits = new string(s.Where(char.IsDigit).ToArray());

            // Anlamli uzunluk karsilastirmasi icin bastaki sifirlari at ("0000123" -> "123",
            // 3 haneli bir deger sayilmali; hepsi sifirsa "0" olarak kalsin).
            digits = digits.TrimStart('0');
            if (digits.Length == 0) digits = "0";

            if (digits.Length > width)
                throw new PluFieldOverflowException(fieldName, digits, width);

            return digits.PadLeft(width, '0');
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
