using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Printing;
using System.Text;
using System.Windows.Forms;

namespace CasScaleSender
{
    public class MainForm : Form
    {
        private const int ACTION_DOWNLOAD = 3;   // teraziye yaz
        private const int RECV_SUCCESS = 1001;
        private const int RECV_FAIL = 1002;

        private AxCASSCALELib.AxCasScale ax;
        private AppSettings cfg;

        private TextBox txtExcel, txtBlankRows;
        private Button btnBrowse, btnSend, btnReceive, btnCancel, btnPrint;
        private Button btnAddScale, btnEditScale, btnDelScale, btnTestScale;
        private CheckedListBox lstScales;
        private ListBox log;

        // Teraziden okuma (AL) durumu
        private bool receiving;
        private int readDept;
        private System.Threading.Thread readThread;
        private List<Dictionary<string, string>> received = new List<Dictionary<string, string>>();

        // Coklu gonderim durumu
        private List<string> records = new List<string>();
        private List<string> names = new List<string>();
        private int index;
        private int okCount, failCount;            // aktif terazi
        private int totalOk, totalFail;            // tum teraziler
        private string sendIp; private int sendPort, sendModel, sendDataType;
        private List<ScaleConfig> sendQueue = new List<ScaleConfig>();
        private int sendScaleIdx;
        private bool sending;
        private System.Windows.Forms.Timer sendTimer;
        private const int SEND_TIMEOUT_MS = 10000; // terazi yaniti icin bekleme suresi (10 sn)

        public MainForm()
        {
            cfg = AppSettings.Load();
            BuildUi();
            InitOcx();
            RefreshScaleList();
            // Tek terazi varsa otomatik isaretle (tek-terazi kullanicilari icin kolaylik).
            if (lstScales.Items.Count == 1) lstScales.SetItemChecked(0, true);
        }

