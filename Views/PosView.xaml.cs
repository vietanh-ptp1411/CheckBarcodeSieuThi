using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CheckBarcodeSieuThi.ViewModels;

namespace CheckBarcodeSieuThi.Views
{
    public partial class PosView : UserControl
    {
        private PosViewModel? _vm;

        public PosView()
        {
            InitializeComponent();

            // F2 / F4 chuyển focus nên xử lý ở view
            InputBindings.Add(new KeyBinding(new RelayCommand(() => FocusBox(ScanBox)), Key.F2, ModifierKeys.None));
            InputBindings.Add(new KeyBinding(new RelayCommand(() => FocusBox(PaidBox)), Key.F4, ModifierKeys.None));

            Loaded += (_, _) =>
            {
                Attach(DataContext as PosViewModel);
                FocusBox(ScanBox);
            };
            Unloaded += (_, _) => Attach(null);
        }

        private void Attach(PosViewModel? vm)
        {
            if (_vm != null) _vm.FocusRequested -= OnFocusRequested;
            _vm = vm;
            if (_vm != null) _vm.FocusRequested += OnFocusRequested;
        }

        private void OnFocusRequested(string target) => FocusBox(target == "Paid" ? PaidBox : ScanBox);

        private void FocusBox(TextBox box)
        {
            // Đợi layout xong (ô có thể vừa được hiện ra) rồi mới focus
            Dispatcher.BeginInvoke(() =>
            {
                if (!box.IsVisible) return;
                box.Focus();
                box.SelectAll();
            }, DispatcherPriority.Input);
        }

        private void CartList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CartList.SelectedItem != null)
                CartList.ScrollIntoView(CartList.SelectedItem);
        }
    }
}
