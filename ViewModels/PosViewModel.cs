using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Media;
using System.Windows;
using System.Windows.Input;
using CheckBarcodeSieuThi.Data;
using CheckBarcodeSieuThi.Helpers;
using CheckBarcodeSieuThi.Models;

namespace CheckBarcodeSieuThi.ViewModels
{
    public enum NoticeKind
    {
        None,
        Success,
        Error,
    }

    /// <summary>Nút chọn nhanh số tiền khách đưa.</summary>
    public record QuickCashOption(string Label, decimal Amount);

    /// <summary>
    /// Màn hình bán hàng: quét mã -> giỏ hàng -> thanh toán -> lưu hóa đơn.
    /// </summary>
    public class PosViewModel : PageViewModel
    {
        private readonly ProductRepository _products;
        private readonly InvoiceRepository _invoices;

        public override string Title => "Bán hàng";
        public override string Subtitle => "Quét mã vạch để thêm sản phẩm vào đơn";
        public override string Icon => "";

        /// <summary>View cần focus vào: "Scan" (ô mã vạch) hoặc "Paid" (ô khách đưa).</summary>
        public event Action<string>? FocusRequested;

        /// <summary>Thanh toán xong, hóa đơn đã lưu.</summary>
        public event Action<Invoice>? CheckoutCompleted;

        /// <summary>Thu ngân muốn thêm nhanh mã chưa có vào danh mục sản phẩm.</summary>
        public event Action<string>? AddProductRequested;

        public ObservableCollection<CartItem> Items { get; } = [];

        public IReadOnlyList<KeyValuePair<PaymentMethod, string>> PaymentMethods { get; } =
            Enum.GetValues<PaymentMethod>().Select(m => new KeyValuePair<PaymentMethod, string>(m, m.ToDisplayName())).ToList();

        public ObservableCollection<QuickCashOption> QuickCashOptions { get; } = [];

        public ICommand AddFromScanBoxCommand { get; }
        public ICommand IncreaseCommand { get; }
        public ICommand DecreaseCommand { get; }
        public ICommand RemoveCommand { get; }
        public ICommand IncreaseSelectedCommand { get; }
        public ICommand DecreaseSelectedCommand { get; }
        public ICommand RemoveSelectedCommand { get; }
        public ICommand ClearCartCommand { get; }
        public ICommand CheckoutCommand { get; }
        public ICommand QuickCashCommand { get; }
        public ICommand AddUnknownProductCommand { get; }

        public PosViewModel(ProductRepository products, InvoiceRepository invoices)
        {
            _products = products;
            _invoices = invoices;

            AddFromScanBoxCommand = new RelayCommand(() =>
            {
                var code = ScanText;
                ScanText = "";
                AddBarcode(code);
            });
            IncreaseCommand = new RelayCommand<CartItem>(i => ChangeQuantity(i, +1));
            DecreaseCommand = new RelayCommand<CartItem>(i => ChangeQuantity(i, -1));
            RemoveCommand = new RelayCommand<CartItem>(RemoveItem);
            IncreaseSelectedCommand = new RelayCommand(() => ChangeQuantity(SelectedItem, +1), () => SelectedItem != null);
            DecreaseSelectedCommand = new RelayCommand(() => ChangeQuantity(SelectedItem, -1), () => SelectedItem != null);
            RemoveSelectedCommand = new RelayCommand(() => RemoveItem(SelectedItem), () => SelectedItem != null);
            ClearCartCommand = new RelayCommand(ConfirmClearCart, () => HasItems);
            CheckoutCommand = new RelayCommand(Checkout, () => HasItems);
            QuickCashCommand = new RelayCommand<QuickCashOption>(o => CustomerPaidText = Money.Format(o.Amount));
            AddUnknownProductCommand = new RelayCommand(() =>
            {
                if (UnknownBarcode is { } code) AddProductRequested?.Invoke(code);
            });

            Items.CollectionChanged += (_, _) => Recalculate();
        }

        public override void OnActivated() => FocusRequested?.Invoke("Scan");

        // ==================================================================
        // Giỏ hàng
        // ==================================================================

        private string _scanText = "";
        public string ScanText { get => _scanText; set => SetField(ref _scanText, value); }

        private CartItem? _selectedItem;
        public CartItem? SelectedItem { get => _selectedItem; set => SetField(ref _selectedItem, value); }

        public bool HasItems => Items.Count > 0;

