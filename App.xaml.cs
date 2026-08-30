using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace PentabServer;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            try
            {
                File.WriteAllText(
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"),
                    args.ExceptionObject?.ToString() ?? "Unknown exception"
                );
            }
            catch { }
        };

        DispatcherUnhandledException += (s, args) =>
        {
            try
            {
                File.WriteAllText(
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"),
                    args.Exception?.ToString() ?? "Unknown dispatcher exception"
                );
            }
            catch { }
            args.Handled = true;
        };

        var mainWindow = new MainWindow();
        mainWindow.Show();
        mainWindow.Activate();
        mainWindow.Focus();
    }
}
