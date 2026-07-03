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

        private TextBox txtExcel, txtIp, txtPort, txtModel, txtDataType, txtBlankRows;
        private Button btnBrowse, btnSend, btnReceive, btnCancel, btnPrint;
        private ListBox log;

        // Teraziden okuma (AL) durumu
        private bool receiving;
        private int readPlu, readEnd, readDept, readConsecTimeout;
        private List<Dictionary<string, string>> received = new List<Dictionary<string, string>>();

        // Teker teker gonderim durumu
        private List<string> records = new List<string>();
        private List<string> names = new List<string>();
        private int index;
        private int okCount, failCount;
        private string sendIp;
        private int sendModel, sendDataType;
        private bool sending;
        private System.Windows.Forms.Timer sendTimer;
        private const int SEND_TIMEOUT_MS = 10000; // terazi yaniti icin bekleme suresi (10 sn)

        public MainForm()
        {
            cfg = AppSettings.Load();
            BuildUi();
            InitOcx();
        }

        private void BuildUi()
        {
            Text = "Novempos - Terazi PLU Gonderici";
            LoadWindowIcon();
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(600, 500);
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(540, 440);

            int x1 = 12, x2 = 130, w = 300, y = 15, h = 26, gap = 36;

            Add(new Label { Text = "Excel dosyasi:", Left = x1, Top = y + 4, Width = 110 });
            txtExcel = new TextBox { Left = x2, Top = y, Width = w - 90, Text = cfg.LastExcel, Anchor = LR() };
            btnBrowse = new Button { Text = "Sec...", Left = x2 + w - 84, Top = y - 1, Width = 74, Height = h, Anchor = TR() };
            btnBrowse.Click += (s, e) => Browse();
            Add(txtExcel); Add(btnBrowse);

            y += gap;
            Add(new Label { Text = "Terazi IP:", Left = x1, Top = y + 4, Width = 110 });
            txtIp = new TextBox { Left = x2, Top = y, Width = 160, Text = cfg.Ip };
            Add(txtIp);

            y += gap;
            Add(new Label { Text = "Port:", Left = x1, Top = y + 4, Width = 110 });
            txtPort = new TextBox { Left = x2, Top = y, Width = 160, Text = cfg.Port.ToString() };
            Add(txtPort);

            y += gap;
            Add(new Label { Text = "Model:", Left = x1, Top = y + 4, Width = 110 });
            txtModel = new TextBox { Left = x2, Top = y, Width = 80, Text = cfg.Model.ToString() };
            Add(new Label { Text = "(5000 = CL5000/CL3000)", Left = x2 + 88, Top = y + 4, Width = 200, ForeColor = Color.Gray });
            Add(txtModel);

            y += gap;
            Add(new Label { Text = "PLU veri tipi:", Left = x1, Top = y + 4, Width = 110 });
            txtDataType = new TextBox { Left = x2, Top = y, Width = 80, Text = cfg.PluDataType.ToString() };
            Add(new Label { Text = "(98=V06, 97=V05, 9=V02)", Left = x2 + 88, Top = y + 4, Width = 200, ForeColor = Color.Gray });
            Add(txtDataType);

            y += gap + 4;
            btnSend = new Button { Text = "GONDER", Left = x2, Top = y, Width = 100, Height = 34, Font = new Font("Segoe UI", 10f, FontStyle.Bold) };
            btnSend.Click += (s, e) => StartSend();
            Add(btnSend);

            btnReceive = new Button { Text = "AL", Left = x2 + 106, Top = y, Width = 54, Height = 34, Font = new Font("Segoe UI", 10f, FontStyle.Bold) };
            btnReceive.Click += (s, e) => StartReceive();
            Add(btnReceive);

            btnCancel = new Button { Text = "IPTAL", Left = x2 + 166, Top = y, Width = 74, Height = 34, Enabled = false };
            btnCancel.Click += (s, e) => CancelOp();
            Add(btnCancel);

            btnPrint = new Button { Text = "PLU Yazdir (80mm)", Left = x2 + 250, Top = y, Width = 180, Height = 34 };
            btnPrint.Click += (s, e) => PrintPluList();
            Add(btnPrint);

            y += 46;
            Add(new Label { Text = "Yazdirmada sona bos satir:", Left = x1, Top = y + 4, Width = 165 });
            txtBlankRows = new TextBox { Left = x1 + 168, Top = y, Width = 55, Text = cfg.BlankRows.ToString() };
            Add(txtBlankRows);
            Add(new Label { Text = "(son PLU'dan devam, ad bos)", Left = x1 + 230, Top = y + 4, Width = 240, ForeColor = Color.Gray });

            y += 36;
            Add(new Label { Text = "Durum:", Left = x1, Top = y, Width = 110 });
            y += 20;
            log = new ListBox { Left = x1, Top = y, Width = ClientSize.Width - 24, Height = ClientSize.Height - y - 12, Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
            Add(log);

            sendTimer = new System.Windows.Forms.Timer { Interval = SEND_TIMEOUT_MS };
            sendTimer.Tick += (s, e) => OnSendTimeout();

            FormClosing += (s, e) => { StopSendTimer(); SaveCfgFromUi(); TryDisconnect(); };
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

        private void Browse()
        {
            using (var d = new OpenFileDialog { Filter = "Excel (*.xlsx)|*.xlsx|Tum dosyalar (*.*)|*.*" })
            {
                if (!string.IsNullOrEmpty(txtExcel.Text)) d.FileName = txtExcel.Text;
                if (d.ShowDialog(this) == DialogResult.OK) txtExcel.Text = d.FileName;
            }
        }

        // Secili Excel'den sadece "PLU No" + "Name" alarak 80mm yaziciya liste basar.
        // Teraziyle/OCX ile ilgisi yok; ayri calisir.
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

            // Sona eklenecek bos satirlar: son PLU'dan devam eden numara, urun adi bos.
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

        // Metindeki rakamlari alip tam sayiya cevirir (yoksa 0).
        private static int DigitsToInt(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            var sb = new StringBuilder();
            foreach (char c in s) if (c >= '0' && c <= '9') sb.Append(c);
            int v;
            return (sb.Length > 0 && int.TryParse(sb.ToString(), out v)) ? v : 0;
        }

        private void StartSend()
        {
            if (sending) return;
            if (ax == null) { Info("OCX yuklu degil - once register.bat calistirin."); return; }
            SaveCfgFromUi();

            if (string.IsNullOrWhiteSpace(txtExcel.Text) || !System.IO.File.Exists(txtExcel.Text))
            { Info("Once gecerli bir Excel dosyasi secin."); return; }
            if (string.IsNullOrWhiteSpace(txtIp.Text)) { Info("Terazi IP giriniz."); return; }

            int port; if (!int.TryParse(txtPort.Text, out port)) { Info("Port sayisal olmali."); return; }
            int model; if (!int.TryParse(txtModel.Text, out model)) { Info("Model sayisal olmali."); return; }
            int dataType; if (!int.TryParse(txtDataType.Text, out dataType)) { Info("PLU veri tipi sayisal olmali."); return; }

            Encoding enc;
            try { enc = Encoding.GetEncoding(cfg.EncodingName); } catch { enc = Encoding.ASCII; }

            ExcelReader.Sheet sheet;
            try { sheet = ExcelReader.Read(txtExcel.Text); }
            catch (Exception ex) { Info("Excel okunamadi: " + ex.Message); return; }

            if (sheet.Rows.Count == 0) { Info("Excel'de veri satiri yok."); return; }

            // Kayitlari onceden hazirla
            records.Clear(); names.Clear();
            foreach (var row in sheet.Rows)
            {
                records.Add(PluBuilder.BuildV06(row, enc));
                string n; row.TryGetValue("Name", out n);
                names.Add(n ?? "");
            }

            log.Items.Clear();
            Info("Excel okundu. Satir: " + sheet.Rows.Count);
            Info("Basliklar: " + string.Join(", ", sheet.Headers));

            sendIp = txtIp.Text.Trim();
            sendModel = model;
            sendDataType = dataType;
            index = 0; okCount = 0; failCount = 0;
            sending = true;
            SetBusy(true);

            int rtnC = ax.ConnectionEx3(sendIp, port, -1, sendModel, cfg.Version, 97);
            Info("Baglaniliyor... (ip=" + sendIp + " port=" + port + ", ret=" + rtnC + ")");
            if (rtnC <= 0)
            {
                Info("Baglanti baslatilamadi. IP/port ve ag baglantisini kontrol edin.");
                sending = false; SetBusy(false);
                return;
            }

            // Ilk PLU'yu gonder; sonrakiler her OK/FAIL cevabinda tek tek gider.
            SendNext();
        }

        // Siradaki PLU'yu gonderir. Basarili kuyruklamada terazi cevabini bekler
        // (Ax_RecvEventString bir sonrakini tetikler). Kuyruklama hemen basarisiz
        // olursa (ret<=0) o kaydi atlayip bir sonrakine gecer.
        private void SendNext()
        {
            while (index < records.Count)
            {
                int i = index;
                int rtn = ax.SendDataString(sendIp, -1, sendModel, cfg.Version, ACTION_DOWNLOAD, sendDataType, records[i]);
                if (rtn > 0)
                {
                    RestartSendTimer(); // 10 sn icinde yanit gelmezse zaman asimi
                    Info(string.Format("  #{0} '{1}' gonderildi, terazi yaniti bekleniyor...", i + 1, names[i]));
                    return; // cevabi Ax_RecvEventString'de bekle
                }
                // Kuyruga alinamadi -> bu kaydi basarisiz say, hemen sonrakine gec
                failCount++;
                Info(string.Format("  #{0} '{1}' KUYRUGA ALINAMADI (ret={2})", i + 1, names[i], rtn));
                index++;
            }
            Finish();
        }

        // ---- OCX olaylari ----

        private void Ax_StateEvent(object sender, AxCASSCALELib._DCasScaleEvents_StateEventEvent e)
        {
            Info("Durum: state=" + e.iState + "  ip=" + e.sIP);
        }

        private void Ax_RecvEventString(object sender, AxCASSCALELib._DCasScaleEvents_RecvEventStringEvent e)
        {
            if (receiving) { HandleReadRecv(e); return; }
            if (!sending || e.iTransType != ACTION_DOWNLOAD) return;

            if (e.iResult == RECV_SUCCESS) { StopSendTimer(); okCount++; Info(string.Format("  -> #{0} '{1}' OK", index + 1, PluName(index))); }
            else if (e.iResult == RECV_FAIL) { StopSendTimer(); failCount++; Info(string.Format("  -> #{0} '{1}' BASARISIZ", index + 1, PluName(index))); }
            else return;

            index++;
            SendNext();
        }

        private string PluName(int i) { return (i >= 0 && i < names.Count) ? names[i] : ""; }

        private void Finish()
        {
            StopSendTimer();
            sending = false;
            Info(string.Format("TAMAMLANDI. Basarili: {0}, Basarisiz: {1}", okCount, failCount));
            TryDisconnect();
            SetBusy(false);
        }

        private void SetBusy(bool busy)
        {
            btnSend.Enabled = !busy;
            btnReceive.Enabled = !busy;
            btnPrint.Enabled = !busy;
            btnCancel.Enabled = busy;
        }

        // Calisan GONDER/AL islemini yarida keser.
        private void CancelOp()
        {
            if (!sending && !receiving) return;
            StopSendTimer();
            bool wasReceiving = receiving;
            sending = false;
            receiving = false;
            TryDisconnect();
            SetBusy(false);
            Info("Islem iptal edildi.");
            if (wasReceiving && received.Count > 0)
            {
                Info("O ana kadar okunan " + received.Count + " PLU kaydedilebilir.");
                SavePluXlsx();
            }
        }

        // ---- zaman asimi ----

        private void RestartSendTimer() { sendTimer.Stop(); sendTimer.Start(); }
        private void StopSendTimer() { if (sendTimer != null) sendTimer.Stop(); }

        // 10 sn icinde terazi yaniti gelmezse: islemi durdur, butonlari geri ac.
        private void OnSendTimeout()
        {
            StopSendTimer();
            if (receiving) { OnReadTimeout(); return; }
            if (!sending) return;
            failCount++;
            Info(string.Format("  -> #{0} '{1}' ZAMAN ASIMI ({2} sn yanit yok)",
                index + 1, PluName(index), SEND_TIMEOUT_MS / 1000));
            Info(string.Format("Gonderim durduruldu. (Basarili: {0}, Basarisiz: {1})", okCount, failCount));
            Info("Terazi baglantisini / IP-Port'u kontrol edip tekrar deneyin.");
            sending = false;
            TryDisconnect();
            SetBusy(false);
        }

        // ---- teraziden okuma (AL) ----

        private void StartReceive()
        {
            if (sending || receiving) return;
            if (ax == null) { Info("OCX yuklu degil - once register.bat calistirin."); return; }
            SaveCfgFromUi();

            if (string.IsNullOrWhiteSpace(txtIp.Text)) { Info("Terazi IP giriniz."); return; }
            int port; if (!int.TryParse(txtPort.Text, out port)) { Info("Port sayisal olmali."); return; }
            int model; if (!int.TryParse(txtModel.Text, out model)) { Info("Model sayisal olmali."); return; }

            int start, end;
            if (!ShowRangeDialog(out start, out end)) return;
            if (start < 1) start = 1;
            if (end < start) { Info("Bitis PLU, baslangictan kucuk olamaz."); return; }

            string ip = txtIp.Text.Trim();
            received.Clear();
            readDept = 1;
            readPlu = start; readEnd = end; readConsecTimeout = 0;

            log.Items.Clear();
            Info(string.Format("Teraziden okunuyor: PLU {0}-{1} (departman {2})", start, end, readDept));

            int rtnC = ax.ConnectionEx3(ip, port, -1, model, cfg.Version, 97);
            Info("Baglaniliyor... (ip=" + ip + " port=" + port + ", ret=" + rtnC + ")");
            if (rtnC <= 0)
            {
                Info("Baglanti baslatilamadi. IP/port ve ag baglantisini kontrol edin.");
                return;
            }

            receiving = true;
            SetBusy(true);
            ReadNext();
        }

        // Siradaki PLU'yu terazidenister; cevabi RecvEventString'de gelir.
        private void ReadNext()
        {
            while (readPlu <= readEnd)
            {
                int rtn = ax.ReadPLU(readDept, readPlu);
                if (rtn > 0)
                {
                    RestartSendTimer(); // 10 sn icinde yanit yoksa zaman asimi
                    return;
                }
                readPlu++; // istek gonderilemedi -> bu PLU'yu atla
            }
            FinishReceive();
        }

        private void HandleReadRecv(AxCASSCALELib._DCasScaleEvents_RecvEventStringEvent e)
        {
            StopSendTimer();
            readConsecTimeout = 0;
            if (e.iResult == RECV_SUCCESS)
            {
                var d = PluReader.Parse(e.sData);
                if (d != null)
                {
                    received.Add(d);
                    string nm; d.TryGetValue("Name", out nm);
                    Info(string.Format("  <- PLU {0} alindi: {1}", readPlu, nm ?? ""));
                }
            }
            // basarisiz/bos -> o numarada PLU yok, sessizce atla
            readPlu++;
            ReadNext();
        }

        private void OnReadTimeout()
        {
            if (!receiving) return;
            readConsecTimeout++;
            Info(string.Format("  PLU {0}: yanit yok (zaman asimi)", readPlu));
            if (readConsecTimeout >= 3)
            {
                Info("Ust uste yanit alinamadi, okuma durduruldu.");
                FinishReceive();
                return;
            }
            readPlu++;
            ReadNext();
        }

        private void FinishReceive()
        {
            StopSendTimer();
            receiving = false;
            TryDisconnect();
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

        // Okunacak PLU araligini soran kucuk pencere.
        private bool ShowRangeDialog(out int start, out int end)
        {
            start = 1; end = 100;
            using (var f = new Form())
            {
                f.Text = "Teraziden PLU Al";
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.StartPosition = FormStartPosition.CenterParent;
                f.MinimizeBox = false; f.MaximizeBox = false;
                f.ClientSize = new Size(280, 135);

                var l1 = new Label { Text = "Baslangic PLU:", Left = 12, Top = 18, Width = 110 };
                var t1 = new TextBox { Left = 128, Top = 15, Width = 120, Text = "1" };
                var l2 = new Label { Text = "Bitis PLU:", Left = 12, Top = 52, Width = 110 };
                var t2 = new TextBox { Left = 128, Top = 49, Width = 120, Text = "100" };
                var ok = new Button { Text = "Al", Left = 128, Top = 92, Width = 58, DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "Iptal", Left = 190, Top = 92, Width = 58, DialogResult = DialogResult.Cancel };
                f.Controls.AddRange(new Control[] { l1, t1, l2, t2, ok, cancel });
                f.AcceptButton = ok; f.CancelButton = cancel;

                if (f.ShowDialog(this) != DialogResult.OK) return false;
                int.TryParse(t1.Text.Trim(), out start);
                int.TryParse(t2.Text.Trim(), out end);
                return true;
            }
        }

        // ---- yardimcilar ----

        private void TryDisconnect()
        {
            try { if (ax != null) ax.DisconnectOneEx(txtIp.Text.Trim(), -1); } catch { }
        }

        private void SaveCfgFromUi()
        {
            cfg.LastExcel = txtExcel.Text.Trim();
            cfg.Ip = txtIp.Text.Trim();
            int p; if (int.TryParse(txtPort.Text, out p)) cfg.Port = p;
            int m; if (int.TryParse(txtModel.Text, out m)) cfg.Model = m;
            int d; if (int.TryParse(txtDataType.Text, out d)) cfg.PluDataType = d;
            int br; if (txtBlankRows != null && int.TryParse(txtBlankRows.Text, out br)) cfg.BlankRows = br;
            cfg.Save();
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
