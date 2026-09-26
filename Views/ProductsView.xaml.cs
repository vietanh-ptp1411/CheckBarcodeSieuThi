using System.Windows.Controls;
using System.Windows.Threading;
using CheckBarcodeSieuThi.ViewModels;

namespace CheckBarcodeSieuThi.Views
{
    public partial class ProductsView : UserControl
    {
        private ProductsViewModel? _vm;

        public ProductsView()
        {
            InitializeComponent();

            Loaded += (_, _) =>
            {
                Attach(DataContext as ProductsViewModel);
                // Vừa chuyển từ màn hình bán hàng sang với barcode điền sẵn thì nhập tên luôn
                OnFocusRequested(_vm is { EditBarcode.Length: > 0, EditName.Length: 0 } ? "Name" : "Barcode");
            };
            Unloaded += (_, _) => Attach(null);
        }

        private void Attach(ProductsViewModel? vm)
        {
            if (_vm != null) _vm.FocusRequested -= OnFocusRequested;
            _vm = vm;
            if (_vm != null) _vm.FocusRequested += OnFocusRequested;
        }

        private void OnFocusRequested(string field)
        {
            TextBox box = field switch
            {
                "Name" => NameBox,
                "Price" => PriceBox,
                _ => BarcodeBox,
            };
            Dispatcher.BeginInvoke(() =>
            {
                box.Focus();
                box.SelectAll();
            }, DispatcherPriority.Input);
        }

        private void ProductGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ProductGrid.SelectedItem != null)
                ProductGrid.ScrollIntoView(ProductGrid.SelectedItem);
        }
    }
}