        private void BuildUi()
        {
            Text = "Novempos - Terazi PLU Gonderici";
            LoadWindowIcon();
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(660, 600);
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(680, 520);

            int x1 = 12, y = 15, h = 26;

            // --- Excel dosyasi ---
            Add(new Label { Text = "Excel dosyasi:", Left = x1, Top = y + 4, Width = 90 });
            txtExcel = new TextBox { Left = 108, Top = y, Width = ClientSize.Width - 108 - 12 - 78, Text = cfg.LastExcel, Anchor = LR() };
            btnBrowse = new Button { Text = "Sec...", Left = ClientSize.Width - 12 - 72, Top = y - 1, Width = 72, Height = h, Anchor = TR() };
            btnBrowse.Click += (s, e) => Browse();
            Add(txtExcel); Add(btnBrowse);

            // --- Terazi listesi ---
            y += 34;
            Add(new Label { Text = "Teraziler (GONDER: coklu isaretle · AL: tek isaretle · en fazla 10):", Left = x1, Top = y, Width = 520 });
            y += 25; // etiket ile liste arasinda biraz nefes payi
            lstScales = new CheckedListBox
            {
                Left = x1,
                Top = y,
                Width = 430,
                Height = 140,
                CheckOnClick = true,
                IntegralHeight = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };
            Add(lstScales);

            int bx = x1 + 430 + 10, bw = 190;
            btnAddScale = ScaleBtn("Terazi Ekle", bx, y, bw); btnAddScale.Click += (s, e) => AddScale();
            btnEditScale = ScaleBtn("Duzenle", bx, y + 34, bw); btnEditScale.Click += (s, e) => EditScale();
            btnTestScale = ScaleBtn("Baglantiyi Test Et", bx, y + 68, bw); btnTestScale.Click += (s, e) => TestScale();
            btnDelScale = ScaleBtn("Sil", bx, y + 102, bw); btnDelScale.Click += (s, e) => DeleteScale();

            // --- Aksiyonlar ---
            y += 150;
            btnSend = new Button { Text = "GONDER", Left = x1, Top = y, Width = 120, Height = 36, Font = new Font("Segoe UI", 10f, FontStyle.Bold) };
            btnSend.Click += (s, e) => StartSend();
            btnReceive = new Button { Text = "AL", Left = x1 + 128, Top = y, Width = 70, Height = 36, Font = new Font("Segoe UI", 10f, FontStyle.Bold) };
            btnReceive.Click += (s, e) => StartReceive();
            btnCancel = new Button { Text = "IPTAL", Left = x1 + 206, Top = y, Width = 80, Height = 36, Enabled = false };
            btnCancel.Click += (s, e) => CancelOp();
            btnPrint = new Button { Text = "PLU Yazdir (80mm)", Left = x1 + 300, Top = y, Width = 170, Height = 36 };
            btnPrint.Click += (s, e) => PrintPluList();
            Add(btnSend); Add(btnReceive); Add(btnCancel); Add(btnPrint);

            // --- Yazdirma bos satir ---
            y += 46;
            Add(new Label { Text = "Yazdirmada sona bos satir:", Left = x1, Top = y + 4, Width = 165 });
            txtBlankRows = new TextBox { Left = x1 + 168, Top = y, Width = 55, Text = cfg.BlankRows.ToString() };
            Add(txtBlankRows);
            Add(new Label { Text = "(son PLU'dan devam, ad bos)", Left = x1 + 230, Top = y + 4, Width = 240, ForeColor = Color.Gray });

            // --- Durum log ---
            y += 34;
            Add(new Label { Text = "Durum:", Left = x1, Top = y, Width = 110 });
            y += 20;
            log = new ListBox { Left = x1, Top = y, Width = ClientSize.Width - 24, Height = ClientSize.Height - y - 12, Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
            Add(log);

            sendTimer = new System.Windows.Forms.Timer { Interval = SEND_TIMEOUT_MS };
            sendTimer.Tick += (s, e) => OnSendTimeout();

            FormClosing += (s, e) => { StopSendTimer(); SaveCfgFromUi(); TryDisconnect(sendIp); };
        }

        private Button ScaleBtn(string t, int x, int y, int w)
        {
            var b = new Button { Text = t, Left = x, Top = y, Width = w, Height = 28 };
            Add(b);
            return b;
        }

        // Novempos ikonu exe'ye gomulu; baslik cubugu ve Alt+Tab icin yukler.
        private void LoadWindowIcon()
        {
            try
            {
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                using (var st = asm.GetManifestResourceStream("novempos.ico"))
                    if (st != null) Icon = new Icon(st);
            }
            catch { }
        }

        private void InitOcx()
        {
            try
            {
                ax = new AxCASSCALELib.AxCasScale();
                ((System.ComponentModel.ISupportInitialize)ax).BeginInit();
                ax.Location = new Point(0, 0);
                ax.Size = new Size(2, 2);
                ax.Visible = false;
                Controls.Add(ax);
                ((System.ComponentModel.ISupportInitialize)ax).EndInit();

                ax.StateEvent += Ax_StateEvent;
                ax.RecvEventString += Ax_RecvEventString;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "CAS OCX yuklenemedi. 'CasScale.ocx' kayitli degil gibi gorunuyor.\r\n\r\n" +
                    "Cozum: uygulama klasorundeki 'register.bat' dosyasini yonetici olarak bir kez calistirin.\r\n\r\n" +
                    "Detay: " + ex.Message,
                    "OCX Hatasi", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ---- Terazi listesi yonetimi ----

        // Isaretli terazileri dondurur (GONDER coklu, AL tek).
        private List<ScaleConfig> CheckedScales()
        {
            var list = new List<ScaleConfig>();
            foreach (var o in lstScales.CheckedItems) { var sc = o as ScaleConfig; if (sc != null) list.Add(sc); }
            return list;
        }

        // cfg.Scales'ten listeyi yeniden kurar; isaretleri IP:Port'a gore korur.
        private void RefreshScaleList()
        {
            var wasChecked = new HashSet<string>();
            foreach (var sc in CheckedScales()) wasChecked.Add(Key(sc));

            lstScales.Items.Clear();
            foreach (var sc in cfg.Scales)
            {
                int i = lstScales.Items.Add(sc);
                if (wasChecked.Contains(Key(sc))) lstScales.SetItemChecked(i, true);
            }
        }

        private static string Key(ScaleConfig sc) { return sc.Ip + ":" + sc.Port; }

        private void AddScale()
        {
            if (cfg.Scales.Count >= AppSettings.MaxScales)
            { Warn("En fazla " + AppSettings.MaxScales + " terazi eklenebilir."); return; }
            using (var f = new ScaleEditForm(null, "Terazi Ekle"))
            {
                if (f.ShowDialog(this) != DialogResult.OK || f.Result == null) return;
                cfg.Scales.Add(f.Result);
                cfg.Save();
                RefreshScaleList();
                // Yeni ekleneni otomatik isaretle.
                int last = lstScales.Items.Count - 1;
                if (last >= 0) lstScales.SetItemChecked(last, true);
            }
        }

        private void EditScale()
        {
            int i = lstScales.SelectedIndex;
            if (i < 0) { Warn("Duzenlemek icin listeden bir terazi secin."); return; }
            using (var f = new ScaleEditForm(cfg.Scales[i].Clone(), "Terazi Duzenle"))
            {
                if (f.ShowDialog(this) != DialogResult.OK || f.Result == null) return;
                cfg.Scales[i] = f.Result;
                cfg.Save();
                RefreshScaleList();
            }
        }

        private void DeleteScale()
        {
            int i = lstScales.SelectedIndex;
            if (i < 0) { Warn("Silmek icin listeden bir terazi secin."); return; }
            var sc = cfg.Scales[i];
            if (MessageBox.Show(this, "'" + sc.Name + "' silinsin mi?", "Terazi Sil",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            cfg.Scales.RemoveAt(i);
            cfg.Save();
            RefreshScaleList();
        }

        private void TestScale()
        {
            int i = lstScales.SelectedIndex;
            if (i < 0) { Warn("Test icin listeden bir terazi secin."); return; }
            var sc = cfg.Scales[i];
            Info("Baglanti testi: " + sc.Name + " (" + sc.Ip + ":" + sc.Port + ")...");
            string msg;
            bool ok = CasNetReader.TestConnection(sc.Ip, sc.Port, 4000, out msg);
            Info((ok ? "  OK: " : "  BASARISIZ: ") + msg);
            MessageBox.Show(this, msg, "Baglanti Testi", MessageBoxButtons.OK,
                ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }

        private void Browse()
        {
            using (var d = new OpenFileDialog { Filter = "Excel (*.xlsx)|*.xlsx|Tum dosyalar (*.*)|*.*" })
            {
                if (!string.IsNullOrEmpty(txtExcel.Text)) d.FileName = txtExcel.Text;
                if (d.ShowDialog(this) == DialogResult.OK) txtExcel.Text = d.FileName;
            }
        }

        // Secili Excel'den sadece "PLU No" + "Name" alarak 80mm yaziciya liste basar.
        private void PrintPluList()
        {
            if (string.IsNullOrWhiteSpace(txtExcel.Text) || !System.IO.File.Exists(txtExcel.Text))
            { Info("Once gecerli bir Excel dosyasi secin."); return; }

            ExcelReader.Sheet sheet;
            try { sheet = ExcelReader.Read(txtExcel.Text); }
            catch (Exception ex) { Info("Excel okunamadi: " + ex.Message); return; }

            var items = new List<KeyValuePair<string, string>>();
            foreach (var row in sheet.Rows)
            {
                string plu, name;
                row.TryGetValue("PLU No", out plu);
                row.TryGetValue("Name", out name);
                items.Add(new KeyValuePair<string, string>(plu ?? "", name ?? ""));
            }
            if (items.Count == 0) { Info("Yazdirilacak PLU yok."); return; }

            int realCount = items.Count;

            int blankCount = 0;
            int.TryParse(txtBlankRows.Text, out blankCount);
            if (blankCount < 0) blankCount = 0;
            if (blankCount > 500) blankCount = 500;
            cfg.BlankRows = blankCount;

            if (blankCount > 0)
            {
                int lastNum = 0;
                for (int i = realCount - 1; i >= 0; i--)
                {
                    int v = DigitsToInt(items[i].Key);
                    if (v > 0) { lastNum = v; break; }
                }
                for (int k = 1; k <= blankCount; k++)
                    items.Add(new KeyValuePair<string, string>((lastNum + k).ToString(), ""));
            }

            var printer = new PluListPrinter(items, "PLU LISTESI", DateTime.Now, realCount);
            using (var doc = printer.BuildDocument())
            {
                if (!string.IsNullOrEmpty(cfg.PrinterName))
                { try { doc.PrinterSettings.PrinterName = cfg.PrinterName; } catch { } }

                using (var pd = new PrintDialog { Document = doc, UseEXDialog = true })
                {
                    if (pd.ShowDialog(this) != DialogResult.OK) return;
                    cfg.PrinterName = doc.PrinterSettings.PrinterName;
                    cfg.Save();
                    try
                    {
                        doc.Print();
                        Info("Yazdirildi: " + realCount + " PLU" +
                             (blankCount > 0 ? " + " + blankCount + " bos satir" : "") +
                             " -> " + doc.PrinterSettings.PrinterName);
                    }
                    catch (Exception ex) { Info("Yazdirma hatasi: " + ex.Message); }
                }
            }
        }

        private static int DigitsToInt(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            var sb = new StringBuilder();
            foreach (char c in s) if (c >= '0' && c <= '9') sb.Append(c);
            int v;
            return (sb.Length > 0 && int.TryParse(sb.ToString(), out v)) ? v : 0;
        }

        // ---- GONDER (coklu terazi) ----

        private void StartSend()
        {
            if (sending || receiving) return;
            if (ax == null) { Info("OCX yuklu degil - once register.bat calistirin."); return; }
            SaveCfgFromUi();

            var scales = CheckedScales();
            if (scales.Count == 0) { Info("GONDER icin en az bir terazi isaretleyin."); return; }

            if (string.IsNullOrWhiteSpace(txtExcel.Text) || !System.IO.File.Exists(txtExcel.Text))
            { Info("Once gecerli bir Excel dosyasi secin."); return; }

            Encoding enc;
            try { enc = Encoding.GetEncoding(cfg.EncodingName); } catch { enc = Encoding.ASCII; }

            ExcelReader.Sheet sheet;
            try { sheet = ExcelReader.Read(txtExcel.Text); }
            catch (Exception ex) { Info("Excel okunamadi: " + ex.Message); return; }
            if (sheet.Rows.Count == 0) { Info("Excel'de veri satiri yok."); return; }

            records.Clear(); names.Clear();
            var overflowErrors = new List<string>();
            for (int i = 0; i < sheet.Rows.Count; i++)
            {
                var row = sheet.Rows[i];
                string n; row.TryGetValue("Name", out n);
                try
                {
                    records.Add(PluBuilder.BuildV06(row, enc));
                    names.Add(n ?? "");
                }
                catch (PluFieldOverflowException ex)
                {
                    string pluNo; row.TryGetValue("PLU No", out pluNo);
                    overflowErrors.Add(string.Format("Kayit {0} (PLU No={1}, Ad={2}): {3}",
                        i + 1,
                        string.IsNullOrEmpty(pluNo) ? "?" : pluNo,
                        string.IsNullOrEmpty(n) ? "?" : n,
                        ex.Message));
                }
            }

            if (overflowErrors.Count > 0)
            {
                log.Items.Clear();
                Info("GONDERIM DURDURULDU: " + overflowErrors.Count + " kayitta alan tasmasi bulundu. Once Excel'i duzeltin.");
                foreach (var e in overflowErrors) Info("  " + e);
                int shown = Math.Min(10, overflowErrors.Count);
                Warn("Excel'de " + overflowErrors.Count + " kayitta alan tasmasi var, hicbir PLU gonderilmedi:\r\n\r\n" +
                     string.Join("\r\n", overflowErrors.GetRange(0, shown)) +
                     (overflowErrors.Count > shown ? "\r\n... (tumu icin durum listesine bakin)" : ""));
                return;
            }

            log.Items.Clear();
            Info("Excel okundu. Satir: " + sheet.Rows.Count + "  |  Hedef terazi: " + scales.Count);

            sendQueue = scales;
            sendScaleIdx = 0;
            totalOk = 0; totalFail = 0;
            sending = true;
            SetBusy(true);
            SendToScale(0);
        }

        // Sirasidaki teraziye baglanip tum PLU'lari gonderir.
        private void SendToScale(int idx)
        {
            var sc = sendQueue[idx];
            sendIp = sc.Ip; sendPort = sc.Port; sendModel = sc.Model; sendDataType = sc.DataType;
            index = 0; okCount = 0; failCount = 0;

            Info(string.Format("=== [{0}/{1}] {2} ({3}:{4}) ===", idx + 1, sendQueue.Count, sc.Name, sc.Ip, sc.Port));
            int rtnC = ax.ConnectionEx3(sendIp, sendPort, -1, sendModel, cfg.Version, 97);
            Info("  Baglaniliyor... ret=" + rtnC);
            if (rtnC <= 0)
            {
                Info("  Baglanti kurulamadi, bu terazi atlandi.");
                totalFail += records.Count;
                AdvanceScale();
                return;
            }
            SendNext();
        }

        // Siradaki PLU'yu gonderir; terazi cevabini Ax_RecvEventString bekler.
        private void SendNext()
        {
            while (index < records.Count)
            {
                int i = index;
                int rtn = ax.SendDataString(sendIp, -1, sendModel, cfg.Version, ACTION_DOWNLOAD, sendDataType, records[i]);
                if (rtn > 0)
                {
                    RestartSendTimer();
                    Info(string.Format("  #{0} '{1}' gonderildi, yanit bekleniyor...", i + 1, names[i]));
                    return;
                }
                failCount++;
                Info(string.Format("  #{0} '{1}' KUYRUGA ALINAMADI (ret={2})", i + 1, names[i], rtn));
                index++;
            }
            ScaleDone();
        }

        // Aktif terazi bitti -> toplamlara ekle, baglantiyi kapat, sonrakine gec.
        private void ScaleDone()
        {
            StopSendTimer();
            Info(string.Format("  Bitti. Basarili: {0}, Basarisiz: {1}", okCount, failCount));
            totalOk += okCount; totalFail += failCount;
            TryDisconnect(sendIp);
            AdvanceScale();
        }

        private void AdvanceScale()
        {
            if (!sending) { SetBusy(false); return; } // iptal edilmis
            sendScaleIdx++;
            if (sendScaleIdx < sendQueue.Count) { SendToScale(sendScaleIdx); return; }
            sending = false;
            SetBusy(false);
            Info(string.Format("=== TUMU TAMAM. {0} terazi | Toplam basarili: {1}, basarisiz: {2} ===",
                sendQueue.Count, totalOk, totalFail));
        }

        // ---- OCX olaylari ----

        private void Ax_StateEvent(object sender, AxCASSCALELib._DCasScaleEvents_StateEventEvent e)
        {
            Info("Durum: state=" + e.iState + "  ip=" + e.sIP);
        }

        private void Ax_RecvEventString(object sender, AxCASSCALELib._DCasScaleEvents_RecvEventStringEvent e)
        {
            // Okuma artik OCX olay'i degil, dogrudan TCP (CasNetReader) ile yapilir.
            if (!sending || e.iTransType != ACTION_DOWNLOAD) return;

            if (e.iResult == RECV_SUCCESS) { StopSendTimer(); okCount++; Info(string.Format("  -> #{0} '{1}' OK", index + 1, PluName(index))); }
            else if (e.iResult == RECV_FAIL) { StopSendTimer(); failCount++; Info(string.Format("  -> #{0} '{1}' BASARISIZ", index + 1, PluName(index))); }
            else return;

            index++;
            SendNext();
        }

        private string PluName(int i) { return (i >= 0 && i < names.Count) ? names[i] : ""; }

        private void SetBusy(bool busy)
        {
            btnSend.Enabled = !busy;
            btnReceive.Enabled = !busy;
            btnPrint.Enabled = !busy;
            btnAddScale.Enabled = !busy;
            btnEditScale.Enabled = !busy;
            btnDelScale.Enabled = !busy;
            btnTestScale.Enabled = !busy;
            btnCancel.Enabled = busy;
        }

        // Calisan GONDER/AL islemini yarida keser.
        private void CancelOp()
        {
            if (!sending && !receiving) return;
            if (receiving)
            {
                receiving = false; // worker gorup durur; FinishReceive kaydeder
                Info("Iptal ediliyor...");
                return;
            }
            StopSendTimer();
            sending = false;
            TryDisconnect(sendIp);
            SetBusy(false);
            Info("Gonderim iptal edildi.");
        }

        // ---- zaman asimi ----

        private void RestartSendTimer() { sendTimer.Stop(); sendTimer.Start(); }
        private void StopSendTimer() { if (sendTimer != null) sendTimer.Stop(); }

        // 10 sn icinde terazi yaniti gelmezse: bu terazide durdur, sonrakine gec.
        private void OnSendTimeout()
        {
            StopSendTimer();
            if (!sending) return;
            failCount++;
            Info(string.Format("  -> #{0} '{1}' ZAMAN ASIMI ({2} sn yanit yok)",
                index + 1, PluName(index), SEND_TIMEOUT_MS / 1000));
            Info("  Bu terazide gonderim durduruldu, sonraki teraziye geciliyor.");
            totalOk += okCount; totalFail += failCount;
            TryDisconnect(sendIp);
            AdvanceScale();
        }

        // ---- teraziden okuma (AL) — TEK terazi ----

        private void StartReceive()
        {
            if (sending || receiving) return;
            SaveCfgFromUi();

            var checkedScales = CheckedScales();
            if (checkedScales.Count == 0) { Info("AL icin bir terazi isaretleyin."); return; }
            if (checkedScales.Count > 1)
            {
                Info("AL icin yalnizca TEK terazi isaretleyin (su an " + checkedScales.Count + " secili).");
                Warn("Teraziden okuma tek terazi ile yapilir.\r\nLutfen listeden yalnizca BIR terazi isaretleyin.");
                return;
            }
            var sc = checkedScales[0];

            int start, end, dept;
            if (!ShowRangeDialog(out start, out end, out dept)) return;
            if (start < 1) start = 1;
            if (end < start) { Info("Bitis PLU, baslangictan kucuk olamaz."); return; }

            string ip = sc.Ip; int port = sc.Port;
            received.Clear();
            readDept = dept;

            log.Items.Clear();
            Info(string.Format("Teraziden okunuyor: {0} ({1}:{2}) — PLU {3}-{4}, departman {5}", sc.Name, ip, port, start, end, readDept));

            receiving = true;
            SetBusy(true);

            // Okuma, CL-Works'un ASCII protokolu ile DOGRUDAN TCP uzerinden (OCX yok).
            Action<string> logCb = s => { try { BeginInvoke((Action)(() => Info(s))); } catch { } };
            readThread = new System.Threading.Thread(() =>
            {
                List<Dictionary<string, string>> got;
                try { got = CasNetReader.Read(ip, port, start, end, readDept, 8000, logCb, () => !receiving); }
                catch (Exception ex) { logCb("Okuma hatasi: " + ex.Message); got = new List<Dictionary<string, string>>(); }
                try { BeginInvoke((Action)(() => FinishReceive(got))); } catch { }
            });
            readThread.IsBackground = true;
            readThread.Start();
        }

        private void FinishReceive(List<Dictionary<string, string>> got)
        {
            receiving = false;
            received = got ?? new List<Dictionary<string, string>>();
            SetBusy(false);
            Info(string.Format("Okuma bitti. Bulunan PLU: {0}", received.Count));
            if (received.Count == 0) { Info("Kaydedilecek PLU yok."); return; }
            SavePluXlsx();
        }

        private void SavePluXlsx()
        {
            using (var d = new SaveFileDialog { Filter = "Excel (*.xlsx)|*.xlsx", FileName = "terazi_plu.xlsx" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    XlsxWriter.Write(d.FileName, PluReader.Columns, received);
                    Info("Kaydedildi: " + d.FileName + " (" + received.Count + " PLU)");
                }
                catch (Exception ex) { Info("Kaydetme hatasi: " + ex.Message); }
            }
        }

        // Okunacak PLU araligini ve departmani soran kucuk pencere.
        // Departman: CLI'nin --dept secenegiyle ayni anlamda (varsayilan 1);
        // terazide PLU'lar departman 1 disinda tutuluyorsa burada degistirilir.
        // Protokolde 2 HEX haneyle tasindigi icin (CasNetReader.Read) 1-255 ile sinirlanir.
        private bool ShowRangeDialog(out int start, out int end, out int dept)
        {
            start = 1; end = 100; dept = 1;
            using (var f = new Form())
            {
                f.Text = "Teraziden PLU Al";
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.StartPosition = FormStartPosition.CenterParent;
                f.MinimizeBox = false; f.MaximizeBox = false;
                f.ClientSize = new Size(280, 168);

                var l1 = new Label { Text = "Baslangic PLU:", Left = 12, Top = 18, Width = 110 };
                var t1 = new TextBox { Left = 128, Top = 15, Width = 120, Text = "1" };
                var l2 = new Label { Text = "Bitis PLU:", Left = 12, Top = 52, Width = 110 };
                var t2 = new TextBox { Left = 128, Top = 49, Width = 120, Text = "100" };
                var l3 = new Label { Text = "Departman No:", Left = 12, Top = 86, Width = 110 };
                var n3 = new NumericUpDown { Left = 128, Top = 83, Width = 120, Minimum = 1, Maximum = 255, Value = 1 };
                var ok = new Button { Text = "Al", Left = 128, Top = 125, Width = 58, DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "Iptal", Left = 190, Top = 125, Width = 58, DialogResult = DialogResult.Cancel };
                f.Controls.AddRange(new Control[] { l1, t1, l2, t2, l3, n3, ok, cancel });
                f.AcceptButton = ok; f.CancelButton = cancel;

                if (f.ShowDialog(this) != DialogResult.OK) return false;
                int.TryParse(t1.Text.Trim(), out start);
                int.TryParse(t2.Text.Trim(), out end);
                dept = (int)n3.Value;
                return true;
            }
        }

        // ---- yardimcilar ----

        private void TryDisconnect(string ip)
        {
            if (string.IsNullOrWhiteSpace(ip)) return;
            try { if (ax != null) ax.DisconnectOneEx(ip, -1); } catch { }
        }

        private void SaveCfgFromUi()
        {
            cfg.LastExcel = txtExcel.Text.Trim();
            int br; if (txtBlankRows != null && int.TryParse(txtBlankRows.Text, out br)) cfg.BlankRows = br;
            cfg.Save(); // Scales zaten ekle/duzenle/sil'de kaydediliyor
        }

        private void Warn(string m)
        {
            MessageBox.Show(this, m, "Novempos Terazi", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void Info(string s)
        {
            log.Items.Add(s);
            log.TopIndex = log.Items.Count - 1;
        }

        private void Add(Control c) { Controls.Add(c); }
        private static AnchorStyles LR() { return AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; }
        private static AnchorStyles TR() { return AnchorStyles.Top | AnchorStyles.Right; }
    }
}
