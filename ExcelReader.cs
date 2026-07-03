using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CasScaleSender
{
    // Harici kutuphane (NuGet) gerektirmeden .xlsx okur.
    // .xlsx aslinda bir ZIP; icindeki XML'leri ayristirir.
    public static class ExcelReader
    {
        public class Sheet
        {
            public List<string> Headers = new List<string>();
            public List<Dictionary<string, string>> Rows = new List<Dictionary<string, string>>();
        }

        public static Sheet Read(string path)
        {
            using (var zip = ZipFile.OpenRead(path))
            {
                // 1) Ortak string tablosu (sharedStrings.xml)
                var shared = new List<string>();
                var ssEntry = zip.Entries.FirstOrDefault(e =>
                    e.FullName.Equals("xl/sharedStrings.xml", StringComparison.OrdinalIgnoreCase));
                if (ssEntry != null)
                {
                    var doc = Load(ssEntry);
                    foreach (var si in doc.Root.Elements().Where(x => x.Name.LocalName == "si"))
                        shared.Add(string.Concat(si.Descendants()
                            .Where(x => x.Name.LocalName == "t").Select(x => x.Value)));
                }

                // 2) Ilk sayfa (sheet1.xml, sheet2.xml ... arasindan ilki)
                var sheetEntry = zip.Entries
                    .Where(e => Regex.IsMatch(e.FullName, @"^xl/worksheets/sheet\d+\.xml$",
                        RegexOptions.IgnoreCase))
                    .OrderBy(e => e.FullName).FirstOrDefault();
                if (sheetEntry == null)
                    throw new Exception("Excel icinde calisma sayfasi bulunamadi.");

                var sheetDoc = Load(sheetEntry);
                var rowsXml = sheetDoc.Descendants().Where(x => x.Name.LocalName == "row").ToList();

                var result = new Sheet();
                if (rowsXml.Count == 0) return result;

                // Basliklar = ilk satir
                var headerRow = ReadRow(rowsXml[0], shared);
                int maxCol = headerRow.Keys.Count == 0 ? 0 : headerRow.Keys.Max() + 1;
                for (int c = 0; c < maxCol; c++)
                {
                    string h;
                    result.Headers.Add(headerRow.TryGetValue(c, out h) ? (h ?? "").Trim() : "");
                }

                // Veri satirlari
                for (int r = 1; r < rowsXml.Count; r++)
                {
                    var cells = ReadRow(rowsXml[r], shared);
                    if (cells.Count == 0) continue;
                    if (cells.Values.All(v => string.IsNullOrWhiteSpace(v))) continue;

                    var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    for (int c = 0; c < result.Headers.Count; c++)
                    {
                        string val;
                        cells.TryGetValue(c, out val);
                        var key = result.Headers[c];
                        if (!string.IsNullOrEmpty(key) && !dict.ContainsKey(key))
                            dict[key] = val ?? "";
                    }
                    result.Rows.Add(dict);
                }
                return result;
            }
        }

        private static XDocument Load(ZipArchiveEntry entry)
        {
            using (var s = entry.Open())
                return XDocument.Load(s);
        }

        // Bir <row> icindeki hucreleri (kolonIndex -> deger) olarak dondurur.
        private static Dictionary<int, string> ReadRow(XElement row, List<string> shared)
        {
            var map = new Dictionary<int, string>();
            foreach (var c in row.Elements().Where(x => x.Name.LocalName == "c"))
            {
                string reference = (string)c.Attribute("r");     // orn "B3"
                int col = ColIndex(reference);
                string type = (string)c.Attribute("t");          // s, str, inlineStr, b ...
                string text = "";

                if (type == "s") // sharedStrings indexi
                {
                    var v = c.Elements().FirstOrDefault(x => x.Name.LocalName == "v");
                    int idx;
                    if (v != null && int.TryParse(v.Value, out idx) && idx >= 0 && idx < shared.Count)
                        text = shared[idx];
                }
                else if (type == "inlineStr")
                {
                    text = string.Concat(c.Descendants()
                        .Where(x => x.Name.LocalName == "t").Select(x => x.Value));
                }
                else // str (formul sonucu), b, sayi, tarih...
                {
                    var v = c.Elements().FirstOrDefault(x => x.Name.LocalName == "v");
                    if (v != null) text = v.Value;
                }

                if (col >= 0) map[col] = text;
            }
            return map;
        }

        // "B3" -> 1 (0 tabanli kolon indexi)
        private static int ColIndex(string cellRef)
        {
            if (string.IsNullOrEmpty(cellRef)) return -1;
            int col = 0, count = 0;
            foreach (char ch in cellRef)
            {
                if (ch >= 'A' && ch <= 'Z') { col = col * 26 + (ch - 'A' + 1); count++; }
                else if (ch >= 'a' && ch <= 'z') { col = col * 26 + (ch - 'a' + 1); count++; }
                else break;
            }
            return count == 0 ? -1 : col - 1;
        }
    }
}
