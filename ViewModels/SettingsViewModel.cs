using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using CheckBarcodeSieuThi.Services;

namespace CheckBarcodeSieuThi.ViewModels
{
    /// <summary>
    /// Cài đặt: kết nối đầu đọc barcode qua Ethernet và thông tin cửa hàng in trên hóa đơn.
    /// </summary>
    public class SettingsViewModel : PageViewModel
    {
        private const int MaxLogLines = 300;

        private readonly AppSettings _settings;
        private readonly BarcodeReaderService _reader;

        public override string Title => "Cài đặt";
        public override string Subtitle => "Kết nối đầu đọc mã vạch và thông tin in hóa đơn";
        public override string Icon => "";

        public ObservableCollection<string> Logs { get; } = [];

        public IReadOnlyList<KeyValuePair<ReaderConnectionMode, string>> ModeOptions { get; } =
        [
            new(ReaderConnectionMode.Client, "TCP Client (PC kết nối tới đầu đọc)"),
            new(ReaderConnectionMode.Server, "TCP Server (đầu đọc kết nối tới PC)"),
        ];

        public ICommand ToggleConnectCommand { get; }
        public ICommand SendTriggerCommand { get; }
        public ICommand ClearLogCommand { get; }
        public ICommand SaveStoreCommand { get; }

        public SettingsViewModel(AppSettings settings, BarcodeReaderService reader)
        {
            _settings = settings;
            _reader = reader;

            _mode = settings.Mode;
            _readerIp = settings.ReaderIp;
            _portText = settings.Port.ToString();
            _triggerText = settings.TriggerCommand;
            _autoConnect = settings.AutoConnect;
            _duplicateText = settings.DuplicateIgnoreMs.ToString();

            _storeName = settings.StoreName;
            _storeAddress = settings.StoreAddress;
            _storePhone = settings.StorePhone;
            _receiptFooter = settings.ReceiptFooter;
            _autoPrint = settings.AutoPrint;

            ToggleConnectCommand = new RelayCommand(async () => await ToggleConnectAsync());
            SendTriggerCommand = new RelayCommand(async () => await SendTriggerAsync(), () => IsReaderRunning && !string.IsNullOrEmpty(TriggerText));
            ClearLogCommand = new RelayCommand(Logs.Clear);
            SaveStoreCommand = new RelayCommand(SaveStore);

            _reader.StatusChanged += (_, e) => OnUi(() => { ReaderStatus = e.Status; StatusText = e.Message; });
            _reader.Log += (_, msg) => OnUi(() => AddLog(msg));
        }

        /// <summary>Gọi khi mở phần mềm: tự kết nối nếu được bật.</summary>
        public void StartIfAutoConnect()
        {
            if (_settings.AutoConnect)
                _ = ToggleConnectAsync();
        }

        // ==================================================================
        // Kết nối đầu đọc
        // ==================================================================

        private ReaderConnectionMode _mode;
        public ReaderConnectionMode Mode
        {
            get => _mode;
            set { if (SetField(ref _mode, value)) OnPropertyChanged(nameof(IsClientMode)); }
        }

        public bool IsClientMode => Mode == ReaderConnectionMode.Client;

        private string _readerIp;
        public string ReaderIp { get => _readerIp; set => SetField(ref _readerIp, value); }

        private string _portText;
        public string PortText { get => _portText; set => SetField(ref _portText, value); }

        private string _triggerText;
        public string TriggerText { get => _triggerText; set => SetField(ref _triggerText, value); }

        private string _duplicateText;
        /// <summary>Bỏ qua mã trùng đến liên tiếp trong khoảng này (ms). Áp dụng ngay, không cần kết nối lại.</summary>
        public string DuplicateText
        {
            get => _duplicateText;
            set
            {
                if (!SetField(ref _duplicateText, value)) return;
                OnPropertyChanged(nameof(IsDuplicateValid));
                if (!int.TryParse(value.Trim(), out var ms) || ms is < 0 or > 10000) return;
                _settings.DuplicateIgnoreMs = ms;
                _reader.DuplicateIgnoreMs = ms;
                TrySave();
            }
        }

        public bool IsDuplicateValid => int.TryParse(DuplicateText.Trim(), out var ms) && ms is >= 0 and <= 10000;

        private bool _autoConnect;
        public bool AutoConnect
        {
            get => _autoConnect;
            set
            {
                if (!SetField(ref _autoConnect, value)) return;
                _settings.AutoConnect = value;
                TrySave();
            }
        }

        private bool _isReaderRunning;
        public bool IsReaderRunning
        {
            get => _isReaderRunning;
            private set
            {
                if (!SetField(ref _isReaderRunning, value)) return;
                OnPropertyChanged(nameof(ConnectButtonText));
                OnPropertyChanged(nameof(CanEditConnection));
            }
        }

