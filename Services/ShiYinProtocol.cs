using System.Globalization;
using System.Text;

namespace CheckBarcodeSieuThi.Services
{
    /// <summary>Kết quả đầu đọc trả về cho một lệnh: ACK = chấp nhận, NAK = từ chối, ENQ = không hỗ trợ / sai cú pháp.</summary>
    public enum ReplyStatus
    {
        Ack,
        Nak,
        Enq,
    }

    public sealed record CommandReply(string Code, string Value, ReplyStatus Status)
    {
        public bool IsAck => Status == ReplyStatus.Ack;
    }

    /// <summary>
    /// Giao thức cấu hình của đầu đọc Shijie / Superlead (ICW series), được dựng lại từ phần mềm ShiJieConfig của hãng.
    ///
    /// Khung lệnh menu:      STX F0 ETX  &lt;lệnh&gt;[;&lt;lệnh&gt;...]  "."   (ghi tạm: kết thúc bằng "!.")
    /// Khung lệnh sản phẩm:  STX F1 ETX  &lt;lệnh&gt;  "."                  (model, số serial)
    /// Mỗi lệnh = 6 ký tự hex (mã tham số) + phần đuôi:
    ///     "?"  đọc giá trị      "*"  đọc khoảng giá trị cho phép      "&lt;giá trị&gt;"  ghi giá trị
    /// Trả lời: &lt;mã 6 hex&gt;&lt;giá trị&gt;&lt;ACK|NAK|ENQ&gt; , nhiều lệnh cách nhau bằng ";" và kết thúc bằng "." (hoặc "!" với lệnh ghi tạm).
    /// Ghi kèm lệnh 050203 để lưu vào bộ nhớ đầu đọc (giữ sau khi tắt nguồn).
    /// Trigger mềm: STX F4 ETX (bắt đầu đọc), STX F5 ETX (dừng).
    /// </summary>
    public static class ShiYinProtocol
    {
        public const byte STX = 0x02;
        public const byte ETX = 0x03;
        public const byte ENQ = 0x05;
        public const byte ACK = 0x06;
        public const byte NAK = 0x15;

        /// <summary>Lưu cấu hình đang chạy vào bộ nhớ đầu đọc.</summary>
        public const string SaveToFlash = "050203";

        /// <summary>Khôi phục cấu hình gốc của nhà sản xuất (đầu đọc sẽ khởi động lại).</summary>
        public const string RestoreFactory = "0D0100";

        public static readonly byte[] TriggerFrame = [STX, 0xF4, ETX];
        public static readonly byte[] ReleaseFrame = [STX, 0xF5, ETX];

        /// <summary>Gói tin UDP broadcast tới cổng 6000 để tìm đầu đọc trong mạng LAN.</summary>
        public static readonly byte[] DiscoveryRequest = Encoding.ASCII.GetBytes("ShiYinScannerr\0Json");
        public const int DiscoveryPort = 6000;

        public static byte[] MenuFrame(string commands, bool temporary = false) =>
            Frame(0xF0, commands + (temporary ? "!." : "."));

        public static byte[] MenuFrame(IEnumerable<string> commands, bool temporary = false) =>
            MenuFrame(string.Join(";", commands), temporary);

        public static byte[] ProductFrame(string command) => Frame(0xF1, command + ".");

        private static byte[] Frame(byte kind, string body)
        {
            var payload = Encoding.Latin1.GetBytes(body);
            var frame = new byte[payload.Length + 3];
            frame[0] = STX;
            frame[1] = kind;
            frame[2] = ETX;
            payload.CopyTo(frame, 3);
            return frame;
        }

        public static bool IsStatusByte(byte b) => b is ACK or NAK or ENQ;

        /// <summary>Chuỗi có chứa ký tự ACK/NAK/ENQ thì là phản hồi lệnh cấu hình, không phải mã vạch.</summary>
        public static bool LooksLikeReply(ReadOnlySpan<byte> data)
        {
            foreach (var b in data)
                if (IsStatusByte(b)) return true;
            return false;
        }

