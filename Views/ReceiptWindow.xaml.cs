using System.Windows;
using System.Windows.Input;
using CheckBarcodeSieuThi.Models;
using CheckBarcodeSieuThi.Services;
using CheckBarcodeSieuThi.ViewModels;

namespace CheckBarcodeSieuThi.Views
{
    /// <summary>
    /// Xem trước và in hóa đơn. Sau khi thanh toán còn hiện tiền thừa trả khách.
    /// Enter / Esc để đóng nhanh và bán đơn tiếp theo.
    /// </summary>
    public partial class ReceiptWindow : Window
    {
        private readonly AppSettings _settings;

        public Invoice Invoice { get; }
        public bool IsNewSale { get; }
        public bool IsCash => Invoice.PaymentMethod == PaymentMethod.Cash;

        public ReceiptWindow(Invoice invoice, AppSettings settings, bool isNewSale)
        {
            InitializeComponent();
            Invoice = invoice;
            IsNewSale = isNewSale;
            _settings = settings;

            Title = $"Hóa đơn {invoice.Number}";
            Height = Math.Min(Height, SystemParameters.WorkArea.Height - 40); // vừa màn hình nhỏ (1366x768)
            Viewer.Document = ReceiptBuilder.Build(invoice, settings);
            DataContext = this;

            InputBindings.Add(new KeyBinding(new RelayCommand(Print), Key.P, ModifierKeys.Control));
            InputBindings.Add(new KeyBinding(new RelayCommand(Close), Key.Enter, ModifierKeys.None));

            Loaded += (_, _) =>
            {
                if (IsNewSale && _settings.AutoPrint)
                    TryPrint(showDialog: false);
            };
        }

        private void Print_Click(object sender, RoutedEventArgs e) => Print();

        private void Print() => TryPrint(showDialog: true);

        private void TryPrint(bool showDialog)
        {
            try
            {
                if (ReceiptBuilder.Print(Invoice, _settings, showDialog))
                    StatusText.Text = $"Đã gửi lệnh in lúc {DateTime.Now:HH:mm:ss}";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Không in được: {ex.Message}";
            }
        }
    }
}
