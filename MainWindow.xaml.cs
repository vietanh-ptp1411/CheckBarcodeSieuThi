using System.Windows;
using CheckBarcodeSieuThi.Models;
using CheckBarcodeSieuThi.ViewModels;
using CheckBarcodeSieuThi.Views;

namespace CheckBarcodeSieuThi
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _vm;

        public MainWindow()
        {
            InitializeComponent();

            _vm = new MainViewModel();
            _vm.ShowReceiptRequested += ShowReceipt;
            DataContext = _vm;

            Closed += (_, _) => _vm.Dispose();
        }

        private void ShowReceipt(Invoice invoice, bool isNewSale)
        {
            var window = new ReceiptWindow(invoice, _vm.AppSettings, isNewSale) { Owner = this };
            window.ShowDialog();
        }
    }
}
