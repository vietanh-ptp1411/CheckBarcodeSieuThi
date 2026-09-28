using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace CheckBarcodeSieuThi.Services
{
    public enum ReaderStatus
    {
        Disconnected,
        Connecting,
        Listening,
        Connected,
        Error,
    }

    public class BarcodeScannedEventArgs(string barcode, string source) : EventArgs
    {
        public string Barcode { get; } = barcode;

        /// <summary>Địa chỉ IP:port của đầu đọc đã gửi mã.</summary>
        public string Source { get; } = source;
    }

    public class ReaderStatusEventArgs(ReaderStatus status, string message) : EventArgs
    {
        public ReaderStatus Status { get; } = status;
        public string Message { get; } = message;
    }

    /// <summary>
    /// Nhận mã vạch từ đầu đọc qua Ethernet (TCP/IP, "no protocol").
    ///
    /// Đầu đọc gửi chuỗi mã dạng text, thường kèm ký tự kết thúc CR/LF (hoặc bọc STX...ETX).
    /// Service tách từng mã theo các ký tự này; nếu đầu đọc được cấu hình không có ký tự
    /// kết thúc thì mã sẽ được chốt khi không có thêm dữ liệu trong <see cref="FrameIdleTimeout"/>.
    ///
    /// Các event được raise trên thread nền, UI cần tự chuyển về Dispatcher.
    /// </summary>
    public sealed class BarcodeReaderService : IDisposable
    {
        private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan FrameIdleTimeout = TimeSpan.FromMilliseconds(100);
        private const int MaxFrameLength = 4096;

        public event EventHandler<BarcodeScannedEventArgs>? BarcodeScanned;
        public event EventHandler<ReaderStatusEventArgs>? StatusChanged;
        public event EventHandler<string>? Log;

        private readonly List<TcpClient> _clients = [];
        private readonly Lock _clientsLock = new();
        private readonly SemaphoreSlim _sendLock = new(1, 1);

        private CancellationTokenSource? _cts;
        private Task? _runTask;
        private TcpListener? _listener;

        private readonly Lock _dupLock = new();
        private string? _lastCode;
        private long _lastCodeTick;

        // Lệnh cấu hình đang chờ đầu đọc trả lời (chỉ một lệnh tại một thời điểm)
        private readonly SemaphoreSlim _cmdLock = new(1, 1);
        private volatile PendingCommand? _pending;

        private sealed class PendingCommand(string expectCode)
        {
            public string ExpectCode { get; } = expectCode.ToUpperInvariant();
            public TaskCompletionSource<string> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public ReaderStatus Status { get; private set; } = ReaderStatus.Disconnected;
        public bool IsRunning => _cts != null;

        /// <summary>Bỏ qua mã giống mã trước đó nếu đến trong khoảng này (ms). 0 = tắt.</summary>
        public int DuplicateIgnoreMs { get; set; }

        /// <summary>
        /// Chuỗi đầu đọc gửi khi không đọc được mã (vd "NR", cấu hình "Read fail prompt").
        /// Chuỗi này không phải mã vạch nên bị bỏ qua.
        /// </summary>
        public string? NoReadText { get; set; }

        public int ConnectedCount
        {
            get { lock (_clientsLock) return _clients.Count; }
        }

        public void Start(ReaderConnectionMode mode, string ip, int port)
        {
            if (IsRunning)
                throw new InvalidOperationException("Đã kết nối, hãy ngắt kết nối trước.");

            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            _runTask = mode == ReaderConnectionMode.Client
                ? Task.Run(() => RunClientAsync(ip, port, ct))
                : Task.Run(() => RunServerAsync(port, ct));
        }

        public async Task StopAsync()
        {
            if (_cts == null) return;

            _cts.Cancel();
            _listener?.Stop();
            CloseAllClients();

            if (_runTask != null)
            {
                try { await _runTask; }
                catch (Exception) { /* đã log trong vòng chạy */ }
            }

            _cts.Dispose();
            _cts = null;
            _runTask = null;
            SetStatus(ReaderStatus.Disconnected, "Đã ngắt kết nối");
        }

        /// <summary>Gửi chuỗi xuống tất cả đầu đọc đang kết nối (vd: lệnh trigger). Trả về số đầu đọc đã gửi được.</summary>
        public Task<int> SendAsync(string text) => SendBytesAsync(Encoding.ASCII.GetBytes(text));

        /// <summary>Gửi dữ liệu thô xuống tất cả đầu đọc đang kết nối. Trả về số đầu đọc đã gửi được.</summary>
        public async Task<int> SendBytesAsync(byte[] bytes)
        {
            TcpClient[] targets;
            lock (_clientsLock) targets = [.. _clients];

            int sent = 0;
            await _sendLock.WaitAsync();
            try
            {
                foreach (var client in targets)
                {
                    try
                    {
                        await client.GetStream().WriteAsync(bytes);
                        sent++;
                    }
                    catch (Exception ex)
                    {
                        RaiseLog($"Gửi tới {EndpointOf(client)} thất bại: {ex.Message}");
                    }
                }
            }
            finally
            {
                _sendLock.Release();
            }
            return sent;
        }

        /// <summary>
        /// Gửi một khung lệnh cấu hình (xem <see cref="ShiYinProtocol"/>) tới đầu đọc đầu tiên đang kết nối
        /// và chờ chuỗi trả lời bắt đầu bằng <paramref name="expectCode"/> (6 ký tự hex của lệnh đầu tiên).
        /// Trả lời được tách khỏi luồng mã vạch nên không bị coi là mã quét.
        /// </summary>
        public async Task<string> SendCommandAsync(byte[] frame, string expectCode, int timeoutMs = 3000, CancellationToken ct = default)
        {
            TcpClient? target;
            lock (_clientsLock) target = _clients.FirstOrDefault();
            if (target == null)
                throw new InvalidOperationException("Chưa có đầu đọc nào đang kết nối.");

            await _cmdLock.WaitAsync(ct);
            var request = new PendingCommand(expectCode);
            try
            {
                _pending = request;
                await _sendLock.WaitAsync(ct);
                try { await target.GetStream().WriteAsync(frame, ct); }
                finally { _sendLock.Release(); }

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(timeoutMs);
                using (timeoutCts.Token.Register(() => request.Completion.TrySetCanceled()))
                {
                    try
                    {
                        return await request.Completion.Task;
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        throw new TimeoutException($"Đầu đọc không trả lời lệnh {expectCode} sau {timeoutMs / 1000.0:0.#} giây.");
                    }
                }
            }
            finally
            {
                _pending = null;
                _cmdLock.Release();
            }
        }

        /// <summary>Chuyển "\r", "\n", "\t" gõ trong ô cấu hình thành ký tự thật.</summary>
        public static string Unescape(string s) =>
            s.Replace("\\r", "\r").Replace("\\n", "\n").Replace("\\t", "\t");

        // ------------------------------------------------------------------
        // Chế độ Client: PC kết nối tới đầu đọc, tự kết nối lại khi mất kết nối
        // ------------------------------------------------------------------
        private async Task RunClientAsync(string ip, int port, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                SetStatus(ReaderStatus.Connecting, $"Đang kết nối tới {ip}:{port}...");
                var client = new TcpClient();
                try
                {
                    using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                    {
                        connectCts.CancelAfter(ConnectTimeout);
                        await client.ConnectAsync(ip, port, connectCts.Token);
                    }

                    ConfigureSocket(client);
                    AddClient(client);
                    SetStatus(ReaderStatus.Connected, $"Đã kết nối tới đầu đọc {ip}:{port}");

                    await ReadLoopAsync(client, ct);
                    if (!ct.IsCancellationRequested)
                        SetStatus(ReaderStatus.Error, $"Đầu đọc đã đóng kết nối. Thử lại sau {ReconnectDelay.TotalSeconds:0} giây...");
                }
                catch (Exception) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (OperationCanceledException)
                {
                    SetStatus(ReaderStatus.Error, $"Không phản hồi từ {ip}:{port} (timeout). Thử lại sau {ReconnectDelay.TotalSeconds:0} giây...");
                }
                catch (Exception ex)
                {
                    SetStatus(ReaderStatus.Error, $"Lỗi kết nối {ip}:{port}: {ex.Message.TrimEnd('.')}. Thử lại sau {ReconnectDelay.TotalSeconds:0} giây...");
                }
                finally
                {
                    RemoveClient(client);
                    client.Dispose();
                }

                try { await Task.Delay(ReconnectDelay, ct); }
                catch (OperationCanceledException) { break; }
            }
        }

        // ------------------------------------------------------------------
        // Chế độ Server: PC lắng nghe, đầu đọc (TCP Client) kết nối vào
        // ------------------------------------------------------------------
        private async Task RunServerAsync(int port, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    _listener = new TcpListener(IPAddress.Any, port);
                    _listener.Start();
                    SetStatus(ReaderStatus.Listening, $"Đang chờ đầu đọc kết nối vào cổng {port}...");

                    while (!ct.IsCancellationRequested)
                    {
                        var client = await _listener.AcceptTcpClientAsync(ct);
                        _ = HandleServerClientAsync(client, port, ct);
                    }
                }
                catch (Exception) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    SetStatus(ReaderStatus.Error, $"Không mở được cổng {port}: {ex.Message.TrimEnd('.')}. Thử lại sau {ReconnectDelay.TotalSeconds:0} giây...");
                }
                finally
                {
                    _listener?.Stop();
                    _listener = null;
                }

                try { await Task.Delay(ReconnectDelay, ct); }
                catch (OperationCanceledException) { break; }
            }
        }

        private async Task HandleServerClientAsync(TcpClient client, int port, CancellationToken ct)
        {
            var endpoint = EndpointOf(client);
            try
            {
                ConfigureSocket(client);
                AddClient(client);
                SetStatus(ReaderStatus.Connected, $"Đầu đọc {endpoint} đã kết nối (cổng {port})");
                await ReadLoopAsync(client, ct);
                RaiseLog($"Đầu đọc {endpoint} đã ngắt kết nối");
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                RaiseLog($"Mất kết nối với {endpoint}: {ex.Message}");
            }
            catch (Exception)
            {
                // Đang dừng service
            }
            finally
            {
                RemoveClient(client);
                client.Dispose();
                if (!ct.IsCancellationRequested && ConnectedCount == 0)
                    SetStatus(ReaderStatus.Listening, $"Đang chờ đầu đọc kết nối vào cổng {port}...");
            }
        }

        // ------------------------------------------------------------------
        // Đọc & tách khung dữ liệu
        // ------------------------------------------------------------------
        private async Task ReadLoopAsync(TcpClient client, CancellationToken ct)
        {
            var source = EndpointOf(client);
            var stream = client.GetStream();
            var buffer = new byte[4096];
            var pending = new List<byte>();
            Task<int>? readTask = null;

            while (!ct.IsCancellationRequested)
            {
                readTask ??= stream.ReadAsync(buffer.AsMemory(), ct).AsTask();

                // Còn dữ liệu chưa có ký tự kết thúc: chờ thêm một chút, nếu im lặng thì chốt thành 1 mã.
                // Đang chờ trả lời lệnh cấu hình thì không chốt vội (trả lời có thể đến thành nhiều gói).
                if (pending.Count > 0)
                {
                    var finished = await Task.WhenAny(readTask, Task.Delay(FrameIdleTimeout, CancellationToken.None));
                    if (finished != readTask)
                    {
                        if (_pending == null)
                            FlushFrame(pending, source);
                        continue;
                    }
                }

                int n = await readTask;
                readTask = null;
                if (n == 0)
                    break; // Phía đầu đọc đóng kết nối

                foreach (var b in buffer.AsSpan(0, n))
                {
                    // CR, LF, STX, ETX, NUL được coi là ranh giới giữa các mã
                    if (b is 0x0D or 0x0A or 0x02 or 0x03 or 0x00)
                    {
                        FlushFrame(pending, source);
                        continue;
                    }

                    pending.Add(b);

                    // Trả lời lệnh cấu hình kết thúc bằng <ACK|NAK|ENQ> rồi "." (hoặc "!" với lệnh ghi tạm)
                    if (b is (byte)'.' or (byte)'!' && pending.Count >= 2 && ShiYinProtocol.IsStatusByte(pending[^2]))
                    {
                        HandleReply(pending, source);
                        continue;
                    }

                    if (pending.Count >= MaxFrameLength)
                        FlushFrame(pending, source);
                }
            }

            // Thoát do đang dừng: lệnh đọc dở sẽ lỗi khi socket đóng, tránh UnobservedTaskException
            readTask?.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            FlushFrame(pending, source);
        }

        private void FlushFrame(List<byte> pending, string source)
        {
            if (pending.Count == 0) return;

            if (ShiYinProtocol.LooksLikeReply(CollectionsMarshal.AsSpan(pending)))
            {
                HandleReply(pending, source);
                return;
            }

            var code = Encoding.UTF8.GetString(CollectionsMarshal.AsSpan(pending)).Trim();
            pending.Clear();
            EmitBarcode(code, source);
        }

        private void EmitBarcode(string code, string source)
        {
            if (code.Length == 0) return;

            if (!string.IsNullOrEmpty(NoReadText) && code == NoReadText)
            {
                RaiseLog("Đầu đọc báo không đọc được mã");
                return;
            }

            if (IsDuplicate(code))
            {
                RaiseLog($"Bỏ qua mã lặp lại: {code}");
                return;
            }

            BarcodeScanned?.Invoke(this, new BarcodeScannedEventArgs(code, source));
        }

        /// <summary>
        /// Chuỗi trong <paramref name="pending"/> là trả lời lệnh cấu hình: giao cho lệnh đang chờ (nếu khớp mã),
        /// phần mã vạch (nếu có) dính trước trả lời vẫn được tách ra.
        /// </summary>
        private void HandleReply(List<byte> pending, string source)
        {
            var text = Encoding.Latin1.GetString(CollectionsMarshal.AsSpan(pending));
            pending.Clear();

            var request = _pending;
            if (request != null)
            {
                int idx = text.IndexOf(request.ExpectCode, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    if (idx > 0)
                        EmitBarcode(text[..idx].Trim(), source);
                    request.Completion.TrySetResult(text[idx..]);
                    return;
                }
            }

            RaiseLog($"Bỏ qua phản hồi cấu hình không mong đợi: {Printable(text)}");
        }

        private static string Printable(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
                sb.Append(c < ' ' ? $"<{(int)c:X2}>" : c);
            return sb.Length > 200 ? sb.ToString(0, 200) + "…" : sb.ToString();
        }

        private bool IsDuplicate(string code)
        {
            if (DuplicateIgnoreMs <= 0) return false;

            lock (_dupLock)
            {
                var now = Environment.TickCount64;
                bool dup = code == _lastCode && now - _lastCodeTick < DuplicateIgnoreMs;
                _lastCode = code;
                _lastCodeTick = now;
                return dup;
            }
        }

        // ------------------------------------------------------------------
        // Tiện ích
        // ------------------------------------------------------------------

        /// <summary>
        /// Bật TCP keep-alive để phát hiện rút cáp / mất điện đầu đọc trong khoảng ~10 giây,
        /// nếu không kết nối "chết" sẽ vẫn hiện là đang kết nối.
        /// </summary>
        private void ConfigureSocket(TcpClient client)
        {
            client.NoDelay = true;
            var s = client.Client;
            try
            {
                s.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                s.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 5);
                s.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 2);
                s.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 3);
            }
            catch (SocketException ex)
            {
                RaiseLog($"Không bật được keep-alive: {ex.Message}");
            }
        }

        private void AddClient(TcpClient client)
        {
            lock (_clientsLock) _clients.Add(client);
        }

        private void RemoveClient(TcpClient client)
        {
            lock (_clientsLock) _clients.Remove(client);
        }

        private void CloseAllClients()
        {
            TcpClient[] all;
            lock (_clientsLock) all = [.. _clients];
            foreach (var c in all)
                c.Close(); // làm ReadAsync đang chờ kết thúc ngay
        }

        private static string EndpointOf(TcpClient client)
        {
            try
            {
                return client.Client.RemoteEndPoint switch
                {
                    // Socket dual-mode trả về dạng [::ffff:192.168.1.100], đổi về IPv4 cho dễ đọc
                    IPEndPoint ep when ep.Address.IsIPv4MappedToIPv6 => $"{ep.Address.MapToIPv4()}:{ep.Port}",
                    { } ep => ep.ToString() ?? "?",
                    null => "?",
                };
            }
            catch (ObjectDisposedException) { return "?"; }
        }

        private void SetStatus(ReaderStatus status, string message)
        {
            Status = status;
            StatusChanged?.Invoke(this, new ReaderStatusEventArgs(status, message));
            RaiseLog(message);
        }

        private void RaiseLog(string message) => Log?.Invoke(this, message);

        public void Dispose()
        {
            _cts?.Cancel();
            _listener?.Stop();
            CloseAllClients();
            _cts?.Dispose();
            _sendLock.Dispose();
            _cmdLock.Dispose();
        }
    }
}
