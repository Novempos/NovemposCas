using System;
using System.Collections.Generic;
using System.Text;

namespace CasScaleSender
{
    // Teraziden ReadPLU ile gelen V06 PLU string'ini (PluBuilder.BuildV06'nin tersi)
    // import formatindaki kolonlara cevirir. windows-1254 tek-bayt oldugu icin
    // byte genislikleri = karakter genislikleri kabul edilir.
    public static class PluReader
    {
        // Import dosyasindaki kolon sirasi (ExcelReader/PluBuilder ile uyumlu).
        public static readonly string[] Columns =
        {
            "Department No", "PLU No", "PLU Type", "ItemCode", "Name", "Name2", "Name3",
            "Group No", "ExeBarcode", "Label No", "Aux Label No", "Origin No",
            "Unit Weight", "FixedWeight", "Prefix", "Pieces", "Qty Unit No", "Price",
            "Use Fixed Price Type"
        };

        // sData bos/gecersizse null doner.
        public static Dictionary<string, string> Parse(string sData)
        {
            if (string.IsNullOrEmpty(sData)) return null;

            int pos = 0;
            string s = sData;
            Func<int, string> Take = (w) =>
            {
                if (pos >= s.Length) return "";
                int take = Math.Min(w, s.Length - pos);
                string r = s.Substring(pos, take);
                pos += w; // eksik olsa bile mantiksal olarak ilerle
                return r;
            };

            // BuildV06 ile BIREBIR ayni sirada oku; ilgilenmediklerimizi atla.
            string dept = Take(4);
            string pluNo = Take(6);
            string pluType = Take(2);
            string unitWeight = Take(2);
            string price = Take(10);
            string groupNo = Take(4);
            string itemCode = Take(13);
            Take(4);  // Tare No
            Take(10); // Tare
            string pieces = Take(6);
            string qtyUnit = Take(2);
            Take(6); Take(6); Take(6); Take(6); Take(6); Take(6); // sellby/pack/produce/ingredient
            string useFixedPrice = Take(2);
            Take(6); // Traceability
            string originNo = Take(6);
            Take(4); // Nutrifact
            string labelNo = Take(4);
            string auxLabelNo = Take(4);
            Take(4); Take(4); Take(4); Take(10); // Barcode/Barcode2/SaleMsg/SpecialPrice
            string fixedWeight = Take(10);
            Take(2); Take(2); Take(2); Take(2); Take(2); // Picture + 4 flag
            Take(8);  // Reserve
            Take(16); // name1..8 font size (8 x "01")
            string name1 = Take(55);
            string name2 = Take(55);
            string name3 = Take(55);
            Take(55); Take(55); Take(55); Take(55); Take(55); // name4..8
            Take(2);    // Korea Traceability flag
            Take(4096); // Direct Ingredient
            string extBarcode = Take(50);
            string prefix = Take(10);

            // Tamamen bos kayit (PLU No 0) -> yok say
            if (Num(pluNo) == "0" && Txt(name1).Length == 0) return null;

            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            d["Department No"] = Num(dept);
            d["PLU No"] = Num(pluNo);
            d["PLU Type"] = Num(pluType);
            d["ItemCode"] = Num(itemCode);
            d["Name"] = Txt(name1);
            d["Name2"] = Txt(name2);
            d["Name3"] = Txt(name3);
            d["Group No"] = Num(groupNo);
            d["ExeBarcode"] = Txt(extBarcode);
            d["Label No"] = Num(labelNo);
            d["Aux Label No"] = Num(auxLabelNo);
            d["Origin No"] = Num(originNo);
            d["Unit Weight"] = Num(unitWeight);
            d["FixedWeight"] = Num(fixedWeight);
            d["Prefix"] = Txt(prefix);
            d["Pieces"] = Num(pieces);
            d["Qty Unit No"] = Num(qtyUnit);
            d["Price"] = Num(price);
            d["Use Fixed Price Type"] = Num(useFixedPrice);
            return d;
        }

        // Sayisal alan: rakamlari al, bastaki sifirlari at (hepsi sifirsa "0").
        private static string Num(string s)
        {
            if (string.IsNullOrEmpty(s)) return "0";
            var sb = new StringBuilder();
            foreach (char c in s) if (c >= '0' && c <= '9') sb.Append(c);
            string d = sb.ToString().TrimStart('0');
            return d.Length == 0 ? "0" : d;
        }

        // Metin alani: sagdaki dolgu bosluklarini kirp.
        private static string Txt(string s)
        {
            return (s ?? "").TrimEnd(' ', '\0').Trim();
        }
    }
}
