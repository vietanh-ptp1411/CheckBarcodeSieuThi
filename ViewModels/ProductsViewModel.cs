using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using CheckBarcodeSieuThi.Data;
using CheckBarcodeSieuThi.Helpers;
using CheckBarcodeSieuThi.Models;

namespace CheckBarcodeSieuThi.ViewModels
{
    /// <summary>
    /// Danh mục sản phẩm: thêm / sửa / xóa / tìm kiếm.
    /// </summary>
    public class ProductsViewModel : PageViewModel
    {
        private readonly ProductRepository _repo;

        /// <summary>Barcode do màn hình bán hàng chuyển sang để thêm nhanh; thêm xong sẽ quay lại bán hàng.</summary>
        private string? _pendingPosBarcode;

        public override string Title => "Sản phẩm";
        public override string Subtitle => "Quản lý danh mục hàng hóa và giá bán";
        public override string Icon => "";

        /// <summary>View cần focus vào ô: "Barcode", "Name" hoặc "Price".</summary>
        public event Action<string>? FocusRequested;

        /// <summary>Đã thêm sản phẩm mà màn hình bán hàng yêu cầu.</summary>
        public event Action<Product>? PendingPosProductAdded;

        public ObservableCollection<Product> Products { get; } = [];
        public ICollectionView ProductsView { get; }

        public ICommand AddCommand { get; }
        public ICommand UpdateCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand ClearFormCommand { get; }
        public ICommand LookupCommand { get; }

        public ProductsViewModel(ProductRepository repo)
        {
            _repo = repo;

            ProductsView = CollectionViewSource.GetDefaultView(Products);
            ProductsView.Filter = FilterProduct;
            ProductsView.SortDescriptions.Add(new SortDescription(nameof(Product.Name), ListSortDirection.Ascending));

            AddCommand = new RelayCommand(AddProduct, () => !string.IsNullOrWhiteSpace(EditBarcode) && !string.IsNullOrWhiteSpace(EditName));
            UpdateCommand = new RelayCommand(UpdateProduct, () => SelectedProduct != null);
            DeleteCommand = new RelayCommand(DeleteProduct, () => SelectedProduct != null);
            ClearFormCommand = new RelayCommand(ClearForm);
            LookupCommand = new RelayCommand(() => HandleScannedBarcode(EditBarcode), () => !string.IsNullOrWhiteSpace(EditBarcode));

            LoadProducts();
        }

        public override void OnActivated()
        {
            var selectedId = SelectedProduct?.Id;
            LoadProducts();
            if (selectedId is { } id && Products.FirstOrDefault(p => p.Id == id) is { } again)
                SelectProduct(again);
            FocusRequested?.Invoke(string.IsNullOrEmpty(EditBarcode) ? "Barcode" : "Name");
        }

        // ==================================================================

        private string _searchText = "";
        public string SearchText
        {
            get => _searchText;
            set { if (SetField(ref _searchText, value)) ProductsView.Refresh(); }
        }

        private Product? _selectedProduct;
        public Product? SelectedProduct
        {
            get => _selectedProduct;
            set
            {
                if (!SetField(ref _selectedProduct, value)) return;
                OnPropertyChanged(nameof(IsEditing));
                if (value == null) return;
                EditBarcode = value.Barcode;
                EditName = value.Name;
                EditPriceText = Money.Format(value.Price);
            }
        }

        public bool IsEditing => SelectedProduct != null;

        private string _editBarcode = "";
        public string EditBarcode { get => _editBarcode; set => SetField(ref _editBarcode, value); }

        private string _editName = "";
        public string EditName { get => _editName; set => SetField(ref _editName, value); }

        private string _editPriceText = "";
        public string EditPriceText { get => _editPriceText; set => SetField(ref _editPriceText, value); }

        private string _formMessage = "";
        public string FormMessage { get => _formMessage; set => SetField(ref _formMessage, value); }

        private bool _formMessageIsError;
        public bool FormMessageIsError { get => _formMessageIsError; set => SetField(ref _formMessageIsError, value); }

        public string ProductCountText => $"{Products.Count} sản phẩm";

        /// <summary>Màn hình bán hàng quét phải mã chưa có: mở form thêm mới với barcode điền sẵn.</summary>
        public void PrepareNewFromPos(string barcode)
        {
            ClearForm();
            _pendingPosBarcode = barcode;
            EditBarcode = barcode;
            SetMessage($"Mã {barcode} chưa có. Nhập tên, giá rồi bấm \"Thêm mới\" để quay lại bán hàng.", false);
            FocusRequested?.Invoke("Name");
        }

        /// <summary>Quét mã khi đang ở trang Sản phẩm: có rồi thì chọn để sửa, chưa có thì điền sẵn để thêm.</summary>
        public void HandleScannedBarcode(string code)
        {
            code = code.Trim();
            if (code.Length == 0) return;

            var product = Products.FirstOrDefault(p => p.Barcode == code);
            if (product != null)
            {
                SelectProduct(product);
                SetMessage($"Đang sửa: {product.Name}", false);
                FocusRequested?.Invoke("Price");
            }
            else
            {
                SelectedProduct = null;
                EditBarcode = code;
                EditName = "";
                EditPriceText = "";
                SetMessage($"Mã {code} chưa có. Nhập tên, giá rồi bấm \"Thêm mới\".", false);
                FocusRequested?.Invoke("Name");
            }
            CommandManager.InvalidateRequerySuggested();
        }

