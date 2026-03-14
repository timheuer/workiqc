using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using WinRT.Interop;

namespace WorkIQC.App
{
    public sealed partial class MainWindow : Window
    {
        private static readonly Windows.Graphics.SizeInt32 DefaultWindowSize = new(1280, 800);

        public MainWindow()
        {
            InitializeComponent();
            Title = "WorkICQ";
            ExtendsContentIntoTitleBar = true;

            // Apply Mica backdrop for the modern Fluent aesthetic
            if (MicaController.IsSupported())
            {
                SystemBackdrop = new MicaBackdrop { Kind = MicaKind.Base };
            }
            else if (DesktopAcrylicController.IsSupported())
            {
                // Fallback to Acrylic on older systems
                SystemBackdrop = new DesktopAcrylicBackdrop();
            }

            // Set minimum window size for good UX
            var hWnd = WindowNative.GetWindowHandle(this);
            var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            if (appWindow?.Presenter is OverlappedPresenter presenter)
            {
                presenter.SetBorderAndTitleBar(true, true);
            }

            if (appWindow is not null)
            {
                appWindow.Resize(DefaultWindowSize);
                CenterOnDisplay(appWindow, windowId);
            }
        }

        public void SetContent(UIElement content)
        {
            RootGrid.Children.Clear();
            RootGrid.Children.Add(content);

            if (content is FrameworkElement frameworkElement
                && frameworkElement.FindName("TitleBarDragRegion") is UIElement dragRegion)
            {
                SetTitleBar(dragRegion);
            }
        }

        private static void CenterOnDisplay(AppWindow appWindow, WindowId windowId)
        {
            var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
            var workArea = displayArea.WorkArea;
            var centeredX = workArea.X + Math.Max(0, (workArea.Width - DefaultWindowSize.Width) / 2);
            var centeredY = workArea.Y + Math.Max(0, (workArea.Height - DefaultWindowSize.Height) / 2);
            appWindow.Move(new Windows.Graphics.PointInt32(centeredX, centeredY));
        }
    }
}
