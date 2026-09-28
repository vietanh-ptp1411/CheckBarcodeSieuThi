// Giả lập đầu đọc barcode qua TCP để test phần mềm khi chưa có thiết bị.
//
//   dotnet run -- [--mode server|client] [--host 127.0.0.1] [--port 2001] [--suffix crlf|cr|lf|stx|none]
//
//   --mode server (mặc định): giống đầu đọc chạy TCP Server -> phần mềm chọn "TCP Client", IP 127.0.0.1
//   --mode client           : giống đầu đọc chạy TCP Client -> phần mềm chọn "TCP Server"
//
// Gõ mã rồi Enter để gửi. Enter trống = gửi một mã mẫu ngẫu nhiên. Gõ "q" để thoát.
// Khi phần mềm gửi lệnh trigger xuống, simulator trả về một mã mẫu ngẫu nhiên.
//
// Simulator cũng trả lời các khung lệnh cấu hình theo giao thức ShiJieConfig
// (STX F0/F1 ETX <mã 6 hex><giá trị>. ...) để test trang "Cấu hình bên trong đầu đọc":
// giá trị được giữ trong bộ nhớ, lệnh không biết trả về ENQ, STX F4 ETX = trigger.

using System.Net;
using System.Net.Sockets;
using System.Text;

var mode = GetArg("--mode", "server");
var host = GetArg("--host", "127.0.0.1");
var port = int.Parse(GetArg("--port", "2001"));
var suffix = GetArg("--suffix", "crlf");

string[] samples =
[
    "8934563123456", "8935049500018", "8936036020014", "8934588012341",
    "8938505970012", "8934673001121", "8935001700016", "8934822101135",
];

var clients = new List<TcpClient>();
var clientsLock = new object();
var cts = new CancellationTokenSource();

// Tham số giả lập (mã lệnh -> giá trị), lấy theo một đầu đọc ICW76Pro thật
var config = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    ["0D1302"] = "1.0.52", ["0F0100"] = "ICW76Pro", ["0F0800"] = "SIM00000001",
    ["0E0100"] = "8", ["0E0105"] = "1000", ["080B06"] = "750",
    ["024D21"] = "1454", ["024D22"] = "2908", ["024E0D"] = "1454", ["024E0E"] = "2908",
    ["0F0120"] = "1", ["0F0184"] = "54524947474552", ["0F0185"] = "52454C45415345",
    ["080400"] = "99", ["080500"] = "990D0A", ["0F0140"] = "0", ["025004"] = "4E52",
    ["040905"] = "3", ["0F0145"] = "150", ["0F0123"] = "5", ["040906"] = "2", ["0F0141"] = "100",
    ["040211"] = "0", ["040306"] = "3000", ["040307"] = "15", ["040210"] = "0", ["040217"] = "3000", ["040216"] = "15",
    ["0F028B"] = "284",
    ["021301"] = "1", ["021401"] = "1", ["021101"] = "1", ["021201"] = "1", ["020A01"] = "1", ["020301"] = "1",
    ["020D01"] = "1", ["020401"] = "1", ["021F01"] = "0", ["023701"] = "1", ["023601"] = "1",
};
var ranges = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    ["0E0100"] = "0|8", ["0E0105"] = "1-300000", ["080B06"] = "0-30000", ["024E0D"] = "0-2500", ["0F0120"] = "0-160",
    ["0F0145"] = "0|100|120|150|200", ["0F0123"] = "0-15", ["040906"] = "0|3|2", ["0F0141"] = "1-1000",
    ["040211"] = "0|2|4|5|7|8", ["040306"] = "1-13300", ["040307"] = "1-32",
    ["040210"] = "0|2|4|5|7|8", ["040217"] = "1-13300", ["040216"] = "1-32", ["0F028B"] = "0-1023",
};

Console.OutputEncoding = Encoding.UTF8;
Console.WriteLine($"== Giả lập đầu đọc | mode={mode} port={port} suffix={suffix} ==");

if (mode == "server")
{
    var listener = new TcpListener(IPAddress.Any, port);
    listener.Start();
    Console.WriteLine($"Đang lắng nghe cổng {port}. Trong phần mềm chọn TCP Client, IP 127.0.0.1, cổng {port}.");
    _ = Task.Run(async () =>
    {
        while (!cts.IsCancellationRequested)
        {
            var c = await listener.AcceptTcpClientAsync(cts.Token);
            Console.WriteLine($"[+] PC kết nối: {c.Client.RemoteEndPoint}");
            lock (clientsLock) clients.Add(c);
            _ = ListenForTrigger(c);
        }
    });
}
else
{
    _ = Task.Run(async () =>
    {
        while (!cts.IsCancellationRequested)
        {
            var c = new TcpClient();
            try
            {
                await c.ConnectAsync(host, port, cts.Token);
                Console.WriteLine($"[+] Đã kết nối tới PC {host}:{port}");
                lock (clientsLock) clients.Add(c);
                await ListenForTrigger(c);
            }
            catch (Exception ex) when (!cts.IsCancellationRequested)
            {
                Console.WriteLine($"[!] Không kết nối được {host}:{port}: {ex.Message}. Thử lại sau 3s");
            }
            await Task.Delay(3000);
        }
    });
}

