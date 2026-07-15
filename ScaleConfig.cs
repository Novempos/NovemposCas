namespace CasScaleSender
{
    // Tek bir terazinin ayarlari. Kullanici en fazla 10 tane tanimlayabilir.
    // GONDER: birden cok terazi secilip hepsine gonderilir.
    // AL:     yalnizca tek terazi secilebilir.
    public class ScaleConfig
    {
        public string Name = "Terazi";
        public string Ip = "192.168.1.1";
        public int Port = 20304;      // CAS CL serisi standart veri portu
        public int Model = 5000;      // 5000 = CL5000/CL3000
        public int DataType = 98;     // 98 = PLU V06 (97=V05, 9=V02)
        public string Version = "";   // bos birakilabilir (terazi bazinda; AppSettings.Version bkz.)

        // Listede gorunecek etiket.
        public override string ToString()
        {
            string nm = string.IsNullOrWhiteSpace(Name) ? "(adsiz)" : Name.Trim();
            return nm + "  —  " + Ip + ":" + Port;
        }

        public ScaleConfig Clone()
        {
            return new ScaleConfig { Name = Name, Ip = Ip, Port = Port, Model = Model, DataType = DataType, Version = Version };
        }
    }
}
