using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace PentabServer;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            File.WriteAllText("crash.log", args.ExceptionObject.ToString());
        };

        DispatcherUnhandledException += (s, args) =>
        {
            File.WriteAllText("crash.log", args.Exception.ToString());
            args.Handled = true;
        };

        base.OnStartup(e);
    }
}

