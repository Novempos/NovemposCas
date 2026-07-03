using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace CasScaleSender
{
    // Harici kutuphane olmadan minimal .xlsx yazar (inline string hucreler).
    // ExcelReader ile birebir tekrar okunabilir.
    public static class XlsxWriter
    {
        public static void Write(string path, string[] headers, List<Dictionary<string, string>> rows)
        {
            if (File.Exists(path)) File.Delete(path);
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                AddEntry(zip, "[Content_Types].xml", ContentTypes());
                AddEntry(zip, "_rels/.rels", RootRels());
                AddEntry(zip, "xl/workbook.xml", Workbook());
                AddEntry(zip, "xl/_rels/workbook.xml.rels", WorkbookRels());
                AddEntry(zip, "xl/worksheets/sheet1.xml", Sheet(headers, rows));
            }
        }

        private static void AddEntry(ZipArchive zip, string name, string content)
        {
            var e = zip.CreateEntry(name, CompressionLevel.Optimal);
            using (var w = new StreamWriter(e.Open(), new UTF8Encoding(false)))
                w.Write(content);
        }

        private static string Sheet(string[] headers, List<Dictionary<string, string>> rows)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");

            // Baslik satiri
            sb.Append("<row r=\"1\">");
            for (int c = 0; c < headers.Length; c++)
                Cell(sb, Col(c) + "1", headers[c]);
            sb.Append("</row>");

            // Veri satirlari
            for (int r = 0; r < rows.Count; r++)
            {
                int rowNo = r + 2;
                sb.Append("<row r=\"").Append(rowNo).Append("\">");
                for (int c = 0; c < headers.Length; c++)
                {
                    string v;
                    rows[r].TryGetValue(headers[c], out v);
                    Cell(sb, Col(c) + rowNo, v ?? "");
                }
                sb.Append("</row>");
            }

            sb.Append("</sheetData></worksheet>");
            return sb.ToString();
        }

        private static void Cell(StringBuilder sb, string reference, string value)
        {
            sb.Append("<c r=\"").Append(reference).Append("\" t=\"inlineStr\"><is><t xml:space=\"preserve\">");
            sb.Append(Esc(value));
            sb.Append("</t></is></c>");
        }

        // 0 -> A, 25 -> Z, 26 -> AA ...
        private static string Col(int index)
        {
            string s = "";
            index++;
            while (index > 0)
            {
                int m = (index - 1) % 26;
                s = (char)('A' + m) + s;
                index = (index - 1) / 26;
            }
            return s;
        }

        private static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                    .Replace("\"", "&quot;");
        }

        private static string ContentTypes()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
                "</Types>";
        }

        private static string RootRels()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
                "</Relationships>";
        }

        private static string Workbook()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" " +
                "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                "<sheets><sheet name=\"PLU\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>";
        }

        private static string WorkbookRels()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
                "</Relationships>";
        }
    }
}
