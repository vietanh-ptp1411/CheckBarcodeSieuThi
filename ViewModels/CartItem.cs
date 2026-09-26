using CheckBarcodeSieuThi.Models;

namespace CheckBarcodeSieuThi.ViewModels
{
    /// <summary>Một dòng trong giỏ hàng đang bán.</summary>
    public class CartItem(Product product) : ViewModelBase
    {
        public const int MaxQuantity = 9999;

        public Product Product { get; } = product;
        public string Barcode => Product.Barcode;
        public string Name => Product.Name;
        public decimal UnitPrice => Product.Price;

        private int _index;
        /// <summary>Số thứ tự hiển thị (1, 2, 3...).</summary>
        public int Index { get => _index; set => SetField(ref _index, value); }

        private int _quantity = 1;
        public int Quantity
        {
            get => _quantity;
            set
            {
                var v = Math.Clamp(value, 1, MaxQuantity);
                if (SetField(ref _quantity, v))
                    OnPropertyChanged(nameof(LineTotal));
                else if (v != value)
                    OnPropertyChanged(); // gõ 0 hoặc số âm: trả ô nhập về giá trị hợp lệ
            }
        }

        public decimal LineTotal => UnitPrice * Quantity;
    }
}
