using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace CasScaleSender
{
    // Teraziden PLU'lari, CL-Works'un kullandigi ASCII protokolu ile DOGRUDAN TCP
    // uzerinden okur (CAS OCX gerekmez; OCX'in ReadPLU/RecvPLU'su bu terazide cevap
    // vermiyordu). Proxy ile yakalanan gercek protokol:
    //
    //   Istek  (16 byte):  "R02F" + dept(2 HEX) + plu(6 HEX) + ",00\n"
    //                      orn. PLU 10 (0x0A), dept 1:  R02F0100000A,00\n
    //   Cevap:  18 byte sabit header + govde + 1 checksum byte
    //     Header: "W02A" + kayit(5 HEX) + "," + flag(2) + "L" + uzunluk(4 HEX) + ":"
    //       flag "01" = veri var,  "00" = PLU yok / liste sonu (N=0000)
    //     Govde:  meta ( ^=.. .*=.. .N=.... ) + alanlar
    //       Alan: "F=" + kod(2 HEX) + "." + tip(2 HEX) + "," + uzunluk(2 HEX) + ":" + <uzunluk byte deger>
    //       Ilgili kodlar:  01=Departman(2B LE)  02=PLU No(4B LE)  04=PLU Tipi(1B)
    //                       06=Fiyat(4B LE)       0A=Isim(windows-1254)
    public static class CasNetReader
    {
        // Teraziye TCP baglanti testi (ekleme/duzenleme diyalogundaki "Test" butonu).
        // Basariliysa true; degilse false + hata mesaji (out).
        public static bool TestConnection(string ip, int port, int timeoutMs, out string message)
        {
            try
            {
                using (var cli = new TcpClient())
                {
                    if (!cli.ConnectAsync(ip, port).Wait(timeoutMs))
                    {
                        message = "Baglanti kurulamadi (zaman asimi). IP/port ve agi kontrol edin.";
                        return false;
                    }
                    message = "Baglanti basarili: " + ip + ":" + port;
                    return true;
                }
            }
            catch (Exception ex)
            {
                var inner = ex is AggregateException && ex.InnerException != null ? ex.InnerException : ex;
                message = "Baglanti hatasi: " + inner.Message;
                return false;
            }
        }

        public static List<Dictionary<string, string>> Read(
            string ip, int port, int from, int to, int dept, int timeoutMs, Action<string> log,
            Func<bool> cancelled = null)
        {
            var result = new List<Dictionary<string, string>>();
            Encoding enc;
            try { enc = Encoding.GetEncoding("windows-1254"); } catch { enc = Encoding.GetEncoding(1252); }
            log = log ?? (s => { });

            using (var cli = new TcpClient())
            {
                if (!cli.ConnectAsync(ip, port).Wait(timeoutMs))
                {
                    log("Baglanti kurulamadi (zaman asimi). IP/port ve agi kontrol edin.");
                    return result;
                }
                cli.NoDelay = true;
                var st = cli.GetStream();
                st.ReadTimeout = timeoutMs;

                for (int plu = from; plu <= to; plu++)
                {
                    if (cancelled != null && cancelled()) { log("Islem iptal edildi."); break; }
                    string req = "R02F" + Hex(dept, 2) + Hex(plu, 6) + ",00\n";
                    byte[] rb = Encoding.ASCII.GetBytes(req);
                    try { st.Write(rb, 0, rb.Length); }
                    catch (Exception ex) { log("Gonderim hatasi PLU " + plu + ": " + ex.Message); break; }

                    byte[] msg = ReadFrame(st);
                    if (msg == null) { log("PLU " + plu + ": cevap yok (zaman asimi)."); break; }

                    string flag = Ascii(msg, 10, 2);      // "01" veri var / "00" yok
                    if (flag != "01")
                    {
                        // PLU yok. N=0000 ise liste bitti -> dur, degilse bos slot -> atla.
                        if (BodyHasN0000(msg)) { break; }
                        continue;
                    }

                    var d = ParseFields(msg, enc);
                    if (d == null) continue;
                    result.Add(d);
                    string nm; d.TryGetValue("Name", out nm);
                    log("  <- PLU " + plu + " alindi: " + (nm ?? ""));
                }
            }
            return result;
        }

        // 18 byte header + (L) govde + 1 checksum = tam bir cevap cerçevesi.
        private static byte[] ReadFrame(NetworkStream st)
        {
            byte[] head = ReadExact(st, 18);
            if (head == null) return null;
            int bodyLen;
            try { bodyLen = Convert.ToInt32(Ascii(head, 13, 4), 16); } catch { return null; }
            byte[] rest = ReadExact(st, bodyLen + 1); // govde + checksum
            if (rest == null) return null;
            var msg = new byte[18 + rest.Length];
            Buffer.BlockCopy(head, 0, msg, 0, 18);
            Buffer.BlockCopy(rest, 0, msg, 18, rest.Length);
            return msg;
        }

        private static byte[] ReadExact(NetworkStream st, int n)
        {
            var buf = new byte[n];
            int off = 0;
            while (off < n)
            {
                int r;
                try { r = st.Read(buf, off, n - off); }
                catch { return null; }
                if (r <= 0) return null;
                off += r;
            }
            return buf;
        }

        private static Dictionary<string, string> ParseFields(byte[] msg, Encoding enc)
        {
            // Govde 18. byte'tan sonra baslar; ilk "F=" bulununca alanlari cozeriz.
            int i = IndexOf(msg, "F=", 18);
            if (i < 0) return null;

            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            while (i + 11 <= msg.Length && msg[i] == (byte)'F' && msg[i + 1] == (byte)'=')
            {
                int code, len;
                try
                {
                    code = Convert.ToInt32(Ascii(msg, i + 2, 2), 16); // F=CC
                    // i+4 '.'  i+5..i+6 tip  i+7 ','  i+8..i+9 uzunluk  i+10 ':'
                    len = Convert.ToInt32(Ascii(msg, i + 8, 2), 16);
                }
                catch { break; }
                int valStart = i + 11;
                if (valStart + len > msg.Length) break;

                switch (code)
                {
                    case 0x01: d["Department No"] = LeInt(msg, valStart, len).ToString(); break;
                    case 0x02: d["PLU No"] = LeInt(msg, valStart, len).ToString(); break;
                    case 0x04: d["PLU Type"] = LeInt(msg, valStart, len).ToString(); break;
                    case 0x06: d["Price"] = LeInt(msg, valStart, len).ToString(); break;
                    case 0x0A: d["Name"] = enc.GetString(msg, valStart, len).TrimEnd('\0', ' ').Trim(); break;
                }
                i = valStart + len; // sonraki alan hemen ardindan gelir (ayrac yok)
            }
            // PLU No yoksa gecersiz kabul et.
            string p;
            if (!d.TryGetValue("PLU No", out p) || p == "0") return d.Count > 0 ? d : null;
            return d;
        }

        private static bool BodyHasN0000(byte[] msg) { return IndexOf(msg, "N=0000", 18) >= 0; }

        private static long LeInt(byte[] b, int off, int len)
        {
            long v = 0;
            for (int k = 0; k < len && off + k < b.Length; k++) v |= (long)(b[off + k] & 0xFF) << (8 * k);
            return v;
        }

        private static string Ascii(byte[] b, int off, int len)
        {
            var sb = new StringBuilder(len);
            for (int k = 0; k < len && off + k < b.Length; k++) sb.Append((char)b[off + k]);
            return sb.ToString();
        }

        private static int IndexOf(byte[] b, string pat, int start)
        {
            for (int i = start; i <= b.Length - pat.Length; i++)
            {
                bool ok = true;
                for (int k = 0; k < pat.Length; k++) if (b[i + k] != (byte)pat[k]) { ok = false; break; }
                if (ok) return i;
            }
            return -1;
        }

        private static string Hex(int v, int width) { return v.ToString("X" + width); }
    }
}
