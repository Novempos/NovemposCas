using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace CasScaleSender.Cli
{
    // Terazi OCX'ini (AxCasScale) GORUNMEZ bir formda barindirir; GUI ile ayni
    // olay-tabanli akisi konsol icin calistirir. Mesaj pompasi Application.DoEvents
    // ile donduruldugu icin COM olaylari (RecvEventString) ayni thread'de gelir.
    public class ScaleHost : Form
    {
        private const int ACTION_DOWNLOAD = 3;
        private const int RECV_SUCCESS = 1001;
        private const int RECV_FAIL = 1002;

        private AxCASSCALELib.AxCasScale ax;
        private readonly Action<string> log;
        private System.Windows.Forms.Timer timer;
        private int timeoutMs;
        private bool done, canceled;

        // gonderim
        private List<string> recs, names;
        private int idx, okCount, failCount;
        private string ip, version;
        private int model, dataType;

        // okuma
        private bool reading;
        private int rPlu, rEnd, rDept, rConsec;
        private List<Dictionary<string, string>> got;

        public ScaleHost(Action<string> logger) { log = logger ?? (s => { }); }

        // Formu asla gosterme (sadece OCX barinagi).
        protected override void SetVisibleCore(bool value) { base.SetVisibleCore(false); }

        public bool Init()
        {
            try
            {
                var force = this.Handle; // form handle'ini olustur
                ax = new AxCASSCALELib.AxCasScale();
                ((ISupportInitialize)ax).BeginInit();
                ax.Size = new Size(2, 2);
                Controls.Add(ax);
                ((ISupportInitialize)ax).EndInit();
                ax.CreateControl();
                ax.RecvEventString += OnRecvString;
                return true;
            }
            catch (Exception ex)
            {
                log("OCX yuklenemedi (register.bat calistirildi mi?): " + ex.Message);
                return false;
            }
        }

        public void RequestCancel() { canceled = true; }

        private void Pump()
        {
            while (!done)
            {
                Application.DoEvents();
                System.Threading.Thread.Sleep(5);
                if (canceled) { log("Iptal edildi."); StopTimer(); done = true; }
            }
        }

        private void StartTimer() { StopTimer(); timer.Start(); }
        private void StopTimer() { if (timer != null) timer.Stop(); }

        // ---- GONDERIM ----
        // Doner: (basarili, basarisiz)
        public int[] RunSend(string ip, int port, int model, string version, int dataType,
                             List<string> records, List<string> names, int timeoutMs)
        {
            this.ip = ip; this.model = model; this.version = version; this.dataType = dataType;
            this.recs = records; this.names = names; this.timeoutMs = timeoutMs;
            idx = 0; okCount = 0; failCount = 0; done = false; reading = false;
            timer = new System.Windows.Forms.Timer { Interval = timeoutMs };
            timer.Tick += (s, e) => OnTimeout();

            int rc = ax.ConnectionEx3(ip, port, -1, model, version, 97);
            log("Baglaniliyor... (ip=" + ip + " port=" + port + ", ret=" + rc + ")");
            if (rc <= 0) { log("Baglanti kurulamadi. IP/port ve agi kontrol edin."); Disconnect(); return new[] { 0, records.Count }; }

            SendNext();
            Pump();
            Disconnect();
            return new[] { okCount, failCount };
        }

        private void SendNext()
        {
            while (idx < recs.Count)
            {
                int rtn = ax.SendDataString(ip, -1, model, version, ACTION_DOWNLOAD, dataType, recs[idx]);
                if (rtn > 0)
                {
                    StartTimer();
                    log(string.Format("  #{0}/{1} '{2}' gonderildi, yanit bekleniyor...", idx + 1, recs.Count, names[idx]));
                    return;
                }
                failCount++;
                log(string.Format("  #{0} '{1}' KUYRUGA ALINAMADI (ret={2})", idx + 1, names[idx], rtn));
                idx++;
            }
            done = true;
        }

        // ---- OKUMA ----
        public List<Dictionary<string, string>> RunReceive(string ip, int port, int model, string version,
                             int dept, int from, int to, int timeoutMs)
        {
            this.ip = ip; this.model = model; this.version = version;
            this.timeoutMs = timeoutMs;
            rPlu = from; rEnd = to; rDept = dept; rConsec = 0;
            got = new List<Dictionary<string, string>>();
            done = false; reading = true;
            timer = new System.Windows.Forms.Timer { Interval = timeoutMs };
            timer.Tick += (s, e) => OnTimeout();

            int rc = ax.ConnectionEx3(ip, port, -1, model, version, 97);
            log("Baglaniliyor... (ip=" + ip + " port=" + port + ", ret=" + rc + ")");
            if (rc <= 0) { log("Baglanti kurulamadi. IP/port ve agi kontrol edin."); Disconnect(); return got; }

            ReadNext();
            Pump();
            Disconnect();
            return got;
        }

        private void ReadNext()
        {
            while (rPlu <= rEnd)
            {
                if (canceled) { done = true; return; }
                int rtn = ax.ReadPLU(rDept, rPlu);
                if (rtn > 0) { StartTimer(); return; }
                rPlu++;
            }
            done = true;
        }

        // ---- OLAYLAR ----
        private void OnRecvString(object sender, AxCASSCALELib._DCasScaleEvents_RecvEventStringEvent e)
        {
            if (reading) { OnReadRecv(e); return; }
            if (e.iTransType != ACTION_DOWNLOAD) return;
            StopTimer();
            if (e.iResult == RECV_SUCCESS) { okCount++; log(string.Format("  -> #{0} OK", idx + 1)); }
            else if (e.iResult == RECV_FAIL) { failCount++; log(string.Format("  -> #{0} BASARISIZ", idx + 1)); }
            else return;
            idx++;
            SendNext();
        }

        private void OnReadRecv(AxCASSCALELib._DCasScaleEvents_RecvEventStringEvent e)
        {
            StopTimer();
            rConsec = 0;
            if (e.iResult == RECV_SUCCESS)
            {
                var d = PluReader.Parse(e.sData);
                if (d != null)
                {
                    got.Add(d);
                    string nm; d.TryGetValue("Name", out nm);
                    log(string.Format("  <- PLU {0} alindi: {1}", rPlu, nm ?? ""));
                }
            }
            rPlu++;
            ReadNext();
        }

        private void OnTimeout()
        {
            StopTimer();
            if (reading)
            {
                rConsec++;
                log(string.Format("  PLU {0}: yanit yok (zaman asimi)", rPlu));
                if (rConsec >= 3) { log("Ust uste yanit alinamadi, okuma durduruldu."); done = true; return; }
                rPlu++;
                ReadNext();
                return;
            }
            failCount++;
            log(string.Format("  -> #{0} ZAMAN ASIMI ({1} sn yanit yok)", idx + 1, timeoutMs / 1000));
            log("Gonderim durduruldu.");
            done = true;
        }

        private void Disconnect()
        {
            try { if (ax != null) ax.DisconnectOneEx(ip, -1); } catch { }
        }
    }
}