while (true)
{
    var line = Console.ReadLine();
    if (line == null || line.Trim() == "q") break;

    var code = line.Trim().Length == 0 ? samples[Random.Shared.Next(samples.Length)] : line.Trim();
    Send(code);
}
cts.Cancel();

async Task ListenForTrigger(TcpClient c)
{
    var buf = new byte[4096];
    var endpoint = c.Client.RemoteEndPoint;
    var pending = new List<byte>();
    try
    {
        var stream = c.GetStream();
        int n;
        while ((n = await stream.ReadAsync(buf, cts.Token)) > 0)
        {
            pending.AddRange(buf.AsSpan(0, n));
            while (pending.Count > 0)
            {
                // Khung giao thức: STX <loại> ETX ... "." | STX F4/F5 ETX
                if (pending[0] == 0x02 && pending.Count >= 3)
                {
                    if (pending[1] is 0xF4 or 0xF5)
                    {
                        bool trigger = pending[1] == 0xF4;
                        pending.RemoveRange(0, 3);
                        Console.WriteLine(trigger ? "[<] Lệnh trigger (F4) -> trả về mã mẫu" : "[<] Lệnh dừng (F5)");
                        if (trigger) Send(samples[Random.Shared.Next(samples.Length)]);
                        continue;
                    }
                    int end = pending.IndexOf((byte)'.');
                    if (end < 0) break; // chờ thêm dữ liệu
                    var body = Encoding.Latin1.GetString(pending.GetRange(3, end - 3).ToArray());
                    pending.RemoveRange(0, end + 1);
                    var reply = HandleConfigFrame(body);
                    Console.WriteLine($"[<] Lệnh cấu hình \"{body}\"\n[>] Trả lời \"{reply.Replace("\x06", "<ACK>").Replace("\x05", "<ENQ>").Replace("\x15", "<NAK>")}\"");
                    try { c.GetStream().Write(Encoding.Latin1.GetBytes(reply)); } catch (Exception) { }
                    continue;
                }

                // Lệnh dạng chữ (vd "TRIGGER\r\n"): trả về một mã mẫu
                var text = Encoding.ASCII.GetString(pending.ToArray());
                pending.Clear();
                Console.WriteLine($"[<] Nhận lệnh \"{text.Replace("\r", "\\r").Replace("\n", "\\n")}\" -> trả về mã mẫu");
                Send(samples[Random.Shared.Next(samples.Length)]);
            }
        }
    }
    catch (Exception) { }
    Console.WriteLine($"[-] Mất kết nối: {endpoint}");
    lock (clientsLock) clients.Remove(c);
}

// Trả lời một khung lệnh cấu hình: mỗi lệnh "<mã 6 hex><đuôi>" cách nhau bằng ";".
// "?" đọc, "*" đọc khoảng cho phép, còn lại là ghi. Đuôi "!" của cả khung = ghi tạm.
string HandleConfigFrame(string body)
{
    bool temporary = body.EndsWith('!');
    if (temporary) body = body[..^1];

    var parts = new List<string>();
    foreach (var item in body.Split(';', StringSplitOptions.RemoveEmptyEntries))
    {
        if (item.Length < 6) { parts.Add(item + "\x05"); continue; }
        var code = item[..6].ToUpperInvariant();
        var tail = item[6..];
        if (tail == "?")
            parts.Add(config.TryGetValue(code, out var v) ? code + v + "\x06" : item + "\x05");
        else if (tail == "*")
            parts.Add(ranges.TryGetValue(code, out var r) ? code + r + "\x06" : item + "\x05");
        else if (code == "050203" || code == "0D0100")
            parts.Add(code + "\x06");
        else if (config.ContainsKey(code))
        {
            config[code] = tail;
            parts.Add(item + "\x06");
        }
        else
            parts.Add(item + "\x05");
    }
    return string.Join(";", parts) + (temporary ? "!" : ".");
}

void Send(string code)
{
    var framed = suffix switch
    {
        "cr" => code + "\r",
        "lf" => code + "\n",
        "stx" => "\x02" + code + "\x03",
        "none" => code,
        _ => code + "\r\n",
    };
    var bytes = Encoding.UTF8.GetBytes(framed);

    TcpClient[] targets;
    lock (clientsLock) targets = [.. clients];
    if (targets.Length == 0)
    {
        Console.WriteLine("[!] Chưa có kết nối nào, mã không được gửi");
        return;
    }
    foreach (var c in targets)
    {
        try { c.GetStream().Write(bytes); }
        catch (Exception ex) { Console.WriteLine($"[!] Lỗi gửi: {ex.Message}"); }
    }
    Console.WriteLine($"[>] Đã gửi: {code}");
}

string GetArg(string name, string defaultValue)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : defaultValue;
}
