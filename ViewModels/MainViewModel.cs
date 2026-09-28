using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using CheckBarcodeSieuThi.Data;
using CheckBarcodeSieuThi.Models;
using CheckBarcodeSieuThi.Services;

namespace CheckBarcodeSieuThi.ViewModels
{
    /// <summary>
    /// Khung chính: menu trái, thanh tiêu đề, điều hướng trang và phân phối mã quét từ đầu đọc.
    /// </summary>
    public class MainViewModel : ViewModelBase, IDisposable
    {
        private readonly BarcodeReaderService _reader = new();
        private readonly InvoiceRepository _invoiceRepo;
        private readonly DispatcherTimer _clock;

        public AppSettings AppSettings { get; }

        public PosViewModel Pos { get; }
        public ProductsViewModel Products { get; }
        public InvoicesViewModel Invoices { get; }
        public SettingsViewModel Settings { get; }

        public IReadOnlyList<PageViewModel> Pages { get; }

        /// <summary>View hiển thị hóa đơn: (hóa đơn, true nếu vừa thanh toán xong).</summary>
        public event Action<Invoice, bool>? ShowReceiptRequested;

        public ICommand OpenSettingsCommand { get; }

        public MainViewModel()
        {
            Directory.CreateDirectory(AppSettings.DataFolder);
            AppSettings = AppSettings.Load();

            var db = new Database(Path.Combine(AppSettings.DataFolder, "products.db"));
            var productRepo = new ProductRepository(db);
            _invoiceRepo = new InvoiceRepository(db);

            Pos = new PosViewModel(productRepo, _invoiceRepo);
            Products = new ProductsViewModel(productRepo);
            Invoices = new InvoicesViewModel(_invoiceRepo, AppSettings);
            Settings = new SettingsViewModel(AppSettings, _reader);
            Pages = [Pos, Invoices, Products, Settings];

            Pos.CheckoutCompleted += OnCheckoutCompleted;
            Pos.AddProductRequested += barcode =>
            {
                CurrentPage = Products;
                Products.PrepareNewFromPos(barcode);
            };
            Products.PendingPosProductAdded += product =>
            {
                CurrentPage = Pos;
                Pos.AddBarcode(product.Barcode);
            };

            _reader.BarcodeScanned += (_, e) =>
                Application.Current?.Dispatcher.BeginInvoke(() => RouteBarcode(e.Barcode, e.Source));

            OpenSettingsCommand = new RelayCommand(() => CurrentPage = Settings);

            _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clock.Tick += (_, _) => OnPropertyChanged(nameof(Now));
            _clock.Start();

            _currentPage = Pos;
            RefreshTodaySummary();
            Settings.AddLog($"Dữ liệu lưu tại: {AppSettings.DataFolder}");
            Settings.StartIfAutoConnect();
        }

        private PageViewModel _currentPage;
        public PageViewModel CurrentPage
        {
            get => _currentPage;
            set
            {
                if (value == null || !SetField(ref _currentPage, value)) return;
                value.OnActivated();
            }
        }

        public DateTime Now => DateTime.Now;

        private string _todaySummary = "";
        public string TodaySummary { get => _todaySummary; private set => SetField(ref _todaySummary, value); }

        private void RefreshTodaySummary()
        {
            try
            {
                var s = _invoiceRepo.GetSummary(DateTime.Today, DateTime.Today);
                TodaySummary = $"Hôm nay: {s.InvoiceCount} hóa đơn · {Helpers.Money.FormatVnd(s.Revenue)}";
            }
            catch (Exception)
            {
                TodaySummary = "";
            }
        }

        /// <summary>
        /// Mã từ đầu đọc: đang ở trang Sản phẩm thì dùng cho form sản phẩm,
        /// đang ở trang Cài đặt thì chỉ ghi log (để còn chỉnh được đầu đọc khi nó đọc liên tục),
        /// còn lại coi là bán hàng (tự chuyển sang trang Bán hàng).
        /// </summary>
        private void RouteBarcode(string code, string source)
        {
            Settings.AddLog($"Nhận mã [{source}]: {code}");

            if (CurrentPage == Products)
            {
                Products.HandleScannedBarcode(code);
                return;
            }

            if (CurrentPage == Settings)
                return;

            CurrentPage = Pos;
            Pos.AddBarcode(code);
        }

        private void OnCheckoutCompleted(Invoice invoice)
        {
            RefreshTodaySummary();
            ShowReceiptRequested?.Invoke(invoice, true);
        }

        public void Dispose()
        {
            _clock.Stop();
            Settings.SaveOnExit();
            Settings.PauseReaderOnExit();
            _reader.Dispose();
        }
    }
}
