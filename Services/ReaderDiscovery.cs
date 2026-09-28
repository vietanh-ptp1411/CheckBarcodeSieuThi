using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace CheckBarcodeSieuThi.Services
{
    /// <summary>Một đầu đọc tìm thấy trong mạng LAN qua UDP broadcast.</summary>
    public sealed record DiscoveredReader(
        string Ip,
        int Port,
        string Model,
        string SerialNumber,
        string Mac,
        string DeviceName,
        string Mask,
        string Gateway,
        bool Dhcp,
        string LocalIp)
    {
        public string Display => $"{Model}  {Ip}:{Port}   SN {SerialNumber}";
    }

    /// <summary>
    /// Tìm đầu đọc Shijie / Superlead trong mạng: gửi broadcast tới cổng 6000 từ mỗi card mạng,
    /// đầu đọc trả về JSON chứa IP, cổng dữ liệu, model, số serial.
    /// </summary>
    public static class ReaderDiscovery
    {
        public static async Task<List<DiscoveredReader>> SearchAsync(TimeSpan timeout, Action<string>? log = null, CancellationToken ct = default)
        {
            var localIps = GetLocalIPv4();
            log?.Invoke(localIps.Count == 0
                ? "Không tìm thấy card mạng nào đang hoạt động"
                : $"Đang tìm đầu đọc qua {string.Join(", ", localIps.Select(x => x.Address))}...");

            var tasks = localIps.Select(x => SearchOnInterfaceAsync(x.Address, x.Mask, timeout, log, ct)).ToList();
            var results = await Task.WhenAll(tasks);

            return results.SelectMany(r => r)
                .GroupBy(r => (r.Ip, r.SerialNumber))
                .Select(g => g.First())
                .OrderBy(r => r.Ip)
                .ToList();
        }

        private static async Task<List<DiscoveredReader>> SearchOnInterfaceAsync(IPAddress local, IPAddress mask, TimeSpan timeout, Action<string>? log, CancellationToken ct)
        {
            var found = new List<DiscoveredReader>();
            try
            {
                using var udp = new UdpClient(new IPEndPoint(local, 0)) { EnableBroadcast = true };

                // Gửi cả broadcast toàn mạng lẫn broadcast của subnet để đi qua được các card mạng/ảo hóa khác nhau
                var targets = new HashSet<IPAddress> { IPAddress.Broadcast, DirectedBroadcast(local, mask) };
                foreach (var target in targets)
                {
                    try { await udp.SendAsync(ShiYinProtocol.DiscoveryRequest, new IPEndPoint(target, ShiYinProtocol.DiscoveryPort), ct); }
                    catch (SocketException ex) { log?.Invoke($"Không gửi được broadcast tới {target}: {ex.Message}"); }
                }

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(timeout);
                while (!cts.IsCancellationRequested)
                {
                    UdpReceiveResult packet;
                    try { packet = await udp.ReceiveAsync(cts.Token); }
                    catch (OperationCanceledException) { break; }

                    var reader = Parse(packet.Buffer, packet.RemoteEndPoint.Address, local);
                    if (reader != null && !found.Any(f => f.Ip == reader.Ip && f.SerialNumber == reader.SerialNumber))
                    {
                        found.Add(reader);
                        log?.Invoke($"Tìm thấy {reader.Model} tại {reader.Ip}:{reader.Port} (SN {reader.SerialNumber})");
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log?.Invoke($"Lỗi tìm thiết bị trên {local}: {ex.Message}");
            }
            return found;
        }

        private static DiscoveredReader? Parse(byte[] data, IPAddress from, IPAddress local)
        {
            try
            {
                var text = Encoding.UTF8.GetString(data).Trim('\0', ' ', '\r', '\n');
                if (!text.Contains("ShiYinJson", StringComparison.Ordinal)) return null;

                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;
                string Get(string name) => root.TryGetProperty(name, out var p) ? p.GetString() ?? "" : "";

                var ip = Get("IpAddress");
                if (ip.Length == 0) ip = from.ToString();
                _ = int.TryParse(Get("PortNumber"), out var port);
                if (port == 0) port = 18204;

                return new DiscoveredReader(
                    ip, port, Get("TypeNames"), Get("SN"), Get("MacAddress"), Get("DeviceName"),
                    Get("Mask"), Get("Gateway"), Get("IpConfig") == "1", local.ToString());
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static List<(IPAddress Address, IPAddress Mask)> GetLocalIPv4()
        {
            var list = new List<(IPAddress, IPAddress)>();
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var addr in nic.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    if (IPAddress.IsLoopback(addr.Address)) continue;
                    list.Add((addr.Address, addr.IPv4Mask ?? IPAddress.Parse("255.255.255.0")));
                }
            }
            return list;
        }

        private static IPAddress DirectedBroadcast(IPAddress ip, IPAddress mask)
        {
            var a = ip.GetAddressBytes();
            var m = mask.GetAddressBytes();
            var b = new byte[4];
            for (int i = 0; i < 4; i++)
                b[i] = (byte)(a[i] | ~m[i]);
            return new IPAddress(b);
        }
    }
}
