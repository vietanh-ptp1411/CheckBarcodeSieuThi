using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CheckBarcodeSieuThi.Services
{
    /// <summary>
    /// Client: phần mềm (PC) chủ động kết nối tới đầu đọc đang chạy TCP Server.
    /// Server: phần mềm mở cổng lắng nghe, đầu đọc (cấu hình TCP Client) kết nối vào.
    /// </summary>
    public enum ReaderConnectionMode
    {
        Client,
        Server,
    }

    /// <summary>
    /// Cấu hình lưu tại %LocalAppData%\CheckBarcodeSieuThi\settings.json
    /// </summary>
    public class AppSettings
    {
        public ReaderConnectionMode Mode { get; set; } = ReaderConnectionMode.Client;

        /// <summary>IP của đầu đọc (chỉ dùng ở chế độ Client).</summary>
        public string ReaderIp { get; set; } = "192.168.1.100";

        /// <summary>Cổng TCP: ở chế độ Client là cổng của đầu đọc, ở chế độ Server là cổng PC lắng nghe.</summary>
        public int Port { get; set; } = 2001;

        /// <summary>
        /// Lệnh trigger mềm gửi xuống đầu đọc (tùy cấu hình trong phần mềm của đầu đọc).
        /// Hỗ trợ ký tự thoát \r \n \t. Để trống nếu dùng trigger cứng / đọc liên tục.
        /// </summary>
        public string TriggerCommand { get; set; } = "";

        /// <summary>
        /// Bỏ qua cùng một mã nếu đầu đọc gửi lặp lại trong khoảng thời gian này (ms). 0 = tắt.
        /// Không đặt quá lớn: thu ngân quét 2 món giống nhau liên tiếp sẽ bị tính thiếu.
        /// </summary>
        public int DuplicateIgnoreMs { get; set; } = 300;

        /// <summary>Tự kết nối khi mở phần mềm.</summary>
        public bool AutoConnect { get; set; }

        // ----- Thông tin in trên hóa đơn -----
        public string StoreName { get; set; } = "SIÊU THỊ MINI";
        public string StoreAddress { get; set; } = "123 Đường ABC, Phường X, TP. Y";
        public string StorePhone { get; set; } = "0900 000 000";
        public string ReceiptFooter { get; set; } = "Cảm ơn quý khách và hẹn gặp lại!";

        /// <summary>In hóa đơn ra máy in mặc định ngay sau khi thanh toán (không hỏi).</summary>
        public bool AutoPrint { get; set; }

        public static string DataFolder { get; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CheckBarcodeSieuThi");

        private static string FilePath => Path.Combine(DataFolder, "settings.json");

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new AppSettings();
            }
            catch (Exception)
            {
                // File hỏng: dùng cấu hình mặc định, lần lưu sau sẽ ghi đè.
            }
            return new AppSettings();
        }

        public void Save()
        {
            Directory.CreateDirectory(DataFolder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
    }
}
