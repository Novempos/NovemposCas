#!/usr/bin/env python3
"""CAS CL serisi terazi TCP EMÜLATÖRÜ — fiziksel terazi olmadan test.

CL-Works'ün ASCII/binary protokolünü (proxy ile çözülen) konuşur; NovemposCas
GÖNDER / AL / SİL akışları buna karşı çalışır. İn-memory PLU deposu tutar ve
canlı bir web görünümü sunar.

Protokol (port 20304 TCP):
  - OKU:  "R02F" + dept(2H) + plu(6H) + ",00\\n"   (16 byte)
          → W02A cevap çerçevesi (18B header + gövde + 1 checksum).
            flag "01"=veri, "00"=boş/son (N=0000). Alan: F=kod(2H).tip(2H),uzunluk(2H):deger
            Kodlar: 01=Dept(2B LE) 02=PLU No(4B LE) 04=Tip(1B) 06=Fiyat(4B LE)
                    0A=İsim(win-1254) 0B=Ürün Kodu(4B LE)
  - SİL:  "C43F13," + dept(2H) + plu(6H) + "\\n"     → "C003:O..\\n\\n"
  - YAZ:  OCX "W02A"+kayıt(5H)+","+flag(2)+"L"+uzunluk(4H)+":"+alanlar+checksum
          → ACK: "^=01.*=01.$=0.&=<ipHEX>.@=<portHEX>.?=3.W02:O<kayıt2H>\\n\\n"

Kullanım:
    python tools/cas_scale_emulator.py
    python tools/cas_scale_emulator.py --port 20304 --http 8081
    → App/CLI terazi IP'sini 127.0.0.1:20304 yap; web görünüm http://127.0.0.1:8081
"""
import argparse
import datetime
import random
import socket
import socketserver
import sys
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

# Son kullanma tarihi (F=10, 4B LE) kodlaması: 2000-01-01'den itibaren gün
# sayısı hipotezi (DATAOPTION MAX=9999 → ~2027'ye denk gelir). CLWorks yanlış
# tarih gosterirse buradaki EPOCH/format degistirilir.
_DATE_EPOCH = datetime.date(2000, 1, 1)


def _random_sellby():
    """Rastgele gerçek bir son-kullanma tarihi → (F=10 gün-değeri, gösterim str)."""
    d = datetime.date.today() + datetime.timedelta(days=random.randint(1, 45))
    return ((d - _DATE_EPOCH).days, d.strftime("%d.%m.%Y"))


def _sellby_str(val):
    """F=10 gün-değerini okunur tarihe çevir (web görünüm)."""
    try:
        return (_DATE_EPOCH + datetime.timedelta(days=int(val))).strftime("%d.%m.%Y")
    except (ValueError, OverflowError):
        return "-"

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except (AttributeError, ValueError):
    pass

ENC = "cp1254"  # windows-1254 (Türkçe isim)

# ── PLU deposu ──────────────────────────────────────────────────────────────
_LOCK = threading.Lock()
# {(dept, plu): {"name":str, "price":int, "type":int, "itemcode":int, "dept":int}}
_PLU = {}


def _log(msg):
    print(msg, flush=True)


# ── ESC-benzeri alan/çerçeve yardımcıları ──────────────────────────────────
def _field(code, type_, value_bytes):
    """F=CC.TT,LL:<value>  — LL 2 HEX (okuma cevabı formatı, CasNetReader bekler)."""
    head = "F=%02X.%02X,%02X:" % (code, type_, len(value_bytes))
    return head.encode("ascii") + value_bytes


def _le(v, n):
    return int(v).to_bytes(n, "little", signed=False)


