using System;
using System.Collections.Generic;

namespace CasScaleSender
{
    // GONDER akisinin GUI (MainForm) ve CLI (ScaleHost) arasinda ORTAK cekirdegi.
    // Tek bir teraziye baglanir, PLU kayitlarini sirayla gonderir; her kayit icin
    // OCX'in RecvEventString cevabini veya zaman asimini bekler.
    //
    // Bu sinif OCX baglanti/olay kancalarini KENDI KURMAZ: AxCasScale tek bir
    // host/gorunume bagli oldugu icin ornek burada olusturulamaz/paylasilamaz.
    // Cagiran kendi AxCasScale ornegini verir ve o ornegin RecvEventString
    // olayini HandleRecv'e yonlendirir.
    //
    // Kullanim:
    //   var s = new ScaleSendSession(ax, logCallback);
    //   s.Completed += () => { ... sonraki terazi / bitti ... };
    //   s.Start(ip, port, model, version, dataType, records, names, timeoutMs);
    //   // OCX'in RecvEventString olayinda: s.HandleRecv(e.iTransType, e.iResult);
    //   // iptalde:                          s.Cancel();
    public class ScaleSendSession
    {
        private const int ACTION_DOWNLOAD = 3;   // teraziye yaz
        private const int RECV_SUCCESS = 1001;
        private const int RECV_FAIL = 1002;

        private readonly AxCASSCALELib.AxCasScale ax;
        private readonly Action<string> log;
        private readonly System.Windows.Forms.Timer timer;

        private List<string> records, names;
        private string ip, version;
        private int model, dataType, timeoutMs;
        private int index, okCount, failCount;
        private bool active;

        public int OkCount { get { return okCount; } }
        public int FailCount { get { return failCount; } }

        // Bu terazi ile is bitince (kayitlar tukendi VEYA zaman asimiyla
        // durduruldu) tetiklenir. Cancel() ile durdurulursa TETIKLENMEZ.
        public event Action Completed;

        public ScaleSendSession(AxCASSCALELib.AxCasScale ax, Action<string> log)
        {
            this.ax = ax;
            this.log = log ?? (s => { });
            timer = new System.Windows.Forms.Timer();
            timer.Tick += (s, e) => OnTimeout();
        }

        // Baglanir ve gonderime baslar. Baglanti kurulamazsa tum kayitlar
        // basarisiz sayilir ve Completed, Start donmeden ONCE (senkron) tetiklenir.
        public void Start(string ip, int port, int model, string version, int dataType,
                           List<string> records, List<string> names, int timeoutMs)
        {
            this.ip = ip; this.model = model; this.version = version; this.dataType = dataType;
            this.records = records; this.names = names; this.timeoutMs = timeoutMs;
            index = 0; okCount = 0; failCount = 0; active = true;
            timer.Interval = timeoutMs;

            int rc = ax.ConnectionEx3(ip, port, -1, model, version, 97);
            log("  Baglaniliyor... ret=" + rc);
            if (rc <= 0)
            {
                log("  Baglanti kurulamadi, bu terazi atlandi (IP/port ve agi kontrol edin).");
                failCount += records.Count;
                Finish();
                return;
            }
            SendNext();
        }

        // Gonderimi derhal durdurur (baglantiyi keser); Completed TETIKLENMEZ.
        // Kullanici IPTAL ettiginde veya form/host kapatilirken cagirilir.
        public void Cancel()
        {
            if (!active) return;
            active = false;
            timer.Stop();
            Disconnect();
        }

        private void SendNext()
        {
            while (index < records.Count)
            {
                int i = index;
                int rtn = ax.SendDataString(ip, -1, model, version, ACTION_DOWNLOAD, dataType, records[i]);
                if (rtn > 0)
                {
                    timer.Stop(); timer.Start();
                    log(string.Format("  #{0}/{1} '{2}' gonderildi, yanit bekleniyor...", i + 1, records.Count, PluName(i)));
                    return;
                }
                failCount++;
                log(string.Format("  #{0} '{1}' KUYRUGA ALINAMADI (ret={2})", i + 1, PluName(i), rtn));
                index++;
            }
            Finish();
        }

        // Cagiran, kendi ax.RecvEventString olayindan buraya yonlendirir.
        public void HandleRecv(int transType, int result)
        {
            if (!active || transType != ACTION_DOWNLOAD) return;
            if (result == RECV_SUCCESS) { timer.Stop(); okCount++; log(string.Format("  -> #{0} '{1}' OK", index + 1, PluName(index))); }
            else if (result == RECV_FAIL) { timer.Stop(); failCount++; log(string.Format("  -> #{0} '{1}' BASARISIZ", index + 1, PluName(index))); }
            else return;
            index++;
            SendNext();
        }

        private void OnTimeout()
        {
            timer.Stop();
            if (!active) return;
            failCount++;
            log(string.Format("  -> #{0} '{1}' ZAMAN ASIMI ({2} sn yanit yok)", index + 1, PluName(index), timeoutMs / 1000));
            log("  Bu terazide gonderim durduruldu.");
            Finish();
        }

        private void Finish()
        {
            active = false;
            timer.Stop();
            Disconnect();
            var h = Completed;
            if (h != null) h();
        }

        private void Disconnect()
        {
            try { if (ax != null) ax.DisconnectOneEx(ip, -1); } catch { }
        }

        private string PluName(int i) { return (names != null && i >= 0 && i < names.Count) ? names[i] : ""; }
    }
}
