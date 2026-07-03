using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Printing;

namespace CasScaleSender
{
    // (PLU No, Urun Adi) listesini 80mm termal fis yazicisina yazdirir.
    // Sayfa boyutu YAZICININ kendi ayarindan gelir (80mm rulo) - biz sadece
    // kenar bosluklarini kucuk tutup icerigi o genislige sigdiririz. Boylece
    // yanlislikla metrelerce bos kagit sarfi olmaz.
    public class PluListPrinter
    {
        private readonly List<KeyValuePair<string, string>> items;
        private readonly string title;
        private readonly DateTime when;
        private readonly int realCount; // gercek urun sayisi (kalanlar sona eklenen bos satirlar)

        private Font fontTitle, fontHead, fontBody;
        private int idx;          // yazdirma imleci (sayfalar arasi devam eder)
        private int colChars = 3; // PLU sutun genisligi (karakter) - listeye gore hesaplanir
        private const int Gap = 2; // PLU ile urun adi arasi bosluk (karakter)

        public PluListPrinter(List<KeyValuePair<string, string>> items, string title, DateTime when, int realCount)
        {
            this.items = items;
            this.title = title;
            this.when = when;
            this.realCount = realCount;
        }

        public PrintDocument BuildDocument()
        {
            var doc = new PrintDocument();
            doc.DocumentName = "PLU Listesi";
            // Sayfa boyutunu EZMIYORUZ (rulo surekli kagit). Sadece dar kenar boslugu.
            try { doc.DefaultPageSettings.Margins = new Margins(6, 8, 8, 10); } catch { }

            doc.BeginPrint += (s, e) => { idx = 0; MakeFonts(); ComputeColWidth(); };
            doc.EndPrint += (s, e) => DisposeFonts();
            doc.PrintPage += OnPrintPage;
            return doc;
        }

        // Govde metni punto olarak; baslik/ara basliklar orantili. Varsayilan 22.
        public float BodyPointSize = 22f;

        private void MakeFonts()
        {
            float p = BodyPointSize > 0 ? BodyPointSize : 22f;
            fontTitle = new Font("Consolas", p + 4f, FontStyle.Bold);
            fontHead = new Font("Consolas", p, FontStyle.Bold);
            fontBody = new Font("Consolas", p, FontStyle.Regular);
        }

        // PLU sutununu listedeki en uzun PLU'ya gore ayarla ("PLU" basligi icin en az 3).
        private void ComputeColWidth()
        {
            int max = 3;
            foreach (var it in items)
            {
                int len = (it.Key ?? "").Trim().Length;
                if (len > max) max = len;
            }
            if (max > 6) max = 6; // PLU en fazla 6 hane
            colChars = max;
        }

        private void DisposeFonts()
        {
            if (fontTitle != null) fontTitle.Dispose();
            if (fontHead != null) fontHead.Dispose();
            if (fontBody != null) fontBody.Dispose();
            fontTitle = fontHead = fontBody = null;
        }

        private void OnPrintPage(object sender, PrintPageEventArgs e)
        {
            var g = e.Graphics;
            float left = e.MarginBounds.Left;
            float right = e.MarginBounds.Right;
            float width = right - left;
            float y = e.MarginBounds.Top;

            // Baslik yalnizca ilk sayfada
            if (idx == 0)
            {
                y = DrawCentered(g, "NOVEMPOS", fontTitle, left, width, y);
                y = DrawCentered(g, title, fontHead, left, width, y);
                y = DrawCentered(g, when.ToString("dd.MM.yyyy HH:mm"), fontHead, left, width, y);
                y += 4;
                y = DrawSeparator(g, left, right, y);
                y = DrawRow(g, "PLU", "URUN ADI", fontHead, left, width, y);
                y = DrawSeparator(g, left, right, y);
            }

            float lineH = fontBody.GetHeight(g) + 1;
            while (idx < items.Count)
            {
                if (y + lineH > e.MarginBounds.Bottom) // sayfa doldu -> devam et
                {
                    e.HasMorePages = true;
                    return;
                }
                // Urun listesi bitip bos satirlara gecerken ayrac koy
                if (idx == realCount && items.Count > realCount)
                    y = DrawSeparator(g, left, right, y);

                var it = items[idx];
                y = DrawRow(g, it.Key, it.Value, fontBody, left, width, y);
                idx++;
            }

            e.HasMorePages = false;
        }

        // PLU + bosluk + urun adi tek satir olarak, BOSLUK ile hizalanir (Consolas monospace).
        // Tasan urun adi "..." ile kirpilir.
        private float DrawRow(Graphics g, string plu, string name, Font f, float left, float width, float y)
        {
            string p = (plu ?? "").Trim();
            if (p.Length > colChars) p = p.Substring(0, colChars);
            string line = p.PadRight(colChars + Gap) + (name ?? "").Trim();

            var rect = new RectangleF(left, y, width, f.GetHeight(g) + 1);
            using (var sf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                g.DrawString(line, f, Brushes.Black, rect, sf);

            return y + f.GetHeight(g) + 1;
        }

        private float DrawCentered(Graphics g, string text, Font f, float left, float width, float y)
        {
            var sz = g.MeasureString(text, f);
            g.DrawString(text, f, Brushes.Black, left + (width - sz.Width) / 2f, y);
            return y + f.GetHeight(g) + 1;
        }

        private float DrawSeparator(Graphics g, float left, float right, float y)
        {
            using (var pen = new Pen(Color.Black, 1f))
                g.DrawLine(pen, left, y + 1, right, y + 1);
            return y + 5;
        }
    }
}