def build_read_response(plu, rec):
    """Bir PLU kaydı için W02A veri çerçevesi (flag=01)."""
    body = b"N=%04X." % (plu & 0xFFFF)
    body += _field(0x01, 0x57, _le(rec["dept"], 2))
    body += _field(0x02, 0x4C, _le(plu, 4))
    body += _field(0x04, 0x4D, _le(rec["type"], 1))
    body += _field(0x06, 0x4C, _le(rec["price"], 4))
    body += _field(0x0B, 0x4C, _le(rec["itemcode"], 4))
    body += _field(0x10, 0x4C, _le(rec.get("sellby", 0), 4))  # Son kullanma (gün)
    name_b = rec["name"].encode(ENC, "replace")
    body += _field(0x0A, 0x53, name_b)
    return _frame("01", plu, body)


def build_empty_response(plu, ended):
    """PLU yok. ended=True → N=0000 (liste sonu, okuyucu durur)."""
    body = b"N=0000." if ended else b"N=%04X." % (plu & 0xFFFF)
    return _frame("00", plu, body)


def _frame(flag, kayit, body):
    """18B header (W02A+kayıt5H+,+flag2+L+len4H+:) + gövde + 1 checksum(0)."""
    header = "W02A%05X,%sL%04X:" % (kayit & 0xFFFFF, flag, len(body))
    assert len(header) == 18, len(header)
    return header.encode("ascii") + body + b"\x00"


# ── Gelen komut çerçeveleme (binary-safe) ──────────────────────────────────
def read_command(sock_file):
    """Bir komut oku. R02F/C43.. → \\n'e kadar; W02A → header'daki uzunluk kadar.
    Doner: (kind, raw) veya None (bağlantı kapandı)."""
    first = sock_file.read(1)
    if not first:
        return None
    if first == b"R":
        rest = _read_until_lf(sock_file)          # R02F...,00\n
        return ("R", first + rest)
    if first == b"C":
        rest = _read_until_lf(sock_file)          # C43F13,...\n
        return ("C", first + rest)
    if first == b"W":
        head = first + sock_file.read(17)         # 18B header
        if len(head) < 18:
            return None
        try:
            blen = int(head[13:17], 16)
        except ValueError:
            return None
        body = sock_file.read(blen + 1)           # gövde + checksum
        return ("W", head + body)
    # Bilinmeyen — satır sonuna kadar yut
    return ("?", first + _read_until_lf(sock_file))


def _read_until_lf(sock_file):
    out = bytearray()
    while True:
        b = sock_file.read(1)
        if not b:
            break
        out += b
        if b == b"\n":
            break
    return bytes(out)


# ── Komut işleyiciler ───────────────────────────────────────────────────────
def handle_read(raw):
    # "R02F" + dept(2H) + plu(6H) + ",00\n"
    try:
        dept = int(raw[4:6], 16)
        plu = int(raw[6:12], 16)
    except (ValueError, IndexError):
        return build_empty_response(0, True)
    with _LOCK:
        rec = _PLU.get((dept, plu))
        max_plu = max((k[1] for k in _PLU if k[0] == dept), default=0)
    if rec is not None:
        _log("  OKU  PLU %d → %s" % (plu, rec["name"]))
        return build_read_response(plu, rec)
    # Yok: max'ın üstündeyse liste sonu (N=0000), değilse boş slot (devam).
    return build_empty_response(plu, ended=(plu > max_plu))


def handle_delete(raw):
    # "C43F13," + dept(2H) + plu(6H) + "\n"
    try:
        after = raw.split(b",", 1)[1]
        dept = int(after[0:2], 16)
        plu = int(after[2:8], 16)
    except (ValueError, IndexError):
        return b"C003:E00\n\n"
    with _LOCK:
        existed = _PLU.pop((dept, plu), None) is not None
    _log("  SİL  PLU %d %s" % (plu, "(silindi)" if existed else "(yoktu)"))
    return b"C003:O13\n\n"


