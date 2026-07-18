using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace CasScaleSender.Cli
{
    // Terazi OCX'ini (AxCasScale) ekran disindaki bir formda barindirir; asil
    // GONDERIM mantigi GUI (MainForm) ile ORTAK olan ScaleSendSession'da yasar
    // (bkz. ..\ScaleSendSession.cs). Bu sinif yalnizca ScaleSendSession'i konsol
    // icin SENKRON hale getirir: RunSend, Application.DoEvents ile mesaj
    // pompalayip ScaleSendSession'in Completed olayini bekler. Mesaj pompasi
    // Application.DoEvents ile dondugu icin COM olaylari ayni thread'de gelir.
    // (Okuma OCX ile degil, CasNetReader ile dogrudan TCP uzerinden yapilir.)
    public class ScaleHost : Form
    {
        private AxCASSCALELib.AxCasScale ax;
        private readonly Action<string> log;
        private ScaleSendSession session;
        private bool done, canceled;

        public ScaleHost(Action<string> logger) { log = logger ?? (s => { }); }

        public bool Init()
        {
            try
            {
                // OCX (ActiveX) baglanti/Winsock islemleri icin GERCEK bir pencereye (HWND)
                // ihtiyac duyar; gorunmez formda ConnectionEx3 rtn=0 donuyordu. Formu ekran
                // disina alip Show() ederek OCX'e gercek pencere + mesaj dongusu veriyoruz
                // (kullaniciya gorunmez, taskbar'da yok).
                ShowInTaskbar = false;
                FormBorderStyle = FormBorderStyle.None;
                StartPosition = FormStartPosition.Manual;
                Location = new Point(-32000, -32000);
                Size = new Size(1, 1);
                Show();

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
                if (canceled) { log("Iptal edildi."); if (session != null) session.Cancel(); done = true; }
            }
        }

        // ---- GONDERIM ----
        // Doner: (basarili, basarisiz)
        public int[] RunSend(string ip, int port, int model, string version, int dataType,
                             List<string> records, List<string> names, int timeoutMs)
        {
            done = false;
            session = new ScaleSendSession(ax, log);
            session.Completed += () => { done = true; };
            session.Start(ip, port, model, version, dataType, records, names, timeoutMs);
            Pump();
            return new[] { session.OkCount, session.FailCount };
        }

        // ---- OLAYLAR ----
        private void OnRecvString(object sender, AxCASSCALELib._DCasScaleEvents_RecvEventStringEvent e)
        {
            if (session != null) session.HandleRecv(e.iTransType, e.iResult);
        }
    }
}
