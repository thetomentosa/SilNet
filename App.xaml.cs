using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;

namespace SilNet;
public partial class App : Application
{
    private Mutex? instance;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, error) => { AppLog.Write("Fatal", error.Exception.ToString()); error.Handled = true; MessageBox.Show("SilNet beklenmeyen bir sorunla karşılaştı. Günlük klasörü:\n" + AppLog.DirectoryPath, "SilNet", MessageBoxButton.OK, MessageBoxImage.Error); Shutdown(1); };
        TaskScheduler.UnobservedTaskException += (_, error) => { AppLog.Write("Task", error.Exception.ToString()); error.SetObserved(); };
        try
        {
            if (e.Args.Length == 1 && e.Args[0] == "--test-child") { await Task.Delay(60000); Shutdown(); return; }
            if (e.Args.Length == 3 && e.Args[0] == "--worker" && int.TryParse(e.Args[2], out int parentId)) { await EngineWorker.RunAsync(e.Args[1], parentId); Shutdown(); return; }
            if (e.Args.Length == 2 && e.Args[0] == "--check-update")
            {
                var release = await Updates.CheckAsync();
                await File.WriteAllTextAsync(e.Args[1], System.Text.Json.JsonSerializer.Serialize(new { current = Updates.CurrentVersion, release }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                Shutdown(); return;
            }
            if (e.Args.Length == 2 && e.Args[0] is "--self-test" or "--elevated-test")
            {
                try { if (e.Args[0] == "--self-test") await SmokeTests.RunAsync(e.Args[1]); else await SmokeTests.ElevatedAsync(e.Args[1]); Shutdown(0); }
                catch (Exception ex) { AppLog.Write("Test", ex.ToString()); File.WriteAllText(e.Args[1], ex.ToString()); Shutdown(1); }
                return;
            }
            bool preview = e.Args.Length >= 2 && e.Args[0] == "--preview";
            var window = new MainWindow(preview); MainWindow = window;
            if (preview)
            {
                if (e.Args.Length >= 3) window.ShowPage(e.Args[2]);
                if (e.Args.Length >= 4 && e.Args[3] == "compact") { window.Width = 920; window.Height = 740; }
                window.Left = -10000; window.Top = -10000; window.ShowInTaskbar = false; window.Show();
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle); window.UpdateLayout();
                var content = (FrameworkElement)window.Content;
                var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var stream = File.Create(e.Args[1])) encoder.Save(stream);
                window.Close(); return;
            }
            instance = new Mutex(true, "SilNet.UI", out bool firstInstance);
            if (!firstInstance) { MessageBox.Show("SilNet zaten açık. Görev çubuğundaki SilNet penceresini kullanabilirsin.", "SilNet"); Shutdown(); return; }
            window.Show();
        }
        catch (Exception ex) { AppLog.Write("Startup", ex.ToString()); MessageBox.Show("SilNet açılamadı: " + ex.Message + "\n\nGünlük: " + AppLog.DirectoryPath, "SilNet"); Shutdown(1); }
    }
    protected override void OnExit(ExitEventArgs e) { instance?.Dispose(); base.OnExit(e); }
}