def parse_write_fields(frame):
    """W02A yazma çerçevesindeki alanları çöz. Yazma alan uzunluğu DEĞİŞKEN
    haneli (F=02.4C,4:) — okuma cevabından farklı; regex'siz elle tara."""
    fields = {}
    i = frame.find(b":", 0)  # header sonrası ilk ':'
    if i < 0:
        return fields
    i += 1
    n = len(frame)
    while i < n:
        if frame[i:i + 2] != b"F=":
            i += 1
            continue
        try:
            code = int(frame[i + 2:i + 4], 16)
            # tip: i+5..i+6 ; sonra ',' ; uzunluk ',' ile ':' arası (değişken)
            comma = frame.index(b",", i + 4)
            colon = frame.index(b":", comma)
            length = int(frame[comma + 1:colon], 10)
        except (ValueError, IndexError):
            break
        vstart = colon + 1
        value = frame[vstart:vstart + length]
        fields[code] = value
        i = vstart + length
    return fields


def handle_write(frame, conn_ip, conn_port):
    # Kayıt no header'da: W02A<kayıt5H>,...
    try:
        kayit = int(frame[4:9], 16)
    except ValueError:
        kayit = 0
    f = parse_write_fields(frame)
    dept = int.from_bytes(f.get(0x01, b"\x01"), "little") if f.get(0x01) else 1
    plu = int.from_bytes(f.get(0x02, b"\x00"), "little") if f.get(0x02) else 0
    if plu == 0:
        return _write_ack(kayit, conn_ip, conn_port)
    price = int.from_bytes(f.get(0x06, b"\x00"), "little") if f.get(0x06) else 0
    itemcode = int.from_bytes(f.get(0x0B, b"\x00"), "little") if f.get(0x0B) else 0
    ptype = f.get(0x04, b"\x01")[0] if f.get(0x04) else 1
    name = f.get(0x0A, b"").decode(ENC, "replace").rstrip("\x00 ")
    with _LOCK:
        prev = _PLU.get((dept, plu))
        # Son kullanma tarihini stabil tut (varsa koru, yoksa rastgele ata).
        sellby = prev["sellby"] if prev else _random_sellby()[0]
        _PLU[(dept, plu)] = {"name": name, "price": price, "type": ptype,
                             "itemcode": itemcode, "dept": dept, "sellby": sellby}
    _log("  YAZ  PLU %d = '%s' fiyat=%d urunkodu=%d" % (plu, name, price, itemcode))
    return _write_ack(kayit, conn_ip, conn_port)


def _write_ack(kayit, ip, port):
    try:
        ip_hex = "".join("%02X" % int(o) for o in ip.split("."))
    except ValueError:
        ip_hex = "7F000001"
    return ("^=01.*=01.$=0.&=%s.@=%04X.?=3.W02:O%02X\n\n"
            % (ip_hex, port & 0xFFFF, kayit & 0xFF)).encode("ascii")


# ── TCP sunucu ──────────────────────────────────────────────────────────────
class _Handler(socketserver.BaseRequestHandler):
    def handle(self):
        peer = "%s:%d" % self.client_address
        _log("[bağlantı] %s" % peer)
        f = self.request.makefile("rb")
        my_ip, my_port = self.request.getsockname()
        try:
            while True:
                cmd = read_command(f)
                if cmd is None:
                    break
                kind, raw = cmd
                if kind == "R":
                    resp = handle_read(raw)
                elif kind == "C":
                    resp = handle_delete(raw)
                elif kind == "W":
                    resp = handle_write(raw, my_ip, my_port)
                else:
                    resp = b""
                if resp:
                    self.request.sendall(resp)
        except (OSError, ValueError):
            pass
        finally:
            _log("[kapandı] %s" % peer)


class _Server(socketserver.ThreadingTCPServer):
    allow_reuse_address = True
    daemon_threads = True


