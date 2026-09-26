namespace CheckBarcodeSieuThi.ViewModels
{
    /// <summary>Trang hiển thị trong vùng nội dung chính, chọn từ menu trái.</summary>
    public abstract class PageViewModel : ViewModelBase
    {
        public abstract string Title { get; }
        public abstract string Subtitle { get; }

        /// <summary>Glyph trong font Segoe Fluent Icons / Segoe MDL2 Assets.</summary>
        public abstract string Icon { get; }

        /// <summary>Gọi mỗi khi người dùng chuyển tới trang này.</summary>
        public virtual void OnActivated() { }
    }
}
