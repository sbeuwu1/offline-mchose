using System;
using System.IO;
using System.Windows;

namespace MchoseHub
{
    public partial class App : Application
    {
        private static readonly string LogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mchose_debug.log");

        public static void DebugLog(string message)
        {
            try
            {
                File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");
            }
            catch { }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            DebugLog("App.OnStartup: Starting...");

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                DebugLog($"AppDomain UnhandledException: {args.ExceptionObject}");
                MessageBox.Show($"Application Error:\n{args.ExceptionObject}", "Offline Mchose", MessageBoxButton.OK, MessageBoxImage.Error);
            };

            DispatcherUnhandledException += (s, args) =>
            {
                DebugLog($"DispatcherUnhandledException: {args.Exception}");
                MessageBox.Show($"UI Error:\n{args.Exception.Message}\n\n{args.Exception.StackTrace}", "Offline Mchose", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };

            base.OnStartup(e);

            try
            {
                DebugLog("App.OnStartup: Creating MainWindow...");
                var win = new MainWindow();
                DebugLog("App.OnStartup: Calling win.Show()...");
                win.Show();
                DebugLog("App.OnStartup: win.Show() completed successfully.");
            }
            catch (Exception ex)
            {
                DebugLog($"App.OnStartup: Exception creating/showing MainWindow: {ex}");
                MessageBox.Show($"Failed to launch MainWindow:\n{ex.Message}\n\n{ex.StackTrace}", "Offline Mchose", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
