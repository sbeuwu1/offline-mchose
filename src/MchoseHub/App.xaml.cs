using System;
using System.IO;
using System.Windows;
using MchoseHub.Localization;

namespace MchoseHub
{
    public partial class App : Application
    {
        // Logs live in %LOCALAPPDATA% so they survive single-file extraction and stay writable
        // when the app is installed under Program Files.
        private static readonly string LogDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OfflineMchose",
            "logs");

        private static readonly string LogPath = Path.Combine(LogDirectory, "debug.log");
        private const long MaxLogBytes = 2L * 1024 * 1024; // rotate at 2 MB

        private static readonly object LogLock = new();
        private static StreamWriter? _logWriter;
        private static long _logBytes;
        private static bool _logUnavailable;

        public static string LogFilePath => LogPath;

        public static void DebugLog(string message)
        {
            lock (LogLock)
            {
                if (_logUnavailable) return;

                try
                {
                    _logWriter ??= OpenWriter();
                    string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
                    _logWriter.WriteLine(line);
                    _logBytes += line.Length + Environment.NewLine.Length;
                    if (_logBytes >= MaxLogBytes)
                    {
                        Roll();
                    }
                }
                catch
                {
                    // Diagnostics must never take the app down (read-only profile, disk full, ...).
                    _logUnavailable = true;
                    try { _logWriter?.Dispose(); } catch { }
                    _logWriter = null;
                }
            }
        }

        public static void FlushLog()
        {
            lock (LogLock)
            {
                try { _logWriter?.Flush(); } catch { }
            }
        }

        private static StreamWriter OpenWriter()
        {
            Directory.CreateDirectory(LogDirectory);
            _logBytes = File.Exists(LogPath) ? new FileInfo(LogPath).Length : 0;
            var stream = new FileStream(LogPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            return new StreamWriter(stream) { AutoFlush = true };
        }

        private static void Roll()
        {
            string previous = LogPath + ".1";
            try { _logWriter?.Dispose(); } catch { }
            _logWriter = null;

            try
            {
                if (File.Exists(previous)) File.Delete(previous);
                File.Move(LogPath, previous);
            }
            catch
            {
                // If rotation fails, keep appending to the current file instead of losing logs.
            }

            _logBytes = 0;
            _logWriter = OpenWriter();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            // Pick the UI language before any window is parsed: XAML resolves loc:Tr at load time.
            Loc.Initialize(e.Args);
            DebugLog($"App.OnStartup: Starting (log file: {LogPath}, language: {Loc.Language})...");

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                DebugLog($"AppDomain UnhandledException: {args.ExceptionObject}");
                try
                {
                    MessageBox.Show($"Application Error:\n{args.ExceptionObject}", "Offline Mchose", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                catch { }
            };

            DispatcherUnhandledException += (s, args) =>
            {
                DebugLog($"DispatcherUnhandledException: {args.Exception}");
                try
                {
                    MessageBox.Show($"UI Error:\n{args.Exception.Message}\n\n{args.Exception.StackTrace}", "Offline Mchose", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                catch { }
                args.Handled = true;
            };

            Exit += (s, args) =>
            {
                DebugLog("App.OnExit: shutting down.");
                FlushLog();
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
