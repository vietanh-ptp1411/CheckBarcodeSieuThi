using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using CheckBarcodeSieuThi.Data;
using CheckBarcodeSieuThi.Models;
using CheckBarcodeSieuThi.Services;

namespace CheckBarcodeSieuThi.ViewModels
{
    /// <summary>
    /// Lịch sử hóa đơn: lọc theo ngày / số hóa đơn, xem chi tiết, in lại.
    /// </summary>
    public class InvoicesViewModel : PageViewModel
    {
        private readonly InvoiceRepository _repo;
        private readonly AppSettings _settings;

        public override string Title => "Hóa đơn";
        public override string Subtitle => "Tra cứu hóa đơn đã bán và doanh thu";
        public override string Icon => "";

        public ObservableCollection<Invoice> Invoices { get; } = [];

        public ICommand SearchCommand { get; }
        public ICommand TodayCommand { get; }
        public ICommand Last7DaysCommand { get; }
        public ICommand ThisMonthCommand { get; }
        public ICommand PrintCommand { get; }

        public InvoicesViewModel(InvoiceRepository repo, AppSettings settings)
        {
            _repo = repo;
            _settings = settings;

            SearchCommand = new RelayCommand(Refresh);
            TodayCommand = new RelayCommand(() => SetRange(DateTime.Today, DateTime.Today));
            Last7DaysCommand = new RelayCommand(() => SetRange(DateTime.Today.AddDays(-6), DateTime.Today));
            ThisMonthCommand = new RelayCommand(() => SetRange(new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1), DateTime.Today));
            PrintCommand = new RelayCommand(Print, () => SelectedDetail != null);
        }

        public override void OnActivated() => Refresh();

        private DateTime? _fromDate = DateTime.Today;
        public DateTime? FromDate { get => _fromDate; set => SetField(ref _fromDate, value); }

        private DateTime? _toDate = DateTime.Today;
        public DateTime? ToDate { get => _toDate; set => SetField(ref _toDate, value); }

        private string _searchText = "";
        public string SearchText { get => _searchText; set => SetField(ref _searchText, value); }

        private int _invoiceCount;
        public int InvoiceCount { get => _invoiceCount; private set => SetField(ref _invoiceCount, value); }

        private decimal _revenue;
        public decimal Revenue { get => _revenue; private set => SetField(ref _revenue, value); }

        private int _itemCount;
        public int ItemCount { get => _itemCount; private set => SetField(ref _itemCount, value); }

        private Invoice? _selectedInvoice;
        public Invoice? SelectedInvoice
        {
            get => _selectedInvoice;
            set
            {
                if (!SetField(ref _selectedInvoice, value)) return;
                LoadDetail();
            }
        }

        private Invoice? _selectedDetail;
        /// <summary>Hóa đơn đang chọn, kèm các dòng hàng.</summary>
        public Invoice? SelectedDetail
        {
            get => _selectedDetail;
            private set { if (SetField(ref _selectedDetail, value)) OnPropertyChanged(nameof(HasSelection)); }
        }

        public bool HasSelection => SelectedDetail != null;

        private FlowDocument? _previewDocument;
        public FlowDocument? PreviewDocument { get => _previewDocument; private set => SetField(ref _previewDocument, value); }

        private void SetRange(DateTime from, DateTime to)
        {
            FromDate = from;
            ToDate = to;
            Refresh();
        }

        public void Refresh()
        {
            var from = FromDate ?? DateTime.Today;
            var to = ToDate ?? DateTime.Today;
            if (to < from) (from, to) = (to, from);

            try
            {
                var selectedId = SelectedInvoice?.Id;
                var list = _repo.Search(from, to, SearchText);
                Invoices.Clear();
                foreach (var inv in list)
                    Invoices.Add(inv);

                InvoiceCount = list.Count;
                Revenue = list.Sum(i => i.Total);
                ItemCount = list.Sum(i => i.TotalQuantity);

                SelectedInvoice = Invoices.FirstOrDefault(i => i.Id == selectedId) ?? Invoices.FirstOrDefault();
                if (SelectedInvoice == null) LoadDetail();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Không đọc được hóa đơn: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void LoadDetail()
        {
            SelectedDetail = SelectedInvoice == null ? null : _repo.GetById(SelectedInvoice.Id);
            PreviewDocument = SelectedDetail == null ? null : ReceiptBuilder.Build(SelectedDetail, _settings);
        }

        private void Print()
        {
            if (SelectedDetail == null) return;
            try
            {
                ReceiptBuilder.Print(SelectedDetail, _settings, showDialog: true);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Không in được hóa đơn: {ex.Message}", "Lỗi in", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
