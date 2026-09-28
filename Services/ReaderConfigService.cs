namespace CheckBarcodeSieuThi.Services
{
    public sealed record ReaderDeviceInfo(string Model, string Firmware, string SerialNumber);

    /// <summary>Kết quả ghi cấu hình: lệnh nào được chấp nhận, lệnh nào bị từ chối.</summary>
    public sealed class SaveResult
    {
        public List<CommandReply> Replies { get; } = [];
        public IEnumerable<CommandReply> Accepted => Replies.Where(r => r.IsAck && r.Code != ShiYinProtocol.SaveToFlash);
        public IEnumerable<CommandReply> Rejected => Replies.Where(r => !r.IsAck);
        public bool Persisted => Replies.Any(r => r.Code == ShiYinProtocol.SaveToFlash && r.IsAck);
    }

    /// <summary>
    /// Đọc / ghi tham số của đầu đọc qua kết nối TCP đang dùng để nhận mã vạch,
    /// theo giao thức của phần mềm ShiJieConfig (xem <see cref="ShiYinProtocol"/>).
    /// </summary>
    public sealed class ReaderConfigService(BarcodeReaderService reader)
    {
        /// <summary>Số lệnh gộp trong một khung gửi đi. Đầu đọc trả lời tất cả trong một chuỗi.</summary>
        private const int BatchSize = 40;

        public async Task<ReaderDeviceInfo> ReadDeviceInfoAsync(CancellationToken ct = default)
        {
            var fw = ShiYinProtocol.ParseReplies(await reader.SendCommandAsync(ShiYinProtocol.MenuFrame("0D1302?"), "0D1302", 3000, ct));
            var model = ShiYinProtocol.ParseReplies(await reader.SendCommandAsync(ShiYinProtocol.ProductFrame("0F0100?"), "0F0100", 3000, ct));
            var sn = ShiYinProtocol.ParseReplies(await reader.SendCommandAsync(ShiYinProtocol.ProductFrame("0F0800?"), "0F0800", 3000, ct));

            return new ReaderDeviceInfo(
                model.FirstOrDefault()?.Value ?? "",
                fw.FirstOrDefault()?.Value.Trim() ?? "",
                sn.FirstOrDefault()?.Value ?? "");
        }

        /// <summary>Đọc giá trị hiện tại của các tham số. Tham số đầu đọc không hỗ trợ sẽ không có trong kết quả.</summary>
        public Task<Dictionary<string, string>> ReadValuesAsync(IEnumerable<string> codes, CancellationToken ct = default) =>
            QueryAsync(codes, "?", ct);

        /// <summary>Đọc khoảng giá trị cho phép ("min-max" hoặc "a|b|c") của các tham số.</summary>
        public Task<Dictionary<string, string>> ReadRangesAsync(IEnumerable<string> codes, CancellationToken ct = default) =>
            QueryAsync(codes, "*", ct);

        private async Task<Dictionary<string, string>> QueryAsync(IEnumerable<string> codes, string suffix, CancellationToken ct)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var list = codes.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            for (int i = 0; i < list.Count; i += BatchSize)
            {
                var batch = list.Skip(i).Take(BatchSize).ToList();
                var frame = ShiYinProtocol.MenuFrame(batch.Select(c => c + suffix));
                var raw = await reader.SendCommandAsync(frame, batch[0], 4000, ct);
                foreach (var reply in ShiYinProtocol.ParseReplies(raw))
                {
                    if (reply.IsAck)
                        result[reply.Code] = reply.Value;
                }
            }
            return result;
        }

        /// <summary>
        /// Ghi các lệnh (mã 6 hex + giá trị) rồi lưu vào bộ nhớ đầu đọc.
        /// </summary>
        public async Task<SaveResult> SaveAsync(IReadOnlyList<string> commands, CancellationToken ct = default)
        {
            var result = new SaveResult();
            if (commands.Count == 0) return result;

            for (int i = 0; i < commands.Count; i += BatchSize)
            {
                var batch = commands.Skip(i).Take(BatchSize).ToList();
                bool last = i + BatchSize >= commands.Count;
                if (last) batch.Add(ShiYinProtocol.SaveToFlash);

                var raw = await reader.SendCommandAsync(ShiYinProtocol.MenuFrame(batch), batch[0][..6], 6000, ct);
                result.Replies.AddRange(ShiYinProtocol.ParseReplies(raw));
            }
            return result;
        }

        /// <summary>Áp dụng tạm (không lưu, mất khi đầu đọc khởi động lại) để thử nhanh.</summary>
        public async Task<List<CommandReply>> ApplyTemporaryAsync(IReadOnlyList<string> commands, CancellationToken ct = default)
        {
            if (commands.Count == 0) return [];
            var raw = await reader.SendCommandAsync(ShiYinProtocol.MenuFrame(commands, temporary: true), commands[0][..6], 4000, ct);
            return ShiYinProtocol.ParseReplies(raw);
        }

        /// <summary>Khôi phục cấu hình gốc. Đầu đọc sẽ khởi động lại nên có thể không nhận được trả lời.</summary>
        public async Task RestoreFactoryAsync(CancellationToken ct = default)
        {
            var frame = ShiYinProtocol.MenuFrame([ShiYinProtocol.RestoreFactory, ShiYinProtocol.SaveToFlash]);
            try
            {
                await reader.SendCommandAsync(frame, ShiYinProtocol.RestoreFactory, 5000, ct);
            }
            catch (TimeoutException)
            {
                // Đầu đọc khởi động lại ngay sau khi nhận lệnh
            }
        }

        public Task<int> TriggerAsync() => reader.SendBytesAsync(ShiYinProtocol.TriggerFrame);
        public Task<int> ReleaseAsync() => reader.SendBytesAsync(ShiYinProtocol.ReleaseFrame);
    }
}
