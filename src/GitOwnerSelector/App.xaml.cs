using System.Windows;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace GitOwnerSelector;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Length == 2 && e.Args[0] == "--render-preview")
        {
            var window = new MainWindow();
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -20000;
            window.Top = -20000;
            window.ShowInTaskbar = false;
            window.Show();
            Dispatcher.InvokeAsync(() =>
            {
                window.UpdateLayout();
                var content = (FrameworkElement)window;
                var image = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                image.Render(content);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                using (var file = File.Create(Path.GetFullPath(e.Args[1]))) encoder.Save(file);
                Shutdown();
            }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            return;
        }
        base.OnStartup(e);
        new MainWindow().Show();
    }
}