# ── Web görünüm ─────────────────────────────────────────────────────────────
_PAGE_HEAD = """<!doctype html><html lang="tr"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<meta http-equiv="refresh" content="2"><title>CAS Terazi Emülatörü</title>
<style>
 :root{color-scheme:dark}body{margin:0;background:#0e1116;color:#c9d1d9;
 font:14px/1.5 -apple-system,Segoe UI,Roboto,sans-serif}
 header{position:sticky;top:0;background:#161b22;border-bottom:1px solid #30363d;
  padding:14px 20px;display:flex;gap:12px;align-items:center}
 h1{font-size:16px;margin:0}.dot{width:9px;height:9px;border-radius:50%;
  background:#3fb950;box-shadow:0 0 8px #3fb950}.muted{color:#7d8590;font-size:12px}
 table{border-collapse:collapse;width:100%;max-width:760px;margin:20px auto}
 th,td{padding:7px 12px;border-bottom:1px solid #21262d;text-align:left}
 th{color:#7d8590;font-weight:600;font-size:12px}td.n{text-align:right;
  font-family:"SF Mono",Menlo,monospace}tr:hover{background:#161b22}
 #empty{max-width:520px;margin:60px auto;text-align:center;color:#7d8590}
</style></head><body>
<header><span class="dot"></span><h1>CAS Terazi Emülatörü</h1>
<span class="muted">__CNT__ PLU · otomatik yenilenir</span></header>
"""


class _Web(BaseHTTPRequestHandler):
    def log_message(self, *a):
        pass

    def do_GET(self):
        with _LOCK:
            rows = sorted(_PLU.items())
        html = _PAGE_HEAD.replace("__CNT__", str(len(rows)))
        if not rows:
            html += ("<div id='empty'>Henüz PLU yok.<br><br>App'te terazi IP'sini "
                     "<b>127.0.0.1:20304</b> yapıp <b>GÖNDER</b> deyin.</div>")
        else:
            html += ("<table><tr><th>Dept</th><th>PLU No</th><th>Ürün Kodu</th>"
                     "<th>Ad</th><th>Fiyat</th><th>Tip</th><th>Son Kullanma</th></tr>")
            for (dept, plu), r in rows:
                html += ("<tr><td class=n>%d</td><td class=n>%d</td><td class=n>%d</td>"
                         "<td>%s</td><td class=n>%.2f</td><td class=n>%s</td><td class=n>%s</td></tr>"
                         % (dept, plu, r["itemcode"],
                            _esc(r["name"]), r["price"] / 100.0,
                            "tartılı" if r["type"] == 1 else "adet",
                            _sellby_str(r.get("sellby", 0))))
            html += "</table>"
        html += "</body></html>"
        body = html.encode("utf-8")
        self.send_response(200)
        self.send_header("Content-Type", "text/html; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)


def _esc(s):
    return (s.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;"))


def main():
    ap = argparse.ArgumentParser(description="CAS CL terazi TCP emülatörü")
    ap.add_argument("--host", default="0.0.0.0", help="dinleme adresi")
    ap.add_argument("--port", type=int, default=20304, help="terazi TCP portu")
    ap.add_argument("--http", type=int, default=8081, help="web görünüm portu")
    ap.add_argument("--seed", type=int, default=0,
                    help="başlangıçta N adet örnek PLU yükle (test için)")
    args = ap.parse_args()

    for i in range(1, args.seed + 1):
        _PLU[(1, i)] = {"name": "Ornek %d" % i, "price": i * 100, "type": 1,
                        "itemcode": i, "dept": 1, "sellby": _random_sellby()[0]}

    scale = _Server((args.host, args.port), _Handler)
    web = ThreadingHTTPServer((args.host, args.http), _Web)
    threading.Thread(target=scale.serve_forever, daemon=True).start()

    _log("Terazi dinliyor → %s:%d" % (args.host, args.port))
    _log("Web görünüm     → http://127.0.0.1:%d" % args.http)
    _log("App: terazi IP 127.0.0.1, port %d (GÖNDER/AL/SİL test)" % args.port)
    _log("Ctrl+C ile çık.\n")
    try:
        web.serve_forever()
    except KeyboardInterrupt:
        _log("\nKapatılıyor…")
    finally:
        web.server_close()
        scale.shutdown()


if __name__ == "__main__":
    main()