        private bool FilterProduct(object obj)
        {
            if (string.IsNullOrWhiteSpace(SearchText)) return true;
            var p = (Product)obj;
            var s = SearchText.Trim();
            return p.Barcode.Contains(s, StringComparison.OrdinalIgnoreCase)
                || p.Name.Contains(s, StringComparison.CurrentCultureIgnoreCase);
        }

        private void LoadProducts()
        {
            try
            {
                Products.Clear();
                foreach (var p in _repo.GetAll())
                    Products.Add(p);
                OnPropertyChanged(nameof(ProductCountText));
            }
            catch (Exception ex)
            {
                ShowError($"Không đọc được dữ liệu sản phẩm: {ex.Message}");
            }
        }

        private bool TryReadForm(out Product product)
        {
            product = new Product
            {
                Barcode = EditBarcode.Trim(),
                Name = EditName.Trim(),
            };

            if (product.Barcode.Length == 0) { SetMessage("Chưa nhập barcode.", true); FocusRequested?.Invoke("Barcode"); return false; }
            if (product.Name.Length == 0) { SetMessage("Chưa nhập tên sản phẩm.", true); FocusRequested?.Invoke("Name"); return false; }
            if (!Money.TryParse(EditPriceText, out var price))
            {
                SetMessage("Giá không hợp lệ. Nhập số tiền (đồng), ví dụ 15000 hoặc 15.000.", true);
                FocusRequested?.Invoke("Price");
                return false;
            }

            product.Price = price;
            return true;
        }

        private void AddProduct()
        {
            if (!TryReadForm(out var product)) return;

            try
            {
                _repo.Add(product);
                Products.Add(product);
                OnPropertyChanged(nameof(ProductCountText));
                SelectProduct(product);
                SetMessage($"Đã thêm: {product.Name}", false);

                if (_pendingPosBarcode == product.Barcode)
                {
                    _pendingPosBarcode = null;
                    PendingPosProductAdded?.Invoke(product);
                }
            }
            catch (DuplicateBarcodeException)
            {
                SetMessage($"Barcode {product.Barcode} đã có trong danh mục. Chọn sản phẩm đó để sửa.", true);
                var existing = Products.FirstOrDefault(p => p.Barcode == product.Barcode);
                if (existing != null) SelectProduct(existing);
            }
            catch (Exception ex)
            {
                ShowError($"Không thêm được sản phẩm: {ex.Message}");
            }
        }

        private void UpdateProduct()
        {
            if (SelectedProduct is not { } selected) return;
            if (!TryReadForm(out var product)) return;
            product.Id = selected.Id;

            try
            {
                if (!_repo.Update(product))
                {
                    SetMessage("Sản phẩm không còn trong cơ sở dữ liệu, đã tải lại danh sách.", true);
                    LoadProducts();
                    return;
                }

                var index = Products.IndexOf(selected);
                Products[index] = product;
                SelectProduct(product);
                SetMessage($"Đã lưu: {product.Name}", false);
            }
            catch (DuplicateBarcodeException)
            {
                SetMessage($"Barcode {product.Barcode} đang được dùng cho sản phẩm khác.", true);
            }
            catch (Exception ex)
            {
                ShowError($"Không cập nhật được sản phẩm: {ex.Message}");
            }
        }

        private void DeleteProduct()
        {
            if (SelectedProduct is not { } selected) return;

            var answer = MessageBox.Show(
                $"Xóa sản phẩm \"{selected.Name}\" (barcode {selected.Barcode})?\nHóa đơn cũ vẫn giữ nguyên thông tin sản phẩm này.",
                "Xác nhận xóa", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes) return;

            try
            {
                _repo.Delete(selected.Id);
                Products.Remove(selected);
                OnPropertyChanged(nameof(ProductCountText));
                ClearForm();
                SetMessage($"Đã xóa: {selected.Name}", false);
            }
            catch (Exception ex)
            {
                ShowError($"Không xóa được sản phẩm: {ex.Message}");
            }
        }

        private void ClearForm()
        {
            SelectedProduct = null;
            _pendingPosBarcode = null;
            EditBarcode = "";
            EditName = "";
            EditPriceText = "";
            SetMessage("", false);
            FocusRequested?.Invoke("Barcode");
        }

        private void SelectProduct(Product product)
        {
            // Sản phẩm bị bộ lọc tìm kiếm ẩn thì DataGrid không chọn được -> bỏ lọc
            if (!ProductsView.Contains(product))
                SearchText = "";
            _selectedProduct = null; // luôn nạp lại form kể cả khi chọn lại đúng sản phẩm đang chọn
            SelectedProduct = product;
        }

        private void SetMessage(string message, bool isError)
        {
            FormMessage = message;
            FormMessageIsError = isError;
        }

        private static void ShowError(string message) =>
            MessageBox.Show(message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
