using System.Configuration;
using System.Data;
using System.Globalization;
using System.Windows;
using System.Windows.Markup;

namespace CheckBarcodeSieuThi
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Hiển thị số/tiền theo kiểu Việt Nam (15.000 ₫) trong toàn bộ binding
            var vi = CultureInfo.GetCultureInfo("vi-VN");
            CultureInfo.DefaultThreadCurrentCulture = vi;
            CultureInfo.DefaultThreadCurrentUICulture = vi;
            // DatePicker lấy định dạng ngày từ CurrentCulture của thread giao diện
            CultureInfo.CurrentCulture = vi;
            CultureInfo.CurrentUICulture = vi;
            FrameworkElement.LanguageProperty.OverrideMetadata(
                typeof(FrameworkElement),
                new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(vi.IetfLanguageTag)));

            DispatcherUnhandledException += (_, args) =>
            {
                MessageBox.Show(args.Exception.Message, "Lỗi không mong muốn", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };

            base.OnStartup(e);
        }
    }

}
