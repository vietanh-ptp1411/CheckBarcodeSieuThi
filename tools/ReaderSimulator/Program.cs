// Giả lập đầu đọc barcode qua TCP để test phần mềm khi chưa có thiết bị.
//
//   dotnet run -- [--mode server|client] [--host 127.0.0.1] [--port 2001] [--suffix crlf|cr|lf|stx|none]
//
//   --mode server (mặc định): giống đầu đọc chạy TCP Server -> phần mềm chọn "TCP Client", IP 127.0.0.1
//   --mode client           : giống đầu đọc chạy TCP Client -> phần mềm chọn "TCP Server"
//
// Gõ mã rồi Enter để gửi. Enter trống = gửi một mã mẫu ngẫu nhiên. Gõ "q" để thoát.
// Khi phần mềm gửi lệnh trigger xuống, simulator trả về một mã mẫu ngẫu nhiên.

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
    var buf = new byte[256];
    var endpoint = c.Client.RemoteEndPoint;
    try
    {
        var stream = c.GetStream();
        int n;
        while ((n = await stream.ReadAsync(buf, cts.Token)) > 0)
        {
            var cmd = Encoding.ASCII.GetString(buf, 0, n).Replace("\r", "\\r").Replace("\n", "\\n");
            Console.WriteLine($"[<] Nhận lệnh \"{cmd}\" -> trả về mã mẫu");
            Send(samples[Random.Shared.Next(samples.Length)]);
        }
    }
    catch (Exception) { }
    Console.WriteLine($"[-] Mất kết nối: {endpoint}");
    lock (clientsLock) clients.Remove(c);
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