        public bool CanEditConnection => !IsReaderRunning;
        public string ConnectButtonText => IsReaderRunning ? "Ngắt kết nối" : "Kết nối";

        private ReaderStatus _readerStatus = ReaderStatus.Disconnected;
        public ReaderStatus ReaderStatus
        {
            get => _readerStatus;
            set { if (SetField(ref _readerStatus, value)) OnPropertyChanged(nameof(ReaderShortStatus)); }
        }

        /// <summary>Trạng thái ngắn hiển thị trên thanh tiêu đề.</summary>
        public string ReaderShortStatus => ReaderStatus switch
        {
            ReaderStatus.Connected => "Đầu đọc: đã kết nối",
            ReaderStatus.Connecting => "Đầu đọc: đang kết nối...",
            ReaderStatus.Listening => "Đầu đọc: chờ kết nối",
            ReaderStatus.Error => "Đầu đọc: mất kết nối",
            _ => "Đầu đọc: chưa kết nối",
        };

        private string _statusText = "Chưa kết nối";
        public string StatusText { get => _statusText; set => SetField(ref _statusText, value); }

        private async Task ToggleConnectAsync()
        {
            if (_reader.IsRunning)
            {
                await _reader.StopAsync();
                IsReaderRunning = false;
                return;
            }

            if (!int.TryParse(PortText.Trim(), out var port) || port is < 1 or > 65535)
            {
                ShowError("Cổng (port) phải là số từ 1 đến 65535.");
                return;
            }
            var ip = ReaderIp.Trim();
            if (Mode == ReaderConnectionMode.Client && ip.Length == 0)
            {
                ShowError("Chưa nhập địa chỉ IP của đầu đọc.");
                return;
            }

            _settings.Mode = Mode;
            _settings.ReaderIp = ip;
            _settings.Port = port;
            _settings.TriggerCommand = TriggerText;
            TrySave();

            _reader.DuplicateIgnoreMs = _settings.DuplicateIgnoreMs;
            _reader.Start(Mode, ip, port);
            IsReaderRunning = true;
        }

        private async Task SendTriggerAsync()
        {
            _settings.TriggerCommand = TriggerText;
            TrySave();

            var sent = await _reader.SendAsync(BarcodeReaderService.Unescape(TriggerText));
            AddLog(sent > 0
                ? $"Đã gửi lệnh trigger \"{TriggerText}\" tới {sent} đầu đọc"
                : "Chưa có đầu đọc nào kết nối để gửi trigger");
        }

        public void AddLog(string message)
        {
            Logs.Insert(0, $"{DateTime.Now:HH:mm:ss}  {message}");
            while (Logs.Count > MaxLogLines)
                Logs.RemoveAt(Logs.Count - 1);
        }

        // ==================================================================
        // Thông tin cửa hàng
        // ==================================================================

        private string _storeName;
        public string StoreName { get => _storeName; set => SetField(ref _storeName, value); }

        private string _storeAddress;
        public string StoreAddress { get => _storeAddress; set => SetField(ref _storeAddress, value); }

        private string _storePhone;
        public string StorePhone { get => _storePhone; set => SetField(ref _storePhone, value); }

        private string _receiptFooter;
        public string ReceiptFooter { get => _receiptFooter; set => SetField(ref _receiptFooter, value); }

        private bool _autoPrint;
        public bool AutoPrint { get => _autoPrint; set => SetField(ref _autoPrint, value); }

        private string _storeMessage = "";
        public string StoreMessage { get => _storeMessage; set => SetField(ref _storeMessage, value); }

        /// <summary>Tên cửa hàng đã lưu (hiện ở góc trên menu).</summary>
        public string SavedStoreName => _settings.StoreName;

        private void SaveStore()
        {
            if (string.IsNullOrWhiteSpace(StoreName))
            {
                StoreMessage = "Tên cửa hàng không được để trống.";
                return;
            }

            _settings.StoreName = StoreName.Trim();
            _settings.StoreAddress = StoreAddress.Trim();
            _settings.StorePhone = StorePhone.Trim();
            _settings.ReceiptFooter = ReceiptFooter.Trim();
            _settings.AutoPrint = AutoPrint;
            StoreMessage = TrySave() ? $"Đã lưu lúc {DateTime.Now:HH:mm:ss}" : "Không lưu được cấu hình, xem nhật ký kết nối.";
            OnPropertyChanged(nameof(SavedStoreName));
        }

        // ==================================================================

        public void SaveOnExit()
        {
            _settings.TriggerCommand = TriggerText;
            TrySave();
        }

        private bool TrySave()
        {
            try
            {
                _settings.Save();
                return true;
            }
            catch (Exception ex)
            {
                AddLog($"Không lưu được cấu hình: {ex.Message}");
                return false;
            }
        }

        private static void ShowError(string message) =>
            MessageBox.Show(message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);

        private static void OnUi(Action action) =>
            Application.Current?.Dispatcher.BeginInvoke(action);
    }
}
