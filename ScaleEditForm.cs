using System;
using System.Drawing;
using System.Windows.Forms;

namespace CasScaleSender
{
    // Tek bir teraziyi ekleme/duzenleme diyalogu. "Baglantiyi Test Et" ile
    // girilen IP:Port'a TCP baglanti denenir. Kaydet'te [Result]'a yazar.
    public class ScaleEditForm : Form
    {
        private TextBox txtName, txtIp, txtPort, txtModel, txtDataType;
        private Label lblTest;

        // Version alaninin bu diyalogda kendi kutusu yok (AppSettings.Version gibi
        // cogunlukla bos birakilir, elle ayarlar.txt'te duzenlenir); Duzenle'de
        // var olan degeri sessizce koruyoruz ki Kaydet, mevcut scaleN.version'i
        // silmesin.
        private readonly string existingVersion;

        public ScaleConfig Result { get; private set; }

        public ScaleEditForm(ScaleConfig existing, string title)
        {
            existingVersion = existing != null ? existing.Version : "";
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false; MaximizeBox = false;
            ClientSize = new Size(380, 300);
            Font = new Font("Segoe UI", 9f);

            int x1 = 14, x2 = 120, w = 236, y = 16, gap = 34;

            AddLbl("Ad:", x1, y + 4);
            txtName = Txt(x2, y, w, existing != null ? existing.Name : "");
            y += gap;
            AddLbl("Terazi IP:", x1, y + 4);
            txtIp = Txt(x2, y, w, existing != null ? existing.Ip : "192.168.1.1");
            y += gap;
            AddLbl("Port:", x1, y + 4);
            txtPort = Txt(x2, y, 90, existing != null ? existing.Port.ToString() : "20304");
            y += gap;
            AddLbl("Model:", x1, y + 4);
            txtModel = Txt(x2, y, 90, existing != null ? existing.Model.ToString() : "5000");
            Controls.Add(new Label { Text = "(5000 = CL5000/CL3000)", Left = x2 + 98, Top = y + 4, Width = 160, ForeColor = Color.Gray });
            y += gap;
            AddLbl("PLU veri tipi:", x1, y + 4);
            txtDataType = Txt(x2, y, 90, existing != null ? existing.DataType.ToString() : "98");
            Controls.Add(new Label { Text = "(98=V06, 97=V05)", Left = x2 + 98, Top = y + 4, Width = 160, ForeColor = Color.Gray });

            y += gap + 6;
            var btnTest = new Button { Text = "Baglantiyi Test Et", Left = x1, Top = y, Width = 150, Height = 28 };
            btnTest.Click += (s, e) => TestConn();
            Controls.Add(btnTest);
            lblTest = new Label { Left = x1 + 158, Top = y + 6, Width = 200, Text = "", ForeColor = Color.Gray };
            Controls.Add(lblTest);

            y += 44;
            var ok = new Button { Text = "Kaydet", Left = x2 + w - 160, Top = y, Width = 76, Height = 30, DialogResult = DialogResult.OK };
            ok.Click += (s, e) => OnSave();
            var cancel = new Button { Text = "Iptal", Left = x2 + w - 78, Top = y, Width = 76, Height = 30, DialogResult = DialogResult.Cancel };
            Controls.Add(ok); Controls.Add(cancel);
            AcceptButton = ok; CancelButton = cancel;
        }

        private void AddLbl(string t, int x, int y) { Controls.Add(new Label { Text = t, Left = x, Top = y, Width = 104 }); }

        private TextBox Txt(int x, int y, int w, string val)
        {
            var tb = new TextBox { Left = x, Top = y, Width = w, Text = val };
            Controls.Add(tb);
            return tb;
        }

        // Diyalogu kapatmadan girilen IP:Port'a baglanti dener.
        private void TestConn()
        {
            string ip = txtIp.Text.Trim();
            if (ip.Length == 0) { lblTest.ForeColor = Color.Firebrick; lblTest.Text = "IP girin."; return; }
            int port; if (!int.TryParse(txtPort.Text.Trim(), out port)) { lblTest.ForeColor = Color.Firebrick; lblTest.Text = "Port sayisal olmali."; return; }

            lblTest.ForeColor = Color.Gray; lblTest.Text = "Deneniyor...";
            Application.DoEvents();
            string msg;
            bool ok = CasNetReader.TestConnection(ip, port, 4000, out msg);
            lblTest.ForeColor = ok ? Color.Green : Color.Firebrick;
            lblTest.Text = ok ? "Baglanti OK" : "Basarisiz";
            if (!ok) MessageBox.Show(this, msg, "Baglanti Testi", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        // Alanlari dogrula ve Result'a yaz. Hata varsa DialogResult'i iptal edip acik tutar.
        private void OnSave()
        {
            string ip = txtIp.Text.Trim();
            if (ip.Length == 0) { Warn("Terazi IP girin."); return; }
            int port; if (!int.TryParse(txtPort.Text.Trim(), out port)) { Warn("Port sayisal olmali."); return; }
            int model; if (!int.TryParse(txtModel.Text.Trim(), out model)) { Warn("Model sayisal olmali."); return; }
            int dt; if (!int.TryParse(txtDataType.Text.Trim(), out dt)) { Warn("PLU veri tipi sayisal olmali."); return; }

            string name = txtName.Text.Trim();
            if (name.Length == 0) name = ip; // ad bos ise IP'yi ad yap

            Result = new ScaleConfig { Name = name, Ip = ip, Port = port, Model = model, DataType = dt, Version = existingVersion };
        }

        private void Warn(string m)
        {
            DialogResult = DialogResult.None; // diyalogu acik tut
            MessageBox.Show(this, m, "Eksik/Hatali Bilgi", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