        /// <summary>Thêm sản phẩm theo mã vạch (từ đầu đọc hoặc gõ tay). Quét lại mã đã có thì tăng số lượng.</summary>
        public void AddBarcode(string code)
        {
            code = code.Trim();
            if (code.Length == 0) return;

            Product? product;
            try
            {
                product = _products.GetByBarcode(code);
            }
            catch (Exception ex)
            {
                ShowNotice($"Lỗi đọc dữ liệu sản phẩm: {ex.Message}", NoticeKind.Error);
                return;
            }

            if (product == null)
            {
                UnknownBarcode = code;
                ShowNotice($"Không tìm thấy sản phẩm có mã {code}", NoticeKind.Error);
                SystemSounds.Exclamation.Play();
                return;
            }

            UnknownBarcode = null;
            var item = Items.FirstOrDefault(i => i.Product.Id == product.Id);
            if (item != null)
            {
                item.Quantity++;
            }
            else
            {
                item = new CartItem(product);
                item.PropertyChanged += OnItemPropertyChanged;
                Items.Add(item);
                Renumber();
            }

            SelectedItem = item;
            ShowNotice($"{product.Name}  ×{item.Quantity}  ·  {Money.FormatVnd(product.Price)}", NoticeKind.Success);
            CommandManager.InvalidateRequerySuggested();
        }

        private void ChangeQuantity(CartItem? item, int delta)
        {
            if (item == null) return;
            if (item.Quantity + delta < 1)
            {
                RemoveItem(item);
                return;
            }
            item.Quantity += delta;
            SelectedItem = item;
        }

        private void RemoveItem(CartItem? item)
        {
            if (item == null) return;
            var index = Items.IndexOf(item);
            item.PropertyChanged -= OnItemPropertyChanged;
            Items.Remove(item);
            Renumber();
            SelectedItem = Items.Count == 0 ? null : Items[Math.Min(index, Items.Count - 1)];
            FocusRequested?.Invoke("Scan");
        }

        private void ConfirmClearCart()
        {
            if (!HasItems) return;
            var answer = MessageBox.Show(
                $"Hủy đơn hàng hiện tại ({Items.Count} mặt hàng, {Money.FormatVnd(Total)})?",
                "Hủy đơn", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes) return;

            ResetOrder();
            ShowNotice("Đã hủy đơn hàng", NoticeKind.None);
            FocusRequested?.Invoke("Scan");
        }

        private void ResetOrder()
        {
            foreach (var item in Items)
                item.PropertyChanged -= OnItemPropertyChanged;
            Items.Clear();
            SelectedItem = null;
            DiscountText = "";
            CustomerPaidText = "";
            SelectedPaymentMethod = PaymentMethod.Cash;
            UnknownBarcode = null;
        }

        private void Renumber()
        {
            for (int i = 0; i < Items.Count; i++)
                Items[i].Index = i + 1;
        }