        /// <summary>
        /// Tách chuỗi trả lời thành từng lệnh. Ví dụ "0E01000\x06;080505?\x05." →
        /// [("0E0100","0",Ack), ("080505","",Enq)].
        /// </summary>
        public static List<CommandReply> ParseReplies(string raw)
        {
            var list = new List<CommandReply>();
            var text = raw.TrimEnd('.', '!');
            foreach (var item in text.Split(';'))
            {
                if (item.Length < 7) continue;
                var code = item[..6].ToUpperInvariant();
                var body = item[6..];
                var last = body[^1];
                var status = last switch
                {
                    (char)ACK => ReplyStatus.Ack,
                    (char)NAK => ReplyStatus.Nak,
                    (char)ENQ => ReplyStatus.Enq,
                    _ => ReplyStatus.Enq,
                };
                var value = IsStatusByte((byte)last) ? body[..^1] : body;
                if (status != ReplyStatus.Ack)
                    value = value.TrimEnd('?', '*');
                list.Add(new CommandReply(code, value, status));
            }
            return list;
        }

        // ------------------------------------------------------------------
        // Chuỗi ký tự (prefix, suffix, lệnh trigger) được lưu trong đầu đọc dạng hex.
        // Trên giao diện hiển thị dạng dễ đọc: [CR] [LF] [TAB] hoặc \xNN cho ký tự không in được.
        // ------------------------------------------------------------------

        public static string HexToDisplay(string hex)
        {
            var sb = new StringBuilder();
            for (int i = 0; i + 1 < hex.Length; i += 2)
            {
                if (!byte.TryParse(hex.AsSpan(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
                    continue;
                sb.Append(b switch
                {
                    0x0D => "[CR]",
                    0x0A => "[LF]",
                    0x09 => "[TAB]",
                    >= 0x20 and <= 0x7E => ((char)b).ToString(),
                    _ => $"\\x{b:X2}",
                });
            }
            return sb.ToString();
        }

        public static string DisplayToHex(string text)
        {
            var sb = new StringBuilder();
            int i = 0;
            while (i < text.Length)
            {
                if (text[i] == '[')
                {
                    int close = text.IndexOf(']', i);
                    if (close > i)
                    {
                        var token = text[(i + 1)..close].ToUpperInvariant();
                        byte? b = token switch
                        {
                            "CR" => 0x0D,
                            "LF" => 0x0A,
                            "TAB" => 0x09,
                            "STX" => 0x02,
                            "ETX" => 0x03,
                            "GS" => 0x1D,
                            _ => null,
                        };
                        if (b != null)
                        {
                            sb.Append(b.Value.ToString("X2"));
                            i = close + 1;
                            continue;
                        }
                    }
                }
                if (text[i] == '\\' && i + 1 < text.Length)
                {
                    var n = text[i + 1];
                    if ((n == 'x' || n == 'X') && i + 3 < text.Length &&
                        byte.TryParse(text.AsSpan(i + 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hb))
                    {
                        sb.Append(hb.ToString("X2"));
                        i += 4;
                        continue;
                    }
                    byte? esc = n switch { 'r' => 0x0D, 'n' => 0x0A, 't' => 0x09, '\\' => 0x5C, _ => null };
                    if (esc != null)
                    {
                        sb.Append(esc.Value.ToString("X2"));
                        i += 2;
                        continue;
                    }
                }
                foreach (var bt in Encoding.Latin1.GetBytes(text[i].ToString()))
                    sb.Append(bt.ToString("X2"));
                i++;
            }
            return sb.ToString();
        }

        /// <summary>Khoảng giá trị đầu đọc trả về cho lệnh "*": "1-300000" (min-max) hoặc "0|100|150" (danh sách).</summary>
        public static (int Min, int Max)? ParseRange(string? range)
        {
            if (string.IsNullOrEmpty(range)) return null;
            var parts = range.Split('-');
            if (parts.Length == 2 && int.TryParse(parts[0], out var min) && int.TryParse(parts[1], out var max))
                return (min, max);
            return null;
        }

        public static List<string> ParseOptions(string? range)
        {
            if (string.IsNullOrEmpty(range)) return [];
            return range.Contains('|') ? [.. range.Split('|')] : [];
        }
    }
}