        private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(CartItem.LineTotal))
                Recalculate();
        }

        // ==================================================================
        // Thông báo trên đầu giỏ hàng
        // ==================================================================

        private string _noticeText = "";
        public string NoticeText { get => _noticeText; private set => SetField(ref _noticeText, value); }

        private NoticeKind _noticeKind;
        public NoticeKind NoticeKind { get => _noticeKind; private set => SetField(ref _noticeKind, value); }

        private string? _unknownBarcode;
        /// <summary>Mã vừa quét nhưng chưa có trong danh mục (hiện nút "Thêm vào danh mục").</summary>
        public string? UnknownBarcode
        {
            get => _unknownBarcode;
            private set { if (SetField(ref _unknownBarcode, value)) OnPropertyChanged(nameof(HasUnknownBarcode)); }
        }

        public bool HasUnknownBarcode => UnknownBarcode != null;

        private void ShowNotice(string text, NoticeKind kind)
        {
            NoticeText = text;
            NoticeKind = kind;
        }

        // ==================================================================
        // Tổng tiền & thanh toán
        // ==================================================================

        public int TotalQuantity => Items.Sum(i => i.Quantity);
        public decimal Subtotal => Items.Sum(i => i.LineTotal);

        private string _discountText = "";
        public string DiscountText
        {
            get => _discountText;
            set { if (SetField(ref _discountText, value)) Recalculate(); }
        }

        /// <summary>Giảm giá hợp lệ: ô trống hoặc là số tiền không vượt quá tạm tính.</summary>
        public bool IsDiscountValid =>
            string.IsNullOrWhiteSpace(DiscountText) || (Money.TryParse(DiscountText, out var d) && d <= Subtotal);

        public decimal Discount =>
            Money.TryParse(DiscountText, out var d) ? Math.Min(d, Subtotal) : 0;

        public decimal Total => Subtotal - Discount;

        private PaymentMethod _selectedPaymentMethod = PaymentMethod.Cash;
        public PaymentMethod SelectedPaymentMethod
        {
            get => _selectedPaymentMethod;
            set
            {
                if (!SetField(ref _selectedPaymentMethod, value)) return;
                OnPropertyChanged(nameof(IsCash));
                Recalculate();
            }
        }

        public bool IsCash => SelectedPaymentMethod == PaymentMethod.Cash;

        private string _customerPaidText = "";
        public string CustomerPaidText
        {
            get => _customerPaidText;
            set { if (SetField(ref _customerPaidText, value)) Recalculate(); }
        }

        /// <summary>Khách đưa; ô trống nghĩa là đưa vừa đủ.</summary>
        private decimal? CustomerPaid =>
            string.IsNullOrWhiteSpace(CustomerPaidText) ? Total
            : Money.TryParse(CustomerPaidText, out var v) ? v : null;

        public bool IsPaidEnough => !IsCash || (CustomerPaid is { } p && p >= Total);

        /// <summary>Tiền thừa (dương) hoặc còn thiếu (âm).</summary>
        public decimal ChangeDue => IsCash && CustomerPaid is { } p ? p - Total : 0;
        public decimal ChangeDueAbs => Math.Abs(ChangeDue);

        private void Recalculate()
        {
            OnPropertyChanged(nameof(HasItems));
            OnPropertyChanged(nameof(TotalQuantity));
            OnPropertyChanged(nameof(Subtotal));
            OnPropertyChanged(nameof(Discount));
            OnPropertyChanged(nameof(IsDiscountValid));
            OnPropertyChanged(nameof(Total));
            OnPropertyChanged(nameof(IsPaidEnough));
            OnPropertyChanged(nameof(ChangeDue));
            OnPropertyChanged(nameof(ChangeDueAbs));
            UpdateQuickCash();
        }

        /// <summary>
        /// "Đủ tiền" + 3 mệnh giá khách hay đưa: làm tròn lên và các tờ tiền lớn.
        /// </summary>
        private void UpdateQuickCash()
        {
            var total = Total;
            var list = new List<QuickCashOption>();
            if (total > 0)
            {
                var amounts = new SortedSet<decimal>();
                foreach (var step in new decimal[] { 10_000, 50_000, 100_000, 500_000 })
                {
                    var rounded = Math.Ceiling(total / step) * step;
                    if (rounded > total) amounts.Add(rounded);
                }
                foreach (var note in new decimal[] { 100_000, 200_000, 500_000 })
                    if (note > total) amounts.Add(note);

                list.Add(new QuickCashOption("Đủ tiền", total));
                list.AddRange(amounts.Take(3).Select(a => new QuickCashOption(Money.Format(a), a)));
            }

            if (list.SequenceEqual(QuickCashOptions)) return;
            QuickCashOptions.Clear();
            foreach (var o in list) QuickCashOptions.Add(o);
        }

        private void Checkout()
        {
            if (!HasItems)
            {
                ShowNotice("Đơn hàng chưa có sản phẩm", NoticeKind.Error);
                return;
            }
            if (!IsDiscountValid)
            {
                ShowNotice("Giảm giá không hợp lệ (phải là số tiền không lớn hơn tạm tính)", NoticeKind.Error);
                return;
            }

            decimal paid = Total;
            if (IsCash)
            {
                if (CustomerPaid is not { } p)
                {
                    ShowNotice("Số tiền khách đưa không hợp lệ", NoticeKind.Error);
                    FocusRequested?.Invoke("Paid");
                    return;
                }
                if (p < Total)
                {
                    ShowNotice($"Khách đưa chưa đủ, còn thiếu {Money.FormatVnd(Total - p)}", NoticeKind.Error);
                    FocusRequested?.Invoke("Paid");
                    return;
                }
                paid = p;
            }

            var invoice = new Invoice
            {
                CreatedAt = DateTime.Now,
                TotalQuantity = TotalQuantity,
                Subtotal = Subtotal,
                Discount = Discount,
                Total = Total,
                PaymentMethod = SelectedPaymentMethod,
                CustomerPaid = paid,
                ChangeDue = paid - Total,
                Items = Items.Select(i => new InvoiceItem
                {
                    ProductId = i.Product.Id,
                    Barcode = i.Barcode,
                    Name = i.Name,
                    UnitPrice = i.UnitPrice,
                    Quantity = i.Quantity,
                    LineTotal = i.LineTotal,
                }).ToList(),
            };

            try
            {
                _invoices.Create(invoice);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Không lưu được hóa đơn: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            ResetOrder();
            ShowNotice($"Đã thanh toán hóa đơn {invoice.Number}  ·  {Money.FormatVnd(invoice.Total)}", NoticeKind.Success);
            CheckoutCompleted?.Invoke(invoice);
            FocusRequested?.Invoke("Scan");
        }
    }
}
